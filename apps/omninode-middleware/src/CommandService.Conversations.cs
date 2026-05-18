namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public IReadOnlyList<ConversationThreadSummary> ListConversations(string scope, string mode)
        => _conversationAppService.ListConversations(scope, mode);

    public ConversationThreadView CreateConversation(
        string scope,
        string mode,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    ) => _conversationAppService.CreateConversation(scope, mode, title, project, category, tags);

    public ConversationThreadView? GetConversation(string conversationId)
        => _conversationAppService.GetConversation(conversationId);

    public bool DeleteConversation(string conversationId)
        => _conversationAppService.DeleteConversation(conversationId);

    public ConversationThreadView UpdateConversationMetadata(
        string conversationId,
        string? conversationTitle,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    ) => _conversationAppService.UpdateConversationMetadata(conversationId, conversationTitle, project, category, tags);

    public WorkspaceFilePreview? ReadWorkspaceFile(string filePath, int maxChars = 120_000)
        => _conversationAppService.ReadWorkspaceFile(filePath, maxChars);

    public WorkspaceFilePreview? ReadWorkspaceFile(string filePath, string? conversationId, int maxChars = 120_000)
        => _conversationAppService.ReadWorkspaceFile(filePath, conversationId, maxChars);
}
