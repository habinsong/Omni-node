using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace OmniNode.Middleware;

public sealed partial class CommandService
{
    private const int ThinkPlusContextMaxChars = 2000;
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

            return $"[Think+ 모드: 최신 웹 검색 결과]\n{capped}\n\n[Think+ 답변 가이드]\n- 위 웹 검색 결과의 사실을 우선 반영해 답변하세요.\n- 부족한 부분은 본인의 지식·추론으로 자연스럽게 보강하세요.\n- 답변은 한국어로, 결론 먼저. 출처 인용은 정말 필요할 때만 간결히.\n- 웹 결과와 본인 지식이 충돌하면 웹 결과를 기준으로 하고 그 이유를 한 줄 명시하세요.\n--------------------------------------------------\n\n";
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
