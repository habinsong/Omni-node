using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private string ApplySkillCreateDirective(string text, string source)
    {
        var result = SkillCreateDirective.Apply(text);
        if (result.CreatedCount > 0 || result.FailedCount > 0)
        {
            _auditLogger.Log(
                source,
                "skill_create_directive",
                result.FailedCount == 0 ? "ok" : "partial",
                $"created={result.CreatedCount} failed={result.FailedCount}"
            );
        }
        return result.FinalText;
    }

    public Task<ConversationChatResult> ChatSingleWithStateAsync(
        ChatRequest request,
        CancellationToken cancellationToken,
        Action<ChatStreamUpdate>? streamCallback = null
    )
    {
        return ChatSingleWithStateCoreAsync(request, cancellationToken, streamCallback);
    }

    public Task<ConversationChatResult> ChatOrchestrationWithStateAsync(
        ChatRequest request,
        CancellationToken cancellationToken
    )
    {
        return ChatOrchestrationWithStateCoreAsync(request, cancellationToken);
    }

    public Task<ConversationMultiResult> ChatMultiWithStateAsync(
        MultiChatRequest request,
        CancellationToken cancellationToken
    )
    {
        return ChatMultiWithStateCoreAsync(request, cancellationToken);
    }

    public Task<LlmSingleChatResult> ChatSingleAsync(
        string input,
        string provider,
        string? model,
        string source,
        CancellationToken cancellationToken,
        int? maxOutputTokens = null,
        Action<string>? streamCallback = null
    )
    {
        return ChatSingleCoreAsync(input, provider, model, source, cancellationToken, maxOutputTokens, streamCallback);
    }

    public Task<LlmOrchestrationResult> ChatOrchestrationAsync(
        string input,
        string source,
        string? provider,
        string? model,
        string? groqModel,
        string? geminiModel,
        string? copilotModel,
        string? cerebrasModel,
        string? codexModel,
        string? nvidiaModel,
        IReadOnlyList<InputAttachment>? attachments,
        CancellationToken cancellationToken
    )
    {
        return ChatOrchestrationCoreAsync(
            input,
            source,
            provider,
            model,
            groqModel,
            geminiModel,
            copilotModel,
            cerebrasModel,
            codexModel,
            nvidiaModel,
            attachments,
            cancellationToken
        );
    }

    public Task<LlmMultiChatResult> ChatMultiAsync(
        string input,
        string source,
        string? groqModel,
        string? geminiModel,
        string? copilotModel,
        string? cerebrasModel,
        string? summaryProvider,
        string? codexModel,
        string? nvidiaModel,
        IReadOnlyList<InputAttachment>? attachments,
        CancellationToken cancellationToken
    )
    {
        return ChatMultiCoreAsync(
            input,
            source,
            groqModel,
            geminiModel,
            copilotModel,
            cerebrasModel,
            summaryProvider,
            codexModel,
            nvidiaModel,
            attachments,
            cancellationToken
        );
    }

    private async Task<ConversationChatResult> ChatSingleWithStateCoreAsync(
        ChatRequest request,
        CancellationToken cancellationToken,
        Action<ChatStreamUpdate>? streamCallback = null
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
        var localAssistantInfoReply = TryBuildLocalAssistantInfoResponse(rawInput, session.SessionId, request.Source);
        if (!string.IsNullOrWhiteSpace(localAssistantInfoReply))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "local:assistant_info");
            _conversationStore.AppendMessage(thread.Id, "assistant", localAssistantInfoReply, "local:assistant_info");
            ScheduleConversationMaintenance(
                thread.Id,
                "chat-single",
                "local",
                "assistant_info"
            );

            var localInfoUpdated = _conversationStore.Get(thread.Id) ?? thread;
            return new ConversationChatResult(
                "single",
                localInfoUpdated.Id,
                "local",
                "assistant_info",
                localAssistantInfoReply,
                "local:assistant_info",
                localInfoUpdated,
                null,
                null,
                RequestId: request.RequestId
            );
        }

        var localUsageReply = await TryBuildInChatCopilotUsageResponseAsync(rawInput, request.Source, cancellationToken);
        if (!string.IsNullOrWhiteSpace(localUsageReply))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "local:copilot_usage");
            _conversationStore.AppendMessage(thread.Id, "assistant", localUsageReply, "local:copilot_usage");
            ScheduleConversationMaintenance(
                thread.Id,
                "chat-single",
                "local",
                "copilot_usage"
            );

            var localUpdated = _conversationStore.Get(thread.Id) ?? thread;
            return new ConversationChatResult(
                "single",
                localUpdated.Id,
                "local",
                "copilot_usage",
                localUsageReply,
                "local:copilot_usage",
                localUpdated,
                null,
                null,
                RequestId: request.RequestId
            );
        }

        var requestedProvider = NormalizeProvider(request.Provider, allowAuto: true);
        if (requestedProvider == "auto")
        {
            requestedProvider = await ResolveCategoryProviderAsync(
                TaskCategory.GeneralChat,
                requestedProvider,
                null,
                cancellationToken,
                "chat_single"
            );
            if (requestedProvider == "none")
            {
                requestedProvider = "groq";
            }
        }
        var resolvedModel = ResolveModelForCategory(TaskCategory.GeneralChat, requestedProvider, request.Model);
        var resolvedWebUrls = ResolveWebUrls(rawInput, request.WebUrls, request.WebSearchEnabled);
        if (resolvedWebUrls.Count > 0 && _llmRouter.HasGeminiApiKey())
        {
            var memoryHint = BuildSafeWebMemoryPreferenceHint(
                session.SessionId,
                rawInput,
                session.LinkedMemoryNotes
            );
            var urlResult = await GenerateGeminiUrlContextAnswerDetailedAsync(
                rawInput,
                resolvedWebUrls,
                memoryHint,
                allowMarkdownTable: true,
                enforceTelegramOutputStyle: false,
                streamCallback,
                session.Scope,
                session.Mode,
                thread.Id,
                "heuristic_url_context",
                0,
                cancellationToken
            );
            var urlText = urlResult.Response.Text;
            var assistantMeta = "gemini-url-single";
            var urlCitationBundle = BuildAndLogCitationMappings(
                request.Source,
                "chat-single-url-context",
                urlResult.Citations,
                ("text", urlText)
            );
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"{requestedProvider}:{request.Model ?? "-"}");
            _conversationStore.AppendMessage(thread.Id, "assistant", urlText, assistantMeta);
            ScheduleConversationMaintenance(
                thread.Id,
                $"{session.Scope}-{session.Mode}",
                urlResult.Response.Provider,
                urlResult.Response.Model
            );

            var updatedUrl = _conversationStore.Get(thread.Id) ?? thread;
            return new ConversationChatResult(
                "single",
                updatedUrl.Id,
                urlResult.Response.Provider,
                urlResult.Response.Model,
                urlText,
                assistantMeta,
                updatedUrl,
                null,
                null,
                urlResult.Citations,
                urlCitationBundle.Mappings,
                urlCitationBundle.Validation,
                0,
                0,
                "-",
                urlResult.Latency,
                request.RequestId
            );
        }

        // Think+ 모드면 fast-web 단독 라우팅 우회. 기본 LLM이 web context를 prepend 받아 직접 답변하도록.
        if (request.WebSearchEnabled && !request.ThinkPlusEnabled)
        {
            var webLookupInput = ResolveContextualWebLookupInput(thread.Id, rawInput);
            var decisionStopwatch = Stopwatch.StartNew();
            var decisionPath = "llm";
            var shouldUseGeminiWeb = false;
            var selfDecideNeedWeb = false;

            if (LooksLikeExplicitWebLookupQuestion(webLookupInput))
            {
                decisionPath = "heuristic_explicit_web";
                shouldUseGeminiWeb = true;
            }
            else if (LooksLikeRealtimeQuestion(webLookupInput))
            {
                decisionPath = "heuristic_web";
                shouldUseGeminiWeb = true;
            }
            else if (LooksLikeClearlyNonWebQuestion(webLookupInput))
            {
                decisionPath = "heuristic_no_web";
            }
            else
            {
                var webDecision = await DecideNeedWebBySelectedProviderAsync(
                    webLookupInput,
                    requestedProvider,
                    resolvedModel,
                    cancellationToken
                );
                var shouldFallbackToGeminiWeb = !webDecision.DecisionSucceeded && LooksLikeRealtimeQuestion(webLookupInput);
                shouldUseGeminiWeb = webDecision.NeedWeb || shouldFallbackToGeminiWeb;
                selfDecideNeedWeb = shouldFallbackToGeminiWeb;
            }

            var decisionMs = Math.Max(0L, decisionStopwatch.ElapsedMilliseconds);
            if (shouldUseGeminiWeb)
            {
                var memoryHint = BuildSafeWebMemoryPreferenceHint(
                    session.SessionId,
                    webLookupInput,
                    session.LinkedMemoryNotes
                );
                var webResult = await ComposeGroundedWebAnswerWithFallbackAsync(
                    webLookupInput,
                    memoryHint,
                    selfDecideNeedWeb,
                    allowMarkdownTable: true,
                    enforceTelegramOutputStyle: false,
                    streamCallback,
                    session.Scope,
                    session.Mode,
                    thread.Id,
                    decisionPath,
                    decisionMs,
                    request.Source,
                    cancellationToken
                );
                var webText = webResult.Response.Text;
                var assistantMeta = webResult.Route;
                var webCitationBundle = BuildAndLogCitationMappings(
                    request.Source,
                    assistantMeta,
                    webResult.Citations,
                    ("text", webText)
                );
                _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"{requestedProvider}:{request.Model ?? "-"}");
                _conversationStore.AppendMessage(thread.Id, "assistant", webText, assistantMeta);
                ScheduleConversationMaintenance(
                    thread.Id,
                    $"{session.Scope}-{session.Mode}",
                    webResult.Response.Provider,
                    webResult.Response.Model
                );

                var updatedWeb = _conversationStore.Get(thread.Id) ?? thread;
                return new ConversationChatResult(
                    "single",
                    updatedWeb.Id,
                    webResult.Response.Provider,
                    webResult.Response.Model,
                    webText,
                    assistantMeta,
                    updatedWeb,
                    null,
                    webResult.GuardFailure,
                    webResult.Citations,
                    webCitationBundle.Mappings,
                    webCitationBundle.Validation,
                    0,
                    0,
                    "-",
                    webResult.Latency,
                    request.RequestId
                );
            }
        }

        // Provider별로 응답 특성이 달라 일률적인 17초 타임아웃은 부적절하다.
        // - Copilot: 자체 흐름 → 외부 timeout 없음
        // - NVIDIA NIM: 큐잉/콜드스타트로 첫 청크까지 수십 초가 걸릴 수 있음 → 설정값 (기본 180s)
        // - Cerebras: 빠르지만 free-tier 시 변동 → 설정값 (기본 20s)
        // - 그 외(groq/gemini/codex 등): 빠른 제공자 → 17초 유지
        TimeSpan? singleRequestTimeout = requestedProvider.ToLowerInvariant() switch
        {
            "copilot" => null,
            "nvidia" => TimeSpan.FromSeconds(Math.Max(_config.NvidiaMinSingleChatTimeoutSec, _config.NvidiaTimeoutSec)),
            "cerebras" => TimeSpan.FromSeconds(Math.Max(_config.CerebrasMinSingleChatTimeoutSec, _config.CerebrasTimeoutSec)),
            _ => TimeSpan.FromSeconds(_config.SingleChatDefaultTimeoutSec),
        };
        using var singleRequestCts = singleRequestTimeout == null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var singleRequestToken = cancellationToken;
        if (singleRequestCts != null && singleRequestTimeout != null)
        {
            singleRequestCts.CancelAfter(singleRequestTimeout.Value);
            singleRequestToken = singleRequestCts.Token;
        }

        var preparedInput = await PrepareInputForProviderAsync(
            rawInput,
            requestedProvider,
            resolvedModel,
            request.Attachments,
            request.WebUrls,
            false,
            true,
            singleRequestToken,
            request.Source,
            session.SessionKey,
            session.SessionId
        );
        if (!string.IsNullOrWhiteSpace(preparedInput.UnsupportedMessage))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"{requestedProvider}:{request.Model ?? "-"}");
            _conversationStore.AppendMessage(thread.Id, "assistant", preparedInput.UnsupportedMessage, $"{requestedProvider}:{resolvedModel}");
            var blockedView = _conversationStore.Get(thread.Id) ?? thread;
            var blockedCitationBundle = BuildAndLogCitationMappings(
                request.Source,
                "chat-single-unsupported",
                preparedInput.Citations,
                ("text", preparedInput.UnsupportedMessage)
            );
            return new ConversationChatResult(
                "single",
                blockedView.Id,
                requestedProvider,
                resolvedModel,
                preparedInput.UnsupportedMessage,
                string.Empty,
                blockedView,
                null,
                preparedInput.GuardFailure,
                preparedInput.Citations,
                blockedCitationBundle.Mappings,
                blockedCitationBundle.Validation,
                preparedInput.RetryAttempt,
                preparedInput.RetryMaxAttempts,
                preparedInput.RetryStopReason,
                RequestId: request.RequestId
            );
        }

        var singleMaxOutputTokens = ResolveSingleChatMaxOutputTokens(rawInput);
        var effectiveSingleToken = singleRequestToken;
        var singleGenerationProvider = requestedProvider;
        var singleGenerationModel = resolvedModel;
        var streamedChunkIndex = 0;
        Action<string>? singleStreamCallback = streamCallback == null
            ? null
            : delta =>
            {
                if (string.IsNullOrEmpty(delta))
                {
                    return;
                }

                streamedChunkIndex += 1;
                streamCallback(new ChatStreamUpdate(
                    session.Scope,
                    session.Mode,
                    thread.Id,
                    singleGenerationProvider,
                    singleGenerationModel,
                    "chat-single",
                    delta,
                    streamedChunkIndex,
                    request.RequestId
                ));
            };

        async Task<LlmSingleChatResult> GenerateSingleAsync(string prompt, CancellationToken token)
        {
            return singleGenerationProvider == "groq"
                ? await ExecuteGroqSingleChainAsync(
                    prompt,
                    singleGenerationModel,
                    token,
                    singleMaxOutputTokens,
                    singleStreamCallback
                )
                : await ChatSingleAsync(
                    prompt,
                    singleGenerationProvider,
                    singleGenerationModel,
                    request.Source,
                    token,
                    singleMaxOutputTokens,
                    singleStreamCallback
                );
        }

        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, rawInput);
        var skillPreparedText = ApplySelectedSkillToPrompt(
            preparedInput.Text,
            requestedSkillName,
            request.SkillScope
        );
        // Think+ 모드 prepend (chat-single 단독 모드만)
        if (request.ThinkPlusEnabled && request.Mode == "single")
        {
            var effectiveSkillForThinkPlus = ResolveEffectiveSkillNameForThread(requestedSkillName, session.SessionId);
            var thinkPlusContext = await BuildThinkPlusContextAsync(
                rawInput,
                request.Source,
                effectiveSingleToken,
                effectiveSkillForThinkPlus
            ).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(thinkPlusContext))
            {
                skillPreparedText = thinkPlusContext + skillPreparedText;
            }
        }
        var contextualInput = BuildContextualInput(
            session.SessionId,
            skillPreparedText,
            session.LinkedMemoryNotes,
            includeLocalTimeHint: true
        );
        LlmSingleChatResult generated;
        try
        {
            generated = await GenerateSingleAsync(contextualInput, effectiveSingleToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            generated = new LlmSingleChatResult(
                requestedProvider,
                resolvedModel,
                $"{requestedProvider} 응답 시간이 초과되었습니다."
            );
        }

        if (ShouldRetrySingleChatWithoutHistory(rawInput, generated.Text))
        {
            var historyBypassInput = BuildHistoryBypassInput(preparedInput.Text);
            var recovered = await GenerateSingleAsync(historyBypassInput, effectiveSingleToken);
            if (!string.IsNullOrWhiteSpace(recovered.Text))
            {
                var recoveredStillDrift = ShouldRetrySingleChatWithoutHistory(rawInput, recovered.Text);
                _auditLogger.Log(
                    request.Source,
                    "chat_single_history_recovery",
                    recoveredStillDrift ? "skip" : "ok",
                    $"provider={requestedProvider} model={resolvedModel} recoveredStillDrift={(recoveredStillDrift ? "true" : "false")}"
                );
                if (!recoveredStillDrift)
                {
                    generated = recovered;
                }
                else
                {
                    var originalRequestInput = BuildOriginalRequestRetryInput(rawInput);
                    var originalRecovered = await GenerateSingleAsync(originalRequestInput, effectiveSingleToken);
                    if (!string.IsNullOrWhiteSpace(originalRecovered.Text)
                        && !ShouldRetrySingleChatWithoutHistory(rawInput, originalRecovered.Text))
                    {
                        _auditLogger.Log(
                            request.Source,
                            "chat_single_history_recovery",
                            "ok",
                            $"provider={requestedProvider} model={resolvedModel} recoveredStage=original_request"
                        );
                        generated = originalRecovered;
                    }
                    else
                    {
                        generated = new LlmSingleChatResult(
                            generated.Provider,
                            generated.Model,
                            BuildOffTopicGuardMessage(rawInput)
                        );
                    }
                }
            }
            else
            {
                _auditLogger.Log(
                    request.Source,
                    "chat_single_history_recovery",
                    "skip",
                    $"provider={requestedProvider} model={resolvedModel} empty_recovered_text=true"
                );
                var originalRequestInput = BuildOriginalRequestRetryInput(rawInput);
                var originalRecovered = await GenerateSingleAsync(originalRequestInput, effectiveSingleToken);
                if (!string.IsNullOrWhiteSpace(originalRecovered.Text)
                    && !ShouldRetrySingleChatWithoutHistory(rawInput, originalRecovered.Text))
                {
                    _auditLogger.Log(
                        request.Source,
                        "chat_single_history_recovery",
                        "ok",
                        $"provider={requestedProvider} model={resolvedModel} recoveredStage=original_request"
                    );
                    generated = originalRecovered;
                }
                else
                {
                    generated = new LlmSingleChatResult(
                        generated.Provider,
                        generated.Model,
                        BuildOffTopicGuardMessage(rawInput)
                    );
                }
            }
        }

        var citationBundle = BuildAndLogCitationMappings(
            request.Source,
            "chat-single",
            preparedInput.Citations,
            ("text", generated.Text)
        );
        var effectiveGuardFailure = preparedInput.GuardFailure;
        var responseText = ApplyListCountFallback(rawInput, generated.Text, preparedInput.Citations);
        responseText = ApplySkillCreateDirective(responseText, request.Source);
        responseText = CleanLeakedSystemMarkers(responseText);

        _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"{requestedProvider}:{request.Model ?? "-"}");
        _conversationStore.AppendMessage(thread.Id, "assistant", responseText, $"{generated.Provider}:{generated.Model}");
        ScheduleConversationMaintenance(
            thread.Id,
            $"{session.Scope}-{session.Mode}",
            generated.Provider,
            generated.Model
        );

        var updated = _conversationStore.Get(thread.Id) ?? thread;
        return new ConversationChatResult(
            "single",
            updated.Id,
            generated.Provider,
            generated.Model,
            responseText,
            string.Empty,
            updated,
            null,
            effectiveGuardFailure,
            preparedInput.Citations,
            citationBundle.Mappings,
            citationBundle.Validation,
            preparedInput.RetryAttempt,
            preparedInput.RetryMaxAttempts,
            preparedInput.RetryStopReason,
            RequestId: request.RequestId
        );
    }

    private static bool ShouldRetrySingleChatWithoutHistory(string input, string output)
    {
        var normalizedInput = (input ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedOutput = (output ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedInput.Length == 0 || normalizedOutput.Length == 0)
        {
            return false;
        }

        var isNewsRequest = ContainsAny(normalizedInput, "뉴스", "news", "헤드라인", "속보", "브리핑");
        if (isNewsRequest)
        {
            return false;
        }

        var looksLikeNewsAnswer = ContainsAny(
            normalizedOutput,
            "요청하신 소식",
            "주요 뉴스",
            "뉴스 10건",
            "뉴스 5건",
            "오늘 주요 뉴스",
            "no.1 제목"
        );
        if (!looksLikeNewsAnswer)
        {
            return LooksLikeOffTopicModelExplanation(normalizedInput, normalizedOutput)
                || LooksLikeUnrequestedP2SAnswer(normalizedInput, normalizedOutput);
        }

        var asksLlmPricing = ContainsAny(
            normalizedInput,
            "llm",
            "large language model",
            "언어 모델",
            "컨텍스트",
            "context window",
            "토큰",
            "api",
            "비용",
            "가격",
            "요금"
        );
        var hasLlmPricingSignalsInOutput = ContainsAny(
            normalizedOutput,
            "llm",
            "언어 모델",
            "컨텍스트",
            "context window",
            "토큰",
            "api",
            "비용",
            "가격",
            "요금"
        );
        if (asksLlmPricing && !hasLlmPricingSignalsInOutput)
        {
            return true;
        }

        return true;
    }

    private static bool LooksLikeOffTopicModelExplanation(string normalizedInput, string normalizedOutput)
    {
        if (!ContainsAny(normalizedInput, "gpt-oss", "gpt oss", "gpt_oss", "gptoss"))
        {
            return false;
        }

        return !ContainsAny(
            normalizedOutput,
            "gpt-oss",
            "gpt oss",
            "openai",
            "llm",
            "언어 모델",
            "오픈 웨이트",
            "open weight",
            "moe",
            "mixture-of-experts",
            "120b");
    }

    private static bool LooksLikeUnrequestedP2SAnswer(string normalizedInput, string normalizedOutput)
    {
        if (ContainsAny(normalizedInput, "p2s", "print-to-shape", "3d 프린팅", "3d printing"))
        {
            return false;
        }

        return ContainsAny(normalizedOutput, "p2s", "print-to-shape", "3d 프린팅", "3d printing");
    }

    private static string BuildOffTopicGuardMessage(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "모델 응답이 요청과 맞지 않아 답변을 중단했습니다. 다시 질문해 주세요."
            : $"모델 응답이 새 요청과 맞지 않아 답변을 중단했습니다. 원문 요청: {normalized}";
    }

    private string ResolveContextualWebLookupInput(string conversationId, string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (!LooksLikeVagueWebLookupRequest(normalized))
        {
            return normalized;
        }

        var previousUser = _conversationStore.Get(conversationId)?.Messages
            .OrderByDescending(item => item.CreatedUtc)
            .FirstOrDefault(item =>
                item.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                && !item.Text.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?.Text
            .Trim();
        if (string.IsNullOrWhiteSpace(previousUser))
        {
            return normalized;
        }

        return $"{previousUser}\n\n추가 요청: {normalized}";
    }

    private static bool LooksLikeVagueWebLookupRequest(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || normalized.Length > 40)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "웹검색해서 찾아",
            "웹 검색해서 찾아",
            "웹검색해",
            "웹 검색해",
            "검색해서 찾아",
            "찾아봐",
            "찾아 줘",
            "찾아줘",
            "검색해봐",
            "검색해 줘",
            "검색해줘",
            "look it up",
            "search it");
    }

    private static int ResolveSingleChatMaxOutputTokens(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return 4096;
        }

        if (ContainsAny(
                normalized,
                "자세",
                "상세",
                "깊게",
                "설명",
                "가이드",
                "분석",
                "원리",
                "기술",
                "비전공자",
                "non-expert",
                "explain"))
        {
            return 4096;
        }

        if (LooksLikeListOutputRequest(normalized))
        {
            return 3072;
        }

        if (ContainsAny(normalized, "요약", "정리", "summary", "compare", "비교"))
        {
            return 2048;
        }

        return 3072;
    }

    private static string BuildHistoryBypassInput(string preparedInput)
    {
        var normalized = (preparedInput ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return $"""
                [중요]
                아래 새 요청을 최우선으로 처리하세요.
                이전 대화의 형식을 관성으로 따라가지 말고, 새 요청 주제에만 답변하세요.

                [새 요청]
                {normalized}
                """;
    }

    private static string BuildOriginalRequestRetryInput(string rawInput)
    {
        var normalized = (rawInput ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return $"""
                [중요]
                아래 원문 요청만 다시 처리하세요.
                이전 대화의 형식을 관성으로 따라가지 말고, 새 요청 주제에만 답변하세요.

                [원문 요청]
                {normalized}
                """;
    }

    // 이번 턴에 실제로 적용될 스킬 이름. UI/인라인 지정이 있으면 그것, 아니면 thread sticky.
    // Think+ 컨텍스트가 활성 스킬 톤을 인지하도록 활용된다.
    private string? ResolveEffectiveSkillNameForThread(string? requestedSkillName, string? threadKey)
    {
        if (!string.IsNullOrWhiteSpace(requestedSkillName))
        {
            return requestedSkillName.Trim();
        }

        if (string.IsNullOrWhiteSpace(threadKey))
        {
            return null;
        }

        return _activeSkillByThread.TryGetValue(threadKey, out var name)
               && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    // 단어 경계 기반으로 한 스킬 이름이 텍스트 안에 등장하는 첫 인덱스를 찾는다. 매칭은 영문/숫자/_/- 외 문자(공백·구두점·CJK)에 둘러싸여야 한다.
    // 짧은 이름(예: "ai")이 일반 영단어("aim")의 부분 문자열로 매칭되는 false-positive를 막는다.
    private static int IndexOfSkillNameWithBoundary(string text, string skillName)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(skillName))
        {
            return -1;
        }

        var startSearch = 0;
        while (startSearch <= text.Length - skillName.Length)
        {
            var idx = text.IndexOf(skillName, startSearch, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return -1;
            }

            var leftOk = idx == 0 || !IsSkillNameBoundaryInside(text[idx - 1]);
            var rightIdx = idx + skillName.Length;
            var rightOk = rightIdx >= text.Length || !IsSkillNameBoundaryInside(text[rightIdx]);
            if (leftOk && rightOk)
            {
                return idx;
            }

            startSearch = idx + 1;
        }

        return -1;
    }

    private static bool IsSkillNameBoundaryInside(char c)
    {
        // 스킬 이름 토큰 내부 문자로 간주하는 집합. ASCII 영숫자, '-', '_'.
        if (c == '-' || c == '_')
        {
            return true;
        }

        return (c >= 'a' && c <= 'z')
               || (c >= 'A' && c <= 'Z')
               || (c >= '0' && c <= '9');
    }

    // 프롬프트 안에서 언급된 모든 스킬을 길이순으로 탐지.
    // 매칭된 부분은 마스킹해 한 스킬 이름이 다른 스킬 이름의 부분 문자열일 때 중복 카운트되지 않게 한다.
    // 단어 경계 검사로 짧은 스킬 이름이 일반 텍스트에 묻어 들어가는 false-positive를 차단.
    private List<SkillManifest> DetectMentionedSkillsInPrompt(string input)
    {
        var result = new List<SkillManifest>();
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return result;
        }

        try
        {
            var snapshot = _projectContextLoader.LoadSnapshot();
            var working = new StringBuilder(trimmed);
            foreach (var skill in snapshot.Skills.OrderByDescending(s => s.Name.Length))
            {
                if (string.IsNullOrWhiteSpace(skill.Name))
                {
                    continue;
                }

                var idx = IndexOfSkillNameWithBoundary(working.ToString(), skill.Name);
                if (idx < 0)
                {
                    continue;
                }

                result.Add(skill);
                for (var i = 0; i < skill.Name.Length; i++)
                {
                    working[idx + i] = '\0';
                }
            }
        }
        catch (Exception ex)
        {
            _auditLogger.Log("local", "skill_detect_mentions", "failed", ex.Message);
        }

        return result;
    }

    // 한 스킬만 매칭됐을 때 그 스킬을 반환. 0개면 null, 2개 이상이면 거부 케이스이므로 null. 단어 경계 검사 사용.
    private SkillManifest? FindSingleMentionedSkillInPrompt(string input)
    {
        var mentioned = DetectMentionedSkillsInPrompt(input);
        return mentioned.Count == 1 ? mentioned[0] : null;
    }

    // 프롬프트 안에서 스킬 이름이 명시되면 그 스킬을, 그렇지 않으면 UI 드롭다운 선택을 사용.
    // 다중 스킬은 상위 흐름에서 이미 거부되므로 여기선 단일 매칭만 처리한다.
    // UI 선택과 프롬프트 명시가 다르면 프롬프트가 우선 — 사용자가 직접 입력한 의도가 더 명시적.
    private string? ResolvePromptOrUiSkillName(string? uiSkillName, string rawInput)
    {
        var inlineByActivation = TryExtractInlineSkillName(rawInput);
        if (!string.IsNullOrWhiteSpace(inlineByActivation))
        {
            return inlineByActivation.Trim();
        }
        var inlineMention = FindSingleMentionedSkillInPrompt(rawInput);
        if (inlineMention != null)
        {
            return inlineMention.Name;
        }
        return string.IsNullOrWhiteSpace(uiSkillName) ? null : uiSkillName.Trim();
    }

    // 프롬프트에 두 개 이상의 스킬 이름이 들어 있으면 거부 메시지 반환. 한 번에 한 스킬만 허용.
    private string? TryBuildMultiSkillRejectionResponse(string input)
    {
        var mentioned = DetectMentionedSkillsInPrompt(input);
        if (mentioned.Count < 2)
        {
            return null;
        }

        var names = string.Join(", ", mentioned.Select(s => $"`{s.Name}`"));
        return $"""
                한 번에 한 개의 스킬만 사용할 수 있어요.
                지금 입력에서 스킬 이름이 {mentioned.Count}개 발견됐습니다: {names}
                사용할 스킬 하나만 남기고 다시 보내 주세요.
                """;
    }

    private string? TryBuildLocalAssistantInfoResponse(
        string input,
        string? threadKey = null,
        string source = "local"
    )
    {
        // 다중 스킬 언급은 가장 우선순위로 거부. 활성화/인벤토리 검사 모두 건너뛴다.
        var multiSkillRejection = TryBuildMultiSkillRejectionResponse(input ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(multiSkillRejection))
        {
            _auditLogger.Log(source, "skill_multi_mention", "blocked", "");
            return multiSkillRejection;
        }

        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || normalized.Length > 180)
        {
            return null;
        }

        var compact = Regex.Replace(normalized, @"[\p{P}\p{S}\s]+", string.Empty);

        // 스킬 활성화/비활성화 의도를 먼저 처리한다. "X 스킬을 사용해서 Y 해줘" 같이
        // 명시적으로 특정 스킬을 호출하는 입력이 인벤토리/식별 응답으로 잘못 라우팅되는 것을 막는다.
        var skillDeactivationReply = TryBuildLocalSkillDeactivationResponse(normalized, threadKey, source);
        if (!string.IsNullOrWhiteSpace(skillDeactivationReply))
        {
            return skillDeactivationReply;
        }

        var skillActivationReply = TryBuildLocalSkillActivationResponse(normalized, compact, threadKey, source);
        if (!string.IsNullOrWhiteSpace(skillActivationReply))
        {
            return skillActivationReply;
        }

        var hasActiveSkill = !string.IsNullOrWhiteSpace(threadKey)
            && _activeSkillByThread.ContainsKey(threadKey);
        if (hasActiveSkill)
        {
            return null;
        }

        if (LooksLikeLocalSkillInventoryQuestion(normalized, compact))
        {
            return BuildLocalSkillInventoryResponse();
        }

        if (LooksLikeLocalLimitationQuestion(normalized, compact))
        {
            return BuildLocalLimitationResponse();
        }

        if (LooksLikeLocalCapabilityQuestion(normalized, compact))
        {
            return BuildLocalCapabilityResponse();
        }

        if (LooksLikeLocalIdentityQuestion(normalized, compact))
        {
            return BuildLocalIdentityResponse();
        }

        return null;
    }

    private string? TryBuildLocalSkillActivationResponse(
        string normalized,
        string compact,
        string? threadKey,
        string source
    )
    {
        var snapshot = TryLoadProjectContextSnapshotForLocalInfo();
        if (snapshot == null || snapshot.Skills.Count == 0)
        {
            return null;
        }

        var asksToUseSkill = ContainsAny(
                               normalized,
                               "사용해",
                               "사용해줘",
                               "써",
                               "써줘",
                               "적용해",
                               "켜",
                               "켜줘",
                               "활성화",
                               "activate",
                               "use")
                           || compact.Contains("사용해", StringComparison.Ordinal)
                           || compact.Contains("사용해줘", StringComparison.Ordinal)
                           || compact.Contains("활성화", StringComparison.Ordinal)
                           || compact.Contains("activate", StringComparison.Ordinal)
                           || compact.Contains("use", StringComparison.Ordinal);
        if (!asksToUseSkill)
        {
            return null;
        }

        // 단어 경계 검사 helper 사용. 다중 스킬은 상위에서 거부됨.
        var matchedSkill = DetectMentionedSkillsInPrompt(normalized).FirstOrDefault();
        if (matchedSkill == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(threadKey))
        {
            // 활성 스킬 교체 시 이전 이름과 함께 audit log.
            _activeSkillByThread.TryGetValue(threadKey, out var previousSkill);
            _activeSkillByThread[threadKey] = matchedSkill.Name;
            PersistActiveSkillForThread(threadKey, matchedSkill.Name);
            var auditDetail = string.IsNullOrWhiteSpace(previousSkill)
                              || string.Equals(previousSkill, matchedSkill.Name, StringComparison.OrdinalIgnoreCase)
                ? $"name={matchedSkill.Name} scope={matchedSkill.Scope}"
                : $"name={matchedSkill.Name} scope={matchedSkill.Scope} replaced={previousSkill}";
            _auditLogger.Log(source, "skill_activate", "ok", auditDetail);
        }

        // 사용자가 같은 메시지에 task까지 같이 줬으면 안내문 대신 바로 LLM에 넘긴다.
        // 예: "X 스킬 사용해서 Y에 대해 설명해줘"
        var hasInlineTask = Regex.IsMatch(
                               normalized,
                               @"(?i)(사용해서|써서|적용해서|활용해서|이용해서|가지고|using\s+|use\s+\S+\s+to\s+)"
                           )
                           || ContainsAny(
                               normalized,
                               "설명",
                               "알려",
                               "말해",
                               "정리",
                               "분석",
                               "비교",
                               "원리",
                               "방법",
                               "이유",
                               "어떻게",
                               "왜",
                               "도와",
                               "해줘",
                               "해 줘",
                               "explain",
                               "describe",
                               "tell",
                               "summarize",
                               "compare",
                               "analyze");
        if (hasInlineTask)
        {
            return null;
        }

        return $"""
                `{matchedSkill.Name}` 스킬을 이 대화에 적용했습니다.
                다음 메시지부터 이 스킬 지침을 계속 우선 적용합니다.

                - 스킬: {matchedSkill.Name}
                - 설명: {TrimLocalAssistantInfoText(matchedSkill.Description, 120)}
                - 해제: "스킬 해제" 또는 "스킬 중지"
                """;
    }

    private string? TryBuildLocalSkillDeactivationResponse(
        string normalized,
        string? threadKey,
        string source
    )
    {
        if (!LooksLikeSkillDeactivationRequest(normalized))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(threadKey))
        {
            return "현재 대화에 활성화된 스킬이 없습니다.";
        }

        if (_activeSkillByThread.TryRemove(threadKey, out var skillName))
        {
            PersistActiveSkillForThread(threadKey, null);
            _auditLogger.Log(source, "skill_deactivate", "ok", $"name={skillName}");
            return $"`{skillName}` 스킬을 해제했습니다.";
        }

        return "현재 대화에 활성화된 스킬이 없습니다.";
    }

    private static bool LooksLikeLocalSkillInventoryQuestion(string normalized, string compact)
    {
        var hasSkillKeyword = ContainsAny(normalized, "스킬", "skill", "skills", "skill.md")
                              || compact.Contains("스킬", StringComparison.Ordinal)
                              || compact.Contains("skill", StringComparison.Ordinal);
        if (!hasSkillKeyword)
        {
            return false;
        }

        // 인벤토리 질문은 "스킬"과 의문/목록 표지어가 가까이 붙어 있어야 한다.
        // 예) "어떤 스킬 있어?", "스킬 목록 보여줘", "스킬이 뭐 있어"
        // "eli5 스킬을 사용해서 디지털 카메라 원리 말해줘" 처럼 다른 단어로 분리되면 인벤토리가 아니다.
        var inventoryMarker = @"(뭐|무엇|무슨|어떤|어떠한|어떻|종류|목록|리스트|보여|알려|available|list)";
        var skillToken = @"(스킬|skill|skills|skill\.md)";
        var particle = @"(을|를|이|가|은|는|에|의|에는|들|들이|들은|들을)?";

        if (Regex.IsMatch(normalized, $@"(?i){skillToken}\s*{particle}\s*{inventoryMarker}"))
        {
            return true;
        }
        if (Regex.IsMatch(normalized, $@"(?i){inventoryMarker}\s*{skillToken}"))
        {
            return true;
        }
        if (Regex.IsMatch(normalized, $@"(?i){skillToken}\s*{particle}\s*(있|가지고|사용\s*가능)"))
        {
            return true;
        }

        // 띄어쓰기가 없는 합성형 (e.g. "스킬뭐있어", "어떤스킬")
        var compactPatterns = new[]
        {
            "스킬뭐", "스킬어떤", "스킬어떠", "스킬어떻", "스킬무슨", "스킬무엇",
            "스킬목록", "스킬리스트", "스킬종류", "스킬보여", "스킬알려",
            "스킬있", "스킬가지고", "스킬사용가능",
            "어떤스킬", "어떠한스킬", "어떻스킬", "무슨스킬", "무엇스킬",
            "skill뭐", "skill어떤", "skill목록", "skill리스트",
        };
        foreach (var pattern in compactPatterns)
        {
            if (compact.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeLocalLimitationQuestion(string normalized, string compact)
    {
        var asksAssistant = ContainsAny(
            normalized,
            "너",
            "넌",
            "네가",
            "니가",
            "당신",
            "어시스턴트",
            "ai",
            "봇",
            "omni",
            "옴니");
        if (!asksAssistant)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "할 수 없는",
            "할수 없는",
            "못 하는",
            "못하는",
            "못 해",
            "못해",
            "한계",
            "제한",
            "limitations",
            "cannot")
            || compact.Contains("할수없는", StringComparison.Ordinal)
            || compact.Contains("할수없", StringComparison.Ordinal);
    }

    private static bool LooksLikeLocalCapabilityQuestion(string normalized, string compact)
    {
        var asksAssistant = ContainsAny(
            normalized,
            "너",
            "넌",
            "네가",
            "니가",
            "당신",
            "어시스턴트",
            "ai",
            "봇",
            "omni",
            "옴니");
        if (!asksAssistant)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "할 수",
            "할수",
            "뭘 할",
            "뭐 할",
            "무엇을 할",
            "기능",
            "도움",
            "가능",
            "능력",
            "capability",
            "capabilities")
            || compact.Contains("할수있는", StringComparison.Ordinal)
            || compact.Contains("할수있", StringComparison.Ordinal);
    }

    private static bool LooksLikeLocalIdentityQuestion(string normalized, string compact)
    {
        return ContainsAny(
            normalized,
            "너는 누구",
            "넌 누구",
            "너 누구",
            "너가 누구",
            "네가 누구",
            "니가 누구",
            "당신은 누구",
            "당신 누구",
            "정체",
            "자기소개",
            "who are you")
            || compact.Contains("너는누구", StringComparison.Ordinal)
            || compact.Contains("넌누구", StringComparison.Ordinal)
            || compact.Contains("너누구", StringComparison.Ordinal)
            || compact.Contains("너뭐야", StringComparison.Ordinal)
            || compact.Contains("넌뭐야", StringComparison.Ordinal);
    }

    private string BuildLocalIdentityResponse()
    {
        var snapshot = TryLoadProjectContextSnapshotForLocalInfo();
        if (snapshot == null)
        {
            return """
                   저는 Omni-node 어시스턴트입니다.
                   현재 프로젝트 지침과 스킬 스냅샷을 읽지 못해 세부 목록은 확인할 수 없습니다.
                   """;
        }

        return $"""
                저는 Omni-node 어시스턴트입니다.
                현재 프로젝트의 AGENTS.md 지침과 연결된 스킬/명령 정보를 기준으로 답합니다.

                - 프로젝트: {snapshot.ProjectRoot}
                - 지침 소스: {snapshot.Instructions.Sources.Count}개
                - 등록 스킬: {snapshot.Skills.Count}개
                - 등록 명령: {snapshot.Commands.Count}개

                일반 대화는 간결하게 답하고, 코드/문서 작업은 파일을 먼저 읽은 뒤 필요한 범위만 수정하도록 설정되어 있습니다.
                """;
    }

    private string BuildLocalCapabilityResponse()
    {
        var snapshot = TryLoadProjectContextSnapshotForLocalInfo();
        var skillCount = snapshot?.Skills.Count ?? 0;
        var commandCount = snapshot?.Commands.Count ?? 0;

        return $"""
                제가 할 수 있는 일은 이 워크스페이스에 연결된 기능 기준으로 답합니다.

                - 프로젝트 지침(AGENTS.md)을 기준으로 대화와 작업 방식 유지
                - 코드/문서 파일 읽기, 수정, 빌드/테스트 실행, 결과 요약
                - 연결된 스킬을 확인하고 필요한 작업에 맞게 사용
                - 질문이 이전 대화와 이어질 때만 최근 대화, 메모리, RAG 결과를 참고
                - 웹검색이 필요한 질문은 검색 컨텍스트를 붙여 근거 기반으로 답변
                - LLM 제공자/모델 설정, 상태 확인, 응답 이상 진단 지원

                현재 등록된 스킬은 {skillCount}개, 명령 템플릿은 {commandCount}개입니다.
                """;
    }

    private string BuildLocalLimitationResponse()
    {
        var snapshot = TryLoadProjectContextSnapshotForLocalInfo();
        var hasProjectInstructions = snapshot?.Instructions.Sources.Count > 0;

        return $"""
                제가 할 수 없는 일과 제한은 다음과 같습니다.

                - 확인되지 않은 사실을 확정처럼 말하지 않습니다.
                - 사용자 확인 없이 파괴적 변경이나 요청 밖 대규모 변경을 하지 않습니다.
                - 최신 외부 정보는 웹검색이나 제공된 근거 없이는 확정하지 않습니다.
                - 연결되지 않은 계정, 도구, 파일에는 접근할 수 없습니다.
                - 새 질문이 이전 대화와 무관하면 메모리/RAG/최근 대화를 억지로 붙이지 않습니다.

                현재 프로젝트 지침 로드 상태: {(hasProjectInstructions ? "로드됨" : "확인 필요")}
                """;
    }

    private string BuildLocalSkillInventoryResponse()
    {
        var snapshot = TryLoadProjectContextSnapshotForLocalInfo();
        if (snapshot == null)
        {
            return "현재 스킬 스냅샷을 읽지 못했습니다. 프로젝트 컨텍스트 로더 상태를 먼저 확인해야 합니다.";
        }

        if (snapshot.Skills.Count == 0)
        {
            return $"현재 등록된 스킬이 없습니다.\n프로젝트: {snapshot.ProjectRoot}";
        }

        var skills = snapshot.Skills
            .OrderBy(skill => skill.Scope.Equals("project", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"현재 연결된 스킬은 총 {skills.Length}개입니다.");
        builder.AppendLine();

        var emitted = 0;
        AppendSkillGroup("프로젝트 스킬", "project");
        AppendSkillGroup("전역 스킬", "global");

        var remaining = skills.Length - emitted;
        if (remaining > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"나머지 {remaining}개는 스킬 목록 화면에서 확인할 수 있습니다.");
        }

        return builder.ToString().Trim();

        void AppendSkillGroup(string title, string scope)
        {
            var group = skills
                .Where(skill => skill.Scope.Equals(scope, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Max(0, 24 - emitted))
                .ToArray();
            if (group.Length == 0)
            {
                return;
            }

            if (emitted > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine($"{title}:");
            foreach (var skill in group)
            {
                builder.AppendLine($"- {skill.Name}: {TrimLocalAssistantInfoText(skill.Description, 96)}");
                emitted++;
            }
        }
    }

    private ProjectContextSnapshot? TryLoadProjectContextSnapshotForLocalInfo()
    {
        try
        {
            return _projectContextLoader.LoadSnapshot();
        }
        catch (Exception ex)
        {
            _auditLogger.Log("local", "assistant_info_snapshot", "failed", ex.Message);
            return null;
        }
    }

    private string ApplySelectedSkillToPrompt(string input, string? skillName, string? skillScope)
    {
        var safeInput = input ?? string.Empty;
        var normalizedName = (skillName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return safeInput;
        }

        if (safeInput.IndexOf("[Active Skill", StringComparison.OrdinalIgnoreCase) >= 0
            && safeInput.IndexOf(normalizedName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return safeInput;
        }

        try
        {
            var normalizedScope = (skillScope ?? string.Empty).Trim();
            var snapshot = _projectContextLoader.LoadSnapshot();
            var skill = snapshot.Skills
                .Where(item => item.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(normalizedScope)
                               || item.Scope.Equals(normalizedScope, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Scope.Equals("project", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
            if (skill == null || string.IsNullOrWhiteSpace(skill.Path) || !File.Exists(skill.Path))
            {
                return safeInput;
            }

            var raw = File.ReadAllText(skill.Path, Encoding.UTF8);
            var trimmedSkill = TrimToUtf8ByteCount(raw.Trim(), 12_000);
            if (string.IsNullOrWhiteSpace(trimmedSkill))
            {
                return safeInput;
            }

            return $"""
                    다음 SKILL.md 지침을 이번 답변에 적용하세요.

                    [SKILL name={skill.Name} scope={skill.Scope} path={skill.Path}]
                    {trimmedSkill}
                    [/SKILL]

                    사용자 요청:
                    {safeInput}
                    """;
        }
        catch (Exception ex)
        {
            _auditLogger.Log("web", "skill_prompt_apply", "failed", ex.Message);
            return safeInput;
        }
    }

    private string? TryExtractInlineSkillName(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            var snapshot = _projectContextLoader.LoadSnapshot();
            // 단어 경계 검사로 후보를 좁힌 뒤, 활성화 동사 패턴이 함께 있는 첫 매칭만 채택.
            return snapshot.Skills
                .OrderByDescending(skill => skill.Name.Length)
                .FirstOrDefault(skill =>
                    IndexOfSkillNameWithBoundary(normalized, skill.Name) >= 0
                    && Regex.IsMatch(
                        normalized,
                        $@"(?i){Regex.Escape(skill.Name)}\s*(스킬|skill)?\s*(을|를)?\s*(사용|적용|활성|켜|on)"
                    ))?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static string FirstNonEmptyLocal(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string TrimLocalAssistantInfoText(string text, int maxChars)
    {
        var normalized = Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");
        if (normalized.Length <= maxChars)
        {
            return string.IsNullOrWhiteSpace(normalized) ? "설명이 없습니다." : normalized;
        }

        return normalized[..maxChars].TrimEnd() + "...";
    }

    private static string TrimToUtf8ByteCount(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var bytes = rune.Utf8SequenceLength;
            if (used + bytes > maxBytes)
            {
                break;
            }

            builder.Append(rune);
            used += bytes;
        }

        return builder.ToString();
    }

    private async Task<ConversationChatResult> ChatOrchestrationWithStateCoreAsync(
        ChatRequest request,
        CancellationToken cancellationToken
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
        var localAssistantInfoReply = TryBuildLocalAssistantInfoResponse(rawInput, session.SessionId, request.Source);
        if (!string.IsNullOrWhiteSpace(localAssistantInfoReply))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "local:assistant_info");
            _conversationStore.AppendMessage(thread.Id, "assistant", localAssistantInfoReply, "local:assistant_info");
            await EnsureConversationTitleFromFirstTurnAsync(
                thread.Id,
                "local",
                "assistant_info",
                cancellationToken
            );

            var localInfoUpdated = _conversationStore.Get(thread.Id) ?? thread;
            return new ConversationChatResult(
                "orchestration",
                localInfoUpdated.Id,
                "local",
                "assistant_info",
                localAssistantInfoReply,
                "local:assistant_info",
                localInfoUpdated,
                null,
                null
            );
        }

        var localUsageReply = await TryBuildInChatCopilotUsageResponseAsync(rawInput, request.Source, cancellationToken);
        if (!string.IsNullOrWhiteSpace(localUsageReply))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "local:copilot_usage");
            _conversationStore.AppendMessage(thread.Id, "assistant", localUsageReply, "local:copilot_usage");
            await EnsureConversationTitleFromFirstTurnAsync(
                thread.Id,
                "local",
                "copilot_usage",
                cancellationToken
            );

            var localUpdated = _conversationStore.Get(thread.Id) ?? thread;
            return new ConversationChatResult(
                "orchestration",
                localUpdated.Id,
                "local",
                "copilot_usage",
                localUsageReply,
                "local:copilot_usage",
                localUpdated,
                null,
                null
            );
        }

        var basePrepared = await PrepareSharedInputAsync(
            rawInput,
            request.Attachments,
            request.WebUrls,
            request.WebSearchEnabled,
            cancellationToken,
            request.Source,
            session.SessionKey,
            session.SessionId
        );
        if (!string.IsNullOrWhiteSpace(basePrepared.UnsupportedMessage))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"orchestration:{request.Provider ?? "auto"}");
            _conversationStore.AppendMessage(thread.Id, "assistant", basePrepared.UnsupportedMessage, "orchestration:unsupported");
            var blockedView = _conversationStore.Get(thread.Id) ?? thread;
            var blockedCitationBundle = BuildAndLogCitationMappings(
                request.Source,
                "chat-orchestration-unsupported",
                basePrepared.Citations,
                ("text", basePrepared.UnsupportedMessage)
            );
            return new ConversationChatResult(
                "orchestration",
                blockedView.Id,
                request.Provider ?? "auto",
                request.Model ?? "-",
                basePrepared.UnsupportedMessage,
                "orchestration:unsupported",
                blockedView,
                null,
                basePrepared.GuardFailure,
                basePrepared.Citations,
                blockedCitationBundle.Mappings,
                blockedCitationBundle.Validation,
                basePrepared.RetryAttempt,
                basePrepared.RetryMaxAttempts,
                basePrepared.RetryStopReason
            );
        }
        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, rawInput);
        var thinkPlusPreText = ApplySelectedSkillToPrompt(
            basePrepared.Text,
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
            includeLocalTimeHint: true
        );

        var generated = await ChatOrchestrationAsync(
            contextualInput,
            request.Source,
            request.Provider,
            request.Model,
            request.GroqModel,
            request.GeminiModel,
            request.CopilotModel,
            request.CerebrasModel,
            request.CodexModel,
            request.NvidiaModel,
            request.Attachments,
            cancellationToken
        );
        var citationBundle = BuildAndLogCitationMappings(
            request.Source,
            "chat-orchestration",
            basePrepared.Citations,
            ("text", generated.Text)
        );
        var effectiveGuardFailure = basePrepared.GuardFailure;
        var responseText = ApplyListCountFallback(rawInput, generated.Text, basePrepared.Citations);
        responseText = ApplySkillCreateDirective(responseText, request.Source);
        responseText = CleanLeakedSystemMarkers(responseText);

        _conversationStore.AppendMessage(thread.Id, "user", rawInput, $"orchestration:{request.Provider ?? "auto"}");
        _conversationStore.AppendMessage(thread.Id, "assistant", responseText, generated.Route);
        await EnsureConversationTitleFromFirstTurnAsync(
            thread.Id,
            request.Provider ?? "auto",
            request.Model ?? string.Empty,
            cancellationToken
        );

        var note = await MaybeCompressConversationAsync(
            thread.Id,
            $"{session.Scope}-{session.Mode}",
            request.Provider ?? "auto",
            request.Model ?? string.Empty,
            cancellationToken
        );

        var updated = _conversationStore.Get(thread.Id) ?? thread;
        return new ConversationChatResult(
            "orchestration",
            updated.Id,
            request.Provider ?? "auto",
            request.Model ?? "-",
            responseText,
            generated.Route,
            updated,
            note,
            effectiveGuardFailure,
            basePrepared.Citations,
            citationBundle.Mappings,
            citationBundle.Validation,
            basePrepared.RetryAttempt,
            basePrepared.RetryMaxAttempts,
            basePrepared.RetryStopReason
        );
    }

    private async Task<ConversationMultiResult> ChatMultiWithStateCoreAsync(
        MultiChatRequest request,
        CancellationToken cancellationToken
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
        var localGroqModel = IsDisabledModelSelection(request.GroqModel)
            ? "none"
            : string.IsNullOrWhiteSpace(request.GroqModel)
                ? _llmRouter.GetSelectedGroqModel()
                : request.GroqModel.Trim();
        var localGeminiModel = IsDisabledModelSelection(request.GeminiModel)
            ? "none"
            : NormalizeModelSelection(request.GeminiModel) ?? _config.GeminiModel;
        var localCerebrasModel = IsDisabledModelSelection(request.CerebrasModel)
            ? "none"
            : NormalizeModelSelection(request.CerebrasModel) ?? _config.CerebrasModel;
        var localNvidiaModel = IsDisabledModelSelection(request.NvidiaModel)
            ? "none"
            : NormalizeModelSelection(request.NvidiaModel) ?? _config.NvidiaModel;
        var localCopilotModel = IsDisabledModelSelection(request.CopilotModel)
            ? "none"
            : NormalizeModelSelection(request.CopilotModel) ?? _copilotWrapper.GetSelectedModel();
        var localCodexModel = IsDisabledModelSelection(request.CodexModel)
            ? "none"
            : NormalizeModelSelection(request.CodexModel) ?? _config.CodexModel;
        var requestedSummaryProvider = NormalizeProvider(request.SummaryProvider, allowAuto: true);
        var localAssistantInfoReply = TryBuildLocalAssistantInfoResponse(rawInput, session.SessionId, request.Source);
        if (!string.IsNullOrWhiteSpace(localAssistantInfoReply))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "local:assistant_info");
            _conversationStore.AppendMessage(thread.Id, "assistant", localAssistantInfoReply, "local:assistant_info");
            await EnsureConversationTitleFromFirstTurnAsync(
                thread.Id,
                "local",
                "assistant_info",
                cancellationToken
            );

            var localInfoUpdated = _conversationStore.Get(thread.Id) ?? thread;
            return new ConversationMultiResult(
                localInfoUpdated.Id,
                "로컬 안내 응답으로 Groq 호출은 생략되었습니다.",
                "로컬 안내 응답으로 Gemini 호출은 생략되었습니다.",
                "로컬 안내 응답으로 Cerebras 호출은 생략되었습니다.",
                "로컬 안내 응답으로 Copilot 호출은 생략되었습니다.",
                localAssistantInfoReply,
                localGroqModel,
                localGeminiModel,
                localCerebrasModel,
                localCopilotModel,
                requestedSummaryProvider,
                "local",
                localInfoUpdated,
                null,
                null,
                null,
                null,
                null,
                "로컬 안내 응답으로 Codex 호출은 생략되었습니다.",
                localCodexModel,
                localAssistantInfoReply,
                "로컬 안내 응답이라 모델별 차이 정리는 생략되었습니다.",
                "로컬 안내 응답으로 NVIDIA NIM 호출은 생략되었습니다.",
                localNvidiaModel
            );
        }

        var localUsageReply = await TryBuildInChatCopilotUsageResponseAsync(rawInput, request.Source, cancellationToken);
        if (!string.IsNullOrWhiteSpace(localUsageReply))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "local:copilot_usage");
            _conversationStore.AppendMessage(thread.Id, "assistant", localUsageReply, "local:copilot_usage");
            await EnsureConversationTitleFromFirstTurnAsync(
                thread.Id,
                "local",
                "copilot_usage",
                cancellationToken
            );

            var localUpdated = _conversationStore.Get(thread.Id) ?? thread;
            return new ConversationMultiResult(
                localUpdated.Id,
                "로컬 사용량 조회로 Groq 응답은 생략되었습니다.",
                "로컬 사용량 조회로 Gemini 응답은 생략되었습니다.",
                "로컬 사용량 조회로 Cerebras 응답은 생략되었습니다.",
                "로컬 사용량 조회로 Copilot 응답은 생략되었습니다.",
                localUsageReply,
                localGroqModel,
                localGeminiModel,
                localCerebrasModel,
                localCopilotModel,
                requestedSummaryProvider,
                "local",
                localUpdated,
                null,
                null,
                null,
                null,
                null,
                "로컬 사용량 조회로 Codex 응답은 생략되었습니다.",
                localCodexModel,
                "로컬 사용량 조회라 공통 핵심 정리는 생략되었습니다.",
                "로컬 사용량 조회라 부분 차이 정리는 생략되었습니다.",
                "로컬 사용량 조회로 NVIDIA NIM 응답은 생략되었습니다.",
                localNvidiaModel
            );
        }

        var basePrepared = await PrepareSharedInputAsync(
            rawInput,
            request.Attachments,
            request.WebUrls,
            request.WebSearchEnabled,
            cancellationToken,
            request.Source,
            session.SessionKey,
            session.SessionId
        );
        if (!string.IsNullOrWhiteSpace(basePrepared.UnsupportedMessage))
        {
            _conversationStore.AppendMessage(thread.Id, "user", rawInput, "multi");
            _conversationStore.AppendMessage(thread.Id, "assistant", basePrepared.UnsupportedMessage, "multi:unsupported");
            var blockedView = _conversationStore.Get(thread.Id) ?? thread;
            var blockedCitationBundle = BuildAndLogCitationMappings(
                request.Source,
                "chat-multi-unsupported",
                basePrepared.Citations,
                ("summary", basePrepared.UnsupportedMessage)
            );
            return new ConversationMultiResult(
                blockedView.Id,
                basePrepared.UnsupportedMessage,
                basePrepared.UnsupportedMessage,
                basePrepared.UnsupportedMessage,
                basePrepared.UnsupportedMessage,
                basePrepared.UnsupportedMessage,
                localGroqModel,
                localGeminiModel,
                localCerebrasModel,
                localCopilotModel,
                requestedSummaryProvider,
                "blocked",
                blockedView,
                null,
                basePrepared.GuardFailure,
                basePrepared.Citations,
                blockedCitationBundle.Mappings,
                blockedCitationBundle.Validation,
                basePrepared.UnsupportedMessage,
                localCodexModel,
                "공통 핵심 정리를 생략했습니다.",
                "부분 차이 정리를 생략했습니다.",
                basePrepared.UnsupportedMessage,
                localNvidiaModel
            );
        }
        var requestedSkillName = ResolvePromptOrUiSkillName(request.SkillName, rawInput);
        var thinkPlusPreText = ApplySelectedSkillToPrompt(
            basePrepared.Text,
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
            includeLocalTimeHint: true
        );

        var generated = await ChatMultiAsync(
            contextualInput,
            request.Source,
            request.GroqModel,
            request.GeminiModel,
            request.CopilotModel,
            request.CerebrasModel,
            request.SummaryProvider,
            request.CodexModel,
            request.NvidiaModel,
            request.Attachments,
            cancellationToken
        );

        var citationBundle = BuildAndLogCitationMappings(
            request.Source,
            "chat-multi",
            basePrepared.Citations,
            ("groq", generated.GroqText),
            ("gemini", generated.GeminiText),
            ("cerebras", generated.CerebrasText),
            ("nvidia", generated.NvidiaText),
            ("copilot", generated.CopilotText),
            ("codex", generated.CodexText),
            ("summary", generated.Summary),
            ("commonCore", generated.CommonCore),
            ("differences", generated.Differences)
        );
        var effectiveGuardFailure = basePrepared.GuardFailure;
        var responseGroqText = generated.GroqText;
        var responseGeminiText = generated.GeminiText;
        var responseCerebrasText = generated.CerebrasText;
        var responseNvidiaText = generated.NvidiaText;
        var responseCopilotText = generated.CopilotText;
        var responseCodexText = generated.CodexText;
        var responseSummaryText = generated.Summary;
        var comparisonMessageText = BuildMultiComparisonAssistantText(new LlmMultiChatResult(
            responseGroqText,
            responseGeminiText,
            responseCerebrasText,
            responseCopilotText,
            responseSummaryText,
            generated.GroqModel,
            generated.GeminiModel,
            generated.CerebrasModel,
            generated.CopilotModel,
            generated.RequestedSummaryProvider,
            generated.ResolvedSummaryProvider,
            responseCodexText,
            generated.CodexModel,
            generated.CommonCore,
            generated.Differences,
            responseNvidiaText,
            generated.NvidiaModel
        ));
        var summaryMessageText = BuildMultiSummaryAssistantText(
            responseSummaryText,
            generated.CommonCore,
            generated.Differences
        );
        _conversationStore.AppendMessage(thread.Id, "user", rawInput, "multi");
        _conversationStore.AppendMessage(thread.Id, "assistant", comparisonMessageText, "다중 LLM 모델 비교");
        _conversationStore.AppendMessage(
            thread.Id,
            "assistant",
            summaryMessageText,
            $"공통 정리 · {(string.IsNullOrWhiteSpace(generated.ResolvedSummaryProvider) ? "-" : generated.ResolvedSummaryProvider)}"
        );
        await EnsureConversationTitleFromFirstTurnAsync(
            thread.Id,
            generated.ResolvedSummaryProvider,
            string.Empty,
            cancellationToken
        );

        var note = await MaybeCompressConversationAsync(
            thread.Id,
            $"{session.Scope}-{session.Mode}",
            generated.ResolvedSummaryProvider,
            string.Empty,
            cancellationToken
        );

        var updated = _conversationStore.Get(thread.Id) ?? thread;
        return new ConversationMultiResult(
            updated.Id,
            responseGroqText,
            responseGeminiText,
            responseCerebrasText,
            responseCopilotText,
            responseSummaryText,
            generated.GroqModel,
            generated.GeminiModel,
            generated.CerebrasModel,
            generated.CopilotModel,
            generated.RequestedSummaryProvider,
            generated.ResolvedSummaryProvider,
            updated,
            note,
            effectiveGuardFailure,
            basePrepared.Citations,
            citationBundle.Mappings,
            citationBundle.Validation,
            responseCodexText,
            generated.CodexModel,
            generated.CommonCore,
            generated.Differences,
            responseNvidiaText,
            generated.NvidiaModel
        );
    }


    private async Task<LlmSingleChatResult> ChatSingleCoreAsync(
        string input,
        string provider,
        string? model,
        string source,
        CancellationToken cancellationToken,
        int? maxOutputTokens = null,
        Action<string>? streamCallback = null
    )
    {
        var text = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LlmSingleChatResult(provider, model ?? "-", "empty input");
        }

        var generated = await GenerateByProviderSafeAsync(
            provider,
            model,
            text,
            cancellationToken,
            maxOutputTokens,
            streamCallback: streamCallback
        );
        var cleaned = SanitizeChatOutput(generated.Text);
        _auditLogger.Log(source, "chat_single", "ok", $"provider={generated.Provider} model={generated.Model}");
        return new LlmSingleChatResult(generated.Provider, generated.Model, cleaned);
    }

    private async Task<LlmOrchestrationResult> ChatOrchestrationCoreAsync(
        string input,
        string source,
        string? provider,
        string? model,
        string? groqModel,
        string? geminiModel,
        string? copilotModel,
        string? cerebrasModel,
        string? codexModel,
        string? nvidiaModel,
        IReadOnlyList<InputAttachment>? attachments,
        CancellationToken cancellationToken
    )
    {
        var text = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LlmOrchestrationResult("unknown", "empty input");
        }

        var workerSpecs = new List<(string Provider, string? Model)>();
        if (_llmRouter.HasGroqApiKey() && !IsDisabledModelSelection(groqModel))
        {
            var selectedGroq = string.IsNullOrWhiteSpace(groqModel) ? null : groqModel.Trim();
            workerSpecs.Add(("groq", selectedGroq));
        }

        if (_llmRouter.HasGeminiApiKey() && !IsDisabledModelSelection(geminiModel))
        {
            var selectedGemini = string.IsNullOrWhiteSpace(geminiModel) ? null : geminiModel.Trim();
            workerSpecs.Add(("gemini", selectedGemini));
        }

        if (_llmRouter.HasCerebrasApiKey() && !IsDisabledModelSelection(cerebrasModel))
        {
            var selectedCerebras = string.IsNullOrWhiteSpace(cerebrasModel) ? null : cerebrasModel.Trim();
            workerSpecs.Add(("cerebras", selectedCerebras));
        }

        if (_llmRouter.HasNvidiaApiKey() && !IsDisabledModelSelection(nvidiaModel))
        {
            var selectedNvidia = string.IsNullOrWhiteSpace(nvidiaModel) ? null : nvidiaModel.Trim();
            workerSpecs.Add(("nvidia", selectedNvidia));
        }

        var copilotStatus = await _copilotWrapper.GetStatusAsync(cancellationToken);
        if (copilotStatus.Installed && copilotStatus.Authenticated && !IsDisabledModelSelection(copilotModel))
        {
            var selectedCopilot = string.IsNullOrWhiteSpace(copilotModel) ? _copilotWrapper.GetSelectedModel() : copilotModel.Trim();
            workerSpecs.Add(("copilot", selectedCopilot));
        }

        var codexStatus = await _codexWrapper.GetStatusAsync(cancellationToken);
        if (codexStatus.Installed && codexStatus.Authenticated && !IsDisabledModelSelection(codexModel))
        {
            var selectedCodex = NormalizeModelSelection(codexModel) ?? _config.CodexModel;
            workerSpecs.Add(("codex", selectedCodex));
        }

        if (workerSpecs.Count == 0)
        {
            return new LlmOrchestrationResult("no_provider", "사용 가능한 LLM이 없습니다. Groq/Gemini/Cerebras/NVIDIA 키 또는 Copilot/Codex 인증을 확인하세요.");
        }

        var participatingProviders = workerSpecs
            .Select(x => x.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var roleByProvider = BuildChatOrchestrationRoleAssignments(participatingProviders, text);
        var workerRuns = workerSpecs
            .Select(spec =>
            {
                var role = roleByProvider.TryGetValue(spec.Provider, out var assignedRole)
                    ? assignedRole
                    : "보조 워커";
                var prompt = BuildChatOrchestrationWorkerPrompt(text, spec.Provider, role, participatingProviders);
                return (
                    Provider: spec.Provider,
                    Task: ExecuteProviderChatWithPreparedInputAsync(spec.Provider, spec.Model, prompt, attachments, cancellationToken)
                );
            })
            .ToArray();

        await Task.WhenAll(workerRuns.Select(x => x.Task));
        var workerResults = workerRuns
            .Select(x =>
            {
                var result = x.Task.Result;
                return new LlmSingleChatResult(result.Provider, result.Model, SanitizeChatOutput(result.Text));
            })
            .ToList();
        var availabilityByProvider = await GetProviderAvailabilityMapAsync(cancellationToken);
        var selectionByProvider = BuildProviderSelectionMap(groqModel, geminiModel, cerebrasModel, copilotModel, codexModel, nvidiaModel);
        var successfulWorkers = workerResults
            .Where(x => IsUsableWorkerResult(x, availabilityByProvider, selectionByProvider))
            .ToArray();

        var requestedProvider = NormalizeProvider(provider, allowAuto: true);
        var resolvedProvider = ResolveProviderForAggregation(
            TaskCategory.GeneralChat,
            requestedProvider,
            successfulWorkers,
            availabilityByProvider,
            selectionByProvider,
            allowProviderWithoutWorkerFallback: true
        );
        if (resolvedProvider == "none")
        {
            resolvedProvider = workerResults[0].Provider;
        }

        if ((resolvedProvider == "groq" && IsDisabledModelSelection(groqModel))
            || (resolvedProvider == "gemini" && IsDisabledModelSelection(geminiModel))
            || (resolvedProvider == "cerebras" && IsDisabledModelSelection(cerebrasModel))
            || (resolvedProvider == "nvidia" && IsDisabledModelSelection(nvidiaModel))
            || (resolvedProvider == "copilot" && IsDisabledModelSelection(copilotModel))
            || (resolvedProvider == "codex" && IsDisabledModelSelection(codexModel)))
        {
            resolvedProvider = workerResults[0].Provider;
        }

        var aggregateModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (string.IsNullOrWhiteSpace(aggregateModel))
        {
            aggregateModel = resolvedProvider switch
            {
                "groq" => ResolveModelForCategory(TaskCategory.GeneralChat, resolvedProvider, groqModel),
                "copilot" => ResolveModelForCategory(TaskCategory.GeneralChat, resolvedProvider, copilotModel),
                "codex" => ResolveModelForCategory(TaskCategory.GeneralChat, resolvedProvider, codexModel),
                "nvidia" => ResolveModelForCategory(TaskCategory.GeneralChat, resolvedProvider, nvidiaModel),
                "cerebras" => ResolveModelForCategory(TaskCategory.GeneralChat, resolvedProvider, cerebrasModel),
                _ => ResolveModelForCategory(TaskCategory.GeneralChat, resolvedProvider, geminiModel)
            };
        }

        var aggregatePrompt = BuildOrchestrationPrompt(text, workerResults, roleByProvider);
        var finalResult = await GenerateByProviderSafeAsync(
            resolvedProvider,
            aggregateModel,
            aggregatePrompt,
            cancellationToken
        );
        var cleanedFinal = SanitizeChatOutput(finalResult.Text);

        var workerRoute = string.Join(
            ",",
            workerResults.Select(x => $"{x.Provider}:{x.Model}")
        );
        var route = $"orchestration_parallel[{workerRoute}]=>{finalResult.Provider}:{finalResult.Model}";
        _auditLogger.Log(source, "chat_orchestration", "ok", route);
        return new LlmOrchestrationResult(route, cleanedFinal);
    }

    private async Task<LlmMultiChatResult> ChatMultiCoreAsync(
        string input,
        string source,
        string? groqModel,
        string? geminiModel,
        string? copilotModel,
        string? cerebrasModel,
        string? summaryProvider,
        string? codexModel,
        string? nvidiaModel,
        IReadOnlyList<InputAttachment>? attachments,
        CancellationToken cancellationToken
    )
    {
        var text = (input ?? string.Empty).Trim();
        var hasGroqOverride = !string.IsNullOrWhiteSpace(groqModel) && !IsDisabledModelSelection(groqModel);
        var groqSelected = hasGroqOverride ? groqModel!.Trim() : _llmRouter.GetSelectedGroqModel();
        var geminiSelected = NormalizeModelSelection(geminiModel) ?? _config.GeminiModel;
        var cerebrasSelected = NormalizeModelSelection(cerebrasModel) ?? _config.CerebrasModel;
        var nvidiaSelected = NormalizeModelSelection(nvidiaModel) ?? _config.NvidiaModel;
        var copilotSelected = NormalizeModelSelection(copilotModel) ?? _copilotWrapper.GetSelectedModel();
        var codexSelected = NormalizeModelSelection(codexModel) ?? _config.CodexModel;
        var groqResolvedModel = IsDisabledModelSelection(groqModel) ? "none" : groqSelected;
        var geminiResolvedModel = IsDisabledModelSelection(geminiModel) ? "none" : geminiSelected;
        var cerebrasResolvedModel = IsDisabledModelSelection(cerebrasModel) ? "none" : cerebrasSelected;
        var nvidiaResolvedModel = IsDisabledModelSelection(nvidiaModel) ? "none" : nvidiaSelected;
        var copilotResolvedModel = IsDisabledModelSelection(copilotModel) ? "none" : copilotSelected;
        var codexResolvedModel = IsDisabledModelSelection(codexModel) ? "none" : codexSelected;
        var requestedSummaryProvider = NormalizeProvider(summaryProvider, allowAuto: true);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LlmMultiChatResult(
                "empty input",
                "empty input",
                "empty input",
                "empty input",
                "empty input",
                groqResolvedModel,
                geminiResolvedModel,
                cerebrasResolvedModel,
                copilotResolvedModel,
                requestedSummaryProvider,
                "none",
                "empty input",
                codexResolvedModel,
                "empty input",
                "empty input",
                "empty input",
                nvidiaResolvedModel
            );
        }

        Task<LlmSingleChatResult> groqTask = IsDisabledModelSelection(groqModel)
            ? Task.FromResult(new LlmSingleChatResult("groq", "none", "선택 안함"))
            : _llmRouter.HasGroqApiKey()
                ? ExecuteProviderChatWithPreparedInputAsync("groq", hasGroqOverride ? groqSelected : null, text, attachments, cancellationToken)
                : Task.FromResult(new LlmSingleChatResult("groq", groqSelected, "Groq API 키가 설정되지 않았습니다."));

        Task<LlmSingleChatResult> geminiTask = IsDisabledModelSelection(geminiModel)
            ? Task.FromResult(new LlmSingleChatResult("gemini", "none", "선택 안함"))
            : _llmRouter.HasGeminiApiKey()
                ? ExecuteProviderChatWithPreparedInputAsync("gemini", geminiSelected, text, attachments, cancellationToken)
                : Task.FromResult(new LlmSingleChatResult("gemini", geminiSelected, "Gemini API 키가 설정되지 않았습니다."));

        Task<LlmSingleChatResult> cerebrasTask = IsDisabledModelSelection(cerebrasModel)
            ? Task.FromResult(new LlmSingleChatResult("cerebras", "none", "선택 안함"))
            : _llmRouter.HasCerebrasApiKey()
                ? ExecuteProviderChatWithPreparedInputAsync("cerebras", cerebrasSelected, text, attachments, cancellationToken)
                : Task.FromResult(new LlmSingleChatResult("cerebras", cerebrasSelected, "Cerebras API 키가 설정되지 않았습니다."));

        Task<LlmSingleChatResult> nvidiaTask = IsDisabledModelSelection(nvidiaModel)
            ? Task.FromResult(new LlmSingleChatResult("nvidia", "none", "선택 안함"))
            : _llmRouter.HasNvidiaApiKey()
                ? ExecuteProviderChatWithPreparedInputAsync("nvidia", nvidiaSelected, text, attachments, cancellationToken)
                : Task.FromResult(new LlmSingleChatResult("nvidia", nvidiaSelected, "NVIDIA NIM API 키가 설정되지 않았습니다."));

        var copilotStatus = await _copilotWrapper.GetStatusAsync(cancellationToken);
        Task<LlmSingleChatResult> copilotTask = IsDisabledModelSelection(copilotModel)
            ? Task.FromResult(new LlmSingleChatResult("copilot", "none", "선택 안함"))
            : (copilotStatus.Installed && copilotStatus.Authenticated
                ? ExecuteProviderChatWithPreparedInputAsync("copilot", copilotSelected, text, attachments, cancellationToken)
                : Task.FromResult(new LlmSingleChatResult("copilot", copilotSelected, "Copilot 인증이 필요합니다.")));
        var codexStatus = await _codexWrapper.GetStatusAsync(cancellationToken);
        Task<LlmSingleChatResult> codexTask = IsDisabledModelSelection(codexModel)
            ? Task.FromResult(new LlmSingleChatResult("codex", "none", "선택 안함"))
            : (codexStatus.Installed && codexStatus.Authenticated
                ? ExecuteProviderChatWithPreparedInputAsync("codex", codexSelected, text, attachments, cancellationToken)
                : Task.FromResult(new LlmSingleChatResult("codex", codexSelected, "Codex 인증이 필요합니다.")));

        await Task.WhenAll(groqTask, geminiTask, cerebrasTask, nvidiaTask, copilotTask, codexTask);
        var workerResults = new[]
        {
            new LlmSingleChatResult(groqTask.Result.Provider, groqTask.Result.Model, SanitizeChatOutput(groqTask.Result.Text)),
            new LlmSingleChatResult(geminiTask.Result.Provider, geminiTask.Result.Model, SanitizeChatOutput(geminiTask.Result.Text)),
            new LlmSingleChatResult(cerebrasTask.Result.Provider, cerebrasTask.Result.Model, SanitizeChatOutput(cerebrasTask.Result.Text)),
            new LlmSingleChatResult(nvidiaTask.Result.Provider, nvidiaTask.Result.Model, SanitizeChatOutput(nvidiaTask.Result.Text)),
            new LlmSingleChatResult(copilotTask.Result.Provider, copilotTask.Result.Model, SanitizeChatOutput(copilotTask.Result.Text)),
            new LlmSingleChatResult(codexTask.Result.Provider, codexTask.Result.Model, SanitizeChatOutput(codexTask.Result.Text))
        };
        var availabilityByProvider = await GetProviderAvailabilityMapAsync(cancellationToken);
        var selectionByProvider = BuildProviderSelectionMap(groqModel, geminiModel, cerebrasModel, copilotModel, codexModel, nvidiaModel);
        var successfulWorkers = workerResults
            .Where(x => IsUsableWorkerResult(x, availabilityByProvider, selectionByProvider))
            .ToArray();

        var groq = workerResults[0].Text;
        var gemini = workerResults[1].Text;
        var cerebras = workerResults[2].Text;
        var nvidia = workerResults[3].Text;
        var copilot = workerResults[4].Text;
        var codex = workerResults[5].Text;

        var summaryPrompt = $"""
                            사용자 질문:
                            {text}

                            [Groq]
                            {groq}

                            [Gemini]
                            {gemini}

                            [Cerebras]
                            {cerebras}

                            [NVIDIA NIM]
                            {nvidia}

                            [Copilot]
                            {copilot}

                            [Codex]
                            {codex}

                            위 답변들을 비교해 아래 형식으로만 정리하세요.
                            [공통 요약]
                            - 모든 답변을 한 번에 읽지 않아도 되는 짧은 요약 2~4문장

                            [공통 핵심]
                            - 대부분 답변이 겹치는 핵심 bullet

                            [부분 차이]
                            - 서로 결론, 강조점, 조건이 갈리는 부분 bullet

                            규칙:
                            - 공통점이 거의 없으면 [공통 핵심]에 "공통점 없음"이라고 적으세요.
                            - 부분 차이가 없으면 [부분 차이]에 "의미 있는 차이 없음"이라고 적으세요.
                            - 한국어로 간결하게 작성하세요.
                            """;
        var resolvedSummaryProvider = ResolveProviderForAggregation(
            TaskCategory.GeneralChat,
            requestedSummaryProvider,
            successfulWorkers,
            availabilityByProvider,
            selectionByProvider,
            allowProviderWithoutWorkerFallback: false
        );

        string summary;
        if (resolvedSummaryProvider == "none")
        {
            summary = "공통 요약을 만들 수 없어 자동 정리를 건너뜁니다.";
        }
        else
        {
            var summaryResult = await GenerateByProviderSafeAsync(resolvedSummaryProvider, null, summaryPrompt, cancellationToken);
            summary = SanitizeChatOutput(summaryResult.Text);
        }
        var summarySections = ParseMultiSummarySections(summary);

        _auditLogger.Log(source, "chat_multi", "ok", $"groq={groqSelected} nvidia={nvidiaSelected} cerebras={cerebrasSelected} copilot={copilotSelected} codex={codexSelected} summary={resolvedSummaryProvider}");
        return new LlmMultiChatResult(
            groq,
            gemini,
            cerebras,
            copilot,
            summarySections.CommonSummary,
            groqResolvedModel,
            geminiResolvedModel,
            cerebrasResolvedModel,
            copilotResolvedModel,
            requestedSummaryProvider,
            resolvedSummaryProvider,
            codex,
            codexResolvedModel,
            summarySections.CommonCore,
            summarySections.Differences,
            nvidia,
            nvidiaResolvedModel
        );
    }

    private static Dictionary<string, string> BuildChatOrchestrationRoleAssignments(
        IReadOnlyList<string> providers,
        string input
    )
    {
        var uniqueProviders = providers
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (uniqueProviders.Length <= 1)
        {
            return uniqueProviders.ToDictionary(
                provider => provider,
                _ => "단독 처리 담당. 핵심 결론, 리스크, 실행 단계를 한 번에 정리하세요.",
                StringComparer.OrdinalIgnoreCase
            );
        }

        var normalizedInput = (input ?? string.Empty).ToLowerInvariant();
        var planningHeavy = ContainsAny(normalizedInput, "설계", "architecture", "기획", "plan", "전략", "구조", "refactor", "리팩토");
        var debuggingHeavy = ContainsAny(normalizedInput, "bug", "fix", "debug", "error", "issue", "오류", "실패", "원인", "재현");
        var compareHeavy = ContainsAny(normalizedInput, "비교", "차이", "장단점", "vs", "선택", "recommend", "추천", "트레이드오프");
        var actionHeavy = ContainsAny(normalizedInput, "어떻게", "step", "절차", "실행", "명령", "적용", "도입", "migration");
        var uiHeavy = ContainsAny(normalizedInput, "ui", "ux", "layout", "frontend", "html", "css", "화면", "컴포넌트", "디자인");
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in uniqueProviders)
        {
            assignments[provider] = provider switch
            {
                "groq" => debuggingHeavy
                    ? "빠른 원인 후보와 1차 해결 방향을 압축해서 제시하세요."
                    : compareHeavy
                        ? "빠른 결론 후보와 선택 기준을 먼저 정리하세요."
                        : "빠른 1차 답변 초안과 핵심 결론 후보를 정리하세요.",
                "gemini" => planningHeavy
                    ? "요구사항을 분해하고 구조적인 답변 뼈대를 설계하세요."
                    : "빠진 전제, 설명 순서, 숨은 가정을 정리하세요.",
                "cerebras" => compareHeavy || planningHeavy
                    ? "대안 비교, 장단점, 반례와 엣지케이스를 넓게 점검하세요."
                    : "반례, 예외, 장기 영향과 누락된 맥락을 점검하세요.",
                "nvidia" => debuggingHeavy || actionHeavy
                    ? "빠른 실행 관점에서 원인, 수정 절차, 검증 포인트를 구체화하세요."
                    : "실행 가능한 보완 관점과 누락된 조건을 간결하게 점검하세요.",
                "copilot" => uiHeavy
                    ? "바로 적용할 수 있는 화면/컴포넌트 예시와 실행 단계를 구체화하세요."
                    : actionHeavy
                        ? "실행 가능한 절차, 예시, 적용 순서를 구체화하세요."
                        : "실무 적용 예시와 바로 써먹을 단계를 정리하세요.",
                "codex" => debuggingHeavy
                    ? "모순, 누락, 실패 포인트를 검증하고 최종 보수적 결론을 제시하세요."
                    : "정밀 검토 담당으로서 모순 제거와 최종 누락 점검을 하세요.",
                _ => planningHeavy
                    ? "요구사항 정리와 답변 구조화를 보조하세요."
                    : "보조 관점에서 답변을 보강하세요."
            };
        }

        return assignments;
    }

    private static string BuildChatOrchestrationWorkerPrompt(
        string userText,
        string provider,
        string role,
        IReadOnlyList<string> participatingProviders
    )
    {
        var providerLabel = string.IsNullOrWhiteSpace(provider) ? "worker" : provider.Trim();
        var lineup = participatingProviders.Count == 0
            ? providerLabel
            : string.Join(", ", participatingProviders);
        var assignedRole = string.IsNullOrWhiteSpace(role) ? "보조 워커" : role.Trim();
        return $"""
                너는 대화 오케스트레이션 워커다.
                현재 워커: {providerLabel}
                참여 워커: {lineup}
                배정 역할: {assignedRole}

                작업 규칙:
                1) 네 역할 관점에 집중해서 답변한다.
                2) 다른 워커가 맡을 만한 설명을 장황하게 반복하지 않는다.
                3) 확실하지 않으면 단정 대신 보수적으로 표현한다.
                4) 한국어로 간결하게 작성한다.

                출력 형식:
                - 첫 줄: 역할 관점 결론 1문장
                - 이후 최대 5개 bullet

                [사용자 질문/컨텍스트]
                {userText}
                """;
    }

}
