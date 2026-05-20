using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private async Task<IReadOnlyList<WebSearchResultItem>> BuildContextAlignedWebResultsAsync(
        string query,
        string source,
        string freshness,
        int requestedCount,
        string sourceFocus,
        string sourceDomain,
        IReadOnlyList<WebSearchResultItem> initialResults,
        CancellationToken cancellationToken
    )
    {
        if (initialResults == null || initialResults.Count == 0)
        {
            return Array.Empty<WebSearchResultItem>();
        }

        var targetCount = Math.Clamp(requestedCount, 1, 10);
        var normalizedFocus = (sourceFocus ?? string.Empty).Trim();
        if (_context.EnableFastWebPipeline)
        {
            return await BuildFastContextAlignedWebResultsAsync(
                query,
                source,
                freshness,
                targetCount,
                normalizedFocus,
                sourceDomain,
                initialResults,
                cancellationToken
            ).ConfigureAwait(false);
        }

        IReadOnlyList<WebSearchResultItem> candidatePool = initialResults;
        if (ShouldRecollectForSourceFocus(normalizedFocus, targetCount, candidatePool))
        {
            var expandedFreshness = ResolveSourceExpansionFreshness(freshness);
            var poolLimit = Math.Clamp(targetCount * 3, targetCount, 24);
            var merged = candidatePool.Take(poolLimit).ToArray();
            foreach (var expandedQuery in BuildSourceExpansionQueries(query, normalizedFocus).Take(4))
            {
                var expanded = await SearchWebAsync(
                    expandedQuery,
                    targetCount,
                    expandedFreshness,
                    cancellationToken,
                    source
                ).ConfigureAwait(false);
                if (expanded.Disabled || expanded.Results.Count == 0)
                {
                    continue;
                }

                merged = MergeWebSearchItemsByUrl(merged, expanded.Results, poolLimit);
                if (CountProbableSourceMatches(merged, normalizedFocus) >= targetCount
                    || merged.Length >= poolLimit)
                {
                    break;
                }
            }

            candidatePool = merged;

            if (CountProbableSourceMatches(candidatePool, normalizedFocus) < targetCount)
            {
                var normalizedDomain = SearchQueryPolicy.NormalizeSourceDomainHint(sourceDomain);
                if (normalizedDomain.Length == 0)
                {
                    normalizedDomain = ResolveSourceDomainFromQueryOrFocus(query, normalizedFocus);
                }

                if (normalizedDomain.Length > 0)
                {
                    var feedItems = await TryCollectDomainFeedItemsAsync(
                        normalizedDomain,
                        poolLimit,
                        cancellationToken
                    ).ConfigureAwait(false);
                    if (feedItems.Length > 0)
                    {
                        candidatePool = MergeWebSearchItemsByUrl(feedItems, candidatePool, poolLimit);
                    }
                }
            }
        }

        var intentAligned = await SelectWebResultsByIntentWithLlmAsync(
            query,
            normalizedFocus,
            candidatePool,
            targetCount,
            cancellationToken
        ).ConfigureAwait(false);
        IReadOnlyList<WebSearchResultItem> contextAligned;
        if (!ShouldRunArticleClassification(query, normalizedFocus, targetCount))
        {
            contextAligned = intentAligned;
        }
        else
        {
            contextAligned = await FilterArticleCandidatesAsync(
                query,
                normalizedFocus,
                sourceDomain,
                intentAligned,
                candidatePool,
                targetCount,
                cancellationToken
            ).ConfigureAwait(false);
        }

        return await BackfillCountOnceIfNeededAsync(
            query,
            source,
            freshness,
            normalizedFocus,
            sourceDomain,
            contextAligned,
            candidatePool,
            targetCount,
            cancellationToken
        ).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WebSearchResultItem>> BuildFastContextAlignedWebResultsAsync(
        string query,
        string source,
        string freshness,
        int targetCount,
        string sourceFocus,
        string sourceDomain,
        IReadOnlyList<WebSearchResultItem> initialResults,
        CancellationToken cancellationToken
    )
    {
        var cappedTargetCount = Math.Clamp(targetCount, 1, 10);
        var poolLimit = Math.Clamp(cappedTargetCount * 3, cappedTargetCount, 24);
        IReadOnlyList<WebSearchResultItem> candidatePool = initialResults.Take(poolLimit).ToArray();
        var normalizedDomain = SearchQueryPolicy.NormalizeSourceDomainHint(sourceDomain);
        if (normalizedDomain.Length == 0)
        {
            normalizedDomain = ResolveSourceDomainFromQueryOrFocus(query, sourceFocus);
        }

        if (sourceFocus.Length >= 2
            && CountProbableSourceMatches(candidatePool, sourceFocus) < cappedTargetCount
            && normalizedDomain.Length > 0)
        {
            try
            {
                using var feedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                feedCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp((_context.LlmTimeoutSec / 2) + 1, 3, 6)));
                var feedItems = await TryCollectDomainFeedItemsAsync(
                    normalizedDomain,
                    poolLimit,
                    feedCts.Token
                ).ConfigureAwait(false);
                if (feedItems.Length > 0)
                {
                    candidatePool = MergeWebSearchItemsByUrl(feedItems, candidatePool, poolLimit);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        var hardFiltered = candidatePool
            .Where(item => !IsHardNonArticleCandidate(item))
            .ToArray();
        var deterministic = BuildSourcePrioritizedFallback(
            hardFiltered.Length > 0 ? hardFiltered : candidatePool,
            sourceFocus,
            cappedTargetCount
        );
        return await BackfillCountOnceIfNeededAsync(
            query,
            source,
            freshness,
            sourceFocus,
            normalizedDomain,
            deterministic,
            candidatePool,
            cappedTargetCount,
            cancellationToken
        ).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WebSearchResultItem>> BackfillCountOnceIfNeededAsync(
        string query,
        string source,
        string freshness,
        string sourceFocus,
        string sourceDomain,
        IReadOnlyList<WebSearchResultItem> selected,
        IReadOnlyList<WebSearchResultItem> candidatePool,
        int targetCount,
        CancellationToken cancellationToken
    )
    {
        var cappedTargetCount = Math.Clamp(targetCount, 1, 10);
        var selectedItems = (selected ?? Array.Empty<WebSearchResultItem>())
            .Take(cappedTargetCount)
            .ToArray();
        if (selectedItems.Length >= cappedTargetCount)
        {
            return selectedItems;
        }

        var hardFilteredPool = (candidatePool ?? Array.Empty<WebSearchResultItem>())
            .Where(item => !IsHardNonArticleCandidate(item))
            .ToArray();
        var merged = MergeWebSearchItemsByUrl(
            selectedItems,
            BuildLikelyArticleFallback(hardFilteredPool, cappedTargetCount),
            cappedTargetCount
        );
        if (merged.Length >= cappedTargetCount)
        {
            return merged;
        }

        var normalizedDomain = SearchQueryPolicy.NormalizeSourceDomainHint(sourceDomain);
        if (normalizedDomain.Length == 0)
        {
            normalizedDomain = ResolveSourceDomainFromQueryOrFocus(query, sourceFocus);
        }

        var backfillQuery = BuildLowCostBackfillQuery(query, sourceFocus, normalizedDomain);
        if (backfillQuery.Length == 0)
        {
            return merged;
        }

        var backfillCount = cappedTargetCount;
        var backfillFreshness = ResolveSourceExpansionFreshness(freshness) ?? freshness;
        IReadOnlyList<WebSearchResultItem> backfillResults = Array.Empty<WebSearchResultItem>();
        try
        {
            using var backfillCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var backfillTimeout = _context.EnableFastWebPipeline
                ? Math.Clamp((_context.LlmTimeoutSec / 3) + 1, 2, 4)
                : Math.Clamp((_context.LlmTimeoutSec / 2) + 3, 5, 12);
            backfillCts.CancelAfter(TimeSpan.FromSeconds(backfillTimeout));
            var backfill = await SearchWebAsync(
                backfillQuery,
                backfillCount,
                backfillFreshness,
                backfillCts.Token,
                source
            ).ConfigureAwait(false);
            if (!backfill.Disabled && backfill.Results.Count > 0)
            {
                backfillResults = backfill.Results;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            backfillResults = Array.Empty<WebSearchResultItem>();
        }

        if (backfillResults.Count == 0)
        {
            return merged;
        }

        var hardFilteredBackfill = backfillResults
            .Where(item => !IsHardNonArticleCandidate(item))
            .ToArray();
        if (hardFilteredBackfill.Length == 0)
        {
            return merged;
        }

        merged = MergeWebSearchItemsByUrl(
            merged,
            BuildLikelyArticleFallback(hardFilteredBackfill, cappedTargetCount),
            cappedTargetCount
        );
        if (merged.Length >= cappedTargetCount)
        {
            return merged;
        }

        merged = MergeWebSearchItemsByUrl(merged, hardFilteredBackfill, cappedTargetCount);
        if (merged.Length >= cappedTargetCount || normalizedDomain.Length == 0)
        {
            return merged;
        }

        try
        {
            using var feedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var feedTimeout = _context.EnableFastWebPipeline
                ? Math.Clamp((_context.LlmTimeoutSec / 3) + 1, 2, 4)
                : Math.Clamp((_context.LlmTimeoutSec / 2) + 2, 4, 10);
            feedCts.CancelAfter(TimeSpan.FromSeconds(feedTimeout));
            var feedItems = await TryCollectDomainFeedItemsAsync(
                normalizedDomain,
                cappedTargetCount,
                feedCts.Token
            ).ConfigureAwait(false);
            if (feedItems.Length == 0)
            {
                return merged;
            }

            var hardFilteredFeed = feedItems
                .Where(item => !IsHardNonArticleCandidate(item))
                .ToArray();
            if (hardFilteredFeed.Length == 0)
            {
                return merged;
            }

            merged = MergeWebSearchItemsByUrl(
                merged,
                BuildLikelyArticleFallback(hardFilteredFeed, cappedTargetCount),
                cappedTargetCount
            );
            if (merged.Length >= cappedTargetCount)
            {
                return merged;
            }

            return MergeWebSearchItemsByUrl(merged, hardFilteredFeed, cappedTargetCount);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return merged;
        }
    }

    private static string BuildLowCostBackfillQuery(
        string query,
        string sourceFocus,
        string sourceDomain
    )
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        var normalizedFocus = (sourceFocus ?? string.Empty).Trim();
        var normalizedDomain = SearchQueryPolicy.NormalizeSourceDomainHint(sourceDomain);
        if (normalizedDomain.Length > 0)
        {
            if (normalizedFocus.Length > 0)
            {
                return $"site:{normalizedDomain} {normalizedFocus} latest headlines";
            }

            if (normalizedQuery.Length > 0)
            {
                return $"site:{normalizedDomain} {normalizedQuery}";
            }

            return $"site:{normalizedDomain} latest headlines";
        }

        if (normalizedFocus.Length > 0)
        {
            return $"{normalizedFocus} latest headlines";
        }

        return normalizedQuery;
    }

    private static bool ShouldRunArticleClassification(string query, string sourceFocus, int targetCount)
    {
        if (targetCount <= 0)
        {
            return false;
        }

        var normalizedQuery = (query ?? string.Empty).Trim();
        if (SearchQueryPolicy.LooksLikeListOutputRequest(normalizedQuery))
        {
            return true;
        }

        var normalizedFocus = (sourceFocus ?? string.Empty).Trim();
        return normalizedFocus.Length >= 2 && targetCount >= 3;
    }

    private async Task<IReadOnlyList<WebSearchResultItem>> FilterArticleCandidatesAsync(
        string query,
        string sourceFocus,
        string sourceDomain,
        IReadOnlyList<WebSearchResultItem> intentAligned,
        IReadOnlyList<WebSearchResultItem> candidatePool,
        int targetCount,
        CancellationToken cancellationToken
    )
    {
        var cappedTargetCount = Math.Clamp(targetCount, 1, 10);
        var poolLimit = Math.Clamp(cappedTargetCount * 3, cappedTargetCount, 30);
        var mergedPool = MergeWebSearchItemsByUrl(intentAligned, candidatePool, poolLimit);
        var hardFiltered = mergedPool
            .Where(item => !IsHardNonArticleCandidate(item))
            .ToArray();
        if (hardFiltered.Length == 0)
        {
            return intentAligned.Take(cappedTargetCount).ToArray();
        }

        if (hardFiltered.Length <= cappedTargetCount)
        {
            return hardFiltered;
        }

        var (provider, model) = ResolveArticleClassifierProviderModel();
        if (provider.Length == 0)
        {
            return BuildLikelyArticleFallback(hardFiltered, cappedTargetCount);
        }

        var candidateLimit = Math.Min(hardFiltered.Length, Math.Clamp(cappedTargetCount * 2, 10, 24));
        var candidates = hardFiltered.Take(candidateLimit).ToArray();
        var lines = new List<string>(candidates.Length);
        for (var i = 0; i < candidates.Length; i++)
        {
            var item = candidates[i];
            var sourceLabel = ResolveSourceLabel(item.Url, item.Title);
            var host = ExtractHostToken(item.Url);
            var title = TrimForForcedContext(item.Title, 120);
            var desc = TrimForForcedContext(item.Description, 120);
            var urlPath = TryGetUrlPathToken(item.Url);
            lines.Add($"{i + 1}. source={sourceLabel}; host={host}; path={urlPath}; title={title}; desc={desc}");
        }

        var focus = (sourceFocus ?? string.Empty).Trim();
        var domain = SearchQueryPolicy.NormalizeSourceDomainHint(sourceDomain);
        if (domain.Length == 0)
        {
            domain = ResolveSourceDomainFromQueryOrFocus(query, focus);
        }

        var prompt = $"""
                      아래 후보 중에서 "기사성 뉴스 문서"만 선택하세요.
                      제외 규칙:
                      - 약관/개인정보/쿠키/문의/소개/구독/계정/로그인/사이트맵
                      - 카테고리 허브/인덱스/메인 랜딩/프로그램 소개 페이지
                      - 팟캐스트 채널, 비디오 모아보기, 일반 안내 페이지

                      우선 규칙:
                      - 사용자 요청 맥락({query})과 직접 관련된 최신 뉴스 문서를 우선
                      - sourceFocus={focus}, sourceDomain={domain} 이 주어졌다면 해당 원출처 기사 우선
                      - 최대 {cappedTargetCount}개

                      출력 규칙:
                      - 설명 없이 JSON 한 줄만 출력
                      - 스키마: selected 배열만 포함한 JSON 객체

                      후보:
                      {string.Join("\n", lines)}
                      """;
        var judged = await GenerateByProviderSafeAsync(
            provider,
            model,
            prompt,
            cancellationToken,
            maxOutputTokens: 96
        ).ConfigureAwait(false);
        if (!TryParseSelectedCandidateIndexes(judged.Text, candidates.Length, out var selectedIndexes))
        {
            return BuildLikelyArticleFallback(hardFiltered, cappedTargetCount);
        }

        var selected = new List<WebSearchResultItem>(cappedTargetCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedIndex in selectedIndexes)
        {
            if (selectedIndex < 1 || selectedIndex > candidates.Length)
            {
                continue;
            }

            var item = candidates[selectedIndex - 1];
            var key = (item.Url ?? string.Empty).Trim();
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            selected.Add(item);
            if (selected.Count >= cappedTargetCount)
            {
                break;
            }
        }

        if (selected.Count < cappedTargetCount)
        {
            foreach (var item in candidates)
            {
                if (selected.Count >= cappedTargetCount)
                {
                    break;
                }

                var key = (item.Url ?? string.Empty).Trim();
                if (key.Length == 0 || seen.Contains(key))
                {
                    continue;
                }

                if (!IsLikelyArticleCandidate(item))
                {
                    continue;
                }

                seen.Add(key);
                selected.Add(item);
            }
        }

        if (selected.Count < cappedTargetCount)
        {
            foreach (var item in hardFiltered)
            {
                if (selected.Count >= cappedTargetCount)
                {
                    break;
                }

                var key = (item.Url ?? string.Empty).Trim();
                if (key.Length == 0 || seen.Contains(key))
                {
                    continue;
                }

                seen.Add(key);
                selected.Add(item);
            }
        }

        return selected.Count == 0
            ? BuildLikelyArticleFallback(hardFiltered, cappedTargetCount)
            : selected;
    }

    private (string Provider, string? Model) ResolveArticleClassifierProviderModel()
    {
        if (_llmRouter.HasGeminiApiKey())
        {
            return ("gemini", ResolveSearchLlmModel());
        }

        return (string.Empty, null);
    }

    private static IReadOnlyList<WebSearchResultItem> BuildLikelyArticleFallback(
        IReadOnlyList<WebSearchResultItem> candidates,
        int targetCount
    )
    {
        if (candidates == null || candidates.Count == 0 || targetCount <= 0)
        {
            return Array.Empty<WebSearchResultItem>();
        }

        var cappedTargetCount = Math.Clamp(targetCount, 1, 10);
        var output = new List<WebSearchResultItem>(cappedTargetCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in candidates)
        {
            if (output.Count >= cappedTargetCount)
            {
                break;
            }

            var key = (item.Url ?? string.Empty).Trim();
            if (key.Length == 0 || seen.Contains(key))
            {
                continue;
            }

            if (!IsLikelyArticleCandidate(item))
            {
                continue;
            }

            seen.Add(key);
            output.Add(item);
        }

        if (output.Count < cappedTargetCount)
        {
            foreach (var item in candidates)
            {
                if (output.Count >= cappedTargetCount)
                {
                    break;
                }

                var key = (item.Url ?? string.Empty).Trim();
                if (key.Length == 0 || seen.Contains(key))
                {
                    continue;
                }

                seen.Add(key);
                output.Add(item);
            }
        }

        return output;
    }

    private static bool IsHardNonArticleCandidate(WebSearchResultItem item)
    {
        var title = (item.Title ?? string.Empty).Trim().ToLowerInvariant();
        var description = (item.Description ?? string.Empty).Trim().ToLowerInvariant();
        var path = TryGetUrlPathToken(item.Url);
        var looksLikeGoogleNewsArticlePath = path.Contains("/rss/articles/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/articles/", StringComparison.OrdinalIgnoreCase);
        if (Regex.IsMatch(
                title,
                @"\b(terms(\s+and\s+conditions)?|privacy\s+policy|cookie\s+policy|cookie\s+settings|contact\s+us|about\s+us|accessibility|help\s+center|faq)\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(
                path,
                @"/(terms|privacy|cookies?|cookie-policy|contact|about|accessibility|help|faq|account|login|subscribe|newsletters?|sitemap(\.xml)?|feed|rss)(/|$)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            && !looksLikeGoogleNewsArticlePath)
        {
            return true;
        }

        if (Regex.IsMatch(
                path,
                @"/(podcast|podcasts|audio|video|tv-shows?|shows?)(/|$)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            && !Regex.IsMatch(path, @"/20\d{2}/\d{2}/\d{2}/", RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (description.Contains("핵심 내용 확인이 필요합니다.", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(
                path,
                @"/(terms|privacy|cookie|contact|about|sitemap|rss|feed)(/|$)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            && !looksLikeGoogleNewsArticlePath)
        {
            return true;
        }

        return false;
    }

    private static bool IsLikelyArticleCandidate(WebSearchResultItem item)
    {
        if (IsHardNonArticleCandidate(item))
        {
            return false;
        }

        var title = (item.Title ?? string.Empty).Trim();
        var path = TryGetUrlPathToken(item.Url);
        if (Regex.IsMatch(path, @"/20\d{2}/\d{2}/\d{2}/", RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (Regex.IsMatch(path, @"/\d{4}/\d{2}/\d{2}/", RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (title.Length >= 20
            && Regex.IsMatch(path, @"[a-z0-9]+(?:-[a-z0-9]+){2,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            return true;
        }

        var description = (item.Description ?? string.Empty).Trim();
        return description.Length >= 60
               && !description.Contains("핵심 내용 확인이 필요합니다.", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetUrlPathToken(string? url)
    {
        if (!TryNormalizeDisplaySourceUrl(url, out var normalized)
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var path = (uri.AbsolutePath ?? string.Empty).Trim().ToLowerInvariant();
        return path.Length == 0 ? "/" : path;
    }

    private static bool ShouldRecollectForSourceFocus(
        string sourceFocus,
        int targetCount,
        IReadOnlyList<WebSearchResultItem> results
    )
    {
        if (targetCount <= 1 || results == null || results.Count == 0)
        {
            return false;
        }

        var focus = (sourceFocus ?? string.Empty).Trim();
        if (focus.Length < 2)
        {
            return false;
        }

        return CountProbableSourceMatches(results, focus) < targetCount;
    }

    private async Task<IReadOnlyList<WebSearchResultItem>> SelectWebResultsByIntentWithLlmAsync(
        string query,
        string sourceFocus,
        IReadOnlyList<WebSearchResultItem> results,
        int targetCount,
        CancellationToken cancellationToken
    )
    {
        if (results == null || results.Count == 0 || targetCount <= 0)
        {
            return Array.Empty<WebSearchResultItem>();
        }

        var cappedTargetCount = Math.Clamp(targetCount, 1, 10);
        var focus = (sourceFocus ?? string.Empty).Trim();
        if (focus.Length < 2)
        {
            return results.Take(cappedTargetCount).ToArray();
        }

        var candidateLimit = Math.Min(results.Count, Math.Clamp(cappedTargetCount * 2, 10, 24));
        var candidates = results.Take(candidateLimit).ToArray();
        var provider = _llmRouter.HasGeminiApiKey() ? "gemini" : string.Empty;
        if (provider.Length == 0)
        {
            return BuildSourcePrioritizedFallback(candidates, focus, cappedTargetCount);
        }

        var model = ResolveSearchLlmModel();
        var lines = new List<string>(candidates.Length);
        for (var i = 0; i < candidates.Length; i++)
        {
            var item = candidates[i];
            var sourceLabel = ResolveSourceLabel(item.Url, item.Title);
            var host = ExtractHostToken(item.Url);
            var title = TrimForForcedContext(item.Title, 120);
            var desc = TrimForForcedContext(item.Description, 140);
            lines.Add($"{i + 1}. source={sourceLabel}; host={host}; title={title}; desc={desc}");
        }

        var prompt = $"""
                      사용자 뉴스 요청과 검색 후보를 보고, 요청 맥락에 직접 부합하는 항목 번호만 고르세요.
                      핵심 규칙:
                      - 특정 매체/기관/브랜드({focus})가 요구되면 그 매체의 원출처 기사만 선택하세요.
                      - 다른 매체가 {focus}를 언급한 기사/재인용/요약은 제외하세요.
                      - 최대 {cappedTargetCount}개, 우선순위 순서로 고르세요.
                      - 설명 문장 없이 JSON 한 줄만 출력하세요.
                      - 스키마: selected 배열만 포함한 JSON 객체

                      사용자 요청:
                      {query}

                      후보:
                      {string.Join("\n", lines)}
                      """;
        var selection = await GenerateByProviderSafeAsync(
            provider,
            model,
            prompt,
            cancellationToken,
            maxOutputTokens: 96
        ).ConfigureAwait(false);
        if (!TryParseSelectedCandidateIndexes(selection.Text, candidates.Length, out var selectedIndexes))
        {
            return BuildSourcePrioritizedFallback(candidates, focus, cappedTargetCount);
        }

        var chosen = new List<WebSearchResultItem>(cappedTargetCount);
        var chosenUrlSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in selectedIndexes)
        {
            if (index < 1 || index > candidates.Length)
            {
                continue;
            }

            var item = candidates[index - 1];
            var key = (item.Url ?? string.Empty).Trim();
            if (key.Length == 0 || !chosenUrlSet.Add(key))
            {
                continue;
            }

            chosen.Add(item);
            if (chosen.Count >= cappedTargetCount)
            {
                break;
            }
        }

        if (chosen.Count < cappedTargetCount)
        {
            foreach (var item in candidates)
            {
                if (chosen.Count >= cappedTargetCount)
                {
                    break;
                }

                var key = (item.Url ?? string.Empty).Trim();
                if (key.Length == 0 || chosenUrlSet.Contains(key))
                {
                    continue;
                }

                if (!IsProbableSourceMatch(item, focus))
                {
                    continue;
                }

                chosenUrlSet.Add(key);
                chosen.Add(item);
            }
        }

        if (chosen.Count < cappedTargetCount)
        {
            foreach (var item in candidates)
            {
                if (chosen.Count >= cappedTargetCount)
                {
                    break;
                }

                var key = (item.Url ?? string.Empty).Trim();
                if (key.Length == 0 || chosenUrlSet.Contains(key))
                {
                    continue;
                }

                chosenUrlSet.Add(key);
                chosen.Add(item);
            }
        }

        return chosen.Count == 0
            ? BuildSourcePrioritizedFallback(candidates, focus, cappedTargetCount)
            : chosen;
    }

    private static IReadOnlyList<WebSearchResultItem> BuildSourcePrioritizedFallback(
        IReadOnlyList<WebSearchResultItem> candidates,
        string sourceFocus,
        int targetCount
    )
    {
        if (candidates == null || candidates.Count == 0 || targetCount <= 0)
        {
            return Array.Empty<WebSearchResultItem>();
        }

        var cappedTargetCount = Math.Clamp(targetCount, 1, 10);
        var output = new List<WebSearchResultItem>(cappedTargetCount);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in candidates)
        {
            if (output.Count >= cappedTargetCount)
            {
                break;
            }

            var key = (item.Url ?? string.Empty).Trim();
            if (key.Length == 0 || seenUrls.Contains(key))
            {
                continue;
            }

            if (!IsProbableSourceMatch(item, sourceFocus))
            {
                continue;
            }

            seenUrls.Add(key);
            output.Add(item);
        }

        if (output.Count < cappedTargetCount)
        {
            foreach (var item in candidates)
            {
                if (output.Count >= cappedTargetCount)
                {
                    break;
                }

                var key = (item.Url ?? string.Empty).Trim();
                if (key.Length == 0 || seenUrls.Contains(key))
                {
                    continue;
                }

                seenUrls.Add(key);
                output.Add(item);
            }
        }

        return output;
    }

    private static int CountProbableSourceMatches(
        IReadOnlyList<WebSearchResultItem> results,
        string sourceFocus
    )
    {
        if (results == null || results.Count == 0)
        {
            return 0;
        }

        return results.Count(item => IsProbableSourceMatch(item, sourceFocus));
    }

    private static bool IsProbableSourceMatch(WebSearchResultItem item, string sourceFocus)
    {
        var focusRaw = (sourceFocus ?? string.Empty).Trim().ToLowerInvariant();
        var focusKey = NormalizeSourceMatchToken(focusRaw);
        if (focusKey.Length < 2)
        {
            return true;
        }

        var sourceLabel = ResolveSourceLabel(item.Url, item.Title);
        var sourceLabelRaw = (sourceLabel ?? string.Empty).Trim().ToLowerInvariant();
        var hostRaw = ExtractHostToken(item.Url);
        if (IsShortAsciiSourceToken(focusRaw))
        {
            if (ContainsExactAsciiToken(sourceLabelRaw, focusRaw)
                || ContainsExactAsciiToken(hostRaw, focusRaw))
            {
                return true;
            }
        }

        var labelKey = NormalizeSourceMatchToken(sourceLabelRaw);
        if (labelKey.Contains(focusKey, StringComparison.Ordinal))
        {
            return true;
        }

        var hostKey = NormalizeSourceMatchToken(hostRaw);
        return hostKey.Contains(focusKey, StringComparison.Ordinal);
    }

    private static string NormalizeSourceMatchToken(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return Regex.Replace(normalized, @"[^a-z0-9가-힣]", string.Empty);
    }

    private static bool IsShortAsciiSourceToken(string token)
    {
        return Regex.IsMatch(
            (token ?? string.Empty).Trim().ToLowerInvariant(),
            @"^[a-z0-9]{2,12}$",
            RegexOptions.CultureInvariant
        );
    }

    private static bool ContainsExactAsciiToken(string text, string token)
    {
        var normalizedText = (text ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedToken = (token ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedText.Length == 0 || normalizedToken.Length == 0)
        {
            return false;
        }

        foreach (var part in Regex.Split(normalizedText, @"[^a-z0-9]+", RegexOptions.CultureInvariant))
        {
            if (part.Length == 0)
            {
                continue;
            }

            if (part.Equals(normalizedToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractHostToken(string url)
    {
        if (!TryNormalizeDisplaySourceUrl(url, out var normalizedUrl)
            || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        return host;
    }

    private static bool TryParseSelectedCandidateIndexes(
        string? rawText,
        int maxIndex,
        out IReadOnlyList<int> indexes
    )
    {
        indexes = Array.Empty<int>();
        if (maxIndex <= 0)
        {
            return false;
        }

        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var parsedIndexes = new List<int>();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var json = text[start..(end + 1)];
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && TryGetPropertyIgnoreCase(doc.RootElement, "selected", out var selectedElement)
                    && selectedElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in selectedElement.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Number
                            && element.TryGetInt32(out var number)
                            && number >= 1
                            && number <= maxIndex)
                        {
                            parsedIndexes.Add(number);
                            continue;
                        }

                        if (element.ValueKind == JsonValueKind.String
                            && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                            && parsed >= 1
                            && parsed <= maxIndex)
                        {
                            parsedIndexes.Add(parsed);
                        }
                    }
                }
            }
            catch
            {
                // ignore parse error and continue with regex fallback
            }
        }

        if (parsedIndexes.Count == 0)
        {
            foreach (Match match in Regex.Matches(text, @"\b(?<idx>\d{1,2})\b", RegexOptions.CultureInvariant))
            {
                if (!match.Success
                    || !int.TryParse(match.Groups["idx"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    || parsed < 1
                    || parsed > maxIndex)
                {
                    continue;
                }

                parsedIndexes.Add(parsed);
            }
        }

        var distinct = parsedIndexes
            .Distinct()
            .ToArray();
        if (distinct.Length == 0)
        {
            return false;
        }

        indexes = distinct;
        return true;
    }
}
