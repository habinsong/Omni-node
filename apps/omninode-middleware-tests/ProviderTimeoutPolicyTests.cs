using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ProviderTimeoutPolicyTests
{
    [Theory]
    [InlineData("groq", 90)]
    [InlineData("gemini", 90)]
    [InlineData("copilot", 120)]
    [InlineData("codex", 120)]
    [InlineData("cerebras", 120)]
    [InlineData("nvidia", 360)]
    [InlineData("unknown", 20)]
    public void ResolveSingleChatTimeoutSecondsKeepsExistingFloors(string provider, int expectedSeconds)
    {
        var timeout = ProviderTimeoutPolicy.ResolveSingleChatTimeoutSeconds(
            provider,
            CreateProviders(cerebrasTimeoutSec: 40, nvidiaTimeoutSec: 180),
            CreateContext(llmTimeoutSec: 20, geminiWebTimeoutMs: 30000)
        );

        Assert.Equal(expectedSeconds, timeout);
    }

    [Fact]
    public void ResolveSingleChatTimeoutSecondsHonorsPositiveOverride()
    {
        var timeout = ProviderTimeoutPolicy.ResolveSingleChatTimeoutSeconds(
            "nvidia",
            CreateProviders(cerebrasTimeoutSec: 40, nvidiaTimeoutSec: 180),
            CreateContext(llmTimeoutSec: 20, geminiWebTimeoutMs: 30000),
            overrideSeconds: 12
        );

        Assert.Equal(12, timeout);
    }

    [Theory]
    [InlineData(0, 30000)]
    [InlineData(3000, 5000)]
    [InlineData(61000, 60000)]
    [InlineData(45000, 45000)]
    public void NormalizeGeminiGroundedTimeoutMsClampsToSupportedRange(int inputMs, int expectedMs)
    {
        Assert.Equal(expectedMs, ProviderTimeoutPolicy.NormalizeGeminiGroundedTimeoutMs(inputMs));
    }

    [Fact]
    public void ResolveSharedHttpTimeoutCoversProviderAndGeminiWebTimeouts()
    {
        var timeout = ProviderTimeoutPolicy.ResolveSharedHttpTimeout(
            CreateProviders(cerebrasTimeoutSec: 40, nvidiaTimeoutSec: 180),
            CreateContext(llmTimeoutSec: 20, geminiWebTimeoutMs: 30000)
        );

        Assert.Equal(TimeSpan.FromSeconds(185), timeout);
    }

    private static ProviderOptions CreateProviders(int cerebrasTimeoutSec, int nvidiaTimeoutSec)
        => new(
            CopilotCliBinary: "gh",
            CopilotDirectBinary: "copilot",
            CopilotModel: "gpt-5-mini",
            CodexBinary: "codex",
            CodexModel: "gpt-5.3-codex",
            PythonBinary: "python3",
            GroqApiKey: null,
            GroqBaseUrl: "https://api.groq.com/openai/v1",
            GroqModel: "llama",
            GeminiApiKey: null,
            GeminiBaseUrl: "https://generativelanguage.googleapis.com/v1beta",
            GeminiModel: "gemini",
            GeminiSearchModel: "gemini-search",
            CerebrasBaseUrl: "https://api.cerebras.ai/v1",
            CerebrasModel: "cerebras",
            CerebrasTimeoutSec: cerebrasTimeoutSec,
            CerebrasKeychainService: "cerebras",
            CerebrasKeychainAccount: "omninode",
            CerebrasApiKey: null,
            NvidiaBaseUrl: "https://integrate.api.nvidia.com/v1",
            NvidiaModel: "nvidia",
            NvidiaTimeoutSec: nvidiaTimeoutSec,
            NvidiaKeychainService: "nvidia",
            NvidiaKeychainAccount: "omninode",
            NvidiaApiKey: null,
            CodexApiKey: null,
            SttProvider: string.Empty,
            SttBaseUrl: string.Empty,
            SttModel: string.Empty,
            SttApiKey: null,
            GeminiInputPricePerMillionUsd: 0.50m,
            GeminiOutputPricePerMillionUsd: 3.00m
        );

    private static ContextOptions CreateContext(int llmTimeoutSec, int geminiWebTimeoutMs)
        => new(
            ConversationCompressChars: 12000,
            ConversationKeepRecentMessages: 16,
            ConversationHistoryMessages: 18,
            CodingAgentMaxIterations: 6,
            CodingAgentMaxActionsPerIteration: 8,
            CodingCopilotMaxActionsPerIteration: 2,
            CodingWorkspaceSnapshotMaxEntries: 80,
            CodingRecentLoopHistoryForCopilot: 2,
            CodingEnableOneShotUiClone: true,
            ChatMaxOutputTokens: 8192,
            CodingMaxOutputTokens: 16384,
            LlmTimeoutSec: llmTimeoutSec,
            SingleChatDefaultTimeoutSec: 34,
            CerebrasMinSingleChatTimeoutSec: 40,
            NvidiaMinSingleChatTimeoutSec: 30,
            EnableFastWebPipeline: true,
            WebDecisionTimeoutMs: 700,
            GeminiWebTimeoutMs: geminiWebTimeoutMs,
            WebDefaultNewsCount: 10,
            WebDefaultListCount: 5,
            CommandMaxLength: 800,
            MetricsPushIntervalSec: 2
        );
}
