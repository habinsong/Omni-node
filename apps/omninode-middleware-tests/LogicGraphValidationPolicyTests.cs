using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class LogicGraphValidationPolicyTests
{
    [Fact]
    public void NormalizePortDefaultsToMainAndLowercasesInput()
    {
        Assert.Equal("main", LogicGraphValidationPolicy.NormalizePort(null));
        Assert.Equal("main", LogicGraphValidationPolicy.NormalizePort(""));
        Assert.Equal("main", LogicGraphValidationPolicy.NormalizePort("   "));
        Assert.Equal("true", LogicGraphValidationPolicy.NormalizePort("TRUE"));
        Assert.Equal("result", LogicGraphValidationPolicy.NormalizePort(" Result "));
    }

    [Theory]
    [InlineData("=", "equals")]
    [InlineData("==", "equals")]
    [InlineData("!=", "not_equals")]
    [InlineData("<>", "not_equals")]
    [InlineData("notequals", "not_equals")]
    [InlineData(">", "gt")]
    [InlineData(">=", "gte")]
    [InlineData("<", "lt")]
    [InlineData("<=", "lte")]
    [InlineData("truthy", "is_truthy")]
    [InlineData("falsy", "is_falsy")]
    [InlineData("startswith", "starts_with")]
    [InlineData("endswith", "ends_with")]
    [InlineData("notcontains", "not_contains")]
    [InlineData("equals", "equals")]
    [InlineData("not_contains", "not_contains")]
    [InlineData("garbage", "equals")]
    public void NormalizeOperatorMapsAliasesAndFallsBackToEquals(string input, string expected)
    {
        Assert.Equal(expected, LogicGraphValidationPolicy.NormalizeOperator(input));
    }

    [Fact]
    public void IsSourcePortValidEnforcesIfAndParallelSplitRules()
    {
        Assert.True(LogicGraphValidationPolicy.IsSourcePortValid("if", "true"));
        Assert.True(LogicGraphValidationPolicy.IsSourcePortValid("if", "false"));
        Assert.False(LogicGraphValidationPolicy.IsSourcePortValid("if", "main"));

        Assert.True(LogicGraphValidationPolicy.IsSourcePortValid("parallel_split", "branchA"));
        Assert.True(LogicGraphValidationPolicy.IsSourcePortValid("chat_single", "main"));
        Assert.False(LogicGraphValidationPolicy.IsSourcePortValid("chat_single", "other"));
    }

    [Fact]
    public void IsTargetPortValidAllowsMainOrBindableInputs()
    {
        Assert.True(LogicGraphValidationPolicy.IsTargetPortValid("chat_single", "input"));
        Assert.True(LogicGraphValidationPolicy.IsTargetPortValid("chat_single", "main"));
        Assert.True(LogicGraphValidationPolicy.IsTargetPortValid("end", "result"));
        Assert.True(LogicGraphValidationPolicy.IsTargetPortValid("if", "leftref"));
        Assert.True(LogicGraphValidationPolicy.IsTargetPortValid("parallel_join", "branchA"));
        Assert.False(LogicGraphValidationPolicy.IsTargetPortValid("start", "main"));
        Assert.False(LogicGraphValidationPolicy.IsTargetPortValid("chat_single", "wrong"));
    }

    [Fact]
    public void HasCycleDetectsCircularEdges()
    {
        var nodes = new[]
        {
            new LogicNodeDefinition { NodeId = "a", Type = "start" },
            new LogicNodeDefinition { NodeId = "b", Type = "chat_single" },
            new LogicNodeDefinition { NodeId = "c", Type = "end" }
        };
        var acyclic = new[]
        {
            new LogicEdgeDefinition { EdgeId = "e1", SourceNodeId = "a", TargetNodeId = "b" },
            new LogicEdgeDefinition { EdgeId = "e2", SourceNodeId = "b", TargetNodeId = "c" }
        };
        var cyclic = acyclic.Concat(new[]
        {
            new LogicEdgeDefinition { EdgeId = "e3", SourceNodeId = "c", TargetNodeId = "b" }
        }).ToArray();

        Assert.False(LogicGraphValidationPolicy.HasCycle(nodes, acyclic));
        Assert.True(LogicGraphValidationPolicy.HasCycle(nodes, cyclic));
    }

    [Fact]
    public void ValidateRejectsBadSchemaVersion()
    {
        var graph = NewGraph();
        graph.Version = "logic.graph.v0";

        var result = LogicGraphValidationPolicy.Validate(graph);

        Assert.False(result.Ok);
        Assert.Contains("지원하지 않는 그래프 포맷", result.Message);
    }

    [Fact]
    public void ValidateRejectsEmptyOrInactiveNodes()
    {
        var emptyGraph = NewGraph();
        var emptyResult = LogicGraphValidationPolicy.Validate(emptyGraph);
        Assert.False(emptyResult.Ok);
        Assert.Equal("노드가 비어 있습니다.", emptyResult.Message);

        var inactiveGraph = NewGraph();
        inactiveGraph.Nodes.Add(new LogicNodeDefinition { NodeId = "a", Type = "start", Enabled = false });
        var inactiveResult = LogicGraphValidationPolicy.Validate(inactiveGraph);
        Assert.False(inactiveResult.Ok);
        Assert.Equal("활성 노드가 하나 이상 필요합니다.", inactiveResult.Message);
    }

    [Fact]
    public void ValidateRejectsDuplicateNodeIdAndUnknownType()
    {
        var dupGraph = NewGraph();
        dupGraph.Nodes.Add(new LogicNodeDefinition { NodeId = "a", Type = "start" });
        dupGraph.Nodes.Add(new LogicNodeDefinition { NodeId = "a", Type = "end" });
        var dupResult = LogicGraphValidationPolicy.Validate(dupGraph);
        Assert.False(dupResult.Ok);
        Assert.Contains("중복 nodeId", dupResult.Message);

        var unknownGraph = NewGraph();
        unknownGraph.Nodes.Add(new LogicNodeDefinition { NodeId = "a", Type = "unsupported_kind" });
        unknownGraph.Nodes.Add(new LogicNodeDefinition { NodeId = "b", Type = "end" });
        var unknownResult = LogicGraphValidationPolicy.Validate(unknownGraph);
        Assert.False(unknownResult.Ok);
        Assert.Contains("지원하지 않는 노드 타입", unknownResult.Message);
    }

    [Fact]
    public void ValidateRequiresExactlyOneStartAndAtLeastOneTerminator()
    {
        var noStart = NewGraph();
        noStart.Nodes.Add(new LogicNodeDefinition { NodeId = "a", Type = "chat_single" });
        noStart.Nodes.Add(new LogicNodeDefinition { NodeId = "b", Type = "end" });
        var noStartResult = LogicGraphValidationPolicy.Validate(noStart);
        Assert.False(noStartResult.Ok);
        Assert.Equal("start 노드는 정확히 1개여야 합니다.", noStartResult.Message);

        var noEnd = NewGraph();
        noEnd.Nodes.Add(new LogicNodeDefinition { NodeId = "a", Type = "start" });
        noEnd.Nodes.Add(new LogicNodeDefinition { NodeId = "b", Type = "chat_single" });
        var noEndResult = LogicGraphValidationPolicy.Validate(noEnd);
        Assert.False(noEndResult.Ok);
        Assert.Equal("end 또는 output 노드가 하나 이상 필요합니다.", noEndResult.Message);
    }

    [Fact]
    public void ValidateRejectsDuplicateInputPortOnNonJoinTarget()
    {
        var graph = NewGraph();
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "s", Type = "start" });
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "c", Type = "chat_single" });
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "e", Type = "end" });
        graph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e1", SourceNodeId = "s", TargetNodeId = "c", TargetPort = "input" });
        graph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e2", SourceNodeId = "s", TargetNodeId = "c", TargetPort = "input" });
        graph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e3", SourceNodeId = "c", TargetNodeId = "e", TargetPort = "result" });

        var result = LogicGraphValidationPolicy.Validate(graph);

        Assert.False(result.Ok);
        Assert.Contains("같은 입력 칸에는 연결을 하나만", result.Message);
    }

    [Fact]
    public void ValidateRejectsEndWithOutgoingAndStartWithIncoming()
    {
        var endGraph = NewGraph();
        endGraph.Nodes.Add(new LogicNodeDefinition { NodeId = "s", Type = "start" });
        endGraph.Nodes.Add(new LogicNodeDefinition { NodeId = "e", Type = "end" });
        endGraph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e1", SourceNodeId = "s", TargetNodeId = "e", TargetPort = "result" });
        endGraph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e2", SourceNodeId = "e", TargetNodeId = "s" });
        var endResult = LogicGraphValidationPolicy.Validate(endGraph);
        Assert.False(endResult.Ok);
        Assert.Contains("end 노드는 outgoing edge", endResult.Message);
    }

    [Fact]
    public void ValidateRejectsParallelJoinWithoutEnoughIncoming()
    {
        var graph = NewGraph();
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "s", Type = "start" });
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "j", Type = "parallel_join" });
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "e", Type = "end" });
        graph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e1", SourceNodeId = "s", TargetNodeId = "j", TargetPort = "branchA" });
        graph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e2", SourceNodeId = "j", TargetNodeId = "e", TargetPort = "result" });

        var result = LogicGraphValidationPolicy.Validate(graph);

        Assert.False(result.Ok);
        Assert.Contains("parallel_join 노드는 선행 노드가 2개 이상", result.Message);
    }

    [Fact]
    public void ValidateRejectsEdgeConditionWithBadOperatorOrMissingLeftRef()
    {
        var blankLeft = NewGraph();
        blankLeft.Nodes.Add(new LogicNodeDefinition { NodeId = "s", Type = "start" });
        blankLeft.Nodes.Add(new LogicNodeDefinition { NodeId = "e", Type = "end" });
        blankLeft.Edges.Add(new LogicEdgeDefinition
        {
            EdgeId = "e1",
            SourceNodeId = "s",
            TargetNodeId = "e",
            TargetPort = "result",
            Condition = new LogicEdgeCondition { LeftRef = "", Operator = "equals", RightValue = "x" }
        });
        var blankResult = LogicGraphValidationPolicy.Validate(blankLeft);
        Assert.False(blankResult.Ok);
        Assert.Contains("edge condition leftRef가 필요", blankResult.Message);
    }

    [Fact]
    public void ValidateAcceptsLinearStartChatEndGraph()
    {
        var graph = NewGraph();
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "s", Type = "start" });
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "c", Type = "chat_single" });
        graph.Nodes.Add(new LogicNodeDefinition { NodeId = "e", Type = "end" });
        graph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e1", SourceNodeId = "s", TargetNodeId = "c", TargetPort = "input" });
        graph.Edges.Add(new LogicEdgeDefinition { EdgeId = "e2", SourceNodeId = "c", TargetNodeId = "e", TargetPort = "result" });

        var result = LogicGraphValidationPolicy.Validate(graph);

        Assert.True(result.Ok);
        Assert.Equal(string.Empty, result.Message);
    }

    private static LogicGraphDefinition NewGraph()
    {
        return new LogicGraphDefinition
        {
            GraphId = "g1",
            Title = "test",
            Version = LogicGraphValidationPolicy.SchemaVersion
        };
    }
}
