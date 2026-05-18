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
assertIncludes(authGateway, "if (remoteDashboardClient)", "remote dashboard limited-mode branch");
assertIncludes(authGateway, "remoteLimited", "remote dashboard limited-mode marker");
assertIncludes(authGateway, "MarkAuthenticatedFromTrusted(sessionId, expiresAtUtc)", "remote dashboard session is marked authenticated without OTP");
assertIncludes(authGateway, "forbidden_remote_auth", "remote dashboard OTP request stays blocked with auth-specific message");

const setupDispatcher = read("apps/omninode-middleware/src/WsSetupCommandDispatcher.cs");
assertIncludes(setupDispatcher, "bool isAuthenticated", "setup dispatcher auth parameter");
assertIncludes(setupDispatcher, "if (!isAuthenticated)", "setup dispatcher auth guard");
assertIncludes(setupDispatcher, "IsSetupMessage", "setup dispatcher message allowlist");
assertIncludes(setupDispatcher, "GetRemoteRestrictionMessage", "remote setup restrictions use categorized messages");
assertIncludes(setupDispatcher, "forbidden_remote_external_access", "remote external access setting is blocked with specific message");
assertIncludes(setupDispatcher, "forbidden_remote_secret_settings", "remote secret settings are blocked with specific message");
assertIncludes(setupDispatcher, "forbidden_remote_auth", "remote auth actions are blocked with specific message");
assertIncludes(setupDispatcher, "\"routing_policy_save\"", "routing policy updates remain in setup allowlist");
assertIncludes(setupDispatcher, "\"set_groq_model\"", "model selection remains in setup allowlist");
assertNotIncludes(setupDispatcher, "\"routing_policy_save\" =>", "routing policy updates must not be remote-blocked");
assertNotIncludes(setupDispatcher, "\"set_groq_model\" =>", "model selection must not be remote-blocked");

const logicDispatcher = read("apps/omninode-middleware/src/WsLogicCommandDispatcher.cs");
assertNotIncludes(logicDispatcher, "IsRemoteRestrictedLogicMessage", "logic graph actions must remain available in remote limited mode");

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

const dashboardApp = read("apps/omninode-dashboard/app.js");
assertIncludes(dashboardApp, "if (!remoteDashboardClient && token)", "remote dashboard must not send resume_auth");
assertIncludes(dashboardApp, "if (!remoteDashboardClient && !authed)", "conversation list requests must wait for local authentication");
assertIncludes(dashboardApp, "send({ type: \"get_settings\" });\n      if (!remoteDashboardClient) {\n        return;", "initial dashboard bootstrap must not send protected local requests before auth");

const settingsRenderer = read("apps/omninode-dashboard/modules/dashboard-settings-renderers.js");
assertIncludes(settingsRenderer, "basic-remote-limited", "remote dashboard settings use limited-mode panel");
assertIncludes(settingsRenderer, "OTP 요청과 인증 재개", "remote dashboard panel explains blocked auth actions");
assertIncludes(settingsRenderer, "로직 그래프 목록, 열기, 저장, 삭제, 실행, 취소, 결과 조회", "remote dashboard panel explains allowed logic actions");

const errorMessages = read("apps/omninode-dashboard/modules/error-messages.js");
assertIncludes(errorMessages, "forbidden_remote_auth", "dashboard has auth-specific remote restriction copy");
assertIncludes(errorMessages, "forbidden_remote_secret_settings", "dashboard has secret-specific remote restriction copy");
assertIncludes(errorMessages, "forbidden_remote_external_access", "dashboard has external-access-specific remote restriction copy");

const architectureDoc = read("docs/아키텍처_흐름.md");
assertIncludes(architectureDoc, "외부접속 제한 모드 권한표", "architecture doc documents remote limited permissions");
assertIncludes(architectureDoc, "로직 그래프 목록, 열기, 경로 탐색, 저장, 삭제, 실행, 취소, 실행 결과 조회", "architecture doc keeps logic graph actions allowed remotely");

const gateway = read("apps/omninode-middleware/src/WebSocketGateway.cs");
const inputPreparation = read("apps/omninode-middleware/src/CommandService.InputPreparation.cs");
assertNotIncludes(gateway, "dataBase64[..700_000]", "gateway must reject rather than truncate attachments");
assertNotIncludes(inputPreparation, "base64[..700_000]", "input preparation must not truncate attachments");

console.log(JSON.stringify({ ok: true, assertions: 42 }, null, 2));
