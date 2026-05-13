using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private const string RoutineBrowserAgentDefaultProvider = "codex";
    private const string RoutineBrowserAgentDefaultModel = "gpt-5.4";
    private const int RoutineBrowserAgentDefaultTimeoutSeconds = 120;
    private const int RoutineBrowserAgentMinTimeoutSeconds = 120;
    private const int RoutineBrowserAgentMaxTimeoutSeconds = 1800;
    private const string RoutineBrowserAgentToolProfilePlaywrightOnly = "playwright_only";
    private const string RoutineBrowserAgentToolProfileDesktopControl = "desktop_control";
    private static readonly IReadOnlySet<string> RoutineBrowserAgentSupportedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RoutineBrowserAgentDefaultModel
    };
    private static readonly IReadOnlySet<string> RoutineBrowserAgentSupportedToolProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RoutineBrowserAgentToolProfilePlaywrightOnly,
        RoutineBrowserAgentToolProfileDesktopControl
    };

    private async Task<RoutineActionResult> CreateRoutineCoreAsync(
        string request,
        string title,
        string? executionMode,
        string? agentProvider,
        string? agentModel,
        string? agentStartUrl,
        int? agentTimeoutSeconds,
        string? agentToolProfile,
        bool? agentUsePlaywright,
        string scheduleSourceMode,
        int maxRetries,
        int retryDelaySeconds,
        string notifyPolicy,
        bool notifyTelegram,
        RoutineScheduleConfig scheduleConfig,
        bool runImmediately,
        string source,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    )
    {
        var createdAt = DateTimeOffset.UtcNow;
        var id = $"rt-{createdAt:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        var runDir = Path.Combine(_config.WorkspaceRootDir, "routines", id);
        Directory.CreateDirectory(runDir);
        ReportRoutineCreateProgress(
            progressCallback,
            "루틴 요청을 분석하는 중입니다.",
            10,
            "request_analysis",
            "요청 분석",
            "스케줄과 실행 경로를 확인하고 있습니다.",
            1
        );

        var taskRequest = ResolveRoutineExecutionRequestText(request, title, scheduleSourceMode);
        var normalizedExecutionMode = NormalizeRoutineExecutionMode(executionMode);
        var resolvedExecutionMode = ResolveRoutineExecutionMode(taskRequest, normalizedExecutionMode);
        var executionRoute = ResolveRoutineExecutionRoute(taskRequest, normalizedExecutionMode);
        if (resolvedExecutionMode == "browser_agent" && string.IsNullOrWhiteSpace(agentModel))
        {
            return new RoutineActionResult(false, "브라우저 에이전트 루틴은 에이전트 모델이 필요합니다.", null);
        }

        if (!TryNormalizeRoutineAgentStartUrl(agentStartUrl, out var normalizedAgentStartUrl))
        {
            return new RoutineActionResult(false, "시작 URL은 http:// 또는 https:// 형식이어야 합니다.", null);
        }

        var normalizedAgentProvider = resolvedExecutionMode == "browser_agent"
            ? NormalizeRoutineAgentProvider(agentProvider, agentModel)
            : null;
        var normalizedAgentModel = resolvedExecutionMode == "browser_agent"
            ? NormalizeRoutineAgentModel(agentModel)
            : null;
        var normalizedAgentTimeoutSeconds = resolvedExecutionMode == "browser_agent"
            ? NormalizeRoutineAgentTimeoutSeconds(agentTimeoutSeconds)
            : null;
        var normalizedAgentToolProfile = resolvedExecutionMode == "browser_agent"
            ? NormalizeRoutineAgentToolProfile(agentToolProfile, agentUsePlaywright)
            : null;
        var normalizedAgentUsePlaywright = resolvedExecutionMode == "browser_agent"
            ? NormalizeRoutineAgentUsePlaywright(agentUsePlaywright)
            : false;
        if (resolvedExecutionMode == "browser_agent" && !IsSupportedRoutineBrowserAgentModel(normalizedAgentModel))
        {
            return new RoutineActionResult(false, BuildRoutineBrowserAgentUnsupportedModelMessage(normalizedAgentModel), null);
        }
        if (resolvedExecutionMode == "browser_agent" && !IsSupportedRoutineAgentToolProfile(normalizedAgentToolProfile))
        {
            return new RoutineActionResult(false, BuildRoutineBrowserAgentUnsupportedToolProfileMessage(normalizedAgentToolProfile), null);
        }
        RoutineGenerationResult? generation = null;
        var scriptPath = string.Empty;
        var language = resolvedExecutionMode == "browser_agent" ? "agent" : "llm";
        var code = string.Empty;
        var planner = resolvedExecutionMode == "browser_agent" ? "acp" : "gemini";
        var plannerModel = ResolveRoutineLlmModel(executionRoute.Mode);
        var coderModel = executionRoute.Mode;
        var lastOutput = BuildRoutineExecutionPreview(
            executionRoute.Mode,
            normalizedAgentProvider,
            normalizedAgentModel,
            normalizedAgentStartUrl,
            normalizedAgentToolProfile
        );
        ReportRoutineCreateProgress(
            progressCallback,
            "생성 전략을 준비하는 중입니다.",
            24,
            "planning",
            "생성 전략 준비",
            string.Equals(executionRoute.Mode, "script", StringComparison.Ordinal)
                ? "스크립트 생성 경로와 모델 전략을 준비하고 있습니다."
                : "실행 모드에 맞는 구성 경로를 확정하고 있습니다.",
            2
        );

        if (string.Equals(executionRoute.Mode, "script", StringComparison.Ordinal))
        {
            if (!_llmRouter.HasGroqApiKey())
            {
                return new RoutineActionResult(
                    false,
                    "루틴 스크립트 생성 실패: Groq API 키가 없어 실행 코드를 만들 수 없습니다. 설정에서 Groq 키를 저장하거나 실행 모드를 일반 답변/URL 참조/브라우저 에이전트로 바꾸세요.",
                    null
                );
            }

            generation = await GenerateRoutineImplementationAsync(
                taskRequest,
                new RoutineSchedule(scheduleConfig.Hour, scheduleConfig.Minute, scheduleConfig.Display),
                cancellationToken,
                progressCallback
            );
            var validation = ValidateRoutineGeneratedCode(generation.Language, generation.Code, taskRequest);
            if (!validation.Ok)
            {
                return new RoutineActionResult(
                    false,
                    $"루틴 스크립트 생성 품질 검증 실패: {string.Join(" / ", validation.Warnings)}",
                    null
                );
            }

            ReportRoutineCreateProgress(
                progressCallback,
                "생성한 실행 코드를 저장하는 중입니다.",
                82,
                "save",
                "루틴 등록",
                "생성 결과를 스크립트 파일과 루틴 상태에 반영하고 있습니다.",
                4
            );
            scriptPath = WriteRoutineScript(runDir, generation.Language, generation.Code);
            language = generation.Language;
            code = generation.Code;
            planner = generation.PlannerProvider;
            plannerModel = generation.PlannerModel;
            coderModel = generation.CoderModel;
            lastOutput = generation.Plan;
        }
        else if (string.Equals(executionRoute.Mode, "browser_agent", StringComparison.Ordinal))
        {
            ReportRoutineCreateProgress(
                progressCallback,
                "브라우저 에이전트 실행 구성을 만드는 중입니다.",
                62,
                "implementation",
                "실행 구성 생성",
                "브라우저 에이전트 루틴은 코드 파일 없이 실행 구성을 저장합니다.",
                3
            );
            ReportRoutineCreateProgress(
                progressCallback,
                "브라우저 에이전트 루틴을 저장하는 중입니다.",
                82,
                "save",
                "루틴 등록",
                "생성 결과를 저장하고 즉시 실행 준비를 마칩니다.",
                4
            );
            plannerModel = normalizedAgentProvider ?? "acp";
            coderModel = normalizedAgentModel ?? "browser-agent";
        }
        else
        {
            ReportRoutineCreateProgress(
                progressCallback,
                "실행 구성을 정리하는 중입니다.",
                62,
                "implementation",
                "실행 구성 생성",
                "웹/URL 기반 루틴이므로 별도 스크립트 파일 없이 실행 구성을 만듭니다.",
                3
            );
            ReportRoutineCreateProgress(
                progressCallback,
                "루틴 메타데이터를 저장하는 중입니다.",
                82,
                "save",
                "루틴 등록",
                "생성 결과를 저장하고 스케줄에 연결합니다.",
                4
            );
        }

        var routine = new RoutineDefinition
        {
            Id = id,
            Title = title,
            Request = request,
            ExecutionMode = normalizedExecutionMode,
            AgentProvider = normalizedAgentProvider,
            AgentModel = normalizedAgentModel,
            AgentStartUrl = normalizedAgentStartUrl,
            AgentTimeoutSeconds = normalizedAgentTimeoutSeconds,
            AgentToolProfile = normalizedAgentToolProfile,
            AgentUsePlaywright = normalizedAgentUsePlaywright,
            ScheduleText = scheduleConfig.Display,
            ScheduleSourceMode = scheduleSourceMode,
            MaxRetries = maxRetries,
            RetryDelaySeconds = retryDelaySeconds,
            NotifyPolicy = notifyPolicy,
            NotifyTelegram = notifyTelegram,
            TimezoneId = scheduleConfig.TimezoneId,
            Hour = scheduleConfig.Hour,
            Minute = scheduleConfig.Minute,
            Enabled = true,
            NextRunUtc = createdAt,
            LastRunUtc = null,
            LastStatus = "created",
            LastOutput = lastOutput,
            ScriptPath = scriptPath,
            Language = language,
            Code = code,
            Planner = planner,
            PlannerModel = plannerModel,
            CoderModel = coderModel,
            QualityStatus = generation?.QualityStatus ?? (string.Equals(executionRoute.Mode, "script", StringComparison.Ordinal) ? "ok" : "not_applicable"),
            QualityWarnings = generation?.QualityWarnings?.ToList() ?? new List<string>(),
            CronScheduleKind = "cron",
            CronScheduleExpr = scheduleConfig.CronExpr,
            CronScheduleAtMs = null,
            CronScheduleEveryMs = null,
            CronScheduleAnchorMs = null,
            CreatedUtc = createdAt
        };
        if (string.Equals(resolvedExecutionMode, "browser_agent", StringComparison.Ordinal))
        {
            routine.CronSessionTarget = "isolated";
            routine.CronWakeMode = "next-heartbeat";
            routine.CronPayloadKind = "agentTurn";
            routine.CronPayloadModel = normalizedAgentModel;
            routine.CronPayloadThinking = null;
            routine.CronPayloadTimeoutSeconds = normalizedAgentTimeoutSeconds;
            routine.CronPayloadLightContext = false;
        }
        routine.NextRunUtc = ComputeNextCronBridgeRunUtc(routine, createdAt);

        lock (_routineLock)
        {
            _routinesById[routine.Id] = routine;
            SaveRoutineStateLocked();
        }

        if (!runImmediately)
        {
            ReportRoutineCreateProgress(
                progressCallback,
                "루틴을 저장했습니다. 필요하면 상세 화면에서 테스트 실행하세요.",
                100,
                "save",
                "루틴 등록",
                "생성 결과를 저장하고 다음 예약 실행을 대기합니다.",
                4,
                done: true,
                ok: true
            );
            return new RoutineActionResult(
                true,
                $"루틴 생성 완료: {title} ({scheduleConfig.Display})\n초기 실행은 건너뛰었습니다.",
                ToRoutineSummary(routine)
            );
        }

        ReportRoutineCreateProgress(
            progressCallback,
            "생성 직후 초기 실행을 진행하는 중입니다.",
            94,
            "initial_run",
            "초기 실행",
            string.Equals(executionRoute.Mode, "browser_agent", StringComparison.Ordinal)
                ? $"브라우저 에이전트가 실제 사이트를 조작하며 결과를 기록합니다. 최대 {(normalizedAgentTimeoutSeconds ?? RoutineBrowserAgentDefaultTimeoutSeconds).ToString(CultureInfo.InvariantCulture)}초까지 걸릴 수 있습니다."
                : "루틴이 실제로 한 번 실행되며 결과를 기록합니다.",
            5
        );
        var runNow = await RunRoutineNowAsync(routine.Id, source, cancellationToken);
        var createSummaryPrefix = runNow.Ok
            ? $"루틴 생성 완료: {title} ({scheduleConfig.Display})"
            : $"루틴 생성은 완료됐지만 초기 실행은 실패했습니다: {title} ({scheduleConfig.Display})";
        return runNow with
        {
            Routine = ToRoutineSummary(routine),
            Message = $"{createSummaryPrefix}\n{runNow.Message}"
        };
    }

    private (string Mode, IReadOnlyList<string> Urls) ResolveRoutineExecutionRoute(string request, string? executionMode)
    {
        var normalizedRequest = (request ?? string.Empty).Trim();
        var urls = ResolveWebUrls(normalizedRequest, null, webSearchEnabled: true);
        var resolvedExecutionMode = ResolveRoutineExecutionMode(normalizedRequest, executionMode);
        if (resolvedExecutionMode == "browser_agent")
        {
            return ("browser_agent", urls);
        }

        if (resolvedExecutionMode == LogicGraphExecutionMode)
        {
            return (LogicGraphExecutionMode, Array.Empty<string>());
        }

        if (resolvedExecutionMode == "url")
        {
            return ("gemini-url-single", urls);
        }

        if (resolvedExecutionMode == "web")
        {
            return ("gemini-web-single", Array.Empty<string>());
        }

        return ("script", Array.Empty<string>());
    }

    private static bool LooksLikeRoutineWebLookupRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || LooksLikeRoutineLocalSystemRequest(normalized))
        {
            return false;
        }

        return ContainsAny(
                   normalized,
                   "뉴스",
                   "news",
                   "헤드라인",
                   "속보",
                   "브리핑",
                   "기사",
                   "랭킹",
                   "이슈",
                   "실시간",
                   "최신",
                   "최근",
                   "오늘",
                   "현재",
                   "지금"
               )
               || LooksLikeExplicitWebLookupQuestion(normalized)
               || LooksLikeRealtimeQuestion(normalized);
    }

    private static bool LooksLikeRoutineLocalSystemRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "서버 상태",
            "시스템 상태",
            "local",
            "로컬",
            "cpu",
            "메모리",
            "ram",
            "디스크",
            "storage",
            "용량",
            "load",
            "loadavg",
            "uptime",
            "프로세스",
            "process",
            "포트",
            "port",
            "서비스",
            "service",
            "로그",
            "log",
            "docker",
            "컨테이너",
            "pod",
            "k8s",
            "쿠버네티스",
            "워크스페이스",
            "파일",
            "폴더",
            "디렉터리",
            "코드",
            "빌드",
            "테스트",
            "git",
            "브랜치",
            "커밋",
            "macos",
            "linux",
            "운영체제",
            "hostname",
            "호스트"
        );
    }

    private static string NormalizeRoutineExecutionMode(string? executionMode)
    {
        var normalized = (executionMode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "web" => "web",
            "url" => "url",
            "script" => "script",
            "browser_agent" => "browser_agent",
            LogicGraphExecutionMode => LogicGraphExecutionMode,
            _ => string.Empty
        };
    }

    private static string ResolveRoutineExecutionMode(string? request, string? executionMode)
    {
        var normalizedExecutionMode = NormalizeRoutineExecutionMode(executionMode);
        if (!string.IsNullOrWhiteSpace(normalizedExecutionMode))
        {
            return normalizedExecutionMode;
        }

        var normalizedRequest = (request ?? string.Empty).Trim();
        var urls = ResolveWebUrls(normalizedRequest, null, webSearchEnabled: true);
        if (urls.Count > 0)
        {
            return "url";
        }

        return LooksLikeRoutineWebLookupRequest(normalizedRequest)
            ? "web"
            : "script";
    }

    private static bool TryNormalizeRoutineAgentStartUrl(string? raw, out string? normalized)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            normalized = null;
            return true;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            normalized = uri.ToString();
            return true;
        }

        normalized = null;
        return false;
    }

    private static string NormalizeRoutineAgentStartUrlOrEmpty(string? raw)
    {
        return TryNormalizeRoutineAgentStartUrl(raw, out var normalized) && !string.IsNullOrWhiteSpace(normalized)
            ? normalized!
            : string.Empty;
    }

    private static string? NormalizeRoutineAgentProvider(string? provider, string? model)
    {
        var normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "codex" or "openai")
        {
            return RoutineBrowserAgentDefaultProvider;
        }

        var modelText = NormalizeRoutineAgentModel(model);
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return RoutineBrowserAgentDefaultProvider;
        }

        return RoutineBrowserAgentDefaultProvider;
    }

    private static string? NormalizeRoutineAgentModel(string? model)
    {
        var normalized = (model ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? RoutineBrowserAgentDefaultModel : normalized;
    }

    private static int? NormalizeRoutineAgentTimeoutSeconds(int? timeoutSeconds)
    {
        if (!timeoutSeconds.HasValue)
        {
            return RoutineBrowserAgentDefaultTimeoutSeconds;
        }

        return Math.Clamp(timeoutSeconds.Value, RoutineBrowserAgentMinTimeoutSeconds, RoutineBrowserAgentMaxTimeoutSeconds);
    }

    private static bool NormalizeRoutineAgentUsePlaywright(bool? value)
    {
        return true;
    }

    private static bool NormalizeRoutineNotifyTelegram(bool? value, bool fallback = true)
    {
        return value ?? fallback;
    }

    private static string NormalizeRoutineAgentToolProfile(string? value, bool? agentUsePlaywright = null)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return RoutineBrowserAgentToolProfilePlaywrightOnly;
        }

        return normalized switch
        {
            "desktop_control" or "desktop-control" => RoutineBrowserAgentToolProfileDesktopControl,
            "playwright_only" or "playwright-only" or "playwright" => RoutineBrowserAgentToolProfilePlaywrightOnly,
            _ => agentUsePlaywright == false
                ? RoutineBrowserAgentToolProfilePlaywrightOnly
                : normalized
        };
    }

    private static bool IsSupportedRoutineAgentToolProfile(string? profile)
    {
        var normalized = NormalizeRoutineAgentToolProfile(profile);
        return !string.IsNullOrWhiteSpace(normalized)
            && RoutineBrowserAgentSupportedToolProfiles.Contains(normalized);
    }

    private static string BuildRoutineBrowserAgentUnsupportedToolProfileMessage(string? profile)
    {
        var normalized = NormalizeRoutineAgentToolProfile(profile);
        return $"브라우저 에이전트 도구 프로필 '{normalized}'은 현재 지원되지 않습니다. 사용 가능: {string.Join(", ", RoutineBrowserAgentSupportedToolProfiles)}";
    }

    private static string BuildRoutineToolProfileLabel(string? profile)
    {
        return NormalizeRoutineAgentToolProfile(profile) switch
        {
            RoutineBrowserAgentToolProfileDesktopControl => "desktop-control",
            _ => "playwright-only"
        };
    }

    private static bool IsRoutineDesktopControlSupported()
    {
        return OperatingSystem.IsMacOS();
    }

    private string EnsureRoutineBrowserAgentAssetDirectory(string routineId, DateTimeOffset startedAtUtc)
    {
        var safeRoutineId = (routineId ?? string.Empty).Trim();
        var runToken = startedAtUtc.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var assetDir = Path.Combine(_config.WorkspaceRootDir, "routines", safeRoutineId, "assets", runToken);
        Directory.CreateDirectory(assetDir);
        return assetDir;
    }

    private static bool IsSupportedRoutineBrowserAgentModel(string? model)
    {
        var normalized = NormalizeRoutineAgentModel(model);
        return !string.IsNullOrWhiteSpace(normalized)
            && RoutineBrowserAgentSupportedModels.Contains(normalized);
    }

    private static string BuildRoutineBrowserAgentUnsupportedModelMessage(string? model)
    {
        var normalized = NormalizeRoutineAgentModel(model) ?? "-";
        return $"브라우저 에이전트 모델 '{normalized}'은 현재 지원되지 않습니다. 사용 가능: {string.Join(", ", RoutineBrowserAgentSupportedModels)}";
    }

    private string ResolveRoutineLlmModel(string mode)
    {
        return string.Equals(mode, "gemini-url-single", StringComparison.Ordinal)
            ? ResolveUrlContextLlmModel()
            : ResolveSearchLlmModel();
    }

    private static string BuildRoutineExecutionPreview(
        string mode,
        string? agentProvider,
        string? agentModel,
        string? agentStartUrl,
        string? agentToolProfile
    )
    {
        return string.Equals(mode, "browser_agent", StringComparison.Ordinal)
            ? $"이 루틴은 브라우저 에이전트로 실행합니다. provider={agentProvider ?? "acp"} model={agentModel ?? "-"} startUrl={agentStartUrl ?? "(요청 원문 URL 사용)"} toolProfile={BuildRoutineToolProfileLabel(agentToolProfile)}"
            : string.Equals(mode, "gemini-url-single", StringComparison.Ordinal)
            ? "이 루틴은 실행 시 gemini-url-single 경로로 URL 참조 답변을 생성합니다."
            : string.Equals(mode, "gemini-web-single", StringComparison.Ordinal)
                ? "이 루틴은 실행 시 gemini-web-single 경로로 최신 웹검색 답변을 생성합니다."
                : "이 루틴은 실행 시 생성된 스크립트를 수행합니다.";
    }

    private async Task<(string Output, string Status, string? Error)> ExecuteRoutineLlmRouteAsync(
        RoutineDefinition routine,
        string mode,
        IReadOnlyList<string> urls,
        string source,
        CancellationToken cancellationToken
    )
    {
        if (!_llmRouter.HasGeminiApiKey())
        {
            var missingKeyMessage = string.Equals(mode, "gemini-url-single", StringComparison.Ordinal)
                ? "Gemini API 키가 설정되지 않아 URL 참조 루틴을 실행할 수 없습니다."
                : "Gemini API 키가 설정되지 않아 웹검색 루틴을 실행할 수 없습니다.";
            return (missingKeyMessage, "error", missingKeyMessage);
        }

        var enforceTelegramOutputStyle = source.Equals("telegram", StringComparison.OrdinalIgnoreCase)
            || (IsRoutineScheduledSource(source) && routine.NotifyTelegram);
        var taskRequest = ResolveRoutineExecutionRequestText(routine.Request, routine.Title, routine.ScheduleSourceMode);

        if (string.Equals(mode, "gemini-url-single", StringComparison.Ordinal))
        {
            var urlResult = await GenerateGeminiUrlContextAnswerDetailedAsync(
                taskRequest,
                urls,
                string.Empty,
                allowMarkdownTable: true,
                enforceTelegramOutputStyle,
                streamCallback: null,
                scope: "routine",
                mode: "single",
                conversationId: routine.Id,
                decisionPath: "routine_url_context",
                decisionMs: 0,
                cancellationToken
            );
            var output = (urlResult.Response.Text ?? string.Empty).Trim();
            var failed = IsGeminiUrlContextFailureText(output)
                || output.StartsWith("요청하신 URL 참조 답변을 생성하지 못했습니다.", StringComparison.Ordinal);
            return (output, failed ? "error" : "ok", failed ? output : null);
        }

        var webResult = await ComposeGroundedWebAnswerWithFallbackAsync(
            taskRequest,
            string.Empty,
            false,
            true,
            enforceTelegramOutputStyle,
            null,
            "routine",
            "single",
            routine.Id,
            "routine_web",
            0,
            source,
            cancellationToken
        );
        var webOutput = (webResult.Response.Text ?? string.Empty).Trim();
        var webFailed = IsGroundedWebAnswerFailureText(webOutput);
        return (webOutput, webFailed ? "error" : "ok", webFailed ? webOutput : null);
    }

    private Task<RoutineExecutionOutcome> ExecuteRoutineBrowserAgentAsync(
        RoutineDefinition routine,
        string taskRequest,
        IReadOnlyList<string> detectedUrls,
        string assetDirectory,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new RoutineExecutionOutcome(
                "브라우저 에이전트 실행 전에 요청이 취소되었습니다.",
                "error",
                "브라우저 에이전트 실행 전에 요청이 취소되었습니다."
            ));
        }

        var toolProfile = NormalizeRoutineAgentToolProfile(routine.AgentToolProfile, routine.AgentUsePlaywright);
        if (toolProfile == RoutineBrowserAgentToolProfileDesktopControl && !IsRoutineDesktopControlSupported())
        {
            toolProfile = RoutineBrowserAgentToolProfilePlaywrightOnly;
        }

        var startUrl = !string.IsNullOrWhiteSpace(routine.AgentStartUrl)
            ? routine.AgentStartUrl!.Trim()
            : detectedUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(startUrl))
        {
            const string missingStartUrl = "브라우저 에이전트 루틴은 시작 URL이 필요합니다. 시작 URL을 지정하거나 요청 원문에 URL을 포함하세요.";
            return Task.FromResult(new RoutineExecutionOutcome(missingStartUrl, "error", missingStartUrl));
        }

        var agentModel = NormalizeRoutineAgentModel(routine.AgentModel);
        if (string.IsNullOrWhiteSpace(agentModel))
        {
            const string missingModel = "브라우저 에이전트 루틴은 에이전트 모델이 필요합니다.";
            return Task.FromResult(new RoutineExecutionOutcome(missingModel, "error", missingModel));
        }

        var prompt = BuildRoutineBrowserAgentTaskPrompt(
            taskRequest,
            startUrl,
            detectedUrls,
            toolProfile,
            assetDirectory
        );
        var timeoutSeconds = NormalizeRoutineAgentTimeoutSeconds(routine.AgentTimeoutSeconds) ?? RoutineBrowserAgentDefaultTimeoutSeconds;
        var spawnResult = _sessionSpawnTool.Spawn(
            task: prompt,
            label: $"routine-browser-{routine.Title}",
            runtime: "acp",
            runTimeoutSeconds: timeoutSeconds,
            timeoutSeconds: timeoutSeconds,
            thread: false,
            mode: "run",
            acpModel: agentModel,
            acpThinking: null,
            acpLightContext: false,
            acpToolProfile: toolProfile,
            acpOutputDirectory: assetDirectory
        );
        if (!string.Equals(spawnResult.Status, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            var spawnError = string.IsNullOrWhiteSpace(spawnResult.Error)
                ? "브라우저 에이전트 세션 시작에 실패했습니다."
                : $"브라우저 에이전트 세션 시작 실패: {spawnResult.Error}";
            return Task.FromResult(new RoutineExecutionOutcome(spawnError, "error", spawnError));
        }

        var childSession = _conversationStore.Get(spawnResult.ChildSessionKey);
        var transcriptResult = TryExtractRoutineBrowserAgentResult(childSession);
        var allowScreenshotResult = IsRoutineBrowserAgentScreenshotRequested(taskRequest);
        var resolvedArtifacts = ResolveRoutineBrowserAgentArtifacts(
            assetDirectory,
            transcriptResult.ScreenshotPath,
            transcriptResult.DownloadPaths,
            allowScreenshotResult
        );
        var agentMetadata = new RoutineAgentExecutionMetadata(
            spawnResult.ChildSessionKey,
            spawnResult.RunId,
            NormalizeRoutineAgentProvider(routine.AgentProvider, routine.AgentModel) ?? "acp",
            agentModel,
            BuildRoutineToolProfileLabel(toolProfile),
            startUrl,
            transcriptResult.FinalUrl,
            transcriptResult.PageTitle,
            resolvedArtifacts.ScreenshotPath,
            resolvedArtifacts.DownloadPaths
        );

        if (string.IsNullOrWhiteSpace(transcriptResult.Output))
        {
            var noResultMessage = BuildRoutineBrowserAgentMissingResultMessage(startUrl, assetDirectory, spawnResult, childSession);
            return Task.FromResult(new RoutineExecutionOutcome(noResultMessage, "error", noResultMessage, agentMetadata));
        }

        if (IsRoutineBrowserAgentPlaceholderReply(transcriptResult.Output))
        {
            var placeholderMessage = BuildRoutineBrowserAgentMissingResultMessage(startUrl, assetDirectory, spawnResult, childSession);
            return Task.FromResult(new RoutineExecutionOutcome(placeholderMessage, "error", placeholderMessage, agentMetadata));
        }

        var downloadValidationError = ValidateRoutineBrowserAgentDownloads(
            taskRequest,
            resolvedArtifacts.DownloadPaths
        );
        if (!string.IsNullOrWhiteSpace(downloadValidationError))
        {
            var message = string.IsNullOrWhiteSpace(transcriptResult.Output)
                ? downloadValidationError
                : $"{transcriptResult.Output.Trim()}\n\n[download_validation]\n{downloadValidationError}";
            return Task.FromResult(new RoutineExecutionOutcome(message, "error", downloadValidationError, agentMetadata));
        }

        return Task.FromResult(new RoutineExecutionOutcome(transcriptResult.Output, "ok", null, agentMetadata));
    }

    private static string BuildRoutineBrowserAgentTaskPrompt(
        string request,
        string startUrl,
        IReadOnlyList<string> detectedUrls,
        string toolProfile,
        string assetDirectory
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("너는 루틴 전용 브라우저 에이전트다.");
        builder.AppendLine("- 브라우저 자동화는 Playwright 계열 도구만 사용한다.");
        builder.AppendLine("- 검색 엔진을 임의로 새로 열지 말고, 주어진 시작 URL과 그 안에서 도달 가능한 공개 페이지 범위만 사용한다.");
        builder.AppendLine($"- 모든 스크린샷과 다운로드 파일은 반드시 자산 디렉터리('{assetDirectory}') 아래에만 저장한다.");
        if (IsRoutineBrowserAgentScreenshotRequested(request))
        {
            builder.AppendLine("- 사용자가 스크린샷을 요청한 경우에만 스크린샷을 저장하고 결과 메타에 포함한다.");
        }
        else
        {
            builder.AppendLine("- 사용자가 스크린샷을 명시적으로 요청하지 않았다면 스크린샷을 저장하거나 결과 메타에 넣지 않는다.");
        }
        if (NormalizeRoutineAgentToolProfile(toolProfile) == RoutineBrowserAgentToolProfileDesktopControl)
        {
            builder.AppendLine("- Playwright로 해결되지 않으면 desktop_control 도구로 화면 캡처, 클릭, 입력, 스크롤을 수행할 수 있다.");
            builder.AppendLine("- 로그인과 다운로드를 허용한다. 단, 임의 shell/운영체제 명령 실행으로 우회하지 말고 Playwright와 desktop_control 도구만 사용한다.");
        }
        else
        {
            builder.AppendLine("- 파일 다운로드, 로그인 시도, 데스크톱 전체 제어, 운영체제 조작은 금지한다.");
        }
        builder.AppendLine("- 실패하면 어디에서 막혔는지 명확히 설명한다.");
        builder.AppendLine("- 최종 답변은 한국어로 작성한다.");
        builder.AppendLine();
        builder.AppendLine($"시작 URL: {startUrl}");
        builder.AppendLine($"도구 프로필: {BuildRoutineToolProfileLabel(toolProfile)}");
        builder.AppendLine($"자산 디렉터리: {assetDirectory}");
        if (detectedUrls.Count > 0)
        {
            builder.AppendLine("요청 원문에서 감지한 URL:");
            foreach (var url in detectedUrls.Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
            {
                builder.AppendLine($"- {url}");
            }
        }
        var requestedFiles = ExtractRoutineBrowserAgentRequestedFileHints(request);
        if (LooksLikeRoutineBrowserAgentDownloadRequest(request, requestedFiles))
        {
            builder.AppendLine("- 이번 요청의 성공 기준은 실제 파일 다운로드다.");
            builder.AppendLine("- 페이지 저장(Save Page As), PDF 저장, 스크린샷, 페이지 내용을 복사해 새 파일을 만드는 것은 다운로드로 간주하지 않는다.");
            if (IsRoutineBrowserAgentGitHubContext(startUrl, detectedUrls))
            {
                builder.AppendLine("- GitHub에서는 blob 렌더링 페이지를 저장하지 말고 Raw 또는 Download raw file 동작으로 원본 파일을 내려받아라.");
            }
            if (requestedFiles.Count > 0)
            {
                builder.AppendLine($"- 대상 파일 후보: {string.Join(", ", requestedFiles)}");
            }
            var gitHubRawCandidates = BuildRoutineBrowserAgentGitHubRawCandidates(startUrl, detectedUrls, requestedFiles);
            if (gitHubRawCandidates.Count > 0)
            {
                builder.AppendLine("- GitHub raw 다운로드 후보 URL:");
                foreach (var candidate in gitHubRawCandidates)
                {
                    builder.AppendLine($"- {candidate}");
                }
                builder.AppendLine("- 위 후보 URL을 우선 시도하고, 404면 다음 후보를 사용한다.");
            }
            builder.AppendLine("- 다운로드 후 파일명과 파일 내용 앞부분을 확인해 HTML 페이지가 아니라 요청한 실제 파일인지 검증한다.");
        }

        builder.AppendLine();
        builder.AppendLine("사용자 요청:");
        builder.AppendLine(request);
        builder.AppendLine();
        builder.AppendLine("응답 형식:");
        builder.AppendLine("1. 먼저 사용자가 바로 읽을 수 있는 최종 결과를 완결된 한국어로 작성한다.");
        builder.AppendLine("2. 마지막에는 아래 메타 블록을 정확히 추가한다.");
        builder.AppendLine("[ROUTINE_AGENT_META]");
        builder.AppendLine("final_url: <최종으로 확인한 URL 또는 ->");
        builder.AppendLine("page_title: <최종 페이지 제목 또는 ->");
        builder.AppendLine("screenshot_path: <스크린샷을 요청한 경우에만 절대경로, 아니면 ->");
        builder.AppendLine("download_paths: <다운로드한 절대경로를 | 로 연결하거나 ->");
        builder.AppendLine("[/ROUTINE_AGENT_META]");
        return builder.ToString().Trim();
    }

    private static (string Output, string? FinalUrl, string? PageTitle, string? ScreenshotPath, IReadOnlyList<string> DownloadPaths) TryExtractRoutineBrowserAgentResult(
        ConversationThreadView? childSession
    )
    {
        if (childSession == null || childSession.Messages.Count == 0)
        {
            return (string.Empty, null, null, null, Array.Empty<string>());
        }

        var assistant = childSession.Messages
            .Where(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            .Where(message => !message.Meta.StartsWith("sessions_spawn", StringComparison.OrdinalIgnoreCase))
            .Select(message => (message.Text ?? string.Empty).Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(assistant))
        {
            assistant = childSession.Messages
                .Where(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                .Select(message => (message.Text ?? string.Empty).Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .LastOrDefault();
        }

        if (string.IsNullOrWhiteSpace(assistant))
        {
            return (string.Empty, null, null, null, Array.Empty<string>());
        }

        var finalUrl = ExtractRoutineAgentMetaValue(assistant, "final_url");
        var pageTitle = ExtractRoutineAgentMetaValue(assistant, "page_title");
        var screenshotPath = ExtractRoutineAgentMetaValue(assistant, "screenshot_path");
        var downloadPaths = ParseRoutineAgentDownloadPaths(ExtractRoutineAgentMetaValue(assistant, "download_paths"));
        var cleaned = Regex.Replace(
                assistant,
                @"\[\s*ROUTINE_AGENT_META\s*\][\s\S]*?\[\s*/ROUTINE_AGENT_META\s*\]",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            )
            .Trim();
        if (string.IsNullOrWhiteSpace(screenshotPath))
        {
            screenshotPath = TryExtractScreenshotPathFromText(assistant);
        }

        return (
            string.IsNullOrWhiteSpace(cleaned) ? assistant : cleaned,
            NormalizeOptionalAgentMetaValue(finalUrl),
            NormalizeOptionalAgentMetaValue(pageTitle),
            NormalizeOptionalAgentMetaValue(screenshotPath),
            downloadPaths
        );
    }

    private static string? ExtractRoutineAgentMetaValue(string text, string key)
    {
        var match = Regex.Match(
            text ?? string.Empty,
            $@"^\s*{Regex.Escape(key)}\s*:\s*(.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? TryExtractScreenshotPathFromText(string text)
    {
        var match = Regex.Match(
            text ?? string.Empty,
            @"(/[^ \r\n\t]+?\.(?:png|jpg|jpeg|webp))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static IReadOnlyList<string> ParseRoutineAgentDownloadPaths(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeOptionalAgentMetaValue)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray()!;
    }

    private static (string? ScreenshotPath, IReadOnlyList<string> DownloadPaths) ResolveRoutineBrowserAgentArtifacts(
        string assetDirectory,
        string? reportedScreenshotPath,
        IReadOnlyList<string> reportedDownloadPaths,
        bool allowScreenshotResult
    )
    {
        var normalizedAssetDirectory = (assetDirectory ?? string.Empty).Trim();
        var downloadPaths = new List<string>();
        string? screenshotPath = allowScreenshotResult
            ? NormalizeOptionalAgentMetaValue(reportedScreenshotPath)
            : null;

        if (reportedDownloadPaths != null)
        {
            downloadPaths.AddRange(
                reportedDownloadPaths
                    .Select(NormalizeOptionalAgentMetaValue)
                    .Where(static path => !string.IsNullOrWhiteSpace(path))!
            );
        }

        if (!string.IsNullOrWhiteSpace(normalizedAssetDirectory) && Directory.Exists(normalizedAssetDirectory))
        {
            var files = Directory.GetFiles(normalizedAssetDirectory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
            if (allowScreenshotResult && string.IsNullOrWhiteSpace(screenshotPath))
            {
                screenshotPath = files.FirstOrDefault(IsRoutineBrowserAgentScreenshotFile);
            }

            if (downloadPaths.Count == 0)
            {
                foreach (var file in files)
                {
                    if (!IsRoutineBrowserAgentDownloadCandidateFile(file))
                    {
                        continue;
                    }

                    if (downloadPaths.Contains(file, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    downloadPaths.Add(file);
                }
            }
        }

        return (
            screenshotPath,
            downloadPaths
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        );
    }

    private static bool IsRoutineBrowserAgentScreenshotRequested(string? request)
    {
        var normalized = (request ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("스크린샷", StringComparison.Ordinal)
            || normalized.Contains("screenshot", StringComparison.Ordinal)
            || normalized.Contains("screen shot", StringComparison.Ordinal)
            || normalized.Contains("화면 캡처", StringComparison.Ordinal)
            || normalized.Contains("snapshot", StringComparison.Ordinal);
    }

    private static bool LooksLikeRoutineBrowserAgentDownloadRequest(string? request, IReadOnlyList<string>? requestedFiles = null)
    {
        var normalized = (request ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (requestedFiles != null && requestedFiles.Count > 0)
        {
            return true;
        }

        return normalized.Contains("다운로드", StringComparison.Ordinal)
            || normalized.Contains("download", StringComparison.Ordinal)
            || normalized.Contains("받아", StringComparison.Ordinal)
            || normalized.Contains("저장해", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ExtractRoutineBrowserAgentRequestedFileHints(string? request)
    {
        var normalized = (request ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<string>();
        }

        var matches = Regex.Matches(
            normalized,
            @"(?<![:/\w-])(?<path>(?:[\w.-]+/)*[\w.-]+\.[A-Za-z0-9]{1,16})(?![\w-])",
            RegexOptions.CultureInvariant
        );
        if (matches.Count == 0)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            var raw = match.Groups["path"].Value.Trim();
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(raw);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static bool IsRoutineBrowserAgentGitHubContext(string startUrl, IReadOnlyList<string> detectedUrls)
    {
        if (IsRoutineBrowserAgentGitHubUrl(startUrl))
        {
            return true;
        }

        return detectedUrls.Any(IsRoutineBrowserAgentGitHubUrl);
    }

    private static bool IsRoutineBrowserAgentGitHubUrl(string? url)
    {
        var normalized = (url ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildRoutineBrowserAgentGitHubRawCandidates(
        string startUrl,
        IReadOnlyList<string> detectedUrls,
        IReadOnlyList<string> requestedFiles
    )
    {
        if (requestedFiles == null || requestedFiles.Count == 0)
        {
            return Array.Empty<string>();
        }

        var candidateUrls = new List<string> { startUrl };
        if (detectedUrls != null)
        {
            candidateUrls.AddRange(detectedUrls);
        }

        foreach (var url in candidateUrls)
        {
            if (!TryParseRoutineBrowserAgentGitHubRepositoryUrl(url, out var owner, out var repo, out var gitRef, out var blobPath))
            {
                continue;
            }

            var targetPaths = requestedFiles;
            if (!string.IsNullOrWhiteSpace(blobPath))
            {
                targetPaths = new[] { blobPath! };
            }

            var rawCandidates = new List<string>();
            foreach (var targetPath in targetPaths)
            {
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(gitRef))
                {
                    rawCandidates.Add(BuildRoutineBrowserAgentGitHubRawUrl(owner, repo, gitRef!, targetPath));
                    continue;
                }

                rawCandidates.Add(BuildRoutineBrowserAgentGitHubRawUrl(owner, repo, "main", targetPath));
                rawCandidates.Add(BuildRoutineBrowserAgentGitHubRawUrl(owner, repo, "master", targetPath));
            }

            return rawCandidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static bool TryParseRoutineBrowserAgentGitHubRepositoryUrl(
        string? rawUrl,
        out string owner,
        out string repo,
        out string? gitRef,
        out string? blobPath
    )
    {
        owner = string.Empty;
        repo = string.Empty;
        gitRef = null;
        blobPath = null;

        var normalized = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || !IsRoutineBrowserAgentGitHubUrl(normalized))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        owner = segments[0];
        repo = segments[1];
        if (segments.Length >= 5 && string.Equals(segments[2], "blob", StringComparison.OrdinalIgnoreCase))
        {
            gitRef = segments[3];
            blobPath = string.Join('/', segments.Skip(4));
        }

        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }

    private static string BuildRoutineBrowserAgentGitHubRawUrl(
        string owner,
        string repo,
        string gitRef,
        string path
    )
    {
        var escapedPath = string.Join(
            "/",
            (path ?? string.Empty)
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.EscapeDataString)
        );
        return $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/raw/{Uri.EscapeDataString(gitRef)}/{escapedPath}";
    }

    private static string? ValidateRoutineBrowserAgentDownloads(
        string? request,
        IReadOnlyList<string> downloadPaths
    )
    {
        var requestedFiles = ExtractRoutineBrowserAgentRequestedFileHints(request);
        if (!LooksLikeRoutineBrowserAgentDownloadRequest(request, requestedFiles))
        {
            return null;
        }

        var normalizedPaths = (downloadPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return "파일 다운로드 요청이었지만 실제 다운로드 파일이 저장되지 않았습니다.";
        }

        foreach (var path in normalizedPaths)
        {
            if (!File.Exists(path))
            {
                return $"다운로드 파일이 결과 메타에 기록됐지만 실제 파일이 없습니다: {path}";
            }
        }

        if (requestedFiles.Count > 0)
        {
            var downloadedNames = normalizedPaths
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requestedNames = requestedFiles
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (requestedNames.Length > 0 && !requestedNames.Any(downloadedNames.Contains))
            {
                return $"요청한 파일 후보({string.Join(", ", requestedNames)})와 일치하는 다운로드 파일이 없습니다.";
            }
        }

        foreach (var path in normalizedPaths)
        {
            if (!LooksLikeRoutineBrowserAgentDownloadedFileContent(path))
            {
                return $"다운로드 파일 검증에 실패했습니다. HTML 페이지 저장물로 보이는 파일: {path}";
            }
        }

        return null;
    }

    private static bool LooksLikeRoutineBrowserAgentDownloadedFileContent(string path)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !File.Exists(normalized))
        {
            return false;
        }

        var extension = Path.GetExtension(normalized);
        if (!IsRoutineBrowserAgentInspectableDownloadExtension(extension))
        {
            return true;
        }

        try
        {
            using var stream = File.OpenRead(normalized);
            if (stream.Length == 0)
            {
                return false;
            }

            var buffer = new byte[Math.Min(1024, (int)Math.Min(stream.Length, 1024L))];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return false;
            }

            var prefix = Encoding.UTF8.GetString(buffer, 0, read)
                .TrimStart('\uFEFF', ' ', '\t', '\r', '\n')
                .ToLowerInvariant();
            return !(prefix.StartsWith("<!doctype html", StringComparison.Ordinal)
                || prefix.StartsWith("<html", StringComparison.Ordinal)
                || prefix.StartsWith("<head", StringComparison.Ordinal)
                || prefix.StartsWith("<body", StringComparison.Ordinal)
                || prefix.StartsWith("<meta", StringComparison.Ordinal));
        }
        catch
        {
            return true;
        }
    }

    private static bool IsRoutineBrowserAgentInspectableDownloadExtension(string? extension)
    {
        var normalized = (extension ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            ".md" => true,
            ".txt" => true,
            ".json" => true,
            ".csv" => true,
            ".tsv" => true,
            ".xml" => true,
            ".yaml" => true,
            ".yml" => true,
            ".html" => true,
            ".htm" => true,
            ".js" => true,
            ".ts" => true,
            ".css" => true,
            _ => false
        };
    }

    private static bool IsRoutineBrowserAgentScreenshotFile(string? path)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var extension = Path.GetExtension(normalized);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoutineBrowserAgentDownloadCandidateFile(string? path)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (IsRoutineBrowserAgentScreenshotFile(normalized))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var normalizedFileName = fileName.Trim().ToLowerInvariant();
        if (normalizedFileName.StartsWith("console-", StringComparison.Ordinal)
            || normalizedFileName.StartsWith("page-", StringComparison.Ordinal)
            || normalizedFileName.StartsWith("network-", StringComparison.Ordinal)
            || normalizedFileName.StartsWith("trace-", StringComparison.Ordinal)
            || normalizedFileName.StartsWith("video-", StringComparison.Ordinal))
        {
            return false;
        }

        return normalizedFileName switch
        {
            "session.json" => false,
            _ when normalizedFileName.EndsWith(".yml", StringComparison.Ordinal) => false,
            _ when normalizedFileName.EndsWith(".yaml", StringComparison.Ordinal) => false,
            _ when normalizedFileName.EndsWith(".log", StringComparison.Ordinal) => false,
            _ when normalizedFileName.EndsWith(".har", StringComparison.Ordinal) => false,
            _ when normalizedFileName.EndsWith(".webm", StringComparison.Ordinal) => false,
            _ when normalizedFileName.EndsWith(".trace.zip", StringComparison.Ordinal) => false,
            _ => true
        };
    }

    private static string? NormalizeOptionalAgentMetaValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "-")
        {
            return null;
        }

        return normalized;
    }

    private static bool IsRoutineBrowserAgentPlaceholderReply(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.StartsWith("ACP run completed in bridge mode.", StringComparison.Ordinal)
            || normalized.StartsWith("ACP runtime bridge accepted this session", StringComparison.Ordinal)
            || normalized.StartsWith("ACP runtime bridge dispatched the initial task", StringComparison.Ordinal)
            || normalized.StartsWith("ACP persistent session is active.", StringComparison.Ordinal);
    }

    private static string BuildRoutineBrowserAgentMissingResultMessage(
        string startUrl,
        string assetDirectory,
        SessionSpawnToolResult spawnResult,
        ConversationThreadView? childSession
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("브라우저 에이전트 실행은 시작되었지만 최종 결과를 child session에서 찾지 못했습니다.");
        builder.AppendLine($"startUrl={startUrl}");
        if (!string.IsNullOrWhiteSpace(assetDirectory))
        {
            builder.AppendLine($"assetDirectory={assetDirectory}");
        }
        builder.AppendLine($"runId={spawnResult.RunId}");
        builder.AppendLine("hint=ACP command adapter가 없거나 최종 결과를 반환하지 않았습니다.");
        if (!string.IsNullOrWhiteSpace(spawnResult.ChildSessionKey))
        {
            builder.AppendLine($"childSessionKey={spawnResult.ChildSessionKey}");
        }

        if (childSession != null)
        {
            builder.AppendLine($"childMessages={childSession.Messages.Count}");
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeRoutineScriptLanguage(string? language)
    {
        return string.Equals(language, "python", StringComparison.OrdinalIgnoreCase)
            ? "python"
            : "bash";
    }

    private async Task<(string Status, string? Error, string? Fingerprint)> DispatchRoutineResultToTelegramAsync(
        RoutineDefinition? routine,
        string output,
        string source,
        string resultStatus,
        CancellationToken cancellationToken,
        RoutineAgentExecutionMetadata? agentMetadata = null
    )
    {
        if (routine == null || !ShouldSendRoutineResultToTelegram(source))
        {
            return ("not_applicable", null, null);
        }

        var normalizedSource = (source ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedPolicy = NormalizeRoutineNotifyPolicy(routine.NotifyPolicy);
        var fingerprint = ComputeRoutineOutputFingerprint(output);
        var bypassPolicy = normalizedSource is "telegram_test" or "telegram_resend";
        if (!bypassPolicy)
        {
            if (!routine.NotifyTelegram)
            {
                return ("disabled", null, fingerprint);
            }

            if (normalizedPolicy == "never")
            {
                return ("disabled", null, fingerprint);
            }

            if (normalizedPolicy == "error_only" && !string.Equals(resultStatus, "error", StringComparison.OrdinalIgnoreCase))
            {
                return ("policy_skip", null, fingerprint);
            }

            if (normalizedPolicy == "on_change"
                && !string.IsNullOrWhiteSpace(fingerprint)
                && string.Equals(routine.LastNotifiedFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return ("no_change", null, fingerprint);
            }
        }

        if (!_telegramClient.IsConfigured)
        {
            return ("send_failed", "텔레그램 봇이 설정되지 않아 자동 전송하지 못했습니다.", fingerprint);
        }

        try
        {
            var telegramText = BuildRoutineTelegramDeliveryText(routine, output, resultStatus, agentMetadata);
            var sent = await _telegramClient.SendMessageAsync(
                FormatTelegramResponse(telegramText, TelegramMaxResponseChars),
                cancellationToken
            );
            return sent
                ? ("sent", null, fingerprint)
                : ("send_failed", "텔레그램 전송에 실패했습니다.", fingerprint);
        }
        catch (Exception ex)
        {
            return ("send_failed", $"텔레그램 전송 실패: {ex.Message}", fingerprint);
        }
    }

    private static string BuildRoutineTelegramDeliveryText(
        RoutineDefinition routine,
        string output,
        string resultStatus,
        RoutineAgentExecutionMetadata? agentMetadata
    )
    {
        var normalizedOutput = ExtractRoutineArtifactOutputSection(output);
        var executionRequest = ResolveRoutineExecutionRequestText(routine.Request, routine.Title, routine.ScheduleSourceMode);
        var resolvedExecutionMode = ResolveRoutineExecutionMode(executionRequest, routine.ExecutionMode);
        if (string.Equals(resolvedExecutionMode, "script", StringComparison.Ordinal))
        {
            return BuildRoutineScriptTelegramDeliveryText(routine.Title, normalizedOutput, resultStatus);
        }

        if (string.Equals(resolvedExecutionMode, "browser_agent", StringComparison.Ordinal))
        {
            return BuildRoutineBrowserAgentTelegramDeliveryText(
                routine.Title,
                normalizedOutput,
                resultStatus,
                agentMetadata,
                IsRoutineBrowserAgentScreenshotRequested(executionRequest)
            );
        }

        return string.IsNullOrWhiteSpace(normalizedOutput)
            ? routine.Title
            : normalizedOutput.Trim();
    }

    private static string BuildRoutineScriptTelegramDeliveryText(
        string title,
        string output,
        string resultStatus
    )
    {
        var stdout = ExtractRoutineTaggedSection(output, "stdout");
        var stderr = ExtractRoutineTaggedSection(output, "stderr");
        var body = !string.IsNullOrWhiteSpace(stdout) && !string.Equals(stdout, "(출력 없음)", StringComparison.Ordinal)
            ? stdout
            : string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            body = ExtractRoutineScriptBodyFallback(output);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"[{title}]");
        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.AppendLine(body);
        }
        else if (string.Equals(resultStatus, "error", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("실행에 실패했습니다.");
        }
        else
        {
            builder.AppendLine("실행을 완료했습니다. 출력은 없습니다.");
        }

        if (string.Equals(resultStatus, "error", StringComparison.OrdinalIgnoreCase))
        {
            var errorText = !string.IsNullOrWhiteSpace(stderr)
                ? stderr
                : ExtractRoutineTaggedSection(output, "error");
            if (string.IsNullOrWhiteSpace(errorText))
            {
                errorText = ExtractRoutineTaggedSection(output, "exception");
            }

            if (string.IsNullOrWhiteSpace(errorText))
            {
                errorText = ExtractRoutineTaggedSection(output, "artifact");
            }

            if (!string.IsNullOrWhiteSpace(errorText)
                && !string.Equals(errorText, body, StringComparison.Ordinal))
            {
                builder.AppendLine();
                builder.AppendLine("오류:");
                builder.AppendLine(errorText);
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildRoutineBrowserAgentTelegramDeliveryText(
        string title,
        string output,
        string resultStatus,
        RoutineAgentExecutionMetadata? agentMetadata,
        bool includeScreenshot
    )
    {
        var body = NormalizeRoutineBrowserAgentTelegramBody(output);
        var builder = new StringBuilder();
        builder.AppendLine($"[{title}]");
        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.AppendLine(body);
        }
        else if (string.Equals(resultStatus, "error", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("브라우저 작업에 실패했습니다.");
        }
        else
        {
            builder.AppendLine("브라우저 작업을 완료했습니다.");
        }

        var downloadNames = (agentMetadata?.DownloadPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFileName(path.Trim()))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (downloadNames.Length > 0
            && !downloadNames.Any(name => body.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            builder.AppendLine();
            builder.AppendLine(downloadNames.Length == 1
                ? $"다운로드 파일: {downloadNames[0]}"
                : $"다운로드 파일: {string.Join(", ", downloadNames)}");
        }

        var screenshotName = string.IsNullOrWhiteSpace(agentMetadata?.ScreenshotPath)
            ? null
            : Path.GetFileName(agentMetadata!.ScreenshotPath);
        if (includeScreenshot
            && !string.IsNullOrWhiteSpace(screenshotName)
            && !body.Contains(screenshotName, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine($"스크린샷: {screenshotName}");
        }

        return builder.ToString().Trim();
    }

    private static string ExtractRoutineArtifactOutputSection(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var match = Regex.Match(
            normalized,
            @"(?ms)^##\s+Output\s*\n+(?<body>.*)$",
            RegexOptions.CultureInvariant
        );
        return match.Success ? match.Groups["body"].Value.Trim() : normalized;
    }

    private static string? ExtractRoutineTaggedSection(string text, string tag)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var match = Regex.Match(
            text,
            $@"(?ms)^\[{Regex.Escape(tag)}\]\s*\n(?<body>.*?)(?=^\[[^\]]+\]\s*$|\z)",
            RegexOptions.CultureInvariant
        );
        return match.Success ? match.Groups["body"].Value.Trim() : null;
    }

    private static string ExtractRoutineScriptBodyFallback(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized
            .Split('\n', StringSplitOptions.None)
            .Select(static line => line.Trim())
            .Where(static line =>
                !string.IsNullOrWhiteSpace(line)
                && !line.StartsWith("[Routine:", StringComparison.Ordinal)
                && !line.StartsWith("status=", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("model=", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("script=", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("run_dir=", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(line, @"^\[[a-z_]+\]$", RegexOptions.CultureInvariant))
            .ToArray();
        return string.Join('\n', lines).Trim();
    }

    private static string NormalizeRoutineBrowserAgentTelegramBody(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(
            normalized,
            @"\[\s*ROUTINE_AGENT_META\s*\][\s\S]*?\[\s*/ROUTINE_AGENT_META\s*\]",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        ).Trim();

        var lines = normalized
            .Split('\n', StringSplitOptions.None)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Where(static line => !IsRoutineBrowserAgentTelegramNoiseLine(line))
            .Select(NormalizeRoutineBrowserAgentTelegramLine)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        return string.Join('\n', lines).Trim();
    }

    private static bool IsRoutineBrowserAgentTelegramNoiseLine(string line)
    {
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        if (normalized.StartsWith("저장 파일:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("저장 경로:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("스크린샷:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("스크린샷 경로:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("출처 링크:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.StartsWith("finalUrl=", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("pageTitle=", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("screenshotPath=", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("downloadPaths=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(normalized, @"^/[^ \t\r\n]+$", RegexOptions.CultureInvariant);
    }

    private static string NormalizeRoutineBrowserAgentTelegramLine(string line)
    {
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(
            normalized,
            @"^\[(download_validation|error|exception|artifact)\]\s*$",
            "오류:",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        normalized = Regex.Replace(
            normalized,
            @"(/(?:Users|tmp|var|private|Volumes|opt|Applications|Library|System)/[^ \t\r\n]+)",
            static match =>
            {
                var path = match.Groups[1].Value;
                if (!Path.IsPathRooted(path))
                {
                    return path;
                }

                var fileName = Path.GetFileName(path);
                return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
            },
            RegexOptions.CultureInvariant
        );
        return normalized.Trim();
    }

    private static bool ShouldSendRoutineResultToTelegram(string source)
    {
        return IsRoutineScheduledSource(source)
            || source.Equals("telegram_test", StringComparison.OrdinalIgnoreCase)
            || source.Equals("telegram_resend", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoutineScheduledSource(string source)
    {
        return source.Equals("scheduler", StringComparison.OrdinalIgnoreCase)
            || source.Equals("cron-wake", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFallbackRoutineRunDetailContent(RoutineDefinition routine, RoutineRunLogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[Routine:{routine.Id}] {routine.Title}");
        builder.AppendLine($"status={entry.Status ?? "-"} source={entry.Source ?? entry.Action ?? "-"} attempts={Math.Max(1, entry.AttemptCount).ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(entry.TelegramStatus))
        {
            builder.AppendLine($"telegram={entry.TelegramStatus}");
        }

        if (!string.IsNullOrWhiteSpace(entry.AgentSessionId))
        {
            builder.AppendLine($"agentSessionId={entry.AgentSessionId}");
        }

        if (!string.IsNullOrWhiteSpace(entry.AgentRunId))
        {
            builder.AppendLine($"agentRunId={entry.AgentRunId}");
        }

        if (!string.IsNullOrWhiteSpace(entry.AgentProvider) || !string.IsNullOrWhiteSpace(entry.AgentModel))
        {
            builder.AppendLine($"agent={entry.AgentProvider ?? "-"}:{entry.AgentModel ?? "-"}");
        }

        if (!string.IsNullOrWhiteSpace(entry.ToolProfile))
        {
            builder.AppendLine($"toolProfile={entry.ToolProfile}");
        }

        if (!string.IsNullOrWhiteSpace(entry.StartUrl))
        {
            builder.AppendLine($"startUrl={entry.StartUrl}");
        }

        if (!string.IsNullOrWhiteSpace(entry.FinalUrl))
        {
            builder.AppendLine($"finalUrl={entry.FinalUrl}");
        }

        if (!string.IsNullOrWhiteSpace(entry.PageTitle))
        {
            builder.AppendLine($"pageTitle={entry.PageTitle}");
        }

        if (!string.IsNullOrWhiteSpace(entry.ScreenshotPath))
        {
            builder.AppendLine($"screenshotPath={entry.ScreenshotPath}");
        }

        if (entry.DownloadPaths != null && entry.DownloadPaths.Count > 0)
        {
            builder.AppendLine($"downloadPaths={string.Join(" | ", entry.DownloadPaths)}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Error))
        {
            builder.AppendLine();
            builder.AppendLine("[error]");
            builder.AppendLine(entry.Error);
        }

        if (!string.IsNullOrWhiteSpace(entry.Summary))
        {
            builder.AppendLine();
            builder.AppendLine("[summary]");
            builder.AppendLine(entry.Summary);
        }

        return builder.ToString().Trim();
    }
}
