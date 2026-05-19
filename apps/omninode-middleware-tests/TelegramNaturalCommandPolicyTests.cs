using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class TelegramNaturalCommandPolicyTests
{
    [Theory]
    [InlineData("help llm", "/help llm")]
    [InlineData("코딩 상태 보여줘", "/coding status")]
    [InlineData("코딩 파일 목록", "/coding files")]
    [InlineData("safe refactor 상태", "/refactor status")]
    [InlineData("메모리 초기화", "/memory clear")]
    [InlineData("루틴 목록", "/routine list")]
    [InlineData("메트릭 보여", "/metrics")]
    public void TryBuildNaturalPseudoCommandMapsSimpleCommands(string input, string expected)
    {
        Assert.Equal(expected, TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand(input));
    }

    [Fact]
    public void TryBuildNaturalPseudoCommandMapsCodingRunWithRequest()
    {
        var command = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("단일 코딩: fizzbuzz 만들어줘");

        Assert.Equal("/coding single run fizzbuzz 만들어줘", command);
    }

    [Fact]
    public void TryBuildNaturalPseudoCommandMapsCodingProviderAndModel()
    {
        var provider = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("오케스트레이션 코딩 제공자를 제미니로 바꿔");
        var model = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("다중 코딩 모델 qwen/test로 변경");

        Assert.Equal("/coding orchestration provider gemini", provider);
        Assert.Equal("/coding multi model qwen/test", model);
    }

    [Fact]
    public void TryBuildNaturalPseudoCommandMapsLlmProviderAndModel()
    {
        var provider = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("단일 제공자를 세레브라스로 바꿔");
        var model = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("오케스트레이션 모델 llama/test");

        Assert.Equal("/llm single provider cerebras", provider);
        Assert.Equal("/llm orchestration model llama/test", model);
    }

    [Fact]
    public void TryBuildNaturalPseudoCommandMapsPlanAndTaskCommands()
    {
        var plan = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("계획 생성: 배포 절차 정리");
        var task = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("작업 결과 graph1 step2");

        Assert.Equal("/plan create 배포 절차 정리", plan);
        Assert.Equal("/task output graph1 step2", task);
    }

    [Fact]
    public void TryBuildNaturalPseudoCommandMapsNotebookAndRoutineCommands()
    {
        var notebook = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("노트북 추가 learning: 배운 점");
        var routine = TelegramNaturalCommandPolicy.TryBuildNaturalPseudoCommand("루틴 수정 daily-news: 시간 9시로");

        Assert.Equal("/notebook append learning 배운 점", notebook);
        Assert.Equal("/routine update daily-news 시간 9시로", routine);
    }

    [Theory]
    [InlineData("그록", false, "groq")]
    [InlineData("제미니", false, "gemini")]
    [InlineData("자동", true, "auto")]
    [InlineData("자동", false, null)]
    public void ExtractProviderAliasNormalizesAliases(string text, bool allowAuto, string? expected)
    {
        Assert.Equal(expected, TelegramNaturalCommandPolicy.ExtractProviderAlias(text, allowAuto));
    }

    [Theory]
    [InlineData("도움말 llm", "llm")]
    [InlineData("코딩 도움말", "coding")]
    [InlineData("도움말", "")]
    [InlineData("그냥 질문", null)]
    public void ExtractHelpTopicDetectsTopic(string text, string? expected)
    {
        Assert.Equal(expected, TelegramNaturalCommandPolicy.ExtractHelpTopic(text));
    }
}
