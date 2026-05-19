using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class MultiComparisonPolicyTests
{
    [Fact]
    public void ShouldIncludeComparisonEntrySkipsUnselectedEmptyWorker()
    {
        Assert.False(MultiComparisonPolicy.ShouldIncludeComparisonEntry("none", ""));
        Assert.False(MultiComparisonPolicy.ShouldIncludeComparisonEntry("none", "선택 안함"));
        Assert.True(MultiComparisonPolicy.ShouldIncludeComparisonEntry("none", "fallback text"));
    }

    [Fact]
    public void BuildComparisonAssistantTextIncludesSelectedProvidersAndEscapesJson()
    {
        var text = MultiComparisonPolicy.BuildComparisonAssistantText(new LlmMultiChatResult(
            GroqText: "hello \"world\"",
            GeminiText: "선택 안함",
            CerebrasText: "",
            CopilotText: "",
            Summary: "",
            GroqModel: "groq-model",
            GeminiModel: "none",
            CerebrasModel: "none",
            CopilotModel: "none",
            RequestedSummaryProvider: "auto",
            ResolvedSummaryProvider: "groq"
        ));

        Assert.StartsWith("[[OMNI_MULTI_COMPARE_JSON]]", text);
        Assert.Contains("\"provider\":\"groq\"", text);
        Assert.Contains("hello \\\"world\\\"", text);
        Assert.DoesNotContain("\"provider\":\"gemini\"", text);
    }

    [Fact]
    public void ParseComparisonSummarySectionsReadsKnownHeadings()
    {
        var parsed = MultiComparisonPolicy.ParseComparisonSummarySections(
            """
            공통 요약:
            같은 결론입니다.

            공통점:
            같은 근거입니다.

            부분 차이:
            모델별 표현이 다릅니다.

            추천:
            groq를 씁니다.
            """
        );

        Assert.Equal("같은 결론입니다.", parsed.CommonSummary);
        Assert.Equal("같은 근거입니다.", parsed.CommonPoints);
        Assert.Equal("모델별 표현이 다릅니다.", parsed.Differences);
        Assert.Equal("groq를 씁니다.", parsed.Recommendation);
    }

    [Fact]
    public void ParseComparisonSummarySectionsUsesFallbacksForBlankInput()
    {
        var parsed = MultiComparisonPolicy.ParseComparisonSummarySections("");

        Assert.Equal("공통 요약을 생성하지 못했습니다.", parsed.CommonSummary);
        Assert.Equal("공통점 없음", parsed.CommonPoints);
        Assert.Equal("부분 차이 정리가 없습니다.", parsed.Differences);
        Assert.Equal("추천 정리가 없습니다.", parsed.Recommendation);
    }

    [Fact]
    public void BuildMultiSummaryAssistantTextUsesFallbackSections()
    {
        var text = MultiComparisonPolicy.BuildMultiSummaryAssistantText("", "", "");

        Assert.Contains("### 공통 요약", text);
        Assert.Contains("공통 요약을 생성하지 못했습니다.", text);
        Assert.Contains("공통점 없음", text);
        Assert.Contains("부분 차이 정리가 없습니다.", text);
    }
}
