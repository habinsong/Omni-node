namespace OmniNode.Middleware;

internal enum TelegramLlmControlCommandKind
{
    Help,
    Status,
    SetMode,
    Models,
    Usage,
    SetGroqModel,
    SetCopilotModel,
    SetSingleProviderThenModel,
    SetSingleProvider,
    SetSingleModel,
    SetOrchestrationProvider,
    SetOrchestrationModel,
    SetMultiChannelModel,
    SetMultiSummaryProvider,
    UsageError,
    Unknown
}

/// <summary>
/// 텔레그램 <c>/llm</c> 제어 명령의 토큰 파싱과 alias/usage 메시지 판정을 담당한다.
/// 인스턴스 상태 변경(SetChannelMode/Provider/Model, async report)은 호출부가 수행한다.
/// </summary>
internal sealed record TelegramLlmControlCommand(
    TelegramLlmControlCommandKind Kind,
    string Primary = "",
    string Secondary = "",
    string Message = ""
);

internal static class TelegramLlmControlCommandParser
{
    private const string ModeUsage = "사용법: /llm mode <single|orchestration|multi>";
    private const string SetUsage = "사용법: /llm set <groq|copilot|codex|nvidia> <model-id>";
    private const string SingleUsage =
        "사용법: /llm single provider <groq|gemini|copilot|cerebras|nvidia|codex> | /llm single model <model-id>";
    private const string SingleModelUsage = "usage: /llm single model <model-id>";
    private const string OrchestrationUsage =
        "사용법: /llm orchestration provider <auto|groq|gemini|copilot|cerebras|nvidia|codex> | /llm orchestration model <model-id>";
    private const string OrchestrationModelUsage = "usage: /llm orchestration model <model-id>";
    private const string MultiUsage =
        "사용법: /llm multi <groq|gemini|copilot|cerebras|nvidia|codex> <model-id> | /llm multi summary <auto|groq|gemini|copilot|cerebras|nvidia|codex>";
    private const string UnknownMessage = "알 수 없는 /llm 명령입니다. /llm help 또는 자연어 요청을 사용하세요.";

    public static bool IsControlCommand(string? text)
    {
        return (text ?? string.Empty).StartsWith("/llm", StringComparison.OrdinalIgnoreCase);
    }

    public static TelegramLlmControlCommand Parse(string text)
    {
        var tokens = (text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 || tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.Help);
        }

        var sub = tokens[1];
        if (sub.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.Status);
        }

        if (sub.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 3)
            {
                return UsageError(ModeUsage);
            }

            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.SetMode, tokens[2].ToLowerInvariant());
        }

        if (sub.Equals("models", StringComparison.OrdinalIgnoreCase))
        {
            var target = tokens.Length >= 3 ? tokens[2].Trim().ToLowerInvariant() : "all";
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.Models, target);
        }

        if (sub.Equals("usage", StringComparison.OrdinalIgnoreCase)
            || sub.Equals("limits", StringComparison.OrdinalIgnoreCase)
            || sub.Equals("quota", StringComparison.OrdinalIgnoreCase))
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.Usage);
        }

        if (sub.Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSet(tokens);
        }

        if (sub.Equals("single", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSlot(tokens, "single", SingleUsage, SingleModelUsage);
        }

        if (sub.Equals("orchestration", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSlot(tokens, "orchestration", OrchestrationUsage, OrchestrationModelUsage);
        }

        if (sub.Equals("multi", StringComparison.OrdinalIgnoreCase))
        {
            return ParseMulti(tokens);
        }

        return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.Unknown, Message: UnknownMessage);
    }

    private static TelegramLlmControlCommand ParseSet(string[] tokens)
    {
        if (tokens.Length < 4)
        {
            return UsageError(SetUsage);
        }

        var provider = tokens[2].Trim().ToLowerInvariant();
        var modelId = string.Join(' ', tokens.Skip(3)).Trim();
        if (provider == "groq")
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.SetGroqModel, modelId);
        }

        if (provider == "copilot")
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.SetCopilotModel, modelId);
        }

        if (provider == "codex")
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.SetSingleProviderThenModel, "codex", modelId);
        }

        if (IsNvidiaAlias(provider))
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.SetSingleProviderThenModel, "nvidia", modelId);
        }

        return UsageError(SetUsage);
    }

    private static TelegramLlmControlCommand ParseSlot(string[] tokens, string slot, string slotUsage, string modelUsage)
    {
        if (tokens.Length < 4)
        {
            return UsageError(slotUsage);
        }

        var providerKind = slot == "single"
            ? TelegramLlmControlCommandKind.SetSingleProvider
            : TelegramLlmControlCommandKind.SetOrchestrationProvider;
        var modelKind = slot == "single"
            ? TelegramLlmControlCommandKind.SetSingleModel
            : TelegramLlmControlCommandKind.SetOrchestrationModel;

        if (tokens[2].Equals("provider", StringComparison.OrdinalIgnoreCase))
        {
            return new TelegramLlmControlCommand(providerKind, tokens[3].Trim().ToLowerInvariant());
        }

        if (tokens[2].Equals("model", StringComparison.OrdinalIgnoreCase))
        {
            var model = string.Join(' ', tokens.Skip(3)).Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                return UsageError(modelUsage);
            }

            return new TelegramLlmControlCommand(modelKind, Secondary: model);
        }

        return UsageError(slotUsage);
    }

    private static TelegramLlmControlCommand ParseMulti(string[] tokens)
    {
        if (tokens.Length < 4)
        {
            return UsageError(MultiUsage);
        }

        var key = tokens[2].ToLowerInvariant();
        var value = string.Join(' ', tokens.Skip(3)).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return UsageError(MultiUsage);
        }

        if (key is "groq" or "gemini" or "copilot" or "cerebras" or "codex")
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.SetMultiChannelModel, $"multi.{key}", value);
        }

        if (IsNvidiaAlias(key))
        {
            return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.SetMultiChannelModel, "multi.nvidia", value);
        }

        if (key == "summary")
        {
            return new TelegramLlmControlCommand(
                TelegramLlmControlCommandKind.SetMultiSummaryProvider,
                value.Trim().ToLowerInvariant()
            );
        }

        return UsageError(MultiUsage);
    }

    private static bool IsNvidiaAlias(string value)
    {
        return value is "nvidia" or "nvidia-nim" or "nvidia_nim" or "nim";
    }

    private static TelegramLlmControlCommand UsageError(string message)
    {
        return new TelegramLlmControlCommand(TelegramLlmControlCommandKind.UsageError, Message: message);
    }
}
