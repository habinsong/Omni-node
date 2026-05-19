using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ConversationTitlePolicyTests
{
    [Fact]
    public void ShouldAutoTitleRequiresUserAndAssistantTurn()
    {
        var thread = BuildThread("chat-single", User("안녕"));

        Assert.False(ConversationTitlePolicy.ShouldAutoTitle(thread));
    }

    [Fact]
    public void ShouldAutoTitleAllowsDefaultFirstTurnTitle()
    {
        var thread = BuildThread("chat-single", User("검색 정리해줘"), Assistant("정리했습니다."));

        Assert.True(ConversationTitlePolicy.ShouldAutoTitle(thread));
    }

    [Fact]
    public void ShouldAutoTitleKeepsCustomTitleAfterMultipleUserTurns()
    {
        var thread = BuildThread(
            "이미 정한 제목",
            User("첫 질문"),
            Assistant("첫 답변"),
            User("다음 질문"),
            Assistant("다음 답변")
        );

        Assert.False(ConversationTitlePolicy.ShouldAutoTitle(thread));
    }

    [Fact]
    public void ShouldAutoTitleRetriesProviderFailureTitle()
    {
        var thread = BuildThread(
            "Gemini 호출 오류: timeout",
            User("첫 질문"),
            Assistant("첫 답변"),
            User("다음 질문"),
            Assistant("다음 답변")
        );

        Assert.True(ConversationTitlePolicy.ShouldAutoTitle(thread));
    }

    [Fact]
    public void NormalizeConversationTitleRemovesNoiseAndUsesFirstLine()
    {
        var title = ConversationTitlePolicy.NormalizeConversationTitle(
            """
            1. "검색 결과 요약"
            둘째 줄은 버림
            """
        );

        Assert.Equal("검색 결과 요약", title);
    }

    [Fact]
    public void BuildFallbackConversationTitleFromAssistantStripsMarkdownAndUrls()
    {
        var title = ConversationTitlePolicy.BuildFallbackConversationTitleFromAssistant(
            "요약: **[Omni-node](https://example.com)** 구현 현황입니다. https://example.com/detail"
        );

        Assert.Equal("Omni-node 구현 현황입니다", title);
    }

    [Fact]
    public void SelectAssistantTextForAutoTitleSkipsProviderFailure()
    {
        var thread = BuildThread(
            "Gemini 호출 오류: timeout",
            User("첫 질문"),
            Assistant("Gemini 호출 오류: timeout"),
            Assistant("실제 답변입니다")
        );

        Assert.Equal("실제 답변입니다", ConversationTitlePolicy.SelectAssistantTextForAutoTitle(thread, preferLatest: false));
    }

    private static ConversationThreadView BuildThread(string title, params ConversationMessageView[] messages)
    {
        return new ConversationThreadView(
            "thread-1",
            "chat",
            "single",
            title,
            "기본",
            "일반",
            Array.Empty<string>(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            messages,
            Array.Empty<string>(),
            null
        );
    }

    private static ConversationMessageView User(string text)
    {
        return new ConversationMessageView("user", text, "test:user", DateTimeOffset.UtcNow);
    }

    private static ConversationMessageView Assistant(string text)
    {
        return new ConversationMessageView("assistant", text, "test:assistant", DateTimeOffset.UtcNow);
    }
}
