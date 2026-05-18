using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

internal readonly record struct ProviderChatChunk(string Content, string FinishReason);

internal readonly record struct ProviderTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    public static ProviderTokenUsage Empty => new(0, 0, 0);
}

internal static class ProviderResponseParser
{
    public static ProviderChatChunk ExtractOpenAiCompatibleChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return new ProviderChatChunk(string.Empty, string.Empty);
        }

        var first = choices[0];
        var finishReason = first.TryGetProperty("finish_reason", out var finishReasonElement)
            ? finishReasonElement.GetString() ?? string.Empty
            : string.Empty;

        if (!first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return new ProviderChatChunk(string.Empty, finishReason);
        }

        if (!message.TryGetProperty("content", out var content))
        {
            return new ProviderChatChunk(string.Empty, finishReason);
        }

        return new ProviderChatChunk(content.GetString() ?? string.Empty, finishReason);
    }

    public static ProviderChatChunk ExtractGeminiChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return new ProviderChatChunk(string.Empty, string.Empty);
        }

        var first = candidates[0];
        var finishReason = first.TryGetProperty("finishReason", out var finishElement)
            ? finishElement.GetString() ?? string.Empty
            : string.Empty;

        if (!first.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            return new ProviderChatChunk(string.Empty, finishReason);
        }

        if (!content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array
            || parts.GetArrayLength() == 0)
        {
            return new ProviderChatChunk(string.Empty, finishReason);
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                builder.AppendLine(textElement.GetString());
            }
        }

        return new ProviderChatChunk(builder.ToString().Trim(), finishReason);
    }

    public static string ExtractGeminiBlockReason(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("promptFeedback", out var promptFeedback)
            || promptFeedback.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!promptFeedback.TryGetProperty("blockReason", out var blockReasonElement))
        {
            return string.Empty;
        }

        return blockReasonElement.GetString() ?? string.Empty;
    }

    public static bool IsOpenAiCompatibleTruncated(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason))
        {
            return false;
        }

        return string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase)
               || string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase)
               || string.Equals(finishReason, "token_limit", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeminiTruncated(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason))
        {
            return false;
        }

        return finishReason.Contains("MAX_TOKENS", StringComparison.OrdinalIgnoreCase)
               || finishReason.Contains("LENGTH", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseOpenAiCompatibleUsage(string responseBody, out ProviderTokenUsage usage)
    {
        usage = ProviderTokenUsage.Empty;
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("usage", out var usageElement)
                || usageElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            usage = new ProviderTokenUsage(
                GetInt(usageElement, "prompt_tokens"),
                GetInt(usageElement, "completion_tokens"),
                GetInt(usageElement, "total_tokens")
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseGeminiUsage(string responseBody, out ProviderTokenUsage usage)
    {
        usage = ProviderTokenUsage.Empty;
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("usageMetadata", out var usageElement)
                || usageElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            usage = new ProviderTokenUsage(
                GetInt(usageElement, "promptTokenCount"),
                GetInt(usageElement, "candidatesTokenCount"),
                GetInt(usageElement, "totalTokenCount")
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// NVIDIA NIM 202 응답 body에서 status polling용 request id를 추출한다. `requestId`와 `id` 키를 대소문자 무시로 본다.
    /// </summary>
    public static string ExtractNvidiaRequestId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (TryGetPropertyCaseInsensitive(doc.RootElement, "requestId", out var requestId)
                && requestId.ValueKind == JsonValueKind.String)
            {
                return requestId.GetString() ?? string.Empty;
            }

            if (TryGetPropertyCaseInsensitive(doc.RootElement, "id", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString() ?? string.Empty;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    /// <summary>
    /// Speech-to-text(STT) 응답 JSON에서 `text` 필드를 꺼낸다. 파싱 실패 시 원본 문자열을 그대로 돌려준다.
    /// </summary>
    public static string ExtractSttText(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
            return json;
        }

        return json;
    }

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return 0;
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
