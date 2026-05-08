using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private const int ThinkPlusContextMaxChars = 1500;
    private readonly ConcurrentDictionary<string, bool> _thinkPlusByThread = new(StringComparer.Ordinal);

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
    /// </summary>
    private async Task<string> BuildThinkPlusContextAsync(string input, string source, CancellationToken cancellationToken)
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

            var capped = raw.Length > ThinkPlusContextMaxChars
                ? raw[..ThinkPlusContextMaxChars] + "\n...(이하 요약 생략)"
                : raw;

            _auditLogger.Log(
                NormalizeAuditToken(source, "web"),
                "think_plus_context",
                "ok",
                $"chars={capped.Length}"
            );

            return $@"[Think+ 참고 자료 — 시작]
다음은 사용자의 질문에 도움이 될 수 있는 최근 웹 검색 결과입니다.
이 내용은 참고용 자료이며, 사용자에게 보일 답변이 아닙니다.

{capped}
[Think+ 참고 자료 — 끝]

[Think+ 답변 규칙 — 반드시 따를 것]
1. 이 참고 자료를 그대로 복사하거나 통째로 붙여넣지 마세요.
2. 사용자의 원래 질문(아래 메시지)에 직접 답하세요.
3. 참고 자료의 사실을 활용하되, 자신의 지식·추론으로 종합·재구성해서 답하세요.
4. 참고 자료에 답이 없으면 솔직히 ""정보가 부족하다""고 말하고 추정 근거를 짧게 덧붙이세요.
5. 답변은 한국어, 결론 먼저, 군더더기 없이. 출처는 핵심만 짧게(또는 생략).
--------------------------------------------------

";
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
}
