using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingDeterministicStructuredRepairPolicyTests
{
    [Fact]
    public void TryBuildPlanBuildsPythonSnapshotBundle()
    {
        var objective = """
            첫 줄은 "READY"
            SNAPSHOT_JSON_BEGIN
            [
              { "bucket": "beta", "value": 2 },
              { "bucket": "alpha", "value": 3 }
            ]
            SNAPSHOT_JSON_END
            """;

        var ok = CodingDeterministicStructuredRepairPolicy.TryBuildPlan(
            objective,
            "python",
            new[] { "src/main.py", "src/ledger.py", "data/snapshot.json" },
            out var plan
        );

        Assert.True(ok);
        Assert.Equal("python", plan.Language);
        Assert.Equal("src/main.py", plan.PrimaryPath);
        Assert.Equal(3, plan.Files.Count);
        Assert.Contains(plan.Files, file => file.Path == "src/main.py" && file.Content.Contains("print(\"READY\")", StringComparison.Ordinal));
        Assert.Contains(plan.Files, file => file.Path == "data/snapshot.json" && file.Content.Contains("\"bucket\": \"beta\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TryBuildPlanBuildsJavascriptScheduleBundle()
    {
        var objective = """
            stdout "READY"
            SCHEDULE_JSON_BEGIN
            [{ "stream": "ops", "minutes": 25 }]
            SCHEDULE_JSON_END
            """;

        var ok = CodingDeterministicStructuredRepairPolicy.TryBuildPlan(
            objective,
            "javascript",
            new[] { "main.js", "planner.js", "schedule.json" },
            out var plan
        );

        Assert.True(ok);
        Assert.Equal("javascript", plan.Language);
        Assert.Contains(plan.Files, file => file.Path == "main.js" && file.Content.Contains("console.log(\"READY\")", StringComparison.Ordinal));
        Assert.Contains(plan.Files, file => file.Path == "planner.js" && file.Content.Contains("buildScheduleReport", StringComparison.Ordinal));
    }

    [Fact]
    public void TryBuildPlanBuildsHtmlDashboardBundleFromVisibleText()
    {
        var objective = """
            visible text "Dashboard Ready"를 보여줘
            DATASET_BEGIN
            [{ "bucket": "alpha", "value": 3 }]
            DATASET_END
            """;

        var ok = CodingDeterministicStructuredRepairPolicy.TryBuildPlan(
            objective,
            "html",
            new[] { "index.html", "styles.css", "app.js" },
            out var plan
        );

        Assert.True(ok);
        Assert.Equal("html", plan.Language);
        Assert.Equal("app.js", plan.PrimaryPath);
        Assert.Contains(plan.Files, file => file.Path == "app.js" && file.Content.Contains("const TOKEN = 'Dashboard Ready';", StringComparison.Ordinal));
        Assert.Contains(plan.Files, file => file.Path == "index.html" && file.Content.Contains("dashboard-root", StringComparison.Ordinal));
    }

    [Fact]
    public void TryBuildPlanRejectsIncompleteStructuredRequest()
    {
        var ok = CodingDeterministicStructuredRepairPolicy.TryBuildPlan(
            """
            stdout "READY"
            SNAPSHOT_JSON_BEGIN
            []
            SNAPSHOT_JSON_END
            """,
            "python",
            new[] { "main.py", "snapshot.json" },
            out _
        );

        Assert.False(ok);
    }
}
