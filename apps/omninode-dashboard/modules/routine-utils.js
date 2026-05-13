import {
  DEFAULT_ROUTINE_AGENT_MODEL,
  DEFAULT_ROUTINE_AGENT_PROVIDER
} from "./dashboard-constants.js";

export const DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS = 120;
export const MIN_ROUTINE_AGENT_TIMEOUT_SECONDS = 120;
export const MAX_ROUTINE_AGENT_TIMEOUT_SECONDS = 1800;

export function normalizeRoutineAgentToolProfile(value, agentUsePlaywright = true) {
  const normalized = (value || "").trim().toLowerCase();
  if (!normalized) {
    return "playwright_only";
  }
  if (normalized === "desktop_control" || normalized === "desktop-control") {
    return "desktop_control";
  }
  if (normalized === "playwright_only" || normalized === "playwright-only" || normalized === "playwright") {
    return "playwright_only";
  }
  return agentUsePlaywright === false ? "playwright_only" : normalized;
}

export function formatRoutineAgentToolProfileLabel(value) {
  return normalizeRoutineAgentToolProfile(value) === "desktop_control"
    ? "데스크톱 제어"
    : "Playwright 전용";
}

export function isRoutineDesktopControlSupportedClient() {
  if (typeof navigator === "undefined") {
    return false;
  }
  const platform = `${navigator.userAgentData?.platform || navigator.platform || ""}`.trim().toLowerCase();
  return platform.includes("mac");
}

export function getViewportSnapshot() {
  if (typeof window === "undefined") {
    return { width: 1440, height: 960 };
  }
  return {
    width: window.innerWidth || 1440,
    height: window.innerHeight || 960
  };
}

export function getRoutineLocalTimezone() {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "Asia/Seoul";
  } catch (_err) {
    return "Asia/Seoul";
  }
}

export function normalizeRoutineWeekdays(values) {
  if (!Array.isArray(values)) {
    return [];
  }

  const normalized = values
    .map((value) => Number(value))
    .filter((value) => Number.isInteger(value))
    .map((value) => (value === 7 ? 0 : value))
    .filter((value) => value >= 0 && value <= 6);
  const unique = Array.from(new Set(normalized));
  unique.sort((a, b) => {
    const normalizedA = a === 0 ? 7 : a;
    const normalizedB = b === 0 ? 7 : b;
    return normalizedA - normalizedB;
  });
  return unique;
}

export function formatRoutineWeekdayLabel(value) {
  switch (value) {
    case 0: return "일";
    case 1: return "월";
    case 2: return "화";
    case 3: return "수";
    case 4: return "목";
    case 5: return "금";
    case 6: return "토";
    default: return "월";
  }
}

export function normalizeRoutineScheduleSourceMode(value, fallback = "auto") {
  const normalized = (value || "").trim().toLowerCase();
  if (normalized === "manual") {
    return "manual";
  }
  if (normalized === "auto") {
    return "auto";
  }
  return fallback;
}

export function normalizeRoutineExecutionModeValue(value) {
  const normalized = (value || "").trim().toLowerCase();
  if (normalized === "web" || normalized === "url" || normalized === "script" || normalized === "browser_agent") {
    return normalized;
  }
  return "";
}

export function looksLikeRoutineLocalSystemRequest(input) {
  const normalized = (input || "").trim().toLowerCase();
  if (!normalized) {
    return false;
  }
  return [
    "서버 상태", "시스템 상태", "cpu", "메모리", "ram", "디스크", "storage", "load",
    "uptime", "프로세스", "포트", "service", "로그", "docker", "컨테이너", "k8s",
    "워크스페이스", "파일", "폴더", "디렉터리", "코드", "빌드", "테스트", "git", "호스트"
  ].some((token) => normalized.includes(token.toLowerCase()));
}

export function inferRoutineExecutionModeFromRequest(request) {
  const normalized = (request || "").trim();
  if (!normalized) {
    return "web";
  }
  if (/https?:\/\//i.test(normalized)) {
    return "url";
  }
  if (!looksLikeRoutineLocalSystemRequest(normalized) && /(뉴스|news|헤드라인|속보|브리핑|기사|랭킹|이슈|실시간|최신|최근|오늘|현재|지금)/i.test(normalized)) {
    return "web";
  }
  return "script";
}

export function resolveRoutineVisibleExecutionMode(form) {
  const explicit = normalizeRoutineExecutionModeValue(form?.executionMode);
  if (explicit) {
    return explicit;
  }
  return inferRoutineExecutionModeFromRequest(form?.request || "");
}

export function formatRoutineExecutionModeLabel(value) {
  switch (normalizeRoutineExecutionModeValue(value)) {
    case "browser_agent": return "브라우저 에이전트";
    case "url": return "URL 참조";
    case "web": return "일반 답변";
    default: return "스크립트";
  }
}

export function normalizeRoutineNotifyPolicy(value, fallback = "always") {
  const normalized = (value || "").trim().toLowerCase();
  if (normalized === "on_change") {
    return "on_change";
  }
  if (normalized === "error_only") {
    return "error_only";
  }
  if (normalized === "never") {
    return "never";
  }
  return fallback;
}

export function normalizeRoutineNotifyTelegram(value, fallback = true) {
  if (typeof value === "boolean") {
    return value;
  }
  return fallback;
}

export function createRoutineFormState(overrides = {}) {
  return {
    title: "",
    request: "",
    executionMode: "",
    agentProvider: DEFAULT_ROUTINE_AGENT_PROVIDER,
    agentModel: DEFAULT_ROUTINE_AGENT_MODEL,
    agentStartUrl: "",
    agentTimeoutSeconds: DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS,
    agentToolProfile: "playwright_only",
    agentUsePlaywright: true,
    scheduleSourceMode: "auto",
    maxRetries: 1,
    retryDelaySeconds: 15,
    runImmediately: false,
    notifyPolicy: "always",
    notifyTelegram: true,
    scheduleKind: "daily",
    scheduleTime: "08:00",
    dayOfMonth: 1,
    weekdays: [1, 2, 3, 4, 5],
    timezoneId: getRoutineLocalTimezone(),
    ...overrides
  };
}

export function hydrateRoutineFormFromRoutine(routine) {
  if (!routine) {
    return createRoutineFormState();
  }

  return createRoutineFormState({
    title: routine.title || "",
    request: routine.request || "",
    executionMode: normalizeRoutineExecutionModeValue(routine.executionMode),
    agentProvider: (routine.agentProvider || DEFAULT_ROUTINE_AGENT_PROVIDER).trim() || DEFAULT_ROUTINE_AGENT_PROVIDER,
    agentModel: (routine.agentModel || DEFAULT_ROUTINE_AGENT_MODEL).trim() || DEFAULT_ROUTINE_AGENT_MODEL,
    agentStartUrl: routine.agentStartUrl || "",
    agentTimeoutSeconds: Number.isFinite(routine.agentTimeoutSeconds) ? Number(routine.agentTimeoutSeconds) : DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS,
    agentToolProfile: normalizeRoutineAgentToolProfile(routine.agentToolProfile, routine.agentUsePlaywright !== false),
    agentUsePlaywright: routine.agentUsePlaywright !== false,
    scheduleSourceMode: normalizeRoutineScheduleSourceMode(routine.scheduleSourceMode, "manual"),
    maxRetries: Number.isFinite(routine.maxRetries) ? Number(routine.maxRetries) : 1,
    retryDelaySeconds: Number.isFinite(routine.retryDelaySeconds) ? Number(routine.retryDelaySeconds) : 15,
    notifyPolicy: normalizeRoutineNotifyPolicy(routine.notifyPolicy, "always"),
    runImmediately: false,
    notifyTelegram: normalizeRoutineNotifyTelegram(routine.notifyTelegram, true),
    scheduleKind: routine.scheduleKind || "daily",
    scheduleTime: routine.timeOfDay || "08:00",
    dayOfMonth: Number.isFinite(routine.dayOfMonth) ? Number(routine.dayOfMonth) : 1,
    weekdays: normalizeRoutineWeekdays(routine.weekdays || []),
    timezoneId: routine.timezoneId || getRoutineLocalTimezone()
  });
}

export function buildRoutinePayloadFromForm(form) {
  const scheduleSourceMode = normalizeRoutineScheduleSourceMode(form?.scheduleSourceMode, "auto");
  const scheduleKind = (form?.scheduleKind || "daily").trim().toLowerCase();
  const executionMode = normalizeRoutineExecutionModeValue(form?.executionMode);
  const weekdays = scheduleKind === "weekly"
    ? normalizeRoutineWeekdays(form?.weekdays || [])
    : [];
  const dayOfMonth = scheduleKind === "monthly"
    ? Math.min(31, Math.max(1, Number(form?.dayOfMonth || 1) || 1))
    : null;
  return {
    title: (form?.title || "").trim(),
    text: (form?.request || "").trim(),
    executionMode,
    agentProvider: (form?.agentProvider || "").trim(),
    agentModel: (form?.agentModel || "").trim(),
    agentStartUrl: (form?.agentStartUrl || "").trim(),
    agentTimeoutSeconds: Math.min(
      MAX_ROUTINE_AGENT_TIMEOUT_SECONDS,
      Math.max(
        MIN_ROUTINE_AGENT_TIMEOUT_SECONDS,
        Number(form?.agentTimeoutSeconds ?? DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS) || DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS
      )
    ),
    agentToolProfile: executionMode === "browser_agent"
      ? normalizeRoutineAgentToolProfile(form?.agentToolProfile, form?.agentUsePlaywright !== false)
      : "",
    agentUsePlaywright: form?.agentUsePlaywright !== false,
    scheduleSourceMode,
    maxRetries: Math.min(5, Math.max(0, Number(form?.maxRetries ?? 1) || 0)),
    retryDelaySeconds: Math.min(300, Math.max(0, Number(form?.retryDelaySeconds ?? 15) || 0)),
    runImmediately: form?.runImmediately === true,
    notifyPolicy: normalizeRoutineNotifyPolicy(form?.notifyPolicy, "always"),
    notifyTelegram: normalizeRoutineNotifyTelegram(form?.notifyTelegram, true),
    scheduleKind,
    scheduleTime: (form?.scheduleTime || "08:00").trim() || "08:00",
    dayOfMonth,
    weekdays,
    timezoneId: (form?.timezoneId || getRoutineLocalTimezone()).trim() || getRoutineLocalTimezone()
  };
}

export function formatRoutineSchedulePreview(form) {
  const sourceMode = normalizeRoutineScheduleSourceMode(form?.scheduleSourceMode, "auto");
  if (sourceMode === "auto") {
    return "요청 원문 기준 자동 · 스케줄 표현이 없으면 매일 08:00";
  }

  const kind = (form?.scheduleKind || "daily").trim().toLowerCase();
  const timeOfDay = (form?.scheduleTime || "08:00").trim() || "08:00";
  const timezoneId = (form?.timezoneId || getRoutineLocalTimezone()).trim() || getRoutineLocalTimezone();
  const suffix = timezoneId === getRoutineLocalTimezone() ? "" : ` · ${timezoneId}`;
  if (kind === "weekly") {
    const labels = normalizeRoutineWeekdays(form?.weekdays || []).map((value) => formatRoutineWeekdayLabel(value));
    return `매주 ${labels.length > 0 ? labels.join(", ") : "월"} ${timeOfDay}${suffix}`;
  }

  if (kind === "monthly") {
    const dayOfMonth = Math.min(31, Math.max(1, Number(form?.dayOfMonth || 1) || 1));
    return `매월 ${dayOfMonth}일 ${timeOfDay}${suffix}`;
  }

  return `매일 ${timeOfDay}${suffix}`;
}

export function getRoutineAgentModelFallback() {
  return DEFAULT_ROUTINE_AGENT_MODEL;
}

export function buildRoutineImagePreviewUrl(filePath) {
  const normalized = (filePath || "").trim();
  if (!normalized) {
    return "";
  }
  if (/^https?:\/\//i.test(normalized) || normalized.startsWith("data:")) {
    return normalized;
  }
  return `/api/local-image?path=${encodeURIComponent(normalized)}`;
}
