# Omni-node 개발 현황 분석

최종 업데이트: 2026-05-18

## 기준

- 이 문서는 현재 작업트리 기준 분석이다.
- 코드 변경 이력 요약이 아니라, 다음 개발 판단을 위한 최신 리스크와 우선순위를 기록한다.
- 저장소의 현재 방향은 로컬 우선 AI 워크벤치다. 웹 대시보드와 텔레그램 봇이 같은 미들웨어 명령 계층을 통과하고, 작업 산출물은 `workspace/`, 영속 상태는 `~/.omninode`에 둔다.

## 현재 상태 요약

- `apps/omninode-core`
  - C11 코어 데몬.
  - 단일 인스턴스, 로컬 IPC, metrics, 제한된 kill 명령을 담당한다.
  - 현재 core IPC는 auth token을 요구하고, `get_metrics`, `kill <pid>` 고정 line protocol만 처리한다.
- `apps/omninode-middleware`
  - .NET 9 서버.
  - WebSocket/HTTP, 텔레그램, LLM provider 라우팅, 상태 저장, 코딩, 루틴, 로직 그래프, 계획, task graph, 노트북, Safe Refactor를 담당한다.
  - 실질적인 제품 핵심은 이 계층에 집중되어 있다.
- `apps/omninode-dashboard`
  - 번들러 없는 정적 HTML/CSS/JavaScript 대시보드.
  - 화면 모듈은 많이 나뉘었지만, `app.js`가 여전히 큰 셸 상태를 들고 있다.
- `apps/omninode-sandbox`
  - Python 실행 제한기.
  - timeout, memory, CPU 제한은 있으나 파일시스템, 네트워크, 환경변수 격리를 제공하는 보안 샌드박스는 아니다.
- `workspace/`
  - 코딩, 루틴, 로직, task graph 실행 산출물 위치.
- `~/.omninode`
  - 대화, 세션, 계획, 라우팅 정책, 노트북, 스킬, 사용량 등 영속 상태 위치.

## 최근 안정화 상태

최근 개발 흐름에서 다음 보강이 들어간 상태다.

- 외부 대시보드 제한 모드 allowlist 정리.
- 원격 제한 모드에서 실행 계열 메시지 차단.
- WebSocket Origin 정책 분리.
- 첨부 개수와 크기 제한.
- dynamic code 기본 비활성.
- 자동 의존성 설치 기본 비활성.
- 상태 JSON `.lock`, `.bak`, 대표 저장소 복구 경로.
- memory index sync 관측성.
- 대시보드 정적 파일 byte 응답, `ETag`, `Last-Modified`, 조건부 `304 Not Modified`.
- C core auth token과 고정 command protocol.

## 검증 기준

현재 기준 검증 명령은 아래와 같다.

```bash
dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj
dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj
node scripts/check-security-boundaries.mjs
npm test
make -C apps/omninode-core -B
git diff --check
```

최근 확인 결과:

- `dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj`: 통과, 경고 0
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj`: 통과, 10 tests
- `node scripts/check-security-boundaries.mjs`: 통과, assertions 101
- `npm test`: 통과
- `make -C apps/omninode-core -B`: 통과
- `git diff --check`: 통과

## 핵심 리스크

### 1. 미들웨어 중심축 과대화

상태: 높음

근거:

- `CommandService.*` 전체가 매우 크다.
- partial 파일로 나뉘었지만 실제 도메인 책임은 여전히 한 타입에 많이 묶여 있다.
- `CommandService.Utils.cs`, `CommandService.Telegram.cs`, `CommandService.SearchPipeline.cs`, `CommandService.LogicGraphs.cs`, `CommandService.Chat.cs`, `CommandService.RoutineManagement.cs`, `CommandService.Coding.cs`가 각각 큰 기능 단위를 직접 들고 있다.

영향:

- 기능 추가 시 충돌 가능성이 높다.
- 작은 수정도 여러 도메인에 부수효과를 만들 수 있다.
- 테스트를 도메인 단위로 고립하기 어렵다.

권장 방향:

- 검색, 텔레그램, 코딩 실행, 루틴, 로직 그래프, provider routing을 실제 service 단위로 점진 분리한다.
- `CommandService`는 orchestrator와 호환 facade 역할만 남긴다.
- 한 번에 대규모 분해하지 말고, 새 변경이 생기는 도메인부터 service extraction을 적용한다.

### 2. `LlmRouter` provider 책임 집중

상태: 높음

근거:

- `LlmRouter.cs`가 provider 호출, streaming 처리, usage 추적, STT, intent classification, fallback 관련 책임을 함께 들고 있다.
- Gemini, Groq, Cerebras, NVIDIA NIM, Copilot/Codex 흐름이 점점 늘어나는 구조다.

영향:

- provider별 timeout, quota, streaming, error 변환 정책이 꼬이기 쉽다.
- 신규 provider 추가나 기존 provider 정책 변경의 회귀 범위가 넓다.

권장 방향:

- provider adapter 인터페이스를 두고 provider별 호출 구현을 분리한다.
- usage tracking과 응답 normalization을 별도 정책 객체로 뺀다.
- provider별 timeout floor와 fallback 정책을 명시적 설정 모델로 모은다.

### 3. WebSocket gateway와 dispatch 경계 복잡도

상태: 중간-높음

근거:

- `WebSocketGateway.cs`와 `WebSocketGateway.SocketLoop.cs`가 인증, remote limited 정책, rate limit, metrics stream, dispatcher 순서, 오류 응답을 함께 조정한다.
- dispatcher 파일은 나뉘었지만, dispatch 순서 자체가 중요한 보안 정책이 되어 있다.

영향:

- 새 message type 추가 시 remote limited allowlist, auth guard, dispatcher 위치를 놓치기 쉽다.
- 문자열 기반 contract test가 보강하고 있지만 실제 runtime 통합 테스트는 부족하다.

권장 방향:

- message type registry를 명시화한다.
- 각 message type에 auth level, remote permission, dispatcher를 선언형으로 연결한다.
- remote limited, unauthenticated, authenticated 경로를 실제 WebSocket 통합 테스트로 검증한다.

### 4. 프런트엔드 셸 상태 과대화

상태: 중간-높음

근거:

- `apps/omninode-dashboard/app.js`가 대시보드 셸과 많은 상태 연결을 들고 있다.
- 모듈은 많지만 최상위 상태와 이벤트 wiring은 여전히 큰 파일에 집중되어 있다.

영향:

- 탭 하나 수정이 전체 대시보드 상태에 영향을 줄 수 있다.
- 모바일 composer, tool 결과, provider runtime, guard timeline 같은 화면 상태가 계속 누적되면 유지보수 비용이 커진다.

권장 방향:

- 탭별 controller를 분리한다.
- `app.js`는 bootstrapping, 공통 상태 주입, router wiring만 담당하게 줄인다.
- 이미 있는 `dashboard-server-message-router.mjs` 같은 분리 방식을 다른 대형 영역에도 반복 적용한다.

### 5. 샌드박스와 동적 실행 경계

상태: 중간

근거:

- `apps/omninode-sandbox/executor.py`는 timeout, memory, CPU 제한만 제공한다.
- 명시적 로컬 코드 실행은 `OMNINODE_ENABLE_DYNAMIC_CODE=true`에서만 허용되도록 보강되어 있다.
- 자동 설치도 `OMNINODE_ENABLE_AUTO_INSTALL=true`에서만 동작한다.

영향:

- dynamic code를 켠 사용자는 여전히 로컬 파일, 네트워크, 환경변수 접근 위험을 감수해야 한다.
- 자동 설치를 켠 경우 typosquatting, 악성 package, 재현성 저하 위험이 남는다.

권장 방향:

- dynamic code를 켜는 모드에 별도 경고와 doctor check를 추가한다.
- 자동 설치 allowlist 또는 dry-run preview를 추가한다.
- 장기적으로는 네트워크 차단, read-only mount, env scrub 같은 실행 격리 옵션을 검토한다.

### 6. 상태 저장소 적용 범위

상태: 중간

근거:

- 대표 JSON 저장소에는 `.lock`, `.bak`, 복구 경로가 들어갔다.
- 모든 JSON 읽기 경로가 같은 복구 정책을 쓰는 것은 아니다.
- usage 계열과 일부 읽기 전용 tool 상태는 후속 정리 대상이다.

영향:

- 장기 운영 중 일부 상태 파일 손상은 여전히 수동 복구가 필요할 수 있다.
- doctor 관측은 강화되었지만 자동 복구 범위가 저장소마다 다르다.

권장 방향:

- JSON 상태 저장소 목록을 작성하고 AtomicFileStore 적용 여부를 표로 관리한다.
- usage 계열부터 backup recovery를 확대한다.
- 상태 스키마 versioning과 migration 정책을 정리한다.

### 7. 테스트 성격의 한계

상태: 중간

근거:

- `npm test`는 넓은 범위를 빠르게 확인한다.
- 다만 상당수 계약 검사가 소스 문자열 포함 여부에 기반한다.
- 실제 HTTP/WebSocket runtime 행위 테스트는 상대적으로 부족하다.

영향:

- 보안 정책이 문자열상 존재해도 runtime wiring이 잘못되면 놓칠 수 있다.
- dispatcher 순서, auth 상태, remote limited 상태 같은 행위는 실제 연결 테스트가 더 적합하다.

권장 방향:

- 최소 WebSocket integration test harness를 추가한다.
- Origin 없음, 잘못된 Origin, remote limited 실행 차단, local authenticated 실행 허용을 실제 서버 기준으로 검증한다.
- `/healthz`, `/readyz`, 정적 파일 `304`, `/api/local-image`도 HTTP integration test로 옮긴다.

## 문서 불일치

### 1. 원격 제한 모드의 로직 그래프 정책

상태: 수정 필요

현재 코드와 최신 주요 문서 기준:

- 원격 제한 모드는 읽기 중심 조회와 모델/라우팅 일부만 허용한다.
- 대화, 코딩, 루틴, 로직 그래프, task graph, refactor, tool 실행은 차단한다.

불일치 파일:

- `docs/검증_가이드.md`
  - 보안 경계 항목에 “외부 로직 그래프 동작 허용”이라고 남아 있다.
- `docs/en/usage.md`
  - remote limited 설명에서 chat, coding, routines, logic graphs, notebooks, plans 등이 available로 적혀 있다.
- `docs/OMNINODE_실환경_수동_최종회귀_체크리스트.md`
  - 외부 클라이언트에서 로직 그래프 목록/열기/저장/삭제/실행/취소/결과 조회 허용으로 남아 있다.

권장 수정:

- 원격 제한 모드 문구를 `README.md`, `docs/아키텍처_흐름.md`, `docs/QUICKSTART.md`, `docs/en/architecture.md`, `docs/en/quickstart.md`와 맞춘다.
- 실행 가능/불가능을 표로 통일한다.
- 수동 회귀 체크리스트는 “외부 로직 그래프 실행 차단 확인”으로 바꾼다.

### 2. 문서 업데이트 날짜

상태: 점검 필요

근거:

- 여러 문서의 업데이트 기준이 2026-05-15로 남아 있다.
- 실제 정책 변경은 2026-05-18 기준으로 진행되었다.

권장 수정:

- 정책 문서만 우선 2026-05-18로 갱신한다.
- 단순 날짜 일괄 변경보다, 실제 내용이 최신 정책을 반영하는 문서만 수정한다.

## 우선순위

### P0. 현재 작업트리 커밋

목표:

- 현재 안정화 작업을 하나의 기준점으로 고정한다.
- 이후 문서 정리와 구조 개선을 분리된 변경으로 다룬다.

완료 기준:

- 전체 작업트리가 staged 상태가 된다.
- 커밋이 생성된다.
- 커밋 후 `git status --short`가 깨끗해야 한다.

### P1. 문서 불일치 정리

목표:

- remote limited 정책을 모든 사용자 문서, 검증 문서, 수동 체크리스트에 일관되게 반영한다.

대상:

- `docs/검증_가이드.md`
- `docs/en/usage.md`
- `docs/OMNINODE_실환경_수동_최종회귀_체크리스트.md`
- 필요 시 `docs/en/manual-regression-checklist.md`

검증:

- `rg -n "외부 로직 그래프|logic graph.*available|로직 그래프.*허용|logic graph.*blocked|로직 그래프 실행" docs README.md`
- `npm test`
- `git diff --check`

### P2. WebSocket runtime 통합 테스트 추가

목표:

- 문자열 계약이 아니라 실제 listener와 WebSocket 연결 기준으로 보안 정책을 검증한다.

우선 케이스:

- 로컬 Origin 없는 WebSocket 허용.
- 원격 Origin 없는 WebSocket 거부는 테스트 환경 설계 필요.
- 잘못된 Origin 거부.
- remote limited에서 실행 message type 거부.
- remote limited에서 read-only message type 허용.
- 미인증 상태에서 protected message type 거부.

검증:

- 새 integration test script를 `npm test`에 포함한다.

### P3. `CommandService` 도메인 분리 시작

목표:

- 새 기능을 붙일 때마다 `CommandService`가 더 커지는 흐름을 끊는다.

첫 후보:

- search pipeline service
- telegram command service
- coding execution service
- routine scheduler/service
- logic graph command service

원칙:

- 외부 API 계약을 바꾸지 않는다.
- 먼저 read-only 또는 pure helper 성격부터 옮긴다.
- 각 추출마다 기존 contract test를 유지하고 필요한 단위 테스트를 추가한다.

### P4. Provider adapter 구조 정리

목표:

- `LlmRouter`를 provider 호출 구현체와 routing/fallback 조정자로 분리한다.

첫 후보:

- Groq adapter
- Gemini adapter
- Cerebras adapter
- NVIDIA adapter
- STT adapter

완료 기준:

- provider별 timeout, error normalization, usage 추적 책임이 명확해진다.
- 신규 provider 추가 시 `LlmRouter` 본문 변경량이 줄어든다.

### P5. 상태 저장소 복구 정책 확대

목표:

- JSON 상태 파일의 lock/backup/recovery 적용 범위를 명확히 한다.

작업:

- 상태 파일 inventory 작성.
- AtomicFileStore 적용 여부 표시.
- usage 계열 상태 복구 정책 추가.
- doctor detail에 파일별 위험을 더 구체화.

## 운영 판단

- 이 프로젝트는 기능적으로는 이미 개인 운영 도구 이상의 범위를 갖췄다.
- 단기 품질은 보안 경계와 contract test가 받치고 있다.
- 중기 리스크는 거대한 중심 타입과 문서 drift다.
- 다음 개발은 새 기능 확장보다 문서 일관성, runtime 통합 테스트, 도메인 분리 순서가 더 안전하다.
