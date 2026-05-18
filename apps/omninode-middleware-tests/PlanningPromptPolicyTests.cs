using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class PlanningPromptPolicyTests
{
    [Fact]
    public void NormalizeProviderChainReturnsDefaultWhenInputEmpty()
    {
        var chain = PlanningPromptPolicy.NormalizeProviderChain(null);

        Assert.Equal(new[] { "gemini", "groq", "nvidia", "cerebras" }, chain);
    }

    [Fact]
    public void NormalizeProviderChainCanonicalizesNvidiaAliases()
    {
        var chain = PlanningPromptPolicy.NormalizeProviderChain(new[] { "nvidia-nim", "nim", "nvidia_nim" });

        Assert.Equal(new[] { "nvidia" }, chain);
    }

    [Fact]
    public void NormalizeProviderChainDropsUnknownProvidersAndDuplicates()
    {
        var chain = PlanningPromptPolicy.NormalizeProviderChain(new[] { "groq", "OpenAI", "groq", "Gemini" });

        Assert.Equal(new[] { "groq", "gemini" }, chain);
    }

    [Fact]
    public void BuildFallbackPlanIncludesInputAndContextSections()
    {
        var plan = PlanningPromptPolicy.BuildFallbackPlan("do thing", "context snapshot");

        Assert.Contains("Input: do thing", plan);
        Assert.Contains("Context: context snapshot", plan);
        Assert.Contains("Execution Plan", plan);
    }

    [Fact]
    public void BuildPlanningPromptIncludesObjectiveConstraintsAndDefaultsToFastMode()
    {
        var prompt = PlanningPromptPolicy.BuildPlanningPrompt(
            objective: "ship feature",
            constraints: new[] { "no breaking change", "owner approval" },
            systemContext: "monorepo",
            mode: "unknown"
        );

        Assert.Contains("[목표]", prompt);
        Assert.Contains("ship feature", prompt);
        Assert.Contains("- no breaking change", prompt);
        Assert.Contains("- owner approval", prompt);
        Assert.Contains("[planning_mode]\nfast", prompt);
        Assert.Contains("monorepo", prompt);
    }

    [Fact]
    public void BuildPlanningPromptHandlesInterviewModeAndMissingConstraints()
    {
        var prompt = PlanningPromptPolicy.BuildPlanningPrompt(
            objective: "ship feature",
            constraints: Array.Empty<string>(),
            systemContext: string.Empty,
            mode: "interview"
        );

        Assert.Contains("[제약사항]\n- 없음", prompt);
        Assert.Contains("[planning_mode]\ninterview", prompt);
    }

    [Fact]
    public void BuildPlanReviewPromptListsStepsAndVerifications()
    {
        var plan = CreatePlan(
            objective: "ship feature",
            constraints: new[] { "no breaking change" },
            steps: new[]
            {
                CreateStep("s1", "first step", verification: new[] { "build" }),
                CreateStep("s2", "second step", verification: new[] { "test", "manual" })
            }
        );

        var review = PlanningPromptPolicy.BuildPlanReviewPrompt(plan, "monorepo");

        Assert.Contains("ship feature", review);
        Assert.Contains("- no breaking change", review);
        Assert.Contains("- s1: first step", review);
        Assert.Contains("verification: build", review);
        Assert.Contains("- s2: second step", review);
        Assert.Contains("verification: test", review);
        Assert.Contains("verification: manual", review);
        Assert.Contains("[project_context]\nmonorepo", review);
    }

    [Fact]
    public void BuildPlanReviewPromptSkipsContextSectionWhenEmpty()
    {
        var plan = CreatePlan(
            objective: "ship feature",
            constraints: Array.Empty<string>(),
            steps: new[] { CreateStep("s1", "only step", verification: new[] { "build" }) }
        );

        var review = PlanningPromptPolicy.BuildPlanReviewPrompt(plan, string.Empty);

        Assert.DoesNotContain("[project_context]", review);
        Assert.Contains("[제약사항]\n- 없음", review);
    }

    [Fact]
    public void BuildFallbackPlanReviewReportsHealthyPlan()
    {
        var plan = CreatePlan(
            objective: "ship feature",
            constraints: new[] { "no breaking change" },
            steps: new[] { CreateStep("s1", "only step", verification: new[] { "build" }) }
        );

        var summary = PlanningPromptPolicy.BuildFallbackPlanReview(plan);

        Assert.Contains("큰 누락", summary);
    }

    [Fact]
    public void BuildFallbackPlanReviewFlagsMissingVerificationAndConstraints()
    {
        var plan = CreatePlan(
            objective: "ship feature",
            constraints: Array.Empty<string>(),
            steps: new[] { CreateStep("s1", "only step", verification: Array.Empty<string>()) }
        );

        var summary = PlanningPromptPolicy.BuildFallbackPlanReview(plan);

        Assert.Contains("검증 단계 보강 필요", summary);
        Assert.Contains("제약사항 명시 필요", summary);
    }

    private static WorkPlan CreatePlan(
        string objective,
        IReadOnlyList<string> constraints,
        IReadOnlyList<PlanStep> steps
    ) => new(
        PlanId: "p1",
        Title: "t",
        Objective: objective,
        Status: PlanStatus.Draft,
        CreatedAtUtc: DateTimeOffset.UtcNow,
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        SourceConversationId: null,
        Constraints: constraints,
        Steps: steps,
        DecisionLog: Array.Empty<string>(),
        ReviewerSummary: null
    );

    private static PlanStep CreateStep(
        string stepId,
        string description,
        IReadOnlyList<string> verification
    ) => new(
        StepId: stepId,
        Title: stepId,
        Description: description,
        MustDo: Array.Empty<string>(),
        MustNotDo: Array.Empty<string>(),
        Verification: verification
    );
}
