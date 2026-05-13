using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private string BuildWorkspaceSnapshot(string workspaceRoot, string provider, int? maxEntriesOverride = null)
    {
        try
        {
            if (!Directory.Exists(workspaceRoot))
            {
                return "(workspace not found)";
            }

            var configuredMaxEntries = Math.Max(
                20,
                string.Equals(provider, "copilot", StringComparison.OrdinalIgnoreCase)
                    ? Math.Min(_config.CodingWorkspaceSnapshotMaxEntries, 60)
                    : _config.CodingWorkspaceSnapshotMaxEntries
            );
            var maxEntries = maxEntriesOverride.HasValue
                ? Math.Max(12, maxEntriesOverride.Value)
                : configuredMaxEntries;
            var files = Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(workspaceRoot, path))
                .Where(path => !path.StartsWith(".git", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.StartsWith("node_modules", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (files.Length == 0)
            {
                return "(empty)";
            }

            var lines = new List<string>();
            lines.Add($"total_files={files.Length}");
            foreach (var relative in files.Take(maxEntries))
            {
                var fullPath = Path.Combine(workspaceRoot, relative);
                long size = 0;
                try
                {
                    size = new FileInfo(fullPath).Length;
                }
                catch
                {
                }

                lines.Add($"{relative} ({size}B)");
            }

            if (files.Length > maxEntries)
            {
                lines.Add($"... +{files.Length - maxEntries} files");
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"(snapshot error: {ex.Message})";
        }
    }

    private string ResolveWorkspaceRoot()
    {
        var configured = string.IsNullOrWhiteSpace(_config.WorkspaceRootDir)
            ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."))
            : _config.WorkspaceRootDir;
        var fullPath = Path.GetFullPath(configured);
        try
        {
            Directory.CreateDirectory(fullPath);
        }
        catch
        {
        }

        return fullPath;
    }

    private string ResolveCodingWorkspaceRoot(string? workspaceRootOverride)
    {
        var candidate = string.IsNullOrWhiteSpace(workspaceRootOverride)
            ? ResolveWorkspaceRoot()
            : Path.GetFullPath(workspaceRootOverride);

        try
        {
            Directory.CreateDirectory(candidate);
        }
        catch
        {
        }

        return candidate;
    }

    private string CreateCodingRunWorkspaceRoot(string modeLabel)
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        var runsRoot = Path.Combine(workspaceRoot, "runs");
        Directory.CreateDirectory(runsRoot);

        var safeMode = SanitizePathSegment((modeLabel ?? string.Empty).ToLowerInvariant());
        if (string.IsNullOrWhiteSpace(safeMode))
        {
            safeMode = "coding";
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var folderName = $"{timestamp}-{safeMode}-{suffix}";
            var runRoot = Path.Combine(runsRoot, folderName);
            if (Directory.Exists(runRoot))
            {
                continue;
            }

            Directory.CreateDirectory(runRoot);
            return runRoot;
        }

        var fallbackRoot = Path.Combine(runsRoot, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{safeMode}");
        Directory.CreateDirectory(fallbackRoot);
        return fallbackRoot;
    }

    private static bool IsPathUnderRoot(string candidatePath, string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(fullCandidate, fullRoot, comparison))
        {
            return true;
        }

        var rootWithSlash = fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(rootWithSlash, comparison);
    }

    private static string? ResolveActionPathOrFallback(
        string actionType,
        string? path,
        string? content,
        IReadOnlyList<string>? requestedPaths = null,
        string? workspaceRoot = null
    )
    {
        var normalizedPath = string.IsNullOrWhiteSpace(workspaceRoot)
            ? NormalizeGeneratedActionPath(path)
            : NormalizeGeneratedActionPathForWorkspace(path, workspaceRoot);
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            return normalizedPath;
        }

        if (actionType == "write_file" || actionType == "append_file")
        {
            var requestedPath = SelectRequestedCodingPath(requestedPaths, GuessLanguageFromPath(InferFallbackPathForGeneratedCode(content), "auto"), content);
            if (!string.IsNullOrWhiteSpace(requestedPath))
            {
                return requestedPath;
            }

            return InferFallbackPathForGeneratedCode(content);
        }

        return null;
    }

    private static string NormalizeGeneratedActionPath(string? path)
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

    private static string NormalizeRequestedCodingPath(string? path)
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

    private static string NormalizeGeneratedActionPathForWorkspace(string? path, string workspaceRoot)
    {
        var normalized = NormalizeGeneratedActionPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = CollapseKnownCodingRootPrefixes(normalized);
        if (!Path.IsPathRooted(normalized))
        {
            normalized = normalized.Replace('\\', '/').Trim('/');
            return IsSafeRelativeCodingPath(normalized) ? normalized : string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(normalized);
            if (IsPathUnderRoot(fullPath, workspaceRoot))
            {
                return Path.GetRelativePath(workspaceRoot, fullPath).Replace('\\', '/');
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CollapseKnownCodingRootPrefixes(string path)
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

    private static bool IsSafeRelativeCodingPath(string? path)
    {
        var normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(normalized)
            && !Path.IsPathRooted(normalized)
            && !normalized.Contains("..", StringComparison.Ordinal)
            && !normalized.StartsWith("/", StringComparison.Ordinal)
            && !normalized.StartsWith("./", StringComparison.Ordinal)
            && !normalized.StartsWith(".\\", StringComparison.Ordinal);
    }

    private static string NormalizeGeneratedRunCommand(string? command)
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

    private static string InferFallbackPathForGeneratedCode(string? content)
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

    private static string ResolveWorkspacePath(string workspaceRoot, string? relativeOrAbsolutePath)
    {
        var raw = (relativeOrAbsolutePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("path is required");
        }

        if (Path.IsPathRooted(raw))
        {
            var fullPath = Path.GetFullPath(raw);
            if (!IsPathUnderRoot(fullPath, workspaceRoot))
            {
                throw new InvalidOperationException("workspace 밖 경로는 코딩탭 자동 작업에서 사용할 수 없습니다.");
            }

            return fullPath;
        }

        var resolved = Path.GetFullPath(Path.Combine(workspaceRoot, raw));
        if (!IsPathUnderRoot(resolved, workspaceRoot))
        {
            throw new InvalidOperationException("workspace 밖 경로는 코딩탭 자동 작업에서 사용할 수 없습니다.");
        }

        return resolved;
    }

    private static string TryBuildExplicitVerificationCommand(
        string objectiveText,
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles
    )
    {
        if (string.IsNullOrWhiteSpace(objectiveText))
        {
            return string.Empty;
        }

        foreach (Match match in ExplicitShellExecutionCommandRegex.Matches(objectiveText))
        {
            var rawCommand = (match.Groups["cmd"].Value ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedPath = NormalizeRequestedCodingPath(match.Groups["path"].Value.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = ResolveWorkspacePath(workspaceRoot, normalizedPath);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (changedFiles.Count > 0 && !changedFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var safePath = EscapeShellArg(fullPath);
            return rawCommand switch
            {
                "node" => $"if command -v node >/dev/null 2>&1; then node {safePath}; else echo 'node 없음'; exit 1; fi",
                "python" or "python3" => $"python3 {safePath}",
                "bash" => $"if command -v bash >/dev/null 2>&1; then bash {safePath}; else echo 'bash 없음'; exit 1; fi",
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private static string TrySelectExplicitExecutionTargetPath(
        string objectiveText,
        string workspaceRoot,
        IReadOnlyCollection<string> changedFiles
    )
    {
        if (string.IsNullOrWhiteSpace(objectiveText))
        {
            return string.Empty;
        }

        foreach (Match match in ExplicitExecutionTargetRegex.Matches(objectiveText))
        {
            var normalizedPath = NormalizeRequestedCodingPath(match.Groups["path"].Value.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = ResolveWorkspacePath(workspaceRoot, normalizedPath);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (changedFiles.Count > 0 && !changedFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            return fullPath;
        }

        return string.Empty;
    }

    private static string? SelectEntryLikeChangedFile(string normalizedLanguage, IReadOnlyCollection<string> changedFiles)
    {
        var preferredNames = normalizedLanguage switch
        {
            "javascript" => new[] { "index.js", "main.js", "app.js", "server.js", "cli.js" },
            "typescript" => new[] { "index.ts", "main.ts", "app.ts", "server.ts", "cli.ts", "main.tsx", "App.tsx" },
            "react-vite" => new[] { "main.tsx", "main.jsx", "App.tsx", "App.jsx", "index.html" },
            "python" => new[] { "main.py", "app.py", "run.py", "cli.py" },
            "java" => new[] { "Main.java", "App.java", "Run.java" },
            "go" => new[] { "main.go" },
            "rust" => new[] { "main.rs", "lib.rs" },
            "php" => new[] { "index.php", "app.php" },
            "ruby" => new[] { "app.rb", "main.rb" },
            "swift" => new[] { "main.swift" },
            "c" => new[] { "main.c", "app.c" },
            "cpp" => new[] { "main.cpp", "app.cpp" },
            "bash" => new[] { "run.sh", "main.sh" },
            _ => Array.Empty<string>()
        };

        foreach (var preferredName in preferredNames)
        {
            var matched = changedFiles.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path)
                && File.Exists(path)
                && string.Equals(Path.GetFileName(path), preferredName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matched))
            {
                return matched;
            }
        }

        return null;
    }

    private static bool ShouldPreferProgramExecutionForVerification(string objective, string normalizedLanguage, string? expectedOutput)
    {
        if (!string.IsNullOrWhiteSpace(expectedOutput))
        {
            return true;
        }

        if (IsInteractiveProgramObjective(objective, normalizedLanguage))
        {
            return false;
        }

        if (normalizedLanguage is not ("python" or "javascript" or "typescript" or "react-vite" or "go" or "rust" or "php" or "ruby" or "swift" or "bash"))
        {
            return false;
        }

        var text = (objective ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            text,
            "직접 실행",
            "실행해서",
            "실행해",
            "실행 시",
            "실행시",
            "실행 결과",
            "실행결과",
            "실행 후",
            "실행후",
            "run ",
            "run해",
            "동작 확인",
            "동작하게",
            "출력하는지",
            "출력하게",
            "출력되게",
            "출력까지",
            "검증해",
            "검증해줘",
            "확인해",
            "확인해줘",
            "검증까지",
            "테스트까지",
            "stdout",
            "표준 출력",
            "when run"
        );
    }

    private static IReadOnlyList<string> CollectWorkspaceMaterializedFiles(string workspaceRoot)
    {
        try
        {
            if (!Directory.Exists(workspaceRoot))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path =>
                {
                    var relative = Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
                    if (relative.StartsWith(".git", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("node_modules", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("__pycache__/", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return !relative.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase)
                        && !relative.EndsWith(".pyo", StringComparison.OrdinalIgnoreCase)
                        && !relative.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase);
                })
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static int MergeWorkspaceMaterializedFiles(string workspaceRoot, ISet<string> changedFiles)
    {
        if (changedFiles == null)
        {
            return 0;
        }

        var merged = 0;
        foreach (var path in CollectWorkspaceMaterializedFiles(workspaceRoot))
        {
            if (changedFiles.Add(path))
            {
                merged++;
            }
        }

        return merged;
    }

    private static string NormalizePythonCommandForShell(string command)
    {
        var raw = command ?? string.Empty;
        var trimmed = raw.TrimStart();
        if (OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(trimmed))
        {
            return raw;
        }

        if (!trimmed.StartsWith("python", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("python3", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        if (trimmed.Length > "python".Length)
        {
            var next = trimmed["python".Length];
            if (!char.IsWhiteSpace(next))
            {
                return raw;
            }
        }

        var prefixLength = raw.Length - trimmed.Length;
        var suffix = trimmed.Length > "python".Length ? trimmed["python".Length..] : string.Empty;
        return new string(' ', prefixLength) + "python3" + suffix;
    }
}
