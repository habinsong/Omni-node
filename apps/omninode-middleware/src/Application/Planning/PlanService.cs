using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed class PlanService
{
    private static readonly Regex StepLineRegex = new(
        @"^\s*(?:\d+[.)]|[-*])\s+(?<text>.+)$",
        RegexOptions.Compiled
    );

    private readonly FilePlanStore _store;
    private readonly LlmRouter _llmRouter;
    private readonly RoutingPolicyResolver _routingPolicyResolver;
    private readonly AppConfig _config;
    private readonly IConversationStore _conversationStore;
    private readonly IMemoryNoteStore _memoryNoteStore;
    private readonly ProjectContextLoader _projectContextLoader;

    public PlanService(
        FilePlanStore store,
        LlmRouter llmRouter,
        RoutingPolicyResolver routingPolicyResolver,
        AppConfig config,
        IConversationStore conversationStore,
        IMemoryNoteStore memoryNoteStore,
        ProjectContextLoader projectContextLoader
    )
    {
        _store = store;
        _llmRouter = llmRouter;
        _routingPolicyResolver = routingPolicyResolver;
        _config = config;
        _conversationStore = conversationStore;
        _memoryNoteStore = memoryNoteStore;
        _projectContextLoader = projectContextLoader;
    }

    public async Task<PlanSnapshot> CreatePlanAsync(
        string objective,
        IReadOnlyList<string> constraints,
        string mode,
        string? sourceConversationId,
        CancellationToken cancellationToken
    )
    {
        var normalizedObjective = NormalizeObjective(objective);
        var normalizedConstraints = NormalizeConstraints(constraints);
        var normalizedMode = NormalizeMode(mode);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var planId = $"plan_{createdAtUtc:yyyyMMddHHmmssfff}";
        var plannerChain = _routingPolicyResolver.ResolveProviderChain(TaskCategory.Planner);
        var plannerRoute = _llmRouter.ResolvePlanningRoute("planner", plannerChain);
        var plannerContext = BuildPlannerContext(sourceConversationId);
        var draft = await _llmRouter.BuildWorkPlanDraftAsync(
            normalizedObjective,
            normalizedConstraints,
            plannerContext,
            normalizedMode,
            plannerChain,
            cancellationToken
        );
        _routingPolicyResolver.ResolveDecision(
            TaskCategory.Planner,
            "auto",
            BuildPlanningAvailabilitySnapshot(plannerRoute),
            reason: "plan_create"
        );
        var structuredDraft = TryParseStructuredDraft(draft);
        var steps = structuredDraft == null
            ? ParseSteps(draft, normalizedObjective, normalizedConstraints, normalizedMode)
            : BuildStepsFromDraft(structuredDraft, normalizedObjective, normalizedConstraints, normalizedMode);
        var plan = new WorkPlan(
            planId,
            ResolveTitle(structuredDraft, draft, normalizedObjective),
            normalizedObjective,
            PlanStatus.Draft,
            createdAtUtc,
            createdAtUtc,
            string.IsNullOrWhiteSpace(sourceConversationId) ? null : sourceConversationId.Trim(),
            normalizedConstraints,
            steps,
            new[]
            {
                $"createdAt={createdAtUtc:O}",
                $"plannerMode={normalizedMode}",
                $"plannerRoute={plannerRoute}",
                $"plannerChain={string.Join(">", plannerChain)}"
            },
            null
        );

        _store.SavePlan(plan);
        return new PlanSnapshot(plan, null, null);
    }

    public IReadOnlyList<PlanIndexEntry> ListPlans()
    {
        return _store.LoadIndex();
    }

    public PlanSnapshot? GetPlan(string planId)
    {
        var normalizedPlanId = NormalizePlanId(planId);
        if (string.IsNullOrWhiteSpace(normalizedPlanId))
        {
            return null;
        }

        var plan = _store.TryLoadPlan(normalizedPlanId);
        if (plan == null)
        {
            return null;
        }

        return new PlanSnapshot(
            plan,
            _store.TryLoadReview(normalizedPlanId),
            _store.TryLoadExecution(normalizedPlanId)
        );
    }

    public void SavePlan(WorkPlan plan)
    {
        _store.SavePlan(plan);
    }

    public void SaveReview(PlanReviewResult review)
    {
        _store.SaveReview(review);
    }

    public void SaveExecution(PlanExecutionRecord execution)
    {
        _store.SaveExecution(execution);
    }

    private string BuildPlannerContext(string? sourceConversationId)
    {
        var builder = new StringBuilder();
        builder.AppendLine(_projectContextLoader.BuildPromptContext(
            _projectContextLoader.LoadSnapshot(),
            2600
        ));
        builder.AppendLine();
        builder.AppendLine("workspace_root:");
        builder.AppendLine(_config.WorkspaceRootDir);

        if (!string.IsNullOrWhiteSpace(sourceConversationId))
        {
            try
            {
                var history = _conversationStore.BuildHistoryText(sourceConversationId.Trim(), 8);
                if (!string.IsNullOrWhiteSpace(history))
                {
                    builder.AppendLine("conversation_history:");
                    builder.AppendLine(TrimBlock(history, 2400));
                }

                var thread = _conversationStore.Get(sourceConversationId.Trim());
                if (thread != null && thread.LinkedMemoryNotes.Count > 0)
                {
                    builder.AppendLine("linked_memory_notes:");
                    foreach (var noteName in thread.LinkedMemoryNotes.Take(3))
                    {
                        var note = _memoryNoteStore.Read(noteName);
                        if (note == null)
                        {
                            continue;
                        }

                        builder.AppendLine($"## {note.Name}");
                        builder.AppendLine(TrimBlock(note.Content, 600));
                    }
                }
            }
            catch
            {
            }
        }

        return builder.ToString().Trim();
    }

    private IReadOnlyList<PlanStep> ParseSteps(
        string rawDraft,
        string objective,
        IReadOnlyList<string> constraints,
        string mode
    )
    {
        var lines = (rawDraft ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var stepTexts = new List<string>();
        foreach (var line in lines)
        {
            var match = StepLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["text"].Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            stepTexts.Add(value);
            if (stepTexts.Count >= 8)
            {
                break;
            }
        }

        if (stepTexts.Count == 0)
        {
            stepTexts.AddRange(BuildFallbackStepTexts(objective, mode));
        }

        var verificationHints = BuildVerificationHints(objective);
        var mustNotDo = constraints.Count == 0
            ? Array.Empty<string>()
            : constraints.ToArray();

        var steps = new List<PlanStep>(stepTexts.Count);
        for (var i = 0; i < stepTexts.Count; i += 1)
        {
            var stepText = stepTexts[i];
            var mustDo = new List<string> { stepText };
            if (i == 0)
            {
                mustDo.Add("요구사항과 제약사항 누락 여부를 먼저 확인한다.");
            }

            var verification = new List<string>();
            if (i == stepTexts.Count - 1)
            {
                verification.AddRange(verificationHints);
            }
            else
            {
                verification.Add("이 단계 산출물이 다음 단계 입력으로 충분한지 확인한다.");
            }

            steps.Add(new PlanStep(
                $"step-{i + 1:00}",
                ResolveStepTitle(stepText, i),
                stepText,
                mustDo,
                mustNotDo,
                verification
            ));
        }

        return steps;
    }

    private static PlanDraft? TryParseStructuredDraft(string rawDraft)
    {
        var normalized = (rawDraft ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        foreach (var candidate in EnumerateJsonCandidates(normalized))
        {
            try
            {
                var draft = PlanJson.DeserializeDraft(candidate);
                if (draft?.Steps != null && draft.Steps.Count > 0)
                {
                    return draft;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        yield return text;

        var fenced = Regex.Match(text, @"```(?:json)?\s*(?<json>\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (fenced.Success)
        {
            yield return fenced.Groups["json"].Value.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            yield return text[start..(end + 1)].Trim();
        }
    }

    private static IReadOnlyList<PlanStep> BuildStepsFromDraft(
        PlanDraft draft,
        string objective,
        IReadOnlyList<string> constraints,
        string mode
    )
    {
        var verificationHints = BuildVerificationHints(objective);
        var globalMustNotDo = constraints.Count == 0
            ? Array.Empty<string>()
            : constraints.ToArray();
        var sourceSteps = (draft.Steps ?? Array.Empty<PlanDraftStep>())
            .Where(step => !string.IsNullOrWhiteSpace(step.Title) || !string.IsNullOrWhiteSpace(step.Description))
            .Take(8)
            .ToArray();
        if (sourceSteps.Length == 0)
        {
            return BuildFallbackStepTexts(objective, mode)
                .Select((text, index) => BuildFallbackPlanStep(text, index, globalMustNotDo, verificationHints))
                .ToArray();
        }

        return sourceSteps.Select((step, index) =>
        {
            var description = Regex.Replace((step.Description ?? step.Title ?? $"단계 {index + 1}").Trim(), @"\s+", " ");
            var title = Regex.Replace((step.Title ?? description).Trim(), @"\s+", " ");
            var mustDo = NormalizePlanList(step.MustDo)
                .DefaultIfEmpty(description)
                .Take(8)
                .ToArray();
            var mustNotDo = NormalizePlanList(step.MustNotDo)
                .Concat(globalMustNotDo)
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray();
            var verification = NormalizePlanList(step.Verification)
                .DefaultIfEmpty(index == sourceSteps.Length - 1 ? verificationHints.Last() : "이 단계 산출물이 다음 단계 입력으로 충분한지 확인한다.")
                .Take(8)
                .ToArray();

            return new PlanStep(
                $"step-{index + 1:00}",
                title.Length > 56 ? title[..56].TrimEnd() : title,
                description,
                mustDo,
                mustNotDo,
                verification
            );
        }).ToArray();
    }

    private static PlanStep BuildFallbackPlanStep(
        string text,
        int index,
        IReadOnlyList<string> mustNotDo,
        IReadOnlyList<string> verificationHints
    )
    {
        return new PlanStep(
            $"step-{index + 1:00}",
            ResolveStepTitle(text, index),
            text,
            new[] { text },
            mustNotDo,
            index == 3 ? verificationHints : new[] { "이 단계 산출물이 다음 단계 입력으로 충분한지 확인한다." }
        );
    }

    private static IEnumerable<string> NormalizePlanList(IReadOnlyList<string>? items)
    {
        return (items ?? Array.Empty<string>())
            .Select(item => Regex.Replace((item ?? string.Empty).Trim(), @"\s+", " "))
            .Where(item => item.Length > 0);
    }

    private static IReadOnlyList<string> BuildFallbackStepTexts(string objective, string mode)
    {
        return mode == "interview"
            ? new[]
            {
                "요구사항과 모호한 지점을 정리하고, 바로 실행하면 위험한 부분을 식별한다.",
                "현재 저장소 구조와 관련 파일을 확인해 변경 범위를 좁힌다.",
                "가장 작은 단위부터 구현하고 필요한 문서를 같은 패치에 반영한다.",
                "빌드와 스모크 검증을 실행해 결과를 남긴다."
            }
            : new[]
            {
                $"목표를 실행 단위로 분해한다: {objective}",
                "관련 파일과 상태 경로를 확인해 변경 지점을 확정한다.",
                "가장 작은 단위부터 구현하고 부수 효과를 제한한다.",
                "변경 후 검증을 수행하고 남은 리스크를 정리한다."
            };
    }

    private static IReadOnlyList<string> BuildVerificationHints(string objective)
    {
        var hints = new List<string> { "변경 파일과 문서가 같은 방향으로 갱신되었는지 확인한다." };
        var normalized = (objective ?? string.Empty).ToLowerInvariant();

        if (normalized.Contains("dashboard", StringComparison.Ordinal)
            || normalized.Contains("ui", StringComparison.Ordinal)
            || normalized.Contains("frontend", StringComparison.Ordinal)
            || normalized.Contains("대시보드", StringComparison.Ordinal))
        {
            hints.Add("대시보드 변경이면 프런트엔드 검증을 실행한다.");
        }

        if (normalized.Contains("middleware", StringComparison.Ordinal)
            || normalized.Contains("backend", StringComparison.Ordinal)
            || normalized.Contains("c#", StringComparison.Ordinal)
            || normalized.Contains("미들웨어", StringComparison.Ordinal))
        {
            hints.Add("미들웨어 변경이면 dotnet build를 실행한다.");
        }

        hints.Add("실패한 검증이 있으면 성공으로 표현하지 않는다.");
        return hints;
    }

    private static string ResolveTitle(PlanDraft? draft, string rawDraft, string objective)
    {
        var draftTitle = Regex.Replace((draft?.Title ?? string.Empty).Trim(), @"\s+", " ");
        if (draftTitle.Length > 0)
        {
            return draftTitle.Length > 72 ? draftTitle[..72].TrimEnd() : draftTitle;
        }

        foreach (var line in (rawDraft ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed["TITLE:".Length..].Trim();
            if (value.Length > 0)
            {
                return value.Length > 72 ? value[..72].TrimEnd() : value;
            }
        }

        var safe = NormalizeObjective(objective);
        return safe.Length > 72 ? safe[..72].TrimEnd() : safe;
    }

    private static string ResolveStepTitle(string stepText, int index)
    {
        var normalized = Regex.Replace(stepText, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return $"단계 {index + 1}";
        }

        return normalized.Length > 56 ? normalized[..56].TrimEnd() : normalized;
    }

    private static string NormalizeMode(string? mode)
    {
        return string.Equals(mode?.Trim(), "interview", StringComparison.OrdinalIgnoreCase)
            ? "interview"
            : "fast";
    }

    private static string NormalizeObjective(string? objective)
    {
        var normalized = (objective ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return "목표가 비어 있습니다.";
        }

        return Regex.Replace(normalized, @"\s+", " ");
    }

    private static IReadOnlyList<string> NormalizeConstraints(IReadOnlyList<string>? constraints)
    {
        return (constraints ?? Array.Empty<string>())
            .Select(item => Regex.Replace((item ?? string.Empty).Trim(), @"\s+", " "))
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
    }

    private static string NormalizePlanId(string? planId)
    {
        return (planId ?? string.Empty).Trim();
    }

    private static string TrimBlock(string text, int maxChars)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars].TrimEnd() + "\n...(truncated)";
    }

    private static IReadOnlyList<ProviderAvailability> BuildPlanningAvailabilitySnapshot(string route)
    {
        var provider = (route ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<ProviderAvailability>();
        }

        return new[]
        {
            new ProviderAvailability(
                provider,
                true,
                "planning_route",
                provider.Equals("gemini", StringComparison.OrdinalIgnoreCase),
                true,
                provider.Equals("gemini", StringComparison.OrdinalIgnoreCase),
                provider.Equals("copilot", StringComparison.OrdinalIgnoreCase)
                    || provider.Equals("codex", StringComparison.OrdinalIgnoreCase),
                !provider.Equals("copilot", StringComparison.OrdinalIgnoreCase)
                    && !provider.Equals("codex", StringComparison.OrdinalIgnoreCase))
        };
    }
}
