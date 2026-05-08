using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed class TelegramClient : IDisposable
{
    private const int MaxAttachmentBytes = 350_000;
    private const int TelegramApiMaxAttempts = 4;
    private static readonly IReadOnlyDictionary<string, string> SourceHomeUrlByLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["연합뉴스"] = "https://www.yna.co.kr",
        ["연합뉴스TV"] = "https://www.yonhapnewstv.co.kr",
        ["뉴시스"] = "https://www.newsis.com",
        ["매일경제"] = "https://www.mk.co.kr",
        ["블룸버그"] = "https://www.bloomberg.com",
        ["아시아경제"] = "https://www.asiae.co.kr",
        ["더구루"] = "https://www.theguru.co.kr",
        ["부산일보"] = "https://www.busan.com",
        ["중앙일보"] = "https://www.joongang.co.kr",
        ["동아일보"] = "https://www.donga.com",
        ["조선일보"] = "https://www.chosun.com",
        ["KBS 뉴스"] = "https://news.kbs.co.kr",
        ["MBC 뉴스"] = "https://imnews.imbc.com",
        ["SBS 뉴스"] = "https://news.sbs.co.kr",
        ["YTN"] = "https://www.ytn.co.kr",
        ["CNN"] = "https://www.cnn.com",
        ["Reuters"] = "https://www.reuters.com",
        ["로이터"] = "https://www.reuters.com",
        ["위키백과"] = "https://ko.wikipedia.org",
        ["인베스트조선"] = "https://www.investchosun.com",
        ["KB자산운용"] = "https://www.kbam.co.kr"
    };
    private readonly HttpClient _httpClient;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly object _errorLogLock = new();
    private DateTimeOffset _lastSendErrorLogUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastGetUpdatesErrorLogUtc = DateTimeOffset.MinValue;

    public TelegramClient(RuntimeSettings runtimeSettings)
    {
        _httpClient = new HttpClient();
        _runtimeSettings = runtimeSettings;
    }

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_runtimeSettings.GetTelegramBotToken())
           && !string.IsNullOrWhiteSpace(_runtimeSettings.GetTelegramChatId());

    public async Task<bool> SendOtpAsync(string otp, CancellationToken cancellationToken)
    {
        return await SendMessageAsync($"[Omni-node] OTP: {otp}", cancellationToken);
    }

    public async Task<bool> SendMessageAsync(string text, CancellationToken cancellationToken)
    {
        if (!TryGetConfiguredRoute(out var botToken, out var chatId))
        {
            Console.WriteLine($"[telegram] not configured, skipped message: {text}");
            return false;
        }

        var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var normalized = NormalizeTelegramText(text);
        var sourceLinkHtml = TryBuildSingleSourceLinkHtml(normalized);
        var sourcePreviewUrl = ExtractFirstUrlFromText(sourceLinkHtml);
        var enableSourcePreview = !string.IsNullOrWhiteSpace(sourceLinkHtml);
        var plainWithPreviewUrl = AppendPreviewUrlToPlainText(normalized, sourcePreviewUrl);
        var htmlCandidate = BuildTelegramHtmlWithAlignedTables(normalized);
        htmlCandidate = AppendSourceLinkHtml(htmlCandidate, sourceLinkHtml);
        if (!string.IsNullOrWhiteSpace(htmlCandidate))
        {
            var htmlChunks = SplitTelegramHtmlMessageSafely(htmlCandidate, 3900);
            if (htmlChunks.Count > 0)
            {
                var htmlResult = await SendChunksAsync(
                    endpoint,
                    chatId,
                    htmlChunks,
                    "HTML",
                    enableSourcePreview,
                    cancellationToken
                );
                if (htmlResult.Success)
                {
                    return true;
                }

                if (ShouldLogSendError())
                {
                    Console.Error.WriteLine(
                        $"[telegram] sendMessage failed chunk={htmlResult.FailedIndex + 1}/{htmlChunks.Count} html=({htmlResult.StatusCode}) {htmlResult.ErrorBody}"
                    );
                }

                if (htmlResult.SentCount > 0)
                {
                    if (ShouldLogSendError())
                    {
                        Console.Error.WriteLine("[telegram] fallback suppressed after partial html delivery to avoid duplicate telegram messages.");
                    }

                    return false;
                }
            }
        }

        var styledHtmlCandidate = BuildTelegramHtmlWithLabelStyling(normalized);
        styledHtmlCandidate = AppendSourceLinkHtml(styledHtmlCandidate, sourceLinkHtml);
        if (!string.IsNullOrWhiteSpace(styledHtmlCandidate))
        {
            var htmlChunks = SplitTelegramHtmlMessageSafely(styledHtmlCandidate, 3900);
            if (htmlChunks.Count > 0)
            {
                var htmlResult = await SendChunksAsync(
                    endpoint,
                    chatId,
                    htmlChunks,
                    "HTML",
                    enableSourcePreview,
                    cancellationToken
                );
                if (htmlResult.Success)
                {
                    return true;
                }

                if (ShouldLogSendError())
                {
                    Console.Error.WriteLine(
                        $"[telegram] sendMessage failed chunk={htmlResult.FailedIndex + 1}/{htmlChunks.Count} html-label=({htmlResult.StatusCode}) {htmlResult.ErrorBody}"
                    );
                }

                if (htmlResult.SentCount > 0)
                {
                    if (ShouldLogSendError())
                    {
                        Console.Error.WriteLine("[telegram] fallback suppressed after partial html-label delivery to avoid duplicate telegram messages.");
                    }

                    return false;
                }
            }
        }

        var chunks = SplitTelegramMessage(plainWithPreviewUrl, 3900);
        var plainResult = await SendChunksAsync(
            endpoint,
            chatId,
            chunks,
            null,
            enableSourcePreview,
            cancellationToken
        );
        if (!plainResult.Success)
        {
            if (ShouldLogSendError())
            {
                Console.Error.WriteLine(
                    $"[telegram] sendMessage failed chunk={plainResult.FailedIndex + 1}/{chunks.Count} plain=({plainResult.StatusCode}) {plainResult.ErrorBody}"
                );
            }

            return false;
        }

        return true;
    }

    public async Task<int?> SendProgressMessageAsync(string text, CancellationToken cancellationToken)
    {
        if (!TryGetConfiguredRoute(out var botToken, out var chatId))
        {
            return null;
        }

        var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var result = await SendMessageCoreDetailedAsync(
            endpoint,
            chatId,
            NormalizeTelegramText(text),
            null,
            false,
            cancellationToken
        );
        return result.Ok && result.MessageId > 0 ? result.MessageId : null;
    }

    public async Task<(bool Success, bool FirstChunkDelivered)> ReplaceMessageAsync(int messageId, string text, CancellationToken cancellationToken)
    {
        if (messageId <= 0)
        {
            return (false, false);
        }

        if (!TryGetConfiguredRoute(out var botToken, out var chatId))
        {
            return (false, false);
        }

        var sendEndpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
        var editEndpoint = $"https://api.telegram.org/bot{botToken}/editMessageText";
        var normalized = NormalizeTelegramText(text);
        var sourceLinkHtml = TryBuildSingleSourceLinkHtml(normalized);
        var sourcePreviewUrl = ExtractFirstUrlFromText(sourceLinkHtml);
        var enableSourcePreview = !string.IsNullOrWhiteSpace(sourceLinkHtml);
        var plainWithPreviewUrl = AppendPreviewUrlToPlainText(normalized, sourcePreviewUrl);
        var htmlCandidate = BuildTelegramHtmlWithAlignedTables(normalized);
        htmlCandidate = AppendSourceLinkHtml(htmlCandidate, sourceLinkHtml);
        if (!string.IsNullOrWhiteSpace(htmlCandidate))
        {
            var htmlChunks = SplitTelegramHtmlMessageSafely(htmlCandidate, 3900);
            if (htmlChunks.Count > 0)
            {
                var htmlResult = await ReplaceFirstChunkAndSendTailAsync(
                    editEndpoint,
                    sendEndpoint,
                    chatId,
                    messageId,
                    htmlChunks,
                    "HTML",
                    enableSourcePreview,
                    cancellationToken
                );
                if (htmlResult.Success)
                {
                    return (true, true);
                }

                if (ShouldLogSendError())
                {
                    Console.Error.WriteLine(
                        $"[telegram] editMessage failed chunk={htmlResult.FailedIndex + 1}/{htmlChunks.Count} html=({htmlResult.StatusCode}) {htmlResult.ErrorBody}"
                    );
                }

                if (htmlResult.FirstChunkDelivered)
                {
                    return (false, true);
                }
            }
        }

        var styledHtmlCandidate = BuildTelegramHtmlWithLabelStyling(normalized);
        styledHtmlCandidate = AppendSourceLinkHtml(styledHtmlCandidate, sourceLinkHtml);
        if (!string.IsNullOrWhiteSpace(styledHtmlCandidate))
        {
            var htmlChunks = SplitTelegramHtmlMessageSafely(styledHtmlCandidate, 3900);
            if (htmlChunks.Count > 0)
            {
                var htmlResult = await ReplaceFirstChunkAndSendTailAsync(
                    editEndpoint,
                    sendEndpoint,
                    chatId,
                    messageId,
                    htmlChunks,
                    "HTML",
                    enableSourcePreview,
                    cancellationToken
                );
                if (htmlResult.Success)
                {
                    return (true, true);
                }

                if (ShouldLogSendError())
                {
                    Console.Error.WriteLine(
                        $"[telegram] editMessage failed chunk={htmlResult.FailedIndex + 1}/{htmlChunks.Count} html-label=({htmlResult.StatusCode}) {htmlResult.ErrorBody}"
                    );
                }

                if (htmlResult.FirstChunkDelivered)
                {
                    return (false, true);
                }
            }
        }

        var plainChunks = SplitTelegramMessage(plainWithPreviewUrl, 3900);
        var plainResult = await ReplaceFirstChunkAndSendTailAsync(
            editEndpoint,
            sendEndpoint,
            chatId,
            messageId,
            plainChunks,
            null,
            enableSourcePreview,
            cancellationToken
        );
        if (!plainResult.Success && ShouldLogSendError())
        {
            Console.Error.WriteLine(
                $"[telegram] editMessage failed chunk={plainResult.FailedIndex + 1}/{plainChunks.Count} plain=({plainResult.StatusCode}) {plainResult.ErrorBody}"
            );
        }

        return (plainResult.Success, plainResult.FirstChunkDelivered);
    }

    public async Task<bool> SendTypingAsync(CancellationToken cancellationToken)
    {
        if (!TryGetConfiguredRoute(out var botToken, out var chatId))
        {
            return false;
        }

        var endpoint = $"https://api.telegram.org/bot{botToken}/sendChatAction";
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append($"\"chat_id\":\"{EscapeJson(chatId)}\",");
        builder.Append("\"action\":\"typing\"");
        builder.Append("}");
        for (var attempt = 1; attempt <= TelegramApiMaxAttempts; attempt += 1)
        {
            try
            {
                using var content = new StringContent(builder.ToString(), Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                if (attempt >= TelegramApiMaxAttempts || !ShouldRetryTelegramRequest(response.StatusCode, body))
                {
                    return false;
                }

                await DelayTelegramRetryAsync(attempt, response.StatusCode, body, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < TelegramApiMaxAttempts)
            {
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < TelegramApiMaxAttempts)
            {
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
        }

        return false;
    }

    private async Task<(bool Success, int SentCount, int FailedIndex, int StatusCode, string ErrorBody)> SendChunksAsync(
        string endpoint,
        string chatId,
        IReadOnlyList<string> chunks,
        string? parseMode,
        bool enableLinkPreview,
        CancellationToken cancellationToken
    )
    {
        var sentCount = 0;
        for (var index = 0; index < chunks.Count; index += 1)
        {
            var chunk = chunks[index];
            var result = await SendMessageCoreAsync(endpoint, chatId, chunk, parseMode, enableLinkPreview, cancellationToken);
            if (!result.Ok)
            {
                return (false, sentCount, index, result.StatusCode, result.ErrorBody);
            }

            sentCount += 1;
        }

        return (true, sentCount, -1, 200, string.Empty);
    }

    private async Task<(bool Success, bool FirstChunkDelivered, int FailedIndex, int StatusCode, string ErrorBody)> ReplaceFirstChunkAndSendTailAsync(
        string editEndpoint,
        string sendEndpoint,
        string chatId,
        int messageId,
        IReadOnlyList<string> chunks,
        string? parseMode,
        bool enableLinkPreview,
        CancellationToken cancellationToken
    )
    {
        if (chunks.Count == 0)
        {
            return (true, false, -1, 200, string.Empty);
        }

        var firstChunkResult = await EditMessageCoreAsync(
            editEndpoint,
            chatId,
            messageId,
            chunks[0],
            parseMode,
            enableLinkPreview,
            cancellationToken
        );
        if (!firstChunkResult.Ok)
        {
            return (false, false, 0, firstChunkResult.StatusCode, firstChunkResult.ErrorBody);
        }

        if (chunks.Count == 1)
        {
            return (true, true, -1, 200, string.Empty);
        }

        var tailResult = await SendChunksAsync(
            sendEndpoint,
            chatId,
            chunks.Skip(1).ToArray(),
            parseMode,
            enableLinkPreview,
            cancellationToken
        );
        return tailResult.Success
            ? (true, true, -1, 200, string.Empty)
            : (false, true, tailResult.FailedIndex + 1, tailResult.StatusCode, tailResult.ErrorBody);
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        var botToken = _runtimeSettings.GetTelegramBotToken();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return Array.Empty<TelegramUpdate>();
        }

        var endpoint = $"https://api.telegram.org/bot{botToken}/getUpdates?timeout=15&offset={offset}";
        using var response = await GetWithRetryAsync(endpoint, cancellationToken);
        if (response == null || !response.IsSuccessStatusCode)
        {
            var body = response != null
                ? await response.Content.ReadAsStringAsync(cancellationToken)
                : "request_failed_after_retries";
            if (ShouldLogGetUpdatesError())
            {
                var statusCode = response != null ? (int)response.StatusCode : 0;
                Console.Error.WriteLine($"[telegram] getUpdates failed ({statusCode}): {body}");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return Array.Empty<TelegramUpdate>();
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(payload);

        if (!doc.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return Array.Empty<TelegramUpdate>();
        }

        if (!doc.RootElement.TryGetProperty("result", out var resultElement) || resultElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TelegramUpdate>();
        }

        var updates = new List<TelegramUpdate>();
        foreach (var updateElement in resultElement.EnumerateArray())
        {
            if (!updateElement.TryGetProperty("update_id", out var updateIdElement) || !updateIdElement.TryGetInt64(out var updateId))
            {
                continue;
            }

            string? text = null;
            string? chatId = null;
            string? fromUserId = null;
            var attachments = new List<InputAttachment>();
            if (updateElement.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.Object)
            {
                if (messageElement.TryGetProperty("text", out var textElement))
                {
                    text = textElement.GetString();
                }

                if (string.IsNullOrWhiteSpace(text)
                    && messageElement.TryGetProperty("caption", out var captionElement)
                    && captionElement.ValueKind == JsonValueKind.String)
                {
                    text = captionElement.GetString();
                }

                if (messageElement.TryGetProperty("chat", out var chatElement)
                    && chatElement.ValueKind == JsonValueKind.Object
                    && chatElement.TryGetProperty("id", out var chatIdElement))
                {
                    chatId = chatIdElement.ValueKind switch
                    {
                        JsonValueKind.Number => chatIdElement.GetInt64().ToString(),
                        JsonValueKind.String => chatIdElement.GetString(),
                        _ => null
                    };
                }

                if (messageElement.TryGetProperty("from", out var fromElement)
                    && fromElement.ValueKind == JsonValueKind.Object
                    && fromElement.TryGetProperty("id", out var fromIdElement))
                {
                    fromUserId = fromIdElement.ValueKind switch
                    {
                        JsonValueKind.Number => fromIdElement.GetInt64().ToString(),
                        JsonValueKind.String => fromIdElement.GetString(),
                        _ => null
                    };
                }

                if (messageElement.TryGetProperty("photo", out var photoElement)
                    && photoElement.ValueKind == JsonValueKind.Array)
                {
                    var selectedPhotoId = string.Empty;
                    var fallbackPhotoId = string.Empty;
                    foreach (var photo in photoElement.EnumerateArray())
                    {
                        if (!photo.TryGetProperty("file_id", out var photoIdElement) || photoIdElement.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var fileId = photoIdElement.GetString();
                        if (string.IsNullOrWhiteSpace(fileId))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(fallbackPhotoId))
                        {
                            fallbackPhotoId = fileId.Trim();
                        }

                        var fileSize = 0L;
                        if (photo.TryGetProperty("file_size", out var photoSizeElement)
                            && photoSizeElement.ValueKind == JsonValueKind.Number
                            && photoSizeElement.TryGetInt64(out var parsedSize))
                        {
                            fileSize = parsedSize;
                        }

                        if (fileSize > 0 && fileSize <= MaxAttachmentBytes)
                        {
                            selectedPhotoId = fileId.Trim();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(selectedPhotoId))
                    {
                        selectedPhotoId = fallbackPhotoId;
                    }

                    if (!string.IsNullOrWhiteSpace(selectedPhotoId))
                    {
                        var attachment = await DownloadAttachmentAsync(
                            botToken,
                            selectedPhotoId,
                            "telegram-photo.jpg",
                            "image/jpeg",
                            true,
                            cancellationToken
                        );
                        if (attachment != null)
                        {
                            attachments.Add(attachment);
                        }
                    }
                }

                if (messageElement.TryGetProperty("document", out var documentElement)
                    && documentElement.ValueKind == JsonValueKind.Object
                    && documentElement.TryGetProperty("file_id", out var documentIdElement)
                    && documentIdElement.ValueKind == JsonValueKind.String)
                {
                    var fileId = documentIdElement.GetString();
                    if (!string.IsNullOrWhiteSpace(fileId))
                    {
                        var fileName = documentElement.TryGetProperty("file_name", out var fileNameElement)
                                       && fileNameElement.ValueKind == JsonValueKind.String
                            ? (fileNameElement.GetString() ?? "telegram-file")
                            : "telegram-file";
                        var mimeType = documentElement.TryGetProperty("mime_type", out var mimeElement)
                                       && mimeElement.ValueKind == JsonValueKind.String
                            ? (mimeElement.GetString() ?? "application/octet-stream")
                            : "application/octet-stream";
                        var isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

                        var attachment = await DownloadAttachmentAsync(
                            botToken,
                            fileId.Trim(),
                            fileName,
                            mimeType,
                            isImage,
                            cancellationToken
                        );
                        if (attachment != null)
                        {
                            attachments.Add(attachment);
                        }
                    }
                }

                if (messageElement.TryGetProperty("voice", out var voiceElement)
                    && voiceElement.ValueKind == JsonValueKind.Object)
                {
                    var attachment = await DownloadTelegramAudioAttachmentAsync(
                        botToken,
                        voiceElement,
                        "telegram-voice.ogg",
                        "audio/ogg",
                        cancellationToken
                    );
                    if (attachment != null)
                    {
                        attachments.Add(attachment);
                    }
                }

                if (messageElement.TryGetProperty("audio", out var audioElement)
                    && audioElement.ValueKind == JsonValueKind.Object)
                {
                    var fallbackName = audioElement.TryGetProperty("file_name", out var audioNameElement)
                                       && audioNameElement.ValueKind == JsonValueKind.String
                        ? (audioNameElement.GetString() ?? "telegram-audio")
                        : "telegram-audio";
                    var attachment = await DownloadTelegramAudioAttachmentAsync(
                        botToken,
                        audioElement,
                        fallbackName,
                        "audio/mpeg",
                        cancellationToken
                    );
                    if (attachment != null)
                    {
                        attachments.Add(attachment);
                    }
                }
            }

            updates.Add(new TelegramUpdate(
                updateId,
                text,
                chatId,
                fromUserId,
                attachments.Count == 0 ? Array.Empty<InputAttachment>() : attachments.ToArray()
            ));
        }

        return updates;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private async Task<(bool Ok, int StatusCode, string ErrorBody)> SendMessageCoreAsync(
        string endpoint,
        string chatId,
        string text,
        string? parseMode,
        bool enableLinkPreview,
        CancellationToken cancellationToken
    )
    {
        var detailed = await SendMessageCoreDetailedAsync(endpoint, chatId, text, parseMode, enableLinkPreview, cancellationToken);
        return (detailed.Ok, detailed.StatusCode, detailed.ErrorBody);
    }

    private async Task<(bool Ok, int StatusCode, string ErrorBody, int MessageId)> SendMessageCoreDetailedAsync(
        string endpoint,
        string chatId,
        string text,
        string? parseMode,
        bool enableLinkPreview,
        CancellationToken cancellationToken
    )
    {
        var requestBody = BuildTelegramSendBody(chatId, text, parseMode, enableLinkPreview);
        string lastErrorBody = string.Empty;
        int lastStatusCode = 0;
        for (var attempt = 1; attempt <= TelegramApiMaxAttempts; attempt += 1)
        {
            try
            {
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return (true, (int)response.StatusCode, string.Empty, ExtractMessageId(body));
                }

                lastStatusCode = (int)response.StatusCode;
                lastErrorBody = body;
                if (attempt >= TelegramApiMaxAttempts || !ShouldRetryTelegramRequest(response.StatusCode, body))
                {
                    return (false, lastStatusCode, lastErrorBody, 0);
                }

                await DelayTelegramRetryAsync(attempt, response.StatusCode, body, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < TelegramApiMaxAttempts)
            {
                lastStatusCode = 408;
                lastErrorBody = ex.Message;
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastStatusCode = 408;
                lastErrorBody = ex.Message;
                return (false, lastStatusCode, lastErrorBody, 0);
            }
            catch (HttpRequestException ex) when (attempt < TelegramApiMaxAttempts)
            {
                lastStatusCode = 503;
                lastErrorBody = ex.Message;
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                lastStatusCode = 503;
                lastErrorBody = ex.Message;
                return (false, lastStatusCode, lastErrorBody, 0);
            }
        }

        return (false, lastStatusCode, lastErrorBody, 0);
    }

    private async Task<(bool Ok, int StatusCode, string ErrorBody)> EditMessageCoreAsync(
        string endpoint,
        string chatId,
        int messageId,
        string text,
        string? parseMode,
        bool enableLinkPreview,
        CancellationToken cancellationToken
    )
    {
        var requestBody = BuildTelegramEditBody(chatId, messageId, text, parseMode, enableLinkPreview);
        string lastErrorBody = string.Empty;
        int lastStatusCode = 0;
        for (var attempt = 1; attempt <= TelegramApiMaxAttempts; attempt += 1)
        {
            try
            {
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return (true, (int)response.StatusCode, string.Empty);
                }

                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                lastStatusCode = (int)response.StatusCode;
                lastErrorBody = errorBody;
                if (attempt >= TelegramApiMaxAttempts || !ShouldRetryTelegramRequest(response.StatusCode, errorBody))
                {
                    return (false, lastStatusCode, lastErrorBody);
                }

                await DelayTelegramRetryAsync(attempt, response.StatusCode, errorBody, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < TelegramApiMaxAttempts)
            {
                lastStatusCode = 408;
                lastErrorBody = ex.Message;
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastStatusCode = 408;
                lastErrorBody = ex.Message;
                return (false, lastStatusCode, lastErrorBody);
            }
            catch (HttpRequestException ex) when (attempt < TelegramApiMaxAttempts)
            {
                lastStatusCode = 503;
                lastErrorBody = ex.Message;
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                lastStatusCode = 503;
                lastErrorBody = ex.Message;
                return (false, lastStatusCode, lastErrorBody);
            }
        }

        return (false, lastStatusCode, lastErrorBody);
    }

    private static string BuildTelegramSendBody(string chatId, string text, string? parseMode, bool enableLinkPreview)
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append($"\"chat_id\":\"{EscapeJson(chatId)}\",");
        builder.Append($"\"text\":\"{EscapeJson(text)}\",");
        builder.Append($"\"disable_web_page_preview\":{(enableLinkPreview ? "false" : "true")}");
        if (!string.IsNullOrWhiteSpace(parseMode))
        {
            builder.Append($",\"parse_mode\":\"{EscapeJson(parseMode)}\"");
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static string BuildTelegramEditBody(string chatId, int messageId, string text, string? parseMode, bool enableLinkPreview)
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append($"\"chat_id\":\"{EscapeJson(chatId)}\",");
        builder.Append($"\"message_id\":{Math.Max(0, messageId)},");
        builder.Append($"\"text\":\"{EscapeJson(text)}\",");
        builder.Append($"\"disable_web_page_preview\":{(enableLinkPreview ? "false" : "true")}");
        if (!string.IsNullOrWhiteSpace(parseMode))
        {
            builder.Append($",\"parse_mode\":\"{EscapeJson(parseMode)}\"");
        }

        builder.Append("}");
        return builder.ToString();
    }

    private bool TryGetConfiguredRoute(out string botToken, out string chatId)
    {
        botToken = _runtimeSettings.GetTelegramBotToken() ?? string.Empty;
        chatId = _runtimeSettings.GetTelegramChatId() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(botToken) && !string.IsNullOrWhiteSpace(chatId);
    }

    private static int ExtractMessageId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                return 0;
            }

            if (!doc.RootElement.TryGetProperty("result", out var resultElement)
                || resultElement.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            if (!resultElement.TryGetProperty("message_id", out var messageIdElement)
                || !messageIdElement.TryGetInt32(out var messageId))
            {
                return 0;
            }

            return messageId;
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeTelegramText(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        normalized = NormalizeTelegramMarkdownTableSource(normalized);
        return string.IsNullOrWhiteSpace(normalized) ? "응답이 비어 있습니다." : normalized;
    }

    private static string NormalizeTelegramMarkdownTableSource(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0 || !LooksLikeTelegramMarkdownTableText(normalized))
        {
            return normalized;
        }

        normalized = Regex.Replace(normalized, @"\|\s+\|", "|\n|", RegexOptions.CultureInvariant);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            var separatorLine = CanonicalizeTelegramMarkdownTableSeparatorLine(rawLine);
            if (separatorLine.Length > 0)
            {
                output.Add(separatorLine);
                continue;
            }

            var rowLine = CanonicalizeTelegramMarkdownTableRow(rawLine);
            if (rowLine.Length > 0)
            {
                output.Add(rowLine);
                continue;
            }

            output.Add(rawLine ?? string.Empty);
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static bool LooksLikeTelegramMarkdownTableText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || !normalized.Contains("|", StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(
                normalized,
                @"\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]{2,}\s*(\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]{2,}\s*)+\|",
                RegexOptions.CultureInvariant))
        {
            return true;
        }

        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Count(line => line.Count(ch => ch == '|') >= 3) >= 2;
    }

    private static string CanonicalizeTelegramMarkdownTableRow(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (!trimmed.Contains("|", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var candidate = trimmed;
        if (!candidate.StartsWith("|", StringComparison.Ordinal))
        {
            candidate = $"|{candidate}";
        }

        if (!candidate.EndsWith("|", StringComparison.Ordinal))
        {
            candidate = $"{candidate}|";
        }

        var cells = candidate
            .Trim('|')
            .Split('|', StringSplitOptions.TrimEntries)
            .Select(cell => (cell ?? string.Empty).Trim())
            .ToArray();
        if (cells.Length < 2)
        {
            return string.Empty;
        }

        return "| " + string.Join(" | ", cells) + " |";
    }

    private static string CanonicalizeTelegramMarkdownTableSeparatorLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (!trimmed.Contains("|", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var candidate = trimmed;
        if (!candidate.StartsWith("|", StringComparison.Ordinal))
        {
            candidate = $"|{candidate}";
        }

        if (!candidate.EndsWith("|", StringComparison.Ordinal))
        {
            candidate = $"{candidate}|";
        }

        var rawCells = candidate
            .Trim('|')
            .Split('|', StringSplitOptions.TrimEntries)
            .Select(cell => (cell ?? string.Empty).Trim())
            .ToArray();
        if (rawCells.Length < 2)
        {
            return string.Empty;
        }

        var normalizedCells = new List<string>(rawCells.Length);
        foreach (var rawCell in rawCells)
        {
            var compact = Regex.Replace(rawCell, @"\s+", string.Empty, RegexOptions.CultureInvariant);
            compact = Regex.Replace(compact, @"[\u2014\u2013\u2011\u2212\u2500\u2012]", "-", RegexOptions.CultureInvariant);
            if (!Regex.IsMatch(compact, @"^:?-+:?$", RegexOptions.CultureInvariant))
            {
                return string.Empty;
            }

            var leadingColon = compact.StartsWith(":", StringComparison.Ordinal) ? ":" : string.Empty;
            var trailingColon = compact.EndsWith(":", StringComparison.Ordinal) ? ":" : string.Empty;
            var dashCount = Math.Max(3, compact.Count(ch => ch == '-'));
            normalizedCells.Add($"{leadingColon}{new string('-', dashCount)}{trailingColon}");
        }

        return "| " + string.Join(" | ", normalizedCells) + " |";
    }

    private static IReadOnlyList<string> SplitTelegramMessage(string text, int maxChars)
    {
        var normalized = NormalizeTelegramText(text);
        var safeMax = Math.Clamp(maxChars, 512, 4096);
        if (normalized.Length <= safeMax)
        {
            return new[] { normalized };
        }

        var chunks = new List<string>(4);
        var remaining = normalized;
        while (remaining.Length > safeMax)
        {
            var cut = remaining.LastIndexOf('\n', safeMax);
            if (cut < safeMax / 2)
            {
                cut = remaining.LastIndexOf(' ', safeMax);
            }

            if (cut < safeMax / 2)
            {
                cut = safeMax;
            }

            var head = remaining[..cut].Trim();
            if (!string.IsNullOrWhiteSpace(head))
            {
                chunks.Add(head);
            }

            remaining = remaining[cut..].TrimStart('\n', ' ', '\t');
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            chunks.Add(remaining);
        }

        return chunks.Count == 0 ? new[] { normalized } : chunks;
    }

    private static string AppendPreviewUrlToPlainText(string text, string url)
    {
        var body = NormalizeTelegramText(text);
        var normalizedUrl = (url ?? string.Empty).Trim();
        if (normalizedUrl.Length == 0)
        {
            return body;
        }

        if (body.Contains(normalizedUrl, StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        return $"{body}\n\n{normalizedUrl}";
    }

    private static string ExtractFirstUrlFromText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var match = Regex.Match(normalized, @"https?://[^\s<>\""]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Value.Trim();
    }

    private static string AppendSourceLinkHtml(string html, string sourceLinkHtml)
    {
        var body = (html ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            return string.Empty;
        }

        var sourceLink = (sourceLinkHtml ?? string.Empty).Trim();
        if (sourceLink.Length == 0)
        {
            return body;
        }

        if (body.Contains(sourceLink, StringComparison.Ordinal))
        {
            return body;
        }

        return $"{body}\n\n{sourceLink}";
    }

    private static string TryBuildSingleSourceLinkHtml(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var sourceLineMatches = Regex.Matches(
            normalized,
            @"(?mi)^\s*(?:<b>)?\s*(?:\*\*)?\s*출처\s*[:：]\s*(?:\*\*)?\s*(?:</b>)?\s*(?<sources>.+)$",
            RegexOptions.CultureInvariant
        );
        var sourceLine = sourceLineMatches.Count > 0
            ? sourceLineMatches[^1].Groups["sources"].Value.Trim()
            : TryExtractSourceLineFromHeadingBlock(normalized);
        if (sourceLine.Length == 0)
        {
            var fallbackUrl = Regex.Match(normalized, @"https?://[^\s<>\""]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (fallbackUrl.Success
                && Uri.TryCreate(fallbackUrl.Value.Trim(), UriKind.Absolute, out var fallbackUri)
                && (fallbackUri.Scheme == Uri.UriSchemeHttp || fallbackUri.Scheme == Uri.UriSchemeHttps)
                && !IsHiddenTelegramSourceHost(fallbackUri.Host))
            {
                return $"<a href=\"{EscapeHtmlForTelegram(fallbackUri.AbsoluteUri)}\">출처 링크: {EscapeHtmlForTelegram(fallbackUri.Host)}</a>\n{EscapeHtmlForTelegram(fallbackUri.AbsoluteUri)}";
            }

            return string.Empty;
        }

        var urlMatch = Regex.Match(sourceLine, @"https?://[^\s,\]]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (urlMatch.Success
            && Uri.TryCreate(urlMatch.Value.Trim(), UriKind.Absolute, out var explicitUri)
            && (explicitUri.Scheme == Uri.UriSchemeHttp || explicitUri.Scheme == Uri.UriSchemeHttps)
            && !IsHiddenTelegramSourceHost(explicitUri.Host))
        {
            var label = explicitUri.Host;
            return $"<a href=\"{EscapeHtmlForTelegram(explicitUri.AbsoluteUri)}\">출처 링크: {EscapeHtmlForTelegram(label)}</a>\n{EscapeHtmlForTelegram(explicitUri.AbsoluteUri)}";
        }

        var candidates = sourceLine
            .Split(new[] { ',', '·', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(candidate => Regex.Replace(candidate, @"^\s*[-•▪]\s*", string.Empty).Trim())
            .Where(candidate => candidate.Length > 0)
            .ToArray();
        if (candidates.Length == 0)
        {
            return string.Empty;
        }

        foreach (var candidateLabel in candidates)
        {
            if (IsHiddenTelegramSourceHost(candidateLabel))
            {
                continue;
            }

            if (!TryResolveSourceUrl(candidateLabel, out var resolvedUrl))
            {
                continue;
            }

            if (Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var resolvedUri)
                && IsHiddenTelegramSourceHost(resolvedUri.Host))
            {
                continue;
            }

            return $"<a href=\"{EscapeHtmlForTelegram(resolvedUrl)}\">출처 링크: {EscapeHtmlForTelegram(candidateLabel)}</a>\n{EscapeHtmlForTelegram(resolvedUrl)}";
        }

        return string.Empty;
    }

    private static bool IsHiddenTelegramSourceHost(string? hostOrLabel)
    {
        var normalized = (hostOrLabel ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        normalized = Regex.Replace(normalized, @"^https?://", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var cutIndex = normalized.IndexOfAny(new[] { '/', '?', '#', ' ' });
        if (cutIndex >= 0)
        {
            normalized = normalized[..cutIndex];
        }

        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        return normalized.Equals("vietnam.vn", StringComparison.Ordinal)
            || normalized.EndsWith(".vietnam.vn", StringComparison.Ordinal);
    }

    private static bool TryResolveSourceUrl(string sourceLabel, out string url)
    {
        url = string.Empty;
        var normalized = (sourceLabel ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (SourceHomeUrlByLabel.TryGetValue(normalized, out var mapped)
            && Uri.TryCreate(mapped, UriKind.Absolute, out _))
        {
            url = mapped;
            return true;
        }

        if (normalized.Equals("전자신문", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://www.etnews.com";
            return true;
        }

        if (normalized.Equals("한국경제", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://www.hankyung.com";
            return true;
        }

        if (normalized.Equals("AI타임스", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://www.aitimes.com";
            return true;
        }

        var domainMatch = Regex.Match(
            normalized,
            @"(?<domain>(?:[a-z0-9-]+\.)+[a-z]{2,})(?:/[^\s]*)?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        if (domainMatch.Success)
        {
            var domain = domainMatch.Groups["domain"].Value.Trim().TrimEnd('.');
            var candidate = $"https://{domain}";
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var domainUri))
            {
                url = domainUri.AbsoluteUri;
                return true;
            }
        }

        return false;
    }

    private static string TryExtractSourceLineFromHeadingBlock(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index += 1)
        {
            var current = NormalizeSourceHeadingLine(lines[index]);
            if (!Regex.IsMatch(current, @"^출처\s*[:：]?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                continue;
            }

            var candidates = new List<string>(8);
            for (var i = index + 1; i < lines.Length; i += 1)
            {
                var next = NormalizeSourceHeadingLine(lines[i]);
                if (next.Length == 0)
                {
                    break;
                }

                if (Regex.IsMatch(next, @"^(?:출처\s*링크)\s*[:：]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
                {
                    break;
                }

                next = Regex.Replace(next, @"^\s*[-•▪]\s*", string.Empty, RegexOptions.CultureInvariant).Trim();
                if (next.Length == 0)
                {
                    continue;
                }

                candidates.Add(next);
                if (candidates.Count >= 8)
                {
                    break;
                }
            }

            if (candidates.Count > 0)
            {
                return string.Join(", ", candidates);
            }
        }

        return string.Empty;
    }

    private static string NormalizeSourceHeadingLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var normalized = line.Trim();
        normalized = Regex.Replace(normalized, @"</?b>", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal);
        return normalized.Trim();
    }

    private static string BuildTelegramHtmlWithAlignedTables(string text)
    {
        if (!TryExtractMarkdownTables(text, out var tables) || tables.Count == 0)
        {
            return string.Empty;
        }

        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var builder = new StringBuilder(normalized.Length + 512);
        var cursor = 0;

        foreach (var table in tables.OrderBy(item => item.StartLine))
        {
            if (table.StartLine < cursor || table.StartLine >= lines.Length)
            {
                continue;
            }

            AppendRenderedTelegramLines(builder, lines, cursor, table.StartLine);
            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append('\n');
            }

            var renderedTableHtml = BuildTelegramRenderedTableHtml(table.Rows);
            if (string.IsNullOrWhiteSpace(renderedTableHtml))
            {
                AppendRenderedTelegramLines(builder, lines, table.StartLine, Math.Min(lines.Length, table.EndLine + 1));
            }
            else
            {
                builder.Append(renderedTableHtml);
                if (table.EndLine + 1 < lines.Length)
                {
                    builder.Append('\n');
                }
            }

            cursor = Math.Min(lines.Length, table.EndLine + 1);
        }

        AppendRenderedTelegramLines(builder, lines, cursor, lines.Length);
        return builder.ToString().Trim();
    }

    private static string BuildTelegramHtmlWithLabelStyling(string text)
    {
        return BuildTelegramHtmlSegment(text);
    }

    private static void EnsureBlankLineBeforeStyledLine(StringBuilder builder)
    {
        if (builder == null || builder.Length == 0)
        {
            return;
        }

        if (builder[^1] != '\n')
        {
            builder.Append('\n');
        }

        if (builder.Length < 2 || builder[^2] != '\n')
        {
            builder.Append('\n');
        }
    }

    private static bool TryFormatTelegramStyledLabelLine(string line, out string formatted)
    {
        formatted = string.Empty;
        if (!TryParseTelegramStructuredLabelLine(
                line,
                out var lead,
                out var prefix,
                out var label,
                out var value))
        {
            return false;
        }
        var escapedValue = EscapeHtmlForTelegram(value);

        if (label.Equals("제목", StringComparison.OrdinalIgnoreCase))
        {
            formatted = value.Length == 0
                ? $"{lead}{prefix}<b>제목:</b>"
                : $"{lead}{prefix}<b>제목:</b> <b>{escapedValue}</b>";
            return true;
        }

        if (label.Equals("내용", StringComparison.OrdinalIgnoreCase))
        {
            formatted = value.Length == 0
                ? $"{lead}{prefix}<b>내용:</b>"
                : $"{lead}{prefix}<b>내용:</b> {escapedValue}";
            return true;
        }

        if (label.Equals("요약", StringComparison.OrdinalIgnoreCase))
        {
            formatted = value.Length == 0
                ? $"{lead}{prefix}<b>요약:</b>"
                : $"{lead}{prefix}<b>요약:</b> {escapedValue}";
            return true;
        }

        if (label.Equals("핵심", StringComparison.OrdinalIgnoreCase))
        {
            formatted = value.Length == 0
                ? $"{lead}{prefix}<b>핵심:</b>"
                : $"{lead}{prefix}<b>핵심:</b> {escapedValue}";
            return true;
        }

        if (label.Equals("출처", StringComparison.OrdinalIgnoreCase))
        {
            formatted = value.Length == 0
                ? $"{lead}{prefix}<b>출처:</b>"
                : $"{lead}{prefix}<b>출처:</b> {escapedValue}";
            return true;
        }

        return false;
    }

    private static bool TryFormatTelegramStyledCategoryLine(string line, out string formatted)
    {
        formatted = string.Empty;
        if (!TryParseTelegramStructuredLabelLine(
                line,
                out var lead,
                out var prefix,
                out var label,
                out var value))
        {
            return false;
        }

        if (label.Equals("제목", StringComparison.OrdinalIgnoreCase)
            || label.Equals("내용", StringComparison.OrdinalIgnoreCase)
            || label.Equals("요약", StringComparison.OrdinalIgnoreCase)
            || label.Equals("핵심", StringComparison.OrdinalIgnoreCase)
            || label.Equals("출처", StringComparison.OrdinalIgnoreCase)
            || label.Equals("출처링크", StringComparison.OrdinalIgnoreCase)
            || label.Equals("http", StringComparison.OrdinalIgnoreCase)
            || label.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var escapedLabel = EscapeHtmlForTelegram(label);
        var escapedValue = EscapeHtmlForTelegram(value);
        formatted = value.Length == 0
            ? $"{lead}{prefix}<b>{escapedLabel}:</b>"
            : $"{lead}{prefix}<b>{escapedLabel}:</b> {escapedValue}";
        return true;
    }

    private static bool TryParseTelegramStructuredLabelLine(
        string line,
        out string lead,
        out string prefix,
        out string label,
        out string value)
    {
        lead = string.Empty;
        prefix = string.Empty;
        label = string.Empty;
        value = string.Empty;

        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0
            || normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LooksLikeStandaloneTelegramTimeLine(normalized))
        {
            return false;
        }

        var match = Regex.Match(
            normalized,
            @"^(?<lead>(?:[-•▪]\s+)?)\s*(?<prefix>(?:No\.\d+|\d+[.)])\s*)?(?:(?:\*\*(?<labelMd>[A-Za-z가-힣0-9()'‘’,.&+_\-/\s]{1,120})\s*[:：]\*\*)|(?<labelPlain>[A-Za-z가-힣0-9()'‘’,.&+_\-/\s]{1,120})\s*[:：])\s*(?<value>.*)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        var parsedLabel = match.Groups["labelMd"].Success
            ? match.Groups["labelMd"].Value
            : match.Groups["labelPlain"].Value;
        parsedLabel = Regex.Replace(parsedLabel, @"\s{2,}", " ").Trim();
        if (parsedLabel.Length == 0)
        {
            return false;
        }

        lead = EscapeHtmlForTelegram(match.Groups["lead"].Value);
        prefix = EscapeHtmlForTelegram(match.Groups["prefix"].Value);
        label = parsedLabel;
        value = NormalizeTelegramStructuredLabelValue(match.Groups["value"].Value);
        return true;
    }

    private static bool LooksLikeStandaloneTelegramTimeLine(string line)
    {
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(
            normalized,
            @"^(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?(?:(?:\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}(?:[-/.]\d{2,4})?)\s+)?\d{1,2}\s*:\s*\d{2}(?:\s*:\s*\d{2})?(?:\s*(?:AM|PM|am|pm))?$",
            RegexOptions.CultureInvariant
        );
    }

    private static string BuildTelegramRenderedTableHtml(IReadOnlyList<string[]> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            return string.Empty;
        }

        if (ShouldUseTelegramMobileKeyValueTable(rows))
        {
            return BuildTelegramMobileKeyValueTableHtml(rows);
        }

        var alignedTable = BuildAlignedTelegramTableText(rows);
        if (string.IsNullOrWhiteSpace(alignedTable))
        {
            return string.Empty;
        }

        return $"<pre><code>{EscapeHtmlForTelegram(alignedTable)}</code></pre>";
    }

    private static void AppendRenderedTelegramLines(StringBuilder builder, string[] lines, int start, int endExclusive)
    {
        var safeStart = Math.Max(0, start);
        var safeEnd = Math.Clamp(endExclusive, safeStart, lines.Length);
        if (safeStart >= safeEnd)
        {
            return;
        }

        var segment = string.Join('\n', lines.Skip(safeStart).Take(safeEnd - safeStart));
        var rendered = BuildTelegramHtmlSegment(segment);
        if (!string.IsNullOrWhiteSpace(rendered))
        {
            builder.Append(rendered);
        }
    }

    private static IReadOnlyList<string> SplitTelegramHtmlMessageSafely(string html, int maxChars)
    {
        var normalized = NormalizeTelegramText(html);
        var safeMax = Math.Clamp(maxChars, 512, 4096);
        if (normalized.Length <= safeMax)
        {
            return new[] { normalized };
        }

        var parts = Regex.Split(
            normalized,
            "(<pre><code>[\\s\\S]*?</code></pre>)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        var segments = new List<string>(parts.Length + 8);
        foreach (var raw in parts)
        {
            var part = raw ?? string.Empty;
            if (part.Length == 0)
            {
                continue;
            }

            var isPreCodeBlock = part.StartsWith("<pre><code>", StringComparison.OrdinalIgnoreCase)
                                 && part.EndsWith("</code></pre>", StringComparison.OrdinalIgnoreCase);
            if (part.Length <= safeMax)
            {
                segments.Add(part);
                continue;
            }

            if (isPreCodeBlock)
            {
                return Array.Empty<string>();
            }

            foreach (var split in SplitTelegramTextSegment(part, safeMax))
            {
                if (!string.IsNullOrWhiteSpace(split))
                {
                    segments.Add(split);
                }
            }
        }

        if (segments.Count == 0)
        {
            return Array.Empty<string>();
        }

        var chunks = new List<string>(4);
        var current = new StringBuilder(safeMax + 64);
        foreach (var segment in segments)
        {
            if (segment.Length > safeMax)
            {
                return Array.Empty<string>();
            }

            if (current.Length > 0 && current.Length + segment.Length > safeMax)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            current.Append(segment);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks.Count == 0 ? Array.Empty<string>() : chunks.Where(x => x.Length > 0).ToArray();
    }

    private static IEnumerable<string> SplitTelegramTextSegment(string text, int maxChars)
    {
        var remaining = (text ?? string.Empty).Trim();
        if (remaining.Length == 0)
        {
            yield break;
        }

        while (remaining.Length > maxChars)
        {
            var cut = remaining.LastIndexOf('\n', maxChars);
            if (cut < maxChars / 2)
            {
                cut = remaining.LastIndexOf(' ', maxChars);
            }

            if (cut < maxChars / 2)
            {
                cut = maxChars;
            }

            var head = remaining[..cut].Trim();
            if (!string.IsNullOrWhiteSpace(head))
            {
                yield return head;
            }

            remaining = remaining[cut..].TrimStart('\n', ' ', '\t');
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            yield return remaining;
        }
    }

    private static bool TryExtractMarkdownTables(string text, out IReadOnlyList<TelegramMarkdownTableBlock> tables)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var blocks = new List<TelegramMarkdownTableBlock>(2);
        var index = 0;

        while (index + 1 < lines.Length)
        {
            if (!IsTelegramMarkdownTableRow(lines[index]) || !IsTelegramMarkdownTableSeparatorRow(lines[index + 1]))
            {
                index += 1;
                continue;
            }

            var start = index;
            var rows = new List<string[]>(8)
            {
                ParseTelegramMarkdownTableCells(lines[index])
            };

            index += 2;
            while (index < lines.Length && IsTelegramMarkdownTableRow(lines[index]))
            {
                rows.Add(ParseTelegramMarkdownTableCells(lines[index]));
                index += 1;
            }

            if (rows.Count >= 2)
            {
                blocks.Add(new TelegramMarkdownTableBlock(start, index - 1, rows));
            }
        }

        tables = blocks;
        return blocks.Count > 0;
    }

    private static bool IsTelegramMarkdownTableRow(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            return false;
        }

        var cells = trimmed.Trim('|').Split('|', StringSplitOptions.TrimEntries);
        return cells.Length >= 2;
    }

    private static bool IsTelegramMarkdownTableSeparatorRow(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (!IsTelegramMarkdownTableRow(trimmed))
        {
            return false;
        }

        var cells = trimmed.Trim('|').Split('|', StringSplitOptions.TrimEntries);
        if (cells.Length < 2)
        {
            return false;
        }

        return cells.All(cell => Regex.IsMatch(cell.Trim(), @"^:?-{2,}:?$", RegexOptions.CultureInvariant));
    }

    private static string[] ParseTelegramMarkdownTableCells(string line)
    {
        return (line ?? string.Empty)
            .Trim()
            .Trim('|')
            .Split('|', StringSplitOptions.TrimEntries)
            .Select(cell => cell.Trim())
            .ToArray();
    }

    private static bool ShouldUseTelegramMobileKeyValueTable(IReadOnlyList<string[]> rows)
    {
        if (rows == null || rows.Count < 2)
        {
            return false;
        }

        var columnCount = rows.Max(row => row?.Length ?? 0);
        if (columnCount < 2)
        {
            return false;
        }

        if (columnCount >= 3)
        {
            return true;
        }

        if (columnCount == 2)
        {
            var values = rows
                .Skip(1)
                .Select(row => row != null && row.Length > 1 ? row[1] : string.Empty)
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToArray();
            if (values.Length == 0)
            {
                return false;
            }

            var maxWidth = values.Max(GetDisplayWidth);
            var avgWidth = values.Sum(GetDisplayWidth) / values.Length;
            if (maxWidth >= 42 || avgWidth >= 30)
            {
                return true;
            }

            return values.Any(cell =>
                GetDisplayWidth(cell) >= 26
                && (cell.Contains('.', StringComparison.Ordinal)
                    || cell.Contains('다', StringComparison.Ordinal)
                    || cell.Contains(',', StringComparison.Ordinal)));
        }

        foreach (var row in rows.Skip(1))
        {
            if (row == null || row.Length == 0)
            {
                continue;
            }

            var joinedWidth = GetDisplayWidth(string.Join(" ", row.Skip(1)));
            if (joinedWidth >= 56)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildTelegramMobileKeyValueTableHtml(IReadOnlyList<string[]> rows)
    {
        if (rows == null || rows.Count < 2)
        {
            return string.Empty;
        }

        var columnCount = rows.Max(row => row?.Length ?? 0);
        if (columnCount < 2)
        {
            return string.Empty;
        }

        var headers = rows[0] ?? Array.Empty<string>();
        var keyColumn = ResolveTelegramMobileKeyColumn(headers, columnCount);
        var builder = new StringBuilder();
        var itemIndex = 0;
        foreach (var row in rows.Skip(1))
        {
            if (row == null)
            {
                continue;
            }

            var key = keyColumn < row.Length ? row[keyColumn] : string.Empty;
            key = string.IsNullOrWhiteSpace(key) ? $"항목 {itemIndex + 1}" : key.Trim();
            itemIndex += 1;

            builder.Append("<b>▪ ");
            builder.Append(EscapeHtmlForTelegram(key));
            builder.AppendLine("</b>");

            if (columnCount == 2)
            {
                var value = row.Length > 1 ? row[1] : string.Empty;
                value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
                builder.AppendLine(EscapeHtmlForTelegram(value));
                builder.AppendLine();
                continue;
            }

            for (var col = 0; col < columnCount; col += 1)
            {
                if (col == keyColumn)
                {
                    continue;
                }

                var value = col < row.Length ? row[col] : string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var header = col < headers.Length ? headers[col] : string.Empty;
                if (string.IsNullOrWhiteSpace(header))
                {
                    header = $"항목 {col + 1}";
                }

                if (col == 0 && keyColumn == 1 && IsTelegramOrdinalHeader(header))
                {
                    continue;
                }

                builder.Append("• <b>");
                builder.Append(EscapeHtmlForTelegram(header.Trim()));
                builder.Append(":</b> ");
                builder.AppendLine(EscapeHtmlForTelegram(value.Trim()));
            }
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static int ResolveTelegramMobileKeyColumn(string[] headers, int columnCount)
    {
        if (columnCount < 2)
        {
            return 0;
        }

        var firstHeader = headers != null && headers.Length > 0 ? headers[0] : string.Empty;
        return IsTelegramOrdinalHeader(firstHeader) ? 1 : 0;
    }

    private static bool IsTelegramOrdinalHeader(string header)
    {
        var normalized = (header ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(
            normalized,
            @"^(순번|번호|순서|No\.?|no\.?|index)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    private static string BuildAlignedTelegramTableText(IReadOnlyList<string[]> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            return string.Empty;
        }

        var columnCount = rows.Max(row => row?.Length ?? 0);
        if (columnCount <= 0)
        {
            return string.Empty;
        }

        var widths = new int[columnCount];
        foreach (var row in rows)
        {
            if (row == null)
            {
                continue;
            }

            for (var col = 0; col < columnCount; col += 1)
            {
                var cell = col < row.Length ? row[col] : string.Empty;
                widths[col] = Math.Max(widths[col], GetDisplayWidth(cell));
            }
        }

        for (var col = 0; col < columnCount; col += 1)
        {
            widths[col] = Math.Max(3, widths[col]);
        }

        var lines = new List<string>(rows.Count + 1);
        var header = rows[0];
        lines.Add(RenderTelegramTableLine(header, widths));
        lines.Add(RenderTelegramTableSeparatorLine(widths));
        foreach (var row in rows.Skip(1))
        {
            lines.Add(RenderTelegramTableLine(row, widths));
        }

        return string.Join('\n', lines).Trim();
    }

    private static string RenderTelegramTableLine(string[]? row, int[] widths)
    {
        var columns = new string[widths.Length];
        for (var col = 0; col < widths.Length; col += 1)
        {
            var cell = row != null && col < row.Length ? row[col] : string.Empty;
            columns[col] = PadRightDisplayWidth(cell, widths[col]);
        }

        return "| " + string.Join(" | ", columns) + " |";
    }

    private static string RenderTelegramTableSeparatorLine(int[] widths)
    {
        var separators = widths.Select(width => new string('-', Math.Max(3, width)));
        return "| " + string.Join(" | ", separators) + " |";
    }

    private static string PadRightDisplayWidth(string text, int totalWidth)
    {
        var safe = (text ?? string.Empty).Replace("\t", " ", StringComparison.Ordinal);
        var displayWidth = GetDisplayWidth(safe);
        var padding = totalWidth - displayWidth;
        return padding > 0 ? safe + new string(' ', padding) : safe;
    }

    private static int GetDisplayWidth(string text)
    {
        var safe = text ?? string.Empty;
        var width = 0;
        foreach (var rune in safe.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format
                or UnicodeCategory.Control)
            {
                continue;
            }

            width += IsWideRune(rune) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWideRune(Rune rune)
    {
        var value = rune.Value;
        if (value is >= 0x1100 and <= 0x11FF) return true;   // Hangul Jamo
        if (value is >= 0x2E80 and <= 0xA4CF) return true;   // CJK/한자/기호
        if (value is >= 0xAC00 and <= 0xD7A3) return true;   // Hangul Syllables
        if (value is >= 0xF900 and <= 0xFAFF) return true;   // CJK Compatibility Ideographs
        if (value is >= 0xFE10 and <= 0xFE6F) return true;   // Vertical/Compatibility Forms
        if (value is >= 0xFF01 and <= 0xFF60) return true;   // Fullwidth Forms
        if (value is >= 0xFFE0 and <= 0xFFE6) return true;   // Fullwidth symbol variants
        if (value is >= 0x1F300 and <= 0x1FAFF) return true; // Emoji ranges
        if (value is >= 0x20000 and <= 0x3FFFD) return true; // CJK Extension
        return false;
    }

    private static string EscapeHtmlForTelegram(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string ConvertMarkdownToTelegramHtml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "응답이 비어 있습니다.";
        }

        var raw = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var codeBlocks = new List<string>();
        raw = Regex.Replace(raw, @"```(?:[^\n`]*)\n([\s\S]*?)```", match =>
        {
            var code = match.Groups[1].Value.TrimEnd();
            var token = $"@@CODEBLOCK{codeBlocks.Count}@@";
            codeBlocks.Add($"<pre><code>{EscapeHtmlForTelegram(code)}</code></pre>");
            return token;
        }, RegexOptions.Multiline);

        var html = EscapeHtmlForTelegram(raw);
        html = Regex.Replace(html, @"(?m)^#{1,6}\s+(.+)$", "<b>$1</b>");
        html = Regex.Replace(html, @"(?m)^&gt;\s?(.*)$", "▎ $1");
        html = Regex.Replace(html, @"(?m)^(\*{3,}|-{3,}|_{3,})\s*$", "────────");
        html = Regex.Replace(html, @"(?m)^\s*[-*+]\s+(.+)$", "• $1");
        html = Regex.Replace(html, @"(?m)^\s*([0-9]+)\.\s+(.+)$", "$1. $2");
        html = Regex.Replace(html, @"\*\*(.+?)\*\*", "<b>$1</b>");
        html = Regex.Replace(html, @"__(.+?)__", "<b>$1</b>");
        html = Regex.Replace(html, @"~~(.+?)~~", "<s>$1</s>");
        html = Regex.Replace(html, @"(?<!\*)\*(?!\s)(.+?)(?<!\s)\*(?!\*)", "<i>$1</i>");
        html = Regex.Replace(html, @"(?<!_)_(?!\s)(.+?)(?<!\s)_(?!_)", "<i>$1</i>");
        html = Regex.Replace(html, @"\[(.+?)\]\((https?://[^\s\)]+)\)", "<a href=\"$2\">$1</a>", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"`([^`\n]+)`", "<code>$1</code>");
        html = Regex.Replace(html, @"\[\^([^\]]+)\]", "<sup>[$1]</sup>");
        html = Regex.Replace(html, @"(?m)^\[\^([^\]]+)\]:\s*(.+)$", "<i>[주석 $1]</i> $2");
        html = Regex.Replace(html, @"(?m)^\|(.+)\|$", "<code>|$1|</code>");

        for (var i = 0; i < codeBlocks.Count; i++)
        {
            html = html.Replace($"@@CODEBLOCK{i}@@", codeBlocks[i], StringComparison.Ordinal);
        }

        return html.Trim();
    }

    private static string BuildTelegramHtmlSegment(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var builder = new StringBuilder(normalized.Length + 128);

        for (var i = 0; i < lines.Length; i += 1)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            var line = (lines[i] ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryFormatTelegramStyledLabelLine(line, out var formatted))
            {
                EnsureBlankLineBeforeStyledLine(builder);
                builder.Append(formatted);
                continue;
            }

            if (TryFormatTelegramStyledCategoryLine(line, out var categoryFormatted))
            {
                EnsureBlankLineBeforeStyledLine(builder);
                builder.Append(categoryFormatted);
                continue;
            }

            builder.Append(ConvertMarkdownToTelegramHtml(line));
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeTelegramStructuredLabelValue(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"^(?:</?b>|\*\*)+\s*", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*(?:</?b>|\*\*)+$", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim();
        return normalized;
    }

    private sealed record TelegramMarkdownTableBlock(
        int StartLine,
        int EndLine,
        IReadOnlyList<string[]> Rows
    );

    private bool ShouldLogSendError()
    {
        return ShouldLog(ref _lastSendErrorLogUtc);
    }

    private bool ShouldLogGetUpdatesError()
    {
        return ShouldLog(ref _lastGetUpdatesErrorLogUtc);
    }

    private bool ShouldLog(ref DateTimeOffset lastLogTimeUtc)
    {
        lock (_errorLogLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - lastLogTimeUtc < TimeSpan.FromSeconds(30))
            {
                return false;
            }

            lastLogTimeUtc = now;
            return true;
        }
    }

    private async Task<HttpResponseMessage?> GetWithRetryAsync(string endpoint, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= TelegramApiMaxAttempts; attempt += 1)
        {
            try
            {
                var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (attempt >= TelegramApiMaxAttempts || !ShouldRetryTelegramRequest(response.StatusCode, body))
                {
                    return response;
                }

                response.Dispose();
                await DelayTelegramRetryAsync(attempt, response.StatusCode, body, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < TelegramApiMaxAttempts)
            {
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (HttpRequestException) when (attempt < TelegramApiMaxAttempts)
            {
                await DelayTelegramRetryAsync(attempt, null, string.Empty, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool ShouldRetryTelegramRequest(HttpStatusCode? statusCode, string errorBody)
    {
        if (statusCode == null)
        {
            return true;
        }

        var numeric = (int)statusCode.Value;
        return numeric == 429 || numeric >= 500;
    }

    private static async Task DelayTelegramRetryAsync(int attempt, HttpStatusCode? statusCode, string errorBody, CancellationToken cancellationToken)
    {
        var delay = TryGetTelegramRetryAfter(errorBody, out var retryAfter)
            ? retryAfter
            : TimeSpan.FromMilliseconds(Math.Min(1000 * Math.Max(1, 1 << (attempt - 1)), 8000));

        if (delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromMilliseconds(750);
        }

        await Task.Delay(delay, cancellationToken);
    }

    private static bool TryGetTelegramRetryAfter(string errorBody, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        var normalized = (errorBody ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (!doc.RootElement.TryGetProperty("parameters", out var parametersElement)
                || parametersElement.ValueKind != JsonValueKind.Object
                || !parametersElement.TryGetProperty("retry_after", out var retryElement))
            {
                return false;
            }

            int seconds;
            if (retryElement.ValueKind == JsonValueKind.Number && retryElement.TryGetInt32(out seconds))
            {
                delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));
                return true;
            }

            if (retryElement.ValueKind == JsonValueKind.String
                && int.TryParse(retryElement.GetString(), out seconds))
            {
                delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private async Task<InputAttachment?> DownloadAttachmentAsync(
        string botToken,
        string fileId,
        string fallbackName,
        string fallbackMimeType,
        bool isImage,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var filePath = await ResolveTelegramFilePathAsync(botToken, fileId, cancellationToken);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var endpoint = $"https://api.telegram.org/file/bot{botToken}/{filePath}";
            using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > MaxAttachmentBytes)
            {
                return null;
            }

            var name = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = fallbackName;
            }

            var mimeType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                mimeType = fallbackMimeType;
            }

            return new InputAttachment(
                name,
                mimeType ?? "application/octet-stream",
                Convert.ToBase64String(bytes),
                bytes.Length,
                isImage || (mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            );
        }
        catch
        {
            return null;
        }
    }

    private async Task<InputAttachment?> DownloadTelegramAudioAttachmentAsync(
        string botToken,
        JsonElement audioElement,
        string fallbackName,
        string fallbackMimeType,
        CancellationToken cancellationToken
    )
    {
        if (!audioElement.TryGetProperty("file_id", out var fileIdElement)
            || fileIdElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var fileId = fileIdElement.GetString();
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        var mimeType = audioElement.TryGetProperty("mime_type", out var mimeElement)
                       && mimeElement.ValueKind == JsonValueKind.String
            ? (mimeElement.GetString() ?? fallbackMimeType)
            : fallbackMimeType;
        return await DownloadAttachmentAsync(
            botToken,
            fileId.Trim(),
            fallbackName,
            mimeType,
            false,
            cancellationToken
        );
    }

    private async Task<string?> ResolveTelegramFilePathAsync(string botToken, string fileId, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = $"https://api.telegram.org/bot{botToken}/getFile?file_id={Uri.EscapeDataString(fileId)}";
            using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("result", out var resultElement)
                || resultElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!resultElement.TryGetProperty("file_path", out var filePathElement)
                || filePathElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return filePathElement.GetString();
        }
        catch
        {
            return null;
        }
    }
}

public sealed record TelegramUpdate(
    long UpdateId,
    string? Text,
    string? ChatId,
    string? FromUserId,
    IReadOnlyList<InputAttachment>? Attachments
);
