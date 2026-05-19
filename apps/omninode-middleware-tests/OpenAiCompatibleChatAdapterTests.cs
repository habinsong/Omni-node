using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class OpenAiCompatibleChatAdapterTests
{
    [Fact]
    public async Task SendAsyncPostsOpenAiCompatibleBody()
    {
        string? capturedBody = null;
        string? capturedAuthorization = null;
        var adapter = new OpenAiCompatibleChatAdapter(new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedAuthorization = request.Headers.Authorization?.ToString();
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"choices":[{"message":{"content":"응답"},"finish_reason":"stop"}]}
                    """
                )
            };
        })));

        var result = await adapter.SendAsync(
            new ProviderChatAdapterRequest(
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

        Assert.True(result.IsSuccess);
        Assert.Equal("Bearer secret", capturedAuthorization);
        Assert.Contains("\"model\":\"model-a\"", capturedBody);
        Assert.Contains("\"max_tokens\":512", capturedBody);
        Assert.Contains("\"stream\":false", capturedBody);
    }

    [Fact]
    public async Task SendAsyncMapsHttpFailure()
    {
        var adapter = new OpenAiCompatibleChatAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"quota"}""")
            })));

        var result = await adapter.SendAsync(
            new ProviderChatAdapterRequest(
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
    public async Task SendAsyncDelegatesAcceptedResponseResolver()
    {
        var adapter = new OpenAiCompatibleChatAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("""{"requestId":"abc"}""")
            })));

        var result = await adapter.SendAsync(
            new ProviderChatAdapterRequest(
                "nvidia",
                "https://api.example.com/chat/completions",
                "secret",
                "model-a",
                "system",
                "hello",
                null,
                "max_tokens",
                512,
                AcceptedResponseResolver: (body, _) => Task.FromResult("""{"choices":[{"message":{"content":"완료"}}]}""")
            ),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("완료", result.ResponseBody);
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
