function formatNotebookTimestamp(value) {
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

function trimNotebookText(value, maxChars = 280) {
  const normalized = `${value || ""}`.trim();
  if (normalized.length <= maxChars) {
    return normalized;
  }

  return `${normalized.slice(0, maxChars)}...`;
}

function renderNotebookDocumentCard(e, title, document) {
  const exists = !!(document && document.exists);
  const preview = `${document && document.preview ? document.preview : ""}`.trim();
  return e("article", { className: "plan-detail-card" },
    e("div", { className: "plans-section-head" },
      e("strong", null, title),
      e("span", { className: `tool-status-chip ${exists ? "ok" : "neutral"}` }, exists ? "있음" : "없음")
    ),
    e("div", { className: "tiny" }, `updated ${formatNotebookTimestamp(document && document.updatedAtUtc)}`),
    e("div", { className: "tiny" }, `size ${document && document.sizeBytes ? document.sizeBytes : 0} bytes`),
    preview
      ? e("pre", { className: "doctor-check-detail" }, preview)
      : e("div", { className: "empty doctor-empty-state" }, "아직 기록이 없습니다."),
    document && document.path
      ? e("div", { className: "tiny mt8" }, trimNotebookText(document.path, 140))
      : null
  );
}

export function renderNotebooksPanel(props) {
  const {
    e,
    authed,
    notebooksState,
    setNotebookProjectKey,
    setNotebookAppendKind,
    setNotebookAppendText,
    refreshNotebook,
    appendNotebook,
    createNotebookHandoff,
    appendSelectedPlanDecision,
    appendSelectedTaskVerification,
    appendDoctorVerification,
    appendRefactorVerification
  } = props;

  const snapshot = notebooksState.snapshot || null;
  const notebook = snapshot?.notebook || null;
  const disabled = !authed || notebooksState.pending || notebooksState.loading;

  return e("section", { className: "panel settings-optimized-panel settings-notebooks-panel notebooks-panel" },
    e("div", { className: "plans-panel-head" },
      e("div", null,
        e("h2", null, "Notebook / Handoff"),
        e("p", { className: "hint" }, "세션이 끝나도 유지되는 learnings, decisions, verification, handoff 문서를 관리합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", {
          className: "btn",
          disabled,
          onClick: () => refreshNotebook()
        }, notebooksState.loading ? "불러오는 중..." : "새로고침"),
        e("button", {
          className: "btn primary",
          disabled,
          onClick: () => createNotebookHandoff()
        }, notebooksState.pending && notebooksState.lastAction === "handoff" ? "생성 중..." : "Handoff 생성")
      )
    ),
    notebooksState.lastError
      ? e("div", { className: "error-banner" }, notebooksState.lastError)
      : null,
    notebooksState.lastMessage
      ? e("div", { className: "tiny" }, notebooksState.lastMessage)
      : e("div", { className: "tiny" },
        notebooksState.loading
          ? "노트북 상태를 읽는 중입니다."
          : notebooksState.pending
            ? "노트북 작업을 처리 중입니다."
            : `최근 수신: ${formatNotebookTimestamp(notebooksState.receivedAt || snapshot?.readAtUtc)}`
      ),
    e("div", { className: "plans-create-grid" },
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "project key"),
        e("input", {
          className: "input",
          value: notebooksState.projectKeyDraft,
          placeholder: notebook?.projectKey || "비우면 현재 프로젝트 키 자동 사용",
          onChange: (event) => setNotebookProjectKey(event.target.value)
        })
      ),
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "append kind"),
        e("select", {
          className: "input",
          value: notebooksState.appendKind || "learning",
          onChange: (event) => setNotebookAppendKind(event.target.value)
        },
        e("option", { value: "learning" }, "learning"),
        e("option", { value: "decision" }, "decision"),
        e("option", { value: "verification" }, "verification"))
      ),
      e("label", { className: "meta-field plan-field-wide" },
        e("span", { className: "meta-label" }, "append 내용"),
        e("textarea", {
          className: "input plan-textarea",
          rows: 5,
          value: notebooksState.appendText,
          placeholder: "세션에서 남길 핵심 교훈, 결정, 검증 결과를 입력하세요.",
          onChange: (event) => setNotebookAppendText(event.target.value)
        })
      )
    ),
    e("div", { className: "row plans-head-actions mt8" },
      e("button", {
        className: "btn primary",
        disabled,
        onClick: () => appendNotebook()
      }, notebooksState.pending && notebooksState.lastAction === "append" ? "추가 중..." : "기록 추가"),
      e("button", {
        className: "btn",
        disabled: !authed || notebooksState.pending,
        onClick: appendSelectedPlanDecision
      }, "선택 plan -> decision"),
      e("button", {
        className: "btn",
        disabled: !authed || notebooksState.pending,
        onClick: appendSelectedTaskVerification
      }, "선택 graph -> verification"),
      e("button", {
        className: "btn",
        disabled: !authed || notebooksState.pending,
        onClick: appendDoctorVerification
      }, "doctor -> verification"),
      e("button", {
        className: "btn",
        disabled: !authed || notebooksState.pending,
        onClick: appendRefactorVerification
      }, "refactor -> verification")
    ),
    notebook
      ? e("div", { className: "tiny mt8" }, `project=${notebook.projectKey || "-"} · root=${notebook.rootPath || "-"}`)
      : null,
    snapshot
      ? e("div", { className: "plan-summary-grid" },
        e("div", { className: "doctor-summary-card" },
          e("div", { className: "doctor-summary-label" }, "learnings"),
          e("div", { className: "doctor-summary-value plan-status-value" }, snapshot.learnings?.exists ? "있음" : "없음")
        ),
        e("div", { className: "doctor-summary-card" },
          e("div", { className: "doctor-summary-label" }, "decisions"),
          e("div", { className: "doctor-summary-value plan-status-value" }, snapshot.decisions?.exists ? "있음" : "없음")
        ),
        e("div", { className: "doctor-summary-card" },
          e("div", { className: "doctor-summary-label" }, "verification"),
          e("div", { className: "doctor-summary-value plan-status-value" }, snapshot.verification?.exists ? "있음" : "없음")
        ),
        e("div", { className: "doctor-summary-card" },
          e("div", { className: "doctor-summary-label" }, "handoff"),
          e("div", { className: "doctor-summary-value plan-status-value" }, snapshot.handoff?.exists ? "있음" : "없음")
        )
      )
      : null,
    !snapshot
      ? e("div", { className: "empty doctor-empty-state" }, "저장된 notebook 문서를 아직 읽지 않았습니다.")
      : e("div", { className: "plan-step-list" },
        renderNotebookDocumentCard(e, "Learnings", snapshot.learnings),
        renderNotebookDocumentCard(e, "Decisions", snapshot.decisions),
        renderNotebookDocumentCard(e, "Verification", snapshot.verification),
        renderNotebookDocumentCard(e, "Handoff", snapshot.handoff)
      )
  );
}
