using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

public sealed partial class WebSocketGateway
{
    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
    }

    internal static TimeSpan ResolveTrustedAuthTtl(int? authTtlHours)
    {
        var hours = authTtlHours ?? DefaultTrustedAuthTtlHours;
        if (hours < MinTrustedAuthTtlHours)
        {
            hours = MinTrustedAuthTtlHours;
        }
        else if (hours > MaxTrustedAuthTtlHours)
        {
            hours = MaxTrustedAuthTtlHours;
        }

        return TimeSpan.FromHours(hours);
    }

    private static string? BuildGuardAlertEventJson(string? rawJson)
    {
        var normalized = (rawJson ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (doc.RootElement.TryGetProperty("guardAlertEvent", out var guardAlertElement)
                && guardAlertElement.ValueKind == JsonValueKind.Object)
            {
                return guardAlertElement.GetRawText();
            }

            if (doc.RootElement.TryGetProperty("payload", out var payloadElement)
                && payloadElement.ValueKind == JsonValueKind.Object)
            {
                return payloadElement.GetRawText();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? BuildCronAddJobJson(string? rawJson)
    {
        var normalized = (rawJson ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = doc.RootElement;
            if (root.TryGetProperty("job", out var jobElement)
                && jobElement.ValueKind == JsonValueKind.Object)
            {
                return jobElement.GetRawText();
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                var found = false;
                foreach (var property in root.EnumerateObject())
                {
                    if (!IsCronAddSyntheticJobField(property.Name))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                    found = true;
                }

                writer.WriteEndObject();
                writer.Flush();
                if (!found)
                {
                    return null;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? BuildCronUpdatePatchJson(string? rawJson)
    {
        var normalized = (rawJson ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = doc.RootElement;
            if (root.TryGetProperty("patch", out var patchElement)
                && patchElement.ValueKind == JsonValueKind.Object)
            {
                return patchElement.GetRawText();
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                var found = false;
                foreach (var property in root.EnumerateObject())
                {
                    if (!IsCronUpdateSyntheticPatchField(property.Name))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                    found = true;
                }

                writer.WriteEndObject();
                writer.Flush();
                if (!found)
                {
                    return null;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsCronAddSyntheticJobField(string name)
    {
        return name switch
        {
            "name" => true,
            "description" => true,
            "enabled" => true,
            "schedule" => true,
            "sessionTarget" => true,
            "wakeMode" => true,
            "payload" => true,
            "text" => true,
            "message" => true,
            "model" => true,
            "thinking" => true,
            "timeoutSeconds" => true,
            "lightContext" => true,
            _ => false
        };
    }

    private static bool IsCronUpdateSyntheticPatchField(string name)
    {
        return name switch
        {
            "name" => true,
            "description" => true,
            "enabled" => true,
            "schedule" => true,
            "sessionTarget" => true,
            "wakeMode" => true,
            "payload" => true,
            "text" => true,
            "message" => true,
            "model" => true,
            "thinking" => true,
            "timeoutSeconds" => true,
            "lightContext" => true,
            _ => false
        };
    }

    internal static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4"));
                    }
                    else if (ch == '\u2028')
                    {
                        builder.Append("\\u2028");
                    }
                    else if (ch == '\u2029')
                    {
                        builder.Append("\\u2029");
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    internal static string FormatLocalDateTime(DateTimeOffset utc)
    {
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    internal static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset < TimeSpan.Zero ? offset.Negate() : offset;
        return $"UTC{sign}{abs:hh\\:mm}";
    }

    internal sealed class ClientMessage
    {
        public string RawJson { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Otp { get; set; }
        public string? AuthToken { get; set; }
        public string? Text { get; set; }
        public string? Message { get; set; }
        public string? Query { get; set; }
        public string? Search { get; set; }
        public string? Freshness { get; set; }
        public string? MemoryPath { get; set; }
        public string? WebFetchUrl { get; set; }
        public string? ExtractMode { get; set; }
        public string? Model { get; set; }
        public string? Provider { get; set; }
        public string? GroqModel { get; set; }
        public string? GeminiModel { get; set; }
        public string? CopilotModel { get; set; }
        public string? CerebrasModel { get; set; }
        public string? NvidiaModel { get; set; }
        public string? CodexModel { get; set; }
        public string? SummaryProvider { get; set; }
        public string? Action { get; set; }
        public string? JobId { get; set; }
        public string? CronId { get; set; }
        public string? RunMode { get; set; }
        public string? Target { get; set; }
        public string? TargetId { get; set; }
        public string? Node { get; set; }
        public string? RequestId { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Priority { get; set; }
        public string? Delivery { get; set; }
        public string? InvokeCommand { get; set; }
        public string? InvokeParamsJson { get; set; }
        public string? SpawnTask { get; set; }
        public string? Label { get; set; }
        public string? Runtime { get; set; }
        public string? SessionKey { get; set; }
        public string? Scope { get; set; }
        public string? Mode { get; set; }
        public string? Profile { get; set; }
        public string? OutputFormat { get; set; }
        public string? ConversationId { get; set; }
        public string? StandardInput { get; set; }
        public string? ConversationTitle { get; set; }
        public string? Project { get; set; }
        public string? ProjectKey { get; set; }
        public string? Source { get; set; }
        public string? Category { get; set; }
        public string? Kind { get; set; }
        public string? PlanId { get; set; }
        public string? GraphId { get; set; }
        public string? LogicGraphJson { get; set; }
        public string? LogicRunId { get; set; }
        public string? LogicRunInput { get; set; }
        public string? TaskId { get; set; }
        public string? PreviewId { get; set; }
        public string? Language { get; set; }
        public string? Symbol { get; set; }
        public string? Pattern { get; set; }
        public string? Replacement { get; set; }
        public string? NoteName { get; set; }
        public string? NewName { get; set; }
        public string? FilePath { get; set; }
        public string? SkillName { get; set; }
        public string? SkillScope { get; set; }
        public string? SkillDescription { get; set; }
        public string? SkillBody { get; set; }
        public bool? ThinkPlus { get; set; }
        public string? RoutineId { get; set; }
        public string? ExecutionMode { get; set; }
        public string? AgentProvider { get; set; }
        public string? AgentModel { get; set; }
        public string? AgentStartUrl { get; set; }
        public string? AgentToolProfile { get; set; }
        public string? ScheduleSourceMode { get; set; }
        public string? NotifyPolicy { get; set; }
        public bool? NotifyTelegram { get; set; }
        public string? ScheduleKind { get; set; }
        public string? ScheduleTime { get; set; }
        public string? TimezoneId { get; set; }
        public string? TelegramBotToken { get; set; }
        public string? TelegramChatId { get; set; }
        public string? GroqApiKey { get; set; }
        public string? GeminiApiKey { get; set; }
        public string? CerebrasApiKey { get; set; }
        public string? NvidiaApiKey { get; set; }
        public string? CodexApiKey { get; set; }
        public string? RoutingPolicyJson { get; set; }
        public string? RefactorEditsJson { get; set; }
        public string? ContentBase64 { get; set; }
        public string? FileName { get; set; }
        public int? AuthTtlHours { get; set; }
        public int? TimeoutSeconds { get; set; }
        public int? RunTimeoutSeconds { get; set; }
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public int? Count { get; set; }
        public int? ActiveMinutes { get; set; }
        public int? MessageLimit { get; set; }
        public int? MaxResults { get; set; }
        public int? MaxChars { get; set; }
        public int? MaxWidth { get; set; }
        public double? MinScore { get; set; }
        public int? FromLine { get; set; }
        public int? Lines { get; set; }
        public int? DayOfMonth { get; set; }
        public int? MaxRetries { get; set; }
        public int? RetryDelaySeconds { get; set; }
        public int? AgentTimeoutSeconds { get; set; }
        public long? Timestamp { get; set; }
        public bool? Enabled { get; set; }
        public bool? AgentUsePlaywright { get; set; }
        public bool? RunImmediately { get; set; }
        public bool? Overwrite { get; set; }
        public bool? CompactConversation { get; set; }
        public bool? IncludeDisabled { get; set; }
        public bool? IncludeTools { get; set; }
        public bool? Thread { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Constraints { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Kinds { get; set; } = Array.Empty<string>();
        public IReadOnlyList<int> Weekdays { get; set; } = Array.Empty<int>();
        public IReadOnlyList<string> MemoryNotes { get; set; } = Array.Empty<string>();
        public IReadOnlyList<InputAttachment> Attachments { get; set; } = Array.Empty<InputAttachment>();
        public IReadOnlyList<string> WebUrls { get; set; } = Array.Empty<string>();
        public bool WebSearchEnabled { get; set; } = true;
        public bool Persist { get; set; }
    }
}
