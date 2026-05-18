using System.Text;
namespace OmniNode.Middleware;

public sealed class BackgroundTaskCoordinator
{
    private const int MaxParallelBackgroundTasks = 3;

    private readonly TaskGraphService _taskGraphService;
    private readonly IStatePathResolver _pathResolver;
    private readonly object _gate = new();
    private readonly Dictionary<string, GraphRunState> _runs = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _workspaceLane = new(1, 1);

    private ICodingApplicationService? _codingService;
    private ICommandExecutionService? _commandExecutionService;

    private sealed class GraphRunState
    {
        public GraphRunState(string graphId)
        {
            GraphId = graphId;
        }

        public string GraphId { get; }
        public CancellationTokenSource CancellationSource { get; } = new();
        public List<TaskGraphEventSink> Subscribers { get; } = new();
        public Dictionary<string, CancellationTokenSource> TaskTokens { get; } = new(StringComparer.Ordinal);
        public Task? LoopTask { get; set; }
    }

    private sealed record RunningTaskHandle(
        string Category,
        Task Task
    );

    private sealed record TaskExecutionOutcome(
        string Status,
        string ExecutorKind,
        string? ConversationId,
        string OutputSummary,
        string? ArtifactPath,
        string ResultJson
    );

    public BackgroundTaskCoordinator(TaskGraphService taskGraphService, IStatePathResolver pathResolver)
    {
        _taskGraphService = taskGraphService;
        _pathResolver = pathResolver;
    }

    public void ConfigureExecutors(
        ICodingApplicationService codingService,
        ICommandExecutionService commandExecutionService
    )
    {
        _codingService = codingService;
        _commandExecutionService = commandExecutionService;
    }

    public Task<TaskGraphActionResult> RunGraphAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        return RunGraphInternalAsync(graphId, source, eventSink, resetAll: true, cancellationToken);
    }

    public Task<TaskGraphActionResult> ResumeGraphAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        return RunGraphInternalAsync(graphId, source, eventSink, resetAll: false, cancellationToken);
    }

    private Task<TaskGraphActionResult> RunGraphInternalAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        bool resetAll,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken;
        if (_codingService == null || _commandExecutionService == null)
        {
            return Task.FromResult(new TaskGraphActionResult(false, "Task graph 실행기가 아직 준비되지 않았습니다.", null));
        }

        var current = _taskGraphService.GetGraph(graphId);
        if (current == null)
        {
            return Task.FromResult(new TaskGraphActionResult(false, "Task graph를 찾을 수 없습니다.", null));
        }

        lock (_gate)
        {
            if (_runs.TryGetValue(current.Graph.GraphId, out var existing))
            {
                if (eventSink != null)
                {
                    existing.Subscribers.Add(eventSink);
                }

                return Task.FromResult(new TaskGraphActionResult(true, "이미 실행 중인 Task graph에 연결했습니다.", current));
            }
        }

        var prepared = resetAll
            ? _taskGraphService.PrepareForRun(current.Graph.GraphId)
            : _taskGraphService.PrepareForResume(current.Graph.GraphId);
        var runState = new GraphRunState(prepared.Graph.GraphId);
        if (eventSink != null)
        {
            runState.Subscribers.Add(eventSink);
        }

        lock (_gate)
        {
            _runs[prepared.Graph.GraphId] = runState;
            runState.LoopTask = Task.Run(() => ExecuteGraphLoopAsync(prepared.Graph.GraphId, source, runState));
        }

        return Task.FromResult(new TaskGraphActionResult(true, "Task graph 실행을 시작했습니다.", prepared));
    }

    public TaskGraphActionResult CancelTask(string graphId, string taskId)
    {
        var snapshot = _taskGraphService.GetGraph(graphId);
        if (snapshot == null)
        {
            return new TaskGraphActionResult(false, "Task graph를 찾을 수 없습니다.", null);
        }

        GraphRunState? runState;
        lock (_gate)
        {
            _runs.TryGetValue(snapshot.Graph.GraphId, out runState);
            if (runState != null && runState.TaskTokens.TryGetValue(taskId, out var taskToken))
            {
                taskToken.Cancel();
                return new TaskGraphActionResult(true, "실행 중인 작업에 취소 요청을 전달했습니다.", snapshot);
            }
        }

        try
        {
            var updated = _taskGraphService.CancelPendingTask(snapshot.Graph.GraphId, taskId);
            var node = updated.Graph.Nodes.FirstOrDefault(item => item.TaskId.Equals(taskId, StringComparison.Ordinal));
            if (runState != null && node != null)
            {
                _ = Task.Run(() => EmitTaskUpdatedAsync(runState, updated.Graph.GraphId, node, CancellationToken.None));
            }

            return new TaskGraphActionResult(true, "대기 중인 작업을 취소했습니다.", updated);
        }
        catch (Exception ex)
        {
            return new TaskGraphActionResult(false, $"작업 취소 실패: {ex.Message}", snapshot);
        }
    }

    public async Task StopAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            foreach (var run in _runs.Values)
            {
                run.CancellationSource.Cancel();
            }

            tasks = _runs.Values
                .Select(run => run.LoopTask)
                .Where(task => task != null)
                .Cast<Task>()
                .ToArray();
        }

        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }
    }

    private async Task ExecuteGraphLoopAsync(string graphId, string source, GraphRunState runState)
    {
        var inflight = new Dictionary<string, RunningTaskHandle>(StringComparer.Ordinal);

        try
        {
            while (!runState.CancellationSource.IsCancellationRequested)
            {
                foreach (var completed in inflight.Where(item => item.Value.Task.IsCompleted).Select(item => item.Key).ToArray())
                {
                    var handle = inflight[completed];
                    inflight.Remove(completed);
                    try
                    {
                        await handle.Task;
                    }
                    catch
                    {
                    }
                }

                var before = _taskGraphService.GetGraph(graphId);
                if (before == null)
                {
                    break;
                }

                var snapshot = _taskGraphService.UpdateReadiness(graphId);
                await EmitSnapshotDiffAsync(runState, before, snapshot, runState.CancellationSource.Token);

                if (IsTerminal(snapshot.Graph.Status) && inflight.Count == 0)
                {
                    break;
                }

                var workspaceBusy = inflight.Values.Any(item => IsWorkspaceExclusive(item.Category));
                var runningBackgroundCount = inflight.Values.Count(item => !IsWorkspaceExclusive(item.Category));
                var readyNodes = snapshot.Graph.Nodes
                    .Where(node => node.Status == TaskNodeStatus.Pending && !inflight.ContainsKey(node.TaskId))
                    .OrderBy(node => node.TaskId, StringComparer.Ordinal)
                    .ToArray();

                foreach (var node in readyNodes)
                {
                    if (IsWorkspaceExclusive(node.Category))
                    {
                        if (workspaceBusy)
                        {
                            continue;
                        }

                        workspaceBusy = true;
                    }
                    else
                    {
                        if (runningBackgroundCount >= MaxParallelBackgroundTasks)
                        {
                            continue;
                        }

                        runningBackgroundCount += 1;
                    }

                    inflight[node.TaskId] = new RunningTaskHandle(
                        node.Category,
                        ExecuteNodeAsync(graphId, node, source, runState)
                    );
                }

                if (inflight.Count == 0)
                {
                    var current = _taskGraphService.GetGraph(graphId);
                    if (current == null || IsTerminal(current.Graph.Status))
                    {
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), runState.CancellationSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            foreach (var handle in inflight.Values)
            {
                try
                {
                    await handle.Task;
                }
                catch
                {
                }
            }

            FinalizeGraphShutdown(graphId);
            lock (_gate)
            {
                _runs.Remove(graphId);
            }

            runState.CancellationSource.Dispose();
        }
    }

    private async Task ExecuteNodeAsync(string graphId, TaskNode node, string source, GraphRunState runState)
    {
        var taskCts = new CancellationTokenSource();
        lock (_gate)
        {
            runState.TaskTokens[node.TaskId] = taskCts;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            runState.CancellationSource.Token,
            taskCts.Token
        );
        var token = linkedCts.Token;
        var runtimePath = _pathResolver.GetTaskRuntimePath(graphId, node.TaskId);
        Directory.CreateDirectory(runtimePath);
        var stdoutPath = Path.Combine(runtimePath, "stdout.log");
        var stderrPath = Path.Combine(runtimePath, "stderr.log");
        var resultPath = Path.Combine(runtimePath, "result.json");
        File.WriteAllText(stdoutPath, string.Empty, Encoding.UTF8);
        File.WriteAllText(stderrPath, string.Empty, Encoding.UTF8);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var startRecord = new TaskExecutionRecord(
            graphId,
            node.TaskId,
            node.Title,
            node.Category,
            startedAtUtc,
            null,
            "running",
            ResolveExecutorKind(node.Category),
            source,
            runtimePath,
            stdoutPath,
            stderrPath,
            resultPath,
            null,
            null,
            null,
            null
        );
        var runningSnapshot = _taskGraphService.UpdateTaskState(
            graphId,
            node.TaskId,
            current => current with
            {
                Status = TaskNodeStatus.Running,
                Error = null,
                OutputSummary = null,
                ArtifactPath = null,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = null
            },
            executions => UpsertExecution(executions, startRecord)
        );
        await EmitTaskUpdatedAsync(runState, graphId, FindNode(runningSnapshot, node.TaskId), token);
        await AppendLogAsync(runState, graphId, node.TaskId, stdoutPath, $"started {node.Category}", token);

        try
        {
            TaskExecutionOutcome outcome;
            if (IsWorkspaceExclusive(node.Category))
            {
                await _workspaceLane.WaitAsync(token);
                try
                {
                    outcome = await ExecuteWorkspaceTaskAsync(node, source, stdoutPath, token, runtimePath);
                }
                finally
                {
                    _workspaceLane.Release();
                }
            }
            else
            {
                outcome = await ExecuteBackgroundTaskAsync(node, source, stdoutPath, token, runtimePath);
            }

            await File.WriteAllTextAsync(resultPath, outcome.ResultJson, Encoding.UTF8, token);
            var completedAtUtc = DateTimeOffset.UtcNow;
            var completedRecord = startRecord with
            {
                CompletedAtUtc = completedAtUtc,
                Status = outcome.Status,
                ConversationId = outcome.ConversationId,
                OutputSummary = outcome.OutputSummary,
                ArtifactPath = outcome.ArtifactPath
            };
            var completedSnapshot = _taskGraphService.UpdateTaskState(
                graphId,
                node.TaskId,
                current => current with
                {
                    Status = outcome.Status == "ok" ? TaskNodeStatus.Completed : TaskNodeStatus.Failed,
                    OutputSummary = outcome.OutputSummary,
                    ArtifactPath = outcome.ArtifactPath,
                    Error = outcome.Status == "ok" ? null : outcome.OutputSummary,
                    CompletedAtUtc = completedAtUtc
                },
                executions => UpsertExecution(executions, completedRecord)
            );
            await AppendLogAsync(
                runState,
                graphId,
                node.TaskId,
                outcome.Status == "ok" ? stdoutPath : stderrPath,
                outcome.Status == "ok" ? "completed" : $"failed {outcome.OutputSummary}",
                token
            );
            await EmitTaskUpdatedAsync(runState, graphId, FindNode(completedSnapshot, node.TaskId), token);
        }
        catch (OperationCanceledException)
        {
            var canceledAtUtc = DateTimeOffset.UtcNow;
            var canceledRecord = startRecord with
            {
                CompletedAtUtc = canceledAtUtc,
                Status = "canceled",
                Error = "작업이 취소되었습니다."
            };
            var canceledSnapshot = _taskGraphService.UpdateTaskState(
                graphId,
                node.TaskId,
                current => current with
                {
                    Status = TaskNodeStatus.Canceled,
                    Error = "작업이 취소되었습니다.",
                    CompletedAtUtc = canceledAtUtc
                },
                executions => UpsertExecution(executions, canceledRecord)
            );
            await AppendLogAsync(runState, graphId, node.TaskId, stderrPath, "canceled", CancellationToken.None);
            await EmitTaskUpdatedAsync(runState, graphId, FindNode(canceledSnapshot, node.TaskId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            var failedAtUtc = DateTimeOffset.UtcNow;
            var failedMessage = TrimText(ex.Message, 800);
            await File.WriteAllTextAsync(
                resultPath,
                BuildFailedResultJson(failedMessage),
                Encoding.UTF8,
                CancellationToken.None
            );
            var failedRecord = startRecord with
            {
                CompletedAtUtc = failedAtUtc,
                Status = "error",
                Error = failedMessage
            };
            var failedSnapshot = _taskGraphService.UpdateTaskState(
                graphId,
                node.TaskId,
                current => current with
                {
                    Status = TaskNodeStatus.Failed,
                    Error = failedMessage,
                    CompletedAtUtc = failedAtUtc
                },
                executions => UpsertExecution(executions, failedRecord)
            );
            await AppendLogAsync(runState, graphId, node.TaskId, stderrPath, $"exception {failedMessage}", CancellationToken.None);
            await EmitTaskUpdatedAsync(runState, graphId, FindNode(failedSnapshot, node.TaskId), CancellationToken.None);
        }
        finally
        {
            lock (_gate)
            {
                if (_runs.TryGetValue(graphId, out var active))
                {
                    if (active.TaskTokens.Remove(node.TaskId, out var registeredToken))
                    {
                        registeredToken.Dispose();
                    }
                }
                else
                {
                    taskCts.Dispose();
                }
            }
        }
    }

    private async Task<TaskExecutionOutcome> ExecuteWorkspaceTaskAsync(
        TaskNode node,
        string source,
        string stdoutPath,
        CancellationToken cancellationToken,
        string runtimePath
    )
    {
        if (_codingService == null)
        {
            throw new InvalidOperationException("coding executor is not configured");
        }

        var effectiveSource = NormalizeExecutionSource(source);
        var result = await _codingService.RunCodingOrchestrationAsync(
            new CodingRunRequest(
                node.Prompt,
                effectiveSource,
                "coding",
                "orchestration",
                null,
                $"[task] {node.Title}",
                "task-graph",
                node.Category,
                new[] { "task-graph", node.TaskId },
                null,
                null,
                "text",
                null
            ),
            cancellationToken,
            progress =>
            {
                var line = string.IsNullOrWhiteSpace(progress.Message)
                    ? $"{progress.Phase} {progress.StageTitle} {progress.StageDetail}".Trim()
                    : progress.Message;
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                AppendPlainLog(stdoutPath, $"progress {TrimText(line, 400)}");
            }
        );

        var summary = TrimText(result.Summary, 1600);
        var artifactPath = result.ChangedFiles.FirstOrDefault();
        return new TaskExecutionOutcome(
            IsSuccessfulExecutionStatus(result.Execution.Status) ? "ok" : "error",
            "coding_orchestration",
            result.ConversationId,
            summary,
            artifactPath,
            BuildCodingResultJson(result, summary, runtimePath)
        );
    }

    private async Task<TaskExecutionOutcome> ExecuteBackgroundTaskAsync(
        TaskNode node,
        string source,
        string stdoutPath,
        CancellationToken cancellationToken,
        string runtimePath
    )
    {
        if (_commandExecutionService == null)
        {
            throw new InvalidOperationException("command executor is not configured");
        }

        var effectiveSource = NormalizeExecutionSource(source);
        var output = await _commandExecutionService.ExecuteAsync(
            node.Prompt,
            effectiveSource,
            cancellationToken
        );
        var summary = TrimText(output, 1600);
        AppendPlainLog(stdoutPath, summary);
        return new TaskExecutionOutcome(
            "ok",
            "command_execution",
            null,
            summary,
            runtimePath,
            BuildCommandResultJson(summary, runtimePath)
        );
    }

    private void FinalizeGraphShutdown(string graphId)
    {
        var snapshot = _taskGraphService.GetGraph(graphId);
        if (snapshot == null)
        {
            return;
        }

        if (!IsTerminal(snapshot.Graph.Status))
        {
            foreach (var node in snapshot.Graph.Nodes)
            {
                if (node.Status == TaskNodeStatus.Pending || node.Status == TaskNodeStatus.Blocked)
                {
                    try
                    {
                        _taskGraphService.CancelPendingTask(graphId, node.TaskId, "애플리케이션 종료로 취소되었습니다.");
                    }
                    catch
                    {
                    }
                }
            }
        }

        _ = _taskGraphService.UpdateReadiness(graphId);
    }

    private static IReadOnlyList<TaskExecutionRecord> UpsertExecution(
        IReadOnlyList<TaskExecutionRecord> executions,
        TaskExecutionRecord nextRecord
    )
    {
        var next = executions
            .Where(item => !item.TaskId.Equals(nextRecord.TaskId, StringComparison.Ordinal))
            .ToList();
        next.Add(nextRecord);
        return next
            .OrderBy(item => item.StartedAtUtc)
            .ToArray();
    }

    private async Task EmitSnapshotDiffAsync(
        GraphRunState runState,
        TaskGraphSnapshot before,
        TaskGraphSnapshot after,
        CancellationToken cancellationToken
    )
    {
        var beforeById = before.Graph.Nodes.ToDictionary(node => node.TaskId, StringComparer.Ordinal);
        foreach (var node in after.Graph.Nodes)
        {
            if (beforeById.TryGetValue(node.TaskId, out var previous)
                && string.Equals(
                    TaskGraphJson.Serialize(previous),
                    TaskGraphJson.Serialize(node),
                    StringComparison.Ordinal))
            {
                continue;
            }

            await EmitTaskUpdatedAsync(runState, after.Graph.GraphId, node, cancellationToken);
        }
    }

    private async Task EmitTaskUpdatedAsync(
        GraphRunState runState,
        string graphId,
        TaskNode node,
        CancellationToken cancellationToken
    )
    {
        foreach (var sink in runState.Subscribers.ToArray())
        {
            if (sink.OnTaskUpdatedAsync == null)
            {
                continue;
            }

            try
            {
                await sink.OnTaskUpdatedAsync(graphId, node, cancellationToken);
            }
            catch
            {
            }
        }
    }

    private async Task AppendLogAsync(
        GraphRunState runState,
        string graphId,
        string taskId,
        string path,
        string line,
        CancellationToken cancellationToken
    )
    {
        AppendPlainLog(path, line);
        foreach (var sink in runState.Subscribers.ToArray())
        {
            if (sink.OnTaskLogAsync == null)
            {
                continue;
            }

            try
            {
                await sink.OnTaskLogAsync(graphId, taskId, line, cancellationToken);
            }
            catch
            {
            }
        }
    }

    private static void AppendPlainLog(string path, string line)
    {
        var text = $"[{DateTimeOffset.UtcNow:O}] {line}{Environment.NewLine}";
        File.AppendAllText(path, text, Encoding.UTF8);
    }

    private static TaskNode FindNode(TaskGraphSnapshot snapshot, string taskId)
    {
        return snapshot.Graph.Nodes.First(node => node.TaskId.Equals(taskId, StringComparison.Ordinal));
    }

    private static bool IsWorkspaceExclusive(string category)
    {
        return category == "coding"
            || category == "refactor"
            || category == "documentation"
            || category == "verification";
    }

    private static bool IsTerminal(TaskGraphStatus status)
    {
        return status == TaskGraphStatus.Completed
            || status == TaskGraphStatus.Failed
            || status == TaskGraphStatus.Canceled;
    }

    private static string ResolveExecutorKind(string category)
    {
        return IsWorkspaceExclusive(category) ? "coding_orchestration" : "command_execution";
    }

    private static string NormalizeExecutionSource(string source)
    {
        return source.Equals("telegram", StringComparison.OrdinalIgnoreCase) ? "web" : "web";
    }

    private static bool IsSuccessfulExecutionStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "ok" || normalized == "success";
    }

    private static string TrimText(string value, int maxChars)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars] + "...";
    }

    private static string BuildFailedResultJson(string error)
    {
        return "{\n"
            + "  \"ok\": false,\n"
            + $"  \"error\": \"{WebSocketGateway.EscapeJson(error)}\"\n"
            + "}";
    }

    private static string BuildCodingResultJson(CodingRunResult result, string summary, string runtimePath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine($"  \"ok\": {(IsSuccessfulExecutionStatus(result.Execution.Status) ? "true" : "false")},");
        builder.AppendLine($"  \"mode\": \"{WebSocketGateway.EscapeJson(result.Mode)}\",");
        builder.AppendLine($"  \"provider\": \"{WebSocketGateway.EscapeJson(result.Provider)}\",");
        builder.AppendLine($"  \"model\": \"{WebSocketGateway.EscapeJson(result.Model)}\",");
        builder.AppendLine($"  \"conversationId\": \"{WebSocketGateway.EscapeJson(result.ConversationId)}\",");
        builder.AppendLine("  \"changedFiles\": [");
        for (var i = 0; i < result.ChangedFiles.Count; i += 1)
        {
            var suffix = i == result.ChangedFiles.Count - 1 ? string.Empty : ",";
            builder.AppendLine($"    \"{WebSocketGateway.EscapeJson(result.ChangedFiles[i])}\"{suffix}");
        }

        builder.AppendLine("  ],");
        builder.AppendLine("  \"execution\": {");
        builder.AppendLine($"    \"status\": \"{WebSocketGateway.EscapeJson(result.Execution.Status)}\",");
        builder.AppendLine($"    \"exitCode\": {result.Execution.ExitCode},");
        builder.AppendLine($"    \"command\": \"{WebSocketGateway.EscapeJson(result.Execution.Command)}\",");
        builder.AppendLine($"    \"runDirectory\": \"{WebSocketGateway.EscapeJson(result.Execution.RunDirectory)}\"");
        builder.AppendLine("  },");
        builder.AppendLine($"  \"summary\": \"{WebSocketGateway.EscapeJson(summary)}\",");
        builder.AppendLine($"  \"runtimePath\": \"{WebSocketGateway.EscapeJson(runtimePath)}\"");
        builder.Append('}');
        return builder.ToString();
    }

    private static string BuildCommandResultJson(string summary, string runtimePath)
    {
        return "{\n"
            + "  \"ok\": true,\n"
            + "  \"executor\": \"command_execution\",\n"
            + $"  \"output\": \"{WebSocketGateway.EscapeJson(summary)}\",\n"
            + $"  \"runtimePath\": \"{WebSocketGateway.EscapeJson(runtimePath)}\"\n"
            + "}";
    }
}
