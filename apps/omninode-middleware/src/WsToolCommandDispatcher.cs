using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsToolCommandDispatcher
{
    internal delegate Task SendSessionsListResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        IReadOnlyList<string> kinds,
        int? limit,
        int? activeMinutes,
        int? messageLimit,
        string? search,
        string? scope,
        string? mode,
        SessionListToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendSessionsHistoryResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string? requestedSessionKey,
        int? limit,
        bool? includeTools,
        SessionHistoryToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendSessionsSendResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string? requestedSessionKey,
        int? timeoutSeconds,
        SessionSendToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendSessionsSpawnResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedTask,
        string? requestedLabel,
        string? requestedRuntime,
        int? requestedRunTimeoutSeconds,
        int? requestedTimeoutSeconds,
        bool? requestedThread,
        string? requestedMode,
        SessionSpawnToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronStatusResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CronToolStatusResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronListResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        bool includeDisabled,
        int? requestedLimit,
        int? requestedOffset,
        CronToolListResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronRunResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        string? requestedRunMode,
        CronToolRunResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronWakeResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string? requestedMode,
        int requestedTextLength,
        CronToolWakeResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronRunsResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        int? requestedLimit,
        int? requestedOffset,
        CronToolRunsResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronAddResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CronToolAddResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronUpdateResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        CronToolUpdateResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCronRemoveResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        CronToolRemoveResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendBrowserResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedAction,
        string? requestedUrl,
        string? requestedProfile,
        string? requestedTargetId,
        int? requestedLimit,
        BrowserToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCanvasResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedAction,
        string? requestedTarget,
        string? requestedUrl,
        string? requestedProfile,
        string? requestedOutputFormat,
        int? requestedMaxWidth,
        CanvasToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendNodesResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedAction,
        string? requestedNode,
        string? requestedRequestId,
        string? requestedProfile,
        string? requestedInvokeCommand,
        NodesToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendTelegramStubResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedText,
        string responseText,
        bool ok,
        string status,
        string? error,
        CancellationToken cancellationToken,
        SearchAnswerGuardFailure? guardFailure = null,
        int retryAttempt = 0,
        int retryMaxAttempts = 0,
        string retryStopReason = "-"
    );

    internal delegate Task SendWebSearchResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string query,
        int? count,
        string? freshness,
        WebSearchToolResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendWebFetchResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedUrl,
        string? requestedExtractMode,
        int? requestedMaxChars,
        WebFetchToolResult result,
        CancellationToken cancellationToken
    );

    private readonly IToolApplicationService _toolService;
    private readonly ICommandExecutionService _commandExecutionService;
    private readonly Func<string, bool> _allowCommand;
    private readonly Func<string?, string?> _buildCronAddJobJson;
    private readonly Func<string?, string?> _buildCronUpdatePatchJson;
    private readonly SendSessionsListResultDelegate _sendSessionsListResultAsync;
    private readonly SendSessionsHistoryResultDelegate _sendSessionsHistoryResultAsync;
    private readonly SendSessionsSendResultDelegate _sendSessionsSendResultAsync;
    private readonly SendSessionsSpawnResultDelegate _sendSessionsSpawnResultAsync;
    private readonly SendCronStatusResultDelegate _sendCronStatusResultAsync;
    private readonly SendCronListResultDelegate _sendCronListResultAsync;
    private readonly SendCronRunResultDelegate _sendCronRunResultAsync;
    private readonly SendCronWakeResultDelegate _sendCronWakeResultAsync;
    private readonly SendCronRunsResultDelegate _sendCronRunsResultAsync;
    private readonly SendCronAddResultDelegate _sendCronAddResultAsync;
    private readonly SendCronUpdateResultDelegate _sendCronUpdateResultAsync;
    private readonly SendCronRemoveResultDelegate _sendCronRemoveResultAsync;
    private readonly SendBrowserResultDelegate _sendBrowserResultAsync;
    private readonly SendCanvasResultDelegate _sendCanvasResultAsync;
    private readonly SendNodesResultDelegate _sendNodesResultAsync;
    private readonly SendTelegramStubResultDelegate _sendTelegramStubResultAsync;
    private readonly SendWebSearchResultDelegate _sendWebSearchResultAsync;
    private readonly SendWebFetchResultDelegate _sendWebFetchResultAsync;

    public WsToolCommandDispatcher(
        IToolApplicationService toolService,
        ICommandExecutionService commandExecutionService,
        Func<string, bool> allowCommand,
        Func<string?, string?> buildCronAddJobJson,
        Func<string?, string?> buildCronUpdatePatchJson,
        SendSessionsListResultDelegate sendSessionsListResultAsync,
        SendSessionsHistoryResultDelegate sendSessionsHistoryResultAsync,
        SendSessionsSendResultDelegate sendSessionsSendResultAsync,
        SendSessionsSpawnResultDelegate sendSessionsSpawnResultAsync,
        SendCronStatusResultDelegate sendCronStatusResultAsync,
        SendCronListResultDelegate sendCronListResultAsync,
        SendCronRunResultDelegate sendCronRunResultAsync,
        SendCronWakeResultDelegate sendCronWakeResultAsync,
        SendCronRunsResultDelegate sendCronRunsResultAsync,
        SendCronAddResultDelegate sendCronAddResultAsync,
        SendCronUpdateResultDelegate sendCronUpdateResultAsync,
        SendCronRemoveResultDelegate sendCronRemoveResultAsync,
        SendBrowserResultDelegate sendBrowserResultAsync,
        SendCanvasResultDelegate sendCanvasResultAsync,
        SendNodesResultDelegate sendNodesResultAsync,
        SendTelegramStubResultDelegate sendTelegramStubResultAsync,
        SendWebSearchResultDelegate sendWebSearchResultAsync,
        SendWebFetchResultDelegate sendWebFetchResultAsync
    )
    {
        _toolService = toolService;
        _commandExecutionService = commandExecutionService;
        _allowCommand = allowCommand;
        _buildCronAddJobJson = buildCronAddJobJson;
        _buildCronUpdatePatchJson = buildCronUpdatePatchJson;
        _sendSessionsListResultAsync = sendSessionsListResultAsync;
        _sendSessionsHistoryResultAsync = sendSessionsHistoryResultAsync;
        _sendSessionsSendResultAsync = sendSessionsSendResultAsync;
        _sendSessionsSpawnResultAsync = sendSessionsSpawnResultAsync;
        _sendCronStatusResultAsync = sendCronStatusResultAsync;
        _sendCronListResultAsync = sendCronListResultAsync;
        _sendCronRunResultAsync = sendCronRunResultAsync;
        _sendCronWakeResultAsync = sendCronWakeResultAsync;
        _sendCronRunsResultAsync = sendCronRunsResultAsync;
        _sendCronAddResultAsync = sendCronAddResultAsync;
        _sendCronUpdateResultAsync = sendCronUpdateResultAsync;
        _sendCronRemoveResultAsync = sendCronRemoveResultAsync;
        _sendBrowserResultAsync = sendBrowserResultAsync;
        _sendCanvasResultAsync = sendCanvasResultAsync;
        _sendNodesResultAsync = sendNodesResultAsync;
        _sendTelegramStubResultAsync = sendTelegramStubResultAsync;
        _sendWebSearchResultAsync = sendWebSearchResultAsync;
        _sendWebFetchResultAsync = sendWebFetchResultAsync;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        string sessionId,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "sessions_list")
        {
            var result = _toolService.ListSessions(
                message.Kinds,
                message.Limit,
                message.ActiveMinutes,
                message.MessageLimit,
                message.Search,
                message.Scope,
                message.Mode
            );
            await _sendSessionsListResultAsync(
                socket,
                sendLock,
                message.Kinds,
                message.Limit,
                message.ActiveMinutes,
                message.MessageLimit,
                message.Search,
                message.Scope,
                message.Mode,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "sessions_history")
        {
            var result = _toolService.GetSessionHistory(
                message.SessionKey,
                message.Limit,
                message.IncludeTools ?? false
            );
            await _sendSessionsHistoryResultAsync(
                socket,
                sendLock,
                message.SessionKey,
                message.Limit,
                message.IncludeTools,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "sessions_send")
        {
            var outboundMessage = string.IsNullOrWhiteSpace(message.Message)
                ? message.Text
                : message.Message;
            if (string.IsNullOrWhiteSpace(outboundMessage))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"message is required\"}", cancellationToken);
                return true;
            }

            var result = _toolService.SendToSession(
                message.SessionKey,
                outboundMessage.Trim(),
                message.TimeoutSeconds
            );
            await _sendSessionsSendResultAsync(
                socket,
                sendLock,
                message.SessionKey,
                message.TimeoutSeconds,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "sessions_spawn")
        {
            var requestedTask = string.IsNullOrWhiteSpace(message.SpawnTask)
                ? (string.IsNullOrWhiteSpace(message.Text) ? message.Message : message.Text)
                : message.SpawnTask;
            if (string.IsNullOrWhiteSpace(requestedTask))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"task is required\"}", cancellationToken);
                return true;
            }

            var trimmedTask = requestedTask.Trim();
            var result = _toolService.SpawnSession(
                trimmedTask,
                message.Label,
                message.Runtime,
                message.RunTimeoutSeconds,
                message.TimeoutSeconds,
                message.Thread,
                message.Mode
            );
            await _sendSessionsSpawnResultAsync(
                socket,
                sendLock,
                trimmedTask,
                message.Label,
                message.Runtime,
                message.RunTimeoutSeconds,
                message.TimeoutSeconds,
                message.Thread,
                message.Mode,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "cron")
        {
            var action = (message.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"cron action is required\"}", cancellationToken);
                return true;
            }

            if (action == "status")
            {
                var result = _toolService.GetCronStatus();
                await _sendCronStatusResultAsync(socket, sendLock, result, cancellationToken);
                return true;
            }

            if (action == "list")
            {
                var includeDisabled = message.IncludeDisabled ?? false;
                var result = _toolService.ListCronJobs(
                    includeDisabled,
                    message.Limit,
                    message.Offset
                );
                await _sendCronListResultAsync(
                    socket,
                    sendLock,
                    includeDisabled,
                    message.Limit,
                    message.Offset,
                    result,
                    cancellationToken
                );
                return true;
            }

            if (action == "add")
            {
                var rawJobJson = _buildCronAddJobJson(message.RawJson);
                var result = _toolService.AddCronJob(rawJobJson);
                await _sendCronAddResultAsync(
                    socket,
                    sendLock,
                    result,
                    cancellationToken
                );
                return true;
            }

            var requestedJobId = string.IsNullOrWhiteSpace(message.JobId)
                ? message.CronId
                : message.JobId;

            if (action == "update")
            {
                if (string.IsNullOrWhiteSpace(requestedJobId))
                {
                    await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"jobId is required\"}", cancellationToken);
                    return true;
                }

                var rawPatchJson = _buildCronUpdatePatchJson(message.RawJson);
                var result = _toolService.UpdateCronJob(requestedJobId, rawPatchJson);
                await _sendCronUpdateResultAsync(
                    socket,
                    sendLock,
                    requestedJobId,
                    result,
                    cancellationToken
                );
                return true;
            }

            if (action == "run")
            {
                if (string.IsNullOrWhiteSpace(requestedJobId))
                {
                    await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"jobId is required\"}", cancellationToken);
                    return true;
                }

                var result = await _toolService.RunCronJobAsync(
                    requestedJobId,
                    message.RunMode,
                    "web",
                    cancellationToken
                );
                await _sendCronRunResultAsync(
                    socket,
                    sendLock,
                    requestedJobId,
                    message.RunMode,
                    result,
                    cancellationToken
                );
                return true;
            }

            if (action == "remove")
            {
                if (string.IsNullOrWhiteSpace(requestedJobId))
                {
                    await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"jobId is required\"}", cancellationToken);
                    return true;
                }

                var result = _toolService.RemoveCronJob(requestedJobId);
                await _sendCronRemoveResultAsync(
                    socket,
                    sendLock,
                    requestedJobId,
                    result,
                    cancellationToken
                );
                return true;
            }

            if (action == "runs")
            {
                if (string.IsNullOrWhiteSpace(requestedJobId))
                {
                    await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"jobId is required\"}", cancellationToken);
                    return true;
                }

                var result = _toolService.ListCronRuns(
                    requestedJobId,
                    message.Limit,
                    message.Offset
                );
                await _sendCronRunsResultAsync(
                    socket,
                    sendLock,
                    requestedJobId,
                    message.Limit,
                    message.Offset,
                    result,
                    cancellationToken
                );
                return true;
            }

            if (action == "wake")
            {
                var wakeText = string.IsNullOrWhiteSpace(message.Text)
                    ? message.Message
                    : message.Text;
                var wakeTextLength = string.IsNullOrWhiteSpace(wakeText)
                    ? 0
                    : wakeText.Trim().Length;
                var result = _toolService.WakeCron(
                    message.Mode,
                    wakeText,
                    "web"
                );
                await _sendCronWakeResultAsync(
                    socket,
                    sendLock,
                    message.Mode,
                    wakeTextLength,
                    result,
                    cancellationToken
                );
                return true;
            }

            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"error\","
                + $"\"message\":\"{WebSocketGateway.EscapeJson($"unsupported cron action: {action}")}\""
                + "}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "browser")
        {
            var action = (message.Action ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"browser action is required\"}", cancellationToken);
                return true;
            }

            var requestedUrl = string.IsNullOrWhiteSpace(message.WebFetchUrl)
                ? (string.IsNullOrWhiteSpace(message.Query) ? null : message.Query.Trim())
                : message.WebFetchUrl.Trim();

            var result = _toolService.ExecuteBrowser(
                action,
                requestedUrl,
                message.Profile,
                message.TargetId,
                message.Limit
            );
            await _sendBrowserResultAsync(
                socket,
                sendLock,
                action,
                requestedUrl,
                message.Profile,
                message.TargetId,
                message.Limit,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "canvas")
        {
            var action = (message.Action ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"canvas action is required\"}", cancellationToken);
                return true;
            }

            var requestedTarget = string.IsNullOrWhiteSpace(message.Target)
                ? (string.IsNullOrWhiteSpace(message.Search) ? null : message.Search.Trim())
                : message.Target.Trim();
            var requestedUrl = string.IsNullOrWhiteSpace(message.WebFetchUrl)
                ? (string.IsNullOrWhiteSpace(message.Query) ? null : message.Query.Trim())
                : message.WebFetchUrl.Trim();
            var requestedTextPayload = string.IsNullOrWhiteSpace(message.Text)
                ? message.Message
                : message.Text;

            var result = _toolService.ExecuteCanvas(
                action,
                message.Profile,
                requestedTarget,
                requestedUrl,
                requestedTextPayload,
                requestedTextPayload,
                message.OutputFormat,
                message.MaxWidth
            );
            await _sendCanvasResultAsync(
                socket,
                sendLock,
                action,
                requestedTarget,
                requestedUrl,
                message.Profile,
                message.OutputFormat,
                message.MaxWidth,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "nodes")
        {
            var action = (message.Action ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"nodes action is required\"}", cancellationToken);
                return true;
            }

            var requestedNode = string.IsNullOrWhiteSpace(message.Node)
                ? (string.IsNullOrWhiteSpace(message.Target) ? null : message.Target.Trim())
                : message.Node.Trim();
            var requestedRequestId = string.IsNullOrWhiteSpace(message.RequestId)
                ? null
                : message.RequestId.Trim();
            var requestedTitle = string.IsNullOrWhiteSpace(message.Title)
                ? null
                : message.Title.Trim();
            var requestedBody = string.IsNullOrWhiteSpace(message.Body)
                ? null
                : message.Body.Trim();
            var requestedPriority = string.IsNullOrWhiteSpace(message.Priority)
                ? null
                : message.Priority.Trim();
            var requestedDelivery = string.IsNullOrWhiteSpace(message.Delivery)
                ? null
                : message.Delivery.Trim();
            var requestedInvokeCommand = string.IsNullOrWhiteSpace(message.InvokeCommand)
                ? null
                : message.InvokeCommand.Trim();
            var requestedInvokeParamsJson = string.IsNullOrWhiteSpace(message.InvokeParamsJson)
                ? null
                : message.InvokeParamsJson.Trim();

            var result = _toolService.ExecuteNodes(
                action,
                message.Profile,
                requestedNode,
                requestedRequestId,
                requestedTitle,
                requestedBody,
                requestedPriority,
                requestedDelivery,
                requestedInvokeCommand,
                requestedInvokeParamsJson
            );
            await _sendNodesResultAsync(
                socket,
                sendLock,
                action,
                requestedNode,
                requestedRequestId,
                message.Profile,
                requestedInvokeCommand,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "cleanup_preview")
        {
            var result = _toolService.PreviewCleanup();
            await WebSocketGateway.SendTextAsync(socket, sendLock, BuildCleanupPreviewResultJson(result), cancellationToken);
            return true;
        }

        if (message.Type == "cleanup_apply")
        {
            var result = _toolService.ApplyCleanup(message.PreviewId);
            await WebSocketGateway.SendTextAsync(socket, sendLock, BuildCleanupApplyResultJson(result), cancellationToken);
            return true;
        }

        if (message.Type == "telegram_stub_command")
        {
            var requestedText = string.IsNullOrWhiteSpace(message.Text)
                ? message.Message
                : message.Text;
            if (string.IsNullOrWhiteSpace(requestedText))
            {
                await _sendTelegramStubResultAsync(
                    socket,
                    sendLock,
                    string.Empty,
                    string.Empty,
                    false,
                    "invalid",
                    "text is required",
                    cancellationToken
                );
                return true;
            }

            var trimmedText = requestedText.Trim();
            if (!_allowCommand(sessionId))
            {
                await _sendTelegramStubResultAsync(
                    socket,
                    sendLock,
                    trimmedText,
                    string.Empty,
                    false,
                    "rate_limited",
                    "rate limit exceeded",
                    cancellationToken
                );
                return true;
            }

            try
            {
                var response = await _commandExecutionService.ExecuteAsync(
                    trimmedText,
                    "telegram",
                    cancellationToken,
                    message.Attachments,
                    message.WebUrls,
                    message.WebSearchEnabled
                );
                var telegramExecutionMeta = _commandExecutionService.GetCurrentTelegramExecutionMetadata();
                var isError = response.StartsWith("error:", StringComparison.OrdinalIgnoreCase);
                await _sendTelegramStubResultAsync(
                    socket,
                    sendLock,
                    trimmedText,
                    response,
                    !isError,
                    isError ? "error" : "ok",
                    isError ? response : null,
                    cancellationToken,
                    telegramExecutionMeta.GuardFailure,
                    telegramExecutionMeta.RetryAttempt,
                    telegramExecutionMeta.RetryMaxAttempts,
                    telegramExecutionMeta.RetryStopReason
                );
            }
            catch (Exception ex)
            {
                await _sendTelegramStubResultAsync(
                    socket,
                    sendLock,
                    trimmedText,
                    string.Empty,
                    false,
                    "error",
                    "telegram_stub_command failed: " + ex.Message,
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "web_search")
        {
            var query = string.IsNullOrWhiteSpace(message.Query)
                ? message.Text
                : message.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"query is required\"}", cancellationToken);
                return true;
            }

            var trimmedQuery = query.Trim();
            var result = await _toolService.SearchWebAsync(
                trimmedQuery,
                message.Count,
                message.Freshness,
                cancellationToken
            );
            await _sendWebSearchResultAsync(
                socket,
                sendLock,
                trimmedQuery,
                message.Count,
                message.Freshness,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "web_fetch")
        {
            var requestedUrl = string.IsNullOrWhiteSpace(message.WebFetchUrl)
                ? (string.IsNullOrWhiteSpace(message.Query) ? message.Text : message.Query)
                : message.WebFetchUrl;
            if (string.IsNullOrWhiteSpace(requestedUrl))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"url is required\"}", cancellationToken);
                return true;
            }

            var trimmedUrl = requestedUrl.Trim();
            var result = await _toolService.FetchWebAsync(
                trimmedUrl,
                message.ExtractMode,
                message.MaxChars,
                cancellationToken
            );
            await _sendWebFetchResultAsync(
                socket,
                sendLock,
                trimmedUrl,
                message.ExtractMode,
                message.MaxChars,
                result,
                cancellationToken
            );
            return true;
        }

        return false;
    }

    private static string BuildCleanupPreviewResultJson(CleanupPreviewResult result)
    {
        var candidatesJson = string.Join(",", result.Candidates.Select(item =>
            "{"
            + $"\"path\":\"{WebSocketGateway.EscapeJson(item.Path)}\","
            + $"\"kind\":\"{WebSocketGateway.EscapeJson(item.Kind)}\","
            + $"\"sizeBytes\":{Math.Max(0L, item.SizeBytes)},"
            + $"\"lastModifiedUtc\":\"{WebSocketGateway.EscapeJson(item.LastModifiedUtc.ToString("O"))}\","
            + $"\"reason\":\"{WebSocketGateway.EscapeJson(item.Reason)}\""
            + "}"
        ));
        return "{"
            + "\"type\":\"cleanup_preview_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{WebSocketGateway.EscapeJson(result.Message)}\","
            + $"\"previewId\":\"{WebSocketGateway.EscapeJson(result.PreviewId)}\","
            + $"\"totalSizeBytes\":{Math.Max(0L, result.TotalSizeBytes)},"
            + $"\"error\":\"{WebSocketGateway.EscapeJson(result.Error ?? string.Empty)}\","
            + $"\"candidates\":[{candidatesJson}]"
            + "}";
    }

    private static string BuildCleanupApplyResultJson(CleanupApplyResult result)
    {
        static string StringArrayJson(IReadOnlyList<string> values)
        {
            return "[" + string.Join(",", values.Select(value => $"\"{WebSocketGateway.EscapeJson(value)}\"")) + "]";
        }

        return "{"
            + "\"type\":\"cleanup_apply_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{WebSocketGateway.EscapeJson(result.Message)}\","
            + $"\"previewId\":\"{WebSocketGateway.EscapeJson(result.PreviewId)}\","
            + $"\"removedCount\":{Math.Max(0, result.RemovedCount)},"
            + $"\"removedSizeBytes\":{Math.Max(0L, result.RemovedSizeBytes)},"
            + $"\"removedPaths\":{StringArrayJson(result.RemovedPaths)},"
            + $"\"failedPaths\":{StringArrayJson(result.FailedPaths)},"
            + $"\"error\":\"{WebSocketGateway.EscapeJson(result.Error ?? string.Empty)}\""
            + "}";
    }
}
