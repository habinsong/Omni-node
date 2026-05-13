import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(rel) {
  return readFileSync(path.join(repoRoot, rel), "utf8");
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

const telegram = read("apps/omninode-middleware/src/CommandService.Telegram.cs");
const updateLoop = read("apps/omninode-middleware/src/TelegramUpdateLoop.cs");
const execution = read("apps/omninode-middleware/src/CommandService.Execution.cs");
const contracts = read("apps/omninode-middleware/src/Application/ApplicationServiceContracts.cs");
const inputPrep = read("apps/omninode-middleware/src/CommandService.InputPreparation.cs");
const utils = read("apps/omninode-middleware/src/CommandService.Utils.cs");

assert(
  contracts.includes("TelegramTurnContext? telegramContext = null"),
  "ICommandExecutionService가 TelegramTurnContext를 전달해야 합니다."
);
assert(
  execution.includes("_telegramTurnContext.Value = telegramContext") && execution.includes("_telegramTurnContext.Value = previousTelegramContext"),
  "CommandService 실행 중 TelegramTurnContext를 설정하고 복원해야 합니다."
);
assert(
  telegram.includes("ResolveTelegramStateKey") && telegram.includes("_telegramTurnContext.Value?.SessionKey"),
  "텔레그램 상태 키는 chat/user 컨텍스트를 우선해야 합니다."
);
assert(
  updateLoop.includes("TryBuildTelegramTurnContext(update, isCallback: false") &&
    updateLoop.includes("TryBuildTelegramTurnContext(update, isCallback: true"),
  "TelegramUpdateLoop가 일반 메시지와 callback에 TelegramTurnContext를 전달해야 합니다."
);
assert(
  updateLoop.includes("IsAllowedCallbackCommand") &&
    updateLoop.includes("StartsWith(\"/skill \"") &&
    updateLoop.includes("StartsWith(\"/think \"") &&
    updateLoop.includes("StartsWith(\"/web \""),
  "callback data는 허용된 텔레그램 명령으로 제한해야 합니다."
);
assert(
  telegram.includes("shouldAllowFastWeb") &&
    telegram.includes("&& !isSkillContextQuery") &&
    telegram.includes("var effectiveWebSearchEnabled = snapshot.Mode == \"single\"") &&
    !telegram.includes("var effectiveWebSearchEnabled = snapshot.Mode == \"single\" ? false : webSearchEnabled"),
  "텔레그램 단일 모드에서 스킬/Think+ 조합의 웹검색 컨텍스트가 무조건 꺼지면 안 됩니다."
);
assert(
  telegram.includes("BuildTelegramFullFidelityPrompt") &&
    telegram.includes("preserveContext") &&
    telegram.includes("첨부/검색/스킬 컨텍스트를 임의로 줄이지 마세요"),
  "텔레그램은 스킬/검색/첨부 컨텍스트가 있을 때 7줄 압축 프롬프트를 쓰면 안 됩니다."
);
assert(
  telegram.includes("ShouldSkipTelegramDriftRecovery") &&
    telegram.includes("[직전 주제]") &&
    telegram.includes("[Active Skill") &&
    telegram.includes("[Think+ 참고 자료"),
  "텔레그램 off-topic 재요청은 follow-up, 스킬, Think+ 컨텍스트에서 보수적으로 건너뛰어야 합니다."
);
assert(
  !telegram.includes(".Take(safeMaxChars == 0 ? 200") &&
    telegram.includes("telegram_response_truncated"),
  "FormatTelegramResponse가 줄 수 기준으로 조용히 자르면 안 됩니다."
);
assert(
  updateLoop.includes("cleanedText.Length > 3800") &&
    updateLoop.includes("응답이 길어 별도 메시지로 나눠 보냅니다."),
  "긴 텔레그램 응답은 progress replace 대신 일반 분할/문서 송신 경로로 보내야 합니다."
);
assert(
  inputPrep.includes("canSelectedProviderHandleAttachments") &&
    inputPrep.includes("선택 모델(") &&
    inputPrep.includes("로 먼저 요약했습니다."),
  "첨부는 선택 모델이 직접 못 볼 때 가능한 보조 provider로 요약 후 전달해야 합니다."
);
assert(
  utils.includes("LooksLikeStrongFollowupQuestion") &&
    utils.includes("LooksLikeExplicitStandaloneQuestion") &&
    utils.includes("새 요청에 자체 주제와 대상이 분명하면 [최근 대화]는 배경으로만 참고"),
  "대화탭은 후속 질문과 독립 질문을 구분하는 공통 맥락 판단 규칙을 가져야 합니다."
);
assert(
  telegram.includes("FindTelegramAnchorTurn") &&
    telegram.includes("[직전 답변]") &&
    telegram.includes("IsTelegramContextualFollowup") &&
    telegram.includes("[현재 후속 질문]"),
  "텔레그램 후속 질문은 직전 사용자 주제와 직전 assistant 답변을 함께 전달해야 합니다."
);

console.log("[check-chat-telegram-contract] ok");
