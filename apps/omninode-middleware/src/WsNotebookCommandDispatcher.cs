using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsNotebookCommandDispatcher
{
    internal delegate Task SendNotebookResultDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string action,
        NotebookActionResult result,
        CancellationToken cancellationToken
    );

    private readonly INotebookApplicationService _notebookService;
    private readonly SendNotebookResultDelegate _sendNotebookResultAsync;

    public WsNotebookCommandDispatcher(
        INotebookApplicationService notebookService,
        SendNotebookResultDelegate sendNotebookResultAsync
    )
    {
        _notebookService = notebookService;
        _sendNotebookResultAsync = sendNotebookResultAsync;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "notebook_get")
        {
            await _sendNotebookResultAsync(
                socket,
                sendLock,
                "get",
                await _notebookService.GetNotebookAsync(message.ProjectKey, cancellationToken),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "notebook_append")
        {
            var kind = (message.Kind ?? string.Empty).Trim().ToLowerInvariant();
            var content = message.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(message.Source)
                || !string.IsNullOrWhiteSpace(message.ConversationId)
                || !string.IsNullOrWhiteSpace(message.Provider)
                || !string.IsNullOrWhiteSpace(message.Model)
                || !string.IsNullOrWhiteSpace(message.Language)
                || !string.IsNullOrWhiteSpace(message.FilePath))
            {
                content = BuildMetadataEntry(message, content);
            }
            var result = kind switch
            {
                "learning" => await _notebookService.AppendLearningAsync(message.ProjectKey, content, cancellationToken),
                "decision" => await _notebookService.AppendDecisionAsync(message.ProjectKey, content, cancellationToken),
                "verification" => await _notebookService.AppendVerificationAsync(message.ProjectKey, content, cancellationToken),
                _ => new NotebookActionResult(false, "kind는 learning, decision, verification 중 하나여야 합니다.", null)
            };
            await _sendNotebookResultAsync(socket, sendLock, "append", result, cancellationToken);
            return true;
        }

        if (message.Type == "handoff_create")
        {
            await _sendNotebookResultAsync(
                socket,
                sendLock,
                "handoff",
                await _notebookService.CreateHandoffAsync(message.ProjectKey, cancellationToken),
                cancellationToken
            );
            return true;
        }

        return false;
    }

    private static string BuildMetadataEntry(WebSocketGateway.ClientMessage message, string content)
    {
        var lines = new List<string>();
        AddMeta(lines, "source", message.Source);
        AddMeta(lines, "conversation", message.ConversationId);
        AddMeta(lines, "provider", message.Provider);
        AddMeta(lines, "model", message.Model);
        AddMeta(lines, "language", message.Language);
        AddMeta(lines, "file", message.FilePath);
        if (message.Tags.Count > 0)
        {
            AddMeta(lines, "tags", string.Join(", ", message.Tags));
        }

        if (lines.Count == 0)
        {
            return content;
        }

        return string.Join("\n", lines) + "\n\n" + (content ?? string.Empty).Trim();
    }

    private static void AddMeta(List<string> lines, string label, string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        lines.Add($"- {label}: {normalized}");
    }
}
