using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class SearchUrlContextPolicyTests
{
    [Fact]
    public void TryParseGitHubRepositoryRootExtractsOwnerAndRepo()
    {
        var parsed = SearchUrlContextPolicy.TryParseGitHubRepositoryRoot(
            "https://github.com/openai/openai-dotnet",
            out var owner,
            out var repo
        );

        Assert.True(parsed);
        Assert.Equal("openai", owner);
        Assert.Equal("openai-dotnet", repo);
    }

    [Theory]
    [InlineData("https://github.com/openai/openai-dotnet", true)]
    [InlineData("https://github.com/openai/openai-dotnet/issues", false)]
    [InlineData("https://gitlab.com/group/project", true)]
    [InlineData("https://example.com/group/project", false)]
    public void LooksLikeRepositoryUrlRejectsNonRepositoryActions(string url, bool expected)
    {
        Assert.Equal(expected, SearchUrlContextPolicy.LooksLikeRepositoryUrl(url));
    }

    [Theory]
    [InlineData("https://example.com/docs/api", true)]
    [InlineData("https://developers.example.com/reference", true)]
    [InlineData("https://example.com/products", false)]
    public void LooksLikeDocumentationUrlUsesHostAndPathSignals(string url, bool expected)
    {
        Assert.Equal(expected, SearchUrlContextPolicy.LooksLikeDocumentationUrl(url));
    }

    [Fact]
    public void ResolveImplicitUrlRequestBuildsRepositoryPrompt()
    {
        var prompt = SearchUrlContextPolicy.ResolveImplicitUrlRequest(
            "https://github.com/openai/openai-dotnet",
            new[] { "https://github.com/openai/openai-dotnet" }
        );

        Assert.Equal("이 코드 저장소/프로젝트가 무엇인지 설명해줘.", prompt);
    }

    [Fact]
    public void ConvertGitHubRichTextToPlainTextRemovesHtmlTags()
    {
        var text = SearchUrlContextPolicy.ConvertGitHubRichTextToPlainText(
            "<p>Hello <strong>SDK</strong></p><ul><li>Chat API</li></ul>"
        );

        Assert.Contains("Hello SDK", text);
        Assert.Contains("- Chat API", text);
        Assert.DoesNotContain("<strong>", text);
    }

    [Fact]
    public void BuildRelevantRepositoryExcerptKeepsNearbyMatchingLines()
    {
        var excerpt = SearchUrlContextPolicy.BuildRelevantRepositoryExcerpt(
            "streaming chat",
            """
            Install the package.
            The SDK supports streaming chat responses.
            Configure retries separately.
            Unrelated deployment notes.
            """
        );

        Assert.Contains("Install the package.", excerpt);
        Assert.Contains("The SDK supports streaming chat responses.", excerpt);
        Assert.Contains("Configure retries separately.", excerpt);
    }

    [Fact]
    public void TryBuildRepositoryExtractiveAnswerUsesOnlyMatchingReadmeLines()
    {
        var answer = SearchUrlContextPolicy.TryBuildRepositoryExtractiveAnswer(
            "streaming",
            "OpenAI SDK",
            """
            Install the package.
            Supports streaming responses.
            Screenshots:
            ![image](demo.png)
            """
        );

        Assert.Contains("확인된 내용:", answer);
        Assert.Contains("- Supports streaming responses.", answer);
        Assert.DoesNotContain("![image]", answer);
        Assert.Contains("출처: github.com", answer);
    }
}
