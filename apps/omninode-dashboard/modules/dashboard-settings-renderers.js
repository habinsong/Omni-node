import { renderDoctorPanel } from "./doctor-renderers.js";
import { renderPlansPanel } from "./plans-renderers.js";
import { renderContextPanel } from "./context-renderers.js";
import { renderRoutingPolicyPanel } from "./routing-policy-renderers.js";
import { renderTaskGraphPanel } from "./task-graph-renderers.js";
import { renderNotebooksPanel } from "./notebooks-renderers.js";

export function renderNotebookRootPanel(props) {
  const {
    e,
    authed,
    notebooksState,
    setNotebookProjectKey,
    setNotebookAppendKind,
    setNotebookAppendText,
    setNotebookFilterText,
    setNotebookExpandedDocument,
    refreshNotebook,
    appendNotebook,
    createNotebookHandoff,
    appendSelectedPlanDecision,
    appendSelectedTaskVerification,
    appendDoctorVerification,
    appendRefactorVerification
  } = props;

  return e(
    "section",
    { className: "settings settings-root-panel settings-root-panel-notebook" },
    e("div", { className: "settings-shell settings-standalone-shell" },
      e("div", { className: "settings-summary" },
        e("div", { className: "settings-summary-head" },
          e("div", null,
            e("h2", null, "노트북"),
            e("p", null, "작업 기록, handoff, 검증 메모를 한 곳에서 정리합니다.")
          )
        )
      ),
      e("div", { className: "settings-detail-stack settings-root-detail-stack" },
        e("div", { className: "settings-detail-item" },
          renderNotebooksPanel({
            e,
            authed,
            notebooksState,
            setNotebookProjectKey,
            setNotebookAppendKind,
            setNotebookAppendText,
            setNotebookFilterText,
            setNotebookExpandedDocument,
            refreshNotebook,
            appendNotebook,
            createNotebookHandoff,
            appendSelectedPlanDecision,
            appendSelectedTaskVerification,
            appendDoctorVerification,
            appendRefactorVerification
          })
        )
      )
    )
  );
}

export function renderAutomationRootPanel(props) {
  const {
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
    defaultCodexModel,
    geminiModelChoices,
    cerebrasModelChoices,
    nvidiaModelChoices,
    send,
    currentAutomationPane,
    setAutomationPane
  } = props;
  const activePane = currentAutomationPane === "tasks" ? "tasks" : "plans";
  const planCount = Array.isArray(plansState?.items) ? plansState.items.length : 0;
  const graphCount = Array.isArray(taskGraphState?.items) ? taskGraphState.items.length : 0;
  const activePanel = activePane === "tasks"
    ? renderTaskGraphPanel({
      e,
      authed,
      plansState,
      taskGraphState,
      routingPolicyState,
      setTaskGraphCreatePlanId,
      setRoutingPolicyChain,
      refreshRoutingPolicy,
      saveRoutingPolicy,
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
      defaultCodexModel,
      geminiModelChoices,
      cerebrasModelChoices,
      nvidiaModelChoices,
      send
    })
    : renderPlansPanel({
      e,
      authed,
      plansState,
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
      selectedGroqModel,
      setSelectedGroqModel,
      groqModels,
      selectedCopilotModel,
      setSelectedCopilotModel,
      copilotModels,
      defaultCodexModel,
      geminiModelChoices,
      cerebrasModelChoices,
      nvidiaModelChoices,
      send
    });
  const navItems = [
    {
      key: "plans",
      title: "작업 정리",
      helper: "할 일 정리, 점검, 진행 확정",
      meta: `${planCount}건`
    },
    {
      key: "tasks",
      title: "작업 나누기",
      helper: "작업 묶음 만들기, AI 순서",
      meta: `${graphCount}건`
    }
  ];

  return e(
    "section",
    { className: "settings settings-root-panel settings-root-panel-automation" },
    e("div", { className: "settings-shell settings-standalone-shell" },
      e("div", { className: "automation-root-layout" },
        e("aside", { className: "automation-root-nav", "aria-label": "작업 계획 선택" },
          navItems.map((item) => e("button", {
            key: item.key,
            type: "button",
            className: `automation-root-nav-item ${activePane === item.key ? "active" : ""}`,
            onClick: () => setAutomationPane(item.key)
          },
          e("span", { className: "automation-root-nav-title" }, item.title),
          e("span", { className: "automation-root-nav-helper" }, item.helper),
          e("span", { className: "automation-root-nav-meta" }, item.meta)))
        ),
        e("div", { className: "settings-detail-stack settings-root-detail-stack automation-root-stack" },
          e("div", { className: "settings-detail-item" }, activePanel)
        )
      )
    )
  );
}

export function renderSettingsPanel(props) {
  const {
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
    opsDomainFilters,
    opsDomainStats,
    applyDomainFocus,
    filteredOpsFlowItems,
    workerRef,
    toolPanel,
    uiPreferencePanel,
    currentSettingsPane,
    renderResponsiveSectionTabs,
    setResponsivePane,
    isPortraitMobileLayout
  } = props;

  const keyReadyItems = [
    settingsState.telegramBotTokenSet && settingsState.telegramChatIdSet,
    settingsState.groqApiKeySet,
    settingsState.geminiApiKeySet,
    settingsState.cerebrasApiKeySet,
    settingsState.nvidiaApiKeySet,
    settingsState.codexApiKeySet
  ];
  const keyReadyCount = keyReadyItems.filter(Boolean).length;
  const remoteDashboardClient = !!settingsState.remoteDashboardClient;
  const externalDashboardEnabled = !!settingsState.externalDashboardEnabled;
  const dashboardExternalUrls = Array.isArray(settingsState.dashboardExternalUrls)
    ? settingsState.dashboardExternalUrls.filter(Boolean)
    : [];
  const copilotReady = /완료|인증|ready/i.test(copilotStatus || "");
  const codexReady = /완료|인증|logged|ready/i.test(codexStatus || "");
  const cliReadyCount = [copilotReady, codexReady].filter(Boolean).length;
  const requestedSettingsPane = currentSettingsPane || (remoteDashboardClient ? "basic-remote-limited" : "basic-auth");

  const statusTone = (ok) => ok ? "ok" : "idle";
  const renderStatusChip = (label, ok) => e(
    "span",
    { className: `settings-status-chip ${statusTone(ok)}` },
    label
  );

  const renderMiniStat = (label, value, tone = "") => e(
    "div",
    { className: `settings-mini-stat ${tone}`.trim() },
    e("span", null, label),
    e("strong", null, value || "-")
  );

  const renderInfoRow = (label, value, chip) => e(
    "div",
    { className: "settings-info-row" },
    e("span", null, label),
    chip || e("strong", null, value || "-")
  );

  const renderPermissionList = (title, items) => e(
    "div",
    { className: "settings-form-card" },
    e("strong", null, title),
    e("ul", { className: "settings-note-list" },
      items.map((item) => e("li", { key: item }, item))
    )
  );

  const otpPanel = e("section", { className: "panel settings-optimized-panel settings-otp-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "OTP 인증"),
        e("p", null, authMeta.telegramConfigured ? "Telegram으로 받은 6자리 코드로 현재 세션 접근을 승인합니다." : "Telegram이 없으면 서버 콘솔 fallback OTP로 로컬 인증합니다.")
      ),
      e("div", { className: "settings-visual-card auth" },
        e("span", null, "SESSION"),
        e("strong", null, remoteDashboardClient && authed ? "외부 접속 제한 모드" : authed ? "인증됨" : "OTP 대기"),
        renderStatusChip(remoteDashboardClient && authed ? "제한 모드" : authed ? "접근 가능" : "인증 필요", authed)
      )
    ),
    e("div", { className: "settings-mini-stat-grid" },
      renderMiniStat("인증 상태", authed ? "활성" : "대기", authed ? "ok" : "idle"),
      renderMiniStat("유지 시간", `${authTtlHours || 24}시간`),
      renderMiniStat("시간대", authLocalOffset || "-")
    ),
    e("div", { className: "settings-form-card" },
      e("label", { className: "settings-field" },
        e("span", null, "OTP 코드"),
        e("input", {
          className: "input",
          value: otp,
          onChange: (event) => setOtp(event.target.value),
          placeholder: "OTP 6자리",
          maxLength: 6
        })
      ),
      e("label", { className: "settings-field compact" },
        e("span", null, "인증 유지 시간"),
        e("input", {
          className: "input compact",
          type: "number",
          min: 1,
          max: 168,
          value: authTtlHours,
          onChange: (event) => setAuthTtlHours(event.target.value)
        })
      ),
      e("div", { className: "settings-action-row" },
        e("button", {
          className: "btn",
          onClick: () => send({ type: "request_otp" })
        }, "OTP 요청"),
        e("button", {
          className: "btn primary",
          onClick: () => {
            const code = otp.trim();
            if (code.length !== 6) {
              log("OTP 6자리를 입력하세요.", "error");
              return;
            }
            const parsedTtl = parseInt(authTtlHours, 10);
            const ttlHours = Number.isFinite(parsedTtl)
              ? Math.max(1, Math.min(168, parsedTtl))
              : 24;
            setAuthTtlHours(String(ttlHours));
            send({ type: "auth", otp: code, authTtlHours: ttlHours });
          }
        }, "OTP 인증")
      )
    ),
    e("div", { className: "settings-info-grid" },
      renderInfoRow("Session", authMeta.sessionId || "-"),
      renderInfoRow("인증 만료", authExpiry || "-"),
      renderInfoRow("허용 범위", "1~168시간")
    )
  );

  const remoteLimitedPanel = e("section", { className: "panel settings-optimized-panel settings-remote-limited-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "외부 접속 제한 모드"),
        e("p", null, "외부 접속에서는 OTP 없이 작업 기능을 사용하되, 인증과 시크릿 설정만 막습니다.")
      ),
      e("div", { className: "settings-visual-card auth" },
        e("span", null, "REMOTE"),
        e("strong", null, "제한 모드"),
        renderStatusChip("작업 기능 허용", true)
      )
    ),
    e("div", { className: "settings-info-grid" },
      renderInfoRow("접속 방식", "OTP 요청 없음"),
      renderInfoRow("차단 기준", "인증/시크릿/외부접속 설정"),
      renderInfoRow("작업 기능", "대화·코딩·루틴·로직·노트북 허용")
    ),
    renderPermissionList("허용", [
      "대화, 코딩, 루틴 실행",
      "로직 그래프 목록, 열기, 저장, 삭제, 실행, 취소, 결과 조회",
      "노트북, 작업 계획, 라우팅 정책, 모델 선택"
    ]),
    renderPermissionList("차단", [
      "OTP 요청과 인증 재개",
      "외부접속 토글 변경",
      "Telegram/LLM 키 저장, 삭제, 테스트",
      "Copilot/Codex CLI 인증 상태 조회, 로그인, 로그아웃"
    ])
  );

  const externalAccessPanel = e("section", { className: "panel settings-optimized-panel settings-external-access-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "외부접속"),
        e("p", null, "같은 LAN의 다른 기기에서 현재 대시보드에 접속할 수 있게 합니다.")
      ),
      e("div", { className: "settings-visual-card console" },
        e("span", null, "LAN ACCESS"),
        e("strong", null, externalDashboardEnabled ? "활성화" : "비활성화"),
        renderStatusChip(externalDashboardEnabled ? "외부 접속 허용" : "로컬 전용", externalDashboardEnabled)
      )
    ),
    e("div", { className: "settings-form-card" },
      e("label", { className: "settings-save-mode" },
        e("input", {
          type: "checkbox",
          checked: externalDashboardEnabled,
          onChange: (event) => send({ type: "set_external_dashboard_access", enabled: event.target.checked })
        }),
        e("span", null, "같은 LAN 기기 접속 허용"),
        e("small", null, externalDashboardEnabled
          ? "현재 리스너가 외부접속을 받도록 열려 있습니다."
          : "현재 대시보드는 이 기기의 localhost에서만 열립니다.")
      )
    ),
    e("div", { className: "settings-section" },
      e("div", { className: "settings-section-head" },
        e("div", null,
          e("div", { className: "settings-section-kicker" }, "ACCESS URL"),
          e("div", { className: "settings-section-title" }, "다른 기기에서 열 주소"),
          e("div", { className: "settings-section-sub" }, externalDashboardEnabled
            ? "같은 와이파이/LAN에 연결된 기기에서 아래 주소를 여세요."
            : "외부접속을 활성화하면 접속 주소가 표시됩니다.")
        )
      ),
      externalDashboardEnabled && dashboardExternalUrls.length > 0
        ? e("div", { className: "settings-info-grid" },
          dashboardExternalUrls.map((url) => renderInfoRow("주소", url, e("code", null, url)))
        )
        : e("div", { className: `settings-banner ${externalDashboardEnabled ? "warn" : ""}`.trim() },
          e("span", null, externalDashboardEnabled
            ? "사용 가능한 LAN IPv4 주소를 찾지 못했습니다. 네트워크 연결을 확인하세요."
            : "외부접속 비활성 상태입니다.")
        )
    ),
    e("div", { className: "settings-info-grid" },
      renderInfoRow("상태", externalDashboardEnabled ? "외부접속 활성" : "로컬 전용"),
      renderInfoRow("범위", "같은 LAN"),
      renderInfoRow("적용", "즉시 반영")
    )
  );

  const telegramReady = settingsState.telegramBotTokenSet && settingsState.telegramChatIdSet;
  const telegramPanel = e("section", { className: "panel settings-optimized-panel settings-telegram-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "Telegram 연동"),
        e("p", null, "OTP, 루틴 결과, 운영 알림을 보낼 Telegram 봇과 채팅 대상을 설정합니다.")
      ),
      e("div", { className: "settings-visual-card telegram" },
        e("span", null, "DELIVERY"),
        e("strong", null, telegramReady ? "연결됨" : "미설정"),
        renderStatusChip(telegramReady ? "발송 준비" : "설정 필요", telegramReady)
      )
    ),
    e("div", { className: "settings-mini-stat-grid" },
      renderMiniStat("Bot Token", settingsState.telegramBotTokenMasked || "미설정", settingsState.telegramBotTokenSet ? "ok" : "idle"),
      renderMiniStat("Chat ID", settingsState.telegramChatIdMasked || "미설정", settingsState.telegramChatIdSet ? "ok" : "idle"),
      renderMiniStat("저장 방식", persist ? "보안 저장" : "현재 세션")
    ),
    e("div", { className: "settings-form-card" },
      e("div", { className: "settings-form-grid two" },
        e("label", { className: "settings-field" },
          e("span", null, "Bot Token"),
          e("input", {
            className: "input",
            value: telegramBotToken,
            onChange: (event) => setTelegramBotToken(event.target.value),
            placeholder: settingsState.telegramBotTokenMasked || "미설정"
          })
        ),
        e("label", { className: "settings-field" },
          e("span", null, "Chat ID"),
          e("input", {
            className: "input",
            value: telegramChatId,
            onChange: (event) => setTelegramChatId(event.target.value),
            placeholder: settingsState.telegramChatIdMasked || "미설정"
          })
        )
      ),
      e("label", { className: "settings-save-mode" },
        e("input", {
          type: "checkbox",
          checked: persist,
          onChange: (event) => setPersist(event.target.checked)
        }),
        e("span", null, "보안 저장소 저장/삭제"),
        e("small", null, persist ? "키체인 또는 0600 보안 저장소에 반영합니다." : "현재 실행 중인 프로세스에만 반영합니다.")
      ),
      e("div", { className: "settings-action-row" },
        e("button", {
          className: "btn primary",
          onClick: () => send({
            type: "set_telegram_credentials",
            telegramBotToken: telegramBotToken.trim() || undefined,
            telegramChatId: telegramChatId.trim() || undefined,
            persist
          })
        }, "저장"),
        e("button", { className: "btn", onClick: () => send({ type: "test_telegram" }) }, "테스트 전송"),
        e("button", {
          className: "btn ghost",
          onClick: () => {
            send({ type: "delete_telegram_credentials", persist });
            setTelegramBotToken("");
            setTelegramChatId("");
          }
        }, "연동 삭제")
      )
    )
  );

  const providerKeyCards = [
    { key: "groq", label: "Groq", value: groqApiKey, setValue: setGroqApiKey, masked: settingsState.groqApiKeyMasked, set: settingsState.groqApiKeySet, helper: "빠른 응답 모델" },
    { key: "gemini", label: "Gemini", value: geminiApiKey, setValue: setGeminiApiKey, masked: settingsState.geminiApiKeyMasked, set: settingsState.geminiApiKeySet, helper: "grounding / 검색" },
    { key: "cerebras", label: "Cerebras", value: cerebrasApiKey, setValue: setCerebrasApiKey, masked: settingsState.cerebrasApiKeyMasked, set: settingsState.cerebrasApiKeySet, helper: "대체 고속 모델" },
    { key: "nvidia", label: "NVIDIA NIM", value: nvidiaApiKey, setValue: setNvidiaApiKey, masked: settingsState.nvidiaApiKeyMasked, set: settingsState.nvidiaApiKeySet, helper: "OpenAI 호환 NIM" },
    { key: "codex", label: "Codex", value: codexApiKey, setValue: setCodexApiKey, masked: settingsState.codexApiKeyMasked, set: settingsState.codexApiKeySet, helper: "API 키 선택 사용" }
  ];
  const llmPanel = e("section", { className: "panel settings-optimized-panel settings-llm-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "LLM 키"),
        e("p", null, "제공자별 키를 따로 관리합니다. 비어 있는 입력은 기존 저장값을 유지합니다.")
      ),
      e("div", { className: "settings-visual-card keys" },
        e("span", null, "PROVIDERS"),
        e("strong", null, `${keyReadyCount} / ${keyReadyItems.length}`),
        e("div", { className: "settings-meter" },
          e("span", { style: { width: `${Math.round((keyReadyCount / keyReadyItems.length) * 100)}%` } })
        )
      )
    ),
    e("div", { className: "settings-provider-key-grid" },
      providerKeyCards.map((item) => e(
        "label",
        { key: item.key, className: `settings-provider-key-card ${item.set ? "ready" : ""}` },
        e("div", { className: "settings-provider-key-head" },
          e("div", null,
            e("strong", null, item.label),
            e("span", null, item.helper)
          ),
          renderStatusChip(item.set ? "저장됨" : "미설정", item.set)
        ),
        e("input", {
          className: "input",
          value: item.value,
          onChange: (event) => item.setValue(event.target.value),
          placeholder: item.masked || "새 키 입력"
        })
      ))
    ),
    e("div", { className: "settings-save-mode inline" },
      e("input", {
        type: "checkbox",
        checked: persist,
        onChange: (event) => setPersist(event.target.checked)
      }),
      e("span", null, persist ? "저장/삭제를 보안 저장소에도 반영" : "현재 실행 중인 프로세스에만 반영")
    ),
    e("div", { className: "settings-action-row" },
      e("button", {
        className: "btn primary",
        onClick: () => send({
          type: "set_llm_credentials",
          groqApiKey: groqApiKey.trim() || undefined,
          geminiApiKey: geminiApiKey.trim() || undefined,
          cerebrasApiKey: cerebrasApiKey.trim() || undefined,
          nvidiaApiKey: nvidiaApiKey.trim() || undefined,
          codexApiKey: codexApiKey.trim() || undefined,
          persist
        })
      }, "키 저장"),
      e("button", {
        className: "btn ghost",
        onClick: () => {
          send({ type: "delete_llm_credentials", persist });
          setGroqApiKey("");
          setGeminiApiKey("");
          setCerebrasApiKey("");
          setNvidiaApiKey("");
          setCodexApiKey("");
        }
      }, "키 삭제"),
      e("button", { className: "btn", onClick: () => send({ type: "get_groq_models" }) }, "Groq 새로고침"),
      e("button", { className: "btn", onClick: () => send({ type: "get_copilot_models" }) }, "Copilot 새로고침")
    )
  );

  const cliAuthPanel = e("section", { className: "panel settings-optimized-panel settings-cli-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "CLI 인증 상태"),
        e("p", null, "로컬 Copilot / Codex CLI 인증 상태를 확인하고 로그인 흐름을 시작합니다.")
      ),
      e("div", { className: "settings-visual-card cli" },
        e("span", null, "CLI READY"),
        e("strong", null, `${cliReadyCount} / 2`),
        e("div", { className: "settings-chip-row" },
          renderStatusChip("Copilot", copilotReady),
          renderStatusChip("Codex", codexReady)
        )
      )
    ),
    e("div", { className: "settings-cli-card-grid" },
      e("article", { className: "settings-cli-card" },
        e("div", { className: "settings-cli-card-head" },
          e("div", null,
            e("strong", null, "Copilot"),
            e("span", null, copilotDetail || "-")
          ),
          renderStatusChip(copilotReady ? "인증됨" : "확인 필요", copilotReady)
        ),
        e("div", { className: "settings-action-row" },
          e("button", { className: "btn", onClick: () => send({ type: "get_copilot_status" }) }, "상태 조회"),
          e("button", { className: "btn primary", onClick: () => send({ type: "start_copilot_login" }) }, "로그인 시작")
        )
      ),
      e("article", { className: "settings-cli-card" },
        e("div", { className: "settings-cli-card-head" },
          e("div", null,
            e("strong", null, "Codex"),
            e("span", null, codexDetail || "-")
          ),
          renderStatusChip(codexReady ? "인증됨" : "확인 필요", codexReady)
        ),
        e("div", { className: "settings-action-row" },
          e("button", { className: "btn", onClick: () => send({ type: "get_codex_status" }) }, "상태 조회"),
          e("button", {
            className: "btn primary",
            onClick: () => {
              setCodexStatus("로그인 시작 중");
              setCodexDetail("브라우저 인증 흐름을 시작하는 중입니다...");
              send({ type: "start_codex_login" });
            }
          }, "OAuth 로그인 시작"),
          e("button", {
            className: "btn ghost",
            onClick: () => {
              setCodexStatus("로그아웃 처리 중");
              setCodexDetail("Codex 인증 정보를 정리하는 중입니다...");
              send({ type: "logout_codex" });
            }
          }, "OAuth 로그아웃")
        )
      )
    )
  );

  const geminiUsagePanel = e("section", { className: "panel settings-optimized-panel settings-gemini-usage-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "Gemini 사용량"),
        e("p", null, "토큰 사용량과 단가 기준 누적 추정 과금입니다. 실제 청구액과는 차이가 있을 수 있습니다.")
      ),
      e("div", { className: "settings-visual-card cost" },
        e("span", null, "ESTIMATED COST"),
        e("strong", null, `$${geminiUsage.estimated_cost_usd || "0.000000"}`),
        e("span", { className: "settings-visual-sub" }, `${geminiUsage.total_tokens || 0} tokens`)
      )
    ),
    e("div", { className: "settings-stat-strip" },
      renderMiniStat("요청 수", geminiUsage.requests || 0),
      renderMiniStat("입력 토큰", geminiUsage.prompt_tokens || 0),
      renderMiniStat("출력 토큰", geminiUsage.completion_tokens || 0),
      renderMiniStat("총 토큰", geminiUsage.total_tokens || 0)
    ),
    e("div", { className: "settings-pricing-row" },
      e("span", null, e("b", null, `$${geminiUsage.input_price_per_million_usd}`), " / 1M 입력 토큰"),
      e("span", null, e("b", null, `$${geminiUsage.output_price_per_million_usd}`), " / 1M 출력 토큰")
    ),
    e("div", { className: "settings-action-row" },
      e("button", { className: "btn primary", onClick: () => send({ type: "get_usage_stats" }) }, "사용량 새로고침")
    )
  );

  const copilotPremiumPercentText = copilotPremiumUsage.available
    ? `${formatDecimal(copilotPremiumUsage.percent_used, 1)}%`
    : "-";
  const copilotPremiumUsageText = copilotPremiumUsage.available
    ? `${formatDecimal(copilotPremiumUsage.used_requests, 1)} / ${copilotPremiumQuotaText}`
    : "-";
  const copilotPremiumPanel = e("section", { className: "panel settings-optimized-panel settings-copilot-usage-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "Copilot 사용량"),
        e("p", null, "GitHub Copilot Premium Requests 월 누적 사용량과 Omni-node 로컬 호출 통계를 함께 보여줍니다.")
      ),
      e("div", { className: "settings-visual-card usage" },
        e("span", null, "PREMIUM USAGE"),
        e("strong", null, copilotPremiumPercentText),
        e("div", { className: "settings-meter" },
          e("span", { style: { width: `${copilotPremiumPercent}%` } })
        )
      )
    ),
    e("div", { className: "settings-banner warn" },
      e("span", null,
        e("strong", null, "월 누적 합산 주의 — "),
        "이 값은 GitHub 계정 월누적이며 VS Code, Web, 기타 Copilot 사용량까지 모두 포함됩니다."
      )
    ),
    e("div", { className: "settings-stat-strip" },
      renderMiniStat("사용량 / 한도", copilotPremiumUsageText, copilotPremiumUsage.available ? "ok" : "idle"),
      renderMiniStat("계정", copilotPremiumUsage.username || "-"),
      renderMiniStat("플랜", copilotPremiumUsage.plan_name || "-"),
      renderMiniStat("갱신(로컬)", copilotPremiumUsage.refreshed_local || "-")
    ),
    !copilotPremiumUsage.available
      ? e("div", { className: "settings-banner error" },
          e("span", null, `Copilot Premium 조회 실패: ${copilotPremiumUsage.message || "조회 실패"}`)
        )
      : null,
    copilotPremiumUsage.requires_user_scope
      ? e("div", { className: "settings-banner error" },
          e("span", null,
            e("strong", null, "권한 필요 — "),
            "터미널에서 다음을 실행하세요: ",
            e("code", null, "gh auth refresh -h github.com -s user")
          )
        )
      : null,
    e("div", { className: "settings-action-row" },
      e("button", { className: "btn primary", onClick: () => send({ type: "get_usage_stats" }) }, "사용량 새로고침"),
      e("button", {
        className: "btn",
        onClick: () => window.open(copilotPremiumUsage.features_url || "https://github.com/settings/copilot/features", "_blank", "noopener,noreferrer")
      }, "GitHub Features 열기"),
      e("button", {
        className: "btn",
        onClick: () => window.open(copilotPremiumUsage.billing_url || "https://github.com/settings/billing/premium_requests_usage", "_blank", "noopener,noreferrer")
      }, "Billing 열기")
    ),
    e("details", { className: "settings-disclosure", open: true },
      e("summary", null, `Premium 모델별 사용량 (${Array.isArray(copilotPremiumUsage.items) ? copilotPremiumUsage.items.length : 0}건)`),
      e("div", { className: "settings-disclosure-body" },
        e("div", { className: "table-wrap" },
          e("table", { className: "settings-table-mini" },
            e("thead", null,
              e("tr", null,
                e("th", null, "모델"),
                e("th", null, "사용 횟수"),
                e("th", null, "비율")
              )
            ),
            e("tbody", null, copilotPremiumRows)
          )
        )
      )
    ),
    e("details", { className: "settings-disclosure" },
      e("summary", null, `Omni-node 로컬 호출 (총 ${copilotLocalUsage.total_requests || 0} req · 현재 모델 ${copilotLocalUsage.selected_model || "-"})`),
      e("div", { className: "settings-disclosure-body" },
        e("div", { className: "table-wrap" },
          e("table", { className: "settings-table-mini" },
            e("thead", null,
              e("tr", null,
                e("th", null, "로컬 모델"),
                e("th", null, "요청 수")
              )
            ),
            e("tbody", null, copilotLocalRows)
          )
        )
      )
    )
  );

  const copilotModelsPanel = e("section", { className: "panel settings-optimized-panel settings-copilot-models-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "Copilot 모델"),
        e("p", null, "Copilot CLI 호출에 사용할 모델을 고릅니다. 변경하면 즉시 다음 호출부터 적용됩니다.")
      ),
      e("div", { className: "settings-visual-card model" },
        e("span", null, "ACTIVE MODEL"),
        e("strong", null, selectedCopilotModel || "-"),
        e("span", { className: "settings-visual-sub" }, `${copilotModels.length}개 카탈로그`)
      )
    ),
    e("div", { className: "settings-section" },
      e("div", { className: "settings-section-head" },
        e("div", null,
          e("div", { className: "settings-section-kicker" }, "MODEL SELECT"),
          e("div", { className: "settings-section-title" }, "사용할 모델"),
          e("div", { className: "settings-section-sub" }, "선택 후 '모델 적용'을 누르면 반영됩니다.")
        )
      ),
      e("select", {
        className: "input",
        value: selectedCopilotModel,
        onChange: (event) => setSelectedCopilotModel(event.target.value)
      },
      copilotModels.length === 0
        ? e("option", { value: "" }, "모델 로딩 중")
        : copilotModels.map((x) => e("option", { key: x.id, value: x.id }, x.id))),
      e("div", { className: "settings-action-row" },
        e("button", {
          className: "btn primary",
          onClick: () => {
            if (!selectedCopilotModel) {
              return;
            }
            send({ type: "set_copilot_model", model: selectedCopilotModel });
          }
        }, "모델 적용"),
        e("button", { className: "btn", onClick: () => send({ type: "get_copilot_models" }) }, "카탈로그 새로고침")
      )
    ),
    e("details", { className: "settings-disclosure", open: true },
      e("summary", null, `모델 카탈로그 (${copilotModels.length}개)`),
      e("div", { className: "settings-disclosure-body" },
        e("div", { className: "table-wrap" },
          e("table", { className: "model-table" },
            e("thead", null,
              e("tr", null,
                e("th", null, "모델"),
                e("th", null, "제공사"),
                e("th", null, "Premium 배수"),
                e("th", null, "출력 TPS"),
                e("th", null, "리미트"),
                e("th", null, "컨텍스트"),
                e("th", null, "최대 출력"),
                e("th", null, "사용량")
              )
            ),
            e("tbody", null, copilotRows)
          )
        )
      )
    )
  );

  const selectedGroqInfo = groqModels.find((m) => m.id === selectedGroqModel);
  const groqModelsPanel = e("section", { className: "panel settings-optimized-panel settings-groq-models-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "Groq 모델"),
        e("p", null, "Groq 추론 엔진의 모델을 선택하고 rate limit / 사용량 잔여를 확인합니다.")
      ),
      e("div", { className: "settings-visual-card groq" },
        e("span", null, "ACTIVE MODEL"),
        e("strong", null, selectedGroqModel || "-"),
        e("span", { className: "settings-visual-sub" }, selectedGroqInfo ? `${selectedGroqInfo.tier || "-"} · ${selectedGroqInfo.context_window || "-"} ctx` : `${groqModels.length}개 카탈로그`)
      )
    ),
    e("div", { className: "settings-section" },
      e("div", { className: "settings-section-head" },
        e("div", null,
          e("div", { className: "settings-section-kicker" }, "MODEL SELECT"),
          e("div", { className: "settings-section-title" }, "사용할 모델"),
          e("div", { className: "settings-section-sub" }, "Production 모델은 안정성, Preview는 평가용으로 표시됩니다.")
        )
      ),
      e("select", {
        className: "input",
        value: selectedGroqModel,
        onChange: (event) => setSelectedGroqModel(event.target.value)
      },
      groqModels.length === 0
        ? e("option", { value: "" }, "모델 로딩 중")
        : groqModels.map((x) => e("option", { key: x.id, value: x.id }, `${x.id}${x.tier ? ` · ${x.tier}` : ""}`))),
      e("div", { className: "settings-action-row" },
        e("button", {
          className: "btn primary",
          onClick: () => {
            if (!selectedGroqModel) {
              return;
            }
            send({ type: "set_groq_model", model: selectedGroqModel });
          }
        }, "모델 적용"),
        e("button", { className: "btn", onClick: () => send({ type: "get_groq_models" }) }, "카탈로그 새로고침")
      )
    ),
    e("details", { className: "settings-disclosure" },
      e("summary", null, `전체 카탈로그 + rate limit (${groqModels.length}개 · 가로 스크롤)`),
      e("div", { className: "settings-disclosure-body" },
        e("div", { className: "table-wrap" },
          e("table", { className: "model-table" },
            e("thead", null,
              e("tr", null,
                e("th", null, "모델"),
                e("th", null, "Tier"),
                e("th", null, "출력 TPS"),
                e("th", null, "컨텍스트"),
                e("th", null, "최대 출력"),
                e("th", null, "RPM"),
                e("th", null, "RPD"),
                e("th", null, "TPM"),
                e("th", null, "TPD"),
                e("th", null, "ASH"),
                e("th", null, "ASD"),
                e("th", null, "사용량(분/시/일)"),
                e("th", null, "라이브 잔여/리셋")
              )
            ),
            e("tbody", null, groqRows)
          )
        )
      )
    )
  );

  const opsAllStats = opsDomainStats.all || { count: 0, errorCount: 0, lastSummary: "-" };
  const consolePanel = e("section", { className: "panel settings-optimized-panel settings-console-panel" },
    e("div", { className: "settings-panel-hero" },
      e("div", null,
        e("h2", null, "운영 콘솔"),
        e("p", null, "슬래시 명령 실행, 실시간 메트릭/로그, 도메인별 운영 이벤트를 한 곳에서 봅니다.")
      ),
      e("div", { className: "settings-visual-card console" },
        e("span", null, "ACTIVITY"),
        e("strong", null, `${opsAllStats.count} 건`),
        e("span", { className: "settings-visual-sub" }, `오류 ${opsAllStats.errorCount}건 · 최신 ${opsAllStats.lastSummary || "-"}`)
      )
    ),
    e("div", { className: "settings-section" },
      e("div", { className: "settings-section-head" },
        e("div", null,
          e("div", { className: "settings-section-kicker" }, "COMMAND"),
          e("div", { className: "settings-section-title" }, "슬래시 명령"),
          e("div", { className: "settings-section-sub" }, "/metrics, /kill <pid>, /code ... — Enter 키로 즉시 실행")
        )
      ),
      e("input", {
        className: "input",
        value: command,
        onChange: (event) => setCommand(event.target.value),
        onKeyDown: (event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            send({ type: "command", text: command.trim() });
          }
        },
        placeholder: "/metrics, /kill <pid>, /code ..."
      }),
      e("div", { className: "settings-action-row" },
        e("button", { className: "btn primary", disabled: !authed, onClick: () => send({ type: "command", text: command.trim() }) }, "명령 실행"),
        e("button", { className: "btn", disabled: !authed, onClick: () => send({ type: "get_metrics" }) }, "메트릭 조회"),
        e("button", {
          className: "btn ghost",
          onClick: () => {
            if (workerRef.current) {
              workerRef.current.postMessage({ type: "clear_logs" });
            }
          }
        }, "로그 비우기")
      )
    ),
    e("div", { className: "settings-monitor-grid" },
      e("div", { className: "monitor" },
        e("div", { className: "monitor-title" }, "실시간 메트릭"),
        e("pre", null, metrics || "(메트릭 대기 중)")
      ),
      e("div", { className: "monitor" },
        e("div", { className: "monitor-title" }, "시스템 로그"),
        e("pre", null, logs || "(로그 없음)")
      )
    ),
    e("div", { className: "settings-section" },
      e("div", { className: "settings-section-head" },
        e("div", null,
          e("div", { className: "settings-section-kicker" }, "OPS EVENTS"),
          e("div", { className: "settings-section-title" }, "도메인별 운영 이벤트"),
          e("div", { className: "settings-section-sub" }, "도구 통합 패널 카드와 동일한 분류로 필터링됩니다.")
        )
      ),
      e("div", { className: "settings-chip-filter-row" },
        opsDomainFilters.map((domainItem) => {
          const stat = opsDomainStats[domainItem.key] || { count: 0, errorCount: 0, lastSummary: "-" };
          return e(
            "button",
            {
              key: domainItem.key,
              type: "button",
              className: `btn ${opsDomainFilter === domainItem.key ? "primary" : ""}`,
              onClick: () => applyDomainFocus(domainItem.key)
            },
            `${domainItem.label} (${stat.count})`
          );
        })
      ),
      e("div", { className: "table-wrap" },
        e("table", { className: "settings-table-mini" },
          e("thead", null,
            e("tr", null,
              e("th", null, "시각(UTC)"),
              e("th", null, "도메인"),
              e("th", null, "소스"),
              e("th", null, "상태"),
              e("th", null, "요약")
            )
          ),
          e("tbody", null,
            filteredOpsFlowItems.length === 0
              ? e("tr", null, e("td", { colSpan: 5, style: { textAlign: "center", color: "#7e90a8", padding: "16px 0" } }, "선택한 도메인에 해당하는 운영 이벤트가 없습니다."))
              : filteredOpsFlowItems.slice(0, 12).map((item) => e(
                "tr",
                { key: item.id, className: item.hasError ? "tool-result-row error" : "tool-result-row" },
                e("td", null, item.capturedAt || "-"),
                e("td", null, item.domain || "-"),
                e("td", null, item.source || "-"),
                e("td", null, e("span", { className: `tool-status-chip ${item.statusTone || "neutral"}` }, item.statusLabel || "-")),
                e("td", { style: { whiteSpace: "normal", wordBreak: "break-word", maxWidth: "320px" } }, item.summary || "-")
              ))
          )
        )
      )
    )
  );

  const doctorPanel = renderDoctorPanel({
    e,
    authed,
    doctorState,
    runDoctorReport,
    refreshDoctorReport
  });

  const routingPolicyPanel = renderRoutingPolicyPanel({
    e,
    authed,
    routingPolicyState,
    setRoutingPolicyChain,
    refreshRoutingPolicy,
    saveRoutingPolicy,
    resetRoutingPolicy,
    refreshRoutingDecision
  });

  const contextPanel = renderContextPanel({
    e,
    authed,
    contextState,
    refreshProjectContext,
    refreshSkillsList,
    refreshCommandsList
  });

  const settingsSummary = e("div", { className: "settings-summary" },
    e("div", { className: "settings-summary-head" },
      e("div", null,
        e("h2", null, "설정"),
        e("p", null, "필수 설정은 먼저 보이고, 운영용 고급 도구는 필요할 때만 엽니다.")
      ),
      e("button", {
        className: "btn",
        onClick: () => {
          send({ type: "get_settings" });
          send({ type: "get_copilot_status" });
          send({ type: "get_codex_status" });
          send({ type: "get_usage_stats" });
        }
      }, "상태 조회")
    ),
    e("div", { className: "settings-summary-grid" },
      e("article", { className: "settings-summary-card" },
        e("div", { className: "settings-summary-label" }, "세션"),
        e("strong", null, remoteDashboardClient && authed ? "외부 접속 제한 모드" : authed ? "인증됨" : "OTP 대기"),
        e("span", null, remoteDashboardClient && authed ? "OTP 없이 제한된 화면만 허용" : authExpiry ? `만료 ${authExpiry}` : "OTP 인증 필요"),
        e("div", { className: "settings-chip-row" },
          renderStatusChip(remoteDashboardClient && authed ? "제한 모드" : authed ? "접근 가능" : "미인증", authed)
        )
      ),
      e("article", { className: "settings-summary-card" },
        e("div", { className: "settings-summary-label" }, "연동 키"),
        e("strong", null, `${keyReadyCount} / ${keyReadyItems.length}`),
        e("span", null, "Telegram, Groq, Gemini, Cerebras, NVIDIA, Codex"),
        e("div", { className: "settings-meter" },
          e("span", { style: { width: `${Math.round((keyReadyCount / keyReadyItems.length) * 100)}%` } })
        )
      ),
      e("article", { className: "settings-summary-card" },
        e("div", { className: "settings-summary-label" }, "CLI 인증"),
        e("strong", null, `${cliReadyCount} / 2`),
        e("span", null, "Copilot / Codex"),
        e("div", { className: "settings-chip-row" },
          renderStatusChip("Copilot", copilotReady),
          renderStatusChip("Codex", codexReady)
        )
      ),
      e("article", { className: "settings-summary-card" },
        e("div", { className: "settings-summary-label" }, "최근 사용량"),
        e("strong", null, `${geminiUsage.total_tokens || 0} token`),
        e("span", null, `Gemini 예상 $${geminiUsage.estimated_cost_usd || "0.000000"}`),
        e("div", { className: "settings-chip-row" },
          renderStatusChip(`${copilotLocalUsage.total_requests || 0} Copilot req`, true)
        )
      ),
      e("article", { className: "settings-summary-card" },
        e("div", { className: "settings-summary-label" }, "서버 시간"),
        e("strong", null, authLocalOffset || "UTC+09:00"),
        e("span", null, authExpiry ? `세션 만료 ${authExpiry}` : "로컬 기준"),
        e("div", { className: "settings-chip-row" },
          renderStatusChip("동기화", true)
        )
      )
    )
  );

  const settingsGroups = [
    {
      key: "basic",
      label: "기본 설정",
      summary: "접근과 외부 연동",
      items: [
        remoteDashboardClient ? { key: "basic-remote-limited", label: "외부 제한", summary: "허용/차단 권한", panel: remoteLimitedPanel } : null,
        remoteDashboardClient ? null : { key: "basic-auth", label: "OTP 인증", summary: "대시보드 접근 세션", panel: otpPanel },
        remoteDashboardClient ? null : { key: "basic-external", label: "외부접속", summary: "같은 LAN 접속 허용", panel: externalAccessPanel },
        remoteDashboardClient ? null : { key: "basic-telegram", label: "Telegram", summary: "알림 봇과 채팅 ID", panel: telegramPanel },
        remoteDashboardClient ? null : { key: "basic-keys", label: "LLM 키", summary: "Groq, Gemini, Cerebras, NVIDIA, Codex", panel: llmPanel },
        remoteDashboardClient ? null : { key: "basic-cli", label: "CLI 인증", summary: "Copilot / Codex 로그인", panel: cliAuthPanel },
        uiPreferencePanel
          ? { key: "basic-preferences", label: "화면/단축키", summary: "테마, 키맵, 백업", panel: uiPreferencePanel }
          : null
      ]
        .filter(Boolean)
    },
    {
      key: "models",
      label: "모델/사용량",
      summary: "모델 선택과 비용",
      items: [
        { key: "models-gemini", label: "Gemini 사용량", summary: "토큰과 예상 비용", panel: geminiUsagePanel },
        { key: "models-copilot-usage", label: "Copilot 사용량", summary: "Premium / 로컬 요청", panel: copilotPremiumPanel },
        { key: "models-copilot", label: "Copilot 모델", summary: "모델 선택과 제한", panel: copilotModelsPanel },
        { key: "models-groq", label: "Groq 모델", summary: "모델과 rate limit", panel: groqModelsPanel }
      ]
    },
    {
      key: "context",
      label: "작업 문맥",
      summary: "프로젝트 문맥과 기록",
      items: [
        { key: "context-project", label: "프로젝트 문맥", summary: "스캔된 문맥과 명령", panel: contextPanel }
      ]
    },
    {
      key: "advanced",
      label: "고급 도구",
      summary: "운영과 진단",
      items: [
        { key: "advanced-routing", label: "라우팅 정책", summary: "provider chain override", panel: routingPolicyPanel },
        { key: "advanced-doctor", label: "Doctor", summary: "운영 진단 보고서", panel: doctorPanel },
        { key: "advanced-console", label: "운영 콘솔", summary: "명령, 메트릭, 로그", panel: consolePanel },
        { key: "advanced-tools", label: "도구 통합", summary: "guard와 도구 관측", panel: toolPanel }
      ]
    }
  ];
  const defaultPaneByGroup = Object.fromEntries(settingsGroups.map((group) => [group.key, group.items[0].key]));
  const flatSettingsItems = settingsGroups.flatMap((group) => group.items.map((item) => ({ ...item, groupKey: group.key })));
  const activeItemKey = flatSettingsItems.some((item) => item.key === requestedSettingsPane)
    ? requestedSettingsPane
    : defaultPaneByGroup[requestedSettingsPane] || "basic-auth";
  const activeItem = flatSettingsItems.find((item) => item.key === activeItemKey) || flatSettingsItems[0];
  const activeGroup = settingsGroups.find((group) => group.key === activeItem.groupKey) || settingsGroups[0];
  const detailStack = e("div", { className: "settings-detail-stack" },
    e("div", { className: "settings-detail-head" },
      e("div", null,
        e("h2", null, activeGroup.label),
        e("p", null, activeGroup.summary)
      )
    ),
    e("div", { className: "settings-detail-item" }, activeItem.panel)
  );

  const desktopSettingsShell = e("div", { className: "settings-shell" },
    settingsSummary,
    e("div", { className: "settings-body" },
      e("nav", { className: "settings-side-nav", "aria-label": "설정 섹션" },
        settingsGroups.map((item) => e(
          "div",
          { key: item.key, className: "settings-tree-group" },
          e(
            "button",
            {
              type: "button",
              className: `settings-side-nav-btn ${activeGroup.key === item.key ? "active" : ""}`,
              onClick: () => setResponsivePane("settings", defaultPaneByGroup[item.key])
            },
            e("span", null, item.label),
            e("small", null, item.summary)
          ),
          activeGroup.key === item.key
            ? item.items.map((child) => e(
              "button",
              {
                key: child.key,
                type: "button",
                className: `settings-tree-child ${activeItem.key === child.key ? "active" : ""}`,
                onClick: () => setResponsivePane("settings", child.key)
              },
              child.label
            ))
            : null
        ))
      ),
      detailStack
    )
  );

  const mobileSettingsStack = e("div", { className: "settings-mobile-shell" },
    settingsSummary,
    e("nav", { className: "settings-mobile-nav-block primary", "aria-label": "설정 그룹" },
      e("div", { className: "settings-mobile-nav-label" }, "설정 그룹"),
      renderResponsiveSectionTabs(settingsGroups, activeGroup.key, (paneKey) => setResponsivePane("settings", defaultPaneByGroup[paneKey]), "settings-mobile-tabs")
    ),
    e("nav", { className: "settings-mobile-nav-block secondary", "aria-label": `${activeGroup.label} 항목` },
      e("div", { className: "settings-mobile-nav-label" }, activeGroup.label),
      renderResponsiveSectionTabs(activeGroup.items, activeItem.key, (paneKey) => setResponsivePane("settings", paneKey), "settings-mobile-subtabs")
    ),
    detailStack
  );

  return e(
    "section",
    { className: "settings" },
    isPortraitMobileLayout ? mobileSettingsStack : desktopSettingsShell
  );
}
