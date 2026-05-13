import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), "utf8");
}

const app = read("apps/omninode-dashboard/app.js");
const composer = read("apps/omninode-dashboard/modules/dashboard-composer-renderers.js");
const threadRenderer = read("apps/omninode-dashboard/modules/dashboard-thread-renderers.js");
const workspaceRenderer = read("apps/omninode-dashboard/modules/dashboard-workspace-renderers.js");
const contracts = read("apps/omninode-middleware/src/Application/Planning/PlanningContracts.cs");
const planJson = read("apps/omninode-middleware/src/Application/Planning/PlanJson.cs");
const planJsonContext = read("apps/omninode-middleware/src/Application/Planning/PlanJsonContext.cs");
const planService = read("apps/omninode-middleware/src/Application/Planning/PlanService.cs");
const llmRouter = read("apps/omninode-middleware/src/LlmRouter.cs");
const telegram = read("apps/omninode-middleware/src/CommandService.Telegram.cs");
const telegramCoding = read("apps/omninode-middleware/src/CommandService.Telegram.Coding.cs");

assert.match(app, /function createPlanFromCurrentInput/, "현재 입력을 작업계획 생성폼으로 옮기는 핸들러가 있어야 합니다.");
assert.match(app, /function createPlanFromAssistantMessage/, "최근 답변 기반 계획 생성 핸들러가 있어야 합니다.");
assert.match(app, /function createPlanFromLatestCodingResult/, "최근 코딩 결과 기반 계획 생성 핸들러가 있어야 합니다.");
assert.match(app, /setRootTab\("automation"\)/, "계획 생성 전환은 작업계획이 있는 automation 탭으로 이동해야 합니다.");
assert.match(app, /setResponsivePane\("automation", "plans"\)/, "모바일/반응형에서도 계획 pane으로 이동해야 합니다.");

assert.match(composer, /createPlanFromCurrentInput\("chat:single"\)/, "대화 단일 입력창에서 계획 전환을 제공해야 합니다.");
assert.match(composer, /createPlanFromCurrentInput\("coding:single"\)/, "코딩 단일 입력창에서 계획 전환을 제공해야 합니다.");
assert.match(workspaceRenderer, /composer-icon-btn plan/, "입력창에 작업계획 버튼이 있어야 합니다.");
assert.match(threadRenderer, /onCreatePlanFromAssistantMessage/, "assistant 답변에 작업계획 버튼이 있어야 합니다.");
assert.match(workspaceRenderer, /createPlanFromLatestCodingResult/, "코딩 결과 카드에 작업계획 버튼이 있어야 합니다.");

assert.match(contracts, /record PlanDraft/, "구조화 계획 draft 계약이 있어야 합니다.");
assert.match(planJson, /DeserializeDraft/, "계획 draft JSON 역직렬화가 있어야 합니다.");
assert.match(planJsonContext, /JsonSerializable\(typeof\(PlanDraft\)\)/, "source generation context에 PlanDraft가 포함되어야 합니다.");
assert.match(planService, /TryParseStructuredDraft/, "계획 생성은 구조화 draft를 먼저 파싱해야 합니다.");
assert.match(planService, /ParseSteps\(draft/, "기존 줄 파서 fallback은 유지해야 합니다.");
assert.match(llmRouter, /반드시 JSON 객체 하나만 출력한다/, "계획 LLM 프롬프트는 JSON 출력을 요구해야 합니다.");

assert.match(telegram, /TryBuildLastTelegramAssistantPlanCreate/, "텔레그램 최근 답변 계획 shortcut이 있어야 합니다.");
assert.match(telegramCoding, /TryBuildLatestTelegramCodingPlanCreate/, "텔레그램 최근 코딩 결과 계획 shortcut이 있어야 합니다.");

console.log("[plan-tab-contract] ok");
