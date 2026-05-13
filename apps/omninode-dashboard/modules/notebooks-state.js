export function createNotebooksState() {
  return {
    loaded: false,
    loading: false,
    pending: false,
    lastError: "",
    lastAction: "",
    lastMessage: "",
    projectKeyDraft: "",
    appendKind: "learning",
    appendText: "",
    filterText: "",
    expandedDocument: "",
    snapshot: null,
    receivedAt: ""
  };
}
