using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class ChatOutputSanitizerPolicyTests
{
    [Fact]
    public void SanitizeReturnsFallbackForBlankText()
    {
        var sanitized = ChatOutputSanitizerPolicy.Sanitize("   ");

        Assert.Equal("응답이 비어 있습니다. 다시 질문해 주세요.", sanitized);
    }

    [Fact]
    public void SanitizeRemovesThinkBlocksHtmlWrapperAndCopilotMetaLines()
    {
        var sanitized = ChatOutputSanitizerPolicy.Sanitize(
            """
            <think>숨김</think>
            <p>● 답변입니다.</p>
            현재 작업: fetch_copilot_cli_documentation
            """
        );

        Assert.Equal("답변입니다.", sanitized);
    }

    [Fact]
    public void SanitizeConvertsMarkdownTablesToListByDefault()
    {
        var sanitized = ChatOutputSanitizerPolicy.Sanitize(
            """
            | 이름 | 값 |
            | — | — |
            | A | 1 |
            """
        );

        Assert.Equal(
            """
            - 이름 / 값
            - A / 1
            """,
            sanitized
        );
    }

    [Fact]
    public void SanitizePreservesMarkdownTablesWhenRequested()
    {
        var sanitized = ChatOutputSanitizerPolicy.Sanitize(
            """
            | 이름 | 값 |
            | — | — |
            | A | 1 |
            """,
            keepMarkdownTables: true
        );

        Assert.Contains("| 이름 | 값 |", sanitized);
        Assert.Contains("| --- | --- |", sanitized);
        Assert.Contains("| A | 1 |", sanitized);
    }

    [Fact]
    public void SanitizeNormalizesSourceBlockAndHidesKnownNoisySource()
    {
        var sanitized = ChatOutputSanitizerPolicy.Sanitize(
            """
            출처:
            vietnam.vn
            Example News

            완료
            """
        );

        Assert.Contains("**출처:** Example News", sanitized);
        Assert.DoesNotContain("vietnam.vn", sanitized);
    }

    [Fact]
    public void NormalizeStructuredLabelBlocksFormatsPlainLabels()
    {
        var normalized = ChatOutputSanitizerPolicy.NormalizeStructuredLabelBlocks("요약: 테스트 결과");

        Assert.Equal("**요약:** 테스트 결과", normalized);
    }

    [Fact]
    public void IsStandaloneNumberedHeadlineLineDetectsHeadlineOnlyLines()
    {
        Assert.True(ChatOutputSanitizerPolicy.IsStandaloneNumberedHeadlineLine("**1. 모델 비교**"));
        Assert.False(ChatOutputSanitizerPolicy.IsStandaloneNumberedHeadlineLine("1. 핵심 기능입니다."));
    }

    [Fact]
    public void RemoveCodeBlocksFromTextHidesCommonCodeBlockForms()
    {
        var compact = ChatOutputSanitizerPolicy.RemoveCodeBlocksFromText(
            """
            설명

            ```js
            console.log('hidden');
            ```

            [code]
            hidden
            [next]

            <code>hidden html</code>
            """
        );

        Assert.Contains("[코드 블록 숨김]", compact);
        Assert.Contains("[code] (숨김)", compact);
        Assert.Contains("[코드 숨김]", compact);
        Assert.DoesNotContain("console.log", compact);
        Assert.DoesNotContain("hidden html", compact);
    }
}
