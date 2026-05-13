function summarizeRoutineStatusMessage(text) {
  const lines = `${text || ""}`
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)

  if (lines.length === 0) {
    return ""
  }

  const first = lines[0]
  return first.length > 180 ? `${first.slice(0, 177)}...` : first
}

function shouldAutoShowCodingLogs(result) {
  const rawStatus = `${result && result.execution && typeof result.execution.status === "string" ? result.execution.status : ""}`.trim().toLowerCase()
  const stdout = `${result && result.execution && typeof result.execution.stdout === "string" ? result.execution.stdout : ""}`.trim()
  const stderr = `${result && result.execution && typeof result.execution.stderr === "string" ? result.execution.stderr : ""}`.trim()
  return /(error|fail|timeout|cancel|killed|aborted)/i.test(rawStatus) || !!stdout || !!stderr
}

function createCodingRuntimeState(msg) {
  const execution = msg && msg.execution && typeof msg.execution === "object" ? msg.execution : null
  return {
    pending: false,
    ok: !!msg.ok,
    runMode: msg.runMode || "",
    language: msg.language || "",
    message: msg.message || "",
    targetProvider: msg.targetProvider || "",
    targetModel: msg.targetModel || "",
    previewUrl: msg.previewUrl || "",
    previewEntry: msg.previewEntry || "",
    execution,
    updatedAt: Date.now()
  }
}

function normalizeWorkspaceSnapshotPath(value) {
  const normalized = `${value || ""}`.trim()
  if (!normalized || normalized === "-" || normalized === "(none)") {
    return ""
  }
  return normalized
}

function resolveCodingPreviewPath(result) {
  const execution = result && result.execution && typeof result.execution === "object" ? result.execution : null
  const changedFiles = Array.isArray(result && result.changedFiles) ? result.changedFiles.filter(Boolean) : []
  const runDirectory = normalizeWorkspaceSnapshotPath(execution && execution.runDirectory ? execution.runDirectory : "")
  const entryFile = normalizeWorkspaceSnapshotPath(execution && execution.entryFile ? execution.entryFile : "")

  if (entryFile) {
    if (entryFile.startsWith("/")) {
      return entryFile
    }

    if (runDirectory) {
      return `${runDirectory.replace(/\/+$/, "")}/${entryFile.replace(/^\/+/, "")}`
    }

    const matchedEntry = changedFiles.find((pathValue) => pathValue === entryFile || pathValue.endsWith(`/${entryFile}`))
    if (matchedEntry) {
      return matchedEntry
    }
  }

  return changedFiles[0] || ""
}

function requestCodingPreviewIfNeeded(conversationId, result, requestWorkspaceFilePreview, existingPreviewPath = "") {
  if (!conversationId || typeof requestWorkspaceFilePreview !== "function") {
    return
  }

  const nextPreviewPath = resolveCodingPreviewPath(result)
  if (!nextPreviewPath || nextPreviewPath === existingPreviewPath) {
    return
  }

  requestWorkspaceFilePreview(nextPreviewPath, conversationId)
}

function shouldAutoRequestCodingPreview(conversation, currentKey, currentConversationId) {
  if (!conversation || conversation.scope !== "coding") {
    return false
  }

  const conversationKey = `${conversation.scope || ""}:${conversation.mode || ""}`.trim()
  const normalizedCurrentKey = `${currentKey || ""}`.trim()
  const normalizedCurrentConversationId = `${currentConversationId || ""}`.trim()

  if (normalizedCurrentConversationId && normalizedCurrentConversationId === conversation.id) {
    return true
  }

  return !!conversationKey && conversationKey === normalizedCurrentKey
}

export function handleConversationMemoryMessage(msg, context) {
  const {
    autoCreateConversationRef,
    setSelectedConversationIdsByKey,
    setSelectedFoldersByKey,
    setConversationLists,
    setActiveConversationByKey,
    requestConversationDetail,
    requestAutoCreateConversation,
    setConversationDetails,
    setSelectedMemoryByConversation,
    setChatMultiResultByConversation,
    log,
    setMemoryNotes,
    setMemoryPreview,
    currentConversationId,
    send,
    setError,
    currentKey,
    setFilePreviewByConversation,
    filePreviewByConversation,
    setCodingResultByConversation,
    codingResultByConversation,
    setShowExecutionLogsByConversation,
    setCodingRuntimeByConversation,
    setCodingExecutionInputByConversation,
    requestWorkspaceFilePreview
  } = context

  if (msg.type === "conversations") {
    const list = Array.isArray(msg.items) ? msg.items : []
    const key = `${msg.scope || "chat"}:${msg.mode || "single"}`
    if (list.length > 0) {
      autoCreateConversationRef.current[key] = false
    }

    setSelectedConversationIdsByKey((prev) => {
      const base = Array.isArray(prev[key]) ? prev[key] : []
      if (base.length === 0) {
        return prev
      }

      const allowed = new Set(list.map((item) => item.id))
      const next = base.filter((id) => allowed.has(id))
      return next.length === base.length ? prev : { ...prev, [key]: next }
    })
    setSelectedFoldersByKey((prev) => {
      const base = Array.isArray(prev[key]) ? prev[key] : []
      if (base.length === 0) {
        return prev
      }

      const allowed = new Set(list.map((item) => ((item.project || "기본").trim() || "기본")))
      const next = base.filter((project) => allowed.has(project))
      return next.length === base.length ? prev : { ...prev, [key]: next }
    })
    setConversationLists((prev) => ({ ...prev, [key]: list }))
    setActiveConversationByKey((prev) => {
      const active = prev[key]
      if (active && list.some((x) => x.id === active)) {
        return prev
      }

      if (list.length > 0) {
        requestConversationDetail(list[0].id)
        return { ...prev, [key]: list[0].id }
      }

      requestAutoCreateConversation(msg.scope || "chat", msg.mode || "single")
      return prev
    })
    return true
  }

  if (msg.type === "conversation_created" || msg.type === "conversation_detail") {
    const conversation = msg.conversation
    if (!conversation || !conversation.id) {
      return true
    }

    const latestCodingResult = conversation.latestCodingResult && typeof conversation.latestCodingResult === "object"
      ? {
          ...conversation.latestCodingResult,
          type: "coding_result",
          conversationId: conversation.id,
          conversation
        }
      : null

    const key = `${conversation.scope}:${conversation.mode}`
    autoCreateConversationRef.current[key] = false
    setConversationDetails((prev) => ({ ...prev, [conversation.id]: conversation }))
    setConversationLists((prev) => {
      const list = Array.isArray(prev[key]) ? prev[key] : []
      if (list.length === 0) {
        return prev
      }

      const index = list.findIndex((item) => item.id === conversation.id)
      if (index < 0) {
        return prev
      }

      const current = list[index]
      const nextItem = {
        ...current,
        title: conversation.title || current.title || "제목 없음",
        preview: conversation.preview || current.preview || "",
        messageCount: Number.isFinite(conversation.messageCount) ? conversation.messageCount : current.messageCount,
        project: conversation.project || current.project || "기본",
        category: conversation.category || current.category || "일반",
        tags: Array.isArray(conversation.tags) ? conversation.tags : (current.tags || [])
      }
      const nextList = list.slice()
      nextList[index] = nextItem
      return { ...prev, [key]: nextList }
    })
    setActiveConversationByKey((prev) => ({ ...prev, [key]: conversation.id }))
    setSelectedMemoryByConversation((prev) => {
      const incoming = Array.isArray(conversation.linkedMemoryNotes) ? conversation.linkedMemoryNotes : []
      const current = Array.isArray(prev[conversation.id]) ? prev[conversation.id] : null
      if (current && current.length === incoming.length && current.every((value, index) => value === incoming[index])) {
        return prev
      }
      return { ...prev, [conversation.id]: incoming }
    })
    setCodingResultByConversation((prev) => {
      if (latestCodingResult) {
        return { ...prev, [conversation.id]: latestCodingResult }
      }

      if (!(conversation.id in prev)) {
        return prev
      }

      const next = { ...prev }
      delete next[conversation.id]
      return next
    })
    setShowExecutionLogsByConversation((prev) => {
      const latestCodingResult = conversation.latestCodingResult && typeof conversation.latestCodingResult === "object"
        ? conversation.latestCodingResult
        : null

      if (latestCodingResult) {
        return { ...prev, [conversation.id]: shouldAutoShowCodingLogs(latestCodingResult) }
      }

      if (!(conversation.id in prev)) {
        return prev
      }

      const next = { ...prev }
      delete next[conversation.id]
      return next
    })
    setCodingRuntimeByConversation((prev) => {
      if (!(conversation.id in prev)) {
        return prev
      }

      const next = { ...prev }
      delete next[conversation.id]
      return next
    })
    if (latestCodingResult && shouldAutoRequestCodingPreview(conversation, currentKey, currentConversationId)) {
      requestCodingPreviewIfNeeded(
        conversation.id,
        latestCodingResult,
        requestWorkspaceFilePreview,
        filePreviewByConversation && filePreviewByConversation[conversation.id] ? filePreviewByConversation[conversation.id].path || "" : ""
      )
    }
    return true
  }

  if (msg.type === "conversation_deleted") {
    const conversationId = msg.conversationId || ""
    if (!conversationId) {
      return true
    }

    setSelectedConversationIdsByKey((prev) => {
      const next = {}
      let changed = false
      Object.entries(prev).forEach(([key, ids]) => {
        if (!Array.isArray(ids)) {
          next[key] = ids
          return
        }

        const filtered = ids.filter((id) => id !== conversationId)
        next[key] = filtered
        if (filtered.length !== ids.length) {
          changed = true
        }
      })
      return changed ? next : prev
    })
    setConversationDetails((prev) => {
      const next = { ...prev }
      delete next[conversationId]
      return next
    })
    setChatMultiResultByConversation((prev) => {
      const next = { ...prev }
      delete next[conversationId]
      return next
    })
    setCodingResultByConversation((prev) => {
      if (!(conversationId in prev)) {
        return prev
      }

      const next = { ...prev }
      delete next[conversationId]
      return next
    })
    setShowExecutionLogsByConversation((prev) => {
      if (!(conversationId in prev)) {
        return prev
      }

      const next = { ...prev }
      delete next[conversationId]
      return next
    })
    setCodingRuntimeByConversation((prev) => {
      if (!(conversationId in prev)) {
        return prev
      }

      const next = { ...prev }
      delete next[conversationId]
      return next
    })
    setCodingExecutionInputByConversation((prev) => {
      if (!(conversationId in prev)) {
        return prev
      }

      const next = { ...prev }
      delete next[conversationId]
      return next
    })
    return true
  }

  if (msg.type === "memory_cleared") {
    const scopeText = String(msg.scope || "chat").toLowerCase()
    const refreshScope = scopeText === "telegram" ? "chat" : scopeText
    if (refreshScope === "all") {
      ["chat", "coding"].forEach((targetScope) => {
        ["single", "orchestration", "multi"].forEach((targetMode) => {
          const key = `${targetScope}:${targetMode}`
          autoCreateConversationRef.current[key] = false
        })
      })
    } else if (refreshScope === "chat" || refreshScope === "coding") {
      ["single", "orchestration", "multi"].forEach((targetMode) => {
        const key = `${refreshScope}:${targetMode}`
        autoCreateConversationRef.current[key] = false
      })
    }

    if (msg.message) {
      log(`[memory] ${msg.message}`, "info")
    }
    return true
  }

  if (msg.type === "memory_notes") {
    setMemoryNotes(Array.isArray(msg.items) ? msg.items : [])
    return true
  }

  if (msg.type === "memory_note_content") {
    setMemoryPreview({
      open: true,
      name: msg.name || "",
      content: msg.content || ""
    })
    return true
  }

  if (msg.type === "memory_note_created") {
    const ok = !!msg.ok
    const conversationId = msg.conversationId || currentConversationId || ""
    const noteName = msg.note && typeof msg.note.name === "string" ? msg.note.name : ""
    if (ok && conversationId && noteName) {
      setSelectedMemoryByConversation((prev) => {
        const base = Array.isArray(prev[conversationId]) ? prev[conversationId] : []
        if (base.includes(noteName)) {
          return prev
        }

        return { ...prev, [conversationId]: base.concat([noteName]) }
      })
    }

    if (msg.message) {
      log(`[memory] ${msg.message}`, ok ? "info" : "error")
    }
    send({ type: "list_memory_notes" }, { silent: true, queueIfClosed: false })
    return true
  }

  if (msg.type === "memory_note_deleted") {
    const ok = !!msg.ok
    const removedNames = Array.isArray(msg.removedNames)
      ? msg.removedNames.filter((name) => typeof name === "string" && name.trim()).map((name) => name.trim())
      : []

    if (removedNames.length > 0) {
      setSelectedMemoryByConversation((prev) => {
        const next = {}
        Object.entries(prev).forEach(([conversationId, names]) => {
          next[conversationId] = Array.isArray(names)
            ? names.filter((name) => !removedNames.includes(name))
            : names
        })
        return next
      })
      setConversationDetails((prev) => {
        const next = {}
        Object.entries(prev).forEach(([conversationId, detail]) => {
          next[conversationId] = detail && Array.isArray(detail.linkedMemoryNotes)
            ? { ...detail, linkedMemoryNotes: detail.linkedMemoryNotes.filter((name) => !removedNames.includes(name)) }
            : detail
        })
        return next
      })
      setMemoryPreview((prev) => (
        removedNames.includes(prev.name)
          ? { open: false, name: "", content: "" }
          : prev
      ))
    }

    if (msg.message) {
      log(`[memory] ${msg.message}`, ok ? "info" : "error")
    }
    send({ type: "list_memory_notes" }, { silent: true, queueIfClosed: false })
    return true
  }

  if (msg.type === "memory_note_renamed") {
    const ok = !!msg.ok
    const oldName = typeof msg.oldName === "string" ? msg.oldName.trim() : ""
    const newName = typeof msg.newName === "string" ? msg.newName.trim() : ""

    if (ok && oldName && newName) {
      setSelectedMemoryByConversation((prev) => {
        const next = {}
        Object.entries(prev).forEach(([conversationId, names]) => {
          next[conversationId] = Array.isArray(names)
            ? names.map((name) => name === oldName ? newName : name)
            : names
        })
        return next
      })
      setConversationDetails((prev) => {
        const next = {}
        Object.entries(prev).forEach(([conversationId, detail]) => {
          next[conversationId] = detail && Array.isArray(detail.linkedMemoryNotes)
            ? {
                ...detail,
                linkedMemoryNotes: detail.linkedMemoryNotes.map((name) => name === oldName ? newName : name)
              }
            : detail
        })
        return next
      })
      setMemoryPreview((prev) => (
        prev.name === oldName
          ? { ...prev, name: newName }
          : prev
      ))
    }

    if (msg.message) {
      log(`[memory] ${msg.message}`, ok ? "info" : "error")
    }
    send({ type: "list_memory_notes" }, { silent: true, queueIfClosed: false })
    return true
  }

  if (msg.type === "workspace_file_preview") {
    const conversationId = msg.conversationId || currentConversationId || ""
    if (!conversationId) {
      return true
    }

    if (!msg.ok) {
      setFilePreviewByConversation((prev) => ({
        ...prev,
        [conversationId]: {
          path: msg.path || "",
          content: msg.message || "파일 프리뷰를 불러오지 못했습니다."
        }
      }))
      return true
    }

    setFilePreviewByConversation((prev) => ({
      ...prev,
      [conversationId]: {
        path: msg.path || "",
        content: msg.content || ""
      }
    }))
    return true
  }

  return false
}

export function handleRoutineMessage(msg, context) {
  const {
    setRoutines,
    setRoutineSelectedId,
    setRoutineProgress,
    setRoutinePreview,
    setRoutineSchedulerStatus,
    isPortraitMobileLayout,
    setResponsivePane,
    log,
    setError,
    routineBrowserAgentPreviewRef,
    send,
    setRoutineOutputPreview
  } = context

  if (msg.type === "routines_state") {
    const items = Array.isArray(msg.items) ? msg.items : []
    setRoutines(items)
    setRoutineSelectedId((prev) => {
      if (prev && items.some((x) => x.id === prev)) {
        return prev
      }
      return items.length > 0 ? (items[0].id || "") : ""
    })
    if (isPortraitMobileLayout && items.length === 0) {
      setResponsivePane("routine", "overview")
    }
    return true
  }

  if (msg.type === "routine_preview") {
    if (typeof setRoutinePreview === "function") {
      setRoutinePreview({
        request: msg.request || "",
        scheduleSourceMode: msg.scheduleSourceMode || "",
        scheduleText: msg.scheduleText || "",
        scheduleKind: msg.scheduleKind || "",
        timezoneId: msg.timezoneId || "",
        resolvedExecutionMode: msg.resolvedExecutionMode || "",
        executionRoute: msg.executionRoute || "",
        warnings: Array.isArray(msg.warnings) ? msg.warnings : []
      })
    }
    return true
  }

  if (msg.type === "routine_scheduler_status") {
    if (typeof setRoutineSchedulerStatus === "function") {
      setRoutineSchedulerStatus({
        enabled: msg.enabled !== false,
        totalRoutines: Number(msg.totalRoutines || 0),
        enabledRoutines: Number(msg.enabledRoutines || 0),
        runningRoutines: Number(msg.runningRoutines || 0),
        dueRoutines: Number(msg.dueRoutines || 0),
        nextRunAtMs: Number.isFinite(Number(msg.nextRunAtMs)) ? Number(msg.nextRunAtMs) : null,
        lastError: msg.lastError || ""
      })
    }
    return true
  }

  if (msg.type === "routine_progress") {
    setRoutineProgress((prev) => {
      const now = Date.now()
      const base = prev || {}
      const startedAt = Number.isFinite(base.startedAt) && base.startedAt > 0 ? base.startedAt : now
      return {
        ...base,
        active: !msg.done,
        operation: msg.operation || base.operation || "create",
        percent: Number.isFinite(msg.percent) ? msg.percent : (base.percent || 0),
        message: summarizeRoutineStatusMessage(msg.message) || base.message || "",
        stageKey: msg.stageKey || base.stageKey || "",
        stageTitle: msg.stageTitle || base.stageTitle || "",
        stageDetail: msg.stageDetail || base.stageDetail || "",
        stageIndex: Number.isFinite(msg.stageIndex) ? msg.stageIndex : (base.stageIndex || 0),
        stageTotal: Number.isFinite(msg.stageTotal) ? msg.stageTotal : (base.stageTotal || 5),
        done: !!msg.done,
        ok: msg.done ? msg.ok !== false : null,
        startedAt,
        updatedAt: now,
        completedAt: msg.done ? now : 0
      }
    })
    return true
  }

  if (msg.type === "routine_result") {
    const ok = !!msg.ok
    const messageText = msg.message || (ok ? "루틴 처리 완료" : "루틴 처리 실패")
    const messageSummary = summarizeRoutineStatusMessage(messageText) || messageText
    log(messageText, ok ? "info" : "error")
    setError("routine:main", ok ? "" : `오류: ${messageText}`)
    setRoutineProgress((prev) => {
      const base = prev || {}
      if ((base.operation || "") !== "create" || !base.active) {
        return base
      }

      const now = Date.now()
      const nextStageTitle = ok ? (base.stageTitle || "초기 실행") : (base.stageTitle || "루틴 생성")
      const nextStageDetail = ok
        ? (base.stageDetail || "생성 직후 실행 결과를 반영했습니다.")
        : (messageSummary || base.stageDetail || "루틴 생성 중 오류가 발생했습니다.")
      return {
        ...base,
        active: false,
        operation: base.operation || "create",
        percent: ok ? 100 : Math.max(8, Math.min(96, Number(base.percent) || 0)),
        message: messageSummary,
        stageTitle: nextStageTitle,
        stageDetail: nextStageDetail,
        stageIndex: Math.max(Number(base.stageIndex) || 0, ok ? Number(base.stageTotal) || 5 : Number(base.stageIndex) || 1),
        stageTotal: Number(base.stageTotal) || 5,
        done: true,
        ok,
        startedAt: Number.isFinite(base.startedAt) && base.startedAt > 0 ? base.startedAt : now,
        updatedAt: now,
        completedAt: now
      }
    })
    if (msg.routine && msg.routine.id) {
      setRoutineSelectedId(msg.routine.id)
      if (isPortraitMobileLayout) {
        setResponsivePane("routine", "detail")
      }
    }
    if (routineBrowserAgentPreviewRef.current
      && msg.routine
      && msg.routine.id === routineBrowserAgentPreviewRef.current) {
      const newestRun = Array.isArray(msg.routine.runs) && msg.routine.runs.length > 0
        ? msg.routine.runs[0]
        : null
      routineBrowserAgentPreviewRef.current = ""
      if (newestRun && newestRun.ts) {
        send({ type: "get_routine_run_detail", routineId: msg.routine.id, ts: newestRun.ts })
      } else {
        setRoutineOutputPreview({
          open: true,
          title: `${msg.routine.title || msg.routine.id} · 브라우저 에이전트 테스트`,
          content: msg.message || "출력 없음",
          imagePath: "",
          imageAlt: ""
        })
      }
    }
    send({ type: "get_routines" })
    return true
  }

  if (msg.type === "routine_run_detail") {
    const ok = !!msg.ok
    if (!ok) {
      const errorText = msg.error || "실행 이력을 불러오지 못했습니다."
      setError("routine:main", `오류: ${errorText}`)
      log(errorText, "error")
      return true
    }

    const titleParts = [
      msg.title || "루틴 실행 상세",
      msg.runAtLocal || "",
      msg.status || ""
    ].filter(Boolean)
    const meta = [
      msg.source ? `source=${msg.source}` : "",
      Number.isFinite(Number(msg.attemptCount)) ? `attempts=${Number(msg.attemptCount)}` : "",
      msg.telegramStatus ? `telegram=${msg.telegramStatus}` : "",
      msg.artifactPath ? `artifact=${msg.artifactPath}` : "",
      msg.agentSessionId ? `agentSessionId=${msg.agentSessionId}` : "",
      msg.agentRunId ? `agentRunId=${msg.agentRunId}` : "",
      msg.agentProvider || msg.agentModel ? `agent=${msg.agentProvider || "-"}:${msg.agentModel || "-"}` : "",
      msg.toolProfile ? `toolProfile=${msg.toolProfile}` : "",
      msg.startUrl ? `startUrl=${msg.startUrl}` : "",
      msg.finalUrl ? `finalUrl=${msg.finalUrl}` : "",
      msg.pageTitle ? `pageTitle=${msg.pageTitle}` : "",
      msg.screenshotPath ? `screenshot=${msg.screenshotPath}` : "",
      Array.isArray(msg.downloadPaths) && msg.downloadPaths.length > 0
        ? `downloadPaths=${msg.downloadPaths.join(" | ")}`
        : ""
    ].filter(Boolean).join("\n")
    setRoutineOutputPreview({
      open: true,
      title: titleParts.join(" · "),
      content: meta ? `${meta}\n\n${msg.content || ""}` : (msg.content || ""),
      imagePath: msg.screenshotPath || "",
      imageAlt: msg.pageTitle || msg.title || "루틴 스크린샷"
    })
    return true
  }

  return false
}

export function handleExecutionFlowMessage(msg, context) {
  const {
    setCodingProgressByKey,
    setPendingByKey,
    setActiveConversationByKey,
    setOptimisticUserByKey,
    normalizeChatMultiResultMessage,
    setChatMultiResultByConversation,
    attachLatencyMetaToConversation,
    setConversationDetails,
    setSelectedMemoryByConversation,
    finishPendingRequest,
    setError,
    setCodingResultByConversation,
    codingResultByConversation,
    setShowExecutionLogsByConversation,
    setCodingRuntimeByConversation,
    setFilePreviewByConversation,
    filePreviewByConversation,
    requestWorkspaceFilePreview,
    send,
    log,
    setMetrics
  } = context

  if (msg.type === "coding_progress") {
    const key = `${msg.scope || "coding"}:${msg.mode || "single"}`
    setCodingProgressByKey((prev) => {
      const now = Date.now()
      const base = prev[key] || {}
      const conversationId = `${msg.conversationId || base.conversationId || ""}`.trim()
      return {
        ...prev,
        [key]: {
          phase: msg.phase || base.phase || "",
          message: msg.message || base.message || "",
          stageKey: msg.stageKey || base.stageKey || "",
          stageTitle: msg.stageTitle || base.stageTitle || "",
          stageDetail: msg.stageDetail || base.stageDetail || "",
          stageIndex: Number.isFinite(msg.stageIndex) ? msg.stageIndex : (base.stageIndex || 0),
          stageTotal: Number.isFinite(msg.stageTotal) ? msg.stageTotal : (base.stageTotal || 0),
          iteration: Number.isFinite(msg.iteration) ? msg.iteration : (base.iteration || 0),
          maxIterations: Number.isFinite(msg.maxIterations) ? msg.maxIterations : (base.maxIterations || 0),
          percent: Number.isFinite(msg.percent) ? msg.percent : (base.percent || 0),
          done: !!msg.done,
          provider: msg.provider || base.provider || "",
          model: msg.model || base.model || "",
          conversationId,
          startedAt: base.startedAt || now,
          updatedAt: now
        }
      }
    })
    return true
  }

  if (msg.type === "llm_chat_stream_chunk") {
    const key = `${msg.scope || "chat"}:${msg.mode || "single"}`
    const conversationId = `${msg.conversationId || ""}`.trim()
    setPendingByKey((prev) => {
      const now = Date.now()
      const base = prev[key] || {}
      const incomingRequestId = `${msg.requestId || ""}`.trim()
      const baseRequestId = `${base.requestId || ""}`.trim()
      if (incomingRequestId && baseRequestId && incomingRequestId !== baseRequestId) {
        return prev
      }
      const baseDraft = typeof base.draftText === "string" ? base.draftText : ""
      const delta = typeof msg.delta === "string" ? msg.delta : ""
      return {
        ...prev,
        [key]: {
          ...base,
          active: true,
          conversationId,
          startedUtc: base.startedUtc || new Date(now).toISOString(),
          updatedAt: now,
          draftText: `${baseDraft}${delta}`,
          provider: msg.provider || base.provider || "",
          model: msg.model || base.model || "",
          route: msg.route || base.route || "",
          chunkIndex: Number.isFinite(msg.chunkIndex) ? msg.chunkIndex : (base.chunkIndex || 0),
          requestId: incomingRequestId || baseRequestId
        }
      }
    })
    setActiveConversationByKey((prev) => {
      if (!conversationId) {
        return prev
      }

      const current = `${prev[key] || ""}`.trim()
      if (current) {
        return prev
      }

      return { ...prev, [key]: conversationId }
    })
    setOptimisticUserByKey((prev) => {
      const base = prev[key]
      if (!base || !conversationId) {
        return prev
      }

      const currentConversationId = `${base.conversationId || ""}`.trim()
      if (currentConversationId) {
        return prev
      }

      return {
        ...prev,
        [key]: {
          ...base,
          conversationId
        }
      }
    })
    return true
  }

  if (msg.type === "llm_chat_result" || msg.type === "llm_chat_multi_result" || msg.type === "coding_result") {
    const conv = msg.conversation
    if (!conv || !conv.id) {
      return true
    }

    if (msg.type === "llm_chat_multi_result") {
      const normalizedResult = normalizeChatMultiResultMessage(msg)
      setChatMultiResultByConversation((prev) => ({
        ...prev,
        [conv.id]: {
          ...normalizedResult,
          updatedUtc: new Date().toISOString()
        }
      }))
    }

    const key = `${conv.scope}:${conv.mode}`
    const normalizedConversation = msg.type === "llm_chat_result"
      ? attachLatencyMetaToConversation(conv, msg)
      : conv
    setConversationDetails((prev) => ({ ...prev, [conv.id]: normalizedConversation }))
    setActiveConversationByKey((prev) => {
      if (prev[key]) {
        return prev
      }
      return { ...prev, [key]: conv.id }
    })
    setSelectedMemoryByConversation((prev) => ({
      ...prev,
      [conv.id]: normalizedConversation.linkedMemoryNotes || prev[conv.id] || []
    }))
    finishPendingRequest(key)
    setError(key, "")

    if (msg.type === "coding_result") {
      setCodingResultByConversation((prev) => ({ ...prev, [conv.id]: msg }))
      setShowExecutionLogsByConversation((prev) => ({ ...prev, [conv.id]: shouldAutoShowCodingLogs(msg) }))
      setCodingRuntimeByConversation((prev) => {
        if (!(conv.id in prev)) {
          return prev
        }

        const next = { ...prev }
        delete next[conv.id]
        return next
      })
      setFilePreviewByConversation((prev) => {
        const next = { ...prev }
        delete next[conv.id]
        return next
      })
      requestCodingPreviewIfNeeded(conv.id, msg, requestWorkspaceFilePreview)
    }

    if (msg.autoMemoryNote) {
      send({ type: "list_memory_notes" })
      log(`자동 컨텍스트 압축 노트 생성: ${msg.autoMemoryNote.name}`)
    }
    return true
  }

  if (msg.type === "coding_execute_result") {
    const conversationId = msg.conversationId || ""
    if (!conversationId) {
      return true
    }

    const runtimeState = createCodingRuntimeState(msg)
    setCodingRuntimeByConversation((prev) => ({
      ...prev,
      [conversationId]: runtimeState
    }))

    if (!msg.ok) {
      log(`[coding] ${runtimeState.message || "최근 코딩 결과 실행 실패"}`, "error")
      return true
    }

    if (runtimeState.runMode === "command" && runtimeState.execution) {
      setShowExecutionLogsByConversation((prev) => ({
        ...prev,
        [conversationId]: true
      }))
      requestCodingPreviewIfNeeded(
        conversationId,
        codingResultByConversation && codingResultByConversation[conversationId]
          ? codingResultByConversation[conversationId]
          : { execution: runtimeState.execution, changedFiles: [] },
        requestWorkspaceFilePreview,
        filePreviewByConversation && filePreviewByConversation[conversationId] ? filePreviewByConversation[conversationId].path || "" : ""
      )
    }
    return true
  }

  if (msg.type === "metrics" || msg.type === "metrics_stream") {
    const text = typeof msg.payload === "string" ? msg.payload : JSON.stringify(msg.payload, null, 2)
    setMetrics(text)
    return true
  }

  if (msg.type === "command_result") {
    log(`결과: ${msg.text || ""}`)
    return true
  }

  return false
}
