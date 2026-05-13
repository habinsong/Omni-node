namespace OmniNode.Middleware;

public sealed class ConversationApplicationService : IConversationApplicationService
{
    private readonly CommandService _inner;

    public ConversationApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<ConversationThreadSummary> ListConversations(string scope, string mode) => _inner.ListConversations(scope, mode);

    public ConversationThreadView CreateConversation(
        string scope,
        string mode,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    )
    {
        return _inner.CreateConversation(scope, mode, title, project, category, tags);
    }

    public ConversationThreadView? GetConversation(string conversationId) => _inner.GetConversation(conversationId);
    public bool DeleteConversation(string conversationId) => _inner.DeleteConversation(conversationId);
    public ConversationSearchResult SearchConversations(string query, int? maxResults = null) => _inner.SearchConversations(query, maxResults);
    public BackupExportResult ExportBackup() => _inner.ExportBackup();
    public BackupImportPreviewResult PreviewBackupImport(string fileName, string contentBase64) => _inner.PreviewBackupImport(fileName, contentBase64);
    public BackupImportApplyResult ApplyBackupImport(string previewId, bool overwrite) => _inner.ApplyBackupImport(previewId, overwrite);

    public ConversationThreadView UpdateConversationMetadata(
        string conversationId,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    )
    {
        return _inner.UpdateConversationMetadata(conversationId, title, project, category, tags);
    }

    public WorkspaceFilePreview? ReadWorkspaceFile(string filePath, int maxChars = 120_000) => _inner.ReadWorkspaceFile(filePath, maxChars);
    public WorkspaceFilePreview? ReadWorkspaceFile(string filePath, string? conversationId, int maxChars = 120_000) => _inner.ReadWorkspaceFile(filePath, conversationId, maxChars);
    public bool ClearActiveSkill(string? conversationId) => _inner.ClearActiveSkillForConversation(conversationId);
}
