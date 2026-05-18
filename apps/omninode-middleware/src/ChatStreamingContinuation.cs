using System.Text;

namespace OmniNode.Middleware;

internal static class ChatStreamingContinuation
{
    private const int ContinuationTailMaxChars = 6000;
    private const int TruncationReasonMaxChars = 80;

    /// <summary>
    /// Streaming 응답이 중간에 끊겼을 때 사용자에게 안내할 suffix 메시지를 만든다.
    /// </summary>
    public static string BuildPartialTruncationSuffix(string provider, string reasonHint)
    {
        var name = OpenAiCompatibleProtocol.DisplayName(provider);
        var safeReason = string.IsNullOrWhiteSpace(reasonHint) ? "stream interrupted" : reasonHint.Trim();
        if (safeReason.Length > TruncationReasonMaxChars)
        {
            safeReason = safeReason[..TruncationReasonMaxChars] + "…";
        }
        return $"⚠️ [{name} 응답이 도중에 끊겼습니다 — {safeReason}. 다시 시도해 주세요.]";
    }

    /// <summary>
    /// NVIDIA NIM에서 자주 보이는 quota/queue 지연 timeout을 사용자 친화 문구로 변환한다.
    /// </summary>
    public static string BuildTimeoutMessage(string provider)
    {
        if (string.Equals(provider, "nvidia", StringComparison.OrdinalIgnoreCase))
        {
            return "NVIDIA NIM 응답 시간이 초과되었습니다. 무료 할당량 한계나 큐잉 지연일 수 있습니다. 잠시 후 다시 시도하거나 다른 provider로 바꿔 보세요.";
        }
        return $"{OpenAiCompatibleProtocol.DisplayName(provider)} 응답 시간이 초과되었습니다.";
    }

    /// <summary>
    /// delta callback이 예외를 던져도 호출자 흐름이 끊기지 않도록 안전하게 실행한다.
    /// </summary>
    public static void SafeEmitDelta(Action<string>? deltaCallback, string delta)
    {
        if (deltaCallback == null || string.IsNullOrEmpty(delta))
        {
            return;
        }

        try
        {
            deltaCallback(delta);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 새로 생성된 chunk를 누적 builder에 줄바꿈으로 이어 붙인다. 비어 있는 chunk는 무시한다.
    /// </summary>
    public static void AppendChunk(StringBuilder builder, string chunk)
    {
        var normalized = (chunk ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(normalized);
    }

    /// <summary>
    /// max_tokens 등으로 응답이 끊겼을 때, 같은 사용자 입력 + 이어쓸 위치 안내가 들어간 continuation 프롬프트를 만든다.
    /// </summary>
    public static string BuildContinuationPrompt(string originalInput, string writtenText)
    {
        var safeWritten = writtenText ?? string.Empty;
        var tail = safeWritten.Length <= ContinuationTailMaxChars
            ? safeWritten
            : safeWritten[^ContinuationTailMaxChars..];
        return """
               아래 답변이 길이 제한으로 중간에서 끊겼습니다.
               이미 작성된 내용은 절대 반복하지 말고, 바로 다음 문장부터 자연스럽게 이어서만 작성하세요.
               마크다운 머리말/서론 재출력 금지.

               [원래 사용자 입력]
               """
               + (originalInput ?? string.Empty)
               + """

               [이미 작성된 답변 끝부분]
               """
               + tail;
    }
}
