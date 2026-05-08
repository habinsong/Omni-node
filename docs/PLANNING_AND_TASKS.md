# Omni-node 자동화 / 계획 / Task Graph

업데이트 기준: 2026-05-08

이 문서는 현재 코드 기준의 `자동화/계획` 탭, planning 저장소, background task graph 실행 흐름, LLM 라우팅 UI를 정리합니다.

주의:

- `자동화/계획` 탭의 task graph는 계획 기반 실행기입니다.
- 대시보드 `로직` 탭의 로직 그래프는 사용자가 직접 노드 캔버스를 편집하는 `logic_graph` 실행기입니다.
- 둘 다 그래프 형태를 쓰지만 저장 위치, 실행 목적, UI가 다르므로 같은 기능으로 보면 안 됩니다.

## 1. 현재 범위

자동화/계획 탭은 설정 탭 하위 패널이 아니라 좌측 루트 메뉴의 독립 탭입니다.

화면은 내부 좌측 선택 영역으로 먼저 나뉩니다.

- `계획`: 계획 생성, 리뷰, 승인, 실행, 계획용 LLM 라우팅
- `태스크 그래프`: plan 기반 graph 생성, graph 실행, task output, graph 실행 라우팅

구현 범위:

- 계획 생성
- 계획 리뷰
- 계획 승인
- 승인된 계획 실행
- `fast` / `interview` 생성 모드
- planner / reviewer LLM 라우팅 상태 조회와 저장
- plan -> task graph 생성
- task graph 목록/상세 조회
- task graph 실행
- 개별 task 취소
- task stdout/stderr/result.json 조회
- task category별 graph 실행 라우팅 상태 조회와 저장
- `~/.omninode/plans/`, `~/.omninode/tasks/`, `workspace/.runtime/tasks/` 상태 복구

## 2. LLM 사용 기준

자동화/계획 안의 모든 동작이 LLM을 쓰는 것은 아닙니다.

| 영역 | LLM 사용 | 설명 |
|---|---|---|
| 계획 생성 | 사용 | planner 라우트 provider/model/fallback chain 사용 |
| 계획 리뷰 | 사용 | reviewer 라우트 provider/model/fallback chain + 휴리스틱 점검 |
| 계획 승인 | 미사용 | 상태 전환 |
| 계획 실행 | 간접 사용 | 승인된 plan으로 task graph를 만들고 graph 실행 시작 |
| task graph 생성 | 미사용 | plan을 규칙 기반으로 DAG 분해 |
| graph 실행 | task별 상이 | category에 따라 LLM 라우팅 또는 command 실행 사용 |
| task output 조회 | 미사용 | 저장된 stdout/stderr/result.json 조회 |

계획 리뷰는 단계 수 부족, 제약 누락, 검증 공백, rollback 부재 같은 휴리스틱 위험을 먼저 잡고, reviewer 모델 요약을 함께 붙입니다.

## 3. 저장 경로

기본 상태 루트는 `~/.omninode`입니다.

Planning:

```text
~/.omninode/plans/index.json
~/.omninode/plans/<plan-id>/plan.json
~/.omninode/plans/<plan-id>/review.json
~/.omninode/plans/<plan-id>/execution.json
```

Task Graph:

```text
~/.omninode/tasks/index.json
~/.omninode/tasks/<graph-id>.json
workspace/.runtime/tasks/<graph-id>/<task-id>/stdout.log
workspace/.runtime/tasks/<graph-id>/<task-id>/stderr.log
workspace/.runtime/tasks/<graph-id>/<task-id>/result.json
```

Routing policy:

```text
~/.omninode/routing-policy.json
```

## 4. 대시보드 구조

### 4.1 좌측 내부 선택

자동화/계획 탭 안에는 내부 좌측 선택 영역이 있습니다.

- `계획`: 사람이 먼저 범위와 의도를 정리하는 화면
- `태스크 그래프`: 승인된 계획을 실제 실행 단위로 쪼개고 실행하는 화면

한 화면에 계획과 그래프를 모두 쌓지 않습니다. 넓은 화면에서도 정보가 좌우로 몰리지 않도록 선택한 작업 영역만 표시합니다.

### 4.2 계획 화면

계획 화면은 아래 흐름을 기준으로 봅니다.

1. 요청과 제약사항 입력
2. `fast` 또는 `interview` 모드 선택
3. 계획 생성
4. 계획 리뷰
5. 승인
6. 실행

계획용 LLM 영역은 planner와 reviewer를 분리해서 보여줍니다.

- `planner`: 계획 생성용 provider/model
- `reviewer`: 계획 리뷰용 provider/model
- `fallback chain`: 우선 provider 실패 시 다음 후보 순서

Groq와 Copilot은 대시보드에서 모델 선택 후 적용할 수 있습니다. Gemini, Cerebras, Codex는 현재 기본 모델 표시 중심으로 동작합니다.

### 4.3 태스크 그래프 화면

태스크 그래프 화면은 아래 흐름을 기준으로 봅니다.

1. 승인된 plan 선택
2. graph 생성
3. graph 실행
4. task 상태, dependency, 로그 tail 확인
5. running/pending task 취소
6. 개별 task의 `stdout`, `stderr`, `result.json` 확인

graph 실행 라우팅은 자주 보는 category를 먼저 노출합니다.

- `visualUi`: UI 작업
- `quickFix`: 빠른 수정/검증
- `safeRefactor`: 안전 리팩터

보조 라우팅은 접힌 영역에서 관리합니다.

- `backgroundMonitor`
- `documentation`
- `searchFallback`

이 구조는 화면 폭이 좁아져도 카드가 밖으로 밀려나지 않도록 provider/model/fallback 정보를 작게 분리해서 표시하는 것이 기준입니다.

## 5. 현재 실행 의미

`Run plan`은 아래 흐름으로 동작합니다.

1. 승인된 plan으로 새 task graph 생성
2. 생성된 graph를 즉시 실행
3. plan 상태를 `Running`으로 저장
4. background monitor가 graph 종료를 감시
5. 종료 후 `execution.json`과 plan 상태를 `Completed` 또는 `Approved`로 갱신

Task Graph 실행기는 아래 흐름으로 동작합니다.

1. 선택한 plan으로 graph 생성
2. graph 안의 ready node를 category 기준으로 실행
3. coding/refactor/documentation/verification은 단일 workspace lane에서 순차 실행
4. analysis/research 계열은 병렬 실행 가능
5. 상태와 로그를 파일로 남기고 세션 재접속 후 다시 조회 가능

실무 권장 흐름:

1. 큰 작업이면 먼저 `계획` 화면에서 범위를 고정
2. `리뷰`로 빠진 검증과 리스크 확인
3. 승인 후 `Run plan` 또는 `태스크 그래프` 화면으로 실행
4. 끝난 뒤 `노트북` 탭에서 `decision`, `verification` 기록

## 6. 명령 사용

웹 명령창과 텔레그램에서 공통으로 아래 명령을 사용할 수 있습니다.

```text
/plan list
/plan get <plan-id>
/plan create [--mode fast|interview] [--constraint <제약>]... <요청>
/plan review <plan-id>
/plan approve <plan-id>
/plan run <plan-id>
/task list
/task create <plan-id>
/task status <graph-id>
/task run <graph-id>
/task cancel <graph-id> <task-id>
/task output <graph-id> <task-id>
```

텔레그램에서는 slash 없이도 `계획 생성 ...`, `계획 리뷰 plan_...`, `작업 상태 graph_...`, `task output ...` 같은 자연어/command-like 입력이 같은 명령층으로 연결됩니다.

예시:

```text
/plan create AGENTS.md와 첨부 설계를 반영해 doctor 기능 구현
/plan create --constraint 사용자가 요청한 내용 외 변경 금지 --constraint 문서도 같이 수정 대시보드 plans 패널 추가
/plan review plan_20260308123000001
/plan approve plan_20260308123000001
/plan run plan_20260308123000001
/task create plan_20260308123000001
/task run graph_20260308123500001
/task status graph_20260308123500001
```

## 7. 확인 포인트

사용자가 직접 확인할 때는 아래만 보면 됩니다.

1. 자동화/계획 탭에서 `계획` 선택
2. 계획 생성
3. 계획 리뷰
4. 계획 승인
5. 계획 실행
6. `~/.omninode/plans/` 파일 생성 확인
7. `태스크 그래프` 선택
8. 승인된 plan으로 graph 생성
9. graph 실행
10. task output 조회
11. `~/.omninode/tasks/` 및 `workspace/.runtime/tasks/` 파일 생성 확인
