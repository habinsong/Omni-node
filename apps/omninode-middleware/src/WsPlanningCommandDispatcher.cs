using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsPlanningCommandDispatcher
{
    internal delegate Task SendPlanActionResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        PlanActionResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendPlanListResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        PlanListResult result,
        CancellationToken cancellationToken
    );

    private readonly IPlanningApplicationService _planService;
    private readonly SendPlanActionResultDelegate _sendPlanActionResultAsync;
    private readonly SendPlanListResultDelegate _sendPlanListResultAsync;

    public WsPlanningCommandDispatcher(
        IPlanningApplicationService planService,
        SendPlanActionResultDelegate sendPlanActionResultAsync,
        SendPlanListResultDelegate sendPlanListResultAsync
    )
    {
        _planService = planService;
        _sendPlanActionResultAsync = sendPlanActionResultAsync;
        _sendPlanListResultAsync = sendPlanListResultAsync;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "plan_create")
        {
            var result = await _planService.CreatePlanAsync(
                message.Text ?? message.Message ?? string.Empty,
                message.Constraints,
                message.Mode,
                message.ConversationId,
                cancellationToken
            );
            await _sendPlanActionResultAsync(socket, sendLock, "create", result, cancellationToken);
            return true;
        }

        if (message.Type == "plan_review")
        {
            var result = await _planService.ReviewPlanAsync(message.PlanId ?? string.Empty, cancellationToken);
            await _sendPlanActionResultAsync(socket, sendLock, "review", result, cancellationToken);
            return true;
        }

        if (message.Type == "plan_approve")
        {
            var result = _planService.ApprovePlan(message.PlanId ?? string.Empty);
            await _sendPlanActionResultAsync(socket, sendLock, "approve", result, cancellationToken);
            return true;
        }

        if (message.Type == "plan_update")
        {
            var result = _planService.UpdatePlan(message.PlanId ?? string.Empty, message.RawJson);
            await _sendPlanActionResultAsync(socket, sendLock, "update", result, cancellationToken);
            return true;
        }

        if (message.Type == "plan_list")
        {
            await _sendPlanListResultAsync(socket, sendLock, _planService.ListPlans(), cancellationToken);
            return true;
        }

        if (message.Type == "plan_get")
        {
            var snapshot = _planService.GetPlan(message.PlanId ?? string.Empty);
            var result = snapshot == null
                ? new PlanActionResult(false, "계획을 찾을 수 없습니다.", null)
                : new PlanActionResult(true, "계획을 불러왔습니다.", snapshot);
            await _sendPlanActionResultAsync(socket, sendLock, "get", result, cancellationToken);
            return true;
        }

        if (message.Type == "plan_run")
        {
            var result = await _planService.RunPlanAsync(message.PlanId ?? string.Empty, "web", cancellationToken);
            await _sendPlanActionResultAsync(socket, sendLock, "run", result, cancellationToken);
            return true;
        }

        return false;
    }
}
