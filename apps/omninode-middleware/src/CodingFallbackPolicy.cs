using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal sealed record CodingFallbackProjectProfile(
    string ProjectKind,
    IReadOnlyList<string> EntryFiles,
    string BuildTool,
    string TestTool,
    IReadOnlyList<string> RunCommands
);

internal sealed record FallbackGeneratedFile(string Path, string Content);

internal sealed record FallbackFileBundle(string Language, string RunCommand, IReadOnlyList<FallbackGeneratedFile> Files);

internal sealed record CodingRepairObjectivePromptRequest(
    string Objective,
    string LanguageHint,
    string WorkspaceRoot,
    CodeExecutionResult LastExecution,
    IReadOnlyCollection<string> ChangedFiles,
    IReadOnlyList<string> LanguageRuleLines,
    bool IsInteractivePythonObjective
);

internal static class CodingFallbackPolicy
{
    private static readonly Regex CodeFenceRegex = new("```([a-zA-Z0-9#+._-]*)\\s*\\n(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex JsonContentFieldRegex = new("\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex JsonPathFieldRegex = new("\"path\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DomainRegex = new(@"([a-z0-9][a-z0-9-]*\.[a-z]{2,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RequestedCodingPathRegex = new(
        @"(?<path>(?:(?:[A-Za-z]:)?[\\/])?(?:[\w.-]+[\\/])*(?:[\w.-]+\.)+(?:json|html|java|tsx|jsx|mjs|cjs|cpp|cxx|hpp|htm|css|txt|md|py|ts|js|cs|kt|kts|sh|cc|hh|h|c|go|rs|php|rb|swift|yml|yaml|toml|xml|csproj|sln|gradle|svelte|vue))(?![\w.-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ExpectedOutputAfterQuotedRegex = new(
        "['\\\"`](?<value>[^'\\\"`\\r\\n]{1,160})['\\\"`]\\s*(?:를|을)?\\s*(?:출력|print|echo|표시)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ExpectedOutputBeforeQuotedRegex = new(
        "(?:출력|print|echo|표시)[^'\\\"`\\r\\n]{0,32}['\\\"`](?<value>[^'\\\"`\\r\\n]{1,160})['\\\"`]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex GenericQuotedTextRegex = new(
        "['\\\"`](?<value>[^'\\\"`\\r\\n]{1,160})['\\\"`]",
        RegexOptions.Compiled
    );

    public static string BuildCodeOnlyPrompt(
        string objective,
        string resolvedLanguage,
        string projectKind,
        IReadOnlyList<string> languageRuleLines
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("아래 요구사항을 만족하는 실행 가능한 단일 파일 코드만 반환하세요.");
        builder.AppendLine("규칙:");
        builder.AppendLine("- 반드시 첫 줄: LANGUAGE=<언어>");
        builder.AppendLine("- 반드시 단 하나의 코드블록만 출력");
        builder.AppendLine("- 설명/해설/JSON/HTML 태그 금지");
        builder.AppendLine("- 코드블록 안에는 순수 코드만 작성");
        builder.AppendLine("- 더미 구현, TODO, 의사코드 금지");
        builder.AppendLine("- 요청에 실행/출력 조건이 있으면 실제로 그 조건을 만족하는 코드만 작성");
        builder.AppendLine("- 이 경로는 명시적 단일 파일/단순 stdout 작업용이다. 여러 파일이 필요하면 파일 번들 경로가 사용된다");
        foreach (var rule in languageRuleLines)
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine();
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine($"프로젝트 프로파일: {projectKind}");
        builder.AppendLine("요구사항:");
        builder.AppendLine(objective ?? string.Empty);
        return builder.ToString().Trim();
    }

    public static string BuildFileBundlePrompt(
        string objective,
        string resolvedLanguage,
        CodingFallbackProjectProfile projectProfile,
        IReadOnlyList<string> languageRuleLines,
        IReadOnlyList<string>? requestedPaths = null
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("아래 요구사항을 만족하는 파일 번들을 JSON으로만 반환하세요.");
        builder.AppendLine("규칙:");
        builder.AppendLine("- 반드시 JSON 객체만 출력");
        builder.AppendLine("- 설명, 해설, 마크다운, 코드펜스 금지");
        builder.AppendLine("- files 배열에는 필요한 파일만 넣기");
        builder.AppendLine("- path 는 상대경로만 사용");
        builder.AppendLine("- content 는 실제 파일 내용 전체를 문자열로 넣기");
        builder.AppendLine("- run 은 최종 실행 명령이 있으면 넣고, 없으면 빈 문자열");
        builder.AppendLine("- build/test/entry/notes 필드는 알맞게 넣고, 없으면 빈 문자열 또는 빈 배열");
        builder.AppendLine("- 일반 코딩 요청은 여러 파일 프로젝트 구조를 우선하라");
        builder.AppendLine("- 파일 간 import/export/package/include 경로와 실행 엔트리가 실제로 맞아야 한다");
        builder.AppendLine("- 미사용 파일, 설명용 더미 파일, TODO 전용 파일 금지");
        builder.AppendLine("- 빈 함수, placeholder 데이터, 껍데기 UI, print-only 게임으로 완료 처리 금지");
        foreach (var rule in languageRuleLines)
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("스키마:");
        builder.AppendLine("{\"language\":\"python\",\"projectProfile\":\"python-package\",\"files\":[{\"path\":\"pyproject.toml\",\"content\":\"...\"},{\"path\":\"src/app.py\",\"content\":\"...\"}],\"entry\":[\"src/app.py\"],\"build\":\"python3 -m compileall -q .\",\"test\":\"python3 -m pytest -q\",\"run\":\"python3 -m app\",\"notes\":\"\"}");
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine($"프로젝트 프로파일: {projectProfile.ProjectKind}");
        builder.AppendLine($"권장 엔트리: {string.Join(", ", projectProfile.EntryFiles)}");
        builder.AppendLine($"권장 빌드 도구: {projectProfile.BuildTool}");
        builder.AppendLine($"권장 테스트 도구: {projectProfile.TestTool}");
        if (projectProfile.RunCommands.Count > 0)
        {
            builder.AppendLine("권장 실행 명령:");
            foreach (var command in projectProfile.RunCommands.Take(3))
            {
                builder.AppendLine($"- {command}");
            }
        }
        if (requestedPaths != null && requestedPaths.Count > 0)
        {
            builder.AppendLine("우선 파일 후보:");
            foreach (var path in requestedPaths.Take(6))
            {
                builder.AppendLine($"- {path}");
            }
        }

        builder.AppendLine("요구사항:");
        builder.AppendLine(objective);
        return builder.ToString().Trim();
    }

    public static ParsedCode ExtractFallbackCode(string rawText, string languageHint, string objective)
    {
        var initialLanguage = CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, objective);
        var variants = CodingLoopPlanParser.BuildTextVariants(rawText ?? string.Empty);
        foreach (var variant in variants)
        {
            var matches = CodeFenceRegex.Matches(variant);
            if (matches.Count == 0)
            {
                continue;
            }

            var fence = matches[^1];
            var code = fence.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var fenceLanguage = fence.Groups[1].Value.Trim();
            var resolved = string.IsNullOrWhiteSpace(fenceLanguage)
                ? initialLanguage
                : CodingLanguagePolicy.NormalizeLanguageForCode(fenceLanguage);
            return new ParsedCode(resolved, GeneratedCodeTextPolicy.NormalizeGeneratedFileContent(code));
        }

        foreach (var variant in variants)
        {
            var prefixed = GeneratedCodeTextPolicy.ExtractLanguagePrefixedPlainCode(variant, initialLanguage);
            if (!string.IsNullOrWhiteSpace(prefixed.Code))
            {
                return new ParsedCode(prefixed.Language, GeneratedCodeTextPolicy.NormalizeGeneratedFileContent(prefixed.Code));
            }
        }

        var plain = (rawText ?? string.Empty).Trim();
        if (plain.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || plain.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCode("html", plain);
        }

        var decoded = WebUtility.HtmlDecode(rawText ?? string.Empty);
        var contentMatches = JsonContentFieldRegex.Matches(decoded)
            .Select(match => new
            {
                Value = Regex.Unescape(match.Groups[1].Value).Trim(),
                Index = match.Index
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .OrderByDescending(x => x.Value.Length)
            .ToArray();
        if (contentMatches.Length > 0)
        {
            var extractedContent = contentMatches[0].Value;
            var detectedLanguage = initialLanguage;
            var pathMatches = JsonPathFieldRegex.Matches(decoded)
                .Select(match => new
                {
                    Value = Regex.Unescape(match.Groups[1].Value).Trim(),
                    Index = match.Index
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToArray();
            if (pathMatches.Length > 0)
            {
                var closestPath = pathMatches
                    .OrderBy(path => Math.Abs(path.Index - contentMatches[0].Index))
                    .First();
                detectedLanguage = CodingLanguagePolicy.GuessLanguageFromPath(closestPath.Value, detectedLanguage);
            }

            return new ParsedCode(detectedLanguage, GeneratedCodeTextPolicy.NormalizeGeneratedFileContent(extractedContent));
        }

        return new ParsedCode(initialLanguage, string.Empty);
    }

    public static FallbackFileBundle ExtractFallbackFileBundle(string rawText, string languageHint, string objective)
    {
        var initialLanguage = CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, objective);
        var explicitLanguage = CodingLanguagePolicy.ResolveExplicitObjectiveLanguage(objective);
        var text = (rawText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new FallbackFileBundle(initialLanguage, string.Empty, Array.Empty<FallbackGeneratedFile>());
        }

        var variants = CodingLoopPlanParser.BuildTextVariants(text);
        var candidates = new List<string>();
        foreach (var variant in variants)
        {
            if (variant.StartsWith("{", StringComparison.Ordinal))
            {
                candidates.Add(variant);
            }

            var firstBrace = variant.IndexOf('{');
            var lastBrace = variant.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                candidates.Add(variant[firstBrace..(lastBrace + 1)]);
            }

            var codeFence = CodeFenceRegex.Match(variant);
            if (codeFence.Success)
            {
                candidates.Add(codeFence.Groups[2].Value.Trim());
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(CodingLoopPlanParser.NormalizeJsonCandidate(candidate));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var language = CodingLanguagePolicy.NormalizeLanguageForCode(GetStringProperty(doc.RootElement, "language") ?? initialLanguage);
                if (language == "auto")
                {
                    language = ResolveAutoBundleLanguage(languageHint, objective);
                }
                var runCommand = GetStringProperty(doc.RootElement, "run")
                    ?? GetStringProperty(doc.RootElement, "command")
                    ?? string.Empty;
                var files = new List<FallbackGeneratedFile>();
                if (doc.RootElement.TryGetProperty("files", out var filesElement)
                    && filesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fileElement in filesElement.EnumerateArray())
                    {
                        if (fileElement.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var rawPath = GetStringProperty(fileElement, "path")
                            ?? GetStringProperty(fileElement, "file")
                            ?? string.Empty;
                        var content = GetStringProperty(fileElement, "content")
                            ?? GetStringProperty(fileElement, "code")
                            ?? GetStringProperty(fileElement, "text")
                            ?? string.Empty;
                        var normalizedPath = NormalizeRequestedCodingPath(rawPath);
                        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(content))
                        {
                            continue;
                        }

                        files.Add(new FallbackGeneratedFile(normalizedPath, GeneratedCodeTextPolicy.NormalizeGeneratedFileContent(content)));
                    }
                }

                if (files.Count > 0)
                {
                    if (!string.IsNullOrWhiteSpace(explicitLanguage)
                        && explicitLanguage is not ("html" or "css" or "javascript")
                        && language is "html" or "css" or "javascript")
                    {
                        continue;
                    }

                    return new FallbackFileBundle(language, NormalizeGeneratedRunCommand(runCommand), files);
                }
            }
            catch
            {
            }
        }

        return new FallbackFileBundle(initialLanguage, string.Empty, Array.Empty<FallbackGeneratedFile>());
    }

    public static string BuildRepairObjectivePrompt(CodingRepairObjectivePromptRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine(request.Objective?.Trim() ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("[최종 검증 실패]");
        builder.AppendLine($"언어 힌트: {request.LanguageHint}");
        builder.AppendLine($"실패 명령: {CompactWorkspaceCommandForPrompt(request.LastExecution.Command, request.WorkspaceRoot)}");
        builder.AppendLine($"stderr: {TrimForOutput(request.LastExecution.StdErr, 1200)}");
        builder.AppendLine($"stdout: {TrimForOutput(request.LastExecution.StdOut, 600)}");
        builder.AppendLine("수정 규칙:");
        builder.AppendLine("- 방금 생성한 파일을 우선 수정");
        builder.AppendLine("- 파일 경로 문자열에 줄바꿈이나 탭을 넣지 말 것");
        builder.AppendLine("- 문자열 리터럴을 불필요한 줄바꿈으로 끊지 말 것");
        builder.AppendLine("- 실패 원인을 실제로 해결한 뒤 최종 검증까지 끝낼 것");
        builder.AppendLine("- 최종 검증에서 생성 파일 존재와 stdout 조건까지 다시 만족할 것");
        if (request.IsInteractivePythonObjective)
        {
            builder.AppendLine("- 요청한 게임 라이브러리를 제거하지 말고 필요한 외부 패키지는 import/requirements.txt에 유지하라");
            builder.AppendLine("- OMNI_HEADLESS_TEST=1 smoke 실행이 통과하도록 짧은 테스트 분기를 추가하라");
            builder.AppendLine("- print 문만 반복하는 텍스트 시뮬레이션은 금지하고 실제 입력 처리, 렌더링, 상태 갱신이 있는 게임 루프를 구현하라");
            var stderr = request.LastExecution.StdErr ?? string.Empty;
            if (stderr.Contains("_tkinter", StringComparison.OrdinalIgnoreCase)
                || stderr.Contains("tkinter", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("- tkinter 계열 import가 실패했으면 pygame 등 설치 가능한 게임 라이브러리로 전환하라");
            }
        }
        var finalStdErr = request.LastExecution.StdErr ?? string.Empty;
        if ((request.LastExecution.Status ?? string.Empty).Equals("quality_failed", StringComparison.OrdinalIgnoreCase)
            || finalStdErr.Contains("[quality-gate]", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- 품질 게이트 실패다. stderr의 [quality-gate] 미충족 항목을 모두 실제 코드로 해결하라.");
            builder.AppendLine("- 실행만 통과하는 껍데기 구현이 아니라 원본 요청의 기능 요구사항을 빠짐없이 구현하라.");
        }
        if (finalStdErr.Contains("Module not found", StringComparison.OrdinalIgnoreCase)
            || finalStdErr.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase)
            || finalStdErr.Contains("ERR_MODULE_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- Node/TypeScript 의존성 오류는 라이브러리를 제거하지 말고 package.json 의 dependencies/devDependencies 또는 import 경로를 수정하라");
        }
        if (finalStdErr.Contains("CS0246", StringComparison.OrdinalIgnoreCase)
            || finalStdErr.Contains("are you missing a using directive", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- C# 타입/네임스페이스 오류는 using, namespace, csproj PackageReference를 맞춰 해결하라");
        }
        if (finalStdErr.Contains("cannot find symbol", StringComparison.OrdinalIgnoreCase)
            || finalStdErr.Contains("package ", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- Java/Kotlin 심볼 또는 package 오류는 파일 경로, package 선언, build.gradle/pom 설정을 일치시켜 해결하라");
        }
        if (finalStdErr.Contains("undefined reference", StringComparison.OrdinalIgnoreCase)
            || finalStdErr.Contains("ld:", StringComparison.OrdinalIgnoreCase)
            || finalStdErr.Contains("ld returned", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("- C/C++ 링커 오류는 모든 소스 파일, 헤더, 라이브러리 링크 옵션, Makefile/CMakeLists.txt를 맞춰 해결하라");
        }
        foreach (var rule in request.LanguageRuleLines)
        {
            builder.AppendLine(rule);
        }
        if (request.ChangedFiles.Count > 0)
        {
            builder.AppendLine("현재 변경 파일:");
            foreach (var path in request.ChangedFiles.Take(8))
            {
                builder.AppendLine($"- {ToWorkspaceRelativePathForPrompt(request.WorkspaceRoot, path)}");
            }
        }

        return builder.ToString().Trim();
    }

    public static IReadOnlyList<string> ExtractRequestedCodingPaths(string objective, string languageHint)
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var normalizedLanguage = CodingLanguagePolicy.NormalizeLanguageForCode(languageHint);
        return RequestedCodingPathRegex.Matches(text)
            .Select(match => NormalizeRequestedCodingPath(match.Groups["path"].Value.Replace('\\', '/')))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !path.Contains("://", StringComparison.Ordinal))
            .Where(path => !path.Contains("..", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => string.Equals(CodingLanguagePolicy.GuessLanguageFromPath(path, normalizedLanguage), normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path.Count(ch => ch == '/'))
            .ToArray();
    }

    public static string SelectRequestedCodingPath(
        IReadOnlyList<string>? requestedPaths,
        string? languageHint,
        string? content
    )
    {
        if (requestedPaths == null || requestedPaths.Count == 0)
        {
            return string.Empty;
        }

        if (requestedPaths.Count == 1)
        {
            var onlyPath = requestedPaths[0];
            var singlePathLanguage = CodingLanguagePolicy.NormalizeCodingLanguageHintPreservingAuto(languageHint);
            if (singlePathLanguage == "auto" && !string.IsNullOrWhiteSpace(content))
            {
                singlePathLanguage = CodingLanguagePolicy.GuessLanguageFromPath(InferFallbackPathForGeneratedCode(content), singlePathLanguage);
            }

            if (singlePathLanguage == "auto")
            {
                return onlyPath;
            }

            var pathLanguage = CodingLanguagePolicy.GuessLanguageFromPath(onlyPath, "auto");
            return string.Equals(pathLanguage, singlePathLanguage, StringComparison.OrdinalIgnoreCase)
                ? onlyPath
                : string.Empty;
        }

        var normalizedLanguage = CodingLanguagePolicy.NormalizeCodingLanguageHintPreservingAuto(languageHint);
        if (normalizedLanguage == "auto" && !string.IsNullOrWhiteSpace(content))
        {
            normalizedLanguage = CodingLanguagePolicy.GuessLanguageFromPath(InferFallbackPathForGeneratedCode(content), normalizedLanguage);
        }

        if (normalizedLanguage == "auto")
        {
            return string.Empty;
        }

        return requestedPaths.FirstOrDefault(path =>
            string.Equals(CodingLanguagePolicy.GuessLanguageFromPath(path, normalizedLanguage), normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
    }

    public static string SuggestFallbackEntryPath(string language, string objective, IReadOnlyList<string>? requestedPaths = null)
    {
        var requestedPath = SelectRequestedCodingPath(requestedPaths, language, null);
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        var normalizedLanguage = CodingLanguagePolicy.NormalizeLanguageForCode(language);
        var objectiveText = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        var domainMatch = DomainRegex.Match(objectiveText);
        var projectFolder = domainMatch.Success
            ? SanitizePathSegment(domainMatch.Groups[1].Value)
            : objectiveText.Contains("naver", StringComparison.OrdinalIgnoreCase)
                ? "naver.com"
                : objectiveText.Contains("clone", StringComparison.OrdinalIgnoreCase) || objectiveText.Contains("클론", StringComparison.OrdinalIgnoreCase)
                    ? "web-clone"
                    : "task";

        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            projectFolder = "task";
        }

        return normalizedLanguage switch
        {
            "html" => $"{projectFolder}/index.html",
            "css" => $"{projectFolder}/styles.css",
            "javascript" => $"{projectFolder}/app.js",
            "typescript" => $"{projectFolder}/src/index.ts",
            "react-vite" => $"{projectFolder}/src/main.tsx",
            "go" => $"{projectFolder}/main.go",
            "rust" => $"{projectFolder}/src/main.rs",
            "php" => $"{projectFolder}/index.php",
            "ruby" => $"{projectFolder}/app.rb",
            "swift" => $"{projectFolder}/Sources/App/main.swift",
            "c" => $"{projectFolder}/main.c",
            "cpp" => $"{projectFolder}/main.cpp",
            "csharp" => $"{projectFolder}/Program.cs",
            "java" => $"{projectFolder}/Main.java",
            "kotlin" => $"{projectFolder}/Main.kt",
            "bash" => $"{projectFolder}/run.sh",
            _ => $"{projectFolder}/main.py"
        };
    }

    public static string ExtractExpectedConsoleOutput(string objective)
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty));
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var regex in new[] { ExpectedOutputAfterQuotedRegex, ExpectedOutputBeforeQuotedRegex })
        {
            foreach (Match match in regex.Matches(text))
            {
                var value = match.Groups["value"].Value.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (LooksLikeRequestedCodingPath(value) || !IsLikelyExpectedOutputLiteral(value))
                {
                    continue;
                }

                return value;
            }
        }

        if (ContainsAny(text.ToLowerInvariant(), "출력", "print", "echo", "표시"))
        {
            var fallbackCandidate = GenericQuotedTextRegex.Matches(text)
                .Select(match => match.Groups["value"].Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => !LooksLikeRequestedCodingPath(value))
                .Where(IsLikelyExpectedOutputLiteral)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(fallbackCandidate))
            {
                return fallbackCandidate;
            }
        }

        return string.Empty;
    }

    public static bool LooksLikeRequestedCodingPath(string value)
    {
        return RequestedCodingPathRegex.IsMatch(value ?? string.Empty);
    }

    public static string[] ExtractQuotedTextLiterals(string text)
    {
        return GenericQuotedTextRegex.Matches(text ?? string.Empty)
            .Select(match => match.Groups["value"].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public static bool IsLikelyExpectedOutputLiteral(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Length > 120)
        {
            return false;
        }

        return !normalized.Equals("새 요청", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("사용자 요청", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("컨텍스트 사용 규칙", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("최근 대화", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("최종 검증 실패", StringComparison.OrdinalIgnoreCase)
               && !Regex.IsMatch(normalized, @"^(?:user|assistant|system)\s*=\s*(?:true|false)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool HasSingleFileIntent(string objective)
    {
        var text = (objective ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            text,
            "파일 하나",
            "파일 한개",
            "파일 1개",
            "한 파일",
            "single file",
            "single-file",
            "one file",
            "하나만"
        );
    }

    public static string NormalizeGeneratedActionPath(string? path)
    {
        var normalized = WebUtility.HtmlDecode(path ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\t', ' ');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"\s*\n\s*", string.Empty);
        normalized = normalized.Trim().Trim('"', '\'', '`', '“', '”', '‘', '’');
        normalized = Regex.Replace(normalized, @" {2,}", " ");
        normalized = normalized.Trim();
        if (normalized.StartsWith("- ", StringComparison.Ordinal))
        {
            normalized = normalized[2..].Trim();
        }

        if (normalized.StartsWith("path:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(normalized.IndexOf(':') + 1)..].Trim();
        }

        normalized = normalized.Trim().Trim('"', '\'', '`', '“', '”', '‘', '’');
        return normalized;
    }

    public static string NormalizeRequestedCodingPath(string? path)
    {
        var normalized = NormalizeGeneratedActionPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = CollapseKnownCodingRootPrefixes(normalized);
        normalized = TryRestoreUnixAbsolutePath(normalized);
        if (Path.IsPathRooted(normalized))
        {
            var fileName = Path.GetFileName(normalized);
            return string.IsNullOrWhiteSpace(fileName) ? string.Empty : fileName;
        }

        normalized = NormalizeSuspiciousIntermediateFileSegments(normalized.Replace('\\', '/').Trim('/'));
        return IsSafeRelativeCodingPath(normalized) ? normalized : string.Empty;
    }

    public static string CollapseKnownCodingRootPrefixes(string path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var runTail = TryExtractRelativePathAfterRunsMarker(normalized);
        if (!string.IsNullOrWhiteSpace(runTail))
        {
            return runTail;
        }

        foreach (var marker in new[] { "workspace/coding/", "coding/" })
        {
            var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var tail = normalized[(index + marker.Length)..].Trim('/');
            if (IsSafeRelativeCodingPath(tail))
            {
                return tail;
            }
        }

        return normalized;
    }

    public static bool IsSafeRelativeCodingPath(string? path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(normalized)
            && !Path.IsPathRooted(normalized)
            && !normalized.Contains("..", StringComparison.Ordinal)
            && !normalized.StartsWith("/", StringComparison.Ordinal)
            && !normalized.StartsWith("./", StringComparison.Ordinal)
            && !normalized.StartsWith(".\\", StringComparison.Ordinal);
    }

    public static string InferFallbackPathForGeneratedCode(string? content)
    {
        var lowered = (content ?? string.Empty).ToLowerInvariant();
        if (lowered.Contains("<!doctype html", StringComparison.Ordinal)
            || lowered.Contains("<html", StringComparison.Ordinal))
        {
            return "index.html";
        }

        if (lowered.Contains("using system;", StringComparison.Ordinal)
            || lowered.Contains("namespace ", StringComparison.Ordinal))
        {
            return "Program.cs";
        }

        if (lowered.Contains("#!/usr/bin/env bash", StringComparison.Ordinal)
            || lowered.Contains("set -e", StringComparison.Ordinal)
            || lowered.Contains("echo ", StringComparison.Ordinal))
        {
            return "run.sh";
        }

        if (lowered.Contains("function ", StringComparison.Ordinal)
            || lowered.Contains("console.log(", StringComparison.Ordinal)
            || lowered.Contains("=>", StringComparison.Ordinal))
        {
            return "main.js";
        }

        return "main.py";
    }

    public static string NormalizeGeneratedRunCommand(string? command)
    {
        var normalized = WebUtility.HtmlDecode(command ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!normalized.Contains('\n'))
        {
            return StripTrailingDisplayCommandHints(normalized);
        }

        if (normalized.Contains("<<", StringComparison.Ordinal)
            || normalized.Contains("EOF", StringComparison.Ordinal)
            || normalized.Contains("then\n", StringComparison.Ordinal)
            || normalized.Contains("do\n", StringComparison.Ordinal)
            || normalized.Contains("case\n", StringComparison.Ordinal))
        {
            return normalized;
        }

        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        return StripTrailingDisplayCommandHints(string.Join(' ', lines));
    }

    private static string ResolveAutoBundleLanguage(string? languageHint, string objective)
    {
        var language = CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, objective ?? string.Empty);
        return language == "auto" ? "python" : language;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string CompactWorkspaceCommandForPrompt(string command, string workspaceRoot)
    {
        var normalized = (command ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            normalized = normalized.Replace(workspaceRoot, ".", StringComparison.OrdinalIgnoreCase);
        }

        return TrimForOutput(normalized, 500);
    }

    private static string ToWorkspaceRelativePathForPrompt(string workspaceRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var relative = Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
            return string.IsNullOrWhiteSpace(relative) ? Path.GetFileName(path) : relative;
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static string TrimForOutput(string text, int limit) =>
        TextOutputTruncator.TruncateWithEllipsis(text, limit);

    private static string TryExtractRelativePathAfterRunsMarker(string path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        const string marker = "workspace/coding/runs/";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var tail = normalized[(index + marker.Length)..].Trim('/');
        if (string.IsNullOrWhiteSpace(tail))
        {
            return string.Empty;
        }

        var firstSlash = tail.IndexOf('/');
        if (firstSlash < 0 || firstSlash >= tail.Length - 1)
        {
            return string.Empty;
        }

        var relative = tail[(firstSlash + 1)..].Trim('/');
        return IsSafeRelativeCodingPath(relative) ? relative : string.Empty;
    }

    private static string TryRestoreUnixAbsolutePath(string path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        return normalized.StartsWith("Users/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("home/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("private/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("tmp/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("var/folders/", StringComparison.OrdinalIgnoreCase)
                ? "/" + normalized
                : normalized;
    }

    private static string NormalizeSuspiciousIntermediateFileSegments(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (segments.Count <= 1)
        {
            return path;
        }

        static bool LooksLikeSourceFileSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return false;
            }

            var extension = Path.GetExtension(segment).ToLowerInvariant();
            return extension is ".py" or ".js" or ".mjs" or ".cjs" or ".ts" or ".tsx" or ".jsx"
                or ".html" or ".htm" or ".css" or ".java" or ".c" or ".cc" or ".cpp" or ".cxx"
                or ".h" or ".hh" or ".hpp" or ".cs" or ".kt" or ".sh" or ".json" or ".txt" or ".md";
        }

        for (var i = segments.Count - 2; i >= 0; i -= 1)
        {
            if (LooksLikeSourceFileSegment(segments[i]))
            {
                segments.RemoveAt(i);
            }
        }

        return string.Join('/', segments);
    }

    private static string SanitizePathSegment(string raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? string.Empty : builder.ToString();
    }

    private static string StripTrailingDisplayCommandHints(string command)
    {
        var normalized = (command ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        while (TryStripTrailingDisplayCommandHint(normalized, out var stripped))
        {
            normalized = stripped;
        }

        return normalized;
    }

    private static bool TryStripTrailingDisplayCommandHint(string command, out string stripped)
    {
        stripped = (command ?? string.Empty).TrimEnd();
        if (stripped.Length < 4 || stripped[^1] != ']')
        {
            return false;
        }

        var openBracketIndex = stripped.LastIndexOf(" [", StringComparison.Ordinal);
        if (openBracketIndex < 0)
        {
            return false;
        }

        var hintBody = stripped[(openBracketIndex + 2)..^1].Trim();
        if (!hintBody.Contains(':', StringComparison.Ordinal)
            && !hintBody.Contains('=', StringComparison.Ordinal))
        {
            return false;
        }

        if (!hintBody.StartsWith("stdout 포함", StringComparison.OrdinalIgnoreCase)
            && !hintBody.StartsWith("파일 확인", StringComparison.OrdinalIgnoreCase)
            && !hintBody.StartsWith("stdout", StringComparison.OrdinalIgnoreCase)
            && !hintBody.StartsWith("verified file", StringComparison.OrdinalIgnoreCase)
            && !hintBody.StartsWith("verified files", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        stripped = stripped[..openBracketIndex].TrimEnd();
        return !string.IsNullOrWhiteSpace(stripped);
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
