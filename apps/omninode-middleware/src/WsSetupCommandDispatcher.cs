using System.Net.WebSockets;
namespace OmniNode.Middleware;

internal sealed class WsSetupCommandDispatcher
{
    private readonly ISettingsApplicationService _settingsService;
    private readonly GroqModelCatalog _groqModelCatalog;
    private readonly CerebrasModelCatalog _cerebrasModelCatalog;
    private readonly LlmRouter _llmRouter;
    private readonly Func<WebSocket, SemaphoreSlim, CancellationToken, bool, Task> _sendSettingsStateAsync;
    private readonly Func<WebSocket, SemaphoreSlim, CancellationToken, Task> _sendGroqModelsAsync;
    private readonly Func<WebSocket, SemaphoreSlim, CancellationToken, Task> _sendCerebrasModelsAsync;
    private readonly Func<WebSocket, SemaphoreSlim, CancellationToken, Task> _sendCopilotModelsAsync;
    private readonly Func<WebSocket, SemaphoreSlim, CancellationToken, bool, Task> _sendUsageStatsAsync;
    private readonly Func<WebSocket, SemaphoreSlim, string, RoutingPolicyActionResult, CancellationToken, Task> _sendRoutingPolicyResultAsync;
    private readonly Func<WebSocket, SemaphoreSlim, RoutingDecision?, CancellationToken, Task> _sendRoutingDecisionAsync;
    private readonly Action _requestListenerRebind;

    public WsSetupCommandDispatcher(
        ISettingsApplicationService settingsService,
        GroqModelCatalog groqModelCatalog,
        CerebrasModelCatalog cerebrasModelCatalog,
        LlmRouter llmRouter,
        Func<WebSocket, SemaphoreSlim, CancellationToken, bool, Task> sendSettingsStateAsync,
        Func<WebSocket, SemaphoreSlim, CancellationToken, Task> sendGroqModelsAsync,
        Func<WebSocket, SemaphoreSlim, CancellationToken, Task> sendCerebrasModelsAsync,
        Func<WebSocket, SemaphoreSlim, CancellationToken, Task> sendCopilotModelsAsync,
        Func<WebSocket, SemaphoreSlim, CancellationToken, bool, Task> sendUsageStatsAsync,
        Func<WebSocket, SemaphoreSlim, string, RoutingPolicyActionResult, CancellationToken, Task> sendRoutingPolicyResultAsync,
        Func<WebSocket, SemaphoreSlim, RoutingDecision?, CancellationToken, Task> sendRoutingDecisionAsync,
        Action requestListenerRebind
    )
    {
        _settingsService = settingsService;
        _groqModelCatalog = groqModelCatalog;
        _cerebrasModelCatalog = cerebrasModelCatalog;
        _llmRouter = llmRouter;
        _sendSettingsStateAsync = sendSettingsStateAsync;
        _sendGroqModelsAsync = sendGroqModelsAsync;
        _sendCerebrasModelsAsync = sendCerebrasModelsAsync;
        _sendCopilotModelsAsync = sendCopilotModelsAsync;
        _sendUsageStatsAsync = sendUsageStatsAsync;
        _sendRoutingPolicyResultAsync = sendRoutingPolicyResultAsync;
        _sendRoutingDecisionAsync = sendRoutingDecisionAsync;
        _requestListenerRebind = requestListenerRebind;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        bool remoteDashboardClient,
        bool isAuthenticated,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        var messageType = message.Type ?? string.Empty;
        if (!IsSetupMessage(messageType))
        {
            return false;
        }

        if (messageType == "get_settings" || messageType == "get_setup_state")
        {
            await _sendSettingsStateAsync(socket, sendLock, cancellationToken, remoteDashboardClient);
            return true;
        }

        if (!isAuthenticated)
        {
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{\"type\":\"error\",\"message\":\"unauthorized\"}",
                cancellationToken
            );
            return true;
        }

        if (remoteDashboardClient && IsRemoteRestrictedMessage(messageType))
        {
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{\"type\":\"error\",\"message\":\"forbidden_remote_dashboard\"}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "set_external_dashboard_access")
        {
            var result = _settingsService.SetExternalDashboardEnabled(message.Enabled == true);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"settings_result\",\"ok\":{(IsSettingsOperationSuccess(result) ? "true" : "false")},\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            await _sendSettingsStateAsync(socket, sendLock, cancellationToken, remoteDashboardClient);
            _requestListenerRebind();
            return true;
        }

        if (message.Type == "routing_policy_get")
        {
            await _sendRoutingPolicyResultAsync(
                socket,
                sendLock,
                "get",
                _settingsService.GetRoutingPolicySnapshot(),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "routing_policy_save")
        {
            RoutingPolicy? policy = null;
            if (!string.IsNullOrWhiteSpace(message.RoutingPolicyJson))
            {
                policy = RoutingPolicyJson.DeserializePolicy(message.RoutingPolicyJson);
            }

            await _sendRoutingPolicyResultAsync(
                socket,
                sendLock,
                "save",
                _settingsService.SaveRoutingPolicy(policy),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "routing_policy_reset")
        {
            await _sendRoutingPolicyResultAsync(
                socket,
                sendLock,
                "reset",
                _settingsService.ResetRoutingPolicy(),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "routing_decision_get_last")
        {
            await _sendRoutingDecisionAsync(
                socket,
                sendLock,
                _settingsService.GetLastRoutingDecision(),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "set_telegram_credentials")
        {
            var result = _settingsService.UpdateTelegramCredentials(message.TelegramBotToken, message.TelegramChatId, message.Persist);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"settings_result\",\"ok\":{(IsSettingsOperationSuccess(result) ? "true" : "false")},\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            await _sendSettingsStateAsync(socket, sendLock, cancellationToken, remoteDashboardClient);
            return true;
        }

        if (message.Type == "delete_telegram_credentials")
        {
            var result = _settingsService.DeleteTelegramCredentials(message.Persist);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"settings_result\",\"ok\":{(IsSettingsOperationSuccess(result) ? "true" : "false")},\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            await _sendSettingsStateAsync(socket, sendLock, cancellationToken, remoteDashboardClient);
            return true;
        }

        if (message.Type == "set_llm_credentials")
        {
            var result = _settingsService.UpdateLlmCredentials(
                message.GroqApiKey,
                message.GeminiApiKey,
                message.CerebrasApiKey,
                message.NvidiaApiKey,
                message.CodexApiKey,
                message.Persist
            );
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"settings_result\",\"ok\":{(IsSettingsOperationSuccess(result) ? "true" : "false")},\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            await _sendSettingsStateAsync(socket, sendLock, cancellationToken, remoteDashboardClient);
            await _sendGroqModelsAsync(socket, sendLock, cancellationToken);
            await _sendCopilotModelsAsync(socket, sendLock, cancellationToken);
            await _sendUsageStatsAsync(socket, sendLock, cancellationToken, false);
            return true;
        }

        if (message.Type == "delete_llm_credentials")
        {
            var result = _settingsService.DeleteLlmCredentials(message.Persist);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"settings_result\",\"ok\":{(IsSettingsOperationSuccess(result) ? "true" : "false")},\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            await _sendSettingsStateAsync(socket, sendLock, cancellationToken, remoteDashboardClient);
            await _sendGroqModelsAsync(socket, sendLock, cancellationToken);
            await _sendCopilotModelsAsync(socket, sendLock, cancellationToken);
            await _sendUsageStatsAsync(socket, sendLock, cancellationToken, false);
            return true;
        }

        if (message.Type == "test_telegram")
        {
            var result = await _settingsService.SendTelegramTestAsync(cancellationToken);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"settings_result\",\"ok\":{(IsSettingsOperationSuccess(result) ? "true" : "false")},\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "get_copilot_status")
        {
            var status = await _settingsService.GetCopilotStatusAsync(cancellationToken);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"copilot_status\","
                + $"\"installed\":{(status.Installed ? "true" : "false")},"
                + $"\"authenticated\":{(status.Authenticated ? "true" : "false")},"
                + $"\"mode\":\"{WebSocketGateway.EscapeJson(status.Mode)}\","
                + $"\"detail\":\"{WebSocketGateway.EscapeJson(status.Detail)}\""
                + "}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "get_codex_status")
        {
            var status = await _settingsService.GetCodexStatusAsync(cancellationToken);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"codex_status\","
                + $"\"installed\":{(status.Installed ? "true" : "false")},"
                + $"\"authenticated\":{(status.Authenticated ? "true" : "false")},"
                + $"\"mode\":\"{WebSocketGateway.EscapeJson(status.Mode)}\","
                + $"\"detail\":\"{WebSocketGateway.EscapeJson(status.Detail)}\""
                + "}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "get_usage_stats")
        {
            await _sendUsageStatsAsync(socket, sendLock, cancellationToken, true);
            return true;
        }

        if (message.Type == "start_copilot_login")
        {
            var result = await _settingsService.StartCopilotLoginAsync(cancellationToken);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"copilot_login_result\",\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "start_codex_login")
        {
            var result = await _settingsService.StartCodexLoginAsync(cancellationToken);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"codex_login_result\",\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "logout_codex")
        {
            var result = await _settingsService.LogoutCodexAsync(cancellationToken);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"codex_logout_result\",\"message\":\"{WebSocketGateway.EscapeJson(result)}\"}}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "get_copilot_models")
        {
            await _sendCopilotModelsAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "set_copilot_model")
        {
            if (string.IsNullOrWhiteSpace(message.Model))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"model is required\"}", cancellationToken);
                return true;
            }

            var models = await _settingsService.GetCopilotModelsAsync(cancellationToken);
            var requestedModel = message.Model.Trim();
            if (!models.Any(x => x.Id.Equals(requestedModel, StringComparison.OrdinalIgnoreCase)))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"unknown copilot model\"}", cancellationToken);
                return true;
            }

            if (!_settingsService.TrySetSelectedCopilotModel(requestedModel))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"invalid model id\"}", cancellationToken);
                return true;
            }

            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"copilot_model_set\",\"ok\":true,\"model\":\"{WebSocketGateway.EscapeJson(requestedModel)}\"}}",
                cancellationToken
            );
            await _sendCopilotModelsAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "get_groq_models")
        {
            await _sendGroqModelsAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "get_cerebras_models")
        {
            await _sendCerebrasModelsAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "set_groq_model")
        {
            if (string.IsNullOrWhiteSpace(message.Model))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"model is required\"}", cancellationToken);
                return true;
            }

            var models = await _groqModelCatalog.GetModelsAsync(cancellationToken);
            var requestedModel = message.Model.Trim();
            if (!models.Any(x => x.Id.Equals(requestedModel, StringComparison.OrdinalIgnoreCase)))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"unknown model\"}", cancellationToken);
                return true;
            }

            if (!_llmRouter.TrySetSelectedGroqModel(requestedModel))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"invalid model id\"}", cancellationToken);
                return true;
            }

            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"groq_model_set\",\"ok\":true,\"model\":\"{WebSocketGateway.EscapeJson(requestedModel)}\"}}",
                cancellationToken
            );
            await _sendGroqModelsAsync(socket, sendLock, cancellationToken);
            return true;
        }

        return false;
    }

    private static bool IsSettingsOperationSuccess(string? result)
    {
        var normalized = (result ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return !normalized.Contains("failed", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("not set", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemoteRestrictedMessage(string? messageType)
    {
        return messageType is
            "set_external_dashboard_access" or
            "set_telegram_credentials" or
            "delete_telegram_credentials" or
            "set_llm_credentials" or
            "delete_llm_credentials" or
            "test_telegram" or
            "get_copilot_status" or
            "get_codex_status" or
            "start_copilot_login" or
            "start_codex_login" or
            "logout_codex";
    }

    private static bool IsSetupMessage(string messageType)
    {
        return messageType is
            "get_settings" or
            "get_setup_state" or
            "set_external_dashboard_access" or
            "routing_policy_get" or
            "routing_policy_save" or
            "routing_policy_reset" or
            "routing_decision_get_last" or
            "set_telegram_credentials" or
            "delete_telegram_credentials" or
            "set_llm_credentials" or
            "delete_llm_credentials" or
            "test_telegram" or
            "get_copilot_status" or
            "get_codex_status" or
            "get_usage_stats" or
            "start_copilot_login" or
            "start_codex_login" or
            "logout_codex" or
            "get_copilot_models" or
            "set_copilot_model" or
            "get_groq_models" or
            "get_cerebras_models" or
            "set_groq_model";
    }
}
