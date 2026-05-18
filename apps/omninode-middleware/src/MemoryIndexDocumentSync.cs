using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed record MemoryIndexSyncSnapshot(
    string DbPath,
    int ScannedDocuments,
    int IndexedDocuments,
    int SkippedDocuments,
    int RemovedDocuments,
    bool FtsAvailable
);

public sealed class MemoryIndexDocumentSync
{
    private const string DefaultModelId = "fts-baseline";
    private const int DefaultChunkTokens = 400;
    private const int DefaultChunkOverlap = 80;
    private const long MaxProjectFileBytes = 768 * 1024;
    private const int SqliteCommandTimeoutMs = 20000;
    private static readonly string[] ProjectIndexRoots = { "docs", "apps", "scripts", "workspace" };
    private static readonly HashSet<string> ProjectTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".cs", ".js", ".mjs", ".json", ".html", ".css", ".py", ".c", ".h", ".sh", ".ps1", ".cmd", ".yml", ".yaml", ".toml"
    };
    private static readonly HashSet<string> ProjectExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".runtime", ".cache", "node_modules", "bin", "obj", "dist", "build", "target", "__pycache__"
    };
    private readonly string _dbPath;
    private readonly bool _ftsAvailable;
    private readonly string _memoryNotesRootDir;
    private readonly string _conversationStatePath;
    private readonly string _projectRootDir;

    public MemoryIndexDocumentSync(
        AppConfig config,
        MemoryIndexSchemaSnapshot schemaSnapshot
    )
    {
        _dbPath = schemaSnapshot.DbPath;
        _ftsAvailable = schemaSnapshot.FtsAvailable;
        _memoryNotesRootDir = Path.GetFullPath(config.MemoryNotesRootDir);
        _conversationStatePath = Path.GetFullPath(config.ConversationStatePath);
        _projectRootDir = ResolveProjectRoot(config);
    }

    public MemoryIndexSyncSnapshot SyncOnce()
    {
        var documents = LoadMemoryDocuments();
        var scanned = documents.Count;
        var indexed = 0;
        var skipped = 0;
        var removed = 0;

        var activeBySource = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["memory"] = new HashSet<string>(StringComparer.Ordinal),
            ["sessions"] = new HashSet<string>(StringComparer.Ordinal),
            ["project"] = new HashSet<string>(StringComparer.Ordinal)
        };

        foreach (var document in documents)
        {
            if (!activeBySource.TryGetValue(document.Source, out var activePaths))
            {
                activePaths = new HashSet<string>(StringComparer.Ordinal);
                activeBySource[document.Source] = activePaths;
            }
            activePaths.Add(document.Path);

            var existingHash = QuerySingleString(
                $"SELECT hash FROM files WHERE path = '{EscapeSql(document.Path)}' AND source = '{EscapeSql(document.Source)}' LIMIT 1;"
            );
            if (!string.IsNullOrWhiteSpace(existingHash)
                && existingHash.Equals(document.Hash, StringComparison.Ordinal))
            {
                skipped += 1;
                continue;
            }

            UpsertDocument(document);
            indexed += 1;
        }

        foreach (var (source, activePaths) in activeBySource)
        {
            var rows = QuerySingleColumnValues(
                $"SELECT path FROM files WHERE source = '{EscapeSql(source)}';"
            );
            foreach (var path in rows)
            {
                if (activePaths.Contains(path))
                {
                    continue;
                }

                DeleteDocument(path, source);
                removed += 1;
            }
        }

        var now = EscapeSql(DateTimeOffset.UtcNow.ToString("O"));
        RunSql(
            $"INSERT INTO meta (key, value) VALUES ('last_sync_utc', '{now}') " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value;"
        );

        return new MemoryIndexSyncSnapshot(
            _dbPath,
            scanned,
            indexed,
            skipped,
            removed,
            _ftsAvailable
        );
    }

    private List<MemorySourceDocument> LoadMemoryDocuments()
    {
        var items = new List<MemorySourceDocument>();
        items.AddRange(LoadMemoryNotes());
        items.AddRange(LoadConversationThreads());
        items.AddRange(LoadProjectDocuments());

        return items
            .OrderBy(x => x.Source, StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<MemorySourceDocument> LoadMemoryNotes()
    {
        if (!Directory.Exists(_memoryNotesRootDir))
        {
            return Array.Empty<MemorySourceDocument>();
        }

        var result = new List<MemorySourceDocument>();
        foreach (var filePath in Directory.EnumerateFiles(_memoryNotesRootDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(filePath);
            var name = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string content;
            FileInfo info;
            try
            {
                content = File.ReadAllText(fullPath);
                info = new FileInfo(fullPath);
            }
            catch
            {
                continue;
            }

            var relative = $"memory-notes/{name}".Replace('\\', '/');
            result.Add(new MemorySourceDocument(
                Source: "memory",
                Path: relative,
                Content: content,
                Hash: Sha256Hex(content),
                MtimeUnixMs: new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                SizeBytes: info.Length
            ));
        }

        return result;
    }

    private IReadOnlyList<MemorySourceDocument> LoadConversationThreads()
    {
        if (!File.Exists(_conversationStatePath))
        {
            return Array.Empty<MemorySourceDocument>();
        }

        ConversationState? state;
        DateTimeOffset stateMtimeUtc;
        try
        {
            var json = File.ReadAllText(_conversationStatePath);
            state = JsonSerializer.Deserialize(json, OmniJsonContext.Default.ConversationState);
            stateMtimeUtc = new FileInfo(_conversationStatePath).LastWriteTimeUtc;
        }
        catch
        {
            return Array.Empty<MemorySourceDocument>();
        }

        if (state?.Conversations == null || state.Conversations.Count == 0)
        {
            return Array.Empty<MemorySourceDocument>();
        }

        var result = new List<MemorySourceDocument>(state.Conversations.Count);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var thread in state.Conversations)
        {
            var path = BuildConversationPath(thread.Id, seenPaths);
            var content = BuildConversationContent(thread);
            var mtime = thread.UpdatedUtc == DateTimeOffset.MinValue
                ? stateMtimeUtc.ToUnixTimeMilliseconds()
                : thread.UpdatedUtc.ToUnixTimeMilliseconds();
            if (mtime <= 0)
            {
                mtime = stateMtimeUtc.ToUnixTimeMilliseconds();
            }

            result.Add(new MemorySourceDocument(
                Source: "sessions",
                Path: path,
                Content: content,
                Hash: Sha256Hex(content),
                MtimeUnixMs: mtime,
                SizeBytes: Encoding.UTF8.GetByteCount(content)
            ));
        }

        return result;
    }

    private IReadOnlyList<MemorySourceDocument> LoadProjectDocuments()
    {
        if (string.IsNullOrWhiteSpace(_projectRootDir) || !Directory.Exists(_projectRootDir))
        {
            return Array.Empty<MemorySourceDocument>();
        }

        var result = new List<MemorySourceDocument>();
        foreach (var rootName in ProjectIndexRoots)
        {
            var rootPath = Path.Combine(_projectRootDir, rootName);
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var filePath in EnumerateProjectFiles(rootPath))
            {
                string content;
                FileInfo info;
                try
                {
                    info = new FileInfo(filePath);
                    if (info.Length <= 0 || info.Length > MaxProjectFileBytes)
                    {
                        continue;
                    }

                    content = File.ReadAllText(filePath, Encoding.UTF8);
                    if (LooksBinary(content))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(_projectRootDir, filePath).Replace('\\', '/');
                result.Add(new MemorySourceDocument(
                    Source: "project",
                    Path: relativePath,
                    Content: content,
                    Hash: Sha256Hex(content),
                    MtimeUnixMs: new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                    SizeBytes: info.Length
                ));
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory);
            }
            catch
            {
                childDirectories = Array.Empty<string>();
            }

            foreach (var child in childDirectories)
            {
                var name = Path.GetFileName(child);
                if (ProjectExcludedDirectoryNames.Contains(name))
                {
                    continue;
                }

                pending.Push(child);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (!ProjectTextExtensions.Contains(extension))
                {
                    continue;
                }

                yield return Path.GetFullPath(file);
            }
        }
    }

    private static bool LooksBinary(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        var probeLength = Math.Min(content.Length, 4096);
        for (var i = 0; i < probeLength; i += 1)
        {
            if (content[i] == '\0')
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildConversationPath(string rawId, HashSet<string> seenPaths)
    {
        var safeId = SanitizePathToken(rawId, "thread");
        var basePath = $"conversations/{safeId}.md";
        if (seenPaths.Add(basePath))
        {
            return basePath;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = $"conversations/{safeId}_{suffix}.md";
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

    private void UpsertDocument(MemorySourceDocument document)
    {
        var chunks = ChunkText(document.Content, DefaultChunkTokens, DefaultChunkOverlap);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sql = new StringBuilder();
        sql.AppendLine("BEGIN;");
        sql.AppendLine($"DELETE FROM chunks WHERE path = '{EscapeSql(document.Path)}' AND source = '{EscapeSql(document.Source)}';");
        if (_ftsAvailable)
        {
            sql.AppendLine($"DELETE FROM chunks_fts WHERE path = '{EscapeSql(document.Path)}' AND source = '{EscapeSql(document.Source)}';");
        }

        foreach (var chunk in chunks)
        {
            var id = Sha256Hex($"{document.Source}:{document.Path}:{chunk.StartLine}:{chunk.EndLine}:{chunk.Hash}:{DefaultModelId}");
            sql.AppendLine(
                "INSERT INTO chunks (id, path, source, start_line, end_line, hash, model, text, embedding, updated_at) VALUES ("
                + $"'{EscapeSql(id)}', "
                + $"'{EscapeSql(document.Path)}', "
                + $"'{EscapeSql(document.Source)}', "
                + $"{chunk.StartLine}, "
                + $"{chunk.EndLine}, "
                + $"'{EscapeSql(chunk.Hash)}', "
                + $"'{EscapeSql(DefaultModelId)}', "
                + $"'{EscapeSql(chunk.Text)}', "
                + "'[]', "
                + $"{now}"
                + ");"
            );

            if (_ftsAvailable)
            {
                sql.AppendLine(
                    "INSERT INTO chunks_fts (text, id, path, source, model, start_line, end_line) VALUES ("
                    + $"'{EscapeSql(chunk.Text)}', "
                    + $"'{EscapeSql(id)}', "
                    + $"'{EscapeSql(document.Path)}', "
                    + $"'{EscapeSql(document.Source)}', "
                    + $"'{EscapeSql(DefaultModelId)}', "
                    + $"{chunk.StartLine}, "
                    + $"{chunk.EndLine}"
                    + ");"
                );
            }
        }

        sql.AppendLine(
            "INSERT INTO files (path, source, hash, mtime, size) VALUES ("
            + $"'{EscapeSql(document.Path)}', "
            + $"'{EscapeSql(document.Source)}', "
            + $"'{EscapeSql(document.Hash)}', "
            + $"{document.MtimeUnixMs}, "
            + $"{document.SizeBytes}"
            + ") "
            + "ON CONFLICT(path) DO UPDATE SET "
            + "source = excluded.source, "
            + "hash = excluded.hash, "
            + "mtime = excluded.mtime, "
            + "size = excluded.size;"
        );
        sql.AppendLine("COMMIT;");
        RunSql(sql.ToString());
    }

    private void DeleteDocument(string path, string source)
    {
        var sql = new StringBuilder();
        sql.AppendLine("BEGIN;");
        sql.AppendLine($"DELETE FROM files WHERE path = '{EscapeSql(path)}' AND source = '{EscapeSql(source)}';");
        sql.AppendLine($"DELETE FROM chunks WHERE path = '{EscapeSql(path)}' AND source = '{EscapeSql(source)}';");
        if (_ftsAvailable)
        {
            sql.AppendLine($"DELETE FROM chunks_fts WHERE path = '{EscapeSql(path)}' AND source = '{EscapeSql(source)}';");
        }
        sql.AppendLine("COMMIT;");
        RunSql(sql.ToString());
    }

    private static IReadOnlyList<MemoryChunkEntry> ChunkText(string content, int chunkTokens, int overlapTokens)
    {
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        if (lines.Length == 0)
        {
            return Array.Empty<MemoryChunkEntry>();
        }

        var maxChars = Math.Max(32, chunkTokens * 4);
        var overlapChars = Math.Max(0, overlapTokens * 4);
        var result = new List<MemoryChunkEntry>();
        var current = new List<(string Segment, int LineNo)>();
        var currentChars = 0;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            var startLine = current[0].LineNo;
            var endLine = current[^1].LineNo;
            var text = string.Join("\n", current.Select(x => x.Segment));
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            result.Add(new MemoryChunkEntry(
                StartLine: startLine,
                EndLine: endLine,
                Text: text,
                Hash: Sha256Hex(text)
            ));
        }

        void CarryOverlap()
        {
            if (overlapChars <= 0 || current.Count == 0)
            {
                current.Clear();
                currentChars = 0;
                return;
            }

            var kept = new List<(string Segment, int LineNo)>();
            var chars = 0;
            for (var i = current.Count - 1; i >= 0; i--)
            {
                var item = current[i];
                kept.Insert(0, item);
                chars += item.Segment.Length + 1;
                if (chars >= overlapChars)
                {
                    break;
                }
            }

            current = kept;
            currentChars = chars;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i] ?? string.Empty;
            var lineNo = i + 1;
            var segments = SplitLine(line, maxChars);

            foreach (var segment in segments)
            {
                var segmentChars = segment.Length + 1;
                if (current.Count > 0 && currentChars + segmentChars > maxChars)
                {
                    Flush();
                    CarryOverlap();
                }

                current.Add((segment, lineNo));
                currentChars += segmentChars;
            }
        }

        Flush();
        return result;
    }

    private static IReadOnlyList<string> SplitLine(string line, int maxChars)
    {
        if (line.Length == 0)
        {
            return new[] { string.Empty };
        }
        if (line.Length <= maxChars)
        {
            return new[] { line };
        }

        var segments = new List<string>((line.Length / maxChars) + 1);
        for (var start = 0; start < line.Length; start += maxChars)
        {
            var length = Math.Min(maxChars, line.Length - start);
            segments.Add(line.Substring(start, length));
        }

        return segments;
    }

    private string QuerySingleString(string sql)
    {
        var output = QuerySql(sql);
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private IReadOnlyList<string> QuerySingleColumnValues(string sql)
    {
        var output = QuerySql(sql);
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<string>();
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private void RunSql(string sql)
    {
        var result = ExecuteSqlite(sql);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"sqlite3 failed: {TrimForError(result.StdErr)}");
        }
    }

    private string QuerySql(string sql)
    {
        var result = ExecuteSqlite(sql);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"sqlite3 query failed: {TrimForError(result.StdErr)}");
        }

        return result.StdOut;
    }

    private SqliteProcessResult ExecuteSqlite(string sql)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sqlite3",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(_dbPath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("sqlite3 executable was not found.", ex);
        }

        process.StandardInput.Write(sql);
        process.StandardInput.Flush();
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(SqliteCommandTimeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException("sqlite3 command timed out.");
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return new SqliteProcessResult(
            process.ExitCode,
            stdoutTask.Result ?? string.Empty,
            stderrTask.Result ?? string.Empty
        );
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

    private static string ResolveProjectRoot(AppConfig config)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.DashboardIndexPath))
        {
            var current = new FileInfo(Path.GetFullPath(config.DashboardIndexPath)).Directory;
            while (current != null)
            {
                candidates.Add(current.FullName);
                current = current.Parent;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.WorkspaceRootDir))
        {
            var current = new DirectoryInfo(Path.GetFullPath(config.WorkspaceRootDir));
            while (current != null)
            {
                candidates.Add(current.FullName);
                current = current.Parent;
            }
        }

        candidates.Add(Environment.CurrentDirectory);
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(Path.Combine(candidate, "apps"))
                    && Directory.Exists(Path.Combine(candidate, "docs")))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
            }
        }

        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    private static string EscapeSql(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string TrimForError(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length <= 400)
        {
            return text;
        }

        return text[..400] + "...";
    }

    private sealed record SqliteProcessResult(
        int ExitCode,
        string StdOut,
        string StdErr
    );

    private sealed record MemorySourceDocument(
        string Source,
        string Path,
        string Content,
        string Hash,
        long MtimeUnixMs,
        long SizeBytes
    );

    private sealed record MemoryChunkEntry(
        int StartLine,
        int EndLine,
        string Text,
        string Hash
    );
}
