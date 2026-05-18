using System.Text;

namespace OmniNode.Middleware;

public sealed class PlanApplicationService : IPlanningApplicationService
{
    private readonly PlanService _planService;
    private readonly PlanReviewService _planReviewService;
    private readonly TaskGraphService _taskGraphService;
    private readonly BackgroundTaskCoordinator _taskGraphCoordinator;
    private readonly object _planLock = new();

    public PlanApplicationService(
        PlanService planService,
        PlanReviewService planReviewService,
        TaskGraphService taskGraphService,
        BackgroundTaskCoordinator taskGraphCoordinator
    )
    {
        _planService = planService;
        _planReviewService = planReviewService;
        _taskGraphService = taskGraphService;
        _taskGraphCoordinator = taskGraphCoordinator;
    }

    public async Task<PlanActionResult> CreatePlanAsync(
        string objective,
        IReadOnlyList<string>? constraints,
        string? mode,
        string? sourceConversationId,
        CancellationToken cancellationToken
    )
    {
        var normalizedObjective = (objective ?? string.Empty).Trim();
        if (normalizedObjective.Length == 0)
        {
            return new PlanActionResult(false, "계획 목표를 입력하세요.", null);
        }

        var snapshot = await _planService.CreatePlanAsync(
            normalizedObjective,
            constraints ?? Array.Empty<string>(),
            mode ?? "fast",
            sourceConversationId,
            cancellationToken
        );
        return new PlanActionResult(true, "계획을 생성했습니다.", snapshot);
    }

    public async Task<PlanActionResult> ReviewPlanAsync(
        string planId,
        CancellationToken cancellationToken
    )
    {
        var snapshot = GetPlan(planId);
        if (snapshot == null)
        {
            return new PlanActionResult(false, "계획을 찾을 수 없습니다.", null);
        }

        var review = await _planReviewService.ReviewAsync(snapshot.Plan, cancellationToken);
        var now = review.ReviewedAtUtc;
        var updatedPlan = snapshot.Plan with
        {
            Status = PlanStatus.ReviewPending,
            UpdatedAtUtc = now,
            ReviewerSummary = review.Summary,
            DecisionLog = AppendDecision(
                snapshot.Plan.DecisionLog,
                $"reviewedAt={now:O} route={review.ReviewerRoute} recommendation={(review.ApprovedRecommendation ? "approve" : "revise")}"
            )
        };

        lock (_planLock)
        {
            _planService.SavePlan(updatedPlan);
            _planService.SaveReview(review);
        }

        return new PlanActionResult(
            true,
            "계획 리뷰를 갱신했습니다.",
            new PlanSnapshot(updatedPlan, review, snapshot.Execution)
        );
    }

    public PlanActionResult ApprovePlan(string planId)
    {
        var snapshot = GetPlan(planId);
        if (snapshot == null)
        {
            return new PlanActionResult(false, "계획을 찾을 수 없습니다.", null);
        }

        var now = DateTimeOffset.UtcNow;
        var reviewNote = snapshot.Review == null
            ? "review=skipped"
            : $"reviewRecommendation={(snapshot.Review.ApprovedRecommendation ? "approve" : "revise")}";
        var updatedPlan = snapshot.Plan with
        {
            Status = PlanStatus.Approved,
            UpdatedAtUtc = now,
            DecisionLog = AppendDecision(snapshot.Plan.DecisionLog, $"approvedAt={now:O} {reviewNote}")
        };

        lock (_planLock)
        {
            _planService.SavePlan(updatedPlan);
        }

        var message = snapshot.Review == null
            ? "리뷰 없이 계획을 승인했습니다."
            : "계획을 승인했습니다.";
        return new PlanActionResult(
            true,
            message,
            new PlanSnapshot(updatedPlan, snapshot.Review, snapshot.Execution)
        );
    }

    public PlanActionResult UpdatePlan(string planId, string? rawJson)
    {
        lock (_planLock)
        {
            return _planService.UpdatePlanFromJson(planId, rawJson);
        }
    }

    public PlanListResult ListPlans()
        => new PlanListResult(_planService.ListPlans());

    public PlanSnapshot? GetPlan(string planId)
        => _planService.GetPlan(planId);

    public async Task<PlanActionResult> RunPlanAsync(
        string planId,
        string source,
        CancellationToken cancellationToken
    )
    {
        var snapshot = GetPlan(planId);
        if (snapshot == null)
        {
            return new PlanActionResult(false, "계획을 찾을 수 없습니다.", null);
        }

        if (snapshot.Plan.Status != PlanStatus.Approved && snapshot.Plan.Status != PlanStatus.Completed)
        {
            return new PlanActionResult(
                false,
                "계획 실행 전 승인 단계가 필요합니다.",
                snapshot
            );
        }

        var startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            var createResult = _taskGraphService.CreateGraphFromPlan(snapshot.Plan.PlanId);
            if (!createResult.Ok || createResult.Snapshot == null)
            {
                return new PlanActionResult(
                    false,
                    createResult.Message,
                    snapshot
                );
            }

            var graphId = createResult.Snapshot.Graph.GraphId;
            var runResult = await _taskGraphCoordinator.RunGraphAsync(graphId, source, null, cancellationToken);
            if (!runResult.Ok)
            {
                var failedAtUtc = DateTimeOffset.UtcNow;
                var failedExecution = new PlanExecutionRecord(
                    snapshot.Plan.PlanId,
                    startedAtUtc,
                    failedAtUtc,
                    "error",
                    runResult.Message,
                    source,
                    graphId,
                    BuildTaskGraphExecutionSummary(createResult.Snapshot)
                );
                var revertedPlan = snapshot.Plan with
                {
                    Status = PlanStatus.Approved,
                    UpdatedAtUtc = failedAtUtc,
                    DecisionLog = AppendDecision(
                        snapshot.Plan.DecisionLog,
                        $"runFailedAt={failedAtUtc:O} graphId={graphId} error={TrimText(runResult.Message, 200)}"
                    )
                };

                lock (_planLock)
                {
                    _planService.SaveExecution(failedExecution);
                    _planService.SavePlan(revertedPlan);
                }

                return new PlanActionResult(
                    false,
                    failedExecution.Message,
                    new PlanSnapshot(revertedPlan, snapshot.Review, failedExecution)
                );
            }

            var runningGraph = runResult.Snapshot ?? createResult.Snapshot;
            var runningExecution = new PlanExecutionRecord(
                snapshot.Plan.PlanId,
                startedAtUtc,
                null,
                "running",
                $"Task graph {graphId} 실행을 시작했습니다.",
                source,
                graphId,
                BuildTaskGraphExecutionSummary(runningGraph)
            );
            var runningPlan = snapshot.Plan with
            {
                Status = PlanStatus.Running,
                UpdatedAtUtc = startedAtUtc,
                DecisionLog = AppendDecision(
                    snapshot.Plan.DecisionLog,
                    $"runRequestedAt={startedAtUtc:O} source={source} graphId={graphId}"
                )
            };

            lock (_planLock)
            {
                _planService.SaveExecution(runningExecution);
                _planService.SavePlan(runningPlan);
            }

            _ = Task.Run(() => MonitorPlanTaskGraphAsync(
                runningPlan.PlanId,
                graphId,
                source,
                startedAtUtc
            ));

            return new PlanActionResult(
                true,
                runningExecution.Message,
                new PlanSnapshot(runningPlan, snapshot.Review, runningExecution)
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failedAtUtc = DateTimeOffset.UtcNow;
            var execution = new PlanExecutionRecord(
                snapshot.Plan.PlanId,
                startedAtUtc,
                failedAtUtc,
                "error",
                $"실행 예외: {TrimText(ex.Message, 300)}",
                source,
                null,
                null
            );
            var revertedPlan = snapshot.Plan with
            {
                Status = PlanStatus.Approved,
                UpdatedAtUtc = failedAtUtc,
                DecisionLog = AppendDecision(
                    snapshot.Plan.DecisionLog,
                    $"runFailedAt={failedAtUtc:O} error={TrimText(ex.Message, 200)}"
                )
            };

            lock (_planLock)
            {
                _planService.SaveExecution(execution);
                _planService.SavePlan(revertedPlan);
            }

            return new PlanActionResult(
                false,
                execution.Message,
                new PlanSnapshot(revertedPlan, snapshot.Review, execution)
            );
        }
    }

    private async Task MonitorPlanTaskGraphAsync(
        string planId,
        string graphId,
        string source,
        DateTimeOffset requestedAtUtc
    )
    {
        try
        {
            var terminalGraph = await WaitForTaskGraphTerminalAsync(graphId, CancellationToken.None);
            var completedAtUtc = DateTimeOffset.UtcNow;
            var latestPlan = _planService.GetPlan(planId);
            if (latestPlan == null)
            {
                return;
            }

            var executionOk = terminalGraph.Graph.Status == TaskGraphStatus.Completed;
            var execution = new PlanExecutionRecord(
                planId,
                requestedAtUtc,
                completedAtUtc,
                executionOk ? "ok" : "error",
                executionOk
                    ? $"Task graph {graphId} 실행이 완료되었습니다."
                    : $"Task graph {graphId} 실행이 {terminalGraph.Graph.Status} 상태로 종료되었습니다.",
                source,
                graphId,
                BuildTaskGraphExecutionSummary(terminalGraph)
            );
            var finalPlan = latestPlan.Plan with
            {
                Status = executionOk ? PlanStatus.Completed : PlanStatus.Approved,
                UpdatedAtUtc = completedAtUtc,
                DecisionLog = AppendDecision(
                    latestPlan.Plan.DecisionLog,
                    $"runCompletedAt={completedAtUtc:O} graphId={graphId} status={terminalGraph.Graph.Status}"
                )
            };

            lock (_planLock)
            {
                _planService.SaveExecution(execution);
                _planService.SavePlan(finalPlan);
            }
        }
        catch (Exception ex)
        {
            var failedAtUtc = DateTimeOffset.UtcNow;
            var latestPlan = _planService.GetPlan(planId);
            if (latestPlan == null)
            {
                return;
            }

            var execution = new PlanExecutionRecord(
                planId,
                requestedAtUtc,
                failedAtUtc,
                "error",
                $"Task graph monitor 예외: {TrimText(ex.Message, 300)}",
                source,
                graphId,
                null
            );
            var revertedPlan = latestPlan.Plan with
            {
                Status = PlanStatus.Approved,
                UpdatedAtUtc = failedAtUtc,
                DecisionLog = AppendDecision(
                    latestPlan.Plan.DecisionLog,
                    $"runMonitorFailedAt={failedAtUtc:O} graphId={graphId} error={TrimText(ex.Message, 200)}"
                )
            };

            lock (_planLock)
            {
                _planService.SaveExecution(execution);
                _planService.SavePlan(revertedPlan);
            }
        }
    }

    private async Task<TaskGraphSnapshot> WaitForTaskGraphTerminalAsync(
        string graphId,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _taskGraphService.GetGraph(graphId);
            if (snapshot == null)
            {
                throw new InvalidOperationException("Task graph를 찾을 수 없습니다.");
            }

            if (snapshot.Graph.Status is TaskGraphStatus.Completed or TaskGraphStatus.Failed or TaskGraphStatus.Canceled)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private static string BuildTaskGraphExecutionSummary(TaskGraphSnapshot snapshot)
    {
        var nodes = snapshot.Graph.Nodes ?? Array.Empty<TaskNode>();
        var completedCount = nodes.Count(node => node.Status == TaskNodeStatus.Completed);
        var failedCount = nodes.Count(node => node.Status == TaskNodeStatus.Failed);
        var runningCount = nodes.Count(node => node.Status == TaskNodeStatus.Running);
        var canceledCount = nodes.Count(node => node.Status == TaskNodeStatus.Canceled);
        var builder = new StringBuilder();
        builder.AppendLine(
            $"graph={snapshot.Graph.GraphId} status={snapshot.Graph.Status} completed={completedCount}/{nodes.Count} failed={failedCount} running={runningCount} canceled={canceledCount}"
        );

        foreach (var node in nodes.Take(8))
        {
            builder.AppendLine($"- {node.TaskId} [{node.Status}] {node.Title}");
            if (!string.IsNullOrWhiteSpace(node.OutputSummary))
            {
                builder.AppendLine($"  summary: {TrimText(node.OutputSummary, 200)}");
            }

            if (!string.IsNullOrWhiteSpace(node.Error))
            {
                builder.AppendLine($"  error: {TrimText(node.Error, 180)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> AppendDecision(IReadOnlyList<string> current, string entry)
    {
        var items = (current ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        items.Add(entry);
        return items.ToArray();
    }

    private static string TrimText(string? text, int maxLength)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }
}
