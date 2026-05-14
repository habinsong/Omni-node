import { readdirSync, statSync } from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const dashboardDir = path.join(repoRoot, "apps", "omninode-dashboard");
const dashboardModulesDir = path.join(dashboardDir, "modules");

function listScriptFiles(dirPath) {
  const entries = readdirSync(dirPath, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const fullPath = path.join(dirPath, entry.name);
    if (entry.isDirectory()) {
      files.push(...listScriptFiles(fullPath));
      continue;
    }
    if (entry.isFile() && (entry.name.endsWith(".js") || entry.name.endsWith(".mjs"))) {
      files.push(fullPath);
    }
  }
  return files.sort();
}

function toRelative(filePath) {
  return path.relative(repoRoot, filePath) || ".";
}

function runStep(label, command, args) {
  process.stdout.write(`\n[test] ${label}\n`);
  const result = spawnSync(command, args, {
    cwd: repoRoot,
    stdio: "inherit",
    env: process.env
  });
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

function resolvePythonCommand() {
  const candidates = ["python3", "python"];
  for (const candidate of candidates) {
    const result = spawnSync(candidate, ["--version"], {
      cwd: repoRoot,
      stdio: "ignore",
      env: process.env
    });
    if (result.status === 0) {
      return candidate;
    }
  }
  throw new Error("python3/python 실행 파일을 찾을 수 없습니다.");
}

function main() {
  if (!statSync(dashboardDir).isDirectory()) {
    throw new Error(`대시보드 디렉터리를 찾을 수 없습니다: ${dashboardDir}`);
  }

  const dashboardFiles = [
    path.join(dashboardDir, "app.js"),
    ...listScriptFiles(dashboardModulesDir)
  ];

  runStep(
    "repo hygiene gate",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-repo-hygiene.mjs"))]
  );

  for (const filePath of dashboardFiles) {
    runStep(`node --check ${toRelative(filePath)}`, "node", ["--check", toRelative(filePath)]);
  }

  runStep(
    "chat multi 계약 smoke",
    "node",
    [toRelative(path.join(dashboardDir, "check-chat-multi-utils.js"))]
  );
  runStep(
    "ops flow 성능 smoke",
    "node",
    [toRelative(path.join(dashboardDir, "check-ops-flow-performance.js")), "--events", "256", "--iterations", "20"]
  );
  runStep(
    "dashboard server message router contract",
    "node",
    [toRelative(path.join(dashboardDir, "check-dashboard-server-message-router.mjs"))]
  );
  runStep(
    "security boundary contract",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-security-boundaries.mjs"))]
  );
  runStep(
    "coding python game contract",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-coding-python-game-contract.mjs"))]
  );
  runStep(
    "chat telegram contract",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-chat-telegram-contract.mjs"))]
  );
  runStep(
    "logic tab contract",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-logic-tab-contract.mjs"))]
  );
  runStep(
    "routine tab contract",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-routine-tab-contract.mjs"))]
  );
  runStep(
    "notebook tab contract",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-notebook-tab-contract.mjs"))]
  );
  runStep(
    "plan tab contract",
    "node",
    [toRelative(path.join(repoRoot, "scripts", "check-plan-tab-contract.mjs"))]
  );
  runStep(
    "middleware build",
    "dotnet",
    ["build", "apps/omninode-middleware/OmniNode.Middleware.csproj"]
  );

  const pythonCommand = resolvePythonCommand();
  runStep(
    "sandbox smoke",
    pythonCommand,
    ["apps/omninode-sandbox/executor.py", "--code", "print('ok')"]
  );

  process.stdout.write("\n[test] ok\n");
}

main();
