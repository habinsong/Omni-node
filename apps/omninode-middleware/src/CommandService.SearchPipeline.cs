using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private sealed record WebNeedDecisionResult(
        bool NeedWeb,
        bool DecisionSucceeded,
        string Reason,
        string Provider,
        string Model
    );
    private readonly record struct GeminiGroundedWebAnswerResult(
        LlmSingleChatResult Response,
        ChatLatencyMetrics? Latency,
        IReadOnlyList<SearchCitationReference>? Citations = null
    );
    private async Task<WebNeedDecisionResult> DecideNeedWebBySelectedProviderAsync(
        string input,
        string provider,
        string model,
        CancellationToken cancellationToken
    )
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var requestedProvider = NormalizeProvider(provider, allowAuto: true);
        var normalizedProvider = requestedProvider == "auto"
            ? await ResolveCategoryProviderAsync(
                TaskCategory.SearchTimeSensitive,
                requestedProvider,
                null,
                cancellationToken,
                "search_need_web"
            )
            : NormalizeProvider(provider, allowAuto: false);
        var resolvedModel = ResolveModelForCategory(TaskCategory.SearchTimeSensitive, normalizedProvider, model);
        if (normalizedInput.Length == 0)
        {
            return new WebNeedDecisionResult(false, true, "empty_input", normalizedProvider, resolvedModel);
        }

        var prompt = BuildWebNeedDecisionPrompt(normalizedInput);
        using var decisionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        decisionCts.CancelAfter(TimeSpan.FromMilliseconds(_context.WebDecisionTimeoutMs));
        LlmSingleChatResult decision;
        try
        {
            decision = await GenerateByProviderAsync(
                normalizedProvider,
                resolvedModel,
                prompt,
                decisionCts.Token,
                maxOutputTokens: 96
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebNeedDecisionResult(false, false, "decision_timeout", normalizedProvider, resolvedModel);
        }
        catch (Exception ex)
        {
            return new WebNeedDecisionResult(false, false, $"decision_error:{ex.Message}", normalizedProvider, resolvedModel);
        }

        if (TryParseNeedWebDecisionJson(decision.Text, out var needWeb, out var reason))
        {
            return new WebNeedDecisionResult(
                needWeb,
                true,
                reason.Length == 0 ? "json" : $"json:{reason}",
                decision.Provider,
                decision.Model
            );
        }

        var normalizedDecisionToken = NormalizeWebSearchDecisionToken(decision.Text);
        if (normalizedDecisionToken == "yes")
        {
            return new WebNeedDecisionResult(true, true, "token_yes", decision.Provider, decision.Model);
        }

        if (normalizedDecisionToken == "no")
        {
            return new WebNeedDecisionResult(false, true, "token_no", decision.Provider, decision.Model);
        }

        return new WebNeedDecisionResult(false, false, "decision_unparsed", decision.Provider, decision.Model);
    }

    private string BuildWebNeedDecisionPrompt(string normalizedInput)
    {
        return SearchPromptPolicy.BuildWebNeedDecisionPrompt(normalizedInput);
    }

    private async Task<GeminiGroundedWebAnswerResult> GenerateGeminiUrlContextAnswerDetailedAsync(
        string input,
        IReadOnlyList<string> urls,
        string memoryHint,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle,
        Action<ChatStreamUpdate>? streamCallback,
        string scope,
        string mode,
        string conversationId,
        string decisionPath,
        long decisionMs,
        CancellationToken cancellationToken
    )
    {
        var model = ResolveUrlContextLlmModel();
        const bool includeGoogleSearch = false;
        const string route = "gemini-url-single";
        var chunkIndex = 0;
        Action<string>? deltaCallback = null;
        if (streamCallback != null)
        {
            deltaCallback = delta =>
            {
                if (string.IsNullOrEmpty(delta))
                {
                    return;
                }

                chunkIndex += 1;
                streamCallback(new ChatStreamUpdate(scope, mode, conversationId, "gemini", model, route, delta, chunkIndex));
            };
        }

        var promptStopwatch = Stopwatch.StartNew();
        var repositoryContext = await TryLoadRepositoryContextSnapshotAsync(input, urls, cancellationToken);
        var extractiveRepositoryAnswer = repositoryContext.HasValue
            ? SearchUrlContextPolicy.TryBuildRepositoryExtractiveAnswer(
                input,
                repositoryContext.Value.Description,
                repositoryContext.Value.ReadmeText
            )
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(extractiveRepositoryAnswer))
        {
            var extractiveSanitizeStopwatch = Stopwatch.StartNew();
            var extractiveOutputText = EnsureReadableWebAnswerResponse(extractiveRepositoryAnswer, input, allowMarkdownTable);
            var extractiveSanitizeMs = Math.Max(0L, extractiveSanitizeStopwatch.ElapsedMilliseconds);
            ChatLatencyMetrics? extractiveLatency = null;
            if (!string.IsNullOrWhiteSpace(decisionPath))
            {
                extractiveLatency = new ChatLatencyMetrics(
                    decisionMs,
                    Math.Max(0L, promptStopwatch.ElapsedMilliseconds),
                    0,
                    Math.Max(0L, promptStopwatch.ElapsedMilliseconds),
                    extractiveSanitizeMs,
                    decisionPath
                );
            }

            if (deltaCallback != null)
            {
                deltaCallback(extractiveOutputText);
            }

            return new GeminiGroundedWebAnswerResult(
                new LlmSingleChatResult("gemini", model, extractiveOutputText),
                extractiveLatency,
                Array.Empty<SearchCitationReference>()
            );
        }

        var prompt = BuildGeminiUrlContextAnswerPrompt(
            input,
            urls,
            memoryHint,
            allowMarkdownTable,
            enforceTelegramOutputStyle,
            includeGoogleSearch,
            repositoryContext
        );
        var maxOutputTokens = ResolveGeminiUrlContextMaxOutputTokens(input);
        var promptBuildMs = Math.Max(0L, promptStopwatch.ElapsedMilliseconds);
        GeminiUrlContextChatResponse response;
        if (repositoryContext.HasValue)
        {
            var directStopwatch = Stopwatch.StartNew();
            var directText = await _llmRouter.GenerateGeminiChatAsync(
                prompt,
                model,
                maxOutputTokens,
                cancellationToken
            );
            response = new GeminiUrlContextChatResponse(
                directText,
                0,
                Math.Max(0L, directStopwatch.ElapsedMilliseconds),
                Array.Empty<SearchCitationReference>()
            );
            if (deltaCallback != null && !string.IsNullOrWhiteSpace(directText))
            {
                deltaCallback(directText);
            }
        }
        else
        {
            response = await _llmRouter.GenerateGeminiUrlContextChatStreamingAsync(
                prompt,
                model,
                maxOutputTokens,
                _context.GeminiWebTimeoutMs,
                includeGoogleSearch,
                deltaCallback,
                cancellationToken
            );
        }

        var sanitizeStopwatch = Stopwatch.StartNew();
        string outputText;
        if (IsGeminiUrlContextFailureText(response.Text))
        {
            outputText = BuildGeminiUrlContextFailureNotice(input, response.Text);
        }
        else
        {
            outputText = SanitizeChatOutput(response.Text, keepMarkdownTables: allowMarkdownTable);
            outputText = EnsureReadableWebAnswerResponse(outputText, input, allowMarkdownTable);
        }

        var sanitizeMs = Math.Max(0L, sanitizeStopwatch.ElapsedMilliseconds);
        ChatLatencyMetrics? latency = null;
        if (!string.IsNullOrWhiteSpace(decisionPath))
        {
            latency = new ChatLatencyMetrics(
                decisionMs,
                promptBuildMs,
                response.FirstChunkMs,
                response.FullResponseMs,
                sanitizeMs,
                decisionPath
            );
        }

        return new GeminiGroundedWebAnswerResult(
            new LlmSingleChatResult("gemini", model, outputText),
            latency,
            response.Citations
        );
    }

    private async Task<LlmSingleChatResult> GenerateGeminiGroundedWebAnswerAsync(
        string input,
        string memoryHint,
        bool selfDecideNeedWeb,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle,
        CancellationToken cancellationToken
    )
    {
        var result = await GenerateGeminiGroundedWebAnswerDetailedAsync(
            input,
            memoryHint,
            selfDecideNeedWeb,
            allowMarkdownTable,
            enforceTelegramOutputStyle,
            null,
            "chat",
            "single",
            string.Empty,
            string.Empty,
            0,
            cancellationToken
        );
        return result.Response;
    }

    private async Task<GeminiGroundedWebAnswerResult> GenerateGeminiGroundedWebAnswerDetailedAsync(
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
        CancellationToken cancellationToken
    )
    {
        var model = ResolveSearchLlmModel();
        var route = "gemini-web-single";
        var chunkIndex = 0;
        Action<string>? deltaCallback = null;
        if (streamCallback != null)
        {
            deltaCallback = delta =>
            {
                if (string.IsNullOrEmpty(delta))
                {
                    return;
                }

                chunkIndex += 1;
                streamCallback(new ChatStreamUpdate(scope, mode, conversationId, "gemini", model, route, delta, chunkIndex));
            };
        }

        var promptStopwatch = Stopwatch.StartNew();
        var prompt = BuildGeminiWebAnswerPrompt(input, memoryHint, selfDecideNeedWeb, allowMarkdownTable, enforceTelegramOutputStyle);
        var maxOutputTokens = ResolveGeminiWebAnswerMaxOutputTokens(input);
        var promptBuildMs = Math.Max(0L, promptStopwatch.ElapsedMilliseconds);
        var response = await _llmRouter.GenerateGeminiGroundedChatStreamingAsync(
            prompt,
            model,
            maxOutputTokens,
            _context.GeminiWebTimeoutMs,
            deltaCallback,
            cancellationToken
        );
        if (IsGeminiWebTimeoutText(response.Text))
        {
            Console.Error.WriteLine($"[gemini] grounded chat timeout retry (model={model})");
            var retryResponse = await _llmRouter.GenerateGeminiGroundedChatStreamingAsync(
                prompt,
                model,
                maxOutputTokens,
                _context.GeminiWebTimeoutMs,
                deltaCallback,
                cancellationToken
            );
            response = new GeminiGroundedChatResponse(
                retryResponse.Text,
                retryResponse.FirstChunkMs > 0
                    ? response.FullResponseMs + retryResponse.FirstChunkMs
                    : 0,
                response.FullResponseMs + retryResponse.FullResponseMs
            );
        }

        var sanitizeStopwatch = Stopwatch.StartNew();
        string outputText;
        if (IsGeminiWebFailureText(response.Text))
        {
            outputText = BuildGeminiWebFailureNotice(input, response.Text);
        }
        else
        {
            outputText = SanitizeChatOutput(response.Text, keepMarkdownTables: allowMarkdownTable);
            outputText = EnsureReadableWebAnswerResponse(outputText, input, allowMarkdownTable);
        }

        var sanitizeMs = Math.Max(0L, sanitizeStopwatch.ElapsedMilliseconds);
        ChatLatencyMetrics? latency = null;
        if (!string.IsNullOrWhiteSpace(decisionPath))
        {
            latency = new ChatLatencyMetrics(
                decisionMs,
                promptBuildMs,
                response.FirstChunkMs,
                response.FullResponseMs,
                sanitizeMs,
                decisionPath
            );
        }

        return new GeminiGroundedWebAnswerResult(
            new LlmSingleChatResult("gemini", model, outputText),
            latency
        );
    }

    private static string EnsureReadableWebAnswerResponse(string text, string input, bool allowMarkdownTable)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        if (allowMarkdownTable && LooksLikeTableRenderRequest(input))
        {
            return EnsureMarkdownTableResponseIfRequested(normalized);
        }

        normalized = RemoveWebAnswerSourceLinkArtifacts(normalized);
        normalized = NormalizeCollapsedWebBulletRuns(normalized);
        if (LooksLikeComparisonRequest(input))
        {
            return normalized;
        }

        if (LooksLikeListOutputRequest(input) || LooksLikeNumberedListResponse(normalized))
        {
            return NormalizeGeminiWebNumberedListResponse(normalized);
        }

        return NormalizeWebAnswerNarrativeParagraphs(normalized);
    }

    private static string NormalizeGeminiWebNumberedListResponse(string text)
    {
        return SearchAnswerFormatterPolicy.NormalizeNumberedListResponse(text);
    }

    private static bool LooksLikeNumberedListResponse(string text)
    {
        return SearchAnswerFormatterPolicy.LooksLikeNumberedListResponse(text);
    }

    private static string NormalizeCollapsedWebBulletRuns(string text)
    {
        return SearchAnswerFormatterPolicy.NormalizeCollapsedBulletRuns(text);
    }

    private static string RemoveWebAnswerSourceLinkArtifacts(string text)
    {
        return SearchAnswerFormatterPolicy.RemoveSourceLinkArtifacts(text);
    }

    private static string NormalizeWebAnswerNarrativeParagraphs(string text)
    {
        return SearchAnswerFormatterPolicy.NormalizeNarrativeParagraphs(text);
    }

    private static string EnsureMarkdownTableResponseIfRequested(string text)
    {
        return SearchAnswerFormatterPolicy.EnsureMarkdownTableResponseIfRequested(text);
    }

    private static string ConvertDelimitedPlainTextTableToMarkdown(string text)
    {
        return SearchAnswerFormatterPolicy.ConvertDelimitedPlainTextTableToMarkdown(text);
    }

    private static string NormalizeMarkdownTableResponseMetadata(string text)
    {
        return SearchAnswerFormatterPolicy.NormalizeMarkdownTableResponseMetadata(text);
    }

    private static string SanitizeTableCell(string value)
    {
        return SearchAnswerFormatterPolicy.SanitizeTableCell(value);
    }

    private string BuildGeminiUrlContextAnswerPrompt(
        string input,
        IReadOnlyList<string> urls,
        string memoryHint,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle,
        bool includeGoogleSearch,
        SearchRepositoryContextSnapshot? repositoryContext = null
    )
    {
        var promptRepositoryContext = repositoryContext.HasValue
            ? new SearchPromptRepositoryContext(
                repositoryContext.Value.RepositorySlug,
                repositoryContext.Value.Description,
                repositoryContext.Value.ReadmeText
            )
            : (SearchPromptRepositoryContext?)null;
        return SearchPromptPolicy.BuildGeminiUrlContextAnswerPrompt(
            input,
            urls,
            memoryHint,
            allowMarkdownTable,
            enforceTelegramOutputStyle,
            includeGoogleSearch,
            _context.WebDefaultNewsCount,
            _context.WebDefaultListCount,
            promptRepositoryContext
        );
    }

    private async Task<SearchRepositoryContextSnapshot?> TryLoadRepositoryContextSnapshotAsync(
        string input,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken
    )
    {
        var loader = new GitHubRepositoryContextLoader(WebFetchClient);
        return await loader.TryLoadAsync(input, urls, cancellationToken);
    }

    private static string ResolveImplicitUrlRequest(string input, IReadOnlyList<string> urls)
    {
        return SearchUrlContextPolicy.ResolveImplicitUrlRequest(input, urls);
    }

    private static bool LooksLikeImplicitUrlSummaryRequest(string input, IReadOnlyList<string> urls)
    {
        return SearchUrlContextPolicy.LooksLikeImplicitUrlSummaryRequest(input, urls);
    }

    private static bool LooksLikeSiteOverviewRequest(string input)
    {
        return SearchUrlContextPolicy.LooksLikeSiteOverviewRequest(input);
    }

    private static bool LooksLikeSiteRootUrl(string url)
    {
        return SearchUrlContextPolicy.LooksLikeSiteRootUrl(url);
    }

    private static bool LooksLikeDocumentationUrl(string url)
    {
        return SearchUrlContextPolicy.LooksLikeDocumentationUrl(url);
    }

    private static bool LooksLikeArticleUrl(string url)
    {
        return SearchUrlContextPolicy.LooksLikeArticleUrl(url);
    }

    private static bool LooksLikeRepositoryUrl(string url)
    {
        return SearchUrlContextPolicy.LooksLikeRepositoryUrl(url);
    }

    private string BuildGeminiWebAnswerPrompt(
        string input,
        string memoryHint,
        bool selfDecideNeedWeb,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle
    )
    {
        return SearchPromptPolicy.BuildGeminiWebAnswerPrompt(
            input,
            memoryHint,
            selfDecideNeedWeb,
            allowMarkdownTable,
            enforceTelegramOutputStyle,
            _context.WebDefaultNewsCount,
            _context.WebDefaultListCount,
            ResolveSourceDomainFromQueryOrFocus
        );
    }

    private string BuildSafeWebMemoryPreferenceHint(
        string conversationId,
        string currentInput,
        IReadOnlyList<string>? linkedMemoryNotes
    )
    {
        var normalizedInput = (currentInput ?? string.Empty).Trim();
        if (normalizedInput.Length == 0)
        {
            return string.Empty;
        }

        if (SearchQueryPolicy.ShouldBlockWebMemoryHintByOverride(normalizedInput))
        {
            return string.Empty;
        }

        var sourceFocus = ExtractSourceFocusHintFromInput(normalizedInput);
        var sourceDomain = ResolveSourceDomainFromQueryOrFocus(normalizedInput, sourceFocus);
        var hasSourceOverride = sourceFocus.Length > 0
            || sourceDomain.Length > 0
            || normalizedInput.Contains("site:", StringComparison.OrdinalIgnoreCase);
        var hasCountOverride = HasExplicitRequestedCountInQuery(normalizedInput);
        var hasFormatOverride = SearchQueryPolicy.LooksLikeWebFormatDirective(normalizedInput);
        var hasToneOverride = SearchQueryPolicy.LooksLikeWebToneDirective(normalizedInput);
        var hasLanguageOverride = SearchQueryPolicy.LooksLikeWebLanguageDirective(normalizedInput);
        var shouldReadMemoryHint = hasSourceOverride || hasFormatOverride || hasToneOverride || hasLanguageOverride;
        if (!shouldReadMemoryHint)
        {
            return string.Empty;
        }

        var candidates = new List<WebPreferenceHint>(16);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var thread = _conversationStore.Get(conversationId);
        if (thread is not null)
        {
            var recentUserMessages = thread.Messages
                .Where(msg => msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                .Select(msg => (msg.Text ?? string.Empty).Trim())
                .Where(text => text.Length > 0 && !text.Equals(normalizedInput, StringComparison.Ordinal))
                .Reverse()
                .Take(8)
                .ToArray();
            foreach (var message in recentUserMessages)
            {
                foreach (var hint in SearchQueryPolicy.ExtractWebPreferenceHints(message, fromMemoryNote: false))
                {
                    var key = SearchQueryPolicy.NormalizeWebPreferenceKey(hint.Text);
                    if (key.Length == 0 || !seen.Add(key))
                    {
                        continue;
                    }

                    candidates.Add(hint);
                    if (candidates.Count >= 16)
                    {
                        break;
                    }
                }

                if (candidates.Count >= 16)
                {
                    break;
                }
            }
        }

        var memoryNotes = MergeMemoryNoteNames(Array.Empty<string>(), linkedMemoryNotes);
        foreach (var noteName in memoryNotes.Take(4))
        {
            var read = _memoryNoteStore.Read(noteName);
            if (read is null || string.IsNullOrWhiteSpace(read.Content))
            {
                continue;
            }

            foreach (var hint in SearchQueryPolicy.ExtractWebPreferenceHints(read.Content, fromMemoryNote: true))
            {
                var key = SearchQueryPolicy.NormalizeWebPreferenceKey(hint.Text);
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                candidates.Add(hint);
                if (candidates.Count >= 16)
                {
                    break;
                }
            }

            if (candidates.Count >= 16)
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        var filtered = candidates
            .Where(item =>
            {
                return item.Category switch
                {
                    "source" => !hasSourceOverride,
                    "count" => !hasCountOverride,
                    "format" => !hasFormatOverride,
                    "tone" => !hasToneOverride,
                    "language" => !hasLanguageOverride,
                    _ => false
                };
            })
            .Select(item => item.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (filtered.Length == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>(2);
        var charBudget = 160;
        var used = 0;
        foreach (var text in filtered)
        {
            if (lines.Count >= 2)
            {
                break;
            }

            var compact = Regex.Replace(text, @"\s+", " ").Trim();
            if (compact.Length == 0)
            {
                continue;
            }

            var line = $"- {compact}";
            var delta = line.Length + (lines.Count == 0 ? 0 : 1);
            if (used + delta > charBudget)
            {
                break;
            }

            lines.Add(line);
            used += delta;
        }

        return lines.Count == 0 ? string.Empty : string.Join('\n', lines);
    }

    private int ResolveWebDefaultCount(string input)
    {
        return SearchQueryPolicy.ResolveWebDefaultCount(
            input,
            _context.WebDefaultNewsCount,
            _context.WebDefaultListCount
        );
    }

    private int ResolveGeminiWebAnswerMaxOutputTokens(string input)
    {
        return SearchPromptPolicy.ResolveGeminiWebAnswerMaxOutputTokens(
            input,
            _context.WebDefaultNewsCount,
            _context.WebDefaultListCount
        );
    }

    private int ResolveGeminiUrlContextMaxOutputTokens(string input)
    {
        return SearchPromptPolicy.ResolveGeminiUrlContextMaxOutputTokens(
            input,
            _context.WebDefaultNewsCount,
            _context.WebDefaultListCount
        );
    }

    private static bool IsGeminiUrlContextFailureText(string text)
    {
        return SearchPromptPolicy.IsGeminiUrlContextFailureText(text);
    }

    private string BuildGeminiUrlContextFailureNotice(string input, string failureText)
    {
        return SearchPromptPolicy.BuildGeminiUrlContextFailureNotice(input, failureText);
    }

    private static bool IsGeminiWebFailureText(string text)
    {
        return SearchPromptPolicy.IsGeminiWebFailureText(text);
    }

    private static bool IsGeminiWebTimeoutText(string text)
    {
        return SearchPromptPolicy.IsGeminiWebTimeoutText(text);
    }

    private string BuildGeminiWebFailureNotice(string input, string failureText)
    {
        return SearchPromptPolicy.BuildGeminiWebFailureNotice(input, failureText);
    }

    private static bool TryParseNeedWebDecisionJson(string? rawText, out bool needWeb, out string reason)
    {
        return SearchPromptPolicy.TryParseNeedWebDecisionJson(rawText, out needWeb, out reason);
    }

    private async Task<SearchRequirementDecision> DecideWebSearchRequirementAsync(
        string input,
        CancellationToken cancellationToken
    )
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return new SearchRequirementDecision(false, "llm:false:empty_input", string.Empty, string.Empty);
        }

        if (LooksLikeClearlyNonWebQuestion(normalized))
        {
            return new SearchRequirementDecision(false, "heuristic:false:non_web", string.Empty, string.Empty);
        }

        if (_context.EnableFastWebPipeline)
        {
            return SearchQueryPolicy.BuildFastRequirementDecision(normalized);
        }

        var provider = await ResolveCategoryProviderAsync(
            TaskCategory.SearchTimeSensitive,
            "auto",
            null,
            cancellationToken,
            "search_requirement"
        );
        if (provider.Length == 0 || provider == "none")
        {
            var fallbackDecision = LooksLikeExplicitWebLookupQuestion(normalized) || LooksLikeRealtimeQuestion(normalized);
            return new SearchRequirementDecision(
                fallbackDecision,
                fallbackDecision ? "fallback:true:no_llm_key" : "fallback:false:no_llm_key",
                ExtractSourceFocusHintFromInput(normalized),
                ExtractSourceDomainHintFromInput(normalized)
            );
        }

        var model = ResolveModelForCategory(TaskCategory.SearchTimeSensitive, provider, null);
        var prompt = $"""
                      사용자의 입력을 보고 웹 검색 필요 여부와 소스 제약 의도를 JSON으로 판단하세요.
                      기준:
                      - 최신성/실시간성(뉴스, 오늘 일정, 시세, 최근 변경, 현재 상태)이 중요하면 needWeb=YES
                      - AI 봇(너, 자신)의 정체성, 능력, 사용법에 대한 질문이거나 인사, 일상 대화(안녕, 반가워 등)면 무조건 needWeb=NO
                      - 짧은 감정 표현(피곤해, 우울해, 좋은 일이 없어), 단순 동조(응, 맞아, 그렇네), 되묻기(너는?) 등 문맥이 없는 일상 대화면 무조건 needWeb=NO
                      - 일반 지식/설명/창작/코딩처럼 최신 웹 근거가 필수 아님이면 needWeb=NO
                      - 특정 매체/기관/브랜드의 정보만 원하는 의도가 보이면 sourceFocus에 그 명칭을 넣으세요.
                      - sourceFocus가 있고 공식 도메인을 신뢰성 있게 유추 가능하면 sourceDomain에 도메인만 넣으세요. (예: cnn.com)

                      출력 규칙:
                      - 반드시 JSON 한 줄만 출력
                      - 스키마 키: needWeb, sourceFocus, sourceDomain
                      - 예시 형식: needWeb=YES, sourceFocus=CNN, sourceDomain=cnn.com
                      - sourceFocus/sourceDomain이 없으면 빈 문자열

                      사용자 입력:
                      {normalized}
                      """;
        var decision = await GenerateByProviderSafeAsync(
            provider,
            model,
            prompt,
            cancellationToken,
            maxOutputTokens: 96
        );
        if (TryParseSearchRequirementDecisionJson(
                decision.Text,
                out var parsedNeedWeb,
                out var parsedSourceFocus,
                out var parsedSourceDomain))
        {
            return new SearchRequirementDecision(
                parsedNeedWeb,
                parsedNeedWeb ? $"llm:true:{decision.Provider}:{decision.Model}" : $"llm:false:{decision.Provider}:{decision.Model}",
                parsedSourceFocus,
                parsedSourceDomain
            );
        }

        var decisionToken = NormalizeWebSearchDecisionToken(decision.Text);
        if (decisionToken == "no")
        {
            return new SearchRequirementDecision(
                false,
                $"llm:false:{provider}:{decision.Model}",
                ExtractSourceFocusHintFromInput(normalized),
                ExtractSourceDomainHintFromInput(normalized)
            );
        }

        if (decisionToken == "yes")
        {
            return new SearchRequirementDecision(
                true,
                $"llm:true:{provider}:{decision.Model}",
                ExtractSourceFocusHintFromInput(normalized),
                ExtractSourceDomainHintFromInput(normalized)
            );
        }

        var fallback = LooksLikeExplicitWebLookupQuestion(normalized) || LooksLikeRealtimeQuestion(normalized);
        return new SearchRequirementDecision(
            fallback,
            fallback
                ? $"fallback:true:unparsed:{provider}:{decision.Model}"
                : $"fallback:false:unparsed:{provider}:{decision.Model}",
            ExtractSourceFocusHintFromInput(normalized),
            ExtractSourceDomainHintFromInput(normalized)
        );
    }

    private string ResolveSearchLlmModel()
    {
        var configured = NormalizeModelSelection(_providers.GeminiSearchModel);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        return _providers.GeminiModel;
    }

    private string ResolveUrlContextLlmModel()
    {
        var configured = NormalizeModelSelection(_providers.GeminiModel);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        return "gemini-3-flash-preview";
    }

    private bool ShouldUseGeminiWebComposer(
        string input,
        IReadOnlyList<SearchCitationReference>? citations,
        string requestedProvider
    )
    {
        if (!_llmRouter.HasGeminiApiKey())
        {
            return false;
        }

        if (requestedProvider.Equals("gemini", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (citations == null || citations.Count == 0)
        {
            return false;
        }

        return LooksLikeListOutputRequest(input);
    }

    private static bool TryParseSearchRequirementDecisionJson(
        string? rawText,
        out bool needWeb,
        out string sourceFocus,
        out string sourceDomain
    )
    {
        return SearchQueryPolicy.TryParseSearchRequirementDecisionJson(rawText, out needWeb, out sourceFocus, out sourceDomain);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ExtractSourceFocusHintFromInput(string input)
    {
        return SearchQueryPolicy.ExtractSourceFocusHintFromInput(input);
    }

    private static string ExtractSourceDomainHintFromInput(string input)
    {
        return SearchQueryPolicy.ExtractSourceDomainHintFromInput(input);
    }

    private static string NormalizeSourceDomainHint(string? domain)
    {
        return SearchQueryPolicy.NormalizeSourceDomainHint(domain);
    }

    private static string BuildEffectiveSearchQuery(string query, SearchRequirementDecision decision)
    {
        return SearchQueryPolicy.BuildEffectiveSearchQuery(query, decision, ResolveSourceDomainFromQueryOrFocus);
    }

    private static string NormalizeWebSearchDecisionToken(string? decisionText)
    {
        return SearchQueryPolicy.NormalizeWebSearchDecisionToken(decisionText);
    }

    private static bool LooksLikeRealtimeQuestion(string input)
    {
        return SearchQueryPolicy.LooksLikeRealtimeQuestion(input);
    }

    private static bool LooksLikeExplicitWebLookupQuestion(string input)
    {
        return SearchQueryPolicy.LooksLikeExplicitWebLookupQuestion(input);
    }

    private static bool LooksLikeClearlyNonWebQuestion(string input)
    {
        return SearchQueryPolicy.LooksLikeClearlyNonWebQuestion(input);
    }

    private static bool LooksLikeCasualOrIdentityQuestion(string input)
    {
        return SearchQueryPolicy.LooksLikeCasualOrIdentityQuestion(input);
    }

    private static bool LooksLikeStandaloneFreshGreeting(string input)
    {
        return SearchQueryPolicy.LooksLikeStandaloneFreshGreeting(input);
    }

    private static bool LooksLikeLocalDateTimeQuestion(string input)
    {
        return SearchQueryPolicy.LooksLikeLocalDateTimeQuestion(input);
    }

    private static bool LooksLikeComparisonRequest(string input)
    {
        return SearchQueryPolicy.LooksLikeComparisonRequest(input);
    }

    private static double ResolveForcedMemoryMinScore(string input)
    {
        return SearchQueryPolicy.ResolveForcedMemoryMinScore(input);
    }

    private static string ResolveSearchFreshnessForQuery(string input)
    {
        return SearchQueryPolicy.ResolveSearchFreshnessForQuery(input);
    }

    private static int ResolveRequestedResultCountFromQuery(string input)
    {
        return SearchQueryPolicy.ResolveRequestedResultCountFromQuery(input);
    }

    private static bool HasExplicitRequestedCountInQuery(string input)
    {
        return SearchQueryPolicy.HasExplicitRequestedCountInQuery(input);
    }

    private static bool CanUseDeterministicListFastPath(
        string input,
        IReadOnlyList<SearchCitationReference>? citations
    )
    {
        if (!HasExplicitRequestedCountInQuery(input))
        {
            return false;
        }

        if (citations == null || citations.Count == 0)
        {
            return false;
        }

        var targetCount = Math.Clamp(ResolveRequestedResultCountFromQuery(input), 1, 10);
        var normalized = citations
            .Select(item =>
            {
                if (!TryNormalizeDisplaySourceUrl(item.Url, out var sourceUrl))
                {
                    return null;
                }

                return item with { Url = sourceUrl };
            })
            .Where(item => item is not null)
            .Cast<SearchCitationReference>()
            .ToArray();
        if (normalized.Length < targetCount)
        {
            return false;
        }

        var deduplicated = DeduplicateCitationsForList(normalized);
        if (deduplicated.Length < targetCount)
        {
            return false;
        }

        var qualityFiltered = deduplicated
            .Where(item => !IsLowQualityCitationForList(item))
            .ToArray();
        return qualityFiltered.Length >= targetCount;
    }
}
