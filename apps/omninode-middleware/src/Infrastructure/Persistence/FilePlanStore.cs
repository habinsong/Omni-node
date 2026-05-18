using System.Text;

namespace OmniNode.Middleware;

public sealed class FilePlanStore
{
    private const string IndexFileName = "index.json";
    private const string PlanFileName = "plan.json";
    private const string ReviewFileName = "review.json";
    private const string ExecutionFileName = "execution.json";

    private readonly IStatePathResolver _pathResolver;

    public FilePlanStore(IStatePathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public IReadOnlyList<PlanIndexEntry> LoadIndex()
    {
        var indexPath = _pathResolver.GetPlansIndexPath();
        if (!File.Exists(indexPath))
        {
            return Array.Empty<PlanIndexEntry>();
        }

        try
        {
            var json = File.ReadAllText(indexPath, Encoding.UTF8);
            var state = PlanJson.DeserializeIndexState(json);
            if (state?.Items == null)
            {
                return Array.Empty<PlanIndexEntry>();
            }

            return state.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.PlanId))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ToArray();
        }
        catch
        {
            return Array.Empty<PlanIndexEntry>();
        }
    }

    public WorkPlan? TryLoadPlan(string planId)
    {
        var path = Path.Combine(GetPlanDirectory(planId), PlanFileName);
        return ReadJson(path, PlanJson.DeserializePlan);
    }

    public PlanReviewResult? TryLoadReview(string planId)
    {
        var path = Path.Combine(GetPlanDirectory(planId), ReviewFileName);
        return ReadJson(path, PlanJson.DeserializeReview);
    }

    public PlanExecutionRecord? TryLoadExecution(string planId)
    {
        var path = Path.Combine(GetPlanDirectory(planId), ExecutionFileName);
        return ReadJson(path, PlanJson.DeserializeExecution);
    }

    public void SavePlan(WorkPlan plan)
    {
        var planDirectory = GetPlanDirectory(plan.PlanId);
        Directory.CreateDirectory(planDirectory);
        AtomicFileStore.WriteAllText(
            Path.Combine(planDirectory, PlanFileName),
            PlanJson.Serialize(plan, indented: true),
            ownerOnly: true
        );

        SaveIndexEntry(new PlanIndexEntry(
            plan.PlanId,
            plan.Title,
            plan.Objective,
            plan.Status,
            plan.CreatedAtUtc,
            plan.UpdatedAtUtc,
            plan.ReviewerSummary
        ));
    }

    public void SaveReview(PlanReviewResult review)
    {
        var planDirectory = GetPlanDirectory(review.PlanId);
        Directory.CreateDirectory(planDirectory);
        AtomicFileStore.WriteAllText(
            Path.Combine(planDirectory, ReviewFileName),
            PlanJson.Serialize(review, indented: true),
            ownerOnly: true
        );
    }

    public void SaveExecution(PlanExecutionRecord execution)
    {
        var planDirectory = GetPlanDirectory(execution.PlanId);
        Directory.CreateDirectory(planDirectory);
        AtomicFileStore.WriteAllText(
            Path.Combine(planDirectory, ExecutionFileName),
            PlanJson.Serialize(execution, indented: true),
            ownerOnly: true
        );
    }

    public void DeleteReview(string planId)
    {
        DeleteIfExists(Path.Combine(GetPlanDirectory(planId), ReviewFileName));
    }

    public void DeleteExecution(string planId)
    {
        DeleteIfExists(Path.Combine(GetPlanDirectory(planId), ExecutionFileName));
    }

    private void SaveIndexEntry(PlanIndexEntry nextEntry)
    {
        var current = LoadIndex()
            .Where(item => !item.PlanId.Equals(nextEntry.PlanId, StringComparison.Ordinal))
            .ToList();
        current.Add(nextEntry);
        var ordered = current
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ToArray();
        var state = new PlanIndexState { Items = ordered };
        AtomicFileStore.WriteAllText(
            _pathResolver.GetPlansIndexPath(),
            PlanJson.Serialize(state, indented: true),
            ownerOnly: true
        );
    }

    private string GetPlanDirectory(string planId)
    {
        return Path.Combine(_pathResolver.GetPlansRoot(), planId.Trim());
    }

    private static T? ReadJson<T>(string path, Func<string, T?> parser)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return parser(json);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
