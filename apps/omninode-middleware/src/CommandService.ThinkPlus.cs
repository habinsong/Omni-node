using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private const int ThinkPlusContextMaxChars = 1500;
    private const int ThinkPlusCacheTtlSeconds = 60;
    private readonly ConcurrentDictionary<string, bool> _thinkPlusByThread = new(StringComparer.Ordinal);
    // Think+ 의 grounded web search 결과를 짧은 시간 캐시. 동일 입력 retry/이중 호출 시 Gemini 비용·지연을 줄인다.
    // 키: 입력 hash, 값: (capped 결과, 만료시각).
    private readonly ConcurrentDictionary<string, (string Capped, DateTime ExpiresAtUtc)> _thinkPlusContextCache = new(StringComparer.Ordinal);

    private static readonly Regex ThinkPlusActivationRegex = new(
        @"(?i)(추론\s*모드\s*(켜|시작|활성|on)|think\s*plus\s*(on|start)|추론\s*모드\s*로\s*답)",
        RegexOptions.Compiled
    );

    private static readonly Regex ThinkPlusDeactivationRegex = new(
        @"(?i)(추론\s*모드\s*(꺼|중지|비활성|종료|off|그만)|think\s*plus\s*off|일반\s*모드(로)?\s*(돌아|복귀|전환)|추론\s*그만)",
        RegexOptions.Compiled
    );

    internal static bool LooksLikeThinkPlusActivation(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return ThinkPlusActivationRegex.IsMatch(input);
    }

    internal static bool LooksLikeThinkPlusDeactivation(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return ThinkPlusDeactivationRegex.IsMatch(input);
    }

    internal bool IsThinkPlusActiveForThread(string? threadKey)
    {
        if (string.IsNullOrWhiteSpace(threadKey)) return false;
        return _thinkPlusByThread.TryGetValue(threadKey, out var v) && v;
    }

    internal void SetThinkPlusForThread(string? threadKey, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(threadKey)) return;
        if (enabled) _thinkPlusByThread[threadKey] = true;
        else _thinkPlusByThread.TryRemove(threadKey, out _);
    }

    internal static string ApplyThinkPlusToggleNoteIfAny(string? note, string responseText)
    {
        if (string.IsNullOrEmpty(note)) return responseText;
        if (string.IsNullOrEmpty(responseText)) return note;
        return $"{note}\n\n{responseText}";
    }

    private static readonly Regex InternalMarkerLineRegex = new(
        @"^\s*\[(?:user|assistant|system|Single\s|Multi\s|Project Context|Active Skill[^\]]*|Think\+\s[^\]]*|컨텍스트\s[^\]]*|최근 대화|공유 메모리 노트|새 요청|로컬 시간|Skill Switched[^\]]*|Skill Deactivated[^\]]*)\][^\n]*\r?\n?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline
    );

    private static readonly Regex EmptyAcknowledgmentRegex = new(
        @"^\s*(?:확인\.?|준비되었습니다\.?|질문해\s*주세요\.?|네\.?|알겠습니다\.?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline
    );

    /// <summary>
    /// LLM 응답에 leak 된 내부 마커 라인을 제거. 빈 인사 응답도 정리.
    /// </summary>
    internal static string CleanLeakedSystemMarkers(string? text)
    {
        var input = text ?? string.Empty;
        if (input.Length == 0) return input;
        var cleaned = InternalMarkerLineRegex.Replace(input, string.Empty);
        // 너무 짧은 인사 응답이 단독으로 있으면 빈 문자열로 대체 (호출자가 fallback)
        cleaned = cleaned.Replace("\r\n", "\n", StringComparison.Ordinal);
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
        return cleaned.Trim();
    }

    /// <summary>
    /// Gemini grounded web search 결과를 fetched 한 뒤 prepend용 컨텍스트 블록을 만든다.
    /// 키 없거나 실패 시 빈 문자열 반환.
    /// activeSkillName이 비어있지 않으면 출력 형식·말투 규칙은 활성 스킬 지침에 양보한다.
    /// </summary>
    private async Task<string> BuildThinkPlusContextAsync(
        string input,
        string source,
        CancellationToken cancellationToken,
        string? activeSkillName = null
    )
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        if (!_llmRouter.HasGeminiApiKey())
        {
            _auditLogger.Log(
                NormalizeAuditToken(source, "web"),
                "think_plus_context",
                "skip",
                "reason=gemini_api_key_missing"
            );
            return string.Empty;
        }

        var cacheKey = ComputeThinkPlusCacheKey(trimmed);
        string capped;
        var hitCache = false;
        if (TryGetThinkPlusCached(cacheKey, out var cachedCapped))
        {
            capped = cachedCapped;
            hitCache = true;
            _auditLogger.Log(
                NormalizeAuditToken(source, "web"),
                "think_plus_context",
                "cache_hit",
                $"chars={capped.Length}"
            );
        }
        else
        {
            try
            {
                var web = await ComposeGroundedWebAnswerWithFallbackAsync(
                    trimmed,
                    string.Empty,
                    false,
                    allowMarkdownTable: false,
                    enforceTelegramOutputStyle: false,
                    streamCallback: null,
                    scope: "chat",
                    mode: "single",
                    conversationId: string.Empty,
                    decisionPath: "think_plus",
                    decisionMs: 0,
                    source: source,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
                var raw = web?.Response?.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw) || IsGroundedWebAnswerFailureText(raw))
                {
                    _auditLogger.Log(
                        NormalizeAuditToken(source, "web"),
                        "think_plus_context",
                        "fallback",
                        "reason=web_answer_failure"
                    );
                    return string.Empty;
                }

                capped = raw.Length > ThinkPlusContextMaxChars
                    ? raw[..ThinkPlusContextMaxChars] + "\n...(이하 요약 생략)"
                    : raw;

                StoreThinkPlusCache(cacheKey, capped);

                _auditLogger.Log(
                    NormalizeAuditToken(source, "web"),
                    "think_plus_context",
                    "ok",
                    $"chars={capped.Length}"
                );
            }
            catch (Exception ex)
            {
                _auditLogger.Log(
                    NormalizeAuditToken(source, "web"),
                    "think_plus_context",
                    "error",
                    $"detail={ex.Message}"
                );
                return string.Empty;
            }
        }

        _ = hitCache;

        // 활성 스킬이 있으면 출력 톤·형식 규칙은 스킬에 양보한다.
        // 사실 정확성 규칙(1~5,7)은 유지하되, 형식 관련 8번은 스킬 우선으로 대체.
        var styleRule = string.IsNullOrWhiteSpace(activeSkillName)
            ? "8. 답변은 한국어, 결론 먼저, 군더더기 없이. 출처는 핵심만 짧게(또는 생략)."
            : $"8. 출력 형식·말투·길이·구성은 활성 스킬(`{activeSkillName}`) 지침을 최우선으로 따르세요. 위 참고 자료는 사실 근거로만 사용하며, Think+ 자체의 톤 규칙은 적용하지 마세요.";

        return $@"[Think+ 참고 자료 — 시작]
다음은 사용자의 질문에 도움이 될 수 있는 최근 웹 검색 결과입니다.
이 내용은 참고용 자료이며, 사용자에게 보일 답변이 아닙니다.

{capped}
[Think+ 참고 자료 — 끝]

[Think+ 답변 규칙 — 반드시 따를 것]
1. 이 참고 자료를 그대로 복사하거나 통째로 붙여넣지 마세요.
2. 사용자의 원래 질문(아래 메시지)에 직접 답하세요.
3. 참고 자료의 사실을 활용하되, 자신의 지식·추론으로 종합·재구성해서 답하세요.
4. 참고 자료에 명시되지 않은 구체 수치(가격, 기온, 지수, 통계, 날짜 등)는 절대 만들어내지 마세요. 자료에 없으면 ""검색 결과에 해당 정보가 없다""고 솔직히 답하세요.
5. 사용자가 특정 사이트나 매체(AccuWeather, 인베스팅닷컴, 연합뉴스 등)를 언급했는데 자료에 그 출처 정보가 없으면 ""해당 사이트의 데이터를 직접 가져오지 못했다""고 명시하세요.
6. 답변에 ""확인.""·""확인했습니다.""·""준비되었습니다."" 같은 거짓 진행 보고나 빈 인사를 넣지 마세요. 결론·핵심부터 작성하세요.
7. 미래 시점(수개월~수년 뒤) 예측은 부정확함을 명시하고, 자료에 공식 예보가 있을 때만 그 값을 인용하세요.
{styleRule}
--------------------------------------------------

";
    }

    // /think on|off|status — 텔레그램 thread의 추론(Think+) 모드를 즉시 토글.
    private string? TryHandleTelegramThinkSlashCommand(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (!normalized.StartsWith("/think", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var firstLine = normalized.Split('\n', 2)[0];
        var tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var session = SessionContext.Create(EnsureTelegramLinkedConversation(), "telegram");
        var key = session.SessionId;

        if (tokens.Length < 2 || tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase) || tokens[1].Equals("?", StringComparison.Ordinal))
        {
            var active = IsThinkPlusActiveForThread(key);
            var (body, toggleCmd, toggleLabel) = active
                ? ("🧠 추론 모드: ON", "/think off", "🚫 끄기")
                : ("🧠 추론 모드: OFF", "/think on", "✅ 켜기");
            return AppendTelegramInlineButtons(body, (toggleCmd, toggleLabel));
        }

        var arg = tokens[1].Trim().ToLowerInvariant();
        if (arg is "on" or "켜" or "활성" or "start")
        {
            SetThinkPlusForThread(key, true);
            return "🧠 추론 모드 ON. 다음 메시지부터 최신 웹검색 결과를 참고해 답변합니다.";
        }
        if (arg is "off" or "꺼" or "끄" or "stop" or "중지" or "해제")
        {
            SetThinkPlusForThread(key, false);
            return "🧠 추론 모드 OFF. 일반 모드로 돌아갑니다.";
        }
        return "사용법: /think on | /think off | /think status";
    }

    // /web on|off — 텔레그램에서 단발성 웹검색 enable. 현재 turn 기준 토글이 아닌 thread sticky.
    private string? TryHandleTelegramWebSlashCommand(string? text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (!normalized.StartsWith("/web", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var firstLine = normalized.Split('\n', 2)[0];
        var tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var session = SessionContext.Create(EnsureTelegramLinkedConversation(), "telegram");
        var key = session.SessionId;

        if (tokens.Length < 2 || tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            var active = IsThinkPlusActiveForThread(key);
            var (body, toggleCmd, toggleLabel) = active
                ? ("🌐 웹검색 컨텍스트: ON (Think+ 모드와 공유)", "/web off", "🚫 끄기")
                : ("🌐 웹검색 컨텍스트: OFF", "/web on", "✅ 켜기");
            return AppendTelegramInlineButtons(body, (toggleCmd, toggleLabel));
        }

        var arg = tokens[1].Trim().ToLowerInvariant();
        if (arg is "on" or "켜" or "start" or "활성")
        {
            SetThinkPlusForThread(key, true);
            return "🌐 웹검색 컨텍스트 ON. 다음 메시지부터 최신 웹 자료를 참고합니다.";
        }
        if (arg is "off" or "꺼" or "끄" or "stop" or "중지" or "해제")
        {
            SetThinkPlusForThread(key, false);
            return "🌐 웹검색 컨텍스트 OFF.";
        }
        return "사용법: /web on | /web off | /web status";
    }

    private static string ComputeThinkPlusCacheKey(string trimmedInput)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(trimmedInput);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private bool TryGetThinkPlusCached(string cacheKey, out string capped)
    {
        capped = string.Empty;
        if (!_thinkPlusContextCache.TryGetValue(cacheKey, out var entry))
        {
            return false;
        }
        if (entry.ExpiresAtUtc <= DateTime.UtcNow)
        {
            _thinkPlusContextCache.TryRemove(cacheKey, out _);
            return false;
        }
        capped = entry.Capped;
        return true;
    }

    private void StoreThinkPlusCache(string cacheKey, string capped)
    {
        if (string.IsNullOrEmpty(cacheKey) || string.IsNullOrEmpty(capped))
        {
            return;
        }
        var expiresAt = DateTime.UtcNow.AddSeconds(ThinkPlusCacheTtlSeconds);
        _thinkPlusContextCache[cacheKey] = (capped, expiresAt);
    }
}
