using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class ConversationContextPolicy
{
    public static bool ShouldUsePriorConversationContext(string input, out bool isAmbiguous)
    {
        isAmbiguous = false;
        var normalized = NormalizeInput(input);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (ContainsAny(normalized, "아니", "그게 아니라", "그 말고", "말고", "라고", "라니까"))
        {
            return true;
        }

        if (SearchQueryPolicy.LooksLikeCasualOrIdentityQuestion(normalized))
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "이전",
                "앞서",
                "아까",
                "방금",
                "위에서",
                "위 내용",
                "최근 대화",
                "대화 내용",
                "메모리",
                "기억",
                "그 답변",
                "그 내용",
                "그거",
                "그것",
                "그걸",
                "해당",
                "이어서",
                "계속",
                "다시",
                "더 자세히",
                "웹검색해서 찾아",
                "웹 검색해서 찾아",
                "찾아봐",
                "검색해봐",
                "search it",
                "look it up",
                "그니까",
                "그러니까",
                "그래서",
                "그럼",
                "그러면",
                "그래도",
                "결국"))
        {
            return true;
        }

        if (LooksLikeStrongFollowupQuestion(normalized))
        {
            return true;
        }

        if (ContainsAny(
                normalized,
                "잘 돌아",
                "잘 작동",
                "잘 동작",
                "잘 되",
                "잘 될",
                "잘 굴러",
                "쓸만",
                "쓸 만",
                "괜찮",
                "어때",
                "어떨",
                "어떤지",
                "어떻게 생각",
                "어떻게 봐",
                "네 생각",
                "네 의견",
                "너 생각",
                "너의 생각",
                "당신 생각",
                "당신의 생각",
                "검토해",
                "판단해",
                "추천해",
                "비교해",
                "rec recommend",
                "what do you think",
                "would it work"))
        {
            isAmbiguous = true;
            return true;
        }

        if (normalized.Length <= 60)
        {
            if (ContainsAny(normalized, "그 ", "이 ", "저 ", "위 ", "앞 ", "방금", "아까", "이거", "저거", "이건", "그건", "이게", "이걸", "그걸"))
            {
                return true;
            }

            if (Regex.IsMatch(normalized, @"\d") && normalized.Length <= 30)
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeStrongFollowupQuestion(string input)
    {
        var text = NormalizeInput(input);
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

    public static bool LooksLikeExplicitStandaloneQuestion(string input)
    {
        var text = NormalizeInput(input);
        if (text.Length == 0)
        {
            return false;
        }

        if (LooksLikeStrongFollowupQuestion(text))
        {
            return false;
        }

        if (ContainsAny(text, "이전", "앞서", "아까", "방금", "그거", "그것", "그 답변", "해당", "이어서", "계속"))
        {
            return false;
        }

        var hasStandaloneTopic = ExtractContextTokens(text).Any(token =>
            token.Length >= 4
            || token.Any(char.IsDigit)
            || token.Contains('-', StringComparison.Ordinal)
            || token.Contains('/', StringComparison.Ordinal)
            || token.Contains('.', StringComparison.Ordinal));
        if (!hasStandaloneTopic)
        {
            return false;
        }

        return ContainsAny(text, "뭐야", "무엇", "설명", "알려", "정리", "원리", "방법", "비교", "추천", "분석", "어떻게", "왜", "what", "how", "why", "explain");
    }

    public static bool HasMeaningfulTokenOverlap(
        IReadOnlySet<string> left,
        IReadOnlySet<string> right,
        int minimumShared = 2
    )
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        var shared = left.Where(right.Contains).ToArray();
        if (shared.Length == 0)
        {
            return false;
        }

        if (shared.Any(token => token.Length >= 5 || token.Any(char.IsDigit) || token.Contains('-', StringComparison.Ordinal)))
        {
            return true;
        }

        return shared.Length >= minimumShared;
    }

    public static IReadOnlySet<string> ExtractContextTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches((text ?? string.Empty).ToLowerInvariant(), @"[\p{L}\p{N}][\p{L}\p{N}._/-]*"))
        {
            var token = NormalizeContextToken(match.Value);
            if (IsContextStopToken(token))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    public static string NormalizeContextToken(string token)
    {
        return (token ?? string.Empty)
            .Trim()
            .Trim('.', ',', ':', ';', '!', '?', '\'', '"', '`', '(', ')', '[', ']', '{', '}');
    }

    public static bool IsContextStopToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length <= 1)
        {
            return true;
        }

        return token is
            "이" or "그" or "저" or "것" or "거" or "수" or "좀" or "왜" or "뭐" or "무엇" or
            "모델" or "설명" or "대해" or "대한" or "관련" or "알려줘" or "해줘" or "줘" or
            "답변" or "질문" or "요청" or "다음" or "현재" or "오늘" or "최신" or "정보" or
            "the" or "a" or "an" or "and" or "or" or "to" or "of" or "for" or "with" or "in" or
            "on" or "about" or "what" or "why" or "how" or "is" or "are" or "it" or "this" or "that";
    }

    private static string NormalizeInput(string input)
    {
        return Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
