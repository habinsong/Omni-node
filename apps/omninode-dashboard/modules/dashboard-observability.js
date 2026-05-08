import { formatDecimal } from "./dashboard-formatters.js";

export const TOOL_RESULT_TYPES = new Set([
  "sessions_list_result",
  "sessions_history_result",
  "sessions_send_result",
  "sessions_spawn_result",
  "web_search_result",
  "web_fetch_result",
  "memory_search_result",
  "memory_get_result",
  "cron_result",
  "browser_result",
  "canvas_result",
  "nodes_result",
  "telegram_stub_result"
]);

export const TOOL_RESULT_GROUPS = [
  { key: "sessions", label: "sessions" },
  { key: "cron", label: "cron" },
  { key: "browser", label: "browser" },
  { key: "canvas", label: "canvas" },
  { key: "nodes", label: "nodes" },
  { key: "web", label: "web(rag)" },
  { key: "memory", label: "memory(rag)" },
  { key: "telegram", label: "telegram(stub)" }
];

const TOOL_RESULT_GROUP_BY_TYPE = {
  sessions_list_result: "sessions",
  sessions_history_result: "sessions",
  sessions_send_result: "sessions",
  sessions_spawn_result: "sessions",
  web_search_result: "web",
  web_fetch_result: "web",
  memory_search_result: "memory",
  memory_get_result: "memory",
  cron_result: "cron",
  browser_result: "browser",
  canvas_result: "canvas",
  nodes_result: "nodes",
  telegram_stub_result: "telegram"
};

export const TOOL_RESULT_FILTERS = [
  { key: "all", label: "전체" },
  { key: "sessions", label: "sessions" },
  { key: "cron", label: "cron" },
  { key: "browser", label: "browser" },
  { key: "canvas", label: "canvas" },
  { key: "nodes", label: "nodes" },
  { key: "web", label: "web(rag)" },
  { key: "memory", label: "memory(rag)" },
  { key: "telegram", label: "telegram(stub)" },
  { key: "errors", label: "오류" }
];

const TOOL_RESULT_DOMAIN_BY_GROUP = {
  sessions: "tool",
  cron: "tool",
  browser: "tool",
  canvas: "tool",
  nodes: "tool",
  telegram: "tool",
  web: "rag",
  memory: "rag"
};

export const TOOL_DOMAIN_FILTERS = [
  { key: "all", label: "전체 도메인" },
  { key: "tool", label: "tool" },
  { key: "rag", label: "rag" }
];

export const OPS_DOMAIN_FILTERS = [
  { key: "all", label: "전체 도메인" },
  { key: "provider", label: "provider" },
  { key: "tool", label: "tool" },
  { key: "rag", label: "rag" }
];

export const PROVIDER_RUNTIME_KEYS = ["groq", "gemini", "cerebras", "nvidia", "copilot", "codex", "auto", "unknown"];
export const GUARD_OBS_CHANNEL_KEYS = ["chat", "coding", "telegram", "search", "other"];
export const GUARD_RETRY_TIMELINE_SCHEMA_VERSION = "guard_retry_timeline.v1";
export const GUARD_RETRY_TIMELINE_CHANNELS = ["chat", "coding", "telegram"];
export const GUARD_RETRY_TIMELINE_BUCKET_MINUTES = 5;
export const GUARD_RETRY_TIMELINE_WINDOW_MINUTES = 60;
export const GUARD_RETRY_TIMELINE_MAX_ENTRIES = 512;
export const GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS = 12;
export const GUARD_RETRY_TIMELINE_API_REFRESH_MS = 15000;

const GUARD_OBS_MESSAGE_TYPES = new Set([
  "llm_chat_result",
  "llm_chat_multi_result",
  "coding_result",
  "telegram_stub_result",
  "web_search_result"
]);

export const GUARD_ALERT_RULES = [
  {
    id: "guard_blocked_rate",
    label: "guard 차단 비율",
    metricType: "rate",
    numeratorKey: "blockedTotal",
    warn: 0.45,
    critical: 0.65,
    minTotal: 8
  },
  {
    id: "retry_required_rate",
    label: "retryRequired 비율",
    metricType: "rate",
    numeratorKey: "retryRequiredTotal",
    warn: 0.45,
    critical: 0.7,
    minTotal: 8
  },
  {
    id: "count_lock_unsatisfied_rate",
    label: "count-lock 미충족 비율",
    metricType: "rate",
    numeratorKey: "countLockUnsatisfiedTotal",
    warn: 0.1,
    critical: 0.2,
    minTotal: 4
  },
  {
    id: "citation_validation_failed_rate",
    label: "citation fail 비율",
    metricType: "rate",
    numeratorKey: "citationValidationFailedTotal",
    warn: 0.1,
    critical: 0.2,
    minTotal: 4
  },
  {
    id: "telegram_guard_meta_blocked_count",
    label: "telegram_guard_meta 차단 건수",
    metricType: "count",
    valueKey: "telegramGuardMetaBlockedTotal",
    warn: 1,
    critical: 2,
    minTotal: 1
  }
];

const GUARD_ALERT_PIPELINE_SCHEMA_VERSION = "guard_alert_event.v1";
const GUARD_ALERT_PIPELINE_EVENT_TYPE = "omninode.guard_alert.summary";

export const GUARD_ALERT_PIPELINE_FIELD_ROWS = [
  {
    path: "schemaVersion",
    type: "string",
    required: "Y",
    description: "고정값 guard_alert_event.v1"
  },
  {
    path: "eventType",
    type: "string",
    required: "Y",
    description: "고정값 omninode.guard_alert.summary"
  },
  {
    path: "emittedAtUtc",
    type: "string(ISO-8601)",
    required: "Y",
    description: "이벤트 생성 시각(UTC)"
  },
  {
    path: "keySourcePolicy",
    type: "string",
    required: "Y",
    description: "keychain|secure_file_600"
  },
  {
    path: "geminiKeyRequiredFor",
    type: "string[]",
    required: "Y",
    description: "test/validation/regression/production_run"
  },
  {
    path: "alertSummary",
    type: "object",
    required: "Y",
    description: "경보 최악 등급/트리거/표본부족 집계"
  },
  {
    path: "guardMetrics",
    type: "object",
    required: "Y",
    description: "guard/retry/count-lock/citation/telegram 핵심 카운터"
  },
  {
    path: "guardMetrics.countLockUnsatisfiedByChannel",
    type: "object",
    required: "Y",
    description: "채널별 count-lock 미충족 건수(chat/coding/telegram/search/other)"
  },
  {
    path: "guardMetrics.countLockUnsatisfiedRateByChannel",
    type: "object",
    required: "Y",
    description: "채널별 count-lock 미충족 비율(chat/coding/telegram/search/other)"
  },
  {
    path: "guardAlertRows[]",
    type: "object[]",
    required: "Y",
    description: "규칙별 observed/warn/critical/status"
  },
  {
    path: "retryTimeline",
    type: "object",
    required: "Y",
    description: "채널 공통 retryAttempt/retryMaxAttempts/retryStopReason 5분 버킷 시계열"
  },
  {
    path: "retryTop",
    type: "object",
    required: "N",
    description: "retryAction/retryReason/retryStopReason 상위 집계"
  }
];

export function inferToolResultGroup(type) {
  return TOOL_RESULT_GROUP_BY_TYPE[type] || "unknown";
}

export function inferToolResultDomain(group) {
  return TOOL_RESULT_DOMAIN_BY_GROUP[group] || "tool";
}

export function inferToolResultAction(msg) {
  if (msg && typeof msg.action === "string" && msg.action.trim()) {
    return msg.action.trim();
  }

  switch (msg && msg.type) {
    case "sessions_list_result":
      return "list";
    case "sessions_history_result":
      return "history";
    case "sessions_send_result":
      return "send";
    case "sessions_spawn_result":
      return "spawn";
    case "web_search_result":
      return "search";
    case "web_fetch_result":
      return "fetch";
    case "memory_search_result":
      return "search";
    case "memory_get_result":
      return "get";
    case "telegram_stub_result":
      return "command";
    default:
      return "-";
  }
}

export function inferToolResultStatus(msg) {
  if (!msg || typeof msg !== "object") {
    return { label: "-", tone: "neutral", hasError: false };
  }

  const errorText = typeof msg.error === "string" ? msg.error.trim() : "";
  const rawStatus = typeof msg.status === "string" ? msg.status.trim() : "";
  const lowerStatus = rawStatus.toLowerCase();
  const hasOkField = typeof msg.ok === "boolean";
  const okValue = hasOkField ? msg.ok : null;

  if (errorText) {
    return { label: rawStatus || "error", tone: "error", hasError: true };
  }

  if (hasOkField) {
    if (okValue) {
      return { label: rawStatus || "ok", tone: "ok", hasError: false };
    }
    return { label: rawStatus || "failed", tone: "error", hasError: true };
  }

  if (rawStatus) {
    if (
      lowerStatus.includes("error")
      || lowerStatus.includes("fail")
      || lowerStatus.includes("timeout")
      || lowerStatus.includes("denied")
      || lowerStatus.includes("invalid")
      || lowerStatus.includes("unavailable")
    ) {
      return { label: rawStatus, tone: "error", hasError: true };
    }

    if (
      lowerStatus === "ok"
      || lowerStatus === "accepted"
      || lowerStatus === "success"
      || lowerStatus === "running"
      || lowerStatus === "ready"
    ) {
      return { label: rawStatus, tone: "ok", hasError: false };
    }

    if (
      lowerStatus === "pending"
      || lowerStatus === "queued"
      || lowerStatus === "processing"
      || lowerStatus === "loading"
    ) {
      return { label: rawStatus, tone: "warn", hasError: false };
    }

    return { label: rawStatus, tone: "neutral", hasError: false };
  }

  if (msg.type === "sessions_list_result") {
    return { label: "ok", tone: "ok", hasError: false };
  }

  if (msg.type === "web_search_result") {
    if (msg.disabled === true) {
      return { label: "disabled", tone: "warn", hasError: false };
    }
    return { label: "ok", tone: "ok", hasError: false };
  }

  if (msg.type === "web_fetch_result") {
    if (msg.disabled === true) {
      return { label: "disabled", tone: "warn", hasError: false };
    }
    if (typeof msg.status === "number" && msg.status >= 400) {
      return { label: `http_${msg.status}`, tone: "error", hasError: true };
    }
    return { label: "ok", tone: "ok", hasError: false };
  }

  if (msg.type === "memory_search_result" || msg.type === "memory_get_result") {
    if (msg.disabled === true) {
      return { label: "disabled", tone: "warn", hasError: false };
    }
    return { label: "ok", tone: "ok", hasError: false };
  }

  if (msg.type === "cron_result" && msg.action === "status") {
    return { label: msg.enabled ? "enabled" : "disabled", tone: msg.enabled ? "ok" : "warn", hasError: false };
  }

  if (msg.disabled === true) {
    return { label: "disabled", tone: "warn", hasError: false };
  }

  return { label: "-", tone: "neutral", hasError: false };
}

export function normalizeProviderName(value) {
  const lowered = `${value ?? ""}`.trim().toLowerCase();
  if (!lowered) {
    return "unknown";
  }
  if (lowered.includes("groq")) {
    return "groq";
  }
  if (lowered.includes("gemini")) {
    return "gemini";
  }
  if (lowered.includes("cerebras")) {
    return "cerebras";
  }
  if (lowered.includes("nvidia") || lowered.includes("nim")) {
    return "nvidia";
  }
  if (lowered.includes("copilot")) {
    return "copilot";
  }
  if (lowered.includes("codex")) {
    return "codex";
  }
  if (lowered === "auto") {
    return "auto";
  }
  return "unknown";
}

export function hasFailureCue(text) {
  const lowered = `${text ?? ""}`.trim().toLowerCase();
  if (!lowered) {
    return false;
  }
  return /(error|fail|timeout|denied|invalid|unavailable|unsupported|exception|오류|실패|미지원|중단|quota)/i.test(lowered);
}

export function inferProviderExecutionStatusFromExecution(execution) {
  const raw = execution && typeof execution.status === "string" ? execution.status.trim() : "";
  const lowered = raw.toLowerCase();
  if (!raw) {
    return { label: "success", tone: "ok", hasError: false };
  }
  if (/(error|fail|timeout|cancel|killed|aborted)/i.test(lowered)) {
    return { label: raw, tone: "error", hasError: true };
  }
  if (/(running|pending|queued|processing)/i.test(lowered)) {
    return { label: raw, tone: "warn", hasError: false };
  }
  return { label: raw, tone: "ok", hasError: false };
}

export function summarizeProviderRuntimeEntry(entry) {
  const scope = entry && entry.scope ? entry.scope : "runtime";
  const mode = entry && entry.mode ? entry.mode : "-";
  const provider = entry && entry.provider ? entry.provider : "unknown";
  const statusLabel = entry && entry.statusLabel ? entry.statusLabel : "-";
  const model = entry && entry.model ? entry.model : "-";
  const detail = entry && entry.detail ? entry.detail : "";
  return `${scope}.${mode} ${provider}/${model} ${statusLabel}${detail ? ` ${detail}` : ""}`;
}

export function buildProviderRuntimeEventsFromMessage(msg) {
  if (!msg || typeof msg !== "object" || typeof msg.type !== "string") {
    return [];
  }

  if (msg.type === "llm_chat_result") {
    const provider = normalizeProviderName(msg.provider);
    return [{
      provider,
      scope: "chat",
      mode: msg.mode || "single",
      model: `${msg.model || ""}`.trim(),
      statusLabel: "success",
      statusTone: "ok",
      hasError: false,
      detail: msg.route ? `route=${msg.route}` : ""
    }];
  }

  if (msg.type === "llm_chat_multi_result") {
    const providers = [
      { key: "groq", model: msg.groqModel, text: msg.groq },
      { key: "gemini", model: msg.geminiModel, text: msg.gemini },
      { key: "cerebras", model: msg.cerebrasModel, text: msg.cerebras },
      { key: "nvidia", model: msg.nvidiaModel, text: msg.nvidia },
      { key: "copilot", model: msg.copilotModel, text: msg.copilot },
      { key: "codex", model: msg.codexModel, text: msg.codex }
    ];
    const events = [];
    providers.forEach((item) => {
      const model = `${item.model || ""}`.trim();
      const text = `${item.text || ""}`.trim();
      if (!model && !text) {
        return;
      }
      const failed = hasFailureCue(text);
      events.push({
        provider: item.key,
        scope: "chat",
        mode: "multi",
        model,
        statusLabel: failed ? "failed" : "success",
        statusTone: failed ? "error" : "ok",
        hasError: failed,
        detail: `chars=${text.length}`
      });
    });
    return events;
  }

  if (msg.type === "coding_progress") {
    const provider = normalizeProviderName(msg.provider);
    const detailText = `${msg.phase || ""} ${msg.message || ""}`.trim();
    const stageText = `${msg.stageTitle || ""}`.trim();
    const finished = !!msg.done;
    const failed = finished && hasFailureCue(detailText);
    const statusLabel = finished ? (failed ? "failed" : "success") : "progress";
    const statusTone = finished ? (failed ? "error" : "ok") : "warn";
    return [{
      provider,
      scope: msg.scope || "coding",
      mode: msg.mode || "single",
      model: `${msg.model || ""}`.trim(),
      statusLabel,
      statusTone,
      hasError: failed,
      detail: `${stageText ? `stage=${stageText} ` : ""}phase=${msg.phase || "-"} iter=${Number.isFinite(msg.iteration) ? msg.iteration : "-"}`
    }];
  }

  if (msg.type === "coding_result") {
    const events = [];
    const mainStatus = inferProviderExecutionStatusFromExecution(msg.execution);
    events.push({
      provider: normalizeProviderName(msg.provider),
      scope: "coding",
      mode: msg.mode || "single",
      model: `${msg.model || ""}`.trim(),
      statusLabel: mainStatus.label,
      statusTone: mainStatus.tone,
      hasError: mainStatus.hasError,
      detail: `exit=${Number.isFinite(msg && msg.execution && msg.execution.exitCode) ? msg.execution.exitCode : "-"}`
    });
    if (Array.isArray(msg.workers)) {
      msg.workers.forEach((worker) => {
        if (!worker || typeof worker !== "object") {
          return;
        }
        const workerStatus = inferProviderExecutionStatusFromExecution(worker.execution);
        events.push({
          provider: normalizeProviderName(worker.provider),
          scope: "coding",
          mode: `${msg.mode || "single"}-worker`,
          model: `${worker.model || ""}`.trim(),
          statusLabel: workerStatus.label,
          statusTone: workerStatus.tone,
          hasError: workerStatus.hasError,
          detail: `worker=${normalizeProviderName(worker.provider)}`
        });
      });
    }
    return events;
  }

  if (msg.type === "error") {
    const raw = `${msg.message || ""}`.trim();
    if (!raw) {
      return [];
    }
    if (!/(chat|coding|provider|groq|gemini|cerebras|nvidia|nim|copilot)/i.test(raw)) {
      return [];
    }
    return [{
      provider: normalizeProviderName(raw),
      scope: /coding/i.test(raw) ? "coding" : "chat",
      mode: "error",
      model: "",
      statusLabel: "failed",
      statusTone: "error",
      hasError: true,
      detail: raw
    }];
  }

  return [];
}

export function normalizeGuardToken(value) {
  const normalized = `${value ?? ""}`.trim().toLowerCase();
  if (!normalized) {
    return "-";
  }
  return normalized.replace(/\s+/g, "_");
}

export function isCountLockUnsatisfiedToken(value) {
  return value === "count_lock_unsatisfied"
    || value === "count_lock_unsatisfied_after_retries"
    || value === "target_count_mismatch";
}

export function normalizeGuardNumber(value) {
  const numeric = Number(value);
  if (!Number.isFinite(numeric) || numeric < 0) {
    return 0;
  }
  return Math.floor(numeric);
}

export function formatGuardAlertThreshold(metricType, value) {
  if (metricType === "rate") {
    return `${formatDecimal(Number(value || 0) * 100, 1)}%`;
  }
  return `${normalizeGuardNumber(value)}건`;
}

export function severityRank(severity) {
  if (severity === "critical") {
    return 3;
  }
  if (severity === "warn") {
    return 2;
  }
  if (severity === "ok") {
    return 1;
  }
  return 0;
}

export function buildGuardAlertRuleResult(rule, metrics) {
  const total = normalizeGuardNumber(metrics && metrics.total);
  const minTotal = normalizeGuardNumber(rule && rule.minTotal);
  const metricType = rule && rule.metricType === "rate" ? "rate" : "count";
  const warn = Number(rule && rule.warn);
  const critical = Number(rule && rule.critical);
  const warnThreshold = Number.isFinite(warn) && warn >= 0 ? warn : 0;
  const criticalThreshold = Number.isFinite(critical) && critical >= 0 ? critical : warnThreshold;

  let observed = 0;
  let numeratorValue = 0;
  let denominatorLabel = "-";
  if (metricType === "rate") {
    const numerator = normalizeGuardNumber(metrics && metrics[rule.numeratorKey]);
    numeratorValue = numerator;
    observed = total > 0 ? numerator / total : 0;
    denominatorLabel = `${total}건`;
  } else {
    observed = normalizeGuardNumber(metrics && metrics[rule.valueKey]);
    numeratorValue = observed;
    denominatorLabel = `표본 ${total}건`;
  }

  if (total < minTotal) {
    return {
      id: rule.id,
      label: rule.label,
      metricType,
      observed,
      numeratorValue,
      sampleCount: total,
      minTotal,
      warnThreshold,
      criticalThreshold,
      valueLabel: metricType === "rate"
        ? `${formatGuardAlertThreshold("rate", observed)} (${denominatorLabel})`
        : formatGuardAlertThreshold("count", observed),
      warnLabel: formatGuardAlertThreshold(metricType, warnThreshold),
      criticalLabel: formatGuardAlertThreshold(metricType, criticalThreshold),
      statusLabel: "sample_pending",
      statusTone: "neutral",
      severity: "neutral",
      note: `표본 부족 (${total}/${minTotal})`
    };
  }

  let severity = "ok";
  if (observed >= criticalThreshold) {
    severity = "critical";
  } else if (observed >= warnThreshold) {
    severity = "warn";
  }

  const statusTone = severity === "critical"
    ? "error"
    : (severity === "warn" ? "warn" : "ok");

  return {
    id: rule.id,
    label: rule.label,
    metricType,
    observed,
    numeratorValue,
    sampleCount: total,
    minTotal,
    warnThreshold,
    criticalThreshold,
    valueLabel: metricType === "rate"
      ? `${formatGuardAlertThreshold("rate", observed)} (${denominatorLabel})`
      : formatGuardAlertThreshold("count", observed),
    warnLabel: formatGuardAlertThreshold(metricType, warnThreshold),
    criticalLabel: formatGuardAlertThreshold(metricType, criticalThreshold),
    statusLabel: severity,
    statusTone,
    severity,
    note: metricType === "rate" ? `분모 ${denominatorLabel}` : denominatorLabel
  };
}

export function normalizeUtcIso(value) {
  const parsed = Date.parse(`${value || ""}`);
  if (!Number.isFinite(parsed)) {
    return null;
  }
  return new Date(parsed).toISOString();
}

export function pickTopRetryStopReason(stopReasonCounts) {
  const entries = Object.entries(stopReasonCounts || {});
  if (entries.length === 0) {
    return "-";
  }

  entries.sort((a, b) => {
    if (b[1] !== a[1]) {
      return b[1] - a[1];
    }
    return a[0].localeCompare(b[0]);
  });
  return entries[0][0] || "-";
}

export function buildGuardRetryTimelineEntry(event, capturedAt) {
  if (!event || typeof event !== "object") {
    return null;
  }

  if (!GUARD_RETRY_TIMELINE_CHANNELS.includes(event.channel)) {
    return null;
  }

  const capturedAtUtc = normalizeUtcIso(capturedAt);
  if (!capturedAtUtc) {
    return null;
  }

  return {
    id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    capturedAt: capturedAtUtc,
    channel: event.channel,
    retryRequired: !!event.retryRequired,
    retryAttempt: normalizeGuardNumber(event.retryAttempt),
    retryMaxAttempts: normalizeGuardNumber(event.retryMaxAttempts),
    retryStopReason: normalizeGuardToken(event.retryStopReason)
  };
}

export function buildGuardRetryTimelineSnapshot(entries, options = {}) {
  const configuredChannels = Array.isArray(options.channels)
    ? options.channels.filter((channel) => GUARD_OBS_CHANNEL_KEYS.includes(channel))
    : [];
  const channels = configuredChannels.length > 0
    ? configuredChannels
    : GUARD_RETRY_TIMELINE_CHANNELS.slice();
  const bucketMinutes = Math.max(
    1,
    normalizeGuardNumber(options.bucketMinutes) || GUARD_RETRY_TIMELINE_BUCKET_MINUTES
  );
  const windowMinutes = Math.max(
    bucketMinutes,
    normalizeGuardNumber(options.windowMinutes) || GUARD_RETRY_TIMELINE_WINDOW_MINUTES
  );
  const maxBucketRows = Math.max(
    1,
    normalizeGuardNumber(options.maxBucketRows) || GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS
  );
  const bucketSizeMs = bucketMinutes * 60 * 1000;
  const nowMs = Date.now();
  const windowStartMs = nowMs - (windowMinutes * 60 * 1000);

  const byChannel = {};
  channels.forEach((channel) => {
    byChannel[channel] = {
      channel,
      totalSamples: 0,
      retryRequiredSamples: 0,
      maxRetryAttempt: 0,
      maxRetryMaxAttempts: 0,
      lastRetryStopReason: "-",
      buckets: new Map()
    };
  });

  if (Array.isArray(entries)) {
    entries.forEach((entry) => {
      const channel = `${entry && entry.channel ? entry.channel : ""}`;
      if (!Object.prototype.hasOwnProperty.call(byChannel, channel)) {
        return;
      }

      const capturedAtUtc = normalizeUtcIso(entry && entry.capturedAt);
      if (!capturedAtUtc) {
        return;
      }

      const capturedAtMs = Date.parse(capturedAtUtc);
      if (!Number.isFinite(capturedAtMs) || capturedAtMs < windowStartMs) {
        return;
      }

      const retryRequired = !!(entry && entry.retryRequired);
      const retryAttempt = normalizeGuardNumber(entry && entry.retryAttempt);
      const retryMaxAttempts = normalizeGuardNumber(entry && entry.retryMaxAttempts);
      const retryStopReason = normalizeGuardToken(entry && entry.retryStopReason);
      const bucketStartMs = Math.floor(capturedAtMs / bucketSizeMs) * bucketSizeMs;
      const bucketStartUtc = new Date(bucketStartMs).toISOString();
      const channelTarget = byChannel[channel];
      channelTarget.totalSamples += 1;
      if (retryRequired) {
        channelTarget.retryRequiredSamples += 1;
      }
      channelTarget.maxRetryAttempt = Math.max(channelTarget.maxRetryAttempt, retryAttempt);
      channelTarget.maxRetryMaxAttempts = Math.max(channelTarget.maxRetryMaxAttempts, retryMaxAttempts);
      if (channelTarget.lastRetryStopReason === "-" && retryStopReason !== "-") {
        channelTarget.lastRetryStopReason = retryStopReason;
      }

      let bucketTarget = channelTarget.buckets.get(bucketStartUtc);
      if (!bucketTarget) {
        bucketTarget = {
          bucketStartUtc,
          bucketStartMs,
          samples: 0,
          retryRequiredCount: 0,
          maxRetryAttempt: 0,
          maxRetryMaxAttempts: 0,
          stopReasonCounts: {}
        };
        channelTarget.buckets.set(bucketStartUtc, bucketTarget);
      }

      bucketTarget.samples += 1;
      if (retryRequired) {
        bucketTarget.retryRequiredCount += 1;
      }
      bucketTarget.maxRetryAttempt = Math.max(bucketTarget.maxRetryAttempt, retryAttempt);
      bucketTarget.maxRetryMaxAttempts = Math.max(bucketTarget.maxRetryMaxAttempts, retryMaxAttempts);
      if (retryStopReason !== "-") {
        bucketTarget.stopReasonCounts[retryStopReason] = (bucketTarget.stopReasonCounts[retryStopReason] || 0) + 1;
      }
    });
  }

  return {
    schemaVersion: GUARD_RETRY_TIMELINE_SCHEMA_VERSION,
    generatedAtUtc: new Date().toISOString(),
    bucketMinutes,
    windowMinutes,
    channels: channels.map((channel) => {
      const channelTarget = byChannel[channel];
      const buckets = Array.from(channelTarget.buckets.values())
        .sort((a, b) => b.bucketStartMs - a.bucketStartMs)
        .slice(0, maxBucketRows)
        .map((bucket) => ({
          bucketStartUtc: bucket.bucketStartUtc,
          samples: bucket.samples,
          retryRequiredCount: bucket.retryRequiredCount,
          maxRetryAttempt: bucket.maxRetryAttempt,
          maxRetryMaxAttempts: bucket.maxRetryMaxAttempts,
          topRetryStopReason: pickTopRetryStopReason(bucket.stopReasonCounts),
          uniqueRetryStopReasons: Object.keys(bucket.stopReasonCounts).length
        }));

      return {
        channel,
        totalSamples: channelTarget.totalSamples,
        retryRequiredSamples: channelTarget.retryRequiredSamples,
        maxRetryAttempt: channelTarget.maxRetryAttempt,
        maxRetryMaxAttempts: channelTarget.maxRetryMaxAttempts,
        lastRetryStopReason: channelTarget.lastRetryStopReason,
        buckets
      };
    })
  };
}

export function normalizeGuardRetryTimelineSnapshot(snapshot, defaults = {}) {
  if (!snapshot || typeof snapshot !== "object") {
    return null;
  }

  const channels = Array.isArray(defaults.channels)
    ? defaults.channels.filter((channel) => GUARD_RETRY_TIMELINE_CHANNELS.includes(channel))
    : GUARD_RETRY_TIMELINE_CHANNELS.slice();
  const fallbackBucketMinutes = Math.max(
    1,
    normalizeGuardNumber(defaults.bucketMinutes) || GUARD_RETRY_TIMELINE_BUCKET_MINUTES
  );
  const fallbackWindowMinutes = Math.max(
    fallbackBucketMinutes,
    normalizeGuardNumber(defaults.windowMinutes) || GUARD_RETRY_TIMELINE_WINDOW_MINUTES
  );
  const fallbackMaxBucketRows = Math.max(
    1,
    normalizeGuardNumber(defaults.maxBucketRows) || GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS
  );
  const maxBucketRows = Math.max(1, Math.min(fallbackMaxBucketRows, 288));
  const bucketMinutes = Math.max(
    1,
    normalizeGuardNumber(snapshot.bucketMinutes) || fallbackBucketMinutes
  );
  const windowMinutes = Math.max(
    bucketMinutes,
    normalizeGuardNumber(snapshot.windowMinutes) || fallbackWindowMinutes
  );
  const generatedAtUtc = normalizeUtcIso(snapshot.generatedAtUtc) || new Date().toISOString();
  const byChannel = {};

  channels.forEach((channel) => {
    byChannel[channel] = {
      channel,
      totalSamples: 0,
      retryRequiredSamples: 0,
      maxRetryAttempt: 0,
      maxRetryMaxAttempts: 0,
      lastRetryStopReason: "-",
      buckets: []
    };
  });

  if (Array.isArray(snapshot.channels)) {
    snapshot.channels.forEach((channelRow) => {
      const channel = `${channelRow && channelRow.channel ? channelRow.channel : ""}`;
      if (!Object.prototype.hasOwnProperty.call(byChannel, channel)) {
        return;
      }

      const target = byChannel[channel];
      target.totalSamples = normalizeGuardNumber(channelRow && channelRow.totalSamples);
      target.retryRequiredSamples = normalizeGuardNumber(channelRow && channelRow.retryRequiredSamples);
      target.maxRetryAttempt = normalizeGuardNumber(channelRow && channelRow.maxRetryAttempt);
      target.maxRetryMaxAttempts = normalizeGuardNumber(channelRow && channelRow.maxRetryMaxAttempts);
      target.lastRetryStopReason = normalizeGuardToken(channelRow && channelRow.lastRetryStopReason);

      if (!Array.isArray(channelRow && channelRow.buckets)) {
        return;
      }

      target.buckets = channelRow.buckets
        .map((bucket) => {
          const bucketStartUtc = normalizeUtcIso(bucket && bucket.bucketStartUtc);
          if (!bucketStartUtc) {
            return null;
          }
          return {
            bucketStartUtc,
            samples: normalizeGuardNumber(bucket && bucket.samples),
            retryRequiredCount: normalizeGuardNumber(bucket && bucket.retryRequiredCount),
            maxRetryAttempt: normalizeGuardNumber(bucket && bucket.maxRetryAttempt),
            maxRetryMaxAttempts: normalizeGuardNumber(bucket && bucket.maxRetryMaxAttempts),
            topRetryStopReason: normalizeGuardToken(bucket && bucket.topRetryStopReason),
            uniqueRetryStopReasons: normalizeGuardNumber(bucket && bucket.uniqueRetryStopReasons)
          };
        })
        .filter(Boolean)
        .sort((a, b) => (a.bucketStartUtc < b.bucketStartUtc ? 1 : -1))
        .slice(0, maxBucketRows);
    });
  }

  return {
    schemaVersion: `${snapshot.schemaVersion || GUARD_RETRY_TIMELINE_SCHEMA_VERSION}`,
    generatedAtUtc,
    bucketMinutes,
    windowMinutes,
    channels: channels.map((channel) => byChannel[channel])
  };
}

export function buildGuardAlertPipelineEvent(guardObsStats, guardAlertSummary, guardRetryTimeline, options = {}) {
  const emittedAtUtc = typeof options.emittedAtUtc === "string" && options.emittedAtUtc.trim()
    ? options.emittedAtUtc.trim()
    : new Date().toISOString();
  const normalizeTopRows = (rows) => {
    if (!Array.isArray(rows)) {
      return [];
    }
    return rows.slice(0, 6).map((item) => ({
      name: `${item && item.name ? item.name : "-"}`,
      count: normalizeGuardNumber(item && item.count)
    }));
  };
  const countLockUnsatisfiedByChannel = {};
  const countLockUnsatisfiedRateByChannel = {};
  GUARD_OBS_CHANNEL_KEYS.forEach((channel) => {
    const channelStat = guardObsStats && guardObsStats.byChannel && guardObsStats.byChannel[channel];
    const count = normalizeGuardNumber(channelStat && channelStat.count);
    const countLockUnsatisfied = normalizeGuardNumber(channelStat && channelStat.countLockUnsatisfiedCount);
    countLockUnsatisfiedByChannel[channel] = countLockUnsatisfied;
    countLockUnsatisfiedRateByChannel[channel] = count > 0
      ? Number((countLockUnsatisfied / count).toFixed(6))
      : 0;
  });

  return {
    schemaVersion: GUARD_ALERT_PIPELINE_SCHEMA_VERSION,
    eventType: GUARD_ALERT_PIPELINE_EVENT_TYPE,
    emittedAtUtc,
    source: "omninode-dashboard",
    pipelineTargets: ["webhook", "log_collector"],
    keySourcePolicy: "keychain|secure_file_600",
    geminiKeyRequiredFor: ["test", "validation", "regression", "production_run"],
    alertSummary: {
      status: `${guardAlertSummary && guardAlertSummary.statusLabel ? guardAlertSummary.statusLabel : "sample_pending"}`,
      tone: `${guardAlertSummary && guardAlertSummary.statusTone ? guardAlertSummary.statusTone : "neutral"}`,
      triggeredCount: normalizeGuardNumber(guardAlertSummary && guardAlertSummary.triggeredCount),
      samplePendingCount: normalizeGuardNumber(guardAlertSummary && guardAlertSummary.samplePendingCount)
    },
    guardMetrics: {
      total: normalizeGuardNumber(guardObsStats && guardObsStats.total),
      blockedTotal: normalizeGuardNumber(guardObsStats && guardObsStats.blockedTotal),
      retryRequiredTotal: normalizeGuardNumber(guardObsStats && guardObsStats.retryRequiredTotal),
      countLockUnsatisfiedTotal: normalizeGuardNumber(guardObsStats && guardObsStats.countLockUnsatisfiedTotal),
      countLockUnsatisfiedByChannel,
      countLockUnsatisfiedRateByChannel,
      citationValidationFailedTotal: normalizeGuardNumber(guardObsStats && guardObsStats.citationValidationFailedTotal),
      citationMappingRetryTotal: normalizeGuardNumber(guardObsStats && guardObsStats.citationMappingRetryTotal),
      citationMappingCountTotal: normalizeGuardNumber(guardObsStats && guardObsStats.citationMappingCountTotal),
      telegramGuardMetaBlockedTotal: normalizeGuardNumber(guardObsStats && guardObsStats.telegramGuardMetaBlockedTotal)
    },
    guardAlertRows: (Array.isArray(guardObsStats && guardObsStats.guardAlertRows) ? guardObsStats.guardAlertRows : []).map((row) => ({
      id: `${row && row.id ? row.id : "-"}`,
      label: `${row && row.label ? row.label : "-"}`,
      metricType: row && row.metricType === "rate" ? "rate" : "count",
      observed: Number.isFinite(Number(row && row.observed)) ? Number(row.observed) : 0,
      numeratorValue: normalizeGuardNumber(row && row.numeratorValue),
      sampleCount: normalizeGuardNumber(row && row.sampleCount),
      minTotal: normalizeGuardNumber(row && row.minTotal),
      warnThreshold: Number.isFinite(Number(row && row.warnThreshold)) ? Number(row.warnThreshold) : 0,
      criticalThreshold: Number.isFinite(Number(row && row.criticalThreshold)) ? Number(row.criticalThreshold) : 0,
      status: `${row && row.statusLabel ? row.statusLabel : "sample_pending"}`,
      severity: `${row && row.severity ? row.severity : "neutral"}`,
      note: `${row && row.note ? row.note : "-"}`
    })),
    retryTimeline: (guardRetryTimeline && typeof guardRetryTimeline === "object")
      ? guardRetryTimeline
      : {
          schemaVersion: GUARD_RETRY_TIMELINE_SCHEMA_VERSION,
          generatedAtUtc: new Date().toISOString(),
          bucketMinutes: GUARD_RETRY_TIMELINE_BUCKET_MINUTES,
          windowMinutes: GUARD_RETRY_TIMELINE_WINDOW_MINUTES,
          channels: []
        },
    retryTop: {
      actions: normalizeTopRows(guardObsStats && guardObsStats.topRetryActions),
      reasons: normalizeTopRows(guardObsStats && guardObsStats.topRetryReasons),
      stopReasons: normalizeTopRows(guardObsStats && guardObsStats.topRetryStopReasons)
    }
  };
}

export function inferGuardChannel(type) {
  if (type === "llm_chat_result" || type === "llm_chat_multi_result") {
    return "chat";
  }
  if (type === "coding_result") {
    return "coding";
  }
  if (type === "telegram_stub_result") {
    return "telegram";
  }
  if (type === "web_search_result") {
    return "search";
  }
  return "other";
}

export function buildGuardObsEvent(msg) {
  if (!msg || typeof msg !== "object" || !GUARD_OBS_MESSAGE_TYPES.has(msg.type)) {
    return null;
  }

  const channel = inferGuardChannel(msg.type);
  const guardCategory = normalizeGuardToken(msg.guardCategory);
  const guardReason = normalizeGuardToken(msg.guardReason);
  const retryAction = normalizeGuardToken(msg.retryAction);
  const retryReason = normalizeGuardToken(msg.retryReason);
  const retryStopReason = normalizeGuardToken(msg.retryStopReason);
  const retryRequired = !!msg.retryRequired;
  const retryAttempt = normalizeGuardNumber(msg.retryAttempt);
  const retryMaxAttempts = normalizeGuardNumber(msg.retryMaxAttempts);
  const citationMappingCount = Array.isArray(msg.citationMappings) ? msg.citationMappings.length : 0;
  const citationValidationPassed = (msg.citationValidation && typeof msg.citationValidation.passed === "boolean")
    ? !!msg.citationValidation.passed
    : null;
  const citationValidationReason = normalizeGuardToken(msg.citationValidation && msg.citationValidation.reasonCode);
  const hasGuardFailure = guardCategory !== "-" || guardReason !== "-";
  const hasCountLockUnsatisfied =
    isCountLockUnsatisfiedToken(retryStopReason)
    || isCountLockUnsatisfiedToken(guardReason);
  const hasCitationValidationFailure = citationValidationPassed === false
    || citationValidationReason === "citation_validation_failed"
    || retryReason === "citation_validation_failed";
  const isCitationMappingRetry = retryAction === "retry_with_citation_mapping"
    || retryReason === "citation_mapping";
  const summaryParts = [
    `${channel}/${msg.type}`
  ];

  if (hasGuardFailure) {
    summaryParts.push(`guard=${guardCategory}:${guardReason}`);
  }
  if (retryAction !== "-") {
    summaryParts.push(`retry=${retryAction}`);
  }
  if (retryStopReason !== "-") {
    summaryParts.push(`stop=${retryStopReason}`);
  }
  if (hasCountLockUnsatisfied) {
    summaryParts.push("countLock=unsatisfied");
  }
  if (citationValidationPassed === true) {
    summaryParts.push("citation=pass");
  } else if (citationValidationPassed === false) {
    summaryParts.push("citation=fail");
  }
  if (citationMappingCount > 0) {
    summaryParts.push(`mapping=${citationMappingCount}`);
  }

  return {
    channel,
    type: msg.type,
    guardCategory,
    guardReason,
    retryRequired,
    retryAction,
    retryReason,
    retryAttempt,
    retryMaxAttempts,
    retryStopReason,
    citationMappingCount,
    citationValidationPassed,
    citationValidationReason,
    hasGuardFailure,
    hasCountLockUnsatisfied,
    hasCitationValidationFailure,
    isCitationMappingRetry,
    summary: summaryParts.join(" ")
  };
}
