namespace OmniNode.Middleware;

public sealed class CoreSocketDoctorCheck : IDoctorCheck
{
    private readonly AppConfig _config;
    private readonly UdsCoreClient _coreClient;

    public CoreSocketDoctorCheck(AppConfig config, UdsCoreClient coreClient)
    {
        _config = config;
        _coreClient = coreClient;
    }

    public string Id => "core_socket";

    public async Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var endpoint = _config.CoreSocketPath;
        var usesTcpEndpoint = UdsCoreClient.IsTcpEndpoint(endpoint);
        var lockPath = DoctorSupport.ResolveCoreLockPath();
        var auditParent = Path.GetDirectoryName(_config.AuditLogPath) ?? "(none)";
        var socketExists = usesTcpEndpoint || File.Exists(endpoint);
        var lockExists = usesTcpEndpoint || File.Exists(lockPath);
        var auditParentExists = Directory.Exists(auditParent);

        if (!socketExists)
        {
            var suggestions = usesTcpEndpoint
                ? new[] { "apps/omninode-core를 빌드하고 코어 프로세스를 다시 기동하세요.", "Windows에서는 기본 TCP 포트 51808 사용 여부와 `OMNINODE_CORE_SOCKET_PATH` 값을 함께 확인하세요." }
                : new[] { "apps/omninode-core를 빌드하고 코어 프로세스를 다시 기동하세요.", "남아 있는 `/tmp/omninode*.sock` 또는 lock 파일 꼬임 여부를 점검하세요." };
            return new DoctorCheckResult(
                Id,
                DoctorStatus.Fail,
                "코어 엔드포인트를 찾을 수 없습니다.",
                $"endpoint={endpoint}; lock={(lockExists ? "present" : "missing")}; auditParent={auditParent}({(auditParentExists ? "present" : "missing")})",
                suggestions
            );
        }

        try
        {
            var metrics = await _coreClient.GetMetricsAsync(cancellationToken);
            var status = lockExists && auditParentExists
                ? DoctorStatus.Ok
                : DoctorStatus.Warn;
            var summary = status == DoctorStatus.Ok
                ? "코어 엔드포인트 응답이 정상입니다."
                : "코어 엔드포인트는 응답했지만 보조 경로 점검에 경고가 있습니다.";
            return new DoctorCheckResult(
                Id,
                status,
                summary,
                $"endpoint={endpoint}; lock={(lockExists ? "present" : "missing")}; auditParent={auditParent}({(auditParentExists ? "present" : "missing")}); metrics={DoctorSupport.Trim(metrics, 240)}",
                status == DoctorStatus.Ok
                    ? Array.Empty<string>()
                    : new[] { "코어 lock 파일과 감사 로그 부모 디렉터리 존재 여부를 점검하세요." }
            );
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult(
                Id,
                DoctorStatus.Fail,
                "코어 엔드포인트 연결에 실패했습니다.",
                $"endpoint={endpoint}; error={DoctorSupport.Trim(ex.Message, 280)}",
                new[] { "코어 프로세스가 실제로 실행 중인지 확인하세요.", "엔드포인트가 `OMNINODE_CORE_SOCKET_PATH`와 일치하는지 확인하세요." }
            );
        }
    }
}
