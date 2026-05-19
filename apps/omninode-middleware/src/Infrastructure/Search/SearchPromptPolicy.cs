using System.Text;
using System.Text.Json;

namespace OmniNode.Middleware;

internal readonly record struct SearchPromptRepositoryContext(
    string RepositorySlug,
    string Description,
    string ReadmeText
);

internal static class SearchPromptPolicy
{
    public static string BuildWebNeedDecisionPrompt(string normalizedInput)
    {
        return "너는 라우팅 전용 판단기다.\n"
            + "사용자 입력이 최신 외부 웹 근거가 필요한지 판정하고 JSON 한 줄만 출력해라.\n\n"
            + "출력 스키마:\n"
            + "{\"need_web\":true|false,\"reason\":\"짧은 근거\"}\n\n"
            + "판정 규칙:\n"
            + "- 뉴스/오늘/최근/실시간/최신/현재 상태/시세/가격/일정/법·정책 변경/특정 매체 기사 요청이면 need_web=true\n"
            + "- AI 봇(너, 자신)의 정체성, 능력, 사용법에 대한 질문이거나 인사, 일상 대화(안녕, 반가워 등)면 무조건 need_web=false\n"
            + "- 짧은 감정 표현(피곤해, 우울해, 좋은 일이 없어), 단순 동조(응, 맞아, 그렇네), 되묻기(너는?) 등 문맥이 없는 일상 대화면 무조건 need_web=false\n"
            + "- 일반 개념 설명/번역/코드 설명/창작/사용자 제공 텍스트 요약이면 need_web=false\n"
            + "- 설명문, 코드블록, 마크다운 금지\n\n"
            + "사용자 입력:\n"
            + normalizedInput;
    }

    public static string BuildGeminiUrlContextAnswerPrompt(
        string input,
        IReadOnlyList<string> urls,
        string memoryHint,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle,
        bool includeGoogleSearch,
        int webDefaultNewsCount,
        int webDefaultListCount,
        SearchPromptRepositoryContext? repositoryContext = null
    )
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var effectiveInput = SearchUrlContextPolicy.ResolveImplicitUrlRequest(normalizedInput, urls);
        var normalizedMemoryHint = (memoryHint ?? string.Empty).Trim();
        var hasExplicitCount = SearchQueryPolicy.HasExplicitRequestedCountInQuery(effectiveInput);
        var requestedCount = hasExplicitCount
            ? Math.Clamp(SearchQueryPolicy.ResolveRequestedResultCountFromQuery(effectiveInput), 1, 20)
            : SearchQueryPolicy.ResolveWebDefaultCount(effectiveInput, webDefaultNewsCount, webDefaultListCount);
        var listMode = SearchQueryPolicy.LooksLikeListOutputRequest(effectiveInput);
        var tableMode = allowMarkdownTable && SearchQueryPolicy.LooksLikeTableRenderRequest(effectiveInput);
        var comparisonMode = SearchQueryPolicy.LooksLikeComparisonRequest(effectiveInput);
        var primaryUrl = urls.FirstOrDefault() ?? string.Empty;
        var siteOverviewMode = urls.Count == 1
            && (SearchUrlContextPolicy.LooksLikeSiteOverviewRequest(effectiveInput)
                || SearchUrlContextPolicy.LooksLikeImplicitUrlSummaryRequest(normalizedInput, urls))
            && SearchUrlContextPolicy.LooksLikeSiteRootUrl(primaryUrl);
        var repositoryMode = urls.Count == 1 && SearchUrlContextPolicy.LooksLikeRepositoryUrl(primaryUrl);
        var documentMode = urls.Count == 1 && SearchUrlContextPolicy.LooksLikeDocumentationUrl(primaryUrl);
        var articleMode = urls.Count == 1 && SearchUrlContextPolicy.LooksLikeArticleUrl(primaryUrl);

        var builder = new StringBuilder();
        builder.AppendLine("너는 URL 컨텍스트 기반 한국어 답변기다.");
        builder.AppendLine("- 아래 참조 URL 내용이 1차 근거다.");
        if (includeGoogleSearch)
        {
            builder.AppendLine("- google_search가 가능하면 배경 보강이나 최신성 확인에만 보조적으로 사용해라.");
            builder.AppendLine("- URL에 없는 내용은 추정하지 말고, 검색으로도 확인되지 않으면 없다고 말해라.");
        }
        else
        {
            builder.AppendLine("- URL에 없는 내용은 추정하지 말고, URL에서 직접 확인되지 않으면 없다고 말해라.");
        }

        builder.AppendLine("- 허위/기억 기반 문장 금지.");
        builder.AppendLine("- 출처는 URL이 아닌 도메인명으로만 작성해라.");
        builder.AppendLine("- 출처 표기는 마지막 한 줄 형식으로만 작성: '출처: 도메인1, 도메인2'.");
        builder.AppendLine("- '출처 링크:' 섹션이나 URL 단독 줄을 만들지 마라.");
        builder.AppendLine("- 문장 중간을 임의로 줄바꿈하지 말고 자연스러운 한국어 문장으로 정리해라.");
        builder.AppendLine("- 답변이 길어질 것 같으면 항목 수를 줄이고 요약해서 문장을 끝까지 완성해라.");
        builder.AppendLine("- 페이지의 제목, 목적, 핵심 내용, 중요한 세부사항이 무엇인지 요약하면서도 분명하게 드러내라.");
        builder.AppendLine("- 본문에서 임의의 '**' 강조를 남발하지 말고, 구조 라벨이나 항목 제목에만 제한적으로 사용해라.");
        if (repositoryContext.HasValue)
        {
            builder.AppendLine("- 아래 [직접 읽은 저장소 정보] 블록이 있으면 그 내용을 URL 도구 결과보다 우선 근거로 사용해라.");
            builder.AppendLine("- 사용자가 특정 키워드나 주제만 물으면 [직접 읽은 저장소 정보]에서 직접 확인된 내용만 답하고, 없으면 없다고 말해라.");
        }

        if (tableMode)
        {
            builder.AppendLine("- 사용자가 표를 요청했으므로 반드시 GitHub 마크다운 표로 작성해라.");
            builder.AppendLine("- 표의 헤더/구분선/데이터 행은 모두 '|'로 시작하고 '|'로 끝내라.");
            builder.AppendLine("- 표를 코드블록으로 감싸지 마라.");
            builder.AppendLine("- 표 안에 '출처' 열이나 '출처' 행을 만들지 마라.");
        }
        else
        {
            builder.AppendLine("- 사용자가 표를 요청하지 않았다면 표/ASCII 테이블 형식은 쓰지 마라.");
        }

        if (enforceTelegramOutputStyle)
        {
            builder.AppendLine("- 출력 채널은 텔레그램이다. 군더더기 머리말 없이 바로 본문 형식으로 작성해라.");
        }

        if (siteOverviewMode)
        {
            builder.AppendLine("- 이 요청은 사이트/서비스 소개 요청이다.");
            builder.AppendLine("- 해당 사이트가 무엇을 하는지, 핵심 제품/기능, 누구를 위한 서비스인지, 눈여겨볼 점을 정리해라.");
        }
        else if (repositoryMode)
        {
            builder.AppendLine("- 이 요청은 코드 저장소/프로젝트 소개 요청이다.");
            builder.AppendLine("- 저장소가 무엇을 하는지, 핵심 기능, 사용 대상, 주요 구조, 눈에 띄는 포인트를 정리해라.");
        }
        else if (documentMode)
        {
            builder.AppendLine("- 이 요청은 문서/가이드 설명 요청이다.");
            builder.AppendLine("- 문서의 목적, 어떤 기능을 설명하는지, 사용 흐름, 지원 대상, 제한/주의점을 정리해라.");
        }
        else if (articleMode)
        {
            builder.AppendLine("- 이 요청은 기사/게시물 요약 요청이다.");
            builder.AppendLine("- 핵심 사실, 주요 인물/기관, 중요한 수치/날짜, 왜 중요한지를 우선 정리해라.");
        }

        if (tableMode)
        {
            builder.AppendLine("요약: <1문장>");
            builder.AppendLine();
            builder.AppendLine("| 항목 | 내용 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| 예시 | 값 |");
            builder.AppendLine();
            builder.AppendLine("출처: 도메인1, 도메인2");
            if (listMode)
            {
                builder.AppendLine($"- 표의 데이터 행은 가능하면 {requestedCount}개로 맞춰라.");
            }
        }
        else if (listMode)
        {
            builder.AppendLine($"- 목록 모드: 목표 {requestedCount}건.");
            builder.AppendLine("- 각 항목은 제목과 핵심 내용만 간결하게 정리해라.");
        }
        else
        {
            builder.AppendLine("- 일반 검색형 답변은 아래 형식만 사용해라.");
            builder.AppendLine("요약: <2~3문장>");
            builder.AppendLine();
            builder.AppendLine("무엇을 다루나: <1~2문장>");
            builder.AppendLine();
            builder.AppendLine("핵심:");
            builder.AppendLine("- <핵심 포인트 3~5개>");
            builder.AppendLine("- <핵심 포인트>");
            builder.AppendLine();
            builder.AppendLine("중요 포인트:");
            builder.AppendLine("- <사용자가 바로 알아야 할 점>");
            builder.AppendLine();
            builder.AppendLine("출처: 도메인1, 도메인2");
            if (comparisonMode)
            {
                builder.AppendLine("- 비교/분류형 답변이면 항목별 줄바꿈을 유지해라.");
            }
        }

        if (normalizedMemoryHint.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("사용자 선호 메모리(보조 규칙, 충돌 시 무시):");
            builder.AppendLine(normalizedMemoryHint);
        }

        if (repositoryContext.HasValue)
        {
            builder.AppendLine();
            builder.AppendLine("[직접 읽은 저장소 정보]");
            builder.AppendLine($"저장소: {repositoryContext.Value.RepositorySlug}");
            if (!string.IsNullOrWhiteSpace(repositoryContext.Value.Description))
            {
                builder.AppendLine($"설명: {repositoryContext.Value.Description}");
            }

            builder.AppendLine("README 발췌:");
            builder.AppendLine(repositoryContext.Value.ReadmeText);
        }

        builder.AppendLine();
        builder.AppendLine("참조 URL:");
        foreach (var url in urls.Take(3))
        {
            builder.AppendLine($"- {url}");
        }
        builder.AppendLine();
        builder.AppendLine("사용자 입력:");
        builder.AppendLine(effectiveInput);
        return builder.ToString().Trim();
    }

    public static string BuildGeminiWebAnswerPrompt(
        string input,
        string memoryHint,
        bool selfDecideNeedWeb,
        bool allowMarkdownTable,
        bool enforceTelegramOutputStyle,
        int webDefaultNewsCount,
        int webDefaultListCount,
        Func<string, string, string> resolveSourceDomain
    )
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var normalizedMemoryHint = (memoryHint ?? string.Empty).Trim();
        var hasExplicitCount = SearchQueryPolicy.HasExplicitRequestedCountInQuery(normalizedInput);
        var requestedCount = hasExplicitCount
            ? Math.Clamp(SearchQueryPolicy.ResolveRequestedResultCountFromQuery(normalizedInput), 1, 20)
            : SearchQueryPolicy.ResolveWebDefaultCount(normalizedInput, webDefaultNewsCount, webDefaultListCount);
        var sourceFocus = SearchQueryPolicy.ExtractSourceFocusHintFromInput(normalizedInput);
        var sourceDomain = resolveSourceDomain(normalizedInput, sourceFocus);
        var listMode = SearchQueryPolicy.LooksLikeListOutputRequest(normalizedInput);
        var tableMode = allowMarkdownTable && SearchQueryPolicy.LooksLikeTableRenderRequest(normalizedInput);
        var comparisonMode = SearchQueryPolicy.LooksLikeComparisonRequest(normalizedInput);

        var builder = new StringBuilder();
        builder.AppendLine("너는 최신 웹 근거 기반 한국어 답변기다.");
        builder.AppendLine("- 현재 사용자 입력을 최우선으로 따른다.");
        builder.AppendLine("- 선호 메모리가 있더라도 현재 입력과 충돌하면 즉시 무시한다.");
        builder.AppendLine("- 사실/수치/날짜/가격/사건 정보는 웹 근거만 사용하고 추정하지 마라.");
        builder.AppendLine("- 사용자가 특정 사이트/매체(AccuWeather, weather.com, 인베스팅닷컴, investing.com, 연합뉴스, Bloomberg, naver.com 등)를 언급하면 그 도메인을 우선 검색해라. 검색어에 site: 연산자 또는 도메인명을 포함해 1차 시도해라.");
        builder.AppendLine("- 1차 검색에서 정보가 충분하지 않으면 키워드를 바꿔(영문/한글 변환, 동의어, 기간 명시, 단위 명시, 추가 매체 포함) 최소 2~3차례 재검색해라.");
        builder.AppendLine("- 검색 결과에 명시되지 않은 구체 수치(가격, 지수, 기온, 강수확률, 통계, 날짜 등)는 절대 만들어내지 마라. 결과에 없으면 '검색 결과에 해당 데이터가 명확하게 없습니다'라고 솔직히 답하고 어디서 확인하면 되는지 안내만 해라.");
        builder.AppendLine("- 미래 시점(수개월~수년 뒤) 예측은 일반적으로 부정확하다는 점을 명시하고, 명시된 공식 예보/장기 전망 출처에서 인용한 값만 사용해라.");
        builder.AppendLine("- 답변에 '확인 중', '확인했습니다', '추출 완료', '잠시만요' 같은 진행 안내문이나 거짓 보고를 넣지 마라. 결론과 핵심부터 직접 작성해라.");
        if (selfDecideNeedWeb)
        {
            builder.AppendLine("- 먼저 사용자 입력만 보고 웹검색 필요 여부를 스스로 판단해라.");
            builder.AppendLine("- 웹검색이 불필요하면 도구 호출 없이 바로 답변해라.");
        }
        else
        {
            builder.AppendLine("- 이번 요청은 웹검색으로만 답변해라.");
        }

        builder.AppendLine("- 허위/기억 기반 문장 금지.");
        builder.AppendLine("- 출처는 URL이 아닌 매체명으로만 작성해라.");
        builder.AppendLine("- 출처 표기는 마지막 한 줄 형식으로만 작성: '출처: 매체1, 매체2, 매체3'.");
        builder.AppendLine("- '출처 링크:' 섹션이나 URL 단독 줄을 만들지 마라.");
        builder.AppendLine("- 문장 중간을 임의로 줄바꿈하지 말고 자연스러운 한국어 문장으로 정리해라.");
        if (tableMode)
        {
            builder.AppendLine("- 사용자가 표를 요청했으므로 반드시 GitHub 마크다운 표로 작성해라.");
            builder.AppendLine("- 표의 헤더/구분선/데이터 행은 모두 '|'로 시작하고 '|'로 끝내라.");
            builder.AppendLine("- 표를 코드블록으로 감싸지 마라.");
            builder.AppendLine("- 표 안에 '출처' 열이나 '출처' 행을 만들지 마라.");
            builder.AppendLine("- 표 요청 응답에서는 불릿 목록으로 대체하지 마라.");
            builder.AppendLine("- 표가 필요한 정보는 반드시 표의 행/열로 정리해라.");
        }
        else
        {
            builder.AppendLine("- 사용자가 표를 요청하지 않았다면 표/ASCII 테이블 형식은 쓰지 마라.");
        }
        if (enforceTelegramOutputStyle)
        {
            builder.AppendLine("- 출력 채널은 텔레그램이다. 군더더기 머리말 없이 바로 본문 형식으로 작성해라.");
            builder.AppendLine("- URL 본문 노출은 금지하고 매체명만 사용해라.");
        }
        if (sourceFocus.Length > 0)
        {
            builder.AppendLine($"- 사용자가 요구한 소스 초점: {sourceFocus}");
            if (sourceDomain.Length > 0)
            {
                builder.AppendLine($"- 가능한 경우 {sourceDomain} 원출처 기사 우선.");
            }
        }

        if (tableMode)
        {
            builder.AppendLine("- 표 요청 모드: 아래 형식만 사용해라.");
            builder.AppendLine("요약: <1문장>");
            builder.AppendLine();
            builder.AppendLine("| 항목 | 내용 |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine("| 예시 | 값 |");
            builder.AppendLine();
            builder.AppendLine("출처: 매체1, 매체2");
            builder.AppendLine("- 표 앞뒤에 불릿 목록을 쓰지 마라.");
            builder.AppendLine("- 가능한 경우 실제 데이터 열 이름을 사용해 3열 이상 표로 확장해라.");
            if (listMode)
            {
                builder.AppendLine($"- 표의 데이터 행은 가능하면 {requestedCount}개로 맞춰라.");
            }
        }
        else if (listMode)
        {
            builder.AppendLine($"- 목록 모드: 목표 {requestedCount}건.");
            builder.AppendLine("- 각 항목은 제목과 핵심 내용만 간결하게 정리해라.");
            if (hasExplicitCount)
            {
                builder.AppendLine($"- 사용자가 건수를 명시했으므로 가능하면 정확히 {requestedCount}건을 작성해라.");
            }
            else
            {
                builder.AppendLine($"- 건수 미지정 요청이므로 기본 {requestedCount}건으로 작성해라.");
            }
        }
        else
        {
            builder.AppendLine("- 일반 질의 모드: 핵심 답변을 간결하게 작성해라.");
            if (comparisonMode)
            {
                builder.AppendLine("- 비교/분류형 답변이면 항목별 줄바꿈을 유지해라.");
            }
            else
            {
                builder.AppendLine("- 일반 검색형 답변은 아래 형식만 사용해라.");
                builder.AppendLine("요약: <1~2문장>");
                builder.AppendLine();
                builder.AppendLine("핵심:");
                builder.AppendLine("- <핵심 포인트>");
                builder.AppendLine("- <핵심 포인트>");
                builder.AppendLine();
                builder.AppendLine("출처: 매체1, 매체2");
                builder.AppendLine("- 위 형식 외의 제목/머리말/날짜 안내문은 추가하지 마라.");
            }
        }

        if (normalizedMemoryHint.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("사용자 선호 메모리(보조 규칙, 충돌 시 무시):");
            builder.AppendLine(normalizedMemoryHint);
        }

        builder.AppendLine();
        builder.AppendLine("사용자 입력:");
        builder.AppendLine(normalizedInput);
        return builder.ToString().Trim();
    }

    public static int ResolveGeminiWebAnswerMaxOutputTokens(
        string input,
        int webDefaultNewsCount,
        int webDefaultListCount
    )
    {
        var normalized = (input ?? string.Empty).Trim();
        var targetCount = SearchQueryPolicy.HasExplicitRequestedCountInQuery(normalized)
            ? Math.Clamp(SearchQueryPolicy.ResolveRequestedResultCountFromQuery(normalized), 1, 20)
            : SearchQueryPolicy.ResolveWebDefaultCount(normalized, webDefaultNewsCount, webDefaultListCount);
        var tableMode = SearchQueryPolicy.LooksLikeTableRenderRequest(normalized);
        var listMode = SearchQueryPolicy.LooksLikeListOutputRequest(normalized);
        var comparisonMode = SearchQueryPolicy.LooksLikeComparisonRequest(normalized);
        if (!tableMode && !listMode && !comparisonMode)
        {
            return 1024;
        }

        if (targetCount > 5 || (tableMode && normalized.Length >= 80))
        {
            return 1280;
        }

        return 1024;
    }

    public static int ResolveGeminiUrlContextMaxOutputTokens(
        string input,
        int webDefaultNewsCount,
        int webDefaultListCount
    )
    {
        var normalized = (input ?? string.Empty).Trim();
        var targetCount = SearchQueryPolicy.HasExplicitRequestedCountInQuery(normalized)
            ? Math.Clamp(SearchQueryPolicy.ResolveRequestedResultCountFromQuery(normalized), 1, 20)
            : SearchQueryPolicy.ResolveWebDefaultCount(normalized, webDefaultNewsCount, webDefaultListCount);
        var tableMode = SearchQueryPolicy.LooksLikeTableRenderRequest(normalized);
        var listMode = SearchQueryPolicy.LooksLikeListOutputRequest(normalized);
        var comparisonMode = SearchQueryPolicy.LooksLikeComparisonRequest(normalized);
        if (!tableMode && !listMode && !comparisonMode)
        {
            return 2048;
        }

        if (targetCount > 5 || (tableMode && normalized.Length >= 80))
        {
            return 4096;
        }

        return 2048;
    }

    public static bool IsGeminiUrlContextFailureText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        return normalized.StartsWith("Gemini URL 참조 요청 실패:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini URL 참조 응답 시간이 초과되었습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini URL 참조 호출 오류:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini URL 참조 응답이 비어 있습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("429", StringComparison.OrdinalIgnoreCase)
               && normalized.Contains("Gemini", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildGeminiUrlContextFailureNotice(string input, string failureText)
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var coreFailure = (failureText ?? string.Empty).Trim();
        return $"""
                요청하신 URL 참조 답변을 생성하지 못했습니다.
                원인: {coreFailure}
                안내: URL 접근 권한이나 본문 공개 상태를 확인한 뒤 다시 요청해 주세요.
                입력: {normalizedInput}
                """.Trim();
    }

    public static bool IsGeminiWebFailureText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        return normalized.StartsWith("Gemini 웹검색 요청 실패:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini 웹검색 응답 시간이 초과되었습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini 웹검색 호출 오류:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Gemini 웹검색 응답이 비어 있습니다.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("429", StringComparison.OrdinalIgnoreCase)
               && normalized.Contains("Gemini", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeminiWebTimeoutText(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        return normalized.StartsWith("Gemini 웹검색 응답 시간이 초과되었습니다.", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildGeminiWebFailureNotice(string input, string failureText)
    {
        var normalizedInput = (input ?? string.Empty).Trim();
        var coreFailure = (failureText ?? string.Empty).Trim();
        var shortagePrefix = SearchQueryPolicy.LooksLikeListOutputRequest(normalizedInput)
            ? "요청하신 목록을 생성하지 못했습니다."
            : "요청하신 최신 정보를 생성하지 못했습니다.";
        return $"""
                {shortagePrefix}
                원인: {coreFailure}
                안내: 잠시 후 다시 요청해 주세요.
                입력: {normalizedInput}
                """.Trim();
    }

    public static bool TryParseNeedWebDecisionJson(string? rawText, out bool needWeb, out string reason)
    {
        needWeb = false;
        reason = string.Empty;
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

            if (!TryGetPropertyIgnoreCase(root, "need_web", out var needWebElement)
                && !TryGetPropertyIgnoreCase(root, "needWeb", out needWebElement))
            {
                return false;
            }

            switch (needWebElement.ValueKind)
            {
                case JsonValueKind.True:
                    needWeb = true;
                    break;
                case JsonValueKind.False:
                    needWeb = false;
                    break;
                case JsonValueKind.String:
                    var normalizedToken = SearchQueryPolicy.NormalizeWebSearchDecisionToken(needWebElement.GetString());
                    if (normalizedToken == "yes")
                    {
                        needWeb = true;
                    }
                    else if (normalizedToken == "no")
                    {
                        needWeb = false;
                    }
                    else
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }

            if (TryGetPropertyIgnoreCase(root, "reason", out var reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String)
            {
                reason = (reasonElement.GetString() ?? string.Empty).Trim();
            }

            return true;
        }
        catch
        {
            return false;
        }
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
}
