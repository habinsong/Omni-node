using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OmniNode.Middleware;

internal static class SecretLoader
{
    public static string? ResolveApiKey(
        string providerName,
        string directEnvKey,
        string fileEnvKey,
        string keychainServiceEnvKey,
        string keychainAccountEnvKey,
        string? defaultKeychainService = null,
        string? defaultKeychainAccount = null
    )
    {
        var service = Environment.GetEnvironmentVariable(keychainServiceEnvKey);
        var account = Environment.GetEnvironmentVariable(keychainAccountEnvKey);
        if (string.IsNullOrWhiteSpace(service))
        {
            service = defaultKeychainService;
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            account = defaultKeychainAccount;
        }

        if (OperatingSystem.IsMacOS()
            && !string.IsNullOrWhiteSpace(service)
            && !string.IsNullOrWhiteSpace(account))
        {
            var keychainValue = ReadFromMacOsKeychain(service!, account!);
            if (!string.IsNullOrWhiteSpace(keychainValue))
            {
                return keychainValue;
            }

            Console.Error.WriteLine($"[secrets] {providerName} keychain lookup failed, fallback source will be used.");
        }

        if (!string.IsNullOrWhiteSpace(service) && !string.IsNullOrWhiteSpace(account))
        {
            var localSecureValue = ReadFromLocalSecureStore(service!, account!);
            if (!string.IsNullOrWhiteSpace(localSecureValue))
            {
                return localSecureValue;
            }
        }

        var filePath = Environment.GetEnvironmentVariable(fileEnvKey);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var fileValue = ReadFromProtectedFile(providerName, filePath!);
            if (!string.IsNullOrWhiteSpace(fileValue))
            {
                return fileValue;
            }
        }

        var direct = Environment.GetEnvironmentVariable(directEnvKey);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            Console.Error.WriteLine($"[secrets] {providerName} direct env key is deprecated. use keychain or *_API_KEY_FILE.");
            return direct.Trim();
        }

        return null;
    }

    public static bool TryWritePlatformSecret(string service, string account, string secret)
    {
        if (OperatingSystem.IsMacOS())
        {
            return TryWriteMacOsKeychainSecret(service, account, secret);
        }

        return TryWriteLocalSecureStore(service, account, secret);
    }

    public static bool TryDeletePlatformSecret(string service, string account)
    {
        if (OperatingSystem.IsMacOS())
        {
            return TryDeleteMacOsKeychainSecret(service, account);
        }

        return TryDeleteLocalSecureStore(service, account);
    }

    private static string? ReadFromMacOsKeychain(string service, string account)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("find-generic-password");
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(service);
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(account);
            startInfo.ArgumentList.Add("-w");

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static bool TryWriteMacOsKeychainSecret(string service, string account, string secret)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("add-generic-password");
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(service);
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(account);
            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add(secret);
            startInfo.ArgumentList.Add("-U");

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return false;
            }

            var verify = ReadFromMacOsKeychain(service, account);
            return !string.IsNullOrWhiteSpace(verify) && verify.Trim() == secret.Trim();
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDeleteMacOsKeychainSecret(string service, string account)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("delete-generic-password");
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(service);
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(account);

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                return true;
            }

            var stderr = process.StandardError.ReadToEnd();
            return stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
                   || stderr.Contains("The specified item could not be found", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWriteLocalSecureStore(string service, string account, string secret)
    {
        try
        {
            var storePath = GetLocalSecureStorePath();
            var store = ReadLocalStore(storePath);
            store[$"{service}:{account}"] = secret.Trim();
            return WriteLocalStore(storePath, store);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteLocalSecureStore(string service, string account)
    {
        try
        {
            var storePath = GetLocalSecureStorePath();
            var store = ReadLocalStore(storePath);
            var removed = store.Remove($"{service}:{account}");
            if (!removed)
            {
                return true;
            }

            return WriteLocalStore(storePath, store);
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadFromLocalSecureStore(string service, string account)
    {
        try
        {
            var storePath = GetLocalSecureStorePath();
            if (!File.Exists(storePath))
            {
                return null;
            }

            if (!HasStrictFilePermission(storePath))
            {
                return null;
            }

            var store = ReadLocalStore(storePath);
            if (!store.TryGetValue($"{service}:{account}", out var value))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string GetLocalSecureStorePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.Combine(appData, "omninode", "secrets.json");
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return Path.Combine(Path.GetTempPath(), "omninode_secrets.json");
        }

        return Path.Combine(home, ".config", "omninode", "secrets.json");
    }

    private static Dictionary<string, string> ReadLocalStore(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = JsonSerializer.Deserialize(json, OmniJsonContext.Default.DictionaryStringString);
        return result ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static bool WriteLocalStore(string path, Dictionary<string, string> store)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return false;
        }

        Directory.CreateDirectory(dir);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var json = JsonSerializer.Serialize(store, OmniJsonContext.Default.DictionaryStringString);
        AtomicFileStore.WriteAllText(path, json, ownerOnly: true);

        return true;
    }

    private static string? ReadFromProtectedFile(string providerName, string filePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"[secrets] {providerName} secret file not found.");
                return null;
            }

            if (!HasStrictFilePermission(fullPath))
            {
                Console.Error.WriteLine($"[secrets] {providerName} secret file permission must be 0600 or stricter.");
                return null;
            }

            var value = File.ReadAllText(fullPath).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                Console.Error.WriteLine($"[secrets] {providerName} secret file is empty.");
                return null;
            }

            return value;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[secrets] {providerName} secret file load failed: {ex.Message}");
            return null;
        }
    }

    private static bool HasStrictFilePermission(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            var insecureBits =
                UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherWrite
                | UnixFileMode.OtherExecute;

            return (mode & insecureBits) == 0;
        }
        catch
        {
            return false;
        }
    }
}
