using System.Runtime.InteropServices;

namespace OmniNode.Middleware;

public interface IStatePathResolver
{
    string StateRootDir { get; }
    string WorkspaceRootDir { get; }
    string DashboardIndexPath { get; }
    string CoreSocketPath { get; }
    string RoutinePromptDir { get; }
    string GetDoctorRoot();
    string GetDoctorLastReportPath();
    string GetDoctorHistoryRoot();
    string GetPlansRoot();
    string GetPlansIndexPath();
    string GetRoutingPolicyPath();
    string GetTaskGraphsRoot();
    string GetTaskGraphsIndexPath();
    string GetTaskRuntimeRoot();
    string GetTaskRuntimePath(string graphId, string taskId);
    string GetLogicRuntimeRoot();
    string GetLogicRuntimePath(string routineId, string runId);
    string GetNotebooksRoot();
    string GetNotebookProjectRoot(string projectKey);
    string GetRefactorPreviewRoot();
    string GetRefactorPreviewPath(string previewId);
    string GetTelegramReplyOutboxPath();
    string GetGlobalSkillsRoot();
    string GetGlobalCommandsRoot();
    string ResolveStateFilePath(string fileName);
    string ResolveStateDirectoryPath(string directoryName);
}

public sealed class DefaultStatePathResolver : IStatePathResolver
{
    public string StateRootDir { get; }
    public string WorkspaceRootDir { get; }
    public string DashboardIndexPath { get; }
    public string CoreSocketPath { get; }
    public string RoutinePromptDir { get; }

    private DefaultStatePathResolver(
        string stateRootDir,
        string workspaceRootDir,
        string dashboardIndexPath,
        string coreSocketPath
    )
    {
        StateRootDir = stateRootDir;
        WorkspaceRootDir = workspaceRootDir;
        DashboardIndexPath = dashboardIndexPath;
        CoreSocketPath = coreSocketPath;
        RoutinePromptDir = Path.Combine(WorkspaceRootDir, "_routine_prompts");
    }

    public static DefaultStatePathResolver CreateDefault()
    {
        var stateRootDir = ResolveDefaultStateDir();
        var workspaceRootDir = ResolveDefaultWorkspaceRootDir();
        var dashboardIndexPath = ResolveDefaultDashboardIndexPath();
        var coreSocketPath = ResolveDefaultCoreSocketPath();
        return new DefaultStatePathResolver(
            stateRootDir,
            workspaceRootDir,
            dashboardIndexPath,
            coreSocketPath
        );
    }

    public string ResolveStateFilePath(string fileName)
    {
        return Path.Combine(StateRootDir, fileName);
    }

    public string ResolveStateDirectoryPath(string directoryName)
    {
        return Path.Combine(StateRootDir, directoryName);
    }

    public string GetDoctorRoot()
    {
        return ResolveStateDirectoryPath("doctor");
    }

    public string GetDoctorLastReportPath()
    {
        return Path.Combine(GetDoctorRoot(), "last-report.json");
    }

    public string GetDoctorHistoryRoot()
    {
        return Path.Combine(GetDoctorRoot(), "history");
    }

    public string GetPlansRoot()
    {
        return ResolveStateDirectoryPath("plans");
    }

    public string GetPlansIndexPath()
    {
        return Path.Combine(GetPlansRoot(), "index.json");
    }

    public string GetRoutingPolicyPath()
    {
        return ResolveStateFilePath("routing-policy.json");
    }

    public string GetTaskGraphsRoot()
    {
        return ResolveStateDirectoryPath("tasks");
    }

    public string GetTaskGraphsIndexPath()
    {
        return Path.Combine(GetTaskGraphsRoot(), "index.json");
    }

    public string GetTaskRuntimeRoot()
    {
        var workspaceContainerRoot = ResolveWorkspaceContainerRoot();
        return Path.Combine(workspaceContainerRoot, ".runtime", "tasks");
    }

    public string GetTaskRuntimePath(string graphId, string taskId)
    {
        return Path.Combine(GetTaskRuntimeRoot(), graphId.Trim(), taskId.Trim());
    }

    public string GetLogicRuntimeRoot()
    {
        var workspaceContainerRoot = ResolveWorkspaceContainerRoot();
        return Path.Combine(workspaceContainerRoot, ".runtime", "logic");
    }

    public string GetLogicRuntimePath(string routineId, string runId)
    {
        return Path.Combine(GetLogicRuntimeRoot(), routineId.Trim(), runId.Trim());
    }

    public string GetNotebooksRoot()
    {
        return ResolveStateDirectoryPath("notebooks");
    }

    public string GetNotebookProjectRoot(string projectKey)
    {
        return Path.Combine(GetNotebooksRoot(), projectKey.Trim());
    }

    public string GetRefactorPreviewRoot()
    {
        var workspaceContainerRoot = ResolveWorkspaceContainerRoot();
        return Path.Combine(workspaceContainerRoot, ".runtime", "refactor-preview");
    }

    public string GetRefactorPreviewPath(string previewId)
    {
        return Path.Combine(GetRefactorPreviewRoot(), $"{previewId.Trim()}.json");
    }

    public string GetTelegramReplyOutboxPath()
    {
        return ResolveStateFilePath("telegram_reply_outbox.json");
    }

    public string GetGlobalSkillsRoot()
    {
        return ResolveStateDirectoryPath("skills");
    }

    public string GetGlobalCommandsRoot()
    {
        return ResolveStateDirectoryPath("commands");
    }

    private static string ResolveDefaultDashboardIndexPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "../../../../omninode-dashboard/index.html")),
            Path.GetFullPath(Path.Combine(cwd, "apps/omninode-dashboard/index.html")),
            Path.GetFullPath(Path.Combine(cwd, "omninode-dashboard/index.html")),
            Path.GetFullPath(Path.Combine(cwd, "../omninode-dashboard/index.html")),
            Path.GetFullPath(Path.Combine(cwd, "../apps/omninode-dashboard/index.html"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static string ResolveDefaultCoreSocketPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return "tcp://127.0.0.1:51808";
        }

        try
        {
            var uid = GetCurrentUnixUid();
            return $"/tmp/omninode_core.{uid}.sock";
        }
        catch
        {
            return "/tmp/omninode_core.sock";
        }
    }

    private static string ResolveDefaultWorkspaceRootDir()
    {
        var baseDir = AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "../../../../../workspace/coding")),
            Path.GetFullPath(Path.Combine(baseDir, "../../../../../coding")),
            Path.GetFullPath(Path.Combine(cwd, "workspace/coding")),
            Path.GetFullPath(Path.Combine(cwd, "coding")),
            Path.GetFullPath(Path.Combine(cwd, "../Omni-node/coding")),
            Path.GetFullPath(Path.Combine(cwd, "../coding")),
            Path.GetFullPath(Path.Combine(cwd, "../workspace/coding"))
        };

        foreach (var candidate in candidates)
        {
            var parent = Directory.GetParent(candidate);
            if (parent != null && Directory.Exists(parent.FullName))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static string ResolveDefaultStateDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return Path.Combine(Path.GetTempPath(), "omninode");
        }

        return Path.Combine(home, ".omninode");
    }

    private string ResolveWorkspaceContainerRoot()
    {
        var workspaceRoot = Path.GetFullPath(WorkspaceRootDir);
        var leaf = Path.GetFileName(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (leaf.Equals("coding", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(workspaceRoot);
            if (parent != null)
            {
                return parent.FullName;
            }
        }

        return workspaceRoot;
    }

    private static uint GetCurrentUnixUid()
    {
        if (OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            return PosixNative.getuid();
        }
        catch
        {
            return 0;
        }
    }

    private static class PosixNative
    {
        [DllImport("libc", SetLastError = true)]
        internal static extern uint getuid();
    }
}
