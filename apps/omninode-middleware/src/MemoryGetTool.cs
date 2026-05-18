using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed record MemoryGetToolResult(
    string Path,
    string Text,
    bool Disabled,
    string? Error
);

public sealed class MemoryGetTool
{
    private const string MemoryNotesPrefix = "memory-notes/";
    private const string ConversationsPrefix = "conversations/";
    private readonly string _memoryNotesRootDir;
    private readonly string _conversationStatePath;
    private readonly string _workspaceRootDir;
    private readonly string _projectRootDir;
    private readonly string _middlewareRootDir;

    public MemoryGetTool(PathOptions paths)
    {
        _memoryNotesRootDir = Path.GetFullPath(paths.MemoryNotesRootDir);
        _conversationStatePath = Path.GetFullPath(paths.ConversationStatePath);
        _workspaceRootDir = Path.GetFullPath(paths.WorkspaceRootDir);
        _projectRootDir = Path.GetFullPath(Path.Combine(_workspaceRootDir, ".."));
        _middlewareRootDir = Path.GetFullPath(Directory.GetCurrentDirectory());
    }

    public MemoryGetToolResult Get(string path, int? from = null, int? lines = null)
    {
        var normalizedPath = NormalizeRelativePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return new MemoryGetToolResult(string.Empty, string.Empty, true, "path required");
        }

        try
        {
            var content = LoadContent(normalizedPath);
            if (content == null)
            {
                return new MemoryGetToolResult(normalizedPath, string.Empty, false, null);
            }

            var sliced = SliceContent(content, from, lines);
            return new MemoryGetToolResult(normalizedPath, sliced, false, null);
        }
        catch (Exception ex)
        {
            return new MemoryGetToolResult(normalizedPath, string.Empty, true, ex.Message);
        }
    }

    private string? LoadContent(string relativePath)
    {
        if (!relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("path required");
        }

        if (relativePath.StartsWith(MemoryNotesPrefix, StringComparison.Ordinal))
        {
            return LoadMemoryNote(relativePath);
        }

        if (relativePath.StartsWith(ConversationsPrefix, StringComparison.Ordinal))
        {
            return LoadConversationSnapshot(relativePath);
        }

        return LoadWorkspaceMarkdown(relativePath);
    }

    private string? LoadMemoryNote(string relativePath)
    {
        var fileName = relativePath[MemoryNotesPrefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("path required");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_memoryNotesRootDir, fileName));
        if (!IsPathUnderRoot(fullPath, _memoryNotesRootDir))
        {
            throw new InvalidOperationException("path required");
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return File.ReadAllText(fullPath);
    }

    private string? LoadConversationSnapshot(string relativePath)
    {
        if (!File.Exists(_conversationStatePath))
        {
            return null;
        }

        ConversationState? state;
        try
        {
            var json = File.ReadAllText(_conversationStatePath);
            state = JsonSerializer.Deserialize(json, OmniJsonContext.Default.ConversationState);
        }
        catch
        {
            return null;
        }

        if (state?.Conversations == null || state.Conversations.Count == 0)
        {
            return null;
        }

        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var thread in state.Conversations)
        {
            var path = BuildConversationPath(thread.Id, seenPaths);
            if (!path.Equals(relativePath, StringComparison.Ordinal))
            {
                continue;
            }

            return BuildConversationContent(thread);
        }

        return null;
    }

    private static string BuildConversationPath(string rawId, HashSet<string> seenPaths)
    {
        var safeId = SanitizePathToken(rawId, "thread");
        var basePath = $"{ConversationsPrefix}{safeId}.md";
        if (seenPaths.Add(basePath))
        {
            return basePath;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = $"{ConversationsPrefix}{safeId}_{suffix}.md";
            if (seenPaths.Add(candidate))
            {
                return candidate;
            }

            suffix += 1;
        }
    }

    private static string BuildConversationContent(ConversationThread thread)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Conversation");
        builder.AppendLine();
        builder.AppendLine($"- id: {thread.Id}");
        builder.AppendLine($"- scope: {thread.Scope}");
        builder.AppendLine($"- mode: {thread.Mode}");
        builder.AppendLine($"- title: {thread.Title}");
        builder.AppendLine($"- project: {thread.Project}");
        builder.AppendLine($"- category: {thread.Category}");
        if (thread.Tags.Count > 0)
        {
            builder.AppendLine($"- tags: {string.Join(", ", thread.Tags)}");
        }
        builder.AppendLine($"- updated_utc: {thread.UpdatedUtc:O}");
        builder.AppendLine();
        builder.AppendLine("## Messages");
        builder.AppendLine();

        if (thread.Messages.Count == 0)
        {
            builder.AppendLine("(empty)");
            return builder.ToString();
        }

        foreach (var message in thread.Messages)
        {
            var role = NormalizeRole(message.Role);
            var text = NormalizeMultilineText(message.Text);
            builder.Append('[').Append(role).Append("] ").AppendLine(text);
        }

        return builder.ToString();
    }

    private static string SliceContent(string content, int? from, int? lines)
    {
        if (!from.HasValue && !lines.HasValue)
        {
            return content;
        }

        var lineArray = (content ?? string.Empty).Split('\n');
        var start = Math.Max(1, from ?? 1);
        var count = Math.Max(1, lines ?? lineArray.Length);
        var slice = lineArray.Skip(start - 1).Take(count);
        return string.Join("\n", slice);
    }

    private static string NormalizeRelativePath(string? raw)
    {
        return (raw ?? string.Empty)
            .Replace('\\', '/')
            .Trim();
    }

    private string? LoadWorkspaceMarkdown(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("path required");
        }

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("path required");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(x => x == ".."))
        {
            throw new InvalidOperationException("path required");
        }

        var candidates = new[]
        {
            _projectRootDir,
            _workspaceRootDir,
            _middlewareRootDir
        };

        foreach (var root in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
            if (!IsPathUnderRoot(fullPath, root))
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            return File.ReadAllText(fullPath);
        }

        return null;
    }

    private static string NormalizeRole(string? role)
    {
        var value = (role ?? string.Empty).Trim().ToLowerInvariant();
        return value is "user" or "assistant" or "system" ? value : "assistant";
    }

    private static string NormalizeMultilineText(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string SanitizePathToken(string? value, string fallback)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                builder.Append(ch);
                continue;
            }

            builder.Append('_');
        }

        var safe = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathWithSep = fullPath + Path.DirectorySeparatorChar;
        var rootWithSep = fullRoot + Path.DirectorySeparatorChar;
        return pathWithSep.StartsWith(rootWithSep, StringComparison.Ordinal);
    }
}
