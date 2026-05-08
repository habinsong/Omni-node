export const MULTI_COMPARE_PREFIX = "[[OMNI_MULTI_COMPARE_JSON]]";
export const CODING_MULTI_COMPARE_PREFIX = "[[OMNI_CODING_MULTI_COMPARE_JSON]]";

function toText(value) {
  if (typeof value === "string") {
    return value;
  }

  if (value === null || value === undefined) {
    return "";
  }

  return `${value}`;
}

function toMetaText(value) {
  return toText(value).trim();
}

export function normalizeChatMultiResultMessage(message) {
  const msg = message && typeof message === "object" ? message : {};
  return {
    groq: toText(msg.groq),
    gemini: toText(msg.gemini),
    cerebras: toText(msg.cerebras),
    nvidia: toText(msg.nvidia),
    copilot: toText(msg.copilot),
    codex: toText(msg.codex),
    summary: toText(msg.summary),
    commonSummary: toText(msg.commonSummary || msg.summary),
    commonCore: toText(msg.commonCore),
    differences: toText(msg.differences),
    groqModel: toMetaText(msg.groqModel),
    geminiModel: toMetaText(msg.geminiModel),
    cerebrasModel: toMetaText(msg.cerebrasModel),
    nvidiaModel: toMetaText(msg.nvidiaModel),
    copilotModel: toMetaText(msg.copilotModel),
    codexModel: toMetaText(msg.codexModel),
    requestedSummaryProvider: toMetaText(msg.requestedSummaryProvider),
    resolvedSummaryProvider: toMetaText(msg.resolvedSummaryProvider)
  };
}

function toProviderDisplayName(providerName) {
  const normalized = toMetaText(providerName).toLowerCase();
  if (normalized === "groq") {
    return "Groq";
  }
  if (normalized === "gemini") {
    return "Gemini";
  }
  if (normalized === "cerebras") {
    return "Cerebras";
  }
  if (normalized === "nvidia") {
    return "NVIDIA NIM";
  }
  if (normalized === "copilot") {
    return "Copilot";
  }
  if (normalized === "codex") {
    return "Codex";
  }
  return normalized ? normalized : "요약";
}

function formatModelDisplayName(providerName, modelName) {
  const provider = toMetaText(providerName).toLowerCase();
  const model = toMetaText(modelName);
  if (!model) {
    return "";
  }

  if (provider === "cerebras" && model === "zai-glm-4.7") {
    return "zai-glm-4.7 (preview)";
  }

  return model;
}

function buildProviderLabel(providerName, modelName) {
  const model = formatModelDisplayName(providerName, modelName);
  return model ? `${providerName} (${model})` : providerName;
}

function buildSummaryLabel(requestedProvider, resolvedProvider) {
  const requested = toMetaText(requestedProvider);
  const resolved = toMetaText(resolvedProvider);
  if (!requested && !resolved) {
    return "요약";
  }

  return `요약 (요청=${requested || "-"}, 실제=${resolved || "-"})`;
}

export function buildChatMultiDisplayLabels(value) {
  const result = normalizeChatMultiResultMessage(value);
  const resolvedProvider = toProviderDisplayName(result.resolvedSummaryProvider);
  const resolvedSummaryModel = (() => {
    const normalized = toMetaText(result.resolvedSummaryProvider).toLowerCase();
    if (normalized === "groq") {
      return result.groqModel;
    }
    if (normalized === "gemini") {
      return result.geminiModel;
    }
    if (normalized === "cerebras") {
      return result.cerebrasModel;
    }
    if (normalized === "nvidia") {
      return result.nvidiaModel;
    }
    if (normalized === "copilot") {
      return result.copilotModel;
    }
    if (normalized === "codex") {
      return result.codexModel;
    }
    return "";
  })();
  return {
    groqLabel: buildProviderLabel("Groq", result.groqModel),
    geminiLabel: buildProviderLabel("Gemini", result.geminiModel),
    cerebrasLabel: buildProviderLabel("Cerebras", result.cerebrasModel),
    nvidiaLabel: buildProviderLabel("NVIDIA NIM", result.nvidiaModel),
    copilotLabel: buildProviderLabel("Copilot", result.copilotModel),
    codexLabel: buildProviderLabel("Codex", result.codexModel),
    summaryLabel: buildSummaryLabel(result.requestedSummaryProvider, result.resolvedSummaryProvider),
    summaryModelLabel: buildProviderLabel(`${resolvedProvider} 요약`, resolvedSummaryModel)
  };
}

function hasFailureCue(text) {
  return /(error|fail|timeout|denied|invalid|unavailable|unsupported|exception|오류|실패|미지원|중단|quota|인증 필요)/i.test(toText(text));
}

function shouldIncludeComparisonEntry(model, body) {
  const normalizedModel = toMetaText(model).toLowerCase();
  const normalizedBody = toMetaText(body);
  if (normalizedModel === "none" && (!normalizedBody || normalizedBody === "선택 안함")) {
    return false;
  }

  return !!normalizedModel || !!normalizedBody;
}

export function normalizeSummarySections(value) {
  const normalized = normalizeChatMultiResultMessage(value);
  return [
    {
      key: "commonSummary",
      title: "공통 요약",
      body: toText(normalized.commonSummary || normalized.summary).trim() || "공통 요약이 없습니다."
    },
    {
      key: "commonCore",
      title: "공통 핵심",
      body: toText(normalized.commonCore).trim() || "공통점 없음"
    },
    {
      key: "differences",
      title: "부분 차이",
      body: toText(normalized.differences).trim() || "의미 있는 차이 없음"
    }
  ];
}

export function buildChatMultiCarouselEntries(value) {
  const normalized = normalizeChatMultiResultMessage(value);
  const labels = buildChatMultiDisplayLabels(normalized);
  const entries = [];

  [
    { provider: "groq", heading: labels.groqLabel, body: normalized.groq, model: normalized.groqModel },
    { provider: "gemini", heading: labels.geminiLabel, body: normalized.gemini, model: normalized.geminiModel },
    { provider: "cerebras", heading: labels.cerebrasLabel, body: normalized.cerebras, model: normalized.cerebrasModel },
    { provider: "nvidia", heading: labels.nvidiaLabel, body: normalized.nvidia, model: normalized.nvidiaModel },
    { provider: "copilot", heading: labels.copilotLabel, body: normalized.copilot, model: normalized.copilotModel },
    { provider: "codex", heading: labels.codexLabel, body: normalized.codex, model: normalized.codexModel }
  ].forEach((entry) => {
    if (!shouldIncludeComparisonEntry(entry.model, entry.body)) {
      return;
    }
    entries.push({
      provider: entry.provider,
      heading: entry.heading,
      meta: `다중 LLM · ${entry.provider}`,
      body: entry.body || "-",
      tone: hasFailureCue(entry.body) ? "error" : "ok"
    });
  });

  return entries;
}

export function parseChatMultiComparisonMessage(text) {
  const raw = toText(text);
  if (!raw.startsWith(MULTI_COMPARE_PREFIX)) {
    return null;
  }

  const payloadText = raw.slice(MULTI_COMPARE_PREFIX.length).trim();
  if (!payloadText) {
    return null;
  }

  try {
    const parsed = JSON.parse(payloadText);
    const entries = Array.isArray(parsed && parsed.entries) ? parsed.entries : [];
    const normalizedEntries = entries
      .map((entry) => ({
        provider: toMetaText(entry && entry.provider).toLowerCase(),
        model: toMetaText(entry && entry.model),
        text: toText(entry && entry.text)
      }))
      .filter((entry) => entry.provider && shouldIncludeComparisonEntry(entry.model, entry.text))
      .map((entry) => ({
        provider: entry.provider,
        heading: buildProviderLabel(toProviderDisplayName(entry.provider), entry.model),
        meta: `다중 LLM · ${entry.provider}`,
        body: entry.text || "-",
        tone: hasFailureCue(entry.text) ? "error" : "ok"
      }));

    return { entries: normalizedEntries };
  } catch (_err) {
    return null;
  }
}

export function buildChatMultiRenderSnapshot(value) {
  const normalized = normalizeChatMultiResultMessage(value);
  const labels = buildChatMultiDisplayLabels(normalized);
  const entries = buildChatMultiCarouselEntries(normalized);
  const summarySections = normalizeSummarySections(normalized);
  return {
    normalized,
    labels,
    entries,
    summarySections,
    sections: [
      ...entries.map((entry) => ({
        provider: entry.provider,
        heading: entry.heading,
        body: entry.body
      })),
      ...summarySections.map((section) => ({
        provider: section.key,
        heading: section.title,
        body: section.body
      }))
    ]
  };
}

function normalizeCodingWorker(worker) {
  const execution = worker && typeof worker.execution === "object" ? worker.execution : {};
  return {
    provider: toMetaText(worker && worker.provider).toLowerCase(),
    model: toMetaText(worker && worker.model),
    role: toMetaText(worker && worker.role),
    language: toMetaText(worker && worker.language),
    summary: toText(worker && worker.summary),
    changedFiles: Array.isArray(worker && worker.changedFiles) ? worker.changedFiles.filter(Boolean).map(toText) : [],
    execution: {
      status: toMetaText(execution && execution.status),
      exitCode: Number.isFinite(Number(execution && execution.exitCode)) ? Number(execution.exitCode) : null,
      command: toText(execution && execution.command),
      runDirectory: toText(execution && execution.runDirectory),
      stdout: toText(execution && execution.stdout),
      stderr: toText(execution && execution.stderr)
    }
  };
}

export function normalizeCodingMultiResultMessage(message) {
  const msg = message && typeof message === "object" ? message : {};
  return {
    summary: toText(msg.summary),
    commonSummary: toText(msg.commonSummary || msg.summary),
    commonPoints: toText(msg.commonPoints || msg.commonCore),
    differences: toText(msg.differences),
    recommendation: toText(msg.recommendation),
    workers: Array.isArray(msg.workers) ? msg.workers.map(normalizeCodingWorker).filter((worker) => worker.provider || worker.model || worker.summary) : []
  };
}

function buildCodingWorkerBody(worker) {
  const lines = [];
  if (worker.role) {
    lines.push(`역할: ${worker.role}`);
  }
  lines.push(`상태: ${worker.execution.status || "-"}`);
  lines.push(`종료 코드: ${worker.execution.exitCode === null ? "-" : worker.execution.exitCode}`);
  if (worker.language) {
    lines.push(`언어: ${worker.language}`);
  }
  lines.push(`변경 파일: ${worker.changedFiles.length}개`);
  const command = toText(worker.execution.command).trim();
  if (command && command !== "(none)" && command !== "-") {
    lines.push(`실행 명령: \`${command}\``);
  }
  if (worker.changedFiles.length > 0) {
    lines.push(`파일: ${worker.changedFiles.join(", ")}`);
  }
  const summary = toText(worker.summary).trim();
  if (summary) {
    lines.push(`요약:\n${summary}`);
  }
  return lines.join("\n\n");
}

export function buildCodingMultiCarouselEntries(value) {
  const normalized = normalizeCodingMultiResultMessage(value);
  return normalized.workers
    .filter((worker) => shouldIncludeComparisonEntry(worker.model, worker.summary || worker.provider))
    .map((worker) => ({
      provider: worker.provider,
      heading: buildProviderLabel(toProviderDisplayName(worker.provider), worker.model),
      meta: `다중 코딩 · ${worker.role || "독립 완주"} · status=${worker.execution.status || "-"} · exit=${worker.execution.exitCode === null ? "-" : worker.execution.exitCode}`,
      body: buildCodingWorkerBody(worker) || "-",
      tone: hasFailureCue(`${worker.execution.status} ${worker.summary}`) ? "error" : "ok"
    }));
}

export function normalizeCodingSummarySections(value) {
  const normalized = normalizeCodingMultiResultMessage(value);
  return [
    {
      key: "commonSummary",
      title: "공통 요약",
      body: toText(normalized.commonSummary || normalized.summary).trim() || "공통 요약이 없습니다."
    },
    {
      key: "commonPoints",
      title: "공통점",
      body: toText(normalized.commonPoints).trim() || "공통점 없음"
    },
    {
      key: "differences",
      title: "차이",
      body: toText(normalized.differences).trim() || "의미 있는 차이 없음"
    },
    {
      key: "recommendation",
      title: "추천",
      body: toText(normalized.recommendation).trim() || "추천 정리가 없습니다."
    }
  ];
}

export function parseCodingMultiComparisonMessage(text) {
  const raw = toText(text);
  if (!raw.startsWith(CODING_MULTI_COMPARE_PREFIX)) {
    return null;
  }

  const payloadText = raw.slice(CODING_MULTI_COMPARE_PREFIX.length).trim();
  if (!payloadText) {
    return null;
  }

  try {
    const parsed = JSON.parse(payloadText);
    const entries = Array.isArray(parsed && parsed.entries) ? parsed.entries : [];
    const normalizedEntries = entries
      .map((entry) => ({
        provider: toMetaText(entry && entry.provider).toLowerCase(),
        model: toMetaText(entry && entry.model),
        role: toMetaText(entry && entry.role),
        text: toText(entry && entry.text)
      }))
      .filter((entry) => entry.provider && shouldIncludeComparisonEntry(entry.model, entry.text))
      .map((entry) => ({
        provider: entry.provider,
        heading: buildProviderLabel(toProviderDisplayName(entry.provider), entry.model),
        meta: `다중 코딩 · ${entry.role || "독립 완주"}`,
        body: entry.text || "-",
        tone: hasFailureCue(entry.text) ? "error" : "ok"
      }));

    return { entries: normalizedEntries };
  } catch (_err) {
    return null;
  }
}

export function buildCodingMultiRenderSnapshot(value) {
  const normalized = normalizeCodingMultiResultMessage(value);
  return {
    normalized,
    entries: buildCodingMultiCarouselEntries(normalized),
    summarySections: normalizeCodingSummarySections(normalized)
  };
}
