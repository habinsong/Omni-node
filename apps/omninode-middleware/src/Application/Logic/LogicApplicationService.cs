namespace OmniNode.Middleware;

public sealed class LogicApplicationService : ILogicApplicationService
{
    private readonly CommandService _inner;

    public LogicApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public LogicGraphListResult ListLogicGraphs() => _inner.ListLogicGraphs();
    public LogicGraphActionResult GetLogicGraph(string graphId) => _inner.GetLogicGraph(graphId);
    public LogicPathBrowseResult BrowseLogicPath(string scope, string? rootKey, string? browsePath) => _inner.BrowseLogicPath(scope, rootKey, browsePath);
    public Task<LogicGraphActionResult> SaveLogicGraphAsync(string? graphId, string logicGraphJson, string source, CancellationToken cancellationToken) => _inner.SaveLogicGraphAsync(graphId, logicGraphJson, source, cancellationToken);
    public LogicGraphActionResult DeleteLogicGraph(string graphId) => _inner.DeleteLogicGraph(graphId);
    public Task<LogicRunActionResult> RunLogicGraphAsync(string graphId, string source, string? runInput, Action<LogicRunEvent>? eventCallback, CancellationToken cancellationToken) => _inner.RunLogicGraphAsync(graphId, source, runInput, eventCallback, cancellationToken);
    public LogicRunActionResult CancelLogicGraphRun(string runId) => _inner.CancelLogicGraphRun(runId);
    public LogicRunSnapshot? GetLogicGraphRun(string runId) => _inner.GetLogicGraphRun(runId);
}
