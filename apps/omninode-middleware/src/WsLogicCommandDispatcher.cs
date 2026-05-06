using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsLogicCommandDispatcher
{
    internal delegate Task SendLogicGraphListResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicGraphListResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendLogicGraphActionResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicGraphActionResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendLogicPathBrowseResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicPathBrowseResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendLogicRunActionResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicRunActionResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendLogicRunEventDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicRunEvent result,
        CancellationToken cancellationToken
    );

    private readonly ILogicApplicationService _logicService;
    private readonly SendLogicGraphListResultDelegate _sendLogicGraphListResultAsync;
    private readonly SendLogicGraphActionResultDelegate _sendLogicGraphActionResultAsync;
    private readonly SendLogicPathBrowseResultDelegate _sendLogicPathBrowseResultAsync;
    private readonly SendLogicRunActionResultDelegate _sendLogicRunActionResultAsync;
    private readonly SendLogicRunEventDelegate _sendLogicRunEventAsync;

    public WsLogicCommandDispatcher(
        ILogicApplicationService logicService,
        SendLogicGraphListResultDelegate sendLogicGraphListResultAsync,
        SendLogicGraphActionResultDelegate sendLogicGraphActionResultAsync,
        SendLogicPathBrowseResultDelegate sendLogicPathBrowseResultAsync,
        SendLogicRunActionResultDelegate sendLogicRunActionResultAsync,
        SendLogicRunEventDelegate sendLogicRunEventAsync
    )
    {
        _logicService = logicService;
        _sendLogicGraphListResultAsync = sendLogicGraphListResultAsync;
        _sendLogicGraphActionResultAsync = sendLogicGraphActionResultAsync;
        _sendLogicPathBrowseResultAsync = sendLogicPathBrowseResultAsync;
        _sendLogicRunActionResultAsync = sendLogicRunActionResultAsync;
        _sendLogicRunEventAsync = sendLogicRunEventAsync;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "logic_graph_list")
        {
            await _sendLogicGraphListResultAsync(
                socket,
                sendLock,
                _logicService.ListLogicGraphs(),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "logic_graph_get")
        {
            if (string.IsNullOrWhiteSpace(message.GraphId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"graphId가 필요합니다.\"}", cancellationToken);
                return true;
            }

            await _sendLogicGraphActionResultAsync(
                socket,
                sendLock,
                _logicService.GetLogicGraph(message.GraphId.Trim()),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "logic_path_list")
        {
            var scope = string.IsNullOrWhiteSpace(message.Scope)
                ? "workspace"
                : message.Scope.Trim();
            var result = _logicService.BrowseLogicPath(scope, message.Target, message.FilePath);
            await _sendLogicPathBrowseResultAsync(socket, sendLock, result, cancellationToken);
            return true;
        }

        if (message.Type == "logic_graph_save")
        {
            if (string.IsNullOrWhiteSpace(message.LogicGraphJson))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"logicGraph is required\"}", cancellationToken);
                return true;
            }

            var result = await _logicService.SaveLogicGraphAsync(
                message.GraphId,
                message.LogicGraphJson,
                "web",
                cancellationToken
            );
            await _sendLogicGraphActionResultAsync(socket, sendLock, result, cancellationToken);
            await _sendLogicGraphListResultAsync(socket, sendLock, _logicService.ListLogicGraphs(), cancellationToken);
            return true;
        }

        if (message.Type == "logic_graph_delete")
        {
            if (string.IsNullOrWhiteSpace(message.GraphId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"graphId가 필요합니다.\"}", cancellationToken);
                return true;
            }

            var result = _logicService.DeleteLogicGraph(message.GraphId.Trim());
            await _sendLogicGraphActionResultAsync(socket, sendLock, result, cancellationToken);
            await _sendLogicGraphListResultAsync(socket, sendLock, _logicService.ListLogicGraphs(), cancellationToken);
            return true;
        }

        if (message.Type == "logic_graph_run")
        {
            if (string.IsNullOrWhiteSpace(message.GraphId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"graphId가 필요합니다.\"}", cancellationToken);
                return true;
            }

            var result = await _logicService.RunLogicGraphAsync(
                message.GraphId.Trim(),
                "web",
                string.IsNullOrWhiteSpace(message.LogicRunInput) ? null : message.LogicRunInput.Trim(),
                evt => _ = _sendLogicRunEventAsync(socket, sendLock, evt, cancellationToken),
                cancellationToken
            );
            await _sendLogicRunActionResultAsync(socket, sendLock, result, cancellationToken);
            await _sendLogicGraphListResultAsync(socket, sendLock, _logicService.ListLogicGraphs(), cancellationToken);
            return true;
        }

        if (message.Type == "logic_graph_cancel")
        {
            if (string.IsNullOrWhiteSpace(message.LogicRunId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"runId가 필요합니다.\"}", cancellationToken);
                return true;
            }

            var result = _logicService.CancelLogicGraphRun(message.LogicRunId.Trim());
            await _sendLogicRunActionResultAsync(socket, sendLock, result, cancellationToken);
            return true;
        }

        if (message.Type == "logic_graph_run_get")
        {
            if (string.IsNullOrWhiteSpace(message.LogicRunId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"runId가 필요합니다.\"}", cancellationToken);
                return true;
            }

            var snapshot = _logicService.GetLogicGraphRun(message.LogicRunId.Trim());
            var result = snapshot == null
                ? new LogicRunActionResult(false, "실행 기록을 찾을 수 없습니다.", message.LogicRunId.Trim(), null)
                : new LogicRunActionResult(true, "실행 기록을 불러왔습니다.", message.LogicRunId.Trim(), snapshot);
            await _sendLogicRunActionResultAsync(socket, sendLock, result, cancellationToken);
            return true;
        }

        return false;
    }
}
