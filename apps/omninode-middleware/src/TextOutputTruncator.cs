namespace OmniNode.Middleware;

internal static class TextOutputTruncator
{
    public const string DefaultTruncationMarker = "...(truncated)";

    public static string TruncateWithMin200(string text, int limit)
    {
        var normalized = text ?? string.Empty;
        var safeLimit = Math.Max(200, limit);
        if (normalized.Length <= safeLimit)
        {
            return normalized;
        }

        return normalized[..safeLimit] + DefaultTruncationMarker;
    }

    public static string TruncateRaw(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0 || text.Length <= maxChars)
        {
            return text ?? string.Empty;
        }

        return text[..maxChars] + DefaultTruncationMarker;
    }

    public static string TruncateWithEllipsis(string text, int limit)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= limit)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, limit - 3)] + "...";
    }
}
