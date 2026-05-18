using System.Collections.Concurrent;

namespace OmniNode.Middleware;

public sealed class CleanupService
{
    private readonly AppConfig _config;
    private readonly ConcurrentDictionary<string, CleanupPreviewResult> _previews
        = new(StringComparer.Ordinal);

    public CleanupService(AppConfig config)
    {
        _config = config;
    }

    public CleanupPreviewResult PreviewCleanup()
    {
        try
        {
            var candidates = CollectCleanupCandidates()
                .OrderByDescending(item => item.SizeBytes)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToArray();
            var previewId = $"cleanup_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
            var totalSize = candidates.Sum(item => Math.Max(0L, item.SizeBytes));
            var result = new CleanupPreviewResult(
                true,
                candidates.Length == 0
                    ? "정리할 runtime/build 산출물이 없습니다."
                    : $"정리 preview를 생성했습니다: {candidates.Length}개 후보, {totalSize} bytes",
                previewId,
                candidates,
                totalSize
            );
            _previews[previewId] = result;
            return result;
        }
        catch (Exception ex)
        {
            return new CleanupPreviewResult(false, $"Cleanup preview 실패: {ex.Message}", string.Empty, Array.Empty<CleanupCandidate>(), 0, ex.Message);
        }
    }

    public CleanupApplyResult ApplyCleanup(string? previewId)
    {
        var normalizedPreviewId = (previewId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPreviewId)
            || !_previews.TryGetValue(normalizedPreviewId, out var preview))
        {
            return new CleanupApplyResult(false, "유효한 cleanup previewId가 필요합니다.", normalizedPreviewId, 0, 0, Array.Empty<string>(), Array.Empty<string>(), "preview_not_found");
        }

        var removed = new List<string>();
        var failed = new List<string>();
        long removedSize = 0;
        foreach (var candidate in preview.Candidates)
        {
            if (!IsSafeCleanupPath(candidate.Path))
            {
                failed.Add(candidate.Path);
                continue;
            }

            try
            {
                if (Directory.Exists(candidate.Path))
                {
                    removedSize += MeasurePathSize(candidate.Path);
                    Directory.Delete(candidate.Path, recursive: true);
                    removed.Add(candidate.Path);
                }
                else if (File.Exists(candidate.Path))
                {
                    var info = new FileInfo(candidate.Path);
                    removedSize += Math.Max(0L, info.Length);
                    File.Delete(candidate.Path);
                    removed.Add(candidate.Path);
                }
            }
            catch
            {
                failed.Add(candidate.Path);
            }
        }

        var ok = failed.Count == 0;
        return new CleanupApplyResult(
            ok,
            ok ? "Cleanup apply를 완료했습니다." : $"Cleanup 일부 실패: {failed.Count}개",
            normalizedPreviewId,
            removed.Count,
            removedSize,
            removed,
            failed,
            ok ? null : "apply_failed"
        );
    }

    private IReadOnlyList<CleanupCandidate> CollectCleanupCandidates()
    {
        var projectRoot = ResolveCleanupProjectRoot();
        var candidates = new List<CleanupCandidate>();
        AddCleanupCandidateIfExists(candidates, Path.Combine(projectRoot, "apps", ".runtime"), "directory", "apps runtime 산출물");
        AddCleanupCandidateIfExists(candidates, Path.Combine(projectRoot, "workspace", ".runtime"), "directory", "workspace runtime 산출물");

        var appsRoot = Path.Combine(projectRoot, "apps");
        if (Directory.Exists(appsRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(appsRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(directory);
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(".runtime", StringComparison.OrdinalIgnoreCase))
                {
                    AddCleanupCandidateIfExists(candidates, directory, "directory", $"ignored build/runtime directory: {name}");
                }
            }
        }

        foreach (var dsStore in Directory.Exists(projectRoot)
            ? Directory.EnumerateFiles(projectRoot, ".DS_Store", SearchOption.AllDirectories)
            : Array.Empty<string>())
        {
            if (IsPathUnderDirectory(dsStore, Path.Combine(projectRoot, ".git")))
            {
                continue;
            }

            AddCleanupCandidateIfExists(candidates, dsStore, "file", "macOS metadata file");
        }

        return candidates
            .GroupBy(item => Path.GetFullPath(item.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(item => IsSafeCleanupPath(item.Path))
            .ToArray();
    }

    private static void AddCleanupCandidateIfExists(
        List<CleanupCandidate> candidates,
        string path,
        string kind,
        string reason
    )
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                var info = new DirectoryInfo(fullPath);
                candidates.Add(new CleanupCandidate(
                    fullPath,
                    "directory",
                    MeasurePathSize(fullPath),
                    info.LastWriteTimeUtc,
                    reason
                ));
            }
            else if (File.Exists(fullPath))
            {
                var info = new FileInfo(fullPath);
                candidates.Add(new CleanupCandidate(
                    fullPath,
                    kind,
                    Math.Max(0L, info.Length),
                    info.LastWriteTimeUtc,
                    reason
                ));
            }
        }
        catch
        {
        }
    }

    private static long MeasurePathSize(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return Math.Max(0L, new FileInfo(path).Length);
            }

            if (!Directory.Exists(path))
            {
                return 0;
            }

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file =>
                {
                    try
                    {
                        return Math.Max(0L, new FileInfo(file).Length);
                    }
                    catch
                    {
                        return 0L;
                    }
                })
                .Sum();
        }
        catch
        {
            return 0;
        }
    }

    private bool IsSafeCleanupPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var projectRoot = ResolveCleanupProjectRoot();
        if (!IsPathUnderDirectory(fullPath, projectRoot))
        {
            return false;
        }

        var normalized = fullPath.Replace('\\', '/');
        return normalized.Contains("/apps/.runtime/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/apps/.runtime", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/workspace/.runtime/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/workspace/.runtime", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/bin", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/obj", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/.DS_Store", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveCleanupProjectRoot()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_config.DashboardIndexPath))
        {
            var current = new FileInfo(Path.GetFullPath(_config.DashboardIndexPath)).Directory;
            while (current != null)
            {
                candidates.Add(current.FullName);
                current = current.Parent;
            }
        }

        candidates.Add(Environment.CurrentDirectory);
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(Path.Combine(candidate, "apps"))
                    && Directory.Exists(Path.Combine(candidate, "workspace")))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
            }
        }

        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    private static bool IsPathUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
