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

  const lower = normalized.toLowerCase();
  const labels = {
    draft: "작성 중",
    reviewpending: "검토 필요",
    approved: "진행 확정",
    running: "진행 중",
    completed: "끝남",
    rejected: "보류",
    abandoned: "중단"
  };
  return labels[lower] || normalized;
}

function formatPlanModeLabel(mode) {
  return `${mode || ""}`.toLowerCase() === "interview" ? "질문 먼저" : "빠른 초안";
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
    cerebrasModelChoices,
    nvidiaModelChoices
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

  if (provider === "nvidia") {
    return nvidiaModelChoices?.[0]?.id || "-";
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
    nvidiaModelChoices,
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
    cerebrasModelChoices,
    nvidiaModelChoices
  });
  const providerOptions = [
    { value: "groq", label: "Groq" },
    { value: "gemini", label: "Gemini" },
    { value: "cerebras", label: "Cerebras" },
    { value: "nvidia", label: "NVIDIA NIM" },
    { value: "codex", label: "Codex" },
    { value: "copilot", label: "Copilot" }
  ];
  const fixedModelText = provider === "gemini"
    ? `Gemini 기본 모델 ${geminiModelChoices?.[0]?.label || modelLabel}`
    : provider === "cerebras"
      ? `Cerebras 기본 모델 ${cerebrasModelChoices?.[0]?.label || modelLabel}`
      : provider === "nvidia"
        ? `NVIDIA NIM 기본 모델 ${nvidiaModelChoices?.[0]?.label || modelLabel}`
        : `Codex 기본 모델 ${defaultCodexModel || modelLabel}`;
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
      e("span", { className: "meta-label" }, "AI 선택"),
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
        e("span", null, "안 될 때 다음 순서"),
        e("strong", null, chainValue || "비어 있음")
      ),
      e("label", { className: "meta-field automation-chain-field" },
        e("span", { className: "meta-label" }, "안 될 때 다음 순서"),
        e("input", {
          className: "input",
          value: chainValue,
          placeholder: "groq, gemini, nvidia, cerebras, codex, copilot",
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
      title: "먼저 작업 내용을 적어야 합니다.",
      description: "무엇을 바꿀지와 하지 말아야 할 일을 적으면 다음 단계가 덜 흔들립니다.",
      action: "draft"
    });
  }

  if (!plan) {
    list.push({
      key: "select",
      title: "저장된 작업을 하나 골라보세요.",
      description: "어떤 목표로 만든 작업인지 먼저 봐야 검토하거나 시작할 수 있습니다.",
      action: "browse"
    });
    return list;
  }

  if (!review) {
    list.push({
      key: "review",
      title: "시작 전에 한 번 점검하세요.",
      description: "빠진 일, 위험한 부분, 확인할 일을 먼저 보면 다시 되돌릴 일이 줄어듭니다.",
      action: "review"
    });
  }

  if (review && `${plan.status || ""}`.toLowerCase() !== "approved") {
    list.push({
      key: "approve",
      title: "진행할지 확정하세요.",
      description: "이 작업을 실제로 시작해도 되는지 한 번 눌러 확정합니다.",
      action: "approve"
    });
  }

  if (`${plan.status || ""}`.toLowerCase() === "approved" && !execution) {
    list.push({
      key: "run",
      title: "이제 작업을 시작할 수 있습니다.",
      description: "단계와 확인할 일을 한 번 보고 시작하세요.",
      action: "run"
    });
  }

  if (list.length === 0) {
    list.push({
      key: "fresh",
      title: "현재 작업 정리는 갖춰져 있습니다.",
      description: "새로 바뀐 방향이 생기면 노트북의 방향 정리에 먼저 남기는 편이 좋습니다.",
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
    nvidiaModelChoices,
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
          e("div", { className: "tiny" }, "해야 할 일"),
          renderStringList(e, "doctor-action-list", step.mustDo)
        ),
        e("div", null,
          e("div", { className: "tiny" }, "하지 말 것"),
          renderStringList(e, "doctor-action-list", step.mustNotDo)
        ),
        e("div", null,
          e("div", { className: "tiny" }, "확인할 것"),
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
        e("h2", null, "작업 정리"),
        e("p", { className: "hint" }, "바로 시작하기 전에 할 일, 지켜야 할 것, 확인할 일을 한 번 정리합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", { type: "button", className: "btn", disabled, onClick: refreshPlansList }, plansState.loading ? "불러오는 중..." : "목록 다시 읽기"),
        e("button", { type: "button", className: "btn primary", disabled, onClick: submitPlanCreate }, plansState.pending ? "처리 중..." : "계획 만들기")
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
            ? `${items.length}개의 저장된 작업이 있습니다.`
            : "저장된 작업이 없습니다."
      )
    ),
    e("div", { className: "plan-metric-grid" },
      renderPlanMetricCard(e, "저장된 작업", `${items.length}건`, "목록에서 골라 내용과 점검 상태 확인"),
      renderPlanMetricCard(e, "진행 확정", `${approvedCount}건`, "바로 시작할 수 있는 작업 수"),
      renderPlanMetricCard(e, "진행 중", `${runningCount}건`, "이미 시작된 작업 수"),
      renderPlanMetricCard(e, "작성 방식", formatPlanModeLabel(activeMode), activeMode === "interview" ? "질문부터 정리" : "빠른 초안 만들기", activeMode === "interview" ? "neutral" : "")
    ),
    e("div", { className: "plans-workbench-shell" },
      e("div", { className: "plans-primary-column" },
        e("section", { className: "plans-create-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "작업 내용 적기"),
              e("p", null, "무엇을 할지와 지켜야 할 기준을 적으면 계획으로 정리합니다.")
            )
          ),
          e("label", { className: "meta-field plan-field-wide" },
            e("span", { className: "meta-label" }, "할 일"),
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
              { key: "interview", label: "질문 먼저", helper: "빠진 조건부터 확인" }
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
            e("span", { className: "plan-template-label" }, "필요하면 시작 문장 넣기"),
            e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("feature") }, "기능 개선"),
            e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("bugfix") }, "버그 수정"),
            e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("interview") }, "요구사항 점검"),
            e("button", { type: "button", className: "btn ghost", onClick: () => setPlanCreateConstraintsText("") }, "기준 비우기")
          ),
          e("label", { className: "meta-field plan-field-wide" },
            e("span", { className: "meta-label" }, "지켜야 할 것"),
            e("textarea", {
              className: "input plan-textarea plan-constraints plan-workbench-constraints",
              value: plansState.createConstraintsText,
              rows: 5,
              placeholder: "줄바꿈 단위로 입력합니다. 예: 기존 기능 유지 / 요청 범위 외 변경 금지 / 반응형 정렬 유지",
              onChange: (event) => setPlanCreateConstraintsText(event.target.value)
            })
          ),
          e("div", { className: "plan-compose-actions" },
            e("button", { type: "button", className: "btn", disabled, onClick: refreshPlansList }, "목록 다시 읽기"),
            e("button", { type: "button", className: "btn primary", disabled, onClick: submitPlanCreate }, plansState.pending ? "만드는 중..." : "계획 만들기")
          )
        ),
        e("section", { className: "automation-llm-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "계획에 쓸 AI"),
              e("p", null, "계획을 만들 때 쓸 AI와, 빠진 점을 봐줄 AI를 고릅니다.")
            ),
            e("div", { className: "automation-llm-actions" },
              e("button", { type: "button", className: "btn", disabled, onClick: refreshRoutingPolicy }, "AI 순서 다시 읽기"),
              e("button", { type: "button", className: "btn primary", disabled, onClick: saveRoutingPolicy }, routingPolicyState?.pending ? "저장 중..." : "AI 순서 저장")
            )
          ),
          e("div", { className: "automation-route-list" },
            renderAutomationRouteEditor(e, {
              title: "초안 만들기",
              description: "할 일을 처음 정리",
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
              nvidiaModelChoices,
              send,
              disabled
            }),
            renderAutomationRouteEditor(e, {
              title: "빠진 점 보기",
              description: "위험한 부분과 확인할 일 찾기",
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
              nvidiaModelChoices,
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
                ? e("button", { type: "button", className: "btn ghost", disabled, onClick: () => reviewPlan(plan.planId) }, "점검")
                : item.action === "approve" && plan
                  ? e("button", { type: "button", className: "btn ghost", disabled, onClick: () => approvePlan(plan.planId) }, "진행 확정")
                  : item.action === "run" && plan
                    ? e("button", { type: "button", className: "btn ghost", disabled, onClick: () => runPlan(plan.planId) }, "시작")
                    : e("button", { type: "button", className: "btn ghost", onClick: () => applyTemplate("feature") }, "초안 채우기")
            ))
          )
        )
      ),
      e("div", { className: "plans-browser-shell" },
        e("section", { className: "plans-list-column plan-browser-list" },
          e("div", { className: "plans-section-head" },
            e("strong", null, "저장된 작업"),
            e("span", { className: "tiny" }, `${items.length}건`)
          ),
          items.length === 0
            ? e("div", { className: "empty plan-empty-state" }, "저장된 작업이 없습니다.")
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
                e("div", { className: "tiny" }, `수정 ${formatPlanTimestamp(item.updatedAtUtc)}`));
              }))
        ),
        e("section", { className: "plans-detail-column plan-browser-detail" },
          !plan
            ? e("div", { className: "empty plan-empty-state" }, "왼쪽 목록에서 작업을 선택하세요.")
            : e("div", { className: "plan-detail" },
              e("div", { className: "plan-detail-head" },
                e("div", null,
                  e("div", { className: "tiny" }, plan.planId || "-"),
                  e("h3", null, plan.title || "제목 없음"),
                  e("div", { className: "tiny" }, `수정 ${formatPlanTimestamp(plan.updatedAtUtc)}`)
                ),
                e("div", { className: "row plan-detail-actions" },
                  e("button", { type: "button", className: "btn", disabled, onClick: () => loadPlanSnapshot(plan.planId) }, "새로 읽기"),
                  e("button", { type: "button", className: "btn", disabled, onClick: () => reviewPlan(plan.planId) }, "점검"),
                  e("button", { type: "button", className: "btn", disabled, onClick: () => approvePlan(plan.planId) }, "진행 확정"),
                  e("button", { type: "button", className: "btn primary", disabled, onClick: () => runPlan(plan.planId) }, "시작")
                )
              ),
              e("div", { className: "plan-summary-grid" },
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "상태"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, normalizeStatusLabel(plan.status))
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "작업 순서"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, String(Array.isArray(plan.steps) ? plan.steps.length : 0))
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "점검"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, review ? "있음" : "없음")
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "시작 상태"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, normalizeStatusLabel(execution?.status))
                )
              ),
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "하려는 일")),
                e("div", { className: "plan-objective-text" }, plan.objective || "-")
              ),
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "지켜야 할 것")),
                renderStringList(e, "doctor-action-list", plan.constraints)
              ),
              review
                ? e("article", { className: "plan-detail-card plan-review-card" },
                  e("div", { className: "plans-section-head" },
                    e("strong", null, "점검 결과"),
                    e("span", { className: "tiny" }, `${formatPlanTimestamp(review.reviewedAtUtc)} · ${review.reviewerRoute || "-"}`)
                  ),
                  e("div", { className: "plan-review-summary" }, review.summary || "-"),
                  e("div", { className: "plan-review-grid" },
                    e("div", { className: "plan-review-box" },
                      e("strong", null, "확인된 점"),
                      renderStringList(e, "doctor-action-list", review.findings)
                    ),
                    e("div", { className: "plan-review-box" },
                      e("strong", null, "위험한 점"),
                      renderStringList(e, "doctor-action-list", review.risks)
                    ),
                    e("div", { className: "plan-review-box" },
                      e("strong", null, "아직 확인할 것"),
                      renderStringList(e, "doctor-action-list", review.missingVerification)
                    )
                  )
                )
                : null,
              execution
                ? e("article", { className: "plan-detail-card" },
                  e("div", { className: "plans-section-head" },
                    e("strong", null, "진행 결과"),
                    e("span", { className: `tool-status-chip ${resolvePlanTone(execution.status)}` }, normalizeStatusLabel(execution.status))
                  ),
                  e("div", { className: "tiny" }, `요청 ${formatPlanTimestamp(execution.requestedAtUtc)}`),
                  e("div", { className: "tiny" }, `완료 ${formatPlanTimestamp(execution.completedAtUtc)}`),
                  e("div", { className: "plan-objective-text" }, execution.message || "-"),
                  execution.resultSummary
                    ? e("pre", { className: "doctor-check-detail" }, execution.resultSummary)
                    : null
                )
                : null,
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "작업 순서")),
                e("div", { className: "plan-step-list" }, planStepsContent)
              ),
              e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" }, e("strong", null, "남겨둔 방향")),
                renderStringList(e, "doctor-action-list", plan.decisionLog)
              )
            )
        )
      )
    )
  );
}
