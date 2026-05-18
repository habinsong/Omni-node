namespace OmniNode.Middleware;

internal sealed class LlmPreferenceContext
{
    public object TelegramLlmLock { get; } = new();
    public object WebLlmLock { get; } = new();
    public object TelegramCodingLock { get; } = new();
    public object TelegramRefactorLock { get; } = new();

    public TelegramLlmPreferences TelegramLlmPreferences { get; }
    public TelegramCodingPreferences TelegramCodingPreferences { get; }
    public TelegramRefactorSession TelegramRefactorSession { get; }
    public WebLlmPreferences WebLlmPreferences { get; }

    internal LlmPreferenceContext(AppConfig config, string? selectedCopilotModel)
    {
        var fastGroqModel = string.IsNullOrWhiteSpace(config.GroqModel)
            ? "meta-llama/llama-4-scout-17b-16e-instruct"
            : config.GroqModel;
        var defaultCopilotModel = string.IsNullOrWhiteSpace(selectedCopilotModel)
            ? "gpt-5-mini"
            : selectedCopilotModel;

        TelegramLlmPreferences = new TelegramLlmPreferences
        {
            Profile = "default",
            Mode = "single",
            SingleProvider = "groq",
            SingleModel = fastGroqModel,
            AutoGroqComplexUpgrade = true,
            OrchestrationProvider = "auto",
            OrchestrationModel = fastGroqModel,
            MultiGroqModel = fastGroqModel,
            MultiGeminiModel = config.GeminiModel,
            MultiCopilotModel = defaultCopilotModel,
            MultiCerebrasModel = config.CerebrasModel,
            MultiNvidiaModel = config.NvidiaModel,
            MultiCodexModel = config.CodexModel,
            MultiSummaryProvider = "auto",
            TalkThinkingLevel = "low",
            CodeThinkingLevel = "high"
        };

        TelegramCodingPreferences = new TelegramCodingPreferences
        {
            Mode = "orchestration",
            SingleProvider = "copilot",
            SingleModel = defaultCopilotModel,
            SingleLanguage = "auto",
            OrchestrationProvider = "auto",
            OrchestrationModel = string.Empty,
            OrchestrationLanguage = "auto",
            OrchestrationGroqModel = fastGroqModel,
            OrchestrationGeminiModel = config.GeminiModel,
            OrchestrationCerebrasModel = config.CerebrasModel,
            OrchestrationNvidiaModel = config.NvidiaModel,
            OrchestrationCopilotModel = "none",
            OrchestrationCodexModel = "none",
            MultiProvider = "gemini",
            MultiModel = config.GeminiModel,
            MultiLanguage = "auto",
            MultiGroqModel = fastGroqModel,
            MultiGeminiModel = config.GeminiModel,
            MultiCerebrasModel = config.CerebrasModel,
            MultiNvidiaModel = config.NvidiaModel,
            MultiCopilotModel = "none",
            MultiCodexModel = "none"
        };

        TelegramRefactorSession = new TelegramRefactorSession();

        WebLlmPreferences = new WebLlmPreferences
        {
            Profile = "default",
            Mode = "single",
            SingleProvider = "groq",
            SingleModel = fastGroqModel,
            AutoGroqComplexUpgrade = true,
            OrchestrationProvider = "auto",
            OrchestrationModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? config.GroqModel : config.GeminiModel,
            MultiGroqModel = fastGroqModel,
            MultiGeminiModel = config.GeminiModel,
            MultiCopilotModel = defaultCopilotModel,
            MultiCerebrasModel = config.CerebrasModel,
            MultiNvidiaModel = config.NvidiaModel,
            MultiCodexModel = config.CodexModel,
            MultiSummaryProvider = "auto",
            TalkThinkingLevel = "low",
            CodeThinkingLevel = "high"
        };
    }
}
