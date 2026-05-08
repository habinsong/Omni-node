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
                    if (!hasText && !hasAttachments)
                    {
                        continue;
                    }

                    if (!IsAuthorizedUpdate(update))
                    {
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
                        var result = await _commandService.ExecuteAsync(
                            input,
                            "telegram",
                            cancellationToken,
                            update.Attachments ?? Array.Empty<InputAttachment>(),
                            null,
                            true,
                            progressStream
                        );
                        await SendTelegramReplyAsync(progressMessageId, result, cancellationToken);
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
        if (progressMessageId.HasValue && progressMessageId.Value > 0)
        {
            var replaceResult = await _telegramClient.ReplaceMessageAsync(progressMessageId.Value, text, cancellationToken);
            if (replaceResult.Success)
            {
                return;
            }
        }

        var sent = await _telegramClient.SendMessageAsync(text, cancellationToken);
        if (!sent)
        {
            QueueReplyForRetry(text, progressMessageId.HasValue ? "reply_replace_and_send_failed" : "reply_send_failed");
        }
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
                return false;
            }
        }

        var expectedUserId = _config.TelegramAllowedUserId?.Trim();
        if (!string.IsNullOrWhiteSpace(expectedUserId))
        {
            var incomingUserId = (update.FromUserId ?? string.Empty).Trim();
            if (!string.Equals(expectedUserId, incomingUserId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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
