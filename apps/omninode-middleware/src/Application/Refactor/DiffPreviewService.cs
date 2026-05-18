using System.Security.Cryptography;
using System.Text;

namespace OmniNode.Middleware;

public sealed class DiffPreviewService
{
    private readonly string _workspaceRoot;
    private readonly FileRefactorPreviewStore _previewStore;

    public DiffPreviewService(PathOptions paths, FileRefactorPreviewStore previewStore)
    {
        _workspaceRoot = Path.GetFullPath(paths.WorkspaceRootDir);
        _previewStore = previewStore;
    }

    public Task<RefactorPreview> CreatePreviewAsync(
        string path,
        string originalText,
        string updatedText,
        IReadOnlyList<AnchorEditRequest> edits,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var createdAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var previewId = $"preview_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..31];
        var unifiedDiff = BuildUnifiedDiff(path, originalText, updatedText);
        var record = new RefactorPreviewRecord(
            previewId,
            path,
            originalText,
            updatedText,
            unifiedDiff,
            createdAtUtc,
            edits
        );
        _previewStore.Save(record);
        return Task.FromResult(new RefactorPreview(
            previewId,
            path,
            unifiedDiff,
            true,
            createdAtUtc,
            edits,
            Array.Empty<AnchorEditIssue>()
        ));
    }

    public Task<RefactorPreview> CreatePreviewAsync(
        string path,
        IReadOnlyList<RefactorPreviewFile> files,
        IReadOnlyList<AnchorEditRequest> edits,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedFiles = NormalizeFiles(files);
        if (normalizedFiles.Count == 0)
        {
            throw new InvalidOperationException("변경된 파일이 없습니다.");
        }

        var createdAtUtc = DateTimeOffset.UtcNow.ToString("O");
        var previewId = $"preview_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..31];
        var unifiedDiff = BuildUnifiedDiff(normalizedFiles);
        var primaryFile = normalizedFiles[0];
        var changedPaths = normalizedFiles
            .Select(file => file.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var record = new RefactorPreviewRecord(
            previewId,
            path,
            primaryFile.OriginalText,
            primaryFile.UpdatedText,
            unifiedDiff,
            createdAtUtc,
            edits,
            normalizedFiles
        );
        _previewStore.Save(record);
        return Task.FromResult(new RefactorPreview(
            previewId,
            path,
            unifiedDiff,
            true,
            createdAtUtc,
            edits,
            Array.Empty<AnchorEditIssue>(),
            changedPaths
        ));
    }

    public Task<RefactorPreviewRecord?> GetPreviewAsync(string previewId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_previewStore.TryLoad(previewId));
    }

    public void DeletePreview(string previewId)
    {
        _previewStore.Delete(previewId);
    }

    public static string ComputeTextHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(AnchorReadService.NormalizeNewlines(text ?? string.Empty)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyList<RefactorPreviewFile> NormalizeFiles(IReadOnlyList<RefactorPreviewFile> files)
    {
        return (files ?? Array.Empty<RefactorPreviewFile>())
            .Where(file => file != null)
            .Where(file => !string.Equals(file.OriginalText, file.UpdatedText, StringComparison.Ordinal))
            .ToArray();
    }

    private string BuildUnifiedDiff(IReadOnlyList<RefactorPreviewFile> files)
    {
        return string.Join(
            "\n\n",
            files.Select(file => BuildUnifiedDiff(file.Path, file.OriginalText, file.UpdatedText))
        );
    }

    private string BuildUnifiedDiff(string path, string originalText, string updatedText)
    {
        var before = AnchorReadService.SplitLines(originalText);
        var after = AnchorReadService.SplitLines(updatedText);
        var label = ToDiffLabel(path);

        var prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Count - prefix
            && suffix < after.Count - prefix
            && before[before.Count - 1 - suffix] == after[after.Count - 1 - suffix])
        {
            suffix++;
        }

        var context = 3;
        var oldChangeStart = prefix;
        var newChangeStart = prefix;
        var oldChangeEnd = before.Count - suffix;
        var newChangeEnd = after.Count - suffix;
        var oldHunkStart = Math.Max(0, oldChangeStart - context);
        var newHunkStart = Math.Max(0, newChangeStart - context);
        var oldHunkEnd = Math.Min(before.Count, oldChangeEnd + context);
        var newHunkEnd = Math.Min(after.Count, newChangeEnd + context);
        var oldCount = oldHunkEnd - oldHunkStart;
        var newCount = newHunkEnd - newHunkStart;

        var lines = new List<string>
        {
            $"--- a/{label}",
            $"+++ b/{label}",
            $"@@ -{FormatRange(oldHunkStart, oldCount)} +{FormatRange(newHunkStart, newCount)} @@"
        };

        for (var index = oldHunkStart; index < oldChangeStart; index++)
        {
            lines.Add($" {before[index]}");
        }

        for (var index = oldChangeStart; index < oldChangeEnd; index++)
        {
            lines.Add($"-{before[index]}");
        }

        for (var index = newChangeStart; index < newChangeEnd; index++)
        {
            lines.Add($"+{after[index]}");
        }

        for (var index = oldChangeEnd; index < oldHunkEnd; index++)
        {
            lines.Add($" {before[index]}");
        }

        return string.Join("\n", lines);
    }

    private string ToDiffLabel(string fullPath)
    {
        try
        {
            var normalized = Path.GetFullPath(fullPath);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (normalized.StartsWith(_workspaceRoot, comparison))
            {
                return Path.GetRelativePath(_workspaceRoot, normalized).Replace('\\', '/');
            }
        }
        catch
        {
        }

        return (fullPath ?? string.Empty).Replace('\\', '/');
    }

    private static string FormatRange(int zeroBasedStart, int count)
    {
        var start = count == 0 ? zeroBasedStart : zeroBasedStart + 1;
        return count == 1
            ? $"{start}"
            : $"{start},{count}";
    }
}
