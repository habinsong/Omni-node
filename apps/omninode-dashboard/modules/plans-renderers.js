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

function renderStringList(e, className, items) {
  const normalizedItems = Array.isArray(items) ? items.filter(Boolean) : [];
  if (normalizedItems.length === 0) {
    return e("div", { className: "tiny" }, "없음");
  }

  return e("ul", { className },
    normalizedItems.map((item, index) => e("li", { key: `${className}-${index}` }, item))
  );
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
    runPlan
  } = props;

  const items = Array.isArray(plansState.items) ? plansState.items : [];
  const snapshot = plansState.snapshot || null;
  const plan = snapshot?.plan || null;
  const review = snapshot?.review || null;
  const execution = snapshot?.execution || null;
  const disabled = !authed || plansState.pending || plansState.loading;
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

  return e("section", { className: "panel settings-optimized-panel settings-plans-panel plans-panel" },
    e("div", { className: "plans-panel-head" },
      e("div", null,
        e("h2", null, "작업 계획"),
        e("p", { className: "hint" }, "대형 작업을 바로 실행하지 않고 계획, 리뷰, 승인, 실행 순서로 관리합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", { className: "btn", disabled, onClick: refreshPlansList }, plansState.loading ? "불러오는 중..." : "목록 새로고침"),
        e("button", { className: "btn primary", disabled, onClick: submitPlanCreate }, plansState.pending ? "처리 중..." : "계획 생성")
      )
    ),
    plansState.lastError
      ? e("div", { className: "error-banner" }, plansState.lastError)
      : null,
    e("div", { className: "plans-create-grid" },
      e("label", { className: "meta-field plan-field-wide" },
        e("span", { className: "meta-label" }, "요청"),
        e("textarea", {
          className: "input plan-textarea",
          value: plansState.createObjective,
          rows: 4,
          placeholder: "예: AGENTS.md와 첨부 설계를 반영해 doctor 기능 구현",
          onChange: (event) => setPlanCreateObjective(event.target.value)
        })
      ),
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "생성 모드"),
        e("select", {
          className: "input",
          value: plansState.createMode || "fast",
          onChange: (event) => setPlanCreateMode(event.target.value)
        },
        e("option", { value: "fast" }, "fast"),
        e("option", { value: "interview" }, "interview"))
      ),
      e("label", { className: "meta-field plan-field-wide" },
        e("span", { className: "meta-label" }, "제약사항 (줄바꿈 구분)"),
        e("textarea", {
          className: "input plan-textarea plan-constraints",
          value: plansState.createConstraintsText,
          rows: 3,
          placeholder: "예: 사용자가 요청한 내용 외 변경 금지",
          onChange: (event) => setPlanCreateConstraintsText(event.target.value)
        })
      )
    ),
    e("div", { className: "plans-layout" },
      e("div", { className: "plans-list-column" },
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
              e("div", { className: "plan-list-item-objective" }, item.objective || "-"),
              e("div", { className: "tiny" }, `updated ${formatPlanTimestamp(item.updatedAtUtc)}`));
            }))
      ),
      e("div", { className: "plans-detail-column" },
        !plan
          ? e("div", { className: "empty plan-empty-state" }, "왼쪽에서 계획을 선택하세요.")
          : e("div", { className: "plan-detail" },
            e("div", { className: "plan-detail-head" },
              e("div", null,
                e("div", { className: "tiny" }, plan.planId || "-"),
                e("h3", null, plan.title || "제목 없음"),
                e("div", { className: "tiny" }, `updated ${formatPlanTimestamp(plan.updatedAtUtc)}`)
              ),
              e("div", { className: "row plan-detail-actions" },
                e("button", { className: "btn", disabled, onClick: () => loadPlanSnapshot(plan.planId) }, "다시 읽기"),
                e("button", { className: "btn", disabled, onClick: () => reviewPlan(plan.planId) }, "리뷰"),
                e("button", { className: "btn", disabled, onClick: () => approvePlan(plan.planId) }, "승인"),
                e("button", { className: "btn primary", disabled, onClick: () => runPlan(plan.planId) }, "실행")
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
                  e("strong", null, "Reviewer"),
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
  );
}
