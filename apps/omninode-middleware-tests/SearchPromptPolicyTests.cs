using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class SearchPromptPolicyTests
{
    [Fact]
    public void BuildWebNeedDecisionPromptRequiresJsonOnlyDecision()
    {
        var prompt = SearchPromptPolicy.BuildWebNeedDecisionPrompt("오늘 NVIDIA 뉴스 알려줘");

        Assert.Contains("JSON 한 줄만 출력", prompt);
        Assert.Contains("\"need_web\":true|false", prompt);
        Assert.Contains("오늘 NVIDIA 뉴스 알려줘", prompt);
    }

    [Fact]
    public void BuildGeminiWebAnswerPromptIncludesSourceFocusAndTableRules()
    {
        var prompt = SearchPromptPolicy.BuildGeminiWebAnswerPrompt(
            "CNN 주요 뉴스 표로 6건",
            "",
            selfDecideNeedWeb: false,
            allowMarkdownTable: true,
            enforceTelegramOutputStyle: true,
            webDefaultNewsCount: 10,
            webDefaultListCount: 5,
            (_, _) => "cnn.com"
        );

        Assert.Contains("표의 헤더/구분선/데이터 행은 모두 '|'", prompt);
        Assert.Contains("사용자가 요구한 소스 초점: CNN", prompt);
        Assert.Contains("cnn.com 원출처 기사 우선", prompt);
        Assert.Contains("표의 데이터 행은 가능하면 6개", prompt);
        Assert.Contains("출력 채널은 텔레그램", prompt);
    }

    [Fact]
    public void BuildGeminiUrlContextAnswerPromptIncludesRepositoryContext()
    {
        var prompt = SearchPromptPolicy.BuildGeminiUrlContextAnswerPrompt(
            "https://github.com/openai/openai-dotnet",
            new[] { "https://github.com/openai/openai-dotnet" },
            "",
            allowMarkdownTable: false,
            enforceTelegramOutputStyle: false,
            includeGoogleSearch: false,
            webDefaultNewsCount: 10,
            webDefaultListCount: 5,
            new SearchPromptRepositoryContext("openai/openai-dotnet", "OpenAI .NET SDK", "README excerpt")
        );

        Assert.Contains("코드 저장소/프로젝트 소개 요청", prompt);
        Assert.Contains("[직접 읽은 저장소 정보]", prompt);
        Assert.Contains("저장소: openai/openai-dotnet", prompt);
        Assert.Contains("README excerpt", prompt);
    }

    [Theory]
    [InlineData("오늘 뉴스 알려줘", 10, 5, 1280)]
    [InlineData("오늘 뉴스 8건 알려줘", 10, 5, 1280)]
    [InlineData("표로 길게 비교해줘 abcdefghijklmnopqrstuvwxyz abcdefghijklmnopqrstuvwxyz abcdefghijklmnopqrstuvwxyz", 10, 5, 1280)]
    public void ResolveGeminiWebAnswerMaxOutputTokensUsesModeAndCount(
        string input,
        int newsDefault,
        int listDefault,
        int expected
    )
    {
        Assert.Equal(expected, SearchPromptPolicy.ResolveGeminiWebAnswerMaxOutputTokens(input, newsDefault, listDefault));
    }

    [Theory]
    [InlineData("일반 URL 설명", 10, 5, 2048)]
    [InlineData("URL 내용 8건 목록", 10, 5, 4096)]
    public void ResolveGeminiUrlContextMaxOutputTokensUsesLargerBudgetForStructuredAnswers(
        string input,
        int newsDefault,
        int listDefault,
        int expected
    )
    {
        Assert.Equal(expected, SearchPromptPolicy.ResolveGeminiUrlContextMaxOutputTokens(input, newsDefault, listDefault));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Gemini URL 참조 응답 시간이 초과되었습니다.", true)]
    [InlineData("정상 응답입니다.", false)]
    public void IsGeminiUrlContextFailureTextDetectsFailureMessages(string text, bool expected)
    {
        Assert.Equal(expected, SearchPromptPolicy.IsGeminiUrlContextFailureText(text));
    }

    [Fact]
    public void TryParseNeedWebDecisionJsonParsesSnakeAndCamelCase()
    {
        Assert.True(SearchPromptPolicy.TryParseNeedWebDecisionJson("{\"need_web\":true,\"reason\":\"news\"}", out var snakeNeedWeb, out var snakeReason));
        Assert.True(snakeNeedWeb);
        Assert.Equal("news", snakeReason);

        Assert.True(SearchPromptPolicy.TryParseNeedWebDecisionJson("{\"needWeb\":\"NO\"}", out var camelNeedWeb, out var camelReason));
        Assert.False(camelNeedWeb);
        Assert.Equal("", camelReason);
    }
}
