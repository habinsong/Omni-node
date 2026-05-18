using System.Text;

namespace OmniNode.Middleware;

public sealed class FileTaskGraphStore
{
    private const string IndexFileName = "index.json";

    private readonly IStatePathResolver _pathResolver;

    public FileTaskGraphStore(IStatePathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public IReadOnlyList<TaskGraphIndexEntry> LoadIndex()
    {
        var indexPath = _pathResolver.GetTaskGraphsIndexPath();
        if (!File.Exists(indexPath))
        {
            return Array.Empty<TaskGraphIndexEntry>();
        }

        try
        {
            var json = AtomicFileStore.ReadAllTextWithBackup(
                indexPath,
                value => TaskGraphJson.DeserializeIndexState(value) != null,
                Encoding.UTF8,
                "task-graph-store"
            );
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<TaskGraphIndexEntry>();
            }

            var state = TaskGraphJson.DeserializeIndexState(json);
            if (state?.Items == null)
            {
                return Array.Empty<TaskGraphIndexEntry>();
            }

            return state.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.GraphId))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ToArray();
        }
        catch
        {
            return Array.Empty<TaskGraphIndexEntry>();
        }
    }

    public TaskGraphSnapshot? TryLoadSnapshot(string graphId)
    {
        var path = GetGraphPath(graphId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = AtomicFileStore.ReadAllTextWithBackup(
                path,
                value => TaskGraphJson.DeserializeSnapshot(value) != null,
                Encoding.UTF8,
                "task-graph-store"
            );
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return TaskGraphJson.DeserializeSnapshot(json);
        }
        catch
        {
            return null;
        }
    }

    public void SaveSnapshot(TaskGraphSnapshot snapshot)
    {
        Directory.CreateDirectory(_pathResolver.GetTaskGraphsRoot());
        AtomicFileStore.WriteAllText(
            GetGraphPath(snapshot.Graph.GraphId),
            TaskGraphJson.Serialize(snapshot, indented: true),
            ownerOnly: true
        );

        SaveIndexEntry(BuildIndexEntry(snapshot));
    }

    public TaskOutputResult LoadOutput(string graphId, string taskId, TaskExecutionRecord? execution)
    {
        var runtimePath = _pathResolver.GetTaskRuntimePath(graphId, taskId);
        var stdoutPath = execution?.StdOutPath ?? Path.Combine(runtimePath, "stdout.log");
        var stderrPath = execution?.StdErrPath ?? Path.Combine(runtimePath, "stderr.log");
        var resultPath = execution?.ResultPath ?? Path.Combine(runtimePath, "result.json");

        return new TaskOutputResult(
            graphId,
            taskId,
            execution,
            ReadTailText(stdoutPath),
            ReadTailText(stderrPath),
            File.Exists(resultPath) ? File.ReadAllText(resultPath, Encoding.UTF8) : null
        );
    }

    private void SaveIndexEntry(TaskGraphIndexEntry nextEntry)
    {
        var current = LoadIndex()
            .Where(item => !item.GraphId.Equals(nextEntry.GraphId, StringComparison.Ordinal))
            .ToList();
        current.Add(nextEntry);
        var ordered = current
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ToArray();
        var state = new TaskGraphIndexState
        {
            Items = ordered
        };

        AtomicFileStore.WriteAllText(
            _pathResolver.GetTaskGraphsIndexPath(),
            TaskGraphJson.Serialize(state, indented: true),
            ownerOnly: true
        );
    }

    private string GetGraphPath(string graphId)
    {
        return Path.Combine(_pathResolver.GetTaskGraphsRoot(), $"{graphId.Trim()}.json");
    }

    private static TaskGraphIndexEntry BuildIndexEntry(TaskGraphSnapshot snapshot)
    {
        var nodes = snapshot.Graph.Nodes;
        return new TaskGraphIndexEntry(
            snapshot.Graph.GraphId,
            snapshot.Graph.SourcePlanId,
            snapshot.Graph.Status,
            snapshot.Graph.CreatedAtUtc,
            snapshot.Graph.UpdatedAtUtc,
            nodes.Count,
            nodes.Count(node => node.Status == TaskNodeStatus.Completed),
            nodes.Count(node => node.Status == TaskNodeStatus.Failed),
            nodes.Count(node => node.Status == TaskNodeStatus.Running)
        );
    }

    private static string ReadTailText(string path, int maxChars = 16_000)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            if (content.Length <= maxChars)
            {
                return content;
            }

            return content[^maxChars..];
        }
        catch
        {
            return string.Empty;
        }
    }
}
