namespace OmniNode.Middleware;

public sealed class LogicGraphRuntimeCoordinator
{
    private const string SnapshotFileName = "snapshot.json";

    private readonly IStatePathResolver _pathResolver;
    private readonly object _gate = new();
    private readonly Dictionary<string, LogicRunState> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeRunByGraphId = new(StringComparer.Ordinal);

    private Func<string, string, string, string, Action<LogicRunEvent>?, CancellationToken, Task<LogicRunSnapshot>>? _executor;

    private sealed class LogicRunState
    {
        public LogicRunState(string graphId, string runId, string runInput, LogicRunSnapshot snapshot)
        {
            GraphId = graphId;
            RunId = runId;
            RunInput = runInput;
            Snapshot = snapshot;
        }

        public string GraphId { get; }
        public string RunId { get; }
        public string RunInput { get; }
        public LogicRunSnapshot Snapshot { get; set; }
        public CancellationTokenSource CancellationSource { get; } = new();
        public List<Action<LogicRunEvent>> Subscribers { get; } = new();
        public Task? LoopTask { get; set; }
        public bool Completed { get; set; }
    }

    public LogicGraphRuntimeCoordinator(IStatePathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public void ConfigureExecutor(
        Func<string, string, string, string, Action<LogicRunEvent>?, CancellationToken, Task<LogicRunSnapshot>> executor
    )
    {
        _executor = executor;
    }

    public LogicRunActionResult RunGraph(
        string graphId,
        string source,
        string? runInput,
        Action<LogicRunEvent>? eventCallback
    )
    {
        var normalizedGraphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedGraphId))
        {
            return new LogicRunActionResult(false, "graphId가 필요합니다.", null, null);
        }

        if (_executor == null)
        {
            return new LogicRunActionResult(false, "흐름 실행 기능이 아직 준비되지 않았습니다.", null, null);
        }

        lock (_gate)
        {
            if (_activeRunByGraphId.TryGetValue(normalizedGraphId, out var activeRunId)
                && _runs.TryGetValue(activeRunId, out var existing)
                && !existing.Completed)
            {
                if (eventCallback != null)
                {
                    existing.Subscribers.Add(eventCallback);
                }

                return new LogicRunActionResult(
                    true,
                    "이미 실행 중인 흐름에 연결했습니다.",
                    existing.RunId,
                    existing.Snapshot
                );
            }
        }

        var runId = $"logicrun-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        var now = DateTimeOffset.UtcNow.ToString("O");
        var initialSnapshot = new LogicRunSnapshot(
            runId,
            normalizedGraphId,
            string.Empty,
            "running",
            string.IsNullOrWhiteSpace(source) ? "web" : source,
            now,
            now,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<LogicNodeRunState>()
        );
        var runState = new LogicRunState(normalizedGraphId, runId, runInput ?? string.Empty, initialSnapshot);
        if (eventCallback != null)
        {
            runState.Subscribers.Add(eventCallback);
        }

        lock (_gate)
        {
            _runs[runId] = runState;
            _activeRunByGraphId[normalizedGraphId] = runId;
            runState.LoopTask = Task.Run(() => ExecuteRunAsync(runState, source));
        }

        return new LogicRunActionResult(
            true,
            "흐름 실행을 시작했습니다.",
            runId,
            initialSnapshot
        );
    }

    public LogicRunActionResult CancelRun(string runId)
    {
        var normalizedRunId = (runId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRunId))
        {
            return new LogicRunActionResult(false, "runId가 필요합니다.", null, null);
        }

        lock (_gate)
        {
            if (!_runs.TryGetValue(normalizedRunId, out var state))
            {
                return new LogicRunActionResult(false, "진행 중인 실행을 찾을 수 없습니다.", null, null);
            }

            if (state.Completed)
            {
                return new LogicRunActionResult(true, "이미 끝난 실행입니다.", normalizedRunId, state.Snapshot);
            }

            state.CancellationSource.Cancel();
            return new LogicRunActionResult(true, "실행 취소를 요청했습니다.", normalizedRunId, state.Snapshot);
        }
    }

    public LogicRunSnapshot? GetSnapshot(string runId)
    {
        var normalizedRunId = (runId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRunId))
        {
            return null;
        }

        lock (_gate)
        {
            if (_runs.TryGetValue(normalizedRunId, out var state))
            {
                return state.Snapshot;
            }
        }

        return TryLoadSnapshotFromDisk(normalizedRunId);
    }

    public LogicRunSnapshot? GetSnapshotByGraphId(string graphId)
    {
        var normalizedGraphId = (graphId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedGraphId))
        {
            return null;
        }

        lock (_gate)
        {
            if (_activeRunByGraphId.TryGetValue(normalizedGraphId, out var runId)
                && _runs.TryGetValue(runId, out var state))
            {
                return state.Snapshot;
            }
        }

        return null;
    }

    public async Task StopAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            foreach (var state in _runs.Values)
            {
                if (!state.Completed)
                {
                    state.CancellationSource.Cancel();
                }
            }

            tasks = _runs.Values
                .Select(state => state.LoopTask)
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
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task ExecuteRunAsync(LogicRunState state, string source)
    {
        var executor = _executor;
        if (executor == null)
        {
            return;
        }

        try
        {
            var snapshot = await executor(
                state.GraphId,
                state.RunId,
                source,
                state.RunInput,
                evt => HandleEvent(state.RunId, evt),
                state.CancellationSource.Token
            ).ConfigureAwait(false);
            lock (_gate)
            {
                if (_runs.TryGetValue(state.RunId, out var current))
                {
                    current.Snapshot = snapshot;
                    if (IsTerminalStatus(snapshot.Status))
                    {
                        current.Completed = true;
                        _activeRunByGraphId.Remove(current.GraphId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            var snapshot = GetSnapshot(state.RunId) ?? state.Snapshot;
            var canceledAt = DateTimeOffset.UtcNow.ToString("O");
            var fallback = snapshot with
            {
                Status = "canceled",
                UpdatedAtUtc = canceledAt,
                CompletedAtUtc = canceledAt,
                Error = string.IsNullOrWhiteSpace(snapshot.Error) ? "실행이 취소되었습니다." : snapshot.Error
            };
            HandleEvent(
                state.RunId,
                new LogicRunEvent(
                    state.RunId,
                    state.GraphId,
                    "run_canceled",
                    fallback.Error,
                    null,
                    fallback
                )
            );
        }
        catch (Exception ex)
        {
            var snapshot = GetSnapshot(state.RunId) ?? state.Snapshot;
            var failedAt = DateTimeOffset.UtcNow.ToString("O");
            var fallback = snapshot with
            {
                Status = "error",
                UpdatedAtUtc = failedAt,
                CompletedAtUtc = failedAt,
                Error = ex.Message
            };
            HandleEvent(
                state.RunId,
                new LogicRunEvent(
                    state.RunId,
                    state.GraphId,
                    "run_failed",
                    ex.Message,
                    null,
                    fallback
                )
            );
        }
    }

    private void HandleEvent(string runId, LogicRunEvent evt)
    {
        Action<LogicRunEvent>[] subscribers;
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var state))
            {
                return;
            }

            state.Snapshot = evt.Snapshot;
            if (IsTerminalStatus(evt.Snapshot.Status))
            {
                state.Completed = true;
                _activeRunByGraphId.Remove(state.GraphId);
            }

            subscribers = state.Subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber(evt);
            }
            catch
            {
            }
        }
    }

    private LogicRunSnapshot? TryLoadSnapshotFromDisk(string runId)
    {
        try
        {
            var root = _pathResolver.GetLogicRuntimeRoot();
            if (!Directory.Exists(root))
            {
                return null;
            }

            foreach (var graphDirectory in Directory.EnumerateDirectories(root))
            {
                var runDirectory = Path.Combine(graphDirectory, runId);
                var snapshotPath = Path.Combine(runDirectory, SnapshotFileName);
                if (!File.Exists(snapshotPath))
                {
                    continue;
                }

                var json = File.ReadAllText(snapshotPath);
                return LogicGraphJson.DeserializeSnapshot(json);
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsTerminalStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "completed" or "error" or "canceled";
    }
}
