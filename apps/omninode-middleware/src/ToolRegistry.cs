namespace OmniNode.Middleware;

public sealed record ToolDefinition(
    string ToolId,
    string Description,
    string Group,
    bool EnabledByDefault,
    bool Implemented,
    bool RequiresGeminiApiKey
);

public sealed record ToolAvailability(
    string ToolId,
    string Description,
    string Group,
    bool Enabled,
    string Reason
);

public sealed class ToolRegistry
{
    private readonly RuntimeSettings _runtimeSettings;
    private readonly Dictionary<string, ToolDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);

    public ToolRegistry(RuntimeSettings runtimeSettings)
    {
        _runtimeSettings = runtimeSettings;
        RegisterDefaults();
    }

    public bool TryRegister(ToolDefinition definition)
    {
        var toolId = NormalizeToolId(definition.ToolId);
        if (string.IsNullOrWhiteSpace(toolId) || _definitions.ContainsKey(toolId))
        {
            return false;
        }

        var normalized = definition with
        {
            ToolId = toolId,
            Description = string.IsNullOrWhiteSpace(definition.Description)
                ? toolId
                : definition.Description.Trim(),
            Group = string.IsNullOrWhiteSpace(definition.Group)
                ? "general"
                : definition.Group.Trim().ToLowerInvariant()
        };
        _definitions[toolId] = normalized;
        return true;
    }

    public ToolAvailability? GetAvailability(string toolId)
    {
        var normalized = NormalizeToolId(toolId);
        if (string.IsNullOrWhiteSpace(normalized) || !_definitions.TryGetValue(normalized, out var definition))
        {
            return null;
        }

        return ResolveAvailability(definition);
    }

    public IReadOnlyList<ToolAvailability> GetAvailabilitySnapshot()
    {
        return _definitions.Values
            .Select(ResolveAvailability)
            .OrderBy(x => x.Group, StringComparer.Ordinal)
            .ThenBy(x => x.ToolId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> GetEnabledToolIds()
    {
        return GetAvailabilitySnapshot()
            .Where(x => x.Enabled)
            .Select(x => x.ToolId)
            .ToArray();
    }

    private ToolAvailability ResolveAvailability(ToolDefinition definition)
    {
        if (!definition.EnabledByDefault)
        {
            return new ToolAvailability(
                definition.ToolId,
                definition.Description,
                definition.Group,
                false,
                "disabled_by_default"
            );
        }

        if (!definition.Implemented)
        {
            return new ToolAvailability(
                definition.ToolId,
                definition.Description,
                definition.Group,
                false,
                "not_implemented"
            );
        }

        if (definition.RequiresGeminiApiKey && string.IsNullOrWhiteSpace(_runtimeSettings.GetGeminiApiKey()))
        {
            return new ToolAvailability(
                definition.ToolId,
                definition.Description,
                definition.Group,
                false,
                "gemini_api_key_missing"
            );
        }

        return new ToolAvailability(
            definition.ToolId,
            definition.Description,
            definition.Group,
            true,
            "ready"
        );
    }

    private void RegisterDefaults()
    {
        _ = TryRegister(new ToolDefinition(
            "web_search",
            "Searches web results via Gemini Google Search grounding",
            "web",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: true
        ));
        _ = TryRegister(new ToolDefinition(
            "web_fetch",
            "Fetches page snippets for referenced URLs",
            "web",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "memory_search",
            "Searches memory index with citations",
            "memory",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "memory_get",
            "Reads memory content by path and line window",
            "memory",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "sessions_list",
            "Lists stored sessions with optional filters",
            "sessions",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "sessions_history",
            "Returns message history for a target session",
            "sessions",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "sessions_send",
            "Sends a message to a target session",
            "sessions",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "sessions_spawn",
            "Spawns an isolated sub-agent session",
            "sessions",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "cron",
            "Manages routine-backed cron jobs (status/list/add/update/run/runs/wake/remove)",
            "automation",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "browser",
            "Runs browser actions with auto Playwright adapter fallback to stub (status/start/stop/tabs/navigate/open/focus/close)",
            "browser",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "canvas",
            "Runs canvas actions via stub adapter (status/present/hide/navigate/eval/snapshot/a2ui_push/a2ui_reset)",
            "canvas",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
        _ = TryRegister(new ToolDefinition(
            "nodes",
            "Runs nodes actions via stub adapter (status/describe/pending/approve/reject/notify/invoke)",
            "nodes",
            EnabledByDefault: true,
            Implemented: true,
            RequiresGeminiApiKey: false
        ));
    }

    private static string NormalizeToolId(string? toolId)
    {
        return string.IsNullOrWhiteSpace(toolId)
            ? string.Empty
            : toolId.Trim().ToLowerInvariant();
    }
}
