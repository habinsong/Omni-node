namespace OmniNode.Middleware;

public sealed class CommandExecutionService : ICommandExecutionService
{
    private readonly CommandService _inner;

    public CommandExecutionService(CommandService inner)
    {
        _inner = inner;
    }

    public Task<string> ExecuteAsync(
        string input,
        string source,
        CancellationToken cancellationToken,
        IReadOnlyList<InputAttachment>? attachments = null,
        IReadOnlyList<string>? webUrls = null,
        bool webSearchEnabled = true,
        Action<string>? streamCallback = null,
        TelegramTurnContext? telegramContext = null
    )
    {
        return _inner.ExecuteAsync(input, source, cancellationToken, attachments, webUrls, webSearchEnabled, streamCallback, telegramContext);
    }

    public TelegramExecutionMetadata GetCurrentTelegramExecutionMetadata()
    {
        return _inner.GetCurrentTelegramExecutionMetadata();
    }
}
