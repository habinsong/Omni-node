using System.Globalization;

namespace OmniNode.Middleware;

/// <summary>
/// 로직 그래프 실행 컨텍스트에서 <c>{{ ... }}</c> 템플릿과 <c>vars./sessions./artifacts./nodes./run.input</c>
/// 참조를 해석하고, edge source port 값을 추출하는 순수 resolver.
/// </summary>
internal static class LogicTemplateResolver
{
    public static string ResolveTemplate(string? template, LogicExecutionContext context)
    {
        var raw = template ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return LogicNodeRuntimePolicy.TemplateRegex.Replace(raw, match =>
        {
            var expr = match.Groups["expr"].Value;
            return ResolveReference(expr, context);
        });
    }

    public static string ResolveReference(string? reference, LogicExecutionContext context)
    {
        var raw = (reference ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (raw.StartsWith("{{", StringComparison.Ordinal) && raw.EndsWith("}}", StringComparison.Ordinal))
        {
            raw = raw[2..^2].Trim();
        }

        if (string.Equals(raw, "run.input", StringComparison.Ordinal))
        {
            return context.RunInput;
        }

        if (raw.StartsWith("vars.", StringComparison.Ordinal))
        {
            return context.Vars.TryGetValue(raw[5..], out var value) ? value : string.Empty;
        }

        if (raw.StartsWith("sessions.", StringComparison.Ordinal))
        {
            return context.Sessions.TryGetValue(raw[9..], out var value) ? value : string.Empty;
        }

        if (raw.StartsWith("artifacts.", StringComparison.Ordinal))
        {
            var suffix = raw[10..];
            if (string.Equals(suffix, "last", StringComparison.Ordinal))
            {
                return context.Artifacts.LastOrDefault() ?? string.Empty;
            }

            if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < context.Artifacts.Count)
            {
                return context.Artifacts[index];
            }

            return string.Empty;
        }

        if (raw.StartsWith("nodes.", StringComparison.Ordinal))
        {
            var parts = raw.Split('.', 4, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
            {
                return string.Empty;
            }

            if (!context.Nodes.TryGetValue(parts[1], out var nodeResult))
            {
                return string.Empty;
            }

            return parts[2] switch
            {
                "text" => nodeResult.Text,
                "type" => nodeResult.Type,
                "ok" => nodeResult.Ok ? "true" : "false",
                "conversationId" => nodeResult.ConversationId ?? string.Empty,
                "sessionKey" => nodeResult.SessionKey ?? string.Empty,
                "data" when parts.Length == 4 => nodeResult.Data.TryGetValue(parts[3], out var value) ? value : string.Empty,
                _ => string.Empty
            };
        }

        return raw;
    }

    public static string ResolveEdgeValue(LogicEdgeDefinition edge, LogicExecutionContext context)
    {
        if (!context.Nodes.TryGetValue(edge.SourceNodeId, out var source))
        {
            return string.Empty;
        }

        var sourcePort = LogicGraphValidationPolicy.NormalizePort(edge.SourcePort);
        if (string.Equals(sourcePort, "main", StringComparison.Ordinal)
            || string.Equals(sourcePort, "text", StringComparison.Ordinal)
            || string.Equals(sourcePort, "true", StringComparison.Ordinal)
            || string.Equals(sourcePort, "false", StringComparison.Ordinal))
        {
            return source.Text;
        }

        if (string.Equals(sourcePort, "session", StringComparison.Ordinal))
        {
            return LogicValueParsingPolicy.FirstNonEmpty(source.SessionKey, source.ConversationId);
        }

        if (string.Equals(sourcePort, "conversation", StringComparison.Ordinal))
        {
            return source.ConversationId ?? string.Empty;
        }

        if (string.Equals(sourcePort, "artifact", StringComparison.Ordinal))
        {
            return source.Artifacts.LastOrDefault() ?? string.Empty;
        }

        if (sourcePort.StartsWith("data.", StringComparison.Ordinal))
        {
            var dataKey = sourcePort[5..];
            return source.Data.TryGetValue(dataKey, out var value) ? value : string.Empty;
        }

        return source.Text;
    }
}
