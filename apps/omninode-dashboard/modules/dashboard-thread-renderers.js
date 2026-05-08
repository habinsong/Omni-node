export function renderThreadInfoPanel(props) {
  const {
    e,
    previewMeta,
    metaTitle,
    setMetaTitle,
    metaProject,
    setMetaProject,
    metaCategory,
    setMetaCategory,
    metaTags,
    setMetaTags,
    toneForCategory,
    currentConversationId,
    saveConversationMeta
  } = props;

  const previewTags = previewMeta.previewTags || [];
  const previewProject = previewMeta.previewProject || "기본";
  const previewCategory = previewMeta.previewCategory || "일반";
  return e("div", { className: "thread-info-panel" },
    e("div", { className: "thread-info-grid" },
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "대화방 이름"),
        e("input", {
          className: "input compact",
          value: metaTitle,
          onChange: (event) => setMetaTitle(event.target.value),
          placeholder: "대화방 이름"
        })
      ),
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "프로젝트 폴더"),
        e("input", {
          className: "input compact",
          value: metaProject,
          onChange: (event) => setMetaProject(event.target.value),
          placeholder: "예: Omni-node 운영"
        })
      ),
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "카테고리"),
        e("input", {
          className: "input compact",
          value: metaCategory,
          onChange: (event) => setMetaCategory(event.target.value),
          placeholder: "예: 설계, 버그, 문서"
        })
      ),
      e("label", { className: "meta-field" },
        e("span", { className: "meta-label" }, "태그"),
        e("input", {
          className: "input compact",
          value: metaTags,
          onChange: (event) => setMetaTags(event.target.value),
          placeholder: "예: backend,urgent,release"
        })
      )
    ),
    e("div", { className: "thread-info-footer" },
      e("div", { className: "meta-preview-row" },
        e("span", { className: "folder-pill" }, `폴더 · ${previewProject}`),
        e("span", { className: `meta-chip category-${toneForCategory(previewCategory)}` }, previewCategory),
        previewTags.length > 0
          ? previewTags.map((tag) => e("span", { key: `info-${tag}`, className: "tag-chip" }, `#${tag}`))
          : e("span", { className: "meta-chip neutral" }, "태그 없음")
      ),
      e("button", {
        className: "btn primary thread-save-btn",
        disabled: !currentConversationId,
        onClick: saveConversationMeta
      }, "메타 저장")
    )
  );
}

export function renderThreadModebar(props) {
  const {
    e,
    rootTab,
    renderModeTabs,
    extraClassName = ""
  } = props;

  return e("div", { className: `thread-modebar ${extraClassName}`.trim() },
    e("div", { className: "thread-modebar-copy" },
      e("div", { className: "thread-mode-kicker" }, rootTab === "coding" ? "코딩 워크플로" : "응답 전략"),
      e("div", { className: "thread-mode-hint" }, rootTab === "coding"
        ? "단일 완주, 역할 분담형 오케스트레이션, 모델별 독립 완주 비교를 한 흐름으로 관리합니다."
        : "단일 답변, 역할 분담형 오케스트레이션, 모델별 비교형 다중 LLM을 대화 흐름 안에서 전환합니다.")
    ),
    renderModeTabs()
  );
}

export function renderThreadHeader(props) {
  const {
    e,
    rootTab,
    currentConversationId,
    currentConversationTitle,
    scope,
    mode,
    currentMemoryNotesCount,
    threadInfoOpen,
    memoryPickerOpen,
    toneForCategory,
    buildThreadPreviewMeta,
    toggleThreadInfoPanel,
    setMemoryPickerOpen,
    isPortraitMobileLayout,
    setResponsivePane,
    responsiveWorkspaceKey,
    renderThreadModebar,
    renderThreadInfoPanel,
    options = {}
  } = props;

  const previewMeta = buildThreadPreviewMeta();
  const previewTags = previewMeta.previewTags;
  const previewProject = previewMeta.previewProject;
  const previewCategory = previewMeta.previewCategory;
  const summaryTokens = [
    previewProject,
    previewCategory,
    `연결된 메모리 ${currentMemoryNotesCount}개`
  ];
  const showInfoPanel = options.showInfoPanel !== false && threadInfoOpen;
  const showActionButtons = options.showActionButtons !== false;
  const showModebar = options.showModebar !== false;
  return e(
    "div",
    { className: "thread-header-shell" },
    e("div", { className: "thread-topbar" },
      e("div", { className: "thread-identity" },
        e("div", { className: `thread-avatar ${rootTab === "coding" ? "coding" : "chat"} ${currentConversationId ? "active" : "idle"}` }, rootTab === "coding" ? "</>" : "AI"),
        e("div", { className: "thread-copy" },
          e("div", { className: "thread-title-row" },
            e("div", { className: "thread-title" }, currentConversationTitle),
            currentConversationId
              ? e("span", { className: `thread-context-badge ${rootTab === "coding" ? "coding" : "chat"}` }, `${scope.toUpperCase()} · ${mode}`)
              : null
          ),
          e("div", { className: "thread-subline" }, summaryTokens.join(" · ")),
          e("div", { className: "thread-chip-row" },
            e("span", { className: "folder-pill" }, `폴더 · ${previewProject}`),
            e("span", { className: `meta-chip category-${toneForCategory(previewCategory)}` }, previewCategory),
            previewTags.length > 0
              ? previewTags.map((tag) => e("span", { key: `preview-${tag}`, className: "tag-chip" }, `#${tag}`))
              : e("span", { className: "meta-chip neutral" }, "태그 없음")
          )
        )
      ),
      showActionButtons
        ? e("div", { className: "thread-actions" },
          e("button", {
            className: `btn ghost thread-action-btn ${threadInfoOpen ? "active" : ""}`,
            disabled: !currentConversationId,
            onClick: toggleThreadInfoPanel
          }, threadInfoOpen ? "정보 닫기" : "정보"),
          e("button", {
            className: "btn ghost thread-action-btn",
            disabled: !currentConversationId,
            onClick: () => {
              const next = !memoryPickerOpen;
              setMemoryPickerOpen(next);
              if (isPortraitMobileLayout && next) {
                setResponsivePane(responsiveWorkspaceKey, "support");
              }
            }
          }, memoryPickerOpen ? "메모리 닫기" : "메모리")
        )
        : null
    ),
    showModebar ? renderThreadModebar() : null,
    showInfoPanel ? renderThreadInfoPanel(previewMeta) : null
  );
}

function normalizeInlineCarouselIndex(index, entryCount) {
  if (!Number.isFinite(entryCount) || entryCount <= 0) {
    return 0;
  }

  const numericIndex = Number.isFinite(Number(index)) ? Number(index) : 0;
  const normalized = numericIndex % entryCount;
  return normalized < 0 ? normalized + entryCount : normalized;
}

function InlineResultCarousel(props) {
  const { useEffect, useState } = React;
  const {
    e,
    MarkdownBubbleText,
    entries,
    kicker,
    title,
    subtitle
  } = props;
  const safeEntries = Array.isArray(entries) ? entries.filter(Boolean) : [];
  const [selectedIndex, setSelectedIndex] = useState(0);

  useEffect(() => {
    setSelectedIndex((prev) => normalizeInlineCarouselIndex(prev, safeEntries.length));
  }, [safeEntries.length]);

  if (safeEntries.length === 0) {
    return e("div", { className: "empty-line" }, "모델별 결과가 없습니다.");
  }

  const activeIndex = normalizeInlineCarouselIndex(selectedIndex, safeEntries.length);
  const activeEntry = safeEntries[activeIndex];
  const canNavigate = safeEntries.length > 1;

  return e(
    "div",
    { className: "multi-inline-carousel" },
    e("div", { className: "bubble-meta" }, kicker || "모델별 비교"),
    e("div", { className: "result-carousel-head thread-inline-carousel-head" },
      e("div", { className: "result-carousel-copy" },
        e("strong", null, title || "결과 비교"),
        e("span", { className: "result-carousel-subtitle" }, activeEntry.meta || subtitle || "모델별 비교")
      ),
      e("div", { className: "result-carousel-nav" },
        e("button", {
          type: "button",
          className: "btn ghost result-carousel-nav-btn",
          onClick: () => setSelectedIndex((prev) => normalizeInlineCarouselIndex(prev - 1, safeEntries.length)),
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
          onClick: () => setSelectedIndex((prev) => normalizeInlineCarouselIndex(prev + 1, safeEntries.length)),
          disabled: !canNavigate,
          title: "다음 모델"
        }, ">")
      )
    ),
    e("div", { className: `thread-inline-carousel-body${activeEntry.tone === "error" ? " is-error" : ""}` },
      e(MarkdownBubbleText, { text: activeEntry.body || "-" })
    )
  );
}

export function renderMessagesPanel(props) {
  const {
    e,
    MarkdownBubbleText,
    currentKey,
    currentConversationId,
    currentMessages,
    optimisticUserByKey,
    pendingByKey,
    codingProgressByKey,
    isConversationBoundEntryVisible,
    elapsedSeconds,
    sanitizeCodingAssistantText,
    messageListRef,
    parseChatMultiComparisonMessage,
    parseCodingMultiComparisonMessage,
    ttsSupported,
    speakingMessageId,
    onToggleSpeakMessage
  } = props;

  const optimisticUserEntry = optimisticUserByKey[currentKey];
  const pendingEntry = pendingByKey[currentKey];
  const progressEntry = codingProgressByKey[currentKey];
  const optimisticUser = isConversationBoundEntryVisible(optimisticUserEntry, currentConversationId) ? optimisticUserEntry : null;
  const pending = isConversationBoundEntryVisible(pendingEntry, currentConversationId) ? pendingEntry : null;
  const progress = isConversationBoundEntryVisible(progressEntry, currentConversationId) ? progressEntry : null;
  const isPendingVisible = !!(pending && pending.active);
  const isCodingScope = currentKey.startsWith("coding:");
  const elapsed = elapsedSeconds(progress);
  const percent = Math.max(0, Math.min(100, Number(progress?.percent || 0)));
  const stageTitle = `${progress?.stageTitle || ""}`.trim();
  const stageDetail = `${progress?.stageDetail || ""}`.trim();
  const stageBadge = progress?.stageIndex > 0 && progress?.stageTotal > 0
    ? `${progress.stageIndex}/${progress.stageTotal}${stageTitle ? ` ${stageTitle}` : ""}`
    : (stageTitle || progress?.phase || "processing");
  const iterationLabel = progress?.maxIterations > 0
    ? `내부 반복 ${progress.iteration || 0}/${progress.maxIterations}`
    : "";
  const isChatMultiMode = currentKey === "chat:multi";
  const isCodingMultiMode = currentKey === "coding:multi";
  return e(
    "div",
    { className: "message-list", ref: messageListRef },
    currentMessages.length === 0 && !optimisticUser
      ? e("div", { className: "empty" }, "대화를 시작하세요.")
      : currentMessages.map((item, index) => {
        const bubbleText = isCodingScope && item.role === "assistant"
          ? sanitizeCodingAssistantText(item.text || "")
          : (item.text || "");
        const isUser = item.role === "user";
        const inlineMulti = !isUser && isChatMultiMode && typeof parseChatMultiComparisonMessage === "function"
          ? parseChatMultiComparisonMessage(item.text || "")
          : (!isUser && isCodingMultiMode && typeof parseCodingMultiComparisonMessage === "function"
              ? parseCodingMultiComparisonMessage(item.text || "")
              : null);
        const inlineCarouselConfig = isChatMultiMode
          ? {
              kicker: "모델별 답변 비교",
              title: "다중 LLM",
              subtitle: "모델별 비교"
            }
          : {
              kicker: "모델별 코딩 비교",
              title: "다중 코딩",
              subtitle: "모델별 독립 완주 결과"
            };
        const messageId = `${currentConversationId || ""}::${item.createdUtc || ""}::${index}`;
        const isSpeaking = !isUser && ttsSupported && speakingMessageId === messageId;
        const ttsButton = !isUser && ttsSupported && typeof onToggleSpeakMessage === "function"
          ? e("button", {
              type: "button",
              className: `tts-pill ${isSpeaking ? "playing" : ""}`,
              "aria-pressed": isSpeaking ? "true" : "false",
              "aria-label": isSpeaking ? "재생 중지" : "음성으로 듣기",
              title: isSpeaking ? "정지" : "듣기",
              onClick: () => onToggleSpeakMessage(messageId, bubbleText || item.text || "")
            },
              e("span", { className: "tts-pill-icon", "aria-hidden": "true" },
                e("svg", { viewBox: "0 0 24 24", width: "14", height: "14", fill: "none", stroke: "currentColor", strokeWidth: "2", strokeLinecap: "round", strokeLinejoin: "round" },
                  e("polygon", { points: "11 5 6 9 2 9 2 15 6 15 11 19 11 5" }),
                  e("path", { className: "tts-wave tts-wave-far", d: "M19.07 4.93a10 10 0 0 1 0 14.14" }),
                  e("path", { className: "tts-wave tts-wave-near", d: "M15.54 8.46a5 5 0 0 1 0 7.07" })
                )
              ),
              e("span", { className: "tts-pill-label" }, isSpeaking ? "정지" : "듣기")
            )
          : null;
        return e(
          "div",
          { key: `${item.createdUtc || index}-${index}`, className: `message-row ${isUser ? "user" : "assistant"}` },
          !isUser
            ? e("div", { className: `message-avatar ${isCodingScope ? "coding" : "assistant"}` }, isCodingScope ? "DEV" : "AI")
            : null,
          e(
            "div",
            { className: `bubble ${isUser ? "user" : "assistant"}${inlineMulti ? " bubble-multi-inline" : ""}` },
            inlineMulti
              ? e(InlineResultCarousel, {
                  e,
                  MarkdownBubbleText,
                  entries: inlineMulti.entries,
                  kicker: inlineCarouselConfig.kicker,
                  title: inlineCarouselConfig.title,
                  subtitle: inlineCarouselConfig.subtitle
                })
              : [
                  item.meta ? e("div", { key: "meta", className: "bubble-meta" }, item.meta) : null,
                  e(MarkdownBubbleText, { key: "body", text: bubbleText })
                ]
          ),
          ttsButton
            ? e("div", { className: "message-tts-aside" }, ttsButton)
            : null,
          isUser
            ? e("div", { className: "message-avatar self" }, "ME")
            : null
        );
      }),
    optimisticUser
      ? e(
        "div",
        { className: "message-row user pending-user-row" },
        e(
          "div",
          { className: "bubble user pending-user" },
          e("div", { className: "bubble-meta" }, "사용자 (전송됨)"),
          e(MarkdownBubbleText, { text: optimisticUser.text || "" })
        ),
        e("div", { className: "message-avatar self" }, "ME")
      )
      : null,
    isPendingVisible
      ? e(
        "div",
        { className: "message-row assistant pending-assistant-row" },
        e("div", { className: `message-avatar ${isCodingScope ? "coding" : "assistant"}` }, isCodingScope ? "DEV" : "AI"),
        e(
          "div",
          { className: "bubble assistant pending-bubble" },
          e("div", { className: "bubble-meta" }, "assistant"),
          e("div", { className: "pending" }, "작성중..."),
          !isCodingScope && pending?.provider
            ? e("div", { className: "pending-route" }, `${pending.provider || "-"}:${pending.model || "-"}${pending.route ? ` · ${pending.route}` : ""}`)
            : null,
          !isCodingScope && pending?.draftText
            ? e(MarkdownBubbleText, { text: pending.draftText })
            : null,
          isCodingScope
            ? e("div", { className: "pending-details" },
              e("div", { className: "pending-meta-row" },
                e("span", { className: "pending-phase" }, stageBadge),
                e("span", { className: "pending-time" }, `${elapsed}s`)
              ),
              stageDetail ? e("div", { className: "pending-message" }, stageDetail) : null,
              progress?.message ? e("div", { className: "pending-message" }, progress.message) : null,
              progress?.provider || progress?.model
                ? e("div", { className: "pending-route" }, `${progress.provider || "-"}:${progress.model || "-"}`)
                : null,
              iterationLabel
                ? e("div", { className: "pending-iteration" }, iterationLabel)
                : null,
              e("div", { className: "progress-track" },
                e("div", { className: "progress-fill", style: { width: `${percent}%` } })
              )
            )
            : null
        )
      )
      : null
  );
}
