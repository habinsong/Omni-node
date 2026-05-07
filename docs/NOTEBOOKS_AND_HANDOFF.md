# Omni-node 노트북 / Handoff

업데이트 기준: 2026-05-07

이 문서는 현재 코드 기준의 `노트북` 탭, notebook 저장소, handoff 생성 흐름을 정리합니다.

## 1. 현재 범위

노트북은 설정 탭 하위 패널이 아니라 좌측 루트 메뉴의 독립 탭입니다.

지원 범위:

- 프로젝트별 `learnings.md`, `decisions.md`, `verification.md`, `handoff.md` 영속 저장
- 대시보드 `노트북` 탭에서 기록 작성, 빠른 기록, 현재 상태, 다음 액션, 문서 보기 제공
- 웹 슬래시 명령과 텔레그램 명령
- WebSocket `notebook_get`, `notebook_append`, `handoff_create`
- plan, task graph, doctor, refactor 결과를 notebook 초안으로 옮기는 빠른 기록 버튼

핵심 원칙:

- 비워 둔 `projectKey`는 현재 프로젝트 루트 기준으로 자동 계산됩니다.
- append는 markdown 문서에 `## <Kind> · <timestamp>` 블록으로 누적됩니다.
- handoff는 learnings / decisions / verification 미리보기를 모아 `handoff.md`를 다시 생성합니다.
- notebook append와 handoff 생성은 현재 LLM을 쓰지 않고 규칙 기반으로 동작합니다.

## 2. 저장 경로

기본 상태 루트는 `~/.omninode`입니다.

```text
~/.omninode/notebooks/<project-key>/learnings.md
~/.omninode/notebooks/<project-key>/decisions.md
~/.omninode/notebooks/<project-key>/verification.md
~/.omninode/notebooks/<project-key>/handoff.md
```

정리 기준:

- 이 경로는 재생성 캐시가 아니라 세션 간 인수인계 원본입니다.
- 다음 세션이 현재 작업을 이어받아야 하면 삭제하면 안 됩니다.
- 상태 파일은 `workspace/`가 아니라 `~/.omninode/notebooks/` 아래에 남습니다.

## 3. 대시보드 사용 흐름

### 3.1 상단 요약

노트북 탭 상단은 현재 문서 상태를 빠르게 보여줍니다.

- `문서 커버리지`: 배운 점, 결정, 검증, handoff 중 채워진 문서 수
- `현재 작성 대상`: 지금 선택한 기록 종류
- `초안 길이`: 현재 입력된 본문 길이
- `작성 엔진`: handoff 생성 방식. 현재는 LLM 미사용, 규칙 기반

### 3.2 기록 작성

기록 작성 영역에서 아래를 한 번에 조정합니다.

- `프로젝트 키`: 직접 입력하거나 현재 프로젝트 키 사용
- `저장 루트`: 현재 notebook 저장 위치 확인
- `배운 점`: 반복해서 얻은 교훈
- `결정`: 왜 이 방향으로 결정했는지
- `검증`: 실제 확인한 결과와 남은 리스크

본문 입력 영역은 여러 줄 기록을 전제로 크게 잡혀 있습니다. 짧은 한 줄 메모보다 다음 세션이 바로 읽고 이어갈 수 있는 문장을 남기는 것이 목적입니다.

### 3.3 빠른 기록

빠른 기록은 다른 화면에서 생긴 결과를 노트북 초안으로 옮기는 기능입니다.

- `선택 plan -> decision`
- `선택 graph -> verification`
- `doctor -> verification`
- `refactor -> verification`

버튼은 바로 저장하지 않고 초안에 넣습니다. 내용을 사람이 확인한 뒤 저장하는 흐름입니다.

### 3.4 현재 상태와 다음 액션

우측 영역은 현재 notebook 상태를 보여주고 다음에 채울 항목을 제안합니다.

- project key와 저장 루트
- 최근 동기화 시점
- handoff 생성 방식
- 기존 결정이 비어 있는지
- 검증 결과가 비어 있는지
- 재사용할 교훈이 없는지
- handoff 문서가 아직 없는지

`다음 액션` 영역은 빠른 기록 영역과 시각적으로 같은 하단 높이를 유지하도록 배치되어, 넓은 화면에서도 좌우 길이가 어긋나지 않게 구성됩니다.

### 3.5 문서 보기

문서 보기는 네 문서를 카드로 분리합니다.

- `배운 점`
- `결정`
- `검증`
- `인수인계`

넓은 화면에서는 2열 구조로 배치되고, 좁은 화면에서는 1열로 내려갑니다. 인수인계는 검증 오른쪽에 위치하는 것이 기준이며, 화면 폭이 부족할 때만 아래로 이동합니다.

## 4. 명령 사용

웹 명령창과 텔레그램에서 공통으로 사용할 수 있습니다.

```text
/notebook show [project-key]
/notebook append <learning|decision|verification> <내용>
/handoff [project-key]
```

예시:

```text
/notebook show
/notebook append decision plan_run은 task graph를 통해 실행한다
/notebook append verification doctor 결과에서 fail check는 없었다
/handoff
```

텔레그램에서는 slash 없이도 아래처럼 요청할 수 있습니다.

```text
노트북 보여줘
verification 노트에 doctor 경고 없음 추가해
handoff 만들어줘
```

## 5. 바로 쓰는 템플릿

### 5.1 learning

```text
상황:
- 무엇이 반복해서 막혔는가

배운 점:
- 다음 세션에서 바로 적용할 규칙

유지 기준:
- 어떤 증상, 로그, 결과를 보고 그렇게 판단했는가
```

### 5.2 decision

```text
결정:
- 무엇을 하기로 했는가

이유:
- 왜 이 방향을 선택했는가

보류한 대안:
- 이번에는 하지 않기로 한 선택지
```

### 5.3 verification

```text
검증 대상:
- 무엇을 확인했는가

실행:
- 어떤 명령, 어떤 UI 흐름, 어떤 보고서를 사용했는가

결과:
- 성공/실패와 핵심 수치 또는 메시지
```

## 6. 권장 운영 루틴

1. 작업 도중 반복 교훈은 `learning`
2. 방향을 바꾼 이유는 `decision`
3. 실제 실행/검증 결과는 `verification`
4. 세션 종료 직전 `handoff 생성`

handoff를 만들기 전에 최소 아래 세 줄은 채우는 편이 안전합니다.

1. 가장 최근 decision 1개 이상
2. 가장 최근 verification 1개 이상
3. 다음 세션이 바로 이어받을 수 있는 next action 한 줄

## 7. 확인 포인트

사용자가 직접 확인할 때는 아래만 보면 됩니다.

1. 노트북 탭에서 decision append
2. verification append
3. 빠른 기록 초안 생성
4. handoff 생성
5. `~/.omninode/notebooks/<project-key>/` 아래 네 문서 갱신 확인
