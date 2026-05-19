using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ConversationContextPolicyTests
{
    [Theory]
    [InlineData("그럼 왜 그런거야?", true, false)]
    [InlineData("이전 대화 이어서 다시 설명해줘", true, false)]
    [InlineData("P2S랑 H2S 비교해줘", true, true)]
    [InlineData("ㅎㅇ", false, false)]
    public void ShouldUsePriorConversationContextClassifiesFollowupsAndStandaloneGreeting(
        string input,
        bool expectedUseContext,
        bool expectedAmbiguous
    )
    {
        var useContext = ConversationContextPolicy.ShouldUsePriorConversationContext(input, out var isAmbiguous);

        Assert.Equal(expectedUseContext, useContext);
        Assert.Equal(expectedAmbiguous, isAmbiguous);
    }

    [Theory]
    [InlineData("왜 그렇게 봐?", true)]
    [InlineData("그럼 예시는?", true)]
    [InlineData("새로운 프로젝트 구조 설명해줘", false)]
    public void LooksLikeStrongFollowupQuestionDetectsAnaphoricQuestions(string input, bool expected)
    {
        Assert.Equal(expected, ConversationContextPolicy.LooksLikeStrongFollowupQuestion(input));
    }

    [Theory]
    [InlineData("OpenAI SDK 사용법 설명해줘", true)]
    [InlineData("방금 그 답변 더 자세히", false)]
    [InlineData("뭐야", false)]
    public void LooksLikeExplicitStandaloneQuestionRequiresStandaloneTopic(string input, bool expected)
    {
        Assert.Equal(expected, ConversationContextPolicy.LooksLikeExplicitStandaloneQuestion(input));
    }

    [Fact]
    public void ExtractContextTokensDropsStopWordsAndNormalizesPunctuation()
    {
        var tokens = ConversationContextPolicy.ExtractContextTokens("이 OpenAI-SDK, 모델 설명 알려줘");

        Assert.Contains("openai-sdk", tokens);
        Assert.DoesNotContain("이", tokens);
        Assert.DoesNotContain("모델", tokens);
        Assert.DoesNotContain("설명", tokens);
    }

    [Fact]
    public void HasMeaningfulTokenOverlapAcceptsLongOrMultipleSharedTokens()
    {
        var longTokenLeft = ConversationContextPolicy.ExtractContextTokens("openai-sdk streaming");
        var longTokenRight = ConversationContextPolicy.ExtractContextTokens("openai-sdk retry");
        Assert.True(ConversationContextPolicy.HasMeaningfulTokenOverlap(longTokenLeft, longTokenRight));

        var shortLeft = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ab", "cd" };
        var shortRight = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ab", "cd" };
        Assert.True(ConversationContextPolicy.HasMeaningfulTokenOverlap(shortLeft, shortRight));

        var weakLeft = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ab" };
        var weakRight = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ab" };
        Assert.False(ConversationContextPolicy.HasMeaningfulTokenOverlap(weakLeft, weakRight));
    }

    [Theory]
    [InlineData("것", true)]
    [InlineData("openai", false)]
    [InlineData("what", true)]
    public void IsContextStopTokenUsesSharedStopList(string token, bool expected)
    {
        Assert.Equal(expected, ConversationContextPolicy.IsContextStopToken(token));
    }
}
