using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GitHubRepositoryContextLoaderTests
{
    [Fact]
    public async Task TryLoadAsyncFetchesRepositoryDescriptionAndRawReadme()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildGitHubHtml(
                        "OpenAI .NET SDK",
                        "main",
                        "README.md",
                        "<p>Fallback README</p>"
                    ))
                };
            }

            if (request.RequestUri?.Host == "raw.githubusercontent.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        Install package.
                        Supports streaming chat responses.
                        Configure retries.
                        """
                    )
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var loader = new GitHubRepositoryContextLoader(new HttpClient(handler));

        var snapshot = await loader.TryLoadAsync(
            "streaming chat",
            new[] { "https://github.com/openai/openai-dotnet" },
            CancellationToken.None
        );

        Assert.True(snapshot.HasValue);
        Assert.Equal("openai/openai-dotnet", snapshot.Value.RepositorySlug);
        Assert.Equal("OpenAI .NET SDK", snapshot.Value.Description);
        Assert.Contains("Supports streaming chat responses.", snapshot.Value.ReadmeText);
        Assert.DoesNotContain("Fallback README", snapshot.Value.ReadmeText);
    }

    [Fact]
    public async Task TryLoadAsyncFallsBackToEmbeddedReadmeWhenRawReadmeFails()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildGitHubHtml(
                        "",
                        "main",
                        "README.md",
                        "<p>Fallback overview</p><ul><li>Supports tools</li></ul>"
                    ))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var loader = new GitHubRepositoryContextLoader(new HttpClient(handler));

        var snapshot = await loader.TryLoadAsync(
            "tools",
            new[] { "https://github.com/openai/openai-dotnet" },
            CancellationToken.None
        );

        Assert.True(snapshot.HasValue);
        Assert.Contains("Fallback overview", snapshot.Value.ReadmeText);
        Assert.Contains("- Supports tools", snapshot.Value.ReadmeText);
    }

    [Theory]
    [InlineData("https://example.com/openai/openai-dotnet")]
    [InlineData("https://github.com/openai/openai-dotnet/issues")]
    public async Task TryLoadAsyncReturnsNullForUnsupportedRepositoryUrls(string url)
    {
        var loader = new GitHubRepositoryContextLoader(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
        )));

        var snapshot = await loader.TryLoadAsync("요약", new[] { url }, CancellationToken.None);

        Assert.False(snapshot.HasValue);
    }

    private static string BuildGitHubHtml(string description, string refName, string readmePath, string richText)
    {
        var escapedDescription = JsonEscape(description);
        var escapedRefName = JsonEscape(refName);
        var escapedReadmePath = JsonEscape(readmePath);
        var escapedRichText = JsonEscape(richText);
        var embeddedJson = "{\"payload\":{\"overview\":{\"overviewFiles\":[{\"preferredFileType\":\"readme\",\"refName\":\""
            + escapedRefName
            + "\",\"path\":\""
            + escapedReadmePath
            + "\",\"richText\":\""
            + escapedRichText
            + "\"}]}}}";
        return """
               <html>
               <head><meta name="description" content="
               """
            + escapedDescription
            + """
               "></head>
               <body>
               <script type="application/json" data-target="react-app.embeddedData">
               """
            + embeddedJson
            + """
               </script>
               </body>
               </html>
               """;
    }

    private static string JsonEscape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(_handler(request));
        }
    }
}
