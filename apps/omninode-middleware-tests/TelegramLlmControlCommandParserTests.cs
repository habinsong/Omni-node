using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class TelegramLlmControlCommandParserTests
{
    [Theory]
    [InlineData("/llm")]
    [InlineData("/LLM")]
    [InlineData("/llm extra")]
    public void IsControlCommandMatchesLlmPrefixCaseInsensitively(string text)
    {
        Assert.True(TelegramLlmControlCommandParser.IsControlCommand(text));
    }

    [Theory]
    [InlineData("/model groq")]
    [InlineData("hello")]
    [InlineData("")]
    public void IsControlCommandRejectsNonLlm(string text)
    {
        Assert.False(TelegramLlmControlCommandParser.IsControlCommand(text));
    }

    [Theory]
    [InlineData("/llm")]
    [InlineData("/llm help")]
    [InlineData("/llm HELP")]
    public void ParseReturnsHelpForBareOrHelp(string text)
    {
        Assert.Equal(TelegramLlmControlCommandKind.Help, TelegramLlmControlCommandParser.Parse(text).Kind);
    }

    [Fact]
    public void ParseReturnsStatus()
    {
        Assert.Equal(TelegramLlmControlCommandKind.Status, TelegramLlmControlCommandParser.Parse("/llm status").Kind);
    }

    [Fact]
    public void ParseModeLowercasesArgAndFlagsUsage()
    {
        var ok = TelegramLlmControlCommandParser.Parse("/llm mode ORCHESTRATION");
        Assert.Equal(TelegramLlmControlCommandKind.SetMode, ok.Kind);
        Assert.Equal("orchestration", ok.Primary);

        var usage = TelegramLlmControlCommandParser.Parse("/llm mode");
        Assert.Equal(TelegramLlmControlCommandKind.UsageError, usage.Kind);
        Assert.Equal("사용법: /llm mode <single|orchestration|multi>", usage.Message);
    }

    [Fact]
    public void ParseModelsDefaultsToAll()
    {
        Assert.Equal("all", TelegramLlmControlCommandParser.Parse("/llm models").Primary);
        var withTarget = TelegramLlmControlCommandParser.Parse("/llm models GROQ");
        Assert.Equal(TelegramLlmControlCommandKind.Models, withTarget.Kind);
        Assert.Equal("groq", withTarget.Primary);
    }

    [Theory]
    [InlineData("/llm usage")]
    [InlineData("/llm limits")]
    [InlineData("/llm quota")]
    public void ParseUsageReportAliases(string text)
    {
        Assert.Equal(TelegramLlmControlCommandKind.Usage, TelegramLlmControlCommandParser.Parse(text).Kind);
    }

    [Fact]
    public void ParseSetGroqAndCopilotKeepModelCase()
    {
        var groq = TelegramLlmControlCommandParser.Parse("/llm set groq meta-llama/Llama-4-Scout");
        Assert.Equal(TelegramLlmControlCommandKind.SetGroqModel, groq.Kind);
        Assert.Equal("meta-llama/Llama-4-Scout", groq.Primary);

        var copilot = TelegramLlmControlCommandParser.Parse("/llm set copilot GPT-5-mini");
        Assert.Equal(TelegramLlmControlCommandKind.SetCopilotModel, copilot.Kind);
        Assert.Equal("GPT-5-mini", copilot.Primary);
    }

    [Theory]
    [InlineData("nvidia")]
    [InlineData("nvidia-nim")]
    [InlineData("nvidia_nim")]
    [InlineData("nim")]
    public void ParseSetNvidiaAliasesNormalizeToNvidiaProviderThenModel(string alias)
    {
        var cmd = TelegramLlmControlCommandParser.Parse($"/llm set {alias} model-x");
        Assert.Equal(TelegramLlmControlCommandKind.SetSingleProviderThenModel, cmd.Kind);
        Assert.Equal("nvidia", cmd.Primary);
        Assert.Equal("model-x", cmd.Secondary);
    }

    [Fact]
    public void ParseSetCodexProducesProviderThenModel()
    {
        var cmd = TelegramLlmControlCommandParser.Parse("/llm set codex gpt-5.4");
        Assert.Equal(TelegramLlmControlCommandKind.SetSingleProviderThenModel, cmd.Kind);
        Assert.Equal("codex", cmd.Primary);
        Assert.Equal("gpt-5.4", cmd.Secondary);
    }

    [Fact]
    public void ParseSetRejectsUnknownProviderAndShortInput()
    {
        var unknown = TelegramLlmControlCommandParser.Parse("/llm set gemini model-x");
        Assert.Equal(TelegramLlmControlCommandKind.UsageError, unknown.Kind);
        Assert.Equal("사용법: /llm set <groq|copilot|codex|nvidia> <model-id>", unknown.Message);

        var short_ = TelegramLlmControlCommandParser.Parse("/llm set groq");
        Assert.Equal(TelegramLlmControlCommandKind.UsageError, short_.Kind);
    }

    [Fact]
    public void ParseSingleProviderAndModel()
    {
        var provider = TelegramLlmControlCommandParser.Parse("/llm single provider GEMINI");
        Assert.Equal(TelegramLlmControlCommandKind.SetSingleProvider, provider.Kind);
        Assert.Equal("gemini", provider.Primary);

        var model = TelegramLlmControlCommandParser.Parse("/llm single model gemini-3.1-flash");
        Assert.Equal(TelegramLlmControlCommandKind.SetSingleModel, model.Kind);
        Assert.Equal("gemini-3.1-flash", model.Secondary);
    }

    [Fact]
    public void ParseSingleModelEmptyAfterKeywordFlagsModelUsage()
    {
        // "/llm single model " collapses to 3 tokens -> slot usage (len<4)
        var slotUsage = TelegramLlmControlCommandParser.Parse("/llm single model");
        Assert.Equal(TelegramLlmControlCommandKind.UsageError, slotUsage.Kind);
        Assert.Contains("single provider", slotUsage.Message);
    }

    [Fact]
    public void ParseSingleUnknownSubReturnsSlotUsage()
    {
        var cmd = TelegramLlmControlCommandParser.Parse("/llm single foo bar");
        Assert.Equal(TelegramLlmControlCommandKind.UsageError, cmd.Kind);
        Assert.Contains("/llm single provider", cmd.Message);
    }

    [Fact]
    public void ParseOrchestrationProviderAndModel()
    {
        var provider = TelegramLlmControlCommandParser.Parse("/llm orchestration provider auto");
        Assert.Equal(TelegramLlmControlCommandKind.SetOrchestrationProvider, provider.Kind);
        Assert.Equal("auto", provider.Primary);

        var model = TelegramLlmControlCommandParser.Parse("/llm orchestration model some-model");
        Assert.Equal(TelegramLlmControlCommandKind.SetOrchestrationModel, model.Kind);
        Assert.Equal("some-model", model.Secondary);
    }

    [Theory]
    [InlineData("groq", "multi.groq")]
    [InlineData("gemini", "multi.gemini")]
    [InlineData("copilot", "multi.copilot")]
    [InlineData("cerebras", "multi.cerebras")]
    [InlineData("codex", "multi.codex")]
    [InlineData("nvidia", "multi.nvidia")]
    [InlineData("nim", "multi.nvidia")]
    public void ParseMultiChannelModelMapsKeyToChannel(string key, string expectedChannel)
    {
        var cmd = TelegramLlmControlCommandParser.Parse($"/llm multi {key} the-model");
        Assert.Equal(TelegramLlmControlCommandKind.SetMultiChannelModel, cmd.Kind);
        Assert.Equal(expectedChannel, cmd.Primary);
        Assert.Equal("the-model", cmd.Secondary);
    }

    [Fact]
    public void ParseMultiSummaryLowercasesProvider()
    {
        var cmd = TelegramLlmControlCommandParser.Parse("/llm multi summary AUTO");
        Assert.Equal(TelegramLlmControlCommandKind.SetMultiSummaryProvider, cmd.Kind);
        Assert.Equal("auto", cmd.Primary);
    }

    [Fact]
    public void ParseMultiUnknownKeyAndShortInputReturnUsage()
    {
        var unknown = TelegramLlmControlCommandParser.Parse("/llm multi bogus value");
        Assert.Equal(TelegramLlmControlCommandKind.UsageError, unknown.Kind);
        Assert.Contains("/llm multi", unknown.Message);

        var short_ = TelegramLlmControlCommandParser.Parse("/llm multi groq");
        Assert.Equal(TelegramLlmControlCommandKind.UsageError, short_.Kind);
    }

    [Fact]
    public void ParseUnknownSubcommandReturnsUnknownMessage()
    {
        var cmd = TelegramLlmControlCommandParser.Parse("/llm bogus");
        Assert.Equal(TelegramLlmControlCommandKind.Unknown, cmd.Kind);
        Assert.Equal("알 수 없는 /llm 명령입니다. /llm help 또는 자연어 요청을 사용하세요.", cmd.Message);
    }
}
