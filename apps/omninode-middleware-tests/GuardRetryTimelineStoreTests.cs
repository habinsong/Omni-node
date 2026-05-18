using System.Text.Json;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class GuardRetryTimelineStoreTests
{
    [Fact]
    public void ConstructorRestoresValidBackupWhenPrimaryIsCorrupt()
    {
        var dir = CreateTempDirectory();
        var path = Path.Combine(dir, "guard_retry_timeline.json");
        var backupState = new GuardRetryTimelineStore.GuardRetryTimelinePersistedState
        {
            Entries =
            [
                new GuardRetryTimelineStore.GuardRetryTimelineEntry
                {
                    Id = "entry-1",
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
                    Channel = "chat",
                    RetryRequired = true,
                    RetryAttempt = 1,
                    RetryMaxAttempts = 2,
                    RetryStopReason = "guard_retry"
                }
            ]
        };
        var backupJson = JsonSerializer.Serialize(
            backupState,
            GuardRetryTimelineJsonContext.Default.GuardRetryTimelinePersistedState
        );
        File.WriteAllText(path, "{broken");
        File.WriteAllText(path + ".bak", backupJson);

        var store = new GuardRetryTimelineStore(path);
        var snapshotJson = store.BuildSnapshotJson(channels: new[] { "chat" });

        Assert.Contains("\"totalSamples\":1", snapshotJson);
        Assert.Contains("\"retryRequiredSamples\":1", snapshotJson);
        Assert.Equal(backupJson, File.ReadAllText(path));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "omninode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
