# Codex 무한 루프 자동 개발 실행 가이드

업데이트 기준: 2026-05-15

## 개요
이 문서는 Gemini 검색 리트리버 전환 작업을 Codex로 반복 실행하기 위한 자동 루프 사용법을 설명한다.

현재 운영 기준에서는 이 문서를 과거 검색 전환 루프 기록/가이드로 본다. 최신 대시보드 사용 흐름은 상위 `docs/사용법_빠른시작.md`, 검색/RAG 관측은 `docs/도구_통합_패널_사용_가이드.md`를 우선 확인한다.

루프마다 아래 파일이 자동 생성된다.
- `01_work_done.md`
- `02_remaining_tasks.md`
- `03_unresolved_errors.md`
- `04_passed_runs.md`
- `05_changed_files.md`
- `06_next_loop_focus.md`

누적 상태 파일:
- `runtime/state/CURRENT_STATUS.md`
- `runtime/state/CURRENT_REMAINING_TASKS.md`
- `runtime/state/CURRENT_UNRESOLVED_ERRORS.md`
- `runtime/LOOP_INDEX.md`

## 단계 운영 규칙(P0~P7)
- 활성 단계에서 구현 가능한 최소 단위 1개 이상 수행한다.
- 단계 완료 기준 충족 시 상태 파일을 갱신하고 자동 승급한다.
- 외부 의존만 남은 항목은 비차단 백로그로 분리한다.
- 작성한 파일의 코드 줄 수는 2000줄을 넘지 않도록 한다.
- 유지보수 편리성과 가독성을 위해 한 파일에 기능을 과도하게 누적시키지 말고, 파편화가 필요하면 적절히 분리한다.
- 기존 외부 검색 API 및 관련 코드는 모두 제거하여 사용하지 않는다. (Gemini Search(Gemini grounding 기반 최신 정보 검색)로 단일화하는게 이번 프로젝트의 목표다.)

## 키 정책 강제 규칙
- 설정 탭 저장값을 사용한다. 그중, Gemini 관련은 설정탭에 저장된 Gemini API Key(키체인 저장됨) 를 사용한다.
- macOS는 keychain 저장값을 사용한다.
- Windows/Linux는 600 권한 파일 또는 로컬 secure store 값을 사용한다.

## 실행 방법

### 1) 루프 시작
```bash
bash docs/gemini-retriever-plan/loop-automation/run_codex_dev_loop.sh
```

### 2) 루프 상태 확인
```bash
bash docs/gemini-retriever-plan/loop-automation/status_codex_dev_loop.sh
```

### 3) 안전 정지 요청
```bash
bash docs/gemini-retriever-plan/loop-automation/stop_codex_dev_loop.sh
```

정지 요청은 현재 루프 완료 후 적용된다.

## 환경변수(선택)
- `CODEX_BIN` 기본: `codex`
- `CODEX_MODEL` 기본: 빈값
- `CODEX_EXEC_EXTRA_FLAGS` 기본: 빈값
- `MAX_LOOPS` 기본: `0`
- `LOOP_SLEEP_SEC` 기본: `2`
- `AUTO_CLEAR_STOP` 기본: `1`
- `OMNINODE_ACP_OPTION_SMOKE_PROMOTE_WRITE_RESULT_TO_PREVIOUS` 기본: `1`
