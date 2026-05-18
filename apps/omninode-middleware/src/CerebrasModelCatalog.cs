using System.Net.Http.Headers;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class CerebrasModelCatalog : IDisposable
{
    private readonly ProviderOptions _providers;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly HttpClient _httpClient;

    public CerebrasModelCatalog(ProviderOptions providers, RuntimeSettings runtimeSettings)
    {
        _providers = providers;
        _runtimeSettings = runtimeSettings;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, _providers.CerebrasTimeoutSec))
        };
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public sealed class CerebrasModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string OwnedBy { get; set; } = string.Empty;
    public long? CreatedUnixSeconds { get; set; }
}
