using System.Diagnostics;
using System.Globalization;

namespace OmniNode.Middleware;

public sealed class CoreProcessBootstrapper
{
    private readonly AppConfig _config;
    private readonly UdsCoreClient _coreClient;

    public CoreProcessBootstrapper(AppConfig config, UdsCoreClient coreClient)
    {
        _config = config;
        _coreClient = coreClient;
    }

    public async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (await IsCoreResponsiveAsync(cancellationToken))
        {
            return;
        }

        var binaryPath = ResolveCoreBinaryPath();
        if (string.IsNullOrWhiteSpace(binaryPath) || !File.Exists(binaryPath))
        {
            Console.Error.WriteLine($"[core-bootstrap] core binary not found: {binaryPath ?? "(none)"}");
            return;
        }

        var workingDirectory = Path.GetDirectoryName(binaryPath) ?? Directory.GetCurrentDirectory();
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.Environment["OMNINODE_CORE_SOCKET_PATH"] = _config.CoreSocketPath;
        if (UdsCoreClient.TryGetTcpPort(_config.CoreSocketPath, out var port))
        {
            startInfo.Environment["OMNINODE_CORE_TCP_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        }

        using var launchProcess = Process.Start(startInfo);
        if (launchProcess == null)
        {
            Console.Error.WriteLine($"[core-bootstrap] failed to start core binary: {binaryPath}");
            return;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
            if (await IsCoreResponsiveAsync(cancellationToken))
            {
                Console.WriteLine($"[core-bootstrap] core ready endpoint={_config.CoreSocketPath}");
                return;
            }
        }

        Console.Error.WriteLine(
            $"[core-bootstrap] core start attempted but endpoint is still unavailable (endpoint={_config.CoreSocketPath}, binary={binaryPath})"
        );
    }

    private async Task<bool> IsCoreResponsiveAsync(CancellationToken cancellationToken)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(TimeSpan.FromMilliseconds(800));
        try
        {
            await _coreClient.GetMetricsAsync(probeCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveCoreBinaryPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();
        var binaryName = OperatingSystem.IsWindows() ? "omninode_core.exe" : "omninode_core";
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(cwd, "apps", "omninode-core", binaryName)),
            Path.GetFullPath(Path.Combine(cwd, "omninode-core", binaryName)),
            Path.GetFullPath(Path.Combine(baseDir, "../../../../../apps/omninode-core", binaryName)),
            Path.GetFullPath(Path.Combine(baseDir, "../../../../omninode-core", binaryName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
