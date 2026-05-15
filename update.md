# Omni-node v1.0.5 업데이트 내역

작성일: 2026-05-15
문서 최신화: 2026-05-15

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
