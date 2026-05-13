using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private const string LogicGraphExecutionMode = "logic_graph";
    private const string LogicGraphSchemaVersion = "logic.graph.v1";
    private const string LogicRunSnapshotFileName = "snapshot.json";
    private const string LogicRunEventsFileName = "events.log";
    private const string LogicResolvedMainInputKey = "__main_input";
    private const int LogicRunMaxLogs = 512;
    private static readonly Regex LogicTemplateRegex = new(@"\{\{\s*(?<expr>[^{}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly IReadOnlySet<string> LogicSupportedNodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "start",
        "end",
        "output",
        "if",
        "delay",
        "parallel_split",
        "parallel_join",
        "set_var",
        "template",
        "chat_single",
        "chat_orchestration",
        "chat_multi",
        "coding_single",
        "coding_orchestration",
        "coding_multi",
        "routine_run",
        "memory_search",
        "memory_get",
        "web_search",
        "web_fetch",
        "file_read",
        "file_write",
        "session_list",
        "session_spawn",
        "session_send",
        "cron_status",
        "cron_run",
        "browser_execute",
        "canvas_execute",
        "nodes_pending",
        "nodes_invoke",
        "telegram_stub"
    };
    private static readonly IReadOnlySet<string> LogicSupportedOperators = new HashSet<string>(StringComparer.Ordinal)
    {
        "equals",
        "not_equals",
        "contains",
        "not_contains",
        "starts_with",
        "ends_with",
        "gt",
        "gte",
        "lt",
        "lte",
        "is_truthy",
        "is_falsy"
    };
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> LogicBindableTargetPortsByType =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["end"] = new HashSet<string>(StringComparer.Ordinal) { "result" },
            ["output"] = new HashSet<string>(StringComparer.Ordinal) { "result" },
            ["if"] = new HashSet<string>(StringComparer.Ordinal) { "leftref" },
            ["set_var"] = new HashSet<string>(StringComparer.Ordinal) { "value" },
            ["template"] = new HashSet<string>(StringComparer.Ordinal) { "template" },
            ["chat_single"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["chat_orchestration"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["chat_multi"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["coding_single"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["coding_orchestration"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["coding_multi"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["routine_run"] = new HashSet<string>(StringComparer.Ordinal) { "task" },
            ["memory_search"] = new HashSet<string>(StringComparer.Ordinal) { "query" },
            ["web_search"] = new HashSet<string>(StringComparer.Ordinal) { "query" },
            ["file_write"] = new HashSet<string>(StringComparer.Ordinal) { "content" },
            ["session_send"] = new HashSet<string>(StringComparer.Ordinal) { "message" },
            ["telegram_stub"] = new HashSet<string>(StringComparer.Ordinal) { "text" }
        };
    private static readonly IReadOnlyDictionary<string, string> LogicImplicitMainInputPortByType =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["end"] = "result",
            ["output"] = "result",
            ["if"] = "leftRef",
            ["set_var"] = "value",
            ["template"] = "template",
            ["chat_single"] = "input",
            ["chat_orchestration"] = "input",
            ["chat_multi"] = "input",
            ["coding_single"] = "input",
            ["coding_orchestration"] = "input",
            ["coding_multi"] = "input",
            ["memory_search"] = "query",
            ["web_search"] = "query",
            ["file_write"] = "content",
            ["session_spawn"] = "task",
            ["session_send"] = "message",
            ["telegram_stub"] = "text"
        };

    private IStatePathResolver? _logicPathResolver;
    private LogicGraphRuntimeCoordinator? _logicRuntimeCoordinator;

    private sealed class MutableLogicNodeState
    {
        public string NodeId { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Status { get; set; } = "pending";
        public string? Error { get; set; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public LogicNodeResultEnvelope? Result { get; set; }
    }

    private sealed record LogicNodeExecutionOutcome(
        LogicNodeResultEnvelope Envelope,
        string? Branch = null
    );

    private sealed class LogicExecutionContext
    {
        public string RunInput { get; init; } = string.Empty;
        public string RunDirectory { get; init; } = string.Empty;
        public Dictionary<string, string> Vars { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, LogicNodeResultEnvelope> Nodes { get; } = new(StringComparer.Ordinal);
        public List<string> Artifacts { get; } = new();
        public Dictionary<string, string> Sessions { get; } = new(StringComparer.Ordinal);
    }

    internal void ConfigureLogicGraphRuntime(
        IStatePathResolver pathResolver,
        LogicGraphRuntimeCoordinator logicRuntimeCoordinator
    )
    {
        _logicPathResolver = pathResolver;
        _logicRuntimeCoordinator = logicRuntimeCoordinator;
        _logicRuntimeCoordinator.ConfigureExecutor(ExecuteLogicGraphRunCoreAsync);
    }

    public LogicGraphListResult ListLogicGraphs()
    {
        lock (_routineLock)
        {
            var items = _routinesById.Values
                .Where(IsLogicGraphRoutine)
                .Select(ToLogicGraphSummaryWithRuntime)
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.GraphId, StringComparer.Ordinal)
                .ToArray();
            return new LogicGraphListResult(items);
        }
    }

    public LogicGraphActionResult GetLogicGraph(string graphId)
    {
        var normalizedGraphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedGraphId))
        {
            return new LogicGraphActionResult(false, "graphId가 필요합니다.", null, null);
        }

        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(normalizedGraphId, out var routine) || !IsLogicGraphRoutine(routine))
            {
                return new LogicGraphActionResult(false, "작업 흐름을 찾을 수 없습니다.", null, null);
            }

            return new LogicGraphActionResult(
                true,
                "작업 흐름을 불러왔습니다.",
                ToLogicGraphSummaryWithRuntime(routine),
                routine.LogicGraph
            );
        }
    }

    public async Task<LogicGraphActionResult> SaveLogicGraphAsync(
        string? graphId,
        string logicGraphJson,
        string source,
        CancellationToken cancellationToken
    )
    {
        _ = source;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var rawJson = (logicGraphJson ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new LogicGraphActionResult(false, "logicGraph JSON이 필요합니다.", null, null);
        }

        LogicGraphDefinition? parsedGraph;
        try
        {
            parsedGraph = LogicGraphJson.DeserializeDefinition(rawJson);
        }
        catch (Exception ex)
        {
            return new LogicGraphActionResult(false, $"작업 흐름 JSON을 읽는 중 오류가 났습니다: {ex.Message}", null, null);
        }

        if (parsedGraph == null)
        {
            return new LogicGraphActionResult(false, "작업 흐름 JSON을 이해하지 못했습니다.", null, null);
        }

        LogicGraphDefinition normalizedGraph;
        try
        {
            normalizedGraph = NormalizeLogicGraphDefinition(parsedGraph, graphId);
        }
        catch (Exception ex)
        {
            return new LogicGraphActionResult(false, $"작업 흐름 형식을 정리하는 중 오류가 났습니다: {ex.Message}", null, parsedGraph);
        }
        var validation = ValidateLogicGraph(normalizedGraph);
        if (!validation.Ok)
        {
            return new LogicGraphActionResult(false, validation.Message, null, normalizedGraph);
        }

        if (!TryBuildLogicScheduleConfig(normalizedGraph, out var scheduleConfig, out var scheduleError))
        {
            return new LogicGraphActionResult(false, scheduleError, null, normalizedGraph);
        }

        var now = DateTimeOffset.UtcNow;
        RoutineDefinition routine;
        lock (_routineLock)
        {
            _routinesById.TryGetValue(normalizedGraph.GraphId, out var existing);
            if (existing != null && existing.Running)
            {
                return new LogicGraphActionResult(
                    false,
                    "지금 실행 중인 작업 흐름은 저장할 수 없습니다.",
                    ToLogicGraphSummaryWithRuntime(existing),
                    existing.LogicGraph
                );
            }

            routine = existing ?? new RoutineDefinition
            {
                Id = normalizedGraph.GraphId,
                CreatedUtc = now
            };

            routine.Id = normalizedGraph.GraphId;
            routine.Title = normalizedGraph.Title;
            routine.Request = ResolveLogicGraphRequestText(normalizedGraph);
            routine.ExecutionMode = LogicGraphExecutionMode;
            routine.ScheduleText = scheduleConfig.Display;
            routine.ScheduleSourceMode = NormalizeRoutineScheduleSourceMode(
                normalizedGraph.Schedule.ScheduleSourceMode,
                routine.Request
            );
            routine.TimezoneId = scheduleConfig.TimezoneId;
            routine.Hour = scheduleConfig.Hour;
            routine.Minute = scheduleConfig.Minute;
            routine.Enabled = normalizedGraph.Enabled && normalizedGraph.Schedule.Enabled;
            routine.LastStatus = string.IsNullOrWhiteSpace(routine.LastStatus) ? "saved" : routine.LastStatus;
            routine.LastOutput = string.IsNullOrWhiteSpace(routine.LastOutput) ? "작업 흐름을 저장했습니다." : routine.LastOutput;
            routine.ScriptPath = string.Empty;
            routine.Language = LogicGraphExecutionMode;
            routine.Code = string.Empty;
            routine.Planner = "logic_graph";
            routine.PlannerModel = LogicGraphSchemaVersion;
            routine.CoderModel = LogicGraphSchemaVersion;
            routine.NotifyTelegram = false;
            routine.NotifyPolicy = "never";
            routine.MaxRetries = 0;
            routine.RetryDelaySeconds = 0;
            routine.LogicGraph = normalizedGraph;
            routine.CronScheduleKind = "cron";
            routine.CronScheduleExpr = scheduleConfig.CronExpr;
            routine.CronScheduleAtMs = null;
            routine.CronScheduleEveryMs = null;
            routine.CronScheduleAnchorMs = null;
            routine.NextRunUtc = ComputeNextCronBridgeRunUtc(routine, now);

            _routinesById[routine.Id] = routine;
            SaveRoutineStateLocked();
        }

        return new LogicGraphActionResult(
            true,
            "작업 흐름을 저장했습니다.",
            ToLogicGraphSummaryWithRuntime(routine),
            normalizedGraph
        );
    }

    public LogicGraphActionResult DeleteLogicGraph(string graphId)
    {
        var normalizedGraphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedGraphId))
        {
            return new LogicGraphActionResult(false, "graphId가 필요합니다.", null, null);
        }

        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(normalizedGraphId, out var routine) || !IsLogicGraphRoutine(routine))
            {
                return new LogicGraphActionResult(false, "작업 흐름을 찾을 수 없습니다.", null, null);
            }

            if (routine.Running)
            {
                return new LogicGraphActionResult(
                    false,
                    "지금 실행 중인 작업 흐름은 삭제할 수 없습니다.",
                    ToLogicGraphSummaryWithRuntime(routine),
                    routine.LogicGraph
                );
            }

            _routinesById.Remove(normalizedGraphId);
            SaveRoutineStateLocked();
            return new LogicGraphActionResult(true, "작업 흐름을 삭제했습니다.", null, null);
        }
    }

    public Task<LogicRunActionResult> RunLogicGraphAsync(
        string graphId,
        string source,
        string? runInput,
        Action<LogicRunEvent>? eventCallback,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken;
        var normalizedGraphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedGraphId))
        {
            return Task.FromResult(new LogicRunActionResult(false, "graphId가 필요합니다.", null, null));
        }

        if (_logicRuntimeCoordinator == null)
        {
            return Task.FromResult(new LogicRunActionResult(false, "흐름 실행 기능이 아직 준비되지 않았습니다.", null, null));
        }

        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(normalizedGraphId, out var routine) || !IsLogicGraphRoutine(routine))
            {
                return Task.FromResult(new LogicRunActionResult(false, "작업 흐름을 찾을 수 없습니다.", null, null));
            }

            if (routine.Running)
            {
                var active = _logicRuntimeCoordinator.GetSnapshotByGraphId(normalizedGraphId);
                if (active == null || IsTerminalLogicStatus(active.Status))
                {
                    routine.Running = false;
                    SaveRoutineStateLocked();
                }
                else
                {
                return Task.FromResult(new LogicRunActionResult(
                    false,
                    "이미 이 작업 흐름이 실행 중입니다.",
                    active?.RunId,
                    active
                ));
                }
            }

            routine.Running = true;
            SaveRoutineStateLocked();
        }

        var result = _logicRuntimeCoordinator.RunGraph(
            normalizedGraphId,
            string.IsNullOrWhiteSpace(source) ? "web" : source,
            runInput,
            eventCallback
        );

        if (!result.Ok)
        {
            lock (_routineLock)
            {
                if (_routinesById.TryGetValue(normalizedGraphId, out var routine))
                {
                    routine.Running = false;
                    SaveRoutineStateLocked();
                }
            }
        }

        return Task.FromResult(result);
    }

    public LogicRunActionResult CancelLogicGraphRun(string runId)
    {
        if (_logicRuntimeCoordinator == null)
        {
            return new LogicRunActionResult(false, "흐름 실행 기능이 아직 준비되지 않았습니다.", null, null);
        }

        var result = _logicRuntimeCoordinator.CancelRun(runId);
        if (result.Snapshot != null && IsTerminalLogicStatus(result.Snapshot.Status))
        {
            ClearLogicGraphRunningState(result.Snapshot.GraphId);
        }

        return result;
    }

    public LogicRunSnapshot? GetLogicGraphRun(string runId)
    {
        if (_logicRuntimeCoordinator == null)
        {
            return TryReadLogicRunSnapshotFromDisk(runId);
        }

        return _logicRuntimeCoordinator.GetSnapshot(runId);
    }

    internal async Task<LogicRunSnapshot> ExecuteLogicGraphRunCoreAsync(
        string graphId,
        string runId,
        string source,
        string runInput,
        Action<LogicRunEvent>? eventCallback,
        CancellationToken cancellationToken
    )
    {
        var normalizedGraphId = (graphId ?? string.Empty).Trim();
        var normalizedRunId = (runId ?? string.Empty).Trim();
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "web" : source.Trim();
        var startedAtUtc = DateTimeOffset.UtcNow;

        RoutineDefinition? routine;
        LogicGraphDefinition? graph;
        lock (_routineLock)
        {
            _routinesById.TryGetValue(normalizedGraphId, out routine);
            graph = routine?.LogicGraph;
        }

        var runDirectory = EnsureLogicRunDirectory(normalizedGraphId, normalizedRunId);
        var logs = new List<string>();
        var nodeStates = new Dictionary<string, MutableLogicNodeState>(StringComparer.Ordinal);
        var status = "running";
        var resultText = string.Empty;
        var runError = string.Empty;
        var title = graph?.Title ?? routine?.Title ?? normalizedGraphId;
        var currentGraphId = graph?.GraphId ?? normalizedGraphId;

        if (graph != null)
        {
            foreach (var node in graph.Nodes)
            {
                nodeStates[node.NodeId] = new MutableLogicNodeState
                {
                    NodeId = node.NodeId,
                    Type = node.Type,
                    Title = node.Title,
                    Status = node.Enabled ? "pending" : "disabled"
                };
            }
        }

        LogicRunSnapshot BuildSnapshot()
        {
            var completedAtValue = status is "completed" or "error" or "canceled"
                ? DateTimeOffset.UtcNow.ToString("O")
                : string.Empty;
            return new LogicRunSnapshot(
                normalizedRunId,
                currentGraphId,
                title,
                status,
                normalizedSource,
                startedAtUtc.ToString("O"),
                DateTimeOffset.UtcNow.ToString("O"),
                completedAtValue,
                resultText,
                runError,
                logs.TakeLast(LogicRunMaxLogs).ToArray(),
                nodeStates.Values
                    .OrderBy(state => state.NodeId, StringComparer.Ordinal)
                    .Select(state => new LogicNodeRunState(
                        state.NodeId,
                        state.Type,
                        state.Title,
                        state.Status,
                        state.Error,
                        state.StartedAtUtc?.ToString("O") ?? string.Empty,
                        state.CompletedAtUtc?.ToString("O") ?? string.Empty,
                        state.Result
                    ))
                    .ToArray()
            );
        }

        void PersistSnapshot(LogicRunSnapshot snapshot)
        {
            File.WriteAllText(
                Path.Combine(runDirectory, LogicRunSnapshotFileName),
                LogicGraphJson.Serialize(snapshot),
                new UTF8Encoding(false)
            );
        }

        void AppendEventLog(string kind, string message, string? nodeId)
        {
            var line = $"[{DateTimeOffset.UtcNow:O}] {kind} {(string.IsNullOrWhiteSpace(nodeId) ? "-" : nodeId)} {message}";
            logs.Add(line);
            if (logs.Count > LogicRunMaxLogs)
            {
                logs.RemoveRange(0, logs.Count - LogicRunMaxLogs);
            }

            File.AppendAllText(
                Path.Combine(runDirectory, LogicRunEventsFileName),
                line + Environment.NewLine,
                new UTF8Encoding(false)
            );
        }

        void Emit(string kind, string message, string? nodeId = null)
        {
            AppendEventLog(kind, message, nodeId);
            var snapshot = BuildSnapshot();
            PersistSnapshot(snapshot);
            eventCallback?.Invoke(new LogicRunEvent(
                normalizedRunId,
                currentGraphId,
                kind,
                message,
                nodeId,
                snapshot
            ));
        }

        try
        {
            if (routine == null || graph == null || !IsLogicGraphRoutine(routine))
            {
                status = "error";
                runError = "작업 흐름을 찾을 수 없습니다.";
                resultText = runError;
                Emit("run_failed", runError);
                return BuildSnapshot();
            }

            currentGraphId = graph.GraphId;
            title = graph.Title;
            File.WriteAllText(
                Path.Combine(runDirectory, "graph.json"),
                LogicGraphJson.Serialize(graph),
                new UTF8Encoding(false)
            );

            var validation = ValidateLogicGraph(graph);
            if (!validation.Ok)
            {
                status = "error";
                runError = validation.Message;
                resultText = validation.Message;
                Emit("run_failed", validation.Message);
                return await FinalizeLogicGraphRunAsync(
                    routine.Id,
                    graph,
                    normalizedSource,
                    startedAtUtc,
                    runDirectory,
                    status,
                    resultText,
                    runError,
                    BuildSnapshot()
                ).ConfigureAwait(false);
            }

            var enabledNodes = graph.Nodes
                .Where(node => node.Enabled)
                .ToDictionary(node => node.NodeId, node => node, StringComparer.Ordinal);
            var enabledEdges = graph.Edges
                .Where(edge => enabledNodes.ContainsKey(edge.SourceNodeId) && enabledNodes.ContainsKey(edge.TargetNodeId))
                .ToArray();
            var outgoingEdges = enabledEdges
                .GroupBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(edge => edge.EdgeId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal
                );
            var incomingEdges = enabledEdges
                .GroupBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(edge => edge.EdgeId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal
                );
            var startNode = enabledNodes.Values.First(node => node.Type == "start");
            var endNodeIds = enabledNodes.Values
                .Where(node => node.Type == "end" || node.Type == "output")
                .Select(node => node.NodeId)
                .ToHashSet(StringComparer.Ordinal);
            var context = new LogicExecutionContext
            {
                RunInput = string.IsNullOrWhiteSpace(runInput)
                    ? ResolveLogicGraphRequestText(graph)
                    : runInput.Trim(),
                RunDirectory = runDirectory
            };
            var queue = new Queue<string>();
            var queued = new HashSet<string>(StringComparer.Ordinal);
            var executed = new HashSet<string>(StringComparer.Ordinal);
            var arrivals = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var completedEndNodes = new List<LogicNodeExecutionOutcome>();

            queue.Enqueue(startNode.NodeId);
            queued.Add(startNode.NodeId);
            Emit("run_started", "흐름 실행을 시작했습니다.");

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nodeId = queue.Dequeue();
                queued.Remove(nodeId);
                if (executed.Contains(nodeId) || !enabledNodes.TryGetValue(nodeId, out var node))
                {
                    continue;
                }

                if (!IsLogicNodeReadyToRun(node, arrivals, incomingEdges))
                {
                    continue;
                }

                if (nodeStates.TryGetValue(nodeId, out var nodeState))
                {
                    nodeState.Status = "running";
                    nodeState.StartedAtUtc = DateTimeOffset.UtcNow;
                }
                Emit("node_started", $"단계 시작: {node.Title}", nodeId);

                LogicNodeExecutionOutcome outcome;
                try
                {
                    outcome = await ExecuteLogicNodeAsync(
                        graph,
                        node,
                        context,
                        incomingEdges.TryGetValue(node.NodeId, out var nodeIncomingEdges) ? nodeIncomingEdges : Array.Empty<LogicEdgeDefinition>(),
                        cancellationToken
                    ).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var failedEnvelope = new LogicNodeResultEnvelope(
                        false,
                        node.Type,
                        ex.Message,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["error"] = ex.Message
                        },
                        Array.Empty<string>(),
                        null,
                        null,
                        Array.Empty<string>()
                    );
                    outcome = new LogicNodeExecutionOutcome(failedEnvelope);
                    if (!node.ContinueOnError)
                    {
                        if (nodeStates.TryGetValue(nodeId, out var failedState))
                        {
                            failedState.Status = "error";
                            failedState.Error = ex.Message;
                            failedState.CompletedAtUtc = DateTimeOffset.UtcNow;
                            failedState.Result = failedEnvelope;
                        }

                        runError = ex.Message;
                        resultText = ex.Message;
                        status = "error";
                        Emit("node_failed", ex.Message, nodeId);
                        Emit("run_failed", ex.Message);
                        return await FinalizeLogicGraphRunAsync(
                            routine.Id,
                            graph,
                            normalizedSource,
                            startedAtUtc,
                            runDirectory,
                            status,
                            resultText,
                            runError,
                            BuildSnapshot()
                        ).ConfigureAwait(false);
                    }
                }

                context.Nodes[nodeId] = outcome.Envelope;
                foreach (var artifact in outcome.Envelope.Artifacts ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(artifact))
                    {
                        context.Artifacts.Add(artifact);
                    }
                }

                if (!string.IsNullOrWhiteSpace(outcome.Envelope.SessionKey))
                {
                    context.Sessions[nodeId] = outcome.Envelope.SessionKey!;
                }
                else if (!string.IsNullOrWhiteSpace(outcome.Envelope.ConversationId))
                {
                    context.Sessions[nodeId] = outcome.Envelope.ConversationId!;
                }

                if (nodeStates.TryGetValue(nodeId, out var completedState))
                {
                    completedState.Status = outcome.Envelope.Ok ? "completed" : "error";
                    completedState.Error = outcome.Envelope.Ok ? null : outcome.Envelope.Text;
                    completedState.CompletedAtUtc = DateTimeOffset.UtcNow;
                    completedState.Result = outcome.Envelope;
                }

                executed.Add(nodeId);
                if (outcome.Envelope.Ok)
                {
                    Emit("node_completed", outcome.Envelope.Text, nodeId);
                }
                else
                {
                    Emit("node_failed", outcome.Envelope.Text, nodeId);
                    if (!node.ContinueOnError)
                    {
                        runError = string.IsNullOrWhiteSpace(outcome.Envelope.Text)
                            ? "노드 실행 실패"
                            : outcome.Envelope.Text;
                        resultText = runError;
                        status = "error";
                        Emit("run_failed", runError);
                        return await FinalizeLogicGraphRunAsync(
                            routine.Id,
                            graph,
                            normalizedSource,
                            startedAtUtc,
                            runDirectory,
                            status,
                            resultText,
                            runError,
                            BuildSnapshot()
                        ).ConfigureAwait(false);
                    }
                }

                if (endNodeIds.Contains(nodeId))
                {
                    completedEndNodes.Add(outcome);
                }

                var selectedEdges = SelectLogicOutgoingEdges(
                    node,
                    outgoingEdges.TryGetValue(nodeId, out var edges) ? edges : Array.Empty<LogicEdgeDefinition>(),
                    context,
                    outcome
                );
                foreach (var edge in selectedEdges)
                {
                    if (!arrivals.TryGetValue(edge.TargetNodeId, out var targetArrivals))
                    {
                        targetArrivals = new HashSet<string>(StringComparer.Ordinal);
                        arrivals[edge.TargetNodeId] = targetArrivals;
                    }

                    targetArrivals.Add(edge.EdgeId);
                    if (executed.Contains(edge.TargetNodeId) || queued.Contains(edge.TargetNodeId))
                    {
                        continue;
                    }

                    if (!enabledNodes.TryGetValue(edge.TargetNodeId, out var targetNode))
                    {
                        continue;
                    }

                    if (IsLogicNodeReadyToRun(targetNode, arrivals, incomingEdges))
                    {
                        queue.Enqueue(edge.TargetNodeId);
                        queued.Add(edge.TargetNodeId);
                    }
                }
            }

            if (completedEndNodes.Count == 0)
            {
                status = "error";
                resultText = "종료 노드에 도달하지 못했습니다.";
                runError = resultText;
                Emit("run_failed", resultText);
            }
            else
            {
                status = "completed";
                resultText = completedEndNodes[^1].Envelope.Text;
                if (string.IsNullOrWhiteSpace(resultText))
                {
                        resultText = completedEndNodes[^1].Envelope.Data.TryGetValue("result", out var resultValue)
                            ? resultValue
                        : "흐름 실행이 끝났습니다.";
                }

                Emit("run_completed", string.IsNullOrWhiteSpace(resultText) ? "흐름 실행이 끝났습니다." : resultText);
            }
        }
        catch (OperationCanceledException)
        {
            status = "canceled";
            runError = "흐름 실행이 취소되었습니다.";
            resultText = runError;
            Emit("run_canceled", runError);
        }
        catch (Exception ex)
        {
            status = "error";
            runError = $"흐름 실행 중 오류가 발생했습니다: {ex.Message}";
            resultText = runError;
            Emit("run_failed", runError);
        }

        return await FinalizeLogicGraphRunAsync(
            normalizedGraphId,
            graph,
            normalizedSource,
            startedAtUtc,
            runDirectory,
            status,
            resultText,
            runError,
            BuildSnapshot()
        ).ConfigureAwait(false);
    }

    private async Task<LogicRunSnapshot> FinalizeLogicGraphRunAsync(
        string graphId,
        LogicGraphDefinition? graph,
        string source,
        DateTimeOffset startedAtUtc,
        string runDirectory,
        string status,
        string resultText,
        string runError,
        LogicRunSnapshot snapshot
    )
    {
        await Task.Yield();
        var completedAtUtc = DateTimeOffset.UtcNow;
        var finalSnapshot = snapshot with
        {
            GraphId = graph?.GraphId ?? graphId,
            Title = graph?.Title ?? snapshot.Title,
            Status = status,
            Source = source,
            UpdatedAtUtc = completedAtUtc.ToString("O"),
            CompletedAtUtc = completedAtUtc.ToString("O"),
            ResultText = resultText,
            Error = runError
        };
        File.WriteAllText(
            Path.Combine(runDirectory, LogicRunSnapshotFileName),
            LogicGraphJson.Serialize(finalSnapshot),
            new UTF8Encoding(false)
        );

        lock (_routineLock)
        {
            if (_routinesById.TryGetValue(graphId, out var routine))
            {
                routine.Running = false;
                routine.LastRunUtc = completedAtUtc;
                routine.LastStatus = status;
                routine.LastOutput = string.IsNullOrWhiteSpace(resultText)
                    ? (string.IsNullOrWhiteSpace(runError) ? "흐름 실행이 끝났습니다." : runError)
                    : resultText;
                routine.LastDurationMs = Math.Max(0L, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds);
                if (string.Equals(NormalizeCronScheduleKind(routine.CronScheduleKind), "at", StringComparison.Ordinal))
                {
                    routine.Enabled = false;
                    routine.NextRunUtc = completedAtUtc;
                }
                else
                {
                    routine.NextRunUtc = ComputeNextCronBridgeRunUtc(routine, completedAtUtc);
                }

                AppendRoutineRunLogEntry(routine, new RoutineRunLogEntry
                {
                    Ts = completedAtUtc.ToUnixTimeMilliseconds(),
                    JobId = routine.Id,
                    Action = "finished",
                    Status = status,
                    Source = source,
                    AttemptCount = 1,
                    Error = string.IsNullOrWhiteSpace(runError) ? null : runError,
                    Summary = BuildCronRunEntrySummary(routine.LastOutput),
                    TelegramStatus = "not_applicable",
                    ArtifactPath = runDirectory,
                    RunAtMs = startedAtUtc.ToUnixTimeMilliseconds(),
                    DurationMs = routine.LastDurationMs,
                    NextRunAtMs = routine.Enabled ? routine.NextRunUtc.ToUnixTimeMilliseconds() : null
                });
                SaveRoutineStateLocked();
            }
        }

        return finalSnapshot;
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicNodeAsync(
        LogicGraphDefinition graph,
        LogicNodeDefinition node,
        LogicExecutionContext context,
        IReadOnlyList<LogicEdgeDefinition> incomingEdges,
        CancellationToken cancellationToken
    )
    {
        var resolvedConfig = ResolveLogicNodeConfig(node, context, incomingEdges);
        return node.Type switch
        {
            "start" => ExecuteLogicStartNode(node, context),
            "end" => ExecuteLogicEndNode(node, context, resolvedConfig),
            "output" => ExecuteLogicOutputNode(node, context, resolvedConfig),
            "template" => ExecuteLogicTemplateNode(node, resolvedConfig),
            "set_var" => ExecuteLogicSetVarNode(node, resolvedConfig, context),
            "delay" => await ExecuteLogicDelayNodeAsync(node, resolvedConfig, cancellationToken).ConfigureAwait(false),
            "if" => ExecuteLogicIfNode(node, resolvedConfig, context),
            "parallel_split" => ExecuteLogicPassNode(node, resolvedConfig, "병렬 분기 실행"),
            "parallel_join" => ExecuteLogicPassNode(node, resolvedConfig, "병렬 합류 완료"),
            "chat_single" => NormalizeLogicAiOutcome(await ExecuteLogicChatNodeAsync(graph, node, resolvedConfig, "single", cancellationToken).ConfigureAwait(false)),
            "chat_orchestration" => NormalizeLogicAiOutcome(await ExecuteLogicChatNodeAsync(graph, node, resolvedConfig, "orchestration", cancellationToken).ConfigureAwait(false)),
            "chat_multi" => NormalizeLogicAiOutcome(await ExecuteLogicChatMultiNodeAsync(graph, node, resolvedConfig, cancellationToken).ConfigureAwait(false)),
            "coding_single" => NormalizeLogicCodingOutcome(await ExecuteLogicCodingNodeAsync(graph, node, resolvedConfig, "single", cancellationToken).ConfigureAwait(false)),
            "coding_orchestration" => NormalizeLogicCodingOutcome(await ExecuteLogicCodingNodeAsync(graph, node, resolvedConfig, "orchestration", cancellationToken).ConfigureAwait(false)),
            "coding_multi" => NormalizeLogicCodingOutcome(await ExecuteLogicCodingNodeAsync(graph, node, resolvedConfig, "multi", cancellationToken).ConfigureAwait(false)),
            "routine_run" => await ExecuteLogicRoutineRunNodeAsync(node, resolvedConfig, cancellationToken).ConfigureAwait(false),
            "memory_search" => ExecuteLogicMemorySearchNode(node, resolvedConfig),
            "memory_get" => ExecuteLogicMemoryGetNode(node, resolvedConfig),
            "web_search" => await ExecuteLogicWebSearchNodeAsync(node, resolvedConfig, cancellationToken).ConfigureAwait(false),
            "web_fetch" => await ExecuteLogicWebFetchNodeAsync(node, resolvedConfig, cancellationToken).ConfigureAwait(false),
            "file_read" => ExecuteLogicFileReadNode(node, resolvedConfig),
            "file_write" => ExecuteLogicFileWriteNode(node, resolvedConfig, context),
            "session_list" => ExecuteLogicSessionListNode(node, resolvedConfig),
            "session_spawn" => ExecuteLogicSessionSpawnNode(node, resolvedConfig, context),
            "session_send" => ExecuteLogicSessionSendNode(node, resolvedConfig, context),
            "cron_status" => ExecuteLogicCronStatusNode(node),
            "cron_run" => await ExecuteLogicCronRunNodeAsync(node, resolvedConfig, cancellationToken).ConfigureAwait(false),
            "browser_execute" => ExecuteLogicBrowserNode(node, resolvedConfig),
            "canvas_execute" => ExecuteLogicCanvasNode(node, resolvedConfig),
            "nodes_pending" => ExecuteLogicNodesPendingNode(node, resolvedConfig),
            "nodes_invoke" => ExecuteLogicNodesInvokeNode(node, resolvedConfig),
            "telegram_stub" => await ExecuteLogicTelegramStubNodeAsync(node, resolvedConfig, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"지원하지 않는 노드 타입입니다: {node.Type}")
        };
    }

    private LogicNodeExecutionOutcome ExecuteLogicStartNode(
        LogicNodeDefinition node,
        LogicExecutionContext context
    )
    {
        var text = string.IsNullOrWhiteSpace(context.RunInput)
            ? "흐름 시작"
            : context.RunInput;
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["input"] = context.RunInput
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicEndNode(
        LogicNodeDefinition node,
        LogicExecutionContext context,
        IReadOnlyDictionary<string, string> config
    )
    {
        var text = FirstNonEmpty(
            config.TryGetValue("result", out var resultValue) ? resultValue : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            context.Nodes.Values.LastOrDefault()?.Text,
            "흐름 실행이 끝났습니다."
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["result"] = text
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicOutputNode(
        LogicNodeDefinition node,
        LogicExecutionContext context,
        IReadOnlyDictionary<string, string> config
    )
    {
        var text = FirstNonEmpty(
            config.TryGetValue("result", out var resultValue) ? resultValue : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            context.Nodes.Values.LastOrDefault()?.Text,
            string.Empty
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["result"] = text
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicTemplateNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var text = FirstNonEmpty(
            config.TryGetValue("template", out var template) ? template : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            string.Empty
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rendered"] = text
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicSetVarNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        LogicExecutionContext context
    )
    {
        var name = (config.TryGetValue("name", out var rawName) ? rawName : string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
                ok: false,
                type: node.Type,
                text: "변수 이름이 필요합니다.",
                data: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["error"] = "name_required"
                }
            ));
        }

        var value = FirstNonEmpty(
            config.TryGetValue("value", out var rawValue) ? rawValue : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            string.Empty
        );
        context.Vars[name] = value;
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: value,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["value"] = value
            }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicDelayNodeAsync(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken
    )
    {
        var milliseconds = ParseLogicPositiveInt(
            FirstNonEmpty(
                config.TryGetValue("milliseconds", out var rawMilliseconds) ? rawMilliseconds : null,
                config.TryGetValue("ms", out var rawMs) ? rawMs : null,
                null
            ),
            fallbackValue: 0,
            maxValue: 300_000
        );
        if (milliseconds <= 0)
        {
            var seconds = ParseLogicPositiveInt(
                config.TryGetValue("seconds", out var rawSeconds) ? rawSeconds : null,
                fallbackValue: 0,
                maxValue: 300
            );
            milliseconds = seconds * 1000;
        }

        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }

        var text = FirstNonEmpty(
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            $"{milliseconds}ms 대기 완료"
        );

        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["milliseconds"] = milliseconds.ToString(CultureInfo.InvariantCulture)
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicIfNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        LogicExecutionContext context
    )
    {
        var condition = ResolveLogicCondition(node, config);
        var left = ResolveLogicReference(condition.LeftRef, context);
        var matched = EvaluateLogicCondition(left, condition.Operator, condition.RightValue);
        var branch = matched ? "true" : "false";
        var payload = FirstNonEmpty(
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            left,
            matched ? "조건 참" : "조건 거짓"
        );
        return new LogicNodeExecutionOutcome(
            BuildLogicEnvelope(
                ok: true,
                type: node.Type,
                text: payload,
                data: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["left"] = left,
                    ["operator"] = condition.Operator,
                    ["right"] = condition.RightValue,
                    ["branch"] = branch
                }
            ),
            branch
        );
    }

    private LogicNodeExecutionOutcome ExecuteLogicPassNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        string fallbackText
    )
    {
        var text = FirstNonEmpty(
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            fallbackText
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicChatNodeAsync(
        LogicGraphDefinition graph,
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        string mode,
        CancellationToken cancellationToken
    )
    {
        var input = FirstNonEmpty(
            config.TryGetValue("input", out var rawInput) ? rawInput : null,
            config.TryGetValue("prompt", out var rawPrompt) ? rawPrompt : null,
            config.TryGetValue("text", out var rawText) ? rawText : null,
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            graph.Description,
            graph.Title
        );
        if (string.IsNullOrWhiteSpace(input))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "채팅 입력이 비어 있습니다."));
        }

        var scope = "chat";
        var title = BuildLogicConversationTitle(graph, node, mode);
        if (string.Equals(mode, "single", StringComparison.Ordinal))
        {
            var request = new ChatRequest(
                input,
                "logic_graph",
                scope,
                mode,
                null,
                title,
                "logic_graph",
                graph.GraphId,
                new[] { "logic_graph", node.Type },
                config.TryGetValue("provider", out var provider) ? provider : null,
                config.TryGetValue("model", out var model) ? model : null,
                ParseCsvValues(config.TryGetValue("memoryNotes", out var memoryNotes) ? memoryNotes : null),
                config.TryGetValue("groqModel", out var groqModel) ? groqModel : null,
                config.TryGetValue("geminiModel", out var geminiModel) ? geminiModel : null,
                config.TryGetValue("copilotModel", out var copilotModel) ? copilotModel : null,
                config.TryGetValue("cerebrasModel", out var cerebrasModel) ? cerebrasModel : null,
                null,
                ParseMultilineValues(config.TryGetValue("webUrls", out var webUrls) ? webUrls : null),
                ParseLogicBool(config.TryGetValue("webSearchEnabled", out var webSearchEnabled) ? webSearchEnabled : null, true),
                config.TryGetValue("codexModel", out var codexModel) ? codexModel : null,
                NvidiaModel: config.TryGetValue("nvidiaModel", out var nvidiaModel) ? nvidiaModel : null
            );
            var result = await ChatSingleWithStateAsync(request, cancellationToken).ConfigureAwait(false);
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
                ok: true,
                type: node.Type,
                text: result.Text,
                data: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["provider"] = result.Provider,
                    ["model"] = result.Model,
                    ["route"] = result.Route
                },
                conversationId: result.ConversationId,
                links: new[] { $"conversation:{result.ConversationId}" }
            ));
        }

        var orchestrationRequest = new ChatRequest(
            input,
            "logic_graph",
            scope,
            mode,
            null,
            title,
            "logic_graph",
            graph.GraphId,
            new[] { "logic_graph", node.Type },
            config.TryGetValue("provider", out var orchestrationProvider) ? orchestrationProvider : null,
            config.TryGetValue("model", out var orchestrationModel) ? orchestrationModel : null,
            ParseCsvValues(config.TryGetValue("memoryNotes", out var orchestrationNotes) ? orchestrationNotes : null),
            config.TryGetValue("groqModel", out var orchestrationGroqModel) ? orchestrationGroqModel : null,
            config.TryGetValue("geminiModel", out var orchestrationGeminiModel) ? orchestrationGeminiModel : null,
            config.TryGetValue("copilotModel", out var orchestrationCopilotModel) ? orchestrationCopilotModel : null,
            config.TryGetValue("cerebrasModel", out var orchestrationCerebrasModel) ? orchestrationCerebrasModel : null,
            null,
            ParseMultilineValues(config.TryGetValue("webUrls", out var orchestrationWebUrls) ? orchestrationWebUrls : null),
            ParseLogicBool(config.TryGetValue("webSearchEnabled", out var orchestrationSearch) ? orchestrationSearch : null, true),
            config.TryGetValue("codexModel", out var orchestrationCodexModel) ? orchestrationCodexModel : null,
            NvidiaModel: config.TryGetValue("nvidiaModel", out var orchestrationNvidiaModel) ? orchestrationNvidiaModel : null
        );
        var orchestrationResult = await ChatOrchestrationWithStateAsync(
            orchestrationRequest,
            cancellationToken
        ).ConfigureAwait(false);
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: orchestrationResult.Text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = orchestrationResult.Provider,
                ["model"] = orchestrationResult.Model,
                ["route"] = orchestrationResult.Route
            },
            conversationId: orchestrationResult.ConversationId,
            links: new[] { $"conversation:{orchestrationResult.ConversationId}" }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicChatMultiNodeAsync(
        LogicGraphDefinition graph,
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken
    )
    {
        var input = FirstNonEmpty(
            config.TryGetValue("input", out var rawInput) ? rawInput : null,
            config.TryGetValue("prompt", out var rawPrompt) ? rawPrompt : null,
            config.TryGetValue("text", out var rawText) ? rawText : null,
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            graph.Description,
            graph.Title
        );
        if (string.IsNullOrWhiteSpace(input))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "채팅 입력이 비어 있습니다."));
        }

        var request = new MultiChatRequest(
            input,
            "logic_graph",
            "chat",
            "multi",
            null,
            BuildLogicConversationTitle(graph, node, "multi"),
            "logic_graph",
            graph.GraphId,
            new[] { "logic_graph", node.Type },
            config.TryGetValue("groqModel", out var groqModel) ? groqModel : null,
            config.TryGetValue("geminiModel", out var geminiModel) ? geminiModel : null,
            config.TryGetValue("copilotModel", out var copilotModel) ? copilotModel : null,
            config.TryGetValue("cerebrasModel", out var cerebrasModel) ? cerebrasModel : null,
            config.TryGetValue("summaryProvider", out var summaryProvider) ? summaryProvider : null,
            ParseCsvValues(config.TryGetValue("memoryNotes", out var memoryNotes) ? memoryNotes : null),
            null,
            ParseMultilineValues(config.TryGetValue("webUrls", out var webUrls) ? webUrls : null),
            ParseLogicBool(config.TryGetValue("webSearchEnabled", out var webSearchEnabled) ? webSearchEnabled : null, true),
            config.TryGetValue("codexModel", out var codexModel) ? codexModel : null,
            config.TryGetValue("nvidiaModel", out var nvidiaModel) ? nvidiaModel : null
        );
        var result = await ChatMultiWithStateAsync(request, cancellationToken).ConfigureAwait(false);
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: result.Summary,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["groqModel"] = result.GroqModel,
                ["geminiModel"] = result.GeminiModel,
                ["copilotModel"] = result.CopilotModel,
                ["cerebrasModel"] = result.CerebrasModel,
                ["nvidiaModel"] = result.NvidiaModel,
                ["requestedSummaryProvider"] = result.RequestedSummaryProvider,
                ["resolvedSummaryProvider"] = result.ResolvedSummaryProvider
            },
            conversationId: result.ConversationId,
            links: new[] { $"conversation:{result.ConversationId}" }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicCodingNodeAsync(
        LogicGraphDefinition graph,
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        string mode,
        CancellationToken cancellationToken
    )
    {
        var input = FirstNonEmpty(
            config.TryGetValue("input", out var rawInput) ? rawInput : null,
            config.TryGetValue("prompt", out var rawPrompt) ? rawPrompt : null,
            config.TryGetValue("text", out var rawText) ? rawText : null,
            config.TryGetValue(LogicResolvedMainInputKey, out var mainInput) ? mainInput : null,
            graph.Description,
            graph.Title
        );
        if (string.IsNullOrWhiteSpace(input))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "코딩 입력이 비어 있습니다."));
        }

        var request = new CodingRunRequest(
            input,
            "logic_graph",
            "coding",
            mode,
            null,
            BuildLogicConversationTitle(graph, node, mode),
            "logic_graph",
            graph.GraphId,
            new[] { "logic_graph", node.Type },
            config.TryGetValue("provider", out var provider) ? provider : null,
            config.TryGetValue("model", out var model) ? model : null,
            FirstNonEmpty(
                config.TryGetValue("language", out var language) ? language : null,
                "auto"
            ),
            ParseCsvValues(config.TryGetValue("memoryNotes", out var memoryNotes) ? memoryNotes : null),
            config.TryGetValue("groqModel", out var groqModel) ? groqModel : null,
            config.TryGetValue("geminiModel", out var geminiModel) ? geminiModel : null,
            config.TryGetValue("cerebrasModel", out var cerebrasModel) ? cerebrasModel : null,
            config.TryGetValue("copilotModel", out var copilotModel) ? copilotModel : null,
            null,
            ParseMultilineValues(config.TryGetValue("webUrls", out var webUrls) ? webUrls : null),
            ParseLogicBool(config.TryGetValue("webSearchEnabled", out var webSearchEnabled) ? webSearchEnabled : null, true),
            config.TryGetValue("codexModel", out var codexModel) ? codexModel : null,
            NvidiaModel: config.TryGetValue("nvidiaModel", out var nvidiaModel) ? nvidiaModel : null
        );

        CodingRunResult result = mode switch
        {
            "single" => await RunCodingSingleAsync(request, cancellationToken).ConfigureAwait(false),
            "orchestration" => await RunCodingOrchestrationAsync(request, cancellationToken).ConfigureAwait(false),
            "multi" => await RunCodingMultiAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"지원하지 않는 coding mode입니다: {mode}")
        };
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: FirstNonEmpty(result.Summary, result.Code, "코딩 실행 완료"),
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = result.Provider,
                ["model"] = result.Model,
                ["language"] = result.Language,
                ["executionStatus"] = result.Execution.Status
            },
            artifacts: result.ChangedFiles ?? Array.Empty<string>(),
            conversationId: result.ConversationId,
            links: new[] { $"conversation:{result.ConversationId}" }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicRoutineRunNodeAsync(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken
    )
    {
        var targetRoutineId = FirstNonEmpty(
            config.TryGetValue("routineId", out var routineId) ? routineId : null,
            config.TryGetValue("graphId", out var graphId) ? graphId : null
        );
        if (string.IsNullOrWhiteSpace(targetRoutineId))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "routineId가 필요합니다."));
        }

        var result = await RunRoutineNowAsync(targetRoutineId, "logic_graph", cancellationToken).ConfigureAwait(false);
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: result.Ok,
            type: node.Type,
            text: result.Message,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["routineId"] = targetRoutineId,
                ["status"] = result.Routine?.LastStatus ?? "-"
            }
        ));
    }

    private LogicNodeExecutionOutcome NormalizeLogicAiOutcome(LogicNodeExecutionOutcome outcome)
    {
        if (!outcome.Envelope.Ok || !LooksLikeLogicAiFailure(outcome.Envelope.Text))
        {
            return outcome;
        }

        return outcome with
        {
            Envelope = outcome.Envelope with
            {
                Ok = false,
                Data = MergeLogicData(outcome.Envelope.Data, "error", "ai_response_failed")
            }
        };
    }

    private LogicNodeExecutionOutcome NormalizeLogicCodingOutcome(LogicNodeExecutionOutcome outcome)
    {
        if (!outcome.Envelope.Ok)
        {
            return outcome;
        }

        var executionStatus = outcome.Envelope.Data.TryGetValue("executionStatus", out var status)
            ? status
            : string.Empty;
        if (IsSuccessfulLogicExecutionStatus(executionStatus))
        {
            return outcome;
        }

        var error = string.IsNullOrWhiteSpace(executionStatus)
            ? "coding_execution_failed"
            : $"coding_execution_{executionStatus}";
        return outcome with
        {
            Envelope = outcome.Envelope with
            {
                Ok = false,
                Text = FirstNonEmpty(outcome.Envelope.Text, $"코딩 실행 상태가 성공이 아닙니다: {executionStatus}"),
                Data = MergeLogicData(outcome.Envelope.Data, "error", error)
            }
        };
    }

    private static IReadOnlyDictionary<string, string> MergeLogicData(
        IReadOnlyDictionary<string, string> source,
        string key,
        string value
    )
    {
        var next = new Dictionary<string, string>(source ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        next[key] = value;
        return next;
    }

    private static bool IsSuccessfulLogicExecutionStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        return normalized.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("success", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeLogicAiFailure(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("API 키가 설정되지 않았습니다", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("인증이 필요합니다", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("호출 오류", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("요청 실패", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("응답 시간이 초과", StringComparison.OrdinalIgnoreCase);
    }

    private LogicNodeExecutionOutcome ExecuteLogicMemorySearchNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var query = FirstNonEmpty(
            config.TryGetValue("query", out var rawQuery) ? rawQuery : null,
            config.TryGetValue("text", out var rawText) ? rawText : null
        );
        if (string.IsNullOrWhiteSpace(query))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "memory search query가 필요합니다."));
        }

        var result = SearchMemory(
            query,
            ParseLogicPositiveInt(config.TryGetValue("maxResults", out var maxResults) ? maxResults : null, 6, 24),
            ParseLogicDouble(config.TryGetValue("minScore", out var minScore) ? minScore : null, 0.35d)
        );
        var text = result.Results.Count == 0
            ? "메모리 검색 결과가 없습니다."
            : string.Join("\n", result.Results.Select(item => $"{item.Path}:{item.StartLine}-{item.EndLine} {TrimPlanText(item.Snippet, 160)}"));
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: !result.Disabled && string.IsNullOrWhiteSpace(result.Error),
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = result.Results.Count.ToString(CultureInfo.InvariantCulture),
                ["disabled"] = result.Disabled ? "true" : "false",
                ["error"] = result.Error ?? string.Empty
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicMemoryGetNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var path = FirstNonEmpty(config.TryGetValue("path", out var rawPath) ? rawPath : null);
        if (string.IsNullOrWhiteSpace(path))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "memory path가 필요합니다."));
        }

        var result = GetMemory(
            path,
            ParseLogicPositiveInt(config.TryGetValue("from", out var fromValue) ? fromValue : null, 1, 100_000),
            ParseLogicPositiveInt(config.TryGetValue("lines", out var lineValue) ? lineValue : null, 120, 100_000)
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: !result.Disabled && string.IsNullOrWhiteSpace(result.Error),
            type: node.Type,
            text: result.Text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["path"] = result.Path,
                ["disabled"] = result.Disabled ? "true" : "false",
                ["error"] = result.Error ?? string.Empty
            }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicWebSearchNodeAsync(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken
    )
    {
        var query = FirstNonEmpty(
            config.TryGetValue("query", out var rawQuery) ? rawQuery : null,
            config.TryGetValue("text", out var rawText) ? rawText : null
        );
        if (string.IsNullOrWhiteSpace(query))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "web search query가 필요합니다."));
        }

        var result = await SearchWebAsync(
            query,
            ParseLogicPositiveInt(config.TryGetValue("count", out var count) ? count : null, 5, 10),
            config.TryGetValue("freshness", out var freshness) ? freshness : null,
            cancellationToken,
            "logic_graph"
        ).ConfigureAwait(false);
        var text = result.Results.Count == 0
            ? (result.Error ?? "검색 결과가 없습니다.")
            : string.Join("\n", result.Results.Select(item => $"{item.Title} | {item.Url}"));
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: !result.Disabled && string.IsNullOrWhiteSpace(result.Error),
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["provider"] = result.Provider,
                ["count"] = result.Results.Count.ToString(CultureInfo.InvariantCulture),
                ["error"] = result.Error ?? string.Empty
            }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicWebFetchNodeAsync(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken
    )
    {
        var url = FirstNonEmpty(config.TryGetValue("url", out var rawUrl) ? rawUrl : null);
        if (string.IsNullOrWhiteSpace(url))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "web fetch url이 필요합니다."));
        }

        var result = await FetchWebAsync(
            url,
            config.TryGetValue("extractMode", out var extractMode) ? extractMode : null,
            ParseLogicPositiveInt(config.TryGetValue("maxChars", out var maxChars) ? maxChars : null, 50_000, 120_000),
            cancellationToken
        ).ConfigureAwait(false);
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: !result.Disabled && string.IsNullOrWhiteSpace(result.Error),
            type: node.Type,
            text: result.Text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = result.Url,
                ["finalUrl"] = result.FinalUrl ?? string.Empty,
                ["status"] = result.Status?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["contentType"] = result.ContentType,
                ["error"] = result.Error ?? string.Empty
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicFileReadNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var path = FirstNonEmpty(config.TryGetValue("path", out var rawPath) ? rawPath : null);
        if (string.IsNullOrWhiteSpace(path))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "file path가 필요합니다."));
        }

        var preview = ReadWorkspaceFile(
            path,
            ParseLogicPositiveInt(config.TryGetValue("maxChars", out var maxChars) ? maxChars : null, 120_000, 200_000)
        );
        if (preview == null)
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "파일을 읽을 수 없습니다."));
        }

        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: preview.Content,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["path"] = preview.FullPath
            },
            artifacts: new[] { preview.FullPath }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicFileWriteNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        LogicExecutionContext context
    )
    {
        var path = FirstNonEmpty(config.TryGetValue("path", out var rawPath) ? rawPath : null);
        if (string.IsNullOrWhiteSpace(path))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "file path가 필요합니다."));
        }

        var content = FirstNonEmpty(
            config.TryGetValue("content", out var rawContent) ? rawContent : null,
            config.TryGetValue("text", out var rawText) ? rawText : null,
            context.Nodes.Values.LastOrDefault()?.Text,
            string.Empty
        );
        var workspaceRoot = ResolveWorkspaceRoot();
        string fullPath;
        try
        {
            fullPath = ResolveWorkspacePath(workspaceRoot, path);
        }
        catch (Exception ex)
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, ex.Message));
        }

        if (!IsPathUnderRoot(fullPath, workspaceRoot))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "workspace 밖으로는 저장할 수 없습니다."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? workspaceRoot);
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        context.Artifacts.Add(fullPath);
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: $"{fullPath} 저장 완료",
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["path"] = fullPath,
                ["length"] = content.Length.ToString(CultureInfo.InvariantCulture)
            },
            artifacts: new[] { fullPath }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicSessionListNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var result = ListSessions(
            ParseCsvValues(config.TryGetValue("kinds", out var kinds) ? kinds : null),
            ParseLogicPositiveInt(config.TryGetValue("limit", out var limit) ? limit : null, 20, 200),
            ParseOptionalLogicInt(config.TryGetValue("activeMinutes", out var activeMinutes) ? activeMinutes : null, 43_200),
            ParseOptionalLogicInt(config.TryGetValue("messageLimit", out var messageLimit) ? messageLimit : null, 20),
            config.TryGetValue("search", out var search) ? search : null,
            config.TryGetValue("scope", out var scope) ? scope : null,
            config.TryGetValue("mode", out var mode) ? mode : null
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: $"세션 {result.Count}개",
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = result.Count.ToString(CultureInfo.InvariantCulture),
                ["firstSessionKey"] = result.Sessions.FirstOrDefault()?.Key ?? string.Empty
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicSessionSpawnNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        LogicExecutionContext context
    )
    {
        var task = FirstNonEmpty(
            config.TryGetValue("task", out var rawTask) ? rawTask : null,
            config.TryGetValue("text", out var rawText) ? rawText : null
        );
        if (string.IsNullOrWhiteSpace(task))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "spawn task가 필요합니다."));
        }

        var result = SpawnSession(
            task,
            config.TryGetValue("label", out var label) ? label : null,
            config.TryGetValue("runtime", out var runtime) ? runtime : null,
            ParseOptionalLogicInt(config.TryGetValue("runTimeoutSeconds", out var runTimeout) ? runTimeout : null, 86_400),
            ParseOptionalLogicInt(config.TryGetValue("timeoutSeconds", out var timeout) ? timeout : null, 86_400),
            ParseOptionalLogicBool(config.TryGetValue("thread", out var thread) ? thread : null),
            config.TryGetValue("mode", out var mode) ? mode : null
        );
        if (!string.IsNullOrWhiteSpace(result.ChildSessionKey))
        {
            var alias = FirstNonEmpty(
                config.TryGetValue("alias", out var aliasValue) ? aliasValue : null,
                node.NodeId
            );
            context.Sessions[alias] = result.ChildSessionKey;
        }

        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: !string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase),
            type: node.Type,
            text: result.Note ?? result.Status,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = result.Status,
                ["runId"] = result.RunId,
                ["childSessionKey"] = result.ChildSessionKey,
                ["runtime"] = result.Runtime
            },
            sessionKey: result.ChildSessionKey,
            links: string.IsNullOrWhiteSpace(result.ChildSessionKey)
                ? Array.Empty<string>()
                : new[] { $"session:{result.ChildSessionKey}" }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicSessionSendNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        LogicExecutionContext context
    )
    {
        var sessionKey = FirstNonEmpty(
            config.TryGetValue("sessionKey", out var rawSessionKey) ? rawSessionKey : null,
            config.TryGetValue("targetSession", out var targetSession) ? targetSession : null
        );
        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            sessionKey = ResolveLogicReference(sessionKey, context);
        }

        var message = FirstNonEmpty(
            config.TryGetValue("message", out var rawMessage) ? rawMessage : null,
            config.TryGetValue("text", out var rawText) ? rawText : null
        );
        if (string.IsNullOrWhiteSpace(sessionKey) || string.IsNullOrWhiteSpace(message))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "sessionKey와 message가 필요합니다."));
        }

        var result = SendToSession(
            sessionKey,
            message,
            ParseOptionalLogicInt(config.TryGetValue("timeoutSeconds", out var timeout) ? timeout : null, 300)
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: !string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase),
            type: node.Type,
            text: FirstNonEmpty(result.Reply, result.Status),
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = result.Status,
                ["runId"] = result.RunId,
                ["sessionKey"] = result.SessionKey,
                ["error"] = result.Error ?? string.Empty
            },
            sessionKey: result.SessionKey,
            links: new[] { $"session:{result.SessionKey}" }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicCronStatusNode(LogicNodeDefinition node)
    {
        var result = GetCronStatus();
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: true,
            type: node.Type,
            text: $"cron enabled={result.Enabled} jobs={result.Jobs}",
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["enabled"] = result.Enabled ? "true" : "false",
                ["jobs"] = result.Jobs.ToString(CultureInfo.InvariantCulture),
                ["storePath"] = result.StorePath
            }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicCronRunNodeAsync(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken
    )
    {
        var jobId = FirstNonEmpty(
            config.TryGetValue("jobId", out var jobIdValue) ? jobIdValue : null,
            config.TryGetValue("routineId", out var routineIdValue) ? routineIdValue : null
        );
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "jobId가 필요합니다."));
        }

        var result = await RunCronJobAsync(
            jobId,
            config.TryGetValue("runMode", out var runMode) ? runMode : null,
            "logic_graph",
            cancellationToken
        ).ConfigureAwait(false);
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: result.Ok,
            type: node.Type,
            text: result.Error ?? result.Reason ?? (result.Ran ? "cron 실행 완료" : "cron 실행 안 함"),
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ran"] = result.Ran ? "true" : "false",
                ["reason"] = result.Reason ?? string.Empty,
                ["error"] = result.Error ?? string.Empty
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicBrowserNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var result = ExecuteBrowser(
            config.TryGetValue("action", out var action) ? action : null,
            config.TryGetValue("targetUrl", out var targetUrl) ? targetUrl : null,
            config.TryGetValue("profile", out var profile) ? profile : null,
            config.TryGetValue("targetId", out var targetId) ? targetId : null,
            ParseOptionalLogicInt(config.TryGetValue("limit", out var limit) ? limit : null, 100)
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: result.Ok,
            type: node.Type,
            text: result.Error ?? $"{result.Action} 완료",
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action"] = result.Action,
                ["profile"] = result.Profile,
                ["activeTargetId"] = result.ActiveTargetId ?? string.Empty,
                ["activeUrl"] = result.ActiveUrl ?? string.Empty
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicCanvasNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var result = ExecuteCanvas(
            config.TryGetValue("action", out var action) ? action : null,
            config.TryGetValue("profile", out var profile) ? profile : null,
            config.TryGetValue("target", out var target) ? target : null,
            config.TryGetValue("targetUrl", out var targetUrl) ? targetUrl : null,
            config.TryGetValue("javaScript", out var javaScript) ? javaScript : null,
            config.TryGetValue("jsonl", out var jsonl) ? jsonl : null,
            config.TryGetValue("outputFormat", out var outputFormat) ? outputFormat : null,
            ParseOptionalLogicInt(config.TryGetValue("maxWidth", out var maxWidth) ? maxWidth : null, 4096)
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: result.Ok,
            type: node.Type,
            text: result.Error ?? $"{result.Action} 완료",
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action"] = result.Action,
                ["profile"] = result.Profile,
                ["target"] = result.Target ?? string.Empty,
                ["url"] = result.Url ?? string.Empty
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicNodesPendingNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var result = ExecuteNodes(
            "pending",
            config.TryGetValue("profile", out var profile) ? profile : null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: result.Ok,
            type: node.Type,
            text: $"pending requests={result.PendingRequests.Count}",
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pendingCount"] = result.PendingRequests.Count.ToString(CultureInfo.InvariantCulture),
                ["nodeCount"] = result.Nodes.Count.ToString(CultureInfo.InvariantCulture)
            }
        ));
    }

    private LogicNodeExecutionOutcome ExecuteLogicNodesInvokeNode(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var action = FirstNonEmpty(
            config.TryGetValue("action", out var actionValue) ? actionValue : null,
            "invoke"
        );
        var result = ExecuteNodes(
            action,
            config.TryGetValue("profile", out var profile) ? profile : null,
            config.TryGetValue("node", out var nodeValue) ? nodeValue : null,
            config.TryGetValue("requestId", out var requestId) ? requestId : null,
            config.TryGetValue("title", out var title) ? title : null,
            config.TryGetValue("body", out var body) ? body : null,
            config.TryGetValue("priority", out var priority) ? priority : null,
            config.TryGetValue("delivery", out var delivery) ? delivery : null,
            config.TryGetValue("invokeCommand", out var invokeCommand) ? invokeCommand : null,
            config.TryGetValue("invokeParamsJson", out var invokeParamsJson) ? invokeParamsJson : null
        );
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: result.Ok,
            type: node.Type,
            text: result.Error ?? $"{result.Action} 완료",
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action"] = result.Action,
                ["selectedNodeId"] = result.SelectedNodeId ?? string.Empty,
                ["selectedCommand"] = result.SelectedCommand ?? string.Empty
            }
        ));
    }

    private async Task<LogicNodeExecutionOutcome> ExecuteLogicTelegramStubNodeAsync(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken
    )
    {
        var input = FirstNonEmpty(
            config.TryGetValue("text", out var rawText) ? rawText : null,
            config.TryGetValue("message", out var rawMessage) ? rawMessage : null
        );
        if (string.IsNullOrWhiteSpace(input))
        {
            return new LogicNodeExecutionOutcome(BuildLogicEnvelope(false, node.Type, "telegram stub input이 필요합니다."));
        }

        var response = await ExecuteAsync(
            input,
            "telegram",
            cancellationToken,
            null,
            ParseMultilineValues(config.TryGetValue("webUrls", out var webUrls) ? webUrls : null),
            ParseLogicBool(config.TryGetValue("webSearchEnabled", out var webSearchEnabled) ? webSearchEnabled : null, true)
        ).ConfigureAwait(false);
        var executionMeta = GetCurrentTelegramExecutionMetadata();
        return new LogicNodeExecutionOutcome(BuildLogicEnvelope(
            ok: !response.StartsWith("error:", StringComparison.OrdinalIgnoreCase),
            type: node.Type,
            text: response,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["retryAttempt"] = executionMeta.RetryAttempt.ToString(CultureInfo.InvariantCulture),
                ["retryMaxAttempts"] = executionMeta.RetryMaxAttempts.ToString(CultureInfo.InvariantCulture),
                ["retryStopReason"] = executionMeta.RetryStopReason ?? string.Empty
            }
        ));
    }

    private LogicNodeResultEnvelope BuildLogicEnvelope(
        bool ok,
        string type,
        string text,
        IReadOnlyDictionary<string, string>? data = null,
        IReadOnlyList<string>? artifacts = null,
        string? conversationId = null,
        string? sessionKey = null,
        IReadOnlyList<string>? links = null
    )
    {
        return new LogicNodeResultEnvelope(
            ok,
            type,
            text ?? string.Empty,
            data ?? new Dictionary<string, string>(StringComparer.Ordinal),
            artifacts ?? Array.Empty<string>(),
            conversationId,
            sessionKey,
            links ?? Array.Empty<string>()
        );
    }

    private IReadOnlyDictionary<string, string> ResolveLogicNodeConfig(
        LogicNodeDefinition node,
        LogicExecutionContext context,
        IReadOnlyList<LogicEdgeDefinition>? incomingEdges = null
    )
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in node.Config)
        {
            resolved[pair.Key] = ResolveLogicTemplate(pair.Value, context);
        }

        var mainInputValue = ResolveLogicMainInputValue(incomingEdges, context);
        if (!string.IsNullOrWhiteSpace(mainInputValue))
        {
            resolved[LogicResolvedMainInputKey] = mainInputValue;
            if (LogicImplicitMainInputPortByType.TryGetValue(node.Type, out var implicitTargetPort)
                && ShouldApplyImplicitMainInput(node, implicitTargetPort))
            {
                resolved[implicitTargetPort] = mainInputValue;
            }
        }

        foreach (var edge in incomingEdges ?? Array.Empty<LogicEdgeDefinition>())
        {
            var targetPort = NormalizeLogicPort(edge.TargetPort);
            if (string.Equals(targetPort, "main", StringComparison.Ordinal))
            {
                continue;
            }

            resolved[targetPort] = ResolveLogicEdgeValue(edge, context);
        }

        return resolved;
    }

    private string ResolveLogicMainInputValue(
        IReadOnlyList<LogicEdgeDefinition>? incomingEdges,
        LogicExecutionContext context
    )
    {
        if (incomingEdges == null || incomingEdges.Count == 0)
        {
            return string.Empty;
        }

        var values = incomingEdges
            .Where(edge => string.Equals(NormalizeLogicPort(edge.TargetPort), "main", StringComparison.Ordinal))
            .Select(edge => ResolveLogicEdgeValue(edge, context))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return values.Length switch
        {
            0 => string.Empty,
            1 => values[0],
            _ => string.Join("\n\n", values)
        };
    }

    private static bool ShouldApplyImplicitMainInput(
        LogicNodeDefinition node,
        string targetPort
    )
    {
        if (node.Config.TryGetValue($"__mode__{targetPort}", out var rawMode))
        {
            var mode = (rawMode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode is "reference" or "edge")
            {
                return false;
            }
        }

        if (!node.Config.TryGetValue(targetPort, out var rawValue))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        return IsLogicRunInputTemplate(rawValue);
    }

    private static bool IsLogicRunInputTemplate(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var match = LogicTemplateRegex.Match(text);
        if (!match.Success || match.Length != text.Length)
        {
            return false;
        }

        var expression = match.Groups["expr"].Value.Trim();
        return string.Equals(expression, "run.input", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveLogicEdgeValue(
        LogicEdgeDefinition edge,
        LogicExecutionContext context
    )
    {
        if (!context.Nodes.TryGetValue(edge.SourceNodeId, out var source))
        {
            return string.Empty;
        }

        var sourcePort = NormalizeLogicPort(edge.SourcePort);
        if (string.Equals(sourcePort, "main", StringComparison.Ordinal)
            || string.Equals(sourcePort, "text", StringComparison.Ordinal)
            || string.Equals(sourcePort, "true", StringComparison.Ordinal)
            || string.Equals(sourcePort, "false", StringComparison.Ordinal))
        {
            return source.Text;
        }

        if (string.Equals(sourcePort, "session", StringComparison.Ordinal))
        {
            return FirstNonEmpty(source.SessionKey, source.ConversationId);
        }

        if (string.Equals(sourcePort, "conversation", StringComparison.Ordinal))
        {
            return source.ConversationId ?? string.Empty;
        }

        if (string.Equals(sourcePort, "artifact", StringComparison.Ordinal))
        {
            return source.Artifacts.LastOrDefault() ?? string.Empty;
        }

        if (sourcePort.StartsWith("data.", StringComparison.Ordinal))
        {
            var dataKey = sourcePort[5..];
            return source.Data.TryGetValue(dataKey, out var value) ? value : string.Empty;
        }

        return source.Text;
    }

    private IReadOnlyList<LogicEdgeDefinition> SelectLogicOutgoingEdges(
        LogicNodeDefinition node,
        IReadOnlyList<LogicEdgeDefinition> edges,
        LogicExecutionContext context,
        LogicNodeExecutionOutcome outcome
    )
    {
        if (edges.Count == 0)
        {
            return Array.Empty<LogicEdgeDefinition>();
        }

        var selected = new List<LogicEdgeDefinition>(edges.Count);
        foreach (var edge in edges)
        {
            if (node.Type == "if")
            {
                var branch = outcome.Branch ?? "false";
                if (!string.Equals(NormalizeLogicPort(edge.SourcePort), branch, StringComparison.Ordinal))
                {
                    continue;
                }
            }
            else if (node.Type != "parallel_split"
                     && !string.Equals(NormalizeLogicPort(edge.SourcePort), "main", StringComparison.Ordinal))
            {
                continue;
            }

            if (edge.Condition != null)
            {
                var left = ResolveLogicReference(edge.Condition.LeftRef, context);
                if (!EvaluateLogicCondition(left, edge.Condition.Operator, edge.Condition.RightValue))
                {
                    continue;
                }
            }

            selected.Add(edge);
        }

        return selected;
    }

    private static bool IsLogicNodeReadyToRun(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, HashSet<string>> arrivals,
        IReadOnlyDictionary<string, LogicEdgeDefinition[]> incomingEdges
    )
    {
        if (node.Type == "start")
        {
            return true;
        }

        if (!arrivals.TryGetValue(node.NodeId, out var sources) || sources.Count == 0)
        {
            return false;
        }

        if (!incomingEdges.TryGetValue(node.NodeId, out var edges) || edges.Length == 0)
        {
            return true;
        }

        if (node.Type == "parallel_join" || RequiresAllLogicIncomingEdges(edges))
        {
            return sources.Count >= edges.Length;
        }

        return true;
    }

    private static bool RequiresAllLogicIncomingEdges(
        IReadOnlyList<LogicEdgeDefinition> edges
    )
    {
        return edges.Any(edge => !string.Equals(NormalizeLogicPort(edge.TargetPort), "main", StringComparison.Ordinal));
    }

    private static string NormalizeLogicPort(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "main" : normalized;
    }

    private static LogicEdgeCondition ResolveLogicCondition(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        node.Config.TryGetValue("leftRef", out var originalLeft);
        node.Config.TryGetValue("operator", out var originalOperator);
        node.Config.TryGetValue("rightValue", out var originalRight);
        return new LogicEdgeCondition
        {
            LeftRef = FirstNonEmpty(
                config.TryGetValue("leftRef", out var fallbackLeft) ? fallbackLeft : originalLeft,
                string.Empty
            ),
            Operator = NormalizeLogicOperator(FirstNonEmpty(
                config.TryGetValue("operator", out var fallbackOperator) ? fallbackOperator : originalOperator,
                "equals"
            )),
            RightValue = FirstNonEmpty(
                config.TryGetValue("rightValue", out var fallbackRight) ? fallbackRight : originalRight,
                string.Empty
            )
        };
    }

    private string ResolveLogicTemplate(string? template, LogicExecutionContext context)
    {
        var raw = template ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return LogicTemplateRegex.Replace(raw, match =>
        {
            var expr = match.Groups["expr"].Value;
            return ResolveLogicReference(expr, context);
        });
    }

    private string ResolveLogicReference(string? reference, LogicExecutionContext context)
    {
        var raw = (reference ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (raw.StartsWith("{{", StringComparison.Ordinal) && raw.EndsWith("}}", StringComparison.Ordinal))
        {
            raw = raw[2..^2].Trim();
        }

        if (string.Equals(raw, "run.input", StringComparison.Ordinal))
        {
            return context.RunInput;
        }

        if (raw.StartsWith("vars.", StringComparison.Ordinal))
        {
            return context.Vars.TryGetValue(raw[5..], out var value) ? value : string.Empty;
        }

        if (raw.StartsWith("sessions.", StringComparison.Ordinal))
        {
            return context.Sessions.TryGetValue(raw[9..], out var value) ? value : string.Empty;
        }

        if (raw.StartsWith("artifacts.", StringComparison.Ordinal))
        {
            var suffix = raw[10..];
            if (string.Equals(suffix, "last", StringComparison.Ordinal))
            {
                return context.Artifacts.LastOrDefault() ?? string.Empty;
            }

            if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < context.Artifacts.Count)
            {
                return context.Artifacts[index];
            }

            return string.Empty;
        }

        if (raw.StartsWith("nodes.", StringComparison.Ordinal))
        {
            var parts = raw.Split('.', 4, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
            {
                return string.Empty;
            }

            if (!context.Nodes.TryGetValue(parts[1], out var nodeResult))
            {
                return string.Empty;
            }

            return parts[2] switch
            {
                "text" => nodeResult.Text,
                "type" => nodeResult.Type,
                "ok" => nodeResult.Ok ? "true" : "false",
                "conversationId" => nodeResult.ConversationId ?? string.Empty,
                "sessionKey" => nodeResult.SessionKey ?? string.Empty,
                "data" when parts.Length == 4 => nodeResult.Data.TryGetValue(parts[3], out var value) ? value : string.Empty,
                _ => string.Empty
            };
        }

        return raw;
    }

    private static bool EvaluateLogicCondition(string left, string? op, string? rightValue)
    {
        var normalizedOperator = NormalizeLogicOperator(op);
        var right = rightValue ?? string.Empty;
        return normalizedOperator switch
        {
            "equals" => string.Equals(left, right, StringComparison.Ordinal),
            "not_equals" => !string.Equals(left, right, StringComparison.Ordinal),
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "not_contains" => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "starts_with" => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
            "ends_with" => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
            "gt" => CompareLogicNumbers(left, right) > 0,
            "gte" => CompareLogicNumbers(left, right) >= 0,
            "lt" => CompareLogicNumbers(left, right) < 0,
            "lte" => CompareLogicNumbers(left, right) <= 0,
            "is_truthy" => ParseLogicBool(left, false),
            "is_falsy" => !ParseLogicBool(left, false),
            _ => false
        };
    }

    private static int CompareLogicNumbers(string left, string right)
    {
        if (!double.TryParse(left, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var leftValue))
        {
            leftValue = 0;
        }

        if (!double.TryParse(right, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var rightValue))
        {
            rightValue = 0;
        }

        return leftValue.CompareTo(rightValue);
    }

    private static string NormalizeLogicOperator(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "=" or "==" => "equals",
            "!=" or "<>" => "not_equals",
            ">=" => "gte",
            "<=" => "lte",
            ">" => "gt",
            "<" => "lt",
            "truthy" => "is_truthy",
            "falsy" => "is_falsy",
            "notequals" => "not_equals",
            "startswith" => "starts_with",
            "endswith" => "ends_with",
            "notcontains" => "not_contains",
            _ when LogicSupportedOperators.Contains(normalized) => normalized,
            _ => "equals"
        };
    }

    private static string BuildLogicConversationTitle(
        LogicGraphDefinition graph,
        LogicNodeDefinition node,
        string mode
    )
    {
        return $"{graph.Title} · {node.Title} · {mode}";
    }

    private LogicRunSnapshot? TryReadLogicRunSnapshotFromDisk(string? runId)
    {
        var normalizedRunId = (runId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRunId))
        {
            return null;
        }

        var root = ResolveLogicRuntimeRoot();
        if (!Directory.Exists(root))
        {
            return null;
        }

        foreach (var graphDirectory in Directory.EnumerateDirectories(root))
        {
            var snapshotPath = Path.Combine(graphDirectory, normalizedRunId, LogicRunSnapshotFileName);
            if (!File.Exists(snapshotPath))
            {
                continue;
            }

            var json = File.ReadAllText(snapshotPath);
            return LogicGraphJson.DeserializeSnapshot(json);
        }

        return null;
    }

    private string EnsureLogicRunDirectory(string graphId, string runId)
    {
        var path = _logicPathResolver?.GetLogicRuntimePath(graphId, runId)
            ?? Path.Combine(ResolveWorkspaceRoot(), ".runtime", "logic", graphId, runId);
        Directory.CreateDirectory(path);
        return path;
    }

    private string ResolveLogicRuntimeRoot()
    {
        return _logicPathResolver?.GetLogicRuntimeRoot()
            ?? Path.Combine(ResolveWorkspaceRoot(), ".runtime", "logic");
    }

    private static bool IsLogicGraphRoutine(RoutineDefinition routine)
    {
        return string.Equals(
                   NormalizeRoutineExecutionMode(routine.ExecutionMode),
                   LogicGraphExecutionMode,
                   StringComparison.Ordinal
               )
               && routine.LogicGraph != null;
    }

    private LogicGraphSummary ToLogicGraphSummaryWithRuntime(RoutineDefinition routine)
    {
        var activeRunId = string.Empty;
        if (routine.Running && _logicRuntimeCoordinator != null)
        {
            var active = _logicRuntimeCoordinator.GetSnapshotByGraphId(routine.Id);
            if (active == null || IsTerminalLogicStatus(active.Status))
            {
                routine.Running = false;
            }
            else
            {
                activeRunId = active.RunId;
            }
        }

        return ToLogicGraphSummary(routine, activeRunId);
    }

    private static LogicGraphSummary ToLogicGraphSummary(RoutineDefinition routine, string activeRunId = "")
    {
        var graph = routine.LogicGraph ?? new LogicGraphDefinition
        {
            GraphId = routine.Id,
            Title = routine.Title,
            Description = routine.Request
        };
        var nextRunLocal = routine.Enabled
            ? routine.NextRunUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";
        var lastRunLocal = routine.LastRunUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        return new LogicGraphSummary(
            graph.GraphId,
            graph.Title,
            graph.Description,
            graph.Version,
            graph.Enabled,
            NormalizeRoutineScheduleKind(graph.Schedule.ScheduleKind),
            graph.Schedule.ScheduleTime,
            graph.Schedule.TimezoneId,
            nextRunLocal,
            lastRunLocal,
            string.IsNullOrWhiteSpace(routine.LastStatus) ? "saved" : routine.LastStatus,
            graph.Nodes.Count,
            graph.Edges.Count,
            activeRunId
        );
    }

    private void ClearLogicGraphRunningState(string graphId)
    {
        var normalizedGraphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedGraphId))
        {
            return;
        }

        lock (_routineLock)
        {
            if (_routinesById.TryGetValue(normalizedGraphId, out var routine) && IsLogicGraphRoutine(routine) && routine.Running)
            {
                routine.Running = false;
                SaveRoutineStateLocked();
            }
        }
    }

    private static bool IsTerminalLogicStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        return normalized.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("error", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("timeout", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("killed", StringComparison.OrdinalIgnoreCase);
    }

    private static LogicGraphDefinition NormalizeLogicGraphDefinition(
        LogicGraphDefinition input,
        string? requestedGraphId
    )
    {
        var graphId = FirstNonEmpty(
            (requestedGraphId ?? string.Empty).Trim(),
            (input.GraphId ?? string.Empty).Trim(),
            $"logic-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}"
        );
        var schedule = input.Schedule ?? new LogicGraphSchedule();
        return new LogicGraphDefinition
        {
            GraphId = graphId,
            Title = FirstNonEmpty(input.Title?.Trim(), $"작업 흐름 {DateTimeOffset.UtcNow:MM-dd HH:mm}"),
            Description = (input.Description ?? string.Empty).Trim(),
            Version = LogicGraphSchemaVersion,
            Viewport = new LogicViewport
            {
                X = input.Viewport?.X ?? 0,
                Y = input.Viewport?.Y ?? 0,
                Zoom = input.Viewport?.Zoom > 0 ? input.Viewport.Zoom : 1
            },
            Schedule = new LogicGraphSchedule
            {
                ScheduleSourceMode = NormalizeRoutineScheduleSourceMode(schedule.ScheduleSourceMode, input.Description),
                ScheduleKind = NormalizeRoutineScheduleKind(schedule.ScheduleKind),
                ScheduleTime = FirstNonEmpty(schedule.ScheduleTime, "08:00"),
                TimezoneId = ResolveTimeZone(schedule.TimezoneId).Id,
                DayOfMonth = schedule.DayOfMonth,
                Weekdays = NormalizeRoutineWeekdays(schedule.Weekdays).ToList(),
                Enabled = schedule.Enabled
            },
            Enabled = input.Enabled,
            Nodes = (input.Nodes ?? new List<LogicNodeDefinition>())
                .Select(node => new LogicNodeDefinition
                {
                    NodeId = FirstNonEmpty(node.NodeId?.Trim(), $"node-{Guid.NewGuid().ToString("N")[..8]}"),
                    Type = (node.Type ?? string.Empty).Trim().ToLowerInvariant(),
                    Title = FirstNonEmpty(node.Title?.Trim(), node.Type?.Trim(), "Node"),
                    Position = new LogicNodePosition
                    {
                        X = node.Position?.X ?? 0,
                        Y = node.Position?.Y ?? 0
                    },
                    Size = new LogicNodeSize
                    {
                        Width = node.Size?.Width > 0 ? node.Size.Width : 168,
                        Height = node.Size?.Height > 0 ? node.Size.Height : 126
                    },
                    Enabled = node.Enabled,
                    ContinueOnError = node.ContinueOnError,
                    Config = NormalizeStringDictionary(node.Config),
                    Outputs = NormalizeStringDictionary(node.Outputs)
                })
                .ToList(),
            Edges = (input.Edges ?? new List<LogicEdgeDefinition>())
                .Select(edge => new LogicEdgeDefinition
                {
                    EdgeId = FirstNonEmpty(edge.EdgeId?.Trim(), $"edge-{Guid.NewGuid().ToString("N")[..8]}"),
                    SourceNodeId = (edge.SourceNodeId ?? string.Empty).Trim(),
                    SourcePort = NormalizeLogicPort(edge.SourcePort),
                    TargetNodeId = (edge.TargetNodeId ?? string.Empty).Trim(),
                    TargetPort = NormalizeLogicPort(edge.TargetPort),
                    Condition = edge.Condition == null
                        ? null
                        : new LogicEdgeCondition
                        {
                            LeftRef = (edge.Condition.LeftRef ?? string.Empty).Trim(),
                            Operator = NormalizeLogicOperator(edge.Condition.Operator),
                            RightValue = edge.Condition.RightValue ?? string.Empty
                        }
                })
                .ToList()
        };
    }

    private static Dictionary<string, string> NormalizeStringDictionary(
        IDictionary<string, string>? source
    )
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source == null)
        {
            return normalized;
        }

        foreach (var pair in source)
        {
            var key = (pair.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            normalized[key] = pair.Value ?? string.Empty;
        }

        return normalized;
    }

    private LogicGraphValidationResult ValidateLogicGraph(LogicGraphDefinition graph)
    {
        if (!string.Equals(graph.Version, LogicGraphSchemaVersion, StringComparison.Ordinal))
        {
            return new LogicGraphValidationResult(false, $"지원하지 않는 그래프 포맷입니다: {graph.Version}");
        }

        if (graph.Nodes.Count == 0)
        {
            return new LogicGraphValidationResult(false, "노드가 비어 있습니다.");
        }

        var enabledNodes = graph.Nodes.Where(node => node.Enabled).ToArray();
        if (enabledNodes.Length == 0)
        {
            return new LogicGraphValidationResult(false, "활성 노드가 하나 이상 필요합니다.");
        }

        var duplicatedNodeId = enabledNodes
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatedNodeId != null)
        {
            return new LogicGraphValidationResult(false, $"중복 nodeId가 있습니다: {duplicatedNodeId.Key}");
        }

        foreach (var node in enabledNodes)
        {
            if (!LogicSupportedNodeTypes.Contains(node.Type))
            {
                return new LogicGraphValidationResult(false, $"지원하지 않는 노드 타입입니다: {node.Type}");
            }
        }

        var startCount = enabledNodes.Count(node => node.Type == "start");
        if (startCount != 1)
        {
            return new LogicGraphValidationResult(false, "start 노드는 정확히 1개여야 합니다.");
        }

        var endCount = enabledNodes.Count(node => node.Type == "end" || node.Type == "output");
        if (endCount < 1)
        {
            return new LogicGraphValidationResult(false, "end 또는 output 노드가 하나 이상 필요합니다.");
        }

        var enabledNodeMap = enabledNodes.ToDictionary(node => node.NodeId, node => node, StringComparer.Ordinal);
        var duplicatedEdgeId = graph.Edges
            .GroupBy(edge => edge.EdgeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatedEdgeId != null)
        {
            return new LogicGraphValidationResult(false, $"중복 edgeId가 있습니다: {duplicatedEdgeId.Key}");
        }

        var duplicatedInputPort = graph.Edges
            .GroupBy(edge => $"{edge.TargetNodeId}:{NormalizeLogicPort(edge.TargetPort)}", StringComparer.Ordinal)
            .FirstOrDefault(group =>
            {
                if (group.Count() < 2)
                {
                    return false;
                }

                var sample = group.FirstOrDefault();
                return sample != null
                    && enabledNodeMap.TryGetValue(sample.TargetNodeId, out var node)
                    && node.Type != "parallel_join";
            });
        if (duplicatedInputPort != null)
        {
            return new LogicGraphValidationResult(false, $"같은 입력 칸에는 연결을 하나만 둘 수 있습니다: {duplicatedInputPort.Key}");
        }

        foreach (var edge in graph.Edges)
        {
            if (!enabledNodeMap.TryGetValue(edge.SourceNodeId, out var sourceNode)
                || !enabledNodeMap.TryGetValue(edge.TargetNodeId, out var targetNode))
            {
                return new LogicGraphValidationResult(false, $"연결이 끊긴 edge가 있습니다: {edge.EdgeId}");
            }

            if (sourceNode.Type == "end")
            {
                return new LogicGraphValidationResult(false, $"end 노드는 outgoing edge를 가질 수 없습니다: {edge.EdgeId}");
            }

            if (targetNode.Type == "start")
            {
                return new LogicGraphValidationResult(false, $"start 노드는 incoming edge를 가질 수 없습니다: {edge.EdgeId}");
            }

            if (!IsLogicSourcePortValid(sourceNode.Type, edge.SourcePort))
            {
                return new LogicGraphValidationResult(false, $"포트 타입이 맞지 않습니다: {edge.EdgeId}");
            }

            if (!IsLogicTargetPortValid(targetNode.Type, edge.TargetPort))
            {
                return new LogicGraphValidationResult(false, $"포트 타입이 맞지 않습니다: {edge.EdgeId}");
            }

            if (edge.Condition != null)
            {
                if (string.IsNullOrWhiteSpace(edge.Condition.LeftRef))
                {
                    return new LogicGraphValidationResult(false, $"edge condition leftRef가 필요합니다: {edge.EdgeId}");
                }

                if (!LogicSupportedOperators.Contains(NormalizeLogicOperator(edge.Condition.Operator)))
                {
                    return new LogicGraphValidationResult(false, $"지원하지 않는 edge operator입니다: {edge.EdgeId}");
                }
            }
        }

        foreach (var joinNode in enabledNodes.Where(node => node.Type == "parallel_join"))
        {
            var incomingCount = graph.Edges.Count(edge => edge.TargetNodeId == joinNode.NodeId);
            if (incomingCount < 2)
            {
                return new LogicGraphValidationResult(false, $"parallel_join 노드는 선행 노드가 2개 이상이어야 합니다: {joinNode.NodeId}");
            }
        }

        if (HasLogicCycle(enabledNodes, graph.Edges.Where(edge =>
            enabledNodeMap.ContainsKey(edge.SourceNodeId) && enabledNodeMap.ContainsKey(edge.TargetNodeId)).ToArray()))
        {
            return new LogicGraphValidationResult(false, "작업 흐름은 순환 없이 이어져야 합니다. 되돌아가는 연결이 있습니다.");
        }

        return new LogicGraphValidationResult(true, string.Empty);
    }

    private static bool HasLogicCycle(
        IReadOnlyList<LogicNodeDefinition> nodes,
        IReadOnlyList<LogicEdgeDefinition> edges
    )
    {
        var indegree = nodes.ToDictionary(node => node.NodeId, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.ToDictionary(node => node.NodeId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            indegree[edge.TargetNodeId] += 1;
            outgoing[edge.SourceNodeId].Add(edge.TargetNodeId);
        }

        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            visited += 1;
            foreach (var targetNodeId in outgoing[nodeId])
            {
                indegree[targetNodeId] -= 1;
                if (indegree[targetNodeId] == 0)
                {
                    queue.Enqueue(targetNodeId);
                }
            }
        }

        return visited != nodes.Count;
    }

    private static bool IsLogicSourcePortValid(string nodeType, string? sourcePort)
    {
        var port = NormalizeLogicPort(sourcePort);
        return nodeType switch
        {
            "if" => port is "true" or "false",
            "parallel_split" => !string.IsNullOrWhiteSpace(port),
            _ => port == "main"
        };
    }

    private static bool IsLogicTargetPortValid(string nodeType, string? targetPort)
    {
        var port = NormalizeLogicPort(targetPort);
        return nodeType switch
        {
            "parallel_join" => !string.IsNullOrWhiteSpace(port),
            "start" => false,
            _ => port == "main"
                || (LogicBindableTargetPortsByType.TryGetValue(nodeType, out var allowedPorts)
                    && allowedPorts.Contains(port))
        };
    }

    private static bool TryBuildLogicScheduleConfig(
        LogicGraphDefinition graph,
        out RoutineScheduleConfig config,
        out string error
    )
    {
        if (string.Equals(
                NormalizeRoutineScheduleSourceMode(graph.Schedule.ScheduleSourceMode, graph.Description),
                "manual",
                StringComparison.Ordinal
            ))
        {
            return TryBuildRoutineScheduleConfig(
                graph.Schedule.ScheduleKind,
                graph.Schedule.ScheduleTime,
                graph.Schedule.Weekdays,
                graph.Schedule.DayOfMonth,
                graph.Schedule.TimezoneId,
                out config,
                out error
            );
        }

        config = ResolveRoutineScheduleConfigFromRequest(
            ResolveLogicGraphRequestText(graph),
            graph.Schedule.TimezoneId
        );
        error = string.Empty;
        return true;
    }

    private static string ResolveLogicGraphRequestText(LogicGraphDefinition graph)
    {
        return FirstNonEmpty(
            (graph.Description ?? string.Empty).Trim(),
            (graph.Title ?? string.Empty).Trim(),
            graph.GraphId
        );
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string>? ParseCsvValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var values = raw
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? null : values;
    }

    private static IReadOnlyList<string>? ParseMultilineValues(string? raw)
    {
        return ParseCsvValues(raw);
    }

    private static int ParseLogicPositiveInt(string? raw, int fallbackValue, int maxValue)
    {
        if (!int.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return fallbackValue;
        }

        return Math.Clamp(value, 0, maxValue);
    }

    private static int? ParseOptionalLogicInt(string? raw, int maxValue)
    {
        var normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Math.Clamp(value, 0, maxValue);
    }

    private static double ParseLogicDouble(string? raw, double fallbackValue)
    {
        if (!double.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
        {
            return fallbackValue;
        }

        return value;
    }

    private static bool ParseLogicBool(string? raw, bool fallbackValue)
    {
        var normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallbackValue;
        }

        return normalized.Equals("1", StringComparison.Ordinal)
               || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("y", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ParseOptionalLogicBool(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return ParseLogicBool(normalized, false);
    }
}
