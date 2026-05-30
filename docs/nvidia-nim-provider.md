# NVIDIA NIM 제공자

[한국어](./nvidia-nim-provider.md) · [English](./en/nvidia-nim-provider.md)

업데이트 기준: 2026-05-21

Omni-node의 NVIDIA NIM provider key는 `nvidia`다. UI 표시명은 `NVIDIA NIM`이고, `nvidia-nim`, `nvidia_nim`, `nim` alias도 `nvidia`로 정규화한다.

## 기본값

| 항목 | 값 |
|---|---|
| Base URL | `https://integrate.api.nvidia.com/v1` |
| 기본 모델 | `meta/llama-3.1-70b-instruct` |
| Timeout | `20`초 |
| Max tokens clamp | `4096` |

## 환경변수

- `OMNINODE_NVIDIA_API_KEY`
- `OMNINODE_NVIDIA_API_KEY_FILE`
- `OMNINODE_NVIDIA_KEYCHAIN_SERVICE`
- `OMNINODE_NVIDIA_KEYCHAIN_ACCOUNT`
- `OMNINODE_NVIDIA_BASE_URL`
- `OMNINODE_NVIDIA_MODEL`
- `OMNINODE_NVIDIA_TIMEOUT_SEC`

## 동작

NIM LLM API는 OpenAI 호환 chat completions 흐름으로 붙는다. streaming SSE는 기존 OpenAI 호환 parser를 재사용하고, 202 응답이 오면 `requestId`를 받아 status polling으로 최종 응답을 가져온다.

v1 범위는 텍스트 LLM이다. 이미지, 비전, 멀티모달 NIM endpoint와 동적 catalog sync는 안정 계약을 확인하기 전까지 제외한다.
