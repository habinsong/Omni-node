function createProviderRuntimeSeed() {
  return {
    count: 0,
    successCount: 0,
    errorCount: 0,
    progressCount: 0,
    lastStatus: "-",
    lastScope: "-",
    lastMode: "-"
  };
}

export function buildToolResultStats(toolResultItems, toolResultGroups) {
  const byGroup = {};
  toolResultGroups.forEach((group) => {
    byGroup[group.key] = {
      count: 0,
      errorCount: 0,
      lastAction: "-",
      lastStatus: "-"
    };
  });

  let errors = 0;
  for (const item of toolResultItems) {
    if (item.hasError) {
      errors += 1;
    }

    const target = byGroup[item.group];
    if (!target) {
      continue;
    }

    if (target.count === 0) {
      target.lastAction = item.action || "-";
      target.lastStatus = item.statusLabel || "-";
    }
    target.count += 1;
    if (item.hasError) {
      target.errorCount += 1;
    }
  }

  return {
    total: toolResultItems.length,
    errors,
    byGroup
  };
}

export function buildProviderRuntimeStats(providerRuntimeItems, providerRuntimeKeys) {
  const byProvider = {};
  providerRuntimeKeys.forEach((provider) => {
    byProvider[provider] = createProviderRuntimeSeed();
  });
  if (!byProvider.unknown) {
    byProvider.unknown = createProviderRuntimeSeed();
  }

  for (const item of providerRuntimeItems) {
    const provider = byProvider[item.provider] ? item.provider : "unknown";
    const target = byProvider[provider];
    if (target.count === 0) {
      target.lastStatus = item.statusLabel || "-";
      target.lastScope = item.scope || "-";
      target.lastMode = item.mode || "-";
    }
    target.count += 1;
    if (item.hasError) {
      target.errorCount += 1;
    } else if (item.statusLabel === "progress") {
      target.progressCount += 1;
    } else {
      target.successCount += 1;
    }
  }

  const total = providerRuntimeItems.length;
  const errorCount = providerRuntimeItems.filter((item) => item.hasError).length;
  const progressCount = providerRuntimeItems.filter((item) => item.statusLabel === "progress").length;
  const successCount = total - errorCount - progressCount;
  const latest = total > 0 ? providerRuntimeItems[0] : null;
  return {
    total,
    errorCount,
    successCount,
    progressCount,
    latest,
    byProvider
  };
}

function createGuardChannelStat() {
  return {
    count: 0,
    blockedCount: 0,
    retryRequiredCount: 0,
    countLockUnsatisfiedCount: 0,
    citationValidationFailedCount: 0,
    citationMappingRetryCount: 0,
    citationMappingCount: 0,
    maxRetryAttempt: 0,
    maxRetryMaxAttempts: 0,
    lastRetryAction: "-",
    lastRetryReason: "-",
    lastRetryStopReason: "-"
  };
}

function toTopRows(entries) {
  return Object.entries(entries)
    .sort((a, b) => {
      if (b[1] !== a[1]) {
        return b[1] - a[1];
      }
      return a[0].localeCompare(b[0]);
    })
    .slice(0, 6)
    .map(([name, count]) => ({ name, count }));
}

export function buildGuardObsStats({
  guardObsItems,
  guardObsChannelKeys,
  guardAlertRules,
  buildGuardAlertRuleResult
}) {
  const byChannel = {};
  guardObsChannelKeys.forEach((channel) => {
    byChannel[channel] = createGuardChannelStat();
  });
  if (!byChannel.other) {
    byChannel.other = createGuardChannelStat();
  }

  const retryActionCounts = {};
  const retryReasonCounts = {};
  const retryStopReasonCounts = {};

  let blockedTotal = 0;
  let retryRequiredTotal = 0;
  let countLockUnsatisfiedTotal = 0;
  let citationValidationFailedTotal = 0;
  let citationMappingRetryTotal = 0;
  let citationMappingCountTotal = 0;

  for (const item of guardObsItems) {
    const channel = byChannel[item.channel] ? item.channel : "other";
    const target = byChannel[channel];
    target.count += 1;

    if (item.hasGuardFailure) {
      target.blockedCount += 1;
      blockedTotal += 1;
    }
    if (item.retryRequired) {
      target.retryRequiredCount += 1;
      retryRequiredTotal += 1;
    }
    if (item.hasCountLockUnsatisfied) {
      target.countLockUnsatisfiedCount += 1;
      countLockUnsatisfiedTotal += 1;
    }
    if (item.hasCitationValidationFailure) {
      target.citationValidationFailedCount += 1;
      citationValidationFailedTotal += 1;
    }
    if (item.isCitationMappingRetry) {
      target.citationMappingRetryCount += 1;
      citationMappingRetryTotal += 1;
    }

    if (item.citationMappingCount > 0) {
      target.citationMappingCount += item.citationMappingCount;
      citationMappingCountTotal += item.citationMappingCount;
    }

    target.maxRetryAttempt = Math.max(target.maxRetryAttempt, item.retryAttempt);
    target.maxRetryMaxAttempts = Math.max(target.maxRetryMaxAttempts, item.retryMaxAttempts);

    if (target.lastRetryAction === "-" && item.retryAction !== "-") {
      target.lastRetryAction = item.retryAction;
    }
    if (target.lastRetryReason === "-" && item.retryReason !== "-") {
      target.lastRetryReason = item.retryReason;
    }
    if (target.lastRetryStopReason === "-" && item.retryStopReason !== "-") {
      target.lastRetryStopReason = item.retryStopReason;
    }

    if (item.retryAction !== "-") {
      retryActionCounts[item.retryAction] = (retryActionCounts[item.retryAction] || 0) + 1;
    }
    if (item.retryReason !== "-") {
      retryReasonCounts[item.retryReason] = (retryReasonCounts[item.retryReason] || 0) + 1;
    }
    if (item.retryStopReason !== "-") {
      retryStopReasonCounts[item.retryStopReason] = (retryStopReasonCounts[item.retryStopReason] || 0) + 1;
    }
  }

  const telegramGuardMetaBlockedTotal = byChannel.telegram ? byChannel.telegram.blockedCount : 0;
  const metrics = {
    total: guardObsItems.length,
    blockedTotal,
    retryRequiredTotal,
    countLockUnsatisfiedTotal,
    citationValidationFailedTotal,
    telegramGuardMetaBlockedTotal
  };

  return {
    total: guardObsItems.length,
    blockedTotal,
    retryRequiredTotal,
    countLockUnsatisfiedTotal,
    citationValidationFailedTotal,
    citationMappingRetryTotal,
    citationMappingCountTotal,
    telegramGuardMetaBlockedTotal,
    byChannel,
    topRetryActions: toTopRows(retryActionCounts),
    topRetryReasons: toTopRows(retryReasonCounts),
    topRetryStopReasons: toTopRows(retryStopReasonCounts),
    guardAlertRows: guardAlertRules.map((rule) => buildGuardAlertRuleResult(rule, metrics))
  };
}

export function buildGuardRetryTimelineRows(guardRetryTimeline, normalizeGuardNumber) {
  const rows = [];
  if (!guardRetryTimeline || !Array.isArray(guardRetryTimeline.channels)) {
    return rows;
  }

  guardRetryTimeline.channels.forEach((channelRow) => {
    const channel = `${channelRow && channelRow.channel ? channelRow.channel : "-"}`;
    if (!Array.isArray(channelRow && channelRow.buckets)) {
      return;
    }

    channelRow.buckets.forEach((bucket) => {
      rows.push({
        channel,
        bucketStartUtc: `${bucket && bucket.bucketStartUtc ? bucket.bucketStartUtc : "-"}`,
        samples: normalizeGuardNumber(bucket && bucket.samples),
        retryRequiredCount: normalizeGuardNumber(bucket && bucket.retryRequiredCount),
        maxRetryAttempt: normalizeGuardNumber(bucket && bucket.maxRetryAttempt),
        maxRetryMaxAttempts: normalizeGuardNumber(bucket && bucket.maxRetryMaxAttempts),
        topRetryStopReason: `${bucket && bucket.topRetryStopReason ? bucket.topRetryStopReason : "-"}`,
        uniqueRetryStopReasons: normalizeGuardNumber(bucket && bucket.uniqueRetryStopReasons)
      });
    });
  });

  rows.sort((a, b) => {
    if (a.bucketStartUtc !== b.bucketStartUtc) {
      return a.bucketStartUtc < b.bucketStartUtc ? 1 : -1;
    }
    return a.channel.localeCompare(b.channel);
  });
  return rows;
}

export function buildGuardAlertSummary(guardAlertRows, severityRank) {
  if (!Array.isArray(guardAlertRows) || guardAlertRows.length === 0) {
    return {
      statusLabel: "sample_pending",
      statusTone: "neutral",
      triggeredCount: 0,
      samplePendingCount: 0
    };
  }

  let worstSeverity = "neutral";
  let triggeredCount = 0;
  let samplePendingCount = 0;

  guardAlertRows.forEach((row) => {
    if (row.statusLabel === "warn" || row.statusLabel === "critical") {
      triggeredCount += 1;
    }
    if (row.statusLabel === "sample_pending") {
      samplePendingCount += 1;
    }
    if (severityRank(row.severity) > severityRank(worstSeverity)) {
      worstSeverity = row.severity;
    }
  });

  if (worstSeverity === "critical") {
    return { statusLabel: "critical", statusTone: "error", triggeredCount, samplePendingCount };
  }
  if (worstSeverity === "warn") {
    return { statusLabel: "warn", statusTone: "warn", triggeredCount, samplePendingCount };
  }
  if (worstSeverity === "ok") {
    return { statusLabel: "ok", statusTone: "ok", triggeredCount, samplePendingCount };
  }
  return { statusLabel: "sample_pending", statusTone: "neutral", triggeredCount, samplePendingCount };
}

export function buildProviderHealthRows({ settingsState, copilotStatus, codexStatus }) {
  const copilotText = (copilotStatus || "").trim();
  const copilotReady = copilotText.startsWith("설치/인증 완료");
  const copilotAuthRequired = copilotText.includes("미인증");
  const copilotMissing = copilotText === "미설치";
  const codexText = (codexStatus || "").trim();
  const codexReady = codexText.startsWith("설치/인증 완료");
  const codexAuthRequired = codexText.includes("미인증");
  const codexMissing = codexText === "미설치";
  return [
    {
      provider: "groq",
      statusLabel: settingsState.groqApiKeySet ? "ready" : "api_key_missing",
      statusTone: settingsState.groqApiKeySet ? "ok" : "error",
      ready: !!settingsState.groqApiKeySet,
      reason: settingsState.groqApiKeySet ? "configured" : "Groq API 키 필요"
    },
    {
      provider: "gemini",
      statusLabel: settingsState.geminiApiKeySet ? "ready" : "api_key_missing",
      statusTone: settingsState.geminiApiKeySet ? "ok" : "error",
      ready: !!settingsState.geminiApiKeySet,
      reason: settingsState.geminiApiKeySet ? "configured" : "Gemini API 키 필요"
    },
    {
      provider: "cerebras",
      statusLabel: settingsState.cerebrasApiKeySet ? "ready" : "api_key_missing",
      statusTone: settingsState.cerebrasApiKeySet ? "ok" : "error",
      ready: !!settingsState.cerebrasApiKeySet,
      reason: settingsState.cerebrasApiKeySet ? "configured" : "Cerebras API 키 필요"
    },
    {
      provider: "nvidia",
      statusLabel: settingsState.nvidiaApiKeySet ? "ready" : "api_key_missing",
      statusTone: settingsState.nvidiaApiKeySet ? "ok" : "error",
      ready: !!settingsState.nvidiaApiKeySet,
      reason: settingsState.nvidiaApiKeySet ? "configured" : "NVIDIA NIM API 키 필요"
    },
    {
      provider: "copilot",
      statusLabel: copilotReady ? "ready" : (copilotAuthRequired ? "auth_required" : (copilotMissing ? "not_installed" : "unavailable")),
      statusTone: copilotReady ? "ok" : (copilotAuthRequired ? "warn" : "error"),
      ready: copilotReady,
      reason: copilotReady
        ? "설치/인증 완료"
        : (copilotAuthRequired
          ? "GitHub 인증 필요"
          : (copilotMissing ? "Copilot 미설치" : (copilotText || "상태 확인 필요")))
    },
    {
      provider: "codex",
      statusLabel: codexReady ? "ready" : (codexAuthRequired ? "auth_required" : (codexMissing ? "not_installed" : "unavailable")),
      statusTone: codexReady ? "ok" : (codexAuthRequired ? "warn" : "error"),
      ready: codexReady,
      reason: codexReady
        ? "설치/인증 완료"
        : (codexAuthRequired
          ? "Codex OAuth 또는 API Key 필요"
          : (codexMissing ? "Codex 미설치" : (codexText || "상태 확인 필요")))
    }
  ];
}

export function buildProviderRuntimeRows(providerHealthRows, providerRuntimeStats) {
  return providerHealthRows.map((row) => {
    const runtime = providerRuntimeStats.byProvider[row.provider] || {
      count: 0,
      successCount: 0,
      errorCount: 0,
      progressCount: 0
    };
    return {
      ...row,
      runtimeCount: runtime.count,
      runtimeSuccessCount: runtime.successCount,
      runtimeErrorCount: runtime.errorCount,
      runtimeProgressCount: runtime.progressCount
    };
  });
}

export function buildProviderHealthSummary(providerHealthRows, providerRuntimeStats) {
  const total = providerHealthRows.length;
  const readyCount = providerHealthRows.filter((row) => row.ready).length;
  const setupErrorCount = total - readyCount;
  const runtimeErrorCount = providerRuntimeStats.errorCount;
  const latest = providerRuntimeStats.latest;
  const latestText = latest
    ? `${latest.provider}:${latest.statusLabel}:${latest.scope}/${latest.mode}`
    : "-";
  const lastStatus = [
    providerHealthRows.map((row) => `${row.provider}:${row.statusLabel}`).join(", "),
    `runtime_latest=${latestText}`
  ].join(" | ");
  return {
    count: total,
    setupErrorCount,
    runtimeErrorCount,
    runtimeTotal: providerRuntimeStats.total,
    runtimeSuccessCount: providerRuntimeStats.successCount,
    runtimeProgressCount: providerRuntimeStats.progressCount,
    lastStatus,
    mainLabel: `${readyCount}/${total} ready · 실행 ${providerRuntimeStats.total}건`
  };
}

export function buildToolDomainStats(toolResultItems) {
  const byDomain = {
    tool: { count: 0, errorCount: 0, lastType: "-", lastStatus: "-" },
    rag: { count: 0, errorCount: 0, lastType: "-", lastStatus: "-" }
  };

  for (const item of toolResultItems) {
    const domain = item.domain === "rag" ? "rag" : "tool";
    const target = byDomain[domain];
    if (target.count === 0) {
      target.lastType = item.type || "-";
      target.lastStatus = item.statusLabel || "-";
    }
    target.count += 1;
    if (item.hasError) {
      target.errorCount += 1;
    }
  }

  return byDomain;
}

export function buildOpsFlowItems(providerRuntimeItems, toolResultItems) {
  const providerItems = providerRuntimeItems.map((item) => ({
    id: `provider-${item.id || `${item.capturedAt || ""}-${item.provider || "unknown"}`}`,
    capturedAt: item.capturedAt || "",
    domain: "provider",
    source: item.scope || "runtime",
    statusLabel: item.statusLabel || "-",
    statusTone: item.statusTone || "neutral",
    hasError: !!item.hasError,
    summary: item.summary || `${item.provider || "unknown"} ${item.statusLabel || "-"}`
  }));
  const toolItems = toolResultItems.map((item) => ({
    id: `tool-${item.id || `${item.capturedAt || ""}-${item.type || "unknown"}`}`,
    capturedAt: item.capturedAt || "",
    domain: item.domain === "rag" ? "rag" : "tool",
    source: item.group || "tool",
    statusLabel: item.statusLabel || "-",
    statusTone: item.statusTone || "neutral",
    hasError: !!item.hasError,
    summary: item.summary || "-"
  }));
  return [...providerItems, ...toolItems]
    .sort((a, b) => (b.capturedAt || "").localeCompare(a.capturedAt || ""))
    .slice(0, 64);
}

export function buildOpsDomainStats(opsFlowItems) {
  const stats = {
    all: { count: 0, errorCount: 0, lastSummary: "-" },
    provider: { count: 0, errorCount: 0, lastSummary: "-" },
    tool: { count: 0, errorCount: 0, lastSummary: "-" },
    rag: { count: 0, errorCount: 0, lastSummary: "-" }
  };

  for (const item of opsFlowItems) {
    const domain = item.domain === "provider" || item.domain === "rag" ? item.domain : "tool";
    const allTarget = stats.all;
    if (allTarget.count === 0) {
      allTarget.lastSummary = item.summary || "-";
    }
    allTarget.count += 1;
    if (item.hasError) {
      allTarget.errorCount += 1;
    }

    const target = stats[domain];
    if (target.count === 0) {
      target.lastSummary = item.summary || "-";
    }
    target.count += 1;
    if (item.hasError) {
      target.errorCount += 1;
    }
  }

  return stats;
}

export function filterOpsFlowItems(opsFlowItems, opsDomainFilter) {
  return opsFlowItems.filter((item) => {
    if (opsDomainFilter === "all") {
      return true;
    }
    return item.domain === opsDomainFilter;
  });
}

export function filterToolResultItems(toolResultItems, toolResultFilter, toolDomainFilter) {
  return toolResultItems.filter((item) => {
    if (toolResultFilter === "errors") {
      if (!item.hasError) {
        return false;
      }
    } else if (toolResultFilter !== "all" && item.group !== toolResultFilter) {
      return false;
    }

    if (toolDomainFilter !== "all" && item.domain !== toolDomainFilter) {
      return false;
    }
    return true;
  });
}
