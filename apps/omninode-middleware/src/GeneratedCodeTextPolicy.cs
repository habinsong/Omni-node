using System.Net;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class GeneratedCodeTextPolicy
{
    private static readonly Regex CodeFenceRegex = new("```([a-zA-Z0-9#+._-]*)\\s*\\n(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline);

    public static string NormalizeGeneratedFileContent(string content)
    {
        var normalized = (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized.Trim();
        }

        var lines = normalized.Split('\n');
        var minIndent = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(GetLeadingIndentWidth)
            .DefaultIfEmpty(0)
            .Min();
        if (minIndent <= 0)
        {
            return normalized.Trim('\n');
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                lines[i] = string.Empty;
                continue;
            }

            var trimCount = Math.Min(minIndent, GetLeadingIndentWidth(lines[i]));
            lines[i] = lines[i][trimCount..];
        }

        return string.Join('\n', lines).Trim('\n');
    }

    public static ParsedCode ExtractLanguagePrefixedPlainCode(string text, string fallbackLanguage)
    {
        var normalized = WebUtility.HtmlDecode(text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        var fallback = CodingLanguagePolicy.NormalizeLanguageForCode(fallbackLanguage);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ParsedCode(fallback, string.Empty);
        }

        var lines = normalized.Split('\n');
        if (lines.Length == 0)
        {
            return new ParsedCode(fallback, string.Empty);
        }

        var firstLine = lines[0].Trim();
        if (!firstLine.StartsWith("LANGUAGE=", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCode(fallback, string.Empty);
        }

        var declaredLanguage = firstLine["LANGUAGE=".Length..].Trim();
        var resolvedLanguage = CodingLanguagePolicy.NormalizeLanguageForCode(
            string.IsNullOrWhiteSpace(declaredLanguage) ? fallbackLanguage : declaredLanguage
        );

        if (lines.Length <= 1)
        {
            return new ParsedCode(resolvedLanguage, string.Empty);
        }

        var code = string.Join('\n', lines.Skip(1))
            .Trim('\n', '\r')
            .TrimEnd();

        if (code.StartsWith("```", StringComparison.Ordinal))
        {
            var fence = CodeFenceRegex.Match(code);
            if (fence.Success)
            {
                var fencedCode = fence.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(fencedCode))
                {
                    return new ParsedCode(resolvedLanguage, fencedCode);
                }
            }
        }

        return new ParsedCode(resolvedLanguage, code);
    }

    private static int GetLeadingIndentWidth(string line)
    {
        var width = 0;
        while (width < line.Length && (line[width] == ' ' || line[width] == '\t'))
        {
            width++;
        }

        return width;
    }
}
