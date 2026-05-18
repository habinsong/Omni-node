namespace OmniNode.Middleware;

public sealed class SearchPipelineDoctorCheck : IDoctorCheck
{
    private readonly ProviderOptions _providers;
    private readonly ContextOptions _context;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly SearchGateway _searchGateway;
    private readonly ISearchGuard _searchGuard;
    private readonly ISearchAnswerComposer _searchAnswerComposer;

    public SearchPipelineDoctorCheck(
        ProviderOptions providers,
        ContextOptions context,
        RuntimeSettings runtimeSettings,
        SearchGateway searchGateway,
        ISearchGuard searchGuard,
        ISearchAnswerComposer searchAnswerComposer
    )
    {
        _providers = providers;
        _context = context;
        _runtimeSettings = runtimeSettings;
        _searchGateway = searchGateway;
        _searchGuard = searchGuard;
        _searchAnswerComposer = searchAnswerComposer;
    }

    public string Id => "search_pipeline";

    public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasGeminiKey = !string.IsNullOrWhiteSpace(_runtimeSettings.GetGeminiApiKey());
        var hasSearchModel = !string.IsNullOrWhiteSpace(_providers.GeminiSearchModel);
        var pipelineReady = _searchGateway != null && _searchGuard != null && _searchAnswerComposer != null;
        var status = !pipelineReady || !hasSearchModel
            ? DoctorStatus.Fail
            : hasGeminiKey
                ? DoctorStatus.Ok
                : DoctorStatus.Warn;

        var summary = status switch
        {
            DoctorStatus.Ok => "검색 파이프라인 기본 구성이 준비되었습니다.",
            DoctorStatus.Warn => "검색 파이프라인은 연결되어 있지만 Gemini 시크릿이 비어 있습니다.",
            _ => "검색 파이프라인 필수 구성이 비어 있습니다."
        };

        var actions = new List<string>();
        if (!hasGeminiKey)
        {
            actions.Add("grounded 검색을 쓰려면 Gemini API Key를 설정하세요.");
        }

        if (!hasSearchModel || !pipelineReady)
        {
            actions.Add("검색 모델과 `SearchAnswerGuard`/composer 조립 상태를 점검하세요.");
        }

        return Task.FromResult(new DoctorCheckResult(
            Id,
            status,
            summary,
            $"geminiSearchModel={_providers.GeminiSearchModel}; geminiKey={(hasGeminiKey ? "set" : "missing")}; fastWebPipeline={_context.EnableFastWebPipeline}; guard={(pipelineReady ? "ready" : "missing")}",
            actions
        ));
    }
}
