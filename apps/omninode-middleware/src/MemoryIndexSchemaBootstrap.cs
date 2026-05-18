using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OmniNode.Middleware;

public sealed record MemoryIndexSchemaSnapshot(
    string DbPath,
    string CacheDirPath,
    bool FtsAvailable,
    string? FtsError
);

public sealed class MemoryIndexSchemaBootstrap
{
    private const string DefaultIndexFileName = "main.sqlite";
    private const int SqliteCommandTimeoutMs = 15000;
    private readonly string _dbPath;
    private readonly string _cacheDirPath;

    public MemoryIndexSchemaBootstrap(PathOptions paths)
    {
        var stateRoot = ResolveStateRoot(paths.ConversationStatePath);
        var indexRoot = Path.Combine(stateRoot, "memory-index");
        _dbPath = Path.Combine(indexRoot, DefaultIndexFileName);
        _cacheDirPath = Path.Combine(indexRoot, "cache");
    }

    public MemoryIndexSchemaSnapshot EnsureInitialized()
    {
        var dbDirectory = Path.GetDirectoryName(_dbPath);
        if (string.IsNullOrWhiteSpace(dbDirectory))
        {
            throw new InvalidOperationException("invalid memory index db path");
        }

        Directory.CreateDirectory(dbDirectory);
        Directory.CreateDirectory(_cacheDirPath);

        RunSql(_dbPath, BuildCoreSchemaScript());
        EnsureColumnExists("files", "source", "TEXT NOT NULL DEFAULT 'memory'");
        EnsureColumnExists("chunks", "source", "TEXT NOT NULL DEFAULT 'memory'");

        var ftsAvailable = false;
        string? ftsError = null;
        try
        {
            RunSql(_dbPath, BuildFtsSchemaScript());
            ftsAvailable = true;
        }
        catch (Exception ex)
        {
            ftsAvailable = false;
            ftsError = ex.Message;
        }

        return new MemoryIndexSchemaSnapshot(
            _dbPath,
            _cacheDirPath,
            ftsAvailable,
            ftsError
        );
    }

    private void EnsureColumnExists(string table, string column, string definition)
    {
        var pragmaOutput = QuerySql(_dbPath, $"PRAGMA table_info({table});");
        var hasColumn = pragmaOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', StringSplitOptions.None))
            .Any(parts => parts.Length >= 2 && parts[1].Equals(column, StringComparison.OrdinalIgnoreCase));

        if (!hasColumn)
        {
            RunSql(_dbPath, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        }
    }

    private static string BuildCoreSchemaScript()
    {
        var now = EscapeSql(DateTimeOffset.UtcNow.ToString("O"));
        var builder = new StringBuilder();
        builder.AppendLine("PRAGMA journal_mode=WAL;");
        builder.AppendLine("CREATE TABLE IF NOT EXISTS meta (");
        builder.AppendLine("  key TEXT PRIMARY KEY,");
        builder.AppendLine("  value TEXT NOT NULL");
        builder.AppendLine(");");
        builder.AppendLine("CREATE TABLE IF NOT EXISTS files (");
        builder.AppendLine("  path TEXT PRIMARY KEY,");
        builder.AppendLine("  source TEXT NOT NULL DEFAULT 'memory',");
        builder.AppendLine("  hash TEXT NOT NULL,");
        builder.AppendLine("  mtime INTEGER NOT NULL,");
        builder.AppendLine("  size INTEGER NOT NULL");
        builder.AppendLine(");");
        builder.AppendLine("CREATE TABLE IF NOT EXISTS chunks (");
        builder.AppendLine("  id TEXT PRIMARY KEY,");
        builder.AppendLine("  path TEXT NOT NULL,");
        builder.AppendLine("  source TEXT NOT NULL DEFAULT 'memory',");
        builder.AppendLine("  start_line INTEGER NOT NULL,");
        builder.AppendLine("  end_line INTEGER NOT NULL,");
        builder.AppendLine("  hash TEXT NOT NULL,");
        builder.AppendLine("  model TEXT NOT NULL,");
        builder.AppendLine("  text TEXT NOT NULL,");
        builder.AppendLine("  embedding TEXT NOT NULL,");
        builder.AppendLine("  updated_at INTEGER NOT NULL");
        builder.AppendLine(");");
        builder.AppendLine("CREATE TABLE IF NOT EXISTS embedding_cache (");
        builder.AppendLine("  provider TEXT NOT NULL,");
        builder.AppendLine("  model TEXT NOT NULL,");
        builder.AppendLine("  provider_key TEXT NOT NULL,");
        builder.AppendLine("  hash TEXT NOT NULL,");
        builder.AppendLine("  embedding TEXT NOT NULL,");
        builder.AppendLine("  dims INTEGER,");
        builder.AppendLine("  updated_at INTEGER NOT NULL,");
        builder.AppendLine("  PRIMARY KEY (provider, model, provider_key, hash)");
        builder.AppendLine(");");
        builder.AppendLine("CREATE INDEX IF NOT EXISTS idx_embedding_cache_updated_at ON embedding_cache(updated_at);");
        builder.AppendLine("CREATE INDEX IF NOT EXISTS idx_chunks_path ON chunks(path);");
        builder.AppendLine("CREATE INDEX IF NOT EXISTS idx_chunks_source ON chunks(source);");
        builder.AppendLine("INSERT INTO meta (key, value) VALUES ('schema_version', '1')");
        builder.AppendLine("ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
        builder.AppendLine($"INSERT INTO meta (key, value) VALUES ('last_bootstrap_utc', '{now}')");
        builder.AppendLine("ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
        return builder.ToString();
    }

    private static string BuildFtsSchemaScript()
    {
        return """
               CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
                 text,
                 id UNINDEXED,
                 path UNINDEXED,
                 source UNINDEXED,
                 model UNINDEXED,
                 start_line UNINDEXED,
                 end_line UNINDEXED
               );
               """;
    }

    private static string ResolveStateRoot(string conversationStatePath)
    {
        var configured = Path.GetDirectoryName(conversationStatePath);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            return Path.Combine(home, ".omninode");
        }

        return Path.GetTempPath();
    }

    private static string EscapeSql(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static void RunSql(string dbPath, string sql)
    {
        var result = ExecuteSqlite(dbPath, sql);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"sqlite3 failed: {TrimForError(result.StdErr)}");
        }
    }

    private static string QuerySql(string dbPath, string sql)
    {
        var result = ExecuteSqlite(dbPath, sql);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"sqlite3 query failed: {TrimForError(result.StdErr)}");
        }

        return result.StdOut;
    }

    private static SqliteProcessResult ExecuteSqlite(string dbPath, string sql)
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
        startInfo.ArgumentList.Add(dbPath);

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
}
