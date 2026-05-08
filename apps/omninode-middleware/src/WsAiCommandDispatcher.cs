using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsAiCommandDispatcher
{
    internal delegate Task SendGuardedErrorDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string message,
        CancellationToken cancellationToken,
        SearchAnswerGuardFailure? guardFailure = null
    );

    internal delegate Task SendChatResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        ConversationChatResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendChatStreamChunkDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        ChatStreamUpdate update,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCodingResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CodingRunResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCodingExecutionResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CodingResultExecutionResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCodingProgressDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string scope,
        string mode,
        CodingProgressUpdate update,
        CancellationToken cancellationToken
    );

    internal delegate Task SendConversationsDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string scope,
        string mode,
        CancellationToken cancellationToken
    );

    internal delegate Task SendModelsDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    );

    internal delegate Task SendUsageStatsDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken,
        bool forceRefresh = false
    );

    internal delegate Task SendMetricsDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string type,
        string metricsRaw,
        CancellationToken cancellationToken
    );

    private readonly IChatApplicationService _chatService;
    private readonly ICodingApplicationService _codingService;
    private readonly ISettingsApplicationService _settingsService;
    private readonly ICommandExecutionService _commandExecutionService;
    private readonly Func<string, bool> _allowCommand;
    private readonly SendGuardedErrorDelegate _sendGuardedErrorAsync;
    private readonly SendChatResultDelegate _sendChatResultAsync;
    private readonly SendChatStreamChunkDelegate _sendChatStreamChunkAsync;
    private readonly SendCodingResultDelegate _sendCodingResultAsync;
    private readonly SendCodingExecutionResultDelegate _sendCodingExecutionResultAsync;
    private readonly SendCodingProgressDelegate _sendCodingProgressAsync;
    private readonly SendConversationsDelegate _sendConversationsAsync;
    private readonly SendModelsDelegate _sendGroqModelsAsync;
    private readonly SendModelsDelegate _sendCopilotModelsAsync;
    private readonly SendUsageStatsDelegate _sendUsageStatsAsync;
    private readonly SendMetricsDelegate _sendMetricsAsync;
    private readonly Func<ConversationMultiResult, string> _buildMultiChatResultJson;

    public WsAiCommandDispatcher(
        IChatApplicationService chatService,
        ICodingApplicationService codingService,
        ISettingsApplicationService settingsService,
        ICommandExecutionService commandExecutionService,
        Func<string, bool> allowCommand,
        SendGuardedErrorDelegate sendGuardedErrorAsync,
        SendChatResultDelegate sendChatResultAsync,
        SendChatStreamChunkDelegate sendChatStreamChunkAsync,
        SendCodingResultDelegate sendCodingResultAsync,
        SendCodingExecutionResultDelegate sendCodingExecutionResultAsync,
        SendCodingProgressDelegate sendCodingProgressAsync,
        SendConversationsDelegate sendConversationsAsync,
        SendModelsDelegate sendGroqModelsAsync,
        SendModelsDelegate sendCopilotModelsAsync,
        SendUsageStatsDelegate sendUsageStatsAsync,
        SendMetricsDelegate sendMetricsAsync,
        Func<ConversationMultiResult, string> buildMultiChatResultJson
    )
    {
        _chatService = chatService;
        _codingService = codingService;
        _settingsService = settingsService;
        _commandExecutionService = commandExecutionService;
        _allowCommand = allowCommand;
        _sendGuardedErrorAsync = sendGuardedErrorAsync;
        _sendChatResultAsync = sendChatResultAsync;
        _sendChatStreamChunkAsync = sendChatStreamChunkAsync;
        _sendCodingResultAsync = sendCodingResultAsync;
        _sendCodingExecutionResultAsync = sendCodingExecutionResultAsync;
        _sendCodingProgressAsync = sendCodingProgressAsync;
        _sendConversationsAsync = sendConversationsAsync;
        _sendGroqModelsAsync = sendGroqModelsAsync;
        _sendCopilotModelsAsync = sendCopilotModelsAsync;
        _sendUsageStatsAsync = sendUsageStatsAsync;
        _sendMetricsAsync = sendMetricsAsync;
        _buildMultiChatResultJson = buildMultiChatResultJson;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        string sessionId,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "llm_chat_single")
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "empty message",
                    cancellationToken
                );
                return true;
            }

            try
            {
                var scopeValue = message.Scope ?? "chat";
                var modeValue = message.Mode ?? "single";
                Action<ChatStreamUpdate> stream = update =>
                {
                    try
                    {
                        _sendChatStreamChunkAsync(socket, sendLock, update, cancellationToken).GetAwaiter().GetResult();
                    }
                    catch
                    {
                    }
                };
                var result = await _chatService.ChatSingleWithStateAsync(
                    new ChatRequest(
                        Input: message.Text,
                        Source: "web",
                        Scope: scopeValue,
                        Mode: modeValue,
                        ConversationId: message.ConversationId,
                        ConversationTitle: message.ConversationTitle,
                        Project: message.Project,
                        Category: message.Category,
                        Tags: message.Tags,
                        Provider: message.Provider,
                        Model: message.Model,
                        LinkedMemoryNotes: message.MemoryNotes,
                        NvidiaModel: message.NvidiaModel,
                        Attachments: message.Attachments,
                        WebUrls: message.WebUrls,
                        WebSearchEnabled: message.WebSearchEnabled,
                        CodexModel: message.CodexModel,
                        RequestId: message.RequestId,
                        SkillName: message.SkillName,
                        SkillScope: message.SkillScope,
                        ThinkPlusEnabled: message.ThinkPlus == true
                    ),
                    cancellationToken,
                    stream
                );

                await _sendChatResultAsync(socket, sendLock, result, cancellationToken);
                await _sendGroqModelsAsync(socket, sendLock, cancellationToken);
                await _sendCopilotModelsAsync(socket, sendLock, cancellationToken);
                await _sendUsageStatsAsync(socket, sendLock, cancellationToken);
                await _sendConversationsAsync(socket, sendLock, scopeValue, modeValue, cancellationToken);
            }
            catch (Exception ex)
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "chat_single failed: " + ex.Message,
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "llm_chat_orchestration")
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "empty message",
                    cancellationToken
                );
                return true;
            }

            try
            {
                var scopeValue = message.Scope ?? "chat";
                var modeValue = message.Mode ?? "orchestration";
                var result = await _chatService.ChatOrchestrationWithStateAsync(
                    new ChatRequest(
                        Input: message.Text,
                        Source: "web",
                        Scope: scopeValue,
                        Mode: modeValue,
                        ConversationId: message.ConversationId,
                        ConversationTitle: message.ConversationTitle,
                        Project: message.Project,
                        Category: message.Category,
                        Tags: message.Tags,
                        Provider: message.Provider,
                        Model: message.Model,
                        LinkedMemoryNotes: message.MemoryNotes,
                        GroqModel: message.GroqModel,
                        GeminiModel: message.GeminiModel,
                        CopilotModel: message.CopilotModel,
                        CerebrasModel: message.CerebrasModel,
                        NvidiaModel: message.NvidiaModel,
                        Attachments: message.Attachments,
                        WebUrls: message.WebUrls,
                        WebSearchEnabled: message.WebSearchEnabled,
                        CodexModel: message.CodexModel,
                        ThinkPlusEnabled: message.ThinkPlus == true
                    ),
                    cancellationToken
                );
                await _sendChatResultAsync(socket, sendLock, result, cancellationToken);
                await _sendUsageStatsAsync(socket, sendLock, cancellationToken);
                await _sendConversationsAsync(socket, sendLock, scopeValue, modeValue, cancellationToken);
            }
            catch (Exception ex)
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "chat_orchestration failed: " + ex.Message,
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "llm_chat_multi")
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "empty message",
                    cancellationToken
                );
                return true;
            }

            try
            {
                var scopeValue = message.Scope ?? "chat";
                var modeValue = message.Mode ?? "multi";
                var result = await _chatService.ChatMultiWithStateAsync(
                    new MultiChatRequest(
                        Input: message.Text,
                        Source: "web",
                        Scope: scopeValue,
                        Mode: modeValue,
                        ConversationId: message.ConversationId,
                        ConversationTitle: message.ConversationTitle,
                        Project: message.Project,
                        Category: message.Category,
                        Tags: message.Tags,
                        GroqModel: message.GroqModel,
                        GeminiModel: message.GeminiModel,
                        CopilotModel: message.CopilotModel,
                        CerebrasModel: message.CerebrasModel,
                        NvidiaModel: message.NvidiaModel,
                        SummaryProvider: message.SummaryProvider,
                        LinkedMemoryNotes: message.MemoryNotes,
                        Attachments: message.Attachments,
                        WebUrls: message.WebUrls,
                        WebSearchEnabled: message.WebSearchEnabled,
                        CodexModel: message.CodexModel,
                        ThinkPlusEnabled: message.ThinkPlus == true
                    ),
                    cancellationToken
                );

                await WebSocketGateway.SendTextAsync(
                    socket,
                    sendLock,
                    _buildMultiChatResultJson(result),
                    cancellationToken
                );
                await _sendGroqModelsAsync(socket, sendLock, cancellationToken);
                await _sendCopilotModelsAsync(socket, sendLock, cancellationToken);
                await _sendUsageStatsAsync(socket, sendLock, cancellationToken);
                await _sendConversationsAsync(socket, sendLock, scopeValue, modeValue, cancellationToken);
            }
            catch (Exception ex)
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "chat_multi failed: " + ex.Message,
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "coding_run_single")
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "empty coding input",
                    cancellationToken
                );
                return true;
            }

            try
            {
                var scopeValue = message.Scope ?? "coding";
                var modeValue = message.Mode ?? "single";
                Action<CodingProgressUpdate> progress = update =>
                {
                    _ = _sendCodingProgressAsync(socket, sendLock, scopeValue, modeValue, update, cancellationToken);
                };
                var result = await _codingService.RunCodingSingleAsync(
                    new CodingRunRequest(
                        Input: message.Text,
                        Source: "web",
                        Scope: scopeValue,
                        Mode: modeValue,
                        ConversationId: message.ConversationId,
                        ConversationTitle: message.ConversationTitle,
                        Project: message.Project,
                        Category: message.Category,
                        Tags: message.Tags,
                        Provider: message.Provider,
                        Model: message.Model,
                        Language: message.Language ?? "auto",
                        LinkedMemoryNotes: message.MemoryNotes,
                        NvidiaModel: message.NvidiaModel,
                        Attachments: message.Attachments,
                        WebUrls: message.WebUrls,
                        WebSearchEnabled: message.WebSearchEnabled,
                        ThinkPlusEnabled: message.ThinkPlus == true
                    ),
                    cancellationToken,
                    progress
                );
                await _sendCodingResultAsync(socket, sendLock, result, cancellationToken);
                await _sendConversationsAsync(socket, sendLock, scopeValue, modeValue, cancellationToken);
            }
            catch (Exception ex)
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "coding_single failed: " + ex.Message,
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "coding_run_orchestration")
        {
            try
            {
                var scopeValue = message.Scope ?? "coding";
                var modeValue = message.Mode ?? "orchestration";
                Action<CodingProgressUpdate> progress = update =>
                {
                    _ = _sendCodingProgressAsync(socket, sendLock, scopeValue, modeValue, update, cancellationToken);
                };
                var result = await _codingService.RunCodingOrchestrationAsync(
                    new CodingRunRequest(
                        Input: message.Text ?? string.Empty,
                        Source: "web",
                        Scope: scopeValue,
                        Mode: modeValue,
                        ConversationId: message.ConversationId,
                        ConversationTitle: message.ConversationTitle,
                        Project: message.Project,
                        Category: message.Category,
                        Tags: message.Tags,
                        Provider: message.Provider,
                        Model: message.Model,
                        Language: message.Language ?? "auto",
                        LinkedMemoryNotes: message.MemoryNotes,
                        GroqModel: message.GroqModel,
                        GeminiModel: message.GeminiModel,
                        CerebrasModel: message.CerebrasModel,
                        NvidiaModel: message.NvidiaModel,
                        CopilotModel: message.CopilotModel,
                        Attachments: message.Attachments,
                        WebUrls: message.WebUrls,
                        WebSearchEnabled: message.WebSearchEnabled,
                        CodexModel: message.CodexModel,
                        ThinkPlusEnabled: message.ThinkPlus == true
                    ),
                    cancellationToken,
                    progress
                );
                await _sendCodingResultAsync(socket, sendLock, result, cancellationToken);
                await _sendConversationsAsync(socket, sendLock, scopeValue, modeValue, cancellationToken);
            }
            catch (Exception ex)
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "coding_orchestration failed: " + ex.Message,
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "coding_run_multi")
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "empty coding input",
                    cancellationToken
                );
                return true;
            }

            try
            {
                var scopeValue = message.Scope ?? "coding";
                var modeValue = message.Mode ?? "multi";
                Action<CodingProgressUpdate> progress = update =>
                {
                    _ = _sendCodingProgressAsync(socket, sendLock, scopeValue, modeValue, update, cancellationToken);
                };
                var result = await _codingService.RunCodingMultiAsync(
                    new CodingRunRequest(
                        Input: message.Text,
                        Source: "web",
                        Scope: scopeValue,
                        Mode: modeValue,
                        ConversationId: message.ConversationId,
                        ConversationTitle: message.ConversationTitle,
                        Project: message.Project,
                        Category: message.Category,
                        Tags: message.Tags,
                        Provider: message.Provider,
                        Model: message.Model,
                        Language: message.Language ?? "auto",
                        LinkedMemoryNotes: message.MemoryNotes,
                        GroqModel: message.GroqModel,
                        GeminiModel: message.GeminiModel,
                        CerebrasModel: message.CerebrasModel,
                        NvidiaModel: message.NvidiaModel,
                        CopilotModel: message.CopilotModel,
                        Attachments: message.Attachments,
                        WebUrls: message.WebUrls,
                        WebSearchEnabled: message.WebSearchEnabled,
                        CodexModel: message.CodexModel,
                        ThinkPlusEnabled: message.ThinkPlus == true
                    ),
                    cancellationToken,
                    progress
                );
                await _sendCodingResultAsync(socket, sendLock, result, cancellationToken);
                await _sendConversationsAsync(socket, sendLock, scopeValue, modeValue, cancellationToken);
            }
            catch (Exception ex)
            {
                await _sendGuardedErrorAsync(
                    socket,
                    sendLock,
                    "coding_multi failed: " + ex.Message,
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "coding_execute_result")
        {
            if (string.IsNullOrWhiteSpace(message.ConversationId))
            {
                await WebSocketGateway.SendTextAsync(
                    socket,
                    sendLock,
                    "{\"type\":\"coding_execute_result\",\"ok\":false,\"message\":\"conversationId가 필요합니다.\"}",
                    cancellationToken
                );
                return true;
            }

            try
            {
                var executionResult = await _codingService.ExecuteLatestCodingResultAsync(
                    message.ConversationId,
                    message.StandardInput,
                    cancellationToken
                );
                await _sendCodingExecutionResultAsync(socket, sendLock, executionResult, cancellationToken);
            }
            catch (Exception ex)
            {
                await WebSocketGateway.SendTextAsync(
                    socket,
                    sendLock,
                    "{"
                    + "\"type\":\"coding_execute_result\","
                    + "\"ok\":false,"
                    + $"\"conversationId\":\"{WebSocketGateway.EscapeJson(message.ConversationId ?? string.Empty)}\","
                    + $"\"message\":\"{WebSocketGateway.EscapeJson(ex.Message)}\""
                    + "}",
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "get_metrics")
        {
            var metricsRaw = await _settingsService.GetMetricsAsync(cancellationToken);
            await _sendMetricsAsync(socket, sendLock, "metrics", metricsRaw, cancellationToken);
            return true;
        }

        if (message.Type == "command")
        {
            if (!_allowCommand(sessionId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"rate limit exceeded\"}", cancellationToken);
                return true;
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"empty command\"}", cancellationToken);
                return true;
            }

            var result = await _commandExecutionService.ExecuteAsync(
                message.Text.Trim(),
                "web",
                cancellationToken,
                message.Attachments,
                message.WebUrls,
                message.WebSearchEnabled
            );
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"command_result\",\"text\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            return true;
        }

        return false;
    }
}
