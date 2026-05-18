namespace OmniNode.Middleware;

public sealed class DoctorApplicationService : IDoctorApplicationService
{
    private readonly CommandService _inner;

    public DoctorApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken)
    {
        return _inner.RunDoctorAsync(cancellationToken);
    }

    public Task<DoctorReport?> GetLastDoctorReportAsync(CancellationToken cancellationToken)
    {
        return _inner.GetLastDoctorReportAsync(cancellationToken);
    }

    public Task<DoctorFixPlanResult> PreviewDoctorFixAsync(CancellationToken cancellationToken)
    {
        return _inner.PreviewDoctorFixAsync(cancellationToken);
    }

    public DoctorFixPlanResult ApplyDoctorFix(string previewId)
    {
        return _inner.ApplyDoctorFix(previewId);
    }
}
