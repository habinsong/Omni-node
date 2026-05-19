using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class ConversationTitlePolicy
{
    private static readonly Regex LeadingTitleNoiseRegex = new(
        @"^(?:(?:[-*#>]+\s*)|(?:\(\d+\)\s+)|(?:\d+[.)]\s+))+",
        RegexOptions.Compiled
    );

    public static bool ShouldAutoTitle(ConversationThreadView thread)
    {
        var userCount = thread.Messages.Count(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        var assistantCount = thread.Messages.Count(x => x.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));
        if (userCount < 1 || assistantCount < 1)
        {
            return false;
        }

        var title = (thread.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        if (IsLikelyProviderFailureText(title))
        {
            return true;
        }

        if (userCount != 1)
        {
            return false;
        }

        var defaultPrefix = $"{thread.Scope}-{thread.Mode}";
        if (title.StartsWith(defaultPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (title.StartsWith("새 대화", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("new chat", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("chat-", StringComparison.OrdinalIgnoreCase)
            || title.StartsWith("coding-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static string NormalizeConversationTitle(string raw)
    {
        var value = (raw ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var line = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? value;
        line = LeadingTitleNoiseRegex.Replace(line, string.Empty).Trim();
        line = line.Trim('"', '\'', '`', '“', '”', '‘', '’');
        if (line.Length > 38)
        {
            line = line[..38].TrimEnd();
        }

        return line;
    }

    public static string BuildFallbackConversationTitle(string firstUser)
    {
        var normalized = (firstUser ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "새 대화";
        }

        return normalized.Length <= 32 ? normalized : normalized[..32].TrimEnd() + "...";
    }

    public static string BuildFallbackConversationTitleFromAssistant(string assistantText)
    {
        var normalized = (assistantText ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized) || IsLikelyProviderFailureText(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"\[(?<text>[^\]]+)\]\((?<url>https?://[^)]+)\)", "${text}");
        normalized = Regex.Replace(normalized, @"https?://\S+", string.Empty);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"^(요약|핵심|출처)\s*:\s*", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"^(?:[-•▪*]\s+|\d+[.)]\s+)", string.Empty);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim().Trim('"', '\'', '`', '“', '”', '‘', '’');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var sentenceBoundary = normalized.IndexOfAny(new[] { '.', '!', '?', ':', ';' });
        if (sentenceBoundary > 8)
        {
            normalized = normalized[..sentenceBoundary].Trim();
        }

        return normalized.Length <= 32 ? normalized : normalized[..32].TrimEnd() + "...";
    }

    public static string TruncateForTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 260 ? normalized : normalized[..260] + "...";
    }

    public static string SelectAssistantTextForAutoTitle(ConversationThreadView thread, bool preferLatest)
    {
        var assistantMessages = thread.Messages
            .Where(x => x.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            .Select(x => (x.Text ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (assistantMessages.Count == 0)
        {
            return string.Empty;
        }

        var candidatePool = preferLatest
            ? assistantMessages.AsEnumerable().Reverse()
            : assistantMessages.AsEnumerable();
        var usable = candidatePool.FirstOrDefault(x => !IsLikelyProviderFailureText(x));
        if (!string.IsNullOrWhiteSpace(usable))
        {
            return usable;
        }

        return preferLatest ? assistantMessages[^1] : assistantMessages[0];
    }

    public static bool IsLikelyProviderFailureText(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var lowered = normalized.ToLowerInvariant();
        if (lowered.Contains("the operation was canceled", StringComparison.Ordinal)
            || lowered.Contains("the operation was cancelled", StringComparison.Ordinal))
        {
            return true;
        }

        if (lowered.EndsWith("api 키가 설정되지 않았습니다.", StringComparison.Ordinal)
            || lowered.EndsWith("인증이 필요합니다.", StringComparison.Ordinal)
            || lowered.EndsWith("응답이 비어 있습니다.", StringComparison.Ordinal)
            || lowered.EndsWith("응답이 비어 있습니다. 다시 질문해 주세요.", StringComparison.Ordinal)
            || lowered.Contains("응답 시간이 초과되었습니다.", StringComparison.Ordinal))
        {
            return true;
        }

        return lowered.StartsWith("groq 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("cerebras 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia nim 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("copilot 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("groq 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("cerebras 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("nvidia nim 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("copilot 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 웹검색 호출 오류:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 웹검색 요청 실패:", StringComparison.Ordinal)
            || lowered.StartsWith("gemini 웹검색 응답 시간이 초과되었습니다.", StringComparison.Ordinal)
            || lowered.StartsWith("현재 groq 요청 한도를 초과했습니다.", StringComparison.Ordinal);
    }
}
