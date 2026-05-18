using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class GroqPromptPolicy
{
    private static readonly Regex GroqMaxTokensLimitRegex = new(
        @"max_tokens`?\s*must be less than or equal to `?(?<limit>\d+)`?|maximum value for `max_tokens` is(?: less than the `context_window` for this model)?\s*`?(?<limit2>\d+)`?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static int ClampGroqMaxOutputTokens(int value)
    {
        return Math.Clamp(value, 256, 8192);
    }

    public static int ClampGroqMaxOutputTokensForModel(string model, int value)
    {
        var clamped = ClampGroqMaxOutputTokens(value);
        if (IsGroqCompoundModel(model))
        {
            return Math.Min(clamped, 1024);
        }

        return clamped;
    }

    public static bool IsGroqCompoundModel(string model)
    {
        var normalized = (model ?? string.Empty).Trim();
        return normalized.StartsWith("groq/compound", StringComparison.OrdinalIgnoreCase);
    }

    public static int ResolveGroqPromptBudgetChars(string model)
    {
        return IsGroqCompoundModel(model) ? 12000 : 22000;
    }

    public static string TruncatePromptForGroq(string prompt, int maxChars)
    {
        var normalized = (prompt ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        var safeMax = Math.Max(1200, maxChars);
        if (normalized.Length <= safeMax)
        {
            return normalized;
        }

        var headLen = (int)Math.Round(safeMax * 0.62);
        var tailLen = Math.Max(200, safeMax - headLen - 80);
        if (headLen + tailLen >= normalized.Length)
        {
            return normalized[..safeMax];
        }

        var head = normalized[..headLen].TrimEnd();
        var tail = normalized[^tailLen..].TrimStart();
        return head
               + "\n\n[중간 내용 생략됨: 요청 크기 제한으로 축약]\n\n"
               + tail;
    }

    public static bool TryExtractGroqMaxTokensLimit(string responseBody, out int limit)
    {
        limit = 0;
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        var match = GroqMaxTokensLimitRegex.Match(responseBody);
        if (!match.Success)
        {
            return false;
        }

        var raw = match.Groups["limit"].Success ? match.Groups["limit"].Value : match.Groups["limit2"].Value;
        return int.TryParse(raw, out limit) && limit > 0;
    }

    public static bool IsGroqRequestTooLarge(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        return responseBody.IndexOf("request_too_large", StringComparison.OrdinalIgnoreCase) >= 0
               || responseBody.IndexOf("entity too large", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static int GetGroqRetryDelayMs(HttpResponseHeaders headers, int fallbackMs)
    {
        string? retryAfter = null;
        if (headers.TryGetValues("retry-after", out var values))
        {
            retryAfter = values.FirstOrDefault();
        }

        if (int.TryParse(retryAfter, out var seconds) && seconds > 0)
        {
            return Math.Clamp(seconds * 1000, 400, 5000);
        }

        return Math.Clamp(fallbackMs, 400, 5000);
    }

    /// <summary>
    /// 단일 user 프롬프트에서 [최근 대화] 섹션을 파싱해 multi-turn messages로 분리.
    /// [최근 대화] 안의 [user]/[assistant] 블록을 개별 메시지로, [새 요청]은 마지막 user로.
    /// 파싱 실패 시 원본 그대로 단일 user 메시지로 폴백.
    /// </summary>
    public static List<(string Role, string Content)> SplitPromptToMultiTurn(string prompt)
    {
        var fallback = new List<(string, string)> { ("user", prompt) };
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return fallback;
        }

        var requestIdx = prompt.IndexOf("\n[새 요청]", StringComparison.Ordinal);
        if (requestIdx < 0)
        {
            return fallback;
        }

        var historyMarker = "\n[최근 대화]";
        var historyIdx = prompt.IndexOf(historyMarker, StringComparison.Ordinal);
        if (historyIdx < 0 || historyIdx > requestIdx)
        {
            return fallback;
        }

        var historyStart = historyIdx + historyMarker.Length;
        var historyText = prompt[historyStart..requestIdx].Trim();
        var requestText = prompt[(requestIdx + "\n[새 요청]".Length)..].Trim();

        var headerText = prompt[..historyIdx].Trim();
        var headerClean = headerText
            .Replace("[컨텍스트 사용 규칙]", "", StringComparison.Ordinal)
            .Trim();

        var messages = new List<(string, string)>();
        var systemContent = headerClean.Length > 0 ? headerClean : "";

        var historyMessages = ParseHistoryBlocks(historyText);

        if (historyMessages.Count == 0)
        {
            return fallback;
        }

        if (systemContent.Length > 0)
        {
            messages.Add(("system_context", systemContent));
        }

        foreach (var (role, content) in historyMessages)
        {
            messages.Add((role, content));
        }

        messages.Add(("user", requestText));

        return messages;
    }

    /// <summary>
    /// "[최근 대화]" 안의 [user]/[assistant] 블록을 파싱.
    /// [이전 대화 압축]은 assistant 메시지로 처리.
    /// </summary>
    public static List<(string Role, string Content)> ParseHistoryBlocks(string historyText)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(historyText))
        {
            return result;
        }

        var compressionIdx = historyText.IndexOf("[이전 대화 압축]", StringComparison.Ordinal);
        var recentTurnIdx = historyText.IndexOf("[최근 턴]", StringComparison.Ordinal);

        if (compressionIdx >= 0)
        {
            var compressionEnd = recentTurnIdx >= 0 ? recentTurnIdx : historyText.Length;
            var compressionText = historyText[compressionIdx..compressionEnd]
                .Replace("[이전 대화 압축]", "", StringComparison.Ordinal).Trim();
            if (compressionText.Length > 0)
            {
                result.Add(("assistant", $"[이전 대화 요약] {compressionText}"));
            }
        }

        var parseTarget = recentTurnIdx >= 0
            ? historyText[recentTurnIdx..]
            : (compressionIdx >= 0 ? historyText[..compressionIdx] : historyText);

        var lines = parseTarget.Split('\n');
        string? currentRole = null;
        var currentContent = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[user]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentRole != null && currentContent.Count > 0)
                {
                    result.Add((currentRole, string.Join('\n', currentContent).Trim()));
                }
                currentRole = "user";
                currentContent = new List<string>();
                var rest = trimmed["[user]".Length..].Trim();
                if (rest.Length > 0) currentContent.Add(rest);
            }
            else if (trimmed.StartsWith("[assistant]", StringComparison.OrdinalIgnoreCase))
            {
                if (currentRole != null && currentContent.Count > 0)
                {
                    result.Add((currentRole, string.Join('\n', currentContent).Trim()));
                }
                currentRole = "assistant";
                currentContent = new List<string>();
                var rest = trimmed["[assistant]".Length..].Trim();
                if (rest.Length > 0) currentContent.Add(rest);
            }
            else if (trimmed.StartsWith("[최근 턴]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            else if (currentRole != null)
            {
                currentContent.Add(line);
            }
        }

        if (currentRole != null && currentContent.Count > 0)
        {
            result.Add((currentRole, string.Join('\n', currentContent).Trim()));
        }

        return result;
    }
}
