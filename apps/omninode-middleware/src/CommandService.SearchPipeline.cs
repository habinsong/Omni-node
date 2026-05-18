using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private sealed record SearchRequirementDecision(
        bool Required,
        string DecisionLabel,
        string SourceFocus,
        string SourceDomain
    );

    private sealed record WebNeedDecisionResult(
        bool NeedWeb,
        bool DecisionSucceeded,
        string Reason,
        string Provider,
        string Model
    );
    private readonly record struct WebPreferenceHint(string Category, string Text);
    private readonly record struct GeminiGroundedWebAnswerResult(
        LlmSingleChatResult Response,
        ChatLatencyMetrics? Latency,
        IReadOnlyList<SearchCitationReference>? Citations = null
    );
    private readonly record struct RepositoryContextSnapshot(
        string RepositorySlug,
        string Description,
        string ReadmeText
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
        decisionCts.CancelAfter(TimeSpan.FromMilliseconds(_config.WebDecisionTimeoutMs));
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
        return "너는 라우팅 전용 판단기다.\n"
            + "사용자 입력이 최신 외부 웹 근거가 필요한지 판정하고 JSON 한 줄만 출력해라.\n\n"
            + "출력 스키마:\n"
            + "{\"need_web\":true|false,\"reason\":\"짧은 근거\"}\n\n"
            + "판정 규칙:\n"
            + "- 뉴스/오늘/최근/실시간/최신/현재 상태/시세/가격/일정/법·정책 변경/특정 매체 기사 요청이면 need_web=true\n"
            + "- AI 봇(너, 자신)의 정체성, 능력, 사용법에 대한 질문이거나 인사, 일상 대화(안녕, 반가워 등)면 무조건 need_web=false\n"
            + "- 짧은 감정 표현(피곤해, 우울해, 좋은 일이 없어), 단순 동조(응, 맞아, 그렇네), 되묻기(너는?) 등 문맥이 없는 일상 대화면 무조건 need_web=false\n"
            + "- 일반 개념 설명/번역/코드 설명/창작/사용자 제공 텍스트 요약이면 need_web=false\n"
            + "- 설명문, 코드블록, 마크다운 금지\n\n"
            + "사용자 입력:\n"
            + normalizedInput;
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
            ? TryBuildRepositoryExtractiveAnswer(input, repositoryContext.Value)
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
                _config.GeminiWebTimeoutMs,
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
            _config.GeminiWebTimeoutMs,
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
                _config.GeminiWebTimeoutMs,
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
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|…)\s*(?=(?:No\.)?\d{1,2}\.\s*(?:\*\*|[A-Za-z가-힣0-9]))",
            "\n\n",
            RegexOptions.CultureInvariant
        );
        normalized = Regex.Replace(
            normalized,
            @"(?m)^(?<n>(?:No\.)?\d{1,2})\.(?=\S)",
            "${n}. ",
            RegexOptions.CultureInvariant
        );
        normalized = MergeDetachedNumberLinesForWeb(normalized);

        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (!LooksLikeNumberedListResponse(normalized))
        {
            return normalized;
        }

        var introParts = new List<string>(8);
        var itemBlocks = new List<string>(12);
        var trailingBlocks = new List<string>(4);
        var currentItem = new StringBuilder();
        var sawItem = false;
        var inTrailing = false;

        void FlushCurrentItem()
        {
            var body = NormalizeGeminiWebListItemBody(currentItem.ToString());
            if (body.Length > 0)
            {
                itemBlocks.Add(body);
            }

            currentItem.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (IsGeminiWebListSourceBlock(line))
            {
                FlushCurrentItem();
                inTrailing = true;
                trailingBlocks.Add(line);
                continue;
            }

            if (inTrailing)
            {
                trailingBlocks.Add(line);
                continue;
            }

            if (TryParseGeminiWebListLeadingNumber(line, out _))
            {
                FlushCurrentItem();
                currentItem.Append(StripGeminiWebListLeadingMarker(line));
                sawItem = true;
                continue;
            }

            if (!sawItem)
            {
                introParts.Add(line);
                continue;
            }

            if (currentItem.Length > 0)
            {
                currentItem.Append(' ');
            }

            currentItem.Append(line);
        }

        FlushCurrentItem();
        if (itemBlocks.Count < 2)
        {
            return normalized;
        }

        var output = new List<string>(itemBlocks.Count + trailingBlocks.Count + 2);
        var introBlock = NormalizeGeminiWebListItemBody(string.Join(" ", introParts));
        if (!string.IsNullOrWhiteSpace(introBlock))
        {
            output.Add(introBlock);
        }

        for (var index = 0; index < itemBlocks.Count; index++)
        {
            var body = NormalizeGeminiWebListItemBody(itemBlocks[index]);
            if (body.Length == 0)
            {
                continue;
            }

            output.Add($"{index + 1}. {body}");
        }

        foreach (var trailing in trailingBlocks)
        {
            output.Add(trailing.Trim());
        }

        return string.Join("\n\n", output.Where(block => !string.IsNullOrWhiteSpace(block))).Trim();
    }

    private static string MergeDetachedNumberLinesForWeb(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var current = (lines[i] ?? string.Empty).Trim();
            if (!Regex.IsMatch(current, @"^(?:No\.)?\d+\.$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                output.Add(lines[i]);
                continue;
            }

            var nextIndex = i + 1;
            while (nextIndex < lines.Length && string.IsNullOrWhiteSpace(lines[nextIndex]))
            {
                nextIndex += 1;
            }

            if (nextIndex >= lines.Length)
            {
                output.Add(lines[i]);
                continue;
            }

            var next = (lines[nextIndex] ?? string.Empty).Trim();
            if (next.Length == 0
                || Regex.IsMatch(next, @"^(?:No\.)?\d+\.$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                || IsMarkdownTableRow(next))
            {
                output.Add(lines[i]);
                continue;
            }

            output.Add($"{current} {next}");
            i = nextIndex;
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static string NormalizeGeminiWebListItemBody(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = normalized.Replace("\t", " ", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s+([,.;:!?])", "$1");
        return normalized.Trim();
    }

    private static bool LooksLikeNumberedListResponse(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|…)\s*(?=(?:No\.)?\d{1,2}\.\s*(?:\*\*|[A-Za-z가-힣0-9]))",
            "\n",
            RegexOptions.CultureInvariant
        );
        var matches = Regex.Matches(
            normalized,
            @"(?m)^\s*(?:No\.)?\d{1,2}\.\s*(?:\*\*|[A-Za-z가-힣0-9])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        return matches.Count >= 2;
    }

    private static bool NeedsGeminiWebListRenumbering(IReadOnlyList<string> itemBlocks)
    {
        if (itemBlocks == null || itemBlocks.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < itemBlocks.Count; index++)
        {
            if (!TryParseGeminiWebListLeadingNumber(itemBlocks[index], out var parsedNumber))
            {
                return true;
            }

            if (parsedNumber != index + 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseGeminiWebListLeadingNumber(string value, out int number)
    {
        number = 0;
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(trimmed, @"^(?:No\.)?(?<n>\d{1,2})\.\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static string StripGeminiWebListLeadingMarker(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        normalized = Regex.Replace(normalized, @"^(?:No\.)?\d{1,2}\.\s*", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return normalized.Trim();
    }

    private static bool IsGeminiWebListIntroBlock(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0
            || TryParseGeminiWebListLeadingNumber(trimmed, out _)
            || IsGeminiWebListSourceBlock(trimmed))
        {
            return false;
        }

        var lowered = trimmed.ToLowerInvariant();
        return lowered.Contains("정리해", StringComparison.Ordinal)
            || lowered.Contains("정리했습니다", StringComparison.Ordinal)
            || lowered.Contains("다음과 같습니다", StringComparison.Ordinal)
            || lowered.Contains("주요 뉴스", StringComparison.Ordinal);
    }

    private static bool IsGeminiWebListSourceBlock(string value)
    {
        return IsDisplaySourceLine(value);
    }

    private static string NormalizeCollapsedWebBulletRuns(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|다\.)\s*-\s+(?=[^\n])",
            "\n- ",
            RegexOptions.CultureInvariant
        );
        return Regex.Replace(normalized, @"\n{3,}", "\n\n");
    }

    private static string RemoveWebAnswerSourceLinkArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        var skipImmediateUrl = false;

        foreach (var raw in lines)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                skipImmediateUrl = false;
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^\s*출처\s*링크\s*[:：]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                skipImmediateUrl = true;
                continue;
            }

            var isRawUrlLine = HttpUrlRegex.IsMatch(trimmed)
                && HttpUrlRegex.Match(trimmed).Value.Equals(trimmed, StringComparison.Ordinal);
            if (skipImmediateUrl && isRawUrlLine)
            {
                skipImmediateUrl = false;
                continue;
            }

            skipImmediateUrl = false;

            if (TryExtractDisplaySourceLineValue(trimmed, out var sourceLineValue))
            {
                var cleanedSource = Regex.Replace(sourceLineValue, @"https?://\S+", string.Empty);
                cleanedSource = cleanedSource.Replace("**", string.Empty, StringComparison.Ordinal);
                cleanedSource = Regex.Replace(cleanedSource, @"\s{2,}", " ").Trim().Trim(',', ';', '|', '/');
                cleanedSource = FilterVisibleDisplaySourceLine(cleanedSource);
                if (cleanedSource.Length == 0)
                {
                    continue;
                }

                output.Add($"출처: {cleanedSource}");
                continue;
            }

            if (isRawUrlLine
                && output.Count > 0
                && IsDisplaySourceLine(output[^1]))
            {
                continue;
            }

            output.Add(trimmed);
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static string NormalizeWebAnswerNarrativeParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 4);
        var paragraphParts = new List<string>(8);

        void FlushParagraph()
        {
            if (paragraphParts.Count == 0)
            {
                return;
            }

            var merged = string.Join(" ", paragraphParts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));
            merged = Regex.Replace(merged, @"\s{2,}", " ").Trim();
            merged = Regex.Replace(merged, @"\s+([,.;:!?])", "$1");
            if (merged.Length > 0)
            {
                output.Add(merged);
            }

            paragraphParts.Clear();
        }

        foreach (var raw in lines)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                continue;
            }

            if (IsNarrativeWebAnswerLine(trimmed))
            {
                paragraphParts.Add(trimmed);
                continue;
            }

            FlushParagraph();
            if (TryNormalizeWebAnswerStructuredLabelLine(trimmed, out var normalizedStructuredLine))
            {
                if (output.Count > 0
                    && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(normalizedStructuredLine);
                continue;
            }

            output.Add(trimmed);
        }

        FlushParagraph();
        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static bool TryNormalizeWebAnswerStructuredLabelLine(string line, out string normalizedLine)
    {
        normalizedLine = string.Empty;
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (TryNormalizeExistingStructuredMarkdownLabelLine(trimmed, out normalizedLine))
        {
            return true;
        }

        return TryFormatStructuredMarkdownLabelLine(trimmed, out normalizedLine);
    }

    private static bool IsNarrativeWebAnswerLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal)
            || IsMarkdownTableRow(trimmed)
            || trimmed.StartsWith("요약:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("핵심:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("**핵심:**", StringComparison.OrdinalIgnoreCase)
            || IsDisplaySourceLine(trimmed))
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, @"^(?:[-•▪*]\s+|\d+[.)]\s+)", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (Regex.IsMatch(
                trimmed,
                @"^(?:\*\*)?\s*[A-Za-z가-힣0-9(][A-Za-z가-힣0-9(),.&+_/\-·\s]{0,40}\s*(?:\*\*)?\s*[:：]",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        return !HttpUrlRegex.IsMatch(trimmed);
    }

    private static string EnsureMarkdownTableResponseIfRequested(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = ConvertDelimitedPlainTextTableToMarkdown(normalized);

        var hasTableHeader = Regex.IsMatch(normalized, @"(?m)^\s*\|.+\|\s*$", RegexOptions.CultureInvariant);
        var hasTableSeparator = Regex.IsMatch(
            normalized,
            @"(?m)^\s*\|\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|\s*$",
            RegexOptions.CultureInvariant
        );
        if (hasTableHeader && hasTableSeparator)
        {
            return NormalizeMarkdownTableResponseMetadata(normalized);
        }

        var sourceNames = new List<string>(8);
        var intro = new List<string>(4);
        var rows = new List<(string Key, string Value)>(8);
        var currentSection = string.Empty;
        var currentSectionItems = new List<string>(4);
        var metadataSection = string.Empty;

        static string NormalizeMetadataLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value, @"[*`_~\[\]\(\)]", string.Empty);
            normalized = Regex.Replace(normalized, @"\s+", string.Empty);
            return normalized.Trim().ToLowerInvariant();
        }

        static bool IsSourceMetadataLabel(string label)
        {
            var normalized = NormalizeMetadataLabel(label);
            return normalized.StartsWith("출처", StringComparison.Ordinal)
                || normalized.Equals("source", StringComparison.Ordinal)
                || normalized.Equals("sources", StringComparison.Ordinal);
        }

        static bool IsSummaryMetadataLabel(string label)
        {
            var normalized = NormalizeMetadataLabel(label);
            return normalized.StartsWith("요약", StringComparison.Ordinal)
                || normalized.Equals("summary", StringComparison.Ordinal);
        }

        static string StripListPrefix(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            normalized = Regex.Replace(normalized, @"^(?:[-•▪●◆▶▷]\s*)+", string.Empty);
            return normalized.Trim();
        }

        void AppendSourceNames(string rawValue)
        {
            var normalized = StripListPrefix(rawValue);
            if (normalized.Length == 0)
            {
                return;
            }

            normalized = Regex.Replace(normalized, @"^(?:\*\*)?\s*출처\s*(?:\*\*)?\s*[:：]\s*", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s{2,}", " ");
            if (normalized.Length == 0)
            {
                return;
            }

            foreach (var token in normalized.Split(new[] { ',', '·', '/', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = token.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (name.StartsWith("출처", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sourceNames.Add(name);
            }
        }

        void FlushSection()
        {
            if (string.IsNullOrWhiteSpace(currentSection))
            {
                currentSectionItems.Clear();
                return;
            }

            var value = currentSectionItems.Count == 0
                ? "-"
                : string.Join(" / ", currentSectionItems.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()));
            value = string.IsNullOrWhiteSpace(value) ? "-" : value;
            rows.Add((SanitizeTableCell(currentSection), SanitizeTableCell(value)));
            currentSection = string.Empty;
            currentSectionItems.Clear();
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (metadataSection.Equals("source", StringComparison.Ordinal))
            {
                AppendSourceNames(line);
                continue;
            }

            if (metadataSection.Equals("summary", StringComparison.Ordinal))
            {
                var summaryValue = StripListPrefix(line);
                if (summaryValue.Length > 0)
                {
                    intro.Add(summaryValue);
                }
                continue;
            }

            var sectionMatch = Regex.Match(line, @"^(?:[■□▪●◆▶▷]\s*)+(?<section>.+)$", RegexOptions.CultureInvariant);
            if (sectionMatch.Success)
            {
                FlushSection();
                var sectionName = sectionMatch.Groups["section"].Value.Trim();
                if (IsSourceMetadataLabel(sectionName))
                {
                    metadataSection = "source";
                    continue;
                }

                if (IsSummaryMetadataLabel(sectionName))
                {
                    metadataSection = "summary";
                    continue;
                }

                metadataSection = string.Empty;
                currentSection = sectionName;
                continue;
            }

            var bulletMatch = Regex.Match(line, @"^(?:[-•·●]\s*)(?<body>.+)$", RegexOptions.CultureInvariant);
            if (bulletMatch.Success)
            {
                var body = bulletMatch.Groups["body"].Value.Trim();
                if (body.Length == 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentSection))
                {
                    currentSectionItems.Add(body);
                    continue;
                }

                var keyValueBullet = Regex.Match(body, @"^(?<k>[^:：]{1,40})\s*[:：]\s*(?<v>.+)$", RegexOptions.CultureInvariant);
                if (keyValueBullet.Success)
                {
                    var key = keyValueBullet.Groups["k"].Value.Trim();
                    var value = keyValueBullet.Groups["v"].Value.Trim();
                    if (IsSourceMetadataLabel(key))
                    {
                        AppendSourceNames(value);
                        continue;
                    }

                    if (IsSummaryMetadataLabel(key))
                    {
                        if (value.Length > 0)
                        {
                            intro.Add($"요약: {value}");
                        }

                        continue;
                    }

                    rows.Add((
                        SanitizeTableCell(key),
                        SanitizeTableCell(value)
                    ));
                }
                else
                {
                    rows.Add(("핵심", SanitizeTableCell(body)));
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentSection))
            {
                currentSectionItems.Add(line);
                continue;
            }

            var keyValueLine = Regex.Match(
                line,
                @"^(?:\*\*)?\s*(?<k>[^:：]{1,40})\s*(?:\*\*)?\s*[:：]\s*(?<v>.+)$",
                RegexOptions.CultureInvariant
            );
            if (keyValueLine.Success)
            {
                var key = keyValueLine.Groups["k"].Value.Trim();
                var value = keyValueLine.Groups["v"].Value.Trim();
                if (IsSourceMetadataLabel(key))
                {
                    AppendSourceNames(value);
                    continue;
                }

                if (IsSummaryMetadataLabel(key))
                {
                    if (value.Length > 0)
                    {
                        intro.Add($"요약: {value}");
                    }

                    continue;
                }
            }

            intro.Add(line);
        }

        FlushSection();

        if (rows.Count == 0)
        {
            return normalized;
        }

        var output = new List<string>(rows.Count + 6);
        if (intro.Count > 0)
        {
            output.AddRange(intro.Take(2));
            output.Add(string.Empty);
        }

        output.Add("| 구분 | 주요 내용 |");
        output.Add("| --- | --- |");
        foreach (var row in rows)
        {
            output.Add($"| {row.Key} | {row.Value} |");
        }

        var distinctSources = FilterVisibleDisplaySources(sourceNames)
            .ToList();
        if (distinctSources.Count > 0)
        {
            output.Add(string.Empty);
            output.Add($"출처: {string.Join(", ", distinctSources)}");
        }

        return string.Join('\n', output).Trim();
    }

    private static string ConvertDelimitedPlainTextTableToMarkdown(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 4);
        var index = 0;
        while (index < lines.Length)
        {
            if (!TrySplitDelimitedTableRow(lines[index], out var headerCells, out var delimiterKind))
            {
                output.Add(lines[index]);
                index += 1;
                continue;
            }

            var rows = new List<string[]>(8);
            var cursor = index + 1;
            while (cursor < lines.Length
                && TrySplitDelimitedTableRow(lines[cursor], out var rowCells, out var rowDelimiterKind)
                && rowDelimiterKind == delimiterKind
                && rowCells.Length == headerCells.Length)
            {
                rows.Add(rowCells);
                cursor += 1;
            }

            if (rows.Count == 0)
            {
                output.Add(lines[index]);
                index += 1;
                continue;
            }

            output.Add($"| {string.Join(" | ", headerCells.Select(SanitizeTableCell))} |");
            output.Add($"| {string.Join(" | ", Enumerable.Repeat("---", headerCells.Length))} |");
            foreach (var row in rows)
            {
                output.Add($"| {string.Join(" | ", row.Select(SanitizeTableCell))} |");
            }

            index = cursor;
        }

        return string.Join('\n', output).Trim();
    }

    private static bool TrySplitDelimitedTableRow(string line, out string[] cells, out string delimiterKind)
    {
        cells = Array.Empty<string>();
        delimiterKind = string.Empty;

        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0
            || trimmed.StartsWith("|", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, @"^(?:[-•▪*]\s+|\d+[.)]\s+)", RegexOptions.CultureInvariant)
            || Regex.IsMatch(trimmed, @"^(?:\*\*)?\s*[A-Za-z가-힣0-9(][A-Za-z가-힣0-9().&+_/\-·\s]{0,40}\s*(?:\*\*)?\s*[:：]", RegexOptions.CultureInvariant)
            || HttpUrlRegex.IsMatch(trimmed))
        {
            return false;
        }

        if (trimmed.Contains('\t'))
        {
            var tabCells = trimmed
                .Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(cell => cell.Trim())
                .Where(cell => cell.Length > 0)
                .ToArray();
            if (tabCells.Length >= 3)
            {
                cells = tabCells;
                delimiterKind = "tab";
                return true;
            }
        }

        var spaceCells = Regex.Split(trimmed, @"\s{2,}", RegexOptions.CultureInvariant)
            .Select(cell => cell.Trim())
            .Where(cell => cell.Length > 0)
            .ToArray();
        if (spaceCells.Length >= 3)
        {
            cells = spaceCells;
            delimiterKind = "space";
            return true;
        }

        return false;
    }

    private static string NormalizeMarkdownTableResponseMetadata(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        static string NormalizeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalizedValue = Regex.Replace(value, @"[*`_~\[\]\(\)]", string.Empty);
            normalizedValue = Regex.Replace(normalizedValue, @"\s+", string.Empty);
            return normalizedValue.Trim().ToLowerInvariant();
        }

        static bool IsSourceLabel(string value)
        {
            var normalizedValue = NormalizeLabel(value);
            return normalizedValue.StartsWith("출처", StringComparison.Ordinal)
                || normalizedValue.Equals("source", StringComparison.Ordinal)
                || normalizedValue.Equals("sources", StringComparison.Ordinal);
        }

        static bool StartsWithSourceMetadata(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            return Regex.IsMatch(
                trimmed,
                @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
        }

        static string[] ParseCells(string line)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^1];
            }

            return trimmed
                .Split('|', StringSplitOptions.None)
                .Select(cell => cell.Trim())
                .ToArray();
        }

        static string BuildRow(IReadOnlyList<string> cells)
        {
            return $"| {string.Join(" | ", cells.Select(cell => SanitizeTableCell(cell)))} |";
        }

        static bool IsTableRow(string line)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (trimmed.Length < 3)
            {
                return false;
            }

            if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                return false;
            }

            return trimmed.Count(ch => ch == '|') >= 2;
        }

        static bool IsTableSeparator(string line)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (!IsTableRow(trimmed))
            {
                return false;
            }

            var cells = ParseCells(trimmed);
            if (cells.Length == 0)
            {
                return false;
            }

            foreach (var cell in cells)
            {
                var compact = cell.Replace(" ", string.Empty, StringComparison.Ordinal);
                if (compact.Length < 3 || !Regex.IsMatch(compact, @"^:?-{3,}:?$", RegexOptions.CultureInvariant))
                {
                    return false;
                }
            }

            return true;
        }

        static void AppendSourceNames(List<string> bucket, string rawValue)
        {
            var normalizedValue = (rawValue ?? string.Empty).Trim();
            normalizedValue = Regex.Replace(
                normalizedValue,
                @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]\s*",
                string.Empty,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
            if (normalizedValue.Length == 0)
            {
                return;
            }

            var split = normalizedValue.Split(new[] { ',', '·', '/', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in split)
            {
                var name = token.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (name.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.StartsWith("출처", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bucket.Add(name);
            }
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var tableStart = -1;
        var tableEnd = -1;
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (!IsTableRow(lines[i]) || !IsTableSeparator(lines[i + 1]))
            {
                continue;
            }

            tableStart = i;
            var cursor = i + 2;
            while (cursor < lines.Length && IsTableRow(lines[cursor]))
            {
                cursor++;
            }

            tableEnd = cursor - 1;
            break;
        }

        if (tableStart < 0 || tableEnd < tableStart + 1)
        {
            return normalized;
        }

        var sourceNames = new List<string>(8);
        var beforeLines = new List<string>(Math.Max(0, tableStart));
        var afterLines = new List<string>(Math.Max(0, lines.Length - tableEnd - 1));

        for (var i = 0; i < tableStart; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length > 0
                && Regex.IsMatch(trimmed, @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                AppendSourceNames(sourceNames, trimmed);
                continue;
            }

            beforeLines.Add(lines[i]);
        }

        for (var i = tableEnd + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length > 0
                && Regex.IsMatch(trimmed, @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                AppendSourceNames(sourceNames, trimmed);
                continue;
            }

            afterLines.Add(lines[i]);
        }

        var headerCells = ParseCells(lines[tableStart]);
        if (headerCells.Length == 0)
        {
            return normalized;
        }

        var sourceColumnIndexes = headerCells
            .Select((cell, index) => (cell, index))
            .Where(item => IsSourceLabel(item.cell))
            .Select(item => item.index)
            .ToHashSet();

        var keepColumnIndexes = Enumerable.Range(0, headerCells.Length)
            .Where(index => !sourceColumnIndexes.Contains(index))
            .ToArray();

        if (keepColumnIndexes.Length == 0)
        {
            return normalized;
        }

        var rebuiltDataRows = new List<string[]>(Math.Max(0, tableEnd - tableStart - 1));
        var movedFromTable = sourceColumnIndexes.Count > 0;
        for (var i = tableStart + 2; i <= tableEnd; i++)
        {
            var parsed = ParseCells(lines[i]);
            var rowCells = new string[headerCells.Length];
            for (var col = 0; col < headerCells.Length; col++)
            {
                rowCells[col] = col < parsed.Length ? parsed[col] : string.Empty;
            }

            var mergedRowText = string.Join(", ", rowCells.Where(value => !string.IsNullOrWhiteSpace(value)));
            var isSourceMetadataRow = rowCells.Any(StartsWithSourceMetadata)
                || (rowCells.Length > 0 && IsSourceLabel(rowCells[0]));
            if (isSourceMetadataRow)
            {
                AppendSourceNames(sourceNames, mergedRowText);
                movedFromTable = true;
                continue;
            }

            foreach (var sourceIndex in sourceColumnIndexes)
            {
                if (sourceIndex >= 0 && sourceIndex < rowCells.Length)
                {
                    AppendSourceNames(sourceNames, rowCells[sourceIndex]);
                }
            }

            var filtered = keepColumnIndexes.Select(index => rowCells[index]).ToArray();
            if (filtered.All(value => string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            rebuiltDataRows.Add(filtered);
        }

        if (!movedFromTable && sourceNames.Count == 0)
        {
            return normalized;
        }

        var rebuilt = new List<string>(lines.Length + 4);
        rebuilt.AddRange(beforeLines);

        var filteredHeader = keepColumnIndexes.Select(index => headerCells[index]).ToArray();
        rebuilt.Add(BuildRow(filteredHeader));
        rebuilt.Add(BuildRow(Enumerable.Repeat("---", filteredHeader.Length).ToArray()));
        foreach (var row in rebuiltDataRows)
        {
            rebuilt.Add(BuildRow(row));
        }

        rebuilt.AddRange(afterLines);

        var distinctSources = FilterVisibleDisplaySources(sourceNames)
            .ToList();

        if (distinctSources.Count > 0)
        {
            if (rebuilt.Count > 0 && !string.IsNullOrWhiteSpace(rebuilt[^1]))
            {
                rebuilt.Add(string.Empty);
            }

            rebuilt.Add($"출처: {string.Join(", ", distinctSources)}");
        }

        return string.Join('\n', rebuilt).Trim();
    }

    private static string SanitizeTableCell(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace("|", "/", StringComparison.Ordinal)
            .Trim();
        normalized = Regex.Replace(normalized, @"\s{2,}", " ");
        return normalized.Length == 0 ? "-" : normalized;
    }

    private string BuildGeminiUrlContextAnswerPrompt(
        string input,
        IReadOnlyList<string> urls,
        string memoryHint,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle,
        bool includeGoogleSearch,
        RepositoryContextSnapshot? repositoryContext = null
    )
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var effectiveInput = ResolveImplicitUrlRequest(normalizedInput, urls);
        var normalizedMemoryHint = (memoryHint ?? string.Empty).Trim();
        var hasExplicitCount = HasExplicitRequestedCountInQuery(effectiveInput);
        var requestedCount = hasExplicitCount
            ? Math.Clamp(ResolveRequestedResultCountFromQuery(effectiveInput), 1, 20)
            : ResolveWebDefaultCount(effectiveInput);
        var listMode = LooksLikeListOutputRequest(effectiveInput);
        var tableMode = allowMarkdownTable && LooksLikeTableRenderRequest(effectiveInput);
        var comparisonMode = LooksLikeComparisonRequest(effectiveInput);
        var primaryUrl = urls.FirstOrDefault() ?? string.Empty;
        var siteOverviewMode = urls.Count == 1
            && (LooksLikeSiteOverviewRequest(effectiveInput) || LooksLikeImplicitUrlSummaryRequest(normalizedInput, urls))
            && LooksLikeSiteRootUrl(primaryUrl);
        var repositoryMode = urls.Count == 1 && LooksLikeRepositoryUrl(primaryUrl);
        var documentMode = urls.Count == 1 && LooksLikeDocumentationUrl(primaryUrl);
        var articleMode = urls.Count == 1 && LooksLikeArticleUrl(primaryUrl);

        var builder = new StringBuilder();
        builder.AppendLine("너는 URL 컨텍스트 기반 한국어 답변기다.");
        builder.AppendLine("- 아래 참조 URL 내용이 1차 근거다.");
        if (includeGoogleSearch)
        {
            builder.AppendLine("- google_search가 가능하면 배경 보강이나 최신성 확인에만 보조적으로 사용해라.");
            builder.AppendLine("- URL에 없는 내용은 추정하지 말고, 검색으로도 확인되지 않으면 없다고 말해라.");
        }
        else
        {
            builder.AppendLine("- URL에 없는 내용은 추정하지 말고, URL에서 직접 확인되지 않으면 없다고 말해라.");
        }
        builder.AppendLine("- 허위/기억 기반 문장 금지.");
        builder.AppendLine("- 출처는 URL이 아닌 도메인명으로만 작성해라.");
        builder.AppendLine("- 출처 표기는 마지막 한 줄 형식으로만 작성: '출처: 도메인1, 도메인2'.");
        builder.AppendLine("- '출처 링크:' 섹션이나 URL 단독 줄을 만들지 마라.");
        builder.AppendLine("- 문장 중간을 임의로 줄바꿈하지 말고 자연스러운 한국어 문장으로 정리해라.");
        builder.AppendLine("- 답변이 길어질 것 같으면 항목 수를 줄이고 요약해서 문장을 끝까지 완성해라.");
        builder.AppendLine("- 페이지의 제목, 목적, 핵심 내용, 중요한 세부사항이 무엇인지 요약하면서도 분명하게 드러내라.");
        builder.AppendLine("- 본문에서 임의의 '**' 강조를 남발하지 말고, 구조 라벨이나 항목 제목에만 제한적으로 사용해라.");
        if (repositoryContext.HasValue)
        {
            builder.AppendLine("- 아래 [직접 읽은 저장소 정보] 블록이 있으면 그 내용을 URL 도구 결과보다 우선 근거로 사용해라.");
            builder.AppendLine("- 사용자가 특정 키워드나 주제만 물으면 [직접 읽은 저장소 정보]에서 직접 확인된 내용만 답하고, 없으면 없다고 말해라.");
        }
        if (tableMode)
        {
            builder.AppendLine("- 사용자가 표를 요청했으므로 반드시 GitHub 마크다운 표로 작성해라.");
            builder.AppendLine("- 표의 헤더/구분선/데이터 행은 모두 '|'로 시작하고 '|'로 끝내라.");
            builder.AppendLine("- 표를 코드블록으로 감싸지 마라.");
            builder.AppendLine("- 표 안에 '출처' 열이나 '출처' 행을 만들지 마라.");
        }
        else
        {
            builder.AppendLine("- 사용자가 표를 요청하지 않았다면 표/ASCII 테이블 형식은 쓰지 마라.");
        }
        if (enforceTelegramOutputStyle)
        {
            builder.AppendLine("- 출력 채널은 텔레그램이다. 군더더기 머리말 없이 바로 본문 형식으로 작성해라.");
        }
        if (siteOverviewMode)
        {
            builder.AppendLine("- 이 요청은 사이트/서비스 소개 요청이다.");
            builder.AppendLine("- 해당 사이트가 무엇을 하는지, 핵심 제품/기능, 누구를 위한 서비스인지, 눈여겨볼 점을 정리해라.");
        }
        else if (repositoryMode)
        {
            builder.AppendLine("- 이 요청은 코드 저장소/프로젝트 소개 요청이다.");
            builder.AppendLine("- 저장소가 무엇을 하는지, 핵심 기능, 사용 대상, 주요 구조, 눈에 띄는 포인트를 정리해라.");
        }
        else if (documentMode)
        {
            builder.AppendLine("- 이 요청은 문서/가이드 설명 요청이다.");
            builder.AppendLine("- 문서의 목적, 어떤 기능을 설명하는지, 사용 흐름, 지원 대상, 제한/주의점을 정리해라.");
        }
        else if (articleMode)
        {
            builder.AppendLine("- 이 요청은 기사/게시물 요약 요청이다.");
            builder.AppendLine("- 핵심 사실, 주요 인물/기관, 중요한 수치/날짜, 왜 중요한지를 우선 정리해라.");
        }

        if (tableMode)
        {
            builder.AppendLine("요약: <1문장>");
            builder.AppendLine();
            builder.AppendLine("| 항목 | 내용 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| 예시 | 값 |");
            builder.AppendLine();
            builder.AppendLine("출처: 도메인1, 도메인2");
            if (listMode)
            {
                builder.AppendLine($"- 표의 데이터 행은 가능하면 {requestedCount}개로 맞춰라.");
            }
        }
        else if (listMode)
        {
            builder.AppendLine($"- 목록 모드: 목표 {requestedCount}건.");
            builder.AppendLine("- 각 항목은 제목과 핵심 내용만 간결하게 정리해라.");
        }
        else
        {
            builder.AppendLine("- 일반 검색형 답변은 아래 형식만 사용해라.");
            builder.AppendLine("요약: <2~3문장>");
            builder.AppendLine();
            builder.AppendLine("무엇을 다루나: <1~2문장>");
            builder.AppendLine();
            builder.AppendLine("핵심:");
            builder.AppendLine("- <핵심 포인트 3~5개>");
            builder.AppendLine("- <핵심 포인트>");
            builder.AppendLine();
            builder.AppendLine("중요 포인트:");
            builder.AppendLine("- <사용자가 바로 알아야 할 점>");
            builder.AppendLine();
            builder.AppendLine("출처: 도메인1, 도메인2");
            if (comparisonMode)
            {
                builder.AppendLine("- 비교/분류형 답변이면 항목별 줄바꿈을 유지해라.");
            }
        }

        if (normalizedMemoryHint.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("사용자 선호 메모리(보조 규칙, 충돌 시 무시):");
            builder.AppendLine(normalizedMemoryHint);
        }

        if (repositoryContext.HasValue)
        {
            builder.AppendLine();
            builder.AppendLine("[직접 읽은 저장소 정보]");
            builder.AppendLine($"저장소: {repositoryContext.Value.RepositorySlug}");
            if (!string.IsNullOrWhiteSpace(repositoryContext.Value.Description))
            {
                builder.AppendLine($"설명: {repositoryContext.Value.Description}");
            }

            builder.AppendLine("README 발췌:");
            builder.AppendLine(repositoryContext.Value.ReadmeText);
        }

        builder.AppendLine();
        builder.AppendLine("참조 URL:");
        foreach (var url in urls.Take(3))
        {
            builder.AppendLine($"- {url}");
        }
        builder.AppendLine();
        builder.AppendLine("사용자 입력:");
        builder.AppendLine(effectiveInput);
        return builder.ToString().Trim();
    }

    private async Task<RepositoryContextSnapshot?> TryLoadRepositoryContextSnapshotAsync(
        string input,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken
    )
    {
        if (urls.Count != 1)
        {
            return null;
        }

        var primaryUrl = urls[0];
        if (!TryParseGitHubRepositoryRoot(primaryUrl, out var owner, out var repo))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, primaryUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Omni-node/1.0");
            using var response = await WebFetchClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var description = TryExtractHtmlMetaContent(html, "description");
            var (refName, readmePath, readmeFallbackText) = TryExtractGitHubReadmeInfo(html);
            var readmeText = await TryFetchGitHubReadmeRawAsync(owner, repo, refName, readmePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(readmeText))
            {
                readmeText = readmeFallbackText;
            }

            readmeText = BuildRelevantRepositoryExcerpt(input, readmeText);
            if (string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(readmeText))
            {
                return null;
            }

            return new RepositoryContextSnapshot(
                $"{owner}/{repo}",
                description,
                readmeText
            );
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseGitHubRepositoryRoot(string url, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
        if (!ContainsAny(host, "github.com"))
        {
            return false;
        }

        var segments = (uri.AbsolutePath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        owner = segments[0];
        repo = segments[1];
        return owner.Length > 0 && repo.Length > 0;
    }

    private static string TryExtractHtmlMetaContent(string html, string metaName)
    {
        var targetName = (metaName ?? string.Empty).Trim();
        if (targetName.Length == 0 || string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var patterns = new[]
        {
            $"<meta\\s+name=\"{Regex.Escape(targetName)}\"\\s+content=\"(?<content>[^\"]*)\"",
            $"<meta\\s+content=\"(?<content>[^\"]*)\"\\s+name=\"{Regex.Escape(targetName)}\"",
            $"<meta\\s+property=\"og:{Regex.Escape(targetName)}\"\\s+content=\"(?<content>[^\"]*)\"",
            $"<meta\\s+content=\"(?<content>[^\"]*)\"\\s+property=\"og:{Regex.Escape(targetName)}\""
        };
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            return WebUtility.HtmlDecode(match.Groups["content"].Value).Trim();
        }

        return string.Empty;
    }

    private static (string RefName, string ReadmePath, string FallbackText) TryExtractGitHubReadmeInfo(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var embeddedMatch = Regex.Match(
            html,
            "<script type=\"application/json\" data-target=\"react-app\\.embeddedData\">(?<json>[\\s\\S]*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!embeddedMatch.Success)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(embeddedMatch.Groups["json"].Value);
            if (!doc.RootElement.TryGetProperty("payload", out var payload))
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            JsonElement overview;
            if (payload.TryGetProperty("codeViewRepoRoute", out var codeViewRepoRoute)
                && codeViewRepoRoute.ValueKind == JsonValueKind.Object
                && codeViewRepoRoute.TryGetProperty("overview", out overview)
                && overview.ValueKind == JsonValueKind.Object)
            {
            }
            else if (payload.TryGetProperty("overview", out overview) && overview.ValueKind == JsonValueKind.Object)
            {
            }
            else
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            if (!overview.TryGetProperty("overviewFiles", out var overviewFiles) || overviewFiles.ValueKind != JsonValueKind.Array)
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            foreach (var item in overviewFiles.EnumerateArray())
            {
                var preferredType = item.TryGetProperty("preferredFileType", out var preferredFileTypeElement)
                    ? (preferredFileTypeElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                if (!string.Equals(preferredType, "readme", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var refName = item.TryGetProperty("refName", out var refNameElement)
                    ? (refNameElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                var readmePath = item.TryGetProperty("path", out var pathElement)
                    ? (pathElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                var richText = item.TryGetProperty("richText", out var richTextElement)
                    ? (richTextElement.GetString() ?? string.Empty)
                    : string.Empty;
                var fallbackText = ConvertGitHubRichTextToPlainText(richText);
                return (refName, readmePath, fallbackText);
            }
        }
        catch
        {
        }

        return (string.Empty, string.Empty, string.Empty);
    }

    private async Task<string> TryFetchGitHubReadmeRawAsync(
        string owner,
        string repo,
        string refName,
        string readmePath,
        CancellationToken cancellationToken
    )
    {
        var normalizedOwner = (owner ?? string.Empty).Trim();
        var normalizedRepo = (repo ?? string.Empty).Trim();
        var normalizedRef = (refName ?? string.Empty).Trim();
        var normalizedPath = (readmePath ?? string.Empty).Trim();
        if (normalizedOwner.Length == 0
            || normalizedRepo.Length == 0
            || normalizedRef.Length == 0
            || normalizedPath.Length == 0)
        {
            return string.Empty;
        }

        var escapedPath = string.Join(
            "/",
            normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.EscapeDataString)
        );
        var rawUrl = $"https://raw.githubusercontent.com/{Uri.EscapeDataString(normalizedOwner)}/{Uri.EscapeDataString(normalizedRepo)}/{Uri.EscapeDataString(normalizedRef)}/{escapedPath}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Omni-node/1.0");
            using var response = await WebFetchClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ConvertGitHubRichTextToPlainText(string richText)
    {
        var normalized = (richText ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"<li\b[^>]*>", "- ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"</(p|div|li|h[1-6]|tr|pre|code|table|ul|ol|blockquote)>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = HtmlBreakTagRegex.Replace(normalized, "\n");
        normalized = HtmlTagRegex.Replace(normalized, " ");
        normalized = WebUtility.HtmlDecode(normalized);
        normalized = normalized.Replace("\r", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[ \t]+\n", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
        return normalized.Trim();
    }

    private static string BuildRelevantRepositoryExcerpt(string input, string readmeText)
    {
        var normalizedText = (readmeText ?? string.Empty).Trim();
        if (normalizedText.Length == 0)
        {
            return string.Empty;
        }

        var lines = SplitRepositoryLines(normalizedText);
        var queryTokens = ExtractRepositoryQueryTokens(input);
        if (queryTokens.Length == 0 || lines.Length == 0)
        {
            return TrimForRepositoryContext(normalizedText, 9000);
        }

        var matchIndexes = FindRepositoryMatchIndexes(lines, queryTokens);
        if (matchIndexes.Count == 0)
        {
            return TrimForRepositoryContext(normalizedText, 9000);
        }

        var builder = new StringBuilder();
        foreach (var index in matchIndexes.OrderBy(value => value))
        {
            var line = lines[index];
            if (builder.Length + line.Length + 1 > 9000)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        var excerpt = builder.ToString().Trim();
        return excerpt.Length == 0 ? TrimForRepositoryContext(normalizedText, 9000) : excerpt;
    }

    private static string TryBuildRepositoryExtractiveAnswer(string input, RepositoryContextSnapshot context)
    {
        var lines = SplitRepositoryLines(context.ReadmeText);
        var queryTokens = ExtractRepositoryQueryTokens(input);
        if (lines.Length == 0 || queryTokens.Length == 0)
        {
            return string.Empty;
        }

        var matchIndexes = FindExactRepositoryMatchIndexes(lines, queryTokens);
        if (matchIndexes.Count == 0)
        {
            return string.Empty;
        }

        var matchedLines = matchIndexes
            .OrderBy(value => value)
            .Select(value => NormalizeRepositoryLineForDisplay(lines[value]))
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        if (matchedLines.Length == 0)
        {
            return string.Empty;
        }

        var topicLabel = BuildRepositoryTopicLabel(queryTokens);
        var builder = new StringBuilder();
        builder.AppendLine($"요약: README에서 {topicLabel} 관련으로 직접 확인되는 내용만 정리합니다.");
        builder.AppendLine();
        builder.AppendLine("확인된 내용:");
        foreach (var line in matchedLines)
        {
            builder.AppendLine($"- {line}");
        }

        if (!string.IsNullOrWhiteSpace(context.Description))
        {
            builder.AppendLine();
            builder.AppendLine("중요 포인트:");
            builder.AppendLine("- 위 항목은 저장소 README/설명에서 직접 확인된 문장만 추린 것이며, 그 밖의 해석은 덧붙이지 않았습니다.");
        }

        builder.AppendLine();
        builder.AppendLine("출처: github.com");
        return builder.ToString().Trim();
    }

    private static string[] ExtractRepositoryQueryTokens(string input)
    {
        var normalized = HttpUrlRegex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), " ");
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{Nd}\s]+", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var rawTokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !IsRepositoryQueryFillerToken(token))
            .ToArray();
        if (rawTokens.Length == 0)
        {
            return Array.Empty<string>();
        }

        var rankedTokens = new List<string>(rawTokens.Length * 2);
        for (var index = 0; index < rawTokens.Length - 1; index += 1)
        {
            var current = rawTokens[index];
            var next = rawTokens[index + 1];
            if (current.Length == 0 || next.Length == 0)
            {
                continue;
            }

            rankedTokens.Add($"{current} {next}");
        }

        foreach (var token in rawTokens)
        {
            if (token.Length >= 2 || token.Any(char.IsDigit))
            {
                rankedTokens.Add(token);
            }
        }

        return rankedTokens
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitRepositoryLines(string text)
    {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static HashSet<int> FindRepositoryMatchIndexes(IReadOnlyList<string> lines, IReadOnlyList<string> queryTokens)
    {
        var matchIndexes = new HashSet<int>();
        for (var index = 0; index < lines.Count; index += 1)
        {
            var normalizedLine = lines[index].ToLowerInvariant();
            var score = 0;
            foreach (var token in queryTokens)
            {
                if (normalizedLine.Contains(token, StringComparison.Ordinal))
                {
                    score += 1;
                }
            }

            if (score <= 0)
            {
                continue;
            }

            matchIndexes.Add(index);
            if (index > 0)
            {
                matchIndexes.Add(index - 1);
            }

            if (index + 1 < lines.Count)
            {
                matchIndexes.Add(index + 1);
            }
        }

        return matchIndexes;
    }

    private static HashSet<int> FindExactRepositoryMatchIndexes(IReadOnlyList<string> lines, IReadOnlyList<string> queryTokens)
    {
        var matchIndexes = new HashSet<int>();
        for (var index = 0; index < lines.Count; index += 1)
        {
            var normalizedLine = lines[index].ToLowerInvariant();
            foreach (var token in queryTokens)
            {
                if (!normalizedLine.Contains(token, StringComparison.Ordinal))
                {
                    continue;
                }

                matchIndexes.Add(index);
                break;
            }
        }

        return matchIndexes;
    }

    private static string BuildRepositoryTopicLabel(IReadOnlyList<string> queryTokens)
    {
        if (queryTokens.Count == 0)
        {
            return "요청한 주제";
        }

        var labels = new List<string>(3);
        foreach (var token in queryTokens)
        {
            var normalizedToken = (token ?? string.Empty).Trim();
            if (normalizedToken.Length == 0)
            {
                continue;
            }

            var overlapsExisting = labels.Any(existing =>
                existing.Contains(normalizedToken, StringComparison.Ordinal)
                || normalizedToken.Contains(existing, StringComparison.Ordinal));
            if (overlapsExisting)
            {
                continue;
            }

            labels.Add(normalizedToken);
            if (labels.Count >= 3)
            {
                break;
            }
        }

        return labels.Count == 0 ? "요청한 주제" : string.Join(" ", labels);
    }

    private static bool IsRepositoryQueryFillerToken(string token)
    {
        return token is
            "이" or "이거" or "이것" or "여기" or "해당" or "링크" or "url" or "주소"
            or "링크에서" or "에서" or "관련" or "내용" or "내용만" or "부분" or "부분만" or "항목" or "얘기" or "말" or "말만" or "말해" or "말해봐" or "말해줘"
            or "설명" or "설명만" or "설명해" or "설명해줘" or "요약" or "요약만" or "요약해" or "요약해줘" or "정리" or "정리해줘"
            or "만" or "만좀" or "만요" or "좀" or "한번" or "봐" or "봐봐" or "봐줘" or "보지말고"
            or "찾아" or "찾아줘" or "추려" or "추려줘" or "뽑아" or "뽑아줘"
            or "what" or "about" or "only" or "from" or "tell" or "show" or "readme";
    }

    private static string TrimForRepositoryContext(string text, int maxChars)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars] + "\n...(truncated)";
    }

    private static string NormalizeRepositoryLineForDisplay(string line)
    {
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Contains("![", StringComparison.Ordinal) || normalized.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"!\[[^\]]*\]\([^)]+\)", string.Empty);
        normalized = Regex.Replace(normalized, @"\[(?<text>[^\]]+)\]\([^)]+\)", "${text}");
        normalized = Regex.Replace(normalized, @"^\s{0,3}(?:>+\s*|[-*+]\s+|#+\s+)", string.Empty);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim();
        return normalized;
    }

    private static string ResolveImplicitUrlRequest(string input, IReadOnlyList<string> urls)
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        if (!LooksLikeImplicitUrlSummaryRequest(normalizedInput, urls))
        {
            return normalizedInput;
        }

        if (urls.Count > 1)
        {
            return "이 URL들의 내용을 요약하고 공통점과 차이를 정리해줘.";
        }

        var primaryUrl = urls.FirstOrDefault() ?? string.Empty;
        if (LooksLikeRepositoryUrl(primaryUrl))
        {
            return "이 코드 저장소/프로젝트가 무엇인지 설명해줘.";
        }

        if (LooksLikeDocumentationUrl(primaryUrl))
        {
            return "이 문서/가이드 내용을 핵심만 요약해줘.";
        }

        if (LooksLikeArticleUrl(primaryUrl))
        {
            return "이 글/기사 내용을 핵심만 요약해줘.";
        }

        if (LooksLikeSiteRootUrl(primaryUrl))
        {
            return "이 사이트/서비스가 무엇인지 설명해줘.";
        }

        return "이 URL 내용을 설명해줘.";
    }

    private static bool LooksLikeImplicitUrlSummaryRequest(string input, IReadOnlyList<string> urls)
    {
        if (urls.Count == 0)
        {
            return false;
        }

        var normalizedInput = (input ?? string.Empty).Trim();
        if (normalizedInput.Length == 0)
        {
            return true;
        }

        var withoutUrls = HttpUrlRegex.Replace(normalizedInput, " ");
        withoutUrls = Regex.Replace(withoutUrls, @"[^\p{L}\p{Nd}\s]+", " ");
        withoutUrls = Regex.Replace(withoutUrls, @"\s+", " ").Trim().ToLowerInvariant();
        if (withoutUrls.Length == 0)
        {
            return true;
        }

        var tokens = withoutUrls
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        foreach (var token in tokens)
        {
            if (!IsUrlFillerToken(token))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUrlFillerToken(string token)
    {
        return token is
            "이" or "이거" or "이곳" or "여기" or "해당"
            or "링크" or "url" or "주소"
            or "사이트" or "페이지" or "문서" or "글" or "기사" or "저장소" or "리포" or "repo" or "프로젝트"
            or "설명" or "요약" or "정리" or "내용" or "소개" or "해석" or "확인"
            or "알려" or "알려줘" or "봐" or "봐줘" or "읽어" or "읽어줘" or "보여" or "보여줘"
            or "좀" or "한번" or "무슨" or "무엇" or "뭐야" or "뭔지";
    }

    private static bool LooksLikeSiteOverviewRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "이 사이트",
            "사이트 내용",
            "사이트 설명",
            "사이트 알려",
            "서비스 설명",
            "회사 소개",
            "기업 소개",
            "무슨 사이트",
            "무슨 서비스"
        );
    }

    private static bool LooksLikeSiteRootUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = (uri.AbsolutePath ?? string.Empty).Trim();
        return path.Length == 0 || path == "/";
    }

    private static bool LooksLikeDocumentationUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).ToLowerInvariant();
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        return ContainsAny(host, "developers", "docs", "api")
               || ContainsAny(path, "/docs", "/doc", "/api", "/guide", "/reference", "/tutorial");
    }

    private static bool LooksLikeArticleUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).ToLowerInvariant();
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        return ContainsAny(host, "news", "blog", "medium", "substack")
               || ContainsAny(path, "/article", "/news", "/blog", "/posts", "/post");
    }

    private static bool LooksLikeRepositoryUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).ToLowerInvariant();
        if (!ContainsAny(host, "github.com", "gitlab.com", "bitbucket.org"))
        {
            return false;
        }

        var segments = (uri.AbsolutePath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var reserved = segments.Length >= 3 ? segments[2].ToLowerInvariant() : string.Empty;
        if (reserved.Length == 0)
        {
            return true;
        }

        return reserved is not (
            "issues" or "pull" or "pulls" or "discussions" or "wiki" or "releases"
            or "actions" or "blob" or "tree" or "commit" or "commits" or "raw"
        );
    }

    private string BuildGeminiWebAnswerPrompt(
        string input,
        string memoryHint,
        bool selfDecideNeedWeb,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle
    )
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var normalizedMemoryHint = (memoryHint ?? string.Empty).Trim();
        var hasExplicitCount = HasExplicitRequestedCountInQuery(normalizedInput);
        var requestedCount = hasExplicitCount
            ? Math.Clamp(ResolveRequestedResultCountFromQuery(normalizedInput), 1, 20)
            : ResolveWebDefaultCount(normalizedInput);
        var sourceFocus = ExtractSourceFocusHintFromInput(normalizedInput);
        var sourceDomain = ResolveSourceDomainFromQueryOrFocus(normalizedInput, sourceFocus);
        var listMode = LooksLikeListOutputRequest(normalizedInput);
        var tableMode = allowMarkdownTable && LooksLikeTableRenderRequest(normalizedInput);
        var comparisonMode = LooksLikeComparisonRequest(normalizedInput);

        var builder = new StringBuilder();
        builder.AppendLine("너는 최신 웹 근거 기반 한국어 답변기다.");
        builder.AppendLine("- 현재 사용자 입력을 최우선으로 따른다.");
        builder.AppendLine("- 선호 메모리가 있더라도 현재 입력과 충돌하면 즉시 무시한다.");
        builder.AppendLine("- 사실/수치/날짜/가격/사건 정보는 웹 근거만 사용하고 추정하지 마라.");
        builder.AppendLine("- 사용자가 특정 사이트/매체(AccuWeather, weather.com, 인베스팅닷컴, investing.com, 연합뉴스, Bloomberg, naver.com 등)를 언급하면 그 도메인을 우선 검색해라. 검색어에 site: 연산자 또는 도메인명을 포함해 1차 시도해라.");
        builder.AppendLine("- 1차 검색에서 정보가 충분하지 않으면 키워드를 바꿔(영문/한글 변환, 동의어, 기간 명시, 단위 명시, 추가 매체 포함) 최소 2~3차례 재검색해라.");
        builder.AppendLine("- 검색 결과에 명시되지 않은 구체 수치(가격, 지수, 기온, 강수확률, 통계, 날짜 등)는 절대 만들어내지 마라. 결과에 없으면 '검색 결과에 해당 데이터가 명확하게 없습니다'라고 솔직히 답하고 어디서 확인하면 되는지 안내만 해라.");
        builder.AppendLine("- 미래 시점(수개월~수년 뒤) 예측은 일반적으로 부정확하다는 점을 명시하고, 명시된 공식 예보/장기 전망 출처에서 인용한 값만 사용해라.");
        builder.AppendLine("- 답변에 '확인 중', '확인했습니다', '추출 완료', '잠시만요' 같은 진행 안내문이나 거짓 보고를 넣지 마라. 결론과 핵심부터 직접 작성해라.");
        if (selfDecideNeedWeb)
        {
            builder.AppendLine("- 먼저 사용자 입력만 보고 웹검색 필요 여부를 스스로 판단해라.");
            builder.AppendLine("- 웹검색이 불필요하면 도구 호출 없이 바로 답변해라.");
        }
        else
        {
            builder.AppendLine("- 이번 요청은 웹검색으로만 답변해라.");
        }

        builder.AppendLine("- 허위/기억 기반 문장 금지.");
        builder.AppendLine("- 출처는 URL이 아닌 매체명으로만 작성해라.");
        builder.AppendLine("- 출처 표기는 마지막 한 줄 형식으로만 작성: '출처: 매체1, 매체2, 매체3'.");
        builder.AppendLine("- '출처 링크:' 섹션이나 URL 단독 줄을 만들지 마라.");
        builder.AppendLine("- 문장 중간을 임의로 줄바꿈하지 말고 자연스러운 한국어 문장으로 정리해라.");
        if (tableMode)
        {
            builder.AppendLine("- 사용자가 표를 요청했으므로 반드시 GitHub 마크다운 표로 작성해라.");
            builder.AppendLine("- 표의 헤더/구분선/데이터 행은 모두 '|'로 시작하고 '|'로 끝내라.");
            builder.AppendLine("- 표를 코드블록으로 감싸지 마라.");
            builder.AppendLine("- 표 안에 '출처' 열이나 '출처' 행을 만들지 마라.");
            builder.AppendLine("- 표 요청 응답에서는 불릿 목록으로 대체하지 마라.");
            builder.AppendLine("- 표가 필요한 정보는 반드시 표의 행/열로 정리해라.");
        }
        else
        {
            builder.AppendLine("- 사용자가 표를 요청하지 않았다면 표/ASCII 테이블 형식은 쓰지 마라.");
        }
        if (enforceTelegramOutputStyle)
        {
            builder.AppendLine("- 출력 채널은 텔레그램이다. 군더더기 머리말 없이 바로 본문 형식으로 작성해라.");
            builder.AppendLine("- URL 본문 노출은 금지하고 매체명만 사용해라.");
        }
        if (sourceFocus.Length > 0)
        {
            builder.AppendLine($"- 사용자가 요구한 소스 초점: {sourceFocus}");
            if (sourceDomain.Length > 0)
            {
                builder.AppendLine($"- 가능한 경우 {sourceDomain} 원출처 기사 우선.");
            }
        }

        if (tableMode)
        {
            builder.AppendLine("- 표 요청 모드: 아래 형식만 사용해라.");
            builder.AppendLine("요약: <1문장>");
            builder.AppendLine();
            builder.AppendLine("| 항목 | 내용 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| 예시 | 값 |");
            builder.AppendLine();
            builder.AppendLine("출처: 매체1, 매체2");
            builder.AppendLine("- 표 앞뒤에 불릿 목록을 쓰지 마라.");
            builder.AppendLine("- 가능한 경우 실제 데이터 열 이름을 사용해 3열 이상 표로 확장해라.");
            if (listMode)
            {
                builder.AppendLine($"- 표의 데이터 행은 가능하면 {requestedCount}개로 맞춰라.");
            }
        }
        else if (listMode)
        {
            builder.AppendLine($"- 목록 모드: 목표 {requestedCount}건.");
            builder.AppendLine("- 각 항목은 제목과 핵심 내용만 간결하게 정리해라.");
            if (hasExplicitCount)
            {
                builder.AppendLine($"- 사용자가 건수를 명시했으므로 가능하면 정확히 {requestedCount}건을 작성해라.");
            }
            else
            {
                builder.AppendLine($"- 건수 미지정 요청이므로 기본 {requestedCount}건으로 작성해라.");
            }
        }
        else
        {
            builder.AppendLine("- 일반 질의 모드: 핵심 답변을 간결하게 작성해라.");
            if (comparisonMode)
            {
                builder.AppendLine("- 비교/분류형 답변이면 항목별 줄바꿈을 유지해라.");
            }
            else
            {
                builder.AppendLine("- 일반 검색형 답변은 아래 형식만 사용해라.");
                builder.AppendLine("요약: <1~2문장>");
                builder.AppendLine();
                builder.AppendLine("핵심:");
                builder.AppendLine("- <핵심 포인트>");
                builder.AppendLine("- <핵심 포인트>");
                builder.AppendLine();
                builder.AppendLine("출처: 매체1, 매체2");
                builder.AppendLine("- 위 형식 외의 제목/머리말/날짜 안내문은 추가하지 마라.");
            }
        }

        if (normalizedMemoryHint.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("사용자 선호 메모리(보조 규칙, 충돌 시 무시):");
            builder.AppendLine(normalizedMemoryHint);
        }

        builder.AppendLine();
        builder.AppendLine("사용자 입력:");
        builder.AppendLine(normalizedInput);
        return builder.ToString().Trim();
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

        if (ShouldBlockWebMemoryHintByOverride(normalizedInput))
        {
            return string.Empty;
        }

        var sourceFocus = ExtractSourceFocusHintFromInput(normalizedInput);
        var sourceDomain = ResolveSourceDomainFromQueryOrFocus(normalizedInput, sourceFocus);
        var hasSourceOverride = sourceFocus.Length > 0
            || sourceDomain.Length > 0
            || normalizedInput.Contains("site:", StringComparison.OrdinalIgnoreCase);
        var hasCountOverride = HasExplicitRequestedCountInQuery(normalizedInput);
        var hasFormatOverride = LooksLikeWebFormatDirective(normalizedInput);
        var hasToneOverride = LooksLikeWebToneDirective(normalizedInput);
        var hasLanguageOverride = LooksLikeWebLanguageDirective(normalizedInput);
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
                foreach (var hint in ExtractWebPreferenceHints(message, fromMemoryNote: false))
                {
                    var key = NormalizeWebPreferenceKey(hint.Text);
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

            foreach (var hint in ExtractWebPreferenceHints(read.Content, fromMemoryNote: true))
            {
                var key = NormalizeWebPreferenceKey(hint.Text);
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

    private static IReadOnlyList<WebPreferenceHint> ExtractWebPreferenceHints(string text, bool fromMemoryNote)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<WebPreferenceHint>();
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var hints = new List<WebPreferenceHint>(8);
        foreach (var raw in lines.Take(120))
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            line = Regex.Replace(line, @"^[-*•\d\.\)\s]+", string.Empty).Trim();
            if (line.Length < 4 || line.Length > 96)
            {
                continue;
            }

            if (fromMemoryNote
                && (line.StartsWith("created_utc", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("mode", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("conversation_id", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("conversation_title", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("provider", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("model", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("#", StringComparison.Ordinal)))
            {
                continue;
            }

            if (!LooksLikeWebPreferenceLine(line, fromMemoryNote))
            {
                continue;
            }

            var category = ClassifyWebPreferenceCategory(line);
            if (category.Length == 0)
            {
                continue;
            }

            hints.Add(new WebPreferenceHint(category, line));
            if (hints.Count >= 8)
            {
                break;
            }
        }

        return hints;
    }

    private static bool LooksLikeWebPreferenceLine(string line, bool fromMemoryNote)
    {
        var lowered = (line ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        if (lowered.Contains("http://", StringComparison.Ordinal)
            || lowered.Contains("https://", StringComparison.Ordinal))
        {
            return false;
        }

        if (ContainsAny(lowered, "가격", "시세", "주가", "배럴", "달러", "환율", "정확한 날짜", "대기압", "수치"))
        {
            return false;
        }

        if (!fromMemoryNote
            && !ContainsAny(lowered, "항상", "앞으로", "이제부터", "매번", "선호", "기억", "기본"))
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "출처",
            "매체",
            "source",
            "site:",
            "형식",
            "포맷",
            "불릿",
            "번호",
            "목록",
            "리스트",
            "한줄",
            "줄바꿈",
            "간결",
            "짧게",
            "자세히",
            "말투",
            "존댓말",
            "반말",
            "한국어",
            "한글",
            "영어",
            "english",
            "korean",
            "cnn",
            "reuters",
            "bbc",
            "연합뉴스",
            "뉴시스",
            "kbs",
            "mbc",
            "sbs",
            "건수",
            "no.n"
        ) || RequestedCountRegex.IsMatch(lowered) || TopCountRegex.IsMatch(lowered);
    }

    private static string ClassifyWebPreferenceCategory(string line)
    {
        var lowered = (line ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return string.Empty;
        }

        if (ContainsAny(lowered, "출처", "매체", "source", "site:", "cnn", "reuters", "bbc", "연합뉴스", "뉴시스", "kbs", "mbc", "sbs"))
        {
            return "source";
        }

        if (ContainsAny(lowered, "형식", "포맷", "불릿", "번호", "목록", "리스트", "한줄", "줄바꿈", "markdown", "마크다운", "no.n"))
        {
            return "format";
        }

        if (ContainsAny(lowered, "간결", "짧게", "자세히", "길게", "말투", "존댓말", "반말", "톤"))
        {
            return "tone";
        }

        if (ContainsAny(lowered, "한국어", "한글", "영어", "english", "korean"))
        {
            return "language";
        }

        var hasCount = RequestedCountRegex.IsMatch(lowered)
            || TopCountRegex.IsMatch(lowered)
            || Regex.IsMatch(lowered, @"(?<!\d)\d{1,2}\s*(개|건)", RegexOptions.CultureInvariant);
        if (hasCount && ContainsAny(lowered, "뉴스", "news", "헤드라인", "목록", "리스트", "건수"))
        {
            return "count";
        }

        return string.Empty;
    }

    private static string NormalizeWebPreferenceKey(string text)
    {
        return Regex.Replace((text ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static bool ShouldBlockWebMemoryHintByOverride(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "말고",
            "제외",
            "빼고",
            "아니고",
            "반대로",
            "다르게",
            "바꿔",
            "변경",
            "무시",
            "이번엔",
            "이번에는"
        );
    }

    private static bool LooksLikeWebFormatDirective(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "형식",
            "포맷",
            "불릿",
            "번호",
            "목록",
            "리스트",
            "한줄",
            "줄바꿈",
            "markdown",
            "마크다운",
            "no.n"
        );
    }

    private static bool LooksLikeWebToneDirective(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "간결",
            "짧게",
            "자세히",
            "길게",
            "말투",
            "존댓말",
            "반말",
            "톤"
        );
    }

    private static bool LooksLikeWebLanguageDirective(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(lowered, "한국어", "한글", "영어", "english", "korean");
    }

    private int ResolveWebDefaultCount(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (ContainsAny(normalized, "뉴스", "news", "헤드라인", "속보"))
        {
            return Math.Clamp(_config.WebDefaultNewsCount, 1, 20);
        }

        return Math.Clamp(_config.WebDefaultListCount, 1, 20);
    }

    private int ResolveGeminiWebAnswerMaxOutputTokens(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        var targetCount = HasExplicitRequestedCountInQuery(normalized)
            ? Math.Clamp(ResolveRequestedResultCountFromQuery(normalized), 1, 20)
            : ResolveWebDefaultCount(normalized);
        var tableMode = LooksLikeTableRenderRequest(normalized);
        var listMode = LooksLikeListOutputRequest(normalized);
        var comparisonMode = LooksLikeComparisonRequest(normalized);
        if (!tableMode && !listMode && !comparisonMode)
        {
            return 1024;
        }

        if (targetCount > 5 || (tableMode && normalized.Length >= 80))
        {
            return 1280;
        }

        return 1024;
    }

    private int ResolveGeminiUrlContextMaxOutputTokens(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        var targetCount = HasExplicitRequestedCountInQuery(normalized)
            ? Math.Clamp(ResolveRequestedResultCountFromQuery(normalized), 1, 20)
            : ResolveWebDefaultCount(normalized);
        var tableMode = LooksLikeTableRenderRequest(normalized);
        var listMode = LooksLikeListOutputRequest(normalized);
        var comparisonMode = LooksLikeComparisonRequest(normalized);
        if (!tableMode && !listMode && !comparisonMode)
        {
            return 2048;
        }

        if (targetCount > 5 || (tableMode && normalized.Length >= 80))
        {
            return 4096;
        }

        return 2048;
    }

    private static bool IsGeminiUrlContextFailureText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        return normalized.StartsWith("Gemini URL 참조 요청 실패:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini URL 참조 응답 시간이 초과되었습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini URL 참조 호출 오류:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini URL 참조 응답이 비어 있습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("429", StringComparison.OrdinalIgnoreCase)
               && normalized.Contains("Gemini", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildGeminiUrlContextFailureNotice(string input, string failureText)
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var coreFailure = (failureText ?? string.Empty).Trim();
        return $"""
                요청하신 URL 참조 답변을 생성하지 못했습니다.
                원인: {coreFailure}
                안내: URL 접근 권한이나 본문 공개 상태를 확인한 뒤 다시 요청해 주세요.
                입력: {normalizedInput}
                """.Trim();
    }

    private static bool IsGeminiWebFailureText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        return normalized.StartsWith("Gemini 웹검색 요청 실패:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini 웹검색 응답 시간이 초과되었습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini 웹검색 호출 오류:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini 웹검색 응답이 비어 있습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("429", StringComparison.OrdinalIgnoreCase)
               && normalized.Contains("Gemini", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeminiWebTimeoutText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        return normalized.StartsWith("Gemini 웹검색 응답 시간이 초과되었습니다.", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildGeminiWebFailureNotice(string input, string failureText)
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var coreFailure = (failureText ?? string.Empty).Trim();
        var shortagePrefix = LooksLikeListOutputRequest(normalizedInput)
            ? "요청하신 목록을 생성하지 못했습니다."
            : "요청하신 최신 정보를 생성하지 못했습니다.";
        return $"""
                {shortagePrefix}
                원인: {coreFailure}
                안내: 잠시 후 다시 요청해 주세요.
                입력: {normalizedInput}
                """.Trim();
    }

    private static bool TryParseNeedWebDecisionJson(string? rawText, out bool needWeb, out string reason)
    {
        needWeb = false;
        reason = string.Empty;
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var json = text[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetPropertyIgnoreCase(root, "need_web", out var needWebElement)
                && !TryGetPropertyIgnoreCase(root, "needWeb", out needWebElement))
            {
                return false;
            }

            switch (needWebElement.ValueKind)
            {
                case JsonValueKind.True:
                    needWeb = true;
                    break;
                case JsonValueKind.False:
                    needWeb = false;
                    break;
                case JsonValueKind.String:
                    var normalizedToken = NormalizeWebSearchDecisionToken(needWebElement.GetString());
                    if (normalizedToken == "yes")
                    {
                        needWeb = true;
                    }
                    else if (normalizedToken == "no")
                    {
                        needWeb = false;
                    }
                    else
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }

            if (TryGetPropertyIgnoreCase(root, "reason", out var reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String)
            {
                reason = (reasonElement.GetString() ?? string.Empty).Trim();
            }

            return true;
        }
        catch
        {
            return false;
        }
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

        if (_config.EnableFastWebPipeline)
        {
            var heuristicNeedWeb = LooksLikeExplicitWebLookupQuestion(normalized) || LooksLikeRealtimeQuestion(normalized);
            return new SearchRequirementDecision(
                heuristicNeedWeb,
                heuristicNeedWeb
                    ? (LooksLikeExplicitWebLookupQuestion(normalized) ? "fast:true:explicit_web" : "fast:true:heuristic")
                    : "fast:false:heuristic",
                ExtractSourceFocusHintFromInput(normalized),
                ExtractSourceDomainHintFromInput(normalized)
            );
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
        var configured = NormalizeModelSelection(_config.GeminiSearchModel);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        return _config.GeminiModel;
    }

    private string ResolveUrlContextLlmModel()
    {
        var configured = NormalizeModelSelection(_config.GeminiModel);
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
        needWeb = false;
        sourceFocus = string.Empty;
        sourceDomain = string.Empty;
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var json = text[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var needToken = string.Empty;
            if (TryGetPropertyIgnoreCase(root, "needWeb", out var needWebElement))
            {
                needToken = (needWebElement.GetString() ?? string.Empty).Trim();
            }

            var normalizedNeed = NormalizeWebSearchDecisionToken(needToken);
            if (normalizedNeed == "yes")
            {
                needWeb = true;
            }
            else if (normalizedNeed == "no")
            {
                needWeb = false;
            }
            else
            {
                return false;
            }

            if (TryGetPropertyIgnoreCase(root, "sourceFocus", out var sourceFocusElement)
                && sourceFocusElement.ValueKind == JsonValueKind.String)
            {
                sourceFocus = (sourceFocusElement.GetString() ?? string.Empty).Trim();
            }

            if (TryGetPropertyIgnoreCase(root, "sourceDomain", out var sourceDomainElement)
                && sourceDomainElement.ValueKind == JsonValueKind.String)
            {
                sourceDomain = NormalizeSourceDomainHint(sourceDomainElement.GetString());
            }

            return true;
        }
        catch
        {
            return false;
        }
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
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var match = Regex.Match(
            normalized,
            @"(?<focus>[A-Za-z0-9가-힣][A-Za-z0-9가-힣\.\-]{1,40})\s*(?:의\s*)?(?:주요\s*)?뉴스",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return string.Empty;
        }

        var focus = (match.Groups["focus"].Value ?? string.Empty).Trim();
        if (focus.Length < 2)
        {
            return string.Empty;
        }

        var loweredFocus = focus.ToLowerInvariant();
        if (loweredFocus is "오늘"
            or "어제"
            or "최근"
            or "최신"
            or "방금"
            or "실시간"
            or "주요"
            or "뉴스"
            or "헤드라인"
            or "속보"
            or "latest"
            or "recent"
            or "today"
            or "breaking"
            or "top")
        {
            return string.Empty;
        }

        return focus;
    }

    private static string ExtractSourceDomainHintFromInput(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var explicitSite = Regex.Match(
            normalized,
            @"site\s*:\s*(?<domain>[A-Za-z0-9][A-Za-z0-9\.\-]*\.[A-Za-z]{2,})",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        if (explicitSite.Success)
        {
            return NormalizeSourceDomainHint(explicitSite.Groups["domain"].Value);
        }

        return string.Empty;
    }

    private static string NormalizeSourceDomainHint(string? domain)
    {
        var normalized = (domain ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("http://", StringComparison.Ordinal))
        {
            normalized = normalized["http://".Length..];
        }
        else if (normalized.StartsWith("https://", StringComparison.Ordinal))
        {
            normalized = normalized["https://".Length..];
        }

        normalized = normalized.Trim('/');
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        return Regex.IsMatch(normalized, @"^[a-z0-9][a-z0-9\.-]*\.[a-z]{2,}$", RegexOptions.CultureInvariant)
            ? normalized
            : string.Empty;
    }

    private static string BuildEffectiveSearchQuery(string query, SearchRequirementDecision decision)
    {
        var baseQuery = (query ?? string.Empty).Trim();
        if (baseQuery.Length == 0)
        {
            return baseQuery;
        }

        var sourceFocus = (decision.SourceFocus ?? string.Empty).Trim();
        if (sourceFocus.Length == 0)
        {
            if (LooksLikeListOutputRequest(baseQuery))
            {
                var lowered = baseQuery.ToLowerInvariant();
                if (!ContainsAny(lowered, "latest", "breaking", "headlines", "top stories")
                    && ContainsAny(lowered, "뉴스", "news", "헤드라인", "속보"))
                {
                    return $"{baseQuery} latest breaking headlines";
                }
            }

            return baseQuery;
        }

        var builder = new StringBuilder(baseQuery);
        if (!baseQuery.Contains(sourceFocus, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(' ').Append(sourceFocus);
        }

        var sourceDomain = NormalizeSourceDomainHint(decision.SourceDomain);
        if (sourceDomain.Length == 0)
        {
            sourceDomain = ResolveSourceDomainFromQueryOrFocus(baseQuery, sourceFocus);
        }
        if (sourceDomain.Length > 0
            && !baseQuery.Contains(sourceDomain, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(' ').Append(sourceDomain);
        }

        if (LooksLikeListOutputRequest(baseQuery))
        {
            var lowered = baseQuery.ToLowerInvariant();
            if (!ContainsAny(lowered, "official", "공식", "homepage", "top headlines", "top stories"))
            {
                builder.Append(' ').Append(sourceFocus).Append(" official top headlines");
            }
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeWebSearchDecisionToken(string? decisionText)
    {
        var normalized = (decisionText ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.StartsWith("YES", StringComparison.Ordinal)
            || normalized == "Y")
        {
            return "yes";
        }

        if (normalized.StartsWith("NO", StringComparison.Ordinal)
            || normalized == "N")
        {
            return "no";
        }

        var compact = Regex.Replace(normalized, @"[^A-Z가-힣]", string.Empty);
        if (compact.Contains("YES", StringComparison.Ordinal))
        {
            return "yes";
        }

        if (compact.Contains("NO", StringComparison.Ordinal))
        {
            return "no";
        }

        if (compact.Contains("필요", StringComparison.Ordinal) && !compact.Contains("불필요", StringComparison.Ordinal))
        {
            return "yes";
        }

        if (compact.Contains("불필요", StringComparison.Ordinal))
        {
            return "no";
        }

        return string.Empty;
    }

    private static bool LooksLikeRealtimeQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (LooksLikeLocalDateTimeQuestion(normalized))
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "최신",
            "최근",
            "오늘",
            "어제",
            "방금",
            "실시간",
            "지금",
            "뉴스",
            "속보",
            "업데이트",
            "변경점",
            "릴리즈",
            "출시",
            "현재",
            "latest",
            "recent",
            "today",
            "yesterday",
            "now",
            "news",
            "update",
            "release",
            "current"
        );
    }

    private static bool LooksLikeExplicitWebLookupQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (LooksLikeLocalDateTimeQuestion(normalized))
        {
            return false;
        }

        return ContainsAny(
                normalized,
                "검색해서",
                "검색해줘",
                "검색해 줘",
                "검색해봐",
                "웹 검색",
                "웹에서",
                "인터넷에서",
                "web search",
                "search for",
                "look up",
                "lookup"
            )
            || Regex.IsMatch(
                normalized,
                @"\b(?:search|lookup)\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
    }

    // 직전 답변에 대한 follow-up/판단 요청처럼 보이는 입력 — 웹 검색이 아니라 conversational LLM 흐름이어야 함.
    // 짧은 anaphoric ("그니까/그래서/이거"), 의견/판단 요청, 비교/추천, 검토 같은 신호를 잡아낸다.
    private static bool LooksLikeConversationalFollowUp(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > 120)
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "그니까",
                "그러니까",
                "그래서",
                "그럼",
                "그러면",
                "그래도",
                "잘 돌아",
                "잘 작동",
                "잘 동작",
                "잘 되",
                "잘 될",
                "잘 굴러",
                "쓸만",
                "쓸 만",
                "괜찮",
                "어때",
                "어떨",
                "어떤지",
                "어떻게 생각",
                "어떻게 봐",
                "네 생각",
                "네 의견",
                "너 생각",
                "너의 생각",
                "당신 생각",
                "당신의 생각",
                "검토해",
                "판단해",
                "추천해",
                "비교해",
                "what do you think",
                "would it work"))
        {
            return true;
        }

        // anaphoric markers + 짧은 길이.
        if (normalized.Length <= 60
            && ContainsAny(normalized, "이거", "이건", "이게", "이걸", "그거", "그건", "그게", "그걸", "저거", "이 환경", "이 모델", "이 상황"))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeClearlyNonWebQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (LooksLikeLocalDateTimeQuestion(normalized))
        {
            return true;
        }

        // 직전 답변에 대한 후속/판단 요청은 무조건 LLM 흐름.
        if (LooksLikeConversationalFollowUp(normalized))
        {
            return true;
        }

        if (LooksLikeRealtimeQuestion(normalized)
            || LooksLikeExplicitWebLookupQuestion(normalized))
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "번역",
                "translate",
                "영작",
                "영문",
                "맞춤법",
                "교정",
                "다듬",
                "rewrite",
                "rephrase"))
        {
            return true;
        }

        if (ContainsAny(normalized, "코드", "code")
            && ContainsAny(normalized, "설명", "해석", "리뷰", "explain", "review"))
        {
            return true;
        }

        if (LooksLikeCasualOrIdentityQuestion(normalized))
        {
            return true;
        }

        if (ContainsAny(normalized, "요약", "summary", "summarize", "정리"))
        {
            return normalized.Contains('\n')
                || normalized.Contains("```", StringComparison.Ordinal)
                || normalized.Contains("다음", StringComparison.Ordinal)
                || normalized.Contains("\"", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool LooksLikeCasualOrIdentityQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();

        if (LooksLikeStandaloneFreshGreeting(normalized))
        {
            return true;
        }

        if (normalized.Length <= 8)
        {
            if (ContainsAny(normalized, "응", "어", "아니", "네", "예", "맞아", "그래", "음", "헐", "대박", "진짜", "뭐해", "ok", "ㅇㅇ", "ㅋㅋ", "ㅎㅎ", "ㅠㅠ", "ㅜㅜ", "너는?", "나도"))
            {
                return true;
            }
        }

        return ContainsAny(
            normalized,
            "할 수 있",
            "할수 있",
            "할 줄 아",
            "할줄 아",
            "뭐할 수",
            "뭐 할수",
            "뭐 할 수",
            "너는 누구",
            "당신은 누구",
            "넌 누구",
            "너는 뭐",
            "넌 뭐",
            "자기소개",
            "안녕",
            "반가워",
            "반갑습니다",
            "기능 알려",
            "명령어",
            "스킬 목록",
            "무엇을 할",
            "좋은 일이 없",
            "피곤해",
            "우울해",
            "배고파",
            "심심해",
            "그렇네",
            "수고",
            "고마워",
            "감사",
            "잘자",
            "잘가"
        );
    }

    private static bool LooksLikeStandaloneFreshGreeting(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || normalized.Length > 16)
        {
            return false;
        }

        var compact = Regex.Replace(normalized, @"[\s\p{P}\p{S}]+", "");
        return compact is
            "ㅎㅇ" or
            "ㅎㅇㅎㅇ" or
            "하이" or
            "안녕" or
            "안녕하세요" or
            "안녕하십니까" or
            "안뇽" or
            "안뇽하세요" or
            "헬로" or
            "hi" or
            "hello" or
            "hey" or
            "yo";
    }

    private static bool LooksLikeLocalDateTimeQuestion(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0)
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "시간복잡도",
                "time complexity",
                "runtime complexity",
                "응답 시간",
                "실행 시간",
                "timeout",
                "타임아웃",
                "러닝타임"))
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "지금 몇시",
                "지금 몇 시",
                "몇시야",
                "몇 시야",
                "현재 시간",
                "현재 시각",
                "지금 시간",
                "로컬 시간",
                "오늘 날짜",
                "오늘 며칠",
                "현재 날짜",
                "로컬 날짜",
                "무슨 요일",
                "어느 요일",
                "몇월 몇일",
                "몇 월 몇 일",
                "현재 타임존",
                "현재 시간대",
                "로컬 타임존",
                "로컬 시간대",
                "what time is it",
                "current time",
                "local time",
                "today's date",
                "today date",
                "current date",
                "what date is it",
                "what day is it",
                "current timezone",
                "local timezone",
                "time zone"))
        {
            return true;
        }

        return Regex.IsMatch(
            normalized,
            @"(?:^|\s)몇\s*시(?:야|예요|인가요|입니까)?(?:\?|$)|(?:^|\s)몇\s*일(?:이야|인가요|입니까)?(?:\?|$)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    private static bool LooksLikeComparisonRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "비교",
            "차이",
            "대비",
            "vs",
            "compare",
            "difference",
            "국가별",
            "유형별",
            "카테고리별"
        );
    }

    private static double ResolveForcedMemoryMinScore(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return 0.45d;
        }

        if (normalized.Length < 10 && !normalized.Contains('.'))
        {
            // 짧은 일상어(예: "1+1 은?")가 엉뚱한 메모리를 끌어오는 것을 방지
            return 0.65d;
        }

        if (LooksLikeRealtimeQuestion(normalized))
        {
            return 0.3d;
        }

        if (ContainsAny(normalized, "비교", "차이", "요약", "정리", "compare", "difference", "summary"))
        {
            return 0.5d;
        }

        return 0.45d;
    }

    private static string ResolveSearchFreshnessForQuery(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (ContainsAny(normalized, "오늘", "어제", "방금", "실시간", "today", "yesterday", "breaking"))
        {
            return "day";
        }

        if (ContainsAny(normalized, "이번달", "한달", "month", "monthly"))
        {
            return "month";
        }

        if (ContainsAny(normalized, "올해", "연간", "year", "yearly"))
        {
            return "year";
        }

        return "week";
    }

    private static int ResolveRequestedResultCountFromQuery(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        var defaultCount = ContainsAny(normalized, "뉴스", "news", "헤드라인", "속보", "브리핑")
            ? 10
            : 5;
        if (normalized.Length == 0)
        {
            return defaultCount;
        }

        var direct = RequestedCountRegex.Match(normalized);
        if (direct.Success
            && int.TryParse(direct.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directParsed))
        {
            return Math.Clamp(directParsed, 1, 10);
        }

        var top = TopCountRegex.Match(normalized);
        if (top.Success
            && int.TryParse(top.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var topParsed))
        {
            return Math.Clamp(topParsed, 1, 10);
        }

        return defaultCount;
    }

    private static bool HasExplicitRequestedCountInQuery(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return RequestedCountRegex.IsMatch(normalized) || TopCountRegex.IsMatch(normalized);
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
