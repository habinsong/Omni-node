using System.Text.Json.Serialization;

namespace OmniNode.Middleware;

[JsonConverter(typeof(JsonStringEnumConverter<PlanStatus>))]
public enum PlanStatus
{
    Draft,
    ReviewPending,
    Approved,
    Rejected,
    Running,
    Completed,
    Abandoned
}

public sealed record PlanStep(
    string StepId,
    string Title,
    string Description,
    IReadOnlyList<string> MustDo,
    IReadOnlyList<string> MustNotDo,
    IReadOnlyList<string> Verification
);

public sealed record PlanDraftStep(
    string? Title,
    string? Description,
    IReadOnlyList<string>? MustDo,
    IReadOnlyList<string>? MustNotDo,
    IReadOnlyList<string>? Verification
);

public sealed record PlanDraft(
    string? Title,
    IReadOnlyList<PlanDraftStep>? Steps
);

public sealed record WorkPlan(
    string PlanId,
    string Title,
    string Objective,
    PlanStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? SourceConversationId,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<PlanStep> Steps,
    IReadOnlyList<string> DecisionLog,
    string? ReviewerSummary
);

public sealed record PlanReviewResult(
    string PlanId,
    DateTimeOffset ReviewedAtUtc,
    string Summary,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> MissingVerification,
    bool ApprovedRecommendation,
    string ReviewerRoute
);

public sealed record PlanExecutionRecord(
    string PlanId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Status,
    string Message,
    string Source,
    string? ConversationId,
    string? ResultSummary
);

public sealed record PlanSnapshot(
    WorkPlan Plan,
    PlanReviewResult? Review,
    PlanExecutionRecord? Execution
);

public sealed record PlanActionResult(
    bool Ok,
    string Message,
    PlanSnapshot? Snapshot
);

public sealed record PlanIndexEntry(
    string PlanId,
    string Title,
    string Objective,
    PlanStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? ReviewerSummary
);

public sealed class PlanIndexState
{
    public PlanIndexEntry[] Items { get; set; } = Array.Empty<PlanIndexEntry>();
}

public sealed record PlanListResult(
    IReadOnlyList<PlanIndexEntry> Items
);
