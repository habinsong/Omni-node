using System.Text;

namespace OmniNode.Middleware;

internal sealed record CodingLoopPromptPolicyRequest(
    string Objective,
    string ResolvedLanguage,
    string ModeLabel,
    string WorkspaceRoot,
    string Provider,
    string Model,
    bool OneShotMode,
    int Iteration,
    int MaxIterations,
    int MaxActions,
    string WorkspaceSnapshot,
    string RecentLogs,
    CodeExecutionResult LastExecution,
    string QualityBrief,
    IReadOnlyList<string> ProviderRuleLines,
    IReadOnlyList<string> LanguageRuleLines,
    IReadOnlyList<string> VerificationRuleLines
);

internal sealed record CodingWorkerSummaryDigest(string Provider, string Model, string Digest);

internal static class CodingPromptPolicy
{
    public static string BuildLoopPrompt(CodingLoopPromptPolicyRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("너는 로컬 코딩 실행 에이전트다.");
        builder.AppendLine($"모드: {request.ModeLabel}");
        builder.AppendLine($"모델 제공자: {request.Provider}");
        builder.AppendLine($"모델: {request.Provider}:{request.Model}");
        builder.AppendLine($"반복: {request.Iteration}/{request.MaxIterations}");
        builder.AppendLine($"기준 작업 디렉터리: {request.WorkspaceRoot}");
        builder.AppendLine($"언어 힌트: {request.ResolvedLanguage}");
        builder.AppendLine($"one-shot: {(request.OneShotMode ? "true" : "false")}");
        builder.AppendLine();
        builder.AppendLine("[목표]");
        builder.AppendLine(request.Objective);
        builder.AppendLine();
        builder.AppendLine("[품질 브리프]");
        builder.AppendLine(request.QualityBrief);
        builder.AppendLine();
        builder.AppendLine("[최근 실행 결과]");
        builder.AppendLine($"status={request.LastExecution.Status}");
        builder.AppendLine($"command={request.LastExecution.Command}");
        builder.AppendLine($"stdout={TrimForOutput(request.LastExecution.StdOut, 1200)}");
        builder.AppendLine($"stderr={TrimForOutput(request.LastExecution.StdErr, 1200)}");
        builder.AppendLine();
        builder.AppendLine("[최근 루프 로그]");
        builder.AppendLine(string.IsNullOrWhiteSpace(request.RecentLogs) ? "(none)" : request.RecentLogs);
        builder.AppendLine();
        builder.AppendLine("[워크스페이스 스냅샷]");
        builder.AppendLine(request.WorkspaceSnapshot);
        builder.AppendLine();
        builder.AppendLine("반드시 JSON 객체만 출력하라. 마크다운/설명 금지.");
        builder.AppendLine("스키마:");
        builder.AppendLine("{\"analysis\":\"...\",\"done\":false,\"final_message\":\"...\",\"actions\":[{\"type\":\"mkdir\",\"path\":\"상대경로\",\"content\":\"...\",\"command\":\"...\"}]}");
        builder.AppendLine("type 허용값: mkdir, write_file, append_file, read_file, delete_file, run");
        builder.AppendLine("주의: type을 `mkdir|write_file`처럼 합치지 말고 반드시 단일 값만 사용");
        builder.AppendLine($"제약: actions 최대 {Math.Max(1, request.MaxActions)}개");
        builder.AppendLine("제공자/모델 힌트:");
        foreach (var rule in request.ProviderRuleLines)
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("언어별 힌트:");
        foreach (var rule in request.LanguageRuleLines)
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("검증 규칙:");
        foreach (var rule in request.VerificationRuleLines)
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine("규칙:");
        builder.AppendLine("- analysis에는 이번 반복에서 무엇을 만들고 어떤 파일을 건드릴지 짧게 적는다");
        builder.AppendLine("- done=false 이면 actions에 최소 1개의 실질 액션(mkdir/write_file/append_file/read_file/delete_file/run)을 반드시 넣는다");
        builder.AppendLine("- 경로는 가능하면 상대경로 사용");
        builder.AppendLine("- run은 마지막 검증 단계에서 1회만 수행되므로 중간 반복에서는 가능한 한 넣지 말고 파일 생성/수정에 집중");
        builder.AppendLine("- JSON 문자열 내부 줄바꿈은 반드시 \\n 으로 이스케이프");
        builder.AppendLine("- path 값에는 줄바꿈, 탭, 마크다운 bullet, 따옴표 래핑을 넣지 말고 순수 상대경로만 넣는다");
        builder.AppendLine("- content는 실제 파일 내용 그대로 넣고, 문자열 리터럴이나 파일명 중간에 임의 줄바꿈을 넣지 않는다");
        builder.AppendLine("- 가능한 한 한 번에 필요한 파일을 모두 생성하고, 마지막 검증(run) 1회만 수행");
        builder.AppendLine("- 실행 성공 및 요구사항 충족 시 즉시 done=true, actions=[]");
        builder.AppendLine("- 오류가 있으면 원인 수정 액션을 포함");
        builder.AppendLine("- 목표 달성 시 done=true");
        return builder.ToString().Trim();
    }

    public static string BuildAgentObjectivePrompt(
        string input,
        string modeLabel,
        string resolvedLanguage,
        IReadOnlyList<string> languageRuleLines
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("목표: 사용자의 코딩 요청을 로컬 프로젝트에서 실제로 완성하세요.");
        builder.AppendLine($"모드: {modeLabel}");
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine("요구사항:");
        builder.AppendLine("- 기존 파일 구조를 우선 존중하고 필요한 최소 범위만 수정");
        builder.AppendLine("- 필요한 폴더/파일 생성 및 수정");
        builder.AppendLine("- 빌드/컴파일/실행/테스트 수행");
        builder.AppendLine("- 오류 발생 시 원인 분석 후 수정 반복");
        builder.AppendLine("- 더미 구현, TODO만 남기는 미완성 결과, 가짜 성공 보고 금지");
        builder.AppendLine("- 테스트용 초기화 로그, 빈 함수, 껍데기 UI, placeholder 데이터만으로 완료 처리 금지");
        builder.AppendLine("- 외부 라이브러리가 필요하면 제거하지 말고 해당 언어의 표준 의존성 파일에 명시");
        builder.AppendLine("- 실패한 명령을 같은 형태로 반복하지 말고 원인을 바꿔 수정");
        builder.AppendLine("- 완료 보고 전에 최종 실행 1회와 생성/수정 파일 존재 여부를 확인");
        builder.AppendLine("- 요청에 출력값이 있으면 stdout도 실제 결과로 확인");
        builder.AppendLine("- 최종 요약에는 실제 변경 내용과 검증 결과만 반영");
        builder.AppendLine("- 최종적으로 실행 가능한 상태를 목표로 진행");
        if (languageRuleLines.Count > 0)
        {
            builder.AppendLine("- 아래 언어별 제약을 같이 만족");
            foreach (var rule in languageRuleLines)
            {
                builder.AppendLine(rule);
            }
        }

        builder.AppendLine();
        builder.AppendLine("사용자 요청:");
        builder.AppendLine(input ?? string.Empty);
        return builder.ToString().Trim();
    }

    public static string BuildDraftWorkerPrompt(
        string objective,
        string resolvedLanguage,
        IReadOnlyList<string> languageRuleLines
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("너는 병렬 코딩 워커 초안 생성기다.");
        builder.AppendLine("실제 파일 수정/삭제/실행은 하지 말고 최종 통합용 초안만 작성하라.");
        builder.AppendLine($"언어 힌트: {resolvedLanguage}");
        builder.AppendLine();
        builder.AppendLine("[작업 목표]");
        builder.AppendLine(objective ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("규칙:");
        builder.AppendLine("- 가장 가능성 높은 구현안 1개만 제시");
        builder.AppendLine("- 불필요한 서론, 사과, 메타 설명 금지");
        builder.AppendLine("- 필요한 파일은 상대경로로 적기");
        builder.AppendLine("- 더미 코드, TODO, 의사코드 금지");
        builder.AppendLine("- 마지막에는 반드시 LANGUAGE=<언어> 줄과 최소 1개의 코드블록 포함");
        builder.AppendLine("- 여러 파일이 필요하면 `FILE: 상대경로` 줄 다음에 코드블록을 이어서 작성");
        foreach (var rule in languageRuleLines)
        {
            builder.AppendLine(rule);
        }

        builder.AppendLine();
        builder.AppendLine("출력 형식:");
        builder.AppendLine("[요약]");
        builder.AppendLine("- 핵심 구현 전략");
        builder.AppendLine("[파일]");
        builder.AppendLine("- 상대경로: 역할");
        builder.AppendLine("[리스크]");
        builder.AppendLine("- 남는 위험 또는 검증 포인트");
        builder.AppendLine("LANGUAGE=<언어>");
        builder.AppendLine("FILE: <핵심 파일 상대경로>");
        builder.AppendLine("```<언어>");
        builder.AppendLine("// 핵심 코드");
        builder.AppendLine("```");
        return builder.ToString().Trim();
    }

    public static string BuildOrchestrationAggregatePrompt(
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint
    )
    {
        return BuildParallelAggregatePrompt(
            "오케스트레이션 통합",
            input,
            workers,
            languageHint,
            "역할별 초안의 장점을 취합해 한 번에 완성도 높은 최종 구현으로 정리"
        );
    }

    public static string BuildMultiAggregatePrompt(
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint
    )
    {
        return BuildParallelAggregatePrompt(
            "다중 코딩 통합",
            input,
            workers,
            languageHint,
            "여러 모델 초안을 비교해 가장 안정적인 구현안을 선택하고 부족한 부분은 보완"
        );
    }

    public static string BuildMultiSummaryPrompt(
        string originalInput,
        IReadOnlyList<CodingWorkerSummaryDigest> workerDigests
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine($"원본 요청: {originalInput}");
        builder.AppendLine();
        builder.AppendLine("아래 각 모델의 독립 코딩 결과를 비교해 공통점과 차이를 정리하세요.");
        builder.AppendLine("출력 형식:");
        builder.AppendLine("[공통 요약]");
        builder.AppendLine("- 여러 결과를 한 번에 이해할 수 있는 짧은 요약");
        builder.AppendLine("[공통점]");
        builder.AppendLine("- 대부분 결과가 공통으로 만족한 구현/검증 포인트");
        builder.AppendLine("[차이]");
        builder.AppendLine("- 모델별로 갈린 구현 방식, 검증 결과, 리스크");
        builder.AppendLine("[추천]");
        builder.AppendLine("- 어떤 결과를 우선 볼지, 어떤 차이를 주의할지");
        builder.AppendLine();
        builder.AppendLine("규칙:");
        builder.AppendLine("- 실행 상태와 변경 파일, 검증 명령, 요약을 근거로 작성");
        builder.AppendLine("- 공통점이 거의 없으면 [공통점]에 '공통점 없음'이라고 적기");
        builder.AppendLine("- 차이가 거의 없으면 [차이]에 '의미 있는 차이 없음'이라고 적기");
        builder.AppendLine("- 추천은 2~4줄 이내로 간결하게 작성");
        builder.AppendLine();
        foreach (var worker in workerDigests)
        {
            builder.AppendLine($"[{worker.Provider}:{worker.Model}]");
            builder.AppendLine(worker.Digest);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildParallelAggregatePrompt(
        string modeLabel,
        string input,
        IReadOnlyList<CodingWorkerResult> workers,
        string languageHint,
        string strategyHint
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine($"다음은 {modeLabel} 단계에서 수집한 병렬 워커 초안들입니다.");
        builder.AppendLine($"요청: {input}");
        builder.AppendLine($"언어 힌트: {languageHint}");
        builder.AppendLine($"통합 전략: {strategyHint}");
        builder.AppendLine();
        foreach (var worker in workers)
        {
            builder.AppendLine($"[Worker {worker.Provider}:{worker.Model}]");
            builder.AppendLine($"status={worker.Execution.Status} exit={worker.Execution.ExitCode} language={worker.Language}");
            if (worker.ChangedFiles.Count > 0)
            {
                builder.AppendLine("changed_files=" + string.Join(", ", worker.ChangedFiles.Take(6)));
            }

            var draft = (worker.RawResponse ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(draft))
            {
                builder.AppendLine("draft:");
                builder.AppendLine(TrimForOutput(draft, 2200));
            }
            else if (!string.IsNullOrWhiteSpace(worker.Code))
            {
                builder.AppendLine("code:");
                builder.AppendLine(TrimForOutput(worker.Code, 2200));
            }

            builder.AppendLine();
        }

        builder.AppendLine("요구사항:");
        builder.AppendLine("1) 워커 초안을 참고하되 그대로 복붙하지 말고 충돌과 중복을 정리");
        builder.AppendLine("2) 실제 워크스페이스에는 필요한 최소 파일만 생성/수정");
        builder.AppendLine("3) 더미 구현, TODO만 남는 미완성 결과, 가짜 성공 보고 금지");
        builder.AppendLine("4) 최종 실행/검증까지 마칠 수 있는 현실적인 구현을 선택");
        return builder.ToString().Trim();
    }

    private static string TrimForOutput(string text, int limit)
    {
        var normalized = text ?? string.Empty;
        var safeLimit = Math.Max(200, limit);
        if (normalized.Length <= safeLimit)
        {
            return normalized;
        }

        return normalized[..safeLimit] + "...(truncated)";
    }
}
