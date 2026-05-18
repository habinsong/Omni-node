using System.Globalization;
using System.Net;
using System.Text;
using System.Linq;

namespace OmniNode.Middleware;

internal sealed class GatewayApiEndpoint
{
    private const string GuardRetryTimelineApiPath = "/api/guard/retry-timeline";
    private const string LocalImageApiPath = "/api/local-image";
    private const string CodingPreviewApiPrefix = "/api/coding-preview/";
    private readonly GuardRetryTimelineStore _guardRetryTimelineStore;
    private readonly IConversationApplicationService _conversationService;
    private readonly IReadOnlyList<string> _localImageRoots;

    public GatewayApiEndpoint(
        GuardRetryTimelineStore guardRetryTimelineStore,
        IConversationApplicationService conversationService,
        PathOptions paths
    )
    {
        _guardRetryTimelineStore = guardRetryTimelineStore;
        _conversationService = conversationService;
        var workspaceRoot = Path.GetFullPath(paths.WorkspaceRootDir);
        _localImageRoots = new[]
        {
            Path.GetFullPath(Path.Combine(workspaceRoot, "routines"))
        };
    }

    public async Task<bool> TryHandleAsync(
        HttpListenerContext context,
        string path,
        CancellationToken cancellationToken
    )
    {
        if (path.Equals(GuardRetryTimelineApiPath, StringComparison.OrdinalIgnoreCase))
        {
            await HandleGuardRetryTimelineApiAsync(context, cancellationToken);
            return true;
        }

        if (path.Equals(LocalImageApiPath, StringComparison.OrdinalIgnoreCase))
        {
            await HandleLocalImageApiAsync(context, cancellationToken);
            return true;
        }

        if (path.StartsWith(CodingPreviewApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await HandleCodingPreviewApiAsync(context, path, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task HandleGuardRetryTimelineApiAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var method = context.Request.HttpMethod?.ToUpperInvariant() ?? "GET";
        if (method != "GET" && method != "HEAD")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            context.Response.Headers["Allow"] = "GET, HEAD";
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Method Not Allowed", cancellationToken);
            return;
        }

        var bucketMinutes = ParseQueryInt(
            context.Request.QueryString["bucketMinutes"],
            1,
            60
        );
        var windowMinutes = ParseQueryInt(
            context.Request.QueryString["windowMinutes"],
            1,
            24 * 60
        );
        var maxBucketRows = ParseQueryInt(
            context.Request.QueryString["maxBucketRows"],
            1,
            288
        );
        var channels = ParseChannelsQuery(context.Request.QueryString["channels"]);
        var payload = _guardRetryTimelineStore.BuildSnapshotJson(
            bucketMinutes: bucketMinutes,
            windowMinutes: windowMinutes,
            maxBucketRows: maxBucketRows,
            channels: channels
        );

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        if (method == "HEAD")
        {
            context.Response.ContentLength64 = 0;
            context.Response.OutputStream.Close();
            return;
        }

        await WriteResponseAsync(
            context.Response,
            "application/json; charset=utf-8",
            payload,
            cancellationToken
        );
    }

    private async Task HandleLocalImageApiAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var method = context.Request.HttpMethod?.ToUpperInvariant() ?? "GET";
        if (method != "GET" && method != "HEAD")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            context.Response.Headers["Allow"] = "GET, HEAD";
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Method Not Allowed", cancellationToken);
            return;
        }

        var requestedPath = (context.Request.QueryString["path"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requestedPath) || !Path.IsPathRooted(requestedPath))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "absolute image path is required", cancellationToken);
            return;
        }

        var fullPath = Path.GetFullPath(requestedPath);
        var extension = Path.GetExtension(fullPath);
        var contentType = extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(contentType))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "unsupported image type", cancellationToken);
            return;
        }

        if (!IsLocalImagePathAllowed(fullPath))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "image path is outside allowed routine asset roots", cancellationToken);
            return;
        }

        if (!File.Exists(fullPath))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "image not found", cancellationToken);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        if (method == "HEAD")
        {
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = 0;
            context.Response.OutputStream.Close();
            return;
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        await WriteBinaryResponseAsync(context.Response, contentType, bytes, cancellationToken);
    }

    private async Task HandleCodingPreviewApiAsync(
        HttpListenerContext context,
        string path,
        CancellationToken cancellationToken
    )
    {
        var method = context.Request.HttpMethod?.ToUpperInvariant() ?? "GET";
        if (method != "GET" && method != "HEAD")
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            context.Response.Headers["Allow"] = "GET, HEAD";
            await WritePreviewResponseAsync(context.Response, "text/plain; charset=utf-8", "Method Not Allowed", cancellationToken);
            return;
        }

        if (!TryResolveCodingPreviewRequest(path, out var conversationId, out var targetSegment, out var relativePath))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WritePreviewResponseAsync(context.Response, "text/plain; charset=utf-8", "preview path is invalid", cancellationToken);
            return;
        }

        var fileResult = ResolveCodingPreviewFile(conversationId, targetSegment, relativePath);
        if (fileResult == null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WritePreviewResponseAsync(context.Response, "text/plain; charset=utf-8", "preview file not found", cancellationToken);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        if (method == "HEAD")
        {
            context.Response.ContentType = fileResult.ContentType;
            context.Response.ContentLength64 = 0;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            context.Response.OutputStream.Close();
            return;
        }

        if (fileResult.IsBinary)
        {
            var bytes = await File.ReadAllBytesAsync(fileResult.FullPath, cancellationToken);
            await WriteBinaryPreviewResponseAsync(context.Response, fileResult.ContentType, bytes, cancellationToken);
            return;
        }

        var body = await File.ReadAllTextAsync(fileResult.FullPath, cancellationToken);
        await WritePreviewResponseAsync(context.Response, fileResult.ContentType, body, cancellationToken);
    }

    private sealed record CodingPreviewFileResult(string FullPath, string ContentType, bool IsBinary);

    private static bool TryResolveCodingPreviewRequest(
        string path,
        out string conversationId,
        out string targetSegment,
        out string relativePath
    )
    {
        conversationId = string.Empty;
        targetSegment = string.Empty;
        relativePath = string.Empty;

        var normalized = (path ?? string.Empty).Trim();
        if (!normalized.StartsWith(CodingPreviewApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = normalized[CodingPreviewApiPrefix.Length..].Trim('/');
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3)
        {
            return false;
        }

        conversationId = Uri.UnescapeDataString(segments[0]);
        targetSegment = Uri.UnescapeDataString(segments[1]);
        relativePath = string.Join("/", segments.Skip(2).Select(Uri.UnescapeDataString));
        return !string.IsNullOrWhiteSpace(conversationId)
            && !string.IsNullOrWhiteSpace(targetSegment)
            && !string.IsNullOrWhiteSpace(relativePath);
    }

    private CodingPreviewFileResult? ResolveCodingPreviewFile(
        string conversationId,
        string targetSegment,
        string relativePath
    )
    {
        var conversation = _conversationService.GetConversation(conversationId);
        var latest = conversation?.LatestCodingResult;
        if (latest == null)
        {
            return null;
        }

        var execution = ResolveCodingPreviewExecution(latest, targetSegment);
        if (execution == null)
        {
            return null;
        }

        var runDirectory = (execution.RunDirectory ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(runDirectory) || !Directory.Exists(runDirectory))
        {
            return null;
        }

        var normalizedRelativePath = relativePath
            .Replace('\\', '/')
            .Trim('/')
            .Replace("//", "/", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath) || normalizedRelativePath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(runDirectory, normalizedRelativePath));
        if (!IsPathUnderRoot(fullPath, runDirectory) || !File.Exists(fullPath))
        {
            return null;
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".txt" => "text/plain; charset=utf-8",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var isBinary = !contentType.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase)
            && !contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            && !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            && !contentType.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase);

        return new CodingPreviewFileResult(fullPath, contentType, isBinary);
    }

    private static CodeExecutionResult? ResolveCodingPreviewExecution(
        ConversationCodingResultSnapshot latest,
        string targetSegment
    )
    {
        if (string.Equals(targetSegment, "main", StringComparison.OrdinalIgnoreCase))
        {
            return latest.Execution;
        }

        if (targetSegment.StartsWith("worker-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(targetSegment["worker-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var workerIndex)
            && workerIndex >= 0
            && workerIndex < latest.Workers.Count)
        {
            return latest.Workers[workerIndex].Execution;
        }

        return null;
    }

    private static int? ParseQueryInt(string? raw, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static string[]? ParseChannelsQuery(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var channels = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return channels.Length > 0 ? channels : null;
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        string contentType,
        string body,
        CancellationToken cancellationToken
    )
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.OutputStream.Close();
    }

    private static async Task WriteBinaryResponseAsync(
        HttpListenerResponse response,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken
    )
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellationToken);
        response.OutputStream.Close();
    }

    private static async Task WritePreviewResponseAsync(
        HttpListenerResponse response,
        string contentType,
        string body,
        CancellationToken cancellationToken
    )
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.OutputStream.Close();
    }

    private static async Task WriteBinaryPreviewResponseAsync(
        HttpListenerResponse response,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken
    )
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellationToken);
        response.OutputStream.Close();
    }

    private static bool IsPathUnderRoot(string candidatePath, string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullCandidate, fullRoot, StringComparison.Ordinal))
        {
            return true;
        }

        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private bool IsLocalImagePathAllowed(string fullPath)
    {
        return _localImageRoots.Any(root => IsPathUnderRoot(fullPath, root));
    }
}
