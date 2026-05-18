using System.Globalization;

namespace OmniNode.Middleware;

public sealed partial class WebSocketGateway
{
    private void SetGatewayHealthState(
        string status,
        string listenerPrefix,
        bool listenerBound,
        bool degradedMode,
        int? listenerErrorCode = null,
        string? listenerErrorMessage = null
    )
    {
        lock (_gatewayHealthLock)
        {
            _gatewayHealthStatus = status;
            _gatewayListenerPrefix = listenerPrefix;
            _gatewayListenerBound = listenerBound;
            _gatewayDegradedMode = degradedMode;
            _gatewayListenerErrorCode = listenerErrorCode;
            _gatewayListenerErrorMessage = listenerErrorMessage;
        }

        WriteGatewayHealthSnapshot(
            status,
            listenerPrefix,
            listenerBound,
            degradedMode,
            listenerErrorCode,
            listenerErrorMessage
        );
    }

    private void RefreshGatewayHealthSnapshot()
    {
        string status;
        string listenerPrefix;
        bool listenerBound;
        bool degradedMode;
        int? listenerErrorCode;
        string? listenerErrorMessage;

        lock (_gatewayHealthLock)
        {
            status = _gatewayHealthStatus;
            listenerPrefix = _gatewayListenerPrefix;
            listenerBound = _gatewayListenerBound;
            degradedMode = _gatewayDegradedMode;
            listenerErrorCode = _gatewayListenerErrorCode;
            listenerErrorMessage = _gatewayListenerErrorMessage;
        }

        if (string.IsNullOrWhiteSpace(listenerPrefix))
        {
            return;
        }

        WriteGatewayHealthSnapshot(
            status,
            listenerPrefix,
            listenerBound,
            degradedMode,
            listenerErrorCode,
            listenerErrorMessage
        );
    }

    private void MarkWebSocketAccepted()
    {
        var acceptedCount = Interlocked.Increment(ref _webSocketAcceptedCount);
        Interlocked.Exchange(ref _lastWebSocketAcceptedUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (acceptedCount == 1)
        {
            RefreshGatewayHealthSnapshot();
        }
    }

    private void MarkWebSocketRoundTrip()
    {
        var roundTripCount = Interlocked.Increment(ref _webSocketRoundTripCount);
        Interlocked.Exchange(ref _lastWebSocketRoundTripUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (roundTripCount == 1)
        {
            Console.WriteLine("[web] ready probe satisfied: websocket ping/pong round-trip observed");
            RefreshGatewayHealthSnapshot();
        }
    }

    private static string BuildTimestampJsonValue(long unixTimeMilliseconds)
    {
        if (unixTimeMilliseconds <= 0)
        {
            return "null";
        }

        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).ToString("O");
        return $"\"{EscapeJson(timestamp)}\"";
    }

    private static bool TryResolveProbeStatus(string path, out string probeStatus)
    {
        switch (path)
        {
            case "/health":
            case "/healthz":
                probeStatus = "live";
                return true;
            case "/ready":
            case "/readyz":
                probeStatus = "ready";
                return true;
            default:
                probeStatus = string.Empty;
                return false;
        }
    }

    private bool IsReadyProbeSatisfied()
    {
        return Interlocked.Read(ref _webSocketRoundTripCount) > 0;
    }

    private string BuildProbeResponseJson(string probeStatus, bool ok)
    {
        var acceptedCount = Interlocked.Read(ref _webSocketAcceptedCount);
        var roundTripCount = Interlocked.Read(ref _webSocketRoundTripCount);
        var lastAccepted = Interlocked.Read(ref _lastWebSocketAcceptedUnixMs);
        var lastRoundTrip = Interlocked.Read(ref _lastWebSocketRoundTripUnixMs);
        var ready = IsReadyProbeSatisfied();
        var reason = probeStatus == "ready" && !ok ? "\"awaiting_websocket_roundtrip\"" : "null";
        return "{"
            + $"\"ok\":{(ok ? "true" : "false")},"
            + $"\"status\":\"{EscapeJson(ok ? "ok" : "not_ready")}\","
            + $"\"probe\":\"{EscapeJson(probeStatus)}\","
            + $"\"webSocketPath\":\"{EscapeJson(WebSocketPath)}\","
            + $"\"webSocketAcceptedCount\":{acceptedCount},"
            + $"\"webSocketRoundTripCount\":{roundTripCount},"
            + $"\"lastWebSocketAcceptedUtc\":{BuildTimestampJsonValue(lastAccepted)},"
            + $"\"lastWebSocketRoundTripUtc\":{BuildTimestampJsonValue(lastRoundTrip)},"
            + $"\"ready\":{(ready ? "true" : "false")},"
            + $"\"reason\":{reason}"
            + "}";
    }

    private void WriteGatewayHealthSnapshot(
        string status,
        string listenerPrefix,
        bool listenerBound,
        bool degradedMode,
        int? listenerErrorCode = null,
        string? listenerErrorMessage = null
    )
    {
        try
        {
            var acceptedCount = Interlocked.Read(ref _webSocketAcceptedCount);
            var roundTripCount = Interlocked.Read(ref _webSocketRoundTripCount);
            var lastAccepted = Interlocked.Read(ref _lastWebSocketAcceptedUnixMs);
            var lastRoundTrip = Interlocked.Read(ref _lastWebSocketRoundTripUnixMs);
            var readyForProbe = IsReadyProbeSatisfied();
            var readyReason = readyForProbe ? "null" : "\"awaiting_websocket_roundtrip\"";
            var payload = "{"
                + $"\"status\":\"{EscapeJson(status)}\","
                + $"\"listenerBound\":{(listenerBound ? "true" : "false")},"
                + $"\"degradedMode\":{(degradedMode ? "true" : "false")},"
                + $"\"port\":{_port},"
                + $"\"prefix\":\"{EscapeJson(listenerPrefix)}\","
                + $"\"healthEndpointPath\":{(_gatewayOptions.EnableHealthEndpoint ? "\"/healthz\"" : "null")},"
                + $"\"readyEndpointPath\":{(_gatewayOptions.EnableHealthEndpoint ? "\"/readyz\"" : "null")},"
                + $"\"webSocketPath\":\"{EscapeJson(WebSocketPath)}\","
                + $"\"webSocketAcceptedCount\":{acceptedCount},"
                + $"\"webSocketRoundTripCount\":{roundTripCount},"
                + $"\"lastWebSocketAcceptedUtc\":{BuildTimestampJsonValue(lastAccepted)},"
                + $"\"lastWebSocketRoundTripUtc\":{BuildTimestampJsonValue(lastRoundTrip)},"
                + $"\"webSocketReady\":{(readyForProbe ? "true" : "false")},"
                + $"\"readyReason\":{readyReason},"
                + $"\"listenerErrorCode\":{(listenerErrorCode.HasValue ? listenerErrorCode.Value.ToString(CultureInfo.InvariantCulture) : "null")},"
                + $"\"listenerErrorMessage\":{(listenerErrorMessage == null ? "null" : $"\"{EscapeJson(listenerErrorMessage)}\"")},"
                + $"\"updatedAtUtc\":\"{EscapeJson(DateTimeOffset.UtcNow.ToString("O"))}\""
                + "}";
            AtomicFileStore.WriteAllText(_paths.GatewayHealthStatePath, payload, ownerOnly: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[web] health snapshot write failed (path={_paths.GatewayHealthStatePath}): {ex.Message}"
            );
        }
    }
}
