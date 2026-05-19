using System.Net.Http.Headers;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class CerebrasModelCatalog : IDisposable
{
    private const int ResolvedModelCacheTtlSeconds = 60;
    private readonly ProviderOptions _providers;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly object _cacheLock = new();
    private string? _resolvedModelCache;
    private DateTime _resolvedModelCachedAt = DateTime.MinValue;

    public CerebrasModelCatalog(ProviderOptions providers, RuntimeSettings runtimeSettings)
        : this(
            providers,
            runtimeSettings,
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(5, providers.CerebrasTimeoutSec))
            },
            ownsHttpClient: true
        )
    {
    }

    internal CerebrasModelCatalog(
        ProviderOptions providers,
        RuntimeSettings runtimeSettings,
        HttpClient httpClient)
        : this(providers, runtimeSettings, httpClient, ownsHttpClient: false)
    {
    }

    private CerebrasModelCatalog(
        ProviderOptions providers,
        RuntimeSettings runtimeSettings,
        HttpClient httpClient,
        bool ownsHttpClient)
    {
        _providers = providers;
        _runtimeSettings = runtimeSettings;
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<CerebrasModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var apiKey = _runtimeSettings.GetCerebrasApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Array.Empty<CerebrasModelInfo>();
        }

        try
        {
            var endpoint = $"{_providers.CerebrasBaseUrl.TrimEnd('/')}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[cerebras] models fetch failed ({(int)response.StatusCode}): {body}");
                return Array.Empty<CerebrasModelInfo>();
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<CerebrasModelInfo>();
            }

            var result = new List<CerebrasModelInfo>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;

                var ownedBy = item.TryGetProperty("owned_by", out var ownedEl) ? ownedEl.GetString() ?? string.Empty : string.Empty;
                long? created = null;
                if (item.TryGetProperty("created", out var createdEl) && createdEl.ValueKind == JsonValueKind.Number)
                {
                    created = createdEl.TryGetInt64(out var v) ? v : null;
                }

                result.Add(new CerebrasModelInfo
                {
                    Id = id,
                    OwnedBy = ownedBy,
                    CreatedUnixSeconds = created
                });
            }

            return result
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cerebras] models fetch error: {ex.Message}");
            return Array.Empty<CerebrasModelInfo>();
        }
    }

    public async Task<string?> ResolveFirstAvailableModelAsync(string apiKey, CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_resolvedModelCache != null
                && (DateTime.UtcNow - _resolvedModelCachedAt).TotalSeconds < ResolvedModelCacheTtlSeconds)
            {
                return _resolvedModelCache;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            var endpoint = $"{_providers.CerebrasBaseUrl.TrimEnd('/')}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[cerebras] catalog fetch failed ({(int)response.StatusCode}): {body}");
                return null;
            }

            var firstId = ExtractFirstModelId(body);
            if (!string.IsNullOrWhiteSpace(firstId))
            {
                lock (_cacheLock)
                {
                    _resolvedModelCache = firstId;
                    _resolvedModelCachedAt = DateTime.UtcNow;
                }

                Console.Error.WriteLine($"[cerebras] resolved available model from catalog: {firstId}");
            }

            return firstId;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cerebras] catalog fetch error: {ex.Message}");
            return null;
        }
    }

    public string? GetCachedResolvedModel()
    {
        lock (_cacheLock)
        {
            if (_resolvedModelCache != null
                && (DateTime.UtcNow - _resolvedModelCachedAt).TotalSeconds < ResolvedModelCacheTtlSeconds)
            {
                return _resolvedModelCache;
            }
        }

        return null;
    }

    internal static string? ExtractFirstModelId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!item.TryGetProperty("id", out var idEl))
                {
                    continue;
                }

                var id = idEl.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed class CerebrasModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string OwnedBy { get; set; } = string.Empty;
    public long? CreatedUnixSeconds { get; set; }
}
