using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class CodingProgressPolicy
{
    public static CodingProgressUpdate BuildUpdate(
        string progressMode,
        string provider,
        string model,
        string phase,
        string message,
        int iteration,
        int maxIterations,
        int percent,
        bool done,
        string stageKey = "",
        string stageTitle = "",
        string stageDetail = "",
        int stageIndex = 0,
        int stageTotal = 0
    )
    {
        return new CodingProgressUpdate(
            progressMode,
            provider,
            model,
            phase,
            message,
            iteration,
            maxIterations,
            percent,
            done,
            stageKey,
            stageTitle,
            stageDetail,
            stageIndex,
            stageTotal
        );
    }

    public static string BuildObjectiveDetail(string objective, string languageHint)
    {
        var compact = Regex.Replace((objective ?? string.Empty).Trim(), @"\s+", " ");
        if (compact.Length > 180)
        {
            compact = compact[..180] + "...";
        }

        var language = string.IsNullOrWhiteSpace(languageHint) ? "auto" : languageHint.Trim();
        return string.IsNullOrWhiteSpace(compact)
            ? $"언어 힌트: {language}"
            : $"{compact} · 언어 힌트: {language}";
    }

    public static string BuildWorkspaceDetail(string workspaceSnapshot)
    {
        var lines = (workspaceSnapshot ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return "워크스페이스 스냅샷 없음";
        }

        var headline = lines[0];
        if (headline.StartsWith("total_files=", StringComparison.OrdinalIgnoreCase))
        {
            return $"작업공간 점검 완료 · {headline.Replace("total_files=", "파일 수 ", StringComparison.OrdinalIgnoreCase)}";
        }

        return TrimForOutput(headline, 180);
    }

    public static string BuildPlanDetail(
        CodingLoopPlan plan,
        IReadOnlyList<CodingLoopAction> actions,
        bool hasDeferredRunAction
    )
    {
        var parts = new List<string>();
        var analysis = Regex.Replace((plan.Analysis ?? string.Empty).Trim(), @"\s+", " ");
        if (!string.IsNullOrWhiteSpace(analysis))
        {
            parts.Add(TrimForOutput(analysis, 180));
        }

        var targetPaths = actions
            .Where(action => !string.Equals(action.Type, "run", StringComparison.OrdinalIgnoreCase))
            .Select(action => (action.Path ?? string.Empty).Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        if (targetPaths.Length > 0)
        {
            parts.Add("대상 파일: " + string.Join(", ", targetPaths));
        }

        if (hasDeferredRunAction)
        {
            parts.Add("실행은 마지막 단계에서 1회만 수행");
        }

        return parts.Count == 0 ? "이번 반복 구현 계획을 정리했습니다." : string.Join(" · ", parts);
    }

    public static string BuildWriteDetail(IReadOnlyCollection<string> changedPaths, bool hasDeferredRunAction)
    {
        var parts = new List<string>();
        if (changedPaths.Count > 0)
        {
            parts.Add($"변경 파일 {changedPaths.Count}개");
            parts.Add(string.Join(", ", changedPaths.Take(4)));
        }
        else
        {
            parts.Add("실제 파일 변경은 아직 없음");
        }

        if (hasDeferredRunAction)
        {
            parts.Add("중간 실행 없이 최종 검증만 예약");
        }

        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string TrimForOutput(string text, int limit)
    {
        var normalized = text ?? string.Empty;
        var safeLimit = Math.Max(200, limit);
        if (normalized.Length <= safeLimit)
        {
            return normalized;
        }

        return normalized[..safeLimit] + "...(truncated)";
    }
}
