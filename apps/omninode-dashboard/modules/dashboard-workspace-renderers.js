import { formatRoutineAgentToolProfileLabel } from "./routine-utils.js";

function renderPaperclipIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "M9.5 19.5 17 12a4.5 4.5 0 1 0-6.364-6.364L3.5 12.772a6.5 6.5 0 0 0 9.192 9.192L19 15.656",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    })
  );
}

function renderSendIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "M21 3 10 14",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    }),
    e("path", {
      d: "m21 3-7 18-4-7-7-4 18-7Z",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    })
  );
}

function renderMicIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3Z",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    }),
    e("path", {
      d: "M5 11a7 7 0 0 0 14 0M12 18v3M8 21h8",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    })
  );
}

function renderThinkPlusIcon(e) {
  // 작은 뇌 도형 + 우상단 + 마크
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "M9 5.5C7 5.5 5.5 7 5.5 9c0 .8.3 1.5.8 2-.5.5-.8 1.2-.8 2 0 1.4.9 2.6 2.2 3-.1.3-.2.6-.2 1 0 1.5 1.3 2.8 2.8 2.8.6 0 1.2-.2 1.6-.5v-13c-.5-.5-1.2-.8-1.9-.8Z",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.6",
      strokeLinejoin: "round"
    }),
    e("path", {
      d: "M15 5.5c2 0 3.5 1.5 3.5 3.5 0 .8-.3 1.5-.8 2 .5.5.8 1.2.8 2 0 1.4-.9 2.6-2.2 3 .1.3.2.6.2 1 0 1.5-1.3 2.8-2.8 2.8-.6 0-1.2-.2-1.6-.5v-13c.5-.5 1.2-.8 1.9-.8Z",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.6",
      strokeLinejoin: "round"
    }),
    e("circle", { cx: "19.5", cy: "5.5", r: "3.2", fill: "currentColor", className: "think-plus-badge" }),
    e("path", {
      d: "M19.5 4v3M18 5.5h3",
      stroke: "var(--surface, #ffffff)",
      strokeWidth: "1.4",
      strokeLinecap: "round"
    })
  );
}

function renderRoutineIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "M8 3v3M16 3v3M4 8h16",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round",
      strokeLinejoin: "round"
    }),
    e("rect", {
      x: "4",
      y: "5",
      width: "16",
      height: "16",
      rx: "2.5",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9"
    }),
    e("path", {
      d: "M9 13h6M9 17h3",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round"
    })
  );
}

function renderPlanIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "M5 5h14M5 12h14M5 19h14",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.9",
      strokeLinecap: "round"
    }),
    e("path", {
      d: "M8 5v14M16 5v14",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "1.4",
      strokeLinecap: "round",
      opacity: "0.75"
    }),
    e("circle", {
      cx: "5",
      cy: "5",
      r: "1.5",
      fill: "currentColor"
    }),
    e("circle", {
      cx: "5",
      cy: "12",
      r: "1.5",
      fill: "currentColor"
    }),
    e("circle", {
      cx: "5",
      cy: "19",
      r: "1.5",
      fill: "currentColor"
    })
  );
}

function renderCloseIcon(e) {
  return e(
    "svg",
    { viewBox: "0 0 24 24", className: "icon-svg", "aria-hidden": "true" },
    e("path", {
      d: "M6 6l12 12M18 6 6 18",
      fill: "none",
      stroke: "currentColor",
      strokeWidth: "2",
      strokeLinecap: "round"
    })
  );
}

function renderRichInputControls(props) {
  const {
    e,
    attachments,
    attachmentDragActive,
    handleAttachmentDragOver,
    handleAttachmentDrop,
    attachmentFileInputId,
    onAttachmentSelected,
    onClearAttachments
  } = props;

  return e(
    "div",
    { className: "rich-input messenger-rich-input" },
    e(
      "div",
      {
        className: `rich-input-row compact-attachment-row ${attachmentDragActive ? "drag-active" : ""}`,
        onDragOver: handleAttachmentDragOver,
        onDrop: handleAttachmentDrop
      },
      attachmentDragActive
        ? e("div", { className: "attachment-drop-message" }, "여기에 파일 추가")
        : [
            e("input", {
              key: "file-input",
              id: attachmentFileInputId,
              type: "file",
              className: "file-upload-input",
              onChange: onAttachmentSelected,
              multiple: true,
              tabIndex: -1,
              "aria-hidden": "true",
              accept: "image/*,.pdf,.txt,.md,.json,.csv,.log,.py,.js,.ts,.java,.kt,.c,.cpp,.cs,.html,.css,.sh,.yaml,.yml,.xml"
            }),
            e("label", {
              key: "file-button",
              className: "btn ghost file-upload-label",
              htmlFor: attachmentFileInputId
            }, "파일 추가"),
            attachments.length > 0
              ? e("div", { key: "summary", className: "attachment-compact-summary" }, `첨부 ${attachments.length}개`)
              : e("div", { key: "summary-empty", className: "attachment-compact-summary empty" }, "첨부 없음"),
            attachments.length > 0
              ? e("button", {
                  key: "clear",
                  className: "btn ghost attachment-clear-btn",
                  onClick: onClearAttachments
                }, "비우기")
              : null
          ]
    )
  );
}

export function renderComposerInputBar(props) {
  const {
    e,
    value,
    onChange,
    onSend,
    pending,
    placeholder,
    attachmentPanelVisible,
    toggleAttachmentPanel,
    autoResizeComposerTextarea,
    onInputKeyDown,
    attachments,
    attachmentDragActive,
    handleAttachmentDragOver,
    handleAttachmentDrop,
    attachmentFileInputId,
    onAttachmentSelected,
    onClearAttachments,
    selectedSkill,
    onClearSkill,
    speechState,
    onStartVoiceInput,
    thinkPlusVisible,
    thinkPlusEnabled,
    thinkPlusReady,
    onToggleThinkPlus,
    onCreateRoutine,
    onCreatePlan
  } = props;
  const skillName = `${selectedSkill?.name || ""}`.trim();
  const skillScope = `${selectedSkill?.scope || "project"}`.trim() || "project";
  const speechSupported = speechState ? speechState.supported !== false : true;
  const speechActive = !!speechState?.active;

  return e(
    "div",
    { className: "composer-message-stack" },
    skillName
      ? e("div", { className: "composer-skill-strip" },
        e("div", { className: "composer-skill-copy" },
          e("span", { className: `composer-skill-scope ${skillScope}` }, skillScope === "global" ? "전역 스킬" : "프로젝트 스킬"),
          e("strong", null, skillName)
        ),
        e("button", {
          type: "button",
          className: "composer-skill-clear",
          title: "스킬 해제",
          onClick: onClearSkill
        }, renderCloseIcon(e))
      )
      : null,
    speechState?.error
      ? e("div", { className: "composer-speech-error" }, speechState.error)
      : null,
    e("div", { className: "composer-input-shell" },
      e("textarea", {
        className: "textarea composer-main-input",
        rows: 1,
        value,
        ref: (node) => autoResizeComposerTextarea(node),
        onInput: (event) => autoResizeComposerTextarea(event.target),
        onChange,
        onKeyDown: (event) => onInputKeyDown(event, onSend),
        placeholder
      }),
      e("div", { className: "composer-side-actions" },
        e("button", {
          type: "button",
          className: `composer-icon-btn mic ${speechActive ? "active recording" : ""}`,
          title: speechActive ? "음성 입력 중지" : (speechSupported ? "음성 입력" : "이 브라우저는 음성 입력을 지원하지 않습니다."),
          onClick: onStartVoiceInput,
          disabled: (pending && !speechActive) || !speechSupported,
          "aria-pressed": speechActive ? "true" : "false"
        }, renderMicIcon(e)),
        e("button", {
          type: "button",
          className: `composer-icon-btn attach ${attachmentPanelVisible ? "active" : ""}`,
          title: attachmentPanelVisible ? "첨부 패널 닫기" : "첨부 패널 열기",
          onClick: toggleAttachmentPanel
        }, renderPaperclipIcon(e)),
        thinkPlusVisible && typeof onToggleThinkPlus === "function"
          ? e("button", {
              type: "button",
              className: `composer-icon-btn think-plus ${thinkPlusEnabled ? "active" : ""}`,
              title: thinkPlusReady === false
                ? "Think+ 모드 (Gemini API 키가 필요합니다)"
                : (thinkPlusEnabled ? "Think+ 모드 끄기 (현재 ON)" : "Think+ 모드 켜기 (최신 웹 검색 + 추론)"),
              onClick: onToggleThinkPlus,
              disabled: thinkPlusReady === false,
              "aria-pressed": thinkPlusEnabled ? "true" : "false"
            }, renderThinkPlusIcon(e))
          : null,
        typeof onCreateRoutine === "function"
          ? e("button", {
              type: "button",
              className: "composer-icon-btn routine",
              title: "현재 입력을 루틴으로 저장",
              onClick: onCreateRoutine,
              disabled: pending || !`${value || ""}`.trim()
            }, renderRoutineIcon(e))
          : null,
        typeof onCreatePlan === "function"
          ? e("button", {
              type: "button",
              className: "composer-icon-btn plan",
              title: "현재 입력으로 작업계획 만들기",
              onClick: onCreatePlan,
              disabled: pending || !`${value || ""}`.trim()
            }, renderPlanIcon(e))
          : null,
        e("button", {
          type: "button",
          className: "composer-icon-btn send",
          title: "전송",
          onClick: onSend,
          disabled: pending
        }, renderSendIcon(e))
      )
    ),
    attachmentPanelVisible
      ? renderRichInputControls({
          e,
          attachments,
          attachmentDragActive,
          handleAttachmentDragOver,
          handleAttachmentDrop,
          attachmentFileInputId,
          onAttachmentSelected,
          onClearAttachments
        })
      : null
  );
}

function normalizeCarouselIndex(index, entryCount) {
  if (!Number.isFinite(entryCount) || entryCount <= 0) {
    return 0;
  }

  const numericIndex = Number.isFinite(Number(index)) ? Number(index) : 0;
  const normalized = numericIndex % entryCount;
  return normalized < 0 ? normalized + entryCount : normalized;
}

function buildCodingWorkerMessageBody(worker) {
  const execution = worker && typeof worker.execution === "object" ? worker.execution : {};
  const changedFiles = Array.isArray(worker && worker.changedFiles) ? worker.changedFiles.filter(Boolean) : [];
  const stdout = `${execution.stdout || ""}`.trim();
  const stderr = `${execution.stderr || ""}`.trim();
  const command = `${execution.command || ""}`.trim();
  const summary = `${worker && worker.summary ? worker.summary : ""}`.trim();
  const lines = [
    worker && worker.role ? `역할: ${worker.role}` : "",
    `상태: ${execution.status || "-"}`,
    `종료 코드: ${Number.isFinite(execution.exitCode) ? execution.exitCode : "-"}`,
    `언어: ${worker && worker.language ? worker.language : "-"}`,
    `변경 파일: ${changedFiles.length}개`
  ].filter(Boolean);

  if (command && command !== "(none)" && command !== "-") {
    lines.push(`실행 명령: \`${command}\``);
  }
  if (changedFiles.length > 0) {
    lines.push(`파일: ${changedFiles.join(", ")}`);
  }
  if (summary) {
    lines.push(`요약:\n${summary}`);
  }
  if (stdout) {
    lines.push(`stdout:\n\`\`\`\n${stdout}\n\`\`\``);
  }
  if (stderr) {
    lines.push(`stderr:\n\`\`\`\n${stderr}\n\`\`\``);
  }
  if (!stdout && !stderr) {
    lines.push("실행 출력은 없습니다.");
  }

  return lines.join("\n\n");
}

function buildCodingResultCarouselEntries(result, sanitizeCodingAssistantText) {
  if (result && result.mode === "multi" && Array.isArray(result.workers)) {
    return result.workers
      .filter((worker) => worker && typeof worker === "object")
      .map((worker, index) => {
        const workerExecution = worker.execution && typeof worker.execution === "object" ? worker.execution : {};
        return {
          id: `worker-${worker.provider || "unknown"}-${index}`,
          heading: `${worker.provider || "-"} (${worker.model || "-"})`,
          meta: `${worker.role || "독립 완주"} · status=${workerExecution.status || "-"} · exit=${Number.isFinite(workerExecution.exitCode) ? workerExecution.exitCode : "-"}`,
          body: buildCodingWorkerMessageBody(worker),
          tone: /(error|fail|timeout|cancel|killed|aborted)/i.test(`${workerExecution.status || ""}`) ? "error" : "ok"
        };
      });
  }

  const execution = result && typeof result.execution === "object" ? result.execution : {};
  return [{
    id: "final",
    heading: `${result.provider || "-"} (${result.model || "-"})`,
    meta: `최종 구현 · status=${execution.status || "-"} · exit=${Number.isFinite(execution.exitCode) ? execution.exitCode : "-"}`,
    body: sanitizeCodingAssistantText(result.summary || "") || "최종 요약이 없습니다.",
    tone: /(error|fail|timeout|cancel|killed|aborted)/i.test(`${execution.status || ""}`) ? "error" : "ok"
  }];
}

function buildCodingResultState(result) {
  const execution = result && typeof result.execution === "object" ? result.execution : {};
  const changedFiles = Array.isArray(result?.changedFiles) ? result.changedFiles.filter(Boolean) : [];
  const workerCount = Array.isArray(result?.workers) ? result.workers.filter(Boolean).length : 0;
  const status = `${execution.status || ""}`.trim() || "unknown";
  const normalized = status.toLowerCase();
  const tone = /(error|fail|timeout|cancel|killed|aborted)/i.test(normalized)
    ? "error"
    : /(success|ok|completed|done)/i.test(normalized)
      ? "ok"
      : "neutral";
  const subtitle = result?.mode === "multi"
    ? `비교 요약 ${result?.provider || "-"}/${result?.model || "-"} · 워커 ${workerCount}개`
    : `${result?.provider || "-"}/${result?.model || "-"} · ${result?.language || "-"}`;
  const detail = [
    `status=${status}`,
    `exit=${execution.exitCode ?? "-"}`,
    result?.mode === "multi" ? `워커 ${workerCount}개` : "",
    `파일 ${changedFiles.length}개`
  ].filter(Boolean).join(" · ");

  return {
    tone,
    statusText: status,
    subtitle,
    detail,
    changedFiles,
    execution
  };
}

function normalizeExecutionText(value) {
  return `${value || ""}`.replace(/\r\n/g, "\n").trim();
}

function formatExecutionTimestamp(updatedAt) {
  if (!updatedAt) {
    return "";
  }

  const date = new Date(updatedAt);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  return date.toLocaleString("ko-KR", {
    hour12: false,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

function renderExecutionDetails(props) {
  const {
    e,
    execution,
    updatedAt,
    humanPath,
    runDir,
    emptyMessage
  } = props;

  const normalizedExecution = execution && typeof execution === "object" ? execution : {};
  const stdout = normalizeExecutionText(normalizedExecution.stdout);
  const stderr = normalizeExecutionText(normalizedExecution.stderr);
  const runtimeDir = `${normalizedExecution.runDirectory || runDir || ""}`.trim();
  const entryFile = `${normalizedExecution.entryFile || ""}`.trim();
  const executedAt = formatExecutionTimestamp(updatedAt);
  const detailItems = [
    {
      key: "status",
      label: "상태",
      value: `${normalizedExecution.status || "-"} · exit=${Number.isFinite(normalizedExecution.exitCode) ? normalizedExecution.exitCode : "-"}`
    },
    executedAt
      ? {
          key: "executed-at",
          label: "실행 시각",
          value: executedAt
        }
      : null,
    {
      key: "command",
      label: "명령",
      value: `${normalizedExecution.command || "(none)"}`
    },
    runtimeDir
      ? {
          key: "run-directory",
          label: "작업 폴더",
          value: runtimeDir
        }
      : null,
    entryFile
      ? {
          key: "entry-file",
          label: "엔트리 파일",
          value: humanPath(entryFile, runtimeDir || runDir || "")
        }
      : null
  ].filter(Boolean);

  return e("div", { className: "coding-execution-details" },
    detailItems.length > 0
      ? e("div", { className: "coding-execution-meta-grid" },
        detailItems.map((item) => e("div", {
          key: item.key,
          className: `coding-execution-meta-card${item.key === "command" || item.key === "run-directory" ? " is-wide" : ""}`
        },
        e("div", { className: "coding-execution-meta-label" }, item.label),
        e("div", { className: "coding-execution-meta-value" }, item.value)
        ))
      )
      : null,
    stdout
      ? e("div", { className: "coding-execution-output-block" },
        e("div", { className: "coding-execution-output-head" }, "stdout"),
        e("pre", { className: "coding-execution-output-content" }, stdout)
      )
      : null,
    stderr
      ? e("div", { className: "coding-execution-output-block is-error" },
        e("div", { className: "coding-execution-output-head" }, "stderr"),
        e("pre", { className: "coding-execution-output-content" }, stderr)
      )
      : null,
    !stdout && !stderr
      ? e("div", { className: "coding-execution-empty" }, emptyMessage || "이번 실행에서는 stdout/stderr가 비어 있습니다.")
      : null
  );
}

function ResultMessageCarousel(props) {
  const { useEffect, useState } = React;
  const {
    e,
    MarkdownBubbleText,
    title,
    subtitle,
    emptyText,
    entries,
    avatarLabel,
    avatarClassName,
    containerElement,
    className,
    headerAction
  } = props;
  const [selectedIndex, setSelectedIndex] = useState(0);
  const safeEntries = Array.isArray(entries) ? entries.filter(Boolean) : [];
  const containerTag = containerElement || "section";
  const containerClassName = className || "coding-result support-card result-carousel-card";

  useEffect(() => {
    setSelectedIndex((prev) => normalizeCarouselIndex(prev, safeEntries.length));
  }, [safeEntries.length]);

  if (safeEntries.length === 0) {
    return e(containerTag, { className: containerClassName },
      e("div", { className: "coding-result-head" },
        e("strong", null, title),
        subtitle ? e("span", null, subtitle) : null
      ),
      e("div", { className: "empty-line" }, emptyText || "표시할 결과가 없습니다.")
    );
  }

  const activeIndex = normalizeCarouselIndex(selectedIndex, safeEntries.length);
  const activeEntry = safeEntries[activeIndex];
  const canNavigate = safeEntries.length > 1;
  const bubbleClassName = `bubble assistant result-carousel-bubble${activeEntry.tone === "error" ? " is-error" : ""}`;

  return e(
    containerTag,
    { className: containerClassName },
    e("div", { className: "result-carousel-head" },
      e("div", { className: "result-carousel-copy" },
        e("strong", null, title),
        subtitle ? e("span", { className: "result-carousel-subtitle" }, subtitle) : null
      ),
      e("div", { className: "result-carousel-head-actions" },
        headerAction || null,
        e("div", { className: "result-carousel-nav" },
          e("button", {
            type: "button",
            className: "btn ghost result-carousel-nav-btn",
            onClick: () => setSelectedIndex((prev) => normalizeCarouselIndex(prev - 1, safeEntries.length)),
            disabled: !canNavigate,
            title: "이전 모델"
          }, "<"),
          e("div", { className: "result-carousel-current" },
            e("div", { className: "result-carousel-current-label" }, activeEntry.heading || "-"),
            e("div", { className: "result-carousel-current-index" }, `${activeIndex + 1} / ${safeEntries.length}`)
          ),
          e("button", {
            type: "button",
            className: "btn ghost result-carousel-nav-btn",
            onClick: () => setSelectedIndex((prev) => normalizeCarouselIndex(prev + 1, safeEntries.length)),
            disabled: !canNavigate,
            title: "다음 모델"
          }, ">")
        )
      )
    ),
    e("div", { className: "message-list result-carousel-message-list" },
      e("div", { className: "message-row assistant result-carousel-row" },
        e("div", { className: `message-avatar ${avatarClassName}` }, avatarLabel),
        e("div", { className: bubbleClassName },
          activeEntry.meta ? e("div", { className: "bubble-meta" }, activeEntry.meta) : null,
          e(MarkdownBubbleText, { text: activeEntry.body || "-" })
        )
      )
    )
  );
}

function renderChatMultiSummaryCard(props) {
  const {
    e,
    MarkdownBubbleText,
    summarySections
  } = props;
  const sections = Array.isArray(summarySections) ? summarySections.filter(Boolean) : [];
  if (sections.length === 0) {
    return null;
  }

  return e(
    "section",
    { className: "support-card chat-multi-summary-card" },
    e("div", { className: "coding-result-head" },
      e("strong", null, "공통 정리")
    ),
    e("div", { className: "chat-multi-summary-grid" },
      sections.map((section) => e(
        "div",
        { key: `chat-multi-summary-${section.key || section.title}`, className: "chat-multi-summary-block" },
        e("div", { className: "chat-multi-summary-title" }, section.title || "-"),
        e("div", { className: "chat-multi-summary-body markdown-panel" },
          e(MarkdownBubbleText, { text: section.body || "-" })
        )
      ))
    )
  );
}

function renderCodingMultiSummaryCard(props) {
  const {
    e,
    MarkdownBubbleText,
    summarySections
  } = props;
  const sections = Array.isArray(summarySections) ? summarySections.filter(Boolean) : [];
  if (sections.length === 0) {
    return null;
  }

  return e(
    "section",
    { className: "support-card coding-multi-summary-card" },
    e("div", { className: "coding-result-head" },
      e("strong", null, "공통 정리")
    ),
    e("div", { className: "chat-multi-summary-grid" },
      sections.map((section) => e(
        "div",
        { key: `coding-multi-summary-${section.key || section.title}`, className: "chat-multi-summary-block" },
        e("div", { className: "chat-multi-summary-title" }, section.title || "-"),
        e("div", { className: "chat-multi-summary-body markdown-panel" },
          e(MarkdownBubbleText, { text: section.body || "-" })
        )
      ))
    )
  );
}

function renderCodingResultCard(props) {
  const {
    e,
    MarkdownBubbleText,
    currentConversationId,
    result,
    codingState,
    filePreviewByConversation,
    runtimeByConversation,
    executionInputByConversation,
    showExecutionLogsByConversation,
    sanitizeCodingAssistantText,
    buildCodingMultiRenderSnapshot,
    requestWorkspaceFilePreview,
    requestLatestCodingResultExecution,
    humanPath,
    setCodingExecutionInputByConversation,
    setShowExecutionLogsByConversation,
    panelClassName,
    headerAction,
    appendLatestCodingResultToNotebook,
    createPlanFromLatestCodingResult,
    notebookPending
  } = props;

  const changedFiles = codingState.changedFiles;
  const preview = filePreviewByConversation[currentConversationId] || null;
  const runtime = runtimeByConversation[currentConversationId] || null;
  const executionInput = `${executionInputByConversation?.[currentConversationId] || ""}`;
  const runDir = codingState.execution.runDirectory || "";
  const showExecutionLogs = !!showExecutionLogsByConversation[currentConversationId];
  const canExecute = !!currentConversationId && typeof requestLatestCodingResultExecution === "function";
  const canEditExecutionInput = !!currentConversationId && typeof setCodingExecutionInputByConversation === "function";
  const safeSummary = sanitizeCodingAssistantText(result.summary || "");
  const multiSnapshot = result.mode === "multi" && typeof buildCodingMultiRenderSnapshot === "function"
    ? buildCodingMultiRenderSnapshot(result)
    : null;
  const carouselEntries = result.mode === "multi"
    ? (multiSnapshot && Array.isArray(multiSnapshot.entries) && multiSnapshot.entries.length > 0
        ? multiSnapshot.entries
        : buildCodingResultCarouselEntries(result, sanitizeCodingAssistantText))
    : [];
  const actionButtons = [];
  if (canExecute) {
    actionButtons.push(e("button", {
      key: "execute",
      type: "button",
      className: "btn secondary coding-execute-btn",
      onClick: () => requestLatestCodingResultExecution(currentConversationId, executionInput),
      disabled: runtime?.pending
    }, runtime?.pending ? "실행 중..." : "실행"));
  }
  if (typeof appendLatestCodingResultToNotebook === "function") {
    actionButtons.push(e("button", {
      key: "notebook",
      type: "button",
      className: "btn secondary",
      disabled: !!notebookPending,
      onClick: appendLatestCodingResultToNotebook
    }, notebookPending ? "저장 중..." : "노트북 저장"));
  }
  if (typeof createPlanFromLatestCodingResult === "function") {
    actionButtons.push(e("button", {
      key: "plan",
      type: "button",
      className: "btn secondary",
      onClick: createPlanFromLatestCodingResult
    }, "계획 만들기"));
  }
  if (headerAction) {
    actionButtons.push(e("span", { key: "header-action", className: "coding-result-inline-action" }, headerAction));
  }
  const mergedHeaderAction = actionButtons.length > 0
    ? e("div", { className: "coding-result-inline-actions" }, actionButtons)
    : null;
  const filesPanel = changedFiles.length > 0
    ? e("div", { className: "coding-files" },
      e("div", { className: "coding-files-head" }, `생성/수정 파일 (${changedFiles.length})`),
      e("div", { className: "coding-files-list" },
        changedFiles.map((pathValue) => {
          const selected = preview?.path === pathValue;
          return e(
            "button",
            {
              key: pathValue,
              className: `file-chip ${selected ? "active" : ""}`,
              onClick: () => requestWorkspaceFilePreview(pathValue, currentConversationId)
            },
            humanPath(pathValue, runDir)
          );
        })
      )
    )
    : e("div", { className: "coding-files empty-line" }, "변경 파일이 감지되지 않았습니다.");
  const previewPanel = preview
    ? e("div", { className: "coding-preview" },
      e("div", { className: "coding-preview-head" }, `프리뷰: ${humanPath(preview.path, runDir)}`),
      e("pre", { className: "coding-preview-content" }, preview.content || "")
    )
    : e("div", { className: "coding-preview empty-line" }, "파일 프리뷰를 불러오는 중이거나 아직 선택된 파일이 없습니다.");
  const runtimePanel = runtime
    ? e("div", {
      className: `coding-runtime-panel${runtime.ok === false ? " is-error" : ""}${runtime.pending ? " is-pending" : ""}`
    },
    e("div", { className: "coding-preview-head" },
      runtime.runMode === "browser" ? "브라우저 실행" : "실행 결과"
    ),
    runtime.message
      ? e("div", { className: "coding-runtime-message" }, runtime.message)
      : null,
    runtime.runMode === "browser" && runtime.previewUrl
      ? e("div", { className: "coding-runtime-scroll" },
          e("div", { className: "coding-runtime-browser-bar" },
            runtime.previewEntry
              ? e("span", { className: "coding-runtime-target" }, runtime.previewEntry)
              : null,
            e("a", {
              className: "btn ghost",
              href: runtime.previewUrl,
              target: "_blank",
              rel: "noreferrer"
            }, "새 탭")
          ),
          e("iframe", {
            key: `browser-frame-${runtime.previewUrl}-${runtime.updatedAt || 0}`,
            className: "coding-browser-frame",
            src: runtime.previewUrl,
            title: "최근 코딩 결과 브라우저 실행"
          })
        )
      : runtime.execution
        ? e("div", { className: "coding-runtime-scroll" },
            renderExecutionDetails({
              e,
              execution: runtime.execution,
              updatedAt: runtime.updatedAt,
              humanPath,
              runDir,
              emptyMessage: "재실행은 완료됐지만 stdout/stderr가 비어 있습니다. 입력형 CLI라면 아래 stdin 입력 칸에 값을 넣고 다시 실행하세요."
            })
          )
        : e("div", { className: "coding-preview empty-line" }, runtime.pending ? "최근 결과를 다시 실행하고 있습니다." : "실행 정보를 표시할 수 없습니다.")
    )
    : null;
  const executionInputPanel = canExecute
    ? e("div", { className: "coding-execution-input-panel" },
      e("div", { className: "coding-execution-input-head" }, "stdin 입력"),
      e("div", { className: "coding-execution-input-help" }, "입력이 필요한 콘솔 프로그램은 값을 줄바꿈으로 넣고 실행하세요."),
      e("textarea", {
        className: "coding-execution-input-textarea",
        value: executionInput,
        placeholder: "예시\n1\n12\n3",
        rows: 4,
        onChange: (event) => {
          if (!canEditExecutionInput) {
            return;
          }

          const nextValue = event && event.target ? event.target.value : "";
          setCodingExecutionInputByConversation((prev) => ({
            ...prev,
            [currentConversationId]: nextValue
          }));
        }
      }),
      e("div", { className: "coding-execution-input-actions" },
        e("button", {
          type: "button",
          className: "btn secondary",
          disabled: runtime?.pending,
          onClick: () => requestLatestCodingResultExecution(currentConversationId, executionInput)
        }, runtime?.pending ? "실행 중..." : "입력 포함 실행"),
        e("button", {
          type: "button",
          className: "btn ghost",
          disabled: runtime?.pending || !executionInput,
          onClick: () => {
            if (!canEditExecutionInput) {
              return;
            }

            setCodingExecutionInputByConversation((prev) => {
              if (!prev || !prev[currentConversationId]) {
                return prev;
              }

              return {
                ...prev,
                [currentConversationId]: ""
              };
            });
          }
        }, "비우기")
      )
    )
    : null;
  const executionLogsPanel = e("div", { className: "coding-output-panel" },
    e("div", { className: "coding-output-panel-head" },
      e("div", { className: "coding-preview-head coding-output-panel-title" }, "생성 단계 실행 로그"),
      e("div", { className: "coding-log-controls" },
        e("button", {
          className: "btn ghost",
          onClick: () => setShowExecutionLogsByConversation((prev) => ({
            ...prev,
            [currentConversationId]: !showExecutionLogs
          }))
        }, showExecutionLogs ? "숨기기" : "보기")
      )
    ),
    showExecutionLogs
      ? renderExecutionDetails({
          e,
          execution: codingState.execution,
          humanPath,
          runDir,
          emptyMessage: "생성 단계에서는 stdout/stderr가 남지 않았습니다."
        })
      : e("div", { className: "coding-output empty-line" }, "생성 단계 실행 로그는 숨김 상태입니다.")
  );
  const contentGrid = e("div", { className: "coding-result-body-grid" },
    e("div", { className: "coding-result-primary-column" },
      filesPanel,
      previewPanel
    ),
    e("div", { className: "coding-result-secondary-column" },
      runtimePanel,
      executionLogsPanel
    )
  );

  return e(
    "section",
    { className: `coding-result support-card ${panelClassName || ""}`.trim() },
    result.mode === "multi"
      ? e(ResultMessageCarousel, {
          key: `coding-carousel-${currentConversationId}`,
          e,
          MarkdownBubbleText,
          title: "최근 코딩 결과",
          subtitle: codingState.subtitle,
          emptyText: "표시할 워커 결과가 없습니다.",
          entries: carouselEntries,
          avatarLabel: "DEV",
          avatarClassName: "coding",
          containerElement: "div",
          className: "result-carousel-card result-carousel-inline",
          headerAction: mergedHeaderAction
        })
      : [
          e("div", { key: "head", className: "coding-result-head" },
            e("strong", null, "최근 코딩 결과"),
            e("div", { className: "coding-result-head-actions" },
              e("span", null, codingState.subtitle),
              mergedHeaderAction
            )
          ),
          safeSummary
            ? e("div", { key: "summary", className: "coding-summary markdown-panel" },
              e(MarkdownBubbleText, { text: safeSummary })
            )
            : null
        ],
    result.mode === "multi" && multiSnapshot
      ? renderCodingMultiSummaryCard({
          e,
          MarkdownBubbleText,
          summarySections: multiSnapshot.summarySections
        })
      : null,
    e("div", { className: "coding-result-meta" },
      `status=${codingState.execution.status || "-"} · exit=${codingState.execution.exitCode ?? "-"} · command=${codingState.execution.command || "(none)"}`
    ),
    executionInputPanel,
    contentGrid
  );
}

export function renderCodingResultPanel(props) {
  const {
    e,
    MarkdownBubbleText,
    rootTab,
    currentConversationId,
    codingResultByConversation,
    filePreviewByConversation,
    runtimeByConversation,
    executionInputByConversation,
    showExecutionLogsByConversation,
    sanitizeCodingAssistantText,
    requestWorkspaceFilePreview,
    requestLatestCodingResultExecution,
    humanPath,
    setCodingExecutionInputByConversation,
    setShowExecutionLogsByConversation
  } = props;

  if (rootTab !== "coding" || !currentConversationId) {
    return null;
  }

  const result = codingResultByConversation[currentConversationId];
  if (!result) {
    return null;
  }

  const codingState = buildCodingResultState(result);
  return renderCodingResultCard({
    ...props,
    result,
    codingState
  });
}

export function renderCodingResultDock(props) {
  const {
    e,
    rootTab,
    currentConversationId,
    codingResultByConversation,
    runtimeByConversation,
    executionInputByConversation,
    requestLatestCodingResultExecution,
    actions
  } = props;

  if (rootTab !== "coding" || !currentConversationId) {
    return null;
  }

  const result = codingResultByConversation[currentConversationId];
  if (!result) {
    return null;
  }

  const codingState = buildCodingResultState(result);
  const detail = [codingState.subtitle, codingState.detail].filter(Boolean).join(" · ");
  const runtime = runtimeByConversation?.[currentConversationId] || null;
  const executionInput = `${executionInputByConversation?.[currentConversationId] || ""}`;
  const canExecute = typeof requestLatestCodingResultExecution === "function";

  return e(
    "div",
    { className: "compact-support-dock coding-result-dock" },
    e("div", { className: "compact-support-dock-copy" },
      e("div", { className: "compact-support-dock-head" },
        e("strong", null, "최근 코딩 결과"),
        e("span", { className: `compact-support-dock-status ${codingState.tone}` }, codingState.statusText)
      ),
      e("div", { className: "compact-support-dock-detail" }, detail)
    ),
    e("div", { className: "coding-result-inline-actions" },
      canExecute
        ? e("button", {
          type: "button",
          className: "btn secondary coding-execute-btn",
          disabled: runtime?.pending,
          onClick: () => {
            requestLatestCodingResultExecution(currentConversationId, executionInput);
            actions.openOverlay();
          }
        }, runtime?.pending ? "실행 중..." : "실행")
        : null,
      e("button", {
        type: "button",
        className: "btn secondary compact-support-dock-trigger",
        onClick: actions.openOverlay,
        title: "최근 코딩 결과 열기"
      },
      e("span", { className: "compact-support-dock-arrow", "aria-hidden": "true" }, "↑"),
      e("span", null, "열기"))
    )
  );
}

export function renderCodingResultOverlay(props) {
  const {
    e,
    rootTab,
    open,
    actions,
    currentConversationId,
    codingResultByConversation
  } = props;

  if (rootTab !== "coding" || !open || !currentConversationId || !codingResultByConversation[currentConversationId]) {
    return null;
  }

  return e(
    "div",
    {
      className: "modal support-overlay-modal",
      onClick: actions.closeOverlay
    },
    e("div", {
      className: "support-overlay-shell",
      onClick: (event) => event.stopPropagation()
    },
    renderCodingResultCard({
      ...props,
      result: codingResultByConversation[currentConversationId],
      codingState: buildCodingResultState(codingResultByConversation[currentConversationId]),
      panelClassName: "coding-result-modal",
      headerAction: e("button", {
        type: "button",
        className: "btn ghost",
        onClick: actions.closeOverlay
      }, "닫기")
    }))
  );
}

export function renderChatMultiResultPanel(props) {
  const {
    e,
    MarkdownBubbleText,
    rootTab,
    mode,
    currentConversationId,
    chatMultiResultByConversation,
    buildChatMultiRenderSnapshot
  } = props;

  if (rootTab !== "chat" || mode !== "multi" || !currentConversationId) {
    return null;
  }

  const result = chatMultiResultByConversation[currentConversationId];
  if (!result) {
    return null;
  }

  const snapshot = buildChatMultiRenderSnapshot(result);
  return e(
    "div",
    { className: "chat-multi-result-stack" },
    e(ResultMessageCarousel, {
      key: `chat-multi-carousel-${currentConversationId}`,
      e,
      MarkdownBubbleText,
      title: "다중 LLM 상세 결과",
      subtitle: result.updatedUtc || "-",
      emptyText: "표시할 다중 LLM 결과가 없습니다.",
      entries: snapshot.entries,
      avatarLabel: "AI",
      avatarClassName: "assistant"
    }),
    renderChatMultiSummaryCard({
      e,
      MarkdownBubbleText,
      summarySections: snapshot.summarySections
    })
  );
}

export function renderThreadSupportStack(props) {
  const {
    e,
    renderChatMultiResult,
    renderCodingResult,
    renderSafeRefactorPanel,
    memoryPickerOpen,
    renderMemoryPicker
  } = props;

  const slots = [];
  const multiResult = renderChatMultiResult();
  if (multiResult) {
    slots.push(e("div", { key: "support-multi", className: "thread-support-slot" }, multiResult));
  }

  const codingResult = renderCodingResult();
  if (codingResult) {
    slots.push(e("div", { key: "support-coding", className: "thread-support-slot" }, codingResult));
  }

  const safeRefactorPanel = renderSafeRefactorPanel();
  if (safeRefactorPanel) {
    slots.push(e("div", { key: "support-refactor", className: "thread-support-slot" }, safeRefactorPanel));
  }

  if (memoryPickerOpen) {
    slots.push(e("div", { key: "support-memory", className: "thread-support-slot" }, renderMemoryPicker()));
  }

  if (slots.length === 0) {
    return null;
  }

  return e("div", { className: "thread-support-stack" }, slots);
}

export function renderResponsiveWorkspaceSupportPane(props) {
  const {
    e,
    currentConversationId,
    renderThreadModebar,
    renderThreadInfoPanel,
    buildThreadPreviewMeta,
    renderMemoryPicker,
    renderChatMultiResult,
    renderCodingResult,
    renderSafeRefactorPanel
  } = props;

  const blocks = [];
  if (currentConversationId) {
    blocks.push(e("div", { key: "support-modebar", className: "thread-support-slot" }, renderThreadModebar("thread-modebar-support")));
    blocks.push(e("div", { key: "support-info", className: "thread-support-slot" }, renderThreadInfoPanel(buildThreadPreviewMeta())));
    blocks.push(e("div", { key: "support-memory", className: "thread-support-slot" }, renderMemoryPicker()));
  }

  const multiResult = renderChatMultiResult();
  if (multiResult) {
    blocks.push(e("div", { key: "support-multi", className: "thread-support-slot" }, multiResult));
  }

  const codingResult = renderCodingResult();
  if (codingResult) {
    blocks.push(e("div", { key: "support-coding", className: "thread-support-slot" }, codingResult));
  }

  const safeRefactorPanel = renderSafeRefactorPanel();
  if (safeRefactorPanel) {
    blocks.push(e("div", { key: "support-refactor", className: "thread-support-slot" }, safeRefactorPanel));
  }

  if (blocks.length === 0) {
    return e("div", { className: "thread-support-stack thread-support-stack-mobile" },
      e("div", { className: "support-card responsive-empty-card" }, "이 화면에서는 응답 전략, 정보, 메모리, 실행 결과 같은 보조 요소를 따로 확인합니다.")
    );
  }

  return e("div", { className: "thread-support-stack thread-support-stack-mobile" }, blocks);
}

export function renderRoutineRunHistoryPanel(props) {
  const {
    e,
    routineId,
    runs,
    openRoutineRunDetail,
    resendRoutineRunTelegram
  } = props;

  if (!Array.isArray(runs) || runs.length === 0) {
    return e("div", { className: "empty routine-history-empty" }, "실행 이력이 아직 없습니다.");
  }

  return e("div", { className: "routine-history-list" },
    runs.map((run) => e("article", { key: `${run.ts}-${run.runAtLocal}`, className: "routine-run-item" },
      e("div", { className: "routine-run-head" },
        e("div", { className: "routine-run-main" },
          e("span", { className: `meta-chip ${run.status === "error" ? "error" : run.status === "success" ? "ok" : "neutral"}` }, run.status || "-"),
          e("strong", null, run.runAtLocal || "-")
        ),
        e("div", { className: "routine-run-meta" }, `${run.source || "-"} · ${run.durationText || "-"} · ${Math.max(1, Number(run.attemptCount || 1))}회`)
      ),
      e("div", { className: "routine-run-summary" }, run.summary || "요약 없음"),
      run.error ? e("div", { className: "routine-run-error" }, run.error) : null,
      run.agentProvider || run.agentModel
        ? e("div", { className: "routine-run-next" }, `agent ${run.agentProvider || "-"}:${run.agentModel || "-"}`)
        : null,
      run.toolProfile ? e("div", { className: "routine-run-next" }, `도구 ${formatRoutineAgentToolProfileLabel(run.toolProfile)}`) : null,
      run.finalUrl ? e("div", { className: "routine-run-next" }, `최종 URL ${run.finalUrl}`) : null,
      run.pageTitle ? e("div", { className: "routine-run-next" }, `페이지 ${run.pageTitle}`) : null,
      run.screenshotPath ? e("div", { className: "routine-run-next" }, `스크린샷 ${run.screenshotPath}`) : null,
      Array.isArray(run.downloadPaths) && run.downloadPaths.length > 0
        ? e("div", { className: "routine-run-next" }, `다운로드 ${run.downloadPaths.join(" | ")}`)
        : null,
      run.telegramStatus ? e("div", { className: "routine-run-next" }, `텔레그램 ${run.telegramStatus}`) : null,
      run.nextRunLocal ? e("div", { className: "routine-run-next" }, `다음 실행 ${run.nextRunLocal}`) : null,
      e("div", { className: "routine-run-actions" },
        e("button", {
          type: "button",
          className: "btn",
          onClick: () => openRoutineRunDetail(routineId, run.ts)
        }, "상세"),
        e("button", {
          type: "button",
          className: "btn",
          onClick: () => resendRoutineRunTelegram(routineId, run.ts)
        }, "텔레그램 재전송")
      )
    ))
  );
}
