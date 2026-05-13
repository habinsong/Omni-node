using System.Net.WebSockets;
using System.Text.Json;

namespace OmniNode.Middleware;

internal sealed class WsContextCommandDispatcher
{
    internal delegate Task SendProjectContextDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        ProjectContextSnapshot snapshot,
        CancellationToken cancellationToken
    );

    internal delegate Task SendSkillsListDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        SkillManifestListResult result,
        CancellationToken cancellationToken
    );

    internal delegate Task SendCommandsListDelegate(
        WebSocket socket,
        SemaphoreSlim sendLock,
        CommandTemplateListResult result,
        CancellationToken cancellationToken
    );

    private readonly IContextApplicationService _contextService;
    private readonly IConversationApplicationService _conversationService;
    private readonly SkillFileService _skillFileService;
    private readonly SendProjectContextDelegate _sendProjectContextAsync;
    private readonly SendSkillsListDelegate _sendSkillsListAsync;
    private readonly SendCommandsListDelegate _sendCommandsListAsync;

    public WsContextCommandDispatcher(
        IContextApplicationService contextService,
        IConversationApplicationService conversationService,
        SkillFileService skillFileService,
        SendProjectContextDelegate sendProjectContextAsync,
        SendSkillsListDelegate sendSkillsListAsync,
        SendCommandsListDelegate sendCommandsListAsync
    )
    {
        _contextService = contextService;
        _conversationService = conversationService;
        _skillFileService = skillFileService;
        _sendProjectContextAsync = sendProjectContextAsync;
        _sendSkillsListAsync = sendSkillsListAsync;
        _sendCommandsListAsync = sendCommandsListAsync;
    }

    public async Task<bool> TryHandleAsync(
        WebSocketGateway.ClientMessage message,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (message.Type == "context_scan")
        {
            await _sendProjectContextAsync(
                socket,
                sendLock,
                await _contextService.ScanProjectContextAsync(cancellationToken),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "skills_list")
        {
            await _sendSkillsListAsync(
                socket,
                sendLock,
                await _contextService.ListSkillsAsync(cancellationToken),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "commands_list")
        {
            await _sendCommandsListAsync(
                socket,
                sendLock,
                await _contextService.ListCommandsAsync(cancellationToken),
                cancellationToken
            );
            return true;
        }

        if (message.Type == "skill_get")
        {
            try
            {
                var result = _skillFileService.Get(message.SkillName, message.SkillScope);
                await SendPayloadAsync(socket, sendLock, "skill_get_result", result, cancellationToken);
            }
            catch (Exception ex)
            {
                var result = new SkillFileGetResult(
                    false,
                    $"스킬 읽기 처리 중 오류: {ex.Message}",
                    message.SkillName ?? string.Empty,
                    message.SkillScope ?? "project",
                    string.Empty,
                    string.Empty,
                    string.Empty
                );
                await SendPayloadAsync(socket, sendLock, "skill_get_result", result, cancellationToken);
            }
            return true;
        }

        if (message.Type == "skill_save")
        {
            try
            {
                var result = _skillFileService.Save(
                    message.SkillName,
                    message.SkillScope,
                    message.SkillDescription,
                    message.SkillBody,
                    message.SkillAllowOverwrite == true
                );
                await SendPayloadAsync(socket, sendLock, "skill_save_result", result, cancellationToken);
            }
            catch (Exception ex)
            {
                var result = new SkillFileSaveResult(
                    false,
                    $"스킬 저장 처리 중 오류: {ex.Message}",
                    message.SkillName ?? string.Empty,
                    message.SkillScope ?? "project",
                    string.Empty
                );
                await SendPayloadAsync(socket, sendLock, "skill_save_result", result, cancellationToken);
            }
            return true;
        }

        if (message.Type == "skill_delete")
        {
            try
            {
                var result = _skillFileService.Delete(message.SkillName, message.SkillScope);
                await SendPayloadAsync(socket, sendLock, "skill_delete_result", result, cancellationToken);
            }
            catch (Exception ex)
            {
                var result = new SkillFileDeleteResult(
                    false,
                    $"스킬 삭제 처리 중 오류: {ex.Message}",
                    message.SkillName ?? string.Empty,
                    message.SkillScope ?? "project"
                );
                await SendPayloadAsync(socket, sendLock, "skill_delete_result", result, cancellationToken);
            }
            return true;
        }

        if (message.Type == "skill_active_clear")
        {
            var ok = _conversationService.ClearActiveSkill(message.ConversationId);
            var error = ok ? string.Empty : "활성 스킬을 해제할 대화 ID가 없거나 처리에 실패했습니다.";
            var json = "{"
                       + "\"type\":\"skill_active_clear_result\","
                       + "\"payload\":{"
                       + $"\"ok\":{(ok ? "true" : "false")},"
                       + $"\"conversationId\":\"{WebSocketGateway.EscapeJson(message.ConversationId ?? string.Empty)}\","
                       + $"\"error\":\"{WebSocketGateway.EscapeJson(error)}\""
                       + "}"
                       + "}";
            await WebSocketGateway.SendTextAsync(socket, sendLock, json, cancellationToken);
            return true;
        }

        return false;
    }

    private static async Task SendJsonAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string type,
        JsonElement payload,
        CancellationToken cancellationToken
    )
    {
        using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            foreach (var prop in payload.EnumerateObject())
            {
                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        await WebSocketGateway.SendTextAsync(socket, sendLock, json, cancellationToken);
    }

    private static async Task SendPayloadAsync<T>(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string type,
        T payload,
        CancellationToken cancellationToken
    )
    {
        var payloadJson = payload switch
        {
            SkillFileGetResult value => JsonSerializer.Serialize(value, ContextJsonContext.Default.SkillFileGetResult),
            SkillFileSaveResult value => JsonSerializer.Serialize(value, ContextJsonContext.Default.SkillFileSaveResult),
            SkillFileDeleteResult value => JsonSerializer.Serialize(value, ContextJsonContext.Default.SkillFileDeleteResult),
            _ => throw new InvalidOperationException("지원하지 않는 스킬 응답 형식입니다.")
        };
        var json = $"{{\"type\":\"{type}\",\"payload\":{payloadJson}}}";
        await WebSocketGateway.SendTextAsync(socket, sendLock, json, cancellationToken);
    }
}
