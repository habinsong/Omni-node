using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ConversationHistoryPolicyTests
{
    [Fact]
    public void ParseHistoryMessagesKeepsMultilineMessageBodies()
    {
        var messages = ConversationHistoryPolicy.ParseHistoryMessages(
            """
            [user] 첫 질문
            이어지는 줄
            [assistant] 첫 답변
            답변 세부 내용
            """
        );

        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("첫 질문\n이어지는 줄", messages[0].Text);
        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal("첫 답변\n답변 세부 내용", messages[1].Text);
    }

    [Fact]
    public void TrimContextHistoryKeepsMostRecentMessagesWithinBudget()
    {
        var history = string.Join(
            '\n',
            "[user] 오래된 질문입니다",
            "[assistant] 오래된 답변입니다",
            "[user] 최근 질문입니다",
            "[assistant] 최근 답변입니다"
        );

        var trimmed = ConversationHistoryPolicy.TrimContextHistory(history, 60);

        Assert.DoesNotContain("오래된 질문", trimmed);
        Assert.Contains("최근 답변", trimmed);
    }

    [Fact]
    public void BuildBudgetedContextHistorySeparatesOlderSummaryAndRecentTurns()
    {
        var history = string.Join(
            '\n',
            "[user] 요구사항: 텔레그램 응답을 줄여줘",
            "[assistant] 설정 파일을 수정했습니다",
            "[user] 두 번째 질문",
            "[assistant] 두 번째 답변",
            "[user] 세 번째 질문",
            "[assistant] 세 번째 답변"
        );

        var budgeted = ConversationHistoryPolicy.BuildBudgetedContextHistory(history, 2000);

        Assert.Contains("[이전 대화 압축]", budgeted);
        Assert.Contains("요구사항", budgeted);
        Assert.Contains("[최근 턴]", budgeted);
        Assert.Contains("세 번째 답변", budgeted);
    }

    [Fact]
    public void BuildMessageLevelSummaryFallsBackToRecentOlderMessages()
    {
        var summary = ConversationHistoryPolicy.BuildMessageLevelSummary(
            new[]
            {
                (Role: "user", Text: "잡담 하나"),
                (Role: "assistant", Text: "잡담 둘"),
                (Role: "user", Text: "잡담 셋")
            },
            2000
        );

        Assert.DoesNotContain("잡담 하나", summary);
        Assert.Contains("잡담 둘", summary);
        Assert.Contains("잡담 셋", summary);
    }

    [Fact]
    public void IsHighSignalHistoryLineDetectsImplementationSignal()
    {
        Assert.True(ConversationHistoryPolicy.IsHighSignalHistoryLine("파일 구현 결과를 확인했습니다"));
        Assert.False(ConversationHistoryPolicy.IsHighSignalHistoryLine("좋은 아침입니다"));
    }
}
