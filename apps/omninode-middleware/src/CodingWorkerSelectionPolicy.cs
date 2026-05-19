using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class CodingWorkerSelectionPolicy
{
    public static bool HasQualityFailure(CodingWorkerResult worker)
    {
        var status = (worker.Execution.Status ?? string.Empty).Trim();
        return status.Equals("quality_failed", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.StdErr ?? string.Empty).Contains("[quality-gate]", StringComparison.OrdinalIgnoreCase)
            || (worker.Summary ?? string.Empty).Contains("[quality-gate]", StringComparison.OrdinalIgnoreCase);
    }

    public static int ScoreWorker(CodingWorkerResult worker)
    {
        var score = 0;
        var status = (worker.Execution.Status ?? string.Empty).Trim();
        if (status.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (status.Equals("mixed", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        else if (status.Equals("quality_failed", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }
        else if (status.Equals("error", StringComparison.OrdinalIgnoreCase) || status.Equals("timeout", StringComparison.OrdinalIgnoreCase))
        {
            score -= 60;
        }

        score += Math.Min(40, (worker.ChangedFiles?.Count ?? 0) * 8);
        if (!string.IsNullOrWhiteSpace(worker.Execution.Command)
            && worker.Execution.Command != "(none)"
            && worker.Execution.Command != "-")
        {
            score += 15;
        }

        if ((worker.Execution.StdOut ?? string.Empty).Contains("[quality-gate] ok", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (HasQualityFailure(worker))
        {
            score -= 80;
        }

        return score;
    }

    public static CodingWorkerResult SelectBest(IReadOnlyList<CodingWorkerResult> workers, CodingWorkerResult fallback)
    {
        return (workers ?? Array.Empty<CodingWorkerResult>())
            .Where(worker => worker != null)
            .OrderByDescending(ScoreWorker)
            .ThenByDescending(worker => string.Equals(worker.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(worker => worker.ChangedFiles?.Count ?? 0)
            .FirstOrDefault() ?? fallback;
    }

    public static IReadOnlyList<string> MergeChangedFiles(IReadOnlyList<CodingWorkerResult> workers)
    {
        return workers
            .SelectMany(worker => worker.ChangedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> MergeChangedFilesForBest(
        CodingWorkerResult bestWorker,
        IReadOnlyList<CodingWorkerResult> workers
    )
    {
        var bestFiles = (bestWorker.ChangedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return bestFiles.Length > 0 ? bestFiles : MergeChangedFiles(workers);
    }

    public static CodingWorkerResult BuildSkippedWorker(
        string provider,
        string model,
        string language,
        string role,
        string runDirectory,
        string summary
    )
    {
        return new CodingWorkerResult(
            provider,
            model,
            language,
            string.Empty,
            summary,
            new CodeExecutionResult(
                language,
                runDirectory,
                "-",
                "(skipped)",
                0,
                string.Empty,
                string.Empty,
                "skipped"
            ),
            Array.Empty<string>(),
            role,
            summary
        );
    }

    public static string BuildWorkerWorkspaceRoot(string runRoot, string provider, string model)
    {
        var providerSlug = Regex.Replace((provider ?? "worker").Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-");
        var modelSlug = Regex.Replace((model ?? "model").Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-");
        providerSlug = string.IsNullOrWhiteSpace(providerSlug) ? "worker" : providerSlug;
        modelSlug = string.IsNullOrWhiteSpace(modelSlug) ? "model" : modelSlug;
        return Path.Combine(runRoot, $"{providerSlug}-{modelSlug}");
    }

    public static CodeExecutionResult BuildMultiResultExecution(
        string runRoot,
        string language,
        IReadOnlyList<CodingWorkerResult> workers,
        CodingWorkerResult? bestWorker = null
    )
    {
        var hasSuccess = workers.Any(worker => (worker.Execution.Status ?? string.Empty).Equals("ok", StringComparison.OrdinalIgnoreCase));
        var hasFailure = workers.Any(worker =>
            (worker.Execution.Status ?? string.Empty).Equals("error", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.Status ?? string.Empty).Equals("quality_failed", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.Status ?? string.Empty).Equals("timeout", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.Status ?? string.Empty).Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.Status ?? string.Empty).Equals("killed", StringComparison.OrdinalIgnoreCase));
        var status = hasSuccess && hasFailure
            ? "mixed"
            : hasFailure
                ? "error"
                : hasSuccess
                    ? "ok"
                    : "skipped";

        var selected = bestWorker ?? workers
            .OrderByDescending(ScoreWorker)
            .FirstOrDefault();
        var selectedExecution = selected?.Execution;
        return new CodeExecutionResult(
            selected?.Language ?? language,
            string.IsNullOrWhiteSpace(selectedExecution?.RunDirectory) ? runRoot : selectedExecution.RunDirectory,
            selectedExecution?.EntryFile ?? "-",
            selectedExecution?.Command ?? "(worker-independent-runs)",
            hasFailure && !hasSuccess ? 1 : 0,
            selectedExecution?.StdOut ?? string.Empty,
            selectedExecution?.StdErr ?? string.Empty,
            status
        );
    }

    public static string BuildOrchestrationSummary(
        IReadOnlyList<CodingWorkerResult> workers,
        CodingWorkerResult finalWorker
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("오케스트레이션 단계 결과");
        foreach (var worker in workers)
        {
            builder.AppendLine($"- {worker.Role}: {worker.Provider}:{worker.Model} · status={worker.Execution.Status} · exit={worker.Execution.ExitCode}");
            var trimmed = TextOutputTruncator.TruncateWithMin200(
                ChatOutputSanitizerPolicy.RemoveCodeBlocksFromText(worker.Summary),
                260
            );
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine($"  {trimmed}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("최종 상태");
        builder.AppendLine($"{finalWorker.Provider}:{finalWorker.Model} · status={finalWorker.Execution.Status} · exit={finalWorker.Execution.ExitCode}");
        builder.AppendLine(TextOutputTruncator.TruncateWithMin200(
            ChatOutputSanitizerPolicy.RemoveCodeBlocksFromText(finalWorker.Summary),
            900
        ));
        return builder.ToString().Trim();
    }
}
