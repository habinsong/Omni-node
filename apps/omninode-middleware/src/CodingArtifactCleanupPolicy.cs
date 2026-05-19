namespace OmniNode.Middleware;

internal static class CodingArtifactCleanupPolicy
{
    public static string[] CleanupRedundantSingleFileArtifacts(
        string workspaceRoot,
        string objective,
        IReadOnlyList<string> requestedPaths,
        IReadOnlyList<string> changedFiles,
        Func<string, string, string> resolveWorkspacePath
    )
    {
        if (requestedPaths.Count != 1 || !CodingFallbackPolicy.HasSingleFileIntent(objective) || changedFiles.Count == 0)
        {
            return NormalizeChangedFiles(changedFiles);
        }

        string requestedFullPath;
        try
        {
            requestedFullPath = resolveWorkspacePath(workspaceRoot, requestedPaths[0]);
        }
        catch
        {
            return NormalizeChangedFiles(changedFiles);
        }

        if (!File.Exists(requestedFullPath))
        {
            return NormalizeChangedFiles(changedFiles);
        }

        var requestedExtension = Path.GetExtension(requestedFullPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedWorkspaceRoot = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cleaned = new List<string>();
        foreach (var path in changedFiles)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            if (string.Equals(fullPath, requestedFullPath, comparison))
            {
                cleaned.Add(fullPath);
                continue;
            }

            var parent = (Path.GetDirectoryName(fullPath) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileName = Path.GetFileName(fullPath);
            var shouldDelete =
                string.Equals(parent, normalizedWorkspaceRoot, comparison)
                && IsGenericCodingFallbackFileName(fileName)
                && string.Equals(Path.GetExtension(fullPath), requestedExtension, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath);

            if (!shouldDelete)
            {
                cleaned.Add(fullPath);
                continue;
            }

            try
            {
                File.Delete(fullPath);
            }
            catch
            {
                cleaned.Add(fullPath);
            }
        }

        return NormalizeChangedFiles(cleaned);
    }

    public static bool IsGenericCodingFallbackFileName(string fileName)
    {
        return fileName is "main.py" or "main.js" or "main.c" or "main.cpp" or "Program.cs" or "Main.java" or "Main.kt" or "run.sh" or "index.html";
    }

    private static string[] NormalizeChangedFiles(IEnumerable<string> changedFiles)
    {
        return changedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
