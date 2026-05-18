// Maps raw error codes / technical messages to user-friendly Korean text.
// Use mapErrorMessage(raw) before showing errors to end users.

const FRIENDLY_MAP = {
  invalid_schema: "서버 응답 형식이 예상과 달라 표시할 수 없습니다.",
  fetch_failed: "서버에 연결하지 못했습니다. 네트워크나 미들웨어 상태를 확인하세요.",
  network_error: "네트워크 연결이 끊어졌습니다.",
  timeout: "응답 시간이 초과됐습니다. 잠시 후 다시 시도하세요.",
  rate_limited: "요청이 너무 많아 잠시 대기가 필요합니다.",
  forbidden: "권한이 없어 요청을 수행할 수 없습니다.",
  unauthorized: "인증이 필요합니다. 다시 로그인하세요.",
  not_found: "요청한 리소스를 찾을 수 없습니다.",
  unsupported: "지원하지 않는 요청입니다.",
  internal_error: "서버 내부 오류가 발생했습니다. 잠시 후 다시 시도하세요.",
  bad_request: "요청 형식이 올바르지 않습니다.",
  conflict: "다른 작업과 충돌했습니다. 새로고침 후 다시 시도하세요.",
  service_unavailable: "서비스를 일시적으로 사용할 수 없습니다.",
  gemini_api_key_missing: "Gemini API 키가 설정되어 있지 않습니다. 설정 탭에서 등록해주세요.",
  groq_api_key_missing: "Groq API 키가 설정되어 있지 않습니다. 설정 탭에서 등록해주세요.",
  cerebras_api_key_missing: "Cerebras API 키가 설정되어 있지 않습니다. 설정 탭에서 등록해주세요.",
  nvidia_api_key_missing: "NVIDIA NIM API 키가 설정되어 있지 않습니다. 설정 탭에서 등록해주세요.",
  copilot_login_required: "Copilot 로그인이 필요합니다. 설정 탭에서 로그인해주세요.",
  codex_login_required: "Codex 로그인이 필요합니다. 설정 탭에서 로그인해주세요.",
  forbidden_remote_auth: "외부 접속 제한 모드에서는 인증 관련 작업을 사용할 수 없습니다.",
  forbidden_remote_secret_settings: "외부 접속 제한 모드에서는 API 키와 Telegram 같은 민감 설정을 바꿀 수 없습니다.",
  forbidden_remote_external_access: "외부 접속 제한 모드에서는 외부접속 설정을 바꿀 수 없습니다.",
  forbidden_remote_limited_action: "외부 접속 제한 모드에서는 이 작업을 사용할 수 없습니다. 로컬 대시보드에서 실행하세요.",
  ws_disconnected: "WebSocket 연결이 끊어졌습니다. 잠시 후 자동으로 재연결됩니다.",
};

const STATUS_MAP = {
  400: "요청 형식이 올바르지 않습니다.",
  401: "인증이 필요합니다.",
  403: "권한이 없습니다.",
  404: "요청한 리소스를 찾을 수 없습니다.",
  408: "응답 시간이 초과됐습니다.",
  409: "다른 작업과 충돌했습니다.",
  413: "요청 본문이 너무 큽니다.",
  429: "요청이 너무 많아 잠시 대기가 필요합니다.",
  500: "서버 내부 오류가 발생했습니다.",
  502: "게이트웨이 오류로 응답을 받지 못했습니다.",
  503: "서비스를 일시적으로 사용할 수 없습니다.",
  504: "응답 시간이 초과됐습니다 (게이트웨이).",
};

export function mapErrorMessage(raw) {
  if (raw == null) return "";
  const text = typeof raw === "string" ? raw : raw?.message ?? String(raw);
  if (!text) return "";

  const trimmed = text.trim();
  const lower = trimmed.toLowerCase();

  if (FRIENDLY_MAP[lower]) {
    return FRIENDLY_MAP[lower];
  }

  // http_500, http_404 등
  const httpMatch = lower.match(/^http[_:\s-]?(\d{3})$/);
  if (httpMatch) {
    const code = parseInt(httpMatch[1], 10);
    return STATUS_MAP[code] || `서버 응답 오류 (HTTP ${code})입니다.`;
  }

  // status: 500 / status=500
  const statusMatch = lower.match(/status[:= ]\s*(\d{3})/);
  if (statusMatch) {
    const code = parseInt(statusMatch[1], 10);
    if (STATUS_MAP[code]) {
      return STATUS_MAP[code];
    }
  }

  // 단순 enum 형태(snake_case) → "x x x x"로 풀어주는 fallback
  if (/^[a-z][a-z0-9_]*$/.test(lower) && lower.includes("_")) {
    const readable = lower.replace(/_/g, " ");
    return `오류: ${readable}`;
  }

  return trimmed;
}

export function formatErrorBanner(raw, prefix = "오류") {
  const friendly = mapErrorMessage(raw);
  if (!friendly) return "";
  if (friendly.startsWith(prefix) || friendly.startsWith("오류")) {
    return friendly;
  }
  return `${prefix}: ${friendly}`;
}
