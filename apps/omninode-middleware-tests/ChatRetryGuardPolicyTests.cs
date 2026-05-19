using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ChatRetryGuardPolicyTests
{
    [Fact]
    public void ShouldRetryWithoutHistoryReturnsFalseForBlankInputs()
    {
        Assert.False(ChatRetryGuardPolicy.ShouldRetryWithoutHistory(string.Empty, "something"));
        Assert.False(ChatRetryGuardPolicy.ShouldRetryWithoutHistory("something", string.Empty));
        Assert.False(ChatRetryGuardPolicy.ShouldRetryWithoutHistory(null!, null!));
    }

    [Fact]
    public void ShouldRetryWithoutHistorySkipsNewsRequests()
    {
        Assert.False(ChatRetryGuardPolicy.ShouldRetryWithoutHistory("오늘 뉴스 5건", "주요 뉴스 모음"));
        Assert.False(ChatRetryGuardPolicy.ShouldRetryWithoutHistory("today news briefing", "no.1 제목"));
    }

    [Fact]
    public void ShouldRetryWithoutHistoryDetectsOffTopicGptOssAnswer()
    {
        var detected = ChatRetryGuardPolicy.ShouldRetryWithoutHistory(
            "gpt-oss 모델 설명해줘",
            "사과는 빨간 과일입니다."
        );

        Assert.True(detected);
    }

    [Fact]
    public void ShouldRetryWithoutHistoryDetectsUnrequestedP2SAnswer()
    {
        var detected = ChatRetryGuardPolicy.ShouldRetryWithoutHistory(
            "테니스 라켓 추천해줘",
            "P2S(Print-to-Shape) 기술은 3D 프린팅의 일종입니다."
        );

        Assert.True(detected);
    }

    [Fact]
    public void LooksLikeOffTopicModelExplanationRequiresGptOssInBothSides()
    {
        Assert.True(ChatRetryGuardPolicy.LooksLikeOffTopicModelExplanation(
            "gpt-oss 알려줘",
            "오늘 날씨는 맑음입니다."
        ));
        Assert.False(ChatRetryGuardPolicy.LooksLikeOffTopicModelExplanation(
            "gpt-oss 알려줘",
            "gpt-oss는 openai 오픈 웨이트 모델입니다."
        ));
        Assert.False(ChatRetryGuardPolicy.LooksLikeOffTopicModelExplanation(
            "다른 질문",
            "이건 무관한 답변"
        ));
    }

    [Fact]
    public void LooksLikeUnrequestedP2SAnswerSkipsWhenP2SIsExplicit()
    {
        Assert.True(ChatRetryGuardPolicy.LooksLikeUnrequestedP2SAnswer(
            "그냥 가격을 알려줘",
            "p2s 인쇄 기술은 비싸다."
        ));
        Assert.False(ChatRetryGuardPolicy.LooksLikeUnrequestedP2SAnswer(
            "p2s 인쇄 알려줘",
            "p2s 인쇄 기술은 비싸다."
        ));
    }

    [Fact]
    public void BuildOffTopicGuardMessageDifferentiatesBlankAndPresent()
    {
        Assert.Contains("다시 질문해", ChatRetryGuardPolicy.BuildOffTopicGuardMessage(string.Empty));
        Assert.Contains("원문 요청: gpt-oss 알려줘", ChatRetryGuardPolicy.BuildOffTopicGuardMessage("gpt-oss 알려줘"));
    }

    [Theory]
    [InlineData("찾아봐", true)]
    [InlineData("검색해줘", true)]
    [InlineData("look it up", true)]
    [InlineData("일반적인 긴 질문인데 약간 더 긴 문장이라서 vague 분류를 받지 않는다", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeVagueWebLookupRequestDetectsShortPhrasesOnly(string? input, bool expected)
    {
        Assert.Equal(expected, ChatRetryGuardPolicy.LooksLikeVagueWebLookupRequest(input!));
    }

    [Theory]
    [InlineData("", 4096)]
    [InlineData("자세히 설명해줘", 4096)]
    [InlineData("non-expert를 위한 가이드", 4096)]
    [InlineData("요약해줘", 2048)]
    [InlineData("compare A and B", 2048)]
    [InlineData("뭔가 평범한 짧은 질문", 3072)]
    public void ResolveSingleChatMaxOutputTokensReturnsExpectedBucket(string input, int expected)
    {
        Assert.Equal(expected, ChatRetryGuardPolicy.ResolveSingleChatMaxOutputTokens(input));
    }

    [Fact]
    public void ResolveSingleChatMaxOutputTokensListRequestPrefersListBucket()
    {
        var tokens = ChatRetryGuardPolicy.ResolveSingleChatMaxOutputTokens("관련 모델 10개를 리스트로 정리해줘");

        Assert.True(tokens == 3072 || tokens == 4096, $"unexpected bucket {tokens}");
    }

    [Fact]
    public void BuildHistoryBypassInputWrapsContentWithDirective()
    {
        var input = ChatRetryGuardPolicy.BuildHistoryBypassInput("사과 색깔 알려줘");

        Assert.Contains("[중요]", input);
        Assert.Contains("[새 요청]", input);
        Assert.Contains("사과 색깔 알려줘", input);
    }

    [Fact]
    public void BuildOriginalRequestRetryInputWrapsContentWithDirective()
    {
        var input = ChatRetryGuardPolicy.BuildOriginalRequestRetryInput("사과 색깔 알려줘");

        Assert.Contains("[중요]", input);
        Assert.Contains("[원문 요청]", input);
        Assert.Contains("사과 색깔 알려줘", input);
    }

    [Fact]
    public void BuildHistoryBypassAndOriginalRequestReturnEmptyForBlankInput()
    {
        Assert.Equal(string.Empty, ChatRetryGuardPolicy.BuildHistoryBypassInput(string.Empty));
        Assert.Equal(string.Empty, ChatRetryGuardPolicy.BuildHistoryBypassInput("   "));
        Assert.Equal(string.Empty, ChatRetryGuardPolicy.BuildOriginalRequestRetryInput(string.Empty));
    }
}
