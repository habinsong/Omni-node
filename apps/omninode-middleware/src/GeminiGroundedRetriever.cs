using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed record GeminiGroundedResultItem(
    string Title,
    string Url,
    string Description,
    string? Published
);

public sealed record GeminiGroundedRetrieverResult(
    IReadOnlyList<GeminiGroundedResultItem> Results,
    bool Disabled,
    string? Error
);

public sealed class GeminiGroundedRetriever : ISearchRetriever
{
    private const string DefaultRetrieverModel = "gemini-3.1-flash-lite-preview";
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxOutputTokens = 8192;
    private static readonly Regex RawUrlRegex = new(
        @"https?://[^\s""'<>\\]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );
    private static readonly Regex SiteDomainRegex = new(
        @"site\s*:\s*(?<domain>[a-z0-9][a-z0-9\.-]*\.[a-z]{2,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly HashSet<string> RedirectQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url",
        "u",
        "target",
        "dest",
        "destination",
        "redirect",
        "r",
        "to"
    };
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    private readonly ProviderOptions _providers;
    private readonly ContextOptions _context;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly HttpClient _httpClient;
    private readonly int _timeoutSeconds;

    public GeminiGroundedRetriever(
        ProviderOptions providers,
        ContextOptions context,
        RuntimeSettings runtimeSettings,
        HttpClient? httpClient = null
    )
    {
        _providers = providers;
        _context = context;
        _runtimeSettings = runtimeSettings;
        _httpClient = httpClient ?? SharedHttpClient;
        _timeoutSeconds = ResolveTimeout(_context.GeminiWebTimeoutMs);
    }

    public async Task<GeminiGroundedRetrieverResult> RetrieveAsync(
        SearchRequest request,
        int maxResults,
        CancellationToken cancellationToken = default
    )
    {
        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return new GeminiGroundedRetrieverResult(Array.Empty<GeminiGroundedResultItem>(), false, "query required");
        }

        var geminiApiKey = (_runtimeSettings.GetGeminiApiKey() ?? string.Empty).Trim();
        if (geminiApiKey.Length == 0)
        {
            return new GeminiGroundedRetrieverResult(Array.Empty<GeminiGroundedResultItem>(), true, "gemini api key missing");
        }

        var normalizedMaxResults = NormalizeMaxResults(maxResults);
        var selectedModel = ResolveRetrieverModel(_providers.GeminiSearchModel);
        var endpoint = $"{_providers.GeminiBaseUrl.TrimEnd('/')}/models/{selectedModel}:generateContent";
        var prompt = BuildRetrieverPrompt(request, normalizedMaxResults);
        var requestJson = BuildRequestJson(prompt);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.TryAddWithoutValidation("x-goog-api-key", geminiApiKey);
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            using var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = ExtractErrorDetail(body);
                return new GeminiGroundedRetrieverResult(
                    Array.Empty<GeminiGroundedResultItem>(),
                    true,
                    $"gemini grounding http {(int)response.StatusCode}: {detail}"
                );
            }

            var mapped = MapResults(body, normalizedMaxResults);
            if (mapped.Count == 0)
            {
                return new GeminiGroundedRetrieverResult(Array.Empty<GeminiGroundedResultItem>(), true, "gemini grounding result empty");
            }

            return new GeminiGroundedRetrieverResult(mapped, false, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"[gemini] grounding timeout ({_timeoutSeconds}s, model={selectedModel}, maxResults={normalizedMaxResults})"
            );
            return new GeminiGroundedRetrieverResult(Array.Empty<GeminiGroundedResultItem>(), true, "gemini grounding timeout");
        }
        catch (Exception ex)
        {
            return new GeminiGroundedRetrieverResult(Array.Empty<GeminiGroundedResultItem>(), true, ex.Message);
        }
    }

    private static int ResolveTimeout(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            return DefaultTimeoutSeconds;
        }

        var timeoutSeconds = (int)Math.Ceiling(timeoutMs / 1000d);
        return Math.Clamp(timeoutSeconds, 5, 60);
    }

    private static string ResolveRetrieverModel(string? configuredModel)
    {
        var normalized = (configuredModel ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? DefaultRetrieverModel : normalized;
    }

    private static int NormalizeMaxResults(int maxResults)
    {
        if (maxResults <= 0)
        {
            return 10;
        }

        return Math.Clamp(maxResults, 1, 30);
    }

    private static string BuildRetrieverPrompt(SearchRequest request, int maxResults)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Collect grounded web search evidence for the user query using Google Search grounding.");
        builder.AppendLine("Return strict JSON only.");
        builder.AppendLine("JSON schema:");
        builder.AppendLine("{\"results\":[{\"title\":\"...\",\"url\":\"https://...\",\"snippet\":\"...\",\"publishedAt\":\"ISO-8601 or empty\"}]}");
        builder.AppendLine("Rules:");
        builder.AppendLine("- Keep only factual web documents.");
        builder.AppendLine("- Remove duplicate URLs.");
        builder.AppendLine("- Prefer recent and independent sources.");
        builder.AppendLine($"- Maximum results: {maxResults}.");
        var sourceDomains = ResolveSourceDomains(request.Query);
        if (sourceDomains.Count > 0)
        {
            builder.AppendLine($"- Restrict results to these domains only: {string.Join(", ", sourceDomains)}");
            builder.AppendLine("- If results are insufficient, search deeper within the same domains. Do not include other domains.");
        }
        builder.AppendLine($"- Query: {request.Query}");
        builder.AppendLine($"- User timezone: {request.UserTimezone}");
        builder.AppendLine($"- MaxAgeHours: {request.Constraints.MaxAgeHours}");
        builder.AppendLine($"- StrictTodayWindow: {(request.Constraints.StrictTodayWindow ? "true" : "false")}");
        builder.AppendLine($"- TimeSensitivity: {request.IntentProfile.TimeSensitivity}");
        return builder.ToString();
    }

    private static IReadOnlyList<string> ResolveSourceDomains(string query)
    {
        var normalized = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in SiteDomainRegex.Matches(normalized))
        {
            if (!match.Success)
            {
                continue;
            }

            var domain = (match.Groups["domain"].Value ?? string.Empty).Trim().ToLowerInvariant();
            if (domain.Length > 0)
            {
                domains.Add(domain);
            }
        }

        return domains
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildRequestJson(string prompt)
    {
        return "{"
            + "\"contents\":[{"
            + "\"role\":\"user\","
            + "\"parts\":["
            + $"{{\"text\":\"{EscapeJson(prompt)}\"}}"
            + "]"
            + "}],"
            + "\"tools\":[{\"google_search\":{}}],"
            + "\"generationConfig\":{"
            + "\"temperature\":0,"
            + $"\"maxOutputTokens\":{MaxOutputTokens},"
            + "\"responseMimeType\":\"application/json\""
            + "}"
            + "}";
    }

    private static IReadOnlyList<GeminiGroundedResultItem> MapResults(string body, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<GeminiGroundedResultItem>();
        }

        var mapped = new List<GeminiGroundedResultItem>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            JsonElement candidates = default;
            var hasCandidates = root.TryGetProperty("candidates", out candidates)
                && candidates.ValueKind == JsonValueKind.Array;

            // 1) grounding 메타데이터를 우선 사용
            if (hasCandidates)
            {
                foreach (var candidate in candidates.EnumerateArray())
                {
                    MapFromGroundingChunks(candidate, seenUrls, mapped, maxResults);
                    if (mapped.Count >= maxResults)
                    {
                        return mapped;
                    }
                }
            }

            MapFromTopLevelGroundingMetadata(root, seenUrls, mapped, maxResults);
            if (mapped.Count >= maxResults)
            {
                return mapped;
            }

            // 2) grounding 결과가 부족할 때만 모델 텍스트 JSON을 보조로 사용
            if (hasCandidates && mapped.Count < maxResults)
            {
                foreach (var candidate in candidates.EnumerateArray())
                {
                    var text = ExtractCandidateText(candidate);
                    if (TryMapFromStructuredJsonText(text, seenUrls, mapped, maxResults)
                        && mapped.Count >= maxResults)
                    {
                        return mapped;
                    }
                }
            }
        }
        catch
        {
        }

        if (mapped.Count == 0)
        {
            MapFromRawUrls(body, seenUrls, mapped, maxResults);
        }

        return mapped;
    }

    private static string ExtractCandidateText(JsonElement candidate)
    {
        if (!TryGetPropertyIgnoreCase(candidate, "content", out var content)
            || content.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!TryGetPropertyIgnoreCase(content, "parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var part in parts.EnumerateArray())
        {
            var text = ReadFirstString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static bool TryMapFromStructuredJsonText(
        string text,
        HashSet<string> seenUrls,
        List<GeminiGroundedResultItem> mapped,
        int maxResults
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().TrimStart();
        if (span.IsEmpty || (span[0] != '{' && span[0] != '['))
        {
            return false;
        }

        try
        {
            using var parsed = JsonDocument.Parse(text);
            var root = parsed.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                MapResultItems(root, seenUrls, mapped, maxResults);
                return true;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryGetPropertyIgnoreCase(root, "results", out var results)
                && results.ValueKind == JsonValueKind.Array)
            {
                MapResultItems(results, seenUrls, mapped, maxResults);
                return true;
            }

            if (TryGetPropertyIgnoreCase(root, "items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                MapResultItems(items, seenUrls, mapped, maxResults);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void MapResultItems(
        JsonElement items,
        HashSet<string> seenUrls,
        List<GeminiGroundedResultItem> mapped,
        int maxResults
    )
    {
        foreach (var item in items.EnumerateArray())
        {
            if (mapped.Count >= maxResults)
            {
                return;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var rawUrl = ReadFirstString(item, "url", "uri", "link");
            if (!TryNormalizeGroundedUrl(rawUrl, out var normalizedUrl) || !seenUrls.Add(normalizedUrl))
            {
                continue;
            }

            var title = ReadFirstString(item, "title", "headline");
            var snippet = ReadFirstString(item, "snippet", "summary", "description");
            var published = NormalizePublished(ReadFirstString(item, "publishedAt", "published", "published_date", "date"));

            mapped.Add(new GeminiGroundedResultItem(title, normalizedUrl, snippet, published));
        }
    }

    private static void MapFromGroundingChunks(
        JsonElement candidate,
        HashSet<string> seenUrls,
        List<GeminiGroundedResultItem> mapped,
        int maxResults
    )
    {
        if (!TryGetPropertyIgnoreCase(candidate, "groundingMetadata", out var groundingMetadata)
            || groundingMetadata.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryGetPropertyIgnoreCase(groundingMetadata, "groundingChunks", out var groundingChunks)
            || groundingChunks.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var chunk in groundingChunks.EnumerateArray())
        {
            if (mapped.Count >= maxResults)
            {
                return;
            }

            if (!TryGetPropertyIgnoreCase(chunk, "web", out var web)
                || web.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var rawUrl = ReadFirstString(web, "uri", "url", "link");
            if (!TryNormalizeGroundedUrl(rawUrl, out var normalizedUrl) || !seenUrls.Add(normalizedUrl))
            {
                continue;
            }

            var title = ReadFirstString(web, "title", "name");
            mapped.Add(new GeminiGroundedResultItem(title, normalizedUrl, string.Empty, null));
        }
    }

    private static void MapFromTopLevelGroundingMetadata(
        JsonElement root,
        HashSet<string> seenUrls,
        List<GeminiGroundedResultItem> mapped,
        int maxResults
    )
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryGetPropertyIgnoreCase(root, "groundingMetadata", out var groundingMetadata)
            || groundingMetadata.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryGetPropertyIgnoreCase(groundingMetadata, "groundingChunks", out var groundingChunks)
            || groundingChunks.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var chunk in groundingChunks.EnumerateArray())
        {
            if (mapped.Count >= maxResults)
            {
                return;
            }

            if (!TryGetPropertyIgnoreCase(chunk, "web", out var web)
                || web.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var rawUrl = ReadFirstString(web, "uri", "url", "link");
            if (!TryNormalizeGroundedUrl(rawUrl, out var normalizedUrl) || !seenUrls.Add(normalizedUrl))
            {
                continue;
            }

            var title = ReadFirstString(web, "title", "name");
            mapped.Add(new GeminiGroundedResultItem(title, normalizedUrl, string.Empty, null));
        }
    }

    private static void MapFromRawUrls(
        string body,
        HashSet<string> seenUrls,
        List<GeminiGroundedResultItem> mapped,
        int maxResults
    )
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var normalizedBody = body.Replace("\\/", "/", StringComparison.Ordinal);
        foreach (Match match in RawUrlRegex.Matches(normalizedBody))
        {
            if (mapped.Count >= maxResults)
            {
                return;
            }

            var url = (match.Value ?? string.Empty).Trim();
            if (url.Length == 0)
            {
                continue;
            }

            url = url.TrimEnd('.', ',', ';', ':', ')', ']', '"', '\'');
            if (!TryNormalizeGroundedUrl(url, out var normalizedUrl) || !seenUrls.Add(normalizedUrl))
            {
                continue;
            }

            var fallbackTitle = BuildFallbackTitleFromUrl(normalizedUrl);
            mapped.Add(new GeminiGroundedResultItem(
                fallbackTitle,
                normalizedUrl,
                string.Empty,
                null
            ));
        }
    }

    private static string BuildFallbackTitleFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return "웹 검색 항목";
        }

        var path = (uri.AbsolutePath ?? string.Empty).Trim('/');
        if (path.Length > 0)
        {
            var decodedPath = Uri.UnescapeDataString(path)
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Replace('/', ' ');
            decodedPath = Regex.Replace(decodedPath, @"\b\d{1,6}\b", " ");
            decodedPath = Regex.Replace(decodedPath, @"\s+", " ").Trim();
            if (decodedPath.Length >= 6)
            {
                return decodedPath.Length <= 120
                    ? decodedPath
                    : decodedPath[..120].TrimEnd() + "...";
            }
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }
        return host;
    }

    private static string ReadFirstString(JsonElement obj, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(obj, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = (value.GetString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            else if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return value.ToString();
            }
        }

        return string.Empty;
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

    private static bool IsHttpUrl(string value)
    {
        if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeGroundedUrl(string? rawUrl, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        var raw = (rawUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || !IsHttpUrl(raw))
        {
            return false;
        }

        if (uri.Host.Contains("vertexaisearch.cloud.google.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Contains("/grounding-api-redirect", StringComparison.OrdinalIgnoreCase))
        {
            if (TryExtractRedirectTarget(uri, out normalizedUrl))
            {
                return true;
            }

            normalizedUrl = uri.AbsoluteUri;
            return true;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
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

        var parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length == 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(kv[0]).Trim();
            if (!RedirectQueryKeys.Contains(key))
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

    private static string? NormalizePublished(string value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        return raw.Length <= 64 ? raw : raw[..64];
    }

    private static string ExtractErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "request failed";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (TryGetPropertyIgnoreCase(root, "error", out var error))
            {
                var message = ReadFirstString(error, "message", "detail", "status");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message.Length <= 280 ? message : message[..280];
                }
            }

            var fallback = ReadFirstString(root, "message", "detail", "error");
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback.Length <= 280 ? fallback : fallback[..280];
            }
        }
        catch
        {
        }

        var compact = body.Trim();
        return compact.Length <= 280 ? compact : compact[..280];
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static HttpClient CreateSharedHttpClient()
    {
        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}
