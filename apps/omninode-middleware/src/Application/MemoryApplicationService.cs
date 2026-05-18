namespace OmniNode.Middleware;

public sealed class MemoryApplicationService : IMemoryApplicationService
{
    private readonly CommandService _inner;

    public MemoryApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public string ClearMemory(string? scope, string source = "web") => _inner.ClearMemory(scope, source);
    public IReadOnlyList<MemoryNoteItem> ListMemoryNotes() => _inner.ListMemoryNotes();
    public MemoryNoteReadResult? ReadMemoryNote(string name) => _inner.ReadMemoryNote(name);
    public (MemoryNoteRenameResult Result, int RelinkedConversations) RenameMemoryNote(string name, string newName) => _inner.RenameMemoryNote(name, newName);
    public MemoryNoteDeleteResult DeleteMemoryNotes(IReadOnlyList<string>? names) => _inner.DeleteMemoryNotes(names);
    public Task<MemoryNoteCreateResult> CreateMemoryNoteAsync(string conversationId, string source, bool compactConversation, CancellationToken cancellationToken) => _inner.CreateMemoryNoteAsync(conversationId, source, compactConversation, cancellationToken);
    public MemorySearchToolResult SearchMemory(string query, int? maxResults = null, double? minScore = null) => _inner.SearchMemory(query, maxResults, minScore);
    public MemoryGetToolResult GetMemory(string path, int? from = null, int? lines = null) => _inner.GetMemory(path, from, lines);
    public MemoryIndexRebuildResult RebuildMemoryIndex() => _inner.RebuildMemoryIndex();
}
