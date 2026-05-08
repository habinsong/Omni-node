using System.Net;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private sealed record CodingExecutionProfile(
        string Provider,
        string Model,
        bool UseCompactLoopPrompt,
        bool OptimizeCodexCli,
        bool AllowUiOneShot,
        bool AllowDeterministicStdoutFastPath,
        bool PreferBundleFallback,
        bool PreferDirectRecovery,
        bool EnableGameScaffoldFallback,
        int RequestTimeoutSeconds,
        int MaxIterations,
        int LoopMaxActions,
        int OneShotMaxActions,
        int PlanMaxOutputTokens,
        int WorkspaceSnapshotMaxEntries,
        int RecentLoopHistory
    );

    private CodingExecutionProfile ResolveCodingExecutionProfile(
        string provider,
        string model,
        string objective,
        string languageHint,
        IReadOnlyList<string> requestedPaths
    )
    {
        var normalizedProvider = NormalizeProvider(provider, allowAuto: false);
        var normalizedModel = NormalizePinnedProviderModelSelection(normalizedProvider, model) ?? ResolveProviderModel(normalizedProvider, model);
        var frontendLike = IsFrontendLikeCodingTask(objective, languageHint);
        var gameLike = IsGameLikeCodingTask(objective, languageHint);
        var multiFileLike = requestedPaths.Count > 1 || frontendLike || gameLike;
        var flashLike = normalizedModel.Contains("flash", StringComparison.OrdinalIgnoreCase)
            || normalizedModel.Contains("lite", StringComparison.OrdinalIgnoreCase);
        var groqCompoundLike = IsGroqCompoundLikeCodingModel(normalizedModel);
        var reasoningLikeModel = normalizedModel.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase);
        var codexLikeModel = normalizedModel.Contains("codex", StringComparison.OrdinalIgnoreCase)
            || normalizedModel.Contains("gpt-5", StringComparison.OrdinalIgnoreCase);
        var baseIterations = Math.Max(2, _config.CodingAgentMaxIterations);
        var baseActions = Math.Max(1, _config.CodingAgentMaxActionsPerIteration);
        var copilotActions = Math.Max(1, _config.CodingCopilotMaxActionsPerIteration);
        var defaultSnapshotEntries = Math.Max(20, _config.CodingWorkspaceSnapshotMaxEntries);
        var defaultRecentLoopHistory = 4;
        var copilotMini = IsPinnedCopilotModel(normalizedProvider, normalizedModel);
        var defaultPlanTokens = normalizedProvider switch
        {
            "groq" or "gemini" or "cerebras" or "nvidia" => Math.Clamp(_config.CodingMaxOutputTokens, 1200, 3200),
            _ => Math.Clamp(_config.CodingMaxOutputTokens, 1200, 2400)
        };

        var baseProfile = normalizedProvider switch
        {
            "groq" => new CodingExecutionProfile(
                normalizedProvider,
                normalizedModel,
                UseCompactLoopPrompt: groqCompoundLike || reasoningLikeModel,
                OptimizeCodexCli: false,
                AllowUiOneShot: false,
                AllowDeterministicStdoutFastPath: true,
                PreferBundleFallback: multiFileLike,
                PreferDirectRecovery: false,
                EnableGameScaffoldFallback: false,
                RequestTimeoutSeconds: groqCompoundLike ? 35 : 40,
                MaxIterations: groqCompoundLike ? Math.Min(baseIterations, 2) : Math.Min(baseIterations, 3),
                LoopMaxActions: groqCompoundLike ? Math.Min(baseActions, 2) : Math.Min(baseActions, 3),
                OneShotMaxActions: groqCompoundLike ? 2 : Math.Max(3, Math.Min(baseActions, 4)),
                PlanMaxOutputTokens: groqCompoundLike
                    ? Math.Clamp(_config.CodingMaxOutputTokens, 900, 1400)
                    : reasoningLikeModel
                        ? Math.Clamp(_config.CodingMaxOutputTokens, 1000, 1800)
                        : Math.Clamp(_config.CodingMaxOutputTokens, 1200, 2200),
                WorkspaceSnapshotMaxEntries: groqCompoundLike ? 18 : Math.Min(defaultSnapshotEntries, 28),
                RecentLoopHistory: groqCompoundLike ? 2 : 3
            ),
            "codex" => new CodingExecutionProfile(
                normalizedProvider,
                normalizedModel,
                UseCompactLoopPrompt: true,
                OptimizeCodexCli: true,
                AllowUiOneShot: true,
                AllowDeterministicStdoutFastPath: true,
                PreferBundleFallback: multiFileLike,
                PreferDirectRecovery: gameLike && multiFileLike,
                EnableGameScaffoldFallback: true,
                RequestTimeoutSeconds: 120,
                MaxIterations: Math.Clamp(frontendLike || gameLike ? 2 : 3, 1, baseIterations),
                LoopMaxActions: Math.Min(baseActions, 4),
                OneShotMaxActions: Math.Min(Math.Max(4, baseActions), 5),
                PlanMaxOutputTokens: Math.Clamp(_config.CodingMaxOutputTokens, 1200, 1800),
                WorkspaceSnapshotMaxEntries: 24,
                RecentLoopHistory: 2
            ),
            "copilot" => new CodingExecutionProfile(
                normalizedProvider,
                normalizedModel,
                UseCompactLoopPrompt: true,
                OptimizeCodexCli: false,
                AllowUiOneShot: !copilotMini && (frontendLike || gameLike),
                AllowDeterministicStdoutFastPath: true,
                PreferBundleFallback: multiFileLike,
                PreferDirectRecovery: !copilotMini && (frontendLike || gameLike),
                EnableGameScaffoldFallback: false,
                RequestTimeoutSeconds: copilotMini ? 60 : 75,
                MaxIterations: Math.Clamp(frontendLike || gameLike ? 2 : 3, 1, baseIterations),
                LoopMaxActions: copilotMini
                    ? Math.Min(Math.Max(1, copilotActions), 2)
                    : Math.Min(baseActions, copilotActions),
                OneShotMaxActions: copilotMini
                    ? 2
                    : Math.Min(Math.Max(4, baseActions), Math.Max(4, copilotActions)),
                PlanMaxOutputTokens: copilotMini
                    ? Math.Clamp(_config.CodingMaxOutputTokens, 1000, 1500)
                    : Math.Clamp(_config.CodingMaxOutputTokens, 1200, 2200),
                WorkspaceSnapshotMaxEntries: copilotMini
                    ? Math.Min(defaultSnapshotEntries, 24)
                    : Math.Min(defaultSnapshotEntries, 48),
                RecentLoopHistory: copilotMini
                    ? 1
                    : Math.Max(1, _config.CodingRecentLoopHistoryForCopilot)
            ),
            "gemini" => new CodingExecutionProfile(
                normalizedProvider,
                normalizedModel,
                UseCompactLoopPrompt: flashLike,
                OptimizeCodexCli: false,
                AllowUiOneShot: flashLike,
                AllowDeterministicStdoutFastPath: true,
                PreferBundleFallback: multiFileLike,
                PreferDirectRecovery: false,
                EnableGameScaffoldFallback: false,
                RequestTimeoutSeconds: flashLike ? 45 : 60,
                MaxIterations: flashLike ? Math.Min(baseIterations, 3) : baseIterations,
                LoopMaxActions: baseActions,
                OneShotMaxActions: Math.Max(4, baseActions),
                PlanMaxOutputTokens: flashLike
                    ? Math.Clamp(_config.CodingMaxOutputTokens, 1200, 2200)
                    : Math.Clamp(_config.CodingMaxOutputTokens, 1200, 3200),
                WorkspaceSnapshotMaxEntries: flashLike ? Math.Min(defaultSnapshotEntries, 36) : defaultSnapshotEntries,
                RecentLoopHistory: flashLike ? 3 : defaultRecentLoopHistory
            ),
            "cerebras" => new CodingExecutionProfile(
                normalizedProvider,
                normalizedModel,
                UseCompactLoopPrompt: true,
                OptimizeCodexCli: false,
                AllowUiOneShot: false,
                AllowDeterministicStdoutFastPath: true,
                PreferBundleFallback: multiFileLike,
                PreferDirectRecovery: false,
                EnableGameScaffoldFallback: false,
                RequestTimeoutSeconds: 45,
                MaxIterations: Math.Min(baseIterations, 3),
                LoopMaxActions: Math.Min(baseActions, 3),
                OneShotMaxActions: Math.Max(3, Math.Min(baseActions, 4)),
                PlanMaxOutputTokens: Math.Clamp(_config.CodingMaxOutputTokens, 1000, 1800),
                WorkspaceSnapshotMaxEntries: Math.Min(defaultSnapshotEntries, 24),
                RecentLoopHistory: 2
            ),
            "nvidia" => new CodingExecutionProfile(
                normalizedProvider,
                normalizedModel,
                UseCompactLoopPrompt: reasoningLikeModel,
                OptimizeCodexCli: false,
                AllowUiOneShot: false,
                AllowDeterministicStdoutFastPath: true,
                PreferBundleFallback: multiFileLike,
                PreferDirectRecovery: false,
                EnableGameScaffoldFallback: false,
                RequestTimeoutSeconds: Math.Max(45, _config.NvidiaTimeoutSec),
                MaxIterations: Math.Min(baseIterations, 3),
                LoopMaxActions: Math.Min(baseActions, 3),
                OneShotMaxActions: Math.Max(3, Math.Min(baseActions, 4)),
                PlanMaxOutputTokens: Math.Clamp(_config.CodingMaxOutputTokens, 1200, 2400),
                WorkspaceSnapshotMaxEntries: Math.Min(defaultSnapshotEntries, 28),
                RecentLoopHistory: 3
            ),
            _ => new CodingExecutionProfile(
                normalizedProvider,
                normalizedModel,
                UseCompactLoopPrompt: normalizedModel.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase),
                OptimizeCodexCli: false,
                AllowUiOneShot: false,
                AllowDeterministicStdoutFastPath: true,
                PreferBundleFallback: multiFileLike,
                PreferDirectRecovery: false,
                EnableGameScaffoldFallback: false,
                RequestTimeoutSeconds: 45,
                MaxIterations: Math.Min(baseIterations, 3),
                LoopMaxActions: baseActions,
                OneShotMaxActions: Math.Max(4, baseActions),
                PlanMaxOutputTokens: defaultPlanTokens,
                WorkspaceSnapshotMaxEntries: Math.Min(defaultSnapshotEntries, 40),
                RecentLoopHistory: defaultRecentLoopHistory
            )
        };

        return ApplyLanguageSpecificCodingProfile(baseProfile, objective, languageHint, requestedPaths);
    }

    private CodingExecutionProfile ApplyLanguageSpecificCodingProfile(
        CodingExecutionProfile profile,
        string objective,
        string languageHint,
        IReadOnlyList<string> requestedPaths
    )
    {
        var normalizedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var frontendLike = IsFrontendLikeCodingTask(objective, normalizedLanguage);
        var gameLike = IsGameLikeCodingTask(objective, normalizedLanguage);
        var multiFileLike = requestedPaths.Count > 1 || frontendLike || gameLike;
        var copilotMini = IsPinnedCopilotModel(profile.Provider, profile.Model);

        return normalizedLanguage switch
        {
            "html" or "css" => profile with
            {
                UseCompactLoopPrompt = true,
                AllowUiOneShot = profile.Provider == "codex" || (!copilotMini && profile.Provider == "copilot") || profile.AllowUiOneShot,
                PreferBundleFallback = true,
                PreferDirectRecovery = profile.Provider == "codex" || profile.Provider == "copilot" || profile.PreferDirectRecovery,
                RequestTimeoutSeconds = Math.Max(profile.RequestTimeoutSeconds, profile.Provider == "copilot" ? 90 : 60),
                MaxIterations = Math.Min(profile.MaxIterations, 2),
                LoopMaxActions = copilotMini ? Math.Min(Math.Max(profile.LoopMaxActions, 2), 3) : Math.Max(profile.LoopMaxActions, 4),
                OneShotMaxActions = copilotMini ? Math.Min(Math.Max(profile.OneShotMaxActions, 2), 3) : Math.Max(profile.OneShotMaxActions, 5),
                PlanMaxOutputTokens = Math.Max(profile.PlanMaxOutputTokens, copilotMini ? 1500 : 1800),
                WorkspaceSnapshotMaxEntries = Math.Min(Math.Max(profile.WorkspaceSnapshotMaxEntries, 20), copilotMini ? 24 : 40),
                RecentLoopHistory = Math.Min(profile.RecentLoopHistory, 2)
            },
            "javascript" when frontendLike || multiFileLike => profile with
            {
                UseCompactLoopPrompt = true,
                PreferBundleFallback = true,
                PreferDirectRecovery = profile.Provider == "codex" || profile.Provider == "copilot" || profile.PreferDirectRecovery,
                AllowUiOneShot = profile.Provider == "codex" || (!copilotMini && profile.Provider == "copilot"),
                MaxIterations = Math.Min(profile.MaxIterations, 3),
                LoopMaxActions = copilotMini ? Math.Min(Math.Max(profile.LoopMaxActions, 2), 3) : Math.Max(profile.LoopMaxActions, 3),
                OneShotMaxActions = copilotMini ? Math.Min(Math.Max(profile.OneShotMaxActions, 2), 3) : Math.Max(profile.OneShotMaxActions, 4),
                PlanMaxOutputTokens = Math.Max(profile.PlanMaxOutputTokens, copilotMini ? 1500 : 1800)
            },
            "javascript" => profile with
            {
                PreferBundleFallback = false,
                PreferDirectRecovery = false,
                AllowUiOneShot = false,
                AllowDeterministicStdoutFastPath = true,
                MaxIterations = Math.Max(profile.MaxIterations, 3),
                PlanMaxOutputTokens = Math.Max(profile.PlanMaxOutputTokens, 1400)
            },
            "python" => profile with
            {
                PreferBundleFallback = profile.PreferBundleFallback || multiFileLike,
                PreferDirectRecovery = profile.PreferDirectRecovery || (copilotMini && multiFileLike),
                AllowUiOneShot = false,
                AllowDeterministicStdoutFastPath = true,
                MaxIterations = copilotMini && multiFileLike ? Math.Min(Math.Max(profile.MaxIterations, 2), 2) : Math.Max(profile.MaxIterations, 3),
                PlanMaxOutputTokens = Math.Max(profile.PlanMaxOutputTokens, 1400)
            },
            "java" => profile with
            {
                UseCompactLoopPrompt = true,
                PreferBundleFallback = false,
                PreferDirectRecovery = copilotMini && multiFileLike,
                AllowUiOneShot = false,
                AllowDeterministicStdoutFastPath = false,
                RequestTimeoutSeconds = Math.Max(profile.RequestTimeoutSeconds, profile.Provider == "copilot" ? 90 : 60),
                MaxIterations = copilotMini && multiFileLike ? Math.Min(Math.Max(profile.MaxIterations, 2), 2) : Math.Max(profile.MaxIterations, 3),
                LoopMaxActions = Math.Min(Math.Max(profile.LoopMaxActions, 2), 3),
                OneShotMaxActions = Math.Min(Math.Max(profile.OneShotMaxActions, 3), 4),
                PlanMaxOutputTokens = Math.Max(profile.PlanMaxOutputTokens, 1800),
                WorkspaceSnapshotMaxEntries = Math.Min(profile.WorkspaceSnapshotMaxEntries, 20),
                RecentLoopHistory = Math.Min(profile.RecentLoopHistory, 2)
            },
            "c" or "cpp" => profile with
            {
                UseCompactLoopPrompt = true,
                PreferBundleFallback = false,
                PreferDirectRecovery = copilotMini && multiFileLike,
                AllowUiOneShot = false,
                AllowDeterministicStdoutFastPath = false,
                RequestTimeoutSeconds = Math.Max(profile.RequestTimeoutSeconds, profile.Provider == "copilot" ? 90 : 60),
                MaxIterations = copilotMini && multiFileLike ? Math.Min(Math.Max(profile.MaxIterations, 2), 2) : Math.Max(profile.MaxIterations, 3),
                LoopMaxActions = Math.Min(Math.Max(profile.LoopMaxActions, 2), 3),
                OneShotMaxActions = Math.Min(Math.Max(profile.OneShotMaxActions, 3), 4),
                PlanMaxOutputTokens = Math.Max(profile.PlanMaxOutputTokens, 2000),
                WorkspaceSnapshotMaxEntries = Math.Min(profile.WorkspaceSnapshotMaxEntries, 18),
                RecentLoopHistory = Math.Min(profile.RecentLoopHistory, 2)
            },
            _ => profile
        };
    }

    private static bool IsFrontendLikeCodingTask(string objective, string languageHint)
    {
        var lang = NormalizeCodingLanguageHintPreservingAuto(languageHint);
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        var backendSignals = ContainsAny(
            text,
            "api",
            "server",
            "grpc",
            "socket",
            "redis",
            "db",
            "database",
            "migration"
        );
        if (string.IsNullOrWhiteSpace(text))
        {
            return lang is "html" or "css" or "javascript";
        }

        if (lang != "auto")
        {
            return (lang is "html" or "css" or "javascript") && !backendSignals;
        }

        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return (explicitLanguage is "html" or "css" or "javascript") && !backendSignals;
        }

        var frontendSignals = ContainsAny(
            text,
            "ui",
            "ux",
            "웹",
            "web",
            "페이지",
            "page",
            "랜딩",
            "landing",
            "html",
            "css",
            "frontend",
            "프론트",
            "react",
            "canvas",
            "vite",
            "브라우저"
        );

        return (lang is "html" or "css" or "javascript" || frontendSignals) && !backendSignals;
    }

    private static bool IsGameLikeCodingTask(string objective, string languageHint)
    {
        var lang = NormalizeCodingLanguageHintPreservingAuto(languageHint);
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return lang is "html" or "javascript" or "css";
        }

        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        var gameSignals = ContainsAny(text, "게임", "game", "arcade", "슈팅", "shooter", "shooting", "platformer", "tetris", "pong", "snake", "벽돌깨기", "비행기");
        if (!gameSignals)
        {
            return false;
        }

        if (lang != "auto")
        {
            return lang is "html" or "javascript" or "css";
        }

        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return explicitLanguage is "html" or "javascript" or "css";
        }

        return ContainsAny(text, "웹", "web", "canvas", "브라우저", "html");
    }

    private bool ShouldUseOneShotMode(CodingExecutionProfile profile, string objective, string languageHint)
    {
        if (!_config.CodingEnableOneShotUiClone || !profile.AllowUiOneShot)
        {
            return false;
        }

        var lang = NormalizeCodingLanguageHintPreservingAuto(languageHint);
        if (lang != "auto" && lang != "html" && lang != "javascript" && lang != "css" && lang != "python")
        {
            return false;
        }

        return IsFrontendLikeCodingTask(objective, languageHint) || IsGameLikeCodingTask(objective, languageHint);
    }

    private int ResolveMaxIterations(CodingExecutionProfile profile, bool oneShotMode)
    {
        return oneShotMode ? 1 : Math.Max(1, profile.MaxIterations);
    }

    private int ResolveMaxActions(CodingExecutionProfile profile, bool oneShotMode)
    {
        return oneShotMode
            ? Math.Max(1, profile.OneShotMaxActions)
            : Math.Max(1, profile.LoopMaxActions);
    }

    private int GetCodingPlanMaxOutputTokens(CodingExecutionProfile profile)
    {
        return Math.Max(1200, profile.PlanMaxOutputTokens);
    }

    private int ResolveDirectGenerationMaxOutputTokens(CodingExecutionProfile profile, bool bundleMode)
    {
        var configured = Math.Max(900, _config.CodingMaxOutputTokens);
        return profile.Provider switch
        {
            "groq" when IsGroqCompoundLikeCodingModel(profile.Model) => Math.Min(configured, bundleMode ? 1400 : 1200),
            "groq" when profile.Model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) => Math.Min(configured, bundleMode ? 1800 : 1500),
            "groq" => Math.Min(configured, bundleMode ? 2400 : 1900),
            "cerebras" => Math.Min(configured, bundleMode ? 1800 : 1500),
            "nvidia" => Math.Min(configured, bundleMode ? 2400 : 1900),
            "gemini" when profile.Model.Contains("flash", StringComparison.OrdinalIgnoreCase)
                || profile.Model.Contains("lite", StringComparison.OrdinalIgnoreCase) => Math.Min(configured, bundleMode ? 2200 : 1800),
            "codex" => Math.Min(configured, bundleMode ? 3600 : 2800),
            "copilot" when IsPinnedCopilotModel(profile.Provider, profile.Model) => Math.Min(configured, bundleMode ? 1600 : 1300),
            "copilot" => Math.Min(configured, bundleMode ? 3200 : 2600),
            _ => Math.Min(configured, bundleMode ? 2600 : 2200)
        };
    }

    private int ResolveDraftGenerationMaxOutputTokens(CodingExecutionProfile profile)
    {
        var configured = Math.Max(1000, _config.CodingMaxOutputTokens);
        return profile.Provider switch
        {
            "groq" when IsGroqCompoundLikeCodingModel(profile.Model) => Math.Min(configured, 1400),
            "groq" => Math.Min(configured, 1800),
            "cerebras" => Math.Min(configured, 1600),
            "nvidia" => Math.Min(configured, 1800),
            "gemini" when profile.Model.Contains("flash", StringComparison.OrdinalIgnoreCase)
                || profile.Model.Contains("lite", StringComparison.OrdinalIgnoreCase) => Math.Min(configured, 1800),
            "codex" => Math.Min(configured, 2600),
            "copilot" when IsPinnedCopilotModel(profile.Provider, profile.Model) => Math.Min(configured, 1400),
            "copilot" => Math.Min(configured, 2400),
            _ => Math.Min(configured, 2200)
        };
    }

    private static bool IsGroqCompoundLikeCodingModel(string model)
    {
        var normalized = (model ?? string.Empty).Trim();
        return normalized.StartsWith("groq/compound", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("compound", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildProviderModelPromptRuleLines(string provider, string model)
    {
        var normalizedProvider = (provider ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedModel = (model ?? string.Empty).Trim();
        var lines = new List<string>();
        switch (normalizedProvider)
        {
            case "groq":
                lines.Add(IsGroqCompoundLikeCodingModel(normalizedModel)
                    ? "- Groq compound 계열이다. analysis와 액션은 짧게 유지하고 한 반복에 핵심 파일만 다뤄라"
                    : "- Groq 계열이다. 장문 설명 대신 파일 액션 위주로 진행하고 read_file 남용을 피하라");
                break;
            case "cerebras":
                lines.Add("- Cerebras 계열이다. 긴 산출물보다 필요한 핵심 파일만 정확히 갱신하라");
                break;
            case "nvidia":
                lines.Add("- NVIDIA NIM 계열이다. OpenAI 호환 텍스트 LLM으로 보고 핵심 파일 변경과 검증 절차를 간결하게 유지하라");
                break;
            case "gemini":
                lines.Add(normalizedModel.Contains("flash", StringComparison.OrdinalIgnoreCase) || normalizedModel.Contains("lite", StringComparison.OrdinalIgnoreCase)
                    ? "- Gemini flash/lite 계열이다. analysis와 액션을 짧고 구조적으로 유지하라"
                    : "- Gemini 계열이다. 설계 근거는 짧게, 검증 명령은 구체적으로 적어라");
                break;
            case "codex":
                lines.Add("- Codex/GPT-5 계열이다. 누락 요구사항과 엣지케이스까지 보완한 뒤 완료를 선언하라");
                break;
            case "copilot":
                lines.Add(IsPinnedCopilotModel(normalizedProvider, normalizedModel)
                    ? "- Copilot gpt-5-mini 경로다. 설명보다 완성 파일과 단일 검증 흐름을 우선하고 액션 수를 스스로 줄여라"
                    : "- Copilot 계열이다. 빠른 초안보다 실제 실행과 출력 검증이 우선이다");
                break;
            default:
                lines.Add("- 불필요한 서술을 줄이고 실제 파일 변경과 검증 액션에 집중하라");
                break;
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildLanguagePromptRuleLines(
        string provider,
        string model,
        string languageHint,
        string objective,
        IReadOnlyList<string>? requestedPaths = null
    )
    {
        var normalizedProvider = (provider ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedModel = (model ?? string.Empty).Trim();
        var normalizedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var effectiveRequestedPaths = requestedPaths ?? ExtractRequestedCodingPaths(objective, normalizedLanguage);
        var frontendLike = IsFrontendLikeCodingTask(objective, normalizedLanguage);
        var gameLike = IsGameLikeCodingTask(objective, normalizedLanguage);
        var dependencyFreePythonGame = ShouldRequireDependencyFreePythonGame(objective, normalizedLanguage);
        var lines = new List<string>();

        switch (normalizedLanguage)
        {
            case "python":
                lines.Add("- Python은 실행 엔트리를 main.py 또는 요청한 파일명으로 고정하고 stdout 조건이 있으면 print 문자열을 정확히 맞춰라");
                lines.Add("- import 경로, f-string, 들여쓰기 블록을 중간 줄바꿈으로 끊지 말고 파일 전체를 완성본으로 작성하라");
                lines.Add("- 명시적으로 요구되지 않으면 외부 pip 패키지보다 표준 라이브러리를 우선하라");
                if (gameLike)
                {
                    lines.Add(dependencyFreePythonGame
                        ? "- Python 게임/시각화는 외부 pip 패키지를 사용하지 말고 curses를 우선하며, 필요할 때만 tkinter 또는 표준 라이브러리를 사용하라"
                        : "- Python 게임/시각화는 pygame 같은 외부 의존성보다 tkinter 또는 표준 라이브러리 기반 구현을 우선하라");
                    lines.Add("- 단순 print 반복이나 턴제 로그 출력으로 끝내지 말고 실제 입력 처리와 화면 갱신이 있는 게임 루프를 구현하라");
                }
                if (dependencyFreePythonGame)
                {
                    lines.Add("- pygame, pyglet, arcade, panda3d, kivy, requirements.txt, pip install 지시를 출력하거나 생성하지 말라");
                }
                break;
            case "javascript":
                if (frontendLike || effectiveRequestedPaths.Count > 1)
                {
                    lines.Add("- 브라우저형 JavaScript는 index.html/styles.css/app.js 같은 번들 구조를 우선하고 DOMContentLoaded에서 실제 DOM을 렌더링하라");
                    lines.Add("- 화면 검증 문자열은 console.log가 아니라 document.body에 실제 텍스트로 보여야 한다");
                }
                else
                {
                    lines.Add("- Node.js JavaScript는 main.js 엔트리와 CommonJS 기준을 우선하고 stdout 문자열을 직접 조립하라");
                    lines.Add("- 객체 출력이나 console.table 대신 console.log 한 줄 형식으로 요구 stdout을 정확히 맞춰라");
                }
                break;
            case "java":
                lines.Add("- Java는 package 선언 없이 Main.java를 엔트리로 두고 보조 클래스는 별도 파일로 분리하라");
                lines.Add("- stdout 조건이 있으면 Map.toString()이나 디버그 출력으로 대체하지 말고 문자열을 직접 조립해 정확히 맞춰라");
                lines.Add("- javac Main.java ... && java Main 기준으로 바로 실행되도록 작성하라");
                break;
            case "c":
            case "cpp":
                lines.Add("- C/C++는 main.c/main.cpp 엔트리, 보조 .c/.cpp, .h 분리를 지키고 헤더에 선언/소스에 구현을 둬라");
                lines.Add("- scanf scanset은 `%[^,]`처럼 쉼표 기준으로 작성하고 포맷 문자열이나 #include를 줄 중간에서 끊지 말라");
                lines.Add("- stdout 조건이 있으면 printf/puts로 최종 문자열을 직접 조립하고 placeholder 0 출력으로 대체하지 말라");
                break;
            case "html":
            case "css":
                lines.Add("- HTML/CSS 과제는 index.html, styles.css, app.js 구조를 우선하고 index.html이 명확한 엔트리가 되게 작성하라");
                lines.Add("- bucket-card 같은 검증 selector는 실제 DOM에 존재해야 하고 border-radius 같은 스타일은 CSS 파일에 명시하라");
                lines.Add("- 보이는 문자열은 화면 텍스트로 렌더링해야 하며 console 로그만 남기고 끝내면 실패다");
                lines.Add("- visible text로 요구된 token/summary 문구는 대소문자와 문자 형태를 그대로 보여야 하며 text-transform으로 변형하지 말라");
                break;
        }

        if (normalizedProvider == "copilot" && normalizedLanguage is "java" or "c" or "cpp" or "html" or "css")
        {
            lines.Add("- Copilot 경로에서는 import/#include/포맷 문자열/HTML attribute를 중간 줄바꿈으로 끊지 말고 파일 전체를 한 번에 써라");
        }
        else if (normalizedProvider == "codex" && normalizedLanguage == "c")
        {
            lines.Add("- Codex C 경로에서는 scanf/printf 포맷 문자열과 헤더 시그니처를 보수적으로 유지하고 최종 실행 전 직접 검증을 전제로 작성하라");
        }
        else if (normalizedProvider == "gemini" && normalizedLanguage is "java" or "c")
        {
            lines.Add("- Gemini 컴파일형 언어 경로에서는 형식상 컴파일만 되는 코드가 아니라 실제 집계 결과를 최종 stdout 문자열로 조립하라");
        }

        if (normalizedModel.Contains("flash", StringComparison.OrdinalIgnoreCase) && normalizedLanguage is "java" or "c" or "html")
        {
            lines.Add("- Flash/Lite 계열이므로 파일 수를 최소로 유지하고 각 파일 역할을 명확하게 나눠 한 번에 완성하라");
        }

        if (IsPinnedCopilotModel(normalizedProvider, normalizedModel))
        {
            lines.Add("- Copilot gpt-5-mini 경로이므로 append_file보다 write_file 전체 교체를 우선하고 각 파일은 처음부터 끝까지 완성본만 써라");
            lines.Add("- 설명, 변명, 추가 제안 없이 요청된 파일 수와 stdout 형식만 정확히 맞춰라");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildCodingVerificationRuleLines()
    {
        return
        [
            "- 완료 판정은 최종 실행 1회, 생성/수정 파일 존재 확인, stdout/stderr 확인 결과를 모두 기준으로만 하라",
            "- 요청에 출력값이 있으면 stdout에 해당 텍스트가 실제로 포함돼야 한다",
            "- 최종 요약에는 실제 남아 있는 파일과 검증 결과만 반영하라"
        ];
    }

    private string BuildRecentLoopLogs(IReadOnlyList<string> iterations, CodingExecutionProfile profile)
    {
        if (iterations.Count == 0)
        {
            return string.Empty;
        }

        var selected = iterations.TakeLast(Math.Max(1, profile.RecentLoopHistory)).ToArray();
        for (var i = 0; i < selected.Length; i++)
        {
            selected[i] = TrimForOutput(selected[i], 500);
        }

        return string.Join("\n", selected);
    }

    private string BuildWorkspaceSnapshot(string workspaceRoot, CodingExecutionProfile profile)
    {
        return BuildWorkspaceSnapshot(workspaceRoot, profile.Provider, profile.WorkspaceSnapshotMaxEntries);
    }

    private string BuildCodingLoopPrompt(
        string objective,
        string languageHint,
        string modeLabel,
        string workspaceRoot,
        CodingExecutionProfile profile,
        bool oneShotMode,
        int iteration,
        int maxIterations,
        int maxActions,
        string workspaceSnapshot,
        string recentLogs,
        CodeExecutionResult lastExecution
    )
    {
        return profile.UseCompactLoopPrompt
            ? BuildCompactCodingLoopPrompt(
                objective,
                languageHint,
                modeLabel,
                workspaceRoot,
                profile,
                oneShotMode,
                iteration,
                maxIterations,
                maxActions,
                workspaceSnapshot,
                recentLogs,
                lastExecution
            )
            : BuildCodingLoopPrompt(
                objective,
                languageHint,
                modeLabel,
                workspaceRoot,
                profile.Provider,
                profile.Model,
                oneShotMode,
                iteration,
                maxIterations,
                maxActions,
                workspaceSnapshot,
                recentLogs,
                lastExecution
            );
    }

    private string BuildCompactCodingLoopPrompt(
        string objective,
        string languageHint,
        string modeLabel,
        string workspaceRoot,
        CodingExecutionProfile profile,
        bool oneShotMode,
        int iteration,
        int maxIterations,
        int maxActions,
        string workspaceSnapshot,
        string recentLogs,
        CodeExecutionResult lastExecution
    )
    {
        var resolvedLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var builder = new StringBuilder();
        builder.AppendLine("로컬 코딩 실행 에이전트다. 반드시 JSON 객체만 출력하라.");
        builder.AppendLine($"provider={profile.Provider}");
        builder.AppendLine($"model={profile.Model}");
        builder.AppendLine($"mode={modeLabel}");
        builder.AppendLine($"iteration={iteration}/{maxIterations}");
        builder.AppendLine($"cwd={workspaceRoot}");
        builder.AppendLine($"language={resolvedLanguage}");
        builder.AppendLine($"one_shot={(oneShotMode ? "true" : "false")}");
        builder.AppendLine();
        builder.AppendLine("[goal]");
        builder.AppendLine(objective);
        builder.AppendLine();
        builder.AppendLine("[quality_brief]");
        builder.AppendLine(BuildCodingQualityBrief(objective, resolvedLanguage));
        builder.AppendLine();
        builder.AppendLine("[last]");
        builder.AppendLine($"status={lastExecution.Status}");
        builder.AppendLine($"command={lastExecution.Command}");
        builder.AppendLine($"stdout={TrimForOutput(lastExecution.StdOut, 500)}");
        builder.AppendLine($"stderr={TrimForOutput(lastExecution.StdErr, 500)}");
        builder.AppendLine();
        builder.AppendLine("[recent]");
        builder.AppendLine(string.IsNullOrWhiteSpace(recentLogs) ? "(none)" : recentLogs);
        builder.AppendLine();
        builder.AppendLine("[workspace]");
        builder.AppendLine(workspaceSnapshot);
        builder.AppendLine();
        builder.AppendLine("schema:");
        builder.AppendLine("{\"analysis\":\"...\",\"done\":false,\"final_message\":\"...\",\"actions\":[{\"type\":\"write_file\",\"path\":\"index.html\",\"content\":\"...\",\"command\":\"\"}]}");
        builder.AppendLine("allowed_types: mkdir, write_file, append_file, read_file, delete_file, run");
        builder.AppendLine($"max_actions={Math.Max(1, maxActions)}");
        builder.AppendLine("provider_rules:");
        foreach (var rule in BuildProviderModelPromptRuleLines(profile.Provider, profile.Model))
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("language_rules:");
        foreach (var rule in BuildLanguagePromptRuleLines(profile.Provider, profile.Model, resolvedLanguage, objective))
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("verification_rules:");
        foreach (var rule in BuildCodingVerificationRuleLines())
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("rules:");
        builder.AppendLine("- analysis는 1~2문장 이내로 유지하라");
        builder.AppendLine("- done=false 면 actions에 최소 1개 이상의 실질 액션을 넣어라");
        builder.AppendLine("- path 는 상대경로만 사용하라");
        builder.AppendLine("- content 는 실제 파일 전체 내용만 넣어라");
        builder.AppendLine("- JSON 문자열 내부 줄바꿈은 \\n 으로 이스케이프하라");
        builder.AppendLine("- 가능하면 한 번에 필요한 파일을 모두 작성하고 run 은 마지막 검증 1회만 사용하라");
        builder.AppendLine("- 완료되면 done=true 와 actions=[] 로 끝내라");
        return builder.ToString().Trim();
    }

    private bool ShouldPreferFileBundleFallback(
        CodingExecutionProfile profile,
        string objective,
        IReadOnlyList<string>? requestedPaths
    )
    {
        if (requestedPaths != null && requestedPaths.Count > 1)
        {
            return true;
        }

        return profile.PreferBundleFallback && (IsFrontendLikeCodingTask(objective, "auto") || IsGameLikeCodingTask(objective, "auto"));
    }

    private bool ShouldAttemptEarlyDirectRecovery(
        CodingExecutionProfile profile,
        string objective,
        string languageHint,
        IReadOnlyList<string> requestedPaths
    )
    {
        if (!profile.PreferDirectRecovery)
        {
            return false;
        }

        return requestedPaths.Count > 1
            || IsFrontendLikeCodingTask(objective, languageHint)
            || IsGameLikeCodingTask(objective, languageHint);
    }

    private async Task<AutonomousCodingOutcome?> TryApplyProviderDirectRecoveryAsync(
        CodingExecutionProfile profile,
        string provider,
        string model,
        string objective,
        string languageHint,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback,
        string progressMode,
        int maxIterations
    )
    {
        if (!ShouldAttemptEarlyDirectRecovery(profile, objective, languageHint, requestedPaths))
        {
            return null;
        }

        progressCallback?.Invoke(BuildCodingProgressUpdate(
            progressMode,
            provider,
            model,
            "recovery",
            "제공자 전용 직생성 복구 경로를 먼저 시도합니다.",
            1,
            maxIterations,
            22,
            false,
            "recovery",
            "마무리 및 복구",
            "긴 계획 루프 대신 파일 번들 또는 코드 직생성을 먼저 시도합니다.",
            3,
            VisibleCodingStageTotal
        ));

        var initialLanguage = ResolveInitialCodingLanguage(languageHint, objective);
        var bundlePreferred = ShouldPreferFileBundleFallback(profile, objective, requestedPaths);
        if (bundlePreferred)
        {
            var bundlePrompt = BuildFallbackFileBundlePrompt(objective, initialLanguage, requestedPaths);
            var bundleGenerated = await GenerateByProviderSafeAsync(
                provider,
                model,
                bundlePrompt,
                cancellationToken,
                ResolveDirectGenerationMaxOutputTokens(profile, bundleMode: true),
                useRawCodexPrompt: true,
                codexWorkingDirectoryOverride: workspaceRoot,
                optimizeCodexForCoding: profile.OptimizeCodexCli,
                timeoutOverrideSeconds: profile.RequestTimeoutSeconds
            );
            var bundleOutcome = await TryMaterializeDirectBundleRecoveryAsync(
                provider,
                model,
                workspaceRoot,
                requestedPaths,
                objective,
                bundleGenerated.Text,
                initialLanguage,
                maxIterations,
                cancellationToken
            );
            if (bundleOutcome != null)
            {
                if (string.Equals(bundleOutcome.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return bundleOutcome;
                }

                var repaired = await TryApplyDeterministicStructuredMultiFileRepairAsync(
                    objective,
                    bundleOutcome.Language,
                    workspaceRoot,
                    requestedPaths,
                    cancellationToken
                );
                if (repaired.Applied && string.Equals(repaired.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return new AutonomousCodingOutcome(
                        repaired.Language,
                        repaired.Code,
                        bundleGenerated.Text,
                        repaired.Execution,
                        repaired.ChangedPaths,
                        BuildAutonomousCodingSummary(
                            new[] { $"provider_direct_bundle_recovery={provider}:{model}", "deterministic_repair=structured_multi_file" },
                            repaired.ChangedPaths,
                            repaired.Execution,
                            maxIterations
                        )
                    );
                }

                return null;
            }

            var repairedWithoutBundle = await TryApplyDeterministicStructuredMultiFileRepairAsync(
                objective,
                initialLanguage,
                workspaceRoot,
                requestedPaths,
                cancellationToken
            );
            if (repairedWithoutBundle.Applied && string.Equals(repairedWithoutBundle.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new AutonomousCodingOutcome(
                    repairedWithoutBundle.Language,
                    repairedWithoutBundle.Code,
                    bundleGenerated.Text,
                    repairedWithoutBundle.Execution,
                    repairedWithoutBundle.ChangedPaths,
                    BuildAutonomousCodingSummary(
                        new[] { $"provider_direct_bundle_recovery={provider}:{model}", "deterministic_repair=structured_multi_file" },
                        repairedWithoutBundle.ChangedPaths,
                        repairedWithoutBundle.Execution,
                        maxIterations
                    )
                );
            }

            if (requestedPaths.Count > 1 || IsFrontendLikeCodingTask(objective, languageHint) || IsGameLikeCodingTask(objective, languageHint))
            {
                return null;
            }
        }

        var fallbackPrompt = BuildFallbackCodeOnlyPrompt(objective, initialLanguage);
        var fallbackGenerated = await GenerateByProviderSafeAsync(
            provider,
            model,
            fallbackPrompt,
            cancellationToken,
            ResolveDirectGenerationMaxOutputTokens(profile, bundleMode: false),
            useRawCodexPrompt: true,
            codexWorkingDirectoryOverride: workspaceRoot,
            optimizeCodexForCoding: profile.OptimizeCodexCli,
            timeoutOverrideSeconds: profile.RequestTimeoutSeconds
        );
        var codeOutcome = await TryMaterializeDirectCodeRecoveryAsync(
            provider,
            model,
            workspaceRoot,
            requestedPaths,
            objective,
            fallbackGenerated.Text,
            initialLanguage,
            maxIterations,
            cancellationToken
        );
        if (codeOutcome == null)
        {
            var repairedWithoutCode = await TryApplyDeterministicStructuredMultiFileRepairAsync(
                objective,
                initialLanguage,
                workspaceRoot,
                requestedPaths,
                cancellationToken
            );
            if (repairedWithoutCode.Applied && string.Equals(repairedWithoutCode.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new AutonomousCodingOutcome(
                    repairedWithoutCode.Language,
                    repairedWithoutCode.Code,
                    fallbackGenerated.Text,
                    repairedWithoutCode.Execution,
                    repairedWithoutCode.ChangedPaths,
                    BuildAutonomousCodingSummary(
                        new[] { $"provider_direct_code_recovery={provider}:{model}", "deterministic_repair=structured_multi_file" },
                        repairedWithoutCode.ChangedPaths,
                        repairedWithoutCode.Execution,
                        maxIterations
                    )
                );
            }

            return null;
        }

        if (string.Equals(codeOutcome.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return codeOutcome;
        }

        var repairedCodeOutcome = await TryApplyDeterministicStructuredMultiFileRepairAsync(
            objective,
            codeOutcome.Language,
            workspaceRoot,
            requestedPaths,
            cancellationToken
        );
        if (repairedCodeOutcome.Applied && string.Equals(repairedCodeOutcome.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return new AutonomousCodingOutcome(
                repairedCodeOutcome.Language,
                repairedCodeOutcome.Code,
                fallbackGenerated.Text,
                repairedCodeOutcome.Execution,
                repairedCodeOutcome.ChangedPaths,
                BuildAutonomousCodingSummary(
                    new[] { $"provider_direct_code_recovery={provider}:{model}", "deterministic_repair=structured_multi_file" },
                    repairedCodeOutcome.ChangedPaths,
                    repairedCodeOutcome.Execution,
                    maxIterations
                )
            );
        }

        return null;
    }

    private async Task<AutonomousCodingOutcome?> TryMaterializeDirectBundleRecoveryAsync(
        string provider,
        string model,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        string objective,
        string rawResponse,
        string initialLanguage,
        int maxIterations,
        CancellationToken cancellationToken
    )
    {
        var fallbackBundle = ExtractFallbackFileBundle(rawResponse, initialLanguage, objective);
        if (fallbackBundle.Files.Count == 0)
        {
            return null;
        }

        var changedPaths = new List<string>();
        var lastCode = string.Empty;
        foreach (var file in fallbackBundle.Files)
        {
            var normalizedContent = NormalizeProviderGeneratedFileContent(provider, file.Path, file.Content);
            var writeAction = new CodingLoopAction("write_file", file.Path, normalizedContent, string.Empty);
            var writeResult = await ExecuteCodingLoopActionAsync(writeAction, workspaceRoot, requestedPaths, provider, cancellationToken);
            if (string.IsNullOrWhiteSpace(lastCode) && !string.IsNullOrWhiteSpace(writeResult.CodePreview))
            {
                lastCode = writeResult.CodePreview;
            }

            if (writeResult.Changed && !string.IsNullOrWhiteSpace(writeResult.ChangedPath))
            {
                changedPaths.Add(writeResult.ChangedPath);
            }
        }

        if (changedPaths.Count == 0)
        {
            return null;
        }

        var expectedOutput = ExtractExpectedConsoleOutput(objective);
        var execution = await RunDirectRecoveryVerificationAsync(
            fallbackBundle.Language,
            workspaceRoot,
            objective,
            requestedPaths,
            changedPaths,
            expectedOutput,
            cancellationToken
        );
        var summary = BuildAutonomousCodingSummary(
            new[] { $"provider_direct_bundle_recovery={provider}:{model}" },
            changedPaths,
            execution,
            maxIterations
        );
        return new AutonomousCodingOutcome(
            fallbackBundle.Language,
            string.IsNullOrWhiteSpace(lastCode) ? fallbackBundle.Files[0].Content : lastCode,
            rawResponse,
            execution,
            changedPaths,
            summary
        );
    }

    private async Task<AutonomousCodingOutcome?> TryMaterializeDirectCodeRecoveryAsync(
        string provider,
        string model,
        string workspaceRoot,
        IReadOnlyList<string> requestedPaths,
        string objective,
        string rawResponse,
        string initialLanguage,
        int maxIterations,
        CancellationToken cancellationToken
    )
    {
        var fallbackCode = ExtractFallbackCode(rawResponse, initialLanguage, objective);
        if (string.IsNullOrWhiteSpace(fallbackCode.Code))
        {
            return null;
        }

        var fallbackPath = SuggestFallbackEntryPath(fallbackCode.Language, objective, requestedPaths);
        var normalizedCode = NormalizeProviderGeneratedFileContent(provider, fallbackPath, fallbackCode.Code);
        var writeAction = new CodingLoopAction("write_file", fallbackPath, normalizedCode, string.Empty);
        var writeResult = await ExecuteCodingLoopActionAsync(writeAction, workspaceRoot, requestedPaths, provider, cancellationToken);
        if (!writeResult.Changed || string.IsNullOrWhiteSpace(writeResult.ChangedPath))
        {
            return null;
        }

        var changedPaths = new[] { writeResult.ChangedPath };
        var expectedOutput = ExtractExpectedConsoleOutput(objective);
        var execution = await RunDirectRecoveryVerificationAsync(
            fallbackCode.Language,
            workspaceRoot,
            objective,
            requestedPaths,
            changedPaths,
            expectedOutput,
            cancellationToken
        );
        var summary = BuildAutonomousCodingSummary(
            new[] { $"provider_direct_code_recovery={provider}:{model}" },
            changedPaths,
            execution,
            maxIterations
        );
        return new AutonomousCodingOutcome(
            fallbackCode.Language,
            string.IsNullOrWhiteSpace(writeResult.CodePreview) ? fallbackCode.Code : writeResult.CodePreview,
            rawResponse,
            execution,
            changedPaths,
            summary
        );
    }

    private static string NormalizeProviderGeneratedFileContent(string provider, string path, string content)
    {
        var normalized = NormalizeGeneratedFileContent(content);
        var extractedShellWrapped = ExtractShellWrappedFileContent(path, normalized);
        if (!string.IsNullOrWhiteSpace(extractedShellWrapped))
        {
            normalized = extractedShellWrapped;
        }

        if (!string.Equals(provider, "copilot", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var extension = (Path.GetExtension(path) ?? string.Empty).Trim().ToLowerInvariant();
        if (extension is not (".py" or ".js" or ".jsx" or ".ts" or ".tsx" or ".json" or ".css" or ".html" or ".java" or ".c" or ".h" or ".cpp" or ".hpp" or ".txt"))
        {
            return normalized;
        }

        var collapsed = CollapseLikelySoftWrappedLines(normalized);
        if (extension == ".py")
        {
            collapsed = RepairLikelyWrappedPythonContent(collapsed);
        }

        if (extension != ".json")
        {
            return collapsed;
        }

        try
        {
            using var document = JsonDocument.Parse(collapsed);
            return document.RootElement.GetRawText();
        }
        catch
        {
            return collapsed;
        }
    }

    private static string ExtractShellWrappedFileContent(string path, string content)
    {
        var targetFileName = Path.GetFileName(path ?? string.Empty);
        var normalized = (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(targetFileName)
            || string.IsNullOrWhiteSpace(normalized)
            || !normalized.Contains("cat >", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith("cat >", StringComparison.Ordinal))
            {
                continue;
            }

            var heredocIndex = trimmed.IndexOf("<<", StringComparison.Ordinal);
            if (heredocIndex <= 5)
            {
                continue;
            }

            var rawPath = trimmed["cat >".Length..heredocIndex].Trim().Trim('\'', '"');
            if (!string.Equals(Path.GetFileName(rawPath), targetFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var delimiter = trimmed[(heredocIndex + 2)..].Trim().TrimStart('-').Trim().Trim('\'', '"');
            if (string.IsNullOrWhiteSpace(delimiter))
            {
                continue;
            }

            var extracted = new List<string>();
            for (var bodyIndex = index + 1; bodyIndex < lines.Length; bodyIndex++)
            {
                if (string.Equals(lines[bodyIndex].Trim(), delimiter, StringComparison.Ordinal))
                {
                    return NormalizeGeneratedFileContent(string.Join('\n', extracted));
                }

                extracted.Add(lines[bodyIndex]);
            }
        }

        return string.Empty;
    }

    private static string CollapseLikelySoftWrappedLines(string content)
    {
        var normalized = (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim('\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized.Trim();
        }

        for (var pass = 0; pass < 8; pass++)
        {
            var lines = normalized.Split('\n');
            var merged = new List<string>(lines.Length);
            var changed = false;

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].TrimEnd();
                if (merged.Count == 0)
                {
                    merged.Add(line);
                    continue;
                }

                var previous = merged[^1];
                var currentTrimmed = line.TrimStart();
                var nextNonEmpty = FindNextNonEmptyTrimmedLine(lines, index + 1);

                if (string.IsNullOrWhiteSpace(currentTrimmed))
                {
                    if (ShouldSkipLikelySoftWrappedBlank(previous, nextNonEmpty))
                    {
                        changed = true;
                        continue;
                    }

                    merged.Add(string.Empty);
                    continue;
                }

                if (ShouldMergeLikelySoftWrappedLine(previous, currentTrimmed))
                {
                    merged[^1] = MergeLikelySoftWrappedLine(previous, currentTrimmed);
                    changed = true;
                    continue;
                }

                merged.Add(line);
            }

            var rebuilt = string.Join('\n', merged).Trim('\n');
            if (!changed || string.Equals(rebuilt, normalized, StringComparison.Ordinal))
            {
                return rebuilt;
            }

            normalized = rebuilt;
        }

        return normalized.Trim('\n');
    }

    private static string FindNextNonEmptyTrimmedLine(string[] lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static bool ShouldSkipLikelySoftWrappedBlank(string previous, string nextNonEmpty)
    {
        var prevTrimmed = (previous ?? string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(prevTrimmed) || string.IsNullOrWhiteSpace(nextNonEmpty))
        {
            return false;
        }

        return EndsWithLikelySoftWrapJoiner(prevTrimmed)
               || EndsInsideOpenString(prevTrimmed)
               || prevTrimmed.EndsWith("{", StringComparison.Ordinal)
               || prevTrimmed.EndsWith("[", StringComparison.Ordinal)
               || LooksLikeJsonKeyValuePrefix(prevTrimmed);
    }

    private static bool ShouldMergeLikelySoftWrappedLine(string previous, string currentTrimmed)
    {
        var prevTrimmed = (previous ?? string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(prevTrimmed) || string.IsNullOrWhiteSpace(currentTrimmed))
        {
            return false;
        }

        if (EndsInsideOpenString(prevTrimmed))
        {
            return true;
        }

        if (EndsWithLikelySoftWrapJoiner(prevTrimmed))
        {
            return true;
        }

        if (LooksLikeJsonKeyValuePrefix(prevTrimmed))
        {
            return true;
        }

        return false;
    }

    private static string MergeLikelySoftWrappedLine(string previous, string currentTrimmed)
    {
        var prevTrimmed = (previous ?? string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(prevTrimmed))
        {
            return currentTrimmed;
        }

        if (EndsInsideOpenString(prevTrimmed))
        {
            return prevTrimmed + currentTrimmed;
        }

        var separator = NeedsSoftWrapJoinWithoutSpace(prevTrimmed, currentTrimmed) ? string.Empty : " ";
        return prevTrimmed + separator + currentTrimmed;
    }

    private static bool EndsWithLikelySoftWrapJoiner(string line)
    {
        var trimmed = (line ?? string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var last = trimmed[^1];
        if ("=+-*/%([{,.&|\\?".Contains(last))
        {
            return true;
        }

        var lastToken = trimmed
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
        return lastToken is "from" or "import" or "in" or "for" or "with" or "return" or "await" or "yield" or "and" or "or" or "not" or "#include";
    }

    private static bool NeedsSoftWrapJoinWithoutSpace(string previous, string currentTrimmed)
    {
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(currentTrimmed))
        {
            return false;
        }

        var prevLast = previous.TrimEnd()[^1];
        var currentFirst = currentTrimmed[0];
        if ("([{./\\".Contains(prevLast) || ")]},.;".Contains(currentFirst))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeJsonKeyValuePrefix(string line)
    {
        var trimmed = (line ?? string.Empty).TrimEnd();
        return trimmed.EndsWith("\":", StringComparison.Ordinal);
    }

    private static bool EndsInsideOpenString(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var delimiter = '\0';
        var escaped = false;
        foreach (var ch in line)
        {
            if (delimiter == '\0')
            {
                if (ch is '"' or '\'' or '`')
                {
                    delimiter = ch;
                }

                escaped = ch == '\\' && !escaped;
                continue;
            }

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == delimiter)
            {
                delimiter = '\0';
            }
        }

        return delimiter != '\0';
    }

    private static string RepairLikelyWrappedPythonContent(string content)
    {
        var normalized = (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim('\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized.Trim();
        }

        var lines = normalized.Split('\n');
        var rebuilt = new List<string>(lines.Length * 2);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Replace('\t', ' ');
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                rebuilt.Add(string.Empty);
                continue;
            }

            var leadingSpaces = trimmed.Length - trimmed.TrimStart().Length;
            if (leadingSpaces > 0 && leadingSpaces % 4 != 0)
            {
                leadingSpaces -= leadingSpaces % 4;
                trimmed = new string(' ', leadingSpaces) + trimmed.TrimStart();
            }

            if (TrySplitInlinePythonSuite(trimmed, out var header, out var suiteBody))
            {
                rebuilt.Add(header);
                rebuilt.Add(suiteBody);
                continue;
            }

            rebuilt.Add(trimmed);
        }

        return string.Join('\n', rebuilt).Trim('\n');
    }

    private static bool TrySplitInlinePythonSuite(string line, out string header, out string suiteBody)
    {
        header = string.Empty;
        suiteBody = string.Empty;
        var text = (line ?? string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var leadingSpaces = text.Length - text.TrimStart().Length;
        var indent = new string(' ', leadingSpaces);
        var trimmed = text.TrimStart();
        var keywords = new[] { "def ", "for ", "if ", "elif ", "while ", "with ", "class ", "try:", "except ", "finally:" };
        if (!keywords.Any(keyword => trimmed.StartsWith(keyword, StringComparison.Ordinal)))
        {
            return false;
        }

        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= trimmed.Length - 1)
        {
            return false;
        }

        var body = trimmed[(colonIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        header = indent + trimmed[..(colonIndex + 1)];
        suiteBody = indent + "    " + body;
        return true;
    }

    private async Task<CodeExecutionResult> RunDirectRecoveryVerificationAsync(
        string language,
        string workspaceRoot,
        string objective,
        IReadOnlyList<string> requestedPaths,
        IReadOnlyCollection<string> changedPaths,
        string? expectedOutput,
        CancellationToken cancellationToken
    )
    {
        var displayCommand = BuildVerificationDisplayCommand(language, changedPaths, workspaceRoot, objective, requestedPaths, expectedOutput);
        var command = BuildVerificationCommand(language, changedPaths, workspaceRoot, objective, requestedPaths, expectedOutput);
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CodeExecutionResult(
                language,
                workspaceRoot,
                changedPaths.FirstOrDefault() ?? "-",
                string.IsNullOrWhiteSpace(displayCommand) ? "(skipped)" : displayCommand,
                0,
                "제공자 전용 복구 후 검증 명령을 만들지 못해 파일 생성 결과만 유지했습니다.",
                string.Empty,
                "skipped"
            );
        }

        var shell = await RunWorkspaceCommandWithAutoInstallAsync(command, workspaceRoot, cancellationToken);
        return new CodeExecutionResult(
            "bash",
            workspaceRoot,
            "-",
            displayCommand,
            shell.ExitCode,
            shell.StdOut,
            shell.StdErr,
            shell.TimedOut ? "timeout" : (shell.ExitCode == 0 ? "ok" : "error")
        );
    }
}
