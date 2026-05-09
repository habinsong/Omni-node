namespace OmniNode.Middleware;

public sealed class TelegramUpdateLoop
{
    private const int TelegramOutboxFlushBatchSize = 4;
    private readonly TelegramClient _telegramClient;
    private readonly ICommandExecutionService _commandService;
    private readonly AppConfig _config;
    private readonly TelegramPollingStateStore _stateStore;
    private readonly FileTelegramReplyOutboxStore _replyOutboxStore;
    private readonly Dictionary<string, CommandWindow> _commandWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _rateLock = new();
    private long _offset;

    public TelegramUpdateLoop(
        TelegramClient telegramClient,
        ICommandExecutionService commandService,
        AppConfig config,
        TelegramPollingStateStore stateStore,
        FileTelegramReplyOutboxStore replyOutboxStore
    )
    {
        _telegramClient = telegramClient;
        _commandService = commandService;
        _config = config;
        _stateStore = stateStore;
        _replyOutboxStore = replyOutboxStore;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var lease = _stateStore.TryAcquireLease();
        if (lease == null)
        {
            Console.WriteLine("[telegram] polling skipped: another middleware instance already owns the loop.");
            return;
        }

        _offset = Math.Max(0, _stateStore.LoadNextOffset());
        while (!cancellationToken.IsCancellationRequested)
        {
            await FlushReplyOutboxAsync(cancellationToken);

            IReadOnlyList<TelegramUpdate> updates;

            try
            {
                updates = await _telegramClient.GetUpdatesAsync(_offset, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[telegram] polling error: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            foreach (var update in updates)
            {
                if (update.UpdateId >= _offset)
                {
                    _offset = update.UpdateId + 1;
                }

                try
                {
                    var hasText = !string.IsNullOrWhiteSpace(update.Text);
                    var hasAttachments = update.Attachments != null && update.Attachments.Count > 0;
                    var hasCallback = !string.IsNullOrWhiteSpace(update.CallbackQueryId)
                                      && !string.IsNullOrWhiteSpace(update.CallbackData);
                    if (!hasText && !hasAttachments && !hasCallback)
                    {
                        continue;
                    }

                    if (!IsAuthorizedUpdate(update))
                    {
                        if (hasCallback)
                        {
                            // 비허용 사용자라도 dangling spinner 방지를 위해 ack만 보낸다.
                            await _telegramClient.AnswerCallbackQueryAsync(update.CallbackQueryId!, null, cancellationToken);
                        }
                        continue;
                    }

                    if (hasCallback)
                    {
                        // callback_data 자체를 사용자 입력처럼 취급해 명령 흐름으로 흘려보낸다.
                        await _telegramClient.AnswerCallbackQueryAsync(update.CallbackQueryId!, "처리 중…", cancellationToken);
                        var callbackInput = update.CallbackData!.Trim();
                        try
                        {
                            var callbackResult = await _commandService.ExecuteAsync(
                                callbackInput,
                                "telegram",
                                cancellationToken,
                                Array.Empty<InputAttachment>(),
                                null,
                                true,
                                null
                            );
                            await SendTelegramReplyAsync(null, callbackResult, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[telegram] callback handle error: {ex.Message}");
                            await TrySendOrQueueStandaloneMessageAsync($"버튼 처리 오류: {ex.Message}", "callback_error_failed", cancellationToken);
                        }
                        TryPersistOffset();
                        continue;
                    }

                    var commandKey = ResolveCommandBucket(update.Text ?? string.Empty);
                    if (!AllowCommand(commandKey))
                    {
                        await TrySendOrQueueStandaloneMessageAsync(
                            $"rate limit exceeded: {commandKey} 명령 요청이 너무 많습니다. 잠시 후 다시 시도하세요.",
                            "rate_limit_message_failed",
                            cancellationToken
                        );
                        continue;
                    }

                    var input = hasText ? update.Text!.Trim() : "첨부 파일을 분석해줘";
                    var showProgressMessage = ShouldShowProgressMessage(input, hasAttachments);
                    int? progressMessageId = null;
                    CancellationTokenSource? progressCts = null;
                    Task? typingTask = null;
                    try
                    {
                        if (showProgressMessage)
                        {
                            progressMessageId = await _telegramClient.SendProgressMessageAsync("응답 생성 중입니다. 잠시만 기다리세요.", cancellationToken);
                            progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            typingTask = RunTypingLoopAsync(progressCts.Token);
                        }

                        var progressStream = progressMessageId.HasValue && progressMessageId.Value > 0
                            ? CreateProgressStreamCallback(progressMessageId.Value, cancellationToken)
                            : null;
                        var perfStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        var result = await _commandService.ExecuteAsync(
                            input,
                            "telegram",
                            cancellationToken,
                            update.Attachments ?? Array.Empty<InputAttachment>(),
                            null,
                            true,
                            progressStream
                        );
                        perfStopwatch.Stop();
                        var resultWithPerf = AppendElapsedTimeToFooter(result, perfStopwatch.ElapsedMilliseconds);
                        await SendTelegramReplyAsync(progressMessageId, resultWithPerf, cancellationToken);
                    }
                    finally
                    {
                        if (progressCts != null)
                        {
                            progressCts.Cancel();
                            try
                            {
                                if (typingTask != null)
                                {
                                    await typingTask;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            progressCts.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[telegram] handle error: {ex.Message}");
                    await TrySendOrQueueStandaloneMessageAsync($"error: {ex.Message}", "error_message_failed", cancellationToken);
                }
                finally
                {
                    TryPersistOffset();
                }
            }
        }
    }

    private async Task SendTelegramReplyAsync(int? progressMessageId, string text, CancellationToken cancellationToken)
    {
        // 빈 응답은 별도 전송 생략 — 첨부 파일 등 부수 효과로 답한 핸들러용.
        if (string.IsNullOrWhiteSpace(text))
        {
            if (progressMessageId.HasValue && progressMessageId.Value > 0)
            {
                // progress 메시지가 떠 있으면 짧은 안내로 교체해서 spinner 제거.
                await _telegramClient.ReplaceMessageAsync(progressMessageId.Value, "✅ 첨부로 보냈습니다.", cancellationToken);
            }
            return;
        }

        // 응답 끝에 inline keyboard 마커가 붙어 있으면 떼어내서 별도로 전송.
        var (cleanedText, buttonRows) = ExtractInlineButtonsFromText(text);

        if (progressMessageId.HasValue && progressMessageId.Value > 0)
        {
            var replaceResult = await _telegramClient.ReplaceMessageAsync(progressMessageId.Value, cleanedText, cancellationToken);
            if (replaceResult.Success)
            {
                if (buttonRows != null && buttonRows.Count > 0)
                {
                    // 본문은 이미 progress 메시지를 통해 갱신됐으므로 버튼만 짧은 후속 메시지로 전송.
                    await _telegramClient.SendMessageWithButtonsAsync("⤵ 빠른 작업", buttonRows, cancellationToken);
                }
                return;
            }
        }

        bool sent;
        if (buttonRows != null && buttonRows.Count > 0)
        {
            sent = await _telegramClient.SendMessageWithButtonsAsync(cleanedText, buttonRows, cancellationToken);
        }
        else
        {
            sent = await _telegramClient.SendMessageAsync(cleanedText, cancellationToken);
        }
        if (!sent)
        {
            QueueReplyForRetry(cleanedText, progressMessageId.HasValue ? "reply_replace_and_send_failed" : "reply_send_failed");
        }
    }

    // 응답 본문이 footer("\n\n— provider·model · ...")를 가지고 있으면 그 끝에 ⏱ 시간을 append.
    // footer가 없으면(slash 명령 응답 등 짧은 텍스트) 표시 생략 — 노이즈를 피한다.
    private static string AppendElapsedTimeToFooter(string text, long elapsedMs)
    {
        if (string.IsNullOrWhiteSpace(text) || elapsedMs < 0)
        {
            return text;
        }
        // 너무 짧은 turn 은 표시 안 함 (sub-second slash commands).
        if (elapsedMs < 1000)
        {
            return text;
        }

        // 마지막 footer 라인 찾기. AppendTelegramResponseFooter가 만든 패턴: \n\n— ...
        var idx = text.LastIndexOf("\n\n— ", StringComparison.Ordinal);
        if (idx < 0)
        {
            return text;
        }
        var afterFooterStart = idx + 4; // skip "\n\n— "
        // footer 가 한 줄이 아니라 그 뒤에도 텍스트가 붙을 수 있으므로 그 줄 끝까지만 본다.
        var lineEnd = text.IndexOf('\n', afterFooterStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }
        var elapsedLabel = elapsedMs >= 60_000
            ? $"{elapsedMs / 60_000.0:0.0}m"
            : $"{elapsedMs / 1000.0:0.0}s";
        var insertion = $" · ⏱ {elapsedLabel}";
        return text.Insert(lineEnd, insertion);
    }

    // 본문 끝의 __TG_BUTTONS__/__/TG_BUTTONS__ 블록을 파싱해 버튼 행으로 변환. 본문에서 마커는 제거.
    private static (string CleanedText, IReadOnlyList<IReadOnlyList<TelegramInlineButton>>? Buttons) ExtractInlineButtonsFromText(string text)
    {
        const string Open = CommandService.TelegramButtonsMarkerOpen;
        const string Close = CommandService.TelegramButtonsMarkerClose;
        if (string.IsNullOrEmpty(text))
        {
            return (text, null);
        }
        var openIdx = text.LastIndexOf(Open, StringComparison.Ordinal);
        if (openIdx < 0)
        {
            return (text, null);
        }
        var closeIdx = text.IndexOf(Close, openIdx + Open.Length, StringComparison.Ordinal);
        if (closeIdx < 0)
        {
            return (text, null);
        }

        var blockStart = openIdx + Open.Length;
        var blockBody = text.Substring(blockStart, closeIdx - blockStart);
        var lines = blockBody
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var row = new List<TelegramInlineButton>();
        foreach (var line in lines)
        {
            var pipeIdx = line.IndexOf('|');
            if (pipeIdx <= 0 || pipeIdx >= line.Length - 1)
            {
                continue;
            }
            var cmd = line[..pipeIdx].Trim();
            var label = line[(pipeIdx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(cmd) || string.IsNullOrWhiteSpace(label))
            {
                continue;
            }
            row.Add(new TelegramInlineButton(label, cmd));
        }

        var cleaned = text[..openIdx].TrimEnd();
        var rows = row.Count == 0
            ? null
            : (IReadOnlyList<IReadOnlyList<TelegramInlineButton>>)new[] { (IReadOnlyList<TelegramInlineButton>)row.ToArray() };
        return (cleaned, rows);
    }

    private async Task TrySendOrQueueStandaloneMessageAsync(string text, string failureReason, CancellationToken cancellationToken)
    {
        var sent = await _telegramClient.SendMessageAsync(text, cancellationToken);
        if (!sent)
        {
            QueueReplyForRetry(text, failureReason);
        }
    }

    private Task RunTypingLoopAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _telegramClient.SendTypingAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cancellationToken);
    }

    private Action<string> CreateProgressStreamCallback(int messageId, CancellationToken cancellationToken)
    {
        var gate = new object();
        var builder = new System.Text.StringBuilder();
        var lastEditUtc = DateTimeOffset.MinValue;
        var editInFlight = 0;

        return delta =>
        {
            if (string.IsNullOrEmpty(delta) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            lock (gate)
            {
                builder.Append(delta);
                if (DateTimeOffset.UtcNow - lastEditUtc < TimeSpan.FromSeconds(1))
                {
                    return;
                }

                lastEditUtc = DateTimeOffset.UtcNow;
            }

            if (Interlocked.Exchange(ref editInFlight, 1) == 1)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    string snapshot;
                    lock (gate)
                    {
                        snapshot = builder.ToString();
                    }

                    var preview = TrimTelegramProgressPreview(snapshot);
                    if (!string.IsNullOrWhiteSpace(preview))
                    {
                        await _telegramClient.ReplaceMessageAsync(
                            messageId,
                            $"작성 중...\n\n{preview}",
                            cancellationToken
                        );
                    }
                }
                catch
                {
                }
                finally
                {
                    Interlocked.Exchange(ref editInFlight, 0);
                }
            }, CancellationToken.None);
        };
    }

    private static string TrimTelegramProgressPreview(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= 3000)
        {
            return normalized;
        }

        return normalized[^3000..];
    }

    private static bool ShouldShowProgressMessage(string input, bool hasAttachments)
    {
        if (hasAttachments)
        {
            return true;
        }

        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private void TryPersistOffset()
    {
        try
        {
            _stateStore.SaveNextOffset(_offset);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[telegram] offset save failed: {ex.Message}");
        }
    }

    private async Task FlushReplyOutboxAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var readyEntries = _replyOutboxStore.GetReadyEntries(nowUtc, TelegramOutboxFlushBatchSize);
        foreach (var entry in readyEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sent = await _telegramClient.SendMessageAsync(entry.Text, cancellationToken);
            if (sent)
            {
                _replyOutboxStore.MarkDelivered(entry.Id);
                Console.WriteLine($"[telegram-outbox] delivered id={entry.Id} attempts={entry.AttemptCount + 1}");
                continue;
            }

            _replyOutboxStore.MarkFailed(entry.Id, "send_failed", DateTimeOffset.UtcNow);
        }
    }

    private void QueueReplyForRetry(string text, string reason)
    {
        var id = _replyOutboxStore.Enqueue(text, reason, DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        Console.Error.WriteLine($"[telegram-outbox] queued id={id} reason={reason}");
    }

    private bool IsAuthorizedUpdate(TelegramUpdate update)
    {
        var expectedChatId = _config.TelegramChatId?.Trim();
        if (!string.IsNullOrWhiteSpace(expectedChatId))
        {
            var incomingChatId = (update.ChatId ?? string.Empty).Trim();
            if (!string.Equals(expectedChatId, incomingChatId, StringComparison.Ordinal))
            {
                LogUnauthorizedUpdate(update, "chat_id_mismatch");
                return false;
            }
        }

        // CSV 다중 허용: "12345,67890" 같이 콤마로 여러 user_id 등록 가능.
        var rawAllowed = _config.TelegramAllowedUserId ?? string.Empty;
        var allowedIds = rawAllowed
            .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        if (allowedIds.Length > 0)
        {
            var incomingUserId = (update.FromUserId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(incomingUserId)
                || !allowedIds.Any(id => string.Equals(id, incomingUserId, StringComparison.Ordinal)))
            {
                LogUnauthorizedUpdate(update, "user_id_not_in_allowlist");
                return false;
            }
        }

        return true;
    }

    private static void LogUnauthorizedUpdate(TelegramUpdate update, string reason)
    {
        // 비허용 호출은 모니터링용으로 한 줄 로그만 남기고 응답은 보내지 않는다 (스팸 방지).
        var preview = string.IsNullOrWhiteSpace(update.Text) ? "(no-text)" : update.Text!.Trim();
        if (preview.Length > 40)
        {
            preview = preview[..40] + "…";
        }
        Console.Error.WriteLine($"[telegram] unauthorized update reason={reason} chat={update.ChatId} from={update.FromUserId} text={preview}");
    }

    private static string ResolveCommandBucket(string text)
    {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("/kill ", StringComparison.Ordinal))
        {
            return "kill";
        }

        if (normalized.StartsWith("/metrics", StringComparison.Ordinal))
        {
            return "metrics";
        }

        if (normalized.StartsWith("/llm", StringComparison.Ordinal))
        {
            return "llm";
        }

        if (normalized.StartsWith("/coding", StringComparison.Ordinal))
        {
            return "coding";
        }

        if (normalized.StartsWith("/refactor", StringComparison.Ordinal))
        {
            return "refactor";
        }

        if (normalized.StartsWith("/doctor", StringComparison.Ordinal))
        {
            return "doctor";
        }

        if (normalized.StartsWith("/plan", StringComparison.Ordinal))
        {
            return "plan";
        }

        if (normalized.StartsWith("/routine", StringComparison.Ordinal)
            || normalized.StartsWith("/routines", StringComparison.Ordinal))
        {
            return "routine";
        }

        return "default";
    }

    private bool AllowCommand(string bucket)
    {
        var now = DateTimeOffset.UtcNow;
        var (limit, window) = ResolveRatePolicy(bucket);
        lock (_rateLock)
        {
            if (!_commandWindows.TryGetValue(bucket, out var current))
            {
                _commandWindows[bucket] = new CommandWindow(now, 1);
                return true;
            }

            if (now - current.StartUtc >= window)
            {
                _commandWindows[bucket] = new CommandWindow(now, 1);
                return true;
            }

            if (current.Count >= limit)
            {
                return false;
            }

            _commandWindows[bucket] = current with { Count = current.Count + 1 };
            return true;
        }
    }

    private static (int Limit, TimeSpan Window) ResolveRatePolicy(string bucket)
    {
        if (bucket == "kill")
        {
            return (3, TimeSpan.FromMinutes(1));
        }

        if (bucket == "metrics")
        {
            return (20, TimeSpan.FromMinutes(1));
        }

        if (bucket == "llm")
        {
            return (20, TimeSpan.FromMinutes(1));
        }

        if (bucket == "coding")
        {
            return (8, TimeSpan.FromMinutes(1));
        }

        if (bucket == "refactor")
        {
            return (16, TimeSpan.FromMinutes(1));
        }

        if (bucket == "doctor")
        {
            return (6, TimeSpan.FromMinutes(1));
        }

        if (bucket == "plan")
        {
            return (12, TimeSpan.FromMinutes(1));
        }

        if (bucket == "routine")
        {
            return (12, TimeSpan.FromMinutes(1));
        }

        return (40, TimeSpan.FromMinutes(1));
    }

    private sealed record CommandWindow(DateTimeOffset StartUtc, int Count);
}
