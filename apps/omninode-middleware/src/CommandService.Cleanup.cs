namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    public CleanupPreviewResult PreviewCleanup() => _cleanupService.PreviewCleanup();

    public CleanupApplyResult ApplyCleanup(string? previewId) => _cleanupService.ApplyCleanup(previewId);
}
