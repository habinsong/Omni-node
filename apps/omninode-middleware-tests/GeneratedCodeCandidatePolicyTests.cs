using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GeneratedCodeCandidatePolicyTests
{
    [Fact]
    public void BuildCodeGenerationPromptIncludesFormatContract()
    {
        var prompt = GeneratedCodeCandidatePolicy.BuildCodeGenerationPrompt("hello 출력", "python", "single");

        Assert.Contains("LANGUAGE=<실행언어>", prompt);
        Assert.Contains("단 하나의 코드블록", prompt);
        Assert.Contains("모드: single", prompt);
        Assert.Contains("언어 힌트: python", prompt);
    }

    [Fact]
    public void ParseCodeCandidateUsesExplicitLanguagePrefixAndFence()
    {
        var parsed = GeneratedCodeCandidatePolicy.ParseCodeCandidate(
            """
            LANGUAGE=python
            ```py
            print("ok")
            ```
            """,
            "bash"
        );

        Assert.Equal("python", parsed.Language);
        Assert.Equal("print(\"ok\")", parsed.Code);
    }

    [Fact]
    public void ParseCodeCandidateReturnsJsonObjectWhenPresent()
    {
        var parsed = GeneratedCodeCandidatePolicy.ParseCodeCandidate(
            "설명 {\"ok\":true} 끝",
            "json"
        );

        Assert.Equal("json", parsed.Language);
        Assert.Equal("{\"ok\":true}", parsed.Code);
    }

    [Fact]
    public void ParseCodeCandidateFallsBackToCleanedPlainText()
    {
        var parsed = GeneratedCodeCandidatePolicy.ParseCodeCandidate("LANGUAGE=sh\necho ok", "bash");

        Assert.Equal("bash", parsed.Language);
        Assert.Equal("sh\necho ok", parsed.Code);
    }
}
