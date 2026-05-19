using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class CodingLoopActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsyncWritesFileAndCreatesParentDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var result = await ExecuteAsync(
                new CodingLoopAction("write_file", "src/app.txt", "hello", string.Empty),
                root
            );

            var path = Path.Combine(root, "src", "app.txt");
            Assert.True(result.Changed);
            Assert.Equal("hello", await File.ReadAllTextAsync(path));
            Assert.Equal(path, result.ChangedPath);
            Assert.Equal("hello", result.CodePreview);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ExecuteAsyncSkipsMkdirWhenPathLooksLikeRequestedFile()
    {
        var root = CreateTempRoot();
        try
        {
            var result = await ExecuteAsync(
                new CodingLoopAction("mkdir", "app.py", string.Empty, string.Empty),
                root,
                requestedPaths: new[] { "app.py" }
            );

            Assert.False(result.Changed);
            Assert.StartsWith("mkdir_skipped_file_like:app.py", result.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "app.py")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ExecuteAsyncReadsExistingFilePreview()
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "readme.txt");
            await File.WriteAllTextAsync(path, "content");

            var result = await ExecuteAsync(
                new CodingLoopAction("read_file", "readme.txt", string.Empty, string.Empty),
                root
            );

            Assert.False(result.Changed);
            Assert.Equal("content", result.CodePreview);
            Assert.StartsWith("read:", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ExecuteAsyncBlocksDangerousRunCommandBeforeRunner()
    {
        var root = CreateTempRoot();
        var called = false;
        try
        {
            var result = await ExecuteAsync(
                new CodingLoopAction("run", string.Empty, string.Empty, "rm -rf workspace"),
                root,
                runner: (_, _, _) =>
                {
                    called = true;
                    return Task.FromResult(new CodingLoopShellResult(0, string.Empty, string.Empty, false));
                }
            );

            Assert.False(called);
            Assert.Null(result.Execution);
            Assert.StartsWith("run_blocked_unsafe:", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ExecuteAsyncRunsSafeCommandThroughRunner()
    {
        var root = CreateTempRoot();
        try
        {
            var result = await ExecuteAsync(
                new CodingLoopAction("run", string.Empty, string.Empty, "python3 app.py"),
                root,
                runner: (command, _, _) => Task.FromResult(new CodingLoopShellResult(
                    0,
                    $"ran:{command}",
                    string.Empty,
                    false
                ))
            );

            Assert.NotNull(result.Execution);
            Assert.Equal("ok", result.Execution.Status);
            Assert.Equal("ran:python3 app.py", result.Execution.StdOut);
            Assert.Equal("run:python3 app.py => ok", result.Message);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static Task<CodingLoopActionResult> ExecuteAsync(
        CodingLoopAction action,
        string root,
        IReadOnlyList<string>? requestedPaths = null,
        Func<string, string, CancellationToken, Task<CodingLoopShellResult>>? runner = null
    )
    {
        return CodingLoopActionExecutor.ExecuteAsync(
            action,
            root,
            requestedPaths ?? Array.Empty<string>(),
            "test",
            ResolveActionPath,
            ResolveWorkspacePath,
            (_, _, content) => content,
            runner ?? ((_, _, _) => Task.FromResult(new CodingLoopShellResult(0, string.Empty, string.Empty, false))),
            CancellationToken.None
        );
    }

    private static string? ResolveActionPath(
        string type,
        string? path,
        string? content,
        IReadOnlyList<string> requestedPaths,
        string workspaceRoot
    )
    {
        _ = type;
        _ = content;
        _ = requestedPaths;
        _ = workspaceRoot;
        return string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/').TrimStart('/');
    }

    private static string ResolveWorkspacePath(string root, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(root, relativePath));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "omninode-loop-action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
