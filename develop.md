# Omni-node 개발 현황 분석

최종 업데이트: 2026-05-19

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
- 상태 JSON `.lock`, `.bak`, 대표 저장소 복구 경로와 상태 저장소 inventory.
- memory index sync 관측성.
- 대시보드 정적 파일 byte 응답, `ETag`, `Last-Modified`, 조건부 `304 Not Modified`.
- C core auth token과 고정 command protocol.

## 검증 기준

현재 기준 검증 명령은 아래와 같다.

```bash
dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj
dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj
node scripts/check-security-boundaries.mjs
node scripts/check-coding-python-game-contract.mjs
node scripts/check-chat-telegram-contract.mjs
node scripts/check-gateway-runtime-contract.mjs
npm test
make -C apps/omninode-core -B
git diff --check
```

최근 확인 결과:

- `dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj`: 통과, 경고 0
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj`: 통과, 494 tests
- `node scripts/check-security-boundaries.mjs`: 통과, assertions 505
- `node scripts/check-coding-python-game-contract.mjs`: 통과, assertions 106
- `node scripts/check-chat-telegram-contract.mjs`: 통과
- `node scripts/check-gateway-runtime-contract.mjs`: 통과
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
- `llm_usage.json`, `copilot_usage.json`, `routing-policy.json`, `guard_retry_timeline.json` 계열은 `.bak` 복구 로더를 통과한다.
- 일부 읽기 전용 tool 상태는 후속 정리 대상이다.

영향:

- 장기 운영 중 일부 상태 파일 손상은 여전히 수동 복구가 필요할 수 있다.
- doctor 관측은 강화되었지만 자동 복구 범위가 저장소마다 다르다.

권장 방향:

- JSON 상태 저장소 목록을 작성하고 AtomicFileStore 적용 여부를 표로 관리한다.
- 남은 JSON 상태 저장소의 recovery 적용 여부를 inventory로 관리한다.
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

상태: 완료

현재 코드와 최신 주요 문서 기준:

- 원격 제한 모드는 읽기 중심 조회와 모델/라우팅 일부만 허용한다.
- 대화, 코딩, 루틴, 로직 그래프, task graph, refactor, tool 실행은 차단한다.

확인된 불일치 파일:

- `docs/검증_가이드.md`
  - 보안 경계 항목에 외부 로직 그래프 동작 허용 기준이 남아 있었다.
- `docs/en/usage.md`
  - remote limited 설명에서 chat, coding, routines, logic graphs, notebooks, plans 등이 available 기준으로 적혀 있었다.
- `docs/OMNINODE_실환경_수동_최종회귀_체크리스트.md`
  - 외부 클라이언트에서 로직 그래프 목록/열기/저장/삭제/실행/취소/결과 조회 허용 기준이 남아 있었다.
- `docs/en/manual-regression-checklist.md`
  - remote logic graph actions available 기준이 남아 있었다.
- `docs/en/validation.md`
  - 원격 로직 그래프 동작을 허용한다는 영문 기준이 남아 있었다.
- `README.en.md`
  - remote dashboard에서 chat/coding/routines/logic graphs가 available로 적혀 있었다.

완료 내용:

- 원격 제한 모드 문구를 `README.md`, `docs/아키텍처_흐름.md`, `docs/QUICKSTART.md`, `docs/en/architecture.md`, `docs/en/quickstart.md`와 맞춘다.
- 실행 가능/불가능을 표로 통일한다.
- 수동 회귀 체크리스트는 “외부 로직 그래프 실행 차단 확인”으로 바꾼다.

### 2. 문서 업데이트 날짜

상태: 진행 중

근거:

- 여러 문서의 업데이트 기준이 2026-05-15로 남아 있다.
- 실제 정책 변경은 2026-05-18 기준으로 진행되었다.

권장 수정:

- 정책 문서만 우선 2026-05-18로 갱신한다.
- 단순 날짜 일괄 변경보다, 실제 내용이 최신 정책을 반영하는 문서만 수정한다.

## 우선순위

### P0. 현재 작업트리 커밋

상태: 완료

목표:

- 현재 안정화 작업을 하나의 기준점으로 고정한다.
- 이후 문서 정리와 구조 개선을 분리된 변경으로 다룬다.

완료 기준:

- 전체 작업트리가 staged 상태가 된다.
- 커밋이 생성된다.
- 커밋 후 `git status --short`가 깨끗해야 한다.

완료 내용:

- `e1ef479 chore: stabilize Omni-node runtime boundaries` 커밋으로 안정화 변경과 기존 분석 문서를 기준점으로 고정했다.

### P1. 문서 불일치 정리

상태: 완료

목표:

- remote limited 정책을 모든 사용자 문서, 검증 문서, 수동 체크리스트에 일관되게 반영한다.

대상:

- `docs/검증_가이드.md`
- `docs/en/usage.md`
- `docs/OMNINODE_실환경_수동_최종회귀_체크리스트.md`
- 필요 시 `docs/en/manual-regression-checklist.md`

진행 내용:

- `docs/검증_가이드.md`
  - 외부 로직 그래프 동작 허용 문구를 외부 실행 차단과 읽기/모델/라우팅 허용 기준으로 교체했다.
- `docs/en/usage.md`
  - remote limited mode에서 logic graph 실행이 가능하다는 설명을 차단 기준으로 교체했다.
- `docs/OMNINODE_실환경_수동_최종회귀_체크리스트.md`
  - 외부 로직 그래프 실행 허용 체크를 외부 실행 차단 체크로 교체했다.
- `docs/en/manual-regression-checklist.md`
  - remote logic graph actions available 문구를 remote execution blocked 기준으로 교체했다.
- `README.en.md`
  - remote dashboard 설명을 한국어 README와 같은 제한 모드 기준으로 맞췄다.
- `docs/en/validation.md`
  - 원격 로직 그래프 동작 허용 문구를 remote execution blocked와 read/model/routing allowed 기준으로 교체했다.
- `apps/omninode-dashboard/modules/dashboard-settings-renderers.js`
  - 외부 접속 제한 모드 패널을 읽기 중심 조회/모델/라우팅 허용, 실행 계열 차단 기준으로 교체했다.
- `scripts/check-security-boundaries.mjs`
  - 대시보드 설정 패널이 최신 제한 모드 정책을 설명하는지 계약 검사를 추가했다.
  - 원격 제한 모드 allowlist가 별도 policy로 분리되어 있는지 확인한다.
- `apps/omninode-middleware/src/RemoteLimitedMessagePolicy.cs`
  - read-oriented message allowlist를 `WebSocketGateway` 내부 private 분기에서 분리했다.
- `apps/omninode-middleware-tests/RemoteLimitedMessagePolicyTests.cs`
  - 원격 제한 모드에서 허용되는 읽기 메시지와 차단되는 실행/인증 메시지를 단위 테스트로 고정했다.

검증 결과:

- `node scripts/check-security-boundaries.mjs`: 통과, assertions 117
- `git diff --check`: 통과

검증:

- `rg -n "외부 로직 그래프|logic graph.*available|로직 그래프.*허용|logic graph.*blocked|로직 그래프 실행" docs README.md`
- `npm test`
- `git diff --check`

### P2. WebSocket runtime 통합 테스트 추가

상태: 완료

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

진행 내용:

- `scripts/check-gateway-runtime-contract.mjs`
  - 임시 포트와 임시 HOME/워크스페이스로 실제 미들웨어를 기동한다.
  - `/healthz` 응답을 확인한다.
  - 로컬 loopback WebSocket의 Origin 없는 handshake가 `101`로 허용되는지 확인한다.
  - 로컬 loopback WebSocket에서 인증 전 보호 메시지가 `unauthorized`로 거부되는지 확인한다.
  - 잘못된 Origin handshake가 `403`으로 거부되는지 확인한다.
  - 테스트 환경에 비 loopback IPv4가 있으면 외부 대시보드 모드로 listener를 띄우고, 비 loopback endpoint 접속을 실제 remote client로 재현한다.
  - 비 loopback remote WebSocket의 Origin 없는 handshake가 `403`으로 거부되는지 확인한다.
  - 비 loopback remote WebSocket이 맞는 Origin으로 접속하면 OTP 없이 `remoteLimited` 인증 상태로 진입하는지 확인한다.
  - remote limited에서 `list_conversations` read-only 메시지가 허용되는지 확인한다.
  - remote limited에서 `llm_chat_single` 실행 메시지가 `forbidden_remote_limited_action`으로 차단되는지 확인한다.
  - WebSocket `ping`/`pong` round-trip 뒤 `/readyz`가 `200`이 되는지 확인한다.
  - 대시보드 index 정적 파일이 `ETag`, `Last-Modified`, 조건부 `304 Not Modified`를 제공하는지 확인한다.
- `scripts/run-omninode-tests.mjs`
  - `npm test`에 `gateway runtime contract` 단계를 추가했다.
- `apps/omninode-middleware/src/Program.cs`
  - gateway runtime test가 기존 사용자 core daemon이나 memory index scan 비용에 영향받지 않도록 `OMNINODE_SKIP_CORE_BOOTSTRAP`, `OMNINODE_SKIP_MEMORY_INDEX_BOOTSTRAP` 테스트용 skip 경로를 추가했다.

검증 결과:

- `node scripts/check-gateway-runtime-contract.mjs`: 통과
- `dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj`: 통과, 경고 0
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj`: 통과, 63 tests
- `node scripts/check-security-boundaries.mjs`: 통과, assertions 135
- `npm test`: 통과
- `make -C apps/omninode-core -B`: 통과
- `git diff --check`: 통과

추가 검증 결과:

- `node scripts/check-gateway-runtime-contract.mjs`: 통과
  - `websocket_no_origin_remote_reject`
  - `remote_limited_auto_auth`
  - `remote_limited_read_only_allow`
  - `remote_limited_execution_block`

### P3. `CommandService` 도메인 분리 시작

상태: 진행 중

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

진행 내용:

- `RemoteLimitedMessagePolicy`를 추가해 remote limited allowlist를 gateway loop 내부에서 분리했다.
- 이 변경은 아직 `CommandService` 분리는 아니지만, P2에서 지적한 message policy 선언화의 첫 단계다.
- `apps/omninode-middleware/src/Infrastructure/Search/SearchQueryPolicy.cs`
  - `CommandService.SearchPipeline`에 있던 검색 필요성 판단, source focus/domain hint 추출, search freshness/count 결정, list/table/comparison/local-time/casual query 판정, fast requirement decision 조립을 별도 정책 클래스로 분리했다.
  - 웹 검색 메모리 선호 힌트 추출, 중복 키 정규화, 일회성 override 차단, format/tone/language directive 판정, 기본 뉴스/목록 개수 계산도 같은 정책 클래스로 이동했다.
  - `CommandService.SearchPipeline`에는 기존 partial 호출부 호환을 위한 얇은 wrapper만 남겼다.
  - `CommandService.Citations`의 list/table 판정도 같은 `SearchQueryPolicy`를 사용하도록 정리했다.
  - `CommandService.SearchPipeline.cs` 본문 크기: 4159 → 3276 라인.
- `apps/omninode-middleware-tests/SearchQueryPolicyTests.cs`
  - fast requirement decision, source hint 추출, LLM JSON decision 파싱, decision token 정규화, 요청 count/freshness, effective query 조립, local date/time false positive, 웹 선호 힌트/override/directive/default count 정책을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Search/SearchUrlContextPolicy.cs`
  - URL-only 요청 기본 의도 해석, site/docs/article/repository URL 판정, GitHub 저장소 root 파싱, GitHub embedded README 정보 추출, richText → plain text 변환, README 관련 발췌/직접 답변 생성을 `CommandService.SearchPipeline`에서 분리했다.
  - 네트워크 호출(`WebFetchClient`로 GitHub HTML/Raw README 읽기)은 기존 `CommandService.SearchPipeline`에 남기고, 순수 판정·파싱·발췌 로직만 정책 클래스로 이동했다.
  - `CommandService.SearchPipeline.cs` 본문 크기: 3276 → 2705 라인.
- `apps/omninode-middleware-tests/SearchUrlContextPolicyTests.cs`
  - GitHub 저장소 URL 파싱, repository action URL 거부, docs URL 판정, URL-only 요청 기본 프롬프트, GitHub richText 정규화, README 주변 문맥 발췌, README 직접 답변 생성을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Search/SearchPromptPolicy.cs`
  - 웹 필요성 판단 프롬프트, Gemini grounded/url-context 답변 프롬프트, Gemini 답변 토큰 예산, Gemini 실패 문구 판정/사용자 안내, need-web JSON 파싱을 `CommandService.SearchPipeline`에서 분리했다.
  - `CommandService.SearchPipeline`은 모델 호출, URL/README 네트워크 fetch, memory note 읽기, 기존 partial 호환 wrapper에 집중하도록 축소했다.
  - `CommandService.SearchPipeline.cs` 본문 크기: 2705 → 2311 라인.
- `apps/omninode-middleware-tests/SearchPromptPolicyTests.cs`
  - need-web 판단 프롬프트, 웹/URL 컨텍스트 프롬프트 핵심 지시문, 저장소 컨텍스트 포함, Gemini 토큰 예산, Gemini 실패 문구 판정, need-web JSON 파싱을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Search/SearchAnswerFormatterPolicy.cs`
  - Gemini 웹 응답 번호 목록 정규화, 출처 링크 artifact 제거, narrative paragraph 병합, structured label 정규화, markdown table 변환/출처 metadata 정리를 `CommandService.SearchPipeline`에서 분리했다.
  - `CommandService.SearchPipeline`에는 텔레그램/인용 partial 호환을 위한 얇은 wrapper만 남겼다.
  - `CommandService.SearchPipeline.cs` 본문 크기: 2311 → 1171 라인.
- `apps/omninode-middleware-tests/SearchAnswerFormatterPolicyTests.cs`
  - 번호 목록 재정렬, plain text table → markdown table 변환, 출처 열 metadata 분리, 출처 URL artifact 제거, narrative paragraph/label 정규화를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Search/GitHubRepositoryContextLoader.cs`
  - GitHub 저장소 URL 컨텍스트 로딩의 네트워크 I/O를 `CommandService.SearchPipeline`에서 분리했다.
  - GitHub HTML fetch, embedded README metadata 해석, raw README fetch, fallback README 사용, 관련 README 발췌 조립을 전담한다.
  - `CommandService.SearchPipeline`은 loader 호출과 Gemini URL-context orchestration만 담당한다.
  - `CommandService.SearchPipeline.cs` 본문 크기: 1171 → 1070 라인.
- `apps/omninode-middleware-tests/GitHubRepositoryContextLoaderTests.cs`
  - GitHub HTML → raw README fetch, raw README 실패 시 embedded README fallback, 비 GitHub/issue URL 거부를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Telegram/TelegramResponseFormatterPolicy.cs`
  - 텔레그램 응답의 markdown → plain text 변환, markdown table 보존/정규화, inline markdown 제거, 가독성 줄바꿈, 번호 목록/소수점 줄 병합, claim spacing, 문자 수 기준 truncation을 `CommandService.Telegram`에서 분리했다.
  - `CommandService.Telegram`에는 `SanitizeChatOutput` 이후 정책 클래스로 위임하는 얇은 wrapper만 남겨 `Execution`/`RoutineExecution` partial의 기존 호출 계약을 유지했다.
  - `CommandService.Telegram.cs` 본문 크기: 4886 → 3911 라인.
- `apps/omninode-middleware-tests/TelegramResponseFormatterPolicyTests.cs`
  - markdown heading/link/code fence 변환, markdown table 보존, 문자 수 기준 truncation marker, 분리된 번호 줄 병합을 단위 테스트로 고정했다.
- `scripts/check-chat-telegram-contract.mjs`
  - `telegram_response_truncated` 계약 검사를 새 `TelegramResponseFormatterPolicy` 위치 기준으로 갱신했다.
- `apps/omninode-middleware/src/Infrastructure/Telegram/TelegramPromptPolicy.cs`
  - 텔레그램 긴 입력 압축 프롬프트, profile/thinking 프롬프트, thinking level 판정, 결론 요구/불확실성 판정, 결론 escalation 프롬프트, orchestration 통합 프롬프트, concise/full-fidelity 프롬프트를 `CommandService.Telegram`에서 분리했다.
  - `CommandService.Telegram`에는 기존 partial 호출 계약을 유지하는 얇은 wrapper만 남겼다.
  - `CommandService.Telegram.cs` 본문 크기: 3911 → 3749 라인.
- `apps/omninode-middleware-tests/TelegramPromptPolicyTests.cs`
  - code/talk profile thinking level, decision/risk 질문 escalation, 목록 요청 건수 유지, profile prompt, orchestration prompt, 불확실성 판정을 단위 테스트로 고정했다.
- `scripts/check-chat-telegram-contract.mjs`
  - full-fidelity prompt 계약을 `TelegramPromptPolicy` 위치 기준으로 갱신했다.
- `scripts/check-security-boundaries.mjs`
  - `TelegramPromptPolicy`가 텔레그램 프롬프트/판정 책임을 소유하고 `CommandService.Telegram`이 위임하는지 계약 검사를 추가했다.
- `apps/omninode-middleware/src/Infrastructure/Telegram/TelegramConversationContextPolicy.cs`
  - 텔레그램 후속 질문 anchor turn 선택, weak follow-up, contextual follow-up, correction follow-up, exhausted feedback 판정과 follow-up aware input 조립을 `CommandService.Telegram`에서 분리했다.
  - `CommandService.Telegram`에는 기존 호출 계약을 유지하는 wrapper만 남겼다.
- `apps/omninode-middleware-tests/TelegramConversationContextPolicyTests.cs`
  - standalone 입력 유지, 웹 검색 후속 질문 확장, 정정 요청 확장, 기본 조치 소진 피드백 확장, 문맥 후속 질문 확장, 이전 weak follow-up skip anchor 선택을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Telegram/TelegramNaturalCommandPolicy.cs`
  - 텔레그램 자연어 제어 요청을 slash command로 바꾸는 정규식/alias 정책, provider alias, thinking alias, help topic 판정을 `CommandService.Telegram`에서 분리했다.
  - 분리 과정에서 `오케스트레이션 코딩 제공자 변경`이 일반 실행 요청보다 먼저 매칭되도록 구체 설정 명령 우선순위를 정책 안에서 고정했다.
  - `CommandService.Telegram.cs` 본문 크기: 3749 → 2894 라인.
- `apps/omninode-middleware-tests/TelegramNaturalCommandPolicyTests.cs`
  - help/status/file/refactor/memory/routine/metrics, coding run/provider/model, LLM provider/model, plan/task/notebook/routine natural command 변환과 provider/help alias를 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `TelegramConversationContextPolicy`, `TelegramNaturalCommandPolicy` 소유권과 `CommandService.Telegram` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/ConversationContextPolicy.cs`
  - 대화탭/코딩탭/텔레그램 공통 맥락 주입 판단의 순수 정책을 `CommandService.Utils`에서 분리했다.
  - prior context 필요 여부, ambiguous opinion request 판정, 강한 후속 질문 판정, 명시적 독립 질문 판정, context token 추출/stop token/meaningful overlap을 소유한다.
  - `CommandService.Utils.cs`에는 기존 partial 호출 호환 wrapper만 남겼다.
  - `CommandService.Utils.cs` 본문 크기: 6941 → 6710 라인.
- `apps/omninode-middleware-tests/ConversationContextPolicyTests.cs`
  - 후속 질문/독립 인사/모호 판단 요청, 독립 질문 판정, context token 추출, token overlap, stop token 정책을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `ConversationContextPolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/CodingLanguagePolicy.cs`
  - 코딩 언어 정규화, `auto` 보존 언어 힌트 정규화, 명시 언어 감지, 초기 코딩 언어 결정, 파일 확장자 기반 언어 추정, `[새 요청]` 블록에서 최신 코딩 요청 추출을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`에는 기존 partial 호출 호환 wrapper만 남겼다.
  - `CommandService.Utils.cs` 본문 크기: 6710 → 6447 라인.
- `apps/omninode-middleware-tests/CodingLanguagePolicyTests.cs`
  - 언어 alias, `auto` 보존, 명시 언어 감지, objective fallback, 파일 확장자 추정, 최신 코딩 요청 추출을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingLanguagePolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/CodingProgressPolicy.cs`
  - 코딩 진행 update 생성, 요청 분석 detail, 작업공간 점검 detail, 반복 계획 detail, 파일 쓰기 detail 조립을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`에는 기존 호출부 호환 wrapper만 남겼다.
- `apps/omninode-middleware-tests/CodingProgressPolicyTests.cs`
  - stage metadata 보존, 요청/작업공간/계획/쓰기 progress detail 조립을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CodingPromptPolicy.cs`
  - 코딩 루프 JSON 프롬프트, 코딩 agent objective 프롬프트, 병렬 워커 초안 프롬프트, orchestration/multi aggregate 프롬프트, multi summary 프롬프트 조립을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`에는 품질 브리프/언어 규칙/worker digest를 주입해 정책 클래스로 위임하는 wrapper만 남겼다.
  - `CommandService.Utils.cs` 본문 크기: 6447 → 6203 라인.
- `apps/omninode-middleware-tests/CodingPromptPolicyTests.cs`
  - 코딩 루프 프롬프트의 JSON/action 규칙, objective 프롬프트의 더미 구현 차단 문구, draft worker 출력 형식, aggregate/summary 프롬프트 조립을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingProgressPolicy`, `CodingPromptPolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `scripts/check-coding-python-game-contract.mjs`
  - 공통 더미 구현 차단 프롬프트 계약을 새 `CodingPromptPolicy` 위치 기준으로 갱신했다.
- `apps/omninode-middleware/src/CodingLoopPlanParser.cs`
  - 코딩 루프 LLM 응답에서 JSON 후보 추출, HTML/code fence unwrap, raw newline escape, trailing comma 제거, `CodingLoopPlan` 파싱, action type 보정을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`에는 기존 호출부 호환 wrapper만 남겼다.
  - `CommandService.Utils.cs` 본문 크기: 6203 → 5894 라인.
- `apps/omninode-middleware-tests/CodingLoopPlanParserTests.cs`
  - code fence JSON 파싱, `mkdir|write_file` action type 보정, command 기반 run 추론, 확장자 없는 path의 mkdir 추론, raw newline/trailing comma 정규화, HTML wrapper 해제를 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingLoopPlanParser` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/GeneratedCodeTextPolicy.cs`
  - 생성 파일 내용 줄바꿈/공통 indent 정규화와 `LANGUAGE=<언어>` prefix 기반 plain code 추출을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`에는 기존 호출부 호환 wrapper만 남겼다.
  - `CommandService.Utils.cs` 본문 크기: 5894 → 5804 라인.
- `apps/omninode-middleware-tests/GeneratedCodeTextPolicyTests.cs`
  - 공통 indent 제거, CRLF/CR 줄바꿈 정규화, `LANGUAGE=ts` alias 처리, fenced code unwrap, prefix 부재 fallback을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `GeneratedCodeTextPolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/CodingFallbackPolicy.cs`
  - fallback code-only/file-bundle 프롬프트, fallback 코드/파일 번들 추출, 요청 파일 경로 추출, fallback entry path 제안, deterministic repair objective prompt, 예상 stdout 추출을 `CommandService.Utils`에서 분리했다.
  - fallback bundle record와 project profile/repair prompt request record도 정책 파일로 이동해 `CommandService.Utils`는 fallback orchestration과 wrapper만 담당한다.
  - `CommandService.Utils.cs` 본문 크기: 5804 → 5303 라인.
- `apps/omninode-middleware-tests/CodingFallbackPolicyTests.cs`
  - code-only/file-bundle 프롬프트, 마지막 코드펜스 언어 선택, JSON content/path 기반 코드 추출, 안전하지 않은 fallback 파일 경로 필터링, requested path 우선순위, entry path 제안, stdout 추출, repair prompt를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/ConversationTitlePolicy.cs`
  - 자동 제목 갱신 가능 여부, provider 실패 제목 감지, 생성 제목 정규화, 사용자/어시스턴트 fallback 제목 생성, 제목 프롬프트용 assistant text truncation, auto-title용 assistant 후보 선택을 `CommandService.Utils`에서 분리했다.
  - `CommandService.cs`의 제목 전용 regex도 정책 파일로 이동했다.
- `apps/omninode-middleware/src/ConversationHistoryPolicy.cs`
  - `[user]`/`[assistant]` history block 파싱, 최근 메시지 우선 budget trimming, 이전 메시지 압축 summary, high-signal history line 판정을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`는 `BuildContextualInput`에서 `ConversationHistoryPolicy.BuildBudgetedContextHistory`를 호출하고, 제목 관리는 `ConversationTitlePolicy`에 위임한다.
  - `CommandService.Utils.cs` 본문 크기: 5303 → 4868 라인.
- `apps/omninode-middleware-tests/ConversationTitlePolicyTests.cs`
  - user/assistant turn 존재 여부, 기본 제목 갱신, custom title 보존, provider 실패 제목 재시도, 제목 노이즈 제거, markdown/url fallback 제목 정리, provider 실패 assistant 후보 skip을 단위 테스트로 고정했다.
- `apps/omninode-middleware-tests/ConversationHistoryPolicyTests.cs`
  - multiline history block 파싱, 최근 메시지 우선 trimming, 이전 대화 압축/최근 턴 분리, high-signal 없는 older message fallback, 구현 신호 판정을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `ConversationTitlePolicy`, `ConversationHistoryPolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/ChatOutputSanitizerPolicy.cs`
  - 일반 대화/코딩/검색/텔레그램 공통 응답 정리 흐름을 `CommandService.Utils`에서 분리했다.
  - `<think>` 제거, Copilot 문서 fetch meta 제거, HTML wrapper 정리, 중복 줄/반복 문자 축약, markdown table separator 정규화, markdown table list 변환/보존, 출처 블록 단일 줄화, structured label 정규화, dangling markdown bold marker 제거를 전담한다.
  - 텔레그램 응답 포매터에서 필요로 하는 `NormalizeStructuredLabelBlocks`, `IsStandaloneNumberedHeadlineLine`, `IsMarkdownTableRow`도 같은 정책 클래스로 이동했다.
  - `CommandService.Utils.cs`는 `SanitizeChatOutput` wrapper만 남기고 정책 클래스로 위임한다.
  - `CommandService.Telegram.cs`는 텔레그램 포매터 delegate를 `ChatOutputSanitizerPolicy`에서 직접 받도록 정리했다.
  - `CommandService.cs`의 sanitizer 전용 regex와 `CommandService.Utils.cs`의 markdown table regex를 정책 파일로 이동했다.
  - `CommandService.Utils.cs` 본문 크기: 4868 → 3737 라인.
- `apps/omninode-middleware-tests/ChatOutputSanitizerPolicyTests.cs`
  - 빈 응답 fallback, think/html/Copilot meta 제거, markdown table list 변환/보존, noisy source 숨김, structured label 정규화, numbered headline 판정을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `ChatOutputSanitizerPolicy` 소유권, `CommandService.Utils`의 sanitizer 위임, `CommandService.Telegram`의 sanitizer delegate 사용 계약을 추가했다.
- `apps/omninode-middleware/src/MultiComparisonPolicy.cs`
  - multi comparison assistant JSON 조립, provider entry 포함 여부 판정, 비교 요약 section 파싱, multi/coding summary section adapter, multi summary assistant text 조립을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`에는 기존 partial 호출 계약을 유지하는 wrapper만 남겼다.
  - `CommandService.Utils.cs` 본문 크기: 3737 → 3569 라인.
- `apps/omninode-middleware-tests/MultiComparisonPolicyTests.cs`
  - 선택 안 함 worker 필터링, JSON escaping, known heading 파싱, blank fallback, summary assistant text fallback을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `MultiComparisonPolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/GeneratedCodeCandidatePolicy.cs`
  - routine bash 생성/재생성 흐름에서 쓰는 code generation prompt와 code candidate parsing을 `CommandService.Utils`에서 분리했다.
  - `LANGUAGE=` prefix, code fence 언어 우선순위, JSON object fallback, plain text cleanup을 전담한다.
  - `CommandService.Utils.cs`에는 기존 partial 호출 계약을 유지하는 wrapper만 남겼다.
  - `CommandService.cs`의 `JsonObjectRegex`도 정책 파일로 이동했다.
  - `CommandService.Utils.cs` 본문 크기: 3569 → 3520 라인.
- `apps/omninode-middleware-tests/GeneratedCodeCandidatePolicyTests.cs`
  - code generation prompt 계약, explicit language/fence 파싱, JSON object fallback, plain text cleanup을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `GeneratedCodeCandidatePolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/CodingExecutionSafetyPolicy.cs`
  - 코딩 루프 실행 안전성 판단을 `CommandService.Utils`에서 분리했다.
  - deferred verification command 신뢰 여부, interactive objective 판정, `mkdir` 액션의 file-like path skip 판단, path segment sanitizing, 위험한 생성 실행 명령 차단, action type 정규화 위임을 전담한다.
  - destructive shell pattern regex도 정책 파일로 이동해 `CommandService.Utils`는 기존 partial 호출 계약을 유지하는 wrapper만 남겼다.
  - `CommandService.Utils.cs` 본문 크기: 3520 → 3411 라인.
- `apps/omninode-middleware-tests/CodingExecutionSafetyPolicyTests.cs`
  - path segment sanitizing, 위험 명령 차단, file-like mkdir 판정, interactive objective 판정, deferred verification command 신뢰 기준, action type 정규화 위임을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingExecutionSafetyPolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `scripts/check-coding-python-game-contract.mjs`
  - frontend deferred command guard 계약 위치를 새 `CodingExecutionSafetyPolicy` 기준으로 갱신했다.
- `apps/omninode-middleware/src/CodingQualityBriefPolicy.cs`
  - 코딩 루프 프롬프트에 들어가는 품질 브리프 조립을 `CommandService.Utils`에서 분리했다.
  - resolved language, 요청 파일 목록, 예상 stdout, frontend/game/general acceptance 기준, verification 요구 문구를 전담한다.
  - `CommandService.Utils.cs`는 `BuildCodingQualityBrief` wrapper만 유지하고 정책 클래스로 위임한다.
  - `CommandService.Utils.cs` 본문 크기: 3411 → 3376 라인.
- `apps/omninode-middleware-tests/CodingQualityBriefPolicyTests.cs`
  - 언어/요청 파일/예상 stdout 포함, 요청 파일 부재 시 기본 엔트리 안내, frontend acceptance 우선순위, game acceptance 분기를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CodingLoopTuningPolicy.cs`
  - 코딩 루프 one-shot 사용 여부, 반복/액션/token 하한, repair pass 수, 최근 loop log trimming을 `CommandService` partial에서 분리했다.
  - `CommandService.CodingProfiles`는 profile 값과 feature flag를 정책 클래스로 넘기는 wrapper만 유지한다.
  - `CommandService.Utils.cs`에 남아 있던 미사용 provider string 기반 loop tuning helper도 제거했다.
  - `CommandService.Utils.cs` 본문 크기: 3376 → 3248 라인.
- `apps/omninode-middleware-tests/CodingLoopTuningPolicyTests.cs`
  - one-shot 조건, 반복/액션/token 하한, interactive/multi-file repair pass 증가, 최근 loop log trimming을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingQualityBriefPolicy`, `CodingLoopTuningPolicy` 소유권과 `CommandService` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/CodingDeterministicOutputRepairPolicy.cs`
  - 단일 Python 파일 stdout deterministic repair의 실행 여부 판정과 Python string literal escaping, `print(...)` 코드 생성을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`는 deterministic repair orchestration만 유지하고 순수 판정/코드 생성은 정책 클래스로 위임한다.
  - `CommandService.Utils.cs` 본문 크기: 3248 → 3197 라인.
- `apps/omninode-middleware-tests/CodingDeterministicOutputRepairPolicyTests.cs`
  - Python 단일 파일/예상 stdout 조건, 자동 언어 추정, JavaScript 거부, Python 문자열 escape, deterministic print 코드 생성을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CodingFallbackDecisionPolicy.cs`
  - fallback file bundle 선호 여부 판단을 `CommandService.Utils`와 `CommandService.CodingProfiles`에서 분리했다.
  - 다중 요청 경로, 명시적 single-file 요청, project profile의 multi-file 선호, provider profile 선호, frontend/game 신호, 명시적 multi-file 텍스트 신호를 하나의 정책으로 묶었다.
  - `CommandService.Utils.cs` 본문 크기: 3197 → 3186 라인.
  - `CommandService.CodingProfiles.cs` 본문 크기: 1597 → 1592 라인.
- `apps/omninode-middleware-tests/CodingFallbackDecisionPolicyTests.cs`
  - 다중 경로 우선, single-file 요청 우선 거부, profile/frontend/game 선호, explicit multi-file 텍스트 신호 분기를 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingDeterministicOutputRepairPolicy`, `CodingFallbackDecisionPolicy` 소유권과 `CommandService` 위임 계약을 추가했다.
- `scripts/check-coding-python-game-contract.mjs`
  - fallback bundle 선호 계약을 새 `CodingFallbackDecisionPolicy` 위치 기준으로 갱신했다.
- `apps/omninode-middleware/src/GroqPromptPolicy.cs`
  - Groq rate limit 응답 판정과 max_tokens 제한 응답 판정을 `CommandService.Utils`에서 분리했다.
  - `CommandService.ProviderRouting`에서 호출되는 기존 wrapper는 유지하되 실제 문자열 판정은 `GroqPromptPolicy`가 소유한다.
  - `CommandService.Utils.cs` 본문 크기: 3186 → 3169 라인.
- `apps/omninode-middleware-tests/GroqPromptPolicyTests.cs`
  - 429/too many requests/rate limit/요청 한도 신호, 일반 오류 무시, max_tokens 제한 메시지 판정을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - Groq 응답 fallback 판정 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
- `apps/omninode-middleware/src/ProviderModelSelectionPolicy.cs`
  - Copilot provider/model pinning, pinned provider model normalization을 `CommandService.Utils`에서 분리했다.
  - `CommandService.Utils.cs`에는 기존 partial 호출 계약을 유지하는 wrapper만 남겼다.
- `apps/omninode-middleware-tests/ProviderModelSelectionPolicyTests.cs`
  - Copilot provider 감지, Copilot 모델 기본값 고정, non-Copilot 모델 정규화 delegate 호출, pinned Copilot model 판정을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/MemoryNoteSelectionPolicy.cs`
  - 명시 memory note 이름 정규화와 linked/request memory note merge를 `CommandService.Utils`에서 분리했다.
  - trim, case-insensitive dedup, base/request 순서 보존을 정책 클래스가 소유한다.
- `apps/omninode-middleware-tests/MemoryNoteSelectionPolicyTests.cs`
  - explicit note name 정규화, null/empty 입력, merge 순서 유지, blank request name 무시를 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `ProviderModelSelectionPolicy`, `MemoryNoteSelectionPolicy` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
  - `CommandService.Utils.cs` 본문 크기: 3169 → 3139 라인.
- `apps/omninode-middleware/src/CodingDeterministicScaffoldPolicy.cs`
  - UI clone deterministic scaffold 생성 책임을 `CommandService.Utils`에서 분리했다.
  - 도메인 기반 폴더명 추출, 명시 언어 guard, 게임 요청 제외, `index.html/styles.css/script.js` scaffold 조립을 정책 클래스가 소유한다.
  - web shooter deterministic scaffold 생성 책임도 같은 정책 클래스로 이동했다.
  - 웹/브라우저/HTML 신호, shooter/game 신호, frontend 언어 힌트 guard, `Sky Patrol` 단일 HTML 게임 scaffold 조립을 정책 클래스가 소유한다.
- `apps/omninode-middleware-tests/CodingDeterministicScaffoldPolicyTests.cs`
  - 도메인 폴더 scaffold, 기본 `web-clone` fallback, 게임 요청 거부, non-web 명시 언어 거부를 단위 테스트로 고정했다.
  - web shooter scaffold의 단일 `index.html`, canvas/runtime loop 포함, non-web 언어 힌트 거부, game 신호 필수 조건을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CodingArtifactCleanupPolicy.cs`
  - 단일 파일 작업 후 root의 generic fallback 산출물(`main.py`, `index.html` 등)을 정리하는 로직을 `CommandService.Utils`에서 분리했다.
  - 실제 삭제는 요청 파일이 존재하고, 단일 파일 의도가 명확하며, 같은 확장자의 root generic fallback 파일일 때만 수행한다.
- `apps/omninode-middleware-tests/CodingArtifactCleanupPolicyTests.cs`
  - 단일 파일 의도 없음 fallback, root generic 동일 확장자 삭제, nested/different-extension 보존, generic fallback 파일명 판정을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CodingLanguagePolicy.cs`
  - 최종 코딩 결과 언어 판정을 `CommandService.Utils`에서 분리했다.
  - 초기 요청이 HTML 계열이거나 JavaScript/CSS 작업에서 HTML 파일이 생성된 경우 최종 결과 언어를 `html`로 승격한다.
- `apps/omninode-middleware/src/ChatOutputSanitizerPolicy.cs`
  - 코딩 결과/텔레그램 요약에 쓰는 code block 숨김 텍스트 정리를 `CommandService.Utils`에서 분리했다.
- `apps/omninode-middleware-tests/CodingLanguagePolicyTests.cs`, `apps/omninode-middleware-tests/ChatOutputSanitizerPolicyTests.cs`
  - 최종 결과 언어 승격과 markdown/`[code]`/HTML code block 숨김을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CodingLoopActionExecutor.cs`
  - 코딩 루프 action 실행(`mkdir`, `write_file`, `append_file`, `read_file`, `delete_file`, `run`)을 `CommandService.Utils`에서 분리했다.
  - workspace path resolve, provider 생성물 정규화, command runner는 delegate로 주입해 기존 `CommandService` 동작을 유지하면서 실행기를 단위 테스트 가능하게 만들었다.
  - 위험한 generated run command 차단, file-like mkdir skip, 파일 preview truncation, shell 실행 결과 → `CodeExecutionResult` 변환을 executor가 소유한다.
- `apps/omninode-middleware-tests/CodingLoopActionExecutorTests.cs`
  - 파일 쓰기/부모 디렉터리 생성, file-like mkdir skip, read preview, 위험 run command runner 호출 전 차단, 안전 command runner 실행을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingDeterministicScaffoldPolicy`, `CodingArtifactCleanupPolicy`, `CodingLanguagePolicy.ResolveFinalResultLanguage`, `ChatOutputSanitizerPolicy.RemoveCodeBlocksFromText`, `CodingLoopActionExecutor` 소유권과 `CommandService.Utils` 위임 계약을 추가했다.
  - `CommandService.Utils.cs` 본문 크기: 3139 → 2268 라인.
- `apps/omninode-middleware/src/CodingExpectedOutputPolicy.cs`
  - deterministic repair에서 쓰는 예상 stdout 줄 추출, visible text 요구 literal 추출, 첫 줄/둘째 줄 label mapping을 `CommandService.CodingDeterministicRepairs`에서 분리했다.
  - `[새 요청]` 최신 블록 우선, inline ordered label, stdout quoted literal fallback을 정책 클래스가 소유한다.
  - `CommandService.CodingDeterministicRepairs.cs`는 기존 partial 호출 계약을 유지하는 wrapper만 남기고 파싱 책임을 정책으로 위임한다.
- `apps/omninode-middleware-tests/CodingExpectedOutputPolicyTests.cs`
  - 첫 줄/둘째 줄 순서 유지, inline ordered label, stdout literal fallback, 최신 `[새 요청]` 블록 우선, visible text literal 추출, label index mapping을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CodingDeterministicStructuredRepairPolicy.cs`
  - Python/JavaScript/Java/C/HTML 다중 파일 deterministic repair plan 생성, structured requested path 판정, dataset/text block 추출, 템플릿 코드 생성을 `CommandService.CodingDeterministicRepairs`에서 분리했다.
  - `CommandService.CodingDeterministicRepairs.cs`는 plan 생성 대신 파일 적용, 검증 명령 실행, exception recovery orchestration만 담당한다.
  - `CommandService.CodingDeterministicRepairs.cs` 본문 크기: 1171 → 222 라인.
- `apps/omninode-middleware-tests/CodingDeterministicStructuredRepairPolicyTests.cs`
  - Python snapshot bundle, JavaScript schedule bundle, HTML dashboard bundle, 불완전 structured 요청 거부를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Telegram/TelegramPseudoCommandExecutor.cs`
  - 텔레그램 자연어 pseudo command/slash command 실행 분기(`/help`, `/talk`, `/code`, `/model`, `/llm`, `/skill`, `/coding`, `/refactor`, `/memory`, `/doctor`, `/plan`, `/task`, `/notebook`, `/routine`, `/metrics`, `/kill`)를 `CommandService.Telegram`에서 분리했다.
  - 실제 도메인 실행은 delegate handler map으로 주입해 기존 private command handler 동작과 감사 로그/kill guard를 유지한다.
  - `CommandService.Telegram.cs` 본문 크기: 2894 → 2852 라인.
- `apps/omninode-middleware-tests/TelegramPseudoCommandExecutorTests.cs`
  - help routing, coding command의 attachment/web context 전달, `/routines`/`/handoff` alias, `/metrics`, `/kill`, unknown command fallback을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/Infrastructure/Telegram/TelegramLlmPreferencePolicy.cs`
  - 텔레그램 `/talk`/`/code` 프로필 기본값 적용, thinking level 정규화, `/model <provider>` 빠른 전환 선택을 `CommandService.Telegram`에서 분리했다.
  - `CommandService.Telegram.cs`는 lock 범위 안에서 정책 결과를 적용하는 thin handler 역할만 남겼다.
  - `CommandService.Telegram.cs` 본문 크기: 2852 → 2806 라인.
- `apps/omninode-middleware-tests/TelegramLlmPreferencePolicyTests.cs`
  - thinking level 정규화, talk/code orchestration preset, Groq fallback 모델, NVIDIA/Codex quick model selection, unknown provider 거부를 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CodingExpectedOutputPolicy`, `CodingDeterministicStructuredRepairPolicy` 소유권과 `CommandService.CodingDeterministicRepairs` 위임 계약을 추가했다.
  - `TelegramPseudoCommandExecutor` 소유권과 `CommandService.Telegram`의 pseudo command 실행 위임 계약을 추가했다.
  - `TelegramLlmPreferencePolicy` 소유권과 `CommandService.Telegram`의 프로필/빠른 모델 선택 위임 계약을 추가했다.

검증 결과:

- `dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj`: 통과, 경고 0
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter ConversationContextPolicyTests`: 통과, 24 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingLanguagePolicyTests|ConversationContextPolicyTests"`: 통과, 48 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "TelegramNaturalCommandPolicyTests|TelegramConversationContextPolicyTests"`: 통과, 29 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingProgressPolicyTests|CodingPromptPolicyTests"`: 통과, 10 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingLoopPlanParserTests|CodingProgressPolicyTests|CodingPromptPolicyTests"`: 통과, 16 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingLoopPlanParserTests|GeneratedCodeTextPolicyTests|CodingProgressPolicyTests|CodingPromptPolicyTests"`: 통과, 21 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "ConversationTitlePolicyTests|ConversationHistoryPolicyTests"`: 통과, 12 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter ChatOutputSanitizerPolicyTests`: 통과, 7 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter MultiComparisonPolicyTests`: 통과, 5 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter GeneratedCodeCandidatePolicyTests`: 통과, 4 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter CodingExecutionSafetyPolicyTests`: 통과, 6 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingQualityBriefPolicyTests|CodingLoopTuningPolicyTests"`: 통과, 8 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingDeterministicOutputRepairPolicyTests|CodingFallbackDecisionPolicyTests"`: 통과, 7 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter GroqPromptPolicyTests`: 통과, 19 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "ProviderModelSelectionPolicyTests|MemoryNoteSelectionPolicyTests"`: 통과, 10 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingArtifactCleanupPolicyTests|CodingDeterministicScaffoldPolicyTests|CodingLanguagePolicyTests|ChatOutputSanitizerPolicyTests"`: 통과, 44 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingLoopActionExecutorTests|CodingDeterministicScaffoldPolicyTests"`: 통과, 12 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter CodingExpectedOutputPolicyTests`: 통과, 8 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "CodingDeterministicStructuredRepairPolicyTests|CodingExpectedOutputPolicyTests"`: 통과, 12 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter TelegramPseudoCommandExecutorTests`: 통과, 7 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter "TelegramLlmPreferencePolicyTests|TelegramPseudoCommandExecutorTests"`: 통과, 17 tests
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj`: 통과, 494 tests
- `node scripts/check-security-boundaries.mjs`: 통과, assertions 505
- `node scripts/check-chat-telegram-contract.mjs`: 통과
- `node scripts/check-coding-python-game-contract.mjs`: 통과, assertions 106
- `node scripts/check-gateway-runtime-contract.mjs`: 통과
- `npm test`: 통과
- `make -C apps/omninode-core -B`: 통과
- `git diff --check`: 통과
- `dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj --filter TelegramResponseFormatterPolicyTests`: 통과, 4 tests

### P4. Provider adapter 구조 정리

상태: 진행 중

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

진행 내용:

- `apps/omninode-middleware/src/ProviderTimeoutPolicy.cs`
  - `LlmRouter` 내부에 묶여 있던 shared HTTP timeout, single-chat timeout floor, Gemini grounded/url-context first-chunk timeout 계산을 단일 static policy로 분리했다.
  - `LlmRouter`와 `CommandService.ProviderRouting`이 같은 정책 객체를 호출하도록 정리했다.
- `apps/omninode-middleware-tests/ProviderTimeoutPolicyTests.cs`
  - provider별 timeout floor, override 우선순위, Gemini grounded timeout clamp, shared HTTP timeout 합계를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/GroqPromptPolicy.cs`
  - Groq compound 모델 max_output_tokens cap, prompt budget, multi-turn split, `[최근 대화]/[새 요청]` 파싱, prompt truncation, request-too-large 판단, retry-after 헤더 기반 retry delay를 묶었다.
  - `LlmRouter`는 더 이상 이 helper의 원본을 들고 있지 않으며, Groq 호출 경로 전체가 `GroqPromptPolicy.*` 단일 진입점을 통과한다.
- `apps/omninode-middleware-tests/GroqPromptPolicyTests.cs`
  - compound 모델 cap, prompt 축약 마커, multi-turn split 동작, max_tokens 에러 추출, request_too_large 식별, retry-after 헤더 처리를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/GeminiRequestPolicy.cs`
  - Gemini grounded body, URL-context body, streaming delta dedupe를 single static policy로 분리했다.
  - `LlmRouter`는 `GeminiRequestPolicy.BuildGroundedBody/BuildUrlContextBody/NormalizeStreamDelta`만 호출한다.
- `apps/omninode-middleware-tests/GeminiRequestPolicyTests.cs`
  - grounded/url-context body의 tool 구성, generationConfig, prompt escaping, stream delta dedupe 분기를 JSON 파싱 기반 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/CerebrasErrorPolicy.cs`
  - Cerebras `model_not_found` 식별과 429/503 메시지 분기(zai-glm-4.7, qwen-3-preview 분기 포함)를 분리했다.
- `apps/omninode-middleware-tests/CerebrasErrorPolicyTests.cs`
  - 명시적 code 필드, substring 폴백, preview 모델 429/503 안내, 기본 statusCode 메시지 동작을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/ProviderResponseParser.cs`
  - OpenAI-compatible(Groq/NVIDIA/Cerebras) `choices` chunk 추출, Gemini `candidates` chunk 추출, truncation 판정(`length`/`max_tokens`/`token_limit`/Gemini `MAX_TOKENS`), Gemini block reason 추출, OpenAI-compatible/Gemini token usage 파싱을 단일 static parser로 묶었다.
  - `ProviderChatChunk`(Content, FinishReason)와 `ProviderTokenUsage`(prompt/completion/total) 두 record를 공용 타입으로 노출하고, `LlmRouter`의 사설 `GroqChatChunk`/`GeminiChatChunk` record는 제거했다.
  - `LlmRouter`의 `CaptureGroqUsage`, `CaptureGeminiUsage`, `CaptureOpenAiCompatibleTokenUsage`는 parser가 돌려준 typed 결과만 받아 state 갱신과 SaveUsageState 호출에 집중하도록 단순화했다.
- `apps/omninode-middleware-tests/ProviderResponseParserTests.cs`
  - OpenAI-compatible/Gemini chunk 추출 분기, truncation 인식, block reason 추출, OpenAI/Gemini usage 파싱(부재/손상 폴백 포함)을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/GroqRateLimitHeaderParser.cs`
  - `x-ratelimit-limit-requests`, `x-ratelimit-remaining-requests`, `x-ratelimit-limit-tokens`, `x-ratelimit-remaining-tokens`, `x-ratelimit-reset-requests`, `x-ratelimit-reset-tokens` 헤더 파싱과 `GroqRateLimit` snapshot 조립을 단일 static parser로 묶었다.
  - `LlmRouter`는 더 이상 `ReadHeaderLong`/`ReadHeaderString` 같은 사설 HTTP 헤더 helper를 들고 있지 않고, `GroqRateLimitHeaderParser.Parse(headers, DateTimeOffset.UtcNow)` 한 줄로 캡처한다.
  - 더 이상 사용처가 없는 `GetInt(JsonElement, string)` private helper도 함께 제거했다.
- `apps/omninode-middleware-tests/GroqRateLimitHeaderParserTests.cs`
  - 전 헤더 채워진 정상 응답, 헤더 부재 시 nullable 유지, 잘못된 정수 헤더 무시 동작을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/OpenAiCompatibleProtocol.cs`
  - Groq/Cerebras/NVIDIA 공통 OpenAI 호환 chat body 빌더, provider 표시 이름, HTTP 실패 메시지 변환(NVIDIA 무료 할당량/429/503/401 분기), SSE delta chunk 추출을 묶었다.
  - `LlmRouter`에서는 `OpenAiCompatibleProtocol.BuildChatBody/BuildFailureMessage/DisplayName/ExtractStreamChunk`만 호출하고 원본 helper는 제거했다.
- `apps/omninode-middleware-tests/OpenAiCompatibleProtocolTests.cs`
  - 기본 system+user message 구성, multi-turn 시퀀스 확장, NVIDIA 할당량 안내, Groq rate limit 메시지, provider 표시 이름 매핑, stream chunk 문자열/배열 content와 손상 JSON 폴백을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/GeminiCitationParser.cs`
  - Gemini url-context citation 추출(`urlContextMetadata.urlMetadata` → `SearchCitationReference[]`)과 grounded search citation 추출(`groundingMetadata.groundingChunks` → `SearchCitationReference[]`), URL 또는 title 기반 dedup key를 single static parser로 분리했다.
  - host 기반 fallback 제목 빌더, 대소문자 무시 JSON property reader, 다중 후보 키에서 첫 매치를 골라주는 string getter는 parser 내부 private helper로 이동했다.
  - `LlmRouter`는 `GeminiCitationParser.ExtractUrlContextCitations/ExtractGroundingCitations/BuildDedupKey`만 호출하고, 사설 `TryGetPropertyIgnoreCase`, `GetJsonString`, `BuildUrlContextCitationTitle`, `BuildCitationDedupKey` helper는 모두 제거했다.
- `apps/omninode-middleware-tests/GeminiCitationParserTests.cs`
  - URL 중복 제거와 호스트 기반 제목 fallback, 메타데이터 부재/손상 JSON 분기, grounding 응답의 title 우선/host fallback 분기, `web.uri` 없는 chunk 무시, dedup key URL 우선/title fallback 동작을 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/PlanningPromptPolicy.cs`
  - planning provider chain 정규화(default `[gemini, groq, nvidia, cerebras]`, `nvidia-nim`/`nvidia_nim`/`nim` 정규화, 미지 provider/중복 제거), planner 프롬프트(JSON 스키마와 mode=interview 분기), reviewer 프롬프트(constraints/steps/verification 나열), fallback plan/review를 단일 policy로 분리했다.
  - `LlmRouter`는 `PlanningPromptPolicy.NormalizeProviderChain/BuildPlanningPrompt/BuildPlanReviewPrompt/BuildFallbackPlan/BuildFallbackPlanReview`만 호출한다.
- `apps/omninode-middleware-tests/PlanningPromptPolicyTests.cs`
  - 기본 provider chain, NVIDIA alias 정규화, 미지 provider 거름, planner 프롬프트의 objective/constraints/`planning_mode` 출력, interview 모드, reviewer 프롬프트의 step/verification 나열과 context 생략 분기, fallback 정상/누락 경고 동작을 단위 테스트로 고정했다.
- `scripts/check-plan-tab-contract.mjs`
  - 계획 LLM 프롬프트의 “반드시 JSON 객체 하나만 출력한다” 계약을 `PlanningPromptPolicy.cs` 기준으로 옮기고, `LlmRouter`가 `PlanningPromptPolicy.BuildPlanningPrompt`로 위임하는지 확인하는 계약을 추가했다.
- `scripts/check-security-boundaries.mjs`
  - 아홉 정책/파서/프로토콜 클래스가 책임을 들고 있고, `LlmRouter`가 정책/파서/프로토콜만 호출하도록 정리되었는지 계약 검사를 추가했다 (assertions 117 → 210).
- `apps/omninode-middleware/src/RouterIntentClassifier.cs`
  - LLM 응답 텍스트에서 카테고리 키워드(`OS_CONTROL`/`QUERY_SYSTEM`/`DYNAMIC_CODE`)를 골라 `RouterIntent`로 매핑하는 `MapFromLlmContent`와, LLM 호출 실패 시 사용자 입력의 prefix/keyword(`/kill`/`/metrics`/`/code`/`terminate`/`status`/`로그`/`파이썬`)로 추정하는 `ClassifyHeuristic`을 분리했다.
  - `LlmRouter`는 `RouterIntentClassifier.MapFromLlmContent`와 `RouterIntentClassifier.ClassifyHeuristic`만 호출하고, 사설 `MapIntent`/`ClassifyIntentFallback` helper는 제거했다.
- `apps/omninode-middleware-tests/RouterIntentClassifierTests.cs`
  - LLM 응답 매핑(다중 키워드 우선순위 포함), heuristic의 prefix/keyword 매칭, null/empty 입력 폴백 동작을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - 열 정책/파서/프로토콜/분류기 클래스 책임과 `LlmRouter` 위임을 계약 검사하도록 확장했다 (assertions 117 → 216).
- `apps/omninode-middleware/src/ChatStreamingContinuation.cs`
  - 스트리밍 응답 중단 시 사용자 안내 suffix(`BuildPartialTruncationSuffix`), provider별 timeout 메시지(NVIDIA quota/queue 안내 포함, `BuildTimeoutMessage`), delta callback 예외 차폐(`SafeEmitDelta`), chunk를 줄바꿈 포함 누적(`AppendChunk`), max_tokens 등으로 끊긴 응답의 continuation 프롬프트(`BuildContinuationPrompt`)를 단일 policy로 분리했다.
  - `LlmRouter`는 `ChatStreamingContinuation.*`만 호출하고, 사설 `BuildPartialTruncationSuffix`/`BuildOpenAiCompatibleTimeoutMessage`/`SafeEmitDelta`/`AppendGeneratedChunk`/`BuildContinuationPrompt` helper는 모두 제거했다.
- `apps/omninode-middleware-tests/ChatStreamingContinuationTests.cs`
  - provider display name 활용, 긴 reason hint 자르기, blank reason fallback, NVIDIA timeout 안내, 다른 provider의 generic 메시지, delta callback의 예외 차폐, chunk append의 줄바꿈/blank 무시, continuation prompt의 입력 포함과 tail 6000자 제한 동작을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - 열한 정책/파서/프로토콜/분류기 클래스 책임과 `LlmRouter` 위임을 계약 검사하도록 확장했다 (assertions 117 → 231).
- `apps/omninode-middleware/src/ProviderResponseParser.cs`
  - NVIDIA NIM 202 응답에서 `requestId`/`id` 키를 대소문자 무시로 꺼내는 `ExtractNvidiaRequestId`와 STT 응답에서 `text` 필드를 꺼내고 파싱 실패 시 원본 문자열을 fallback으로 돌려주는 `ExtractSttText`를 추가했다.
  - parser 내부 private `TryGetPropertyCaseInsensitive` helper로 case-insensitive lookup을 캡슐화했다.
  - `LlmRouter`는 `ProviderResponseParser.ExtractNvidiaRequestId`/`ExtractSttText`만 호출하고, 사설 `ExtractNvidiaRequestId`/`ExtractSttText`/`ExtractGroqContent`/`ExtractGeminiText`/`ExtractGroqChatChunk`/`ExtractGeminiChatChunk` wrapper와 `TryGetPropertyCaseInsensitive` helper를 모두 제거했다. 모든 chat chunk 호출은 `ProviderResponseParser.ExtractOpenAiCompatibleChunk`/`ExtractGeminiChunk`를 직접 호출한다.
- `apps/omninode-middleware-tests/ProviderResponseParserTests.cs`
  - NVIDIA requestId의 `requestId` 우선/`id` fallback/대소문자 무시 매칭/빈 응답/손상 JSON, STT의 정상 텍스트/원본 fallback/빈 입력 동작을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - parser의 새 책임과 `LlmRouter`가 wrapper helper 없이 직접 위임하는지를 계약 검사하도록 확장했다 (assertions 117 → 242).
- `apps/omninode-middleware/src/SttTranscriptionAdapter.cs`
  - STT multipart HTTP 호출을 `LlmRouter`에서 분리했다.
  - `/audio/transcriptions` endpoint 정규화, bearer auth, model/file multipart 구성, STT 응답 텍스트 파싱, HTTP 실패/timeout/error 메시지 변환을 전담한다.
  - `LlmRouter.TranscribeAudioAsync`는 설정 확인 뒤 adapter에 위임한다.
- `apps/omninode-middleware-tests/SttTranscriptionAdapterTests.cs`
  - multipart 요청 구성, endpoint 정규화, invalid base64 사전 차단, HTTP 429 실패 메시지를 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - STT 응답 파싱 계약을 `SttTranscriptionAdapter` 기준으로 갱신했다 (assertions 242 → 243).
- `apps/omninode-middleware/src/ProviderChatAdapter.cs`
  - provider별 chat 호출 분리를 위한 `IProviderChatAdapter` 인터페이스를 도입했다.
  - `OpenAiCompatibleChatAdapter`가 OpenAI-compatible 비스트리밍 HTTP body 구성, bearer auth, 실패 메시지 변환, `202 Accepted` 후속 resolver 위임을 전담한다.
  - NVIDIA 비스트리밍 chat 경로가 `_openAiCompatibleChatAdapter.SendAsync`를 통과하도록 변경했다. `LlmRouter`는 NVIDIA max token, continuation, polling resolver, token usage capture만 조정한다.
- `apps/omninode-middleware-tests/OpenAiCompatibleChatAdapterTests.cs`
  - OpenAI-compatible body/auth 구성, HTTP 실패 메시지, `202 Accepted` resolver 위임을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `IProviderChatAdapter`, `OpenAiCompatibleChatAdapter`, NVIDIA 비스트리밍 adapter 위임 계약을 추가했다 (assertions 243 → 247).
- `apps/omninode-middleware/src/NvidiaStatusPollingAdapter.cs`
  - NVIDIA NIM `202 Accepted` status polling을 `LlmRouter`에서 분리했다.
  - request id 추출, `/status/{requestId}` GET polling, `202 Accepted` continue, timeout/error 변환을 전담한다.
  - 기존 polling은 `IsSuccessStatusCode`가 `202`도 true로 보는 순서 문제를 가질 수 있었고, adapter 테스트를 추가하면서 `Accepted`를 먼저 continue 처리하도록 고정했다.
- `apps/omninode-middleware-tests/NvidiaStatusPollingAdapterTests.cs`
  - 성공 polling, `202 Accepted` 반복 후 성공, request id 누락 예외를 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - NVIDIA request id 추출과 accepted continue 계약을 `NvidiaStatusPollingAdapter` 기준으로 갱신했다 (assertions 247 → 251).
- Groq 비스트리밍 chat 경로도 `_openAiCompatibleChatAdapter.SendAsync`를 통과하도록 변경했다.
  - 기존 rate limit header capture, 429 retry delay, max token limit 재시도, request-too-large prompt 축소, continuation 조정은 `LlmRouter`에 남겼다.
  - adapter result가 `HttpResponseHeaders`를 노출해 Groq retry 정책이 기존 response header 기준을 유지하도록 했다.
- `scripts/check-security-boundaries.mjs`
  - Groq 비스트리밍 adapter 위임 계약을 추가했다 (assertions 251 → 252).
- Cerebras 비스트리밍 chat 경로도 `_openAiCompatibleChatAdapter.SendAsync`를 통과하도록 변경했다.
  - model_not_found fallback, catalog 기반 가용 모델 재시도, token usage capture, continuation 조정은 `LlmRouter`에 남기고 HTTP body/auth/failure 변환은 adapter로 분리했다.
- `apps/omninode-middleware/src/CerebrasModelCatalog.cs`
  - Cerebras catalog fetch, 첫 가용 모델 추출, 60초 resolver cache를 `LlmRouter`에서 분리했다.
  - `LlmRouter`는 `_cerebrasModelCatalog.ResolveFirstAvailableModelAsync`와 `GetCachedResolvedModel`만 호출한다.
  - 자체 생성한 catalog만 dispose하도록 `LlmRouter` 소유권 플래그를 추가했다.
- `apps/omninode-middleware-tests/CerebrasModelCatalogTests.cs`
  - 첫 모델 추출, HTTP fetch와 bearer auth, 60초 cache 재사용, HTTP 실패/blank API key 분기를 단위 테스트로 고정했다.
- `apps/omninode-middleware/src/ProviderStreamingAdapter.cs`
  - `IProviderStreamingChatAdapter`와 `OpenAiCompatibleStreamingChatAdapter`를 추가했다.
  - Groq/Cerebras/NVIDIA 공통 OpenAI-compatible streaming 요청 body 구성, `ResponseHeadersRead`, SSE `data:` event 파싱, HTTP 실패 메시지 변환, NVIDIA `202 Accepted` resolver 위임을 전담한다.
  - `LlmRouter.GenerateOpenAiCompatibleChatStreamingAsync`는 continuation loop, usage capture, rate-limit header capture, delta callback 조정만 담당한다.
- `apps/omninode-middleware-tests/OpenAiCompatibleStreamingChatAdapterTests.cs`
  - SSE payload/delta 파싱, streaming body 구성, HTTP 실패 메시지, `202 Accepted` resolver 위임을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - Cerebras resolver cache 소유권이 `CerebrasModelCatalog`에 있고, OpenAI-compatible streaming HTTP/SSE 경계가 `ProviderStreamingAdapter`에 있는지 계약 검사를 추가했다 (assertions 252 → 268).
- `apps/omninode-middleware/src/GeminiStreamingAdapter.cs`
  - Gemini streaming HTTP 호출과 SSE `data:` event 읽기를 `LlmRouter`에서 분리했다.
  - 일반 Gemini streaming, grounded streaming, URL-context streaming 모두 `_geminiStreamingAdapter.StreamAsync`를 통과한다.
  - 각 경로의 usage capture, citation merge, first-chunk/total timeout 의미, 사용자 반환 메시지는 기존 `LlmRouter` orchestration에 남겼다.
- `apps/omninode-middleware-tests/GeminiStreamingAdapterTests.cs`
  - `x-goog-api-key` header, streaming body 전송, SSE payload 전달, HTTP 실패 body 반환을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - Gemini streaming adapter가 `ResponseHeadersRead`, API key header, SSE line reader를 소유하고 `LlmRouter`가 streaming HTTP 모드를 직접 들고 있지 않은지 계약 검사를 추가했다 (assertions 268 → 275).
- `apps/omninode-middleware/src/GeminiGenerateContentAdapter.cs`
  - Gemini 비스트리밍 `generateContent` HTTP 호출을 `LlmRouter`에서 분리했다.
  - 일반 Gemini chat, 실행계획 생성, grounded generateContent, URL-context generateContent, multimodal generateContent 모두 `_geminiGenerateContentAdapter.SendAsync`를 통과한다.
  - `x-goog-api-key` header와 HTTP body 전송/응답 body 수집은 adapter가 소유하고, usage capture/citation merge/continuation loop는 기존 `LlmRouter` orchestration에 남겼다.
- `apps/omninode-middleware-tests/GeminiGenerateContentAdapterTests.cs`
  - API key header, body 전송, 성공 응답 body 반환, 실패 응답 body 반환을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - Gemini generateContent adapter가 API key header와 비스트리밍 HTTP dispatch를 소유하고, `LlmRouter`가 Gemini API key header를 직접 들고 있지 않은지 계약 검사를 추가했다 (assertions 275 → 281).
- `apps/omninode-middleware/src/CitationAccumulator.cs`
  - Gemini URL-context/grounded citation merge에서 중복되던 `Dictionary<string, SearchCitationReference>` dedup 패턴과 `MergeFromGeminiPayload` 흐름을 단일 accumulator로 묶었다.
  - URL/제목 dedup key 적용, 빈 페이로드 무시, `Array.Empty<>` 캐시 반환을 accumulator가 소유한다.
  - `LlmRouter`의 비스트리밍/스트리밍 URL-context 경로 모두 `citationAccumulator.MergeFromGeminiPayload`와 `citationAccumulator.ToArray`만 호출한다. 사설 `MergeCitations` 로컬 함수, `citationByKey`/`citationByUrl` 사설 dictionary는 제거했다.
- `apps/omninode-middleware-tests/CitationAccumulatorTests.cs`
  - URL 기준 dedup, Gemini 페이로드의 URL-context/grounding 동시 추출, 빈 입력/blank 페이로드 무시, 빈 결과의 `Array.Empty` 재사용을 단위 테스트로 고정했다.
- `scripts/check-security-boundaries.mjs`
  - `CitationAccumulator` 소유권, Gemini 파서 위임, `LlmRouter`가 더 이상 ad-hoc citation dictionary를 들고 있지 않다는 계약 검사를 추가했다 (assertions 499 → 505).
- `LlmRouter` 본문 크기: 3755 → 2135 라인 (≈ 43% 감축).

남은 범위:

- Provider별 HTTP 호출 경계, citation merge, fallback/continuation 정책은 모두 adapter/catalog/policy/accumulator로 분리되었다.
- usage capture는 provider state lock과 결합돼 있고, continuation loop는 turn 단위 orchestration에 묶여 있어 추가 분리는 동작 변경 위험 대비 이득이 작다. P4는 마무리 단계로 본다.

### P5. 상태 저장소 복구 정책 확대

상태: 완료

목표:

- JSON 상태 파일의 lock/backup/recovery 적용 범위를 명확히 한다.

작업:

- 상태 파일 inventory 작성.
- AtomicFileStore 적용 여부 표시.
- usage 계열 상태 복구 정책 추가. 완료:
  - `apps/omninode-middleware/src/UsageStatePersistence.cs`
    - `llm_usage.json`, `copilot_usage.json` 로드 시 `AtomicFileStore.ReadAllTextWithBackup`을 통과한다.
    - primary JSON이 손상되고 `.bak`가 유효하면 primary를 백업 내용으로 복구한다.
  - `apps/omninode-middleware-tests/UsageStatePersistenceTests.cs`
    - LLM usage와 Copilot usage의 손상 primary → 유효 backup 복구를 단위 테스트로 고정했다.
  - `scripts/check-security-boundaries.mjs`
    - usage 상태 로더가 backup recovery helper를 쓰는지 계약 검사에 추가했다.
- doctor detail에 파일별 위험을 더 구체화. 완료:
  - `WorkspaceDoctorCheck`가 손상 JSON 상세를 `corruptJsonFiles=상대경로:backup=yes|no` 형태로 노출한다.
  - `.bak`가 없는 손상 JSON이 있으면 별도 suggested action을 추가한다.
  - `WorkspaceDoctorCheckTests`가 backup 유무가 섞인 손상 JSON을 경고와 detail로 고정했다.
- 상태 파일 inventory 작성. 완료:
  - `docs/환경변수_및_상태파일.md`
    - JSON 상태 저장소별 기본 위치, override 환경변수, 원자 쓰기/백업 복구 여부를 표로 정리했다.
    - 실행 산출물과 Markdown 문서는 JSON 상태 저장소와 분리해 기록했다.
  - `scripts/check-security-boundaries.mjs`
    - 상태 저장소 inventory 문서가 핵심 상태 파일과 백업 복구 기준을 포함하는지 계약 검사에 추가했다.
- 추가 백업 복구 확대. 완료:
  - `FileRoutingPolicyStore`
    - `routing-policy.json` 로드 시 `AtomicFileStore.ReadAllTextWithBackup`을 사용한다.
  - `GuardRetryTimelineStore`
    - `guard_retry_timeline.json` 로드 시 `AtomicFileStore.ReadAllTextWithBackup`을 사용한다.
  - `RoutingPolicyTests`, `GuardRetryTimelineStoreTests`
    - 손상 primary → 유효 `.bak` 복구를 단위 테스트로 고정했다.

## 진척도 스냅샷

업데이트 기준: 2026-05-19

| 우선순위 | 상태 | 완료율 | 남은 핵심 작업 |
|---|---|---|---|
| P0. 작업트리 커밋 기준점 | 완료 | 100% | — |
| P1. 문서 불일치 정리 | 완료 | 100% | — |
| P2. WebSocket runtime 통합 테스트 | 완료 | 100% | — |
| P3. CommandService 도메인 분리 | 진행 중 | 95% | exception recovery/loop recovery orchestration 추가 축소, Telegram `/llm` 세부 설정 핸들러 서비스화, coding/routine/logic graph 서비스 단위 추출, SearchPipeline Gemini 호출 orchestration 축소 |
| P4. Provider adapter 구조 정리 | 완료 | 100% | — (usage capture/continuation loop는 turn-state 결합으로 추가 분리 미적용) |
| P5. 상태 저장소 복구 정책 확대 | 완료 | 100% | — |

전체 산술 평균: 99.2% (P0 100, P1 100, P2 100, P3 95, P4 100, P5 100 → 평균 99.2%).

이 수치는 책임 분량을 동등 가중치로 본 추정이다. P3는 SearchPipeline 정책/포매터/README 로더, Telegram 응답 포매터/프롬프트/후속질문/자연어 명령/pseudo command executor/LLM preference 정책, 공통 대화 맥락 정책, 코딩 언어/진행상태/프롬프트/루프 계획 파서/생성 코드 텍스트/fallback/대화 제목/대화 히스토리/chat output sanitizer/multi comparison/code candidate/코딩 실행 안전성/품질 브리프/루프 튜닝/deterministic stdout repair/UI clone scaffold/web shooter scaffold/artifact cleanup/loop action executor/fallback decision/Groq fallback 응답 판정/provider model selection/memory note selection/expected output parsing/structured repair plan 정책 추출이 진행됐지만 Telegram `/llm` 세부 설정 핸들러와 일부 exception recovery/loop recovery orchestration도 한 타입에 남아 있어, 실제 코드 양 기준으로 가중치를 다시 잡으면 90% 중반이 더 보수적이다. P4는 provider별 HTTP 호출/SSE/citation dedup이 adapter/parser/policy/accumulator로 분리된 상태이며, 잔여 항목(usage capture/continuation loop)은 turn-state 결합으로 추가 분리의 이득이 작아 마무리로 간주한다.

## 운영 판단

- 이 프로젝트는 기능적으로는 이미 개인 운영 도구 이상의 범위를 갖췄다.
- 단기 품질은 보안 경계와 contract test가 받치고 있다.
- 중기 리스크는 거대한 중심 타입과 문서 drift다.
- 다음 개발은 새 기능 확장보다 문서 일관성, runtime 통합 테스트, 도메인 분리 순서가 더 안전하다.
