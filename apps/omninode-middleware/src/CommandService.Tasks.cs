using System.Text;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public TaskGraphActionResult CreateTaskGraph(string planId)
    {
        return _taskGraphService.CreateGraphFromPlan(planId);
    }

    public TaskGraphActionResult UpdateTaskGraph(string graphId, string? rawJson)
    {
        return _taskGraphService.UpdateGraphFromJson(graphId, rawJson);
    }

    public TaskGraphListResult ListTaskGraphs()
    {
        return _taskGraphService.ListGraphs();
    }

    public TaskGraphSnapshot? GetTaskGraph(string graphId)
    {
        return _taskGraphService.GetGraph(graphId);
    }

    public async Task<TaskGraphActionResult> RunTaskGraphAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        var snapshot = _taskGraphService.GetGraph(graphId);
        if (snapshot == null)
        {
            return new TaskGraphActionResult(false, "Task graph를 찾을 수 없습니다.", null);
        }

        var sourcePlan = _planService.GetPlan(snapshot.Graph.SourcePlanId);
        if (sourcePlan != null
            && sourcePlan.Plan.Status != PlanStatus.Approved
            && sourcePlan.Plan.Status != PlanStatus.Completed
            && sourcePlan.Plan.Status != PlanStatus.Running)
        {
            return new TaskGraphActionResult(
                false,
                "Task graph 실행 전 원본 계획 승인 단계가 필요합니다.",
                snapshot
            );
        }

        return await _taskGraphCoordinator.RunGraphAsync(
            snapshot.Graph.GraphId,
            source,
            eventSink,
            cancellationToken
        );
    }

    public TaskGraphActionResult CancelTask(string graphId, string taskId)
    {
        return _taskGraphCoordinator.CancelTask(graphId, taskId);
    }

    public async Task<TaskGraphActionResult> RetryTaskAsync(
        string graphId,
        string taskId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        var retry = _taskGraphService.RetryTask(graphId, taskId);
        if (!retry.Ok)
        {
            return retry;
        }

        return await _taskGraphCoordinator.ResumeGraphAsync(
            retry.Snapshot?.Graph.GraphId ?? graphId,
            source,
            eventSink,
            cancellationToken
        );
    }

    public async Task<TaskGraphActionResult> ResumeTaskGraphAsync(
        string graphId,
        string source,
        TaskGraphEventSink? eventSink,
        CancellationToken cancellationToken
    )
    {
        var snapshot = _taskGraphService.GetGraph(graphId);
        if (snapshot == null)
        {
            return new TaskGraphActionResult(false, "Task graph를 찾을 수 없습니다.", null);
        }

        return await _taskGraphCoordinator.ResumeGraphAsync(
            snapshot.Graph.GraphId,
            source,
            eventSink,
            cancellationToken
        );
    }

    public TaskOutputResult? GetTaskOutput(string graphId, string taskId)
    {
        return _taskGraphService.GetTaskOutput(graphId, taskId);
    }

    private async Task<string> ExecuteTaskSlashCommandAsync(
        IReadOnlyList<string> tokens,
        string source,
        CancellationToken cancellationToken
    )
    {
        _ = source;
        if (tokens.Count == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   [작업 명령]
                   자연어 예시:
                   - "작업 목록 보여줘"
                   - "작업 상태 graph_20260308123500001"
                   - "작업 실행 graph_20260308123500001"

                   정확히 제어할 때:
                   /task list
                   /task create <plan-id>
                   /task status <graph-id>
                   /task run <graph-id>
                   /task cancel <graph-id> <task-id>
                   /task output <graph-id> <task-id>
                   """;
        }

        var action = tokens[1].Trim().ToLowerInvariant();
        if (action == "list")
        {
            return FormatTaskGraphList(ListTaskGraphs());
        }

        if (action == "create")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /task create <plan-id>";
            }

            var result = CreateTaskGraph(tokens[2]);
            return FormatTaskGraphActionResult(result);
        }

        if (action == "status" || action == "get")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /task status <graph-id>";
            }

            var snapshot = GetTaskGraph(tokens[2]);
            return snapshot == null
                ? "Task graph를 찾을 수 없습니다."
                : FormatTaskGraphSnapshot(snapshot);
        }

        if (action == "run")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /task run <graph-id>";
            }

            var result = await RunTaskGraphAsync(tokens[2], "web", null, cancellationToken);
            return FormatTaskGraphActionResult(result);
        }

        if (action == "cancel")
        {
            if (tokens.Count < 4)
            {
                return "사용법: /task cancel <graph-id> <task-id>";
            }

            var result = CancelTask(tokens[2], tokens[3]);
            return FormatTaskGraphActionResult(result);
        }

        if (action == "output")
        {
            if (tokens.Count < 4)
            {
                return "사용법: /task output <graph-id> <task-id>";
            }

            var output = GetTaskOutput(tokens[2], tokens[3]);
            return output == null
                ? "Task output을 찾을 수 없습니다."
                : FormatTaskOutput(output);
        }

        return "알 수 없는 /task 명령입니다. /task help를 확인하세요.";
    }

    private static string FormatTaskGraphList(TaskGraphListResult result)
    {
        if (result.Items.Count == 0)
        {
            return "저장된 Task graph가 없습니다.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"task graphs: {result.Items.Count}");
        foreach (var item in result.Items.Take(12))
        {
            builder.AppendLine(
                $"- {item.GraphId} plan={item.SourcePlanId} status={item.Status} done={item.CompletedNodes}/{item.TotalNodes} fail={item.FailedNodes} running={item.RunningNodes}"
            );
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatTaskGraphActionResult(TaskGraphActionResult result)
    {
        if (!result.Ok)
        {
            return $"error: {result.Message}";
        }

        if (result.Snapshot == null)
        {
            return result.Message;
        }

        return $"{result.Message}\n{FormatTaskGraphSnapshot(result.Snapshot)}";
    }

    private static string FormatTaskGraphSnapshot(TaskGraphSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            $"graph={snapshot.Graph.GraphId} plan={snapshot.Graph.SourcePlanId} status={snapshot.Graph.Status} nodes={snapshot.Graph.Nodes.Count}"
        );
        foreach (var node in snapshot.Graph.Nodes)
        {
            var dependsOn = node.DependsOn.Count == 0 ? "-" : string.Join(",", node.DependsOn);
            builder.AppendLine($"- {node.TaskId} [{node.Status}] {node.Category} deps={dependsOn} title={node.Title}");
            if (!string.IsNullOrWhiteSpace(node.OutputSummary))
            {
                builder.AppendLine($"  summary: {TrimPlanText(node.OutputSummary, 200)}");
            }

            if (!string.IsNullOrWhiteSpace(node.Error))
            {
                builder.AppendLine($"  error: {TrimPlanText(node.Error, 160)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatTaskOutput(TaskOutputResult output)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"graph={output.GraphId} task={output.TaskId}");
        if (output.Execution != null)
        {
            builder.AppendLine(
                $"status={output.Execution.Status} executor={output.Execution.ExecutorKind} started={output.Execution.StartedAtUtc:O}"
            );
        }

        if (!string.IsNullOrWhiteSpace(output.StdOut))
        {
            builder.AppendLine("[stdout]");
            builder.AppendLine(TrimPlanText(output.StdOut, 1200));
        }

        if (!string.IsNullOrWhiteSpace(output.StdErr))
        {
            builder.AppendLine("[stderr]");
            builder.AppendLine(TrimPlanText(output.StdErr, 1200));
        }

        if (!string.IsNullOrWhiteSpace(output.ResultJson))
        {
            builder.AppendLine("[result]");
            builder.AppendLine(TrimPlanText(output.ResultJson, 1200));
        }

        return builder.ToString().TrimEnd();
    }
}
