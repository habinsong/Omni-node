namespace OmniNode.Middleware;

internal static class RemoteLimitedMessagePolicy
{
    private static readonly HashSet<string> AllowedMessageTypes = new(StringComparer.Ordinal)
    {
        "list_conversations",
        "get_conversation",
        "list_memory_notes",
        "read_memory_note",
        "memory_search",
        "conversation_search",
        "memory_get",
        "context_scan",
        "skills_list",
        "commands_list",
        "notebook_get"
    };

    public static bool IsAllowed(string? messageType)
    {
        return !string.IsNullOrWhiteSpace(messageType)
               && AllowedMessageTypes.Contains(messageType);
    }
}
