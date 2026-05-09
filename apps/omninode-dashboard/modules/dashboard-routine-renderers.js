import {
  DEFAULT_ROUTINE_AGENT_PROVIDER,
  ROUTINE_WEEKDAY_OPTIONS
} from "./dashboard-constants.js";
import {
  DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS,
  MAX_ROUTINE_AGENT_TIMEOUT_SECONDS,
  MIN_ROUTINE_AGENT_TIMEOUT_SECONDS,
  formatRoutineAgentToolProfileLabel,
  formatRoutineExecutionModeLabel,
  formatRoutineSchedulePreview,
  getRoutineAgentModelFallback,
  getRoutineLocalTimezone,
  isRoutineDesktopControlSupportedClient,
  normalizeRoutineAgentToolProfile,
  normalizeRoutineExecutionModeValue,
  normalizeRoutineNotifyPolicy,
  normalizeRoutineNotifyTelegram,
  normalizeRoutineScheduleSourceMode,
  normalizeRoutineWeekdays,
  resolveRoutineVisibleExecutionMode
} from "./routine-utils.js";
import { renderRoutineRunHistoryPanel } from "./dashboard-workspace-renderers.js";
import { computeRoutineStats } from "./routine-stats.js";
import { renderRoutineRetryAndNotifyFields } from "./routine-form-fields.js";
import { renderRoutineProgressPanel as renderRoutineProgressPanelV2 } from "./routine-progress-renderer.js";
import {
  buildDeleteRoutineHandler,
  renderRoutineTestSplitMenu
} from "./routine-defensive-ui.js";
import { createRoutineCreateWizard } from "./routine-create-wizard.js";

// Lazy-instantiate the wizard component once. React is a global script tag
// so we defer creation until first render to ensure React is loaded.
let RoutineCreateWizardCtor = null;
function ensureWizardComponent(e) {
  if (RoutineCreateWizardCtor) return RoutineCreateWizardCtor;
  RoutineCreateWizardCtor = createRoutineCreateWizard({ React: window.React, e });
  return RoutineCreateWizardCtor;
}

// Progress hero rendering moved to ./routine-progress-renderer.js
// Stats moved to ./routine-stats.js
// Shared retry/notify field group moved to ./routine-form-fields.js
// Confirm-delete + split-menu moved to ./routine-defensive-ui.js

function renderRoutineScheduleBuilder(props) {
  const {
    e,
    form,
    formType,
    patchRoutineForm,
    toggleRoutineWeekday
  } = props;

  const scheduleSourceMode = normalizeRoutineScheduleSourceMode(form.scheduleSourceMode, "auto");
  const scheduleKind = form.scheduleKind || "daily";
  return e(
    "div",
    { className: "routine-editor-card routine-schedule-editor" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "routine-editor-title" }, "스케줄"),
      e("div", { className: "routine-editor-subtitle" }, formatRoutineSchedulePreview(form))
    ),
    e("div", { className: "routine-segmented-control routine-source-control" },
      e("button", {
        type: "button",
        className: `routine-segment-btn ${scheduleSourceMode === "auto" ? "active" : ""}`,
        onClick: () => patchRoutineForm(formType, { scheduleSourceMode: "auto" })
      }, "자동(요청 원문)"),
      e("button", {
        type: "button",
        className: `routine-segment-btn ${scheduleSourceMode === "manual" ? "active" : ""}`,
        onClick: () => patchRoutineForm(formType, { scheduleSourceMode: "manual" })
      }, "수동")
    ),
    scheduleSourceMode === "auto"
      ? e("div", { className: "routine-auto-schedule-note" },
        e("strong", null, "요청 원문 우선"),
        e("span", null, "요청에 적은 매일, 요일, 시간 표현을 그대로 사용합니다. 수동으로 바꾸면 아래 스케줄 설정이 요청 원문보다 우선합니다.")
      )
      : e(
        React.Fragment,
        null,
        e("div", { className: "routine-segmented-control" },
          ["daily", "weekly", "monthly"].map((kind) => e("button", {
            key: `${formType}-${kind}`,
            type: "button",
            className: `routine-segment-btn ${scheduleKind === kind ? "active" : ""}`,
            onClick: () => patchRoutineForm(formType, { scheduleKind: kind })
          }, kind === "daily" ? "매일" : kind === "weekly" ? "주간" : "월간"))
        ),
        e("div", { className: "routine-form-grid routine-form-grid-tight" },
          e("label", { className: "routine-field" },
            e("span", { className: "routine-field-label" }, "실행 시간"),
            e("input", {
              className: "input",
              type: "time",
              value: form.scheduleTime || "08:00",
              onChange: (event) => patchRoutineForm(formType, { scheduleTime: event.target.value })
            })
          ),
          e("label", { className: "routine-field" },
            e("span", { className: "routine-field-label" }, "시간대"),
            e("input", {
              className: "input",
              value: form.timezoneId || getRoutineLocalTimezone(),
              onChange: (event) => patchRoutineForm(formType, { timezoneId: event.target.value })
            })
          )
        ),
        scheduleKind === "weekly"
          ? e("div", { className: "routine-weekday-picker" },
            ROUTINE_WEEKDAY_OPTIONS.map((item) => {
              const active = normalizeRoutineWeekdays(form.weekdays || []).includes(item.value);
              return e("button", {
                key: `${formType}-weekday-${item.value}`,
                type: "button",
                className: `routine-weekday-btn ${active ? "active" : ""}`,
                onClick: () => toggleRoutineWeekday(formType, item.value)
              }, item.label);
            })
          )
          : null,
        scheduleKind === "monthly"
          ? e("label", { className: "routine-field" },
            e("span", { className: "routine-field-label" }, "실행 날짜"),
            e("select", {
              className: "input",
              value: `${Math.min(31, Math.max(1, Number(form.dayOfMonth || 1) || 1))}`,
              onChange: (event) => patchRoutineForm(formType, { dayOfMonth: Number(event.target.value) || 1 })
            }, Array.from({ length: 31 }, (_, index) => index + 1).map((value) =>
              e("option", { key: `${formType}-dom-${value}`, value }, `${value}일`)
            ))
          )
          : null
      )
  );
}

function renderRoutineExecutionModeBuilder(props) {
  const {
    e,
    form,
    formType,
    patchRoutineForm,
    routineAgentProviderOptions,
    routineAgentModelOptions
  } = props;

  const visibleMode = resolveRoutineVisibleExecutionMode(form);
  const explicitMode = normalizeRoutineExecutionModeValue(form.executionMode);
  const agentProvider = (form.agentProvider || DEFAULT_ROUTINE_AGENT_PROVIDER).trim().toLowerCase() || DEFAULT_ROUTINE_AGENT_PROVIDER;
  const toolProfile = normalizeRoutineAgentToolProfile(form.agentToolProfile, form.agentUsePlaywright !== false);
  const desktopControlSupported = isRoutineDesktopControlSupportedClient();
  return e(
    "div",
    { className: "routine-editor-card routine-execution-editor" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "routine-editor-title" }, "실행 모드"),
      e("div", { className: "routine-editor-subtitle" }, `${formatRoutineExecutionModeLabel(visibleMode)} · ${explicitMode ? "명시 선택" : "요청 기반 자동 감지"}`)
    ),
    e("div", { className: "routine-segmented-control routine-mode-control" },
      [
        ["", "자동"],
        ["web", "일반 답변"],
        ["url", "URL 참조"],
        ["script", "스크립트"],
        ["browser_agent", "브라우저 에이전트"]
      ].map(([value, label]) => e("button", {
        key: `${formType}-mode-${value}`,
        type: "button",
        className: `routine-segment-btn ${value ? (explicitMode === value ? "active" : "") : (!explicitMode ? "active" : "")}`,
        onClick: () => patchRoutineForm(formType, {
          executionMode: value,
          agentProvider: value === "browser_agent" ? (form.agentProvider || DEFAULT_ROUTINE_AGENT_PROVIDER) : form.agentProvider,
          agentModel: value === "browser_agent"
            ? ((form.agentModel || "").trim() || getRoutineAgentModelFallback(form.agentProvider || DEFAULT_ROUTINE_AGENT_PROVIDER))
            : form.agentModel,
          agentToolProfile: value === "browser_agent"
            ? normalizeRoutineAgentToolProfile(form.agentToolProfile, form.agentUsePlaywright !== false)
            : form.agentToolProfile,
          agentUsePlaywright: value === "browser_agent"
        })
      }, value === "browser_agent"
        ? e(React.Fragment, null, "브라우저", e("br"), "에이전트")
        : label))
    ),
    !form.executionMode
      ? e("div", { className: "routine-auto-schedule-note routine-auto-execution-note" },
        e("strong", null, "자동 감지 중"),
        e("span", null, "URL이 있으면 URL 참조, 최신 정보 질의면 일반 답변, 그 외는 스크립트로 처리합니다. 브라우저 에이전트는 명시 선택일 때만 사용합니다.")
      )
      : null,
    visibleMode === "browser_agent"
      ? e("div", { className: "routine-form-grid routine-form-grid-agent" },
        e("label", { className: "routine-field" },
          e("span", { className: "routine-field-label" }, "에이전트 제공자"),
          e("select", {
            className: "input",
            value: agentProvider,
            onChange: (event) => {
              const nextProvider = event.target.value || DEFAULT_ROUTINE_AGENT_PROVIDER;
              patchRoutineForm(formType, {
                agentProvider: nextProvider,
                agentModel: getRoutineAgentModelFallback(nextProvider)
              });
            }
          }, routineAgentProviderOptions)
        ),
        e("label", { className: "routine-field" },
          e("span", { className: "routine-field-label" }, "에이전트 모델"),
          e("select", {
            className: "input",
            value: (form.agentModel || "").trim() || getRoutineAgentModelFallback(agentProvider),
            onChange: (event) => patchRoutineForm(formType, { agentModel: event.target.value })
          }, routineAgentModelOptions)
        ),
        e("label", { className: "routine-field routine-field-full" },
          e("span", { className: "routine-field-label" }, "시작 URL"),
          e("input", {
            className: "input",
            value: form.agentStartUrl || "",
            onChange: (event) => patchRoutineForm(formType, { agentStartUrl: event.target.value }),
            placeholder: "비워두면 요청 원문에 포함된 첫 URL 사용"
          })
        ),
        e("label", { className: "routine-field" },
          e("span", { className: "routine-field-label" }, "타임아웃(초)"),
          e("input", {
            className: "input",
            type: "number",
            min: MIN_ROUTINE_AGENT_TIMEOUT_SECONDS,
            max: MAX_ROUTINE_AGENT_TIMEOUT_SECONDS,
            value: `${Math.min(
              MAX_ROUTINE_AGENT_TIMEOUT_SECONDS,
              Math.max(
                MIN_ROUTINE_AGENT_TIMEOUT_SECONDS,
                Number(form.agentTimeoutSeconds ?? DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS) || DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS
              )
            )}`,
            onChange: (event) => patchRoutineForm(formType, {
              agentTimeoutSeconds: Number(event.target.value) || DEFAULT_ROUTINE_AGENT_TIMEOUT_SECONDS
            })
          })
        ),
        e("label", { className: "routine-field routine-field-full" },
          e("span", { className: "routine-field-label" }, "도구 프로필"),
          e("select", {
            className: "input",
            value: toolProfile,
            onChange: (event) => patchRoutineForm(formType, {
              agentToolProfile: normalizeRoutineAgentToolProfile(event.target.value, true),
              agentUsePlaywright: true
            })
          },
          e("option", { value: "playwright_only" }, "Playwright 전용"),
          e("option", {
            value: "desktop_control",
            disabled: !desktopControlSupported && toolProfile !== "desktop_control"
          }, "데스크톱 제어"))
        ),
        e("div", { className: "routine-auto-schedule-note routine-agent-note" },
          e("strong", null, formatRoutineAgentToolProfileLabel(toolProfile)),
          toolProfile === "desktop_control"
            ? e("span", null, desktopControlSupported
              ? "Playwright 우선으로 실행하고, 필요할 때만 데스크톱 제어를 추가로 사용합니다. 로그인과 다운로드를 허용합니다."
              : "이 클라이언트에서는 macOS가 아니라서 새로 선택할 수 없습니다. 서버도 macOS가 아니면 실행 시 명확하게 실패합니다.")
            : e("span", null, desktopControlSupported
              ? "브라우저 자동화는 Playwright만 사용합니다. 로그인, 다운로드, 데스크톱 전체 제어는 허용하지 않습니다."
              : "브라우저 자동화는 Playwright만 사용합니다. 데스크톱 제어 프로필은 macOS에서만 새로 선택할 수 있습니다.")
        )
      )
      : null
  );
}

function renderRoutineRunHistory(props) {
  const {
    e,
    routineId,
    runs,
    openRoutineRunDetail,
    resendRoutineRunTelegram
  } = props;

  return renderRoutineRunHistoryPanel({
    e,
    routineId,
    runs,
    openRoutineRunDetail,
    resendRoutineRunTelegram
  });
}

export function renderRoutineTab(props) {
  const {
    e,
    routines,
    routineSelectedId,
    currentRoutinePane,
    isPortraitMobileLayout,
    isRoutineCompactLayout,
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
    renderResponsiveSectionTabs,
    routineDetailSubPane,
    setRoutineDetailSubPane
  } = props;

  const selected = routines.find((item) => item.id === routineSelectedId) || null;
  const selectedRuns = Array.isArray(selected?.runs) ? selected.runs : [];
  const isRoutineCreatePending = !!(routineProgress && routineProgress.active && routineProgress.operation === "create");
  const stats = computeRoutineStats(routines);
  const enabledCount = stats.enabled;
  const browserAgentCount = stats.browserAgent;
  const failedCount = stats.failed;
  const scheduledCount = stats.scheduled;
  const selectedModeLabel = selected
    ? formatRoutineExecutionModeLabel(selected.resolvedExecutionMode || selected.executionMode || "script")
    : "루틴 선택 대기";
  const selectedScheduleSource = selected
    ? (normalizeRoutineScheduleSourceMode(selected.scheduleSourceMode, "manual") === "auto" ? "요청 원문 기준" : "수동 스케줄")
    : "왼쪽 목록에서 선택";
  const selectedToolProfile = selected
    && normalizeRoutineExecutionModeValue(selected.resolvedExecutionMode || selected.executionMode) === "browser_agent"
    ? formatRoutineAgentToolProfileLabel(selected.agentToolProfile)
    : "";
  const selectedHeadline = selected
    ? `${selected.scheduleText || "-"} · ${selected.lastStatus || "실행 전"}`
    : "루틴을 선택하면 실행 상태와 스케줄을 한눈에 확인할 수 있습니다.";
  const selectedRequestPreview = selected && `${selected.request || ""}`.trim()
    ? selected.request
    : "선택된 루틴이 없으면 이 영역에 요청 원문과 최근 상태가 표시됩니다.";
  const selectedNotifyTelegram = normalizeRoutineNotifyTelegram(selected?.notifyTelegram, true);
  const routineMobileSections = [
    { key: "overview", label: "개요" },
    { key: "list", label: "목록" },
    { key: "create", label: "생성" },
    { key: "detail", label: "상세" }
  ];

  const overviewCards = e("div", { className: "routine-overview-grid" },
    e("div", { className: "routine-overview-card routine-overview-card-selected" },
      e("div", { className: "routine-overview-label" }, selected ? "선택된 루틴" : "상세 패널"),
      e("div", { className: "routine-overview-value routine-overview-value-lg" }, selected ? (selected.title || selected.id) : selectedModeLabel),
      e("div", { className: "routine-overview-note" }, `${selectedScheduleSource} · ${selectedModeLabel}${selectedToolProfile ? ` · ${selectedToolProfile}` : ""}`),
      e("div", { className: "routine-overview-note routine-overview-note-strong" }, selectedHeadline)
    ),
    e("div", { className: "routine-overview-card" },
      e("div", { className: "routine-overview-label" }, "전체 루틴"),
      e("div", { className: "routine-overview-value" }, `${routines.length}`),
      e("div", { className: "routine-overview-note" }, "등록된 자동화 작업 수")
    ),
    e("div", { className: "routine-overview-card" },
      e("div", { className: "routine-overview-label" }, "활성 루틴"),
      e("div", { className: "routine-overview-value" }, `${enabledCount}`),
      e("div", { className: "routine-overview-note" }, `비활성 ${Math.max(0, routines.length - enabledCount)}개`)
    ),
    e("div", { className: "routine-overview-card" },
      e("div", { className: "routine-overview-label" }, "예약 대기"),
      e("div", { className: "routine-overview-value" }, `${scheduledCount}`),
      e("div", { className: "routine-overview-note" }, "다음 실행 시간이 잡힌 루틴")
    ),
    e("div", { className: "routine-overview-card" },
      e("div", { className: "routine-overview-label" }, "브라우저 에이전트"),
      e("div", { className: "routine-overview-value" }, `${browserAgentCount}`),
      e("div", { className: "routine-overview-note" }, "브라우저 자동화 루틴")
    ),
    e("div", { className: "routine-overview-card" },
      e("div", { className: "routine-overview-label" }, "최근 오류"),
      e("div", { className: "routine-overview-value" }, `${failedCount}`),
      e("div", { className: "routine-overview-note" }, "마지막 실행 기준 오류/타임아웃")
    ),
    e("button", {
      type: "button",
      className: "routine-overview-card routine-overview-action-card",
      onClick: refreshRoutines
    },
      e("div", { className: "routine-overview-label" }, "새로고침"),
      e("div", { className: "routine-overview-value" }, "동기화"),
      e("div", { className: "routine-overview-note" }, "루틴 상태와 실행 이력 다시 조회")
    )
  );

  const RoutineCreateWizard = ensureWizardComponent(e);
  const createPanel = e(RoutineCreateWizard, {
    routineCreateForm,
    patchRoutineForm,
    toggleRoutineWeekday,
    createRoutineFromUi,
    onInputKeyDown,
    isRoutineCreatePending,
    routineAgentProviderOptions,
    routineAgentModelOptions,
    errorMessage: errorByKey["routine:main"] || "",
    renderRoutineExecutionModeBuilder,
    renderRoutineScheduleBuilder
  });

  const listPanel = e("section", { className: "routine-list-panel routine-library-panel" },
    e("div", { className: "routine-head" },
      e("div", null,
        e("div", { className: "routine-head-kicker" }, "목록"),
        e("h2", null, `${routines.length}개 루틴`)
      ),
      e("div", { className: "routine-library-meta" }, `${enabledCount}개 활성`)
    ),
    e("div", { className: "routine-list" },
      routines.length === 0
        ? e("div", { className: "empty routine-empty-state" }, "등록된 루틴이 없습니다.")
        : routines.map((item) => e(
          "button",
          {
            key: item.id,
            className: `routine-item ${routineSelectedId === item.id ? "active" : ""}`,
            onClick: () => {
              setRoutineSelectedId(item.id);
              if (isPortraitMobileLayout || isRoutineCompactLayout) {
                setResponsivePane("routine", "detail");
              }
            }
          },
          e("div", { className: "routine-item-head" },
            e("div", { className: "routine-item-title" }, item.title || item.id),
            e("span", { className: `meta-chip ${item.enabled ? "ok" : "neutral"}` }, item.enabled ? "ON" : "OFF")
          ),
          e("div", { className: "routine-item-meta" },
            e("span", { className: "meta-chip neutral" }, formatRoutineExecutionModeLabel(item.resolvedExecutionMode || item.executionMode || "script")),
            normalizeRoutineExecutionModeValue(item.resolvedExecutionMode || item.executionMode) === "browser_agent"
              ? e("span", { className: "meta-chip neutral" }, formatRoutineAgentToolProfileLabel(item.agentToolProfile))
              : null,
            e("span", { className: "meta-chip neutral" }, normalizeRoutineScheduleSourceMode(item.scheduleSourceMode, "manual") === "auto" ? "자동" : "수동"),
            e("span", { className: "meta-chip neutral" }, item.scheduleText || "-"),
            e("span", { className: "meta-chip neutral" }, item.lastRunLocal ? `최근 ${item.lastRunLocal}` : "실행 전")
          ),
          e("div", { className: "item-preview" }, item.request || "")
        ))
    )
  );

  const detailPanel = e("section", { className: "routine-detail-panel" },
    !selected
      ? e("div", { className: "routine-section-card routine-empty-card" },
        e("div", { className: "empty routine-empty-state" }, "왼쪽 목록에서 루틴을 선택하면 상세 설정과 실행 이력을 볼 수 있습니다.")
      )
      : e(
        React.Fragment,
        null,
        e("div", { className: "routine-section-card routine-detail-header-card" },
          e("div", { className: "routine-detail-head" },
            e("div", { className: "routine-detail-copy" },
              e("div", { className: "routine-head-kicker" }, "루틴 상세"),
              e("strong", null, selected.title || selected.id),
              e("div", { className: "routine-item-meta" },
                // Active toggle — chip itself is the control (P2: 1-click defensive UX)
                e("button", {
                  type: "button",
                  className: `meta-chip chip-toggle ${selected.enabled ? "ok" : "neutral"}`,
                  onClick: () => setRoutineEnabled(selected.id, !selected.enabled),
                  title: selected.enabled ? "클릭해서 비활성화" : "클릭해서 활성화"
                }, selected.enabled ? "✓ 활성" : "○ 비활성"),
                // Telegram-response toggle — same pattern
                e("button", {
                  type: "button",
                  className: `meta-chip chip-toggle ${selectedNotifyTelegram ? "ok" : "neutral"}`,
                  onClick: () => setRoutineTelegramResponseEnabled(selected.id, !selectedNotifyTelegram),
                  title: selectedNotifyTelegram ? "클릭해서 텔레그램 응답 끄기" : "클릭해서 텔레그램 응답 켜기"
                }, selectedNotifyTelegram ? "텔레그램 응답 ON" : "텔레그램 응답 OFF"),
                // Read-only descriptive chips
                e("span", { className: "meta-chip neutral" }, formatRoutineExecutionModeLabel(selected.resolvedExecutionMode || selected.executionMode || "script")),
                normalizeRoutineExecutionModeValue(selected.resolvedExecutionMode || selected.executionMode) === "browser_agent"
                  ? e("span", { className: "meta-chip neutral" }, formatRoutineAgentToolProfileLabel(selected.agentToolProfile))
                  : null,
                e("span", { className: "meta-chip neutral" }, normalizeRoutineScheduleSourceMode(selected.scheduleSourceMode, "manual") === "auto" ? "자동" : "수동"),
                e("span", { className: "meta-chip neutral" }, selected.scheduleText || "-"),
                e("span", { className: "meta-chip neutral" }, selected.language || "-")
              )
            ),
            e("div", { className: "routine-action-row" },
              // Test split-menu — primary "웹 테스트" + chevron with browser/telegram in popover
              renderRoutineTestSplitMenu({
                e,
                primaryLabel: "웹 테스트",
                primaryAction: () => runRoutineNow(selected.id),
                secondaryItems: [
                  (selected.resolvedExecutionMode || selected.executionMode) === "browser_agent"
                    ? { key: "browser", label: "브라우저 에이전트 테스트", action: () => testRoutineBrowserAgent(selected.id) }
                    : null,
                  { key: "telegram", label: "텔레그램 테스트", action: () => testRoutineTelegram(selected.id) }
                ].filter(Boolean)
              }),
              // Defensive delete — confirm + auto-deselect (P2)
              e("button", {
                className: "btn danger",
                onClick: buildDeleteRoutineHandler({
                  routine: selected,
                  deleteRoutineById,
                  afterDelete: () => setRoutineSelectedId("")
                })
              }, "삭제")
            )
          ),
          e("div", { className: "routine-request-preview" }, selectedRequestPreview)
        ),
        e("div", { className: "routine-stats-grid" },
          e("div", { className: "routine-stat-card" },
            e("span", { className: "routine-stat-label" }, "다음 실행"),
            e("strong", null, selected.nextRunLocal || "-")
          ),
          e("div", { className: "routine-stat-card" },
            e("span", { className: "routine-stat-label" }, "마지막 실행"),
            e("strong", null, selected.lastRunLocal || "-")
          ),
          e("div", { className: "routine-stat-card" },
            e("span", { className: "routine-stat-label" }, "상태"),
            e("strong", null, selected.lastStatus || "-")
          ),
          e("div", { className: "routine-stat-card" },
            e("span", { className: "routine-stat-label" }, "생성 모델"),
            e("strong", null, selected.coderModel || "-")
          ),
          e("div", { className: "routine-stat-card" },
            e("span", { className: "routine-stat-label" }, "실행 모드"),
            e("strong", null, formatRoutineExecutionModeLabel(selected.resolvedExecutionMode || selected.executionMode || "script"))
          ),
          normalizeRoutineExecutionModeValue(selected.resolvedExecutionMode || selected.executionMode) === "browser_agent"
            ? e("div", { className: "routine-stat-card" },
              e("span", { className: "routine-stat-label" }, "도구 프로필"),
              e("strong", null, formatRoutineAgentToolProfileLabel(selected.agentToolProfile))
            )
            : null,
          e("div", { className: "routine-stat-card" },
            e("span", { className: "routine-stat-label" }, "텔레그램 응답"),
            e("strong", null, selectedNotifyTelegram ? "켜짐" : "꺼짐")
          ),
          e("div", { className: "routine-stat-card" },
            e("span", { className: "routine-stat-label" }, "알림 정책"),
            e("strong", null, normalizeRoutineNotifyPolicy(selected.notifyPolicy, "always"))
          )
        ),
        e("div", { className: "routine-detail-grid" },
          e("div", { className: "routine-primary-column" },
            e("div", { className: "routine-section-card routine-edit-card" },
              e("div", { className: "routine-editor-section-head" },
                e("div", { className: "routine-editor-title" }, "루틴 수정"),
                e("button", { className: "btn primary routine-submit-btn", onClick: updateRoutineFromUi }, "루틴 수정 저장")
              ),
              e("div", { className: "routine-form-grid routine-form-grid-primary" },
                e("label", { className: "routine-field" },
                  e("span", { className: "routine-field-label" }, "루틴 이름"),
                  e("input", {
                    className: "input",
                    value: routineEditForm.title,
                    onChange: (event) => patchRoutineForm("edit", { title: event.target.value })
                  })
                ),
                e("label", { className: "routine-field routine-field-full" },
                  e("span", { className: "routine-field-label" }, "요청 원문"),
                  e("textarea", {
                    className: "textarea routine-input routine-input-compact",
                    value: routineEditForm.request,
                    onChange: (event) => patchRoutineForm("edit", { request: event.target.value }),
                    onKeyDown: (event) => onInputKeyDown(event, updateRoutineFromUi)
                  })
                )
              ),
              e("div", { className: "routine-execution-config-stack" },
                renderRoutineExecutionModeBuilder({
                  e,
                  form: routineEditForm,
                  formType: "edit",
                  patchRoutineForm,
                  routineAgentProviderOptions,
                  routineAgentModelOptions
                }),
                renderRoutineRetryAndNotifyFields({
                  e,
                  form: routineEditForm,
                  formType: "edit",
                  patchRoutineForm
                })
              ),
              renderRoutineScheduleBuilder({
                e,
                form: routineEditForm,
                formType: "edit",
                patchRoutineForm,
                toggleRoutineWeekday
              })
            )
          ),
          e("div", { className: "routine-secondary-column" },
            e("div", { className: "routine-detail-tabs" },
              e("button", {
                className: `routine-detail-tab ${routineDetailSubPane !== "output" ? "active" : ""}`,
                onClick: () => setRoutineDetailSubPane("history")
              }, "실행 이력"),
              e("button", {
                className: `routine-detail-tab ${routineDetailSubPane === "output" ? "active" : ""}`,
                onClick: () => setRoutineDetailSubPane("output")
              }, "최근 실행 출력")
            ),
            routineDetailSubPane !== "output"
              ? e("div", { className: "routine-section-card routine-detail-tab-panel" },
                  e("div", { className: "routine-section-head" },
                    e("div", { className: "routine-editor-title" }, `${selectedRuns.length}건`)
                  ),
                  renderRoutineRunHistory({
                    e,
                    routineId: selected.id,
                    runs: selectedRuns,
                    openRoutineRunDetail,
                    resendRoutineRunTelegram
                  })
                )
              : e("div", { className: "routine-section-card routine-detail-tab-panel" },
                  e("div", { className: "routine-section-head" },
                    e("div", { className: "routine-editor-title" }, selected.lastStatus || "-")
                  ),
                  e("div", { className: "routine-kv" },
                    e("div", null, `ID: ${selected.id}`),
                    e("div", null, `실행 모드: ${formatRoutineExecutionModeLabel(selected.resolvedExecutionMode || selected.executionMode || "script")}`),
                    e("div", null, `언어: ${selected.language || "-"}`),
                    e("div", null, `시간대: ${selected.timezoneId || "-"}`),
                    e("div", null, `재시도: ${Math.max(0, Number(selected.maxRetries || 0))}회 / ${Math.max(0, Number(selected.retryDelaySeconds || 0))}초`),
                    e("div", null, `텔레그램 응답: ${selectedNotifyTelegram ? "켜짐" : "꺼짐"}`),
                    e("div", null, `알림: ${normalizeRoutineNotifyPolicy(selected.notifyPolicy, "always")}`),
                    e("div", null, `에이전트: ${(selected.agentProvider || "-")} / ${(selected.agentModel || "-")}`),
                    e("div", null, `시작 URL: ${selected.agentStartUrl || "-"}`),
                    e("div", null, `스크립트: ${selected.scriptPath || "-"}`)
                  ),
                  e("button", {
                    type: "button",
                    className: "routine-output-button",
                    onClick: () => setRoutineOutputPreview({
                      open: true,
                      title: `${selected.title || selected.id} · 최근 실행 출력`,
                      content: selected.lastOutput || "출력 없음",
                      imagePath: "",
                      imageAlt: ""
                    })
                  },
                    e("pre", { className: "routine-output" }, selected.lastOutput || "출력 없음")
                  )
                )
          )
        )
      )
  );

  return e(
    "section",
    { className: "routine-tab" },
    e("div", { className: "routine-hero" },
      renderRoutineProgressPanelV2(e, routineProgress)
    ),
    (isPortraitMobileLayout || isRoutineCompactLayout)
      ? e(
        "div",
        { className: "routine-mobile-shell" },
        renderResponsiveSectionTabs(routineMobileSections, currentRoutinePane, (paneKey) => setResponsivePane("routine", paneKey), "routine-mobile-tabs"),
        currentRoutinePane === "overview" ? overviewCards : null,
        currentRoutinePane === "list" ? listPanel : null,
        currentRoutinePane === "create" ? createPanel : null,
        currentRoutinePane === "detail" ? detailPanel : null
      )
      : e(
        React.Fragment,
        null,
        overviewCards,
        e("div", { className: "routine-layout" },
          e("div", { className: "routine-left-column" },
            e("div", { className: "routine-left-tabs" },
              e("button", {
                className: `routine-left-tab ${currentRoutinePane === "list" ? "active" : ""}`,
                onClick: () => setResponsivePane("routine", "list")
              }, "목록"),
              e("button", {
                className: `routine-left-tab ${currentRoutinePane === "create" ? "active" : ""}`,
                onClick: () => setResponsivePane("routine", "create")
              }, "새 루틴")
            ),
            currentRoutinePane === "create" ? createPanel : listPanel
          ),
          detailPanel
        )
      )
  );
}
