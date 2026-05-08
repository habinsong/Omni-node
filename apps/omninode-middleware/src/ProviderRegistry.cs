namespace OmniNode.Middleware;

public sealed record ProviderAvailability(
    string Provider,
    bool Available,
    string Reason,
    bool VisualCapable,
    bool CodeCapable,
    bool SearchCapable,
    bool CliAuthRequired,
    bool BackgroundSafe
);

public sealed class ProviderRegistry
{
    private static readonly string[] AutoPriority = { "gemini", "groq", "nvidia", "cerebras", "copilot", "codex" };
    private readonly LlmRouter _llmRouter;
    private readonly CopilotCliWrapper _copilotWrapper;
    private readonly CodexCliWrapper _codexWrapper;

    public ProviderRegistry(LlmRouter llmRouter, CopilotCliWrapper copilotWrapper, CodexCliWrapper codexWrapper)
    {
        _llmRouter = llmRouter;
        _copilotWrapper = copilotWrapper;
        _codexWrapper = codexWrapper;
    }

    public async Task<IReadOnlyList<string>> GetAvailableProvidersAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetAvailabilitySnapshotAsync(cancellationToken);
        return snapshot
            .Where(item => item.Available)
            .Select(item => item.Provider)
            .ToArray();
    }

    public async Task<string> ResolveAutoProviderAsync(CancellationToken cancellationToken)
    {
        var available = await GetAvailableProvidersAsync(cancellationToken);
        foreach (var provider in AutoPriority)
        {
            if (available.Contains(provider, StringComparer.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return "none";
    }

    public async Task<IReadOnlyList<ProviderAvailability>> GetAvailabilitySnapshotAsync(CancellationToken cancellationToken)
    {
        var items = new List<ProviderAvailability>(6)
        {
            _llmRouter.HasGeminiApiKey()
                ? new ProviderAvailability("gemini", true, "configured", true, true, true, false, true)
                : new ProviderAvailability("gemini", false, "api_key_missing", true, true, true, false, true),
            _llmRouter.HasGroqApiKey()
                ? new ProviderAvailability("groq", true, "configured", false, true, false, false, true)
                : new ProviderAvailability("groq", false, "api_key_missing", false, true, false, false, true),
            _llmRouter.HasNvidiaApiKey()
                ? new ProviderAvailability("nvidia", true, "configured", false, true, false, false, true)
                : new ProviderAvailability("nvidia", false, "api_key_missing", false, true, false, false, true),
            _llmRouter.HasCerebrasApiKey()
                ? new ProviderAvailability("cerebras", true, "configured", false, true, false, false, true)
                : new ProviderAvailability("cerebras", false, "api_key_missing", false, true, false, false, true)
        };

        var copilot = await _copilotWrapper.GetStatusAsync(cancellationToken);
        items.Add(copilot.Installed && copilot.Authenticated
            ? new ProviderAvailability("copilot", true, "ready", false, true, false, true, false)
            : new ProviderAvailability("copilot", false, "not_ready", false, true, false, true, false));
        var codex = await _codexWrapper.GetStatusAsync(cancellationToken);
        items.Add(codex.Installed && codex.Authenticated
            ? new ProviderAvailability("codex", true, "ready", false, true, false, true, false)
            : new ProviderAvailability("codex", false, "not_ready", false, true, false, true, false));

        return items;
    }
}
