using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GeminiGenerateContentAdapterTests
{
    [Fact]
    public async Task SendAsyncPostsBodyAndApiKeyHeader()
    {
        string? capturedApiKey = null;
        string? capturedBody = null;
        var adapter = new GeminiGenerateContentAdapter(new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"응답"}]}}]}""")
            };
        })));

        var result = await adapter.SendAsync(
            new GeminiGenerateContentRequest(
                "https://gemini.example.com/generateContent",
                "gemini-key",
                """{"contents":[]}"""
            ),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("gemini-key", capturedApiKey);
        Assert.Equal("""{"contents":[]}""", capturedBody);
        Assert.Contains("응답", result.ResponseBody);
    }

    [Fact]
    public async Task SendAsyncReturnsFailureBody()
    {
        var adapter = new GeminiGenerateContentAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"rate_limited"}""")
            })));

        var result = await adapter.SendAsync(
            new GeminiGenerateContentRequest(
                "https://gemini.example.com/generateContent",
                "gemini-key",
                """{"contents":[]}"""
            ),
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Contains("rate_limited", result.ResponseBody);
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
