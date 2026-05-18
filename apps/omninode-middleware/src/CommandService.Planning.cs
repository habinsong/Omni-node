using System.Text;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public Task<PlanActionResult> CreatePlanAsync(
        string objective,
        IReadOnlyList<string>? constraints,
        string? mode,
        string? sourceConversationId,
        CancellationToken cancellationToken
    ) => _planAppService.CreatePlanAsync(objective, constraints, mode, sourceConversationId, cancellationToken);

    public Task<PlanActionResult> ReviewPlanAsync(string planId, CancellationToken cancellationToken)
        => _planAppService.ReviewPlanAsync(planId, cancellationToken);

    public PlanActionResult ApprovePlan(string planId)
        => _planAppService.ApprovePlan(planId);

    public PlanActionResult UpdatePlan(string planId, string? rawJson)
        => _planAppService.UpdatePlan(planId, rawJson);

    public PlanListResult ListPlans()
        => _planAppService.ListPlans();

    public PlanSnapshot? GetPlan(string planId)
        => _planAppService.GetPlan(planId);

    public Task<PlanActionResult> RunPlanAsync(string planId, string source, CancellationToken cancellationToken)
        => _planAppService.RunPlanAsync(planId, source, cancellationToken);

    private async Task<string> ExecutePlanSlashCommandAsync(
        IReadOnlyList<string> tokens,
        string source,
        CancellationToken cancellationToken
    )
    {
        if (tokens.Count == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return """
                   [계획 명령]
                   자연어 예시:
                   - "계획 목록 보여줘"
                   - "계획 생성: doctor 기능 구현"
                   - "계획 리뷰 plan_20260308103000001"

                   정확히 제어할 때:
                   /plan list
                   /plan get <plan-id>
                   /plan create [--mode fast|interview] [--constraint <제약>]... <요청>
                   /plan review <plan-id>
                   /plan approve <plan-id>
                   /plan run <plan-id>
                   """;
        }

        var action = tokens[1].Trim().ToLowerInvariant();
        if (action == "list")
        {
            return BuildPlanListText(ListPlans());
        }

        if (action == "get")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /plan get <plan-id>";
            }

            var snapshot = GetPlan(tokens[2]);
            return FormatPlanActionText(snapshot == null
                ? new PlanActionResult(false, "계획을 찾을 수 없습니다.", null)
                : new PlanActionResult(true, "계획을 불러왔습니다.", snapshot));
        }

        if (action == "create")
        {
            if (!TryParsePlanCreateCommand(tokens, out var objective, out var constraints, out var mode, out var error))
            {
                return error;
            }

            var result = await CreatePlanAsync(objective, constraints, mode, null, cancellationToken);
            return FormatPlanActionText(result);
        }

        if (action == "review")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /plan review <plan-id>";
            }

            return FormatPlanActionText(await ReviewPlanAsync(tokens[2], cancellationToken));
        }

        if (action == "approve")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /plan approve <plan-id>";
            }

            return FormatPlanActionText(ApprovePlan(tokens[2]));
        }

        if (action == "run")
        {
            if (tokens.Count < 3)
            {
                return "사용법: /plan run <plan-id>";
            }

            return FormatPlanActionText(await RunPlanAsync(tokens[2], source, cancellationToken));
        }

        return "알 수 없는 /plan 명령입니다. /plan help를 확인하세요.";
    }

    private static bool TryParsePlanCreateCommand(
        IReadOnlyList<string> tokens,
        out string objective,
        out IReadOnlyList<string> constraints,
        out string mode,
        out string error
    )
    {
        objective = string.Empty;
        constraints = Array.Empty<string>();
        mode = "fast";
        error = "사용법: /plan create [--mode fast|interview] [--constraint <제약>]... <요청>";
        if (tokens.Count < 3)
        {
            return false;
        }

        var constraintList = new List<string>();
        var objectiveTokens = new List<string>();
        for (var i = 2; i < tokens.Count; i += 1)
        {
            var token = tokens[i].Trim();
            if (token.Equals("--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokens.Count)
                {
                    return false;
                }

                mode = tokens[++i].Trim();
                continue;
            }

            if (token.Equals("--constraint", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= tokens.Count)
                {
                    return false;
                }

                var value = tokens[++i].Trim();
                if (value.Length > 0)
                {
                    constraintList.Add(value);
                }
                continue;
            }

            objectiveTokens.Add(token);
        }

        objective = string.Join(' ', objectiveTokens).Trim();
        constraints = constraintList.Count == 0 ? Array.Empty<string>() : constraintList.ToArray();
        mode = mode.Equals("interview", StringComparison.OrdinalIgnoreCase) ? "interview" : "fast";
        return objective.Length > 0;
    }

    private static string BuildPlanListText(PlanListResult result)
    {
        if (result.Items.Count == 0)
        {
            return "저장된 계획이 없습니다.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("[계획 목록]");
        foreach (var item in result.Items.Take(20))
        {
            builder.AppendLine($"- {item.PlanId} | {item.Status} | {item.Title} | updated={item.UpdatedAtUtc:O}");
        }

        return builder.ToString().Trim();
    }

    private static string FormatPlanActionText(PlanActionResult result)
    {
        if (!result.Ok)
        {
            return $"계획 오류: {result.Message}";
        }

        if (result.Snapshot == null)
        {
            return result.Message;
        }

        var plan = result.Snapshot.Plan;
        var builder = new StringBuilder();
        builder.AppendLine(result.Message);
        builder.AppendLine($"id={plan.PlanId}");
        builder.AppendLine($"status={plan.Status}");
        builder.AppendLine($"title={plan.Title}");
        builder.AppendLine($"objective={plan.Objective}");
        builder.AppendLine($"steps={plan.Steps.Count}");
        if (!string.IsNullOrWhiteSpace(plan.ReviewerSummary))
        {
            builder.AppendLine($"review={TrimPlanText(plan.ReviewerSummary, 240)}");
        }

        if (result.Snapshot.Execution != null)
        {
            builder.AppendLine($"execution={result.Snapshot.Execution.Status}");
            if (!string.IsNullOrWhiteSpace(result.Snapshot.Execution.ResultSummary))
            {
                builder.AppendLine($"result={TrimPlanText(result.Snapshot.Execution.ResultSummary, 240)}");
            }
        }

        return builder.ToString().Trim();
    }

    // Notebooks/LogicGraphs/Tasks/Memory 등 다른 partial들이 공용으로 사용하는
    // 텍스트 trim 헬퍼. PlanApplicationService는 내부에 동일 로직(TrimText)을
    // 따로 두고 있으며, 추후 AuditTokens/TextHelpers 같은 공용 유틸로 통합 가능.
    private static string TrimPlanText(string? text, int maxLength)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }
}
