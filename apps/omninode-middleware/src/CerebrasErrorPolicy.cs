using System.Text.Json;

namespace OmniNode.Middleware;

internal static class CerebrasErrorPolicy
{
    private const string ZaiGlmModel = "zai-glm-4.7";
    private const string Qwen3Model = "qwen-3-235b-a22b-instruct-2507";

    public static bool IsModelNotFound(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (TryGetPropertyCaseInsensitive(doc.RootElement, "code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String
                && codeElement.GetString()?.Trim().Equals("model_not_found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        catch
        {
        }

        return responseBody.IndexOf("model_not_found", StringComparison.OrdinalIgnoreCase) >= 0
               || responseBody.IndexOf("does not exist or you do not have access", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string BuildHttpFailureText(System.Net.HttpStatusCode statusCode, string model)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            if (IsPreviewModel(normalizedModel))
            {
                return $"Cerebras 요청 실패: 429 (rate limit). 현재 {normalizedModel} (preview)는 수요가 높아 free-tier rate limit이 일시적으로 줄어든 상태입니다. 오늘 처음 써도 걸릴 수 있으니 잠시 후 다시 시도하거나 `gpt-oss-120b`로 바꿔 보세요.";
            }

            return "Cerebras 요청 실패: 429 (rate limit). 짧은 시간에 허용된 요청 수를 넘었거나 현재 free-tier 제한에 걸린 상태입니다. 잠시 후 다시 시도해 주세요.";
        }

        if (statusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            if (IsPreviewModel(normalizedModel))
            {
                return $"Cerebras 요청 실패: 503 (service unavailable). 현재 {normalizedModel} (preview) 쪽 서버가 일시적으로 과부하이거나 불안정한 상태일 수 있습니다. 잠시 후 다시 시도하거나 `gpt-oss-120b`로 바꿔 보세요.";
            }

            return "Cerebras 요청 실패: 503 (service unavailable). Cerebras 서버가 일시적으로 과부하이거나 불안정한 상태입니다. 잠시 후 다시 시도해 주세요.";
        }

        return $"Cerebras 요청 실패: {(int)statusCode}";
    }

    private static bool IsPreviewModel(string normalizedModel)
    {
        return string.Equals(normalizedModel, ZaiGlmModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedModel, Qwen3Model, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }
}
