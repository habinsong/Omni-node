using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private async Task<LlmSingleChatResult> ExecuteProviderChatWithPreparedInputAsync(
        string provider,
        string? model,
        string input,
        IReadOnlyList<InputAttachment>? attachments,
        CancellationToken cancellationToken
    )
    {
        var resolvedProvider = NormalizeProvider(provider, allowAuto: false);
        var resolvedModel = ResolveModel(resolvedProvider, model);
        var prepared = await PrepareInputForProviderAsync(
            input,
            resolvedProvider,
            resolvedModel,
            attachments,
            null,
            true,
            false,
            cancellationToken
        );
        if (!string.IsNullOrWhiteSpace(prepared.UnsupportedMessage))
        {
            return new LlmSingleChatResult(resolvedProvider, resolvedModel, prepared.UnsupportedMessage);
        }

        return await GenerateByProviderSafeAsync(
            resolvedProvider,
            resolvedModel,
            prepared.Text,
            cancellationToken
        );
    }

    private async Task<InputPreparationResult> PrepareSharedInputAsync(
        string input,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        CancellationToken cancellationToken,
        string source = "web",
        string? sessionKey = null,
        string? threadBindingKey = null,
        string? requestedSkillName = null,
        string? requestedSkillScope = null
    )
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var normalizedAttachments = NormalizeAttachments(attachments);
        var urls = ResolveWebUrls(normalizedInput, webUrls, webSearchEnabled);
        var builder = new StringBuilder();

        var skipProjectContext = Regex.IsMatch(normalizedInput, @"(?i)(agent\.md|agents\.md)\s*(사용\s*안\s*함|쓰지\s*마|사용하지\s*마|제외|빼|무시)");
        // 인벤토리/리스트 의도는 "스킬"과 의문/목록 표지어가 인접해 있을 때만 인정한다.
        // "X 스킬 사용해서 ... 어떻게/어떤지" 같이 단어가 멀리 떨어진 경우는 활성화 요청이지 리스트 요청이 아니다.
        var isSkillListRequested = Regex.IsMatch(
                                       normalizedInput,
                                       @"(?i)(스킬|skill|skills|skill\.md)\s*(을|를|이|가|은|는|에|의|들)?\s*(목록|리스트|뭐|무엇|무슨|보여|알려|어떤|어떠한|종류|있어|있니|있나|가지고)"
                                   )
                                   || Regex.IsMatch(
                                       normalizedInput,
                                       @"(?i)(목록|리스트|뭐|무엇|무슨|보여|알려|어떤|어떠한|종류).{0,8}(스킬|skill|skills|skill\.md)"
                                   );
        var isSkillCreationRequested = LooksLikeSkillCreationRequest(normalizedInput);
        var isSkillDeactivationRequested = LooksLikeSkillDeactivationRequest(normalizedInput);
        var threadKeyForActiveSkill = string.IsNullOrWhiteSpace(threadBindingKey) ? sessionKey : threadBindingKey;
        var hasActiveSkill = !string.IsNullOrWhiteSpace(threadKeyForActiveSkill)
            && _activeSkillByThread.ContainsKey(threadKeyForActiveSkill);
        var shouldIncludeProjectContext = !skipProjectContext
            && (TryExtractSessionScope(sessionKey) == "coding"
                || LooksLikeProjectContextRequest(normalizedInput)
                || isSkillListRequested
                || isSkillCreationRequested
                || isSkillDeactivationRequested
                || hasActiveSkill);
        var notebookContext = BuildNotebookPromptContext(normalizedInput, sessionKey);

        if (shouldIncludeProjectContext)
        {
            try
            {
                var snapshot = _projectContextLoader.LoadSnapshot();
                var contextBuilder = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(snapshot.Instructions.CombinedText))
                {
                    contextBuilder.AppendLine("[System Kernel (AGENTS.md)]");
                    contextBuilder.AppendLine(snapshot.Instructions.CombinedText);
                    contextBuilder.AppendLine();
                }

                if (isSkillListRequested)
                {
                    contextBuilder.AppendLine("[Available Skills List]");
                    foreach (var skill in snapshot.Skills)
                    {
                        contextBuilder.AppendLine($"- {skill.Name}: {skill.Description}");
                    }
                    contextBuilder.AppendLine("위 스킬 목록을 참조하여, 사용자의 질문에 친절하게 요약해서 답변해주세요.");
                    contextBuilder.AppendLine();
                }

                var threadKey = string.IsNullOrWhiteSpace(threadBindingKey)
                    ? sessionKey
                    : threadBindingKey;
                var deactivationDetected = LooksLikeSkillDeactivationRequest(normalizedInput);

                // 단어 경계 검사 helper로 이름 부분문자열 false-positive를 차단.
                // 다중 스킬 입력은 상위 흐름에서 이미 거부되므로 여기선 첫 매칭(가장 긴 이름)만 채택.
                var explicitlyMentionedSkill = DetectMentionedSkillsInPrompt(normalizedInput).FirstOrDefault();
                var requestedSkill = explicitlyMentionedSkill == null && !deactivationDetected
                    ? FindSkillManifestByName(requestedSkillName, requestedSkillScope)
                    : null;

                if (deactivationDetected && explicitlyMentionedSkill == null)
                {
                    if (!string.IsNullOrWhiteSpace(threadKey)
                        && _activeSkillByThread.TryRemove(threadKey, out var clearedSkillName))
                    {
                        PersistActiveSkillForThread(threadKey, null);
                        contextBuilder.AppendLine($"[Skill Deactivated: {clearedSkillName}]");
                        contextBuilder.AppendLine("사용자가 활성 스킬을 해제했습니다. 이번 응답부터는 일반 대화 톤으로 답하세요.");
                        contextBuilder.AppendLine();
                    }
                }

                SkillManifest? skillToActivate = null;
                string? previousSkillName = null;
                if (explicitlyMentionedSkill != null)
                {
                    // 새 스킬로 전환되는지 검사 (이전 활성 스킬 이름이 다르면 switch)
                    if (!string.IsNullOrWhiteSpace(threadKey)
                        && _activeSkillByThread.TryGetValue(threadKey, out var existingActive)
                        && !string.IsNullOrWhiteSpace(existingActive)
                        && !string.Equals(existingActive, explicitlyMentionedSkill.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        previousSkillName = existingActive;
                    }
                    skillToActivate = explicitlyMentionedSkill;
                    if (!string.IsNullOrWhiteSpace(threadKey))
                    {
                        _activeSkillByThread[threadKey] = explicitlyMentionedSkill.Name;
                        PersistActiveSkillForThread(threadKey, explicitlyMentionedSkill.Name);
                    }
                }
                else if (requestedSkill != null)
                {
                    if (!string.IsNullOrWhiteSpace(threadKey)
                        && _activeSkillByThread.TryGetValue(threadKey, out var existingActive)
                        && !string.IsNullOrWhiteSpace(existingActive)
                        && !string.Equals(existingActive, requestedSkill.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        previousSkillName = existingActive;
                    }
                    skillToActivate = requestedSkill;
                    if (!string.IsNullOrWhiteSpace(threadKey))
                    {
                        _activeSkillByThread[threadKey] = requestedSkill.Name;
                        PersistActiveSkillForThread(threadKey, requestedSkill.Name);
                    }
                }
                else if (!deactivationDetected
                    && !string.IsNullOrWhiteSpace(threadKey)
                    && _activeSkillByThread.TryGetValue(threadKey, out var stickySkillName))
                {
                    skillToActivate = snapshot.Skills.FirstOrDefault(s =>
                        string.Equals(s.Name, stickySkillName, StringComparison.OrdinalIgnoreCase));
                    if (skillToActivate == null)
                    {
                        _activeSkillByThread.TryRemove(threadKey, out _);
                        PersistActiveSkillForThread(threadKey, null);
                    }
                }

                if (skillToActivate != null)
                {
                    try
                    {
                        var skillText = System.IO.File.ReadAllText(skillToActivate.Path, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(previousSkillName))
                        {
                            contextBuilder.AppendLine($"[Skill Switched: {previousSkillName} → {skillToActivate.Name}]");
                            contextBuilder.AppendLine($"이전 `{previousSkillName}` 스킬은 즉시 종료되었습니다. 이번 응답부터는 `{skillToActivate.Name}` 스킬만 적용하고, 이전 스킬의 톤·규칙·관점·관심사는 절대 사용하지 마세요.");
                            contextBuilder.AppendLine();
                        }
                        var activationLabel = explicitlyMentionedSkill != null ? "Active Skill (sticky)" : "Active Skill (sticky, persisted)";
                        contextBuilder.AppendLine($"[{activationLabel}: {skillToActivate.Name}]");
                        contextBuilder.AppendLine($"이 대화는 `{skillToActivate.Name}` 스킬이 활성화된 상태로 진행됩니다.");
                        contextBuilder.AppendLine("매 응답마다 스킬의 목적·말투·규칙을 우선 적용하세요. 사용자가 \"스킬 그만 / 종료 / 해제 / off\" 같이 명시적으로 끄거나 다른 스킬을 호출하기 전까지 계속 적용합니다.");
                        contextBuilder.AppendLine("스킬과 무관한 감정 위로·잡담·주제 전환을 추가하지 마세요.");
                        contextBuilder.AppendLine(skillText);
                        contextBuilder.AppendLine();
                    }
                    catch { }
                }

                if (isSkillCreationRequested)
                {
                    contextBuilder.AppendLine("[Skill Creation Mode — 반드시 따를 것]");
                    contextBuilder.AppendLine("사용자가 새 스킬 생성을 요청했습니다. 응답에 반드시 아래 형식의 디렉티브를 정확히 포함해야 합니다. 디렉티브가 없으면 파일이 만들어지지 않습니다.");
                    contextBuilder.AppendLine("스킬은 몇 줄짜리 메모가 아니라 재사용 가능한 SKILL.md 실행 지침이어야 합니다. 사용자가 요청한 범위 안에서 목적, 사용 흐름, 응답 원칙, 출력 형식, 확인 기준, 피해야 할 것을 구체적으로 작성하세요.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("형식 (속성 순서/따옴표/줄바꿈 그대로):");
                    contextBuilder.AppendLine("<omni:skill name=\"kebab-case-name\" description=\"무엇을 하며 어떤 요청에서 사용할지 구체적으로 쓴 한 줄 설명\" scope=\"project\" overwrite=\"false\">");
                    contextBuilder.AppendLine("# 스킬 제목");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 목표");
                    contextBuilder.AppendLine("- 이 스킬이 해결할 일을 명확히 쓴다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 사용 흐름");
                    contextBuilder.AppendLine("- 입력에서 확인할 핵심 요소와 처리 순서를 쓴다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 응답 원칙");
                    contextBuilder.AppendLine("- 말투, 깊이, 근거 수준, 예외 처리 방식을 쓴다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 출력 형식");
                    contextBuilder.AppendLine("- 기본 답변 구조를 쓴다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 확인 기준");
                    contextBuilder.AppendLine("- 답변 전 점검할 품질 기준을 쓴다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 피해야 할 것");
                    contextBuilder.AppendLine("- 금지할 행동을 쓴다.");
                    contextBuilder.AppendLine("</omni:skill>");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("규칙:");
                    contextBuilder.AppendLine("- name: 소문자/숫자/하이픈만 (예: casual-empathy, pr-summary, coding-for-beginners). 한글·공백·언더스코어 금지.");
                    contextBuilder.AppendLine("- 사용자가 영문 이름을 안 주면 의도를 짧은 kebab-case로 의역해 정한다.");
                    contextBuilder.AppendLine("- description: 스킬 호출 판단에 쓰인다. 무엇을 하는지와 언제 쓰는지를 한 줄로 구체적으로 적는다. 큰따옴표 안에 들어가므로 큰따옴표 문자는 피한다.");
                    contextBuilder.AppendLine("- scope: 사용자가 전역/global/어디서나/모든 프로젝트를 명시한 경우만 global. 그 외에는 project.");
                    contextBuilder.AppendLine("- overwrite: 사용자가 기존 스킬 덮어쓰기에 명시 동의한 경우만 true.");
                    contextBuilder.AppendLine("- 본문은 사용자가 요청한 동작만 적는다. 추측/부풀림 금지.");
                    contextBuilder.AppendLine("- 너무 짧은 3~5줄 스킬 금지. 단순 톤 스킬도 최소 8줄 이상, 보통 15~30줄 안팎으로 작성한다.");
                    contextBuilder.AppendLine("- 대화/톤 스킬은 첫 문장 방식, 조언 조건, 답변 길이, 피할 표현을 포함한다.");
                    contextBuilder.AppendLine("- 설명 스킬은 대상 독자, 비유 사용 기준, 단계적 설명 순서, 마지막 요약 방식을 포함한다.");
                    contextBuilder.AppendLine("- 코드/리뷰 스킬은 읽을 자료, 변경 원칙, 검증, 위험 보고 방식을 포함한다.");
                    contextBuilder.AppendLine("- 검색/리서치 스킬은 우선 출처, 최신성 확인, 인용 방식, 불확실성 표기를 포함한다.");
                    contextBuilder.AppendLine("- README, 예시 파일, 추가 폴더는 만들지 않는다. 디렉티브 하나로 SKILL.md 본문만 생성한다.");
                    contextBuilder.AppendLine("- 디렉티브를 코드블록(```)으로 감싸지 마라.");
                    contextBuilder.AppendLine("- 사용자 입력과 무관한 잡담/요약/일반 설명을 추가하지 마라. 한 줄 인사 후 바로 디렉티브를 출력한다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("예시 입력: \"공감하는 일상 대화 스킬 만들어줘\"");
                    contextBuilder.AppendLine("올바른 응답:");
                    contextBuilder.AppendLine("스킬 추가했어요.");
                    contextBuilder.AppendLine("<omni:skill name=\"casual-empathy\" description=\"일상 대화에서 사용자의 감정과 상황을 먼저 인정하고 짧고 편안한 공감 톤으로 답해야 할 때 사용한다.\" scope=\"project\" overwrite=\"false\">");
                    contextBuilder.AppendLine("# Casual Empathy");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 목표");
                    contextBuilder.AppendLine("- 일상 대화에서 사용자의 감정과 상황을 먼저 알아주고 부담 없는 톤으로 답한다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 사용 흐름");
                    contextBuilder.AppendLine("- 첫 문장은 사용자의 감정이나 상황을 한 문장으로 인정한다.");
                    contextBuilder.AppendLine("- 사용자가 조언을 요청한 경우에만 짧은 제안을 붙인다.");
                    contextBuilder.AppendLine("- 대화가 이어질 수 있도록 마지막에 가벼운 질문을 하나만 둔다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 응답 원칙");
                    contextBuilder.AppendLine("- 기본 길이는 2~4문장으로 유지한다.");
                    contextBuilder.AppendLine("- 평가, 훈계, 단정 표현을 피한다.");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine("## 피해야 할 것");
                    contextBuilder.AppendLine("- 사용자가 원하지 않은 해결책을 길게 제시하지 않는다.");
                    contextBuilder.AppendLine("</omni:skill>");
                    contextBuilder.AppendLine();
                }

                if (contextBuilder.Length > 0)
                {
                    builder.AppendLine("[Project Context]");
                    builder.AppendLine(contextBuilder.ToString().Trim());
                    builder.AppendLine("--------------------------------------------------");
                    builder.AppendLine();
                }
            }
            catch (Exception)
            {
                // 스캔 실패 시 무시
            }
        }

        if (!string.IsNullOrWhiteSpace(notebookContext))
        {
            builder.AppendLine(notebookContext);
            builder.AppendLine("--------------------------------------------------");
            builder.AppendLine();
        }

        builder.AppendLine(normalizedInput);

        var textAttachmentBlock = BuildTextAttachmentBlock(normalizedAttachments);
        if (!string.IsNullOrWhiteSpace(textAttachmentBlock))
        {
            builder.AppendLine();
            builder.AppendLine(textAttachmentBlock);
        }

        if (urls.Count > 0)
        {
            var webBlock = await BuildWebContextBlockAsync(normalizedInput, urls, cancellationToken);
            if (!string.IsNullOrWhiteSpace(webBlock))
            {
                builder.AppendLine();
                builder.AppendLine(webBlock);
            }
        }

        var normalizedSource = NormalizeAuditToken(source, "web");
        var forcedContextRequestId = BuildForcedContextRequestId();
        var sessionThreadBinding = TryExtractSessionThreadBinding(sessionKey);
        var normalizedSessionThread = NormalizeAuditToken(sessionThreadBinding, "-");
        var normalizedThreadBinding = NormalizeAuditToken(threadBindingKey, "-");
        var bindingStatus = ResolveThreadBindingStatus(sessionThreadBinding, threadBindingKey);
        SearchAnswerGuardFailure? forcedGuardFailure = null;
        IReadOnlyList<SearchCitationReference> forcedCitations = Array.Empty<SearchCitationReference>();
        var forcedRetryAttempt = 0;
        var forcedRetryMaxAttempts = 0;
        var forcedRetryStopReason = "-";
        try
        {
            var (forcedBlock, forcedTrace, guardFailure, citations, retryAttempt, retryMaxAttempts, retryStopReason) = await BuildForcedRetrievalContextBlockAsync(
                normalizedInput,
                normalizedSource,
                sessionKey,
                threadBindingKey,
                forcedContextRequestId,
                cancellationToken
            );
            forcedGuardFailure = guardFailure;
            forcedCitations = citations;
            forcedRetryAttempt = retryAttempt;
            forcedRetryMaxAttempts = retryMaxAttempts;
            forcedRetryStopReason = retryStopReason;
            _auditLogger.Log(normalizedSource, "forced_context", "ok", forcedTrace);
            if (!string.IsNullOrWhiteSpace(forcedBlock))
            {
                builder.AppendLine();
                builder.AppendLine(forcedBlock);
            }
        }
        catch (Exception ex)
        {
            forcedGuardFailure = new SearchAnswerGuardFailure(
                SearchAnswerGuardFailureCategory.Coverage,
                "forced_context_exception",
                TrimForAudit(ex.Message, 160)
            );
            forcedRetryAttempt = 1;
            forcedRetryMaxAttempts = 1;
            forcedRetryStopReason = "forced_context_exception";
            _auditLogger.Log(
                normalizedSource,
                "forced_context",
                "fail",
                BuildForcedContextTraceMessage(
                    forcedContextRequestId,
                    normalizedSource,
                    sessionKey,
                    normalizedThreadBinding,
                    normalizedSessionThread,
                    bindingStatus,
                    "na",
                    CreateForcedToolTrace("error", detail: "prestep_exception"),
                    CreateForcedToolTrace("error", detail: "prestep_exception"),
                    CreateForcedToolTrace("error", detail: "prestep_exception"),
                    CreateForcedToolTrace("error", detail: "prestep_exception"),
                    TrimForAudit(ex.Message, 220)
                )
            );
        }

        var unsupportedMessage = forcedGuardFailure is null
            ? string.Empty
            : BuildGroundedSearchFailureMessage(forcedGuardFailure, forcedRetryStopReason);
        return new InputPreparationResult(
            builder.ToString().Trim(),
            unsupportedMessage,
            forcedGuardFailure,
            forcedCitations,
            forcedRetryAttempt,
            forcedRetryMaxAttempts,
            forcedRetryStopReason
        );
    }

    private async Task<InputPreparationResult> PrepareInputForProviderAsync(
        string input,
        string provider,
        string? model,
        IReadOnlyList<InputAttachment>? attachments,
        IReadOnlyList<string>? webUrls,
        bool webSearchEnabled,
        bool includeSharedContext,
        CancellationToken cancellationToken,
        string source = "web",
        string? sessionKey = null,
        string? threadBindingKey = null,
        string? requestedSkillName = null,
        string? requestedSkillScope = null
    )
    {
        var shared = includeSharedContext
            ? await PrepareSharedInputAsync(
                input,
                attachments,
                webUrls,
                webSearchEnabled,
                cancellationToken,
                source,
                sessionKey,
                threadBindingKey,
                requestedSkillName,
                requestedSkillScope
            )
            : new InputPreparationResult((input ?? string.Empty).Trim(), string.Empty);
        var normalizedAttachments = NormalizeAttachments(attachments);
        var nonTextAttachments = normalizedAttachments
            .Where(item => !IsTextLikeAttachment(item))
            .ToArray();
        if (nonTextAttachments.Length == 0)
        {
            return shared;
        }

        var resolvedProvider = NormalizeProvider(provider, allowAuto: false);
        var resolvedModel = ResolveModel(resolvedProvider, model);
        var summaryPrompt = BuildAttachmentSummaryPrompt(input ?? string.Empty, nonTextAttachments);
        string summary;
        var attachmentProvider = resolvedProvider;
        var attachmentModel = resolvedModel;
        var canSelectedProviderHandleAttachments = CanProviderHandleAttachments(resolvedProvider, resolvedModel, nonTextAttachments);
        if (!canSelectedProviderHandleAttachments)
        {
            if (_llmRouter.HasGeminiApiKey())
            {
                attachmentProvider = "gemini";
                attachmentModel = ResolveModel("gemini", null);
            }
            else if (_llmRouter.HasGroqApiKey() && nonTextAttachments.All(IsImageAttachment))
            {
                attachmentProvider = "groq";
                attachmentModel = DefaultGroqPrimaryModel;
            }
            else
            {
                return new InputPreparationResult(
                    shared.Text,
                    $"현재 선택 모델({resolvedProvider}:{resolvedModel})은 이미지/파일을 확인할 수 없습니다. Gemini 또는 Groq vision 모델 API 키가 있으면 첨부 요약 후 전달할 수 있습니다.",
                    shared.GuardFailure,
                    shared.Citations,
                    shared.RetryAttempt,
                    shared.RetryMaxAttempts,
                    shared.RetryStopReason
                );
            }
        }

        if (attachmentProvider == "gemini")
        {
            summary = await _llmRouter.GenerateGeminiMultimodalChatAsync(
                summaryPrompt,
                attachmentModel,
                nonTextAttachments,
                Math.Min(_config.ChatMaxOutputTokens, 1400),
                cancellationToken
            );
        }
        else if (attachmentProvider == "groq")
        {
            summary = await _llmRouter.GenerateGroqMultimodalChatAsync(
                summaryPrompt,
                attachmentModel,
                nonTextAttachments,
                Math.Min(_config.ChatMaxOutputTokens, 1400),
                cancellationToken
            );
        }
        else
        {
            return new InputPreparationResult(
                shared.Text,
                $"현재 선택 모델({resolvedProvider}:{resolvedModel})은 이미지/파일을 확인할 수 없습니다.",
                shared.GuardFailure,
                shared.Citations,
                shared.RetryAttempt,
                shared.RetryMaxAttempts,
                shared.RetryStopReason
            );
        }

        var cleanedSummary = SanitizeChatOutput(summary);
        if (string.IsNullOrWhiteSpace(cleanedSummary))
        {
            cleanedSummary = "첨부 분석 결과를 생성하지 못했습니다.";
        }

        var merged = new StringBuilder();
        merged.AppendLine(shared.Text);
        merged.AppendLine();
        merged.AppendLine("[첨부 이미지/파일 분석 요약]");
        if (!canSelectedProviderHandleAttachments)
        {
            merged.AppendLine($"선택 모델({resolvedProvider}:{resolvedModel})이 첨부를 직접 볼 수 없어 {attachmentProvider}:{attachmentModel}로 먼저 요약했습니다.");
        }
        merged.AppendLine(cleanedSummary);
        return new InputPreparationResult(
            merged.ToString().Trim(),
            string.Empty,
            shared.GuardFailure,
            shared.Citations,
            shared.RetryAttempt,
            shared.RetryMaxAttempts,
            shared.RetryStopReason
        );
    }

    private async Task<(
        string ContextBlock,
        string TraceMessage,
        SearchAnswerGuardFailure? GuardFailure,
        IReadOnlyList<SearchCitationReference> Citations,
        int RetryAttempt,
        int RetryMaxAttempts,
        string RetryStopReason
    )> BuildForcedRetrievalContextBlockAsync(
        string input,
        string source,
        string? sessionKey,
        string? threadBindingKey,
        string requestId,
        CancellationToken cancellationToken
    )
    {
        var query = (input ?? string.Empty).Trim();
        var sessionThreadBinding = TryExtractSessionThreadBinding(sessionKey);
        var normalizedSessionThread = NormalizeAuditToken(sessionThreadBinding, "-");
        var normalizedThreadBinding = NormalizeAuditToken(threadBindingKey, "-");
        var bindingStatus = ResolveThreadBindingStatus(
            sessionThreadBinding,
            threadBindingKey
        );
        var memorySearchTrace = CreateForcedToolTrace("skip", skipReason: "not_executed");
        var memoryGetTrace = CreateForcedToolTrace("skip", skipReason: "not_executed");
        var webSearchTrace = CreateForcedToolTrace("skip", skipReason: "not_executed");
        var webFetchTrace = CreateForcedToolTrace("skip", skipReason: "not_executed");
        var freshnessTrace = "na";
        SearchAnswerGuardFailure? webSearchGuardFailure = null;
        IReadOnlyList<SearchCitationReference> webSearchCitations = Array.Empty<SearchCitationReference>();
        var retryAttempt = 0;
        var retryMaxAttempts = 0;
        var retryStopReason = "-";
        var rawSessionScope = TryExtractSessionScope(sessionKey);
        var normalizedMemoryScope = NormalizeMemoryScopeForForcedContext(rawSessionScope);
        var allowedConversationIds = BuildScopedConversationIdSet(normalizedMemoryScope);
        if (string.IsNullOrWhiteSpace(query))
        {
            memorySearchTrace = CreateForcedToolTrace("skip", skipReason: "empty_query");
            memoryGetTrace = CreateForcedToolTrace("skip", skipReason: "empty_query");
            webSearchTrace = CreateForcedToolTrace("skip", skipReason: "empty_query");
            webFetchTrace = CreateForcedToolTrace("skip", skipReason: "empty_query");
            return (
                string.Empty,
                BuildForcedContextTraceMessage(
                    requestId,
                    source,
                    sessionKey,
                    normalizedThreadBinding,
                    normalizedSessionThread,
                    bindingStatus,
                    freshnessTrace,
                    memorySearchTrace,
                    memoryGetTrace,
                    webSearchTrace,
                    webFetchTrace,
                    "-"
                ),
                null,
                Array.Empty<SearchCitationReference>(),
                retryAttempt,
                retryMaxAttempts,
                "empty_query"
            );
        }

        var sections = new List<string>();
        MemoryGetToolResult? memoryGet = null;
        var forceMemoryContext = ShouldUseForcedMemoryContext(query);

        if (LooksLikeCasualOrIdentityQuestion(query))
        {
            memorySearchTrace = CreateForcedToolTrace("skip", skipReason: "casual_query");
            memoryGetTrace = CreateForcedToolTrace("skip", skipReason: "casual_query");
        }
        else
        {
            var memorySearch = _memorySearchTool.Search(query, maxResults: 4, minScore: ResolveForcedMemoryMinScore(query));
            var scopedMemoryResults = FilterMemorySearchResultsByScope(memorySearch.Results, normalizedMemoryScope, allowedConversationIds);
            if (memorySearch.Disabled)
            {
                memorySearchTrace = CreateForcedToolTrace(
                    "disabled",
                    detail: TrimForAudit(memorySearch.Error, 120)
                );
            }
            else
            {
                memorySearchTrace = CreateForcedToolTrace(
                    "ok",
                    result: $"{scopedMemoryResults.Count.ToString(CultureInfo.InvariantCulture)}/{memorySearch.Results.Count.ToString(CultureInfo.InvariantCulture)}"
                );
                var useSearchHits = forceMemoryContext || HasRelevantMemorySearchResults(query, scopedMemoryResults);
                if (useSearchHits && scopedMemoryResults.Count > 0)
                {
                    sections.Add(BuildMemorySearchContextBlock(scopedMemoryResults));
                }

                var topMemoryHit = useSearchHits
                    ? scopedMemoryResults.FirstOrDefault()
                    : null;
                if (topMemoryHit != null)
                {
                    var lineWindow = Math.Clamp(topMemoryHit.EndLine - topMemoryHit.StartLine + 4, 6, 28);
                    memoryGet = _memoryGetTool.Get(topMemoryHit.Path, topMemoryHit.StartLine, lineWindow);
                    if (memoryGet.Disabled)
                    {
                        memoryGetTrace = CreateForcedToolTrace(
                            "disabled",
                            detail: TrimForAudit(memoryGet.Error, 120)
                        );
                    }
                    else if (string.IsNullOrWhiteSpace(memoryGet.Text))
                    {
                        memoryGetTrace = CreateForcedToolTrace(
                            "ok",
                            result: "0",
                            detail: TrimForAudit(
                                $"{topMemoryHit.Path}{FormatMemoryLineRange(topMemoryHit.StartLine, topMemoryHit.EndLine)}",
                                120
                            )
                        );
                    }
                    else
                    {
                        memoryGetTrace = CreateForcedToolTrace(
                            "ok",
                            result: "1",
                            detail: TrimForAudit(
                                $"{topMemoryHit.Path}{FormatMemoryLineRange(topMemoryHit.StartLine, topMemoryHit.EndLine)}",
                                120
                            )
                        );
                        sections.Add(BuildMemoryGetContextBlock(topMemoryHit, memoryGet));
                    }
                }
                else if (memoryGetTrace["skipReason"] == "-")
                {
                    memoryGetTrace = CreateForcedToolTrace(
                        "skip",
                        skipReason: scopedMemoryResults.Count == 0 ? "no_hit" : "unrelated"
                    );
                }
            }
        }

        var fallbackNote = forceMemoryContext
            ? ResolveFallbackMemoryNoteForScope(normalizedMemoryScope)
            : null;
        if (fallbackNote == null)
        {
            if (memoryGetTrace["skipReason"] == "-")
            {
                memoryGetTrace = CreateForcedToolTrace("skip", skipReason: "no_hit_no_note");
            }
        }
        else if (sections.Count == 0) // 메모리 히트가 없었을 때만 fallback note 적용
        {
            var fallbackPath = $"memory-notes/{fallbackNote.Name}";
            memoryGet = _memoryGetTool.Get(fallbackPath, 1, 16);
            if (memoryGet.Disabled)
            {
                memoryGetTrace = CreateForcedToolTrace(
                    "disabled",
                    detail: TrimForAudit($"fallback:{memoryGet.Error}", 120)
                );
            }
            else if (string.IsNullOrWhiteSpace(memoryGet.Text))
            {
                memoryGetTrace = CreateForcedToolTrace(
                    "ok",
                    result: "0",
                    detail: TrimForAudit(fallbackPath, 120)
                );
            }
            else
            {
                memoryGetTrace = CreateForcedToolTrace(
                    "ok",
                    result: "1",
                    detail: TrimForAudit(fallbackPath, 120)
                );
                sections.Add(
                    "[memory_get]\n"
                    + $"path: {fallbackPath}\n"
                    + TrimForForcedContext(memoryGet.Text, 900)
                );
            }
        }

        var webSearchDecision = await DecideWebSearchRequirementAsync(
            query,
            cancellationToken
        );
        freshnessTrace = webSearchDecision.DecisionLabel;
        if (!webSearchDecision.Required)
        {
            webSearchTrace = CreateForcedToolTrace("skip", skipReason: "llm_not_required");
            webFetchTrace = CreateForcedToolTrace("skip", skipReason: "llm_not_required");
            retryStopReason = "llm_not_required";
        }
        else
        {
            var freshness = ResolveSearchFreshnessForQuery(query);
            var requestedSearchCount = ResolveRequestedResultCountFromQuery(query);
            var effectiveSearchQuery = BuildEffectiveSearchQuery(query, webSearchDecision);
            WebSearchToolResult webSearch;
            try
            {
                if (_config.EnableFastWebPipeline)
                {
                    webSearch = await SearchWebAsync(
                        effectiveSearchQuery,
                        requestedSearchCount,
                        freshness,
                        cancellationToken,
                        source: source
                    );
                }
                else
                {
                    var searchTimeoutSeconds = Math.Clamp(_config.LlmTimeoutSec + 8, 12, 40);
                    using var searchTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    searchTimeoutCts.CancelAfter(TimeSpan.FromSeconds(searchTimeoutSeconds));
                    webSearch = await SearchWebAsync(
                        effectiveSearchQuery,
                        requestedSearchCount,
                        freshness,
                        searchTimeoutCts.Token,
                        source: source
                    );
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                webSearch = new WebSearchToolResult(
                    Provider: "gemini_grounding",
                    Results: Array.Empty<WebSearchResultItem>(),
                    Disabled: true,
                    Error: "web_search_timeout",
                    RetryAttempt: 1,
                    RetryMaxAttempts: 1,
                    RetryStopReason: "web_search_timeout"
                );
            }

            webSearchGuardFailure = webSearch.GuardFailure;
            retryAttempt = Math.Max(0, webSearch.RetryAttempt);
            retryMaxAttempts = Math.Max(0, webSearch.RetryMaxAttempts);
            retryStopReason = string.IsNullOrWhiteSpace(webSearch.RetryStopReason)
                ? "-"
                : webSearch.RetryStopReason;
            if (webSearch.Disabled)
            {
                webSearchTrace = CreateForcedToolTrace(
                    "disabled",
                    detail: TrimForAudit(webSearch.Error, 120),
                    guardCategory: webSearch.GuardFailure?.Category.ToString(),
                    guardReason: webSearch.GuardFailure?.ReasonCode,
                    guardDetail: webSearch.GuardFailure?.Detail
                );
                webFetchTrace = CreateForcedToolTrace(
                    "skip",
                    skipReason: webSearch.Error?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true
                        ? "search_timeout"
                        : "search_disabled"
                );
            }
            else
            {
                IReadOnlyList<WebSearchResultItem> contextAlignedResults;
                var alignTimedOut = false;
                try
                {
                    using var alignTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var alignTimeoutSeconds = _config.EnableFastWebPipeline
                        ? Math.Clamp((_config.LlmTimeoutSec / 2), 3, 6)
                        : Math.Clamp(_config.LlmTimeoutSec, 8, 24);
                    alignTimeoutCts.CancelAfter(TimeSpan.FromSeconds(alignTimeoutSeconds));
                    contextAlignedResults = await BuildContextAlignedWebResultsAsync(
                        query,
                        source,
                        freshness,
                        requestedSearchCount,
                        webSearchDecision.SourceFocus,
                        webSearchDecision.SourceDomain,
                        webSearch.Results,
                        alignTimeoutCts.Token
                    );
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    alignTimedOut = true;
                    contextAlignedResults = webSearch.Results
                        .Take(Math.Clamp(requestedSearchCount, 1, 10))
                        .ToArray();
                }

                if (contextAlignedResults.Count < requestedSearchCount && webSearch.Results.Count > contextAlignedResults.Count)
                {
                    contextAlignedResults = MergeWebSearchItemsByUrl(
                        contextAlignedResults,
                        webSearch.Results,
                        Math.Clamp(requestedSearchCount, 1, 10)
                    );
                }

                webSearchTrace = CreateForcedToolTrace(
                    "ok",
                    result: contextAlignedResults.Count.ToString(CultureInfo.InvariantCulture),
                    detail: alignTimedOut ? "align_timeout_fallback" : "-"
                );
                if (contextAlignedResults.Count > 0)
                {
                    webSearchCitations = BuildSearchCitationReferences(contextAlignedResults);
                    sections.Add(BuildWebSearchContextBlock(freshness, contextAlignedResults));
                    sections.Add(BuildWebAnswerFormattingContextBlock(query, requestedSearchCount));
                }

                webFetchTrace = CreateForcedToolTrace("skip", skipReason: "disabled_for_latency");
            }
        }

        var contextBlock = sections.Count == 0
            ? string.Empty
            : "[강제 메모리/RAG/GeminiSearch]\n" + string.Join("\n\n", sections);
        return (
            contextBlock,
            BuildForcedContextTraceMessage(
                requestId,
                source,
                sessionKey,
                normalizedThreadBinding,
                normalizedSessionThread,
                bindingStatus,
                freshnessTrace,
                memorySearchTrace,
                memoryGetTrace,
                webSearchTrace,
                webFetchTrace,
                "-"
            ),
            webSearchGuardFailure,
            webSearchCitations,
            retryAttempt,
            retryMaxAttempts,
            retryStopReason
        );
    }

    private static bool ShouldUseForcedMemoryContext(string input)
    {
        return ShouldUsePriorConversationContext(input, out _)
            || ContainsAny(
                (input ?? string.Empty).Trim().ToLowerInvariant(),
                "rag",
                "메모리",
                "memory",
                "기억",
                "컨텍스트",
                "context",
                "노트",
                "note",
                "이전 대화",
                "대화 기록",
                "최근 대화");
    }

    private string BuildNotebookPromptContext(string input, string? sessionKey)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        var scope = TryExtractSessionScope(sessionKey);
        var shouldUseNotebook =
            scope == "coding"
            || ContainsAny(
                normalized,
                "노트북",
                "notebook",
                "handoff",
                "인수인계",
                "이전 결정",
                "결정한 것",
                "검증한 것",
                "이어",
                "계속",
                "방금",
                "전에",
                "아까"
            );
        if (!shouldUseNotebook)
        {
            return string.Empty;
        }

        try
        {
            return _notebookService.BuildContextBlock();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool HasRelevantMemorySearchResults(
        string query,
        IReadOnlyList<MemorySearchCitationResult> results
    )
    {
        var top = results.FirstOrDefault();
        if (top == null)
        {
            return false;
        }

        if (top.Score >= 0.62d)
        {
            return true;
        }

        if (top.Score < 0.45d)
        {
            return false;
        }

        var queryTokens = ExtractContextTokens(query);
        var resultTokens = ExtractContextTokens($"{top.Path}\n{top.Snippet}");
        return HasMeaningfulTokenOverlap(queryTokens, resultTokens);
    }

    private static bool LooksLikeProjectContextRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        return ContainsAny(
            normalized,
            "agents.md",
            "agent.md",
            "agants.md",
            "프로젝트 지침",
            "프로젝트 컨텍스트",
            "project context",
            "스킬",
            "skill",
            "skills",
            "skill.md");
    }

    private static bool LooksLikeSkillCreationRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var creationPattern =
            @"(?i)(((새|신규|새로운|new)\s+)?(스킬|skill|skills)\s*(을|를)?\s*(만들|만들어|생성|등록|추가|작성)|" +
            @"(create|make|add|register|build|write)\s+(a\s+|an\s+|new\s+)?(skill|skills))";
        var usagePattern = @"(?i)(스킬|skill|skills)\s*(을|를)?\s*(사용|적용|활성|켜|on)\s*(해서|하여|하고)?";
        var creationMatched = Regex.IsMatch(normalized, creationPattern);

        if (Regex.IsMatch(normalized, usagePattern) && !creationMatched)
        {
            return false;
        }

        return creationMatched;
    }

    private static bool LooksLikeSkillDeactivationRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(
            normalized,
            @"(?i)((스킬|skill|skills)\s*(을|를|은|는)?\s*(그만|종료|중지|해제|빼|꺼|끄|off|stop|disable|deactivate|exit|end)|" +
            @"(stop|disable|deactivate|turn\s+off|exit|end)\s+(the\s+)?(skill|skills)|" +
            @"(스킬|skill)\s*(끝|그만)|" +
            @"일반\s*(대화|모드)\s*(로|으로)?\s*(돌아|복귀|전환))"
        );
    }

    private static string BuildMemorySearchContextBlock(IReadOnlyList<MemorySearchCitationResult> results)
    {
        var lines = results
            .Take(3)
            .Select((entry, index) =>
                $"{index + 1}. {entry.Path}{FormatMemoryLineRange(entry.StartLine, entry.EndLine)} "
                + $"score={entry.Score.ToString("0.###", CultureInfo.InvariantCulture)} "
                + $"{TrimForForcedContext(entry.Snippet, 260)}"
            );
        return "[memory_search]\n" + string.Join("\n", lines);
    }

    private static string BuildMemoryGetContextBlock(
        MemorySearchCitationResult citation,
        MemoryGetToolResult memoryGet
    )
    {
        return "[memory_get]\n"
            + $"path: {citation.Path}{FormatMemoryLineRange(citation.StartLine, citation.EndLine)}\n"
            + TrimForForcedContext(memoryGet.Text, 900);
    }

    private static string BuildWebSearchContextBlock(string freshness, IReadOnlyList<WebSearchResultItem> results)
    {
        var normalizedFreshness = string.IsNullOrWhiteSpace(freshness) ? "week" : freshness.Trim();
        var lines = new List<string>(Math.Min(10, results.Count));
        var itemNo = 0;
        foreach (var item in results)
        {
            if (itemNo >= 10)
            {
                break;
            }

            if (!TryNormalizeDisplaySourceUrl(item.Url, out var sourceUrl))
            {
                continue;
            }

            itemNo += 1;
            var citationId = NormalizeCitationId(item.CitationId, itemNo);
            var published = string.IsNullOrWhiteSpace(item.Published) ? "-" : item.Published.Trim();
            var sourceLabel = ResolveSourceLabel(sourceUrl, item.Title);
            lines.Add(
                $"{itemNo}. [{citationId}] {TrimForForcedContext(item.Title, 120)} | {published}\n"
                + $"source: {sourceLabel}\n"
                + $"desc: {TrimForForcedContext(item.Description, 220)}"
            );
        }

        return $"[web_search freshness={normalizedFreshness}]\n" + string.Join("\n", lines);
    }

    private static string BuildWebAnswerFormattingContextBlock(string query, int requestedCount)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        var loweredQuery = normalizedQuery.ToLowerInvariant();
        var normalizedRequestedCount = Math.Clamp(requestedCount, 1, 10);
        var tableMode = LooksLikeTableRenderRequest(normalizedQuery);
        var listMode = LooksLikeListOutputRequest(normalizedQuery);
        var newsMode = ContainsAny(loweredQuery, "뉴스", "news", "헤드라인", "속보", "브리핑");
        return "[response_format_rule]\n"
            + "- 아래 web_search 항목에 있는 사실만 사용해 답변하세요.\n"
            + "- web_search 항목에 없는 추정/기억 기반 내용은 작성하지 마세요.\n"
            + "- 같은 제목/내용을 반복하거나 제목만 먼저 몰아서 나열하지 마세요.\n"
            + "- '정리했습니다', '다음과 같습니다' 같은 서론 문장과 날짜 설명문을 앞에 붙이지 마세요.\n"
            + "- URL을 직접 노출하지 말고, 출처는 매체명만 작성하세요.\n"
            + (tableMode
                ? $"- 사용자가 표를 요청했으므로 정확히 {normalizedRequestedCount}개 행의 GitHub 마크다운 표만 출력하세요.\n"
                    + "- 표 바깥에 제목 나열, 불릿, 추가 설명 문단을 쓰지 마세요.\n"
                    + "- 표 헤더는 정확히 '| 번호 | 뉴스 제목 | 핵심 요약 | 출처 |' 로 작성하세요.\n"
                    + "- 각 셀은 한 줄로 짧게 작성하세요.\n"
                : listMode
                    ? $"- 사용자가 목록을 요청했으므로 정확히 {normalizedRequestedCount}개 항목만 작성하세요.\n"
                        + "- 번호는 1부터 순서대로 작성하세요.\n"
                        + "- 각 항목은 아래 3줄 형식만 사용하세요.\n"
                        + "  1. 뉴스 제목\n"
                        + "  - 핵심: 한 줄 요약\n"
                        + "  - 출처: 매체명\n"
                    : newsMode
                        ? "- 뉴스 요약 요청이면 핵심만 간결하게 줄바꿈해 정리하세요.\n"
                        : "- 사용자가 목록/표를 명시하지 않았다면 번호 목록을 강제하지 마세요.\n");
    }

    private static string NormalizeMemoryScopeForForcedContext(string? scope)
    {
        var normalized = NormalizeAuditToken(scope, string.Empty);
        if (normalized == "telegram")
        {
            return "chat";
        }

        return normalized switch
        {
            "chat" => "chat",
            "coding" => "coding",
            _ => "unknown"
        };
    }

    private static string? TryExtractSessionScope(string? sessionKey)
    {
        var normalized = (sessionKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var parts = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !parts[0].Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts[2].Trim().ToLowerInvariant();
    }

    private IReadOnlySet<string> BuildScopedConversationIdSet(string normalizedScope)
    {
        if (normalizedScope != "chat" && normalizedScope != "coding")
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in new[] { "single", "orchestration", "multi" })
        {
            foreach (var item in _conversationStore.List(normalizedScope, mode))
            {
                if (!string.IsNullOrWhiteSpace(item.Id))
                {
                    ids.Add(item.Id.Trim());
                }
            }
        }

        return ids;
    }

    private static bool IsScopedConversationPath(string path, IReadOnlySet<string> allowedConversationIds)
    {
        if (allowedConversationIds.Count == 0)
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (allowedConversationIds.Contains(fileName))
        {
            return true;
        }

        var pivot = fileName.LastIndexOf('_');
        if (pivot <= 0 || pivot >= fileName.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(fileName[(pivot + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        return allowedConversationIds.Contains(fileName[..pivot]);
    }

    private IReadOnlyList<MemorySearchCitationResult> FilterMemorySearchResultsByScope(
        IReadOnlyList<MemorySearchCitationResult> results,
        string normalizedScope,
        IReadOnlySet<string> allowedConversationIds
    )
    {
        if (results.Count == 0 || (normalizedScope != "chat" && normalizedScope != "coding"))
        {
            return results;
        }

        var marker = $"_{normalizedScope}-";
        return results
            .Where(item =>
            {
                var path = (item.Path ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                if (path.StartsWith("memory-notes/", StringComparison.OrdinalIgnoreCase))
                {
                    var noteName = Path.GetFileName(path);
                    return noteName.Contains(marker, StringComparison.OrdinalIgnoreCase);
                }

                if (path.StartsWith("conversations/", StringComparison.OrdinalIgnoreCase))
                {
                    return IsScopedConversationPath(path, allowedConversationIds);
                }

                return false;
            })
            .ToArray();
    }

    private MemoryNoteItem? ResolveFallbackMemoryNoteForScope(string normalizedScope)
    {
        var notes = _memoryNoteStore.List();
        if (notes.Count == 0)
        {
            return null;
        }

        if (normalizedScope != "chat" && normalizedScope != "coding")
        {
            return notes[0];
        }

        var marker = $"_{normalizedScope}-";
        return notes.FirstOrDefault(item =>
            item.Name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatMemoryLineRange(int startLine, int endLine)
    {
        var safeStart = Math.Max(1, startLine);
        var safeEnd = Math.Max(safeStart, endLine);
        return safeStart == safeEnd
            ? $"#L{safeStart}"
            : $"#L{safeStart}-L{safeEnd}";
    }

    private static string TrimForForcedContext(string? text, int maxChars)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxChars)] + "...";
    }

    private static string TrimForAudit(string? text, int maxChars)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        return normalized.Length <= maxChars
            ? normalized
            : normalized[..Math.Max(0, maxChars)] + "...";
    }

    private static string BuildForcedContextRequestId()
    {
        return $"fc-{Guid.NewGuid():N}";
    }

    private static IReadOnlyDictionary<string, string> CreateForcedToolTrace(
        string status,
        string? skipReason = null,
        string? result = null,
        string? detail = null,
        string? guardCategory = null,
        string? guardReason = null,
        string? guardDetail = null
    )
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["status"] = NormalizeForcedToolStatus(status),
            ["skipReason"] = NormalizeForcedSkipReason(skipReason),
            ["result"] = NormalizeForcedToolValue(result, "-"),
            ["detail"] = NormalizeForcedToolValue(detail, "-"),
            ["guardCategory"] = NormalizeForcedGuardCategory(guardCategory),
            ["guardReason"] = NormalizeForcedGuardReason(guardReason),
            ["guardDetail"] = NormalizeForcedToolValue(guardDetail, "-")
        };
    }

    private static string BuildForcedContextTraceMessage(
        string requestId,
        string source,
        string? sessionKey,
        string threadBinding,
        string sessionThread,
        string threadBindingStatus,
        string freshness,
        IReadOnlyDictionary<string, string> memorySearch,
        IReadOnlyDictionary<string, string> memoryGet,
        IReadOnlyDictionary<string, string> webSearch,
        IReadOnlyDictionary<string, string> webFetch,
        string error
    )
    {
        var builder = new StringBuilder(720);
        builder.Append('{');
        AppendForcedTraceField(builder, "schema", "forced_context.v1", isFirst: true);
        AppendForcedTraceField(builder, "requestId", NormalizeAuditToken(requestId, "fc-unknown"));
        AppendForcedTraceField(builder, "source", NormalizeAuditToken(source, "web"));
        AppendForcedTraceField(builder, "sessionKey", NormalizeAuditToken(sessionKey, "-"));
        AppendForcedTraceField(builder, "threadBinding", NormalizeAuditToken(threadBinding, "-"));
        AppendForcedTraceField(builder, "sessionThread", NormalizeAuditToken(sessionThread, "-"));
        AppendForcedTraceField(builder, "threadBindingStatus", NormalizeAuditToken(threadBindingStatus, "na"));
        AppendForcedTraceField(builder, "freshness", NormalizeAuditToken(freshness, "na"));
        builder.Append(",\"tools\":{");
        AppendForcedToolTrace(builder, "memory_search", memorySearch, isFirst: true);
        AppendForcedToolTrace(builder, "memory_get", memoryGet, isFirst: false);
        AppendForcedToolTrace(builder, "web_search", webSearch, isFirst: false);
        AppendForcedToolTrace(builder, "web_fetch", webFetch, isFirst: false);
        builder.Append('}');
        AppendForcedTraceField(builder, "error", NormalizeForcedToolValue(error, "-"));
        builder.Append('}');
        return builder.ToString();
    }

    private static string NormalizeForcedToolStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "ok" or "disabled" or "skip" or "error" => normalized,
            _ => "error"
        };
    }

    private static string NormalizeForcedSkipReason(string? reason)
    {
        var normalized = (reason ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static string NormalizeForcedToolValue(string? value, string fallback)
    {
        var normalized = TrimForAudit(value, 140);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string NormalizeForcedGuardCategory(string? category)
    {
        var normalized = (category ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "coverage" or "freshness" or "credibility" => normalized,
            _ => "-"
        };
    }

    private static string NormalizeForcedGuardReason(string? reason)
    {
        var normalized = (reason ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static void AppendForcedTraceField(
        StringBuilder builder,
        string key,
        string value,
        bool isFirst = false
    )
    {
        if (!isFirst)
        {
            builder.Append(',');
        }

        builder.Append('"')
            .Append(EscapeForcedTraceJson(key))
            .Append("\":\"")
            .Append(EscapeForcedTraceJson(value))
            .Append('"');
    }

    private static void AppendForcedToolTrace(
        StringBuilder builder,
        string toolName,
        IReadOnlyDictionary<string, string> trace,
        bool isFirst
    )
    {
        if (!isFirst)
        {
            builder.Append(',');
        }

        var status = NormalizeForcedToolStatus(ReadForcedToolTraceField(trace, "status", "error"));
        var skipReason = NormalizeForcedSkipReason(ReadForcedToolTraceField(trace, "skipReason", "-"));
        var result = NormalizeForcedToolValue(ReadForcedToolTraceField(trace, "result", "-"), "-");
        var detail = NormalizeForcedToolValue(ReadForcedToolTraceField(trace, "detail", "-"), "-");
        var guardCategory = NormalizeForcedGuardCategory(ReadForcedToolTraceField(trace, "guardCategory", "-"));
        var guardReason = NormalizeForcedGuardReason(ReadForcedToolTraceField(trace, "guardReason", "-"));
        var guardDetail = NormalizeForcedToolValue(ReadForcedToolTraceField(trace, "guardDetail", "-"), "-");

        builder.Append('"')
            .Append(EscapeForcedTraceJson(toolName))
            .Append("\":{");
        AppendForcedTraceField(builder, "status", status, isFirst: true);
        AppendForcedTraceField(builder, "skipReason", skipReason);
        AppendForcedTraceField(builder, "result", result);
        AppendForcedTraceField(builder, "detail", detail);
        AppendForcedTraceField(builder, "guardCategory", guardCategory);
        AppendForcedTraceField(builder, "guardReason", guardReason);
        AppendForcedTraceField(builder, "guardDetail", guardDetail);
        builder.Append('}');
    }

    private static string ReadForcedToolTraceField(
        IReadOnlyDictionary<string, string> trace,
        string key,
        string fallback
    )
    {
        if (trace.TryGetValue(key, out var value))
        {
            var normalized = (value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return fallback;
    }

    private static string EscapeForcedTraceJson(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string NormalizeAuditToken(string? token, string fallback)
    {
        var normalized = (token ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized.ToLowerInvariant();
    }

    private static string? TryExtractSessionThreadBinding(string? sessionKey)
    {
        var normalized = (sessionKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var parts = normalized
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !parts[0].Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (parts.Length >= 5)
        {
            return string.Join(":", parts.Skip(4)).Trim().ToLowerInvariant();
        }

        return parts[^1].Trim().ToLowerInvariant();
    }

    private static string ResolveThreadBindingStatus(
        string? sessionThreadBinding,
        string? requestedThreadBinding
    )
    {
        var normalizedSessionThread = NormalizeAuditToken(sessionThreadBinding, string.Empty);
        var normalizedRequestedBinding = NormalizeAuditToken(requestedThreadBinding, string.Empty);
        var hasSessionThread = !string.IsNullOrWhiteSpace(normalizedSessionThread);
        var hasRequestedBinding = !string.IsNullOrWhiteSpace(normalizedRequestedBinding);
        if (!hasSessionThread && !hasRequestedBinding)
        {
            return "na";
        }

        if (hasSessionThread && !hasRequestedBinding)
        {
            return "session_only";
        }

        if (!hasSessionThread && hasRequestedBinding)
        {
            return "missing_session";
        }

        return string.Equals(normalizedSessionThread, normalizedRequestedBinding, StringComparison.Ordinal)
            ? "match"
            : "mismatch";
    }

    private static IReadOnlyList<InputAttachment> NormalizeAttachments(IReadOnlyList<InputAttachment>? attachments)
    {
        if (attachments == null || attachments.Count == 0)
        {
            return Array.Empty<InputAttachment>();
        }

        var list = new List<InputAttachment>(attachments.Count);
        foreach (var item in attachments)
        {
            var name = (item.Name ?? string.Empty).Trim();
            var mimeType = string.IsNullOrWhiteSpace(item.MimeType) ? "application/octet-stream" : item.MimeType.Trim();
            var base64 = (item.DataBase64 ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(base64))
            {
                continue;
            }

            list.Add(new InputAttachment(name, mimeType, base64, item.SizeBytes, item.IsImage));
            if (list.Count >= 6)
            {
                break;
            }
        }

        return list.ToArray();
    }

    private static bool CanProviderHandleAttachments(
        string provider,
        string model,
        IReadOnlyList<InputAttachment> nonTextAttachments
    )
    {
        if (nonTextAttachments.Count == 0)
        {
            return true;
        }

        if (provider == "gemini")
        {
            return true;
        }

        if (provider == "groq")
        {
            if (!SupportsGroqVisionModel(model))
            {
                return false;
            }

            return nonTextAttachments.All(IsImageAttachment);
        }

        return false;
    }

    private static bool SupportsGroqVisionModel(string model)
    {
        var normalized = (model ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Contains("llama-4-scout", StringComparison.Ordinal)
               || normalized.Contains("llama-4-maverick", StringComparison.Ordinal);
    }

    private static bool IsImageAttachment(InputAttachment attachment)
    {
        if (attachment.IsImage)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(attachment.MimeType)
            && attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = (attachment.Name ?? string.Empty).Trim().ToLowerInvariant();
        return name.EndsWith(".png", StringComparison.Ordinal)
               || name.EndsWith(".jpg", StringComparison.Ordinal)
               || name.EndsWith(".jpeg", StringComparison.Ordinal)
               || name.EndsWith(".webp", StringComparison.Ordinal)
               || name.EndsWith(".gif", StringComparison.Ordinal);
    }

    private static bool IsTextLikeAttachment(InputAttachment attachment)
    {
        var mime = (attachment.MimeType ?? string.Empty).Trim().ToLowerInvariant();
        if (mime.StartsWith("text/", StringComparison.Ordinal))
        {
            return true;
        }

        if (mime is "application/json" or "application/xml" or "text/csv" or "application/x-sh")
        {
            return true;
        }

        var name = (attachment.Name ?? string.Empty).Trim().ToLowerInvariant();
        return name.EndsWith(".txt", StringComparison.Ordinal)
               || name.EndsWith(".md", StringComparison.Ordinal)
               || name.EndsWith(".json", StringComparison.Ordinal)
               || name.EndsWith(".csv", StringComparison.Ordinal)
               || name.EndsWith(".log", StringComparison.Ordinal)
               || name.EndsWith(".yml", StringComparison.Ordinal)
               || name.EndsWith(".yaml", StringComparison.Ordinal)
               || name.EndsWith(".xml", StringComparison.Ordinal)
               || name.EndsWith(".ini", StringComparison.Ordinal)
               || name.EndsWith(".conf", StringComparison.Ordinal)
               || name.EndsWith(".cs", StringComparison.Ordinal)
               || name.EndsWith(".java", StringComparison.Ordinal)
               || name.EndsWith(".kt", StringComparison.Ordinal)
               || name.EndsWith(".js", StringComparison.Ordinal)
               || name.EndsWith(".ts", StringComparison.Ordinal)
               || name.EndsWith(".py", StringComparison.Ordinal)
               || name.EndsWith(".c", StringComparison.Ordinal)
               || name.EndsWith(".cpp", StringComparison.Ordinal)
               || name.EndsWith(".h", StringComparison.Ordinal)
               || name.EndsWith(".hpp", StringComparison.Ordinal)
               || name.EndsWith(".html", StringComparison.Ordinal)
               || name.EndsWith(".css", StringComparison.Ordinal)
               || name.EndsWith(".sh", StringComparison.Ordinal);
    }

    private static string BuildTextAttachmentBlock(IReadOnlyList<InputAttachment> attachments)
    {
        var textItems = attachments.Where(IsTextLikeAttachment).Take(3).ToArray();
        if (textItems.Length == 0)
        {
            return string.Empty;
        }

        var blocks = new List<string>(textItems.Length);
        foreach (var attachment in textItems)
        {
            if (!TryDecodeAttachmentText(attachment, out var content))
            {
                continue;
            }

            var trimmed = content.Length <= 2200 ? content : content[..2200] + "\n...(truncated)";
            var name = string.IsNullOrWhiteSpace(attachment.Name) ? "attachment" : attachment.Name;
            blocks.Add($"### {name}\n{trimmed}");
        }

        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        return "[첨부 텍스트 파일]\n" + string.Join("\n\n", blocks);
    }

    private static bool TryDecodeAttachmentText(InputAttachment attachment, out string content)
    {
        content = string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(attachment.DataBase64);
            if (bytes.Length == 0)
            {
                return false;
            }

            var text = Encoding.UTF8.GetString(bytes);
            text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
            text = text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            content = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ResolveWebUrls(string input, IReadOnlyList<string>? requestUrls, bool webSearchEnabled)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (requestUrls != null)
        {
            foreach (var raw in requestUrls)
            {
                var normalized = NormalizeWebUrl(raw);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    set.Add(normalized);
                }
            }
        }

        foreach (Match match in HttpUrlRegex.Matches(input ?? string.Empty))
        {
            var normalized = NormalizeWebUrl(match.Value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                set.Add(normalized);
            }
        }

        return set.Take(3).ToArray();
    }

    private static string NormalizeWebUrl(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return uri.AbsoluteUri;
    }

    private async Task<string> BuildWebContextBlockAsync(string input, IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            return string.Empty;
        }

        if (_llmRouter.HasGeminiApiKey())
        {
            var summaryPrompt = BuildGeminiUrlContextSummaryPrompt(input, urls);
            var summaryResponse = await _llmRouter.GenerateGeminiUrlContextChatAsync(
                summaryPrompt,
                ResolveUrlContextLlmModel(),
                maxOutputTokens: 768,
                _config.GeminiWebTimeoutMs,
                includeGoogleSearch: false,
                cancellationToken
            );
            if (!IsGeminiUrlContextFailureText(summaryResponse.Text))
            {
                var summary = SanitizeChatOutput(summaryResponse.Text);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    return "[URL 참조]\n" + summary.Trim();
                }
            }
        }

        var blocks = new List<string>();
        foreach (var url in urls.Take(3))
        {
            var snippet = await FetchWebSnippetAsync(url, cancellationToken);
            if (string.IsNullOrWhiteSpace(snippet))
            {
                continue;
            }

            blocks.Add($"### {url}\n{snippet}");
        }

        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        return "[웹 참조]\n" + string.Join("\n\n", blocks);
    }

    private string BuildGeminiUrlContextSummaryPrompt(string input, IReadOnlyList<string> urls)
    {
        var normalizedInput = ResolveImplicitUrlRequest((input ?? string.Empty).Trim(), urls);
        var builder = new StringBuilder();
        builder.AppendLine("너는 URL 컨텍스트 전처리 요약기다.");
        builder.AppendLine("- 제공된 URL 내용만 사용해 후속 LLM이 참고할 요약 블록을 만들어라.");
        builder.AppendLine("- 한국어.");
        builder.AppendLine("- 최대 8줄.");
        builder.AppendLine("- 군더더기 서론/결론/출처 링크 섹션 금지.");
        builder.AppendLine("- 사실, 수치, 핵심 규칙, 중요한 예제 위주로만 정리해라.");
        builder.AppendLine("- 각 줄은 독립적인 짧은 문장 또는 불릿으로 작성해라.");
        builder.AppendLine();
        builder.AppendLine("사용자 요청:");
        builder.AppendLine(normalizedInput.Length == 0 ? "이 URL 내용을 요약해줘." : normalizedInput);
        builder.AppendLine();
        builder.AppendLine("참조 URL:");
        foreach (var url in urls.Take(3))
        {
            builder.AppendLine($"- {url}");
        }

        return builder.ToString().Trim();
    }

    private async Task<string> FetchWebSnippetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Omni-node/1.0");
            using var response = await WebFetchClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            if (raw.Length > 24_000)
            {
                raw = raw[..24_000];
            }

            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                var titleMatch = HtmlTitleRegex.Match(raw);
                var title = titleMatch.Success ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim() : string.Empty;
                var stripped = HtmlTagStripRegex.Replace(raw, " ");
                stripped = WebUtility.HtmlDecode(stripped);
                stripped = Regex.Replace(stripped, @"\s{2,}", " ").Trim();
                if (stripped.Length > 1800)
                {
                    stripped = stripped[..1800] + "...";
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    return WrapWebFetchSnippet($"제목: {title}\n요약: {stripped}");
                }

                return WrapWebFetchSnippet(stripped);
            }

            var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim();
            if (normalized.Length > 1800)
            {
                normalized = normalized[..1800] + "...";
            }

            return WrapWebFetchSnippet(normalized);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string WrapWebFetchSnippet(string snippet)
    {
        var normalized = (snippet ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return ExternalContentGuard.WrapWebContent(normalized, ExternalContentSource.WebFetch);
    }

    private static string BuildAttachmentSummaryPrompt(string input, IReadOnlyList<InputAttachment> attachments)
    {
        var lines = attachments
            .Select((item, index) =>
            {
                var name = string.IsNullOrWhiteSpace(item.Name) ? $"attachment-{index + 1}" : item.Name;
                var mime = string.IsNullOrWhiteSpace(item.MimeType) ? "application/octet-stream" : item.MimeType;
                return $"- {name} ({mime}, {item.SizeBytes} bytes)";
            })
            .ToArray();
        return $"""
                첨부된 이미지/파일을 먼저 해석한 뒤 아래 사용자 요청을 처리하기 위한 핵심 정보만 요약하세요.
                출력 규칙:
                - 한국어
                - 최대 8줄
                - 관찰 사실/수치/텍스트를 우선
                - 불확실하면 추정이라고 명시

                [사용자 요청]
                {input}

                [첨부 목록]
                {string.Join("\n", lines)}
                """;
    }
}
