using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class GeneratedCodeCandidatePolicy
{
    private static readonly Regex CodeFenceRegex = new("```([a-zA-Z0-9#+._-]*)\\s*\\n(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex JsonObjectRegex = new("\\{[\\s\\S]*\\}", RegexOptions.Compiled);

    public static string BuildCodeGenerationPrompt(string input, string languageHint, string modeLabel)
    {
        return $"""
                너는 로컬 실행 가능한 코드를 생성하는 엔지니어다.
                모드: {modeLabel}
                언어 힌트: {languageHint}

                아래 형식을 반드시 지켜라:
                1) 첫 줄에 LANGUAGE=<실행언어> (예: python, javascript, c, cpp, csharp, java, kotlin, html, css, bash)
                2) 다음에 단 하나의 코드블록만 출력
                3) 코드블록 안에는 순수 코드만 포함 (설명 금지)

                요청:
                {input}
                """;
    }

    public static ParsedCode ParseCodeCandidate(string text, string languageHint)
    {
        var raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedCode(CodingLanguagePolicy.NormalizeLanguageForCode(languageHint), string.Empty);
        }

        var firstLine = raw.Split('\n').FirstOrDefault() ?? string.Empty;
        var explicitLanguage = string.Empty;
        if (firstLine.StartsWith("LANGUAGE=", StringComparison.OrdinalIgnoreCase))
        {
            explicitLanguage = firstLine["LANGUAGE=".Length..].Trim();
        }

        var detectedLanguage = CodingLanguagePolicy.NormalizeLanguageForCode(string.IsNullOrWhiteSpace(explicitLanguage) ? languageHint : explicitLanguage);
        var match = CodeFenceRegex.Match(raw);
        if (match.Success)
        {
            var fenceLanguage = match.Groups[1].Value.Trim();
            var fenceCode = match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(fenceLanguage))
            {
                detectedLanguage = CodingLanguagePolicy.NormalizeLanguageForCode(fenceLanguage);
            }

            return new ParsedCode(detectedLanguage, fenceCode.Trim());
        }

        var jsonMatch = JsonObjectRegex.Match(raw);
        if (jsonMatch.Success)
        {
            var jsonCode = jsonMatch.Value.Trim();
            return new ParsedCode(detectedLanguage, jsonCode);
        }

        var cleaned = raw
            .Replace("LANGUAGE=", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return new ParsedCode(detectedLanguage, cleaned);
    }
}
