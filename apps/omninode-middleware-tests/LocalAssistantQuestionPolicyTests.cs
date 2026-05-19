using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class LocalAssistantQuestionPolicyTests
{
    [Theory]
    [InlineData("어떤 스킬 있어?", "어떤스킬있어", true)]
    [InlineData("스킬 목록 보여줘", "스킬목록보여줘", true)]
    [InlineData("skill list 보여줘", "skilllist보여줘", true)]
    [InlineData("스킬뭐있어", "스킬뭐있어", true)]
    [InlineData("eli5 스킬을 사용해서 디지털 카메라 원리 말해줘", "eli5스킬을사용해서디지털카메라원리말해줘", false)]
    [InlineData("그냥 일반 질문", "그냥일반질문", false)]
    public void LooksLikeSkillInventoryQuestionMatchesInventoryPatterns(string normalized, string compact, bool expected)
    {
        Assert.Equal(expected, LocalAssistantQuestionPolicy.LooksLikeSkillInventoryQuestion(normalized, compact));
    }

    [Theory]
    [InlineData("너는 뭘 못 해?", "너는뭘못해", true)]
    [InlineData("ai 한계 알려줘", "ai한계알려줘", true)]
    [InlineData("당신이 cannot do?", "당신이cannot do", true)]
    [InlineData("이걸 못 하는 사람이 누구야?", "이걸못하는사람이누구야", false)]
    [InlineData("오늘 뉴스 알려줘", "오늘뉴스알려줘", false)]
    public void LooksLikeLimitationQuestionRequiresAssistantSubject(string normalized, string compact, bool expected)
    {
        Assert.Equal(expected, LocalAssistantQuestionPolicy.LooksLikeLimitationQuestion(normalized, compact));
    }

    [Theory]
    [InlineData("너 뭘 할 수 있어?", "너뭘할수있어", true)]
    [InlineData("ai 기능 뭐야", "ai기능뭐야", true)]
    [InlineData("당신의 능력은?", "당신의능력은", true)]
    [InlineData("내가 할 수 있는 일", "내가할수있는일", false)]
    public void LooksLikeCapabilityQuestionRequiresAssistantSubject(string normalized, string compact, bool expected)
    {
        Assert.Equal(expected, LocalAssistantQuestionPolicy.LooksLikeCapabilityQuestion(normalized, compact));
    }

    [Theory]
    [InlineData("너는 누구야?", "너는누구야", true)]
    [InlineData("who are you", "who are you", true)]
    [InlineData("정체가 뭐야?", "정체가뭐야", true)]
    [InlineData("자기소개 해줘", "자기소개해줘", true)]
    [InlineData("그가 누구야", "그가누구야", false)]
    public void LooksLikeIdentityQuestionMatchesIdentityCues(string normalized, string compact, bool expected)
    {
        Assert.Equal(expected, LocalAssistantQuestionPolicy.LooksLikeIdentityQuestion(normalized, compact));
    }

    [Fact]
    public void IndexOfSkillNameWithBoundaryRequiresBoundaryAroundMatch()
    {
        Assert.Equal(0, LocalAssistantQuestionPolicy.IndexOfSkillNameWithBoundary("ai 무엇이든 알려줘", "ai"));
        Assert.Equal(-1, LocalAssistantQuestionPolicy.IndexOfSkillNameWithBoundary("aim me 도와줘", "ai"));
        Assert.True(LocalAssistantQuestionPolicy.IndexOfSkillNameWithBoundary("코드를 개선해줘 eli5", "eli5") >= 0);
        Assert.Equal(-1, LocalAssistantQuestionPolicy.IndexOfSkillNameWithBoundary(string.Empty, "ai"));
        Assert.Equal(-1, LocalAssistantQuestionPolicy.IndexOfSkillNameWithBoundary("ai", "  "));
    }

    [Fact]
    public void IndexOfSkillNameWithBoundarySkipsPartialMatchAndContinuesSearching()
    {
        var idx = LocalAssistantQuestionPolicy.IndexOfSkillNameWithBoundary("aim ai 둘 다", "ai");

        Assert.Equal(4, idx);
    }

    [Theory]
    [InlineData('-', true)]
    [InlineData('_', true)]
    [InlineData('a', true)]
    [InlineData('Z', true)]
    [InlineData('5', true)]
    [InlineData(' ', false)]
    [InlineData('가', false)]
    [InlineData('.', false)]
    public void IsSkillNameBoundaryInsideClassifiesAscii(char c, bool expected)
    {
        Assert.Equal(expected, LocalAssistantQuestionPolicy.IsSkillNameBoundaryInside(c));
    }

    [Fact]
    public void TrimAssistantInfoTextNormalizesWhitespaceAndFallbacksOnEmpty()
    {
        Assert.Equal("설명이 없습니다.", LocalAssistantQuestionPolicy.TrimAssistantInfoText("   ", 50));
        Assert.Equal("hello world", LocalAssistantQuestionPolicy.TrimAssistantInfoText("hello    world", 50));
    }

    [Fact]
    public void TrimAssistantInfoTextTruncatesWithEllipsis()
    {
        var raw = new string('a', 100);

        var trimmed = LocalAssistantQuestionPolicy.TrimAssistantInfoText(raw, 10);

        Assert.Equal(new string('a', 10) + "...", trimmed);
    }

    [Fact]
    public void TrimToUtf8ByteCountKeepsValueWhenWithinLimit()
    {
        Assert.Equal("hello", LocalAssistantQuestionPolicy.TrimToUtf8ByteCount("hello", 100));
        Assert.Equal(string.Empty, LocalAssistantQuestionPolicy.TrimToUtf8ByteCount(string.Empty, 100));
        Assert.Equal(string.Empty, LocalAssistantQuestionPolicy.TrimToUtf8ByteCount("hello", 0));
    }

    [Fact]
    public void TrimToUtf8ByteCountTruncatesAtRuneBoundary()
    {
        // 한 -> 3 bytes in utf-8. limit=4 means one 한 + drop second.
        var trimmed = LocalAssistantQuestionPolicy.TrimToUtf8ByteCount("한글", 4);

        Assert.Equal("한", trimmed);
    }
}
