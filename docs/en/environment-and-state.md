# Environment and State Files

[한국어](../환경변수_및_상태파일.md) · [English](./environment-and-state.md)

Updated: 2026-05-15

Secrets should use `*_FILE` or a secure store where possible. Runtime state lives under `~/.omninode`; generated work lives under `workspace/`.

Common variables: `OMNINODE_GEMINI_API_KEY_FILE`, `OMNINODE_GROQ_API_KEY_FILE`, `OMNINODE_CEREBRAS_API_KEY_FILE`, `OMNINODE_NVIDIA_API_KEY_FILE`, `OMNINODE_CODEX_API_KEY_FILE`, `OMNINODE_WORKSPACE_ROOT`.
