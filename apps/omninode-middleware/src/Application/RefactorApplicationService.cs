using System.Text;

namespace OmniNode.Middleware;

public sealed class RefactorApplicationService : IRefactorApplicationService
{
    private readonly AnchorReadService _anchorReadService;
    private readonly AnchorEditService _anchorEditService;
    private readonly DiffPreviewService _diffPreviewService;
    private readonly LspRefactorService _lspRefactorService;
    private readonly AstGrepRefactorService _astGrepRefactorService;
    private readonly AuditLogger _auditLogger;
    private readonly PathOptions _paths;

    public RefactorApplicationService(
        AnchorReadService anchorReadService,
        AnchorEditService anchorEditService,
        DiffPreviewService diffPreviewService,
        LspRefactorService lspRefactorService,
        AstGrepRefactorService astGrepRefactorService,
        AuditLogger auditLogger,
        PathOptions paths
    )
    {
        _anchorReadService = anchorReadService;
        _anchorEditService = anchorEditService;
        _diffPreviewService = diffPreviewService;
        _lspRefactorService = lspRefactorService;
        _astGrepRefactorService = astGrepRefactorService;
        _auditLogger = auditLogger;
        _paths = paths;
    }

    public async Task<RefactorActionResult> ReadWithAnchorsAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var readResult = await _anchorReadService.ReadWithAnchorsAsync(path, cancellationToken);
            _auditLogger.Log(
                "web",
                "refactor_read",
                "ok",
                $"path={NormalizeAuditToken(readResult.Path, "-")} lines={readResult.Lines.Count}"
            );
            return new RefactorActionResult(
                true,
                readResult.Truncated ? "anchor를 읽었습니다. 일부 line만 표시합니다." : "anchor를 읽었습니다.",
                ReadResult: readResult
            );
        }
        catch (Exception ex)
        {
            _auditLogger.Log(
                "web",
                "refactor_read",
                "error",
                $"path={NormalizeAuditToken(path, "-")} error={NormalizeAuditToken(ex.Message, "-")}"
            );
            return new RefactorActionResult(false, ex.Message);
        }
    }

    public async Task<RefactorActionResult> PreviewRefactorAsync(
        string path,
        IReadOnlyList<AnchorEditRequest>? edits,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var fullPath = _anchorReadService.ResolveWorkspacePath(path);
            if (!File.Exists(fullPath))
            {
                return new RefactorActionResult(false, "파일을 찾을 수 없습니다.");
            }

            var originalText = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var computation = _anchorEditService.ComputePreview(fullPath, originalText, edits);
            if (!computation.Ok)
            {
                return new RefactorActionResult(
                    false,
                    "anchor mismatch 또는 edit 검증 실패로 preview를 만들지 못했습니다.",
                    Issues: computation.Issues
                );
            }

            if (string.Equals(originalText, computation.UpdatedText, StringComparison.Ordinal))
            {
                return new RefactorActionResult(false, "변경점이 없습니다.");
            }

            var preview = await _diffPreviewService.CreatePreviewAsync(
                fullPath,
                originalText,
                computation.UpdatedText,
                computation.Edits,
                cancellationToken
            );
            _auditLogger.Log(
                "web",
                "refactor_preview",
                "ok",
                $"path={NormalizeAuditToken(fullPath, "-")} previewId={NormalizeAuditToken(preview.PreviewId, "-")}"
            );
            return new RefactorActionResult(true, "preview를 만들었습니다.", Preview: preview);
        }
        catch (Exception ex)
        {
            _auditLogger.Log(
                "web",
                "refactor_preview",
                "error",
                $"path={NormalizeAuditToken(path, "-")} error={NormalizeAuditToken(ex.Message, "-")}"
            );
            return new RefactorActionResult(false, ex.Message);
        }
    }

    public async Task<RefactorActionResult> ApplyRefactorAsync(
        string previewId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var normalizedPreviewId = (previewId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPreviewId))
            {
                return new RefactorActionResult(false, "previewId가 필요합니다.");
            }

            var previewRecord = await _diffPreviewService.GetPreviewAsync(normalizedPreviewId, cancellationToken);
            if (previewRecord == null)
            {
                return new RefactorActionResult(false, "preview를 찾을 수 없습니다. 만료되었거나 이미 적용되었을 수 있습니다.");
            }

            if (previewRecord.Files is { Count: > 0 })
            {
                return await ApplyStructuredPreviewAsync(previewRecord, cancellationToken);
            }

            if (!File.Exists(previewRecord.Path))
            {
                return new RefactorActionResult(false, "적용 대상 파일을 찾을 수 없습니다.");
            }

            var currentText = await File.ReadAllTextAsync(previewRecord.Path, cancellationToken);
            var computation = _anchorEditService.ComputePreview(
                previewRecord.Path,
                currentText,
                previewRecord.Edits
            );
            if (!computation.Ok)
            {
                return new RefactorActionResult(
                    false,
                    "파일이 바뀌어 apply를 차단했습니다. 다시 읽고 preview를 새로 만드세요.",
                    ApplyResult: new RefactorApplyOutcome(
                        previewRecord.PreviewId,
                        previewRecord.Path,
                        false,
                        DateTimeOffset.UtcNow.ToString("O"),
                        computation.Issues
                    ),
                    Issues: computation.Issues
                );
            }

            await File.WriteAllTextAsync(
                previewRecord.Path,
                previewRecord.UpdatedText,
                new UTF8Encoding(false),
                cancellationToken
            );
            _diffPreviewService.DeletePreview(previewRecord.PreviewId);
            var applyResult = new RefactorApplyOutcome(
                previewRecord.PreviewId,
                previewRecord.Path,
                true,
                DateTimeOffset.UtcNow.ToString("O"),
                Array.Empty<AnchorEditIssue>()
            );
            _auditLogger.Log(
                "web",
                "refactor_apply",
                "ok",
                $"path={NormalizeAuditToken(previewRecord.Path, "-")} previewId={NormalizeAuditToken(previewRecord.PreviewId, "-")}"
            );
            return new RefactorActionResult(true, "preview를 파일에 적용했습니다.", ApplyResult: applyResult);
        }
        catch (Exception ex)
        {
            _auditLogger.Log(
                "web",
                "refactor_apply",
                "error",
                $"previewId={NormalizeAuditToken(previewId, "-")} error={NormalizeAuditToken(ex.Message, "-")}"
            );
            return new RefactorActionResult(false, ex.Message);
        }
    }

    public Task<RefactorActionResult> RunLspRenameAsync(
        string path,
        string symbol,
        string newName,
        CancellationToken cancellationToken
    ) => _lspRefactorService.RunRenameAsync(path, symbol, newName, cancellationToken);

    public Task<RefactorActionResult> RunAstReplaceAsync(
        string path,
        string pattern,
        string replacement,
        CancellationToken cancellationToken
    ) => _astGrepRefactorService.RunReplaceAsync(path, pattern, replacement, cancellationToken);

    private async Task<RefactorActionResult> ApplyStructuredPreviewAsync(
        RefactorPreviewRecord previewRecord,
        CancellationToken cancellationToken
    )
    {
        var previewFiles = previewRecord.Files ?? Array.Empty<RefactorPreviewFile>();
        var issues = new List<AnchorEditIssue>();
        foreach (var previewFile in previewFiles)
        {
            if (!File.Exists(previewFile.Path))
            {
                issues.Add(new AnchorEditIssue(
                    0,
                    0,
                    "적용 대상 파일을 찾을 수 없습니다.",
                    new[] { previewFile.OriginalHash },
                    Array.Empty<string>(),
                    string.Empty
                ));
                continue;
            }

            var currentText = await File.ReadAllTextAsync(previewFile.Path, cancellationToken);
            var currentHash = DiffPreviewService.ComputeTextHash(currentText);
            if (!string.Equals(currentHash, previewFile.OriginalHash, StringComparison.Ordinal))
            {
                issues.Add(new AnchorEditIssue(
                    0,
                    0,
                    "파일이 바뀌어 apply를 차단했습니다. 다시 preview를 새로 만드세요.",
                    new[] { previewFile.OriginalHash },
                    new[] { currentHash },
                    BuildSnippet(currentText)
                ));
            }
        }

        var changedPaths = previewFiles
            .Select(file => file.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (issues.Count > 0)
        {
            return new RefactorActionResult(
                false,
                "파일이 바뀌어 apply를 차단했습니다. 다시 preview를 새로 만드세요.",
                ApplyResult: new RefactorApplyOutcome(
                    previewRecord.PreviewId,
                    previewRecord.Path,
                    false,
                    DateTimeOffset.UtcNow.ToString("O"),
                    issues,
                    changedPaths
                ),
                Issues: issues
            );
        }

        foreach (var previewFile in previewFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(previewFile.Path) ?? _paths.WorkspaceRootDir);
            await File.WriteAllTextAsync(
                previewFile.Path,
                previewFile.UpdatedText,
                new UTF8Encoding(false),
                cancellationToken
            );
        }

        _diffPreviewService.DeletePreview(previewRecord.PreviewId);
        var applyResult = new RefactorApplyOutcome(
            previewRecord.PreviewId,
            previewRecord.Path,
            true,
            DateTimeOffset.UtcNow.ToString("O"),
            Array.Empty<AnchorEditIssue>(),
            changedPaths
        );
        _auditLogger.Log(
            "web",
            "refactor_apply",
            "ok",
            $"path={NormalizeAuditToken(previewRecord.Path, "-")} previewId={NormalizeAuditToken(previewRecord.PreviewId, "-")} files={changedPaths.Length}"
        );
        var message = changedPaths.Length > 1
            ? $"{changedPaths.Length}개 파일 preview를 적용했습니다."
            : "preview를 파일에 적용했습니다.";
        return new RefactorActionResult(true, message, ApplyResult: applyResult);
    }

    private static string BuildSnippet(string text)
    {
        var lines = AnchorReadService.SplitLines(text);
        return string.Join("\n", lines.Take(8));
    }

    private static string NormalizeAuditToken(string? token, string fallback)
    {
        var normalized = (token ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized.ToLowerInvariant();
    }
}
