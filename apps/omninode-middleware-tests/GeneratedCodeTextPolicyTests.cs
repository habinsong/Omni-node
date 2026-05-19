using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GeneratedCodeTextPolicyTests
{
    [Fact]
    public void NormalizeGeneratedFileContentRemovesSharedIndent()
    {
        var normalized = GeneratedCodeTextPolicy.NormalizeGeneratedFileContent(
            """
                def main():
                    print("ok")
            """
        );

        Assert.Equal(
            """
            def main():
                print("ok")
            """,
            normalized
        );
    }

    [Fact]
    public void NormalizeGeneratedFileContentNormalizesLineEndings()
    {
        var normalized = GeneratedCodeTextPolicy.NormalizeGeneratedFileContent("a\r\nb\rc");

        Assert.Equal("a\nb\nc", normalized);
    }

    [Fact]
    public void ExtractLanguagePrefixedPlainCodeUsesDeclaredLanguageAndPlainCode()
    {
        var parsed = GeneratedCodeTextPolicy.ExtractLanguagePrefixedPlainCode(
            """
            LANGUAGE=ts
            console.log("ok")
            """,
            "python"
        );

        Assert.Equal("typescript", parsed.Language);
        Assert.Equal("console.log(\"ok\")", parsed.Code);
    }

    [Fact]
    public void ExtractLanguagePrefixedPlainCodeUnwrapsCodeFence()
    {
        var parsed = GeneratedCodeTextPolicy.ExtractLanguagePrefixedPlainCode(
            """
            LANGUAGE=python
            ```python
            print("ok")
            ```
            """,
            "auto"
        );

        Assert.Equal("python", parsed.Language);
        Assert.Equal("print(\"ok\")", parsed.Code);
    }

    [Fact]
    public void ExtractLanguagePrefixedPlainCodeFallsBackWhenPrefixMissing()
    {
        var parsed = GeneratedCodeTextPolicy.ExtractLanguagePrefixedPlainCode("print('ok')", "py");

        Assert.Equal("python", parsed.Language);
        Assert.Equal(string.Empty, parsed.Code);
    }
}
