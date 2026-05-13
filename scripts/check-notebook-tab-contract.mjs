import { readFileSync } from "node:fs";

function read(path) {
  return readFileSync(path, "utf8");
}

function assertIncludes(text, needle, label) {
  if (!text.includes(needle)) {
    throw new Error(`${label} 누락: ${needle}`);
  }
}

const inputPreparation = read("apps/omninode-middleware/src/CommandService.InputPreparation.cs");
const notebookService = read("apps/omninode-middleware/src/Application/Notebooks/NotebookService.cs");
const notebookModels = read("apps/omninode-middleware/src/Application/Notebooks/NotebookModels.cs");
const notebookStore = read("apps/omninode-middleware/src/Infrastructure/Persistence/FileNotebookStore.cs");
const wsDispatcher = read("apps/omninode-middleware/src/WsNotebookCommandDispatcher.cs");
const wsClient = read("apps/omninode-dashboard/modules/ws-notebooks.js");
const notebookState = read("apps/omninode-dashboard/modules/notebooks-state.js");
const notebookRenderer = read("apps/omninode-dashboard/modules/notebooks-renderers.js");
const threadRenderer = read("apps/omninode-dashboard/modules/dashboard-thread-renderers.js");
const workspaceRenderer = read("apps/omninode-dashboard/modules/dashboard-workspace-renderers.js");
const app = read("apps/omninode-dashboard/app.js");
const telegram = read("apps/omninode-middleware/src/CommandService.Telegram.cs");
const telegramCoding = read("apps/omninode-middleware/src/CommandService.Telegram.Coding.cs");

assertIncludes(notebookModels, "string Content", "노트북 전체 내용 snapshot");
assertIncludes(notebookModels, "bool ContentTruncated", "노트북 truncation 표시");
assertIncludes(notebookStore, "BuildContent(content)", "노트북 전체 내용 로드");
assertIncludes(notebookService, "BuildContextBlock", "노트북 컨텍스트 블록");
assertIncludes(inputPreparation, "BuildNotebookPromptContext", "대화/코딩 노트북 컨텍스트 주입");
assertIncludes(inputPreparation, "\"notebook\"", "노트북 명시 요청 감지");
assertIncludes(wsDispatcher, "BuildMetadataEntry", "노트북 메타데이터 저장");
assertIncludes(wsClient, "source:", "노트북 source 전송");
assertIncludes(wsClient, "conversationId:", "노트북 conversationId 전송");
assertIncludes(notebookState, "filterText", "노트북 검색 상태");
assertIncludes(notebookState, "expandedDocument", "노트북 전체 보기 상태");
assertIncludes(notebookRenderer, "filterNotebookDocuments", "노트북 검색 UI");
assertIncludes(notebookRenderer, "전체 보기", "노트북 전체 보기 버튼");
assertIncludes(threadRenderer, "onSaveAssistantMessageToNotebook", "대화 답변 노트북 저장 버튼");
assertIncludes(workspaceRenderer, "appendLatestCodingResultToNotebook", "코딩 결과 노트북 저장 버튼");
assertIncludes(app, "appendAssistantMessageToNotebook", "대화 답변 저장 핸들러");
assertIncludes(app, "appendLatestCodingResultToNotebook", "코딩 결과 저장 핸들러");
assertIncludes(telegram, "TryBuildLastTelegramAssistantNotebookAppend", "텔레그램 최근 답변 저장");
assertIncludes(telegramCoding, "TryBuildLatestTelegramCodingNotebookAppend", "텔레그램 코딩 결과 저장");

console.log("[notebook-tab-contract] ok");
