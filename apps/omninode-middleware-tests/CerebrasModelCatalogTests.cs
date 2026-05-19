using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CerebrasModelCatalogTests
{
    [Fact]
    public void ExtractFirstModelIdReturnsFirstNonEmptyId()
    {
        var modelId = CerebrasModelCatalog.ExtractFirstModelId(
            """
            {
              "data": [
                { "id": "" },
                { "id": "qwen-3-preview" },
                { "id": "llama-4" }
              ]
            }
            """
        );

        Assert.Equal("qwen-3-preview", modelId);
    }

    [Fact]
    public void ExtractFirstModelIdReturnsNullForMissingData()
    {
        Assert.Null(CerebrasModelCatalog.ExtractFirstModelId("""{"object":"list"}"""));
    }

    [Fact]
    public void ExtractFirstModelIdReturnsNullForMalformedJson()
    {
        Assert.Null(CerebrasModelCatalog.ExtractFirstModelId("{"));
    }

    [Fact]
    public async Task ResolveFirstAvailableModelAsyncFetchesAndCachesFirstModel()
    {
        var calls = 0;
        string? capturedAuthorization = null;
        var catalog = new CerebrasModelCatalog(
            CreateProviders(),
            CreateRuntimeSettings(),
            new HttpClient(new StubHttpMessageHandler(request =>
            {
                calls += 1;
                capturedAuthorization = request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"data":[{"id":"qwen-3-preview"},{"id":"llama-4"}]}
                        """
                    )
                };
            }))
        );

        var first = await catalog.ResolveFirstAvailableModelAsync("secret-key", CancellationToken.None);
        var second = await catalog.ResolveFirstAvailableModelAsync("secret-key", CancellationToken.None);

        Assert.Equal("qwen-3-preview", first);
        Assert.Equal("qwen-3-preview", second);
        Assert.Equal("qwen-3-preview", catalog.GetCachedResolvedModel());
        Assert.Equal("Bearer secret-key", capturedAuthorization);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ResolveFirstAvailableModelAsyncReturnsNullForHttpFailure()
    {
        var catalog = new CerebrasModelCatalog(
            CreateProviders(),
            CreateRuntimeSettings(),
            new HttpClient(new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("""{"error":"denied"}""")
                }))
        );

        var result = await catalog.ResolveFirstAvailableModelAsync("secret-key", CancellationToken.None);

        Assert.Null(result);
        Assert.Null(catalog.GetCachedResolvedModel());
    }

    [Fact]
    public async Task ResolveFirstAvailableModelAsyncSkipsHttpWhenApiKeyIsBlank()
    {
        var calls = 0;
        var catalog = new CerebrasModelCatalog(
            CreateProviders(),
            CreateRuntimeSettings(),
            new HttpClient(new StubHttpMessageHandler(_ =>
            {
                calls += 1;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }))
        );

        var result = await catalog.ResolveFirstAvailableModelAsync(" ", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    private static ProviderOptions CreateProviders()
    {
        return new AppConfig
        {
            CerebrasBaseUrl = "https://cerebras.example.com/v1",
            CerebrasTimeoutSec = 5
        }.Providers;
    }

    private static RuntimeSettings CreateRuntimeSettings()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"omninode-cerebras-catalog-{Guid.NewGuid():N}.json");
        return new RuntimeSettings(new AppConfig { DashboardAccessStatePath = statePath });
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
