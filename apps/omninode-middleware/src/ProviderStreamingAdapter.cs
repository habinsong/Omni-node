using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace OmniNode.Middleware;

internal interface IProviderStreamingChatAdapter
{
    Task<ProviderStreamingTurnResult> SendTurnAsync(
        ProviderStreamingAdapterRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed record ProviderStreamingAdapterRequest(
    string Provider,
    string Endpoint,
    string ApiKey,
    string Model,
    string SystemPrompt,
    string UserInput,
    List<(string Role, string Content)>? MultiTurn,
    string MaxTokensProperty,
    int MaxOutputTokens,
    Func<string, CancellationToken, Task<string>>? AcceptedResponseResolver = null,
    Action<HttpResponseHeaders>? OnResponseHeaders = null,
    Action<string>? OnRawPayload = null,
    Action<string>? OnDelta = null
);

internal sealed record ProviderStreamingTurnResult(
    bool IsSuccess,
    HttpStatusCode StatusCode,
    string FailureMessage,
    string FinishReason,
    string AcceptedResponseBody
);

internal sealed class OpenAiCompatibleStreamingChatAdapter : IProviderStreamingChatAdapter
{
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleStreamingChatAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProviderStreamingTurnResult> SendTurnAsync(
        ProviderStreamingAdapterRequest request,
        CancellationToken cancellationToken
    )
    {
        var body = OpenAiCompatibleProtocol.BuildChatBody(
            request.Model,
            request.SystemPrompt,
            request.UserInput,
            request.MultiTurn,
            request.MaxTokensProperty,
            request.MaxOutputTokens,
            stream: true
        );

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        request.OnResponseHeaders?.Invoke(response.Headers);

        if (response.StatusCode == HttpStatusCode.Accepted && request.AcceptedResponseResolver != null)
        {
            var acceptedBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var resolvedBody = await request.AcceptedResponseResolver(acceptedBody, cancellationToken);
            return new ProviderStreamingTurnResult(true, HttpStatusCode.OK, string.Empty, string.Empty, resolvedBody);
        }

        if (!response.IsSuccessStatusCode)
        {
            var failureBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.Error.WriteLine($"[{request.Provider}] chat stream failed ({(int)response.StatusCode}): {failureBody}");
            return new ProviderStreamingTurnResult(
                false,
                response.StatusCode,
                OpenAiCompatibleProtocol.BuildFailureMessage(request.Provider, response.StatusCode, failureBody),
                string.Empty,
                string.Empty
            );
        }

        var eventBuilder = new StringBuilder();
        var finishReason = string.Empty;
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            if (line.Length == 0)
            {
                ConsumeOpenAiStreamEvent(eventBuilder.ToString());
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
            ConsumeOpenAiStreamEvent(eventBuilder.ToString());
        }

        return new ProviderStreamingTurnResult(true, response.StatusCode, string.Empty, finishReason, string.Empty);

        void ConsumeOpenAiStreamEvent(string eventPayload)
        {
            var payload = (eventPayload ?? string.Empty).Trim();
            if (payload.Length == 0 || payload.Equals("[DONE]", StringComparison.Ordinal))
            {
                return;
            }

            request.OnRawPayload?.Invoke(payload);
            var chunk = OpenAiCompatibleProtocol.ExtractStreamChunk(payload);
            if (!string.IsNullOrWhiteSpace(chunk.FinishReason))
            {
                finishReason = chunk.FinishReason;
            }

            if (chunk.Content.Length > 0)
            {
                request.OnDelta?.Invoke(chunk.Content);
            }
        }
    }
}
