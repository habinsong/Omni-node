import {
  LOGIC_NODE_LIBRARY,
  LOGIC_NODE_DEFAULT_SIZE,
  createLogicNode,
  getLogicNodeInspectorDefinition,
  normalizeLogicNodeSize,
  summarizeLogicGraph
} from "./logic-state.js"

const LOGIC_NODE_WIDTH = LOGIC_NODE_DEFAULT_SIZE.width
const LOGIC_NODE_RESIZE_HANDLES = [
  "nw",
  "n",
  "ne",
  "e",
  "se",
  "s",
  "sw",
  "w"
]
const LOGIC_FIELD_MODE_PREFIX = "__mode__"
const LOGIC_FIELD_LITERAL_PREFIX = "__literal__"
const LOGIC_FIELD_REFERENCE_PREFIX = "__reference__"
const LOGIC_INLINE_REFERENCE_REGEX = /\{\{\s*([^{}]+?)\s*\}\}/g
const LOGIC_BINDABLE_FIELD_KEYS = new Set([
  "result",
  "leftRef",
  "value",
  "template",
  "input",
  "query",
  "content",
  "message",
  "text",
  "task"
])
const LOGIC_REFERENCE_OUTPUTS_BY_TYPE = {
  start: [
    { value: "data.input", label: "원본 입력" }
  ],
  end: [
    { value: "data.result", label: "마무리 결과" }
  ],
  output: [
    { value: "data.result", label: "출력 결과" }
  ],
  if: [
    { value: "data.branch", label: "선택된 갈래" }
  ],
  set_var: [
    { value: "data.value", label: "저장한 값" }
  ],
  template: [
    { value: "data.rendered", label: "완성 문장" }
  ],
  memory_get: [
    { value: "data.path", label: "읽은 문서 경로" }
  ],
  file_read: [
    { value: "data.path", label: "읽은 파일 경로" }
  ],
  file_write: [
    { value: "data.path", label: "저장한 파일 경로" }
  ],
  web_fetch: [
    { value: "data.url", label: "읽은 주소" }
  ]
}
const LOGIC_SESSION_REFERENCE_TYPES = new Set([
  "chat_single",
  "chat_orchestration",
  "chat_multi",
  "coding_single",
  "coding_orchestration",
  "coding_multi",
  "session_spawn",
  "session_send"
])

const LOGIC_CATEGORY_META = {
  flow: {
    label: "흐름",
    className: "flow"
  },
  ai: {
    label: "문답/코딩",
    className: "ai"
  },
  automation: {
    label: "자동화",
    className: "automation"
  },
  data: {
    label: "데이터/도구",
    className: "data"
  },
  ops: {
    label: "운영",
    className: "ops"
  }
}

const LOGIC_CATEGORY_THEME_META = {
  flow: [
    {
      surface: "rgba(240, 246, 255, 0.98)",
      border: "rgba(181, 206, 239, 0.96)",
      tint: "rgba(227, 240, 255, 0.98)",
      badgeBg: "rgba(214, 232, 255, 0.95)",
      badgeFg: "#2f5b9c"
    },
    {
      surface: "rgba(235, 245, 255, 0.98)",
      border: "rgba(169, 198, 236, 0.96)",
      tint: "rgba(221, 237, 255, 0.98)",
      badgeBg: "rgba(204, 227, 255, 0.95)",
      badgeFg: "#315693"
    },
    {
      surface: "rgba(243, 248, 255, 0.98)",
      border: "rgba(188, 211, 241, 0.96)",
      tint: "rgba(231, 243, 255, 0.98)",
      badgeBg: "rgba(221, 236, 255, 0.95)",
      badgeFg: "#345b95"
    }
  ],
  ai: [
    {
      surface: "rgba(255, 242, 238, 0.98)",
      border: "rgba(240, 198, 186, 0.96)",
      tint: "rgba(255, 232, 225, 0.98)",
      badgeBg: "rgba(255, 223, 214, 0.95)",
      badgeFg: "#b05e45"
    },
    {
      surface: "rgba(255, 244, 241, 0.98)",
      border: "rgba(238, 190, 177, 0.96)",
      tint: "rgba(255, 236, 230, 0.98)",
      badgeBg: "rgba(255, 229, 220, 0.95)",
      badgeFg: "#b46549"
    },
    {
      surface: "rgba(255, 239, 236, 0.98)",
      border: "rgba(236, 184, 171, 0.96)",
      tint: "rgba(255, 229, 221, 0.98)",
      badgeBg: "rgba(255, 220, 211, 0.95)",
      badgeFg: "#b25c42"
    }
  ],
  automation: [
    {
      surface: "rgba(239, 251, 245, 0.98)",
      border: "rgba(181, 224, 202, 0.96)",
      tint: "rgba(228, 246, 238, 0.98)",
      badgeBg: "rgba(216, 242, 228, 0.95)",
      badgeFg: "#2f7d58"
    },
    {
      surface: "rgba(241, 252, 247, 0.98)",
      border: "rgba(174, 220, 198, 0.96)",
      tint: "rgba(232, 247, 240, 0.98)",
      badgeBg: "rgba(222, 244, 231, 0.95)",
      badgeFg: "#32795a"
    },
    {
      surface: "rgba(236, 249, 243, 0.98)",
      border: "rgba(168, 214, 193, 0.96)",
      tint: "rgba(225, 244, 235, 0.98)",
      badgeBg: "rgba(212, 239, 223, 0.95)",
      badgeFg: "#2d7354"
    }
  ],
  data: [
    {
      surface: "rgba(238, 252, 250, 0.98)",
      border: "rgba(178, 222, 217, 0.96)",
      tint: "rgba(227, 246, 244, 0.98)",
      badgeBg: "rgba(214, 243, 239, 0.95)",
      badgeFg: "#28706e"
    },
    {
      surface: "rgba(241, 252, 251, 0.98)",
      border: "rgba(171, 216, 212, 0.96)",
      tint: "rgba(231, 246, 245, 0.98)",
      badgeBg: "rgba(220, 245, 242, 0.95)",
      badgeFg: "#276f6b"
    },
    {
      surface: "rgba(235, 250, 248, 0.98)",
      border: "rgba(164, 211, 208, 0.96)",
      tint: "rgba(225, 244, 242, 0.98)",
      badgeBg: "rgba(210, 240, 236, 0.95)",
      badgeFg: "#256a67"
    }
  ],
  ops: [
    {
      surface: "rgba(245, 242, 255, 0.98)",
      border: "rgba(203, 193, 235, 0.96)",
      tint: "rgba(236, 230, 255, 0.98)",
      badgeBg: "rgba(227, 221, 255, 0.95)",
      badgeFg: "#6251a4"
    },
    {
      surface: "rgba(247, 244, 255, 0.98)",
      border: "rgba(196, 186, 231, 0.96)",
      tint: "rgba(239, 233, 255, 0.98)",
      badgeBg: "rgba(233, 227, 255, 0.95)",
      badgeFg: "#6450a2"
    },
    {
      surface: "rgba(243, 240, 255, 0.98)",
      border: "rgba(190, 180, 227, 0.96)",
      tint: "rgba(234, 227, 255, 0.98)",
      badgeBg: "rgba(224, 217, 255, 0.95)",
      badgeFg: "#604d9c"
    }
  ]
}

const LOGIC_STATUS_LABELS = {
  idle: "대기",
  pending: "대기",
  running: "실행 중",
  completed: "완료",
  error: "실패",
  failed: "실패",
  canceled: "취소됨",
  disabled: "비활성",
  saved: "저장됨",
  unsaved: "변경 있음"
}

const LOGIC_EVENT_LABELS = {
  run_started: "실행 시작",
  run_completed: "실행 완료",
  run_failed: "실행 실패",
  run_canceled: "실행 취소",
  run_snapshot: "최근 상태",
  node_started: "노드 시작",
  node_completed: "노드 완료",
  node_failed: "노드 실패"
}

const LOGIC_NODE_CATEGORY_MAP = Object.fromEntries(
  LOGIC_NODE_LIBRARY.flatMap((group) => group.items.map(([type]) => [type, group.key]))
)

function stopLogicCanvasEvent(event) {
  event?.stopPropagation?.()
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value))
}

function truncateText(value, maxLength = 44) {
  const normalized = `${value || ""}`.replace(/\s+/g, " ").trim()
  if (!normalized) {
    return ""
  }
  if (normalized.length <= maxLength) {
    return normalized
  }
  return `${normalized.slice(0, Math.max(0, maxLength - 1))}…`
}

const LOGIC_NODE_PORT_OFFSET_Y = 18

function getLogicPortPosition(node, role, portKey = "main", graph = null, resolveNodeSize = null) {
  return getLogicPortAnchor(node, role, portKey, graph, resolveNodeSize)
}

function buildLogicCurvePath(start, end) {
  const midX = Math.round((start.x + end.x) / 2)
  return `M ${start.x} ${start.y} C ${midX} ${start.y}, ${midX} ${end.y}, ${end.x} ${end.y}`
}

function renderSectionTabs(e, renderResponsiveSectionTabs, currentPane, setResponsivePane) {
  const activePane = normalizeLogicPaneKey(currentPane, true)
  return renderResponsiveSectionTabs(
    [
      { key: "canvas", label: "캔버스" },
      { key: "flow", label: "흐름" },
      { key: "nodes", label: "노드" },
      { key: "selection", label: "선택" },
      { key: "run", label: "실행" },
      { key: "json", label: "JSON" }
    ],
    activePane,
    (pane) => setResponsivePane("logic", pane)
  )
}

function normalizeLogicPaneKey(paneKey, allowCanvas = false) {
  const normalized = `${paneKey || ""}`.trim().toLowerCase()
  if (allowCanvas && normalized === "canvas") {
    return "canvas"
  }
  if (["flow", "nodes", "selection", "run", "json"].includes(normalized)) {
    return normalized
  }
  if (normalized === "list") {
    return "flow"
  }
  if (normalized === "palette") {
    return "nodes"
  }
  if (normalized === "inspector") {
    return "selection"
  }
  return allowCanvas ? "canvas" : "selection"
}

function getStageMetrics(nodes, resolveNodeSize = null) {
  let maxX = 1600
  let maxY = 1080
  ;(Array.isArray(nodes) ? nodes : []).forEach((node) => {
    const x = Number(node?.position?.x) || 0
    const y = Number(node?.position?.y) || 0
    const size = typeof resolveNodeSize === "function"
      ? resolveNodeSize(node)
      : normalizeLogicNodeSize(node?.size, node?.type)
    maxX = Math.max(maxX, x + size.width + 220)
    maxY = Math.max(maxY, y + size.height + 180)
  })
  return {
    width: maxX,
    height: maxY
  }
}

function ensureOptionValue(options, value, fallbackLabel = "") {
  const safeOptions = Array.isArray(options) ? options.slice() : []
  const safeValue = `${value || ""}`
  if (!safeValue) {
    return safeOptions
  }
  if (safeOptions.some((item) => `${item?.value || ""}` === safeValue)) {
    return safeOptions
  }
  return [
    { value: safeValue, label: fallbackLabel || safeValue },
    ...safeOptions
  ]
}

function renderOptionList(e, options, emptyLabel = "선택값 없음") {
  const safeOptions = Array.isArray(options) && options.length > 0
    ? options
    : [{ value: "", label: emptyLabel }]
  return safeOptions.map((item, index) => e("option", {
    key: `${item?.value || "empty"}-${index}`,
    value: item?.value || ""
  }, item?.label || item?.value || emptyLabel))
}

function readCatalogOptions(catalogs, key) {
  if (!catalogs || !key) {
    return []
  }
  const value = catalogs[key]
  return Array.isArray(value) ? value : []
}

function resolveProviderModelOptions(node, field, catalogs) {
  const providerKey = field.providerKey || "provider"
  const provider = `${node?.config?.[providerKey] || ""}`.trim().toLowerCase()
  switch (provider) {
    case "groq":
      return readCatalogOptions(catalogs, "groqModelOptions")
    case "gemini":
      return readCatalogOptions(catalogs, "geminiModelOptions")
    case "cerebras":
      return readCatalogOptions(catalogs, "cerebrasModelOptions")
    case "nvidia":
      return readCatalogOptions(catalogs, "nvidiaModelOptions")
    case "copilot":
      return readCatalogOptions(catalogs, "copilotModelOptions")
    case "codex":
      return readCatalogOptions(catalogs, "codexModelOptions")
    default:
      return []
  }
}

function getLogicNodeCategory(type) {
  return LOGIC_NODE_CATEGORY_MAP[`${type || ""}`.trim()] || "flow"
}

function getLogicNodeToneIndex(type) {
  const category = getLogicNodeCategory(type)
  const group = getPaletteLibraryGroup(category)
  const foundIndex = (group?.items || []).findIndex(([itemType]) => itemType === type)
  if (foundIndex < 0) {
    return 0
  }
  return foundIndex % 3
}

function getLogicCategoryMeta(typeOrCategory) {
  const normalized = `${typeOrCategory || ""}`.trim()
  if (LOGIC_CATEGORY_META[normalized]) {
    return LOGIC_CATEGORY_META[normalized]
  }
  return LOGIC_CATEGORY_META[getLogicNodeCategory(normalized)] || LOGIC_CATEGORY_META.flow
}

function getPaletteLibraryGroup(groupKey) {
  return LOGIC_NODE_LIBRARY.find((group) => group.key === groupKey) || LOGIC_NODE_LIBRARY[0]
}

function getPaletteLibraryItem(type) {
  for (const group of LOGIC_NODE_LIBRARY) {
    const found = group.items.find(([itemType]) => itemType === type)
    if (found) {
      return {
        group,
        type: found[0],
        label: found[1]
      }
    }
  }
  return {
    group: LOGIC_NODE_LIBRARY[0],
    type,
    label: type
  }
}

function getLogicStatusLabel(status) {
  const normalized = `${status || ""}`.trim().toLowerCase()
  return LOGIC_STATUS_LABELS[normalized] || (normalized || "-")
}

function getLogicStatusTone(status) {
  const normalized = `${status || ""}`.trim().toLowerCase()
  if (normalized === "completed" || normalized === "saved") {
    return "ok"
  }
  if (normalized === "running" || normalized === "unsaved") {
    return "warn"
  }
  if (normalized === "error" || normalized === "failed") {
    return "error"
  }
  return "idle"
}

function getLogicEventLabel(kind) {
  const normalized = `${kind || ""}`.trim().toLowerCase()
  return LOGIC_EVENT_LABELS[normalized] || (normalized || "이벤트")
}

function buildLogicThemeVars(type) {
  const category = getLogicNodeCategory(type)
  const toneIndex = getLogicNodeToneIndex(type)
  const themeSet = LOGIC_CATEGORY_THEME_META[category] || LOGIC_CATEGORY_THEME_META.flow
  const tone = themeSet[toneIndex] || themeSet[0]
  return {
    "--logic-node-surface": tone.surface,
    "--logic-node-border": tone.border,
    "--logic-node-tint": tone.tint,
    "--logic-node-badge-bg": tone.badgeBg,
    "--logic-node-badge-fg": tone.badgeFg
  }
}

function getLogicFieldModeKey(fieldKey) {
  return `${LOGIC_FIELD_MODE_PREFIX}${fieldKey}`
}

function getLogicFieldLiteralKey(fieldKey) {
  return `${LOGIC_FIELD_LITERAL_PREFIX}${fieldKey}`
}

function getLogicFieldReferenceKey(fieldKey) {
  return `${LOGIC_FIELD_REFERENCE_PREFIX}${fieldKey}`
}

function isLogicBindableField(field) {
  return !!field?.key && LOGIC_BINDABLE_FIELD_KEYS.has(field.key)
}

function normalizeLogicReferenceExpression(value) {
  const raw = `${value || ""}`.trim()
  if (!raw) {
    return ""
  }
  if (raw.startsWith("{{") && raw.endsWith("}}")) {
    return raw.slice(2, -2).trim()
  }
  return raw
}

function isLogicReferenceValue(value) {
  const normalized = normalizeLogicReferenceExpression(value)
  if (!normalized) {
    return false
  }
  return normalized === "run.input"
    || normalized.startsWith("vars.")
    || normalized.startsWith("nodes.")
    || normalized.startsWith("sessions.")
    || normalized.startsWith("artifacts.")
}

function getLogicFieldIncomingEdges(graph, nodeId, fieldKey) {
  return (graph?.edges || []).filter((edge) =>
    edge?.targetNodeId === nodeId
    && `${edge?.targetPort || "main"}`.trim().toLowerCase() === `${fieldKey || "main"}`.trim().toLowerCase()
  )
}

function getLogicFieldMode(graph, node, field) {
  if (!node || !field?.key || !isLogicBindableField(field)) {
    return "literal"
  }
  const config = node.config || {}
  const storedMode = `${config[getLogicFieldModeKey(field.key)] || ""}`.trim().toLowerCase()
  if (storedMode === "literal" || storedMode === "reference" || storedMode === "edge") {
    return storedMode
  }
  if (getLogicFieldIncomingEdges(graph, node.nodeId, field.key).length > 0) {
    return "edge"
  }
  return isLogicReferenceValue(config[field.key]) ? "reference" : "literal"
}

function getLogicFieldLiteralValue(node, field) {
  const config = node?.config || {}
  const stored = config[getLogicFieldLiteralKey(field.key)]
  if (typeof stored === "string") {
    return stored
  }
  const current = config[field.key]
  return isLogicReferenceValue(current) ? "" : (current || "")
}

function getLogicFieldReferenceValue(node, field) {
  const config = node?.config || {}
  const stored = config[getLogicFieldReferenceKey(field.key)]
  if (typeof stored === "string" && stored.trim()) {
    return stored
  }
  return isLogicReferenceValue(config[field.key]) ? config[field.key] : ""
}

function getLogicNodeInputPorts(node, graph) {
  const ports = []
  if (node?.type !== "start") {
    ports.push({
      key: "main",
      label: node?.type === "parallel_join" ? "합류 입력" : "입력",
      role: "flow"
    })
  }
  const definition = getLogicNodeInspectorDefinition(node?.type)
  ;(definition?.fields || []).forEach((field) => {
    if (!isLogicBindableField(field)) {
      return
    }
    if (getLogicFieldMode(graph, node, field) !== "edge") {
      return
    }
    ports.push({
      key: field.key,
      label: field.label,
      role: "field"
    })
  })
  return ports
}

function getLogicNodeOutputPorts(node) {
  if (`${node?.type || ""}`.trim() === "if") {
    return [
      { key: "true", label: "참" },
      { key: "false", label: "거짓" }
    ]
  }
  return [
    { key: "main", label: "출력" }
  ]
}

function getLogicPortAnchor(node, role, portKey = "main", graph = null, resolveNodeSize = null) {
  const x = Number(node?.position?.x) || 0
  const y = Number(node?.position?.y) || 0
  const size = typeof resolveNodeSize === "function"
    ? resolveNodeSize(node)
    : normalizeLogicNodeSize(node?.size, node?.type)
  const ports = role === "output"
    ? getLogicNodeOutputPorts(node)
    : getLogicNodeInputPorts(node, graph)
  const index = Math.max(0, ports.findIndex((port) => port.key === portKey))
  if (ports.length <= 1) {
    return {
      x: role === "output" ? x + size.width : x,
      y: y + Math.round(size.height / 2) - LOGIC_NODE_PORT_OFFSET_Y
    }
  }

  const topInset = 58
  const bottomInset = 26
  const usableHeight = Math.max(44, size.height - topInset - bottomInset)
  const gap = ports.length === 1 ? 0 : usableHeight / Math.max(1, ports.length - 1)
  const portY = y + topInset + Math.round(gap * index)
  return {
    x: role === "output" ? x + size.width : x,
    y: portY
  }
}

function getLogicReferenceOutputEntries(node) {
  const extras = LOGIC_REFERENCE_OUTPUTS_BY_TYPE[`${node?.type || ""}`.trim()] || []
  return [
    { value: "text", label: "본문" },
    ...extras
  ]
}

function formatLogicReferenceLabel(value, graph = null) {
  const raw = normalizeLogicReferenceExpression(value)
  if (!raw) {
    return ""
  }
  if (raw === "run.input") {
    return "시작 입력"
  }
  if (raw.startsWith("vars.")) {
    return `저장 변수 · ${raw.slice(5)}`
  }
  if (raw.startsWith("sessions.")) {
    const nodeId = raw.slice(9)
    const sourceNode = (graph?.nodes || []).find((item) => item.nodeId === nodeId)
    const sourceName = sourceNode?.title || getPaletteLibraryItem(sourceNode?.type)?.label || nodeId
    return `작업 세션 · ${sourceName}`
  }
  if (raw.startsWith("artifacts.")) {
    const suffix = raw.slice(10)
    return suffix === "last" ? "가장 최근 산출물" : `산출물 ${suffix}`
  }
  if (raw.startsWith("nodes.")) {
    const parts = raw.split(".")
    const nodeId = parts[1] || ""
    const sourceNode = (graph?.nodes || []).find((item) => item.nodeId === nodeId)
    const sourceName = sourceNode?.title || getPaletteLibraryItem(sourceNode?.type)?.label || nodeId
    if (parts[2] === "text") {
      return `노드 결과 · ${sourceName} → 본문`
    }
    if (parts[2] === "data" && parts[3]) {
      return `노드 결과 · ${sourceName} → ${parts[3]}`
    }
  }
  return raw
}

function formatLogicInlineReferenceText(value, graph = null) {
  const raw = `${value || ""}`
  if (!raw.includes("{{")) {
    return raw
  }
  return raw.replace(LOGIC_INLINE_REFERENCE_REGEX, (match, expression) => {
    const label = formatLogicReferenceLabel(expression, graph)
    if (!label) {
      return match
    }
    return `[${label}]`
  })
}

function appendLogicInlineReference(text, referenceValue) {
  const token = `${referenceValue || ""}`.trim()
  if (!token) {
    return `${text || ""}`
  }
  const current = `${text || ""}`
  if (!current) {
    return token
  }
  if (/\s$/.test(current)) {
    return `${current}${token}`
  }
  return `${current} ${token}`
}

function buildLogicReferenceOptions(graph, selectedNode) {
  const options = [
    { value: "{{run.input}}", label: "시작 입력" },
    { value: "{{artifacts.last}}", label: "가장 최근 산출물" }
  ]
  ;(graph?.nodes || []).forEach((node) => {
    if (!node?.nodeId || node.nodeId === selectedNode?.nodeId) {
      return
    }
    const nodeName = node.title || getPaletteLibraryItem(node.type)?.label || node.nodeId
    getLogicReferenceOutputEntries(node).forEach((entry) => {
      options.push({
        value: `{{nodes.${node.nodeId}.${entry.value}}}`,
        label: `노드 결과 · ${nodeName} → ${entry.label}`
      })
    })
    if (`${node?.type || ""}`.trim() === "set_var") {
      const variableName = `${node?.config?.name || ""}`.trim()
      if (variableName) {
        options.push({
          value: `{{vars.${variableName}}}`,
          label: `저장 변수 · ${variableName}`
        })
      }
    }
    if (LOGIC_SESSION_REFERENCE_TYPES.has(`${node?.type || ""}`.trim())) {
      options.push({
        value: `{{sessions.${node.nodeId}}}`,
        label: `작업 세션 · ${nodeName}`
      })
    }
  })
  return options
}

function getLogicFieldDisplayValue(graph, node, fieldKey, fallbackValue = "") {
  const field = (getLogicNodeInspectorDefinition(node?.type)?.fields || []).find((item) => item?.key === fieldKey)
  if (!field) {
    return formatLogicReferenceLabel(fallbackValue, graph) || fallbackValue || ""
  }
  const mode = getLogicFieldMode(graph, node, field)
  if (mode === "edge") {
    const edges = getLogicFieldIncomingEdges(graph, node?.nodeId, field.key)
    if (edges.length === 0) {
      return `${field.label} 연결 대기`
    }
    return edges.map((edge) => {
      const sourceNode = (graph?.nodes || []).find((item) => item.nodeId === edge.sourceNodeId)
      const sourceName = sourceNode?.title || getPaletteLibraryItem(sourceNode?.type)?.label || edge.sourceNodeId
      const sourcePort = `${edge?.sourcePort || "main"}`.trim().toLowerCase()
      const sourceLabel = sourcePort === "main"
        ? "본문"
        : (sourcePort === "true" ? "참 갈래" : (sourcePort === "false" ? "거짓 갈래" : sourcePort))
      return `${sourceName} → ${sourceLabel}`
    }).join(", ")
  }
  if (mode === "reference") {
    return formatLogicReferenceLabel(getLogicFieldReferenceValue(node, field), graph)
  }
  const literalValue = getLogicFieldLiteralValue(node, field)
  if (literalValue) {
    return formatLogicInlineReferenceText(literalValue, graph)
  }
  const fallbackLabel = formatLogicReferenceLabel(fallbackValue, graph)
  return fallbackLabel || formatLogicInlineReferenceText(fallbackValue, graph) || fallbackValue || ""
}

function getLogicPortDisplayLabel(node, role, portKey, graph = null) {
  const normalizedPort = `${portKey || "main"}`.trim().toLowerCase() || "main"
  if (role === "output") {
    if (normalizedPort === "main") {
      return "본문"
    }
    if (normalizedPort === "true") {
      return "참 갈래"
    }
    if (normalizedPort === "false") {
      return "거짓 갈래"
    }
    return normalizedPort
  }
  if (normalizedPort === "main") {
    return node?.type === "parallel_join" ? "합류" : "흐름"
  }
  const field = (getLogicNodeInspectorDefinition(node?.type)?.fields || []).find((item) => item?.key === normalizedPort)
  return field?.label || normalizedPort
}

function appendMetaChip(target, label, value, maxLength = 30) {
  const normalized = truncateText(value, maxLength)
  if (!normalized) {
    return
  }
  target.push({
    label,
    value: normalized
  })
}

function countConfiguredWorkers(config) {
  return [
    "groqModel",
    "geminiModel",
    "cerebrasModel",
    "nvidiaModel",
    "copilotModel",
    "codexModel"
  ].filter((key) => {
    const value = `${config?.[key] || ""}`.trim().toLowerCase()
    return value && value !== "none"
  }).length
}

function buildLogicNodeMetaChips(node, graph = null) {
  const config = node?.config || {}
  const chips = []
  switch (`${node?.type || ""}`.trim()) {
    case "start":
      appendMetaChip(chips, "입력", config.input ? getLogicFieldDisplayValue(graph, node, "input", config.input) : "시작 입력")
      break
    case "end":
      appendMetaChip(chips, "결과", getLogicFieldDisplayValue(graph, node, "result", config.result || ""))
      break
    case "output":
      appendMetaChip(chips, "출력", getLogicFieldDisplayValue(graph, node, "result", config.result || "연결 입력"))
      break
    case "if":
      appendMetaChip(chips, "왼쪽", getLogicFieldDisplayValue(graph, node, "leftRef", config.leftRef || ""))
      appendMetaChip(chips, "조건", config.operator)
      appendMetaChip(chips, "오른쪽", config.rightValue)
      break
    case "delay":
      appendMetaChip(chips, "지연", config.seconds ? `${config.seconds}s` : `${config.milliseconds || "0"}ms`)
      break
    case "set_var":
      appendMetaChip(chips, "변수", config.name)
      appendMetaChip(chips, "값", getLogicFieldDisplayValue(graph, node, "value", config.value || ""))
      break
    case "template":
      appendMetaChip(chips, "문장", getLogicFieldDisplayValue(graph, node, "template", config.template || ""))
      break
    case "chat_single":
      appendMetaChip(chips, "공급자", config.provider || "auto")
      appendMetaChip(chips, "모델", config.model || "AUTO")
      appendMetaChip(chips, "웹 참고", config.webSearchEnabled === "false" ? "끔" : "켬")
      break
    case "chat_orchestration":
      appendMetaChip(chips, "중심", config.provider || "AUTO")
      appendMetaChip(chips, "보조", `${countConfiguredWorkers(config)}개`)
      appendMetaChip(chips, "웹 참고", config.webSearchEnabled === "false" ? "끔" : "켬")
      break
    case "chat_multi":
      appendMetaChip(chips, "요약", config.summaryProvider || "AUTO")
      appendMetaChip(chips, "비교", `${countConfiguredWorkers(config)}개`)
      appendMetaChip(chips, "웹 참고", config.webSearchEnabled === "false" ? "끔" : "켬")
      break
    case "coding_single":
      appendMetaChip(chips, "공급자", config.provider || "auto")
      appendMetaChip(chips, "모델", config.model || "AUTO")
      appendMetaChip(chips, "언어", config.language || "auto")
      break
    case "coding_orchestration":
      appendMetaChip(chips, "중심", config.provider || "AUTO")
      appendMetaChip(chips, "보조", `${countConfiguredWorkers(config)}개`)
      appendMetaChip(chips, "언어", config.language || "auto")
      break
    case "coding_multi":
      appendMetaChip(chips, "정리", config.provider || "AUTO")
      appendMetaChip(chips, "비교", `${countConfiguredWorkers(config)}개`)
      appendMetaChip(chips, "언어", config.language || "auto")
      break
    case "routine_run":
      appendMetaChip(chips, "루틴", config.routineId)
      appendMetaChip(chips, "별칭", config.graphId)
      break
    case "memory_search":
      appendMetaChip(chips, "검색어", getLogicFieldDisplayValue(graph, node, "query", config.query || ""))
      appendMetaChip(chips, "결과", config.maxResults ? `${config.maxResults}개` : "")
      break
    case "memory_get":
    case "file_read":
    case "file_write":
      appendMetaChip(chips, "경로", config.path)
      break
    case "web_search":
      appendMetaChip(chips, "검색어", getLogicFieldDisplayValue(graph, node, "query", config.query || ""))
      appendMetaChip(chips, "최신성", config.freshness || "기본")
      break
    case "web_fetch":
      appendMetaChip(chips, "URL", config.url)
      appendMetaChip(chips, "추출", config.extractMode || "auto")
      break
    case "session_spawn":
      appendMetaChip(chips, "별칭", config.alias)
      appendMetaChip(chips, "런타임", config.runtime)
      break
    case "session_send":
      appendMetaChip(chips, "세션", config.sessionKey)
      appendMetaChip(chips, "타임아웃", config.timeoutSeconds ? `${config.timeoutSeconds}s` : "")
      break
    case "cron_run":
      appendMetaChip(chips, "작업", config.jobId)
      appendMetaChip(chips, "모드", config.runMode)
      break
    case "browser_execute":
    case "canvas_execute":
    case "nodes_invoke":
      appendMetaChip(chips, "동작", config.action)
      appendMetaChip(chips, "대상", config.targetUrl || config.node || config.invokeCommand || config.target)
      break
    case "telegram_stub":
      appendMetaChip(chips, "메시지", getLogicFieldDisplayValue(graph, node, "text", config.text || ""))
      break
    default:
      appendMetaChip(
        chips,
        "입력",
        getLogicFieldDisplayValue(
          graph,
          node,
          "input",
          config.input || config.query || config.path || ""
        )
      )
      break
  }
  return chips.slice(0, 4)
}

function buildLogicNodePreviewText(node, runtime, definition, graph = null) {
  const nodeType = `${node?.type || ""}`.trim()
  if (runtime?.result?.text) {
    return nodeType === "output" ? runtime.result.text : truncateText(runtime.result.text, 140)
  }
  const config = node?.config || {}
  const preview = [
    getLogicFieldDisplayValue(graph, node, "result", config.result || ""),
    getLogicFieldDisplayValue(graph, node, "template", config.template || ""),
    getLogicFieldDisplayValue(graph, node, "input", config.input || ""),
    getLogicFieldDisplayValue(graph, node, "query", config.query || ""),
    getLogicFieldDisplayValue(graph, node, "content", config.content || ""),
    formatLogicInlineReferenceText(config.url, graph),
    formatLogicInlineReferenceText(config.path, graph),
    getLogicFieldDisplayValue(graph, node, "message", config.message || ""),
    getLogicFieldDisplayValue(graph, node, "text", config.text || ""),
    config.invokeCommand,
    config.action
  ].find((item) => `${item || ""}`.trim())
  if (preview) {
    return nodeType === "output" ? preview : truncateText(preview, 140)
  }
  const fallback = definition?.example || definition?.description || "노드를 놓고 아래 설정에서 내용을 채워 주세요."
  return nodeType === "output" ? fallback : truncateText(fallback, 140)
}

function buildInspectorFieldGroups(fields) {
  const groups = []
  let currentGroup = {
    title: "기본 설정",
    description: "",
    fields: []
  }
  ;(Array.isArray(fields) ? fields : []).forEach((item) => {
    if (item?.kind === "section") {
      if (currentGroup.fields.length > 0) {
        groups.push(currentGroup)
      }
      currentGroup = {
        title: item.title || "섹션",
        description: item.description || "",
        fields: []
      }
      return
    }
    currentGroup.fields.push(item)
  })
  if (currentGroup.fields.length > 0) {
    groups.push(currentGroup)
  }
  return groups
}

function renderMetaChipList(e, chips, className = "logic-node-meta-chips") {
  if (!Array.isArray(chips) || chips.length === 0) {
    return null
  }
  return e("div", { className },
    chips.map((chip, index) => e("span", {
      key: `${chip.label}-${chip.value}-${index}`,
      className: "logic-node-meta-chip"
    },
    e("span", { className: "logic-node-meta-chip-label" }, chip.label),
    e("span", { className: "logic-node-meta-chip-value" }, chip.value)))
  )
}

function formatLogicGraphListId(graphId) {
  const normalized = `${graphId || ""}`.trim()
  if (!normalized) {
    return ""
  }
  return normalized.length > 14
    ? `…${normalized.slice(-8)}`
    : normalized
}

function renderGraphList(props) {
  const {
    e,
    graphs,
    selectedGraphId,
    draftGraph,
    listPaneRef,
    graphListRef,
    onScrollListMouseDown,
    onScrollListClickCapture,
    onSelectGraph,
    onCreateNewGraph,
    onDeleteGraph,
    onExportGraph,
    jsonBuffer,
    onJsonBufferChange,
    onImportGraph,
    onGraphFieldChange,
    onRun,
    onRunGraph,
    onSave,
    dirty,
    activeRunId,
    runSnapshot
  } = props

  const selectedSummary = summarizeLogicGraph(draftGraph)
  const currentGraphId = `${draftGraph?.graphId || selectedGraphId || ""}`.trim()
  const currentGraphStatus = activeRunId && runSnapshot
    ? (runSnapshot.status || "running")
    : (dirty ? "unsaved" : "saved")

  return e("section", {
    className: "logic-pane logic-list-pane logic-scroll-surface",
    ref: listPaneRef,
    onMouseDownCapture: onScrollListMouseDown,
    onClickCapture: onScrollListClickCapture
  },
    e("div", { className: "logic-pane-head" },
      e("div", null,
        e("div", { className: "routine-head-kicker" }, "흐름"),
        e("strong", null, "작업 흐름")
      ),
      e("div", { className: "logic-side-actions" },
        e("button", {
          className: "btn secondary",
          type: "button",
          onClick: onSave
        }, "저장"),
        e("button", {
          className: "btn primary",
          type: "button",
          onClick: onRun,
          disabled: !draftGraph
        }, "실행"),
        e("button", {
          className: "btn ghost",
          type: "button",
          onClick: onCreateNewGraph
        }, "새 흐름")
      )
    ),
    e("div", { className: "logic-json-card logic-current-graph-card" },
      e("div", { className: "routine-editor-section-head" },
        e("div", { className: "routine-editor-title" }, "지금 편집 중"),
        e("div", { className: "routine-editor-subtitle" }, currentGraphId || "아직 저장 전")
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "흐름 이름"),
        e("input", {
          className: "input",
          value: draftGraph?.title || "",
          placeholder: "예: 오전 회의 정리 자동화",
          onChange: (event) => onGraphFieldChange("title", event.target.value)
        })
      ),
      e("div", { className: "logic-graph-current-meta" },
        e("span", {
          className: `pill ${getLogicStatusTone(currentGraphStatus)}`
        }, getLogicStatusLabel(currentGraphStatus)),
        e("span", { className: "tiny" }, selectedSummary || "편집 중인 그래프 없음")
      ),
      e("div", { className: "logic-side-actions" },
        e("button", {
          className: "btn secondary",
          type: "button",
          onClick: onSave
        }, "저장"),
        e("button", {
          className: "btn primary",
          type: "button",
          onClick: onRun,
          disabled: !draftGraph
        }, "실행"),
        e("button", {
          className: "btn ghost",
          type: "button",
          onClick: onExportGraph,
          disabled: !draftGraph
        }, "JSON 내보내기"),
        e("button", {
          className: "btn danger",
          type: "button",
          onClick: () => onDeleteGraph(selectedGraphId || draftGraph?.graphId || ""),
          disabled: !selectedGraphId && !draftGraph?.graphId
        }, "삭제")
      )
    ),
    e("div", {
      className: "logic-graph-list logic-scroll-list",
      ref: graphListRef
    },
      graphs.length === 0
        ? e("div", { className: "logic-empty-card" }, "저장된 작업 흐름이 없습니다.")
        : graphs.map((item) => {
          const isActive = selectedGraphId === item.graphId
          const rowDescription = `${isActive ? (draftGraph?.description || "") : (item.description || "")}`.trim()
          const rowSummary = isActive
            ? selectedSummary
            : `${item.nodeCount || 0}개 노드 · ${item.edgeCount || 0}개 연결`
          const compactSummary = rowDescription || rowSummary
          const compactMetrics = rowDescription ? rowSummary : ""
          const compactStatus = getLogicStatusLabel(item.lastStatus || "saved")
          const rowMain = isActive
            ? e("div", {
              className: "logic-graph-list-main logic-graph-list-main-static"
            },
            e("input", {
              className: "input logic-graph-title-input",
              value: draftGraph?.title || "",
              onPointerDown: stopLogicCanvasEvent,
              onClick: (event) => event.stopPropagation(),
              onChange: (event) => onGraphFieldChange("title", event.target.value)
            }),
            e("div", { className: "logic-graph-row-subline tiny compact" },
              e("span", { className: "logic-graph-row-subline-text" }, compactSummary || "설명 없음"),
              compactMetrics
                ? e("span", { className: "logic-graph-row-summary" }, compactMetrics)
                : null,
              e("span", { className: `pill ${getLogicStatusTone(item.lastStatus || "saved")}` }, compactStatus)
            ))
            : e("button", {
              type: "button",
              className: "logic-graph-list-main",
              onClick: () => onSelectGraph(item.graphId)
            },
            e("strong", null, item.title || item.graphId),
            e("div", { className: "logic-graph-row-subline tiny compact" },
              e("span", { className: "logic-graph-row-subline-text" }, compactSummary || "설명 없음"),
              compactMetrics
                ? e("span", { className: "logic-graph-row-summary" }, compactMetrics)
                : null,
              e("span", { className: `pill ${getLogicStatusTone(item.lastStatus || "saved")}` }, compactStatus)
            ))
          return e("div", {
            key: item.graphId,
            className: `logic-graph-list-row ${isActive ? "active" : ""}`
          },
          rowMain,
          e("div", { className: "logic-graph-row-actions" },
            isActive
              ? e("button", {
                className: "btn secondary tiny-btn",
                type: "button",
                onClick: onSave
              }, "저장")
              : e("button", {
                className: "btn ghost tiny-btn",
                type: "button",
                onClick: () => onSelectGraph(item.graphId)
              }, "열기"),
            e("button", {
              className: "btn primary tiny-btn",
              type: "button",
              onClick: () => onRunGraph(item.graphId),
              disabled: !item.graphId
            }, "실행")
          ))
        })
    ),
    e("div", { className: "logic-json-card" },
      e("div", { className: "routine-editor-section-head" },
        e("div", { className: "routine-editor-title" }, "JSON 가져오기 / 내보내기"),
        e("div", { className: "routine-editor-subtitle" }, selectedSummary || "편집 중인 흐름 없음")
      ),
      e("textarea", {
        className: "input logic-json-textarea",
        value: jsonBuffer,
        placeholder: "logic.graph.v1 JSON을 붙여넣으면 이 작업 흐름으로 바뀝니다.",
        onChange: (event) => onJsonBufferChange(event.target.value)
      }),
      e("div", { className: "logic-side-actions" },
        e("button", {
          className: "btn secondary",
          type: "button",
          onClick: onImportGraph,
          disabled: !jsonBuffer.trim()
        }, "JSON 불러오기"),
        activeRunId && runSnapshot
          ? e("span", { className: "tiny" }, `실행 중: ${activeRunId} · ${getLogicStatusLabel(runSnapshot.status || "-")}`)
          : null
      )
    )
  )
}

function renderPalettePanel(props) {
  const {
    e,
    paletteGroup,
    palettePaneRef,
    paletteListRef,
    onScrollListMouseDown,
    onScrollListClickCapture,
    onPaletteGroupChange,
    onSelectNodeType
  } = props

  const activeGroup = getPaletteLibraryGroup(paletteGroup)

  return e("section", {
    className: "logic-pane logic-palette-pane logic-scroll-surface",
    ref: palettePaneRef,
    onMouseDownCapture: onScrollListMouseDown,
    onClickCapture: onScrollListClickCapture
  },
    e("div", { className: "logic-pane-head" },
      e("div", null,
        e("div", { className: "routine-head-kicker" }, "노드"),
        e("strong", null, "노드 팔레트")
      ),
      e("span", { className: "pill idle" }, activeGroup?.label || "흐름")
    ),
    e("div", { className: "logic-palette-tabs" },
      LOGIC_NODE_LIBRARY.map((group) => {
        const meta = getLogicCategoryMeta(group.key)
        return e("button", {
          key: group.key,
          type: "button",
          className: `logic-palette-tab logic-palette-tab-${meta.className} ${activeGroup?.key === group.key ? "active" : ""}`,
          onClick: () => onPaletteGroupChange(group.key)
        }, group.label)
      })
    ),
    e("div", {
      className: "logic-palette-list logic-scroll-list",
      ref: paletteListRef
    },
      activeGroup.items.map(([type, label]) => {
        const definition = getLogicNodeInspectorDefinition(type)
        const meta = getLogicCategoryMeta(type)
        const themeVars = buildLogicThemeVars(type)
        return e("button", {
          key: type,
          type: "button",
          className: `logic-palette-item-card logic-palette-item-card-${meta.className}`,
          style: themeVars,
          onClick: () => onSelectNodeType(type)
        },
        e("div", { className: "logic-palette-item-head" },
          e("div", null,
            e("strong", null, label),
            e("div", { className: "tiny logic-palette-item-summary" }, truncateText(definition?.example || definition?.description || label, 36))
          )
        ))
      })
    )
  )
}

function renderCanvas(props) {
  const {
    e,
    draftGraph,
    viewport,
    canvasGesture,
    canvasShellRef,
    selectedNodeId,
    selectedEdgeId,
    pendingSourceNodeId,
    connectionPreview,
    onSelectNode,
    onSelectEdge,
    onCanvasPointerDown,
    onNodePointerDown,
    onNodeResizePointerDown,
    onConnectionStart,
    onViewportReset,
    onViewportFit,
    onViewportZoomIn,
    onViewportZoomOut,
    onDeleteEdge,
    onDeleteNode,
    onDuplicateNode,
    activeRunSnapshot,
    runOutputExpanded,
    onRunOutputExpandedChange,
    resolveNodeSize
  } = props

  const nodes = Array.isArray(draftGraph?.nodes) ? draftGraph.nodes : []
  const edges = Array.isArray(draftGraph?.edges) ? draftGraph.edges : []
  const readNodeSize = typeof resolveNodeSize === "function"
    ? resolveNodeSize
    : (node) => normalizeLogicNodeSize(node?.size, node?.type)
  const stageMetrics = getStageMetrics(nodes, readNodeSize)
  const nodeStateMap = {}
  if (activeRunSnapshot && Array.isArray(activeRunSnapshot.nodes)) {
    activeRunSnapshot.nodes.forEach((item) => {
      if (item && item.nodeId) {
        nodeStateMap[item.nodeId] = item
      }
    })
  }

  const shellRect = canvasShellRef?.current?.getBoundingClientRect?.() || null
  const shellWidth = shellRect?.width || canvasShellRef?.current?.clientWidth || 0
  const shellHeight = shellRect?.height || canvasShellRef?.current?.clientHeight || 0
  const currentZoom = Number(viewport?.zoom) || 1
  const currentViewportX = Number(viewport?.x) || 0
  const currentViewportY = Number(viewport?.y) || 0

  const previewSourceNode = connectionPreview?.sourceNodeId
    ? nodes.find((item) => item.nodeId === connectionPreview.sourceNodeId)
    : null
  const previewPointerPosition = previewSourceNode && shellRect
    ? {
      x: clamp((Number(connectionPreview?.clientX) || 0) - shellRect.left, 0, shellWidth || Number.MAX_SAFE_INTEGER),
      y: clamp((Number(connectionPreview?.clientY) || 0) - shellRect.top, 0, shellHeight || Number.MAX_SAFE_INTEGER)
    }
    : null
  const previewSourceScreenPosition = previewSourceNode
    ? {
      x: currentViewportX + (getLogicPortPosition(previewSourceNode, "output", connectionPreview?.sourcePort || "main", draftGraph, readNodeSize).x * currentZoom),
      y: currentViewportY + (getLogicPortPosition(previewSourceNode, "output", connectionPreview?.sourcePort || "main", draftGraph, readNodeSize).y * currentZoom)
    }
    : null
  const previewTargetNode = connectionPreview?.targetNodeId
    ? nodes.find((item) => item.nodeId === connectionPreview.targetNodeId)
    : null
  const previewTargetScreenPosition = previewTargetNode
    ? {
      x: currentViewportX + (getLogicPortPosition(previewTargetNode, "input", connectionPreview?.targetPort || "main", draftGraph, readNodeSize).x * currentZoom),
      y: currentViewportY + (getLogicPortPosition(previewTargetNode, "input", connectionPreview?.targetPort || "main", draftGraph, readNodeSize).y * currentZoom)
    }
    : null

  return e("section", { className: "logic-pane logic-canvas-pane" },
    e("div", { className: "logic-pane-head logic-canvas-head" },
      e("div", { className: "logic-canvas-copy" },
        e("div", { className: "routine-head-kicker" }, "캔버스"),
        e("strong", null, draftGraph?.title || "새 작업 흐름"),
        e("div", { className: "logic-canvas-meta" },
          pendingSourceNodeId
            ? e("span", { className: "pill warn" }, `연결 대기: ${pendingSourceNodeId}`)
            : e("span", { className: "pill idle" }, `${nodes.length}개 노드`),
          e("span", { className: "tiny" }, "배경을 끌어 이동 · 휠로 확대/축소 · 점을 끌어 연결")
        )
      ),
      e("div", {
        className: "logic-canvas-toolbar",
        onPointerDown: stopLogicCanvasEvent
      },
        e("button", { className: "btn ghost tiny-btn", type: "button", onClick: onViewportZoomOut }, "−"),
        e("span", { className: "logic-canvas-zoom-label" }, `${Math.round((viewport?.zoom || 1) * 100)}%`),
        e("button", { className: "btn ghost tiny-btn", type: "button", onClick: onViewportZoomIn }, "+"),
        e("button", { className: "btn ghost tiny-btn", type: "button", onClick: onViewportFit }, "맞춤"),
        e("button", { className: "btn ghost tiny-btn", type: "button", onClick: onViewportReset }, "원점")
      )
    ),
    e("div", {
      className: `logic-canvas-shell ${canvasGesture !== "idle" ? "is-interacting" : ""} ${canvasGesture === "connect" ? "is-connecting" : ""}`,
      ref: canvasShellRef,
      onPointerDown: onCanvasPointerDown
    },
    e("div", { className: "logic-canvas-surface" }),
    previewSourceScreenPosition && previewPointerPosition && shellWidth > 0 && shellHeight > 0
      ? e("svg", {
        className: "logic-canvas-preview-overlay",
        viewBox: `0 0 ${shellWidth} ${shellHeight}`,
        preserveAspectRatio: "none"
      },
      e("path", {
        d: buildLogicCurvePath(
          previewSourceScreenPosition,
          previewTargetScreenPosition || previewPointerPosition
        ),
        className: "logic-edge-path preview"
      }),
      previewTargetScreenPosition
        ? e("circle", {
          cx: previewTargetScreenPosition.x,
          cy: previewTargetScreenPosition.y,
          r: 12,
          className: "logic-preview-target"
        })
        : null,
      e("circle", {
        cx: previewPointerPosition.x,
        cy: previewPointerPosition.y,
        r: 7,
        className: "logic-preview-pointer"
      }))
      : null,
    e("div", {
      className: "logic-canvas-stage",
      style: {
        width: `${stageMetrics.width}px`,
        height: `${stageMetrics.height}px`,
        transform: `translate(${viewport?.x || 0}px, ${viewport?.y || 0}px) scale(${viewport?.zoom || 1})`
      }
    },
    e("svg", {
      className: "logic-canvas-lines",
      viewBox: `0 0 ${stageMetrics.width} ${stageMetrics.height}`,
      preserveAspectRatio: "none"
    },
    edges.map((edge) => {
      const source = nodes.find((item) => item.nodeId === edge.sourceNodeId)
      const target = nodes.find((item) => item.nodeId === edge.targetNodeId)
      if (!source || !target) {
        return null
      }
      const path = buildLogicCurvePath(
        getLogicPortPosition(source, "output", edge.sourcePort || "main", draftGraph, readNodeSize),
        getLogicPortPosition(target, "input", edge.targetPort || "main", draftGraph, readNodeSize)
      )
      return e("path", {
        key: edge.edgeId,
        d: path,
        className: `logic-edge-path ${selectedEdgeId === edge.edgeId ? "active" : ""}`,
        onPointerDown: (event) => stopLogicCanvasEvent(event),
        onClick: (event) => {
          event.stopPropagation()
          onSelectEdge(edge.edgeId)
        }
      })
    })),
    nodes.map((node) => {
      const runtime = nodeStateMap[node.nodeId] || null
      const status = runtime?.status || (node.enabled === false ? "disabled" : "idle")
      const categoryMeta = getLogicCategoryMeta(node.type)
      const definition = getLogicNodeInspectorDefinition(node.type)
      const chips = buildLogicNodeMetaChips(node, draftGraph)
      const paletteItem = getPaletteLibraryItem(node.type)
      const nodeSize = readNodeSize(node)
      const themeVars = buildLogicThemeVars(node.type)
      const statusLabel = getLogicStatusLabel(status)
      const inputPorts = getLogicNodeInputPorts(node, draftGraph)
      const outputPorts = getLogicNodeOutputPorts(node)

      return e("div", {
        key: node.nodeId,
        className: `logic-node-card logic-node-card-${categoryMeta.className} ${selectedNodeId === node.nodeId ? "active" : ""} ${status}`,
        "data-logic-node-id": node.nodeId,
        "data-logic-node-type": node.type,
        style: {
          ...themeVars,
          left: `${node.position?.x || 0}px`,
          top: `${node.position?.y || 0}px`,
          width: `${nodeSize.width}px`,
          height: `${nodeSize.height}px`
        },
        onPointerDown: (event) => onNodePointerDown(event, node.nodeId),
        onClick: () => onSelectNode(node.nodeId)
      },
      LOGIC_NODE_RESIZE_HANDLES.map((handle) => e("button", {
        key: `${node.nodeId}-${handle}`,
        type: "button",
        className: `logic-node-resize-handle logic-node-resize-handle-${handle}`,
        title: `${handle} 리사이즈`,
        onPointerDown: (event) => onNodeResizePointerDown(event, node.nodeId, handle)
      })),
      inputPorts.map((port) => {
        const position = getLogicPortPosition(node, "input", port.key, draftGraph, readNodeSize)
        return e("div", {
          key: `${node.nodeId}-input-${port.key}`,
          className: "logic-node-port-anchor logic-node-port-anchor-input",
          style: {
            left: "0px",
            top: `${position.y - (Number(node.position?.y) || 0)}px`
          }
        },
        e("button", {
          className: `logic-node-port logic-node-port-input ${connectionPreview?.targetNodeId === node.nodeId && `${connectionPreview?.targetPort || "main"}` === port.key ? "active" : ""}`,
          type: "button",
          title: `${port.label} 입력`,
          "data-logic-port-role": "input",
          "data-logic-node-id": node.nodeId,
          "data-logic-port-key": port.key,
          onPointerDown: (event) => {
            event.preventDefault()
            stopLogicCanvasEvent(event)
          },
          onClick: (event) => {
            event.preventDefault()
            event.stopPropagation()
            onSelectNode(node.nodeId)
          }
        }),
        port.key !== "main"
          ? e("span", { className: "logic-node-port-label logic-node-port-label-input" }, port.label)
          : null)
      }),
      outputPorts.map((port) => {
        const position = getLogicPortPosition(node, "output", port.key, draftGraph, readNodeSize)
        return e("div", {
          key: `${node.nodeId}-output-${port.key}`,
          className: "logic-node-port-anchor logic-node-port-anchor-output",
          style: {
            right: "0px",
            top: `${position.y - (Number(node.position?.y) || 0)}px`
          }
        },
        port.key !== "main"
          ? e("span", { className: "logic-node-port-label logic-node-port-label-output" }, port.label)
          : null,
        e("button", {
          className: `logic-node-port logic-node-port-output ${pendingSourceNodeId === node.nodeId && `${connectionPreview?.sourcePort || "main"}` === port.key ? "active" : ""}`,
          type: "button",
          title: `${port.label} 출력`,
          "data-logic-port-role": "output",
          "data-logic-node-id": node.nodeId,
          "data-logic-port-key": port.key,
          onPointerDown: (event) => onConnectionStart(event, node.nodeId, port.key)
        }))
      }),
      e("div", { className: "logic-node-frame" },
        e("div", { className: "logic-node-drag-handle" },
          e("span", {
            className: `logic-node-kind-badge logic-node-kind-badge-${categoryMeta.className}`,
            style: themeVars
          }, categoryMeta.label),
          e("div", { className: "logic-node-title-block" },
            e("strong", null, node.title || paletteItem.label || node.type),
            e("span", { className: "tiny" }, truncateText(definition?.example || definition?.description || paletteItem.label || node.type, 56))
          ),
          e("span", { className: `pill ${getLogicStatusTone(status)}` }, statusLabel)
        ),
        e("div", {
          className: "logic-node-body"
        },
        renderMetaChipList(e, chips),
        e("div", {
          className: `logic-node-preview ${node.type === "output" ? "logic-node-preview-full" : ""} ${runtime?.result?.text ? "" : "muted"}`
        }, buildLogicNodePreviewText(node, runtime, definition, draftGraph))),
        e("div", { className: "logic-node-actions" },
          e("button", {
            className: "btn ghost tiny-btn",
            type: "button",
            onPointerDown: stopLogicCanvasEvent,
            onClick: (event) => {
              event.stopPropagation()
              onDuplicateNode(node.nodeId)
            }
          }, "복제"),
          e("button", {
            className: "btn ghost tiny-btn",
            type: "button",
            onPointerDown: stopLogicCanvasEvent,
            onClick: (event) => {
              event.stopPropagation()
              onDeleteNode(node.nodeId)
            }
          }, "삭제")
        ))
      )
    }),
    nodes.length === 0
      ? e("div", { className: "logic-canvas-empty" }, "우측 노드 팔레트에서 카드를 눌러 노드를 추가하고, 포트를 끌어서 연결하세요.")
      : null
    ),
    selectedEdgeId
      ? e("button", {
        className: "btn danger logic-edge-delete",
        type: "button",
        onPointerDown: stopLogicCanvasEvent,
        onClick: () => onDeleteEdge(selectedEdgeId)
      }, "선택 연결 삭제")
      : null,
    renderLogicRunOutputPanel({
      ...props,
      runSnapshot: activeRunSnapshot,
      expanded: runOutputExpanded,
      onExpandedChange: onRunOutputExpandedChange
    })
    )
  )
}

function buildLogicRunNodeMap(runSnapshot) {
  const map = {}
  if (runSnapshot && Array.isArray(runSnapshot.nodes)) {
    runSnapshot.nodes.forEach((item) => {
      if (item?.nodeId) {
        map[item.nodeId] = item
      }
    })
  }
  return map
}

function getLogicRunResultText(runtime) {
  return `${runtime?.result?.text || ""}`.trim()
}

function getLogicRunResultDataRows(runtime) {
  const data = runtime?.result?.data || {}
  if (!data || typeof data !== "object") {
    return []
  }
  return Object.entries(data)
    .filter(([, value]) => `${value || ""}`.trim())
    .map(([key, value]) => ({
      label: `data.${key}`,
      value: `${value}`
    }))
}

function resolveLogicEdgeOutputText(edge, runtimeMap) {
  const sourceRuntime = runtimeMap[edge.sourceNodeId]
  if (!sourceRuntime?.result) {
    return ""
  }
  const port = `${edge.sourcePort || "main"}`.trim()
  if (port.startsWith("data.")) {
    return `${sourceRuntime.result.data?.[port.slice(5)] || ""}`.trim()
  }
  return getLogicRunResultText(sourceRuntime)
}

function buildLogicNodeInputRows(node, graph, runtimeMap) {
  const incomingEdges = (Array.isArray(graph?.edges) ? graph.edges : [])
    .filter((edge) => edge.targetNodeId === node.nodeId)
  if (incomingEdges.length > 0) {
    return incomingEdges
      .map((edge) => {
        const sourceNode = (graph?.nodes || []).find((item) => item.nodeId === edge.sourceNodeId)
        const sourceLabel = sourceNode?.title || getPaletteLibraryItem(sourceNode?.type)?.label || edge.sourceNodeId
        const targetPort = `${edge.targetPort || "main"}`.trim()
        return {
          label: targetPort === "main" ? sourceLabel : `${sourceLabel} · ${targetPort}`,
          value: resolveLogicEdgeOutputText(edge, runtimeMap)
        }
      })
      .filter((row) => `${row.value || ""}`.trim())
  }

  const config = node?.config || {}
  const configInputKeys = [
    "input",
    "query",
    "content",
    "message",
    "text",
    "task",
    "template",
    "value",
    "result",
    "leftRef"
  ]
  return configInputKeys
    .filter((key) => `${config[key] || ""}`.trim())
    .map((key) => ({
      label: key,
      value: getLogicFieldDisplayValue(graph, node, key, config[key])
    }))
}

function buildLogicNodeProviderRows(node) {
  const config = node?.config || {}
  const rows = []
  const append = (label, value) => {
    const text = `${value || ""}`.trim()
    if (text && text.toLowerCase() !== "none") {
      rows.push({ label, value: text })
    }
  }
  append("공급자", config.provider || config.summaryProvider)
  append("모델", config.model || config.summaryModel)
  append("Groq", config.groqModel)
  append("Gemini", config.geminiModel)
  append("Cerebras", config.cerebrasModel)
  append("NVIDIA NIM", config.nvidiaModel)
  append("Copilot", config.copilotModel)
  append("Codex", config.codexModel)
  return rows
}

function buildLogicNodeWebReferenceRows(node, runtime) {
  const config = node?.config || {}
  const rows = []
  const append = (label, value) => {
    const text = `${value || ""}`.trim()
    if (text) {
      rows.push({ label, value: text })
    }
  }
  ;(runtime?.result?.links || []).forEach((link, index) => append(`참고 ${index + 1}`, link));
  `${config.webUrls || ""}`
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean)
    .forEach((url, index) => append(`설정 URL ${index + 1}`, url))
  if (`${node?.type || ""}`.trim().startsWith("web_")) {
    append("웹 대상", config.url || config.query)
  }
  return rows
}

function renderLogicRunValueRows(e, rows) {
  if (!Array.isArray(rows) || rows.length === 0) {
    return null
  }
  return rows.map((row, index) => e("div", {
    key: `${row.label}-${index}`,
    className: "logic-run-output-row"
  },
  e("span", null, row.label),
  e("pre", null, row.value)))
}

function renderLogicRunOutputPanel(props) {
  const {
    e,
    draftGraph,
    runSnapshot,
    expanded,
    onExpandedChange
  } = props
  const nodes = Array.isArray(draftGraph?.nodes) ? draftGraph.nodes : []
  const runtimeMap = buildLogicRunNodeMap(runSnapshot)
  const visibleNodes = nodes.filter((node) => runtimeMap[node.nodeId]?.result)
  const finalOutput = `${runSnapshot?.resultText || ""}`.trim()
  const hasFinalOutput = finalOutput && ["completed", "error", "canceled"].includes(`${runSnapshot?.status || ""}`.trim())
  const isExpanded = Boolean(expanded)
  const setExpanded = (nextValue) => {
    if (typeof onExpandedChange === "function") {
      onExpandedChange(nextValue)
    }
  }

  return e("section", {
    className: `logic-run-output-panel ${isExpanded ? "expanded" : "collapsed"}`,
    onPointerDown: stopLogicCanvasEvent,
    onWheel: (event) => event.stopPropagation()
  },
    e("div", { className: "logic-run-output-head" },
      e("div", null,
        e("div", { className: "routine-head-kicker" }, "실행 결과"),
        e("strong", null, "노드 입출력")
      ),
      e("div", { className: "logic-run-output-head-actions" },
        runSnapshot
          ? e("span", { className: `pill ${getLogicStatusTone(runSnapshot.status || "-")}` }, getLogicStatusLabel(runSnapshot.status || "-"))
          : e("span", { className: "pill idle" }, "대기"),
        e("button", {
          className: "btn ghost tiny-btn",
          type: "button",
          onClick: () => setExpanded(!isExpanded)
        }, isExpanded ? "최소화" : "펼치기"))
    ),
    isExpanded
      ? e("div", { className: "logic-run-output-scroll" },
        hasFinalOutput
          ? e("article", { className: "logic-run-output-final" },
            e("strong", null, "최종 출력"),
            e("pre", null, finalOutput))
          : null,
        visibleNodes.length > 0
          ? visibleNodes.map((node) => {
            const runtime = runtimeMap[node.nodeId]
            const paletteItem = getPaletteLibraryItem(node.type)
            const inputs = buildLogicNodeInputRows(node, draftGraph, runtimeMap)
            const outputText = getLogicRunResultText(runtime)
            const outputData = getLogicRunResultDataRows(runtime)
            const providerRows = buildLogicNodeProviderRows(node)
            const webRows = buildLogicNodeWebReferenceRows(node, runtime)
            return e("article", {
              key: node.nodeId,
              className: `logic-run-output-card ${node.type === "output" ? "logic-run-output-card-full" : ""}`
            },
            e("div", { className: "logic-run-output-card-head" },
              e("strong", null, node.title || paletteItem.label || node.type),
              e("span", { className: `pill ${getLogicStatusTone(runtime.status || "-")}` }, getLogicStatusLabel(runtime.status || "-"))
            ),
            inputs.length > 0
              ? e("div", { className: "logic-run-output-section" },
                e("span", { className: "logic-run-output-section-title" }, "입력"),
                renderLogicRunValueRows(e, inputs))
              : null,
            outputText || outputData.length > 0
              ? e("div", { className: "logic-run-output-section" },
                e("span", { className: "logic-run-output-section-title" }, "출력"),
                outputText ? e("pre", { className: "logic-run-output-text" }, outputText) : null,
                renderLogicRunValueRows(e, outputData))
              : null,
            providerRows.length > 0
              ? e("div", { className: "logic-run-output-section" },
                e("span", { className: "logic-run-output-section-title" }, "공급자 / 모델"),
                renderLogicRunValueRows(e, providerRows))
              : null,
            webRows.length > 0
              ? e("div", { className: "logic-run-output-section" },
                e("span", { className: "logic-run-output-section-title" }, "웹 참고"),
                renderLogicRunValueRows(e, webRows))
              : null)
          })
          : (hasFinalOutput ? null : e("div", { className: "logic-detail-empty" }, "실행 후 각 노드의 입력과 출력이 여기에 표시됩니다.")))
      : null
  )
}

function renderNodeReferenceGuide(e) {
  return e("div", { className: "logic-inspector-docs" },
    e("strong", null, "입력 넣는 방법"),
    e("div", { className: "tiny" }, "1. 직접 적기: 프롬프트나 문장을 바로 입력합니다."),
    e("div", { className: "tiny" }, "2. 목록에서 고르기: 시작 입력, 저장 변수, 다른 노드 결과를 선택합니다."),
    e("div", { className: "tiny" }, "3. 선으로 받기: 필드를 '연결해서 받기'로 바꾸고 캔버스에서 점을 이어 줍니다.")
  )
}

function renderLogicPathBrowser(e, pathBrowser, options = {}) {
  const {
    allowDirectorySelection,
    onClose,
    onSelectRoot,
    onNavigate,
    onSelectValue
  } = options
  const roots = Array.isArray(pathBrowser?.roots) ? pathBrowser.roots : []
  const items = Array.isArray(pathBrowser?.items) ? pathBrowser.items : []

  return e("div", { className: "logic-path-browser-card" },
    e("div", { className: "logic-path-browser-head" },
      e("div", null,
        e("strong", null, pathBrowser?.fieldLabel || "경로 탐색"),
        e("div", { className: "tiny" }, pathBrowser?.displayPath || "루트를 선택하세요.")
      ),
      e("button", {
        type: "button",
        className: "btn ghost tiny-btn",
        onClick: onClose
      }, "닫기")
    ),
    roots.length > 1
      ? e("div", { className: "logic-path-browser-root-tabs" },
        roots.map((root) => e("button", {
          key: root.key,
          type: "button",
          className: `logic-path-browser-root-tab ${pathBrowser?.rootKey === root.key ? "active" : ""}`,
          onClick: () => onSelectRoot(root.key)
        }, root.label)))
      : null,
    e("div", { className: "logic-path-browser-toolbar" },
      pathBrowser?.parentBrowsePath !== null && pathBrowser?.parentBrowsePath !== undefined
        ? e("button", {
          type: "button",
          className: "btn ghost tiny-btn",
          onClick: () => onNavigate(pathBrowser.parentBrowsePath)
        }, "상위 폴더")
        : e("span", { className: "tiny" }, "최상위"),
      allowDirectorySelection && pathBrowser?.directorySelectPath
        ? e("button", {
          type: "button",
          className: "btn secondary tiny-btn",
          onClick: () => onSelectValue(pathBrowser.directorySelectPath)
        }, "현재 폴더 선택")
        : null
    ),
    e("div", { className: "logic-path-browser-status tiny" },
      pathBrowser?.loading
        ? "경로 목록을 불러오는 중입니다."
        : (pathBrowser?.message || "경로를 선택하세요.")
    ),
    e("div", { className: "logic-path-browser-list" },
      items.length > 0
        ? items.map((item, index) => e("div", {
          key: `${item.selectPath || item.browsePath || item.name || "item"}-${index}`,
          className: `logic-path-browser-entry ${item.isDirectory ? "directory" : "file"}`
        },
        e("button", {
          type: "button",
          className: "logic-path-browser-entry-main",
          onClick: () => {
            if (item.isDirectory) {
              onNavigate(item.browsePath)
              return
            }
            onSelectValue(item.selectPath)
          }
        },
        e("strong", null, item.name || (item.isDirectory ? "(폴더)" : "(파일)")),
        e("span", { className: "tiny" }, item.description || (item.isDirectory ? "폴더" : "파일"))),
        item.isDirectory
          ? (allowDirectorySelection
            ? e("button", {
              type: "button",
              className: "btn ghost tiny-btn",
              onClick: () => onSelectValue(item.selectPath)
            }, "선택")
            : null)
          : e("button", {
            type: "button",
            className: "btn ghost tiny-btn",
            onClick: () => onSelectValue(item.selectPath)
          }, "선택")))
        : e("div", { className: "logic-path-browser-empty" }, "표시할 항목이 없습니다.")
    )
  )
}

function renderLogicBindingModeTabs(e, mode, onChangeMode) {
  return e("div", { className: "logic-field-mode-tabs" },
    [
      ["literal", "직접 입력"],
      ["reference", "목록에서 고르기"],
      ["edge", "연결해서 받기"]
    ].map(([value, label]) => e("button", {
      key: value,
      type: "button",
      className: `logic-field-mode-tab ${mode === value ? "active" : ""}`,
      onClick: () => onChangeMode(value)
    }, label))
  )
}

function renderNodeField(
  e,
  field,
  selectedNode,
  draftGraph,
  onNodeConfigChange,
  onNodeBindingChange,
  logicInspectorCatalogs,
  pathBrowserState,
  pathBrowserActions
) {
  const config = selectedNode?.config || {}
  const currentValue = config[field.key] ?? ""
  let control = null
  const bindable = isLogicBindableField(field)
  const fieldMode = bindable ? getLogicFieldMode(draftGraph, selectedNode, field) : "literal"
  const literalValue = bindable ? getLogicFieldLiteralValue(selectedNode, field) : currentValue
  const referenceValue = bindable ? getLogicFieldReferenceValue(selectedNode, field) : ""
  const referenceOptions = bindable
    ? ensureOptionValue(buildLogicReferenceOptions(draftGraph, selectedNode), referenceValue, formatLogicReferenceLabel(referenceValue, draftGraph))
    : []
  const connectedEdges = bindable ? getLogicFieldIncomingEdges(draftGraph, selectedNode?.nodeId, field.key) : []
  const canInsertInlineReference = bindable
    && fieldMode === "literal"
    && (field.control === "textarea" || field.control === "text")

  if (bindable) {
    const commonFieldProps = {
      placeholder: field.placeholder || ""
    }
    let modeSpecificControl = null
    if (fieldMode === "literal") {
      if (field.control === "textarea") {
        modeSpecificControl = e("textarea", {
          className: "input logic-inspector-textarea logic-config-textarea",
          rows: field.rows || 4,
          value: literalValue,
          ...commonFieldProps,
          onChange: (event) => onNodeBindingChange(selectedNode.nodeId, field.key, {
            mode: "literal",
            literalValue: event.target.value
          })
        })
      } else {
        modeSpecificControl = e("input", {
          className: "input",
          value: literalValue,
          ...commonFieldProps,
          onChange: (event) => onNodeBindingChange(selectedNode.nodeId, field.key, {
            mode: "literal",
            literalValue: event.target.value
          })
        })
      }
      if (canInsertInlineReference && referenceOptions.length > 0) {
        const previewText = formatLogicInlineReferenceText(literalValue, draftGraph)
        modeSpecificControl = e("div", { className: "logic-binding-mode-panel" },
          modeSpecificControl,
          e("div", { className: "logic-inline-reference-tools" },
            e("span", { className: "tiny logic-inline-reference-label" }, "값 넣기"),
            e("select", {
              className: "input logic-inline-reference-select",
              defaultValue: "",
              onChange: (event) => {
                const nextReference = `${event.target.value || ""}`.trim()
                if (!nextReference) {
                  return
                }
                onNodeBindingChange(selectedNode.nodeId, field.key, {
                  mode: "literal",
                  literalValue: appendLogicInlineReference(literalValue, nextReference)
                })
                event.target.value = ""
              }
            },
            e("option", { value: "" }, "시작 입력, 저장 변수, 다른 노드 결과 넣기"),
            referenceOptions.map((option, index) => e("option", {
              key: `${option.value || "ref"}-${index}`,
              value: option.value || ""
            }, option.label || option.value || "값")))),
          e("div", { className: "tiny logic-binding-help" },
            literalValue && previewText !== literalValue
              ? `보이는 문장: ${previewText}`
              : "문장 안에 다른 값이 필요하면 목록에서 골라 바로 끼워 넣으세요."))
      }
    } else if (fieldMode === "reference") {
      modeSpecificControl = e("div", { className: "logic-binding-mode-panel" },
        e("select", {
          className: "input",
          value: referenceValue,
          onChange: (event) => onNodeBindingChange(selectedNode.nodeId, field.key, {
            mode: "reference",
            referenceValue: event.target.value
          })
        }, renderOptionList(e, referenceOptions, "선택할 항목 없음")),
        e("div", { className: "tiny logic-binding-help" },
          referenceValue
            ? formatLogicReferenceLabel(referenceValue, draftGraph)
            : "시작 입력이나 다른 노드 결과를 목록에서 고르세요.")
      )
    } else {
      modeSpecificControl = e("div", { className: "logic-binding-mode-panel logic-binding-mode-panel-edge" },
        e("div", { className: "tiny logic-binding-help" },
          connectedEdges.length > 0
            ? connectedEdges.map((edge) => {
              const sourceNode = (draftGraph?.nodes || []).find((item) => item.nodeId === edge.sourceNodeId)
              const sourceName = sourceNode?.title || getPaletteLibraryItem(sourceNode?.type)?.label || edge.sourceNodeId
              const sourcePort = `${edge?.sourcePort || "main"}`.trim().toLowerCase()
              return `${sourceName} · ${sourcePort === "main" ? "본문" : sourcePort}`
            }).join(", ")
            : `캔버스에서 이 노드의 '${field.label}' 점으로 선을 연결하세요.`),
        connectedEdges.length > 0
          ? e("button", {
            type: "button",
            className: "btn ghost tiny-btn",
            onClick: () => onNodeBindingChange(selectedNode.nodeId, field.key, {
              mode: "edge",
              clearEdges: true
            })
          }, "연결 지우기")
          : null
      )
    }

    return e("label", {
      key: field.key,
      className: "routine-field logic-field-with-binding"
    },
    e("span", { className: "routine-field-label" }, field.label),
    renderLogicBindingModeTabs(e, fieldMode, (nextMode) => onNodeBindingChange(selectedNode.nodeId, field.key, { mode: nextMode })),
    modeSpecificControl)
  }

  if (field.control === "textarea") {
    control = e("textarea", {
      className: "input logic-inspector-textarea logic-config-textarea",
      rows: field.rows || 4,
      value: currentValue,
      placeholder: field.placeholder || "",
      onChange: (event) => onNodeConfigChange(field.key, event.target.value)
    })
  } else if (field.control === "select") {
    const options = ensureOptionValue(field.options, currentValue, currentValue)
    control = e("select", {
      className: "input",
      value: currentValue,
      onChange: (event) => onNodeConfigChange(field.key, event.target.value)
    }, renderOptionList(e, options))
  } else if (field.control === "catalog-select") {
    const options = ensureOptionValue(
      readCatalogOptions(logicInspectorCatalogs, field.catalogKey),
      currentValue,
      currentValue
    )
    control = e("select", {
      className: "input",
      value: currentValue,
      onChange: (event) => onNodeConfigChange(field.key, event.target.value)
    }, renderOptionList(e, options, field.placeholder || "선택지 없음"))
  } else if (field.control === "provider-model") {
    const provider = `${config[field.providerKey || "provider"] || ""}`.trim().toLowerCase()
    const baseOptions = resolveProviderModelOptions(selectedNode, field, logicInspectorCatalogs)
    const options = provider
      ? ensureOptionValue(baseOptions, currentValue, currentValue || provider)
      : ensureOptionValue([{ value: "", label: field.autoLabel || "AUTO" }], currentValue, currentValue)
    control = e("select", {
      className: "input",
      value: currentValue,
      onChange: (event) => onNodeConfigChange(field.key, event.target.value)
    }, renderOptionList(
      e,
      options.length > 0 ? options : [{ value: "", label: field.autoLabel || "AUTO" }],
      provider ? `${provider} 모델 로딩 전` : (field.autoLabel || "AUTO")
    ))
  } else if (field.control === "number") {
    control = e("input", {
      className: "input",
      type: "number",
      min: field.min,
      max: field.max,
      step: field.step,
      value: currentValue,
      placeholder: field.placeholder || "",
      onChange: (event) => onNodeConfigChange(field.key, event.target.value)
    })
  } else if (field.control === "path") {
    const isBrowserOpen = pathBrowserState?.open
      && pathBrowserState?.nodeId === selectedNode?.nodeId
      && pathBrowserState?.fieldKey === field.key
    control = e("div", { className: "logic-path-field" },
      e("div", { className: "logic-path-field-row" },
        e("input", {
          className: "input",
          value: currentValue,
          placeholder: field.placeholder || "",
          onChange: (event) => onNodeConfigChange(field.key, event.target.value)
        }),
        e("button", {
          type: "button",
          className: "btn ghost tiny-btn",
          onClick: () => pathBrowserActions?.onOpen?.(field)
        }, "찾아보기")
      ),
      isBrowserOpen
        ? renderLogicPathBrowser(e, pathBrowserState, {
          allowDirectorySelection: !!field.allowDirectorySelection,
          onClose: () => pathBrowserActions?.onClose?.(),
          onSelectRoot: (rootKey) => pathBrowserActions?.onSelectRoot?.(rootKey),
          onNavigate: (browsePath) => pathBrowserActions?.onNavigate?.(browsePath),
          onSelectValue: (value) => pathBrowserActions?.onSelectValue?.(value)
        })
        : null
    )
  } else {
    control = e("input", {
      className: "input",
      value: currentValue,
      placeholder: field.placeholder || "",
      onChange: (event) => onNodeConfigChange(field.key, event.target.value)
    })
  }

  return e("label", {
    key: field.key,
    className: "routine-field"
  },
  e("span", { className: "routine-field-label" }, field.label),
  control)
}

function renderSelectedNodeCard(props) {
  const {
    e,
    draftGraph,
    selectedNode,
    onNodeFieldChange,
    onToggleNodeEnabled,
    onNodeConfigChange,
    onNodeBindingChange,
    logicInspectorCatalogs,
    logicPathBrowser,
    onOpenPathBrowser,
    onClosePathBrowser,
    onSelectPathBrowserRoot,
    onNavigatePathBrowser,
    onSelectPathBrowserValue
  } = props

  const definition = getLogicNodeInspectorDefinition(selectedNode?.type)
  const categoryMeta = getLogicCategoryMeta(selectedNode?.type)
  const themeVars = buildLogicThemeVars(selectedNode?.type)
  const paletteItem = getPaletteLibraryItem(selectedNode?.type)
  const nodeDisplayName = selectedNode?.title || paletteItem?.label || "이름 없는 노드"
  const fieldGroups = buildInspectorFieldGroups(definition?.fields)
  const chips = buildLogicNodeMetaChips(selectedNode, draftGraph)
  const knownConfigKeys = new Set((definition?.fields || []).filter((item) => item.kind === "field").map((item) => item.key))
  const extraConfigEntries = Object.entries(selectedNode?.config || {}).filter(([key]) =>
    !knownConfigKeys.has(key)
    && !key.startsWith(LOGIC_FIELD_MODE_PREFIX)
    && !key.startsWith(LOGIC_FIELD_LITERAL_PREFIX)
    && !key.startsWith(LOGIC_FIELD_REFERENCE_PREFIX)
  )

  return e("div", { className: "routine-editor-card logic-inspector-card logic-selected-node-card" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "logic-selected-node-title" },
        e("span", {
          className: `logic-node-kind-badge logic-node-kind-badge-${categoryMeta.className}`,
          style: themeVars
        }, categoryMeta.label),
        e("div", null,
          e("div", { className: "routine-editor-title" }, nodeDisplayName),
          e("div", { className: "routine-editor-subtitle" }, selectedNode.nodeId)
        )
      ),
      e("span", { className: `pill ${selectedNode.enabled === false ? "idle" : "ok"}` }, selectedNode.enabled === false ? "비활성" : "활성")
    ),
    e("div", { className: "logic-selected-node-summary" },
      e("div", { className: "tiny" }, definition?.description || "이 노드 설명이 없습니다."),
      renderMetaChipList(e, chips, "logic-node-meta-chips logic-node-meta-chips-wide")
    ),
    e("div", { className: "routine-form-grid routine-form-grid-tight" },
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "제목"),
        e("input", {
          className: "input",
          value: selectedNode.title || "",
          onChange: (event) => onNodeFieldChange("title", event.target.value)
        })
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "종류"),
        e("input", {
          className: "input",
          value: paletteItem?.label || selectedNode.type || "",
          readOnly: true
        })
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "계속 진행"),
        e("select", {
          className: "input",
          value: selectedNode.continueOnError ? "true" : "false",
          onChange: (event) => onNodeFieldChange("continueOnError", event.target.value === "true")
        },
        e("option", { value: "false" }, "실패 시 중단"),
        e("option", { value: "true" }, "실패 무시"))
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "노드 활성"),
        e("select", {
          className: "input",
          value: selectedNode.enabled === false ? "false" : "true",
          onChange: (event) => onNodeFieldChange("enabled", event.target.value === "true")
        }, renderOptionList(e, [
          { value: "true", label: "사용" },
          { value: "false", label: "비활성" }
        ]))
      )
    ),
    e("div", { className: "logic-side-actions" },
      e("button", {
        className: "btn ghost",
        type: "button",
        onClick: () => onToggleNodeEnabled(selectedNode.nodeId)
      }, selectedNode.enabled === false ? "노드 활성" : "노드 비활성")
    ),
    e("div", { className: "logic-node-config-grid" },
      fieldGroups.map((group, index) => e("section", {
        key: `${group.title}-${index}`,
        className: "logic-node-config-group"
      },
      e("div", { className: "logic-node-config-group-head" },
        e("strong", null, group.title),
        group.description
          ? e("div", { className: "tiny" }, group.description)
          : null
      ),
      e("div", { className: "logic-node-config-group-body" },
        group.fields.map((fieldItem) => renderNodeField(
          e,
          fieldItem,
          selectedNode,
          draftGraph,
          onNodeConfigChange,
          onNodeBindingChange,
          logicInspectorCatalogs,
          logicPathBrowser,
          {
            onOpen: onOpenPathBrowser,
            onClose: onClosePathBrowser,
            onSelectRoot: onSelectPathBrowserRoot,
            onNavigate: onNavigatePathBrowser,
            onSelectValue: onSelectPathBrowserValue
          }
        ))
      )))
    ),
    extraConfigEntries.length > 0
      ? e("div", { className: "logic-extra-config-block" },
        e("strong", null, "추가 설정"),
        extraConfigEntries.map(([key, value]) => e("label", {
          key,
          className: "routine-field"
        },
        e("span", { className: "routine-field-label" }, key),
        e("textarea", {
          className: "input logic-inspector-textarea logic-config-textarea",
          rows: 4,
          value: value || "",
          onChange: (event) => onNodeConfigChange(key, event.target.value)
        })))
      )
      : null
  )
}

function renderSelectedEdgeCard(props) {
  const {
    e,
    draftGraph,
    selectedEdge
  } = props

  const sourceNode = (draftGraph?.nodes || []).find((item) => item.nodeId === selectedEdge.sourceNodeId)
  const targetNode = (draftGraph?.nodes || []).find((item) => item.nodeId === selectedEdge.targetNodeId)
  const sourceName = sourceNode?.title || getPaletteLibraryItem(sourceNode?.type)?.label || selectedEdge.sourceNodeId
  const targetName = targetNode?.title || getPaletteLibraryItem(targetNode?.type)?.label || selectedEdge.targetNodeId

  return e("div", { className: "routine-editor-card logic-inspector-card" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "routine-editor-title" }, "선택 연결"),
      e("div", { className: "routine-editor-subtitle" }, selectedEdge.edgeId)
    ),
    e("div", { className: "logic-node-doc-card" },
      e("div", { className: "tiny" }, `${sourceName} → ${targetName}`),
      e("div", { className: "tiny" }, `포트: ${getLogicPortDisplayLabel(sourceNode, "output", selectedEdge.sourcePort, draftGraph)} → ${getLogicPortDisplayLabel(targetNode, "input", selectedEdge.targetPort, draftGraph)}`),
      selectedEdge.condition
        ? e("div", { className: "tiny" }, `조건: ${JSON.stringify(selectedEdge.condition)}`)
        : e("div", { className: "tiny" }, "조건 없음")
    )
  )
}

function renderGraphSettingsCard(props) {
  const {
    e,
    draftGraph,
    dirty,
    lastMessage,
    activeRunId,
    onGraphFieldChange,
    onScheduleFieldChange,
    onRun,
    onCancelRun,
    onSave,
    onRefresh
  } = props

  return e("div", { className: "routine-editor-card logic-inspector-card" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "routine-editor-title" }, "흐름 설정"),
      e("div", { className: "routine-editor-subtitle" }, lastMessage || summarizeLogicGraph(draftGraph))
    ),
    e("div", { className: "logic-selected-node-summary" },
      e("div", { className: "tiny" }, "흐름 제목, 시작 입력, 예약 실행을 여기서 관리합니다."),
      e("span", { className: `pill ${dirty ? "warn" : "ok"}` }, dirty ? "저장 필요" : "저장됨")
    ),
    e("label", { className: "routine-field" },
      e("span", { className: "routine-field-label" }, "제목"),
      e("input", {
        className: "input",
        value: draftGraph?.title || "",
        onChange: (event) => onGraphFieldChange("title", event.target.value)
      })
    ),
    e("label", { className: "routine-field" },
      e("span", { className: "routine-field-label" }, "설명 / 시작 입력"),
      e("textarea", {
        className: "input logic-inspector-textarea",
        value: draftGraph?.description || "",
        onChange: (event) => onGraphFieldChange("description", event.target.value)
      })
    ),
    e("div", { className: "routine-form-grid routine-form-grid-tight" },
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "활성화"),
        e("select", {
          className: "input",
          value: draftGraph?.enabled === false ? "false" : "true",
          onChange: (event) => onGraphFieldChange("enabled", event.target.value === "true")
        },
        e("option", { value: "true" }, "활성"),
        e("option", { value: "false" }, "비활성"))
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "스케줄 사용"),
        e("select", {
          className: "input",
          value: draftGraph?.schedule?.enabled === true ? "true" : "false",
          onChange: (event) => onScheduleFieldChange("enabled", event.target.value === "true")
        },
        e("option", { value: "false" }, "수동 전용"),
        e("option", { value: "true" }, "스케줄 활성"))
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "주기"),
        e("select", {
          className: "input",
          value: draftGraph?.schedule?.scheduleKind || "daily",
          onChange: (event) => onScheduleFieldChange("scheduleKind", event.target.value)
        },
        e("option", { value: "daily" }, "매일"),
        e("option", { value: "weekly" }, "주간"),
        e("option", { value: "monthly" }, "월간"))
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "시간"),
        e("input", {
          className: "input",
          type: "time",
          value: draftGraph?.schedule?.scheduleTime || "08:00",
          onChange: (event) => onScheduleFieldChange("scheduleTime", event.target.value)
        })
      )
    ),
    e("div", { className: "logic-side-actions" },
      e("button", { className: "btn secondary", type: "button", onClick: onSave }, "저장"),
      e("button", { className: "btn primary", type: "button", onClick: onRun, disabled: !draftGraph?.graphId }, "실행"),
      activeRunId
        ? e("button", { className: "btn ghost", type: "button", onClick: () => onCancelRun(activeRunId) }, "취소")
        : null,
      e("button", { className: "btn ghost", type: "button", onClick: onRefresh }, "새로고침")
    )
  )
}

function renderNodeDocsCard(props) {
  const {
    e,
    selectedNode
  } = props

  const definition = getLogicNodeInspectorDefinition(selectedNode?.type)
  const paletteItem = getPaletteLibraryItem(selectedNode?.type)
  const nodeDisplayName = selectedNode?.title || paletteItem?.label || "선택 없음"

  return e("div", { className: "routine-editor-card logic-inspector-card" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "routine-editor-title" }, "노드 안내"),
      e("div", { className: "routine-editor-subtitle" }, selectedNode ? nodeDisplayName : "선택 없음")
    ),
    e("div", { className: "logic-node-doc-card" },
      e("div", { className: "tiny" }, definition?.description || "이 노드 설명이 아직 없습니다."),
      definition?.example
        ? e("div", { className: "tiny" }, `실제 예시: ${definition.example}`)
        : null,
      Array.isArray(definition?.outputs) && definition.outputs.length > 0
        ? e("div", { className: "logic-node-doc-list" },
          definition.outputs.map((item, index) => e("div", {
            key: `output-${index}`,
            className: "tiny logic-node-doc-item"
          }, item))
        )
        : e("div", { className: "tiny" }, "정의된 출력 설명이 없습니다."),
      renderNodeReferenceGuide(e)
    )
  )
}

function renderSelectionEmptyCard(e, selectedEdge) {
  return e("div", { className: "routine-editor-card logic-inspector-card" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "routine-editor-title" }, selectedEdge ? "연결 선택" : "선택 상태"),
      e("div", { className: "routine-editor-subtitle" }, selectedEdge ? "연결 정보 확인" : "노드를 선택하세요")
    ),
    e("div", { className: "logic-node-doc-card" },
      selectedEdge
        ? e("div", { className: "tiny" }, "연결 상세는 좌측 카드에서 확인할 수 있습니다.")
        : e("div", { className: "tiny" }, "캔버스 노드를 누르면 아래 인스펙터에 필요한 설정이 묶어서 나타납니다."),
      e("div", { className: "tiny" }, "포트를 끌어 연결하고, 저장한 흐름은 왼쪽 목록에서 바로 실행할 수 있습니다.")
    )
  )
}

function renderRunLogCard(props) {
  const {
    e,
    activeRunId,
    runSnapshot,
    runEvents
  } = props

  return e("div", { className: "routine-editor-card logic-inspector-card logic-inspector-log-card" },
    e("div", { className: "routine-editor-section-head" },
      e("div", { className: "routine-editor-title" }, "실행 로그"),
      e("div", { className: "routine-editor-subtitle" }, activeRunId || "대기 중")
    ),
    runSnapshot
      ? e("div", { className: "logic-run-summary" },
        e("span", { className: `pill ${getLogicStatusTone(runSnapshot.status || "-")}` }, getLogicStatusLabel(runSnapshot.status || "-")),
        e("span", { className: "tiny" }, runSnapshot.resultText || runSnapshot.error || "결과 대기 중")
      )
      : e("div", { className: "tiny" }, "아직 실행 기록이 없습니다."),
    e("div", { className: "logic-run-log-list" },
      (Array.isArray(runEvents) ? runEvents : []).slice(0, 24).map((event, index) => e("div", {
        key: `${event.runId || "run"}-${event.kind || "event"}-${index}`,
        className: "logic-run-log-item"
      },
      e("strong", null, getLogicEventLabel(event.kind || "event")),
      e("span", { className: "tiny" }, event.nodeId || "-"),
      e("div", { className: "tiny" }, event.message || "-")))
    )
  )
}

function renderInspector(props) {
  const {
    e,
    draftGraph,
    selectedNode,
    selectedEdge,
    activeRunId,
    runSnapshot,
    runEvents,
    dirty,
    lastMessage,
    logicInspectorCatalogs,
    logicPathBrowser,
    onGraphFieldChange,
    onScheduleFieldChange,
    onNodeFieldChange,
    onNodeConfigChange,
    onNodeBindingChange,
    onOpenPathBrowser,
    onClosePathBrowser,
    onSelectPathBrowserRoot,
    onNavigatePathBrowser,
    onSelectPathBrowserValue,
    onToggleNodeEnabled,
    onRun,
    onCancelRun,
    onSave,
    onRefresh
  } = props

  const mainCard = selectedNode
    ? renderSelectedNodeCard({
      e,
      draftGraph,
      selectedNode,
      onNodeFieldChange,
      onToggleNodeEnabled,
      onNodeConfigChange,
      onNodeBindingChange,
      logicInspectorCatalogs,
      logicPathBrowser,
      onOpenPathBrowser,
      onClosePathBrowser,
      onSelectPathBrowserRoot,
      onNavigatePathBrowser,
      onSelectPathBrowserValue
    })
    : selectedEdge
      ? renderSelectedEdgeCard({
        e,
        draftGraph,
        selectedEdge
      })
      : renderGraphSettingsCard({
        e,
        draftGraph,
        dirty,
        lastMessage,
        activeRunId,
        onGraphFieldChange,
        onScheduleFieldChange,
        onRun,
        onCancelRun,
        onSave,
        onRefresh
      })

  const sideCards = []
  if (selectedNode) {
    sideCards.push(renderNodeDocsCard({
      e,
      selectedNode
    }))
    sideCards.push(renderGraphSettingsCard({
      e,
      draftGraph,
      dirty,
      lastMessage,
      activeRunId,
      onGraphFieldChange,
      onScheduleFieldChange,
      onRun,
      onCancelRun,
      onSave,
      onRefresh
    }))
  } else if (selectedEdge) {
    sideCards.push(renderSelectionEmptyCard(e, selectedEdge))
    sideCards.push(renderGraphSettingsCard({
      e,
      draftGraph,
      dirty,
      lastMessage,
      activeRunId,
      onGraphFieldChange,
      onScheduleFieldChange,
      onRun,
      onCancelRun,
      onSave,
      onRefresh
    }))
  } else {
    sideCards.push(renderSelectionEmptyCard(e, null))
  }
  sideCards.push(renderRunLogCard({
    e,
    activeRunId,
    runSnapshot,
    runEvents
  }))

  return e("section", { className: "logic-pane logic-inspector-pane" },
    e("div", { className: "logic-pane-head" },
      e("div", null,
        e("div", { className: "routine-head-kicker" }, "인스펙터"),
        e("strong", null, selectedNode ? selectedNode.title : (selectedEdge ? "연결 상세" : "그래프 설정"))
      ),
      dirty
        ? e("span", { className: "pill warn" }, "저장 필요")
        : e("span", { className: "pill ok" }, "저장됨")
    ),
    e("div", { className: "logic-inspector-scroll" },
      e("div", { className: "logic-inspector-layout" },
        e("div", { className: "logic-inspector-main-column" }, mainCard),
        e("div", { className: "logic-inspector-side-column" }, sideCards)
      )
    )
  )
}

function getLogicWorkbenchStatus(props) {
  const {
    draftGraph,
    dirty,
    activeRunId,
    runSnapshot
  } = props
  if (activeRunId && runSnapshot) {
    return runSnapshot.status || "running"
  }
  if (!draftGraph?.graphId) {
    return dirty ? "unsaved" : "idle"
  }
  return dirty ? "unsaved" : "saved"
}

function buildLogicSectionItems(props) {
  const {
    graphs,
    draftGraph,
    selectedNode,
    selectedEdge,
    runEvents,
    jsonBuffer
  } = props
  const nodeCount = Array.isArray(draftGraph?.nodes) ? draftGraph.nodes.length : 0
  const edgeCount = Array.isArray(draftGraph?.edges) ? draftGraph.edges.length : 0
  const eventCount = Array.isArray(runEvents) ? runEvents.length : 0
  return [
    {
      key: "nodes",
      icon: "□",
      label: "노드",
      description: "노드 라이브러리",
      metric: `${LOGIC_NODE_LIBRARY.reduce((sum, group) => sum + group.items.length, 0)}`
    },
    {
      key: "run",
      icon: "▶",
      label: "실행",
      description: "실행 상태와 기록",
      metric: `${eventCount}`
    },
    {
      key: "json",
      icon: "{}",
      label: "JSON",
      description: "흐름 JSON 편집",
      metric: jsonBuffer?.trim() ? "편집" : `${nodeCount}/${edgeCount}`
    }
  ]
}

function renderLogicCommandBar(props, activeSection, onSectionChange) {
  const {
    e,
    draftGraph,
    dirty,
    activeRunId,
    runSnapshot,
    onCreateNewGraph,
    onSave,
    onRun
  } = props
  const nodes = Array.isArray(draftGraph?.nodes) ? draftGraph.nodes : []
  const edges = Array.isArray(draftGraph?.edges) ? draftGraph.edges : []
  const status = getLogicWorkbenchStatus(props)
  const statusText = activeRunId && runSnapshot
    ? `${activeRunId} · ${getLogicStatusLabel(runSnapshot.status || "running")}`
    : (dirty ? "저장 필요" : getLogicStatusLabel(status))

  return e("header", { className: "logic-command-bar" },
    e("div", { className: "logic-command-title" },
      e("button", {
        type: "button",
        className: "logic-command-section-button",
        onClick: () => onSectionChange("flow")
      }, "로직"),
      e("div", { className: "logic-command-divider" }),
      e("button", {
        type: "button",
        className: "logic-command-graph-button",
        onClick: () => onSectionChange(activeSection === "flow" ? "selection" : "flow")
      },
      e("span", null, draftGraph?.title || "새 작업 흐름"),
      e("span", { className: "logic-command-caret" }, "▾")),
      e("span", { className: `logic-command-status ${getLogicStatusTone(status)}` }, statusText)
    ),
    e("div", { className: "logic-command-meta" },
      e("span", null, `${nodes.length}개 노드`),
      e("span", null, `${edges.length}개 연결`),
      draftGraph?.graphId
        ? e("span", null, formatLogicGraphListId(draftGraph.graphId))
        : e("span", null, "초안")
    ),
    e("div", { className: "logic-command-actions" },
      e("button", {
        className: "btn ghost",
        type: "button",
        onClick: onCreateNewGraph
      }, "새 흐름"),
      e("button", {
        className: "btn secondary",
        type: "button",
        onClick: onSave
      }, "저장"),
      e("button", {
        className: "btn primary",
        type: "button",
        onClick: onRun,
        disabled: !draftGraph?.graphId
      }, "실행"))
  )
}

function renderLogicSectionNav(props, activeSection, onSectionChange) {
  const {
    e,
    graphs,
    selectedGraphId,
    draftGraph,
    paletteGroup,
    onSelectGraph,
    onRunGraph,
    onPaletteGroupChange
  } = props
  const currentGraphId = `${draftGraph?.graphId || selectedGraphId || ""}`.trim()
  const sections = buildLogicSectionItems(props).filter((item) => item.key !== "nodes")

  const renderSectionItem = (item) => e("button", {
    key: item.key,
    type: "button",
    className: `logic-section-nav-item ${activeSection === item.key ? "active" : ""}`,
    onClick: () => onSectionChange(item.key)
  },
  e("span", { className: "logic-section-icon" }, item.icon),
  e("span", { className: "logic-section-copy" },
    e("span", { className: "logic-section-label" }, item.label),
    e("span", { className: "logic-section-description" }, item.description)
  ),
  e("span", { className: "logic-section-metric" }, item.metric))

  return e("aside", { className: "logic-section-nav" },
    e("div", { className: "logic-section-block logic-section-category-block" },
      e("div", { className: "logic-section-block-head" },
        e("strong", null, "노드 카테고리"),
        e("span", { className: "tiny" }, "바로가기")
      ),
      e("div", { className: "logic-section-category-list" },
        LOGIC_NODE_LIBRARY.map((group) => {
          const meta = getLogicCategoryMeta(group.key)
          return e("button", {
            key: group.key,
            type: "button",
            className: `logic-section-category logic-section-category-${meta.className} ${paletteGroup === group.key ? "active" : ""}`,
            onClick: () => {
              onPaletteGroupChange(group.key)
              onSectionChange("nodes")
            }
          },
          e("span", null, group.label),
          e("span", null, `${group.items.length}`))
        }))
    ),
    e("div", { className: "logic-section-nav-list" },
      sections.map(renderSectionItem)
    ),
    e("div", { className: "logic-section-block" },
      e("div", { className: "logic-section-block-head" },
        e("strong", null, "저장된 흐름"),
        e("span", { className: "tiny" }, `${Array.isArray(graphs) ? graphs.length : 0}`)
      ),
      e("div", { className: "logic-section-flow-list" },
        Array.isArray(graphs) && graphs.length > 0
          ? graphs.map((item) => {
            const isActive = currentGraphId === item.graphId
            return e("div", {
              key: item.graphId,
              className: `logic-section-flow-row ${isActive ? "active" : ""}`
            },
            e("button", {
              type: "button",
              className: "logic-section-flow-main",
              onClick: () => {
                onSelectGraph(item.graphId)
                onSectionChange("flow")
              }
            },
            e("strong", null, item.title || item.graphId),
            e("span", { className: "tiny" }, `${item.nodeCount || 0}개 노드 · ${item.edgeCount || 0}개 연결`)),
            e("button", {
              type: "button",
              className: "btn ghost tiny-btn",
              onClick: () => onRunGraph(item.graphId),
              disabled: !item.graphId
            }, "실행"))
          })
          : e("div", { className: "logic-section-empty tiny" }, "저장된 흐름 없음"))
    )
  )
}

function renderFlowDetail(props) {
  const {
    e,
    graphs,
    selectedGraphId,
    draftGraph,
    dirty,
    activeRunId,
    runSnapshot,
    lastMessage,
    onSelectGraph,
    onCreateNewGraph,
    onDeleteGraph,
    onGraphFieldChange,
    onScheduleFieldChange,
    onRun,
    onRunGraph,
    onCancelRun,
    onSave,
    onRefresh
  } = props
  const summary = summarizeLogicGraph(draftGraph)
  const status = getLogicWorkbenchStatus(props)
  const currentGraphId = `${draftGraph?.graphId || selectedGraphId || ""}`.trim()

  return e("div", { className: "logic-detail-stack" },
    e("section", { className: "logic-detail-card" },
      e("div", { className: "logic-detail-section-head" },
        e("div", null,
          e("strong", null, "현재 흐름"),
          e("span", { className: "tiny" }, lastMessage || summary || "초안")
        ),
        e("span", { className: `pill ${getLogicStatusTone(status)}` }, getLogicStatusLabel(status))
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "제목"),
        e("input", {
          className: "input",
          value: draftGraph?.title || "",
          onChange: (event) => onGraphFieldChange("title", event.target.value)
        })
      ),
      e("label", { className: "routine-field" },
        e("span", { className: "routine-field-label" }, "설명 / 시작 입력"),
        e("textarea", {
          className: "input logic-inspector-textarea",
          value: draftGraph?.description || "",
          onChange: (event) => onGraphFieldChange("description", event.target.value)
        })
      ),
      e("div", { className: "routine-form-grid routine-form-grid-tight" },
        e("label", { className: "routine-field" },
          e("span", { className: "routine-field-label" }, "활성화"),
          e("select", {
            className: "input",
            value: draftGraph?.enabled === false ? "false" : "true",
            onChange: (event) => onGraphFieldChange("enabled", event.target.value === "true")
          },
          e("option", { value: "true" }, "활성"),
          e("option", { value: "false" }, "비활성"))
        ),
        e("label", { className: "routine-field" },
          e("span", { className: "routine-field-label" }, "스케줄"),
          e("select", {
            className: "input",
            value: draftGraph?.schedule?.enabled === true ? "true" : "false",
            onChange: (event) => onScheduleFieldChange("enabled", event.target.value === "true")
          },
          e("option", { value: "false" }, "수동"),
          e("option", { value: "true" }, "사용"))
        ),
        e("label", { className: "routine-field" },
          e("span", { className: "routine-field-label" }, "주기"),
          e("select", {
            className: "input",
            value: draftGraph?.schedule?.scheduleKind || "daily",
            onChange: (event) => onScheduleFieldChange("scheduleKind", event.target.value)
          },
          e("option", { value: "daily" }, "매일"),
          e("option", { value: "weekly" }, "주간"),
          e("option", { value: "monthly" }, "월간"))
        ),
        e("label", { className: "routine-field" },
          e("span", { className: "routine-field-label" }, "시간"),
          e("input", {
            className: "input",
            type: "time",
            value: draftGraph?.schedule?.scheduleTime || "08:00",
            onChange: (event) => onScheduleFieldChange("scheduleTime", event.target.value)
          })
        )
      ),
      e("div", { className: "logic-detail-actions" },
        e("button", { className: "btn secondary", type: "button", onClick: onSave }, "저장"),
        e("button", { className: "btn primary", type: "button", onClick: onRun, disabled: !draftGraph?.graphId }, "실행"),
        activeRunId
          ? e("button", { className: "btn ghost", type: "button", onClick: () => onCancelRun(activeRunId) }, "취소")
          : null,
        e("button", { className: "btn ghost", type: "button", onClick: onRefresh }, "새로고침"),
        e("button", {
          className: "btn danger",
          type: "button",
          onClick: () => onDeleteGraph(currentGraphId),
          disabled: !currentGraphId
        }, "삭제"))
    ),
    e("section", { className: "logic-detail-card" },
      e("div", { className: "logic-detail-section-head" },
        e("strong", null, "흐름 목록"),
        e("button", { className: "btn ghost tiny-btn", type: "button", onClick: onCreateNewGraph }, "새 흐름")
      ),
      e("div", { className: "logic-detail-list" },
        Array.isArray(graphs) && graphs.length > 0
          ? graphs.map((item) => {
            const isActive = currentGraphId === item.graphId
            return e("div", {
              key: item.graphId,
              className: `logic-detail-list-row ${isActive ? "active" : ""}`
            },
            e("button", {
              type: "button",
              className: "logic-detail-list-main",
              onClick: () => onSelectGraph(item.graphId)
            },
            e("strong", null, item.title || item.graphId),
            e("span", { className: "tiny" }, `${item.nodeCount || 0}개 노드 · ${item.edgeCount || 0}개 연결`)),
            e("button", {
              type: "button",
              className: "btn ghost tiny-btn",
              onClick: () => onRunGraph(item.graphId),
              disabled: !item.graphId
            }, "실행"))
          })
          : e("div", { className: "logic-detail-empty" }, "저장된 흐름이 없습니다.")
      ),
      activeRunId && runSnapshot
        ? e("div", { className: "logic-run-summary" },
          e("span", { className: `pill ${getLogicStatusTone(runSnapshot.status || "-")}` }, getLogicStatusLabel(runSnapshot.status || "-")),
          e("span", { className: "tiny" }, `실행 중: ${activeRunId}`))
        : null
    )
  )
}

function renderNodeLibraryDetail(props) {
  const {
    e,
    paletteGroup,
    onPaletteGroupChange,
    onSelectNodeType
  } = props
  const activeGroup = getPaletteLibraryGroup(paletteGroup)

  return e("div", { className: "logic-detail-stack" },
    e("section", { className: "logic-detail-card" },
      e("div", { className: "logic-detail-section-head" },
        e("div", null,
          e("strong", null, "노드 라이브러리"),
          e("span", { className: "tiny" }, activeGroup?.label || "흐름")
        ),
        e("span", { className: "pill idle" }, `${activeGroup?.items?.length || 0}`)
      ),
      e("div", { className: "logic-node-category-tabs" },
        LOGIC_NODE_LIBRARY.map((group) => {
          const meta = getLogicCategoryMeta(group.key)
          return e("button", {
            key: group.key,
            type: "button",
            className: `logic-node-category-tab logic-node-category-tab-${meta.className} ${activeGroup?.key === group.key ? "active" : ""}`,
            onClick: () => onPaletteGroupChange(group.key)
          }, group.label)
        })
      ),
      e("div", { className: "logic-node-library-list" },
        activeGroup.items.map(([type, label]) => {
          const definition = getLogicNodeInspectorDefinition(type)
          const meta = getLogicCategoryMeta(type)
          const themeVars = buildLogicThemeVars(type)
          return e("button", {
            key: type,
            type: "button",
            className: `logic-node-library-item logic-node-library-item-${meta.className}`,
            style: themeVars,
            onClick: () => onSelectNodeType(type)
          },
          e("span", {
            className: `logic-node-kind-badge logic-node-kind-badge-${meta.className}`,
            style: themeVars
          }, meta.label),
          e("span", { className: "logic-node-library-copy" },
            e("strong", null, label),
            e("span", null, truncateText(definition?.description || definition?.example || label, 72))
          ))
        }))
    )
  )
}

function renderSelectionDetail(props) {
  const {
    e,
    selectedNode,
    selectedEdge
  } = props
  if (selectedNode) {
    return e("div", { className: "logic-detail-stack" },
      renderSelectedNodeCard(props)
    )
  }
  if (selectedEdge) {
    return e("div", { className: "logic-detail-stack" },
      renderSelectedEdgeCard(props)
    )
  }
  return e("div", { className: "logic-detail-stack" },
    e("section", { className: "logic-detail-card logic-detail-empty-state" },
      e("strong", null, "선택 없음"),
      e("div", { className: "tiny" }, "캔버스에서 노드나 연결을 선택하면 상세 설정이 여기에 표시됩니다.")
    )
  )
}

function renderRunDetail(props) {
  const {
    e,
    draftGraph,
    activeRunId,
    runSnapshot,
    runEvents,
    onRun,
    onCancelRun,
    onOpenRunPreview,
    onRefresh
  } = props
  const events = Array.isArray(runEvents) ? runEvents : []
  const openRunPreview = typeof onOpenRunPreview === "function" ? onOpenRunPreview : null
  const runStatusContent = [
    `실행 ID: ${activeRunId || "-"}`,
    `상태: ${getLogicStatusLabel(runSnapshot?.status || "-")}`,
    `결과: ${runSnapshot?.resultText || "-"}`,
    `오류: ${runSnapshot?.error || "-"}`,
    "",
    JSON.stringify(runSnapshot || {}, null, 2)
  ].join("\n")

  return e("div", { className: "logic-detail-stack" },
    e("section", { className: "logic-detail-card" },
      e("div", { className: "logic-detail-section-head" },
        e("div", null,
          e("strong", null, "실행 상태"),
          e("span", { className: "tiny" }, activeRunId || "대기 중")
        ),
        runSnapshot
          ? e("span", { className: `pill ${getLogicStatusTone(runSnapshot.status || "-")}` }, getLogicStatusLabel(runSnapshot.status || "-"))
          : e("span", { className: "pill idle" }, "대기")
      ),
      e("button", {
        className: "logic-run-summary logic-run-preview-trigger",
        type: "button",
        onClick: () => openRunPreview?.({
          open: true,
          title: "실행 상태",
          content: runStatusContent
        })
      },
        e("span", { className: "tiny" }, runSnapshot?.resultText || runSnapshot?.error || "최근 실행 결과가 없습니다.")
      ),
      e("div", { className: "logic-detail-actions" },
        e("button", { className: "btn primary", type: "button", onClick: onRun, disabled: !draftGraph?.graphId }, "실행"),
        activeRunId
          ? e("button", { className: "btn ghost", type: "button", onClick: () => onCancelRun(activeRunId) }, "취소")
          : null,
        e("button", { className: "btn secondary", type: "button", onClick: onRefresh }, "새로고침"))
    ),
    e("section", { className: "logic-detail-card" },
      e("div", { className: "logic-detail-section-head" },
        e("strong", null, "실행 로그"),
        e("span", { className: "tiny" }, `${events.length}`)
      ),
      e("div", { className: "logic-run-log-list logic-detail-run-list" },
        events.length > 0
          ? events.slice(0, 36).map((event, index) => {
            const eventTitle = getLogicEventLabel(event.kind || "event")
            const eventContent = [
              `이벤트: ${eventTitle}`,
              `노드: ${event.nodeId || "-"}`,
              `메시지: ${event.message || "-"}`,
              `실행 ID: ${event.runId || activeRunId || "-"}`,
              "",
              JSON.stringify(event || {}, null, 2)
            ].join("\n")
            return e("button", {
            key: `${event.runId || "run"}-${event.kind || "event"}-${index}`,
            className: "logic-run-log-item logic-run-preview-trigger",
            type: "button",
            onClick: () => openRunPreview?.({
              open: true,
              title: `실행 로그 · ${eventTitle}`,
              content: eventContent
            })
          },
          e("strong", null, eventTitle),
          e("span", { className: "tiny" }, event.nodeId || "-"),
          e("div", { className: "tiny" }, event.message || "-"))
          })
          : e("div", { className: "logic-detail-empty" }, "실행 기록이 없습니다."))
    )
  )
}

function renderJsonDetail(props) {
  const {
    e,
    draftGraph,
    selectedGraphId,
    jsonBuffer,
    activeRunId,
    runSnapshot,
    onExportGraph,
    onJsonBufferChange,
    onImportGraph
  } = props
  return e("div", { className: "logic-detail-stack" },
    e("section", { className: "logic-detail-card" },
      e("div", { className: "logic-detail-section-head" },
        e("div", null,
          e("strong", null, "JSON"),
          e("span", { className: "tiny" }, draftGraph?.graphId || selectedGraphId || "초안")
        ),
        e("span", { className: "pill idle" }, draftGraph?.version || "logic.graph.v1")
      ),
      e("textarea", {
        className: "input logic-json-textarea logic-detail-json-textarea",
        value: jsonBuffer,
        placeholder: "logic.graph.v1 JSON을 붙여넣으면 이 작업 흐름으로 바뀝니다.",
        onChange: (event) => onJsonBufferChange(event.target.value)
      }),
      e("div", { className: "logic-detail-actions" },
        e("button", {
          className: "btn secondary",
          type: "button",
          onClick: onExportGraph,
          disabled: !draftGraph
        }, "JSON 내보내기"),
        e("button", {
          className: "btn primary",
          type: "button",
          onClick: onImportGraph,
          disabled: !jsonBuffer.trim()
        }, "JSON 불러오기"))
    ),
    activeRunId && runSnapshot
      ? e("section", { className: "logic-detail-card" },
        e("div", { className: "logic-run-summary" },
          e("span", { className: `pill ${getLogicStatusTone(runSnapshot.status || "-")}` }, getLogicStatusLabel(runSnapshot.status || "-")),
          e("span", { className: "tiny" }, `실행 중: ${activeRunId}`))
      )
      : null
  )
}

function getLogicDetailTitle(activeSection, selectedNode, selectedEdge) {
  if (activeSection === "flow") {
    return ["흐름", "작업 흐름 설정"]
  }
  if (activeSection === "nodes") {
    return ["노드", "노드 추가"]
  }
  if (activeSection === "run") {
    return ["실행", "상태와 기록"]
  }
  if (activeSection === "json") {
    return ["JSON", "가져오기 / 내보내기"]
  }
  if (selectedNode) {
    return ["선택", selectedNode.title || getPaletteLibraryItem(selectedNode.type)?.label || "선택 노드"]
  }
  if (selectedEdge) {
    return ["선택", "연결 상세"]
  }
  return ["선택", "선택 없음"]
}

function renderLogicDetailPanel(props, activeSection) {
  const {
    e,
    selectedNode,
    selectedEdge,
    dirty
  } = props
  const [kicker, title] = getLogicDetailTitle(activeSection, selectedNode, selectedEdge)
  let content = null
  if (activeSection === "flow") {
    content = renderFlowDetail(props)
  } else if (activeSection === "nodes") {
    content = renderNodeLibraryDetail(props)
  } else if (activeSection === "run") {
    content = renderRunDetail(props)
  } else if (activeSection === "json") {
    content = renderJsonDetail(props)
  } else {
    content = renderSelectionDetail(props)
  }

  return e("aside", { className: `logic-detail-pane logic-detail-pane-${activeSection}` },
    e("div", { className: "logic-detail-head" },
      e("div", null,
        e("div", { className: "routine-head-kicker" }, kicker),
        e("strong", null, title)
      ),
      dirty
        ? e("span", { className: "pill warn" }, "저장 필요")
        : e("span", { className: "pill ok" }, "저장됨")
    ),
    e("div", { className: "logic-detail-scroll" }, content)
  )
}

export function renderLogicTab(props) {
  const {
    e,
    isPortraitMobileLayout,
    currentLogicPane,
    renderResponsiveSectionTabs,
    setResponsivePane
  } = props
  const activeSection = normalizeLogicPaneKey(currentLogicPane, isPortraitMobileLayout)
  const detailSection = activeSection === "canvas" ? "selection" : activeSection
  const setLogicSection = (section) => setResponsivePane("logic", section)

  if (isPortraitMobileLayout) {
    const tabs = renderSectionTabs(e, renderResponsiveSectionTabs, currentLogicPane, setResponsivePane)
    return e("div", { className: "logic-tab-shell mobile" },
      tabs,
      activeSection === "canvas"
        ? e("div", { className: "logic-canvas-mobile-workspace" },
          renderCanvas({
            ...props,
            activeRunSnapshot: props.runSnapshot
          })
        )
        : renderLogicDetailPanel(props, detailSection)
    )
  }

  return e("div", { className: "logic-tab-shell" },
    renderLogicCommandBar(props, detailSection, setLogicSection),
    e("div", { className: "logic-workbench-grid" },
      renderLogicSectionNav(props, detailSection, setLogicSection),
      e("div", { className: "logic-canvas-workspace" },
        renderCanvas({
          ...props,
          activeRunSnapshot: props.runSnapshot
        })
      ),
      renderLogicDetailPanel(props, detailSection)
    )
  )
}
