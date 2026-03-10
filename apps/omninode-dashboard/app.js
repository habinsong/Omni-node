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
  buildDashboardWsUrl,
  clearPersistedAuthSession,
  flushQueuedPayloads,
  getSavedAuthExpiry,
  getSavedAuthToken,
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
  requestSkillsList
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
import { renderToolControlPanel as renderToolControlPanelModule } from "./modules/dashboard-ops-renderers.js";
import { renderSettingsPanel as renderSettingsPanelModule } from "./modules/dashboard-settings-renderers.js";
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
  const GEMINI_MODEL_CHOICES = [
    { id: "gemini-3-flash-preview", label: "Gemini 기본: gemini-3-flash-preview" },
    { id: "gemini-3.1-flash-lite-preview", label: "Gemini: gemini-3.1-flash-lite-preview" }
  ];
  const DEFAULT_CEREBRAS_MODEL = "gpt-oss-120b";
  const CEREBRAS_MODEL_CHOICES = [
    { id: DEFAULT_CEREBRAS_MODEL, label: `Cerebras 기본: ${DEFAULT_CEREBRAS_MODEL}` },
    { id: "zai-glm-4.7", label: "Cerebras: zai-glm-4.7 (preview)" },
    { id: "qwen-3-235b-a22b-instruct-2507", label: "Cerebras: qwen-3-235b-a22b-instruct-2507" },
    { id: "llama3.1-8b", label: "Cerebras: llama3.1-8b" }
  ];
  const CONVERSATION_STATE_DEFAULTS = createConversationState();
  const CHAT_STATE_DEFAULTS = createChatState({
    noneModel: NONE_MODEL,
    defaultGroqSingleModel: DEFAULT_GROQ_SINGLE_MODEL,
    defaultGroqWorkerModel: DEFAULT_GROQ_WORKER_MODEL,
    defaultGeminiWorkerModel: DEFAULT_GEMINI_WORKER_MODEL,
    defaultCerebrasModel: DEFAULT_CEREBRAS_MODEL
  });
  const CODING_STATE_DEFAULTS = createCodingState({
    noneModel: NONE_MODEL,
    defaultGroqWorkerModel: DEFAULT_GROQ_WORKER_MODEL,
    defaultGeminiWorkerModel: DEFAULT_GEMINI_WORKER_MODEL,
    defaultCerebrasModel: DEFAULT_CEREBRAS_MODEL
  });
  const ROUTINE_STATE_DEFAULTS = createRoutineState({
    defaultMobilePanes: DEFAULT_MOBILE_PANES
  });
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

  function App() {
    const [rootTab, setRootTab] = useState("chat");
    const [chatMode, setChatMode] = useState("single");
    const [codingMode, setCodingMode] = useState("single");

    const [status, setStatus] = useState("연결 대기");
    const [authed, setAuthed] = useState(false);
    const [authExpiry, setAuthExpiry] = useState("");
    const [authLocalOffset, setAuthLocalOffset] = useState(localUtcOffsetLabel());
    const [authTtlHours, setAuthTtlHours] = useState("24");
    const [otp, setOtp] = useState("");
    const [authMeta, setAuthMeta] = useState(() => createAuthMetaState());

    const [settingsState, setSettingsState] = useState(() => createSettingsState());

    const [telegramBotToken, setTelegramBotToken] = useState("");
    const [telegramChatId, setTelegramChatId] = useState("");
    const [groqApiKey, setGroqApiKey] = useState("");
    const [geminiApiKey, setGeminiApiKey] = useState("");
    const [cerebrasApiKey, setCerebrasApiKey] = useState("");
    const [codexApiKey, setCodexApiKey] = useState("");
    const [persist, setPersist] = useState(true);

    const [copilotStatus, setCopilotStatus] = useState("확인 전");
    const [copilotDetail, setCopilotDetail] = useState("-");
    const [codexStatus, setCodexStatus] = useState("확인 전");
    const [codexDetail, setCodexDetail] = useState("-");
    const [groqModels, setGroqModels] = useState([]);
    const [copilotModels, setCopilotModels] = useState([]);
    const [selectedGroqModel, setSelectedGroqModel] = useState("");
    const [selectedCopilotModel, setSelectedCopilotModel] = useState("");

    const [geminiUsage, setGeminiUsage] = useState(() => createGeminiUsageState());
    const [copilotPremiumUsage, setCopilotPremiumUsage] = useState(() => createCopilotPremiumUsageState());
    const [copilotLocalUsage, setCopilotLocalUsage] = useState(() => createCopilotLocalUsageState());
    const [doctorState, setDoctorState] = useState(() => createDoctorState());
    const [plansState, setPlansState] = useState(() => createPlansState());
    const [contextState, setContextState] = useState(() => createContextState());
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
    const [chatOrchCopilotModel, setChatOrchCopilotModel] = useState(CHAT_STATE_DEFAULTS.orchCopilotModel);
    const [chatOrchCodexModel, setChatOrchCodexModel] = useState(CHAT_STATE_DEFAULTS.orchCodexModel);
    const [chatMultiGroqModel, setChatMultiGroqModel] = useState(CHAT_STATE_DEFAULTS.multiGroqModel);
    const [chatMultiGeminiModel, setChatMultiGeminiModel] = useState(CHAT_STATE_DEFAULTS.multiGeminiModel);
    const [chatMultiCerebrasModel, setChatMultiCerebrasModel] = useState(CHAT_STATE_DEFAULTS.multiCerebrasModel);
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
    const [codingOrchCopilotModel, setCodingOrchCopilotModel] = useState(CODING_STATE_DEFAULTS.orchCopilotModel);
    const [codingOrchCodexModel, setCodingOrchCodexModel] = useState(CODING_STATE_DEFAULTS.orchCodexModel);

    const [codingMultiProvider, setCodingMultiProvider] = useState(CODING_STATE_DEFAULTS.multiProvider);
    const [codingMultiModel, setCodingMultiModel] = useState(CODING_STATE_DEFAULTS.multiModel);
    const [codingMultiLanguage, setCodingMultiLanguage] = useState(CODING_STATE_DEFAULTS.multiLanguage);
    const [codingMultiGroqModel, setCodingMultiGroqModel] = useState(CODING_STATE_DEFAULTS.multiGroqModel);
    const [codingMultiGeminiModel, setCodingMultiGeminiModel] = useState(CODING_STATE_DEFAULTS.multiGeminiModel);
    const [codingMultiCerebrasModel, setCodingMultiCerebrasModel] = useState(CODING_STATE_DEFAULTS.multiCerebrasModel);
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
    const [groqUsageWindowBaseByModel, setGroqUsageWindowBaseByModel] = useState(() => ({ ...ROUTINE_STATE_DEFAULTS.groqUsageWindowBaseByModel }));
    const [viewportSize, setViewportSize] = useState(() => getViewportSnapshot());
    const [mainShellViewportTop, setMainShellViewportTop] = useState(0);
    const [mobilePaneByTab, setMobilePaneByTab] = useState(() => ({ ...ROUTINE_STATE_DEFAULTS.mobilePaneByTab }));

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
    const currentRoutinePane = mobilePaneByTab.routine || (routineSelectedId ? "detail" : "overview");
    const currentSettingsPane = mobilePaneByTab.settings || "auth";
    const groupedConversationList = useMemo(() => {
      const keyword = currentConversationFilter.trim().toLowerCase();
      const groups = {};
      currentConversationList.forEach((item) => {
        const detail = conversationDetails[item.id] || null;
        const merged = {
          ...item,
          project: detail?.project ?? item.project,
          category: detail?.category ?? item.category,
          tags: Array.isArray(detail?.tags) ? detail.tags : item.tags
        };
        const project = (merged.project || "기본").trim() || "기본";
        const category = (merged.category || "일반").trim() || "일반";
        const tags = Array.isArray(merged.tags) ? merged.tags.filter(Boolean).map((x) => `${x}`.trim()).filter(Boolean) : [];
        const title = `${merged.title || ""}`.toLowerCase();
        const preview = `${merged.preview || ""}`.toLowerCase();
        const searchable = [project.toLowerCase(), category.toLowerCase(), ...tags.map((x) => x.toLowerCase()), title, preview];
        if (keyword && !searchable.some((text) => text.includes(keyword))) {
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
    }, [conversationDetails, currentConversationFilter, currentConversationList]);
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
          setGuardRetryTimelineApiError(error instanceof Error ? error.message : "fetch_failed");
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
        `plan_id: ${plan.planId || "-"}`,
        `title: ${plan.title || "-"}`,
        `status: ${plan.status || "-"}`,
        `objective: ${plan.objective || "-"}`
      ];

      if (Array.isArray(plan.constraints) && plan.constraints.length > 0) {
        lines.push("", "constraints:");
        plan.constraints.forEach((item) => {
          lines.push(`- ${item}`);
        });
      }

      if (snapshot.review) {
        lines.push(
          "",
          `review_route: ${snapshot.review.reviewerRoute || "-"}`,
          `review_summary: ${snapshot.review.summary || "-"}`
        );
      }

      if (snapshot.execution) {
        lines.push(
          "",
          `latest_execution: ${snapshot.execution.status || "-"}`,
          `latest_message: ${snapshot.execution.message || "-"}`
        );
        if (snapshot.execution.resultSummary) {
          lines.push(trimNotebookEntry(snapshot.execution.resultSummary, 420));
        }
      }

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
        `graph_id: ${graph.graphId || "-"}`,
        `source_plan_id: ${graph.sourcePlanId || "-"}`,
        `status: ${graph.status || "-"}`,
        `completed: ${nodes.filter((task) => task.status === "Completed").length}/${nodes.length}`,
        `failed: ${nodes.filter((task) => task.status === "Failed").length}`
      ];

      if (selectedTask) {
        lines.push(
          "",
          `selected_task: ${selectedTask.taskId} · ${selectedTask.title || "-"}`,
          `task_status: ${selectedTask.status || "-"}`,
          `task_category: ${selectedTask.category || "-"}`
        );
        if (selectedTask.outputSummary) {
          lines.push(`task_summary: ${trimNotebookEntry(selectedTask.outputSummary, 280)}`);
        }
        if (selectedTask.error) {
          lines.push(`task_error: ${trimNotebookEntry(selectedTask.error, 220)}`);
        }
      }

      if (output?.resultJson) {
        lines.push("", "[result.json]", trimNotebookEntry(output.resultJson, 420));
      } else if (output?.stdout) {
        lines.push("", "[stdout]", trimNotebookEntry(output.stdout, 420));
      }

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
        `report_id: ${report.reportId || "-"}`,
        `created_at_utc: ${report.createdAtUtc || "-"}`,
        `ok_count: ${report.okCount || 0}`,
        `warn_count: ${report.warnCount || 0}`,
        `fail_count: ${report.failCount || 0}`,
        `skip_count: ${report.skipCount || 0}`
      ];

      if (failed.length > 0 || warned.length > 0) {
        lines.push("", "issues:");
        failed.concat(warned).slice(0, 8).forEach((item) => {
          lines.push(`- ${item.id || "-"}: ${item.summary || "-"}`);
        });
      } else {
        lines.push("", "issues:", "- 현재 warn/fail check 없음");
      }

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
        `path: ${filePath || "-"}`,
        `last_action: ${refactorState.lastAction || "-"}`,
        `last_message: ${refactorState.lastMessage || "-"}`
      ];

      if (preview) {
        lines.push(
          `preview_id: ${preview.previewId || "-"}`,
          `safe_to_apply: ${preview.safeToApply ? "yes" : "no"}`
        );
        if (preview.unifiedDiff) {
          lines.push("", "[preview]", trimNotebookEntry(preview.unifiedDiff, 420));
        }
      }

      if (issues.length > 0) {
        lines.push("", "issues:");
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
          lastError: "오류: handoff 생성 요청을 전송하지 못했습니다."
        }));
      }

      return ok;
    }

    function appendSelectedPlanDecision() {
      const content = buildSelectedPlanDecisionText();
      if (!content) {
        setNotebooksState((prev) => ({
          ...prev,
          lastError: "선택된 계획이 없어 decision 템플릿을 만들 수 없습니다."
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
          lastError: "선택된 Task graph가 없어 verification 템플릿을 만들 수 없습니다."
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
          lastError: "최근 doctor 보고서가 없어 verification 템플릿을 만들 수 없습니다."
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
          lastError: "최근 refactor 상태가 없어 verification 템플릿을 만들 수 없습니다."
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
          lastError: "오류: 계획 목록 요청을 전송하지 못했습니다."
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
          lastError: "오류: 계획 상세 요청을 전송하지 못했습니다."
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
          lastError: "계획 요청을 입력하세요."
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
          lastError: "오류: 계획 생성 요청을 전송하지 못했습니다."
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
          lastError: "오류: 계획 리뷰 요청을 전송하지 못했습니다."
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
          lastError: "오류: 계획 승인 요청을 전송하지 못했습니다."
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
          lastError: "오류: 계획 실행 요청을 전송하지 못했습니다."
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
          lastError: "오류: 라우팅 정책 요청을 전송하지 못했습니다."
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
          lastError: "오류: 라우팅 정책 저장 요청을 전송하지 못했습니다."
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
          lastError: "오류: 라우팅 정책 초기화 요청을 전송하지 못했습니다."
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
        log("오류: 마지막 라우팅 결정 요청을 전송하지 못했습니다.", "error");
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
          lastError: "오류: Task graph 목록 요청을 전송하지 못했습니다."
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
          lastError: "오류: Task graph 상세 요청을 전송하지 못했습니다."
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
          lastError: "Task graph를 만들 plan id를 입력하세요."
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
          lastError: "오류: Task graph 생성 요청을 전송하지 못했습니다."
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
          lastError: "오류: Task graph 실행 요청을 전송하지 못했습니다."
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
          lastError: "오류: task cancel 요청을 전송하지 못했습니다."
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
      if (codingSingleProvider === "copilot" && !codingSingleModel && selectedCopilotModel) {
        setCodingSingleModel(selectedCopilotModel);
      }
      if (codingOrchProvider === "copilot" && !codingOrchModel && selectedCopilotModel) {
        setCodingOrchModel(selectedCopilotModel);
      }
      if (!codingOrchCopilotModel && selectedCopilotModel) {
        setCodingOrchCopilotModel(selectedCopilotModel);
      }
      if (!codingOrchCodexModel) {
        setCodingOrchCodexModel(DEFAULT_CODEX_MODEL);
      }
      if (codingMultiProvider === "copilot" && !codingMultiModel && selectedCopilotModel) {
        setCodingMultiModel(selectedCopilotModel);
      }
      if (!codingMultiCodexModel) {
        setCodingMultiCodexModel(DEFAULT_CODEX_MODEL);
      }
    }, [
      selectedCopilotModel,
      chatMultiCopilotModel,
      chatMultiCodexModel,
      chatOrchCopilotModel,
      chatOrchCodexModel,
      codingSingleProvider,
      codingSingleModel,
      codingOrchProvider,
      codingOrchModel,
      codingOrchCopilotModel,
      codingOrchCodexModel,
      codingMultiProvider,
      codingMultiModel,
      codingMultiCodexModel
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
      send({ type: "get_copilot_status" });
      send({ type: "get_codex_status" });
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

    function beginPendingRequest(key, userText, isCoding, conversationId) {
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
          chunkIndex: 0
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
          : undefined;
      const conversationId = activeConversationByKey["chat:single"] || "";
      beginPendingRequest("chat:single", effectiveText, false, conversationId);
      setChatInputSingle("");

      const ok = send({
        type: "llm_chat_single",
        scope: "chat",
        mode: "single",
        conversationId: conversationId || undefined,
        text: effectiveText,
        project: metaProject.trim() || "기본",
        category: metaCategory.trim() || "일반",
        tags: parseTags(metaTags),
        provider: chatSingleProvider,
        model,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled
      });

      if (!ok) {
        finishPendingRequest("chat:single");
        setError("chat:single", "오류: WebSocket 연결이 끊어졌습니다.");
      } else {
        clearRichInputDraft("chat:single");
      }
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
            : chatOrchProvider === "cerebras"
              ? (chatOrchModel || chatOrchCerebrasModel || DEFAULT_CEREBRAS_MODEL)
            : undefined;
      const workerGroqModel = normalizeModelChoice(chatOrchGroqModel, DEFAULT_GROQ_WORKER_MODEL);
      const workerGeminiModel = normalizeModelChoice(chatOrchGeminiModel, DEFAULT_GEMINI_WORKER_MODEL);
      const workerCerebrasModel = normalizeModelChoice(chatOrchCerebrasModel, DEFAULT_CEREBRAS_MODEL);
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
        copilotModel: workerCopilotModel,
        codexModel: workerCodexModel,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled
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
        copilotModel: normalizeModelChoice(chatMultiCopilotModel, NONE_MODEL),
        codexModel: normalizeModelChoice(chatMultiCodexModel, DEFAULT_CODEX_MODEL),
        summaryProvider: chatMultiSummaryProvider,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled
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
            : (codingSingleModel || undefined),
        language: codingSingleLanguage,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled
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
        copilotModel: normalizeModelChoice(codingOrchCopilotModel, NONE_MODEL),
        codexModel: normalizeModelChoice(codingOrchCodexModel, DEFAULT_CODEX_MODEL),
        language: codingOrchLanguage,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled
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
          : (isNoneModel(codingMultiModel) ? undefined : (codingMultiModel || undefined)),
        groqModel: normalizeModelChoice(codingMultiGroqModel, DEFAULT_GROQ_WORKER_MODEL),
        geminiModel: normalizeModelChoice(codingMultiGeminiModel, DEFAULT_GEMINI_WORKER_MODEL),
        cerebrasModel: normalizeModelChoice(codingMultiCerebrasModel, DEFAULT_CEREBRAS_MODEL),
        copilotModel: normalizeModelChoice(codingMultiCopilotModel, NONE_MODEL),
        codexModel: normalizeModelChoice(codingMultiCodexModel, DEFAULT_CODEX_MODEL),
        language: codingMultiLanguage,
        memoryNotes: currentMemoryNotes,
        attachments: rich.attachments,
        webUrls: rich.webUrls,
        webSearchEnabled: rich.webSearchEnabled
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
          refactorState,
          notebooksState,
          filePreviewByConversation,
          codingResultByConversation
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
          setRoutineOutputPreview
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
      if (rootTab === "settings" && authed) {
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
      }
    }, [rootTab, authed]);

    useEffect(() => {
      const selected = routines.find((item) => item.id === routineSelectedId) || null;
      setRoutineEditForm(hydrateRoutineFormFromRoutine(selected));
    }, [routines, routineSelectedId]);

    function refreshRoutines() {
      send({ type: "get_routines" });
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
          agentUsePlaywright: prev.agentUsePlaywright !== false,
          scheduleSourceMode: normalizeRoutineScheduleSourceMode(prev.scheduleSourceMode, "auto"),
          maxRetries: Math.min(5, Math.max(0, Number(prev.maxRetries ?? 1) || 0)),
          retryDelaySeconds: Math.min(300, Math.max(0, Number(prev.retryDelaySeconds ?? 15) || 0)),
          notifyPolicy: normalizeRoutineNotifyPolicy(prev.notifyPolicy, "always"),
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

      if (event.key === "Enter" && !event.shiftKey) {
        event.preventDefault();
        handler();
      }
    }

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
      routineAgentProviderOptions,
      routineAgentModelOptions,
      groqWorkerModelOptions,
      geminiWorkerModelOptions,
      copilotWorkerModelOptions,
      codexWorkerModelOptions
    } = useMemo(
      () => buildDashboardModelOptionSets({
        e,
        groqModels,
        copilotModels,
        codeXModelChoices: CODEX_MODEL_CHOICES,
        geminiModelChoices: GEMINI_MODEL_CHOICES,
        noneModel: NONE_MODEL,
        defaultGroqWorkerModel: DEFAULT_GROQ_WORKER_MODEL,
        defaultRoutineAgentProvider: DEFAULT_ROUTINE_AGENT_PROVIDER,
        defaultRoutineAgentModel: DEFAULT_ROUTINE_AGENT_MODEL
      }),
      [groqModels, copilotModels]
    );

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
        deleteConversation
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
        options
      });
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
        parseCodingMultiComparisonMessage: chatMultiUtils.parseCodingMultiComparisonMessage
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
      return null;
    }

    function renderComposerInputBar({ value, onChange, onSend, pendingKey, placeholder }) {
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
        onClearAttachments: () => setAttachmentsByKey((prev) => clearAttachmentDraft(prev, currentKey))
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
        renderCodingResult: () => null,
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
          CEREBRAS_MODEL_CHOICES
        },
        optionSets: {
          groqModelOptions,
          copilotModelOptions,
          codexModelOptions,
          geminiModelOptions,
          groqWorkerModelOptions,
          geminiWorkerModelOptions,
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
          chatOrchCopilotModel,
          chatOrchCodexModel,
          chatMultiGroqModel,
          chatMultiGeminiModel,
          chatMultiCerebrasModel,
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
          setChatOrchCopilotModel,
          setChatOrchCodexModel,
          setChatMultiGroqModel,
          setChatMultiGeminiModel,
          setChatMultiCerebrasModel,
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
          CEREBRAS_MODEL_CHOICES
        },
        optionSets: {
          groqModelOptions,
          copilotModelOptions,
          codexModelOptions,
          geminiModelOptions,
          groqWorkerModelOptions,
          geminiWorkerModelOptions,
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
          codingOrchCopilotModel,
          codingOrchCodexModel,
          codingMultiProvider,
          codingMultiModel,
          codingMultiLanguage,
          codingInputMulti,
          codingMultiGroqModel,
          codingMultiGeminiModel,
          codingMultiCerebrasModel,
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
          setCodingOrchCopilotModel,
          setCodingOrchCodexModel,
          setCodingMultiProvider,
          setCodingMultiModel,
          setCodingMultiLanguage,
          setCodingInputMulti,
          setCodingMultiGroqModel,
          setCodingMultiGeminiModel,
          setCodingMultiCerebrasModel,
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
        responsiveWorkspaceKey,
        setResponsivePane,
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
        setRoutineEnabled,
        deleteRoutineById,
        openRoutineRunDetail,
        resendRoutineRunTelegram,
        setRoutineOutputPreview,
        renderResponsiveSectionTabs
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
        currentSettingsPane,
        renderResponsiveSectionTabs,
        setResponsivePane,
        isPortraitMobileLayout
      });
    }

    return e(
      "div",
      { className: "app-shell" },
      renderGlobalNav(),
      e(
        "main",
        { className: "main-shell", ref: mainShellRef },
        rootTab === "settings"
          ? renderSettings()
          : rootTab === "routine"
            ? renderRoutine()
            : renderWorkspace()
      ),
      renderCodingResultOverlay(),
      renderSafeRefactorOverlay(),
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
        : null
    );
  }

  ReactDOM.createRoot(document.getElementById("root")).render(e(App));
})();
