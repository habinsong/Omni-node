namespace OmniNode.Middleware.Tests;

public sealed class RoutingPolicyTests
{
    [Fact]
    public void CloneCreatesIndependentCopy()
    {
        var policy = new RoutingPolicy
        {
            GeneralChat = new[] { "groq", "gemini" }
        };

        var clone = policy.Clone();
        clone.GeneralChat![0] = "copilot";

        Assert.Equal("groq", policy.GeneralChat![0]);
        Assert.Equal("copilot", clone.GeneralChat[0]);
    }

    [Fact]
    public void GetChainReturnsConfiguredChain()
    {
        var policy = new RoutingPolicy
        {
            DeepCode = new[] { "codex", "groq" }
        };

        var chain = policy.GetChain(TaskCategory.DeepCode);

        Assert.NotNull(chain);
        Assert.Equal(new[] { "codex", "groq" }, chain);
    }
}
