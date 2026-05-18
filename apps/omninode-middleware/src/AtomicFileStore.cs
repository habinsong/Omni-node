using System.Text;

namespace OmniNode.Middleware;

internal static class AtomicFileStore
{
    private const string BackupSuffix = ".bak";
    private const string LockSuffix = ".lock";

    public static void WriteAllText(string path, string content, bool ownerOnly = true)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            throw new InvalidOperationException("invalid file path");
        }

        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows() && ownerOnly)
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        using var lease = AcquireWriteLease(fullPath, ownerOnly);
        WriteAllTextUnlocked(fullPath, content, ownerOnly);
    }

    public static string? ReadAllTextWithBackup(
        string path,
        Func<string, bool> isValid,
        Encoding? encoding = null,
        string? logScope = null
    )
    {
        var fullPath = Path.GetFullPath(path);
        var backupPath = fullPath + BackupSuffix;

        if (!File.Exists(fullPath))
        {
            return null;
        }

        var effectiveEncoding = encoding ?? Encoding.UTF8;
        try
        {
            var primary = File.ReadAllText(fullPath, effectiveEncoding);
            if (isValid(primary))
            {
                return primary;
            }

            LogRecovery(logScope, fullPath, "primary_invalid");
        }
        catch (Exception ex)
        {
            LogRecovery(logScope, fullPath, $"primary_read_failed: {ex.Message}");
        }

        if (!File.Exists(backupPath))
        {
            return null;
        }

        try
        {
            var backup = File.ReadAllText(backupPath, effectiveEncoding);
            if (!isValid(backup))
            {
                LogRecovery(logScope, backupPath, "backup_invalid");
                return null;
            }

            try
            {
                WriteAllText(fullPath, backup, ownerOnly: true);
            }
            catch (Exception ex)
            {
                LogRecovery(logScope, fullPath, $"primary_restore_failed: {ex.Message}");
            }

            LogRecovery(logScope, fullPath, "restored_from_backup");
            return backup;
        }
        catch (Exception ex)
        {
            LogRecovery(logScope, backupPath, $"backup_read_failed: {ex.Message}");
            return null;
        }
    }

    private static void WriteAllTextUnlocked(string fullPath, string content, bool ownerOnly)
    {
        var tmpPath = fullPath + ".tmp";
        var backupPath = fullPath + BackupSuffix;
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);

        using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        if (!OperatingSystem.IsWindows() && ownerOnly)
        {
            File.SetUnixFileMode(tmpPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        if (File.Exists(fullPath))
        {
            File.Copy(fullPath, backupPath, overwrite: true);
            if (!OperatingSystem.IsWindows() && ownerOnly)
            {
                File.SetUnixFileMode(backupPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        File.Move(tmpPath, fullPath, overwrite: true);
        if (!OperatingSystem.IsWindows() && ownerOnly)
        {
            File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static FileStream AcquireWriteLease(string fullPath, bool ownerOnly)
    {
        var lockPath = fullPath + LockSuffix;
        var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var payload = Encoding.UTF8.GetBytes($"{Environment.ProcessId}\n{DateTimeOffset.UtcNow:O}\n");
            stream.SetLength(0);
            stream.Write(payload, 0, payload.Length);
            stream.Flush(flushToDisk: true);
            if (!OperatingSystem.IsWindows() && ownerOnly)
            {
                File.SetUnixFileMode(lockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void LogRecovery(string? scope, string path, string reason)
    {
        var prefix = string.IsNullOrWhiteSpace(scope) ? "atomic-file-store" : scope.Trim();
        Console.Error.WriteLine($"[{prefix}] state recovery: path={path}; reason={reason}");
    }
}
