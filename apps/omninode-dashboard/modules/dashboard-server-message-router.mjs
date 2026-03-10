function createItemId() {
  return `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
}

export function summarizeToolResult(msg) {
  if (!msg || typeof msg !== "object") {
    return "도구 응답 수신"
  }

  if (msg.type === "sessions_list_result") {
    return `sessions_list count=${msg.count || 0}`
  }

  if (msg.type === "sessions_history_result") {
    return `sessions_history status=${msg.status || "-"} count=${msg.count || 0}`
  }

  if (msg.type === "sessions_send_result") {
    return `sessions_send status=${msg.status || "-"} runId=${msg.runId || "-"}`
  }

  if (msg.type === "sessions_spawn_result") {
    return `sessions_spawn status=${msg.status || "-"} child=${msg.childSessionKey || "-"} runtime=${msg.runtime || "-"}`
  }

  if (msg.type === "cron_result") {
    const action = msg.action || "-"
    if (action === "status") {
      return `cron.status enabled=${msg.enabled ? "true" : "false"} jobs=${msg.jobs ?? 0}`
    }
    if (action === "list") {
      return `cron.list total=${msg.total ?? 0} hasMore=${msg.hasMore ? "true" : "false"}`
    }
    return `cron.${action} ok=${msg.ok ? "true" : "false"}`
  }

  if (msg.type === "browser_result") {
    return `browser.${msg.action || "-"} ok=${msg.ok ? "true" : "false"} running=${msg.running ? "true" : "false"} tabs=${Array.isArray(msg.tabs) ? msg.tabs.length : 0}`
  }

  if (msg.type === "canvas_result") {
    return `canvas.${msg.action || "-"} ok=${msg.ok ? "true" : "false"} visible=${msg.visible ? "true" : "false"} target=${msg.target || "-"}`
  }

  if (msg.type === "nodes_result") {
    return `nodes.${msg.action || "-"} ok=${msg.ok ? "true" : "false"} nodes=${Array.isArray(msg.nodes) ? msg.nodes.length : 0} pending=${Array.isArray(msg.pendingRequests) ? msg.pendingRequests.length : 0}`
  }

  if (msg.type === "telegram_stub_result") {
    const head = (msg.input || "").trim()
    const shortHead = head.length > 40 ? `${head.slice(0, 40)}...` : (head || "-")
    return `telegram.stub status=${msg.status || "-"} ok=${msg.ok ? "true" : "false"} input=${shortHead}`
  }

  if (msg.type === "web_search_result") {
    const head = (msg.query || "").trim()
    const shortHead = head.length > 40 ? `${head.slice(0, 40)}...` : (head || "-")
    const provider = msg.provider || "-"
    const count = Array.isArray(msg.results) ? msg.results.length : 0
    return `web.search provider=${provider} results=${count} query=${shortHead}`
  }

  if (msg.type === "web_fetch_result") {
    return `web.fetch status=${msg.status ?? "-"} len=${msg.length ?? 0} url=${msg.url || msg.requestedUrl || "-"}`
  }

  if (msg.type === "memory_search_result") {
    const count = Array.isArray(msg.results) ? msg.results.length : 0
    return `memory.search disabled=${msg.disabled ? "true" : "false"} results=${count} query=${msg.query || "-"}`
  }

  if (msg.type === "memory_get_result") {
    const text = typeof msg.text === "string" ? msg.text : ""
    return `memory.get disabled=${msg.disabled ? "true" : "false"} path=${msg.path || msg.requestedPath || "-"} chars=${text.length}`
  }

  return msg.type || "도구 응답"
}

function pushToolResult(msg, context) {
  const {
    setters,
    utils
  } = context
  const summary = summarizeToolResult(msg)
  const capturedAt = new Date().toISOString()
  const normalizedType = msg && msg.type ? String(msg.type) : "unknown"
  const group = utils.inferToolResultGroup(normalizedType)
  const domain = utils.inferToolResultDomain(group)
  const action = utils.inferToolResultAction(msg)
  const statusInfo = utils.inferToolResultStatus(msg)
  const preview = JSON.stringify(msg || {}, null, 2)
  const errorText = msg && typeof msg.error === "string" ? msg.error.trim() : ""
  const itemId = createItemId()

  setters.setToolResultPreview(preview)
  setters.setSelectedToolResultId(itemId)
  setters.setToolResultItems((prev) => {
    const next = [
      {
        id: itemId,
        type: normalizedType,
        group,
        domain,
        action,
        statusLabel: statusInfo.label,
        statusTone: statusInfo.tone,
        hasError: statusInfo.hasError,
        summary,
        capturedAt,
        errorText,
        preview
      },
      ...prev
    ]
    return next.slice(0, 16)
  })

  if (msg && typeof msg.childSessionKey === "string" && msg.childSessionKey.trim()) {
    setters.setToolSessionKey(msg.childSessionKey.trim())
  }

  if (errorText) {
    setters.setToolControlError(errorText)
    return
  }

  setters.setToolControlError("")
}

function pushProviderRuntimeEvents(msg, context) {
  const { setters, utils } = context
  const events = utils.buildProviderRuntimeEventsFromMessage(msg)
  if (!Array.isArray(events) || events.length === 0) {
    return
  }

  const capturedAt = new Date().toISOString()
  setters.setProviderRuntimeItems((prev) => {
    const next = [
      ...events.map((event) => {
        const safeProvider = utils.PROVIDER_RUNTIME_KEYS.includes(event.provider)
          ? event.provider
          : "unknown"
        const normalized = {
          provider: safeProvider,
          scope: event.scope || "runtime",
          mode: event.mode || "-",
          model: event.model || "",
          statusLabel: event.statusLabel || "-",
          statusTone: event.statusTone || "neutral",
          hasError: !!event.hasError,
          detail: event.detail || ""
        }
        return {
          id: createItemId(),
          capturedAt,
          ...normalized,
          summary: utils.summarizeProviderRuntimeEntry(normalized)
        }
      }),
      ...prev
    ]
    return next.slice(0, 48)
  })
}

function pushGuardObsEvent(msg, context) {
  const { setters, utils } = context
  const event = utils.buildGuardObsEvent(msg)
  if (!event) {
    return
  }

  const capturedAt = new Date().toISOString()
  setters.setGuardObsItems((prev) => {
    const next = [
      {
        id: createItemId(),
        capturedAt,
        ...event
      },
      ...prev
    ]
    return next.slice(0, 64)
  })

  const timelineEntry = utils.buildGuardRetryTimelineEntry(event, capturedAt)
  if (!timelineEntry) {
    return
  }

  setters.setGuardRetryTimelineItems((prev) => {
    const next = [timelineEntry, ...prev]
    return next.slice(0, utils.GUARD_RETRY_TIMELINE_MAX_ENTRIES)
  })
}

function handleGuardAlertDispatchResult(msg, context) {
  const {
    setters,
    actions
  } = context
  const ok = !!msg.ok
  const statusLabel = (typeof msg.status === "string" && msg.status.trim())
    ? msg.status.trim()
    : (ok ? "sent" : "failed")
  const attemptedAtUtc = (typeof msg.attemptedAtUtc === "string" && msg.attemptedAtUtc.trim())
    ? msg.attemptedAtUtc.trim()
    : new Date().toISOString()
  const targets = Array.isArray(msg.targets)
    ? msg.targets.map((item) => ({
      name: item && item.name ? `${item.name}` : "-",
      status: item && item.status ? `${item.status}` : "-",
      attempts: Number.isFinite(Number(item && item.attempts)) ? Number(item.attempts) : 0,
      statusCode: Number.isFinite(Number(item && item.statusCode)) ? Number(item.statusCode) : null,
      error: item && item.error ? `${item.error}` : "-",
      endpoint: item && item.endpoint ? `${item.endpoint}` : "-"
    }))
    : []

  setters.setGuardAlertDispatchState({
    statusLabel,
    statusTone: ok ? "ok" : "error",
    message: msg.message || (ok ? "guard alert 전송 완료" : "guard alert 전송 실패"),
    attemptedAtUtc,
    sentCount: Number.isFinite(Number(msg.sentCount)) ? Number(msg.sentCount) : targets.filter((item) => item.status === "sent").length,
    failedCount: Number.isFinite(Number(msg.failedCount)) ? Number(msg.failedCount) : targets.filter((item) => item.status === "failed").length,
    skippedCount: Number.isFinite(Number(msg.skippedCount)) ? Number(msg.skippedCount) : targets.filter((item) => item.status === "skipped").length,
    targets
  })
  actions.log(`[guard-alert] ${msg.message || (ok ? "전송 완료" : "전송 실패")}`, ok ? "info" : "error")
}

function handleUnauthorizedError(msg, context) {
  const {
    state,
    setters,
    actions
  } = context

  actions.clearAuthToken()
  setters.setAuthed(false)
  setters.setStatus("인증 필요")
  setters.setCodingRuntimeByConversation((prev) => {
    const entries = Object.entries(prev || {})
    const pendingIds = entries
      .filter(([, runtime]) => runtime && runtime.pending)
      .map(([conversationId]) => conversationId)
    if (pendingIds.length === 0 && !state.currentConversationId) {
      return prev
    }

    const next = { ...prev }
    pendingIds.forEach((conversationId) => {
      next[conversationId] = actions.buildCodingRuntimeMessageState(
        "세션 인증이 만료되었습니다. 설정 탭에서 OTP 인증 후 다시 시도하세요.",
        false,
        false
      )
    })

    if (state.currentConversationId && !next[state.currentConversationId]) {
      next[state.currentConversationId] = actions.buildCodingRuntimeMessageState(
        "세션 인증이 만료되었습니다. 설정 탭에서 OTP 인증 후 다시 시도하세요.",
        false,
        false
      )
    }

    return next
  })
}

export function handleDashboardServerMessage(msg, context) {
  if (!msg || typeof msg !== "object") {
    return false
  }

  pushProviderRuntimeEvents(msg, context)
  pushGuardObsEvent(msg, context)

  const {
    state,
    refs,
    setters,
    actions,
    handlers,
    utils
  } = context

  if (msg.type === "pong") {
    return true
  }

  if (msg.type === "auth_required") {
    setters.setAuthMeta({ sessionId: msg.sessionId || "", telegramConfigured: !!msg.telegramConfigured })
    return true
  }

  if (msg.type === "otp_request_result") {
    actions.log(msg.message || "OTP 요청 결과를 확인하세요.", msg.ok ? "info" : "error")
    return true
  }

  if (msg.type === "auth_result") {
    const ok = !!msg.ok
    setters.setAuthed(ok)
    if (ok) {
      const expiryText = msg.expiresAtLocal || msg.expiresAtUtc || ""
      if (msg.authToken) {
        actions.saveAuthToken(msg.authToken, expiryText)
      }
      setters.setAuthLocalOffset(msg.localUtcOffset || "")
      if (Number.isFinite(msg.ttlHours) && Number(msg.ttlHours) > 0) {
        setters.setAuthTtlHours(String(msg.ttlHours))
      }
      setters.setStatus("세션 인증됨")
      actions.send({ type: "get_routines" })
      actions.requestDoctorLast(actions.send, { silent: true, queueIfClosed: false })
      actions.requestRoutingPolicyGet(actions.send, { silent: true, queueIfClosed: false })
      actions.requestRoutingDecisionGetLast(actions.send, { silent: true, queueIfClosed: false })
      actions.requestPlanList(actions.send, { silent: true, queueIfClosed: false })
      actions.requestTaskGraphList(actions.send, { silent: true, queueIfClosed: false })
      actions.requestContextScan(actions.send, { silent: true, queueIfClosed: false })
      actions.requestSkillsList(actions.send, { silent: true, queueIfClosed: false })
      actions.requestCommandsList(actions.send, { silent: true, queueIfClosed: false })
      actions.requestNotebookGet(actions.send, "", { silent: true, queueIfClosed: false })
      return true
    }

    if (msg.resumed) {
      actions.clearAuthToken()
    }
    setters.setAuthLocalOffset(actions.localUtcOffsetLabel())
    setters.setStatus(msg.resumed ? "세션 만료 / OTP 필요" : "인증 실패")
    return true
  }

  if (msg.type === "context_scan_result") {
    const payload = msg.payload || null
    const sources = Array.isArray(payload?.instructions?.sources) ? payload.instructions.sources : []
    const skills = Array.isArray(payload?.skills) ? payload.skills : []
    const commands = Array.isArray(payload?.commands) ? payload.commands : []

    setters.setContextState((prev) => ({
      ...prev,
      loaded: !!payload,
      loading: false,
      lastError: payload ? "" : "프로젝트 문맥 스냅샷이 비어 있습니다.",
      lastAction: "scan",
      snapshot: payload,
      skills: skills.length > 0 ? skills : prev.skills,
      commands: commands.length > 0 ? commands : prev.commands
    }))

    if (payload) {
      actions.log(
        `[context] scan · instructions=${sources.length} skills=${skills.length} commands=${commands.length}`,
        "info"
      )
    }
    return true
  }

  if (msg.type === "skills_list_result") {
    const payload = msg.payload || {}
    const items = Array.isArray(payload.items) ? payload.items : []
    setters.setContextState((prev) => ({
      ...prev,
      loadingSkills: false,
      lastError: "",
      lastAction: "skills",
      skills: items
    }))
    return true
  }

  if (msg.type === "commands_list_result") {
    const payload = msg.payload || {}
    const items = Array.isArray(payload.items) ? payload.items : []
    setters.setContextState((prev) => ({
      ...prev,
      loadingCommands: false,
      lastError: "",
      lastAction: "commands",
      commands: items
    }))
    return true
  }

  if (msg.type === "routing_policy_result") {
    const payload = msg.payload || {}
    const snapshot = payload.snapshot || null
    const ok = payload.ok !== false
    const action = msg.action || state.routingPolicyState.lastAction || ""
    const effective = snapshot?.effectiveChains || {}
    const draftChains = Object.keys(effective).reduce((acc, key) => {
      acc[key] = Array.isArray(effective[key]) ? effective[key].join(", ") : ""
      return acc
    }, {})

    setters.setRoutingPolicyState((prev) => ({
      ...prev,
      loaded: true,
      loading: false,
      pending: false,
      lastAction: action,
      lastError: ok ? "" : (payload.message || "라우팅 정책 요청이 실패했습니다."),
      snapshot: snapshot || prev.snapshot,
      draftChains: Object.keys(draftChains).length > 0 ? draftChains : prev.draftChains
    }))

    actions.log(
      `[routing] ${action || "action"} · ${payload.message || (ok ? "완료" : "실패")}`,
      ok ? "info" : "error"
    )
    return true
  }

  if (msg.type === "routing_decision_result") {
    const decision = msg.payload || null
    setters.setRoutingPolicyState((prev) => ({
      ...prev,
      snapshot: prev.snapshot
        ? {
          ...prev.snapshot,
          lastDecision: decision
        }
        : {
          defaultChains: {},
          overrideChains: {},
          effectiveChains: {},
          lastDecision: decision
        }
    }))
    return true
  }

  if (msg.type === "plan_list_result") {
    const payload = msg.payload || {}
    const items = Array.isArray(payload.items) ? payload.items : []
    const nextSelectedPlanId = items.some((item) => item.planId === state.plansState.selectedPlanId)
      ? state.plansState.selectedPlanId
      : (items[0]?.planId || "")

    setters.setPlansState((prev) => ({
      ...prev,
      items,
      loaded: true,
      loading: false,
      pending: false,
      lastError: items.length === 0 ? "저장된 계획이 없습니다." : "",
      selectedPlanId: nextSelectedPlanId || "",
      snapshot: nextSelectedPlanId && prev.snapshot?.plan?.planId === nextSelectedPlanId
        ? prev.snapshot
        : (nextSelectedPlanId ? null : prev.snapshot)
    }))

    if (nextSelectedPlanId) {
      actions.requestPlanGet(actions.send, nextSelectedPlanId, { silent: true, queueIfClosed: false })
    }

    return true
  }

  if (msg.type === "plan_result") {
    const payload = msg.payload || {}
    const snapshot = payload.snapshot || null
    const ok = payload.ok !== false
    const action = msg.action || state.plansState.lastAction || ""
    const nextPlanId = snapshot?.plan?.planId || state.plansState.selectedPlanId || ""

    setters.setPlansState((prev) => {
      const items = Array.isArray(prev.items) ? [...prev.items] : []
      if (snapshot?.plan?.planId) {
        const nextItem = {
          planId: snapshot.plan.planId,
          title: snapshot.plan.title || snapshot.plan.planId,
          objective: snapshot.plan.objective || "",
          status: snapshot.plan.status || "Draft",
          createdAtUtc: snapshot.plan.createdAtUtc || "",
          updatedAtUtc: snapshot.plan.updatedAtUtc || "",
          reviewerSummary: snapshot.plan.reviewerSummary || ""
        }
        const itemIndex = items.findIndex((item) => item.planId === nextItem.planId)
        if (itemIndex >= 0) {
          items[itemIndex] = nextItem
        } else {
          items.unshift(nextItem)
        }
      }

      return {
        ...prev,
        items,
        loaded: true,
        loading: false,
        pending: false,
        lastAction: action,
        lastError: ok ? "" : (payload.message || "계획 요청이 실패했습니다."),
        selectedPlanId: nextPlanId,
        snapshot: snapshot || prev.snapshot,
        createObjective: ok && action === "create" ? "" : prev.createObjective,
        createConstraintsText: ok && action === "create" ? "" : prev.createConstraintsText,
        createMode: ok && action === "create" ? "fast" : prev.createMode
      }
    })

    actions.log(
      `[plan] ${action || "action"} · ${payload.message || (ok ? "완료" : "실패")}`,
      ok ? "info" : "error"
    )

    if (ok && action !== "get") {
      actions.requestPlanList(actions.send, { silent: true, queueIfClosed: false })
    }

    if (ok && action === "run") {
      actions.requestTaskGraphList(actions.send, { silent: true, queueIfClosed: false })
    }

    return true
  }

  if (msg.type === "task_graph_list_result") {
    const payload = msg.payload || {}
    const items = Array.isArray(payload.items) ? payload.items : []
    const nextSelectedGraphId = items.some((item) => item.graphId === state.taskGraphState.selectedGraphId)
      ? state.taskGraphState.selectedGraphId
      : (items[0]?.graphId || "")

    setters.setTaskGraphState((prev) => ({
      ...prev,
      items,
      loaded: true,
      loading: false,
      pending: false,
      lastError: items.length === 0 ? "저장된 Task graph가 없습니다." : "",
      selectedGraphId: nextSelectedGraphId || "",
      snapshot: nextSelectedGraphId && prev.snapshot?.graph?.graphId === nextSelectedGraphId
        ? prev.snapshot
        : (nextSelectedGraphId ? null : prev.snapshot)
    }))

    if (nextSelectedGraphId) {
      actions.requestTaskGraphGet(actions.send, nextSelectedGraphId, { silent: true, queueIfClosed: false })
    }

    return true
  }

  if (msg.type === "task_graph_result") {
    const payload = msg.payload || {}
    const snapshot = payload.snapshot || null
    const ok = payload.ok !== false
    const action = msg.action || state.taskGraphState.lastAction || ""
    const nextGraphId = snapshot?.graph?.graphId || state.taskGraphState.selectedGraphId || ""
    const nextTaskId = snapshot?.graph?.nodes?.some((task) => task.taskId === state.taskGraphState.selectedTaskId)
      ? state.taskGraphState.selectedTaskId
      : (snapshot?.graph?.nodes?.[0]?.taskId || "")

    setters.setTaskGraphState((prev) => {
      const items = Array.isArray(prev.items) ? [...prev.items] : []
      if (snapshot?.graph?.graphId) {
        const nextItem = {
          graphId: snapshot.graph.graphId,
          sourcePlanId: snapshot.graph.sourcePlanId || "",
          status: snapshot.graph.status || "Draft",
          createdAtUtc: snapshot.graph.createdAtUtc || "",
          updatedAtUtc: snapshot.graph.updatedAtUtc || "",
          totalNodes: Array.isArray(snapshot.graph.nodes) ? snapshot.graph.nodes.length : 0,
          completedNodes: Array.isArray(snapshot.graph.nodes)
            ? snapshot.graph.nodes.filter((task) => task.status === "Completed").length
            : 0,
          failedNodes: Array.isArray(snapshot.graph.nodes)
            ? snapshot.graph.nodes.filter((task) => task.status === "Failed").length
            : 0,
          runningNodes: Array.isArray(snapshot.graph.nodes)
            ? snapshot.graph.nodes.filter((task) => task.status === "Running").length
            : 0
        }
        const itemIndex = items.findIndex((item) => item.graphId === nextItem.graphId)
        if (itemIndex >= 0) {
          items[itemIndex] = nextItem
        } else {
          items.unshift(nextItem)
        }
      }

      return {
        ...prev,
        items,
        loaded: true,
        loading: false,
        pending: false,
        lastAction: action,
        lastError: ok ? "" : (payload.message || "Task graph 요청이 실패했습니다."),
        selectedGraphId: nextGraphId,
        selectedTaskId: nextTaskId,
        snapshot: snapshot || prev.snapshot,
        createPlanId: ok && action === "create" ? "" : prev.createPlanId
      }
    })

    actions.log(
      `[task-graph] ${action || "action"} · ${payload.message || (ok ? "완료" : "실패")}`,
      ok ? "info" : "error"
    )

    if (nextGraphId && nextTaskId) {
      actions.requestTaskOutput(actions.send, nextGraphId, nextTaskId, { silent: true, queueIfClosed: false })
    }

    if (ok && action !== "get") {
      actions.requestTaskGraphList(actions.send, { silent: true, queueIfClosed: false })
    }

    const normalizedGraphStatus = `${snapshot?.graph?.status || ""}`.toLowerCase()
    const sourcePlanId = `${snapshot?.graph?.sourcePlanId || ""}`.trim()
    if (
      sourcePlanId
      && sourcePlanId === `${state.plansState.selectedPlanId || ""}`.trim()
      && (normalizedGraphStatus === "completed" || normalizedGraphStatus === "failed" || normalizedGraphStatus === "canceled")
    ) {
      actions.requestPlanGet(actions.send, sourcePlanId, { silent: true, queueIfClosed: false })
    }

    return true
  }

  if (msg.type === "task_output_result") {
    const payload = msg.payload || {}
    setters.setTaskGraphState((prev) => ({
      ...prev,
      output: payload,
      selectedGraphId: payload.graphId || prev.selectedGraphId,
      selectedTaskId: payload.taskId || prev.selectedTaskId
    }))
    return true
  }

  if (msg.type === "task_updated") {
    const graphId = `${msg.graphId || ""}`.trim()
    const task = msg.task || null
    if (!graphId || !task?.taskId) {
      return true
    }

    if (state.taskGraphState.selectedGraphId === graphId) {
      actions.requestTaskGraphGet(actions.send, graphId, { silent: true, queueIfClosed: false })
      if (state.taskGraphState.selectedTaskId === task.taskId) {
        actions.requestTaskOutput(actions.send, graphId, task.taskId, { silent: true, queueIfClosed: false })
      }
    }

    actions.requestTaskGraphList(actions.send, { silent: true, queueIfClosed: false })

    const normalizedStatus = `${task.status || ""}`.toLowerCase()
    if (normalizedStatus === "failed" || normalizedStatus === "completed" || normalizedStatus === "canceled") {
      actions.log(
        `[task] ${task.taskId} ${task.status || "-"} · ${task.title || "-"}`,
        normalizedStatus === "failed" ? "error" : "info"
      )
    }
    return true
  }

  if (msg.type === "task_log") {
    const graphId = `${msg.graphId || ""}`.trim()
    const taskId = `${msg.taskId || ""}`.trim()
    const line = `${msg.line || ""}`
    if (graphId && taskId && state.taskGraphState.selectedGraphId === graphId && state.taskGraphState.selectedTaskId === taskId) {
      setters.setTaskGraphState((prev) => ({
        ...prev,
        output: {
          ...(prev.output || {}),
          graphId,
          taskId,
          stdout: `${prev.output?.stdout || ""}${prev.output?.stdout ? "\n" : ""}${line}`
        }
      }))
    }
    return true
  }

  if (msg.type === "refactor_result") {
    const payload = msg.payload || {}
    const ok = payload.ok !== false
    const action = msg.action || state.refactorState.lastAction || ""
    const readResult = payload.readResult || null
    const preview = payload.preview || null
    const applyResult = payload.applyResult || null
    const issues = Array.isArray(payload.issues)
      ? payload.issues
      : Array.isArray(preview?.issues)
        ? preview.issues
        : Array.isArray(applyResult?.issues)
          ? applyResult.issues
          : []

    setters.setRefactorState((prev) => ({
      ...prev,
      pending: false,
      lastAction: action,
      lastError: ok ? "" : (payload.message || "Safe Refactor 요청이 실패했습니다."),
      lastMessage: payload.message || (ok ? "완료" : ""),
      lastIssues: issues,
      toolResult: payload.toolResult || null,
      filePath: readResult?.path || preview?.path || applyResult?.path || prev.filePath,
      loadedPath: readResult?.path || preview?.path || applyResult?.path || prev.loadedPath,
      readResult: action === "read"
        ? (readResult || prev.readResult)
        : action === "apply" && ok
          ? prev.readResult
          : prev.readResult,
      preview: ["preview", "lsp_rename", "ast_replace"].includes(action)
        ? (preview || null)
        : action === "apply" && ok && applyResult?.applied
          ? null
          : prev.preview,
      selectedStartLine: action === "read" && readResult ? "" : prev.selectedStartLine,
      selectedEndLine: action === "read" && readResult ? "" : prev.selectedEndLine,
      replacement: action === "apply" && ok && applyResult?.applied && prev.mode === "anchor" ? "" : prev.replacement
    }))

    actions.log(
      `[refactor] ${action || "action"} · ${payload.message || (ok ? "완료" : "실패")}`,
      ok ? "info" : "error"
    )

    if (action === "apply" && ok && applyResult?.applied && applyResult.path) {
      actions.requestRefactorRead(actions.send, applyResult.path, { silent: true, queueIfClosed: false })
    }

    return true
  }

  if (msg.type === "doctor_result") {
    const report = msg.report || null
    const found = !!msg.found
    setters.setDoctorState((prev) => ({
      ...prev,
      report: found ? report : null,
      loaded: true,
      loading: false,
      pending: false,
      lastAction: msg.action || prev.lastAction || "",
      lastError: found ? "" : "저장된 doctor 보고서가 없습니다.",
      receivedAt: new Date().toISOString()
    }))

    if (found && report) {
      const failCount = Number(report.failCount || 0)
      const warnCount = Number(report.warnCount || 0)
      actions.log(
        `[doctor] ${msg.action === "run" ? "실행" : "조회"} 완료 · ok=${report.okCount || 0} warn=${warnCount} fail=${failCount}`,
        failCount > 0 ? "error" : "info"
      )
    } else if (msg.action === "get_last") {
      actions.log("[doctor] 저장된 보고서가 없습니다.", "info")
    }
    return true
  }

  if (msg.type === "notebook_result") {
    const payload = msg.payload || {}
    const snapshot = payload.snapshot || null
    const ok = payload.ok !== false
    const action = msg.action || state.notebooksState.lastAction || ""

    setters.setNotebooksState((prev) => ({
      ...prev,
      loaded: true,
      loading: false,
      pending: false,
      lastAction: action,
      lastError: ok ? "" : (payload.message || "notebook 요청이 실패했습니다."),
      lastMessage: ok ? (payload.message || "") : "",
      snapshot: snapshot || prev.snapshot,
      receivedAt: new Date().toISOString(),
      projectKeyDraft: prev.projectKeyDraft || snapshot?.notebook?.projectKey || "",
      appendText: ok && action === "append" ? "" : prev.appendText
    }))

    actions.log(
      `[notebook] ${action || "action"} · ${payload.message || (ok ? "완료" : "실패")}`,
      ok ? "info" : "error"
    )
    return true
  }

  if (msg.type === "settings_state") {
    setters.setSettingsState({
      telegramBotTokenSet: !!msg.telegramBotTokenSet,
      telegramChatIdSet: !!msg.telegramChatIdSet,
      groqApiKeySet: !!msg.groqApiKeySet,
      geminiApiKeySet: !!msg.geminiApiKeySet,
      cerebrasApiKeySet: !!msg.cerebrasApiKeySet,
      codexApiKeySet: !!msg.codexApiKeySet,
      telegramBotTokenMasked: msg.telegramBotTokenMasked || "",
      telegramChatIdMasked: msg.telegramChatIdMasked || "",
      groqApiKeyMasked: msg.groqApiKeyMasked || "",
      geminiApiKeyMasked: msg.geminiApiKeyMasked || "",
      cerebrasApiKeyMasked: msg.cerebrasApiKeyMasked || "",
      codexApiKeyMasked: msg.codexApiKeyMasked || ""
    })
    return true
  }

  if (msg.type === "settings_result") {
    actions.log(msg.message || "설정 적용 완료", msg.ok === false ? "error" : "info")
    actions.send({ type: "get_settings" })
    actions.send({ type: "get_codex_status" })
    actions.send({ type: "get_groq_models" })
    actions.send({ type: "get_copilot_models" })
    actions.send({ type: "get_usage_stats" })
    return true
  }

  if (msg.type === "usage_stats") {
    setters.setGeminiUsage({
      requests: msg.gemini?.requests || 0,
      prompt_tokens: msg.gemini?.prompt_tokens || 0,
      completion_tokens: msg.gemini?.completion_tokens || 0,
      total_tokens: msg.gemini?.total_tokens || 0,
      input_price_per_million_usd: msg.gemini?.input_price_per_million_usd || "0.5000",
      output_price_per_million_usd: msg.gemini?.output_price_per_million_usd || "3.0000",
      estimated_cost_usd: msg.gemini?.estimated_cost_usd || "0.000000"
    })

    const hasPremiumPayload = Object.prototype.hasOwnProperty.call(msg, "copilotPremium")
    const premium = hasPremiumPayload ? (msg.copilotPremium || {}) : {}
    const premiumItems = Array.isArray(premium.items) ? premium.items : []
    const premiumFallbackMessage = hasPremiumPayload
      ? (premium.message || "Copilot Premium 응답 메시지가 비어 있습니다.")
      : "미들웨어가 copilotPremium 필드를 보내지 않았습니다. 미들웨어 재시작 후 다시 시도하세요."
    const local = msg.copilotLocal || {}
    const localItems = Array.isArray(local.items) ? local.items : []

    setters.setCopilotPremiumUsage({
      available: hasPremiumPayload ? !!premium.available : false,
      requires_user_scope: hasPremiumPayload ? !!premium.requires_user_scope : false,
      message: premiumFallbackMessage,
      username: premium.username || "",
      plan_name: premium.plan_name || "-",
      used_requests: premium.used_requests || "0.0",
      monthly_quota: premium.monthly_quota || "0.0",
      percent_used: premium.percent_used || "0.00",
      refreshed_local: premium.refreshed_local || "",
      features_url: premium.features_url || "https://github.com/settings/copilot/features",
      billing_url: premium.billing_url || "https://github.com/settings/billing/premium_requests_usage",
      items: premiumItems.map((item) => ({
        model: item.model || "-",
        requests: item.requests || "0.0",
        percent: item.percent || "0.00"
      }))
    })

    setters.setCopilotLocalUsage({
      selected_model: local.selected_model || "",
      selected_model_requests: Number(local.selected_model_requests || 0),
      total_requests: Number(local.total_requests || 0),
      items: localItems.map((item) => ({
        model: item.model || "-",
        requests: Number(item.requests || 0)
      }))
    })
    return true
  }

  if (msg.type === "copilot_status") {
    const installed = !!msg.installed
    const authenticated = !!msg.authenticated
    const text = installed
      ? (authenticated ? `설치/인증 완료 (${msg.mode || "-"})` : `설치됨, 미인증 (${msg.mode || "-"})`)
      : "미설치"
    setters.setCopilotStatus(text)
    setters.setCopilotDetail(msg.detail || "-")
    return true
  }

  if (msg.type === "copilot_login_result") {
    actions.log(`Copilot 로그인 결과: ${msg.message || "-"}`)
    actions.send({ type: "get_copilot_status" })
    actions.send({ type: "get_copilot_models" })
    return true
  }

  if (msg.type === "codex_status") {
    const installed = !!msg.installed
    const authenticated = !!msg.authenticated
    const text = installed
      ? (authenticated ? `설치/인증 완료 (${msg.mode || "-"})` : `설치됨, 미인증 (${msg.mode || "-"})`)
      : "미설치"
    setters.setCodexStatus(text)
    setters.setCodexDetail(msg.detail || "-")
    return true
  }

  if (msg.type === "codex_login_result") {
    const resultMessage = msg.message || "-"
    actions.log(`Codex 로그인 결과: ${resultMessage}`)
    if (/code=.*url=/i.test(resultMessage)) {
      setters.setCodexStatus("설치됨, 미인증 (device_auth)")
      setters.setCodexDetail(resultMessage)
    } else if (/failed|429|Too Many Requests|error/i.test(resultMessage)) {
      setters.setCodexStatus("설치됨, 미인증 (codex)")
      setters.setCodexDetail(resultMessage)
    }
    actions.send({ type: "get_codex_status" })
    return true
  }

  if (msg.type === "codex_logout_result") {
    const resultMessage = msg.message || "-"
    actions.log(`Codex 로그아웃 결과: ${resultMessage}`)
    setters.setCodexStatus("설치됨, 미인증 (codex)")
    setters.setCodexDetail(resultMessage)
    actions.send({ type: "get_codex_status" })
    return true
  }

  if (msg.type === "groq_models") {
    const items = Array.isArray(msg.items) ? msg.items : []
    setters.setGroqModels(items)
    setters.setSelectedGroqModel(msg.selected || state.defaultGroqSingleModel)
    return true
  }

  if (msg.type === "copilot_models") {
    const items = Array.isArray(msg.items) ? msg.items : []
    setters.setCopilotModels(items)
    setters.setSelectedCopilotModel(msg.selected || "")
    return true
  }

  if (msg.type === "groq_model_set" && msg.ok) {
    setters.setSelectedGroqModel(msg.model || "")
    return true
  }

  if (msg.type === "copilot_model_set" && msg.ok) {
    setters.setSelectedCopilotModel(msg.model || "")
    return true
  }

  if (typeof handlers.handleConversationMemoryMessage === "function" && handlers.handleConversationMemoryMessage(msg, {
    autoCreateConversationRef: refs.autoCreateConversationRef,
    setSelectedConversationIdsByKey: setters.setSelectedConversationIdsByKey,
    setSelectedFoldersByKey: setters.setSelectedFoldersByKey,
    setConversationLists: setters.setConversationLists,
    setActiveConversationByKey: setters.setActiveConversationByKey,
    requestConversationDetail: actions.requestConversationDetail,
    requestAutoCreateConversation: actions.requestAutoCreateConversation,
    setConversationDetails: setters.setConversationDetails,
    setSelectedMemoryByConversation: setters.setSelectedMemoryByConversation,
    setChatMultiResultByConversation: setters.setChatMultiResultByConversation,
    log: actions.log,
    setMemoryNotes: setters.setMemoryNotes,
    setMemoryPreview: setters.setMemoryPreview,
    currentConversationId: state.currentConversationId,
    send: actions.send,
    setError: actions.setError,
    currentKey: state.currentKey,
    setFilePreviewByConversation: setters.setFilePreviewByConversation,
    filePreviewByConversation: state.filePreviewByConversation,
    setCodingResultByConversation: setters.setCodingResultByConversation,
    codingResultByConversation: state.codingResultByConversation,
    setShowExecutionLogsByConversation: setters.setShowExecutionLogsByConversation,
    setCodingRuntimeByConversation: setters.setCodingRuntimeByConversation,
    setCodingExecutionInputByConversation: setters.setCodingExecutionInputByConversation,
    requestWorkspaceFilePreview: actions.requestWorkspaceFilePreview
  })) {
    return true
  }

  if (typeof handlers.handleRoutineMessage === "function" && handlers.handleRoutineMessage(msg, {
    setRoutines: setters.setRoutines,
    setRoutineSelectedId: setters.setRoutineSelectedId,
    setRoutineProgress: setters.setRoutineProgress,
    isPortraitMobileLayout: state.isPortraitMobileLayout,
    setResponsivePane: actions.setResponsivePane,
    log: actions.log,
    setError: actions.setError,
    routineBrowserAgentPreviewRef: refs.routineBrowserAgentPreviewRef,
    send: actions.send,
    setRoutineOutputPreview: setters.setRoutineOutputPreview
  })) {
    return true
  }

  if (typeof handlers.handleExecutionFlowMessage === "function" && handlers.handleExecutionFlowMessage(msg, {
    setCodingProgressByKey: setters.setCodingProgressByKey,
    setPendingByKey: setters.setPendingByKey,
    setActiveConversationByKey: setters.setActiveConversationByKey,
    setOptimisticUserByKey: setters.setOptimisticUserByKey,
    normalizeChatMultiResultMessage: utils.normalizeChatMultiResultMessage,
    setChatMultiResultByConversation: setters.setChatMultiResultByConversation,
    attachLatencyMetaToConversation: utils.attachLatencyMetaToConversation,
    setConversationDetails: setters.setConversationDetails,
    setSelectedMemoryByConversation: setters.setSelectedMemoryByConversation,
    finishPendingRequest: actions.finishPendingRequest,
    setError: actions.setError,
    setCodingResultByConversation: setters.setCodingResultByConversation,
    codingResultByConversation: state.codingResultByConversation,
    setShowExecutionLogsByConversation: setters.setShowExecutionLogsByConversation,
    setCodingRuntimeByConversation: setters.setCodingRuntimeByConversation,
    setFilePreviewByConversation: setters.setFilePreviewByConversation,
    filePreviewByConversation: state.filePreviewByConversation,
    requestWorkspaceFilePreview: actions.requestWorkspaceFilePreview,
    send: actions.send,
    log: actions.log,
    setMetrics: setters.setMetrics
  })) {
    return true
  }

  if (msg.type === "guard_alert_dispatch_result") {
    handleGuardAlertDispatchResult(msg, context)
    return true
  }

  if (utils.TOOL_RESULT_TYPES.has(msg.type)) {
    pushToolResult(msg, context)
    return true
  }

  if (msg.type === "error") {
    const errorText = `오류: ${msg.message || "-"}`
    const targetKey = actions.inferErrorKey(msg.message)
    const rawMessage = typeof msg.message === "string" ? msg.message : ""
    if (state.rootTab === "settings" && /(sessions_|cron|browser|canvas|nodes|telegram_stub|memory_|web_)/i.test(rawMessage)) {
      setters.setToolControlError(rawMessage)
    }
    if ((msg.message || "").toLowerCase().includes("unauthorized")) {
      handleUnauthorizedError(msg, context)
    }
    actions.finishPendingRequest(targetKey)
    actions.setError(targetKey, errorText)
    actions.log(errorText, "error")
    return true
  }

  return false
}
