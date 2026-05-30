namespace OmniNode.Middleware;

/// <summary>
/// 인스턴스 서비스에 의존하지 않는 순수 로직 노드(start/end/output/template/set_var/if/parallel pass)의 실행기.
/// config/context와 정책(LogicTemplateResolver/LogicValueParsingPolicy/LogicGraphValidationPolicy)만 사용한다.
/// </summary>
internal static class LogicLeafNodeExecutor
{
    public const string MainInputKey = "__main_input";

    public static LogicNodeExecutionOutcome ExecuteStart(LogicNodeDefinition node, LogicExecutionContext context)
    {
        var text = string.IsNullOrWhiteSpace(context.RunInput)
            ? "흐름 시작"
            : context.RunInput;
        return new LogicNodeExecutionOutcome(Envelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["input"] = context.RunInput
            }
        ));
    }

    public static LogicNodeExecutionOutcome ExecuteEnd(
        LogicNodeDefinition node,
        LogicExecutionContext context,
        IReadOnlyDictionary<string, string> config
    )
    {
        var text = LogicValueParsingPolicy.FirstNonEmpty(
            config.TryGetValue("result", out var resultValue) ? resultValue : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            config.TryGetValue(MainInputKey, out var mainInput) ? mainInput : null,
            context.Nodes.Values.LastOrDefault()?.Text,
            "흐름 실행이 끝났습니다."
        );
        return new LogicNodeExecutionOutcome(Envelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["result"] = text
            }
        ));
    }

    public static LogicNodeExecutionOutcome ExecuteOutput(
        LogicNodeDefinition node,
        LogicExecutionContext context,
        IReadOnlyDictionary<string, string> config
    )
    {
        var text = LogicValueParsingPolicy.FirstNonEmpty(
            config.TryGetValue("result", out var resultValue) ? resultValue : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            config.TryGetValue(MainInputKey, out var mainInput) ? mainInput : null,
            context.Nodes.Values.LastOrDefault()?.Text,
            string.Empty
        );
        return new LogicNodeExecutionOutcome(Envelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["result"] = text
            }
        ));
    }

    public static LogicNodeExecutionOutcome ExecuteTemplate(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        var text = LogicValueParsingPolicy.FirstNonEmpty(
            config.TryGetValue("template", out var template) ? template : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            string.Empty
        );
        return new LogicNodeExecutionOutcome(Envelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rendered"] = text
            }
        ));
    }

    public static LogicNodeExecutionOutcome ExecuteSetVar(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        LogicExecutionContext context
    )
    {
        var name = (config.TryGetValue("name", out var rawName) ? rawName : string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return new LogicNodeExecutionOutcome(Envelope(
                ok: false,
                type: node.Type,
                text: "변수 이름이 필요합니다.",
                data: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["error"] = "name_required"
                }
            ));
        }

        var value = LogicValueParsingPolicy.FirstNonEmpty(
            config.TryGetValue("value", out var rawValue) ? rawValue : null,
            config.TryGetValue("text", out var textValue) ? textValue : null,
            string.Empty
        );
        context.Vars[name] = value;
        return new LogicNodeExecutionOutcome(Envelope(
            ok: true,
            type: node.Type,
            text: value,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["value"] = value
            }
        ));
    }

    public static LogicNodeExecutionOutcome ExecuteIf(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        LogicExecutionContext context
    )
    {
        var condition = ResolveCondition(node, config);
        var left = LogicTemplateResolver.ResolveReference(condition.LeftRef, context);
        var matched = LogicValueParsingPolicy.EvaluateCondition(left, condition.Operator, condition.RightValue);
        var branch = matched ? "true" : "false";
        var payload = LogicValueParsingPolicy.FirstNonEmpty(
            config.TryGetValue(MainInputKey, out var mainInput) ? mainInput : null,
            left,
            matched ? "조건 참" : "조건 거짓"
        );
        return new LogicNodeExecutionOutcome(
            Envelope(
                ok: true,
                type: node.Type,
                text: payload,
                data: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["left"] = left,
                    ["operator"] = condition.Operator,
                    ["right"] = condition.RightValue,
                    ["branch"] = branch
                }
            ),
            branch
        );
    }

    public static LogicNodeExecutionOutcome ExecutePass(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config,
        string fallbackText
    )
    {
        var text = LogicValueParsingPolicy.FirstNonEmpty(
            config.TryGetValue(MainInputKey, out var mainInput) ? mainInput : null,
            fallbackText
        );
        return new LogicNodeExecutionOutcome(Envelope(
            ok: true,
            type: node.Type,
            text: text,
            data: new Dictionary<string, string>(StringComparer.Ordinal)
        ));
    }

    internal static LogicEdgeCondition ResolveCondition(
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, string> config
    )
    {
        node.Config.TryGetValue("leftRef", out var originalLeft);
        node.Config.TryGetValue("operator", out var originalOperator);
        node.Config.TryGetValue("rightValue", out var originalRight);
        return new LogicEdgeCondition
        {
            LeftRef = LogicValueParsingPolicy.FirstNonEmpty(
                config.TryGetValue("leftRef", out var fallbackLeft) ? fallbackLeft : originalLeft,
                string.Empty
            ),
            Operator = LogicGraphValidationPolicy.NormalizeOperator(LogicValueParsingPolicy.FirstNonEmpty(
                config.TryGetValue("operator", out var fallbackOperator) ? fallbackOperator : originalOperator,
                "equals"
            )),
            RightValue = LogicValueParsingPolicy.FirstNonEmpty(
                config.TryGetValue("rightValue", out var fallbackRight) ? fallbackRight : originalRight,
                string.Empty
            )
        };
    }

    private static LogicNodeResultEnvelope Envelope(
        bool ok,
        string type,
        string text,
        IReadOnlyDictionary<string, string>? data = null,
        IReadOnlyList<string>? artifacts = null
    )
    {
        return new LogicNodeResultEnvelope(
            ok,
            type,
            text ?? string.Empty,
            data ?? new Dictionary<string, string>(StringComparer.Ordinal),
            artifacts ?? Array.Empty<string>(),
            null,
            null,
            Array.Empty<string>()
        );
    }
}
