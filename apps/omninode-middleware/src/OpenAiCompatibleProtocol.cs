using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

internal static class OpenAiCompatibleProtocol
{
    public static string BuildChatBody(
        string model,
        string systemPrompt,
        string userInput,
        List<(string Role, string Content)>? multiTurn,
        string maxTokensProperty,
        int maxOutputTokens,
        bool stream
    )
    {
        string messagesJson;
        if (multiTurn != null && multiTurn.Count > 1 && multiTurn[0].Role != "user")
        {
            var mb = new StringBuilder();
            mb.Append($"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}}");
            foreach (var (role, msgContent) in multiTurn)
            {
                var apiRole = role == "assistant" ? "assistant" : "user";
                mb.Append($",{{\"role\":\"{apiRole}\",\"content\":\"{EscapeJson(msgContent)}\"}}");
            }
            messagesJson = mb.ToString();
        }
        else
        {
            messagesJson = $"{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}},"
                + $"{{\"role\":\"user\",\"content\":\"{EscapeJson(userInput)}\"}}";
        }

        return "{"
            + $"\"model\":\"{EscapeJson(model)}\","
            + "\"temperature\":0.3,"
            + $"\"stream\":{(stream ? "true" : "false")},"
            + $"\"{EscapeJson(maxTokensProperty)}\":{maxOutputTokens},"
            + "\"messages\":["
            + messagesJson
            + "]"
            + "}";
    }

    public static string BuildFailureMessage(
        string provider,
        System.Net.HttpStatusCode statusCode,
        string failureBody
    )
    {
        var name = DisplayName(provider);
        var statusInt = (int)statusCode;
        var lowered = (failureBody ?? string.Empty).ToLowerInvariant();
        var looksLikeQuota = lowered.Contains("quota")
                             || lowered.Contains("rate limit")
                             || lowered.Contains("rate_limit")
                             || lowered.Contains("too many requests")
                             || lowered.Contains("credits");

        if (string.Equals(provider, "nvidia", StringComparison.OrdinalIgnoreCase))
        {
            if (statusCode == System.Net.HttpStatusCode.TooManyRequests || looksLikeQuota)
            {
                return $"{name} 무료 할당량(또는 rate limit)에 도달했습니다 ({statusInt}). 잠시 후 다시 시도하거나 다른 provider(예: Cerebras·Groq·Gemini)로 바꿔 보세요.";
            }
            if (statusCode == System.Net.HttpStatusCode.Unauthorized || statusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return $"{name} 인증 실패 ({statusInt}). API 키를 확인해 주세요.";
            }
            if (statusCode == System.Net.HttpStatusCode.ServiceUnavailable || statusCode == System.Net.HttpStatusCode.BadGateway)
            {
                return $"{name} 서버가 일시적으로 불안정합니다 ({statusInt}). 잠시 후 다시 시도해 주세요.";
            }
        }

        if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            return $"{name} rate limit ({statusInt}). 잠시 후 다시 시도해 주세요.";
        }

        return $"{name} 요청 실패: {statusInt}";
    }

    public static string DisplayName(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "groq" => "Groq",
            "gemini" => "Gemini",
            "cerebras" => "Cerebras",
            "nvidia" => "NVIDIA NIM",
            _ => provider
        };
    }

    public static OpenAiCompatibleStreamChunk ExtractStreamChunk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return new OpenAiCompatibleStreamChunk(string.Empty, string.Empty);
            }

            var first = choices[0];
            var finishReason = first.TryGetProperty("finish_reason", out var finishReasonElement)
                ? finishReasonElement.GetString() ?? string.Empty
                : string.Empty;
            if (!first.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
            {
                return new OpenAiCompatibleStreamChunk(string.Empty, finishReason);
            }

            if (!delta.TryGetProperty("content", out var content))
            {
                return new OpenAiCompatibleStreamChunk(string.Empty, finishReason);
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return new OpenAiCompatibleStreamChunk(content.GetString() ?? string.Empty, finishReason);
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(part.GetString());
                    }
                    else if (part.ValueKind == JsonValueKind.Object
                             && part.TryGetProperty("text", out var textPart)
                             && textPart.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(textPart.GetString());
                    }
                }

                return new OpenAiCompatibleStreamChunk(builder.ToString(), finishReason);
            }
        }
        catch (JsonException)
        {
        }

        return new OpenAiCompatibleStreamChunk(string.Empty, string.Empty);
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\b", "\\b", StringComparison.Ordinal)
            .Replace("\f", "\\f", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
