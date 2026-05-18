namespace OmniNode.Middleware;

public sealed class PlanApplicationService : IPlanningApplicationService
{
    private readonly CommandService _inner;

    public PlanApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public Task<PlanActionResult> CreatePlanAsync(
        string objective,
        IReadOnlyList<string>? constraints,
        string? mode,
        string? sourceConversationId,
        CancellationToken cancellationToken
    )
    {
        return _inner.CreatePlanAsync(objective, constraints, mode, sourceConversationId, cancellationToken);
    }

    public Task<PlanActionResult> ReviewPlanAsync(string planId, CancellationToken cancellationToken)
    {
        return _inner.ReviewPlanAsync(planId, cancellationToken);
    }

    public PlanActionResult ApprovePlan(string planId)
    {
        return _inner.ApprovePlan(planId);
    }

    public PlanActionResult UpdatePlan(string planId, string? rawJson)
    {
        return _inner.UpdatePlan(planId, rawJson);
    }

    public PlanListResult ListPlans()
    {
        return _inner.ListPlans();
    }

    public PlanSnapshot? GetPlan(string planId)
    {
        return _inner.GetPlan(planId);
    }

    public Task<PlanActionResult> RunPlanAsync(string planId, string source, CancellationToken cancellationToken)
    {
        return _inner.RunPlanAsync(planId, source, cancellationToken);
    }
}
