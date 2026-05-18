using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class LspRefactorService
{
    private readonly PathOptions _paths;
    private readonly RefactorOptions _options;
    private readonly RefactorToolAvailability _toolAvailability;
    private readonly AnchorReadService _anchorReadService;
    private readonly DiffPreviewService _diffPreviewService;

    public LspRefactorService(
        PathOptions paths,
        RefactorOptions options,
        RefactorToolAvailability toolAvailability,
        AnchorReadService anchorReadService,
        DiffPreviewService diffPreviewService
    )
    {
        _paths = paths;
        _options = options;
        _toolAvailability = toolAvailability;
        _anchorReadService = anchorReadService;
        _diffPreviewService = diffPreviewService;
    }

    public async Task<RefactorActionResult> RunRenameAsync(
        string path,
        string symbol,
        string newName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = _anchorReadService.ResolveWorkspacePath(path);
        var probe = _toolAvailability.ProbeLsp(normalizedPath, _options.RefactorEnableLsp);
        if (!probe.Enabled || !probe.Available)
        {
            return BuildResult(probe, probe.Message);
        }

        var normalizedSymbol = (symbol ?? string.Empty).Trim();
        var normalizedNewName = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSymbol) || string.IsNullOrWhiteSpace(normalizedNewName))
        {
            return BuildResult(
                probe,
                "LSP rename에는 symbol과 newName이 모두 필요합니다.",
                status: "invalid_request"
            );
        }

        if (!File.Exists(normalizedPath))
        {
            return BuildResult(probe, "파일을 찾을 수 없습니다.", status: "missing_file");
        }

        await using var client = CreateClient(probe);
        try
        {
            var documentText = await File.ReadAllTextAsync(normalizedPath, cancellationToken);
            var languageId = ResolveLanguageId(probe.Language, normalizedPath);

            await client.InitializeAsync(_paths.WorkspaceRootDir, cancellationToken);
            await client.OpenDocumentAsync(normalizedPath, languageId, documentText, cancellationToken);

            using var symbols = await client.RequestDocumentSymbolsAsync(normalizedPath, cancellationToken);
            var matches = FindSymbolMatches(symbols.RootElement, normalizedSymbol)
                .DistinctBy(candidate => $"{candidate.Position.Line}:{candidate.Position.Character}")
                .OrderBy(candidate => candidate.Depth)
                .ToArray();
            if (matches.Length == 0)
            {
                return BuildResult(probe, $"symbol `{normalizedSymbol}` 을(를) 찾지 못했습니다.", status: "symbol_not_found");
            }

            if (matches.Length > 1)
            {
                return BuildResult(
                    probe,
                    $"symbol `{normalizedSymbol}` 이(가) 여러 위치에 있어 rename 기준을 정할 수 없습니다. 더 구체적인 이름으로 시도하세요.",
                    status: "ambiguous_symbol"
                );
            }

            using var renameResult = await client.RequestRenameAsync(
                normalizedPath,
                matches[0].Position,
                normalizedNewName,
                cancellationToken
            );
            var previewFiles = await BuildPreviewFilesAsync(
                renameResult.RootElement,
                client.OffsetEncoding,
                cancellationToken
            );
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
                ? $"{previewFiles.Count}개 파일 LSP rename preview를 만들었습니다."
                : "LSP rename preview를 만들었습니다.";
            return BuildSuccessResult(probe, preview, message);
        }
        catch (Exception ex)
        {
            var diagnostics = client.GetDiagnostics();
            var message = string.IsNullOrWhiteSpace(diagnostics)
                ? $"LSP rename 실패: {ex.Message}"
                : $"LSP rename 실패: {ex.Message} ({diagnostics})";
            return BuildResult(probe, message, status: "error");
        }
    }

    private async Task<IReadOnlyList<RefactorPreviewFile>> BuildPreviewFilesAsync(
        JsonElement workspaceEdit,
        string offsetEncoding,
        CancellationToken cancellationToken
    )
    {
        var editsByPath = new Dictionary<string, List<LspTextEdit>>(StringComparer.Ordinal);
        CollectWorkspaceEdits(workspaceEdit, editsByPath);
        var files = new List<RefactorPreviewFile>();
        foreach (var entry in editsByPath.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = _anchorReadService.ResolveWorkspacePath(entry.Key);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"rename 대상 파일을 찾을 수 없습니다: {fullPath}");
            }

            var originalText = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var updatedText = ApplyEdits(originalText, entry.Value, offsetEncoding);
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

    private static void CollectWorkspaceEdits(
        JsonElement workspaceEdit,
        Dictionary<string, List<LspTextEdit>> editsByPath
    )
    {
        if (workspaceEdit.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (workspaceEdit.TryGetProperty("changes", out var changes)
            && changes.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in changes.EnumerateObject())
            {
                var path = NormalizeWorkspacePath(property.Name);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                foreach (var edit in ParseTextEdits(property.Value))
                {
                    AddEdit(editsByPath, path, edit);
                }
            }
        }

        if (workspaceEdit.TryGetProperty("documentChanges", out var documentChanges)
            && documentChanges.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in documentChanges.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (item.TryGetProperty("kind", out var kindElement)
                    && kindElement.ValueKind == JsonValueKind.String)
                {
                    throw new InvalidOperationException("현재는 파일 생성/삭제가 포함된 workspace edit는 지원하지 않습니다.");
                }

                if (!item.TryGetProperty("textDocument", out var textDocument)
                    || !textDocument.TryGetProperty("uri", out var uriElement)
                    || uriElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var path = NormalizeWorkspacePath(uriElement.GetString());
                if (string.IsNullOrWhiteSpace(path) || !item.TryGetProperty("edits", out var edits))
                {
                    continue;
                }

                foreach (var edit in ParseTextEdits(edits))
                {
                    AddEdit(editsByPath, path, edit);
                }
            }
        }
    }

    private static IEnumerable<LspTextEdit> ParseTextEdits(JsonElement edits)
    {
        if (edits.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var edit in edits.EnumerateArray())
        {
            if (edit.ValueKind != JsonValueKind.Object
                || !edit.TryGetProperty("range", out var range)
                || !range.TryGetProperty("start", out var start)
                || !range.TryGetProperty("end", out var end)
                || !TryReadPosition(start, out var startPosition)
                || !TryReadPosition(end, out var endPosition))
            {
                continue;
            }

            var newText = edit.TryGetProperty("newText", out var newTextElement) && newTextElement.ValueKind == JsonValueKind.String
                ? newTextElement.GetString() ?? string.Empty
                : string.Empty;
            yield return new LspTextEdit(startPosition, endPosition, newText);
        }
    }

    private static string ApplyEdits(
        string text,
        IReadOnlyList<LspTextEdit> edits,
        string offsetEncoding
    )
    {
        var ordered = edits
            .OrderByDescending(edit => edit.Start.Line)
            .ThenByDescending(edit => edit.Start.Character)
            .ToArray();
        if (ordered.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        foreach (var edit in ordered)
        {
            var startIndex = GetIndexFromPosition(builder.ToString(), edit.Start, offsetEncoding);
            var endIndex = GetIndexFromPosition(builder.ToString(), edit.End, offsetEncoding);
            if (endIndex < startIndex)
            {
                throw new InvalidOperationException("LSP edit range가 올바르지 않습니다.");
            }

            builder.Remove(startIndex, endIndex - startIndex);
            builder.Insert(startIndex, edit.NewText);
        }

        return builder.ToString();
    }

    private static int GetIndexFromPosition(string text, LspPosition position, string offsetEncoding)
    {
        var lineStarts = BuildLineStarts(text);
        var safeLine = Math.Clamp(position.Line, 0, Math.Max(0, lineStarts.Count - 1));
        var lineStart = lineStarts[safeLine];
        var lineEnd = FindLineEnd(text, lineStart);

        if (string.Equals(offsetEncoding, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            return GetUtf8Index(text, lineStart, lineEnd, position.Character);
        }

        return Math.Clamp(lineStart + Math.Max(0, position.Character), lineStart, lineEnd);
    }

    private static int GetUtf8Index(string text, int lineStart, int lineEnd, int targetBytes)
    {
        var index = lineStart;
        var consumedBytes = 0;
        while (index < lineEnd && consumedBytes < targetBytes)
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

    private static List<int> BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                starts.Add(index + 1);
                continue;
            }

            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts;
    }

    private static int FindLineEnd(string text, int lineStart)
    {
        var index = lineStart;
        while (index < text.Length && text[index] != '\r' && text[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private static IEnumerable<LspSymbolCandidate> FindSymbolMatches(JsonElement root, string symbol)
    {
        var matches = new List<LspSymbolCandidate>();
        CollectSymbols(root, 0, symbol, matches);
        return matches;
    }

    private static void CollectSymbols(
        JsonElement element,
        int depth,
        string symbol,
        List<LspSymbolCandidate> matches
    )
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectSymbols(item, depth, symbol, matches);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString() ?? string.Empty
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (TryReadSymbolPosition(element, out var position)
                && string.Equals(name.Trim(), symbol, StringComparison.Ordinal))
            {
                matches.Add(new LspSymbolCandidate(name, position, depth));
            }
        }

        if (element.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                CollectSymbols(child, depth + 1, symbol, matches);
            }
        }
    }

    private static bool TryReadSymbolPosition(JsonElement symbol, out LspPosition position)
    {
        position = new LspPosition(0, 0);
        if (symbol.TryGetProperty("selectionRange", out var selectionRange)
            && selectionRange.ValueKind == JsonValueKind.Object
            && selectionRange.TryGetProperty("start", out var selectionStart)
            && TryReadPosition(selectionStart, out position))
        {
            return true;
        }

        if (symbol.TryGetProperty("location", out var location)
            && location.ValueKind == JsonValueKind.Object
            && location.TryGetProperty("range", out var locationRange)
            && locationRange.TryGetProperty("start", out var locationStart)
            && TryReadPosition(locationStart, out position))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadPosition(JsonElement element, out LspPosition position)
    {
        position = new LspPosition(0, 0);
        if (!element.TryGetProperty("line", out var lineElement)
            || !element.TryGetProperty("character", out var characterElement)
            || lineElement.ValueKind != JsonValueKind.Number
            || characterElement.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        position = new LspPosition(lineElement.GetInt32(), characterElement.GetInt32());
        return true;
    }

    private static void AddEdit(
        IDictionary<string, List<LspTextEdit>> editsByPath,
        string path,
        LspTextEdit edit
    )
    {
        if (!editsByPath.TryGetValue(path, out var edits))
        {
            edits = new List<LspTextEdit>();
            editsByPath[path] = edits;
        }

        edits.Add(edit);
    }

    private static string NormalizeWorkspacePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(rawPath, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return Path.GetFullPath(uri.LocalPath);
        }

        return Path.GetFullPath(rawPath);
    }

    private static string ResolveLanguageId(string? language, string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return language switch
        {
            "typescript" when extension == ".tsx" => "typescriptreact",
            "typescript" when extension == ".jsx" => "javascriptreact",
            "typescript" when extension == ".js" => "javascript",
            "typescript" => "typescript",
            "python" => "python",
            "csharp" => "csharp",
            "cpp" when extension is ".c" or ".h" => "c",
            "cpp" => "cpp",
            "go" => "go",
            "rust" => "rust",
            "java" => "java",
            "json" => "json",
            "web" when extension == ".html" => "html",
            "web" when extension == ".css" => "css",
            _ => "plaintext"
        };
    }

    private static LspStdioClient CreateClient(RefactorToolProbe probe)
    {
        var binary = (probe.BinaryPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(binary))
        {
            throw new InvalidOperationException("LSP 실행 경로가 비어 있습니다.");
        }

        if (string.Equals(binary, "npm:typescript-language-server", StringComparison.Ordinal))
        {
            return LspStdioClient.Start(
                "npm",
                new[]
                {
                    "exec",
                    "--yes",
                    "--package=typescript",
                    "--package=typescript-language-server",
                    "typescript-language-server",
                    "--",
                    "--stdio"
                },
                Directory.GetCurrentDirectory()
            );
        }

        if (string.Equals(binary, "npm:pyright-langserver", StringComparison.Ordinal))
        {
            return LspStdioClient.Start(
                "npm",
                new[]
                {
                    "exec",
                    "--yes",
                    "--package=pyright",
                    "pyright-langserver",
                    "--",
                    "--stdio"
                },
                Directory.GetCurrentDirectory()
            );
        }

        var command = Path.GetFileName(binary);
        var arguments = command switch
        {
            "clangd" => new[] { "--offset-encoding=utf-8" },
            "typescript-language-server" => new[] { "--stdio" },
            "pyright-langserver" => new[] { "--stdio" },
            "omnisharp" => new[] { "-lsp" },
            _ => Array.Empty<string>()
        };
        return LspStdioClient.Start(binary, arguments, Directory.GetCurrentDirectory());
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

    private sealed record LspTextEdit(LspPosition Start, LspPosition End, string NewText);

    private sealed record LspSymbolCandidate(string Name, LspPosition Position, int Depth);
}
