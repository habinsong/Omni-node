namespace OmniNode.Middleware;

internal static class RouterIntentClassifier
{
    /// <summary>
    /// LLM 분류 결과 텍스트(예: "OS_CONTROL", "DYNAMIC_CODE")에서 카테고리 키워드를 골라 RouterIntent로 매핑한다.
    /// </summary>
    public static RouterIntent MapFromLlmContent(string content)
    {
        var normalized = (content ?? string.Empty).Trim().ToUpperInvariant();

        if (normalized.Contains("OS_CONTROL", StringComparison.Ordinal))
        {
            return RouterIntent.OsControl;
        }

        if (normalized.Contains("QUERY_SYSTEM", StringComparison.Ordinal))
        {
            return RouterIntent.QuerySystem;
        }

        if (normalized.Contains("DYNAMIC_CODE", StringComparison.Ordinal))
        {
            return RouterIntent.DynamicCode;
        }

        return RouterIntent.Unknown;
    }

    /// <summary>
    /// LLM 호출 실패 시 사용자 입력 자체에서 카테고리를 추정한다.
    /// </summary>
    public static RouterIntent ClassifyHeuristic(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.StartsWith("/kill ", StringComparison.Ordinal) || normalized.Contains("terminate", StringComparison.Ordinal))
        {
            return RouterIntent.OsControl;
        }

        if (normalized.StartsWith("/metrics", StringComparison.Ordinal)
            || normalized.Contains("status", StringComparison.Ordinal)
            || normalized.Contains("로그", StringComparison.Ordinal))
        {
            return RouterIntent.QuerySystem;
        }

        if (normalized.StartsWith("/code ", StringComparison.Ordinal)
            || normalized.Contains("script", StringComparison.Ordinal)
            || normalized.Contains("파이썬", StringComparison.Ordinal))
        {
            return RouterIntent.DynamicCode;
        }

        return RouterIntent.Unknown;
    }
}
