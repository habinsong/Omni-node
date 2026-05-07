# 멀티 생성기 제공자 확장 설계

업데이트 기준: 2026-05-07

현재 상태: 이 문서는 Gemini 검색 전환 당시의 provider 확장 계획이다. 최신 운영 기준은 상위 `docs/아키텍처_흐름.md`, `docs/도구_통합_패널_사용_가이드.md`, `docs/검증_가이드.md`를 우선 본다.

## 1. 목표
- 검색은 단일 리트리버로 통일하고, 생성은 멀티 제공자를 유지한다.
- 모든 생성기는 동일한 Evidence Pack 입력 계약을 사용한다.

## 2. 공통 인터페이스
```csharp
public interface IGeneratorProviderClient
{
    string ProviderId { get; }
    Task<ProviderChatResult> GenerateAsync(GeneratorRequest request, CancellationToken ct);
    Task<IReadOnlyList<ProviderModelInfo>> ListModelsAsync(CancellationToken ct);
}
```

## 3. 제공자 구성
- Groq
- Gemini
- Copilot
- Cerebras

## 4. 생성기 규칙
1. Evidence Pack 외부 사실 단정 금지
2. 핵심 주장별 citationId 매핑
3. coverage/count-lock 실패 시 건수 억지 채우기 금지
4. 미검증 영역은 확인 불가로 표시

## 5. 모드별 동작
- 단일: 지정 제공자 1개 사용
- 오케스트레이션: 집계자 1개 + 필요 시 워커 다수
- 다중: 병렬 생성 후 요약 제공자 통합

## 6. 폴백 정책
- 제공자 장애 시 나머지 제공자로 진행
- 근거 검증 실패는 제공자 폴백으로 해결하지 않고 재수집 경로를 우선 적용

## 7. 완료 기준
- 4개 제공자가 동일 계약으로 응답 생성 가능
- 단일/오케스트레이션/다중 모드 회귀 통과
