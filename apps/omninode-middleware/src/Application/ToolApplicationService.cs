namespace OmniNode.Middleware;

public sealed class ToolApplicationService : IToolApplicationService
{
    private readonly CommandService _inner;

    public ToolApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public SessionListToolResult ListSessions(IReadOnlyList<string>? kinds = null, int? limit = null, int? activeMinutes = null, int? messageLimit = null, string? search = null, string? scope = null, string? mode = null) => _inner.ListSessions(kinds, limit, activeMinutes, messageLimit, search, scope, mode);
    public SessionHistoryToolResult GetSessionHistory(string? sessionKey, int? limit = null, bool includeTools = false) => _inner.GetSessionHistory(sessionKey, limit, includeTools);
    public SessionSendToolResult SendToSession(string? sessionKey, string? message, int? timeoutSeconds = null) => _inner.SendToSession(sessionKey, message, timeoutSeconds);
    public SessionSpawnToolResult SpawnSession(string? task, string? label = null, string? runtime = null, int? runTimeoutSeconds = null, int? timeoutSeconds = null, bool? thread = null, string? mode = null) => _inner.SpawnSession(task, label, runtime, runTimeoutSeconds, timeoutSeconds, thread, mode);
    public CronToolStatusResult GetCronStatus() => _inner.GetCronStatus();
    public CronToolListResult ListCronJobs(bool includeDisabled = false, int? limit = null, int? offset = null) => _inner.ListCronJobs(includeDisabled, limit, offset);
    public CronToolRunsResult ListCronRuns(string? jobId, int? limit = null, int? offset = null) => _inner.ListCronRuns(jobId, limit, offset);
    public CronToolAddResult AddCronJob(string? rawJobJson) => _inner.AddCronJob(rawJobJson);
    public CronToolUpdateResult UpdateCronJob(string? jobId, string? rawPatchJson) => _inner.UpdateCronJob(jobId, rawPatchJson);
    public Task<CronToolRunResult> RunCronJobAsync(string? jobId, string? runMode, string source, CancellationToken cancellationToken) => _inner.RunCronJobAsync(jobId, runMode, source, cancellationToken);
    public CronToolWakeResult WakeCron(string? mode, string? text, string source) => _inner.WakeCron(mode, text, source);
    public CronToolRemoveResult RemoveCronJob(string? jobId) => _inner.RemoveCronJob(jobId);
    public Task<WebSearchToolResult> SearchWebAsync(string query, int? count = null, string? freshness = null, CancellationToken cancellationToken = default, string source = "web") => _inner.SearchWebAsync(query, count, freshness, cancellationToken, source);
    public Task<WebFetchToolResult> FetchWebAsync(string url, string? extractMode = null, int? maxChars = null, CancellationToken cancellationToken = default) => _inner.FetchWebAsync(url, extractMode, maxChars, cancellationToken);
    public BrowserToolResult ExecuteBrowser(string? action, string? targetUrl = null, string? profile = null, string? targetId = null, int? limit = null) => _inner.ExecuteBrowser(action, targetUrl, profile, targetId, limit);
    public CanvasToolResult ExecuteCanvas(string? action, string? profile = null, string? target = null, string? targetUrl = null, string? javaScript = null, string? jsonl = null, string? outputFormat = null, int? maxWidth = null) => _inner.ExecuteCanvas(action, profile, target, targetUrl, javaScript, jsonl, outputFormat, maxWidth);
    public NodesToolResult ExecuteNodes(string? action, string? profile = null, string? node = null, string? requestId = null, string? title = null, string? body = null, string? priority = null, string? delivery = null, string? invokeCommand = null, string? invokeParamsJson = null) => _inner.ExecuteNodes(action, profile, node, requestId, title, body, priority, delivery, invokeCommand, invokeParamsJson);
    public CleanupPreviewResult PreviewCleanup() => _inner.PreviewCleanup();
    public CleanupApplyResult ApplyCleanup(string? previewId) => _inner.ApplyCleanup(previewId);
}
