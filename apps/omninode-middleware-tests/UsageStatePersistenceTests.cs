using System.Text.Json;
using OmniNode.Middleware;

namespace OmniNode.Middleware.Tests;

public sealed class UsageStatePersistenceTests
{
    [Fact]
    public void LoadLlmUsageStateRestoresValidBackupWhenPrimaryIsCorrupt()
    {
        var dir = CreateTempDirectory();
        var path = Path.Combine(dir, "llm_usage.json");
        var backupState = new LlmUsageState
        {
            GroqUsageByModel = new Dictionary<string, GroqUsage>(StringComparer.OrdinalIgnoreCase)
            {
                ["llama-3.3"] = new GroqUsage
                {
                    Requests = 2,
                    PromptTokens = 10,
                    CompletionTokens = 20,
                    TotalTokens = 30
                }
            },
            GeminiUsage = new GeminiUsage
            {
                Requests = 1,
                PromptTokens = 3,
                CompletionTokens = 5,
                TotalTokens = 8
            }
        };

        var backupJson = JsonSerializer.Serialize(backupState, OmniJsonContext.Default.LlmUsageState);
        File.WriteAllText(path, "{broken");
        File.WriteAllText(path + ".bak", backupJson);

        var restored = UsageStatePersistence.LoadLlmUsageState(path);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.GroqUsageByModel["llama-3.3"].Requests);
        Assert.Equal(30, restored.GroqUsageByModel["llama-3.3"].TotalTokens);
        Assert.Equal(1, restored.GeminiUsage.Requests);
        Assert.Equal(backupJson, File.ReadAllText(path));
    }

    [Fact]
    public void LoadCopilotStateRestoresValidBackupWhenPrimaryIsCorrupt()
    {
        var dir = CreateTempDirectory();
        var path = Path.Combine(dir, "copilot_usage.json");
        var backupState = new CopilotState
        {
            SelectedModel = "gpt-5-mini",
            UsageByModel = new Dictionary<string, CopilotUsage>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-5-mini"] = new CopilotUsage
                {
                    Requests = 7
                }
            }
        };

        var backupJson = JsonSerializer.Serialize(backupState, OmniJsonContext.Default.CopilotState);
        File.WriteAllText(path, "{broken");
        File.WriteAllText(path + ".bak", backupJson);

        var restored = UsageStatePersistence.LoadCopilotState(path);

        Assert.NotNull(restored);
        Assert.Equal("gpt-5-mini", restored.SelectedModel);
        Assert.Equal(7, restored.UsageByModel["gpt-5-mini"].Requests);
        Assert.Equal(backupJson, File.ReadAllText(path));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "omninode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
