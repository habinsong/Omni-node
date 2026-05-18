using System.Text.Json;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class AtomicFileStoreTests
{
    [Fact]
    public void WriteAllTextCreatesBackupBeforeReplacingExistingFile()
    {
        var dir = CreateTempDirectory();
        var path = Path.Combine(dir, "state.json");

        AtomicFileStore.WriteAllText(path, "{\"version\":1}");
        AtomicFileStore.WriteAllText(path, "{\"version\":2}");

        Assert.Equal("{\"version\":2}", File.ReadAllText(path));
        Assert.Equal("{\"version\":1}", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void ReadAllTextWithBackupRestoresValidBackupWhenPrimaryIsCorrupt()
    {
        var dir = CreateTempDirectory();
        var path = Path.Combine(dir, "state.json");
        File.WriteAllText(path, "{broken");
        File.WriteAllText(path + ".bak", "{\"ok\":true}");

        var restored = AtomicFileStore.ReadAllTextWithBackup(
            path,
            value =>
            {
                try
                {
                    using var _ = JsonDocument.Parse(value);
                    return true;
                }
                catch
                {
                    return false;
                }
            },
            logScope: "test"
        );

        Assert.Equal("{\"ok\":true}", restored);
        Assert.Equal("{\"ok\":true}", File.ReadAllText(path));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "omninode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
