# 정리 기준

[한국어](./CLEANUP.md) · [English](./en/cleanup.md)

업데이트 기준: 2026-05-19

Omni-node는 작업 산출물이 많이 생긴다. 지워도 되는 캐시와 보존해야 하는 상태를 구분해야 한다.

대시보드 도구 통합 패널의 `cleanup.preview`는 정리 후보를 먼저 보여주고, `cleanup.apply`는 선택된 previewId가 있을 때만 삭제를 수행한다. 기본 후보는 `apps/.runtime`, `workspace/.runtime`, `apps/**/bin`, `apps/**/obj`, `.DS_Store`처럼 재생성 가능한 ignored 산출물이다.

## 보통 지워도 되는 것

| 경로 | 이유 |
|---|---|
| `node_modules/` | `npm ci`로 복구 가능 |
| `apps/omninode-middleware/bin/`, `obj/` | .NET 빌드 산출물 |
| `workspace/coding/venv/` | 재생성 가능한 가상환경 |
| `output/playwright/` | 재생성 가능한 회귀 스크린샷 |

## 먼저 확인해야 하는 것

| 경로 | 이유 |
|---|---|
| `workspace/coding/runs/` | 코딩 결과 파일이 들어 있음 |
| `workspace/coding/routines/` | 루틴 실행 결과와 다운로드 자산 |
| `workspace/.runtime/logic/` | 로직 실행 추적 |
| `workspace/.runtime/tasks/` | task graph 로그 |

## 함부로 지우면 안 되는 것

`~/.omninode` 아래는 설정과 기록의 원본이다. 대화, 계획, 노트북, 라우팅 정책, 텔레그램 offset이 들어 있으므로 백업 없이 삭제하지 않는다.
