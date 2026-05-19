namespace OmniNode.Middleware;

internal static class TelegramPromptPolicy
{
    public static string BuildCompressionPrompt(string input)
    {
        return $"""
                아래 긴 입력을 핵심만 유지해 한국어로 압축 요약하세요.
                규칙:
                - 최대 8줄
                - 요구사항/제약/에러/결정포인트 보존
                - 불필요한 수식어 제거

                [원문]
                {input}
                """;
    }

    public static string BuildProfilePrompt(string concisePrompt, string profile, string thinkingLevel, string localNow)
    {
        var modeGuide = profile switch
        {
            "code" => "코딩 모드: 변경 포인트, 실행/검증 명령, 실패 시 다음 조치까지 간결히 제시하세요.",
            "talk" => "대화 모드: 핵심 결론 중심으로 정리하세요.",
            _ => "기본 모드: 핵심만 간결하게 답하세요."
        };
        var thinkingGuide = thinkingLevel == "high"
            ? "사고 강도: high (정확성, 리스크, 예외 케이스를 우선)"
            : "사고 강도: low (빠르고 간결한 결론 우선)";

        return $"""
                로컬 시간 기준:
                {localNow}

                {modeGuide}
                {thinkingGuide}

                {concisePrompt}
                """;
    }

    public static string ResolveThinkingLevel(TelegramLlmPreferences snapshot, string userText)
    {
        if (snapshot.Profile == "code")
        {
            return snapshot.CodeThinkingLevel == "high" ? "high" : "low";
        }

        if (snapshot.TalkThinkingLevel == "high")
        {
            return "high";
        }

        return IsDecisionOrRiskQuestion(userText) ? "high" : "low";
    }

    public static bool IsDecisionOrRiskQuestion(string input)
    {
        var normalized = (input ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "비교",
            "결정",
            "결론",
            "추천",
            "정확",
            "리스크",
            "위험",
            "근거",
            "정책",
            "장단점",
            "tradeoff",
            "risk"
        );
    }

    public static bool UserRequiresConclusion(string input)
    {
        var normalized = (input ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "결론",
            "결정",
            "확정",
            "하나만",
            "추천",
            "최종안",
            "choose",
            "final answer"
        );
    }

    public static bool ModelShowsUncertainty(string answer)
    {
        var normalized = (answer ?? string.Empty).ToLowerInvariant();
        return ContainsAny(
            normalized,
            "확실하지",
            "알 수 없",
            "근거 부족",
            "불확실",
            "모르겠",
            "추정",
            "insufficient",
            "uncertain",
            "not sure"
        );
    }

    public static string BuildConclusionEscalationPrompt(string contextualPrompt, string priorAnswer, string thinkingLevel)
    {
        var style = thinkingLevel == "high"
            ? "정확성과 리스크를 우선해 단일 결론을 제시하세요."
            : "간결하게 단일 결론을 제시하세요.";
        return $"""
                아래는 기존 답변입니다.
                이 답변의 불확실성을 줄이고 반드시 결론을 한 가지로 확정하세요.
                {style}
                출력 규칙:
                - 첫 줄에 결론 1문장
                - 이후 최대 5줄로 근거
                - 군더더기 금지

                [이전 답변]
                {priorAnswer}

                [원 질문]
                {contextualPrompt}
                """;
    }

    public static string BuildOrchestrationPrompt(
        string userText,
        IReadOnlyList<LlmSingleChatResult> workerResults,
        IReadOnlyDictionary<string, string> roleByProvider
    )
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("다음은 오케스트레이션 워커들이 역할을 나눠 처리한 결과입니다.");
        builder.AppendLine($"사용자 질문: {userText}");
        builder.AppendLine();
        builder.AppendLine("[역할 계획]");
        foreach (var item in workerResults)
        {
            var role = roleByProvider.TryGetValue(item.Provider, out var assignedRole)
                ? assignedRole
                : "보조 워커";
            builder.AppendLine($"- {item.Provider}:{item.Model} => {role}");
        }

        builder.AppendLine();
        builder.AppendLine("[역할별 결과]");
        foreach (var item in workerResults)
        {
            var role = roleByProvider.TryGetValue(item.Provider, out var assignedRole)
                ? assignedRole
                : "보조 워커";
            builder.AppendLine($"[{item.Provider}:{item.Model}]");
            builder.AppendLine($"역할: {role}");
            builder.AppendLine(item.Text);
            builder.AppendLine();
        }

        builder.AppendLine("요구사항:");
        builder.AppendLine("1) 역할별 장점을 취합해 하나의 최종 답변으로 통합");
        builder.AppendLine("2) 사실 충돌이 있으면 가장 보수적이고 검증 친화적인 결론을 선택");
        builder.AppendLine("3) 내부 역할 분담 과정은 드러내지 말고 사용자에게 바로 쓸 답변만 출력");
        builder.AppendLine("4) 한국어로 간결하고 실행 가능하게 답변");
        builder.AppendLine("5) 마크다운 코드블록은 필요할 때만 사용");
        return builder.ToString().Trim();
    }

    public static string BuildConcisePrompt(string input, string localNow)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        var looksLikeListRequest = SearchQueryPolicy.LooksLikeListOutputRequest(normalized);
        var requestedCount = SearchQueryPolicy.ResolveRequestedResultCountFromQuery(input ?? string.Empty);
        var lengthRule = looksLikeListRequest
            ? $"- 목록형 요청은 항목 수를 임의로 줄이지 말고 요청 건수({requestedCount})를 유지"
            : "- 최대 7줄";
        return $"""
                아래 질문에 한국어로 간결하게 답하세요.
                규칙:
                - 결론 먼저
                - 불필요한 인삿말/군더더기 금지
                {lengthRule}
                - 핵심 불릿 위주
                - 시간 관련 질문은 아래 로컬 시간을 기준으로 답변

                로컬 시간:
                {localNow}

                질문:
                {input}
                """;
    }

    public static string BuildFullFidelityPrompt(string input, string localNow)
    {
        return $"""
                아래 요청에 한국어로 답하세요.
                규칙:
                - 사용자의 원 요구사항, 개수, 형식, 코드블록, 표, 첨부/검색/스킬 컨텍스트를 임의로 줄이지 마세요.
                - 답변은 결론부터 시작하되, 필요한 설명과 근거는 충분히 포함하세요.
                - 내부 컨텍스트 마커는 답변에 노출하지 말고, 사용자에게 필요한 내용만 자연스럽게 정리하세요.
                - 시간 관련 질문은 아래 로컬 시간을 기준으로 답변하세요.

                로컬 시간:
                {localNow}

                요청:
                {input}
                """;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
