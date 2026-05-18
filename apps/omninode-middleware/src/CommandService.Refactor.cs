namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public Task<RefactorActionResult> ReadWithAnchorsAsync(string path, CancellationToken cancellationToken)
        => _refactorAppService.ReadWithAnchorsAsync(path, cancellationToken);

    public Task<RefactorActionResult> PreviewRefactorAsync(
        string path,
        IReadOnlyList<AnchorEditRequest>? edits,
        CancellationToken cancellationToken
    ) => _refactorAppService.PreviewRefactorAsync(path, edits, cancellationToken);

    public Task<RefactorActionResult> ApplyRefactorAsync(string previewId, CancellationToken cancellationToken)
        => _refactorAppService.ApplyRefactorAsync(previewId, cancellationToken);

    public Task<RefactorActionResult> RunLspRenameAsync(
        string path,
        string symbol,
        string newName,
        CancellationToken cancellationToken
    ) => _refactorAppService.RunLspRenameAsync(path, symbol, newName, cancellationToken);

    public Task<RefactorActionResult> RunAstReplaceAsync(
        string path,
        string pattern,
        string replacement,
        CancellationToken cancellationToken
    ) => _refactorAppService.RunAstReplaceAsync(path, pattern, replacement, cancellationToken);
}
