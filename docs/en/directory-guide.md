# Directory Guide

[한국어](../디렉터리_가이드.md) · [English](./directory-guide.md)

Updated: 2026-05-19

Canonical paths are `apps/`, `docs/`, and `workspace/`.

| Path | Purpose |
|---|---|
| `apps/omninode-core` | C core daemon |
| `apps/omninode-middleware` | .NET server and command layer |
| `apps/omninode-dashboard` | Static dashboard |
| `apps/omninode-sandbox` | Python executor |
| `docs/assets/readme` | README screenshots |
| `scripts` | `Omni-node setup/start/shutdown`, Windows `Omni-node.ps1`, and test scripts |
| `workspace` | Generated work artifacts |

## Launchers

- `./scripts/Omni-node setup`: checks or installs dependencies, builds, validates, and registers the launcher on macOS/Linux.
- `./scripts/Omni-node`: starts the server; if the setup marker is missing, it attempts automatic setup first.
- `./scripts/Omni-node shutdown`: stops running Omni-node processes.
- `.\scripts\Omni-node.ps1 setup`: Windows setup, build, and validation path.
