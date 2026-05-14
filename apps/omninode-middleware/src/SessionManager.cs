using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class SessionManager : IAuthSessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, TrustedSessionRecord> _trustedSessions = new();
    private readonly string _statePath;
    private readonly string _tokenKeyPath;
    private readonly object _stateLock = new();
    private byte[] _primaryTokenKey = Array.Empty<byte>();
    private byte[] _previousTokenKey = Array.Empty<byte>();
    private string _tokenKeyDay = string.Empty;

    public SessionManager(string statePath)
    {
        _statePath = Path.GetFullPath(statePath);
        _tokenKeyPath = _statePath + ".keys";
        LoadOrCreateTokenKeys();
        LoadTrustedSessions();
    }

    public (string SessionId, string Otp) CreatePending(TimeSpan ttl)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var record = new SessionRecord(otp, DateTimeOffset.UtcNow.Add(ttl), authenticated: false);
        _sessions[sessionId] = record;
        return (sessionId, otp);
    }

    public bool Authenticate(
        string sessionId,
        string otp,
        TimeSpan trustedTtl,
        out TrustedAuthTicket ticket
    )
    {
        ticket = new TrustedAuthTicket(string.Empty, DateTimeOffset.MinValue);
        if (!_sessions.TryGetValue(sessionId, out var record))
        {
            return false;
        }

        lock (record)
        {
            if (record.ExpiresAtUtc < DateTimeOffset.UtcNow)
            {
                _sessions.TryRemove(sessionId, out _);
                return false;
            }

            if (!string.Equals(record.Otp, otp, StringComparison.Ordinal))
            {
                return false;
            }

            var trustedExpiresAtUtc = DateTimeOffset.UtcNow.Add(trustedTtl);
            var trustedToken = CreateSignedToken(sessionId, trustedExpiresAtUtc);
            record.Authenticated = true;
            record.ExpiresAtUtc = trustedExpiresAtUtc;
            SaveTrustedSessionLocked(trustedToken, trustedExpiresAtUtc);
            ticket = new TrustedAuthTicket(trustedToken, trustedExpiresAtUtc);
            return true;
        }
    }

    public bool TryResumeTrusted(string authToken, out DateTimeOffset expiresAtUtc)
    {
        expiresAtUtc = DateTimeOffset.MinValue;
        var token = (authToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (TryValidateSignedToken(token, out expiresAtUtc))
        {
            return true;
        }

        if (!_trustedSessions.TryGetValue(token, out var trusted))
        {
            return false;
        }

        if (trusted.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            _trustedSessions.TryRemove(token, out _);
            SaveTrustedSessions();
            return false;
        }

        expiresAtUtc = trusted.ExpiresAtUtc;
        return true;
    }

    public bool MarkAuthenticatedFromTrusted(string sessionId, DateTimeOffset expiresAtUtc)
    {
        if (!_sessions.TryGetValue(sessionId, out var record))
        {
            return false;
        }

        lock (record)
        {
            if (record.ExpiresAtUtc < DateTimeOffset.UtcNow)
            {
                _sessions.TryRemove(sessionId, out _);
                return false;
            }

            record.Authenticated = true;
            record.ExpiresAtUtc = expiresAtUtc > DateTimeOffset.UtcNow
                ? expiresAtUtc
                : DateTimeOffset.UtcNow.AddHours(12);
            return true;
        }
    }

    public bool TryGetOtp(string sessionId, out string otp)
    {
        otp = string.Empty;
        if (!_sessions.TryGetValue(sessionId, out var record))
        {
            return false;
        }

        if (record.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(sessionId, out _);
            return false;
        }

        otp = record.Otp;
        return true;
    }

    public bool IsAuthenticated(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var record))
        {
            return false;
        }

        if (record.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(sessionId, out _);
            return false;
        }

        return record.Authenticated;
    }

    public void Remove(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    private string CreateSignedToken(string sessionId, DateTimeOffset expiresAtUtc)
    {
        RotateTokenKeysIfNeeded();
        var nonce = CreateSecureToken();
        var payload = $"{sessionId}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}|{expiresAtUtc.ToUnixTimeSeconds()}|{nonce}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] signature;
        lock (_stateLock)
        {
            using var hmac = new HMACSHA256(_primaryTokenKey);
            signature = hmac.ComputeHash(payloadBytes);
        }

        return $"{ToBase64Url(payloadBytes)}.{ToBase64Url(signature)}";
    }

    private bool TryValidateSignedToken(string token, out DateTimeOffset expiresAtUtc)
    {
        expiresAtUtc = DateTimeOffset.MinValue;
        var parts = token.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = FromBase64Url(parts[0]);
            signatureBytes = FromBase64Url(parts[1]);
        }
        catch
        {
            return false;
        }

        RotateTokenKeysIfNeeded();
        var validSignature = false;
        lock (_stateLock)
        {
            validSignature = VerifySignature(payloadBytes, signatureBytes, _primaryTokenKey)
                             || (_previousTokenKey.Length > 0 && VerifySignature(payloadBytes, signatureBytes, _previousTokenKey));
        }

        if (!validSignature)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var payloadParts = payload.Split('|', StringSplitOptions.TrimEntries);
        if (payloadParts.Length < 4)
        {
            return false;
        }

        if (!long.TryParse(payloadParts[2], out var expUnix))
        {
            return false;
        }

        expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        if (expiresAtUtc < DateTimeOffset.UtcNow)
        {
            return false;
        }

        return true;
    }

    private static bool VerifySignature(byte[] payloadBytes, byte[] signatureBytes, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        var expected = hmac.ComputeHash(payloadBytes);
        return CryptographicOperations.FixedTimeEquals(expected, signatureBytes);
    }

    private void RotateTokenKeysIfNeeded()
    {
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        lock (_stateLock)
        {
            if (_tokenKeyDay == today)
            {
                return;
            }

            _previousTokenKey = _primaryTokenKey;
            _primaryTokenKey = GenerateSecureKey();
            _tokenKeyDay = today;
            SaveTokenKeysLocked();
        }
    }

    private void LoadOrCreateTokenKeys()
    {
        lock (_stateLock)
        {
            try
            {
                if (File.Exists(_tokenKeyPath))
                {
                    var lines = File.ReadAllLines(_tokenKeyPath, Encoding.UTF8);
                    if (lines.Length >= 3)
                    {
                        _tokenKeyDay = lines[0].Trim();
                        _primaryTokenKey = Convert.FromBase64String(lines[1].Trim());
                        _previousTokenKey = string.IsNullOrWhiteSpace(lines[2]) ? Array.Empty<byte>() : Convert.FromBase64String(lines[2].Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[session] key load failed: {ex.Message}");
            }

            if (_primaryTokenKey.Length == 0)
            {
                _tokenKeyDay = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                _primaryTokenKey = GenerateSecureKey();
                _previousTokenKey = Array.Empty<byte>();
                SaveTokenKeysLocked();
            }
        }
    }

    private void SaveTokenKeysLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_tokenKeyPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var content = string.Join(
                "\n",
                _tokenKeyDay,
                Convert.ToBase64String(_primaryTokenKey),
                _previousTokenKey.Length == 0 ? string.Empty : Convert.ToBase64String(_previousTokenKey)
            );
            AtomicFileStore.WriteAllText(_tokenKeyPath, content, ownerOnly: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[session] key save failed: {ex.Message}");
        }
    }

    private static byte[] GenerateSecureKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    private static string CreateSecureToken()
    {
        Span<byte> raw = stackalloc byte[24];
        RandomNumberGenerator.Fill(raw);
        var encoded = Convert.ToBase64String(raw);
        return encoded
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string ToBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value
            .Replace("-", "+", StringComparison.Ordinal)
            .Replace("_", "/", StringComparison.Ordinal);
        var remainder = padded.Length % 4;
        if (remainder > 0)
        {
            padded = padded.PadRight(padded.Length + (4 - remainder), '=');
        }

        return Convert.FromBase64String(padded);
    }

    private void SaveTrustedSessionLocked(string token, DateTimeOffset expiresAtUtc)
    {
        PruneExpiredTrustedSessions();
        _trustedSessions[token] = new TrustedSessionRecord
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc
        };
        SaveTrustedSessions();
    }

    private void PruneExpiredTrustedSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _trustedSessions)
        {
            if (pair.Value.ExpiresAtUtc < now)
            {
                _trustedSessions.TryRemove(pair.Key, out _);
            }
        }
    }

    private void LoadTrustedSessions()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            var json = File.ReadAllText(_statePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var parsed = JsonSerializer.Deserialize(json, OmniJsonContext.Default.AuthSessionState);
            if (parsed == null)
            {
                return;
            }

            foreach (var item in parsed.TrustedSessions)
            {
                if (string.IsNullOrWhiteSpace(item.Token))
                {
                    continue;
                }

                if (item.ExpiresAtUtc < DateTimeOffset.UtcNow)
                {
                    continue;
                }

                _trustedSessions[item.Token] = new TrustedSessionRecord
                {
                    Token = item.Token,
                    ExpiresAtUtc = item.ExpiresAtUtc
                };
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[session] load failed: {ex.Message}");
        }
    }

    private void SaveTrustedSessions()
    {
        lock (_stateLock)
        {
            try
            {
                PruneExpiredTrustedSessions();
                var dir = Path.GetDirectoryName(_statePath);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    return;
                }

                Directory.CreateDirectory(dir);
                var state = new AuthSessionState
                {
                    TrustedSessions = _trustedSessions.Values
                        .OrderByDescending(x => x.ExpiresAtUtc)
                        .Select(x => new TrustedSessionRecord
                        {
                            Token = x.Token,
                            ExpiresAtUtc = x.ExpiresAtUtc
                        })
                        .ToList()
                };
                var json = JsonSerializer.Serialize(state, OmniJsonContext.Default.AuthSessionState);
                AtomicFileStore.WriteAllText(_statePath, json, ownerOnly: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[session] save failed: {ex.Message}");
            }
        }
    }

    private sealed class SessionRecord
    {
        public SessionRecord(string otp, DateTimeOffset expiresAtUtc, bool authenticated)
        {
            Otp = otp;
            ExpiresAtUtc = expiresAtUtc;
            Authenticated = authenticated;
        }

        public string Otp { get; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public bool Authenticated { get; set; }
    }
}

public sealed record TrustedAuthTicket(string Token, DateTimeOffset ExpiresAtUtc);

public sealed class AuthSessionState
{
    public List<TrustedSessionRecord> TrustedSessions { get; set; } = new();
}

public sealed class TrustedSessionRecord
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
