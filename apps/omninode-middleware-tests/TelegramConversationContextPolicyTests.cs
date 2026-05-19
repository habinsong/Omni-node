using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class TelegramConversationContextPolicyTests
{
    [Fact]
    public void BuildFollowupAwareInputKeepsStandaloneInputWithoutAnchor()
    {
        var thread = BuildThread();

        var result = TelegramConversationContextPolicy.BuildFollowupAwareInput(thread, "새 질문이야");

        Assert.Equal("새 질문이야", result);
    }

    [Fact]
    public void BuildFollowupAwareInputExpandsWeakWebLookupFollowup()
    {
        var thread = BuildThread(
            User("P2S 방식이 뭐야?", -3),
            Assistant("P2S는 print-to-shape 방식입니다.", -2)
        );

        var result = TelegramConversationContextPolicy.BuildFollowupAwareInput(thread, "검색해줘");

        Assert.Contains("[직전 주제]", result);
        Assert.Contains("P2S 방식이 뭐야?", result);
        Assert.Contains("[직전 답변]", result);
        Assert.Contains("웹에서 검색해서 근거 중심", result);
    }

    [Fact]
    public void BuildFollowupAwareInputExpandsCorrectionFollowup()
    {
        var thread = BuildThread(
            User("H2S 센서 스펙 알려줘", -3),
            Assistant("H2S 센서 기준으로 정리했습니다.", -2)
        );

        var result = TelegramConversationContextPolicy.BuildFollowupAwareInput(thread, "H2S 가 아니라 P2S 라고");

        Assert.Contains("[정정 요청]", result);
        Assert.Contains("이전에 잘못 잡은 대상", result);
        Assert.Contains("H2S 센서 스펙 알려줘", result);
    }

    [Fact]
    public void BuildFollowupAwareInputExpandsExhaustedFeedbackWithLatestAssistant()
    {
        var thread = BuildThread(
            User("빌드 실패 원인 알려줘", -4),
            Assistant("캐시 삭제 후 다시 빌드하세요.", -3),
            User("다 해봤는데도 안돼", -2),
            Assistant("다른 기본 조치를 확인하세요.", -1)
        );

        var result = TelegramConversationContextPolicy.BuildFollowupAwareInput(thread, "이미 해봤는데");

        Assert.Contains("[사용자 추가 피드백]", result);
        Assert.Contains("같은 해결책을 반복하지 말고", result);
        Assert.Contains("다른 기본 조치를 확인하세요.", result);
        Assert.Contains("빌드 실패 원인 알려줘", result);
    }

    [Fact]
    public void BuildFollowupAwareInputExpandsContextualFollowup()
    {
        var thread = BuildThread(
            User("Gemini URL context 설명해줘", -3),
            Assistant("URL context는 URL 기반 컨텍스트를 씁니다.", -2)
        );

        var result = TelegramConversationContextPolicy.BuildFollowupAwareInput(thread, "그럼 장점은?");

        Assert.Contains("[현재 후속 질문]", result);
        Assert.Contains("그럼 장점은?", result);
        Assert.Contains("직전 주제와 답변", result);
    }

    [Fact]
    public void FindAnchorTurnSkipsPriorWeakFollowups()
    {
        var thread = BuildThread(
            User("원래 주제 설명해줘", -5),
            Assistant("원래 주제 답변", -4),
            User("왜?", -3),
            Assistant("후속 답변", -2)
        );

        var anchor = TelegramConversationContextPolicy.FindAnchorTurn(thread, "그럼 예시는?");

        Assert.Equal("원래 주제 설명해줘", anchor.User);
        Assert.Equal("후속 답변", anchor.Assistant);
    }

    [Theory]
    [InlineData("검색해줘")]
    [InlineData("그건?")]
    [InlineData("웹에서 찾아줘")]
    public void IsWeakFollowupInputDetectsShortLookupRequests(string input)
    {
        Assert.True(TelegramConversationContextPolicy.IsWeakFollowupInput(input));
    }

    private static ConversationThreadView BuildThread(params ConversationMessageView[] messages)
    {
        var now = DateTimeOffset.UtcNow;
        return new ConversationThreadView(
            "thread-1",
            "default",
            "chat",
            "테스트",
            "default",
            "telegram",
            Array.Empty<string>(),
            now.AddMinutes(-10),
            now,
            messages,
            Array.Empty<string>(),
            null
        );
    }

    private static ConversationMessageView User(string text, int minuteOffset)
    {
        return new ConversationMessageView("user", text, "telegram:user", DateTimeOffset.UtcNow.AddMinutes(minuteOffset));
    }

    private static ConversationMessageView Assistant(string text, int minuteOffset)
    {
        return new ConversationMessageView("assistant", text, "telegram:assistant", DateTimeOffset.UtcNow.AddMinutes(minuteOffset));
    }
}
