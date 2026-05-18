namespace OmniNode.Middleware;

public sealed class FileTelegramReplyOutboxStore
{
    private readonly string _storePath;
    private readonly object _lock = new();

    public FileTelegramReplyOutboxStore(IStatePathResolver pathResolver)
    {
        _storePath = pathResolver.GetTelegramReplyOutboxPath();
    }

    public string Enqueue(string text, string reason, DateTimeOffset nowUtc)
    {
        var normalizedText = (text ?? string.Empty).Trim();
        if (normalizedText.Length == 0)
        {
            return string.Empty;
        }

        lock (_lock)
        {
            var state = LoadUnsafe();
            var entry = new TelegramReplyOutboxEntry
            {
                Id = $"tgout_{nowUtc:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}",
                Text = normalizedText,
                Reason = string.IsNullOrWhiteSpace(reason) ? "send_failed" : reason.Trim(),
                EnqueuedUtc = nowUtc,
                NextAttemptUtc = nowUtc,
                AttemptCount = 0,
                LastError = string.Empty
            };
            state.Entries.Add(entry);
            if (!SaveUnsafe(state))
            {
                return string.Empty;
            }

            return entry.Id;
        }
    }

    public IReadOnlyList<TelegramReplyOutboxEntry> GetReadyEntries(DateTimeOffset nowUtc, int maxCount)
    {
        var safeMaxCount = Math.Max(1, maxCount);
        lock (_lock)
        {
            var state = LoadUnsafe();
            return state.Entries
                .Where(entry => entry.NextAttemptUtc <= nowUtc && !string.IsNullOrWhiteSpace(entry.Text))
                .OrderBy(entry => entry.NextAttemptUtc)
                .ThenBy(entry => entry.EnqueuedUtc)
                .Take(safeMaxCount)
                .Select(CloneEntry)
                .ToArray();
        }
    }

    public void MarkDelivered(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (_lock)
        {
            var state = LoadUnsafe();
            state.Entries.RemoveAll(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
            SaveUnsafe(state);
        }
    }

    public void MarkFailed(string id, string error, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (_lock)
        {
            var state = LoadUnsafe();
            var entry = state.Entries.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (entry == null)
            {
                return;
            }

            entry.AttemptCount = Math.Max(0, entry.AttemptCount) + 1;
            entry.LastError = TrimForStorage(error, 400);
            entry.NextAttemptUtc = nowUtc + ComputeRetryDelay(entry.AttemptCount);
            SaveUnsafe(state);
        }
    }

    private TelegramReplyOutboxState LoadUnsafe()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return new TelegramReplyOutboxState();
            }

            var text = AtomicFileStore.ReadAllTextWithBackup(
                _storePath,
                value => FileTelegramReplyOutboxJson.DeserializeState(value) != null,
                logScope: "telegram-outbox"
            );
            if (string.IsNullOrWhiteSpace(text))
            {
                return new TelegramReplyOutboxState();
            }

            return FileTelegramReplyOutboxJson.DeserializeState(text)
                ?? new TelegramReplyOutboxState();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[telegram-outbox] load failed: {ex.Message}");
            return new TelegramReplyOutboxState();
        }
    }

    private bool SaveUnsafe(TelegramReplyOutboxState state)
    {
        try
        {
            var payload = FileTelegramReplyOutboxJson.SerializeState(state ?? new TelegramReplyOutboxState());
            AtomicFileStore.WriteAllText(_storePath, payload, ownerOnly: true);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[telegram-outbox] save failed: {ex.Message}");
            return false;
        }
    }

    private static TelegramReplyOutboxEntry CloneEntry(TelegramReplyOutboxEntry entry)
    {
        return new TelegramReplyOutboxEntry
        {
            Id = entry.Id,
            Text = entry.Text,
            Reason = entry.Reason,
            EnqueuedUtc = entry.EnqueuedUtc,
            NextAttemptUtc = entry.NextAttemptUtc,
            AttemptCount = entry.AttemptCount,
            LastError = entry.LastError
        };
    }

    private static TimeSpan ComputeRetryDelay(int attemptCount)
    {
        var safeAttempt = Math.Clamp(attemptCount, 1, 8);
        var seconds = Math.Min(300, 5 * (1 << (safeAttempt - 1)));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string TrimForStorage(string text, int maxChars)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars] + "...(truncated)";
    }

}

public sealed class TelegramReplyOutboxEntry
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset EnqueuedUtc { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public int AttemptCount { get; set; }
    public string LastError { get; set; } = string.Empty;
}

internal sealed class TelegramReplyOutboxState
{
    public List<TelegramReplyOutboxEntry> Entries { get; set; } = new();
}
