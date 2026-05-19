using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingExpectedOutputPolicyTests
{
    [Fact]
    public void ExtractExpectedConsoleOutputLinesKeepsExplicitFirstAndSecondLineOrder()
    {
        var lines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(
            """
            첫 줄은 "alpha"
            둘째 줄은 "beta"
            """
        );

        Assert.Equal(new[] { "alpha", "beta" }, lines);
    }

    [Fact]
    public void ExtractExpectedConsoleOutputLinesParsesInlineOrderedLabels()
    {
        var lines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(
            "first line should be `ready` and second line should be `done`"
        );

        Assert.Equal(new[] { "ready", "done" }, lines);
    }

    [Fact]
    public void ExtractExpectedConsoleOutputLinesFallsBackToStdoutLineQuotedLiterals()
    {
        var lines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(
            "stdout에는 \"token\"과 \"checksum\"이 나와야 한다"
        );

        Assert.Equal(new[] { "token", "checksum" }, lines);
    }

    [Fact]
    public void ExtractExpectedConsoleOutputLinesUsesLastNewRequestBlock()
    {
        var lines = CodingExpectedOutputPolicy.ExtractExpectedConsoleOutputLines(
            """
            [최근 대화]
            stdout "old"

            [새 요청]
            stdout "new"
            [로컬 시간]
            now
            """
        );

        Assert.Equal(new[] { "new" }, lines);
    }

    [Fact]
    public void ExtractVisibleTextRequirementLiteralsReadsVisibleTextLine()
    {
        var lines = CodingExpectedOutputPolicy.ExtractVisibleTextRequirementLiterals(
            """
            visible text "Dashboard Ready"를 보여줘
            stdout "ignored"
            """
        );

        Assert.Equal(new[] { "Dashboard Ready" }, lines);
    }

    [Theory]
    [InlineData("첫 줄", 0)]
    [InlineData("second line", 1)]
    [InlineData("세번째 줄", -1)]
    public void ResolveExpectedOutputLineIndexMapsKnownLabels(string label, int expected)
    {
        Assert.Equal(expected, CodingExpectedOutputPolicy.ResolveExpectedOutputLineIndex(label));
    }
}
