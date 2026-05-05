# Omni-node Doctor 가이드

업데이트 기준: 2026-05-05

이 문서는 `doctor` 진단 기능의 실행 방법, 점검 항목, 결과 저장 위치를 정리한다.

## 1. 실행 방법

서버를 띄우지 않고 단독 실행:

```bash
dotnet run --project apps/omninode-middleware/OmniNode.Middleware.csproj -- doctor
```

JSON 출력:

```bash
dotnet run --project apps/omninode-middleware/OmniNode.Middleware.csproj -- doctor --json
```

## 2. 점검 항목

현재 doctor는 아래 항목을 점검한다.

- `core_socket`: 코어 IPC 응답(macOS/Linux UDS, Windows 로컬 TCP), lock 경로, 감사 로그 부모 경로
- `workspace`: `OMNINODE_WORKSPACE_ROOT`, `workspace/.runtime`, `~/.omninode/doctor` 쓰기 가능 여부
- `sandbox`: Python sandbox smoke (`print('ok')`)
- `sqlite`: `sqlite3 --version` 실행 가능 여부
- `provider_secrets`: Gemini, Groq, Cerebras 시크릿 존재 여부와 secure file 권한
- `codex`: Codex CLI 설치/인증 상태
- `copilot`: Copilot CLI 설치/인증 상태
- `telegram`: Telegram Bot Token / Chat ID / Allowed User ID 상태
- `search_pipeline`: Gemini grounded search 기본 구성과 guard/composer 조립 상태

`core_socket` 해석 주의:

- macOS/Linux에서 stale socket 파일만 남아 있거나 `Connection refused`가 나오던 상태는 현재 미들웨어 시작 시 자동 부트스트랩으로 먼저 복구를 시도한다.
- `apps/omninode-core/omninode_core` 또는 Windows `apps/omninode-core/omninode_core.exe` 바이너리가 있으면 doctor 전에 `core_socket`이 자동으로 `ok`까지 회복될 수 있다.

## 3. 대시보드와 텔레그램

- 대시보드: 설정 탭의 `환경 진단` 패널에서 최근 보고서 조회와 새 실행 가능
- 텔레그램: `/doctor`, `/doctor json`
- 텔레그램 자연어: `환경 진단해줘`도 같은 진단 경로로 연결됨

웹 상태 엔드포인트 별칭:

- `/healthz`, `/readyz`
- `/health`, `/ready`

## 4. 상태 파일 위치

보고서는 기본적으로 아래에 저장된다.

- `~/.omninode/doctor/last-report.json`
- `~/.omninode/doctor/history/<timestamp>.json`

`OMNINODE_DOCTOR_WRITE_HISTORY=false`면 `history/` 기록은 남기지 않고 마지막 보고서만 갱신한다.

## 5. 관련 환경변수

| 환경변수 | 기본값 | 의미 |
|---|---|---|
| `OMNINODE_DOCTOR_TIMEOUT_SECONDS` | `15` | 개별 check 타임아웃 |
| `OMNINODE_DOCTOR_ENABLE_SANDBOX_SMOKE` | `true` | 샌드박스 smoke 실행 여부 |
| `OMNINODE_DOCTOR_WRITE_HISTORY` | `true` | history JSON 누적 저장 여부 |

## 6. 해석 기준

- `ok`: 현재 구성으로 사용 가능
- `warn`: 기능은 일부 동작하거나 선택 구성이라 즉시 장애는 아니지만 보완 필요
- `fail`: 핵심 경로 또는 설정에 실제 문제가 있음
- `skip`: 현재 실행에서 의도적으로 생략됨
