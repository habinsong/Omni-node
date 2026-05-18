namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken)
        => _doctorAppService.RunDoctorAsync(cancellationToken);

    public Task<DoctorReport?> GetLastDoctorReportAsync(CancellationToken cancellationToken)
        => _doctorAppService.GetLastDoctorReportAsync(cancellationToken);

    public Task<DoctorFixPlanResult> PreviewDoctorFixAsync(CancellationToken cancellationToken)
        => _doctorAppService.PreviewDoctorFixAsync(cancellationToken);

    public DoctorFixPlanResult ApplyDoctorFix(string previewId)
        => _doctorAppService.ApplyDoctorFix(previewId);

    private async Task<string> ExecuteDoctorReportCommandAsync(
        bool json,
        bool latestOnly,
        CancellationToken cancellationToken
    )
    {
        var report = latestOnly
            ? await GetLastDoctorReportAsync(cancellationToken)
            : await RunDoctorAsync(cancellationToken);

        if (json)
        {
            return report == null ? "null" : DoctorJson.Serialize(report, indented: true);
        }

        return DoctorCli.RenderText(report);
    }
}
