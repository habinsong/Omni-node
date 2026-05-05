using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed record MemorySearchCitationResult(
    string Path,
    int StartLine,
    int EndLine,
    string Snippet,
    double Score,
    string Source
);

public sealed record MemorySearchToolResult(
    IReadOnlyList<MemorySearchCitationResult> Results,
    bool Disabled,
    string? Error
);

public sealed class MemorySearchTool
{
    private const string DefaultIndexFileName = "main.sqlite";
    private const int DefaultMaxResults = 6;
    private const int MaxAllowedResults = 24;
    private const int CandidateMultiplier = 4;
    private const int MaxCandidateResults = 200;
    private const double DefaultMinScore = 0.35d;
    private const int SnippetMaxChars = 1200;
    private const int SqliteCommandTimeoutMs = 15000;
    private static readonly Regex FtsTokenRegex = new("[\\p{L}\\p{N}_]+", RegexOptions.Compiled);
    private static readonly HashSet<string> FtsStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "to", "for", "of", "in", "on", "at", "is", "are", "was", "were",
        "be", "been", "being", "about", "this", "that", "these", "those", "what", "which", "who", "how",
        "why", "when", "where", "please", "tell", "show", "give", "get", "find", "latest", "recent",
        "today", "yesterday", "now", "current", "update", "updates", "news",
        "이거", "그거", "저거", "그리고", "또는", "은", "는", "이", "가", "을", "를", "의", "에", "에서",
        "으로", "로", "와", "과", "도", "만", "좀", "해줘", "해주세요", "알려줘", "알려주세요",
        "정리해줘", "정리해주세요", "요약해줘", "요약해주세요", "최신", "최근", "오늘", "어제", "지금",
        "방금", "현재", "업데이트", "뉴스"
    };
    private readonly string _dbPath;

    public MemorySearchTool(AppConfig config)
    {
        var stateRoot = ResolveStateRoot(config.ConversationStatePath);
        _dbPath = Path.Combine(stateRoot, "memory-index", DefaultIndexFileName);
    }

    public MemorySearchToolResult Search(string query, int? maxResults = null, double? minScore = null)
    {
        var cleaned = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return new MemorySearchToolResult(Array.Empty<MemorySearchCitationResult>(), false, null);
        }

        if (!File.Exists(_dbPath))
        {
            return new MemorySearchToolResult(Array.Empty<MemorySearchCitationResult>(), true, "memory index db not found");
        }

        bool ftsReady;
        try
        {
            ftsReady = HasFtsTable();
        }
        catch (Exception ex)
        {
            return new MemorySearchToolResult(Array.Empty<MemorySearchCitationResult>(), true, ex.Message);
        }

        if (!ftsReady)
        {
            return new MemorySearchToolResult(Array.Empty<MemorySearchCitationResult>(), true, "memory search fts table unavailable");
        }

        var strictFtsQuery = BuildFtsQuery(cleaned);
        if (string.IsNullOrWhiteSpace(strictFtsQuery))
        {
            return new MemorySearchToolResult(Array.Empty<MemorySearchCitationResult>(), false, null);
        }

        var resolvedMaxResults = NormalizeMaxResults(maxResults);
        var resolvedMinScore = NormalizeMinScore(minScore);
        var candidateLimit = Math.Min(
            MaxCandidateResults,
            Math.Max(resolvedMaxResults, resolvedMaxResults * CandidateMultiplier)
        );

        var parsedRows = new List<SearchRow>(candidateLimit);
        try
        {
            parsedRows.AddRange(QueryRows(strictFtsQuery, candidateLimit));

            // Strict AND 결과가 0건일 때만 OR 확장을 사용한다.
            // 일부 strict hit가 존재하는 상황에서 OR 확장을 섞으면 무관 snippet이 상위로 유입될 수 있다.
            if (parsedRows.Count == 0)
            {
                var relaxedFtsQuery = BuildRelaxedFtsQuery(cleaned);
                if (!string.IsNullOrWhiteSpace(relaxedFtsQuery)
                    && !string.Equals(relaxedFtsQuery, strictFtsQuery, StringComparison.Ordinal))
                {
                    parsedRows.AddRange(QueryRows(relaxedFtsQuery, candidateLimit));
                }
            }
        }
        catch (Exception ex)
        {
            return new MemorySearchToolResult(Array.Empty<MemorySearchCitationResult>(), true, ex.Message);
        }
        var deduped = new Dictionary<string, MemorySearchCitationResult>(StringComparer.Ordinal);

        foreach (var row in parsedRows)
        {
            if (string.IsNullOrWhiteSpace(row.Path))
            {
                continue;
            }

            var score = Bm25RankToScore(row.Rank);
            if (score < resolvedMinScore)
            {
                continue;
            }

            var startLine = Math.Max(1, row.StartLine);
            var endLine = Math.Max(startLine, row.EndLine);
            var item = new MemorySearchCitationResult(
                Path: row.Path,
                StartLine: startLine,
                EndLine: endLine,
                Snippet: NormalizeSnippet(row.Snippet),
                Score: score,
                Source: NormalizeSource(row.Source)
            );

            var key = $"{item.Source}:{item.Path}:{item.StartLine}:{item.EndLine}";
            if (!deduped.TryGetValue(key, out var current) || item.Score > current.Score)
            {
                deduped[key] = item;
            }
        }

        var results = deduped.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.StartLine)
            .Take(resolvedMaxResults)
            .ToArray();

        return new MemorySearchToolResult(results, false, null);
    }

    private bool HasFtsTable()
    {
        var output = QuerySingleValue(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'chunks_fts';"
        );
        return output.Trim().StartsWith("1", StringComparison.Ordinal);
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

    private static int NormalizeMaxResults(int? value)
    {
        if (!value.HasValue)
        {
            return DefaultMaxResults;
        }

        return Math.Clamp(value.Value, 1, MaxAllowedResults);
    }

    private static double NormalizeMinScore(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return DefaultMinScore;
        }

        return Math.Clamp(value.Value, 0d, 1d);
    }

    private static string? BuildFtsQuery(string raw)
    {
        var tokens = ExtractTokens(raw);
        if (tokens.Length == 0)
        {
            return null;
        }

        var keywords = tokens
            .Where(IsRelaxedKeyword)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        if (keywords.Length == 0)
        {
            keywords = tokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToArray();
        }

        return string.Join(" AND ", keywords.Select(QuoteToken));
    }

    private static string? BuildRelaxedFtsQuery(string raw)
    {
        var tokens = ExtractTokens(raw);
        if (tokens.Length == 0)
        {
            return null;
        }

        var keywords = tokens
            .Where(IsRelaxedKeyword)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        if (keywords.Length == 0)
        {
            keywords = tokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
        }

        if (keywords.Length == 0)
        {
            return null;
        }

        return string.Join(" OR ", keywords.Select(QuoteToken));
    }

    private static string[] ExtractTokens(string raw)
    {
        return FtsTokenRegex
            .Matches(raw)
            .Select(match => match.Value.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Take(24)
            .ToArray();
    }

    private static bool IsRelaxedKeyword(string token)
    {
        var normalized = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (FtsStopWords.Contains(normalized))
        {
            return false;
        }

        if (normalized.Length >= 2)
        {
            return true;
        }

        return normalized.Any(char.IsDigit);
    }

    private static string QuoteToken(string token)
    {
        return $"\"{token.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
    }

    private static string BuildSearchSql(string ftsQuery, int candidateLimit)
    {
        return
            "WITH ranked AS ("
            + " SELECT path, source, start_line, end_line, text, bm25(chunks_fts) AS rank"
            + " FROM chunks_fts"
            + $" WHERE chunks_fts MATCH '{EscapeSql(ftsQuery)}'"
            + " ORDER BY rank ASC"
            + $" LIMIT {candidateLimit}"
            + ")"
            + " SELECT COALESCE(json_group_array(json_object("
            + " 'path', path,"
            + " 'source', source,"
            + " 'startLine', start_line,"
            + " 'endLine', end_line,"
            + " 'snippet', text,"
            + " 'rank', rank"
            + " )), '[]')"
            + " FROM ranked;";
    }

    private IReadOnlyList<SearchRow> QueryRows(string ftsQuery, int candidateLimit)
    {
        var jsonRows = QuerySingleValue(BuildSearchSql(ftsQuery, candidateLimit));
        return ParseRows(jsonRows);
    }

    private static IReadOnlyList<SearchRow> ParseRows(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SearchRow>();
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SearchRow>();
        }

        var rows = new List<SearchRow>(document.RootElement.GetArrayLength());
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var path = GetString(item, "path");
            var source = GetString(item, "source");
            var snippet = GetString(item, "snippet");
            var startLine = GetInt(item, "startLine");
            var endLine = GetInt(item, "endLine");
            var rank = GetDouble(item, "rank");

            rows.Add(new SearchRow(path, source, snippet, startLine, endLine, rank));
        }

        return rows;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0d;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var parsed
            ))
        {
            return parsed;
        }

        return 0d;
    }

    private static string NormalizeSnippet(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length <= SnippetMaxChars)
        {
            return normalized;
        }

        return normalized[..SnippetMaxChars] + "...";
    }

    private static string NormalizeSource(string source)
    {
        var normalized = (source ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "memory" => "memory",
            "sessions" => "sessions",
            _ => "memory"
        };
    }

    private static double Bm25RankToScore(double rank)
    {
        var normalized = double.IsFinite(rank) ? Math.Max(0d, rank) : 999d;
        return 1d / (1d + normalized);
    }

    private string QuerySingleValue(string sql)
    {
        var output = QuerySql(sql);
        return (output ?? string.Empty).Trim();
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

    private static string EscapeSql(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
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

    private sealed record SearchRow(
        string Path,
        string Source,
        string Snippet,
        int StartLine,
        int EndLine,
        double Rank
    );

    private sealed record SqliteProcessResult(
        int ExitCode,
        string StdOut,
        string StdErr
    );
}
