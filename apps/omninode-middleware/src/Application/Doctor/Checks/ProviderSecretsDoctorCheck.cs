namespace OmniNode.Middleware;

public sealed class ProviderSecretsDoctorCheck : IDoctorCheck
{
    private readonly RuntimeSettings _runtimeSettings;

    public ProviderSecretsDoctorCheck(RuntimeSettings runtimeSettings)
    {
        _runtimeSettings = runtimeSettings;
    }

    public string Id => "provider_secrets";

    public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var evaluations = new[]
        {
            EvaluateProvider("gemini", _runtimeSettings.GetGeminiApiKey(), "OMNINODE_GEMINI_API_KEY_FILE"),
            EvaluateProvider("groq", _runtimeSettings.GetGroqApiKey(), "OMNINODE_GROQ_API_KEY_FILE"),
            EvaluateProvider("cerebras", _runtimeSettings.GetCerebrasApiKey(), "OMNINODE_CEREBRAS_API_KEY_FILE"),
            EvaluateProvider("nvidia", _runtimeSettings.GetNvidiaApiKey(), "OMNINODE_NVIDIA_API_KEY_FILE")
        };

        var hasSecurityFailure = evaluations.Any(item => item.SecurityFailure);
        var missingCount = evaluations.Count(item => !item.HasValue);
        var status = hasSecurityFailure
            ? DoctorStatus.Fail
            : missingCount > 0
                ? DoctorStatus.Warn
                : DoctorStatus.Ok;

        var summary = status switch
        {
            DoctorStatus.Ok => "주요 제공자 시크릿이 모두 준비되었습니다.",
            DoctorStatus.Fail => "시크릿 파일 경로 또는 권한에 문제가 있습니다.",
            _ => "일부 제공자 시크릿이 비어 있습니다."
        };

        var actions = new List<string>();
        if (hasSecurityFailure)
        {
            actions.Add("시크릿 파일이 있다면 권한을 0600 이하로 조정하고 존재 여부를 확인하세요.");
        }

        if (missingCount > 0)
        {
            actions.Add("필요한 제공자 키를 `*_API_KEY_FILE` 또는 보안 저장소로 설정하세요.");
        }

        return Task.FromResult(new DoctorCheckResult(
            Id,
            status,
            summary,
            string.Join("; ", evaluations.Select(item => item.Detail)),
            actions
        ));
    }

    private static ProviderSecretEvaluation EvaluateProvider(
        string providerName,
        string? runtimeValue,
        string fileEnvKey
    )
    {
        var filePath = (Environment.GetEnvironmentVariable(fileEnvKey) ?? string.Empty).Trim();
        var hasValue = !string.IsNullOrWhiteSpace(runtimeValue);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new ProviderSecretEvaluation(
                hasValue,
                false,
                $"{providerName}={(hasValue ? "configured" : "missing")}; fileEnv=unset"
            );
        }

        if (!File.Exists(filePath))
        {
            return new ProviderSecretEvaluation(
                hasValue,
                true,
                $"{providerName}={(hasValue ? "configured" : "missing")}; file={filePath}(missing)"
            );
        }

        if (DoctorSupport.TryGetUnixFileMode(filePath, out var mode) && DoctorSupport.HasLoosePermissions(mode))
        {
            return new ProviderSecretEvaluation(
                hasValue,
                true,
                $"{providerName}={(hasValue ? "configured" : "missing")}; file={filePath}; mode={DoctorSupport.FormatUnixFileMode(mode)}(too_open)"
            );
        }

        var modeText = DoctorSupport.TryGetUnixFileMode(filePath, out mode)
            ? DoctorSupport.FormatUnixFileMode(mode)
            : "n/a";
        return new ProviderSecretEvaluation(
            hasValue,
            false,
            $"{providerName}={(hasValue ? "configured" : "missing")}; file={filePath}; mode={modeText}"
        );
    }

    private sealed record ProviderSecretEvaluation(
        bool HasValue,
        bool SecurityFailure,
        string Detail
    );
}
