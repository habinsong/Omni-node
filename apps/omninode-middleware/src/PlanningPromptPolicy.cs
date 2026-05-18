using System.Text;

namespace OmniNode.Middleware;

internal static class PlanningPromptPolicy
{
    private static readonly string[] DefaultProviderChain = new[] { "gemini", "groq", "nvidia", "cerebras" };
    private static readonly HashSet<string> AllowedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "gemini", "groq", "nvidia", "cerebras"
    };

    public static IReadOnlyList<string> NormalizeProviderChain(IReadOnlyList<string>? providerChain)
    {
        if (providerChain == null || providerChain.Count == 0)
        {
            return DefaultProviderChain;
        }

        return providerChain
            .Select(item =>
            {
                var normalized = (item ?? string.Empty).Trim().ToLowerInvariant();
                return normalized is "nvidia-nim" or "nvidia_nim" or "nim"
                    ? "nvidia"
                    : normalized;
            })
            .Where(item => AllowedProviders.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string BuildFallbackPlan(string userInput, string systemContext)
    {
        return $"""
                [Execution Plan]
                1. Validate requested task from user input.
                2. Use current system context snapshot for safety checks.
                3. Generate a minimal script to satisfy the request.
                4. Print deterministic stdout only.
                Input: {userInput}
                Context: {systemContext}
                """;
    }

    public static string BuildPlanningPrompt(
        string objective,
        IReadOnlyList<string> constraints,
        string systemContext,
        string mode
    )
    {
        var constraintsText = constraints == null || constraints.Count == 0
            ? "- 없음"
            : string.Join('\n', constraints.Select(item => $"- {item}"));
        var normalizedMode = string.Equals(mode, "interview", StringComparison.OrdinalIgnoreCase)
            ? "interview"
            : "fast";

        return $"""
                한국어로만 답하라.
                너의 역할은 Omni-node Planner다.
                아래 목표를 실제 구현 가능한 작업 계획으로 분해하라.
                과도한 설명 없이 바로 실행 가능한 단계만 작성하라.

                반드시 JSON 객체 하나만 출력한다. Markdown 코드블록, 설명 문장, 주석은 쓰지 않는다.
                스키마 키:
                - title: 한 줄 제목
                - steps: 4~8개 배열
                - steps[].title: 단계 제목
                - steps[].description: 실제 수행할 작업 설명
                - steps[].mustDo: 반드시 할 일 배열
                - steps[].mustNotDo: 피해야 할 일 배열
                - steps[].verification: 확인 방법 배열

                규칙:
                - 단계는 4개 이상 8개 이하
                - 각 단계는 실제 저장소 작업 단위로 쓴다
                - 각 단계마다 mustDo, mustNotDo, verification을 비우지 않는다
                - 마지막 단계에는 빌드/테스트/수동 확인 등 검증을 포함한다
                - mode=interview면 첫 단계에 모호한 지점 확인을 넣는다

                [목표]
                {objective}

                [제약사항]
                {constraintsText}

                [planning_mode]
                {normalizedMode}

                [context]
                {systemContext}
                """;
    }

    public static string BuildPlanReviewPrompt(WorkPlan plan, string systemContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("한국어로만 답하라.");
        builder.AppendLine("너의 역할은 Omni-node Reviewer다.");
        builder.AppendLine("아래 계획에서 빠진 검증, 위험, 범위 누락만 짚어라.");
        builder.AppendLine("200자 이내의 짧은 리뷰 요약만 출력하라.");
        builder.AppendLine();
        builder.AppendLine("[목표]");
        builder.AppendLine(plan.Objective);
        builder.AppendLine();
        builder.AppendLine("[제약사항]");
        if (plan.Constraints.Count == 0)
        {
            builder.AppendLine("- 없음");
        }
        else
        {
            foreach (var item in plan.Constraints)
            {
                builder.AppendLine($"- {item}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[단계]");
        foreach (var step in plan.Steps)
        {
            builder.AppendLine($"- {step.StepId}: {step.Description}");
            foreach (var item in step.Verification)
            {
                builder.AppendLine($"  verification: {item}");
            }
        }

        if (!string.IsNullOrWhiteSpace(systemContext))
        {
            builder.AppendLine();
            builder.AppendLine("[project_context]");
            builder.AppendLine(systemContext);
        }

        return builder.ToString().Trim();
    }

    public static string BuildFallbackPlanReview(WorkPlan plan)
    {
        var verificationGap = plan.Steps.Any(step => step.Verification == null || step.Verification.Count == 0);
        var constraintsMissing = plan.Constraints.Count == 0;
        if (!verificationGap && !constraintsMissing)
        {
            return "큰 누락은 보이지 않지만 실행 전에 검증 명령과 변경 범위를 마지막으로 확인해야 합니다.";
        }

        var issues = new List<string>();
        if (verificationGap)
        {
            issues.Add("검증 단계 보강 필요");
        }

        if (constraintsMissing)
        {
            issues.Add("제약사항 명시 필요");
        }

        return $"보완 필요: {string.Join(", ", issues)}.";
    }
}
