export function renderToolControlPanel(props) {
  const {
    e,
    authed,
    toolControlError,
    opsDomainFilter,
    applyDomainFocus,
    providerHealthSummary,
    toolDomainStats,
    providerRuntimeRows,
    guardObsStats,
    guardAlertSummary,
    formatGuardAlertThreshold,
    guardRetryTimeline,
    guardRetryTimelineRows,
    guardRetryTimelineSource,
    guardRetryTimelineApiFetchedAt,
    guardRetryTimelineApiError,
    guardAlertPipelineFieldRows,
    submitGuardAlertDispatch,
    guardAlertDispatchState,
    guardAlertPipelinePreview,
    toolResultGroups,
    toolResultStats,
    toolResultFilter,
    setToolResultFilter,
    toolResultFilters,
    toolDomainFilters,
    toolResultItems,
    submitSessionsList,
    submitCronStatus,
    submitBrowserStatus,
    submitCanvasStatus,
    submitNodesStatus,
    toolSessionKey,
    setToolSessionKey,
    submitSessionsHistory,
    submitSessionSend,
    toolSpawnTask,
    setToolSpawnTask,
    submitSessionSpawn,
    toolSessionMessage,
    setToolSessionMessage,
    toolCronJobId,
    setToolCronJobId,
    submitCronList,
    submitCronRun,
    toolBrowserUrl,
    setToolBrowserUrl,
    submitBrowserNavigate,
    toolCanvasTarget,
    setToolCanvasTarget,
    submitCanvasPresent,
    toolNodesNode,
    setToolNodesNode,
    toolNodesRequestId,
    setToolNodesRequestId,
    submitNodesPending,
    toolNodesInvokeCommand,
    setToolNodesInvokeCommand,
    toolNodesInvokeParamsJson,
    setToolNodesInvokeParamsJson,
    submitNodesInvoke,
    toolTelegramStubText,
    setToolTelegramStubText,
    submitTelegramStubCommand,
    toolWebSearchQuery,
    setToolWebSearchQuery,
    submitWebSearchProbe,
    toolWebFetchUrl,
    setToolWebFetchUrl,
    submitWebFetchProbe,
    toolMemorySearchQuery,
    setToolMemorySearchQuery,
    submitMemorySearchProbe,
    toolMemoryGetPath,
    setToolMemoryGetPath,
    submitMemoryGetProbe,
    clearToolControlResults,
    toolResultPreview,
    filteredToolResultItems,
    selectedToolResultId,
    selectToolResultItem
  } = props;

  const renderSection = (title, subtitle, ...items) => {
    const className = typeof items[items.length - 1] === "string" ? items.pop() : "";
    return e(
      "section",
      { className: `tool-ux-section ${className}`.trim() },
      e("div", { className: "tool-ux-section-head" },
        e("div", null,
          e("h3", null, title),
          subtitle ? e("p", null, subtitle) : null
        )
      ),
      ...items
    );
  };

  const renderTableCard = (title, subtitle, table) => e(
    "article",
    { className: "tool-ux-card tool-ux-table-card" },
    e("div", { className: "tool-ux-card-head" },
      e("div", null,
        e("strong", null, title),
        subtitle ? e("span", null, subtitle) : null
      )
    ),
    e("div", { className: "table-wrap" }, table)
  );

  const renderDisclosure = (title, subtitle, body, open = false) => e(
    "details",
    { className: "tool-ux-disclosure", open },
    e("summary", null,
      e("span", null, title),
      subtitle ? e("small", null, subtitle) : null
    ),
    e("div", { className: "tool-ux-disclosure-body" }, body)
  );

  const renderControlGroup = (title, subtitle, rows) => e(
    "article",
    { className: "tool-control-group" },
    e("div", { className: "tool-ux-card-head" },
      e("div", null,
        e("strong", null, title),
        subtitle ? e("span", null, subtitle) : null
      )
    ),
    rows
  );

  const renderTextInput = (value, onChange, placeholder) => e("input", {
    className: "input",
    value,
    onChange: (event) => onChange(event.target.value),
    placeholder
  });

  const providerTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "provider"),
        e("th", null, "상태"),
        e("th", null, "실행(success/fail/progress)"),
        e("th", null, "근거")
      )
    ),
    e("tbody", null,
      providerRuntimeRows.map((row) => e("tr", { key: `provider-health-${row.provider}` },
        e("td", null, row.provider),
        e("td", null, e("span", { className: `tool-status-chip ${row.statusTone || "neutral"}` }, row.statusLabel || "-")),
        e("td", null, `${row.runtimeSuccessCount || 0}/${row.runtimeErrorCount || 0}/${row.runtimeProgressCount || 0}`),
        e("td", null, row.reason || "-")
      ))
    )
  );

  const guardChannelTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "채널"),
        e("th", null, "이벤트"),
        e("th", null, "guard 차단"),
        e("th", null, "retryRequired"),
        e("th", null, "count-lock 미충족"),
        e("th", null, "count-lock 비율"),
        e("th", null, "citation fail"),
        e("th", null, "citation_mapping retry"),
        e("th", null, "retry 시도 최대"),
        e("th", null, "최근 retry")
      )
    ),
    e("tbody", null,
      ["chat", "coding", "telegram", "search", "other"].map((channel) => {
        const stat = guardObsStats.byChannel[channel] || {
          count: 0,
          blockedCount: 0,
          retryRequiredCount: 0,
          countLockUnsatisfiedCount: 0,
          citationValidationFailedCount: 0,
          citationMappingRetryCount: 0,
          maxRetryAttempt: 0,
          maxRetryMaxAttempts: 0,
          lastRetryAction: "-",
          lastRetryReason: "-",
          lastRetryStopReason: "-"
        };
        const countLockUnsatisfiedRate = (stat.count || 0) > 0
          ? (stat.countLockUnsatisfiedCount || 0) / (stat.count || 1)
          : 0;
        return e("tr", { key: `guard-obs-${channel}` },
          e("td", null, channel),
          e("td", null, stat.count || 0),
          e("td", null, stat.blockedCount || 0),
          e("td", null, stat.retryRequiredCount || 0),
          e("td", null, stat.countLockUnsatisfiedCount || 0),
          e("td", null, formatGuardAlertThreshold("rate", countLockUnsatisfiedRate)),
          e("td", null, stat.citationValidationFailedCount || 0),
          e("td", null, stat.citationMappingRetryCount || 0),
          e("td", null, `${stat.maxRetryAttempt || 0}/${stat.maxRetryMaxAttempts || 0}`),
          e("td", null, `${stat.lastRetryAction || "-"}/${stat.lastRetryReason || "-"} (${stat.lastRetryStopReason || "-"})`)
        );
      })
    )
  );

  const retryTimelineTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "채널"),
        e("th", null, "버킷 시작(UTC)"),
        e("th", null, "샘플"),
        e("th", null, "retryRequired"),
        e("th", null, "max retry"),
        e("th", null, "max retryMax"),
        e("th", null, "top retryStopReason"),
        e("th", null, "고유 stopReason")
      )
    ),
    e("tbody", null,
      guardRetryTimelineRows.length === 0
        ? e("tr", null, e("td", { colSpan: 8 }, "채널 공통 retry 시계열 데이터가 없습니다."))
        : guardRetryTimelineRows.map((row, index) => e("tr", { key: `guard-retry-timeline-${row.channel}-${row.bucketStartUtc}-${index}` },
          e("td", null, row.channel),
          e("td", null, row.bucketStartUtc),
          e("td", null, row.samples),
          e("td", null, row.retryRequiredCount),
          e("td", null, row.maxRetryAttempt),
          e("td", null, row.maxRetryMaxAttempts),
          e("td", null, row.topRetryStopReason),
          e("td", null, row.uniqueRetryStopReasons)
        ))
    )
  );

  const guardAlertTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "규칙"),
        e("th", null, "측정값"),
        e("th", null, "warn"),
        e("th", null, "critical"),
        e("th", null, "상태"),
        e("th", null, "비고")
      )
    ),
    e("tbody", null,
      guardObsStats.guardAlertRows.map((row) => e("tr", { key: `guard-alert-${row.id}` },
        e("td", null, row.label),
        e("td", null, row.valueLabel),
        e("td", null, row.warnLabel),
        e("td", null, row.criticalLabel),
        e("td", null, e("span", { className: `tool-status-chip ${row.statusTone}` }, row.statusLabel)),
        e("td", null, row.note || "-")
      ))
    )
  );

  const guardSchemaTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "필드 경로"),
        e("th", null, "타입"),
        e("th", null, "필수"),
        e("th", null, "설명")
      )
    ),
    e("tbody", null,
      guardAlertPipelineFieldRows.map((field) => e("tr", { key: `guard-alert-schema-${field.path}` },
        e("td", null, field.path),
        e("td", null, field.type),
        e("td", null, field.required),
        e("td", null, field.description)
      ))
    )
  );

  const dispatchResultTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "대상"),
        e("th", null, "상태"),
        e("th", null, "시도"),
        e("th", null, "HTTP"),
        e("th", null, "오류"),
        e("th", null, "endpoint")
      )
    ),
    e("tbody", null,
      Array.isArray(guardAlertDispatchState.targets) && guardAlertDispatchState.targets.length > 0
        ? guardAlertDispatchState.targets.map((item, index) => e("tr", { key: `guard-alert-dispatch-${item.name}-${index}` },
          e("td", null, item.name || "-"),
          e("td", null, item.status || "-"),
          e("td", null, Number.isFinite(Number(item.attempts)) ? Number(item.attempts) : 0),
          e("td", null, Number.isFinite(Number(item.statusCode)) ? Number(item.statusCode) : "-"),
          e("td", null, item.error || "-"),
          e("td", null, item.endpoint || "-")
        ))
        : e("tr", null, e("td", { colSpan: 6 }, "아직 전송 이력이 없습니다."))
    )
  );

  const retryKeyTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "분류"),
        e("th", null, "키"),
        e("th", null, "횟수")
      )
    ),
    e("tbody", null,
      (() => {
        const rows = [];
        guardObsStats.topRetryActions.forEach((item) => rows.push({ kind: "retryAction", name: item.name, count: item.count }));
        guardObsStats.topRetryReasons.forEach((item) => rows.push({ kind: "retryReason", name: item.name, count: item.count }));
        guardObsStats.topRetryStopReasons.forEach((item) => rows.push({ kind: "retryStopReason", name: item.name, count: item.count }));
        if (rows.length === 0) {
          return e("tr", null, e("td", { colSpan: 3 }, "retryAction/retryReason/retryStopReason 집계 데이터가 없습니다."));
        }
        return rows.map((row, index) => e("tr", { key: `guard-obs-row-${row.kind}-${row.name}-${index}` },
          e("td", null, row.kind),
          e("td", null, row.name),
          e("td", null, row.count)
        ));
      })()
    )
  );

  const resultTable = e("table", { className: "model-table" },
    e("thead", null,
      e("tr", null,
        e("th", null, "시각(UTC)"),
        e("th", null, "도메인"),
        e("th", null, "그룹"),
        e("th", null, "액션"),
        e("th", null, "타입"),
        e("th", null, "상태"),
        e("th", null, "요약")
      )
    ),
    e("tbody", null,
      toolResultItems.length === 0
        ? e("tr", null, e("td", { colSpan: 7 }, "아직 수신된 도구 결과가 없습니다."))
        : (filteredToolResultItems.length === 0
          ? e("tr", null, e("td", { colSpan: 7 }, "필터 조건에 맞는 결과가 없습니다."))
          : filteredToolResultItems.map((item) => e("tr", {
            key: item.id,
            className: `tool-result-row ${selectedToolResultId === item.id ? "selected" : ""} ${item.hasError ? "error" : ""}`,
            onClick: () => selectToolResultItem(item)
          },
          e("td", null, item.capturedAt || "-"),
          e("td", null, item.domain || "-"),
          e("td", null, item.group || "-"),
          e("td", null, item.action || "-"),
          e("td", null, item.type || "-"),
          e("td", null, e("span", { className: `tool-status-chip ${item.statusTone || "neutral"}` }, item.statusLabel || "-")),
          e("td", null, item.summary || "-", item.errorText ? e("div", { className: "tool-error-text" }, item.errorText) : null)
          )))
    )
  );

  return e(
    "section",
    { className: "panel settings-optimized-panel settings-tools-panel ops-panel tool-ux-panel" },
    e("div", { className: "tool-ux-hero" },
      e("div", null,
        e("h2", null, "도구 통합"),
        e("p", null, "Provider 상태, Guard 관측, 도구 제어 요청, 실행 결과를 작업 흐름별로 나눠 확인합니다.")
      ),
      e("div", { className: "tool-ux-health-strip" },
        e("div", { className: "tool-ux-health-card" },
          e("span", null, "Provider"),
          e("strong", null, providerHealthSummary.mainLabel)
        ),
        e("div", { className: "tool-ux-health-card" },
          e("span", null, "Tool"),
          e("strong", null, `${toolDomainStats.tool.count}건`)
        ),
        e("div", { className: "tool-ux-health-card" },
          e("span", null, "Guard"),
          e("strong", null, `${guardObsStats.total}건`)
        )
      )
    ),
    toolControlError ? e("div", { className: "error-banner" }, toolControlError) : null,

    renderSection("1. 상태 요약", "가장 먼저 봐야 할 provider/tool/rag 상태입니다.",
      e("div", { className: "tool-ux-overview-grid" },
        e("button", {
          type: "button",
          className: `tool-summary-card ${opsDomainFilter === "provider" ? "active" : ""}`,
          onClick: () => applyDomainFocus("provider")
        },
        e("div", { className: "tool-summary-title" }, "provider"),
        e("div", { className: "tool-summary-main" }, providerHealthSummary.mainLabel),
        e("div", { className: "tool-summary-meta" }, `설정오류 ${providerHealthSummary.setupErrorCount}건 / 실행실패 ${providerHealthSummary.runtimeErrorCount}건`),
        e("div", { className: "tool-summary-meta" }, `성공 ${providerHealthSummary.runtimeSuccessCount}건 / 진행 ${providerHealthSummary.runtimeProgressCount}건`),
        e("div", { className: "tool-summary-meta" }, `상태 ${providerHealthSummary.lastStatus}`)),
        e("button", {
          type: "button",
          className: `tool-summary-card ${opsDomainFilter === "tool" ? "active" : ""}`,
          onClick: () => applyDomainFocus("tool")
        },
        e("div", { className: "tool-summary-title" }, "tool"),
        e("div", { className: "tool-summary-main" }, `${toolDomainStats.tool.count}건`),
        e("div", { className: "tool-summary-meta" }, `오류 ${toolDomainStats.tool.errorCount}건`),
        e("div", { className: "tool-summary-meta" }, `최근 ${toolDomainStats.tool.lastType}/${toolDomainStats.tool.lastStatus}`)),
        e("button", {
          type: "button",
          className: `tool-summary-card ${opsDomainFilter === "rag" ? "active" : ""}`,
          onClick: () => applyDomainFocus("rag")
        },
        e("div", { className: "tool-summary-title" }, "rag"),
        e("div", { className: "tool-summary-main" }, `${toolDomainStats.rag.count}건`),
        e("div", { className: "tool-summary-meta" }, `오류 ${toolDomainStats.rag.errorCount}건`),
        e("div", { className: "tool-summary-meta" }, `최근 ${toolDomainStats.rag.lastType}/${toolDomainStats.rag.lastStatus}`))
      )
    ),

    renderSection("2. Provider 상태", "모델/도구 호출 실패가 provider 문제인지 먼저 확인합니다.",
      renderTableCard("Provider 런타임", "상태, 성공/실패/진행, 근거", providerTable),
      "tool-ux-provider-section"
    ),

    renderSection("3. Guard 관측", "차단, retry, citation 문제를 요약 후 필요한 상세만 펼쳐 봅니다.",
      e("div", { className: "tool-ux-overview-grid four" },
        e("div", { className: "tool-summary-card" },
          e("div", { className: "tool-summary-title" }, "guard/retry"),
          e("div", { className: "tool-summary-main" }, `${guardObsStats.total}건`),
          e("div", { className: "tool-summary-meta" }, `guard 차단 ${guardObsStats.blockedTotal}건`),
          e("div", { className: "tool-summary-meta" }, `retryRequired ${guardObsStats.retryRequiredTotal}건`),
          e("div", { className: "tool-summary-meta" }, `count-lock 미충족 ${guardObsStats.countLockUnsatisfiedTotal}건`)
        ),
        e("div", { className: "tool-summary-card" },
          e("div", { className: "tool-summary-title" }, "citation"),
          e("div", { className: "tool-summary-main" }, `fail ${guardObsStats.citationValidationFailedTotal}건`),
          e("div", { className: "tool-summary-meta" }, `mapping retry ${guardObsStats.citationMappingRetryTotal}건`),
          e("div", { className: "tool-summary-meta" }, `mapping 누적 ${guardObsStats.citationMappingCountTotal}개`)
        ),
        e("div", { className: "tool-summary-card" },
          e("div", { className: "tool-summary-title" }, "telegram guard"),
          e("div", { className: "tool-summary-main" }, `${guardObsStats.telegramGuardMetaBlockedTotal}건`),
          e("div", { className: "tool-summary-meta" }, "telegram guard blocked 집계"),
          e("div", { className: "tool-summary-meta" }, "source=telegram 기준")
        ),
        e("div", { className: "tool-summary-card" },
          e("div", { className: "tool-summary-title" }, "경보 상태"),
          e("div", { className: "tool-summary-main" },
            e("span", { className: `tool-status-chip ${guardAlertSummary.statusTone}` }, guardAlertSummary.statusLabel)
          ),
          e("div", { className: "tool-summary-meta" }, `triggered ${guardAlertSummary.triggeredCount}건`),
          e("div", { className: "tool-summary-meta" }, `sample_pending ${guardAlertSummary.samplePendingCount}건`)
        )
      ),
      renderDisclosure("채널별 guard 상세", "chat/coding/telegram/search", renderTableCard("채널별 guard", "차단, retry, count-lock, citation", guardChannelTable), true),
      renderDisclosure("retry 시계열", `${guardRetryTimeline.bucketMinutes}분 버킷 / 최근 ${guardRetryTimeline.windowMinutes}분`,
        e("div", null,
          renderTableCard("retry 시계열", "최근 retryRequired 흐름", retryTimelineTable),
          e("div", { className: "hint" },
            `source=${guardRetryTimelineSource}`,
            guardRetryTimelineSource === "server_api" && guardRetryTimelineApiFetchedAt ? ` · fetchedAt=${guardRetryTimelineApiFetchedAt}` : "",
            guardRetryTimelineSource === "memory_fallback" && guardRetryTimelineApiError ? ` · fallbackReason=${guardRetryTimelineApiError}` : ""
          )
        )
      ),
      renderDisclosure("경보 임계치와 전송", "warn/critical, schema, dispatch",
        e("div", { className: "tool-ux-stack" },
          renderTableCard("guard 경보 임계치", "warn/critical 상태", guardAlertTable),
          renderTableCard("외부 전송 스키마", "guard_alert_event.v1", guardSchemaTable),
          e("div", { className: "tool-ux-action-strip" },
            e("button", { className: "btn", disabled: !authed, onClick: submitGuardAlertDispatch }, "guard_alert_event.v1 전송"),
            e("span", { className: `tool-status-chip ${guardAlertDispatchState.statusTone}` }, guardAlertDispatchState.statusLabel),
            e("span", { className: "hint" }, `sent=${guardAlertDispatchState.sentCount} failed=${guardAlertDispatchState.failedCount} skipped=${guardAlertDispatchState.skippedCount}`),
            e("span", { className: "hint" }, `at=${guardAlertDispatchState.attemptedAtUtc}`)
          ),
          e("div", { className: "hint" }, guardAlertDispatchState.message || "-"),
          renderTableCard("외부 전송 결과", "대상별 전송 결과", dispatchResultTable),
          e("pre", { className: "screen metrics tool-ux-json-preview" }, guardAlertPipelinePreview),
          renderTableCard("retry 키 집계", "action/reason/stopReason", retryKeyTable)
        )
      )
    ),

    renderSection("4. 제어 요청", "필요한 도구 요청만 찾아 실행하도록 기능별로 묶었습니다.",
      e("div", { className: "tool-control-board" },
        renderControlGroup("상태 조회", "현재 연결 상태만 빠르게 확인",
          e("div", { className: "tool-control-grid five" },
            e("button", { className: "btn", disabled: !authed, onClick: submitSessionsList }, "sessions_list"),
            e("button", { className: "btn", disabled: !authed, onClick: submitCronStatus }, "cron.status"),
            e("button", { className: "btn", disabled: !authed, onClick: submitBrowserStatus }, "browser.status"),
            e("button", { className: "btn", disabled: !authed, onClick: submitCanvasStatus }, "canvas.status"),
            e("button", { className: "btn", disabled: !authed, onClick: submitNodesStatus }, "nodes.status")
          )
        ),
        renderControlGroup("Sessions", "세션 기록 조회, 메시지 전송, 새 작업 생성",
          e("div", { className: "tool-control-stack" },
            e("div", { className: "tool-control-grid three" },
              renderTextInput(toolSessionKey, setToolSessionKey, "sessionKey"),
              e("button", { className: "btn", disabled: !authed, onClick: submitSessionsHistory }, "sessions_history"),
              e("button", { className: "btn", disabled: !authed, onClick: submitSessionSend }, "sessions_send")
            ),
            e("div", { className: "tool-control-grid three" },
              renderTextInput(toolSpawnTask, setToolSpawnTask, "spawn task"),
              e("button", { className: "btn", disabled: !authed, onClick: submitSessionSpawn }, "sessions_spawn"),
              renderTextInput(toolSessionMessage, setToolSessionMessage, "sessions_send message")
            )
          )
        ),
        renderControlGroup("Cron", "작업 목록 조회와 수동 실행",
          e("div", { className: "tool-control-grid three" },
            renderTextInput(toolCronJobId, setToolCronJobId, "cron jobId"),
            e("button", { className: "btn", disabled: !authed, onClick: submitCronList }, "cron.list"),
            e("button", { className: "btn", disabled: !authed, onClick: submitCronRun }, "cron.run")
          )
        ),
        renderControlGroup("Browser / Canvas", "브라우저 이동과 canvas 표시",
          e("div", { className: "tool-control-grid four" },
            renderTextInput(toolBrowserUrl, setToolBrowserUrl, "browser navigate URL"),
            e("button", { className: "btn", disabled: !authed, onClick: submitBrowserNavigate }, "browser.navigate"),
            renderTextInput(toolCanvasTarget, setToolCanvasTarget, "canvas target"),
            e("button", { className: "btn", disabled: !authed, onClick: submitCanvasPresent }, "canvas.present")
          )
        ),
        renderControlGroup("Nodes", "대기 요청 조회와 명령 invoke",
          e("div", { className: "tool-control-stack" },
            e("div", { className: "tool-control-grid three" },
              renderTextInput(toolNodesNode, setToolNodesNode, "nodes node (optional)"),
              renderTextInput(toolNodesRequestId, setToolNodesRequestId, "nodes requestId (optional)"),
              e("button", { className: "btn", disabled: !authed, onClick: submitNodesPending }, "nodes.pending")
            ),
            e("div", { className: "tool-control-grid three" },
              renderTextInput(toolNodesInvokeCommand, setToolNodesInvokeCommand, "nodes invokeCommand"),
              renderTextInput(toolNodesInvokeParamsJson, setToolNodesInvokeParamsJson, "nodes invoke params JSON"),
              e("button", { className: "btn", disabled: !authed, onClick: submitNodesInvoke }, "nodes.invoke")
            )
          )
        ),
        renderControlGroup("Telegram / Web / Memory", "검증용 stub, 검색, fetch, memory probe",
          e("div", { className: "tool-control-stack" },
            e("div", { className: "tool-control-grid three" },
              renderTextInput(toolTelegramStubText, setToolTelegramStubText, "telegram stub text (예: /llm status)"),
              e("button", { className: "btn", disabled: !authed, onClick: submitTelegramStubCommand }, "telegram_stub.command"),
              e("div", { className: "tiny" }, "개발/테스트 전용 우회 경로")
            ),
            e("div", { className: "tool-control-grid four" },
              renderTextInput(toolWebSearchQuery, setToolWebSearchQuery, "web_search query"),
              e("button", { className: "btn", disabled: !authed, onClick: submitWebSearchProbe }, "web_search"),
              renderTextInput(toolWebFetchUrl, setToolWebFetchUrl, "web_fetch url"),
              e("button", { className: "btn", disabled: !authed, onClick: submitWebFetchProbe }, "web_fetch")
            ),
            e("div", { className: "tool-control-grid four" },
              renderTextInput(toolMemorySearchQuery, setToolMemorySearchQuery, "memory_search query"),
              e("button", { className: "btn", disabled: !authed, onClick: submitMemorySearchProbe }, "memory_search"),
              renderTextInput(toolMemoryGetPath, setToolMemoryGetPath, "memory_get path"),
              e("button", { className: "btn", disabled: !authed, onClick: submitMemoryGetProbe }, "memory_get")
            )
          )
        )
      )
    ),

    renderSection("5. 결과 확인", "필터를 선택하고 JSON 프리뷰와 결과 테이블을 확인합니다.",
      e("div", { className: "tool-ux-overview-grid" },
        toolResultGroups.map((group) => {
          const stat = toolResultStats.byGroup[group.key] || { count: 0, errorCount: 0, lastAction: "-", lastStatus: "-" };
          return e("button", {
            key: group.key,
            type: "button",
            className: `tool-summary-card ${toolResultFilter === group.key ? "active" : ""}`,
            onClick: () => setToolResultFilter(group.key)
          },
          e("div", { className: "tool-summary-title" }, group.label),
          e("div", { className: "tool-summary-main" }, `${stat.count}건`),
          e("div", { className: "tool-summary-meta" }, `오류 ${stat.errorCount}건`),
          e("div", { className: "tool-summary-meta" }, `최근 ${stat.lastAction}/${stat.lastStatus}`));
        })
      ),
      e("div", { className: "tool-filter-row" },
        toolResultFilters.map((filterItem) => {
          const count = filterItem.key === "all"
            ? toolResultStats.total
            : (filterItem.key === "errors" ? toolResultStats.errors : ((toolResultStats.byGroup[filterItem.key] || {}).count || 0));
          return e("button", {
            key: filterItem.key,
            type: "button",
            className: `btn tool-filter-btn ${toolResultFilter === filterItem.key ? "active" : ""}`,
            onClick: () => setToolResultFilter(filterItem.key)
          }, `${filterItem.label} (${count})`);
        })
      ),
      e("div", { className: "tool-filter-row" },
        toolDomainFilters.map((domainItem) => {
          const count = domainItem.key === "all" ? toolResultItems.length : ((toolDomainStats[domainItem.key] || {}).count || 0);
          return e("button", {
            key: domainItem.key,
            type: "button",
            className: `btn tool-filter-btn ${opsDomainFilter === domainItem.key ? "active" : ""}`,
            onClick: () => applyDomainFocus(domainItem.key)
          }, `${domainItem.label} (${count})`);
        })
      ),
      e("div", { className: "tool-ux-card tool-ux-preview-card" },
        e("div", { className: "tool-ux-card-head" },
          e("div", null,
            e("strong", null, "선택한 도구 응답(JSON)"),
            e("span", null, "결과 테이블 행을 선택하면 여기에 원문이 표시됩니다.")
          ),
          e("button", { className: "btn ghost", onClick: clearToolControlResults }, "결과 비우기")
        ),
        e("pre", { className: "screen metrics tool-ux-json-preview" }, toolResultPreview)
      ),
      renderTableCard("도구 결과", "최근 수신 결과와 오류 요약", resultTable)
    )
  );
}
