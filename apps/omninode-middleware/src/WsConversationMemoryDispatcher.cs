using System.Globalization;
using System.Linq;
using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal sealed class WsConversationMemoryDispatcher
{
    private readonly IConversationApplicationService _conversationService;
    private readonly IMemoryApplicationService _memoryService;
    private readonly Func<WebSocket, SemaphoreSlim, string, string, CancellationToken, Task> _sendConversationsAsync;
    private readonly Func<WebSocket, SemaphoreSlim, string, ConversationThreadView, CancellationToken, Task> _sendConversationDetailAsync;
    private readonly Func<WebSocket, SemaphoreSlim, CancellationToken, Task> _sendMemoryNotesAsync;
    private readonly Func<WebSocket, SemaphoreSlim, string, MemorySearchToolResult, CancellationToken, Task> _sendMemorySearchResultAsync;
    private readonly Func<WebSocket, SemaphoreSlim, string, int?, int?, MemoryGetToolResult, CancellationToken, Task> _sendMemoryGetResultAsync;
    private readonly Func<MemoryNoteSaveResult?, string> _buildMemoryNoteJson;

    public WsConversationMemoryDispatcher(
        IConversationApplicationService conversationService,
        IMemoryApplicationService memoryService,
        Func<WebSocket, SemaphoreSlim, string, string, CancellationToken, Task> sendConversationsAsync,
        Func<WebSocket, SemaphoreSlim, string, ConversationThreadView, CancellationToken, Task> sendConversationDetailAsync,
        Func<WebSocket, SemaphoreSlim, CancellationToken, Task> sendMemoryNotesAsync,
        Func<WebSocket, SemaphoreSlim, string, MemorySearchToolResult, CancellationToken, Task> sendMemorySearchResultAsync,
        Func<WebSocket, SemaphoreSlim, string, int?, int?, MemoryGetToolResult, CancellationToken, Task> sendMemoryGetResultAsync,
        Func<MemoryNoteSaveResult?, string> buildMemoryNoteJson
    )
    {
        _conversationService = conversationService;
        _memoryService = memoryService;
        _sendConversationsAsync = sendConversationsAsync;
        _sendConversationDetailAsync = sendConversationDetailAsync;
        _sendMemoryNotesAsync = sendMemoryNotesAsync;
        _sendMemorySearchResultAsync = sendMemorySearchResultAsync;
        _sendMemoryGetResultAsync = sendMemoryGetResultAsync;
        _buildMemoryNoteJson = buildMemoryNoteJson;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        bool isAuthenticated,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        var messageType = message.Type ?? string.Empty;
        if (!IsConversationMemoryMessage(messageType))
        {
            return false;
        }

        if (!isAuthenticated)
        {
            await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"unauthorized\"}", cancellationToken);
            return true;
        }

        if (message.Type == "list_conversations")
        {
            await _sendConversationsAsync(
                socket,
                sendLock,
                message.Scope ?? "chat",
                message.Mode ?? "single",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "create_conversation")
        {
            var created = _conversationService.CreateConversation(
                message.Scope ?? "chat",
                message.Mode ?? "single",
                message.ConversationTitle,
                message.Project,
                message.Category,
                message.Tags
            );
            await _sendConversationDetailAsync(socket, sendLock, "conversation_created", created, cancellationToken);
            await _sendConversationsAsync(socket, sendLock, created.Scope, created.Mode, cancellationToken);
            return true;
        }

        if (message.Type == "get_conversation")
        {
            if (string.IsNullOrWhiteSpace(message.ConversationId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"conversationId is required\"}", cancellationToken);
                return true;
            }

            var found = _conversationService.GetConversation(message.ConversationId.Trim());
            if (found == null)
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"conversation not found\"}", cancellationToken);
                return true;
            }

            await _sendConversationDetailAsync(socket, sendLock, "conversation_detail", found, cancellationToken);
            return true;
        }

        if (message.Type == "delete_conversation")
        {
            if (string.IsNullOrWhiteSpace(message.ConversationId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"conversationId is required\"}", cancellationToken);
                return true;
            }

            var conversationId = message.ConversationId.Trim();
            var deleted = _conversationService.DeleteConversation(conversationId);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                $"{{\"type\":\"conversation_deleted\",\"ok\":{(deleted ? "true" : "false")},\"conversationId\":\"{WebSocketGateway.EscapeJson(conversationId)}\"}}",
                cancellationToken
            );
            await _sendConversationsAsync(socket, sendLock, message.Scope ?? "chat", message.Mode ?? "single", cancellationToken);
            await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "clear_memory")
        {
            var requestedScope = string.IsNullOrWhiteSpace(message.Scope)
                ? "chat"
                : message.Scope.Trim().ToLowerInvariant();
            var result = _memoryService.ClearMemory(requestedScope, "web");
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"memory_cleared\","
                + "\"ok\":true,"
                + $"\"scope\":\"{WebSocketGateway.EscapeJson(requestedScope)}\","
                + $"\"message\":\"{WebSocketGateway.EscapeJson(result)}\""
                + "}",
                cancellationToken
            );

            var refreshScope = requestedScope == "telegram" ? "chat" : requestedScope;
            if (refreshScope == "all")
            {
                foreach (var targetScope in new[] { "chat", "coding" })
                {
                    foreach (var targetMode in new[] { "single", "orchestration", "multi" })
                    {
                        await _sendConversationsAsync(socket, sendLock, targetScope, targetMode, cancellationToken);
                    }
                }
            }
            else if (refreshScope == "chat" || refreshScope == "coding")
            {
                foreach (var targetMode in new[] { "single", "orchestration", "multi" })
                {
                    await _sendConversationsAsync(socket, sendLock, refreshScope, targetMode, cancellationToken);
                }
            }

            await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "list_memory_notes")
        {
            await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "read_memory_note")
        {
            if (string.IsNullOrWhiteSpace(message.NoteName))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"note name is required\"}", cancellationToken);
                return true;
            }

            var note = _memoryService.ReadMemoryNote(message.NoteName.Trim());
            if (note == null)
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"note not found\"}", cancellationToken);
                return true;
            }

            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"memory_note_content\","
                + $"\"name\":\"{WebSocketGateway.EscapeJson(note.Name)}\","
                + $"\"fullPath\":\"{WebSocketGateway.EscapeJson(note.FullPath)}\","
                + $"\"content\":\"{WebSocketGateway.EscapeJson(note.Content)}\""
                + "}",
                cancellationToken
            );
            return true;
        }

        if (message.Type == "rename_memory_note")
        {
            if (string.IsNullOrWhiteSpace(message.NoteName))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"note name is required\"}", cancellationToken);
                return true;
            }

            if (string.IsNullOrWhiteSpace(message.NewName))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"newName is required\"}", cancellationToken);
                return true;
            }

            var renamed = _memoryService.RenameMemoryNote(message.NoteName.Trim(), message.NewName.Trim());
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"memory_note_renamed\","
                + $"\"ok\":{(renamed.Result.Ok ? "true" : "false")},"
                + $"\"message\":\"{WebSocketGateway.EscapeJson(renamed.Result.Message)}\","
                + $"\"oldName\":\"{WebSocketGateway.EscapeJson(renamed.Result.OldName)}\","
                + $"\"newName\":\"{WebSocketGateway.EscapeJson(renamed.Result.NewName)}\","
                + $"\"relinkedConversations\":{renamed.RelinkedConversations},"
                + $"\"note\":{_buildMemoryNoteJson(renamed.Result.Note)}"
                + "}",
                cancellationToken
            );
            await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "delete_memory_notes")
        {
            var deleted = _memoryService.DeleteMemoryNotes(message.MemoryNotes);
            var removedNamesJson = string.Join(
                ",",
                deleted.RemovedNames.Select(name => $"\"{WebSocketGateway.EscapeJson(name)}\"")
            );
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"memory_note_deleted\","
                + $"\"ok\":{(deleted.Ok ? "true" : "false")},"
                + $"\"message\":\"{WebSocketGateway.EscapeJson(deleted.Message)}\","
                + $"\"requested\":{deleted.Requested},"
                + $"\"removed\":{deleted.Removed},"
                + $"\"unlinkedConversations\":{deleted.UnlinkedConversations},"
                + $"\"removedNames\":[{removedNamesJson}]"
                + "}",
                cancellationToken
            );
            await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            return true;
        }

        if (message.Type == "create_memory_note")
        {
            if (string.IsNullOrWhiteSpace(message.ConversationId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"conversationId is required\"}", cancellationToken);
                return true;
            }

            var conversationId = message.ConversationId.Trim();
            var compactConversation = message.CompactConversation ?? false;
            var created = await _memoryService.CreateMemoryNoteAsync(
                conversationId,
                "web",
                compactConversation,
                cancellationToken
            );
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"memory_note_created\","
                + $"\"ok\":{(created.Ok ? "true" : "false")},"
                + $"\"conversationId\":\"{WebSocketGateway.EscapeJson(conversationId)}\","
                + $"\"compact\":{(compactConversation ? "true" : "false")},"
                + $"\"message\":\"{WebSocketGateway.EscapeJson(created.Message)}\","
                + $"\"note\":{_buildMemoryNoteJson(created.Note)}"
                + "}",
                cancellationToken
            );

            if (created.Ok && created.Conversation != null)
            {
                await _sendConversationDetailAsync(socket, sendLock, "conversation_detail", created.Conversation, cancellationToken);
                await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            }

            return true;
        }

        if (message.Type == "memory_search")
        {
            var query = string.IsNullOrWhiteSpace(message.Query)
                ? message.Text
                : message.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"query is required\"}", cancellationToken);
                return true;
            }

            var result = _memoryService.SearchMemory(
                query.Trim(),
                message.MaxResults,
                message.MinScore
            );
            await _sendMemorySearchResultAsync(socket, sendLock, query.Trim(), result, cancellationToken);
            return true;
        }

        if (message.Type == "conversation_search")
        {
            var query = string.IsNullOrWhiteSpace(message.Query)
                ? message.Text
                : message.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"query is required\"}", cancellationToken);
                return true;
            }

            var result = _conversationService.SearchConversations(query.Trim(), message.MaxResults);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                BuildConversationSearchResultJson(result),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "backup_export_prepare")
        {
            var result = _conversationService.ExportBackup();
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                BuildBackupExportResultJson(result),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "backup_import_preview")
        {
            if (string.IsNullOrWhiteSpace(message.ContentBase64))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"contentBase64 is required\"}", cancellationToken);
                return true;
            }

            var result = _conversationService.PreviewBackupImport(
                message.FileName ?? "backup.zip",
                message.ContentBase64
            );
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                BuildBackupImportPreviewResultJson(result),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "backup_import_apply")
        {
            if (string.IsNullOrWhiteSpace(message.PreviewId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"previewId is required\"}", cancellationToken);
                return true;
            }

            var result = _conversationService.ApplyBackupImport(
                message.PreviewId.Trim(),
                message.Overwrite ?? false
            );
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                BuildBackupImportApplyResultJson(result),
                cancellationToken
            );
            if (result.Ok)
            {
                await _sendConversationsAsync(socket, sendLock, "chat", "single", cancellationToken);
                await _sendConversationsAsync(socket, sendLock, "coding", "single", cancellationToken);
                await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            }

            return true;
        }

        if (message.Type == "memory_get")
        {
            var requestedPath = message.MemoryPath;
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"path is required\"}", cancellationToken);
                return true;
            }

            var trimmedPath = requestedPath.Trim();
            var result = _memoryService.GetMemory(
                trimmedPath,
                message.FromLine,
                message.Lines
            );
            await _sendMemoryGetResultAsync(
                socket,
                sendLock,
                trimmedPath,
                message.FromLine,
                message.Lines,
                result,
                cancellationToken
            );
            return true;
        }

        if (message.Type == "memory_index_rebuild")
        {
            var result = _memoryService.RebuildMemoryIndex();
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                BuildMemoryIndexRebuildResultJson(result),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "update_conversation_meta")
        {
            if (string.IsNullOrWhiteSpace(message.ConversationId))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"conversationId is required\"}", cancellationToken);
                return true;
            }

            try
            {
                var updated = _conversationService.UpdateConversationMetadata(
                    message.ConversationId.Trim(),
                    message.ConversationTitle,
                    message.Project,
                    message.Category,
                    message.Tags
                );
                await _sendConversationDetailAsync(socket, sendLock, "conversation_detail", updated, cancellationToken);
                await _sendConversationsAsync(socket, sendLock, updated.Scope, updated.Mode, cancellationToken);
                await _sendMemoryNotesAsync(socket, sendLock, cancellationToken);
            }
            catch (Exception ex)
            {
                await WebSocketGateway.SendTextAsync(
                    socket,
                    sendLock,
                    $"{{\"type\":\"error\",\"message\":\"{WebSocketGateway.EscapeJson(ex.Message)}\"}}",
                    cancellationToken
                );
            }

            return true;
        }

        if (message.Type == "read_workspace_file")
        {
            if (string.IsNullOrWhiteSpace(message.FilePath))
            {
                await WebSocketGateway.SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"filePath is required\"}", cancellationToken);
                return true;
            }

            var preview = _conversationService.ReadWorkspaceFile(
                message.FilePath.Trim(),
                message.ConversationId,
                120_000
            );
            if (preview == null)
            {
                await WebSocketGateway.SendTextAsync(
                    socket,
                    sendLock,
                    "{"
                    + "\"type\":\"workspace_file_preview\","
                    + "\"ok\":false,"
                    + $"\"conversationId\":\"{WebSocketGateway.EscapeJson(message.ConversationId ?? string.Empty)}\","
                    + $"\"path\":\"{WebSocketGateway.EscapeJson(message.FilePath.Trim())}\","
                    + "\"message\":\"파일을 읽을 수 없습니다. 경로/권한을 확인하세요.\""
                    + "}",
                    cancellationToken
                );
                return true;
            }

            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"workspace_file_preview\","
                + "\"ok\":true,"
                + $"\"conversationId\":\"{WebSocketGateway.EscapeJson(message.ConversationId ?? string.Empty)}\","
                + $"\"path\":\"{WebSocketGateway.EscapeJson(preview.FullPath)}\","
                + $"\"content\":\"{WebSocketGateway.EscapeJson(preview.Content)}\""
                + "}",
                cancellationToken
            );
            return true;
        }

        return false;
    }

    private static bool IsConversationMemoryMessage(string messageType)
    {
        return messageType is "list_conversations"
            or "create_conversation"
            or "get_conversation"
            or "delete_conversation"
            or "clear_memory"
            or "list_memory_notes"
            or "read_memory_note"
            or "rename_memory_note"
            or "delete_memory_notes"
            or "create_memory_note"
            or "memory_search"
            or "conversation_search"
            or "backup_export_prepare"
            or "backup_import_preview"
            or "backup_import_apply"
            or "memory_get"
            or "memory_index_rebuild"
            or "update_conversation_meta"
            or "read_workspace_file";
    }

    private static string BuildMemoryIndexRebuildResultJson(MemoryIndexRebuildResult result)
    {
        var snapshot = result.Snapshot;
        return "{"
            + "\"type\":\"memory_index_rebuild_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"message\":\"{WebSocketGateway.EscapeJson(result.Message)}\","
            + $"\"error\":\"{WebSocketGateway.EscapeJson(result.Error ?? string.Empty)}\","
            + "\"snapshot\":"
            + (snapshot == null
                ? "null"
                : "{"
                  + $"\"dbPath\":\"{WebSocketGateway.EscapeJson(snapshot.DbPath)}\","
                  + $"\"scannedDocuments\":{snapshot.ScannedDocuments},"
                  + $"\"indexedDocuments\":{snapshot.IndexedDocuments},"
                  + $"\"skippedDocuments\":{snapshot.SkippedDocuments},"
                  + $"\"removedDocuments\":{snapshot.RemovedDocuments},"
                  + $"\"memoryDocuments\":{snapshot.MemoryDocuments},"
                  + $"\"sessionDocuments\":{snapshot.SessionDocuments},"
                  + $"\"projectDocuments\":{snapshot.ProjectDocuments},"
                  + $"\"elapsedMs\":{snapshot.ElapsedMs},"
                  + $"\"ftsAvailable\":{(snapshot.FtsAvailable ? "true" : "false")}"
                  + "}")
            + "}";
    }

    private static string BuildConversationSearchResultJson(ConversationSearchResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"conversation_search_result\",");
        builder.Append($"\"query\":\"{WebSocketGateway.EscapeJson(result.Query)}\",");
        builder.Append($"\"disabled\":{(result.Disabled ? "true" : "false")},");
        builder.Append("\"results\":[");
        for (var i = 0; i < result.Results.Count; i++)
        {
            var item = result.Results[i];
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append("{");
            builder.Append($"\"conversationId\":\"{WebSocketGateway.EscapeJson(item.ConversationId)}\",");
            builder.Append($"\"title\":\"{WebSocketGateway.EscapeJson(item.Title)}\",");
            builder.Append($"\"scope\":\"{WebSocketGateway.EscapeJson(item.Scope)}\",");
            builder.Append($"\"mode\":\"{WebSocketGateway.EscapeJson(item.Mode)}\",");
            builder.Append($"\"role\":\"{WebSocketGateway.EscapeJson(item.Role)}\",");
            builder.Append($"\"snippet\":\"{WebSocketGateway.EscapeJson(item.Snippet)}\",");
            builder.Append($"\"updatedUtc\":\"{WebSocketGateway.EscapeJson(item.UpdatedUtc.ToString("O"))}\",");
            builder.Append($"\"messageUtc\":\"{WebSocketGateway.EscapeJson(item.MessageUtc.ToString("O"))}\",");
            builder.Append($"\"score\":{item.Score.ToString("0.####", CultureInfo.InvariantCulture)}");
            builder.Append("}");
        }

        builder.Append("]");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{WebSocketGateway.EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static string BuildBackupExportResultJson(BackupExportResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"backup_export_result\",");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"fileName\":\"{WebSocketGateway.EscapeJson(result.FileName)}\",");
        builder.Append($"\"contentBase64\":\"{WebSocketGateway.EscapeJson(result.ContentBase64)}\",");
        builder.Append($"\"sizeBytes\":{Math.Max(0, result.SizeBytes)},");
        AppendStringArray(builder, "included", result.Included);
        builder.Append(",");
        AppendStringArray(builder, "excluded", result.Excluded);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{WebSocketGateway.EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static string BuildBackupImportPreviewResultJson(BackupImportPreviewResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("{");
        builder.Append("\"type\":\"backup_import_preview_result\",");
        builder.Append($"\"ok\":{(result.Ok ? "true" : "false")},");
        builder.Append($"\"previewId\":\"{WebSocketGateway.EscapeJson(result.PreviewId)}\",");
        builder.Append($"\"fileName\":\"{WebSocketGateway.EscapeJson(result.FileName)}\",");
        builder.Append($"\"conversationCount\":{Math.Max(0, result.ConversationCount)},");
        builder.Append($"\"conversationConflictCount\":{Math.Max(0, result.ConversationConflictCount)},");
        builder.Append($"\"fileCount\":{Math.Max(0, result.FileCount)},");
        AppendStringArray(builder, "conflicts", result.Conflicts);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            builder.Append($",\"error\":\"{WebSocketGateway.EscapeJson(result.Error)}\"");
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static string BuildBackupImportApplyResultJson(BackupImportApplyResult result)
    {
        return "{"
            + "\"type\":\"backup_import_result\","
            + $"\"ok\":{(result.Ok ? "true" : "false")},"
            + $"\"importedConversations\":{Math.Max(0, result.ImportedConversations)},"
            + $"\"skippedConversations\":{Math.Max(0, result.SkippedConversations)},"
            + $"\"overwrittenConversations\":{Math.Max(0, result.OverwrittenConversations)},"
            + $"\"importedFiles\":{Math.Max(0, result.ImportedFiles)},"
            + $"\"skippedFiles\":{Math.Max(0, result.SkippedFiles)},"
            + $"\"error\":\"{WebSocketGateway.EscapeJson(result.Error ?? string.Empty)}\""
            + "}";
    }

    private static void AppendStringArray(System.Text.StringBuilder builder, string name, IReadOnlyList<string> values)
    {
        builder.Append($"\"{WebSocketGateway.EscapeJson(name)}\":[");
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            builder.Append($"\"{WebSocketGateway.EscapeJson(values[i])}\"");
        }

        builder.Append("]");
    }
}
