# Omni-node Quickstart

[한국어](../QUICKSTART.md) · [English](./quickstart.md)

Updated: 2026-05-21

This is the shortest path from clone to dashboard.

![Dashboard](../assets/readme/dashboard-desktop-1920x1080.png)

## Requirements

| Tool | Purpose |
|---|---|
| `.NET SDK 9` | Middleware build and run |
| C compiler | Core daemon build |
| `python3` | Sandbox and coding validation |
| `node`, `npm` | Dashboard checks and regression scripts |
| Optional: `gh`, `copilot`, `codex` | Copilot/Codex CLI integration |

## Run

macOS/Linux launcher:

```bash
Omni-node setup
Omni-node
Omni-node shutdown
```

From a fresh checkout, run `./scripts/Omni-node setup` first. Setup checks or installs required tools, builds `apps/omninode-core`, builds the middleware, runs `npm test`, and registers the launcher. If the setup marker is missing, the first `Omni-node` start also attempts automatic setup.

Manual run:

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

Open `http://127.0.0.1:8080/`. Health endpoints are `/healthz` and `/readyz`.

The first WebSocket session starts in an OTP-pending state. If Telegram is configured, the OTP is sent there; local development can use the console fallback OTP when enabled.

Remote dashboard access is off by default. When enabled from Settings, LAN clients enter limited mode without an OTP prompt. Limited mode allows read-oriented views, routing policy, and model selection; chat, coding, routine, logic graph, task, refactor, and tool execution actions are blocked along with OTP/CLI auth, Telegram/LLM keys, and external-access toggle changes.
