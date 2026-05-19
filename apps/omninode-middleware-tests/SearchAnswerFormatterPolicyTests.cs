using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class SearchAnswerFormatterPolicyTests
{
    [Fact]
    public void NormalizeNumberedListResponseMergesDetachedNumbersAndRenumbers()
    {
        var normalized = SearchAnswerFormatterPolicy.NormalizeNumberedListResponse(
            """
            오늘 주요 뉴스입니다.
            2.
            두 번째 뉴스
            4. 네 번째 뉴스
            출처: Reuters
            """
        );

        Assert.Contains("오늘 주요 뉴스입니다.", normalized);
        Assert.Contains("1. 두 번째 뉴스", normalized);
        Assert.Contains("2. 네 번째 뉴스", normalized);
        Assert.Contains("출처: Reuters", normalized);
    }

    [Fact]
    public void ConvertDelimitedPlainTextTableToMarkdownBuildsMarkdownTable()
    {
        var normalized = SearchAnswerFormatterPolicy.ConvertDelimitedPlainTextTableToMarkdown(
            """
            국가  수도  대륙
            한국  서울  아시아
            프랑스  파리  유럽
            """
        );

        Assert.Contains("| 국가 | 수도 | 대륙 |", normalized);
        Assert.Contains("| --- | --- | --- |", normalized);
        Assert.Contains("| 한국 | 서울 | 아시아 |", normalized);
    }

    [Fact]
    public void NormalizeMarkdownTableResponseMetadataMovesVisibleSourcesOutOfTable()
    {
        var normalized = SearchAnswerFormatterPolicy.NormalizeMarkdownTableResponseMetadata(
            """
            | 제목 | 출처 |
            | --- | --- |
            | 첫 뉴스 | Reuters |
            | 둘째 뉴스 | vietnam.vn |
            """
        );

        Assert.Contains("| 제목 |", normalized);
        Assert.DoesNotContain("| 제목 | 출처 |", normalized);
        Assert.Contains("출처: Reuters", normalized);
        Assert.DoesNotContain("vietnam.vn", normalized);
    }

    [Fact]
    public void RemoveSourceLinkArtifactsDropsRawUrlsAndKeepsVisibleSourceName()
    {
        var normalized = SearchAnswerFormatterPolicy.RemoveSourceLinkArtifacts(
            """
            요약입니다.
            출처 링크: https://example.com/news
            https://example.com/news
            출처: Reuters https://example.com/news
            """
        );

        Assert.Contains("요약입니다.", normalized);
        Assert.Contains("출처: Reuters", normalized);
        Assert.DoesNotContain("https://example.com/news", normalized);
        Assert.DoesNotContain("출처 링크", normalized);
    }

    [Fact]
    public void NormalizeNarrativeParagraphsMergesNarrativeAndFormatsLabels()
    {
        var normalized = SearchAnswerFormatterPolicy.NormalizeNarrativeParagraphs(
            """
            첫 문장입니다.
            둘째 문장입니다.
            핵심: 확인된 내용
            """
        );

        Assert.Contains("첫 문장입니다. 둘째 문장입니다.", normalized);
        Assert.Contains("**핵심:** 확인된 내용", normalized);
    }

    [Fact]
    public void EnsureReadableWebAnswerReturnsEmptyForBlankInput()
    {
        Assert.Equal(string.Empty, SearchAnswerFormatterPolicy.EnsureReadableWebAnswerResponse("   ", "질문", allowMarkdownTable: true));
        Assert.Equal(string.Empty, SearchAnswerFormatterPolicy.EnsureReadableWebAnswerResponse(string.Empty, "질문", allowMarkdownTable: true));
    }

    [Fact]
    public void EnsureReadableWebAnswerNormalizesNumberedListWhenInputAsksForList()
    {
        var raw = """
            오늘 주요 뉴스입니다.
            1. 첫 번째 뉴스
            2.
            두 번째 뉴스
            """;
        var normalized = SearchAnswerFormatterPolicy.EnsureReadableWebAnswerResponse(raw, "오늘 뉴스 5건 목록으로 보여줘", allowMarkdownTable: false);

        Assert.Contains("1.", normalized);
        Assert.Contains("두 번째 뉴스", normalized);
    }

    [Fact]
    public void EnsureReadableWebAnswerKeepsRawComparisonWhenInputAsksForComparison()
    {
        var raw = "옵션 A는 빠르다. 옵션 B는 안정적이다.";
        var normalized = SearchAnswerFormatterPolicy.EnsureReadableWebAnswerResponse(raw, "두 옵션 비교해줘", allowMarkdownTable: false);

        Assert.Contains("옵션 A", normalized);
        Assert.Contains("옵션 B", normalized);
    }

    [Fact]
    public void EnsureReadableWebAnswerSkipsTableModeWhenNotAllowed()
    {
        var raw = """
            제목 | 요약 | 출처
            기사1 | 본문1 | Reuters
            """;
        var normalized = SearchAnswerFormatterPolicy.EnsureReadableWebAnswerResponse(raw, "표로 보여줘", allowMarkdownTable: false);

        Assert.DoesNotContain("|---|", normalized);
    }
}
