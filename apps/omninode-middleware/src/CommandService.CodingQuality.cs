using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private sealed record CodingQualityGateResult(
        bool Ok,
        int Score,
        string Summary,
        IReadOnlyList<string> Passed,
        IReadOnlyList<string> Failed
    );

    private static CodingQualityGateResult EvaluateCodingQualityGate(
        string objective,
        string language,
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        CodeExecutionResult execution
    )
    {
        var normalizedLanguage = NormalizeLanguageForCode(language);
        var objectiveText = CodingLanguagePolicy.ExtractLatestCodingRequestText(objective ?? string.Empty);
        var files = (changedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var passed = new List<string>();
        var failed = new List<string>();

        if (files.Length == 0)
        {
            failed.Add("생성/수정 파일이 없습니다.");
        }
        else
        {
            passed.Add($"생성/수정 파일 {files.Length}개 확인");
        }

        var sourceByFile = ReadCodingSources(files);
        var mergedSource = string.Join("\n", sourceByFile.Values).ToLowerInvariant();
        if (LooksLikeDummyOrPlaceholderImplementation(sourceByFile))
        {
            failed.Add("TODO/placeholder/미구현 중심 코드가 남아 있습니다.");
        }
        else if (sourceByFile.Count > 0)
        {
            passed.Add("더미/TODO 중심 구현 아님");
        }

        var requestedPaths = CodingFallbackPolicy.ExtractRequestedCodingPaths(objectiveText, normalizedLanguage);
        foreach (var requestedPath in requestedPaths.Take(12))
        {
            var full = ResolveWorkspacePath(workspaceRoot, requestedPath);
            if (File.Exists(full))
            {
                passed.Add($"요청 파일 존재: {requestedPath}");
            }
            else
            {
                failed.Add($"요청 파일 누락: {requestedPath}");
            }
        }

        if (IsGameLikeCodingTask(objectiveText, normalizedLanguage) || IsInteractiveProgramObjective(objectiveText, normalizedLanguage))
        {
            EvaluateGameQuality(objectiveText, normalizedLanguage, files, mergedSource, passed, failed);
        }

        if (IsFrontendLikeCodingTask(objectiveText, normalizedLanguage) || LooksLikeBrowserAppObjective(objectiveText))
        {
            EvaluateFrontendQuality(workspaceRoot, files, mergedSource, passed, failed);
        }

        if (LooksLikeCliObjective(objectiveText))
        {
            EvaluateCliQuality(normalizedLanguage, mergedSource, passed, failed);
        }

        EvaluateLanguageProjectQuality(normalizedLanguage, workspaceRoot, files, mergedSource, objectiveText, passed, failed);

        if (string.Equals(execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            passed.Add("최종 실행/검증 exit=0");
        }
        else if (!string.Equals(execution.Status, "skipped", StringComparison.OrdinalIgnoreCase))
        {
            failed.Add($"최종 실행 상태가 성공이 아닙니다: {execution.Status}");
        }

        var score = Math.Max(0, passed.Count * 10 - failed.Count * 25);
        var ok = failed.Count == 0;
        var summary = BuildCodingQualityGateSummary(ok, score, passed, failed);
        return new CodingQualityGateResult(ok, score, summary, passed, failed);
    }

    private static CodeExecutionResult ApplyCodingQualityGateToExecution(
        string objective,
        string language,
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles,
        CodeExecutionResult execution
    )
    {
        if (!string.Equals(execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return execution;
        }

        var gate = EvaluateCodingQualityGate(objective, language, workspaceRoot, changedFiles, execution);
        if (gate.Ok)
        {
            var stdout = AppendQualityNote(execution.StdOut, gate.Summary);
            return execution with { StdOut = stdout };
        }

        var stderr = AppendQualityNote(execution.StdErr, gate.Summary);
        return execution with
        {
            ExitCode = execution.ExitCode == 0 ? 1 : execution.ExitCode,
            StdErr = stderr,
            Status = "quality_failed"
        };
    }

    private static Dictionary<string, string> ReadCodingSources(IEnumerable<string> files)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files ?? Array.Empty<string>())
        {
            try
            {
                var extension = Path.GetExtension(file);
                if (string.IsNullOrWhiteSpace(extension)
                    || !new[] { ".py", ".js", ".jsx", ".ts", ".tsx", ".html", ".css", ".cs", ".java", ".kt", ".c", ".cc", ".cpp", ".cxx", ".h", ".hpp", ".sh", ".json", ".xml", ".toml", ".gradle" }
                        .Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                result[file] = File.ReadAllText(file);
            }
            catch
            {
            }
        }

        return result;
    }

    private static bool LooksLikeDummyOrPlaceholderImplementation(IReadOnlyDictionary<string, string> sources)
    {
        var text = string.Join("\n", sources.Values);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var dummyMatches = Regex.Matches(
            text,
            @"\b(TODO|FIXME|placeholder|dummy|stub|not implemented|미구현|임시|나중에|pass\s*(#|$)|NotImplementedException|throw\s+new\s+NotImplementedException)\b",
            RegexOptions.IgnoreCase | RegexOptions.Multiline
        ).Count;
        var meaningfulLines = text
            .Split('\n')
            .Select(line => line.Trim())
            .Count(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith("#", StringComparison.Ordinal));
        return dummyMatches >= 2 || (dummyMatches >= 1 && meaningfulLines < 25);
    }

    private static void EvaluateGameQuality(
        string objectiveText,
        string language,
        IReadOnlyList<string> files,
        string mergedSource,
        List<string> passed,
        List<string> failed
    )
    {
        if (language == "python")
        {
            if (LooksLikeInteractivePythonGameSource(files))
            {
                passed.Add("Python 게임 입력/렌더링 루프 흔적 확인");
            }
            else
            {
                failed.Add("Python 게임에 실제 입력 처리/렌더링 루프가 없습니다.");
            }
        }

        if (mergedSource.Contains("print(") && !Regex.IsMatch(mergedSource, @"while\s+[^:\n]+:|requestanimationframe|pygame\.event\.get|addEventListener", RegexOptions.IgnoreCase))
        {
            failed.Add("게임 요청이 print-only 시뮬레이션에 가깝습니다.");
        }

        if (ContainsAny(objectiveText.ToLowerInvariant(), "tetris", "테트리스"))
        {
            var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["10x20 보드"] = @"cols\s*=\s*10|rows\s*=\s*20|10\s*,\s*20|board",
                ["블록/피스 정의"] = @"shape|tetromino|piece|block|블록",
                ["회전"] = @"rotat|회전",
                ["충돌"] = @"collid|collision|충돌|valid_position|check_",
                ["라인 클리어"] = @"line.*clear|clear.*line|remove.*line|라인",
                ["점수"] = @"score|점수",
                ["레벨"] = @"level|레벨",
                ["게임오버"] = @"game_over|game over|게임\s*오버"
            };
            foreach (var (label, pattern) in checks)
            {
                if (Regex.IsMatch(mergedSource, pattern, RegexOptions.IgnoreCase))
                {
                    passed.Add($"테트리스 요구사항 확인: {label}");
                }
                else
                {
                    failed.Add($"테트리스 요구사항 누락: {label}");
                }
            }
        }
    }

    private static void EvaluateFrontendQuality(
        string workspaceRoot,
        IReadOnlyList<string> files,
        string mergedSource,
        List<string> passed,
        List<string> failed
    )
    {
        var hasIndex = files.Any(path => Path.GetFileName(path).Equals("index.html", StringComparison.OrdinalIgnoreCase))
            || File.Exists(Path.Combine(workspaceRoot, "index.html"));
        var hasPackageJson = files.Any(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase))
            || File.Exists(Path.Combine(workspaceRoot, "package.json"));
        if (hasIndex || hasPackageJson)
        {
            passed.Add("브라우저 앱 엔트리 확인");
        }
        else
        {
            failed.Add("브라우저 앱 엔트리(index.html/package.json)가 없습니다.");
        }

        if (Regex.IsMatch(mergedSource, @"document\.|createRoot|ReactDOM|requestAnimationFrame|addEventListener|<body|<main|<div", RegexOptions.IgnoreCase))
        {
            passed.Add("실제 DOM/렌더링 코드 확인");
        }
        else
        {
            failed.Add("실제 DOM 렌더링 코드가 부족합니다.");
        }

        if (mergedSource.Contains("cat > ") || mergedSource.Contains("#!/usr/bin/env bash"))
        {
            failed.Add("HTML/CSS/JS 파일에 셸 스크립트 생성 코드가 섞여 있습니다.");
        }
    }

    private static void EvaluateCliQuality(string language, string mergedSource, List<string> passed, List<string> failed)
    {
        var hasInputHandling = language switch
        {
            "python" => Regex.IsMatch(mergedSource, @"sys\.argv|argparse|input\s*\(|sys\.stdin", RegexOptions.IgnoreCase),
            "javascript" => Regex.IsMatch(mergedSource, @"process\.argv|readline|process\.stdin", RegexOptions.IgnoreCase),
            "bash" => Regex.IsMatch(mergedSource, @"\$1|\$@|getopts|read\s+", RegexOptions.IgnoreCase),
            "java" => Regex.IsMatch(mergedSource, @"String\[\]\s+args|Scanner|System\.in", RegexOptions.IgnoreCase),
            "csharp" => Regex.IsMatch(mergedSource, @"string\[\]\s+args|Console\.ReadLine", RegexOptions.IgnoreCase),
            "c" or "cpp" => Regex.IsMatch(mergedSource, @"argc|argv|scanf|getline|std::cin", RegexOptions.IgnoreCase),
            _ => true
        };
        if (hasInputHandling)
        {
            passed.Add("CLI 입력/인자 처리 흔적 확인");
        }
        else
        {
            failed.Add("CLI 요청인데 인자/stdin 처리 코드가 부족합니다.");
        }
    }

    private static void EvaluateLanguageProjectQuality(
        string language,
        string workspaceRoot,
        IReadOnlyList<string> files,
        string mergedSource,
        string objectiveText,
        List<string> passed,
        List<string> failed
    )
    {
        switch (language)
        {
            case "csharp":
                if (files.Any(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) || File.Exists(Path.Combine(workspaceRoot, "App.csproj")))
                {
                    passed.Add(".NET 프로젝트 파일 확인");
                }
                else if (ContainsAny(objectiveText.ToLowerInvariant(), "nuget", "프로젝트", "project", "asp.net"))
                {
                    failed.Add("C# 프로젝트 요청인데 .csproj가 없습니다.");
                }
                break;
            case "java":
                if (Regex.IsMatch(mergedSource, @"public\s+static\s+void\s+main|application\s*\{", RegexOptions.IgnoreCase))
                {
                    passed.Add("Java 실행 엔트리 확인");
                }
                else
                {
                    failed.Add("Java 실행 엔트리(main/run)가 없습니다.");
                }
                break;
            case "c":
            case "cpp":
                if (Regex.IsMatch(mergedSource, @"\bint\s+main\s*\(", RegexOptions.IgnoreCase))
                {
                    passed.Add("네이티브 실행 엔트리 확인");
                }
                else
                {
                    failed.Add("C/C++ 실행 엔트리 main이 없습니다.");
                }
                break;
        }
    }

    private static bool LooksLikeBrowserAppObjective(string objectiveText)
    {
        var text = (objectiveText ?? string.Empty).ToLowerInvariant();
        return ContainsAny(text, "react", "vite", "canvas", "browser", "브라우저", "프론트", "frontend", "html", "css", "웹앱");
    }

    private static bool LooksLikeCliObjective(string objectiveText)
    {
        var text = (objectiveText ?? string.Empty).ToLowerInvariant();
        return ContainsAny(text, "cli", "command line", "명령줄", "커맨드", "인자", "argument", "stdin", "표준 입력");
    }

    private static string BuildCodingQualityGateSummary(bool ok, int score, IReadOnlyList<string> passed, IReadOnlyList<string> failed)
    {
        var builder = new StringBuilder();
        builder.AppendLine(ok ? $"[quality-gate] ok score={score}" : $"[quality-gate] failed score={score}");
        if (passed.Count > 0)
        {
            builder.AppendLine("충족:");
            foreach (var item in passed.Take(10))
            {
                builder.AppendLine($"- {item}");
            }
        }

        if (failed.Count > 0)
        {
            builder.AppendLine("미충족:");
            foreach (var item in failed.Take(12))
            {
                builder.AppendLine($"- {item}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string AppendQualityNote(string original, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return original ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(original)
            ? note
            : $"{original.TrimEnd()}\n\n{note}";
    }
}
