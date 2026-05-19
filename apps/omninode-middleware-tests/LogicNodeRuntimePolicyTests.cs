using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class LogicNodeRuntimePolicyTests
{
    [Theory]
    [InlineData("ok", true)]
    [InlineData("OK", true)]
    [InlineData("success", true)]
    [InlineData("SUCCESS", true)]
    [InlineData("  ok  ", true)]
    [InlineData("failed", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSuccessfulExecutionStatusRecognizesCommonOkTokens(string? status, bool expected)
    {
        Assert.Equal(expected, LogicNodeRuntimePolicy.IsSuccessfulExecutionStatus(status));
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("error", true)]
    [InlineData("failed", true)]
    [InlineData("canceled", true)]
    [InlineData("cancelled", true)]
    [InlineData("timeout", true)]
    [InlineData("killed", true)]
    [InlineData("running", false)]
    [InlineData("pending", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTerminalStatusRecognizesFinalStates(string? status, bool expected)
    {
        Assert.Equal(expected, LogicNodeRuntimePolicy.IsTerminalStatus(status));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(null, true)]
    [InlineData("Error: something exploded", true)]
    [InlineData("error: blah", true)]
    [InlineData("Gemini API 키가 설정되지 않았습니다.", true)]
    [InlineData("인증이 필요합니다", true)]
    [InlineData("호출 오류 발생", true)]
    [InlineData("요청 실패", true)]
    [InlineData("응답 시간이 초과되었습니다", true)]
    [InlineData("Hello world", false)]
    [InlineData("정상 응답", false)]
    public void LooksLikeAiFailureFlagsBlankAndKnownErrorSignals(string? text, bool expected)
    {
        Assert.Equal(expected, LogicNodeRuntimePolicy.LooksLikeAiFailure(text));
    }

    [Theory]
    [InlineData("{{run.input}}", true)]
    [InlineData("{{  run.input  }}", true)]
    [InlineData("{{RUN.INPUT}}", true)]
    [InlineData("prefix {{run.input}}", false)]
    [InlineData("{{run.input}} suffix", false)]
    [InlineData("{{vars.x}}", false)]
    [InlineData("plain text", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRunInputTemplateOnlyMatchesPureRunInputPlaceholder(string? value, bool expected)
    {
        Assert.Equal(expected, LogicNodeRuntimePolicy.IsRunInputTemplate(value));
    }

    [Fact]
    public void ShouldApplyImplicitMainInputDefaultsToTrueWhenConfigMissing()
    {
        var node = new LogicNodeDefinition { NodeId = "n1", Type = "chat_single" };

        Assert.True(LogicNodeRuntimePolicy.ShouldApplyImplicitMainInput(node, "input"));
    }

    [Fact]
    public void ShouldApplyImplicitMainInputRespectsReferenceAndEdgeMode()
    {
        var refMode = new LogicNodeDefinition
        {
            NodeId = "n1",
            Type = "chat_single",
            Config = new Dictionary<string, string>(StringComparer.Ordinal) { ["__mode__input"] = "reference" }
        };
        var edgeMode = new LogicNodeDefinition
        {
            NodeId = "n1",
            Type = "chat_single",
            Config = new Dictionary<string, string>(StringComparer.Ordinal) { ["__mode__input"] = "edge" }
        };

        Assert.False(LogicNodeRuntimePolicy.ShouldApplyImplicitMainInput(refMode, "input"));
        Assert.False(LogicNodeRuntimePolicy.ShouldApplyImplicitMainInput(edgeMode, "input"));
    }

    [Fact]
    public void ShouldApplyImplicitMainInputUsesValueAsTemplateProbe()
    {
        var withRunInput = new LogicNodeDefinition
        {
            NodeId = "n1",
            Type = "chat_single",
            Config = new Dictionary<string, string>(StringComparer.Ordinal) { ["input"] = "{{run.input}}" }
        };
        var withCustomValue = new LogicNodeDefinition
        {
            NodeId = "n1",
            Type = "chat_single",
            Config = new Dictionary<string, string>(StringComparer.Ordinal) { ["input"] = "literal" }
        };
        var blankValue = new LogicNodeDefinition
        {
            NodeId = "n1",
            Type = "chat_single",
            Config = new Dictionary<string, string>(StringComparer.Ordinal) { ["input"] = "   " }
        };

        Assert.True(LogicNodeRuntimePolicy.ShouldApplyImplicitMainInput(withRunInput, "input"));
        Assert.False(LogicNodeRuntimePolicy.ShouldApplyImplicitMainInput(withCustomValue, "input"));
        Assert.True(LogicNodeRuntimePolicy.ShouldApplyImplicitMainInput(blankValue, "input"));
    }

    [Fact]
    public void RequiresAllIncomingEdgesReturnsTrueWhenAnyTargetPortIsNonMain()
    {
        var allMain = new[]
        {
            new LogicEdgeDefinition { EdgeId = "e1", TargetPort = "main" },
            new LogicEdgeDefinition { EdgeId = "e2", TargetPort = "main" }
        };
        var mixed = new[]
        {
            new LogicEdgeDefinition { EdgeId = "e1", TargetPort = "main" },
            new LogicEdgeDefinition { EdgeId = "e2", TargetPort = "branchA" }
        };

        Assert.False(LogicNodeRuntimePolicy.RequiresAllIncomingEdges(allMain));
        Assert.True(LogicNodeRuntimePolicy.RequiresAllIncomingEdges(mixed));
    }

    [Fact]
    public void IsNodeReadyToRunHandlesStartParallelJoinAndPartialArrival()
    {
        var startNode = new LogicNodeDefinition { NodeId = "s", Type = "start" };
        var emptyArrivals = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var emptyEdges = new Dictionary<string, LogicEdgeDefinition[]>(StringComparer.Ordinal);
        Assert.True(LogicNodeRuntimePolicy.IsNodeReadyToRun(startNode, emptyArrivals, emptyEdges));

        var midNode = new LogicNodeDefinition { NodeId = "m", Type = "chat_single" };
        Assert.False(LogicNodeRuntimePolicy.IsNodeReadyToRun(midNode, emptyArrivals, emptyEdges));

        var joinNode = new LogicNodeDefinition { NodeId = "j", Type = "parallel_join" };
        var twoIncomingEdges = new LogicEdgeDefinition[]
        {
            new() { EdgeId = "e1", TargetNodeId = "j", TargetPort = "branchA" },
            new() { EdgeId = "e2", TargetNodeId = "j", TargetPort = "branchB" }
        };
        var arrivalsWithOne = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["j"] = new HashSet<string>(StringComparer.Ordinal) { "a" }
        };
        var arrivalsWithTwo = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["j"] = new HashSet<string>(StringComparer.Ordinal) { "a", "b" }
        };
        var incomingForJoin = new Dictionary<string, LogicEdgeDefinition[]>(StringComparer.Ordinal)
        {
            ["j"] = twoIncomingEdges
        };

        Assert.False(LogicNodeRuntimePolicy.IsNodeReadyToRun(joinNode, arrivalsWithOne, incomingForJoin));
        Assert.True(LogicNodeRuntimePolicy.IsNodeReadyToRun(joinNode, arrivalsWithTwo, incomingForJoin));
    }

    [Fact]
    public void BuildConversationTitleConcatenatesGraphNodeAndMode()
    {
        var graph = new LogicGraphDefinition { Title = "MyGraph" };
        var node = new LogicNodeDefinition { NodeId = "n", Title = "ChatStep" };

        Assert.Equal("MyGraph · ChatStep · single", LogicNodeRuntimePolicy.BuildConversationTitle(graph, node, "single"));
    }

    [Fact]
    public void TemplateRegexMatchesBalancedDoubleBraces()
    {
        var match = LogicNodeRuntimePolicy.TemplateRegex.Match("hello {{vars.name}} world");

        Assert.True(match.Success);
        Assert.Equal("vars.name", match.Groups["expr"].Value);
    }
}
