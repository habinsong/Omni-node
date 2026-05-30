using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class LogicLeafNodeExecutorTests
{
    private static LogicNodeDefinition Node(string type) => new() { NodeId = "n", Type = type };

    private static Dictionary<string, string> Config(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
        {
            dict[k] = v;
        }
        return dict;
    }

    [Fact]
    public void ExecuteStartUsesRunInputOrFallback()
    {
        var ctx = new LogicExecutionContext { RunInput = "hi there" };
        var outcome = LogicLeafNodeExecutor.ExecuteStart(Node("start"), ctx);
        Assert.True(outcome.Envelope.Ok);
        Assert.Equal("hi there", outcome.Envelope.Text);
        Assert.Equal("hi there", outcome.Envelope.Data["input"]);

        var emptyCtx = new LogicExecutionContext { RunInput = "  " };
        Assert.Equal("흐름 시작", LogicLeafNodeExecutor.ExecuteStart(Node("start"), emptyCtx).Envelope.Text);
    }

    [Fact]
    public void ExecuteEndPrefersResultThenTextThenMainInputThenLastNode()
    {
        var ctx = new LogicExecutionContext();
        Assert.Equal("R", LogicLeafNodeExecutor.ExecuteEnd(Node("end"), ctx, Config(("result", "R"), ("text", "T"))).Envelope.Text);
        Assert.Equal("T", LogicLeafNodeExecutor.ExecuteEnd(Node("end"), ctx, Config(("text", "T"))).Envelope.Text);
        Assert.Equal("M", LogicLeafNodeExecutor.ExecuteEnd(Node("end"), ctx, Config((LogicLeafNodeExecutor.MainInputKey, "M"))).Envelope.Text);

        ctx.Nodes["a"] = new LogicNodeResultEnvelope(true, "x", "last-node", new Dictionary<string, string>(), Array.Empty<string>(), null, null, Array.Empty<string>());
        Assert.Equal("last-node", LogicLeafNodeExecutor.ExecuteEnd(Node("end"), ctx, Config()).Envelope.Text);

        var emptyCtx = new LogicExecutionContext();
        Assert.Equal("흐름 실행이 끝났습니다.", LogicLeafNodeExecutor.ExecuteEnd(Node("end"), emptyCtx, Config()).Envelope.Text);
    }

    [Fact]
    public void ExecuteOutputFallsBackToEmpty()
    {
        var ctx = new LogicExecutionContext();
        Assert.Equal(string.Empty, LogicLeafNodeExecutor.ExecuteOutput(Node("output"), ctx, Config()).Envelope.Text);
        Assert.Equal("done", LogicLeafNodeExecutor.ExecuteOutput(Node("output"), ctx, Config(("result", "done"))).Envelope.Text);
    }

    [Fact]
    public void ExecuteTemplateRendersTemplateOrText()
    {
        Assert.Equal("tmpl", LogicLeafNodeExecutor.ExecuteTemplate(Node("template"), Config(("template", "tmpl"))).Envelope.Data["rendered"]);
        Assert.Equal("txt", LogicLeafNodeExecutor.ExecuteTemplate(Node("template"), Config(("text", "txt"))).Envelope.Text);
    }

    [Fact]
    public void ExecuteSetVarStoresValueAndRejectsMissingName()
    {
        var ctx = new LogicExecutionContext();
        var ok = LogicLeafNodeExecutor.ExecuteSetVar(Node("set_var"), Config(("name", "x"), ("value", "42")), ctx);
        Assert.True(ok.Envelope.Ok);
        Assert.Equal("42", ctx.Vars["x"]);
        Assert.Equal("x", ok.Envelope.Data["name"]);

        var fail = LogicLeafNodeExecutor.ExecuteSetVar(Node("set_var"), Config(("value", "v")), ctx);
        Assert.False(fail.Envelope.Ok);
        Assert.Equal("name_required", fail.Envelope.Data["error"]);
    }

    [Fact]
    public void ExecuteIfEvaluatesConditionAndReturnsBranch()
    {
        var ctx = new LogicExecutionContext();
        ctx.Vars["score"] = "10";
        var node = new LogicNodeDefinition
        {
            NodeId = "n",
            Type = "if",
            Config = Config(("leftRef", "vars.score"), ("operator", "gt"), ("rightValue", "5"))
        };
        var trueOutcome = LogicLeafNodeExecutor.ExecuteIf(node, node.Config, ctx);
        Assert.Equal("true", trueOutcome.Branch);
        Assert.Equal("true", trueOutcome.Envelope.Data["branch"]);
        Assert.Equal("10", trueOutcome.Envelope.Data["left"]);

        ctx.Vars["score"] = "1";
        var falseOutcome = LogicLeafNodeExecutor.ExecuteIf(node, node.Config, ctx);
        Assert.Equal("false", falseOutcome.Branch);
    }

    [Fact]
    public void ExecutePassUsesMainInputThenFallback()
    {
        Assert.Equal("payload", LogicLeafNodeExecutor.ExecutePass(Node("parallel_split"), Config((LogicLeafNodeExecutor.MainInputKey, "payload")), "분기").Envelope.Text);
        Assert.Equal("분기", LogicLeafNodeExecutor.ExecutePass(Node("parallel_split"), Config(), "분기").Envelope.Text);
    }

    [Fact]
    public void ResolveConditionFallsBackToNodeConfigAndDefaultsOperator()
    {
        var node = new LogicNodeDefinition
        {
            NodeId = "n",
            Type = "if",
            Config = Config(("leftRef", "vars.a"), ("rightValue", "b"))
        };
        var condition = LogicLeafNodeExecutor.ResolveCondition(node, node.Config);
        Assert.Equal("vars.a", condition.LeftRef);
        Assert.Equal("equals", condition.Operator);
        Assert.Equal("b", condition.RightValue);
    }
}
