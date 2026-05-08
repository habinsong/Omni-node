namespace OmniNode.Middleware;

public interface ICommandExecutionService
{
    Task<string> ExecuteAsync(
        string input,
        string source,
        CancellationToken cancellationToken,
        IReadOnlyList<InputAttachment>? attachments = null,
        IReadOnlyList<string>? webUrls = null,
        bool webSearchEnabled = true,
        Action<string>? streamCallback = null
    );

    TelegramExecutionMetadata GetCurrentTelegramExecutionMetadata();
}

public interface ISettingsApplicationService
{
    SettingsSnapshot GetSettingsSnapshot();
    RoutingPolicyActionResult GetRoutingPolicySnapshot();
    RoutingPolicyActionResult SaveRoutingPolicy(RoutingPolicy? policy);
    RoutingPolicyActionResult ResetRoutingPolicy();
    RoutingDecision? GetLastRoutingDecision();
    string UpdateTelegramCredentials(string? botToken, string? chatId, bool persist);
    string UpdateLlmCredentials(
        string? groqApiKey,
        string? geminiApiKey,
        string? cerebrasApiKey,
        string? nvidiaApiKey,
        string? codexApiKey,
        bool persist
    );
    string DeleteTelegramCredentials(bool deletePersisted);
    string DeleteLlmCredentials(bool deletePersisted);
    string SetExternalDashboardEnabled(bool enabled);
    GeminiUsage GetGeminiUsageSnapshot();
    Task<CopilotPremiumUsageSnapshot> GetCopilotPremiumUsageSnapshotAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false
    );
    Task<string> SendTelegramTestAsync(CancellationToken cancellationToken);
    Task<string> StartCopilotLoginAsync(CancellationToken cancellationToken);
    Task<string> StartCodexLoginAsync(CancellationToken cancellationToken);
    Task<string> GetMetricsAsync(CancellationToken cancellationToken);
    Task<CopilotStatus> GetCopilotStatusAsync(CancellationToken cancellationToken);
    Task<CodexStatus> GetCodexStatusAsync(CancellationToken cancellationToken);
    Task<string> LogoutCodexAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CopilotModelInfo>> GetCopilotModelsAsync(CancellationToken cancellationToken);
    string GetSelectedCopilotModel();
    IReadOnlyDictionary<string, CopilotUsage> GetCopilotLocalUsageSnapshot();
    bool TrySetSelectedCopilotModel(string modelId);
}

public interface IDoctorApplicationService
{
    Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken);
    Task<DoctorReport?> GetLastDoctorReportAsync(CancellationToken cancellationToken);
}

public interface IPlanningApplicationService
{
    Task<PlanActionResult> CreatePlanAsync(
        string objective,
        IReadOnlyList<string>? constraints,
        string? mode,
        string? sourceConversationId,
        CancellationToken cancellationToken
    );
    Task<PlanActionResult> ReviewPlanAsync(string planId, CancellationToken cancellationToken);
    PlanActionResult ApprovePlan(string planId);
    PlanListResult ListPlans();
    PlanSnapshot? GetPlan(string planId);
    Task<PlanActionResult> RunPlanAsync(string planId, string source, CancellationToken cancellationToken);
}

public interface ITaskGraphApplicationService
{
    TaskGraphActionResult CreateTaskGraph(string planId);
    TaskGraphListResult ListTaskGraphs();
    TaskGraphSnapshot? GetTaskGraph(string graphId);
    Task<TaskGraphActionResult> RunTaskGraphAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    );
    TaskGraphActionResult CancelTask(string graphId, string taskId);
    TaskOutputResult? GetTaskOutput(string graphId, string taskId);
}

public interface ILogicApplicationService
{
    LogicGraphListResult ListLogicGraphs();
    LogicGraphActionResult GetLogicGraph(string graphId);
    LogicPathBrowseResult BrowseLogicPath(string scope, string? rootKey, string? browsePath);
    Task<LogicGraphActionResult> SaveLogicGraphAsync(
        string? graphId,
        string logicGraphJson,
        string source,
        CancellationToken cancellationToken
    );
    LogicGraphActionResult DeleteLogicGraph(string graphId);
    Task<LogicRunActionResult> RunLogicGraphAsync(
        string graphId,
        string source,
        string? runInput,
        Action<LogicRunEvent>? eventCallback,
        CancellationToken cancellationToken
    );
    LogicRunActionResult CancelLogicGraphRun(string runId);
    LogicRunSnapshot? GetLogicGraphRun(string runId);
}

public interface IRefactorApplicationService
{
    Task<RefactorActionResult> ReadWithAnchorsAsync(string path, CancellationToken cancellationToken);
    Task<RefactorActionResult> PreviewRefactorAsync(
        string path,
        IReadOnlyList<AnchorEditRequest>? edits,
        CancellationToken cancellationToken
    );
    Task<RefactorActionResult> ApplyRefactorAsync(string previewId, CancellationToken cancellationToken);
    Task<RefactorActionResult> RunLspRenameAsync(
        string path,
        string symbol,
        string newName,
        CancellationToken cancellationToken
    );
    Task<RefactorActionResult> RunAstReplaceAsync(
        string path,
        string pattern,
        string replacement,
        CancellationToken cancellationToken
    );
}

public interface IContextApplicationService
{
    Task<ProjectContextSnapshot> ScanProjectContextAsync(CancellationToken cancellationToken);
    Task<SkillManifestListResult> ListSkillsAsync(CancellationToken cancellationToken);
    Task<CommandTemplateListResult> ListCommandsAsync(CancellationToken cancellationToken);
}

public interface INotebookApplicationService
{
    Task<NotebookActionResult> GetNotebookAsync(string? projectKey, CancellationToken cancellationToken);
    Task<NotebookActionResult> AppendLearningAsync(string? projectKey, string content, CancellationToken cancellationToken);
    Task<NotebookActionResult> AppendDecisionAsync(string? projectKey, string content, CancellationToken cancellationToken);
    Task<NotebookActionResult> AppendVerificationAsync(string? projectKey, string content, CancellationToken cancellationToken);
    Task<NotebookActionResult> CreateHandoffAsync(string? projectKey, CancellationToken cancellationToken);
}

public interface IConversationApplicationService
{
    IReadOnlyList<ConversationThreadSummary> ListConversations(string scope, string mode);
    ConversationThreadView CreateConversation(
        string scope,
        string mode,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    );
    ConversationThreadView? GetConversation(string conversationId);
    bool DeleteConversation(string conversationId);
    ConversationSearchResult SearchConversations(string query, int? maxResults = null);
    BackupExportResult ExportBackup();
    BackupImportPreviewResult PreviewBackupImport(string fileName, string contentBase64);
    BackupImportApplyResult ApplyBackupImport(string previewId, bool overwrite);
    ConversationThreadView UpdateConversationMetadata(
        string conversationId,
        string? title,
        string? project,
        string? category,
        IReadOnlyList<string>? tags
    );
    WorkspaceFilePreview? ReadWorkspaceFile(string filePath, int maxChars = 120_000);
    WorkspaceFilePreview? ReadWorkspaceFile(string filePath, string? conversationId, int maxChars = 120_000);
}

public interface IMemoryApplicationService
{
    string ClearMemory(string? scope, string source = "web");
    IReadOnlyList<MemoryNoteItem> ListMemoryNotes();
    MemoryNoteReadResult? ReadMemoryNote(string name);
    (MemoryNoteRenameResult Result, int RelinkedConversations) RenameMemoryNote(string name, string newName);
    MemoryNoteDeleteResult DeleteMemoryNotes(IReadOnlyList<string>? names);
    Task<MemoryNoteCreateResult> CreateMemoryNoteAsync(
        string conversationId,
        string source,
        bool compactConversation,
        CancellationToken cancellationToken
    );
    MemorySearchToolResult SearchMemory(string query, int? maxResults = null, double? minScore = null);
    MemoryGetToolResult GetMemory(string path, int? from = null, int? lines = null);
}

public interface IToolApplicationService
{
    SessionListToolResult ListSessions(
        IReadOnlyList<string>? kinds = null,
        int? limit = null,
        int? activeMinutes = null,
        int? messageLimit = null,
        string? search = null,
        string? scope = null,
        string? mode = null
    );
    SessionHistoryToolResult GetSessionHistory(
        string? sessionKey,
        int? limit = null,
        bool includeTools = false
    );
    SessionSendToolResult SendToSession(
        string? sessionKey,
        string? message,
        int? timeoutSeconds = null
    );
    SessionSpawnToolResult SpawnSession(
        string? task,
        string? label = null,
        string? runtime = null,
        int? runTimeoutSeconds = null,
        int? timeoutSeconds = null,
        bool? thread = null,
        string? mode = null
    );
    CronToolStatusResult GetCronStatus();
    CronToolListResult ListCronJobs(
        bool includeDisabled = false,
        int? limit = null,
        int? offset = null
    );
    CronToolRunsResult ListCronRuns(
        string? jobId,
        int? limit = null,
        int? offset = null
    );
    CronToolAddResult AddCronJob(string? rawJobJson);
    CronToolUpdateResult UpdateCronJob(string? jobId, string? rawPatchJson);
    Task<CronToolRunResult> RunCronJobAsync(
        string? jobId,
        string? runMode,
        string source,
        CancellationToken cancellationToken
    );
    CronToolWakeResult WakeCron(
        string? mode,
        string? text,
        string source
    );
    CronToolRemoveResult RemoveCronJob(string? jobId);
    Task<WebSearchToolResult> SearchWebAsync(
        string query,
        int? count = null,
        string? freshness = null,
        CancellationToken cancellationToken = default,
        string source = "web"
    );
    Task<WebFetchToolResult> FetchWebAsync(
        string url,
        string? extractMode = null,
        int? maxChars = null,
        CancellationToken cancellationToken = default
    );
    BrowserToolResult ExecuteBrowser(
        string? action,
        string? targetUrl = null,
        string? profile = null,
        string? targetId = null,
        int? limit = null
    );
    CanvasToolResult ExecuteCanvas(
        string? action,
        string? profile = null,
        string? target = null,
        string? targetUrl = null,
        string? javaScript = null,
        string? jsonl = null,
        string? outputFormat = null,
        int? maxWidth = null
    );
    NodesToolResult ExecuteNodes(
        string? action,
        string? profile = null,
        string? node = null,
        string? requestId = null,
        string? title = null,
        string? body = null,
        string? priority = null,
        string? delivery = null,
        string? invokeCommand = null,
        string? invokeParamsJson = null
    );
}

public interface IRoutineApplicationService
{
    IReadOnlyList<RoutineSummary> ListRoutines();
    Task<RoutineActionResult> CreateRoutineAsync(
        string request,
        string source,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    );
    Task<RoutineActionResult> CreateRoutineAsync(
        string request,
        string? title,
        string? executionMode,
        string? agentProvider,
        string? agentModel,
        string? agentStartUrl,
        int? agentTimeoutSeconds,
        string? agentToolProfile,
        bool? agentUsePlaywright,
        string? scheduleSourceMode,
        int? maxRetries,
        int? retryDelaySeconds,
        string? notifyPolicy,
        bool? notifyTelegram,
        string? scheduleKind,
        string? scheduleTime,
        IReadOnlyList<int>? weekdays,
        int? dayOfMonth,
        string? timezoneId,
        string source,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    );
    Task<RoutineActionResult> UpdateRoutineAsync(
        string routineId,
        string request,
        string? title,
        string? executionMode,
        string? agentProvider,
        string? agentModel,
        string? agentStartUrl,
        int? agentTimeoutSeconds,
        string? agentToolProfile,
        bool? agentUsePlaywright,
        string? scheduleSourceMode,
        int? maxRetries,
        int? retryDelaySeconds,
        string? notifyPolicy,
        bool? notifyTelegram,
        string? scheduleKind,
        string? scheduleTime,
        IReadOnlyList<int>? weekdays,
        int? dayOfMonth,
        string? timezoneId,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    );
    Task<RoutineActionResult> RunRoutineNowAsync(string routineId, string source, CancellationToken cancellationToken);
    RoutineRunDetailResult GetRoutineRunDetail(string routineId, long ts);
    Task<RoutineActionResult> ResendRoutineRunToTelegramAsync(
        string routineId,
        long ts,
        CancellationToken cancellationToken
    );
    RoutineActionResult SetRoutineEnabled(string routineId, bool enabled);
    RoutineActionResult DeleteRoutine(string routineId);
}

public interface IChatApplicationService
{
    Task<ConversationChatResult> ChatSingleWithStateAsync(
        ChatRequest request,
        CancellationToken cancellationToken,
        Action<ChatStreamUpdate>? streamCallback = null
    );
    Task<ConversationChatResult> ChatOrchestrationWithStateAsync(
        ChatRequest request,
        CancellationToken cancellationToken
    );
    Task<ConversationMultiResult> ChatMultiWithStateAsync(
        MultiChatRequest request,
        CancellationToken cancellationToken
    );
}

public interface ICodingApplicationService
{
    Task<CodingRunResult> RunCodingSingleAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    );
    Task<CodingRunResult> RunCodingOrchestrationAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    );
    Task<CodingRunResult> RunCodingMultiAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    );
    Task<CodingResultExecutionResult> ExecuteLatestCodingResultAsync(
        string conversationId,
        string? standardInput,
        CancellationToken cancellationToken
    );
}

public interface IGatewayApplicationService :
    ICommandExecutionService,
    ISettingsApplicationService,
    IConversationApplicationService,
    IMemoryApplicationService,
    IToolApplicationService,
    ITaskGraphApplicationService,
    ILogicApplicationService,
    IRefactorApplicationService,
    IContextApplicationService,
    INotebookApplicationService,
    IRoutineApplicationService,
    IChatApplicationService,
    ICodingApplicationService
{
}
