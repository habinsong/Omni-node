using System.Net.Http.Headers;

namespace OmniNode.Middleware;

internal static class GroqRateLimitHeaderParser
{
    public static GroqRateLimit Parse(HttpResponseHeaders headers, DateTimeOffset capturedAtUtc)
    {
        return new GroqRateLimit
        {
            LimitRequests = ReadHeaderLong(headers, "x-ratelimit-limit-requests"),
            RemainingRequests = ReadHeaderLong(headers, "x-ratelimit-remaining-requests"),
            LimitTokens = ReadHeaderLong(headers, "x-ratelimit-limit-tokens"),
            RemainingTokens = ReadHeaderLong(headers, "x-ratelimit-remaining-tokens"),
            ResetRequests = ReadHeaderString(headers, "x-ratelimit-reset-requests"),
            ResetTokens = ReadHeaderString(headers, "x-ratelimit-reset-tokens"),
            LastUpdatedUtc = capturedAtUtc
        };
    }

    private static long? ReadHeaderLong(HttpResponseHeaders headers, string key)
    {
        if (!headers.TryGetValues(key, out var values))
        {
            return null;
        }

        var first = values.FirstOrDefault();
        if (long.TryParse(first, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ReadHeaderString(HttpResponseHeaders headers, string key)
    {
        if (!headers.TryGetValues(key, out var values))
        {
            return null;
        }

        return values.FirstOrDefault();
    }
}
