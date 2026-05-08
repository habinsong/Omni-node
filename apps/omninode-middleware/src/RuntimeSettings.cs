using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class RuntimeSettings
{
    private const string KeychainAccount = "omninode";
    private const string TelegramBotTokenService = "omninode_telegram_bot_token";
    private const string TelegramChatIdService = "omninode_telegram_chat_id";
    private const string GroqApiKeyService = "omninode_groq_api_key";
    private const string GeminiApiKeyService = "omninode_gemini_api_key";
    private const string CerebrasApiKeyService = "omninode_cerebras_api_key";
    private const string NvidiaApiKeyService = "omninode_nvidia_api_key";
    private const string CodexApiKeyService = "omninode_codex_api_key";
    private const string SttApiKeyService = "omninode_stt_api_key";

    private readonly object _lock = new();
    private string? _telegramBotToken;
    private string? _telegramChatId;
    private string? _groqApiKey;
    private string? _geminiApiKey;
    private string? _cerebrasApiKey;
    private string? _nvidiaApiKey;
    private string? _codexApiKey;
    private string? _sttApiKey;
    private bool _externalDashboardEnabled;
    private readonly string _cerebrasKeychainService;
    private readonly string _cerebrasKeychainAccount;
    private readonly string _nvidiaKeychainService;
    private readonly string _nvidiaKeychainAccount;
    private readonly string _dashboardAccessStatePath;

    public RuntimeSettings(AppConfig config)
    {
        _telegramBotToken = config.TelegramBotToken;
        _telegramChatId = config.TelegramChatId;
        _groqApiKey = config.GroqApiKey;
        _geminiApiKey = config.GeminiApiKey;
        _cerebrasApiKey = config.CerebrasApiKey;
        _nvidiaApiKey = config.NvidiaApiKey;
        _codexApiKey = config.CodexApiKey;
        _sttApiKey = config.SttApiKey;
        _dashboardAccessStatePath = string.IsNullOrWhiteSpace(config.DashboardAccessStatePath)
            ? DefaultStatePathResolver.CreateDefault().ResolveStateFilePath("dashboard_access.json")
            : config.DashboardAccessStatePath.Trim();
        _externalDashboardEnabled = LoadExternalDashboardEnabled(_dashboardAccessStatePath, config.ExternalDashboardEnabled);
        _cerebrasKeychainService = string.IsNullOrWhiteSpace(config.CerebrasKeychainService)
            ? CerebrasApiKeyService
            : config.CerebrasKeychainService.Trim();
        _cerebrasKeychainAccount = string.IsNullOrWhiteSpace(config.CerebrasKeychainAccount)
            ? KeychainAccount
            : config.CerebrasKeychainAccount.Trim();
        _nvidiaKeychainService = string.IsNullOrWhiteSpace(config.NvidiaKeychainService)
            ? NvidiaApiKeyService
            : config.NvidiaKeychainService.Trim();
        _nvidiaKeychainAccount = string.IsNullOrWhiteSpace(config.NvidiaKeychainAccount)
            ? KeychainAccount
            : config.NvidiaKeychainAccount.Trim();
    }

    public string? GetTelegramBotToken()
    {
        lock (_lock)
        {
            return _telegramBotToken;
        }
    }

    public string? GetTelegramChatId()
    {
        lock (_lock)
        {
            return _telegramChatId;
        }
    }

    public string? GetGroqApiKey()
    {
        lock (_lock)
        {
            return _groqApiKey;
        }
    }

    public string? GetGeminiApiKey()
    {
        lock (_lock)
        {
            return _geminiApiKey;
        }
    }

    public string? GetCerebrasApiKey()
    {
        lock (_lock)
        {
            return _cerebrasApiKey;
        }
    }

    public string? GetNvidiaApiKey()
    {
        lock (_lock)
        {
            return _nvidiaApiKey;
        }
    }

    public string? GetCodexApiKey()
    {
        lock (_lock)
        {
            return _codexApiKey;
        }
    }

    public string? GetSttApiKey()
    {
        lock (_lock)
        {
            return _sttApiKey;
        }
    }

    public bool HasTelegramCredentials()
    {
        lock (_lock)
        {
            return !string.IsNullOrWhiteSpace(_telegramBotToken) && !string.IsNullOrWhiteSpace(_telegramChatId);
        }
    }

    public SettingsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new SettingsSnapshot(
                Mask(_telegramBotToken),
                Mask(_telegramChatId),
                Mask(_groqApiKey),
                Mask(_geminiApiKey),
                Mask(_cerebrasApiKey),
                Mask(_nvidiaApiKey),
                Mask(_codexApiKey),
                HasValue(_telegramBotToken),
                HasValue(_telegramChatId),
                HasValue(_groqApiKey),
                HasValue(_geminiApiKey),
                HasValue(_cerebrasApiKey),
                HasValue(_nvidiaApiKey),
                HasValue(_codexApiKey),
                _externalDashboardEnabled
            );
        }
    }

    public bool IsExternalDashboardEnabled()
    {
        lock (_lock)
        {
            return _externalDashboardEnabled;
        }
    }

    public string SetExternalDashboardEnabled(bool enabled)
    {
        lock (_lock)
        {
            _externalDashboardEnabled = enabled;
        }

        SaveExternalDashboardEnabled(_dashboardAccessStatePath, enabled);
        return enabled ? "external dashboard access enabled" : "external dashboard access disabled";
    }

    public string UpdateTelegram(string? botToken, string? chatId, bool persist)
    {
        var updatedFields = new List<string>();
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(botToken))
            {
                _telegramBotToken = botToken.Trim();
                updatedFields.Add("telegram_bot_token");
            }

            if (!string.IsNullOrWhiteSpace(chatId))
            {
                _telegramChatId = chatId.Trim();
                updatedFields.Add("telegram_chat_id");
            }
        }

        if (!persist)
        {
            return updatedFields.Count == 0
                ? "no changes applied"
                : $"runtime updated: {string.Join(", ", updatedFields)}";
        }

        var failed = new List<string>();
        if (persist)
        {
            if (!Persist(TelegramBotTokenService, KeychainAccount, botToken))
            {
                failed.Add("telegram_bot_token");
            }

            if (!Persist(TelegramChatIdService, KeychainAccount, chatId))
            {
                failed.Add("telegram_chat_id");
            }
        }

        if (failed.Count > 0)
        {
            return $"runtime updated, secure persist failed: {string.Join(", ", failed)}";
        }

        if (updatedFields.Count == 0)
        {
            return "no changes applied";
        }

        return $"runtime + secure storage updated: {string.Join(", ", updatedFields)}";
    }

    public string UpdateLlmKeys(
        string? groqApiKey,
        string? geminiApiKey,
        string? cerebrasApiKey,
        string? nvidiaApiKey,
        string? codexApiKey,
        bool persist
    )
    {
        var updatedFields = new List<string>();
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(groqApiKey))
            {
                _groqApiKey = groqApiKey.Trim();
                updatedFields.Add("groq_api_key");
            }

            if (!string.IsNullOrWhiteSpace(geminiApiKey))
            {
                _geminiApiKey = geminiApiKey.Trim();
                updatedFields.Add("gemini_api_key");
            }

            if (!string.IsNullOrWhiteSpace(cerebrasApiKey))
            {
                _cerebrasApiKey = cerebrasApiKey.Trim();
                updatedFields.Add("cerebras_api_key");
            }

            if (!string.IsNullOrWhiteSpace(nvidiaApiKey))
            {
                _nvidiaApiKey = nvidiaApiKey.Trim();
                updatedFields.Add("nvidia_api_key");
            }

            if (!string.IsNullOrWhiteSpace(codexApiKey))
            {
                _codexApiKey = codexApiKey.Trim();
                updatedFields.Add("codex_api_key");
            }
        }

        if (!persist)
        {
            return updatedFields.Count == 0
                ? "no changes applied"
                : $"runtime updated: {string.Join(", ", updatedFields)}";
        }

        var failed = new List<string>();
        if (persist)
        {
            if (!Persist(GroqApiKeyService, KeychainAccount, groqApiKey))
            {
                failed.Add("groq_api_key");
            }

            if (!Persist(GeminiApiKeyService, KeychainAccount, geminiApiKey))
            {
                failed.Add("gemini_api_key");
            }

            if (!Persist(_cerebrasKeychainService, _cerebrasKeychainAccount, cerebrasApiKey))
            {
                failed.Add("cerebras_api_key");
            }

            if (!Persist(_nvidiaKeychainService, _nvidiaKeychainAccount, nvidiaApiKey))
            {
                failed.Add("nvidia_api_key");
            }

            if (!Persist(CodexApiKeyService, KeychainAccount, codexApiKey))
            {
                failed.Add("codex_api_key");
            }
        }

        if (failed.Count > 0)
        {
            return $"runtime updated, secure persist failed: {string.Join(", ", failed)}";
        }

        if (updatedFields.Count == 0)
        {
            return "no changes applied";
        }

        return $"runtime + secure storage updated: {string.Join(", ", updatedFields)}";
    }

    public string DeleteTelegramCredentials(bool deletePersisted)
    {
        lock (_lock)
        {
            _telegramBotToken = null;
            _telegramChatId = null;
        }

        if (!deletePersisted)
        {
            return "telegram runtime credentials cleared";
        }

        var failed = new List<string>();
        if (!SecretLoader.TryDeletePlatformSecret(TelegramBotTokenService, KeychainAccount))
        {
            failed.Add("telegram_bot_token");
        }

        if (!SecretLoader.TryDeletePlatformSecret(TelegramChatIdService, KeychainAccount))
        {
            failed.Add("telegram_chat_id");
        }

        if (failed.Count > 0)
        {
            return $"telegram runtime cleared, secure delete failed: {string.Join(", ", failed)}";
        }

        return "telegram runtime + secure storage deleted";
    }

    public string DeleteLlmCredentials(bool deletePersisted)
    {
        lock (_lock)
        {
            _groqApiKey = null;
            _geminiApiKey = null;
            _cerebrasApiKey = null;
            _nvidiaApiKey = null;
            _codexApiKey = null;
        }

        if (!deletePersisted)
        {
            return "llm runtime credentials cleared";
        }

        var failed = new List<string>();
        if (!SecretLoader.TryDeletePlatformSecret(GroqApiKeyService, KeychainAccount))
        {
            failed.Add("groq_api_key");
        }

        if (!SecretLoader.TryDeletePlatformSecret(GeminiApiKeyService, KeychainAccount))
        {
            failed.Add("gemini_api_key");
        }

        if (!SecretLoader.TryDeletePlatformSecret(_cerebrasKeychainService, _cerebrasKeychainAccount))
        {
            failed.Add("cerebras_api_key");
        }

        if (!SecretLoader.TryDeletePlatformSecret(_nvidiaKeychainService, _nvidiaKeychainAccount))
        {
            failed.Add("nvidia_api_key");
        }

        if (!SecretLoader.TryDeletePlatformSecret(CodexApiKeyService, KeychainAccount))
        {
            failed.Add("codex_api_key");
        }

        if (failed.Count > 0)
        {
            return $"llm runtime cleared, secure delete failed: {string.Join(", ", failed)}";
        }

        return "llm runtime + secure storage deleted";
    }

    private static bool HasValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= 8)
        {
            return "***";
        }

        return $"{trimmed[..4]}...{trimmed[^4..]}";
    }

    private static bool Persist(string service, string account, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var ok = SecretLoader.TryWritePlatformSecret(service, account, value.Trim());
        if (!ok)
        {
            Console.Error.WriteLine($"[settings] secure persist failed for {service}");
            return false;
        }

        return true;
    }

    private static bool LoadExternalDashboardEnabled(string path, bool fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("externalDashboardEnabled", out var enabled)
                && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? enabled.GetBoolean()
                : fallback;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[settings] dashboard access state read failed: {ex.Message}");
            return fallback;
        }
    }

    private static void SaveExternalDashboardEnabled(string path, bool enabled)
    {
        var json = "{\n"
                   + $"  \"externalDashboardEnabled\": {(enabled ? "true" : "false")},\n"
                   + $"  \"updatedAtUtc\": \"{DateTimeOffset.UtcNow:O}\"\n"
                   + "}\n";
        AtomicFileStore.WriteAllText(path, json, ownerOnly: true);
    }
}

public sealed record SettingsSnapshot(
    string TelegramBotTokenMasked,
    string TelegramChatIdMasked,
    string GroqApiKeyMasked,
    string GeminiApiKeyMasked,
    string CerebrasApiKeyMasked,
    string NvidiaApiKeyMasked,
    string CodexApiKeyMasked,
    bool TelegramBotTokenSet,
    bool TelegramChatIdSet,
    bool GroqApiKeySet,
    bool GeminiApiKeySet,
    bool CerebrasApiKeySet,
    bool NvidiaApiKeySet,
    bool CodexApiKeySet,
    bool ExternalDashboardEnabled
);
