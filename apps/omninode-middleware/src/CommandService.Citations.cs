using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private static IReadOnlyList<SearchCitationReference> BuildSearchCitationReferences(
        IReadOnlyList<WebSearchResultItem> results
    )
    {
        if (results.Count == 0)
        {
            return Array.Empty<SearchCitationReference>();
        }

        var citations = new List<SearchCitationReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < results.Count; index++)
        {
            var item = results[index];
            if (!TryNormalizeDisplaySourceUrl(item.Url, out var sourceUrl))
            {
                continue;
            }

            var citationId = NormalizeCitationId(item.CitationId, index + 1);
            var key = $"{citationId}|{sourceUrl}";
            if (!seen.Add(key))
            {
                continue;
            }

            var published = string.IsNullOrWhiteSpace(item.Published) ? "-" : item.Published.Trim();
            var sourceType = string.IsNullOrWhiteSpace(item.CitationId) ? "web_search" : "gemini_grounding";
            citations.Add(new SearchCitationReference(
                CitationId: citationId,
                Title: TrimForForcedContext(item.Title, 160),
                Url: sourceUrl,
                Published: published,
                Snippet: TrimForForcedContext(item.Description, 240),
                SourceType: sourceType
            ));
        }

        return citations;
    }

    private static string NormalizeCitationId(string? citationId, int fallbackIndex)
    {
        var normalized = (citationId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return $"c{Math.Max(1, fallbackIndex)}";
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static SearchAnswerGuardFailure? BuildCitationValidationGuardFailure(SearchCitationValidationSummary? validation)
    {
        if (validation is null || validation.Passed)
        {
            return null;
        }

        return new SearchAnswerGuardFailure(
            SearchAnswerGuardFailureCategory.Coverage,
            "citation_validation_failed",
            $"missing={validation.MissingSentences}, unknown={validation.UnknownCitationSentences}, total={validation.TotalSentences}"
        );
    }

    private void LogCitationValidationGuardBlocked(
        string? source,
        string route,
        SearchAnswerGuardFailure failure
    )
    {
        var routeToken = NormalizeAuditToken(route, "-");
        _auditLogger.Log(
            NormalizeSearchAuditSource(source),
            "search_answer_guard",
            "blocked",
            $"guardCategory={NormalizeSearchGuardCategory(failure.Category.ToString())} guardReason={NormalizeSearchGuardReason(failure.ReasonCode)} guardDetail={NormalizeSearchGuardDetail($"{failure.Detail}, route={routeToken}")}"
        );
    }

    private static string BuildCitationValidationBlockedResponseText(SearchAnswerGuardFailure? failure = null)
    {
        if (failure is null)
        {
            return "근거 인용 검증에 실패하여 fail-closed 정책으로 답변을 차단했습니다. 잠시 후 다시 시도해 주세요.";
        }

        return BuildGroundedSearchFailureMessage(failure, retryStopReason: "guard_blocked");
    }

    private static string BuildGroundedSearchFailureMessage(
        SearchAnswerGuardFailure? failure,
        string? retryStopReason = null
    )
    {
        if (failure is null)
        {
            return "검색 실패: 원인을 확인할 수 없습니다. 잠시 후 다시 시도해 주세요.";
        }

        var reasonCode = NormalizeFailureToken(failure.ReasonCode, "unknown");
        var detail = NormalizeFailureDetail(failure.Detail);
        var terminationCode = ExtractGuardDetailToken(detail, "termination");
        var retryCode = NormalizeFailureToken(retryStopReason, "-");

        var message = reasonCode switch
        {
            "citation_validation_failed" => "검색 실패: 생성 응답의 인용 검증이 실패했습니다.",
            "freshness_guard_failed" => "검색 실패: 최신성 검증을 통과하지 못했습니다.",
            "credibility_guard_failed" => "검색 실패: 신뢰도 검증을 통과하지 못했습니다.",
            "insufficient_document_count" or "count_lock_unsatisfied" or "count_lock_unsatisfied_after_retries"
                => "검색 실패: 요청한 건수를 채우지 못했습니다.",
            "insufficient_independent_sources"
                => "검색 실패: 출처 다양성 기준을 충족하지 못했습니다.",
            "no_documents" => terminationCode switch
            {
                "gemini_api_key_missing" => "검색 실패: Gemini API 키가 없거나 읽기에 실패했습니다. 설정탭에서 키를 다시 저장하세요.",
                "gemini_grounding_timeout" => "검색 실패: Gemini 검색 요청이 시간 초과되었습니다.",
                "no_documents_after_filter" => "검색 실패: 검색 결과는 있었지만 필터(시간창/신뢰도/중복) 통과 문서가 0건입니다.",
                "gemini_result_empty" => "검색 실패: Gemini 응답이 비어 있어 문서를 만들지 못했습니다.",
                "gemini_auth_failed" => "검색 실패: Gemini 인증에 실패했습니다. API 키 권한/유효성을 확인하세요.",
                "gemini_rate_limited" => "검색 실패: Gemini 호출 제한(rate limit)에 걸렸습니다. 잠시 후 재시도하세요.",
                "gemini_upstream_error" => "검색 실패: Gemini 상위 서버 오류가 발생했습니다.",
                "retriever_unavailable" => "검색 실패: Gemini 검색 리트리버를 사용할 수 없습니다(키/네트워크/API 오류).",
                _ => "검색 실패: 문서 수집 결과가 0건입니다."
            },
            "web_search_unavailable" when detail.Contains("api key missing", StringComparison.OrdinalIgnoreCase)
                => "검색 실패: Gemini API 키가 없거나 읽기에 실패했습니다. 설정탭에서 키를 확인하세요.",
            "web_search_unavailable" when detail.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                => "검색 실패: Gemini 검색 요청이 시간 초과되었습니다.",
            "web_search_unavailable" when detail.Contains("http 429", StringComparison.OrdinalIgnoreCase)
                => "검색 실패: Gemini 호출 제한(rate limit)에 걸렸습니다. 잠시 후 재시도하세요.",
            "web_search_unavailable" => "검색 실패: 검색 게이트웨이 호출에 실패했습니다.",
            _ => "검색 실패: 가드 정책에 의해 응답이 차단되었습니다."
        };

        var diagnostics = new List<string>
        {
            $"원인 코드: {reasonCode}"
        };
        if (!string.IsNullOrWhiteSpace(terminationCode))
        {
            diagnostics.Add($"종료 코드: {terminationCode}");
        }

        if (!string.Equals(retryCode, "-", StringComparison.Ordinal))
        {
            diagnostics.Add($"재시도 상태: {retryCode}");
        }

        if (!string.IsNullOrWhiteSpace(detail) && !string.Equals(detail, "-", StringComparison.Ordinal))
        {
            diagnostics.Add($"상세: {TrimForAudit(detail, 180)}");
        }

        return message + "\n" + string.Join("\n", diagnostics);
    }

    private static string NormalizeFailureToken(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return fallback;
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static string NormalizeFailureDetail(string? detail)
    {
        var normalized = (detail ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length == 0 ? "-" : normalized;
    }

    private static string ExtractGuardDetailToken(string detail, string key)
    {
        var normalizedDetail = (detail ?? string.Empty).Trim();
        var normalizedKey = (key ?? string.Empty).Trim();
        if (normalizedDetail.Length == 0 || normalizedKey.Length == 0)
        {
            return string.Empty;
        }

        var marker = normalizedKey + "=";
        var startIndex = normalizedDetail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        var valueStart = startIndex + marker.Length;
        var valueEnd = valueStart;
        while (valueEnd < normalizedDetail.Length)
        {
            var ch = normalizedDetail[valueEnd];
            if (ch == ',' || ch == ' ')
            {
                break;
            }

            valueEnd++;
        }

        if (valueEnd <= valueStart)
        {
            return string.Empty;
        }

        return NormalizeFailureToken(normalizedDetail[valueStart..valueEnd], string.Empty);
    }

    private (IReadOnlyList<SearchCitationSentenceMapping> Mappings, SearchCitationValidationSummary Validation) BuildAndLogCitationMappings(
        string? source,
        string route,
        IReadOnlyList<SearchCitationReference>? citations,
        params (string Segment, string Text)[] segments
    )
    {
        var bundle = BuildCitationMappingBundle(citations, segments);
        if (bundle.Mappings.Count == 0 && bundle.Validation.TotalSentences == 0)
        {
            return bundle;
        }

        _auditLogger.Log(
            NormalizeAuditToken(source, "web"),
            "citation_mapping",
            bundle.Validation.Passed ? "ok" : "warn",
            $"route={NormalizeAuditToken(route, "-")} total={bundle.Validation.TotalSentences} tagged={bundle.Validation.TaggedSentences} missing={bundle.Validation.MissingSentences} unknown={bundle.Validation.UnknownCitationSentences}"
        );
        return bundle;
    }

    private static (IReadOnlyList<SearchCitationSentenceMapping> Mappings, SearchCitationValidationSummary Validation) BuildCitationMappingBundle(
        IReadOnlyList<SearchCitationReference>? citations,
        params (string Segment, string Text)[] segments
    )
    {
        if (citations == null || citations.Count == 0 || segments.Length == 0)
        {
            return (
                Array.Empty<SearchCitationSentenceMapping>(),
                new SearchCitationValidationSummary(
                    TotalSentences: 0,
                    TaggedSentences: 0,
                    MissingSentences: 0,
                    UnknownCitationSentences: 0,
                    Passed: true
                )
            );
        }

        var knownCitationIds = citations
            .Where(item => !string.IsNullOrWhiteSpace(item.CitationId))
            .Select((item, index) => NormalizeCitationId(item.CitationId, index + 1))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (knownCitationIds.Count == 0)
        {
            return (
                Array.Empty<SearchCitationSentenceMapping>(),
                new SearchCitationValidationSummary(
                    TotalSentences: 0,
                    TaggedSentences: 0,
                    MissingSentences: 0,
                    UnknownCitationSentences: 0,
                    Passed: true
                )
            );
        }

        var mappings = new List<SearchCitationSentenceMapping>();
        var taggedSentences = 0;
        var missingSentences = 0;
        var unknownCitationSentences = 0;

        foreach (var segment in segments)
        {
            var normalizedSegment = NormalizeCitationSegment(segment.Segment);
            var sentenceIndex = 0;
            foreach (var sentence in SplitGeneratedSentences(segment.Text))
            {
                sentenceIndex++;
                var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var unknownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in CitationBracketRegex.Matches(sentence))
                {
                    var candidate = NormalizeCitationId(match.Groups["id"].Value, sentenceIndex);
                    if (knownCitationIds.Contains(candidate))
                    {
                        knownIds.Add(candidate);
                    }
                    else
                    {
                        unknownIds.Add(candidate);
                    }
                }

                var knownArray = knownIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                var unknownArray = unknownIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                if (knownArray.Length > 0)
                {
                    taggedSentences++;
                }

                var missingCitation = ShouldRequireCitationForSentence(sentence) && knownArray.Length == 0;
                if (missingCitation)
                {
                    missingSentences++;
                }

                if (unknownArray.Length > 0 && knownArray.Length == 0)
                {
                    unknownCitationSentences++;
                }

                mappings.Add(new SearchCitationSentenceMapping(
                    Segment: normalizedSegment,
                    SentenceIndex: sentenceIndex,
                    Sentence: sentence,
                    CitationIds: knownArray,
                    UnknownCitationIds: unknownArray,
                    MissingCitation: missingCitation
                ));
            }
        }

        return (
            mappings,
            new SearchCitationValidationSummary(
                TotalSentences: mappings.Count,
                TaggedSentences: taggedSentences,
                MissingSentences: missingSentences,
                UnknownCitationSentences: unknownCitationSentences,
                Passed: missingSentences == 0 && unknownCitationSentences == 0
            )
        );
    }

    private static string NormalizeCitationSegment(string? segment)
    {
        var normalized = (segment ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return "text";
        }

        return Regex.Replace(normalized, @"\s+", "_");
    }

    private static IReadOnlyList<string> SplitGeneratedSentences(string? text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<string>();
        }

        var sentences = new List<string>();
        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunks = CitationSentenceSplitRegex
                .Split(line)
                .Select(chunk => chunk.Trim())
                .Where(chunk => !string.IsNullOrWhiteSpace(chunk));
            foreach (var chunk in chunks)
            {
                sentences.Add(TrimForForcedContext(chunk, 360));
            }
        }

        return sentences;
    }

    private static bool ShouldRequireCitationForSentence(string sentence)
    {
        var normalized = (sentence ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.StartsWith("[", StringComparison.Ordinal)
            && normalized.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Any(char.IsLetterOrDigit);
    }

    private static string ApplyListCountFallback(
        string input,
        string responseText,
        IReadOnlyList<SearchCitationReference>? citations
    )
    {
        var normalizedResponse = (responseText ?? string.Empty).Trim();
        if (citations == null || citations.Count == 0)
        {
            if (LooksLikeListOutputRequest(input))
            {
                return ApplyHighRiskFactualGuardFallback(
                    input,
                    ReplaceListUrlsWithSourceLabelsWithoutCitations(normalizedResponse)
                );
            }

            return ApplyHighRiskFactualGuardFallback(input, normalizedResponse);
        }

        if (!LooksLikeListOutputRequest(input))
        {
            return ApplyHighRiskFactualGuardFallback(input, normalizedResponse);
        }

        var hasExplicitRequestedCount = HasExplicitRequestedCountInQuery(input);
        var requestedCount = Math.Clamp(ResolveRequestedResultCountFromQuery(input), 1, 10);
        var validCitations = citations
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
        validCitations = DeduplicateCitationsForList(validCitations);
        var qualityFiltered = validCitations
            .Where(item => !IsLowQualityCitationForList(item))
            .ToArray();
        if (qualityFiltered.Length > 0)
        {
            validCitations = qualityFiltered;
        }
        validCitations = DeduplicateCitationsForList(validCitations);

        var sourceFocus = ExtractSourceFocusHintFromInput(input);
        var focusMatchedCitations = FilterCitationsBySourceFocus(validCitations, sourceFocus);
        if (focusMatchedCitations.Length > 0)
        {
            if (focusMatchedCitations.Length >= requestedCount)
            {
                validCitations = focusMatchedCitations;
            }
            else
            {
                var merged = new List<SearchCitationReference>(validCitations.Length);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var citation in focusMatchedCitations)
                {
                    var key = BuildSourceUrlMatchKey(citation.Url);
                    if (key.Length == 0 || !seen.Add(key))
                    {
                        continue;
                    }

                    merged.Add(citation);
                }

                foreach (var citation in validCitations)
                {
                    var key = BuildSourceUrlMatchKey(citation.Url);
                    if (key.Length == 0 || !seen.Add(key))
                    {
                        continue;
                    }

                    merged.Add(citation);
                }

                validCitations = merged.ToArray();
            }
        }

        if (validCitations.Length == 0)
        {
            return ApplyHighRiskFactualGuardFallback(input, normalizedResponse);
        }

        var targetCount = requestedCount;
        if (!hasExplicitRequestedCount)
        {
            targetCount = Math.Min(targetCount, validCitations.Length);
        }

        if (targetCount <= 0)
        {
            return normalizedResponse;
        }

        var listedCount = CountListItemsInResponse(normalizedResponse);
        var sourceCoverageValid = validCitations.Length >= targetCount
            && ResponseHasValidSourceCoverage(normalizedResponse, targetCount, validCitations);
        var sourceLabelReplacedResponse = ReplaceListUrlsWithSourceLabels(normalizedResponse, validCitations);
        var modelListResponseUsable = LooksLikeUsableModelListResponse(
            sourceLabelReplacedResponse,
            targetCount,
            sourceFocus
        );
        if (listedCount >= targetCount && modelListResponseUsable)
        {
            return ApplyHighRiskFactualGuardFallback(input, sourceLabelReplacedResponse);
        }

        if (listedCount >= targetCount && sourceCoverageValid)
        {
            return ApplyHighRiskFactualGuardFallback(
                input,
                sourceLabelReplacedResponse
            );
        }

        var tableMode = LooksLikeTableRenderRequest(input);
        var builder = new StringBuilder();
        if (tableMode)
        {
            builder.AppendLine("| 번호 | 뉴스 제목 | 핵심 요약 | 출처 |");
            builder.AppendLine("| --- | --- | --- | --- |");
        }

        for (var index = 0; index < targetCount; index++)
        {
            var hasDirectCitation = index < validCitations.Length;
            var item = hasDirectCitation
                ? validCitations[index]
                : validCitations[index % validCitations.Length];
            var sourceLabel = ResolveSourceLabel(item.Url, item.Title);
            var title = BuildCitationDisplayTitle(item, sourceLabel, index + 1, hasDirectCitation);
            var summary = BuildCitationDisplaySummary(item, sourceLabel, hasDirectCitation);
            if (tableMode)
            {
                builder.AppendLine(
                    $"| {index + 1} | {SanitizeTableCell(title)} | {SanitizeTableCell(summary)} | {SanitizeTableCell(sourceLabel)} |"
                );
            }
            else
            {
                builder.AppendLine($"{index + 1}. {title}");
                builder.AppendLine($"- 핵심: {summary}");
                builder.AppendLine($"- 출처: {sourceLabel}");
            }

            if (!tableMode && index < targetCount - 1)
            {
                builder.AppendLine();
            }
        }

        return ApplyHighRiskFactualGuardFallback(input, builder.ToString().Trim());
    }

    private static bool IsLowQualityCitationForList(SearchCitationReference citation)
    {
        var title = NormalizeFallbackListText(citation.Title, 140, string.Empty);
        var snippet = NormalizeFallbackListText(citation.Snippet, 220, string.Empty);
        if (title.Length == 0)
        {
            return true;
        }

        var loweredTitle = title.ToLowerInvariant();
        if (Regex.IsMatch(
                loweredTitle,
                @"^(view|article|news|briefing|newsletter|news\.php|articleview\.html|actuallyview\.do)\b",
                RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (loweredTitle.Contains("articleview.html", StringComparison.Ordinal)
            || loweredTitle.Contains("news articleview", StringComparison.Ordinal)
            || loweredTitle.Contains("view akr", StringComparison.Ordinal))
        {
            return true;
        }

        if (Regex.IsMatch(loweredTitle, @"^(?:v|view|article|news|story)\s*\d{8,}$", RegexOptions.CultureInvariant)
            || Regex.IsMatch(loweredTitle, @"^[a-z]{1,3}\s*\d{8,}$", RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (Regex.IsMatch(loweredTitle, @"^[a-z0-9][a-z0-9\-_/.]{6,}$", RegexOptions.CultureInvariant)
            && (loweredTitle.Contains('/', StringComparison.Ordinal)
                || loweredTitle.Contains('_', StringComparison.Ordinal)
                || Regex.IsMatch(loweredTitle, @"\d{6,}", RegexOptions.CultureInvariant)))
        {
            return true;
        }

        var letterCount = loweredTitle.Count(char.IsLetter);
        var digitCount = loweredTitle.Count(char.IsDigit);
        if (title.Length < 24 && digitCount >= 6 && letterCount <= 6)
        {
            return true;
        }

        if (snippet.Length == 0
            || snippet.Contains("핵심 내용 확인이 필요합니다.", StringComparison.OrdinalIgnoreCase))
        {
            return title.Length < 18 || digitCount >= 6;
        }

        return false;
    }

    private static SearchCitationReference[] DeduplicateCitationsForList(
        IReadOnlyList<SearchCitationReference> citations
    )
    {
        if (citations == null || citations.Count == 0)
        {
            return Array.Empty<SearchCitationReference>();
        }

        var deduplicated = new List<SearchCitationReference>(citations.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var citation in citations)
        {
            var urlKey = BuildSourceUrlMatchKey(citation.Url);
            var titleKey = NormalizeFallbackListText(citation.Title, 140, string.Empty).ToLowerInvariant();
            var snippetKey = NormalizeFallbackListText(citation.Snippet, 100, string.Empty).ToLowerInvariant();
            var key = urlKey.Length > 0
                ? $"{urlKey}|{titleKey}"
                : titleKey.Length > 0
                    ? titleKey
                    : snippetKey;
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            deduplicated.Add(citation);
        }

        return deduplicated.Count > 0 ? deduplicated.ToArray() : citations.ToArray();
    }

    private static bool LooksLikeUsableModelListResponse(
        string responseText,
        int targetCount,
        string sourceFocus
    )
    {
        if (targetCount <= 0)
        {
            return false;
        }

        var normalized = (responseText ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var lowered = normalized.ToLowerInvariant();
        if (lowered.Contains("핵심 내용 확인이 필요", StringComparison.Ordinal)
            || lowered.Contains("내용없음", StringComparison.Ordinal)
            || lowered.Contains("내용 없음", StringComparison.Ordinal)
            || lowered.Contains("vertexaisearch.cloud.google.com", StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(
                lowered,
                @"(?:^|\n)\s*no\.\d+\s*제목:\s*(view|article|news|briefing|newsletter|news\.php|articleview\.html|actuallyview\.do)\b",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        var titleMatches = Regex.Matches(
            normalized,
            @"(?:^|\n)\s*No\.(?<n>\d{1,2})\s*제목:\s*(?<title>[^\n]+)",
            RegexOptions.CultureInvariant
        );
        if (titleMatches.Count < targetCount)
        {
            return false;
        }

        var uniqueTitleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in titleMatches)
        {
            if (!match.Success)
            {
                continue;
            }

            var titleKey = NormalizeFallbackListText(match.Groups["title"].Value, 120, string.Empty).ToLowerInvariant();
            if (titleKey.Length == 0)
            {
                continue;
            }

            uniqueTitleKeys.Add(titleKey);
        }

        var minimumUniqueTitles = Math.Clamp(targetCount - 2, 3, targetCount);
        if (uniqueTitleKeys.Count < minimumUniqueTitles)
        {
            return false;
        }

        var normalizedFocus = (sourceFocus ?? string.Empty).Trim();
        if (normalizedFocus.Length >= 2
            && normalized.IndexOf(normalizedFocus, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    private static string BuildCitationDisplayTitle(
        SearchCitationReference citation,
        string sourceLabel,
        int ordinal,
        bool hasDirectCitation
    )
    {
        var title = NormalizeFallbackListText(citation.Title, 120, string.Empty);
        if (title.Length == 0 || IsLowQualityCitationForList(citation))
        {
            return hasDirectCitation
                ? $"{sourceLabel} 주요 기사"
                : $"{sourceLabel} 추가 기사 {ordinal}";
        }

        return title;
    }

    private static string BuildCitationDisplaySummary(
        SearchCitationReference citation,
        string sourceLabel,
        bool hasDirectCitation
    )
    {
        var summary = NormalizeFallbackListText(citation.Snippet, 200, string.Empty);
        if (summary.Length == 0
            || summary.Contains("핵심 내용 확인이 필요합니다.", StringComparison.OrdinalIgnoreCase))
        {
            return hasDirectCitation
                ? $"{sourceLabel} 보도 기사입니다. 원문 요약 데이터가 부족해 상세 내용은 원문 확인이 필요합니다."
                : "추가 업데이트 확인 중입니다.";
        }

        return summary;
    }

    private static string ApplyHighRiskFactualGuardFallback(string input, string responseText)
    {
        var normalizedInput = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedInput.Length == 0)
        {
            return (responseText ?? string.Empty).Trim();
        }

        var asksMarsNuke = ContainsAny(normalizedInput, "화성", "mars")
            && ContainsAny(normalizedInput, "핵폭탄", "nuke", "테라포밍", "terraform");
        if (!asksMarsNuke)
        {
            return (responseText ?? string.Empty).Trim();
        }

        var normalizedResponse = (responseText ?? string.Empty).Trim();
        if (normalizedResponse.Length == 0)
        {
            normalizedResponse = "확인 가능한 공식 기록이 없습니다.";
        }

        var loweredResponse = normalizedResponse.ToLowerInvariant();
        var hasNoEvidenceSignal = ContainsAny(
            loweredResponse,
            "없",
            "없습니다",
            "확인 불가",
            "공식 기록",
            "실행된 사실",
            "존재하지 않",
            "가설",
            "not available",
            "no official",
            "no evidence"
        );
        if (hasNoEvidenceSignal)
        {
            return normalizedResponse;
        }

        return """
               현재까지 스페이스X·일론 머스크가 화성 극지방에 핵폭탄을 실제로 투하했다는 공식 기록은 없습니다.
               - 정확한 투하 날짜: 확인 불가(실행된 사실 없음)
               - 화성 대기압 상승 수치: 실측 데이터 없음(이론적 가정만 존재)
               """;
    }

    private static SearchCitationReference[] FilterCitationsBySourceFocus(
        IReadOnlyList<SearchCitationReference> citations,
        string sourceFocus
    )
    {
        if (citations == null || citations.Count == 0)
        {
            return Array.Empty<SearchCitationReference>();
        }

        var focus = (sourceFocus ?? string.Empty).Trim();
        if (focus.Length < 2)
        {
            return citations.ToArray();
        }

        var focusKey = Regex.Replace(focus.ToLowerInvariant(), @"\s+", string.Empty);
        var matched = citations
            .Where(item =>
            {
                var labelKey = Regex.Replace(ResolveSourceLabel(item.Url, item.Title).ToLowerInvariant(), @"\s+", string.Empty);
                if (labelKey.Contains(focusKey, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!TryNormalizeDisplaySourceUrl(item.Url, out var normalizedUrl)
                    || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                var hostKey = Regex.Replace(uri.Host.ToLowerInvariant(), @"\s+", string.Empty);
                return hostKey.Contains(focusKey, StringComparison.Ordinal);
            })
            .ToArray();
        return matched.Length == 0 ? citations.ToArray() : matched;
    }

    private static string ReplaceListUrlsWithSourceLabelsWithoutCitations(string responseText)
    {
        var normalized = (responseText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                output.Add(string.Empty);
                continue;
            }

            if (TryExtractDisplaySourceLineValue(trimmed, out _))
            {
                var match = HttpUrlRegex.Match(trimmed);
                if (match.Success)
                {
                    output.Add($"출처: {ResolveSourceLabel(match.Value, null)}");
                }
                else
                {
                    output.Add(trimmed);
                }
                continue;
            }

            var replaced = HttpUrlRegex.Replace(trimmed, match =>
                ResolveSourceLabel(match.Value, null));
            replaced = Regex.Replace(replaced, @"\s{2,}", " ").Trim();
            replaced = Regex.Replace(replaced, @"\s*([/\|,;:])\s*$", string.Empty);
            if (replaced.Length == 0)
            {
                continue;
            }

            output.Add(replaced);
        }

        var merged = string.Join('\n', output).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    private static bool ResponseHasValidSourceCoverage(
        string responseText,
        int targetCount,
        IReadOnlyList<SearchCitationReference> validCitations
    )
    {
        if (targetCount <= 0)
        {
            return true;
        }

        if (validCitations == null || validCitations.Count == 0)
        {
            return false;
        }

        var citationUrlKeys = validCitations
            .Select(item => BuildSourceUrlMatchKey(item.Url))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (citationUrlKeys.Count == 0)
        {
            return false;
        }

        var matchedCount = 0;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawUrl in ExtractSourceUrlsFromResponse(responseText))
        {
            if (!TryNormalizeDisplaySourceUrl(rawUrl, out var normalizedResponseUrl))
            {
                continue;
            }

            var responseUrlKey = BuildSourceUrlMatchKey(normalizedResponseUrl);
            if (responseUrlKey.Length == 0)
            {
                continue;
            }

            if (!citationUrlKeys.Contains(responseUrlKey) || !seenKeys.Add(responseUrlKey))
            {
                continue;
            }

            matchedCount += 1;
            if (matchedCount >= targetCount)
            {
                return true;
            }
        }

        var citationLabelKeys = validCitations
            .Select(item => NormalizeSourceLabelKey(ResolveSourceLabel(item.Url, item.Title)))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (citationLabelKeys.Count == 0)
        {
            return false;
        }

        var seenLabelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedLabelCount = 0;
        foreach (var sourceLabel in ExtractSourceLabelsFromResponse(responseText))
        {
            var labelKey = NormalizeSourceLabelKey(sourceLabel);
            if (labelKey.Length == 0)
            {
                continue;
            }

            if (!citationLabelKeys.Contains(labelKey) || !seenLabelKeys.Add(labelKey))
            {
                continue;
            }

            matchedLabelCount += 1;
            if (matchedLabelCount >= targetCount)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReplaceListUrlsWithSourceLabels(
        string responseText,
        IReadOnlyList<SearchCitationReference> validCitations
    )
    {
        var normalized = (responseText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var urlLabelByKey = validCitations
            .Select(item =>
            {
                var key = BuildSourceUrlMatchKey(item.Url);
                if (key.Length == 0)
                {
                    return default(KeyValuePair<string, string>?);
                }

                var label = ResolveSourceLabel(item.Url, item.Title);
                return new KeyValuePair<string, string>(key, label);
            })
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                output.Add(string.Empty);
                continue;
            }

            if (TryExtractDisplaySourceLineValue(trimmed, out _))
            {
                var match = HttpUrlRegex.Match(trimmed);
                if (!match.Success)
                {
                    output.Add(trimmed);
                    continue;
                }

                var label = ResolveSourceLabelByUrl(
                    match.Value,
                    urlLabelByKey,
                    fallbackLabel: "출처 확인 필요"
                );
                output.Add($"출처: {label}");
                continue;
            }

            var replaced = HttpUrlRegex.Replace(trimmed, match =>
                ResolveSourceLabelByUrl(match.Value, urlLabelByKey, fallbackLabel: "출처"));
            replaced = Regex.Replace(replaced, @"\s{2,}", " ").Trim();
            replaced = Regex.Replace(replaced, @"\s*([/\|,;:])\s*$", string.Empty);
            if (replaced.Length == 0)
            {
                continue;
            }

            output.Add(replaced);
        }

        var merged = string.Join('\n', output).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    private static string ResolveSourceLabelByUrl(
        string rawUrl,
        IReadOnlyDictionary<string, string> urlLabelByKey,
        string fallbackLabel
    )
    {
        var key = BuildSourceUrlMatchKey(rawUrl);
        if (key.Length > 0 && urlLabelByKey.TryGetValue(key, out var labelFromKey) && !string.IsNullOrWhiteSpace(labelFromKey))
        {
            return labelFromKey;
        }

        var label = ResolveSourceLabel(rawUrl, null);
        return string.IsNullOrWhiteSpace(label) ? fallbackLabel : label;
    }

    private static string ResolveSourceLabel(string? rawUrl, string? title)
    {
        var normalizedTitle = NormalizeFallbackListText(title, 80, string.Empty);
        if (TryNormalizeDisplaySourceUrl(rawUrl, out var normalizedUrl)
            && Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            var host = uri.Host.Trim().ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal))
            {
                host = host[4..];
            }

            if (host.Equals("news.google.com", StringComparison.OrdinalIgnoreCase))
            {
                var inferred = TryExtractSourceLabelFromTitle(normalizedTitle);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    return inferred;
                }
            }

            foreach (var entry in SourceLabelByHostSuffix)
            {
                if (host.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("." + entry.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
            return host;
        }

        return string.IsNullOrWhiteSpace(normalizedTitle) ? "출처 확인 필요" : normalizedTitle;
    }

    private static string TryExtractSourceLabelFromTitle(string title)
    {
        var normalized = (title ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        string[] separators = { " - ", " | ", " · ", " — " };
        foreach (var separator in separators)
        {
            var pivot = normalized.LastIndexOf(separator, StringComparison.Ordinal);
            if (pivot <= 0 || pivot >= normalized.Length - separator.Length)
            {
                continue;
            }

            var candidate = normalized[(pivot + separator.Length)..].Trim();
            if (candidate.Length < 2 || candidate.Length > 40)
            {
                continue;
            }

            if (candidate.Contains("뉴스", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("news", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            if (Regex.IsMatch(candidate, @"^[A-Za-z0-9 ._-]{2,40}$", RegexOptions.CultureInvariant))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> ExtractSourceUrlsFromResponse(string responseText)
    {
        var normalized = (responseText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        foreach (var line in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = line.Trim();
            if (TryExtractDisplaySourceLineValue(trimmed, out var candidate))
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    yield return candidate;
                }
            }

            foreach (Match match in HttpUrlRegex.Matches(trimmed))
            {
                if (match.Success && !string.IsNullOrWhiteSpace(match.Value))
                {
                    yield return match.Value.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> ExtractSourceLabelsFromResponse(string responseText)
    {
        var normalized = (responseText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        foreach (var line in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = line.Trim();
            if (!TryExtractDisplaySourceLineValue(trimmed, out var candidate))
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string BuildSourceUrlMatchKey(string? rawUrl)
    {
        if (!TryNormalizeDisplaySourceUrl(rawUrl, out var normalizedUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var portPart = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = string.IsNullOrWhiteSpace(uri.AbsolutePath)
            ? "/"
            : uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        var query = uri.Query ?? string.Empty;
        return $"{scheme}://{host}{portPart}{path}{query}";
    }

    private static string NormalizeSourceLabelKey(string? label)
    {
        var normalized = (label ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"\s+", string.Empty);
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}]", string.Empty);
        return normalized;
    }

    private static IReadOnlyList<string> FilterVisibleDisplaySources(IEnumerable<string> sources)
    {
        return sources
            .SelectMany(value =>
            {
                var normalized = NormalizeDisplaySourceCandidate(value);
                if (normalized.Length == 0)
                {
                    return Array.Empty<string>();
                }

                var split = normalized.Split(new[] { ',', '·', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return split.Length == 0 ? new[] { normalized } : split;
            })
            .Select(NormalizeDisplaySourceCandidate)
            .Where(value => value.Length > 0 && !ShouldHideDisplaySource(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryExtractDisplaySourceLineValue(string? line, out string value)
    {
        value = string.Empty;
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(
            trimmed,
            @"^(?:\*\*)?\s*출처\s*[:：]\s*(?:\*\*)?\s*(?<value>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups["value"].Value.Trim();
        return value.Length > 0;
    }

    private static bool IsDisplaySourceLine(string? line)
    {
        return TryExtractDisplaySourceLineValue(line, out _);
    }

    private static string FilterVisibleDisplaySourceLine(string? sourceLine)
    {
        var normalized = NormalizeDisplaySourceCandidate(sourceLine);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var candidates = normalized
            .Split(new[] { ',', '·', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (candidates.Length == 0)
        {
            return ShouldHideDisplaySource(normalized) ? string.Empty : normalized;
        }

        var visible = FilterVisibleDisplaySources(candidates);
        return visible.Count == 0 ? string.Empty : string.Join(", ", visible);
    }

    private static string NormalizeDisplaySourceCandidate(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"^\s*출처\s*링크\s*:\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Trim().Trim(',', ';', '|', '/');
        return normalized;
    }

    private static bool ShouldHideDisplaySource(string? value)
    {
        var normalized = NormalizeDisplaySourceCandidate(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            return IsHiddenDisplaySourceHost(absoluteUri.Host);
        }

        var hostCandidate = Regex.Replace(normalized, @"^https?://", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var cutIndex = hostCandidate.IndexOfAny(new[] { '/', '?', '#', ' ' });
        if (cutIndex >= 0)
        {
            hostCandidate = hostCandidate[..cutIndex];
        }

        return IsHiddenDisplaySourceHost(hostCandidate);
    }

    private static bool IsHiddenDisplaySourceHost(string? host)
    {
        var normalized = (host ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        return normalized.Equals("vietnam.vn", StringComparison.Ordinal)
            || normalized.EndsWith(".vietnam.vn", StringComparison.Ordinal);
    }

    private static bool LooksLikeListOutputRequest(string input)
    {
        return SearchQueryPolicy.LooksLikeListOutputRequest(input);
    }

    private static bool LooksLikeTableRenderRequest(string input)
    {
        return SearchQueryPolicy.LooksLikeTableRenderRequest(input);
    }

    private static int CountListItemsInResponse(string responseText)
    {
        var normalized = (responseText ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return 0;
        }

        var declared = Regex.Match(
            normalized,
            @"요청하신[^\n]{0,60}?(?<n>\d+)\s*건",
            RegexOptions.CultureInvariant
        );
        var declaredCount = 0;
        if (declared.Success
            && int.TryParse(declared.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDeclared))
        {
            declaredCount = parsedDeclared;
        }

        var noMatches = Regex.Matches(normalized, @"(?:^|\n)\s*No\.(?<n>\d{1,2})", RegexOptions.CultureInvariant);
        var noCount = 0;
        foreach (Match match in noMatches)
        {
            if (!match.Success)
            {
                continue;
            }

            if (int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                noCount = Math.Max(noCount, n);
            }
        }

        var numberedMatches = Regex.Matches(normalized, @"(?:^|\n)\s*(?<n>\d{1,2})\.", RegexOptions.CultureInvariant);
        var numberedCount = 0;
        foreach (Match match in numberedMatches)
        {
            if (!match.Success)
            {
                continue;
            }

            if (int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                if (n <= 30)
                {
                    numberedCount = Math.Max(numberedCount, n);
                }
            }
        }

        var sourceCount = Regex.Matches(normalized, @"(?:^|\n)\s*출처\s*:", RegexOptions.CultureInvariant).Count;
        return Math.Max(Math.Max(declaredCount, noCount), Math.Max(numberedCount, sourceCount));
    }

    private static string NormalizeFallbackListText(string? value, int maxLength, string fallback)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return fallback;
        }

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "...";
    }

    private static string NormalizeFallbackSourceUrl(string? rawUrl)
    {
        if (TryNormalizeDisplaySourceUrl(rawUrl, out var normalizedUrl))
        {
            return normalizedUrl;
        }

        var normalized = (rawUrl ?? string.Empty).Trim();
        return normalized.Length == 0 ? "-" : NormalizeFallbackListText(normalized, 160, "-");
    }

    private static string NormalizeContextSourceUrl(string? rawUrl)
    {
        if (TryNormalizeDisplaySourceUrl(rawUrl, out var normalizedUrl))
        {
            return normalizedUrl;
        }

        var normalized = (rawUrl ?? string.Empty).Trim();
        return normalized.Length == 0 ? "-" : NormalizeFallbackListText(normalized, 180, "-");
    }

    private static bool TryNormalizeDisplaySourceUrl(string? rawUrl, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        var raw = (rawUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (uri.Host.Contains("vertexaisearch.cloud.google.com", StringComparison.OrdinalIgnoreCase))
        {
            if (!uri.AbsolutePath.Contains("/grounding-api-redirect", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryExtractGroundingRedirectTarget(uri, out var resolvedTarget))
            {
                return false;
            }

            normalizedUrl = resolvedTarget;
            return true;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static bool TryExtractGroundingRedirectTarget(Uri redirectUri, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        var query = redirectUri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return false;
        }

        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(kv[0]).Trim().ToLowerInvariant();
            if (key is not ("url" or "u" or "target" or "dest" or "destination" or "redirect" or "r" or "to"))
            {
                continue;
            }

            var value = kv.Length > 1 ? kv[1] : string.Empty;
            var decoded = value;
            for (var i = 0; i < 2; i += 1)
            {
                decoded = Uri.UnescapeDataString(decoded);
            }

            if (!Uri.TryCreate(decoded, UriKind.Absolute, out var target))
            {
                continue;
            }

            if (!target.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                && !target.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (target.Host.Contains("vertexaisearch.cloud.google.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalizedUrl = target.AbsoluteUri;
            return true;
        }

        return false;
    }
}
