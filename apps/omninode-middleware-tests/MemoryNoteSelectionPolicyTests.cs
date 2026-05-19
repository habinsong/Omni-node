using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class MemoryNoteSelectionPolicyTests
{
    [Fact]
    public void NormalizeExplicitNamesTrimsAndDeduplicatesCaseInsensitively()
    {
        var names = MemoryNoteSelectionPolicy.NormalizeExplicitNames(new[] { " alpha ", "", "ALPHA", "beta" });

        Assert.Equal(new[] { "alpha", "beta" }, names);
    }

    [Fact]
    public void NormalizeExplicitNamesReturnsEmptyForNullOrEmptyInput()
    {
        Assert.Empty(MemoryNoteSelectionPolicy.NormalizeExplicitNames(null));
        Assert.Empty(MemoryNoteSelectionPolicy.NormalizeExplicitNames(Array.Empty<string>()));
    }

    [Fact]
    public void MergeNamesKeepsBaseThenRequestAndDeduplicates()
    {
        var names = MemoryNoteSelectionPolicy.MergeNames(
            new[] { " alpha ", "Beta" },
            new[] { "ALPHA", " gamma " }
        );

        Assert.Equal(new[] { "alpha", "Beta", "gamma" }, names);
    }

    [Fact]
    public void MergeNamesIgnoresBlankRequestNames()
    {
        var names = MemoryNoteSelectionPolicy.MergeNames(new[] { "base" }, new[] { "", "  " });

        Assert.Equal(new[] { "base" }, names);
    }
}
