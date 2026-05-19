using System.Text;

namespace OmniNode.Middleware;

internal static class MultiComparisonPolicy
{
    public static bool ShouldIncludeComparisonEntry(string model, string text)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        var normalizedText = (text ?? string.Empty).Trim();
        if (normalizedModel.Equals("none", StringComparison.OrdinalIgnoreCase)
            && (normalizedText.Length == 0 || normalizedText.Equals("선택 안함", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return normalizedModel.Length > 0 || normalizedText.Length > 0;
    }

    public static string BuildComparisonAssistantText(LlmMultiChatResult result)
    {
        var entries = new List<(string Provider, string Model, string Text)>();

        void AddEntry(string provider, string model, string text)
        {
            if (!ShouldIncludeComparisonEntry(model, text))
            {
                return;
            }

            entries.Add((provider, model ?? string.Empty, text ?? string.Empty));
        }

        AddEntry("groq", result.GroqModel, result.GroqText);
        AddEntry("gemini", result.GeminiModel, result.GeminiText);
        AddEntry("cerebras", result.CerebrasModel, result.CerebrasText);
        AddEntry("nvidia", result.NvidiaModel, result.NvidiaText);
        AddEntry("copilot", result.CopilotModel, result.CopilotText);
        AddEntry("codex", result.CodexModel, result.CodexText);

        var builder = new StringBuilder();
        builder.Append("[[OMNI_MULTI_COMPARE_JSON]]{\"entries\":[");
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(",");
            }

            var entry = entries[i];
            builder.Append("{");
            builder.Append($"\"provider\":\"{WebSocketGateway.EscapeJson(entry.Provider)}\",");
            builder.Append($"\"model\":\"{WebSocketGateway.EscapeJson(entry.Model)}\",");
            builder.Append($"\"text\":\"{WebSocketGateway.EscapeJson(entry.Text)}\"");
            builder.Append("}");
        }

        builder.Append("]}");
        return builder.ToString();
    }

    public static (string CommonSummary, string CommonPoints, string Differences, string Recommendation) ParseComparisonSummarySections(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return ("공통 요약을 생성하지 못했습니다.", "공통점 없음", "부분 차이 정리가 없습니다.", "추천 정리가 없습니다.");
        }

        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string currentKey = "summary";

        static string DetectSectionKey(string line)
        {
            var normalized = (line ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            normalized = normalized.TrimStart('#', '-', '*', ' ').Trim();
            if (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
            {
                normalized = normalized[1..^1].Trim();
            }

            if (normalized.EndsWith(":", StringComparison.Ordinal))
            {
                normalized = normalized[..^1].Trim();
            }

            return normalized switch
            {
                "공통 요약" => "summary",
                "요약" => "summary",
                "공통점" => "common_points",
                "공통 핵심" => "core",
                "핵심" => "core",
                "공통" => "common_points",
                "추천" => "recommendation",
                "부분 차이" => "differences",
                "차이" => "differences",
                _ => string.Empty
            };
        }

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (!sections.ContainsKey(currentKey))
                {
                    sections[currentKey] = new List<string>();
                }

                if (sections[currentKey].Count > 0 && sections[currentKey][^1].Length > 0)
                {
                    sections[currentKey].Add(string.Empty);
                }

                continue;
            }

            var sectionKey = DetectSectionKey(trimmed);
            if (sectionKey.Length > 0)
            {
                currentKey = sectionKey;
                sections.TryAdd(currentKey, new List<string>());
                continue;
            }

            if (!sections.ContainsKey(currentKey))
            {
                sections[currentKey] = new List<string>();
            }

            sections[currentKey].Add(line);
        }

        static string JoinSection(Dictionary<string, List<string>> source, string key, string fallback)
        {
            if (!source.TryGetValue(key, out var lines))
            {
                return fallback;
            }

            var text = string.Join("\n", lines).Trim();
            return text.Length == 0 ? fallback : text;
        }

        var commonSummary = JoinSection(sections, "summary", normalized);
        var commonPoints = JoinSection(
            sections,
            sections.ContainsKey("common_points") ? "common_points" : "core",
            "공통점 없음"
        );
        var differences = JoinSection(sections, "differences", "부분 차이 정리가 없습니다.");
        var recommendation = JoinSection(sections, "recommendation", "추천 정리가 없습니다.");
        return (commonSummary, commonPoints, differences, recommendation);
    }

    public static (string CommonSummary, string CommonCore, string Differences) ParseMultiSummarySections(string text)
    {
        var parsed = ParseComparisonSummarySections(text);
        return (parsed.CommonSummary, parsed.CommonPoints, parsed.Differences);
    }

    public static (string CommonSummary, string CommonPoints, string Differences, string Recommendation) ParseCodingMultiSummarySections(string text)
    {
        return ParseComparisonSummarySections(text);
    }

    public static string BuildMultiSummaryAssistantText(string commonSummary, string commonCore, string differences)
    {
        var summary = string.IsNullOrWhiteSpace(commonSummary) ? "공통 요약을 생성하지 못했습니다." : commonSummary.Trim();
        var core = string.IsNullOrWhiteSpace(commonCore) ? "공통점 없음" : commonCore.Trim();
        var diff = string.IsNullOrWhiteSpace(differences) ? "부분 차이 정리가 없습니다." : differences.Trim();
        return $"""
                ### 공통 요약
                {summary}

                ### 공통 핵심
                {core}

                ### 부분 차이
                {diff}
                """;
    }
}
