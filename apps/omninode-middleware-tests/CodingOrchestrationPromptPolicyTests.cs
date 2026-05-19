using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingOrchestrationPromptPolicyTests
{
    [Fact]
    public void PlannerPromptIncludesLanguageRoleGoalsAndOriginalRequest()
    {
        var prompt = CodingOrchestrationPromptPolicy.BuildPlannerPrompt(
            "사용자 입력 텍스트",
            "python",
            "기획자"
        );

        Assert.Contains("오케스트레이션 코딩의 기획 단계", prompt);
        Assert.Contains("언어 힌트: python", prompt);
        Assert.Contains("역할: 기획자", prompt);
        Assert.Contains("구현 범위와 핵심 파일 후보", prompt);
        Assert.Contains("사용자 입력 텍스트", prompt);
    }

    [Fact]
    public void ImplementationPromptForwardsPlanningSummary()
    {
        var planning = NewWorker("plan summary");

        var prompt = CodingOrchestrationPromptPolicy.BuildImplementationPrompt(
            "입력",
            "typescript",
            "개발자",
            planning
        );

        Assert.Contains("개발 단계", prompt);
        Assert.Contains("plan summary", prompt);
        Assert.Contains("더미 구현, TODO, 미완성 보고 금지", prompt);
        Assert.Contains("입력", prompt);
    }

    [Fact]
    public void VerificationPromptIncludesPlanningAndImplementationSummaries()
    {
        var planning = NewWorker("PLAN");
        var implementation = NewWorker("IMPL");

        var prompt = CodingOrchestrationPromptPolicy.BuildVerificationPrompt(
            "입력",
            "go",
            "검증자",
            planning,
            implementation
        );

        Assert.Contains("검증 및 테스트 단계", prompt);
        Assert.Contains("PLAN", prompt);
        Assert.Contains("IMPL", prompt);
        Assert.Contains("최소 수정까지 포함해 보정 가능", prompt);
    }

    [Fact]
    public void FixPromptThreadsAllStageSummaries()
    {
        var planning = NewWorker("PLAN");
        var implementation = NewWorker("IMPL");
        var verification = NewWorker("VERIFY");

        var prompt = CodingOrchestrationPromptPolicy.BuildFixPrompt(
            "입력",
            "rust",
            "리페어어",
            planning,
            implementation,
            verification
        );

        Assert.Contains("수정 단계", prompt);
        Assert.Contains("PLAN", prompt);
        Assert.Contains("IMPL", prompt);
        Assert.Contains("VERIFY", prompt);
        Assert.Contains("남은 오류, 실패, 누락 요구사항", prompt);
    }

    [Fact]
    public void MultiWorkerPromptForbidsCoordinationAndDummyOutput()
    {
        var prompt = CodingOrchestrationPromptPolicy.BuildMultiWorkerPrompt("입력", "kotlin");

        Assert.Contains("다중 코딩 모드의 독립 실행 모델", prompt);
        Assert.Contains("언어 힌트: kotlin", prompt);
        Assert.Contains("다른 모델과 협의하지 않고", prompt);
        Assert.Contains("더미 구현, TODO, 가짜 성공 보고 금지", prompt);
        Assert.Contains("입력", prompt);
    }

    [Fact]
    public void AutoInputDefaultsLanguageHintToAutoWhenBlank()
    {
        var withLanguage = CodingOrchestrationPromptPolicy.BuildAutoInput("python");
        var blank = CodingOrchestrationPromptPolicy.BuildAutoInput("   ");

        Assert.Contains("자동 오케스트레이션 코딩 모드", withLanguage);
        Assert.Contains("언어 힌트: python", withLanguage);
        Assert.Contains("언어 힌트: auto", blank);
        Assert.Contains("워크스페이스를 점검하고 실행 가능한 개선 작업 1건 수행", blank);
    }

    private static CodingWorkerResult NewWorker(string summary)
    {
        var execution = new CodeExecutionResult(
            "python",
            "/tmp/run",
            "main.py",
            "python main.py",
            0,
            "stdout",
            "stderr",
            "ok"
        );
        return new CodingWorkerResult(
            "groq",
            "model",
            "python",
            "raw",
            summary,
            execution,
            Array.Empty<string>(),
            "role",
            summary
        );
    }
}
