using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ProviderModelSelectionPolicyTests
{
    [Theory]
    [InlineData("copilot")]
    [InlineData(" Copilot ")]
    public void IsPinnedCopilotProviderMatchesCopilotOnly(string provider)
    {
        Assert.True(ProviderModelSelectionPolicy.IsPinnedCopilotProvider(provider));
    }

    [Fact]
    public void IsPinnedCopilotProviderRejectsOtherProviders()
    {
        Assert.False(ProviderModelSelectionPolicy.IsPinnedCopilotProvider("gemini"));
        Assert.False(ProviderModelSelectionPolicy.IsPinnedCopilotProvider(null));
    }

    [Fact]
    public void NormalizePinnedProviderModelSelectionPinsCopilotModel()
    {
        var result = ProviderModelSelectionPolicy.NormalizePinnedProviderModelSelection(
            "copilot",
            "custom",
            "gpt-5-mini",
            model => model?.Trim()
        );

        Assert.Equal("gpt-5-mini", result);
    }

    [Fact]
    public void NormalizePinnedProviderModelSelectionDelegatesNonCopilotModelNormalization()
    {
        var result = ProviderModelSelectionPolicy.NormalizePinnedProviderModelSelection(
            "gemini",
            " gemini-2.5-pro ",
            "gpt-5-mini",
            model => model?.Trim()
        );

        Assert.Equal("gemini-2.5-pro", result);
    }

    [Fact]
    public void IsPinnedCopilotModelChecksProviderAndDefaultModel()
    {
        Assert.True(ProviderModelSelectionPolicy.IsPinnedCopilotModel("copilot", " GPT-5-MINI ", "gpt-5-mini"));
        Assert.False(ProviderModelSelectionPolicy.IsPinnedCopilotModel("copilot", "other", "gpt-5-mini"));
        Assert.False(ProviderModelSelectionPolicy.IsPinnedCopilotModel("codex", "gpt-5-mini", "gpt-5-mini"));
    }
}
