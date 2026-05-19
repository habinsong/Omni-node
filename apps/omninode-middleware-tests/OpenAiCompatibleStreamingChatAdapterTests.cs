using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class OpenAiCompatibleStreamingChatAdapterTests
{
    [Fact]
    public async Task SendTurnAsyncStreamsPayloadsAndDeltas()
    {
        string? capturedBody = null;
        var payloads = new List<string>();
        var deltas = new List<string>();
        var adapter = new OpenAiCompatibleStreamingChatAdapter(new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"안녕\"},\"finish_reason\":null}]}\n\n"
                    + "data: {\"choices\":[{\"delta\":{\"content\":\"하세요\"},\"finish_reason\":\"stop\"}]}\n\n"
                    + "data: [DONE]\n\n"
                )
            };
        })));

        var result = await adapter.SendTurnAsync(
            new ProviderStreamingAdapterRequest(
                "groq",
                "https://api.example.com/chat/completions",
                "secret",
                "model-a",
                "system",
                "hello",
                null,
                "max_tokens",
                512,
                OnRawPayload: payloads.Add,
                OnDelta: deltas.Add
            ),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("stop", result.FinishReason);
        Assert.Contains("\"stream\":true", capturedBody);
        Assert.Equal(2, payloads.Count);
        Assert.Equal(new[] { "안녕", "하세요" }, deltas);
    }

    [Fact]
    public async Task SendTurnAsyncMapsHttpFailure()
    {
        var adapter = new OpenAiCompatibleStreamingChatAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"quota"}""")
            })));

        var result = await adapter.SendTurnAsync(
            new ProviderStreamingAdapterRequest(
                "nvidia",
                "https://api.example.com/chat/completions",
                "secret",
                "model-a",
                "system",
                "hello",
                null,
                "max_tokens",
                512
            ),
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Contains("무료 할당량", result.FailureMessage);
    }

    [Fact]
    public async Task SendTurnAsyncDelegatesAcceptedResponseResolver()
    {
        var adapter = new OpenAiCompatibleStreamingChatAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("""{"requestId":"abc"}""")
            })));

        var result = await adapter.SendTurnAsync(
            new ProviderStreamingAdapterRequest(
                "nvidia",
                "https://api.example.com/chat/completions",
                "secret",
                "model-a",
                "system",
                "hello",
                null,
                "max_tokens",
                512,
                AcceptedResponseResolver: (body, _) => Task.FromResult(body.Replace("abc", "resolved", StringComparison.Ordinal))
            ),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"requestId":"resolved"}""", result.AcceptedResponseBody);
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
