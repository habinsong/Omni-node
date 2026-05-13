namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private const int RoutineCreateStageTotal = 5;

    private static void ReportRoutineCreateProgress(
        Action<RoutineProgressUpdate>? progressCallback,
        string message,
        int percent,
        string stageKey,
        string stageTitle,
        string stageDetail,
        int stageIndex,
        bool done = false,
        bool? ok = null
    )
    {
        progressCallback?.Invoke(new RoutineProgressUpdate(
            "create",
            message,
            Math.Max(0, Math.Min(100, percent)),
            done,
            ok,
            stageKey,
            stageTitle,
            stageDetail,
            stageIndex,
            RoutineCreateStageTotal
        ));
    }

    public IReadOnlyList<RoutineSummary> ListRoutines()
    {
        lock (_routineLock)
        {
            return _routinesById.Values
                .Where(routine => !IsLogicGraphRoutine(routine))
                .OrderBy(x => x.NextRunUtc)
                .Select(ToRoutineSummary)
                .ToArray();
        }
    }

    public RoutineSchedulerStatus GetRoutineSchedulerStatus()
    {
        lock (_routineLock)
        {
            var routines = _routinesById.Values
                .Where(routine => !IsLogicGraphRoutine(routine))
                .ToArray();
            var now = DateTimeOffset.UtcNow;
            long? nextRunAtMs = null;
            foreach (var routine in routines.Where(static routine => routine.Enabled))
            {
                var candidate = routine.NextRunUtc.ToUnixTimeMilliseconds();
                if (!nextRunAtMs.HasValue || candidate < nextRunAtMs.Value)
                {
                    nextRunAtMs = candidate;
                }
            }

            var schedulerEnabled = _routineSchedulerTask is null
                || (!_routineSchedulerTask.IsFaulted && !_routineSchedulerTask.IsCanceled);
            return new RoutineSchedulerStatus(
                schedulerEnabled,
                routines.Length,
                routines.Count(static routine => routine.Enabled),
                routines.Count(static routine => routine.Running),
                routines.Count(routine => routine.Enabled && !routine.Running && routine.NextRunUtc <= now),
                nextRunAtMs,
                _routineSchedulerLastError
            );
        }
    }

    public RoutineExecutionPreviewResult PreviewRoutine(
        string request,
        string? executionMode,
        string? scheduleSourceMode,
        string? scheduleKind,
        string? scheduleTime,
        IReadOnlyList<int>? weekdays,
        int? dayOfMonth,
        string? timezoneId
    )
    {
        var input = (request ?? string.Empty).Trim();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            warnings.Add("루틴 요청이 비어 있습니다.");
        }

        var resolvedScheduleSourceMode = NormalizeRoutineScheduleSourceMode(scheduleSourceMode, input);
        if (!TryResolveRoutineScheduleConfig(
                input,
                resolvedScheduleSourceMode,
                scheduleKind,
                scheduleTime,
                weekdays,
                dayOfMonth,
                timezoneId,
                out var scheduleConfig,
                out var scheduleError
            ))
        {
            warnings.Add(scheduleError);
            scheduleConfig = BuildDailyRoutineScheduleConfig(8, 0, timezoneId ?? TimeZoneInfo.Local.Id);
        }

        var taskRequest = ResolveRoutineExecutionRequestText(input, BuildRoutineTitle(input), resolvedScheduleSourceMode);
        var normalizedExecutionMode = NormalizeRoutineExecutionMode(executionMode);
        var resolvedExecutionMode = ResolveRoutineExecutionMode(taskRequest, normalizedExecutionMode);
        var route = ResolveRoutineExecutionRoute(taskRequest, normalizedExecutionMode);
        if (resolvedExecutionMode == "browser_agent" && route.Urls.Count == 0)
        {
            warnings.Add("브라우저 에이전트는 시작 URL 또는 요청 원문 URL이 필요합니다.");
        }

        if (route.Mode == "script" && !_llmRouter.HasGroqApiKey())
        {
            warnings.Add("Groq API 키가 없어 스크립트 루틴 코드를 생성할 수 없습니다.");
        }

        return new RoutineExecutionPreviewResult(
            taskRequest,
            resolvedScheduleSourceMode,
            scheduleConfig.Display,
            scheduleConfig.Kind,
            scheduleConfig.TimezoneId,
            resolvedExecutionMode,
            route.Mode,
            warnings
        );
    }

    public async Task<RoutineActionResult> CreateRoutineAsync(
        string request,
        string source,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    )
    {
        var input = (request ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return new RoutineActionResult(false, "루틴 요청이 비어 있습니다.", null);
        }

        var scheduleConfig = ResolveRoutineScheduleConfigFromRequest(input, TimeZoneInfo.Local.Id);
        return await CreateRoutineCoreAsync(
            input,
            BuildRoutineTitle(input),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            NormalizeRoutineScheduleSourceMode("auto", input),
            NormalizeRoutineRetryCount(null),
            NormalizeRoutineRetryDelaySeconds(null),
            NormalizeRoutineNotifyPolicy(null),
            NormalizeRoutineNotifyTelegram(null),
            scheduleConfig,
            true,
            source,
            cancellationToken,
            progressCallback
        );
    }

    public async Task<RoutineActionResult> CreateRoutineAsync(
        string request,
        string? title,
        string? executionMode,
        string? agentProvider,
        string? agentModel,
        string? agentStartUrl,
        int? agentTimeoutSeconds,
        string? agentToolProfile,
        bool? agentUsePlaywright,
        string? scheduleSourceMode,
        int? maxRetries,
        int? retryDelaySeconds,
        string? notifyPolicy,
        bool? notifyTelegram,
        string? scheduleKind,
        string? scheduleTime,
        IReadOnlyList<int>? weekdays,
        int? dayOfMonth,
        string? timezoneId,
        bool runImmediately,
        string source,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    )
    {
        var input = (request ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return new RoutineActionResult(false, "루틴 요청이 비어 있습니다.", null);
        }

        var resolvedScheduleSourceMode = NormalizeRoutineScheduleSourceMode(scheduleSourceMode, input);
        if (!TryResolveRoutineScheduleConfig(
                input,
                resolvedScheduleSourceMode,
                scheduleKind,
                scheduleTime,
                weekdays,
                dayOfMonth,
                timezoneId,
                out var scheduleConfig,
                out var scheduleError
            ))
        {
            return new RoutineActionResult(false, scheduleError, null);
        }

        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? BuildRoutineTitle(input)
            : title.Trim();
        return await CreateRoutineCoreAsync(
            input,
            resolvedTitle,
            executionMode,
            agentProvider,
            agentModel,
            agentStartUrl,
            agentTimeoutSeconds,
            agentToolProfile,
            agentUsePlaywright,
            resolvedScheduleSourceMode,
            NormalizeRoutineRetryCount(maxRetries),
            NormalizeRoutineRetryDelaySeconds(retryDelaySeconds),
            NormalizeRoutineNotifyPolicy(notifyPolicy),
            NormalizeRoutineNotifyTelegram(notifyTelegram),
            scheduleConfig,
            runImmediately,
            source,
            cancellationToken,
            progressCallback
        );
    }

    public async Task<RoutineActionResult> UpdateRoutineAsync(
        string routineId,
        string request,
        string? title,
        string? executionMode,
        string? agentProvider,
        string? agentModel,
        string? agentStartUrl,
        int? agentTimeoutSeconds,
        string? agentToolProfile,
        bool? agentUsePlaywright,
        string? scheduleSourceMode,
        int? maxRetries,
        int? retryDelaySeconds,
        string? notifyPolicy,
        bool? notifyTelegram,
        string? scheduleKind,
        string? scheduleTime,
        IReadOnlyList<int>? weekdays,
        int? dayOfMonth,
        string? timezoneId,
        CancellationToken cancellationToken,
        Action<RoutineProgressUpdate>? progressCallback = null
    )
    {
        var key = (routineId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new RoutineActionResult(false, "routineId가 필요합니다.", null);
        }

        var input = (request ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return new RoutineActionResult(false, "루틴 요청이 비어 있습니다.", null);
        }

        var resolvedScheduleSourceMode = NormalizeRoutineScheduleSourceMode(scheduleSourceMode, input);
        if (!TryResolveRoutineScheduleConfig(
                input,
                resolvedScheduleSourceMode,
                scheduleKind,
                scheduleTime,
                weekdays,
                dayOfMonth,
                timezoneId,
                out var scheduleConfig,
                out var scheduleError
            ))
        {
            return new RoutineActionResult(false, scheduleError, null);
        }

        RoutineDefinition? existing;
        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(key, out var found))
            {
                return new RoutineActionResult(false, "루틴을 찾을 수 없습니다.", null);
            }

            if (found.Running)
            {
                return new RoutineActionResult(false, "실행 중인 루틴은 수정할 수 없습니다.", ToRoutineSummary(found));
            }

            existing = found;
        }

        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? BuildRoutineTitle(input)
            : title.Trim();
        var taskRequest = ResolveRoutineExecutionRequestText(input, resolvedTitle, resolvedScheduleSourceMode);
        var normalizedExecutionMode = NormalizeRoutineExecutionMode(executionMode);
        var resolvedExecutionMode = ResolveRoutineExecutionMode(taskRequest, normalizedExecutionMode);
        var nextExecutionRoute = ResolveRoutineExecutionRoute(taskRequest, normalizedExecutionMode);
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
            ? NormalizeRoutineAgentToolProfile(
                string.IsNullOrWhiteSpace(agentToolProfile) ? existing.AgentToolProfile : agentToolProfile,
                agentUsePlaywright
            )
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
        var requestChanged = !string.Equals(existing.Request, input, StringComparison.Ordinal);
        var titleChanged = !string.Equals(existing.Title, resolvedTitle, StringComparison.Ordinal);
        var executionModeChanged = !string.Equals(
            NormalizeRoutineExecutionMode(existing.ExecutionMode),
            normalizedExecutionMode,
            StringComparison.Ordinal
        );
        var scheduleChanged = !RoutineMatchesSchedule(existing, scheduleConfig);
        var scheduleSourceChanged = !string.Equals(
            NormalizeRoutineScheduleSourceMode(existing.ScheduleSourceMode, existing.Request),
            resolvedScheduleSourceMode,
            StringComparison.Ordinal
        );
        var retryCountChanged = existing.MaxRetries != NormalizeRoutineRetryCount(maxRetries);
        var retryDelayChanged = existing.RetryDelaySeconds != NormalizeRoutineRetryDelaySeconds(retryDelaySeconds);
        var notifyPolicyChanged = !string.Equals(
            NormalizeRoutineNotifyPolicy(existing.NotifyPolicy),
            NormalizeRoutineNotifyPolicy(notifyPolicy),
            StringComparison.Ordinal
        );
        var normalizedNotifyTelegram = NormalizeRoutineNotifyTelegram(notifyTelegram, existing.NotifyTelegram);
        var notifyTelegramChanged = existing.NotifyTelegram != normalizedNotifyTelegram;
        var agentProviderChanged = !string.Equals(
            NormalizeRoutineAgentProvider(existing.AgentProvider, existing.AgentModel),
            normalizedAgentProvider,
            StringComparison.Ordinal
        );
        var agentModelChanged = !string.Equals(
            NormalizeRoutineAgentModel(existing.AgentModel),
            normalizedAgentModel,
            StringComparison.Ordinal
        );
        var agentStartUrlChanged = !string.Equals(
            NormalizeRoutineAgentStartUrlOrEmpty(existing.AgentStartUrl),
            normalizedAgentStartUrl,
            StringComparison.Ordinal
        );
        var agentTimeoutChanged = NormalizeRoutineAgentTimeoutSeconds(existing.AgentTimeoutSeconds)
            != normalizedAgentTimeoutSeconds;
        var agentToolProfileChanged = !string.Equals(
            NormalizeRoutineAgentToolProfile(existing.AgentToolProfile, existing.AgentUsePlaywright),
            normalizedAgentToolProfile,
            StringComparison.Ordinal
        );
        var agentUsePlaywrightChanged = NormalizeRoutineAgentUsePlaywright(existing.AgentUsePlaywright)
            != normalizedAgentUsePlaywright;
        RoutineGenerationResult? generation = null;
        if ((requestChanged || scheduleChanged || scheduleSourceChanged || executionModeChanged)
            && string.Equals(nextExecutionRoute.Mode, "script", StringComparison.Ordinal))
        {
            if (!_llmRouter.HasGroqApiKey())
            {
                return new RoutineActionResult(
                    false,
                    "루틴 스크립트 수정 실패: Groq API 키가 없어 실행 코드를 다시 만들 수 없습니다. 설정에서 Groq 키를 저장하거나 실행 모드를 일반 답변/URL 참조/브라우저 에이전트로 바꾸세요.",
                    null
                );
            }

            generation = await GenerateRoutineImplementationAsync(
                taskRequest,
                new RoutineSchedule(scheduleConfig.Hour, scheduleConfig.Minute, scheduleConfig.Display),
                cancellationToken
            );
            var generationValidation = ValidateRoutineGeneratedCode(generation.Language, generation.Code, taskRequest);
            if (!generationValidation.Ok)
            {
                return new RoutineActionResult(
                    false,
                    $"루틴 스크립트 생성 품질 검증 실패: {string.Join(" / ", generationValidation.Warnings)}",
                    null
                );
            }
        }

        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(key, out var update))
            {
                return new RoutineActionResult(false, "루틴을 찾을 수 없습니다.", null);
            }

            if (update.Running)
            {
                return new RoutineActionResult(false, "실행 중인 루틴은 수정할 수 없습니다.", ToRoutineSummary(update));
            }

            update.Title = resolvedTitle;
            update.Request = input;
            update.ExecutionMode = normalizedExecutionMode;
            update.AgentProvider = normalizedAgentProvider;
            update.AgentModel = normalizedAgentModel;
            update.AgentStartUrl = normalizedAgentStartUrl;
            update.AgentTimeoutSeconds = normalizedAgentTimeoutSeconds;
            update.AgentToolProfile = normalizedAgentToolProfile;
            update.AgentUsePlaywright = normalizedAgentUsePlaywright;
            update.ScheduleText = scheduleConfig.Display;
            update.ScheduleSourceMode = resolvedScheduleSourceMode;
            update.MaxRetries = NormalizeRoutineRetryCount(maxRetries);
            update.RetryDelaySeconds = NormalizeRoutineRetryDelaySeconds(retryDelaySeconds);
            update.NotifyPolicy = NormalizeRoutineNotifyPolicy(notifyPolicy);
            update.NotifyTelegram = normalizedNotifyTelegram;
            update.TimezoneId = scheduleConfig.TimezoneId;
            update.Hour = scheduleConfig.Hour;
            update.Minute = scheduleConfig.Minute;
            update.CronScheduleKind = "cron";
            update.CronScheduleExpr = scheduleConfig.CronExpr;
            update.CronScheduleAtMs = null;
            update.CronScheduleEveryMs = null;
            update.CronScheduleAnchorMs = null;
            update.NextRunUtc = ComputeNextCronBridgeRunUtc(update, DateTimeOffset.UtcNow);
            if (resolvedExecutionMode == "browser_agent")
            {
                update.ScriptPath = string.Empty;
                update.Language = "agent";
                update.Code = string.Empty;
                update.Planner = "acp";
                update.PlannerModel = normalizedAgentProvider ?? "acp";
                update.CoderModel = normalizedAgentModel ?? "browser-agent";
                update.QualityStatus = "not_applicable";
                update.QualityWarnings = new List<string>();
                update.CronSessionTarget = "isolated";
                update.CronWakeMode = "next-heartbeat";
                update.CronPayloadKind = "agentTurn";
                update.CronPayloadModel = normalizedAgentModel;
                update.CronPayloadThinking = null;
                update.CronPayloadTimeoutSeconds = normalizedAgentTimeoutSeconds;
                update.CronPayloadLightContext = false;
                update.LastOutput = BuildRoutineExecutionPreview(
                    resolvedExecutionMode,
                    normalizedAgentProvider,
                    normalizedAgentModel,
                    normalizedAgentStartUrl,
                    normalizedAgentToolProfile
                );
            }

            if (requestChanged
                || scheduleChanged
                || scheduleSourceChanged
                || notifyPolicyChanged
                || executionModeChanged
                || agentProviderChanged
                || agentModelChanged
                || agentStartUrlChanged
                || agentTimeoutChanged
                || agentToolProfileChanged
                || agentUsePlaywrightChanged)
            {
                update.LastNotifiedFingerprint = null;
            }

            if (generation != null)
            {
                var runDir = string.IsNullOrWhiteSpace(update.ScriptPath)
                    ? Path.Combine(_config.WorkspaceRootDir, "routines", update.Id)
                    : (Path.GetDirectoryName(update.ScriptPath) ?? Path.Combine(_config.WorkspaceRootDir, "routines", update.Id));
                Directory.CreateDirectory(runDir);
                update.ScriptPath = WriteRoutineScript(runDir, generation.Language, generation.Code);
                update.Language = generation.Language;
                update.Code = generation.Code;
                update.Planner = generation.PlannerProvider;
                update.PlannerModel = generation.PlannerModel;
                update.CoderModel = generation.CoderModel;
                update.QualityStatus = generation.QualityStatus;
                update.QualityWarnings = generation.QualityWarnings?.ToList() ?? new List<string>();
                update.LastOutput = generation.Plan;
            }
            else if (!string.Equals(nextExecutionRoute.Mode, "script", StringComparison.Ordinal)
                && !string.Equals(nextExecutionRoute.Mode, "browser_agent", StringComparison.Ordinal))
            {
                update.ScriptPath = string.Empty;
                update.Language = "llm";
                update.Code = string.Empty;
                update.Planner = "gemini";
                update.PlannerModel = ResolveRoutineLlmModel(nextExecutionRoute.Mode);
                update.CoderModel = nextExecutionRoute.Mode;
                update.QualityStatus = "not_applicable";
                update.QualityWarnings = new List<string>();
                update.CronSessionTarget = "main";
                update.CronWakeMode = "next-heartbeat";
                update.CronPayloadKind = "systemEvent";
                update.CronPayloadModel = null;
                update.CronPayloadThinking = null;
                update.CronPayloadTimeoutSeconds = null;
                update.CronPayloadLightContext = null;
                update.LastOutput = BuildRoutineExecutionPreview(nextExecutionRoute.Mode, null, null, null, null);
            }
            else if (string.Equals(nextExecutionRoute.Mode, "script", StringComparison.Ordinal))
            {
                update.CronSessionTarget = "main";
                update.CronWakeMode = "next-heartbeat";
                update.CronPayloadKind = "systemEvent";
                update.CronPayloadModel = null;
                update.CronPayloadThinking = null;
                update.CronPayloadTimeoutSeconds = null;
                update.CronPayloadLightContext = null;
            }

            if (titleChanged || requestChanged || scheduleChanged || scheduleSourceChanged || retryCountChanged || retryDelayChanged || notifyPolicyChanged || notifyTelegramChanged)
            {
                update.LastStatus = update.LastRunUtc.HasValue
                    ? update.LastStatus
                    : "updated";
            }

            SaveRoutineStateLocked();
            return new RoutineActionResult(true, "루틴 수정 완료", ToRoutineSummary(update));
        }
    }

    public async Task<RoutineActionResult> RunRoutineNowAsync(string routineId, string source, CancellationToken cancellationToken)
    {
        var key = (routineId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new RoutineActionResult(false, "routineId가 필요합니다.", null);
        }

        RoutineDefinition? routine;
        var runningMarked = false;
        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(key, out var found))
            {
                return new RoutineActionResult(false, "루틴을 찾을 수 없습니다.", null);
            }

            if (found.Running)
            {
                return new RoutineActionResult(false, "이미 실행 중인 루틴입니다.", ToRoutineSummary(found));
            }

            found.Running = true;
            runningMarked = true;
            routine = found;
            SaveRoutineStateLocked();
        }

        try
        {
            return await RunRoutineNowCoreAsync(key, routine, source, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (runningMarked)
            {
                EnsureRoutineNotRunning(key);
            }
        }
    }

    private async Task<RoutineActionResult> RunRoutineNowCoreAsync(
        string key,
        RoutineDefinition routine,
        string source,
        CancellationToken cancellationToken
    )
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var taskRequest = ResolveRoutineExecutionRequestText(routine.Request, routine.Title, routine.ScheduleSourceMode);
        var executionRoute = ResolveRoutineExecutionRoute(taskRequest, routine.ExecutionMode);
        if (string.Equals(executionRoute.Mode, LogicGraphExecutionMode, StringComparison.Ordinal))
        {
            var runId = $"logicrun-{startedAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
            var snapshot = await ExecuteLogicGraphRunCoreAsync(
                routine.Id,
                runId,
                source,
                string.Empty,
                null,
                cancellationToken
            ).ConfigureAwait(false);
            RoutineDefinition? updatedRoutine;
            lock (_routineLock)
            {
                _routinesById.TryGetValue(key, out updatedRoutine);
            }

            var finalMessage = string.IsNullOrWhiteSpace(snapshot.ResultText)
                ? (string.IsNullOrWhiteSpace(snapshot.Error) ? "흐름 실행이 끝났습니다." : snapshot.Error)
                : snapshot.ResultText;
            return new RoutineActionResult(
                string.Equals(snapshot.Status, "completed", StringComparison.Ordinal),
                finalMessage,
                updatedRoutine == null ? null : ToRoutineSummary(updatedRoutine)
            );
        }

        var maxAttempts = NormalizeRoutineRetryCount(routine.MaxRetries) + 1;
        var retryDelaySeconds = NormalizeRoutineRetryDelaySeconds(routine.RetryDelaySeconds);
        var attemptCount = 0;
        string output = string.Empty;
        string lastStatus = "error";
        string runStatus = "error";
        string? runError = null;
        RoutineAgentExecutionMetadata? agentMetadata = null;
        var resultOk = true;
        var telegramDispatch = (Status: "not_applicable", Error: (string?)null, Fingerprint: (string?)null);

        try
        {
            for (var attemptIndex = 0; attemptIndex < maxAttempts; attemptIndex += 1)
            {
                attemptCount = attemptIndex + 1;

                if (string.Equals(executionRoute.Mode, "browser_agent", StringComparison.Ordinal))
                {
                    var runAssetDirectory = EnsureRoutineBrowserAgentAssetDirectory(routine.Id, startedAtUtc);
                    var browserExecution = await ExecuteRoutineBrowserAgentAsync(
                        routine,
                        taskRequest,
                        executionRoute.Urls,
                        runAssetDirectory,
                        cancellationToken
                    );
                    output = browserExecution.Output;
                    lastStatus = browserExecution.Status;
                    runStatus = browserExecution.Status;
                    runError = browserExecution.Error;
                    agentMetadata = browserExecution.AgentMetadata;
                }
                else if (!string.Equals(executionRoute.Mode, "script", StringComparison.Ordinal))
                {
                    var llmExecution = await ExecuteRoutineLlmRouteAsync(
                        routine,
                        executionRoute.Mode,
                        executionRoute.Urls,
                        source,
                        cancellationToken
                    );
                    output = llmExecution.Output;
                    lastStatus = llmExecution.Status;
                    runStatus = llmExecution.Status;
                    runError = llmExecution.Error;
                }
                else
                {
                    var normalizedLanguage = NormalizeRoutineScriptLanguage(routine.Language);
                    var normalizedCode = string.IsNullOrWhiteSpace(routine.Code)
                        ? string.Empty
                        : EnsureRoutineShebang(routine.Code, normalizedLanguage);
                    if (!ShouldRunCronAgentTurnBridge(routine) && string.IsNullOrWhiteSpace(normalizedCode))
                    {
                        runStatus = "error";
                        lastStatus = "error";
                        runError = "루틴 실행 코드가 비어 있어 실행할 수 없습니다.";
                        output = runError;
                        break;
                    }

                    if (!string.Equals(normalizedLanguage, routine.Language, StringComparison.Ordinal)
                        || !string.Equals(normalizedCode, routine.Code, StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(routine.ScriptPath))
                    {
                        lock (_routineLock)
                        {
                            if (_routinesById.TryGetValue(key, out var update))
                            {
                                var runDir = string.IsNullOrWhiteSpace(update.ScriptPath)
                                    ? Path.Combine(_config.WorkspaceRootDir, "routines", update.Id)
                                    : (Path.GetDirectoryName(update.ScriptPath) ?? Path.Combine(_config.WorkspaceRootDir, "routines", update.Id));
                                Directory.CreateDirectory(runDir);
                                update.ScriptPath = WriteRoutineScript(runDir, normalizedLanguage, normalizedCode);
                                update.Language = normalizedLanguage;
                                update.Code = normalizedCode;
                                SaveRoutineStateLocked();
                                routine = update;
                            }
                        }
                    }

                    if (!ShouldRunCronAgentTurnBridge(routine) && RoutineCodeNeedsRepair(routine.Language, routine.Code))
                    {
                        if (!_llmRouter.HasGroqApiKey())
                        {
                            runStatus = "error";
                            lastStatus = "error";
                            runError = "루틴 실행 코드 보정이 필요하지만 Groq API 키가 없어 재생성할 수 없습니다.";
                            output = runError;
                            break;
                        }

                        var regenerated = await GenerateRoutineImplementationAsync(
                            taskRequest,
                            new RoutineSchedule(routine.Hour, routine.Minute, routine.ScheduleText),
                            cancellationToken
                        );
                        var regeneratedValidation = ValidateRoutineGeneratedCode(regenerated.Language, regenerated.Code, taskRequest);
                        if (!regeneratedValidation.Ok)
                        {
                            runStatus = "error";
                            lastStatus = "error";
                            runError = $"루틴 실행 코드 재생성 품질 검증 실패: {string.Join(" / ", regeneratedValidation.Warnings)}";
                            output = runError;
                            break;
                        }

                        lock (_routineLock)
                        {
                            if (_routinesById.TryGetValue(key, out var update))
                            {
                                var runDir = string.IsNullOrWhiteSpace(update.ScriptPath)
                                    ? Path.Combine(_config.WorkspaceRootDir, "routines", update.Id)
                                    : (Path.GetDirectoryName(update.ScriptPath) ?? Path.Combine(_config.WorkspaceRootDir, "routines", update.Id));
                                Directory.CreateDirectory(runDir);
                                update.ScriptPath = WriteRoutineScript(runDir, regenerated.Language, regenerated.Code);
                                update.Language = regenerated.Language;
                                update.Code = regenerated.Code;
                                update.Planner = regenerated.PlannerProvider;
                                update.PlannerModel = regenerated.PlannerModel;
                                update.CoderModel = regenerated.CoderModel;
                                update.QualityStatus = regenerated.QualityStatus;
                                update.QualityWarnings = regenerated.QualityWarnings?.ToList() ?? new List<string>();
                                update.LastOutput = regenerated.Plan;
                                SaveRoutineStateLocked();
                                routine = update;
                            }
                        }
                    }

                    CodeExecutionResult exec;
                    if (ShouldRunCronAgentTurnBridge(routine))
                    {
                        exec = ExecuteCronAgentTurnBridge(routine, cancellationToken);
                    }
                    else
                    {
                        exec = await _codeRunner.ExecuteAsync(routine.Language, routine.Code, cancellationToken);
                    }

                    output = BuildRoutineExecutionText(routine, exec);
                    lastStatus = exec.Status;
                    runStatus = ResolveCronRunEntryStatus(exec);
                    runError = BuildCronRunEntryError(exec, output, runStatus);
                }

                if (!IsRoutineRetryableStatus(runStatus) || attemptCount >= maxAttempts)
                {
                    break;
                }

                if (retryDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                telegramDispatch = await DispatchRoutineResultToTelegramAsync(
                    routine,
                    output,
                    source,
                    runStatus,
                    cancellationToken,
                    agentMetadata
                );
                if (!string.IsNullOrWhiteSpace(telegramDispatch.Error))
                {
                    output = string.IsNullOrWhiteSpace(output)
                        ? telegramDispatch.Error
                        : $"{output.Trim()}\n\n[telegram]\n{telegramDispatch.Error}";
                    lastStatus = "error";
                    runStatus = "error";
                    runError = telegramDispatch.Error;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            const string canceledMessage = "루틴 실행이 취소되었습니다.";
            output = string.IsNullOrWhiteSpace(output)
                ? canceledMessage
                : $"{output.Trim()}\n\n[cancel]\n{canceledMessage}";
            lastStatus = "error";
            runStatus = "error";
            runError = canceledMessage;
            resultOk = false;
        }
        catch (Exception ex)
        {
            var exceptionMessage = $"루틴 실행 중 예외가 발생했습니다: {ex.Message}";
            output = string.IsNullOrWhiteSpace(output)
                ? exceptionMessage
                : $"{output.Trim()}\n\n[exception]\n{exceptionMessage}";
            lastStatus = "error";
            runStatus = "error";
            runError = exceptionMessage;
            resultOk = false;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            output = runError ?? "루틴 실행 결과가 비어 있습니다.";
        }

        var completedAtUtc = DateTimeOffset.UtcNow;
        var durationMs = Math.Max(0L, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds);
        var runSummary = BuildCronRunEntrySummary(output);
        string? artifactPath = null;
        try
        {
            artifactPath = _runArtifactStore.WriteRoutineRun(new RoutineRunArtifactWriteRequest(
                routine.Id,
                routine.Title,
                source,
                attemptCount,
                runStatus,
                output,
                runError,
                telegramDispatch.Status,
                agentMetadata,
                startedAtUtc,
                completedAtUtc,
                string.Equals(executionRoute.Mode, "browser_agent", StringComparison.Ordinal)
                    ? EnsureRoutineBrowserAgentAssetDirectory(routine.Id, startedAtUtc)
                    : null
            ));
        }
        catch (Exception ex)
        {
            var artifactError = $"실행 결과 저장 실패: {ex.Message}";
            output = string.IsNullOrWhiteSpace(output)
                ? artifactError
                : $"{output.Trim()}\n\n[artifact]\n{artifactError}";
            runError = string.IsNullOrWhiteSpace(runError)
                ? artifactError
                : $"{runError}\n{artifactError}";
            lastStatus = "error";
            runStatus = "error";
            runSummary = BuildCronRunEntrySummary(output);
            resultOk = false;
        }

        lock (_routineLock)
        {
            if (_routinesById.TryGetValue(key, out var update))
            {
                update.Running = false;
                update.LastRunUtc = completedAtUtc;
                update.LastStatus = lastStatus;
                update.LastOutput = output;
                update.LastDurationMs = durationMs;
                if (string.Equals(NormalizeCronScheduleKind(update.CronScheduleKind), "at", StringComparison.Ordinal))
                {
                    update.Enabled = false;
                    update.NextRunUtc = completedAtUtc;
                }
                else
                {
                    update.NextRunUtc = ComputeNextCronBridgeRunUtc(update, completedAtUtc);
                }

                if (string.Equals(telegramDispatch.Status, "sent", StringComparison.Ordinal)
                    && IsRoutineScheduledSource(source)
                    && !string.IsNullOrWhiteSpace(telegramDispatch.Fingerprint))
                {
                    update.LastNotifiedFingerprint = telegramDispatch.Fingerprint;
                }

                AppendRoutineRunLogEntry(update, new RoutineRunLogEntry
                {
                    Ts = completedAtUtc.ToUnixTimeMilliseconds(),
                    JobId = update.Id,
                    Action = "finished",
                    Status = runStatus,
                    Source = source,
                    AttemptCount = Math.Max(1, attemptCount),
                    Error = runError,
                    Summary = runSummary,
                    TelegramStatus = telegramDispatch.Status,
                    ArtifactPath = artifactPath,
                    AgentSessionId = agentMetadata?.SessionKey,
                    AgentRunId = agentMetadata?.RunId,
                    AgentProvider = agentMetadata?.Provider,
                    AgentModel = agentMetadata?.Model,
                    ToolProfile = agentMetadata?.ToolProfile,
                    StartUrl = agentMetadata?.StartUrl,
                    FinalUrl = agentMetadata?.FinalUrl,
                    PageTitle = agentMetadata?.PageTitle,
                    ScreenshotPath = agentMetadata?.ScreenshotPath,
                    DownloadPaths = agentMetadata?.DownloadPaths?.ToList() ?? new List<string>(),
                    RunAtMs = startedAtUtc.ToUnixTimeMilliseconds(),
                    DurationMs = durationMs,
                    NextRunAtMs = update.Enabled ? update.NextRunUtc.ToUnixTimeMilliseconds() : null
                });
                SaveRoutineStateLocked();
                routine = update;
            }
        }

        return new RoutineActionResult(
            resultOk && !IsRoutineRetryableStatus(runStatus),
            output,
            routine == null ? null : ToRoutineSummary(routine)
        );
    }

    public RoutineRunDetailResult GetRoutineRunDetail(string routineId, long ts)
    {
        var key = (routineId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new RoutineRunDetailResult(false, string.Empty, ts, "루틴 실행 상세", "-", "-", 1, null, null, null, null, null, null, null, null, null, null, null, Array.Empty<string>(), "routineId가 필요합니다.", "routineId가 필요합니다.");
        }

        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(key, out var routine))
            {
                return new RoutineRunDetailResult(false, key, ts, "루틴 실행 상세", "-", "-", 1, null, null, null, null, null, null, null, null, null, null, null, Array.Empty<string>(), "루틴을 찾을 수 없습니다.", "루틴을 찾을 수 없습니다.");
            }

            var entry = (routine.CronRunLog ?? new List<RoutineRunLogEntry>())
                .OrderByDescending(static item => item.Ts)
                .FirstOrDefault(item => item.Ts == ts);
            if (entry == null)
            {
                return new RoutineRunDetailResult(false, key, ts, routine.Title, "-", "-", 1, null, null, null, null, null, null, null, null, null, null, null, Array.Empty<string>(), "실행 이력을 찾을 수 없습니다.", "실행 이력을 찾을 수 없습니다.");
            }

            var content = _runArtifactStore.ReadText(entry.ArtifactPath);
            if (string.IsNullOrWhiteSpace(content))
            {
                content = BuildFallbackRoutineRunDetailContent(routine, entry);
            }

            return new RoutineRunDetailResult(
                true,
                key,
                ts,
                routine.Title,
                string.IsNullOrWhiteSpace(entry.Status) ? "-" : entry.Status!,
                string.IsNullOrWhiteSpace(entry.Source) ? (entry.Action ?? "-") : entry.Source!,
                Math.Max(1, entry.AttemptCount),
                string.IsNullOrWhiteSpace(entry.TelegramStatus) ? null : entry.TelegramStatus,
                string.IsNullOrWhiteSpace(entry.ArtifactPath) ? null : entry.ArtifactPath,
                string.IsNullOrWhiteSpace(entry.AgentSessionId) ? null : entry.AgentSessionId,
                string.IsNullOrWhiteSpace(entry.AgentRunId) ? null : entry.AgentRunId,
                string.IsNullOrWhiteSpace(entry.AgentProvider) ? null : entry.AgentProvider,
                string.IsNullOrWhiteSpace(entry.AgentModel) ? null : entry.AgentModel,
                string.IsNullOrWhiteSpace(entry.ToolProfile) ? null : entry.ToolProfile,
                string.IsNullOrWhiteSpace(entry.StartUrl) ? null : entry.StartUrl,
                string.IsNullOrWhiteSpace(entry.FinalUrl) ? null : entry.FinalUrl,
                string.IsNullOrWhiteSpace(entry.PageTitle) ? null : entry.PageTitle,
                string.IsNullOrWhiteSpace(entry.ScreenshotPath) ? null : entry.ScreenshotPath,
                (entry.DownloadPaths ?? new List<string>())
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .ToArray(),
                string.IsNullOrWhiteSpace(entry.Error) ? null : entry.Error,
                content
            );
        }
    }

    private void EnsureRoutineNotRunning(string routineId)
    {
        var key = (routineId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_routineLock)
        {
            if (_routinesById.TryGetValue(key, out var routine) && routine.Running)
            {
                routine.Running = false;
                SaveRoutineStateLocked();
            }
        }
    }

    public async Task<RoutineActionResult> ResendRoutineRunToTelegramAsync(
        string routineId,
        long ts,
        CancellationToken cancellationToken
    )
    {
        var detail = GetRoutineRunDetail(routineId, ts);
        if (!detail.Ok)
        {
            return new RoutineActionResult(false, detail.Error ?? "실행 이력을 찾을 수 없습니다.", null);
        }

        RoutineSummary? summary = null;
        lock (_routineLock)
        {
            if (_routinesById.TryGetValue(routineId, out var routine))
            {
                summary = ToRoutineSummary(routine);
            }
        }

        if (summary == null)
        {
            return new RoutineActionResult(false, "루틴을 찾을 수 없습니다.", null);
        }

        var routineForSend = new RoutineDefinition
        {
            Id = summary.Id,
            Title = summary.Title,
            Request = summary.Request,
            ExecutionMode = summary.ExecutionMode,
            AgentProvider = summary.AgentProvider ?? string.Empty,
            AgentModel = summary.AgentModel ?? string.Empty,
            AgentStartUrl = summary.AgentStartUrl,
            AgentToolProfile = summary.AgentToolProfile,
            ScheduleSourceMode = summary.ScheduleSourceMode,
            NotifyPolicy = "always"
        };
        var agentMetadata = new RoutineAgentExecutionMetadata(
            detail.AgentSessionId,
            detail.AgentRunId,
            detail.AgentProvider,
            detail.AgentModel,
            detail.ToolProfile,
            detail.StartUrl,
            detail.FinalUrl,
            detail.PageTitle,
            detail.ScreenshotPath,
            detail.DownloadPaths
        );
        var dispatch = await DispatchRoutineResultToTelegramAsync(
            routineForSend,
            detail.Content,
            "telegram_resend",
            detail.Status,
            cancellationToken,
            agentMetadata
        );
        if (!string.IsNullOrWhiteSpace(dispatch.Error))
        {
            return new RoutineActionResult(false, dispatch.Error, summary);
        }

        return new RoutineActionResult(true, "선택한 실행 결과를 텔레그램으로 다시 보냈습니다.", summary);
    }

    public RoutineActionResult SetRoutineEnabled(string routineId, bool enabled)
    {
        var key = (routineId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new RoutineActionResult(false, "routineId가 필요합니다.", null);
        }

        lock (_routineLock)
        {
            if (!_routinesById.TryGetValue(key, out var routine))
            {
                return new RoutineActionResult(false, "루틴을 찾을 수 없습니다.", null);
            }

            routine.Enabled = enabled;
            if (enabled)
            {
                routine.NextRunUtc = ComputeNextCronBridgeRunUtc(routine, DateTimeOffset.UtcNow);
            }
            routine.LastStatus = enabled ? "enabled" : "disabled";
            SaveRoutineStateLocked();
            return new RoutineActionResult(true, enabled ? "루틴 활성화" : "루틴 비활성화", ToRoutineSummary(routine));
        }
    }

    public RoutineActionResult DeleteRoutine(string routineId)
    {
        var key = (routineId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new RoutineActionResult(false, "routineId가 필요합니다.", null);
        }

        lock (_routineLock)
        {
            if (!_routinesById.Remove(key))
            {
                return new RoutineActionResult(false, "루틴을 찾을 수 없습니다.", null);
            }

            SaveRoutineStateLocked();
            return new RoutineActionResult(true, "루틴 삭제 완료", null);
        }
    }
}
