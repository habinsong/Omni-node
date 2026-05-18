namespace OmniNode.Middleware;

public sealed class ExecutionContext
{
    private readonly AsyncLocal<TelegramExecutionMetadata?> _telegramExecutionMetadata = new();
    private readonly AsyncLocal<TelegramTurnContext?> _telegramTurnContext = new();

    public TelegramTurnContext? CurrentTelegramTurn
    {
        get => _telegramTurnContext.Value;
        set => _telegramTurnContext.Value = value;
    }

    public TelegramExecutionMetadata GetTelegramExecutionMetadata()
    {
        return _telegramExecutionMetadata.Value ?? new TelegramExecutionMetadata();
    }

    public void SetTelegramExecutionMetadata(
        SearchAnswerGuardFailure? guardFailure = null,
        int retryAttempt = 0,
        int retryMaxAttempts = 0,
        string? retryStopReason = "-"
    )
    {
        _telegramExecutionMetadata.Value = new TelegramExecutionMetadata(
            guardFailure,
            Math.Max(0, retryAttempt),
            Math.Max(0, retryMaxAttempts),
            string.IsNullOrWhiteSpace(retryStopReason) ? "-" : retryStopReason.Trim()
        );
    }
}
