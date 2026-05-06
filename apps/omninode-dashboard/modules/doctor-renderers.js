function formatDoctorTimestamp(value) {
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

function resolveDoctorTone(status) {
  const normalized = `${status || ""}`.toLowerCase();
  if (normalized === "ok") {
    return "ok";
  }
  if (normalized === "warn") {
    return "warn";
  }
  if (normalized === "fail") {
    return "error";
  }
  return "neutral";
}

function resolveDoctorLabel(status) {
  const normalized = `${status || ""}`.toLowerCase();
  if (normalized === "ok" || normalized === "warn" || normalized === "fail" || normalized === "skip") {
    return normalized;
  }
  return "-";
}

function buildCountCard(e, label, value, tone = "neutral") {
  return e("div", { className: `doctor-summary-card ${tone}`.trim() },
    e("div", { className: "doctor-summary-label" }, label),
    e("div", { className: "doctor-summary-value" }, String(value))
  );
}

export function renderDoctorPanel(props) {
  const {
    e,
    authed,
    doctorState,
    runDoctorReport,
    refreshDoctorReport
  } = props;

  const report = doctorState.report || null;
  const checks = report && Array.isArray(report.checks) ? report.checks : [];
  const disabled = !authed || doctorState.pending || doctorState.loading;

  return e("section", { className: "panel settings-optimized-panel settings-doctor-panel doctor-panel" },
    e("div", { className: "doctor-panel-head" },
      e("div", null,
        e("h2", null, "환경 진단"),
        e("p", { className: "hint" }, "코어, 워크스페이스, 샌드박스, 시크릿, 검색 경로 상태를 한 번에 점검합니다.")
      ),
      e("div", { className: "row doctor-action-row" },
        e("button", { className: "btn", disabled, onClick: refreshDoctorReport }, "최근 보고서 조회"),
        e("button", { className: "btn primary", disabled, onClick: runDoctorReport }, doctorState.pending ? "진단 실행 중..." : "진단 실행")
      )
    ),
    e("div", { className: "tiny" },
      doctorState.pending
        ? "현재 doctor 실행 중입니다."
        : doctorState.loading
          ? "최근 doctor 보고서를 불러오는 중입니다."
          : `최근 수신: ${formatDoctorTimestamp(doctorState.receivedAt || (report && report.createdAtUtc))}`
    ),
    report
      ? e("div", { className: "doctor-report-meta tiny mt8" }, `report=${report.reportId || "-"} · createdAt=${formatDoctorTimestamp(report.createdAtUtc)}`)
      : null,
    doctorState.lastError
      ? e("div", { className: "error-banner mt8" }, doctorState.lastError)
      : null,
    report
      ? e("div", { className: "doctor-summary-grid" },
        buildCountCard(e, "ok", report.okCount || 0, "ok"),
        buildCountCard(e, "warn", report.warnCount || 0, "warn"),
        buildCountCard(e, "fail", report.failCount || 0, "error"),
        buildCountCard(e, "skip", report.skipCount || 0, "neutral")
      )
      : null,
    checks.length === 0
      ? e("div", { className: "empty doctor-empty-state" }, "저장된 doctor 보고서가 없습니다.")
      : e("div", { className: "doctor-check-list" },
        checks.map((check) => {
          const actions = Array.isArray(check.suggestedActions) ? check.suggestedActions.filter(Boolean) : [];
          return e("article", { key: `${check.id}:${check.status}`, className: "doctor-check-item" },
            e("div", { className: "doctor-check-head" },
              e("div", { className: "doctor-check-title" }, check.id || "-"),
              e("span", { className: `tool-status-chip ${resolveDoctorTone(check.status)}` }, resolveDoctorLabel(check.status))
            ),
            e("div", { className: "doctor-check-summary" }, check.summary || "-"),
            check.detail
              ? e("pre", { className: "doctor-check-detail" }, check.detail)
              : null,
            actions.length > 0
              ? e("ul", { className: "doctor-action-list" },
                actions.map((action, index) => e("li", { key: `${check.id}:action:${index}` }, action)))
              : null
          );
        })
      )
  );
}
