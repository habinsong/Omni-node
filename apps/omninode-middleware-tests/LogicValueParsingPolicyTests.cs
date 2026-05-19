using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class LogicValueParsingPolicyTests
{
    [Fact]
    public void FirstNonEmptyTrimsAndReturnsFirstMeaningfulEntry()
    {
        Assert.Equal(string.Empty, LogicValueParsingPolicy.FirstNonEmpty());
        Assert.Equal(string.Empty, LogicValueParsingPolicy.FirstNonEmpty(null, "", "   "));
        Assert.Equal("b", LogicValueParsingPolicy.FirstNonEmpty(null, "  ", "b", "c"));
        Assert.Equal("trim me", LogicValueParsingPolicy.FirstNonEmpty("   trim me   "));
    }

    [Fact]
    public void ParseCsvValuesSplitsByCommaOrNewlineDedupCaseInsensitiveAndDropsBlank()
    {
        Assert.Null(LogicValueParsingPolicy.ParseCsvValues(null));
        Assert.Null(LogicValueParsingPolicy.ParseCsvValues("   "));
        Assert.Null(LogicValueParsingPolicy.ParseCsvValues(",,, ,\n"));

        var values = LogicValueParsingPolicy.ParseCsvValues("alpha,Beta\nGAMMA,beta")!;
        Assert.Equal(new[] { "alpha", "Beta", "GAMMA" }, values);
    }

    [Fact]
    public void ParseMultilineValuesReusesCsvSemantics()
    {
        var values = LogicValueParsingPolicy.ParseMultilineValues("one\ntwo\nONE")!;
        Assert.Equal(new[] { "one", "two" }, values);
    }

    [Theory]
    [InlineData("5", 0, 10, 5)]
    [InlineData("-3", 4, 10, 0)]
    [InlineData("999", 1, 10, 10)]
    [InlineData("garbage", 7, 10, 7)]
    [InlineData(null, 7, 10, 7)]
    public void ParsePositiveIntClampsAndFallsBack(string? raw, int fallback, int max, int expected)
    {
        Assert.Equal(expected, LogicValueParsingPolicy.ParsePositiveInt(raw, fallback, max));
    }

    [Theory]
    [InlineData("5", 10, 5)]
    [InlineData("100", 10, 10)]
    [InlineData("-1", 10, 0)]
    [InlineData("", 10, null)]
    [InlineData("notanint", 10, null)]
    public void ParseOptionalIntReturnsNullForBlankOrGarbage(string? raw, int max, int? expected)
    {
        Assert.Equal(expected, LogicValueParsingPolicy.ParseOptionalInt(raw, max));
    }

    [Theory]
    [InlineData("3.14", 0.0, 3.14)]
    [InlineData("not a number", 1.5, 1.5)]
    [InlineData(null, 2.0, 2.0)]
    public void ParseDoubleFallsBackOnGarbage(string? raw, double fallback, double expected)
    {
        Assert.Equal(expected, LogicValueParsingPolicy.ParseDouble(raw, fallback));
    }

    [Theory]
    [InlineData("1", false, true)]
    [InlineData("true", false, true)]
    [InlineData("YES", false, true)]
    [InlineData("on", false, true)]
    [InlineData("0", true, false)]
    [InlineData("nope", false, false)]
    [InlineData("", true, true)]
    [InlineData(null, false, false)]
    public void ParseBoolRecognizesCommonTruthyTokens(string? raw, bool fallback, bool expected)
    {
        Assert.Equal(expected, LogicValueParsingPolicy.ParseBool(raw, fallback));
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseOptionalBoolReturnsNullForBlank(string? raw, bool? expected)
    {
        Assert.Equal(expected, LogicValueParsingPolicy.ParseOptionalBool(raw));
    }

    [Theory]
    [InlineData("3", "5", -1)]
    [InlineData("5", "3", 1)]
    [InlineData("5", "5", 0)]
    [InlineData("garbage", "3", -1)]
    public void CompareNumbersHandlesNumericsAndFallsBackToZero(string left, string right, int expectedSign)
    {
        var result = LogicValueParsingPolicy.CompareNumbers(left, right);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Theory]
    [InlineData("a", "equals", "a", true)]
    [InlineData("a", "equals", "b", false)]
    [InlineData("a", "not_equals", "b", true)]
    [InlineData("ABCdef", "contains", "cDe", true)]
    [InlineData("abc", "not_contains", "z", true)]
    [InlineData("HelloWorld", "starts_with", "hello", true)]
    [InlineData("HelloWorld", "ends_with", "WORLD", true)]
    [InlineData("5", "gt", "3", true)]
    [InlineData("5", "gte", "5", true)]
    [InlineData("5", "lt", "10", true)]
    [InlineData("5", "lte", "5", true)]
    [InlineData("yes", "is_truthy", "", true)]
    [InlineData("no", "is_falsy", "", true)]
    public void EvaluateConditionMatchesNormalizedOperators(string left, string op, string right, bool expected)
    {
        Assert.Equal(expected, LogicValueParsingPolicy.EvaluateCondition(left, op, right));
    }

    [Fact]
    public void EvaluateConditionUsesOperatorAliasesFromValidationPolicy()
    {
        Assert.True(LogicValueParsingPolicy.EvaluateCondition("a", "==", "a"));
        Assert.True(LogicValueParsingPolicy.EvaluateCondition("a", "!=", "b"));
        Assert.True(LogicValueParsingPolicy.EvaluateCondition("5", ">=", "5"));
        Assert.True(LogicValueParsingPolicy.EvaluateCondition("abcdef", "startswith", "abc"));
        Assert.False(LogicValueParsingPolicy.EvaluateCondition("a", "unknown_op", "b"));
    }
}
