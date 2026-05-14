import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), "utf8");
}

function assertIncludes(text, needle, label) {
  assert.ok(text.includes(needle), `${label}: expected to include ${needle}`);
}

function assertNotIncludes(text, needle, label) {
  assert.ok(!text.includes(needle), `${label}: expected not to include ${needle}`);
}

const socketLoop = read("apps/omninode-middleware/src/WebSocketGateway.SocketLoop.cs");
assertIncludes(socketLoop, "IsAllowedWebSocketOrigin(context)", "websocket origin gate");
assertIncludes(socketLoop, "TryValidateClientPayloadLimits(text", "websocket payload limit gate");
assertIncludes(socketLoop, "AllowCommand(sessionId!)", "websocket command rate limit");
assertIncludes(socketLoop, "TryHandleAsync(message, remoteDashboardClient, authenticated", "setup dispatcher receives auth state");

const authGateway = read("apps/omninode-middleware/src/AuthSessionGateway.cs");
assertNotIncludes(authGateway, "MarkAuthenticatedForRemoteDashboard", "remote dashboard must not auto-authenticate");
assertNotIncludes(authGateway, "forbidden_remote_dashboard", "remote dashboard must be able to request OTP");

const setupDispatcher = read("apps/omninode-middleware/src/WsSetupCommandDispatcher.cs");
assertIncludes(setupDispatcher, "bool isAuthenticated", "setup dispatcher auth parameter");
assertIncludes(setupDispatcher, "if (!isAuthenticated)", "setup dispatcher auth guard");
assertIncludes(setupDispatcher, "IsSetupMessage", "setup dispatcher message allowlist");

const conversationDispatcher = read("apps/omninode-middleware/src/WsConversationMemoryDispatcher.cs");
assertIncludes(conversationDispatcher, "IsConversationMemoryMessage", "conversation dispatcher message allowlist");
assertIncludes(conversationDispatcher, "or \"delete_conversation\"", "conversation delete requires auth");
assertIncludes(conversationDispatcher, "or \"clear_memory\"", "memory clear requires auth");
assertIncludes(conversationDispatcher, "if (!isAuthenticated)", "conversation dispatcher auth guard");

const apiEndpoint = read("apps/omninode-middleware/src/GatewayApiEndpoint.cs");
assertIncludes(apiEndpoint, "_localImageRoots", "local image root allowlist");
assertIncludes(apiEndpoint, "IsLocalImagePathAllowed(fullPath)", "local image path guard");
assertIncludes(apiEndpoint, "Path.Combine(workspaceRoot, \"routines\")", "local image routine root");

const markdown = read("apps/omninode-dashboard/modules/dashboard-markdown.js");
assertIncludes(markdown, "html: false", "markdown raw html disabled");
assertIncludes(markdown, "html = renderTableAwareFallbackHtml(text)", "markdown sanitizer fallback");

const gateway = read("apps/omninode-middleware/src/WebSocketGateway.cs");
const inputPreparation = read("apps/omninode-middleware/src/CommandService.InputPreparation.cs");
assertNotIncludes(gateway, "dataBase64[..700_000]", "gateway must reject rather than truncate attachments");
assertNotIncludes(inputPreparation, "base64[..700_000]", "input preparation must not truncate attachments");

console.log(JSON.stringify({ ok: true, assertions: 20 }, null, 2));
