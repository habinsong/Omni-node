namespace OmniNode.Middleware;

public interface IConversationStore
{
    IReadOnlyList<ConversationThreadSummary> List(string scope, string mode);
    IReadOnlyList<ConversationThreadSummary> ListAll();
    ConversationThreadView Create(
        string scope,
        string mode,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    );
    ConversationThreadView Ensure(
        string scope,
        string mode,
        string? conversationId,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    );
    ConversationThreadView? Get(string conversationId);
    IReadOnlyList<ConversationThreadView> ListAllViews();
    bool Delete(string conversationId);
    ConversationImportResult ImportConversations(IReadOnlyList<ConversationThreadView> conversations, bool overwrite);
    int DeleteByScope(string scope, string? mode = null);
    ConversationThreadView AppendMessage(string conversationId, string role, string text, string meta, TokenUsage? tokenUsage = null);
    ConversationThreadView SetLatestCodingResult(string conversationId, ConversationCodingResultSnapshot? result);
    void SetActiveSkillName(string conversationId, string? skillName);
    IReadOnlyList<(string ConversationId, string SkillName)> ListActiveSkillBindings();
    ConversationThreadView SetLinkedMemoryNotes(string conversationId, IReadOnlyList<string> names);
    ConversationThreadView AddLinkedMemoryNote(string conversationId, string name);
    int RemoveLinkedMemoryNotes(IReadOnlyList<string> names);
    int RenameLinkedMemoryNote(string oldName, string newName);
    ConversationThreadView UpdateTitle(string conversationId, string title);
    ConversationThreadView UpdateMetadata(
        string conversationId,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    );
    ConversationThreadView UpdateMetadata(
        string conversationId,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    );
    int GetTotalCharacters(string conversationId);
    ConversationThreadView CompactWithSummary(string conversationId, int keepRecentMessages, string summaryNotice);
    string BuildHistoryText(string conversationId, int maxMessages);
    string BuildCompressionSourceText(string conversationId, int keepRecentMessages);
}

public interface IMemoryNoteStore
{
    MemoryNoteSaveResult Save(
        string modeKey,
        string conversationId,
        string conversationTitle,
        string provider,
        string model,
        string summary
    );
    string RenameForConversationTitle(string name, string conversationTitle);
    IReadOnlyList<MemoryNoteItem> List();
    MemoryNoteReadResult? Read(string name);
    int DeleteByScope(string scope);
    bool Delete(string name);
    MemoryNoteRenameResult Rename(string name, string newName);
}

public interface IAuthSessionStore
{
    (string SessionId, string Otp) CreatePending(TimeSpan ttl);
    bool Authenticate(
        string sessionId,
        string otp,
        TimeSpan trustedTtl,
        out TrustedAuthTicket ticket
    );
    bool TryResumeTrusted(string authToken, out DateTimeOffset expiresAtUtc);
    bool MarkAuthenticatedFromTrusted(string sessionId, DateTimeOffset expiresAtUtc);
    bool TryGetOtp(string sessionId, out string otp);
    bool IsAuthenticated(string sessionId);
    void Remove(string sessionId);
}

internal interface IRoutineStore
{
    string StorePath { get; }
    IReadOnlyList<RoutineDefinition> Load();
    void Save(IReadOnlyList<RoutineDefinition> items);
}

internal sealed record RoutineRunArtifactWriteRequest(
    string RoutineId,
    string RoutineTitle,
    string Source,
    int AttemptCount,
    string Status,
    string Output,
    string? Error,
    string? TelegramStatus,
    RoutineAgentExecutionMetadata? AgentMetadata,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? AssetDirectory = null
);

internal interface IRunArtifactStore
{
    string RootDirectory { get; }
    string? WriteRoutineRun(RoutineRunArtifactWriteRequest request);
    string? ReadText(string? artifactPath);
}
