using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingWorkerSelectionPolicyTests
{
    [Fact]
    public void HasQualityFailureDetectsStatusAndQualityGateMarkers()
    {
        Assert.True(CodingWorkerSelectionPolicy.HasQualityFailure(NewWorker(status: "quality_failed")));
        Assert.True(CodingWorkerSelectionPolicy.HasQualityFailure(NewWorker(stderr: "[quality-gate] failed")));
        Assert.True(CodingWorkerSelectionPolicy.HasQualityFailure(NewWorker(summary: "[quality-gate] failed score=10")));
        Assert.False(CodingWorkerSelectionPolicy.HasQualityFailure(NewWorker(status: "ok")));
    }

    [Fact]
    public void ScoreWorkerRewardsOkAndPenalizesFailure()
    {
        var ok = NewWorker(status: "ok", command: "python main.py", stdout: "[quality-gate] ok score=100");
        var failed = NewWorker(status: "error", command: "(none)", stderr: "[quality-gate] failed");
        var mixed = NewWorker(status: "mixed");
        var qualityFailed = NewWorker(status: "quality_failed");

        Assert.True(CodingWorkerSelectionPolicy.ScoreWorker(ok) > 100);
        Assert.True(CodingWorkerSelectionPolicy.ScoreWorker(failed) < 0);
        Assert.True(CodingWorkerSelectionPolicy.ScoreWorker(mixed) > 0);
        Assert.True(CodingWorkerSelectionPolicy.ScoreWorker(qualityFailed) < 0);
    }

    [Fact]
    public void SelectBestPicksHighestScoringWorker()
    {
        var bad = NewWorker(status: "error");
        var ok = NewWorker(status: "ok", command: "python main.py");
        var fallback = NewWorker(status: "skipped");

        var selected = CodingWorkerSelectionPolicy.SelectBest(new[] { bad, ok }, fallback);

        Assert.Same(ok, selected);
    }

    [Fact]
    public void SelectBestReturnsFallbackForEmptyList()
    {
        var fallback = NewWorker(status: "skipped");
        var selected = CodingWorkerSelectionPolicy.SelectBest(Array.Empty<CodingWorkerResult>(), fallback);

        Assert.Same(fallback, selected);
    }

    [Fact]
    public void MergeChangedFilesDedupSortsAndDropsBlank()
    {
        var a = NewWorker(changedFiles: new[] { "src/A.cs", "src/B.cs" });
        var b = NewWorker(changedFiles: new[] { "src/b.cs", "  ", "src/C.cs" });

        var merged = CodingWorkerSelectionPolicy.MergeChangedFiles(new[] { a, b });

        Assert.Equal(new[] { "src/A.cs", "src/B.cs", "src/C.cs" }, merged);
    }

    [Fact]
    public void MergeChangedFilesForBestPrefersBestWorkerFilesWhenAvailable()
    {
        var best = NewWorker(changedFiles: new[] { "best.cs" });
        var other = NewWorker(changedFiles: new[] { "other.cs" });

        var merged = CodingWorkerSelectionPolicy.MergeChangedFilesForBest(best, new[] { best, other });
        Assert.Equal(new[] { "best.cs" }, merged);
    }

    [Fact]
    public void MergeChangedFilesForBestFallsBackToUnionWhenBestEmpty()
    {
        var best = NewWorker(changedFiles: Array.Empty<string>());
        var other = NewWorker(changedFiles: new[] { "other.cs" });

        var merged = CodingWorkerSelectionPolicy.MergeChangedFilesForBest(best, new[] { best, other });
        Assert.Equal(new[] { "other.cs" }, merged);
    }

    [Fact]
    public void BuildSkippedWorkerProducesSkippedStatusEnvelope()
    {
        var worker = CodingWorkerSelectionPolicy.BuildSkippedWorker(
            "groq",
            "scout",
            "python",
            "보조",
            "/tmp/run",
            "skipped because"
        );

        Assert.Equal("groq", worker.Provider);
        Assert.Equal("scout", worker.Model);
        Assert.Equal("skipped", worker.Execution.Status);
        Assert.Equal("(skipped)", worker.Execution.Command);
        Assert.Equal("skipped because", worker.Summary);
    }

    [Fact]
    public void BuildWorkerWorkspaceRootSlugifiesProviderAndModel()
    {
        var root = CodingWorkerSelectionPolicy.BuildWorkerWorkspaceRoot("/tmp/runs", "NVIDIA/NIM", "Llama 4 Scout 17b");

        Assert.EndsWith("nvidia-nim-llama-4-scout-17b", root);
        Assert.StartsWith("/tmp/runs", root);
    }

    [Fact]
    public void BuildWorkerWorkspaceRootHandlesBlankInputsWithDefaults()
    {
        var root = CodingWorkerSelectionPolicy.BuildWorkerWorkspaceRoot("/tmp/runs", "   ", "   ");
        Assert.EndsWith("worker-model", root);
    }

    [Fact]
    public void BuildMultiResultExecutionPicksStatusFromMix()
    {
        var ok = NewWorker(status: "ok", command: "python a.py");
        var error = NewWorker(status: "error");

        var mixed = CodingWorkerSelectionPolicy.BuildMultiResultExecution("/tmp", "python", new[] { ok, error });
        Assert.Equal("mixed", mixed.Status);

        var allError = CodingWorkerSelectionPolicy.BuildMultiResultExecution("/tmp", "python", new[] { error });
        Assert.Equal("error", allError.Status);

        var allSkipped = CodingWorkerSelectionPolicy.BuildMultiResultExecution(
            "/tmp",
            "python",
            new[] { NewWorker(status: "skipped") }
        );
        Assert.Equal("skipped", allSkipped.Status);
    }

    [Fact]
    public void BuildOrchestrationSummaryListsWorkersAndFinalState()
    {
        var planning = NewWorker(role: "기획", status: "ok", summary: "기획 요약");
        var implementation = NewWorker(role: "개발", status: "ok", summary: "개발 요약");

        var summary = CodingWorkerSelectionPolicy.BuildOrchestrationSummary(
            new[] { planning, implementation },
            implementation
        );

        Assert.Contains("오케스트레이션 단계 결과", summary);
        Assert.Contains("기획", summary);
        Assert.Contains("개발", summary);
        Assert.Contains("최종 상태", summary);
    }

    private static CodingWorkerResult NewWorker(
        string status = "ok",
        string command = "python main.py",
        string stdout = "",
        string stderr = "",
        string summary = "summary",
        string role = "role",
        string[]? changedFiles = null
    )
    {
        var execution = new CodeExecutionResult(
            "python",
            "/tmp/run",
            "main.py",
            command,
            0,
            stdout,
            stderr,
            status
        );
        return new CodingWorkerResult(
            "groq",
            "model",
            "python",
            "raw",
            summary,
            execution,
            changedFiles ?? Array.Empty<string>(),
            role,
            summary
        );
    }
}
