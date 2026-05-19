using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class SearchAnswerFormatterPolicy
{
    private const string StructuredLabelPattern =
        @"[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}?";

    private static readonly Regex HttpUrlRegex = new(
        "https?://[^\\s<>()\\\"'`]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static string NormalizeNumberedListResponse(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|…)\s*(?=(?:No\.)?\d{1,2}\.\s*(?:\*\*|[A-Za-z가-힣0-9]))",
            "\n\n",
            RegexOptions.CultureInvariant
        );
        normalized = Regex.Replace(
            normalized,
            @"(?m)^(?<n>(?:No\.)?\d{1,2})\.(?=\S)",
            "${n}. ",
            RegexOptions.CultureInvariant
        );
        normalized = MergeDetachedNumberLinesForWeb(normalized);

        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (!LooksLikeNumberedListResponse(normalized))
        {
            return normalized;
        }

        var introParts = new List<string>(8);
        var itemBlocks = new List<string>(12);
        var trailingBlocks = new List<string>(4);
        var currentItem = new StringBuilder();
        var sawItem = false;
        var inTrailing = false;

        void FlushCurrentItem()
        {
            var body = NormalizeListItemBody(currentItem.ToString());
            if (body.Length > 0)
            {
                itemBlocks.Add(body);
            }

            currentItem.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (IsDisplaySourceLine(line))
            {
                FlushCurrentItem();
                inTrailing = true;
                trailingBlocks.Add(line);
                continue;
            }

            if (inTrailing)
            {
                trailingBlocks.Add(line);
                continue;
            }

            if (TryParseLeadingNumber(line, out _))
            {
                FlushCurrentItem();
                currentItem.Append(StripLeadingNumber(line));
                sawItem = true;
                continue;
            }

            if (!sawItem)
            {
                introParts.Add(line);
                continue;
            }

            if (currentItem.Length > 0)
            {
                currentItem.Append(' ');
            }

            currentItem.Append(line);
        }

        FlushCurrentItem();
        if (itemBlocks.Count < 2)
        {
            return normalized;
        }

        var output = new List<string>(itemBlocks.Count + trailingBlocks.Count + 2);
        var introBlock = NormalizeListItemBody(string.Join(" ", introParts));
        if (!string.IsNullOrWhiteSpace(introBlock))
        {
            output.Add(introBlock);
        }

        for (var index = 0; index < itemBlocks.Count; index++)
        {
            var body = NormalizeListItemBody(itemBlocks[index]);
            if (body.Length == 0)
            {
                continue;
            }

            output.Add($"{index + 1}. {body}");
        }

        foreach (var trailing in trailingBlocks)
        {
            output.Add(trailing.Trim());
        }

        return string.Join("\n\n", output.Where(block => !string.IsNullOrWhiteSpace(block))).Trim();
    }

    public static bool LooksLikeNumberedListResponse(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|…)\s*(?=(?:No\.)?\d{1,2}\.\s*(?:\*\*|[A-Za-z가-힣0-9]))",
            "\n",
            RegexOptions.CultureInvariant
        );
        var matches = Regex.Matches(
            normalized,
            @"(?m)^\s*(?:No\.)?\d{1,2}\.\s*(?:\*\*|[A-Za-z가-힣0-9])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        return matches.Count >= 2;
    }

    public static string NormalizeCollapsedBulletRuns(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(
            normalized,
            @"(?<=[.!?]|다\.)\s*-\s+(?=[^\n])",
            "\n- ",
            RegexOptions.CultureInvariant
        );
        return Regex.Replace(normalized, @"\n{3,}", "\n\n");
    }

    public static string RemoveSourceLinkArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length);
        var skipImmediateUrl = false;

        foreach (var raw in lines)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                skipImmediateUrl = false;
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^\s*출처\s*링크\s*[:：]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                skipImmediateUrl = true;
                continue;
            }

            var isRawUrlLine = HttpUrlRegex.IsMatch(trimmed)
                && HttpUrlRegex.Match(trimmed).Value.Equals(trimmed, StringComparison.Ordinal);
            if (skipImmediateUrl && isRawUrlLine)
            {
                skipImmediateUrl = false;
                continue;
            }

            skipImmediateUrl = false;

            if (TryExtractDisplaySourceLineValue(trimmed, out var sourceLineValue))
            {
                var cleanedSource = Regex.Replace(sourceLineValue, @"https?://\S+", string.Empty);
                cleanedSource = cleanedSource.Replace("**", string.Empty, StringComparison.Ordinal);
                cleanedSource = Regex.Replace(cleanedSource, @"\s{2,}", " ").Trim().Trim(',', ';', '|', '/');
                cleanedSource = FilterVisibleDisplaySourceLine(cleanedSource);
                if (cleanedSource.Length == 0)
                {
                    continue;
                }

                output.Add($"출처: {cleanedSource}");
                continue;
            }

            if (isRawUrlLine
                && output.Count > 0
                && IsDisplaySourceLine(output[^1]))
            {
                continue;
            }

            output.Add(trimmed);
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    public static string NormalizeNarrativeParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 4);
        var paragraphParts = new List<string>(8);

        void FlushParagraph()
        {
            if (paragraphParts.Count == 0)
            {
                return;
            }

            var merged = string.Join(" ", paragraphParts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));
            merged = Regex.Replace(merged, @"\s{2,}", " ").Trim();
            merged = Regex.Replace(merged, @"\s+([,.;:!?])", "$1");
            if (merged.Length > 0)
            {
                output.Add(merged);
            }

            paragraphParts.Clear();
        }

        foreach (var raw in lines)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                continue;
            }

            if (IsNarrativeWebAnswerLine(trimmed))
            {
                paragraphParts.Add(trimmed);
                continue;
            }

            FlushParagraph();
            if (TryNormalizeStructuredLabelLine(trimmed, out var normalizedStructuredLine))
            {
                if (output.Count > 0
                    && !string.IsNullOrWhiteSpace(output[^1]))
                {
                    output.Add(string.Empty);
                }

                output.Add(normalizedStructuredLine);
                continue;
            }

            output.Add(trimmed);
        }

        FlushParagraph();
        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    public static string EnsureMarkdownTableResponseIfRequested(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = ConvertDelimitedPlainTextTableToMarkdown(normalized);

        var hasTableHeader = Regex.IsMatch(normalized, @"(?m)^\s*\|.+\|\s*$", RegexOptions.CultureInvariant);
        var hasTableSeparator = Regex.IsMatch(
            normalized,
            @"(?m)^\s*\|\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|\s*$",
            RegexOptions.CultureInvariant
        );
        if (hasTableHeader && hasTableSeparator)
        {
            return NormalizeMarkdownTableResponseMetadata(normalized);
        }

        var sourceNames = new List<string>(8);
        var intro = new List<string>(4);
        var rows = new List<(string Key, string Value)>(8);
        var currentSection = string.Empty;
        var currentSectionItems = new List<string>(4);
        var metadataSection = string.Empty;

        static string NormalizeMetadataLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value, @"[*`_~\[\]\(\)]", string.Empty);
            normalized = Regex.Replace(normalized, @"\s+", string.Empty);
            return normalized.Trim().ToLowerInvariant();
        }

        static bool IsSourceMetadataLabel(string label)
        {
            var normalized = NormalizeMetadataLabel(label);
            return normalized.StartsWith("출처", StringComparison.Ordinal)
                || normalized.Equals("source", StringComparison.Ordinal)
                || normalized.Equals("sources", StringComparison.Ordinal);
        }

        static bool IsSummaryMetadataLabel(string label)
        {
            var normalized = NormalizeMetadataLabel(label);
            return normalized.StartsWith("요약", StringComparison.Ordinal)
                || normalized.Equals("summary", StringComparison.Ordinal);
        }

        static string StripListPrefix(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            normalized = Regex.Replace(normalized, @"^(?:[-•▪●◆▶▷]\s*)+", string.Empty);
            return normalized.Trim();
        }

        void AppendSourceNames(string rawValue)
        {
            var normalized = StripListPrefix(rawValue);
            if (normalized.Length == 0)
            {
                return;
            }

            normalized = Regex.Replace(normalized, @"^(?:\*\*)?\s*출처\s*(?:\*\*)?\s*[:：]\s*", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s{2,}", " ");
            if (normalized.Length == 0)
            {
                return;
            }

            foreach (var token in normalized.Split(new[] { ',', '·', '/', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = token.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (name.StartsWith("출처", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sourceNames.Add(name);
            }
        }

        void FlushSection()
        {
            if (string.IsNullOrWhiteSpace(currentSection))
            {
                currentSectionItems.Clear();
                return;
            }

            var value = currentSectionItems.Count == 0
                ? "-"
                : string.Join(" / ", currentSectionItems.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()));
            value = string.IsNullOrWhiteSpace(value) ? "-" : value;
            rows.Add((SanitizeTableCell(currentSection), SanitizeTableCell(value)));
            currentSection = string.Empty;
            currentSectionItems.Clear();
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        foreach (var raw in lines)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (metadataSection.Equals("source", StringComparison.Ordinal))
            {
                AppendSourceNames(line);
                continue;
            }

            if (metadataSection.Equals("summary", StringComparison.Ordinal))
            {
                var summaryValue = StripListPrefix(line);
                if (summaryValue.Length > 0)
                {
                    intro.Add(summaryValue);
                }
                continue;
            }

            var sectionMatch = Regex.Match(line, @"^(?:[■□▪●◆▶▷]\s*)+(?<section>.+)$", RegexOptions.CultureInvariant);
            if (sectionMatch.Success)
            {
                FlushSection();
                var sectionName = sectionMatch.Groups["section"].Value.Trim();
                if (IsSourceMetadataLabel(sectionName))
                {
                    metadataSection = "source";
                    continue;
                }

                if (IsSummaryMetadataLabel(sectionName))
                {
                    metadataSection = "summary";
                    continue;
                }

                metadataSection = string.Empty;
                currentSection = sectionName;
                continue;
            }

            var bulletMatch = Regex.Match(line, @"^(?:[-•·●]\s*)(?<body>.+)$", RegexOptions.CultureInvariant);
            if (bulletMatch.Success)
            {
                var body = bulletMatch.Groups["body"].Value.Trim();
                if (body.Length == 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentSection))
                {
                    currentSectionItems.Add(body);
                    continue;
                }

                var keyValueBullet = Regex.Match(body, @"^(?<k>[^:：]{1,40})\s*[:：]\s*(?<v>.+)$", RegexOptions.CultureInvariant);
                if (keyValueBullet.Success)
                {
                    var key = keyValueBullet.Groups["k"].Value.Trim();
                    var value = keyValueBullet.Groups["v"].Value.Trim();
                    if (IsSourceMetadataLabel(key))
                    {
                        AppendSourceNames(value);
                        continue;
                    }

                    if (IsSummaryMetadataLabel(key))
                    {
                        if (value.Length > 0)
                        {
                            intro.Add($"요약: {value}");
                        }

                        continue;
                    }

                    rows.Add((
                        SanitizeTableCell(key),
                        SanitizeTableCell(value)
                    ));
                }
                else
                {
                    rows.Add(("핵심", SanitizeTableCell(body)));
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentSection))
            {
                currentSectionItems.Add(line);
                continue;
            }

            var keyValueLine = Regex.Match(
                line,
                @"^(?:\*\*)?\s*(?<k>[^:：]{1,40})\s*(?:\*\*)?\s*[:：]\s*(?<v>.+)$",
                RegexOptions.CultureInvariant
            );
            if (keyValueLine.Success)
            {
                var key = keyValueLine.Groups["k"].Value.Trim();
                var value = keyValueLine.Groups["v"].Value.Trim();
                if (IsSourceMetadataLabel(key))
                {
                    AppendSourceNames(value);
                    continue;
                }

                if (IsSummaryMetadataLabel(key))
                {
                    if (value.Length > 0)
                    {
                        intro.Add($"요약: {value}");
                    }

                    continue;
                }
            }

            intro.Add(line);
        }

        FlushSection();

        if (rows.Count == 0)
        {
            return normalized;
        }

        var output = new List<string>(rows.Count + 6);
        if (intro.Count > 0)
        {
            output.AddRange(intro.Take(2));
            output.Add(string.Empty);
        }

        output.Add("| 구분 | 주요 내용 |");
        output.Add("| --- | --- |");
        foreach (var row in rows)
        {
            output.Add($"| {row.Key} | {row.Value} |");
        }

        var distinctSources = FilterVisibleDisplaySources(sourceNames)
            .ToList();
        if (distinctSources.Count > 0)
        {
            output.Add(string.Empty);
            output.Add($"출처: {string.Join(", ", distinctSources)}");
        }

        return string.Join('\n', output).Trim();
    }

    public static string ConvertDelimitedPlainTextTableToMarkdown(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var output = new List<string>(lines.Length + 4);
        var index = 0;
        while (index < lines.Length)
        {
            if (!TrySplitDelimitedTableRow(lines[index], out var headerCells, out var delimiterKind))
            {
                output.Add(lines[index]);
                index += 1;
                continue;
            }

            var rows = new List<string[]>(8);
            var cursor = index + 1;
            while (cursor < lines.Length
                && TrySplitDelimitedTableRow(lines[cursor], out var rowCells, out var rowDelimiterKind)
                && rowDelimiterKind == delimiterKind
                && rowCells.Length == headerCells.Length)
            {
                rows.Add(rowCells);
                cursor += 1;
            }

            if (rows.Count == 0)
            {
                output.Add(lines[index]);
                index += 1;
                continue;
            }

            output.Add($"| {string.Join(" | ", headerCells.Select(SanitizeTableCell))} |");
            output.Add($"| {string.Join(" | ", Enumerable.Repeat("---", headerCells.Length))} |");
            foreach (var row in rows)
            {
                output.Add($"| {string.Join(" | ", row.Select(SanitizeTableCell))} |");
            }

            index = cursor;
        }

        return string.Join('\n', output).Trim();
    }

    public static string NormalizeMarkdownTableResponseMetadata(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        static string NormalizeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalizedValue = Regex.Replace(value, @"[*`_~\[\]\(\)]", string.Empty);
            normalizedValue = Regex.Replace(normalizedValue, @"\s+", string.Empty);
            return normalizedValue.Trim().ToLowerInvariant();
        }

        static bool IsSourceLabel(string value)
        {
            var normalizedValue = NormalizeLabel(value);
            return normalizedValue.StartsWith("출처", StringComparison.Ordinal)
                || normalizedValue.Equals("source", StringComparison.Ordinal)
                || normalizedValue.Equals("sources", StringComparison.Ordinal);
        }

        static bool StartsWithSourceMetadata(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            return Regex.IsMatch(
                trimmed,
                @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
        }

        static string[] ParseCells(string line)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^1];
            }

            return trimmed
                .Split('|', StringSplitOptions.None)
                .Select(cell => cell.Trim())
                .ToArray();
        }

        static string BuildRow(IReadOnlyList<string> cells)
        {
            return $"| {string.Join(" | ", cells.Select(cell => SanitizeTableCell(cell)))} |";
        }

        static bool IsTableRow(string line)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (trimmed.Length < 3)
            {
                return false;
            }

            if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                return false;
            }

            return trimmed.Count(ch => ch == '|') >= 2;
        }

        static bool IsTableSeparator(string line)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (!IsTableRow(trimmed))
            {
                return false;
            }

            var cells = ParseCells(trimmed);
            if (cells.Length == 0)
            {
                return false;
            }

            foreach (var cell in cells)
            {
                var compact = cell.Replace(" ", string.Empty, StringComparison.Ordinal);
                if (compact.Length < 3 || !Regex.IsMatch(compact, @"^:?-{3,}:?$", RegexOptions.CultureInvariant))
                {
                    return false;
                }
            }

            return true;
        }

        static void AppendSourceNames(List<string> bucket, string rawValue)
        {
            var normalizedValue = (rawValue ?? string.Empty).Trim();
            normalizedValue = Regex.Replace(
                normalizedValue,
                @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]\s*",
                string.Empty,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
            if (normalizedValue.Length == 0)
            {
                return;
            }

            var split = normalizedValue.Split(new[] { ',', '·', '/', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in split)
            {
                var name = token.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (name.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.StartsWith("출처", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bucket.Add(name);
            }
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var tableStart = -1;
        var tableEnd = -1;
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (!IsTableRow(lines[i]) || !IsTableSeparator(lines[i + 1]))
            {
                continue;
            }

            tableStart = i;
            var cursor = i + 2;
            while (cursor < lines.Length && IsTableRow(lines[cursor]))
            {
                cursor++;
            }

            tableEnd = cursor - 1;
            break;
        }

        if (tableStart < 0 || tableEnd < tableStart + 1)
        {
            return normalized;
        }

        var sourceNames = new List<string>(8);
        var beforeLines = new List<string>(Math.Max(0, tableStart));
        var afterLines = new List<string>(Math.Max(0, lines.Length - tableEnd - 1));

        for (var i = 0; i < tableStart; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length > 0
                && Regex.IsMatch(trimmed, @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                AppendSourceNames(sourceNames, trimmed);
                continue;
            }

            beforeLines.Add(lines[i]);
        }

        for (var i = tableEnd + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length > 0
                && Regex.IsMatch(trimmed, @"^(?:\*\*)?\s*(?:출처|sources?)\s*(?:\*\*)?\s*[:：]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                AppendSourceNames(sourceNames, trimmed);
                continue;
            }

            afterLines.Add(lines[i]);
        }

        var headerCells = ParseCells(lines[tableStart]);
        if (headerCells.Length == 0)
        {
            return normalized;
        }

        var sourceColumnIndexes = headerCells
            .Select((cell, index) => (cell, index))
            .Where(item => IsSourceLabel(item.cell))
            .Select(item => item.index)
            .ToHashSet();

        var keepColumnIndexes = Enumerable.Range(0, headerCells.Length)
            .Where(index => !sourceColumnIndexes.Contains(index))
            .ToArray();

        if (keepColumnIndexes.Length == 0)
        {
            return normalized;
        }

        var rebuiltDataRows = new List<string[]>(Math.Max(0, tableEnd - tableStart - 1));
        var movedFromTable = sourceColumnIndexes.Count > 0;
        for (var i = tableStart + 2; i <= tableEnd; i++)
        {
            var parsed = ParseCells(lines[i]);
            var rowCells = new string[headerCells.Length];
            for (var col = 0; col < headerCells.Length; col++)
            {
                rowCells[col] = col < parsed.Length ? parsed[col] : string.Empty;
            }

            var mergedRowText = string.Join(", ", rowCells.Where(value => !string.IsNullOrWhiteSpace(value)));
            var isSourceMetadataRow = rowCells.Any(StartsWithSourceMetadata)
                || (rowCells.Length > 0 && IsSourceLabel(rowCells[0]));
            if (isSourceMetadataRow)
            {
                AppendSourceNames(sourceNames, mergedRowText);
                movedFromTable = true;
                continue;
            }

            foreach (var sourceIndex in sourceColumnIndexes)
            {
                if (sourceIndex >= 0 && sourceIndex < rowCells.Length)
                {
                    AppendSourceNames(sourceNames, rowCells[sourceIndex]);
                }
            }

            var filtered = keepColumnIndexes.Select(index => rowCells[index]).ToArray();
            if (filtered.All(value => string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            rebuiltDataRows.Add(filtered);
        }

        if (!movedFromTable && sourceNames.Count == 0)
        {
            return normalized;
        }

        var rebuilt = new List<string>(lines.Length + 4);
        rebuilt.AddRange(beforeLines);

        var filteredHeader = keepColumnIndexes.Select(index => headerCells[index]).ToArray();
        rebuilt.Add(BuildRow(filteredHeader));
        rebuilt.Add(BuildRow(Enumerable.Repeat("---", filteredHeader.Length).ToArray()));
        foreach (var row in rebuiltDataRows)
        {
            rebuilt.Add(BuildRow(row));
        }

        rebuilt.AddRange(afterLines);

        var distinctSources = FilterVisibleDisplaySources(sourceNames)
            .ToList();

        if (distinctSources.Count > 0)
        {
            if (rebuilt.Count > 0 && !string.IsNullOrWhiteSpace(rebuilt[^1]))
            {
                rebuilt.Add(string.Empty);
            }

            rebuilt.Add($"출처: {string.Join(", ", distinctSources)}");
        }

        return string.Join('\n', rebuilt).Trim();
    }

    public static string SanitizeTableCell(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace("|", "/", StringComparison.Ordinal)
            .Trim();
        normalized = Regex.Replace(normalized, @"\s{2,}", " ");
        return normalized.Length == 0 ? "-" : normalized;
    }

    private static string MergeDetachedNumberLinesForWeb(string text)
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
            if (!Regex.IsMatch(current, @"^(?:No\.)?\d+\.$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
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
                || Regex.IsMatch(next, @"^(?:No\.)?\d+\.$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                || IsMarkdownTableRow(next))
            {
                output.Add(lines[i]);
                continue;
            }

            output.Add($"{current} {next}");
            i = nextIndex;
        }

        return Regex.Replace(string.Join('\n', output).Trim(), @"\n{3,}", "\n\n");
    }

    private static string NormalizeListItemBody(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = normalized.Replace("\t", " ", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s+([,.;:!?])", "$1");
        return normalized.Trim();
    }

    private static bool TryParseLeadingNumber(string value, out int number)
    {
        number = 0;
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(trimmed, @"^(?:No\.)?(?<n>\d{1,2})\.\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    private static string StripLeadingNumber(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        normalized = Regex.Replace(normalized, @"^(?:No\.)?\d{1,2}\.\s*", string.Empty, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return normalized.Trim();
    }

    private static bool IsMarkdownTableRow(string? line)
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

    private static bool IsDisplaySourceLine(string? line)
    {
        return TryExtractDisplaySourceLineValue(line, out _);
    }

    private static bool TryExtractDisplaySourceLineValue(string? line, out string value)
    {
        value = string.Empty;
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var match = Regex.Match(
            trimmed,
            @"^(?:\*\*)?\s*출처\s*[:：]\s*(?:\*\*)?\s*(?<value>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups["value"].Value.Trim();
        return value.Length > 0;
    }

    private static string FilterVisibleDisplaySourceLine(string? sourceLine)
    {
        var normalized = NormalizeDisplaySourceCandidate(sourceLine);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var candidates = normalized
            .Split(new[] { ',', '·', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (candidates.Length == 0)
        {
            return ShouldHideDisplaySource(normalized) ? string.Empty : normalized;
        }

        var visible = FilterVisibleDisplaySources(candidates);
        return visible.Count == 0 ? string.Empty : string.Join(", ", visible);
    }

    private static bool IsNarrativeWebAnswerLine(string line)
    {
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal)
            || IsMarkdownTableRow(trimmed)
            || trimmed.StartsWith("요약:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("핵심:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("**핵심:**", StringComparison.OrdinalIgnoreCase)
            || IsDisplaySourceLine(trimmed))
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, @"^(?:[-•▪*]\s+|\d+[.)]\s+)", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (Regex.IsMatch(
                trimmed,
                @"^(?:\*\*)?\s*[A-Za-z가-힣0-9(][A-Za-z가-힣0-9(),.&+_/\-·\s]{0,40}\s*(?:\*\*)?\s*[:：]",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        return !HttpUrlRegex.IsMatch(trimmed);
    }

    private static bool TryNormalizeStructuredLabelLine(string line, out string normalizedLine)
    {
        normalizedLine = string.Empty;
        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (TryNormalizeExistingStructuredMarkdownLabelLine(trimmed, out normalizedLine))
        {
            return true;
        }

        return TryFormatStructuredMarkdownLabelLine(trimmed, out normalizedLine);
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
            $@"^(?<lead>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?)\*\*(?<label>{StructuredLabelPattern})\s*[:：]\*\*\s*(?<value>.*)$",
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
            $@"^(?<lead>(?:[-•▪]\s*)?(?:(?:No\.\d+|\d+[.)])\s*)?)(?<label>{StructuredLabelPattern})\s*[:：]\s*(?<value>.*)$",
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

    private static bool TrySplitDelimitedTableRow(string line, out string[] cells, out string delimiterKind)
    {
        cells = Array.Empty<string>();
        delimiterKind = string.Empty;

        var trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0
            || trimmed.StartsWith("|", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, @"^(?:[-•▪*]\s+|\d+[.)]\s+)", RegexOptions.CultureInvariant)
            || Regex.IsMatch(trimmed, @"^(?:\*\*)?\s*[A-Za-z가-힣0-9(][A-Za-z가-힣0-9().&+_/\-·\s]{0,40}\s*(?:\*\*)?\s*[:：]", RegexOptions.CultureInvariant)
            || HttpUrlRegex.IsMatch(trimmed))
        {
            return false;
        }

        if (trimmed.Contains('\t'))
        {
            var tabCells = trimmed
                .Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(cell => cell.Trim())
                .Where(cell => cell.Length > 0)
                .ToArray();
            if (tabCells.Length >= 3)
            {
                cells = tabCells;
                delimiterKind = "tab";
                return true;
            }
        }

        var spaceCells = Regex.Split(trimmed, @"\s{2,}", RegexOptions.CultureInvariant)
            .Select(cell => cell.Trim())
            .Where(cell => cell.Length > 0)
            .ToArray();
        if (spaceCells.Length >= 3)
        {
            cells = spaceCells;
            delimiterKind = "space";
            return true;
        }

        return false;
    }

    public static bool ContainsHttpUrl(string? text)
    {
        return HttpUrlRegex.IsMatch(text ?? string.Empty);
    }

    public static string EnsureReadableWebAnswerResponse(string text, string input, bool allowMarkdownTable)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        if (allowMarkdownTable && SearchQueryPolicy.LooksLikeTableRenderRequest(input))
        {
            return EnsureMarkdownTableResponseIfRequested(normalized);
        }

        normalized = RemoveSourceLinkArtifacts(normalized);
        normalized = NormalizeCollapsedBulletRuns(normalized);
        if (SearchQueryPolicy.LooksLikeComparisonRequest(input))
        {
            return normalized;
        }

        if (SearchQueryPolicy.LooksLikeListOutputRequest(input) || LooksLikeNumberedListResponse(normalized))
        {
            return NormalizeNumberedListResponse(normalized);
        }

        return NormalizeNarrativeParagraphs(normalized);
    }
}
