namespace OmniNode.Middleware;

internal static class ProviderModelSelectionPolicy
{
    public static bool IsPinnedCopilotProvider(string? provider)
    {
        return string.Equals((provider ?? string.Empty).Trim(), "copilot", StringComparison.OrdinalIgnoreCase);
    }

    public static string? NormalizePinnedProviderModelSelection(
        string provider,
        string? modelOverride,
        string defaultCopilotModel,
        Func<string?, string?> normalizeModelSelection
    )
    {
        if (IsPinnedCopilotProvider(provider))
        {
            return defaultCopilotModel;
        }

        return normalizeModelSelection(modelOverride);
    }

    public static bool IsPinnedCopilotModel(string provider, string model, string defaultCopilotModel)
    {
        return IsPinnedCopilotProvider(provider)
            && string.Equals((model ?? string.Empty).Trim(), defaultCopilotModel, StringComparison.OrdinalIgnoreCase);
    }
}
