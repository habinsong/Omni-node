using System.Net.Http.Headers;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class GroqModelCatalog : IDisposable
{
    private static readonly Dictionary<string, GroqModelSpec> StaticSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["llama-3.1-8b-instant"] = new("Production", "560", "250K TPM / 1K RPM", "1K", "14.4K", "250K", "18M", "-", "-", "131072", "131072", "-", "$0.05", "$0.08"),
        ["llama-3.3-70b-versatile"] = new("Production", "280", "300K TPM / 1K RPM", "1K", "14.4K", "300K", "1M", "-", "-", "131072", "32768", "-", "$0.59", "$0.79"),
        ["openai/gpt-oss-120b"] = new("Production", "500", "250K TPM / 1K RPM", "1K", "14.4K", "250K", "1M", "-", "-", "131072", "65536", "-", "$0.15", "$0.60"),
        ["openai/gpt-oss-20b"] = new("Production", "1000", "250K TPM / 1K RPM", "1K", "14.4K", "250K", "1M", "-", "-", "131072", "65536", "-", "$0.075", "$0.30"),
        ["whisper-large-v3"] = new("Production", "-", "200K ASH / 300 RPM", "300", "2K", "-", "-", "200K", "2K", "-", "-", "100 MB", "$0.111 / hour", "-"),
        ["whisper-large-v3-turbo"] = new("Production", "-", "400K ASH / 400 RPM", "400", "2K", "-", "-", "400K", "7K", "-", "-", "-", "$0.04 / hour", "-"),
        ["groq/compound"] = new("Production", "450", "200K TPM / 200 RPM", "200", "1K", "200K", "200K", "-", "-", "131072", "8192", "-", "$0.59", "$0.79"),
        ["groq/compound-mini"] = new("Production", "450", "200K TPM / 200 RPM", "200", "3K", "200K", "200K", "-", "-", "131072", "8192", "-", "$0.30", "$0.50"),
        ["canopylabs/orpheus-arabic-saudi"] = new("Preview", "-", "50K TPM / 250 RPM", "250", "1K", "50K", "100K", "-", "-", "4000", "50000", "-", "$0.00", "$0.00"),
        ["canopylabs/orpheus-v1-english"] = new("Preview", "-", "50K TPM / 250 RPM", "250", "1K", "50K", "100K", "-", "-", "4000", "50000", "-", "$0.00", "$0.00"),
        ["meta-llama/llama-4-scout-17b-16e-instruct"] = new("Preview", "750", "300K TPM / 1K RPM", "1K", "14.4K", "300K", "1M", "-", "-", "131072", "8192", "20 MB", "$0.11", "$0.34"),
        ["meta-llama/llama-prompt-guard-2-22m"] = new("Preview", "-", "30K TPM / 100 RPM", "100", "500", "30K", "15K", "-", "-", "512", "512", "-", "$0.03", "$0.03"),
        ["meta-llama/llama-prompt-guard-2-86m"] = new("Preview", "-", "30K TPM / 100 RPM", "100", "500", "30K", "15K", "-", "-", "512", "512", "-", "$0.04", "$0.04"),
        ["openai/gpt-oss-safeguard-20b"] = new("Preview", "1000", "150K TPM / 1K RPM", "1K", "-", "150K", "-", "-", "-", "131072", "65536", "-", "$0.075", "$0.30"),
        ["qwen/qwen3-32b"] = new("Preview", "400", "300K TPM / 1K RPM", "1K", "14.4K", "300K", "1M", "-", "-", "131072", "40960", "-", "$0.29", "$0.59")
    };

    private readonly ProviderOptions _providers;
    private readonly ContextOptions _context;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly HttpClient _httpClient;

    public GroqModelCatalog(ProviderOptions providers, ContextOptions context, RuntimeSettings runtimeSettings)
    {
        _providers = providers;
        _context = context;
        _runtimeSettings = runtimeSettings;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, _context.LlmTimeoutSec))
        };
    }

    public async Task<IReadOnlyList<GroqModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StaticSpecs.Keys, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_runtimeSettings.GetGroqApiKey()))
        {
            var remoteIds = await FetchRemoteModelsAsync(cancellationToken);
            foreach (var modelId in remoteIds)
            {
                ids.Add(modelId);
            }
        }

        var result = new List<GroqModelInfo>();
        foreach (var modelId in ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!StaticSpecs.TryGetValue(modelId, out var spec))
            {
                spec = new GroqModelSpec("Unknown", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-");
            }

            result.Add(new GroqModelInfo
            {
                Id = modelId,
                Tier = spec.Tier,
                SpeedTokensPerSecond = spec.SpeedTokensPerSecond,
                RateLimit = spec.RateLimit,
                Rpm = spec.Rpm,
                Rpd = spec.Rpd,
                Tpm = spec.Tpm,
                Tpd = spec.Tpd,
                Ash = spec.Ash,
                Asd = spec.Asd,
                ContextWindow = spec.ContextWindow,
                MaxCompletionTokens = spec.MaxCompletionTokens,
                MaxFileSize = spec.MaxFileSize,
                PriceInputPerMillion = spec.PriceInputPerMillion,
                PriceOutputPerMillion = spec.PriceOutputPerMillion
            });
        }

        return result;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<IReadOnlyList<string>> FetchRemoteModelsAsync(CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var groqApiKey = _runtimeSettings.GetGroqApiKey();
        if (string.IsNullOrWhiteSpace(groqApiKey))
        {
            return ids;
        }

        try
        {
            var endpoint = $"{_providers.GroqBaseUrl.TrimEnd('/')}/models";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[groq] models fetch failed ({(int)response.StatusCode}): {body}");
                return ids;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return ids;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                var id = idElement.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[groq] models fetch error: {ex.Message}");
        }

        return ids;
    }
}

public sealed class GroqModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Tier { get; set; } = "Unknown";
    public string SpeedTokensPerSecond { get; set; } = "-";
    public string RateLimit { get; set; } = "-";
    public string Rpm { get; set; } = "-";
    public string Rpd { get; set; } = "-";
    public string Tpm { get; set; } = "-";
    public string Tpd { get; set; } = "-";
    public string Ash { get; set; } = "-";
    public string Asd { get; set; } = "-";
    public string ContextWindow { get; set; } = "-";
    public string MaxCompletionTokens { get; set; } = "-";
    public string MaxFileSize { get; set; } = "-";
    public string PriceInputPerMillion { get; set; } = "-";
    public string PriceOutputPerMillion { get; set; } = "-";
}

public sealed record GroqModelSpec(
    string Tier,
    string SpeedTokensPerSecond,
    string RateLimit,
    string Rpm,
    string Rpd,
    string Tpm,
    string Tpd,
    string Ash,
    string Asd,
    string ContextWindow,
    string MaxCompletionTokens,
    string MaxFileSize,
    string PriceInputPerMillion,
    string PriceOutputPerMillion
);
