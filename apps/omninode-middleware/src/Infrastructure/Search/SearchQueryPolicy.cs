using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

internal sealed record SearchRequirementDecision(
    bool Required,
    string DecisionLabel,
    string SourceFocus,
    string SourceDomain
);

internal readonly record struct WebPreferenceHint(string Category, string Text);

internal static class SearchQueryPolicy
{
    private static readonly Regex RequestedCountRegex = new(
        @"(?<!\d)(?<n>[1-9]\d?)\s*(개|건|가지|뉴스|news|items?|results?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex TopCountRegex = new(
        @"(?:top|상위)\s*(?<n>[1-9]\d?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static SearchRequirementDecision BuildFastRequirementDecision(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return new SearchRequirementDecision(false, "llm:false:empty_input", string.Empty, string.Empty);
        }

        if (LooksLikeClearlyNonWebQuestion(normalized))
        {
            return new SearchRequirementDecision(false, "heuristic:false:non_web", string.Empty, string.Empty);
        }

        var heuristicNeedWeb = LooksLikeExplicitWebLookupQuestion(normalized) || LooksLikeRealtimeQuestion(normalized);
        return new SearchRequirementDecision(
            heuristicNeedWeb,
            heuristicNeedWeb
                ? (LooksLikeExplicitWebLookupQuestion(normalized) ? "fast:true:explicit_web" : "fast:true:heuristic")
                : "fast:false:heuristic",
            ExtractSourceFocusHintFromInput(normalized),
            ExtractSourceDomainHintFromInput(normalized)
        );
    }

    public static bool TryParseSearchRequirementDecisionJson(
        string? rawText,
        out bool needWeb,
        out string sourceFocus,
        out string sourceDomain
    )
    {
        needWeb = false;
        sourceFocus = string.Empty;
        sourceDomain = string.Empty;
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var json = text[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var needToken = string.Empty;
            if (TryGetPropertyIgnoreCase(root, "needWeb", out var needWebElement))
            {
                needToken = needWebElement.ValueKind switch
                {
                    JsonValueKind.True => "yes",
                    JsonValueKind.False => "no",
                    JsonValueKind.String => needWebElement.GetString() ?? string.Empty,
                    _ => string.Empty
                };
            }

            var normalizedNeed = NormalizeWebSearchDecisionToken(needToken);
            if (normalizedNeed == "yes")
            {
                needWeb = true;
            }
            else if (normalizedNeed == "no")
            {
                needWeb = false;
            }
            else
            {
                return false;
            }

            if (TryGetPropertyIgnoreCase(root, "sourceFocus", out var sourceFocusElement)
                && sourceFocusElement.ValueKind == JsonValueKind.String)
            {
                sourceFocus = (sourceFocusElement.GetString() ?? string.Empty).Trim();
            }

            if (TryGetPropertyIgnoreCase(root, "sourceDomain", out var sourceDomainElement)
                && sourceDomainElement.ValueKind == JsonValueKind.String)
            {
                sourceDomain = NormalizeSourceDomainHint(sourceDomainElement.GetString());
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ExtractSourceFocusHintFromInput(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var match = Regex.Match(
            normalized,
            @"(?<focus>[A-Za-z0-9가-힣][A-Za-z0-9가-힣\.\-]{1,40})\s*(?:의\s*)?(?:주요\s*)?뉴스",
            RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return string.Empty;
        }

        var focus = (match.Groups["focus"].Value ?? string.Empty).Trim();
        if (focus.Length < 2)
        {
            return string.Empty;
        }

        var loweredFocus = focus.ToLowerInvariant();
        if (loweredFocus is "오늘"
            or "어제"
            or "최근"
            or "최신"
            or "방금"
            or "실시간"
            or "주요"
            or "뉴스"
            or "헤드라인"
            or "속보"
            or "latest"
            or "recent"
            or "today"
            or "breaking"
            or "top")
        {
            return string.Empty;
        }

        return focus;
    }

    public static string ExtractSourceDomainHintFromInput(string input)
    {
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var explicitSite = Regex.Match(
            normalized,
            @"site\s*:\s*(?<domain>[A-Za-z0-9][A-Za-z0-9\.\-]*\.[A-Za-z]{2,})",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        return explicitSite.Success
            ? NormalizeSourceDomainHint(explicitSite.Groups["domain"].Value)
            : string.Empty;
    }

    public static string NormalizeSourceDomainHint(string? domain)
    {
        var normalized = (domain ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("http://", StringComparison.Ordinal))
        {
            normalized = normalized["http://".Length..];
        }
        else if (normalized.StartsWith("https://", StringComparison.Ordinal))
        {
            normalized = normalized["https://".Length..];
        }

        normalized = normalized.Trim('/');
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        return Regex.IsMatch(normalized, @"^[a-z0-9][a-z0-9\.-]*\.[a-z]{2,}$", RegexOptions.CultureInvariant)
            ? normalized
            : string.Empty;
    }

    public static string BuildEffectiveSearchQuery(
        string query,
        SearchRequirementDecision decision,
        Func<string, string, string> resolveSourceDomain
    )
    {
        var baseQuery = (query ?? string.Empty).Trim();
        if (baseQuery.Length == 0)
        {
            return baseQuery;
        }

        var sourceFocus = (decision.SourceFocus ?? string.Empty).Trim();
        if (sourceFocus.Length == 0)
        {
            if (LooksLikeListOutputRequest(baseQuery))
            {
                var lowered = baseQuery.ToLowerInvariant();
                if (!ContainsAny(lowered, "latest", "breaking", "headlines", "top stories")
                    && ContainsAny(lowered, "뉴스", "news", "헤드라인", "속보"))
                {
                    return $"{baseQuery} latest breaking headlines";
                }
            }

            return baseQuery;
        }

        var builder = new StringBuilder(baseQuery);
        if (!baseQuery.Contains(sourceFocus, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(' ').Append(sourceFocus);
        }

        var sourceDomain = NormalizeSourceDomainHint(decision.SourceDomain);
        if (sourceDomain.Length == 0)
        {
            sourceDomain = resolveSourceDomain(baseQuery, sourceFocus);
        }
        if (sourceDomain.Length > 0
            && !baseQuery.Contains(sourceDomain, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(' ').Append(sourceDomain);
        }

        if (LooksLikeListOutputRequest(baseQuery))
        {
            var lowered = baseQuery.ToLowerInvariant();
            if (!ContainsAny(lowered, "official", "공식", "homepage", "top headlines", "top stories"))
            {
                builder.Append(' ').Append(sourceFocus).Append(" official top headlines");
            }
        }

        return builder.ToString().Trim();
    }

    public static string NormalizeWebSearchDecisionToken(string? decisionText)
    {
        var normalized = (decisionText ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.StartsWith("YES", StringComparison.Ordinal)
            || normalized == "Y")
        {
            return "yes";
        }

        if (normalized.StartsWith("NO", StringComparison.Ordinal)
            || normalized == "N")
        {
            return "no";
        }

        var compact = Regex.Replace(normalized, @"[^A-Z가-힣]", string.Empty);
        if (compact.Contains("YES", StringComparison.Ordinal))
        {
            return "yes";
        }

        if (compact.Contains("NO", StringComparison.Ordinal))
        {
            return "no";
        }

        if (compact.Contains("필요", StringComparison.Ordinal) && !compact.Contains("불필요", StringComparison.Ordinal))
        {
            return "yes";
        }

        if (compact.Contains("불필요", StringComparison.Ordinal))
        {
            return "no";
        }

        return string.Empty;
    }

    public static bool LooksLikeRealtimeQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (LooksLikeLocalDateTimeQuestion(normalized))
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "최신",
            "최근",
            "오늘",
            "어제",
            "방금",
            "실시간",
            "지금",
            "뉴스",
            "속보",
            "업데이트",
            "변경점",
            "릴리즈",
            "출시",
            "현재",
            "latest",
            "recent",
            "today",
            "yesterday",
            "now",
            "news",
            "update",
            "release",
            "current"
        );
    }

    public static bool LooksLikeExplicitWebLookupQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (LooksLikeLocalDateTimeQuestion(normalized))
        {
            return false;
        }

        return ContainsAny(
                normalized,
                "검색해서",
                "검색해줘",
                "검색해 줘",
                "검색해봐",
                "웹 검색",
                "웹에서",
                "인터넷에서",
                "web search",
                "search for",
                "look up",
                "lookup"
            )
            || Regex.IsMatch(
                normalized,
                @"\b(?:search|lookup)\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
    }

    public static bool LooksLikeClearlyNonWebQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (LooksLikeLocalDateTimeQuestion(normalized)
            || LooksLikeConversationalFollowUp(normalized))
        {
            return true;
        }

        if (LooksLikeRealtimeQuestion(normalized)
            || LooksLikeExplicitWebLookupQuestion(normalized))
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "번역",
                "translate",
                "영작",
                "영문",
                "맞춤법",
                "교정",
                "다듬",
                "rewrite",
                "rephrase"))
        {
            return true;
        }

        if (ContainsAny(normalized, "코드", "code")
            && ContainsAny(normalized, "설명", "해석", "리뷰", "explain", "review"))
        {
            return true;
        }

        if (LooksLikeCasualOrIdentityQuestion(normalized))
        {
            return true;
        }

        if (ContainsAny(normalized, "요약", "summary", "summarize", "정리"))
        {
            return normalized.Contains('\n')
                || normalized.Contains("```", StringComparison.Ordinal)
                || normalized.Contains("다음", StringComparison.Ordinal)
                || normalized.Contains("\"", StringComparison.Ordinal);
        }

        return false;
    }

    public static bool LooksLikeCasualOrIdentityQuestion(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();

        if (LooksLikeStandaloneFreshGreeting(normalized))
        {
            return true;
        }

        if (normalized.Length <= 8)
        {
            if (ContainsAny(normalized, "응", "어", "아니", "네", "예", "맞아", "그래", "음", "헐", "대박", "진짜", "뭐해", "ok", "ㅇㅇ", "ㅋㅋ", "ㅎㅎ", "ㅠㅠ", "ㅜㅜ", "너는?", "나도"))
            {
                return true;
            }
        }

        return ContainsAny(
            normalized,
            "할 수 있",
            "할수 있",
            "할 줄 아",
            "할줄 아",
            "뭐할 수",
            "뭐 할수",
            "뭐 할 수",
            "너는 누구",
            "당신은 누구",
            "넌 누구",
            "너는 뭐",
            "넌 뭐",
            "자기소개",
            "안녕",
            "반가워",
            "반갑습니다",
            "기능 알려",
            "명령어",
            "스킬 목록",
            "무엇을 할",
            "좋은 일이 없",
            "피곤해",
            "우울해",
            "배고파",
            "심심해",
            "그렇네",
            "수고",
            "고마워",
            "감사",
            "잘자",
            "잘가"
        );
    }

    public static bool LooksLikeStandaloneFreshGreeting(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0 || normalized.Length > 16)
        {
            return false;
        }

        var compact = Regex.Replace(normalized, @"[\s\p{P}\p{S}]+", "");
        return compact is
            "ㅎㅇ" or
            "ㅎㅇㅎㅇ" or
            "하이" or
            "안녕" or
            "안녕하세요" or
            "안녕하십니까" or
            "안뇽" or
            "안뇽하세요" or
            "헬로" or
            "hi" or
            "hello" or
            "hey" or
            "yo";
    }

    public static bool LooksLikeLocalDateTimeQuestion(string input)
    {
        var normalized = Regex.Replace((input ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        if (normalized.Length == 0)
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "시간복잡도",
                "time complexity",
                "runtime complexity",
                "응답 시간",
                "실행 시간",
                "timeout",
                "타임아웃",
                "러닝타임"))
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "지금 몇시",
                "지금 몇 시",
                "몇시야",
                "몇 시야",
                "현재 시간",
                "현재 시각",
                "지금 시간",
                "로컬 시간",
                "오늘 날짜",
                "오늘 며칠",
                "현재 날짜",
                "로컬 날짜",
                "무슨 요일",
                "어느 요일",
                "몇월 몇일",
                "몇 월 몇 일",
                "현재 타임존",
                "현재 시간대",
                "로컬 타임존",
                "로컬 시간대",
                "what time is it",
                "current time",
                "local time",
                "today's date",
                "today date",
                "current date",
                "what date is it",
                "what day is it",
                "current timezone",
                "local timezone",
                "time zone"))
        {
            return true;
        }

        return Regex.IsMatch(
            normalized,
            @"(?:^|\s)몇\s*시(?:야|예요|인가요|입니까)?(?:\?|$)|(?:^|\s)몇\s*일(?:이야|인가요|입니까)?(?:\?|$)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
    }

    public static bool LooksLikeComparisonRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "비교",
            "차이",
            "대비",
            "vs",
            "compare",
            "difference",
            "국가별",
            "유형별",
            "카테고리별"
        );
    }

    public static bool LooksLikeListOutputRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return RequestedCountRegex.IsMatch(normalized)
            || TopCountRegex.IsMatch(normalized)
            || ContainsAny(normalized, "뉴스", "news", "헤드라인", "속보", "목록", "리스트", "top");
    }

    public static bool LooksLikeTableRenderRequest(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            normalized,
            "표로",
            "표 형태",
            "표형식",
            "테이블",
            "도표",
            "table",
            "tabular"
        );
    }

    public static IReadOnlyList<WebPreferenceHint> ExtractWebPreferenceHints(string text, bool fromMemoryNote)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<WebPreferenceHint>();
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        var hints = new List<WebPreferenceHint>(8);
        foreach (var raw in lines.Take(120))
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            line = Regex.Replace(line, @"^[-*•\d\.\)\s]+", string.Empty).Trim();
            if (line.Length < 4 || line.Length > 96)
            {
                continue;
            }

            if (fromMemoryNote
                && (line.StartsWith("created_utc", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("mode", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("conversation_id", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("conversation_title", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("provider", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("model", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("#", StringComparison.Ordinal)))
            {
                continue;
            }

            if (!LooksLikeWebPreferenceLine(line, fromMemoryNote))
            {
                continue;
            }

            var category = ClassifyWebPreferenceCategory(line);
            if (category.Length == 0)
            {
                continue;
            }

            hints.Add(new WebPreferenceHint(category, line));
            if (hints.Count >= 8)
            {
                break;
            }
        }

        return hints;
    }

    public static string NormalizeWebPreferenceKey(string text)
    {
        return Regex.Replace((text ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
    }

    public static bool ShouldBlockWebMemoryHintByOverride(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "말고",
            "제외",
            "빼고",
            "아니고",
            "반대로",
            "다르게",
            "바꿔",
            "변경",
            "무시",
            "이번엔",
            "이번에는"
        );
    }

    public static bool LooksLikeWebFormatDirective(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "형식",
            "포맷",
            "표로",
            "표 형태",
            "표형식",
            "테이블",
            "table",
            "불릿",
            "번호",
            "목록",
            "리스트",
            "한줄",
            "줄바꿈",
            "markdown",
            "마크다운",
            "no.n"
        );
    }

    public static bool LooksLikeWebToneDirective(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "간결",
            "짧게",
            "자세히",
            "길게",
            "말투",
            "존댓말",
            "반말",
            "톤"
        );
    }

    public static bool LooksLikeWebLanguageDirective(string input)
    {
        var lowered = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        return ContainsAny(lowered, "한국어", "한글", "영어", "english", "korean");
    }

    public static int ResolveWebDefaultCount(string input, int newsDefaultCount, int listDefaultCount)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        return ContainsAny(normalized, "뉴스", "news", "헤드라인", "속보")
            ? Math.Clamp(newsDefaultCount, 1, 20)
            : Math.Clamp(listDefaultCount, 1, 20);
    }

    public static double ResolveForcedMemoryMinScore(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return 0.45d;
        }

        if (normalized.Length < 10 && !normalized.Contains('.'))
        {
            return 0.65d;
        }

        if (LooksLikeRealtimeQuestion(normalized))
        {
            return 0.3d;
        }

        if (ContainsAny(normalized, "비교", "차이", "요약", "정리", "compare", "difference", "summary"))
        {
            return 0.5d;
        }

        return 0.45d;
    }

    public static string ResolveSearchFreshnessForQuery(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (ContainsAny(normalized, "오늘", "어제", "방금", "실시간", "today", "yesterday", "breaking"))
        {
            return "day";
        }

        if (ContainsAny(normalized, "이번달", "한달", "month", "monthly"))
        {
            return "month";
        }

        if (ContainsAny(normalized, "올해", "연간", "year", "yearly"))
        {
            return "year";
        }

        return "week";
    }

    public static int ResolveRequestedResultCountFromQuery(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        var defaultCount = ContainsAny(normalized, "뉴스", "news", "헤드라인", "속보", "브리핑")
            ? 10
            : 5;
        if (normalized.Length == 0)
        {
            return defaultCount;
        }

        var direct = RequestedCountRegex.Match(normalized);
        if (direct.Success
            && int.TryParse(direct.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directParsed))
        {
            return Math.Clamp(directParsed, 1, 10);
        }

        var top = TopCountRegex.Match(normalized);
        if (top.Success
            && int.TryParse(top.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var topParsed))
        {
            return Math.Clamp(topParsed, 1, 10);
        }

        return defaultCount;
    }

    public static bool HasExplicitRequestedCountInQuery(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return false;
        }

        return RequestedCountRegex.IsMatch(normalized) || TopCountRegex.IsMatch(normalized);
    }

    private static bool LooksLikeConversationalFollowUp(string input)
    {
        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > 120)
        {
            return false;
        }

        if (ContainsAny(
                normalized,
                "그니까",
                "그러니까",
                "그래서",
                "그럼",
                "그러면",
                "그래도",
                "잘 돌아",
                "잘 작동",
                "잘 동작",
                "잘 되",
                "잘 될",
                "잘 굴러",
                "쓸만",
                "쓸 만",
                "괜찮",
                "어때",
                "어떨",
                "어떤지",
                "어떻게 생각",
                "어떻게 봐",
                "네 생각",
                "네 의견",
                "너 생각",
                "너의 생각",
                "당신 생각",
                "당신의 생각",
                "검토해",
                "판단해",
                "추천해",
                "비교해",
                "what do you think",
                "would it work"))
        {
            return true;
        }

        return normalized.Length <= 60
            && ContainsAny(normalized, "이거", "이건", "이게", "이걸", "그거", "그건", "그게", "그걸", "저거", "이 환경", "이 모델", "이 상황");
    }

    private static bool LooksLikeWebPreferenceLine(string line, bool fromMemoryNote)
    {
        var lowered = (line ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return false;
        }

        if (lowered.Contains("http://", StringComparison.Ordinal)
            || lowered.Contains("https://", StringComparison.Ordinal))
        {
            return false;
        }

        if (ContainsAny(lowered, "가격", "시세", "주가", "배럴", "달러", "환율", "정확한 날짜", "대기압", "수치"))
        {
            return false;
        }

        if (!fromMemoryNote
            && !ContainsAny(lowered, "항상", "앞으로", "이제부터", "매번", "선호", "기억", "기본"))
        {
            return false;
        }

        return ContainsAny(
            lowered,
            "출처",
            "매체",
            "source",
            "site:",
            "형식",
            "포맷",
            "불릿",
            "번호",
            "목록",
            "리스트",
            "한줄",
            "줄바꿈",
            "간결",
            "짧게",
            "자세히",
            "말투",
            "존댓말",
            "반말",
            "한국어",
            "한글",
            "영어",
            "english",
            "korean",
            "cnn",
            "reuters",
            "bbc",
            "연합뉴스",
            "뉴시스",
            "kbs",
            "mbc",
            "sbs",
            "건수",
            "no.n"
        ) || RequestedCountRegex.IsMatch(lowered) || TopCountRegex.IsMatch(lowered);
    }

    private static string ClassifyWebPreferenceCategory(string line)
    {
        var lowered = (line ?? string.Empty).Trim().ToLowerInvariant();
        if (lowered.Length == 0)
        {
            return string.Empty;
        }

        if (ContainsAny(lowered, "출처", "매체", "source", "site:", "cnn", "reuters", "bbc", "연합뉴스", "뉴시스", "kbs", "mbc", "sbs"))
        {
            return "source";
        }

        if (ContainsAny(lowered, "형식", "포맷", "불릿", "번호", "목록", "리스트", "한줄", "줄바꿈", "markdown", "마크다운", "no.n"))
        {
            return "format";
        }

        if (ContainsAny(lowered, "간결", "짧게", "자세히", "길게", "말투", "존댓말", "반말", "톤"))
        {
            return "tone";
        }

        if (ContainsAny(lowered, "한국어", "한글", "영어", "english", "korean"))
        {
            return "language";
        }

        var hasCount = RequestedCountRegex.IsMatch(lowered)
            || TopCountRegex.IsMatch(lowered)
            || Regex.IsMatch(lowered, @"(?<!\d)\d{1,2}\s*(개|건)", RegexOptions.CultureInvariant);
        if (hasCount && ContainsAny(lowered, "뉴스", "news", "헤드라인", "목록", "리스트", "건수"))
        {
            return "count";
        }

        return string.Empty;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
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
