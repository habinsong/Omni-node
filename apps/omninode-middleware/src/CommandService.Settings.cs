namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public SettingsSnapshot GetSettingsSnapshot()
        => _settingsAppService.GetSettingsSnapshot();

    public RoutingPolicyActionResult GetRoutingPolicySnapshot()
        => _settingsAppService.GetRoutingPolicySnapshot();

    public RoutingPolicyActionResult SaveRoutingPolicy(RoutingPolicy? policy)
        => _settingsAppService.SaveRoutingPolicy(policy);

    public RoutingPolicyActionResult ResetRoutingPolicy()
        => _settingsAppService.ResetRoutingPolicy();

    public RoutingDecision? GetLastRoutingDecision()
        => _settingsAppService.GetLastRoutingDecision();

    public string UpdateTelegramCredentials(string? botToken, string? chatId, bool persist)
        => _settingsAppService.UpdateTelegramCredentials(botToken, chatId, persist);

    public string UpdateLlmCredentials(
        string? groqApiKey,
        string? geminiApiKey,
        string? cerebrasApiKey,
        string? nvidiaApiKey,
        string? codexApiKey,
        bool persist
    ) => _settingsAppService.UpdateLlmCredentials(groqApiKey, geminiApiKey, cerebrasApiKey, nvidiaApiKey, codexApiKey, persist);

    public string DeleteTelegramCredentials(bool deletePersisted)
        => _settingsAppService.DeleteTelegramCredentials(deletePersisted);

    public string DeleteLlmCredentials(bool deletePersisted)
        => _settingsAppService.DeleteLlmCredentials(deletePersisted);

    public string SetExternalDashboardEnabled(bool enabled)
        => _settingsAppService.SetExternalDashboardEnabled(enabled);

    public GeminiUsage GetGeminiUsageSnapshot()
        => _settingsAppService.GetGeminiUsageSnapshot();

    public Task<CopilotPremiumUsageSnapshot> GetCopilotPremiumUsageSnapshotAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false
    ) => _settingsAppService.GetCopilotPremiumUsageSnapshotAsync(cancellationToken, forceRefresh);

    public Task<string> SendTelegramTestAsync(CancellationToken cancellationToken)
        => _settingsAppService.SendTelegramTestAsync(cancellationToken);

    public Task<string> StartCopilotLoginAsync(CancellationToken cancellationToken)
        => _settingsAppService.StartCopilotLoginAsync(cancellationToken);

    public Task<string> StartCodexLoginAsync(CancellationToken cancellationToken)
        => _settingsAppService.StartCodexLoginAsync(cancellationToken);

    public Task<string> GetMetricsAsync(CancellationToken cancellationToken)
        => _settingsAppService.GetMetricsAsync(cancellationToken);

    public Task<CopilotStatus> GetCopilotStatusAsync(CancellationToken cancellationToken)
        => _settingsAppService.GetCopilotStatusAsync(cancellationToken);

    public Task<CodexStatus> GetCodexStatusAsync(CancellationToken cancellationToken)
        => _settingsAppService.GetCodexStatusAsync(cancellationToken);

    public Task<string> LogoutCodexAsync(CancellationToken cancellationToken)
        => _settingsAppService.LogoutCodexAsync(cancellationToken);

    public Task<IReadOnlyList<CopilotModelInfo>> GetCopilotModelsAsync(CancellationToken cancellationToken)
        => _settingsAppService.GetCopilotModelsAsync(cancellationToken);

    public string GetSelectedCopilotModel()
        => _settingsAppService.GetSelectedCopilotModel();

    public IReadOnlyDictionary<string, CopilotUsage> GetCopilotLocalUsageSnapshot()
        => _settingsAppService.GetCopilotLocalUsageSnapshot();

    public bool TrySetSelectedCopilotModel(string modelId)
        => _settingsAppService.TrySetSelectedCopilotModel(modelId);
}
