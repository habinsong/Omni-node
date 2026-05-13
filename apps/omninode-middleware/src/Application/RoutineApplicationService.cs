namespace OmniNode.Middleware;

public sealed class RoutineApplicationService : IRoutineApplicationService
{
    private readonly CommandService _inner;

    public RoutineApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<RoutineSummary> ListRoutines() => _inner.ListRoutines();
    public RoutineSchedulerStatus GetRoutineSchedulerStatus() => _inner.GetRoutineSchedulerStatus();
    public RoutineExecutionPreviewResult PreviewRoutine(string request, string? executionMode, string? scheduleSourceMode, string? scheduleKind, string? scheduleTime, IReadOnlyList<int>? weekdays, int? dayOfMonth, string? timezoneId) => _inner.PreviewRoutine(request, executionMode, scheduleSourceMode, scheduleKind, scheduleTime, weekdays, dayOfMonth, timezoneId);
    public Task<RoutineActionResult> CreateRoutineAsync(string request, string source, CancellationToken cancellationToken, Action<RoutineProgressUpdate>? progressCallback = null) => _inner.CreateRoutineAsync(request, source, cancellationToken, progressCallback);
    public Task<RoutineActionResult> CreateRoutineAsync(string request, string? title, string? executionMode, string? agentProvider, string? agentModel, string? agentStartUrl, int? agentTimeoutSeconds, string? agentToolProfile, bool? agentUsePlaywright, string? scheduleSourceMode, int? maxRetries, int? retryDelaySeconds, string? notifyPolicy, bool? notifyTelegram, string? scheduleKind, string? scheduleTime, IReadOnlyList<int>? weekdays, int? dayOfMonth, string? timezoneId, bool runImmediately, string source, CancellationToken cancellationToken, Action<RoutineProgressUpdate>? progressCallback = null) => _inner.CreateRoutineAsync(request, title, executionMode, agentProvider, agentModel, agentStartUrl, agentTimeoutSeconds, agentToolProfile, agentUsePlaywright, scheduleSourceMode, maxRetries, retryDelaySeconds, notifyPolicy, notifyTelegram, scheduleKind, scheduleTime, weekdays, dayOfMonth, timezoneId, runImmediately, source, cancellationToken, progressCallback);
    public Task<RoutineActionResult> UpdateRoutineAsync(string routineId, string request, string? title, string? executionMode, string? agentProvider, string? agentModel, string? agentStartUrl, int? agentTimeoutSeconds, string? agentToolProfile, bool? agentUsePlaywright, string? scheduleSourceMode, int? maxRetries, int? retryDelaySeconds, string? notifyPolicy, bool? notifyTelegram, string? scheduleKind, string? scheduleTime, IReadOnlyList<int>? weekdays, int? dayOfMonth, string? timezoneId, CancellationToken cancellationToken, Action<RoutineProgressUpdate>? progressCallback = null) => _inner.UpdateRoutineAsync(routineId, request, title, executionMode, agentProvider, agentModel, agentStartUrl, agentTimeoutSeconds, agentToolProfile, agentUsePlaywright, scheduleSourceMode, maxRetries, retryDelaySeconds, notifyPolicy, notifyTelegram, scheduleKind, scheduleTime, weekdays, dayOfMonth, timezoneId, cancellationToken, progressCallback);
    public Task<RoutineActionResult> RunRoutineNowAsync(string routineId, string source, CancellationToken cancellationToken) => _inner.RunRoutineNowAsync(routineId, source, cancellationToken);
    public RoutineRunDetailResult GetRoutineRunDetail(string routineId, long ts) => _inner.GetRoutineRunDetail(routineId, ts);
    public Task<RoutineActionResult> ResendRoutineRunToTelegramAsync(string routineId, long ts, CancellationToken cancellationToken) => _inner.ResendRoutineRunToTelegramAsync(routineId, ts, cancellationToken);
    public RoutineActionResult SetRoutineEnabled(string routineId, bool enabled) => _inner.SetRoutineEnabled(routineId, enabled);
    public RoutineActionResult DeleteRoutine(string routineId) => _inner.DeleteRoutine(routineId);
}
