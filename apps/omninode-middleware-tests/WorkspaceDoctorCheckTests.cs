using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class WorkspaceDoctorCheckTests
{
    [Fact]
    public async Task RunAsyncReportsCorruptJsonFileBackupRisk()
    {
        var root = CreateTempDirectory();
        var workspaceRoot = Path.Combine(root, "workspace");
        var stateRoot = Path.Combine(root, "state");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(stateRoot);
        File.WriteAllText(Path.Combine(stateRoot, "broken-with-backup.json"), "{broken");
        File.WriteAllText(Path.Combine(stateRoot, "broken-with-backup.json.bak"), "{\"ok\":true}");
        File.WriteAllText(Path.Combine(stateRoot, "broken-no-backup.json"), "{broken");

        var check = new WorkspaceDoctorCheck(CreatePathOptions(root, workspaceRoot), new TestStatePathResolver(root, stateRoot, workspaceRoot));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.Equal(DoctorStatus.Warn, result.Status);
        Assert.Contains("corruptJson=2", result.Detail);
        Assert.Contains("broken-with-backup.json:backup=yes", result.Detail);
        Assert.Contains("broken-no-backup.json:backup=no", result.Detail);
        Assert.Contains(result.SuggestedActions, action => action.Contains("`.bak`가 없는 손상 JSON", StringComparison.Ordinal));
    }

    private static PathOptions CreatePathOptions(string root, string workspaceRoot)
    {
        var stateRoot = Path.Combine(root, "state");
        return new PathOptions(
            CoreSocketPath: Path.Combine(root, "core.sock"),
            DashboardIndexPath: Path.Combine(root, "index.html"),
            LlmUsageStatePath: Path.Combine(stateRoot, "llm_usage.json"),
            CopilotUsageStatePath: Path.Combine(stateRoot, "copilot_usage.json"),
            ConversationStatePath: Path.Combine(stateRoot, "conversations.json"),
            AuthSessionStatePath: Path.Combine(stateRoot, "auth_sessions.json"),
            MemoryNotesRootDir: Path.Combine(stateRoot, "memory"),
            CodeRunsRootDir: Path.Combine(workspaceRoot, "coding"),
            RoutineRunsRootDir: Path.Combine(workspaceRoot, "routines"),
            WorkspaceRootDir: workspaceRoot,
            RoutineStatePath: Path.Combine(stateRoot, "routines.json"),
            RoutinePromptDir: Path.Combine(workspaceRoot, "_routine_prompts"),
            AuditLogPath: Path.Combine(stateRoot, "audit.log"),
            GuardRetryTimelineStatePath: Path.Combine(stateRoot, "guard_retry_timeline.json"),
            GatewayHealthStatePath: Path.Combine(stateRoot, "gateway_health.json"),
            GatewayStartupProbeStatePath: Path.Combine(stateRoot, "gateway_startup_probe.json"),
            DashboardAccessStatePath: Path.Combine(stateRoot, "dashboard_access.json"),
            SandboxExecutorPath: Path.Combine(root, "executor.py")
        );
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "omninode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestStatePathResolver : IStatePathResolver
    {
        private readonly string _root;

        public TestStatePathResolver(string root, string stateRootDir, string workspaceRootDir)
        {
            _root = root;
            StateRootDir = stateRootDir;
            WorkspaceRootDir = workspaceRootDir;
            DashboardIndexPath = Path.Combine(root, "index.html");
            CoreSocketPath = Path.Combine(root, "core.sock");
            RoutinePromptDir = Path.Combine(workspaceRootDir, "_routine_prompts");
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
        public string GetTaskRuntimeRoot() => Path.Combine(_root, ".runtime", "tasks");
        public string GetTaskRuntimePath(string graphId, string taskId) => Path.Combine(GetTaskRuntimeRoot(), graphId, taskId);
        public string GetLogicRuntimeRoot() => Path.Combine(_root, ".runtime", "logic");
        public string GetLogicRuntimePath(string routineId, string runId) => Path.Combine(GetLogicRuntimeRoot(), routineId, runId);
        public string GetNotebooksRoot() => Path.Combine(StateRootDir, "notebooks");
        public string GetNotebookProjectRoot(string projectKey) => Path.Combine(GetNotebooksRoot(), projectKey);
        public string GetRefactorPreviewRoot() => Path.Combine(_root, ".runtime", "refactor-preview");
        public string GetRefactorPreviewPath(string previewId) => Path.Combine(GetRefactorPreviewRoot(), $"{previewId}.json");
        public string GetTelegramReplyOutboxPath() => Path.Combine(StateRootDir, "telegram_reply_outbox.json");
        public string GetGlobalSkillsRoot() => Path.Combine(StateRootDir, "skills");
        public string GetGlobalCommandsRoot() => Path.Combine(StateRootDir, "commands");
        public string ResolveStateFilePath(string fileName) => Path.Combine(StateRootDir, fileName);
        public string ResolveStateDirectoryPath(string directoryName) => Path.Combine(StateRootDir, directoryName);
    }
}
