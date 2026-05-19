using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingLanguagePolicyTests
{
    [Theory]
    [InlineData("py", "python")]
    [InlineData("python3", "python")]
    [InlineData("node", "javascript")]
    [InlineData("tsx", "typescript")]
    [InlineData("react_vite", "react-vite")]
    [InlineData("c#", "csharp")]
    [InlineData("", "auto")]
    public void NormalizeLanguageForCodeMapsAliases(string input, string expected)
    {
        Assert.Equal(expected, CodingLanguagePolicy.NormalizeLanguageForCode(input));
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("auto", "auto")]
    [InlineData("TS", "typescript")]
    public void NormalizeCodingLanguageHintPreservingAutoKeepsAuto(string? input, string expected)
    {
        Assert.Equal(expected, CodingLanguagePolicy.NormalizeCodingLanguageHintPreservingAuto(input));
    }

    [Theory]
    [InlineData("앱을 파이썬으로 만들어줘", "python")]
    [InlineData("React Vite 화면 구현", "react-vite")]
    [InlineData("자바스크립트로 CLI 작성", "javascript")]
    [InlineData("자바 스프링 예제", "java")]
    [InlineData("javascript 예제", "javascript")]
    public void ResolveExplicitObjectiveLanguageDetectsRequestedLanguage(string objective, string expected)
    {
        Assert.Equal(expected, CodingLanguagePolicy.ResolveExplicitObjectiveLanguage(objective));
    }

    [Fact]
    public void ResolveInitialCodingLanguagePrefersExplicitHint()
    {
        Assert.Equal(
            "typescript",
            CodingLanguagePolicy.ResolveInitialCodingLanguage("ts", "파이썬으로 만들어줘")
        );
    }

    [Theory]
    [InlineData("UI 웹 페이지 클론 만들어줘", "html")]
    [InlineData("러스트 CLI 만들어줘", "rust")]
    [InlineData("별도 힌트 없음", "auto")]
    public void ResolveInitialCodingLanguageFallsBackToObjectiveSignals(string objective, string expected)
    {
        Assert.Equal(expected, CodingLanguagePolicy.ResolveInitialCodingLanguage("auto", objective));
    }

    [Theory]
    [InlineData("src/App.tsx", "auto", "typescript")]
    [InlineData("main.py", "auto", "python")]
    [InlineData("view.svelte", "auto", "html")]
    [InlineData("README.md", "auto", "auto")]
    public void GuessLanguageFromPathUsesExtension(string path, string fallback, string expected)
    {
        Assert.Equal(expected, CodingLanguagePolicy.GuessLanguageFromPath(path, fallback));
    }

    [Fact]
    public void ResolveFinalResultLanguageKeepsHtmlForWebObjective()
    {
        var language = CodingLanguagePolicy.ResolveFinalResultLanguage(
            "javascript",
            "auto",
            "html/css/js 웹 페이지 만들어줘",
            new[] { "/tmp/work/script.js" }
        );

        Assert.Equal("html", language);
    }

    [Fact]
    public void ResolveFinalResultLanguagePromotesJavascriptWhenHtmlFileWasWritten()
    {
        var language = CodingLanguagePolicy.ResolveFinalResultLanguage(
            "javascript",
            "auto",
            "브라우저 스크립트 만들어줘",
            new[] { "/tmp/work/index.html", "/tmp/work/script.js" }
        );

        Assert.Equal("html", language);
    }

    [Fact]
    public void ExtractLatestCodingRequestTextKeepsLastNewRequestBlock()
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(
            """
            [최근 대화]
            이전 요청

            [새 요청]
            React 화면 만들어줘
            [로컬 시간]
            now
            """
        );

        Assert.Equal("React 화면 만들어줘", text);
    }
}
