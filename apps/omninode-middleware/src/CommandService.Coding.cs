using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public Task<CodingRunResult> RunCodingSingleAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    )
    {
        return RunCodingSingleCoreAsync(request, cancellationToken, progressCallback);
    }

    public Task<CodingRunResult> RunCodingOrchestrationAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    )
    {
        return RunCodingOrchestrationCoreAsync(request, cancellationToken, progressCallback);
    }

    public Task<CodingRunResult> RunCodingMultiAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    )
    {
        return RunCodingMultiCoreAsync(request, cancellationToken, progressCallback);
    }

    // 프롬프트에 두 개 이상의 스킬 이름이 있으면 코딩 흐름에서도 거부 결과 반환.
    private CodingRunResult? TryBuildCodingMultiSkillRejectionResult(
        string mode,
        ConversationThreadView thread,
        string rawInput,
        string provider,
        string model,
        string codingRunRoot,
        string language,
        string source
    )
    {
        var rejection = TryBuildMultiSkillRejectionResponse(rawInput);
        if (string.IsNullOrWhiteSpace(rejection))
        {
            return null;
        }

        _auditLogger.Log(source, "skill_multi_mention", "blocked", $"mode=coding-{mode}");
        var execution = new CodeExecutionResult(
            language,
            codingRunRoot,
            "-",
            "(none)",
            0,
            string.Empty,
            rejection,
            "skipped"
        );
        var assistantMeta = $"coding-{mode}:multi_skill_rejected";
        _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"coding-{mode}");
        _conversationStore.AppendMessage(thread.Id, "assistant", rejection, assistantMeta);
        var view = _conversationStore.Get(thread.Id) ?? thread;
        return PersistLatestCodingResult(new CodingRunResult(
            mode,
            view.Id,
            provider,
            model,
            language,
            string.Empty,
            execution,
            Array.Empty<CodingWorkerResult>(),
            Array.Empty<string>(),
            rejection,
            view,
            null
        ));
    }

    private async Task<CodingRunResult> RunCodingSingleCoreAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    )
    {
        var session = PrepareSessionContext(
            request.Scope,
            request.Mode,
            request.ConversationId,
            request.ConversationTitle,
            request.Project,
            request.Category,
            request.Tags,
            request.LinkedMemoryNotes,
            request.Source
        );
        var thread = session.Thread;
        var rawInput = (request.Input ?? string.Empty).Trim();
        var codingRunRoot = CreateCodingRunWorkspaceRoot("single");
        if (TryHandleBrowserCodingIntent(session, rawInput, "single", codingRunRoot, request.Language) is { } browserIntentResult)
        {
            return browserIntentResult;
        }

        var routingCategory = ResolveCodingTaskCategory(request.Category, rawInput);

        var multiSkillRejectionSingle = TryBuildCodingMultiSkillRejectionResult(
            "single",
            thread,
            rawInput,
            "-",
            "-",
            codingRunRoot,
            request.Language,
            request.Source
        );
        if (multiSkillRejectionSingle != null)
        {
            return multiSkillRejectionSingle;
        }

        var provider = NormalizeProvider(request.Provider, allowAuto: true);
        if (provider == "auto")
        {
            provider = await ResolveCategoryProviderAsync(
                routingCategory,
                provider,
                BuildProviderSelectionMap(
                    request.GroqModel,
                    request.GeminiModel,
                    request.CerebrasModel,
                    request.CopilotModel,
                    request.CodexModel,
                    request.NvidiaModel
                ),
                cancellationToken,
                "coding_single"
            );
            if (provider == "none")
            {
                provider = "groq";
            }
        }

        var model = ResolveModelForCategory(routingCategory, provider, request.Model);
        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, rawInput);
        var preparedInput = await PrepareInputForProviderAsync(
            rawInput,
            provider,
            model,
            request.Attachments,
            request.WebUrls,
            request.WebSearchEnabled,
            true,
            cancellationToken,
            request.Source,
            session.SessionKey,
            session.SessionId,
            requestedSkillName,
            request.SkillScope
        );
        if (!string.IsNullOrWhiteSpace(preparedInput.UnsupportedMessage))
        {
            var blockedExecution = new CodeExecutionResult(
                request.Language,
                codingRunRoot,
                "-",
                "(none)",
                0,
                "첨부 해석을 수행하지 않았습니다.",
                preparedInput.UnsupportedMessage,
                "skipped"
            );
            var blockedText = BuildCodingAssistantText(
                "single",
                provider,
                model,
                request.Language,
                blockedExecution,
                Array.Empty<string>(),
                preparedInput.UnsupportedMessage
            );
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"coding:{provider}:{model}");
            _conversationStore.AppendMessage(thread.Id, "assistant", blockedText, $"coding:{provider}:{model}");
            var blockedView = _conversationStore.Get(thread.Id) ?? thread;
            var blockedCitationBundle = BuildAndLogCitationMappings(
                request.Source,
                "coding-single-unsupported",
                preparedInput.Citations,
                ("summary", preparedInput.UnsupportedMessage)
            );
            return PersistLatestCodingResult(new CodingRunResult(
                "single",
                blockedView.Id,
                provider,
                model,
                request.Language,
                string.Empty,
                blockedExecution,
                Array.Empty<CodingWorkerResult>(),
                Array.Empty<string>(),
                preparedInput.UnsupportedMessage,
                blockedView,
                null,
                preparedInput.GuardFailure,
                preparedInput.Citations,
                blockedCitationBundle.Mappings,
                blockedCitationBundle.Validation,
                preparedInput.RetryAttempt,
                preparedInput.RetryMaxAttempts,
                preparedInput.RetryStopReason
            ));
        }

        var thinkPlusPreText = ApplySelectedSkillToPrompt(
            preparedInput.Text,
            requestedSkillName,
            request.SkillScope
        );
        if (request.ThinkPlusEnabled && request.Mode == "single")
        {
            var effectiveSkillForThinkPlus = ResolveEffectiveSkillNameForThread(requestedSkillName, session.SessionId);
            var thinkPlusContext = await BuildThinkPlusContextAsync(
                rawInput,
                request.Source,
                cancellationToken,
                effectiveSkillForThinkPlus
            ).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(thinkPlusContext))
            {
                thinkPlusPreText = thinkPlusContext + thinkPlusPreText;
            }
        }
        var contextualInput = BuildContextualInput(
            session.SessionId,
            thinkPlusPreText,
            session.LinkedMemoryNotes,
            contextDecisionInput: rawInput
        );
        var rawRequestedPaths = ExtractRequestedCodingPaths(rawInput, request.Language);
        var rawExpectedOutput = ExtractExpectedConsoleOutput(rawInput);
        AutonomousCodingOutcome outcome;
        try
        {
            if (string.Equals(provider, "copilot", StringComparison.OrdinalIgnoreCase)
                && ShouldTryDeterministicSingleFileOutputRepair(rawInput, request.Language, rawRequestedPaths, rawExpectedOutput))
            {
                progressCallback?.Invoke(BuildCodingProgressUpdate(
                    "single",
                    provider,
                    model,
                    "planning",
                    "단순 단일 파일 출력 요청이라 빠른 결정론적 경로를 적용합니다.",
                    1,
                    1,
                    35,
                    false,
                    "planning",
                    "구현 계획",
                    "원본 요청 그대로 파일을 만들고 stdout까지 즉시 검증합니다.",
                    3,
                    6
                ));
                var deterministicOutcome = await TryApplyDeterministicSingleFileOutputRepairAsync(
                    rawInput,
                    request.Language,
                    codingRunRoot,
                    rawRequestedPaths,
                    rawExpectedOutput,
                    cancellationToken
                );
                if (deterministicOutcome.Applied && string.Equals(deterministicOutcome.Execution.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    var changedPaths = new[] { deterministicOutcome.ChangedPath };
                    var fastPathSummary = BuildAutonomousCodingSummary(
                        new[] { "deterministic_fast_path=single_file_output" },
                        changedPaths,
                        deterministicOutcome.Execution,
                        1
                    );
                    progressCallback?.Invoke(BuildCodingProgressUpdate(
                        "single",
                        provider,
                        model,
                        "done",
                        "코딩 작업이 완료되었습니다.",
                        1,
                        1,
                        100,
                        true,
                        "verification",
                        "최종 실행 및 검증",
                        $"최종 상태: {deterministicOutcome.Execution.Status} (exit={deterministicOutcome.Execution.ExitCode})",
                        6,
                        6
                    ));
                    outcome = new AutonomousCodingOutcome(
                        "python",
                        deterministicOutcome.Code,
                        "[deterministic-fast-path]",
                        deterministicOutcome.Execution,
                        changedPaths,
                        fastPathSummary
                    );
                }
                else
                {
                    var objective = BuildCodingAgentObjectivePrompt(contextualInput, request.Language, "단일 모델 코딩");
                    outcome = await RunAutonomousCodingLoopAsync(
                        provider,
                        model,
                        objective,
                        request.Language,
                        "single",
                        cancellationToken,
                        progressCallback,
                        workspaceRootOverride: codingRunRoot
                    );
                }
            }
            else
            {
                var objective = BuildCodingAgentObjectivePrompt(contextualInput, request.Language, "단일 모델 코딩");
                outcome = await RunAutonomousCodingLoopAsync(
                    provider,
                    model,
                    objective,
                    request.Language,
                    "single",
                    cancellationToken,
                    progressCallback,
                    workspaceRootOverride: codingRunRoot
                );
            }
        }
        catch (Exception ex)
        {
            var recoveredOutcome = await TryRecoverCodingLoopExceptionAsync(
                ex,
                rawInput,
                request.Language,
                codingRunRoot,
                rawRequestedPaths,
                cancellationToken
            );
            if (recoveredOutcome == null)
            {
                throw;
            }

            outcome = recoveredOutcome;
        }

        var citationBundle = BuildAndLogCitationMappings(
            request.Source,
            "coding-single",
            preparedInput.Citations,
            ("summary", outcome.Summary)
        );
        var citationValidationGuardFailure = BuildCitationValidationGuardFailure(citationBundle.Validation);
        var effectiveGuardFailure = preparedInput.GuardFailure ?? citationValidationGuardFailure;
        var responseSummary = outcome.Summary;
        var assistantText = BuildCodingAssistantText(
            "single",
            provider,
            model,
            outcome.Language,
            outcome.Execution,
            outcome.ChangedFiles,
            responseSummary
        );
        if (citationValidationGuardFailure is not null)
        {
            LogCitationValidationGuardBlocked(request.Source, "coding-single", citationValidationGuardFailure);
            responseSummary = BuildCitationValidationBlockedResponseText(citationValidationGuardFailure);
            assistantText = responseSummary;
        }

        _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"coding:{provider}:{model}");
        _conversationStore.AppendMessage(
            thread.Id,
            "assistant",
            assistantText,
            $"coding:{provider}:{model}",
            outcome.TokenUsage ?? TokenUsageEstimator.Estimate(contextualInput, assistantText)
        );
        await EnsureConversationTitleFromFirstTurnAsync(thread.Id, provider, model, cancellationToken);

        var note = await MaybeCompressConversationAsync(thread.Id, $"{session.Scope}-{session.Mode}", provider, model, cancellationToken);
        var updated = _conversationStore.Get(thread.Id) ?? thread;
        return PersistLatestCodingResult(new CodingRunResult(
            "single",
            updated.Id,
            provider,
            model,
            outcome.Language,
            outcome.Code,
            outcome.Execution,
            Array.Empty<CodingWorkerResult>(),
            outcome.ChangedFiles,
            responseSummary,
            updated,
            note,
            effectiveGuardFailure,
            preparedInput.Citations,
            citationBundle.Mappings,
            citationBundle.Validation,
            preparedInput.RetryAttempt,
            preparedInput.RetryMaxAttempts,
            preparedInput.RetryStopReason
        ));
    }

    private async Task<CodingRunResult> RunCodingOrchestrationCoreAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    )
    {
        var session = PrepareSessionContext(
            request.Scope,
            request.Mode,
            request.ConversationId,
            request.ConversationTitle,
            request.Project,
            request.Category,
            request.Tags,
            request.LinkedMemoryNotes,
            request.Source
        );
        var thread = session.Thread;
        var rawInput = (request.Input ?? string.Empty).Trim();
        var codingRunRoot = CreateCodingRunWorkspaceRoot("orchestration");
        if (TryHandleBrowserCodingIntent(session, rawInput, "orchestration", codingRunRoot, request.Language) is { } browserIntentResult)
        {
            return browserIntentResult;
        }

        var multiSkillRejectionOrch = TryBuildCodingMultiSkillRejectionResult(
            "orchestration",
            thread,
            rawInput,
            "-",
            "-",
            codingRunRoot,
            request.Language,
            request.Source
        );
        if (multiSkillRejectionOrch != null)
        {
            return multiSkillRejectionOrch;
        }

        var autoRoleMode = string.IsNullOrWhiteSpace(rawInput);
        var effectiveInput = autoRoleMode
            ? CodingOrchestrationPromptPolicy.BuildAutoInput(request.Language)
            : rawInput;
        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, effectiveInput);
        var sharedPrepared = await PrepareSharedInputAsync(
            effectiveInput,
            request.Attachments,
            request.WebUrls,
            request.WebSearchEnabled,
            cancellationToken,
            request.Source,
            session.SessionKey,
            session.SessionId,
            requestedSkillName,
            request.SkillScope
        );
        if (!string.IsNullOrWhiteSpace(sharedPrepared.UnsupportedMessage))
        {
            var blockedUserText = autoRoleMode
                ? "[AUTO] 입력 없이 실행: 워커 자동 역할 협의 모드"
                : rawInput;
            _conversationStore.AppendMessage(thread.Id, "user", blockedUserText, "coding-orchestration");
            _conversationStore.AppendMessage(thread.Id, "assistant", sharedPrepared.UnsupportedMessage, "coding-orchestration:unsupported");
            var blockedView = _conversationStore.Get(thread.Id) ?? thread;
            var blockedExecution = new CodeExecutionResult(
                request.Language,
                codingRunRoot,
                "-",
                "(none)",
                0,
                string.Empty,
                sharedPrepared.UnsupportedMessage,
                "skipped"
            );
            var blockedCitationBundle = BuildAndLogCitationMappings(
                request.Source,
                "coding-orchestration-unsupported",
                sharedPrepared.Citations,
                ("summary", sharedPrepared.UnsupportedMessage)
            );
            return PersistLatestCodingResult(new CodingRunResult(
                "orchestration",
                blockedView.Id,
                "-",
                "-",
                request.Language,
                string.Empty,
                blockedExecution,
                Array.Empty<CodingWorkerResult>(),
                Array.Empty<string>(),
                sharedPrepared.UnsupportedMessage,
                blockedView,
                null,
                sharedPrepared.GuardFailure,
                sharedPrepared.Citations,
                blockedCitationBundle.Mappings,
                blockedCitationBundle.Validation,
                sharedPrepared.RetryAttempt,
                sharedPrepared.RetryMaxAttempts,
                sharedPrepared.RetryStopReason
            ));
        }
        var thinkPlusPreText = ApplySelectedSkillToPrompt(
            sharedPrepared.Text,
            requestedSkillName,
            request.SkillScope
        );
        if (request.ThinkPlusEnabled)
        {
            var effectiveSkillForThinkPlus = ResolveEffectiveSkillNameForThread(requestedSkillName, session.SessionId);
            var thinkPlusContext = await BuildThinkPlusContextAsync(
                effectiveInput,
                request.Source,
                cancellationToken,
                effectiveSkillForThinkPlus
            ).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(thinkPlusContext))
            {
                thinkPlusPreText = thinkPlusContext + thinkPlusPreText;
            }
        }
        var contextualInput = BuildContextualInput(
            session.SessionId,
            thinkPlusPreText,
            session.LinkedMemoryNotes,
            contextDecisionInput: rawInput
        );

        var availableProviders = await GetAvailableProvidersAsync(cancellationToken);
        if (availableProviders.Count == 0)
        {
            var emptyResult = new CodeExecutionResult(request.Language, "-", "-", "-", 1, string.Empty, "사용 가능한 모델이 없습니다.", "error");
            emptyResult = emptyResult with { RunDirectory = codingRunRoot };
            var citationBundleUnavailable = BuildAndLogCitationMappings(
                request.Source,
                "coding-orchestration-unavailable",
                sharedPrepared.Citations,
                ("summary", "모델 사용 불가")
            );
            return PersistLatestCodingResult(new CodingRunResult(
                "orchestration",
                thread.Id,
                "-",
                "-",
                request.Language,
                string.Empty,
                emptyResult,
                Array.Empty<CodingWorkerResult>(),
                Array.Empty<string>(),
                "모델 사용 불가",
                thread,
                null,
                sharedPrepared.GuardFailure,
                sharedPrepared.Citations,
                citationBundleUnavailable.Mappings,
                citationBundleUnavailable.Validation,
                sharedPrepared.RetryAttempt,
                sharedPrepared.RetryMaxAttempts,
                sharedPrepared.RetryStopReason
            ));
        }

        var workerProviders = availableProviders
            .Where(provider =>
            {
                var selection = provider switch
                {
                    "groq" => request.GroqModel,
                    "gemini" => request.GeminiModel,
                    "cerebras" => request.CerebrasModel,
                    "nvidia" => request.NvidiaModel,
                    "copilot" => request.CopilotModel,
                    "codex" => request.CodexModel,
                    _ => null
                };
                return !IsDisabledModelSelection(selection);
            })
            .ToArray();
        if (workerProviders.Length == 0)
        {
            var emptyResult = new CodeExecutionResult(request.Language, "-", "-", "-", 1, string.Empty, "워커가 모두 '선택 안함'으로 설정되었습니다.", "error");
            emptyResult = emptyResult with { RunDirectory = codingRunRoot };
            var citationBundleWorkersDisabled = BuildAndLogCitationMappings(
                request.Source,
                "coding-orchestration-workers-disabled",
                sharedPrepared.Citations,
                ("summary", "모델 사용 불가")
            );
            return PersistLatestCodingResult(new CodingRunResult(
                "orchestration",
                thread.Id,
                "-",
                "-",
                request.Language,
                string.Empty,
                emptyResult,
                Array.Empty<CodingWorkerResult>(),
                Array.Empty<string>(),
                "모델 사용 불가",
                thread,
                null,
                sharedPrepared.GuardFailure,
                sharedPrepared.Citations,
                citationBundleWorkersDisabled.Mappings,
                citationBundleWorkersDisabled.Validation,
                sharedPrepared.RetryAttempt,
                sharedPrepared.RetryMaxAttempts,
                sharedPrepared.RetryStopReason
            ));
        }

        var resolvedWorkerModels = workerProviders.ToDictionary(
            provider => provider,
            provider => ResolveModel(
                provider,
                provider switch
                {
                    "groq" => request.GroqModel,
                    "gemini" => request.GeminiModel,
                    "cerebras" => request.CerebrasModel,
                    "nvidia" => request.NvidiaModel,
                    "copilot" => request.CopilotModel,
                    "codex" => request.CodexModel,
                    _ => null
                }
            ),
            StringComparer.OrdinalIgnoreCase
        );
        var workerRoles = autoRoleMode
            ? await NegotiateOrchestrationRolesAsync(workerProviders, resolvedWorkerModels, contextualInput, request.Language, cancellationToken)
            : BuildDefaultOrchestrationRoles(workerProviders);
        progressCallback?.Invoke(new CodingProgressUpdate(
            "orchestration",
            "-",
            "-",
            "coordination",
            "기획, 개발, 검증, 수정 단계를 순서대로 배치합니다.",
            0,
            0,
            6,
            false
        ));
        var stageAssignments = BuildOrchestrationStageAssignments(workerProviders, resolvedWorkerModels, workerRoles).ToArray();
        var preferredImplementationProvider = NormalizeProvider(request.Provider, allowAuto: true);
        if (!string.IsNullOrWhiteSpace(preferredImplementationProvider)
            && !preferredImplementationProvider.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && resolvedWorkerModels.ContainsKey(preferredImplementationProvider))
        {
            var preferredImplementationModel = string.IsNullOrWhiteSpace(request.Model)
                ? resolvedWorkerModels[preferredImplementationProvider]
                : ResolveModel(preferredImplementationProvider, request.Model);
            stageAssignments[1] = stageAssignments[1] with
            {
                Provider = preferredImplementationProvider,
                Model = preferredImplementationModel
            };
        }

        var planningStage = stageAssignments[0];
        progressCallback?.Invoke(new CodingProgressUpdate(
            "orchestration",
            planningStage.Provider,
            planningStage.Model,
            "planning",
            "기획 담당 모델이 구현 계획과 검증 포인트를 정리합니다.",
            0,
            0,
            12,
            false
        ));
        var planningProfile = ResolveCodingExecutionProfile(
            planningStage.Provider,
            planningStage.Model,
            contextualInput,
            request.Language,
            Array.Empty<string>()
        );
        var planningGenerated = await GenerateByProviderSafeAsync(
            planningStage.Provider,
            planningStage.Model,
            CodingOrchestrationPromptPolicy.BuildPlannerPrompt(contextualInput, request.Language, planningStage.RolePrompt),
            cancellationToken,
            ResolveDraftGenerationMaxOutputTokens(planningProfile),
            useRawCodexPrompt: true,
            optimizeCodexForCoding: planningProfile.OptimizeCodexCli,
            timeoutOverrideSeconds: planningProfile.RequestTimeoutSeconds
        );
        var planningText = SanitizeChatOutput(planningGenerated.Text);
        var planningResult = new CodingWorkerResult(
            planningStage.Provider,
            planningStage.Model,
            request.Language,
            string.Empty,
            planningText,
            new CodeExecutionResult(
                request.Language,
                codingRunRoot,
                "-",
                "(planning)",
                0,
                string.Empty,
                string.Empty,
                "skipped"
            ),
            Array.Empty<string>(),
            planningStage.Title,
            string.IsNullOrWhiteSpace(planningText) ? "기획 단계 응답이 비어 있습니다." : planningText,
            planningGenerated.TokenUsage
        );

        var implementationStage = stageAssignments[1];
        var implementationPrompt = BuildCodingAgentObjectivePrompt(
            CodingOrchestrationPromptPolicy.BuildImplementationPrompt(contextualInput, request.Language, implementationStage.RolePrompt, planningResult),
            request.Language,
            "오케스트레이션 개발"
        );
        var implementationResult = await RunCodingWorkerAsync(
            implementationStage.Provider,
            implementationStage.Model,
            implementationPrompt,
            request.Language,
            cancellationToken,
            progressCallback,
            "orchestration",
            allowRunActions: true,
            role: implementationStage.Title,
            workspaceRootOverride: codingRunRoot
        );

        var verificationStage = stageAssignments[2];
        var verificationPrompt = BuildCodingAgentObjectivePrompt(
            CodingOrchestrationPromptPolicy.BuildVerificationPrompt(
                contextualInput,
                request.Language,
                verificationStage.RolePrompt,
                planningResult,
                implementationResult
            ),
            request.Language,
            "오케스트레이션 검증"
        );
        var verificationResult = await RunCodingWorkerAsync(
            verificationStage.Provider,
            verificationStage.Model,
            verificationPrompt,
            request.Language,
            cancellationToken,
            progressCallback,
            "orchestration",
            allowRunActions: true,
            role: verificationStage.Title,
            workspaceRootOverride: codingRunRoot
        );

        var fixStage = stageAssignments[3];
        CodingWorkerResult fixResult;
        if (ShouldRunOrchestrationFixStage(verificationResult))
        {
            var fixPrompt = BuildCodingAgentObjectivePrompt(
                CodingOrchestrationPromptPolicy.BuildFixPrompt(
                    contextualInput,
                    request.Language,
                    fixStage.RolePrompt,
                    planningResult,
                    implementationResult,
                    verificationResult
                ),
                request.Language,
                "오케스트레이션 수정"
            );
            fixResult = await RunCodingWorkerAsync(
                fixStage.Provider,
                fixStage.Model,
                fixPrompt,
                request.Language,
                cancellationToken,
                progressCallback,
                "orchestration",
                allowRunActions: true,
                role: fixStage.Title,
                workspaceRootOverride: codingRunRoot
            );
        }
        else
        {
            fixResult = CodingWorkerSelectionPolicy.BuildSkippedWorker(
                fixStage.Provider,
                fixStage.Model,
                request.Language,
                fixStage.Title,
                codingRunRoot,
                "검증 단계에서 치명 오류가 없어 수정 단계를 건너뛰었습니다."
            );
        }

        var workerResults = new[] { planningResult, implementationResult, verificationResult, fixResult };
        var finalWorker = CodingWorkerSelectionPolicy.SelectBest(
            new[] { implementationResult, verificationResult, fixResult }
                .Where(worker => worker.Execution.Command != "(planning)")
                .ToArray(),
            fixResult.Execution.Status == "skipped" ? verificationResult : fixResult
        );
        var orchestrationSummary = CodingWorkerSelectionPolicy.BuildOrchestrationSummary(workerResults, finalWorker);
        var citationBundleOrchestration = BuildAndLogCitationMappings(
            request.Source,
            "coding-orchestration",
            sharedPrepared.Citations,
            ("summary", orchestrationSummary)
        );
        var citationValidationGuardFailure = BuildCitationValidationGuardFailure(citationBundleOrchestration.Validation);
        var effectiveGuardFailure = sharedPrepared.GuardFailure ?? citationValidationGuardFailure;
        var responseSummary = orchestrationSummary;
        var assistantText = BuildCodingAssistantText(
            "orchestration",
            finalWorker.Provider,
            finalWorker.Model,
            finalWorker.Language,
            finalWorker.Execution,
            CodingWorkerSelectionPolicy.MergeChangedFilesForBest(finalWorker, workerResults),
            responseSummary
        );
        if (citationValidationGuardFailure is not null)
        {
            LogCitationValidationGuardBlocked(request.Source, "coding-orchestration", citationValidationGuardFailure);
            responseSummary = BuildCitationValidationBlockedResponseText(citationValidationGuardFailure);
            assistantText = responseSummary;
        }
        var conversationUserText = autoRoleMode
            ? "[AUTO] 입력 없이 실행: 워커 자동 역할 협의 모드"
            : rawInput;
        _conversationStore.AppendMessage(thread.Id, "user", conversationUserText, "coding-orchestration");
        _conversationStore.AppendMessage(
            thread.Id,
            "assistant",
            assistantText,
            $"coding-orchestration:{finalWorker.Provider}:{finalWorker.Model}",
            TokenUsageEstimator.Combine(workerResults.Select(worker => worker.TokenUsage))
                ?? TokenUsageEstimator.Estimate(contextualInput, assistantText)
        );
        await EnsureConversationTitleFromFirstTurnAsync(thread.Id, finalWorker.Provider, finalWorker.Model, cancellationToken);

        var note = await MaybeCompressConversationAsync(thread.Id, $"{session.Scope}-{session.Mode}", finalWorker.Provider, finalWorker.Model, cancellationToken);
        var updated = _conversationStore.Get(thread.Id) ?? thread;
        return PersistLatestCodingResult(new CodingRunResult(
            "orchestration",
            updated.Id,
            finalWorker.Provider,
            finalWorker.Model,
            finalWorker.Language,
            finalWorker.Code,
            finalWorker.Execution,
            workerResults,
            CodingWorkerSelectionPolicy.MergeChangedFilesForBest(finalWorker, workerResults),
            responseSummary,
            updated,
            note,
            effectiveGuardFailure,
            sharedPrepared.Citations,
            citationBundleOrchestration.Mappings,
            citationBundleOrchestration.Validation,
            sharedPrepared.RetryAttempt,
            sharedPrepared.RetryMaxAttempts,
            sharedPrepared.RetryStopReason
        ));
    }

    private async Task<CodingRunResult> RunCodingMultiCoreAsync(
        CodingRunRequest request,
        CancellationToken cancellationToken,
        Action<CodingProgressUpdate>? progressCallback = null
    )
    {
        var session = PrepareSessionContext(
            request.Scope,
            request.Mode,
            request.ConversationId,
            request.ConversationTitle,
            request.Project,
            request.Category,
            request.Tags,
            request.LinkedMemoryNotes,
            request.Source
        );
        var thread = session.Thread;
        var rawInput = (request.Input ?? string.Empty).Trim();
        var codingRunRoot = CreateCodingRunWorkspaceRoot("multi");
        if (TryHandleBrowserCodingIntent(session, rawInput, "multi", codingRunRoot, request.Language) is { } browserIntentResult)
        {
            return browserIntentResult;
        }

        var multiSkillRejectionMulti = TryBuildCodingMultiSkillRejectionResult(
            "multi",
            thread,
            rawInput,
            "-",
            "-",
            codingRunRoot,
            request.Language,
            request.Source
        );
        if (multiSkillRejectionMulti != null)
        {
            return multiSkillRejectionMulti;
        }

        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, rawInput);
        var sharedPrepared = await PrepareSharedInputAsync(
            rawInput,
            request.Attachments,
            request.WebUrls,
            request.WebSearchEnabled,
            cancellationToken,
            request.Source,
            session.SessionKey,
            session.SessionId,
            requestedSkillName,
            request.SkillScope
        );
        if (!string.IsNullOrWhiteSpace(sharedPrepared.UnsupportedMessage))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "coding-multi");
            _conversationStore.AppendMessage(thread.Id, "assistant", sharedPrepared.UnsupportedMessage, "coding-multi:unsupported");
            var blockedView = _conversationStore.Get(thread.Id) ?? thread;
            var blockedExecution = new CodeExecutionResult(
                request.Language,
                codingRunRoot,
                "-",
                "(none)",
                0,
                string.Empty,
                sharedPrepared.UnsupportedMessage,
                "skipped"
            );
            var blockedCitationBundle = BuildAndLogCitationMappings(
                request.Source,
                "coding-multi-unsupported",
                sharedPrepared.Citations,
                ("summary", sharedPrepared.UnsupportedMessage)
            );
            return PersistLatestCodingResult(new CodingRunResult(
                "multi",
                blockedView.Id,
                "-",
                "-",
                request.Language,
                string.Empty,
                blockedExecution,
                Array.Empty<CodingWorkerResult>(),
                Array.Empty<string>(),
                sharedPrepared.UnsupportedMessage,
                blockedView,
                null,
                sharedPrepared.GuardFailure,
                sharedPrepared.Citations,
                blockedCitationBundle.Mappings,
                blockedCitationBundle.Validation,
                sharedPrepared.RetryAttempt,
                sharedPrepared.RetryMaxAttempts,
                sharedPrepared.RetryStopReason
            ));
        }
        var thinkPlusPreText = ApplySelectedSkillToPrompt(
            sharedPrepared.Text,
            requestedSkillName,
            request.SkillScope
        );
        if (request.ThinkPlusEnabled)
        {
            var effectiveSkillForThinkPlus = ResolveEffectiveSkillNameForThread(requestedSkillName, session.SessionId);
            var thinkPlusContext = await BuildThinkPlusContextAsync(
                rawInput,
                request.Source,
                cancellationToken,
                effectiveSkillForThinkPlus
            ).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(thinkPlusContext))
            {
                thinkPlusPreText = thinkPlusContext + thinkPlusPreText;
            }
        }
        var contextualInput = BuildContextualInput(
            session.SessionId,
            thinkPlusPreText,
            session.LinkedMemoryNotes,
            contextDecisionInput: rawInput
        );
        var workers = await GetAvailableProvidersAsync(cancellationToken);
        workers = workers
            .Where(provider =>
            {
                var selection = provider switch
                {
                    "groq" => request.GroqModel,
                    "gemini" => request.GeminiModel,
                    "cerebras" => request.CerebrasModel,
                    "nvidia" => request.NvidiaModel,
                    "copilot" => request.CopilotModel,
                    "codex" => request.CodexModel,
                    _ => null
                };
                return !IsDisabledModelSelection(selection);
            })
            .ToArray();
        if (workers.Count == 0)
        {
            var emptyResult = new CodeExecutionResult(request.Language, "-", "-", "-", 1, string.Empty, "다중 코딩 워커가 모두 '선택 안함' 또는 미사용 상태입니다.", "error");
            emptyResult = emptyResult with { RunDirectory = codingRunRoot };
            var citationBundleWorkersDisabled = BuildAndLogCitationMappings(
                request.Source,
                "coding-multi-workers-disabled",
                sharedPrepared.Citations,
                ("summary", "모델 사용 불가")
            );
            return PersistLatestCodingResult(new CodingRunResult(
                "multi",
                thread.Id,
                "-",
                "-",
                request.Language,
                string.Empty,
                emptyResult,
                Array.Empty<CodingWorkerResult>(),
                Array.Empty<string>(),
                "모델 사용 불가",
                thread,
                null,
                sharedPrepared.GuardFailure,
                sharedPrepared.Citations,
                citationBundleWorkersDisabled.Mappings,
                citationBundleWorkersDisabled.Validation,
                sharedPrepared.RetryAttempt,
                sharedPrepared.RetryMaxAttempts,
                sharedPrepared.RetryStopReason
            ));
        }

        var workerInputs = workers
            .Select(provider => new
            {
                Provider = provider,
                Model = ResolveModel(
                    provider,
                    provider switch
                    {
                        "groq" => request.GroqModel,
                        "gemini" => request.GeminiModel,
                        "cerebras" => request.CerebrasModel,
                        "nvidia" => request.NvidiaModel,
                        "copilot" => request.CopilotModel,
                        "codex" => request.CodexModel,
                        _ => null
                    }
                )
            })
            .Select(item => new
            {
                item.Provider,
                item.Model,
                PrepareTask = PrepareInputForProviderAsync(
                    contextualInput,
                    item.Provider,
                    item.Model,
                    request.Attachments,
                    request.WebUrls,
                    request.WebSearchEnabled,
                    false,
                    cancellationToken
                )
            })
            .ToArray();
        await Task.WhenAll(workerInputs.Select(item => item.PrepareTask));

        var workerTaskList = new List<Task<CodingWorkerResult>>();
        progressCallback?.Invoke(new CodingProgressUpdate(
            "multi",
            "-",
            "-",
            "coordination",
            "선택한 각 모델이 서로 독립된 작업 폴더에서 끝까지 구현합니다.",
            0,
            0,
            8,
            false
        ));
        foreach (var workerInput in workerInputs)
        {
            var provider = workerInput.Provider;
            var model = workerInput.Model;
            var providerPrepared = workerInput.PrepareTask.Result;
            if (!string.IsNullOrWhiteSpace(providerPrepared.UnsupportedMessage))
            {
                workerTaskList.Add(Task.FromResult(BuildUnsupportedCodingWorkerResult(
                    provider,
                    model,
                    request.Language,
                    providerPrepared.UnsupportedMessage,
                    "독립 완주"
                )));
                continue;
            }

            var prompt = BuildCodingAgentObjectivePrompt(
                CodingOrchestrationPromptPolicy.BuildMultiWorkerPrompt(providerPrepared.Text, request.Language),
                request.Language,
                "다중 코딩 단독 완주"
            );
            workerTaskList.Add(RunCodingWorkerAsync(
                provider,
                model,
                prompt,
                request.Language,
                cancellationToken,
                progressCallback,
                "multi-worker",
                allowRunActions: true,
                role: "독립 완주",
                workspaceRootOverride: CodingWorkerSelectionPolicy.BuildWorkerWorkspaceRoot(codingRunRoot, provider, model)
            ));
        }
        var workerTasks = workerTaskList.ToArray();
        await Task.WhenAll(workerTasks);

        var workerResults = workerTasks.Select(x => x.Result).ToArray();
        progressCallback?.Invoke(new CodingProgressUpdate(
            "multi",
            "-",
            "-",
            "comparison",
            $"모델 {workerResults.Length}개의 완주 결과를 비교 요약합니다.",
            0,
            0,
            40,
            false
        ));
        var availabilityByProvider = await GetProviderAvailabilityMapAsync(cancellationToken);
        var selectionByProvider = BuildProviderSelectionMap(
            request.GroqModel,
            request.GeminiModel,
            request.CerebrasModel,
            request.CopilotModel,
            request.CodexModel,
            request.NvidiaModel
        );
        var workerChatResults = workerResults
            .Select(x => new LlmSingleChatResult(x.Provider, x.Model, x.RawResponse))
            .ToArray();
        var successfulWorkers = workerChatResults
            .Where(x => IsUsableWorkerResult(x, availabilityByProvider, selectionByProvider))
            .ToArray();
        var routingCategory = ResolveCodingTaskCategory(request.Category, rawInput);
        var requestedAggregateProvider = NormalizeProvider(request.Provider, allowAuto: true);
        var aggregateProvider = ResolveProviderForAggregation(
            routingCategory,
            requestedAggregateProvider,
            successfulWorkers,
            availabilityByProvider,
            selectionByProvider,
            allowProviderWithoutWorkerFallback: true
        );
        if (requestedAggregateProvider == "auto"
            && string.Equals(aggregateProvider, "copilot", StringComparison.OrdinalIgnoreCase))
        {
                var fallbackAggregateProvider = new[] { "codex", "gemini", "nvidia", "cerebras", "groq" }
                .FirstOrDefault(candidate => successfulWorkers.Any(worker => worker.Provider.Equals(candidate, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(fallbackAggregateProvider))
            {
                aggregateProvider = fallbackAggregateProvider;
            }
        }

        if (aggregateProvider == "none")
        {
            aggregateProvider = workerResults[0].Provider;
        }
        else if (selectionByProvider.TryGetValue(aggregateProvider, out var selection)
            && IsDisabledModelSelection(selection))
        {
            aggregateProvider = workerResults[0].Provider;
        }

        var aggregateModelOverride = request.Model;
        if (string.IsNullOrWhiteSpace(aggregateModelOverride))
        {
            aggregateModelOverride = aggregateProvider switch
            {
                "groq" => request.GroqModel,
                "gemini" => request.GeminiModel,
                "cerebras" => request.CerebrasModel,
                "nvidia" => request.NvidiaModel,
                "copilot" => request.CopilotModel,
                "codex" => request.CodexModel,
                _ => null
            };
        }

        var aggregateModel = ResolveModelForCategory(routingCategory, aggregateProvider, aggregateModelOverride);
        string summary;
        TokenUsage? summaryTokenUsage = null;
        if (aggregateProvider == "none")
        {
            summary = """
                [공통 요약]
                공통 요약 모델을 고를 수 없어 자동 비교 요약을 생략했습니다.

                [공통점]
                공통점 자동 정리 없음

                [차이]
                모델별 카드에서 직접 비교가 필요합니다.

                [추천]
                상태가 `ok`인 모델부터 파일 칩과 실행 로그를 확인하세요.
                """;
        }
        else
        {
            progressCallback?.Invoke(new CodingProgressUpdate(
                "multi",
                aggregateProvider,
                aggregateModel,
                "comparison",
                "모델별 완주 결과의 공통점과 차이를 요약합니다.",
                0,
                0,
                52,
                false
            ));
            var summaryResult = await GenerateByProviderSafeAsync(
                aggregateProvider,
                aggregateModel,
                BuildMultiCodingSummaryPrompt(contextualInput, workerResults),
                cancellationToken,
                Math.Min(Math.Max(1400, _context.CodingMaxOutputTokens), 2600),
                useRawCodexPrompt: true,
                codexWorkingDirectoryOverride: codingRunRoot,
                optimizeCodexForCoding: string.Equals(aggregateProvider, "codex", StringComparison.OrdinalIgnoreCase)
            );
            summary = SanitizeChatOutput(summaryResult.Text);
            summaryTokenUsage = summaryResult.TokenUsage;
        }

        var summarySections = ParseCodingMultiSummarySections(summary);
        var multiSummary = string.IsNullOrWhiteSpace(summarySections.CommonSummary)
            ? "공통 요약을 생성하지 못했습니다."
            : summarySections.CommonSummary;
        var bestWorker = CodingWorkerSelectionPolicy.SelectBest(workerResults, workerResults.FirstOrDefault() ?? CodingWorkerSelectionPolicy.BuildSkippedWorker(
            aggregateProvider,
            aggregateModel,
            request.Language,
            "독립 완주",
            codingRunRoot,
            "선택 가능한 worker 결과가 없습니다."
        ));
        var multiExecution = CodingWorkerSelectionPolicy.BuildMultiResultExecution(codingRunRoot, request.Language, workerResults, bestWorker);
        var comparisonAssistantText = BuildCodingMultiComparisonAssistantText(workerResults);
        var summaryAssistantText = BuildCodingMultiSummaryAssistantText(
            summarySections.CommonSummary,
            summarySections.CommonPoints,
            summarySections.Differences,
            summarySections.Recommendation
        );

        var citationBundleMulti = BuildAndLogCitationMappings(
            request.Source,
            "coding-multi",
            sharedPrepared.Citations,
            ("summary", multiSummary)
        );
        var citationValidationGuardFailure = BuildCitationValidationGuardFailure(citationBundleMulti.Validation);
        var effectiveGuardFailure = sharedPrepared.GuardFailure ?? citationValidationGuardFailure;
        var responseSummary = multiSummary;
        var assistantText = summaryAssistantText;
        if (citationValidationGuardFailure is not null)
        {
            LogCitationValidationGuardBlocked(request.Source, "coding-multi", citationValidationGuardFailure);
            responseSummary = BuildCitationValidationBlockedResponseText(citationValidationGuardFailure);
            assistantText = responseSummary;
        }
        _conversationStore.AppendMessage(thread.Id, "user", rawInput, "coding-multi");
        if (citationValidationGuardFailure is null)
        {
            _conversationStore.AppendMessage(
                thread.Id,
                "assistant",
                comparisonAssistantText,
                "coding-multi:comparison",
                TokenUsageEstimator.Combine(workerResults.Select(worker => worker.TokenUsage))
            );
        }
        _conversationStore.AppendMessage(
            thread.Id,
            "assistant",
            assistantText,
            $"coding-multi:summary={aggregateProvider}:{aggregateModel}",
            summaryTokenUsage ?? TokenUsageEstimator.Estimate(contextualInput, assistantText)
        );
        await EnsureConversationTitleFromFirstTurnAsync(thread.Id, aggregateProvider, aggregateModel, cancellationToken);

        var note = await MaybeCompressConversationAsync(
            thread.Id,
            $"{session.Scope}-{session.Mode}",
            aggregateProvider,
            aggregateModel,
            cancellationToken
        );
        var updated = _conversationStore.Get(thread.Id) ?? thread;
        return PersistLatestCodingResult(new CodingRunResult(
            "multi",
            updated.Id,
            bestWorker.Provider,
            bestWorker.Model,
            bestWorker.Language,
            bestWorker.Code,
            multiExecution,
            workerResults,
            CodingWorkerSelectionPolicy.MergeChangedFilesForBest(bestWorker, workerResults),
            responseSummary,
            updated,
            note,
            effectiveGuardFailure,
            sharedPrepared.Citations,
            citationBundleMulti.Mappings,
            citationBundleMulti.Validation,
            sharedPrepared.RetryAttempt,
            sharedPrepared.RetryMaxAttempts,
            sharedPrepared.RetryStopReason,
            citationValidationGuardFailure is null ? summarySections.CommonSummary : responseSummary,
            citationValidationGuardFailure is null ? summarySections.CommonPoints : string.Empty,
            citationValidationGuardFailure is null ? summarySections.Differences : string.Empty,
            citationValidationGuardFailure is null ? summarySections.Recommendation : string.Empty
        ));
    }

    private CodingRunResult PersistLatestCodingResult(CodingRunResult result)
    {
        var resultWithEvidence = result.Evidence == null
            ? result with { Evidence = BuildCodingEvidencePack("generation", result.Execution, result.ChangedFiles) }
            : result;
        var updatedConversation = _conversationStore.SetLatestCodingResult(
            resultWithEvidence.ConversationId,
            BuildConversationCodingResultSnapshot(resultWithEvidence)
        );
        return resultWithEvidence with
        {
            Conversation = updatedConversation,
            ConversationId = updatedConversation.Id
        };
    }

    private static ConversationCodingResultSnapshot BuildConversationCodingResultSnapshot(CodingRunResult result)
    {
        return new ConversationCodingResultSnapshot(
            result.Mode,
            result.ConversationId,
            result.Provider,
            result.Model,
            result.Language,
            result.Summary,
            result.Execution,
            result.Workers.Select(worker => new CodingWorkerResultSnapshot(
                worker.Provider,
                worker.Model,
                worker.Language,
                worker.Execution,
                worker.ChangedFiles.ToArray(),
                worker.Role,
                worker.Summary
            )).ToArray(),
            result.ChangedFiles.ToArray(),
            result.CommonSummary,
            result.CommonPoints,
            result.Differences,
            result.Recommendation,
            result.Evidence
        );
    }

    private static CodingEvidencePack BuildCodingEvidencePack(
        string runMode,
        CodeExecutionResult? execution,
        IReadOnlyList<string>? changedFiles,
        string previewUrl = "",
        string previewEntry = "",
        string screenshotPath = "",
        string consoleSummary = ""
    )
    {
        var files = (changedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .Take(80)
            .ToArray();
        return new CodingEvidencePack(
            runMode,
            execution?.Command ?? string.Empty,
            execution?.ExitCode,
            execution?.Status ?? (string.IsNullOrWhiteSpace(previewUrl) ? "unknown" : "preview_ready"),
            TrimForOutput(execution?.StdOut ?? string.Empty, 2400),
            TrimForOutput(execution?.StdErr ?? string.Empty, 2400),
            files,
            previewUrl,
            previewEntry,
            screenshotPath,
            consoleSummary
        );
    }

    private sealed record OrchestrationStageAssignment(
        string StageKey,
        string Title,
        string Provider,
        string Model,
        string RolePrompt
    );

    private static IReadOnlyList<OrchestrationStageAssignment> BuildOrchestrationStageAssignments(
        IReadOnlyList<string> providers,
        IReadOnlyDictionary<string, string> modelByProvider,
        IReadOnlyDictionary<string, string> roleByProvider
    )
    {
        var selectedProviders = providers
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedProviders.Length == 0)
        {
            return Array.Empty<OrchestrationStageAssignment>();
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copilotPinned = modelByProvider.TryGetValue("copilot", out var copilotModel)
            && IsPinnedCopilotModel("copilot", copilotModel);
        return new[]
        {
            CreateOrchestrationStageAssignment("planning", "기획", new[] { "설계", "기획", "리스크", "구조", "테스트" }, copilotPinned ? new[] { "gemini", "codex", "cerebras", "groq", "copilot" } : new[] { "gemini", "codex", "cerebras", "copilot", "groq" }, selectedProviders, modelByProvider, roleByProvider, used),
            CreateOrchestrationStageAssignment("implementation", "개발", new[] { "구현", "생성", "개발", "초안" }, copilotPinned ? new[] { "codex", "cerebras", "gemini", "groq", "copilot" } : new[] { "copilot", "codex", "cerebras", "groq", "gemini" }, selectedProviders, modelByProvider, roleByProvider, used),
            CreateOrchestrationStageAssignment("verification", "검증 및 테스트", new[] { "검증", "리뷰", "테스트", "리스크", "품질" }, copilotPinned ? new[] { "gemini", "codex", "groq", "cerebras", "copilot" } : new[] { "gemini", "codex", "groq", "copilot", "cerebras" }, selectedProviders, modelByProvider, roleByProvider, used),
            CreateOrchestrationStageAssignment("fix", "수정", new[] { "수정", "보완", "정밀", "마무리", "누락" }, copilotPinned ? new[] { "codex", "gemini", "groq", "cerebras", "copilot" } : new[] { "codex", "copilot", "gemini", "groq", "cerebras" }, selectedProviders, modelByProvider, roleByProvider, used)
        };
    }

    private static OrchestrationStageAssignment CreateOrchestrationStageAssignment(
        string stageKey,
        string title,
        IReadOnlyList<string> keywords,
        IReadOnlyList<string> priorityOrder,
        IReadOnlyList<string> providers,
        IReadOnlyDictionary<string, string> modelByProvider,
        IReadOnlyDictionary<string, string> roleByProvider,
        ISet<string> used
    )
    {
        string PickProvider(bool requireUnused, bool requireKeyword)
        {
            foreach (var candidate in priorityOrder)
            {
                var provider = providers.FirstOrDefault(value => value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(provider))
                {
                    continue;
                }

                if (requireUnused && used.Contains(provider))
                {
                    continue;
                }

                roleByProvider.TryGetValue(provider, out var rolePrompt);
                var hasKeyword = keywords.Any(keyword => (rolePrompt ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase));
                if (requireKeyword && !hasKeyword)
                {
                    continue;
                }

                return provider;
            }

            foreach (var provider in providers)
            {
                if (requireUnused && used.Contains(provider))
                {
                    continue;
                }

                roleByProvider.TryGetValue(provider, out var rolePrompt);
                var hasKeyword = keywords.Any(keyword => (rolePrompt ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase));
                if (requireKeyword && !hasKeyword)
                {
                    continue;
                }

                return provider;
            }

            return providers[0];
        }

        var provider = PickProvider(requireUnused: true, requireKeyword: true);
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = PickProvider(requireUnused: true, requireKeyword: false);
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = PickProvider(requireUnused: false, requireKeyword: true);
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = providers[0];
        }

        used.Add(provider);
        roleByProvider.TryGetValue(provider, out var rolePrompt);
        modelByProvider.TryGetValue(provider, out var model);
        return new OrchestrationStageAssignment(
            stageKey,
            title,
            provider,
            string.IsNullOrWhiteSpace(model) ? "-" : model,
            string.IsNullOrWhiteSpace(rolePrompt) ? $"{title} 담당" : rolePrompt
        );
    }

    private static bool ShouldRunOrchestrationFixStage(CodingWorkerResult verificationResult)
    {
        var status = (verificationResult.Execution.Status ?? string.Empty).Trim();
        return status.Equals("error", StringComparison.OrdinalIgnoreCase)
            || status.Equals("quality_failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("timeout", StringComparison.OrdinalIgnoreCase)
            || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("killed", StringComparison.OrdinalIgnoreCase)
            || CodingWorkerSelectionPolicy.HasQualityFailure(verificationResult);
    }

    private static Dictionary<string, string> BuildDefaultOrchestrationRoles(IReadOnlyList<string> providers)
    {
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["copilot"] = "코드 생성/수정 담당. 바로 실행 가능한 코드/패치를 우선 제시하세요.",
            ["codex"] = "정밀 구현/검증 담당. 요구사항 누락 없이 완성도 높은 해법을 제시하세요.",
            ["gemini"] = "리뷰/설계 검증 담당. 버그 원인, 예외, 트레이드오프를 점검하세요.",
            ["groq"] = "빠른 보조 담당. 로그 해석, 대안 아이디어, 빠른 수정 포인트를 제시하세요."
        };

        return providers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                provider => provider,
                provider => roles.TryGetValue(provider, out var role)
                    ? role
                    : "구현자. 동작 코드 중심으로 작성하세요.",
                StringComparer.OrdinalIgnoreCase
            );
    }

    private Task<Dictionary<string, string>> NegotiateOrchestrationRolesAsync(
        IReadOnlyList<string> providers,
        IReadOnlyDictionary<string, string> modelByProvider,
        string contextualInput,
        string language,
        CancellationToken cancellationToken
    )
    {
        _ = language;
        _ = cancellationToken;
        var defaults = BuildDefaultOrchestrationRoles(providers);
        if (providers.Count <= 1)
        {
            return Task.FromResult(defaults);
        }

        var normalizedInput = (contextualInput ?? string.Empty).ToLowerInvariant();
        var uiHeavy = ContainsAny(normalizedInput, "ui", "ux", "layout", "frontend", "html", "css", "컴포넌트", "화면");
        var reviewHeavy = ContainsAny(normalizedInput, "bug", "error", "fix", "debug", "test", "회귀", "검증", "오류", "테스트");
        var copilotPinned = modelByProvider.TryGetValue("copilot", out var copilotModel)
            && IsPinnedCopilotModel("copilot", copilotModel);
        var resolved = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);

        if (resolved.ContainsKey("copilot"))
        {
            resolved["copilot"] = copilotPinned
                ? "보조 구현 담당. 작은 파일 수정, 누락 보완, 형식 정리에 집중하고 주 구현 판단은 다른 모델에 양보하세요."
                : uiHeavy
                    ? "주 구현 담당. 화면/컴포넌트 초안을 빠르게 구성하고 필요한 파일 구조를 제안하세요."
                    : "주 구현 담당. 핵심 기능이 실제로 동작하는 첫 초안을 만드세요.";
        }

        if (resolved.ContainsKey("codex"))
        {
            resolved["codex"] = reviewHeavy
                ? "정밀 수정/최종 검증 담당. 실패 원인, 엣지케이스, 누락된 검증 포인트를 우선 보완하세요."
                : "정밀 구현 담당. 누락된 세부 동작과 예외 처리를 보완하세요.";
        }

        if (resolved.ContainsKey("gemini"))
        {
            resolved["gemini"] = "설계/리스크 리뷰 담당. 요구사항 누락, 예외 흐름, 테스트 포인트를 점검하세요.";
        }

        if (resolved.ContainsKey("groq"))
        {
            resolved["groq"] = "빠른 보조 담당. 로그 해석, 단순 수정 포인트, 대안 구현 아이디어를 압축해서 제시하세요.";
        }

        if (resolved.ContainsKey("cerebras"))
        {
            resolved["cerebras"] = "대용량 초안 담당. 여러 파일에 걸친 구조 정리와 보완 코드를 제안하세요.";
        }

        return Task.FromResult(resolved);
    }

    private static string ExtractNegotiatedRoleText(string text)
    {
        var cleaned = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var lines = cleaned
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var normalized = line.Trim().TrimStart('-', '*', '#', ' ');
            if (normalized.StartsWith("역할:", StringComparison.OrdinalIgnoreCase))
            {
                var role = normalized[3..].Trim();
                if (!string.IsNullOrWhiteSpace(role))
                {
                    return role;
                }
            }

            if (normalized.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
            {
                var role = normalized[5..].Trim();
                if (!string.IsNullOrWhiteSpace(role))
                {
                    return role;
                }
            }
        }

        return lines.Length == 0 ? string.Empty : lines[0];
    }

    private static Dictionary<string, string> ParseNegotiatedRoleAssignments(string text, IReadOnlyList<string> providers)
    {
        var known = providers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimStart('-', '*', '#', ' ');
            var split = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (split.Length < 2)
            {
                continue;
            }

            var provider = split[0].Trim().ToLowerInvariant();
            if (!known.Contains(provider))
            {
                continue;
            }

            var role = split[1].Trim();
            if (!string.IsNullOrWhiteSpace(role))
            {
                map[provider] = role;
            }
        }

        return map;
    }
}
