namespace OmniNode.Middleware;

public sealed class NotebookApplicationService : INotebookApplicationService
{
    private readonly NotebookService _notebookService;

    public NotebookApplicationService(NotebookService notebookService)
    {
        _notebookService = notebookService;
    }

    public Task<NotebookActionResult> GetNotebookAsync(string? projectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new NotebookActionResult(
            true,
            "노트북을 불러왔습니다.",
            _notebookService.GetNotebook(projectKey)
        ));
    }

    public Task<NotebookActionResult> AppendLearningAsync(string? projectKey, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_notebookService.AppendEntry(projectKey, "learning", content));
    }

    public Task<NotebookActionResult> AppendDecisionAsync(string? projectKey, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_notebookService.AppendEntry(projectKey, "decision", content));
    }

    public Task<NotebookActionResult> AppendVerificationAsync(string? projectKey, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_notebookService.AppendEntry(projectKey, "verification", content));
    }

    public Task<NotebookActionResult> CreateHandoffAsync(string? projectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_notebookService.CreateHandoff(projectKey));
    }

    public Task<string> BuildNotebookContextBlockAsync(string? projectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_notebookService.BuildContextBlock(projectKey));
    }
}
