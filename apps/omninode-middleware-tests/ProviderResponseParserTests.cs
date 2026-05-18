using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ProviderResponseParserTests
{
    [Fact]
    public void ExtractOpenAiCompatibleChunkReadsContentAndFinishReason()
    {
        var json = """
            {
              "choices": [
                {
                  "finish_reason": "stop",
                  "message": { "role": "assistant", "content": "hello" }
                }
              ]
            }
        """;

        var chunk = ProviderResponseParser.ExtractOpenAiCompatibleChunk(json);

        Assert.Equal("hello", chunk.Content);
        Assert.Equal("stop", chunk.FinishReason);
    }

    [Fact]
    public void ExtractOpenAiCompatibleChunkReturnsEmptyWhenChoicesMissing()
    {
        var chunk = ProviderResponseParser.ExtractOpenAiCompatibleChunk("{}");

        Assert.Equal(string.Empty, chunk.Content);
        Assert.Equal(string.Empty, chunk.FinishReason);
    }

    [Fact]
    public void ExtractGeminiChunkConcatenatesParts()
    {
        var json = """
            {
              "candidates": [
                {
                  "finishReason": "STOP",
                  "content": {
                    "parts": [
                      { "text": "hello" },
                      { "text": "world" }
                    ]
                  }
                }
              ]
            }
        """;

        var chunk = ProviderResponseParser.ExtractGeminiChunk(json);

        Assert.Contains("hello", chunk.Content);
        Assert.Contains("world", chunk.Content);
        Assert.Equal("STOP", chunk.FinishReason);
    }

    [Theory]
    [InlineData("length", true)]
    [InlineData("max_tokens", true)]
    [InlineData("token_limit", true)]
    [InlineData("stop", false)]
    [InlineData(null, false)]
    public void IsOpenAiCompatibleTruncatedRecognizesAllTruncationVariants(string? finishReason, bool expected)
    {
        Assert.Equal(expected, ProviderResponseParser.IsOpenAiCompatibleTruncated(finishReason));
    }

    [Theory]
    [InlineData("MAX_TOKENS", true)]
    [InlineData("LENGTH", true)]
    [InlineData("STOP", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGeminiTruncatedRecognizesGeminiStyleReasons(string? finishReason, bool expected)
    {
        Assert.Equal(expected, ProviderResponseParser.IsGeminiTruncated(finishReason));
    }

    [Fact]
    public void ExtractGeminiBlockReasonReturnsValueWhenPresent()
    {
        var json = """{"promptFeedback":{"blockReason":"SAFETY"}}""";

        var blockReason = ProviderResponseParser.ExtractGeminiBlockReason(json);

        Assert.Equal("SAFETY", blockReason);
    }

    [Fact]
    public void ExtractGeminiBlockReasonReturnsEmptyWhenAbsent()
    {
        Assert.Equal(string.Empty, ProviderResponseParser.ExtractGeminiBlockReason("{}"));
    }

    [Fact]
    public void TryParseOpenAiCompatibleUsageReadsTokenCounts()
    {
        var json = """{"usage":{"prompt_tokens":12,"completion_tokens":7,"total_tokens":19}}""";

        Assert.True(ProviderResponseParser.TryParseOpenAiCompatibleUsage(json, out var usage));
        Assert.Equal(12, usage.PromptTokens);
        Assert.Equal(7, usage.CompletionTokens);
        Assert.Equal(19, usage.TotalTokens);
    }

    [Fact]
    public void TryParseOpenAiCompatibleUsageReturnsFalseWhenMissing()
    {
        Assert.False(ProviderResponseParser.TryParseOpenAiCompatibleUsage("{}", out _));
    }

    [Fact]
    public void TryParseGeminiUsageReadsTokenCounts()
    {
        var json = """{"usageMetadata":{"promptTokenCount":50,"candidatesTokenCount":120,"totalTokenCount":170}}""";

        Assert.True(ProviderResponseParser.TryParseGeminiUsage(json, out var usage));
        Assert.Equal(50, usage.PromptTokens);
        Assert.Equal(120, usage.CompletionTokens);
        Assert.Equal(170, usage.TotalTokens);
    }

    [Fact]
    public void TryParseGeminiUsageReturnsFalseWhenAbsent()
    {
        Assert.False(ProviderResponseParser.TryParseGeminiUsage("{}", out _));
    }

    [Fact]
    public void TryParseOpenAiCompatibleUsageGuardsCorruptBody()
    {
        Assert.False(ProviderResponseParser.TryParseOpenAiCompatibleUsage("{broken", out _));
    }
}
