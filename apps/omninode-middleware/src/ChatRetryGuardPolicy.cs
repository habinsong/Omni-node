using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class ChatRetryGuardPolicy
{
    public static bool ShouldRetryWithoutHistory(string input, string output)
    {
        var normalizedInput = (input ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedOutput = (output ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedInput.Length == 0 || normalizedOutput.Length == 0)
        {
            return false;
        }

        var isNewsRequest = ContainsAny(normalizedInput, "뉴스", "news", "헤드라인", "속보", "브리핑");
        if (isNewsRequest)
        {
            return false;
        }

        var looksLikeNewsAnswer = ContainsAny(
            normalizedOutput,
            "요청하신 소식",
            "주요 뉴스",
            "뉴스 10건",
            "뉴스 5건",
            "오늘 주요 뉴스",
            "no.1 제목"
        );
        if (!looksLikeNewsAnswer)
        {
            return LooksLikeOffTopicModelExplanation(normalizedInput, normalizedOutput)
                || LooksLikeUnrequestedP2SAnswer(normalizedInput, normalizedOutput);
        }

        var asksLlmPricing = ContainsAny(
            normalizedInput,
            "llm",
            "large language model",
            "언어 모델",
            "컨텍스트",
            "context window",
            "토큰",
            "api",
            "비용",
            "가격",
            "요금"
        );
        var hasLlmPricingSignalsInOutput = ContainsAny(
            normalizedOutput,
            "llm",
            "언어 모델",
            "컨텍스트",
            "context window",
            "토큰",
            "api",
            "비용",
            "가격",
            "요금"
        );
        if (asksLlmPricing && !hasLlmPricingSignalsInOutput)
        {
            return true;
        }

        return true;
    }

    public static bool LooksLikeOffTopicModelExplanation(string normalizedInput, string normalizedOutput)
    {
        if (!ContainsAny(normalizedInput, "gpt-oss", "gpt oss", "gpt_oss", "gptoss"))
        {
            return false;
        }

        return !ContainsAny(
            normalizedOutput,
            "gpt-oss",
            "gpt oss",
            "openai",
            "llm",
            "언어 모델",
            "오픈 웨이트",
            "open weight",
            "moe",
            "mixture-of-experts",
            "120b");
    }

    public static bool LooksLikeUnrequestedP2SAnswer(string normalizedInput, string normalizedOutput)
    {
        if (ContainsAny(normalizedInput, "p2s", "print-to-shape", "3d 프린팅", "3d printing"))
        {
            return false;
        }

        return ContainsAny(normalizedOutput, "p2s", "print-to-shape", "3d 프린팅", "3d printing");
    }

    public static string BuildOffTopicGuardMessage(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "모델 응답이 요청과 맞지 않아 답변을 중단했습니다. 다시 질문해 주세요."
            : $"모델 응답이 새 요청과 맞지 않아 답변을 중단했습니다. 원문 요청: {normalized}";
    }

    public static bool LooksLikeVagueWebLookupRequest(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || normalized.Length > 40)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "웹검색해서 찾아",
            "웹 검색해서 찾아",
            "웹검색해",
            "웹 검색해",
            "검색해서 찾아",
            "찾아봐",
            "찾아 줘",
            "찾아줘",
            "검색해봐",
            "검색해 줘",
            "검색해줘",
            "look it up",
            "search it");
    }

    public static int ResolveSingleChatMaxOutputTokens(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return 4096;
        }

        if (ContainsAny(
                normalized,
                "자세",
                "상세",
                "깊게",
                "설명",
                "가이드",
                "분석",
                "원리",
                "기술",
                "비전공자",
                "non-expert",
                "explain"))
        {
            return 4096;
        }

        if (SearchQueryPolicy.LooksLikeListOutputRequest(normalized))
        {
            return 3072;
        }

        if (ContainsAny(normalized, "요약", "정리", "summary", "compare", "비교"))
        {
            return 2048;
        }

        return 3072;
    }

    public static string BuildHistoryBypassInput(string preparedInput)
    {
        var normalized = (preparedInput ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return $"""
                [중요]
                아래 새 요청을 최우선으로 처리하세요.
                이전 대화의 형식을 관성으로 따라가지 말고, 새 요청 주제에만 답변하세요.

                [새 요청]
                {normalized}
                """;
    }

    public static string BuildOriginalRequestRetryInput(string rawInput)
    {
        var normalized = (rawInput ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return $"""
                [중요]
                아래 원문 요청만 다시 처리하세요.
                이전 대화의 형식을 관성으로 따라가지 말고, 새 요청 주제에만 답변하세요.

                [원문 요청]
                {normalized}
                """;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern)
                && text.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
