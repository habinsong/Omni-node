# Doctor

[한국어](./DOCTOR.md) · [English](./en/doctor.md)

업데이트 기준: 2026-05-19

Doctor는 Omni-node를 운영할 때 가장 먼저 보는 진단 명령이다. provider 키, CLI 인증, 코어 연결, 상태 파일, 작업공간, 검색/도구 상태를 한 번에 확인한다.

## 실행

```bash
dotnet run --project apps/omninode-middleware/OmniNode.Middleware.csproj -- doctor --json
```

대시보드 설정 탭의 환경 진단 패널에서도 최근 doctor 결과를 볼 수 있다.

도구 통합 패널의 `doctor.fix.preview`는 최근 Doctor 결과와 현재 설정을 바탕으로 적용 가능한 복구 계획을 만든다. `doctor.fix.apply`는 해당 previewId가 있는 경우에만 실행되며, 자동 적용 범위는 누락된 상태/워크스페이스 디렉터리 생성으로 제한된다. API 키 입력, CLI 인증, 파괴적 정리는 자동 실행하지 않는다.

## 읽는 법

- `ok`: 정상
- `warn`: 기능은 돌 수 있지만 확인이 필요
- `fail`: 해당 기능 사용 전에 조치 필요

provider가 `api_key_missing`이면 설정 탭 또는 `*_FILE` 환경변수에서 키를 먼저 설정한다. Copilot/Codex가 CLI 인증을 요구하면 설정 탭의 CLI 인증 영역에서 상태를 확인한다.
