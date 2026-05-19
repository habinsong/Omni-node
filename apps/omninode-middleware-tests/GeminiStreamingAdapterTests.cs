using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GeminiStreamingAdapterTests
{
    [Fact]
    public async Task StreamAsyncPostsGeminiBodyAndReadsSsePayloads()
    {
        string? capturedApiKey = null;
        string? capturedBody = null;
        var payloads = new List<string>();
        var adapter = new GeminiStreamingAdapter(new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"안녕\"}]}}]}\n\n"
                    + "data: {\"usageMetadata\":{\"totalTokenCount\":7}}\n\n"
                    + "data: [DONE]\n\n"
                )
            };
        })));

        var result = await adapter.StreamAsync(
            new GeminiStreamingAdapterRequest(
                "https://gemini.example.com/streamGenerateContent?alt=sse",
                "gemini-key",
                """{"contents":[]}""",
                CancellationToken.None,
                CancellationToken.None,
                () => CancellationToken.None,
                payloads.Add
            ),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("gemini-key", capturedApiKey);
        Assert.Equal("""{"contents":[]}""", capturedBody);
        Assert.Equal(2, payloads.Count);
        Assert.Contains("안녕", payloads[0]);
        Assert.Contains("usageMetadata", payloads[1]);
    }

    [Fact]
    public async Task StreamAsyncMapsHttpFailure()
    {
        var adapter = new GeminiStreamingAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"error":"denied"}""")
            })));

        var result = await adapter.StreamAsync(
            new GeminiStreamingAdapterRequest(
                "https://gemini.example.com/streamGenerateContent?alt=sse",
                "gemini-key",
                """{"contents":[]}""",
                CancellationToken.None,
                CancellationToken.None,
                () => CancellationToken.None,
                _ => { }
            ),
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Contains("denied", result.FailureBody);
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
