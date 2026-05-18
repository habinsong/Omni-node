using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed partial class WebSocketGateway
{
    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        WebSocket? socket = null;
        string? sessionId = null;
        var sendLock = new SemaphoreSlim(1, 1);
        var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? streamTask = null;

        try
        {
            if (!IsAllowedWebSocketOrigin(context))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.ContentLength64 = 0;
                context.Response.OutputStream.Close();
                return;
            }

            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            socket = wsContext.WebSocket;
            var remoteDashboardClient = IsRemoteDashboardClient(context);
            MarkWebSocketAccepted();
            sessionId = await _authSessionGateway.CreatePendingSessionAsync(
                socket,
                sendLock,
                remoteDashboardClient,
                cancellationToken
            );
            await SendSettingsStateAsync(socket, sendLock, cancellationToken, remoteDashboardClient);
            if (_sessionManager.IsAuthenticated(sessionId))
            {
                await SendGroqModelsAsync(socket, sendLock, cancellationToken);
                await SendCopilotModelsAsync(socket, sendLock, cancellationToken);
                await SendUsageStatsAsync(socket, sendLock, cancellationToken);
                streamTask = StreamMetricsAsync(socket, sendLock, streamCts.Token);
            }

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                string? text;
                try
                {
                    text = await ReceiveTextAsync(socket, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    await SendTextAsync(socket, sendLock, $"{{\"type\":\"error\",\"message\":\"{EscapeJson(ex.Message)}\"}}", cancellationToken);
                    break;
                }

                if (text == null)
                {
                    break;
                }

                if (!TryValidateClientPayloadLimits(text, out var payloadError))
                {
                    await SendTextAsync(
                        socket,
                        sendLock,
                        $"{{\"type\":\"error\",\"message\":\"{EscapeJson(payloadError)}\"}}",
                        cancellationToken
                    );
                    continue;
                }

                var message = ParseClientMessage(text);
                if (message == null)
                {
                    await SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"invalid message format\"}", cancellationToken);
                    continue;
                }

                if (message.Type == "ping")
                {
                    MarkWebSocketRoundTrip();
                    var acceptedCount = Interlocked.Read(ref _webSocketAcceptedCount);
                    var roundTripCount = Interlocked.Read(ref _webSocketRoundTripCount);
                    var nowUtc = DateTimeOffset.UtcNow.ToString("O");
                    await SendTextAsync(
                        socket,
                        sendLock,
                        "{"
                        + "\"type\":\"pong\","
                        + $"\"atUtc\":\"{EscapeJson(nowUtc)}\","
                        + $"\"webSocketAcceptedCount\":{acceptedCount},"
                        + $"\"webSocketRoundTripCount\":{roundTripCount}"
                        + "}",
                        cancellationToken
                    );
                    continue;
                }

                var authDispatch = await _authSessionGateway.TryHandleAsync(
                    message.Type,
                    sessionId,
                    message.Otp,
                    message.AuthToken,
                    message.AuthTtlHours,
                    remoteDashboardClient,
                    socket,
                    sendLock,
                    cancellationToken
                );
                if (authDispatch.Handled)
                {
                    if (authDispatch.Authenticated)
                    {
                        await SendGroqModelsAsync(socket, sendLock, cancellationToken);
                        await SendCopilotModelsAsync(socket, sendLock, cancellationToken);
                        await SendUsageStatsAsync(socket, sendLock, cancellationToken);
                        if (streamTask == null)
                        {
                            streamTask = StreamMetricsAsync(socket, sendLock, streamCts.Token);
                        }
                    }

                    continue;
                }

                var authenticated = _sessionManager.IsAuthenticated(sessionId);
                if (await _setupCommandDispatcher.TryHandleAsync(message, remoteDashboardClient, authenticated, socket, sendLock, cancellationToken))
                {
                    continue;
                }

                if (remoteDashboardClient && !IsAllowedRemoteLimitedMessage(message.Type))
                {
                    await SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"forbidden_remote_limited_action\"}", cancellationToken);
                    continue;
                }

                if (await _conversationMemoryDispatcher.TryHandleAsync(
                        message,
                        authenticated,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (!authenticated)
                {
                    await SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"unauthorized\"}", cancellationToken);
                    continue;
                }

                if (!AllowCommand(sessionId!))
                {
                    await SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"rate_limited\"}", cancellationToken);
                    continue;
                }

                if (message.Type == GuardAlertDispatchMessageType)
                {
                    var guardAlertEventJson = BuildGuardAlertEventJson(message.RawJson);
                    if (string.IsNullOrWhiteSpace(guardAlertEventJson))
                    {
                        await SendTextAsync(
                            socket,
                            sendLock,
                            "{"
                            + $"\"type\":\"{GuardAlertDispatchResultType}\","
                            + "\"ok\":false,"
                            + "\"status\":\"invalid_payload\","
                            + "\"message\":\"guardAlertEvent object is required\","
                            + $"\"schemaVersion\":\"{GuardAlertSchemaVersion}\","
                            + $"\"eventType\":\"{GuardAlertEventType}\","
                            + $"\"attemptedAtUtc\":\"{EscapeJson(DateTimeOffset.UtcNow.ToString("O"))}\","
                            + "\"targets\":[]"
                            + "}",
                            cancellationToken
                        );
                        continue;
                    }

                    var dispatchResult = await DispatchGuardAlertEventAsync(guardAlertEventJson, cancellationToken);
                    await SendTextAsync(
                        socket,
                        sendLock,
                        BuildGuardAlertDispatchResultJson(dispatchResult),
                        cancellationToken
                    );
                    continue;
                }

                if (await _toolCommandDispatcher.TryHandleAsync(
                        message,
                        sessionId!,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _routineCommandDispatcher.TryHandleAsync(
                        message,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _logicCommandDispatcher.TryHandleAsync(
                        message,
                        remoteDashboardClient,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _doctorCommandDispatcher.TryHandleAsync(
                        message,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _planningCommandDispatcher.TryHandleAsync(
                        message,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _taskCommandDispatcher.TryHandleAsync(
                        message,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _refactorCommandDispatcher.TryHandleAsync(
                        message,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _contextCommandDispatcher.TryHandleAsync(
                        message,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _notebookCommandDispatcher.TryHandleAsync(
                        message,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (await _aiCommandDispatcher.TryHandleAsync(
                        message,
                        sessionId!,
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                await SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"unsupported message type\"}", cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ws] client error: {ex.Message}");
        }
        finally
        {
            streamCts.Cancel();
            if (streamTask != null)
            {
                try
                {
                    await streamTask;
                }
                catch
                {
                }
            }

            if (sessionId != null)
            {
                _sessionManager.Remove(sessionId);
                ClearRateWindow(sessionId);
            }

            if (socket != null && socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch
                {
                }
            }

            socket?.Dispose();
            sendLock.Dispose();
            streamCts.Dispose();
        }
    }

    private async Task StreamMetricsAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _gatewayOptions.MetricsPushIntervalSec));
        var warnedCoreUnavailable = false;
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            try
            {
                var metricsRaw = await _settingsService.GetMetricsAsync(cancellationToken);
                await SendMetricsAsync(socket, sendLock, "metrics_stream", metricsRaw, cancellationToken);
                warnedCoreUnavailable = false;
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                var message = ex.Message ?? string.Empty;
                var isConnectionRefused = message.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isConnectionRefused || !warnedCoreUnavailable)
                {
                    Console.Error.WriteLine($"[ws] metrics stream error: {message}");
                }

                warnedCoreUnavailable = warnedCoreUnavailable || isConnectionRefused;
                var delay = isConnectionRefused
                    ? TimeSpan.FromSeconds(Math.Max(3, _gatewayOptions.MetricsPushIntervalSec))
                    : TimeSpan.FromSeconds(1);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private bool AllowCommand(string sessionId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_rateLock)
        {
            if (!_sessionRateMap.TryGetValue(sessionId, out var window))
            {
                _sessionRateMap[sessionId] = new RateWindow(now, 1);
                return true;
            }

            if (now - window.WindowStart >= TimeSpan.FromMinutes(1))
            {
                _sessionRateMap[sessionId] = new RateWindow(now, 1);
                return true;
            }

            if (window.Count >= _gatewayOptions.WebSocketCommandsPerMinute)
            {
                return false;
            }

            _sessionRateMap[sessionId] = window with { Count = window.Count + 1 };
            return true;
        }
    }

    private void ClearRateWindow(string sessionId)
    {
        lock (_rateLock)
        {
            _sessionRateMap.Remove(sessionId);
        }
    }

    private static bool IsRemoteDashboardClient(HttpListenerContext context)
    {
        var address = context.Request.RemoteEndPoint?.Address;
        if (address == null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4()))
        {
            return false;
        }

        return true;
    }

    private static bool IsAllowedWebSocketOrigin(HttpListenerContext context)
    {
        var origin = context.Request.Headers["Origin"];
        if (string.IsNullOrWhiteSpace(origin))
        {
            return !IsRemoteDashboardClient(context);
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var requestHost = context.Request.Url?.Host;
        var requestPort = context.Request.Url?.Port ?? -1;
        if (string.IsNullOrWhiteSpace(requestHost) || requestPort <= 0)
        {
            var hostHeader = context.Request.Headers["Host"];
            if (!string.IsNullOrWhiteSpace(hostHeader))
            {
                var parsed = hostHeader.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                requestHost = parsed.Length > 0 ? parsed[0].Trim('[', ']') : requestHost;
                if (parsed.Length > 1 && int.TryParse(parsed[^1], out var parsedPort))
                {
                    requestPort = parsedPort;
                }
            }
        }

        var originPort = originUri.IsDefaultPort
            ? (originUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : originUri.Port;
        if (requestPort > 0 && originPort != requestPort)
        {
            return false;
        }

        var originHost = originUri.Host.Trim('[', ']');
        var normalizedRequestHost = (requestHost ?? string.Empty).Trim('[', ']');
        if (originHost.Equals(normalizedRequestHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsLoopbackHost(originHost) && IsLoopbackHost(normalizedRequestHost);
    }

    private static bool IsLoopbackHost(string host)
    {
        var normalized = (host ?? string.Empty).Trim().Trim('[', ']').ToLowerInvariant();
        if (normalized is "localhost" or "::1")
        {
            return true;
        }

        if (!IPAddress.TryParse(normalized, out var address))
        {
            return false;
        }

        return IPAddress.IsLoopback(address)
            || (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4()));
    }

    private static bool IsAllowedRemoteLimitedMessage(string? messageType)
    {
        return RemoteLimitedMessagePolicy.IsAllowed(messageType);
    }

    private static bool TryValidateClientPayloadLimits(string json, out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("attachments", out var attachmentsElement)
                || attachmentsElement.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            var count = 0;
            foreach (var item in attachmentsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                count += 1;
                if (count > MaxAttachmentCount)
                {
                    errorMessage = $"too many attachments (max={MaxAttachmentCount})";
                    return false;
                }

                var name = item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? "attachment"
                    : "attachment";
                if (item.TryGetProperty("sizeBytes", out var sizeElement))
                {
                    var sizeBytes = 0L;
                    if (sizeElement.ValueKind == JsonValueKind.Number)
                    {
                        sizeElement.TryGetInt64(out sizeBytes);
                    }
                    else if (sizeElement.ValueKind == JsonValueKind.String)
                    {
                        long.TryParse(sizeElement.GetString(), out sizeBytes);
                    }

                    if (sizeBytes > MaxAttachmentBytes)
                    {
                        errorMessage = $"attachment too large: {name} (max={MaxAttachmentBytes} bytes)";
                        return false;
                    }
                }

                var dataBase64 = item.TryGetProperty("dataBase64", out var dataElement) && dataElement.ValueKind == JsonValueKind.String
                    ? dataElement.GetString() ?? string.Empty
                    : string.Empty;
                if (dataBase64.Length > MaxAttachmentBase64Chars)
                {
                    errorMessage = $"attachment too large: {name} (max={MaxAttachmentBytes} bytes)";
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            return true;
        }

        return true;
    }

    private async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            total += result.Count;
            if (total > _gatewayOptions.WebSocketMaxMessageBytes)
            {
                throw new InvalidOperationException($"payload too large (max={_gatewayOptions.WebSocketMaxMessageBytes})");
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    internal static async Task SendTextAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        string text,
        CancellationToken cancellationToken
    )
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }
}
