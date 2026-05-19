using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class NvidiaStatusPollingAdapterTests
{
    [Fact]
    public async Task PollAsyncReturnsSuccessfulStatusBody()
    {
        var requestedUris = new List<string>();
        var adapter = new NvidiaStatusPollingAdapter(new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"done"}}]}""")
            };
        })));

        var result = await adapter.PollAsync(
            "secret",
            """{"requestId":"req 1"}""",
            "https://integrate.api.nvidia.com/v1",
            timeoutSec: 3,
            CancellationToken.None
        );

        Assert.Contains("done", result);
        Assert.Single(requestedUris);
        Assert.Equal("https://integrate.api.nvidia.com/v1/status/req 1", requestedUris[0]);
    }

    [Fact]
    public async Task PollAsyncContinuesOnAccepted()
    {
        var calls = 0;
        var adapter = new NvidiaStatusPollingAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
        {
            calls += 1;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""") };
        })));

        var result = await adapter.PollAsync(
            "secret",
            """{"id":"abc"}""",
            "https://integrate.api.nvidia.com/v1/",
            timeoutSec: 3,
            CancellationToken.None
        );

        Assert.Contains("true", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PollAsyncRejectsMissingRequestId()
    {
        var adapter = new NvidiaStatusPollingAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.PollAsync("secret", "{}", "https://integrate.api.nvidia.com/v1", 3, CancellationToken.None));
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
