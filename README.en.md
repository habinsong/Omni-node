# Omni-node

<div align="center">

**A local-first AI workbench that connects chat, coding, runs, routines, refactoring, notebooks, Telegram, and provider routing into one practical workflow.**

[한국어](./README.md) · [English](./README.en.md)

[![local first](https://img.shields.io/badge/local--first-workflow-111111?style=for-the-badge)](./docs/en/architecture.md)
[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?style=for-the-badge)](./apps/omninode-middleware)
[![dashboard](https://img.shields.io/badge/dashboard-websocket-0A7EA4?style=for-the-badge)](./apps/omninode-dashboard)
[![telegram](https://img.shields.io/badge/telegram-natural%20control-26A5E4?style=for-the-badge)](./docs/en/usage.md)
[![safe refactor](https://img.shields.io/badge/Safe%20Refactor-preview%20→%20apply-2E8B57?style=for-the-badge)](./docs/en/safe-refactoring.md)

</div>

Updated: 2026-05-14

Omni-node is not just another chat UI. It is a local-first AI workbench that keeps conversations, coding runs, generated files, validation logs, routines, logic graphs, notebooks, Safe Refactor previews, and Telegram control in the same operational flow.

It does not assume that one expensive model or one expensive machine is the answer. You can use Groq, Gemini, Cerebras, NVIDIA NIM, Copilot, and Codex from the same dashboard, then keep the result as files, logs, previews, and recoverable run snapshots.

> Start here: [Quickstart](./docs/en/quickstart.md).

## Why it exists

A lot of AI tools look good until the first answer is over. Then the trail disappears.

- Generated files and commands are hard to find later.
- The web dashboard and the Telegram bot do not share the same capabilities.
- Coding output is hard to rerun, compare, or inspect.
- Refactoring often means overwriting a file without a real preview.
- Scheduled jobs, chat context, search, and handoff notes live in separate places.

Omni-node turns those loose pieces into a working loop.

| Common problem | Omni-node approach |
|---|---|
| Chat and execution are separate | Conversations, coding results, and logs stay connected |
| Provider comparison is messy | Single, orchestration, and multi-LLM modes work in chat and coding |
| Web and Telegram drift apart | Both use the same CommandService layer |
| Refactoring feels risky | Preview, stale validation, and guarded apply |
| Ops state is unclear | `/healthz`, `/readyz`, and `doctor --json` |

## Screenshots

### Full dashboard

![Omni-node dashboard](docs/assets/readme/dashboard-desktop-1920x1080.png)

### Core screens

| Chat | Coding |
|---|---|
| ![Chat tab](docs/assets/readme/dashboard-chat-tab.png) | ![Coding tab](docs/assets/readme/dashboard-coding-tab.png) |

| Safe Refactor | Routines |
|---|---|
| ![Safe Refactor](docs/assets/readme/dashboard-safe-refactor.png) | ![Routines tab](docs/assets/readme/dashboard-routines-tab.png) |

| Logic Graph | Notebooks |
|---|---|
| ![Logic tab](docs/assets/readme/dashboard-logic-tab.png) | ![Notebooks tab](docs/assets/readme/dashboard-notebooks-tab.png) |

| Plans | Skills |
|---|---|
| ![Plans tab](docs/assets/readme/dashboard-plans-tab.png) | ![Skills tab](docs/assets/readme/dashboard-skills-tab.png) |

| Settings | Mobile closed |
|---|---|
| ![Settings tab](docs/assets/readme/dashboard-settings-tab.png) | ![Mobile closed](docs/assets/readme/dashboard-mobile-closed-390x844.png) |

| Mobile composer open |
|---|
| ![Mobile composer open](docs/assets/readme/dashboard-mobile-composer-390x844.png.png) |

## What works today

- **Chat**: single model, orchestration, multi-LLM comparison, Think+, URL/search/memory context, TTS
- **Coding**: single completion loop, orchestration loop, multi-provider independent runs, preview and rerun support
- **Search**: Gemini grounding, query rewrite, evidence pack, guard, cache fallback
- **Routines**: natural-language routine creation, immediate/scheduled runs, browser agent, Telegram delivery
- **Logic Graph**: connect chat, coding, routines, and tools on a node canvas
- **Notebooks**: learnings, decisions, verification notes, and handoff documents
- **Safe Refactor**: anchor edits, LSP rename, ast-grep replacement with preview and guarded apply
- **Skills**: project/global `SKILL.md` files, sticky activation, shared chat and Telegram behavior
- **Remote dashboard**: LAN access toggle with sensitive settings blocked for remote clients

## Quick start

macOS/Linux global launcher:

```bash
Omni-node
Omni-node shutdown
```

Manual run:

```bash
make -C apps/omninode-core
dotnet run --project apps/omninode-middleware/OmniNode.Middleware.csproj
```

Windows:

```powershell
.\apps\omninode-core\build.ps1
dotnet run --project apps\omninode-middleware\OmniNode.Middleware.csproj
```

Open:

- Dashboard: [http://127.0.0.1:8080/](http://127.0.0.1:8080/)
- Health: [http://127.0.0.1:8080/healthz](http://127.0.0.1:8080/healthz)
- Ready: [http://127.0.0.1:8080/readyz](http://127.0.0.1:8080/readyz)

## Providers

| provider key | Label | Integration |
|---|---|---|
| `gemini` | Gemini | Google API and grounded search |
| `groq` | Groq | OpenAI-compatible HTTP |
| `cerebras` | Cerebras | HTTP API |
| `nvidia` | NVIDIA NIM | OpenAI-compatible `https://integrate.api.nvidia.com/v1` |
| `copilot` | Copilot | `gh`/`copilot` CLI |
| `codex` | Codex | `codex` CLI or API key |

## Documentation

| Korean | English |
|---|---|
| [5분 시작](./docs/QUICKSTART.md) | [English](./docs/en/quickstart.md) |
| [사용법](./docs/사용법_빠른시작.md) | [English](./docs/en/usage.md) |
| [아키텍처](./docs/아키텍처_흐름.md) | [English](./docs/en/architecture.md) |
| [기술 스택](./docs/기술스택_정리.md) | [English](./docs/en/tech-stack.md) |
| [환경변수와 상태 파일](./docs/환경변수_및_상태파일.md) | [English](./docs/en/environment-and-state.md) |
| [검증](./docs/검증_가이드.md) | [English](./docs/en/validation.md) |
| [디렉터리](./docs/디렉터리_가이드.md) | [English](./docs/en/directory-guide.md) |
| [AGENTS와 스킬](./docs/AGENTS_AND_SKILLS.md) | [English](./docs/en/agents-and-skills.md) |
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
