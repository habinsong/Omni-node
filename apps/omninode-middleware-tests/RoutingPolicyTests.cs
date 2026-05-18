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

    [Fact]
    public void FileStoreRestoresValidBackupWhenPrimaryIsCorrupt()
    {
        var dir = CreateTempDirectory();
        var resolver = new TestStatePathResolver(dir);
        var path = resolver.GetRoutingPolicyPath();
        var backupJson = "{\n  \"generalChat\": [\"groq\", \"gemini\"]\n}\n";
        File.WriteAllText(path, "{broken");
        File.WriteAllText(path + ".bak", backupJson);

        var store = new FileRoutingPolicyStore(resolver);
        var policy = store.LoadOverrides();

        Assert.Equal(new[] { "groq", "gemini" }, policy.GeneralChat);
        Assert.Equal(backupJson, File.ReadAllText(path));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "omninode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestStatePathResolver : IStatePathResolver
    {
        public TestStatePathResolver(string stateRootDir)
        {
            StateRootDir = stateRootDir;
            WorkspaceRootDir = Path.Combine(stateRootDir, "workspace");
            DashboardIndexPath = Path.Combine(stateRootDir, "index.html");
            CoreSocketPath = Path.Combine(stateRootDir, "core.sock");
            RoutinePromptDir = Path.Combine(WorkspaceRootDir, "_routine_prompts");
        }

        public string StateRootDir { get; }
        public string WorkspaceRootDir { get; }
        public string DashboardIndexPath { get; }
        public string CoreSocketPath { get; }
        public string RoutinePromptDir { get; }
        public string GetDoctorRoot() => Path.Combine(StateRootDir, "doctor");
        public string GetDoctorLastReportPath() => Path.Combine(GetDoctorRoot(), "last-report.json");
        public string GetDoctorHistoryRoot() => Path.Combine(GetDoctorRoot(), "history");
        public string GetPlansRoot() => Path.Combine(StateRootDir, "plans");
        public string GetPlansIndexPath() => Path.Combine(GetPlansRoot(), "index.json");
        public string GetRoutingPolicyPath() => Path.Combine(StateRootDir, "routing-policy.json");
        public string GetTaskGraphsRoot() => Path.Combine(StateRootDir, "tasks");
        public string GetTaskGraphsIndexPath() => Path.Combine(GetTaskGraphsRoot(), "index.json");
        public string GetTaskRuntimeRoot() => Path.Combine(StateRootDir, ".runtime", "tasks");
        public string GetTaskRuntimePath(string graphId, string taskId) => Path.Combine(GetTaskRuntimeRoot(), graphId, taskId);
        public string GetLogicRuntimeRoot() => Path.Combine(StateRootDir, ".runtime", "logic");
        public string GetLogicRuntimePath(string routineId, string runId) => Path.Combine(GetLogicRuntimeRoot(), routineId, runId);
        public string GetNotebooksRoot() => Path.Combine(StateRootDir, "notebooks");
        public string GetNotebookProjectRoot(string projectKey) => Path.Combine(GetNotebooksRoot(), projectKey);
        public string GetRefactorPreviewRoot() => Path.Combine(StateRootDir, ".runtime", "refactor-preview");
        public string GetRefactorPreviewPath(string previewId) => Path.Combine(GetRefactorPreviewRoot(), $"{previewId}.json");
        public string GetTelegramReplyOutboxPath() => Path.Combine(StateRootDir, "telegram_reply_outbox.json");
        public string GetGlobalSkillsRoot() => Path.Combine(StateRootDir, "skills");
        public string GetGlobalCommandsRoot() => Path.Combine(StateRootDir, "commands");
        public string ResolveStateFilePath(string fileName) => Path.Combine(StateRootDir, fileName);
        public string ResolveStateDirectoryPath(string directoryName) => Path.Combine(StateRootDir, directoryName);
    }
}
