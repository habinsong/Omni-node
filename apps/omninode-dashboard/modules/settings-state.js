export function createAuthMetaState() {
  return { sessionId: "", telegramConfigured: false };
}

export function createSettingsState() {
  return {
    telegramBotTokenSet: false,
    telegramChatIdSet: false,
    groqApiKeySet: false,
    geminiApiKeySet: false,
    cerebrasApiKeySet: false,
    nvidiaApiKeySet: false,
    codexApiKeySet: false,
    externalDashboardEnabled: false,
    remoteDashboardClient: false,
    dashboardExternalUrls: [],
    telegramBotTokenMasked: "",
    telegramChatIdMasked: "",
    groqApiKeyMasked: "",
    geminiApiKeyMasked: "",
    cerebrasApiKeyMasked: "",
    nvidiaApiKeyMasked: "",
    codexApiKeyMasked: ""
  };
}

export function createGeminiUsageState() {
  return {
    requests: 0,
    prompt_tokens: 0,
    completion_tokens: 0,
    total_tokens: 0,
    input_price_per_million_usd: "0.5000",
    output_price_per_million_usd: "3.0000",
    estimated_cost_usd: "0.000000"
  };
}

export function createCopilotPremiumUsageState() {
  return {
    available: false,
    requires_user_scope: false,
    message: "-",
    username: "",
    plan_name: "-",
    used_requests: "0.0",
    monthly_quota: "0.0",
    percent_used: "0.00",
    refreshed_local: "",
    features_url: "https://github.com/settings/copilot/features",
    billing_url: "https://github.com/settings/billing/premium_requests_usage",
    items: []
  };
}

export function createCopilotLocalUsageState() {
  return {
    selected_model: "",
    selected_model_requests: 0,
    total_requests: 0,
    items: []
  };
}

export function createGuardAlertDispatchState() {
  return {
    statusLabel: "idle",
    statusTone: "neutral",
    message: "전송 대기",
    attemptedAtUtc: "-",
    sentCount: 0,
    failedCount: 0,
    skippedCount: 0,
    targets: []
  };
}
