using System.Text.Json;

namespace OmniNode.Middleware;

public sealed class LogicViewport
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class LogicNodePosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class LogicNodeSize
{
    public double Width { get; set; } = 168;
    public double Height { get; set; } = 126;
}

public sealed class LogicGraphSchedule
{
    public string ScheduleSourceMode { get; set; } = "manual";
    public string ScheduleKind { get; set; } = "daily";
    public string? ScheduleTime { get; set; } = "08:00";
    public string TimezoneId { get; set; } = TimeZoneInfo.Local.Id;
    public int? DayOfMonth { get; set; }
    public List<int> Weekdays { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public sealed class LogicEdgeCondition
{
    public string LeftRef { get; set; } = string.Empty;
    public string Operator { get; set; } = "equals";
    public string RightValue { get; set; } = string.Empty;
}

public sealed class LogicNodeDefinition
{
    public string NodeId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public LogicNodePosition Position { get; set; } = new();
    public LogicNodeSize Size { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool ContinueOnError { get; set; }
    public Dictionary<string, string> Config { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.Ordinal);
}

public sealed class LogicEdgeDefinition
{
    public string EdgeId { get; set; } = string.Empty;
    public string SourceNodeId { get; set; } = string.Empty;
    public string SourcePort { get; set; } = "main";
    public string TargetNodeId { get; set; } = string.Empty;
    public string TargetPort { get; set; } = "main";
    public LogicEdgeCondition? Condition { get; set; }
}

public sealed class LogicGraphDefinition
{
    public string GraphId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "logic.graph.v1";
    public LogicViewport Viewport { get; set; } = new();
    public LogicGraphSchedule Schedule { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public List<LogicNodeDefinition> Nodes { get; set; } = new();
    public List<LogicEdgeDefinition> Edges { get; set; } = new();
}

public sealed record LogicGraphSummary(
    string GraphId,
    string Title,
    string Description,
    string Version,
    bool Enabled,
    string ScheduleKind,
    string? ScheduleTime,
    string TimezoneId,
    string NextRunLocal,
    string LastRunLocal,
    string LastStatus,
    int NodeCount,
    int EdgeCount,
    string ActiveRunId = ""
);

public sealed record LogicGraphActionResult(
    bool Ok,
    string Message,
    LogicGraphSummary? Summary,
    LogicGraphDefinition? Graph
);

public sealed record LogicGraphListResult(
    IReadOnlyList<LogicGraphSummary> Items
);

public sealed record LogicPathBrowseRoot(
    string Key,
    string Label
);

public sealed record LogicPathBrowseEntry(
    string Name,
    bool IsDirectory,
    string BrowsePath,
    string SelectPath,
    string Description
);

public sealed record LogicPathBrowseResult(
    bool Ok,
    string Message,
    string Scope,
    string RootKey,
    string RootLabel,
    string DisplayPath,
    string BrowsePath,
    string? ParentBrowsePath,
    string? DirectorySelectPath,
    IReadOnlyList<LogicPathBrowseRoot> Roots,
    IReadOnlyList<LogicPathBrowseEntry> Items
);

public sealed record LogicNodeResultEnvelope(
    bool Ok,
    string Type,
    string Text,
    IReadOnlyDictionary<string, string> Data,
    IReadOnlyList<string> Artifacts,
    string? ConversationId,
    string? SessionKey,
    IReadOnlyList<string> Links
);

/// <summary>
/// 로직 그래프 1회 실행의 가변 상태 holder. run 입력/작업 디렉터리, 변수,
/// 노드 결과, 산출물, 세션 매핑을 담는다. 템플릿/참조 resolver가 읽기 전용으로 참조한다.
/// </summary>
internal sealed class LogicExecutionContext
{
    public string RunInput { get; init; } = string.Empty;
    public string RunDirectory { get; init; } = string.Empty;
    public Dictionary<string, string> Vars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, LogicNodeResultEnvelope> Nodes { get; } = new(StringComparer.Ordinal);
    public List<string> Artifacts { get; } = new();
    public Dictionary<string, string> Sessions { get; } = new(StringComparer.Ordinal);
}

public sealed record LogicNodeRunState(
    string NodeId,
    string Type,
    string Title,
    string Status,
    string? Error,
    string StartedAtUtc,
    string CompletedAtUtc,
    LogicNodeResultEnvelope? Result
);

public sealed record LogicRunSnapshot(
    string RunId,
    string GraphId,
    string Title,
    string Status,
    string Source,
    string StartedAtUtc,
    string UpdatedAtUtc,
    string CompletedAtUtc,
    string ResultText,
    string Error,
    IReadOnlyList<string> Logs,
    IReadOnlyList<LogicNodeRunState> Nodes
);

public sealed record LogicRunActionResult(
    bool Ok,
    string Message,
    string? RunId,
    LogicRunSnapshot? Snapshot
);

public sealed record LogicRunEvent(
    string RunId,
    string GraphId,
    string Kind,
    string Message,
    string? NodeId,
    LogicRunSnapshot Snapshot
);

internal sealed record LogicGraphValidationResult(
    bool Ok,
    string Message
);

internal static class LogicGraphJson
{
    public static string Serialize(LogicGraphDefinition value)
    {
        return JsonSerializer.Serialize(value, LogicGraphJsonContext.Default.LogicGraphDefinition);
    }

    public static string Serialize(LogicGraphSummary value)
    {
        return JsonSerializer.Serialize(value, LogicGraphJsonContext.Default.LogicGraphSummary);
    }

    public static string Serialize(IReadOnlyList<LogicGraphSummary> value)
    {
        return JsonSerializer.Serialize(
            value as LogicGraphSummary[] ?? value.ToArray(),
            LogicGraphJsonContext.Default.LogicGraphSummaryArray
        );
    }

    public static string Serialize(IReadOnlyList<LogicPathBrowseRoot> value)
    {
        return JsonSerializer.Serialize(
            value as LogicPathBrowseRoot[] ?? value.ToArray(),
            LogicGraphJsonContext.Default.LogicPathBrowseRootArray
        );
    }

    public static string Serialize(IReadOnlyList<LogicPathBrowseEntry> value)
    {
        return JsonSerializer.Serialize(
            value as LogicPathBrowseEntry[] ?? value.ToArray(),
            LogicGraphJsonContext.Default.LogicPathBrowseEntryArray
        );
    }

    public static string Serialize(LogicRunSnapshot value)
    {
        return JsonSerializer.Serialize(value, LogicGraphJsonContext.Default.LogicRunSnapshot);
    }

    public static LogicGraphDefinition? DeserializeDefinition(string json)
    {
        return JsonSerializer.Deserialize(json, LogicGraphJsonContext.Default.LogicGraphDefinition);
    }

    public static LogicRunSnapshot? DeserializeSnapshot(string json)
    {
        return JsonSerializer.Deserialize(json, LogicGraphJsonContext.Default.LogicRunSnapshot);
    }
}
