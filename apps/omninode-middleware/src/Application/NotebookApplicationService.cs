namespace OmniNode.Middleware;

public sealed class NotebookApplicationService : INotebookApplicationService
{
    private readonly CommandService _inner;

    public NotebookApplicationService(CommandService inner)
    {
        _inner = inner;
    }

    public Task<NotebookActionResult> GetNotebookAsync(string? projectKey, CancellationToken cancellationToken)
    {
        return _inner.GetNotebookAsync(projectKey, cancellationToken);
    }

    public Task<NotebookActionResult> AppendLearningAsync(string? projectKey, string content, CancellationToken cancellationToken)
    {
        return _inner.AppendLearningAsync(projectKey, content, cancellationToken);
    }

    public Task<NotebookActionResult> AppendDecisionAsync(string? projectKey, string content, CancellationToken cancellationToken)
    {
        return _inner.AppendDecisionAsync(projectKey, content, cancellationToken);
    }

    public Task<NotebookActionResult> AppendVerificationAsync(string? projectKey, string content, CancellationToken cancellationToken)
    {
        return _inner.AppendVerificationAsync(projectKey, content, cancellationToken);
    }

    public Task<NotebookActionResult> CreateHandoffAsync(string? projectKey, CancellationToken cancellationToken)
    {
        return _inner.CreateHandoffAsync(projectKey, cancellationToken);
    }

    public Task<string> BuildNotebookContextBlockAsync(string? projectKey, CancellationToken cancellationToken)
    {
        return _inner.BuildNotebookContextBlockAsync(projectKey, cancellationToken);
    }
}
