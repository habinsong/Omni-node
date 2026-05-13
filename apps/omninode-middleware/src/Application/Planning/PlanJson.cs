using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniNode.Middleware;

internal static class PlanJson
{
    private static readonly PlanJsonContext BaseContext = new(CreateOptions(indented: false));
    private static readonly PlanJsonContext IndentedContext = new(CreateOptions(indented: true));

    public static string Serialize(WorkPlan plan, bool indented = false)
    {
        return JsonSerializer.Serialize(plan, (indented ? IndentedContext : BaseContext).WorkPlan);
    }

    public static string Serialize(PlanReviewResult review, bool indented = false)
    {
        return JsonSerializer.Serialize(review, (indented ? IndentedContext : BaseContext).PlanReviewResult);
    }

    public static string Serialize(PlanExecutionRecord execution, bool indented = false)
    {
        return JsonSerializer.Serialize(execution, (indented ? IndentedContext : BaseContext).PlanExecutionRecord);
    }

    public static string Serialize(PlanSnapshot snapshot, bool indented = false)
    {
        return JsonSerializer.Serialize(snapshot, (indented ? IndentedContext : BaseContext).PlanSnapshot);
    }

    public static string Serialize(PlanActionResult result, bool indented = false)
    {
        return JsonSerializer.Serialize(result, (indented ? IndentedContext : BaseContext).PlanActionResult);
    }

    public static string Serialize(PlanListResult result, bool indented = false)
    {
        return JsonSerializer.Serialize(result, (indented ? IndentedContext : BaseContext).PlanListResult);
    }

    public static string Serialize(PlanIndexState state, bool indented = false)
    {
        return JsonSerializer.Serialize(state, (indented ? IndentedContext : BaseContext).PlanIndexState);
    }

    public static WorkPlan? DeserializePlan(string json)
    {
        return JsonSerializer.Deserialize(json, BaseContext.WorkPlan);
    }

    public static PlanReviewResult? DeserializeReview(string json)
    {
        return JsonSerializer.Deserialize(json, BaseContext.PlanReviewResult);
    }

    public static PlanExecutionRecord? DeserializeExecution(string json)
    {
        return JsonSerializer.Deserialize(json, BaseContext.PlanExecutionRecord);
    }

    public static PlanIndexState? DeserializeIndexState(string json)
    {
        return JsonSerializer.Deserialize(json, BaseContext.PlanIndexState);
    }

    public static PlanDraft? DeserializeDraft(string json)
    {
        return JsonSerializer.Deserialize(json, BaseContext.PlanDraft);
    }

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
