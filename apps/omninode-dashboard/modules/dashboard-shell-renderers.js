import {
  renderCodingResultDock as renderCodingResultDockModule,
  renderCodingResultOverlay as renderCodingResultOverlayModule,
  renderComposerInputBar as renderComposerInputBarModule,
  renderResponsiveWorkspaceSupportPane as renderResponsiveWorkspaceSupportPaneModule,
  renderThreadSupportStack as renderThreadSupportStackModule
} from "./dashboard-workspace-renderers.js";
import {
  renderSafeRefactorDock as renderSafeRefactorDockModule,
  renderSafeRefactorOverlay as renderSafeRefactorOverlayModule,
  renderSafeRefactorPanel as renderSafeRefactorPanelModule
} from "./refactor-renderers.js";
import {
  renderChatComposerPanel as renderChatComposerPanelModule,
  renderCodingComposerPanel as renderCodingComposerPanelModule
} from "./dashboard-composer-renderers.js";

function startWorkspaceResize(event) {
  if (event.button !== 0) return;
  const grid = event.currentTarget.closest(".workspace-grid");
  if (!grid) return;
  event.preventDefault();

  const bounds = grid.getBoundingClientRect();
  const minWidth = 220;
  const maxWidth = Math.min(560, Math.max(260, bounds.width * 0.42));
  const setWidth = (clientX) => {
    const nextWidth = Math.min(maxWidth, Math.max(minWidth, clientX - bounds.left));
    grid.style.setProperty("--conversation-column-width", `${Math.round(nextWidth)}px`);
  };
  const handlePointerMove = (moveEvent) => setWidth(moveEvent.clientX);
  const handlePointerUp = () => {
    document.removeEventListener("pointermove", handlePointerMove);
    document.removeEventListener("pointerup", handlePointerUp);
    document.body.style.cursor = "";
    document.body.style.userSelect = "";
  };

  document.body.style.cursor = "col-resize";
  document.body.style.userSelect = "none";
  setWidth(event.clientX);
  document.addEventListener("pointermove", handlePointerMove);
  document.addEventListener("pointerup", handlePointerUp, { once: true });
}

export function renderGlobalNav(props) {
  const {
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
    defaultCodexModel,
    defaultCerebrasModel
  } = props;

  const navStatusText = authed ? "세션 인증됨" : status;
  const geminiLabel = settingsState.geminiApiKeySet
    ? "gemini-3-flash-preview / gemini-3.1-flash-lite-preview"
    : "미설정";
  const cerebrasLabel = settingsState.cerebrasApiKeySet
    ? defaultCerebrasModel
    : "미설정";
  const codexLabel = (codexStatus || "").trim().startsWith("설치/인증 완료")
    ? defaultCodexModel
    : codexStatus;

  return e(
    "aside",
    { className: "global-nav" },
    e("div", { className: "global-nav-head" },
      e("div", { className: "brand-wrap" },
        e("h1", { className: "brand" }, "Omni-node"),
        e("p", { className: "brand-sub" }, "From intent to execution.")
      ),
      e("div", { className: "pill-group" },
        e("span", { className: `pill ${authed || navStatusText.startsWith("연결") || navStatusText.startsWith("인증") ? "ok" : "idle"}` }, navStatusText)
      )
    ),
    e("div", { className: "global-nav-menu" },
      e("div", { className: "nav-title" }, "메뉴"),
      e("button", { className: `nav-btn ${rootTab === "chat" ? "active" : ""}`, onClick: () => setRootTab("chat") }, "대화"),
      e("button", { className: `nav-btn ${rootTab === "routine" ? "active" : ""}`, onClick: () => setRootTab("routine") }, "루틴"),
      e("button", { className: `nav-btn ${rootTab === "logic" ? "active" : ""}`, onClick: () => setRootTab("logic") }, "로직"),
      e("button", { className: `nav-btn ${rootTab === "coding" ? "active" : ""}`, onClick: () => setRootTab("coding") }, "코딩"),
      e("button", { className: `nav-btn ${rootTab === "notebook" ? "active" : ""}`, onClick: () => setRootTab("notebook") }, "노트북"),
      e("button", { className: `nav-btn ${rootTab === "automation" ? "active" : ""}`, onClick: () => setRootTab("automation") }, "자동화/계획"),
      e("button", { className: `nav-btn ${rootTab === "settings" ? "active" : ""}`, onClick: () => setRootTab("settings") }, "설정")
    ),
    e("div", { className: "nav-meta" },
      e("div", null, `Groq: ${selectedGroqModel || "-"}`),
      e("div", null, `Gemini: ${geminiLabel}`),
      e("div", null, `Cerebras: ${cerebrasLabel}`),
      e("div", null, `Copilot: ${selectedCopilotModel || "-"}`),
      e("div", null, `Codex: ${codexLabel || "-"}`),
      e("div", null, copilotStatus)
    )
  );
}

export function renderModeTabs(props) {
  const {
    e,
    rootTab,
    chatMode,
    codingMode,
    chatModes,
    codingModes,
    setChatMode,
    setCodingMode
  } = props;

  const modes = rootTab === "coding" ? codingModes : chatModes;
  const activeMode = rootTab === "coding" ? codingMode : chatMode;

  return e(
    "div",
    { className: "mode-tabs" },
    modes.map((item) => e(
      "button",
      {
        key: item.key,
        className: `mode-btn ${activeMode === item.key ? "active" : ""}`,
        onClick: () => {
          if (rootTab === "coding") {
            setCodingMode(item.key);
          } else {
            setChatMode(item.key);
          }
        }
      },
      item.label
    ))
  );
}

export function buildCodingResultRendererProps(props) {
  const {
    e,
    MarkdownBubbleText,
    rootTab,
    currentConversationId,
    codingResultByConversation,
    filePreviewByConversation,
    runtimeByConversation,
    executionInputByConversation,
    showExecutionLogsByConversation,
    sanitizeCodingAssistantText,
    buildCodingMultiRenderSnapshot,
    requestWorkspaceFilePreview,
    requestLatestCodingResultExecution,
    humanPath,
    setCodingExecutionInputByConversation,
    setShowExecutionLogsByConversation,
    actions
  } = props;

  return {
    e,
    MarkdownBubbleText,
    rootTab,
    currentConversationId,
    codingResultByConversation,
    filePreviewByConversation,
    runtimeByConversation,
    executionInputByConversation,
    showExecutionLogsByConversation,
    sanitizeCodingAssistantText,
    buildCodingMultiRenderSnapshot,
    requestWorkspaceFilePreview,
    requestLatestCodingResultExecution,
    humanPath,
    setCodingExecutionInputByConversation,
    setShowExecutionLogsByConversation,
    actions
  };
}

export function renderCodingResultDock(props) {
  return renderCodingResultDockModule({
    ...buildCodingResultRendererProps(props),
    open: props.open
  });
}

export function renderCodingResultOverlay(props) {
  return renderCodingResultOverlayModule({
    ...buildCodingResultRendererProps(props),
    open: props.open
  });
}

export function buildSafeRefactorRendererProps(props) {
  const {
    e,
    rootTab,
    state,
    selectedLines,
    currentSnippet,
    selectedSummary,
    helpers,
    actions
  } = props;

  return {
    e,
    rootTab,
    state,
    selectedLines,
    currentSnippet,
    selectedSummary,
    helpers,
    actions
  };
}

export function renderSafeRefactorPanel(props) {
  return renderSafeRefactorPanelModule(buildSafeRefactorRendererProps(props));
}

export function renderSafeRefactorDock(props) {
  return renderSafeRefactorDockModule({
    ...buildSafeRefactorRendererProps(props),
    open: props.open
  });
}

export function renderSafeRefactorOverlay(props) {
  return renderSafeRefactorOverlayModule({
    ...buildSafeRefactorRendererProps(props),
    open: props.open
  });
}

export function renderComposerInputBar(props) {
  return renderComposerInputBarModule(props);
}

export function renderThreadSupportStack(props) {
  return renderThreadSupportStackModule(props);
}

export function renderResponsiveWorkspaceSupportPane(props) {
  return renderResponsiveWorkspaceSupportPaneModule(props);
}

export function renderChatComposer(props) {
  return renderChatComposerPanelModule(props);
}

export function renderCodingComposer(props) {
  return renderCodingComposerPanelModule(props);
}

export function renderWorkspace(props) {
  const {
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
    chatComposer,
    codingComposer
  } = props;

  const composer = rootTab === "chat" ? chatComposer : codingComposer;
  const mobileWorkspaceSections = [
    { key: "list", label: rootTab === "coding" ? "작업함" : "보관함" },
    { key: "thread", label: "대화" },
    { key: "support", label: rootTab === "coding" ? "결과" : "보조" }
  ];

  if (isPortraitMobileLayout) {
    return e(
      "div",
      {
        className: "workspace-mobile-shell",
        style: mobileWorkspaceHeight > 0
          ? { minHeight: `${mobileWorkspaceHeight}px`, height: `${mobileWorkspaceHeight}px` }
          : undefined
      },
      renderResponsiveSectionTabs(
        mobileWorkspaceSections,
        currentWorkspacePane,
        (paneKey) => setResponsivePane(responsiveWorkspaceKey, paneKey),
        "workspace-mobile-tabs"
      ),
      currentWorkspacePane === "list"
        ? renderConversationPanel()
        : e(
          "section",
          { className: "chat-panel chat-panel-mobile" },
          currentWorkspacePane === "support"
            ? renderThreadHeader({ showInfoPanel: false, showActionButtons: false, showModebar: false })
            : null,
          errorByKey[currentKey] ? e("div", { className: "error-banner" }, errorByKey[currentKey]) : null,
          currentWorkspacePane === "thread"
            ? e(
              React.Fragment,
              null,
              renderMessages(),
              composer
            )
            : null,
          currentWorkspacePane === "support" ? renderResponsiveWorkspaceSupportPane() : null,
          null
        )
    );
  }

  return e(
    "div",
    { className: "workspace-grid" },
    renderConversationPanel(),
    e("div", {
      className: "workspace-resizer",
      role: "separator",
      "aria-orientation": "vertical",
      title: "좌우 패널 너비 조절",
      onPointerDown: startWorkspaceResize
    }),
    e(
      "section",
      { className: "chat-panel" },
      renderThreadHeader(),
      errorByKey[currentKey] ? e("div", { className: "error-banner" }, errorByKey[currentKey]) : null,
      renderMessages(),
      renderThreadSupportStack(),
      composer
    )
  );
}
