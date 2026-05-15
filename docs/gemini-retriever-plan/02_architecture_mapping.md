# Gemini 검색 리트리버 전환 아키텍처 매핑

업데이트 기준: 2026-05-15

현재 상태: 이 문서는 Gemini 검색 전환 당시의 아키텍처 매핑이다. 최신 운영 기준은 상위 `docs/아키텍처_흐름.md`, `docs/도구_통합_패널_사용_가이드.md`, `docs/검증_가이드.md`를 우선 본다.

## 1. 매핑 원칙
- 검색과 생성을 분리한다.
- 채널별 중복 구현을 금지하고 공통 파이프라인으로 수렴한다.
- 근거 데이터 계약(Evidence Pack)을 경계 인터페이스로 고정한다.

## 2. 대상 코드베이스
- 코어 제어: `omninode-core`
- 메인 런타임: `omninode-middleware`
- UI: `omninode-dashboard`

## 3. 모듈 매핑표

| 기능군 | 대상 모듈 | 구현 방식 |
|---|---|---|
| 질의 프로파일링 | `CommandService` 경유 신규 프로파일러 | 시간민감도/리스크/응답유형 분류 |
| 검색 게이트웨이 | `SearchGateway` | 검색 인터페이스 표준화 |
| 검색 구현 | `GeminiGroundedRetriever` | Google Search grounding 호출 |
| 근거 정규화 | `EvidencePackBuilder` | 정규화/중복제거/시간검증/출처점수 |
| 출력 검증 | `AnswerGuard` | fail-closed + count-lock 판정 |
| 생성기 어댑터 | `ProviderAgnosticGeneratorAdapter` | 멀티 제공자 공통 입력 처리 |
| 채널 통합 | 대화/코딩/텔레그램 라우터 | 동일 근거 팩 주입 경로 |
| 키 정책 | `RuntimeSettings` + 루프 스크립트 | keychain/secure_file_600 강제 |

## 4. 데이터/상태 매핑

| 데이터 | 위치 | 설명 |
|---|---|---|
| 근거 팩 | 런타임 메모리 + 로그 | 요청별 표준 근거 묶음 |
| 검증 상태 | Answer Guard 결과 | freshness/credibility/coverage/countLock |
| 키 소스 정보 | 설정 스냅샷/로그 | 마스킹 + 소스 유형만 기록 |
| 루프 상태 | `loop-automation/runtime/state/*` | 단계/남은 항목/오류 관리 |

## 5. 채널 적용 순서
1. 대화 탭
2. 코딩 탭
3. 텔레그램 우회 경로
4. 텔레그램 수동 실회귀

## 6. 고정 인터페이스
- `SearchGateway`
- `GeminiGroundedRetriever`
- `EvidencePackBuilder`
- `AnswerGuard`
- `ProviderAgnosticGeneratorAdapter`
