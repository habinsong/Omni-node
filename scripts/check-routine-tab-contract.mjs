import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), "utf8");
}

const protocol = read("apps/omninode-middleware/src/WebSocketGateway.Protocol.cs");
const gateway = read("apps/omninode-middleware/src/WebSocketGateway.cs");
const dispatcher = read("apps/omninode-middleware/src/WsRoutineCommandDispatcher.cs");
const routines = read("apps/omninode-middleware/src/CommandService.Routines.cs");
const routineExecution = read("apps/omninode-middleware/src/CommandService.RoutineExecution.cs");
const routineUtils = read("apps/omninode-dashboard/modules/routine-utils.js");
const app = read("apps/omninode-dashboard/app.js");
const composer = read("apps/omninode-dashboard/modules/dashboard-composer-renderers.js");
const workspace = read("apps/omninode-dashboard/modules/dashboard-workspace-renderers.js");

assert.match(protocol, /public bool\? RunImmediately \{ get; set; \}/, "WS 메시지 계약에 runImmediately가 있어야 합니다.");
assert.match(gateway, /TryGetProperty\("runImmediately"/, "WS 파서가 runImmediately를 읽어야 합니다.");
assert.match(gateway, /RunImmediately = runImmediately/, "WS 파서 결과가 ClientMessage에 반영되어야 합니다.");
assert.match(dispatcher, /message\.RunImmediately \?\? true/, "기존 WS 생성은 runImmediately 미전달 시 즉시 실행을 유지해야 합니다.");
assert.match(routines, /bool runImmediately/, "루틴 생성 서비스 계약이 runImmediately를 받아야 합니다.");
assert.match(routineExecution, /if \(!runImmediately\)/, "루틴 생성 코어가 초기 실행 건너뛰기를 지원해야 합니다.");
assert.match(routineExecution, /초기 실행은 건너뛰었습니다/, "저장만 생성 결과 메시지가 있어야 합니다.");

assert.match(routineUtils, /runImmediately: false/, "대시보드 루틴 생성 기본값은 저장만이어야 합니다.");
assert.match(routineUtils, /runImmediately: form\?\.runImmediately === true/, "대시보드 payload에 runImmediately가 포함되어야 합니다.");
assert.match(app, /function createRoutineFromCurrentInput/, "대화/코딩 입력을 루틴 생성폼으로 옮기는 핸들러가 있어야 합니다.");
assert.match(app, /setRootTab\("routine"\)/, "루틴 전환 핸들러가 루틴 탭으로 이동해야 합니다.");
assert.match(composer, /createRoutineFromCurrentInput\("chat:single"\)/, "대화 단일 입력창에서 루틴 전환을 제공해야 합니다.");
assert.match(composer, /createRoutineFromCurrentInput\("coding:single"\)/, "코딩 단일 입력창에서 루틴 전환을 제공해야 합니다.");
assert.match(workspace, /composer-icon-btn routine/, "입력창에 루틴 저장 버튼이 있어야 합니다.");

console.log("[routine-tab-contract] ok");
