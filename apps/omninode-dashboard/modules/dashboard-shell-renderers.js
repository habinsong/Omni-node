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

function renderKeyboardIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("rect", {
      x: "3",
      y: "6",
      width: "18",
      height: "12",
      rx: "2",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.8"
    }),
    e("path", {
      d: "M7 10h.01M10 10h.01M13 10h.01M16 10h.01M7 14h10",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "2",
      strokeLinecap: "round"
    })
  );
}

function renderChevronDownIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "m6 9 6 6 6-6",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "2",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    })
  );
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
    defaultNvidiaModel,
    defaultCerebrasModel
  } = props;

  const remoteDashboardClient = !!settingsState.remoteDashboardClient;
  const navStatusText = authed ? (remoteDashboardClient ? "외부 접속 인증됨" : "세션 인증됨") : status;
  const geminiLabel = settingsState.geminiApiKeySet
    ? "gemini-3-flash-preview / gemini-3.1-flash-lite-preview"
    : "미설정";
  const cerebrasLabel = settingsState.cerebrasApiKeySet
    ? defaultCerebrasModel
    : "미설정";
  const nvidiaLabel = settingsState.nvidiaApiKeySet
    ? defaultNvidiaModel
    : "미설정";
  const copilotLabel = remoteDashboardClient
    ? "외부 접속 제한"
    : (selectedCopilotModel || "-");
  const codexLabel = remoteDashboardClient
    ? "외부 접속 제한"
    : (codexStatus || "").trim().startsWith("설치/인증 완료")
    ? defaultCodexModel
    : codexStatus;
  const copilotStatusLine = remoteDashboardClient ? null : e("div", null, copilotStatus);

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
      e("button", { className: `nav-btn ${rootTab === "automation" ? "active" : ""}`, onClick: () => setRootTab("automation") }, "작업 계획"),
      e("button", { className: `nav-btn ${rootTab === "skills" ? "active" : ""}`, onClick: () => setRootTab("skills") }, "스킬"),
      e("button", { className: `nav-btn ${rootTab === "settings" ? "active" : ""}`, onClick: () => setRootTab("settings") }, "설정")
    ),
    e("div", { className: "nav-meta" },
      e("div", null, `Groq: ${selectedGroqModel || "-"}`),
      e("div", null, `Gemini: ${geminiLabel}`),
      e("div", null, `Cerebras: ${cerebrasLabel}`),
      e("div", null, `NVIDIA NIM: ${nvidiaLabel}`),
      e("div", null, `Copilot: ${copilotLabel}`),
      e("div", null, `Codex: ${codexLabel || "-"}`),
      copilotStatusLine
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
    const showMobileThread = currentWorkspacePane === "thread";
    const showMobileComposerButton = showMobileThread && !mobileComposerOpen;
    const showSupportPane = currentWorkspacePane === "support";
    return e(
      "div",
      {
        className: `workspace-mobile-shell ${mobileComposerOpen ? "mobile-composer-open" : ""}`,
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
          showSupportPane
            ? renderThreadHeader({ showInfoPanel: false, showActionButtons: false, showModebar: false })
            : null,
          errorByKey[currentKey] ? e("div", { className: "error-banner" }, errorByKey[currentKey]) : null,
          showMobileThread
            ? e(
              React.Fragment,
              null,
              renderMessages(),
              mobileComposerOpen
                ? e(
                  "div",
                  { className: "mobile-composer-layer" },
                  e("button", {
                    type: "button",
                    className: "mobile-composer-minimize",
                    title: "입력창 최소화",
                    "aria-label": "입력창 최소화",
                    onClick: () => setMobileComposerOpen(responsiveWorkspaceKey, false)
                  }, renderChevronDownIcon(e)),
                  composer
                )
                : null,
              showMobileComposerButton
                ? e("button", {
                  type: "button",
                  className: "mobile-keyboard-toggle",
                  title: "입력창 열기",
                  "aria-label": "입력창 열기",
                  onClick: () => setMobileComposerOpen(responsiveWorkspaceKey, true)
                }, renderKeyboardIcon(e))
                : null
            )
            : null,
          showSupportPane
            ? e(
              "div",
              { className: "mobile-support-scroll" },
              renderResponsiveWorkspaceSupportPane()
            )
            : null,
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
