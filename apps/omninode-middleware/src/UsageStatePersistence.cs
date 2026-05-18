using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

internal static class UsageStatePersistence
{
    public static LlmUsageState? LoadLlmUsageState(string path)
    {
        var json = AtomicFileStore.ReadAllTextWithBackup(
            path,
            IsValidLlmUsageStateJson,
            Encoding.UTF8,
            logScope: "usage"
        );
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize(json, OmniJsonContext.Default.LlmUsageState);
    }

    public static CopilotState? LoadCopilotState(string path)
    {
        var json = AtomicFileStore.ReadAllTextWithBackup(
            path,
            IsValidCopilotStateJson,
            Encoding.UTF8,
            logScope: "copilot"
        );
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize(json, OmniJsonContext.Default.CopilotState);
    }

    private static bool IsValidLlmUsageStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            return JsonSerializer.Deserialize(json, OmniJsonContext.Default.LlmUsageState) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidCopilotStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            return JsonSerializer.Deserialize(json, OmniJsonContext.Default.CopilotState) != null;
        }
        catch
        {
            return false;
        }
    }
}
