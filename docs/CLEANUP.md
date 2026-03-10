# Omni-node 정리 기준

업데이트 기준: 2026-03-10

이 문서는 나중에 저장소를 다시 열었을 때 `무엇을 지워도 되는지`, `무엇을 보존해야 하는지`, `어떤 경로를 기준으로 기억할지`를 빠르게 판단하기 위한 위생 기준표입니다.

## 1. 기준 경로

기본으로 기억할 경로는 아래 세 개뿐입니다.

| 구분 | 기준 경로 | 메모 |
|---|---|---|
| 앱 소스 | `apps/` | 실제 런타임 소스 |
| 문서 | `docs/` | canonical 문서 위치 |
| 작업공간 | `/path/to/Omni-node/workspace/coding` | `OMNINODE_WORKSPACE_ROOT` 기본 예시 |

하위 호환 alias:

- 루트 `coding`, `runtime`, `omninode-*`, 루트 문서 파일은 하위 호환용 심볼릭 링크입니다.
- 기본 예시, 쉘 export 예시, 운영 메모에서는 alias 대신 canonical 경로만 씁니다.

## 2. 두 줄 규칙

- 재생성 가능한 것은 과감히 청소 가능
- 상태 원본과 실행 이력은 청소 전에 보존 여부를 먼저 판단

## 3. 지워도 되는 것과 보존할 것

| 경로 | 분류 | 청소 판단 |
|---|---|---|
| `workspace/coding/venv/` | 재생성 가능한 캐시 | 필요 없으면 삭제 가능 |
| `node_modules/` | 재생성 가능한 캐시 | `npm ci`로 복구 가능 |
| `output/playwright/` | 재생성 가능한 회귀 스크린샷 | 필요 없으면 삭제 가능 |
| `apps/omninode-middleware/bin/` | 빌드 산출물 | 삭제 가능 |
| `apps/omninode-middleware/obj/` | 빌드 산출물 | 삭제 가능 |
| `workspace/.runtime/` | 회귀 결과/임시 분석물 | 보관 이유가 없으면 정리 가능 |
| `workspace/runtime/` | 현재 세션 상태 스냅샷 | 세션 종료 후 필요 없으면 정리 가능 |
| `/tmp/omninode_core.<uid>.sock` | 임시 실행 상태 | 프로세스가 내려간 뒤 꼬였을 때만 정리 |
| `/tmp/omninode.<uid>.lock` | 임시 실행 상태 | 프로세스가 내려간 뒤 꼬였을 때만 정리 |
| `/tmp/omninode_audit.log` | 임시 로그 | 필요 없으면 정리 가능 |
| `workspace/coding/runs/` | 코딩 실행 생성 파일 | 과거 생성 파일이 필요 없을 때만 정리 |
| `workspace/coding/routines/` | 실행 결과 이력 | 과거 실행 기록이 필요 없을 때만 정리 |
| `~/.omninode/code-runs/` | 실행 결과 이력 | 코드 실행 기록이 필요 없을 때만 정리 |
| `~/.omninode/doctor/history/` | 진단 이력 | 과거 진단 기록이 필요 없을 때만 정리 |
| `~/.omninode/plans/` | 상태 원본 | 계획/리뷰/실행 이력을 보존하려면 유지 |
| `~/.omninode/tasks/` | 상태 원본 | task graph 정의와 최근 실행 상태를 보존하려면 유지 |
| `~/.omninode/notebooks/` | 상태 원본 | learnings / decisions / verification / handoff를 보존하려면 유지 |
| `~/.omninode/routines.json` | 상태 원본 | 루틴 정의를 보존하려면 유지 |
| `~/.omninode/conversations.json` | 상태 원본 | 대화 기록을 보존하려면 유지 |
| `~/.omninode/auth_sessions.json` | 상태 원본 | 세션 복구 상태를 보존하려면 유지 |
| `~/.omninode/telegram_update_offset.txt` | 상태 원본 | 텔레그램 polling 재시작 지점을 보존하려면 유지 |
| `~/.omninode/telegram_update_loop.lock` | 런타임 보호 파일 | polling 단일 인스턴스 락. 실행 중이 아니면 stale 여부 확인 후 정리 |
| `~/.omninode/telegram_reply_outbox.json` | 상태 원본 | 텔레그램 응답 재전송 대기 큐. 미전송 답변을 보존하려면 유지 |
| `~/.omninode/memory-notes/` | 상태 원본 | 메모리 노트를 보존하려면 유지 |
| `~/.omninode/memory-index/main.sqlite` | 상태 원본/인덱스 | 메모리 검색 인덱스를 보존하려면 유지 |
| `~/.omninode/doctor/last-report.json` | 상태 원본/최근 진단 | 최신 진단 결과를 보존하려면 유지 |
| `~/.omninode/routing-policy.json` | 상태 원본 | category별 provider override를 보존하려면 유지 |
| `~/.omninode/AGENTS.md` | 전역 문맥 | 지우면 안 됨 |
| `~/.omninode/skills/` | 전역 문맥 | 존재한다면 지우면 안 됨 |
| `~/.omninode/commands/` | 전역 문맥 | 존재한다면 지우면 안 됨 |
| `AGENTS.md` | 프로젝트 문맥 | 지우면 안 됨 |
| `AGENTS.override.md` | 프로젝트 문맥 | 존재한다면 지우면 안 됨 |
| `apps/omninode-middleware/AGENTS.md` | 프로젝트 문맥 | 지우면 안 됨 |
| `apps/omninode-dashboard/AGENTS.md` | 프로젝트 문맥 | 지우면 안 됨 |
| `.omni/skills/` | 프로젝트 문맥 | 존재한다면 지우면 안 됨 |
| `.omni/commands/` | 프로젝트 문맥 | 존재한다면 지우면 안 됨 |

## 4. 상태를 볼 때의 기준

Omni-node 상태는 세 층으로 기억하면 덜 헷갈립니다.

| 층 | 대표 경로 | 의미 |
|---|---|---|
| 영속 상태 | `~/.omninode` | 설정, 세션, 대화, 루틴 정의의 원본 |
| 작업 산출물 | `workspace/` | 루틴 결과, 회귀 아티팩트, 작업 대상 파일 |
| 임시 실행 상태 | `/tmp`, `workspace/.runtime`, `workspace/runtime` | 소켓, 락, 임시 스냅샷 |

루틴은 특히 아래처럼 봅니다.

- 루틴 정의를 살리고 싶으면 `~/.omninode/routines.json`이 중요합니다.
- 코딩 탭에서 만들어진 실제 파일까지 남기려면 `workspace/coding/runs/`를 같이 봐야 합니다.
- 최근 코딩 결과 복원과 다중 코딩 비교 메타를 남기려면 `~/.omninode/conversations.json`도 같이 봐야 합니다.
- 과거 실행 결과까지 남기려면 `workspace/coding/routines/`도 같이 봐야 합니다.
- Task graph를 다시 열어야 하면 `~/.omninode/tasks/`와 `workspace/.runtime/tasks/`를 같이 봐야 합니다.
- 세션 handoff를 다시 열어야 하면 `~/.omninode/notebooks/`를 같이 봐야 합니다.

## 5. 문서에서 무엇을 보면 되는가

| 상황 | 먼저 볼 문서 |
|---|---|
| 프로젝트 구조와 canonical 맵이 궁금할 때 | `README.md` |
| 다시 부팅하고 싶을 때 | `docs/사용법_빠른시작.md` |
| 경로, 상태, 시크릿 기준이 필요할 때 | `docs/환경변수_및_상태파일.md` |
| 지워도 되는지 판단이 필요할 때 | `docs/CLEANUP.md` |
| 검증 명령이 헷갈릴 때 | `docs/검증_가이드.md` |

## 6. 하루 1분 유지보수 체크리스트

1. 기본 워크스페이스 예시를 `workspace/coding` 하나로만 쓰고 있는지 본다.
2. `workspace/.runtime/`, `workspace/runtime/`에 남겨둘 이유 없는 임시 산출물이 쌓였는지 본다.
3. `node_modules/`, `output/playwright/`, `bin/`, `obj/`, `venv/`처럼 다시 만들 수 있는 캐시가 과하게 커졌는지 본다.
4. 지우기 전에 `~/.omninode`, `workspace/coding/runs/`, `workspace/coding/routines/` 중 무엇을 보존해야 하는지 먼저 판단한다.
5. 점검은 `부팅 검증 -> readyz/WS roundtrip 확인 -> 기본 건강검진(npm test 가능 시, repo hygiene gate 포함) -> 샌드박스 직접 실행` 순서로 기억한다.
6. `~/.omninode/doctor/history/`는 보관 이유가 없을 때만 정리하고, 최신 진단이 필요하면 `last-report.json`은 남긴다.
7. `~/.omninode/plans/`는 재생성 캐시가 아니라 계획 원본이므로, 진행 중인 작업을 다시 열 가능성이 있으면 지우지 않는다.
8. `~/.omninode/tasks/`와 `workspace/.runtime/tasks/`는 Task graph 재개 근거가 되므로, 세션 복구가 필요하면 지우지 않는다.
9. `~/.omninode/notebooks/`는 handoff 원본이므로, 다음 세션 인수인계가 필요하면 지우지 않는다.
10. `AGENTS.md`, `AGENTS.override.md`, `.omni/skills/`, `.omni/commands/`는 project context 원본이므로 캐시처럼 지우지 않는다.
11. `~/.omninode/AGENTS.md`, `~/.omninode/skills/`, `~/.omninode/commands/`도 전역 문맥 원본이므로 보존 여부를 먼저 판단한다.
