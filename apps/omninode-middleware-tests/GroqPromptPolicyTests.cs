using System.Net;
using System.Net.Http;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GroqPromptPolicyTests
{
    [Fact]
    public void ClampGroqMaxOutputTokensForModelCapsCompoundModels()
    {
        var clamped = GroqPromptPolicy.ClampGroqMaxOutputTokensForModel("groq/compound", 4096);
        Assert.Equal(1024, clamped);
    }

    [Fact]
    public void ClampGroqMaxOutputTokensForModelKeepsRegularModelsWithinUpperBound()
    {
        var clamped = GroqPromptPolicy.ClampGroqMaxOutputTokensForModel("llama-3", 16384);
        Assert.Equal(8192, clamped);
    }

    [Theory]
    [InlineData("groq/compound-beta", 12000)]
    [InlineData("llama-3-70b", 22000)]
    public void ResolveGroqPromptBudgetCharsMatchesModelClass(string model, int expected)
    {
        Assert.Equal(expected, GroqPromptPolicy.ResolveGroqPromptBudgetChars(model));
    }

    [Fact]
    public void TruncatePromptForGroqShortensOversizedPromptWithMarker()
    {
        var oversized = new string('a', 30000);

        var truncated = GroqPromptPolicy.TruncatePromptForGroq(oversized, 22000);

        Assert.True(truncated.Length <= 22000);
        Assert.Contains("중간 내용 생략됨", truncated);
    }

    [Fact]
    public void TruncatePromptForGroqPassesThroughSmallPrompt()
    {
        var truncated = GroqPromptPolicy.TruncatePromptForGroq("short", 22000);
        Assert.Equal("short", truncated);
    }

    [Fact]
    public void SplitPromptToMultiTurnExtractsHistoryAndRequest()
    {
        var prompt = string.Join("\n",
            "[컨텍스트 사용 규칙]",
            "do the thing",
            "[최근 대화]",
            "[user] hello",
            "[assistant] hi",
            "[새 요청] what's up"
        );

        var messages = GroqPromptPolicy.SplitPromptToMultiTurn(prompt);

        Assert.Equal(4, messages.Count);
        Assert.Equal("system_context", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("hello", messages[1].Content);
        Assert.Equal("assistant", messages[2].Role);
        Assert.Equal("hi", messages[2].Content);
        Assert.Equal("user", messages[3].Role);
        Assert.Equal("what's up", messages[3].Content);
    }

    [Fact]
    public void SplitPromptToMultiTurnFallsBackToSingleMessageWhenMarkersMissing()
    {
        var messages = GroqPromptPolicy.SplitPromptToMultiTurn("just a single request");

        Assert.Single(messages);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("just a single request", messages[0].Content);
    }

    [Fact]
    public void TryExtractGroqMaxTokensLimitReadsErrorPayload()
    {
        var body = "{\"error\":{\"message\":\"`max_tokens` must be less than or equal to `4096`\"}}";

        Assert.True(GroqPromptPolicy.TryExtractGroqMaxTokensLimit(body, out var limit));
        Assert.Equal(4096, limit);
    }

    [Fact]
    public void IsGroqRequestTooLargeFlagsExplicitStatus()
    {
        Assert.True(GroqPromptPolicy.IsGroqRequestTooLarge(HttpStatusCode.RequestEntityTooLarge, string.Empty));
    }

    [Fact]
    public void IsGroqRequestTooLargeMatchesErrorCodeInBody()
    {
        Assert.True(GroqPromptPolicy.IsGroqRequestTooLarge(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"request_too_large\"}}"));
    }

    [Fact]
    public void GetGroqRetryDelayMsHonorsRetryAfterHeader()
    {
        var response = new HttpResponseMessage();
        response.Headers.Add("retry-after", "2");

        var delay = GroqPromptPolicy.GetGroqRetryDelayMs(response.Headers, 1200);

        Assert.Equal(2000, delay);
    }

    [Fact]
    public void GetGroqRetryDelayMsClampsFallbackBetweenSafeBounds()
    {
        var response = new HttpResponseMessage();

        var delay = GroqPromptPolicy.GetGroqRetryDelayMs(response.Headers, 30000);

        Assert.Equal(5000, delay);
    }
}
