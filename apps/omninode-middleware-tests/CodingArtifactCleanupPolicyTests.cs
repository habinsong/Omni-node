using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingArtifactCleanupPolicyTests
{
    [Fact]
    public void CleanupRedundantSingleFileArtifactsReturnsNormalizedListWhenNotSingleFileIntent()
    {
        var cleaned = CodingArtifactCleanupPolicy.CleanupRedundantSingleFileArtifacts(
            "/tmp/work",
            "일반 작업",
            new[] { "app.py" },
            new[] { "", "/tmp/work/b.py", "/tmp/work/a.py", "/tmp/work/a.py" },
            ResolvePath
        );

        Assert.Equal(new[] { "/tmp/work/a.py", "/tmp/work/b.py" }, cleaned);
    }

    [Fact]
    public void CleanupRedundantSingleFileArtifactsDeletesGenericRootFallbackWithSameExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "omninode-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var requested = Path.Combine(root, "solution.py");
            var generic = Path.Combine(root, "main.py");
            File.WriteAllText(requested, "print('ok')");
            File.WriteAllText(generic, "print('fallback')");

            var cleaned = CodingArtifactCleanupPolicy.CleanupRedundantSingleFileArtifacts(
                root,
                "파일 하나만 solution.py로 만들어줘",
                new[] { "solution.py" },
                new[] { requested, generic },
                ResolvePath
            );

            Assert.Equal(new[] { requested }, cleaned);
            Assert.True(File.Exists(requested));
            Assert.False(File.Exists(generic));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CleanupRedundantSingleFileArtifactsKeepsNonGenericOrNestedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "omninode-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        try
        {
            var requested = Path.Combine(root, "solution.py");
            var nestedGeneric = Path.Combine(root, "src", "main.py");
            var rootDifferentExtension = Path.Combine(root, "main.js");
            File.WriteAllText(requested, "print('ok')");
            File.WriteAllText(nestedGeneric, "print('nested')");
            File.WriteAllText(rootDifferentExtension, "console.log('keep')");

            var cleaned = CodingArtifactCleanupPolicy.CleanupRedundantSingleFileArtifacts(
                root,
                "single file solution.py로 만들어줘",
                new[] { "solution.py" },
                new[] { requested, nestedGeneric, rootDifferentExtension },
                ResolvePath
            );

            Assert.Equal(
                new[] { rootDifferentExtension, nestedGeneric, requested }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                cleaned
            );
            Assert.True(File.Exists(nestedGeneric));
            Assert.True(File.Exists(rootDifferentExtension));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("main.py", true)]
    [InlineData("Program.cs", true)]
    [InlineData("custom.py", false)]
    public void IsGenericCodingFallbackFileNameDetectsKnownFallbackNames(string fileName, bool expected)
    {
        Assert.Equal(expected, CodingArtifactCleanupPolicy.IsGenericCodingFallbackFileName(fileName));
    }

    private static string ResolvePath(string workspaceRoot, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
    }
}
