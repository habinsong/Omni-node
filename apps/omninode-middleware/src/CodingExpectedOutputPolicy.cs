using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class CodingExpectedOutputPolicy
{
    private static readonly Regex OrderedExpectedOutputLineRegex = new(
        @"(?<label>첫\s*줄|첫째\s*줄|1\s*번째\s*줄|1\s*줄|first\s+line|둘째\s*줄|두\s*번째\s*줄|2\s*번째\s*줄|2\s*줄|second\s+line)[^'""`\r\n]{0,64}['""`](?<value>[^'""`\r\n]{1,200})['""`]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static IReadOnlyList<string> ExtractExpectedConsoleOutputLines(string objective)
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(objective);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var lineOrdered = new SortedDictionary<int, string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var quoted = CodingFallbackPolicy.ExtractQuotedTextLiterals(line);
            if (quoted.Length == 0)
            {
                continue;
            }

            var lowered = line.ToLowerInvariant();
            if (!lineOrdered.ContainsKey(0) && ContainsAny(lowered, "첫 줄", "첫째 줄", "1번째 줄", "1 번째 줄", "first line"))
            {
                lineOrdered[0] = quoted[0];
            }

            if (!lineOrdered.ContainsKey(1) && ContainsAny(lowered, "둘째 줄", "두번째 줄", "두 번째 줄", "2번째 줄", "2 번째 줄", "second line"))
            {
                lineOrdered[1] = quoted[^1];
            }
        }

        if (lineOrdered.Count > 0)
        {
            return lineOrdered
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value)
                .ToArray();
        }

        var ordered = new SortedDictionary<int, string>();
        foreach (Match match in OrderedExpectedOutputLineRegex.Matches(text))
        {
            var index = ResolveExpectedOutputLineIndex(match.Groups["label"].Value);
            var value = match.Groups["value"].Value.Trim();
            if (index < 0 || string.IsNullOrWhiteSpace(value) || ordered.ContainsKey(index))
            {
                continue;
            }

            ordered[index] = value;
        }

        if (ordered.Count > 0)
        {
            return ordered
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value)
                .ToArray();
        }

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!ContainsAny(line.ToLowerInvariant(), "stdout", "표준 출력", "첫 줄", "둘째 줄", "first line", "second line"))
            {
                continue;
            }

            var quoted = CodingFallbackPolicy.ExtractQuotedTextLiterals(line);
            if (quoted.Length > 0)
            {
                return quoted;
            }
        }

        return Array.Empty<string>();
    }

    public static IReadOnlyList<string> ExtractVisibleTextRequirementLiterals(string objective)
    {
        var text = CodingLanguagePolicy.ExtractLatestCodingRequestText(objective);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!ContainsAny(line.ToLowerInvariant(), "보이는 텍스트", "visible text", "innertext"))
            {
                continue;
            }

            var quoted = CodingFallbackPolicy.ExtractQuotedTextLiterals(line);
            if (quoted.Length > 0)
            {
                return quoted;
            }
        }

        return Array.Empty<string>();
    }

    public static int ResolveExpectedOutputLineIndex(string label)
    {
        var normalized = (label ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "첫 줄" or "첫째 줄" or "1번째 줄" or "1 번째 줄" or "1 줄" or "first line" => 0,
            "둘째 줄" or "두번째 줄" or "두 번째 줄" or "2번째 줄" or "2 번째 줄" or "2 줄" or "second line" => 1,
            _ => -1
        };
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
