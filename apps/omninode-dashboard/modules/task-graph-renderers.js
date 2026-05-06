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
  if (normalized === "running" || normalized === "blocked" || normalized === "draft") {
    return "warn";
  }
  if (normalized === "failed") {
    return "error";
  }
  return "neutral";
}

function renderOutputBlock(e, title, text) {
  return e("article", { className: "plan-detail-card task-graph-output-card" },
    e("div", { className: "plans-section-head" }, e("strong", null, title)),
    e("pre", { className: "doctor-check-detail task-graph-log" }, text && `${text}`.trim() ? text : "비어 있음")
  );
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
    cancelTask
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

  return e("section", { className: "panel settings-optimized-panel settings-task-graph-panel task-graph-panel" },
    e("div", { className: "plans-panel-head" },
      e("div", null,
        e("h2", null, "Background Task Graph"),
        e("p", { className: "hint" }, "선택한 계획을 세션용 DAG로 바꾸고, 개별 task 상태와 로그를 추적합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", { className: "btn", disabled, onClick: refreshTaskGraphList }, taskGraphState.loading ? "불러오는 중..." : "목록 새로고침"),
        e("button", { className: "btn primary", disabled, onClick: submitTaskGraphCreate }, taskGraphState.pending ? "처리 중..." : "그래프 생성")
      )
    ),
    taskGraphState.lastError
      ? e("div", { className: "error-banner" }, taskGraphState.lastError)
      : null,
    e("div", { className: "task-graph-form" },
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "plan id"),
        e("input", {
          className: "input",
          value: taskGraphState.createPlanId,
          placeholder: selectedPlanId || "plan_...",
          onChange: (event) => setTaskGraphCreatePlanId(event.target.value)
        })
      ),
      e("button", {
        className: "btn",
        disabled: !authed,
        onClick: useSelectedPlanForTaskGraph
      }, "선택한 계획 채우기"),
      e("div", { className: "tiny" },
        selectedPlanId
          ? `현재 기본 plan: ${selectedPlanId}`
          : "현재 선택된 plan이 없습니다."
      )
    ),
    e("div", { className: "plans-layout" },
      e("div", { className: "plans-list-column" },
        e("div", { className: "plans-section-head" },
          e("strong", null, "저장된 graph"),
          e("span", { className: "tiny" }, `${items.length}건`)
        ),
        items.length === 0
          ? e("div", { className: "empty plan-empty-state" }, "저장된 Task graph가 없습니다.")
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
      e("div", { className: "plans-detail-column" },
        !graph
          ? e("div", { className: "empty plan-empty-state" }, "왼쪽에서 Task graph를 선택하세요.")
          : e("div", { className: "plan-detail" },
            e("div", { className: "plan-detail-head" },
              e("div", null,
                e("div", { className: "tiny" }, graph.graphId || "-"),
                e("h3", null, `source ${graph.sourcePlanId || "-"}`),
                e("div", { className: "tiny" }, `updated ${formatTaskTimestamp(graph.updatedAtUtc)}`)
              ),
              e("div", { className: "row plan-detail-actions" },
                e("button", { className: "btn", disabled, onClick: () => loadTaskGraph(graph.graphId) }, "다시 읽기"),
                e("button", { className: "btn primary", disabled, onClick: () => runTaskGraph(graph.graphId) }, "실행")
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
                e("div", { className: "doctor-summary-value plan-status-value" }, String(tasks.filter((task) => task.status === "Completed").length))
              ),
              e("div", { className: "doctor-summary-card" },
                e("div", { className: "doctor-summary-label" }, "failed"),
                e("div", { className: "doctor-summary-value plan-status-value" }, String(tasks.filter((task) => task.status === "Failed").length))
              )
            ),
            e("article", { className: "plan-detail-card" },
              e("div", { className: "plans-section-head" }, e("strong", null, "Task 목록")),
              tasks.length === 0
                ? e("div", { className: "empty plan-empty-state" }, "Task가 없습니다.")
                : e("div", { className: "task-graph-task-list" },
                  tasks.map((task) => {
                    const active = task.taskId === selectedTaskId;
                    const canCancel = task.status === "Running" || task.status === "Pending" || task.status === "Blocked";
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
                    e("div", { className: "plan-objective-text" }, trimText(task.outputSummary || task.prompt || "-")),
                    task.error
                      ? e("div", { className: "tiny task-graph-error" }, `error ${task.error}`)
                      : null,
                    e("div", { className: "row task-graph-node-actions" },
                      e("button", {
                        className: "btn",
                        disabled: !authed,
                        onClick: () => loadTaskOutput(graph.graphId, task.taskId)
                      }, active ? "선택됨" : "출력 보기"),
                      canCancel
                        ? e("button", {
                          className: "btn ghost",
                          disabled: disabled,
                          onClick: () => cancelTask(graph.graphId, task.taskId)
                        }, "취소")
                        : null
                    ));
                  }))
            ),
            selectedTask
              ? e("article", { className: "plan-detail-card" },
                e("div", { className: "plans-section-head" },
                  e("strong", null, `선택된 task · ${selectedTask.taskId}`),
                  e("span", { className: "tiny" }, `${selectedTask.category || "-"} · ${selectedTask.title || "-"}`)
                ),
                e("div", { className: "task-graph-output-grid" },
                  renderOutputBlock(e, "stdout", output?.stdout || ""),
                  renderOutputBlock(e, "stderr", output?.stderr || ""),
                  renderOutputBlock(e, "result.json", output?.resultJson || "")
                )
              )
              : null
          )
      )
    )
  );
}
