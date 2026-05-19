namespace OmniNode.Middleware.Infrastructure.Telegram;

using OmniNode.Middleware;

internal sealed record TelegramPseudoCommandRequest(
    string Command,
    IReadOnlyList<InputAttachment>? Attachments,
    IReadOnlyList<string>? WebUrls,
    bool WebSearchEnabled
);

internal delegate Task<string?> TelegramPseudoCommandHandler(string command, CancellationToken cancellationToken);

internal delegate Task<string?> TelegramPseudoCodingCommandHandler(
    string command,
    IReadOnlyList<InputAttachment>? attachments,
    IReadOnlyList<string>? webUrls,
    bool webSearchEnabled,
    CancellationToken cancellationToken
);

internal sealed record TelegramPseudoCommandHandlers(
    Func<string, string> ParseHelpTopic,
    Func<string?, string> BuildHelpText,
    TelegramPseudoCommandHandler HandleProfileAsync,
    TelegramPseudoCommandHandler HandleQuickModelAsync,
    TelegramPseudoCommandHandler HandleLlmControlAsync,
    TelegramPseudoCommandHandler HandleSkillAsync,
    TelegramPseudoCodingCommandHandler HandleCodingAsync,
    TelegramPseudoCommandHandler HandleRefactorAsync,
    TelegramPseudoCommandHandler HandleMemoryAsync,
    TelegramPseudoCommandHandler HandleDoctorAsync,
    TelegramPseudoCommandHandler HandlePlanAsync,
    TelegramPseudoCommandHandler HandleTaskAsync,
    TelegramPseudoCommandHandler HandleNotebookAsync,
    TelegramPseudoCommandHandler HandleRoutineAsync,
    TelegramPseudoCommandHandler HandleMetricsAsync,
    TelegramPseudoCommandHandler HandleKillAsync
);

internal static class TelegramPseudoCommandExecutor
{
    public static async Task<string?> ExecuteAsync(
        TelegramPseudoCommandRequest request,
        TelegramPseudoCommandHandlers handlers,
        CancellationToken cancellationToken
    )
    {
        var command = (request.Command ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (command.StartsWith("/help", StringComparison.OrdinalIgnoreCase)
            || command.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            return handlers.BuildHelpText(handlers.ParseHelpTopic(command));
        }

        if (StartsWithAny(command, "/talk", "/code"))
        {
            return await handlers.HandleProfileAsync(command, cancellationToken);
        }

        if (command.StartsWith("/model", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleQuickModelAsync(command, cancellationToken);
        }

        if (command.StartsWith("/llm", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleLlmControlAsync(command, cancellationToken);
        }

        if (StartsWithAny(command, "/skill", "/skills"))
        {
            return await handlers.HandleSkillAsync(command, cancellationToken);
        }

        if (command.StartsWith("/coding", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleCodingAsync(
                command,
                request.Attachments,
                request.WebUrls,
                request.WebSearchEnabled,
                cancellationToken
            );
        }

        if (command.StartsWith("/refactor", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleRefactorAsync(command, cancellationToken);
        }

        if (command.StartsWith("/memory", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleMemoryAsync(command, cancellationToken);
        }

        if (command.StartsWith("/doctor", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleDoctorAsync(command, cancellationToken);
        }

        if (command.StartsWith("/plan", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandlePlanAsync(command, cancellationToken);
        }

        if (command.StartsWith("/task", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleTaskAsync(command, cancellationToken);
        }

        if (StartsWithAny(command, "/notebook", "/handoff"))
        {
            return await handlers.HandleNotebookAsync(command, cancellationToken);
        }

        if (StartsWithAny(command, "/routine", "/routines"))
        {
            return await handlers.HandleRoutineAsync(command, cancellationToken);
        }

        if (command.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleMetricsAsync(command, cancellationToken);
        }

        if (command.StartsWith("/kill ", StringComparison.OrdinalIgnoreCase))
        {
            return await handlers.HandleKillAsync(command, cancellationToken);
        }

        return null;
    }

    private static bool StartsWithAny(string command, params string[] prefixes)
    {
        return prefixes.Any(prefix => command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
