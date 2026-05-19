using System.Net;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class CodingLanguagePolicy
{
    public static string NormalizeLanguageForCode(string? language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "py" or "python3" => "python",
            "js" or "node" => "javascript",
            "ts" or "tsx" => "typescript",
            "react" or "vite" or "react-vite" or "react_vite" => "react-vite",
            "golang" => "go",
            "rs" or "cargo" => "rust",
            "rb" => "ruby",
            "sh" or "shell" => "bash",
            "c++" or "cc" => "cpp",
            "cs" or "c#" => "csharp",
            "kt" => "kotlin",
            "htm" => "html",
            "" or "auto" => "auto",
            _ => value
        };
    }

    public static string NormalizeCodingLanguageHintPreservingAuto(string? languageHint)
    {
        var raw = (languageHint ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raw) || raw == "auto")
        {
            return "auto";
        }

        return NormalizeLanguageForCode(raw);
    }

    public static string ResolveExplicitObjectiveLanguage(string? objective)
    {
        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (ContainsAny(text, "파이썬", "python"))
        {
            return "python";
        }

        if (ContainsAny(text, "react", "vite", "리액트"))
        {
            return "react-vite";
        }

        if (ContainsAny(text, "typescript", "타입스크립트", "tsx"))
        {
            return "typescript";
        }

        if (ContainsAny(text, "자바스크립트", "javascript", "node.js", "nodejs"))
        {
            return "javascript";
        }

        if (ContainsAny(text, "c#", "csharp", "dotnet", "asp.net"))
        {
            return "csharp";
        }

        if (ContainsAny(text, "c++", "cpp"))
        {
            return "cpp";
        }

        if (ContainsAny(text, "html"))
        {
            return "html";
        }

        if (ContainsAny(text, "css"))
        {
            return "css";
        }

        if (ContainsAny(text, "코틀린", "kotlin", "안드로이드"))
        {
            return "kotlin";
        }

        if (ContainsAny(text, "golang", " go ", "go언어", "go 언어"))
        {
            return "go";
        }

        if (ContainsAny(text, "rust", "러스트", "cargo"))
        {
            return "rust";
        }

        if (ContainsAny(text, "php", "laravel"))
        {
            return "php";
        }

        if (ContainsAny(text, "ruby", "rails"))
        {
            return "ruby";
        }

        if (ContainsAny(text, "swift", "스위프트"))
        {
            return "swift";
        }

        if (Regex.IsMatch(text, @"(?<![a-z])java(?!script)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(text, @"자바(?!스크립트)", RegexOptions.CultureInvariant)
            || ContainsAny(text, "spring"))
        {
            return "java";
        }

        if (ContainsAny(text, "c언어", "gcc", "clang"))
        {
            return "c";
        }

        if (ContainsAny(text, "bash", "shell", "쉘"))
        {
            return "bash";
        }

        return string.Empty;
    }

    public static string ResolveInitialCodingLanguage(string? languageHint, string objective)
    {
        var rawHint = (languageHint ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(rawHint) && rawHint != "auto")
        {
            return NormalizeLanguageForCode(rawHint);
        }

        var explicitLanguage = ResolveExplicitObjectiveLanguage(objective);
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return explicitLanguage;
        }

        var text = ExtractLatestCodingRequestText(WebUtility.HtmlDecode(objective ?? string.Empty)).ToLowerInvariant();
        if (ContainsAny(text, "react", "vite", "리액트"))
        {
            return "react-vite";
        }

        if (ContainsAny(text, "typescript", "타입스크립트", "tsx"))
        {
            return "typescript";
        }

        if (ContainsAny(text, "html", "css", "javascript", "js", "ui", "웹", "frontend", "vue", "next", "클론"))
        {
            return "html";
        }

        if (ContainsAny(text, "c#", "dotnet", "asp.net"))
        {
            return "csharp";
        }

        if (ContainsAny(text, "kotlin", "안드로이드"))
        {
            return "kotlin";
        }

        if (ContainsAny(text, "golang", " go ", "go언어", "go 언어"))
        {
            return "go";
        }

        if (ContainsAny(text, "rust", "러스트", "cargo"))
        {
            return "rust";
        }

        if (ContainsAny(text, "php", "laravel"))
        {
            return "php";
        }

        if (ContainsAny(text, "ruby", "rails"))
        {
            return "ruby";
        }

        if (ContainsAny(text, "swift", "스위프트"))
        {
            return "swift";
        }

        if (ContainsAny(text, "java", "spring"))
        {
            return "java";
        }

        if (ContainsAny(text, "c++", "cpp"))
        {
            return "cpp";
        }

        if (ContainsAny(text, " c ", " gcc ", "clang", "c언어"))
        {
            return "c";
        }

        if (ContainsAny(text, "bash", "shell", "스크립트"))
        {
            return "bash";
        }

        return "auto";
    }

    public static string GuessLanguageFromPath(string path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return fallback;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".py" => "python",
            ".js" or ".mjs" => "javascript",
            ".jsx" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".c" => "c",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".cs" => "csharp",
            ".java" => "java",
            ".kt" or ".kts" => "kotlin",
            ".go" => "go",
            ".rs" => "rust",
            ".php" => "php",
            ".rb" => "ruby",
            ".swift" => "swift",
            ".html" or ".htm" => "html",
            ".vue" or ".svelte" => "html",
            ".css" => "css",
            ".sh" => "bash",
            _ => fallback
        };
    }

    public static string ResolveFinalResultLanguage(
        string currentLanguage,
        string languageHint,
        string objective,
        IReadOnlyCollection<string> changedFiles
    )
    {
        var normalizedCurrent = NormalizeLanguageForCode(currentLanguage);
        var normalizedInitial = ResolveInitialCodingLanguage(languageHint, objective);
        if (normalizedInitial == "html")
        {
            return "html";
        }

        if (normalizedCurrent is "javascript" or "css")
        {
            var hasHtmlFile = (changedFiles ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Any(path =>
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    return extension is ".html" or ".htm";
                });
            if (hasHtmlFile)
            {
                return "html";
            }
        }

        return normalizedCurrent;
    }

    public static string ExtractLatestCodingRequestText(string objective)
    {
        var text = (objective ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lastNewRequestMarker = text.LastIndexOf("[새 요청]", StringComparison.Ordinal);
        if (lastNewRequestMarker >= 0)
        {
            var requestText = text[(lastNewRequestMarker + "[새 요청]".Length)..].Trim();
            if (string.IsNullOrWhiteSpace(requestText))
            {
                return string.Empty;
            }

            var lines = requestText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None);
            var collected = new List<string>(lines.Length);
            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();
                if (collected.Count > 0
                    && trimmed.Length > 0
                    && Regex.IsMatch(trimmed, @"^\[[^\]\r\n]{1,80}\]$", RegexOptions.CultureInvariant))
                {
                    break;
                }

                collected.Add(line);
            }

            return string.Join('\n', collected).Trim();
        }

        return text;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
