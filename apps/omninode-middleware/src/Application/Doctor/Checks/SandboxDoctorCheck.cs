using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class SandboxDoctorCheck : IDoctorCheck
{
    private readonly ProviderOptions _providers;
    private readonly PathOptions _paths;
    private readonly DoctorOptions _options;
    private readonly PythonSandboxClient _sandboxClient;

    public SandboxDoctorCheck(
        ProviderOptions providers,
        PathOptions paths,
        DoctorOptions options,
        PythonSandboxClient sandboxClient
    )
    {
        _providers = providers;
        _paths = paths;
        _options = options;
        _sandboxClient = sandboxClient;
    }

    public string Id => "sandbox";

    public async Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.DoctorEnableSandboxSmoke)
        {
            return new DoctorCheckResult(
                Id,
                DoctorStatus.Skip,
                "샌드박스 스모크 테스트가 비활성화되어 있습니다.",
                "OMNINODE_DOCTOR_ENABLE_SANDBOX_SMOKE=false",
                new[] { "샌드박스 실행기까지 함께 점검하려면 `OMNINODE_DOCTOR_ENABLE_SANDBOX_SMOKE=true`로 실행하세요." }
            );
        }

        try
        {
            var output = await _sandboxClient.ExecuteCodeAsync("print('ok')", cancellationToken);
            var normalized = (output ?? string.Empty).Trim();
            if (!IsSuccessfulSandboxSmoke(normalized))
            {
                return new DoctorCheckResult(
                    Id,
                    DoctorStatus.Fail,
                    "샌드박스 스모크 출력이 예상과 다릅니다.",
                    $"output={DoctorSupport.Trim(normalized, 240)}",
                    new[] { "`apps/omninode-sandbox/executor.py` 직접 실행으로 경로와 Python 환경을 점검하세요." }
                );
            }

            return new DoctorCheckResult(
                Id,
                DoctorStatus.Ok,
                "샌드박스 스모크 테스트가 정상입니다.",
                $"python={_providers.PythonBinary}; executor={_paths.SandboxExecutorPath}; output={normalized}",
                Array.Empty<string>()
            );
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult(
                Id,
                DoctorStatus.Fail,
                "샌드박스 스모크 테스트 실행에 실패했습니다.",
                DoctorSupport.Trim(ex.Message, 320),
                new[] { "Python 바이너리와 샌드박스 실행기 경로를 확인하세요.", "`python3 apps/omninode-sandbox/executor.py --code \"print('ok')\"`를 직접 실행해 보세요." }
            );
        }
    }

    private static bool IsSuccessfulSandboxSmoke(string output)
    {
        if (output.Equals("ok", StringComparison.Ordinal))
        {
            return true;
        }

        if (!output.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement)
                ? (statusElement.GetString() ?? string.Empty).Trim()
                : string.Empty;
            var stdout = root.TryGetProperty("stdout", out var stdoutElement)
                ? (stdoutElement.GetString() ?? string.Empty).Trim()
                : string.Empty;
            return status.Equals("ok", StringComparison.OrdinalIgnoreCase)
                && stdout.Equals("ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
