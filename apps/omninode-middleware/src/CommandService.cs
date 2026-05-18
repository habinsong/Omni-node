using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService :
    IGatewayApplicationService
{
    private const string DefaultGroqPrimaryModel = "meta-llama/llama-4-scout-17b-16e-instruct";
    private const string DefaultGroqFastModel = "llama-3.1-8b-instant";
    private const string DefaultGroqComplexModel = "qwen/qwen3-32b";
    private const string RoutineModelMaverick = "meta-llama/llama-4-maverick-17b-128e-instruct";
    private const string RoutineModelGptOss = "openai/gpt-oss-120b";
    private const string RoutineModelKimi = "moonshotai/kimi-k2-instruct-0905";
    private const string DefaultCerebrasModel = "gpt-oss-120b";
    private const string LegacyCerebrasLlamaModel = "llama3.1-8b";
    private const string DefaultCopilotModel = "gpt-5-mini";
    private const int TelegramUpgradeDailyCap = 100;
    // 텔레그램 응답 길이 제한 — TelegramClient가 3900자 단위로 자동 분할 송신하므로
    // 응답 자체는 거의 무제한으로 생성 후 분할하도록 큰 값으로 설정.
    private const int TelegramMaxResponseChars = 60000;
    private const int TelegramFastModeMaxOutputTokens = 1024;
    private const int TelegramComplexModeMaxOutputTokens = 1536;
    // 텔레그램 입력 압축 임계값 — 대화탭과 동일하게 사실상 비활성화 (LLM 컨텍스트 한도까지 그대로 전달).
    private const int TelegramLongContextThresholdChars = 200000;
    private const int TelegramLongContextTargetChars = 1200;
    private static readonly Regex CodeFenceRegex = new("```([a-zA-Z0-9#+._-]*)\\s*\\n(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex JsonObjectRegex = new("\\{[\\s\\S]*\\}", RegexOptions.Compiled);
    private static readonly Regex LeadingTitleNoiseRegex = new(
        @"^(?:(?:[-*#>]+\s*)|(?:\(\d+\)\s+)|(?:\d+[.)]\s+))+",
        RegexOptions.Compiled
    );
    private static readonly Regex RepeatedChunkRegex = new(@"(.{12,120}?)(?:\s+\1){2,}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex OuterHtmlContainerRegex = new(@"^\s*<\s*(p|pre|code)\b[^>]*>([\s\S]*)</\s*\1\s*>\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex JsonTrailingCommaRegex = new(@",\s*([}\]])", RegexOptions.Compiled);
    private static readonly Regex DomainRegex = new(@"([a-z0-9][a-z0-9-]*\.[a-z]{2,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex JsonContentFieldRegex = new("\"content\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex JsonPathFieldRegex = new("\"path\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex HttpUrlRegex = new(
        "https?://[^\\s<>()\\\"'`]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex HtmlTitleRegex = new(
        @"<title[^>]*>([\s\S]*?)</title>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex HtmlTagStripRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled
    );
    private static readonly Regex CopilotFetchParagraphRegex = new(
        @"●?\s*<p>\s*Fetching the Copilot CLI documentation[\s\S]*?</p>\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex CopilotFetchSentenceRegex = new(
        @"Fetching the Copilot CLI documentation[\s\S]*?parallel\.\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex HtmlBreakTagRegex = new(
        @"<\s*(?:br|/p|/div|/li)\s*/?\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled
    );
    private static readonly Regex LeadingBulletSymbolRegex = new(
        @"^\s*[●•▪◦■□▶➤❖]+\s*",
        RegexOptions.Compiled
    );
    private static readonly Regex CopilotMetaLineRegex = new(
        @"(?i)(fetch_copilot_cli_documentation|fetching the copilot cli documentation|문서 조회 및 진행 상태 보고|활성 모델 확인을 위해 copilot cli 문서를 조회|i'?ll call the docs fetch|현재 작업:\s*fetch_copilot_cli_documentation)",
        RegexOptions.Compiled
    );
    private static readonly Regex ThinkTagBlockRegex = new(
        @"<think\b[^>]*>[\s\S]*?</think>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ThinkTagInlineRegex = new(
        @"</?think\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly HttpClient WebFetchClient = CreateWebFetchClient();
    private readonly ProviderOptions _providers;
    private readonly PathOptions _paths;
    private readonly SecurityOptions _security;
    private readonly ContextOptions _context;
    private readonly ExecutionOptions _execution;
    private readonly LlmRouter _llmRouter;
    private readonly GroqModelCatalog _groqModelCatalog;
    private readonly UdsCoreClient _coreClient;
    private readonly TelegramClient _telegramClient;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly ProviderRegistry _providerRegistry;
    private readonly RoutingPolicyResolver _routingPolicyResolver;
    private readonly ToolRegistry _toolRegistry;
    private readonly SearchGateway _searchGateway;
    private readonly ISearchGuard _searchGuard;
    private readonly ISearchAnswerComposer _searchAnswerComposer;
    private readonly WebFetchTool _webFetchTool;
    private readonly MemorySearchTool _memorySearchTool;
    private readonly MemoryGetTool _memoryGetTool;
    private readonly SessionListTool _sessionListTool;
    private readonly SessionHistoryTool _sessionHistoryTool;
    private readonly SessionSendTool _sessionSendTool;
    private readonly SessionSpawnTool _sessionSpawnTool;
    private readonly BrowserTool _browserTool;
    private readonly CanvasTool _canvasTool;
    private readonly NodesTool _nodesTool;
    private SkillCreateDirective? _skillCreateDirective;
    private SkillCreateDirective SkillCreateDirective =>
        _skillCreateDirective ??= new SkillCreateDirective(_paths.WorkspaceRootDir);
    private SkillFileService? _skillFileService;
    private SkillFileService SkillFiles =>
        _skillFileService ??= new SkillFileService(_paths);
    private readonly ConcurrentDictionary<string, string> _activeSkillByThread = new(StringComparer.Ordinal);
    private readonly CopilotCliWrapper _copilotWrapper;
    private readonly CodexCliWrapper _codexWrapper;
    private readonly PythonSandboxClient _sandboxClient;
    private readonly IMemoryNoteStore _memoryNoteStore;
    private readonly IConversationStore _conversationStore;
    private readonly IRunArtifactStore _runArtifactStore;
    private readonly UniversalCodeRunner _codeRunner;
    private readonly AuditLogger _auditLogger;
    private readonly DoctorService _doctorService;
    private readonly PlanService _planService;
    private readonly PlanReviewService _planReviewService;
    private readonly TaskGraphService _taskGraphService;
    private readonly BackgroundTaskCoordinator _taskGraphCoordinator;
    private readonly ProjectContextLoader _projectContextLoader;
    private readonly NotebookService _notebookService;
    private readonly AnchorReadService _anchorReadService;
    private readonly AnchorEditService _anchorEditService;
    private readonly DiffPreviewService _diffPreviewService;
    private readonly LspRefactorService _lspRefactorService;
    private readonly AstGrepRefactorService _astGrepRefactorService;
    private readonly Queue<string> _recentEvents = new();
    private readonly object _eventLock = new();
    private readonly object _telegramUpgradeQuotaLock = new();
    private readonly IDoctorApplicationService _doctorAppService;
    private readonly INotebookApplicationService _notebookAppService;
    private readonly ISettingsApplicationService _settingsAppService;
    private readonly IRefactorApplicationService _refactorAppService;
    private readonly IContextApplicationService _contextAppService;
    private readonly CleanupService _cleanupService;
    private readonly ITaskGraphApplicationService _taskGraphAppService;
    private readonly IMemoryApplicationService _memoryAppService;
    private readonly IPlanningApplicationService _planAppService;
    private readonly IConversationApplicationService _conversationAppService;
    private readonly IToolApplicationService _toolAppService;
    private readonly LlmPreferenceContext _llmPreferenceContext;
    private readonly ExecutionContext _executionContext;
    private readonly RoutineRegistry _routineRegistry;
    private readonly string _telegramUpgradeQuotaStatePath;
    private readonly string _routineStatePath;
    private readonly string _routinePromptDir;
    private readonly string[] _killAllowlist;
    private readonly CancellationTokenSource _routineSchedulerCts = new();
    private readonly SemaphoreSlim _routineSchedulerDispatchGate = new(2, 2);
    private Task? _routineSchedulerTask;
    private string? _routineSchedulerLastError;
    private string _telegramUpgradeQuotaDay = string.Empty;
    private int _telegramUpgradeQuotaCount;

    private bool IsDynamicCodeExecutionEnabled()
    {
        return _security.EnableDynamicCode;
    }

    private static string BuildDynamicCodeDisabledMessage()
    {
        return "dynamic code is disabled. set OMNINODE_ENABLE_DYNAMIC_CODE=true";
    }

    internal CommandService(
        AppConfig config,
        LlmRouter llmRouter,
        GroqModelCatalog groqModelCatalog,
        UdsCoreClient coreClient,
        TelegramClient telegramClient,
        RuntimeSettings runtimeSettings,
        ProviderRegistry providerRegistry,
        RoutingPolicyResolver routingPolicyResolver,
        ToolRegistry toolRegistry,
        SearchGateway searchGateway,
        ISearchGuard searchGuard,
        ISearchAnswerComposer searchAnswerComposer,
        WebFetchTool webFetchTool,
        MemorySearchTool memorySearchTool,
        MemoryGetTool memoryGetTool,
        SessionListTool sessionListTool,
        SessionHistoryTool sessionHistoryTool,
        SessionSendTool sessionSendTool,
        SessionSpawnTool sessionSpawnTool,
        BrowserTool browserTool,
        CanvasTool canvasTool,
        NodesTool nodesTool,
        CopilotCliWrapper copilotWrapper,
        CodexCliWrapper codexWrapper,
        PythonSandboxClient sandboxClient,
        IMemoryNoteStore memoryNoteStore,
        IConversationStore conversationStore,
        IRunArtifactStore runArtifactStore,
        UniversalCodeRunner codeRunner,
        AuditLogger auditLogger,
        DoctorService doctorService,
        PlanService planService,
        PlanReviewService planReviewService,
        TaskGraphService taskGraphService,
        BackgroundTaskCoordinator taskGraphCoordinator,
        ProjectContextLoader projectContextLoader,
        NotebookService notebookService,
        AnchorReadService anchorReadService,
        AnchorEditService anchorEditService,
        DiffPreviewService diffPreviewService,
        LspRefactorService lspRefactorService,
        AstGrepRefactorService astGrepRefactorService,
        IDoctorApplicationService doctorAppService,
        INotebookApplicationService notebookAppService,
        ISettingsApplicationService settingsAppService,
        IRefactorApplicationService refactorAppService,
        IContextApplicationService contextAppService,
        CleanupService cleanupService,
        ITaskGraphApplicationService taskGraphAppService,
        IMemoryApplicationService memoryAppService,
        IPlanningApplicationService planAppService,
        IConversationApplicationService conversationAppService,
        IToolApplicationService toolAppService,
        LlmPreferenceContext llmPreferenceContext,
        ExecutionContext executionContext,
        RoutineRegistry routineRegistry
    )
    {
        _providers = config.Providers;
        _paths = config.Paths;
        _security = config.Security;
        _context = config.Context;
        _execution = config.Execution;
        _llmRouter = llmRouter;
        _groqModelCatalog = groqModelCatalog;
        _coreClient = coreClient;
        _telegramClient = telegramClient;
        _runtimeSettings = runtimeSettings;
        _providerRegistry = providerRegistry;
        _routingPolicyResolver = routingPolicyResolver;
        _toolRegistry = toolRegistry;
        _searchGateway = searchGateway;
        _searchGuard = searchGuard;
        _searchAnswerComposer = searchAnswerComposer;
        _webFetchTool = webFetchTool;
        _memorySearchTool = memorySearchTool;
        _memoryGetTool = memoryGetTool;
        _sessionListTool = sessionListTool;
        _sessionHistoryTool = sessionHistoryTool;
        _sessionSendTool = sessionSendTool;
        _sessionSpawnTool = sessionSpawnTool;
        _browserTool = browserTool;
        _canvasTool = canvasTool;
        _nodesTool = nodesTool;
        _copilotWrapper = copilotWrapper;
        _codexWrapper = codexWrapper;
        _sandboxClient = sandboxClient;
        _memoryNoteStore = memoryNoteStore;
        _conversationStore = conversationStore;
        _runArtifactStore = runArtifactStore;
        _codeRunner = codeRunner;
        _auditLogger = auditLogger;
        _doctorService = doctorService;
        _planService = planService;
        _planReviewService = planReviewService;
        _taskGraphService = taskGraphService;
        _taskGraphCoordinator = taskGraphCoordinator;
        _projectContextLoader = projectContextLoader;
        _notebookService = notebookService;
        _anchorReadService = anchorReadService;
        _anchorEditService = anchorEditService;
        _diffPreviewService = diffPreviewService;
        _lspRefactorService = lspRefactorService;
        _astGrepRefactorService = astGrepRefactorService;
        _doctorAppService = doctorAppService;
        _notebookAppService = notebookAppService;
        _settingsAppService = settingsAppService;
        _refactorAppService = refactorAppService;
        _contextAppService = contextAppService;
        _cleanupService = cleanupService;
        _taskGraphAppService = taskGraphAppService;
        _memoryAppService = memoryAppService;
        _planAppService = planAppService;
        _conversationAppService = conversationAppService;
        _toolAppService = toolAppService;
        _llmPreferenceContext = llmPreferenceContext;
        _executionContext = executionContext;
        _routineRegistry = routineRegistry;
        _killAllowlist = (_security.KillAllowlistCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        _telegramUpgradeQuotaStatePath = BuildTelegramUpgradeQuotaStatePath();
        var stateBaseDir = Path.GetDirectoryName(_paths.ConversationStatePath);
        if (string.IsNullOrWhiteSpace(stateBaseDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            stateBaseDir = string.IsNullOrWhiteSpace(home) ? Path.GetTempPath() : Path.Combine(home, ".omninode");
        }

        _routineStatePath = _routineRegistry.StorePath;
        _routinePromptDir = _paths.RoutinePromptDir;
        LoadTelegramUpgradeQuotaState();
        EnsureRoutinePromptFiles();
        _routineRegistry.Load();
        RestoreActiveSkillBindingsFromStore();
        _routineSchedulerTask = Task.Run(() => RoutineSchedulerLoopAsync(_routineSchedulerCts.Token));
    }

    private object _telegramLlmLock => _llmPreferenceContext.TelegramLlmLock;
    private object _webLlmLock => _llmPreferenceContext.WebLlmLock;
    private object _telegramCodingLock => _llmPreferenceContext.TelegramCodingLock;
    private object _telegramRefactorLock => _llmPreferenceContext.TelegramRefactorLock;
    private TelegramLlmPreferences _telegramLlmPreferences => _llmPreferenceContext.TelegramLlmPreferences;
    private TelegramCodingPreferences _telegramCodingPreferences => _llmPreferenceContext.TelegramCodingPreferences;
    private TelegramRefactorSession _telegramRefactorSession => _llmPreferenceContext.TelegramRefactorSession;
    private WebLlmPreferences _webLlmPreferences => _llmPreferenceContext.WebLlmPreferences;

    // 시작 시 ConversationStore에 영구 저장된 활성 스킬을 메모리 dictionary로 복원.
    private void RestoreActiveSkillBindingsFromStore()
    {
        try
        {
            var bindings = _conversationStore.ListActiveSkillBindings();
            foreach (var (conversationId, skillName) in bindings)
            {
                if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(skillName))
                {
                    continue;
                }
                _activeSkillByThread[conversationId.Trim().ToLowerInvariant()] = skillName.Trim();
            }
            _auditLogger.Log("local", "skill_active_restore", "ok", $"count={bindings.Count}");
        }
        catch (Exception ex)
        {
            _auditLogger.Log("local", "skill_active_restore", "failed", ex.Message);
        }
    }

    // 활성 스킬 변경/해제를 ConversationStore에도 영구 저장. 키가 비어있으면 no-op.
    private void PersistActiveSkillForThread(string? threadKey, string? skillName)
    {
        if (string.IsNullOrWhiteSpace(threadKey))
        {
            return;
        }
        try
        {
            _conversationStore.SetActiveSkillName(threadKey, skillName);
        }
        catch (Exception ex)
        {
            _auditLogger.Log("local", "skill_active_persist", "failed", ex.Message);
        }
    }

    public bool ClearActiveSkill(string? conversationId) => _conversationAppService.ClearActiveSkill(conversationId);

    public bool ClearActiveSkillForConversation(string? conversationId)
    {
        var trimmed = (conversationId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        try
        {
            var normalized = trimmed.ToLowerInvariant();
            var removed = _activeSkillByThread.TryRemove(trimmed, out _);
            if (!string.Equals(trimmed, normalized, StringComparison.Ordinal))
            {
                removed = _activeSkillByThread.TryRemove(normalized, out _) || removed;
            }

            _conversationStore.SetActiveSkillName(trimmed, null);
            _auditLogger.Log("web", "skill_active_clear", "ok", $"conversation={trimmed} removed={(removed ? "true" : "false")}");
            return true;
        }
        catch (Exception ex)
        {
            _auditLogger.Log("web", "skill_active_clear", "failed", ex.Message);
            return false;
        }
    }

}
