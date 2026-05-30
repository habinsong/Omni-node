using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class LogicTemplateResolverTests
{
    private static LogicExecutionContext NewContext()
    {
        var ctx = new LogicExecutionContext
        {
            RunInput = "run-input-text",
            RunDirectory = "/tmp/run"
        };
        ctx.Vars["greeting"] = "hello";
        ctx.Sessions["main"] = "session-123";
        ctx.Artifacts.Add("artifact-0");
        ctx.Artifacts.Add("artifact-1");
        ctx.Nodes["n1"] = new LogicNodeResultEnvelope(
            Ok: true,
            Type: "chat_single",
            Text: "node-text",
            Data: new Dictionary<string, string> { ["k"] = "v" },
            Artifacts: new[] { "node-artifact" },
            ConversationId: "conv-9",
            SessionKey: "skey-9",
            Links: Array.Empty<string>()
        );
        return ctx;
    }

    [Fact]
    public void ResolveReferenceReadsRunInputVarsSessions()
    {
        var ctx = NewContext();
        Assert.Equal("run-input-text", LogicTemplateResolver.ResolveReference("run.input", ctx));
        Assert.Equal("hello", LogicTemplateResolver.ResolveReference("vars.greeting", ctx));
        Assert.Equal("session-123", LogicTemplateResolver.ResolveReference("sessions.main", ctx));
        Assert.Equal(string.Empty, LogicTemplateResolver.ResolveReference("vars.missing", ctx));
    }

    [Fact]
    public void ResolveReferenceStripsSurroundingBraces()
    {
        var ctx = NewContext();
        Assert.Equal("hello", LogicTemplateResolver.ResolveReference("{{ vars.greeting }}", ctx));
    }

    [Fact]
    public void ResolveReferenceArtifactsByIndexAndLast()
    {
        var ctx = NewContext();
        Assert.Equal("artifact-1", LogicTemplateResolver.ResolveReference("artifacts.last", ctx));
        Assert.Equal("artifact-0", LogicTemplateResolver.ResolveReference("artifacts.0", ctx));
        Assert.Equal("artifact-1", LogicTemplateResolver.ResolveReference("artifacts.1", ctx));
        Assert.Equal(string.Empty, LogicTemplateResolver.ResolveReference("artifacts.99", ctx));
    }

    [Fact]
    public void ResolveReferenceNodeFields()
    {
        var ctx = NewContext();
        Assert.Equal("node-text", LogicTemplateResolver.ResolveReference("nodes.n1.text", ctx));
        Assert.Equal("chat_single", LogicTemplateResolver.ResolveReference("nodes.n1.type", ctx));
        Assert.Equal("true", LogicTemplateResolver.ResolveReference("nodes.n1.ok", ctx));
        Assert.Equal("conv-9", LogicTemplateResolver.ResolveReference("nodes.n1.conversationId", ctx));
        Assert.Equal("skey-9", LogicTemplateResolver.ResolveReference("nodes.n1.sessionKey", ctx));
        Assert.Equal("v", LogicTemplateResolver.ResolveReference("nodes.n1.data.k", ctx));
        Assert.Equal(string.Empty, LogicTemplateResolver.ResolveReference("nodes.missing.text", ctx));
        Assert.Equal(string.Empty, LogicTemplateResolver.ResolveReference("nodes.n1.unknown", ctx));
    }

    [Fact]
    public void ResolveReferenceReturnsLiteralForUnknownPrefix()
    {
        var ctx = NewContext();
        Assert.Equal("plain literal", LogicTemplateResolver.ResolveReference("plain literal", ctx));
        Assert.Equal(string.Empty, LogicTemplateResolver.ResolveReference("   ", ctx));
    }

    [Fact]
    public void ResolveTemplateSubstitutesPlaceholders()
    {
        var ctx = NewContext();
        Assert.Equal(
            "say hello to session-123",
            LogicTemplateResolver.ResolveTemplate("say {{vars.greeting}} to {{sessions.main}}", ctx)
        );
        Assert.Equal(string.Empty, LogicTemplateResolver.ResolveTemplate("   ", ctx));
    }

    [Fact]
    public void ResolveEdgeValueReadsMainPortAsText()
    {
        var ctx = NewContext();
        var edge = new LogicEdgeDefinition { SourceNodeId = "n1", SourcePort = "main" };
        Assert.Equal("node-text", LogicTemplateResolver.ResolveEdgeValue(edge, ctx));
    }

    [Fact]
    public void ResolveEdgeValueReadsSessionConversationArtifactData()
    {
        var ctx = NewContext();
        Assert.Equal("skey-9", LogicTemplateResolver.ResolveEdgeValue(
            new LogicEdgeDefinition { SourceNodeId = "n1", SourcePort = "session" }, ctx));
        Assert.Equal("conv-9", LogicTemplateResolver.ResolveEdgeValue(
            new LogicEdgeDefinition { SourceNodeId = "n1", SourcePort = "conversation" }, ctx));
        Assert.Equal("node-artifact", LogicTemplateResolver.ResolveEdgeValue(
            new LogicEdgeDefinition { SourceNodeId = "n1", SourcePort = "artifact" }, ctx));
        Assert.Equal("v", LogicTemplateResolver.ResolveEdgeValue(
            new LogicEdgeDefinition { SourceNodeId = "n1", SourcePort = "data.k" }, ctx));
    }

    [Fact]
    public void ResolveEdgeValueReturnsEmptyForMissingSource()
    {
        var ctx = NewContext();
        var edge = new LogicEdgeDefinition { SourceNodeId = "missing", SourcePort = "main" };
        Assert.Equal(string.Empty, LogicTemplateResolver.ResolveEdgeValue(edge, ctx));
    }
}
