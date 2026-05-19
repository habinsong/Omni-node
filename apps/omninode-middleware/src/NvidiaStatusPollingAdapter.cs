using System.Net.Http.Headers;

namespace OmniNode.Middleware;

internal sealed class NvidiaStatusPollingAdapter
{
    private readonly HttpClient _httpClient;

    public NvidiaStatusPollingAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> PollAsync(
        string apiKey,
        string acceptedBody,
        string baseUrl,
        int timeoutSec,
        CancellationToken cancellationToken)
    {
        var requestId = ProviderResponseParser.ExtractNvidiaRequestId(acceptedBody);
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new InvalidOperationException("NVIDIA NIM 202 응답에 requestId가 없습니다.");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(3, timeoutSec));
        var endpoint = $"{(baseUrl ?? string.Empty).TrimEnd('/')}/status/{Uri.EscapeDataString(requestId)}";
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(750, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            Console.Error.WriteLine($"[nvidia] status poll failed ({(int)response.StatusCode}): {body}");
            throw new InvalidOperationException($"NVIDIA NIM status polling failed: {(int)response.StatusCode}");
        }

        throw new TimeoutException("NVIDIA NIM status polling timed out.");
    }
}
