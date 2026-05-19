using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace OmniNode.Middleware;

internal interface IProviderChatAdapter
{
    Task<ProviderChatAdapterResult> SendAsync(
        ProviderChatAdapterRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed record ProviderChatAdapterRequest(
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
    Action<HttpResponseHeaders>? OnResponseHeaders = null
);

internal sealed record ProviderChatAdapterResult(
    bool IsSuccess,
    HttpStatusCode StatusCode,
    string ResponseBody,
    string FailureMessage,
    HttpResponseHeaders ResponseHeaders
);

internal sealed class OpenAiCompatibleChatAdapter : IProviderChatAdapter
{
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleChatAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProviderChatAdapterResult> SendAsync(
        ProviderChatAdapterRequest request,
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
            stream: false
        );

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.Endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey);
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        request.OnResponseHeaders?.Invoke(response.Headers);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted && request.AcceptedResponseResolver != null)
        {
            responseBody = await request.AcceptedResponseResolver(responseBody, cancellationToken);
            return new ProviderChatAdapterResult(true, HttpStatusCode.OK, responseBody, string.Empty, response.Headers);
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[{request.Provider}] chat failed ({(int)response.StatusCode}): {responseBody}");
            return new ProviderChatAdapterResult(
                false,
                response.StatusCode,
                responseBody,
                OpenAiCompatibleProtocol.BuildFailureMessage(request.Provider, response.StatusCode, responseBody),
                response.Headers
            );
        }

        return new ProviderChatAdapterResult(true, response.StatusCode, responseBody, string.Empty, response.Headers);
    }
}
