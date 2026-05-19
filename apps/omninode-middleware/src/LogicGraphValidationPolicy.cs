namespace OmniNode.Middleware;

internal static class LogicGraphValidationPolicy
{
    public const string SchemaVersion = "logic.graph.v1";

    public static readonly IReadOnlySet<string> SupportedNodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "start",
        "end",
        "output",
        "if",
        "delay",
        "parallel_split",
        "parallel_join",
        "set_var",
        "template",
        "chat_single",
        "chat_orchestration",
        "chat_multi",
        "coding_single",
        "coding_orchestration",
        "coding_multi",
        "routine_run",
        "memory_search",
        "memory_get",
        "web_search",
        "web_fetch",
        "file_read",
        "file_write",
        "session_list",
        "session_spawn",
        "session_send",
        "cron_status",
        "cron_run",
        "browser_execute",
        "canvas_execute",
        "nodes_pending",
        "nodes_invoke",
        "telegram_stub"
    };

    public static readonly IReadOnlySet<string> SupportedOperators = new HashSet<string>(StringComparer.Ordinal)
    {
        "equals",
        "not_equals",
        "contains",
        "not_contains",
        "starts_with",
        "ends_with",
        "gt",
        "gte",
        "lt",
        "lte",
        "is_truthy",
        "is_falsy"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> BindableTargetPortsByType =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["end"] = new HashSet<string>(StringComparer.Ordinal) { "result" },
            ["output"] = new HashSet<string>(StringComparer.Ordinal) { "result" },
            ["if"] = new HashSet<string>(StringComparer.Ordinal) { "leftref" },
            ["set_var"] = new HashSet<string>(StringComparer.Ordinal) { "value" },
            ["template"] = new HashSet<string>(StringComparer.Ordinal) { "template" },
            ["chat_single"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["chat_orchestration"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["chat_multi"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["coding_single"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["coding_orchestration"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["coding_multi"] = new HashSet<string>(StringComparer.Ordinal) { "input" },
            ["routine_run"] = new HashSet<string>(StringComparer.Ordinal) { "task" },
            ["memory_search"] = new HashSet<string>(StringComparer.Ordinal) { "query" },
            ["web_search"] = new HashSet<string>(StringComparer.Ordinal) { "query" },
            ["file_write"] = new HashSet<string>(StringComparer.Ordinal) { "content" },
            ["session_send"] = new HashSet<string>(StringComparer.Ordinal) { "message" },
            ["telegram_stub"] = new HashSet<string>(StringComparer.Ordinal) { "text" }
        };

    public static string NormalizePort(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "main" : normalized;
    }

    public static string NormalizeOperator(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "=" or "==" => "equals",
            "!=" or "<>" => "not_equals",
            ">=" => "gte",
            "<=" => "lte",
            ">" => "gt",
            "<" => "lt",
            "truthy" => "is_truthy",
            "falsy" => "is_falsy",
            "notequals" => "not_equals",
            "startswith" => "starts_with",
            "endswith" => "ends_with",
            "notcontains" => "not_contains",
            _ when SupportedOperators.Contains(normalized) => normalized,
            _ => "equals"
        };
    }

    public static bool IsSourcePortValid(string nodeType, string? sourcePort)
    {
        var port = NormalizePort(sourcePort);
        return nodeType switch
        {
            "if" => port is "true" or "false",
            "parallel_split" => !string.IsNullOrWhiteSpace(port),
            _ => port == "main"
        };
    }

    public static bool IsTargetPortValid(string nodeType, string? targetPort)
    {
        var port = NormalizePort(targetPort);
        return nodeType switch
        {
            "parallel_join" => !string.IsNullOrWhiteSpace(port),
            "start" => false,
            _ => port == "main"
                || (BindableTargetPortsByType.TryGetValue(nodeType, out var allowedPorts)
                    && allowedPorts.Contains(port))
        };
    }

    public static bool HasCycle(
        IReadOnlyList<LogicNodeDefinition> nodes,
        IReadOnlyList<LogicEdgeDefinition> edges
    )
    {
        var indegree = nodes.ToDictionary(node => node.NodeId, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.ToDictionary(node => node.NodeId, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            indegree[edge.TargetNodeId] += 1;
            outgoing[edge.SourceNodeId].Add(edge.TargetNodeId);
        }

        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            visited += 1;
            foreach (var targetNodeId in outgoing[nodeId])
            {
                indegree[targetNodeId] -= 1;
                if (indegree[targetNodeId] == 0)
                {
                    queue.Enqueue(targetNodeId);
                }
            }
        }

        return visited != nodes.Count;
    }

    public static LogicGraphValidationResult Validate(LogicGraphDefinition graph)
    {
        if (!string.Equals(graph.Version, SchemaVersion, StringComparison.Ordinal))
        {
            return new LogicGraphValidationResult(false, $"지원하지 않는 그래프 포맷입니다: {graph.Version}");
        }

        if (graph.Nodes.Count == 0)
        {
            return new LogicGraphValidationResult(false, "노드가 비어 있습니다.");
        }

        var enabledNodes = graph.Nodes.Where(node => node.Enabled).ToArray();
        if (enabledNodes.Length == 0)
        {
            return new LogicGraphValidationResult(false, "활성 노드가 하나 이상 필요합니다.");
        }

        var duplicatedNodeId = enabledNodes
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatedNodeId != null)
        {
            return new LogicGraphValidationResult(false, $"중복 nodeId가 있습니다: {duplicatedNodeId.Key}");
        }

        foreach (var node in enabledNodes)
        {
            if (!SupportedNodeTypes.Contains(node.Type))
            {
                return new LogicGraphValidationResult(false, $"지원하지 않는 노드 타입입니다: {node.Type}");
            }
        }

        var startCount = enabledNodes.Count(node => node.Type == "start");
        if (startCount != 1)
        {
            return new LogicGraphValidationResult(false, "start 노드는 정확히 1개여야 합니다.");
        }

        var endCount = enabledNodes.Count(node => node.Type == "end" || node.Type == "output");
        if (endCount < 1)
        {
            return new LogicGraphValidationResult(false, "end 또는 output 노드가 하나 이상 필요합니다.");
        }

        var enabledNodeMap = enabledNodes.ToDictionary(node => node.NodeId, node => node, StringComparer.Ordinal);
        var duplicatedEdgeId = graph.Edges
            .GroupBy(edge => edge.EdgeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatedEdgeId != null)
        {
            return new LogicGraphValidationResult(false, $"중복 edgeId가 있습니다: {duplicatedEdgeId.Key}");
        }

        var duplicatedInputPort = graph.Edges
            .GroupBy(edge => $"{edge.TargetNodeId}:{NormalizePort(edge.TargetPort)}", StringComparer.Ordinal)
            .FirstOrDefault(group =>
            {
                if (group.Count() < 2)
                {
                    return false;
                }

                var sample = group.FirstOrDefault();
                return sample != null
                    && enabledNodeMap.TryGetValue(sample.TargetNodeId, out var node)
                    && node.Type != "parallel_join";
            });
        if (duplicatedInputPort != null)
        {
            return new LogicGraphValidationResult(false, $"같은 입력 칸에는 연결을 하나만 둘 수 있습니다: {duplicatedInputPort.Key}");
        }

        foreach (var edge in graph.Edges)
        {
            if (!enabledNodeMap.TryGetValue(edge.SourceNodeId, out var sourceNode)
                || !enabledNodeMap.TryGetValue(edge.TargetNodeId, out var targetNode))
            {
                return new LogicGraphValidationResult(false, $"연결이 끊긴 edge가 있습니다: {edge.EdgeId}");
            }

            if (sourceNode.Type == "end")
            {
                return new LogicGraphValidationResult(false, $"end 노드는 outgoing edge를 가질 수 없습니다: {edge.EdgeId}");
            }

            if (targetNode.Type == "start")
            {
                return new LogicGraphValidationResult(false, $"start 노드는 incoming edge를 가질 수 없습니다: {edge.EdgeId}");
            }

            if (!IsSourcePortValid(sourceNode.Type, edge.SourcePort))
            {
                return new LogicGraphValidationResult(false, $"포트 타입이 맞지 않습니다: {edge.EdgeId}");
            }

            if (!IsTargetPortValid(targetNode.Type, edge.TargetPort))
            {
                return new LogicGraphValidationResult(false, $"포트 타입이 맞지 않습니다: {edge.EdgeId}");
            }

            if (edge.Condition != null)
            {
                if (string.IsNullOrWhiteSpace(edge.Condition.LeftRef))
                {
                    return new LogicGraphValidationResult(false, $"edge condition leftRef가 필요합니다: {edge.EdgeId}");
                }

                if (!SupportedOperators.Contains(NormalizeOperator(edge.Condition.Operator)))
                {
                    return new LogicGraphValidationResult(false, $"지원하지 않는 edge operator입니다: {edge.EdgeId}");
                }
            }
        }

        foreach (var joinNode in enabledNodes.Where(node => node.Type == "parallel_join"))
        {
            var incomingCount = graph.Edges.Count(edge => edge.TargetNodeId == joinNode.NodeId);
            if (incomingCount < 2)
            {
                return new LogicGraphValidationResult(false, $"parallel_join 노드는 선행 노드가 2개 이상이어야 합니다: {joinNode.NodeId}");
            }
        }

        if (HasCycle(enabledNodes, graph.Edges.Where(edge =>
            enabledNodeMap.ContainsKey(edge.SourceNodeId) && enabledNodeMap.ContainsKey(edge.TargetNodeId)).ToArray()))
        {
            return new LogicGraphValidationResult(false, "작업 흐름은 순환 없이 이어져야 합니다. 되돌아가는 연결이 있습니다.");
        }

        return new LogicGraphValidationResult(true, string.Empty);
    }
}
