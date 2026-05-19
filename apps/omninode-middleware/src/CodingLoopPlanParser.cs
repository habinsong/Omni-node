using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class CodingLoopPlanParser
{
    private static readonly Regex CodeFenceRegex = new("```([a-zA-Z0-9#+._-]*)\\s*\\n(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex OuterHtmlContainerRegex = new(@"^\s*<\s*(p|pre|code)\b[^>]*>([\s\S]*)</\s*\1\s*>\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex JsonTrailingCommaRegex = new(@",\s*([}\]])", RegexOptions.Compiled);

    public static CodingLoopPlan? Parse(string rawText)
    {
        var text = (rawText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var candidates = BuildJsonCandidates(text);
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(NormalizeJsonCandidate(candidate));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var analysis = GetStringProperty(doc.RootElement, "analysis");
                var finalMessage = GetStringProperty(doc.RootElement, "final_message");
                var done = GetBoolProperty(doc.RootElement, "done");
                var actions = new List<CodingLoopAction>();
                if (doc.RootElement.TryGetProperty("actions", out var actionsElement)
                    && actionsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var actionElement in actionsElement.EnumerateArray())
                    {
                        if (actionElement.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var type = GetStringProperty(actionElement, "type");
                        if (string.IsNullOrWhiteSpace(type))
                        {
                            type = GetStringProperty(actionElement, "op");
                        }

                        var path = GetStringProperty(actionElement, "path") ?? string.Empty;
                        var content = GetStringProperty(actionElement, "content") ?? string.Empty;
                        var command = GetStringProperty(actionElement, "command") ?? string.Empty;
                        actions.Add(new CodingLoopAction(
                            NormalizeActionType(type, path, content, command),
                            path,
                            content,
                            command
                        ));
                    }
                }

                return new CodingLoopPlan(analysis ?? string.Empty, finalMessage ?? string.Empty, done, actions);
            }
            catch
            {
            }
        }

        return null;
    }

    public static IReadOnlyList<string> BuildTextVariants(string text)
    {
        var list = new List<string>();
        AddVariant(list, text);

        var decoded = WebUtility.HtmlDecode(text);
        AddVariant(list, decoded);
        AddVariant(list, UnwrapHtmlContainer(decoded));

        var unwrapped = UnwrapHtmlContainer(text);
        AddVariant(list, unwrapped);
        AddVariant(list, WebUtility.HtmlDecode(unwrapped));

        return list.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string NormalizeJsonCandidate(string candidate)
    {
        var normalized = (candidate ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        normalized = WebUtility.HtmlDecode(normalized);
        normalized = UnwrapHtmlContainer(normalized);
        normalized = normalized
            .Replace('\u201c', '"')
            .Replace('\u201d', '"')
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'');
        normalized = EscapeJsonControlCharsInsideStrings(normalized);
        normalized = JsonTrailingCommaRegex.Replace(normalized, "$1");

        return normalized.Trim();
    }

    public static string NormalizeActionType(string? rawType, string? path, string? content, string? command)
    {
        var raw = (rawType ?? string.Empty).Trim().ToLowerInvariant();
        if (IsKnownActionType(raw))
        {
            return raw;
        }

        if (raw.Contains('|', StringComparison.Ordinal))
        {
            var split = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in split)
            {
                if (IsKnownActionType(token))
                {
                    return token;
                }
            }
        }

        if (raw.Contains("mkdir", StringComparison.Ordinal))
        {
            return "mkdir";
        }

        if (raw.Contains("append", StringComparison.Ordinal))
        {
            return "append_file";
        }

        if (raw.Contains("write", StringComparison.Ordinal) || raw.Contains("create", StringComparison.Ordinal))
        {
            return "write_file";
        }

        if (raw.Contains("read", StringComparison.Ordinal))
        {
            return "read_file";
        }

        if (raw.Contains("delete", StringComparison.Ordinal) || raw.Contains("remove", StringComparison.Ordinal))
        {
            return "delete_file";
        }

        if (raw.Contains("run", StringComparison.Ordinal) || raw.Contains("exec", StringComparison.Ordinal))
        {
            return "run";
        }

        var normalizedCommand = (command ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return "run";
        }

        var normalizedPath = (path ?? string.Empty).Trim();
        var normalizedContent = content ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            if (!string.IsNullOrWhiteSpace(normalizedContent))
            {
                return "write_file";
            }

            if (!Path.HasExtension(normalizedPath))
            {
                return "mkdir";
            }

            return "write_file";
        }

        return "run";
    }

    private static IReadOnlyList<string> BuildJsonCandidates(string text)
    {
        var candidates = new List<string>();
        foreach (var variant in BuildTextVariants(text))
        {
            if (variant.StartsWith("{", StringComparison.Ordinal))
            {
                candidates.Add(variant);
            }

            var firstBrace = variant.IndexOf('{');
            var lastBrace = variant.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                candidates.Add(variant[firstBrace..(lastBrace + 1)]);
            }

            var codeFence = CodeFenceRegex.Match(variant);
            if (codeFence.Success)
            {
                candidates.Add(codeFence.Groups[2].Value.Trim());
            }
        }

        return candidates;
    }

    private static void AddVariant(List<string> variants, string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            variants.Add(trimmed);
        }
    }

    private static string UnwrapHtmlContainer(string text)
    {
        var current = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(current))
        {
            return string.Empty;
        }

        current = current.TrimStart('●', '•', '-', '*', ' ');
        for (var i = 0; i < 3; i++)
        {
            var match = OuterHtmlContainerRegex.Match(current);
            if (!match.Success)
            {
                break;
            }

            current = match.Groups[2].Value.Trim();
        }

        return current;
    }

    private static string EscapeJsonControlCharsInsideStrings(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input ?? string.Empty;
        }

        var builder = new StringBuilder(input.Length + 64);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (!inString)
            {
                if (ch == '"')
                {
                    inString = true;
                }

                builder.Append(ch);
                continue;
            }

            if (escaped)
            {
                builder.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                builder.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                builder.Append(ch);
                inString = false;
                continue;
            }

            if (ch == '\r')
            {
                builder.Append("\\n");
                if (i + 1 < input.Length && input[i + 1] == '\n')
                {
                    i++;
                }
                continue;
            }

            if (ch == '\n')
            {
                builder.Append("\\n");
                continue;
            }

            if (ch == '\t')
            {
                builder.Append("\\t");
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool GetBoolProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
    }

    private static bool IsKnownActionType(string value)
    {
        return value == "mkdir"
               || value == "write_file"
               || value == "append_file"
               || value == "read_file"
               || value == "delete_file"
               || value == "run";
    }
}
