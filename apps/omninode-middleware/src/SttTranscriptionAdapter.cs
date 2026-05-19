using System.Net.Http.Headers;
using System.Text;

namespace OmniNode.Middleware;

internal sealed class SttTranscriptionAdapter
{
    private readonly HttpClient _httpClient;

    public SttTranscriptionAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> TranscribeAsync(
        InputAttachment attachment,
        string baseUrl,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (attachment == null || string.IsNullOrWhiteSpace(attachment.DataBase64))
        {
            return "음성 파일이 비어 있습니다.";
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(attachment.DataBase64);
        }
        catch
        {
            return "음성 파일을 읽을 수 없습니다.";
        }

        if (bytes.Length == 0)
        {
            return "음성 파일이 비어 있습니다.";
        }

        var endpoint = ResolveEndpoint(baseUrl);
        var fileName = string.IsNullOrWhiteSpace(attachment.Name) ? "telegram-audio.ogg" : attachment.Name.Trim();
        var mimeType = string.IsNullOrWhiteSpace(attachment.MimeType) ? "audio/ogg" : attachment.MimeType.Trim();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey ?? string.Empty);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent((model ?? string.Empty).Trim(), Encoding.UTF8), "model");

            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            form.Add(fileContent, "file", fileName);
            request.Content = form;

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[stt] failed ({(int)response.StatusCode}): {responseBody}");
                return $"음성 변환 실패: {(int)response.StatusCode}";
            }

            var text = ProviderResponseParser.ExtractSttText(responseBody).Trim();
            return string.IsNullOrWhiteSpace(text) ? "음성 변환 결과가 비어 있습니다." : text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "음성 변환 시간이 초과되었습니다.";
        }
        catch (Exception ex)
        {
            return $"음성 변환 오류: {ex.Message}";
        }
    }

    internal static string ResolveEndpoint(string baseUrl)
    {
        var normalized = (baseUrl ?? string.Empty).Trim();
        return normalized.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized.TrimEnd('/')}/audio/transcriptions";
    }
}
