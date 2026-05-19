using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class LogicNodeRuntimePolicy
{
    public static readonly Regex TemplateRegex =
        new(@"\{\{\s*(?<expr>[^{}]+?)\s*\}\}", RegexOptions.Compiled);

    public static bool IsSuccessfulExecutionStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        return normalized.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("success", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTerminalStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        return normalized.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("error", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("timeout", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("killed", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeAiFailure(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("API 키가 설정되지 않았습니다", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("인증이 필요합니다", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("호출 오류", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("요청 실패", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("응답 시간이 초과", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunInputTemplate(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var match = TemplateRegex.Match(text);
        if (!match.Success || match.Length != text.Length)
        {
            return false;
        }

        var expression = match.Groups["expr"].Value.Trim();
        return string.Equals(expression, "run.input", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldApplyImplicitMainInput(
        LogicNodeDefinition node,
        string targetPort
    )
    {
        if (node.Config.TryGetValue($"__mode__{targetPort}", out var rawMode))
        {
            var mode = (rawMode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode is "reference" or "edge")
            {
                return false;
            }
        }

        if (!node.Config.TryGetValue(targetPort, out var rawValue))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        return IsRunInputTemplate(rawValue);
    }

    public static bool RequiresAllIncomingEdges(IReadOnlyList<LogicEdgeDefinition> edges)
    {
        return edges.Any(edge => !string.Equals(
            LogicGraphValidationPolicy.NormalizePort(edge.TargetPort),
            "main",
            StringComparison.Ordinal
        ));
    }

    public static bool IsNodeReadyToRun(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, HashSet<string>> arrivals,
        IReadOnlyDictionary<string, LogicEdgeDefinition[]> incomingEdges
    )
    {
        if (node.Type == "start")
        {
            return true;
        }

        if (!arrivals.TryGetValue(node.NodeId, out var sources) || sources.Count == 0)
        {
            return false;
        }

        if (!incomingEdges.TryGetValue(node.NodeId, out var edges) || edges.Length == 0)
        {
            return true;
        }

        if (node.Type == "parallel_join" || RequiresAllIncomingEdges(edges))
        {
            return sources.Count >= edges.Length;
        }

        return true;
    }

    public static string BuildConversationTitle(
        LogicGraphDefinition graph,
        LogicNodeDefinition node,
        string mode
    )
    {
        return $"{graph.Title} · {node.Title} · {mode}";
    }
}
