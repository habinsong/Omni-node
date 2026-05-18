using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class GuardRetryTimelineStore
{
    public const string SchemaVersion = "guard_retry_timeline.v1";
    public const int DefaultBucketMinutes = 5;
    public const int DefaultWindowMinutes = 60;
    public const int DefaultMaxBucketRows = 12;
    public const int DefaultMaxEntries = 512;

    private static readonly string[] DefaultChannels = ["chat", "coding", "telegram"];
    private readonly string _statePath;
    private readonly int _maxEntries;
    private readonly List<GuardRetryTimelineEntry> _entries = new();
    private readonly object _lock = new();

    public GuardRetryTimelineStore(string statePath, int maxEntries = DefaultMaxEntries)
    {
        _statePath = string.IsNullOrWhiteSpace(statePath)
            ? Path.GetFullPath(Path.Combine(Path.GetTempPath(), "omninode_guard_retry_timeline.json"))
            : Path.GetFullPath(statePath);
        _maxEntries = Math.Clamp(maxEntries, 64, 4096);
        LoadFromDisk();
    }

    public void Add(
        string channel,
        bool retryRequired,
        int retryAttempt,
        int retryMaxAttempts,
        string? retryStopReason,
        DateTimeOffset? capturedAtUtc = null
    )
    {
        var normalizedChannel = NormalizeChannel(channel);
        if (normalizedChannel is null)
        {
            return;
        }

        var entry = new GuardRetryTimelineEntry
        {
            Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString("N")[..8]}",
            CapturedAt = (capturedAtUtc ?? DateTimeOffset.UtcNow).ToString("O"),
            Channel = normalizedChannel,
            RetryRequired = retryRequired,
            RetryAttempt = Math.Max(0, retryAttempt),
            RetryMaxAttempts = Math.Max(0, retryMaxAttempts),
            RetryStopReason = NormalizeRetryStopReason(retryStopReason)
        };

        lock (_lock)
        {
            _entries.Insert(0, entry);
            TrimToMaxEntriesLocked();
            SaveToDiskLocked();
        }
    }

    public string BuildSnapshotJson(
        int? bucketMinutes = null,
        int? windowMinutes = null,
        int? maxBucketRows = null,
        IReadOnlyList<string>? channels = null
    )
    {
        lock (_lock)
        {
            var snapshot = BuildSnapshotLocked(
                bucketMinutes,
                windowMinutes,
                maxBucketRows,
                channels
            );
            return JsonSerializer.Serialize(
                snapshot,
                GuardRetryTimelineJsonContext.Default.GuardRetryTimelineSnapshot
            );
        }
    }

    private GuardRetryTimelineSnapshot BuildSnapshotLocked(
        int? bucketMinutes,
        int? windowMinutes,
        int? maxBucketRows,
        IReadOnlyList<string>? channels
    )
    {
        var resolvedBucketMinutes = Math.Clamp(
            bucketMinutes.GetValueOrDefault(DefaultBucketMinutes),
            1,
            60
        );
        var resolvedWindowMinutes = Math.Clamp(
            windowMinutes.GetValueOrDefault(DefaultWindowMinutes),
            resolvedBucketMinutes,
            24 * 60
        );
        var resolvedMaxBucketRows = Math.Clamp(
            maxBucketRows.GetValueOrDefault(DefaultMaxBucketRows),
            1,
            288
        );

        var resolvedChannels = ResolveChannels(channels);
        var bucketSizeMs = resolvedBucketMinutes * 60L * 1000L;
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddMinutes(-resolvedWindowMinutes);

        var byChannel = new Dictionary<string, ChannelAccumulator>(StringComparer.Ordinal);
        foreach (var channel in resolvedChannels)
        {
            byChannel[channel] = new ChannelAccumulator(channel);
        }

        foreach (var entry in _entries)
        {
            if (!byChannel.TryGetValue(entry.Channel, out var channelAccumulator))
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(
                    entry.CapturedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var capturedAt
                ))
            {
                continue;
            }

            if (capturedAt < windowStart)
            {
                continue;
            }

            var retryAttempt = Math.Max(0, entry.RetryAttempt);
            var retryMaxAttempts = Math.Max(0, entry.RetryMaxAttempts);
            var retryStopReason = NormalizeRetryStopReason(entry.RetryStopReason);
            var bucketStartUnixMs = (capturedAt.ToUnixTimeMilliseconds() / bucketSizeMs) * bucketSizeMs;
            var bucketStartUtc = DateTimeOffset.FromUnixTimeMilliseconds(bucketStartUnixMs).ToString("O");

            channelAccumulator.TotalSamples += 1;
            if (entry.RetryRequired)
            {
                channelAccumulator.RetryRequiredSamples += 1;
            }

            channelAccumulator.MaxRetryAttempt = Math.Max(channelAccumulator.MaxRetryAttempt, retryAttempt);
            channelAccumulator.MaxRetryMaxAttempts = Math.Max(channelAccumulator.MaxRetryMaxAttempts, retryMaxAttempts);

            if (channelAccumulator.LastRetryStopReason == "-" && retryStopReason != "-")
            {
                channelAccumulator.LastRetryStopReason = retryStopReason;
            }

            if (!channelAccumulator.Buckets.TryGetValue(bucketStartUnixMs, out var bucket))
            {
                bucket = new BucketAccumulator(bucketStartUtc);
                channelAccumulator.Buckets[bucketStartUnixMs] = bucket;
            }

            bucket.Samples += 1;
            if (entry.RetryRequired)
            {
                bucket.RetryRequiredCount += 1;
            }

            bucket.MaxRetryAttempt = Math.Max(bucket.MaxRetryAttempt, retryAttempt);
            bucket.MaxRetryMaxAttempts = Math.Max(bucket.MaxRetryMaxAttempts, retryMaxAttempts);
            if (retryStopReason != "-")
            {
                bucket.StopReasonCounts[retryStopReason] = bucket.StopReasonCounts.GetValueOrDefault(retryStopReason) + 1;
            }
        }

        var snapshotChannels = new List<GuardRetryTimelineChannelSnapshot>(resolvedChannels.Count);
        foreach (var channel in resolvedChannels)
        {
            var target = byChannel[channel];
            var buckets = target.Buckets
                .OrderByDescending(item => item.Key)
                .Take(resolvedMaxBucketRows)
                .Select(item => new GuardRetryTimelineBucketSnapshot
                {
                    BucketStartUtc = item.Value.BucketStartUtc,
                    Samples = item.Value.Samples,
                    RetryRequiredCount = item.Value.RetryRequiredCount,
                    MaxRetryAttempt = item.Value.MaxRetryAttempt,
                    MaxRetryMaxAttempts = item.Value.MaxRetryMaxAttempts,
                    TopRetryStopReason = PickTopStopReason(item.Value.StopReasonCounts),
                    UniqueRetryStopReasons = item.Value.StopReasonCounts.Count
                })
                .ToList();

            snapshotChannels.Add(new GuardRetryTimelineChannelSnapshot
            {
                Channel = channel,
                TotalSamples = target.TotalSamples,
                RetryRequiredSamples = target.RetryRequiredSamples,
                MaxRetryAttempt = target.MaxRetryAttempt,
                MaxRetryMaxAttempts = target.MaxRetryMaxAttempts,
                LastRetryStopReason = target.LastRetryStopReason,
                Buckets = buckets
            });
        }

        return new GuardRetryTimelineSnapshot
        {
            SchemaVersion = SchemaVersion,
            GeneratedAtUtc = now.ToString("O"),
            BucketMinutes = resolvedBucketMinutes,
            WindowMinutes = resolvedWindowMinutes,
            Channels = snapshotChannels
        };
    }

    private static List<string> ResolveChannels(IReadOnlyList<string>? channels)
    {
        if (channels is null || channels.Count == 0)
        {
            return DefaultChannels.ToList();
        }

        var resolved = new List<string>();
        foreach (var channel in channels)
        {
            var normalized = NormalizeChannel(channel);
            if (normalized is null || resolved.Contains(normalized, StringComparer.Ordinal))
            {
                continue;
            }

            resolved.Add(normalized);
        }

        return resolved.Count > 0 ? resolved : DefaultChannels.ToList();
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_statePath))
        {
            return;
        }

        try
        {
            var raw = AtomicFileStore.ReadAllTextWithBackup(
                _statePath,
                IsValidPersistedStateJson,
                Encoding.UTF8,
                "guard-retry-timeline"
            );
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var persisted = JsonSerializer.Deserialize(
                raw,
                GuardRetryTimelineJsonContext.Default.GuardRetryTimelinePersistedState
            );
            if (persisted?.Entries is null || persisted.Entries.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                _entries.Clear();
                foreach (var entry in persisted.Entries)
                {
                    var normalizedChannel = NormalizeChannel(entry.Channel);
                    if (normalizedChannel is null)
                    {
                        continue;
                    }

                    _entries.Add(new GuardRetryTimelineEntry
                    {
                        Id = string.IsNullOrWhiteSpace(entry.Id) ? "-" : entry.Id.Trim(),
                        CapturedAt = string.IsNullOrWhiteSpace(entry.CapturedAt)
                            ? DateTimeOffset.UtcNow.ToString("O")
                            : entry.CapturedAt.Trim(),
                        Channel = normalizedChannel,
                        RetryRequired = entry.RetryRequired,
                        RetryAttempt = Math.Max(0, entry.RetryAttempt),
                        RetryMaxAttempts = Math.Max(0, entry.RetryMaxAttempts),
                        RetryStopReason = NormalizeRetryStopReason(entry.RetryStopReason)
                    });
                }

                TrimToMaxEntriesLocked();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[guard-retry-timeline] state load failed: {ex.Message}");
        }
    }

    private static bool IsValidPersistedStateJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            return JsonSerializer.Deserialize(
                raw,
                GuardRetryTimelineJsonContext.Default.GuardRetryTimelinePersistedState
            ) != null;
        }
        catch
        {
            return false;
        }
    }

    private void SaveToDiskLocked()
    {
        var payload = new GuardRetryTimelinePersistedState
        {
            SchemaVersion = SchemaVersion,
            SavedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            MaxEntries = _maxEntries,
            Entries = _entries.ToList()
        };

        var raw = JsonSerializer.Serialize(
            payload,
            GuardRetryTimelineJsonContext.Default.GuardRetryTimelinePersistedState
        );
        AtomicFileStore.WriteAllText(_statePath, raw, ownerOnly: true);
    }

    private void TrimToMaxEntriesLocked()
    {
        if (_entries.Count <= _maxEntries)
        {
            return;
        }

        _entries.RemoveRange(_maxEntries, _entries.Count - _maxEntries);
    }

    private static string? NormalizeChannel(string? channel)
    {
        var normalized = (channel ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "chat" or "coding" or "telegram" => normalized,
            _ => null
        };
    }

    private static string NormalizeRetryStopReason(string? retryStopReason)
    {
        var normalized = (retryStopReason ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "-";
        }

        var tokens = normalized
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return "-";
        }

        return string.Join("_", tokens);
    }

    private static string PickTopStopReason(Dictionary<string, int> stopReasonCounts)
    {
        if (stopReasonCounts.Count == 0)
        {
            return "-";
        }

        return stopReasonCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .First()
            .Key;
    }

    private sealed class ChannelAccumulator
    {
        public ChannelAccumulator(string channel)
        {
            Channel = channel;
        }

        public string Channel { get; }

        public int TotalSamples { get; set; }

        public int RetryRequiredSamples { get; set; }

        public int MaxRetryAttempt { get; set; }

        public int MaxRetryMaxAttempts { get; set; }

        public string LastRetryStopReason { get; set; } = "-";

        public Dictionary<long, BucketAccumulator> Buckets { get; } = new();
    }

    private sealed class BucketAccumulator
    {
        public BucketAccumulator(string bucketStartUtc)
        {
            BucketStartUtc = bucketStartUtc;
        }

        public string BucketStartUtc { get; }

        public int Samples { get; set; }

        public int RetryRequiredCount { get; set; }

        public int MaxRetryAttempt { get; set; }

        public int MaxRetryMaxAttempts { get; set; }

        public Dictionary<string, int> StopReasonCounts { get; } = new(StringComparer.Ordinal);
    }

    internal sealed class GuardRetryTimelinePersistedState
    {
        public string SchemaVersion { get; set; } = GuardRetryTimelineStore.SchemaVersion;

        public string SavedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

        public int MaxEntries { get; set; } = DefaultMaxEntries;

        public List<GuardRetryTimelineEntry> Entries { get; set; } = [];
    }

    internal sealed class GuardRetryTimelineSnapshot
    {
        public string SchemaVersion { get; set; } = GuardRetryTimelineStore.SchemaVersion;

        public string GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");

        public int BucketMinutes { get; set; } = DefaultBucketMinutes;

        public int WindowMinutes { get; set; } = DefaultWindowMinutes;

        public List<GuardRetryTimelineChannelSnapshot> Channels { get; set; } = [];
    }

    internal sealed class GuardRetryTimelineChannelSnapshot
    {
        public string Channel { get; set; } = "-";

        public int TotalSamples { get; set; }

        public int RetryRequiredSamples { get; set; }

        public int MaxRetryAttempt { get; set; }

        public int MaxRetryMaxAttempts { get; set; }

        public string LastRetryStopReason { get; set; } = "-";

        public List<GuardRetryTimelineBucketSnapshot> Buckets { get; set; } = [];
    }

    internal sealed class GuardRetryTimelineBucketSnapshot
    {
        public string BucketStartUtc { get; set; } = "-";

        public int Samples { get; set; }

        public int RetryRequiredCount { get; set; }

        public int MaxRetryAttempt { get; set; }

        public int MaxRetryMaxAttempts { get; set; }

        public string TopRetryStopReason { get; set; } = "-";

        public int UniqueRetryStopReasons { get; set; }
    }

    internal sealed class GuardRetryTimelineEntry
    {
        public string Id { get; set; } = "-";

        public string CapturedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

        public string Channel { get; set; } = "-";

        public bool RetryRequired { get; set; }

        public int RetryAttempt { get; set; }

        public int RetryMaxAttempts { get; set; }

        public string RetryStopReason { get; set; } = "-";
    }
}
