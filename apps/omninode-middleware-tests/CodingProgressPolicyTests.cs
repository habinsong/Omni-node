using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingProgressPolicyTests
{
    [Fact]
    public void BuildUpdatePreservesStageMetadata()
    {
        var update = CodingProgressPolicy.BuildUpdate(
            "single",
            "codex",
            "gpt-5",
            "planning",
            "계획 중",
            2,
            6,
            35,
            false,
            "planning",
            "구현 계획",
            "대상 파일: src/app.py",
            3,
            6
        );

        Assert.Equal("single", update.Mode);
        Assert.Equal("codex", update.Provider);
        Assert.Equal("planning", update.StageKey);
        Assert.Equal("구현 계획", update.StageTitle);
        Assert.Equal("대상 파일: src/app.py", update.StageDetail);
        Assert.Equal(3, update.StageIndex);
        Assert.Equal(6, update.StageTotal);
    }

    [Fact]
    public void BuildObjectiveDetailCompactsWhitespaceAndKeepsLanguageHint()
    {
        var detail = CodingProgressPolicy.BuildObjectiveDetail("  React   화면\n만들기  ", "tsx");

        Assert.Equal("React 화면 만들기 · 언어 힌트: tsx", detail);
    }

    [Fact]
    public void BuildWorkspaceDetailReportsTotalFileHeadline()
    {
        var detail = CodingProgressPolicy.BuildWorkspaceDetail(
            """
            total_files=12
            src/app.py
            """
        );

        Assert.Equal("작업공간 점검 완료 · 파일 수 12", detail);
    }

    [Fact]
    public void BuildPlanDetailIncludesAnalysisTargetPathsAndDeferredRun()
    {
        var plan = new CodingLoopPlan(
            "  app.py와 test_app.py를 생성합니다. ",
            string.Empty,
            false,
            Array.Empty<CodingLoopAction>()
        );
        var actions = new[]
        {
            new CodingLoopAction("write_file", "app.py", "print('ok')", string.Empty),
            new CodingLoopAction("write_file", "test_app.py", "def test_ok(): pass", string.Empty),
            new CodingLoopAction("run", string.Empty, string.Empty, "python3 app.py")
        };

        var detail = CodingProgressPolicy.BuildPlanDetail(plan, actions, true);

        Assert.Contains("app.py와 test_app.py를 생성합니다.", detail);
        Assert.Contains("대상 파일: app.py, test_app.py", detail);
        Assert.Contains("실행은 마지막 단계에서 1회만 수행", detail);
    }

    [Fact]
    public void BuildWriteDetailReportsChangedFilesAndDeferredRun()
    {
        var detail = CodingProgressPolicy.BuildWriteDetail(
            new[] { "app.py", "test_app.py", "README.md", "pyproject.toml", "extra.txt" },
            true
        );

        Assert.Contains("변경 파일 5개", detail);
        Assert.Contains("app.py, test_app.py, README.md, pyproject.toml", detail);
        Assert.Contains("중간 실행 없이 최종 검증만 예약", detail);
    }
}
