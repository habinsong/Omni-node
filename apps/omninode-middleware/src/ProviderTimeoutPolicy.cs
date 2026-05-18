namespace OmniNode.Middleware;

internal static class ProviderTimeoutPolicy
{
    public static TimeSpan ResolveSharedHttpTimeout(ProviderOptions providers, ContextOptions context)
    {
        var llmTimeoutMs = Math.Max(5000, context.LlmTimeoutSec * 1000);
        var providerTimeoutMs = Math.Max(
            Math.Max(providers.NvidiaTimeoutSec, providers.CerebrasTimeoutSec),
            45
        ) * 1000;
        var geminiWebTimeoutMs = NormalizeGeminiGroundedTimeoutMs(context.GeminiWebTimeoutMs);
        return TimeSpan.FromMilliseconds(Math.Max(Math.Max(llmTimeoutMs, providerTimeoutMs + 5000), geminiWebTimeoutMs + 5000));
    }

    public static int ResolveSingleChatTimeoutSeconds(
        string normalizedProvider,
        ProviderOptions providers,
        ContextOptions context,
        int? overrideSeconds = null
    )
    {
        if (overrideSeconds.HasValue)
        {
            return Math.Max(1, overrideSeconds.Value);
        }

        return normalizedProvider switch
        {
            "groq" => Math.Max(90, context.LlmTimeoutSec * 3),
            "gemini" => Math.Max(90, context.LlmTimeoutSec * 3),
            "copilot" => Math.Max(120, context.LlmTimeoutSec * 3),
            "codex" => Math.Max(120, context.LlmTimeoutSec * 3),
            "cerebras" => Math.Max(120, providers.CerebrasTimeoutSec * 3),
            "nvidia" => Math.Max(360, providers.NvidiaTimeoutSec * 2),
            _ => Math.Max(8, context.LlmTimeoutSec)
        };
    }

    public static int NormalizeGeminiGroundedTimeoutMs(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            return 30000;
        }

        return Math.Clamp(timeoutMs, 5000, 60000);
    }

    public static int NormalizeGeminiGroundedFirstChunkTimeoutMs(int totalTimeoutMs)
    {
        var normalizedTotal = NormalizeGeminiGroundedTimeoutMs(totalTimeoutMs);
        var derived = Math.Min(7000, Math.Max(3000, normalizedTotal / 4));
        return Math.Clamp(derived, 3000, normalizedTotal);
    }

    public static int NormalizeGeminiUrlContextFirstChunkTimeoutMs(int totalTimeoutMs)
    {
        var normalizedTotal = NormalizeGeminiGroundedTimeoutMs(totalTimeoutMs);
        var derived = Math.Min(30000, Math.Max(8000, normalizedTotal));
        return Math.Clamp(derived, 8000, normalizedTotal);
    }
}
