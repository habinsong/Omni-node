# Gemini 검색 리트리버 전환 마스터 계획

업데이트 기준: 2026-05-07

현재 상태: 이 문서는 Gemini 검색 전환 당시의 실행 계획서다. 최신 운영 기준은 상위 `docs/아키텍처_흐름.md`, `docs/도구_통합_패널_사용_가이드.md`, `docs/검증_가이드.md`를 우선 본다.

## 1. 목표
Omni-node에 검색-생성 분리 구조를 적용해 다음을 제공한다.

- Gemini grounding 기반 최신 정보 검색
- Evidence Pack 기반 근거 표준화
- Answer Guard 기반 fail-closed 출력 제어
- 멀티 생성기(`groq`, `gemini`, `copilot`, `cerebras`) 유지
- 대화/코딩/텔레그램 경로 공통 파이프라인

## 2. 범위

### 포함
- SearchGateway/GeminiGroundedRetriever 도입
- Evidence Pack Builder 및 Answer Guard 도입
- count-lock 기반 건수 충족 정책 도입
- 설정 탭 저장 Gemini 키 강제 정책 적용
- 루프 자동화/체크리스트/릴리스 문서 동기화

### 제외
- 비근거 단정형 응답 허용
- 환경변수 키 직접 주입 운영
- 채널별 별도 검색 파이프라인 분기

## 3. 완료 기준
- 대화/코딩/텔레그램 경로가 동일 검색 파이프라인을 사용한다.
- 요청 건수 N에 대해 최종 출력 건수 N이 유지된다(count-lock).
- 근거 부족/최신성 실패 시 fail-closed가 동작한다.
- 테스트/검증/회귀/실행 경로에서 설정 탭 저장 Gemini 키만 사용한다.

## 4. 작업 원칙
- 인터페이스를 먼저 고정하고 구현을 채운다.
- 근거 구조(Evidence Pack)를 응답보다 우선한다.
- 단계별 승급(P0~P7)은 완료 조건 충족 시에만 수행한다.
- 로그에는 키 평문을 기록하지 않는다.

## 5. 단계별 실행 계획

| 단계 | 목표 | 주요 작업 | 산출물 | 완료 조건 |
|---|---|---|---|---|
| P0 | 기반 고정 | SearchGateway, 키 정책 타입, 공통 DTO 정의 | 인터페이스/정책 명세 | 구현 착수 가능한 상태 |
| P1 | 검색 리트리버 | GeminiGroundedRetriever, 질의 프로파일러 | 검색 모듈 | 기본 검색/정규화 성공 |
| P2 | 근거 팩 | Evidence Pack Builder, 인용 스키마 | 근거 데이터 계약 | 대화/코딩 주입 가능 |
| P3 | 출력 게이트 | Answer Guard, fail-closed, count-lock | 게이트 모듈 | 부정확 응답 차단 동작 |
| P4 | 멀티 생성기 연결 | ProviderAgnosticGeneratorAdapter | 생성기 어댑터 | 4개 생성기 공통 동작 |
| P5 | 채널 통합 | 대화/코딩/텔레그램 공통 경로 적용 | 채널 라우팅 반영 | 경로별 회귀 통과 |
| P6 | 운영 관측 | 로그/메트릭/릴리스 게이트 확장 | 운영 문서/스크립트 | 운영 점검 가능 |
| P7 | 안정화 | 성능/품질/복구 기준 확정 | 최종 릴리스 자료 | 배포 가능 상태 |

## 6. 의사결정 고정 사항
- 검색은 Gemini grounding 단일 경로로 운영한다.
- 생성은 멀티 제공자 경로를 유지한다.
- `GeminiKeySource = keychain | secure_file_600` 외 값은 차단한다.
- `GeminiKeyRequiredFor = test, validation, regression, production_run`를 강제한다.
- 금칙어 문자열은 저장소 내 0건을 유지한다.
