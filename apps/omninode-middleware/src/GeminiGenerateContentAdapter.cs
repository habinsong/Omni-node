using System.Net;
using System.Text;

namespace OmniNode.Middleware;

internal interface IGeminiGenerateContentAdapter
{
    Task<GeminiGenerateContentResult> SendAsync(
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed record GeminiGenerateContentRequest(
    string Endpoint,
    string ApiKey,
    string Body
);

internal sealed record GeminiGenerateContentResult(
    bool IsSuccess,
    HttpStatusCode StatusCode,
    string ResponseBody
);

internal sealed class GeminiGenerateContentAdapter : IGeminiGenerateContentAdapter
{
    private readonly HttpClient _httpClient;

    public GeminiGenerateContentAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GeminiGenerateContentResult> SendAsync(
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    )
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.Endpoint);
        httpRequest.Headers.Add("x-goog-api-key", request.ApiKey);
        httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return new GeminiGenerateContentResult(response.IsSuccessStatusCode, response.StatusCode, responseBody);
    }
}
