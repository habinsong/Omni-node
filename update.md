# Omni-node v1.0.4 업데이트 내역

작성일: 2026-05-14
문서 최신화: 2026-05-15

이번 업데이트는 v1.0.3 안정화 범위에 외부접속 보안 경계와 릴리스 위생을 더한 패치다. 대화탭, 코딩탭, 텔레그램, 스킬탭에서 스킬이 서로 다른 경로로 적용되던 부분을 하나의 기준으로 맞추고, 활성 스킬이 URL/웹검색 빠른 응답 경로에서 우회되지 않도록 보강했다. 루틴탭은 개요 카드와 새로고침/동기화 버튼이 한 줄에 정렬되도록 UI를 정리했다.

## 핵심 요약

- 대화/코딩 입력창에서 선택한 스킬이 서버의 sticky skill 상태에도 반영되도록 했다.
- 프롬프트에 스킬이 명시된 경우 UI 선택보다 프롬프트 명시를 우선하도록 유지하면서, 단일 유효 스킬만 공통 입력 준비 단계에서 적용되도록 정리했다.
- 활성 스킬이 있는 상태에서 URL이나 웹검색 질문을 보내도 빠른 응답 경로가 스킬 컨텍스트를 우회하지 않도록 막았다.
- 텔레그램 일반 대화와 `/coding run` 모두 활성 스킬을 같은 방식으로 전달하도록 보강했다.
- 스킬 배지의 끄기 버튼을 누르면 UI 선택뿐 아니라 서버에 저장된 해당 대화의 sticky skill도 함께 해제된다.
- `/skill create`와 스킬탭의 새 스킬 저장이 기존 스킬을 조용히 덮어쓰지 않도록 안전장치를 추가했다.
- 같은 이름의 project/global 스킬이 함께 있을 때 project 스킬을 우선한다.
- 루틴탭의 개요 영역에서 새로고침/동기화 버튼이 두 번째 줄로 떨어지지 않고 오른쪽 끝에 한 줄로 보이도록 레이아웃을 조정했다.
- 관련 문서와 계약 테스트를 갱신했다.
- 외부접속 자동 인증 제거, 인증 전 WebSocket 메시지 차단, 루틴 이미지 프리뷰 경로 제한, 첨부 파일 reject 정책, Markdown raw HTML 차단을 추가했다.

## 스킬 로직 개선

### 공통 입력 준비 단계 정리

- UI에서 선택한 스킬 이름과 범위를 공통 입력 준비 단계로 전달하도록 했다.
- 프롬프트 안에 스킬 이름이 직접 명시되어 있으면 그 스킬을 우선 적용한다.
- 프롬프트에 명시된 스킬이 없을 때만 UI 선택 스킬을 활성 스킬로 반영한다.
- 이미 다른 sticky skill이 있는 대화에서 UI 선택 스킬을 바꾸면 이전 활성 스킬 정보를 보존한 뒤 새 스킬로 갱신한다.
- `PrepareSharedInputAsync`와 `PrepareInputWithAttachmentsAsync`가 `requestedSkillName`, `requestedSkillScope`를 받을 수 있도록 확장했다.

### 스킬 탐색과 우선순위

- 스킬 manifest 탐색을 공통 helper로 분리했다.
- 같은 이름의 스킬이 project/global 범위에 동시에 있으면 project 범위를 먼저 선택한다.
- 스킬 이름이나 범위가 비어 있으면 불필요한 탐색 없이 안전하게 넘어가도록 했다.

### 빠른 웹 응답 경로 보정

- 대화탭에서 활성 스킬이 있는 경우 URL fast path와 웹검색 fast path를 우회한다.
- Think+가 켜진 경우와 마찬가지로, 스킬이 켜진 요청은 공통 입력 준비 단계에서 웹 문맥과 스킬 지침을 함께 붙인 뒤 모델이 답하도록 했다.
- 이로 인해 웹 검색 결과는 사실 근거로 사용하되, 최종 응답 형식과 말투는 활성 스킬 지침을 따르게 된다.

## 텔레그램 개선

- 텔레그램 일반 대화에서 inline 스킬 요청을 공통 입력 준비 단계로 전달한다.
- 텔레그램에 sticky skill이 활성화되어 있으면 URL/웹검색 빠른 응답 경로가 스킬 지침을 우회하지 않는다.
- `/coding run` 실행 시 텔레그램 대화의 활성 스킬을 코딩 요청의 `SkillName`으로 전달한다.
- 단일, 오케스트레이션, 다중 LLM 코딩 모드 모두 텔레그램 활성 스킬을 반영한다.
- `/skill create`는 기존 스킬을 자동으로 덮어쓰지 않고 실패 메시지를 반환한다.

## 스킬탭과 대화/코딩 UI 개선

- 대화/코딩 입력창의 스킬 배지를 해제하면 서버에 저장된 해당 대화의 active skill도 같이 지운다.
- 이를 위해 WebSocket 메시지 `skill_active_clear`와 응답 `skill_active_clear_result`를 추가했다.
- 새 스킬 생성 저장은 기본적으로 `allowOverwrite=false`로 보내 기존 스킬 덮어쓰기를 막는다.
- 기존 스킬을 열어 수정하는 경우에만 `allowOverwrite=true`로 저장한다.

## 루틴탭 UI 개선

- 루틴탭 개요 그리드를 8개 항목 기준으로 조정했다.
- `상세 패널`, `전체 루틴`, `활성 루틴`, `예약 대기`, `브라우저 에이전트`, `최근 오류`, `스케줄러`, `새로고침/동기화`가 한 줄에 정렬되도록 했다.
- 화면 폭이 줄어드는 구간에서도 새로고침/동기화 카드가 두 번째 줄로 밀리지 않도록 column 최소 폭을 다시 잡았다.

## 문서와 계약 테스트

- `docs/AGENTS_AND_SKILLS.md`에 sticky skill 해제, 빠른 웹 응답 우회 방지, project 우선순위, 덮어쓰기 방지 규칙을 추가했다.
- `docs/텔레그램_봇_가이드.md`에 텔레그램 스킬과 웹검색 경로의 동작 기준을 보강했다.
- `scripts/check-chat-telegram-contract.mjs`에서 텔레그램 스킬/Think+와 웹검색 계약을 최신 동작에 맞게 갱신했다.

## 변경된 주요 영역

- 대시보드 UI: `apps/omninode-dashboard/app.js`, `apps/omninode-dashboard/styles.css`
- 대시보드 WebSocket context: `apps/omninode-dashboard/modules/ws-context.js`
- 미들웨어 입력 준비/대화 처리: `apps/omninode-middleware/src/CommandService.InputPreparation.cs`, `apps/omninode-middleware/src/CommandService.Chat.cs`
- 텔레그램 처리: `apps/omninode-middleware/src/CommandService.Telegram.cs`, `apps/omninode-middleware/src/CommandService.Telegram.Coding.cs`, `apps/omninode-middleware/src/CommandService.Telegram.Skills.cs`
- 스킬 저장/해제: `apps/omninode-middleware/src/SkillFileService.cs`, `apps/omninode-middleware/src/WsContextCommandDispatcher.cs`, `apps/omninode-middleware/src/WebSocketGateway*.cs`
- 문서와 테스트: `docs/AGENTS_AND_SKILLS.md`, `docs/텔레그램_봇_가이드.md`, `scripts/check-chat-telegram-contract.mjs`

## 검증한 명령

```bash
dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj
npm test
```

두 검증 모두 통과했다.

## 비전공자용 설명

v1.0.4는 “스킬을 켰는데 어떤 화면에서는 먹고, 어떤 경로에서는 빠지는” 문제를 줄이고, 외부접속과 첨부·마크다운 처리의 안전 경계를 더 분명하게 만든 안정화 업데이트다.

예를 들어 대화창에서 특정 말투나 작업 규칙을 가진 스킬을 켜고 URL을 같이 보내면, 예전에는 빠른 웹 응답 경로가 먼저 동작하면서 스킬 지침을 건너뛸 수 있었다. 이제는 활성 스킬이 있으면 웹 자료를 참고하더라도 스킬의 말투와 출력 규칙을 유지한다.

텔레그램도 같은 기준으로 맞췄다. 텔레그램에서 스킬을 켠 뒤 일반 질문을 하거나 `/coding run`으로 코딩 작업을 시켜도 활성 스킬이 전달된다.

스킬 저장도 더 안전해졌다. 새 스킬을 만들 때 같은 이름이 이미 있으면 자동으로 덮어쓰지 않는다. 기존 스킬을 수정하려면 스킬탭에서 해당 스킬을 열어 저장해야 한다.

루틴탭은 화면 상단의 상태 카드들이 한 줄로 정리되었다. 새로고침/동기화 버튼이 따로 아래 줄로 내려가지 않고 오른쪽 끝에 붙어서 보인다.

정리하면 v1.0.4는 큰 기능을 새로 늘리기보다, v1.0.3에서 정리한 스킬·텔레그램·루틴 흐름을 실제 사용 중 덜 흔들리게 만들고 외부접속 보안 경계를 보강한 업데이트다.
