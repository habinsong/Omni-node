using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingLoopTuningPolicyTests
{
    [Fact]
    public void ShouldUseOneShotModeRequiresConfigProfileLanguageAndUiOrGameSignal()
    {
        static bool FrontendLike(string objective, string language)
        {
            return objective.Contains("웹", StringComparison.OrdinalIgnoreCase);
        }

        static bool GameLike(string objective, string language)
        {
            return objective.Contains("게임", StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(CodingLoopTuningPolicy.ShouldUseOneShotMode(true, true, "웹 게임 작성", "html", FrontendLike, GameLike));
        Assert.False(CodingLoopTuningPolicy.ShouldUseOneShotMode(false, true, "웹 게임 작성", "html", FrontendLike, GameLike));
        Assert.False(CodingLoopTuningPolicy.ShouldUseOneShotMode(true, false, "웹 게임 작성", "html", FrontendLike, GameLike));
        Assert.False(CodingLoopTuningPolicy.ShouldUseOneShotMode(true, true, "API 서버 작성", "go", FrontendLike, GameLike));
        Assert.False(CodingLoopTuningPolicy.ShouldUseOneShotMode(true, true, "CLI 작성", "python", FrontendLike, GameLike));
    }

    [Fact]
    public void ResolveLoopLimitsClampToMinimumsAndOneShotValues()
    {
        Assert.Equal(1, CodingLoopTuningPolicy.ResolveMaxIterations(4, oneShotMode: true));
        Assert.Equal(1, CodingLoopTuningPolicy.ResolveMaxIterations(0, oneShotMode: false));
        Assert.Equal(3, CodingLoopTuningPolicy.ResolveMaxActions(loopMaxActions: 3, oneShotMaxActions: 5, oneShotMode: false));
        Assert.Equal(5, CodingLoopTuningPolicy.ResolveMaxActions(loopMaxActions: 3, oneShotMaxActions: 5, oneShotMode: true));
        Assert.Equal(1200, CodingLoopTuningPolicy.ResolvePlanMaxOutputTokens(900));
    }

    [Fact]
    public void ResolveMaxRepairPassesRaisesForInteractiveOrMultiFileTasks()
    {
        Assert.Equal(3, CodingLoopTuningPolicy.ResolveMaxRepairPasses(
            "pygame 게임 작성",
            "python",
            (_, _) => true,
            (_, _) => false,
            _ => false
        ));
        Assert.Equal(2, CodingLoopTuningPolicy.ResolveMaxRepairPasses(
            "src/app.js 와 src/index.html 작성",
            "javascript",
            (_, _) => false,
            (_, _) => false,
            _ => false
        ));
        Assert.Equal(1, CodingLoopTuningPolicy.ResolveMaxRepairPasses(
            "main.py 파일에서 ok 출력",
            "python",
            (_, _) => false,
            (_, _) => false,
            _ => false
        ));
    }

    [Fact]
    public void BuildRecentLoopLogsKeepsMostRecentEntriesAndTrimsEachEntry()
    {
        var logs = CodingLoopTuningPolicy.BuildRecentLoopLogs(
            new[] { "old", new string('a', 520), "latest" },
            recentLoopHistory: 2
        );

        Assert.DoesNotContain("old", logs);
        Assert.Contains("latest", logs);
        Assert.Contains("...(truncated)", logs);
    }
}
