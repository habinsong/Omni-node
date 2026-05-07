using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed record SkillCreateDirectiveResult(
    string FinalText,
    int CreatedCount,
    int FailedCount,
    IReadOnlyList<string> CreatedPaths
);

public sealed class SkillCreateDirective
{
    private static readonly Regex DirectiveRegex = new(
        @"<omni:skill\b(?<attrs>[^>]*)>(?<body>.*?)</omni:skill>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex AttributeRegex = new(
        @"(?<key>[a-zA-Z_][a-zA-Z0-9_-]*)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled
    );

    private static readonly Regex SkillNameRegex = new(
        @"^[a-z0-9][a-z0-9-]{0,62}$",
        RegexOptions.Compiled
    );

    private readonly string _projectRootDir;
    private readonly string _globalSkillsRootDir;

    public SkillCreateDirective(AppConfig config)
    {
        var workspaceRoot = Path.GetFullPath(config.WorkspaceRootDir);
        _projectRootDir = Path.GetFullPath(Path.Combine(workspaceRoot, ".."));

        var stateRoot = Environment.GetEnvironmentVariable("OMNINODE_STATE_DIR");
        if (string.IsNullOrWhiteSpace(stateRoot))
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetEnvironmentVariable("USERPROFILE")
                ?? string.Empty;
            stateRoot = string.IsNullOrWhiteSpace(home)
                ? string.Empty
                : Path.Combine(home, ".omninode");
        }

        _globalSkillsRootDir = string.IsNullOrWhiteSpace(stateRoot)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(stateRoot, "skills"));
    }

    public SkillCreateDirectiveResult Apply(string? responseText)
    {
        var text = responseText ?? string.Empty;
        if (text.IndexOf("<omni:skill", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return new SkillCreateDirectiveResult(text, 0, 0, Array.Empty<string>());
        }

        var createdPaths = new List<string>();
        var createdCount = 0;
        var failedCount = 0;

        var rewritten = DirectiveRegex.Replace(text, match =>
        {
            var attrs = ParseAttributes(match.Groups["attrs"].Value);
            var body = match.Groups["body"].Value ?? string.Empty;

            attrs.TryGetValue("name", out var name);
            attrs.TryGetValue("description", out var description);
            attrs.TryGetValue("scope", out var scope);
            attrs.TryGetValue("overwrite", out var overwriteRaw);

            var trimmedName = (name ?? string.Empty).Trim();
            var trimmedDescription = (description ?? string.Empty).Trim();
            var normalizedScope = (scope ?? "project").Trim().ToLowerInvariant();
            var allowOverwrite = string.Equals(
                (overwriteRaw ?? string.Empty).Trim(),
                "true",
                StringComparison.OrdinalIgnoreCase
            );

            if (!SkillNameRegex.IsMatch(trimmedName))
            {
                failedCount += 1;
                return BuildErrorNote(
                    $"스킬 생성 실패: 이름이 비어 있거나 규칙(kebab-case, 영문/숫자/-)을 따르지 않습니다. (값: '{trimmedName}')"
                );
            }

            string targetRootDir;
            string scopeLabel;
            if (normalizedScope == "global")
            {
                if (string.IsNullOrWhiteSpace(_globalSkillsRootDir))
                {
                    failedCount += 1;
                    return BuildErrorNote("스킬 생성 실패: 전역 스킬 경로를 확인할 수 없습니다.");
                }
                targetRootDir = _globalSkillsRootDir;
                scopeLabel = "global";
            }
            else
            {
                targetRootDir = Path.Combine(_projectRootDir, ".omni", "skills");
                scopeLabel = "project";
            }

            var skillDir = Path.GetFullPath(Path.Combine(targetRootDir, trimmedName));
            if (!IsPathUnderRoot(skillDir, targetRootDir))
            {
                failedCount += 1;
                return BuildErrorNote("스킬 생성 실패: 경로가 허용 범위를 벗어났습니다.");
            }

            var skillFilePath = Path.Combine(skillDir, "SKILL.md");

            try
            {
                var alreadyExists = File.Exists(skillFilePath);
                if (alreadyExists && !allowOverwrite)
                {
                    failedCount += 1;
                    return BuildErrorNote(
                        $"스킬 생성 실패: '{trimmedName}' 이(가) 이미 존재합니다. 덮어쓰려면 overwrite=\"true\"를 지정하세요. (경로: {RelativizeForDisplay(skillFilePath)})"
                    );
                }

                Directory.CreateDirectory(skillDir);
                var content = BuildSkillFileContent(trimmedName, trimmedDescription, body);
                File.WriteAllText(skillFilePath, content, new UTF8Encoding(false));
                createdCount += 1;
                createdPaths.Add(skillFilePath);

                var relativeForUser = RelativizeForDisplay(skillFilePath);
                var verb = alreadyExists ? "덮어썼습니다" : "생성했습니다";
                return BuildSuccessNote(
                    $"스킬 '{trimmedName}'을(를) {scopeLabel} 범위에 {verb}. (경로: {relativeForUser})\n사용 방법: \"{trimmedName} 스킬 사용해\" 또는 설정 탭의 'skills 새로고침'."
                );
            }
            catch (Exception ex)
            {
                failedCount += 1;
                return BuildErrorNote($"스킬 생성 실패: {ex.Message}");
            }
        });

        return new SkillCreateDirectiveResult(rewritten, createdCount, failedCount, createdPaths);
    }

    private static Dictionary<string, string> ParseAttributes(string raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return dict;
        }

        foreach (Match match in AttributeRegex.Matches(raw))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value;
            dict[key] = value;
        }
        return dict;
    }

    private static string BuildSkillFileContent(string name, string description, string body)
    {
        var sanitizedDescription = string.IsNullOrWhiteSpace(description)
            ? "설명이 없습니다."
            : description.Replace("\r", string.Empty).Replace("\n", " ").Trim();

        var bodyText = (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(name).Append('\n');
        sb.Append("description: ").Append(sanitizedDescription).Append('\n');
        sb.Append("---\n\n");
        if (bodyText.Length > 0)
        {
            sb.Append(bodyText);
            if (!bodyText.EndsWith("\n", StringComparison.Ordinal))
            {
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string BuildSuccessNote(string message)
    {
        return $"\n[skill_create:ok] {message}\n";
    }

    private static string BuildErrorNote(string message)
    {
        return $"\n[skill_create:error] {message}\n";
    }

    private string RelativizeForDisplay(string fullPath)
    {
        try
        {
            if (fullPath.StartsWith(_projectRootDir, StringComparison.Ordinal))
            {
                var relative = Path.GetRelativePath(_projectRootDir, fullPath);
                return relative.Replace('\\', '/');
            }
        }
        catch
        {
        }
        return fullPath;
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathWithSep = fullPath + Path.DirectorySeparatorChar;
        var rootWithSep = fullRoot + Path.DirectorySeparatorChar;
        return pathWithSep.StartsWith(rootWithSep, StringComparison.Ordinal);
    }
}
