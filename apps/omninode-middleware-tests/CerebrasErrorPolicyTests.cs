using System.Net;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CerebrasErrorPolicyTests
{
    [Fact]
    public void IsModelNotFoundRecognizesExplicitCodeField()
    {
        var body = "{\"code\":\"model_not_found\",\"message\":\"...\"}";
        Assert.True(CerebrasErrorPolicy.IsModelNotFound(body));
    }

    [Fact]
    public void IsModelNotFoundFallsBackToSubstringMatch()
    {
        var body = "the requested model does not exist or you do not have access";
        Assert.True(CerebrasErrorPolicy.IsModelNotFound(body));
    }

    [Fact]
    public void IsModelNotFoundReturnsFalseForUnrelatedBody()
    {
        Assert.False(CerebrasErrorPolicy.IsModelNotFound("{\"error\":\"rate_limit\"}"));
    }

    [Theory]
    [InlineData("zai-glm-4.7")]
    [InlineData("qwen-3-235b-a22b-instruct-2507")]
    public void BuildHttpFailureTextMentionsPreviewFallbackForRateLimitedPreviewModels(string model)
    {
        var text = CerebrasErrorPolicy.BuildHttpFailureText(HttpStatusCode.TooManyRequests, model);

        Assert.Contains("preview", text);
        Assert.Contains("gpt-oss-120b", text);
    }

    [Fact]
    public void BuildHttpFailureTextReturnsGenericRateLimitForRegularModels()
    {
        var text = CerebrasErrorPolicy.BuildHttpFailureText(HttpStatusCode.TooManyRequests, "gpt-oss-120b");

        Assert.Contains("429", text);
        Assert.DoesNotContain("preview", text);
    }

    [Fact]
    public void BuildHttpFailureTextHandlesServiceUnavailable()
    {
        var text = CerebrasErrorPolicy.BuildHttpFailureText(HttpStatusCode.ServiceUnavailable, "gpt-oss-120b");

        Assert.Contains("503", text);
    }

    [Fact]
    public void BuildHttpFailureTextDefaultsToStatusCodeMessage()
    {
        var text = CerebrasErrorPolicy.BuildHttpFailureText(HttpStatusCode.InternalServerError, "gpt-oss-120b");

        Assert.Contains("500", text);
    }
}
