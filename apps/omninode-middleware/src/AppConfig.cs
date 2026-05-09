namespace OmniNode.Middleware;

public sealed class AppConfig
{
    private const string DefaultKeychainAccount = "omninode";
    private const string TelegramBotTokenService = "omninode_telegram_bot_token";
    private const string TelegramChatIdService = "omninode_telegram_chat_id";
    private const string GroqApiKeyService = "omninode_groq_api_key";
    private const string GeminiApiKeyService = "omninode_gemini_api_key";
    private const string CerebrasApiKeyService = "omninode_cerebras_api_key";
    private const string NvidiaApiKeyService = "omninode_nvidia_api_key";
    private const string CodexApiKeyService = "omninode_codex_api_key";
    private const string SttApiKeyService = "omninode_stt_api_key";

    public int WebSocketPort { get; init; } = 8080;
    public string CoreSocketPath { get; init; } = ResolveDefaultCoreSocketPath();
    public string? TelegramBotToken { get; init; }
    public string? TelegramChatId { get; init; }
    public string? TelegramAllowedUserId { get; init; }
    public string CopilotCliBinary { get; init; } = "gh";
    public string CopilotDirectBinary { get; init; } = "copilot";
    public string CopilotModel { get; init; } = "gpt-5-mini";
    public string CodexBinary { get; init; } = "codex";
    public string CodexModel { get; init; } = "gpt-5.4";
    public string PythonBinary { get; init; } = ResolveDefaultPythonBinary();
    public string SandboxExecutorPath { get; init; } = ResolveDefaultSandboxExecutorPath();
    public string DashboardIndexPath { get; init; } = ResolveDefaultDashboardIndexPath();
    public bool EnableDynamicCode { get; init; }
    public string? GroqApiKey { get; init; }
    public string GroqBaseUrl { get; init; } = "https://api.groq.com/openai/v1";
    public string GroqModel { get; init; } = "meta-llama/llama-4-scout-17b-16e-instruct";
    public string? GeminiApiKey { get; init; }
    public string GeminiBaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta";
    public string GeminiModel { get; init; } = "gemini-3-flash-preview";
    public string GeminiSearchModel { get; init; } = "gemini-3.1-flash-lite-preview";
    public string CerebrasBaseUrl { get; init; } = "https://api.cerebras.ai/v1";
    public string CerebrasModel { get; init; } = "gpt-oss-120b";
    public int CerebrasTimeoutSec { get; init; } = 40;
    public string CerebrasKeychainService { get; init; } = CerebrasApiKeyService;
    public string CerebrasKeychainAccount { get; init; } = DefaultKeychainAccount;
    public string? CerebrasApiKey { get; init; }
    public string NvidiaBaseUrl { get; init; } = "https://integrate.api.nvidia.com/v1";
    public string NvidiaModel { get; init; } = "meta/llama-3.1-70b-instruct";
    public int NvidiaTimeoutSec { get; init; } = 180;
    public string NvidiaKeychainService { get; init; } = NvidiaApiKeyService;
    public string NvidiaKeychainAccount { get; init; } = DefaultKeychainAccount;
    public string? NvidiaApiKey { get; init; }
    public string? CodexApiKey { get; init; }
    public string SttProvider { get; init; } = string.Empty;
    public string SttBaseUrl { get; init; } = string.Empty;
    public string SttModel { get; init; } = string.Empty;
    public string? SttApiKey { get; init; }
    public decimal GeminiInputPricePerMillionUsd { get; init; } = 0.50m;
    public decimal GeminiOutputPricePerMillionUsd { get; init; } = 3.00m;
    public string LlmUsageStatePath { get; init; } = ResolveDefaultStateFilePath("llm_usage.json");
    public string CopilotUsageStatePath { get; init; } = ResolveDefaultStateFilePath("copilot_usage.json");
    public string ConversationStatePath { get; init; } = ResolveDefaultStateFilePath("conversations.json");
    public string AuthSessionStatePath { get; init; } = ResolveDefaultStateFilePath("auth_sessions.json");
    public string MemoryNotesRootDir { get; init; } = ResolveDefaultStateDirectoryPath("memory-notes");
    public int ConversationCompressChars { get; init; } = 12000;
    public int ConversationKeepRecentMessages { get; init; } = 16;
    public int ConversationHistoryMessages { get; init; } = 18;
    public string CodeRunsRootDir { get; init; } = ResolveDefaultStateDirectoryPath("code-runs");
    public string RoutineRunsRootDir { get; init; } = Path.Combine(ResolveDefaultWorkspaceRootDir(), "routines");
    public int CodeExecutionTimeoutSec { get; init; } = 120;
    public string WorkspaceRootDir { get; init; } = ResolveDefaultWorkspaceRootDir();
    public string RoutineStatePath { get; init; } = ResolveDefaultStateFilePath("routines.json");
    public string RoutinePromptDir { get; init; } = Path.Combine(ResolveDefaultWorkspaceRootDir(), "_routine_prompts");
    public int CodingAgentMaxIterations { get; init; } = 6;
    public int CodingAgentMaxActionsPerIteration { get; init; } = 8;
    public int CodingCopilotMaxActionsPerIteration { get; init; } = 2;
    public int CodingWorkspaceSnapshotMaxEntries { get; init; } = 80;
    public int CodingRecentLoopHistoryForCopilot { get; init; } = 2;
    public bool CodingEnableOneShotUiClone { get; init; } = true;
    public int ChatMaxOutputTokens { get; init; } = 8192;
    public int CodingMaxOutputTokens { get; init; } = 16384;
    public int LlmTimeoutSec { get; init; } = 20;
    public bool EnableFastWebPipeline { get; init; } = true;
    public int WebDecisionTimeoutMs { get; init; } = 700;
    public int GeminiWebTimeoutMs { get; init; } = 30000;
    public int WebDefaultNewsCount { get; init; } = 10;
    public int WebDefaultListCount { get; init; } = 5;
    public int WebSocketMaxMessageBytes { get; init; } = 157286400;
    public int WebSocketCommandsPerMinute { get; init; } = 30;
    public int MetricsPushIntervalSec { get; init; } = 2;
    public int CommandMaxLength { get; init; } = 800;
    public string AuditLogPath { get; init; } = ResolveDefaultStateFilePath("audit.log");
    public string GuardAlertWebhookUrl { get; init; } = string.Empty;
    public string GuardAlertLogCollectorUrl { get; init; } = string.Empty;
    public int GuardAlertDispatchTimeoutMs { get; init; } = 3500;
    public int GuardAlertDispatchMaxAttempts { get; init; } = 2;
    public string GuardRetryTimelineStatePath { get; init; } = ResolveDefaultStateFilePath("guard_retry_timeline.json");
    public string GatewayHealthStatePath { get; init; } = ResolveDefaultStateFilePath("gateway_health.json");
    public string GatewayStartupProbeStatePath { get; init; } = ResolveDefaultStateFilePath("gateway_startup_probe.json");
    public string DashboardAccessStatePath { get; init; } = ResolveDefaultStateFilePath("dashboard_access.json");
    public bool ExternalDashboardEnabled { get; init; }
    public bool EnableHealthEndpoint { get; init; } = true;
    public bool EnableGatewayStartupProbe { get; init; }
    public int GatewayStartupProbeDelayMs { get; init; } = 250;
    public int GatewayStartupProbeTimeoutSec { get; init; } = 8;
    public int GatewayStartupProbePollIntervalMs { get; init; } = 150;
    public string GatewayStartupProbeMode { get; init; } = "live";
    public bool EnableLocalOtpFallback { get; init; } = true;
    public string KillAllowlistCsv { get; init; } = string.Empty;
    public int DoctorTimeoutSeconds { get; init; } = 15;
    public bool DoctorEnableSandboxSmoke { get; init; } = true;
    public bool DoctorWriteHistory { get; init; } = true;
    public bool RefactorEnableLsp { get; init; }
    public bool RefactorEnableAstGrep { get; init; }
    public int RefactorPreviewTtlMinutes { get; init; } = 120;
    public string ProjectContextFallbackFilenamesCsv { get; init; } = "TEAM_GUIDE.md,.agents.md";
    public int ProjectContextMaxBytes { get; init; } = 65536;

    public static AppConfig LoadFromEnvironment()
    {
        var pathResolver = DefaultStatePathResolver.CreateDefault();
        return new AppConfig
        {
            WebSocketPort = GetIntEnv("OMNINODE_WS_PORT", 8080),
            CoreSocketPath = GetStringEnv("OMNINODE_CORE_SOCKET_PATH", pathResolver.CoreSocketPath),
            TelegramBotToken = SecretLoader.ResolveApiKey(
                providerName: "telegram_bot_token",
                directEnvKey: "OMNINODE_TELEGRAM_BOT_TOKEN",
                fileEnvKey: "OMNINODE_TELEGRAM_BOT_TOKEN_FILE",
                keychainServiceEnvKey: "OMNINODE_TELEGRAM_TOKEN_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_TELEGRAM_TOKEN_KEYCHAIN_ACCOUNT",
                defaultKeychainService: TelegramBotTokenService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            TelegramChatId = SecretLoader.ResolveApiKey(
                providerName: "telegram_chat_id",
                directEnvKey: "OMNINODE_TELEGRAM_CHAT_ID",
                fileEnvKey: "OMNINODE_TELEGRAM_CHAT_ID_FILE",
                keychainServiceEnvKey: "OMNINODE_TELEGRAM_CHAT_ID_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_TELEGRAM_CHAT_ID_KEYCHAIN_ACCOUNT",
                defaultKeychainService: TelegramChatIdService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            TelegramAllowedUserId = GetStringEnv("OMNINODE_TELEGRAM_ALLOWED_USER_ID", string.Empty),
            CopilotCliBinary = GetStringEnv("OMNINODE_COPILOT_BIN", "gh"),
            CopilotDirectBinary = GetStringEnv("OMNINODE_COPILOT_DIRECT_BIN", "copilot"),
            CopilotModel = GetStringEnv("OMNINODE_COPILOT_MODEL", "gpt-5-mini"),
            CodexBinary = GetStringEnv("OMNINODE_CODEX_BIN", "codex"),
            CodexModel = GetStringEnv("OMNINODE_CODEX_MODEL", "gpt-5.4"),
            PythonBinary = GetStringEnv("OMNINODE_PYTHON_BIN", ResolveDefaultPythonBinary()),
            SandboxExecutorPath = GetStringEnv("OMNINODE_SANDBOX_EXECUTOR", ResolveDefaultSandboxExecutorPath()),
            DashboardIndexPath = GetStringEnv("OMNINODE_DASHBOARD_INDEX", pathResolver.DashboardIndexPath),
            EnableDynamicCode = GetBoolEnv("OMNINODE_ENABLE_DYNAMIC_CODE", false),
            GroqApiKey = SecretLoader.ResolveApiKey(
                providerName: "groq",
                directEnvKey: "OMNINODE_GROQ_API_KEY",
                fileEnvKey: "OMNINODE_GROQ_API_KEY_FILE",
                keychainServiceEnvKey: "OMNINODE_GROQ_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_GROQ_KEYCHAIN_ACCOUNT",
                defaultKeychainService: GroqApiKeyService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            GroqBaseUrl = GetStringEnv("OMNINODE_GROQ_BASE_URL", "https://api.groq.com/openai/v1"),
            GroqModel = GetStringEnv("OMNINODE_GROQ_MODEL", "meta-llama/llama-4-scout-17b-16e-instruct"),
            GeminiApiKey = SecretLoader.ResolveApiKey(
                providerName: "gemini",
                directEnvKey: "OMNINODE_GEMINI_API_KEY",
                fileEnvKey: "OMNINODE_GEMINI_API_KEY_FILE",
                keychainServiceEnvKey: "OMNINODE_GEMINI_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_GEMINI_KEYCHAIN_ACCOUNT",
                defaultKeychainService: GeminiApiKeyService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            GeminiBaseUrl = GetStringEnv("OMNINODE_GEMINI_BASE_URL", "https://generativelanguage.googleapis.com/v1beta"),
            GeminiModel = GetStringEnv("OMNINODE_GEMINI_MODEL", "gemini-3-flash-preview"),
            GeminiSearchModel = GetStringEnv("OMNINODE_GEMINI_SEARCH_MODEL", "gemini-3.1-flash-lite-preview"),
            CerebrasBaseUrl = GetStringEnv("OMNINODE_CEREBRAS_BASE_URL", "https://api.cerebras.ai/v1"),
            CerebrasModel = GetStringEnv("OMNINODE_CEREBRAS_MODEL", "gpt-oss-120b"),
            CerebrasTimeoutSec = GetIntEnv("OMNINODE_CEREBRAS_TIMEOUT_SEC", 40),
            CerebrasKeychainService = GetStringEnv("OMNINODE_CEREBRAS_KEYCHAIN_SERVICE", CerebrasApiKeyService),
            CerebrasKeychainAccount = GetStringEnv("OMNINODE_CEREBRAS_KEYCHAIN_ACCOUNT", DefaultKeychainAccount),
            CerebrasApiKey = SecretLoader.ResolveApiKey(
                providerName: "cerebras",
                directEnvKey: "OMNINODE_CEREBRAS_API_KEY",
                fileEnvKey: "OMNINODE_CEREBRAS_API_KEY_FILE",
                keychainServiceEnvKey: "OMNINODE_CEREBRAS_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_CEREBRAS_KEYCHAIN_ACCOUNT",
                defaultKeychainService: CerebrasApiKeyService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            NvidiaBaseUrl = GetStringEnv("OMNINODE_NVIDIA_BASE_URL", "https://integrate.api.nvidia.com/v1"),
            NvidiaModel = GetStringEnv("OMNINODE_NVIDIA_MODEL", "meta/llama-3.1-70b-instruct"),
            NvidiaTimeoutSec = GetIntEnv("OMNINODE_NVIDIA_TIMEOUT_SEC", 180),
            NvidiaKeychainService = GetStringEnv("OMNINODE_NVIDIA_KEYCHAIN_SERVICE", NvidiaApiKeyService),
            NvidiaKeychainAccount = GetStringEnv("OMNINODE_NVIDIA_KEYCHAIN_ACCOUNT", DefaultKeychainAccount),
            NvidiaApiKey = SecretLoader.ResolveApiKey(
                providerName: "nvidia",
                directEnvKey: "OMNINODE_NVIDIA_API_KEY",
                fileEnvKey: "OMNINODE_NVIDIA_API_KEY_FILE",
                keychainServiceEnvKey: "OMNINODE_NVIDIA_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_NVIDIA_KEYCHAIN_ACCOUNT",
                defaultKeychainService: NvidiaApiKeyService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            CodexApiKey = SecretLoader.ResolveApiKey(
                providerName: "codex",
                directEnvKey: "OMNINODE_CODEX_API_KEY",
                fileEnvKey: "OMNINODE_CODEX_API_KEY_FILE",
                keychainServiceEnvKey: "OMNINODE_CODEX_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_CODEX_KEYCHAIN_ACCOUNT",
                defaultKeychainService: CodexApiKeyService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            SttProvider = GetStringEnv("OMNINODE_STT_PROVIDER", string.Empty),
            SttBaseUrl = GetStringEnv("OMNINODE_STT_BASE_URL", string.Empty),
            SttModel = GetStringEnv("OMNINODE_STT_MODEL", string.Empty),
            SttApiKey = SecretLoader.ResolveApiKey(
                providerName: "stt",
                directEnvKey: "OMNINODE_STT_API_KEY",
                fileEnvKey: "OMNINODE_STT_API_KEY_FILE",
                keychainServiceEnvKey: "OMNINODE_STT_KEYCHAIN_SERVICE",
                keychainAccountEnvKey: "OMNINODE_STT_KEYCHAIN_ACCOUNT",
                defaultKeychainService: SttApiKeyService,
                defaultKeychainAccount: DefaultKeychainAccount
            ),
            GeminiInputPricePerMillionUsd = GetDecimalEnv("OMNINODE_GEMINI_INPUT_PRICE_PER_MILLION_USD", 0.50m),
            GeminiOutputPricePerMillionUsd = GetDecimalEnv("OMNINODE_GEMINI_OUTPUT_PRICE_PER_MILLION_USD", 3.00m),
            LlmUsageStatePath = GetStringEnv("OMNINODE_LLM_USAGE_STATE_PATH", pathResolver.ResolveStateFilePath("llm_usage.json")),
            CopilotUsageStatePath = GetStringEnv("OMNINODE_COPILOT_USAGE_STATE_PATH", pathResolver.ResolveStateFilePath("copilot_usage.json")),
            ConversationStatePath = GetStringEnv("OMNINODE_CONVERSATION_STATE_PATH", pathResolver.ResolveStateFilePath("conversations.json")),
            AuthSessionStatePath = GetStringEnv("OMNINODE_AUTH_SESSION_STATE_PATH", pathResolver.ResolveStateFilePath("auth_sessions.json")),
            MemoryNotesRootDir = GetStringEnv("OMNINODE_MEMORY_NOTES_DIR", pathResolver.ResolveStateDirectoryPath("memory-notes")),
            ConversationCompressChars = GetIntEnv("OMNINODE_CONVERSATION_COMPRESS_CHARS", 12000),
            ConversationKeepRecentMessages = GetIntEnv("OMNINODE_CONVERSATION_KEEP_RECENT_MESSAGES", 16),
            ConversationHistoryMessages = GetIntEnv("OMNINODE_CONVERSATION_HISTORY_MESSAGES", 18),
            CodeRunsRootDir = GetStringEnv("OMNINODE_CODE_RUNS_DIR", pathResolver.ResolveStateDirectoryPath("code-runs")),
            RoutineRunsRootDir = GetStringEnv("OMNINODE_ROUTINE_RUNS_DIR", Path.Combine(pathResolver.WorkspaceRootDir, "routines")),
            CodeExecutionTimeoutSec = GetIntEnv("OMNINODE_CODE_EXEC_TIMEOUT_SEC", 120),
            WorkspaceRootDir = GetStringEnv("OMNINODE_WORKSPACE_ROOT", pathResolver.WorkspaceRootDir),
            RoutineStatePath = GetStringEnv("OMNINODE_ROUTINE_STATE_PATH", pathResolver.ResolveStateFilePath("routines.json")),
            RoutinePromptDir = GetStringEnv("OMNINODE_ROUTINE_PROMPT_DIR", pathResolver.RoutinePromptDir),
            CodingAgentMaxIterations = GetIntEnv("OMNINODE_CODING_AGENT_MAX_ITERATIONS", 6),
            CodingAgentMaxActionsPerIteration = GetIntEnv("OMNINODE_CODING_AGENT_MAX_ACTIONS", 8),
            CodingCopilotMaxActionsPerIteration = GetIntEnv("OMNINODE_CODING_COPILOT_MAX_ACTIONS", 2),
            CodingWorkspaceSnapshotMaxEntries = GetIntEnv("OMNINODE_CODING_SNAPSHOT_MAX_ENTRIES", 80),
            CodingRecentLoopHistoryForCopilot = GetIntEnv("OMNINODE_CODING_COPILOT_HISTORY", 2),
            CodingEnableOneShotUiClone = GetBoolEnv("OMNINODE_CODING_ENABLE_ONESHOT_UI_CLONE", true),
            ChatMaxOutputTokens = GetIntEnv("OMNINODE_CHAT_MAX_OUTPUT_TOKENS", 8192),
            CodingMaxOutputTokens = GetIntEnv("OMNINODE_CODING_MAX_OUTPUT_TOKENS", 16384),
            LlmTimeoutSec = GetIntEnv("OMNINODE_LLM_TIMEOUT_SEC", 20),
            EnableFastWebPipeline = GetBoolEnv("OMNINODE_FAST_WEB_PIPELINE", true),
            WebDecisionTimeoutMs = Math.Clamp(GetIntEnv("OMNINODE_WEB_DECISION_TIMEOUT_MS", 700), 200, 5000),
            GeminiWebTimeoutMs = Math.Clamp(GetIntEnv("OMNINODE_GEMINI_WEB_TIMEOUT_MS", 30000), 5000, 60000),
            WebDefaultNewsCount = Math.Clamp(GetIntEnv("OMNINODE_WEB_DEFAULT_NEWS_COUNT", 10), 1, 20),
            WebDefaultListCount = Math.Clamp(GetIntEnv("OMNINODE_WEB_DEFAULT_LIST_COUNT", 5), 1, 20),
            WebSocketMaxMessageBytes = GetIntEnv("OMNINODE_WS_MAX_MESSAGE_BYTES", 157286400),
            WebSocketCommandsPerMinute = GetIntEnv("OMNINODE_WS_COMMANDS_PER_MINUTE", 30),
            MetricsPushIntervalSec = GetIntEnv("OMNINODE_METRICS_PUSH_INTERVAL_SEC", 2),
            CommandMaxLength = GetIntEnv("OMNINODE_COMMAND_MAX_LENGTH", 800),
            AuditLogPath = GetStringEnv("OMNINODE_AUDIT_LOG_PATH", pathResolver.ResolveStateFilePath("audit.log")),
            GuardAlertWebhookUrl = GetStringEnv("OMNINODE_GUARD_ALERT_WEBHOOK_URL", string.Empty),
            GuardAlertLogCollectorUrl = GetStringEnv("OMNINODE_GUARD_ALERT_LOG_COLLECTOR_URL", string.Empty),
            GuardAlertDispatchTimeoutMs = Math.Clamp(GetIntEnv("OMNINODE_GUARD_ALERT_DISPATCH_TIMEOUT_MS", 3500), 500, 120000),
            GuardAlertDispatchMaxAttempts = Math.Clamp(GetIntEnv("OMNINODE_GUARD_ALERT_DISPATCH_MAX_ATTEMPTS", 2), 1, 5),
            GuardRetryTimelineStatePath = GetStringEnv("OMNINODE_GUARD_RETRY_TIMELINE_STATE_PATH", pathResolver.ResolveStateFilePath("guard_retry_timeline.json")),
            GatewayHealthStatePath = GetStringEnv("OMNINODE_GATEWAY_HEALTH_STATE_PATH", pathResolver.ResolveStateFilePath("gateway_health.json")),
            GatewayStartupProbeStatePath = GetStringEnv("OMNINODE_GATEWAY_STARTUP_PROBE_STATE_PATH", pathResolver.ResolveStateFilePath("gateway_startup_probe.json")),
            DashboardAccessStatePath = GetStringEnv("OMNINODE_DASHBOARD_ACCESS_STATE_PATH", pathResolver.ResolveStateFilePath("dashboard_access.json")),
            ExternalDashboardEnabled = GetBoolEnv("OMNINODE_EXTERNAL_DASHBOARD", false),
            EnableHealthEndpoint = GetBoolEnv("OMNINODE_ENABLE_HEALTH_ENDPOINT", true),
            EnableGatewayStartupProbe = GetBoolEnv("OMNINODE_GATEWAY_STARTUP_PROBE", false),
            GatewayStartupProbeDelayMs = Math.Max(0, GetIntEnv("OMNINODE_GATEWAY_STARTUP_PROBE_DELAY_MS", 250)),
            GatewayStartupProbeTimeoutSec = Math.Max(3, GetIntEnv("OMNINODE_GATEWAY_STARTUP_PROBE_TIMEOUT_SEC", 8)),
            GatewayStartupProbePollIntervalMs = Math.Max(50, GetIntEnv("OMNINODE_GATEWAY_STARTUP_PROBE_POLL_INTERVAL_MS", 150)),
            GatewayStartupProbeMode = GetStringEnv("OMNINODE_GATEWAY_STARTUP_PROBE_MODE", "live"),
            EnableLocalOtpFallback = GetBoolEnv("OMNINODE_ENABLE_LOCAL_OTP_FALLBACK", true),
            KillAllowlistCsv = GetStringEnv("OMNINODE_KILL_ALLOWLIST", string.Empty),
            DoctorTimeoutSeconds = Math.Max(3, GetIntEnv("OMNINODE_DOCTOR_TIMEOUT_SECONDS", 15)),
            DoctorEnableSandboxSmoke = GetBoolEnv("OMNINODE_DOCTOR_ENABLE_SANDBOX_SMOKE", true),
            DoctorWriteHistory = GetBoolEnv("OMNINODE_DOCTOR_WRITE_HISTORY", true),
            RefactorEnableLsp = GetBoolEnv("OMNINODE_REFACTOR_ENABLE_LSP", false),
            RefactorEnableAstGrep = GetBoolEnv("OMNINODE_REFACTOR_ENABLE_AST_GREP", false),
            RefactorPreviewTtlMinutes = Math.Clamp(GetIntEnv("OMNINODE_REFACTOR_PREVIEW_TTL_MINUTES", 120), 5, 1440),
            ProjectContextFallbackFilenamesCsv = GetStringEnv("OMNINODE_PROJECT_CONTEXT_FALLBACK_FILENAMES", "TEAM_GUIDE.md,.agents.md"),
            ProjectContextMaxBytes = Math.Clamp(GetIntEnv("OMNINODE_PROJECT_CONTEXT_MAX_BYTES", 65536), 4096, 262144)
        };
    }

    private static int GetIntEnv(string key, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static decimal GetDecimalEnv(string key, decimal defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return decimal.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string GetStringEnv(string key, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static bool GetBoolEnv(string key, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDefaultPythonBinary()
    {
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private static string ResolveDefaultStateFilePath(string fileName)
    {
        return DefaultStatePathResolver.CreateDefault().ResolveStateFilePath(fileName);
    }

    private static string ResolveDefaultStateDirectoryPath(string directoryName)
    {
        return DefaultStatePathResolver.CreateDefault().ResolveStateDirectoryPath(directoryName);
    }

    private static string ResolveDefaultDashboardIndexPath()
    {
        return DefaultStatePathResolver.CreateDefault().DashboardIndexPath;
    }

    private static string ResolveDefaultCoreSocketPath()
    {
        return DefaultStatePathResolver.CreateDefault().CoreSocketPath;
    }

    private static string ResolveDefaultSandboxExecutorPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "../../../../omninode-sandbox/executor.py")),
            Path.GetFullPath(Path.Combine(cwd, "apps/omninode-sandbox/executor.py")),
            Path.GetFullPath(Path.Combine(cwd, "omninode-sandbox/executor.py")),
            Path.GetFullPath(Path.Combine(cwd, "../omninode-sandbox/executor.py")),
            Path.GetFullPath(Path.Combine(cwd, "../apps/omninode-sandbox/executor.py"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static string ResolveDefaultWorkspaceRootDir()
    {
        return DefaultStatePathResolver.CreateDefault().WorkspaceRootDir;
    }
}
