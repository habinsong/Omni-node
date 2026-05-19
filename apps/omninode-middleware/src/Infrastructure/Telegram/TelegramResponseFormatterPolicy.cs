using System.Globalization;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class TelegramResponseFormatterPolicy
{
    public static string FormatSanitizedResponse(
        string text,
        int maxChars,
        Func<string, string>? normalizeStructuredLabelBlocks = null,
        Func<string, bool>? isStandaloneNumberedHeadlineLine = null,
        Func<string?, bool>? isMarkdownTableRow = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "응답이 비어 있습니다.";
        }

        const bool keepMarkdownTables = true;
        var normalized = ConvertMarkdownToPlainText(text, keepMarkdownTables);
        normalized = ImproveReadability(normalized, keepMarkdownTables, isStandaloneNumberedHeadlineLine, isMarkdownTableRow);
        normalized = CollapseTokenizedNumberedList(normalized);
        if (SearchAnswerFormatterPolicy.LooksLikeNumberedListResponse(normalized))
        {
            normalized = SearchAnswerFormatterPolicy.NormalizeNumberedListResponse(normalized);
        }

        if (normalizeStructuredLabelBlocks != null)
        {
            normalized = normalizeStructuredLabelBlocks(normalized);
        }

        normalized = MergeDetachedDecimalLines(normalized);
        normalized = MergeDetachedNumberLines(normalized, isMarkdownTableRow);
        normalized = AddClaimSpacing(normalized, isStandaloneNumberedHeadlineLine);

        var safeMaxChars = Math.Max(0, maxChars);
        if (safeMaxChars > 0 && normalized.Length > safeMaxChars)
        {
            return normalized[..safeMaxChars].TrimEnd() + "\n...(telegram_response_truncated)";
        }

        return normalized.Trim();
    }

    public static string ConvertMarkdownToPlainText(string text, bool keepMarkdownTables = false)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = ExpandCollapsedMarkdown(normalized);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var result = new List<string>(lines.Length + 12);
        var inCodeFence = false;
        string[]? tableHeaders = null;
        var tableAutoIndex = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                if (!inCodeFence)
                {
                    if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                    {
                        result.Add(string.Empty);
                    }

                    result.Add("[코드]");
                    inCodeFence = true;
                }
                else
                {
                    inCodeFence = false;
                    if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                    {
                        result.Add(string.Empty);
                    }
                }

                continue;
            }

            if (inCodeFence)
            {
                result.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                {
                    result.Add(string.Empty);
                }

                continue;
            }

            if (Regex.IsMatch(trimmed, @"^#{1,6}\s+"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var heading = Regex.Replace(trimmed, @"^#{1,6}\s+", string.Empty);
                heading = StripInlineMarkdown(heading);
                if (!string.IsNullOrWhiteSpace(heading))
                {
                    if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                    {
                        result.Add(string.Empty);
                    }

                    result.Add(heading);
                }

                continue;
            }

            if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1]))
                {
                    result.Add(string.Empty);
                }

                result.Add("-----");
                continue;
            }

            if (trimmed.StartsWith("|", StringComparison.Ordinal) && trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                if (keepMarkdownTables)
                {
                    tableHeaders = null;
                    tableAutoIndex = 0;
                    var preservedTableLine = NormalizeTableRowForPlainText(trimmed);
                    if (!string.IsNullOrWhiteSpace(preservedTableLine))
                    {
                        result.Add(preservedTableLine);
                    }

                    continue;
                }

                var cells = trimmed
                    .Trim('|')
                    .Split('|', StringSplitOptions.TrimEntries)
                    .Select(StripInlineMarkdown)
                    .ToArray();
                if (cells.Length == 0)
                {
                    continue;
                }

                if (cells.All(cell => Regex.IsMatch(cell, @"^:?-{2,}:?$")))
                {
                    continue;
                }

                if (tableHeaders == null && IsLikelyTableHeaderRow(cells))
                {
                    tableHeaders = cells;
                    tableAutoIndex = 0;
                    continue;
                }

                tableAutoIndex += 1;
                var itemNo = tableAutoIndex;
                var firstCell = cells[0];
                if (TryExtractLeadingNumber(firstCell, out var parsedNo, out var firstCellRemainder))
                {
                    itemNo = parsedNo;
                    firstCell = firstCellRemainder;
                }

                var details = new List<string>(4);
                if (tableHeaders != null && tableHeaders.Length >= 2)
                {
                    for (var ci = 0; ci < Math.Min(cells.Length, tableHeaders.Length); ci += 1)
                    {
                        var key = StripInlineMarkdown(tableHeaders[ci]);
                        var value = ci == 0 ? firstCell : cells[ci];
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(key))
                        {
                            details.Add(value);
                            continue;
                        }

                        details.Add($"{key}: {value}");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(firstCell))
                    {
                        details.Add(firstCell);
                    }

                    foreach (var extra in cells.Skip(1))
                    {
                        if (!string.IsNullOrWhiteSpace(extra))
                        {
                            details.Add(extra);
                        }
                    }
                }

                if (details.Count > 0)
                {
                    result.Add($"{itemNo}. {details[0]}");
                    foreach (var detail in details.Skip(1))
                    {
                        result.Add($"- {detail}");
                    }

                    result.Add(string.Empty);
                }

                continue;
            }

            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var quote = StripInlineMarkdown(trimmed.TrimStart('>', ' '));
                if (!string.IsNullOrWhiteSpace(quote))
                {
                    result.Add($"인용: {quote}");
                }

                continue;
            }

            if (Regex.IsMatch(trimmed, @"^\d+[.)]\s+"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var orderedLine = Regex.Replace(trimmed, @"^(\d+)[.)]\s+", "$1. ");
                orderedLine = StripInlineMarkdown(orderedLine);
                if (!string.IsNullOrWhiteSpace(orderedLine))
                {
                    result.Add(orderedLine);
                }

                continue;
            }

            if (Regex.IsMatch(trimmed, @"^[-*+]\s+"))
            {
                tableHeaders = null;
                tableAutoIndex = 0;
                var bulletContent = Regex.Replace(trimmed, @"^[-*+]\s+", string.Empty);
                bulletContent = StripInlineMarkdown(bulletContent);
                if (!string.IsNullOrWhiteSpace(bulletContent))
                {
                    result.Add($"- {bulletContent}");
                }

                continue;
            }

            tableHeaders = null;
            tableAutoIndex = 0;
            var plainLine = StripInlineMarkdown(trimmed);
            if (!string.IsNullOrWhiteSpace(plainLine))
            {
                result.Add(plainLine);
            }
        }

        var merged = string.Join('\n', result).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    public static string NormalizeTableRowForPlainText(string tableRow)
    {
        var trimmed = (tableRow ?? string.Empty).Trim();
        if (!trimmed.StartsWith("|", StringComparison.Ordinal)
            || !trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var cells = trimmed
            .Trim('|')
            .Split('|', StringSplitOptions.TrimEntries)
            .Select(StripInlineMarkdown)
            .ToArray();
        if (cells.Length == 0)
        {
            return string.Empty;
        }

        if (cells.All(cell => Regex.IsMatch(cell, @"^:?-{2,}:?$")))
        {
            var normalizedSeparator = cells.Select(cell =>
            {
                var compact = cell.Trim();
                var leadingColon = compact.StartsWith(':');
                var trailingColon = compact.EndsWith(':');
                var dashCount = Math.Max(3, compact.Count(ch => ch == '-'));
                return string.Concat(
                    leadingColon ? ":" : string.Empty,
                    new string('-', dashCount),
                    trailingColon ? ":" : string.Empty
                );
            });
            return "| " + string.Join(" | ", normalizedSeparator) + " |";
        }

        return "| " + string.Join(" | ", cells.Select(cell => cell.Trim())) + " |";
    }

    public static string ExpandCollapsedMarkdown(string text)
    {
        var normalized = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"\s+\|\s+\|", " |\n|");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    public static string StripInlineMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = text;
        value = Regex.Replace(value, @"!\[(?<alt>[^\]]*)\]\((?<url>[^)]+)\)", "[이미지] ${alt} (${url})");
        value = Regex.Replace(value, @"\[(?<title>[^\]]+)\]\((?<url>[^)]+)\)", "${title} (${url})");
        value = Regex.Replace(value, @"\[(?<title>[^\]]+)\]\[[^\]]*\]", "${title}");
        value = Regex.Replace(value, @"`{1,3}([^`]+)`{1,3}", "$1");
        value = Regex.Replace(value, @"(\*\*|__)(?<inner>.+?)\1", "${inner}");
        value = Regex.Replace(value, @"(\*|_)(?<inner>.+?)\1", "${inner}");
        value = Regex.Replace(value, @"~~(?<inner>.+?)~~", "${inner}");
        value = Regex.Replace(value, @"<[^>]+>", string.Empty);
        value = value
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal)
            .Replace("\\*", "*", StringComparison.Ordinal)
            .Replace("\\_", "_", StringComparison.Ordinal)
            .Replace("\\`", "`", StringComparison.Ordinal);
        value = Regex.Replace(value, @"[ \t]{2,}", " ");
        return value.Trim();
    }

    private static string ImproveReadability(
        string text,
        bool keepMarkdownTables,
        Func<string, bool>? isStandaloneNumberedHeadlineLine,
        Func<string?, bool>? isMarkdownTableRow)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);
        var tightWrap = ShouldUseTightWrap(normalized);
        var wrapWidth = tightWrap ? 200 : 4000;

        var sourceLines = normalized.Split('\n', StringSplitOptions.None);
        var lines = new List<string>(sourceLines.Length + 8);
        foreach (var rawLine in sourceLines)
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                continue;
            }

            if (keepMarkdownTables && IsMarkdownTableRow(line, isMarkdownTableRow))
            {
                lines.Add(line.Trim());
                continue;
            }

            if (TrySplitAsHeadlineList(line, out var headlineLines))
            {
                foreach (var headlineLine in headlineLines)
                {
                    lines.Add(headlineLine);
                }

                continue;
            }

            if (tightWrap && line.Length > 120)
            {
                line = Regex.Replace(
                    line,
                    @"([.!?])\s+(?!(?:md|js|cs|ts|jsx|tsx|txt|py|json|html|css|xml|yml|yaml|sh|ps1|bat|cmd|log|csv|exe|dll|zip|tar|gz)\b)(?=(?:[""'“‘(\[])?[가-힣A-Z])",
                    "$1\n",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
                line = Regex.Replace(
                    line,
                    @"(다\.|요\.)\s+(?!(?:md|js|cs|ts|jsx|tsx|txt|py|json|html|css|xml|yml|yaml|sh|ps1|bat|cmd|log|csv|exe|dll|zip|tar|gz)\b)(?=(?:[""'“‘(\[])?[가-힣A-Z])",
                    "$1\n",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
                line = Regex.Replace(line, @"(…+)\s+", "$1\n");
                line = Regex.Replace(line, @"\s+\|\s+\|", " |\n|");
                var splitLines = line.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var splitLine in splitLines)
                {
                    AddWrappedLines(lines, splitLine, wrapWidth, isStandaloneNumberedHeadlineLine);
                }

                continue;
            }

            AddWrappedLines(lines, line, wrapWidth, isStandaloneNumberedHeadlineLine);
        }

        var merged = string.Join('\n', lines).Trim();
        merged = Regex.Replace(merged, @"\n{3,}", "\n\n");
        return merged;
    }

    private static bool ShouldUseTightWrap(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var hasStructuredTitle = Regex.IsMatch(
            normalized,
            @"(?mi)^\s*(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?제목\s*[:：]",
            RegexOptions.CultureInvariant
        );
        var hasStructuredContent = Regex.IsMatch(
            normalized,
            @"(?mi)^\s*(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?내용\s*[:：]",
            RegexOptions.CultureInvariant
        );
        return hasStructuredTitle || hasStructuredContent;
    }

    private static void AddWrappedLines(
        List<string> output,
        string line,
        int maxWidth,
        Func<string, bool>? isStandaloneNumberedHeadlineLine)
    {
        var normalizedLine = (line ?? string.Empty).Trim();
        if (normalizedLine.Length == 0)
        {
            return;
        }

        if (IsTitleLine(normalizedLine, isStandaloneNumberedHeadlineLine)
            || IsSourceLine(normalizedLine)
            || IsCategoryLine(normalizedLine))
        {
            output.Add(normalizedLine);
            return;
        }

        var wrapped = WrapLongLine(normalizedLine, maxWidth).ToArray();
        foreach (var wrappedLine in wrapped)
        {
            output.Add(wrappedLine);
        }
    }

    private static string CollapseTokenizedNumberedList(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        var index = 0;
        while (index < lines.Length)
        {
            var line = (lines[index] ?? string.Empty).Trim();
            if (!TryParseShortNumberedToken(line, out var token))
            {
                output.Add(lines[index]);
                index += 1;
                continue;
            }

            var runTokens = new List<string> { token };
            var runStart = index;
            var expectedNumber = 2;
            var cursor = index + 1;
            while (cursor < lines.Length)
            {
                var next = (lines[cursor] ?? string.Empty).Trim();
                if (!TryParseShortNumberedToken(next, out var nextToken, out var parsedNumber)
                    || parsedNumber != expectedNumber)
                {
                    break;
                }

                runTokens.Add(nextToken);
                expectedNumber += 1;
                cursor += 1;
            }

            if (runTokens.Count >= 6)
            {
                output.Add(string.Join(' ', runTokens));
                index = cursor;
                continue;
            }

            for (var i = runStart; i < cursor; i += 1)
            {
                output.Add(lines[i]);
            }

            index = cursor;
        }

        var merged = string.Join('\n', output).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    private static string MergeDetachedNumberLines(string text, Func<string?, bool>? isMarkdownTableRow)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var current = (lines[i] ?? string.Empty).Trim();
            if (!Regex.IsMatch(current, @"^\d+\.$", RegexOptions.CultureInvariant))
            {
                output.Add(lines[i]);
                continue;
            }

            if (i + 1 >= lines.Length)
            {
                output.Add(lines[i]);
                continue;
            }

            var nextIndex = i + 1;
            while (nextIndex < lines.Length && string.IsNullOrWhiteSpace(lines[nextIndex]))
            {
                nextIndex += 1;
            }

            if (nextIndex >= lines.Length)
            {
                output.Add(lines[i]);
                continue;
            }

            var next = (lines[nextIndex] ?? string.Empty).Trim();
            if (next.Length == 0
                || Regex.IsMatch(next, @"^\d+\.$", RegexOptions.CultureInvariant)
                || next.StartsWith("```", StringComparison.Ordinal)
                || IsMarkdownTableRow(next, isMarkdownTableRow))
            {
                output.Add(lines[i]);
                continue;
            }

            output.Add($"{current} {next}");
            i = nextIndex;
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static string MergeDetachedDecimalLines(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(
            normalized,
            @"(\d+)\.\s*\n+(?=\d)",
            "$1.",
            RegexOptions.CultureInvariant
        );
        return Regex.Replace(normalized.Trim(), @"\n{3,}", "\n\n");
    }

    private static string AddClaimSpacing(string text, Func<string, bool>? isStandaloneNumberedHeadlineLine)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 24);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                continue;
            }

            if (ShouldInsertBlankBefore(trimmed, isStandaloneNumberedHeadlineLine)
                && output.Count > 0
                && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }

            output.Add(trimmed);

            var next = FindNextNonEmptyLine(lines, i + 1);
            if (ShouldInsertBlankAfter(trimmed, next, isStandaloneNumberedHeadlineLine)
                && output.Count > 0
                && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static bool IsLikelyTableHeaderRow(string[] cells)
    {
        if (cells == null || cells.Length < 2)
        {
            return false;
        }

        if (cells.All(cell => Regex.IsMatch(cell, @"^:?-{2,}:?$")))
        {
            return false;
        }

        if (cells.Any(cell => Regex.IsMatch(cell, @"^\d+[.)]?\s*")))
        {
            return false;
        }

        return true;
    }

    private static bool TryExtractLeadingNumber(string value, out int number, out string remainder)
    {
        number = 0;
        remainder = (value ?? string.Empty).Trim();
        var match = Regex.Match(remainder, @"^(?<num>\d+)[.)]?\s*(?<rest>.*)$");
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            number = 0;
            return false;
        }

        var rest = match.Groups["rest"].Value.Trim();
        remainder = string.IsNullOrWhiteSpace(rest) ? remainder : rest;
        return true;
    }

    private static bool ShouldInsertBlankBefore(string line, Func<string, bool>? isStandaloneNumberedHeadlineLine)
    {
        return IsClaimLine(line, isStandaloneNumberedHeadlineLine) || IsSourceLine(line);
    }

    private static bool ShouldInsertBlankAfter(string current, string? next, Func<string, bool>? isStandaloneNumberedHeadlineLine)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return false;
        }

        var currentTrimmed = (current ?? string.Empty).Trim();
        if (IsSourceLine(currentTrimmed))
        {
            return true;
        }

        var nextTrimmed = next.Trim();
        if (!IsClaimLine(nextTrimmed, isStandaloneNumberedHeadlineLine) && !IsSourceLine(nextTrimmed))
        {
            return false;
        }

        return currentTrimmed.EndsWith(".", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("다.", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("요.", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("니다.", StringComparison.Ordinal)
            || currentTrimmed.EndsWith(":", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("?", StringComparison.Ordinal)
            || currentTrimmed.EndsWith("!", StringComparison.Ordinal);
    }

    private static bool IsClaimLine(string line, Func<string, bool>? isStandaloneNumberedHeadlineLine)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return IsStandaloneNumberedHeadlineLine(trimmed, isStandaloneNumberedHeadlineLine)
            || trimmed.StartsWith("- ", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, @"^\d+\.\s+", RegexOptions.CultureInvariant)
            || Regex.IsMatch(trimmed, @"^[■□▪●◆▶▷]\s+", RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                trimmed,
                @"^(?:(?:No\.\d+|\d+[.)])\s*)?\*\*[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}\s*[:：]\*\*",
                RegexOptions.CultureInvariant
            )
            || Regex.IsMatch(
                trimmed,
                @"^(?:(?:No\.\d+|\d+[.)])\s*)?(제목|내용)\s*[:：]",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
    }

    private static bool IsSourceLine(string line)
    {
        return Regex.IsMatch(
            (line ?? string.Empty).Trim(),
            @"^(?:\*\*)?\s*출처\s*[:：](?:\*\*)?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    private static bool IsTitleLine(string line, Func<string, bool>? isStandaloneNumberedHeadlineLine)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return IsStandaloneNumberedHeadlineLine(trimmed, isStandaloneNumberedHeadlineLine)
            || Regex.IsMatch(
                trimmed,
                @"^(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?제목\s*[:：]",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
    }

    private static bool IsCategoryLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LooksLikeStandaloneTimeLine(trimmed))
        {
            return false;
        }

        var match = Regex.Match(
            trimmed,
            @"^(?:[-•▪]\s*)?(?<label>[A-Za-z가-힣0-9()'‘’,.&+\- ]{1,24})\s*[:：]\s*(?<value>.+)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        var label = match.Groups["label"].Value.Trim();
        if (label.Length == 0)
        {
            return false;
        }

        if (label.Equals("제목", StringComparison.OrdinalIgnoreCase)
            || label.Equals("내용", StringComparison.OrdinalIgnoreCase)
            || label.Equals("출처", StringComparison.OrdinalIgnoreCase)
            || label.Equals("출처링크", StringComparison.OrdinalIgnoreCase)
            || label.Equals("http", StringComparison.OrdinalIgnoreCase)
            || label.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeStandaloneTimeLine(string line)
    {
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(
            normalized,
            @"^(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?(?:(?:\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}(?:[-/.]\d{2,4})?)\s+)?\d{1,2}\s*:\s*\d{2}(?:\s*:\s*\d{2})?(?:\s*(?:AM|PM|am|pm))?$",
            RegexOptions.CultureInvariant
        );
    }

    private static string? FindNextNonEmptyLine(string[] lines, int startIndex)
    {
        for (var i = startIndex; i < lines.Length; i += 1)
        {
            var candidate = (lines[i] ?? string.Empty).Trim();
            if (candidate.Length > 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryParseShortNumberedToken(string line, out string token)
    {
        return TryParseShortNumberedToken(line, out token, out _);
    }

    private static bool TryParseShortNumberedToken(string line, out string token, out int number)
    {
        token = string.Empty;
        number = 0;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(
            normalized,
            @"^(?<n>\d{1,2})\.\s+(?<token>[^\s]{1,12})$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success
            || !int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        token = match.Groups["token"].Value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        if (token.Contains("출처", StringComparison.OrdinalIgnoreCase)
            || token.Contains("제목", StringComparison.OrdinalIgnoreCase)
            || token.Contains("내용", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (token.Contains(':', StringComparison.Ordinal)
            || token.Contains('/', StringComparison.Ordinal)
            || token.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool TrySplitAsHeadlineList(string line, out IReadOnlyList<string> splitLines)
    {
        splitLines = Array.Empty<string>();
        var raw = (line ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (Regex.IsMatch(raw, @"^\d+[.)]\s+"))
        {
            return false;
        }

        if (raw.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (raw.Contains(" / ", StringComparison.Ordinal)
            || raw.Contains("|", StringComparison.Ordinal)
            || raw.Contains("출처:", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("제목:", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("내용:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaCount = raw.Count(ch => ch == ',');
        if (raw.Length < 90 || commaCount < 2)
        {
            return false;
        }

        var candidate = raw;
        candidate = Regex.Replace(
            candidate,
            @"([.…]{1,3})(?=(?:[""'“‘(\[])?[가-힣A-Z])",
            "$1\n",
            RegexOptions.CultureInvariant
        );
        candidate = Regex.Replace(
            candidate,
            @"([.…]{1,3})\s+(?=(?:[""'“‘(\[])?[가-힣A-Z])",
            "$1\n",
            RegexOptions.CultureInvariant
        );
        candidate = Regex.Replace(candidate, @"\s+(?=[^,\n]{6,36},)", "\n");
        candidate = Regex.Replace(candidate, @"\n{2,}", "\n");

        var items = candidate
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (items.Count < 3 || items.Count > 8)
        {
            return false;
        }

        var shortOrSingleWordCount = items.Count(item =>
            item.Length < 8 || !item.Contains(' ', StringComparison.Ordinal));
        if (shortOrSingleWordCount > (items.Count / 3))
        {
            return false;
        }

        var output = new List<string>(items.Count);
        for (var i = 0; i < items.Count; i += 1)
        {
            output.Add($"- {items[i]}");
        }

        splitLines = output;
        return true;
    }

    private static IEnumerable<string> WrapLongLine(string line, int maxWidth)
    {
        var value = (line ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var safeWidth = Math.Max(10, maxWidth);
        while (value.Length > safeWidth)
        {
            var cut = value.LastIndexOf(' ', safeWidth);
            if (cut < Math.Max(8, safeWidth / 2))
            {
                var nextSpace = value.IndexOf(' ', safeWidth);
                if (nextSpace > safeWidth && nextSpace <= safeWidth + 12)
                {
                    cut = nextSpace;
                }
                else
                {
                    cut = safeWidth;
                }
            }

            var head = value[..cut].Trim();
            if (!string.IsNullOrWhiteSpace(head))
            {
                yield return head;
            }

            value = value[cut..].TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            yield return value;
        }
    }

    private static bool IsMarkdownTableRow(string? line, Func<string?, bool>? matcher)
    {
        if (matcher != null)
        {
            return matcher(line);
        }

        var trimmed = (line ?? string.Empty).Trim();
        return trimmed.Length >= 3
            && trimmed.StartsWith("|", StringComparison.Ordinal)
            && trimmed.EndsWith("|", StringComparison.Ordinal)
            && trimmed.Count(ch => ch == '|') >= 2;
    }

    private static bool IsStandaloneNumberedHeadlineLine(string line, Func<string, bool>? matcher)
    {
        if (matcher != null)
        {
            return matcher(line);
        }

        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(
            trimmed,
            @"^(?:[-•▪]\s*)?(?:\*\*)?\d+[.)]\s*\S.{6,160}(?:\*\*)?$",
            RegexOptions.CultureInvariant
        );
    }
}
