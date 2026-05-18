namespace OmniNode.Middleware;

public sealed class RoutingPolicy
{
    public string[]? GeneralChat { get; set; }
    public string[]? Planner { get; set; }
    public string[]? Reviewer { get; set; }
    public string[]? SearchTimeSensitive { get; set; }
    public string[]? SearchFallback { get; set; }
    public string[]? DeepCode { get; set; }
    public string[]? SafeRefactor { get; set; }
    public string[]? QuickFix { get; set; }
    public string[]? VisualUi { get; set; }
    public string[]? RoutineBuilder { get; set; }
    public string[]? BackgroundMonitor { get; set; }
    public string[]? Documentation { get; set; }

    public RoutingPolicy Clone()
    {
        return new RoutingPolicy
        {
            GeneralChat = CloneChain(GeneralChat),
            Planner = CloneChain(Planner),
            Reviewer = CloneChain(Reviewer),
            SearchTimeSensitive = CloneChain(SearchTimeSensitive),
            SearchFallback = CloneChain(SearchFallback),
            DeepCode = CloneChain(DeepCode),
            SafeRefactor = CloneChain(SafeRefactor),
            QuickFix = CloneChain(QuickFix),
            VisualUi = CloneChain(VisualUi),
            RoutineBuilder = CloneChain(RoutineBuilder),
            BackgroundMonitor = CloneChain(BackgroundMonitor),
            Documentation = CloneChain(Documentation)
        };
    }

    public IReadOnlyList<string>? GetChain(TaskCategory category)
    {
        return category switch
        {
            TaskCategory.GeneralChat => GeneralChat,
            TaskCategory.Planner => Planner,
            TaskCategory.Reviewer => Reviewer,
            TaskCategory.SearchTimeSensitive => SearchTimeSensitive,
            TaskCategory.SearchFallback => SearchFallback,
            TaskCategory.DeepCode => DeepCode,
            TaskCategory.SafeRefactor => SafeRefactor,
            TaskCategory.QuickFix => QuickFix,
            TaskCategory.VisualUi => VisualUi,
            TaskCategory.RoutineBuilder => RoutineBuilder,
            TaskCategory.BackgroundMonitor => BackgroundMonitor,
            TaskCategory.Documentation => Documentation,
            _ => null
        };
    }

    public void SetChain(TaskCategory category, IReadOnlyList<string>? chain)
    {
        var normalized = chain == null ? null : chain.ToArray();
        switch (category)
        {
            case TaskCategory.GeneralChat:
                GeneralChat = normalized;
                break;
            case TaskCategory.Planner:
                Planner = normalized;
                break;
            case TaskCategory.Reviewer:
                Reviewer = normalized;
                break;
            case TaskCategory.SearchTimeSensitive:
                SearchTimeSensitive = normalized;
                break;
            case TaskCategory.SearchFallback:
                SearchFallback = normalized;
                break;
            case TaskCategory.DeepCode:
                DeepCode = normalized;
                break;
            case TaskCategory.SafeRefactor:
                SafeRefactor = normalized;
                break;
            case TaskCategory.QuickFix:
                QuickFix = normalized;
                break;
            case TaskCategory.VisualUi:
                VisualUi = normalized;
                break;
            case TaskCategory.RoutineBuilder:
                RoutineBuilder = normalized;
                break;
            case TaskCategory.BackgroundMonitor:
                BackgroundMonitor = normalized;
                break;
            case TaskCategory.Documentation:
                Documentation = normalized;
                break;
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ToDictionary()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var category in TaskCategoryMetadata.All)
        {
            var chain = GetChain(category);
            if (chain == null || chain.Count == 0)
            {
                continue;
            }

            result[TaskCategoryMetadata.ToPolicyKey(category)] = chain.ToArray();
        }

        return result;
    }

    private static string[]? CloneChain(string[]? chain)
    {
        return chain == null ? null : chain.ToArray();
    }
}

internal static class RoutingPolicyJson
{
    public static bool IsValidPolicyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    public static RoutingPolicy DeserializePolicy(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RoutingPolicy();
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return new RoutingPolicy();
            }

            var policy = new RoutingPolicy();
            foreach (var category in TaskCategoryMetadata.All)
            {
                if (!doc.RootElement.TryGetProperty(TaskCategoryMetadata.ToPolicyKey(category), out var chainElement)
                    || chainElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                var chain = chainElement.EnumerateArray()
                    .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .ToArray();
                policy.SetChain(category, chain);
            }

            return policy;
        }
        catch (System.Text.Json.JsonException)
        {
            return new RoutingPolicy();
        }
    }

    public static string SerializePolicy(RoutingPolicy policy, bool indented = true)
    {
        return SerializeChainsObject(policy.ToDictionary(), indented, indentLevel: 0);
    }

    public static string SerializeActionResult(RoutingPolicyActionResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"message\":\"{WebSocketGateway.EscapeJson(result.Message)}\",");
        builder.Append("\"snapshot\":");
        builder.Append(SerializeSnapshot(result.Snapshot));
        builder.Append("}");
        return builder.ToString();
    }

    public static string SerializeSnapshot(RoutingPolicySnapshot snapshot)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{");
        builder.Append("\"defaultChains\":");
        builder.Append(SerializeChainsObject(snapshot.DefaultChains, indented: false, indentLevel: 0));
        builder.Append(",");
        builder.Append("\"overrideChains\":");
        builder.Append(SerializeChainsObject(snapshot.OverrideChains, indented: false, indentLevel: 0));
        builder.Append(",");
        builder.Append("\"effectiveChains\":");
        builder.Append(SerializeChainsObject(snapshot.EffectiveChains, indented: false, indentLevel: 0));
        builder.Append(",");
        builder.Append("\"lastDecision\":");
        builder.Append(snapshot.LastDecision == null ? "null" : SerializeDecision(snapshot.LastDecision));
        builder.Append("}");
        return builder.ToString();
    }

    public static string SerializeDecision(RoutingDecision decision)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{");
        builder.Append($"\"decisionId\":\"{WebSocketGateway.EscapeJson(decision.DecisionId)}\",");
        builder.Append($"\"category\":\"{WebSocketGateway.EscapeJson(decision.Category.ToString())}\",");
        builder.Append($"\"categoryKey\":\"{WebSocketGateway.EscapeJson(decision.CategoryKey)}\",");
        builder.Append($"\"categoryLabel\":\"{WebSocketGateway.EscapeJson(decision.CategoryLabel)}\",");
        builder.Append($"\"decidedAtUtc\":\"{WebSocketGateway.EscapeJson(decision.DecidedAtUtc.ToString("O"))}\",");
        builder.Append($"\"requestedProvider\":\"{WebSocketGateway.EscapeJson(decision.RequestedProvider)}\",");
        builder.Append($"\"resolvedProvider\":\"{WebSocketGateway.EscapeJson(decision.ResolvedProvider)}\",");
        builder.Append("\"providerChain\":");
        builder.Append(SerializeStringArray(decision.ProviderChain));
        builder.Append(",");
        builder.Append("\"availableProviders\":");
        builder.Append(SerializeStringArray(decision.AvailableProviders));
        builder.Append(",");
        builder.Append($"\"reason\":\"{WebSocketGateway.EscapeJson(decision.Reason)}\"");
        builder.Append("}");
        return builder.ToString();
    }

    private static string SerializeChainsObject(
        IReadOnlyDictionary<string, IReadOnlyList<string>> chains,
        bool indented,
        int indentLevel
    )
    {
        var builder = new System.Text.StringBuilder();
        var indent = indented ? new string(' ', indentLevel * 2) : string.Empty;
        var innerIndent = indented ? new string(' ', (indentLevel + 1) * 2) : string.Empty;
        builder.Append("{");
        if (indented && chains.Count > 0)
        {
            builder.AppendLine();
        }

        var ordered = chains
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < ordered.Length; i += 1)
        {
            var item = ordered[i];
            if (indented)
            {
                builder.Append(innerIndent);
            }

            builder.Append($"\"{WebSocketGateway.EscapeJson(item.Key)}\":");
            builder.Append(SerializeStringArray(item.Value, indented, indentLevel + 1));
            if (i < ordered.Length - 1)
            {
                builder.Append(",");
            }

            if (indented)
            {
                builder.AppendLine();
            }
        }

        if (indented && ordered.Length > 0)
        {
            builder.Append(indent);
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static string SerializeStringArray(IReadOnlyList<string> items, bool indented = false, int indentLevel = 0)
    {
        var builder = new System.Text.StringBuilder();
        if (!indented)
        {
            builder.Append("[");
            for (var i = 0; i < items.Count; i += 1)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                builder.Append($"\"{WebSocketGateway.EscapeJson(items[i])}\"");
            }

            builder.Append("]");
            return builder.ToString();
        }

        var indent = new string(' ', indentLevel * 2);
        var innerIndent = new string(' ', (indentLevel + 1) * 2);
        builder.Append("[");
        if (items.Count > 0)
        {
            builder.AppendLine();
        }

        for (var i = 0; i < items.Count; i += 1)
        {
            builder.Append(innerIndent);
            builder.Append($"\"{WebSocketGateway.EscapeJson(items[i])}\"");
            if (i < items.Count - 1)
            {
                builder.Append(",");
            }

            builder.AppendLine();
        }

        if (items.Count > 0)
        {
            builder.Append(indent);
        }

        builder.Append("]");
        return builder.ToString();
    }
}
