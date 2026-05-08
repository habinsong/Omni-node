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
        var localAssistantInfoReply = TryBuildLocalAssistantInfoResponse(rawInput);
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

        if (request.WebSearchEnabled)
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

        using var singleRequestCts = requestedProvider.Equals("copilot", StringComparison.OrdinalIgnoreCase)
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var singleRequestToken = cancellationToken;
        if (singleRequestCts != null)
        {
            singleRequestCts.CancelAfter(TimeSpan.FromSeconds(17));
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

        var skillPreparedText = ApplySelectedSkillToPrompt(
            preparedInput.Text,
            request.SkillName,
            request.SkillScope
        );
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
                    generated = new LlmSingleChatResult(
                        generated.Provider,
                        generated.Model,
                        BuildOffTopicGuardMessage(rawInput)
                    );
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
                generated = new LlmSingleChatResult(
                    generated.Provider,
                    generated.Model,
                    BuildOffTopicGuardMessage(rawInput)
                );
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
            return 1600;
        }

        if (LooksLikeListOutputRequest(normalized))
        {
            return 1100;
        }

        if (ContainsAny(normalized, "요약", "정리", "summary", "compare", "비교"))
        {
            return 900;
        }

        return 1200;
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

    private string? TryBuildLocalAssistantInfoResponse(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || normalized.Length > 180)
        {
            return null;
        }

        var compact = Regex.Replace(normalized, @"[\p{P}\p{S}\s]+", string.Empty);
        if (LooksLikeLocalSkillInventoryQuestion(normalized, compact))
        {
            return BuildLocalSkillInventoryResponse();
        }

        var skillActivationReply = TryBuildLocalSkillActivationResponse(normalized, compact);
        if (!string.IsNullOrWhiteSpace(skillActivationReply))
        {
            return skillActivationReply;
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

    private string? TryBuildLocalSkillActivationResponse(string normalized, string compact)
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

        // 사용자가 같은 메시지에 task까지 같이 줬으면 stub으로 가로채지 말고 LLM에 넘김.
        // 예: "X 스킬 사용해서 Y에 대해 설명해줘" — "사용해서" 연결어미 + 추가 동사/명사가 task 신호.
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

        var matchedSkill = snapshot.Skills
            .OrderByDescending(skill => skill.Name.Length)
            .FirstOrDefault(skill => normalized.Contains(skill.Name, StringComparison.OrdinalIgnoreCase));
        if (matchedSkill == null)
        {
            return null;
        }

        return $"""
                `{matchedSkill.Name}` 스킬을 사용할 수 있습니다.
                다음 메시지에서 설명하거나 검토할 주제를 바로 보내세요.

                - 스킬: {matchedSkill.Name}
                - 설명: {TrimLocalAssistantInfoText(matchedSkill.Description, 120)}
                """;
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

        return ContainsAny(
            normalized,
            "뭐",
            "무엇",
            "어떤",
            "목록",
            "리스트",
            "보여",
            "알려",
            "있어",
            "있니",
            "가능",
            "가지고",
            "사용 가능한",
            "available",
            "list");
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
        var normalizedName = (skillName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return input;
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
                return input;
            }

            var raw = File.ReadAllText(skill.Path, Encoding.UTF8);
            var trimmedSkill = TrimToUtf8ByteCount(raw.Trim(), 12_000);
            if (string.IsNullOrWhiteSpace(trimmedSkill))
            {
                return input;
            }

            return $"""
                    다음 SKILL.md 지침을 이번 답변에 적용하세요.

                    [SKILL name={skill.Name} scope={skill.Scope} path={skill.Path}]
                    {trimmedSkill}
                    [/SKILL]

                    사용자 요청:
                    {input}
                    """;
        }
        catch (Exception ex)
        {
            _auditLogger.Log("web", "skill_prompt_apply", "failed", ex.Message);
            return input;
        }
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
        var localAssistantInfoReply = TryBuildLocalAssistantInfoResponse(rawInput);
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
        var contextualInput = BuildContextualInput(
            session.SessionId,
            basePrepared.Text,
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
        var localCopilotModel = IsDisabledModelSelection(request.CopilotModel)
            ? "none"
            : NormalizeModelSelection(request.CopilotModel) ?? _copilotWrapper.GetSelectedModel();
        var localCodexModel = IsDisabledModelSelection(request.CodexModel)
            ? "none"
            : NormalizeModelSelection(request.CodexModel) ?? _config.CodexModel;
        var requestedSummaryProvider = NormalizeProvider(request.SummaryProvider, allowAuto: true);
        var localAssistantInfoReply = TryBuildLocalAssistantInfoResponse(rawInput);
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
                "로컬 안내 응답이라 모델별 차이 정리는 생략되었습니다."
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
                "로컬 사용량 조회라 부분 차이 정리는 생략되었습니다."
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
                "부분 차이 정리를 생략했습니다."
            );
        }
        var contextualInput = BuildContextualInput(
            session.SessionId,
            basePrepared.Text,
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
            generated.Differences
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
            generated.Differences
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
            return new LlmOrchestrationResult("no_provider", "사용 가능한 LLM이 없습니다. Groq/Gemini/Cerebras 키 또는 Copilot/Codex 인증을 확인하세요.");
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
        var selectionByProvider = BuildProviderSelectionMap(groqModel, geminiModel, cerebrasModel, copilotModel, codexModel);
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
        IReadOnlyList<InputAttachment>? attachments,
        CancellationToken cancellationToken
    )
    {
        var text = (input ?? string.Empty).Trim();
        var hasGroqOverride = !string.IsNullOrWhiteSpace(groqModel) && !IsDisabledModelSelection(groqModel);
        var groqSelected = hasGroqOverride ? groqModel!.Trim() : _llmRouter.GetSelectedGroqModel();
        var geminiSelected = NormalizeModelSelection(geminiModel) ?? _config.GeminiModel;
        var cerebrasSelected = NormalizeModelSelection(cerebrasModel) ?? _config.CerebrasModel;
        var copilotSelected = NormalizeModelSelection(copilotModel) ?? _copilotWrapper.GetSelectedModel();
        var codexSelected = NormalizeModelSelection(codexModel) ?? _config.CodexModel;
        var groqResolvedModel = IsDisabledModelSelection(groqModel) ? "none" : groqSelected;
        var geminiResolvedModel = IsDisabledModelSelection(geminiModel) ? "none" : geminiSelected;
        var cerebrasResolvedModel = IsDisabledModelSelection(cerebrasModel) ? "none" : cerebrasSelected;
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
                "empty input"
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

        await Task.WhenAll(groqTask, geminiTask, cerebrasTask, copilotTask, codexTask);
        var workerResults = new[]
        {
            new LlmSingleChatResult(groqTask.Result.Provider, groqTask.Result.Model, SanitizeChatOutput(groqTask.Result.Text)),
            new LlmSingleChatResult(geminiTask.Result.Provider, geminiTask.Result.Model, SanitizeChatOutput(geminiTask.Result.Text)),
            new LlmSingleChatResult(cerebrasTask.Result.Provider, cerebrasTask.Result.Model, SanitizeChatOutput(cerebrasTask.Result.Text)),
            new LlmSingleChatResult(copilotTask.Result.Provider, copilotTask.Result.Model, SanitizeChatOutput(copilotTask.Result.Text)),
            new LlmSingleChatResult(codexTask.Result.Provider, codexTask.Result.Model, SanitizeChatOutput(codexTask.Result.Text))
        };
        var availabilityByProvider = await GetProviderAvailabilityMapAsync(cancellationToken);
        var selectionByProvider = BuildProviderSelectionMap(groqModel, geminiModel, cerebrasModel, copilotModel, codexModel);
        var successfulWorkers = workerResults
            .Where(x => IsUsableWorkerResult(x, availabilityByProvider, selectionByProvider))
            .ToArray();

        var groq = workerResults[0].Text;
        var gemini = workerResults[1].Text;
        var cerebras = workerResults[2].Text;
        var copilot = workerResults[3].Text;
        var codex = workerResults[4].Text;

        var summaryPrompt = $"""
                            사용자 질문:
                            {text}

                            [Groq]
                            {groq}

                            [Gemini]
                            {gemini}

                            [Cerebras]
                            {cerebras}

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

        _auditLogger.Log(source, "chat_multi", "ok", $"groq={groqSelected} cerebras={cerebrasSelected} copilot={copilotSelected} codex={codexSelected} summary={resolvedSummaryProvider}");
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
            summarySections.Differences
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
