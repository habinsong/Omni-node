namespace OmniNode.Middleware;

public sealed class TaskGraphApplicationService : ITaskGraphApplicationService
{
    private readonly CommandService _inner;

    public TaskGraphApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public TaskGraphActionResult CreateTaskGraph(string planId)
    {
        return _inner.CreateTaskGraph(planId);
    }

    public TaskGraphActionResult UpdateTaskGraph(string graphId, string? rawJson)
    {
        return _inner.UpdateTaskGraph(graphId, rawJson);
    }

    public TaskGraphListResult ListTaskGraphs()
    {
        return _inner.ListTaskGraphs();
    }

    public TaskGraphSnapshot? GetTaskGraph(string graphId)
    {
        return _inner.GetTaskGraph(graphId);
    }

    public Task<TaskGraphActionResult> RunTaskGraphAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        return _inner.RunTaskGraphAsync(graphId, source, eventSink, cancellationToken);
    }

    public TaskGraphActionResult CancelTask(string graphId, string taskId)
    {
        return _inner.CancelTask(graphId, taskId);
    }

    public Task<TaskGraphActionResult> RetryTaskAsync(
        string graphId,
        string taskId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        return _inner.RetryTaskAsync(graphId, taskId, source, eventSink, cancellationToken);
    }

    public Task<TaskGraphActionResult> ResumeTaskGraphAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        return _inner.ResumeTaskGraphAsync(graphId, source, eventSink, cancellationToken);
    }

    public TaskOutputResult? GetTaskOutput(string graphId, string taskId)
    {
        return _inner.GetTaskOutput(graphId, taskId);
    }
}
