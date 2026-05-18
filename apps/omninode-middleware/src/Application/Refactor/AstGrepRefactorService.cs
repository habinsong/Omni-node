using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class AstGrepRefactorService
{
    private readonly RefactorOptions _options;
    private readonly RefactorToolAvailability _toolAvailability;
    private readonly AnchorReadService _anchorReadService;
    private readonly DiffPreviewService _diffPreviewService;

    public AstGrepRefactorService(
        RefactorOptions options,
        RefactorToolAvailability toolAvailability,
        AnchorReadService anchorReadService,
        DiffPreviewService diffPreviewService
    )
    {
        _options = options;
        _toolAvailability = toolAvailability;
        _anchorReadService = anchorReadService;
        _diffPreviewService = diffPreviewService;
    }

    public async Task<RefactorActionResult> RunReplaceAsync(
        string path,
        string pattern,
        string replacement,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = _anchorReadService.ResolveWorkspacePath(path);
        var probe = _toolAvailability.ProbeAstGrep(normalizedPath, _options.RefactorEnableAstGrep);
        if (!probe.Enabled || !probe.Available)
        {
            return BuildResult(probe, probe.Message);
        }

        var normalizedPattern = (pattern ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return BuildResult(
                probe,
                "ast replace에는 pattern이 필요합니다.",
                status: "invalid_request"
            );
        }

        if (string.IsNullOrWhiteSpace(replacement))
        {
            return BuildResult(
                probe,
                "ast replace에는 replacement가 필요합니다.",
                status: "invalid_request"
            );
        }

        if (!File.Exists(normalizedPath))
        {
            return BuildResult(probe, "파일을 찾을 수 없습니다.", status: "missing_file");
        }

        try
        {
            var astLanguage = ResolveAstLanguage(normalizedPath, probe.Language);
            var processResult = await RunAstGrepAsync(
                probe,
                normalizedPath,
                astLanguage,
                normalizedPattern,
                replacement,
                cancellationToken
            );
            var previewFiles = await BuildPreviewFilesAsync(processResult.StdOut, cancellationToken);
            if (previewFiles.Count == 0 && processResult.ExitCode != 0)
            {
                var errorText = !string.IsNullOrWhiteSpace(processResult.StdErr)
                    ? processResult.StdErr
                    : processResult.StdOut;
                if (IsEmptyJsonArray(processResult.StdOut))
                {
                    return BuildResult(probe, "변경점이 없습니다.", status: "no_changes");
                }

                return BuildResult(
                    probe,
                    $"ast-grep 실행 실패: {DoctorSupport.Trim(errorText, 600)}",
                    status: "error"
                );
            }

            if (previewFiles.Count == 0)
            {
                return BuildResult(probe, "변경점이 없습니다.", status: "no_changes");
            }

            var preview = await _diffPreviewService.CreatePreviewAsync(
                normalizedPath,
                previewFiles,
                Array.Empty<AnchorEditRequest>(),
                cancellationToken
            );
            var message = previewFiles.Count > 1
                ? $"{previewFiles.Count}개 파일 ast replace preview를 만들었습니다."
                : "ast replace preview를 만들었습니다.";
            return BuildSuccessResult(probe, preview, message);
        }
        catch (Exception ex)
        {
            return BuildResult(probe, $"ast-grep replace 실패: {ex.Message}", status: "error");
        }
    }

    private async Task<IReadOnlyList<RefactorPreviewFile>> BuildPreviewFilesAsync(
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        var matches = ParseMatches(rawJson);
        var files = new List<RefactorPreviewFile>();
        foreach (var group in matches.GroupBy(match => match.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = _anchorReadService.ResolveWorkspacePath(group.Key);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"ast-grep 대상 파일을 찾을 수 없습니다: {fullPath}");
            }

            var originalText = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var edits = group
                .Select(match => new AstRewrite(
                    GetIndexFromUtf8ByteOffset(originalText, match.StartByte),
                    GetIndexFromUtf8ByteOffset(originalText, match.EndByte),
                    match.Replacement
                ))
                .OrderByDescending(match => match.StartIndex)
                .ThenByDescending(match => match.EndIndex)
                .ToArray();
            var updatedText = ApplyEdits(originalText, edits);
            if (string.Equals(originalText, updatedText, StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(new RefactorPreviewFile(
                fullPath,
                originalText,
                updatedText,
                DiffPreviewService.ComputeTextHash(originalText),
                DiffPreviewService.ComputeTextHash(updatedText)
            ));
        }

        return files;
    }

    private static string ApplyEdits(string originalText, IReadOnlyList<AstRewrite> edits)
    {
        if (edits.Count == 0)
        {
            return originalText;
        }

        var builder = new StringBuilder(originalText);
        var previousStart = int.MaxValue;
        foreach (var edit in edits)
        {
            if (edit.EndIndex < edit.StartIndex)
            {
                throw new InvalidOperationException("ast-grep rewrite 범위가 올바르지 않습니다.");
            }

            if (edit.EndIndex > previousStart)
            {
                throw new InvalidOperationException("겹치는 ast-grep rewrite 결과는 지원하지 않습니다.");
            }

            builder.Remove(edit.StartIndex, edit.EndIndex - edit.StartIndex);
            builder.Insert(edit.StartIndex, edit.Replacement);
            previousStart = edit.StartIndex;
        }

        return builder.ToString();
    }

    private static int GetIndexFromUtf8ByteOffset(string text, int targetBytes)
    {
        if (targetBytes <= 0)
        {
            return 0;
        }

        var index = 0;
        var consumedBytes = 0;
        while (index < text.Length && consumedBytes < targetBytes)
        {
            Rune.DecodeFromUtf16(text.AsSpan(index), out _, out var charsConsumed);
            var bytes = Encoding.UTF8.GetByteCount(text.AsSpan(index, charsConsumed));
            if (consumedBytes + bytes > targetBytes)
            {
                break;
            }

            consumedBytes += bytes;
            index += charsConsumed;
        }

        return index;
    }

    private static IReadOnlyList<AstGrepMatch> ParseMatches(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Array.Empty<AstGrepMatch>();
        }

        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AstGrepMatch>();
        }

        var matches = new List<AstGrepMatch>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var path = item.TryGetProperty("file", out var fileElement) && fileElement.ValueKind == JsonValueKind.String
                ? fileElement.GetString() ?? string.Empty
                : string.Empty;
            var replacement = item.TryGetProperty("replacement", out var replacementElement)
                && replacementElement.ValueKind == JsonValueKind.String
                ? replacementElement.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(path)
                || !TryReadOffsets(item, out var startByte, out var endByte))
            {
                continue;
            }

            matches.Add(new AstGrepMatch(Path.GetFullPath(path), startByte, endByte, replacement));
        }

        return matches;
    }

    private static bool IsEmptyJsonArray(string rawJson)
    {
        return string.Equals((rawJson ?? string.Empty).Trim(), "[]", StringComparison.Ordinal);
    }

    private static string ResolveAstLanguage(string path, string? fallback)
    {
        var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".js" => "javascript",
            ".jsx" => "jsx",
            ".ts" => "typescript",
            ".tsx" => "tsx",
            ".py" => "python",
            ".cs" => "csharp",
            ".c" or ".h" or ".cc" or ".cpp" or ".hpp" => "cpp",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".json" => "json",
            ".html" => "html",
            ".css" => "css",
            _ => fallback ?? "javascript"
        };
    }

    private static bool TryReadOffsets(JsonElement element, out int startByte, out int endByte)
    {
        startByte = 0;
        endByte = 0;

        if (element.TryGetProperty("replacementOffsets", out var replacementOffsets)
            && replacementOffsets.ValueKind == JsonValueKind.Object
            && replacementOffsets.TryGetProperty("start", out var replacementStart)
            && replacementOffsets.TryGetProperty("end", out var replacementEnd)
            && replacementStart.ValueKind == JsonValueKind.Number
            && replacementEnd.ValueKind == JsonValueKind.Number)
        {
            startByte = replacementStart.GetInt32();
            endByte = replacementEnd.GetInt32();
            return true;
        }

        if (element.TryGetProperty("range", out var range)
            && range.ValueKind == JsonValueKind.Object
            && range.TryGetProperty("byteOffset", out var byteOffset)
            && byteOffset.ValueKind == JsonValueKind.Object
            && byteOffset.TryGetProperty("start", out var rangeStart)
            && byteOffset.TryGetProperty("end", out var rangeEnd)
            && rangeStart.ValueKind == JsonValueKind.Number
            && rangeEnd.ValueKind == JsonValueKind.Number)
        {
            startByte = rangeStart.GetInt32();
            endByte = rangeEnd.GetInt32();
            return true;
        }

        return false;
    }

    private static Task<DoctorProcessResult> RunAstGrepAsync(
        RefactorToolProbe probe,
        string path,
        string language,
        string pattern,
        string replacement,
        CancellationToken cancellationToken
    )
    {
        var binary = (probe.BinaryPath ?? string.Empty).Trim();
        if (string.Equals(binary, "npm:@ast-grep/cli", StringComparison.Ordinal))
        {
            return DoctorSupport.RunProcessAsync(
                "npm",
                new[]
                {
                    "exec",
                    "--yes",
                    "--package=@ast-grep/cli",
                    "ast-grep",
                    "--",
                    "run",
                    "--pattern",
                    pattern,
                    "--rewrite",
                    replacement,
                    "--lang",
                    language,
                    "--json=compact",
                    path
                },
                cancellationToken
            );
        }

        return DoctorSupport.RunProcessAsync(
            binary,
            new[]
            {
                "run",
                "--pattern",
                pattern,
                "--rewrite",
                replacement,
                "--lang",
                language,
                "--json=compact",
                path
            },
            cancellationToken
        );
    }

    private static RefactorActionResult BuildResult(
        RefactorToolProbe probe,
        string message,
        string? status = null
    )
    {
        var toolResult = new RefactorToolInvocationResult(
            probe.Tool,
            probe.Enabled,
            probe.Available,
            status ?? probe.Status,
            message,
            probe.BinaryPath,
            probe.Language
        );
        return new RefactorActionResult(false, message, ToolResult: toolResult);
    }

    private static RefactorActionResult BuildSuccessResult(
        RefactorToolProbe probe,
        RefactorPreview preview,
        string message
    )
    {
        var toolResult = new RefactorToolInvocationResult(
            probe.Tool,
            probe.Enabled,
            probe.Available,
            "preview_ready",
            message,
            probe.BinaryPath,
            probe.Language,
            preview
        );
        return new RefactorActionResult(true, message, Preview: preview, ToolResult: toolResult);
    }

    private sealed record AstGrepMatch(string Path, int StartByte, int EndByte, string Replacement);

    private sealed record AstRewrite(int StartIndex, int EndIndex, string Replacement);
}
