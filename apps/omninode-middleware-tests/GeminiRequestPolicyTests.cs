using System.Text.Json;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GeminiRequestPolicyTests
{
    [Fact]
    public void BuildGroundedBodyEmbedsGoogleSearchTool()
    {
        var body = GeminiRequestPolicy.BuildGroundedBody("hello\n\"world\"", 256);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("tools", out var tools));
        Assert.Equal(1, tools.GetArrayLength());
        Assert.True(tools[0].TryGetProperty("google_search", out _));

        var maxTokens = root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32();
        Assert.Equal(256, maxTokens);

        var text = root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal("hello\n\"world\"", text);
    }

    [Fact]
    public void BuildUrlContextBodyAddsBothToolsWhenSearchEnabled()
    {
        var body = GeminiRequestPolicy.BuildUrlContextBody("prompt", 512, includeGoogleSearch: true);

        using var doc = JsonDocument.Parse(body);
        var tools = doc.RootElement.GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());
        Assert.True(tools[0].TryGetProperty("url_context", out _));
        Assert.True(tools[1].TryGetProperty("google_search", out _));
    }

    [Fact]
    public void BuildUrlContextBodyOmitsGoogleSearchWhenDisabled()
    {
        var body = GeminiRequestPolicy.BuildUrlContextBody("prompt", 512, includeGoogleSearch: false);

        using var doc = JsonDocument.Parse(body);
        var tools = doc.RootElement.GetProperty("tools");
        Assert.Equal(1, tools.GetArrayLength());
        Assert.True(tools[0].TryGetProperty("url_context", out _));
    }

    [Fact]
    public void NormalizeStreamDeltaReturnsSuffixWhenChunkPrependsCurrent()
    {
        var delta = GeminiRequestPolicy.NormalizeStreamDelta("hello world", "hello");

        Assert.Equal(" world", delta);
    }

    [Fact]
    public void NormalizeStreamDeltaReturnsEmptyWhenChunkAlreadyMerged()
    {
        var delta = GeminiRequestPolicy.NormalizeStreamDelta("world", "hello world");

        Assert.Equal(string.Empty, delta);
    }

    [Fact]
    public void NormalizeStreamDeltaReturnsChunkWhenNoMergedPrefix()
    {
        var delta = GeminiRequestPolicy.NormalizeStreamDelta("brand new", "different");

        Assert.Equal("brand new", delta);
    }
}
