using System.Net.WebSockets;

namespace OmniNode.Middleware;

internal readonly record struct AuthSessionDispatchResult(bool Handled, bool Authenticated);

internal sealed class AuthSessionGateway
{
    private readonly IAuthSessionStore _sessionManager;
    private readonly TelegramClient _telegramClient;
    private readonly bool _enableLocalOtpFallback;

    public AuthSessionGateway(
        IAuthSessionStore sessionManager,
        TelegramClient telegramClient,
        bool enableLocalOtpFallback
    )
    {
        _sessionManager = sessionManager;
        _telegramClient = telegramClient;
        _enableLocalOtpFallback = enableLocalOtpFallback;
    }

    public async Task<string> CreatePendingSessionAsync(
        WebSocket socket,
        SemaphoreSlim sendLock,
        bool remoteDashboardClient,
        CancellationToken cancellationToken
    )
    {
        var session = _sessionManager.CreatePending(TimeSpan.FromMinutes(3));
        var sessionId = session.SessionId;

        if (remoteDashboardClient)
        {
            var ttl = TimeSpan.FromHours(12);
            var expiresAtUtc = DateTimeOffset.UtcNow.Add(ttl);
            var ok = _sessionManager.MarkAuthenticatedFromTrusted(sessionId, expiresAtUtc);
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{"
                + "\"type\":\"auth_result\","
                + $"\"ok\":{(ok ? "true" : "false")},"
                + "\"resumed\":false,"
                + "\"authToken\":\"\","
                + $"\"expiresAtUtc\":\"{WebSocketGateway.EscapeJson(ok ? expiresAtUtc.ToString("O") : string.Empty)}\","
                + $"\"expiresAtLocal\":\"{WebSocketGateway.EscapeJson(ok ? WebSocketGateway.FormatLocalDateTime(expiresAtUtc) : string.Empty)}\","
                + $"\"localUtcOffset\":\"{WebSocketGateway.EscapeJson(ok ? WebSocketGateway.FormatUtcOffset(expiresAtUtc.ToLocalTime().Offset) : string.Empty)}\","
                + "\"ttlHours\":12,"
                + "\"remoteDashboardClient\":true,"
                + "\"remoteLimited\":true"
                + "}",
                cancellationToken
            );
            return sessionId;
        }

        await WebSocketGateway.SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"auth_required\","
            + $"\"sessionId\":\"{WebSocketGateway.EscapeJson(sessionId)}\","
            + $"\"telegramConfigured\":{(_telegramClient.IsConfigured ? "true" : "false")},"
            + $"\"remoteDashboardClient\":{(remoteDashboardClient ? "true" : "false")}"
            + "}",
            cancellationToken
        );
        return sessionId;
    }

    public async Task<AuthSessionDispatchResult> TryHandleAsync(
        string? messageType,
        string? sessionId,
        string? otp,
        string? authToken,
        int? authTtlHours,
        bool remoteDashboardClient,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (string.Equals(messageType, "request_otp", StringComparison.Ordinal))
        {
            if (remoteDashboardClient)
            {
                await WebSocketGateway.SendTextAsync(
                    socket,
                    sendLock,
                    "{\"type\":\"error\",\"message\":\"forbidden_remote_auth\"}",
                    cancellationToken
                );
                return new AuthSessionDispatchResult(true, false);
            }

            await HandleRequestOtpAsync(sessionId, socket, sendLock, cancellationToken);
            return new AuthSessionDispatchResult(true, false);
        }

        if (string.Equals(messageType, "auth", StringComparison.Ordinal))
        {
            var ok = await HandleAuthenticateAsync(
                sessionId,
                otp,
                authTtlHours,
                remoteDashboardClient,
                socket,
                sendLock,
                cancellationToken
            );
            return new AuthSessionDispatchResult(true, ok);
        }

        if (string.Equals(messageType, "resume_auth", StringComparison.Ordinal))
        {
            var resumed = await HandleResumeAsync(
                sessionId,
                authToken,
                remoteDashboardClient,
                socket,
                sendLock,
                cancellationToken
            );
            return new AuthSessionDispatchResult(true, resumed);
        }

        return default;
    }

    private async Task HandleRequestOtpAsync(
        string? sessionId,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessionManager.TryGetOtp(sessionId, out var currentOtp))
        {
            await WebSocketGateway.SendTextAsync(
                socket,
                sendLock,
                "{\"type\":\"otp_request_result\",\"ok\":false,\"message\":\"session expired\"}",
                cancellationToken
            );
            return;
        }

        var otpSent = false;
        if (_telegramClient.IsConfigured)
        {
            otpSent = await _telegramClient.SendOtpAsync(currentOtp, cancellationToken);
        }

        if (!otpSent && _enableLocalOtpFallback)
        {
            Console.WriteLine($"[otp] local fallback otp={currentOtp} session={sessionId}");
            otpSent = true;
        }

        var otpResultMessage = otpSent
            ? "OTP를 발송했습니다."
            : "OTP 발송에 실패했습니다. Telegram 설정을 확인하세요.";
        await WebSocketGateway.SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"otp_request_result\","
            + $"\"ok\":{(otpSent ? "true" : "false")},"
            + $"\"message\":\"{WebSocketGateway.EscapeJson(otpResultMessage)}\""
            + "}",
            cancellationToken
        );
    }

    private async Task<bool> HandleAuthenticateAsync(
        string? sessionId,
        string? otp,
        int? authTtlHours,
        bool remoteDashboardClient,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        var ticket = new TrustedAuthTicket(string.Empty, DateTimeOffset.MinValue);
        var trustedTtl = WebSocketGateway.ResolveTrustedAuthTtl(authTtlHours);
        var ok = !string.IsNullOrWhiteSpace(sessionId)
                 && !string.IsNullOrWhiteSpace(otp)
                 && _sessionManager.Authenticate(sessionId, otp, trustedTtl, out ticket);
        await WebSocketGateway.SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"auth_result\","
            + $"\"ok\":{(ok ? "true" : "false")},"
            + "\"resumed\":false,"
            + $"\"authToken\":\"{WebSocketGateway.EscapeJson(ok ? ticket.Token : string.Empty)}\","
            + $"\"expiresAtUtc\":\"{WebSocketGateway.EscapeJson(ok ? ticket.ExpiresAtUtc.ToString("O") : string.Empty)}\","
            + $"\"expiresAtLocal\":\"{WebSocketGateway.EscapeJson(ok ? WebSocketGateway.FormatLocalDateTime(ticket.ExpiresAtUtc) : string.Empty)}\","
            + $"\"localUtcOffset\":\"{WebSocketGateway.EscapeJson(ok ? WebSocketGateway.FormatUtcOffset(ticket.ExpiresAtUtc.ToLocalTime().Offset) : string.Empty)}\","
            + $"\"ttlHours\":{(int)Math.Round(trustedTtl.TotalHours)},"
            + $"\"remoteDashboardClient\":{(remoteDashboardClient ? "true" : "false")}"
            + "}",
            cancellationToken
        );
        return ok;
    }

    private async Task<bool> HandleResumeAsync(
        string? sessionId,
        string? authToken,
        bool remoteDashboardClient,
        WebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken
    )
    {
        var hasToken = !string.IsNullOrWhiteSpace(authToken);
        var expiresAtUtc = DateTimeOffset.MinValue;
        var resumed = !string.IsNullOrWhiteSpace(sessionId)
                      && hasToken
                      && _sessionManager.TryResumeTrusted(authToken!, out expiresAtUtc)
                      && _sessionManager.MarkAuthenticatedFromTrusted(sessionId, expiresAtUtc);

        await WebSocketGateway.SendTextAsync(
            socket,
            sendLock,
            "{"
            + "\"type\":\"auth_result\","
            + $"\"ok\":{(resumed ? "true" : "false")},"
            + "\"resumed\":true,"
            + $"\"authToken\":\"{WebSocketGateway.EscapeJson(resumed ? authToken ?? string.Empty : string.Empty)}\","
            + $"\"expiresAtUtc\":\"{WebSocketGateway.EscapeJson(resumed ? expiresAtUtc.ToString("O") : string.Empty)}\","
            + $"\"expiresAtLocal\":\"{WebSocketGateway.EscapeJson(resumed ? WebSocketGateway.FormatLocalDateTime(expiresAtUtc) : string.Empty)}\","
            + $"\"localUtcOffset\":\"{WebSocketGateway.EscapeJson(resumed ? WebSocketGateway.FormatUtcOffset(expiresAtUtc.ToLocalTime().Offset) : string.Empty)}\","
            + $"\"remoteDashboardClient\":{(remoteDashboardClient ? "true" : "false")}"
            + "}",
            cancellationToken
        );
        return resumed;
    }
}
