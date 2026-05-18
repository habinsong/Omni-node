namespace OmniNode.Middleware;

public sealed class SettingsApplicationService : ISettingsApplicationService
{
    private readonly RuntimeSettings _runtimeSettings;
    private readonly RoutingPolicyResolver _routingPolicyResolver;
    private readonly AuditLogger _auditLogger;
    private readonly LlmRouter _llmRouter;
    private readonly CopilotCliWrapper _copilotWrapper;
    private readonly CodexCliWrapper _codexWrapper;
    private readonly TelegramClient _telegramClient;
    private readonly UdsCoreClient _coreClient;

    public SettingsApplicationService(
        RuntimeSettings runtimeSettings,
        RoutingPolicyResolver routingPolicyResolver,
        AuditLogger auditLogger,
        LlmRouter llmRouter,
        CopilotCliWrapper copilotWrapper,
        CodexCliWrapper codexWrapper,
        TelegramClient telegramClient,
        UdsCoreClient coreClient
    )
    {
        _runtimeSettings = runtimeSettings;
        _routingPolicyResolver = routingPolicyResolver;
        _auditLogger = auditLogger;
        _llmRouter = llmRouter;
        _copilotWrapper = copilotWrapper;
        _codexWrapper = codexWrapper;
        _telegramClient = telegramClient;
        _coreClient = coreClient;
    }

    public SettingsSnapshot GetSettingsSnapshot()
        => _runtimeSettings.GetSnapshot();

    public RoutingPolicyActionResult GetRoutingPolicySnapshot()
        => _routingPolicyResolver.GetSnapshotResult();

    public RoutingPolicyActionResult SaveRoutingPolicy(RoutingPolicy? policy)
    {
        var result = _routingPolicyResolver.SaveOverrides(policy);
        _auditLogger.Log("web", "routing_policy_save", result.Ok ? "ok" : "error", result.Message);
        return result;
    }

    public RoutingPolicyActionResult ResetRoutingPolicy()
    {
        var result = _routingPolicyResolver.ResetOverrides();
        _auditLogger.Log("web", "routing_policy_reset", result.Ok ? "ok" : "error", result.Message);
        return result;
    }

    public RoutingDecision? GetLastRoutingDecision()
        => _routingPolicyResolver.GetLastDecision();

    public string UpdateTelegramCredentials(string? botToken, string? chatId, bool persist)
    {
        var result = _runtimeSettings.UpdateTelegram(botToken, chatId, persist);
        _auditLogger.Log("web", "update_telegram_credentials", "ok", result);
        return result;
    }

    public string UpdateLlmCredentials(
        string? groqApiKey,
        string? geminiApiKey,
        string? cerebrasApiKey,
        string? nvidiaApiKey,
        string? codexApiKey,
        bool persist
    )
    {
        var result = _runtimeSettings.UpdateLlmKeys(
            groqApiKey,
            geminiApiKey,
            cerebrasApiKey,
            nvidiaApiKey,
            codexApiKey,
            persist
        );
        _auditLogger.Log("web", "update_llm_credentials", "ok", result);
        return result;
    }

    public string DeleteTelegramCredentials(bool deletePersisted)
    {
        var result = _runtimeSettings.DeleteTelegramCredentials(deletePersisted);
        _auditLogger.Log("web", "delete_telegram_credentials", "ok", result);
        return result;
    }

    public string DeleteLlmCredentials(bool deletePersisted)
    {
        var result = _runtimeSettings.DeleteLlmCredentials(deletePersisted);
        _auditLogger.Log("web", "delete_llm_credentials", "ok", result);
        return result;
    }

    public string SetExternalDashboardEnabled(bool enabled)
    {
        var result = _runtimeSettings.SetExternalDashboardEnabled(enabled);
        _auditLogger.Log("web", "set_external_dashboard_access", "ok", result);
        return result;
    }

    public GeminiUsage GetGeminiUsageSnapshot()
        => _llmRouter.GetGeminiUsageSnapshot();

    public Task<CopilotPremiumUsageSnapshot> GetCopilotPremiumUsageSnapshotAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false
    ) => _copilotWrapper.GetPremiumUsageSnapshotAsync(cancellationToken, forceRefresh);

    public async Task<string> SendTelegramTestAsync(CancellationToken cancellationToken)
    {
        if (!_runtimeSettings.HasTelegramCredentials())
        {
            return "telegram credentials are not set";
        }

        var sent = await _telegramClient.SendMessageAsync("[Omni-node] Telegram 연동 테스트 메시지", cancellationToken);
        return sent ? "telegram test message sent" : "telegram send failed. check bot token/chat id";
    }

    public Task<string> StartCopilotLoginAsync(CancellationToken cancellationToken)
        => _copilotWrapper.StartLoginAsync(cancellationToken);

    public Task<string> StartCodexLoginAsync(CancellationToken cancellationToken)
        => _codexWrapper.StartLoginAsync(cancellationToken);

    public Task<string> GetMetricsAsync(CancellationToken cancellationToken)
        => _coreClient.GetMetricsAsync(cancellationToken);

    public Task<CopilotStatus> GetCopilotStatusAsync(CancellationToken cancellationToken)
        => _copilotWrapper.GetStatusAsync(cancellationToken);

    public Task<CodexStatus> GetCodexStatusAsync(CancellationToken cancellationToken)
        => _codexWrapper.GetStatusAsync(cancellationToken);

    public Task<string> LogoutCodexAsync(CancellationToken cancellationToken)
        => _codexWrapper.LogoutAsync(cancellationToken);

    public Task<IReadOnlyList<CopilotModelInfo>> GetCopilotModelsAsync(CancellationToken cancellationToken)
        => _copilotWrapper.GetModelsAsync(cancellationToken);

    public string GetSelectedCopilotModel()
        => _copilotWrapper.GetSelectedModel();

    public IReadOnlyDictionary<string, CopilotUsage> GetCopilotLocalUsageSnapshot()
        => _copilotWrapper.GetUsageSnapshot();

    public bool TrySetSelectedCopilotModel(string modelId)
        => _copilotWrapper.TrySetSelectedModel(modelId);
}
