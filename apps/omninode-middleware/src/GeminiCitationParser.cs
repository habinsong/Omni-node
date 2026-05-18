using System.Text.Json;

namespace OmniNode.Middleware;

internal static class GeminiCitationParser
{
    public static SearchCitationReference[] ExtractUrlContextCitations(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var first = candidates[0];
            if (!TryGetPropertyIgnoreCase(first, "urlContextMetadata", out var metadata)
                || metadata.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<SearchCitationReference>();
            }

            if (!TryGetPropertyIgnoreCase(metadata, "urlMetadata", out var urlMetadata)
                || urlMetadata.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var citations = new List<SearchCitationReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in urlMetadata.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = GetJsonString(item, "retrievedUrl", "retrieved_url");
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                {
                    continue;
                }

                var status = GetJsonString(item, "urlRetrievalStatus", "url_retrieval_status");
                var citationId = $"urlctx-{citations.Count + 1}";
                citations.Add(new SearchCitationReference(
                    citationId,
                    BuildCitationTitle(url),
                    url,
                    string.Empty,
                    string.IsNullOrWhiteSpace(status) ? "URL_CONTEXT" : status,
                    "url_context"
                ));
            }

            return citations.ToArray();
        }
        catch
        {
            return Array.Empty<SearchCitationReference>();
        }
    }

    public static SearchCitationReference[] ExtractGroundingCitations(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var first = candidates[0];
            if (!TryGetPropertyIgnoreCase(first, "groundingMetadata", out var metadata)
                || metadata.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<SearchCitationReference>();
            }

            if (!TryGetPropertyIgnoreCase(metadata, "groundingChunks", out var groundingChunks)
                || groundingChunks.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SearchCitationReference>();
            }

            var citations = new List<SearchCitationReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in groundingChunks.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetPropertyIgnoreCase(item, "web", out var webChunk)
                    || webChunk.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = GetJsonString(webChunk, "uri");
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                {
                    continue;
                }

                var title = GetJsonString(webChunk, "title");
                var citationId = $"gsearch-{citations.Count + 1}";
                citations.Add(new SearchCitationReference(
                    citationId,
                    string.IsNullOrWhiteSpace(title) ? BuildCitationTitle(url) : title,
                    url,
                    string.Empty,
                    "GOOGLE_SEARCH",
                    "google_search"
                ));
            }

            return citations.ToArray();
        }
        catch
        {
            return Array.Empty<SearchCitationReference>();
        }
    }

    public static string BuildDedupKey(SearchCitationReference citation)
    {
        var url = (citation.Url ?? string.Empty).Trim();
        if (url.Length > 0)
        {
            return url;
        }

        var title = (citation.Title ?? string.Empty).Trim();
        return title;
    }

    private static string BuildCitationTitle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var host = uri.Host?.Trim() ?? string.Empty;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        return string.IsNullOrWhiteSpace(host) ? url : host;
    }

    private static string GetJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyIgnoreCase(element, name, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }
}
