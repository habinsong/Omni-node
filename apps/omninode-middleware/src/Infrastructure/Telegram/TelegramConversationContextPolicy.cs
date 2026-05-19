using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class TelegramConversationContextPolicy
{
    public static string BuildFollowupAwareInput(ConversationThreadView thread, string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var anchorTurn = FindAnchorTurn(thread, normalized);
        if (string.IsNullOrWhiteSpace(anchorTurn.User))
        {
            return normalized;
        }

        var anchor = anchorTurn.User!;
        var assistantContext = string.IsNullOrWhiteSpace(anchorTurn.Assistant)
            ? string.Empty
            : $"""

              [직전 답변]
              {anchorTurn.Assistant}
              """;

        if (SearchQueryPolicy.LooksLikeExplicitWebLookupQuestion(normalized) && IsWeakFollowupInput(normalized))
        {
            return $"""
                    [직전 주제]
                    {anchor}{assistantContext}

                    [현재 요청]
                    위 주제에 대해 웹에서 검색해서 근거 중심으로 다시 설명해줘.
                    사용자 추가 요청: {normalized}
                    """;
        }

        if (IsCorrectionFollowup(normalized))
        {
            return $"""
                    [직전 주제]
                    {anchor}{assistantContext}

                    [정정 요청]
                    {normalized}

                    위 정정을 반영해서, 이전에 잘못 잡은 대상이 있으면 바로잡아 다시 답해줘.
                    """;
        }

        if (IsExhaustedFeedback(normalized))
        {
            var latestAssistant = thread.Messages
                .LastOrDefault(msg => msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                ?.Text?.Trim();
            var assistantBlock = string.IsNullOrWhiteSpace(latestAssistant)
                ? string.Empty
                : $"""

                  [직전 답변 요약]
                  {latestAssistant}
                  """;
            return $"""
                    [직전 주제]
                    {anchor}{assistantBlock}

                    [사용자 추가 피드백]
                    {normalized}

                    사용자는 앞서 제안한 기본 조치를 이미 시도했다고 본다.
                    같은 해결책을 반복하지 말고, 남은 원인 후보와 확인 순서를 더 좁혀서 답해줘.
                    """;
        }

        if (IsContextualFollowup(normalized))
        {
            return $"""
                    [직전 주제]
                    {anchor}{assistantContext}

                    [현재 후속 질문]
                    {normalized}

                    위 직전 주제와 답변을 기준으로, 현재 후속 질문에만 직접 답해줘.
                    """;
        }

        return normalized;
    }

    public static (string? User, string? Assistant) FindAnchorTurn(ConversationThreadView thread, string currentInput)
    {
        if (thread.Messages.Count == 0)
        {
            return (null, null);
        }

        string? latestAssistant = null;
        foreach (var message in thread.Messages.AsEnumerable().Reverse())
        {
            var text = (message.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                latestAssistant ??= text.Length > 1000 ? text[..1000] + "..." : text;
                continue;
            }

            if (!message.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                || text.Equals(currentInput, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsWeakFollowupInput(text)
                || IsCorrectionFollowup(text)
                || IsExhaustedFeedback(text)
                || IsContextualFollowup(text))
            {
                continue;
            }

            var cappedUser = text.Length > 800 ? text[..800] + "..." : text;
            return (cappedUser, latestAssistant);
        }

        var fallbackUser = thread.Messages
            .Where(msg => msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Select(msg => (msg.Text ?? string.Empty).Trim())
            .LastOrDefault(text => text.Length > 0 && !text.Equals(currentInput, StringComparison.Ordinal));
        return (fallbackUser, latestAssistant);
    }

    public static bool IsWeakFollowupInput(string input)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.Length <= 18
            && (SearchQueryPolicy.LooksLikeExplicitWebLookupQuestion(normalized)
                || normalized is "그건?" or "이건?" or "왜?" or "진짜?" or "다시"))
        {
            return true;
        }

        return normalized is "검색해서 말해"
            or "검색해줘"
            or "검색해 줘"
            or "웹에서 찾아줘"
            or "웹에서 말해줘"
            or "그거 검색해줘"
            or "그거 검색해서 말해줘";
    }

    public static bool IsContextualFollowup(string input)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return false;
        }

        return LooksLikeStrongFollowupQuestion(normalized)
               || (normalized.Length <= 24
                   && ContainsAny(
                       normalized,
                       "왜",
                       "그럼",
                       "그래서",
                       "근데",
                       "예시",
                       "근거",
                       "차이",
                       "장점",
                       "단점",
                       "문제",
                       "해결",
                       "다시"));
    }

    public static bool IsCorrectionFollowup(string input)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.Contains("아니라", StringComparison.Ordinal)
               || normalized.Contains("말한게 아니라", StringComparison.Ordinal)
               || normalized.Contains("말한 게 아니라", StringComparison.Ordinal)
               || normalized.Contains("정정", StringComparison.Ordinal)
               || normalized.Contains("오타", StringComparison.Ordinal)
               || normalized.Contains("p2s 라고", StringComparison.Ordinal)
               || normalized.Contains("h2s 가 아니라", StringComparison.Ordinal);
    }

    public static bool IsExhaustedFeedback(string input)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "다했다고",
            "다 했다고",
            "다해도",
            "다 해도",
            "해봤는데",
            "해 봤는데",
            "이미 해봤",
            "이미 해 봤",
            "너가 말한거 다했다고",
            "너가 말한 거 다했다고",
            "다 했는데도",
            "했는데도"
        );
    }

    private static bool LooksLikeStrongFollowupQuestion(string normalized)
    {
        var text = Normalize(normalized);
        if (text.Length == 0)
        {
            return false;
        }

        if (ContainsAny(
                text,
                "왜 그렇게",
                "왜 그런",
                "왜?",
                "근거는",
                "예시는",
                "장점은",
                "단점은",
                "차이는",
                "문제는",
                "해결책은",
                "그 이유",
                "그 원리",
                "그 차이",
                "더 쉽게",
                "더 자세히",
                "한 줄로",
                "예시 들어",
                "예시를 들어",
                "그 기준",
                "그 방식",
                "방금 말한",
                "위 답변"))
        {
            return true;
        }

        return text.Length <= 40
               && (text.StartsWith("왜", StringComparison.Ordinal)
                   || text.StartsWith("그럼", StringComparison.Ordinal)
                   || text.StartsWith("그러면", StringComparison.Ordinal)
                   || text.StartsWith("그래서", StringComparison.Ordinal)
                   || text.StartsWith("근데", StringComparison.Ordinal)
                   || text.StartsWith("근데 ", StringComparison.Ordinal)
                   || text.StartsWith("그건", StringComparison.Ordinal)
                   || text.StartsWith("이건", StringComparison.Ordinal)
                   || text.StartsWith("그게", StringComparison.Ordinal)
                   || text.StartsWith("그걸", StringComparison.Ordinal)
                   || text.StartsWith("그거", StringComparison.Ordinal)
                   || text.StartsWith("그 부분", StringComparison.Ordinal));
    }

    private static string Normalize(string input)
    {
        return Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
