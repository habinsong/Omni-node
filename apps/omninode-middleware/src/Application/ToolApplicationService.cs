namespace OmniNode.Middleware;

public sealed class ToolApplicationService : IToolApplicationService
{
    private readonly SessionListTool _sessionListTool;
    private readonly SessionHistoryTool _sessionHistoryTool;
    private readonly SessionSendTool _sessionSendTool;
    private readonly SessionSpawnTool _sessionSpawnTool;
    private readonly WebFetchTool _webFetchTool;
    private readonly BrowserTool _browserTool;
    private readonly CanvasTool _canvasTool;
    private readonly NodesTool _nodesTool;
    private readonly CleanupService _cleanupService;

    // Cron/Search 메서드는 _routinesById 등록부와 SearchPipeline에 깊게
    // 결합되어 있어 façade로 옮기지 못함. CommandService 생성 후
    // ConfigureCronAndSearchDelegates로 위임 함수를 주입.
    private CronSearchDelegates? _cronSearch;

    public ToolApplicationService(
        SessionListTool sessionListTool,
        SessionHistoryTool sessionHistoryTool,
        SessionSendTool sessionSendTool,
        SessionSpawnTool sessionSpawnTool,
        WebFetchTool webFetchTool,
        BrowserTool browserTool,
        CanvasTool canvasTool,
        NodesTool nodesTool,
        CleanupService cleanupService
    )
    {
        _sessionListTool = sessionListTool;
        _sessionHistoryTool = sessionHistoryTool;
        _sessionSendTool = sessionSendTool;
        _sessionSpawnTool = sessionSpawnTool;
        _webFetchTool = webFetchTool;
        _browserTool = browserTool;
        _canvasTool = canvasTool;
        _nodesTool = nodesTool;
        _cleanupService = cleanupService;
    }

    public void ConfigureCronAndSearchDelegates(CronSearchDelegates delegates)
    {
        _cronSearch = delegates;
    }

    public SessionListToolResult ListSessions(
        IReadOnlyList<string>? kinds = null,
        int? limit = null,
        int? activeMinutes = null,
        int? messageLimit = null,
        string? search = null,
        string? scope = null,
        string? mode = null
    ) => _sessionListTool.List(kinds, limit, activeMinutes, messageLimit, search, scope, mode);

    public SessionHistoryToolResult GetSessionHistory(
        string? sessionKey,
        int? limit = null,
        bool includeTools = false
    ) => _sessionHistoryTool.Get(sessionKey, limit, includeTools);

    public SessionSendToolResult SendToSession(
        string? sessionKey,
        string? message,
        int? timeoutSeconds = null
    ) => _sessionSendTool.Send(sessionKey, message, timeoutSeconds);

    public SessionSpawnToolResult SpawnSession(
        string? task,
        string? label = null,
        string? runtime = null,
        int? runTimeoutSeconds = null,
        int? timeoutSeconds = null,
        bool? thread = null,
        string? mode = null
    ) => _sessionSpawnTool.Spawn(task, label, runtime, runTimeoutSeconds, timeoutSeconds, thread, mode);

    public Task<WebFetchToolResult> FetchWebAsync(
        string url,
        string? extractMode = null,
        int? maxChars = null,
        CancellationToken cancellationToken = default
    ) => _webFetchTool.FetchAsync(url, extractMode, maxChars, cancellationToken);

    public BrowserToolResult ExecuteBrowser(
        string? action,
        string? targetUrl = null,
        string? profile = null,
        string? targetId = null,
        int? limit = null
    ) => _browserTool.Execute(action, targetUrl, profile, targetId, limit);

    public CanvasToolResult ExecuteCanvas(
        string? action,
        string? profile = null,
        string? target = null,
        string? targetUrl = null,
        string? javaScript = null,
        string? jsonl = null,
        string? outputFormat = null,
        int? maxWidth = null
    ) => _canvasTool.Execute(action, profile, target, targetUrl, javaScript, jsonl, outputFormat, maxWidth);

    public NodesToolResult ExecuteNodes(
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
    ) => _nodesTool.Execute(action, profile, node, requestId, title, body, priority, delivery, invokeCommand, invokeParamsJson);

    public CleanupPreviewResult PreviewCleanup() => _cleanupService.PreviewCleanup();

    public CleanupApplyResult ApplyCleanup(string? previewId) => _cleanupService.ApplyCleanup(previewId);

    // --- 다음 메서드들은 _routinesById/SearchPipeline에 결합되어 있어 setter
    // delegate를 통해 CommandService 구현으로 위임. ---

    public CronToolStatusResult GetCronStatus()
        => RequireCronSearch().GetCronStatus();

    public CronToolListResult ListCronJobs(bool includeDisabled = false, int? limit = null, int? offset = null)
        => RequireCronSearch().ListCronJobs(includeDisabled, limit, offset);

    public CronToolRunsResult ListCronRuns(string? jobId, int? limit = null, int? offset = null)
        => RequireCronSearch().ListCronRuns(jobId, limit, offset);

    public CronToolAddResult AddCronJob(string? rawJobJson)
        => RequireCronSearch().AddCronJob(rawJobJson);

    public CronToolUpdateResult UpdateCronJob(string? jobId, string? rawPatchJson)
        => RequireCronSearch().UpdateCronJob(jobId, rawPatchJson);

    public Task<CronToolRunResult> RunCronJobAsync(string? jobId, string? runMode, string source, CancellationToken cancellationToken)
        => RequireCronSearch().RunCronJobAsync(jobId, runMode, source, cancellationToken);

    public CronToolWakeResult WakeCron(string? mode, string? text, string source)
        => RequireCronSearch().WakeCron(mode, text, source);

    public CronToolRemoveResult RemoveCronJob(string? jobId)
        => RequireCronSearch().RemoveCronJob(jobId);

    public Task<WebSearchToolResult> SearchWebAsync(
        string query,
        int? count = null,
        string? freshness = null,
        CancellationToken cancellationToken = default,
        string source = "web"
    ) => RequireCronSearch().SearchWebAsync(query, count, freshness, cancellationToken, source);

    private CronSearchDelegates RequireCronSearch()
    {
        if (_cronSearch == null)
        {
            throw new InvalidOperationException(
                "ToolApplicationService.ConfigureCronAndSearchDelegates가 호출되지 않았습니다."
            );
        }

        return _cronSearch;
    }

    public sealed record CronSearchDelegates(
        Func<CronToolStatusResult> GetCronStatus,
        Func<bool, int?, int?, CronToolListResult> ListCronJobs,
        Func<string?, int?, int?, CronToolRunsResult> ListCronRuns,
        Func<string?, CronToolAddResult> AddCronJob,
        Func<string?, string?, CronToolUpdateResult> UpdateCronJob,
        Func<string?, string?, string, CancellationToken, Task<CronToolRunResult>> RunCronJobAsync,
        Func<string?, string?, string, CronToolWakeResult> WakeCron,
        Func<string?, CronToolRemoveResult> RemoveCronJob,
        Func<string, int?, string?, CancellationToken, string, Task<WebSearchToolResult>> SearchWebAsync
    );
}
