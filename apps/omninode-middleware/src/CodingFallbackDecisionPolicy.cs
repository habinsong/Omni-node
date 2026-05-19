using System.Net;

namespace OmniNode.Middleware;

internal static class CodingFallbackDecisionPolicy
{
    public static bool ShouldPreferFileBundleFallback(
        string objective,
        IReadOnlyList<string>? requestedPaths,
        bool isExplicitSingleFile,
        bool projectPrefersMultiFile,
        bool profilePrefersBundle = false,
        bool frontendLike = false,
        bool gameLike = false,
        bool includeExplicitMultiFileTextSignals = false
    )
    {
        if (requestedPaths != null && requestedPaths.Count > 1)
        {
            return true;
        }

        if (isExplicitSingleFile)
        {
            return false;
        }

        return projectPrefersMultiFile
            || profilePrefersBundle
            || frontendLike
            || gameLike
            || (includeExplicitMultiFileTextSignals && HasExplicitMultiFileTextSignal(objective));
    }

    private static bool HasExplicitMultiFileTextSignal(string objective)
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        return ContainsAny(
            text,
            "두 파일",
            "2개 파일",
            "여러 파일",
            "multi-file",
            "multiple files"
        );
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
