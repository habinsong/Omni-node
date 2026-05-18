namespace OmniNode.Middleware;

public sealed class WorkspaceDoctorCheck : IDoctorCheck
{
    private readonly PathOptions _paths;
    private readonly IStatePathResolver _pathResolver;

    public WorkspaceDoctorCheck(PathOptions paths, IStatePathResolver pathResolver)
    {
        _paths = paths;
        _pathResolver = pathResolver;
    }

    public string Id => "workspace";

    public async Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var workspaceRoot = _paths.WorkspaceRootDir;
        var workspaceEnv = Environment.GetEnvironmentVariable("OMNINODE_WORKSPACE_ROOT");
        if (!Directory.Exists(workspaceRoot))
        {
            return new DoctorCheckResult(
                Id,
                DoctorStatus.Fail,
                "워크스페이스 루트를 찾을 수 없습니다.",
                $"workspace={workspaceRoot}; env={(string.IsNullOrWhiteSpace(workspaceEnv) ? "auto" : workspaceEnv)}",
                new[] { "OMNINODE_WORKSPACE_ROOT를 올바른 canonical 경로로 설정하세요.", "`workspace/coding` 경로가 실제로 존재하는지 확인하세요." }
            );
        }

        var workspaceProbe = await TryProbeAsync(
            Path.Combine(Directory.GetParent(workspaceRoot)?.FullName ?? workspaceRoot, ".runtime", "doctor"),
            "workspace",
            cancellationToken
        );
        var stateProbe = await TryProbeAsync(_pathResolver.GetDoctorRoot(), "state", cancellationToken);
        var stateArtifacts = InspectStateArtifacts(_pathResolver.StateRootDir);
        var status = workspaceProbe.Ok && stateProbe.Ok
            ? stateArtifacts.CorruptJsonCount > 0
                ? DoctorStatus.Warn
                : DoctorStatus.Ok
            : DoctorStatus.Fail;

        var actions = new List<string>();
        if (!workspaceProbe.Ok || !stateProbe.Ok)
        {
            actions.Add("워크스페이스와 `~/.omninode`의 쓰기 권한을 확인하세요.");
            actions.Add("상태 루트가 다른 파일시스템 정책에 막히지 않았는지 확인하세요.");
        }

        if (stateArtifacts.CorruptJsonCount > 0)
        {
            actions.Add("손상된 상태 JSON을 점검하고, 필요한 경우 같은 경로의 `.bak` 파일로 복구하세요.");
            if (stateArtifacts.CorruptJsonFiles.Any(x => x.EndsWith(":backup=no", StringComparison.OrdinalIgnoreCase)))
            {
                actions.Add("`.bak`가 없는 손상 JSON은 자동 복구 대상이 아니므로 파일별 수동 확인이 필요합니다.");
            }
        }

        return new DoctorCheckResult(
            Id,
            status,
            status == DoctorStatus.Ok
                ? "워크스페이스와 상태 루트에 접근할 수 있습니다."
                : status == DoctorStatus.Warn
                    ? "상태 루트에 접근 가능하지만 손상된 상태 JSON이 감지되었습니다."
                    : "워크스페이스 또는 상태 루트 접근이 실패했습니다.",
            $"workspace={workspaceRoot}; env={(string.IsNullOrWhiteSpace(workspaceEnv) ? "auto" : workspaceEnv)}; workspaceProbe={workspaceProbe.Detail}; stateRoot={_pathResolver.StateRootDir}; stateProbe={stateProbe.Detail}; stateJson={stateArtifacts.JsonCount}; backups={stateArtifacts.BackupCount}; locks={stateArtifacts.LockCount}; corruptJson={stateArtifacts.CorruptJsonCount}{FormatCorruptJsonFilesDetail(stateArtifacts)}",
            actions
        );
    }

    private static async Task<(bool Ok, string Detail)> TryProbeAsync(
        string directoryPath,
        string label,
        CancellationToken cancellationToken
    )
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var probePath = Path.Combine(directoryPath, $"doctor-probe-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);
            return (true, $"{label}=ok:{directoryPath}");
        }
        catch (Exception ex)
        {
            return (false, $"{label}=fail:{directoryPath}:{DoctorSupport.Trim(ex.Message, 180)}");
        }
    }

    private static StateArtifactSummary InspectStateArtifacts(string stateRootDir)
    {
        if (!Directory.Exists(stateRootDir))
        {
            return new StateArtifactSummary(0, 0, 0, 0, Array.Empty<string>());
        }

        var jsonCount = 0;
        var backupCount = 0;
        var lockCount = 0;
        var corruptJsonCount = 0;
        var corruptJsonFiles = new List<string>();

        foreach (var path in Directory.EnumerateFiles(stateRootDir, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                lockCount++;
                continue;
            }

            if (fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                backupCount++;
                continue;
            }

            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            jsonCount++;
            try
            {
                using var _ = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            }
            catch
            {
                corruptJsonCount++;
                if (corruptJsonFiles.Count < 8)
                {
                    corruptJsonFiles.Add(FormatCorruptJsonFileRisk(stateRootDir, path));
                }
            }
        }

        return new StateArtifactSummary(jsonCount, backupCount, lockCount, corruptJsonCount, corruptJsonFiles);
    }

    private static string FormatCorruptJsonFilesDetail(StateArtifactSummary stateArtifacts)
    {
        if (stateArtifacts.CorruptJsonFiles.Count == 0)
        {
            return string.Empty;
        }

        var hiddenCount = Math.Max(0, stateArtifacts.CorruptJsonCount - stateArtifacts.CorruptJsonFiles.Count);
        var suffix = hiddenCount > 0 ? $",+{hiddenCount} more" : string.Empty;
        return $"; corruptJsonFiles={string.Join(",", stateArtifacts.CorruptJsonFiles)}{suffix}";
    }

    private static string FormatCorruptJsonFileRisk(string stateRootDir, string path)
    {
        var relativePath = Path.GetRelativePath(stateRootDir, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            relativePath = Path.GetFileName(path);
        }

        var hasBackup = File.Exists(path + ".bak") ? "yes" : "no";
        return $"{relativePath}:backup={hasBackup}";
    }

    private sealed record StateArtifactSummary(
        int JsonCount,
        int BackupCount,
        int LockCount,
        int CorruptJsonCount,
        IReadOnlyList<string> CorruptJsonFiles
    );
}
