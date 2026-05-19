namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private async Task<SearchAnswerCompositionResult> ComposeGroundedWebAnswerWithFallbackAsync(
        string input,
        string memoryHint,
        bool selfDecideNeedWeb,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle,
        Action<ChatStreamUpdate>? streamCallback,
        string scope,
        string mode,
        string conversationId,
        string decisionPath,
        long decisionMs,
        string source,
        CancellationToken cancellationToken
    )
    {
        if (_llmRouter.HasGeminiApiKey())
        {
            var geminiResult = await GenerateGeminiGroundedWebAnswerDetailedAsync(
                input,
                memoryHint,
                selfDecideNeedWeb,
                allowMarkdownTable,
                enforceTelegramOutputStyle,
                streamCallback,
                scope,
                mode,
                conversationId,
                decisionPath,
                decisionMs,
                cancellationToken
            ).ConfigureAwait(false);
            if (!IsGroundedWebAnswerFailureText(geminiResult.Response.Text))
            {
                return new SearchAnswerCompositionResult(
                    geminiResult.Response,
                    "gemini-web-single",
                    geminiResult.Latency,
                    geminiResult.Citations,
                    null,
                    SearchRetrieverPath.GeminiGrounding
                );
            }

            _auditLogger.Log(
                NormalizeAuditToken(source, "web"),
                "search_answer_composer",
                "fallback",
                $"reason=gemini_web_failure route=gemini-web-single detail={TrimForAudit(geminiResult.Response.Text, 180)}"
            );
        }
        else
        {
            _auditLogger.Log(
                NormalizeAuditToken(source, "web"),
                "search_answer_composer",
                "fallback",
                "reason=gemini_api_key_missing route=gemini-web-single"
            );
        }

        try
        {
            return await _searchAnswerComposer.ComposeGroundedWebAnswerAsync(
                new SearchAnswerCompositionRequest(
                    input,
                    memoryHint,
                    selfDecideNeedWeb,
                    allowMarkdownTable,
                    enforceTelegramOutputStyle,
                    scope,
                    mode,
                    conversationId,
                    decisionPath,
                    decisionMs,
                    streamCallback
                ),
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = new SearchAnswerGuardFailure(
                SearchAnswerGuardFailureCategory.Coverage,
                "search_answer_composer_exception",
                TrimForAudit(ex.Message, 160)
            );
            _auditLogger.Log(
                NormalizeAuditToken(source, "web"),
                "search_answer_composer",
                "fail",
                $"reason=search_answer_composer_exception detail={TrimForAudit(ex.Message, 180)}"
            );
            return new SearchAnswerCompositionResult(
                new LlmSingleChatResult(
                    "search_evidence",
                    "fallback",
                    BuildGroundedSearchFailureMessage(failure, "composer_exception")
                ),
                "search-evidence-fallback",
                string.IsNullOrWhiteSpace(decisionPath)
                    ? null
                    : new ChatLatencyMetrics(decisionMs, 0, 0, 0, 0, $"{decisionPath}:search_evidence_exception"),
                Array.Empty<SearchCitationReference>(),
                failure,
                null
            );
        }
    }

    private static bool IsGroundedWebAnswerFailureText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        return SearchPromptPolicy.IsGeminiWebFailureText(normalized)
            || normalized.StartsWith("요청하신 최신 정보를 생성하지 못했습니다.", StringComparison.Ordinal)
            || normalized.StartsWith("요청하신 목록을 생성하지 못했습니다.", StringComparison.Ordinal)
            || normalized.StartsWith("검색 실패:", StringComparison.Ordinal);
    }
}
