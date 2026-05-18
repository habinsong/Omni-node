import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(relativePath) {
  return fs.readFileSync(path.join(repoRoot, relativePath), "utf8");
}

function count(source, token) {
  return source.split(token).length - 1;
}

const browserIntent = read("apps/omninode-middleware/src/CommandService.BrowserIntent.cs");
const chat = read("apps/omninode-middleware/src/CommandService.Chat.cs");
const coding = read("apps/omninode-middleware/src/CommandService.Coding.cs");
const browserTool = read("apps/omninode-middleware/src/BrowserTool.cs");
const usage = read("docs/사용법_빠른시작.md");
const toolGuide = read("docs/도구_통합_패널_사용_가이드.md");

assert.match(browserIntent, /BrowserIntentOpenVerbRegex/, "브라우저 자연어 실행 동사 감지 누락");
assert.match(browserIntent, /"네이버", "https:\/\/www\.naver\.com\/"/, "네이버 alias 누락");
assert.match(browserIntent, /BrowserIntentNewTabRegex\.IsMatch\(text\) \? "open" : "navigate"/, "새 탭 외 기본 navigate 계약 누락");
assert.match(browserIntent, /_browserTool\.Execute\(command\.Action, command\.Url\)/, "브라우저 intent가 BrowserTool 실행으로 연결되지 않음");
assert.match(browserIntent, /adapter=playwright/, "실제 Playwright 실행 결과 표시 누락");

assert.equal(count(chat, "TryHandleBrowserChatIntent("), 2, "대화 single/orchestration 경로 브라우저 intent 연결 수 불일치");
assert.equal(count(chat, "TryHandleBrowserMultiIntent("), 1, "대화 multi 경로 브라우저 intent 연결 누락");
assert.equal(count(coding, "TryHandleBrowserCodingIntent("), 3, "코딩 single/orchestration/multi 경로 브라우저 intent 연결 수 불일치");

assert.match(browserTool, /_ => "auto"/, "BrowserTool 기본 auto 모드 누락");
assert.match(usage, /네이버 열어줘/, "사용법 문서의 대화 탭 브라우저 예시 누락");
assert.match(usage, /코딩 탭에서도 `네이버 열어줘`/, "사용법 문서의 코딩 탭 브라우저 예시 누락");
assert.match(toolGuide, /일반 사용자는 대화 탭이나 코딩 탭에 자연어로 입력한다/, "도구 패널 문서의 실제 사용 경로 설명 누락");

console.log("[check-browser-intent-contract] ok");
