using System.Text;

namespace OmniNode.Middleware;

internal static class ConversationHistoryPolicy
{
    /// <summary>
    /// BuildHistoryText 결과("[user] ...\n[assistant] ...")를 개별 메시지 블록으로 파싱한다.
    /// </summary>
    public static List<(string Role, string Text)> ParseHistoryMessages(string history)
    {
        var result = new List<(string Role, string Text)>();
        if (string.IsNullOrWhiteSpace(history))
        {
            return result;
        }

        var rawLines = history.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? currentRole = null;
        var currentText = new StringBuilder();

        for (var i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i];
            if (line.StartsWith("[user] ", StringComparison.Ordinal))
            {
                if (currentRole != null)
                {
                    result.Add((currentRole, currentText.ToString().TrimEnd()));
                }

                currentRole = "user";
                currentText.Clear();
                currentText.Append(line["[user] ".Length..]);
            }
            else if (line.StartsWith("[assistant] ", StringComparison.Ordinal))
            {
                if (currentRole != null)
                {
                    result.Add((currentRole, currentText.ToString().TrimEnd()));
                }

                currentRole = "assistant";
                currentText.Clear();
                currentText.Append(line["[assistant] ".Length..]);
            }
            else if (currentRole != null)
            {
                currentText.Append('\n');
                currentText.Append(line);
            }
        }

        if (currentRole != null)
        {
            result.Add((currentRole, currentText.ToString().TrimEnd()));
        }

        return result;
    }

    public static string TrimContextHistory(string history, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(history))
        {
            return string.Empty;
        }

        var trimmed = history.Trim();
        if (trimmed.Length <= maxChars)
        {
            return trimmed;
        }

        var messages = ParseHistoryMessages(trimmed);
        if (messages.Count == 0)
        {
            return trimmed[^maxChars..];
        }

        var kept = new List<string>();
        var budget = maxChars;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var prefix = $"[{msg.Role}] ";
            var available = budget - prefix.Length - 1;

            if (available <= 0)
            {
                break;
            }

            var text = msg.Text.Length <= available ? msg.Text : msg.Text[..available] + "...";
            var block = $"{prefix}{text}";
            kept.Insert(0, block);
            budget -= block.Length + 1;
        }

        return string.Join('\n', kept);
    }

    public static string BuildBudgetedContextHistory(string history, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(history))
        {
            return string.Empty;
        }

        var messages = ParseHistoryMessages(history);
        if (messages.Count == 0)
        {
            return string.Empty;
        }

        const int RecentMessageCount = 4;
        const int MaxCharsPerRecentMessage = 800;
        var recentCount = Math.Min(messages.Count, RecentMessageCount);
        var olderCount = Math.Max(0, messages.Count - recentCount);

        var recentParts = new List<string>();
        foreach (var msg in messages.Skip(olderCount))
        {
            var text = msg.Text.Length <= MaxCharsPerRecentMessage
                ? msg.Text
                : msg.Text[..MaxCharsPerRecentMessage] + "...";
            recentParts.Add($"[{msg.Role}] {text}");
        }

        var recent = string.Join('\n', recentParts);
        if (olderCount == 0)
        {
            return TrimContextHistory(recent, maxChars);
        }

        var olderMessages = messages.Take(olderCount).ToList();
        var olderSummary = BuildMessageLevelSummary(olderMessages, 1200);
        var combined = string.IsNullOrWhiteSpace(olderSummary)
            ? recent
            : $"[이전 대화 압축]\n{olderSummary}\n\n[최근 턴]\n{recent}";
        return combined.Length <= maxChars ? combined : TrimContextHistory(combined, maxChars);
    }

    public static string BuildMessageLevelSummary(IReadOnlyList<(string Role, string Text)> olderMessages, int maxChars)
    {
        if (olderMessages.Count == 0)
        {
            return string.Empty;
        }

        const int MaxCharsPerOlderMessage = 300;
        var selected = new List<string>();

        foreach (var msg in olderMessages)
        {
            var lines = msg.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var hasHighSignal = lines.Any(IsHighSignalHistoryLine);
            if (!hasHighSignal)
            {
                continue;
            }

            var text = msg.Text.Length <= MaxCharsPerOlderMessage
                ? msg.Text
                : msg.Text[..MaxCharsPerOlderMessage] + "...";
            selected.Add($"[{msg.Role}] {text}");
            if (selected.Count >= 4)
            {
                break;
            }
        }

        if (selected.Count == 0)
        {
            foreach (var msg in olderMessages.TakeLast(Math.Min(2, olderMessages.Count)))
            {
                var text = msg.Text.Length <= MaxCharsPerOlderMessage
                    ? msg.Text
                    : msg.Text[..MaxCharsPerOlderMessage] + "...";
                selected.Add($"[{msg.Role}] {text}");
            }
        }

        var summary = string.Join('\n', selected);
        return summary.Length <= maxChars ? summary : summary[..maxChars].TrimEnd() + "...";
    }

    public static bool IsHighSignalHistoryLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var lowered = line.ToLowerInvariant();
        return lowered.Contains("요구", StringComparison.Ordinal)
               || lowered.Contains("수정", StringComparison.Ordinal)
               || lowered.Contains("오류", StringComparison.Ordinal)
               || lowered.Contains("결정", StringComparison.Ordinal)
               || lowered.Contains("파일", StringComparison.Ordinal)
               || lowered.Contains("설정", StringComparison.Ordinal)
               || lowered.Contains("스킬", StringComparison.Ordinal)
               || lowered.Contains("think+", StringComparison.Ordinal)
               || lowered.Contains("nvidia", StringComparison.Ordinal)
               || lowered.Contains("error", StringComparison.Ordinal)
               || lowered.Contains("fix", StringComparison.Ordinal)
               || lowered.Contains("bug", StringComparison.Ordinal)
               || lowered.Contains("todo", StringComparison.Ordinal)
               || lowered.Contains("requirement", StringComparison.Ordinal)
               || lowered.Contains("검색", StringComparison.Ordinal)
               || lowered.Contains("api", StringComparison.Ordinal)
               || lowered.Contains("llm", StringComparison.Ordinal)
               || lowered.Contains("모델", StringComparison.Ordinal)
               || lowered.Contains("결과", StringComparison.Ordinal)
               || lowered.Contains("답변", StringComparison.Ordinal)
               || lowered.Contains("추천", StringComparison.Ordinal)
               || lowered.Contains("비교", StringComparison.Ordinal)
               || lowered.Contains("분석", StringComparison.Ordinal)
               || lowered.Contains("search", StringComparison.Ordinal)
               || lowered.Contains("web", StringComparison.Ordinal)
               || lowered.Contains("result", StringComparison.Ordinal)
               || lowered.Contains("recommend", StringComparison.Ordinal)
               || lowered.Contains("tavily", StringComparison.Ordinal)
               || lowered.Contains("rag", StringComparison.Ordinal)
               || lowered.Contains("프레임워크", StringComparison.Ordinal)
               || lowered.Contains("도구", StringComparison.Ordinal)
               || lowered.Contains("tool", StringComparison.Ordinal)
               || lowered.Contains("service", StringComparison.Ordinal)
               || lowered.Contains("framework", StringComparison.Ordinal)
               || lowered.Contains("통합", StringComparison.Ordinal)
               || lowered.Contains("구현", StringComparison.Ordinal)
               || lowered.Contains("서비스", StringComparison.Ordinal)
               || lowered.Contains("필터", StringComparison.Ordinal)
               || lowered.Contains("filter", StringComparison.Ordinal);
    }
}
