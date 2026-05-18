namespace OmniNode.Middleware;

internal static class GeminiRequestPolicy
{
    public static string BuildGroundedBody(string prompt, int maxOutputTokens)
    {
        return "{"
            + "\"contents\":[{"
            + "\"role\":\"user\","
            + "\"parts\":["
            + $"{{\"text\":\"{EscapeJson(prompt)}\"}}"
            + "]"
            + "}],"
            + "\"tools\":[{\"google_search\":{}}],"
            + "\"generationConfig\":{"
            + "\"temperature\":0.1,"
            + $"\"maxOutputTokens\":{maxOutputTokens}"
            + "}"
            + "}";
    }

    public static string BuildUrlContextBody(string prompt, int maxOutputTokens, bool includeGoogleSearch)
    {
        var tools = includeGoogleSearch
            ? "[{\"url_context\":{}},{\"google_search\":{}}]"
            : "[{\"url_context\":{}}]";
        return "{"
            + "\"contents\":[{"
            + "\"role\":\"user\","
            + "\"parts\":["
            + $"{{\"text\":\"{EscapeJson(prompt)}\"}}"
            + "]"
            + "}],"
            + $"\"tools\":{tools},"
            + "\"generationConfig\":{"
            + "\"temperature\":0.1,"
            + $"\"maxOutputTokens\":{maxOutputTokens}"
            + "}"
            + "}";
    }

    public static string NormalizeStreamDelta(string chunkText, string currentText)
    {
        var nextChunk = chunkText ?? string.Empty;
        if (nextChunk.Length == 0)
        {
            return string.Empty;
        }

        var merged = currentText ?? string.Empty;
        if (merged.Length == 0)
        {
            return nextChunk;
        }

        if (nextChunk.StartsWith(merged, StringComparison.Ordinal))
        {
            return nextChunk[merged.Length..];
        }

        if (merged.EndsWith(nextChunk, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return nextChunk;
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\b", "\\b", StringComparison.Ordinal)
            .Replace("\f", "\\f", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
