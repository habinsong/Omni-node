import assert from "node:assert/strict";
import {
  handleDashboardServerMessage,
  summarizeToolResult
} from "./modules/dashboard-server-message-router.mjs";

function createStateStore() {
  return {
    rootTab: "settings",
    currentConversationId: "conv-current",
    currentKey: "coding:single",
    isPortraitMobileLayout: false,
    defaultGroqSingleModel: "meta-llama/llama-4-scout-17b-16e-instruct",
    routingPolicyState: { lastAction: "" },
    plansState: { selectedPlanId: "plan-1", lastAction: "" },
    taskGraphState: { selectedGraphId: "graph-1", selectedTaskId: "task-1", lastAction: "" },
    refactorState: { lastAction: "", mode: "anchor" },
    notebooksState: { lastAction: "" },
    filePreviewByConversation: {},
    codingResultByConversation: {},
    authMeta: {},
    authed: false,
    authLocalOffset: "",
    authTtlHours: "",
    status: "",
    authExpiry: "",
    doctorState: { loaded: false },
    contextState: { loaded: false, skills: [], commands: [] },
    settingsState: {},
    geminiUsage: {},
    copilotPremiumUsage: {},
    copilotLocalUsage: {},
    copilotStatus: "",
    copilotDetail: "",
    codexStatus: "",
    codexDetail: "",
    groqModels: [],
    selectedGroqModel: "",
    copilotModels: [],
    selectedCopilotModel: "",
    conversationLists: {},
    activeConversationByKey: {},
    conversationDetails: {},
    selectedMemoryByConversation: {},
    chatMultiResultByConversation: {},
    memoryNotes: [],
    memoryPreview: {},
    selectedConversationIdsByKey: {},
    selectedFoldersByKey: {},
    codingProgressByKey: {},
    pendingByKey: {},
    optimisticUserByKey: {},
    codingExecutionInputByConversation: {},
    codingRuntimeByConversation: {
      "conv-current": { pending: true }
    },
    showExecutionLogsByConversation: {},
    toolSessionKey: "",
    toolControlError: "",
    toolResultPreview: "",
    selectedToolResultId: "",
    toolResultItems: [],
    providerRuntimeItems: [],
    guardObsItems: [],
    guardRetryTimelineItems: [],
    guardAlertDispatchState: {},
    routines: [],
    routineSelectedId: "",
    routineProgress: {},
    routineOutputPreview: {},
    metrics: "",
    errors: {}
  };
}

function createSetter(store, key) {
  return (value) => {
    store[key] = typeof value === "function" ? value(store[key]) : value;
    return store[key];
  };
}

function createContext(store, calls) {
  return {
    state: {
      rootTab: store.rootTab,
      currentConversationId: store.currentConversationId,
      currentKey: store.currentKey,
      isPortraitMobileLayout: store.isPortraitMobileLayout,
      defaultGroqSingleModel: store.defaultGroqSingleModel,
      routingPolicyState: store.routingPolicyState,
      plansState: store.plansState,
      taskGraphState: store.taskGraphState,
      refactorState: store.refactorState,
      notebooksState: store.notebooksState,
      filePreviewByConversation: store.filePreviewByConversation,
      codingResultByConversation: store.codingResultByConversation
    },
    refs: {
      autoCreateConversationRef: { current: {} },
      routineBrowserAgentPreviewRef: { current: "" }
    },
    setters: {
      setAuthMeta: createSetter(store, "authMeta"),
      setAuthed: createSetter(store, "authed"),
      setAuthLocalOffset: createSetter(store, "authLocalOffset"),
      setAuthTtlHours: createSetter(store, "authTtlHours"),
      setStatus: createSetter(store, "status"),
      setContextState: createSetter(store, "contextState"),
      setRoutingPolicyState: createSetter(store, "routingPolicyState"),
      setPlansState: createSetter(store, "plansState"),
      setTaskGraphState: createSetter(store, "taskGraphState"),
      setRefactorState: createSetter(store, "refactorState"),
      setDoctorState: createSetter(store, "doctorState"),
      setNotebooksState: createSetter(store, "notebooksState"),
      setSettingsState: createSetter(store, "settingsState"),
      setGeminiUsage: createSetter(store, "geminiUsage"),
      setCopilotPremiumUsage: createSetter(store, "copilotPremiumUsage"),
      setCopilotLocalUsage: createSetter(store, "copilotLocalUsage"),
      setCopilotStatus: createSetter(store, "copilotStatus"),
      setCopilotDetail: createSetter(store, "copilotDetail"),
      setCodexStatus: createSetter(store, "codexStatus"),
      setCodexDetail: createSetter(store, "codexDetail"),
      setGroqModels: createSetter(store, "groqModels"),
      setSelectedGroqModel: createSetter(store, "selectedGroqModel"),
      setCopilotModels: createSetter(store, "copilotModels"),
      setSelectedCopilotModel: createSetter(store, "selectedCopilotModel"),
      setConversationLists: createSetter(store, "conversationLists"),
      setActiveConversationByKey: createSetter(store, "activeConversationByKey"),
      setConversationDetails: createSetter(store, "conversationDetails"),
      setSelectedMemoryByConversation: createSetter(store, "selectedMemoryByConversation"),
      setChatMultiResultByConversation: createSetter(store, "chatMultiResultByConversation"),
      setMemoryNotes: createSetter(store, "memoryNotes"),
      setMemoryPreview: createSetter(store, "memoryPreview"),
      setSelectedConversationIdsByKey: createSetter(store, "selectedConversationIdsByKey"),
      setSelectedFoldersByKey: createSetter(store, "selectedFoldersByKey"),
      setCodingResultByConversation: createSetter(store, "codingResultByConversation"),
      setShowExecutionLogsByConversation: createSetter(store, "showExecutionLogsByConversation"),
      setCodingRuntimeByConversation: createSetter(store, "codingRuntimeByConversation"),
      setCodingExecutionInputByConversation: createSetter(store, "codingExecutionInputByConversation"),
      setFilePreviewByConversation: createSetter(store, "filePreviewByConversation"),
      setCodingProgressByKey: createSetter(store, "codingProgressByKey"),
      setPendingByKey: createSetter(store, "pendingByKey"),
      setOptimisticUserByKey: createSetter(store, "optimisticUserByKey"),
      setMetrics: createSetter(store, "metrics"),
      setToolSessionKey: createSetter(store, "toolSessionKey"),
      setToolControlError: createSetter(store, "toolControlError"),
      setToolResultPreview: createSetter(store, "toolResultPreview"),
      setSelectedToolResultId: createSetter(store, "selectedToolResultId"),
      setToolResultItems: createSetter(store, "toolResultItems"),
      setProviderRuntimeItems: createSetter(store, "providerRuntimeItems"),
      setGuardObsItems: createSetter(store, "guardObsItems"),
      setGuardRetryTimelineItems: createSetter(store, "guardRetryTimelineItems"),
      setGuardAlertDispatchState: createSetter(store, "guardAlertDispatchState"),
      setRoutines: createSetter(store, "routines"),
      setRoutineSelectedId: createSetter(store, "routineSelectedId"),
      setRoutineProgress: createSetter(store, "routineProgress"),
      setRoutineOutputPreview: createSetter(store, "routineOutputPreview")
    },
    actions: {
      send: (payload) => {
        calls.sent.push(payload);
        return true;
      },
      log: (text, level = "info") => {
        calls.logs.push({ text, level });
      },
      saveAuthToken: (token, expiresAtUtc) => {
        calls.savedAuth.push({ token, expiresAtUtc });
        store.authExpiry = expiresAtUtc;
      },
      clearAuthToken: () => {
        calls.clearAuthToken += 1;
        store.authExpiry = "";
      },
      localUtcOffsetLabel: () => "+09:00",
      ensureAuthed: () => store.authed,
      finishPendingRequest: (key) => {
        calls.finishedKeys.push(key);
      },
      setError: (key, value) => {
        store.errors[key] = value;
      },
      requestConversationDetail: (conversationId) => {
        calls.requests.push({ type: "conversation_detail", conversationId });
      },
      requestAutoCreateConversation: (scope, mode) => {
        calls.requests.push({ type: "auto_create", scope, mode });
      },
      requestWorkspaceFilePreview: (filePath, conversationId) => {
        calls.requests.push({ type: "workspace_file_preview", filePath, conversationId });
      },
      buildCodingRuntimeMessageState: (message, ok, pending = false) => ({
        message,
        ok,
        pending
      }),
      inferErrorKey: (message) => /coding/i.test(`${message || ""}`) ? "coding:single" : "chat:single",
      requestDoctorLast: (send, options) => {
        calls.requests.push({ type: "doctor_last", options, send: typeof send });
      },
      requestRoutingPolicyGet: (_send, options) => {
        calls.requests.push({ type: "routing_policy_get", options });
      },
      requestRoutingDecisionGetLast: (_send, options) => {
        calls.requests.push({ type: "routing_decision_get_last", options });
      },
      requestPlanList: (_send, options) => {
        calls.requests.push({ type: "plan_list", options });
      },
      requestTaskGraphList: (_send, options) => {
        calls.requests.push({ type: "task_graph_list", options });
      },
      requestContextScan: (_send, options) => {
        calls.requests.push({ type: "context_scan", options });
      },
      requestSkillsList: (_send, options) => {
        calls.requests.push({ type: "skills_list", options });
      },
      requestCommandsList: (_send, options) => {
        calls.requests.push({ type: "commands_list", options });
      },
      requestNotebookGet: (_send, projectKey, options) => {
        calls.requests.push({ type: "notebook_get", projectKey, options });
      },
      requestPlanGet: (_send, planId, options) => {
        calls.requests.push({ type: "plan_get", planId, options });
      },
      requestTaskGraphGet: (_send, graphId, options) => {
        calls.requests.push({ type: "task_graph_get", graphId, options });
      },
      requestTaskOutput: (_send, graphId, taskId, options) => {
        calls.requests.push({ type: "task_output", graphId, taskId, options });
      },
      requestRefactorRead: (_send, filePath, options) => {
        calls.requests.push({ type: "refactor_read", filePath, options });
      },
      setResponsivePane: (tabKey, paneKey) => {
        calls.responsivePane.push({ tabKey, paneKey });
      }
    },
    handlers: {
      handleConversationMemoryMessage: (msg) => {
        if (msg.type === "conversation_detail") {
          calls.delegated.push("conversation_detail");
          return true;
        }
        return false;
      },
      handleRoutineMessage: (msg) => {
        if (msg.type === "routine_result") {
          calls.delegated.push("routine_result");
          return true;
        }
        return false;
      },
      handleExecutionFlowMessage: (msg) => {
        if (msg.type === "coding_result") {
          calls.delegated.push("coding_result");
          return true;
        }
        return false;
      }
    },
    utils: {
      normalizeChatMultiResultMessage: (value) => value,
      attachLatencyMetaToConversation: (conversation) => conversation,
      buildProviderRuntimeEventsFromMessage: (msg) => msg.type === "provider_runtime_event"
        ? [{
          provider: "groq",
          scope: "chat",
          mode: "single",
          model: "gpt-oss-120b",
          statusLabel: "ready",
          statusTone: "ok",
          hasError: false,
          detail: "ok"
        }]
        : [],
      summarizeProviderRuntimeEntry: (entry) => `${entry.provider}:${entry.statusLabel}`,
      PROVIDER_RUNTIME_KEYS: ["groq", "gemini", "cerebras", "copilot", "codex", "auto", "unknown"],
      buildGuardObsEvent: (msg) => msg.type === "guard_obs_event"
        ? { channel: "chat", blocked: false, retryRequired: false }
        : null,
      buildGuardRetryTimelineEntry: (event, capturedAt) => ({
        channel: event.channel,
        capturedAt
      }),
      GUARD_RETRY_TIMELINE_MAX_ENTRIES: 8,
      inferToolResultGroup: (type) => type.startsWith("telegram") ? "telegram" : "web",
      inferToolResultDomain: (group) => group === "web" ? "rag" : "tool",
      inferToolResultAction: (msg) => msg.action || (msg.type === "web_search_result" ? "search" : "command"),
      inferToolResultStatus: (msg) => ({
        label: msg.ok === false ? "error" : "ok",
        tone: msg.ok === false ? "error" : "ok",
        hasError: !!msg.error || msg.ok === false
      }),
      TOOL_RESULT_TYPES: new Set(["telegram_stub_result", "web_search_result"])
    }
  };
}

function createCallStore() {
  return {
    sent: [],
    logs: [],
    savedAuth: [],
    clearAuthToken: 0,
    finishedKeys: [],
    requests: [],
    delegated: [],
    responsivePane: []
  };
}

function run() {
  assert.equal(
    summarizeToolResult({ type: "web_search_result", provider: "gemini", results: [1, 2], query: "Omni-node" }),
    "web.search provider=gemini results=2 query=Omni-node"
  );

  const store = createStateStore();
  const calls = createCallStore();

  handleDashboardServerMessage({
    type: "auth_result",
    ok: true,
    authToken: "token-1",
    expiresAtUtc: "2026-03-10T12:00:00Z",
    localUtcOffset: "+09:00",
    ttlHours: 24
  }, createContext(store, calls));

  assert.equal(store.authed, true);
  assert.equal(store.status, "세션 인증됨");
  assert.equal(store.authExpiry, "2026-03-10T12:00:00Z");
  assert.deepEqual(
    calls.requests.map((entry) => entry.type),
    [
      "doctor_last",
      "routing_policy_get",
      "routing_decision_get_last",
      "plan_list",
      "task_graph_list",
      "context_scan",
      "skills_list",
      "commands_list",
      "notebook_get"
    ]
  );

  handleDashboardServerMessage({
    type: "telegram_stub_result",
    ok: false,
    status: "failed",
    input: "/llm status",
    childSessionKey: "child-1",
    error: "telegram down"
  }, createContext(store, calls));

  assert.equal(store.toolSessionKey, "child-1");
  assert.equal(store.toolControlError, "telegram down");
  assert.equal(store.toolResultItems.length, 1);
  assert.equal(store.toolResultItems[0].group, "telegram");
  assert.match(store.toolResultItems[0].summary, /^telegram\.stub/);

  handleDashboardServerMessage({
    type: "provider_runtime_event"
  }, createContext(store, calls));
  assert.equal(store.providerRuntimeItems.length, 1);
  assert.equal(store.providerRuntimeItems[0].summary, "groq:ready");

  handleDashboardServerMessage({
    type: "guard_obs_event"
  }, createContext(store, calls));
  assert.equal(store.guardObsItems.length, 1);
  assert.equal(store.guardRetryTimelineItems.length, 1);

  handleDashboardServerMessage({
    type: "conversation_detail"
  }, createContext(store, calls));
  handleDashboardServerMessage({
    type: "coding_result"
  }, createContext(store, calls));
  handleDashboardServerMessage({
    type: "routine_result"
  }, createContext(store, calls));
  assert.deepEqual(calls.delegated, ["conversation_detail", "coding_result", "routine_result"]);

  handleDashboardServerMessage({
    type: "guard_alert_dispatch_result",
    ok: true,
    message: "sent",
    targets: [{ name: "tg", status: "sent", attempts: 1 }]
  }, createContext(store, calls));
  assert.equal(store.guardAlertDispatchState.statusLabel, "sent");
  assert.equal(store.guardAlertDispatchState.sentCount, 1);

  handleDashboardServerMessage({
    type: "error",
    message: "coding unauthorized"
  }, createContext(store, calls));

  assert.equal(calls.clearAuthToken, 1);
  assert.equal(store.authed, false);
  assert.equal(store.status, "인증 필요");
  assert.equal(store.errors["coding:single"], "오류: coding unauthorized");
  assert.equal(store.codingRuntimeByConversation["conv-current"].pending, false);
  assert.match(store.codingRuntimeByConversation["conv-current"].message, /세션 인증이 만료/);

  console.log(JSON.stringify({
    ok: true,
    assertions: 20,
    delegated: calls.delegated,
    requestTypes: calls.requests.map((entry) => entry.type),
    toolResultType: store.toolResultItems[0].type,
    unauthorizedStatus: store.status
  }, null, 2));
}

run();
