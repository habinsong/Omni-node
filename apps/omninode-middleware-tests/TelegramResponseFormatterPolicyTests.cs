using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class TelegramResponseFormatterPolicyTests
{
    [Fact]
    public void ConvertMarkdownToPlainTextKeepsReadableHeadingsLinksAndCodeFences()
    {
        var normalized = TelegramResponseFormatterPolicy.ConvertMarkdownToPlainText(
            """
            ## 핵심
            - [문서](https://example.com/docs)를 확인하세요.

            ```csharp
            Console.WriteLine("ok");
            ```
            """
        );

        Assert.Contains("핵심", normalized);
        Assert.Contains("- 문서 (https://example.com/docs)를 확인하세요.", normalized);
        Assert.Contains("[코드]", normalized);
        Assert.Contains("""Console.WriteLine("ok");""", normalized);
    }

    [Fact]
    public void ConvertMarkdownToPlainTextPreservesMarkdownTablesWhenRequested()
    {
        var normalized = TelegramResponseFormatterPolicy.ConvertMarkdownToPlainText(
            """
            | 이름 | 값 |
            | --- | --- |
            | **Alpha** | `42` |
            """,
            keepMarkdownTables: true
        );

        Assert.Contains("| 이름 | 값 |", normalized);
        Assert.Contains("| --- | --- |", normalized);
        Assert.Contains("| Alpha | 42 |", normalized);
    }

    [Fact]
    public void FormatSanitizedResponseUsesCharacterLimitAndTruncationMarker()
    {
        var normalized = TelegramResponseFormatterPolicy.FormatSanitizedResponse(
            "1234567890 1234567890",
            maxChars: 10
        );

        Assert.StartsWith("1234567890", normalized);
        Assert.Contains("telegram_response_truncated", normalized);
    }

    [Fact]
    public void FormatSanitizedResponseNormalizesDetachedNumberLines()
    {
        var normalized = TelegramResponseFormatterPolicy.FormatSanitizedResponse(
            """
            1.
            첫 번째 항목

            2.
            두 번째 항목
            """,
            maxChars: 0
        );

        Assert.Contains("1. 첫 번째 항목", normalized);
        Assert.Contains("2. 두 번째 항목", normalized);
    }
}
