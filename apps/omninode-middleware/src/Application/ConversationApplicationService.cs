using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed class ConversationApplicationService : IConversationApplicationService
{
    private const int ConversationSearchDefaultMaxResults = 12;
    private const int ConversationSearchMaxResults = 40;

    private readonly IConversationStore _conversationStore;
    private readonly IMemoryNoteStore _memoryNoteStore;
    private readonly AuditLogger _auditLogger;
    private readonly AppConfig _config;
    private readonly MemorySearchTool _memorySearchTool;

    private readonly object _indexSyncLock = new();
    private DateTimeOffset _lastIndexSyncUtc = DateTimeOffset.MinValue;
    private readonly TimeSpan _indexSyncThrottle = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, BackupImportPackage> _backupImportPreviews
        = new(StringComparer.Ordinal);

    // ClearActiveSkill은 CommandService의 _activeSkillByThread private state에
    // 결합되어 있어 façade로 옮기지 못함. CommandService 생성 후
    // ConfigureClearActiveSkillDelegate로 위임 함수를 주입.
    private Func<string?, bool>? _clearActiveSkill;

    public ConversationApplicationService(
        IConversationStore conversationStore,
        IMemoryNoteStore memoryNoteStore,
        AuditLogger auditLogger,
        AppConfig config,
        MemorySearchTool memorySearchTool
    )
    {
        _conversationStore = conversationStore;
        _memoryNoteStore = memoryNoteStore;
        _auditLogger = auditLogger;
        _config = config;
        _memorySearchTool = memorySearchTool;
    }

    public void ConfigureClearActiveSkillDelegate(Func<string?, bool> @delegate)
    {
        _clearActiveSkill = @delegate;
    }

    public IReadOnlyList<ConversationThreadSummary> ListConversations(string scope, string mode)
        => _conversationStore.List(scope, mode);

    public ConversationThreadView CreateConversation(
        string scope,
        string mode,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    ) => _conversationStore.Create(scope, mode, title, project, category, tags);

    public ConversationThreadView? GetConversation(string conversationId)
        => _conversationStore.Get(conversationId);

    public bool DeleteConversation(string conversationId)
    {
        var targetConversationId = (conversationId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetConversationId))
        {
            return false;
        }

        var thread = _conversationStore.Get(targetConversationId);
        if (thread == null)
        {
            return false;
        }

        var linkedNotes = thread.LinkedMemoryNotes
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var deleted = _conversationStore.Delete(targetConversationId);
        if (!deleted)
        {
            return false;
        }

        _auditLogger.Log(
            "web",
            "delete_conversation",
            "ok",
            $"conversationId={NormalizeAuditToken(targetConversationId, "-")} linkedNotes={linkedNotes.Length} removedNotes=0"
        );
        return true;
    }

    public ConversationThreadView UpdateConversationMetadata(
        string conversationId,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    )
    {
        var targetConversationId = (conversationId ?? string.Empty).Trim();
        var before = _conversationStore.Get(targetConversationId)
            ?? throw new InvalidOperationException("conversation not found");
        var updated = _conversationStore.UpdateMetadata(
            targetConversationId,
            title,
            project,
            category,
            tags
        );

        var titleChanged = !string.Equals(before.Title, updated.Title, StringComparison.Ordinal);
        if (!titleChanged || before.LinkedMemoryNotes.Count == 0)
        {
            return updated;
        }

        var renamedNotes = before.LinkedMemoryNotes
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name =>
            {
                var original = name.Trim();
                var renamed = _memoryNoteStore.RenameForConversationTitle(original, updated.Title);
                return string.IsNullOrWhiteSpace(renamed) ? original : renamed;
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return _conversationStore.SetLinkedMemoryNotes(updated.Id, renamedNotes);
    }

    public WorkspaceFilePreview? ReadWorkspaceFile(string filePath, int maxChars = 120_000)
        => ReadWorkspaceFile(filePath, null, maxChars);

    public WorkspaceFilePreview? ReadWorkspaceFile(string filePath, string? conversationId, int maxChars = 120_000)
    {
        var raw = (filePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var fullPath = TryResolveWorkspacePreviewPath(raw, conversationId);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                return null;
            }

            var content = File.ReadAllText(fullPath);
            if (content.Length > maxChars)
            {
                content = content[..maxChars] + "\n...(truncated)";
            }

            return new WorkspaceFilePreview(fullPath, content);
        }
        catch
        {
            return null;
        }
    }

    public ConversationSearchResult SearchConversations(string query, int? maxResults = null)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new ConversationSearchResult(normalizedQuery, Array.Empty<ConversationSearchHit>(), false);
        }

        TrySyncConversationIndexThrottled();
        var resolvedMax = Math.Clamp(maxResults ?? ConversationSearchDefaultMaxResults, 1, ConversationSearchMaxResults);
        var memoryResult = _memorySearchTool.Search(normalizedQuery, Math.Min(ConversationSearchMaxResults, resolvedMax * 3), 0.05d);
        var hits = BuildConversationSearchHits(normalizedQuery, memoryResult, resolvedMax);
        if (hits.Count == 0)
        {
            hits = BuildInMemoryConversationSearchHits(normalizedQuery, resolvedMax);
        }

        return new ConversationSearchResult(
            normalizedQuery,
            hits,
            memoryResult.Disabled,
            memoryResult.Error
        );
    }

    public BackupExportResult ExportBackup()
    {
        var included = new List<string>();
        var excluded = new List<string>
        {
            "API 키",
            "Telegram token/chat id",
            "auth session",
            "OTP",
            "lock/cache/runtime 임시 파일",
            "runtime logs",
            "outbox"
        };

        try
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddFileToBackup(archive, _config.ConversationStatePath, "conversations.json", included);
                AddFileToBackup(archive, _config.RoutineStatePath, "routines.json", included);
                AddFileToBackup(archive, ResolveStateFilePath("routing-policy.json"), "routing-policy.json", included);
                AddDirectoryToBackup(archive, _config.MemoryNotesRootDir, "memory-notes", included);
                AddDirectoryToBackup(archive, ResolveStateDirectoryPath("plans"), "plans", included);
                AddDirectoryToBackup(archive, ResolveStateDirectoryPath("tasks"), "tasks", included);
                AddDirectoryToBackup(archive, ResolveStateDirectoryPath("notebooks"), "notebooks", included);
                AddDirectoryToBackup(archive, ResolveStateDirectoryPath("skills"), "skills/global", included);
                AddDirectoryToBackup(archive, ResolveStateDirectoryPath("commands"), "commands/global", included);
                AddDirectoryToBackup(archive, Path.Combine(_config.WorkspaceRootDir, ".omni", "skills"), "skills/project", included);
                AddDirectoryToBackup(archive, Path.Combine(_config.WorkspaceRootDir, ".omni", "commands"), "commands/project", included);
            }

            var bytes = memory.ToArray();
            var fileName = $"omninode-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
            return new BackupExportResult(
                true,
                fileName,
                Convert.ToBase64String(bytes),
                bytes.Length,
                included,
                excluded
            );
        }
        catch (Exception ex)
        {
            return new BackupExportResult(false, string.Empty, string.Empty, 0, included, excluded, ex.Message);
        }
    }

    public BackupImportPreviewResult PreviewBackupImport(string fileName, string contentBase64)
    {
        try
        {
            var bytes = Convert.FromBase64String((contentBase64 ?? string.Empty).Trim());
            var conversations = ReadBackupConversations(bytes);
            var existingIds = _conversationStore.ListAll()
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var conflicts = conversations
                .Where(item => existingIds.Contains(item.Id))
                .Select(item => item.Id)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            var fileCount = CountBackupFiles(bytes);
            var previewId = Guid.NewGuid().ToString("N");
            _backupImportPreviews[previewId] = new BackupImportPackage(
                bytes,
                conversations,
                DateTimeOffset.UtcNow
            );

            CleanupExpiredBackupPreviews();
            return new BackupImportPreviewResult(
                true,
                previewId,
                string.IsNullOrWhiteSpace(fileName) ? "backup.zip" : Path.GetFileName(fileName.Trim()),
                conversations.Count,
                conflicts.Length,
                fileCount,
                conflicts
            );
        }
        catch (Exception ex)
        {
            return new BackupImportPreviewResult(false, string.Empty, fileName ?? string.Empty, 0, 0, 0, Array.Empty<string>(), ex.Message);
        }
    }

    public BackupImportApplyResult ApplyBackupImport(string previewId, bool overwrite)
    {
        var normalizedPreviewId = (previewId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPreviewId)
            || !_backupImportPreviews.TryGetValue(normalizedPreviewId, out var package))
        {
            return new BackupImportApplyResult(false, 0, 0, 0, 0, 0, "previewId를 찾을 수 없습니다.");
        }

        try
        {
            var conversationResult = _conversationStore.ImportConversations(package.Conversations, overwrite);
            var fileResult = ImportBackupFiles(package.ZipBytes, overwrite);
            _backupImportPreviews.TryRemove(normalizedPreviewId, out _);
            return new BackupImportApplyResult(
                true,
                conversationResult.Imported,
                conversationResult.Skipped,
                conversationResult.Overwritten,
                fileResult.Imported,
                fileResult.Skipped
            );
        }
        catch (Exception ex)
        {
            return new BackupImportApplyResult(false, 0, 0, 0, 0, 0, ex.Message);
        }
    }

    public bool ClearActiveSkill(string? conversationId)
    {
        if (_clearActiveSkill == null)
        {
            throw new InvalidOperationException(
                "ConversationApplicationService.ClearActiveSkill delegate가 주입되지 않았습니다. " +
                "Program.cs에서 ConfigureClearActiveSkillDelegate를 호출하세요."
            );
        }

        return _clearActiveSkill(conversationId);
    }

    // --- private helpers ---

    private string? TryResolveWorkspacePreviewPath(string rawPath, string? conversationId)
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        var normalizedRawPath = (rawPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRawPath))
        {
            return null;
        }

        if (TryResolveDirectWorkspacePreviewPath(workspaceRoot, normalizedRawPath, out var directPath))
        {
            return directPath;
        }

        var normalizedConversationId = (conversationId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedConversationId))
        {
            return null;
        }

        var conversation = _conversationStore.Get(normalizedConversationId);
        var latest = conversation?.LatestCodingResult;
        if (latest == null)
        {
            return null;
        }

        return TryResolveConversationPreviewPath(normalizedRawPath, latest, workspaceRoot);
    }

    private static bool TryResolveDirectWorkspacePreviewPath(string workspaceRoot, string rawPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        try
        {
            var candidate = Path.IsPathRooted(rawPath)
                ? Path.GetFullPath(rawPath)
                : Path.GetFullPath(Path.Combine(workspaceRoot, rawPath));
            if (!IsPathUnderRoot(candidate, workspaceRoot) || !File.Exists(candidate))
            {
                return false;
            }

            resolvedPath = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryResolveConversationPreviewPath(
        string rawPath,
        ConversationCodingResultSnapshot latest,
        string workspaceRoot
    )
    {
        var previewRoots = BuildConversationPreviewRoots(latest, workspaceRoot);
        var previewFiles = BuildConversationPreviewFiles(latest)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (Path.IsPathRooted(rawPath))
        {
            try
            {
                var fullRequested = Path.GetFullPath(rawPath);
                if (File.Exists(fullRequested)
                    && previewRoots.Any(root => IsPathUnderRoot(fullRequested, root)))
                {
                    return fullRequested;
                }
            }
            catch
            {
            }
        }

        var normalizedRequested = NormalizePreviewPath(rawPath);
        if (!string.IsNullOrWhiteSpace(normalizedRequested))
        {
            var suffixMatches = previewFiles
                .Where(File.Exists)
                .Where(path => PathMatchesPreviewRequest(path, normalizedRequested))
                .ToArray();
            if (suffixMatches.Length == 1)
            {
                return suffixMatches[0];
            }

            if (suffixMatches.Length > 1)
            {
                var exactRunDirMatch = suffixMatches.FirstOrDefault(path =>
                    string.Equals(
                        NormalizePreviewPath(path),
                        normalizedRequested,
                        StringComparison.OrdinalIgnoreCase
                    ));
                if (!string.IsNullOrWhiteSpace(exactRunDirMatch))
                {
                    return exactRunDirMatch;
                }
            }
        }

        if (!Path.IsPathRooted(rawPath))
        {
            foreach (var root in previewRoots)
            {
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(root, rawPath));
                    if (File.Exists(candidate) && IsPathUnderRoot(candidate, root))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }
        }

        var basename = Path.GetFileName(rawPath.Trim());
        if (!string.IsNullOrWhiteSpace(basename))
        {
            var basenameMatches = previewFiles
                .Where(File.Exists)
                .Where(path => string.Equals(Path.GetFileName(path), basename, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (basenameMatches.Length == 1)
            {
                return basenameMatches[0];
            }
        }

        return null;
    }

    private static string[] BuildConversationPreviewRoots(
        ConversationCodingResultSnapshot latest,
        string workspaceRoot
    )
    {
        return EnumerateConversationPreviewRoots(latest, workspaceRoot)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateConversationPreviewRoots(
        ConversationCodingResultSnapshot latest,
        string workspaceRoot
    )
    {
        yield return workspaceRoot;

        var latestRunDirectory = (latest.Execution.RunDirectory ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(latestRunDirectory))
        {
            yield return latestRunDirectory;
        }

        foreach (var worker in latest.Workers ?? Array.Empty<CodingWorkerResultSnapshot>())
        {
            var workerRunDirectory = (worker.Execution.RunDirectory ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(workerRunDirectory))
            {
                yield return workerRunDirectory;
            }
        }
    }

    private static string[] BuildConversationPreviewFiles(ConversationCodingResultSnapshot latest)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddConversationPreviewFileCandidates(files, latest.Execution, latest.ChangedFiles);
        foreach (var worker in latest.Workers ?? Array.Empty<CodingWorkerResultSnapshot>())
        {
            AddConversationPreviewFileCandidates(files, worker.Execution, worker.ChangedFiles);
        }

        return files.ToArray();
    }

    private static void AddConversationPreviewFileCandidates(
        ISet<string> files,
        CodeExecutionResult execution,
        IReadOnlyList<string> changedFiles
    )
    {
        foreach (var path in changedFiles ?? Array.Empty<string>())
        {
            if (TryNormalizeFullPreviewCandidate(path, out var fullPath))
            {
                files.Add(fullPath);
            }
        }

        var runDirectory = (execution.RunDirectory ?? string.Empty).Trim();
        var entryFile = (execution.EntryFile ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(runDirectory) || string.IsNullOrWhiteSpace(entryFile))
        {
            return;
        }

        try
        {
            var fullPath = Path.IsPathRooted(entryFile)
                ? Path.GetFullPath(entryFile)
                : Path.GetFullPath(Path.Combine(runDirectory, entryFile));
            files.Add(fullPath);
        }
        catch
        {
        }
    }

    private static bool TryNormalizeFullPreviewCandidate(string path, out string fullPath)
    {
        fullPath = string.Empty;
        var normalized = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool PathMatchesPreviewRequest(string candidatePath, string normalizedRequested)
    {
        var normalizedCandidate = NormalizePreviewPath(candidatePath);
        if (string.Equals(normalizedCandidate, normalizedRequested, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.EndsWith("/" + normalizedRequested, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePreviewPath(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .Trim('/');
    }

    private IReadOnlyList<ConversationSearchHit> BuildConversationSearchHits(
        string query,
        MemorySearchToolResult memoryResult,
        int maxResults
    )
    {
        if (memoryResult.Results.Count == 0)
        {
            return Array.Empty<ConversationSearchHit>();
        }

        var allConversations = _conversationStore.ListAllViews();
        var hits = new List<ConversationSearchHit>();
        foreach (var item in memoryResult.Results)
        {
            if (!item.Source.Equals("sessions", StringComparison.OrdinalIgnoreCase)
                || !item.Path.StartsWith("conversations/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var conversation = allConversations.FirstOrDefault(candidate =>
                item.Path.Contains(candidate.Id, StringComparison.OrdinalIgnoreCase));
            if (conversation == null)
            {
                continue;
            }

            var message = FindBestConversationMessage(conversation, query, item.Snippet);
            hits.Add(new ConversationSearchHit(
                conversation.Id,
                conversation.Title,
                conversation.Scope,
                conversation.Mode,
                message?.Role ?? "-",
                NormalizeConversationSearchSnippet(item.Snippet),
                conversation.UpdatedUtc,
                message?.CreatedUtc ?? conversation.UpdatedUtc,
                item.Score
            ));
        }

        return hits
            .GroupBy(item => $"{item.ConversationId}:{item.Snippet}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.UpdatedUtc)
            .Take(maxResults)
            .ToArray();
    }

    private IReadOnlyList<ConversationSearchHit> BuildInMemoryConversationSearchHits(string query, int maxResults)
    {
        var normalizedQuery = query.Trim();
        var loweredQuery = normalizedQuery.ToLowerInvariant();
        var hits = new List<ConversationSearchHit>();
        foreach (var conversation in _conversationStore.ListAllViews())
        {
            foreach (var message in conversation.Messages.OrderByDescending(item => item.CreatedUtc))
            {
                var text = message.Text ?? string.Empty;
                var index = text.ToLowerInvariant().IndexOf(loweredQuery, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                hits.Add(new ConversationSearchHit(
                    conversation.Id,
                    conversation.Title,
                    conversation.Scope,
                    conversation.Mode,
                    message.Role,
                    BuildInlineSnippet(text, index, normalizedQuery.Length),
                    conversation.UpdatedUtc,
                    message.CreatedUtc,
                    0.5d
                ));
                break;
            }
        }

        return hits
            .OrderByDescending(item => item.MessageUtc)
            .Take(maxResults)
            .ToArray();
    }

    private static ConversationMessageView? FindBestConversationMessage(
        ConversationThreadView conversation,
        string query,
        string snippet
    )
    {
        var loweredQuery = (query ?? string.Empty).Trim().ToLowerInvariant();
        var loweredSnippet = (snippet ?? string.Empty).Trim().ToLowerInvariant();
        return conversation.Messages
            .OrderByDescending(message => message.CreatedUtc)
            .FirstOrDefault(message =>
            {
                var text = (message.Text ?? string.Empty).ToLowerInvariant();
                return (!string.IsNullOrWhiteSpace(loweredQuery) && text.Contains(loweredQuery, StringComparison.Ordinal))
                       || (!string.IsNullOrWhiteSpace(loweredSnippet) && text.Contains(loweredSnippet, StringComparison.Ordinal));
            })
            ?? conversation.Messages.OrderByDescending(message => message.CreatedUtc).FirstOrDefault();
    }

    private void TrySyncConversationIndexThrottled()
    {
        lock (_indexSyncLock)
        {
            if (DateTimeOffset.UtcNow - _lastIndexSyncUtc < _indexSyncThrottle)
            {
                return;
            }

            _lastIndexSyncUtc = DateTimeOffset.UtcNow;
        }

        try
        {
            var schema = new MemoryIndexSchemaBootstrap(_config).EnsureInitialized();
            _ = new MemoryIndexDocumentSync(_config, schema).SyncOnce();
        }
        catch (Exception ex)
        {
            _auditLogger.Log("web", "conversation_search_index_sync", "failed", ex.Message);
        }
    }

    private static string NormalizeConversationSearchSnippet(string snippet)
    {
        var normalized = Regex.Replace((snippet ?? string.Empty).Trim(), @"\s+", " ");
        if (normalized.Length <= 360)
        {
            return normalized;
        }

        return normalized[..357].TrimEnd() + "...";
    }

    private static string BuildInlineSnippet(string text, int index, int queryLength)
    {
        var start = Math.Max(0, index - 140);
        var end = Math.Min(text.Length, index + Math.Max(queryLength, 1) + 220);
        var snippet = text[start..end];
        if (start > 0)
        {
            snippet = "..." + snippet;
        }

        if (end < text.Length)
        {
            snippet += "...";
        }

        return NormalizeConversationSearchSnippet(snippet);
    }

    private IReadOnlyList<ConversationThreadView> ReadBackupConversations(byte[] zipBytes)
    {
        using var memory = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("conversations.json");
        if (entry == null)
        {
            return Array.Empty<ConversationThreadView>();
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();
        var state = JsonSerializer.Deserialize(json, OmniJsonContext.Default.ConversationState);
        if (state?.Conversations == null)
        {
            return Array.Empty<ConversationThreadView>();
        }

        return state.Conversations
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => new ConversationThreadView(
                item.Id.Trim(),
                item.Scope,
                item.Mode,
                item.Title,
                item.Project,
                item.Category,
                item.Tags.ToArray(),
                item.CreatedUtc,
                item.UpdatedUtc,
                item.Messages
                    .Select(message => new ConversationMessageView(
                        message.Role,
                        message.Text,
                        message.Meta,
                        message.CreatedUtc,
                        message.TokenUsage
                    ))
                    .ToArray(),
                item.LinkedMemoryNotes.ToArray(),
                item.LatestCodingResult
            ))
            .ToArray();
    }

    private static int CountBackupFiles(byte[] zipBytes)
    {
        using var memory = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        return archive.Entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Name));
    }

    private BackupFileImportResult ImportBackupFiles(byte[] zipBytes, bool overwrite)
    {
        var imported = 0;
        var skipped = 0;
        using var memory = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)
                || entry.FullName.Equals("conversations.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetPath = ResolveBackupImportTargetPath(entry.FullName);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                skipped += 1;
                continue;
            }

            if (File.Exists(targetPath) && !overwrite)
            {
                skipped += 1;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ResolveStateRootDir());
            using var input = entry.Open();
            using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            imported += 1;
        }

        return new BackupFileImportResult(imported, skipped);
    }

    private string? ResolveBackupImportTargetPath(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Split('/').Any(part => part == ".."))
        {
            return null;
        }

        var stateRoot = ResolveStateRootDir();
        return normalized switch
        {
            "routines.json" => _config.RoutineStatePath,
            "routing-policy.json" => ResolveStateFilePath("routing-policy.json"),
            _ when normalized.StartsWith("memory-notes/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(stateRoot, normalized),
            _ when normalized.StartsWith("plans/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(stateRoot, normalized),
            _ when normalized.StartsWith("tasks/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(stateRoot, normalized),
            _ when normalized.StartsWith("notebooks/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(stateRoot, normalized),
            _ when normalized.StartsWith("skills/global/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(stateRoot, "skills", normalized["skills/global/".Length..]),
            _ when normalized.StartsWith("commands/global/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(stateRoot, "commands", normalized["commands/global/".Length..]),
            _ when normalized.StartsWith("skills/project/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(_config.WorkspaceRootDir, ".omni", "skills", normalized["skills/project/".Length..]),
            _ when normalized.StartsWith("commands/project/", StringComparison.OrdinalIgnoreCase) =>
                Path.Combine(_config.WorkspaceRootDir, ".omni", "commands", normalized["commands/project/".Length..]),
            _ => null
        };
    }

    private void AddFileToBackup(ZipArchive archive, string filePath, string entryName, List<string> included)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var fileStream = File.OpenRead(filePath);
        fileStream.CopyTo(entryStream);
        included.Add(entryName);
    }

    private void AddDirectoryToBackup(ZipArchive archive, string directoryPath, string entryRoot, List<string> included)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        var root = Path.GetFullPath(directoryPath);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (ShouldSkipBackupFile(relative))
            {
                continue;
            }

            var entryName = $"{entryRoot.TrimEnd('/')}/{relative}";
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(file);
            fileStream.CopyTo(entryStream);
            included.Add(entryName);
        }
    }

    private static bool ShouldSkipBackupFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var lower = normalized.ToLowerInvariant();
        return lower.Contains("/cache/", StringComparison.Ordinal)
               || lower.Contains("/lock", StringComparison.Ordinal)
               || lower.EndsWith(".lock", StringComparison.Ordinal)
               || lower.EndsWith(".log", StringComparison.Ordinal)
               || lower.Contains("/runtime/", StringComparison.Ordinal)
               || lower.Contains("/outbox", StringComparison.Ordinal);
    }

    private string ResolveStateRootDir()
    {
        return Path.GetDirectoryName(Path.GetFullPath(_config.ConversationStatePath))
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".omninode");
    }

    private string ResolveStateFilePath(string fileName) => Path.Combine(ResolveStateRootDir(), fileName);

    private string ResolveStateDirectoryPath(string directoryName) => Path.Combine(ResolveStateRootDir(), directoryName);

    private void CleanupExpiredBackupPreviews()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        foreach (var item in _backupImportPreviews)
        {
            if (item.Value.CreatedUtc < cutoff)
            {
                _backupImportPreviews.TryRemove(item.Key, out _);
            }
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

    private static string NormalizeAuditToken(string? token, string fallback)
    {
        var normalized = (token ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized.ToLowerInvariant();
    }

    private sealed record BackupImportPackage(
        byte[] ZipBytes,
        IReadOnlyList<ConversationThreadView> Conversations,
        DateTimeOffset CreatedUtc
    );

    private sealed record BackupFileImportResult(int Imported, int Skipped);
}
