using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class TelegramPromptPolicyTests
{
    [Fact]
    public void ResolveThinkingLevelUsesCodeProfileDefault()
    {
        var prefs = new TelegramLlmPreferences
        {
            Profile = "code",
            CodeThinkingLevel = "high",
            TalkThinkingLevel = "low"
        };

        Assert.Equal("high", TelegramPromptPolicy.ResolveThinkingLevel(prefs, "간단히 알려줘"));
    }

    [Fact]
    public void ResolveThinkingLevelEscalatesDecisionQuestion()
    {
        var prefs = new TelegramLlmPreferences
        {
            Profile = "talk",
            TalkThinkingLevel = "low"
        };

        Assert.Equal("high", TelegramPromptPolicy.ResolveThinkingLevel(prefs, "둘 중 뭐가 더 안전한지 비교하고 추천해줘"));
    }

    [Fact]
    public void BuildConcisePromptPreservesRequestedListCount()
    {
        var prompt = TelegramPromptPolicy.BuildConcisePrompt("AI 뉴스 8개 알려줘", "2026-05-19 10:00 KST");

        Assert.Contains("요청 건수(8)", prompt);
        Assert.Contains("2026-05-19 10:00 KST", prompt);
    }

    [Fact]
    public void BuildProfilePromptIncludesModeAndThinking()
    {
        var prompt = TelegramPromptPolicy.BuildProfilePrompt("질문", "code", "high", "now");

        Assert.Contains("코딩 모드", prompt);
        Assert.Contains("사고 강도: high", prompt);
        Assert.Contains("질문", prompt);
    }

    [Fact]
    public void BuildOrchestrationPromptListsWorkerRoles()
    {
        var prompt = TelegramPromptPolicy.BuildOrchestrationPrompt(
            "검토해줘",
            new[]
            {
                new LlmSingleChatResult("groq", "m1", "빠른 답"),
                new LlmSingleChatResult("gemini", "m2", "근거 답")
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["groq"] = "빠른 초안",
                ["gemini"] = "검증"
            }
        );

        Assert.Contains("groq:m1 => 빠른 초안", prompt);
        Assert.Contains("gemini:m2", prompt);
        Assert.Contains("내부 역할 분담 과정은 드러내지", prompt);
    }

    [Fact]
    public void ModelShowsUncertaintyDetectsWeakAnswer()
    {
        Assert.True(TelegramPromptPolicy.ModelShowsUncertainty("근거 부족이라 확실하지 않습니다."));
        Assert.False(TelegramPromptPolicy.ModelShowsUncertainty("가능합니다. 핵심 근거는 두 가지입니다."));
    }
}
