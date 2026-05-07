function formatTaskTimestamp(value) {
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

function formatTaskRelative(value) {
  const raw = `${value || ""}`.trim();
  if (!raw) {
    return "기록 없음";
  }

  const diffMs = Date.now() - new Date(raw).getTime();
  if (!Number.isFinite(diffMs) || diffMs < 0) {
    return formatTaskTimestamp(raw);
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

function trimText(value, maxChars = 320) {
  const normalized = `${value || ""}`.trim();
  if (normalized.length <= maxChars) {
    return normalized;
  }

  return `${normalized.slice(0, maxChars)}...`;
}

function resolveTaskTone(status) {
  const normalized = `${status || ""}`.toLowerCase();
  if (normalized === "completed") {
    return "ok";
  }
  if (normalized === "running" || normalized === "blocked" || normalized === "draft" || normalized === "pending") {
    return "warn";
  }
  if (normalized === "failed" || normalized === "canceled") {
    return "error";
  }
  return "neutral";
}

function renderTaskMetricCard(e, label, value, helper, tone = "") {
  return e("article", { className: `task-graph-metric-card ${tone}`.trim() },
    e("span", { className: "task-graph-metric-label" }, label),
    e("strong", { className: "task-graph-metric-value" }, value),
    e("p", { className: "task-graph-metric-helper" }, helper)
  );
}

function renderOutputBlock(e, title, text) {
  return e("article", { className: "plan-detail-card task-graph-output-card" },
    e("div", { className: "plans-section-head" }, e("strong", null, title)),
    e("pre", { className: "doctor-check-detail task-graph-log" }, text && `${text}`.trim() ? text : "비어 있음")
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

function buildGraphChecklist(graph, selectedPlanId, selectedTask) {
  const list = [];
  if (!selectedPlanId) {
    list.push({
      key: "plan",
      title: "먼저 plan을 지정해야 합니다.",
      description: "선택된 계획을 채우거나 plan id를 직접 넣어야 그래프를 만들 수 있습니다.",
      action: "fill"
    });
    return list;
  }

  if (!graph) {
    list.push({
      key: "create",
      title: "아직 생성된 그래프가 없습니다.",
      description: "plan id를 기준으로 태스크 그래프를 만든 뒤 실행 상태를 추적합니다.",
      action: "create"
    });
    return list;
  }

  if (`${graph.status || ""}`.toLowerCase() === "draft") {
    list.push({
      key: "run",
      title: "생성된 그래프를 실행하세요.",
      description: "현재는 초안 상태이므로 실제 task 전환을 시작하지 않았습니다.",
      action: "run"
    });
  }

  if (selectedTask && !selectedTask.outputSummary && !selectedTask.error) {
    list.push({
      key: "output",
      title: "선택된 task 출력을 확인하세요.",
      description: "stdout, stderr, result.json을 먼저 보면 현재 막힌 지점을 빠르게 파악할 수 있습니다.",
      action: "output"
    });
  }

  if (list.length === 0) {
    list.push({
      key: "watch",
      title: "현재 그래프는 추적 가능한 상태입니다.",
      description: "상태가 바뀌면 선택 task 출력과 실패 노드를 먼저 다시 확인하세요.",
      action: "browse"
    });
  }

  return list;
}

export function renderTaskGraphPanel(props) {
  const {
    e,
    authed,
    plansState,
    taskGraphState,
    setTaskGraphCreatePlanId,
    useSelectedPlanForTaskGraph,
    refreshTaskGraphList,
    loadTaskGraph,
    submitTaskGraphCreate,
    runTaskGraph,
    loadTaskOutput,
    cancelTask,
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

  const items = Array.isArray(taskGraphState.items) ? taskGraphState.items : [];
  const snapshot = taskGraphState.snapshot || null;
  const graph = snapshot?.graph || null;
  const tasks = Array.isArray(graph?.nodes) ? graph.nodes : [];
  const output = taskGraphState.output || null;
  const selectedTaskId = taskGraphState.selectedTaskId || tasks[0]?.taskId || "";
  const disabled = !authed || taskGraphState.pending || taskGraphState.loading;
  const selectedPlanId = taskGraphState.createPlanId || plansState?.selectedPlanId || plansState?.snapshot?.plan?.planId || "";
  const selectedTask = tasks.find((task) => task.taskId === selectedTaskId) || tasks[0] || null;
  const completedCount = tasks.filter((task) => `${task.status || ""}`.toLowerCase() === "completed").length;
  const failedCount = tasks.filter((task) => `${task.status || ""}`.toLowerCase() === "failed").length;
  const runningCount = tasks.filter((task) => {
    const status = `${task.status || ""}`.toLowerCase();
    return status === "running" || status === "pending" || status === "blocked";
  }).length;
  const checklist = buildGraphChecklist(graph, selectedPlanId, selectedTask);

  return e("section", { className: "panel settings-optimized-panel settings-task-graph-panel task-graph-panel task-graph-workbench-panel" },
    e("div", { className: "plans-panel-head task-graph-panel-head-ux" },
      e("div", null,
        e("h2", null, "태스크 그래프"),
        e("p", { className: "hint" }, "선택한 계획을 실행 단위 그래프로 바꾸고, task별 진행과 출력을 한 화면에서 추적합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", { type: "button", className: "btn", disabled, onClick: refreshTaskGraphList }, taskGraphState.loading ? "불러오는 중..." : "목록 새로고침"),
        e("button", { type: "button", className: "btn primary", disabled, onClick: submitTaskGraphCreate }, taskGraphState.pending ? "처리 중..." : "그래프 생성")
      )
    ),
    taskGraphState.lastError
      ? e("div", { className: "error-banner" }, taskGraphState.lastError)
      : null,
    e("div", { className: "task-graph-feedback-bar" },
      e("span", { className: `tool-status-chip ${authed ? "ok" : "neutral"}` }, authed ? "세션 인증됨" : "인증 필요"),
      e("span", { className: `tool-status-chip ${taskGraphState.pending ? "warn" : taskGraphState.loading ? "neutral" : "ok"}` }, taskGraphState.pending ? "처리 중" : taskGraphState.loading ? "동기화 중" : "준비됨"),
      e("span", { className: "task-graph-feedback-text" },
        graph
          ? `${graph.graphId || "-"} · ${graph.status || "-"} · ${formatTaskRelative(graph.updatedAtUtc)}`
          : items.length > 0
            ? `${items.length}개의 저장된 그래프가 있습니다.`
            : "저장된 그래프가 없습니다."
      )
    ),
    e("div", { className: "task-graph-metric-grid" },
      renderTaskMetricCard(e, "저장된 그래프", `${items.length}건`, "목록에서 선택 후 task 상태 확인"),
      renderTaskMetricCard(e, "진행 중 task", String(runningCount), "running, pending, blocked 합계"),
      renderTaskMetricCard(e, "실패 task", String(failedCount), "현재 선택 그래프 기준 실패 수"),
      renderTaskMetricCard(e, "기준 plan", selectedPlanId || "-", selectedPlanId ? "현재 그래프 생성에 사용할 기본 plan" : "먼저 plan을 선택하거나 입력해야 함", selectedPlanId ? "" : "neutral")
    ),
    e("div", { className: "task-graph-workbench-shell" },
      e("div", { className: "task-graph-primary-column" },
        e("section", { className: "task-graph-create-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "그래프 생성"),
              e("p", null, "plan id를 확정한 뒤 그래프를 만들고, 필요하면 바로 실행까지 이어갑니다.")
            )
          ),
          e("div", { className: "task-graph-form task-graph-form-ux" },
            e("label", { className: "meta-field" },
              e("span", { className: "meta-label" }, "plan id"),
              e("input", {
                className: "input",
                value: taskGraphState.createPlanId,
                placeholder: selectedPlanId || "plan_...",
                onChange: (event) => setTaskGraphCreatePlanId(event.target.value)
              })
            ),
            e("div", { className: "task-graph-create-actions" },
              e("button", {
                type: "button",
                className: "btn",
                disabled: !authed || taskGraphState.pending,
                onClick: useSelectedPlanForTaskGraph
              }, "선택한 계획 채우기"),
              e("button", {
                type: "button",
                className: "btn primary",
                disabled,
                onClick: submitTaskGraphCreate
              }, taskGraphState.pending ? "생성 중..." : "그래프 생성")
            ),
            e("div", { className: "task-graph-plan-hint" },
              e("strong", null, selectedPlanId || "선택된 plan 없음"),
              e("span", null, selectedPlanId ? "현재 선택된 계획을 기준으로 그래프를 만들 수 있습니다." : "계획 탭에서 선택한 plan이 없으면 직접 입력해야 합니다.")
            )
          )
        ),
        e("section", { className: "automation-llm-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "그래프 실행 라우팅"),
              e("p", null, "자주 바꾸는 실행 경로를 먼저 보여주고, 보조 경로는 아래에서 필요할 때만 조정합니다.")
            ),
            e("div", { className: "automation-llm-actions" },
              e("button", { type: "button", className: "btn", disabled, onClick: refreshRoutingPolicy }, "라우팅 새로고침"),
              e("button", { type: "button", className: "btn primary", disabled, onClick: saveRoutingPolicy }, routingPolicyState?.pending ? "저장 중..." : "라우팅 저장")
            )
          ),
          e("div", { className: "automation-route-list automation-route-list-primary" },
            renderAutomationRouteEditor(e, {
              title: "UI 작업",
              description: "visualUi",
              categoryKey: "visualUi",
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
              title: "빠른 수정/검증",
              description: "quickFix",
              categoryKey: "quickFix",
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
              title: "안전 리팩터",
              description: "safeRefactor",
              categoryKey: "safeRefactor",
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
          ),
          e("details", { className: "automation-route-details" },
            e("summary", null,
              e("strong", null, "보조 라우팅"),
              e("span", null, "분석, 문서, 검색 보조")
            ),
            e("div", { className: "automation-route-list" },
              renderAutomationRouteEditor(e, {
                title: "분석/모니터",
                description: "backgroundMonitor",
                categoryKey: "backgroundMonitor",
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
                title: "문서",
                description: "documentation",
                categoryKey: "documentation",
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
                title: "리서치/검색 보조",
                description: "searchFallback",
                categoryKey: "searchFallback",
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
          )
        ),
        e("section", { className: "task-graph-next-card" },
          e("div", { className: "plans-section-head plans-section-head-ux" },
            e("div", null,
              e("strong", null, "다음 액션"),
              e("p", null, "현재 상태에서 바로 눌러야 할 동작만 먼저 보여줍니다.")
            )
          ),
          e("div", { className: "task-graph-next-list" },
            checklist.map((item) => e("article", { key: item.key, className: "task-graph-next-item" },
              e("div", null,
                e("strong", null, item.title),
                e("p", null, item.description)
              ),
              item.action === "fill"
                ? e("button", { type: "button", className: "btn ghost", disabled: !authed || taskGraphState.pending, onClick: useSelectedPlanForTaskGraph }, "plan 채우기")
                : item.action === "create"
                  ? e("button", { type: "button", className: "btn ghost", disabled, onClick: submitTaskGraphCreate }, "그래프 생성")
                  : item.action === "run" && graph
                    ? e("button", { type: "button", className: "btn ghost", disabled, onClick: () => runTaskGraph(graph.graphId) }, "실행")
                    : item.action === "output" && graph && selectedTask
                      ? e("button", { type: "button", className: "btn ghost", disabled: !authed, onClick: () => loadTaskOutput(graph.graphId, selectedTask.taskId) }, "출력 보기")
                      : e("button", { type: "button", className: "btn ghost", disabled, onClick: refreshTaskGraphList }, "상태 새로고침")
            ))
          )
        )
      ),
      e("div", { className: "task-graph-browser-shell" },
        e("section", { className: "plans-list-column task-graph-browser-list" },
          e("div", { className: "plans-section-head" },
            e("strong", null, "저장된 그래프"),
            e("span", { className: "tiny" }, `${items.length}건`)
          ),
          items.length === 0
            ? e("div", { className: "empty plan-empty-state" }, "저장된 태스크 그래프가 없습니다.")
            : e("div", { className: "plans-list" },
              items.map((item) => {
                const selected = item.graphId === taskGraphState.selectedGraphId;
                return e("button", {
                  key: item.graphId,
                  type: "button",
                  className: `plan-list-item ${selected ? "active" : ""}`,
                  onClick: () => loadTaskGraph(item.graphId)
                },
                e("div", { className: "plan-list-item-head" },
                  e("strong", null, item.graphId || "-"),
                  e("span", { className: `tool-status-chip ${resolveTaskTone(item.status)}` }, item.status || "-")
                ),
                e("div", { className: "tiny" }, `plan ${item.sourcePlanId || "-"}`),
                e("div", { className: "tiny" }, `done ${item.completedNodes || 0}/${item.totalNodes || 0} · fail ${item.failedNodes || 0} · running ${item.runningNodes || 0}`),
                e("div", { className: "tiny" }, `updated ${formatTaskTimestamp(item.updatedAtUtc)}`));
              }))
        ),
        e("section", { className: "plans-detail-column task-graph-browser-detail" },
          !graph
            ? e("div", { className: "empty plan-empty-state" }, "왼쪽 목록에서 그래프를 선택하세요.")
            : e("div", { className: "plan-detail task-graph-detail" },
              e("div", { className: "plan-detail-head" },
                e("div", null,
                  e("div", { className: "tiny" }, graph.graphId || "-"),
                  e("h3", null, `source ${graph.sourcePlanId || "-"}`),
                  e("div", { className: "tiny" }, `updated ${formatTaskTimestamp(graph.updatedAtUtc)}`)
                ),
                e("div", { className: "row plan-detail-actions" },
                  e("button", { type: "button", className: "btn", disabled, onClick: () => loadTaskGraph(graph.graphId) }, "다시 읽기"),
                  e("button", { type: "button", className: "btn primary", disabled, onClick: () => runTaskGraph(graph.graphId) }, "실행")
                )
              ),
              e("div", { className: "plan-summary-grid" },
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "status"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, graph.status || "-")
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "tasks"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, String(tasks.length))
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "completed"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, String(completedCount))
                ),
                e("div", { className: "doctor-summary-card" },
                  e("div", { className: "doctor-summary-label" }, "failed"),
                  e("div", { className: "doctor-summary-value plan-status-value" }, String(failedCount))
                )
              ),
              e("div", { className: "task-graph-inspector-grid" },
                e("article", { className: "plan-detail-card task-graph-task-browser" },
                  e("div", { className: "plans-section-head" },
                    e("strong", null, "Task 목록"),
                    e("span", { className: "tiny" }, `${tasks.length}개`)
                  ),
                  tasks.length === 0
                    ? e("div", { className: "empty plan-empty-state" }, "Task가 없습니다.")
                    : e("div", { className: "task-graph-task-list" },
                      tasks.map((task) => {
                        const active = task.taskId === selectedTaskId;
                        const canCancel = ["running", "pending", "blocked"].includes(`${task.status || ""}`.toLowerCase());
                        const dependsOn = Array.isArray(task.dependsOn) && task.dependsOn.length > 0
                          ? task.dependsOn.join(", ")
                          : "-";
                        return e("article", {
                          key: task.taskId,
                          className: `task-graph-node-card ${active ? "active" : ""}`
                        },
                        e("div", { className: "task-graph-node-head" },
                          e("div", null,
                            e("strong", null, `${task.taskId} · ${task.title || "-"}`),
                            e("div", { className: "task-graph-node-meta" },
                              e("span", null, `category ${task.category || "-"}`),
                              e("span", null, `deps ${dependsOn}`)
                            )
                          ),
                          e("span", { className: `tool-status-chip ${resolveTaskTone(task.status)}` }, task.status || "-")
                        ),
                        e("div", { className: "plan-objective-text" }, trimText(task.outputSummary || task.prompt || "-", 180)),
                        task.error
                          ? e("div", { className: "tiny task-graph-error" }, `error ${task.error}`)
                          : null,
                        e("div", { className: "row task-graph-node-actions" },
                          e("button", {
                            type: "button",
                            className: "btn",
                            disabled: !authed,
                            onClick: () => loadTaskOutput(graph.graphId, task.taskId)
                          }, active ? "선택됨" : "출력 보기"),
                          canCancel
                            ? e("button", {
                              type: "button",
                              className: "btn ghost",
                              disabled,
                              onClick: () => cancelTask(graph.graphId, task.taskId)
                            }, "취소")
                            : null
                        ));
                      }))
                ),
                e("div", { className: "task-graph-output-stack" },
                  selectedTask
                    ? e("article", { className: "plan-detail-card task-graph-selected-card" },
                      e("div", { className: "plans-section-head" },
                        e("strong", null, `선택된 task · ${selectedTask.taskId}`),
                        e("span", { className: "tiny" }, `${selectedTask.category || "-"} · ${selectedTask.title || "-"}`)
                      ),
                      e("div", { className: "task-graph-selected-summary" },
                        e("div", { className: "task-graph-selected-row" },
                          e("span", null, "상태"),
                          e("strong", null, selectedTask.status || "-")
                        ),
                        e("div", { className: "task-graph-selected-row" },
                          e("span", null, "최근 업데이트"),
                          e("strong", null, formatTaskRelative(selectedTask.updatedAtUtc || graph.updatedAtUtc))
                        ),
                        e("div", { className: "task-graph-selected-row" },
                          e("span", null, "요약"),
                          e("strong", null, trimText(selectedTask.outputSummary || selectedTask.prompt || "-", 120))
                        )
                      )
                    )
                    : e("div", { className: "empty plan-empty-state" }, "출력을 볼 task를 먼저 선택하세요."),
                  selectedTask
                    ? e("div", { className: "task-graph-output-grid" },
                      renderOutputBlock(e, "stdout", output?.stdout || ""),
                      renderOutputBlock(e, "stderr", output?.stderr || ""),
                      renderOutputBlock(e, "result.json", output?.resultJson || "")
                    )
                    : null
                )
              )
            )
        )
      )
    )
  );
}
