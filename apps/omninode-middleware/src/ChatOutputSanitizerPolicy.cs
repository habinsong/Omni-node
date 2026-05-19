using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class ChatOutputSanitizerPolicy
{
    private static readonly Regex RepeatedChunkRegex = new(@"(.{12,120}?)(?:\s+\1){2,}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CopilotFetchParagraphRegex = new(
        @"●?\s*<p>\s*Fetching the Copilot CLI documentation[\s\S]*?</p>\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex CopilotFetchSentenceRegex = new(
        @"Fetching the Copilot CLI documentation[\s\S]*?parallel\.\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex HtmlBreakTagRegex = new(
        @"<\s*(?:br|/p|/div|/li)\s*/?\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled
    );
    private static readonly Regex LeadingBulletSymbolRegex = new(
        @"^\s*[●•▪◦■□▶➤❖]+\s*",
        RegexOptions.Compiled
    );
    private static readonly Regex CopilotMetaLineRegex = new(
        @"(?i)(fetch_copilot_cli_documentation|fetching the copilot cli documentation|문서 조회 및 진행 상태 보고|활성 모델 확인을 위해 copilot cli 문서를 조회|i'?ll call the docs fetch|현재 작업:\s*fetch_copilot_cli_documentation)",
        RegexOptions.Compiled
    );
    private static readonly Regex ThinkTagBlockRegex = new(
        @"<think\b[^>]*>[\s\S]*?</think>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex ThinkTagInlineRegex = new(
        @"</?think\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly Regex MarkdownTableSeparatorCandidateRegex = new(
        @"^\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]+\s*(\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]+\s*)+\|$",
        RegexOptions.Compiled
    );
    private static readonly Regex MarkdownLooseSeparatorCellRegex = new(
        @"^:?-+:?$",
        RegexOptions.Compiled
    );

    public static string Sanitize(string text, bool keepMarkdownTables = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "응답이 비어 있습니다. 다시 질문해 주세요.";
        }

        var normalized = WebUtility.HtmlDecode(text ?? string.Empty);
        normalized = normalized.Replace('\u00A0', ' ');
        normalized = normalized.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Trim();
        normalized = ThinkTagBlockRegex.Replace(normalized, string.Empty);
        normalized = RemoveCopilotMetaPreamble(normalized);
        var lines = normalized.Split('\n');
        var compact = new List<string>(lines.Length);
        string? previous = null;
        var repeatCount = 0;
        var inThinkBlock = false;
        var inCodeFence = false;
        var hadBlankLine = false;
        foreach (var raw in lines)
        {
            var rawLine = raw ?? string.Empty;
            if (rawLine.Contains("<think", StringComparison.OrdinalIgnoreCase))
            {
                inThinkBlock = true;
            }

            if (inThinkBlock)
            {
                if (rawLine.Contains("</think>", StringComparison.OrdinalIgnoreCase))
                {
                    inThinkBlock = false;
                }

                continue;
            }

            var line = ThinkTagInlineRegex.Replace(rawLine, string.Empty);
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                if (compact.Count > 0 && !string.IsNullOrWhiteSpace(compact[^1]) && hadBlankLine)
                {
                    compact.Add(string.Empty);
                }

                compact.Add(trimmedLine);
                previous = null;
                repeatCount = 0;
                hadBlankLine = false;
                continue;
            }

            if (!inCodeFence)
            {
                line = NormalizeDisplayLine(line);
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                hadBlankLine = true;
                continue;
            }

            if (ShouldDropMetaLine(line))
            {
                continue;
            }

            if (hadBlankLine && compact.Count > 0 && !string.IsNullOrWhiteSpace(compact[^1]))
            {
                compact.Add(string.Empty);
            }
            hadBlankLine = false;

            if (previous != null && line.Equals(previous, StringComparison.Ordinal))
            {
                repeatCount += 1;
                if (repeatCount >= 2)
                {
                    continue;
                }
            }
            else
            {
                previous = line;
                repeatCount = 0;
            }

            compact.Add(line);
        }

        var merged = string.Join('\n', compact);
        if (merged.Length == 0)
        {
            merged = normalized;
        }

        merged = NormalizeMarkdownTableSeparators(merged);
        merged = CollapseMarkdownTableBlankLines(merged);
        merged = UnwrapMarkdownTableCodeFences(merged);
        if (!keepMarkdownTables)
        {
            merged = ConvertMarkdownTableRowsToList(merged);
        }
        var markdownLike = IsLikelyMarkdownText(merged);
        if (!markdownLike)
        {
            merged = ImprovePlainTextLineBreaksForChat(merged);
            merged = RepeatedChunkRegex.Replace(merged, "$1 ...");
            merged = CollapseRepeatedCharacters(merged);
        }
        else
        {
            merged = Regex.Replace(merged, @"\n{3,}", "\n\n");
        }

        merged = NormalizeSourceBlockToSingleLine(merged);
        merged = NormalizeStructuredLabelBlocks(merged);
        merged = RemoveDanglingMarkdownBoldMarkers(merged);
        return merged;
    }

    public static string RemoveCodeBlocksFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "```[\\s\\S]*?```", "[코드 블록 숨김]");
        normalized = Regex.Replace(normalized, @"(?is)\[code\][\s\S]*?(?=\n\[[^\n]+\]|$)", "[code] (숨김)\n");
        normalized = Regex.Replace(normalized, @"(?is)<code[^>]*>[\s\S]*?</code>", "[코드 숨김]");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string RemoveDanglingMarkdownBoldMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("```", StringComparison.Ordinal)
                || IsMarkdownTableRow(trimmed))
            {
                continue;
            }

            if (Regex.Matches(line, Regex.Escape("**"), RegexOptions.CultureInvariant).Count % 2 != 0)
            {
                line = line.Replace("**", string.Empty, StringComparison.Ordinal);
            }

            if (Regex.Matches(line, Regex.Escape("__"), RegexOptions.CultureInvariant).Count % 2 != 0)
            {
                line = line.Replace("__", string.Empty, StringComparison.Ordinal);
            }

            lines[i] = line;
        }

        return string.Join('\n', lines).Trim();
    }

    private static string NormalizeSourceBlockToSingleLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);

        for (var index = 0; index < lines.Length; index += 1)
        {
            var current = lines[index] ?? string.Empty;
            var currentTrimmed = current.Trim();
            if (!Regex.IsMatch(currentTrimmed, @"^(출처|sources?)\s*:?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                output.Add(current);
                continue;
            }

            var sourceNames = new List<string>(8);
            var cursor = index + 1;
            while (cursor < lines.Length)
            {
                var nextTrimmed = (lines[cursor] ?? string.Empty).Trim();
                if (nextTrimmed.Length == 0)
                {
                    // 출처 블록 내 공백 줄은 허용하되, 다음 비어있지 않은 줄이 출처 후보가 아닐 때 종료한다.
                    var lookahead = cursor + 1;
                    while (lookahead < lines.Length && string.IsNullOrWhiteSpace(lines[lookahead]))
                    {
                        lookahead += 1;
                    }

                    if (lookahead >= lines.Length || !TryExtractSourceNameCandidateLine(lines[lookahead], out _))
                    {
                        break;
                    }

                    cursor += 1;
                    continue;
                }

                if (!TryExtractSourceNameCandidateLine(lines[cursor], out var sourceName))
                {
                    break;
                }

                sourceNames.Add(sourceName);
                cursor += 1;
            }

            if (sourceNames.Count == 0)
            {
                output.Add(currentTrimmed);
                continue;
            }

            var distinctNames = FilterVisibleDisplaySources(sourceNames)
                .ToArray();
            if (distinctNames.Length == 0)
            {
                index = cursor - 1;
                continue;
            }

            output.Add($"출처: {string.Join(", ", distinctNames)}");
            index = cursor - 1;
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    public static string NormalizeStructuredLabelBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|…)\s*(?=(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?[A-Za-z가-힣0-9(][A-Za-z가-힣0-9().&+_/\-·\s]{1,80}[:：](?:\s|$))",
            "\n\n",
            RegexOptions.CultureInvariant
        );
        normalized = Regex.Replace(
            normalized,
            @"(?im)^(?<head>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?출처\s*링크\s*[:：]\s*[^\n]*?)\s+(?<url>https?://\S+)\s*$",
            "${head}\n${url}",
            RegexOptions.CultureInvariant
        );

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 8);
        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal) || IsMarkdownTableRow(line))
            {
                output.Add(line);
                continue;
            }

            if (TryFormatStandaloneNumberedHeadlineLine(line, out var formattedHeadline))
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(formattedHeadline);
                continue;
            }

            if (TryNormalizeExistingStructuredMarkdownLabelLine(line, out var normalizedExistingLabel))
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(normalizedExistingLabel);
                continue;
            }

            if (TryFormatStructuredMarkdownLabelLine(line, out var formatted))
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(formatted);
                continue;
            }

            output.Add(line);
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static bool TryFormatStandaloneNumberedHeadlineLine(string line, out string formatted)
    {
        formatted = string.Empty;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Contains("**", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryExtractStandaloneNumberedHeadlineParts(
                normalized,
                allowWrappedBold: false,
                out var lead,
                out var body,
                out _))
        {
            return false;
        }

        formatted = lead.Length == 0
            ? $"**{body}**"
            : $"{lead}**{body}**";
        return true;
    }

    public static bool IsStandaloneNumberedHeadlineLine(string line)
    {
        return TryExtractStandaloneNumberedHeadlineParts(
            line,
            allowWrappedBold: true,
            out _,
            out _,
            out _);
    }

    private static bool TryExtractStandaloneNumberedHeadlineParts(
        string line,
        bool allowWrappedBold,
        out string lead,
        out string body,
        out string headline)
    {
        lead = string.Empty;
        body = string.Empty;
        headline = string.Empty;

        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0
            || normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("[", StringComparison.Ordinal)
            || normalized.StartsWith("```", StringComparison.Ordinal)
            || IsMarkdownTableRow(normalized))
        {
            return false;
        }

        var pattern = allowWrappedBold
            ? @"^(?<lead>(?:[-•▪]\s*)?)(?:\*\*(?<bodyBold>\d+[.)]\s*[^\n]+)\*\*|(?<bodyPlain>\d+[.)]\s*[^\n]+))$"
            : @"^(?<lead>(?:[-•▪]\s*)?)(?<bodyPlain>\d+[.)]\s*[^\n]+)$";
        var match = Regex.Match(normalized, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        lead = match.Groups["lead"].Value;
        body = match.Groups["bodyBold"].Success
            ? match.Groups["bodyBold"].Value.Trim()
            : match.Groups["bodyPlain"].Value.Trim();
        if (body.Length == 0)
        {
            return false;
        }

        var headlineMatch = Regex.Match(body, @"^\d+[.)]\s*(?<headline>.+)$", RegexOptions.CultureInvariant);
        if (!headlineMatch.Success)
        {
            return false;
        }

        headline = Regex.Replace(headlineMatch.Groups["headline"].Value, @"\s{2,}", " ").Trim();
        return LooksLikeStandaloneNumberedHeadlineText(headline);
    }

    private static bool LooksLikeStandaloneNumberedHeadlineText(string headline)
    {
        var normalized = (headline ?? string.Empty).Trim();
        if (normalized.Length < 2 || normalized.Length > 140)
        {
            return false;
        }

        if (normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Contains('：', StringComparison.Ordinal)
            || normalized.Contains('|', StringComparison.Ordinal)
            || normalized.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("출처", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("요약", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("핵심", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.EndsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith("?", StringComparison.Ordinal)
            || normalized.EndsWith("!", StringComparison.Ordinal)
            || normalized.EndsWith("다.", StringComparison.Ordinal)
            || normalized.EndsWith("요.", StringComparison.Ordinal)
            || normalized.EndsWith("니다.", StringComparison.Ordinal)
            || normalized.EndsWith("습니다.", StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(normalized, @"[A-Za-z가-힣0-9]", RegexOptions.CultureInvariant);
    }

    private static bool TryNormalizeExistingStructuredMarkdownLabelLine(string line, out string formatted)
    {
        formatted = string.Empty;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(
            normalized,
            @"^(?<lead>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?)\*\*(?<label>[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}?)\s*[:：]\*\*\s*(?<value>.*)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        var lead = match.Groups["lead"].Value;
        var label = Regex.Replace(match.Groups["label"].Value, @"\s{2,}", " ").Trim();
        if (!LooksLikeStructuredLabel(label))
        {
            return false;
        }

        var value = NormalizeStructuredLabelValueText(match.Groups["value"].Value);
        formatted = value.Length == 0
            ? $"{lead}**{label}:**"
            : $"{lead}**{label}:** {value}";
        return true;
    }

    private static bool TryFormatStructuredMarkdownLabelLine(string line, out string formatted)
    {
        formatted = string.Empty;
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0
            || normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(
                normalized,
                @"^(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?\*\*.+?:\*\*(?:\s|$)",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        var match = Regex.Match(
            normalized,
            @"^(?<lead>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?)(?<label>[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}?)\s*[:：]\s*(?<value>.*)$",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        var lead = match.Groups["lead"].Value;
        var label = Regex.Replace(match.Groups["label"].Value, @"\s{2,}", " ").Trim();
        var value = NormalizeStructuredLabelValueText(match.Groups["value"].Value);
        if (!LooksLikeStructuredLabel(label))
        {
            return false;
        }

        formatted = value.Length == 0
            ? $"{lead}**{label}:**"
            : $"{lead}**{label}:** {value}";
        return true;
    }

    private static string NormalizeStructuredLabelValueText(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"^\*\*\s+", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s+\*\*$", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"^\*\*$", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim();
        return normalized;
    }

    private static bool LooksLikeStructuredLabel(string label)
    {
        var normalized = (label ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > 80)
        {
            return false;
        }

        if (normalized.Contains("://", StringComparison.Ordinal)
            || normalized.Contains('[', StringComparison.Ordinal)
            || normalized.Contains(']', StringComparison.Ordinal)
            || normalized.Contains('{', StringComparison.Ordinal)
            || normalized.Contains('}', StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(normalized, @"^(?:오전|오후)\s*\d{1,2}$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return Regex.IsMatch(normalized, @"[A-Za-z가-힣0-9]", RegexOptions.CultureInvariant);
    }

    private static bool TryExtractSourceNameCandidateLine(string line, out string sourceName)
    {
        sourceName = string.Empty;
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        trimmed = Regex.Replace(trimmed, @"^[-*•]\s+", string.Empty);
        trimmed = Regex.Replace(trimmed, @"^\d+[.)]\s+", string.Empty);
        trimmed = trimmed.Trim();
        if (!IsSourceNameCandidateLine(trimmed))
        {
            return false;
        }

        sourceName = trimmed;
        return true;
    }

    private static bool IsSourceNameCandidateLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Length > 60)
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, @"^(No\.\d+|\d+[.)])\s+", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (trimmed.StartsWith("제목", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("내용", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("출처", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("요약", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeMarkdownTableSeparators(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var changed = false;

        for (var i = 0; i < lines.Length; i += 1)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.IndexOf('|') < 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            var candidate = trimmed;
            if (!candidate.StartsWith("|", StringComparison.Ordinal))
            {
                candidate = "|" + candidate;
            }

            if (!candidate.EndsWith("|", StringComparison.Ordinal))
            {
                candidate += "|";
            }

            if (!MarkdownTableSeparatorCandidateRegex.IsMatch(candidate))
            {
                continue;
            }

            var cellsRaw = candidate.Trim('|').Split('|', StringSplitOptions.TrimEntries);
            if (cellsRaw.Length < 2)
            {
                continue;
            }

            var cells = new List<string>(cellsRaw.Length);
            var valid = true;
            foreach (var cellRaw in cellsRaw)
            {
                var compact = Regex.Replace(cellRaw, @"\s+", string.Empty);
                compact = NormalizeDashVariants(compact);
                if (!MarkdownLooseSeparatorCellRegex.IsMatch(compact))
                {
                    valid = false;
                    break;
                }

                var leadingColon = compact.StartsWith(':');
                var trailingColon = compact.EndsWith(':');
                var dashCount = compact.Count(ch => ch == '-');
                dashCount = Math.Max(3, dashCount);

                var cell = string.Concat(
                    leadingColon ? ":" : string.Empty,
                    new string('-', dashCount),
                    trailingColon ? ":" : string.Empty
                );
                cells.Add(cell);
            }

            if (!valid)
            {
                continue;
            }

            var leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
            var rebuilt = leadingWhitespace + "| " + string.Join(" | ", cells) + " |";
            if (!line.Equals(rebuilt, StringComparison.Ordinal))
            {
                lines[i] = rebuilt;
                changed = true;
            }
        }

        return changed ? string.Join('\n', lines) : normalized;
    }

    private static string CollapseMarkdownTableBlankLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length < 3)
        {
            return normalized;
        }

        var compact = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i += 1)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                var previous = compact.Count > 0 ? compact[^1] : string.Empty;
                var next = FindNextNonEmptyLine(lines, i + 1);
                if (IsMarkdownTableRow(previous) && IsMarkdownTableRow(next))
                {
                    continue;
                }
            }

            compact.Add(line);
        }

        return string.Join('\n', compact);
    }

    private static string UnwrapMarkdownTableCodeFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(
            text,
            @"```(?:markdown|md|table)?\s*\n(?<body>[\s\S]*?)```",
            match =>
            {
                var body = match.Groups["body"].Value
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)
                    .Trim();
                if (body.Length == 0)
                {
                    return match.Value;
                }

                var lines = body
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
                if (lines.Length < 2)
                {
                    return match.Value;
                }

                for (var i = 0; i + 1 < lines.Length; i += 1)
                {
                    if (!IsMarkdownTableRow(lines[i]))
                    {
                        continue;
                    }

                    if (!MarkdownTableSeparatorCandidateRegex.IsMatch(lines[i + 1].Trim()))
                    {
                        continue;
                    }

                    return body;
                }

                return match.Value;
            },
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    private static string ConvertMarkdownTableRowsToList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var converted = new List<string>(lines.Length);
        var inCodeFence = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                converted.Add(line);
                continue;
            }

            if (inCodeFence)
            {
                converted.Add(line);
                continue;
            }

            if (!trimmed.StartsWith("|", StringComparison.Ordinal)
                || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                converted.Add(line);
                continue;
            }

            if (MarkdownTableSeparatorCandidateRegex.IsMatch(trimmed))
            {
                continue;
            }

            var cells = trimmed
                .Trim('|')
                .Split('|', StringSplitOptions.TrimEntries)
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToArray();
            if (cells.Length < 2)
            {
                converted.Add(line);
                continue;
            }

            converted.Add("- " + string.Join(" / ", cells));
        }

        var merged = string.Join('\n', converted).Trim();
        return Regex.Replace(merged, @"\n{3,}", "\n\n");
    }

    private static string FindNextNonEmptyLine(string[] lines, int startIndex)
    {
        for (var i = Math.Max(0, startIndex); i < lines.Length; i += 1)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return lines[i];
            }
        }

        return string.Empty;
    }

    public static bool IsMarkdownTableRow(string? line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        if (!trimmed.StartsWith("|", StringComparison.Ordinal)
            || !trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            return false;
        }

        return trimmed.Count(ch => ch == '|') >= 2;
    }

    private static string NormalizeDashVariants(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\u2014' or '\u2013' or '\u2011' or '\u2212' or '\u2500' or '\u2012')
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string RemoveCopilotMetaPreamble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = WebUtility.HtmlDecode(text ?? string.Empty);
        var hasWrapperTags = cleaned.Contains("<p", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("</p>", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("<pre", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("</pre>", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("<code", StringComparison.OrdinalIgnoreCase)
            || cleaned.Contains("</code>", StringComparison.OrdinalIgnoreCase);
        if (hasWrapperTags)
        {
            cleaned = HtmlBreakTagRegex.Replace(cleaned, "\n");
            cleaned = HtmlTagRegex.Replace(cleaned, string.Empty);
        }

        cleaned = CopilotFetchParagraphRegex.Replace(cleaned, string.Empty);
        cleaned = CopilotFetchSentenceRegex.Replace(cleaned, string.Empty);
        var lines = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDisplayLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !ShouldDropMetaLine(line))
            .ToArray();
        return lines.Length == 0 ? string.Empty : string.Join('\n', lines).Trim();
    }

    private static string NormalizeDisplayLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var line = raw.Trim();
        line = line.Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<pre>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</pre>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<code>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</code>", string.Empty, StringComparison.OrdinalIgnoreCase);
        line = WebUtility.HtmlDecode(line);
        line = LeadingBulletSymbolRegex.Replace(line, string.Empty);
        line = Regex.Replace(line, @"[ \t]{2,}", " ").Trim();
        return line;
    }

    private static bool IsLikelyMarkdownText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains("```", StringComparison.Ordinal)
            || text.Contains("| ---", StringComparison.Ordinal)
            || text.Contains("](", StringComparison.Ordinal)
            || Regex.IsMatch(text, @"\*\*.+?\*\*")
            || Regex.IsMatch(text, @"(^|\n)\s*(#{1,6}\s|[-*+]\s|\d+\.\s|>\s)", RegexOptions.Multiline))
        {
            return true;
        }

        return false;
    }

    private static string ImprovePlainTextLineBreaksForChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Trim();

        if (!normalized.Contains('\n'))
        {
            normalized = Regex.Replace(normalized, @"(?<!\n)(\d+[.)]\s+)", "\n$1");
            normalized = Regex.Replace(
                normalized,
                @"([.!?]|…|\.{3})\s+(?!(?:md|js|cs|ts|jsx|tsx|txt|py|json|html|css|xml|yml|yaml|sh|ps1|bat|cmd|log|csv|exe|dll|zip|tar|gz)\b)(?=[^\n])",
                "$1\n",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );
        }

        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized;
    }

    private static bool ShouldDropMetaLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var trimmed = line.Trim();
        if (trimmed.Equals("copy", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("복사", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("", StringComparison.Ordinal)
            || trimmed.Equals("", StringComparison.Ordinal))
        {
            return true;
        }

        if (CopilotMetaLineRegex.IsMatch(line))
        {
            return true;
        }

        if (line.StartsWith("현재 작업:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (line.StartsWith("[자동 전환 모델:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string CollapseRepeatedCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var prev = '\0';
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == prev)
            {
                count += 1;
            }
            else
            {
                prev = ch;
                count = 1;
            }

            if (count <= 8)
            {
                builder.Append(ch);
            }
            else if (count == 9)
            {
                builder.Append("...");
            }
        }

        return builder.ToString();
    }

    private static string CollapseRepeatedSentenceRuns(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var segments = Regex.Split(text, @"(?<=[\.\!\?]|다\.)\s+")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        if (segments.Length <= 1)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        string? prev = null;
        var repeats = 0;
        foreach (var segment in segments)
        {
            var normalized = Regex.Replace(segment, @"\s+", " ").Trim();
            if (prev != null && string.Equals(prev, normalized, StringComparison.OrdinalIgnoreCase))
            {
                repeats += 1;
                if (repeats >= 2)
                {
                    continue;
                }
            }
            else
            {
                prev = normalized;
                repeats = 0;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(normalized);
        }

        return builder.Length == 0 ? text : builder.ToString();
    }

    private static IReadOnlyList<string> FilterVisibleDisplaySources(IEnumerable<string> sources)
    {
        return sources
            .SelectMany(value =>
            {
                var normalized = NormalizeDisplaySourceCandidate(value);
                if (normalized.Length == 0)
                {
                    return Array.Empty<string>();
                }

                var split = normalized.Split(new[] { ',', '·', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return split.Length == 0 ? new[] { normalized } : split;
            })
            .Select(NormalizeDisplaySourceCandidate)
            .Where(value => value.Length > 0 && !ShouldHideDisplaySource(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeDisplaySourceCandidate(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"^\s*출처\s*링크\s*:\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Trim().Trim(',', ';', '|', '/');
        return normalized;
    }

    private static bool ShouldHideDisplaySource(string? value)
    {
        var normalized = NormalizeDisplaySourceCandidate(value);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            return IsHiddenDisplaySourceHost(absoluteUri.Host);
        }

        var hostCandidate = Regex.Replace(normalized, @"^https?://", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var cutIndex = hostCandidate.IndexOfAny(new[] { '/', '?', '#', ' ' });
        if (cutIndex >= 0)
        {
            hostCandidate = hostCandidate[..cutIndex];
        }

        return IsHiddenDisplaySourceHost(hostCandidate);
    }

    private static bool IsHiddenDisplaySourceHost(string? host)
    {
        var normalized = (host ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        return normalized.Equals("vietnam.vn", StringComparison.Ordinal)
            || normalized.EndsWith(".vietnam.vn", StringComparison.Ordinal);
    }

}
