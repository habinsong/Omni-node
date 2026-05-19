using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal static class SearchUrlContextPolicy
{
    private static readonly Regex HttpUrlRegex = new(
        "https?://[^\\s<>()\\\"'`]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex HtmlBreakTagRegex = new(
        @"<br\s*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled
    );

    public static bool TryParseGitHubRepositoryRoot(string url, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
        if (!ContainsAny(host, "github.com"))
        {
            return false;
        }

        var segments = (uri.AbsolutePath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        owner = segments[0];
        repo = segments[1];
        return owner.Length > 0 && repo.Length > 0;
    }

    public static string TryExtractHtmlMetaContent(string html, string metaName)
    {
        var targetName = (metaName ?? string.Empty).Trim();
        if (targetName.Length == 0 || string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var patterns = new[]
        {
            $"<meta\\s+name=\"{Regex.Escape(targetName)}\"\\s+content=\"(?<content>[^\"]*)\"",
            $"<meta\\s+content=\"(?<content>[^\"]*)\"\\s+name=\"{Regex.Escape(targetName)}\"",
            $"<meta\\s+property=\"og:{Regex.Escape(targetName)}\"\\s+content=\"(?<content>[^\"]*)\"",
            $"<meta\\s+content=\"(?<content>[^\"]*)\"\\s+property=\"og:{Regex.Escape(targetName)}\""
        };
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            return WebUtility.HtmlDecode(match.Groups["content"].Value).Trim();
        }

        return string.Empty;
    }

    public static (string RefName, string ReadmePath, string FallbackText) TryExtractGitHubReadmeInfo(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var embeddedMatch = Regex.Match(
            html,
            "<script type=\"application/json\" data-target=\"react-app\\.embeddedData\">(?<json>[\\s\\S]*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!embeddedMatch.Success)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(embeddedMatch.Groups["json"].Value);
            if (!doc.RootElement.TryGetProperty("payload", out var payload))
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            JsonElement overview;
            if (payload.TryGetProperty("codeViewRepoRoute", out var codeViewRepoRoute)
                && codeViewRepoRoute.ValueKind == JsonValueKind.Object
                && codeViewRepoRoute.TryGetProperty("overview", out overview)
                && overview.ValueKind == JsonValueKind.Object)
            {
            }
            else if (payload.TryGetProperty("overview", out overview) && overview.ValueKind == JsonValueKind.Object)
            {
            }
            else
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            if (!overview.TryGetProperty("overviewFiles", out var overviewFiles) || overviewFiles.ValueKind != JsonValueKind.Array)
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            foreach (var item in overviewFiles.EnumerateArray())
            {
                var preferredType = item.TryGetProperty("preferredFileType", out var preferredFileTypeElement)
                    ? (preferredFileTypeElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                if (!string.Equals(preferredType, "readme", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var refName = item.TryGetProperty("refName", out var refNameElement)
                    ? (refNameElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                var readmePath = item.TryGetProperty("path", out var pathElement)
                    ? (pathElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                var richText = item.TryGetProperty("richText", out var richTextElement)
                    ? (richTextElement.GetString() ?? string.Empty)
                    : string.Empty;
                var fallbackText = ConvertGitHubRichTextToPlainText(richText);
                return (refName, readmePath, fallbackText);
            }
        }
        catch
        {
        }

        return (string.Empty, string.Empty, string.Empty);
    }

    public static string ConvertGitHubRichTextToPlainText(string richText)
    {
        var normalized = (richText ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"<li\b[^>]*>", "- ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"</(p|div|li|h[1-6]|tr|pre|code|table|ul|ol|blockquote)>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = HtmlBreakTagRegex.Replace(normalized, "\n");
        normalized = HtmlTagRegex.Replace(normalized, " ");
        normalized = WebUtility.HtmlDecode(normalized);
        normalized = normalized.Replace("\r", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[ \t]+\n", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
        return normalized.Trim();
    }

    public static string BuildRelevantRepositoryExcerpt(string input, string readmeText)
    {
        var normalizedText = (readmeText ?? string.Empty).Trim();
        if (normalizedText.Length == 0)
        {
            return string.Empty;
        }

        var lines = SplitRepositoryLines(normalizedText);
        var queryTokens = ExtractRepositoryQueryTokens(input);
        if (queryTokens.Length == 0 || lines.Length == 0)
        {
            return TrimForRepositoryContext(normalizedText, 9000);
        }

        var matchIndexes = FindRepositoryMatchIndexes(lines, queryTokens);
        if (matchIndexes.Count == 0)
        {
            return TrimForRepositoryContext(normalizedText, 9000);
        }

        var builder = new StringBuilder();
        foreach (var index in matchIndexes.OrderBy(value => value))
        {
            var line = lines[index];
            if (builder.Length + line.Length + 1 > 9000)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        var excerpt = builder.ToString().Trim();
        return excerpt.Length == 0 ? TrimForRepositoryContext(normalizedText, 9000) : excerpt;
    }

    public static string TryBuildRepositoryExtractiveAnswer(string input, string description, string readmeText)
    {
        var lines = SplitRepositoryLines(readmeText);
        var queryTokens = ExtractRepositoryQueryTokens(input);
        if (lines.Length == 0 || queryTokens.Length == 0)
        {
            return string.Empty;
        }

        var matchIndexes = FindExactRepositoryMatchIndexes(lines, queryTokens);
        if (matchIndexes.Count == 0)
        {
            return string.Empty;
        }

        var matchedLines = matchIndexes
            .OrderBy(value => value)
            .Select(value => NormalizeRepositoryLineForDisplay(lines[value]))
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        if (matchedLines.Length == 0)
        {
            return string.Empty;
        }

        var topicLabel = BuildRepositoryTopicLabel(queryTokens);
        var builder = new StringBuilder();
        builder.AppendLine($"요약: README에서 {topicLabel} 관련으로 직접 확인되는 내용만 정리합니다.");
        builder.AppendLine();
        builder.AppendLine("확인된 내용:");
        foreach (var line in matchedLines)
        {
            builder.AppendLine($"- {line}");
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine();
            builder.AppendLine("중요 포인트:");
            builder.AppendLine("- 위 항목은 저장소 README/설명에서 직접 확인된 문장만 추린 것이며, 그 밖의 해석은 덧붙이지 않았습니다.");
        }

        builder.AppendLine();
        builder.AppendLine("출처: github.com");
        return builder.ToString().Trim();
    }

    public static string ResolveImplicitUrlRequest(string input, IReadOnlyList<string> urls)
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        if (!LooksLikeImplicitUrlSummaryRequest(normalizedInput, urls))
        {
            return normalizedInput;
        }

        if (urls.Count > 1)
        {
            return "이 URL들의 내용을 요약하고 공통점과 차이를 정리해줘.";
        }

        var primaryUrl = urls.FirstOrDefault() ?? string.Empty;
        if (LooksLikeRepositoryUrl(primaryUrl))
        {
            return "이 코드 저장소/프로젝트가 무엇인지 설명해줘.";
        }

        if (LooksLikeDocumentationUrl(primaryUrl))
        {
            return "이 문서/가이드 내용을 핵심만 요약해줘.";
        }

        if (LooksLikeArticleUrl(primaryUrl))
        {
            return "이 글/기사 내용을 핵심만 요약해줘.";
        }

        if (LooksLikeSiteRootUrl(primaryUrl))
        {
            return "이 사이트/서비스가 무엇인지 설명해줘.";
        }

        return "이 URL 내용을 설명해줘.";
    }

    public static bool LooksLikeImplicitUrlSummaryRequest(string input, IReadOnlyList<string> urls)
    {
        if (urls.Count == 0)
        {
            return false;
        }

        var normalizedInput = (input ?? string.Empty).Trim();
        if (normalizedInput.Length == 0)
        {
            return true;
        }

        var withoutUrls = HttpUrlRegex.Replace(normalizedInput, " ");
        withoutUrls = Regex.Replace(withoutUrls, @"[^\p{L}\p{Nd}\s]+", " ");
        withoutUrls = Regex.Replace(withoutUrls, @"\s+", " ").Trim().ToLowerInvariant();
        if (withoutUrls.Length == 0)
        {
            return true;
        }

        var tokens = withoutUrls
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        foreach (var token in tokens)
        {
            if (!IsUrlFillerToken(token))
            {
                return false;
            }
        }

        return true;
    }

    public static bool LooksLikeSiteOverviewRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "이 사이트",
            "사이트 내용",
            "사이트 설명",
            "사이트 알려",
            "서비스 설명",
            "회사 소개",
            "기업 소개",
            "무슨 사이트",
            "무슨 서비스"
        );
    }

    public static bool LooksLikeSiteRootUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = (uri.AbsolutePath ?? string.Empty).Trim();
        return path.Length == 0 || path == "/";
    }

    public static bool LooksLikeDocumentationUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).ToLowerInvariant();
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        return ContainsAny(host, "developers", "docs", "api")
               || ContainsAny(path, "/docs", "/doc", "/api", "/guide", "/reference", "/tutorial");
    }

    public static bool LooksLikeArticleUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).ToLowerInvariant();
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        return ContainsAny(host, "news", "blog", "medium", "substack")
               || ContainsAny(path, "/article", "/news", "/blog", "/posts", "/post");
    }

    public static bool LooksLikeRepositoryUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = (uri.Host ?? string.Empty).ToLowerInvariant();
        if (!ContainsAny(host, "github.com", "gitlab.com", "bitbucket.org"))
        {
            return false;
        }

        var segments = (uri.AbsolutePath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var reserved = segments.Length >= 3 ? segments[2].ToLowerInvariant() : string.Empty;
        if (reserved.Length == 0)
        {
            return true;
        }

        return reserved is not (
            "issues" or "pull" or "pulls" or "discussions" or "wiki" or "releases"
            or "actions" or "blob" or "tree" or "commit" or "commits" or "raw"
        );
    }

    private static string[] ExtractRepositoryQueryTokens(string input)
    {
        var normalized = HttpUrlRegex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), " ");
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{Nd}\s]+", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var rawTokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !IsRepositoryQueryFillerToken(token))
            .ToArray();
        if (rawTokens.Length == 0)
        {
            return Array.Empty<string>();
        }

        var rankedTokens = new List<string>(rawTokens.Length * 2);
        for (var index = 0; index < rawTokens.Length - 1; index += 1)
        {
            var current = rawTokens[index];
            var next = rawTokens[index + 1];
            if (current.Length == 0 || next.Length == 0)
            {
                continue;
            }

            rankedTokens.Add($"{current} {next}");
        }

        foreach (var token in rawTokens)
        {
            if (token.Length >= 2 || token.Any(char.IsDigit))
            {
                rankedTokens.Add(token);
            }
        }

        return rankedTokens
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitRepositoryLines(string text)
    {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static HashSet<int> FindRepositoryMatchIndexes(IReadOnlyList<string> lines, IReadOnlyList<string> queryTokens)
    {
        var matchIndexes = new HashSet<int>();
        for (var index = 0; index < lines.Count; index += 1)
        {
            var normalizedLine = lines[index].ToLowerInvariant();
            var score = 0;
            foreach (var token in queryTokens)
            {
                if (normalizedLine.Contains(token, StringComparison.Ordinal))
                {
                    score += 1;
                }
            }

            if (score <= 0)
            {
                continue;
            }

            matchIndexes.Add(index);
            if (index > 0)
            {
                matchIndexes.Add(index - 1);
            }

            if (index + 1 < lines.Count)
            {
                matchIndexes.Add(index + 1);
            }
        }

        return matchIndexes;
    }

    private static HashSet<int> FindExactRepositoryMatchIndexes(IReadOnlyList<string> lines, IReadOnlyList<string> queryTokens)
    {
        var matchIndexes = new HashSet<int>();
        for (var index = 0; index < lines.Count; index += 1)
        {
            var normalizedLine = lines[index].ToLowerInvariant();
            foreach (var token in queryTokens)
            {
                if (!normalizedLine.Contains(token, StringComparison.Ordinal))
                {
                    continue;
                }

                matchIndexes.Add(index);
                break;
            }
        }

        return matchIndexes;
    }

    private static string BuildRepositoryTopicLabel(IReadOnlyList<string> queryTokens)
    {
        if (queryTokens.Count == 0)
        {
            return "요청한 주제";
        }

        var labels = new List<string>(3);
        foreach (var token in queryTokens)
        {
            var normalizedToken = (token ?? string.Empty).Trim();
            if (normalizedToken.Length == 0)
            {
                continue;
            }

            var overlapsExisting = labels.Any(existing =>
                existing.Contains(normalizedToken, StringComparison.Ordinal)
                || normalizedToken.Contains(existing, StringComparison.Ordinal));
            if (overlapsExisting)
            {
                continue;
            }

            labels.Add(normalizedToken);
            if (labels.Count >= 3)
            {
                break;
            }
        }

        return labels.Count == 0 ? "요청한 주제" : string.Join(" ", labels);
    }

    private static bool IsRepositoryQueryFillerToken(string token)
    {
        return token is
            "이" or "이거" or "이것" or "여기" or "해당" or "링크" or "url" or "주소"
            or "링크에서" or "에서" or "관련" or "내용" or "내용만" or "부분" or "부분만" or "항목" or "얘기" or "말" or "말만" or "말해" or "말해봐" or "말해줘"
            or "설명" or "설명만" or "설명해" or "설명해줘" or "요약" or "요약만" or "요약해" or "요약해줘" or "정리" or "정리해줘"
            or "만" or "만좀" or "만요" or "좀" or "한번" or "봐" or "봐봐" or "봐줘" or "보지말고"
            or "찾아" or "찾아줘" or "추려" or "추려줘" or "뽑아" or "뽑아줘"
            or "what" or "about" or "only" or "from" or "tell" or "show" or "readme";
    }

    private static string TrimForRepositoryContext(string text, int maxChars)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars] + "\n...(truncated)";
    }

    private static string NormalizeRepositoryLineForDisplay(string line)
    {
        var normalized = (line ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Contains("![", StringComparison.Ordinal) || normalized.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, @"!\[[^\]]*\]\([^)]+\)", string.Empty);
        normalized = Regex.Replace(normalized, @"\[(?<text>[^\]]+)\]\([^)]+\)", "${text}");
        normalized = Regex.Replace(normalized, @"^\s{0,3}(?:>+\s*|[-*+]\s+|#+\s+)", string.Empty);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ").Trim();
        return normalized;
    }

    private static bool IsUrlFillerToken(string token)
    {
        return token is
            "이" or "이거" or "이곳" or "여기" or "해당"
            or "링크" or "url" or "주소"
            or "사이트" or "페이지" or "문서" or "글" or "기사" or "저장소" or "리포" or "repo" or "프로젝트"
            or "설명" or "요약" or "정리" or "내용" or "소개" or "해석" or "확인"
            or "알려" or "알려줘" or "봐" or "봐줘" or "읽어" or "읽어줘" or "보여" or "보여줘"
            or "좀" or "한번" or "무슨" or "무엇" or "뭐야" or "뭔지";
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern)
                && text.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
