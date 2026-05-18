namespace OmniNode.Middleware;

public sealed class MemoryApplicationService : IMemoryApplicationService
{
    private readonly IConversationStore _conversationStore;
    private readonly IMemoryNoteStore _memoryNoteStore;
    private readonly AuditLogger _auditLogger;
    private readonly AppConfig _config;
    private readonly MemorySearchTool _memorySearchTool;
    private readonly MemoryGetTool _memoryGetTool;
    private Func<string, string, bool, CancellationToken, Task<MemoryNoteCreateResult>>? _createMemoryNote;

    public MemoryApplicationService(
        IConversationStore conversationStore,
        IMemoryNoteStore memoryNoteStore,
        AuditLogger auditLogger,
        AppConfig config,
        MemorySearchTool memorySearchTool,
        MemoryGetTool memoryGetTool
    )
    {
        _conversationStore = conversationStore;
        _memoryNoteStore = memoryNoteStore;
        _auditLogger = auditLogger;
        _config = config;
        _memorySearchTool = memorySearchTool;
        _memoryGetTool = memoryGetTool;
    }

    // CreateMemoryNoteAsync는 CommandService partial의 private state(LLM
    // preferences)에 강하게 결합되어 있어 façade로 옮기지 못함. CommandService
    // 생성 후 ConfigureCreateMemoryNoteDelegate로 위임 함수를 주입.
    public void ConfigureCreateMemoryNoteDelegate(
        Func<string, string, bool, CancellationToken, Task<MemoryNoteCreateResult>> @delegate
    )
    {
        _createMemoryNote = @delegate;
    }

    public string ClearMemory(string? scope, string source = "web")
    {
        var normalized = NormalizeMemoryClearScope(scope);
        var conversationScope = normalized == "telegram" ? "chat" : normalized;

        var removedConversations = 0;
        if (conversationScope == "all")
        {
            removedConversations += _conversationStore.DeleteByScope("chat");
            removedConversations += _conversationStore.DeleteByScope("coding");
        }
        else
        {
            removedConversations = _conversationStore.DeleteByScope(conversationScope);
        }

        var removedNotes = conversationScope == "all"
            ? _memoryNoteStore.DeleteByScope("all")
            : _memoryNoteStore.DeleteByScope(conversationScope);

        var message = $"scope={normalized} conversations={removedConversations} notes={removedNotes}";
        _auditLogger.Log(source, "clear_memory", "ok", message);
        return message;
    }

    public IReadOnlyList<MemoryNoteItem> ListMemoryNotes() => _memoryNoteStore.List();

    public MemoryNoteReadResult? ReadMemoryNote(string name) => _memoryNoteStore.Read(name);

    public (MemoryNoteRenameResult Result, int RelinkedConversations) RenameMemoryNote(string name, string newName)
    {
        var renamed = _memoryNoteStore.Rename(name, newName);
        if (!renamed.Ok || string.IsNullOrWhiteSpace(renamed.OldName) || string.IsNullOrWhiteSpace(renamed.NewName))
        {
            _auditLogger.Log("web", "memory_note_rename", renamed.Ok ? "ok" : "skip", renamed.Message);
            return (renamed, 0);
        }

        var relinkedConversations = _conversationStore.RenameLinkedMemoryNote(renamed.OldName, renamed.NewName);
        _auditLogger.Log(
            "web",
            "memory_note_rename",
            "ok",
            $"old={renamed.OldName} new={renamed.NewName} relinkedConversations={relinkedConversations}"
        );
        return (renamed, relinkedConversations);
    }

    public MemoryNoteDeleteResult DeleteMemoryNotes(IReadOnlyList<string>? names)
    {
        var normalizedNames = (names ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedNames.Length == 0)
        {
            return new MemoryNoteDeleteResult(
                false,
                "삭제할 메모리 노트를 선택하세요.",
                0,
                0,
                0,
                Array.Empty<string>()
            );
        }

        var removedNames = new List<string>(normalizedNames.Length);
        foreach (var noteName in normalizedNames)
        {
            if (_memoryNoteStore.Delete(noteName))
            {
                removedNames.Add(noteName);
            }
        }

        var unlinkedConversations = removedNames.Count > 0
            ? _conversationStore.RemoveLinkedMemoryNotes(removedNames)
            : 0;
        var removedCount = removedNames.Count;
        var message = removedCount == 0
            ? "선택한 메모리 노트를 삭제하지 못했습니다."
            : $"메모리 노트 삭제 완료: {removedCount}/{normalizedNames.Length}";
        _auditLogger.Log(
            "web",
            "memory_note_delete",
            removedCount > 0 ? "ok" : "skip",
            $"requested={normalizedNames.Length} removed={removedCount} unlinkedConversations={unlinkedConversations}"
        );
        return new MemoryNoteDeleteResult(
            removedCount > 0,
            message,
            normalizedNames.Length,
            removedCount,
            unlinkedConversations,
            removedNames.ToArray()
        );
    }

    public Task<MemoryNoteCreateResult> CreateMemoryNoteAsync(
        string conversationId,
        string source,
        bool compactConversation,
        CancellationToken cancellationToken
    )
    {
        if (_createMemoryNote == null)
        {
            throw new InvalidOperationException(
                "MemoryApplicationService.CreateMemoryNoteAsync delegate가 주입되지 않았습니다. " +
                "Program.cs에서 ConfigureCreateMemoryNoteDelegate를 호출하세요."
            );
        }

        return _createMemoryNote(conversationId, source, compactConversation, cancellationToken);
    }

    public MemorySearchToolResult SearchMemory(string query, int? maxResults = null, double? minScore = null)
        => _memorySearchTool.Search(query, maxResults, minScore);

    public MemoryGetToolResult GetMemory(string path, int? from = null, int? lines = null)
        => _memoryGetTool.Get(path, from, lines);

    public MemoryIndexRebuildResult RebuildMemoryIndex()
    {
        try
        {
            var schema = new MemoryIndexSchemaBootstrap(_config).EnsureInitialized();
            var snapshot = new MemoryIndexDocumentSync(_config, schema).SyncOnce();
            var message = $"메모리 인덱스 재빌드 완료: scanned={snapshot.ScannedDocuments}, indexed={snapshot.IndexedDocuments}, skipped={snapshot.SkippedDocuments}, removed={snapshot.RemovedDocuments}";
            _auditLogger.Log("web", "memory_index_rebuild", "ok", message);
            return new MemoryIndexRebuildResult(true, message, snapshot);
        }
        catch (Exception ex)
        {
            var message = $"메모리 인덱스 재빌드 실패: {ex.Message}";
            _auditLogger.Log("web", "memory_index_rebuild", "error", message.Length > 300 ? message[..300] : message);
            return new MemoryIndexRebuildResult(false, message, null, ex.Message);
        }
    }

    private static string NormalizeMemoryClearScope(string? scope)
    {
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "chat" => "chat",
            "coding" => "coding",
            "telegram" => "telegram",
            "all" => "all",
            _ => "chat"
        };
    }
}
