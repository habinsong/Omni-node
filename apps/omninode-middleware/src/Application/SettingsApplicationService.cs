namespace OmniNode.Middleware;

public sealed class SettingsApplicationService : ISettingsApplicationService
{
    private readonly CommandService _inner;

    public SettingsApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public SettingsSnapshot GetSettingsSnapshot() => _inner.GetSettingsSnapshot();
    public RoutingPolicyActionResult GetRoutingPolicySnapshot() => _inner.GetRoutingPolicySnapshot();
    public RoutingPolicyActionResult SaveRoutingPolicy(RoutingPolicy? policy) => _inner.SaveRoutingPolicy(policy);
    public RoutingPolicyActionResult ResetRoutingPolicy() => _inner.ResetRoutingPolicy();
    public RoutingDecision? GetLastRoutingDecision() => _inner.GetLastRoutingDecision();
    public string UpdateTelegramCredentials(string? botToken, string? chatId, bool persist) => _inner.UpdateTelegramCredentials(botToken, chatId, persist);
    public string UpdateLlmCredentials(string? groqApiKey, string? geminiApiKey, string? cerebrasApiKey, string? nvidiaApiKey, string? codexApiKey, bool persist) => _inner.UpdateLlmCredentials(groqApiKey, geminiApiKey, cerebrasApiKey, nvidiaApiKey, codexApiKey, persist);
    public string DeleteTelegramCredentials(bool deletePersisted) => _inner.DeleteTelegramCredentials(deletePersisted);
    public string DeleteLlmCredentials(bool deletePersisted) => _inner.DeleteLlmCredentials(deletePersisted);
    public string SetExternalDashboardEnabled(bool enabled) => _inner.SetExternalDashboardEnabled(enabled);
    public GeminiUsage GetGeminiUsageSnapshot() => _inner.GetGeminiUsageSnapshot();
    public Task<CopilotPremiumUsageSnapshot> GetCopilotPremiumUsageSnapshotAsync(CancellationToken cancellationToken, bool forceRefresh = false) => _inner.GetCopilotPremiumUsageSnapshotAsync(cancellationToken, forceRefresh);
    public Task<string> SendTelegramTestAsync(CancellationToken cancellationToken) => _inner.SendTelegramTestAsync(cancellationToken);
    public Task<string> StartCopilotLoginAsync(CancellationToken cancellationToken) => _inner.StartCopilotLoginAsync(cancellationToken);
    public Task<string> StartCodexLoginAsync(CancellationToken cancellationToken) => _inner.StartCodexLoginAsync(cancellationToken);
    public Task<string> GetMetricsAsync(CancellationToken cancellationToken) => _inner.GetMetricsAsync(cancellationToken);
    public Task<CopilotStatus> GetCopilotStatusAsync(CancellationToken cancellationToken) => _inner.GetCopilotStatusAsync(cancellationToken);
    public Task<CodexStatus> GetCodexStatusAsync(CancellationToken cancellationToken) => _inner.GetCodexStatusAsync(cancellationToken);
    public Task<string> LogoutCodexAsync(CancellationToken cancellationToken) => _inner.LogoutCodexAsync(cancellationToken);
    public Task<IReadOnlyList<CopilotModelInfo>> GetCopilotModelsAsync(CancellationToken cancellationToken) => _inner.GetCopilotModelsAsync(cancellationToken);
    public string GetSelectedCopilotModel() => _inner.GetSelectedCopilotModel();
    public IReadOnlyDictionary<string, CopilotUsage> GetCopilotLocalUsageSnapshot() => _inner.GetCopilotLocalUsageSnapshot();
    public bool TrySetSelectedCopilotModel(string modelId) => _inner.TrySetSelectedCopilotModel(modelId);
}
