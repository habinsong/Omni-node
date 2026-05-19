namespace OmniNode.Middleware;

internal static class CodingOrchestrationPromptPolicy
{
    public static string BuildPlannerPrompt(string input, string language, string rolePrompt)
    {
        return $"""
                아래 요청을 오케스트레이션 코딩의 기획 단계로 정리하세요.
                언어 힌트: {language}
                역할: {rolePrompt}

                목표:
                1) 구현 범위와 핵심 파일 후보 정리
                2) 먼저 만들 것, 나중에 검증할 것, 위험한 가정 정리
                3) 실행/검증/테스트에서 볼 포인트 정리

                규칙:
                - 코드 본문을 길게 쓰지 말고 실무 메모처럼 간결하게 정리
                - 구현 단계가 바로 사용할 수 있게 파일/검증 포인트를 구체적으로 적기
                - 한국어로 작성

                원본 요청:
                {input}
                """;
    }

    public static string BuildImplementationPrompt(
        string input,
        string language,
        string rolePrompt,
        CodingWorkerResult planningResult
    )
    {
        return $"""
                오케스트레이션 코딩의 개발 단계입니다.
                언어 힌트: {language}
                역할: {rolePrompt}

                [기획 메모]
                {planningResult.Summary}

                규칙:
                - 처음부터 끝까지 실제로 동작하는 구현을 완성
                - 필요한 파일 생성/수정, 실행, 검증, 테스트까지 직접 수행
                - 더미 구현, TODO, 미완성 보고 금지

                원본 요청:
                {input}
                """;
    }

    public static string BuildVerificationPrompt(
        string input,
        string language,
        string rolePrompt,
        CodingWorkerResult planningResult,
        CodingWorkerResult implementationResult
    )
    {
        return $"""
                오케스트레이션 코딩의 검증 및 테스트 단계입니다.
                언어 힌트: {language}
                역할: {rolePrompt}

                [기획 메모]
                {planningResult.Summary}

                [개발 단계 요약]
                {implementationResult.Summary}

                목표:
                1) 현재 작업 폴더 기준으로 다시 실행/검증/테스트
                2) 실패 원인이 보이면 최소 수정까지 포함해 보정 가능
                3) 남는 리스크와 확인 결과를 실제 상태 기준으로 정리

                원본 요청:
                {input}
                """;
    }

    public static string BuildFixPrompt(
        string input,
        string language,
        string rolePrompt,
        CodingWorkerResult planningResult,
        CodingWorkerResult implementationResult,
        CodingWorkerResult verificationResult
    )
    {
        return $"""
                오케스트레이션 코딩의 수정 단계입니다.
                언어 힌트: {language}
                역할: {rolePrompt}

                [기획 메모]
                {planningResult.Summary}

                [개발 단계 요약]
                {implementationResult.Summary}

                [검증 단계 요약]
                {verificationResult.Summary}

                목표:
                1) 검증 단계에서 남은 오류, 실패, 누락 요구사항 해결
                2) 필요한 수정 후 다시 실행/검증 마무리
                3) 최종 상태를 실제 결과 기준으로 요약

                원본 요청:
                {input}
                """;
    }

    public static string BuildMultiWorkerPrompt(string input, string language)
    {
        return $"""
                너는 다중 코딩 모드의 독립 실행 모델이다.
                언어 힌트: {language}

                규칙:
                - 다른 모델과 협의하지 않고 처음부터 끝까지 직접 완성
                - 필요한 파일 생성/수정, 실행, 검증, 테스트, 수정 반복까지 스스로 처리
                - 더미 구현, TODO, 가짜 성공 보고 금지
                - 최종적으로 실제 작업 폴더에 남은 결과만 기준으로 요약

                원본 요청:
                {input}
                """;
    }

    public static string BuildAutoInput(string language)
    {
        var targetLanguage = string.IsNullOrWhiteSpace(language) ? "auto" : language;
        return $"""
                [자동 오케스트레이션 코딩 모드]
                사용자가 입력 없이 실행했습니다.
                워커 모델들이 역할을 스스로 협의해 작업을 분배하고 병렬 실행하세요.
                목표:
                1) 워크스페이스를 점검하고 실행 가능한 개선 작업 1건 수행
                2) 변경 파일 생성/수정
                3) 실행/검증 후 결과 요약
                언어 힌트: {targetLanguage}
                """;
    }
}
