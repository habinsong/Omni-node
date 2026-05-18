using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsTaskCommandDispatcher
{
    internal delegate Task SendTaskGraphActionResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        TaskGraphActionResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendTaskGraphListResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        TaskGraphListResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendTaskOutputResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        TaskOutputResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendTaskUpdatedDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string graphId,
        TaskNode task,
        CancellationToken cancellationToken
    );

    internal delegate Task SendTaskLogDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string graphId,
        string taskId,
        string line,
        CancellationToken cancellationToken
    );

    private readonly ITaskGraphApplicationService _taskGraphService;
    private readonly SendTaskGraphActionResultDelegate _sendTaskGraphActionResultAsync;
    private readonly SendTaskGraphListResultDelegate _sendTaskGraphListResultAsync;
    private readonly SendTaskOutputResultDelegate _sendTaskOutputResultAsync;
    private readonly SendTaskUpdatedDelegate _sendTaskUpdatedAsync;
    private readonly SendTaskLogDelegate _sendTaskLogAsync;

    public WsTaskCommandDispatcher(
        ITaskGraphApplicationService taskGraphService,
        SendTaskGraphActionResultDelegate sendTaskGraphActionResultAsync,
        SendTaskGraphListResultDelegate sendTaskGraphListResultAsync,
        SendTaskOutputResultDelegate sendTaskOutputResultAsync,
        SendTaskUpdatedDelegate sendTaskUpdatedAsync,
        SendTaskLogDelegate sendTaskLogAsync
    )
    {
        _taskGraphService = taskGraphService;
        _sendTaskGraphActionResultAsync = sendTaskGraphActionResultAsync;
        _sendTaskGraphListResultAsync = sendTaskGraphListResultAsync;
        _sendTaskOutputResultAsync = sendTaskOutputResultAsync;
        _sendTaskUpdatedAsync = sendTaskUpdatedAsync;
        _sendTaskLogAsync = sendTaskLogAsync;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "task_graph_list")
        {
            await _sendTaskGraphListResultAsync(
                socket,
                sendLock,
                _taskGraphService.ListTaskGraphs(),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "task_graph_create")
        {
            var result = _taskGraphService.CreateTaskGraph(message.PlanId ?? string.Empty);
            await _sendTaskGraphActionResultAsync(socket, sendLock, "create", result, cancellationToken);
            return true;
        }

        if (message.Type == "task_graph_update")
        {
            var result = _taskGraphService.UpdateTaskGraph(message.GraphId ?? string.Empty, message.RawJson);
            await _sendTaskGraphActionResultAsync(socket, sendLock, "update", result, cancellationToken);
            return true;
        }

        if (message.Type == "task_graph_get")
        {
            var snapshot = _taskGraphService.GetTaskGraph(message.GraphId ?? string.Empty);
            var result = snapshot == null
                ? new TaskGraphActionResult(false, "Task graph를 찾을 수 없습니다.", null)
                : new TaskGraphActionResult(true, "Task graph를 불러왔습니다.", snapshot);
            await _sendTaskGraphActionResultAsync(socket, sendLock, "get", result, cancellationToken);
            return true;
        }

        if (message.Type == "task_graph_run")
        {
            var sink = new TaskGraphEventSink
            {
                OnTaskUpdatedAsync = (graphId, task, token) =>
                    _sendTaskUpdatedAsync(socket, sendLock, graphId, task, token),
                OnTaskLogAsync = (graphId, taskId, line, token) =>
                    _sendTaskLogAsync(socket, sendLock, graphId, taskId, line, token)
            };
            var result = await _taskGraphService.RunTaskGraphAsync(
                message.GraphId ?? string.Empty,
                "web",
                sink,
                cancellationToken
            );
            await _sendTaskGraphActionResultAsync(socket, sendLock, "run", result, cancellationToken);
            return true;
        }

        if (message.Type == "task_cancel")
        {
            var result = _taskGraphService.CancelTask(
                message.GraphId ?? string.Empty,
                message.TaskId ?? string.Empty
            );
            await _sendTaskGraphActionResultAsync(socket, sendLock, "cancel", result, cancellationToken);
            return true;
        }

        if (message.Type == "task_retry")
        {
            var sink = new TaskGraphEventSink
            {
                OnTaskUpdatedAsync = (graphId, task, token) =>
                    _sendTaskUpdatedAsync(socket, sendLock, graphId, task, token),
                OnTaskLogAsync = (graphId, taskId, line, token) =>
                    _sendTaskLogAsync(socket, sendLock, graphId, taskId, line, token)
            };
            var result = await _taskGraphService.RetryTaskAsync(
                message.GraphId ?? string.Empty,
                message.TaskId ?? string.Empty,
                "web",
                sink,
                cancellationToken
            );
            await _sendTaskGraphActionResultAsync(socket, sendLock, "retry", result, cancellationToken);
            return true;
        }

        if (message.Type == "task_resume")
        {
            var sink = new TaskGraphEventSink
            {
                OnTaskUpdatedAsync = (graphId, task, token) =>
                    _sendTaskUpdatedAsync(socket, sendLock, graphId, task, token),
                OnTaskLogAsync = (graphId, taskId, line, token) =>
                    _sendTaskLogAsync(socket, sendLock, graphId, taskId, line, token)
            };
            var result = await _taskGraphService.ResumeTaskGraphAsync(
                message.GraphId ?? string.Empty,
                "web",
                sink,
                cancellationToken
            );
            await _sendTaskGraphActionResultAsync(socket, sendLock, "resume", result, cancellationToken);
            return true;
        }

        if (message.Type == "task_output_get")
        {
            var output = _taskGraphService.GetTaskOutput(
                message.GraphId ?? string.Empty,
                message.TaskId ?? string.Empty
            );
            if (output == null)
            {
                var fallback = new TaskGraphActionResult(false, "Task output을 찾을 수 없습니다.", null);
                await _sendTaskGraphActionResultAsync(socket, sendLock, "output", fallback, cancellationToken);
            }
            else
            {
                await _sendTaskOutputResultAsync(socket, sendLock, output, cancellationToken);
            }

            return true;
        }

        return false;
    }
}
