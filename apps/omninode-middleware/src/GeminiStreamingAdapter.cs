using System.Net;
using System.Text;

namespace OmniNode.Middleware;

internal interface IGeminiStreamingAdapter
{
    Task<GeminiStreamingAdapterResult> StreamAsync(
        GeminiStreamingAdapterRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed record GeminiStreamingAdapterRequest(
    string Endpoint,
    string ApiKey,
    string Body,
    CancellationToken RequestCancellationToken,
    CancellationToken FailureBodyCancellationToken,
    Func<CancellationToken> ReadLineCancellationToken,
    Action<string> OnPayload
);

internal sealed record GeminiStreamingAdapterResult(
    bool IsSuccess,
    HttpStatusCode StatusCode,
    string FailureBody
);

internal sealed class GeminiStreamingAdapter : IGeminiStreamingAdapter
{
    private readonly HttpClient _httpClient;

    public GeminiStreamingAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GeminiStreamingAdapterResult> StreamAsync(
        GeminiStreamingAdapterRequest request,
        CancellationToken cancellationToken
    )
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.Endpoint);
        httpRequest.Headers.Add("x-goog-api-key", request.ApiKey);
        httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            request.RequestCancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            var failureBody = await response.Content.ReadAsStringAsync(request.FailureBodyCancellationToken);
            return new GeminiStreamingAdapterResult(false, response.StatusCode, failureBody);
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(request.RequestCancellationToken);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var eventBuilder = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(request.ReadLineCancellationToken());
            if (line == null)
            {
                break;
            }

            if (line.Length == 0)
            {
                ConsumeEvent(eventBuilder.ToString());
                eventBuilder.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var payloadLine = line[5..].TrimStart();
                if (payloadLine.Length > 0)
                {
                    if (eventBuilder.Length > 0)
                    {
                        eventBuilder.Append('\n');
                    }

                    eventBuilder.Append(payloadLine);
                }
            }
        }

        if (eventBuilder.Length > 0)
        {
            ConsumeEvent(eventBuilder.ToString());
        }

        return new GeminiStreamingAdapterResult(true, response.StatusCode, string.Empty);

        void ConsumeEvent(string eventPayload)
        {
            var trimmedPayload = (eventPayload ?? string.Empty).Trim();
            if (trimmedPayload.Length == 0 || trimmedPayload.Equals("[DONE]", StringComparison.Ordinal))
            {
                return;
            }

            request.OnPayload(trimmedPayload);
        }
    }
}
