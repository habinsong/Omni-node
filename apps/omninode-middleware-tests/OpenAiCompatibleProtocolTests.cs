using System.Net;
using System.Text.Json;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class OpenAiCompatibleProtocolTests
{
    [Fact]
    public void BuildChatBodyEmitsSystemPlusUserMessagesWhenNoMultiTurn()
    {
        var body = OpenAiCompatibleProtocol.BuildChatBody(
            model: "llama-3-70b",
            systemPrompt: "you are helpful",
            userInput: "hi",
            multiTurn: null,
            maxTokensProperty: "max_tokens",
            maxOutputTokens: 1024,
            stream: false
        );

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("llama-3-70b", root.GetProperty("model").GetString());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("you are helpful", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public void BuildChatBodyExpandsMultiTurnSequenceWhenLeadingMessageIsSystemContext()
    {
        var multiTurn = new List<(string Role, string Content)>
        {
            ("system_context", "house rules"),
            ("user", "first"),
            ("assistant", "ack"),
            ("user", "second")
        };

        var body = OpenAiCompatibleProtocol.BuildChatBody(
            model: "groq/compound",
            systemPrompt: "you are helpful",
            userInput: "ignored when multiTurn supplied",
            multiTurn: multiTurn,
            maxTokensProperty: "max_completion_tokens",
            maxOutputTokens: 512,
            stream: true
        );

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(512, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.True(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages");
        Assert.Equal(5, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("you are helpful", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("house rules", messages[1].GetProperty("content").GetString());
        Assert.Equal("user", messages[2].GetProperty("role").GetString());
        Assert.Equal("first", messages[2].GetProperty("content").GetString());
        Assert.Equal("assistant", messages[3].GetProperty("role").GetString());
        Assert.Equal("ack", messages[3].GetProperty("content").GetString());
        Assert.Equal("user", messages[4].GetProperty("role").GetString());
        Assert.Equal("second", messages[4].GetProperty("content").GetString());
    }

    [Fact]
    public void BuildFailureMessageSurfacesNvidiaQuotaGuidance()
    {
        var text = OpenAiCompatibleProtocol.BuildFailureMessage("nvidia", HttpStatusCode.TooManyRequests, "{\"error\":\"quota\"}");

        Assert.Contains("NVIDIA NIM", text);
        Assert.Contains("Cerebras", text);
    }

    [Fact]
    public void BuildFailureMessageHandlesGenericRateLimit()
    {
        var text = OpenAiCompatibleProtocol.BuildFailureMessage("groq", HttpStatusCode.TooManyRequests, string.Empty);

        Assert.Contains("Groq", text);
        Assert.Contains("rate limit", text);
    }

    [Fact]
    public void BuildFailureMessageReturnsGenericFailureForOtherStatuses()
    {
        var text = OpenAiCompatibleProtocol.BuildFailureMessage("cerebras", HttpStatusCode.InternalServerError, string.Empty);

        Assert.Contains("Cerebras", text);
        Assert.Contains("500", text);
    }

    [Theory]
    [InlineData("groq", "Groq")]
    [InlineData("gemini", "Gemini")]
    [InlineData("cerebras", "Cerebras")]
    [InlineData("nvidia", "NVIDIA NIM")]
    [InlineData("unknown-provider", "unknown-provider")]
    public void DisplayNameMapsKnownProviders(string provider, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleProtocol.DisplayName(provider));
    }

    [Fact]
    public void ExtractStreamChunkReadsTextDelta()
    {
        var json = "{\"choices\":[{\"finish_reason\":null,\"delta\":{\"content\":\"hello\"}}]}";

        var chunk = OpenAiCompatibleProtocol.ExtractStreamChunk(json);

        Assert.Equal("hello", chunk.Content);
        Assert.Equal(string.Empty, chunk.FinishReason);
    }

    [Fact]
    public void ExtractStreamChunkConcatenatesArrayContentParts()
    {
        var json = "{\"choices\":[{\"finish_reason\":\"stop\",\"delta\":{\"content\":[{\"text\":\"foo\"},\"bar\"]}}]}";

        var chunk = OpenAiCompatibleProtocol.ExtractStreamChunk(json);

        Assert.Equal("foobar", chunk.Content);
        Assert.Equal("stop", chunk.FinishReason);
    }

    [Fact]
    public void ExtractStreamChunkReturnsEmptyForBrokenJson()
    {
        var chunk = OpenAiCompatibleProtocol.ExtractStreamChunk("{broken");

        Assert.Equal(string.Empty, chunk.Content);
        Assert.Equal(string.Empty, chunk.FinishReason);
    }
}
