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
    private const int TelegramMaxResponseChars = 3600;
    private const int TelegramFastModeMaxOutputTokens = 1024;
    private const int TelegramComplexModeMaxOutputTokens = 1536;
    private const int TelegramLongContextThresholdChars = 2800;
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
    private readonly AppConfig _config;
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
        _skillCreateDirective ??= new SkillCreateDirective(_config);
    private readonly ConcurrentDictionary<string, string> _activeSkillByThread = new(StringComparer.Ordinal);
    private readonly CopilotCliWrapper _copilotWrapper;
    private readonly CodexCliWrapper _codexWrapper;
    private readonly PythonSandboxClient _sandboxClient;
    private readonly IMemoryNoteStore _memoryNoteStore;
    private readonly IConversationStore _conversationStore;
    private readonly IRoutineStore _routineStore;
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
    private readonly object _telegramLlmLock = new();
    private readonly object _webLlmLock = new();
    private readonly object _telegramCodingLock = new();
    private readonly object _telegramRefactorLock = new();
    private readonly object _telegramUpgradeQuotaLock = new();
    private readonly object _routineLock = new();
    private readonly object _planLock = new();
    private readonly AsyncLocal<TelegramExecutionMetadata?> _telegramExecutionMetadata = new();
    private readonly string _telegramUpgradeQuotaStatePath;
    private readonly string _routineStatePath;
    private readonly string _routinePromptDir;
    private readonly string[] _killAllowlist;
    private readonly Dictionary<string, RoutineDefinition> _routinesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _routineSchedulerCts = new();
    private Task? _routineSchedulerTask;
    private string _telegramUpgradeQuotaDay = string.Empty;
    private int _telegramUpgradeQuotaCount;
    private TelegramLlmPreferences _telegramLlmPreferences;
    private TelegramCodingPreferences _telegramCodingPreferences;
    private TelegramRefactorSession _telegramRefactorSession;
    private WebLlmPreferences _webLlmPreferences;

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
        IRoutineStore routineStore,
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
        AstGrepRefactorService astGrepRefactorService
    )
    {
        _config = config;
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
        _routineStore = routineStore;
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
        _killAllowlist = (_config.KillAllowlistCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        _telegramUpgradeQuotaStatePath = BuildTelegramUpgradeQuotaStatePath();
        var stateBaseDir = Path.GetDirectoryName(_config.ConversationStatePath);
        if (string.IsNullOrWhiteSpace(stateBaseDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            stateBaseDir = string.IsNullOrWhiteSpace(home) ? Path.GetTempPath() : Path.Combine(home, ".omninode");
        }

        _routineStatePath = _routineStore.StorePath;
        _routinePromptDir = _config.RoutinePromptDir;
        LoadTelegramUpgradeQuotaState();
        _telegramLlmPreferences = new TelegramLlmPreferences
        {
            Profile = "default",
            Mode = "single",
            SingleProvider = "groq",
            SingleModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
            AutoGroqComplexUpgrade = true,
            OrchestrationProvider = "auto",
            OrchestrationModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
            MultiGroqModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
            MultiGeminiModel = _config.GeminiModel,
            MultiCopilotModel = string.IsNullOrWhiteSpace(_copilotWrapper.GetSelectedModel()) ? DefaultCopilotModel : _copilotWrapper.GetSelectedModel(),
            MultiCerebrasModel = _config.CerebrasModel,
            MultiCodexModel = _config.CodexModel,
            MultiSummaryProvider = "auto",
            TalkThinkingLevel = "low",
            CodeThinkingLevel = "high"
        };
        _telegramCodingPreferences = new TelegramCodingPreferences
        {
            Mode = "orchestration",
            SingleProvider = "copilot",
            SingleModel = string.IsNullOrWhiteSpace(_copilotWrapper.GetSelectedModel()) ? DefaultCopilotModel : _copilotWrapper.GetSelectedModel(),
            SingleLanguage = "auto",
            OrchestrationProvider = "auto",
            OrchestrationModel = string.Empty,
            OrchestrationLanguage = "auto",
            OrchestrationGroqModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
            OrchestrationGeminiModel = _config.GeminiModel,
            OrchestrationCerebrasModel = _config.CerebrasModel,
            OrchestrationCopilotModel = "none",
            OrchestrationCodexModel = "none",
            MultiProvider = "gemini",
            MultiModel = _config.GeminiModel,
            MultiLanguage = "auto",
            MultiGroqModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
            MultiGeminiModel = _config.GeminiModel,
            MultiCerebrasModel = _config.CerebrasModel,
            MultiCopilotModel = "none",
            MultiCodexModel = "none"
        };
        _telegramRefactorSession = new TelegramRefactorSession();
        _webLlmPreferences = new WebLlmPreferences
        {
            Profile = "default",
            Mode = "single",
            SingleProvider = "groq",
            SingleModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
            AutoGroqComplexUpgrade = true,
            OrchestrationProvider = "auto",
            OrchestrationModel = string.IsNullOrWhiteSpace(_config.GeminiModel) ? _config.GroqModel : _config.GeminiModel,
            MultiGroqModel = string.IsNullOrWhiteSpace(_config.GroqModel) ? DefaultGroqPrimaryModel : _config.GroqModel,
            MultiGeminiModel = _config.GeminiModel,
            MultiCopilotModel = string.IsNullOrWhiteSpace(_copilotWrapper.GetSelectedModel()) ? DefaultCopilotModel : _copilotWrapper.GetSelectedModel(),
            MultiCerebrasModel = _config.CerebrasModel,
            MultiCodexModel = _config.CodexModel,
            MultiSummaryProvider = "auto",
            TalkThinkingLevel = "low",
            CodeThinkingLevel = "high"
        };
        EnsureRoutinePromptFiles();
        LoadRoutineState();
        _routineSchedulerTask = Task.Run(() => RoutineSchedulerLoopAsync(_routineSchedulerCts.Token));
    }

}
