using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class SearchQueryPolicyTests
{
    [Theory]
    [InlineData("최신 NVIDIA 뉴스 5건", true, "fast:true:heuristic")]
    [InlineData("웹에서 React 20 릴리즈 검색해줘", true, "fast:true:explicit_web")]
    [InlineData("지금 몇 시야?", false, "heuristic:false:non_web")]
    [InlineData("안녕", false, "heuristic:false:non_web")]
    public void BuildFastRequirementDecisionUsesHeuristics(string input, bool required, string label)
    {
        var decision = SearchQueryPolicy.BuildFastRequirementDecision(input);

        Assert.Equal(required, decision.Required);
        Assert.Equal(label, decision.DecisionLabel);
    }

    [Fact]
    public void BuildFastRequirementDecisionExtractsSourceHints()
    {
        var decision = SearchQueryPolicy.BuildFastRequirementDecision("CNN 주요 뉴스 site:cnn.com 3건");

        Assert.True(decision.Required);
        Assert.Equal("CNN", decision.SourceFocus);
        Assert.Equal("cnn.com", decision.SourceDomain);
    }

    [Theory]
    [InlineData("{\"needWeb\":\"YES\",\"sourceFocus\":\"BBC\",\"sourceDomain\":\"https://www.bbc.com/\"}", true, "BBC", "bbc.com")]
    [InlineData("{\"needWeb\":true,\"sourceFocus\":\"Reuters\",\"sourceDomain\":\"reuters.com\"}", true, "Reuters", "reuters.com")]
    [InlineData("prefix {\"needWeb\":\"NO\"} suffix", false, "", "")]
    public void TryParseSearchRequirementDecisionJsonParsesSupportedShapes(
        string raw,
        bool expectedNeedWeb,
        string expectedFocus,
        string expectedDomain
    )
    {
        var parsed = SearchQueryPolicy.TryParseSearchRequirementDecisionJson(
            raw,
            out var needWeb,
            out var sourceFocus,
            out var sourceDomain
        );

        Assert.True(parsed);
        Assert.Equal(expectedNeedWeb, needWeb);
        Assert.Equal(expectedFocus, sourceFocus);
        Assert.Equal(expectedDomain, sourceDomain);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"needWeb\":\"maybe\"}")]
    [InlineData("{\"sourceFocus\":\"CNN\"}")]
    public void TryParseSearchRequirementDecisionJsonRejectsInvalidInput(string raw)
    {
        Assert.False(SearchQueryPolicy.TryParseSearchRequirementDecisionJson(raw, out _, out _, out _));
    }

    [Theory]
    [InlineData("YES", "yes")]
    [InlineData("필요합니다", "yes")]
    [InlineData("NO", "no")]
    [InlineData("불필요", "no")]
    [InlineData("maybe", "")]
    public void NormalizeWebSearchDecisionTokenNormalizesLlmOutput(string raw, string expected)
    {
        Assert.Equal(expected, SearchQueryPolicy.NormalizeWebSearchDecisionToken(raw));
    }

    [Theory]
    [InlineData("오늘 뉴스 7건", 7)]
    [InlineData("top 3 ai news", 3)]
    [InlineData("최신 헤드라인", 10)]
    [InlineData("일반 검색", 5)]
    public void ResolveRequestedResultCountFromQueryUsesCountSignals(string input, int expected)
    {
        Assert.Equal(expected, SearchQueryPolicy.ResolveRequestedResultCountFromQuery(input));
    }

    [Theory]
    [InlineData("오늘 속보", "day")]
    [InlineData("이번달 동향", "month")]
    [InlineData("올해 정리", "year")]
    [InlineData("최근 자료", "week")]
    public void ResolveSearchFreshnessForQueryMapsTimeWindows(string input, string expected)
    {
        Assert.Equal(expected, SearchQueryPolicy.ResolveSearchFreshnessForQuery(input));
    }

    [Fact]
    public void BuildEffectiveSearchQueryAddsHeadlineTermsForNewsList()
    {
        var decision = new SearchRequirementDecision(true, "fast:true:heuristic", "", "");

        var query = SearchQueryPolicy.BuildEffectiveSearchQuery(
            "오늘 주요 뉴스",
            decision,
            (_, _) => string.Empty
        );

        Assert.Equal("오늘 주요 뉴스 latest breaking headlines", query);
    }

    [Fact]
    public void BuildEffectiveSearchQueryAddsSourceFocusAndResolvedDomain()
    {
        var decision = new SearchRequirementDecision(true, "llm:true", "CNN", "");

        var query = SearchQueryPolicy.BuildEffectiveSearchQuery(
            "오늘 주요 뉴스",
            decision,
            (_, _) => "cnn.com"
        );

        Assert.Equal("오늘 주요 뉴스 CNN cnn.com CNN official top headlines", query);
    }

    [Theory]
    [InlineData("지금 몇시야?", true)]
    [InlineData("runtime complexity 알려줘", false)]
    public void LooksLikeLocalDateTimeQuestionAvoidsRuntimeComplexityFalsePositive(string input, bool expected)
    {
        Assert.Equal(expected, SearchQueryPolicy.LooksLikeLocalDateTimeQuestion(input));
    }

    [Fact]
    public void ExtractWebPreferenceHintsClassifiesConversationSourcePreference()
    {
        var hints = SearchQueryPolicy.ExtractWebPreferenceHints("앞으로 뉴스는 CNN 출처를 선호해", fromMemoryNote: false);

        var hint = Assert.Single(hints);
        Assert.Equal("source", hint.Category);
        Assert.Equal("앞으로 뉴스는 CNN 출처를 선호해", hint.Text);
    }

    [Fact]
    public void ExtractWebPreferenceHintsSkipsMemoryMetadata()
    {
        var hints = SearchQueryPolicy.ExtractWebPreferenceHints(
            """
            created_utc: 2026-05-18T00:00:00Z
            provider: gemini
            앞으로 뉴스는 Reuters 출처를 선호해
            """,
            fromMemoryNote: true
        );

        var hint = Assert.Single(hints);
        Assert.Equal("source", hint.Category);
        Assert.Equal("앞으로 뉴스는 Reuters 출처를 선호해", hint.Text);
    }

    [Theory]
    [InlineData("이번에는 BBC 말고 CNN으로 찾아줘", true)]
    [InlineData("오늘 뉴스 5건 알려줘", false)]
    public void ShouldBlockWebMemoryHintByOverrideDetectsOneShotOverrides(string input, bool expected)
    {
        Assert.Equal(expected, SearchQueryPolicy.ShouldBlockWebMemoryHintByOverride(input));
    }

    [Theory]
    [InlineData("표로 정리해줘", true, false, false)]
    [InlineData("짧게 요약해줘", false, true, false)]
    [InlineData("영어로 답해줘", false, false, true)]
    [InlineData("오늘 뉴스 알려줘", false, false, false)]
    public void WebDirectiveDetectorsClassifyFormatToneAndLanguage(
        string input,
        bool expectedFormat,
        bool expectedTone,
        bool expectedLanguage
    )
    {
        Assert.Equal(expectedFormat, SearchQueryPolicy.LooksLikeWebFormatDirective(input));
        Assert.Equal(expectedTone, SearchQueryPolicy.LooksLikeWebToneDirective(input));
        Assert.Equal(expectedLanguage, SearchQueryPolicy.LooksLikeWebLanguageDirective(input));
    }

    [Theory]
    [InlineData("최신 뉴스 알려줘", 12, 4, 12)]
    [InlineData("일반 검색 결과 알려줘", 12, 4, 4)]
    [InlineData("속보 정리", 99, 0, 20)]
    [InlineData("문서 목록", 99, 0, 1)]
    public void ResolveWebDefaultCountUsesNewsOrListDefaultsWithClamp(
        string input,
        int newsDefault,
        int listDefault,
        int expected
    )
    {
        Assert.Equal(expected, SearchQueryPolicy.ResolveWebDefaultCount(input, newsDefault, listDefault));
    }
}
