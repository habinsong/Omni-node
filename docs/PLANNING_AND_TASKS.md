# 자동화 / 계획 / Task Graph

[한국어](./PLANNING_AND_TASKS.md) · [English](./en/planning-and-tasks.md)

업데이트 기준: 2026-05-21

작업 계획 탭은 큰 요청을 계획, 리뷰, 승인, 실행 흐름으로 나누는 화면이다. 로직 탭의 사용자 편집 그래프와 다르게, task graph는 승인된 plan을 실행 단위로 쪼개기 위한 내부 실행 그래프다.

![작업 계획 탭](./assets/readme/dashboard-plans-tab.png)

## 화면 구조

- 계획: 요청 작성, 제약사항, fast/interview 생성, 리뷰, 승인, 실행
- 태스크 그래프: plan 기반 graph 생성, 실행, task 상태와 output 확인
- 라우팅: planner/reviewer와 task category별 provider/fallback 설정

## 저장 위치

| 위치 | 내용 |
|---|---|
| `~/.omninode/plans/` | plan, review, execution 상태 |
| `~/.omninode/tasks/` | task graph 원본 |
| `workspace/.runtime/tasks/` | task stdout/stderr/result.json |
| `~/.omninode/routing-policy.json` | 라우팅 override |

계획 생성과 리뷰는 LLM을 사용하고, task graph 생성은 승인된 plan을 규칙 기반으로 분해한다.
