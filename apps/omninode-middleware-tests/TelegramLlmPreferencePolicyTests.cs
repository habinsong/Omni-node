using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class TelegramLlmPreferencePolicyTests
{
    [Theory]
    [InlineData("low", "auto", "low")]
    [InlineData("HIGH", "auto", "high")]
    [InlineData("medium", "auto", "auto")]
    public void NormalizeThinkingLevelKeepsOnlySupportedValues(string? value, string fallback, string expected)
    {
        Assert.Equal(expected, TelegramLlmPreferencePolicy.NormalizeThinkingLevel(value, fallback));
    }

    [Fact]
    public void ApplyTalkDefaultsBuildsTalkOrchestrationPreset()
    {
        var preferences = new TelegramLlmPreferences();

        TelegramLlmPreferencePolicy.ApplyTalkDefaults(
            preferences,
            "invalid",
            "groq-fast",
            "gemini-pro",
            "copilot-default",
            "cerebras-default",
            "codex-default"
        );

        Assert.Equal("talk", preferences.Profile);
        Assert.Equal("orchestration", preferences.Mode);
        Assert.Equal("groq", preferences.SingleProvider);
        Assert.Equal("groq-fast", preferences.SingleModel);
        Assert.True(preferences.AutoGroqComplexUpgrade);
        Assert.Equal("gemini", preferences.OrchestrationProvider);
        Assert.Equal("gemini-pro", preferences.MultiGeminiModel);
        Assert.Equal("low", preferences.TalkThinkingLevel);
    }

    [Fact]
    public void ApplyCodeDefaultsBuildsCodeOrchestrationPreset()
    {
        var preferences = new TelegramLlmPreferences();

        TelegramLlmPreferencePolicy.ApplyCodeDefaults(
            preferences,
            "low",
            "groq-fast",
            "gemini-pro",
            "copilot-default",
            "cerebras-default",
            "codex-default"
        );

        Assert.Equal("code", preferences.Profile);
        Assert.Equal("orchestration", preferences.Mode);
        Assert.Equal("copilot", preferences.SingleProvider);
        Assert.Equal("copilot-default", preferences.SingleModel);
        Assert.False(preferences.AutoGroqComplexUpgrade);
        Assert.Equal("gemini", preferences.OrchestrationProvider);
        Assert.Equal("low", preferences.CodeThinkingLevel);
    }

    [Theory]
    [InlineData("groq", "groq-config", "groq", "groq-config", true, "Groq")]
    [InlineData("groq", "", "groq", "groq-default", true, "Groq")]
    [InlineData("nvidia-nim", "groq-config", "nvidia", "nvidia-default", false, "NVIDIA NIM")]
    [InlineData("codex", "groq-config", "codex", "codex-default", false, "Codex")]
    public void ResolveQuickModelSelectionMapsSupportedProviders(
        string key,
        string groqModel,
        string provider,
        string model,
        bool autoGroq,
        string displayName
    )
    {
        var selection = TelegramLlmPreferencePolicy.ResolveQuickModelSelection(
            key,
            groqModel,
            "groq-default",
            "gemini-default",
            "copilot-default",
            "cerebras-default",
            "nvidia-default",
            "codex-default"
        );

        Assert.NotNull(selection);
        Assert.Equal(provider, selection.Provider);
        Assert.Equal(model, selection.Model);
        Assert.Equal(autoGroq, selection.AutoGroqComplexUpgrade);
        Assert.Equal(displayName, selection.ProviderDisplayName);
    }

    [Fact]
    public void ResolveQuickModelSelectionRejectsUnknownProvider()
    {
        Assert.Null(TelegramLlmPreferencePolicy.ResolveQuickModelSelection(
            "unknown",
            "groq-config",
            "groq-default",
            "gemini-default",
            "copilot-default",
            "cerebras-default",
            "nvidia-default",
            "codex-default"
        ));
    }
}
