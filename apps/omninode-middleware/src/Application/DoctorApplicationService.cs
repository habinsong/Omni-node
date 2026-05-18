using System.Collections.Concurrent;

namespace OmniNode.Middleware;

public sealed class DoctorApplicationService : IDoctorApplicationService
{
    private readonly DoctorService _doctorService;
    private readonly AppConfig _config;
    private readonly ConcurrentDictionary<string, DoctorFixPlanResult> _doctorFixPreviews
        = new(StringComparer.Ordinal);

    public DoctorApplicationService(DoctorService doctorService, AppConfig config)
    {
        _doctorService = doctorService;
        _config = config;
    }

    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken)
    {
        return _doctorService.RunAsync(cancellationToken);
    }

    public Task<DoctorReport?> GetLastDoctorReportAsync(CancellationToken cancellationToken)
    {
        return _doctorService.GetLastReportAsync(cancellationToken);
    }

    public async Task<DoctorFixPlanResult> PreviewDoctorFixAsync(CancellationToken cancellationToken)
    {
        var report = await GetLastDoctorReportAsync(cancellationToken)
            ?? await RunDoctorAsync(cancellationToken);
        var actions = BuildDoctorFixActions(report);
        var previewId = $"doctor_fix_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var result = new DoctorFixPlanResult(
            true,
            actions.Count == 0
                ? "자동 적용 가능한 Doctor 복구 항목이 없습니다."
                : $"Doctor 복구 preview를 생성했습니다: {actions.Count}개 항목",
            previewId,
            actions
        );
        _doctorFixPreviews[previewId] = result;
        return result;
    }

    public DoctorFixPlanResult ApplyDoctorFix(string previewId)
    {
        var normalizedPreviewId = (previewId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPreviewId)
            || !_doctorFixPreviews.TryGetValue(normalizedPreviewId, out var preview))
        {
            return new DoctorFixPlanResult(false, "유효한 Doctor fix previewId가 필요합니다.", normalizedPreviewId, Array.Empty<DoctorFixAction>(), "preview_not_found");
        }

        var applied = new List<DoctorFixAction>();
        foreach (var action in preview.Actions)
        {
            if (!action.AutoApply || action.Kind != "create_directory")
            {
                applied.Add(action with { Status = "skipped" });
                continue;
            }

            try
            {
                Directory.CreateDirectory(action.Target);
                applied.Add(action with { Status = "applied", Error = null });
            }
            catch (Exception ex)
            {
                applied.Add(action with { Status = "failed", Error = ex.Message });
            }
        }

        var failed = applied.Count(action => action.Status == "failed");
        var ok = failed == 0;
        var message = ok
            ? "Doctor 복구 항목을 적용했습니다."
            : $"Doctor 복구 일부 실패: {failed}개";
        var result = new DoctorFixPlanResult(ok, message, normalizedPreviewId, applied, ok ? null : "apply_failed");
        _doctorFixPreviews[normalizedPreviewId] = result;
        return result;
    }

    private IReadOnlyList<DoctorFixAction> BuildDoctorFixActions(DoctorReport report)
    {
        var actions = new List<DoctorFixAction>();
        var directories = new[]
        {
            _config.WorkspaceRootDir,
            _config.CodeRunsRootDir,
            _config.RoutineRunsRootDir,
            _config.MemoryNotesRootDir,
            Path.GetDirectoryName(_config.ConversationStatePath) ?? string.Empty,
            Path.GetDirectoryName(_config.AuditLogPath) ?? string.Empty
        }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var directory in directories)
        {
            if (Directory.Exists(directory))
            {
                continue;
            }

            actions.Add(new DoctorFixAction(
                $"create_directory_{actions.Count + 1:00}",
                "create_directory",
                directory,
                "누락된 Omni-node 상태/워크스페이스 디렉터리를 생성합니다.",
                AutoApply: true
            ));
        }

        foreach (var check in report.Checks.Where(check => check.Status is DoctorStatus.Warn or DoctorStatus.Fail))
        {
            foreach (var suggested in check.SuggestedActions.Take(4))
            {
                actions.Add(new DoctorFixAction(
                    $"manual_{actions.Count + 1:00}",
                    "manual_check",
                    check.Id,
                    suggested,
                    AutoApply: false
                ));
            }
        }

        return actions.Take(24).ToArray();
    }
}
