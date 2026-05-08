# Omni-node Quickstart

[한국어](../QUICKSTART.md) · [English](./quickstart.md)

Updated: 2026-05-08

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

Open `http://127.0.0.1:8080/`. Health endpoints are `/healthz` and `/readyz`.
