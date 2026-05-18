using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

internal sealed class GatewayStartupProbe
{
    private const int MaxReceiveBytes = 262_144;
    private const string ProbeModeLive = "live";
    private const string ProbeModeMock = "mock";
    private const int WebSocketPingPongMaxAttemptsCap = 20;
    private readonly GatewayOptions _gatewayOptions;
    private readonly PathOptions _paths;
    private readonly int _port;

    public GatewayStartupProbe(GatewayOptions gatewayOptions, PathOptions paths, int port)
    {
        _gatewayOptions = gatewayOptions;
        _paths = paths;
        _port = port;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_gatewayOptions.EnableGatewayStartupProbe)
        {
            return;
        }

        if (!_gatewayOptions.EnableHealthEndpoint)
        {
            const string reason = "health endpoint disabled";
            Console.WriteLine($"[web] startup probe skipped: {reason}");
            WriteProbeSnapshot(result: "skipped", reason: reason);
            return;
        }

        var probeMode = ResolveProbeMode();
        if (string.Equals(probeMode, ProbeModeMock, StringComparison.Ordinal))
        {
            await RunMockProbeAsync(cancellationToken);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_gatewayOptions.GatewayStartupProbeTimeoutSec));
        var effectiveToken = timeoutCts.Token;

        try
        {
            if (_gatewayOptions.GatewayStartupProbeDelayMs > 0)
            {
                await Task.Delay(_gatewayOptions.GatewayStartupProbeDelayMs, effectiveToken);
            }

            var healthUri = new Uri($"http://127.0.0.1:{_port}/healthz");
            var readyUri = new Uri($"http://127.0.0.1:{_port}/readyz");

            if (!TryCreateProbeHttpClient(out var httpClient, out var httpClientCreateError))
            {
                Console.WriteLine($"[web] startup probe skipped: {httpClientCreateError}");
                WriteProbeSnapshot(result: "skipped", reason: httpClientCreateError);
                return;
            }

            using (httpClient)
            {
                var initialProbeResult = await WaitForInitialProbeEndpointsAsync(
                    httpClient,
                    healthUri,
                    readyUri,
                    effectiveToken
                );
                if (!initialProbeResult.Success || !string.IsNullOrWhiteSpace(initialProbeResult.Reason))
                {
                    var reason = initialProbeResult.Reason ?? "probe endpoint unavailable";
                    Console.WriteLine($"[web] startup probe skipped: {reason}");
                    WriteProbeSnapshot(
                        result: "skipped",
                        reason: reason,
                        healthCode: initialProbeResult.HealthCode,
                        readyBeforeCode: initialProbeResult.ReadyBeforeCode
                    );
                    return;
                }

                if (!initialProbeResult.HealthCode.HasValue || !initialProbeResult.ReadyBeforeCode.HasValue)
                {
                    const string reason = "probe endpoint status missing after readiness wait";
                    Console.WriteLine($"[web] startup probe skipped: {reason}");
                    WriteProbeSnapshot(result: "skipped", reason: reason);
                    return;
                }

                if (initialProbeResult.Attempts > 1)
                {
                    Console.WriteLine(
                        $"[web] startup probe endpoints ready after {initialProbeResult.Attempts} attempts "
                        + $"(pollIntervalMs={Math.Max(50, _gatewayOptions.GatewayStartupProbePollIntervalMs)})"
                    );
                }

                var healthCode = initialProbeResult.HealthCode.Value;
                var readyBeforeCode = initialProbeResult.ReadyBeforeCode.Value;
                var wsPingPongOk = await TryRunWebSocketPingPongAsync(effectiveToken);
                var readyAfterResult = await WaitForReadyAfterProbeAsync(
                    httpClient,
                    readyUri,
                    wsPingPongOk,
                    effectiveToken
                );
                if (!readyAfterResult.Success || !readyAfterResult.ReadyAfterCode.HasValue)
                {
                    var reason = readyAfterResult.Reason ?? "readyz recheck unavailable";
                    if (readyAfterResult.Skip)
                    {
                        Console.WriteLine($"[web] startup probe skipped: {reason}");
                        WriteProbeSnapshot(
                            result: "skipped",
                            reason: reason,
                            healthCode: healthCode,
                            readyBeforeCode: readyBeforeCode,
                            wsPingPongOk: wsPingPongOk
                        );
                        return;
                    }

                    Console.WriteLine(
                        $"[web] startup probe failed: {reason}"
                    );
                    WriteProbeSnapshot(
                        result: "failed",
                        reason: reason,
                        healthCode: healthCode,
                        readyBeforeCode: readyBeforeCode,
                        wsPingPongOk: wsPingPongOk,
                        failedChecks: "readyz_recheck_unavailable"
                    );
                    return;
                }
                var readyAfterCode = readyAfterResult.ReadyAfterCode.Value;
                if (wsPingPongOk && readyAfterResult.Attempts > 1)
                {
                    Console.WriteLine(
                        $"[web] startup probe readyz transitioned after {readyAfterResult.Attempts} attempts "
                        + $"(pollIntervalMs={Math.Max(50, _gatewayOptions.GatewayStartupProbePollIntervalMs)})"
                    );
                }

                var gatewayRoundTripMetricResult = await WaitForGatewayRoundTripMetricAsync(
                    wsPingPongOk,
                    effectiveToken
                );
                if (!gatewayRoundTripMetricResult.Success)
                {
                    var reason = gatewayRoundTripMetricResult.Reason
                        ?? "gateway health snapshot roundtrip count unavailable";
                    if (gatewayRoundTripMetricResult.Skip)
                    {
                        Console.WriteLine($"[web] startup probe skipped: {reason}");
                        WriteProbeSnapshot(
                            result: "skipped",
                            reason: reason,
                            healthCode: healthCode,
                            readyBeforeCode: readyBeforeCode,
                            wsPingPongOk: wsPingPongOk,
                            readyAfterCode: readyAfterCode,
                            gatewayRoundTripCount: gatewayRoundTripMetricResult.RoundTripCount
                        );
                        return;
                    }

                    Console.WriteLine($"[web] startup probe failed: {reason}");
                    WriteProbeSnapshot(
                        result: "failed",
                        reason: reason,
                        healthCode: healthCode,
                        readyBeforeCode: readyBeforeCode,
                        wsPingPongOk: wsPingPongOk,
                        readyAfterCode: readyAfterCode,
                        failedChecks: "gateway_roundtrip_count_unavailable",
                        gatewayRoundTripCount: gatewayRoundTripMetricResult.RoundTripCount
                    );
                    return;
                }

                var gatewayRoundTripCount = gatewayRoundTripMetricResult.RoundTripCount;
                if (wsPingPongOk && gatewayRoundTripCount.HasValue && gatewayRoundTripMetricResult.Attempts > 1)
                {
                    Console.WriteLine(
                        $"[web] startup probe gateway roundtrip metric observed after {gatewayRoundTripMetricResult.Attempts} attempts "
                        + $"(pollIntervalMs={Math.Max(50, _gatewayOptions.GatewayStartupProbePollIntervalMs)}, count={gatewayRoundTripCount.Value.ToString(CultureInfo.InvariantCulture)})"
                    );
                }

                var evaluation = EvaluateProbeResult(
                    healthCode,
                    readyBeforeCode,
                    wsPingPongOk,
                    readyAfterCode,
                    gatewayRoundTripCount
                );
                var probeSummary =
                    $"healthz={healthCode} readyz_before={readyBeforeCode} "
                    + $"ws_ping_pong={(wsPingPongOk ? "ok" : "failed")} readyz_after={readyAfterCode} "
                    + $"gateway_roundtrip_count={(gatewayRoundTripCount.HasValue ? gatewayRoundTripCount.Value.ToString(CultureInfo.InvariantCulture) : "null")} "
                    + $"transition={evaluation.Transition}";
                Console.WriteLine($"[web] startup probe {probeSummary}");
                if (evaluation.Passed)
                {
                    Console.WriteLine($"[web] startup probe result=ok {probeSummary}");
                    WriteProbeSnapshot(
                        result: "ok",
                        reason: "all_checks_passed",
                        healthCode: healthCode,
                        readyBeforeCode: readyBeforeCode,
                        wsPingPongOk: wsPingPongOk,
                        readyAfterCode: readyAfterCode,
                        transition: evaluation.Transition,
                        failedChecks: evaluation.FailedChecks,
                        gatewayRoundTripCount: gatewayRoundTripCount
                    );
                }
                else
                {
                    Console.Error.WriteLine(
                        $"[web] startup probe result=failed {probeSummary} checks={evaluation.FailedChecks}"
                    );
                    WriteProbeSnapshot(
                        result: "failed",
                        reason: "probe_checks_failed",
                        healthCode: healthCode,
                        readyBeforeCode: readyBeforeCode,
                        wsPingPongOk: wsPingPongOk,
                        readyAfterCode: readyAfterCode,
                        transition: evaluation.Transition,
                        failedChecks: evaluation.FailedChecks,
                        gatewayRoundTripCount: gatewayRoundTripCount
                    );
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine(
                    $"[web] startup probe timeout ({_gatewayOptions.GatewayStartupProbeTimeoutSec}s)"
                );
                WriteProbeSnapshot(
                    result: "timeout",
                    reason: $"timeout ({_gatewayOptions.GatewayStartupProbeTimeoutSec}s)"
                );
            }
        }
        catch (Exception ex)
        {
            var networkInitReason = GetNetworkInitializationFailureReason(ex);
            if (networkInitReason != null)
            {
                Console.WriteLine($"[web] startup probe skipped: {networkInitReason}");
                WriteProbeSnapshot(result: "skipped", reason: networkInitReason);
                return;
            }

            Console.Error.WriteLine($"[web] startup probe failed: {ex.Message}");
            WriteProbeSnapshot(result: "error", reason: ex.Message);
        }
    }

    private async Task RunMockProbeAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_gatewayOptions.GatewayStartupProbeTimeoutSec));
        var effectiveToken = timeoutCts.Token;

        try
        {
            if (_gatewayOptions.GatewayStartupProbeDelayMs > 0)
            {
                await Task.Delay(_gatewayOptions.GatewayStartupProbeDelayMs, effectiveToken);
            }

            var evaluation = EvaluateProbeResult(
                healthCode: 200,
                readyBeforeCode: 503,
                wsPingPongOk: true,
                readyAfterCode: 200,
                gatewayRoundTripCount: 1
            );
            var probeSummary =
                "healthz=200 readyz_before=503 ws_ping_pong=ok readyz_after=200 gateway_roundtrip_count=1 "
                + $"transition={evaluation.Transition} mode={ProbeModeMock} simulated=true";
            Console.WriteLine("[web] startup probe mode=mock (development/test only)");
            Console.WriteLine($"[web] startup probe {probeSummary}");
            Console.WriteLine($"[web] startup probe result=ok {probeSummary}");
            WriteProbeSnapshot(
                result: "ok",
                reason: "mock_probe_all_checks_passed",
                healthCode: 200,
                readyBeforeCode: 503,
                wsPingPongOk: true,
                readyAfterCode: 200,
                transition: evaluation.Transition,
                failedChecks: evaluation.FailedChecks,
                gatewayRoundTripCount: 1
            );
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine(
                    $"[web] startup probe timeout ({_gatewayOptions.GatewayStartupProbeTimeoutSec}s)"
                );
                WriteProbeSnapshot(
                    result: "timeout",
                    reason: $"timeout ({_gatewayOptions.GatewayStartupProbeTimeoutSec}s)"
                );
            }
        }
    }

    private string ResolveProbeMode()
    {
        return NormalizeProbeMode(_gatewayOptions.GatewayStartupProbeMode);
    }

    private static string NormalizeProbeMode(string? mode)
    {
        if (string.Equals(mode, ProbeModeMock, StringComparison.OrdinalIgnoreCase))
        {
            return ProbeModeMock;
        }

        return ProbeModeLive;
    }

    private readonly record struct InitialProbeEndpointsResult(
        bool Success,
        int? HealthCode,
        int? ReadyBeforeCode,
        string? Reason,
        int Attempts
    );

    private readonly record struct ReadyAfterProbeResult(
        bool Success,
        int? ReadyAfterCode,
        string? Reason,
        bool Skip,
        int Attempts
    );

    private readonly record struct GatewayRoundTripMetricResult(
        bool Success,
        long? RoundTripCount,
        string? Reason,
        bool Skip,
        int Attempts
    );

    private readonly record struct WebSocketPingPongAttemptResult(
        bool Success,
        bool Retryable
    );

    private async Task<InitialProbeEndpointsResult> WaitForInitialProbeEndpointsAsync(
        HttpClient httpClient,
        Uri healthUri,
        Uri readyUri,
        CancellationToken cancellationToken
    )
    {
        var attempts = 0;
        var pollIntervalMs = Math.Max(50, _gatewayOptions.GatewayStartupProbePollIntervalMs);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                var healthCode = await GetStatusCodeAsync(httpClient, healthUri, cancellationToken);
                var readyCode = await GetStatusCodeAsync(httpClient, readyUri, cancellationToken);
                return new InitialProbeEndpointsResult(
                    Success: true,
                    HealthCode: healthCode,
                    ReadyBeforeCode: readyCode,
                    Reason: null,
                    Attempts: attempts
                );
            }
            catch (Exception ex)
            {
                var networkInitReason = GetNetworkInitializationFailureReason(ex);
                if (networkInitReason != null)
                {
                    return new InitialProbeEndpointsResult(
                        Success: false,
                        HealthCode: null,
                        ReadyBeforeCode: null,
                        Reason: networkInitReason,
                        Attempts: attempts
                    );
                }

                if (ex is HttpRequestException or TaskCanceledException)
                {
                    var listenerBindFailureReason = TryResolveListenerBindFailureReasonFromHealthSnapshot();
                    if (listenerBindFailureReason != null)
                    {
                        return new InitialProbeEndpointsResult(
                            Success: false,
                            HealthCode: null,
                            ReadyBeforeCode: null,
                            Reason: listenerBindFailureReason,
                            Attempts: attempts
                        );
                    }

                    if (attempts == 1 || attempts % 10 == 0)
                    {
                        Console.WriteLine(
                            $"[web] startup probe waiting for endpoint readiness "
                            + $"(attempt={attempts}, pollIntervalMs={pollIntervalMs}): {ex.Message}"
                        );
                    }

                    await Task.Delay(pollIntervalMs, cancellationToken);
                    continue;
                }

                throw;
            }
        }
    }

    private async Task<ReadyAfterProbeResult> WaitForReadyAfterProbeAsync(
        HttpClient httpClient,
        Uri readyUri,
        bool wsPingPongOk,
        CancellationToken cancellationToken
    )
    {
        var attempts = 0;
        var pollIntervalMs = Math.Max(50, _gatewayOptions.GatewayStartupProbePollIntervalMs);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                var readyAfterCode = await GetStatusCodeAsync(httpClient, readyUri, cancellationToken);
                if (!wsPingPongOk || readyAfterCode != 503)
                {
                    return new ReadyAfterProbeResult(
                        Success: true,
                        ReadyAfterCode: readyAfterCode,
                        Reason: null,
                        Skip: false,
                        Attempts: attempts
                    );
                }

                if (attempts == 1 || attempts % 10 == 0)
                {
                    Console.WriteLine(
                        $"[web] startup probe waiting for readyz transition "
                        + $"(attempt={attempts}, pollIntervalMs={pollIntervalMs}, status={readyAfterCode})"
                    );
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
            }
            catch (Exception ex)
            {
                var networkInitReason = GetNetworkInitializationFailureReason(ex);
                if (networkInitReason != null)
                {
                    return new ReadyAfterProbeResult(
                        Success: false,
                        ReadyAfterCode: null,
                        Reason: networkInitReason,
                        Skip: true,
                        Attempts: attempts
                    );
                }

                if (ex is HttpRequestException or TaskCanceledException)
                {
                    var listenerBindFailureReason = TryResolveListenerBindFailureReasonFromHealthSnapshot();
                    if (listenerBindFailureReason != null)
                    {
                        return new ReadyAfterProbeResult(
                            Success: false,
                            ReadyAfterCode: null,
                            Reason: listenerBindFailureReason,
                            Skip: true,
                            Attempts: attempts
                        );
                    }

                    if (!wsPingPongOk)
                    {
                        return new ReadyAfterProbeResult(
                            Success: false,
                            ReadyAfterCode: null,
                            Reason: $"readyz recheck unavailable ({ex.Message})",
                            Skip: false,
                            Attempts: attempts
                        );
                    }

                    if (attempts == 1 || attempts % 10 == 0)
                    {
                        Console.WriteLine(
                            $"[web] startup probe waiting for readyz recheck "
                            + $"(attempt={attempts}, pollIntervalMs={pollIntervalMs}): {ex.Message}"
                        );
                    }

                    await Task.Delay(pollIntervalMs, cancellationToken);
                    continue;
                }

                throw;
            }
        }
    }

    private string? TryResolveListenerBindFailureReasonFromHealthSnapshot()
    {
        try
        {
            if (
                string.IsNullOrWhiteSpace(_paths.GatewayHealthStatePath)
                || !File.Exists(_paths.GatewayHealthStatePath)
            )
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_paths.GatewayHealthStatePath));
            var root = document.RootElement;
            if (
                root.TryGetProperty("listenerBound", out var listenerBoundElement)
                && listenerBoundElement.ValueKind == JsonValueKind.True
            )
            {
                return null;
            }

            int? listenerErrorCode = null;
            if (
                root.TryGetProperty("listenerErrorCode", out var listenerErrorCodeElement)
                && listenerErrorCodeElement.ValueKind == JsonValueKind.Number
                && listenerErrorCodeElement.TryGetInt32(out var parsedErrorCode)
            )
            {
                listenerErrorCode = parsedErrorCode;
            }

            string? listenerErrorMessage = null;
            if (
                root.TryGetProperty("listenerErrorMessage", out var listenerErrorMessageElement)
                && listenerErrorMessageElement.ValueKind == JsonValueKind.String
            )
            {
                listenerErrorMessage = listenerErrorMessageElement.GetString();
            }

            if (!listenerErrorCode.HasValue && string.IsNullOrWhiteSpace(listenerErrorMessage))
            {
                return null;
            }

            var message = string.IsNullOrWhiteSpace(listenerErrorMessage)
                ? "listener start failed"
                : listenerErrorMessage.Trim();
            var code = listenerErrorCode.HasValue
                ? listenerErrorCode.Value.ToString(CultureInfo.InvariantCulture)
                : "unknown";
            return $"probe endpoint unavailable (listener start failed: code={code}, message={message})";
        }
        catch
        {
            return null;
        }
    }

    private async Task<GatewayRoundTripMetricResult> WaitForGatewayRoundTripMetricAsync(
        bool wsPingPongOk,
        CancellationToken cancellationToken
    )
    {
        if (!wsPingPongOk)
        {
            return new GatewayRoundTripMetricResult(
                Success: true,
                RoundTripCount: null,
                Reason: null,
                Skip: false,
                Attempts: 0
            );
        }

        var attempts = 0;
        var pollIntervalMs = Math.Max(50, _gatewayOptions.GatewayStartupProbePollIntervalMs);
        var maxAttempts = Math.Max(
            1,
            (_gatewayOptions.GatewayStartupProbeTimeoutSec * 1000) / pollIntervalMs
        );
        while (attempts < maxAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;

            if (TryReadGatewayRoundTripCountFromHealthSnapshot(out var roundTripCount))
            {
                if (roundTripCount > 0)
                {
                    return new GatewayRoundTripMetricResult(
                        Success: true,
                        RoundTripCount: roundTripCount,
                        Reason: null,
                        Skip: false,
                        Attempts: attempts
                    );
                }

                if (attempts == 1 || attempts % 10 == 0)
                {
                    Console.WriteLine(
                        $"[web] startup probe waiting for gateway roundtrip metric "
                        + $"(attempt={attempts}, pollIntervalMs={pollIntervalMs}, count={roundTripCount.ToString(CultureInfo.InvariantCulture)})"
                    );
                }
            }
            else if (attempts == 1 || attempts % 10 == 0)
            {
                Console.WriteLine(
                    $"[web] startup probe waiting for gateway health snapshot "
                    + $"(attempt={attempts}, pollIntervalMs={pollIntervalMs})"
                );
            }

            var listenerBindFailureReason = TryResolveListenerBindFailureReasonFromHealthSnapshot();
            if (listenerBindFailureReason != null)
            {
                return new GatewayRoundTripMetricResult(
                    Success: false,
                    RoundTripCount: null,
                    Reason: listenerBindFailureReason,
                    Skip: true,
                    Attempts: attempts
                );
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
        }

        var finalRoundTripCount = TryReadGatewayRoundTripCountFromHealthSnapshot(
            out var parsedRoundTripCount
        )
            ? parsedRoundTripCount
            : (long?)null;
        return new GatewayRoundTripMetricResult(
            Success: false,
            RoundTripCount: finalRoundTripCount,
            Reason: "gateway health snapshot roundtrip count unavailable",
            Skip: false,
            Attempts: attempts
        );
    }

    private bool TryReadGatewayRoundTripCountFromHealthSnapshot(out long roundTripCount)
    {
        roundTripCount = 0;
        try
        {
            if (
                string.IsNullOrWhiteSpace(_paths.GatewayHealthStatePath)
                || !File.Exists(_paths.GatewayHealthStatePath)
            )
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_paths.GatewayHealthStatePath));
            var root = document.RootElement;
            if (
                root.TryGetProperty("webSocketRoundTripCount", out var roundTripElement)
                && roundTripElement.ValueKind == JsonValueKind.Number
                && roundTripElement.TryGetInt64(out var parsedRoundTripCount)
            )
            {
                roundTripCount = parsedRoundTripCount;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> GetStatusCodeAsync(
        HttpClient httpClient,
        Uri endpoint,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return (int)response.StatusCode;
    }

    private async Task<bool> TryRunWebSocketPingPongAsync(CancellationToken cancellationToken)
    {
        var pollIntervalMs = Math.Max(50, _gatewayOptions.GatewayStartupProbePollIntervalMs);
        var maxAttempts = Math.Max(
            1,
            Math.Min(
                WebSocketPingPongMaxAttemptsCap,
                (_gatewayOptions.GatewayStartupProbeTimeoutSec * 1000) / pollIntervalMs
            )
        );

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wsPingPongAttempt = await TryRunWebSocketPingPongSingleAttemptAsync(cancellationToken);
            if (wsPingPongAttempt.Success)
            {
                if (attempt > 1)
                {
                    Console.WriteLine(
                        "[web] startup probe websocket ping/pong recovered "
                        + $"(attempt={attempt}, pollIntervalMs={pollIntervalMs})"
                    );
                }

                return true;
            }

            if (!wsPingPongAttempt.Retryable)
            {
                return false;
            }

            var listenerBindFailureReason = TryResolveListenerBindFailureReasonFromHealthSnapshot();
            if (!string.IsNullOrWhiteSpace(listenerBindFailureReason))
            {
                Console.WriteLine(
                    $"[web] startup probe websocket step skipped ({listenerBindFailureReason})"
                );
                return false;
            }

            if (attempt == 1 || attempt % 10 == 0)
            {
                Console.WriteLine(
                    "[web] startup probe waiting for websocket ping/pong "
                    + $"(attempt={attempt}, maxAttempts={maxAttempts}, pollIntervalMs={pollIntervalMs})"
                );
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(pollIntervalMs, cancellationToken);
            }
        }

        return false;
    }

    private async Task<WebSocketPingPongAttemptResult> TryRunWebSocketPingPongSingleAttemptAsync(
        CancellationToken cancellationToken
    )
    {
        var wsUri = new Uri($"ws://127.0.0.1:{_port}/ws/");
        using var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(wsUri, cancellationToken);
            await SendTextMessageAsync(socket, "{\"type\":\"ping\"}", cancellationToken);

            for (var i = 0; i < 6; i++)
            {
                var payload = await ReceiveTextMessageAsync(socket, cancellationToken);
                if (payload == null)
                {
                    break;
                }

                if (IsPongMessage(payload))
                {
                    await TryCloseSocketAsync(socket, cancellationToken);
                    return new WebSocketPingPongAttemptResult(
                        Success: true,
                        Retryable: false
                    );
                }
            }
        }
        catch (Exception ex) when (
            ex is WebSocketException or HttpRequestException or TaskCanceledException
            || GetNetworkInitializationFailureReason(ex) != null
        )
        {
            var networkInitReason = GetNetworkInitializationFailureReason(ex);
            if (networkInitReason != null)
            {
                Console.WriteLine(
                    $"[web] startup probe websocket step skipped ({networkInitReason})"
                );
                await TryCloseSocketAsync(socket, cancellationToken);
                return new WebSocketPingPongAttemptResult(
                    Success: false,
                    Retryable: false
                );
            }

            Console.WriteLine($"[web] startup probe websocket step failed ({ex.Message})");
        }

        await TryCloseSocketAsync(socket, cancellationToken);
        return new WebSocketPingPongAttemptResult(
            Success: false,
            Retryable: true
        );
    }

    private static async Task SendTextMessageAsync(
        ClientWebSocket socket,
        string payload,
        CancellationToken cancellationToken
    )
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken
        );
    }

    private static async Task<string?> ReceiveTextMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
                if (stream.Length > MaxReceiveBytes)
                {
                    throw new InvalidOperationException("websocket frame too large");
                }
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool IsPongMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("type", out var typeElement))
            {
                return false;
            }

            var typeValue = typeElement.GetString();
            return string.Equals(typeValue, "pong", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task TryCloseSocketAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "startup_probe_done",
                    cancellationToken
                );
            }
        }
        catch
        {
            // Ignore probe shutdown failures.
        }
    }

    private bool TryCreateProbeHttpClient(
        [NotNullWhen(true)] out HttpClient? httpClient,
        out string error
    )
    {
        try
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None
            };

            httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(3, _gatewayOptions.GatewayStartupProbeTimeoutSec))
            };
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            httpClient = null;
            var networkInitReason = GetNetworkInitializationFailureReason(ex);
            error = networkInitReason ?? $"probe http client init failed ({ex.Message})";
            return false;
        }
    }

    private static string? GetNetworkInitializationFailureReason(Exception ex)
    {
        Exception? current = ex;
        while (current != null)
        {
            if (current is TypeInitializationException typeInitialization
                && !string.IsNullOrWhiteSpace(typeInitialization.TypeName)
                && typeInitialization.TypeName.Contains("System.Net.CookieContainer", StringComparison.Ordinal))
            {
                return "runtime network stack unavailable (System.Net.CookieContainer)";
            }

            var message = current.Message;
            if (!string.IsNullOrWhiteSpace(message)
                && message.Contains("System.Net.CookieContainer", StringComparison.Ordinal))
            {
                return "runtime network stack unavailable (System.Net.CookieContainer)";
            }

            current = current.InnerException;
        }

        return null;
    }

    private void WriteProbeSnapshot(
        string result,
        string reason,
        int? healthCode = null,
        int? readyBeforeCode = null,
        bool? wsPingPongOk = null,
        int? readyAfterCode = null,
        string? transition = null,
        string? failedChecks = null,
        long? gatewayRoundTripCount = null
    )
    {
        try
        {
            var probeMode = ResolveProbeMode();
            var simulated = string.Equals(probeMode, ProbeModeMock, StringComparison.Ordinal);
            var reasonMetadata = ResolveReasonMetadata(result, reason);
            var validationProfileHint = ResolveValidationProfileHint(
                result,
                probeMode,
                reasonMetadata.ReasonCode
            );
            var healthzOk = healthCode.HasValue ? healthCode.Value == 200 : (bool?)null;
            var readyzBeforeOk = readyBeforeCode.HasValue ? readyBeforeCode.Value == 503 : (bool?)null;
            var wsPingPongStateOk = wsPingPongOk;
            var readyzAfterOk = readyAfterCode.HasValue ? readyAfterCode.Value == 200 : (bool?)null;
            var readyzTransitionOk = readyBeforeCode.HasValue && readyAfterCode.HasValue
                ? readyBeforeCode.Value == 503 && readyAfterCode.Value == 200
                : (bool?)null;
            var gatewayRoundTripOk = gatewayRoundTripCount.HasValue
                ? gatewayRoundTripCount.Value > 0
                : (bool?)null;
            var allChecksPassed = healthzOk.HasValue
                && readyzBeforeOk.HasValue
                && wsPingPongStateOk.HasValue
                && readyzAfterOk.HasValue
                && gatewayRoundTripOk.HasValue
                && readyzTransitionOk.HasValue
                ? healthzOk.Value
                    && readyzBeforeOk.Value
                    && wsPingPongStateOk.Value
                    && readyzAfterOk.Value
                    && gatewayRoundTripOk.Value
                    && readyzTransitionOk.Value
                : (bool?)null;

            var payload = "{"
                + $"\"result\":\"{EscapeJson(result)}\","
                + $"\"reason\":\"{EscapeJson(reason)}\","
                + $"\"probeMode\":\"{EscapeJson(probeMode)}\","
                + $"\"simulated\":{(simulated ? "true" : "false")},"
                + $"\"reasonCode\":\"{EscapeJson(reasonMetadata.ReasonCode)}\","
                + $"\"reasonHint\":{FormatNullableString(reasonMetadata.ReasonHint)},"
                + $"\"environmentBlocked\":{FormatNullableBool(reasonMetadata.EnvironmentBlocked)},"
                + $"\"validationProfileHint\":{FormatNullableString(validationProfileHint)},"
                + $"\"port\":{_port},"
                + "\"healthEndpointPath\":\"/healthz\","
                + "\"readyEndpointPath\":\"/readyz\","
                + $"\"healthz\":{FormatNullableInt(healthCode)},"
                + $"\"healthzOk\":{FormatNullableBool(healthzOk)},"
                + $"\"readyzBefore\":{FormatNullableInt(readyBeforeCode)},"
                + $"\"readyzBeforeOk\":{FormatNullableBool(readyzBeforeOk)},"
                + $"\"wsPingPong\":{FormatWsPingPongState(wsPingPongOk)},"
                + $"\"wsPingPongOk\":{FormatNullableBool(wsPingPongStateOk)},"
                + $"\"readyzAfter\":{FormatNullableInt(readyAfterCode)},"
                + $"\"readyzAfterOk\":{FormatNullableBool(readyzAfterOk)},"
                + $"\"gatewayWebSocketRoundTripCount\":{FormatNullableLong(gatewayRoundTripCount)},"
                + $"\"gatewayWebSocketRoundTripOk\":{FormatNullableBool(gatewayRoundTripOk)},"
                + $"\"readyzTransitionOk\":{FormatNullableBool(readyzTransitionOk)},"
                + $"\"allChecksPassed\":{FormatNullableBool(allChecksPassed)},"
                + $"\"transition\":{FormatNullableString(transition)},"
                + $"\"failedChecks\":{FormatNullableString(failedChecks)},"
                + $"\"updatedAtUtc\":\"{EscapeJson(DateTimeOffset.UtcNow.ToString("O"))}\""
                + "}";
            AtomicFileStore.WriteAllText(_paths.GatewayStartupProbeStatePath, payload, ownerOnly: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[web] startup probe snapshot write failed (path={_paths.GatewayStartupProbeStatePath}): {ex.Message}"
            );
        }
    }

    private static string FormatNullableInt(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";
    }

    private static string FormatNullableLong(long? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";
    }

    private static string FormatNullableString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "null" : $"\"{EscapeJson(value)}\"";
    }

    private static string FormatNullableBool(bool? value)
    {
        if (!value.HasValue)
        {
            return "null";
        }

        return value.Value ? "true" : "false";
    }

    private static string FormatWsPingPongState(bool? value)
    {
        if (!value.HasValue)
        {
            return "null";
        }

        return value.Value ? "\"ok\"" : "\"failed\"";
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static (string ReasonCode, string? ReasonHint, bool? EnvironmentBlocked) ResolveReasonMetadata(
        string result,
        string reason
    )
    {
        if (reason.Contains("mock_probe", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "startup_probe_mocked",
                "development/test mock path only; rerun with OMNINODE_GATEWAY_STARTUP_PROBE_MODE=live in a bindable environment for real E2E verification",
                false
            );
        }

        if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return ("all_checks_passed", null, false);
        }

        if (string.Equals(result, "timeout", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "startup_probe_timeout",
                "increase OMNINODE_GATEWAY_STARTUP_PROBE_TIMEOUT_SEC or rerun where local port binding is allowed",
                null
            );
        }

        if (reason.Contains("health endpoint disabled", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "health_endpoint_disabled",
                "set OMNINODE_ENABLE_HEALTH_ENDPOINT=1 before running startup probe",
                false
            );
        }

        if (reason.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "listener_permission_denied",
                "rerun in an environment that allows binding to 127.0.0.1:<port>",
                true
            );
        }

        if (reason.Contains("runtime network stack unavailable", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("System.Net.CookieContainer", StringComparison.Ordinal))
        {
            return (
                "runtime_network_stack_unavailable",
                "rerun in an environment with unrestricted .NET network stack initialization",
                true
            );
        }

        if (reason.Contains("probe endpoint unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "probe_endpoint_unavailable",
                "verify listener bind success and probe endpoint reachability",
                null
            );
        }

        if (reason.Contains("readyz recheck unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "readyz_recheck_unavailable",
                "verify /readyz recheck after websocket ping/pong succeeds",
                false
            );
        }

        if (reason.Contains("gateway health snapshot roundtrip", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "gateway_roundtrip_count_unavailable",
                "verify gateway_health.json updates and rerun in an environment where websocket probe can complete",
                false
            );
        }

        if (string.Equals(reason, "probe_checks_failed", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "probe_checks_failed",
                "inspect failedChecks for healthz/readyz/ws_ping_pong mismatches",
                false
            );
        }

        if (string.Equals(result, "error", StringComparison.OrdinalIgnoreCase))
        {
            return ("startup_probe_error", "inspect startup probe logs for exception details", null);
        }

        return ("startup_probe_unknown", null, null);
    }

    private static string? ResolveValidationProfileHint(
        string result,
        string probeMode,
        string reasonCode
    )
    {
        if (
            string.Equals(probeMode, ProbeModeMock, StringComparison.OrdinalIgnoreCase)
            && string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "mock-success";
        }

        if (
            string.Equals(probeMode, ProbeModeLive, StringComparison.OrdinalIgnoreCase)
            && string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "live-success";
        }

        if (
            string.Equals(probeMode, ProbeModeLive, StringComparison.OrdinalIgnoreCase)
            && (
                string.Equals(reasonCode, "listener_permission_denied", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    reasonCode,
                    "runtime_network_stack_unavailable",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            return "live-environment-blocked";
        }

        return null;
    }

    private static (bool Passed, string Transition, string FailedChecks) EvaluateProbeResult(
        int healthCode,
        int readyBeforeCode,
        bool wsPingPongOk,
        int readyAfterCode,
        long? gatewayRoundTripCount
    )
    {
        var transition = readyBeforeCode == 503 && readyAfterCode == 200
            ? "ok"
            : "unexpected";
        var failedChecks = new List<string>();
        if (healthCode != 200)
        {
            failedChecks.Add($"healthz_expected_200_actual_{healthCode}");
        }

        if (readyBeforeCode != 503)
        {
            failedChecks.Add($"readyz_before_expected_503_actual_{readyBeforeCode}");
        }

        if (!wsPingPongOk)
        {
            failedChecks.Add("ws_ping_pong_expected_ok_actual_failed");
        }
        else
        {
            if (!gatewayRoundTripCount.HasValue)
            {
                failedChecks.Add("gateway_roundtrip_expected_gt0_actual_missing");
            }
            else if (gatewayRoundTripCount.Value <= 0)
            {
                failedChecks.Add(
                    $"gateway_roundtrip_expected_gt0_actual_{gatewayRoundTripCount.Value.ToString(CultureInfo.InvariantCulture)}"
                );
            }
        }

        if (readyAfterCode != 200)
        {
            failedChecks.Add($"readyz_after_expected_200_actual_{readyAfterCode}");
        }

        return (
            failedChecks.Count == 0,
            transition,
            failedChecks.Count == 0 ? "none" : string.Join(",", failedChecks)
        );
    }
}
