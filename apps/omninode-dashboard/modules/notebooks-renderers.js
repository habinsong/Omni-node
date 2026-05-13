const NOTEBOOK_KIND_META = {
  learning: {
    label: "작업 메모",
    title: "Learnings",
    empty: "오늘 작업하면서 나중에 다시 볼 만한 내용을 적어둡니다.",
    template: [
      "오늘 남길 것:",
      "- ",
      "",
      "다음에 써먹을 것:",
      "- ",
      "",
      "주의할 점:",
      "- "
    ].join("\n")
  },
  decision: {
    label: "방향 정리",
    title: "Decisions",
    empty: "이번 작업에서 어떤 방향으로 갔는지 짧게 남겨둡니다.",
    template: [
      "뭐 하기로 했나:",
      "- ",
      "",
      "왜 그렇게 갔나:",
      "- ",
      "",
      "일단 안 한 것:",
      "- "
    ].join("\n")
  },
  verification: {
    label: "확인한 내용",
    title: "Verification",
    empty: "실제로 눌러봤거나 실행해서 확인한 내용을 적어둡니다.",
    template: [
      "확인한 것:",
      "- ",
      "",
      "어떻게 확인했나:",
      "- ",
      "",
      "결과:",
      "- ",
      "",
      "아직 찝찝한 것:",
      "- "
    ].join("\n")
  },
  handoff: {
    label: "다음에 이어볼 것",
    title: "Handoff",
    empty: "다음 사람이 바로 이어서 볼 수 있게 현재 상태를 묶어둔 문서입니다."
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

function filterNotebookDocuments(documents, filterText) {
  const query = `${filterText || ""}`.trim().toLowerCase();
  if (!query) {
    return documents;
  }

  return documents.filter((item) => {
    const meta = NOTEBOOK_KIND_META[item.key] || NOTEBOOK_KIND_META.learning;
    const document = item.document || {};
    const text = [
      item.key,
      meta.label,
      meta.title,
      document.preview,
      document.content,
      document.path
    ].join("\n").toLowerCase();
    return text.includes(query);
  });
}

function buildNotebookChecklist(snapshot) {
  if (!snapshot) {
    return [
      {
        key: "load",
        title: "노트북을 먼저 불러오세요.",
        description: "현재 프로젝트에 남긴 메모를 먼저 읽어와야 이어서 쓸 수 있습니다.",
        kind: "learning",
        template: NOTEBOOK_KIND_META.learning.template
      }
    ];
  }

  const items = [];
  if (!snapshot.decisions?.exists) {
    items.push({
      key: "decision",
      title: "이번 작업 방향이 비어 있습니다.",
      description: "어디까지 했고 어디는 안 했는지만 남겨도 다음 작업이 훨씬 빨라집니다.",
      kind: "decision",
      template: NOTEBOOK_KIND_META.decision.template
    });
  }

  if (!snapshot.verification?.exists) {
    items.push({
      key: "verification",
      title: "확인한 내용이 없습니다.",
      description: "직접 눌러본 것, 실행한 것, 아직 못 본 것을 짧게 남기세요.",
      kind: "verification",
      template: NOTEBOOK_KIND_META.verification.template
    });
  }

  if (!snapshot.learnings?.exists) {
    items.push({
      key: "learning",
      title: "작업 메모가 아직 없습니다.",
      description: "반복해서 헷갈린 점이나 다음에 그대로 써먹을 내용을 적어두세요.",
      kind: "learning",
      template: NOTEBOOK_KIND_META.learning.template
    });
  }

  if (!snapshot.handoff?.exists) {
    items.push({
      key: "handoff",
      title: "이어보기 문서가 아직 없습니다.",
      description: "작업을 넘기기 전에 지금 상태를 한 번에 묶어두면 다음에 바로 시작할 수 있습니다.",
      kind: "handoff",
      template: ""
    });
  }

  if (items.length === 0) {
    items.push({
      key: "fresh",
      title: "지금 필요한 메모는 갖춰져 있습니다.",
      description: "새로 확인한 내용이 생기면 먼저 적고 이어보기 문서를 다시 만들면 됩니다.",
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
  const actionLabel = kind === "handoff" ? "이어보기 다시 만들기" : `${meta.label}로 가져오기`;
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
      kind !== "handoff"
        ? e("button", {
          type: "button",
          className: "btn ghost",
          disabled: !exists,
          onClick: () => options.setNotebookExpandedDocument(kind)
        }, "전체 보기")
        : null,
      kind === "handoff"
        ? e("button", {
          type: "button",
          className: "btn",
          disabled,
          onClick: () => createNotebookHandoff()
        }, isHandoffPending ? "만드는 중..." : actionLabel)
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

  const snapshot = notebooksState.snapshot || null;
  const notebook = snapshot?.notebook || null;
  const documents = buildNotebookDocuments(snapshot);
  const filteredDocuments = filterNotebookDocuments(documents, notebooksState.filterText);
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
        e("p", { className: "hint" }, "작업 중 남길 말, 바꾼 방향, 확인한 것, 다음에 이어볼 내용을 한 화면에서 정리합니다.")
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
        }, isHandoffPending ? "만드는 중..." : "이어보기 만들기")
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
      renderNotebookMetricCard(e, "남긴 문서", `${coverageCount}/4`, "작업 메모, 방향 정리, 확인한 내용, 이어보기"),
      renderNotebookMetricCard(e, "현재 작성 대상", activeKind.label, "왼쪽 작성 작업대의 저장 위치"),
      renderNotebookMetricCard(e, "초안 길이", `${currentDraftLength}자`, "기록 추가 전에 현재 초안 분량 확인"),
      renderNotebookMetricCard(e, "작성 방식", "직접 작성", "이어보기 만들기는 현재 규칙 기반으로 동작", "neutral")
    ),
    e("div", { className: "notebook-shell" },
      e("div", { className: "notebook-primary-column" },
        e("section", { className: "notebook-workbench-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "기록 작성"),
              e("p", null, "일기 쓰듯이 남기고, 필요할 때만 템플릿을 불러옵니다.")
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
              e("strong", null, notebook?.projectKey || "자동 선택"),
              e("span", null, notebook?.rootPath || "현재 프로젝트 루트를 기준으로 저장합니다.")
            )
          ),
          e("div", { className: "notebook-kind-grid", role: "tablist", "aria-label": "기록 방식" },
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
            e("span", { className: "notebook-template-label" }, "필요하면 시작 문장 넣기"),
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
            }, "비우기")
          ),
          e("label", { className: "meta-field notebook-text-field" },
            e("span", { className: "meta-label" }, activeKind.label),
            e("textarea", {
              className: "input plan-textarea notebook-textarea",
              rows: 14,
              value: notebooksState.appendText,
              placeholder: activeKind.template || "다음에 다시 볼 사람이 이해할 수 있게 현재 상태를 적어두세요.",
              onChange: (event) => setNotebookAppendText(event.target.value)
            })
          ),
          e("div", { className: "notebook-compose-actions" },
            e("button", {
              className: "btn primary",
              disabled,
              onClick: () => appendNotebook()
            }, isAppendPending ? "저장 중..." : "저장"),
            e("button", {
              className: "btn",
              disabled: !authed || notebooksState.pending,
              onClick: () => createNotebookHandoff()
            }, isHandoffPending ? "만드는 중..." : "이어보기 갱신")
          )
        ),
        e("section", { className: "notebook-source-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "빠른 기록"),
              e("p", null, "다른 화면에서 나온 결과를 사람이 읽을 수 있는 초안으로 가져옵니다.")
            )
          ),
          e("div", { className: "notebook-source-grid" },
            e("button", {
              className: "btn",
              disabled: !authed || notebooksState.pending,
              onClick: appendSelectedPlanDecision
            }, "선택한 계획 가져오기"),
            e("button", {
              className: "btn",
              disabled: !authed || notebooksState.pending,
              onClick: appendSelectedTaskVerification
            }, "그래프 결과 가져오기"),
            e("button", {
              className: "btn",
              disabled: !authed || notebooksState.pending,
              onClick: appendDoctorVerification
            }, "진단 결과 가져오기"),
            e("button", {
              className: "btn",
              disabled: !authed || notebooksState.pending,
              onClick: appendRefactorVerification
            }, "리팩터 결과 가져오기")
          )
        )
      ),
      e("aside", { className: "notebook-side-column" },
        e("section", { className: "notebook-status-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "현재 상태"),
              e("p", null, "어디에 저장되는지, 뭐가 아직 비었는지 확인합니다.")
            )
          ),
          e("div", { className: "notebook-status-list" },
            e("div", { className: "notebook-status-row" },
              e("span", null, "project key"),
              e("strong", null, notebook?.projectKey || notebooksState.projectKeyDraft || "자동 선택")
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
              e("span", null, "이어보기 생성"),
              e("strong", null, "규칙 기반")
            )
          )
        ),
        e("section", { className: "notebook-next-card" },
          e("div", { className: "notebook-section-head" },
            e("div", null,
              e("strong", null, "다음 액션"),
              e("p", null, "뭘 먼저 적으면 좋은지 알려줍니다.")
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
                }, "이어보기 만들기")
                : e("button", {
                  className: "btn ghost",
                  type: "button",
                  onClick: () => replaceDraftWithTemplate(item.kind)
                }, `${NOTEBOOK_KIND_META[item.kind].label} 시작`)
            ))
          )
        )
      )
    ),
    e("div", { className: "notebook-documents-section" },
      e("div", { className: "notebook-section-head notebook-documents-head" },
        e("div", null,
          e("strong", null, "문서 보기"),
          e("p", null, "나중에 다시 볼 내용을 카드별로 나눠서 보여줍니다.")
        ),
        e("label", { className: "meta-field notebook-search-field" },
          e("span", { className: "meta-label" }, "검색"),
          e("input", {
            className: "input",
            value: notebooksState.filterText || "",
            placeholder: "결정, 파일명, 키워드 검색",
            onChange: (event) => setNotebookFilterText(event.target.value)
          })
        )
      ),
      !snapshot
        ? e("div", { className: "empty doctor-empty-state notebook-empty-state" }, "노트북을 아직 읽지 않았습니다. 상단에서 새로고침을 눌러 현재 상태를 불러오세요.")
        : e("div", { className: "notebook-documents-grid" },
          filteredDocuments.map((item) => renderNotebookDocumentCard(e, {
            kind: item.key,
            document: item.document,
            applyDraft,
            createNotebookHandoff,
            disabled: !authed || notebooksState.pending,
            isHandoffPending,
            setNotebookExpandedDocument
          }))
        ),
      notebooksState.expandedDocument && snapshot
        ? e("div", {
          className: "modal notebook-document-modal",
          onClick: () => setNotebookExpandedDocument("")
        },
          e("div", {
            className: "support-overlay-shell notebook-document-modal-shell",
            onClick: (event) => event.stopPropagation()
          },
            (() => {
              const selected = documents.find((item) => item.key === notebooksState.expandedDocument);
              const meta = NOTEBOOK_KIND_META[selected?.key] || NOTEBOOK_KIND_META.learning;
              const document = selected?.document || {};
              const fullText = `${document.content || document.preview || ""}`.trim();
              return [
                e("div", { key: "head", className: "coding-result-head" },
                  e("strong", null, `${meta.label} 전체 보기`),
                  e("button", {
                    type: "button",
                    className: "btn ghost",
                    onClick: () => setNotebookExpandedDocument("")
                  }, "닫기")
                ),
                document.contentTruncated
                  ? e("div", { key: "truncated", className: "hint" }, "문서가 길어 최근 20,000자만 표시합니다.")
                  : null,
                e("pre", { key: "content", className: "notebook-document-full-content" }, fullText || "표시할 내용이 없습니다.")
              ];
            })()
          )
        )
        : null
    )
  );
}

export { NOTEBOOK_KIND_META };
