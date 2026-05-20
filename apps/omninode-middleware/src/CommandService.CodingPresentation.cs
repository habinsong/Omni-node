using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private static string BuildAutonomousCodingSummary(
        IReadOnlyList<string> iterations,
        IReadOnlyList<string> changedFiles,
        CodeExecutionResult execution,
        int maxIterations
    )
    {
        var highlights = new List<string>();
        foreach (var entry in iterations.TakeLast(12))
        {
            var highlight = ExtractCodingIterationHighlight(entry);
            if (string.IsNullOrWhiteSpace(highlight))
            {
                continue;
            }

            if (highlights.Contains(highlight, StringComparer.Ordinal))
            {
                continue;
            }

            highlights.Add(highlight);
            if (highlights.Count >= 3)
            {
                break;
            }
        }

        if (highlights.Count == 0)
        {
            highlights.Add("반복 로그에서 요약 가능한 핵심 항목이 없습니다.");
        }

        var iterationCount = Math.Min(Math.Max(iterations.Count, 0), Math.Max(maxIterations, 0));
        var displayFiles = BuildCodingDisplayPaths(changedFiles, execution.RunDirectory, 4);
        var stdoutSnippet = BuildExecutionLogSnippet(execution.StdOut, 220);
        var stderrSnippet = BuildExecutionLogSnippet(execution.StdErr, 220);
        var builder = new StringBuilder();
        builder.AppendLine($"내부 반복: {iterationCount}/{Math.Max(maxIterations, 1)}");
        builder.AppendLine($"변경 파일: {changedFiles.Count}개");
        builder.AppendLine($"실행 상태: {execution.Status} (exit={execution.ExitCode})");
        if (!string.IsNullOrWhiteSpace(execution.Command) && execution.Command != "(none)" && execution.Command != "-")
        {
            builder.AppendLine($"실행 명령: {TrimForOutput(execution.Command, 180)}");
        }

        if (displayFiles.Length > 0)
        {
            builder.AppendLine("확인 파일:");
            foreach (var file in displayFiles)
            {
                builder.AppendLine($"- {file}");
            }

            if (changedFiles.Count > displayFiles.Length)
            {
                builder.AppendLine($"- ... +{changedFiles.Count - displayFiles.Length}개");
            }
        }

        if (!string.IsNullOrWhiteSpace(stdoutSnippet))
        {
            builder.AppendLine($"stdout 요약: {stdoutSnippet}");
        }

        if (!string.IsNullOrWhiteSpace(stderrSnippet))
        {
            builder.AppendLine($"stderr 요약: {stderrSnippet}");
        }

        builder.AppendLine("핵심 진행:");
        foreach (var highlight in highlights)
        {
            builder.AppendLine($"- {TrimForOutput(highlight, 220)}");
        }

        return builder.ToString().Trim();
    }

    private static string ExtractCodingIterationHighlight(string iterationLog)
    {
        var value = (iterationLog ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Contains("plan_parse_failed", StringComparison.OrdinalIgnoreCase))
        {
            return "계획 파싱 실패로 복구 경로를 시도했습니다.";
        }

        if (value.StartsWith("fallback=write:", StringComparison.OrdinalIgnoreCase))
        {
            return "복구 코드 생성 결과를 파일로 저장했습니다.";
        }

        if (value.StartsWith("fallback=scaffold:", StringComparison.OrdinalIgnoreCase))
        {
            return "자동 스캐폴드로 기본 파일을 생성했습니다.";
        }

        if (value.StartsWith("fallback=no_code", StringComparison.OrdinalIgnoreCase))
        {
            return "복구 생성에서도 코드 블록을 찾지 못했습니다.";
        }

        var marker = "analysis=";
        var idx = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var analysis = value[(idx + marker.Length)..];
            var split = analysis.IndexOf(" | ", StringComparison.Ordinal);
            if (split >= 0)
            {
                analysis = analysis[..split];
            }

            analysis = Regex.Replace(analysis, @"\s+", " ").Trim();
            return analysis;
        }

        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string BuildCodingAssistantText(
        string mode,
        string provider,
        string model,
        string language,
        CodeExecutionResult execution,
        IReadOnlyList<string> changedFiles,
        string summary
    )
    {
        var safeCommand = string.IsNullOrWhiteSpace(execution.Command) ? "(none)" : execution.Command;
        var displayFiles = BuildCodingDisplayPaths(changedFiles, execution.RunDirectory, 6);
        var changed = displayFiles.Length == 0
            ? "- 변경 파일 없음"
            : string.Join("\n", displayFiles.Select(path => $"- {path}"));
        var hasMoreFiles = changedFiles.Count > displayFiles.Length;
        var compactSummary = TrimForOutput(ChatOutputSanitizerPolicy.RemoveCodeBlocksFromText(summary), 800);
        var stdoutSnippet = BuildExecutionLogSnippet(execution.StdOut, 260);
        var stderrSnippet = BuildExecutionLogSnippet(execution.StdErr, 260);
        var installSummary = BuildAutoInstallSummary(execution.StdOut, execution.StdErr);
        var headlessSummary = BuildHeadlessSmokeSummary(execution.Command, execution.StdOut, execution.StdErr);
        var projectProfile = ResolveCodingProjectProfile(summary, language);
        var runCommands = projectProfile.RunCommands.Count == 0
            ? string.Empty
            : string.Join(", ", projectProfile.RunCommands.Take(2));
        return $"""
                [Coding:{mode}]
                모델: {provider}:{model}
                언어: {language}
                프로젝트 프로파일: {projectProfile.ProjectKind}
                작업 폴더: {execution.RunDirectory}
                실행 상태: {execution.Status} (exit={execution.ExitCode})
                실행 명령: {safeCommand}
                권장 실행: {(string.IsNullOrWhiteSpace(runCommands) ? "-" : runCommands)}
                권장 빌드/테스트: {projectProfile.BuildTool} / {projectProfile.TestTool}
                변경 파일: {changedFiles.Count}개
                {changed}
                {(hasMoreFiles ? "- ...(추가 파일 있음, 하단 카드에서 확인)" : string.Empty)}
                {(string.IsNullOrWhiteSpace(installSummary) ? string.Empty : $"의존성: {installSummary}")}
                {(string.IsNullOrWhiteSpace(headlessSummary) ? string.Empty : $"검증: {headlessSummary}")}
                {(string.IsNullOrWhiteSpace(stdoutSnippet) ? string.Empty : $"stdout 요약: {stdoutSnippet}")}
                {(string.IsNullOrWhiteSpace(stderrSnippet) ? string.Empty : $"stderr 요약: {stderrSnippet}")}

                요약:
                {compactSummary}

                상세 파일 프리뷰는 아래 '최근 코딩 결과' 카드에서 파일 칩을 눌러 확인하세요.
                """;
    }

    private static string BuildAutoInstallSummary(params string?[] values)
    {
        var merged = string.Join("\n", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!merged.Contains("[auto-install]", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var packages = Regex.Matches(merged, @"Python (?:import 패키지|누락 모듈|CLI 패키지) 설치(?:\(\.venv\))?\((?<name>[^)]+)\)")
            .Select(match => match.Groups["name"].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return packages.Length > 0
            ? $"자동 설치 완료: {string.Join(", ", packages.Take(8))}"
            : "자동 설치 로그 포함";
    }

    private static string BuildHeadlessSmokeSummary(string? command, params string?[] logs)
    {
        var merged = string.Join("\n", new[] { command }.Concat(logs).Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!merged.Contains("OMNI_HEADLESS_TEST=1", StringComparison.Ordinal)
            && !merged.Contains("SDL_VIDEODRIVER=dummy", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return "headless smoke 검증 통과. 실제 실행은 최근 코딩 결과의 실행 명령을 사용";
    }

    private static string BuildCodingWorkerDigest(CodingWorkerResult worker)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(worker.Role))
        {
            builder.AppendLine($"역할: {worker.Role}");
        }

        var displayFiles = BuildCodingDisplayPaths(worker.ChangedFiles, worker.Execution.RunDirectory, 8);
        var stdoutSnippet = BuildExecutionLogSnippet(worker.Execution.StdOut, 260);
        var stderrSnippet = BuildExecutionLogSnippet(worker.Execution.StdErr, 260);
        builder.AppendLine($"상태: {worker.Execution.Status} (exit={worker.Execution.ExitCode})");
        builder.AppendLine($"언어: {worker.Language}");
        builder.AppendLine($"변경 파일: {worker.ChangedFiles.Count}개");
        if (!string.IsNullOrWhiteSpace(worker.Execution.Command)
            && worker.Execution.Command != "(none)"
            && worker.Execution.Command != "-")
        {
            builder.AppendLine($"실행 명령: {TrimForOutput(worker.Execution.Command, 180)}");
        }

        if (displayFiles.Length > 0)
        {
            builder.AppendLine("파일:");
            foreach (var path in displayFiles)
            {
                builder.AppendLine($"- {path}");
            }
        }

        if (!string.IsNullOrWhiteSpace(stdoutSnippet))
        {
            builder.AppendLine($"stdout 요약: {stdoutSnippet}");
        }

        if (!string.IsNullOrWhiteSpace(stderrSnippet))
        {
            builder.AppendLine($"stderr 요약: {stderrSnippet}");
        }

        var summary = TrimForOutput(ChatOutputSanitizerPolicy.RemoveCodeBlocksFromText(worker.Summary ?? string.Empty), 1800);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            builder.AppendLine("요약:");
            builder.AppendLine(summary);
        }

        return builder.ToString().Trim();
    }

    private static string[] BuildCodingDisplayPaths(IReadOnlyList<string> changedFiles, string? runDirectory, int take)
    {
        return (changedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Take(Math.Max(1, take))
            .Select(path => ToCodingDisplayPath(path, runDirectory))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static string ToCodingDisplayPath(string path, string? runDirectory)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(runDirectory))
        {
            return normalized;
        }

        try
        {
            var fullPath = Path.GetFullPath(normalized);
            var fullRunDirectory = Path.GetFullPath(runDirectory);
            if (IsPathUnderRoot(fullPath, fullRunDirectory))
            {
                return Path.GetRelativePath(fullRunDirectory, fullPath).Replace('\\', '/');
            }
        }
        catch
        {
        }

        return normalized;
    }

    private static string BuildExecutionLogSnippet(string text, int maxLength)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private static bool ShouldIncludeCodingComparisonEntry(string model, string text)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        var normalizedText = (text ?? string.Empty).Trim();
        if (normalizedModel.Equals("none", StringComparison.OrdinalIgnoreCase)
            && (normalizedText.Length == 0 || normalizedText.Equals("선택 안함", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return normalizedModel.Length > 0 || normalizedText.Length > 0;
    }

    private static string BuildCodingMultiComparisonAssistantText(IReadOnlyList<CodingWorkerResult> workers)
    {
        var builder = new StringBuilder();
        builder.Append("[[OMNI_CODING_MULTI_COMPARE_JSON]]{\"entries\":[");
        var emitted = 0;
        foreach (var worker in workers)
        {
            var body = BuildCodingWorkerDigest(worker);
            if (!ShouldIncludeCodingComparisonEntry(worker.Model, body))
            {
                continue;
            }

            if (emitted > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"provider\":\"{WebSocketGateway.EscapeJson(worker.Provider)}\",");
            builder.Append($"\"model\":\"{WebSocketGateway.EscapeJson(worker.Model)}\",");
            builder.Append($"\"role\":\"{WebSocketGateway.EscapeJson(worker.Role)}\",");
            builder.Append($"\"text\":\"{WebSocketGateway.EscapeJson(body)}\"");
            builder.Append("}");
            emitted++;
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static string BuildCodingMultiSummaryAssistantText(
        string commonSummary,
        string commonPoints,
        string differences,
        string recommendation
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("[공통 요약]");
        builder.AppendLine(string.IsNullOrWhiteSpace(commonSummary) ? "공통 요약이 없습니다." : commonSummary.Trim());
        builder.AppendLine();
        builder.AppendLine("[공통점]");
        builder.AppendLine(string.IsNullOrWhiteSpace(commonPoints) ? "공통점 없음" : commonPoints.Trim());
        builder.AppendLine();
        builder.AppendLine("[차이]");
        builder.AppendLine(string.IsNullOrWhiteSpace(differences) ? "의미 있는 차이 없음" : differences.Trim());
        builder.AppendLine();
        builder.AppendLine("[추천]");
        builder.AppendLine(string.IsNullOrWhiteSpace(recommendation) ? "추천 정리가 없습니다." : recommendation.Trim());
        return builder.ToString().Trim();
    }
}
