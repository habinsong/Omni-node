export function renderConversationPanel(props) {
  const {
    e,
    currentConversationFilter,
    setConversationFilterByKey,
    currentKey,
    rootTab,
    scope,
    mode,
    currentConversationList,
    groupedConversationList,
    isFolderExpanded,
    currentSelectedFolders,
    selectionMode,
    toggleFolderSelection,
    toggleFolder,
    currentSelectedConversationIds,
    toggleConversationSelection,
    currentConversationId,
    selectConversation,
    toneForCategory,
    buildConversationAvatarText,
    formatConversationUpdatedLabel,
    createConversation,
    metaProject,
    metaCategory,
    parseTags,
    metaTags,
    toggleSelectionMode,
    clearScopeMemory,
    selectedDeleteConversationIds,
    deleteConversation,
    conversationSearchState,
    onConversationSearch,
    clearConversationSearch
  } = props;

  const keyword = currentConversationFilter.trim();
  const searchState = conversationSearchState || { query: "", loading: false, results: [], error: "" };
  const searchResultCount = Array.isArray(searchState.results) ? searchState.results.length : 0;
  return e(
    "section",
    { className: "conversation-panel" },
    e("div", { className: "conversation-head" },
      e("div", { className: "conversation-head-copy" },
        e("div", { className: "conversation-head-kicker" }, rootTab === "coding" ? "코딩 워크스페이스" : "메시지 보관함"),
        e("strong", null, `${scope.toUpperCase()} · ${mode}`),
        e("div", { className: "conversation-head-count" }, `${currentConversationList.length}개 대화`)
      )
    ),
    e("div", { className: "conversation-search" },
      e("input", {
        className: "input folder-search-input",
        value: currentConversationFilter,
        onChange: (event) => setConversationFilterByKey((prev) => ({ ...prev, [currentKey]: event.target.value })),
        placeholder: "제목, 미리보기, 본문 검색"
      }),
      e("div", { className: "conversation-search-actions" },
        e("button", {
          type: "button",
          className: "btn ghost conversation-search-btn",
          onClick: onConversationSearch,
          disabled: keyword.length < 2 || searchState.loading
        }, searchState.loading ? "검색 중..." : "본문 검색"),
        keyword
          ? e("button", {
            type: "button",
            className: "btn ghost conversation-search-clear",
            onClick: clearConversationSearch
          }, "지우기")
          : null
      ),
      keyword.length >= 2
        ? e("div", { className: `conversation-search-status ${searchState.error ? "error" : ""}` },
          searchState.error
            ? searchState.error
            : searchState.loading
              ? "대화 본문까지 찾는 중입니다."
              : searchResultCount > 0
                ? `본문 검색 ${searchResultCount}건을 목록에 반영했습니다.`
                : "제목과 본문에서 검색합니다."
        )
        : null
    ),
    e("div", { className: "conversation-list" },
      currentConversationList.length === 0
        ? e("div", { className: "empty" }, "대화가 없습니다.")
        : groupedConversationList.map((group) => {
          const expanded = keyword.length > 0 || isFolderExpanded(currentKey, group.project);
          const folderSelected = currentSelectedFolders.includes(group.project);
          return e(
            "div",
            { key: `group-${group.project}`, className: "conversation-group" },
            e("div", { className: `folder-header-shell ${selectionMode ? "selection-mode" : ""}` },
              selectionMode
                ? e("button", {
                  type: "button",
                  className: `selection-toggle ${folderSelected ? "active" : ""}`,
                  onClick: () => toggleFolderSelection(group.project),
                  "aria-pressed": folderSelected ? "true" : "false"
                }, folderSelected ? "✓" : "")
                : null,
              e("button", {
                type: "button",
                className: `group-title folder-title folder-toggle ${expanded ? "expanded" : ""} ${folderSelected ? "selected" : ""}`,
                onClick: () => toggleFolder(currentKey, group.project),
                "aria-expanded": expanded ? "true" : "false"
              },
              e("span", { className: "folder-chevron" }, "▸"),
              e("span", { className: "folder-badge" }, "폴더"),
              e("span", { className: "folder-name" }, group.project),
              e("span", { className: "folder-count" }, `${group.items.length}`)
              )
            ),
            e("div", { className: `folder-children ${expanded ? "expanded" : "collapsed"}` },
              expanded
                ? group.items.map((item) => {
                  const itemSelected = currentSelectedConversationIds.includes(item.id);
                  return e(
                    "div",
                    { key: item.id, className: `conversation-item-shell ${selectionMode ? "selection-mode" : ""}` },
                    selectionMode
                      ? e("button", {
                        type: "button",
                        className: `selection-toggle ${itemSelected ? "active" : ""}`,
                        onClick: () => toggleConversationSelection(item.id),
                        "aria-pressed": itemSelected ? "true" : "false"
                      }, itemSelected ? "✓" : "")
                      : null,
                    e(
                      "button",
                      {
                        className: `conversation-item ${currentConversationId === item.id ? "active" : ""} ${itemSelected ? "selected" : ""}`,
                        onClick: () => selectConversation({ ...item, scope, mode })
                      },
                      e("div", { className: `item-avatar category-${toneForCategory(item.category || "일반")}` }, buildConversationAvatarText(item)),
                      e("div", { className: "item-content" },
                        e("div", { className: "item-row" },
                          e("div", { className: "item-title" }, item.title || "제목 없음"),
                          e("div", { className: "item-time" }, formatConversationUpdatedLabel(item.updatedUtc))
                        ),
                        e("div", { className: "item-preview" }, item.preview || ""),
                        e("div", { className: "item-meta" },
                          e("span", { className: `meta-chip category-${toneForCategory(item.category || "일반")}` }, item.category || "일반"),
                          e("span", { className: "meta-chip neutral item-count-chip" }, `${item.messageCount || 0} msgs`)
                        ),
                        Array.isArray(item.tags) && item.tags.length > 0
                          ? e("div", { className: "item-tags" }, item.tags.slice(0, 3).map((tag) => e("span", { key: `${item.id}-${tag}`, className: "tag-chip" }, `#${tag}`)))
                          : null
                      ),
                    )
                  );
                })
                : null
            )
          );
        })
    ),
    e("div", { className: "conversation-bottom-actions" },
      e("button", {
        className: "btn primary conversation-new-btn conversation-bottom-new-btn",
        onClick: () => createConversation(scope, mode, "", metaProject, metaCategory, parseTags(metaTags))
      }, "새 대화"),
      e("div", { className: "conversation-actions conversation-actions-bottom-row" },
        e("button", {
          className: `btn action-select-btn ${selectionMode ? "active" : ""}`,
          onClick: toggleSelectionMode
        }, selectionMode ? "선택 종료" : "선택"),
        e("button", {
          className: "btn action-memory-btn",
          onClick: () => clearScopeMemory(scope)
        }, "메모리 초기화"),
        e("button", {
          className: "btn action-delete-btn",
          disabled: selectionMode ? selectedDeleteConversationIds.length === 0 : !currentConversationId,
          onClick: deleteConversation
        }, "삭제")
      )
    )
  );
}

export function renderMemoryPicker(props) {
  const {
    e,
    currentConversationId,
    send,
    createManualMemoryNote,
    currentCheckedMemoryNotes,
    deleteSelectedMemoryNotes,
    setMemoryPickerOpen,
    memoryNotes,
    currentMemoryNotes,
    toggleMemoryNote,
    renameMemoryNote
  } = props;

  if (!currentConversationId) {
    return e("div", { className: "memory-dock empty" }, "대화를 선택하면 메모리 노트를 연결할 수 있습니다.");
  }

  return e(
    "section",
    { className: "memory-dock support-card" },
    e("div", { className: "memory-dock-head" },
      e("strong", null, "공유 메모리 노트"),
      e("div", { className: "memory-dock-actions" },
        e("button", { className: "btn ghost", onClick: () => send({ type: "list_memory_notes" }) }, "새로고침"),
        e("button", {
          className: "btn ghost",
          disabled: !currentConversationId,
          onClick: () => createManualMemoryNote(false)
        }, "수동 생성"),
        e("button", {
          className: "btn ghost",
          disabled: currentCheckedMemoryNotes.length === 0,
          onClick: deleteSelectedMemoryNotes
        }, "삭제"),
        e("button", { className: "btn ghost", onClick: () => setMemoryPickerOpen(false) }, "닫기")
      )
    ),
    e("div", { className: "memory-dock-list" },
      memoryNotes.length === 0
        ? e("div", { className: "empty" }, "메모리 노트 없음")
        : memoryNotes.map((note) => {
          const checked = currentMemoryNotes.includes(note.name);
          return e(
            "label",
            { key: note.name, className: "memory-dock-item" },
            e("input", {
              type: "checkbox",
              checked,
              onChange: (event) => toggleMemoryNote(note.name, event.target.checked)
            }),
            e("span", { className: "memory-dock-name" }, note.name),
            e("div", { className: "memory-dock-item-actions" },
              e("button", {
                className: "link-btn",
                onClick: (event) => {
                  event.preventDefault();
                  renameMemoryNote(note.name);
                }
              }, "수정"),
              e("button", {
                className: "link-btn",
                onClick: (event) => {
                  event.preventDefault();
                  send({ type: "read_memory_note", noteName: note.name });
                }
              }, "보기")
            )
          );
        })
    )
  );
}
