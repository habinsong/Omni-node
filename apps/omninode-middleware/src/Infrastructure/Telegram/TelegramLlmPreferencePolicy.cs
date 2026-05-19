namespace OmniNode.Middleware;

internal sealed record TelegramQuickModelSelection(
    string Provider,
    string Model,
    bool AutoGroqComplexUpgrade,
    string ProviderDisplayName
);

internal static class TelegramLlmPreferencePolicy
{
    public static string NormalizeThinkingLevel(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized == "low" || normalized == "high")
        {
            return normalized;
        }

        return fallback;
    }

    public static void ApplyTalkDefaults(
        TelegramLlmPreferences preferences,
        string requestedThinking,
        string fastModel,
        string geminiModel,
        string defaultCopilotModel,
        string cerebrasModel,
        string codexModel
    )
    {
        preferences.Profile = "talk";
        preferences.Mode = "orchestration";
        preferences.SingleProvider = "groq";
        preferences.SingleModel = fastModel;
        preferences.AutoGroqComplexUpgrade = true;
        preferences.OrchestrationProvider = "gemini";
        preferences.OrchestrationModel = geminiModel;
        preferences.MultiGroqModel = fastModel;
        preferences.MultiGeminiModel = geminiModel;
        preferences.MultiCopilotModel = defaultCopilotModel;
        preferences.MultiCerebrasModel = cerebrasModel;
        preferences.MultiCodexModel = codexModel;
        preferences.MultiSummaryProvider = "gemini";
        preferences.TalkThinkingLevel = NormalizeThinkingLevel(requestedThinking, "low");
    }

    public static void ApplyCodeDefaults(
        TelegramLlmPreferences preferences,
        string requestedThinking,
        string fastModel,
        string geminiModel,
        string defaultCopilotModel,
        string cerebrasModel,
        string codexModel
    )
    {
        preferences.Profile = "code";
        preferences.Mode = "orchestration";
        preferences.SingleProvider = "copilot";
        preferences.SingleModel = defaultCopilotModel;
        preferences.AutoGroqComplexUpgrade = false;
        preferences.OrchestrationProvider = "gemini";
        preferences.OrchestrationModel = geminiModel;
        preferences.MultiGroqModel = fastModel;
        preferences.MultiGeminiModel = geminiModel;
        preferences.MultiCopilotModel = defaultCopilotModel;
        preferences.MultiCerebrasModel = cerebrasModel;
        preferences.MultiCodexModel = codexModel;
        preferences.MultiSummaryProvider = "gemini";
        preferences.CodeThinkingLevel = NormalizeThinkingLevel(requestedThinking, "high");
    }

    public static TelegramQuickModelSelection? ResolveQuickModelSelection(
        string key,
        string groqModel,
        string defaultGroqPrimaryModel,
        string geminiModel,
        string defaultCopilotModel,
        string cerebrasModel,
        string nvidiaModel,
        string codexModel
    )
    {
        var normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
        var selectedGroqModel = string.IsNullOrWhiteSpace(groqModel) ? defaultGroqPrimaryModel : groqModel;
        return normalized switch
        {
            "groq" => new TelegramQuickModelSelection("groq", selectedGroqModel, true, "Groq"),
            "gemini" => new TelegramQuickModelSelection("gemini", geminiModel, false, "Gemini"),
            "copilot" => new TelegramQuickModelSelection("copilot", defaultCopilotModel, false, "Copilot"),
            "cerebras" => new TelegramQuickModelSelection("cerebras", cerebrasModel, false, "Cerebras"),
            "nvidia" or "nvidia-nim" or "nvidia_nim" or "nim" => new TelegramQuickModelSelection("nvidia", nvidiaModel, false, "NVIDIA NIM"),
            "codex" => new TelegramQuickModelSelection("codex", codexModel, false, "Codex"),
            _ => null
        };
    }
}
