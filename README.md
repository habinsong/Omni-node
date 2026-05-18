# Omni-node

<div align="center">

**AI가 답만 하고 사라지지 않게. 대화, 코딩, 실행, 검증, 루틴, 텔레그램까지 한 흐름으로 묶은 로컬 우선 작업대.**

[한국어](./README.md) · [English](./README.en.md)

[![local first](https://img.shields.io/badge/local--first-workflow-111111?style=for-the-badge)](./docs/아키텍처_흐름.md)
[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?style=for-the-badge)](./apps/omninode-middleware)
[![dashboard](https://img.shields.io/badge/dashboard-websocket-0A7EA4?style=for-the-badge)](./apps/omninode-dashboard)
[![telegram](https://img.shields.io/badge/telegram-natural%20control-26A5E4?style=for-the-badge)](./docs/사용법_빠른시작.md)
[![safe refactor](https://img.shields.io/badge/Safe%20Refactor-preview%20→%20apply-2E8B57?style=for-the-badge)](./docs/SAFE_REFACTORING.md)

</div>

업데이트 기준: 2026-05-15

Omni-node는 챗봇 하나 더 만들자는 프로젝트가 아니다. LLM에게 물어보고, 코드 만들고, 실행해 보고, 실패하면 고치고, 그 기록을 다시 꺼내보는 과정을 한 화면에 묶으려고 만든 도구다.

요즘 AI 도구 이야기는 종종 이상한 방향으로 흐른다. 비싼 장비를 사야 한다거나, 최고가 모델 하나만 써야 한다거나, 토큰 비용을 크게 태우는 게 실력처럼 보이는 식이다. Omni-node는 반대로 간다. 지금 가진 환경에서 바로 켜고, 여러 제공자를 바꿔 쓰고, 결과를 파일과 로그로 남기는 쪽을 우선한다.

Groq, Gemini, Cerebras, NVIDIA NIM 같은 HTTP 제공자와 Copilot, Codex CLI/API 흐름을 같은 대시보드에서 다룬다. 웹에서 하던 일은 텔레그램으로 이어갈 수 있고, 코딩 결과는 실행 폴더와 최근 결과 카드로 다시 확인할 수 있다.

> 처음이면 [5분 시작 가이드](./docs/QUICKSTART.md)를 먼저 보면 된다.

<table>
  <tr>
    <td bgcolor="#EAF4FF">
      <strong><font color="#315B7C">한 줄로 말하면</font></strong><br><br>
      <strong><font color="#1F3A5F">Omni-node는 말만 하는 AI가 아니라, 실제 작업을 만들고 돌려 보고 검증한 흔적까지 남기는 로컬 우선 AI 워크벤치다.</font></strong><br><br>
      <strong><font color="#1F3A5F">비싼 모델 하나에 묶이지 않고, 웹 대시보드와 텔레그램에서 같은 흐름으로 계속 작업할 수 있게 만든다.</font></strong>
    </td>
  </tr>
</table>

## 왜 만들었나

첫 답변은 누구나 그럴듯하게 만든다. 문제는 작업이 길어질 때다.

- 모델이 만든 파일이 어디 있는지 나중에 못 찾는다.
- 어떤 명령을 실행했고 stdout/stderr가 어땠는지 흐려진다.
- 웹에서는 되던 기능이 텔레그램에서는 안 된다.
- 코딩 결과를 모델별로 비교하기 어렵다.
- 리팩터링이 preview 없이 파일을 바로 덮어쓴다.
- 루틴, 검색, 노트북, 작업 계획이 서로 다른 도구처럼 흩어진다.

Omni-node는 이 문제를 기능 목록이 아니라 작업 흐름으로 풀려고 한다.

| 흔한 문제 | Omni-node의 방식 |
|---|---|
| 채팅과 실행 결과가 분리됨 | 대화, 코딩 결과, 실행 로그를 같은 흐름에 저장 |
| 모델 비교가 귀찮음 | 단일, 오케스트레이션, 다중 LLM 모드를 대화와 코딩 모두에서 지원 |
| 웹과 텔레그램 기능이 다름 | 같은 CommandService 계층을 공유 |
| 리팩터링이 불안함 | preview 생성, stale 검증, apply 차단 |
| 운영 상태를 보기 어려움 | `/healthz`, `/readyz`, `doctor --json` 제공 |

## 지금 되는 것

- **대화**: 단일 모델, 오케스트레이션, 다중 LLM, Think+, URL/검색/메모리 문맥, TTS
- **코딩**: 단일 완주, 오케스트레이션 완주, 다중 LLM 독립 완주 비교, 실행/프리뷰/최근 결과 복원
- **검색**: Gemini grounding 기반 검색, query rewrite, evidence pack, guard, cache fallback
- **루틴**: 자연어 루틴 생성, 즉시 실행, 예약 실행, 브라우저 에이전트, 텔레그램 전송
- **로직 그래프**: 대화/코딩/루틴/도구 노드를 캔버스에서 연결하고 실행
- **노트북**: 작업 메모, 방향 정리, 검증 기록, 이어보기 문서 관리
- **작업 계획**: 계획 생성, 리뷰, 승인, task graph 실행
- **Safe Refactor**: 줄 범위 교체, LSP rename, ast-grep replace를 preview 중심으로 적용
- **스킬**: `.omni/skills/**/SKILL.md`를 대화탭과 텔레그램에서 공통 사용, sticky 활성화/중지·재시작 후 자동 복원·다중 스킬 거부·단어 경계 매칭·Think+ 동시 사용 시 톤 양보·`/skill quick` 별명·`/skill status`
- **외부접속**: 설정 탭에서 LAN 접속을 켜고, 외부 클라이언트는 OTP 없이 제한 모드로 사용. 읽기 중심 조회와 모델/라우팅 설정만 허용하고 대화/코딩/루틴/로직 그래프 실행, 인증/시크릿/외부접속 설정은 차단
- **제공자**: Gemini, Groq, Cerebras, NVIDIA NIM, Copilot CLI, Codex CLI/API
- **텔레그램 봇**: 슬래시 명령(`/skill /think /web /history /coding download /off`), inline keyboard 빠른 작업, 음성 메시지 자동 STT + 들은 내용 echo, 다중 user allowlist (CSV), 본문 하단 footer (provider·model · 활성 스킬 · ⏱ 소요시간), 긴 응답 자동 .txt 첨부

## 최근 변경

자세한 사용법은 [텔레그램 봇 가이드](./docs/텔레그램_봇_가이드.md)와 [스킬 사용 가이드](./docs/AGENTS_AND_SKILLS.md)를 참고.

- **응답 품질**: provider별 single-chat 타임아웃 분기(NIM 180s/Cerebras 40s/그 외 34s) — `OperationCanceledException`으로 응답이 몇 글자만 나오고 끊기는 문제 해결. 부분 응답에 `⚠️ 끊겼습니다` suffix 자동 부착. 코드펜스 한가운데 잘림 방지.
- **컨텍스트 추적**: "그니까 잘 돌아가?" 같은 짧은 후속 질문에서 history가 누락되던 버그 해결. 의견·판단 요청에 모델이 "정확도/정밀도 같은 객관 지표 필요" 회피 답변 못 하도록 시스템 프롬프트 강화.
- **NVIDIA quota 안내**: 429/quota/credits 본문을 식별해 "무료 할당량 도달, 다른 provider로 바꿔 보세요" 안내로 변환.
- **Think+ 캐시**: 동일 입력 60초 TTL 캐시로 중복 Gemini 호출 절감.
- **보안 경계 보강**: 외부접속은 OTP 요청을 차단하고 제한 모드로 자동 승인. 제한 모드 권한표, 세분화된 차단 메시지, WebSocket Origin 검사, 인증 전 메시지 allowlist, `/api/local-image` 루틴 자산 경로 제한, 첨부 파일 개수/크기 reject 정책, Markdown raw HTML 차단 추가.

## 스크린샷

### 전체 대시보드

![Omni-node 데스크톱 대시보드](docs/assets/readme/dashboard-desktop-1920x1080.png)

### 핵심 화면

| 대화 | 코딩 |
|---|---|
| ![대화 탭](docs/assets/readme/dashboard-chat-tab.png) | ![코딩 탭](docs/assets/readme/dashboard-coding-tab.png) |

| Safe Refactor | 루틴 |
|---|---|
| ![Safe Refactor](docs/assets/readme/dashboard-safe-refactor.png) | ![루틴 탭](docs/assets/readme/dashboard-routines-tab.png) |

| 로직 그래프 | 노트북 |
|---|---|
| ![로직 탭](docs/assets/readme/dashboard-logic-tab.png) | ![노트북 탭](docs/assets/readme/dashboard-notebooks-tab.png) |

| 작업 계획 | 스킬 |
|---|---|
| ![작업 계획 탭](docs/assets/readme/dashboard-plans-tab.png) | ![스킬 탭](docs/assets/readme/dashboard-skills-tab.png) |

| 설정 | 모바일 composer 닫힘 |
|---|---|
| ![설정 탭](docs/assets/readme/dashboard-settings-tab.png) | ![모바일 composer 닫힘](docs/assets/readme/dashboard-mobile-closed-390x844.png) |

| 모바일 composer 열림 |
|---|
| ![모바일 composer 열림](docs/assets/readme/dashboard-mobile-composer-390x844.png.png) |

## 기능을 조금 더 자세히 보면

### 대화

대화 탭은 일반 챗봇처럼 보이지만 내부 흐름은 더 넓다. 단일 모델로 빠르게 답을 받거나, 오케스트레이션으로 역할을 나눠 검토하고, 다중 LLM으로 여러 모델의 답을 비교할 수 있다. 짧은 인사나 단순 수식에는 검색/메모리를 억지로 붙이지 않고, 최신 정보가 필요한 질문만 검색 경로로 보낸다.

### 코딩

코딩 탭은 모델 답변을 코드 블록으로 끝내지 않는다. 실행별 폴더를 만들고, 생성 파일, 실행 명령, stdout/stderr, 검증 상태를 남긴다. 최근 코딩 결과는 대화별로 복원되므로 새로고침 뒤에도 다시 열 수 있다.

### 루틴과 로직

루틴은 자연어로 만들고 즉시 실행하거나 예약할 수 있다. 로직 탭은 ComfyUI처럼 노드를 연결해 대화, 코딩, 루틴, 도구 호출을 하나의 그래프로 엮는다.

### Safe Refactor

리팩터링은 바로 적용하지 않는다. 먼저 preview를 만들고 diff를 확인한 뒤 apply한다. preview 이후 파일이 바뀌었으면 적용을 막는다.

### 텔레그램

텔레그램 봇은 별도 장난감 인터페이스가 아니다. 대화, 코딩, 루틴, Safe Refactor, doctor, plan/task, notebook/handoff, 모델 제어가 같은 명령 계층으로 들어간다.

## 5분 시작

필수 도구는 `.NET SDK 9`, C 컴파일러, `python3`, `node/npm`이다. LLM 키는 하나 이상만 있어도 시작할 수 있다.

macOS/Linux에서 전역 실행기가 등록되어 있으면:

```bash
Omni-node setup
Omni-node
Omni-node shutdown
```

저장소에서 바로 준비하려면 `./scripts/Omni-node setup`을 먼저 실행한다. 이 명령은 의존성 확인/설치, 코어 빌드, 미들웨어 빌드, `npm test`, 실행기 등록을 한 번에 처리한다. 첫 `Omni-node` 실행 시 setup marker가 없으면 자동 setup도 시도한다.

수동 실행:

```bash
make -C apps/omninode-core
dotnet run --project apps/omninode-middleware/OmniNode.Middleware.csproj
```

Windows:

```powershell
.\scripts\Omni-node.ps1 setup
.\apps\omninode-core\build.ps1
dotnet run --project apps\omninode-middleware\OmniNode.Middleware.csproj
```

접속 주소:

- 대시보드: [http://127.0.0.1:8080/](http://127.0.0.1:8080/)
- health: [http://127.0.0.1:8080/healthz](http://127.0.0.1:8080/healthz)
- ready: [http://127.0.0.1:8080/readyz](http://127.0.0.1:8080/readyz)

## 구조

```text
apps/
  omninode-core/        C11 코어 데몬
  omninode-middleware/  .NET 9 서버, WebSocket/HTTP, 텔레그램, 라우팅
  omninode-dashboard/   정적 웹 대시보드
  omninode-sandbox/     Python 샌드박스 실행기
docs/                   한국어 문서와 docs/en 영어 문서
workspace/              코딩/루틴/로직/task 실행 산출물
```

영속 상태는 기본적으로 `~/.omninode` 아래에 남고, 작업 산출물은 `workspace/` 아래에 남는다.

## 제공자

| provider key | 표시명 | 방식 |
|---|---|---|
| `gemini` | Gemini | Google API, grounding 검색 |
| `groq` | Groq | OpenAI 호환 HTTP |
| `cerebras` | Cerebras | HTTP API |
| `nvidia` | NVIDIA NIM | OpenAI 호환 `https://integrate.api.nvidia.com/v1` |
| `copilot` | Copilot | `gh`/`copilot` CLI |
| `codex` | Codex | `codex` CLI 또는 API 키 |

`nvidia-nim`, `nvidia_nim`, `nim`은 모두 `nvidia`로 정규화된다.

## 검증

```bash
make -C apps/omninode-core
python3 apps/omninode-sandbox/executor.py --code "print('ok')"
dotnet build apps/omninode-middleware/OmniNode.Middleware.csproj
npm test
```

운영 점검:

```bash
curl -s http://127.0.0.1:8080/healthz
curl -s http://127.0.0.1:8080/readyz
dotnet run --project apps/omninode-middleware/OmniNode.Middleware.csproj -- doctor --json
```

## 문서

| 한국어 | English |
|---|---|
| [5분 시작](./docs/QUICKSTART.md) | [English](./docs/en/quickstart.md) |
| [사용법](./docs/사용법_빠른시작.md) | [English](./docs/en/usage.md) |
| [아키텍처](./docs/아키텍처_흐름.md) | [English](./docs/en/architecture.md) |
| [기술 스택](./docs/기술스택_정리.md) | [English](./docs/en/tech-stack.md) |
| [환경변수와 상태 파일](./docs/환경변수_및_상태파일.md) | [English](./docs/en/environment-and-state.md) |
| [검증](./docs/검증_가이드.md) | [English](./docs/en/validation.md) |
| [디렉터리](./docs/디렉터리_가이드.md) | [English](./docs/en/directory-guide.md) |
| [AGENTS와 스킬](./docs/AGENTS_AND_SKILLS.md) | [English](./docs/en/agents-and-skills.md) |
| [텔레그램 봇 가이드](./docs/텔레그램_봇_가이드.md) | — |
| [NVIDIA NIM](./docs/nvidia-nim-provider.md) | [English](./docs/en/nvidia-nim-provider.md) |
| [Safe Refactor](./docs/SAFE_REFACTORING.md) | [English](./docs/en/safe-refactoring.md) |
| [Doctor](./docs/DOCTOR.md) | [English](./docs/en/doctor.md) |
| [도구 통합 패널](./docs/도구_통합_패널_사용_가이드.md) | [English](./docs/en/tool-integration-panel.md) |
| [노트북과 이어보기](./docs/NOTEBOOKS_AND_HANDOFF.md) | [English](./docs/en/notebooks-and-handoff.md) |
| [계획과 Task Graph](./docs/PLANNING_AND_TASKS.md) | [English](./docs/en/planning-and-tasks.md) |
| [정리 기준](./docs/CLEANUP.md) | [English](./docs/en/cleanup.md) |
| [토큰과 메모리 초기화](./docs/토큰_메모리_초기화_가이드.md) | [English](./docs/en/token-memory-reset.md) |
| [수동 회귀 체크리스트](./docs/OMNINODE_실환경_수동_최종회귀_체크리스트.md) | [English](./docs/en/manual-regression-checklist.md) |
| [Gemini 검색 전환 기록](./docs/GEMINI_SEARCH_RETRIEVER_INTEGRATION_PLAN.md) | [English](./docs/en/gemini-search-retriever-integration-plan.md) |

## 이런 사람에게 맞다

- 여러 LLM 제공자를 한 화면에서 바꿔 쓰고 싶은 사람
- 코딩 결과를 파일, 명령, 로그까지 남기고 싶은 사람
- 웹 대시보드와 텔레그램 봇을 같은 작업 흐름으로 쓰고 싶은 사람
- 리팩터링을 바로 적용하기 전에 preview와 stale 검증을 거치고 싶은 사람
- 자동화, 루틴, 검색, 노트북 기록을 따로 관리하기 싫은 사람
