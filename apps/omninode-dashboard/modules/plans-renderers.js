function formatPlanTimestamp(value) {
  const raw = `${value || ""}`.trim();
  if (!raw) {
    return "-";
  }

  const parsed = new Date(raw);
  if (Number.isNaN(parsed.getTime())) {
    return raw;
  }

  return parsed.toLocaleString("ko-KR", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false
  });
}

function formatPlanRelative(value) {
  const raw = `${value || ""}`.trim();
  if (!raw) {
    return "기록 없음";
  }

  const diffMs = Date.now() - new Date(raw).getTime();
  if (!Number.isFinite(diffMs) || diffMs < 0) {
    return formatPlanTimestamp(raw);
  }

  const minutes = Math.floor(diffMs / 60000);
  if (minutes < 1) {
    return "방금 전";
  }

  if (minutes < 60) {
    return `${minutes}분 전`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours}시간 전`;
  }

  const days = Math.floor(hours / 24);
  return `${days}일 전`;
}

function resolvePlanTone(status) {
  const normalized = `${status || ""}`.toLowerCase();
  if (normalized === "approved" || normalized === "completed") {
    return "ok";
  }
  if (normalized === "reviewpending" || normalized === "running") {
    return "warn";
  }
  if (normalized === "rejected" || normalized === "abandoned") {
    return "error";
  }
  return "neutral";
}

function normalizeStatusLabel(status) {
  const normalized = `${status || ""}`.trim();
  if (!normalized) {
    return "-";
  }

  return normalized;
}

function trimPlanText(value, maxChars = 220) {
  const normalized = `${value || ""}`.trim();
  if (normalized.length <= maxChars) {
    return normalized;
  }

  return `${normalized.slice(0, maxChars)}...`;
}

function renderStringList(e, className, items) {
  const normalizedItems = Array.isArray(items) ? items.filter(Boolean) : [];
  if (normalizedItems.length === 0) {
    return e("div", { className: "tiny" }, "없음");
  }

  return e("ul", { className },
    normalizedItems.map((item, index) => e("li", { key: `${className}-${index}` }, item))
  );
}

function renderPlanMetricCard(e, label, value, helper, tone = "") {
  return e("article", { className: `plan-metric-card ${tone}`.trim() },
    e("span", { className: "plan-metric-label" }, label),
    e("strong", { className: "plan-metric-value" }, value),
    e("p", { className: "plan-metric-helper" }, helper)
  );
}

function normalizeChainValue(routingPolicyState, categoryKey) {
  const draft = routingPolicyState?.draftChains?.[categoryKey];
  if (typeof draft === "string" && draft.trim()) {
    return draft.trim();
  }

  const effective = routingPolicyState?.snapshot?.effectiveChains?.[categoryKey];
  if (Array.isArray(effective) && effective.length > 0) {
    return effective.join(", ");
  }

  return "";
}

function parseChainProviders(value) {
  return `${value || ""}`
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}

function resolvePrimaryProvider(routingPolicyState, categoryKey, fallback = "groq") {
  const providers = parseChainProviders(normalizeChainValue(routingPolicyState, categoryKey));
  return providers[0] || fallback;
}

function reorderProviderChain(chain, provider) {
  const ordered = [provider].concat((Array.isArray(chain) ? chain : []).filter((item) => item !== provider));
  return ordered.join(", ");
}

function resolveProviderModelLabel(options) {
  const {
    provider,
    selectedGroqModel,
    selectedCopilotModel,
    defaultCodexModel,
    geminiModelChoices,
    cerebrasModelChoices
  } = options;

  if (provider === "groq") {
    return selectedGroqModel || "-";
  }

  if (provider === "copilot") {
    return selectedCopilotModel || "gpt-5-mini";
  }

  if (provider === "gemini") {
    return geminiModelChoices?.[0]?.id || "-";
  }

  if (provider === "cerebras") {
    return cerebrasModelChoices?.[0]?.id || "-";
  }

  if (provider === "codex") {
    return defaultCodexModel || "-";
  }

  return "-";
}

function renderAutomationRouteEditor(e, options) {
  const {
    title,
    description,
    categoryKey,
    routingPolicyState,
    setRoutingPolicyChain,
    selectedGroqModel,
    setSelectedGroqModel,
    groqModels,
    selectedCopilotModel,
    setSelectedCopilotModel,
    copilotModels,
    defaultCodexModel,
    geminiModelChoices,
    cerebrasModelChoices,
    send,
    disabled
  } = options;

  const provider = resolvePrimaryProvider(routingPolicyState, categoryKey);
  const chainValue = normalizeChainValue(routingPolicyState, categoryKey);
  const modelLabel = resolveProviderModelLabel({
    provider,
    selectedGroqModel,
    selectedCopilotModel,
    defaultCodexModel,
    geminiModelChoices,
    cerebrasModelChoices
  });
  const providerOptions = [
    { value: "groq", label: "Groq" },
    { value: "gemini", label: "Gemini" },
    { value: "cerebras", label: "Cerebras" },
    { value: "codex", label: "Codex" },
    { value: "copilot", label: "Copilot" }
  ];
  const fixedModelText = provider === "gemini"
    ? `Gemini 기본 ${geminiModelChoices?.[0]?.label || modelLabel}`
    : provider === "cerebras"
      ? `Cerebras 기본 ${cerebrasModelChoices?.[0]?.label || modelLabel}`
      : `Codex 기본 ${defaultCodexModel || modelLabel}`;
  const modelControl = provider === "groq"
    ? e("div", { className: "automation-model-control" },
      e("select", {
        className: "input",
        value: selectedGroqModel,
        onChange: (event) => setSelectedGroqModel(event.target.value)
      },
      (groqModels?.length || 0) === 0
        ? e("option", { value: "" }, "Groq 모델 로딩 중")
        : groqModels.map((item) => e("option", { key: `groq-${categoryKey}-${item.id}`, value: item.id }, item.id))),
      e("button", {
        type: "button",
        className: "btn",
        disabled: disabled || !selectedGroqModel,
        onClick: () => send({ type: "set_groq_model", model: selectedGroqModel })
      }, "적용")
    )
    : provider === "copilot"
      ? e("div", { className: "automation-model-control" },
        e("select", {
          className: "input",
          value: selectedCopilotModel,
          onChange: (event) => setSelectedCopilotModel(event.target.value)
        },
        (copilotModels?.length || 0) === 0
          ? e("option", { value: "" }, "Copilot 모델 로딩 중")
          : copilotModels.map((item) => e("option", { key: `copilot-${categoryKey}-${item.id}`, value: item.id }, item.id))),
        e("button", {
          type: "button",
          className: "btn",
          disabled: disabled || !selectedCopilotModel,
          onClick: () => send({ type: "set_copilot_model", model: selectedCopilotModel })
        }, "적용")
      )
      : e("div", { className: "automation-model-static", title: fixedModelText }, modelLabel);

  return e("article", { className: "automation-route-compact" },
    e("div", { className: "automation-route-name" },
      e("strong", null, title),
      e("span", null, description || categoryKey)
    ),
    e("label", { className: "meta-field automation-provider-field" },
      e("span", { className: "meta-label" }, "제공자"),
      e("select", {
        className: "input",
        value: provider,
        onChange: (event) => setRoutingPolicyChain(categoryKey, reorderProviderChain(parseChainProviders(chainValue), event.target.value))
      },
      providerOptions.map((item) => e("option", { key: `${categoryKey}-${item.value}`, value: item.value }, item.label)))
    ),
    e("label", { className: "meta-field automation-model-field" },
      e("span", { className: "meta-label" }, "모델"),
      modelControl
    ),
    e("details", { className: "automation-chain-details" },
      e("summary", null,
        e("span", null, "대체 순서"),
        e("strong", null, chainValue || "비어 있음")
      ),
      e("label", { className: "meta-field automation-chain-field" },
        e("span", { className: "meta-label" }, "대체 순서"),
        e("input", {
          className: "input",
          value: chainValue,
          placeholder: "groq, gemini, cerebras, codex, copilot",
          onChange: (event) => setRoutingPolicyChain(categoryKey, event.target.value)
        })
      )
    )
  );
}

function buildPlanChecklist(plan, review, execution, items) {
  const list = [];
  if ((items?.length || 0) === 0) {
    list.push({
      key: "create-first",
      title: "첫 계획부터 만들어야 합니다.",
      description: "요청과 제약사항을 먼저 적으면 이후 승인과 실행 흐름이 안정됩니다.",
      action: "draft"
    });
  }

  if (!plan) {
    list.push({
      key: "select",
      title: "저장된 계획을 하나 선택해 상세를 읽으세요.",
      description: "리뷰와 실행 전에는 목표, 제약사항, 단계 구성이 먼저 보여야 합니다.",
      action: "browse"
    });
    return list;
  }

  if (!review) {
    list.push({
      key: "review",
      title: "리뷰를 먼저 돌리세요.",
      description: "실행 전에 findings, risks, verification gaps를 확인해야 합니다.",
      action: "review"
    });
  }

  if (review && `${plan.status || ""}`.toLowerCase() !== "approved") {
    list.push({
      key: "approve",
      title: "리뷰 확인 후 승인 상태를 명확히 하세요.",
      description: "승인 여부가 남아 있으면 이후 실행 기준이 흔들립니다.",
      action: "approve"
    });
  }

  if (`${plan.status || ""}`.toLowerCase() === "approved" && !execution) {
    list.push({
      key: "run",
      title: "승인된 계획은 실행 단계로 넘길 수 있습니다.",
      description: "실행 전 단계 수와 검증 포인트를 다시 확인한 뒤 시작하세요.",
      action: "run"
    });
  }

  if (list.length === 0) {
    list.push({
      key: "fresh",
      title: "현재 계획 흐름이 갖춰져 있습니다.",
      description: "새 변경이 생기면 제약사항과 decision log를 먼저 갱신하는 편이 낫습니다.",
      action: "browse"
    });
  }

  return list;
}

export function renderPlansPanel(props) {
  const {
    e,
    authed,
    plansState,
    setPlanCreateObjective,
    setPlanCreateConstraintsText,
    setPlanCreateMode,
    refreshPlansList,
    loadPlanSnapshot,
    submitPlanCreate,
    reviewPlan,
    approvePlan,
    runPlan,
    routingPolicyState,
    setRoutingPolicyChain,
    refreshRoutingPolicy,
    saveRoutingPolicy,
    selectedGroqModel,
    setSelectedGroqModel,
    groqModels,
    selectedCopilotModel,
    setSelectedCopilotModel,
    copilotModels,
    defaultCodexModel,
    geminiModelChoices,
    cerebrasModelChoices,
    send
  } = props;

  const items = Array.isArray(plansState.items) ? plansState.items : [];
  const snapshot = plansState.snapshot || null;
  const plan = snapshot?.plan || null;
  const review = snapshot?.review || null;
  const execution = snapshot?.execution || null;
  const disabled = !authed || plansState.pending || plansState.loading;
  const approvedCount = items.filter((item) => `${item.status || ""}`.toLowerCase() === "approved").length;
  const runningCount = items.filter((item) => `${item.status || ""}`.toLowerCase() === "running").length;
  const activeMode = plansState.createMode || "fast";
  const checklist = buildPlanChecklist(plan, review, execution, items);
  const planStepsContent = plan && Array.isArray(plan.steps) && plan.steps.length > 0
    ? plan.steps.map((step, index) => e("article", { key: step.stepId || `step-${index}`, className: "plan-step-card" },
      e("div", { className: "plan-step-head" },
        e("strong", null, `${index + 1}. ${step.title || step.stepId || "-"}`),
        e("span", { className: "tiny" }, step.stepId || "-")
      ),
      e("div", { className: "plan-objective-text" }, step.description || "-"),
      e("div", { className: "plan-step-grid" },
        e("div", null,
          e("div", { className: "tiny" }, "must do"),
          renderStringList(e, "doctor-action-list", step.mustDo)
        ),
        e("div", null,
          e("div", { className: "tiny" }, "must not do"),
          renderStringList(e, "doctor-action-list", step.mustNotDo)
        ),
        e("div", null,
          e("div", { className: "tiny" }, "verification"),
          renderStringList(e, "doctor-action-list", step.verification)
        )
      )
    ))
    : e("div", { className: "empty plan-empty-state" }, "단계 정보가 없습니다.");

  const applyTemplate = (kind) => {
    if (kind === "feature") {
      setPlanCreateObjective("예: 기존 UI/UX를 유지하면서 특정 기능 화면을 재구성하고 반응형 정렬 문제를 해결");
      setPlanCreateConstraintsText([
        "사용자가 요청한 범위 외 변경 금지",
        "기존 기능 유지",
        "반응형 웹 기준으로 레이아웃 정렬",
        "테스트는 사용자가 직접 수행"
      ].join("\n"));
      setPlanCreateMode("fast");
      return;
    }

    if (kind === "bugfix") {
      setPlanCreateObjective("예: 재현 가능한 UI 깨짐 또는 동작 오류를 수정하고 회귀 포인트를 정리");
      setPlanCreateConstraintsText([
        "문제 재현 범위 외 구조 변경 금지",
        "기존 동작 회귀 방지 포인트 명시",
        "필요 최소 수정만 적용"
      ].join("\n"));
      setPlanCreateMode("fast");
      return;
    }

    if (kind === "interview") {
      setPlanCreateObjective("예: 작업 착수 전에 빠진 요구사항과 리스크를 먼저 인터뷰 기반으로 정리");
      setPlanCreateConstraintsText([
        "확인되지 않은 요구사항은 추정하지 않음",
        "리스크와 가정 먼저 정리",
        "승인 전 구현 범위 확장 금지"
      ].join("\n"));
      setPlanCreateMode("interview");
    }
  };

  return e("section", { className: "panel settings-optimized-panel settings-plans-panel plans-panel plans-workbench-panel" },
    e("div", { className: "plans-panel-head plans-panel-head-ux" },
      e("div", null,
        e("h2", null, "계획"),
        e("p", { className: "hint" }, "요청을 바로 실행하지 않고, 계획 작성부터 리뷰와 승인까지 단계별로 나눠 관리합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", { type: "button", className: "btn", disabled, onClick: refreshPlansList }, plansState.loading ? "불러오는 중..." : "목록 새로고침"),
        e("button", { type: "button", className: "btn primary", disabled, onClick: submitPlanCreate }, plansState.pending ? "처리 중..." : "계획 생성")
      )
    ),
    plansState.lastError
      ? e("div", { className: "error-banner" }, plansState.lastError)
      : null,
    e("div", { className: "plans-feedback-bar" },
      e("span", { className: `tool-status-chip ${authed ? "ok" : "neutral"}` }, authed ? "세션 인증됨" : "인증 필요"),
      e("span", { className: `tool-status-chip ${plansState.pending ? "warn" : plansState.loading ? "neutral" : "ok"}` }, plansState.pending ? "처리 중" : plansState.loading ? "동기화 중" : "준비됨"),
      e("span", { className: "plans-feedback-text" },
        plan
          ? `${plan.title || plan.planId || "-"} · ${normalizeStatusLabel(plan.status)} · ${formatPlanRelative(plan.updatedAtUtc)}`
          : items.length > 0
            ? `${items.length}개의 저장된 계획이 있습니다.`
            : "저장된 계획이 없습니다."
      )
    ),
    e("div", { className: "plan-metric-grid" },
      renderPlanMetricCard(e, "저장된 계획", `${items.length}건`, "목록에서 선택해 상세와 리뷰 상태 확인"),
      renderPlanMetricCard(e, "승인 완료", `${approvedCount}건`, "즉시 실행 후보가 되는 계획 수"),
      renderPlanMetricCard(e, "진행 중", `${runningCount}건`, "실행 상태가 살아 있는 계획 수"),
      renderPlanMetricCard(e, "생성 모드", activeMode, activeMode === "interview" ? "요구사항 확인 중심" : "빠른 초안 생성", activeMode === "interview" ? "neutral" : "")
    ),
    e("div", { className: "plans-workbench-shell" },
      e("div", { className: "plans-primary-column" },
        e("section", { className: "plans-create-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "계획 초안 작성"),
              e("p", null, "요청, 제약, 생성 모드를 먼저 정리한 뒤 계획을 만듭니다.")
            )
          ),
          e("label", { className: "meta-field plan-field-wide" },
            e("span", { className: "meta-label" }, "요청"),
            e("textarea", {
              className: "input plan-textarea plan-workbench-textarea",
              value: plansState.createObjective,
              rows: 6,
              placeholder: "예: 특정 화면 UI/UX 개선, 기존 기능 유지, 반응형 웹 정렬 문제 수정",
              onChange: (event) => setPlanCreateObjective(event.target.value)
            })
          ),
          e("div", { className: "plan-mode-grid" },
            [
              { key: "fast", label: "빠른 초안", helper: "지금 바로 계획을 생성" },
              { key: "interview", label: "인터뷰 모드", helper: "질문과 리스크부터 정리" }
            ].map((mode) => e("button", {
              key: mode.key,
              type: "button",
              className: `plan-mode-card ${activeMode === mode.key ? "active" : ""}`,
              onClick: () => setPlanCreateMode(mode.key)
            },
            e("strong", null, mode.label),
            e("span", null, mode.helper)))
          ),
          e("div", { className: "plan-template-strip" },
            e("span", { className: "plan-template-label" }, "빠른 템플릿"),
            e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("feature") }, "기능 개선"),
            e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("bugfix") }, "버그 수정"),
            e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("interview") }, "요구사항 점검"),
            e("button", { type: "button", className: "btn ghost", onClick: () => setPlanCreateConstraintsText("") }, "제약 비우기")
          ),
          e("label", { className: "meta-field plan-field-wide" },
            e("span", { className: "meta-label" }, "제약사항"),
            e("textarea", {
              className: "input plan-textarea plan-constraints plan-workbench-constraints",
              value: plansState.createConstraintsText,
              rows: 5,
              placeholder: "줄바꿈 단위로 입력합니다. 예: 기존 기능 유지 / 요청 범위 외 변경 금지 / 반응형 정렬 유지",
              onChange: (event) => setPlanCreateConstraintsText(event.target.value)
            })
          ),
          e("div", { className: "plan-compose-actions" },
            e("button", { type: "button", className: "btn", disabled, onClick: refreshPlansList }, "목록 새로고침"),
            e("button", { type: "button", className: "btn primary", disabled, onClick: submitPlanCreate }, plansState.pending ? "생성 중..." : "계획 생성")
          )
        ),
        e("section", { className: "automation-llm-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "계획용 LLM"),
              e("p", null, "계획 생성과 리뷰는 라우팅 체인과 provider별 모델 상태를 같이 봐야 합니다.")
            ),
            e("div", { className: "automation-llm-actions" },
              e("button", { type: "button", className: "btn", disabled, onClick: refreshRoutingPolicy }, "라우팅 새로고침"),
              e("button", { type: "button", className: "btn primary", disabled, onClick: saveRoutingPolicy }, routingPolicyState?.pending ? "저장 중..." : "라우팅 저장")
            )
          ),
          e("div", { className: "automation-route-list" },
            renderAutomationRouteEditor(e, {
              title: "계획 생성",
              description: "planner",
              categoryKey: "planner",
              routingPolicyState,
              setRoutingPolicyChain,
              selectedGroqModel,
              setSelectedGroqModel,
              groqModels,
              selectedCopilotModel,
              setSelectedCopilotModel,
              copilotModels,
              defaultCodexModel,
              geminiModelChoices,
              cerebrasModelChoices,
              send,
              disabled
            }),
            renderAutomationRouteEditor(e, {
              title: "계획 리뷰",
              description: "reviewer",
              categoryKey: "reviewer",
              routingPolicyState,
              setRoutingPolicyChain,
              selectedGroqModel,
              setSelectedGroqModel,
              groqModels,
              selectedCopilotModel,
              setSelectedCopilotModel,
              copilotModels,
              defaultCodexModel,
              geminiModelChoices,
              cerebrasModelChoices,
              send,
              disabled
            })
          )
        ),
        e("section", { className: "plan-next-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "다음 액션"),
              e("p", null, "현재 상태에서 먼저 해야 할 작업만 좁혀서 보여줍니다.")
            )
          ),
          e("div", { className: "plan-next-list" },
            checklist.map((item) => e("article", { key: item.key, className: "plan-next-item" },
              e("div", null,
                e("strong", null, item.title),
                e("p", null, item.description)
              ),
              item.action === "review" && plan
                ? e("button", { type: "button", className: "btn ghost", disabled, onClick: () => reviewPlan(plan.planId) }, "리뷰")
                : item.action === "approve" && plan
                  ? e("button", { type: "button", className: "btn ghost", disabled, onClick: () => approvePlan(plan.planId) }, "승인")
                  : item.action === "run" && plan
                    ? e("button", { type: "button", className: "btn ghost", disabled, onClick: () => runPlan(plan.planId) }, "실행")
                    : e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("feature") }, "초안 채우기")
            ))
          )
        )
      ),
      e("div", { className: "plans-browser-shell" },
        e("section", { className: "plans-list-column plan-browser-list" },
          e("div", { className: "plans-section-head" },
            e("strong", null, "저장된 계획"),
            e("span", { className: "tiny" }, `${items.length}건`)
          ),
          items.length === 0
            ? e("div", { className: "empty plan-empty-state" }, "저장된 계획이 없습니다.")
            : e("div", { className: "plans-list" },
              items.map((item) => {
                const selected = item.planId === plansState.selectedPlanId;
                return e("button", {
                  key: item.planId,
                  type: "button",
                  className: `plan-list-item ${selected ? "active" : ""}`,
                  onClick: () => loadPlanSnapshot(item.planId)
                },
                e("div", { className: "plan-list-item-head" },
                  e("strong", null, item.title || item.planId),
                  e("span", { className: `tool-status-chip ${resolvePlanTone(item.status)}` }, normalizeStatusLabel(item.status))
                ),
                e("div", { className: "tiny" }, item.planId || "-"),
                e("div", { className: "plan-list-item-objective" }, trimPlanText(item.objective || "-", 120)),
                e("div", { className: "tiny" }, `updated ${formatPlanTimestamp(item.updatedAtUtc)}`));
              }))
        ),
        e("section", { className: "plans-detail-column plan-browser-detail" },
          !plan
            ? e("div", { className: "empty plan-empty-state" }, "왼쪽 목록에서 계획을 선택하세요.")
            : e("div", { className: "plan-detail" },
              e("div", { className: "plan-detail-head" },
                e("div", null,
                  e("div", { className: "tiny" }, plan.planId || "-"),
                  e("h3", null, plan.title || "제목 없음"),
                  e("div", { className: "tiny" }, `updated ${formatPlanTimestamp(plan.updatedAtUtc)}`)
                ),
                e("div", { className: "row plan-detail-actions" },
                  e("button", { type: "button", className: "btn", disabled, onClick: () => loadPlanSnapshot(plan.planId) }, "다시 읽기"),
                  e("button", { type: "button", className: "btn", disabled, onClick: () => reviewPlan(plan.planId) }, "리뷰"),
                  e("button", { type: "button", className: "btn", disabled, onClick: () => approvePlan(plan.planId) }, "승인"),
                  e("button", { type: "button", className: "btn primary", disabled, onClick: () => runPlan(plan.planId) }, "실행")
                )
              ),
              e("div", { className: "plan-summary-grid" },
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "status"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, normalizeStatusLabel(plan.status))
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "steps"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, String(Array.isArray(plan.steps) ? plan.steps.length : 0))
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "review"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, review ? "있음" : "없음")
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "execution"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, execution?.status || "-")
                )
              ),
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "목표")),
                e("div", { className: "plan-objective-text" }, plan.objective || "-")
              ),
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "제약사항")),
                renderStringList(e, "doctor-action-list", plan.constraints)
              ),
              review
                ? e("article", { className: "plan-detail-card plan-review-card" },
                  e("div", { className: "plans-section-head" },
                    e("strong", null, "리뷰 결과"),
                    e("span", { className: "tiny" }, `${formatPlanTimestamp(review.reviewedAtUtc)} · ${review.reviewerRoute || "-"}`)
                  ),
                  e("div", { className: "plan-review-summary" }, review.summary || "-"),
                  e("div", { className: "plan-review-grid" },
                    e("div", { className: "plan-review-box" },
                      e("strong", null, "findings"),
                      renderStringList(e, "doctor-action-list", review.findings)
                    ),
                    e("div", { className: "plan-review-box" },
                      e("strong", null, "risks"),
                      renderStringList(e, "doctor-action-list", review.risks)
                    ),
                    e("div", { className: "plan-review-box" },
                      e("strong", null, "verification gaps"),
                      renderStringList(e, "doctor-action-list", review.missingVerification)
                    )
                  )
                )
                : null,
              execution
                ? e("article", { className: "plan-detail-card" },
                  e("div", { className: "plans-section-head" },
                    e("strong", null, "실행 결과"),
                    e("span", { className: `tool-status-chip ${resolvePlanTone(execution.status)}` }, execution.status || "-")
                  ),
                  e("div", { className: "tiny" }, `requested ${formatPlanTimestamp(execution.requestedAtUtc)}`),
                  e("div", { className: "tiny" }, `completed ${formatPlanTimestamp(execution.completedAtUtc)}`),
                  e("div", { className: "plan-objective-text" }, execution.message || "-"),
                  execution.resultSummary
                    ? e("pre", { className: "doctor-check-detail" }, execution.resultSummary)
                    : null
                )
                : null,
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "단계")),
                e("div", { className: "plan-step-list" }, planStepsContent)
              ),
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "결정 로그")),
                renderStringList(e, "doctor-action-list", plan.decisionLog)
              )
            )
        )
      )
    )
  );
}
