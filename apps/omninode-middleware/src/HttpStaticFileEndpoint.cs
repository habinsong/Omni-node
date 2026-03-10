using System.Net;
using System.Text;

namespace OmniNode.Middleware;

internal sealed class HttpStaticFileEndpoint
{
    private readonly string _dashboardRootPath;

    public HttpStaticFileEndpoint(string dashboardIndexPath)
    {
        _dashboardRootPath = Path.GetDirectoryName(Path.GetFullPath(dashboardIndexPath))
                             ?? Path.GetFullPath(".");
    }

    public async Task<bool> TryHandleAsync(
        HttpListenerContext context,
        string path,
        CancellationToken cancellationToken
    )
    {
        if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            context.Response.ContentLength64 = 0;
            context.Response.OutputStream.Close();
            return true;
        }

        if (!context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryResolveStaticAsset(path, out var filePath, out var contentType))
        {
            return false;
        }

        var body = await File.ReadAllTextAsync(filePath, cancellationToken);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        await WriteResponseAsync(context.Response, contentType, body, cancellationToken);
        return true;
    }

    private bool TryResolveStaticAsset(string path, out string filePath, out string contentType)
    {
        var normalized = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "/")
        {
            normalized = "/index.html";
        }

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized;
        }

        var relativePath = normalized[1..];
        if (relativePath.Length == 0
            || relativePath.Contains("..", StringComparison.Ordinal)
            || relativePath.Contains('\\', StringComparison.Ordinal))
        {
            filePath = string.Empty;
            contentType = "text/plain";
            return false;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(_dashboardRootPath, relativePath));
        var dashboardRoot = Path.GetFullPath(_dashboardRootPath);
        var rootPrefix = dashboardRoot.EndsWith(Path.DirectorySeparatorChar)
            ? dashboardRoot
            : dashboardRoot + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(rootPrefix, StringComparison.Ordinal)
            && !string.Equals(candidatePath, dashboardRoot, StringComparison.Ordinal))
        {
            filePath = string.Empty;
            contentType = "text/plain";
            return false;
        }

        if (!File.Exists(candidatePath))
        {
            filePath = string.Empty;
            contentType = "text/plain";
            return false;
        }

        var extension = Path.GetExtension(candidatePath).ToLowerInvariant();
        contentType = extension switch
        {
            ".js" => "application/javascript; charset=utf-8",
            ".mjs" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".html" => "text/html; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
        if (contentType == "text/plain; charset=utf-8")
        {
            filePath = string.Empty;
            return false;
        }

        filePath = candidatePath;
        return true;
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
}
