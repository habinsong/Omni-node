using System.Security.Cryptography;
using System.Text;

namespace OmniNode.Middleware;

public sealed class AnchorReadService
{
    private const int DefaultMaxLines = 1200;
    private const int DefaultMaxChars = 200_000;

    private readonly string _workspaceRoot;

    public AnchorReadService(PathOptions paths)
    {
        _workspaceRoot = ResolveWorkspaceRoot(paths.WorkspaceRootDir);
    }

    public async Task<AnchorReadResult> ReadWithAnchorsAsync(
        string filePath,
        CancellationToken cancellationToken,
        int maxLines = DefaultMaxLines,
        int maxChars = DefaultMaxChars
    )
    {
        var fullPath = ResolveWorkspacePath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("파일을 찾을 수 없습니다.", fullPath);
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var allLines = SplitLines(text);
        var visibleLines = new List<AnchorLine>();
        var charBudget = 0;
        var truncated = false;

        for (var index = 0; index < allLines.Count; index++)
        {
            var content = allLines[index];
            if (visibleLines.Count >= maxLines || charBudget + content.Length > maxChars)
            {
                truncated = true;
                break;
            }

            charBudget += content.Length;
            visibleLines.Add(new AnchorLine(
                index + 1,
                BuildLineHash(fullPath, index + 1, content),
                content
            ));
        }

        return new AnchorReadResult(
            fullPath,
            visibleLines,
            allLines.Count,
            truncated,
            DateTimeOffset.UtcNow.ToString("O")
        );
    }

    public string ResolveWorkspacePath(string filePath)
    {
        var raw = (filePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("path is required");
        }

        var combined = Path.IsPathRooted(raw)
            ? raw
            : Path.Combine(_workspaceRoot, raw);
        var fullPath = Path.GetFullPath(combined);
        if (!IsPathUnderRoot(fullPath, _workspaceRoot))
        {
            throw new InvalidOperationException("workspace 밖의 파일은 다룰 수 없습니다.");
        }

        return fullPath;
    }

    internal static List<string> SplitLines(string text)
    {
        var normalized = NormalizeNewlines(text);
        if (string.IsNullOrEmpty(normalized))
        {
            return new List<string>();
        }

        var parts = normalized.Split('\n');
        if (normalized.EndsWith('\n') && parts.Length > 0)
        {
            parts = parts[..^1];
        }

        return parts.ToList();
    }

    internal static string JoinLines(IReadOnlyList<string> lines, bool preserveTrailingNewline)
    {
        if (lines == null || lines.Count == 0)
        {
            return string.Empty;
        }

        var joined = string.Join("\n", lines);
        return preserveTrailingNewline ? $"{joined}\n" : joined;
    }

    internal static string BuildLineHash(string fullPath, int lineNumber, string content)
    {
        var normalizedPath = (fullPath ?? string.Empty).Replace('\\', '/');
        var payload = $"{normalizedPath}\n{lineNumber}\n{content}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    internal static string NormalizeNewlines(string text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
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

        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string ResolveWorkspaceRoot(string configuredRoot)
    {
        var fallback = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
        var target = string.IsNullOrWhiteSpace(configuredRoot) ? fallback : configuredRoot;
        return Path.GetFullPath(target);
    }
}
