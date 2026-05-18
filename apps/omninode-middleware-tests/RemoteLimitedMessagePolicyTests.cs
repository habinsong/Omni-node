using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class RemoteLimitedMessagePolicyTests
{
    [Theory]
    [InlineData("list_conversations")]
    [InlineData("get_conversation")]
    [InlineData("list_memory_notes")]
    [InlineData("read_memory_note")]
    [InlineData("memory_search")]
    [InlineData("conversation_search")]
    [InlineData("memory_get")]
    [InlineData("context_scan")]
    [InlineData("skills_list")]
    [InlineData("commands_list")]
    [InlineData("notebook_get")]
    public void AllowsReadOrientedMessages(string messageType)
    {
        Assert.True(RemoteLimitedMessagePolicy.IsAllowed(messageType));
    }

    [Theory]
    [InlineData("llm_chat_single")]
    [InlineData("coding_run_single")]
    [InlineData("run_routine")]
    [InlineData("logic_graph_run")]
    [InlineData("task_graph_run")]
    [InlineData("refactor_apply")]
    [InlineData("tool_execute")]
    [InlineData("request_otp")]
    [InlineData("resume_auth")]
    [InlineData("")]
    [InlineData(null)]
    public void BlocksExecutionAndAuthMessages(string? messageType)
    {
        Assert.False(RemoteLimitedMessagePolicy.IsAllowed(messageType));
    }
}
