# Gemini 검색 리트리버 전환 계획 문서

업데이트 기준: 2026-05-15

현재 상태:

- 이 디렉터리는 검색 파이프라인 설계/확장 계획 묶음이다.
- 실제 구현 기준은 `apps/omninode-middleware/src/CommandService.SearchPipeline.cs`, `GeminiGroundedRetriever.cs`, `SearchAnswerGuard.cs`, `DefaultSearchAnswerComposer.cs`를 우선 본다.
- 현재 운영 문서는 상위 `README.md`, `docs/아키텍처_흐름.md`, `docs/도구_통합_패널_사용_가이드.md`, `docs/검증_가이드.md`를 우선 본다.
- 이 하위 문서들은 검색 전환 당시의 설계/실행 기록 성격을 유지한다.

## 목적
Gemini 검색 리트리버 기반 구조를 Omni-node에 적용하기 위한 실행 문서 모음이다.
본 문서 세트는 기존 Omni-node UI/UX와 동작 방식을 유지한 상태에서 검색 신뢰성과 최신성 품질을 높이는 것을 목표로 한다.

## 핵심 전제
- 검색 경로는 `gemini-3.1-flash-lite-preview` + Google Search grounding 단일 경로로 운영한다.
- 최종 답변 생성은 `groq`, `gemini`, `copilot`, `cerebras` 멀티 제공자 경로를 유지한다.
- `Evidence Pack` 외부 사실 단정은 금지한다.
- `fail-closed`와 `count-lock` 정책을 기본 적용한다.
- 현재 운영의 시크릿 로딩 우선순위와 provider 범위는 상위 환경변수 문서와 `AppConfig.cs`를 기준으로 한다.

## 문서 목록
- [01_master_plan.md](./01_master_plan.md): 전체 목표, 범위, 단계, 완료 기준
- [02_architecture_mapping.md](./02_architecture_mapping.md): 아키텍처 및 모듈 매핑
- [03_rag_grounding_design.md](./03_rag_grounding_design.md): Gemini 검색 리트리버 + RAG 설계
- [04_provider_expansion.md](./04_provider_expansion.md): 멀티 생성기 제공자 확장 설계
- [05_feature_backlog.md](./05_feature_backlog.md): 기능군별 백로그
- [06_sprint_schedule.md](./06_sprint_schedule.md): 스프린트 일정과 산출물
- [07_execution_checklist.md](./07_execution_checklist.md): 단계별 실행 체크리스트
- [08_risk_and_quality.md](./08_risk_and_quality.md): 리스크, 품질, 운영 기준
- [09_release_gate_checklist.md](./09_release_gate_checklist.md): 릴리스 게이트 체크리스트
- [10_release_notes_template.md](./10_release_notes_template.md): 릴리스 노트 템플릿

## 사용 방법
1. `01_master_plan.md`에서 P0~P7 목표와 완료 기준을 고정한다.
2. `02_architecture_mapping.md` 기준으로 구현 책임 모듈을 확정한다.
3. `03_rag_grounding_design.md`를 검색/근거 파이프라인 기준으로 채택한다.
4. `05`, `06`, `07` 문서로 루프 운영을 진행한다.
5. `08`, `09`, `10` 문서로 릴리스 게이트를 통과한다.
