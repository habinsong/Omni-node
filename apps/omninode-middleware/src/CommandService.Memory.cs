namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public string ClearMemory(string? scope, string source = "web")
    {
        var normalized = NormalizeMemoryClearScope(scope);
        var conversationScope = normalized == "telegram" ? "chat" : normalized;

        var removedConversations = 0;
        if (conversationScope == "all")
        {
            removedConversations += _conversationStore.DeleteByScope("chat");
            removedConversations += _conversationStore.DeleteByScope("coding");
        }
        else
        {
            removedConversations = _conversationStore.DeleteByScope(conversationScope);
        }

        var removedNotes = conversationScope == "all"
            ? _memoryNoteStore.DeleteByScope("all")
            : _memoryNoteStore.DeleteByScope(conversationScope);

        var message = $"scope={normalized} conversations={removedConversations} notes={removedNotes}";
        _auditLogger.Log(source, "clear_memory", "ok", message);
        return message;
    }

    public IReadOnlyList<MemoryNoteItem> ListMemoryNotes()
    {
        return _memoryNoteStore.List();
    }

    public MemoryNoteReadResult? ReadMemoryNote(string name)
    {
        return _memoryNoteStore.Read(name);
    }

    public (MemoryNoteRenameResult Result, int RelinkedConversations) RenameMemoryNote(string name, string newName)
    {
        var renamed = _memoryNoteStore.Rename(name, newName);
        if (!renamed.Ok || string.IsNullOrWhiteSpace(renamed.OldName) || string.IsNullOrWhiteSpace(renamed.NewName))
        {
            _auditLogger.Log("web", "memory_note_rename", renamed.Ok ? "ok" : "skip", renamed.Message);
            return (renamed, 0);
        }

        var relinkedConversations = _conversationStore.RenameLinkedMemoryNote(renamed.OldName, renamed.NewName);
        _auditLogger.Log(
            "web",
            "memory_note_rename",
            "ok",
            $"old={renamed.OldName} new={renamed.NewName} relinkedConversations={relinkedConversations}"
        );
        return (renamed, relinkedConversations);
    }

    public MemoryNoteDeleteResult DeleteMemoryNotes(IReadOnlyList<string>? names)
    {
        var normalizedNames = (names ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedNames.Length == 0)
        {
            return new MemoryNoteDeleteResult(
                false,
                "삭제할 메모리 노트를 선택하세요.",
                0,
                0,
                0,
                Array.Empty<string>()
            );
        }

        var removedNames = new List<string>(normalizedNames.Length);
        foreach (var noteName in normalizedNames)
        {
            if (_memoryNoteStore.Delete(noteName))
            {
                removedNames.Add(noteName);
            }
        }

        var unlinkedConversations = removedNames.Count > 0
            ? _conversationStore.RemoveLinkedMemoryNotes(removedNames)
            : 0;
        var removedCount = removedNames.Count;
        var message = removedCount == 0
            ? "선택한 메모리 노트를 삭제하지 못했습니다."
            : $"메모리 노트 삭제 완료: {removedCount}/{normalizedNames.Length}";
        _auditLogger.Log(
            "web",
            "memory_note_delete",
            removedCount > 0 ? "ok" : "skip",
            $"requested={normalizedNames.Length} removed={removedCount} unlinkedConversations={unlinkedConversations}"
        );
        return new MemoryNoteDeleteResult(
            removedCount > 0,
            message,
            normalizedNames.Length,
            removedCount,
            unlinkedConversations,
            removedNames.ToArray()
        );
    }

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
                _config.ConversationKeepRecentMessages,
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

    public MemorySearchToolResult SearchMemory(string query, int? maxResults = null, double? minScore = null)
    {
        return _memorySearchTool.Search(query, maxResults, minScore);
    }

    public MemoryGetToolResult GetMemory(string path, int? from = null, int? lines = null)
    {
        return _memoryGetTool.Get(path, from, lines);
    }

    public MemoryIndexRebuildResult RebuildMemoryIndex()
    {
        try
        {
            var schema = new MemoryIndexSchemaBootstrap(_config).EnsureInitialized();
            var snapshot = new MemoryIndexDocumentSync(_config, schema).SyncOnce();
            var message = $"메모리 인덱스 재빌드 완료: scanned={snapshot.ScannedDocuments}, indexed={snapshot.IndexedDocuments}, skipped={snapshot.SkippedDocuments}, removed={snapshot.RemovedDocuments}";
            _auditLogger.Log("web", "memory_index_rebuild", "ok", message);
            return new MemoryIndexRebuildResult(true, message, snapshot);
        }
        catch (Exception ex)
        {
            var message = $"메모리 인덱스 재빌드 실패: {ex.Message}";
            _auditLogger.Log("web", "memory_index_rebuild", "error", TrimPlanText(message, 300));
            return new MemoryIndexRebuildResult(false, message, null, ex.Message);
        }
    }

    private static string NormalizeMemoryClearScope(string? scope)
    {
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "chat" => "chat",
            "coding" => "coding",
            "telegram" => "telegram",
            "all" => "all",
            _ => "chat"
        };
    }
}
