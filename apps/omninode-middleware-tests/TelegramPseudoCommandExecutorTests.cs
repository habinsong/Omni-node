using OmniNode.Middleware.Infrastructure.Telegram;

namespace OmniNode.Middleware.Tests;

public sealed class TelegramPseudoCommandExecutorTests
{
    [Fact]
    public async Task ExecuteAsyncRoutesHelpWithoutCallingHandlers()
    {
        var result = await TelegramPseudoCommandExecutor.ExecuteAsync(
            new TelegramPseudoCommandRequest("/help coding", null, null, false),
            BuildHandlers(command => Task.FromResult<string?>($"unexpected:{command}")) with
            {
                ParseHelpTopic = command => command.Contains("coding", StringComparison.OrdinalIgnoreCase) ? "coding" : string.Empty,
                BuildHelpText = topic => $"help:{topic}"
            },
            CancellationToken.None
        );

        Assert.Equal("help:coding", result);
    }

    [Fact]
    public async Task ExecuteAsyncRoutesCodingWithAttachmentsAndWebContext()
    {
        var attachment = new InputAttachment("a.txt", "text/plain", "abc", 3);
        IReadOnlyList<InputAttachment>? seenAttachments = null;
        IReadOnlyList<string>? seenUrls = null;
        var seenWeb = false;

        var handlers = BuildHandlers(command => Task.FromResult<string?>($"unexpected:{command}")) with
        {
            HandleCodingAsync = (command, attachments, webUrls, webSearchEnabled, _) =>
            {
                seenAttachments = attachments;
                seenUrls = webUrls;
                seenWeb = webSearchEnabled;
                return Task.FromResult<string?>($"coding:{command}");
            }
        };

        var result = await TelegramPseudoCommandExecutor.ExecuteAsync(
            new TelegramPseudoCommandRequest("/coding run build", new[] { attachment }, new[] { "https://example.com" }, true),
            handlers,
            CancellationToken.None
        );

        Assert.Equal("coding:/coding run build", result);
        Assert.Same(attachment, Assert.Single(seenAttachments!));
        Assert.Equal("https://example.com", Assert.Single(seenUrls!));
        Assert.True(seenWeb);
    }

    [Theory]
    [InlineData("/routines", "routine:/routines")]
    [InlineData("/handoff", "notebook:/handoff")]
    [InlineData("/metrics", "metrics:/metrics")]
    [InlineData("/kill 123", "kill:/kill 123")]
    public async Task ExecuteAsyncRoutesAliasesAndRuntimeCommands(string command, string expected)
    {
        var handlers = BuildHandlers(input => Task.FromResult<string?>($"unexpected:{input}")) with
        {
            HandleRoutineAsync = (input, _) => Task.FromResult<string?>($"routine:{input}"),
            HandleNotebookAsync = (input, _) => Task.FromResult<string?>($"notebook:{input}"),
            HandleMetricsAsync = (input, _) => Task.FromResult<string?>($"metrics:{input}"),
            HandleKillAsync = (input, _) => Task.FromResult<string?>($"kill:{input}")
        };

        var result = await TelegramPseudoCommandExecutor.ExecuteAsync(
            new TelegramPseudoCommandRequest(command, null, null, false),
            handlers,
            CancellationToken.None
        );

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteAsyncReturnsNullForUnknownCommand()
    {
        var result = await TelegramPseudoCommandExecutor.ExecuteAsync(
            new TelegramPseudoCommandRequest("/unknown", null, null, false),
            BuildHandlers(command => Task.FromResult<string?>($"unexpected:{command}")),
            CancellationToken.None
        );

        Assert.Null(result);
    }

    private static TelegramPseudoCommandHandlers BuildHandlers(Func<string, Task<string?>> fallback)
    {
        Task<string?> Simple(string command, CancellationToken _) => fallback(command);

        return new TelegramPseudoCommandHandlers(
            ParseHelpTopic: _ => string.Empty,
            BuildHelpText: topic => $"help:{topic}",
            HandleProfileAsync: Simple,
            HandleQuickModelAsync: Simple,
            HandleLlmControlAsync: Simple,
            HandleSkillAsync: Simple,
            HandleCodingAsync: (command, _, _, _, _) => fallback(command),
            HandleRefactorAsync: Simple,
            HandleMemoryAsync: Simple,
            HandleDoctorAsync: Simple,
            HandlePlanAsync: Simple,
            HandleTaskAsync: Simple,
            HandleNotebookAsync: Simple,
            HandleRoutineAsync: Simple,
            HandleMetricsAsync: Simple,
            HandleKillAsync: Simple
        );
    }
}
