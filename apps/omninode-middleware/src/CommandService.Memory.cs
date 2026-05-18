namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public string ClearMemory(string? scope, string source = "web")
        => _memoryAppService.ClearMemory(scope, source);

    public IReadOnlyList<MemoryNoteItem> ListMemoryNotes()
        => _memoryAppService.ListMemoryNotes();

    public MemoryNoteReadResult? ReadMemoryNote(string name)
        => _memoryAppService.ReadMemoryNote(name);

    public (MemoryNoteRenameResult Result, int RelinkedConversations) RenameMemoryNote(string name, string newName)
        => _memoryAppService.RenameMemoryNote(name, newName);

    public MemoryNoteDeleteResult DeleteMemoryNotes(IReadOnlyList<string>? names)
        => _memoryAppService.DeleteMemoryNotes(names);

    public MemorySearchToolResult SearchMemory(string query, int? maxResults = null, double? minScore = null)
        => _memoryAppService.SearchMemory(query, maxResults, minScore);

    public MemoryGetToolResult GetMemory(string path, int? from = null, int? lines = null)
        => _memoryAppService.GetMemory(path, from, lines);

    public MemoryIndexRebuildResult RebuildMemoryIndex()
        => _memoryAppService.RebuildMemoryIndex();

    // CreateMemoryNoteAsync는 _telegramLlmPreferences / _webLlmPreferences private
    // 상태와 다른 partial 헬퍼(NormalizeProvider, ResolveAutoProviderAsync,
    // ResolveModel, GenerateByProviderSafeAsync, IsLikelyWorkerFailure)에 결합되어
    // 있어 partial에 그대로 유지. MemoryApplicationService는 이 메서드를 delegate
    // 형태로 호출함.
    public async Task<MemoryNoteCreateResult> CreateMemoryNoteAsync(
        string conversationId,
        string source,
        bool compactConversation,
        CancellationToken cancellationToken
    )
    {
        var targetConversationId = (conversationId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetConversationId))
        {
            return new MemoryNoteCreateResult(false, "conversationId가 필요합니다.", null, null);
        }

        var thread = _conversationStore.Get(targetConversationId);
        if (thread == null)
        {
            return new MemoryNoteCreateResult(false, "대화를 찾을 수 없습니다.", null, null);
        }

        var sourceLines = thread.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Text))
            .Select(message =>
            {
                var role = (message.Role ?? string.Empty).Trim().ToLowerInvariant();
                if (role != "user" && role != "assistant" && role != "system")
                {
                    role = "assistant";
                }

                return $"[{role}] {(message.Text ?? string.Empty).Trim()}";
            })
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (sourceLines.Length == 0)
        {
            return new MemoryNoteCreateResult(false, "메모리 노트로 저장할 대화 내용이 없습니다.", null, null);
        }

        var sourceText = string.Join('\n', sourceLines);
        if (sourceText.Length > 24_000)
        {
            sourceText = "[conversation_truncated]\n" + sourceText[^24_000..];
        }

        var normalizedSource = NormalizeAuditToken(source, "web");
        var preferredProvider = "auto";
        var preferredModel = string.Empty;
        if (normalizedSource.Equals("telegram", StringComparison.OrdinalIgnoreCase))
        {
            TelegramLlmPreferences snapshot;
            lock (_telegramLlmLock)
            {
                snapshot = _telegramLlmPreferences.Clone();
            }

            preferredProvider = snapshot.SingleProvider;
            preferredModel = snapshot.SingleModel;
        }
        else
        {
            WebLlmPreferences snapshot;
            lock (_webLlmLock)
            {
                snapshot = _webLlmPreferences.Clone();
            }

            preferredProvider = snapshot.SingleProvider;
            preferredModel = snapshot.SingleModel;
        }

        var provider = NormalizeProvider(preferredProvider, allowAuto: true);
        if (provider == "auto")
        {
            provider = await ResolveAutoProviderAsync(cancellationToken);
        }

        if (provider == "none")
        {
            if (_llmRouter.HasGeminiApiKey())
            {
                provider = "gemini";
            }
            else if (_llmRouter.HasGroqApiKey())
            {
                provider = "groq";
            }
            else if (_llmRouter.HasCerebrasApiKey())
            {
                provider = "cerebras";
            }
            else
            {
                var copilotStatus = await _copilotWrapper.GetStatusAsync(cancellationToken);
                provider = copilotStatus.Installed && copilotStatus.Authenticated ? "copilot" : "none";
            }
        }

        if (provider == "none")
        {
            return new MemoryNoteCreateResult(
                false,
                "사용 가능한 LLM이 없어 메모리 노트를 생성할 수 없습니다.",
                null,
                thread
            );
        }

        var model = ResolveModel(provider, preferredModel);
        var summaryPrompt = $"""
                            아래는 한 대화방의 전체 로그입니다.
                            나중에 컨텍스트로 재사용할 수 있도록 한국어 메모리 노트로 압축하세요.
                            형식 규칙:
                            - 불릿 중심
                            - 최대 25줄
                            - 추측 금지
                            - 포함 필수:
                              1) 사용자 목표
                              2) 확정된 결정/제약
                              3) 미해결 항목/다음 액션
                              4) 중요한 설정값/모델/경로

                            [대화 로그]
                            {sourceText}
                            """;

        var summaryResult = await GenerateByProviderSafeAsync(provider, model, summaryPrompt, cancellationToken, 1200);
        var summaryText = (summaryResult.Text ?? string.Empty).Trim();
        if (summaryText.Length == 0 || IsLikelyWorkerFailure(summaryResult.Provider, summaryText))
        {
            summaryText = sourceText.Length > 2400 ? sourceText[^2400..] : sourceText;
        }

        var modeKey = $"{thread.Scope}-{thread.Mode}";
        var saved = _memoryNoteStore.Save(
            modeKey,
            thread.Id,
            thread.Title,
            summaryResult.Provider,
            summaryResult.Model,
            summaryText
        );

        var linked = _conversationStore.AddLinkedMemoryNote(thread.Id, saved.Name);
        var updated = linked;
        if (compactConversation)
        {
            updated = _conversationStore.CompactWithSummary(
                thread.Id,
                _context.ConversationKeepRecentMessages,
                $"수동 압축 완료. 메모리 노트 `{saved.Name}` 를 컨텍스트로 사용합니다."
            );
        }

        var message = compactConversation
            ? $"메모리 노트 생성 및 압축 완료: {saved.Name}"
            : $"메모리 노트 생성 완료: {saved.Name}";
        _auditLogger.Log(
            normalizedSource,
            "memory_note_create",
            "ok",
            $"conversationId={NormalizeAuditToken(thread.Id, "-")} note={NormalizeAuditToken(saved.Name, "-")} compact={compactConversation}"
        );

        return new MemoryNoteCreateResult(true, message, saved, updated);
    }
}
