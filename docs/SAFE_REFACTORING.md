# Omni-node Safe Refactoring

업데이트 기준: 2026-05-07

이 문서는 코딩 탭의 `Safe Refactor` 도크와 미들웨어의 안전한 refactor preview/apply 흐름을 설명한다.

핵심 원칙은 간단하다.

- 파일을 바로 덮어쓰지 않는다.
- 먼저 preview를 만든다.
- apply 직전에 다시 검증한다.
- 그 사이 파일이 바뀌었으면 apply를 막는다.

## 1. 현재 지원 범위

- `refactor_read`: 파일을 읽고 line별 anchor를 만든다.
- `refactor_preview`: 선택 범위와 replacement로 unified diff preview를 만든다.
- `refactor_apply`: 저장된 preview를 현재 파일과 다시 대조한 뒤 적용한다.
- `lsp_rename`: 언어별 LSP 서버를 호출해 symbol rename preview를 만들고 적용한다.
- `ast_replace`: ast-grep rewrite를 호출해 pattern 기반 preview를 만들고 적용한다.

즉, 현재 실사용 경로는 아래 3가지다.

- `anchor read -> preview -> apply`
- `lsp rename -> preview -> apply`
- `ast replace -> preview -> apply`

## 2. 대시보드 사용 순서

코딩 탭의 `요구사항 입력` 바로 위에는 얇게 접힌 `Safe Refactor` 도크가 있고, `열기`를 누르면 중앙 오버레이로 뜬다. 오버레이가 열리면 뒤 배경은 약하게 blur 처리된다.

상단 모드 버튼으로 아래 3가지를 전환한다.

- `Anchor Edit`
- `LSP Rename`
- `AST Replace`

### 2.1 Anchor Edit

1. 파일 경로 입력
2. `Anchor 읽기`
3. line 클릭 또는 시작/끝 line 직접 입력
4. replacement 입력
5. `Preview 만들기`
6. diff 확인
7. `Apply`

apply 뒤에 서버는 같은 파일을 다시 읽어 최신 anchor를 새로 보여준다.

### 2.2 LSP Rename

1. 파일 경로 입력
2. `LSP Rename` 모드 선택
3. 대상 symbol 입력
4. 새 이름 입력
5. `Rename Preview 만들기`
6. diff 확인
7. `Apply`

LSP 서버가 관련 edit를 계산하고, 바뀌는 파일 전체를 preview에 모은다.

### 2.3 AST Replace

1. 파일 경로 입력
2. `AST Replace` 모드 선택
3. pattern 입력
4. rewrite replacement 입력
5. `AST Preview 만들기`
6. diff 확인
7. `Apply`

ast-grep가 반환한 rewrite 결과를 preview/apply로 감싼다.

## 3. 왜 stale apply가 막히는가

모드마다 stale 검증 방식이 다르다.

### 3.1 Anchor Edit

- 절대 경로
- line number
- line content

따라서 preview를 만든 뒤 다음 중 하나라도 바뀌면 apply가 차단된다.

- 같은 줄 내용이 수정됨
- 위쪽 줄 추가/삭제로 line number가 밀림
- 다른 사람이 같은 파일을 먼저 저장함

대시보드에는 mismatch reason과 현재 스니펫이 함께 표시된다.

### 3.2 LSP Rename / AST Replace

구조적 refactor preview는 preview 생성 시점의 파일 본문 hash를 함께 저장한다.

따라서 preview를 만든 뒤 대상 파일 중 하나라도 바뀌면 apply를 차단한다.

- 같은 파일의 다른 부분이 수정됨
- 여러 파일 중 한 파일만 먼저 저장됨
- preview 이후 외부 도구가 파일을 다시 생성함

## 4. 텔레그램 사용

웹 대시보드 없이도 텔레그램에서 같은 anchor 기반 흐름을 사용할 수 있다.

- 읽기: `/refactor read <path> [start] [end]`
- 미리보기: `/refactor preview <path> <start> <end>` 다음 줄부터 교체 코드를 붙여 넣기
- 적용: `/refactor apply [preview-id]`
- 상태 확인: `/refactor status`

slash 없이도 command-like 입력을 받는다.

```text
refactor read apps/omninode-middleware/src/CommandService.Telegram.cs 10 20
refactor preview apps/omninode-middleware/src/CommandService.Telegram.cs 12 14 ::: 새 코드
refactor apply
```

다만 replacement가 여러 줄이면 slash 없는 자연어 문장보다 위 형식처럼 command-like로 쓰는 편이 가장 안전하다.

현재 텔레그램은 anchor 기반 read/preview/apply 흐름을 우선 지원한다.

## 5. WebSocket 예시

### 5.1 anchor 읽기

```json
{ "type": "refactor_read", "path": "app.js" }
```

### 5.2 preview 만들기

```json
{
  "type": "refactor_preview",
  "mode": "anchor-edit",
  "path": "app.js",
  "edits": [
    {
      "startLine": 10,
      "endLine": 12,
      "expectedHashes": ["a1b2c3d4e5f6", "b1c2d3e4f5a6", "c1d2e3f4a5b6"],
      "replacement": "const value = 1;\nreturn value;"
    }
  ]
}
```

### 5.3 apply

```json
{ "type": "refactor_apply", "previewId": "preview_20260308123456_abcdef123456" }
```

### 5.4 LSP rename

```json
{
  "type": "lsp_rename",
  "path": "refactor-smoke/sample.h",
  "symbol": "addValue",
  "newName": "sumValue"
}
```

### 5.5 AST replace

```json
{
  "type": "ast_replace",
  "path": "refactor-smoke/sample.js",
  "pattern": "hello($A)",
  "replacement": "hi($A)"
}
```

## 6. 상태 파일

preview는 영구 설정 파일이 아니라 작업 산출물이다.

- 저장 위치: `workspace/.runtime/refactor-preview/<preview-id>.json`
- TTL: `OMNINODE_REFACTOR_PREVIEW_TTL_MINUTES`
- 목적: diff 확인과 apply 재검증

preview는 오래되면 자동 정리되고, apply 성공 후에도 재사용하지 않는 것이 원칙이다.

LSP/AST preview도 같은 위치를 사용한다.

중요:
- `OMNINODE_WORKSPACE_ROOT`를 쓰는 경우 preview 저장 경로도 그 작업공간 기준으로 같이 맞아야 한다.
- 구조적 refactor는 언어 서버나 ast-grep 결과에 따라 실제 변경 파일 수가 달라질 수 있다.

## 7. 운영 체크리스트

- refactor 전에 먼저 최신 파일을 읽었는지 확인
- Anchor Edit면 선택 범위 line 수와 expected hash 수가 같은지 확인
- LSP/AST면 symbol/pattern이 너무 넓지 않은지 확인
- preview diff가 의도한 범위만 바꾸는지 확인
- apply 직전 다른 편집기가 같은 파일을 저장하지 않았는지 확인
- mismatch가 나면 예전 preview를 버리고 `Anchor 읽기`부터 다시 시작

## 8. 검증에 쓴 대표 시나리오

대표 시나리오는 아래와 같다.

1. `alpha / beta / gamma` 3줄 파일 생성
2. line 2 anchor로 preview 생성
3. 파일을 외부 수정해서 stale mismatch 1건 확인
4. 다시 anchor를 읽고 새 preview 생성
5. apply 성공 후 line 2가 `beta-approved`로 바뀐 것 확인

추가 구조적 smoke:

1. `sample.h`에 선언된 심볼을 `lsp_rename`으로 rename preview 생성
2. `refactor_apply`로 실제 헤더 선언 변경 확인
3. `sample.js`에 `ast_replace` preview 생성
4. `refactor_apply`로 `hello(...) -> hi(...)` 치환 확인

자세한 검증 명령은 [검증_가이드.md](검증_가이드.md)를 본다.
