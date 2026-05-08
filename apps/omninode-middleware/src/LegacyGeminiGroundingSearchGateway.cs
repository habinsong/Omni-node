using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed class LegacyGeminiGroundingSearchGateway : SearchGateway
{
    private const int DefaultTargetCount = 5;
    private const int MaxTargetCount = 10;
    private const int MaxRetrieverResults = 50;
    private static readonly Regex QuotedPhraseRegex = new(
        "[\"'`“”‘’](?<value>[^\"'`“”‘’]{2,80})[\"'`“”‘’]",
        RegexOptions.Compiled
    );
    private static readonly Regex DomainLikeTitleRegex = new(
        @"^[a-z0-9][a-z0-9\.-]*\.[a-z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly TimeSpan SuccessCacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RedirectResolveTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly HttpClient RedirectResolveHttpClient = CreateRedirectResolveHttpClient();
    private static readonly ConcurrentDictionary<string, string?> RedirectResolveCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CachedSearchSnapshot> SuccessCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISearchRetriever _searchRetriever;
    private readonly IEvidencePackBuilder _evidencePackBuilder;

    private sealed record CachedSearchSnapshot(
        IReadOnlyList<SearchDocument> Docs,
        DateTimeOffset CachedAtUtc
    );

    public LegacyGeminiGroundingSearchGateway(
        ISearchRetriever searchRetriever,
        IEvidencePackBuilder evidencePackBuilder
    )
    {
        _searchRetriever = searchRetriever;
        _evidencePackBuilder = evidencePackBuilder;
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = (request.Query ?? string.Empty).Trim();
        var targetCount = ResolveTargetCount(request.Constraints.TargetCount);
        var minIndependentSources = Math.Max(1, request.Constraints.MinIndependentSources);
        var sourceConstrained = query.Contains("site:", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            var termination = BuildTermination(
                SearchLoopTerminationReason.EmptyQuery,
                reasonCode: "empty_query",
                countLockReasonCode: "not_evaluated",
                attemptCount: 0,
                collectedCandidateCount: 0,
                validDocumentCount: 0,
                independentSourceCount: 0
            );
            return BuildBlockedResponse(
                request,
                targetCount,
                minIndependentSources,
                coverageReasonCode: "not_evaluated",
                termination: termination
            );
        }

        var maxResults = ResolveRetrieverMaxResults(targetCount, sourceConstrained);
        async Task<(GeminiGroundedRetrieverResult RetrieverResult, IReadOnlyList<SearchDocument> Docs)> RunAttemptAsync(SearchRequest attemptRequest)
        {
            var result = await _searchRetriever
                .RetrieveAsync(attemptRequest, maxResults, cancellationToken)
                .ConfigureAwait(false);
            if (result.Disabled)
            {
                return (result, Array.Empty<SearchDocument>());
            }

            var built = await BuildDocumentsAsync(
                result.Results,
                attemptRequest,
                targetCount,
                cancellationToken
            ).ConfigureAwait(false);
            return (result, built);
        }

        static int CountIndependentSources(IReadOnlyList<SearchDocument> docs)
        {
            return docs
                .Select(x => x.Domain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        var firstAttempt = await RunAttemptAsync(request).ConfigureAwait(false);
        var firstIndependentSourceCount = CountIndependentSources(firstAttempt.Docs);
        var firstCountLockSatisfied = firstAttempt.Docs.Count >= targetCount
                                      && firstIndependentSourceCount >= minIndependentSources;
        if (firstCountLockSatisfied)
        {
            CacheSuccessfulDocs(query, targetCount, firstAttempt.Docs, request.RequestedAtUtc);
            var successTermination = BuildTermination(
                SearchLoopTerminationReason.Success,
                reasonCode: "retrieved_once",
                countLockReasonCode: "count_lock_satisfied",
                attemptCount: 1,
                collectedCandidateCount: firstAttempt.RetrieverResult.Results.Count,
                validDocumentCount: firstAttempt.Docs.Count,
                independentSourceCount: firstIndependentSourceCount
            );
            return new SearchResponse(SearchRetrieverPath.GeminiGrounding, firstAttempt.Docs, targetCount, true)
            {
                EvidencePack = _evidencePackBuilder.Build(
                    request,
                    firstAttempt.Docs,
                    targetCount,
                    minIndependentSources,
                    countLockSatisfied: true,
                    coverageReason: "coverage_available",
                    termination: successTermination
                ),
                Termination = successTermination
            };
        }

        // 1회 자동 재시도: 같은 질의 반복 대신 검색 친화적으로 재작성한 질의를 사용하고 결과를 병합한다.
        var retryRequest = BuildRetrySearchRequest(request);
        var secondRawAttempt = await RunAttemptAsync(retryRequest).ConfigureAwait(false);
        var mergedRetryDocs = MergeSearchDocuments(firstAttempt.Docs, secondRawAttempt.Docs, targetCount);
        var secondAttempt = (secondRawAttempt.RetrieverResult, Docs: mergedRetryDocs);
        var secondIndependentSourceCount = CountIndependentSources(secondAttempt.Docs);
        var secondCountLockSatisfied = secondAttempt.Docs.Count >= targetCount
                                       && secondIndependentSourceCount >= minIndependentSources;
        if (secondCountLockSatisfied)
        {
            CacheSuccessfulDocs(query, targetCount, secondAttempt.Docs, request.RequestedAtUtc);
            var successTermination = BuildTermination(
                SearchLoopTerminationReason.Success,
                reasonCode: string.Equals(retryRequest.Query, request.Query, StringComparison.Ordinal)
                    ? "retrieved_with_retry"
                    : "retrieved_with_rewritten_retry",
                countLockReasonCode: "count_lock_satisfied",
                attemptCount: 2,
                collectedCandidateCount: firstAttempt.RetrieverResult.Results.Count + secondRawAttempt.RetrieverResult.Results.Count,
                validDocumentCount: secondAttempt.Docs.Count,
                independentSourceCount: secondIndependentSourceCount
            );
            return new SearchResponse(SearchRetrieverPath.GeminiGrounding, secondAttempt.Docs, targetCount, true)
            {
                EvidencePack = _evidencePackBuilder.Build(
                    request,
                    secondAttempt.Docs,
                    targetCount,
                    minIndependentSources,
                    countLockSatisfied: true,
                    coverageReason: "coverage_available",
                    termination: successTermination
                ),
                Termination = successTermination
            };
        }

        var useSecondAttempt = secondAttempt.Docs.Count >= firstAttempt.Docs.Count;
        var selectedAttempt = useSecondAttempt ? secondAttempt : firstAttempt;
        var selectedIndependentSourceCount = useSecondAttempt ? secondIndependentSourceCount : firstIndependentSourceCount;
        if (TryGetCachedDocs(query, targetCount, request.RequestedAtUtc, out var cachedDocs))
        {
            var cachedIndependentSourceCount = CountIndependentSources(cachedDocs);
            if (cachedDocs.Count >= targetCount && cachedIndependentSourceCount >= minIndependentSources)
            {
                var cacheTermination = BuildTermination(
                    SearchLoopTerminationReason.Success,
                    reasonCode: "cache_fallback",
                    countLockReasonCode: "count_lock_satisfied",
                    attemptCount: 2,
                    collectedCandidateCount: cachedDocs.Count,
                    validDocumentCount: cachedDocs.Count,
                    independentSourceCount: cachedIndependentSourceCount
                );
                return new SearchResponse(SearchRetrieverPath.LocalCacheFallback, cachedDocs, targetCount, true)
                {
                    EvidencePack = _evidencePackBuilder.Build(
                        request,
                        cachedDocs,
                        targetCount,
                        minIndependentSources,
                        countLockSatisfied: true,
                        coverageReason: "coverage_available",
                        termination: cacheTermination
                    ),
                    Termination = cacheTermination
                };
            }
        }

        if (selectedAttempt.Docs.Count > 0)
        {
            var countLockTermination = BuildTermination(
                SearchLoopTerminationReason.RetrievePlanExhausted,
                reasonCode: string.Equals(retryRequest.Query, request.Query, StringComparison.Ordinal)
                    ? "count_lock_unsatisfied_after_retries"
                    : "count_lock_unsatisfied_after_rewritten_retry",
                countLockReasonCode: "count_lock_unsatisfied",
                attemptCount: 2,
                collectedCandidateCount: firstAttempt.RetrieverResult.Results.Count + secondRawAttempt.RetrieverResult.Results.Count,
                validDocumentCount: selectedAttempt.Docs.Count,
                independentSourceCount: selectedIndependentSourceCount
            );
            return BuildBlockedResponse(
                request,
                targetCount,
                minIndependentSources,
                coverageReasonCode: "count_lock_unsatisfied",
                documents: selectedAttempt.Docs,
                termination: countLockTermination
            );
        }

        var exhaustedReasonCode = ResolveRetrieverFailureReasonCode(selectedAttempt.RetrieverResult);
        var exhaustedTermination = BuildTermination(
            SearchLoopTerminationReason.RetrievePlanExhausted,
            reasonCode: exhaustedReasonCode,
            countLockReasonCode: "not_evaluated",
            attemptCount: 2,
            collectedCandidateCount: firstAttempt.RetrieverResult.Results.Count + secondRawAttempt.RetrieverResult.Results.Count,
            validDocumentCount: 0,
            independentSourceCount: 0
        );
        return BuildBlockedResponse(
            request,
            targetCount,
            minIndependentSources,
            coverageReasonCode: "no_documents",
            termination: exhaustedTermination
        );
    }

    private static string BuildCacheKey(string query, int targetCount)
    {
        return $"{targetCount}|{(query ?? string.Empty).Trim().ToLowerInvariant()}";
    }

    private static void CacheSuccessfulDocs(
        string query,
        int targetCount,
        IReadOnlyList<SearchDocument> docs,
        DateTimeOffset requestedAtUtc
    )
    {
        if (docs == null || docs.Count == 0)
        {
            return;
        }

        var key = BuildCacheKey(query, targetCount);
        SuccessCache[key] = new CachedSearchSnapshot(docs, requestedAtUtc);
    }

    private static bool TryGetCachedDocs(
        string query,
        int targetCount,
        DateTimeOffset requestedAtUtc,
        out IReadOnlyList<SearchDocument> docs
    )
    {
        docs = Array.Empty<SearchDocument>();
        var key = BuildCacheKey(query, targetCount);
        if (!SuccessCache.TryGetValue(key, out var snapshot))
        {
            return false;
        }

        if (requestedAtUtc - snapshot.CachedAtUtc > SuccessCacheTtl)
        {
            SuccessCache.TryRemove(key, out _);
            return false;
        }

        docs = snapshot.Docs;
        return docs.Count > 0;
    }

    private static SearchRequest BuildRetrySearchRequest(SearchRequest request)
    {
        var rewritten = RewriteSearchQueryForRetry(request.Query, request);
        if (string.Equals(rewritten, request.Query, StringComparison.Ordinal))
        {
            return request;
        }

        return request with { Query = rewritten };
    }

    private static string RewriteSearchQueryForRetry(string query, SearchRequest request)
    {
        var normalized = NormalizeSearchQueryText(query);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Contains("site:", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var tokens = ExtractSearchQueryTokens(normalized);
        if (tokens.Count == 0)
        {
            return normalized;
        }

        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
            builder.Append(token);
        }

        var rewritten = builder.ToString().Trim();
        if (request.IntentProfile.TimeSensitivity == QueryTimeSensitivity.High && !ContainsAnyToken(rewritten, "latest", "recent", "today", "breaking", "최신", "최근", "오늘", "속보"))
        {
            rewritten += " latest";
        }

        if (request.IntentProfile.AnswerType == QueryAnswerType.List && !ContainsAnyToken(rewritten, "top", "list", "목록", "리스트"))
        {
            rewritten += " list";
        }

        return rewritten.Length == 0 ? normalized : rewritten;
    }

    private static string NormalizeSearchQueryText(string query)
    {
        return Regex.Replace((query ?? string.Empty).Trim(), @"\s+", " ");
    }

    private static IReadOnlyList<string> ExtractSearchQueryTokens(string query)
    {
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in QuotedPhraseRegex.Matches(query))
        {
            var phrase = NormalizeSearchQueryText(match.Groups["value"].Value);
            if (phrase.Length >= 2 && seen.Add(phrase))
            {
                tokens.Add(phrase);
            }
        }

        foreach (var rawToken in Regex.Split(query, @"[^\p{L}\p{N}\.\-_/]+"))
        {
            var token = rawToken.Trim().Trim('.', ',', ':', ';', '!', '?', '(', ')', '[', ']');
            if (token.Length < 2 || IsSearchQueryStopword(token))
            {
                continue;
            }

            if (seen.Add(token))
            {
                tokens.Add(token);
            }

            if (tokens.Count >= 14)
            {
                break;
            }
        }

        return tokens;
    }

    private static bool IsSearchQueryStopword(string token)
    {
        var lowered = token.ToLowerInvariant();
        return lowered is
            "the" or "a" or "an" or "and" or "or" or "for" or "to" or "of" or "in" or "on" or "with" or
            "about" or "please" or "tell" or "me" or "what" or "how" or "why" or "is" or "are" or
            "좀" or "알려줘" or "설명해줘" or "정리해줘" or "해줘" or "줘" or "뭐야" or "무엇" or "어떻게" or "왜" or "그리고" or "또는" or "관련";
    }

    private static bool ContainsAnyToken(string text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SearchDocument> MergeSearchDocuments(
        IReadOnlyList<SearchDocument> first,
        IReadOnlyList<SearchDocument> second,
        int take
    )
    {
        var merged = new List<SearchDocument>(Math.Max(1, take));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRange(IReadOnlyList<SearchDocument> docs)
        {
            foreach (var doc in docs)
            {
                if (merged.Count >= take || doc == null)
                {
                    break;
                }

                var key = string.IsNullOrWhiteSpace(doc.Url) ? doc.Title : doc.Url;
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key.Trim()))
                {
                    continue;
                }

                merged.Add(doc with { CitationId = $"c{merged.Count + 1}" });
            }
        }

        AddRange(first);
        AddRange(second);
        return merged;
    }

    private SearchResponse BuildBlockedResponse(
        SearchRequest request,
        int targetCount,
        int minIndependentSources,
        string coverageReasonCode,
        IReadOnlyList<SearchDocument>? documents = null,
        SearchLoopTermination? termination = null
    )
    {
        var blockedDocuments = documents ?? Array.Empty<SearchDocument>();
        return new SearchResponse(SearchRetrieverPath.GeminiGrounding, blockedDocuments, targetCount, false)
        {
            EvidencePack = _evidencePackBuilder.Build(
                request,
                blockedDocuments,
                targetCount,
                minIndependentSources,
                countLockSatisfied: false,
                coverageReason: coverageReasonCode,
                termination: termination
            ),
            Termination = termination
        };
    }

    private static SearchLoopTermination BuildTermination(
        SearchLoopTerminationReason reason,
        string reasonCode,
        string countLockReasonCode,
        int attemptCount,
        int collectedCandidateCount,
        int validDocumentCount,
        int independentSourceCount
    )
    {
        return new SearchLoopTermination(
            Reason: reason,
            ReasonCode: reasonCode,
            CountLockReasonCode: countLockReasonCode,
            AttemptCount: attemptCount,
            CollectedCandidateCount: collectedCandidateCount,
            ValidDocumentCount: validDocumentCount,
            IndependentSourceCount: independentSourceCount
        );
    }

    private static string ResolveRetrieverFailureReasonCode(GeminiGroundedRetrieverResult retrieverResult)
    {
        if (!retrieverResult.Disabled)
        {
            return "no_documents";
        }

        var normalizedError = (retrieverResult.Error ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedError.Contains("api key missing", StringComparison.Ordinal))
        {
            return "gemini_api_key_missing";
        }

        if (normalizedError.Contains("timeout", StringComparison.Ordinal))
        {
            return "gemini_grounding_timeout";
        }

        if (normalizedError.Contains("canceled", StringComparison.Ordinal)
            || normalizedError.Contains("cancelled", StringComparison.Ordinal))
        {
            return "gemini_grounding_timeout";
        }

        if (normalizedError.Contains("http 401", StringComparison.Ordinal)
            || normalizedError.Contains("http 403", StringComparison.Ordinal))
        {
            return "gemini_auth_failed";
        }

        if (normalizedError.Contains("http 429", StringComparison.Ordinal)
            || normalizedError.Contains("rate", StringComparison.Ordinal))
        {
            return "gemini_rate_limited";
        }

        if (normalizedError.Contains("result empty", StringComparison.Ordinal))
        {
            return "gemini_result_empty";
        }

        if (normalizedError.Contains("http 5", StringComparison.Ordinal))
        {
            return "gemini_upstream_error";
        }

        return "retriever_unavailable";
    }

    private static int ResolveTargetCount(int targetCount)
    {
        if (targetCount <= 0)
        {
            return DefaultTargetCount;
        }

        return Math.Clamp(targetCount, 1, MaxTargetCount);
    }

    private static int ResolveRetrieverMaxResults(int targetCount, bool sourceConstrained)
    {
        if (sourceConstrained)
        {
            return Math.Clamp(targetCount * 5, 1, MaxRetrieverResults);
        }

        return Math.Clamp(targetCount * 3, 1, MaxRetrieverResults);
    }

    private static async Task<IReadOnlyList<SearchDocument>> BuildDocumentsAsync(
        IReadOnlyList<GeminiGroundedResultItem> candidates,
        SearchRequest request,
        int targetCount,
        CancellationToken cancellationToken
    )
    {
        var docs = new List<SearchDocument>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var citationNumber = 1;

        var normalizeTasks = candidates
            .Select((item, index) => NormalizeCandidateUrlAsync(item, index, cancellationToken))
            .ToArray();
        var normalizedUrls = await Task.WhenAll(normalizeTasks).ConfigureAwait(false);
        Array.Sort(normalizedUrls, static (a, b) => a.Index.CompareTo(b.Index));

        foreach (var normalized in normalizedUrls)
        {
            if (docs.Count >= targetCount)
            {
                break;
            }

            var item = normalized.Item;
            var normalizedUrl = normalized.Url;
            if (normalizedUrl.Length == 0 || !seenUrls.Add(normalizedUrl))
            {
                continue;
            }

            if (!TryExtractDomain(normalizedUrl, out var domain))
            {
                continue;
            }

            var title = NormalizeCandidateText(item.Title);
            if (title.Length == 0)
            {
                title = domain;
            }

            var snippet = NormalizeCandidateText(item.Description);
            if (snippet.Length == 0 && DomainLikeTitleRegex.IsMatch(title))
            {
                var titleFromUrlPath = BuildFallbackTitleFromUrlPath(normalizedUrl);
                if (titleFromUrlPath.Length == 0)
                {
                    continue;
                }

                title = titleFromUrlPath;
            }
            if (!TryParsePublishedAt(item.Published, out var publishedAt))
            {
                publishedAt = request.RequestedAtUtc;
            }

            if (publishedAt > request.RequestedAtUtc)
            {
                publishedAt = request.RequestedAtUtc;
            }

            var ageHours = Math.Max(0d, (request.RequestedAtUtc - publishedAt).TotalHours);
            var freshnessScore = Math.Clamp(1.0d / (1.0d + (ageHours / 24d)), 0.0d, 1.0d);
            docs.Add(new SearchDocument(
                CitationId: $"c{citationNumber}",
                Title: title,
                Url: normalizedUrl,
                Domain: domain,
                PublishedAt: publishedAt,
                RetrievedAtUtc: request.RequestedAtUtc,
                Snippet: snippet,
                SourceType: "web",
                IsPrimarySource: IsPrimarySourceDomain(domain),
                FreshnessScore: freshnessScore,
                CredibilityScore: ScoreCredibility(domain),
                DuplicateClusterId: BuildDuplicateClusterId(domain, normalizedUrl)
            ));
            citationNumber++;
        }

        return docs;
    }

    private static async Task<(int Index, GeminiGroundedResultItem Item, string Url)> NormalizeCandidateUrlAsync(
        GeminiGroundedResultItem item,
        int index,
        CancellationToken cancellationToken
    )
    {
        var normalizedUrl = await NormalizeUrlAsync(item.Url, cancellationToken).ConfigureAwait(false);
        return (index, item, normalizedUrl);
    }

    private static bool TryParsePublishedAt(string? value, out DateTimeOffset publishedAt)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            publishedAt = default;
            return false;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out publishedAt))
        {
            publishedAt = publishedAt.ToUniversalTime();
            return true;
        }

        publishedAt = default;
        return false;
    }

    private static async Task<string> NormalizeUrlAsync(string? rawUrl, CancellationToken cancellationToken)
    {
        var normalized = (rawUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (uri.Host.Contains("vertexaisearch.cloud.google.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Contains("/grounding-api-redirect", StringComparison.OrdinalIgnoreCase))
        {
            if (TryExtractRedirectTarget(uri, out var resolvedTarget))
            {
                return resolvedTarget;
            }

            return await ResolveVertexRedirectUrlAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        return uri.AbsoluteUri;
    }

    private static bool TryExtractRedirectTarget(Uri redirectUri, out string normalizedUrl)
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

    private static async Task<string> ResolveVertexRedirectUrlAsync(Uri redirectUri, CancellationToken cancellationToken)
    {
        var cacheKey = redirectUri.AbsoluteUri;
        if (RedirectResolveCache.TryGetValue(cacheKey, out var cached))
        {
            return cached ?? string.Empty;
        }

        async Task<string> ResolveByMethodAsync(HttpMethod method)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RedirectResolveTimeout);
            using var request = new HttpRequestMessage(method, redirectUri);
            using var response = await RedirectResolveHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            if (!IsRedirectStatusCode(response.StatusCode))
            {
                return string.Empty;
            }

            var location = response.Headers.Location;
            if (location is null)
            {
                return string.Empty;
            }

            var absoluteLocation = location.IsAbsoluteUri ? location : new Uri(redirectUri, location);
            if (!absoluteLocation.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                && !absoluteLocation.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (absoluteLocation.Host.Contains("vertexaisearch.cloud.google.com", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return absoluteLocation.AbsoluteUri;
        }

        string resolved;
        try
        {
            resolved = await ResolveByMethodAsync(HttpMethod.Head).ConfigureAwait(false);
            if (resolved.Length == 0)
            {
                resolved = await ResolveByMethodAsync(HttpMethod.Get).ConfigureAwait(false);
            }
        }
        catch
        {
            resolved = string.Empty;
        }

        RedirectResolveCache[cacheKey] = resolved.Length == 0 ? null : resolved;
        return resolved;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and < 400;
    }

    private static HttpClient CreateRedirectResolveHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };

        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static bool TryExtractDomain(string normalizedUrl, out string domain)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            domain = string.Empty;
            return false;
        }

        domain = uri.Host.Trim().ToLowerInvariant();
        return true;
    }

    private static string NormalizeCandidateText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static string BuildFallbackTitleFromUrlPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var path = (uri.AbsolutePath ?? string.Empty).Trim('/');
        if (path.Length == 0)
        {
            return string.Empty;
        }

        var decoded = Uri.UnescapeDataString(path);
        decoded = decoded.Replace('-', ' ').Replace('_', ' ').Replace('/', ' ');
        decoded = Regex.Replace(decoded, @"\b\d{1,6}\b", " ");
        decoded = Regex.Replace(decoded, @"\s+", " ").Trim();
        if (decoded.Length < 6)
        {
            return string.Empty;
        }

        return decoded.Length <= 120
            ? decoded
            : decoded[..120].TrimEnd() + "...";
    }

    private static double ScoreCredibility(string domain)
    {
        if (domain.EndsWith(".gov", StringComparison.Ordinal)
            || domain.EndsWith(".go.kr", StringComparison.Ordinal)
            || domain.EndsWith(".gouv.fr", StringComparison.Ordinal))
        {
            return 0.95d;
        }

        if (domain.EndsWith(".edu", StringComparison.Ordinal)
            || domain.EndsWith(".ac.kr", StringComparison.Ordinal))
        {
            return 0.85d;
        }

        if (domain.EndsWith(".org", StringComparison.Ordinal))
        {
            return 0.72d;
        }

        return 0.60d;
    }

    private static bool IsPrimarySourceDomain(string domain)
    {
        return domain.EndsWith(".gov", StringComparison.Ordinal)
               || domain.EndsWith(".go.kr", StringComparison.Ordinal)
               || domain.EndsWith(".edu", StringComparison.Ordinal)
               || domain.EndsWith(".ac.kr", StringComparison.Ordinal);
    }

    private static string BuildDuplicateClusterId(string domain, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return domain;
        }

        var path = uri.AbsolutePath.Trim().ToLowerInvariant();
        if (path.Length == 0 || path == "/")
        {
            return domain;
        }

        return $"{domain}:{path}";
    }
}
