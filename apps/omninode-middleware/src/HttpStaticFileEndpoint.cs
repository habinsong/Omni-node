using System.Net;
using System.Globalization;

namespace OmniNode.Middleware;

internal sealed class HttpStaticFileEndpoint
{
    private const long MaxCachedAssetBytes = 1 * 1024 * 1024;
    private const long MaxTotalCachedAssetBytes = 8 * 1024 * 1024;

    private readonly string _dashboardRootPath;
    private readonly object _assetCacheGate = new();
    private readonly Dictionary<string, StaticAssetCacheEntry> _assetCache = new(StringComparer.Ordinal);
    private long _cachedAssetBytes;

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

        var asset = await ReadStaticAssetAsync(filePath, contentType, cancellationToken);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        ApplyStaticAssetHeaders(context.Response, asset);
        if (IsClientCacheFresh(
                context.Request.Headers["If-None-Match"],
                context.Request.Headers["If-Modified-Since"],
                asset.ETag,
                asset.LastModifiedUtc))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotModified;
            context.Response.ContentLength64 = 0;
            context.Response.OutputStream.Close();
            return true;
        }

        await WriteResponseAsync(context.Response, asset, cancellationToken);
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

    private async Task<StaticAssetCacheEntry> ReadStaticAssetAsync(
        string filePath,
        string contentType,
        CancellationToken cancellationToken
    )
    {
        var fileInfo = new FileInfo(filePath);
        var lastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var fileLength = fileInfo.Length;

        lock (_assetCacheGate)
        {
            if (_assetCache.TryGetValue(filePath, out var cached)
                && cached.LastWriteTimeUtcTicks == lastWriteTimeUtcTicks
                && cached.Length == fileLength
                && cached.ContentType == contentType)
            {
                return cached;
            }
        }

        var body = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var asset = CreateCacheEntry(filePath, contentType, body, fileInfo);
        StoreCacheEntry(asset);
        return asset;
    }

    private void StoreCacheEntry(StaticAssetCacheEntry asset)
    {
        lock (_assetCacheGate)
        {
            if (_assetCache.TryGetValue(asset.FilePath, out var previous))
            {
                _cachedAssetBytes -= previous.Body.LongLength;
                _assetCache.Remove(asset.FilePath);
            }

            if (asset.Body.LongLength > MaxCachedAssetBytes)
            {
                return;
            }

            if (_cachedAssetBytes + asset.Body.LongLength > MaxTotalCachedAssetBytes)
            {
                _assetCache.Clear();
                _cachedAssetBytes = 0;
            }

            _assetCache[asset.FilePath] = asset;
            _cachedAssetBytes += asset.Body.LongLength;
        }
    }

    private static StaticAssetCacheEntry CreateCacheEntry(
        string filePath,
        string contentType,
        byte[] body,
        FileInfo fileInfo
    )
    {
        var lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
        var lastModifiedUtc = TruncateToWholeSeconds(new DateTimeOffset(lastWriteTimeUtc, TimeSpan.Zero));
        var etag = $"\"{lastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";
        return new StaticAssetCacheEntry(
            filePath,
            contentType,
            body,
            fileInfo.Length,
            lastWriteTimeUtc.Ticks,
            etag,
            lastModifiedUtc
        );
    }

    internal static bool IsClientCacheFresh(
        string? ifNoneMatchHeader,
        string? ifModifiedSinceHeader,
        string etag,
        DateTimeOffset lastModifiedUtc
    )
    {
        if (!string.IsNullOrWhiteSpace(ifNoneMatchHeader))
        {
            return MatchesETag(ifNoneMatchHeader, etag);
        }

        if (string.IsNullOrWhiteSpace(ifModifiedSinceHeader))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
                   ifModifiedSinceHeader,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out var modifiedSince
               )
               && lastModifiedUtc <= TruncateToWholeSeconds(modifiedSince.ToUniversalTime());
    }

    private static bool MatchesETag(string ifNoneMatchHeader, string etag)
    {
        var values = ifNoneMatchHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal));
    }

    private static DateTimeOffset TruncateToWholeSeconds(DateTimeOffset value)
    {
        return DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());
    }

    private static void ApplyStaticAssetHeaders(HttpListenerResponse response, StaticAssetCacheEntry asset)
    {
        response.ContentType = asset.ContentType;
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["ETag"] = asset.ETag;
        response.Headers["Last-Modified"] = asset.LastModifiedUtc.ToString("R", CultureInfo.InvariantCulture);
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        StaticAssetCacheEntry asset,
        CancellationToken cancellationToken
    )
    {
        response.ContentLength64 = asset.Body.Length;
        await response.OutputStream.WriteAsync(asset.Body, cancellationToken);
        response.OutputStream.Close();
    }

    private sealed record StaticAssetCacheEntry(
        string FilePath,
        string ContentType,
        byte[] Body,
        long Length,
        long LastWriteTimeUtcTicks,
        string ETag,
        DateTimeOffset LastModifiedUtc
    );
}
