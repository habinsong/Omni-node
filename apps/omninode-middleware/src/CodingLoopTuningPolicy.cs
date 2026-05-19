namespace OmniNode.Middleware;

internal static class CodingLoopTuningPolicy
{
    public static bool ShouldUseOneShotMode(
        bool enableOneShotUiClone,
        bool allowUiOneShot,
        string objective,
        string languageHint,
        Func<string, string, bool> isFrontendLikeCodingTask,
        Func<string, string, bool> isGameLikeCodingTask
    )
    {
        if (!enableOneShotUiClone || !allowUiOneShot)
        {
            return false;
        }

        var lang = CodingLanguagePolicy.NormalizeCodingLanguageHintPreservingAuto(languageHint);
        if (lang != "auto" && lang != "html" && lang != "javascript" && lang != "css" && lang != "python")
        {
            return false;
        }

        return isFrontendLikeCodingTask(objective, languageHint) || isGameLikeCodingTask(objective, languageHint);
    }

    public static int ResolveMaxIterations(int profileMaxIterations, bool oneShotMode)
    {
        return oneShotMode ? 1 : Math.Max(1, profileMaxIterations);
    }

    public static int ResolveMaxActions(int loopMaxActions, int oneShotMaxActions, bool oneShotMode)
    {
        return oneShotMode
            ? Math.Max(1, oneShotMaxActions)
            : Math.Max(1, loopMaxActions);
    }

    public static int ResolvePlanMaxOutputTokens(int planMaxOutputTokens)
    {
        return Math.Max(1200, planMaxOutputTokens);
    }

    public static int ResolveMaxRepairPasses(
        string objective,
        string languageHint,
        Func<string, string, bool> isGameLikeCodingTask,
        Func<string, string, bool> isFrontendLikeCodingTask,
        Func<string, bool> looksLikeBrowserAppObjective,
        int defaultMaxRepairPasses = 1
    )
    {
        var language = CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, objective);
        if (isGameLikeCodingTask(objective, language) || isFrontendLikeCodingTask(objective, language))
        {
            return 3;
        }

        var requestedPaths = CodingFallbackPolicy.ExtractRequestedCodingPaths(objective ?? string.Empty, language);
        if (requestedPaths.Count > 1 || looksLikeBrowserAppObjective(objective ?? string.Empty))
        {
            return 2;
        }

        return Math.Max(1, defaultMaxRepairPasses);
    }

    public static string BuildRecentLoopLogs(IReadOnlyList<string> iterations, int recentLoopHistory, int maxCharsPerEntry = 500)
    {
        if (iterations.Count == 0)
        {
            return string.Empty;
        }

        var selected = iterations.TakeLast(Math.Max(1, recentLoopHistory)).ToArray();
        for (var i = 0; i < selected.Length; i++)
        {
            selected[i] = TrimForOutput(selected[i], maxCharsPerEntry);
        }

        return string.Join("\n", selected);
    }

    private static string TrimForOutput(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0 || text.Length <= maxChars)
        {
            return text ?? string.Empty;
        }

        return text[..maxChars] + "...(truncated)";
    }
}
