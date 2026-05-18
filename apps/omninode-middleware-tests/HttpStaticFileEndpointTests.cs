using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class HttpStaticFileEndpointTests
{
    [Fact]
    public void IsClientCacheFreshAcceptsMatchingETag()
    {
        var lastModifiedUtc = new DateTimeOffset(2026, 5, 18, 1, 2, 3, TimeSpan.Zero);

        var fresh = HttpStaticFileEndpoint.IsClientCacheFresh(
            "\"abc\", \"def\"",
            null,
            "\"def\"",
            lastModifiedUtc
        );

        Assert.True(fresh);
    }

    [Fact]
    public void IsClientCacheFreshPrefersETagOverIfModifiedSince()
    {
        var lastModifiedUtc = new DateTimeOffset(2026, 5, 18, 1, 2, 3, TimeSpan.Zero);

        var fresh = HttpStaticFileEndpoint.IsClientCacheFresh(
            "\"other\"",
            "Mon, 18 May 2026 01:02:03 GMT",
            "\"def\"",
            lastModifiedUtc
        );

        Assert.False(fresh);
    }

    [Fact]
    public void IsClientCacheFreshAcceptsRecentIfModifiedSince()
    {
        var lastModifiedUtc = new DateTimeOffset(2026, 5, 18, 1, 2, 3, TimeSpan.Zero);

        var fresh = HttpStaticFileEndpoint.IsClientCacheFresh(
            null,
            "Mon, 18 May 2026 01:02:04 GMT",
            "\"def\"",
            lastModifiedUtc
        );

        Assert.True(fresh);
    }
}
