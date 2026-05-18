using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed record SkillFileGetResult(
    bool Ok,
    string? Error,
    string Name,
    string Scope,
    string Description,
    string Body,
    string Path
);

public sealed record SkillFileSaveResult(
    bool Ok,
    string? Error,
    string Name,
    string Scope,
    string Path
);

public sealed record SkillFileDeleteResult(
    bool Ok,
    string? Error,
    string Name,
    string Scope
);

public sealed class SkillFileService
{
    private static readonly Regex SkillNameRegex = new(
        @"^[a-z0-9][a-z0-9-]{0,62}$",
        RegexOptions.Compiled
    );

    private readonly string _projectRootDir;
    private readonly string _globalSkillsRootDir;

    public SkillFileService(PathOptions paths)
    {
        var workspaceRoot = Path.GetFullPath(paths.WorkspaceRootDir);
        _projectRootDir = ResolveProjectRoot(workspaceRoot);

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

    public SkillFileGetResult Get(string? name, string? scope)
    {
        var validation = ValidateNameAndScope(name, scope);
        if (validation.Error != null)
        {
            return new SkillFileGetResult(false, validation.Error, name ?? string.Empty, scope ?? "project", string.Empty, string.Empty, string.Empty);
        }

        var skillFilePath = Path.Combine(validation.SkillDir, "SKILL.md");
        if (!File.Exists(skillFilePath))
        {
            return new SkillFileGetResult(false, "스킬 파일이 존재하지 않습니다.", validation.Name, validation.Scope, string.Empty, string.Empty, skillFilePath);
        }

        try
        {
            var raw = File.ReadAllText(skillFilePath, Encoding.UTF8);
            var (description, body) = ParseSkillFile(raw);
            return new SkillFileGetResult(true, null, validation.Name, validation.Scope, description, body, skillFilePath);
        }
        catch (Exception ex)
        {
            return new SkillFileGetResult(false, $"읽기 실패: {ex.Message}", validation.Name, validation.Scope, string.Empty, string.Empty, skillFilePath);
        }
    }

    public SkillFileSaveResult Save(string? name, string? scope, string? description, string? body, bool allowOverwrite = true)
    {
        var validation = ValidateNameAndScope(name, scope);
        if (validation.Error != null)
        {
            return new SkillFileSaveResult(false, validation.Error, name ?? string.Empty, scope ?? "project", string.Empty);
        }

        var skillFilePath = Path.Combine(validation.SkillDir, "SKILL.md");

        try
        {
            if (File.Exists(skillFilePath) && !allowOverwrite)
            {
                return new SkillFileSaveResult(false, "같은 이름의 스킬이 이미 있습니다. 기존 스킬을 고칠 때만 overwrite를 허용하세요.", validation.Name, validation.Scope, skillFilePath);
            }

            Directory.CreateDirectory(validation.SkillDir);
            var content = BuildSkillFileContent(validation.Name, description ?? string.Empty, body ?? string.Empty);
            File.WriteAllText(skillFilePath, content, new UTF8Encoding(false));
            return new SkillFileSaveResult(true, null, validation.Name, validation.Scope, skillFilePath);
        }
        catch (Exception ex)
        {
            return new SkillFileSaveResult(false, $"저장 실패: {ex.Message}", validation.Name, validation.Scope, skillFilePath);
        }
    }

    public SkillFileDeleteResult Delete(string? name, string? scope)
    {
        var validation = ValidateNameAndScope(name, scope);
        if (validation.Error != null)
        {
            return new SkillFileDeleteResult(false, validation.Error, name ?? string.Empty, scope ?? "project");
        }

        if (!Directory.Exists(validation.SkillDir))
        {
            return new SkillFileDeleteResult(false, "스킬 폴더가 이미 없습니다.", validation.Name, validation.Scope);
        }

        try
        {
            Directory.Delete(validation.SkillDir, recursive: true);
            return new SkillFileDeleteResult(true, null, validation.Name, validation.Scope);
        }
        catch (Exception ex)
        {
            return new SkillFileDeleteResult(false, $"삭제 실패: {ex.Message}", validation.Name, validation.Scope);
        }
    }

    private (string? Error, string Name, string Scope, string SkillDir) ValidateNameAndScope(string? name, string? scope)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (!SkillNameRegex.IsMatch(trimmedName))
        {
            return ("스킬 이름이 비어 있거나 규칙(소문자/숫자/하이픈만, 시작은 영문자나 숫자)을 따르지 않습니다.", trimmedName, "project", string.Empty);
        }

        var normalizedScope = (scope ?? "project").Trim().ToLowerInvariant();
        string targetRootDir;
        if (normalizedScope == "global")
        {
            if (string.IsNullOrWhiteSpace(_globalSkillsRootDir))
            {
                return ("전역 스킬 경로를 확인할 수 없습니다.", trimmedName, normalizedScope, string.Empty);
            }
            targetRootDir = _globalSkillsRootDir;
        }
        else
        {
            normalizedScope = "project";
            targetRootDir = Path.Combine(_projectRootDir, ".omni", "skills");
        }

        var skillDir = Path.GetFullPath(Path.Combine(targetRootDir, trimmedName));
        if (!IsPathUnderRoot(skillDir, targetRootDir))
        {
            return ("경로가 허용 범위를 벗어났습니다.", trimmedName, normalizedScope, string.Empty);
        }

        return (null, trimmedName, normalizedScope, skillDir);
    }

    private static (string Description, string Body) ParseSkillFile(string raw)
    {
        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var description = string.Empty;
        var body = normalized;

        if (normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            var endIdx = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (endIdx > 0)
            {
                var frontmatter = normalized.Substring(4, endIdx - 4);
                var afterFrontmatter = endIdx + 4;
                if (afterFrontmatter < normalized.Length && normalized[afterFrontmatter] == '\n')
                {
                    afterFrontmatter += 1;
                }
                if (afterFrontmatter < normalized.Length && normalized[afterFrontmatter] == '\n')
                {
                    afterFrontmatter += 1;
                }
                body = afterFrontmatter < normalized.Length
                    ? normalized.Substring(afterFrontmatter)
                    : string.Empty;

                foreach (var line in frontmatter.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                    {
                        description = trimmed["description:".Length..].Trim().Trim('"').Trim('\'');
                        break;
                    }
                }
            }
        }

        return (description, body.TrimEnd('\n'));
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
        sb.Append("description: ").Append(QuoteYamlScalar(sanitizedDescription)).Append('\n');
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

    private static string QuoteYamlScalar(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string ResolveProjectRoot(string workspaceRootDir)
    {
        var current = Path.GetFullPath(workspaceRootDir);
        for (var i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(current, ".git"))
                || File.Exists(Path.Combine(current, ".git"))
                || File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }
            current = parent.FullName;
        }
        return Path.GetFullPath(Path.Combine(workspaceRootDir, ".."));
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
