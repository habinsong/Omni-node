# 노트북 / 이어보기

[한국어](./NOTEBOOKS_AND_HANDOFF.md) · [English](./en/notebooks-and-handoff.md)

업데이트 기준: 2026-05-19

노트북은 작업 중 남길 말을 모으는 곳이다. LLM 답변을 다시 요약하는 기능이 아니라, 사람이 다음 세션에서 바로 이어갈 수 있게 작업 메모, 결정, 검증, handoff를 남긴다.

![노트북 탭](./assets/readme/dashboard-notebooks-tab.png)

## 문서 종류

| 문서 | 파일 |
|---|---|
| 작업 메모 | `learnings.md` |
| 방향 정리 | `decisions.md` |
| 확인한 내용 | `verification.md` |
| 다음에 이어볼 것 | `handoff.md` |

기본 저장 위치는 `~/.omninode/notebooks/<project-key>/`다.

## 사용 흐름

1. 작업 중 생긴 메모를 기록한다.
2. 방향이 바뀌면 결정 내용을 남긴다.
3. 실제로 확인한 결과를 검증 기록에 쓴다.
4. 세션이 끝나기 전 handoff를 만든다.

웹 명령과 텔레그램 명령 모두 `/notebook`, `/handoff` 계열을 사용한다.
