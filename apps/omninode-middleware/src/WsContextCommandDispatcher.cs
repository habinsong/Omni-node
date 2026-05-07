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
    private readonly SkillFileService _skillFileService;
    private readonly SendProjectContextDelegate _sendProjectContextAsync;
    private readonly SendSkillsListDelegate _sendSkillsListAsync;
    private readonly SendCommandsListDelegate _sendCommandsListAsync;

    public WsContextCommandDispatcher(
        IContextApplicationService contextService,
        SkillFileService skillFileService,
        SendProjectContextDelegate sendProjectContextAsync,
        SendSkillsListDelegate sendSkillsListAsync,
        SendCommandsListDelegate sendCommandsListAsync
    )
    {
        _contextService = contextService;
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
            var result = _skillFileService.Get(message.SkillName, message.SkillScope);
            await SendJsonAsync(socket, sendLock, "skill_get_result", JsonSerializer.SerializeToElement(result), cancellationToken);
            return true;
        }

        if (message.Type == "skill_save")
        {
            var result = _skillFileService.Save(message.SkillName, message.SkillScope, message.SkillDescription, message.SkillBody);
            await SendJsonAsync(socket, sendLock, "skill_save_result", JsonSerializer.SerializeToElement(result), cancellationToken);
            return true;
        }

        if (message.Type == "skill_delete")
        {
            var result = _skillFileService.Delete(message.SkillName, message.SkillScope);
            await SendJsonAsync(socket, sendLock, "skill_delete_result", JsonSerializer.SerializeToElement(result), cancellationToken);
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
}
