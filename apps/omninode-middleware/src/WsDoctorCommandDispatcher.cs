using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsDoctorCommandDispatcher
{
    internal delegate Task SendDoctorResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        DoctorReport? report,
        bool found,
        CancellationToken cancellationToken
    );

    private readonly IDoctorApplicationService _doctorService;
    private readonly SendDoctorResultDelegate _sendDoctorResultAsync;

    public WsDoctorCommandDispatcher(
        IDoctorApplicationService doctorService,
        SendDoctorResultDelegate sendDoctorResultAsync
    )
    {
        _doctorService = doctorService;
        _sendDoctorResultAsync = sendDoctorResultAsync;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "doctor_run")
        {
            var report = await _doctorService.RunDoctorAsync(cancellationToken);
            await _sendDoctorResultAsync(socket, sendLock, "run", report, true, cancellationToken);
            return true;
        }

        if (message.Type == "doctor_get_last")
        {
            var report = await _doctorService.GetLastDoctorReportAsync(cancellationToken);
            await _sendDoctorResultAsync(socket, sendLock, "get_last", report, report != null, cancellationToken);
            return true;
        }

        if (message.Type == "doctor_fix_preview")
        {
            var result = await _doctorService.PreviewDoctorFixAsync(cancellationToken);
            await WebSocketGateway.SendTextAsync(socket, sendLock, BuildDoctorFixResultJson("preview", result), cancellationToken);
            return true;
        }

        if (message.Type == "doctor_fix_apply")
        {
            var result = _doctorService.ApplyDoctorFix(message.PreviewId ?? string.Empty);
            await WebSocketGateway.SendTextAsync(socket, sendLock, BuildDoctorFixResultJson("apply", result), cancellationToken);
            return true;
        }

        return false;
    }

    private static string BuildDoctorFixResultJson(string action, DoctorFixPlanResult result)
    {
        var actionsJson = string.Join(",", result.Actions.Select(item =>
            "{"
            + $"\"actionId\":\"{WebSocketGateway.EscapeJson(item.ActionId)}\","
            + $"\"kind\":\"{WebSocketGateway.EscapeJson(item.Kind)}\","
            + $"\"target\":\"{WebSocketGateway.EscapeJson(item.Target)}\","
            + $"\"description\":\"{WebSocketGateway.EscapeJson(item.Description)}\","
            + $"\"autoApply\":{(item.AutoApply ? "true" : "false")},"
            + $"\"status\":\"{WebSocketGateway.EscapeJson(item.Status)}\","
            + $"\"error\":\"{WebSocketGateway.EscapeJson(item.Error ?? string.Empty)}\""
            + "}"
        ));
        return "{"
            + "\"type\":\"doctor_fix_result\","
            + $"\"action\":\"{WebSocketGateway.EscapeJson(action)}\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{WebSocketGateway.EscapeJson(result.Message)}\","
            + $"\"previewId\":\"{WebSocketGateway.EscapeJson(result.PreviewId)}\","
            + $"\"error\":\"{WebSocketGateway.EscapeJson(result.Error ?? string.Empty)}\","
            + $"\"actions\":[{actionsJson}]"
            + "}";
    }
}
