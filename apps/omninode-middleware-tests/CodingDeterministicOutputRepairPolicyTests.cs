using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingDeterministicOutputRepairPolicyTests
{
    [Fact]
    public void ShouldTrySingleFileOutputRepairRequiresOnePythonTargetAndExpectedOutput()
    {
        Assert.True(CodingDeterministicOutputRepairPolicy.ShouldTrySingleFileOutputRepair("auto", new[] { "main.py" }, "ok"));
        Assert.True(CodingDeterministicOutputRepairPolicy.ShouldTrySingleFileOutputRepair("python", new[] { "script.txt" }, "ok"));
        Assert.False(CodingDeterministicOutputRepairPolicy.ShouldTrySingleFileOutputRepair("auto", new[] { "main.js" }, "ok"));
        Assert.False(CodingDeterministicOutputRepairPolicy.ShouldTrySingleFileOutputRepair("python", new[] { "a.py", "b.py" }, "ok"));
        Assert.False(CodingDeterministicOutputRepairPolicy.ShouldTrySingleFileOutputRepair("python", new[] { "main.py" }, ""));
    }

    [Fact]
    public void BuildPythonStringLiteralEscapesPythonStringCharacters()
    {
        var literal = CodingDeterministicOutputRepairPolicy.BuildPythonStringLiteral("a\"b\\c\n\t\u0001");

        Assert.Equal("\"a\\\"b\\\\c\\n\\t\\u0001\"", literal);
    }

    [Fact]
    public void BuildPythonPrintCodeWrapsExpectedOutput()
    {
        var code = CodingDeterministicOutputRepairPolicy.BuildPythonPrintCode("hello");

        Assert.Equal("print(\"hello\")\n", code);
    }
}
