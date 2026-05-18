using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private static readonly IReadOnlyList<LogicPathBrowseRoot> LogicWorkspaceBrowseRoots = new[]
    {
        new LogicPathBrowseRoot("workspace", "워크스페이스")
    };

    private static readonly IReadOnlyList<LogicPathBrowseRoot> LogicMemoryBrowseRoots = new[]
    {
        new LogicPathBrowseRoot("project-markdown", "프로젝트 Markdown"),
        new LogicPathBrowseRoot("workspace-markdown", "워크스페이스 Markdown"),
        new LogicPathBrowseRoot("memory-notes", "메모리 노트"),
        new LogicPathBrowseRoot("conversations", "대화 스냅샷")
    };

    public LogicPathBrowseResult BrowseLogicPath(string scope, string? rootKey, string? browsePath)
    {
        var normalizedScope = (scope ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedScope switch
        {
            "workspace" => BrowseWorkspaceLogicPath(browsePath),
            "memory" => BrowseMemoryLogicPath(rootKey, browsePath),
            _ => new LogicPathBrowseResult(
                false,
                "지원하지 않는 경로 탐색 범위입니다.",
                normalizedScope,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                null,
                Array.Empty<LogicPathBrowseRoot>(),
                Array.Empty<LogicPathBrowseEntry>()
            )
        };
    }

    private LogicPathBrowseResult BrowseWorkspaceLogicPath(string? browsePath)
    {
        return BrowseLogicDirectoryRoot(
            scope: "workspace",
            rootKey: "workspace",
            rootLabel: "워크스페이스",
            rootDirectory: ResolveWorkspaceRoot(),
            browsePath,
            roots: LogicWorkspaceBrowseRoots,
            markdownOnly: false,
            directoriesEnabled: true
        );
    }

    private LogicPathBrowseResult BrowseMemoryLogicPath(string? rootKey, string? browsePath)
    {
        var normalizedRootKey = NormalizeLogicMemoryBrowseRootKey(rootKey);
        return normalizedRootKey switch
        {
            "project-markdown" => BrowseLogicDirectoryRoot(
                scope: "memory",
                rootKey: normalizedRootKey,
                rootLabel: "프로젝트 Markdown",
                rootDirectory: Path.GetFullPath(Path.Combine(ResolveWorkspaceRoot(), "..")),
                browsePath,
                roots: LogicMemoryBrowseRoots,
                markdownOnly: true,
                directoriesEnabled: true
            ),
            "workspace-markdown" => BrowseLogicDirectoryRoot(
                scope: "memory",
                rootKey: normalizedRootKey,
                rootLabel: "워크스페이스 Markdown",
                rootDirectory: ResolveWorkspaceRoot(),
                browsePath,
                roots: LogicMemoryBrowseRoots,
                markdownOnly: true,
                directoriesEnabled: true
            ),
            "memory-notes" => BrowseLogicMemoryNotesRoot(),
            "conversations" => BrowseLogicConversationRoot(),
            _ => new LogicPathBrowseResult(
                false,
                "지원하지 않는 메모리 루트입니다.",
                "memory",
                normalizedRootKey,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                null,
                LogicMemoryBrowseRoots,
                Array.Empty<LogicPathBrowseEntry>()
            )
        };
    }

    private LogicPathBrowseResult BrowseLogicMemoryNotesRoot()
    {
        var rootDirectory = Path.GetFullPath(_paths.MemoryNotesRootDir);
        try
        {
            Directory.CreateDirectory(rootDirectory);
            var items = Directory.EnumerateFiles(rootDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .Select(filePath =>
                {
                    var name = Path.GetFileName(filePath);
                    return new LogicPathBrowseEntry(
                        name,
                        false,
                        string.Empty,
                        $"memory-notes/{name}",
                        FormatLogicBrowseFileDescription(filePath)
                    );
                })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new LogicPathBrowseResult(
                true,
                "메모리 노트 목록을 불러왔습니다.",
                "memory",
                "memory-notes",
                "메모리 노트",
                "메모리 노트",
                string.Empty,
                null,
                null,
                LogicMemoryBrowseRoots,
                items
            );
        }
        catch (Exception ex)
        {
            return new LogicPathBrowseResult(
                false,
                $"메모리 노트 목록을 불러오지 못했습니다: {ex.Message}",
                "memory",
                "memory-notes",
                "메모리 노트",
                "메모리 노트",
                string.Empty,
                null,
                null,
                LogicMemoryBrowseRoots,
                Array.Empty<LogicPathBrowseEntry>()
            );
        }
    }

    private LogicPathBrowseResult BrowseLogicConversationRoot()
    {
        try
        {
            var items = LoadLogicConversationEntries();
            return new LogicPathBrowseResult(
                true,
                "대화 스냅샷 목록을 불러왔습니다.",
                "memory",
                "conversations",
                "대화 스냅샷",
                "대화 스냅샷",
                string.Empty,
                null,
                null,
                LogicMemoryBrowseRoots,
                items
            );
        }
        catch (Exception ex)
        {
            return new LogicPathBrowseResult(
                false,
                $"대화 스냅샷 목록을 불러오지 못했습니다: {ex.Message}",
                "memory",
                "conversations",
                "대화 스냅샷",
                "대화 스냅샷",
                string.Empty,
                null,
                null,
                LogicMemoryBrowseRoots,
                Array.Empty<LogicPathBrowseEntry>()
            );
        }
    }

    private LogicPathBrowseResult BrowseLogicDirectoryRoot(
        string scope,
        string rootKey,
        string rootLabel,
        string rootDirectory,
        string? browsePath,
        IReadOnlyList<LogicPathBrowseRoot> roots,
        bool markdownOnly,
        bool directoriesEnabled
    )
    {
        var normalizedBrowsePath = NormalizeLogicBrowsePath(browsePath);
        Directory.CreateDirectory(rootDirectory);
        string fullDirectory;
        try
        {
            fullDirectory = ResolveLogicBrowseDirectory(rootDirectory, normalizedBrowsePath);
        }
        catch (Exception ex)
        {
            return new LogicPathBrowseResult(
                false,
                ex.Message,
                scope,
                rootKey,
                rootLabel,
                rootLabel,
                normalizedBrowsePath,
                null,
                null,
                roots,
                Array.Empty<LogicPathBrowseEntry>()
            );
        }

        try
        {
            var items = BuildLogicBrowseEntries(fullDirectory, normalizedBrowsePath, markdownOnly, directoriesEnabled);
            var parentBrowsePath = GetLogicParentBrowsePath(normalizedBrowsePath);
            var directorySelectPath = string.IsNullOrWhiteSpace(normalizedBrowsePath)
                ? null
                : EnsureTrailingSlash(normalizedBrowsePath);
            var displayPath = string.IsNullOrWhiteSpace(normalizedBrowsePath)
                ? rootLabel
                : $"{rootLabel} / {normalizedBrowsePath}";

            return new LogicPathBrowseResult(
                true,
                "경로 목록을 불러왔습니다.",
                scope,
                rootKey,
                rootLabel,
                displayPath,
                normalizedBrowsePath,
                parentBrowsePath,
                directorySelectPath,
                roots,
                items
            );
        }
        catch (Exception ex)
        {
            return new LogicPathBrowseResult(
                false,
                $"경로 목록을 불러오지 못했습니다: {ex.Message}",
                scope,
                rootKey,
                rootLabel,
                rootLabel,
                normalizedBrowsePath,
                null,
                null,
                roots,
                Array.Empty<LogicPathBrowseEntry>()
            );
        }
    }

    private static IReadOnlyList<LogicPathBrowseEntry> BuildLogicBrowseEntries(
        string fullDirectory,
        string normalizedBrowsePath,
        bool markdownOnly,
        bool directoriesEnabled
    )
    {
        var items = new List<LogicPathBrowseEntry>();

        if (directoriesEnabled)
        {
            foreach (var directory in Directory.EnumerateDirectories(fullDirectory)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var nextBrowsePath = CombineLogicBrowsePath(normalizedBrowsePath, name);
                items.Add(new LogicPathBrowseEntry(
                    name,
                    true,
                    nextBrowsePath,
                    EnsureTrailingSlash(nextBrowsePath),
                    "폴더"
                ));
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(fullDirectory)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            if (markdownOnly && !filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var relativePath = CombineLogicBrowsePath(normalizedBrowsePath, name);
            items.Add(new LogicPathBrowseEntry(
                name,
                false,
                string.Empty,
                relativePath,
                FormatLogicBrowseFileDescription(filePath)
            ));
        }

        return items;
    }

    private IReadOnlyList<LogicPathBrowseEntry> LoadLogicConversationEntries()
    {
        var statePath = Path.GetFullPath(_paths.ConversationStatePath);
        if (!File.Exists(statePath))
        {
            return Array.Empty<LogicPathBrowseEntry>();
        }

        ConversationState? state;
        try
        {
            var json = File.ReadAllText(statePath);
            state = JsonSerializer.Deserialize(json, OmniJsonContext.Default.ConversationState);
        }
        catch
        {
            return Array.Empty<LogicPathBrowseEntry>();
        }

        if (state?.Conversations == null || state.Conversations.Count == 0)
        {
            return Array.Empty<LogicPathBrowseEntry>();
        }

        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        return state.Conversations
            .Select(thread =>
            {
                var fileName = BuildLogicConversationBrowsePath(thread.Id, seenPaths);
                var description = $"{(thread.Scope ?? "chat")} · {(thread.Mode ?? "single")} · {thread.UpdatedUtc:yyyy-MM-dd HH:mm}";
                return new LogicPathBrowseEntry(
                    fileName,
                    false,
                    string.Empty,
                    $"conversations/{fileName}",
                    description
                );
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildLogicConversationBrowsePath(string rawId, HashSet<string> seenPaths)
    {
        var safeId = SanitizeLogicBrowseToken(rawId, "thread");
        var basePath = $"{safeId}.md";
        if (seenPaths.Add(basePath))
        {
            return basePath;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = $"{safeId}_{suffix}.md";
            if (seenPaths.Add(candidate))
            {
                return candidate;
            }

            suffix += 1;
        }
    }

    private static string NormalizeLogicMemoryBrowseRootKey(string? rootKey)
    {
        var normalized = (rootKey ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "workspace" => "workspace-markdown",
            "project" => "project-markdown",
            "project-markdown" => "project-markdown",
            "workspace-markdown" => "workspace-markdown",
            "memory-notes" => "memory-notes",
            "conversations" => "conversations",
            _ => "project-markdown"
        };
    }

    private static string NormalizeLogicBrowsePath(string? browsePath)
    {
        var normalized = (browsePath ?? string.Empty)
            .Replace('\\', '/')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = normalized.Trim('/');
        return normalized;
    }

    private static string ResolveLogicBrowseDirectory(string rootDirectory, string normalizedBrowsePath)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        var candidate = string.IsNullOrWhiteSpace(normalizedBrowsePath)
            ? fullRoot
            : Path.GetFullPath(Path.Combine(fullRoot, normalizedBrowsePath));
        if (!IsLogicBrowsePathUnderRoot(candidate, fullRoot))
        {
            throw new InvalidOperationException("허용되지 않는 경로입니다.");
        }

        if (File.Exists(candidate))
        {
            candidate = Path.GetDirectoryName(candidate) ?? fullRoot;
        }

        if (!Directory.Exists(candidate))
        {
            throw new InvalidOperationException("디렉터리를 찾을 수 없습니다.");
        }

        return candidate;
    }

    private static string CombineLogicBrowsePath(string basePath, string name)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return name;
        }

        return $"{basePath.TrimEnd('/')}/{name}";
    }

    private static string? GetLogicParentBrowsePath(string normalizedBrowsePath)
    {
        if (string.IsNullOrWhiteSpace(normalizedBrowsePath))
        {
            return null;
        }

        var index = normalizedBrowsePath.LastIndexOf('/');
        return index <= 0 ? string.Empty : normalizedBrowsePath[..index];
    }

    private static string EnsureTrailingSlash(string path)
    {
        var normalized = NormalizeLogicBrowsePath(path);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : $"{normalized}/";
    }

    private static bool IsLogicBrowsePathUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.Ordinal)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string FormatLogicBrowseFileDescription(string filePath)
    {
        var info = new FileInfo(filePath);
        var size = info.Exists ? FormatLogicBrowseFileSize(info.Length) : "-";
        var updated = info.Exists
            ? info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "-";
        return $"{size} · {updated}";
    }

    private static string FormatLogicBrowseFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{(bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} KB";
        }

        return $"{(bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture)} MB";
    }

    private static string SanitizeLogicBrowseToken(string? value, string fallback)
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
}
