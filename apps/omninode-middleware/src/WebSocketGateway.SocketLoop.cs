using System.Net;
using System.Net.WebSockets;
using System.Text;

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

                if (await _setupCommandDispatcher.TryHandleAsync(message, remoteDashboardClient, socket, sendLock, cancellationToken))
                {
                    continue;
                }

                if (await _conversationMemoryDispatcher.TryHandleAsync(
                        message,
                        _sessionManager.IsAuthenticated(sessionId),
                        socket,
                        sendLock,
                        cancellationToken
                    ))
                {
                    continue;
                }

                if (!_sessionManager.IsAuthenticated(sessionId))
                {
                    await SendTextAsync(socket, sendLock, "{\"type\":\"error\",\"message\":\"unauthorized\"}", cancellationToken);
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
        var interval = TimeSpan.FromSeconds(Math.Max(1, _config.MetricsPushIntervalSec));
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
                    ? TimeSpan.FromSeconds(Math.Max(3, _config.MetricsPushIntervalSec))
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

            if (window.Count >= _config.WebSocketCommandsPerMinute)
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
            if (total > _config.WebSocketMaxMessageBytes)
            {
                throw new InvalidOperationException($"payload too large (max={_config.WebSocketMaxMessageBytes})");
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
