# Safe Refactor

[한국어](./SAFE_REFACTORING.md) · [English](./en/safe-refactoring.md)

업데이트 기준: 2026-05-21

Safe Refactor는 파일을 바로 덮어쓰지 않는다. 먼저 preview를 만들고, apply 직전에 다시 파일 상태를 확인한다.

![Safe Refactor](./assets/readme/dashboard-safe-refactor.png)

## 지원 모드

| 모드 | 설명 |
|---|---|
| Anchor Edit | 줄 범위와 line hash를 기준으로 안전하게 교체 |
| LSP Rename | 언어 서버가 계산한 rename edit를 preview/apply |
| AST Replace | ast-grep pattern/rewrite 결과를 preview/apply |

## 흐름

1. 대상 파일을 읽는다.
2. 교체 범위나 symbol/pattern을 정한다.
3. preview를 만든다.
4. diff를 확인한다.
5. apply 직전에 stale 여부를 다시 검증한다.
6. 파일이 바뀌었으면 적용을 막고 다시 preview를 만들게 한다.

preview는 `workspace/.runtime/refactor-preview/` 아래에 저장된다. 이 파일은 영속 설정이 아니라 작업 산출물이다.
