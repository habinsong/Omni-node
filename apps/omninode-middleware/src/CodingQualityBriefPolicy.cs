using System.Text;

namespace OmniNode.Middleware;

internal static class CodingQualityBriefPolicy
{
    public static string Build(
        string objective,
        string languageHint,
        Func<string, string, bool> isFrontendLikeCodingTask,
        Func<string, string, bool> isGameLikeCodingTask
    )
    {
        var normalizedObjective = (objective ?? string.Empty).Trim();
        var language = CodingLanguagePolicy.ResolveInitialCodingLanguage(languageHint, normalizedObjective);
        var requestedPaths = CodingFallbackPolicy.ExtractRequestedCodingPaths(normalizedObjective, language);
        var expectedOutput = CodingFallbackPolicy.ExtractExpectedConsoleOutput(normalizedObjective);
        var builder = new StringBuilder();
        builder.AppendLine($"- language={language}");
        if (requestedPaths.Count > 0)
        {
            builder.AppendLine($"- required_files={string.Join(", ", requestedPaths.Take(8))}");
        }
        else
        {
            builder.AppendLine("- required_files=요청에서 명시한 파일이 없으면 표준 엔트리 파일을 만들 것");
        }

        if (!string.IsNullOrWhiteSpace(expectedOutput))
        {
            builder.AppendLine($"- expected_stdout={expectedOutput}");
        }

        if (isFrontendLikeCodingTask(normalizedObjective, language))
        {
            builder.AppendLine("- ui_acceptance=첫 화면에 실제 DOM 콘텐츠가 보이고 주요 조작이 동작해야 함");
            builder.AppendLine("- file_boundary=index.html/styles.css/app.js 같은 실행 가능한 브라우저 파일 구조 우선");
        }
        else if (isGameLikeCodingTask(normalizedObjective, language))
        {
            builder.AppendLine("- game_acceptance=입력 처리, 상태 갱신, 실패/승리 조건이 있는 실제 루프 필요");
        }
        else
        {
            builder.AppendLine("- acceptance=실행 가능, 요청 출력/동작 충족, 불필요한 더미/TODO 없음");
        }

        builder.AppendLine("- verification=마지막 액션은 가능한 실제 실행/검증이어야 하며 실패하면 원인을 수정할 것");
        return builder.ToString().Trim();
    }
}
