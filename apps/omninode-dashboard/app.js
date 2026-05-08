import {
  CHAT_MODES,
  CODING_LANGUAGES,
  CODING_MODES,
  CODEX_MODEL_CHOICES,
  DEFAULT_CODEX_MODEL,
  DEFAULT_MOBILE_PANES,
  DEFAULT_ROUTINE_AGENT_MODEL,
  DEFAULT_ROUTINE_AGENT_PROVIDER
} from "./modules/dashboard-constants.js";
import {
  buildRoutineImagePreviewUrl,
  buildRoutinePayloadFromForm,
  createRoutineFormState,
  getRoutineLocalTimezone,
  getViewportSnapshot,
  hydrateRoutineFormFromRoutine,
  normalizeRoutineNotifyPolicy,
  normalizeRoutineNotifyTelegram,
  normalizeRoutineScheduleSourceMode,
  normalizeRoutineWeekdays
} from "./modules/routine-utils.js";
import {
  createAuthMetaState,
  createCopilotLocalUsageState,
  createCopilotPremiumUsageState,
  createGeminiUsageState,
  createGuardAlertDispatchState,
  createSettingsState
} from "./modules/settings-state.js";
import { createDoctorState } from "./modules/doctor-state.js";
import { createPlansState } from "./modules/plans-state.js";
import { createContextState } from "./modules/context-state.js";
import { createRoutingPolicyState } from "./modules/routing-policy-state.js";
import { createTaskGraphState } from "./modules/task-graph-state.js";
import { createRefactorState } from "./modules/refactor-state.js";
import { createNotebooksState } from "./modules/notebooks-state.js";
import {
  createChatState,
  createConversationState
} from "./modules/chat-state.js";
import { createCodingState } from "./modules/coding-state.js";
import {
  createRoutineOutputPreviewState,
  createRoutineProgressState,
  createRoutineState
} from "./modules/routine-state.js";
import {
  cloneLogicGraph,
  createEmptyLogicGraph,
  createLogicEdge,
  createLogicNode,
  createLogicState,
  getLogicNodeMinimumSize,
  LOGIC_NODE_DEFAULT_SIZE,
  normalizeLogicNodeSize
} from "./modules/logic-state.js";
import {
  buildDashboardWsUrl,
  clearPersistedAuthSession,
  flushQueuedPayloads,
  getSavedAuthExpiry,
  getSavedAuthToken,
  isRemoteDashboardHost,
  persistAuthSession,
  sendWsPayload
} from "./modules/ws-client.js";
import {
  requestDoctorLast,
  requestDoctorRun
} from "./modules/ws-doctor.js";
import {
  requestCommandsList,
  requestContextScan,
  requestSkillsList,
  requestSkillGet,
  requestSkillSave,
  requestSkillDelete
} from "./modules/ws-context.js";
import {
  requestPlanApprove,
  requestPlanCreate,
  requestPlanGet,
  requestPlanList,
  requestPlanReview,
  requestPlanRun
} from "./modules/ws-plans.js";
import {
  requestRoutingDecisionGetLast,
  requestRoutingPolicyGet,
  requestRoutingPolicyReset,
  requestRoutingPolicySave
} from "./modules/ws-routing.js";
import {
  requestTaskCancel,
  requestTaskGraphCreate,
  requestTaskGraphGet,
  requestTaskGraphList,
  requestTaskGraphRun,
  requestTaskOutput
} from "./modules/ws-tasks.js";
import {
  requestLogicGraphCancel,
  requestLogicGraphDelete,
  requestLogicGraphGet,
  requestLogicGraphList,
  requestLogicPathList,
  requestLogicGraphRun,
  requestLogicGraphSave,
  requestLogicGraphRunGet
} from "./modules/ws-logic.js";
import {
  requestAstReplace,
  requestRefactorApply,
  requestLspRename,
  requestRefactorPreview,
  requestRefactorRead
} from "./modules/ws-refactor.js";
import {
  requestHandoffCreate,
  requestNotebookAppend,
  requestNotebookGet
} from "./modules/ws-notebooks.js";
import {
  handleConversationMemoryMessage,
  handleExecutionFlowMessage,
  handleRoutineMessage
} from "./modules/dashboard-message-handlers.js";
import { handleDashboardServerMessage } from "./modules/dashboard-server-message-router.mjs";
import {
  renderChatMultiResultPanel,
  renderCodingResultPanel
} from "./modules/dashboard-workspace-renderers.js";
import {
  renderMessagesPanel as renderMessagesPanelModule,
  renderThreadHeader as renderThreadHeaderModule,
  renderThreadInfoPanel as renderThreadInfoPanelModule,
  renderThreadModebar as renderThreadModebarModule
} from "./modules/dashboard-thread-renderers.js";
import {
  renderConversationPanel as renderConversationPanelModule,
  renderMemoryPicker as renderMemoryPickerModule
} from "./modules/dashboard-sidebar-renderers.js";
import { renderRoutineTab as renderRoutineTabModule } from "./modules/dashboard-routine-renderers.js";
import { renderLogicTab as renderLogicTabModule } from "./modules/dashboard-logic-renderers.js";
import { renderToolControlPanel as renderToolControlPanelModule } from "./modules/dashboard-ops-renderers.js";
import {
  renderAutomationRootPanel as renderAutomationRootPanelModule,
  renderNotebookRootPanel as renderNotebookRootPanelModule,
  renderSettingsPanel as renderSettingsPanelModule
} from "./modules/dashboard-settings-renderers.js";
import { renderSkillsRoot as renderSkillsRootModule } from "./modules/skills-renderers.js";
import { mapErrorMessage, formatErrorBanner } from "./modules/error-messages.js";
import {
  attachLatencyMetaToConversation,
  buildConversationAvatarText,
  buildTimeWindowKeys,
  formatDecimal,
  formatConversationUpdatedLabel,
  localUtcOffsetLabel,
  toneForCategory
} from "./modules/dashboard-formatters.js";
import {
  buildGuardAlertPipelineEvent,
  buildGuardAlertRuleResult,
  buildGuardObsEvent,
  buildGuardRetryTimelineEntry,
  buildGuardRetryTimelineSnapshot,
  buildProviderRuntimeEventsFromMessage,
  formatGuardAlertThreshold,
  GUARD_ALERT_PIPELINE_FIELD_ROWS,
  GUARD_ALERT_RULES,
  GUARD_OBS_CHANNEL_KEYS,
  GUARD_RETRY_TIMELINE_API_REFRESH_MS,
  GUARD_RETRY_TIMELINE_BUCKET_MINUTES,
  GUARD_RETRY_TIMELINE_CHANNELS,
  GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS,
  GUARD_RETRY_TIMELINE_MAX_ENTRIES,
  GUARD_RETRY_TIMELINE_WINDOW_MINUTES,
  inferToolResultAction,
  inferToolResultDomain,
  inferToolResultGroup,
  inferToolResultStatus,
  normalizeGuardNumber,
  normalizeGuardRetryTimelineSnapshot,
  OPS_DOMAIN_FILTERS,
  PROVIDER_RUNTIME_KEYS,
  severityRank,
  summarizeProviderRuntimeEntry,
  TOOL_DOMAIN_FILTERS,
  TOOL_RESULT_FILTERS,
  TOOL_RESULT_GROUPS,
  TOOL_RESULT_TYPES
} from "./modules/dashboard-observability.js";
import {
  buildChatMultiRenderSnapshot,
  buildCodingMultiRenderSnapshot,
  normalizeChatMultiResultMessage,
  parseChatMultiComparisonMessage,
  parseCodingMultiComparisonMessage
} from "./modules/dashboard-chat-multi.js";
import { createMarkdownSupport } from "./modules/dashboard-markdown.js";
import {
  autoResizeComposerTextarea,
  createResponsiveSectionTabsRenderer
} from "./modules/dashboard-ui-helpers.js";
import {
  buildNextAttachments,
  buildRichInputPayload,
  clearAttachmentDraft,
  hasDraggedFiles
} from "./modules/dashboard-attachments.js";
import {
  buildDashboardModelOptionSets,
  buildSettingsModelTableState
} from "./modules/dashboard-model-data.js";
import {
  buildGuardAlertSummary,
  buildGuardObsStats,
  buildGuardRetryTimelineRows,
  buildOpsDomainStats,
  buildOpsFlowItems,
  buildProviderHealthRows,
  buildProviderHealthSummary,
  buildProviderRuntimeRows,
  buildProviderRuntimeStats,
  buildToolDomainStats,
  buildToolResultStats,
  filterOpsFlowItems,
  filterToolResultItems
} from "./modules/dashboard-derived-state.js";
import {
  buildCodingResultRendererProps as buildCodingResultRendererPropsModule,
  buildSafeRefactorRendererProps as buildSafeRefactorRendererPropsModule,
  renderChatComposer as renderChatComposerShell,
  renderCodingComposer as renderCodingComposerShell,
  renderCodingResultDock as renderCodingResultDockShell,
  renderCodingResultOverlay as renderCodingResultOverlayShell,
  renderComposerInputBar as renderComposerInputBarShell,
  renderGlobalNav as renderGlobalNavModule,
  renderModeTabs as renderModeTabsModule,
  renderResponsiveWorkspaceSupportPane as renderResponsiveWorkspaceSupportPaneShell,
  renderSafeRefactorDock as renderSafeRefactorDockShell,
  renderSafeRefactorOverlay as renderSafeRefactorOverlayShell,
  renderSafeRefactorPanel as renderSafeRefactorPanelShell,
  renderThreadSupportStack as renderThreadSupportStackShell,
  renderWorkspace as renderWorkspaceShell
} from "./modules/dashboard-shell-renderers.js";

(function () {
  const { useEffect, useMemo, useRef, useState } = React;
  const e = React.createElement;
  const NONE_MODEL = "none";
  const DEFAULT_GROQ_SINGLE_MODEL = "meta-llama/llama-4-scout-17b-16e-instruct";
  const DEFAULT_GROQ_WORKER_MODEL = "openai/gpt-oss-120b";
  const DEFAULT_GEMINI_WORKER_MODEL = "gemini-3-flash-preview";
  const DEFAULT_NVIDIA_MODEL = "meta/llama-3.1-70b-instruct";
  const GEMINI_MODEL_CHOICES = [
    { id: "gemini-3-flash-preview", label: "Gemini 기본: gemini-3-flash-preview" },
    { id: "gemini-3.1-pro-preview", label: "Gemini: gemini-3.1-pro-preview" },
    { id: "gemini-3.1-flash-lite-preview", label: "Gemini: gemini-3.1-flash-lite-preview" }
  ];
  const DEFAULT_CEREBRAS_MODEL = "gpt-oss-120b";
  const CEREBRAS_MODEL_CHOICES = [
    { id: DEFAULT_CEREBRAS_MODEL, label: `Cerebras 기본: ${DEFAULT_CEREBRAS_MODEL}` },
    { id: "zai-glm-4.7", label: "Cerebras: zai-glm-4.7 (preview)" }
  ];
  const NVIDIA_MODEL_CHOICES = [
    { id: DEFAULT_NVIDIA_MODEL, label: `NVIDIA NIM 기본: ${DEFAULT_NVIDIA_MODEL}` },
    { id: "meta/llama-3.3-70b-instruct", label: "NVIDIA NIM: meta/llama-3.3-70b-instruct" },
    { id: "nvidia/llama-3.3-nemotron-super-49b-v1.5", label: "NVIDIA NIM: nvidia/llama-3.3-nemotron-super-49b-v1.5" },
    { id: "nvidia/nemotron-3-super-120b-a12b", label: "NVIDIA NIM: nvidia/nemotron-3-super-120b-a12b" },
    { id: "openai/gpt-oss-120b", label: "NVIDIA NIM: openai/gpt-oss-120b" },
    { id: "qwen/qwen3-coder-480b-a35b-instruct", label: "NVIDIA NIM: qwen/qwen3-coder-480b-a35b-instruct" }
  ];
  const UI_PREFERENCES_KEY = "omninode_ui_preferences_v1";
  const DEFAULT_UI_PREFERENCES = {
    theme: { mode: "system" },
    shortcuts: {
      palette: "mod+k",
      paletteAlt: "mod+p",
      send: "ctrl+enter",
      close: "escape",
      newChat: "mod+n",
      searchConversations: "mod+shift+f",
      focusComposer: "mod+/",
      toggleTheme: "mod+shift+l",
      toggleVoice: "mod+shift+v",
      tabChat: "mod+1",
      tabRoutine: "mod+2",
      tabLogic: "mod+3",
      tabCoding: "mod+4",
      tabNotebook: "mod+5",
      tabAutomation: "mod+6",
      tabSkills: "mod+7",
      tabSettings: "mod+8",
      toggleAutoSpeak: "mod+shift+a",
      stopSpeaking: "mod+shift+s"
    },
    speech: {
      lang: "ko-KR",
      autoSpeak: false,
      voiceURI: "",
      rate: 1.0,
      pitch: 1.0,
      volume: 1.0,
      stripMarkdown: true
    }
  };
  const TAB_SHORTCUTS = [
    ["chat", "대화"],
    ["routine", "루틴"],
    ["logic", "로직"],
    ["coding", "코딩"],
    ["notebook", "노트북"],
    ["automation", "작업 계획"],
    ["skills", "스킬"],
    ["settings", "설정"]
  ];
  const CONVERSATION_STATE_DEFAULTS = createConversationState();
  const CHAT_STATE_DEFAULTS = createChatState({
    noneModel: NONE_MODEL,
    defaultGroqSingleModel: DEFAULT_GROQ_SINGLE_MODEL,
    defaultGroqWorkerModel: DEFAULT_GROQ_WORKER_MODEL,
    defaultGeminiWorkerModel: DEFAULT_GEMINI_WORKER_MODEL,
    defaultCerebrasModel: DEFAULT_CEREBRAS_MODEL,
    defaultNvidiaModel: DEFAULT_NVIDIA_MODEL
  });
  const CODING_STATE_DEFAULTS = createCodingState({
    noneModel: NONE_MODEL,
    defaultGroqWorkerModel: DEFAULT_GROQ_WORKER_MODEL,
    defaultGeminiWorkerModel: DEFAULT_GEMINI_WORKER_MODEL,
    defaultCerebrasModel: DEFAULT_CEREBRAS_MODEL,
    defaultNvidiaModel: DEFAULT_NVIDIA_MODEL
  });
  const ROUTINE_STATE_DEFAULTS = createRoutineState({
    defaultMobilePanes: DEFAULT_MOBILE_PANES
  });
  const LOGIC_STATE_DEFAULTS = createLogicState();
  const LOGIC_VIEWPORT_DEFAULTS = { x: 96, y: 72, zoom: 1 };
  const LOGIC_CANVAS_MIN_ZOOM = 0.45;
  const LOGIC_CANVAS_MAX_ZOOM = 1.8;
  const LOGIC_CANVAS_STAGE_PADDING = 48;
  const LOGIC_FIELD_MODE_PREFIX = "__mode__";
  const LOGIC_FIELD_LITERAL_PREFIX = "__literal__";
  const LOGIC_FIELD_REFERENCE_PREFIX = "__reference__";
  function createEmptyLogicCanvasInteraction() {
    return {
      mode: "",
      pointerId: null,
      nodeId: "",
      nodeType: "",
      sourcePort: "main",
      handle: "",
      startClientX: 0,
      startClientY: 0,
      originX: 0,
      originY: 0,
      originWidth: LOGIC_NODE_DEFAULT_SIZE.width,
      originHeight: LOGIC_NODE_DEFAULT_SIZE.height
    };
  }
  function createEmptyLogicPathBrowserState() {
    return {
      open: false,
      loading: false,
      nodeId: "",
      fieldKey: "",
      fieldLabel: "",
      scope: "workspace",
      rootKey: "workspace",
      browsePath: "",
      displayPath: "",
      parentBrowsePath: null,
      directorySelectPath: null,
      roots: [],
      items: [],
      message: "",
      allowDirectorySelection: false
    };
  }
  function normalizeLogicPathBrowserValue(value) {
    return `${value || ""}`.replace(/\\/g, "/").trim();
  }
  function getLogicFieldModeKey(fieldKey) {
    return `${LOGIC_FIELD_MODE_PREFIX}${fieldKey}`;
  }
  function getLogicFieldLiteralKey(fieldKey) {
    return `${LOGIC_FIELD_LITERAL_PREFIX}${fieldKey}`;
  }
  function getLogicFieldReferenceKey(fieldKey) {
    return `${LOGIC_FIELD_REFERENCE_PREFIX}${fieldKey}`;
  }
  function normalizeLogicReferenceValue(value) {
    const raw = `${value || ""}`.trim();
    if (!raw) {
      return "";
    }
    if (raw.startsWith("{{") && raw.endsWith("}}")) {
      return raw;
    }
    return `{{${raw}}}`;
  }
  function isLogicReferenceValue(value) {
    const raw = `${value || ""}`.trim();
    if (!raw) {
      return false;
    }
    const normalized = raw.startsWith("{{") && raw.endsWith("}}")
      ? raw.slice(2, -2).trim()
      : raw;
    return normalized === "run.input"
      || normalized.startsWith("vars.")
      || normalized.startsWith("nodes.")
      || normalized.startsWith("sessions.")
      || normalized.startsWith("artifacts.");
  }
  function getLogicPathBrowserDirectory(value) {
    const normalized = normalizeLogicPathBrowserValue(value).replace(/^\/+/, "").replace(/\/+$/, "");
    if (!normalized) {
      return "";
    }
    if (normalizeLogicPathBrowserValue(value).endsWith("/")) {
      return normalized;
    }
    const index = normalized.lastIndexOf("/");
    return index < 0 ? "" : normalized.slice(0, index);
  }
  function resolveLogicPathBrowserRoot(scope, value) {
    const normalizedValue = normalizeLogicPathBrowserValue(value);
    if (scope === "memory") {
      if (normalizedValue.startsWith("memory-notes/")) {
        return "memory-notes";
      }
      if (normalizedValue.startsWith("conversations/")) {
        return "conversations";
      }
      return "project-markdown";
    }
    return "workspace";
  }
  function resolveLogicPathBrowserBrowsePath(scope, rootKey, value) {
    const normalizedValue = normalizeLogicPathBrowserValue(value);
    if (!normalizedValue) {
      return "";
    }
    if (scope === "memory" && (rootKey === "memory-notes" || rootKey === "conversations")) {
      return "";
    }
    return getLogicPathBrowserDirectory(normalizedValue);
  }
  const chatMultiUtils = {
    normalizeChatMultiResultMessage,
    parseChatMultiComparisonMessage,
    parseCodingMultiComparisonMessage,
    buildCodingMultiRenderSnapshot,
    buildChatMultiRenderSnapshot
  };
  const { MarkdownBubbleText } = createMarkdownSupport({
    React,
    window: typeof window !== "undefined" ? window : undefined
  });

  function mergeUiPreferences(value) {
    const source = value && typeof value === "object" ? value : {};
    return {
      theme: {
        ...DEFAULT_UI_PREFERENCES.theme,
        ...(source.theme && typeof source.theme === "object" ? source.theme : {})
      },
      shortcuts: {
        ...DEFAULT_UI_PREFERENCES.shortcuts,
        ...(source.shortcuts && typeof source.shortcuts === "object" ? source.shortcuts : {})
      },
      speech: {
        ...DEFAULT_UI_PREFERENCES.speech,
        ...(source.speech && typeof source.speech === "object" ? source.speech : {})
      }
    };
  }

  function loadUiPreferences() {
    if (typeof window === "undefined" || !window.localStorage) {
      return DEFAULT_UI_PREFERENCES;
    }

    try {
      const raw = window.localStorage.getItem(UI_PREFERENCES_KEY);
      return raw ? mergeUiPreferences(JSON.parse(raw)) : DEFAULT_UI_PREFERENCES;
    } catch {
      return DEFAULT_UI_PREFERENCES;
    }
  }

  function formatRelativeTime(ms) {
    const seconds = Math.max(0, Math.floor(ms / 1000));
    if (seconds < 5) return "방금";
    if (seconds < 60) return `${seconds}초 전`;
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}분 전`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}시간 전`;
    const days = Math.floor(hours / 24);
    return `${days}일 전`;
  }

  function persistUiPreferences(value) {
    if (typeof window === "undefined" || !window.localStorage) {
      return;
    }

    try {
      window.localStorage.setItem(UI_PREFERENCES_KEY, JSON.stringify(value));
    } catch {
    }
  }

  function normalizeShortcut(value) {
    return `${value || ""}`.trim().toLowerCase().replace(/\s+/g, "");
  }

  function isMacOs() {
    if (typeof navigator === "undefined") return false;
    if (navigator.userAgentData?.platform) {
      return /mac/i.test(navigator.userAgentData.platform);
    }
    return /Mac|iPhone|iPad|iPod/i.test(navigator.platform || "") || /Mac/i.test(navigator.userAgent || "");
  }

  const KEY_LABEL_MAP_BASE = {
    enter: "Enter",
    escape: "Esc",
    space: "Space",
    arrowup: "↑",
    arrowdown: "↓",
    arrowleft: "←",
    arrowright: "→",
    backspace: "⌫",
    delete: "Del",
    tab: "Tab",
    home: "Home",
    end: "End",
    pageup: "PgUp",
    pagedown: "PgDn",
    "/": "/",
    ".": ".",
    ",": ",",
    "-": "-",
    "=": "=",
    "[": "[",
    "]": "]",
    ";": ";",
    "'": "'",
    "`": "`"
  };

  function formatShortcutLabel(value) {
    const normalized = normalizeShortcut(value);
    if (!normalized) return "";
    const isMac = isMacOs();
    const parts = normalized.split("+").filter(Boolean);
    return parts.map((part) => {
      if (part === "mod") return isMac ? "⌘" : "Ctrl";
      if (part === "cmd" || part === "meta") return "⌘";
      if (part === "ctrl") return isMac ? "⌃" : "Ctrl";
      if (part === "alt") return isMac ? "⌥" : "Alt";
      if (part === "shift") return isMac ? "⇧" : "Shift";
      if (KEY_LABEL_MAP_BASE[part]) return KEY_LABEL_MAP_BASE[part];
      if (part.length === 1) return part.toUpperCase();
      return part.charAt(0).toUpperCase() + part.slice(1);
    }).join(isMac ? "" : "+");
  }

  function shortcutFromEvent(event) {
    const key = `${event.key || ""}`.toLowerCase();
    if (!key || key === "control" || key === "shift" || key === "alt" || key === "meta") {
      return "";
    }
    const parts = [];
    if (event.metaKey) parts.push(isMacOs() ? "mod" : "cmd");
    if (event.ctrlKey && !(isMacOs() && event.metaKey)) parts.push(isMacOs() ? "ctrl" : "mod");
    // dedupe (mod이 ctrl+meta 둘 다 커버하니, 한쪽만)
    const dedup = [];
    parts.forEach((p) => {
      if (!dedup.includes(p)) dedup.push(p);
    });
    if (event.altKey) dedup.push("alt");
    if (event.shiftKey) dedup.push("shift");
    let normalizedKey = key;
    if (key === " ") normalizedKey = "space";
    if (key === "esc") normalizedKey = "escape";
    dedup.push(normalizedKey);
    // mod와 cmd/ctrl 둘 다 들어간 경우 정리
    return dedup.join("+");
  }

  function clampNumber(value, lo, hi, fallback) {
    const num = Number(value);
    if (!Number.isFinite(num)) return fallback;
    return Math.max(lo, Math.min(hi, num));
  }

  function sanitizeForSpeech(text, opts) {
    const { stripMarkdown = true } = opts || {};
    let cleaned = `${text || ""}`;
    if (!cleaned) return "";
    if (stripMarkdown) {
      // 코드 블록은 통째로 건너뛴다 (안 읽음)
      cleaned = cleaned.replace(/```[\s\S]*?```/g, " ");
      // 인라인 코드: 백틱만 제거하고 내용만 읽음
      cleaned = cleaned.replace(/`([^`]+)`/g, "$1");
      // 헤더/리스트/인용 마커 제거 (내용은 그대로)
      cleaned = cleaned.replace(/^\s{0,3}#+\s+/gm, "");
      cleaned = cleaned.replace(/^\s*[-*+]\s+/gm, "");
      cleaned = cleaned.replace(/^\s*>\s+/gm, "");
      // 강조/취소선 기호 제거
      cleaned = cleaned.replace(/\*\*([^*]+)\*\*/g, "$1");
      cleaned = cleaned.replace(/\*([^*]+)\*/g, "$1");
      cleaned = cleaned.replace(/__([^_]+)__/g, "$1");
      cleaned = cleaned.replace(/_([^_]+)_/g, "$1");
      cleaned = cleaned.replace(/~~([^~]+)~~/g, "$1");
      // 링크 [text](url) → text 만 발음
      cleaned = cleaned.replace(/\[([^\]]+)\]\([^)]+\)/g, "$1");
      // 이미지·단독 URL 은 건너뛰기
      cleaned = cleaned.replace(/!\[[^\]]*\]\([^)]+\)/g, " ");
      cleaned = cleaned.replace(/https?:\/\/\S+/g, " ");
    }
    cleaned = cleaned.replace(/[ \t]+/g, " ").replace(/\n{3,}/g, "\n\n").trim();
    return cleaned;
  }

  function listSpeechVoices() {
    if (typeof window === "undefined" || !window.speechSynthesis) return [];
    try {
      return window.speechSynthesis.getVoices() || [];
    } catch {
      return [];
    }
  }

  function speakText(text, prefs) {
    if (typeof window === "undefined" || !window.speechSynthesis) return false;
    const cleaned = sanitizeForSpeech(text, {
      stripMarkdown: prefs?.stripMarkdown !== false
    });
    if (!cleaned) return false;
    try {
      window.speechSynthesis.cancel();
      const utter = new SpeechSynthesisUtterance(cleaned);
      utter.lang = prefs?.lang || "ko-KR";
      utter.rate = clampNumber(prefs?.rate, 0.5, 2, 1);
      utter.pitch = clampNumber(prefs?.pitch, 0.5, 2, 1);
      utter.volume = clampNumber(prefs?.volume, 0, 1, 1);
      if (prefs?.voiceURI) {
        const match = listSpeechVoices().find((v) => v.voiceURI === prefs.voiceURI);
        if (match) {
          utter.voice = match;
          if (match.lang) utter.lang = match.lang;
        }
      }
      window.speechSynthesis.speak(utter);
      return true;
    } catch {
      return false;
    }
  }

  function stopSpeaking() {
    if (typeof window !== "undefined" && window.speechSynthesis) {
      try { window.speechSynthesis.cancel(); } catch {}
    }
  }

  function isSpeechSynthesisSupported() {
    return typeof window !== "undefined" && !!window.speechSynthesis && typeof window.SpeechSynthesisUtterance !== "undefined";
  }

  function isValidShortcut(value) {
    const normalized = normalizeShortcut(value);
    if (!normalized) return false;
    const parts = normalized.split("+").filter(Boolean);
    if (parts.length === 0) return false;
    const last = parts[parts.length - 1];
    const modifiers = ["mod", "ctrl", "cmd", "meta", "alt", "shift"];
    // 단일 modifier-only는 무효
    if (parts.length === 1 && modifiers.includes(last)) return false;
    // escape 같은 단일 키는 허용
    return true;
  }

  function shortcutMatches(event, shortcut) {
    const normalized = normalizeShortcut(shortcut);
    if (!normalized) {
      return false;
    }

    const parts = normalized.split("+").filter(Boolean);
    const key = parts[parts.length - 1] || "";
    const actualKey = `${event.key || ""}`.toLowerCase();
    if (key && key !== actualKey) {
      return false;
    }

    const wantsMod = parts.includes("mod");
    const wantsCtrl = parts.includes("ctrl");
    const wantsMeta = parts.includes("cmd") || parts.includes("meta");
    const wantsAlt = parts.includes("alt");
    const wantsShift = parts.includes("shift");
    const allowsCtrl = wantsCtrl || wantsMod;
    const allowsMeta = wantsMeta || wantsMod;
    if (wantsMod && !(event.ctrlKey || event.metaKey)) {
      return false;
    }
    if (wantsCtrl && !event.ctrlKey) {
      return false;
    }
    if (wantsMeta && !event.metaKey) {
      return false;
    }
    if (!allowsCtrl && event.ctrlKey) {
      return false;
    }
    if (!allowsMeta && event.metaKey) {
      return false;
    }
    if (wantsAlt !== !!event.altKey) {
      return false;
    }
    if (wantsShift !== !!event.shiftKey) {
      return false;
    }

    return true;
  }

  function buildRequestId(prefix) {
    return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  }

  function normalizePaletteSkills(skills) {
    return (Array.isArray(skills) ? skills : [])
      .map((skill) => {
        const name = `${skill.name || skill.Name || ""}`.trim();
        const scope = `${skill.scope || skill.Scope || "project"}`.trim().toLowerCase() === "global" ? "global" : "project";
        return {
          name,
          scope,
          key: `${scope}:${name}`,
          description: `${skill.description || skill.Description || ""}`.trim(),
          path: `${skill.path || skill.Path || ""}`.trim()
        };
      })
      .filter((skill) => skill.name)
      .sort((a, b) => {
        if (a.scope !== b.scope) {
          return a.scope === "project" ? -1 : 1;
        }
        return a.name.localeCompare(b.name, "ko");
      });
  }

  function App() {
    const [rootTab, setRootTab] = useState("chat");
    const [chatMode, setChatMode] = useState("single");
    const [codingMode, setCodingMode] = useState("single");
    const [uiPreferences, setUiPreferences] = useState(() => loadUiPreferences());
    const [uiPreferencesSavedAt, setUiPreferencesSavedAt] = useState(0);
    const [thinkPlusModeByKey, setThinkPlusModeByKey] = useState(() => {
      try {
        const raw = typeof window !== "undefined" && window.localStorage
          ? window.localStorage.getItem("omninode_think_plus_v1")
          : null;
        return raw ? JSON.parse(raw) : {};
      } catch {
        return {};
      }
    });
    const [recordingShortcutKey, setRecordingShortcutKey] = useState(null);
    const uiPreferencesSessionStartRef = useRef(null);
    const [commandPaletteOpen, setCommandPaletteOpen] = useState(false);
    const [commandPaletteQuery, setCommandPaletteQuery] = useState("");
    const [selectedChatSkill, setSelectedChatSkill] = useState(null);
    const [conversationSearchByKey, setConversationSearchByKey] = useState({});
    const [backupState, setBackupState] = useState({
      status: "",
      exportBusy: false,
      importBusy: false,
      preview: null
    });
    const [speechState, setSpeechState] = useState({
      active: false,
      supported: typeof window !== "undefined" && !!(window.SpeechRecognition || window.webkitSpeechRecognition),
      error: ""
    });
    const speechRecognitionRef = useRef(null);
    const speechTranscriptRef = useRef("");
    const speechBaseDraftRef = useRef("");
    const speechTargetKeyRef = useRef("");
    const speechStopRequestedRef = useRef(false);
    const sendCurrentComposerRef = useRef(null);
    const lastSpokenMessageRef = useRef("");
    const [availableTtsVoices, setAvailableTtsVoices] = useState(() => listSpeechVoices());
    const [speakingMessageId, setSpeakingMessageId] = useState("");
    const [shortcutGroupTab, setShortcutGroupTab] = useState("palette");

    const [status, setStatus] = useState("연결 대기");
    const [authed, setAuthed] = useState(false);
    const authedRef = useRef(false);
    const [authExpiry, setAuthExpiry] = useState("");
    const [authLocalOffset, setAuthLocalOffset] = useState(localUtcOffsetLabel());
    const [authTtlHours, setAuthTtlHours] = useState("24");
    const [otp, setOtp] = useState("");
    const [authMeta, setAuthMeta] = useState(() => createAuthMetaState());
    const authMetaRef = useRef(null);

    const [settingsState, setSettingsState] = useState(() => createSettingsState());
    const settingsStateRef = useRef(null);

    const [telegramBotToken, setTelegramBotToken] = useState("");
    const [telegramChatId, setTelegramChatId] = useState("");
    const [groqApiKey, setGroqApiKey] = useState("");
    const [geminiApiKey, setGeminiApiKey] = useState("");
    const [cerebrasApiKey, setCerebrasApiKey] = useState("");
    const [nvidiaApiKey, setNvidiaApiKey] = useState("");
    const [codexApiKey, setCodexApiKey] = useState("");
    const [persist, setPersist] = useState(true);

    const [copilotStatus, setCopilotStatus] = useState("확인 전");
    const [copilotDetail, setCopilotDetail] = useState("-");
    const [codexStatus, setCodexStatus] = useState("확인 전");
    const [codexDetail, setCodexDetail] = useState("-");
    const [groqModels, setGroqModels] = useState([]);
    const [cerebrasModels, setCerebrasModels] = useState([]);
    const [copilotModels, setCopilotModels] = useState([]);
    const [selectedGroqModel, setSelectedGroqModel] = useState("");
    const [selectedCopilotModel, setSelectedCopilotModel] = useState("");

    const [geminiUsage, setGeminiUsage] = useState(() => createGeminiUsageState());
    const [copilotPremiumUsage, setCopilotPremiumUsage] = useState(() => createCopilotPremiumUsageState());
    const [copilotLocalUsage, setCopilotLocalUsage] = useState(() => createCopilotLocalUsageState());
    const [doctorState, setDoctorState] = useState(() => createDoctorState());
    const [plansState, setPlansState] = useState(() => createPlansState());
    const [contextState, setContextState] = useState(() => createContextState());
    const [selectedSkillKey, setSelectedSkillKey] = useState("");
    const [skillEditor, setSkillEditor] = useState(null);
    const [skillStatus, setSkillStatus] = useState(null);
    const [loadingSkillKey, setLoadingSkillKey] = useState("");
    const [skillSearch, setSkillSearch] = useState("");
    const [routingPolicyState, setRoutingPolicyState] = useState(() => createRoutingPolicyState());
    const [taskGraphState, setTaskGraphState] = useState(() => createTaskGraphState());
    const [refactorState, setRefactorState] = useState(() => createRefactorState());
    const [codingResultOverlayOpen, setCodingResultOverlayOpen] = useState(false);
    const [safeRefactorOverlayOpen, setSafeRefactorOverlayOpen] = useState(false);
    const [notebooksState, setNotebooksState] = useState(() => createNotebooksState());

    const [conversationLists, setConversationLists] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.conversationLists }));
    const [activeConversationByKey, setActiveConversationByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.activeConversationByKey }));
    const [conversationDetails, setConversationDetails] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.conversationDetails }));
    const [expandedFoldersByKey, setExpandedFoldersByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.expandedFoldersByKey }));
    const [conversationFilterByKey, setConversationFilterByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.conversationFilterByKey }));
    const [selectionModeByKey, setSelectionModeByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.selectionModeByKey }));
    const [selectedConversationIdsByKey, setSelectedConversationIdsByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.selectedConversationIdsByKey }));
    const [selectedFoldersByKey, setSelectedFoldersByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.selectedFoldersByKey }));
    const [memoryNotes, setMemoryNotes] = useState(() => [...CONVERSATION_STATE_DEFAULTS.memoryNotes]);
    const [selectedMemoryByConversation, setSelectedMemoryByConversation] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.selectedMemoryByConversation }));
    const [metaTitle, setMetaTitle] = useState(CONVERSATION_STATE_DEFAULTS.metaTitle);
    const [metaProject, setMetaProject] = useState(CONVERSATION_STATE_DEFAULTS.metaProject);
    const [metaCategory, setMetaCategory] = useState(CONVERSATION_STATE_DEFAULTS.metaCategory);
    const [metaTags, setMetaTags] = useState(CONVERSATION_STATE_DEFAULTS.metaTags);
    const [codingResultByConversation, setCodingResultByConversation] = useState(() => ({ ...CODING_STATE_DEFAULTS.resultByConversation }));
    const [memoryPreview, setMemoryPreview] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.memoryPreview }));
    const [routineOutputPreview, setRoutineOutputPreview] = useState(() => createRoutineOutputPreviewState());
    const [memoryPickerOpen, setMemoryPickerOpen] = useState(CONVERSATION_STATE_DEFAULTS.memoryPickerOpen);
    const [threadInfoOpenByScope, setThreadInfoOpenByScope] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.threadInfoOpenByScope }));

    const [pendingByKey, setPendingByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.pendingByKey }));
    const [errorByKey, setErrorByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.errorByKey }));
    const [optimisticUserByKey, setOptimisticUserByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.optimisticUserByKey }));
    const [codingProgressByKey, setCodingProgressByKey] = useState(() => ({ ...CODING_STATE_DEFAULTS.progressByKey }));
    const [filePreviewByConversation, setFilePreviewByConversation] = useState(() => ({ ...CODING_STATE_DEFAULTS.filePreviewByConversation }));
    const [codingRuntimeByConversation, setCodingRuntimeByConversation] = useState(() => ({ ...CODING_STATE_DEFAULTS.runtimeByConversation }));
    const [codingExecutionInputByConversation, setCodingExecutionInputByConversation] = useState(() => ({ ...CODING_STATE_DEFAULTS.executionInputByConversation }));
    const [showExecutionLogsByConversation, setShowExecutionLogsByConversation] = useState(() => ({ ...CODING_STATE_DEFAULTS.showExecutionLogsByConversation }));
    const [attachmentsByKey, setAttachmentsByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.attachmentsByKey }));
    const [attachmentPanelOpenByKey, setAttachmentPanelOpenByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.attachmentPanelOpenByKey }));
    const [attachmentDragActiveByKey, setAttachmentDragActiveByKey] = useState(() => ({ ...CONVERSATION_STATE_DEFAULTS.attachmentDragActiveByKey }));
    const [clockTick, setClockTick] = useState(Date.now());

    const [chatInputSingle, setChatInputSingle] = useState(CHAT_STATE_DEFAULTS.inputSingle);
    const [chatInputOrch, setChatInputOrch] = useState(CHAT_STATE_DEFAULTS.inputOrch);
    const [chatInputMulti, setChatInputMulti] = useState(CHAT_STATE_DEFAULTS.inputMulti);

    const [chatSingleProvider, setChatSingleProvider] = useState(CHAT_STATE_DEFAULTS.singleProvider);
    const [chatSingleModel, setChatSingleModel] = useState(CHAT_STATE_DEFAULTS.singleModel);
    const [chatOrchProvider, setChatOrchProvider] = useState(CHAT_STATE_DEFAULTS.orchProvider);
    const [chatOrchModel, setChatOrchModel] = useState(CHAT_STATE_DEFAULTS.orchModel);
    const [chatOrchGroqModel, setChatOrchGroqModel] = useState(CHAT_STATE_DEFAULTS.orchGroqModel);
    const [chatOrchGeminiModel, setChatOrchGeminiModel] = useState(CHAT_STATE_DEFAULTS.orchGeminiModel);
    const [chatOrchCerebrasModel, setChatOrchCerebrasModel] = useState(CHAT_STATE_DEFAULTS.orchCerebrasModel);
    const [chatOrchNvidiaModel, setChatOrchNvidiaModel] = useState(CHAT_STATE_DEFAULTS.orchNvidiaModel);
    const [chatOrchCopilotModel, setChatOrchCopilotModel] = useState(CHAT_STATE_DEFAULTS.orchCopilotModel);
    const [chatOrchCodexModel, setChatOrchCodexModel] = useState(CHAT_STATE_DEFAULTS.orchCodexModel);
    const [chatMultiGroqModel, setChatMultiGroqModel] = useState(CHAT_STATE_DEFAULTS.multiGroqModel);
    const [chatMultiGeminiModel, setChatMultiGeminiModel] = useState(CHAT_STATE_DEFAULTS.multiGeminiModel);
    const [chatMultiCerebrasModel, setChatMultiCerebrasModel] = useState(CHAT_STATE_DEFAULTS.multiCerebrasModel);
    const [chatMultiNvidiaModel, setChatMultiNvidiaModel] = useState(CHAT_STATE_DEFAULTS.multiNvidiaModel);
    const [chatMultiCopilotModel, setChatMultiCopilotModel] = useState(CHAT_STATE_DEFAULTS.multiCopilotModel);
    const [chatMultiCodexModel, setChatMultiCodexModel] = useState(CHAT_STATE_DEFAULTS.multiCodexModel);
    const [chatMultiSummaryProvider, setChatMultiSummaryProvider] = useState(CHAT_STATE_DEFAULTS.multiSummaryProvider);
    const [chatMultiResultByConversation, setChatMultiResultByConversation] = useState(() => ({ ...CHAT_STATE_DEFAULTS.multiResultByConversation }));

    const [codingInputSingle, setCodingInputSingle] = useState(CODING_STATE_DEFAULTS.inputSingle);
    const [codingInputOrch, setCodingInputOrch] = useState(CODING_STATE_DEFAULTS.inputOrch);
    const [codingInputMulti, setCodingInputMulti] = useState(CODING_STATE_DEFAULTS.inputMulti);

    const [codingSingleProvider, setCodingSingleProvider] = useState(CODING_STATE_DEFAULTS.singleProvider);
    const [codingSingleModel, setCodingSingleModel] = useState(CODING_STATE_DEFAULTS.singleModel);
    const [codingSingleLanguage, setCodingSingleLanguage] = useState(CODING_STATE_DEFAULTS.singleLanguage);

    const [codingOrchProvider, setCodingOrchProvider] = useState(CODING_STATE_DEFAULTS.orchProvider);
    const [codingOrchModel, setCodingOrchModel] = useState(CODING_STATE_DEFAULTS.orchModel);
    const [codingOrchLanguage, setCodingOrchLanguage] = useState(CODING_STATE_DEFAULTS.orchLanguage);
    const [codingOrchGroqModel, setCodingOrchGroqModel] = useState(CODING_STATE_DEFAULTS.orchGroqModel);
    const [codingOrchGeminiModel, setCodingOrchGeminiModel] = useState(CODING_STATE_DEFAULTS.orchGeminiModel);
    const [codingOrchCerebrasModel, setCodingOrchCerebrasModel] = useState(CODING_STATE_DEFAULTS.orchCerebrasModel);
    const [codingOrchNvidiaModel, setCodingOrchNvidiaModel] = useState(CODING_STATE_DEFAULTS.orchNvidiaModel);
    const [codingOrchCopilotModel, setCodingOrchCopilotModel] = useState(CODING_STATE_DEFAULTS.orchCopilotModel);
    const [codingOrchCodexModel, setCodingOrchCodexModel] = useState(CODING_STATE_DEFAULTS.orchCodexModel);

    const [codingMultiProvider, setCodingMultiProvider] = useState(CODING_STATE_DEFAULTS.multiProvider);
    const [codingMultiModel, setCodingMultiModel] = useState(CODING_STATE_DEFAULTS.multiModel);
    const [codingMultiLanguage, setCodingMultiLanguage] = useState(CODING_STATE_DEFAULTS.multiLanguage);
    const [codingMultiGroqModel, setCodingMultiGroqModel] = useState(CODING_STATE_DEFAULTS.multiGroqModel);
    const [codingMultiGeminiModel, setCodingMultiGeminiModel] = useState(CODING_STATE_DEFAULTS.multiGeminiModel);
    const [codingMultiCerebrasModel, setCodingMultiCerebrasModel] = useState(CODING_STATE_DEFAULTS.multiCerebrasModel);
    const [codingMultiNvidiaModel, setCodingMultiNvidiaModel] = useState(CODING_STATE_DEFAULTS.multiNvidiaModel);
    const [codingMultiCopilotModel, setCodingMultiCopilotModel] = useState(CODING_STATE_DEFAULTS.multiCopilotModel);
    const [codingMultiCodexModel, setCodingMultiCodexModel] = useState(CODING_STATE_DEFAULTS.multiCodexModel);

    const [command, setCommand] = useState("/metrics");
    const [metrics, setMetrics] = useState("메트릭 대기 중");
    const [logs, setLogs] = useState("");
    const [toolSessionKey, setToolSessionKey] = useState("");
    const [toolSpawnTask, setToolSpawnTask] = useState("상태 확인용 하위 세션 생성");
    const [toolSessionMessage, setToolSessionMessage] = useState("현재 상태를 간단히 요약해줘");
    const [toolCronJobId, setToolCronJobId] = useState("");
    const [toolBrowserUrl, setToolBrowserUrl] = useState("https://example.com");
    const [toolCanvasTarget, setToolCanvasTarget] = useState("main");
    const [toolNodesNode, setToolNodesNode] = useState("");
    const [toolNodesRequestId, setToolNodesRequestId] = useState("");
    const [toolNodesInvokeCommand, setToolNodesInvokeCommand] = useState("status");
    const [toolNodesInvokeParamsJson, setToolNodesInvokeParamsJson] = useState("{}");
    const [toolWebSearchQuery, setToolWebSearchQuery] = useState("오늘 미국 기준 주요 AI 뉴스");
    const [toolWebFetchUrl, setToolWebFetchUrl] = useState("https://example.com");
    const [toolMemorySearchQuery, setToolMemorySearchQuery] = useState("Omni-node 운영");
    const [toolMemoryGetPath, setToolMemoryGetPath] = useState("MEMORY.md");
    const [toolTelegramStubText, setToolTelegramStubText] = useState("/llm status");
    const [toolControlError, setToolControlError] = useState("");
    const [toolResultPreview, setToolResultPreview] = useState("결과 대기 중");
    const [toolResultItems, setToolResultItems] = useState([]);
    const [providerRuntimeItems, setProviderRuntimeItems] = useState([]);
    const [guardObsItems, setGuardObsItems] = useState([]);
    const [guardRetryTimelineItems, setGuardRetryTimelineItems] = useState([]);
    const [guardRetryTimelineApiSnapshot, setGuardRetryTimelineApiSnapshot] = useState(null);
    const [guardRetryTimelineApiFetchedAt, setGuardRetryTimelineApiFetchedAt] = useState("");
    const [guardRetryTimelineApiError, setGuardRetryTimelineApiError] = useState("");
    const [guardAlertDispatchState, setGuardAlertDispatchState] = useState(() => createGuardAlertDispatchState());
    const [toolResultFilter, setToolResultFilter] = useState("all");
    const [toolDomainFilter, setToolDomainFilter] = useState("all");
    const [opsDomainFilter, setOpsDomainFilter] = useState("all");
    const [selectedToolResultId, setSelectedToolResultId] = useState("");
    const [routines, setRoutines] = useState(() => [...ROUTINE_STATE_DEFAULTS.routines]);
    const [routineCreateForm, setRoutineCreateForm] = useState(() => createRoutineFormState());
    const [routineEditForm, setRoutineEditForm] = useState(() => createRoutineFormState());
    const [routineSelectedId, setRoutineSelectedId] = useState(ROUTINE_STATE_DEFAULTS.routineSelectedId);
    const [routineProgress, setRoutineProgress] = useState(() => createRoutineProgressState(ROUTINE_STATE_DEFAULTS.progress));
    const [logicGraphs, setLogicGraphs] = useState(() => [...LOGIC_STATE_DEFAULTS.graphs]);
    const [logicSelectedGraphId, setLogicSelectedGraphId] = useState(LOGIC_STATE_DEFAULTS.selectedGraphId);
    const [logicDraftGraph, setLogicDraftGraph] = useState(() => cloneLogicGraph(LOGIC_STATE_DEFAULTS.draftGraph));
    const [logicSelectedNodeId, setLogicSelectedNodeId] = useState(LOGIC_STATE_DEFAULTS.selectedNodeId);
    const [logicSelectedEdgeId, setLogicSelectedEdgeId] = useState(LOGIC_STATE_DEFAULTS.selectedEdgeId);
    const [logicPendingSourceNodeId, setLogicPendingSourceNodeId] = useState(LOGIC_STATE_DEFAULTS.pendingSourceNodeId);
    const [logicActiveRunId, setLogicActiveRunId] = useState(LOGIC_STATE_DEFAULTS.activeRunId);
    const [logicRunSnapshot, setLogicRunSnapshot] = useState(LOGIC_STATE_DEFAULTS.runSnapshot);
    const [logicRunEvents, setLogicRunEvents] = useState(() => [...LOGIC_STATE_DEFAULTS.runEvents]);
    const [logicJsonBuffer, setLogicJsonBuffer] = useState(LOGIC_STATE_DEFAULTS.jsonBuffer);
    const [logicDirty, setLogicDirty] = useState(LOGIC_STATE_DEFAULTS.dirty);
    const [logicLastMessage, setLogicLastMessage] = useState(LOGIC_STATE_DEFAULTS.lastMessage);
    const [logicPaletteGroup, setLogicPaletteGroup] = useState("flow");
    const [logicCanvasGesture, setLogicCanvasGesture] = useState("idle");
    const [logicConnectionPreview, setLogicConnectionPreview] = useState(null);
    const [logicMeasuredNodeMinimumById, setLogicMeasuredNodeMinimumById] = useState(() => ({}));
    const [logicPathBrowser, setLogicPathBrowser] = useState(() => createEmptyLogicPathBrowserState());
    const [logicRunOutputExpanded, setLogicRunOutputExpanded] = useState(false);
    const [logicRunPreview, setLogicRunPreview] = useState(() => ({
      open: false,
      title: "",
      content: ""
    }));
    const [groqUsageWindowBaseByModel, setGroqUsageWindowBaseByModel] = useState(() => ({ ...ROUTINE_STATE_DEFAULTS.groqUsageWindowBaseByModel }));
    const [viewportSize, setViewportSize] = useState(() => getViewportSnapshot());
    const [mainShellViewportTop, setMainShellViewportTop] = useState(0);
    const [mobilePaneByTab, setMobilePaneByTab] = useState(() => ({ ...ROUTINE_STATE_DEFAULTS.mobilePaneByTab }));
    const [mobileComposerOpenByTab, setMobileComposerOpenByTab] = useState(() => ({ chat: false, coding: false }));

    const wsRef = useRef(null);
    const workerRef = useRef(null);
    const reconnectTimerRef = useRef(null);
    const unmountedRef = useRef(false);
    const messageListRef = useRef(null);
    const outboundQueueRef = useRef([]);
    const hasOpenedSocketRef = useRef(false);
    const autoCreateConversationRef = useRef({});
    const attachmentDragDepthRef = useRef(0);
    const currentKeyRef = useRef("");
    const groqAutoRefreshWindowRef = useRef({ minute: "", hour: "", day: "" });
    const routineBrowserAgentPreviewRef = useRef("");
    const mainShellRef = useRef(null);
    const logicCanvasShellRef = useRef(null);
    const logicListPaneRef = useRef(null);
    const logicPalettePaneRef = useRef(null);
    const logicGraphListRef = useRef(null);
    const logicPaletteListRef = useRef(null);
    const logicCanvasInteractionRef = useRef(createEmptyLogicCanvasInteraction());
    const logicScrollDragRef = useRef({
      active: false,
      element: null,
      startClientY: 0,
      startScrollTop: 0,
      moved: false,
      suppressClick: false,
      suppressElement: null
    });
    const logicSelectedGraphIdRef = useRef(logicSelectedGraphId);
    const logicDraftGraphRef = useRef(logicDraftGraph);
    const logicAutoFrameKeyRef = useRef("");
    const logicMeasuredNodeMinimumRef = useRef(logicMeasuredNodeMinimumById);

    const scope = rootTab === "coding" ? "coding" : "chat";
    const mode = rootTab === "coding" ? codingMode : chatMode;
    const currentKey = `${scope}:${mode}`;
    const currentConversationList = conversationLists[currentKey] || [];
    const currentConversationId = activeConversationByKey[currentKey] || "";
    const currentConversation = currentConversationId ? conversationDetails[currentConversationId] : null;
    const currentMessages = currentConversation?.messages || [];
    const currentConversationTitle = currentConversation?.title || "대화를 선택하세요";
    const currentMemoryNotes = currentConversationId
      ? (selectedMemoryByConversation[currentConversationId] || currentConversation?.linkedMemoryNotes || [])
      : [];
    const currentCheckedMemoryNotes = memoryNotes
      .filter((note) => currentMemoryNotes.includes(note.name))
      .map((note) => note.name);
    const currentAttachments = attachmentsByKey[currentKey] || [];
    const attachmentPanelOpen = !!attachmentPanelOpenByKey[currentKey];
    const attachmentDragActive = !!attachmentDragActiveByKey[currentKey];
    const attachmentPanelVisible = attachmentPanelOpen || attachmentDragActive;
    const currentConversationFilter = conversationFilterByKey[currentKey] || "";
    const currentConversationSearchState = conversationSearchByKey[currentKey] || {
      query: "",
      loading: false,
      results: [],
      error: ""
    };
    const selectionMode = !!selectionModeByKey[currentKey];
    const currentSelectedConversationIds = Array.isArray(selectedConversationIdsByKey[currentKey])
      ? selectedConversationIdsByKey[currentKey]
      : [];
    const currentSelectedFolders = Array.isArray(selectedFoldersByKey[currentKey])
      ? selectedFoldersByKey[currentKey]
      : [];
    const attachmentFileInputId = `attachment-file-input-${currentKey.replace(/[^a-z0-9_-]/gi, "-")}`;
    const threadInfoScopeKey = rootTab === "coding" ? "coding" : "chat";
    const threadInfoOpen = !!threadInfoOpenByScope[threadInfoScopeKey];
    const responsiveWorkspaceKey = rootTab === "coding" ? "coding" : "chat";
    const isPortraitMobileLayout = viewportSize.width <= 920 && viewportSize.height > viewportSize.width;
    const mobileWorkspaceHeight = isPortraitMobileLayout
      ? Math.max(360, viewportSize.height - Math.max(0, mainShellViewportTop))
      : 0;
    const currentWorkspacePane = mobilePaneByTab[responsiveWorkspaceKey]
      || (currentConversationId ? "thread" : "list");
    const mobileComposerOpen = !!mobileComposerOpenByTab[responsiveWorkspaceKey];
    const currentRoutinePane = mobilePaneByTab.routine || (routineSelectedId ? "detail" : "overview");
    const currentLogicPane = mobilePaneByTab.logic || (isPortraitMobileLayout ? "canvas" : "selection");
    const currentSettingsPane = mobilePaneByTab.settings || "auth";
    const currentAutomationPane = mobilePaneByTab.automation || "plans";

    useEffect(() => {
      logicSelectedGraphIdRef.current = logicSelectedGraphId;
    }, [logicSelectedGraphId]);

    useEffect(() => {
      logicDraftGraphRef.current = logicDraftGraph;
    }, [logicDraftGraph]);

    useEffect(() => {
      logicMeasuredNodeMinimumRef.current = logicMeasuredNodeMinimumById;
    }, [logicMeasuredNodeMinimumById]);

    const groupedConversationList = useMemo(() => {
      const keyword = currentConversationFilter.trim().toLowerCase();
      const fullTextHits = Array.isArray(currentConversationSearchState.results)
        ? currentConversationSearchState.results
        : [];
      const fullTextById = new Map(fullTextHits.map((item) => [item.conversationId, item]));
      const groups = {};
      currentConversationList.forEach((item) => {
        const detail = conversationDetails[item.id] || null;
        const fullTextHit = fullTextById.get(item.id);
        const merged = {
          ...item,
          project: detail?.project ?? item.project,
          category: detail?.category ?? item.category,
          tags: Array.isArray(detail?.tags) ? detail.tags : item.tags,
          preview: fullTextHit?.snippet || detail?.preview || item.preview
        };
        const project = (merged.project || "기본").trim() || "기본";
        const category = (merged.category || "일반").trim() || "일반";
        const tags = Array.isArray(merged.tags) ? merged.tags.filter(Boolean).map((x) => `${x}`.trim()).filter(Boolean) : [];
        const title = `${merged.title || ""}`.toLowerCase();
        const preview = `${merged.preview || ""}`.toLowerCase();
        const searchable = [project.toLowerCase(), category.toLowerCase(), ...tags.map((x) => x.toLowerCase()), title, preview];
        if (keyword && !fullTextHit && !searchable.some((text) => text.includes(keyword))) {
          return;
        }

        if (!groups[project]) {
          groups[project] = [];
        }
        groups[project].push(merged);
      });

      return Object.keys(groups)
        .sort((a, b) => a.localeCompare(b, "ko"))
        .map((project) => ({
          project,
          items: groups[project].slice().sort((a, b) => (b.updatedUtc || "").localeCompare(a.updatedUtc || ""))
        }));
    }, [conversationDetails, currentConversationFilter, currentConversationList, currentConversationSearchState.results]);

    useEffect(() => {
      authMetaRef.current = authMeta;
    }, [authMeta]);

    useEffect(() => {
      settingsStateRef.current = settingsState;
    }, [settingsState]);

    useEffect(() => {
      authedRef.current = authed;
    }, [authed]);

    useEffect(() => {
      if (uiPreferencesSessionStartRef.current === null) {
        uiPreferencesSessionStartRef.current = uiPreferences;
      }
      persistUiPreferences(uiPreferences);
      setUiPreferencesSavedAt(Date.now());
    }, [uiPreferences]);

    useEffect(() => {
      if (typeof window === "undefined" || !window.localStorage) return;
      try {
        window.localStorage.setItem("omninode_think_plus_v1", JSON.stringify(thinkPlusModeByKey));
      } catch {}
    }, [thinkPlusModeByKey]);

    // 시스템 TTS 음성 목록 갱신 (브라우저가 비동기로 voice를 로드하므로 onvoiceschanged 후 한 번 더 받기)
    useEffect(() => {
      if (!isSpeechSynthesisSupported()) return undefined;
      const refresh = () => setAvailableTtsVoices(listSpeechVoices());
      refresh();
      try {
        window.speechSynthesis.addEventListener("voiceschanged", refresh);
        return () => window.speechSynthesis.removeEventListener("voiceschanged", refresh);
      } catch {
        return undefined;
      }
    }, []);

    // 새 assistant 메시지 도착 시 auto-speak (chat/coding 탭에서, 설정에 따라)
    useEffect(() => {
      if (!uiPreferences.speech?.autoSpeak) return;
      if (rootTab !== "chat" && rootTab !== "coding") return;
      if (!isSpeechSynthesisSupported()) return;
      if (!Array.isArray(currentMessages) || currentMessages.length === 0) return;

      let lastAssistant = null;
      for (let i = currentMessages.length - 1; i >= 0; i -= 1) {
        if (currentMessages[i]?.role === "assistant") {
          lastAssistant = currentMessages[i];
          break;
        }
      }
      if (!lastAssistant) return;

      const text = lastAssistant.text || lastAssistant.content || "";
      if (!`${text}`.trim()) return;
      const id = `${currentConversationId}|${lastAssistant.ts || lastAssistant.id || currentMessages.length}|${`${text}`.slice(0, 48)}`;
      if (id === lastSpokenMessageRef.current) return;
      // 첫 마운트 시 기존 메시지를 자동으로 읽지 않도록: ref가 비어있으면 기록만 하고 발화 X
      if (!lastSpokenMessageRef.current) {
        lastSpokenMessageRef.current = id;
        return;
      }
      lastSpokenMessageRef.current = id;
      speakText(text, uiPreferences.speech);
    }, [currentMessages, currentConversationId, uiPreferences.speech, rootTab]);

    useEffect(() => {
      if (typeof document === "undefined") {
        return undefined;
      }

      const mode = uiPreferences.theme?.mode || "system";
      const media = typeof window !== "undefined" && window.matchMedia
        ? window.matchMedia("(prefers-color-scheme: dark)")
        : null;
      const applyTheme = () => {
        const resolved = mode === "system"
          ? (media && media.matches ? "dark" : "light")
          : mode;
        document.documentElement.dataset.theme = resolved === "dark" ? "dark" : "light";
        document.documentElement.dataset.themeMode = mode;
      };
      applyTheme();
      if (mode !== "system" || !media) {
        return undefined;
      }

      media.addEventListener?.("change", applyTheme);
      return () => media.removeEventListener?.("change", applyTheme);
    }, [uiPreferences.theme?.mode]);

    useEffect(() => () => {
      const recognition = speechRecognitionRef.current;
      if (!recognition) {
        return;
      }

      try {
        recognition.abort();
      } catch {
      }
      speechRecognitionRef.current = null;
    }, []);

    useEffect(() => {
      const query = currentConversationFilter.trim();
      if (!authed || query.length < 2) {
        setConversationSearchByKey((prev) => ({
          ...prev,
          [currentKey]: { query, loading: false, results: [], error: "" }
        }));
        return undefined;
      }

      const timer = window.setTimeout(() => {
        setConversationSearchByKey((prev) => ({
          ...prev,
          [currentKey]: {
            ...(prev[currentKey] || {}),
            query,
            loading: true,
            error: ""
          }
        }));
        send({ type: "conversation_search", query, maxResults: 24 }, { silent: true });
      }, 350);
      return () => window.clearTimeout(timer);
    }, [authed, currentConversationFilter, currentKey]);

    useEffect(() => {
      function handleGlobalKeyDown(event) {
        const native = event.nativeEvent || event;
        if (event.defaultPrevented) {
          return;
        }
        if (native.isComposing || native.keyCode === 229) {
          return;
        }

        const shortcuts = uiPreferences.shortcuts || DEFAULT_UI_PREFERENCES.shortcuts;
        if (shortcutMatches(event, shortcuts.close)) {
          if (commandPaletteOpen) {
            event.preventDefault();
            setCommandPaletteOpen(false);
            return;
          }
        }

        if (commandPaletteOpen) {
          return;
        }

        if (shortcutMatches(event, shortcuts.palette) || shortcutMatches(event, shortcuts.paletteAlt)) {
          event.preventDefault();
          setCommandPaletteOpen(true);
          return;
        }

        const tabKeyMap = {
          tabChat: "chat",
          tabRoutine: "routine",
          tabLogic: "logic",
          tabCoding: "coding",
          tabNotebook: "notebook",
          tabAutomation: "automation",
          tabSkills: "skills",
          tabSettings: "settings"
        };
        let matchedTab = null;
        for (const [shortcutKey, tabKey] of Object.entries(tabKeyMap)) {
          if (shortcuts[shortcutKey] && shortcutMatches(event, shortcuts[shortcutKey])) {
            matchedTab = tabKey;
            break;
          }
        }
        if (matchedTab) {
          event.preventDefault();
          setRootTab(matchedTab);
          return;
        }

        if (shortcutMatches(event, shortcuts.newChat)) {
          event.preventDefault();
          if (typeof createConversation === "function") {
            createConversation();
          }
          return;
        }

        if (shortcutMatches(event, shortcuts.searchConversations)) {
          event.preventDefault();
          const searchInput = document.querySelector(".conversation-search input.input");
          if (searchInput && typeof searchInput.focus === "function") {
            searchInput.focus();
            searchInput.select?.();
          }
          return;
        }

        if (shortcutMatches(event, shortcuts.toggleTheme)) {
          event.preventDefault();
          const order = ["system", "light", "dark"];
          const current = uiPreferences.theme?.mode || "system";
          const next = order[(order.indexOf(current) + 1) % order.length];
          updateUiPreference({ theme: { ...(uiPreferences.theme || {}), mode: next } });
          return;
        }

        if (shortcutMatches(event, shortcuts.focusComposer)) {
          event.preventDefault();
          const composerInput = document.querySelector(".composer textarea, .composer .rich-input, .composer input.input");
          if (composerInput && typeof composerInput.focus === "function") {
            composerInput.focus();
          }
          return;
        }

        if (shortcutMatches(event, shortcuts.toggleVoice)) {
          event.preventDefault();
          const micBtn = document.querySelector(".composer-icon-btn.mic");
          if (micBtn && typeof micBtn.click === "function") {
            micBtn.click();
          }
          return;
        }

        if (shortcutMatches(event, shortcuts.toggleAutoSpeak)) {
          event.preventDefault();
          updateUiPreference({
            speech: { ...(uiPreferences.speech || {}), autoSpeak: !uiPreferences.speech?.autoSpeak }
          });
          return;
        }

        if (shortcutMatches(event, shortcuts.stopSpeaking)) {
          event.preventDefault();
          stopSpeaking();
          return;
        }

        if (shortcutMatches(event, shortcuts.send)) {
          event.preventDefault();
          sendCurrentComposer();
        }
      }

      window.addEventListener("keydown", handleGlobalKeyDown);
      return () => window.removeEventListener("keydown", handleGlobalKeyDown);
    }, [commandPaletteOpen, uiPreferences.shortcuts, uiPreferences.theme, rootTab, chatMode, codingMode, chatInputSingle, chatInputOrch, chatInputMulti, codingInputSingle, codingInputOrch, codingInputMulti]);
    const selectedDeleteConversationIds = useMemo(() => {
      const ids = new Set();
      currentSelectedConversationIds.forEach((id) => {
        if (id) {
          ids.add(id);
        }
      });

      if (currentSelectedFolders.length > 0) {
        currentConversationList.forEach((item) => {
          const detail = conversationDetails[item.id] || null;
          const project = (detail?.project ?? item.project ?? "기본").trim() || "기본";
          if (currentSelectedFolders.includes(project)) {
            ids.add(item.id);
          }
        });
      }

      return Array.from(ids);
    }, [conversationDetails, currentConversationList, currentSelectedConversationIds, currentSelectedFolders]);

    useEffect(() => {
      if (typeof window === "undefined") {
        return undefined;
      }

      let frameId = 0;
      const syncViewportMetrics = () => {
        setViewportSize(getViewportSnapshot());
        const top = Math.max(0, Math.round(mainShellRef.current?.getBoundingClientRect().top || 0));
        setMainShellViewportTop(top);
      };
      const handleResize = () => {
        if (frameId) {
          window.cancelAnimationFrame(frameId);
        }
        frameId = window.requestAnimationFrame(() => {
          syncViewportMetrics();
        });
      };

      syncViewportMetrics();
      window.addEventListener("resize", handleResize);
      return () => {
        if (frameId) {
          window.cancelAnimationFrame(frameId);
        }
        window.removeEventListener("resize", handleResize);
      };
    }, []);

    useEffect(() => {
      if (rootTab !== "logic") {
        return undefined;
      }

      const shell = logicCanvasShellRef.current;
      if (!shell) {
        return undefined;
      }

      function handleWheel(event) {
        handleLogicCanvasWheel(event);
      }

      shell.addEventListener("wheel", handleWheel, { passive: false });
      return () => {
        shell.removeEventListener("wheel", handleWheel);
      };
    }, [rootTab, currentLogicPane, isPortraitMobileLayout]);

    useEffect(() => {
      if (typeof window === "undefined") {
        return;
      }
      const frameId = window.requestAnimationFrame(() => {
        const top = Math.max(0, Math.round(mainShellRef.current?.getBoundingClientRect().top || 0));
        setMainShellViewportTop(top);
      });
      return () => window.cancelAnimationFrame(frameId);
    }, [rootTab, isPortraitMobileLayout, currentWorkspacePane]);

    useEffect(() => {
      if (!isPortraitMobileLayout) {
        return;
      }

      if (currentWorkspacePane === "composer") {
        setMobilePaneByTab((prev) => ({ ...prev, [responsiveWorkspaceKey]: "thread" }));
      }

      if (!routineSelectedId && currentRoutinePane === "detail") {
        setMobilePaneByTab((prev) => ({ ...prev, routine: "overview" }));
      }
    }, [
      currentRoutinePane,
      currentWorkspacePane,
      isPortraitMobileLayout,
      responsiveWorkspaceKey,
      routineSelectedId
    ]);

    useEffect(() => {
      if (!settingsState.remoteDashboardClient) {
        return;
      }

      const sensitiveSettingsPanes = new Set([
        "auth",
        "basic-auth",
        "basic-external",
        "basic-telegram",
        "basic-keys",
        "basic-cli"
      ]);
      if (sensitiveSettingsPanes.has(currentSettingsPane)) {
        setResponsivePane("settings", "basic-preferences");
      }
    }, [currentSettingsPane, settingsState.remoteDashboardClient]);

    function setResponsivePane(tabKey, paneKey) {
      if (!tabKey || !paneKey) {
        return;
      }
      setMobilePaneByTab((prev) => {
        if (prev[tabKey] === paneKey) {
          return prev;
        }
        return {
          ...prev,
          [tabKey]: paneKey
        };
      });
    }

    function adjustMobileMessageScrollForComposer(open, measuredComposerHeight = 0) {
      const panel = messageListRef.current;
      if (!panel) {
        return;
      }

      window.requestAnimationFrame(() => {
        const composer = document.querySelector(".mobile-composer-layer .composer");
        const composerHeight = composer ? composer.getBoundingClientRect().height : measuredComposerHeight;
        const scrollDelta = Math.max(0, Math.ceil(composerHeight));
        if (open) {
          panel.scrollTop = Math.min(panel.scrollHeight, panel.scrollTop + scrollDelta);
        } else {
          panel.scrollTop = Math.max(0, panel.scrollTop - scrollDelta);
        }
      });
    }

    function setMobileComposerOpen(tabKey, open) {
      if (!tabKey) {
        return;
      }
      const wasOpen = !!mobileComposerOpenByTab[tabKey];
      if (wasOpen === !!open) {
        return;
      }
      const currentComposer = document.querySelector(".mobile-composer-layer .composer");
      const currentComposerHeight = currentComposer ? currentComposer.getBoundingClientRect().height : 0;
      setMobileComposerOpenByTab((prev) => ({
        ...prev,
        [tabKey]: !!open
      }));
      if (open) {
        setResponsivePane(tabKey, "thread");
      }
      adjustMobileMessageScrollForComposer(!!open, currentComposerHeight);
    }

    const renderResponsiveSectionTabs = createResponsiveSectionTabsRenderer(e);

    function toggleThreadInfoPanel() {
      const next = !threadInfoOpen;
      setThreadInfoOpenByScope((prev) => ({
        ...prev,
        [threadInfoScopeKey]: next
      }));
      if (isPortraitMobileLayout && next) {
        setResponsivePane(responsiveWorkspaceKey, "support");
      }
    }

    function toggleAttachmentPanel() {
      const nextValue = !attachmentPanelOpen;
      setAttachmentPanelOpenByKey((prev) => ({
        ...prev,
        [currentKey]: nextValue
      }));
      if (isPortraitMobileLayout && nextValue) {
        setResponsivePane(responsiveWorkspaceKey, "thread");
      }
    }

    function setAttachmentDragActive(key, active) {
      const normalizedKey = `${key || currentKey}`.trim() || currentKey;
      setAttachmentDragActiveByKey((prev) => {
        const current = !!prev[normalizedKey];
        if (current === !!active) {
          return prev;
        }

        return {
          ...prev,
          [normalizedKey]: !!active
        };
      });
    }

    function clearAttachmentDragState(key) {
      attachmentDragDepthRef.current = 0;
      setAttachmentDragActive(key || currentKeyRef.current || currentKey, false);
    }
    const toolResultStats = useMemo(
      () => buildToolResultStats(toolResultItems, TOOL_RESULT_GROUPS),
      [toolResultItems]
    );
    const providerRuntimeStats = useMemo(
      () => buildProviderRuntimeStats(providerRuntimeItems, PROVIDER_RUNTIME_KEYS),
      [providerRuntimeItems]
    );
    const guardObsStats = useMemo(
      () => buildGuardObsStats({
        guardObsItems,
        guardObsChannelKeys: GUARD_OBS_CHANNEL_KEYS,
        guardAlertRules: GUARD_ALERT_RULES,
        buildGuardAlertRuleResult
      }),
      [guardObsItems]
    );
    const guardRetryTimelineMemorySnapshot = useMemo(
      () => buildGuardRetryTimelineSnapshot(
        guardRetryTimelineItems,
        {
          channels: GUARD_RETRY_TIMELINE_CHANNELS,
          bucketMinutes: GUARD_RETRY_TIMELINE_BUCKET_MINUTES,
          windowMinutes: GUARD_RETRY_TIMELINE_WINDOW_MINUTES,
          maxBucketRows: GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS
        }
      ),
      [guardRetryTimelineItems]
    );
    const guardRetryTimelineServerSnapshot = useMemo(
      () => normalizeGuardRetryTimelineSnapshot(
        guardRetryTimelineApiSnapshot,
        {
          channels: GUARD_RETRY_TIMELINE_CHANNELS,
          bucketMinutes: GUARD_RETRY_TIMELINE_BUCKET_MINUTES,
          windowMinutes: GUARD_RETRY_TIMELINE_WINDOW_MINUTES,
          maxBucketRows: GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS
        }
      ),
      [guardRetryTimelineApiSnapshot]
    );
    const guardRetryTimeline = useMemo(
      () => guardRetryTimelineServerSnapshot || guardRetryTimelineMemorySnapshot,
      [guardRetryTimelineMemorySnapshot, guardRetryTimelineServerSnapshot]
    );
    const guardRetryTimelineSource = guardRetryTimelineServerSnapshot ? "server_api" : "memory_fallback";
    const guardRetryTimelineRows = useMemo(
      () => buildGuardRetryTimelineRows(guardRetryTimeline, normalizeGuardNumber),
      [guardRetryTimeline]
    );
    const guardAlertSummary = useMemo(
      () => buildGuardAlertSummary(guardObsStats.guardAlertRows, severityRank),
      [guardObsStats.guardAlertRows]
    );
    const guardAlertPipelineEvent = useMemo(() => {
      const latestCapturedAt = guardObsItems[0] && guardObsItems[0].capturedAt
        ? guardObsItems[0].capturedAt
        : null;
      return buildGuardAlertPipelineEvent(
        guardObsStats,
        guardAlertSummary,
        guardRetryTimeline,
        latestCapturedAt ? { emittedAtUtc: latestCapturedAt } : {}
      );
    }, [guardAlertSummary, guardObsItems, guardObsStats, guardRetryTimeline]);
    const guardAlertPipelinePreview = useMemo(
      () => JSON.stringify(guardAlertPipelineEvent, null, 2),
      [guardAlertPipelineEvent]
    );
    const providerHealthRows = useMemo(() => buildProviderHealthRows({
      settingsState,
      copilotStatus,
      codexStatus
    }), [
      copilotStatus,
      codexStatus,
      settingsState
    ]);
    const providerRuntimeRows = useMemo(
      () => buildProviderRuntimeRows(providerHealthRows, providerRuntimeStats),
      [providerHealthRows, providerRuntimeStats]
    );
    const providerHealthSummary = useMemo(
      () => buildProviderHealthSummary(providerHealthRows, providerRuntimeStats),
      [providerHealthRows, providerRuntimeStats]
    );
    const toolDomainStats = useMemo(
      () => buildToolDomainStats(toolResultItems),
      [toolResultItems]
    );
    const opsFlowItems = useMemo(
      () => buildOpsFlowItems(providerRuntimeItems, toolResultItems),
      [providerRuntimeItems, toolResultItems]
    );
    const opsDomainStats = useMemo(
      () => buildOpsDomainStats(opsFlowItems),
      [opsFlowItems]
    );
    const filteredOpsFlowItems = useMemo(
      () => filterOpsFlowItems(opsFlowItems, opsDomainFilter),
      [opsDomainFilter, opsFlowItems]
    );
    const filteredToolResultItems = useMemo(
      () => filterToolResultItems(toolResultItems, toolResultFilter, toolDomainFilter),
      [toolDomainFilter, toolResultFilter, toolResultItems]
    );

    useEffect(() => {
      if (!authed) {
        setGuardRetryTimelineApiSnapshot(null);
        setGuardRetryTimelineApiFetchedAt("");
        setGuardRetryTimelineApiError("");
        return undefined;
      }

      let cancelled = false;
      let timerId = null;
      const query = new URLSearchParams({
        bucketMinutes: String(GUARD_RETRY_TIMELINE_BUCKET_MINUTES),
        windowMinutes: String(GUARD_RETRY_TIMELINE_WINDOW_MINUTES),
        maxBucketRows: String(GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS),
        channels: GUARD_RETRY_TIMELINE_CHANNELS.join(",")
      }).toString();

      const pollRetryTimeline = async () => {
        try {
          const response = await fetch(`/api/guard/retry-timeline?${query}`, {
            method: "GET",
            cache: "no-store",
            headers: { Accept: "application/json" }
          });
          if (!response.ok) {
            throw new Error(`http_${response.status}`);
          }

          const payload = await response.json();
          const normalized = normalizeGuardRetryTimelineSnapshot(
            payload,
            {
              channels: GUARD_RETRY_TIMELINE_CHANNELS,
              bucketMinutes: GUARD_RETRY_TIMELINE_BUCKET_MINUTES,
              windowMinutes: GUARD_RETRY_TIMELINE_WINDOW_MINUTES,
              maxBucketRows: GUARD_RETRY_TIMELINE_MAX_BUCKET_ROWS
            }
          );
          if (!normalized) {
            throw new Error("invalid_schema");
          }

          if (cancelled) {
            return;
          }
          setGuardRetryTimelineApiSnapshot(normalized);
          setGuardRetryTimelineApiFetchedAt(new Date().toISOString());
          setGuardRetryTimelineApiError("");
        } catch (error) {
          if (cancelled) {
            return;
          }
          setGuardRetryTimelineApiSnapshot(null);
          setGuardRetryTimelineApiError(mapErrorMessage(error instanceof Error ? error.message : "fetch_failed"));
        } finally {
          if (!cancelled) {
            timerId = window.setTimeout(pollRetryTimeline, GUARD_RETRY_TIMELINE_API_REFRESH_MS);
          }
        }
      };

      pollRetryTimeline();
      return () => {
        cancelled = true;
        if (timerId !== null) {
          window.clearTimeout(timerId);
        }
      };
    }, [authed]);

    function applyDomainFocus(domain) {
      const normalized = domain === "provider" || domain === "tool" || domain === "rag" ? domain : "all";
      setOpsDomainFilter(normalized);
      if (normalized === "provider") {
        setToolDomainFilter("all");
        return;
      }
      setToolDomainFilter(normalized);
    }

    function clearToolControlResults() {
      setToolResultItems([]);
      setProviderRuntimeItems([]);
      setGuardObsItems([]);
      setGuardRetryTimelineItems([]);
      setGuardRetryTimelineApiSnapshot(null);
      setGuardRetryTimelineApiFetchedAt("");
      setGuardRetryTimelineApiError("");
      setToolResultPreview("결과 대기 중");
      setToolResultFilter("all");
      applyDomainFocus("all");
      setSelectedToolResultId("");
      setToolControlError("");
    }

    function refreshDoctorReport(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setDoctorState((prev) => ({
        ...prev,
        loading: true,
        pending: false,
        lastError: "",
        lastAction: "get_last"
      }));

      const ok = requestDoctorLast(send, options);
      if (!ok) {
        setDoctorState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: 최근 doctor 보고서 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function runDoctorReport() {
      if (!ensureAuthed()) {
        return false;
      }

      setDoctorState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        lastError: "",
        lastAction: "run"
      }));

      const ok = requestDoctorRun(send);
      if (!ok) {
        setDoctorState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: doctor 실행 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function trimNotebookEntry(value, maxChars = 800) {
      const normalized = `${value || ""}`.trim();
      if (normalized.length <= maxChars) {
        return normalized;
      }

      return `${normalized.slice(0, maxChars)}...`;
    }

    function buildSelectedPlanDecisionText() {
      const snapshot = plansState.snapshot || null;
      const plan = snapshot?.plan || null;
      if (!plan) {
        return "";
      }

      const lines = [
        "선택한 작업 정리에서 가져온 내용",
        "",
        `작업: ${plan.title || plan.planId || "-"}`,
        `상태: ${plan.status || "-"}`,
        `하려던 일: ${plan.objective || "-"}`
      ];

      if (Array.isArray(plan.constraints) && plan.constraints.length > 0) {
        lines.push("", "주의할 점:");
        plan.constraints.forEach((item) => {
          lines.push(`- ${item}`);
        });
      }

      if (snapshot.review) {
        lines.push(
          "",
          "점검에서 나온 말:",
          `- ${snapshot.review.summary || "-"}`
        );
      }

      if (snapshot.execution) {
        lines.push(
          "",
          "최근 시작 상태:",
          `- ${snapshot.execution.status || "-"}`,
          `- ${snapshot.execution.message || "-"}`
        );
        if (snapshot.execution.resultSummary) {
          lines.push("", "진행 요약:", trimNotebookEntry(snapshot.execution.resultSummary, 420));
        }
      }

      lines.push("", `참고 ID: ${plan.planId || "-"}`);

      return lines.join("\n").trim();
    }

    function buildSelectedTaskVerificationText() {
      const snapshot = taskGraphState.snapshot || null;
      const graph = snapshot?.graph || null;
      if (!graph) {
        return "";
      }

      const nodes = Array.isArray(graph.nodes) ? graph.nodes : [];
      const selectedTask = nodes.find((task) => task.taskId === taskGraphState.selectedTaskId)
        || nodes[0]
        || null;
      const output = taskGraphState.output || null;
      const lines = [
        "작업 묶음에서 가져온 내용",
        "",
        `작업 묶음 상태: ${graph.status || "-"}`,
        `완료: ${nodes.filter((task) => task.status === "Completed").length}/${nodes.length}`,
        `실패: ${nodes.filter((task) => task.status === "Failed").length}`
      ];

      if (selectedTask) {
        lines.push(
          "",
          "대표 작업:",
          `- ${selectedTask.title || selectedTask.taskId || "-"}`,
          `- 상태: ${selectedTask.status || "-"}`,
          `- 종류: ${selectedTask.category || "-"}`
        );
        if (selectedTask.outputSummary) {
          lines.push("", "작업 요약:", trimNotebookEntry(selectedTask.outputSummary, 280));
        }
        if (selectedTask.error) {
          lines.push("", "문제:", trimNotebookEntry(selectedTask.error, 220));
        }
      }

      if (output?.resultJson) {
        lines.push("", "결과 파일에서 볼 것:", trimNotebookEntry(output.resultJson, 420));
      } else if (output?.stdout) {
        lines.push("", "출력에서 볼 것:", trimNotebookEntry(output.stdout, 420));
      }

      lines.push("", `참고 ID: ${graph.graphId || "-"} / 기준 계획 ${graph.sourcePlanId || "-"}`);

      return lines.join("\n").trim();
    }

    function buildDoctorVerificationText() {
      const report = doctorState.report || null;
      if (!report) {
        return "";
      }

      const checks = Array.isArray(report.checks) ? report.checks : [];
      const failed = checks.filter((item) => `${item.status || ""}`.toLowerCase() === "fail");
      const warned = checks.filter((item) => `${item.status || ""}`.toLowerCase() === "warn");
      const lines = [
        "환경 진단에서 가져온 내용",
        "",
        `진단 시간: ${report.createdAtUtc || "-"}`,
        `정상: ${report.okCount || 0}`,
        `경고: ${report.warnCount || 0}`,
        `실패: ${report.failCount || 0}`,
        `건너뜀: ${report.skipCount || 0}`
      ];

      if (failed.length > 0 || warned.length > 0) {
        lines.push("", "봐야 할 항목:");
        failed.concat(warned).slice(0, 8).forEach((item) => {
          lines.push(`- ${item.id || "-"}: ${item.summary || "-"}`);
        });
      } else {
        lines.push("", "봐야 할 항목:", "- 현재 경고나 실패 항목 없음");
      }

      lines.push("", `참고 ID: ${report.reportId || "-"}`);

      return lines.join("\n").trim();
    }

    function buildRefactorVerificationText() {
      const preview = refactorState.preview || null;
      const readResult = refactorState.readResult || null;
      const issues = Array.isArray(refactorState.lastIssues) ? refactorState.lastIssues : [];
      const filePath = readResult?.path || preview?.path || refactorState.loadedPath || refactorState.filePath || "";
      if (!filePath && !refactorState.lastMessage && issues.length === 0) {
        return "";
      }

      const lines = [
        "리팩터 작업에서 가져온 내용",
        "",
        `파일: ${filePath || "-"}`,
        `마지막 작업: ${refactorState.lastAction || "-"}`,
        `상태: ${refactorState.lastMessage || "-"}`
      ];

      if (preview) {
        lines.push(
          `미리보기 ID: ${preview.previewId || "-"}`,
          `바로 적용 가능: ${preview.safeToApply ? "예" : "아니오"}`
        );
        if (preview.unifiedDiff) {
          lines.push("", "미리보기:", trimNotebookEntry(preview.unifiedDiff, 420));
        }
      }

      if (issues.length > 0) {
        lines.push("", "확인할 문제:");
        issues.slice(0, 6).forEach((issue) => {
          lines.push(`- ${issue.startLine || "?"}-${issue.endLine || "?"}: ${issue.reason || "-"}`);
        });
      }

      return lines.join("\n").trim();
    }

    function setNotebookProjectKey(value) {
      setNotebooksState((prev) => ({ ...prev, projectKeyDraft: value }));
    }

    function setNotebookAppendKind(value) {
      const normalized = `${value || ""}`.trim().toLowerCase();
      const nextKind = normalized === "decision" || normalized === "verification" ? normalized : "learning";
      setNotebooksState((prev) => ({ ...prev, appendKind: nextKind }));
    }

    function setNotebookAppendText(value) {
      setNotebooksState((prev) => ({ ...prev, appendText: value }));
    }

    function refreshNotebook(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setNotebooksState((prev) => ({
        ...prev,
        loading: true,
        pending: false,
        lastError: "",
        lastMessage: "",
        lastAction: "get"
      }));

      const ok = requestNotebookGet(send, notebooksState.projectKeyDraft, options);
      if (!ok) {
        setNotebooksState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: notebook 조회 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function appendNotebook(override = null) {
      if (!ensureAuthed()) {
        return false;
      }

      const kind = `${override && override.kind ? override.kind : notebooksState.appendKind || "learning"}`.trim().toLowerCase();
      const content = `${override && override.content ? override.content : notebooksState.appendText || ""}`.trim();
      const projectKey = `${override && typeof override.projectKey === "string" ? override.projectKey : notebooksState.projectKeyDraft || ""}`.trim();
      if (!content) {
        setNotebooksState((prev) => ({
          ...prev,
          lastError: "기록할 내용을 입력하세요."
        }));
        return false;
      }

      setNotebooksState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        lastError: "",
        lastMessage: "",
        lastAction: "append",
        appendKind: kind === "decision" || kind === "verification" ? kind : "learning",
        appendText: content
      }));

      const ok = requestNotebookAppend(send, {
        projectKey,
        kind,
        content
      });
      if (!ok) {
        setNotebooksState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: notebook append 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function createNotebookHandoff(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setNotebooksState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        lastError: "",
        lastMessage: "",
        lastAction: "handoff"
      }));

      const ok = requestHandoffCreate(send, notebooksState.projectKeyDraft, options);
      if (!ok) {
        setNotebooksState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 이어보기 생성 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function appendSelectedPlanDecision() {
      const content = buildSelectedPlanDecisionText();
      if (!content) {
        setNotebooksState((prev) => ({
          ...prev,
          lastError: "선택된 계획이 없어 방향 정리 초안을 만들 수 없습니다."
        }));
        return false;
      }

      return appendNotebook({
        kind: "decision",
        content
      });
    }

    function appendSelectedTaskVerification() {
      const content = buildSelectedTaskVerificationText();
      if (!content) {
        setNotebooksState((prev) => ({
          ...prev,
          lastError: "선택한 작업 묶음이 없어 확인 내용 초안을 만들 수 없습니다."
        }));
        return false;
      }

      return appendNotebook({
        kind: "verification",
        content
      });
    }

    function appendDoctorVerification() {
      const content = buildDoctorVerificationText();
      if (!content) {
        setNotebooksState((prev) => ({
          ...prev,
          lastError: "최근 doctor 보고서가 없어 확인 내용 초안을 만들 수 없습니다."
        }));
        return false;
      }

      return appendNotebook({
        kind: "verification",
        content
      });
    }

    function appendRefactorVerification() {
      const content = buildRefactorVerificationText();
      if (!content) {
        setNotebooksState((prev) => ({
          ...prev,
          lastError: "최근 refactor 상태가 없어 확인 내용 초안을 만들 수 없습니다."
        }));
        return false;
      }

      return appendNotebook({
        kind: "verification",
        content
      });
    }

    function setPlanCreateObjective(value) {
      setPlansState((prev) => ({ ...prev, createObjective: value }));
    }

    function refreshProjectContext(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setContextState((prev) => ({
        ...prev,
        loading: true,
        lastError: "",
        lastAction: "scan"
      }));

      const ok = requestContextScan(send, options);
      if (!ok) {
        setContextState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: 프로젝트 문맥 스캔 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function refreshSkillsList(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setContextState((prev) => ({
        ...prev,
        loadingSkills: true,
        lastError: "",
        lastAction: "skills"
      }));

      const ok = requestSkillsList(send, options);
      if (!ok) {
        setContextState((prev) => ({
          ...prev,
          loadingSkills: false,
          lastError: "오류: skills 목록 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function refreshCommandsList(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setContextState((prev) => ({
        ...prev,
        loadingCommands: true,
        lastError: "",
        lastAction: "commands"
      }));

      const ok = requestCommandsList(send, options);
      if (!ok) {
        setContextState((prev) => ({
          ...prev,
          loadingCommands: false,
          lastError: "오류: commands 목록 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function setPlanCreateConstraintsText(value) {
      setPlansState((prev) => ({ ...prev, createConstraintsText: value }));
    }

    function setPlanCreateMode(value) {
      setPlansState((prev) => ({ ...prev, createMode: value === "interview" ? "interview" : "fast" }));
    }

    function refreshPlansList(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setPlansState((prev) => ({
        ...prev,
        loading: true,
        pending: false,
        lastError: "",
        lastAction: "list"
      }));

      const ok = requestPlanList(send, options);
      if (!ok) {
        setPlansState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: 작업 목록 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function loadPlanSnapshot(planId, options = {}) {
      const normalizedPlanId = `${planId || ""}`.trim();
      if (!ensureAuthed() || !normalizedPlanId) {
        return false;
      }

      setPlansState((prev) => ({
        ...prev,
        loading: true,
        pending: false,
        selectedPlanId: normalizedPlanId,
        lastError: "",
        lastAction: "get"
      }));

      const ok = requestPlanGet(send, normalizedPlanId, options);
      if (!ok) {
        setPlansState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: 작업 상세 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function submitPlanCreate() {
      if (!ensureAuthed()) {
        return false;
      }

      const objective = `${plansState.createObjective || ""}`.trim();
      if (!objective) {
        setPlansState((prev) => ({
          ...prev,
          lastError: "할 일을 입력하세요."
        }));
        return false;
      }

      const constraints = `${plansState.createConstraintsText || ""}`
        .split("\n")
        .map((item) => item.trim())
        .filter(Boolean);

      setPlansState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        lastError: "",
        lastAction: "create"
      }));

      const ok = requestPlanCreate(send, {
        objective,
        constraints,
        mode: plansState.createMode || "fast",
        conversationId: currentConversationId || undefined
      });
      if (!ok) {
        setPlansState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 계획 만들기 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function reviewPlan(planId) {
      const normalizedPlanId = `${planId || ""}`.trim();
      if (!ensureAuthed() || !normalizedPlanId) {
        return false;
      }

      setPlansState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        selectedPlanId: normalizedPlanId,
        lastError: "",
        lastAction: "review"
      }));

      const ok = requestPlanReview(send, normalizedPlanId);
      if (!ok) {
        setPlansState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 계획 점검 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function approvePlan(planId) {
      const normalizedPlanId = `${planId || ""}`.trim();
      if (!ensureAuthed() || !normalizedPlanId) {
        return false;
      }

      setPlansState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        selectedPlanId: normalizedPlanId,
        lastError: "",
        lastAction: "approve"
      }));

      const ok = requestPlanApprove(send, normalizedPlanId);
      if (!ok) {
        setPlansState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 진행 확정 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function runPlan(planId) {
      const normalizedPlanId = `${planId || ""}`.trim();
      if (!ensureAuthed() || !normalizedPlanId) {
        return false;
      }

      setPlansState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        selectedPlanId: normalizedPlanId,
        lastError: "",
        lastAction: "run"
      }));

      const ok = requestPlanRun(send, normalizedPlanId);
      if (!ok) {
        setPlansState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 작업 시작 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function setTaskGraphCreatePlanId(value) {
      setTaskGraphState((prev) => ({ ...prev, createPlanId: value }));
    }

    function setRoutingPolicyChain(categoryKey, value) {
      setRoutingPolicyState((prev) => ({
        ...prev,
        draftChains: {
          ...(prev.draftChains || {}),
          [categoryKey]: value
        }
      }));
    }

    function refreshRoutingPolicy(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setRoutingPolicyState((prev) => ({
        ...prev,
        loading: true,
        pending: false,
        lastError: "",
        lastAction: "get"
      }));

      const ok = requestRoutingPolicyGet(send, options);
      if (!ok) {
        setRoutingPolicyState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: AI 순서 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function saveRoutingPolicy() {
      if (!ensureAuthed()) {
        return false;
      }

      const draft = routingPolicyState.draftChains || {};
      const policy = Object.keys(draft).reduce((acc, key) => {
        const items = `${draft[key] || ""}`
          .split(",")
          .map((item) => item.trim())
          .filter(Boolean);
        acc[key] = items;
        return acc;
      }, {});

      setRoutingPolicyState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        lastError: "",
        lastAction: "save"
      }));

      const ok = requestRoutingPolicySave(send, policy);
      if (!ok) {
        setRoutingPolicyState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: AI 순서 저장 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function resetRoutingPolicy() {
      if (!ensureAuthed()) {
        return false;
      }

      setRoutingPolicyState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        lastError: "",
        lastAction: "reset"
      }));

      const ok = requestRoutingPolicyReset(send);
      if (!ok) {
        setRoutingPolicyState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: AI 순서 초기화 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function refreshRoutingDecision(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      const ok = requestRoutingDecisionGetLast(send, options);
      if (!ok) {
        log("오류: 마지막 AI 선택 기록 요청을 전송하지 못했습니다.", "error");
      }

      return ok;
    }

    function useSelectedPlanForTaskGraph() {
      const nextPlanId = plansState.selectedPlanId || plansState.snapshot?.plan?.planId || "";
      setTaskGraphState((prev) => ({ ...prev, createPlanId: nextPlanId }));
    }

    function refreshTaskGraphList(options = {}) {
      if (!ensureAuthed()) {
        return false;
      }

      setTaskGraphState((prev) => ({
        ...prev,
        loading: true,
        pending: false,
        lastError: "",
        lastAction: "list"
      }));

      const ok = requestTaskGraphList(send, options);
      if (!ok) {
        setTaskGraphState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: 작업 묶음 목록 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function loadTaskGraph(graphId, options = {}) {
      const normalizedGraphId = `${graphId || ""}`.trim();
      if (!ensureAuthed() || !normalizedGraphId) {
        return false;
      }

      setTaskGraphState((prev) => ({
        ...prev,
        loading: true,
        pending: false,
        selectedGraphId: normalizedGraphId,
        lastError: "",
        lastAction: "get"
      }));

      const ok = requestTaskGraphGet(send, normalizedGraphId, options);
      if (!ok) {
        setTaskGraphState((prev) => ({
          ...prev,
          loading: false,
          lastError: "오류: 작업 묶음 상세 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function submitTaskGraphCreate() {
      if (!ensureAuthed()) {
        return false;
      }

      const planId = `${taskGraphState.createPlanId || plansState.selectedPlanId || plansState.snapshot?.plan?.planId || ""}`.trim();
      if (!planId) {
        setTaskGraphState((prev) => ({
          ...prev,
          lastError: "작업 묶음을 만들 계획 ID를 입력하세요."
        }));
        return false;
      }

      setTaskGraphState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        lastError: "",
        lastAction: "create"
      }));

      const ok = requestTaskGraphCreate(send, planId);
      if (!ok) {
        setTaskGraphState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 작업 묶음 생성 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function runTaskGraph(graphId) {
      const normalizedGraphId = `${graphId || ""}`.trim();
      if (!ensureAuthed() || !normalizedGraphId) {
        return false;
      }

      setTaskGraphState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        selectedGraphId: normalizedGraphId,
        lastError: "",
        lastAction: "run"
      }));

      const ok = requestTaskGraphRun(send, normalizedGraphId);
      if (!ok) {
        setTaskGraphState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 작업 묶음 시작 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function loadTaskOutput(graphId, taskId, options = {}) {
      const normalizedGraphId = `${graphId || ""}`.trim();
      const normalizedTaskId = `${taskId || ""}`.trim();
      if (!ensureAuthed() || !normalizedGraphId || !normalizedTaskId) {
        return false;
      }

      setTaskGraphState((prev) => ({
        ...prev,
        selectedGraphId: normalizedGraphId,
        selectedTaskId: normalizedTaskId,
        lastError: "",
        output: prev.selectedGraphId === normalizedGraphId && prev.selectedTaskId === normalizedTaskId
          ? prev.output
          : null
      }));

      return requestTaskOutput(send, normalizedGraphId, normalizedTaskId, options);
    }

    function cancelTask(graphId, taskId) {
      const normalizedGraphId = `${graphId || ""}`.trim();
      const normalizedTaskId = `${taskId || ""}`.trim();
      if (!ensureAuthed() || !normalizedGraphId || !normalizedTaskId) {
        return false;
      }

      setTaskGraphState((prev) => ({
        ...prev,
        pending: true,
        loading: false,
        selectedGraphId: normalizedGraphId,
        selectedTaskId: normalizedTaskId,
        lastError: "",
        lastAction: "cancel"
      }));

      const ok = requestTaskCancel(send, normalizedGraphId, normalizedTaskId);
      if (!ok) {
        setTaskGraphState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: 작업 취소 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function selectRefactorLine(lineNumber) {
      setRefactorState((prev) => {
        const current = parseRefactorLineNumber(lineNumber);
        if (!current) {
          return prev;
        }

        const start = parseRefactorLineNumber(prev.selectedStartLine);
        const end = parseRefactorLineNumber(prev.selectedEndLine);
        if (!start || !end || start !== end || current === start) {
          return {
            ...prev,
            selectedStartLine: String(current),
            selectedEndLine: String(current),
            preview: null,
            lastError: ""
          };
        }

        return {
          ...prev,
          selectedStartLine: String(Math.min(start, current)),
          selectedEndLine: String(Math.max(start, current)),
          preview: null,
          lastError: ""
        };
      });
    }

    function readRefactorAnchors() {
      if (!ensureAuthed()) {
        return false;
      }

      const path = `${refactorState.filePath || ""}`.trim();
      if (!path) {
        setRefactorState((prev) => ({
          ...prev,
          lastError: "파일 경로를 입력하세요."
        }));
        return false;
      }

      setRefactorState((prev) => ({
        ...prev,
        pending: true,
        lastAction: "read",
        lastError: "",
        lastMessage: "",
        lastIssues: []
      }));

      const ok = requestRefactorRead(send, path);
      if (!ok) {
        setRefactorState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: Anchor 읽기 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function previewSafeRefactor() {
      if (!ensureAuthed()) {
        return false;
      }

      const mode = `${refactorState.mode || "anchor"}`;
      if (mode === "lsp") {
        const path = `${refactorState.filePath || refactorState.loadedPath || ""}`.trim();
        const symbol = `${refactorState.symbol || ""}`.trim();
        const newName = `${refactorState.newName || ""}`.trim();
        if (!path) {
          setRefactorState((prev) => ({
            ...prev,
            lastError: "파일 경로를 입력하세요."
          }));
          return false;
        }

        if (!symbol || !newName) {
          setRefactorState((prev) => ({
            ...prev,
            lastError: "symbol과 새 이름을 모두 입력하세요."
          }));
          return false;
        }

        setRefactorState((prev) => ({
          ...prev,
          pending: true,
          lastAction: "lsp_rename",
          lastError: "",
          lastMessage: "",
          lastIssues: []
        }));

        const ok = requestLspRename(send, path, symbol, newName);
        if (!ok) {
          setRefactorState((prev) => ({
            ...prev,
            pending: false,
            lastError: "오류: LSP rename 요청을 전송하지 못했습니다."
          }));
        }

        return ok;
      }

      if (mode === "ast") {
        const path = `${refactorState.filePath || refactorState.loadedPath || ""}`.trim();
        const pattern = `${refactorState.pattern || ""}`.trim();
        if (!path) {
          setRefactorState((prev) => ({
            ...prev,
            lastError: "파일 경로를 입력하세요."
          }));
          return false;
        }

        if (!pattern || !`${refactorState.replacement || ""}`) {
          setRefactorState((prev) => ({
            ...prev,
            lastError: "pattern과 replacement를 모두 입력하세요."
          }));
          return false;
        }

        setRefactorState((prev) => ({
          ...prev,
          pending: true,
          lastAction: "ast_replace",
          lastError: "",
          lastMessage: "",
          lastIssues: []
        }));

        const ok = requestAstReplace(send, path, pattern, refactorState.replacement || "");
        if (!ok) {
          setRefactorState((prev) => ({
            ...prev,
            pending: false,
            lastError: "오류: AST replace 요청을 전송하지 못했습니다."
          }));
        }

        return ok;
      }

      const readResult = refactorState.readResult || null;
      if (!readResult || !Array.isArray(readResult.lines) || readResult.lines.length === 0) {
        setRefactorState((prev) => ({
          ...prev,
          lastError: "먼저 Anchor를 읽으세요."
        }));
        return false;
      }

      const start = parseRefactorLineNumber(refactorState.selectedStartLine);
      const end = parseRefactorLineNumber(refactorState.selectedEndLine);
      if (!start || !end) {
        setRefactorState((prev) => ({
          ...prev,
          lastError: "시작/끝 line을 모두 선택하세요."
        }));
        return false;
      }

      const safeStart = Math.min(start, end);
      const safeEnd = Math.max(start, end);
      const selectedLines = getRefactorSelectedLines({
        ...refactorState,
        selectedStartLine: String(safeStart),
        selectedEndLine: String(safeEnd)
      });
      if (selectedLines.length !== safeEnd - safeStart + 1) {
        setRefactorState((prev) => ({
          ...prev,
          lastError: "현재 로드된 Anchor 범위 밖의 line은 preview할 수 없습니다. 파일을 다시 읽으세요."
        }));
        return false;
      }

      setRefactorState((prev) => ({
        ...prev,
        pending: true,
        lastAction: "preview",
        lastError: "",
        lastMessage: "",
        lastIssues: []
      }));

      const ok = requestRefactorPreview(send, readResult.path || refactorState.loadedPath || refactorState.filePath, [{
        startLine: safeStart,
        endLine: safeEnd,
        expectedHashes: selectedLines.map((line) => line.hash),
        replacement: refactorState.replacement || ""
      }]);
      if (!ok) {
        setRefactorState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: Safe Refactor preview 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function applySafeRefactor() {
      if (!ensureAuthed()) {
        return false;
      }

      const previewId = `${refactorState.preview && refactorState.preview.previewId ? refactorState.preview.previewId : ""}`.trim();
      if (!previewId) {
        setRefactorState((prev) => ({
          ...prev,
          lastError: "먼저 preview를 만드세요."
        }));
        return false;
      }

      setRefactorState((prev) => ({
        ...prev,
        pending: true,
        lastAction: "apply",
        lastError: "",
        lastMessage: ""
      }));

      const ok = requestRefactorApply(send, previewId);
      if (!ok) {
        setRefactorState((prev) => ({
          ...prev,
          pending: false,
          lastError: "오류: Safe Refactor apply 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function selectToolResultItem(item) {
      setSelectedToolResultId(item.id);
      setToolResultPreview(item.preview || "결과 대기 중");
      if (item.errorText) {
        setToolControlError(item.errorText);
      }
    }

    useEffect(() => {
      unmountedRef.current = false;
      setAuthExpiry(getSavedAuthExpiry());

      const worker = new Worker("/worker.js");
      worker.onmessage = (event) => {
        const msg = event.data || {};
        if (msg.type === "logs_updated") {
          setLogs(msg.payload || "");
          return;
        }

        if (msg.type === "ws_message") {
          handleServerMessage(msg.payload || {});
        }
      };

      workerRef.current = worker;
      connect();

      return () => {
        unmountedRef.current = true;
        if (reconnectTimerRef.current) {
          clearTimeout(reconnectTimerRef.current);
          reconnectTimerRef.current = null;
        }

        const ws = wsRef.current;
        if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
          ws.close();
        }

        wsRef.current = null;
        outboundQueueRef.current = [];
        hasOpenedSocketRef.current = false;
        worker.terminate();
      };
    }, []);

    useEffect(() => {
      if (rootTab !== "coding" && (safeRefactorOverlayOpen || codingResultOverlayOpen)) {
        setSafeRefactorOverlayOpen(false);
        setCodingResultOverlayOpen(false);
      }
    }, [rootTab, safeRefactorOverlayOpen, codingResultOverlayOpen]);

    useEffect(() => {
      if (!codingResultOverlayOpen) {
        return;
      }

      if (!currentConversationId || !codingResultByConversation[currentConversationId]) {
        setCodingResultOverlayOpen(false);
      }
    }, [codingResultOverlayOpen, currentConversationId, codingResultByConversation]);

    useEffect(() => {
      if (!safeRefactorOverlayOpen && !codingResultOverlayOpen) {
        return undefined;
      }

      function handleSupportOverlayEscape(event) {
        if (event.key === "Escape") {
          setSafeRefactorOverlayOpen(false);
          setCodingResultOverlayOpen(false);
        }
      }

      window.addEventListener("keydown", handleSupportOverlayEscape);
      return () => window.removeEventListener("keydown", handleSupportOverlayEscape);
    }, [safeRefactorOverlayOpen, codingResultOverlayOpen]);

    useEffect(() => {
      if (!chatSingleModel && selectedGroqModel) {
        setChatSingleModel(selectedGroqModel);
      }
      if (!chatOrchModel && selectedGroqModel) {
        setChatOrchModel(selectedGroqModel);
      }
      if (!chatOrchGroqModel && selectedGroqModel) {
        setChatOrchGroqModel(selectedGroqModel);
      }
      if (!chatMultiGroqModel && selectedGroqModel) {
        setChatMultiGroqModel(selectedGroqModel);
      }
      if (codingSingleProvider === "groq" && !codingSingleModel && selectedGroqModel) {
        setCodingSingleModel(selectedGroqModel);
      }
      if (codingOrchProvider === "groq" && !codingOrchModel && selectedGroqModel) {
        setCodingOrchModel(selectedGroqModel);
      }
      if (!codingOrchGroqModel && selectedGroqModel) {
        setCodingOrchGroqModel(selectedGroqModel);
      }
      if (codingMultiProvider === "groq" && !codingMultiModel && selectedGroqModel) {
        setCodingMultiModel(selectedGroqModel);
      }
    }, [
      selectedGroqModel,
      chatSingleModel,
      chatOrchModel,
      chatOrchGroqModel,
      chatMultiGroqModel,
      codingSingleProvider,
      codingSingleModel,
      codingOrchProvider,
      codingOrchModel,
      codingOrchGroqModel,
      codingMultiProvider,
      codingMultiModel
    ]);

    useEffect(() => {
      if (!chatMultiCopilotModel && selectedCopilotModel) {
        setChatMultiCopilotModel(selectedCopilotModel);
      }
      if (!chatMultiCodexModel) {
        setChatMultiCodexModel(DEFAULT_CODEX_MODEL);
      }
      if (!chatOrchCopilotModel && selectedCopilotModel) {
        setChatOrchCopilotModel(selectedCopilotModel);
      }
      if (!chatOrchCodexModel) {
        setChatOrchCodexModel(DEFAULT_CODEX_MODEL);
      }
      if (!chatOrchNvidiaModel) {
        setChatOrchNvidiaModel(DEFAULT_NVIDIA_MODEL);
      }
      if (!chatMultiNvidiaModel) {
        setChatMultiNvidiaModel(DEFAULT_NVIDIA_MODEL);
      }
      if (codingSingleProvider === "copilot" && !codingSingleModel && selectedCopilotModel) {
        setCodingSingleModel(selectedCopilotModel);
      }
      if (codingSingleProvider === "nvidia" && !codingSingleModel) {
        setCodingSingleModel(DEFAULT_NVIDIA_MODEL);
      }
      if (codingOrchProvider === "copilot" && !codingOrchModel && selectedCopilotModel) {
        setCodingOrchModel(selectedCopilotModel);
      }
      if (codingOrchProvider === "nvidia" && !codingOrchModel) {
        setCodingOrchModel(DEFAULT_NVIDIA_MODEL);
      }
      if (!codingOrchCopilotModel && selectedCopilotModel) {
        setCodingOrchCopilotModel(selectedCopilotModel);
      }
      if (!codingOrchCodexModel) {
        setCodingOrchCodexModel(DEFAULT_CODEX_MODEL);
      }
      if (!codingOrchNvidiaModel) {
        setCodingOrchNvidiaModel(DEFAULT_NVIDIA_MODEL);
      }
      if (codingMultiProvider === "copilot" && !codingMultiModel && selectedCopilotModel) {
        setCodingMultiModel(selectedCopilotModel);
      }
      if (codingMultiProvider === "nvidia" && !codingMultiModel) {
        setCodingMultiModel(DEFAULT_NVIDIA_MODEL);
      }
      if (!codingMultiCodexModel) {
        setCodingMultiCodexModel(DEFAULT_CODEX_MODEL);
      }
      if (!codingMultiNvidiaModel) {
        setCodingMultiNvidiaModel(DEFAULT_NVIDIA_MODEL);
      }
    }, [
      selectedCopilotModel,
      chatMultiCopilotModel,
      chatMultiCodexModel,
      chatMultiNvidiaModel,
      chatOrchCopilotModel,
      chatOrchCodexModel,
      chatOrchNvidiaModel,
      codingSingleProvider,
      codingSingleModel,
      codingOrchProvider,
      codingOrchModel,
      codingOrchCopilotModel,
      codingOrchCodexModel,
      codingOrchNvidiaModel,
      codingMultiProvider,
      codingMultiModel,
      codingMultiCodexModel,
      codingMultiNvidiaModel
    ]);

    useEffect(() => {
      requestConversations(scope, mode);
    }, [scope, mode]);

    useEffect(() => {
      currentKeyRef.current = currentKey;
      attachmentDragDepthRef.current = 0;

      function handleWindowDragEnter(event) {
        if (!hasDraggedFiles(event.dataTransfer)) {
          return;
        }

        event.preventDefault();
        attachmentDragDepthRef.current += 1;
        setAttachmentDragActive(currentKey, true);
      }

      function handleWindowDragOver(event) {
        if (!hasDraggedFiles(event.dataTransfer)) {
          return;
        }

        event.preventDefault();
        if (event.dataTransfer) {
          event.dataTransfer.dropEffect = "copy";
        }

        if (attachmentDragDepthRef.current <= 0) {
          attachmentDragDepthRef.current = 1;
        }
        setAttachmentDragActive(currentKey, true);
      }

      function handleWindowDragLeave(event) {
        if (!hasDraggedFiles(event.dataTransfer)) {
          return;
        }

        event.preventDefault();
        attachmentDragDepthRef.current = Math.max(0, attachmentDragDepthRef.current - 1);
        if (attachmentDragDepthRef.current === 0) {
          setAttachmentDragActive(currentKey, false);
        }
      }

      function handleWindowDrop(event) {
        if (!hasDraggedFiles(event.dataTransfer)) {
          return;
        }

        event.preventDefault();
        clearAttachmentDragState(currentKey);
      }

      window.addEventListener("dragenter", handleWindowDragEnter);
      window.addEventListener("dragover", handleWindowDragOver);
      window.addEventListener("dragleave", handleWindowDragLeave);
      window.addEventListener("drop", handleWindowDrop);

      return () => {
        window.removeEventListener("dragenter", handleWindowDragEnter);
        window.removeEventListener("dragover", handleWindowDragOver);
        window.removeEventListener("dragleave", handleWindowDragLeave);
        window.removeEventListener("drop", handleWindowDrop);
        attachmentDragDepthRef.current = 0;
        setAttachmentDragActive(currentKey, false);
      };
    }, [currentKey]);

    useEffect(() => {
      if (!currentConversation) {
        setMetaTitle("");
        setMetaProject("기본");
        setMetaCategory("일반");
        setMetaTags("");
        return;
      }

      setMetaTitle(currentConversation.title || "");
      setMetaProject(currentConversation.project || "기본");
      setMetaCategory(currentConversation.category || "일반");
      setMetaTags(Array.isArray(currentConversation.tags) ? currentConversation.tags.join(", ") : "");
    }, [currentConversationId, currentConversation]);

    useEffect(() => {
      const panel = messageListRef.current;
      if (!panel) {
        return;
      }

      panel.scrollTop = panel.scrollHeight;
    }, [currentConversationId, currentMessages, optimisticUserByKey[currentKey], pendingByKey[currentKey], errorByKey[currentKey]]);

    useEffect(() => {
      const timer = setInterval(() => setClockTick(Date.now()), 1000);
      return () => clearInterval(timer);
    }, []);

    useEffect(() => {
      if (!Array.isArray(groqModels) || groqModels.length === 0) {
        return;
      }

      const windowKeys = buildTimeWindowKeys(clockTick);
      setGroqUsageWindowBaseByModel((prev) => {
        let changed = false;
        const next = { ...prev };

        groqModels.forEach((item) => {
          const modelId = (item && item.id ? item.id : "").trim();
          if (!modelId) {
            return;
          }

          const totalRequests = Number(item.usage_requests || 0);
          const totalTokens = Number(item.usage_total_tokens || 0);
          const current = next[modelId] ? { ...next[modelId] } : {};

          if (current.minuteKey !== windowKeys.minute) {
            current.minuteKey = windowKeys.minute;
            current.minuteRequests = totalRequests;
            current.minuteTokens = totalTokens;
            changed = true;
          }
          if (current.hourKey !== windowKeys.hour) {
            current.hourKey = windowKeys.hour;
            current.hourRequests = totalRequests;
            current.hourTokens = totalTokens;
            changed = true;
          }
          if (current.dayKey !== windowKeys.day) {
            current.dayKey = windowKeys.day;
            current.dayRequests = totalRequests;
            current.dayTokens = totalTokens;
            changed = true;
          }

          next[modelId] = current;
        });

        return changed ? next : prev;
      });
    }, [clockTick, groqModels]);

    useEffect(() => {
      if (!authed) {
        return;
      }

      const windowKeys = buildTimeWindowKeys(clockTick);
      const previous = groqAutoRefreshWindowRef.current || { minute: "", hour: "", day: "" };
      const changed = previous.minute !== windowKeys.minute
        || previous.hour !== windowKeys.hour
        || previous.day !== windowKeys.day;

      if (!changed) {
        return;
      }

      groqAutoRefreshWindowRef.current = windowKeys;
      send({ type: "get_groq_models" }, { silent: true, queueIfClosed: false });
      send({ type: "get_cerebras_models" }, { silent: true, queueIfClosed: false });
    }, [authed, clockTick]);

    function saveAuthToken(token, expiresAtUtc) {
      persistAuthSession(token, expiresAtUtc);
      setAuthExpiry(expiresAtUtc || "");
    }

    function clearAuthToken() {
      clearPersistedAuthSession();
      setAuthExpiry("");
    }

    function log(text, level) {
      if (workerRef.current) {
        workerRef.current.postMessage({ type: "log", payload: text, level: level || "info" });
      }
    }

    function connect() {
      if (wsRef.current && (wsRef.current.readyState === WebSocket.OPEN || wsRef.current.readyState === WebSocket.CONNECTING)) {
        return;
      }

      const ws = new WebSocket(buildDashboardWsUrl());
      wsRef.current = ws;
      setStatus("연결 중");

      ws.onopen = () => {
        if (reconnectTimerRef.current) {
          clearTimeout(reconnectTimerRef.current);
          reconnectTimerRef.current = null;
        }

        const token = getSavedAuthToken();
        setStatus(token ? "연결됨 / 세션 인증 확인 중" : "연결됨 / OTP 대기");
        setAuthed(false);
        hasOpenedSocketRef.current = true;
        flushQueuedPayloads(ws, outboundQueueRef.current);
        sendInitialRequests();
      };

      ws.onclose = () => {
        wsRef.current = null;
        if (unmountedRef.current) {
          return;
        }

        // 인증된 적 없는 상태에서 끊김이 반복되면 stale authToken 이 매 재연결 시
        // resume_auth 시도 → 잠시 인증됨 → 다시 끊김 으로 토글 루프를 만든다.
        // 한 번도 인증 성공 못 했고 ws가 끊긴 경우 토큰을 비워서 OTP 요청 UI가
        // 안정적으로 노출되도록 한다.
        if (!authedRef.current) {
          try { clearAuthToken(); } catch {}
        }

        setStatus("연결 끊김 / 재연결 중");
        setAuthed(false);
        setAuthMeta({ sessionId: "", telegramConfigured: false });
        scheduleReconnect();
      };

      ws.onerror = () => {
        log("WebSocket 에러", "error");
      };

      ws.onmessage = (event) => {
        if (workerRef.current) {
          workerRef.current.postMessage({ type: "parse_ws", payload: event.data });
        }
      };
    }

    function sendInitialRequests() {
      send({ type: "ping" }, { silent: true, queueIfClosed: false });

      const token = getSavedAuthToken();
      if (token) {
        send({ type: "resume_auth", authToken: token });
      }

      send({ type: "get_settings" });
      const remoteDashboardClient = isRemoteDashboardHost()
        || !!authMetaRef.current?.remoteDashboardClient
        || !!settingsStateRef.current?.remoteDashboardClient;
      if (!remoteDashboardClient) {
        send({ type: "get_copilot_status" });
        send({ type: "get_codex_status" });
      }
      send({ type: "get_groq_models" });
      send({ type: "get_copilot_models" });
      send({ type: "get_usage_stats" });
      send({ type: "list_memory_notes" });
      ["chat", "coding"].forEach((s) => {
        ["single", "orchestration", "multi"].forEach((m) => {
          send({ type: "list_conversations", scope: s, mode: m });
        });
      });
    }

    function scheduleReconnect() {
      if (unmountedRef.current || reconnectTimerRef.current) {
        return;
      }

      reconnectTimerRef.current = setTimeout(() => {
        reconnectTimerRef.current = null;
        connect();
      }, 1200);
    }

    function send(payload, options = {}) {
      return sendWsPayload({
        ws: wsRef.current,
        payload,
        outboundQueue: outboundQueueRef.current,
        queueIfClosed: !!options.queueIfClosed,
        silent: !!options.silent,
        hasOpenedSocket: hasOpenedSocketRef.current,
        log
      });
    }

    function setError(key, value) {
      setErrorByKey((prev) => ({ ...prev, [key]: value || "" }));
    }

    function downloadBase64File(fileName, contentBase64, mimeType) {
      try {
        const binary = window.atob(contentBase64);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index += 1) {
          bytes[index] = binary.charCodeAt(index);
        }
        const blob = new Blob([bytes], { type: mimeType || "application/octet-stream" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName || "download";
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      } catch (error) {
        setBackupState((prev) => ({ ...prev, status: `다운로드 준비 실패: ${error.message || error}` }));
      }
    }

    function beginPendingRequest(key, userText, isCoding, conversationId, requestId = "") {
      const now = Date.now();
      const normalizedConversationId = (conversationId || "").trim();
      setPendingByKey((prev) => ({
        ...prev,
        [key]: {
          active: true,
          conversationId: normalizedConversationId,
          startedUtc: new Date(now).toISOString(),
          updatedAt: now,
          draftText: "",
          provider: "",
          model: "",
          route: "",
          chunkIndex: 0,
          requestId
        }
      }));
      setError(key, "");
      setOptimisticUserByKey((prev) => ({
        ...prev,
        [key]: {
          text: userText,
          createdUtc: new Date(now).toISOString(),
          conversationId: normalizedConversationId
        }
      }));

      if (isCoding) {
        setCodingProgressByKey((prev) => ({
          ...prev,
          [key]: {
            phase: "queued",
            message: "요청 접수됨",
            stageKey: "",
            stageTitle: "",
            stageDetail: "",
            stageIndex: 0,
            stageTotal: 0,
            iteration: 0,
            maxIterations: 0,
            percent: 1,
            done: false,
            provider: "",
            model: "",
            conversationId: normalizedConversationId,
            startedAt: now,
            updatedAt: now
          }
        }));
      }
    }

    function finishPendingRequest(key) {
      setPendingByKey((prev) => {
        const next = { ...prev };
        delete next[key];
        return next;
      });
      setOptimisticUserByKey((prev) => {
        const next = { ...prev };
        delete next[key];
        return next;
      });
      setCodingProgressByKey((prev) => {
        const next = { ...prev };
        delete next[key];
        return next;
      });
    }

    function isRequestPending(key) {
      const pendingEntry = pendingByKey[key];
      return !!(pendingEntry && pendingEntry.active);
    }

    function isConversationBoundEntryVisible(entry, conversationId) {
      if (!entry) {
        return false;
      }

      const entryConversationId = (entry.conversationId || "").trim();
      const targetConversationId = (conversationId || "").trim();
      if (!entryConversationId && !targetConversationId) {
        return true;
      }
      return entryConversationId === targetConversationId;
    }

    function elapsedSeconds(progress) {
      if (!progress || !progress.startedAt) {
        return 0;
      }

      return Math.max(0, Math.floor((clockTick - progress.startedAt) / 1000));
    }

    function humanPath(pathValue, runDir) {
      const value = pathValue || "";
      if (!value) {
        return "-";
      }

      if (runDir && value.startsWith(runDir)) {
        return value.slice(runDir.length).replace(/^\/+/, "");
      }

      return value;
    }

    function sanitizeCodingAssistantText(text) {
      const value = (text || "").replace(/\r\n/g, "\n");
      if (!value) {
        return "";
      }

      return value.replace(/\n{3,}/g, "\n\n").trim();
    }

    function resolveWorkspacePreviewRequestPath(filePath, conversationId) {
      const rawPath = `${filePath || ""}`.trim();
      if (!rawPath) {
        return "";
      }

      if (rawPath.startsWith("/") || /^[A-Za-z]:[\\/]/.test(rawPath)) {
        return rawPath;
      }

      const normalizedConversationId = `${conversationId || currentConversationId || ""}`.trim();
      if (!normalizedConversationId) {
        return rawPath;
      }

      const latestCodingResult = codingResultByConversation[normalizedConversationId];
      const latestRuntime = codingRuntimeByConversation[normalizedConversationId];
      const candidateRunDirectories = [
        latestCodingResult && latestCodingResult.execution && latestCodingResult.execution.runDirectory,
        latestRuntime && latestRuntime.execution && latestRuntime.execution.runDirectory
      ]
        .map((value) => `${value || ""}`.trim())
        .filter(Boolean);

      for (const runDirectory of candidateRunDirectories) {
        const normalizedRunDirectory = runDirectory.replace(/[\\/]+$/, "");
        const normalizedRelativePath = rawPath.replace(/^[\\/]+/, "");
        if (!normalizedRunDirectory || !normalizedRelativePath) {
          continue;
        }

        return `${normalizedRunDirectory}/${normalizedRelativePath}`;
      }

      return rawPath;
    }

    function requestWorkspaceFilePreview(filePath, conversationId) {
      const resolvedPath = resolveWorkspacePreviewRequestPath(filePath, conversationId);
      if (!resolvedPath) {
        return;
      }

      send({
        type: "read_workspace_file",
        filePath: resolvedPath,
        conversationId: conversationId || undefined
      });
    }

    function buildCodingRuntimeMessageState(message, ok, pending = false) {
      return {
        pending,
        ok,
        runMode: "",
        language: "",
        message,
        targetProvider: "",
        targetModel: "",
        previewUrl: "",
        previewEntry: "",
        execution: null,
        updatedAt: Date.now()
      };
    }

    function requestLatestCodingResultExecution(conversationId, standardInput = "") {
      const normalizedConversationId = `${conversationId || currentConversationId || ""}`.trim();
      if (!normalizedConversationId) {
        return;
      }

      if (!authed) {
        setCodingRuntimeByConversation((prev) => ({
          ...prev,
          [normalizedConversationId]: buildCodingRuntimeMessageState(
            "세션 인증이 필요합니다. 설정 탭에서 OTP 인증 후 다시 시도하세요.",
            false,
            false
          )
        }));
        return;
      }

      setCodingRuntimeByConversation((prev) => ({
        ...prev,
        [normalizedConversationId]: buildCodingRuntimeMessageState("최근 코딩 결과를 실행하는 중입니다.", true, true)
      }));
      setShowExecutionLogsByConversation((prev) => ({
        ...prev,
        [normalizedConversationId]: true
      }));

      const ok = send({
        type: "coding_execute_result",
        conversationId: normalizedConversationId,
        stdin: typeof standardInput === "string" ? standardInput : ""
      });
      if (ok) {
        return;
      }

      setCodingRuntimeByConversation((prev) => ({
        ...prev,
        [normalizedConversationId]: buildCodingRuntimeMessageState("오류: WebSocket 연결이 끊어졌습니다.", false, false)
      }));
    }

    function humanWorkspacePath(pathValue) {
      const value = `${pathValue || ""}`.trim();
      if (!value) {
        return "-";
      }

      const workspaceMarker = "/workspace/coding/";
      const markerIndex = value.lastIndexOf(workspaceMarker);
      if (markerIndex >= 0) {
        return value.slice(markerIndex + workspaceMarker.length);
      }

      return value;
    }

    function parseRefactorLineNumber(value) {
      const parsed = Number.parseInt(`${value || ""}`.trim(), 10);
      if (!Number.isFinite(parsed) || parsed < 1) {
        return 0;
      }
      return parsed;
    }

    function getRefactorSelectedLines(state = refactorState) {
      const readResult = state && state.readResult ? state.readResult : null;
      if (!readResult || !Array.isArray(readResult.lines)) {
        return [];
      }

      const start = parseRefactorLineNumber(state.selectedStartLine);
      const end = parseRefactorLineNumber(state.selectedEndLine);
      if (!start || !end) {
        return [];
      }

      const safeStart = Math.min(start, end);
      const safeEnd = Math.max(start, end);
      return readResult.lines.filter((line) => line.lineNumber >= safeStart && line.lineNumber <= safeEnd);
    }

    function inferErrorKey(messageText) {
      const text = (messageText || "").toLowerCase();
      if (text.includes("logic")) {
        return "logic:main";
      }
      if (text.includes("routine")) {
        return "routine:main";
      }
      if (text.includes("chat_single")) {
        return "chat:single";
      }
      if (text.includes("chat_orchestration")) {
        return "chat:orchestration";
      }
      if (text.includes("chat_multi")) {
        return "chat:multi";
      }
      if (text.includes("coding_single") || text.includes("coding_run_single")) {
        return "coding:single";
      }
      if (text.includes("coding_orchestration") || text.includes("coding_run_orchestration")) {
        return "coding:orchestration";
      }
      if (text.includes("coding_multi") || text.includes("coding_run_multi")) {
        return "coding:multi";
      }

      return currentKey;
    }

    function requestConversations(targetScope, targetMode) {
      send(
        { type: "list_conversations", scope: targetScope, mode: targetMode },
        { silent: true, queueIfClosed: false }
      );
    }

    function requestConversationDetail(conversationId) {
      if (!conversationId) {
        return;
      }

      send({ type: "get_conversation", conversationId });
    }

    function parseTags(value) {
      if (!value) {
        return [];
      }

      return value
        .split(",")
        .map((x) => x.trim())
        .filter((x) => x.length > 0);
    }

    function createConversation(targetScope, targetMode, title, project, category, tags) {
      return send({
        type: "create_conversation",
        scope: targetScope,
        mode: targetMode,
        conversationTitle: title || `${targetScope}-${targetMode}-${new Date().toLocaleTimeString("ko-KR", { hour12: false })}`,
        project: (project || "").trim() || undefined,
        category: (category || "").trim() || undefined,
        tags: Array.isArray(tags) && tags.length > 0 ? tags : undefined
      });
    }

    function requestAutoCreateConversation(targetScope, targetMode, title, project, category, tags) {
      const normalizedScope = targetScope || "chat";
      const normalizedMode = targetMode || "single";
      const key = `${normalizedScope}:${normalizedMode}`;
      if (autoCreateConversationRef.current[key]) {
        return;
      }

      autoCreateConversationRef.current[key] = true;
      const sent = createConversation(normalizedScope, normalizedMode, title, project, category, tags);
      if (!sent) {
        autoCreateConversationRef.current[key] = false;
      }
    }

    function saveConversationMeta() {
      if (!currentConversationId) {
        return;
      }

      const baseTitle = (currentConversation && currentConversation.title ? currentConversation.title : "").trim();
      const nextTitle = metaTitle.trim() || baseTitle || "제목 없음";
      const nextProject = metaProject.trim() || "기본";
      const nextCategory = metaCategory.trim() || "일반";
      const nextTags = parseTags(metaTags);
      setMetaTitle(nextTitle);

      setConversationDetails((prev) => {
        const base = prev[currentConversationId];
        if (!base) {
          return prev;
        }

        return {
          ...prev,
          [currentConversationId]: {
            ...base,
            title: nextTitle,
            project: nextProject,
            category: nextCategory,
            tags: nextTags
          }
        };
      });
      setConversationLists((prev) => {
        let changed = false;
        const next = {};
        Object.keys(prev).forEach((key) => {
          const list = Array.isArray(prev[key]) ? prev[key] : [];
          const updated = list.map((item) => {
            if (item.id !== currentConversationId) {
              return item;
            }
            changed = true;
            return {
              ...item,
              title: nextTitle,
              project: nextProject,
              category: nextCategory,
              tags: nextTags
            };
          });
          next[key] = updated;
        });
        return changed ? next : prev;
      });

      send({
        type: "update_conversation_meta",
        conversationId: currentConversationId,
        conversationTitle: nextTitle,
        project: nextProject,
        category: nextCategory,
        tags: nextTags
      });
    }

    function deleteConversation() {
      const targetIds = selectionMode
        ? selectedDeleteConversationIds
        : (currentConversationId ? [currentConversationId] : []);
      if (targetIds.length === 0) {
        return;
      }

      const folderCount = currentSelectedFolders.length;
      const conversationCount = currentSelectedConversationIds.length;
      const message = selectionMode
        ? `선택한 항목을 삭제할까요?\n폴더 ${folderCount}개, 대화 ${conversationCount}개 선택됨\n실제 삭제 대상 대화 ${targetIds.length}개`
        : "현재 대화를 삭제할까요?";
      const confirmed = window.confirm(message);
      if (!confirmed) {
        return;
      }

      targetIds.forEach((conversationId) => {
        send({
          type: "delete_conversation",
          scope,
          mode,
          conversationId
        }, { silent: true, queueIfClosed: false });
      });

      if (selectionMode) {
        setSelectedConversationIdsByKey((prev) => ({ ...prev, [currentKey]: [] }));
        setSelectedFoldersByKey((prev) => ({ ...prev, [currentKey]: [] }));
      }
    }

    function clearScopeMemory(targetScope) {
      if (!ensureAuthed()) {
        return;
      }

      const normalizedScope = String(targetScope || scope || "chat").toLowerCase();
      const label = normalizedScope === "coding" ? "코딩 탭" : "대화 탭(+텔레그램)";
      const confirmed = window.confirm(`${label} 메모리를 초기화할까요?\n대화 이력과 메모리 노트가 삭제됩니다.`);
      if (!confirmed) {
        return;
      }

      send({
        type: "clear_memory",
        scope: normalizedScope
      });
    }

    function createManualMemoryNote(compactConversation = false) {
      if (!currentConversationId) {
        return;
      }

      send({
        type: "create_memory_note",
        conversationId: currentConversationId,
        compactConversation
      });
    }

    function renameMemoryNote(noteName) {
      const currentName = String(noteName || "").trim();
      if (!currentName) {
        return;
      }

      const nextName = window.prompt("새 메모리 노트 이름", currentName);
      if (typeof nextName !== "string") {
        return;
      }

      const trimmed = nextName.trim();
      if (!trimmed || trimmed === currentName) {
        return;
      }

      send({
        type: "rename_memory_note",
        noteName: currentName,
        newName: trimmed
      });
    }

    function deleteSelectedMemoryNotes() {
      if (currentCheckedMemoryNotes.length === 0) {
        return;
      }

      const confirmed = window.confirm(`체크된 메모리 노트 ${currentCheckedMemoryNotes.length}개를 삭제할까요?`);
      if (!confirmed) {
        return;
      }

      send({
        type: "delete_memory_notes",
        memoryNotes: currentCheckedMemoryNotes
      });
    }

    function selectConversation(item) {
      const key = `${item.scope}:${item.mode}`;
      setActiveConversationByKey((prev) => ({ ...prev, [key]: item.id }));
      if (isPortraitMobileLayout) {
        setResponsivePane(item.scope === "coding" ? "coding" : "chat", "thread");
      }
      requestConversationDetail(item.id);
    }

    function buildThreadPreviewMeta() {
      const previewTags = parseTags(metaTags).slice(0, 6);
      const previewProject = metaProject.trim() || "기본";
      const previewCategory = metaCategory.trim() || "일반";
      return {
        previewTags,
        previewProject,
        previewCategory
      };
    }

    function toggleSelectionMode() {
      const nextValue = !selectionMode;
      setSelectionModeByKey((prev) => ({ ...prev, [currentKey]: nextValue }));
      if (!nextValue) {
        setSelectedConversationIdsByKey((prev) => ({ ...prev, [currentKey]: [] }));
        setSelectedFoldersByKey((prev) => ({ ...prev, [currentKey]: [] }));
      }
    }

    function toggleFolderSelection(projectName) {
      const normalized = (projectName || "기본").trim() || "기본";
      setSelectedFoldersByKey((prev) => {
        const base = Array.isArray(prev[currentKey]) ? prev[currentKey] : [];
        const next = base.includes(normalized)
          ? base.filter((item) => item !== normalized)
          : base.concat([normalized]);
        return { ...prev, [currentKey]: next };
      });
    }

    function toggleConversationSelection(conversationId) {
      const normalized = String(conversationId || "").trim();
      if (!normalized) {
        return;
      }

      setSelectedConversationIdsByKey((prev) => {
        const base = Array.isArray(prev[currentKey]) ? prev[currentKey] : [];
        const next = base.includes(normalized)
          ? base.filter((item) => item !== normalized)
          : base.concat([normalized]);
        return { ...prev, [currentKey]: next };
      });
    }

    function buildFolderKey(scopeModeKey, projectName) {
      const project = (projectName || "기본").trim() || "기본";
      return `${scopeModeKey}::${project}`;
    }

    function toggleFolder(scopeModeKey, projectName) {
      const key = buildFolderKey(scopeModeKey, projectName);
      setExpandedFoldersByKey((prev) => ({ ...prev, [key]: !prev[key] }));
    }

    function isFolderExpanded(scopeModeKey, projectName) {
      return !!expandedFoldersByKey[buildFolderKey(scopeModeKey, projectName)];
    }

    function ensureAuthed() {
      if (authed) {
        return true;
      }

      setError(rootTab === "routine" ? "routine:main" : currentKey, "OTP 인증 후 사용 가능합니다.");
      return false;
    }

    function sendChatSingle() {
      if (!ensureAuthed()) {
        return;
      }

      const text = chatInputSingle.trim();
      const rich = getRichInputPayload(text);
      const effectiveText = text || (rich.attachments.length > 0 ? "첨부 파일을 분석해줘" : "");
      if (!effectiveText) {
        return;
      }

      const model = chatSingleProvider === "groq"
        ? (chatSingleModel || selectedGroqModel || undefined)
        : chatSingleProvider === "copilot"
          ? (chatSingleModel || selectedCopilotModel || undefined)
          : chatSingleProvider === "codex"
            ? (chatSingleModel || DEFAULT_CODEX_MODEL)
          : chatSingleProvider === "gemini"
            ? (isNoneModel(chatSingleModel) ? DEFAULT_GEMINI_WORKER_MODEL : (chatSingleModel || DEFAULT_GEMINI_WORKER_MODEL))
          : chatSingleProvider === "cerebras"
            ? (chatSingleModel || DEFAULT_CEREBRAS_MODEL)
          : chatSingleProvider === "nvidia"
            ? (chatSingleModel || DEFAULT_NVIDIA_MODEL)
          : undefined;
      const conversationId = activeConversationByKey["chat:single"] || "";
      const requestId = buildRequestId("chat-single");
      beginPendingRequest("chat:single", effectiveText, false, conversationId, requestId);
      setChatInputSingle("");

      const ok = send({
        type: "llm_chat_single",
        scope: "chat",
        mode: "single",
        conversationId: conversationId || undefined,
        requestId,
        text: effectiveText,
        project: metaProject.trim() || "기본",
        category: metaCategory.trim() || "일반",
        tags: parseTags(metaTags),
        provider: chatSingleProvider,
        model,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled,
        skillName: selectedChatSkill?.name || undefined,
        skillScope: selectedChatSkill?.scope || undefined,
        thinkPlus: !!thinkPlusModeByKey["chat:single"]
      });

      if (!ok) {
        finishPendingRequest("chat:single");
        setError("chat:single", "오류: WebSocket 연결이 끊어졌습니다.");
      } else {
        clearRichInputDraft("chat:single");
      }
    }

    function appendTextToCurrentComposer(text) {
      const value = `${text || ""}`.trim();
      if (!value) {
        return;
      }

      const append = (prev) => {
        const base = `${prev || ""}`.trimEnd();
        return base ? `${base}\n${value}` : value;
      };
      if (currentKey === "chat:single") {
        setChatInputSingle(append);
      } else if (currentKey === "chat:orchestration") {
        setChatInputOrch(append);
      } else if (currentKey === "chat:multi") {
        setChatInputMulti(append);
      } else if (currentKey === "coding:single") {
        setCodingInputSingle(append);
      } else if (currentKey === "coding:orchestration") {
        setCodingInputOrch(append);
      } else if (currentKey === "coding:multi") {
        setCodingInputMulti(append);
      }
    }

    function getComposerTextByKey(key) {
      if (key === "chat:single") {
        return chatInputSingle;
      }
      if (key === "chat:orchestration") {
        return chatInputOrch;
      }
      if (key === "chat:multi") {
        return chatInputMulti;
      }
      if (key === "coding:single") {
        return codingInputSingle;
      }
      if (key === "coding:orchestration") {
        return codingInputOrch;
      }
      if (key === "coding:multi") {
        return codingInputMulti;
      }
      return "";
    }

    function setComposerTextByKey(key, value) {
      const nextValue = `${value || ""}`;
      if (key === "chat:single") {
        setChatInputSingle(nextValue);
      } else if (key === "chat:orchestration") {
        setChatInputOrch(nextValue);
      } else if (key === "chat:multi") {
        setChatInputMulti(nextValue);
      } else if (key === "coding:single") {
        setCodingInputSingle(nextValue);
      } else if (key === "coding:orchestration") {
        setCodingInputOrch(nextValue);
      } else if (key === "coding:multi") {
        setCodingInputMulti(nextValue);
      }
    }

    function buildSpeechComposerText(baseText, transcript) {
      const base = `${baseText || ""}`.trimEnd();
      const spoken = `${transcript || ""}`.trim();
      if (!spoken) {
        return base;
      }
      return base ? `${base}\n${spoken}` : spoken;
    }

    function extractSpeechTranscript(event) {
      const results = event?.results || [];
      const parts = [];
      for (let index = 0; index < results.length; index += 1) {
        const item = results[index];
        const text = item && item[0] ? `${item[0].transcript || ""}`.trim() : "";
        if (text) {
          parts.push(text);
        }
      }
      return parts.join(" ").replace(/\s+/g, " ").trim();
    }

    function getSpeechErrorMessage(errorName) {
      const normalized = `${errorName || ""}`.trim().toLowerCase();
      if (normalized === "not-allowed"
        || normalized === "service-not-allowed"
        || normalized === "notallowederror"
        || normalized === "securityerror"
        || normalized === "permissiondeniederror") {
        return "마이크 권한이 차단되었습니다. 브라우저 권한을 허용하세요.";
      }
      if (normalized === "audio-capture"
        || normalized === "notfounderror"
        || normalized === "devicesnotfounderror") {
        return "사용 가능한 마이크를 찾지 못했습니다. 마이크 연결 상태를 확인하세요.";
      }
      if (normalized === "notreadableerror" || normalized === "trackstarterror") {
        return "마이크를 다른 앱에서 사용 중이거나 접근할 수 없습니다.";
      }
      if (normalized === "network") {
        return "음성 인식 서비스에 연결하지 못했습니다. 네트워크 상태를 확인하세요.";
      }
      if (normalized === "no-speech" || normalized === "aborted" || normalized === "aborterror") {
        return "입력된 음성이 없습니다. 다시 눌러 말하세요.";
      }
      if (normalized === "invalid-state" || normalized === "invalidstateerror") {
        return "이미 음성 입력이 실행 중입니다.";
      }
      return "음성 입력을 시작하지 못했습니다.";
    }

    function stopVoiceInput() {
      const recognition = speechRecognitionRef.current;
      if (!recognition) {
        setSpeechState((prev) => ({ ...prev, active: false }));
        return;
      }

      speechStopRequestedRef.current = true;
      try {
        recognition.stop();
      } catch {
        try {
          recognition.abort();
        } catch {
        }
        speechRecognitionRef.current = null;
        setSpeechState((prev) => ({ ...prev, active: false }));
      }
    }

    function readMicrophonePermission() {
      if (typeof navigator === "undefined" || !navigator.permissions?.query) {
        return Promise.resolve("");
      }

      return navigator.permissions.query({ name: "microphone" })
        .then((permission) => permission?.state || "")
        .catch(() => "");
    }

    async function startVoiceInput() {
      const Recognition = typeof window !== "undefined"
        ? (window.SpeechRecognition || window.webkitSpeechRecognition)
        : null;
      if (!Recognition) {
        setSpeechState((prev) => ({ ...prev, active: false, supported: false, error: "이 브라우저는 음성 입력을 지원하지 않습니다." }));
        return;
      }

      if (speechRecognitionRef.current) {
        stopVoiceInput();
        return;
      }

      const recognition = new Recognition();
      recognition.lang = uiPreferences.speech?.lang || "ko-KR";
      recognition.interimResults = true;
      recognition.continuous = false;
      recognition.maxAlternatives = 1;
      const targetKey = currentKeyRef.current || currentKey;
      speechTargetKeyRef.current = targetKey;
      speechRecognitionRef.current = recognition;
      speechTranscriptRef.current = "";
      speechBaseDraftRef.current = getComposerTextByKey(targetKey);
      speechStopRequestedRef.current = false;
      setSpeechState((prev) => ({ ...prev, active: true, supported: true, error: "" }));
      recognition.onresult = (event) => {
        const transcript = extractSpeechTranscript(event);
        const key = speechTargetKeyRef.current || targetKey;
        speechTranscriptRef.current = transcript;
        setComposerTextByKey(key, buildSpeechComposerText(speechBaseDraftRef.current, transcript));
      };
      recognition.onerror = (event) => {
        const errorName = event.error || "";
        const quietStop = speechStopRequestedRef.current && (errorName === "aborted" || !errorName);
        setSpeechState((prev) => ({
          ...prev,
          active: false,
          error: quietStop ? "" : getSpeechErrorMessage(errorName)
        }));
      };
      recognition.onend = () => {
        const stoppedByUser = speechStopRequestedRef.current;
        const hadTranscript = !!`${speechTranscriptRef.current || ""}`.trim();
        speechRecognitionRef.current = null;
        speechStopRequestedRef.current = false;
        setSpeechState((prev) => ({
          ...prev,
          active: false,
          error: stoppedByUser || hadTranscript ? prev.error : (prev.error || "입력된 음성이 없습니다. 다시 눌러 말하세요.")
        }));
        // 자동 전송: 음성 인식 결과가 있으면 composer에 채워진 내용을 그대로 전송.
        // 사용자 의도("음성 → 자동 전송"). chat/coding 탭에서만.
        // ref로 최신 sendCurrentComposer 호출해서 stale closure / state 회피.
        if (hadTranscript) {
          setTimeout(() => {
            try {
              const fn = sendCurrentComposerRef.current;
              if (typeof fn === "function") {
                fn();
              }
            } catch {}
          }, 200);
        }
      };
      try {
        recognition.start();
        readMicrophonePermission().then((state) => {
          if (state === "denied" && speechRecognitionRef.current === recognition) {
            try {
              recognition.abort();
            } catch {
            }
            speechRecognitionRef.current = null;
            setSpeechState((prev) => ({
              ...prev,
              active: false,
              supported: true,
              error: "마이크 권한이 차단되었습니다. 브라우저 권한을 허용하세요."
            }));
          }
        });
      } catch (error) {
        speechRecognitionRef.current = null;
        setSpeechState((prev) => ({
          ...prev,
          active: false,
          error: getSpeechErrorMessage(error?.name || "invalid-state")
        }));
      }
    }

    function requestBackupExport() {
      if (!ensureAuthed()) {
        return;
      }

      setBackupState((prev) => ({ ...prev, exportBusy: true, status: "백업 파일을 만드는 중입니다." }));
      if (!send({ type: "backup_export_prepare" })) {
        setBackupState((prev) => ({ ...prev, exportBusy: false, status: "백업 요청 전송에 실패했습니다." }));
      }
    }

    function handleBackupFileSelected(event) {
      const file = event.target.files && event.target.files[0];
      if (!file) {
        return;
      }

      if (!ensureAuthed()) {
        event.target.value = "";
        return;
      }

      setBackupState((prev) => ({ ...prev, importBusy: true, preview: null, status: "백업 파일을 읽는 중입니다." }));
      const reader = new FileReader();
      reader.onload = () => {
        const raw = `${reader.result || ""}`;
        const base64 = raw.includes(",") ? raw.split(",").pop() : raw;
        const ok = send({
          type: "backup_import_preview",
          fileName: file.name,
          contentBase64: base64
        });
        if (!ok) {
          setBackupState((prev) => ({ ...prev, importBusy: false, status: "가져오기 미리보기 요청에 실패했습니다." }));
        }
      };
      reader.onerror = () => {
        setBackupState((prev) => ({ ...prev, importBusy: false, status: "백업 파일을 읽을 수 없습니다." }));
      };
      reader.readAsDataURL(file);
      event.target.value = "";
    }

    function applyBackupImport(overwrite = false) {
      if (!ensureAuthed() || !backupState.preview?.previewId) {
        return;
      }

      setBackupState((prev) => ({ ...prev, importBusy: true, status: overwrite ? "겹치는 항목을 덮어쓰는 중입니다." : "새 항목만 가져오는 중입니다." }));
      if (!send({ type: "backup_import_apply", previewId: backupState.preview.previewId, overwrite })) {
        setBackupState((prev) => ({ ...prev, importBusy: false, status: "가져오기 적용 요청에 실패했습니다." }));
      }
    }

    function requestConversationSearch(queryValue = currentConversationFilter, key = currentKey) {
      const query = `${queryValue || ""}`.trim();
      if (!ensureAuthed() || query.length < 2) {
        return false;
      }

      setConversationSearchByKey((prev) => ({
        ...prev,
        [key]: {
          ...(prev[key] || {}),
          query,
          loading: true,
          error: ""
        }
      }));
      return send({ type: "conversation_search", query, maxResults: 24 }, { silent: true });
    }

    function clearCurrentConversationSearch() {
      setConversationFilterByKey((prev) => ({ ...prev, [currentKey]: "" }));
      setConversationSearchByKey((prev) => ({
        ...prev,
        [currentKey]: { query: "", loading: false, results: [], error: "" }
      }));
    }

    function sendChatOrchestration() {
      if (!ensureAuthed()) {
        return;
      }

      const text = chatInputOrch.trim();
      const rich = getRichInputPayload(text);
      const effectiveText = text || (rich.attachments.length > 0 ? "첨부 파일을 분석해줘" : "");
      if (!effectiveText) {
        return;
      }

      const model = chatOrchProvider === "groq"
        ? (chatOrchModel || selectedGroqModel || undefined)
        : chatOrchProvider === "copilot"
          ? (chatOrchModel || selectedCopilotModel || undefined)
          : chatOrchProvider === "codex"
            ? (chatOrchModel || chatOrchCodexModel || DEFAULT_CODEX_MODEL)
          : chatOrchProvider === "gemini"
            ? ((!isNoneModel(chatOrchModel) ? chatOrchModel : "")
              || (!isNoneModel(chatOrchGeminiModel) ? chatOrchGeminiModel : DEFAULT_GEMINI_WORKER_MODEL))
          : chatOrchProvider === "nvidia"
            ? ((!isNoneModel(chatOrchModel) ? chatOrchModel : "")
              || (!isNoneModel(chatOrchNvidiaModel) ? chatOrchNvidiaModel : DEFAULT_NVIDIA_MODEL))
            : chatOrchProvider === "cerebras"
              ? (chatOrchModel || chatOrchCerebrasModel || DEFAULT_CEREBRAS_MODEL)
            : undefined;
      const workerGroqModel = normalizeModelChoice(chatOrchGroqModel, DEFAULT_GROQ_WORKER_MODEL);
      const workerGeminiModel = normalizeModelChoice(chatOrchGeminiModel, DEFAULT_GEMINI_WORKER_MODEL);
      const workerCerebrasModel = normalizeModelChoice(chatOrchCerebrasModel, DEFAULT_CEREBRAS_MODEL);
      const workerNvidiaModel = normalizeModelChoice(chatOrchNvidiaModel, DEFAULT_NVIDIA_MODEL);
      const workerCopilotModel = normalizeModelChoice(chatOrchCopilotModel, NONE_MODEL);
      const workerCodexModel = normalizeModelChoice(chatOrchCodexModel, DEFAULT_CODEX_MODEL);
      const conversationId = activeConversationByKey["chat:orchestration"] || "";
      beginPendingRequest("chat:orchestration", effectiveText, false, conversationId);
      setChatInputOrch("");

      const ok = send({
        type: "llm_chat_orchestration",
        scope: "chat",
        mode: "orchestration",
        conversationId: conversationId || undefined,
        text: effectiveText,
        project: metaProject.trim() || "기본",
        category: metaCategory.trim() || "일반",
        tags: parseTags(metaTags),
        provider: chatOrchProvider,
        model,
        groqModel: workerGroqModel,
        geminiModel: workerGeminiModel,
        cerebrasModel: workerCerebrasModel,
        nvidiaModel: workerNvidiaModel,
        copilotModel: workerCopilotModel,
        codexModel: workerCodexModel,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled,
        thinkPlus: !!thinkPlusModeByKey["chat:orchestration"],
        skillName: selectedChatSkill?.name || undefined,
        skillScope: selectedChatSkill?.scope || undefined
      });

      if (!ok) {
        finishPendingRequest("chat:orchestration");
        setError("chat:orchestration", "오류: WebSocket 연결이 끊어졌습니다.");
      } else {
        clearRichInputDraft("chat:orchestration");
      }
    }

    function sendChatMulti() {
      if (!ensureAuthed()) {
        return;
      }

      const text = chatInputMulti.trim();
      const rich = getRichInputPayload(text);
      const effectiveText = text || (rich.attachments.length > 0 ? "첨부 파일을 분석해줘" : "");
      if (!effectiveText) {
        return;
      }

      const conversationId = activeConversationByKey["chat:multi"] || "";
      beginPendingRequest("chat:multi", effectiveText, false, conversationId);
      setChatInputMulti("");

      const ok = send({
        type: "llm_chat_multi",
        scope: "chat",
        mode: "multi",
        conversationId: conversationId || undefined,
        text: effectiveText,
        project: metaProject.trim() || "기본",
        category: metaCategory.trim() || "일반",
        tags: parseTags(metaTags),
        groqModel: normalizeModelChoice(chatMultiGroqModel, DEFAULT_GROQ_WORKER_MODEL),
        geminiModel: normalizeModelChoice(chatMultiGeminiModel, DEFAULT_GEMINI_WORKER_MODEL),
        cerebrasModel: normalizeModelChoice(chatMultiCerebrasModel, DEFAULT_CEREBRAS_MODEL),
        nvidiaModel: normalizeModelChoice(chatMultiNvidiaModel, DEFAULT_NVIDIA_MODEL),
        copilotModel: normalizeModelChoice(chatMultiCopilotModel, NONE_MODEL),
        codexModel: normalizeModelChoice(chatMultiCodexModel, DEFAULT_CODEX_MODEL),
        summaryProvider: chatMultiSummaryProvider,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled,
        thinkPlus: !!thinkPlusModeByKey["chat:multi"],
        skillName: selectedChatSkill?.name || undefined,
        skillScope: selectedChatSkill?.scope || undefined
      });

      if (!ok) {
        finishPendingRequest("chat:multi");
        setError("chat:multi", "오류: WebSocket 연결이 끊어졌습니다.");
      } else {
        clearRichInputDraft("chat:multi");
      }
    }

    function sendCodingSingle() {
      if (!ensureAuthed()) {
        return;
      }

      const text = codingInputSingle.trim();
      const rich = getRichInputPayload(text);
      const effectiveText = text || (rich.attachments.length > 0 ? "첨부 파일을 반영해 코딩해줘" : "");
      if (!effectiveText) {
        return;
      }

      const conversationId = activeConversationByKey["coding:single"] || "";
      beginPendingRequest("coding:single", effectiveText, true, conversationId);
      setCodingInputSingle("");

      const ok = send({
        type: "coding_run_single",
        scope: "coding",
        mode: "single",
        conversationId: conversationId || undefined,
        text: effectiveText,
        project: metaProject.trim() || "기본",
        category: metaCategory.trim() || "일반",
        tags: parseTags(metaTags),
        provider: codingSingleProvider,
        model: codingSingleProvider === "gemini"
          ? (isNoneModel(codingSingleModel) ? DEFAULT_GEMINI_WORKER_MODEL : (codingSingleModel || DEFAULT_GEMINI_WORKER_MODEL))
          : codingSingleProvider === "codex"
            ? (codingSingleModel || DEFAULT_CODEX_MODEL)
          : codingSingleProvider === "nvidia"
            ? (codingSingleModel || DEFAULT_NVIDIA_MODEL)
            : (codingSingleModel || undefined),
        language: codingSingleLanguage,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled,
        thinkPlus: !!thinkPlusModeByKey["coding:single"],
        skillName: selectedChatSkill?.name || undefined,
        skillScope: selectedChatSkill?.scope || undefined
      });

      if (!ok) {
        finishPendingRequest("coding:single");
        setError("coding:single", "오류: WebSocket 연결이 끊어졌습니다.");
      } else {
        clearRichInputDraft("coding:single");
      }
    }

    function sendCodingOrchestration() {
      if (!ensureAuthed()) {
        return;
      }

      const text = codingInputOrch.trim();
      const rich = getRichInputPayload(text);
      const pendingLabel = text || (rich.attachments.length > 0 ? "첨부 파일 반영 코딩" : "(입력 없음) 워커 자동 역할 협의 모드");

      const aggregateModel = codingOrchProvider === "groq"
        ? (codingOrchModel || selectedGroqModel || undefined)
        : codingOrchProvider === "copilot"
          ? (codingOrchModel || selectedCopilotModel || undefined)
          : codingOrchProvider === "codex"
            ? (codingOrchModel || codingOrchCodexModel || DEFAULT_CODEX_MODEL)
          : codingOrchProvider === "cerebras"
            ? (codingOrchModel || codingOrchCerebrasModel || DEFAULT_CEREBRAS_MODEL)
          : codingOrchProvider === "nvidia"
            ? ((!isNoneModel(codingOrchModel) ? codingOrchModel : "")
              || (!isNoneModel(codingOrchNvidiaModel) ? codingOrchNvidiaModel : DEFAULT_NVIDIA_MODEL))
          : codingOrchProvider === "gemini"
            ? ((!isNoneModel(codingOrchModel) ? codingOrchModel : "")
              || (!isNoneModel(codingOrchGeminiModel) ? codingOrchGeminiModel : DEFAULT_GEMINI_WORKER_MODEL))
            : undefined;
      const effectiveText = text || (rich.attachments.length > 0 ? "첨부 파일을 반영해 코딩해줘" : "");

      const conversationId = activeConversationByKey["coding:orchestration"] || "";
      beginPendingRequest("coding:orchestration", pendingLabel, true, conversationId);
      setCodingInputOrch("");

      const ok = send({
        type: "coding_run_orchestration",
        scope: "coding",
        mode: "orchestration",
        conversationId: conversationId || undefined,
        text: effectiveText,
        project: metaProject.trim() || "기본",
        category: metaCategory.trim() || "일반",
        tags: parseTags(metaTags),
        provider: codingOrchProvider,
        model: aggregateModel,
        groqModel: normalizeModelChoice(codingOrchGroqModel, DEFAULT_GROQ_WORKER_MODEL),
        geminiModel: normalizeModelChoice(codingOrchGeminiModel, DEFAULT_GEMINI_WORKER_MODEL),
        cerebrasModel: normalizeModelChoice(codingOrchCerebrasModel, DEFAULT_CEREBRAS_MODEL),
        nvidiaModel: normalizeModelChoice(codingOrchNvidiaModel, DEFAULT_NVIDIA_MODEL),
        copilotModel: normalizeModelChoice(codingOrchCopilotModel, NONE_MODEL),
        codexModel: normalizeModelChoice(codingOrchCodexModel, DEFAULT_CODEX_MODEL),
        language: codingOrchLanguage,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled,
        thinkPlus: !!thinkPlusModeByKey["coding:orchestration"],
        skillName: selectedChatSkill?.name || undefined,
        skillScope: selectedChatSkill?.scope || undefined
      });

      if (!ok) {
        finishPendingRequest("coding:orchestration");
        setError("coding:orchestration", "오류: WebSocket 연결이 끊어졌습니다.");
      } else {
        clearRichInputDraft("coding:orchestration");
      }
    }

    function sendCodingMulti() {
      if (!ensureAuthed()) {
        return;
      }

      const text = codingInputMulti.trim();
      const rich = getRichInputPayload(text);
      const effectiveText = text || (rich.attachments.length > 0 ? "첨부 파일을 반영해 코딩해줘" : "");
      if (!effectiveText) {
        return;
      }

      const conversationId = activeConversationByKey["coding:multi"] || "";
      beginPendingRequest("coding:multi", effectiveText, true, conversationId);
      setCodingInputMulti("");

      const ok = send({
        type: "coding_run_multi",
        scope: "coding",
        mode: "multi",
        conversationId: conversationId || undefined,
        text: effectiveText,
        project: metaProject.trim() || "기본",
        category: metaCategory.trim() || "일반",
        tags: parseTags(metaTags),
        provider: codingMultiProvider,
        model: codingMultiProvider === "gemini"
          ? (isNoneModel(codingMultiModel) ? DEFAULT_GEMINI_WORKER_MODEL : (codingMultiModel || DEFAULT_GEMINI_WORKER_MODEL))
          : codingMultiProvider === "codex"
            ? (codingMultiModel || DEFAULT_CODEX_MODEL)
          : codingMultiProvider === "nvidia"
            ? (isNoneModel(codingMultiModel) ? undefined : (codingMultiModel || DEFAULT_NVIDIA_MODEL))
          : (isNoneModel(codingMultiModel) ? undefined : (codingMultiModel || undefined)),
        groqModel: normalizeModelChoice(codingMultiGroqModel, DEFAULT_GROQ_WORKER_MODEL),
        geminiModel: normalizeModelChoice(codingMultiGeminiModel, DEFAULT_GEMINI_WORKER_MODEL),
        cerebrasModel: normalizeModelChoice(codingMultiCerebrasModel, DEFAULT_CEREBRAS_MODEL),
        nvidiaModel: normalizeModelChoice(codingMultiNvidiaModel, DEFAULT_NVIDIA_MODEL),
        copilotModel: normalizeModelChoice(codingMultiCopilotModel, NONE_MODEL),
        codexModel: normalizeModelChoice(codingMultiCodexModel, DEFAULT_CODEX_MODEL),
        language: codingMultiLanguage,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled,
        thinkPlus: !!thinkPlusModeByKey["coding:multi"],
        skillName: selectedChatSkill?.name || undefined,
        skillScope: selectedChatSkill?.scope || undefined
      });

      if (!ok) {
        finishPendingRequest("coding:multi");
        setError("coding:multi", "오류: WebSocket 연결이 끊어졌습니다.");
      } else {
        clearRichInputDraft("coding:multi");
      }
    }

    function sendToolControlRequest(payload, requestLabel) {
      if (!ensureAuthed()) {
        return;
      }

      setToolControlError("");
      const ok = send(payload);
      if (!ok) {
        setToolControlError("오류: WebSocket 연결이 끊어졌습니다.");
        return;
      }

      if (requestLabel) {
        log(`[tool-control] ${requestLabel}`);
      }
    }

    function submitSessionsList() {
      sendToolControlRequest(
        {
          type: "sessions_list",
          limit: 8,
          messageLimit: 2
        },
        "sessions_list"
      );
    }

    function submitSessionsHistory() {
      const key = toolSessionKey.trim();
      if (!key) {
        setToolControlError("sessions_history 실행에는 sessionKey가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "sessions_history",
          sessionKey: key,
          limit: 20,
          includeTools: false
        },
        "sessions_history"
      );
    }

    function submitSessionSpawn() {
      const task = toolSpawnTask.trim();
      if (!task) {
        setToolControlError("sessions_spawn 실행에는 task가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "sessions_spawn",
          task,
          mode: "run",
          timeoutSeconds: 0
        },
        "sessions_spawn"
      );
    }

    function submitSessionSend() {
      const key = toolSessionKey.trim();
      const outbound = toolSessionMessage.trim();
      if (!key) {
        setToolControlError("sessions_send 실행에는 sessionKey가 필요합니다.");
        return;
      }
      if (!outbound) {
        setToolControlError("sessions_send 실행에는 message가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "sessions_send",
          sessionKey: key,
          message: outbound,
          timeoutSeconds: 30
        },
        "sessions_send"
      );
    }

    function submitCronStatus() {
      sendToolControlRequest({ type: "cron", action: "status" }, "cron.status");
    }

    function submitCronList() {
      sendToolControlRequest(
        {
          type: "cron",
          action: "list",
          includeDisabled: true,
          limit: 10,
          offset: 0
        },
        "cron.list"
      );
    }

    function submitCronRun() {
      const jobId = toolCronJobId.trim();
      if (!jobId) {
        setToolControlError("cron.run 실행에는 jobId가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "cron",
          action: "run",
          jobId,
          runMode: "force"
        },
        "cron.run"
      );
    }

    function submitBrowserStatus() {
      sendToolControlRequest({ type: "browser", action: "status" }, "browser.status");
    }

    function submitBrowserNavigate() {
      const url = toolBrowserUrl.trim();
      if (!url) {
        setToolControlError("browser.navigate 실행에는 URL이 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "browser",
          action: "navigate",
          url
        },
        "browser.navigate"
      );
    }

    function submitCanvasStatus() {
      sendToolControlRequest({ type: "canvas", action: "status" }, "canvas.status");
    }

    function submitCanvasPresent() {
      sendToolControlRequest(
        {
          type: "canvas",
          action: "present",
          target: toolCanvasTarget.trim() || "main"
        },
        "canvas.present"
      );
    }

    function submitNodesStatus() {
      sendToolControlRequest(
        {
          type: "nodes",
          action: "status",
          node: toolNodesNode.trim() || undefined
        },
        "nodes.status"
      );
    }

    function submitNodesPending() {
      sendToolControlRequest(
        {
          type: "nodes",
          action: "pending",
          node: toolNodesNode.trim() || undefined
        },
        "nodes.pending"
      );
    }

    function submitNodesInvoke() {
      const commandText = toolNodesInvokeCommand.trim();
      if (!commandText) {
        setToolControlError("nodes.invoke 실행에는 invokeCommand가 필요합니다.");
        return;
      }

      const rawParams = toolNodesInvokeParamsJson.trim() || "{}";
      let parsedParams;
      try {
        parsedParams = JSON.parse(rawParams);
      } catch (_err) {
        setToolControlError("nodes.invoke params는 유효한 JSON이어야 합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "nodes",
          action: "invoke",
          node: toolNodesNode.trim() || undefined,
          requestId: toolNodesRequestId.trim() || undefined,
          invokeCommand: commandText,
          invokeParamsJson: parsedParams
        },
        "nodes.invoke"
      );
    }

    function submitTelegramStubCommand() {
      const text = toolTelegramStubText.trim();
      if (!text) {
        setToolControlError("telegram stub 실행에는 text가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "telegram_stub_command",
          text
        },
        "telegram_stub.command"
      );
    }

    function submitWebSearchProbe() {
      const query = toolWebSearchQuery.trim();
      if (!query) {
        setToolControlError("web_search 실행에는 query가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "web_search",
          query,
          count: 5,
          freshness: "pd"
        },
        "web_search"
      );
    }

    function submitWebFetchProbe() {
      const url = toolWebFetchUrl.trim();
      if (!url) {
        setToolControlError("web_fetch 실행에는 url이 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "web_fetch",
          url,
          extractMode: "markdown",
          maxChars: 2400
        },
        "web_fetch"
      );
    }

    function submitMemorySearchProbe() {
      const query = toolMemorySearchQuery.trim();
      if (!query) {
        setToolControlError("memory_search 실행에는 query가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "memory_search",
          query,
          maxResults: 5
        },
        "memory_search"
      );
    }

    function submitMemoryGetProbe() {
      const path = toolMemoryGetPath.trim();
      if (!path) {
        setToolControlError("memory_get 실행에는 path가 필요합니다.");
        return;
      }

      sendToolControlRequest(
        {
          type: "memory_get",
          path,
          from: 1,
          lines: 40
        },
        "memory_get"
      );
    }

    function submitGuardAlertDispatch() {
      if (!ensureAuthed()) {
        return;
      }

      setToolControlError("");
      setGuardAlertDispatchState((prev) => ({
        ...prev,
        statusLabel: "dispatching",
        statusTone: "warn",
        message: "guard_alert_event.v1 전송 중"
      }));
      const ok = send({
        type: "dispatch_guard_alert",
        guardAlertEvent: guardAlertPipelineEvent
      });
      if (!ok) {
        setGuardAlertDispatchState((prev) => ({
          ...prev,
          statusLabel: "failed",
          statusTone: "error",
          message: "오류: WebSocket 연결이 끊어졌습니다."
        }));
        setToolControlError("오류: WebSocket 연결이 끊어졌습니다.");
        return;
      }

      log("[guard-alert] dispatch_guard_alert 요청 전송");
    }

    function handleServerMessage(msg) {
      if (msg.type === "conversation_search_result") {
        setConversationSearchByKey((prev) => ({
          ...prev,
          [currentKey]: {
            query: msg.query || "",
            loading: false,
            results: Array.isArray(msg.results) ? msg.results : [],
            error: msg.error || ""
          }
        }));
        return;
      }

      if (msg.type === "backup_export_result") {
        setBackupState((prev) => ({
          ...prev,
          exportBusy: false,
          status: msg.ok ? "백업 파일을 만들었습니다." : (msg.error || "백업 생성에 실패했습니다.")
        }));
        if (msg.ok && msg.contentBase64) {
          downloadBase64File(msg.fileName || "omninode-backup.zip", msg.contentBase64, "application/zip");
        }
        return;
      }

      if (msg.type === "backup_import_preview_result") {
        setBackupState((prev) => ({
          ...prev,
          importBusy: false,
          preview: msg.ok ? msg : null,
          status: msg.ok
            ? `가져오기 미리보기: 대화 ${msg.conversationCount || 0}개, 파일 ${msg.fileCount || 0}개`
            : (msg.error || "백업 미리보기에 실패했습니다.")
        }));
        return;
      }

      if (msg.type === "backup_import_result") {
        setBackupState((prev) => ({
          ...prev,
          importBusy: false,
          preview: msg.ok ? null : prev.preview,
          status: msg.ok
            ? `가져오기 완료: 대화 ${msg.importedConversations || 0}개, 파일 ${msg.importedFiles || 0}개`
            : (msg.error || "백업 가져오기에 실패했습니다.")
        }));
        return;
      }

      handleDashboardServerMessage(msg, {
        state: {
          rootTab,
          currentConversationId,
          currentKey,
          isPortraitMobileLayout,
          defaultGroqSingleModel: DEFAULT_GROQ_SINGLE_MODEL,
          routingPolicyState,
          plansState,
          taskGraphState,
          logicSelectedGraphId: logicSelectedGraphIdRef.current,
          refactorState,
          notebooksState,
          settingsState,
          filePreviewByConversation,
          codingResultByConversation,
          logicDraftGraph: logicDraftGraphRef.current,
          logicPathBrowser
        },
        refs: {
          autoCreateConversationRef,
          routineBrowserAgentPreviewRef
        },
        setters: {
          setAuthMeta,
          setAuthed,
          setAuthLocalOffset,
          setAuthTtlHours,
          setStatus,
          setContextState,
          setSelectedSkillKey,
          setSkillEditor,
          setSkillStatus,
          setLoadingSkillKey,
          refreshSkillsList,
          setRoutingPolicyState,
          setPlansState,
          setTaskGraphState,
          setRefactorState,
          setDoctorState,
          setNotebooksState,
          setSettingsState,
          setGeminiUsage,
          setCopilotPremiumUsage,
          setCopilotLocalUsage,
          setCopilotStatus,
          setCopilotDetail,
          setCodexStatus,
          setCodexDetail,
          setGroqModels,
          setCerebrasModels,
          setSelectedGroqModel,
          setCopilotModels,
          setSelectedCopilotModel,
          setConversationLists,
          setActiveConversationByKey,
          setConversationDetails,
          setSelectedMemoryByConversation,
          setChatMultiResultByConversation,
          setMemoryNotes,
          setMemoryPreview,
          setSelectedConversationIdsByKey,
          setSelectedFoldersByKey,
          setCodingResultByConversation,
          setShowExecutionLogsByConversation,
          setCodingRuntimeByConversation,
          setCodingExecutionInputByConversation,
          setFilePreviewByConversation,
          setCodingProgressByKey,
          setPendingByKey,
          setOptimisticUserByKey,
          setMetrics,
          setToolSessionKey,
          setToolControlError,
          setToolResultPreview,
          setSelectedToolResultId,
          setToolResultItems,
          setProviderRuntimeItems,
          setGuardObsItems,
          setGuardRetryTimelineItems,
          setGuardAlertDispatchState,
          setRoutines,
          setRoutineSelectedId,
          setRoutineProgress,
          setRoutineOutputPreview,
          setLogicGraphs,
          setLogicSelectedGraphId: (value) => {
            const nextValue = typeof value === "function"
              ? value(logicSelectedGraphIdRef.current)
              : value;
            logicSelectedGraphIdRef.current = nextValue;
            setLogicSelectedGraphId(nextValue);
          },
          setLogicDraftGraph: (value) => {
            const nextValue = typeof value === "function"
              ? value(logicDraftGraphRef.current)
              : value;
            logicDraftGraphRef.current = nextValue;
            setLogicDraftGraph(nextValue);
          },
          setLogicSelectedNodeId,
          setLogicSelectedEdgeId,
          setLogicPendingSourceNodeId,
          setLogicActiveRunId,
          setLogicRunSnapshot,
          setLogicRunEvents,
          setLogicJsonBuffer,
          setLogicDirty,
          setLogicLastMessage,
          setLogicPathBrowser
        },
        actions: {
          send,
          log,
          saveAuthToken,
          clearAuthToken,
          localUtcOffsetLabel,
          ensureAuthed,
          finishPendingRequest,
          setError,
          requestConversationDetail,
          requestAutoCreateConversation,
          requestWorkspaceFilePreview,
          buildCodingRuntimeMessageState,
          inferErrorKey,
          requestDoctorLast,
          requestRoutingPolicyGet,
          requestRoutingDecisionGetLast,
          requestPlanList,
          requestTaskGraphList,
          requestContextScan,
          requestSkillsList,
          requestCommandsList,
          requestNotebookGet,
          requestPlanGet,
          requestTaskGraphGet,
          requestTaskOutput,
          requestLogicGraphList,
          requestLogicGraphGet,
          requestRefactorRead,
          setResponsivePane
        },
        handlers: {
          handleConversationMemoryMessage,
          handleRoutineMessage,
          handleExecutionFlowMessage
        },
        utils: {
          normalizeChatMultiResultMessage: chatMultiUtils.normalizeChatMultiResultMessage,
          attachLatencyMetaToConversation,
          buildProviderRuntimeEventsFromMessage,
          summarizeProviderRuntimeEntry,
          PROVIDER_RUNTIME_KEYS,
          buildGuardObsEvent,
          buildGuardRetryTimelineEntry,
          GUARD_RETRY_TIMELINE_MAX_ENTRIES,
          inferToolResultGroup,
          inferToolResultDomain,
          inferToolResultAction,
          inferToolResultStatus,
          TOOL_RESULT_TYPES
        }
      });
    }

    useEffect(() => {
      if (rootTab === "routine" && authed) {
        refreshRoutines();
      }
    }, [rootTab, authed]);

    useEffect(() => {
      if (rootTab === "logic" && authed) {
        refreshLogicGraphs();
      }
    }, [rootTab, authed]);

    useEffect(() => {
      if (rootTab !== "logic" || isPortraitMobileLayout) {
        return;
      }
      const shell = logicCanvasShellRef.current;
      const shellRect = shell?.getBoundingClientRect();
      const graph = logicDraftGraphRef.current;
      const nodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
      const key = `${graph?.graphId || "draft"}:${nodes.length}:${graph?.edges?.length || 0}`;
      const viewport = normalizeLogicViewport(graph?.viewport);
      const hasVisibleNode = !!shellRect?.width
        && !!shellRect?.height
        && nodes.some((node) => {
          const size = normalizeRuntimeLogicNodeSize(node.size, node);
          const left = viewport.x + ((Number(node.position?.x) || 0) * viewport.zoom);
          const top = viewport.y + ((Number(node.position?.y) || 0) * viewport.zoom);
          const width = size.width * viewport.zoom;
          const height = size.height * viewport.zoom;
          return left < shellRect.width && (left + width) > 0 && top < shellRect.height && (top + height) > 0;
        });
      const shouldAutoFrame = !graph?.graphId
        && viewport.x === LOGIC_VIEWPORT_DEFAULTS.x
        && viewport.y === LOGIC_VIEWPORT_DEFAULTS.y
        && viewport.zoom === LOGIC_VIEWPORT_DEFAULTS.zoom
        && nodes.length > 0
      const shouldRecoverViewport = !!graph?.graphId
        && nodes.length > 0
        && !!shellRect?.width
        && !!shellRect?.height
        && !hasVisibleNode;
      if ((!shouldAutoFrame && !shouldRecoverViewport) || logicAutoFrameKeyRef.current === key) {
        return;
      }

      const frameId = window.requestAnimationFrame(() => {
        fitLogicViewport();
        logicAutoFrameKeyRef.current = key;
      });
      return () => window.cancelAnimationFrame(frameId);
    }, [rootTab, isPortraitMobileLayout, logicDraftGraph, viewportSize.width, viewportSize.height]);

    useEffect(() => {
      if (rootTab !== "logic" || typeof window === "undefined") {
        return undefined;
      }
      const frameId = window.requestAnimationFrame(() => {
        if (logicListPaneRef.current) {
          logicListPaneRef.current.scrollTop = 0;
        }
        if (logicPalettePaneRef.current) {
          logicPalettePaneRef.current.scrollTop = 0;
        }
      });
      return () => window.cancelAnimationFrame(frameId);
    }, [rootTab]);

    useEffect(() => {
      if (rootTab !== "logic") {
        return undefined;
      }
      if (logicCanvasGesture !== "idle") {
        return undefined;
      }
      if (typeof window === "undefined") {
        return undefined;
      }

      const shell = logicCanvasShellRef.current;
      if (!shell) {
        return undefined;
      }

      const frameId = window.requestAnimationFrame(() => {
        const nextMeasured = {};
        const cards = Array.from(shell.querySelectorAll(".logic-node-card[data-logic-node-id]"));
        cards.forEach((card) => {
          const nodeId = `${card.getAttribute("data-logic-node-id") || ""}`.trim();
          const nodeType = `${card.getAttribute("data-logic-node-type") || ""}`.trim();
          if (!nodeId) {
            return;
          }

          const baseMinimum = getLogicNodeMinimumSize(nodeType);
          nextMeasured[nodeId] = {
            width: baseMinimum.width,
            height: baseMinimum.height
          };
        });

        setLogicMeasuredNodeMinimumById((prev) => {
          const prevKeys = Object.keys(prev || {});
          const nextKeys = Object.keys(nextMeasured);
          if (prevKeys.length === nextKeys.length) {
            const unchanged = nextKeys.every((key) => {
              const previous = prev?.[key];
              const next = nextMeasured[key];
              return previous?.width === next?.width && previous?.height === next?.height;
            });
            if (unchanged) {
              return prev;
            }
          }
          return nextMeasured;
        });
      });

      return () => window.cancelAnimationFrame(frameId);
    }, [rootTab, currentLogicPane, isPortraitMobileLayout, logicCanvasGesture, logicDraftGraph, logicRunSnapshot, viewportSize.width, viewportSize.height]);

    useEffect(() => {
      if (rootTab !== "logic" || typeof window === "undefined") {
        return undefined;
      }

      function isEditableLogicTarget(target) {
        return !!target?.closest?.("input, textarea, select, option, label, [contenteditable='true'], [contenteditable='']");
      }

      function handleLogicSelectionKeyDown(event) {
        if (!event || event.defaultPrevented) {
          return;
        }
        if (event.key !== "Backspace" && event.key !== "Delete") {
          return;
        }
        if (isEditableLogicTarget(event.target)) {
          return;
        }
        if (logicSelectedNodeId) {
          event.preventDefault();
          deleteLogicNode(logicSelectedNodeId);
          return;
        }
        if (logicSelectedEdgeId) {
          event.preventDefault();
          deleteLogicEdge(logicSelectedEdgeId);
        }
      }

      window.addEventListener("keydown", handleLogicSelectionKeyDown);
      return () => window.removeEventListener("keydown", handleLogicSelectionKeyDown);
    }, [rootTab, logicSelectedNodeId, logicSelectedEdgeId]);

    useEffect(() => {
      if (typeof window === "undefined") {
        return undefined;
      }

      function clearDragScrollState() {
        const dragState = logicScrollDragRef.current;
        dragState?.element?.classList?.remove("is-drag-scroll-ready", "is-drag-scrolling");
        logicScrollDragRef.current = {
          ...dragState,
          active: false,
          element: null,
          moved: false
        };
      }

      function handleWindowMouseMove(event) {
        const dragState = logicScrollDragRef.current;
        if (!dragState?.active || !dragState?.element) {
          return;
        }
        const deltaY = event.clientY - dragState.startClientY;
        if (!dragState.moved && Math.abs(deltaY) > 5) {
          dragState.moved = true;
          dragState.element.classList.add("is-drag-scrolling");
        }
        if (!dragState.moved) {
          return;
        }
        dragState.element.scrollTop = dragState.startScrollTop - deltaY;
        if (event.cancelable) {
          event.preventDefault();
        }
      }

      function handleWindowMouseUp() {
        const dragState = logicScrollDragRef.current;
        if (!dragState?.active || !dragState?.element) {
          return;
        }
        const targetElement = dragState.element;
        const suppressClick = !!dragState.moved;
        clearDragScrollState();
        if (suppressClick) {
          logicScrollDragRef.current = {
            ...logicScrollDragRef.current,
            suppressClick: true,
            suppressElement: targetElement
          };
          window.setTimeout(() => {
            if (logicScrollDragRef.current?.suppressElement === targetElement) {
              logicScrollDragRef.current = {
                ...logicScrollDragRef.current,
                suppressClick: false,
                suppressElement: null
              };
            }
          }, 0);
        }
      }

      window.addEventListener("mousemove", handleWindowMouseMove, { passive: false, capture: true });
      window.addEventListener("mouseup", handleWindowMouseUp, true);

      return () => {
        clearDragScrollState();
        window.removeEventListener("mousemove", handleWindowMouseMove, true);
        window.removeEventListener("mouseup", handleWindowMouseUp, true);
      };
    }, []);

    useEffect(() => {
      if (typeof window === "undefined") {
        return undefined;
      }

      function resolveLogicConnectionTarget(event) {
        if (
          typeof document === "undefined"
          || !Number.isFinite(Number(event?.clientX))
          || !Number.isFinite(Number(event?.clientY))
        ) {
          return { nodeId: "", portKey: "" };
        }

        const target = document.elementFromPoint(event.clientX, event.clientY);
        const targetPort = target?.closest?.("[data-logic-port-role='input'][data-logic-node-id]");
        return {
          nodeId: targetPort?.getAttribute?.("data-logic-node-id") || "",
          portKey: targetPort?.getAttribute?.("data-logic-port-key") || "main"
        };
      }

      function handlePointerMove(event) {
        const interaction = logicCanvasInteractionRef.current;
        if (!interaction?.mode) {
          return;
        }

        if (interaction.mode === "pan") {
          patchLogicViewport({
            x: interaction.originX + (event.clientX - interaction.startClientX),
            y: interaction.originY + (event.clientY - interaction.startClientY),
            zoom: normalizeLogicViewport(logicDraftGraphRef.current?.viewport).zoom
          });
          return;
        }

        if (interaction.mode === "node" && interaction.nodeId) {
          const currentViewport = normalizeLogicViewport(logicDraftGraphRef.current?.viewport);
          patchLogicNodePosition(interaction.nodeId, {
            x: interaction.originX + ((event.clientX - interaction.startClientX) / currentViewport.zoom),
            y: interaction.originY + ((event.clientY - interaction.startClientY) / currentViewport.zoom)
          });
          return;
        }

        if (interaction.mode === "resize" && interaction.nodeId && interaction.handle) {
          const currentViewport = normalizeLogicViewport(logicDraftGraphRef.current?.viewport);
          const deltaX = (event.clientX - interaction.startClientX) / currentViewport.zoom;
          const deltaY = (event.clientY - interaction.startClientY) / currentViewport.zoom;
          const nextSize = normalizeRuntimeLogicNodeSize({
            width: interaction.originWidth
              + (interaction.handle.includes("e") ? deltaX : 0)
              - (interaction.handle.includes("w") ? deltaX : 0),
            height: interaction.originHeight
              + (interaction.handle.includes("s") ? deltaY : 0)
              - (interaction.handle.includes("n") ? deltaY : 0)
          }, interaction.nodeId, interaction.nodeType);
          const nextPosition = {
            x: interaction.originX,
            y: interaction.originY
          };

          if (interaction.handle.includes("w")) {
            nextPosition.x = interaction.originX + (interaction.originWidth - nextSize.width);
          }
          if (interaction.handle.includes("n")) {
            nextPosition.y = interaction.originY + (interaction.originHeight - nextSize.height);
          }

          patchLogicNodeFrame(interaction.nodeId, {
            position: nextPosition,
            size: nextSize
          });
          return;
        }

        if (interaction.mode === "connect" && interaction.nodeId) {
          const target = resolveLogicConnectionTarget(event);
          setLogicConnectionPreview({
            sourceNodeId: interaction.nodeId,
            sourcePort: interaction.sourcePort || "main",
            targetNodeId: target.nodeId,
            targetPort: target.portKey || "main",
            clientX: event.clientX,
            clientY: event.clientY
          });
        }
      }

      function clearInteraction(event) {
        const interaction = logicCanvasInteractionRef.current;
        if (!interaction?.mode) {
          return;
        }

        if (interaction.mode === "connect" && interaction.nodeId) {
          const target = resolveLogicConnectionTarget(event);
          if (target.nodeId) {
            addLogicEdge(interaction.nodeId, target.nodeId, interaction.sourcePort || "main", target.portKey || "main");
          }
          setLogicPendingSourceNodeId("");
          setLogicConnectionPreview(null);
        }

        logicCanvasInteractionRef.current = createEmptyLogicCanvasInteraction();
        setLogicCanvasGesture("idle");
      }

      window.addEventListener("pointermove", handlePointerMove);
      window.addEventListener("pointerup", clearInteraction);
      window.addEventListener("pointercancel", clearInteraction);
      window.addEventListener("blur", clearInteraction);
      return () => {
        window.removeEventListener("pointermove", handlePointerMove);
        window.removeEventListener("pointerup", clearInteraction);
        window.removeEventListener("pointercancel", clearInteraction);
        window.removeEventListener("blur", clearInteraction);
      };
    }, []);

    useEffect(() => {
      if ((rootTab === "settings" || rootTab === "automation") && authed) {
        setDoctorState((prev) => ({
          ...prev,
          loading: true,
          lastError: ""
        }));
        requestDoctorLast(send, { silent: true, queueIfClosed: false });
        setPlansState((prev) => ({
          ...prev,
          loading: true,
          lastError: ""
        }));
        requestPlanList(send, { silent: true, queueIfClosed: false });
        setRoutingPolicyState((prev) => ({
          ...prev,
          loading: true,
          lastError: ""
        }));
        requestRoutingPolicyGet(send, { silent: true, queueIfClosed: false });
        setTaskGraphState((prev) => ({
          ...prev,
          loading: true,
          lastError: ""
        }));
        requestTaskGraphList(send, { silent: true, queueIfClosed: false });
      }
    }, [rootTab, authed]);

    useEffect(() => {
      if (rootTab === "notebook" && authed) {
        refreshNotebook({ silent: true, queueIfClosed: false });
      }
    }, [rootTab, authed]);

    useEffect(() => {
      if (rootTab === "skills" && authed) {
        refreshSkillsList({ silent: true, queueIfClosed: false });
      }
    }, [rootTab, authed]);

    useEffect(() => {
      const selected = routines.find((item) => item.id === routineSelectedId) || null;
      setRoutineEditForm(hydrateRoutineFormFromRoutine(selected));
    }, [routines, routineSelectedId]);

    function refreshRoutines() {
      send({ type: "get_routines" });
    }

    function refreshLogicGraphs() {
      requestLogicGraphList(send, { silent: true, queueIfClosed: false });
    }

    function requestLogicGraphDetail(graphId) {
      if (!graphId) {
        return;
      }
      requestLogicGraphGet(send, graphId, { silent: true, queueIfClosed: false });
    }

    function getRuntimeLogicNodeMinimum(nodeOrId, explicitType = "") {
      const nodeId = typeof nodeOrId === "object" ? `${nodeOrId?.nodeId || ""}` : `${nodeOrId || ""}`;
      const type = typeof nodeOrId === "object" ? `${nodeOrId?.type || explicitType || ""}` : `${explicitType || ""}`;
      const baseMinimum = getLogicNodeMinimumSize(type);
      const measuredMinimum = logicMeasuredNodeMinimumRef.current?.[nodeId] || null;
      return {
        width: Math.max(baseMinimum.width, Math.round(Number(measuredMinimum?.width) || 0)),
        height: Math.max(baseMinimum.height, Math.round(Number(measuredMinimum?.height) || 0))
      };
    }

    function normalizeRuntimeLogicNodeSize(size, nodeOrId, explicitType = "") {
      const type = typeof nodeOrId === "object" ? `${nodeOrId?.type || explicitType || ""}` : `${explicitType || ""}`;
      const normalized = normalizeLogicNodeSize(size, type);
      const minimum = getRuntimeLogicNodeMinimum(nodeOrId, type);
      return {
        width: Math.max(normalized.width, minimum.width),
        height: Math.max(normalized.height, minimum.height)
      };
    }

    function normalizeLogicViewport(viewport) {
      const normalized = viewport && typeof viewport === "object" ? viewport : {};
      const zoom = Number.isFinite(Number(normalized.zoom))
        ? Math.min(LOGIC_CANVAS_MAX_ZOOM, Math.max(LOGIC_CANVAS_MIN_ZOOM, Number(normalized.zoom)))
        : LOGIC_VIEWPORT_DEFAULTS.zoom;
      return {
        x: Number.isFinite(Number(normalized.x)) ? Math.round(Number(normalized.x)) : LOGIC_VIEWPORT_DEFAULTS.x,
        y: Number.isFinite(Number(normalized.y)) ? Math.round(Number(normalized.y)) : LOGIC_VIEWPORT_DEFAULTS.y,
        zoom: Math.round(zoom * 100) / 100
      };
    }

    function patchLogicViewport(nextViewportOrUpdater, options = {}) {
      let changed = false;
      setLogicDraftGraph((prev) => {
        if (!prev || typeof prev !== "object") {
          return prev;
        }
        const currentViewport = normalizeLogicViewport(prev.viewport);
        const candidate = typeof nextViewportOrUpdater === "function"
          ? nextViewportOrUpdater(currentViewport, prev)
          : nextViewportOrUpdater;
        const nextViewport = normalizeLogicViewport(candidate);
        if (
          currentViewport.x === nextViewport.x
          && currentViewport.y === nextViewport.y
          && currentViewport.zoom === nextViewport.zoom
        ) {
          logicDraftGraphRef.current = prev;
          return prev;
        }
        changed = true;
        const nextGraph = {
          ...prev,
          viewport: nextViewport
        };
        logicDraftGraphRef.current = nextGraph;
        return nextGraph;
      });
      if (changed && options.markDirty !== false) {
        setLogicDirty(true);
      }
      return changed;
    }

    function patchLogicNodePosition(nodeId, nextPosition) {
      if (!nodeId) {
        return false;
      }
      let changed = false;
      setLogicDraftGraph((prev) => {
        if (!prev || !Array.isArray(prev.nodes)) {
          return prev;
        }
        const normalizedPosition = {
          x: Math.max(32, Math.round(Number(nextPosition?.x) || 0)),
          y: Math.max(32, Math.round(Number(nextPosition?.y) || 0))
        };
        const nodes = prev.nodes.map((node) => {
          if (node.nodeId !== nodeId) {
            return node;
          }
          const currentX = Number(node.position?.x) || 0;
          const currentY = Number(node.position?.y) || 0;
          if (currentX === normalizedPosition.x && currentY === normalizedPosition.y) {
            return node;
          }
          changed = true;
          return {
            ...node,
            position: normalizedPosition
          };
        });
        if (!changed) {
          logicDraftGraphRef.current = prev;
          return prev;
        }
        const nextGraph = {
            ...prev,
            nodes
          };
        logicDraftGraphRef.current = nextGraph;
        return nextGraph;
      });
      if (changed) {
        setLogicDirty(true);
      }
      return changed;
    }

    function patchLogicNodeFrame(nodeId, nextFrame) {
      if (!nodeId) {
        return false;
      }
      let changed = false;
      setLogicDraftGraph((prev) => {
        if (!prev || !Array.isArray(prev.nodes)) {
          return prev;
        }
        const normalizedPosition = {
          x: Math.max(24, Math.round(Number(nextFrame?.position?.x) || 0)),
          y: Math.max(24, Math.round(Number(nextFrame?.position?.y) || 0))
        };
        const nodes = prev.nodes.map((node) => {
          if (node.nodeId !== nodeId) {
            return node;
          }
          const normalizedSize = normalizeRuntimeLogicNodeSize(nextFrame?.size, node);
          const currentPositionX = Number(node.position?.x) || 0;
          const currentPositionY = Number(node.position?.y) || 0;
          const currentSize = normalizeRuntimeLogicNodeSize(node.size, node);
          if (
            currentPositionX === normalizedPosition.x
            && currentPositionY === normalizedPosition.y
            && currentSize.width === normalizedSize.width
            && currentSize.height === normalizedSize.height
          ) {
            return node;
          }
          changed = true;
          return {
            ...node,
            position: normalizedPosition,
            size: normalizedSize
          };
        });
        if (!changed) {
          logicDraftGraphRef.current = prev;
          return prev;
        }
        const nextGraph = {
          ...prev,
          nodes
        };
        logicDraftGraphRef.current = nextGraph;
        return nextGraph;
      });
      if (changed) {
        setLogicDirty(true);
      }
      return changed;
    }

    function zoomLogicViewportAroundClient(nextZoom, clientX, clientY) {
      const shell = logicCanvasShellRef.current;
      if (!shell) {
        return;
      }
      const rect = shell.getBoundingClientRect();
      if (!rect.width || !rect.height) {
        return;
      }
      patchLogicViewport((currentViewport) => {
        const safeZoom = Math.min(LOGIC_CANVAS_MAX_ZOOM, Math.max(LOGIC_CANVAS_MIN_ZOOM, Number(nextZoom) || currentViewport.zoom));
        const anchorX = clientX - rect.left;
        const anchorY = clientY - rect.top;
        const graphX = (anchorX - currentViewport.x) / currentViewport.zoom;
        const graphY = (anchorY - currentViewport.y) / currentViewport.zoom;
        return {
          x: anchorX - (graphX * safeZoom),
          y: anchorY - (graphY * safeZoom),
          zoom: safeZoom
        };
      });
    }

    function resetLogicViewport() {
      patchLogicViewport(LOGIC_VIEWPORT_DEFAULTS);
    }

    function stepLogicViewportZoom(direction) {
      const shell = logicCanvasShellRef.current;
      const rect = shell?.getBoundingClientRect();
      const currentViewport = normalizeLogicViewport(logicDraftGraphRef.current?.viewport);
      const nextZoom = currentViewport.zoom + (direction > 0 ? 0.12 : -0.12);
      if (rect?.width && rect?.height) {
        zoomLogicViewportAroundClient(nextZoom, rect.left + (rect.width / 2), rect.top + (rect.height / 2));
        return;
      }
      patchLogicViewport({
        ...currentViewport,
        zoom: nextZoom
      });
    }

    function fitLogicViewport() {
      const shell = logicCanvasShellRef.current;
      const rect = shell?.getBoundingClientRect();
      const graph = logicDraftGraphRef.current;
      const nodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
      if (!rect?.width || !rect?.height || nodes.length === 0) {
        resetLogicViewport();
        return;
      }

      let minX = Infinity;
      let minY = Infinity;
      let maxX = -Infinity;
      let maxY = -Infinity;
      nodes.forEach((node) => {
        const x = Number(node.position?.x) || 0;
        const y = Number(node.position?.y) || 0;
        const size = normalizeRuntimeLogicNodeSize(node.size, node);
        minX = Math.min(minX, x);
        minY = Math.min(minY, y);
        maxX = Math.max(maxX, x + size.width);
        maxY = Math.max(maxY, y + size.height);
      });

      const boundsWidth = Math.max(420, maxX - minX);
      const boundsHeight = Math.max(280, maxY - minY);
      const nextZoom = Math.min(
        1,
        Math.max(
          LOGIC_CANVAS_MIN_ZOOM,
          Math.min(
            LOGIC_CANVAS_MAX_ZOOM,
            (rect.width - (LOGIC_CANVAS_STAGE_PADDING * 2)) / boundsWidth,
            (rect.height - (LOGIC_CANVAS_STAGE_PADDING * 2)) / boundsHeight
          )
        )
      );

      patchLogicViewport({
        x: (rect.width - (boundsWidth * nextZoom)) / 2 - (minX * nextZoom),
        y: (rect.height - (boundsHeight * nextZoom)) / 2 - (minY * nextZoom),
        zoom: nextZoom
      });
    }

    function beginLogicScrollListDrag(event) {
      if (!event || event.button !== 0) {
        return;
      }
      const scrollTarget = event.currentTarget;
      if (!scrollTarget || (scrollTarget.scrollHeight - scrollTarget.clientHeight) <= 6) {
        return;
      }
      if (event.target?.closest?.("input, textarea, select, option, label")) {
        return;
      }
      logicScrollDragRef.current = {
        active: true,
        element: scrollTarget,
        startClientY: event.clientY,
        startScrollTop: scrollTarget.scrollTop,
        moved: false,
        suppressClick: false,
        suppressElement: null
      };
      scrollTarget.classList.add("is-drag-scroll-ready");
    }

    function handleLogicScrollListClickCapture(event) {
      const dragState = logicScrollDragRef.current;
      if (!dragState?.suppressClick || !dragState?.suppressElement || dragState.suppressElement !== event.currentTarget) {
        return;
      }
      event.preventDefault();
      event.stopPropagation();
      logicScrollDragRef.current = {
        ...dragState,
        suppressClick: false,
        suppressElement: null
      };
    }

    function beginLogicViewportPan(event) {
      if (!event || (event.button !== 0 && event.button !== 1)) {
        return;
      }
      if (event.target && event.target.closest(".logic-node-card, .logic-canvas-toolbar, .logic-edge-delete, .logic-edge-path")) {
        return;
      }
      const currentViewport = normalizeLogicViewport(logicDraftGraphRef.current?.viewport);
      logicCanvasInteractionRef.current = {
        mode: "pan",
        pointerId: event.pointerId,
        nodeId: "",
        startClientX: event.clientX,
        startClientY: event.clientY,
        originX: currentViewport.x,
        originY: currentViewport.y
      };
      setLogicCanvasGesture("pan");
      event.preventDefault();
      event.currentTarget?.setPointerCapture?.(event.pointerId);
    }

    function beginLogicNodeDrag(event, nodeId) {
      if (!event || event.button !== 0 || !nodeId) {
        return;
      }
      if (
        event.target
        && event.target.closest(".logic-node-actions, .logic-node-resize-handle, .btn, input, textarea, select, option, label, a")
      ) {
        return;
      }
      const graph = logicDraftGraphRef.current;
      const node = Array.isArray(graph?.nodes) ? graph.nodes.find((item) => item.nodeId === nodeId) : null;
      if (!node) {
        return;
      }
      logicCanvasInteractionRef.current = {
        mode: "node",
        pointerId: event.pointerId,
        nodeId,
        handle: "",
        startClientX: event.clientX,
        startClientY: event.clientY,
        originX: Number(node.position?.x) || 0,
        originY: Number(node.position?.y) || 0,
        originWidth: normalizeRuntimeLogicNodeSize(node.size, node).width,
        originHeight: normalizeRuntimeLogicNodeSize(node.size, node).height,
        nodeType: node.type
      };
      setLogicCanvasGesture("node");
      setLogicSelectedNodeId(nodeId);
      setLogicSelectedEdgeId("");
      event.preventDefault();
      event.stopPropagation();
      event.currentTarget?.setPointerCapture?.(event.pointerId);
    }

    function handleLogicCanvasWheel(event) {
      if (!event) {
        return;
      }
      event.preventDefault();
      event.stopPropagation();
      const currentViewport = normalizeLogicViewport(logicDraftGraphRef.current?.viewport);
      const nextZoom = currentViewport.zoom + (event.deltaY < 0 ? 0.08 : -0.08);
      zoomLogicViewportAroundClient(nextZoom, event.clientX, event.clientY);
    }

    function mutateLogicDraft(updater) {
      setLogicDraftGraph((prev) => {
        const current = cloneLogicGraph(prev);
        const next = typeof updater === "function" ? updater(current) : current;
        const cloned = cloneLogicGraph(next);
        logicDraftGraphRef.current = cloned;
        return cloned;
      });
      setLogicDirty(true);
    }

    function resetLogicConnectionState() {
      logicCanvasInteractionRef.current = createEmptyLogicCanvasInteraction();
      setLogicPendingSourceNodeId("");
      setLogicConnectionPreview(null);
      if (logicCanvasGesture === "connect") {
        setLogicCanvasGesture("idle");
      }
    }

    function addLogicEdge(sourceNodeId, targetNodeId, sourcePort = "main", targetPort = "main") {
      const normalizedSourceNodeId = `${sourceNodeId || ""}`.trim();
      const normalizedTargetNodeId = `${targetNodeId || ""}`.trim();
      const normalizedSourcePort = `${sourcePort || "main"}`.trim().toLowerCase() || "main";
      const normalizedTargetPort = `${targetPort || "main"}`.trim().toLowerCase() || "main";
      if (
        !normalizedSourceNodeId
        || !normalizedTargetNodeId
        || normalizedSourceNodeId === normalizedTargetNodeId
      ) {
        return false;
      }

      let changed = false;
      let selectedEdgeId = "";
      setLogicDraftGraph((prev) => {
        if (!prev || !Array.isArray(prev.nodes) || !Array.isArray(prev.edges)) {
          return prev;
        }

        const hasSource = prev.nodes.some((node) => node.nodeId === normalizedSourceNodeId);
        const hasTarget = prev.nodes.some((node) => node.nodeId === normalizedTargetNodeId);
        if (!hasSource || !hasTarget) {
          logicDraftGraphRef.current = prev;
          return prev;
        }

        const existingEdge = prev.edges.find((edge) =>
          edge.sourceNodeId === normalizedSourceNodeId
          && edge.targetNodeId === normalizedTargetNodeId
          && `${edge.sourcePort || "main"}`.trim().toLowerCase() === normalizedSourcePort
          && `${edge.targetPort || "main"}`.trim().toLowerCase() === normalizedTargetPort
        );
        if (existingEdge) {
          selectedEdgeId = existingEdge.edgeId;
          logicDraftGraphRef.current = prev;
          return prev;
        }

        const nextEdges = prev.edges.filter((edge) => {
          const edgeTargetPort = `${edge.targetPort || "main"}`.trim().toLowerCase() || "main";
          if (normalizedTargetPort !== "main" && edge.targetNodeId === normalizedTargetNodeId && edgeTargetPort === normalizedTargetPort) {
            return false;
          }
          if (normalizedTargetPort === "main" && normalizedSourcePort !== "main" && edge.sourceNodeId === normalizedSourceNodeId && edge.targetNodeId === normalizedTargetNodeId) {
            return false;
          }
          return true;
        });
        const nextEdge = createLogicEdge("", normalizedSourceNodeId, normalizedTargetNodeId, {
          sourcePort: normalizedSourcePort,
          targetPort: normalizedTargetPort
        });
        selectedEdgeId = nextEdge.edgeId;
        changed = true;
        const nextGraph = {
          ...prev,
          edges: [...nextEdges, nextEdge]
        };
        logicDraftGraphRef.current = nextGraph;
        return nextGraph;
      });

      if (selectedEdgeId) {
        setLogicSelectedEdgeId(selectedEdgeId);
        setLogicSelectedNodeId("");
      }
      if (changed) {
        setLogicDirty(true);
      }
      return changed;
    }

    function selectLogicGraph(graphId) {
      logicSelectedGraphIdRef.current = graphId || "";
      setLogicSelectedGraphId(graphId || "");
      setLogicSelectedNodeId("");
      setLogicSelectedEdgeId("");
      closeLogicPathBrowser();
      resetLogicConnectionState();
      setLogicRunOutputExpanded(false);
      logicAutoFrameKeyRef.current = "";
      if (graphId) {
        requestLogicGraphDetail(graphId);
      }
    }

    function createNewLogicGraphDraft() {
      const defaultSchedule = createEmptyLogicGraph().schedule;
      const nextGraph = createEmptyLogicGraph({
        viewport: { ...LOGIC_VIEWPORT_DEFAULTS },
        schedule: {
          ...defaultSchedule,
          timezoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || "Asia/Seoul"
        }
      });
      logicSelectedGraphIdRef.current = "";
      setLogicSelectedGraphId("");
      logicAutoFrameKeyRef.current = "";
      logicDraftGraphRef.current = nextGraph;
      setLogicDraftGraph(nextGraph);
      setLogicSelectedNodeId("");
      setLogicSelectedEdgeId("");
      closeLogicPathBrowser();
      resetLogicConnectionState();
      setLogicRunSnapshot(null);
      setLogicRunEvents([]);
      setLogicActiveRunId("");
      setLogicRunOutputExpanded(false);
      setLogicLastMessage("새 작업 흐름 초안을 만들었습니다.");
      setLogicDirty(true);
    }

    function patchLogicGraphField(field, value) {
      mutateLogicDraft((draft) => {
        draft[field] = value;
        return draft;
      });
    }

    function patchLogicScheduleField(field, value) {
      mutateLogicDraft((draft) => {
        draft.schedule = {
          ...(draft.schedule || {}),
          [field]: value
        };
        return draft;
      });
    }

    function addLogicNode(type) {
      const shell = logicCanvasShellRef.current;
      const rect = shell?.getBoundingClientRect();
      const currentViewport = normalizeLogicViewport(logicDraftGraphRef.current?.viewport);
      const visibleCenterX = rect?.width
        ? ((rect.width * 0.5) - currentViewport.x) / currentViewport.zoom
        : 360;
      const visibleCenterY = rect?.height
        ? ((rect.height * 0.42) - currentViewport.y) / currentViewport.zoom
        : 220;
      let nextNodeId = "";
      mutateLogicDraft((draft) => {
        const offsetIndex = draft.nodes.length % 6;
        const nextNode = createLogicNode(type, {
          position: {
            x: Math.max(72, Math.round(visibleCenterX - 90 + ((offsetIndex % 3) * 140))),
            y: Math.max(72, Math.round(visibleCenterY - 54 + (Math.floor(offsetIndex / 3) * 120)))
          }
        });
        nextNodeId = nextNode.nodeId;
        draft.nodes.push(nextNode);
        return draft;
      });
      if (nextNodeId) {
        setLogicSelectedNodeId(nextNodeId);
        setLogicSelectedEdgeId("");
        closeLogicPathBrowser();
        setResponsivePane("logic", "selection");
      }
    }

    function deleteLogicNode(nodeId) {
      if (!nodeId) {
        return;
      }
      mutateLogicDraft((draft) => {
        draft.nodes = draft.nodes.filter((node) => node.nodeId !== nodeId);
        draft.edges = draft.edges.filter((edge) => edge.sourceNodeId !== nodeId && edge.targetNodeId !== nodeId);
        return draft;
      });
      if (logicSelectedNodeId === nodeId) {
        setLogicSelectedNodeId("");
        closeLogicPathBrowser();
      }
      if (logicPendingSourceNodeId === nodeId) {
        resetLogicConnectionState();
      }
    }

    function duplicateLogicNode(nodeId) {
      const target = (logicDraftGraph.nodes || []).find((item) => item.nodeId === nodeId);
      if (!target) {
        return;
      }
      mutateLogicDraft((draft) => {
        draft.nodes.push(createLogicNode(target.type, {
          title: `${target.title} 복제`,
          position: {
            x: (target.position?.x || 0) + 36,
            y: (target.position?.y || 0) + 36
          },
          size: normalizeRuntimeLogicNodeSize(target.size, target),
          continueOnError: !!target.continueOnError,
          config: { ...(target.config || {}) },
          outputs: { ...(target.outputs || {}) }
        }));
        return draft;
      });
    }

    function moveLogicNode(nodeId, dx, dy) {
      mutateLogicDraft((draft) => {
        draft.nodes = draft.nodes.map((node) => {
          if (node.nodeId !== nodeId) {
            return node;
          }
          return {
            ...node,
            position: {
              x: Math.max(24, (node.position?.x || 0) + dx),
              y: Math.max(24, (node.position?.y || 0) + dy)
            }
          };
        });
        return draft;
      });
    }

    function beginLogicNodeResize(event, nodeId, handle) {
      if (!event || event.button !== 0 || !nodeId || !handle) {
        return;
      }
      const graph = logicDraftGraphRef.current;
      const node = Array.isArray(graph?.nodes) ? graph.nodes.find((item) => item.nodeId === nodeId) : null;
      if (!node) {
        return;
      }
      const size = normalizeRuntimeLogicNodeSize(node.size, node);
      logicCanvasInteractionRef.current = {
        mode: "resize",
        pointerId: event.pointerId,
        nodeId,
        handle,
        startClientX: event.clientX,
        startClientY: event.clientY,
        originX: Number(node.position?.x) || 0,
        originY: Number(node.position?.y) || 0,
        originWidth: size.width,
        originHeight: size.height,
        nodeType: node.type
      };
      setLogicCanvasGesture("resize");
      setLogicSelectedNodeId(nodeId);
      setLogicSelectedEdgeId("");
      event.preventDefault();
      event.stopPropagation();
      event.currentTarget?.setPointerCapture?.(event.pointerId);
    }

    function beginLogicEdgeConnection(event, nodeId, sourcePort = "main") {
      if (!event || event.button !== 0 || !nodeId) {
        return;
      }
      logicCanvasInteractionRef.current = {
        mode: "connect",
        pointerId: event.pointerId,
        nodeId,
        sourcePort: `${sourcePort || "main"}`.trim().toLowerCase() || "main",
        handle: "",
        startClientX: event.clientX,
        startClientY: event.clientY,
        originX: 0,
        originY: 0,
        originWidth: LOGIC_NODE_DEFAULT_SIZE.width,
        originHeight: LOGIC_NODE_DEFAULT_SIZE.height
      };
      setLogicPendingSourceNodeId(nodeId);
      setLogicSelectedNodeId(nodeId);
      setLogicSelectedEdgeId("");
      setLogicConnectionPreview({
        sourceNodeId: nodeId,
        sourcePort: `${sourcePort || "main"}`.trim().toLowerCase() || "main",
        targetNodeId: "",
        targetPort: "main",
        clientX: event.clientX,
        clientY: event.clientY
      });
      setLogicCanvasGesture("connect");
      event.preventDefault();
      event.stopPropagation();
      event.currentTarget?.setPointerCapture?.(event.pointerId);
    }

    function selectLogicNode(nodeId) {
      setLogicSelectedNodeId(nodeId || "");
      setLogicSelectedEdgeId("");
      closeLogicPathBrowser();
      setResponsivePane("logic", "selection");
    }

    function selectLogicEdge(edgeId) {
      setLogicSelectedEdgeId(edgeId || "");
      setLogicSelectedNodeId("");
      closeLogicPathBrowser();
      setResponsivePane("logic", "selection");
    }

    function deleteLogicEdge(edgeId) {
      if (!edgeId) {
        return;
      }
      mutateLogicDraft((draft) => {
        draft.edges = draft.edges.filter((edge) => edge.edgeId !== edgeId);
        return draft;
      });
      if (logicSelectedEdgeId === edgeId) {
        setLogicSelectedEdgeId("");
      }
    }

    function patchLogicNodeField(field, value) {
      if (!logicSelectedNodeId) {
        return;
      }
      mutateLogicDraft((draft) => {
        draft.nodes = draft.nodes.map((node) => node.nodeId === logicSelectedNodeId
          ? { ...node, [field]: value }
          : node);
        return draft;
      });
    }

    function patchLogicNodeConfigById(nodeId, key, value) {
      if (!nodeId || !key) {
        return;
      }
      mutateLogicDraft((draft) => {
        draft.nodes = draft.nodes.map((node) => node.nodeId === nodeId
          ? {
            ...node,
            config: {
              ...(node.config || {}),
              [key]: value
            }
          }
          : node);
        return draft;
      });
    }

    function patchLogicNodeConfigEntriesById(nodeId, entries) {
      if (!nodeId || !entries || typeof entries !== "object") {
        return;
      }
      mutateLogicDraft((draft) => {
        draft.nodes = draft.nodes.map((node) => node.nodeId === nodeId
          ? {
            ...node,
            config: {
              ...(node.config || {}),
              ...entries
            }
          }
          : node);
        return draft;
      });
    }

    function patchLogicNodeConfig(key, value) {
      if (!logicSelectedNodeId) {
        return;
      }
      patchLogicNodeConfigById(logicSelectedNodeId, key, value);
    }

    function removeLogicFieldEdges(nodeId, fieldKey) {
      if (!nodeId || !fieldKey) {
        return;
      }
      const removedEdgeIds = [];
      mutateLogicDraft((draft) => {
        draft.edges = draft.edges.filter((edge) => {
          const matches = edge.targetNodeId === nodeId
            && `${edge.targetPort || "main"}`.trim().toLowerCase() === `${fieldKey}`.trim().toLowerCase();
          if (matches) {
            removedEdgeIds.push(edge.edgeId);
          }
          return !matches;
        });
        return draft;
      });
      if (removedEdgeIds.includes(logicSelectedEdgeId)) {
        setLogicSelectedEdgeId("");
      }
    }

    function patchLogicNodeBinding(nodeId, fieldKey, patch = {}) {
      if (!nodeId || !fieldKey) {
        return;
      }
      const targetNode = (logicDraftGraphRef.current?.nodes || []).find((node) => node.nodeId === nodeId);
      const currentConfig = targetNode?.config || {};
      const modeKey = getLogicFieldModeKey(fieldKey);
      const literalKey = getLogicFieldLiteralKey(fieldKey);
      const referenceKey = getLogicFieldReferenceKey(fieldKey);
      const currentMode = `${currentConfig[modeKey] || ""}`.trim().toLowerCase()
        || (isLogicReferenceValue(currentConfig[fieldKey]) ? "reference" : "literal");
      const nextMode = `${patch.mode || currentMode || "literal"}`.trim().toLowerCase();
      const currentLiteral = typeof currentConfig[literalKey] === "string"
        ? currentConfig[literalKey]
        : (isLogicReferenceValue(currentConfig[fieldKey]) ? "" : `${currentConfig[fieldKey] || ""}`);
      const currentReference = typeof currentConfig[referenceKey] === "string"
        ? currentConfig[referenceKey]
        : (isLogicReferenceValue(currentConfig[fieldKey]) ? `${currentConfig[fieldKey] || ""}` : "");
      const nextLiteral = patch.literalValue !== undefined ? `${patch.literalValue}` : currentLiteral;
      const nextReference = patch.referenceValue !== undefined
        ? normalizeLogicReferenceValue(patch.referenceValue)
        : currentReference;
      const entries = {
        [modeKey]: nextMode,
        [literalKey]: nextLiteral,
        [referenceKey]: nextReference
      };
      if (nextMode === "reference") {
        entries[fieldKey] = nextReference;
      } else if (nextMode === "edge") {
        entries[fieldKey] = "";
      } else {
        entries[fieldKey] = nextLiteral;
      }
      patchLogicNodeConfigEntriesById(nodeId, entries);
      if ((nextMode !== "edge") || patch.clearEdges) {
        removeLogicFieldEdges(nodeId, fieldKey);
      }
    }

    function closeLogicPathBrowser() {
      setLogicPathBrowser(createEmptyLogicPathBrowserState());
    }

    function requestLogicPathBrowserListing(scope, rootKey, browsePath, patch = {}) {
      if (!ensureAuthed()) {
        return false;
      }
      setLogicPathBrowser((prev) => ({
        ...prev,
        ...patch,
        open: true,
        loading: true,
        scope,
        rootKey,
        browsePath,
        message: ""
      }));
      const ok = requestLogicPathList(send, scope, rootKey, browsePath, { silent: true, queueIfClosed: false });
      if (!ok) {
        setError("logic:main", "오류: WebSocket 연결이 끊어졌습니다.");
        setLogicPathBrowser((prev) => ({
          ...prev,
          loading: false,
          message: "오류: WebSocket 연결이 끊어졌습니다."
        }));
      }
      return ok;
    }

    function openLogicPathBrowser(field) {
      const pathScope = `${field?.pathScope || "workspace"}`.trim() || "workspace";
      const nodeId = `${logicSelectedNodeId || ""}`.trim();
      if (!nodeId || !field?.key) {
        return;
      }
      const selectedNode = (logicDraftGraph?.nodes || []).find((item) => item.nodeId === nodeId);
      const currentValue = selectedNode?.config?.[field.key] ?? "";
      const rootKey = resolveLogicPathBrowserRoot(pathScope, currentValue);
      const browsePath = resolveLogicPathBrowserBrowsePath(pathScope, rootKey, currentValue);
      requestLogicPathBrowserListing(pathScope, rootKey, browsePath, {
        nodeId,
        fieldKey: field.key,
        fieldLabel: field.label || field.key,
        allowDirectorySelection: !!field.allowDirectorySelection,
        roots: [],
        items: [],
        displayPath: "",
        parentBrowsePath: null,
        directorySelectPath: null
      });
    }

    function changeLogicPathBrowserRoot(rootKey) {
      if (!logicPathBrowser.open) {
        return;
      }
      requestLogicPathBrowserListing(logicPathBrowser.scope, rootKey, "", {});
    }

    function navigateLogicPathBrowser(browsePath) {
      if (!logicPathBrowser.open) {
        return;
      }
      requestLogicPathBrowserListing(logicPathBrowser.scope, logicPathBrowser.rootKey, browsePath || "", {});
    }

    function selectLogicPathBrowserValue(value) {
      const nodeId = `${logicPathBrowser.nodeId || ""}`.trim();
      const fieldKey = `${logicPathBrowser.fieldKey || ""}`.trim();
      if (!nodeId || !fieldKey) {
        closeLogicPathBrowser();
        return;
      }
      patchLogicNodeConfigById(nodeId, fieldKey, value);
      closeLogicPathBrowser();
    }

    function toggleLogicNodeEnabled(nodeId) {
      if (!nodeId) {
        return;
      }
      mutateLogicDraft((draft) => {
        draft.nodes = draft.nodes.map((node) => node.nodeId === nodeId
          ? { ...node, enabled: node.enabled === false }
          : node);
        return draft;
      });
    }

    function saveLogicGraph() {
      if (!ensureAuthed()) {
        return;
      }
      setError("logic:main", "");
      const draftGraph = logicDraftGraphRef.current;
      const targetGraphId = logicSelectedGraphIdRef.current || draftGraph?.graphId || "";
      const ok = requestLogicGraphSave(send, targetGraphId, draftGraph);
      if (!ok) {
        setError("logic:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function runLogicGraph() {
      if (!ensureAuthed()) {
        return;
      }
      const draftGraph = logicDraftGraphRef.current;
      const targetGraphId = logicSelectedGraphIdRef.current || draftGraph?.graphId || "";
      if (!targetGraphId) {
        setError("logic:main", "먼저 저장된 작업 흐름을 선택하거나 저장하세요.");
        return;
      }
      setError("logic:main", "");
      const startNode = (draftGraph?.nodes || []).find((n) => n.type === "start");
      const runInput = (startNode?.config?.input || "").trim();
      const ok = requestLogicGraphRun(send, targetGraphId, runInput);
      if (!ok) {
        setError("logic:main", "오류: WebSocket 연결이 끊어졌습니다.");
        return;
      }
      setLogicRunOutputExpanded(true);
    }

    function runLogicGraphById(graphId) {
      const targetGraphId = `${graphId || ""}`.trim();
      if (!ensureAuthed() || !targetGraphId) {
        return;
      }
      setError("logic:main", "");
      const ok = requestLogicGraphRun(send, targetGraphId, ((logicDraftGraphRef.current?.nodes || []).find((n) => n.type === "start")?.config?.input || "").trim());
      if (!ok) {
        setError("logic:main", "오류: WebSocket 연결이 끊어졌습니다.");
        return;
      }
      setLogicRunOutputExpanded(true);
    }

    function cancelLogicGraphRun(runId) {
      if (!ensureAuthed() || !runId) {
        return;
      }
      requestLogicGraphCancel(send, runId, { silent: true, queueIfClosed: false });
    }

    function refreshLogicRun(runId) {
      if (!ensureAuthed() || !runId) {
        return;
      }
      requestLogicGraphRunGet(send, runId, { silent: true, queueIfClosed: false });
    }

    function exportLogicGraphJson() {
      const serialized = JSON.stringify(logicDraftGraphRef.current || {}, null, 2);
      setLogicJsonBuffer(serialized);
      if (typeof navigator !== "undefined" && navigator.clipboard?.writeText) {
        navigator.clipboard.writeText(serialized).catch(() => {});
      }
      setLogicLastMessage("현재 작업 흐름 JSON을 복사했습니다.");
    }

    function importLogicGraphJson() {
      try {
        const parsed = JSON.parse(logicJsonBuffer);
        const nextGraph = cloneLogicGraph({
          ...parsed,
          viewport: normalizeLogicViewport(parsed?.viewport)
        });
        logicAutoFrameKeyRef.current = "";
        logicDraftGraphRef.current = nextGraph;
        logicSelectedGraphIdRef.current = parsed.graphId || "";
        setLogicDraftGraph(nextGraph);
        setLogicSelectedGraphId(parsed.graphId || "");
        setLogicSelectedNodeId("");
        setLogicSelectedEdgeId("");
        closeLogicPathBrowser();
        resetLogicConnectionState();
        setLogicDirty(true);
        setLogicLastMessage("JSON으로 작업 흐름을 불러왔습니다.");
      } catch (error) {
        setError("logic:main", `JSON 형식이 잘못됐습니다: ${error?.message || error}`);
      }
    }

    function patchRoutineForm(formType, patch) {
      const setter = formType === "edit" ? setRoutineEditForm : setRoutineCreateForm;
      setter((prev) => ({ ...prev, ...patch }));
    }

    function toggleRoutineWeekday(formType, weekday) {
      const setter = formType === "edit" ? setRoutineEditForm : setRoutineCreateForm;
      setter((prev) => {
        const current = normalizeRoutineWeekdays(prev.weekdays || []);
        const exists = current.includes(weekday);
        const nextWeekdays = exists
          ? current.filter((value) => value !== weekday)
          : normalizeRoutineWeekdays([...current, weekday]);
        return {
          ...prev,
          weekdays: nextWeekdays
        };
      });
    }

    function createRoutineFromUi() {
      if (!ensureAuthed()) {
        return;
      }

      const payload = buildRoutinePayloadFromForm(routineCreateForm);
      if (!payload.text) {
        setError("routine:main", "루틴 요청을 입력하세요.");
        return;
      }

      setError("routine:main", "");
      const ok = send({ type: "create_routine", ...payload });
      if (ok) {
        const now = Date.now();
        setRoutineProgress(createRoutineProgressState({
          active: true,
          operation: "create",
          percent: 6,
          message: "루틴 생성 요청을 전송했습니다.",
          stageKey: "request_analysis",
          stageTitle: "요청 분석",
          stageDetail: "스케줄과 실행 경로를 확인하고 있습니다.",
          stageIndex: 1,
          stageTotal: 5,
          done: false,
          ok: null,
          startedAt: now,
          updatedAt: now,
          completedAt: 0
        }));
        setRoutineCreateForm((prev) => createRoutineFormState({
          executionMode: prev.executionMode,
          agentProvider: prev.agentProvider,
          agentModel: prev.agentModel,
          agentStartUrl: prev.agentStartUrl,
          agentTimeoutSeconds: prev.agentTimeoutSeconds,
          agentToolProfile: prev.agentToolProfile,
          agentUsePlaywright: prev.agentUsePlaywright !== false,
          scheduleSourceMode: normalizeRoutineScheduleSourceMode(prev.scheduleSourceMode, "auto"),
          maxRetries: Math.min(5, Math.max(0, Number(prev.maxRetries ?? 1) || 0)),
          retryDelaySeconds: Math.min(300, Math.max(0, Number(prev.retryDelaySeconds ?? 15) || 0)),
          notifyPolicy: normalizeRoutineNotifyPolicy(prev.notifyPolicy, "always"),
          notifyTelegram: normalizeRoutineNotifyTelegram(prev.notifyTelegram, true),
          scheduleKind: prev.scheduleKind,
          scheduleTime: prev.scheduleTime,
          dayOfMonth: prev.dayOfMonth,
          weekdays: normalizeRoutineWeekdays(prev.weekdays || []),
          timezoneId: prev.timezoneId || getRoutineLocalTimezone()
        }));
      } else {
        const now = Date.now();
        setRoutineProgress(createRoutineProgressState({
          active: false,
          operation: "create",
          percent: 0,
          message: "오류: WebSocket 연결이 끊어졌습니다.",
          stageKey: "request_analysis",
          stageTitle: "요청 분석",
          stageDetail: "루틴 생성 요청을 보내지 못했습니다.",
          stageIndex: 1,
          stageTotal: 5,
          done: true,
          ok: false,
          startedAt: now,
          updatedAt: now,
          completedAt: now
        }));
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function updateRoutineFromUi() {
      if (!ensureAuthed() || !routineSelectedId) {
        return;
      }

      const payload = buildRoutinePayloadFromForm(routineEditForm);
      if (!payload.text) {
        setError("routine:main", "루틴 요청을 입력하세요.");
        return;
      }

      setError("routine:main", "");
      if (!send({ type: "update_routine", routineId: routineSelectedId, ...payload })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function setRoutineTelegramResponseEnabled(routineId, notifyTelegram) {
      if (!ensureAuthed() || !routineId) {
        return;
      }

      const target = routines.find((item) => item.id === routineId);
      if (!target) {
        setError("routine:main", "오류: 루틴을 찾을 수 없습니다.");
        return;
      }

      const payload = {
        ...buildRoutinePayloadFromForm(hydrateRoutineFormFromRoutine(target)),
        notifyTelegram: !!notifyTelegram
      };
      setRoutineEditForm((prev) => ({
        ...prev,
        notifyTelegram: !!notifyTelegram
      }));
      setError("routine:main", "");
      if (!send({ type: "update_routine", routineId, ...payload })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function runRoutineNow(routineId) {
      if (!ensureAuthed() || !routineId) {
        return;
      }

      setError("routine:main", "");
      if (!send({ type: "run_routine", routineId })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function testRoutineTelegram(routineId) {
      if (!ensureAuthed() || !routineId) {
        return;
      }

      setError("routine:main", "");
      if (!send({ type: "test_routine_telegram", routineId })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function testRoutineBrowserAgent(routineId) {
      if (!ensureAuthed() || !routineId) {
        return;
      }

      routineBrowserAgentPreviewRef.current = routineId;
      setError("routine:main", "");
      if (!send({ type: "test_browser_agent_routine", routineId })) {
        routineBrowserAgentPreviewRef.current = "";
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function openRoutineRunDetail(routineId, ts) {
      if (!ensureAuthed() || !routineId || !ts) {
        return;
      }

      setError("routine:main", "");
      if (!send({ type: "get_routine_run_detail", routineId, ts })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function resendRoutineRunTelegram(routineId, ts) {
      if (!ensureAuthed() || !routineId || !ts) {
        return;
      }

      setError("routine:main", "");
      if (!send({ type: "resend_routine_run_telegram", routineId, ts })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function setRoutineEnabled(routineId, enabled) {
      if (!ensureAuthed() || !routineId) {
        return;
      }

      setError("routine:main", "");
      if (!send({ type: "toggle_routine", routineId, enabled: !!enabled })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function deleteRoutineById(routineId) {
      if (!ensureAuthed() || !routineId) {
        return;
      }

      setError("routine:main", "");
      if (!send({ type: "delete_routine", routineId })) {
        setError("routine:main", "오류: WebSocket 연결이 끊어졌습니다.");
      }
    }

    function onInputKeyDown(event, handler) {
      const native = event.nativeEvent || {};
      if (event.isComposing || native.isComposing || native.keyCode === 229) {
        return;
      }

      const sendShortcut = uiPreferences.shortcuts?.send || DEFAULT_UI_PREFERENCES.shortcuts.send;
      if ((shortcutMatches(event, sendShortcut) || (event.key === "Enter" && !event.shiftKey)) && typeof handler === "function") {
        event.preventDefault();
        handler();
      }
    }

    function sendCurrentComposer() {
      if (rootTab === "chat") {
        if (chatMode === "single") {
          sendChatSingle();
        } else if (chatMode === "orchestration") {
          sendChatOrchestration();
        } else {
          sendChatMulti();
        }
        return;
      }

      if (rootTab === "coding") {
        if (codingMode === "single") {
          sendCodingSingle();
        } else if (codingMode === "orchestration") {
          sendCodingOrchestration();
        } else {
          sendCodingMulti();
        }
      }
    }

    // 매 렌더마다 sendCurrentComposer ref를 최신화 (음성 자동 전송 등에서 stale closure 회피)
    sendCurrentComposerRef.current = sendCurrentComposer;

    function getRichInputPayload(inputText = "") {
      return buildRichInputPayload({
        inputText,
        attachments: currentAttachments
      });
    }

    function clearRichInputDraft(key) {
      setAttachmentsByKey((prev) => clearAttachmentDraft(prev, key));
    }

    async function appendAttachmentsForKey(key, fileList) {
      const normalizedKey = `${key || currentKey}`.trim() || currentKey;
      const safeFileList = Array.isArray(fileList) ? fileList : [];
      if (safeFileList.length === 0) {
        return;
      }

      const next = await buildNextAttachments({
        existing: attachmentsByKey[normalizedKey] || [],
        fileList: safeFileList,
        onError: (message) => log(message, "error")
      });
      setAttachmentsByKey((prev) => ({ ...prev, [normalizedKey]: next }));
    }

    async function onAttachmentSelected(event) {
      const fileList = event.target.files ? Array.from(event.target.files) : [];
      if (fileList.length === 0) {
        return;
      }

      await appendAttachmentsForKey(currentKey, fileList);
      event.target.value = "";
    }

    function handleAttachmentDragOver(event) {
      if (!hasDraggedFiles(event.dataTransfer)) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      if (event.dataTransfer) {
        event.dataTransfer.dropEffect = "copy";
      }
    }

    async function handleAttachmentDrop(event) {
      if (!hasDraggedFiles(event.dataTransfer)) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      const fileList = event.dataTransfer && event.dataTransfer.files
        ? Array.from(event.dataTransfer.files)
        : [];
      clearAttachmentDragState(currentKey);
      if (fileList.length === 0) {
        return;
      }

      await appendAttachmentsForKey(currentKey, fileList);
    }

    function normalizeModelChoice(value, fallback) {
      const trimmed = (value || "").trim();
      return trimmed || fallback;
    }

    function isNoneModel(value) {
      return (value || "").trim().toLowerCase() === NONE_MODEL;
    }
    const {
      groqModelOptions,
      copilotModelOptions,
      codexModelOptions,
      geminiModelOptions,
      nvidiaModelOptions,
      routineAgentProviderOptions,
      routineAgentModelOptions,
      groqWorkerModelOptions,
      geminiWorkerModelOptions,
      nvidiaWorkerModelOptions,
      copilotWorkerModelOptions,
      codexWorkerModelOptions
    } = useMemo(
      () => buildDashboardModelOptionSets({
        e,
        groqModels,
        copilotModels,
        codeXModelChoices: CODEX_MODEL_CHOICES,
        geminiModelChoices: GEMINI_MODEL_CHOICES,
        nvidiaModelChoices: NVIDIA_MODEL_CHOICES,
        noneModel: NONE_MODEL,
        defaultGroqWorkerModel: DEFAULT_GROQ_WORKER_MODEL,
        defaultNvidiaModel: DEFAULT_NVIDIA_MODEL,
        defaultRoutineAgentProvider: DEFAULT_ROUTINE_AGENT_PROVIDER,
        defaultRoutineAgentModel: DEFAULT_ROUTINE_AGENT_MODEL
      }),
      [groqModels, copilotModels]
    );

    const logicInspectorCatalogs = useMemo(() => {
      const dedupeOptions = (items) => {
        const seen = new Set();
        return (Array.isArray(items) ? items : []).filter((item) => {
          const value = `${item?.value || ""}`;
          if (seen.has(value)) {
            return false;
          }
          seen.add(value);
          return true;
        });
      };

      const groqBaseOptions = Array.isArray(groqModels) && groqModels.length > 0
        ? groqModels.map((item) => ({ value: item.id, label: item.id }))
        : [{ value: "", label: "Groq 모델 로딩 전" }];
      const copilotBaseOptions = Array.isArray(copilotModels) && copilotModels.length > 0
        ? copilotModels.map((item) => ({ value: item.id, label: item.id }))
        : [{ value: "", label: "Copilot 모델 로딩 전" }];
      const cerebrasBaseOptions = Array.isArray(cerebrasModels) && cerebrasModels.length > 0
        ? cerebrasModels.map((item) => ({ value: item.id, label: item.owned_by ? `${item.id} · ${item.owned_by}` : item.id }))
        : CEREBRAS_MODEL_CHOICES.map((item) => ({ value: item.id, label: item.label }));
      const nvidiaBaseOptions = NVIDIA_MODEL_CHOICES.map((item) => ({ value: item.id, label: item.label }));

      return {
        groqModelOptions: groqBaseOptions,
        copilotModelOptions: copilotBaseOptions,
        codexModelOptions: CODEX_MODEL_CHOICES.map((item) => ({ value: item.id, label: item.label })),
        geminiModelOptions: GEMINI_MODEL_CHOICES.map((item) => ({ value: item.id, label: item.label })),
        cerebrasModelOptions: cerebrasBaseOptions,
        nvidiaModelOptions: nvidiaBaseOptions,
        groqWorkerOptions: dedupeOptions([
          { value: NONE_MODEL, label: "Groq: 선택 안 함" },
          { value: DEFAULT_GROQ_WORKER_MODEL, label: `Groq 기본: ${DEFAULT_GROQ_WORKER_MODEL}` },
          ...groqBaseOptions
        ]),
        geminiWorkerOptions: dedupeOptions([
          { value: NONE_MODEL, label: "Gemini: 선택 안 함" },
          ...GEMINI_MODEL_CHOICES.map((item) => ({ value: item.id, label: item.label }))
        ]),
        cerebrasWorkerOptions: dedupeOptions([
          { value: NONE_MODEL, label: "Cerebras: 선택 안 함" },
          ...cerebrasBaseOptions
        ]),
        nvidiaWorkerOptions: dedupeOptions([
          { value: NONE_MODEL, label: "NVIDIA NIM: 선택 안 함" },
          { value: DEFAULT_NVIDIA_MODEL, label: `NVIDIA NIM 기본: ${DEFAULT_NVIDIA_MODEL}` },
          ...nvidiaBaseOptions
        ]),
        copilotWorkerOptions: dedupeOptions([
          { value: NONE_MODEL, label: "Copilot: 선택 안 함" },
          ...copilotBaseOptions
        ]),
        codexWorkerOptions: dedupeOptions([
          { value: NONE_MODEL, label: "Codex: 선택 안 함" },
          ...CODEX_MODEL_CHOICES.map((item) => ({ value: item.id, label: item.label }))
        ]),
        routineOptions: (Array.isArray(routines) ? routines : []).map((item) => ({
          value: item?.id || "",
          label: item?.title
            ? `${item.title} (${item.id || "-"})`
            : (item?.id || "")
        }))
      };
    }, [groqModels, copilotModels, cerebrasModels, routines]);

    const {
      groqRows,
      copilotRows,
      copilotPremiumPercent,
      copilotPremiumQuotaText,
      copilotPremiumRows,
      copilotLocalRows
    } = useMemo(
      () => buildSettingsModelTableState({
        e,
        groqModels,
        groqUsageWindowBaseByModel,
        copilotModels,
        copilotPremiumUsage,
        copilotLocalUsage,
        formatDecimal
      }),
      [groqModels, groqUsageWindowBaseByModel, copilotModels, copilotPremiumUsage, copilotLocalUsage]
    );

    function renderGlobalNav() {
      return renderGlobalNavModule({
        e,
        authed,
        status,
        rootTab,
        setRootTab,
        selectedGroqModel,
        selectedCopilotModel,
        settingsState,
        copilotStatus,
        codexStatus,
        defaultCodexModel: DEFAULT_CODEX_MODEL,
        defaultNvidiaModel: DEFAULT_NVIDIA_MODEL,
        defaultCerebrasModel: DEFAULT_CEREBRAS_MODEL
      });
    }

    function renderModeTabs() {
      return renderModeTabsModule({
        e,
        rootTab,
        chatMode,
        codingMode,
        chatModes: CHAT_MODES,
        codingModes: CODING_MODES,
        setChatMode,
        setCodingMode
      });
    }

    function renderConversationPanel() {
      return renderConversationPanelModule({
        e,
        currentConversationFilter,
        setConversationFilterByKey,
        currentKey,
        rootTab,
        scope,
        mode,
        currentConversationList,
        groupedConversationList,
        isFolderExpanded,
        currentSelectedFolders,
        selectionMode,
        toggleFolderSelection,
        toggleFolder,
        currentSelectedConversationIds,
        toggleConversationSelection,
        currentConversationId,
        selectConversation,
        toneForCategory,
        buildConversationAvatarText,
        formatConversationUpdatedLabel,
        createConversation,
        metaProject,
        metaCategory,
        parseTags,
        metaTags,
        toggleSelectionMode,
        clearScopeMemory,
        selectedDeleteConversationIds,
        deleteConversation,
        conversationSearchState: currentConversationSearchState,
        onConversationSearch: () => requestConversationSearch(),
        clearConversationSearch: clearCurrentConversationSearch,
        modeItems: rootTab === "coding" ? CODING_MODES : CHAT_MODES,
        activeMode: mode,
        onSelectMode: rootTab === "coding" ? setCodingMode : setChatMode
      });
    }

    function toggleMemoryNote(noteName, checked) {
      if (!currentConversationId) {
        return;
      }

      const next = checked
        ? currentMemoryNotes.concat([noteName])
        : currentMemoryNotes.filter((x) => x !== noteName);
      setSelectedMemoryByConversation((prev) => ({ ...prev, [currentConversationId]: next }));
    }

    function renderMemoryPicker() {
      return renderMemoryPickerModule({
        e,
        currentConversationId,
        send,
        createManualMemoryNote,
        currentCheckedMemoryNotes,
        deleteSelectedMemoryNotes,
        setMemoryPickerOpen,
        memoryNotes,
        currentMemoryNotes,
        toggleMemoryNote,
        renameMemoryNote
      });
    }

    function renderThreadInfoPanel(previewMeta) {
      return renderThreadInfoPanelModule({
        e,
        previewMeta,
        metaTitle,
        setMetaTitle,
        metaProject,
        setMetaProject,
        metaCategory,
        setMetaCategory,
        metaTags,
        setMetaTags,
        toneForCategory,
        currentConversationId,
        saveConversationMeta
      });
    }

    function renderThreadModebar(extraClassName = "") {
      return renderThreadModebarModule({
        e,
        rootTab,
        renderModeTabs,
        extraClassName
      });
    }

    function renderThreadHeader(options = {}) {
      return renderThreadHeaderModule({
        e,
        rootTab,
        currentConversationId,
        currentConversationTitle,
        scope,
        mode,
        currentMemoryNotesCount: currentMemoryNotes.length,
        threadInfoOpen,
        memoryPickerOpen,
        toneForCategory,
        buildThreadPreviewMeta,
        toggleThreadInfoPanel,
        setMemoryPickerOpen,
        isPortraitMobileLayout,
        setResponsivePane,
        responsiveWorkspaceKey,
        renderThreadModebar,
        renderThreadInfoPanel,
        ttsSupported: isSpeechSynthesisSupported(),
        autoSpeakOn: !!uiPreferences.speech?.autoSpeak,
        onToggleAutoSpeak: () => updateUiPreference({
          speech: { ...(uiPreferences.speech || {}), autoSpeak: !uiPreferences.speech?.autoSpeak }
        }),
        options
      });
    }

    function speakMessageById(id, text) {
      if (!isSpeechSynthesisSupported()) return;
      const trimmedId = `${id || ""}`;
      // 같은 메시지 재클릭 → 정지 토글
      if (trimmedId && trimmedId === speakingMessageId) {
        stopSpeaking();
        setSpeakingMessageId("");
        return;
      }
      // 다른 메시지 재생 중이면 정지
      stopSpeaking();
      const cleaned = sanitizeForSpeech(text, {
        stripMarkdown: uiPreferences.speech?.stripMarkdown !== false
      });
      if (!cleaned) {
        setSpeakingMessageId("");
        return;
      }
      try {
        const utter = new SpeechSynthesisUtterance(cleaned);
        const prefs = uiPreferences.speech || {};
        utter.lang = prefs.lang || "ko-KR";
        utter.rate = clampNumber(prefs.rate, 0.5, 2, 1);
        utter.pitch = clampNumber(prefs.pitch, 0.5, 2, 1);
        utter.volume = clampNumber(prefs.volume, 0, 1, 1);
        if (prefs.voiceURI) {
          const match = listSpeechVoices().find((v) => v.voiceURI === prefs.voiceURI);
          if (match) {
            utter.voice = match;
            if (match.lang) utter.lang = match.lang;
          }
        }
        const reset = () => setSpeakingMessageId((prev) => (prev === trimmedId ? "" : prev));
        utter.onend = reset;
        utter.onerror = reset;
        window.speechSynthesis.speak(utter);
        setSpeakingMessageId(trimmedId);
      } catch {
        setSpeakingMessageId("");
      }
    }

    function renderMessages() {
      return renderMessagesPanelModule({
        e,
        MarkdownBubbleText,
        currentKey,
        currentConversationId,
        currentMessages,
        optimisticUserByKey,
        pendingByKey,
        codingProgressByKey,
        isConversationBoundEntryVisible,
        elapsedSeconds,
        sanitizeCodingAssistantText,
        messageListRef,
        parseChatMultiComparisonMessage: chatMultiUtils.parseChatMultiComparisonMessage,
        parseCodingMultiComparisonMessage: chatMultiUtils.parseCodingMultiComparisonMessage,
        ttsSupported: isSpeechSynthesisSupported(),
        speakingMessageId,
        onToggleSpeakMessage: (id, text) => speakMessageById(id, text)
      });
    }

    function renderCodingResult() {
      return renderCodingResultPanel({
        e,
        MarkdownBubbleText,
        rootTab,
        currentConversationId,
        codingResultByConversation,
        filePreviewByConversation,
        runtimeByConversation: codingRuntimeByConversation,
        executionInputByConversation: codingExecutionInputByConversation,
        showExecutionLogsByConversation,
        sanitizeCodingAssistantText,
        buildCodingMultiRenderSnapshot: chatMultiUtils.buildCodingMultiRenderSnapshot,
        requestWorkspaceFilePreview,
        requestLatestCodingResultExecution,
        humanPath,
        setCodingExecutionInputByConversation,
        setShowExecutionLogsByConversation
      });
    }

    function buildCodingResultRendererProps() {
      return buildCodingResultRendererPropsModule({
        e,
        MarkdownBubbleText,
        rootTab,
        currentConversationId,
        codingResultByConversation,
        filePreviewByConversation,
        runtimeByConversation: codingRuntimeByConversation,
        executionInputByConversation: codingExecutionInputByConversation,
        showExecutionLogsByConversation,
        sanitizeCodingAssistantText,
        buildCodingMultiRenderSnapshot: chatMultiUtils.buildCodingMultiRenderSnapshot,
        requestWorkspaceFilePreview,
        requestLatestCodingResultExecution,
        humanPath,
        setCodingExecutionInputByConversation,
        setShowExecutionLogsByConversation,
        actions: {
          openOverlay: () => {
            setSafeRefactorOverlayOpen(false);
            setCodingResultOverlayOpen(true);
          },
          closeOverlay: () => setCodingResultOverlayOpen(false)
        }
      });
    }

    function renderCodingResultDock() {
      return renderCodingResultDockShell({
        ...buildCodingResultRendererProps(),
        open: codingResultOverlayOpen
      });
    }

    function renderCodingResultOverlay() {
      return renderCodingResultOverlayShell({
        ...buildCodingResultRendererProps(),
        open: codingResultOverlayOpen
      });
    }

    function buildSafeRefactorRendererProps() {
      const selectedLines = getRefactorSelectedLines();
      const selectedStart = parseRefactorLineNumber(refactorState.selectedStartLine);
      const selectedEnd = parseRefactorLineNumber(refactorState.selectedEndLine);
      const safeStart = selectedStart && selectedEnd ? Math.min(selectedStart, selectedEnd) : 0;
      const safeEnd = selectedStart && selectedEnd ? Math.max(selectedStart, selectedEnd) : 0;
      const selectedSummary = `${refactorState.mode || "anchor"}` === "anchor" && selectedLines.length > 0
        ? `${safeStart}-${safeEnd}줄 · ${selectedLines.length}개 기준 줄`
        : "";
      const currentSnippet = `${refactorState.mode || "anchor"}` === "anchor" && selectedLines.length > 0
        ? selectedLines.map((line) => line.content || "").join("\n")
        : "";

      return buildSafeRefactorRendererPropsModule({
        e,
        rootTab,
        state: refactorState,
        selectedLines,
        currentSnippet,
        selectedSummary,
        helpers: {
          humanWorkspacePath
        },
        actions: {
          setMode: (value) => setRefactorState((prev) => ({
            ...prev,
            mode: value || "anchor",
            preview: null,
            lastError: "",
            lastIssues: [],
            lastMessage: ""
          })),
          setFilePath: (value) => setRefactorState((prev) => ({
            ...prev,
            filePath: value,
            preview: null,
            lastError: ""
          })),
          setSelectedStartLine: (value) => setRefactorState((prev) => ({
            ...prev,
            selectedStartLine: value,
            preview: null,
            lastError: ""
          })),
          setSelectedEndLine: (value) => setRefactorState((prev) => ({
            ...prev,
            selectedEndLine: value,
            preview: null,
            lastError: ""
          })),
          setReplacement: (value) => setRefactorState((prev) => ({
            ...prev,
            replacement: value,
            preview: null,
            lastError: ""
          })),
          setSymbol: (value) => setRefactorState((prev) => ({
            ...prev,
            symbol: value,
            preview: null,
            lastError: ""
          })),
          setNewName: (value) => setRefactorState((prev) => ({
            ...prev,
            newName: value,
            preview: null,
            lastError: ""
          })),
          setPattern: (value) => setRefactorState((prev) => ({
            ...prev,
            pattern: value,
            preview: null,
            lastError: ""
          })),
          selectAnchorLine: selectRefactorLine,
          readAnchors: readRefactorAnchors,
          previewRefactor: previewSafeRefactor,
          applyRefactor: applySafeRefactor,
          openOverlay: () => {
            setCodingResultOverlayOpen(false);
            setSafeRefactorOverlayOpen(true);
          },
          closeOverlay: () => setSafeRefactorOverlayOpen(false)
        }
      });
    }

    function renderSafeRefactorPanel() {
      return renderSafeRefactorPanelShell(buildSafeRefactorRendererProps());
    }

    function renderSafeRefactorDock() {
      return renderSafeRefactorDockShell({
        ...buildSafeRefactorRendererProps(),
        open: safeRefactorOverlayOpen
      });
    }

    function renderSafeRefactorOverlay() {
      return renderSafeRefactorOverlayShell({
        ...buildSafeRefactorRendererProps(),
        open: safeRefactorOverlayOpen
      });
    }

    function renderChatMultiResult() {
      return renderChatMultiResultPanel({
        e,
        MarkdownBubbleText,
        rootTab,
        mode,
        currentConversationId,
        chatMultiResultByConversation,
        buildChatMultiRenderSnapshot
      });
    }

    function renderComposerInputBar({ value, onChange, onSend, pendingKey, placeholder }) {
      const thinkPlusVisible = pendingKey?.startsWith("chat:") || pendingKey?.startsWith("coding:");
      const thinkPlusReady = !!geminiUsage?.totals || true; // Gemini API 키 가용 여부는 백엔드가 더 정확히 알지만 UI는 일단 활성화 후 백엔드에서 fallback
      return renderComposerInputBarShell({
        e,
        value,
        onChange,
        onSend,
        pending: isRequestPending(pendingKey),
        placeholder,
        attachmentPanelVisible,
        toggleAttachmentPanel,
        autoResizeComposerTextarea,
        onInputKeyDown,
        attachments: currentAttachments,
        attachmentDragActive,
        handleAttachmentDragOver,
        handleAttachmentDrop,
        attachmentFileInputId,
        onAttachmentSelected,
        onClearAttachments: () => setAttachmentsByKey((prev) => clearAttachmentDraft(prev, currentKey)),
        selectedSkill: (currentKey.startsWith("chat:") || currentKey.startsWith("coding:")) ? selectedChatSkill : null,
        onClearSkill: () => setSelectedChatSkill(null),
        speechState,
        onStartVoiceInput: startVoiceInput,
        thinkPlusVisible,
        thinkPlusEnabled: thinkPlusVisible ? !!thinkPlusModeByKey[pendingKey] : false,
        thinkPlusReady,
        onToggleThinkPlus: thinkPlusVisible
          ? () => setThinkPlusModeByKey((prev) => ({ ...prev, [pendingKey]: !prev[pendingKey] }))
          : undefined
      });
    }

    function renderThreadSupportStack() {
      return renderThreadSupportStackShell({
        e,
        renderChatMultiResult,
        renderCodingResult: () => null,
        renderSafeRefactorPanel: () => null,
        memoryPickerOpen,
        renderMemoryPicker
      });
    }

    function renderResponsiveWorkspaceSupportPane() {
      return renderResponsiveWorkspaceSupportPaneShell({
        e,
        currentConversationId,
        renderThreadModebar,
        renderThreadInfoPanel,
        buildThreadPreviewMeta,
        renderMemoryPicker,
        renderChatMultiResult,
        renderCodingResult: rootTab === "coding" ? renderCodingResult : () => null,
        renderSafeRefactorPanel: () => null
      });
    }

    function renderChatComposer() {
      return renderChatComposerShell({
        e,
        mode,
        renderComposerInputBar,
        constants: {
          NONE_MODEL,
          DEFAULT_GROQ_SINGLE_MODEL,
          DEFAULT_CODEX_MODEL,
          DEFAULT_GEMINI_WORKER_MODEL,
          DEFAULT_CEREBRAS_MODEL,
          DEFAULT_NVIDIA_MODEL,
          CEREBRAS_MODEL_CHOICES
        },
        optionSets: {
          groqModelOptions,
          copilotModelOptions,
          codexModelOptions,
          geminiModelOptions,
          nvidiaModelOptions,
          groqWorkerModelOptions,
          geminiWorkerModelOptions,
          nvidiaWorkerModelOptions,
          copilotWorkerModelOptions,
          codexWorkerModelOptions
        },
        selectedModels: {
          selectedGroqModel,
          selectedCopilotModel
        },
        values: {
          chatSingleProvider,
          chatSingleModel,
          chatInputSingle,
          chatOrchProvider,
          chatOrchModel,
          chatInputOrch,
          chatOrchGroqModel,
          chatOrchGeminiModel,
          chatOrchCerebrasModel,
          chatOrchNvidiaModel,
          chatOrchCopilotModel,
          chatOrchCodexModel,
          chatMultiGroqModel,
          chatMultiGeminiModel,
          chatMultiCerebrasModel,
          chatMultiNvidiaModel,
          chatMultiCopilotModel,
          chatMultiCodexModel,
          chatMultiSummaryProvider,
          chatInputMulti
        },
        setters: {
          setChatSingleProvider,
          setChatSingleModel,
          setChatInputSingle,
          setChatOrchProvider,
          setChatOrchModel,
          setChatInputOrch,
          setChatOrchGroqModel,
          setChatOrchGeminiModel,
          setChatOrchCerebrasModel,
          setChatOrchNvidiaModel,
          setChatOrchCopilotModel,
          setChatOrchCodexModel,
          setChatMultiGroqModel,
          setChatMultiGeminiModel,
          setChatMultiCerebrasModel,
          setChatMultiNvidiaModel,
          setChatMultiCopilotModel,
          setChatMultiCodexModel,
          setChatMultiSummaryProvider,
          setChatInputMulti
        },
        helpers: {
          isNoneModel
        },
        actions: {
          sendChatSingle,
          sendChatOrchestration,
          sendChatMulti
        }
      });
    }

    function renderCodingComposer() {
      return renderCodingComposerShell({
        e,
        mode,
        renderComposerInputBar,
        codingResultDock: renderCodingResultDock(),
        safeRefactorDock: renderSafeRefactorDock(),
        constants: {
          NONE_MODEL,
          DEFAULT_CODEX_MODEL,
          DEFAULT_GEMINI_WORKER_MODEL,
          DEFAULT_CEREBRAS_MODEL,
          DEFAULT_NVIDIA_MODEL,
          CEREBRAS_MODEL_CHOICES
        },
        optionSets: {
          groqModelOptions,
          copilotModelOptions,
          codexModelOptions,
          geminiModelOptions,
          nvidiaModelOptions,
          groqWorkerModelOptions,
          geminiWorkerModelOptions,
          nvidiaWorkerModelOptions,
          copilotWorkerModelOptions,
          codexWorkerModelOptions
        },
        selectedModels: {
          selectedGroqModel,
          selectedCopilotModel
        },
        values: {
          codingSingleProvider,
          codingSingleModel,
          codingSingleLanguage,
          codingInputSingle,
          codingOrchProvider,
          codingOrchModel,
          codingOrchLanguage,
          codingInputOrch,
          codingOrchGroqModel,
          codingOrchGeminiModel,
          codingOrchCerebrasModel,
          codingOrchNvidiaModel,
          codingOrchCopilotModel,
          codingOrchCodexModel,
          codingMultiProvider,
          codingMultiModel,
          codingMultiLanguage,
          codingInputMulti,
          codingMultiGroqModel,
          codingMultiGeminiModel,
          codingMultiCerebrasModel,
          codingMultiNvidiaModel,
          codingMultiCopilotModel,
          codingMultiCodexModel
        },
        setters: {
          setCodingSingleProvider,
          setCodingSingleModel,
          setCodingSingleLanguage,
          setCodingInputSingle,
          setCodingOrchProvider,
          setCodingOrchModel,
          setCodingOrchLanguage,
          setCodingInputOrch,
          setCodingOrchGroqModel,
          setCodingOrchGeminiModel,
          setCodingOrchCerebrasModel,
          setCodingOrchNvidiaModel,
          setCodingOrchCopilotModel,
          setCodingOrchCodexModel,
          setCodingMultiProvider,
          setCodingMultiModel,
          setCodingMultiLanguage,
          setCodingInputMulti,
          setCodingMultiGroqModel,
          setCodingMultiGeminiModel,
          setCodingMultiCerebrasModel,
          setCodingMultiNvidiaModel,
          setCodingMultiCopilotModel,
          setCodingMultiCodexModel
        },
        helpers: {
          isNoneModel
        },
        actions: {
          sendCodingSingle,
          sendCodingOrchestration,
          sendCodingMulti
        }
      });
    }

    function renderWorkspace() {
      return renderWorkspaceShell({
        e,
        React,
        rootTab,
        isPortraitMobileLayout,
        mobileWorkspaceHeight,
        currentWorkspacePane,
        mobileComposerOpen,
        responsiveWorkspaceKey,
        setResponsivePane,
        setMobileComposerOpen,
        currentKey,
        errorByKey,
        renderConversationPanel,
        renderThreadHeader,
        renderMessages,
        renderThreadSupportStack,
        renderResponsiveWorkspaceSupportPane,
        renderResponsiveSectionTabs,
        chatComposer: renderChatComposer(),
        codingComposer: renderCodingComposer()
      });
    }

    function renderRoutine() {
      return renderRoutineTabModule({
        e,
        routines,
        routineSelectedId,
        currentRoutinePane,
        isPortraitMobileLayout,
        errorByKey,
        routineCreateForm,
        routineEditForm,
        routineProgress,
        routineAgentProviderOptions,
        routineAgentModelOptions,
        patchRoutineForm,
        toggleRoutineWeekday,
        createRoutineFromUi,
        updateRoutineFromUi,
        onInputKeyDown,
        refreshRoutines,
        setRoutineSelectedId,
        setResponsivePane,
        runRoutineNow,
        testRoutineBrowserAgent,
        testRoutineTelegram,
        setRoutineTelegramResponseEnabled,
        setRoutineEnabled,
        deleteRoutineById,
        openRoutineRunDetail,
        resendRoutineRunTelegram,
        setRoutineOutputPreview,
        renderResponsiveSectionTabs
      });
    }

    function renderLogic() {
      const selectedNode = (logicDraftGraph?.nodes || []).find((item) => item.nodeId === logicSelectedNodeId) || null;
      const selectedEdge = (logicDraftGraph?.edges || []).find((item) => item.edgeId === logicSelectedEdgeId) || null;
      return renderLogicTabModule({
        e,
        graphs: logicGraphs,
        selectedGraphId: logicSelectedGraphId,
        draftGraph: logicDraftGraph,
        selectedNode,
        selectedEdge,
        selectedNodeId: logicSelectedNodeId,
        selectedEdgeId: logicSelectedEdgeId,
        pendingSourceNodeId: logicPendingSourceNodeId,
        connectionPreview: logicConnectionPreview,
        activeRunId: logicActiveRunId,
        runSnapshot: logicRunSnapshot,
        runEvents: logicRunEvents,
        runOutputExpanded: logicRunOutputExpanded,
        viewport: normalizeLogicViewport(logicDraftGraph?.viewport),
        canvasGesture: logicCanvasGesture,
        resolveNodeSize: (node) => normalizeRuntimeLogicNodeSize(node?.size, node || null, node?.type || ""),
        jsonBuffer: logicJsonBuffer,
        dirty: logicDirty,
        lastMessage: logicLastMessage,
        logicInspectorCatalogs,
        logicPathBrowser,
        paletteGroup: logicPaletteGroup,
        currentLogicPane,
        isPortraitMobileLayout,
        renderResponsiveSectionTabs,
        setResponsivePane,
        canvasShellRef: logicCanvasShellRef,
        listPaneRef: logicListPaneRef,
        palettePaneRef: logicPalettePaneRef,
        graphListRef: logicGraphListRef,
        paletteListRef: logicPaletteListRef,
        onScrollListMouseDown: beginLogicScrollListDrag,
        onScrollListClickCapture: handleLogicScrollListClickCapture,
        onSelectGraph: selectLogicGraph,
        onCreateNewGraph: createNewLogicGraphDraft,
        onDeleteGraph: (graphId) => {
          if (!ensureAuthed() || !graphId) {
            return;
          }
          requestLogicGraphDelete(send, graphId, { silent: true, queueIfClosed: false });
        },
        onExportGraph: exportLogicGraphJson,
        onJsonBufferChange: setLogicJsonBuffer,
        onImportGraph: importLogicGraphJson,
        onSelectNode: selectLogicNode,
        onSelectEdge: selectLogicEdge,
        onMoveNode: moveLogicNode,
        onCanvasPointerDown: beginLogicViewportPan,
        onNodePointerDown: beginLogicNodeDrag,
        onNodeResizePointerDown: beginLogicNodeResize,
        onConnectionStart: beginLogicEdgeConnection,
        onViewportReset: resetLogicViewport,
        onViewportFit: fitLogicViewport,
        onViewportZoomIn: () => stepLogicViewportZoom(1),
        onViewportZoomOut: () => stepLogicViewportZoom(-1),
        onDeleteEdge: deleteLogicEdge,
        onDeleteNode: deleteLogicNode,
        onDuplicateNode: duplicateLogicNode,
        onGraphFieldChange: patchLogicGraphField,
        onScheduleFieldChange: patchLogicScheduleField,
        onNodeFieldChange: patchLogicNodeField,
        onNodeConfigChange: patchLogicNodeConfig,
        onNodeBindingChange: patchLogicNodeBinding,
        onOpenPathBrowser: openLogicPathBrowser,
        onClosePathBrowser: closeLogicPathBrowser,
        onSelectPathBrowserRoot: changeLogicPathBrowserRoot,
        onNavigatePathBrowser: navigateLogicPathBrowser,
        onSelectPathBrowserValue: selectLogicPathBrowserValue,
        onToggleNodeEnabled: toggleLogicNodeEnabled,
        onPaletteGroupChange: setLogicPaletteGroup,
        onSelectNodeType: addLogicNode,
        onRun: runLogicGraph,
        onRunGraph: runLogicGraphById,
        onRunOutputExpandedChange: setLogicRunOutputExpanded,
        onCancelRun: cancelLogicGraphRun,
        onSave: saveLogicGraph,
        onOpenRunPreview: setLogicRunPreview,
        onRefresh: () => {
          refreshLogicGraphs();
          if (logicActiveRunId) {
            refreshLogicRun(logicActiveRunId);
          }
        }
      });
    }

    function renderToolControlPanel() {
      return renderToolControlPanelModule({
        e,
        authed,
        toolControlError,
        opsDomainFilter,
        applyDomainFocus,
        providerHealthSummary,
        toolDomainStats,
        providerRuntimeRows,
        guardObsStats,
        guardAlertSummary,
        formatGuardAlertThreshold,
        guardRetryTimeline,
        guardRetryTimelineRows,
        guardRetryTimelineSource,
        guardRetryTimelineApiFetchedAt,
        guardRetryTimelineApiError,
        guardAlertPipelineFieldRows: GUARD_ALERT_PIPELINE_FIELD_ROWS,
        submitGuardAlertDispatch,
        guardAlertDispatchState,
        guardAlertPipelinePreview,
        toolResultGroups: TOOL_RESULT_GROUPS,
        toolResultStats,
        toolResultFilter,
        setToolResultFilter,
        toolResultFilters: TOOL_RESULT_FILTERS,
        toolDomainFilters: TOOL_DOMAIN_FILTERS,
        toolResultItems,
        submitSessionsList,
        submitCronStatus,
        submitBrowserStatus,
        submitCanvasStatus,
        submitNodesStatus,
        toolSessionKey,
        setToolSessionKey,
        submitSessionsHistory,
        submitSessionSend,
        toolSpawnTask,
        setToolSpawnTask,
        submitSessionSpawn,
        toolSessionMessage,
        setToolSessionMessage,
        toolCronJobId,
        setToolCronJobId,
        submitCronList,
        submitCronRun,
        toolBrowserUrl,
        setToolBrowserUrl,
        submitBrowserNavigate,
        toolCanvasTarget,
        setToolCanvasTarget,
        submitCanvasPresent,
        toolNodesNode,
        setToolNodesNode,
        toolNodesRequestId,
        setToolNodesRequestId,
        submitNodesPending,
        toolNodesInvokeCommand,
        setToolNodesInvokeCommand,
        toolNodesInvokeParamsJson,
        setToolNodesInvokeParamsJson,
        submitNodesInvoke,
        toolTelegramStubText,
        setToolTelegramStubText,
        submitTelegramStubCommand,
        toolWebSearchQuery,
        setToolWebSearchQuery,
        submitWebSearchProbe,
        toolWebFetchUrl,
        setToolWebFetchUrl,
        submitWebFetchProbe,
        toolMemorySearchQuery,
        setToolMemorySearchQuery,
        submitMemorySearchProbe,
        toolMemoryGetPath,
        setToolMemoryGetPath,
        submitMemoryGetProbe,
        clearToolControlResults,
        toolResultPreview,
        filteredToolResultItems,
        selectedToolResultId,
        selectToolResultItem
      });
    }

    function updateUiPreference(partial) {
      setUiPreferences((prev) => mergeUiPreferences({
        ...prev,
        ...partial
      }));
    }

    function updateShortcut(key, value) {
      setUiPreferences((prev) => mergeUiPreferences({
        ...prev,
        shortcuts: {
          ...(prev.shortcuts || {}),
          [key]: value
        }
      }));
    }

    function renderUiPreferencePanel() {
      const shortcuts = uiPreferences.shortcuts || DEFAULT_UI_PREFERENCES.shortcuts;
      const shortcutGroups = [
        {
          key: "palette",
          title: "팔레트 / 입력",
          rows: [
            { key: "palette", label: "팔레트 열기", helper: "검색, 스킬, 모델 전환" },
            { key: "paletteAlt", label: "팔레트 보조", helper: "VSCode 스타일 빠른 실행" },
            { key: "send", label: "메시지 전송", helper: "입력창에서 바로 전송" },
            { key: "close", label: "닫기", helper: "팔레트와 오버레이 닫기" },
            { key: "focusComposer", label: "입력창 포커스", helper: "어디서든 바로 타이핑" }
          ]
        },
        {
          key: "conversation",
          title: "대화 / 워크플로우",
          rows: [
            { key: "newChat", label: "새 대화 시작", helper: "현재 모드에서 새 thread" },
            { key: "searchConversations", label: "대화 검색", helper: "사이드바 검색창 포커스" },
            { key: "toggleTheme", label: "테마 전환", helper: "시스템 → 라이트 → 다크 순환" },
            { key: "toggleVoice", label: "음성 입력 토글", helper: "마이크 버튼 클릭과 동일" },
            { key: "toggleAutoSpeak", label: "자동 읽어주기 토글", helper: "응답을 음성으로 읽기 ON/OFF" },
            { key: "stopSpeaking", label: "읽기 중단", helper: "재생 중인 음성 즉시 정지" }
          ]
        },
        {
          key: "tabs",
          title: "탭 전환",
          rows: [
            { key: "tabChat", label: "대화 탭", helper: "" },
            { key: "tabRoutine", label: "루틴 탭", helper: "" },
            { key: "tabLogic", label: "로직 탭", helper: "" },
            { key: "tabCoding", label: "코딩 탭", helper: "" },
            { key: "tabNotebook", label: "노트북 탭", helper: "" },
            { key: "tabAutomation", label: "작업 계획 탭", helper: "" },
            { key: "tabSkills", label: "스킬 탭", helper: "" },
            { key: "tabSettings", label: "설정 탭", helper: "" }
          ]
        }
      ];

      const allRows = shortcutGroups.flatMap((group) => group.rows);
      const labelByKey = Object.fromEntries(allRows.map((row) => [row.key, row.label]));
      const valuesByKey = Object.fromEntries(allRows.map((row) => [row.key, normalizeShortcut(shortcuts[row.key])]));
      const occurrences = {};
      allRows.forEach((row) => {
        const v = valuesByKey[row.key];
        if (!v) return;
        if (!occurrences[v]) occurrences[v] = [];
        occurrences[v].push(row.key);
      });
      const conflictGroups = Object.entries(occurrences).filter(([, keys]) => keys.length > 1);
      const duplicateValues = new Set(conflictGroups.map(([v]) => v));
      const hasShortcutConflict = conflictGroups.length > 0;
      const lastSavedLabel = uiPreferencesSavedAt
        ? formatRelativeTime(Date.now() - uiPreferencesSavedAt)
        : "아직 저장 안 됨";
      const sessionDirty = uiPreferencesSessionStartRef.current
        ? JSON.stringify(uiPreferencesSessionStartRef.current) !== JSON.stringify(uiPreferences)
        : false;
      const preview = backupState.preview || null;
      const conflicts = Array.isArray(preview?.conflicts) ? preview.conflicts : [];
      const activeShortcutGroup = shortcutGroups.find((group) => group.key === shortcutGroupTab) || shortcutGroups[0];

      function renderShortcutRow(row) {
        const value = shortcuts[row.key] || "";
        const normalized = valuesByKey[row.key];
        const conflict = normalized && duplicateValues.has(normalized);
        const recording = recordingShortcutKey === row.key;
        const formatted = formatShortcutLabel(value);
        const invalid = value && !isValidShortcut(value);
        return e("label", {
          key: row.key,
          className: `shortcut-row ${conflict ? "conflict" : ""} ${recording ? "recording" : ""} ${invalid ? "invalid" : ""}`
        },
        e("span", { className: "shortcut-row-label" }, row.label),
        e("div", { className: "shortcut-row-controls" },
          e("input", {
            className: "input",
            value,
            placeholder: recording ? "키를 누르세요…" : "예: mod+k",
            onChange: (event) => updateShortcut(row.key, event.target.value),
            onKeyDown: (event) => {
              if (recording) {
                event.preventDefault();
                event.stopPropagation();
                const captured = shortcutFromEvent(event);
                if (captured && isValidShortcut(captured)) {
                  updateShortcut(row.key, captured);
                  setRecordingShortcutKey(null);
                }
              }
            },
            spellCheck: false
          }),
          formatted
            ? e("span", { className: "shortcut-chip" }, formatted)
            : null,
          e("button", {
            type: "button",
            className: `btn ghost shortcut-record-btn ${recording ? "active" : ""}`,
            onClick: () => setRecordingShortcutKey(recording ? null : row.key),
            title: recording ? "취소" : "키 입력 받기"
          }, recording ? "취소" : "캡처"),
          e("button", {
            type: "button",
            className: "btn ghost shortcut-clear-btn",
            onClick: () => updateShortcut(row.key, ""),
            title: "비우기"
          }, "✕")
        ),
        row.helper ? e("small", null, row.helper) : null,
        invalid ? e("small", { className: "shortcut-invalid-hint" }, "형식이 올바르지 않습니다 (예: mod+k, shift+enter)") : null
        );
      }

      return e("section", { className: "panel settings-optimized-panel ui-preferences-panel" },
        e("div", { className: "settings-panel-hero" },
          e("div", null,
            e("h2", null, "화면/단축키"),
            e("p", null, "테마, 키보드 동작, 백업을 현재 브라우저 기준으로 관리합니다.")
          ),
          e("div", { className: "settings-visual-card preferences" },
            e("span", null, "LOCAL"),
            e("strong", null, uiPreferences.theme?.mode === "dark" ? "다크" : uiPreferences.theme?.mode === "light" ? "라이트" : "시스템"),
            e("div", { className: "settings-chip-row" },
              e("span", { className: `settings-status-chip ${hasShortcutConflict ? "idle" : "ok"}` }, hasShortcutConflict ? "충돌 확인" : "키맵 정상"),
              e("span", { className: "settings-status-chip ok" }, `저장: ${lastSavedLabel}`)
            )
          )
        ),
        e("div", { className: "preference-section" },
          e("div", { className: "preference-section-head" },
            e("strong", null, "테마"),
            e("span", null, "브라우저에 저장됩니다.")
          ),
          e("div", { className: "theme-choice-row" },
            ["system", "light", "dark"].map((modeValue) => e("button", {
              key: modeValue,
              type: "button",
              className: `theme-choice ${uiPreferences.theme?.mode === modeValue ? "active" : ""}`,
              onClick: () => updateUiPreference({ theme: { ...(uiPreferences.theme || {}), mode: modeValue } })
            },
            modeValue === "system" ? "시스템" : modeValue === "dark" ? "다크" : "라이트"))
          )
        ),
        e("div", { className: "preference-section" },
          e("div", { className: "preference-section-head" },
            e("strong", null, "단축키"),
            e("span", null, isMacOs() ? "Mac 기준 ⌘ 표기. 다른 OS는 Ctrl로 표시됩니다." : "Win/Linux 기준 Ctrl 표기. mod = Ctrl/Cmd 호환.")
          ),
          hasShortcutConflict
            ? e("div", { className: "shortcut-conflict" },
              e("strong", null, "충돌: "),
              e("span", null, conflictGroups.map(([value, keys]) => {
                const labels = keys.map((k) => labelByKey[k] || k).join(" ↔ ");
                return `${formatShortcutLabel(value)} (${labels})`;
              }).join(" / "))
            )
            : null,
          e("div", { className: "shortcut-tab-row" },
            shortcutGroups.map((group) => e("button", {
              key: group.key,
              type: "button",
              className: `shortcut-tab ${activeShortcutGroup.key === group.key ? "active" : ""}`,
              onClick: () => setShortcutGroupTab(group.key)
            }, group.title))
          ),
          e("div", { className: "shortcut-group" },
            e("div", { className: "shortcut-group-title" }, activeShortcutGroup.title),
            e("div", { className: "shortcut-grid" },
              activeShortcutGroup.rows.map((row) => renderShortcutRow(row))
            )
          ),
          e("div", { className: "settings-action-row shortcut-action-row" },
            e("button", {
              type: "button",
              className: "btn ghost",
              onClick: () => {
                if (window.confirm("모든 단축키를 기본값으로 복원하시겠습니까?")) {
                  setUiPreferences(mergeUiPreferences(DEFAULT_UI_PREFERENCES));
                }
              }
            }, "기본값 복원"),
            e("button", {
              type: "button",
              className: "btn ghost",
              disabled: !sessionDirty,
              onClick: () => {
                if (uiPreferencesSessionStartRef.current && window.confirm("이번 세션의 변경 사항을 모두 되돌립니까?")) {
                  setUiPreferences(mergeUiPreferences(uiPreferencesSessionStartRef.current));
                }
              },
              title: sessionDirty ? "이번 세션 변경 모두 취소" : "변경 사항 없음"
            }, "세션 변경 되돌리기"),
            e("span", { className: "shortcut-save-hint" }, sessionDirty ? "변경 후 자동 저장됨" : "현재까지 모두 저장됨")
          )
        ),
        e("div", { className: "preference-section" },
          e("div", { className: "preference-section-head" },
            e("strong", null, "음성 출력 (TTS)"),
            e("span", null, isSpeechSynthesisSupported() ? "브라우저의 시스템 음성을 사용합니다." : "이 브라우저는 음성 출력을 지원하지 않습니다.")
          ),
          isSpeechSynthesisSupported()
            ? e("div", { className: "tts-controls" },
              e("label", { className: "tts-toggle-row" },
                e("input", {
                  type: "checkbox",
                  checked: !!uiPreferences.speech?.autoSpeak,
                  onChange: (event) => updateUiPreference({
                    speech: { ...(uiPreferences.speech || {}), autoSpeak: event.target.checked }
                  })
                }),
                e("span", null, "응답이 도착하면 자동으로 음성으로 읽기")
              ),
              e("div", { className: "tts-grid" },
                e("label", { className: "tts-field" },
                  e("span", null, "음성"),
                  e("select", {
                    className: "input",
                    value: uiPreferences.speech?.voiceURI || "",
                    onChange: (event) => updateUiPreference({
                      speech: { ...(uiPreferences.speech || {}), voiceURI: event.target.value }
                    })
                  },
                    e("option", { value: "" }, "시스템 기본"),
                    availableTtsVoices.map((voice) => e("option", {
                      key: voice.voiceURI,
                      value: voice.voiceURI
                    }, `${voice.name} (${voice.lang})${voice.default ? " · 기본" : ""}`))
                  )
                ),
                e("label", { className: "tts-field" },
                  e("span", null, `속도: ${(uiPreferences.speech?.rate ?? 1).toFixed(2)}x`),
                  e("input", {
                    type: "range",
                    min: "0.5",
                    max: "2",
                    step: "0.05",
                    value: uiPreferences.speech?.rate ?? 1,
                    onChange: (event) => updateUiPreference({
                      speech: { ...(uiPreferences.speech || {}), rate: parseFloat(event.target.value) }
                    })
                  })
                ),
                e("label", { className: "tts-field" },
                  e("span", null, `음높이: ${(uiPreferences.speech?.pitch ?? 1).toFixed(2)}`),
                  e("input", {
                    type: "range",
                    min: "0.5",
                    max: "2",
                    step: "0.05",
                    value: uiPreferences.speech?.pitch ?? 1,
                    onChange: (event) => updateUiPreference({
                      speech: { ...(uiPreferences.speech || {}), pitch: parseFloat(event.target.value) }
                    })
                  })
                ),
                e("label", { className: "tts-field" },
                  e("span", null, `음량: ${Math.round((uiPreferences.speech?.volume ?? 1) * 100)}%`),
                  e("input", {
                    type: "range",
                    min: "0",
                    max: "1",
                    step: "0.05",
                    value: uiPreferences.speech?.volume ?? 1,
                    onChange: (event) => updateUiPreference({
                      speech: { ...(uiPreferences.speech || {}), volume: parseFloat(event.target.value) }
                    })
                  })
                )
              ),
              e("label", { className: "tts-toggle-row" },
                e("input", {
                  type: "checkbox",
                  checked: uiPreferences.speech?.stripMarkdown !== false,
                  onChange: (event) => updateUiPreference({
                    speech: { ...(uiPreferences.speech || {}), stripMarkdown: event.target.checked }
                  })
                }),
                e("span", null, "코드 블록·markdown 기호 제거 후 읽기 (권장)")
              ),
              e("div", { className: "settings-action-row" },
                e("button", {
                  type: "button",
                  className: "btn",
                  onClick: () => speakText("음성 출력 테스트입니다. 들리시면 정상입니다.", uiPreferences.speech)
                }, "샘플 재생"),
                e("button", {
                  type: "button",
                  className: "btn ghost",
                  onClick: () => stopSpeaking()
                }, "정지"),
                e("button", {
                  type: "button",
                  className: "btn ghost",
                  onClick: () => updateUiPreference({
                    speech: {
                      ...(uiPreferences.speech || {}),
                      rate: 1,
                      pitch: 1,
                      volume: 1,
                      voiceURI: "",
                      stripMarkdown: true
                    }
                  })
                }, "음성 설정 초기화")
              )
            )
            : e("div", { className: "tts-unsupported" }, "Chrome / Edge / Safari 등 SpeechSynthesis API를 지원하는 브라우저에서 사용할 수 있습니다.")
        ),
        e("div", { className: "preference-section" },
          e("div", { className: "preference-section-head" },
            e("strong", null, "백업 / 가져오기"),
            e("span", null, "API 키와 세션 정보는 제외합니다.")
          ),
          e("div", { className: "backup-action-grid" },
            e("button", {
              type: "button",
              className: "btn primary",
              onClick: requestBackupExport,
              disabled: backupState.exportBusy
            }, backupState.exportBusy ? "백업 만드는 중..." : "전체 백업 내보내기"),
            e("label", { className: `btn ghost backup-import-label ${backupState.importBusy ? "disabled" : ""}` },
              "백업 파일 선택",
              e("input", {
                type: "file",
                accept: ".zip,application/zip",
                onChange: handleBackupFileSelected,
                disabled: backupState.importBusy
              })
            )
          ),
          backupState.status
            ? e("div", { className: "backup-status" }, backupState.status)
            : null,
          preview
            ? e("div", { className: "backup-preview-card" },
              e("div", { className: "backup-preview-head" },
                e("strong", null, preview.fileName || "백업 파일"),
                e("span", null, `대화 ${preview.conversationCount || 0}개 · 파일 ${preview.fileCount || 0}개`)
              ),
              conflicts.length > 0
                ? e("div", { className: "backup-conflict-list" },
                  e("span", null, `겹치는 대화 ${preview.conversationConflictCount || conflicts.length}개`),
                  e("code", null, conflicts.slice(0, 3).join(", ")),
                  conflicts.length > 3 ? e("small", null, `외 ${conflicts.length - 3}개`) : null
                )
                : e("div", { className: "backup-conflict-list clean" }, "겹치는 대화가 없습니다."),
              e("div", { className: "backup-preview-actions" },
                e("button", {
                  type: "button",
                  className: "btn primary",
                  onClick: () => applyBackupImport(false),
                  disabled: backupState.importBusy
                }, "새 항목만 가져오기"),
                e("button", {
                  type: "button",
                  className: "btn danger",
                  onClick: () => {
                    if (window.confirm("겹치는 대화와 파일을 백업 내용으로 덮어쓸까요?")) {
                      applyBackupImport(true);
                    }
                  },
                  disabled: backupState.importBusy || !conflicts.length
                }, "겹치는 항목 덮어쓰기")
              )
            )
            : null
        )
      );
    }

    function buildCommandPaletteActions() {
      const query = commandPaletteQuery.trim();
      const actions = [];
      TAB_SHORTCUTS.forEach((item, index) => {
        actions.push({
          id: `tab-${item[0]}`,
          group: "탭 이동",
          label: item[1],
          helper: `Ctrl/Cmd+${index + 1}`,
          run: () => setRootTab(item[0])
        });
      });

      if (query.length >= 2) {
        actions.push({
          id: "search-conversations",
          group: "검색",
          label: `"${query}" 대화 본문 검색`,
          helper: "제목, 미리보기, 저장된 대화 본문에서 찾습니다.",
          run: () => {
            setRootTab("chat");
            setChatMode("single");
            setConversationFilterByKey((prev) => ({ ...prev, "chat:single": query }));
            requestConversationSearch(query, "chat:single");
          }
        });
      }

      normalizePaletteSkills(contextState.skills).forEach((skill) => {
        actions.push({
          id: `skill-${skill.key}`,
          group: "스킬 적용",
          label: skill.name,
          helper: `${skill.scope === "global" ? "전역" : "프로젝트"} · ${skill.description || skill.path || "선택한 대화에 적용"}`,
          run: () => {
            setSelectedChatSkill({ name: skill.name, scope: skill.scope, key: skill.key });
          }
        });
      });

      [
        { provider: "groq", label: "Groq", model: selectedGroqModel || DEFAULT_GROQ_SINGLE_MODEL },
        { provider: "gemini", label: "Gemini", model: DEFAULT_GEMINI_WORKER_MODEL },
        { provider: "cerebras", label: "Cerebras", model: DEFAULT_CEREBRAS_MODEL },
        { provider: "nvidia", label: "NVIDIA NIM", model: DEFAULT_NVIDIA_MODEL },
        { provider: "codex", label: "Codex", model: DEFAULT_CODEX_MODEL },
        { provider: "copilot", label: "Copilot", model: selectedCopilotModel || "" }
      ].forEach((item) => {
        actions.push({
          id: `model-${item.provider}`,
          group: "모델 전환",
          label: `${item.label}로 전환`,
          helper: item.model || "저장된 기본 모델 사용",
          run: () => {
            setRootTab("chat");
            setChatMode("single");
            setChatSingleProvider(item.provider);
            if (item.model) {
              setChatSingleModel(item.model);
            }
          }
        });
      });

      return actions;
    }

    function runCommandPaletteAction(action) {
      if (!action || typeof action.run !== "function") {
        return;
      }
      action.run();
      setCommandPaletteOpen(false);
      setCommandPaletteQuery("");
    }

    function renderCommandPalette() {
      if (!commandPaletteOpen) {
        return null;
      }

      const query = commandPaletteQuery.trim().toLowerCase();
      const allActions = buildCommandPaletteActions();
      const visibleActions = allActions
        .filter((action) => {
          if (!query) {
            return true;
          }
          return [action.group, action.label, action.helper]
            .some((value) => `${value || ""}`.toLowerCase().includes(query));
        })
        .slice(0, 18);
      const firstAction = visibleActions[0] || null;

      return e("div", {
        className: "command-palette-backdrop",
        onMouseDown: (event) => {
          if (event.target === event.currentTarget) {
            setCommandPaletteOpen(false);
          }
        }
      },
      e("section", {
        className: "command-palette",
        role: "dialog",
        "aria-label": "Command Palette",
        onMouseDown: (event) => event.stopPropagation()
      },
      e("div", { className: "command-palette-input-wrap" },
        e("input", {
          className: "command-palette-input",
          value: commandPaletteQuery,
          autoFocus: true,
          placeholder: "탭 이동, 대화 검색, 스킬 적용, 모델 전환",
          onChange: (event) => setCommandPaletteQuery(event.target.value),
          onKeyDown: (event) => {
            if (event.key === "Enter" && firstAction) {
              event.preventDefault();
              runCommandPaletteAction(firstAction);
            }
            if (event.key === "Escape") {
              event.preventDefault();
              setCommandPaletteOpen(false);
            }
          }
        })
      ),
      e("div", { className: "command-palette-results" },
        visibleActions.length === 0
          ? e("div", { className: "command-palette-empty" }, "실행할 항목이 없습니다.")
          : visibleActions.map((action) => e("button", {
            key: action.id,
            type: "button",
            className: "command-palette-item",
            onClick: () => runCommandPaletteAction(action)
          },
          e("span", { className: "command-palette-group" }, action.group),
          e("strong", null, action.label),
          e("small", null, action.helper)))
      )));
    }

    function renderSettings() {
      return renderSettingsPanelModule({
        e,
        authMeta,
        otp,
        setOtp,
        authTtlHours,
        setAuthTtlHours,
        log,
        send,
        authExpiry,
        authLocalOffset,
        telegramBotToken,
        setTelegramBotToken,
        telegramChatId,
        setTelegramChatId,
        persist,
        setPersist,
        settingsState,
        groqApiKey,
        setGroqApiKey,
        geminiApiKey,
        setGeminiApiKey,
        cerebrasApiKey,
        setCerebrasApiKey,
        nvidiaApiKey,
        setNvidiaApiKey,
        codexApiKey,
        setCodexApiKey,
        copilotStatus,
        copilotDetail,
        codexStatus,
        setCodexStatus,
        codexDetail,
        setCodexDetail,
        geminiUsage,
        copilotPremiumUsage,
        copilotPremiumPercent,
        copilotPremiumQuotaText,
        formatDecimal,
        copilotLocalUsage,
        copilotPremiumRows,
        copilotLocalRows,
        selectedCopilotModel,
        setSelectedCopilotModel,
        copilotModels,
        copilotRows,
        selectedGroqModel,
        setSelectedGroqModel,
        groqModels,
        groqRows,
        command,
        setCommand,
        authed,
        metrics,
        logs,
        doctorState,
        runDoctorReport,
        refreshDoctorReport,
        contextState,
        routingPolicyState,
        plansState,
        taskGraphState,
        notebooksState,
        refreshProjectContext,
        refreshSkillsList,
        refreshCommandsList,
        setRoutingPolicyChain,
        refreshRoutingPolicy,
        saveRoutingPolicy,
        resetRoutingPolicy,
        refreshRoutingDecision,
        setPlanCreateObjective,
        setPlanCreateConstraintsText,
        setPlanCreateMode,
        refreshPlansList,
        loadPlanSnapshot,
        submitPlanCreate,
        reviewPlan,
        approvePlan,
        runPlan,
        setTaskGraphCreatePlanId,
        useSelectedPlanForTaskGraph,
        refreshTaskGraphList,
        loadTaskGraph,
        submitTaskGraphCreate,
        runTaskGraph,
        loadTaskOutput,
        cancelTask,
        setNotebookProjectKey,
        setNotebookAppendKind,
        setNotebookAppendText,
        refreshNotebook,
        appendNotebook,
        createNotebookHandoff,
        appendSelectedPlanDecision,
        appendSelectedTaskVerification,
        appendDoctorVerification,
        appendRefactorVerification,
        opsDomainFilter,
        opsDomainFilters: OPS_DOMAIN_FILTERS,
        opsDomainStats,
        applyDomainFocus,
        filteredOpsFlowItems,
        workerRef,
        toolPanel: renderToolControlPanel(),
        uiPreferencePanel: renderUiPreferencePanel(),
        currentSettingsPane,
        renderResponsiveSectionTabs,
        setResponsivePane,
        isPortraitMobileLayout
      });
    }

    function renderNotebookRoot() {
      return renderNotebookRootPanelModule({
        e,
        authed,
        notebooksState,
        setNotebookProjectKey,
        setNotebookAppendKind,
        setNotebookAppendText,
        refreshNotebook,
        appendNotebook,
        createNotebookHandoff,
        appendSelectedPlanDecision,
        appendSelectedTaskVerification,
        appendDoctorVerification,
        appendRefactorVerification
      });
    }

    function renderSkillsRoot() {
      const onLoadSkillDetail = (name, scope) => {
        const normalizedScope = `${scope || "project"}`.trim() || "project";
        const normalizedName = `${name || ""}`.trim();
        const requestKey = `${normalizedScope}:${normalizedName}`;
        setLoadingSkillKey(requestKey);
        setSkillEditor(null);
        setSkillStatus({ kind: "info", message: "불러오는 중..." });
        const ok = requestSkillGet(send, normalizedName, normalizedScope);
        if (!ok) {
          setLoadingSkillKey("");
          setSkillStatus({ kind: "error", message: "스킬 요청 전송에 실패했습니다." });
          return;
        }
        window.setTimeout(() => {
          setLoadingSkillKey((current) => {
            if (current !== requestKey) {
              return current;
            }
            setSkillStatus({ kind: "error", message: "스킬 응답이 지연되고 있습니다. 목록을 다시 읽거나 다시 선택하세요." });
            return "";
          });
        }, 8000);
      };
      const onSaveSkill = (editor) => {
        const name = `${editor?.name || ""}`.trim();
        const scope = `${editor?.scope || "project"}`.trim() || "project";
        if (!editor || !name) {
          setSkillStatus({ kind: "error", message: "이름이 비어 있습니다." });
          return;
        }
        setSkillStatus({ kind: "info", message: "저장 중..." });
        const ok = requestSkillSave(send, {
          name,
          scope,
          description: `${editor.description || ""}`.trim(),
          body: editor.body || ""
        });
        if (!ok) {
          setSkillStatus({ kind: "error", message: "저장 요청 전송에 실패했습니다." });
        } else if (editor.isNew) {
          setSelectedSkillKey(`${scope}:${name}`);
        }
      };
      const onDeleteSkill = (name, scope) => {
        setSkillStatus({ kind: "info", message: "삭제 중..." });
        const ok = requestSkillDelete(send, name, scope);
        if (!ok) {
          setLoadingSkillKey("");
          setSkillStatus({ kind: "error", message: "삭제 요청 전송에 실패했습니다." });
        }
      };
      const onStartNewSkill = () => {
        setSelectedSkillKey("");
        setLoadingSkillKey("");
        setSkillStatus(null);
        setSkillEditor({ name: "", scope: "project", description: "", body: "", isNew: true });
      };
      return renderSkillsRootModule({
        e,
        authed,
        skills: contextState.skills,
        selectedSkillKey,
        setSelectedSkillKey,
        skillEditor,
        setSkillEditor,
        skillStatus,
        loadingSkillKey,
        skillsBusy: contextState.loadingSkills,
        skillSearch,
        setSkillSearch,
        onRefreshSkills: () => refreshSkillsList(),
        onLoadSkillDetail,
        onSaveSkill,
        onDeleteSkill,
        onStartNewSkill
      });
    }

    function renderAutomationRoot() {
      return renderAutomationRootPanelModule({
        e,
        authed,
        plansState,
        taskGraphState,
        routingPolicyState,
        setPlanCreateObjective,
        setPlanCreateConstraintsText,
        setPlanCreateMode,
        setRoutingPolicyChain,
        refreshRoutingPolicy,
        saveRoutingPolicy,
        refreshPlansList,
        loadPlanSnapshot,
        submitPlanCreate,
        reviewPlan,
        approvePlan,
        runPlan,
        setTaskGraphCreatePlanId,
        useSelectedPlanForTaskGraph,
        refreshTaskGraphList,
        loadTaskGraph,
        submitTaskGraphCreate,
        runTaskGraph,
        loadTaskOutput,
        cancelTask,
        selectedGroqModel,
        setSelectedGroqModel,
        groqModels,
        selectedCopilotModel,
        setSelectedCopilotModel,
        copilotModels,
        defaultCodexModel: DEFAULT_CODEX_MODEL,
        geminiModelChoices: GEMINI_MODEL_CHOICES,
        cerebrasModelChoices: CEREBRAS_MODEL_CHOICES,
        nvidiaModelChoices: NVIDIA_MODEL_CHOICES,
        send,
        currentAutomationPane,
        setAutomationPane: (paneKey) => setResponsivePane("automation", paneKey)
      });
    }

    const mainContent = rootTab === "settings"
      ? renderSettings()
      : rootTab === "notebook"
        ? renderNotebookRoot()
        : rootTab === "automation"
          ? renderAutomationRoot()
          : rootTab === "routine"
            ? renderRoutine()
            : rootTab === "logic"
              ? renderLogic()
              : rootTab === "skills"
                ? renderSkillsRoot()
                : renderWorkspace();

    return e(
      "div",
      { className: "app-shell" },
      renderGlobalNav(),
      e(
        "main",
        { className: "main-shell", ref: mainShellRef },
        mainContent
      ),
      renderCodingResultOverlay(),
      renderSafeRefactorOverlay(),
      renderCommandPalette(),
      memoryPreview.open
        ? e("div", { className: "modal" },
          e("div", { className: "modal-card" },
            e("div", { className: "modal-head" },
              e("strong", null, memoryPreview.name || "메모리 노트"),
              e("button", {
                className: "btn ghost",
                onClick: () => setMemoryPreview({ open: false, name: "", content: "" })
              }, "닫기")
            ),
            e("pre", { className: "modal-content" }, memoryPreview.content || "")
          )
        )
        : null,
      routineOutputPreview.open
        ? e("div", { className: "modal" },
          e("div", { className: "modal-card" },
            e("div", { className: "modal-head" },
              e("strong", null, routineOutputPreview.title || "실행 출력"),
              e("button", {
                className: "btn ghost",
                onClick: () => setRoutineOutputPreview({ open: false, title: "", content: "", imagePath: "", imageAlt: "" })
              }, "닫기")
            ),
            routineOutputPreview.imagePath
              ? e("div", { className: "routine-output-preview-image-wrap" },
                e("div", { className: "tiny" }, routineOutputPreview.imagePath),
                e("img", {
                  className: "routine-output-preview-image",
                  src: buildRoutineImagePreviewUrl(routineOutputPreview.imagePath),
                  alt: routineOutputPreview.imageAlt || "루틴 스크린샷"
                })
              )
              : null,
            e("pre", { className: "modal-content" }, routineOutputPreview.content || "출력 없음")
          )
        )
        : null,
      logicRunPreview.open
        ? e("div", { className: "modal logic-run-preview-modal" },
          e("div", { className: "modal-card logic-run-preview-card" },
            e("div", { className: "modal-head" },
              e("strong", null, logicRunPreview.title || "실행 상세"),
              e("button", {
                className: "btn ghost logic-run-preview-close",
                type: "button",
                onClick: () => setLogicRunPreview({ open: false, title: "", content: "" })
              }, "X")
            ),
            e("pre", { className: "modal-content logic-run-preview-content" }, logicRunPreview.content || "표시할 내용이 없습니다.")
          )
        )
        : null
    );
  }

  ReactDOM.createRoot(document.getElementById("root")).render(e(App));
})();
