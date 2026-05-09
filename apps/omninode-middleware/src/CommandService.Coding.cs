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
            session.SessionId
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

        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, rawInput);
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
        var contextualInput = BuildContextualInput(session.SessionId, thinkPlusPreText, session.LinkedMemoryNotes);
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
        _conversationStore.AppendMessage(thread.Id, "assistant", assistantText, $"coding:{provider}:{model}");
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
            ? BuildAutoOrchestrationCodingInput(request.Language)
            : rawInput;
        var sharedPrepared = await PrepareSharedInputAsync(
            effectiveInput,
            request.Attachments,
            request.WebUrls,
            request.WebSearchEnabled,
            cancellationToken,
            request.Source,
            session.SessionKey,
            session.SessionId
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
        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, effectiveInput);
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
        var contextualInput = BuildContextualInput(session.SessionId, thinkPlusPreText, session.LinkedMemoryNotes);

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
        var planningText = SanitizeChatOutput((await GenerateByProviderSafeAsync(
            planningStage.Provider,
            planningStage.Model,
            BuildOrchestrationPlannerPrompt(contextualInput, request.Language, planningStage.RolePrompt),
            cancellationToken,
            ResolveDraftGenerationMaxOutputTokens(planningProfile),
            useRawCodexPrompt: true,
            optimizeCodexForCoding: planningProfile.OptimizeCodexCli,
            timeoutOverrideSeconds: planningProfile.RequestTimeoutSeconds
        )).Text);
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
            string.IsNullOrWhiteSpace(planningText) ? "기획 단계 응답이 비어 있습니다." : planningText
        );

        var implementationStage = stageAssignments[1];
        var implementationPrompt = BuildCodingAgentObjectivePrompt(
            BuildOrchestrationImplementationPrompt(contextualInput, request.Language, implementationStage.RolePrompt, planningResult),
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
            BuildOrchestrationVerificationPrompt(
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
                BuildOrchestrationFixPrompt(
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
            fixResult = BuildSkippedCodingWorkerResult(
                fixStage.Provider,
                fixStage.Model,
                request.Language,
                fixStage.Title,
                codingRunRoot,
                "검증 단계에서 치명 오류가 없어 수정 단계를 건너뛰었습니다."
            );
        }

        var workerResults = new[] { planningResult, implementationResult, verificationResult, fixResult };
        var finalWorker = fixResult.Execution.Status == "skipped" ? verificationResult : fixResult;
        var orchestrationSummary = BuildOrchestrationSummary(workerResults, finalWorker);
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
            MergeChangedFiles(workerResults),
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
        _conversationStore.AppendMessage(thread.Id, "assistant", assistantText, $"coding-orchestration:{finalWorker.Provider}:{finalWorker.Model}");
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
            MergeChangedFiles(workerResults),
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

        var sharedPrepared = await PrepareSharedInputAsync(
            rawInput,
            request.Attachments,
            request.WebUrls,
            request.WebSearchEnabled,
            cancellationToken,
            request.Source,
            session.SessionKey,
            session.SessionId
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
        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, rawInput);
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
        var contextualInput = BuildContextualInput(session.SessionId, thinkPlusPreText, session.LinkedMemoryNotes);
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
                BuildMultiCodingWorkerPrompt(providerPrepared.Text, request.Language),
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
                workspaceRootOverride: BuildMultiCodingWorkerWorkspaceRoot(codingRunRoot, provider, model)
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
                Math.Min(Math.Max(1400, _config.CodingMaxOutputTokens), 2600),
                useRawCodexPrompt: true,
                codexWorkingDirectoryOverride: codingRunRoot,
                optimizeCodexForCoding: string.Equals(aggregateProvider, "codex", StringComparison.OrdinalIgnoreCase)
            );
            summary = SanitizeChatOutput(summaryResult.Text);
        }

        var summarySections = ParseCodingMultiSummarySections(summary);
        var multiSummary = string.IsNullOrWhiteSpace(summarySections.CommonSummary)
            ? "공통 요약을 생성하지 못했습니다."
            : summarySections.CommonSummary;
        var multiExecution = BuildMultiCodingResultExecution(codingRunRoot, request.Language, workerResults);
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
            _conversationStore.AppendMessage(thread.Id, "assistant", comparisonAssistantText, "coding-multi:comparison");
        }
        _conversationStore.AppendMessage(thread.Id, "assistant", assistantText, $"coding-multi:summary={aggregateProvider}:{aggregateModel}");
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
            aggregateProvider,
            aggregateModel,
            request.Language,
            string.Empty,
            multiExecution,
            workerResults,
            MergeChangedFiles(workerResults),
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
        var updatedConversation = _conversationStore.SetLatestCodingResult(
            result.ConversationId,
            BuildConversationCodingResultSnapshot(result)
        );
        return result with
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
            result.Recommendation
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

    private static string BuildOrchestrationPlannerPrompt(string input, string language, string rolePrompt)
    {
        return $"""
                아래 요청을 오케스트레이션 코딩의 기획 단계로 정리하세요.
                언어 힌트: {language}
                역할: {rolePrompt}

                목표:
                1) 구현 범위와 핵심 파일 후보 정리
                2) 먼저 만들 것, 나중에 검증할 것, 위험한 가정 정리
                3) 실행/검증/테스트에서 볼 포인트 정리

                규칙:
                - 코드 본문을 길게 쓰지 말고 실무 메모처럼 간결하게 정리
                - 구현 단계가 바로 사용할 수 있게 파일/검증 포인트를 구체적으로 적기
                - 한국어로 작성

                원본 요청:
                {input}
                """;
    }

    private static string BuildOrchestrationImplementationPrompt(
        string input,
        string language,
        string rolePrompt,
        CodingWorkerResult planningResult
    )
    {
        return $"""
                오케스트레이션 코딩의 개발 단계입니다.
                언어 힌트: {language}
                역할: {rolePrompt}

                [기획 메모]
                {planningResult.Summary}

                규칙:
                - 처음부터 끝까지 실제로 동작하는 구현을 완성
                - 필요한 파일 생성/수정, 실행, 검증, 테스트까지 직접 수행
                - 더미 구현, TODO, 미완성 보고 금지

                원본 요청:
                {input}
                """;
    }

    private static string BuildOrchestrationVerificationPrompt(
        string input,
        string language,
        string rolePrompt,
        CodingWorkerResult planningResult,
        CodingWorkerResult implementationResult
    )
    {
        return $"""
                오케스트레이션 코딩의 검증 및 테스트 단계입니다.
                언어 힌트: {language}
                역할: {rolePrompt}

                [기획 메모]
                {planningResult.Summary}

                [개발 단계 요약]
                {implementationResult.Summary}

                목표:
                1) 현재 작업 폴더 기준으로 다시 실행/검증/테스트
                2) 실패 원인이 보이면 최소 수정까지 포함해 보정 가능
                3) 남는 리스크와 확인 결과를 실제 상태 기준으로 정리

                원본 요청:
                {input}
                """;
    }

    private static string BuildOrchestrationFixPrompt(
        string input,
        string language,
        string rolePrompt,
        CodingWorkerResult planningResult,
        CodingWorkerResult implementationResult,
        CodingWorkerResult verificationResult
    )
    {
        return $"""
                오케스트레이션 코딩의 수정 단계입니다.
                언어 힌트: {language}
                역할: {rolePrompt}

                [기획 메모]
                {planningResult.Summary}

                [개발 단계 요약]
                {implementationResult.Summary}

                [검증 단계 요약]
                {verificationResult.Summary}

                목표:
                1) 검증 단계에서 남은 오류, 실패, 누락 요구사항 해결
                2) 필요한 수정 후 다시 실행/검증 마무리
                3) 최종 상태를 실제 결과 기준으로 요약

                원본 요청:
                {input}
                """;
    }

    private static bool ShouldRunOrchestrationFixStage(CodingWorkerResult verificationResult)
    {
        var status = (verificationResult.Execution.Status ?? string.Empty).Trim();
        return status.Equals("error", StringComparison.OrdinalIgnoreCase)
            || status.Equals("timeout", StringComparison.OrdinalIgnoreCase)
            || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("killed", StringComparison.OrdinalIgnoreCase);
    }

    private static CodingWorkerResult BuildSkippedCodingWorkerResult(
        string provider,
        string model,
        string language,
        string role,
        string runDirectory,
        string summary
    )
    {
        return new CodingWorkerResult(
            provider,
            model,
            language,
            string.Empty,
            summary,
            new CodeExecutionResult(
                language,
                runDirectory,
                "-",
                "(skipped)",
                0,
                string.Empty,
                string.Empty,
                "skipped"
            ),
            Array.Empty<string>(),
            role,
            summary
        );
    }

    private static string BuildOrchestrationSummary(IReadOnlyList<CodingWorkerResult> workers, CodingWorkerResult finalWorker)
    {
        var builder = new StringBuilder();
        builder.AppendLine("오케스트레이션 단계 결과");
        foreach (var worker in workers)
        {
            builder.AppendLine($"- {worker.Role}: {worker.Provider}:{worker.Model} · status={worker.Execution.Status} · exit={worker.Execution.ExitCode}");
            var trimmed = TrimForOutput(RemoveCodeBlocksFromText(worker.Summary), 260);
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                builder.AppendLine($"  {trimmed}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("최종 상태");
        builder.AppendLine($"{finalWorker.Provider}:{finalWorker.Model} · status={finalWorker.Execution.Status} · exit={finalWorker.Execution.ExitCode}");
        builder.AppendLine(TrimForOutput(RemoveCodeBlocksFromText(finalWorker.Summary), 900));
        return builder.ToString().Trim();
    }

    private static IReadOnlyList<string> MergeChangedFiles(IReadOnlyList<CodingWorkerResult> workers)
    {
        return workers
            .SelectMany(worker => worker.ChangedFiles ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildMultiCodingWorkerPrompt(string input, string language)
    {
        return $"""
                너는 다중 코딩 모드의 독립 실행 모델이다.
                언어 힌트: {language}

                규칙:
                - 다른 모델과 협의하지 않고 처음부터 끝까지 직접 완성
                - 필요한 파일 생성/수정, 실행, 검증, 테스트, 수정 반복까지 스스로 처리
                - 더미 구현, TODO, 가짜 성공 보고 금지
                - 최종적으로 실제 작업 폴더에 남은 결과만 기준으로 요약

                원본 요청:
                {input}
                """;
    }

    private static string BuildMultiCodingWorkerWorkspaceRoot(string runRoot, string provider, string model)
    {
        var providerSlug = Regex.Replace((provider ?? "worker").Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-");
        var modelSlug = Regex.Replace((model ?? "model").Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-");
        providerSlug = string.IsNullOrWhiteSpace(providerSlug) ? "worker" : providerSlug;
        modelSlug = string.IsNullOrWhiteSpace(modelSlug) ? "model" : modelSlug;
        return Path.Combine(runRoot, $"{providerSlug}-{modelSlug}");
    }

    private static CodeExecutionResult BuildMultiCodingResultExecution(
        string runRoot,
        string language,
        IReadOnlyList<CodingWorkerResult> workers
    )
    {
        var hasSuccess = workers.Any(worker => (worker.Execution.Status ?? string.Empty).Equals("ok", StringComparison.OrdinalIgnoreCase));
        var hasFailure = workers.Any(worker =>
            (worker.Execution.Status ?? string.Empty).Equals("error", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.Status ?? string.Empty).Equals("timeout", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.Status ?? string.Empty).Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || (worker.Execution.Status ?? string.Empty).Equals("killed", StringComparison.OrdinalIgnoreCase));
        var status = hasSuccess && hasFailure
            ? "mixed"
            : hasFailure
                ? "error"
                : hasSuccess
                    ? "ok"
                    : "skipped";

        return new CodeExecutionResult(
            language,
            runRoot,
            "-",
            "(worker-independent-runs)",
            hasFailure && !hasSuccess ? 1 : 0,
            string.Empty,
            string.Empty,
            status
        );
    }

    private static string BuildAutoOrchestrationCodingInput(string language)
    {
        var targetLanguage = string.IsNullOrWhiteSpace(language) ? "auto" : language;
        return $"""
                [자동 오케스트레이션 코딩 모드]
                사용자가 입력 없이 실행했습니다.
                워커 모델들이 역할을 스스로 협의해 작업을 분배하고 병렬 실행하세요.
                목표:
                1) 워크스페이스를 점검하고 실행 가능한 개선 작업 1건 수행
                2) 변경 파일 생성/수정
                3) 실행/검증 후 결과 요약
                언어 힌트: {targetLanguage}
                """;
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
