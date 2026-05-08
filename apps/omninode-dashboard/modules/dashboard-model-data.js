function toProviderLabel(provider) {
  const normalized = `${provider || ""}`.trim().toLowerCase();
  if (normalized === "codex") {
    return "Codex";
  }
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
  return provider || "-";
}

export function buildDashboardModelOptionSets({
  e,
  groqModels,
  copilotModels,
  codeXModelChoices,
  geminiModelChoices,
  nvidiaModelChoices,
  noneModel,
  defaultGroqWorkerModel,
  defaultNvidiaModel,
  defaultRoutineAgentProvider,
  defaultRoutineAgentModel
}) {
  const groqModelOptions = groqModels.map((item) => e("option", { key: item.id, value: item.id }, item.id));
  const copilotModelOptions = copilotModels.map((item) => e("option", { key: item.id, value: item.id }, item.id));
  const codexModelOptions = codeXModelChoices.map((item) =>
    e("option", { key: `codex-${item.id}`, value: item.id }, item.label)
  );
  const geminiModelOptions = geminiModelChoices.map((item) =>
    e("option", { key: `gemini-${item.id}`, value: item.id }, item.label)
  );
  const nvidiaModelOptions = nvidiaModelChoices.map((item) =>
    e("option", { key: `nvidia-${item.id}`, value: item.id }, item.label)
  );
  const routineAgentProviderOptions = [
    e(
      "option",
      { key: `routine-agent-provider-${defaultRoutineAgentProvider}`, value: defaultRoutineAgentProvider },
      toProviderLabel(defaultRoutineAgentProvider)
    )
  ];
  const routineAgentModelOptions = [
    e(
      "option",
      { key: `routine-agent-model-${defaultRoutineAgentModel}`, value: defaultRoutineAgentModel },
      `Codex 기본: ${defaultRoutineAgentModel}`
    )
  ];

  const groqWorkerOptionSeen = new Set();
  const groqWorkerModelOptions = [];
  const pushGroqWorkerOption = (value, label) => {
    if (groqWorkerOptionSeen.has(value)) {
      return;
    }
    groqWorkerOptionSeen.add(value);
    groqWorkerModelOptions.push(e("option", { key: `gw-${value}`, value }, label));
  };
  pushGroqWorkerOption(noneModel, "Groq: 선택 안함");
  pushGroqWorkerOption(defaultGroqWorkerModel, `Groq 기본: ${defaultGroqWorkerModel}`);
  groqModels.forEach((item) => pushGroqWorkerOption(item.id, item.id));

  const geminiWorkerModelOptions = [
    e("option", { key: "gm-none", value: noneModel }, "Gemini: 선택 안함"),
    ...geminiModelChoices.map((item) => e("option", { key: `gm-${item.id}`, value: item.id }, item.label))
  ];

  const nvidiaWorkerOptionSeen = new Set();
  const nvidiaWorkerModelOptions = [];
  const pushNvidiaWorkerOption = (value, label) => {
    if (nvidiaWorkerOptionSeen.has(value)) {
      return;
    }
    nvidiaWorkerOptionSeen.add(value);
    nvidiaWorkerModelOptions.push(e("option", { key: `nw-${value}`, value }, label));
  };
  pushNvidiaWorkerOption(noneModel, "NVIDIA NIM: 선택 안함");
  pushNvidiaWorkerOption(defaultNvidiaModel, `NVIDIA NIM 기본: ${defaultNvidiaModel}`);
  nvidiaModelChoices.forEach((item) => pushNvidiaWorkerOption(item.id, item.label));

  const copilotWorkerOptionSeen = new Set([noneModel]);
  const copilotWorkerModelOptions = [
    e("option", { key: "cw-none", value: noneModel }, "Copilot: 선택 안함")
  ];
  copilotModels.forEach((item) => {
    if (copilotWorkerOptionSeen.has(item.id)) {
      return;
    }
    copilotWorkerOptionSeen.add(item.id);
    copilotWorkerModelOptions.push(e("option", { key: `cw-${item.id}`, value: item.id }, item.id));
  });

  const codexWorkerModelOptions = [
    e("option", { key: "xw-none", value: noneModel }, "Codex: 선택 안함"),
    ...codeXModelChoices.map((item) =>
      e("option", { key: `xw-${item.id}`, value: item.id }, item.label)
    )
  ];

  return {
    groqModelOptions,
    copilotModelOptions,
    codexModelOptions,
    geminiModelOptions,
    nvidiaModelOptions,
    routineAgentProviderOptions,
    routineAgentModelOptions,
    groqWorkerModelOptions,
    geminiWorkerModelOptions,
    nvidiaWorkerModelOptions,
    copilotWorkerModelOptions,
    codexWorkerModelOptions
  };
}

export function buildSettingsModelTableState({
  e,
  groqModels,
  groqUsageWindowBaseByModel,
  copilotModels,
  copilotPremiumUsage,
  copilotLocalUsage,
  formatDecimal
}) {
  const groqRows = groqModels.length === 0
    ? [e("tr", { key: "empty-g" }, e("td", { colSpan: 13 }, "모델 데이터가 없습니다."))]
    : groqModels.map((item) => e(
      "tr",
      { key: item.id },
      e("td", null, item.id),
      e("td", null, item.tier || "-"),
      e("td", null, item.speed_tps || "-"),
      e("td", null, item.context_window || "-"),
      e("td", null, item.max_completion_tokens || "-"),
      e("td", null, item.rpm || "-"),
      e("td", null, item.rpd || "-"),
      e("td", null, item.tpm || "-"),
      e("td", null, item.tpd || "-"),
      e("td", null, item.ash || "-"),
      e("td", null, item.asd || "-"),
      (() => {
        const usageRequests = Number(item.usage_requests || 0);
        const usageTokens = Number(item.usage_total_tokens || 0);
        const baseline = item.id ? (groqUsageWindowBaseByModel[item.id] || {}) : {};
        const minuteRequests = Math.max(0, usageRequests - Number(baseline.minuteRequests ?? usageRequests));
        const minuteTokens = Math.max(0, usageTokens - Number(baseline.minuteTokens ?? usageTokens));
        const hourRequests = Math.max(0, usageRequests - Number(baseline.hourRequests ?? usageRequests));
        const hourTokens = Math.max(0, usageTokens - Number(baseline.hourTokens ?? usageTokens));
        const dayRequests = Math.max(0, usageRequests - Number(baseline.dayRequests ?? usageRequests));
        const dayTokens = Math.max(0, usageTokens - Number(baseline.dayTokens ?? usageTokens));
        return e(
          "td",
          null,
          e("div", { className: "tiny" }, `분 ${minuteRequests}req/${minuteTokens}tok`),
          e("div", { className: "tiny" }, `시 ${hourRequests}req/${hourTokens}tok`),
          e("div", { className: "tiny" }, `일 ${dayRequests}req/${dayTokens}tok`)
        );
      })(),
      e(
        "td",
        null,
        e("div", { className: "tiny" }, item.limit_requests == null ? "req -/-" : `req ${item.remaining_requests || 0}/${item.limit_requests}`),
        e("div", { className: "tiny" }, item.limit_tokens == null ? "tok -/-" : `tok ${item.remaining_tokens || 0}/${item.limit_tokens}`),
        e("div", { className: "tiny" }, `reset ${item.reset_requests || "-"} / ${item.reset_tokens || "-"}`)
      )
    ));

  const copilotRows = copilotModels.length === 0
    ? [e("tr", { key: "empty-c" }, e("td", { colSpan: 8 }, "Copilot 모델 데이터가 없습니다."))]
    : copilotModels.map((item) => e(
      "tr",
      { key: item.id },
      e("td", null, item.id),
      e("td", null, item.provider || "-"),
      e("td", null, item.premium_multiplier || "-"),
      e("td", null, item.speed_tps || "-"),
      e("td", null, item.rate_limit || "-"),
      e("td", null, item.context_window || "-"),
      e("td", null, item.max_completion_tokens || "-"),
      e("td", null, `${item.usage_requests || 0} req`)
    ));

  const premiumPercent = Number.parseFloat(copilotPremiumUsage.percent_used || "0");
  const copilotPremiumPercent = Number.isFinite(premiumPercent)
    ? Math.min(100, Math.max(0, premiumPercent))
    : 0;

  const premiumQuota = Number.parseFloat(copilotPremiumUsage.monthly_quota || "0");
  const copilotPremiumQuotaText = (!Number.isFinite(premiumQuota) || premiumQuota <= 0)
    ? "-"
    : formatDecimal(premiumQuota, 1);

  const copilotPremiumRows = !Array.isArray(copilotPremiumUsage.items) || copilotPremiumUsage.items.length === 0
    ? [e("tr", { key: "empty-cp" }, e("td", { colSpan: 3 }, "모델별 사용량 데이터가 없습니다."))]
    : copilotPremiumUsage.items.map((item, index) => e(
      "tr",
      { key: `cp-${item.model}-${index}` },
      e("td", null, item.model || "-"),
      e("td", null, `${formatDecimal(item.requests, 1)} req`),
      e("td", null, `${formatDecimal(item.percent, 2)}%`)
    ));

  const copilotLocalRows = !Array.isArray(copilotLocalUsage.items) || copilotLocalUsage.items.length === 0
    ? [e("tr", { key: "empty-cl" }, e("td", { colSpan: 2 }, "로컬 사용량 데이터가 없습니다."))]
    : copilotLocalUsage.items.map((item, index) => e(
      "tr",
      { key: `cl-${item.model}-${index}` },
      e("td", null, item.model || "-"),
      e("td", null, `${item.requests || 0} req`)
    ));

  return {
    groqRows,
    copilotRows,
    copilotPremiumPercent,
    copilotPremiumQuotaText,
    copilotPremiumRows,
    copilotLocalRows
  };
}
