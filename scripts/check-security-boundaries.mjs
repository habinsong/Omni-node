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
assertIncludes(socketLoop, "return !IsRemoteDashboardClient(context);", "remote websocket without origin is rejected");
assertIncludes(socketLoop, "TryValidateClientPayloadLimits(text", "websocket payload limit gate");
assertIncludes(socketLoop, "AllowCommand(sessionId!)", "websocket command rate limit");
assertIncludes(socketLoop, "TryHandleAsync(message, remoteDashboardClient, authenticated", "setup dispatcher receives auth state");
assertIncludes(socketLoop, "IsAllowedRemoteLimitedMessage(message.Type)", "remote limited mode has explicit message allowlist gate");
assertIncludes(socketLoop, "forbidden_remote_limited_action", "remote limited mode blocks non-allowlisted actions");
assertIncludes(socketLoop, "\"list_conversations\"", "remote limited mode allows read-only conversation list");
assertIncludes(socketLoop, "\"notebook_get\"", "remote limited mode allows notebook read");
assertNotIncludes(socketLoop, "\"llm_chat_single\"", "remote limited mode must not allow chat execution");
assertNotIncludes(socketLoop, "\"coding_run_single\"", "remote limited mode must not allow coding execution");
assertNotIncludes(socketLoop, "\"run_routine\"", "remote limited mode must not allow routine execution");
assertNotIncludes(socketLoop, "\"logic_graph_run\"", "remote limited mode must not allow logic execution");

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

const conversationDispatcher = read("apps/omninode-middleware/src/WsConversationMemoryDispatcher.cs");
assertIncludes(conversationDispatcher, "IsConversationMemoryMessage", "conversation dispatcher message allowlist");
assertIncludes(conversationDispatcher, "or \"delete_conversation\"", "conversation delete requires auth");
assertIncludes(conversationDispatcher, "or \"clear_memory\"", "memory clear requires auth");
assertIncludes(conversationDispatcher, "if (!isAuthenticated)", "conversation dispatcher auth guard");

const apiEndpoint = read("apps/omninode-middleware/src/GatewayApiEndpoint.cs");
assertIncludes(apiEndpoint, "_localImageRoots", "local image root allowlist");
assertIncludes(apiEndpoint, "IsLocalImagePathAllowed(fullPath)", "local image path guard");
assertIncludes(apiEndpoint, "Path.Combine(workspaceRoot, \"routines\")", "local image routine root");

const staticFileEndpoint = read("apps/omninode-middleware/src/HttpStaticFileEndpoint.cs");
assertNotIncludes(staticFileEndpoint, "File.ReadAllTextAsync", "static assets must not be re-encoded as text per request");
assertIncludes(staticFileEndpoint, "File.ReadAllBytesAsync", "static assets are served as bytes");
assertIncludes(staticFileEndpoint, "_assetCache", "static assets use a small in-memory cache");
assertIncludes(staticFileEndpoint, "HttpStatusCode.NotModified", "static assets support conditional 304 responses");
assertIncludes(staticFileEndpoint, "\"ETag\"", "static assets expose ETag");
assertIncludes(staticFileEndpoint, "\"Last-Modified\"", "static assets expose Last-Modified");
assertIncludes(staticFileEndpoint, "\"Cache-Control\"] = \"no-cache\"", "static assets revalidate without breaking local edits");

const coreMain = read("apps/omninode-core/src/main.c");
const udsCoreClient = read("apps/omninode-middleware/src/UdsCoreClient.cs");
const coreBootstrapper = read("apps/omninode-middleware/src/CoreProcessBootstrapper.cs");
assertIncludes(coreMain, "parse_command_request", "core uses fixed line protocol parser");
assertIncludes(coreMain, "strcmp(command, \"get_metrics\")", "core metrics command is fixed token");
assertIncludes(coreMain, "strcmp(command, \"kill\")", "core kill command is fixed token plus pid");
assertIncludes(coreMain, "OMNINODE_CORE_AUTH_TOKEN", "core requires middleware auth token");
assertIncludes(coreMain, "strcmp(auth, g_auth_token)", "core validates middleware auth token");
assertIncludes(coreMain, "resolve_posix_socket_path", "core honors configured POSIX socket path");
assertIncludes(coreMain, "write_all", "core sends full nonblocking responses");
assertNotIncludes(coreMain, "extract_json_string", "core must not use ad hoc JSON string parser");
assertNotIncludes(coreMain, "extract_json_int64", "core must not use ad hoc JSON integer parser");
assertIncludes(udsCoreClient, "RequestAsync($\"get_metrics {_authToken}\"", "middleware sends authenticated core metrics line command");
assertIncludes(udsCoreClient, "RequestAsync($\"kill {_authToken} {pid}\"", "middleware sends authenticated core kill line command");
assertIncludes(coreBootstrapper, "OMNINODE_CORE_AUTH_TOKEN", "middleware passes core auth token to core process");

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
assertIncludes(errorMessages, "forbidden_remote_limited_action", "dashboard has generic remote limited action copy");

const architectureDoc = read("docs/아키텍처_흐름.md");
assertIncludes(architectureDoc, "외부접속 제한 모드 권한표", "architecture doc documents remote limited permissions");
assertIncludes(architectureDoc, "대화/코딩/루틴/로직 그래프 실행", "architecture doc documents remote execution actions are blocked");
assertIncludes(architectureDoc, "`ETag`/`Last-Modified`", "architecture doc documents static asset revalidation");

const gateway = read("apps/omninode-middleware/src/WebSocketGateway.cs");
const inputPreparation = read("apps/omninode-middleware/src/CommandService.InputPreparation.cs");
assertNotIncludes(gateway, "dataBase64[..700_000]", "gateway must reject rather than truncate attachments");
assertNotIncludes(inputPreparation, "base64[..700_000]", "input preparation must not truncate attachments");

const commandService = read("apps/omninode-middleware/src/CommandService.cs");
const appConfig = read("apps/omninode-middleware/src/AppConfig.cs");
const commandExecution = read("apps/omninode-middleware/src/CommandService.Execution.cs");
const codingExecution = read("apps/omninode-middleware/src/CommandService.CodingExecution.cs");
const codingResultActions = read("apps/omninode-middleware/src/CommandService.CodingResultActions.cs");
const routines = read("apps/omninode-middleware/src/CommandService.Routines.cs");
const atomicFileStore = read("apps/omninode-middleware/src/AtomicFileStore.cs");
const conversationStore = read("apps/omninode-middleware/src/ConversationStore.cs");
const sessionManager = read("apps/omninode-middleware/src/SessionManager.cs");
const routineStore = read("apps/omninode-middleware/src/Infrastructure/Persistence/FileRoutineStore.cs");
const planStore = read("apps/omninode-middleware/src/Infrastructure/Persistence/FilePlanStore.cs");
const taskGraphStore = read("apps/omninode-middleware/src/Infrastructure/Persistence/FileTaskGraphStore.cs");
const telegramOutboxStore = read("apps/omninode-middleware/src/Infrastructure/Persistence/FileTelegramReplyOutboxStore.cs");
const workspaceDoctorCheck = read("apps/omninode-middleware/src/Application/Doctor/Checks/WorkspaceDoctorCheck.cs");
const memoryIndexDocumentSync = read("apps/omninode-middleware/src/MemoryIndexDocumentSync.cs");
const wsConversationMemoryDispatcher = read("apps/omninode-middleware/src/WsConversationMemoryDispatcher.cs");
const memoryApplicationService = read("apps/omninode-middleware/src/Application/MemoryApplicationService.cs");
assertIncludes(commandService, "IsDynamicCodeExecutionEnabled", "command service has shared dynamic code guard");
assertIncludes(commandService, "BuildDynamicCodeDisabledMessage", "command service has shared dynamic code disabled message");
assertIncludes(appConfig, "EnableAutoInstall", "auto install has explicit config flag");
assertIncludes(appConfig, "OMNINODE_ENABLE_AUTO_INSTALL", "auto install has environment toggle");
assertIncludes(appConfig, "bool EnableAutoInstall", "auto install is part of execution options");
assertIncludes(commandExecution, "if (!IsDynamicCodeExecutionEnabled())", "natural dynamic code path checks dynamic code flag");
assertIncludes(codingExecution, "if (!IsDynamicCodeExecutionEnabled())", "workspace shell execution checks dynamic code flag");
assertIncludes(codingExecution, "new ShellRunResult(126", "workspace shell execution returns blocked result");
assertIncludes(codingExecution, "if (_execution.EnableAutoInstall)", "auto install pipeline checks explicit flag");
assertIncludes(codingResultActions, "if (!IsDynamicCodeExecutionEnabled())", "coding result rerun checks dynamic code flag");
assertIncludes(codingResultActions, "\"blocked\"", "coding result rerun returns blocked execution status");
assertIncludes(routines, "if (!IsDynamicCodeExecutionEnabled())", "routine script execution checks dynamic code flag");
assertIncludes(routines, "runStatus = \"blocked\"", "routine script execution records blocked status");
assertIncludes(atomicFileStore, "AcquireWriteLease", "state writes use per-file lease");
assertIncludes(atomicFileStore, "BackupSuffix = \".bak\"", "state writes keep backup file");
assertIncludes(atomicFileStore, "ReadAllTextWithBackup", "state reads can recover from backup");
assertIncludes(conversationStore, "ReadAllTextWithBackup", "conversation state uses backup recovery");
assertIncludes(sessionManager, "ReadAllTextWithBackup", "session state uses backup recovery");
assertIncludes(routineStore, "ReadAllTextWithBackup", "routine state uses backup recovery");
assertIncludes(planStore, "ReadAllTextWithBackup", "plan state uses backup recovery");
assertIncludes(taskGraphStore, "ReadAllTextWithBackup", "task graph state uses backup recovery");
assertIncludes(telegramOutboxStore, "ReadAllTextWithBackup", "telegram outbox uses backup recovery");
assertIncludes(workspaceDoctorCheck, "corruptJson", "doctor exposes corrupt state json count");
assertIncludes(workspaceDoctorCheck, "backups=", "doctor exposes backup state file count");
assertIncludes(workspaceDoctorCheck, "locks=", "doctor exposes state lock file count");
assertIncludes(memoryIndexDocumentSync, "ElapsedMs", "memory index sync reports elapsed time");
assertIncludes(memoryIndexDocumentSync, "MemoryDocuments", "memory index sync reports memory source count");
assertIncludes(memoryIndexDocumentSync, "SessionDocuments", "memory index sync reports session source count");
assertIncludes(memoryIndexDocumentSync, "ProjectDocuments", "memory index sync reports project source count");
assertIncludes(memoryApplicationService, "elapsedMs=", "memory index rebuild audit includes elapsed time");
assertIncludes(wsConversationMemoryDispatcher, "\\\"elapsedMs\\\"", "memory index rebuild websocket result includes elapsed time");
assertIncludes(wsConversationMemoryDispatcher, "\\\"projectDocuments\\\"", "memory index rebuild websocket result includes project document count");

console.log(JSON.stringify({ ok: true, assertions: 101 }, null, 2));
