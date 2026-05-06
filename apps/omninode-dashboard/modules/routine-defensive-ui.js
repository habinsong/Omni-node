/**
 * Defensive UI helpers for the routine tab.
 * - Centralizes the "are you sure" dialogs so we never accidentally fire
 *   destructive actions from a misclick.
 * - Provides a native-<details>-based split menu for grouping related but
 *   destructive-adjacent actions (test variants).
 */

export function confirmThenRun(message, action) {
  if (typeof window === "undefined" || typeof window.confirm !== "function") {
    action();
    return;
  }
  if (window.confirm(message)) {
    action();
  }
}

/**
 * Wraps the delete action with a confirm dialog and a post-delete cleanup
 * step (e.g. clearing routineSelectedId so the detail pane goes empty).
 */
export function buildDeleteRoutineHandler({ routine, deleteRoutineById, afterDelete }) {
  return () => {
    if (!routine) return;
    const label = routine.title || routine.id || "이 루틴";
    confirmThenRun(`"${label}" 루틴을 삭제할까요?\n실행 이력은 유지되지만 스케줄과 정의는 되돌릴 수 없습니다.`, () => {
      deleteRoutineById(routine.id);
      if (typeof afterDelete === "function") afterDelete(routine.id);
    });
  };
}

/**
 * Native <details> based split menu — no React state needed.
 * Closes itself on any item click via internal blur/toggle.
 */
export function renderRoutineTestSplitMenu(props) {
  const { e, primaryLabel, primaryAction, secondaryItems } = props;
  const validItems = (secondaryItems || []).filter((item) => item && item.label && typeof item.action === "function");

  if (validItems.length === 0) {
    return e("button", { className: "btn primary", onClick: primaryAction }, primaryLabel);
  }

  return e("details", { className: "routine-split-menu" },
    e("summary", { className: "routine-split-menu-summary" },
      e("button", {
        type: "button",
        className: "btn primary routine-split-menu-primary",
        onClick: (event) => { event.preventDefault(); primaryAction(); }
      }, primaryLabel),
      e("span", { className: "routine-split-menu-caret", "aria-hidden": "true" }, "▾")
    ),
    e("div", { className: "routine-split-menu-popover" },
      ...validItems.map((item) => e("button", {
        key: item.key || item.label,
        type: "button",
        className: "routine-split-menu-item",
        onClick: (event) => {
          event.preventDefault();
          item.action();
          // Collapse menu after click
          const details = event.currentTarget.closest("details");
          if (details) details.open = false;
        }
      }, item.label))
    )
  );
}
