const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const {
  MULTI_COMPARE_PREFIX,
  normalizeChatMultiResultMessage,
  buildChatMultiDisplayLabels,
  normalizeSummarySections,
  parseChatMultiComparisonMessage,
  buildChatMultiRenderSnapshot
} = require("./chat-multi-utils.js");

const gatewaySourcePath = path.resolve(__dirname, "../omninode-middleware/src/WebSocketGateway.Serialization.cs");
const composerRendererPath = path.resolve(__dirname, "modules/dashboard-composer-renderers.js");
const threadRendererPath = path.resolve(__dirname, "modules/dashboard-thread-renderers.js");
const stylesPath = path.resolve(__dirname, "styles.css");
const expectedGatewayKeys = [
  "type",
  "conversationId",
  "groq",
  "gemini",
  "cerebras",
  "nvidia",
  "copilot",
  "codex",
  "summary",
  "commonSummary",
  "commonCore",
  "differences",
  "groqModel",
  "geminiModel",
  "cerebrasModel",
  "nvidiaModel",
  "copilotModel",
  "codexModel",
  "requestedSummaryProvider",
  "resolvedSummaryProvider",
  "conversation",
  "autoMemoryNote"
];

function extractGatewayMultiResultKeys(sourceText) {
  const startToken = "private static string BuildMultiChatResultJson(ConversationMultiResult result)";
  const endToken = "private static string BuildConversationJson";
  const start = sourceText.indexOf(startToken);
  assert.notEqual(start, -1, "WebSocketGateway.cs에서 BuildMultiChatResultJson 함수를 찾을 수 없습니다.");

  const end = sourceText.indexOf(endToken, start);
  assert.notEqual(end, -1, "WebSocketGateway.cs에서 BuildConversationJson 함수를 찾을 수 없습니다.");

  const methodBody = sourceText.slice(start, end);
  const keyRegex = /\\\"([a-zA-Z0-9_]+)\\\":/g;
  const keys = [];
  let match = null;
  while ((match = keyRegex.exec(methodBody)) !== null) {
    keys.push(match[1]);
  }

  return [...new Set(keys)];
}

function assertGatewayContract() {
  const sourceText = fs.readFileSync(gatewaySourcePath, "utf8");
  const keys = extractGatewayMultiResultKeys(sourceText);
  const missingKeys = expectedGatewayKeys.filter((key) => !keys.includes(key));
  assert.equal(missingKeys.length, 0, `llm_chat_multi_result 계약 누락 필드: ${missingKeys.join(", ")}`);
  assert.ok(sourceText.includes('\\"tokenUsageTotal\\":{BuildTokenUsageJson(conversation.TokenUsageTotal)}'), "conversation JSON tokenUsageTotal 직렬화 누락");
  assert.ok(sourceText.includes('\\"tokenUsage\\":{BuildTokenUsageJson(message.TokenUsage)}'), "message tokenUsage 직렬화 누락");
  return {
    keys,
    missingKeys,
    extraKeys: keys.filter((key) => !expectedGatewayKeys.includes(key))
  };
}

function assertTokenUsageRendererContract() {
  const composerSource = fs.readFileSync(composerRendererPath, "utf8");
  const threadSource = fs.readFileSync(threadRendererPath, "utf8");
  const stylesSource = fs.readFileSync(stylesPath, "utf8");

  assert.match(composerSource, /renderComposerTokenPill\(e, tokenUsageTotal\)/, "composer 왼쪽 토큰 pill 렌더링 누락");
  assert.match(composerSource, /className: "toolbar token-toolbar"/, "composer token-toolbar 클래스 누락");
  assert.match(threadSource, /\[saveNotebookButton, createPlanButton, ttsButton\]/, "assistant 액션 순서가 노트북 → 작업계획 → 음성이 아닙니다");
  assert.match(threadSource, /className: "assistant-action-column"/, "assistant 액션 세로 열 렌더링 누락");
  assert.match(threadSource, /className: "message-token-usage"/, "assistant 메시지 토큰 label 렌더링 누락");
  assert.match(threadSource, /toLocaleString\("en-US"\).*tokens/s, "토큰 tooltip 전체 숫자 포맷 누락");
  assert.match(stylesSource, /\.composer-token-pill/, "composer token pill 스타일 누락");
  assert.match(stylesSource, /\.assistant-action-column\s*\{[^}]*flex-direction:\s*column/s, "assistant 액션 세로 스타일 누락");
  assert.match(stylesSource, /\.message-token-usage/, "메시지 토큰 label 스타일 누락");
}

function run() {
  const gatewayContract = assertGatewayContract();
  assertTokenUsageRendererContract();

  const payload = {
    type: "llm_chat_multi_result",
    conversationId: "conv-001",
    groq: "g",
    gemini: "ge",
    cerebras: "c",
    nvidia: "n",
    copilot: "co",
    codex: "cx",
    summary: "sum",
    commonSummary: "sum",
    commonCore: "- 공통 1",
    differences: "- 차이 1",
    groqModel: " openai/gpt-oss-120b ",
    geminiModel: " gemini-3-flash-preview ",
    cerebrasModel: " zai-glm-4.7 ",
    nvidiaModel: " meta/llama-3.1-70b-instruct ",
    copilotModel: " gpt-5 ",
    codexModel: " codex-mini ",
    requestedSummaryProvider: " auto ",
    resolvedSummaryProvider: " gemini ",
    conversation: { id: "conv-001", scope: "chat", mode: "multi", messages: [] },
    autoMemoryNote: null
  };

  const normalized = normalizeChatMultiResultMessage(payload);

  assert.equal(normalized.groqModel, "openai/gpt-oss-120b");
  assert.equal(normalized.geminiModel, "gemini-3-flash-preview");
  assert.equal(normalized.cerebrasModel, "zai-glm-4.7");
  assert.equal(normalized.nvidiaModel, "meta/llama-3.1-70b-instruct");
  assert.equal(normalized.copilotModel, "gpt-5");
  assert.equal(normalized.codexModel, "codex-mini");
  assert.equal(normalized.requestedSummaryProvider, "auto");
  assert.equal(normalized.resolvedSummaryProvider, "gemini");
  assert.equal(normalized.commonSummary, "sum");
  assert.equal(normalized.commonCore, "- 공통 1");
  assert.equal(normalized.differences, "- 차이 1");

  const labels = buildChatMultiDisplayLabels(normalized);
  assert.equal(labels.groqLabel, "Groq (openai/gpt-oss-120b)");
  assert.equal(labels.geminiLabel, "Gemini (gemini-3-flash-preview)");
  assert.equal(labels.cerebrasLabel, "Cerebras (zai-glm-4.7 (preview))");
  assert.equal(labels.nvidiaLabel, "NVIDIA NIM (meta/llama-3.1-70b-instruct)");
  assert.equal(labels.copilotLabel, "Copilot (gpt-5)");
  assert.equal(labels.codexLabel, "Codex (codex-mini)");
  assert.equal(labels.summaryLabel, "요약 (요청=auto, 실제=gemini)");

  const summarySections = normalizeSummarySections(payload);
  assert.deepEqual(
    summarySections.map((section) => section.title),
    ["공통 요약", "공통 핵심", "부분 차이"]
  );
  assert.deepEqual(
    summarySections.map((section) => section.body),
    ["sum", "- 공통 1", "- 차이 1"]
  );

  const comparisonPayload = `${MULTI_COMPARE_PREFIX}${JSON.stringify({
    entries: [
      { provider: "groq", model: "openai/gpt-oss-120b", text: "g" },
      { provider: "nvidia", model: "meta/llama-3.1-70b-instruct", text: "n" },
      { provider: "codex", model: "codex-mini", text: "cx" }
    ]
  })}`;
  const comparison = parseChatMultiComparisonMessage(comparisonPayload);
  assert.ok(comparison, "비교 payload 파싱 결과가 null이면 안 됩니다.");
  assert.deepEqual(
    comparison.entries.map((entry) => entry.heading),
    ["Groq (openai/gpt-oss-120b)", "NVIDIA NIM (meta/llama-3.1-70b-instruct)", "Codex (codex-mini)"]
  );

  const snapshot = buildChatMultiRenderSnapshot(payload);
  assert.deepEqual(
    snapshot.entries.map((section) => section.provider),
    ["groq", "gemini", "cerebras", "nvidia", "copilot", "codex"]
  );
  assert.deepEqual(
    snapshot.entries.map((section) => section.heading),
    [
      "Groq (openai/gpt-oss-120b)",
      "Gemini (gemini-3-flash-preview)",
      "Cerebras (zai-glm-4.7 (preview))",
      "NVIDIA NIM (meta/llama-3.1-70b-instruct)",
      "Copilot (gpt-5)",
      "Codex (codex-mini)"
    ]
  );
  assert.deepEqual(
    snapshot.summarySections.map((section) => section.body),
    ["sum", "- 공통 1", "- 차이 1"]
  );

  const fallbackSnapshot = buildChatMultiRenderSnapshot({});
  assert.deepEqual(
    fallbackSnapshot.summarySections.map((section) => section.title),
    ["공통 요약", "공통 핵심", "부분 차이"]
  );
  assert.deepEqual(
    fallbackSnapshot.summarySections.map((section) => section.body),
    ["공통 요약이 없습니다.", "공통점 없음", "의미 있는 차이 없음"]
  );

  console.log(
    JSON.stringify(
      {
        ok: true,
        cases: 6,
        gatewaySourcePath,
        gatewayKeys: gatewayContract.keys,
        gatewayExtraKeys: gatewayContract.extraKeys,
        summaryLabel: labels.summaryLabel,
        fallbackSummaryLabel: fallbackSnapshot.labels.summaryLabel,
        comparisonPrefix: MULTI_COMPARE_PREFIX
      },
      null,
      2
    )
  );
}

run();
