export const ROUTINE_CREATE_PROGRESS_STAGES = [
  { key: "request_analysis", title: "요청 분석", compactTitle: "요청 분석", detail: "스케줄과 실행 경로를 확인합니다." },
  { key: "planning", title: "생성 전략 준비", compactTitle: "전략 준비", detail: "실행 방식과 사용할 생성 경로를 고릅니다." },
  { key: "implementation", title: "실행 구성 생성", compactTitle: "구성 생성", detail: "스크립트 또는 실행 구성을 만들고 필요한 보정을 적용합니다." },
  { key: "save", title: "루틴 등록", compactTitle: "루틴 등록", detail: "생성 결과를 저장하고 스케줄에 연결합니다." },
  { key: "initial_run", title: "초기 실행", compactTitle: "초기 실행", detail: "생성 직후 1회 실행해서 결과를 반영합니다." }
];

function clampPercent(value) {
  const num = Number(value);
  if (!Number.isFinite(num)) return 0;
  return Math.max(0, Math.min(100, Math.round(num)));
}

function formatElapsed(progress) {
  const startedAt = Number(progress && progress.startedAt);
  if (!Number.isFinite(startedAt) || startedAt <= 0) return "";
  const end = progress && progress.active && !progress.done
    ? Date.now()
    : (Number(progress && progress.completedAt) > 0
      ? Number(progress.completedAt)
      : (Number(progress && progress.updatedAt) > 0 ? Number(progress.updatedAt) : Date.now()));
  const ms = Math.max(0, end - startedAt);
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(ms >= 10000 ? 0 : 1)}초`;
}

/**
 * Lazy-built memoized component — only re-renders when `progress` ref changes.
 * Insulates the heavy stage-list/bar-fill subtree from unrelated parent state churn.
 */
let RoutineProgressComponent = null;
function ensureProgressComponent(e) {
  if (RoutineProgressComponent) return RoutineProgressComponent;
  if (typeof window === "undefined" || !window.React) return null;
  const { memo } = window.React;
  RoutineProgressComponent = memo(function RoutineProgress({ progress }) {
    return renderRoutineProgressPanelInner(e, progress);
  }, (prev, next) => prev.progress === next.progress);
  return RoutineProgressComponent;
}

/**
 * Public entry — returns a memoized React element when possible, falls back to
 * direct render when React is unavailable (e.g. unit-test environment).
 */
export function renderRoutineProgressPanel(e, progress) {
  const Component = ensureProgressComponent(e);
  if (Component) {
    return e(Component, { progress });
  }
  return renderRoutineProgressPanelInner(e, progress);
}

function renderRoutineProgressPanelInner(e, progress) {
  const tracking = !!(progress && progress.operation === "create" && (progress.active || progress.done));
  const percent = tracking ? clampPercent(progress.done && progress.ok ? 100 : progress.percent) : 0;
  const elapsed = tracking ? formatElapsed(progress) : "";
  const summaryTitle = tracking
    ? (progress.done ? (progress.ok ? "루틴 생성 완료" : "루틴 생성 실패") : (progress.stageTitle || "루틴 생성 진행 중"))
    : "루틴 생성 대기";
  const summaryDetail = tracking
    ? (progress.stageDetail || progress.message || "루틴 생성 단계를 진행 중입니다.")
    : "요청 전 대기";
  const badgeText = tracking ? (progress.done ? (progress.ok ? "완료" : "실패") : "진행 중") : "대기";
  const badgeClass = tracking ? (progress.done ? (progress.ok ? "ok" : "error") : "working") : "idle";
  const currentStageIndex = tracking
    ? Math.max(1, Math.min(ROUTINE_CREATE_PROGRESS_STAGES.length, Number(progress.stageIndex) || 1))
    : 0;

  const panelClassName = `routine-progress-panel ${tracking ? "is-tracking" : "is-idle"} ${progress?.done ? (progress.ok ? "is-ok" : "is-error") : ""}`.trim();

  return e("aside", { className: panelClassName },
    e("div", { className: "routine-progress-head" },
      e("div", null,
        e("div", { className: "routine-head-kicker" }, "생성 프로그레스"),
        e("strong", { className: "routine-progress-title" }, summaryTitle)
      ),
      e("div", { className: "routine-progress-head-side" },
        elapsed ? e("span", { className: "routine-progress-elapsed" }, `경과 ${elapsed}`) : null,
        e("span", { className: `routine-progress-badge ${badgeClass}` }, badgeText)
      )
    ),
    e("div", { className: "routine-progress-meta" },
      e("span", { className: "routine-progress-caption" }, summaryDetail),
      e("span", { className: "routine-progress-percent" }, tracking ? `${percent}%` : "대기")
    ),
    e("div", {
      className: "routine-progress-bar",
      role: "progressbar",
      "aria-valuemin": 0,
      "aria-valuemax": 100,
      "aria-valuenow": percent
    },
      e("div", { className: "routine-progress-bar-fill", style: { width: `${percent}%` } })
    ),
    tracking
      ? e("div", { className: "routine-progress-stage-list" },
        ROUTINE_CREATE_PROGRESS_STAGES.map((stage, index) => {
          const stageNumber = index + 1;
          let status = "pending";
          if (currentStageIndex > 0) {
            if (stageNumber < currentStageIndex) status = "done";
            else if (stageNumber === currentStageIndex) status = progress.done ? (progress.ok ? "done" : "error") : "active";
          }
          return e("div", { key: stage.key, className: `routine-progress-stage ${status}` },
            e("span", { className: "routine-progress-stage-index" }, `${stageNumber}`),
            e("span", { className: "routine-progress-stage-title" }, stage.compactTitle || stage.title)
          );
        })
      )
      : null
  );
}
