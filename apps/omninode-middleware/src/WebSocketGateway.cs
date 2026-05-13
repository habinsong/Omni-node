using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class WebSocketGateway
{
    private const int DefaultTrustedAuthTtlHours = 24;
    private const int MinTrustedAuthTtlHours = 1;
    private const int MaxTrustedAuthTtlHours = 168;
    private const string WebSocketPath = "/ws/";
    private const string GuardRetryTimelineApiPath = "/api/guard/retry-timeline";
    private const string LocalImageApiPath = "/api/local-image";
    private const string GuardAlertDispatchMessageType = "dispatch_guard_alert";
    private const string GuardAlertDispatchResultType = "guard_alert_dispatch_result";
    private const string GuardAlertSchemaVersion = "guard_alert_event.v1";
    private const string GuardAlertEventType = "omninode.guard_alert.summary";
    private const string CitationValidationBlockedToken = "근거 인용 검증에 실패하여 fail-closed 정책으로 답변을 차단했습니다";
    private static readonly Regex AnswerGuardBlockedPattern = new(
        @"answer-guard blocked\s*\(\s*category=(?<category>[^,\)]+)\s*,\s*reason=(?<reason>[^,\)]+)(?:\s*,\s*detail=(?<detail>.*?))?(?:\s*,\s*target=\d+\s*,\s*collected=\d+)?\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex GroundedFailureReasonPattern = new(
        @"원인\s*코드:\s*(?<reason>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex GroundedFailureTerminationPattern = new(
        @"종료\s*코드:\s*(?<termination>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex GroundedFailureDetailPattern = new(
        @"상세:\s*(?<detail>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly HttpClient GuardAlertPipelineHttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly AppConfig _config;
    private readonly int _port;
    private readonly IAuthSessionStore _sessionManager;
    private readonly TelegramClient _telegramClient;
    private readonly ICommandExecutionService _commandExecutionService;
    private readonly ISettingsApplicationService _settingsService;
    private readonly IConversationApplicationService _conversationService;
    private readonly IMemoryApplicationService _memoryService;
    private readonly IToolApplicationService _toolService;
    private readonly IRoutineApplicationService _routineService;
    private readonly ILogicApplicationService _logicService;
    private readonly IPlanningApplicationService _planService;
    private readonly IRefactorApplicationService _refactorService;
    private readonly IContextApplicationService _contextService;
    private readonly INotebookApplicationService _notebookService;
    private readonly IChatApplicationService _chatService;
    private readonly ICodingApplicationService _codingService;
    private readonly LlmRouter _llmRouter;
    private readonly GroqModelCatalog _groqModelCatalog;
    private readonly CerebrasModelCatalog _cerebrasModelCatalog;
    private readonly GuardRetryTimelineStore _guardRetryTimelineStore;
    private readonly AuditLogger _auditLogger;
    private readonly HttpStaticFileEndpoint _staticFileEndpoint;
    private readonly GatewayApiEndpoint _apiEndpoint;
    private readonly AuthSessionGateway _authSessionGateway;
    private readonly WsSetupCommandDispatcher _setupCommandDispatcher;
    private readonly WsConversationMemoryDispatcher _conversationMemoryDispatcher;
    private readonly WsToolCommandDispatcher _toolCommandDispatcher;
    private readonly WsRoutineCommandDispatcher _routineCommandDispatcher;
    private readonly WsLogicCommandDispatcher _logicCommandDispatcher;
    private readonly WsDoctorCommandDispatcher _doctorCommandDispatcher;
    private readonly WsPlanningCommandDispatcher _planningCommandDispatcher;
    private readonly WsTaskCommandDispatcher _taskCommandDispatcher;
    private readonly WsRefactorCommandDispatcher _refactorCommandDispatcher;
    private readonly WsContextCommandDispatcher _contextCommandDispatcher;
    private readonly WsNotebookCommandDispatcher _notebookCommandDispatcher;
    private readonly WsAiCommandDispatcher _aiCommandDispatcher;
    private readonly Dictionary<string, RateWindow> _sessionRateMap = new();
    private readonly object _rateLock = new();
    private readonly object _gatewayHealthLock = new();
    private long _webSocketAcceptedCount;
    private long _webSocketRoundTripCount;
    private long _lastWebSocketAcceptedUnixMs;
    private long _lastWebSocketRoundTripUnixMs;
    private string _gatewayHealthStatus = "starting";
    private string _gatewayListenerPrefix = string.Empty;
    private bool _gatewayListenerBound;
    private bool _gatewayDegradedMode;
    private int? _gatewayListenerErrorCode;
    private string? _gatewayListenerErrorMessage;
    private Action? _requestListenerRebind;

    private sealed record GuardAlertDispatchTargetResult(
        string Name,
        string Status,
        int Attempts,
        int? StatusCode,
        string Error,
        string Endpoint
    );

    private sealed record GuardAlertDispatchResult(
        bool Ok,
        string Status,
        string Message,
        string SchemaVersion,
        string EventType,
        string AttemptedAtUtc,
        IReadOnlyList<GuardAlertDispatchTargetResult> Targets
    );

    public WebSocketGateway(
        AppConfig config,
        int port,
        IAuthSessionStore sessionManager,
        TelegramClient telegramClient,
        ICommandExecutionService commandExecutionService,
        ISettingsApplicationService settingsService,
        IConversationApplicationService conversationService,
        IMemoryApplicationService memoryService,
        IToolApplicationService toolService,
        IRoutineApplicationService routineService,
        ILogicApplicationService logicService,
        IDoctorApplicationService doctorService,
        IPlanningApplicationService planService,
        ITaskGraphApplicationService taskGraphService,
        IRefactorApplicationService refactorService,
        IContextApplicationService contextService,
        INotebookApplicationService notebookService,
        IChatApplicationService chatService,
        ICodingApplicationService codingService,
        LlmRouter llmRouter,
        GroqModelCatalog groqModelCatalog,
        CerebrasModelCatalog cerebrasModelCatalog,
        GuardRetryTimelineStore guardRetryTimelineStore,
        AuditLogger auditLogger
    )
    {
        _config = config;
        _port = port;
        _sessionManager = sessionManager;
        _telegramClient = telegramClient;
        _commandExecutionService = commandExecutionService;
        _settingsService = settingsService;
        _conversationService = conversationService;
        _memoryService = memoryService;
        _toolService = toolService;
        _routineService = routineService;
        _logicService = logicService;
        _planService = planService;
        _refactorService = refactorService;
        _contextService = contextService;
        _notebookService = notebookService;
        _chatService = chatService;
        _codingService = codingService;
        _llmRouter = llmRouter;
        _groqModelCatalog = groqModelCatalog;
        _cerebrasModelCatalog = cerebrasModelCatalog;
        _guardRetryTimelineStore = guardRetryTimelineStore;
        _auditLogger = auditLogger;
        _staticFileEndpoint = new HttpStaticFileEndpoint(config.DashboardIndexPath);
        _apiEndpoint = new GatewayApiEndpoint(guardRetryTimelineStore, conversationService);
        _authSessionGateway = new AuthSessionGateway(sessionManager, telegramClient, config.EnableLocalOtpFallback);
        _setupCommandDispatcher = new WsSetupCommandDispatcher(
            settingsService,
            groqModelCatalog,
            cerebrasModelCatalog,
            llmRouter,
            SendSettingsStateAsync,
            SendGroqModelsAsync,
            SendCerebrasModelsAsync,
            SendCopilotModelsAsync,
            (socket, sendLock, token, forceRefresh) => SendUsageStatsAsync(socket, sendLock, token, forceRefresh),
            SendRoutingPolicyResultAsync,
            SendRoutingDecisionAsync,
            RequestListenerRebind
        );
        _conversationMemoryDispatcher = new WsConversationMemoryDispatcher(
            conversationService,
            memoryService,
            SendConversationsAsync,
            SendConversationDetailAsync,
            SendMemoryNotesAsync,
            SendMemorySearchResultAsync,
            SendMemoryGetResultAsync,
            BuildMemoryNoteJson
        );
        _toolCommandDispatcher = new WsToolCommandDispatcher(
            toolService,
            commandExecutionService,
            AllowCommand,
            rawJson => BuildCronAddJobJson(rawJson),
            rawJson => BuildCronUpdatePatchJson(rawJson),
            SendSessionsListResultAsync,
            SendSessionsHistoryResultAsync,
            SendSessionsSendResultAsync,
            SendSessionsSpawnResultAsync,
            SendCronStatusResultAsync,
            SendCronListResultAsync,
            SendCronRunResultAsync,
            SendCronWakeResultAsync,
            SendCronRunsResultAsync,
            SendCronAddResultAsync,
            SendCronUpdateResultAsync,
            SendCronRemoveResultAsync,
            SendBrowserResultAsync,
            SendCanvasResultAsync,
            SendNodesResultAsync,
            SendTelegramStubResultAsync,
            SendWebSearchResultAsync,
            SendWebFetchResultAsync
        );
        _routineCommandDispatcher = new WsRoutineCommandDispatcher(
            routineService,
            SendRoutinesAsync,
            SendRoutineActionResultAsync,
            SendRoutineProgressAsync,
            SendRoutineRunDetailAsync
        );
        _logicCommandDispatcher = new WsLogicCommandDispatcher(
            logicService,
            SendLogicGraphListResultAsync,
            SendLogicGraphActionResultAsync,
            SendLogicPathBrowseResultAsync,
            SendLogicRunActionResultAsync,
            SendLogicRunEventAsync
        );
        _doctorCommandDispatcher = new WsDoctorCommandDispatcher(
            doctorService,
            SendDoctorResultAsync
        );
        _planningCommandDispatcher = new WsPlanningCommandDispatcher(
            planService,
            SendPlanActionResultAsync,
            SendPlanListResultAsync
        );
        _taskCommandDispatcher = new WsTaskCommandDispatcher(
            taskGraphService,
            SendTaskGraphActionResultAsync,
            SendTaskGraphListResultAsync,
            SendTaskOutputResultAsync,
            SendTaskUpdatedAsync,
            SendTaskLogAsync
        );
        _refactorCommandDispatcher = new WsRefactorCommandDispatcher(
            refactorService,
            SendRefactorActionResultAsync
        );
        _contextCommandDispatcher = new WsContextCommandDispatcher(
            contextService,
            conversationService,
            new SkillFileService(_config),
            SendProjectContextAsync,
            SendSkillsListAsync,
            SendCommandsListAsync
        );
        _notebookCommandDispatcher = new WsNotebookCommandDispatcher(
            notebookService,
            SendNotebookResultAsync
        );
        _aiCommandDispatcher = new WsAiCommandDispatcher(
            chatService,
            codingService,
            settingsService,
            commandExecutionService,
            AllowCommand,
            SendGuardedErrorAsync,
            SendChatResultAsync,
            SendChatStreamChunkAsync,
            SendCodingResultAsync,
            SendCodingExecutionResultAsync,
            SendCodingProgressAsync,
            SendConversationsAsync,
            SendGroqModelsAsync,
            SendCopilotModelsAsync,
            (socket, sendLock, token, forceRefresh) => SendUsageStatsAsync(socket, sendLock, token, forceRefresh),
            SendMetricsAsync,
            result => BuildMultiChatResultJson(result)
        );
    }

    private Task SendDoctorResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        DoctorReport? report,
        bool found,
        CancellationToken cancellationToken
    )
    {
        var reportJson = report == null ? "null" : DoctorJson.Serialize(report);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"doctor_result\","
            + $"\"action\":\"{EscapeJson(action)}\","
            + $"\"found\":{(found ? "true" : "false")},"
            + $"\"report\":{reportJson}"
            + "}",
            cancellationToken
        );
    }

    private Task SendPlanActionResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        PlanActionResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = PlanJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"plan_result\","
            + $"\"action\":\"{EscapeJson(action)}\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendPlanListResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        PlanListResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = PlanJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"plan_list_result\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendTaskGraphActionResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        TaskGraphActionResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = TaskGraphJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"task_graph_result\","
            + $"\"action\":\"{EscapeJson(action)}\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendTaskGraphListResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        TaskGraphListResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = TaskGraphJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"task_graph_list_result\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendTaskOutputResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        TaskOutputResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = TaskGraphJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"task_output_result\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendRoutingPolicyResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        RoutingPolicyActionResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = RoutingPolicyJson.SerializeActionResult(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"routing_policy_result\","
            + $"\"action\":\"{EscapeJson(action)}\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendRefactorActionResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        RefactorActionResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = RefactorJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"refactor_result\","
            + $"\"action\":\"{EscapeJson(action)}\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendProjectContextAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        ProjectContextSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var payload = ContextJson.Serialize(snapshot);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"context_scan_result\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendSkillsListAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        SkillManifestListResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = ContextJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"skills_list_result\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendCommandsListAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CommandTemplateListResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = ContextJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"commands_list_result\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendNotebookResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        NotebookActionResult result,
        CancellationToken cancellationToken
    )
    {
        var payload = NotebookJson.Serialize(result);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"notebook_result\","
            + $"\"action\":\"{EscapeJson(action)}\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendRoutingDecisionAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        RoutingDecision? decision,
        CancellationToken cancellationToken
    )
    {
        var payload = decision == null ? "null" : RoutingPolicyJson.SerializeDecision(decision);
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"routing_decision_result\","
            + $"\"payload\":{payload}"
            + "}",
            cancellationToken
        );
    }

    private Task SendTaskUpdatedAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string graphId,
        TaskNode task,
        CancellationToken cancellationToken
    )
    {
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"task_updated\","
            + $"\"graphId\":\"{EscapeJson(graphId)}\","
            + $"\"task\":{TaskGraphJson.Serialize(task)}"
            + "}",
            cancellationToken
        );
    }

    private Task SendTaskLogAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string graphId,
        string taskId,
        string line,
        CancellationToken cancellationToken
    )
    {
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"task_log\","
            + $"\"graphId\":\"{EscapeJson(graphId)}\","
            + $"\"taskId\":\"{EscapeJson(taskId)}\","
            + $"\"line\":\"{EscapeJson(line)}\""
            + "}",
            cancellationToken
        );
    }

    private async Task SendSettingsStateAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken,
        bool remoteDashboardClient = false
    )
    {
        var snapshot = _settingsService.GetSettingsSnapshot();
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"settings_state\","
            + $"\"telegramBotTokenSet\":{(snapshot.TelegramBotTokenSet ? "true" : "false")},"
            + $"\"telegramChatIdSet\":{(snapshot.TelegramChatIdSet ? "true" : "false")},"
            + $"\"groqApiKeySet\":{(snapshot.GroqApiKeySet ? "true" : "false")},"
            + $"\"geminiApiKeySet\":{(snapshot.GeminiApiKeySet ? "true" : "false")},"
            + $"\"cerebrasApiKeySet\":{(snapshot.CerebrasApiKeySet ? "true" : "false")},"
            + $"\"nvidiaApiKeySet\":{(snapshot.NvidiaApiKeySet ? "true" : "false")},"
            + $"\"codexApiKeySet\":{(snapshot.CodexApiKeySet ? "true" : "false")},"
            + $"\"telegramBotTokenMasked\":\"{EscapeJson(snapshot.TelegramBotTokenMasked)}\","
            + $"\"telegramChatIdMasked\":\"{EscapeJson(snapshot.TelegramChatIdMasked)}\","
            + $"\"groqApiKeyMasked\":\"{EscapeJson(snapshot.GroqApiKeyMasked)}\","
            + $"\"geminiApiKeyMasked\":\"{EscapeJson(snapshot.GeminiApiKeyMasked)}\","
            + $"\"cerebrasApiKeyMasked\":\"{EscapeJson(snapshot.CerebrasApiKeyMasked)}\","
            + $"\"nvidiaApiKeyMasked\":\"{EscapeJson(snapshot.NvidiaApiKeyMasked)}\","
            + $"\"codexApiKeyMasked\":\"{EscapeJson(snapshot.CodexApiKeyMasked)}\","
            + $"\"externalDashboardEnabled\":{(snapshot.ExternalDashboardEnabled ? "true" : "false")},"
            + $"\"remoteDashboardClient\":{(remoteDashboardClient ? "true" : "false")},"
            + $"\"dashboardExternalUrls\":{BuildDashboardExternalUrlsJson(snapshot.ExternalDashboardEnabled)}"
            + "}",
            cancellationToken
        );
    }

    private string BuildDashboardExternalUrlsJson(bool externalDashboardEnabled)
    {
        var urls = externalDashboardEnabled
            ? GetPrivateNetworkAddresses()
                .Select(address => $"http://{address}:{_port}/")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(url => url, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var builder = new StringBuilder();
        builder.Append("[");
        for (var i = 0; i < urls.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append($"\"{EscapeJson(urls[i])}\"");
        }

        builder.Append("]");
        return builder.ToString();
    }

    private static IEnumerable<string> GetPrivateNetworkAddresses()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(address))
                {
                    continue;
                }

                var text = address.ToString();
                if (IsPrivateIPv4(text))
                {
                    yield return text;
                }
            }
        }
    }

    private static bool IsPrivateIPv4(string address)
    {
        var parts = address.Split('.');
        if (parts.Length != 4
            || !int.TryParse(parts[0], out var a)
            || !int.TryParse(parts[1], out var b))
        {
            return false;
        }

        return a == 10
               || (a == 172 && b >= 16 && b <= 31)
               || (a == 192 && b == 168);
    }

    private void RequestListenerRebind()
    {
        _requestListenerRebind?.Invoke();
    }

    private async Task SendCerebrasModelsAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var models = await _cerebrasModelCatalog.GetModelsAsync(cancellationToken);
        var selectedModel = _config.CerebrasModel ?? string.Empty;
        var builder = new StringBuilder();
        builder.Append("{\"type\":\"cerebras_models\",");
        builder.Append($"\"selected\":\"{EscapeJson(selectedModel)}\",");
        builder.Append("\"items\":[");
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            if (i > 0) builder.Append(",");
            builder.Append("{");
            builder.Append($"\"id\":\"{EscapeJson(model.Id)}\",");
            builder.Append($"\"owned_by\":\"{EscapeJson(model.OwnedBy)}\",");
            builder.Append($"\"created\":{(model.CreatedUnixSeconds.HasValue ? model.CreatedUnixSeconds.Value.ToString() : "null")}");
            builder.Append("}");
        }
        builder.Append("]}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendGroqModelsAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var models = await _groqModelCatalog.GetModelsAsync(cancellationToken);
        var usageMap = _llmRouter.GetGroqUsageSnapshot();
        var rateMap = _llmRouter.GetGroqRateLimitSnapshot();
        var selectedModel = _llmRouter.GetSelectedGroqModel();

        var builder = new StringBuilder();
        builder.Append("{\"type\":\"groq_models\",");
        builder.Append($"\"selected\":\"{EscapeJson(selectedModel)}\",");
        builder.Append("\"items\":[");

        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            usageMap.TryGetValue(model.Id, out var usage);
            rateMap.TryGetValue(model.Id, out var rate);

            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"id\":\"{EscapeJson(model.Id)}\",");
            builder.Append($"\"tier\":\"{EscapeJson(model.Tier)}\",");
            builder.Append($"\"speed_tps\":\"{EscapeJson(model.SpeedTokensPerSecond)}\",");
            builder.Append($"\"rate_limit\":\"{EscapeJson(model.RateLimit)}\",");
            builder.Append($"\"rpm\":\"{EscapeJson(model.Rpm)}\",");
            builder.Append($"\"rpd\":\"{EscapeJson(model.Rpd)}\",");
            builder.Append($"\"tpm\":\"{EscapeJson(model.Tpm)}\",");
            builder.Append($"\"tpd\":\"{EscapeJson(model.Tpd)}\",");
            builder.Append($"\"ash\":\"{EscapeJson(model.Ash)}\",");
            builder.Append($"\"asd\":\"{EscapeJson(model.Asd)}\",");
            builder.Append($"\"context_window\":\"{EscapeJson(model.ContextWindow)}\",");
            builder.Append($"\"max_completion_tokens\":\"{EscapeJson(model.MaxCompletionTokens)}\",");
            builder.Append($"\"max_file_size\":\"{EscapeJson(model.MaxFileSize)}\",");
            builder.Append($"\"price_input\":\"{EscapeJson(model.PriceInputPerMillion)}\",");
            builder.Append($"\"price_output\":\"{EscapeJson(model.PriceOutputPerMillion)}\",");
            builder.Append($"\"usage_requests\":{usage?.Requests ?? 0},");
            builder.Append($"\"usage_prompt_tokens\":{usage?.PromptTokens ?? 0},");
            builder.Append($"\"usage_completion_tokens\":{usage?.CompletionTokens ?? 0},");
            builder.Append($"\"usage_total_tokens\":{usage?.TotalTokens ?? 0},");
            builder.Append($"\"limit_requests\":{(rate?.LimitRequests.HasValue == true ? rate.LimitRequests.Value.ToString() : "null")},");
            builder.Append($"\"remaining_requests\":{(rate?.RemainingRequests.HasValue == true ? rate.RemainingRequests.Value.ToString() : "null")},");
            builder.Append($"\"limit_tokens\":{(rate?.LimitTokens.HasValue == true ? rate.LimitTokens.Value.ToString() : "null")},");
            builder.Append($"\"remaining_tokens\":{(rate?.RemainingTokens.HasValue == true ? rate.RemainingTokens.Value.ToString() : "null")},");
            builder.Append($"\"reset_requests\":\"{EscapeJson(rate?.ResetRequests ?? "-")}\",");
            builder.Append($"\"reset_tokens\":\"{EscapeJson(rate?.ResetTokens ?? "-")}\"");
            builder.Append("}");
        }

        builder.Append("]}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCopilotModelsAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var models = await _settingsService.GetCopilotModelsAsync(cancellationToken);
        var selectedModel = _settingsService.GetSelectedCopilotModel();

        var builder = new StringBuilder();
        builder.Append("{\"type\":\"copilot_models\",");
        builder.Append($"\"selected\":\"{EscapeJson(selectedModel)}\",");
        builder.Append("\"items\":[");

        for (var i = 0; i < models.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            var model = models[i];
            builder.Append("{");
            builder.Append($"\"id\":\"{EscapeJson(model.Id)}\",");
            builder.Append($"\"provider\":\"{EscapeJson(model.Provider)}\",");
            builder.Append($"\"premium_multiplier\":\"{EscapeJson(model.PremiumMultiplier)}\",");
            builder.Append($"\"speed_tps\":\"{EscapeJson(model.OutputTokensPerSecond)}\",");
            builder.Append($"\"rate_limit\":\"{EscapeJson(model.RateLimit)}\",");
            builder.Append($"\"context_window\":\"{EscapeJson(model.ContextWindow)}\",");
            builder.Append($"\"max_completion_tokens\":\"{EscapeJson(model.MaxCompletionTokens)}\",");
            builder.Append($"\"usage_requests\":{model.UsageRequests}");
            builder.Append("}");
        }

        builder.Append("]}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendUsageStatsAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken,
        bool forceRefresh = false
    )
    {
        var gemini = _settingsService.GetGeminiUsageSnapshot();
        var copilotPremium = await _settingsService.GetCopilotPremiumUsageSnapshotAsync(cancellationToken, forceRefresh);
        var copilotLocal = _settingsService.GetCopilotLocalUsageSnapshot();
        var localItems = copilotLocal
            .OrderByDescending(x => x.Value.Requests)
            .Take(12)
            .ToArray();
        long localTotalRequests = 0;
        for (var i = 0; i < localItems.Length; i++)
        {
            localTotalRequests += localItems[i].Value.Requests;
        }

        var selectedLocalModel = _settingsService.GetSelectedCopilotModel();
        var selectedLocalRequests = 0L;
        if (!string.IsNullOrWhiteSpace(selectedLocalModel)
            && copilotLocal.TryGetValue(selectedLocalModel, out var selectedUsage))
        {
            selectedLocalRequests = selectedUsage.Requests;
        }

        var premiumMessage = string.IsNullOrWhiteSpace(copilotPremium.Message)
            ? (copilotPremium.Available
                ? "Copilot Premium 사용량 조회 성공"
                : "Copilot Premium 사용량 조회 실패")
            : copilotPremium.Message;
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"usage_stats\",");
        builder.Append("\"gemini\":{");
        builder.Append($"\"requests\":{gemini.Requests},");
        builder.Append($"\"prompt_tokens\":{gemini.PromptTokens},");
        builder.Append($"\"completion_tokens\":{gemini.CompletionTokens},");
        builder.Append($"\"total_tokens\":{gemini.TotalTokens},");
        builder.Append($"\"input_price_per_million_usd\":\"{_config.GeminiInputPricePerMillionUsd.ToString("F4", CultureInfo.InvariantCulture)}\",");
        builder.Append($"\"output_price_per_million_usd\":\"{_config.GeminiOutputPricePerMillionUsd.ToString("F4", CultureInfo.InvariantCulture)}\",");
        builder.Append($"\"estimated_cost_usd\":\"{gemini.EstimatedCostUsd.ToString("F6", CultureInfo.InvariantCulture)}\"");
        builder.Append("},");
        builder.Append("\"copilotPremium\":{");
        builder.Append($"\"available\":{(copilotPremium.Available ? "true" : "false")},");
        builder.Append($"\"requires_user_scope\":{(copilotPremium.RequiresUserScope ? "true" : "false")},");
        builder.Append($"\"message\":\"{EscapeJson(premiumMessage)}\",");
        builder.Append($"\"username\":\"{EscapeJson(copilotPremium.Username)}\",");
        builder.Append($"\"plan_name\":\"{EscapeJson(copilotPremium.PlanName)}\",");
        builder.Append($"\"used_requests\":\"{copilotPremium.UsedRequests.ToString("F1", CultureInfo.InvariantCulture)}\",");
        builder.Append($"\"monthly_quota\":\"{copilotPremium.MonthlyQuota.ToString("F1", CultureInfo.InvariantCulture)}\",");
        builder.Append($"\"percent_used\":\"{copilotPremium.PercentUsed.ToString("F2", CultureInfo.InvariantCulture)}\",");
        builder.Append($"\"refreshed_local\":\"{EscapeJson(copilotPremium.RefreshedLocal)}\",");
        builder.Append($"\"features_url\":\"{EscapeJson(copilotPremium.FeaturesUrl)}\",");
        builder.Append($"\"billing_url\":\"{EscapeJson(copilotPremium.BillingUrl)}\",");
        builder.Append("\"items\":[");
        for (var i = 0; i < copilotPremium.Items.Count; i++)
        {
            var item = copilotPremium.Items[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"model\":\"{EscapeJson(item.Model)}\",");
            builder.Append($"\"requests\":\"{item.Requests.ToString("F1", CultureInfo.InvariantCulture)}\",");
            builder.Append($"\"percent\":\"{item.Percent.ToString("F2", CultureInfo.InvariantCulture)}\"");
            builder.Append("}");
        }

        builder.Append("]},");
        builder.Append("\"copilotLocal\":{");
        builder.Append($"\"selected_model\":\"{EscapeJson(selectedLocalModel)}\",");
        builder.Append($"\"selected_model_requests\":{selectedLocalRequests},");
        builder.Append($"\"total_requests\":{localTotalRequests},");
        builder.Append("\"items\":[");
        for (var i = 0; i < localItems.Length; i++)
        {
            var item = localItems[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"model\":\"{EscapeJson(item.Key)}\",");
            builder.Append($"\"requests\":{item.Value.Requests}");
            builder.Append("}");
        }

        builder.Append("]}");
        builder.Append("}");
        await SendTextAsync(
            socket,
            sendLock,
            builder.ToString(),
            cancellationToken
        );
    }

    private async Task SendConversationsAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string scope,
        string mode,
        CancellationToken cancellationToken
    )
    {
        var items = _conversationService.ListConversations(scope, mode);
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"conversations\",");
        builder.Append($"\"scope\":\"{EscapeJson(scope)}\",");
        builder.Append($"\"mode\":\"{EscapeJson(mode)}\",");
        builder.Append("\"items\":[");
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"id\":\"{EscapeJson(item.Id)}\",");
            builder.Append($"\"title\":\"{EscapeJson(item.Title)}\",");
            builder.Append($"\"project\":\"{EscapeJson(item.Project)}\",");
            builder.Append($"\"category\":\"{EscapeJson(item.Category)}\",");
            builder.Append($"\"createdUtc\":\"{EscapeJson(item.CreatedUtc.ToString("O"))}\",");
            builder.Append($"\"updatedUtc\":\"{EscapeJson(item.UpdatedUtc.ToString("O"))}\",");
            builder.Append($"\"messageCount\":{item.MessageCount},");
            builder.Append($"\"preview\":\"{EscapeJson(item.Preview)}\",");
            builder.Append("\"tags\":[");
            for (var j = 0; j < item.Tags.Count; j++)
            {
                if (j > 0)
                {
                    builder.Append(",");
                }

                builder.Append($"\"{EscapeJson(item.Tags[j])}\"");
            }

            builder.Append("],");
            builder.Append("\"linkedMemoryNotes\":[");
            for (var j = 0; j < item.LinkedMemoryNotes.Count; j++)
            {
                if (j > 0)
                {
                    builder.Append(",");
                }

                builder.Append($"\"{EscapeJson(item.LinkedMemoryNotes[j])}\"");
            }

            builder.Append("]");
            builder.Append("}");
        }

        builder.Append("]}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendConversationDetailAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string type,
        ConversationThreadView conversation,
        CancellationToken cancellationToken
    )
    {
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + $"\"type\":\"{EscapeJson(type)}\","
            + $"\"conversation\":{BuildConversationJson(conversation)}"
            + "}",
            cancellationToken
        );
    }

    private async Task SendMemoryNotesAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var notes = _memoryService.ListMemoryNotes();
        var builder = new StringBuilder();
        builder.Append("{\"type\":\"memory_notes\",\"items\":[");
        for (var i = 0; i < notes.Count; i++)
        {
            var item = notes[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"name\":\"{EscapeJson(item.Name)}\",");
            builder.Append($"\"fullPath\":\"{EscapeJson(item.FullPath)}\",");
            builder.Append($"\"excerpt\":\"{EscapeJson(item.Excerpt)}\",");
            builder.Append($"\"sizeBytes\":{item.SizeBytes},");
            builder.Append($"\"lastWriteUtc\":\"{EscapeJson(item.LastWriteUtc.ToString("O"))}\"");
            builder.Append("}");
        }

        builder.Append("]}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendMemorySearchResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string query,
        MemorySearchToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"memory_search_result\",");
        builder.Append($"\"query\":\"{EscapeJson(query)}\",");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        builder.Append("\"results\":[");

        for (var i = 0; i < result.Results.Count; i++)
        {
            var item = result.Results[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"path\":\"{EscapeJson(item.Path)}\",");
            builder.Append($"\"startLine\":{item.StartLine},");
            builder.Append($"\"endLine\":{item.EndLine},");
            builder.Append($"\"snippet\":\"{EscapeJson(item.Snippet)}\",");
            builder.Append($"\"score\":{item.Score.ToString("0.####", CultureInfo.InvariantCulture)},");
            builder.Append($"\"source\":\"{EscapeJson(item.Source)}\"");
            builder.Append("}");
        }

        builder.Append("]");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCanvasResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedAction,
        string? requestedTarget,
        string? requestedUrl,
        string? requestedProfile,
        string? requestedOutputFormat,
        int? requestedMaxWidth,
        CanvasToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"canvas_result\",");
        builder.Append($"\"requestedAction\":\"{EscapeJson(requestedAction)}\",");
        builder.Append($"\"action\":\"{EscapeJson(result.Action)}\",");
        builder.Append($"\"profile\":\"{EscapeJson(result.Profile)}\",");
        if (!string.IsNullOrWhiteSpace(requestedProfile))
        {
            builder.Append($"\"requestedProfile\":\"{EscapeJson(requestedProfile.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedTarget))
        {
            builder.Append($"\"requestedTarget\":\"{EscapeJson(requestedTarget)}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedUrl))
        {
            builder.Append($"\"requestedUrl\":\"{EscapeJson(requestedUrl)}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedOutputFormat))
        {
            builder.Append($"\"requestedOutputFormat\":\"{EscapeJson(requestedOutputFormat.Trim())}\",");
        }

        if (requestedMaxWidth.HasValue)
        {
            builder.Append($"\"requestedMaxWidth\":{requestedMaxWidth.Value},");
        }

        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        builder.Append($"\"adapter\":\"{EscapeJson(result.Adapter)}\",");
        builder.Append($"\"visible\":{(result.Visible ? "true" : "false")},");
        if (!string.IsNullOrWhiteSpace(result.Target))
        {
            builder.Append($"\"target\":\"{EscapeJson(result.Target)}\",");
        }

        if (!string.IsNullOrWhiteSpace(result.Url))
        {
            builder.Append($"\"url\":\"{EscapeJson(result.Url)}\",");
        }

        if (!string.IsNullOrWhiteSpace(result.EvalResult))
        {
            builder.Append($"\"evalResult\":\"{EscapeJson(result.EvalResult)}\",");
        }

        builder.Append($"\"a2uiRevision\":{result.A2UiRevision},");
        builder.Append($"\"updatedAtMs\":{result.UpdatedAtMs}");
        if (result.Snapshot is not null)
        {
            builder.Append(",\"snapshot\":{");
            builder.Append($"\"snapshotId\":\"{EscapeJson(result.Snapshot.SnapshotId)}\",");
            builder.Append($"\"format\":\"{EscapeJson(result.Snapshot.Format)}\",");
            builder.Append($"\"width\":{result.Snapshot.Width},");
            builder.Append($"\"height\":{result.Snapshot.Height},");
            builder.Append($"\"updatedAtMs\":{result.Snapshot.UpdatedAtMs}");
            builder.Append("}");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendBrowserResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedAction,
        string? requestedUrl,
        string? requestedProfile,
        string? requestedTargetId,
        int? requestedLimit,
        BrowserToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"browser_result\",");
        builder.Append($"\"requestedAction\":\"{EscapeJson(requestedAction)}\",");
        builder.Append($"\"action\":\"{EscapeJson(result.Action)}\",");
        builder.Append($"\"profile\":\"{EscapeJson(result.Profile)}\",");
        if (!string.IsNullOrWhiteSpace(requestedProfile))
        {
            builder.Append($"\"requestedProfile\":\"{EscapeJson(requestedProfile.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedUrl))
        {
            builder.Append($"\"requestedUrl\":\"{EscapeJson(requestedUrl)}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedTargetId))
        {
            builder.Append($"\"requestedTargetId\":\"{EscapeJson(requestedTargetId.Trim())}\",");
        }

        if (requestedLimit.HasValue)
        {
            builder.Append($"\"limit\":{requestedLimit.Value},");
        }

        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        builder.Append($"\"adapter\":\"{EscapeJson(result.Adapter)}\",");
        builder.Append($"\"running\":{(result.Running ? "true" : "false")},");
        if (!string.IsNullOrWhiteSpace(result.ActiveTargetId))
        {
            builder.Append($"\"activeTargetId\":\"{EscapeJson(result.ActiveTargetId)}\",");
        }

        if (!string.IsNullOrWhiteSpace(result.ActiveUrl))
        {
            builder.Append($"\"activeUrl\":\"{EscapeJson(result.ActiveUrl)}\",");
        }

        builder.Append("\"tabs\":[");
        for (var i = 0; i < result.Tabs.Count; i++)
        {
            var tab = result.Tabs[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"targetId\":\"{EscapeJson(tab.TargetId)}\",");
            builder.Append($"\"url\":\"{EscapeJson(tab.Url)}\",");
            builder.Append($"\"title\":\"{EscapeJson(tab.Title)}\",");
            builder.Append($"\"active\":{(tab.Active ? "true" : "false")},");
            builder.Append($"\"updatedAtMs\":{tab.UpdatedAtMs}");
            builder.Append("}");
        }

        builder.Append("]");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendNodesResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedAction,
        string? requestedNode,
        string? requestedRequestId,
        string? requestedProfile,
        string? requestedInvokeCommand,
        NodesToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"nodes_result\",");
        builder.Append($"\"requestedAction\":\"{EscapeJson(requestedAction)}\",");
        builder.Append($"\"action\":\"{EscapeJson(result.Action)}\",");
        builder.Append($"\"profile\":\"{EscapeJson(result.Profile)}\",");
        if (!string.IsNullOrWhiteSpace(requestedProfile))
        {
            builder.Append($"\"requestedProfile\":\"{EscapeJson(requestedProfile.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedNode))
        {
            builder.Append($"\"requestedNode\":\"{EscapeJson(requestedNode.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedRequestId))
        {
            builder.Append($"\"requestedRequestId\":\"{EscapeJson(requestedRequestId.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedInvokeCommand))
        {
            builder.Append($"\"requestedInvokeCommand\":\"{EscapeJson(requestedInvokeCommand.Trim())}\",");
        }

        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        builder.Append($"\"adapter\":\"{EscapeJson(result.Adapter)}\",");
        if (!string.IsNullOrWhiteSpace(result.SelectedNodeId))
        {
            builder.Append($"\"selectedNodeId\":\"{EscapeJson(result.SelectedNodeId)}\",");
        }

        if (!string.IsNullOrWhiteSpace(result.SelectedCommand))
        {
            builder.Append($"\"selectedCommand\":\"{EscapeJson(result.SelectedCommand)}\",");
        }

        if (!string.IsNullOrWhiteSpace(result.InvokePayloadJson))
        {
            builder.Append($"\"invokePayloadJson\":\"{EscapeJson(result.InvokePayloadJson)}\",");
        }

        builder.Append($"\"updatedAtMs\":{result.UpdatedAtMs},");
        builder.Append("\"nodes\":[");
        for (var i = 0; i < result.Nodes.Count; i++)
        {
            var node = result.Nodes[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"nodeId\":\"{EscapeJson(node.NodeId)}\",");
            builder.Append($"\"label\":\"{EscapeJson(node.Label)}\",");
            builder.Append($"\"online\":{(node.Online ? "true" : "false")},");
            builder.Append($"\"platform\":\"{EscapeJson(node.Platform)}\",");
            if (!string.IsNullOrWhiteSpace(node.LastCommand))
            {
                builder.Append($"\"lastCommand\":\"{EscapeJson(node.LastCommand)}\",");
            }

            if (node.LastCommandAtMs.HasValue)
            {
                builder.Append($"\"lastCommandAtMs\":{node.LastCommandAtMs.Value},");
            }

            builder.Append($"\"updatedAtMs\":{node.UpdatedAtMs},");
            builder.Append("\"commands\":[");
            for (var commandIndex = 0; commandIndex < node.Commands.Count; commandIndex++)
            {
                if (commandIndex > 0)
                {
                    builder.Append(",");
                }

                builder.Append($"\"{EscapeJson(node.Commands[commandIndex])}\"");
            }

            builder.Append("]");
            builder.Append("}");
        }

        builder.Append("],");
        builder.Append("\"pendingRequests\":[");
        for (var i = 0; i < result.PendingRequests.Count; i++)
        {
            var pending = result.PendingRequests[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"requestId\":\"{EscapeJson(pending.RequestId)}\",");
            builder.Append($"\"nodeLabel\":\"{EscapeJson(pending.NodeLabel)}\",");
            builder.Append($"\"status\":\"{EscapeJson(pending.Status)}\",");
            builder.Append($"\"requestedAtMs\":{pending.RequestedAtMs},");
            builder.Append($"\"updatedAtMs\":{pending.UpdatedAtMs}");
            builder.Append("}");
        }

        builder.Append("]");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendTelegramStubResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedText,
        string responseText,
        bool ok,
        string status,
        string? error,
        CancellationToken cancellationToken,
        SearchAnswerGuardFailure? guardFailure = null,
        int retryAttempt = 0,
        int retryMaxAttempts = 0,
        string retryStopReason = "-"
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"telegram_stub_result\",");
        builder.Append("\"action\":\"command\",");
        builder.Append("\"group\":\"telegram\",");
        builder.Append("\"adapter\":\"stub\",");
        builder.Append("\"channel\":\"telegram\",");
        builder.Append($"\"input\":\"{EscapeJson(requestedText)}\",");
        builder.Append($"\"ok\":{(ok ? "true" : "false")},");
        builder.Append($"\"status\":\"{EscapeJson(status)}\",");
        builder.Append($"\"response\":\"{EscapeJson(responseText)}\"");
        if (!string.IsNullOrWhiteSpace(error))
        {
            builder.Append($",\"error\":\"{EscapeJson(error)}\"");
        }

        var effectiveGuardFailure = guardFailure ?? TryParseGuardFailureFromMessage(error ?? responseText);
        var guardCategory = NormalizeWebSearchGuardCategory(effectiveGuardFailure);
        var guardReason = NormalizeWebSearchGuardReason(effectiveGuardFailure);
        var guardDetail = NormalizeWebSearchGuardDetail(effectiveGuardFailure);
        var retryDirective = ResolveRetryDirective(effectiveGuardFailure);
        var resolvedRetryAttempt = Math.Max(0, retryAttempt);
        var resolvedRetryMaxAttempts = Math.Max(0, retryMaxAttempts);
        var resolvedRetryStopReason = string.IsNullOrWhiteSpace(retryStopReason)
            ? NormalizeWebSearchGuardReason(effectiveGuardFailure)
            : retryStopReason;
        var normalizedRetryStopReason = NormalizeWebSearchRetryStopReason(resolvedRetryStopReason);
        builder.Append($",\"guardCategory\":\"{EscapeJson(guardCategory)}\"");
        builder.Append($",\"guardReason\":\"{EscapeJson(guardReason)}\"");
        builder.Append($",\"guardDetail\":\"{EscapeJson(guardDetail)}\"");
        builder.Append($",\"retryAttempt\":{resolvedRetryAttempt}");
        builder.Append($",\"retryMaxAttempts\":{resolvedRetryMaxAttempts}");
        builder.Append($",\"retryStopReason\":\"{EscapeJson(normalizedRetryStopReason)}\"");
        builder.Append($",\"retryRequired\":{(retryDirective.RetryRequired ? "true" : "false")}");
        builder.Append($",\"retryAction\":\"{EscapeJson(retryDirective.RetryAction)}\"");
        builder.Append($",\"retryScope\":\"{EscapeJson(retryDirective.RetryScope)}\"");
        builder.Append($",\"retryReason\":\"{EscapeJson(retryDirective.RetryReason)}\"");

        builder.Append("}");
        TrackGuardRetryTimelineEntry(
            channel: "telegram",
            retryRequired: retryDirective.RetryRequired,
            retryAttempt: resolvedRetryAttempt,
            retryMaxAttempts: resolvedRetryMaxAttempts,
            retryStopReason: normalizedRetryStopReason
        );
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendWebSearchResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string query,
        int? count,
        string? freshness,
        WebSearchToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"web_search_result\",");
        builder.Append($"\"query\":\"{EscapeJson(query)}\",");
        builder.Append($"\"provider\":\"{EscapeJson(result.Provider)}\",");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        if (count.HasValue)
        {
            builder.Append($"\"count\":{count.Value},");
        }

        if (!string.IsNullOrWhiteSpace(freshness))
        {
            builder.Append($"\"freshness\":\"{EscapeJson(freshness.Trim())}\",");
        }

        builder.Append("\"results\":[");
        for (var i = 0; i < result.Results.Count; i++)
        {
            var item = result.Results[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"title\":\"{EscapeJson(item.Title)}\",");
            builder.Append($"\"url\":\"{EscapeJson(item.Url)}\",");
            builder.Append($"\"description\":\"{EscapeJson(item.Description)}\",");
            builder.Append($"\"citationId\":\"{EscapeJson(NormalizeWebSearchCitationId(item.CitationId, i + 1))}\"");
            if (!string.IsNullOrWhiteSpace(item.Published))
            {
                builder.Append($",\"published\":\"{EscapeJson(item.Published)}\"");
            }

            builder.Append("}");
        }

        builder.Append("]");
        if (result.ExternalContent is not null)
        {
            builder.Append(",\"externalContent\":{");
            builder.Append($"\"untrusted\":{(result.ExternalContent.Untrusted ? "true" : "false")},");
            builder.Append($"\"source\":\"{EscapeJson(result.ExternalContent.Source)}\",");
            builder.Append($"\"provider\":\"{EscapeJson(result.ExternalContent.Provider)}\",");
            builder.Append($"\"wrapped\":{(result.ExternalContent.Wrapped ? "true" : "false")}");
            builder.Append("}");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        var guardCategory = NormalizeWebSearchGuardCategory(result.GuardFailure);
        var guardReason = NormalizeWebSearchGuardReason(result.GuardFailure);
        var guardDetail = NormalizeWebSearchGuardDetail(result.GuardFailure);
        builder.Append($",\"guardCategory\":\"{EscapeJson(guardCategory)}\"");
        builder.Append($",\"guardReason\":\"{EscapeJson(guardReason)}\"");
        builder.Append($",\"guardDetail\":\"{EscapeJson(guardDetail)}\"");
        builder.Append($",\"retryAttempt\":{Math.Max(0, result.RetryAttempt)}");
        builder.Append($",\"retryMaxAttempts\":{Math.Max(0, result.RetryMaxAttempts)}");
        builder.Append($",\"retryStopReason\":\"{EscapeJson(NormalizeWebSearchRetryStopReason(result.RetryStopReason))}\"");
        AppendRetryDirectiveJson(builder, result.GuardFailure);

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private static string NormalizeWebSearchGuardCategory(SearchAnswerGuardFailure? failure)
    {
        if (failure is null)
        {
            return "-";
        }

        var normalized = failure.Category.ToString().ToLowerInvariant();
        return normalized switch
        {
            "coverage" or "freshness" or "credibility" => normalized,
            _ => "-"
        };
    }

    private static string NormalizeWebSearchGuardReason(SearchAnswerGuardFailure? failure)
    {
        if (failure is null)
        {
            return "-";
        }

        var normalized = (failure.ReasonCode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static string NormalizeWebSearchGuardDetail(SearchAnswerGuardFailure? failure)
    {
        if (failure is null)
        {
            return "-";
        }

        var normalized = (failure.Detail ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        return normalized.Length <= 180
            ? normalized
            : normalized[..180] + "...";
    }

    private static string NormalizeWebSearchRetryStopReason(string? stopReason)
    {
        var normalized = (stopReason ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private void TrackGuardRetryTimelineEntry(
        string channel,
        bool retryRequired,
        int retryAttempt,
        int retryMaxAttempts,
        string? retryStopReason
    )
    {
        try
        {
            _guardRetryTimelineStore.Add(
                channel: channel,
                retryRequired: retryRequired,
                retryAttempt: Math.Max(0, retryAttempt),
                retryMaxAttempts: Math.Max(0, retryMaxAttempts),
                retryStopReason: NormalizeWebSearchRetryStopReason(retryStopReason)
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[guard-retry-timeline] append failed: {ex.Message}");
        }
    }

    private static void AppendRetryDirectiveJson(StringBuilder builder, SearchAnswerGuardFailure? failure)
    {
        var retryDirective = ResolveRetryDirective(failure);
        builder.Append($",\"retryRequired\":{(retryDirective.RetryRequired ? "true" : "false")}");
        builder.Append($",\"retryAction\":\"{EscapeJson(retryDirective.RetryAction)}\"");
        builder.Append($",\"retryScope\":\"{EscapeJson(retryDirective.RetryScope)}\"");
        builder.Append($",\"retryReason\":\"{EscapeJson(retryDirective.RetryReason)}\"");
    }

    private static (bool RetryRequired, string RetryAction, string RetryScope, string RetryReason) ResolveRetryDirective(
        SearchAnswerGuardFailure? failure
    )
    {
        if (failure is null)
        {
            return (false, "-", "-", "-");
        }

        var reason = NormalizeWebSearchGuardReason(failure);
        var retryAction = reason switch
        {
            "citation_validation_failed" => "retry_with_citation_mapping",
            "count_lock_unsatisfied" or "count_lock_unsatisfied_after_retries" or "target_count_mismatch" => "retry_with_recollect_loop",
            _ => failure.Category switch
            {
                SearchAnswerGuardFailureCategory.Coverage => "retry_with_recollect_loop",
                SearchAnswerGuardFailureCategory.Freshness => "retry_with_freshness_probe",
                SearchAnswerGuardFailureCategory.Credibility => "retry_with_source_expansion",
                _ => "retry_with_recollect_loop"
            }
        };

        return (
            true,
            retryAction,
            "gemini_grounding_search",
            reason
        );
    }

    private static string NormalizeWebSearchCitationId(string? citationId, int fallbackIndex)
    {
        var normalized = (citationId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return $"c{Math.Max(1, fallbackIndex)}";
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static SearchAnswerGuardFailure? TryParseGuardFailureFromMessage(string? message)
    {
        var normalized = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Contains(CitationValidationBlockedToken, StringComparison.Ordinal))
        {
            return new SearchAnswerGuardFailure(
                SearchAnswerGuardFailureCategory.Coverage,
                "citation_validation_failed",
                "blocked_response_text_detected"
            );
        }

        var localizedFailure = TryParseLocalizedGuardFailure(normalized);
        if (localizedFailure is not null)
        {
            return localizedFailure;
        }

        var match = AnswerGuardBlockedPattern.Match(normalized);
        if (!match.Success)
        {
            return null;
        }

        var categoryRaw = match.Groups["category"].Value.Trim().ToLowerInvariant();
        var category = categoryRaw switch
        {
            "coverage" => SearchAnswerGuardFailureCategory.Coverage,
            "freshness" => SearchAnswerGuardFailureCategory.Freshness,
            "credibility" => SearchAnswerGuardFailureCategory.Credibility,
            _ => (SearchAnswerGuardFailureCategory?)null
        };
        if (category is null)
        {
            return null;
        }

        var reasonCode = match.Groups["reason"].Value.Trim();
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            reasonCode = "unknown";
        }

        var detail = match.Groups["detail"].Success
            ? match.Groups["detail"].Value.Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = "-";
        }

        return new SearchAnswerGuardFailure(
            category.Value,
            reasonCode,
            detail
        );
    }

    private static SearchAnswerGuardFailure? TryParseLocalizedGuardFailure(string message)
    {
        if (!message.Contains("검색 실패", StringComparison.Ordinal))
        {
            return null;
        }

        var reasonMatch = GroundedFailureReasonPattern.Match(message);
        if (!reasonMatch.Success)
        {
            return null;
        }

        var reasonCode = NormalizeGuardFailureToken(reasonMatch.Groups["reason"].Value, "unknown");
        var category = ResolveGuardFailureCategory(reasonCode);
        var detailSegments = new List<string>(2);
        var terminationCode = string.Empty;

        var terminationMatch = GroundedFailureTerminationPattern.Match(message);
        if (terminationMatch.Success)
        {
            terminationCode = NormalizeGuardFailureToken(terminationMatch.Groups["termination"].Value, string.Empty);
            if (terminationCode.Length > 0)
            {
                detailSegments.Add($"termination={terminationCode}");
            }
        }

        var detailMatch = GroundedFailureDetailPattern.Match(message);
        if (detailMatch.Success)
        {
            var detail = (detailMatch.Groups["detail"].Value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            if (detail.Length > 0
                && (terminationCode.Length == 0
                    || !detail.Contains($"termination={terminationCode}", StringComparison.OrdinalIgnoreCase)))
            {
                detailSegments.Add(detail);
            }
        }

        var mergedDetail = detailSegments.Count == 0
            ? "-"
            : string.Join(", ", detailSegments);
        return new SearchAnswerGuardFailure(
            category,
            reasonCode,
            mergedDetail
        );
    }

    private static string NormalizeGuardFailureToken(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return fallback;
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static SearchAnswerGuardFailureCategory ResolveGuardFailureCategory(string reasonCode)
    {
        return reasonCode switch
        {
            "freshness_guard_failed" => SearchAnswerGuardFailureCategory.Freshness,
            "credibility_guard_failed" => SearchAnswerGuardFailureCategory.Credibility,
            _ => SearchAnswerGuardFailureCategory.Coverage
        };
    }

    private async Task SendGuardedErrorAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string message,
        CancellationToken cancellationToken,
        SearchAnswerGuardFailure? guardFailure = null
    )
    {
        var effectiveGuardFailure = guardFailure ?? TryParseGuardFailureFromMessage(message);
        var retryDirective = ResolveRetryDirective(effectiveGuardFailure);
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"error\","
            + $"\"message\":\"{EscapeJson(message)}\","
            + $"\"guardCategory\":\"{EscapeJson(NormalizeWebSearchGuardCategory(effectiveGuardFailure))}\","
            + $"\"guardReason\":\"{EscapeJson(NormalizeWebSearchGuardReason(effectiveGuardFailure))}\","
            + $"\"guardDetail\":\"{EscapeJson(NormalizeWebSearchGuardDetail(effectiveGuardFailure))}\","
            + $"\"retryRequired\":{(retryDirective.RetryRequired ? "true" : "false")},"
            + $"\"retryAction\":\"{EscapeJson(retryDirective.RetryAction)}\","
            + $"\"retryScope\":\"{EscapeJson(retryDirective.RetryScope)}\","
            + $"\"retryReason\":\"{EscapeJson(retryDirective.RetryReason)}\""
            + "}",
            cancellationToken
        );
    }

    private async Task SendWebFetchResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedUrl,
        string? requestedExtractMode,
        int? requestedMaxChars,
        WebFetchToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"web_fetch_result\",");
        builder.Append($"\"url\":\"{EscapeJson(result.Url)}\",");
        builder.Append($"\"requestedUrl\":\"{EscapeJson(requestedUrl)}\",");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        builder.Append($"\"extractMode\":\"{EscapeJson(result.ExtractMode)}\",");
        if (!string.IsNullOrWhiteSpace(requestedExtractMode))
        {
            builder.Append($"\"requestedExtractMode\":\"{EscapeJson(requestedExtractMode.Trim())}\",");
        }

        if (requestedMaxChars.HasValue)
        {
            builder.Append($"\"maxChars\":{requestedMaxChars.Value},");
        }

        if (!string.IsNullOrWhiteSpace(result.FinalUrl))
        {
            builder.Append($"\"finalUrl\":\"{EscapeJson(result.FinalUrl)}\",");
        }

        if (result.Status.HasValue)
        {
            builder.Append($"\"status\":{result.Status.Value},");
        }

        if (!string.IsNullOrWhiteSpace(result.ContentType))
        {
            builder.Append($"\"contentType\":\"{EscapeJson(result.ContentType)}\",");
        }

        builder.Append($"\"truncated\":{(result.Truncated ? "true" : "false")},");
        builder.Append($"\"length\":{result.Length},");
        builder.Append($"\"text\":\"{EscapeJson(result.Text)}\"");
        if (result.ExternalContent is not null)
        {
            builder.Append(",\"externalContent\":{");
            builder.Append($"\"untrusted\":{(result.ExternalContent.Untrusted ? "true" : "false")},");
            builder.Append($"\"source\":\"{EscapeJson(result.ExternalContent.Source)}\",");
            builder.Append($"\"provider\":\"{EscapeJson(result.ExternalContent.Provider)}\",");
            builder.Append($"\"wrapped\":{(result.ExternalContent.Wrapped ? "true" : "false")}");
            builder.Append("}");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendMemoryGetResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedPath,
        int? fromLine,
        int? lines,
        MemoryGetToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"memory_get_result\",");
        builder.Append($"\"path\":\"{EscapeJson(result.Path)}\",");
        builder.Append($"\"requestedPath\":\"{EscapeJson(requestedPath)}\",");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        if (fromLine.HasValue)
        {
            builder.Append($"\"from\":{fromLine.Value},");
        }

        if (lines.HasValue)
        {
            builder.Append($"\"lines\":{lines.Value},");
        }

        builder.Append($"\"text\":\"{EscapeJson(result.Text)}\"");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendSessionsListResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        IReadOnlyList<string> kinds,
        int? limit,
        int? activeMinutes,
        int? messageLimit,
        string? search,
        string? scope,
        string? mode,
        SessionListToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"sessions_list_result\",");
        if (limit.HasValue)
        {
            builder.Append($"\"limit\":{limit.Value},");
        }

        if (activeMinutes.HasValue)
        {
            builder.Append($"\"activeMinutes\":{activeMinutes.Value},");
        }

        if (messageLimit.HasValue)
        {
            builder.Append($"\"messageLimit\":{messageLimit.Value},");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            builder.Append($"\"search\":\"{EscapeJson(search.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            builder.Append($"\"scope\":\"{EscapeJson(scope.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(mode))
        {
            builder.Append($"\"mode\":\"{EscapeJson(mode.Trim())}\",");
        }

        builder.Append("\"kinds\":[");
        for (var i = 0; i < kinds.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append($"\"{EscapeJson(kinds[i])}\"");
        }

        builder.Append("],");
        builder.Append($"\"count\":{result.Count},");
        builder.Append("\"sessions\":[");
        for (var i = 0; i < result.Sessions.Count; i++)
        {
            var item = result.Sessions[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"key\":\"{EscapeJson(item.Key)}\",");
            builder.Append($"\"kind\":\"{EscapeJson(item.Kind)}\",");
            builder.Append($"\"scope\":\"{EscapeJson(item.Scope)}\",");
            builder.Append($"\"mode\":\"{EscapeJson(item.Mode)}\",");
            builder.Append($"\"label\":\"{EscapeJson(item.Label)}\",");
            builder.Append($"\"displayName\":\"{EscapeJson(item.DisplayName)}\",");
            builder.Append($"\"project\":\"{EscapeJson(item.Project)}\",");
            builder.Append($"\"category\":\"{EscapeJson(item.Category)}\",");
            builder.Append($"\"updatedAt\":{item.UpdatedAt},");
            builder.Append($"\"messageCount\":{item.MessageCount},");
            builder.Append($"\"preview\":\"{EscapeJson(item.Preview)}\",");
            builder.Append("\"tags\":[");
            for (var j = 0; j < item.Tags.Count; j++)
            {
                if (j > 0)
                {
                    builder.Append(",");
                }

                builder.Append($"\"{EscapeJson(item.Tags[j])}\"");
            }

            builder.Append("],");
            builder.Append("\"linkedMemoryNotes\":[");
            for (var j = 0; j < item.LinkedMemoryNotes.Count; j++)
            {
                if (j > 0)
                {
                    builder.Append(",");
                }

                builder.Append($"\"{EscapeJson(item.LinkedMemoryNotes[j])}\"");
            }

            builder.Append("],");
            builder.Append("\"messages\":[");
            for (var j = 0; j < item.Messages.Count; j++)
            {
                var message = item.Messages[j];
                if (j > 0)
                {
                    builder.Append(",");
                }

                builder.Append("{");
                builder.Append($"\"role\":\"{EscapeJson(message.Role)}\",");
                builder.Append($"\"text\":\"{EscapeJson(message.Text)}\",");
                builder.Append($"\"createdUtc\":\"{EscapeJson(message.CreatedUtc)}\"");
                builder.Append("}");
            }

            builder.Append("]");
            builder.Append("}");
        }

        builder.Append("]");
        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendSessionsHistoryResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string? requestedSessionKey,
        int? limit,
        bool? includeTools,
        SessionHistoryToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"sessions_history_result\",");
        builder.Append($"\"sessionKey\":\"{EscapeJson(result.SessionKey)}\",");
        builder.Append($"\"requestedSessionKey\":\"{EscapeJson((requestedSessionKey ?? string.Empty).Trim())}\",");
        if (limit.HasValue)
        {
            builder.Append($"\"limit\":{limit.Value},");
        }

        if (includeTools.HasValue)
        {
            builder.Append($"\"includeTools\":{(includeTools.Value ? "true" : "false")},");
        }

        builder.Append($"\"status\":\"{EscapeJson(result.Status)}\",");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($"\"error\":\"{EscapeJson(result.Error)}\",");
        }

        builder.Append($"\"count\":{result.Count},");
        builder.Append($"\"truncated\":{(result.Truncated ? "true" : "false")},");
        builder.Append($"\"droppedMessages\":{(result.DroppedMessages ? "true" : "false")},");
        builder.Append($"\"contentTruncated\":{(result.ContentTruncated ? "true" : "false")},");
        builder.Append($"\"contentRedacted\":{(result.ContentRedacted ? "true" : "false")},");
        builder.Append($"\"bytes\":{result.Bytes},");
        builder.Append("\"messages\":[");
        for (var i = 0; i < result.Messages.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            var message = result.Messages[i];
            builder.Append("{");
            builder.Append($"\"role\":\"{EscapeJson(message.Role)}\",");
            builder.Append($"\"text\":\"{EscapeJson(message.Text)}\",");
            builder.Append($"\"createdUtc\":\"{EscapeJson(message.CreatedUtc)}\"");
            builder.Append("}");
        }

        builder.Append("]");
        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendSessionsSendResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string? requestedSessionKey,
        int? timeoutSeconds,
        SessionSendToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"sessions_send_result\",");
        builder.Append($"\"sessionKey\":\"{EscapeJson(result.SessionKey)}\",");
        builder.Append($"\"requestedSessionKey\":\"{EscapeJson((requestedSessionKey ?? string.Empty).Trim())}\",");
        builder.Append($"\"timeoutSeconds\":{result.TimeoutSeconds},");
        if (timeoutSeconds.HasValue)
        {
            builder.Append($"\"requestedTimeoutSeconds\":{timeoutSeconds.Value},");
        }

        builder.Append($"\"status\":\"{EscapeJson(result.Status)}\",");
        builder.Append($"\"runId\":\"{EscapeJson(result.RunId)}\",");
        builder.Append($"\"messageTruncated\":{(result.MessageTruncated ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(result.Reply))
        {
            builder.Append($",\"reply\":\"{EscapeJson(result.Reply)}\"");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        if (string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.Status, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(",\"delivery\":{\"status\":\"pending\",\"mode\":\"announce\"}");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendSessionsSpawnResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedTask,
        string? requestedLabel,
        string? requestedRuntime,
        int? requestedRunTimeoutSeconds,
        int? requestedTimeoutSeconds,
        bool? requestedThread,
        string? requestedMode,
        SessionSpawnToolResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"sessions_spawn_result\",");
        builder.Append($"\"task\":\"{EscapeJson(requestedTask)}\",");
        if (!string.IsNullOrWhiteSpace(requestedLabel))
        {
            builder.Append($"\"label\":\"{EscapeJson(requestedLabel.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedRuntime))
        {
            builder.Append($"\"requestedRuntime\":\"{EscapeJson(requestedRuntime.Trim())}\",");
        }

        if (!string.IsNullOrWhiteSpace(requestedMode))
        {
            builder.Append($"\"requestedMode\":\"{EscapeJson(requestedMode.Trim())}\",");
        }

        if (requestedRunTimeoutSeconds.HasValue)
        {
            builder.Append($"\"requestedRunTimeoutSeconds\":{requestedRunTimeoutSeconds.Value},");
        }

        if (requestedTimeoutSeconds.HasValue)
        {
            builder.Append($"\"requestedTimeoutSeconds\":{requestedTimeoutSeconds.Value},");
        }

        if (requestedThread.HasValue)
        {
            builder.Append($"\"requestedThread\":{(requestedThread.Value ? "true" : "false")},");
        }

        builder.Append($"\"status\":\"{EscapeJson(result.Status)}\",");
        builder.Append($"\"runId\":\"{EscapeJson(result.RunId)}\",");
        builder.Append($"\"childSessionKey\":\"{EscapeJson(result.ChildSessionKey)}\",");
        builder.Append($"\"mode\":\"{EscapeJson(result.Mode)}\",");
        builder.Append($"\"runtime\":\"{EscapeJson(result.Runtime)}\",");
        builder.Append($"\"runTimeoutSeconds\":{result.RunTimeoutSeconds},");
        builder.Append($"\"thread\":{(result.Thread ? "true" : "false")},");
        builder.Append($"\"taskTruncated\":{(result.TaskTruncated ? "true" : "false")},");
        builder.Append($"\"followUpStatus\":\"{EscapeJson(result.FollowUpStatus)}\",");
        builder.Append($"\"followUpAction\":\"{EscapeJson(result.FollowUpAction)}\"");
        if (!string.IsNullOrWhiteSpace(result.BackendSessionId))
        {
            builder.Append($",\"backendSessionId\":\"{EscapeJson(result.BackendSessionId)}\"");
        }

        if (!string.IsNullOrWhiteSpace(result.ThreadBindingKey))
        {
            builder.Append($",\"threadBindingKey\":\"{EscapeJson(result.ThreadBindingKey)}\"");
        }

        if (!string.IsNullOrWhiteSpace(result.Note))
        {
            builder.Append($",\"note\":\"{EscapeJson(result.Note)}\"");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronStatusResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CronToolStatusResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"status\",");
        builder.Append($"\"enabled\":{(result.Enabled ? "true" : "false")},");
        builder.Append($"\"storePath\":\"{EscapeJson(result.StorePath)}\",");
        builder.Append($"\"jobs\":{result.Jobs},");
        builder.Append($"\"nextWakeAtMs\":{(result.NextWakeAtMs.HasValue ? result.NextWakeAtMs.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronListResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        bool includeDisabled,
        int? requestedLimit,
        int? requestedOffset,
        CronToolListResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"list\",");
        builder.Append($"\"includeDisabled\":{(includeDisabled ? "true" : "false")},");
        if (requestedLimit.HasValue)
        {
            builder.Append($"\"requestedLimit\":{requestedLimit.Value},");
        }

        if (requestedOffset.HasValue)
        {
            builder.Append($"\"requestedOffset\":{requestedOffset.Value},");
        }

        builder.Append($"\"total\":{result.Total},");
        builder.Append($"\"offset\":{result.Offset},");
        builder.Append($"\"limit\":{result.Limit},");
        builder.Append($"\"hasMore\":{(result.HasMore ? "true" : "false")},");
        builder.Append($"\"nextOffset\":{(result.NextOffset.HasValue ? result.NextOffset.Value.ToString(CultureInfo.InvariantCulture) : "null")},");
        builder.Append("\"jobs\":[");
        for (var i = 0; i < result.Jobs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            AppendCronJobJson(builder, result.Jobs[i]);
        }

        builder.Append("]");
        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronRunResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        string? requestedRunMode,
        CronToolRunResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"run\",");
        builder.Append($"\"jobId\":\"{EscapeJson(requestedJobId)}\",");
        if (!string.IsNullOrWhiteSpace(requestedRunMode))
        {
            builder.Append($"\"runMode\":\"{EscapeJson(requestedRunMode.Trim())}\",");
        }

        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"ran\":{(result.Ran ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(result.Reason))
        {
            builder.Append($",\"reason\":\"{EscapeJson(result.Reason)}\"");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronWakeResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string? requestedMode,
        int requestedTextLength,
        CronToolWakeResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"wake\",");
        if (!string.IsNullOrWhiteSpace(requestedMode))
        {
            builder.Append($"\"requestedMode\":\"{EscapeJson(requestedMode.Trim())}\",");
        }

        builder.Append($"\"textLength\":{requestedTextLength},");
        builder.Append($"\"mode\":\"{EscapeJson(result.Mode)}\",");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")}");
        if (result.Ok)
        {
            builder.Append($",\"triggeredRuns\":{result.TriggeredRuns}");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronRunsResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        int? requestedLimit,
        int? requestedOffset,
        CronToolRunsResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"runs\",");
        builder.Append($"\"jobId\":\"{EscapeJson(requestedJobId)}\",");
        if (requestedLimit.HasValue)
        {
            builder.Append($"\"requestedLimit\":{requestedLimit.Value},");
        }

        if (requestedOffset.HasValue)
        {
            builder.Append($"\"requestedOffset\":{requestedOffset.Value},");
        }

        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")}");
        if (result.Ok)
        {
            builder.Append($",\"total\":{result.Total}");
            builder.Append($",\"offset\":{result.Offset}");
            builder.Append($",\"limit\":{result.Limit}");
            builder.Append($",\"hasMore\":{(result.HasMore ? "true" : "false")}");
            builder.Append($",\"nextOffset\":{(result.NextOffset.HasValue ? result.NextOffset.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
            builder.Append(",\"entries\":[");
            for (var i = 0; i < result.Entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                AppendCronRunLogEntryJson(builder, result.Entries[i]);
            }

            builder.Append("]");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronAddResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CronToolAddResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"add\",");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")}");
        if (result.Job != null)
        {
            builder.Append(",\"job\":");
            AppendCronJobJson(builder, result.Job);
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronUpdateResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        CronToolUpdateResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"update\",");
        builder.Append($"\"jobId\":\"{EscapeJson(requestedJobId)}\",");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")}");
        if (result.Job != null)
        {
            builder.Append(",\"job\":");
            AppendCronJobJson(builder, result.Job);
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendCronRemoveResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string requestedJobId,
        CronToolRemoveResult result,
        CancellationToken cancellationToken
    )
    {
        var builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"cron_result\",");
        builder.Append("\"action\":\"remove\",");
        builder.Append($"\"jobId\":\"{EscapeJson(requestedJobId)}\",");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"removed\":{(result.Removed ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private static void AppendCronJobJson(StringBuilder builder, CronToolJob job)
    {
        builder.Append("{");
        builder.Append($"\"id\":\"{EscapeJson(job.Id)}\",");
        builder.Append($"\"name\":\"{EscapeJson(job.Name)}\",");
        builder.Append($"\"enabled\":{(job.Enabled ? "true" : "false")},");
        builder.Append($"\"createdAtMs\":{job.CreatedAtMs},");
        builder.Append($"\"updatedAtMs\":{job.UpdatedAtMs},");
        builder.Append($"\"sessionTarget\":\"{EscapeJson(job.SessionTarget)}\",");
        builder.Append($"\"wakeMode\":\"{EscapeJson(job.WakeMode)}\",");
        if (!string.IsNullOrWhiteSpace(job.Description))
        {
            builder.Append($"\"description\":\"{EscapeJson(job.Description)}\",");
        }

        builder.Append("\"schedule\":{");
        builder.Append($"\"kind\":\"{EscapeJson(job.Schedule.Kind)}\"");
        if (!string.IsNullOrWhiteSpace(job.Schedule.Expr))
        {
            builder.Append($",\"expr\":\"{EscapeJson(job.Schedule.Expr)}\"");
        }

        if (!string.IsNullOrWhiteSpace(job.Schedule.Tz))
        {
            builder.Append($",\"tz\":\"{EscapeJson(job.Schedule.Tz)}\"");
        }

        if (!string.IsNullOrWhiteSpace(job.Schedule.At))
        {
            builder.Append($",\"at\":\"{EscapeJson(job.Schedule.At)}\"");
        }

        if (job.Schedule.EveryMs.HasValue)
        {
            builder.Append($",\"everyMs\":{job.Schedule.EveryMs.Value}");
        }

        if (job.Schedule.AnchorMs.HasValue)
        {
            builder.Append($",\"anchorMs\":{job.Schedule.AnchorMs.Value}");
        }

        builder.Append("},");
        builder.Append("\"payload\":{");
        builder.Append($"\"kind\":\"{EscapeJson(job.Payload.Kind)}\"");
        if (string.Equals(job.Payload.Kind, "agentTurn", StringComparison.Ordinal))
        {
            var message = string.IsNullOrWhiteSpace(job.Payload.Message)
                ? (job.Payload.Text ?? string.Empty)
                : job.Payload.Message!;
            builder.Append($",\"message\":\"{EscapeJson(message)}\"");
            if (!string.IsNullOrWhiteSpace(job.Payload.Model))
            {
                builder.Append($",\"model\":\"{EscapeJson(job.Payload.Model)}\"");
            }

            if (!string.IsNullOrWhiteSpace(job.Payload.Thinking))
            {
                builder.Append($",\"thinking\":\"{EscapeJson(job.Payload.Thinking)}\"");
            }

            if (job.Payload.TimeoutSeconds.HasValue)
            {
                builder.Append($",\"timeoutSeconds\":{job.Payload.TimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture)}");
            }

            if (job.Payload.LightContext.HasValue)
            {
                builder.Append($",\"lightContext\":{(job.Payload.LightContext.Value ? "true" : "false")}");
            }
        }
        else
        {
            var text = string.IsNullOrWhiteSpace(job.Payload.Text)
                ? (job.Payload.Message ?? string.Empty)
                : job.Payload.Text!;
            builder.Append($",\"text\":\"{EscapeJson(text)}\"");
        }
        builder.Append("},");
        builder.Append("\"state\":{");
        builder.Append($"\"nextRunAtMs\":{ToJsonLongOrNull(job.State.NextRunAtMs)},");
        builder.Append($"\"runningAtMs\":{ToJsonLongOrNull(job.State.RunningAtMs)},");
        builder.Append($"\"lastRunAtMs\":{ToJsonLongOrNull(job.State.LastRunAtMs)},");
        builder.Append($"\"lastRunStatus\":{ToJsonStringOrNull(job.State.LastRunStatus)},");
        builder.Append($"\"lastError\":{ToJsonStringOrNull(job.State.LastError)},");
        builder.Append($"\"lastDurationMs\":{ToJsonLongOrNull(job.State.LastDurationMs)}");
        builder.Append("}");
        builder.Append("}");
    }

    private static void AppendCronRunLogEntryJson(StringBuilder builder, CronToolRunLogEntry entry)
    {
        builder.Append("{");
        builder.Append($"\"ts\":{entry.Ts.ToString(CultureInfo.InvariantCulture)},");
        builder.Append($"\"jobId\":\"{EscapeJson(entry.JobId)}\",");
        builder.Append($"\"action\":\"{EscapeJson(entry.Action)}\"");
        if (!string.IsNullOrWhiteSpace(entry.Status))
        {
            builder.Append($",\"status\":\"{EscapeJson(entry.Status)}\"");
        }

        if (!string.IsNullOrWhiteSpace(entry.Error))
        {
            builder.Append($",\"error\":\"{EscapeJson(entry.Error)}\"");
        }

        if (!string.IsNullOrWhiteSpace(entry.Summary))
        {
            builder.Append($",\"summary\":\"{EscapeJson(entry.Summary)}\"");
        }

        if (entry.RunAtMs.HasValue)
        {
            builder.Append($",\"runAtMs\":{entry.RunAtMs.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (entry.DurationMs.HasValue)
        {
            builder.Append($",\"durationMs\":{entry.DurationMs.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (entry.NextRunAtMs.HasValue)
        {
            builder.Append($",\"nextRunAtMs\":{entry.NextRunAtMs.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(entry.JobName))
        {
            builder.Append($",\"jobName\":\"{EscapeJson(entry.JobName)}\"");
        }

        builder.Append("}");
    }

    private static string ToJsonLongOrNull(long? value)
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : "null";
    }

    private static string ToJsonStringOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "null"
            : $"\"{EscapeJson(value)}\"";
    }

    private async Task SendRoutinesAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var items = _routineService.ListRoutines();
        var builder = new StringBuilder();
        builder.Append("{\"type\":\"routines_state\",\"items\":[");
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append(BuildRoutineJson(items[i]));
        }

        builder.Append("]}");
        await SendTextAsync(socket, sendLock, builder.ToString(), cancellationToken);
    }

    private async Task SendRoutineActionResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        RoutineActionResult result,
        CancellationToken cancellationToken
    )
    {
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"routine_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{EscapeJson(result.Message)}\","
            + $"\"routine\":{(result.Routine == null ? "null" : BuildRoutineJson(result.Routine))}"
            + "}",
            cancellationToken
        );
    }

    private async Task SendRoutineProgressAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        RoutineProgressUpdate update,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"routine_progress\","
                + $"\"operation\":\"{EscapeJson(update.Operation)}\","
                + $"\"message\":\"{EscapeJson(update.Message)}\","
                + $"\"percent\":{Math.Max(0, Math.Min(100, update.Percent))},"
                + $"\"done\":{(update.Done ? "true" : "false")},"
                + $"\"ok\":{(update.Ok.HasValue ? (update.Ok.Value ? "true" : "false") : "null")},"
                + $"\"stageKey\":\"{EscapeJson(update.StageKey)}\","
                + $"\"stageTitle\":\"{EscapeJson(update.StageTitle)}\","
                + $"\"stageDetail\":\"{EscapeJson(update.StageDetail)}\","
                + $"\"stageIndex\":{Math.Max(0, update.StageIndex)},"
                + $"\"stageTotal\":{Math.Max(0, update.StageTotal)}"
                + "}",
                cancellationToken
            );
        }
        catch
        {
        }
    }

    private Task SendLogicGraphListResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicGraphListResult result,
        CancellationToken cancellationToken
    )
    {
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"logic_graph_list_result\","
            + $"\"items\":{LogicGraphJson.Serialize(result.Items)}"
            + "}",
            cancellationToken
        );
    }

    private Task SendLogicGraphActionResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicGraphActionResult result,
        CancellationToken cancellationToken
    )
    {
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"logic_graph_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{EscapeJson(result.Message)}\","
            + $"\"summary\":{(result.Summary == null ? "null" : LogicGraphJson.Serialize(result.Summary))},"
            + $"\"graph\":{(result.Graph == null ? "null" : LogicGraphJson.Serialize(result.Graph))}"
            + "}",
            cancellationToken
        );
    }

    private Task SendLogicPathBrowseResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicPathBrowseResult result,
        CancellationToken cancellationToken
    )
    {
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"logic_path_list_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{EscapeJson(result.Message)}\","
            + $"\"scope\":\"{EscapeJson(result.Scope)}\","
            + $"\"rootKey\":\"{EscapeJson(result.RootKey)}\","
            + $"\"rootLabel\":\"{EscapeJson(result.RootLabel)}\","
            + $"\"displayPath\":\"{EscapeJson(result.DisplayPath)}\","
            + $"\"browsePath\":\"{EscapeJson(result.BrowsePath)}\","
            + $"\"parentBrowsePath\":{ToJsonStringOrNull(result.ParentBrowsePath)},"
            + $"\"directorySelectPath\":{ToJsonStringOrNull(result.DirectorySelectPath)},"
            + $"\"roots\":{LogicGraphJson.Serialize(result.Roots)},"
            + $"\"items\":{LogicGraphJson.Serialize(result.Items)}"
            + "}",
            cancellationToken
        );
    }

    private Task SendLogicRunActionResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicRunActionResult result,
        CancellationToken cancellationToken
    )
    {
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"logic_graph_run_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{EscapeJson(result.Message)}\","
            + $"\"runId\":{ToJsonStringOrNull(result.RunId)},"
            + $"\"snapshot\":{(result.Snapshot == null ? "null" : LogicGraphJson.Serialize(result.Snapshot))}"
            + "}",
            cancellationToken
        );
    }

    private Task SendLogicRunEventAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        LogicRunEvent result,
        CancellationToken cancellationToken
    )
    {
        return SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"logic_graph_run_event\","
            + $"\"runId\":\"{EscapeJson(result.RunId)}\","
            + $"\"graphId\":\"{EscapeJson(result.GraphId)}\","
            + $"\"kind\":\"{EscapeJson(result.Kind)}\","
            + $"\"message\":\"{EscapeJson(result.Message)}\","
            + $"\"nodeId\":{ToJsonStringOrNull(result.NodeId)},"
            + $"\"snapshot\":{LogicGraphJson.Serialize(result.Snapshot)}"
            + "}",
            cancellationToken
        );
    }

    private async Task SendChatResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        ConversationChatResult result,
        CancellationToken cancellationToken
    )
    {
        var retryDirective = ResolveRetryDirective(result.GuardFailure);
        var normalizedRetryStopReason = NormalizeWebSearchRetryStopReason(result.RetryStopReason);
        TrackGuardRetryTimelineEntry(
            channel: "chat",
            retryRequired: retryDirective.RetryRequired,
            retryAttempt: result.RetryAttempt,
            retryMaxAttempts: result.RetryMaxAttempts,
            retryStopReason: normalizedRetryStopReason
        );
        var finalizeStopwatch = Stopwatch.StartNew();
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"llm_chat_result\","
            + $"\"mode\":\"{EscapeJson(result.Mode)}\","
            + $"\"conversationId\":\"{EscapeJson(result.ConversationId)}\","
            + $"\"provider\":\"{EscapeJson(result.Provider)}\","
            + $"\"model\":\"{EscapeJson(result.Model)}\","
            + $"\"route\":\"{EscapeJson(result.Route)}\","
            + $"\"requestId\":\"{EscapeJson(result.RequestId ?? string.Empty)}\","
            + $"\"text\":\"{EscapeJson(result.Text)}\","
            + $"\"conversation\":{BuildConversationJson(result.Conversation)},"
            + $"\"autoMemoryNote\":{BuildMemoryNoteJson(result.AutoMemoryNote)},"
            + $"\"citations\":{BuildSearchCitationArrayJson(result.Citations)},"
            + $"\"citationMappings\":{BuildSearchCitationMappingArrayJson(result.CitationMappings)},"
            + $"\"citationValidation\":{BuildSearchCitationValidationJson(result.CitationValidation)},"
            + $"\"guardCategory\":\"{EscapeJson(NormalizeWebSearchGuardCategory(result.GuardFailure))}\","
            + $"\"guardReason\":\"{EscapeJson(NormalizeWebSearchGuardReason(result.GuardFailure))}\","
            + $"\"guardDetail\":\"{EscapeJson(NormalizeWebSearchGuardDetail(result.GuardFailure))}\","
            + $"\"retryRequired\":{(retryDirective.RetryRequired ? "true" : "false")},"
            + $"\"retryAction\":\"{EscapeJson(retryDirective.RetryAction)}\","
            + $"\"retryScope\":\"{EscapeJson(retryDirective.RetryScope)}\","
            + $"\"retryReason\":\"{EscapeJson(retryDirective.RetryReason)}\","
            + $"\"retryAttempt\":{Math.Max(0, result.RetryAttempt)},"
            + $"\"retryMaxAttempts\":{Math.Max(0, result.RetryMaxAttempts)},"
            + $"\"retryStopReason\":\"{EscapeJson(normalizedRetryStopReason)}\","
            + $"\"latency\":{BuildChatLatencyJson(result.Latency)}"
            + "}",
            cancellationToken
        );
        if (result.Latency != null
            && (result.Route.Equals("gemini-web-single", StringComparison.OrdinalIgnoreCase)
                || result.Route.Equals("gemini-url-single", StringComparison.OrdinalIgnoreCase)))
        {
            _auditLogger.Log(
                "web",
                "chat_single:web",
                "ok",
                $"decision_ms={result.Latency.DecisionMs.ToString(CultureInfo.InvariantCulture)} "
                + $"prompt_build_ms={result.Latency.PromptBuildMs.ToString(CultureInfo.InvariantCulture)} "
                + $"first_chunk_ms={result.Latency.FirstChunkMs.ToString(CultureInfo.InvariantCulture)} "
                + $"full_response_ms={result.Latency.FullResponseMs.ToString(CultureInfo.InvariantCulture)} "
                + $"sanitize_ms={result.Latency.SanitizeMs.ToString(CultureInfo.InvariantCulture)} "
                + $"ws_finalize_ms={Math.Max(0L, finalizeStopwatch.ElapsedMilliseconds).ToString(CultureInfo.InvariantCulture)} "
                + $"model={NormalizeAuditToken(result.Model, "-")} "
                + $"route={NormalizeAuditToken(result.Route, "-")} "
                + $"decision_path={NormalizeAuditToken(result.Latency.DecisionPath, "-")}"
            );
        }
    }

    private async Task SendChatStreamChunkAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        ChatStreamUpdate update,
        CancellationToken cancellationToken
    )
    {
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"llm_chat_stream_chunk\","
            + $"\"scope\":\"{EscapeJson(update.Scope)}\","
            + $"\"mode\":\"{EscapeJson(update.Mode)}\","
            + $"\"provider\":\"{EscapeJson(update.Provider)}\","
            + $"\"model\":\"{EscapeJson(update.Model)}\","
            + $"\"route\":\"{EscapeJson(update.Route)}\","
            + $"\"requestId\":\"{EscapeJson(update.RequestId ?? string.Empty)}\","
            + $"\"delta\":\"{EscapeJson(update.Delta)}\","
            + $"\"conversationId\":\"{EscapeJson(update.ConversationId)}\","
            + $"\"chunkIndex\":{Math.Max(0, update.ChunkIndex)}"
            + "}",
            cancellationToken
        );
    }

    private static string NormalizeAuditToken(string? token, string fallback)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return fallback;
        }

        var normalized = token.Trim();
        return normalized.Length == 0 ? fallback : normalized.Replace(' ', '_');
    }

    private async Task SendCodingResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CodingRunResult result,
        CancellationToken cancellationToken
    )
    {
        var retryDirective = ResolveRetryDirective(result.GuardFailure);
        var normalizedRetryStopReason = NormalizeWebSearchRetryStopReason(result.RetryStopReason);
        TrackGuardRetryTimelineEntry(
            channel: "coding",
            retryRequired: retryDirective.RetryRequired,
            retryAttempt: result.RetryAttempt,
            retryMaxAttempts: result.RetryMaxAttempts,
            retryStopReason: normalizedRetryStopReason
        );
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"coding_result\","
            + $"\"mode\":\"{EscapeJson(result.Mode)}\","
            + $"\"conversationId\":\"{EscapeJson(result.ConversationId)}\","
            + $"\"provider\":\"{EscapeJson(result.Provider)}\","
            + $"\"model\":\"{EscapeJson(result.Model)}\","
            + $"\"language\":\"{EscapeJson(result.Language)}\","
            + "\"code\":\"\","
            + $"\"summary\":\"{EscapeJson(result.Summary)}\","
            + $"\"commonSummary\":\"{EscapeJson(result.CommonSummary)}\","
            + $"\"commonPoints\":\"{EscapeJson(result.CommonPoints)}\","
            + $"\"differences\":\"{EscapeJson(result.Differences)}\","
            + $"\"recommendation\":\"{EscapeJson(result.Recommendation)}\","
            + $"\"execution\":{BuildExecutionJson(result.Execution)},"
            + $"\"workers\":{BuildCodingWorkersJson(result.Workers)},"
            + $"\"changedFiles\":{BuildStringArrayJson(result.ChangedFiles)},"
            + $"\"conversation\":{BuildConversationJson(result.Conversation)},"
            + $"\"autoMemoryNote\":{BuildMemoryNoteJson(result.AutoMemoryNote)},"
            + $"\"citations\":{BuildSearchCitationArrayJson(result.Citations)},"
            + $"\"citationMappings\":{BuildSearchCitationMappingArrayJson(result.CitationMappings)},"
            + $"\"citationValidation\":{BuildSearchCitationValidationJson(result.CitationValidation)},"
            + $"\"guardCategory\":\"{EscapeJson(NormalizeWebSearchGuardCategory(result.GuardFailure))}\","
            + $"\"guardReason\":\"{EscapeJson(NormalizeWebSearchGuardReason(result.GuardFailure))}\","
            + $"\"guardDetail\":\"{EscapeJson(NormalizeWebSearchGuardDetail(result.GuardFailure))}\","
            + $"\"retryRequired\":{(retryDirective.RetryRequired ? "true" : "false")},"
            + $"\"retryAction\":\"{EscapeJson(retryDirective.RetryAction)}\","
            + $"\"retryScope\":\"{EscapeJson(retryDirective.RetryScope)}\","
            + $"\"retryReason\":\"{EscapeJson(retryDirective.RetryReason)}\","
            + $"\"retryAttempt\":{Math.Max(0, result.RetryAttempt)},"
            + $"\"retryMaxAttempts\":{Math.Max(0, result.RetryMaxAttempts)},"
            + $"\"retryStopReason\":\"{EscapeJson(normalizedRetryStopReason)}\""
            + "}",
            cancellationToken
        );
    }

    private async Task SendCodingExecutionResultAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CodingResultExecutionResult result,
        CancellationToken cancellationToken
    )
    {
        await SendTextAsync(
            socket,
            sendLock,
            BuildCodingExecutionResultJson(result),
            cancellationToken
        );
    }

    private async Task SendCodingProgressAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string scope,
        string mode,
        CodingProgressUpdate update,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var effectiveMode = string.IsNullOrWhiteSpace(update.Mode) ? mode : update.Mode;
            await SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"coding_progress\","
                + $"\"scope\":\"{EscapeJson(scope)}\","
                + $"\"mode\":\"{EscapeJson(effectiveMode)}\","
                + $"\"provider\":\"{EscapeJson(update.Provider)}\","
                + $"\"model\":\"{EscapeJson(update.Model)}\","
                + $"\"phase\":\"{EscapeJson(update.Phase)}\","
                + $"\"message\":\"{EscapeJson(update.Message)}\","
                + $"\"iteration\":{update.Iteration},"
                + $"\"maxIterations\":{update.MaxIterations},"
                + $"\"percent\":{update.Percent},"
                + $"\"done\":{(update.Done ? "true" : "false")},"
                + $"\"stageKey\":\"{EscapeJson(update.StageKey)}\","
                + $"\"stageTitle\":\"{EscapeJson(update.StageTitle)}\","
                + $"\"stageDetail\":\"{EscapeJson(update.StageDetail)}\","
                + $"\"stageIndex\":{update.StageIndex},"
                + $"\"stageTotal\":{update.StageTotal}"
                + "}",
                cancellationToken
            );
        }
        catch
        {
        }
    }

    private async Task SendRoutineRunDetailAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        RoutineRunDetailResult result,
        CancellationToken cancellationToken
    )
    {
        await SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"routine_run_detail\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"routineId\":\"{EscapeJson(result.RoutineId)}\","
            + $"\"ts\":{result.Ts.ToString(CultureInfo.InvariantCulture)},"
            + $"\"runAtLocal\":\"{EscapeJson(DateTimeOffset.FromUnixTimeMilliseconds(result.Ts).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))}\","
            + $"\"title\":\"{EscapeJson(result.Title)}\","
            + $"\"status\":\"{EscapeJson(result.Status)}\","
            + $"\"source\":\"{EscapeJson(result.Source)}\","
            + $"\"attemptCount\":{result.AttemptCount.ToString(CultureInfo.InvariantCulture)},"
            + $"\"telegramStatus\":{ToJsonStringOrNull(result.TelegramStatus)},"
            + $"\"artifactPath\":{ToJsonStringOrNull(result.ArtifactPath)},"
            + $"\"agentSessionId\":{ToJsonStringOrNull(result.AgentSessionId)},"
            + $"\"agentRunId\":{ToJsonStringOrNull(result.AgentRunId)},"
            + $"\"agentProvider\":{ToJsonStringOrNull(result.AgentProvider)},"
            + $"\"agentModel\":{ToJsonStringOrNull(result.AgentModel)},"
            + $"\"toolProfile\":{ToJsonStringOrNull(result.ToolProfile)},"
            + $"\"startUrl\":{ToJsonStringOrNull(result.StartUrl)},"
            + $"\"finalUrl\":{ToJsonStringOrNull(result.FinalUrl)},"
            + $"\"pageTitle\":{ToJsonStringOrNull(result.PageTitle)},"
            + $"\"screenshotPath\":{ToJsonStringOrNull(result.ScreenshotPath)},"
            + $"\"downloadPaths\":{BuildStringArrayJson(result.DownloadPaths)},"
            + $"\"error\":{ToJsonStringOrNull(result.Error)},"
            + $"\"content\":\"{EscapeJson(result.Content)}\""
            + "}",
            cancellationToken
        );
    }

    private async Task SendMetricsAsync(WebSocket socket, SemaphoreSlim sendLock, string type, string metricsRaw, CancellationToken cancellationToken)
    {
        if (LooksLikeJson(metricsRaw))
        {
            await SendTextAsync(socket, sendLock, $"{{\"type\":\"{type}\",\"payload\":{metricsRaw}}}", cancellationToken);
            return;
        }

        await SendTextAsync(
            socket,
            sendLock,
            $"{{\"type\":\"{type}\",\"payload\":\"{EscapeJson(metricsRaw)}\"}}",
            cancellationToken
        );
    }

    private async Task<GuardAlertDispatchResult> DispatchGuardAlertEventAsync(
        string guardAlertEventJson,
        CancellationToken cancellationToken
    )
    {
        var attemptedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var schemaVersion = GuardAlertSchemaVersion;
        var eventType = GuardAlertEventType;

        try
        {
            using var eventDoc = JsonDocument.Parse(guardAlertEventJson);
            if (eventDoc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new GuardAlertDispatchResult(
                    Ok: false,
                    Status: "invalid_payload",
                    Message: "guardAlertEvent must be an object",
                    SchemaVersion: schemaVersion,
                    EventType: eventType,
                    AttemptedAtUtc: attemptedAtUtc,
                    Targets: Array.Empty<GuardAlertDispatchTargetResult>()
                );
            }

            if (eventDoc.RootElement.TryGetProperty("schemaVersion", out var schemaVersionElement)
                && schemaVersionElement.ValueKind == JsonValueKind.String)
            {
                schemaVersion = schemaVersionElement.GetString()?.Trim() ?? schemaVersion;
            }

            if (eventDoc.RootElement.TryGetProperty("eventType", out var eventTypeElement)
                && eventTypeElement.ValueKind == JsonValueKind.String)
            {
                eventType = eventTypeElement.GetString()?.Trim() ?? eventType;
            }
        }
        catch (JsonException ex)
        {
            return new GuardAlertDispatchResult(
                Ok: false,
                Status: "invalid_json",
                Message: $"guardAlertEvent parse failed: {TrimTo(ex.Message, 120)}",
                SchemaVersion: schemaVersion,
                EventType: eventType,
                AttemptedAtUtc: attemptedAtUtc,
                Targets: Array.Empty<GuardAlertDispatchTargetResult>()
            );
        }

        if (!string.Equals(schemaVersion, GuardAlertSchemaVersion, StringComparison.Ordinal))
        {
            return new GuardAlertDispatchResult(
                Ok: false,
                Status: "invalid_schema_version",
                Message: $"schemaVersion must be {GuardAlertSchemaVersion}",
                SchemaVersion: schemaVersion,
                EventType: eventType,
                AttemptedAtUtc: attemptedAtUtc,
                Targets: Array.Empty<GuardAlertDispatchTargetResult>()
            );
        }

        if (!string.Equals(eventType, GuardAlertEventType, StringComparison.Ordinal))
        {
            return new GuardAlertDispatchResult(
                Ok: false,
                Status: "invalid_event_type",
                Message: $"eventType must be {GuardAlertEventType}",
                SchemaVersion: schemaVersion,
                EventType: eventType,
                AttemptedAtUtc: attemptedAtUtc,
                Targets: Array.Empty<GuardAlertDispatchTargetResult>()
            );
        }

        var targetConfigs = new (string Name, string Url)[]
        {
            ("webhook", _config.GuardAlertWebhookUrl),
            ("log_collector", _config.GuardAlertLogCollectorUrl)
        };
        var targetResults = new List<GuardAlertDispatchTargetResult>(targetConfigs.Length);

        foreach (var target in targetConfigs)
        {
            if (string.IsNullOrWhiteSpace(target.Url))
            {
                targetResults.Add(
                    new GuardAlertDispatchTargetResult(
                        Name: target.Name,
                        Status: "skipped",
                        Attempts: 0,
                        StatusCode: null,
                        Error: "target_not_configured",
                        Endpoint: "-"
                    )
                );
                continue;
            }

            if (!TryResolveGuardAlertEndpoint(target.Url, out var endpointUri, out var endpointDisplay, out var endpointError))
            {
                targetResults.Add(
                    new GuardAlertDispatchTargetResult(
                        Name: target.Name,
                        Status: "failed",
                        Attempts: 0,
                        StatusCode: null,
                        Error: endpointError,
                        Endpoint: endpointDisplay
                    )
                );
                continue;
            }

            var targetResult = await SendGuardAlertEventToTargetAsync(
                target.Name,
                endpointUri!,
                endpointDisplay,
                guardAlertEventJson,
                schemaVersion,
                eventType,
                cancellationToken
            );
            targetResults.Add(targetResult);
        }

        var sentCount = targetResults.Count(x => x.Status == "sent");
        var failedCount = targetResults.Count(x => x.Status == "failed");
        var skippedCount = targetResults.Count(x => x.Status == "skipped");
        var ok = failedCount == 0 && sentCount > 0;
        var status = ok
            ? "sent"
            : failedCount > 0
                ? (sentCount > 0 ? "partial_failed" : "failed")
                : "no_target_configured";
        var message = ok
            ? $"guard alert dispatched ({sentCount} targets)"
            : status switch
            {
                "partial_failed" => $"guard alert partial failure (sent={sentCount}, failed={failedCount}, skipped={skippedCount})",
                "failed" => $"guard alert dispatch failed (failed={failedCount}, skipped={skippedCount})",
                "no_target_configured" => "guard alert dispatch skipped: no target configured",
                _ => "guard alert dispatch failed"
            };

        if (ok)
        {
            Console.WriteLine($"[guard-alert] {message}");
        }
        else
        {
            Console.Error.WriteLine($"[guard-alert] {message}");
        }

        return new GuardAlertDispatchResult(
            Ok: ok,
            Status: status,
            Message: message,
            SchemaVersion: schemaVersion,
            EventType: eventType,
            AttemptedAtUtc: attemptedAtUtc,
            Targets: targetResults
        );
    }

    private async Task<GuardAlertDispatchTargetResult> SendGuardAlertEventToTargetAsync(
        string targetName,
        Uri endpointUri,
        string endpointDisplay,
        string guardAlertEventJson,
        string schemaVersion,
        string eventType,
        CancellationToken cancellationToken
    )
    {
        var maxAttempts = Math.Max(1, _config.GuardAlertDispatchMaxAttempts);
        var timeoutMs = Math.Clamp(_config.GuardAlertDispatchTimeoutMs, 500, 120000);
        var lastError = "dispatch_failed";
        int? lastStatusCode = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt += 1)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri);
                request.Headers.TryAddWithoutValidation("X-OmniNode-Event-Type", eventType);
                request.Headers.TryAddWithoutValidation("X-OmniNode-Schema-Version", schemaVersion);
                request.Headers.TryAddWithoutValidation("X-OmniNode-Dispatch-Source", "websocket_gateway");
                request.Content = new StringContent(guardAlertEventJson, Encoding.UTF8, "application/json");

                using var response = await GuardAlertPipelineHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token
                );
                lastStatusCode = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    return new GuardAlertDispatchTargetResult(
                        Name: targetName,
                        Status: "sent",
                        Attempts: attempt,
                        StatusCode: lastStatusCode,
                        Error: "-",
                        Endpoint: endpointDisplay
                    );
                }

                var failureBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                lastError = $"http_{lastStatusCode}";
                var detail = TrimTo(failureBody, 160).Trim();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    lastError = $"{lastError}: {detail}";
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = "timeout";
            }
            catch (Exception ex)
            {
                var detail = TrimTo(ex.Message, 160).Trim();
                lastError = string.IsNullOrWhiteSpace(detail) ? "request_exception" : detail;
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }

        return new GuardAlertDispatchTargetResult(
            Name: targetName,
            Status: "failed",
            Attempts: maxAttempts,
            StatusCode: lastStatusCode,
            Error: lastError,
            Endpoint: endpointDisplay
        );
    }

    private static bool TryResolveGuardAlertEndpoint(
        string rawUrl,
        out Uri? endpointUri,
        out string endpointDisplay,
        out string error
    )
    {
        endpointUri = null;
        endpointDisplay = "-";
        error = "invalid_target_url";
        var normalized = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "target_not_configured";
            return false;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            error = "invalid_target_url";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "invalid_target_scheme";
            return false;
        }

        endpointUri = uri;
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        endpointDisplay = builder.Uri.ToString();
        if (endpointDisplay.EndsWith("/", StringComparison.Ordinal) && builder.Uri.AbsolutePath != "/")
        {
            endpointDisplay = endpointDisplay[..^1];
        }

        error = "-";
        return true;
    }

    private static string BuildGuardAlertDispatchResultJson(GuardAlertDispatchResult result)
    {
        var sentCount = result.Targets.Count(x => x.Status == "sent");
        var failedCount = result.Targets.Count(x => x.Status == "failed");
        var skippedCount = result.Targets.Count(x => x.Status == "skipped");
        var builder = new StringBuilder(512);
        builder.Append("{");
        builder.Append($"\"type\":\"{GuardAlertDispatchResultType}\",");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"status\":\"{EscapeJson(result.Status)}\",");
        builder.Append($"\"message\":\"{EscapeJson(result.Message)}\",");
        builder.Append($"\"schemaVersion\":\"{EscapeJson(result.SchemaVersion)}\",");
        builder.Append($"\"eventType\":\"{EscapeJson(result.EventType)}\",");
        builder.Append($"\"attemptedAtUtc\":\"{EscapeJson(result.AttemptedAtUtc)}\",");
        builder.Append($"\"sentCount\":{sentCount},");
        builder.Append($"\"failedCount\":{failedCount},");
        builder.Append($"\"skippedCount\":{skippedCount},");
        builder.Append("\"targets\":[");
        for (var i = 0; i < result.Targets.Count; i += 1)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            var target = result.Targets[i];
            builder.Append("{");
            builder.Append($"\"name\":\"{EscapeJson(target.Name)}\",");
            builder.Append($"\"status\":\"{EscapeJson(target.Status)}\",");
            builder.Append($"\"attempts\":{Math.Max(0, target.Attempts)},");
            builder.Append($"\"statusCode\":{(target.StatusCode.HasValue ? target.StatusCode.Value.ToString(CultureInfo.InvariantCulture) : "null")},");
            builder.Append($"\"error\":\"{EscapeJson(target.Error)}\",");
            builder.Append($"\"endpoint\":\"{EscapeJson(target.Endpoint)}\"");
            builder.Append("}");
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static ClientMessage? ParseClientMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("type", out var typeElement))
            {
                return null;
            }

            var type = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            string? otp = null;
            string? authToken = null;
            string? text = null;
            string? message = null;
            string? query = null;
            string? search = null;
            string? freshness = null;
            string? memoryPath = null;
            string? webFetchUrl = null;
            string? extractMode = null;
            string? model = null;
            string? provider = null;
            string? groqModel = null;
            string? geminiModel = null;
            string? copilotModel = null;
            string? cerebrasModel = null;
            string? nvidiaModel = null;
            string? codexModel = null;
            string? summaryProvider = null;
            string? action = null;
            string? jobId = null;
            string? cronId = null;
            string? runMode = null;
            string? target = null;
            string? targetId = null;
            string? node = null;
            string? requestId = null;
            string? title = null;
            string? body = null;
            string? priority = null;
            string? delivery = null;
            string? invokeCommand = null;
            string? invokeParamsJson = null;
            string? spawnTask = null;
            string? label = null;
            string? runtime = null;
            string? sessionKey = null;
            string? scope = null;
            string? mode = null;
            string? profile = null;
            string? outputFormat = null;
            string? conversationId = null;
            string? standardInput = null;
            string? conversationTitle = null;
            string? project = null;
            string? projectKey = null;
            string? category = null;
            string? kind = null;
            string? planId = null;
            string? graphId = null;
            string? logicGraphJson = null;
            string? logicRunId = null;
            string? logicRunInput = null;
            string? taskId = null;
            string? previewId = null;
            string? language = null;
            string? symbol = null;
            string? pattern = null;
            string? replacement = null;
            string? noteName = null;
            string? newName = null;
            string? filePath = null;
            string? routineId = null;
            string? executionMode = null;
            string? agentProvider = null;
            string? agentModel = null;
            string? agentStartUrl = null;
            string? agentToolProfile = null;
            string? scheduleSourceMode = null;
            string? notifyPolicy = null;
            bool? notifyTelegram = null;
            string? scheduleKind = null;
            string? scheduleTime = null;
            string? timezoneId = null;
            string? telegramBotToken = null;
            string? telegramChatId = null;
            string? groqApiKey = null;
            string? geminiApiKey = null;
            string? cerebrasApiKey = null;
            string? nvidiaApiKey = null;
            string? codexApiKey = null;
            string? routingPolicyJson = null;
            string? refactorEditsJson = null;
            string? contentBase64 = null;
            string? fileName = null;
            int? authTtlHours = null;
            int? timeoutSeconds = null;
            int? runTimeoutSeconds = null;
            int? limit = null;
            int? offset = null;
            int? count = null;
            int? activeMinutes = null;
            int? messageLimit = null;
            int? maxResults = null;
            int? maxChars = null;
            int? maxWidth = null;
            double? minScore = null;
            int? fromLine = null;
            int? lines = null;
            int? dayOfMonth = null;
            int? maxRetries = null;
            int? retryDelaySeconds = null;
            int? agentTimeoutSeconds = null;
            long? timestamp = null;
            bool? enabled = null;
            bool? agentUsePlaywright = null;
            bool? runImmediately = null;
            bool? overwrite = null;
            bool? compactConversation = null;
            bool? includeDisabled = null;
            bool? includeTools = null;
            bool? thread = null;
            var attachments = new List<InputAttachment>();
            var memoryNotes = new List<string>();
            var tags = new List<string>();
            var constraints = new List<string>();
            var kinds = new List<string>();
            var weekdays = new List<int>();
            var webUrls = new List<string>();
            var webSearchEnabled = true;
            var persist = false;

            if (doc.RootElement.TryGetProperty("otp", out var otpElement))
            {
                otp = otpElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("authToken", out var authTokenElement))
            {
                authToken = authTokenElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                text = textElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(text)
                && doc.RootElement.TryGetProperty("content", out var contentElement))
            {
                text = contentElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("query", out var queryElement))
            {
                query = queryElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("search", out var searchElement))
            {
                search = searchElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("freshness", out var freshnessElement))
            {
                freshness = freshnessElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("url", out var webFetchUrlElement))
            {
                webFetchUrl = webFetchUrlElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("extractMode", out var extractModeElement))
            {
                extractMode = extractModeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("path", out var pathElement))
            {
                memoryPath = pathElement.GetString();
                filePath = pathElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("model", out var modelElement))
            {
                model = modelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("provider", out var providerElement))
            {
                provider = providerElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("groqModel", out var groqModelElement))
            {
                groqModel = groqModelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("geminiModel", out var geminiModelElement))
            {
                geminiModel = geminiModelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("copilotModel", out var copilotModelElement))
            {
                copilotModel = copilotModelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("cerebrasModel", out var cerebrasModelElement))
            {
                cerebrasModel = cerebrasModelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("nvidiaModel", out var nvidiaModelElement))
            {
                nvidiaModel = nvidiaModelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("codexModel", out var codexModelElement))
            {
                codexModel = codexModelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("policy", out var policyElement))
            {
                routingPolicyJson = policyElement.ValueKind == JsonValueKind.String
                    ? policyElement.GetString()
                    : policyElement.GetRawText();
            }

            if (string.IsNullOrWhiteSpace(routingPolicyJson)
                && doc.RootElement.TryGetProperty("routingPolicy", out var routingPolicyElement))
            {
                routingPolicyJson = routingPolicyElement.ValueKind == JsonValueKind.String
                    ? routingPolicyElement.GetString()
                    : routingPolicyElement.GetRawText();
            }

            if (doc.RootElement.TryGetProperty("summaryProvider", out var summaryProviderElement))
            {
                summaryProvider = summaryProviderElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("action", out var actionElement))
            {
                action = actionElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("jobId", out var jobIdElement))
            {
                jobId = jobIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("id", out var idElement))
            {
                cronId = idElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("runMode", out var runModeElement))
            {
                runMode = runModeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("target", out var targetElement))
            {
                target = targetElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("targetId", out var targetIdElement))
            {
                targetId = targetIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("node", out var nodeElement))
            {
                node = nodeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("requestId", out var requestIdElement))
            {
                requestId = requestIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("title", out var titleElement))
            {
                title = titleElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("body", out var bodyElement))
            {
                body = bodyElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("priority", out var priorityElement))
            {
                priority = priorityElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("delivery", out var deliveryElement))
            {
                delivery = deliveryElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("invokeCommand", out var invokeCommandElement))
            {
                invokeCommand = invokeCommandElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("invokeParamsJson", out var invokeParamsElement))
            {
                if (invokeParamsElement.ValueKind == JsonValueKind.String)
                {
                    invokeParamsJson = invokeParamsElement.GetString();
                }
                else
                {
                    invokeParamsJson = invokeParamsElement.GetRawText();
                }
            }

            if (doc.RootElement.TryGetProperty("task", out var taskElement))
            {
                spawnTask = taskElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("label", out var labelElement))
            {
                label = labelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("runtime", out var runtimeElement))
            {
                runtime = runtimeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("sessionKey", out var sessionKeyElement))
            {
                sessionKey = sessionKeyElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("scope", out var scopeElement))
            {
                scope = scopeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("mode", out var modeElement))
            {
                mode = modeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("profile", out var profileElement))
            {
                profile = profileElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("outputFormat", out var outputFormatElement))
            {
                outputFormat = outputFormatElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("conversationId", out var conversationIdElement))
            {
                conversationId = conversationIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("stdin", out var standardInputElement)
                && standardInputElement.ValueKind == JsonValueKind.String)
            {
                standardInput = standardInputElement.GetString();
            }
            else if (doc.RootElement.TryGetProperty("standardInput", out var explicitStandardInputElement)
                     && explicitStandardInputElement.ValueKind == JsonValueKind.String)
            {
                standardInput = explicitStandardInputElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("conversationTitle", out var conversationTitleElement))
            {
                conversationTitle = conversationTitleElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("project", out var projectElement))
            {
                project = projectElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("projectKey", out var projectKeyElement))
            {
                projectKey = projectKeyElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("category", out var categoryElement))
            {
                category = categoryElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("kind", out var kindElement))
            {
                kind = kindElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("planId", out var planIdElement))
            {
                planId = planIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("graphId", out var graphIdElement))
            {
                graphId = graphIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("logicGraph", out var logicGraphElement))
            {
                logicGraphJson = logicGraphElement.ValueKind == JsonValueKind.String
                    ? logicGraphElement.GetString()
                    : logicGraphElement.GetRawText();
            }

            if (string.IsNullOrWhiteSpace(logicGraphJson)
                && doc.RootElement.TryGetProperty("logicGraphJson", out var logicGraphJsonElement))
            {
                logicGraphJson = logicGraphJsonElement.ValueKind == JsonValueKind.String
                    ? logicGraphJsonElement.GetString()
                    : logicGraphJsonElement.GetRawText();
            }

            if (doc.RootElement.TryGetProperty("runId", out var runIdElement))
            {
                logicRunId = runIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("logicRunInput", out var logicRunInputElement))
            {
                logicRunInput = logicRunInputElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("taskId", out var taskIdElement))
            {
                taskId = taskIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("previewId", out var previewIdElement))
            {
                previewId = previewIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("fileName", out var fileNameElement))
            {
                fileName = fileNameElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("contentBase64", out var contentBase64Element))
            {
                contentBase64 = contentBase64Element.GetString();
            }

            if (doc.RootElement.TryGetProperty("tags", out var tagsElement)
                && tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tagsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        tags.Add(value.Trim());
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("constraints", out var constraintsElement)
                && constraintsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in constraintsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = item.GetString();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    constraints.Add(value.Trim());
                    if (constraints.Count >= 16)
                    {
                        break;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("kinds", out var kindsElement)
                && kindsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in kindsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = item.GetString();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    kinds.Add(value.Trim());
                    if (kinds.Count >= 16)
                    {
                        break;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("language", out var languageElement))
            {
                language = languageElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("symbol", out var symbolElement))
            {
                symbol = symbolElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("pattern", out var patternElement))
            {
                pattern = patternElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("replacement", out var replacementElement))
            {
                replacement = replacementElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("noteName", out var noteNameElement))
            {
                noteName = noteNameElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("newName", out var newNameElement))
            {
                newName = newNameElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("filePath", out var filePathElement))
            {
                filePath = filePathElement.GetString();
            }

            string? skillName = null;
            string? skillScope = null;
            string? skillDescription = null;
            string? skillBody = null;
            bool? thinkPlus = null;
            if (doc.RootElement.TryGetProperty("skillName", out var skillNameEl))
            {
                skillName = skillNameEl.GetString();
            }
            if (doc.RootElement.TryGetProperty("skillScope", out var skillScopeEl))
            {
                skillScope = skillScopeEl.GetString();
            }
            if (doc.RootElement.TryGetProperty("skillDescription", out var skillDescEl))
            {
                skillDescription = skillDescEl.GetString();
            }
            if (doc.RootElement.TryGetProperty("skillBody", out var skillBodyEl))
            {
                skillBody = skillBodyEl.GetString();
            }
            if (doc.RootElement.TryGetProperty("thinkPlus", out var thinkPlusEl))
            {
                if (thinkPlusEl.ValueKind == JsonValueKind.True) thinkPlus = true;
                else if (thinkPlusEl.ValueKind == JsonValueKind.False) thinkPlus = false;
            }

            if (doc.RootElement.TryGetProperty("edits", out var editsElement))
            {
                refactorEditsJson = editsElement.GetRawText();
            }

            if (doc.RootElement.TryGetProperty("routineId", out var routineIdElement))
            {
                routineId = routineIdElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("executionMode", out var executionModeElement))
            {
                executionMode = executionModeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("agentProvider", out var agentProviderElement))
            {
                agentProvider = agentProviderElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("agentModel", out var agentModelElement))
            {
                agentModel = agentModelElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("agentStartUrl", out var agentStartUrlElement))
            {
                agentStartUrl = agentStartUrlElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("scheduleKind", out var scheduleKindElement))
            {
                scheduleKind = scheduleKindElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("scheduleSourceMode", out var scheduleSourceModeElement))
            {
                scheduleSourceMode = scheduleSourceModeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("notifyPolicy", out var notifyPolicyElement))
            {
                notifyPolicy = notifyPolicyElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("notifyTelegram", out var notifyTelegramElement))
            {
                if (notifyTelegramElement.ValueKind == JsonValueKind.True || notifyTelegramElement.ValueKind == JsonValueKind.False)
                {
                    notifyTelegram = notifyTelegramElement.GetBoolean();
                }
                else if (notifyTelegramElement.ValueKind == JsonValueKind.String
                         && bool.TryParse(notifyTelegramElement.GetString(), out var parsedNotifyTelegram))
                {
                    notifyTelegram = parsedNotifyTelegram;
                }
            }

            if (doc.RootElement.TryGetProperty("scheduleTime", out var scheduleTimeElement))
            {
                scheduleTime = scheduleTimeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("timezoneId", out var timezoneElement))
            {
                timezoneId = timezoneElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("dayOfMonth", out var dayOfMonthElement))
            {
                if (dayOfMonthElement.ValueKind == JsonValueKind.Number && dayOfMonthElement.TryGetInt32(out var parsedDayOfMonth))
                {
                    dayOfMonth = parsedDayOfMonth;
                }
                else if (dayOfMonthElement.ValueKind == JsonValueKind.String
                         && int.TryParse(dayOfMonthElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDayOfMonthString))
                {
                    dayOfMonth = parsedDayOfMonthString;
                }
            }

            if (doc.RootElement.TryGetProperty("maxRetries", out var maxRetriesElement))
            {
                if (maxRetriesElement.ValueKind == JsonValueKind.Number && maxRetriesElement.TryGetInt32(out var parsedMaxRetries))
                {
                    maxRetries = parsedMaxRetries;
                }
                else if (maxRetriesElement.ValueKind == JsonValueKind.String
                         && int.TryParse(maxRetriesElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxRetriesString))
                {
                    maxRetries = parsedMaxRetriesString;
                }
            }

            if (doc.RootElement.TryGetProperty("retryDelaySeconds", out var retryDelaySecondsElement))
            {
                if (retryDelaySecondsElement.ValueKind == JsonValueKind.Number && retryDelaySecondsElement.TryGetInt32(out var parsedRetryDelay))
                {
                    retryDelaySeconds = parsedRetryDelay;
                }
                else if (retryDelaySecondsElement.ValueKind == JsonValueKind.String
                         && int.TryParse(retryDelaySecondsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRetryDelayString))
                {
                    retryDelaySeconds = parsedRetryDelayString;
                }
            }

            if (doc.RootElement.TryGetProperty("agentTimeoutSeconds", out var agentTimeoutSecondsElement))
            {
                if (agentTimeoutSecondsElement.ValueKind == JsonValueKind.Number && agentTimeoutSecondsElement.TryGetInt32(out var parsedAgentTimeout))
                {
                    agentTimeoutSeconds = parsedAgentTimeout;
                }
                else if (agentTimeoutSecondsElement.ValueKind == JsonValueKind.String
                         && int.TryParse(agentTimeoutSecondsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAgentTimeoutString))
                {
                    agentTimeoutSeconds = parsedAgentTimeoutString;
                }
            }

            if (doc.RootElement.TryGetProperty("ts", out var timestampElement))
            {
                if (timestampElement.ValueKind == JsonValueKind.Number && timestampElement.TryGetInt64(out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }
                else if (timestampElement.ValueKind == JsonValueKind.String
                         && long.TryParse(timestampElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimestampString))
                {
                    timestamp = parsedTimestampString;
                }
            }

            if (doc.RootElement.TryGetProperty("weekdays", out var weekdaysElement)
                && weekdaysElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in weekdaysElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var weekdayValue))
                    {
                        weekdays.Add(weekdayValue);
                    }
                    else if (item.ValueKind == JsonValueKind.String
                             && int.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weekdayStringValue))
                    {
                        weekdays.Add(weekdayStringValue);
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("memoryNotes", out var memoryNotesElement)
                && memoryNotesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in memoryNotesElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        memoryNotes.Add(value.Trim());
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("webUrls", out var webUrlsElement)
                && webUrlsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in webUrlsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = item.GetString();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var normalized = value.Trim();
                    if (normalized.Length > 512)
                    {
                        normalized = normalized[..512];
                    }

                    webUrls.Add(normalized);
                    if (webUrls.Count >= 8)
                    {
                        break;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("webSearchEnabled", out var webSearchElement))
            {
                if (webSearchElement.ValueKind == JsonValueKind.False)
                {
                    webSearchEnabled = false;
                }
                else if (webSearchElement.ValueKind == JsonValueKind.True)
                {
                    webSearchEnabled = true;
                }
            }

            if (doc.RootElement.TryGetProperty("attachments", out var attachmentsElement)
                && attachmentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in attachmentsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                        ? (nameElement.GetString() ?? string.Empty).Trim()
                        : string.Empty;
                    var mimeType = item.TryGetProperty("mimeType", out var mimeElement) && mimeElement.ValueKind == JsonValueKind.String
                        ? (mimeElement.GetString() ?? string.Empty).Trim()
                        : "application/octet-stream";
                    var dataBase64 = item.TryGetProperty("dataBase64", out var dataElement) && dataElement.ValueKind == JsonValueKind.String
                        ? (dataElement.GetString() ?? string.Empty).Trim()
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(dataBase64))
                    {
                        continue;
                    }

                    long sizeBytes = 0;
                    if (item.TryGetProperty("sizeBytes", out var sizeElement))
                    {
                        if (sizeElement.ValueKind == JsonValueKind.Number && sizeElement.TryGetInt64(out var parsedLong))
                        {
                            sizeBytes = parsedLong;
                        }
                        else if (sizeElement.ValueKind == JsonValueKind.String
                                 && long.TryParse(sizeElement.GetString(), out var parsedString))
                        {
                            sizeBytes = parsedString;
                        }
                    }

                    var isImage = item.TryGetProperty("isImage", out var isImageElement)
                                  && isImageElement.ValueKind == JsonValueKind.True;
                    if (dataBase64.Length > 700_000)
                    {
                        dataBase64 = dataBase64[..700_000];
                    }

                    attachments.Add(new InputAttachment(name, mimeType, dataBase64, sizeBytes, isImage));
                    if (attachments.Count >= 6)
                    {
                        break;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("telegramBotToken", out var tBot))
            {
                telegramBotToken = tBot.GetString();
            }

            if (doc.RootElement.TryGetProperty("telegramChatId", out var tChat))
            {
                telegramChatId = tChat.GetString();
            }

            if (doc.RootElement.TryGetProperty("groqApiKey", out var gKey))
            {
                groqApiKey = gKey.GetString();
            }

            if (doc.RootElement.TryGetProperty("geminiApiKey", out var geKey))
            {
                geminiApiKey = geKey.GetString();
            }

            if (doc.RootElement.TryGetProperty("cerebrasApiKey", out var cerebrasKey))
            {
                cerebrasApiKey = cerebrasKey.GetString();
            }

            if (doc.RootElement.TryGetProperty("nvidiaApiKey", out var nvidiaKey))
            {
                nvidiaApiKey = nvidiaKey.GetString();
            }

            if (doc.RootElement.TryGetProperty("codexApiKey", out var codexKey))
            {
                codexApiKey = codexKey.GetString();
            }

            if (doc.RootElement.TryGetProperty("persist", out var persistElement) && persistElement.ValueKind == JsonValueKind.True)
            {
                persist = true;
            }

            if (doc.RootElement.TryGetProperty("enabled", out var enabledElement))
            {
                if (enabledElement.ValueKind == JsonValueKind.True)
                {
                    enabled = true;
                }
                else if (enabledElement.ValueKind == JsonValueKind.False)
                {
                    enabled = false;
                }
            }

            if (doc.RootElement.TryGetProperty("agentUsePlaywright", out var agentUsePlaywrightElement))
            {
                if (agentUsePlaywrightElement.ValueKind == JsonValueKind.True)
                {
                    agentUsePlaywright = true;
                }
                else if (agentUsePlaywrightElement.ValueKind == JsonValueKind.False)
                {
                    agentUsePlaywright = false;
                }
            }

            if (doc.RootElement.TryGetProperty("runImmediately", out var runImmediatelyElement))
            {
                if (runImmediatelyElement.ValueKind == JsonValueKind.True)
                {
                    runImmediately = true;
                }
                else if (runImmediatelyElement.ValueKind == JsonValueKind.False)
                {
                    runImmediately = false;
                }
            }

            if (doc.RootElement.TryGetProperty("overwrite", out var overwriteElement))
            {
                if (overwriteElement.ValueKind == JsonValueKind.True)
                {
                    overwrite = true;
                }
                else if (overwriteElement.ValueKind == JsonValueKind.False)
                {
                    overwrite = false;
                }
            }

            if (doc.RootElement.TryGetProperty("agentToolProfile", out var agentToolProfileElement)
                && agentToolProfileElement.ValueKind == JsonValueKind.String)
            {
                agentToolProfile = agentToolProfileElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("compactConversation", out var compactConversationElement))
            {
                if (compactConversationElement.ValueKind == JsonValueKind.True)
                {
                    compactConversation = true;
                }
                else if (compactConversationElement.ValueKind == JsonValueKind.False)
                {
                    compactConversation = false;
                }
            }

            if (doc.RootElement.TryGetProperty("includeTools", out var includeToolsElement))
            {
                if (includeToolsElement.ValueKind == JsonValueKind.True)
                {
                    includeTools = true;
                }
                else if (includeToolsElement.ValueKind == JsonValueKind.False)
                {
                    includeTools = false;
                }
            }

            if (doc.RootElement.TryGetProperty("includeDisabled", out var includeDisabledElement))
            {
                if (includeDisabledElement.ValueKind == JsonValueKind.True)
                {
                    includeDisabled = true;
                }
                else if (includeDisabledElement.ValueKind == JsonValueKind.False)
                {
                    includeDisabled = false;
                }
            }

            if (doc.RootElement.TryGetProperty("thread", out var threadElement))
            {
                if (threadElement.ValueKind == JsonValueKind.True)
                {
                    thread = true;
                }
                else if (threadElement.ValueKind == JsonValueKind.False)
                {
                    thread = false;
                }
            }

            if (doc.RootElement.TryGetProperty("authTtlHours", out var ttlElement))
            {
                if (ttlElement.ValueKind == JsonValueKind.Number && ttlElement.TryGetInt32(out var ttlInt))
                {
                    authTtlHours = ttlInt;
                }
                else if (ttlElement.ValueKind == JsonValueKind.String
                         && int.TryParse(ttlElement.GetString(), out var ttlParsed))
                {
                    authTtlHours = ttlParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("timeoutSeconds", out var timeoutSecondsElement))
            {
                if (timeoutSecondsElement.ValueKind == JsonValueKind.Number
                    && timeoutSecondsElement.TryGetInt32(out var timeoutSecondsInt))
                {
                    timeoutSeconds = timeoutSecondsInt;
                }
                else if (timeoutSecondsElement.ValueKind == JsonValueKind.String
                         && int.TryParse(
                             timeoutSecondsElement.GetString(),
                             NumberStyles.Integer,
                             CultureInfo.InvariantCulture,
                             out var timeoutSecondsParsed
                         ))
                {
                    timeoutSeconds = timeoutSecondsParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("runTimeoutSeconds", out var runTimeoutSecondsElement))
            {
                if (runTimeoutSecondsElement.ValueKind == JsonValueKind.Number
                    && runTimeoutSecondsElement.TryGetInt32(out var runTimeoutSecondsInt))
                {
                    runTimeoutSeconds = runTimeoutSecondsInt;
                }
                else if (runTimeoutSecondsElement.ValueKind == JsonValueKind.String
                         && int.TryParse(
                             runTimeoutSecondsElement.GetString(),
                             NumberStyles.Integer,
                             CultureInfo.InvariantCulture,
                             out var runTimeoutSecondsParsed
                         ))
                {
                    runTimeoutSeconds = runTimeoutSecondsParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("limit", out var limitElement))
            {
                if (limitElement.ValueKind == JsonValueKind.Number && limitElement.TryGetInt32(out var limitInt))
                {
                    limit = limitInt;
                }
                else if (limitElement.ValueKind == JsonValueKind.String
                         && int.TryParse(limitElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limitParsed))
                {
                    limit = limitParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("offset", out var offsetElement))
            {
                if (offsetElement.ValueKind == JsonValueKind.Number && offsetElement.TryGetInt32(out var offsetInt))
                {
                    offset = offsetInt;
                }
                else if (offsetElement.ValueKind == JsonValueKind.String
                         && int.TryParse(offsetElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offsetParsed))
                {
                    offset = offsetParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("maxResults", out var maxResultsElement))
            {
                if (maxResultsElement.ValueKind == JsonValueKind.Number && maxResultsElement.TryGetInt32(out var maxResultsInt))
                {
                    maxResults = maxResultsInt;
                }
                else if (maxResultsElement.ValueKind == JsonValueKind.String
                         && int.TryParse(maxResultsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResultsParsed))
                {
                    maxResults = maxResultsParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("activeMinutes", out var activeMinutesElement))
            {
                if (activeMinutesElement.ValueKind == JsonValueKind.Number && activeMinutesElement.TryGetInt32(out var activeMinutesInt))
                {
                    activeMinutes = activeMinutesInt;
                }
                else if (activeMinutesElement.ValueKind == JsonValueKind.String
                         && int.TryParse(activeMinutesElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var activeMinutesParsed))
                {
                    activeMinutes = activeMinutesParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("messageLimit", out var messageLimitElement))
            {
                if (messageLimitElement.ValueKind == JsonValueKind.Number && messageLimitElement.TryGetInt32(out var messageLimitInt))
                {
                    messageLimit = messageLimitInt;
                }
                else if (messageLimitElement.ValueKind == JsonValueKind.String
                         && int.TryParse(messageLimitElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var messageLimitParsed))
                {
                    messageLimit = messageLimitParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("maxChars", out var maxCharsElement))
            {
                if (maxCharsElement.ValueKind == JsonValueKind.Number && maxCharsElement.TryGetInt32(out var maxCharsInt))
                {
                    maxChars = maxCharsInt;
                }
                else if (maxCharsElement.ValueKind == JsonValueKind.String
                         && int.TryParse(maxCharsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxCharsParsed))
                {
                    maxChars = maxCharsParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("maxWidth", out var maxWidthElement))
            {
                if (maxWidthElement.ValueKind == JsonValueKind.Number && maxWidthElement.TryGetInt32(out var maxWidthInt))
                {
                    maxWidth = maxWidthInt;
                }
                else if (maxWidthElement.ValueKind == JsonValueKind.String
                         && int.TryParse(maxWidthElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxWidthParsed))
                {
                    maxWidth = maxWidthParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("count", out var countElement))
            {
                if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt32(out var countInt))
                {
                    count = countInt;
                }
                else if (countElement.ValueKind == JsonValueKind.String
                         && int.TryParse(countElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var countParsed))
                {
                    count = countParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("minScore", out var minScoreElement))
            {
                if (minScoreElement.ValueKind == JsonValueKind.Number && minScoreElement.TryGetDouble(out var minScoreDouble))
                {
                    minScore = minScoreDouble;
                }
                else if (minScoreElement.ValueKind == JsonValueKind.String
                         && double.TryParse(
                             minScoreElement.GetString(),
                             NumberStyles.Float | NumberStyles.AllowThousands,
                             CultureInfo.InvariantCulture,
                             out var minScoreParsed
                         ))
                {
                    minScore = minScoreParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("from", out var fromElement))
            {
                if (fromElement.ValueKind == JsonValueKind.Number && fromElement.TryGetInt32(out var fromInt))
                {
                    fromLine = fromInt;
                }
                else if (fromElement.ValueKind == JsonValueKind.String
                         && int.TryParse(fromElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromParsed))
                {
                    fromLine = fromParsed;
                }
            }

            if (doc.RootElement.TryGetProperty("lines", out var linesElement))
            {
                if (linesElement.ValueKind == JsonValueKind.Number && linesElement.TryGetInt32(out var linesInt))
                {
                    lines = linesInt;
                }
                else if (linesElement.ValueKind == JsonValueKind.String
                         && int.TryParse(linesElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var linesParsed))
                {
                    lines = linesParsed;
                }
            }

            return new ClientMessage
            {
                RawJson = json,
                Type = type,
                Otp = otp,
                AuthToken = authToken,
                Text = text,
                Message = message,
                Query = query,
                Search = search,
                Freshness = freshness,
                MemoryPath = memoryPath,
                WebFetchUrl = webFetchUrl,
                ExtractMode = extractMode,
                Model = model,
                Provider = provider,
                GroqModel = groqModel,
                GeminiModel = geminiModel,
                CopilotModel = copilotModel,
                CerebrasModel = cerebrasModel,
                NvidiaModel = nvidiaModel,
                CodexModel = codexModel,
                SummaryProvider = summaryProvider,
                Action = action,
                JobId = jobId,
                CronId = cronId,
                RunMode = runMode,
                Target = target,
                TargetId = targetId,
                Node = node,
                RequestId = requestId,
                Title = title,
                Body = body,
                Priority = priority,
                Delivery = delivery,
                InvokeCommand = invokeCommand,
                InvokeParamsJson = invokeParamsJson,
                SpawnTask = spawnTask,
                Label = label,
                Runtime = runtime,
                SessionKey = sessionKey,
                Scope = scope,
                Mode = mode,
                Profile = profile,
                OutputFormat = outputFormat,
                ConversationId = conversationId,
                StandardInput = standardInput,
                ConversationTitle = conversationTitle,
                Project = project,
                ProjectKey = projectKey,
                Category = category,
                Kind = kind,
                PlanId = planId,
                GraphId = graphId,
                LogicGraphJson = logicGraphJson,
                LogicRunId = logicRunId,
                LogicRunInput = logicRunInput,
                TaskId = taskId,
                PreviewId = previewId,
                Language = language,
                Symbol = symbol,
                Pattern = pattern,
                Replacement = replacement,
                NoteName = noteName,
                NewName = newName,
                FilePath = filePath,
                SkillName = skillName,
                SkillScope = skillScope,
                SkillDescription = skillDescription,
                SkillBody = skillBody,
                ThinkPlus = thinkPlus,
                RoutineId = routineId,
                ExecutionMode = executionMode,
                AgentProvider = agentProvider,
                AgentModel = agentModel,
                AgentStartUrl = agentStartUrl,
                AgentToolProfile = agentToolProfile,
                ScheduleSourceMode = scheduleSourceMode,
                NotifyPolicy = notifyPolicy,
                NotifyTelegram = notifyTelegram,
                ScheduleKind = scheduleKind,
                ScheduleTime = scheduleTime,
                TimezoneId = timezoneId,
                TelegramBotToken = telegramBotToken,
                TelegramChatId = telegramChatId,
                GroqApiKey = groqApiKey,
                GeminiApiKey = geminiApiKey,
                CerebrasApiKey = cerebrasApiKey,
                NvidiaApiKey = nvidiaApiKey,
                CodexApiKey = codexApiKey,
                RoutingPolicyJson = routingPolicyJson,
                RefactorEditsJson = refactorEditsJson,
                ContentBase64 = contentBase64,
                FileName = fileName,
                AuthTtlHours = authTtlHours,
                TimeoutSeconds = timeoutSeconds,
                RunTimeoutSeconds = runTimeoutSeconds,
                Limit = limit,
                Offset = offset,
                Count = count,
                ActiveMinutes = activeMinutes,
                MessageLimit = messageLimit,
                MaxResults = maxResults,
                MaxChars = maxChars,
                MaxWidth = maxWidth,
                MinScore = minScore,
                FromLine = fromLine,
                Lines = lines,
                DayOfMonth = dayOfMonth,
                MaxRetries = maxRetries,
                RetryDelaySeconds = retryDelaySeconds,
                AgentTimeoutSeconds = agentTimeoutSeconds,
                Timestamp = timestamp,
                Enabled = enabled,
                AgentUsePlaywright = agentUsePlaywright,
                RunImmediately = runImmediately,
                Overwrite = overwrite,
                CompactConversation = compactConversation,
                IncludeDisabled = includeDisabled,
                IncludeTools = includeTools,
                Thread = thread,
                Tags = tags.Count == 0 ? Array.Empty<string>() : tags.ToArray(),
                Constraints = constraints.Count == 0 ? Array.Empty<string>() : constraints.ToArray(),
                Kinds = kinds.Count == 0 ? Array.Empty<string>() : kinds.ToArray(),
                Weekdays = weekdays.Count == 0 ? Array.Empty<int>() : weekdays.ToArray(),
                MemoryNotes = memoryNotes.Count == 0 ? Array.Empty<string>() : memoryNotes.ToArray(),
                Attachments = attachments.Count == 0 ? Array.Empty<InputAttachment>() : attachments.ToArray(),
                WebUrls = webUrls.Count == 0 ? Array.Empty<string>() : webUrls.ToArray(),
                WebSearchEnabled = webSearchEnabled,
                Persist = persist
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RateWindow(DateTimeOffset WindowStart, int Count);
}
