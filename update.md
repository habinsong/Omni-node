# Omni-node v1.0.5 업데이트 내역

작성일: 2026-05-15
문서 최신화: 2026-05-21

> **정정 (2026-05-21)**: 아래 v1.0.5 본문의 "외부 제한 모드에서 대화/코딩/루틴/로직 그래프 실행 허용" 설명은 이후 보안 정책이 강화되면서 더 이상 유효하지 않다. **현재 외부 제한 모드는 모든 작업 실행(대화/코딩/루틴/로직 그래프/task graph/refactor/tool)을 차단하고, 읽기 중심 조회와 모델/라우팅 설정만 허용한다.** 권한표의 최신 기준은 [아키텍처 문서](./docs/아키텍처_흐름.md#외부접속-제한-모드-권한표)와 [검증 가이드](./docs/검증_가이드.md)를 따른다. 아래 v1.0.5 내역은 당시 릴리스 기록으로 보존한다.

이번 업데이트는 v1.0.4 이후 확인한 외부접속 제한 모드와 문서/릴리스 위생을 정리한 패치다. 외부 접속에서 OTP 요청 자체를 제거하고 제한 모드로 자동 진입하게 했으며, 원격에서 허용할 작업과 차단할 인증/시크릿/외부접속 설정을 UI·서버·문서·계약 테스트에 같은 기준으로 맞췄다. `./scripts/Omni-node setup` 실행 흐름도 README와 docs에 명시했다.

## 핵심 요약

- 외부접속 클라이언트는 OTP 요청 없이 제한 모드로 자동 진입한다.
- 외부 제한 모드에서 대화, 코딩, 루틴, 로직 그래프, 노트북, 작업 계획, 라우팅 정책, 모델 선택은 허용한다.
- 외부 제한 모드에서 OTP/CLI 인증, Telegram/LLM 키, 외부접속 토글 변경은 차단한다.
- 서버 차단 메시지를 `forbidden_remote_auth`, `forbidden_remote_secret_settings`, `forbidden_remote_external_access`로 세분화했다.
- 설정 탭의 외부 제한 패널에 허용/차단 권한표를 표시한다.
- 보안 경계 계약 테스트에 원격 제한 모드, 로직 그래프 허용, 모델/라우팅 허용, 세분화된 차단 메시지를 추가했다.
- README와 docs에 `./scripts/Omni-node setup` 및 Windows `.\scripts\Omni-node.ps1 setup` 흐름을 명시했다.
- `package.json`과 `package-lock.json` 버전을 1.0.5로 맞췄다.

## 외부접속 제한 모드

### 자동 진입

- 원격 대시보드 클라이언트는 pending 세션 생성 직후 12시간 제한 세션으로 표시된다.
- 원격 클라이언트에는 `authToken`을 발급하지 않고 `remoteLimited=true` 상태만 전달한다.
- 대시보드는 외부 접속 상태를 `외부 접속 제한 모드`로 표시하고, 저장된 로컬 인증 토큰으로 `resume_auth`를 보내지 않는다.

### 허용되는 작업

- 대화, 코딩, 루틴 실행
- 로직 그래프 목록, 열기, 경로 탐색, 저장, 삭제, 실행, 취소, 실행 결과 조회
- 노트북, 작업 계획, 라우팅 정책, 모델 목록 조회, 모델 선택

### 차단되는 작업

- OTP 요청과 인증 재개
- Copilot/Codex CLI 인증 상태 조회, 로그인, 로그아웃
- Telegram/LLM 키 저장, 삭제, 테스트
- 외부접속 토글 변경

## 문서 최신화

- `README.md`, `README.en.md`, `docs/QUICKSTART.md`, `docs/en/quickstart.md`에 setup 명령을 추가했다.
- `docs/아키텍처_흐름.md`, `docs/en/architecture.md`에 외부접속 제한 모드 권한표를 정리했다.
- `docs/검증_가이드.md`, `docs/en/validation.md`, 수동 회귀 체크리스트에 원격 제한 모드 검증 항목을 추가했다.
- `docs/README.md`, `docs/en/README.md`, 사용법/디렉터리 문서를 v1.0.5 기준으로 갱신했다.

## 변경된 주요 영역

- 대시보드 UI: `apps/omninode-dashboard/app.js`, `apps/omninode-dashboard/modules/dashboard-settings-renderers.js`, `apps/omninode-dashboard/modules/dashboard-server-message-router.mjs`, `apps/omninode-dashboard/modules/error-messages.js`, `apps/omninode-dashboard/styles.css`
- 미들웨어 인증/설정/로직: `apps/omninode-middleware/src/AuthSessionGateway.cs`, `apps/omninode-middleware/src/WebSocketGateway.SocketLoop.cs`, `apps/omninode-middleware/src/WsSetupCommandDispatcher.cs`, `apps/omninode-middleware/src/WsLogicCommandDispatcher.cs`
- 문서와 버전: `README.md`, `README.en.md`, `docs/**/*.md`, `update.md`, `package.json`, `package-lock.json`
- 계약 테스트: `scripts/check-security-boundaries.mjs`, `scripts/check-logic-tab-contract.mjs`, `apps/omninode-dashboard/check-dashboard-server-message-router.mjs`

## 검증한 명령

```bash
node scripts/check-security-boundaries.mjs
node scripts/check-logic-tab-contract.mjs
node apps/omninode-dashboard/check-dashboard-server-message-router.mjs
dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj
npm test
```

위 검증 명령은 모두 통과했다.

## 비전공자용 설명

v1.0.5는 외부접속을 “OTP를 다시 요구하는 원격 화면”이 아니라 “작업 기능은 쓰되 민감 설정만 막는 제한 모드”로 정리한 업데이트다.

이제 같은 LAN에서 접속한 외부 클라이언트는 OTP 요청 화면으로 빠지지 않는다. 대신 제한 모드 패널에서 무엇이 허용되고 무엇이 차단되는지 바로 볼 수 있다.

로직 그래프 작업은 외부에서도 계속 사용할 수 있다. 목록을 보고, 열고, 경로를 탐색하고, 저장/삭제/실행/취소/결과 조회까지 가능하다. 모델 선택과 라우팅 정책도 사용자가 바꿀 수 있도록 남겨 두었다.

반대로 OTP 요청, CLI 로그인/로그아웃, Telegram/LLM 키 변경, 외부접속 토글 변경은 막는다. 서버와 대시보드는 이 차단 이유를 인증, 시크릿, 외부접속 설정으로 나눠 보여준다.

또한 처음 설치하는 사람이 헷갈리지 않도록 `./scripts/Omni-node setup`과 `.\scripts\Omni-node.ps1 setup`을 README와 docs에 명확히 넣었다.

## 내부 구조 리팩터링 (2026-05-21)

기능 동작 변경 없이 미들웨어 중심 타입(`CommandService`, `LlmRouter`)의 순수 로직을 단위 테스트 가능한 정책/파서/리졸버 클래스로 분리하는 작업을 진행했다. 외부 API·동작·보안 경계는 그대로다.

- 순수 판정·파싱·프롬프트·resolver 로직을 검색/코딩/대화/텔레그램/루틴/로직 그래프/provider 도메인별 정책 클래스로 추출했다. 대표: `SearchQueryPolicy`, `CodingWorkerSelectionPolicy`, `ChatRetryGuardPolicy`, `TelegramLlmControlCommandParser`, `RoutineSchedulePolicy`, `LogicGraphValidationPolicy`, `LogicTemplateResolver`, `LogicLeafNodeExecutor`, `OpenAiCompatibleProtocol`, `ProviderTimeoutPolicy` 등.
- `CommandService`/`LlmRouter`로 단순 위임만 하던 wrapper 메서드 다수를 제거하고 호출부를 정책 직접 호출로 정리했다.
- `LogicExecutionContext`/`LogicNodeExecutionOutcome` 같은 실행 컨텍스트 타입을 top-level로 승격해 노드 실행기를 정책으로 분리할 수 있게 했다.
- 인스턴스 서비스(LLM 호출, 파일 IO, 대화 저장소)에 강결합된 로직 그래프 노드 실행기(file/web/chat/coding/session/cron/browser/routine)와 exception/loop recovery orchestration은 추출 시 회귀 위험이 이득보다 커 현재 위치에 유지했다. 진척 상세는 `develop.md` 참고.

### 검증 (2026-05-21 기준)

```bash
dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj   # 경고 0
dotnet test apps/omninode-middleware-tests/OmniNode.Middleware.Tests.csproj   # 841 통과
node scripts/check-security-boundaries.mjs                          # assertions 721
node scripts/check-coding-python-game-contract.mjs                  # assertions 106
npm test                                                            # 전체 통과
make -C apps/omninode-core -B
```
