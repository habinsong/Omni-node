namespace OmniNode.Middleware;

public sealed class DoctorService
{
    private readonly IReadOnlyList<IDoctorCheck> _checks;
    private readonly FileDoctorReportStore _store;
    private readonly DoctorOptions _options;

    public DoctorService(
        IEnumerable<IDoctorCheck> checks,
        FileDoctorReportStore store,
        DoctorOptions options
    )
    {
        _checks = checks.ToArray();
        _store = store;
        _options = options;
    }

    public async Task<DoctorReport> RunAsync(CancellationToken cancellationToken)
    {
        var results = new List<DoctorCheckResult>(_checks.Count);
        foreach (var check in _checks)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.DoctorTimeoutSeconds));

            try
            {
                var result = await check.RunAsync(timeoutCts.Token);
                results.Add(NormalizeResult(check.Id, result));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results.Add(new DoctorCheckResult(
                    check.Id,
                    DoctorStatus.Fail,
                    "진단 시간이 초과되었습니다.",
                    $"timeout={_options.DoctorTimeoutSeconds}s",
                    new[] { "OMNINODE_DOCTOR_TIMEOUT_SECONDS 값을 늘리거나 관련 의존성 응답 시간을 점검하세요." }
                ));
            }
            catch (Exception ex)
            {
                results.Add(new DoctorCheckResult(
                    check.Id,
                    DoctorStatus.Fail,
                    "진단 중 예외가 발생했습니다.",
                    DoctorSupport.Trim(ex.Message, 400),
                    new[] { "미들웨어 로그와 해당 의존성 상태를 함께 점검하세요." }
                ));
            }
        }

        var report = new DoctorReport(
            $"doctor-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            DateTimeOffset.UtcNow,
            results,
            results.Count(result => result.Status == DoctorStatus.Ok),
            results.Count(result => result.Status == DoctorStatus.Warn),
            results.Count(result => result.Status == DoctorStatus.Fail),
            results.Count(result => result.Status == DoctorStatus.Skip)
        );

        _store.Save(report, _options.DoctorWriteHistory);
        return report;
    }

    public Task<DoctorReport?> GetLastReportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.TryLoadLastReport());
    }

    private static DoctorCheckResult NormalizeResult(string fallbackId, DoctorCheckResult result)
    {
        var id = string.IsNullOrWhiteSpace(result.Id) ? fallbackId : result.Id.Trim();
        var summary = string.IsNullOrWhiteSpace(result.Summary)
            ? "요약 없음"
            : result.Summary.Trim();
        var actions = (result.SuggestedActions ?? Array.Empty<string>())
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Select(action => action.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return result with
        {
            Id = id,
            Summary = summary,
            Detail = string.IsNullOrWhiteSpace(result.Detail) ? null : result.Detail.Trim(),
            SuggestedActions = actions
        };
    }
}
