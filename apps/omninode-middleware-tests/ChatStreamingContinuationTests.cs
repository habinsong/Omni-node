using System.Text;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ChatStreamingContinuationTests
{
    [Fact]
    public void BuildPartialTruncationSuffixIncludesProviderDisplayNameAndReason()
    {
        var suffix = ChatStreamingContinuation.BuildPartialTruncationSuffix("groq", "rate_limit");

        Assert.Contains("Groq", suffix);
        Assert.Contains("rate_limit", suffix);
        Assert.Contains("도중에 끊겼", suffix);
    }

    [Fact]
    public void BuildPartialTruncationSuffixTrimsLongReasonHints()
    {
        var longReason = new string('x', 200);

        var suffix = ChatStreamingContinuation.BuildPartialTruncationSuffix("nvidia", longReason);

        Assert.Contains("NVIDIA NIM", suffix);
        Assert.Contains("…", suffix);
    }

    [Fact]
    public void BuildPartialTruncationSuffixFallsBackToDefaultReasonWhenBlank()
    {
        var suffix = ChatStreamingContinuation.BuildPartialTruncationSuffix("gemini", "   ");

        Assert.Contains("Gemini", suffix);
        Assert.Contains("stream interrupted", suffix);
    }

    [Fact]
    public void BuildTimeoutMessageSurfacesNvidiaQuotaGuidance()
    {
        var message = ChatStreamingContinuation.BuildTimeoutMessage("nvidia");

        Assert.Contains("NVIDIA NIM", message);
        Assert.Contains("무료 할당량", message);
    }

    [Theory]
    [InlineData("groq", "Groq")]
    [InlineData("gemini", "Gemini")]
    [InlineData("cerebras", "Cerebras")]
    public void BuildTimeoutMessageUsesProviderDisplayNameForGenericProviders(string provider, string expected)
    {
        var message = ChatStreamingContinuation.BuildTimeoutMessage(provider);

        Assert.Contains(expected, message);
        Assert.Contains("응답 시간이 초과", message);
    }

    [Fact]
    public void SafeEmitDeltaInvokesCallbackForNonEmptyDelta()
    {
        var captured = new List<string>();

        ChatStreamingContinuation.SafeEmitDelta(captured.Add, "hello");

        Assert.Equal(new[] { "hello" }, captured);
    }

    [Fact]
    public void SafeEmitDeltaSkipsEmptyOrNullDelta()
    {
        var captured = new List<string>();

        ChatStreamingContinuation.SafeEmitDelta(captured.Add, string.Empty);
        ChatStreamingContinuation.SafeEmitDelta(null, "ignored");

        Assert.Empty(captured);
    }

    [Fact]
    public void SafeEmitDeltaSwallowsCallbackExceptions()
    {
        var ex = Record.Exception(() =>
        {
            ChatStreamingContinuation.SafeEmitDelta(_ => throw new InvalidOperationException("boom"), "x");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void AppendChunkInsertsNewlineBetweenExistingContentAndNextChunk()
    {
        var builder = new StringBuilder("first");

        ChatStreamingContinuation.AppendChunk(builder, "second");

        var result = builder.ToString();
        Assert.Equal($"first{Environment.NewLine}second", result);
    }

    [Fact]
    public void AppendChunkSkipsBlankChunks()
    {
        var builder = new StringBuilder("first");

        ChatStreamingContinuation.AppendChunk(builder, "   ");
        ChatStreamingContinuation.AppendChunk(builder, null!);

        Assert.Equal("first", builder.ToString());
    }

    [Fact]
    public void BuildContinuationPromptIncludesOriginalInputAndTrailingTail()
    {
        var prompt = ChatStreamingContinuation.BuildContinuationPrompt("hello", "writing so far");

        Assert.Contains("길이 제한으로 중간에서 끊겼", prompt);
        Assert.Contains("[원래 사용자 입력]", prompt);
        Assert.Contains("hello", prompt);
        Assert.Contains("[이미 작성된 답변 끝부분]", prompt);
        Assert.Contains("writing so far", prompt);
    }

    [Fact]
    public void BuildContinuationPromptKeepsOnlyTailWhenWrittenTextExceedsLimit()
    {
        // 6500 char written text → tail keeps last 6000 chars, dropping the leading "HEAD_DROP" marker
        var written = "HEAD_DROP" + new string('a', 491) + "TAIL_MARKER" + new string('b', 5989);

        var prompt = ChatStreamingContinuation.BuildContinuationPrompt("hello", written);

        Assert.Contains("TAIL_MARKER", prompt);
        Assert.DoesNotContain("HEAD_DROP", prompt);
    }
}
