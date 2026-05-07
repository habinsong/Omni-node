const NOTEBOOK_KIND_META = {
  learning: {
    label: "배운 점",
    title: "Learnings",
    empty: "이번 작업에서 반복될 만한 교훈을 남깁니다.",
    template: [
      "- 상황:",
      "- 배운 점:",
      "- 다음 작업에서 유지할 기준:"
    ].join("\n")
  },
  decision: {
    label: "결정",
    title: "Decisions",
    empty: "왜 이 방향으로 결정했는지 남겨 다음 세션이 흔들리지 않게 합니다.",
    template: [
      "- 결정 내용:",
      "- 결정 이유:",
      "- 영향 범위:",
      "- 보류한 대안:"
    ].join("\n")
  },
  verification: {
    label: "검증",
    title: "Verification",
    empty: "실제로 확인한 결과와 남은 리스크를 정리합니다.",
    template: [
      "- 검증 대상:",
      "- 확인 방법:",
      "- 결과:",
      "- 남은 리스크:"
    ].join("\n")
  },
  handoff: {
    label: "인수인계",
    title: "Handoff",
    empty: "다음 세션이 바로 이어받을 수 있는 인수인계 문서입니다."
  }
};

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

function formatNotebookRelative(value) {
  const raw = `${value || ""}`.trim();
  if (!raw) {
    return "기록 없음";
  }

  const diffMs = Date.now() - new Date(raw).getTime();
  if (!Number.isFinite(diffMs) || diffMs < 0) {
    return formatNotebookTimestamp(raw);
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

function formatNotebookSize(bytes) {
  const size = Number(bytes) || 0;
  if (size < 1024) {
    return `${size} B`;
  }

  if (size < 1024 * 1024) {
    return `${(size / 1024).toFixed(size < 10 * 1024 ? 1 : 0)} KB`;
  }

  return `${(size / (1024 * 1024)).toFixed(1)} MB`;
}

function trimNotebookText(value, maxChars = 280) {
  const normalized = `${value || ""}`.trim();
  if (normalized.length <= maxChars) {
    return normalized;
  }

  return `${normalized.slice(0, maxChars)}...`;
}

function mergeNotebookDraft(base, next) {
  const current = `${base || ""}`.trim();
  const addition = `${next || ""}`.trim();
  if (!current) {
    return addition;
  }

  if (!addition) {
    return current;
  }

  if (current.includes(addition)) {
    return current;
  }

  return `${current}\n\n${addition}`.trim();
}

function buildNotebookDocuments(snapshot) {
  if (!snapshot) {
    return [];
  }

  return [
    { key: "learning", document: snapshot.learnings },
    { key: "decision", document: snapshot.decisions },
    { key: "verification", document: snapshot.verification },
    { key: "handoff", document: snapshot.handoff }
  ];
}

function buildNotebookChecklist(snapshot) {
  if (!snapshot) {
    return [
      {
        key: "load",
        title: "노트북을 먼저 불러오세요.",
        description: "현재 프로젝트 기준 문서와 최근 상태를 읽어와야 다음 작업을 정리할 수 있습니다.",
        kind: "learning",
        template: NOTEBOOK_KIND_META.learning.template
      }
    ];
  }

  const items = [];
  if (!snapshot.decisions?.exists) {
    items.push({
      key: "decision",
      title: "기준 결정이 비어 있습니다.",
      description: "이번 작업의 방향과 제외한 대안을 먼저 남기면 다음 세션이 덜 흔들립니다.",
      kind: "decision",
      template: NOTEBOOK_KIND_META.decision.template
    });
  }

  if (!snapshot.verification?.exists) {
    items.push({
      key: "verification",
      title: "검증 결과가 없습니다.",
      description: "실행, doctor, refactor 결과 중 실제로 확인한 내용을 verification에 남기세요.",
      kind: "verification",
      template: NOTEBOOK_KIND_META.verification.template
    });
  }

  if (!snapshot.learnings?.exists) {
    items.push({
      key: "learning",
      title: "재사용할 교훈이 아직 없습니다.",
      description: "반복 작업에서 다시 써먹을 수 있는 패턴이나 주의점을 learning으로 남기세요.",
      kind: "learning",
      template: NOTEBOOK_KIND_META.learning.template
    });
  }

  if (!snapshot.handoff?.exists) {
    items.push({
      key: "handoff",
      title: "handoff 문서가 아직 생성되지 않았습니다.",
      description: "다음 세션을 넘기기 전에 handoff를 생성해 현재 상태를 한 번에 묶어두는 편이 낫습니다.",
      kind: "handoff",
      template: ""
    });
  }

  if (items.length === 0) {
    items.push({
      key: "fresh",
      title: "현재 문서 구성이 갖춰져 있습니다.",
      description: "새 변경이 생기면 verification을 먼저 보강하고 handoff를 다시 생성하세요.",
      kind: "verification",
      template: NOTEBOOK_KIND_META.verification.template
    });
  }

  return items;
}

function renderNotebookMetricCard(e, label, value, helper, tone = "") {
  return e("article", { className: `notebook-metric-card ${tone}`.trim() },
    e("span", { className: "notebook-metric-label" }, label),
    e("strong", { className: "notebook-metric-value" }, value),
    e("p", { className: "notebook-metric-helper" }, helper)
  );
}

function renderNotebookDocumentCard(e, options) {
  const { kind, document, applyDraft, createNotebookHandoff, disabled, isHandoffPending } = options;
  const meta = NOTEBOOK_KIND_META[kind] || NOTEBOOK_KIND_META.learning;
  const exists = !!(document && document.exists);
  const preview = `${document && document.preview ? document.preview : ""}`.trim();
  const actionLabel = kind === "handoff" ? "handoff 다시 생성" : `${meta.label} 초안 넣기`;
  const statusTone = exists ? "ok" : "neutral";

  return e("article", { className: `notebook-document-card notebook-document-${kind}` },
    e("div", { className: "notebook-document-head" },
      e("div", null,
        e("strong", null, meta.label),
        e("p", null, exists ? formatNotebookRelative(document.updatedAtUtc) : meta.empty)
      ),
      e("span", { className: `tool-status-chip ${statusTone}` }, exists ? "기록 있음" : "비어 있음")
    ),
    e("div", { className: "notebook-document-stats" },
      e("span", null, `업데이트 ${formatNotebookTimestamp(document && document.updatedAtUtc)}`),
      e("span", null, `크기 ${formatNotebookSize(document && document.sizeBytes)}`)
    ),
    preview
      ? e("pre", { className: "notebook-document-preview" }, preview)
      : e("div", { className: "notebook-document-empty" }, meta.empty),
    document && document.path
      ? e("div", { className: "notebook-document-path" }, trimNotebookText(document.path, 140))
      : null,
    e("div", { className: "notebook-document-actions" },
      kind === "handoff"
        ? e("button", {
          type: "button",
          className: "btn",
          disabled,
          onClick: () => createNotebookHandoff()
        }, isHandoffPending ? "생성 중..." : actionLabel)
        : e("button", {
          type: "button",
          className: "btn",
          disabled,
          onClick: () => applyDraft(kind, exists ? preview : meta.template)
        }, actionLabel)
    )
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
  const documents = buildNotebookDocuments(snapshot);
  const coverageCount = documents.filter((item) => item.document?.exists).length;
  const checklist = buildNotebookChecklist(snapshot);
  const disabled = !authed || notebooksState.pending || notebooksState.loading;
  const activeKind = NOTEBOOK_KIND_META[notebooksState.appendKind] || NOTEBOOK_KIND_META.learning;
  const currentDraftLength = `${notebooksState.appendText || ""}`.trim().length;
  const isHandoffPending = notebooksState.pending && notebooksState.lastAction === "handoff";
  const isAppendPending = notebooksState.pending && notebooksState.lastAction === "append";
  const syncLabel = notebooksState.loading
    ? "동기화 중"
    : notebooksState.pending
      ? "저장 중"
      : notebooksState.loaded
        ? "준비됨"
        : "대기";

  const applyDraft = (kind, content) => {
    if (kind === "handoff") {
      return;
    }

    setNotebookAppendKind(kind);
    setNotebookAppendText(mergeNotebookDraft(notebooksState.appendText, content));
  };

  const replaceDraftWithTemplate = (kind) => {
    const meta = NOTEBOOK_KIND_META[kind] || NOTEBOOK_KIND_META.learning;
    setNotebookAppendKind(kind);
    setNotebookAppendText(meta.template || "");
  };

  return e("section", { className: "panel settings-optimized-panel settings-notebooks-panel notebooks-panel notebooks-workbench-panel" },
    e("div", { className: "plans-panel-head notebook-panel-head" },
      e("div", null,
        e("h2", null, "노트북"),
        e("p", { className: "hint" }, "작업 중 배운 점, 결정, 검증 결과, handoff를 한 화면에서 정리합니다.")
      ),
      e("div", { className: "row plans-head-actions notebook-panel-actions" },
        e("button", {
          className: "btn",
          disabled: !authed || notebooksState.pending,
          onClick: () => refreshNotebook()
        }, notebooksState.loading ? "불러오는 중..." : "새로고침"),
        e("button", {
          className: "btn primary",
          disabled: !authed || notebooksState.pending,
          onClick: () => createNotebookHandoff()
        }, isHandoffPending ? "생성 중..." : "handoff 생성")
      )
    ),
    notebooksState.lastError
      ? e("div", { className: "error-banner" }, notebooksState.lastError)
      : null,
    e("div", { className: "notebook-feedback-bar" },
      e("span", { className: `tool-status-chip ${authed ? "ok" : "neutral"}` }, authed ? "세션 인증됨" : "인증 필요"),
      e("span", { className: `tool-status-chip ${notebooksState.pending ? "warn" : notebooksState.loading ? "neutral" : "ok"}` }, syncLabel),
      e("span", { className: "notebook-feedback-text" },
        notebooksState.lastMessage
          ? notebooksState.lastMessage
          : notebooksState.loading
            ? "노트북 상태를 읽는 중입니다."
            : notebooksState.pending
              ? "노트북 작업을 처리 중입니다."
              : `최근 수신 ${formatNotebookTimestamp(notebooksState.receivedAt || snapshot?.readAtUtc)}`
      )
    ),
    e("div", { className: "notebook-metric-grid" },
      renderNotebookMetricCard(e, "문서 커버리지", `${coverageCount}/4`, "배운 점, 결정, 검증, handoff 문서 구성"),
      renderNotebookMetricCard(e, "현재 작성 대상", activeKind.label, "왼쪽 작성 작업대의 저장 위치"),
      renderNotebookMetricCard(e, "초안 길이", `${currentDraftLength}자`, "기록 추가 전에 현재 초안 분량 확인"),
      renderNotebookMetricCard(e, "작성 엔진", "LLM 미사용", "handoff 생성은 현재 규칙 기반으로 동작", "neutral")
    ),
    e("div", { className: "notebook-shell" },
      e("div", { className: "notebook-primary-column" },
        e("section", { className: "notebook-workbench-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "기록 작성"),
              e("p", null, "어디에 남길지 먼저 고르고, 템플릿으로 초안을 만든 뒤 바로 저장합니다.")
            ),
            e("div", { className: "notebook-draft-meta" }, `${currentDraftLength}자`)
          ),
          e("div", { className: "notebook-project-row" },
            e("label", { className: "meta-field notebook-project-field" },
              e("span", { className: "meta-label" }, "프로젝트 키"),
              e("input", {
                className: "input",
                value: notebooksState.projectKeyDraft,
                placeholder: notebook?.projectKey || "비우면 현재 프로젝트 키 자동 사용",
                onChange: (event) => setNotebookProjectKey(event.target.value)
              })
            ),
            e("div", { className: "notebook-project-hint" },
              e("strong", null, notebook?.projectKey || "자동 결정"),
              e("span", null, notebook?.rootPath || "현재 프로젝트 루트를 기준으로 저장합니다.")
            )
          ),
          e("div", { className: "notebook-kind-grid", role: "tablist", "aria-label": "기록 종류" },
            ["learning", "decision", "verification"].map((kind) => {
              const meta = NOTEBOOK_KIND_META[kind];
              const active = notebooksState.appendKind === kind;
              return e("button", {
                key: kind,
                type: "button",
                className: `notebook-kind-card ${active ? "active" : ""}`,
                onClick: () => setNotebookAppendKind(kind)
              },
                e("strong", null, meta.label),
                e("span", null, meta.empty));
            })
          ),
          e("div", { className: "notebook-template-strip" },
            e("span", { className: "notebook-template-label" }, "빠른 템플릿"),
            ["learning", "decision", "verification"].map((kind) => e("button", {
              key: `template-${kind}`,
              type: "button",
              className: "btn ghost",
              onClick: () => replaceDraftWithTemplate(kind)
            }, NOTEBOOK_KIND_META[kind].label)),
            e("button", {
              type: "button",
              className: "btn ghost",
              onClick: () => setNotebookAppendText("")
            }, "초안 비우기")
          ),
          e("label", { className: "meta-field notebook-text-field" },
            e("span", { className: "meta-label" }, `${activeKind.label} 내용`),
            e("textarea", {
              className: "input plan-textarea notebook-textarea",
              rows: 14,
              value: notebooksState.appendText,
              placeholder: activeKind.template || "이번 세션에서 남겨야 할 핵심 내용을 입력하세요.",
              onChange: (event) => setNotebookAppendText(event.target.value)
            })
          ),
          e("div", { className: "notebook-compose-actions" },
            e("button", {
              className: "btn primary",
              disabled,
              onClick: () => appendNotebook()
            }, isAppendPending ? "저장 중..." : `${activeKind.label} 저장`),
            e("button", {
              className: "btn",
              disabled: !authed || notebooksState.pending,
              onClick: () => createNotebookHandoff()
            }, isHandoffPending ? "생성 중..." : "handoff 갱신")
          )
        ),
        e("section", { className: "notebook-source-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "빠른 기록"),
              e("p", null, "다른 패널에서 선택한 결과를 바로 노트북으로 저장합니다.")
            )
          ),
          e("div", { className: "notebook-source-grid" },
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
          )
        )
      ),
      e("aside", { className: "notebook-side-column" },
        e("section", { className: "notebook-status-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "현재 상태"),
              e("p", null, "어디에 저장되고 무엇이 비어 있는지 바로 확인합니다.")
            )
          ),
          e("div", { className: "notebook-status-list" },
            e("div", { className: "notebook-status-row" },
              e("span", null, "project key"),
              e("strong", null, notebook?.projectKey || notebooksState.projectKeyDraft || "자동 결정")
            ),
            e("div", { className: "notebook-status-row" },
              e("span", null, "저장 루트"),
              e("strong", null, trimNotebookText(notebook?.rootPath || "-", 72))
            ),
            e("div", { className: "notebook-status-row" },
              e("span", null, "최근 동기화"),
              e("strong", null, formatNotebookRelative(notebooksState.receivedAt || snapshot?.readAtUtc))
            ),
            e("div", { className: "notebook-status-row" },
              e("span", null, "handoff 생성 방식"),
              e("strong", null, "규칙 기반")
            )
          )
        ),
        e("section", { className: "notebook-next-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "다음 액션"),
              e("p", null, "비어 있는 문서부터 채우도록 권장 작업을 먼저 보여줍니다.")
            )
          ),
          e("div", { className: "notebook-next-list" },
            checklist.map((item) => e("article", { key: item.key, className: "notebook-next-item" },
              e("div", null,
                e("strong", null, item.title),
                e("p", null, item.description)
              ),
              item.kind === "handoff"
                ? e("button", {
                  className: "btn",
                  disabled: !authed || notebooksState.pending,
                  onClick: () => createNotebookHandoff()
                }, "handoff 생성")
                : e("button", {
                  className: "btn ghost",
                  type: "button",
                  onClick: () => replaceDraftWithTemplate(item.kind)
                }, `${NOTEBOOK_KIND_META[item.kind].label} 초안`)
            ))
          )
        )
      )
    ),
    e("div", { className: "notebook-documents-section" },
      e("div", { className: "notebook-section-head notebook-documents-head" },
        e("div", null,
          e("strong", null, "문서 보기"),
          e("p", null, "문서 상태를 카드 단위로 분리해 필요한 정보만 바로 읽을 수 있게 정리했습니다.")
        )
      ),
      !snapshot
        ? e("div", { className: "empty doctor-empty-state notebook-empty-state" }, "노트북을 아직 읽지 않았습니다. 상단에서 새로고침을 눌러 현재 상태를 불러오세요.")
        : e("div", { className: "notebook-documents-grid" },
          documents.map((item) => renderNotebookDocumentCard(e, {
            kind: item.key,
            document: item.document,
            applyDraft,
            createNotebookHandoff,
            disabled: !authed || notebooksState.pending,
            isHandoffPending
          }))
        )
    )
  );
}
