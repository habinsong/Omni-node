import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), "utf8");
}

const wsLogic = read("apps/omninode-middleware/src/WsLogicCommandDispatcher.cs");
const socketLoop = read("apps/omninode-middleware/src/WebSocketGateway.SocketLoop.cs");
const logicGraphs = read("apps/omninode-middleware/src/CommandService.LogicGraphs.cs");
const logicModels = read("apps/omninode-middleware/src/Application/Logic/LogicGraphModels.cs");
const app = read("apps/omninode-dashboard/app.js");
const router = read("apps/omninode-dashboard/modules/dashboard-server-message-router.mjs");
const wsLogicClient = read("apps/omninode-dashboard/modules/ws-logic.js");

assert.match(wsLogic, /TryHandleAsync\(\s*WebSocketGateway\.ClientMessage message,\s*bool remoteDashboardClient,/s);
assert.match(wsLogic, /IsRemoteRestrictedLogicMessage/);
assert.match(wsLogic, /"logic_graph_run"/);
assert.match(wsLogic, /"logic_graph_save"/);
assert.match(wsLogic, /SaveLogicGraphAsync\(\s*targetGraphId,\s*message\.LogicGraphJson,/s);
assert.match(socketLoop, /_logicCommandDispatcher\.TryHandleAsync\(\s*message,\s*remoteDashboardClient,/s);

assert.match(logicModels, /string ActiveRunId = ""/);
assert.match(logicGraphs, /ToLogicGraphSummaryWithRuntime/);
assert.match(logicGraphs, /ClearLogicGraphRunningState/);
assert.match(logicGraphs, /NormalizeLogicCodingOutcome/);
assert.match(logicGraphs, /LooksLikeLogicAiFailure/);

assert.match(app, /const graphPayload = logicDirty \|\| !targetGraphId \? draftGraph : null/);
assert.match(app, /requestLogicGraphRunGet,/);
assert.match(router, /activeItem\?\.activeRunId/);
assert.match(router, /requestLogicGraphRunGet\(actions\.send, activeItem\.activeRunId/);
assert.match(wsLogicClient, /logicGraph: graphOrOptions/);

console.log("logic tab contract ok");
