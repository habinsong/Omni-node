using System.Net.Http;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GroqRateLimitHeaderParserTests
{
    [Fact]
    public void ParseExtractsAllRateLimitHeadersAndStampsCapturedAt()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("x-ratelimit-limit-requests", "1000");
        response.Headers.Add("x-ratelimit-remaining-requests", "997");
        response.Headers.Add("x-ratelimit-limit-tokens", "100000");
        response.Headers.Add("x-ratelimit-remaining-tokens", "99500");
        response.Headers.Add("x-ratelimit-reset-requests", "1m20s");
        response.Headers.Add("x-ratelimit-reset-tokens", "45s");

        var capturedAt = new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);
        var snapshot = GroqRateLimitHeaderParser.Parse(response.Headers, capturedAt);

        Assert.Equal(1000, snapshot.LimitRequests);
        Assert.Equal(997, snapshot.RemainingRequests);
        Assert.Equal(100000, snapshot.LimitTokens);
        Assert.Equal(99500, snapshot.RemainingTokens);
        Assert.Equal("1m20s", snapshot.ResetRequests);
        Assert.Equal("45s", snapshot.ResetTokens);
        Assert.Equal(capturedAt, snapshot.LastUpdatedUtc);
    }

    [Fact]
    public void ParseReturnsNullableFieldsWhenHeadersAreAbsent()
    {
        using var response = new HttpResponseMessage();

        var snapshot = GroqRateLimitHeaderParser.Parse(response.Headers, DateTimeOffset.UtcNow);

        Assert.Null(snapshot.LimitRequests);
        Assert.Null(snapshot.RemainingRequests);
        Assert.Null(snapshot.LimitTokens);
        Assert.Null(snapshot.RemainingTokens);
        Assert.Null(snapshot.ResetRequests);
        Assert.Null(snapshot.ResetTokens);
    }

    [Fact]
    public void ParseIgnoresNonNumericLongHeaders()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("x-ratelimit-limit-requests", "not-a-number");
        response.Headers.Add("x-ratelimit-reset-requests", "12s");

        var snapshot = GroqRateLimitHeaderParser.Parse(response.Headers, DateTimeOffset.UtcNow);

        Assert.Null(snapshot.LimitRequests);
        Assert.Equal("12s", snapshot.ResetRequests);
    }
}
