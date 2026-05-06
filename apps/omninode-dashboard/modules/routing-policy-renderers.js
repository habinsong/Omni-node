const CATEGORY_ROWS = [
  { key: "generalChat", label: "일반 채팅", hint: "기본 단일/오케스트레이션 채팅" },
  { key: "planner", label: "계획 생성", hint: "작업 계획 초안 생성" },
  { key: "reviewer", label: "계획 리뷰", hint: "작업 계획 검토" },
  { key: "searchTimeSensitive", label: "최신성 검색", hint: "실시간 웹 필요 여부 판단" },
  { key: "searchFallback", label: "검색 보조", hint: "검색 fallback 보조 판단" },
  { key: "deepCode", label: "깊은 코딩", hint: "대형 구현과 통합 작업" },
  { key: "safeRefactor", label: "안전 리팩터", hint: "구조 정리와 안전 수정" },
  { key: "quickFix", label: "빠른 수정", hint: "짧은 버그 수정과 검증" },
  { key: "visualUi", label: "UI/시각 작업", hint: "레이아웃과 스타일 작업" },
  { key: "routineBuilder", label: "루틴 빌더", hint: "루틴 생성과 갱신" },
  { key: "backgroundMonitor", label: "백그라운드 모니터", hint: "분석과 상태 확인" },
  { key: "documentation", label: "문서", hint: "문서화와 가이드" }
];

function formatTimestamp(value) {
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

function resolveDraftValue(state, key, fallback) {
  if (state?.draftChains && Object.prototype.hasOwnProperty.call(state.draftChains, key)) {
    return state.draftChains[key] || "";
  }

  return Array.isArray(fallback) ? fallback.join(", ") : "";
}

export function renderRoutingPolicyPanel(props) {
  const {
    e,
    authed,
    routingPolicyState,
    setRoutingPolicyChain,
    refreshRoutingPolicy,
    saveRoutingPolicy,
    resetRoutingPolicy,
    refreshRoutingDecision
  } = props;

  const snapshot = routingPolicyState.snapshot || null;
  const effective = snapshot?.effectiveChains || {};
  const overrides = snapshot?.overrideChains || {};
  const lastDecision = snapshot?.lastDecision || null;
  const disabled = !authed || routingPolicyState.loading || routingPolicyState.pending;

  return e("section", { className: "panel settings-optimized-panel settings-routing-panel routing-policy-panel" },
    e("div", { className: "plans-panel-head" },
      e("div", null,
        e("h2", null, "카테고리 라우팅 정책"),
        e("p", { className: "hint" }, "업무 종류별 provider chain을 조회하고 override를 저장합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", { className: "btn", disabled, onClick: refreshRoutingPolicy }, routingPolicyState.loading ? "불러오는 중..." : "정책 새로고침"),
        e("button", { className: "btn", disabled, onClick: refreshRoutingDecision }, "마지막 결정"),
        e("button", { className: "btn primary", disabled, onClick: saveRoutingPolicy }, routingPolicyState.pending ? "저장 중..." : "override 저장"),
        e("button", { className: "btn ghost", disabled, onClick: resetRoutingPolicy }, "override 초기화")
      )
    ),
    routingPolicyState.lastError
      ? e("div", { className: "error-banner" }, routingPolicyState.lastError)
      : null,
    lastDecision
      ? e("article", { className: "plan-detail-card routing-policy-decision" },
        e("div", { className: "plans-section-head" },
          e("strong", null, "마지막 라우팅 결정"),
          e("span", { className: "tiny" }, formatTimestamp(lastDecision.decidedAtUtc))
        ),
        e("div", { className: "routing-policy-decision-grid" },
          e("div", { className: "doctor-summary-card" },
            e("div", { className: "doctor-summary-label" }, "카테고리"),
            e("div", { className: "doctor-summary-value plan-status-value" }, lastDecision.categoryLabel || lastDecision.categoryKey || "-")
          ),
          e("div", { className: "doctor-summary-card" },
            e("div", { className: "doctor-summary-label" }, "요청"),
            e("div", { className: "doctor-summary-value plan-status-value" }, lastDecision.requestedProvider || "-")
          ),
          e("div", { className: "doctor-summary-card" },
            e("div", { className: "doctor-summary-label" }, "결과"),
            e("div", { className: "doctor-summary-value plan-status-value" }, lastDecision.resolvedProvider || "-")
          ),
          e("div", { className: "doctor-summary-card" },
            e("div", { className: "doctor-summary-label" }, "사유"),
            e("div", { className: "doctor-summary-value plan-status-value" }, lastDecision.reason || "-")
          )
        ),
        e("div", { className: "tiny" }, `chain ${Array.isArray(lastDecision.providerChain) ? lastDecision.providerChain.join(" > ") : "-"}`),
        e("div", { className: "tiny" }, `available ${Array.isArray(lastDecision.availableProviders) ? lastDecision.availableProviders.join(", ") : "-"}`)
      )
      : e("div", { className: "empty plan-empty-state" }, "아직 기록된 라우팅 결정이 없습니다."),
    e("div", { className: "routing-policy-grid" },
      CATEGORY_ROWS.map((row) => {
        const effectiveChain = effective[row.key] || [];
        const overrideChain = overrides[row.key] || [];
        return e("label", { key: row.key, className: "meta-field routing-policy-row" },
          e("span", { className: "meta-label" }, row.label),
          e("span", { className: "tiny" }, row.hint),
          e("input", {
            className: "input routing-policy-input",
            value: resolveDraftValue(routingPolicyState, row.key, effectiveChain),
            placeholder: Array.isArray(effectiveChain) ? effectiveChain.join(", ") : "",
            onChange: (event) => setRoutingPolicyChain(row.key, event.target.value)
          }),
          e("div", { className: "tiny" }, `effective ${Array.isArray(effectiveChain) ? effectiveChain.join(" > ") : "-"}`),
          e("div", { className: "tiny" }, `override ${Array.isArray(overrideChain) && overrideChain.length > 0 ? overrideChain.join(" > ") : "없음"}`)
        );
      })
    )
  );
}
