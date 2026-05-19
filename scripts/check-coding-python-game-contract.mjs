import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), "utf8");
}

function assertIncludes(text, needle, label) {
  if (!text.includes(needle)) {
    throw new Error(`${label}: expected to include ${needle}`);
  }
}

function assertNotIncludes(text, needle, label) {
  if (text.includes(needle)) {
    throw new Error(`${label}: unexpected text ${needle}`);
  }
}

function main() {
  const execution = read("apps/omninode-middleware/src/CommandService.CodingExecution.cs");
  const verification = read("apps/omninode-middleware/src/CommandService.CodingVerification.cs");
  const quality = read("apps/omninode-middleware/src/CommandService.CodingQuality.cs");
  const codingWorkerSelectionPolicy = read("apps/omninode-middleware/src/CodingWorkerSelectionPolicy.cs");
  const profiles = read("apps/omninode-middleware/src/CommandService.CodingProfiles.cs");
  const projectProfiles = read("apps/omninode-middleware/src/CommandService.CodingProjectProfiles.cs");
  const utils = read("apps/omninode-middleware/src/CommandService.Utils.cs");
  const codingLanguagePolicy = read("apps/omninode-middleware/src/CodingLanguagePolicy.cs");
  const codingPromptPolicy = read("apps/omninode-middleware/src/CodingPromptPolicy.cs");
  const codingFallbackPolicy = read("apps/omninode-middleware/src/CodingFallbackPolicy.cs");
  const codingExecutionSafetyPolicy = read("apps/omninode-middleware/src/CodingExecutionSafetyPolicy.cs");
  const codingFallbackDecisionPolicy = read("apps/omninode-middleware/src/CodingFallbackDecisionPolicy.cs");
  const coding = read("apps/omninode-middleware/src/CommandService.Coding.cs");
  const rerun = read("apps/omninode-middleware/src/CommandService.CodingResultActions.cs");
  const dashboardConstants = read("apps/omninode-dashboard/modules/dashboard-constants.js");

  assertIncludes(execution, "EnsureWorkspacePythonEnvironmentAsync", "venv helper");
  assertIncludes(execution, "EnumeratePythonDependencySourceFiles", "python multi-file dependency scan");
  assertIncludes(execution, "python3 -m venv .venv", "venv creation");
  assertIncludes(execution, "python -m venv .venv", "windows venv creation");
  assertIncludes(execution, ".venv/bin/python -m pip install", "venv pip install");
  assertIncludes(execution, ".venv\\\\Scripts\\\\python.exe", "windows venv python");
  assertIncludes(execution, "npm init -y", "node local package init");
  assertIncludes(execution, "npm install --no-save --no-fund --no-audit", "node local package install");
  assertNotIncludes(execution, "Python 게임 외부 패키지 자동 설치 차단", "no game dependency block");
  assertNotIncludes(execution, "ShouldSkipPythonThirdPartyAutoInstallForInteractiveScript", "no skip helper");

  assertIncludes(verification, "[\"SDL_VIDEODRIVER\"] = \"dummy\"", "pygame headless smoke");
  assertIncludes(verification, "[\"OMNI_HEADLESS_TEST\"] = \"1\"", "headless smoke flag");
  assertIncludes(verification, "BuildPythonGameStaticQualityCommand", "static game quality check");
  assertIncludes(verification, "TryBuildPythonProjectVerificationCommand", "python project verification helper");
  assertIncludes(verification, "-m compileall -q .", "python project compileall");
  assertIncludes(verification, "-m pytest -q", "python project pytest");
  assertIncludes(verification, "pyproject.toml", "python pyproject detection");
  assertIncludes(verification, "missing game feature marker", "feature marker diagnostics");
  assertIncludes(verification, "\"rotation\"", "tetris rotation marker");
  assertIncludes(verification, "\"line_clear\"", "tetris line clear marker");
  assertIncludes(verification, "pygame", "pygame module detection");
  assertIncludes(verification, "TryBuildNodeProjectVerificationCommand", "node project verification helper");
  assertIncludes(verification, "npm run build", "node build verification");
  assertIncludes(verification, "npx tsc --noEmit", "typescript verification");
  assertIncludes(verification, "normalizedLanguage is \"javascript\" or \"typescript\" or \"react-vite\"", "typescript/react verification route");
  assertIncludes(verification, "go test ./... && go build ./...", "go project verification");
  assertIncludes(verification, "cargo test && cargo build", "rust cargo verification");
  assertIncludes(verification, "php -l", "php syntax verification");
  assertIncludes(verification, "ruby -c", "ruby syntax verification");
  assertIncludes(verification, "swift test && swift build", "swift package verification");
  assertIncludes(verification, "BuildWindowsExpectedOutputAssertionCommand", "windows stdout assertion");
  assertIncludes(verification, "BuildWindowsStructuredHtmlVerificationCommand", "windows html verification");
  assertIncludes(verification, "BuildPlaywrightProjectSmokeCommand", "playwright browser smoke");
  assertIncludes(verification, "playwright smoke ok", "playwright smoke success marker");
  assertIncludes(verification, "TryBuildDotnetProjectVerificationCommand", "dotnet project verification helper");
  assertIncludes(verification, "dotnet restore", "dotnet restore verification");
  assertIncludes(verification, "TryBuildJavaProjectVerificationCommand", "java project verification helper");
  assertIncludes(verification, "TryBuildKotlinProjectVerificationCommand", "kotlin project verification helper");
  assertIncludes(verification, "TryBuildNativeProjectVerificationCommand", "native project verification helper");
  assertIncludes(verification, "cmake -S . -B build", "cmake verification");
  assertIncludes(verification, "make &&", "make verification");
  assertIncludes(verification, "cd /d", "windows cd support");
  assertIncludes(verification, "where {program}", "windows program detection");
  assertIncludes(verification, "gradlew.bat", "windows gradle wrapper");
  assertIncludes(verification, "set \\\"__omni_py=.venv\\\\Scripts\\\\python.exe\\\"", "windows python runner");
  assertIncludes(verification, "for /r build %f in (*.exe)", "windows native executable discovery");
  assertNotIncludes(verification, "interactive python game must avoid third-party packages", "no dependency-free failure");

  assertIncludes(quality, "EvaluateCodingQualityGate", "quality gate evaluator");
  assertIncludes(quality, "quality_failed", "quality failed status");
  assertIncludes(codingWorkerSelectionPolicy, "SelectBest", "best worker selection through policy");
  assertIncludes(quality, "테트리스 요구사항 누락", "tetris requirement failure");
  assertIncludes(quality, "TODO/placeholder/미구현", "dummy implementation gate");

  assertIncludes(profiles, "OMNI_HEADLESS_TEST=1", "headless prompt rule");
  assertIncludes(profiles, "Pygame을 요구하거나 적합하면 pygame 기반 구현을 우선하라", "pygame prompt rule");
  assertIncludes(profiles, "10x20 보드", "tetris prompt rule");
  assertIncludes(profiles, "React/Vite 요청은 package.json", "react vite prompt rule");
  assertIncludes(profiles, "프로젝트 프로파일은", "project profile prompt rule");
  assertIncludes(profiles, "case \"typescript\"", "typescript prompt rule");
  assertIncludes(profiles, "case \"react-vite\"", "react vite language prompt rule");
  assertIncludes(profiles, "lang is \"html\" or \"css\" or \"javascript\" or \"typescript\" or \"react-vite\"", "frontend language expansion");
  assertIncludes(profiles, "case \"go\"", "go prompt rule");
  assertIncludes(profiles, "case \"rust\"", "rust prompt rule");
  assertIncludes(profiles, "case \"php\"", "php prompt rule");
  assertIncludes(profiles, "case \"ruby\"", "ruby prompt rule");
  assertIncludes(profiles, "case \"swift\"", "swift prompt rule");
  assertIncludes(profiles, "C#은 .csproj가 필요하면 생성", "csharp prompt rule");
  assertIncludes(profiles, "Java 단일 파일 요청은 Main.java", "java prompt rule");
  assertIncludes(profiles, "Makefile 또는 CMakeLists.txt", "native prompt rule");
  assertIncludes(profiles, "Bash/CLI 요청은 set -euo pipefail", "bash prompt rule");
  assertNotIncludes(profiles, "requirements.txt, pip install 지시를 출력하거나 생성하지 말라", "no dependency ban prompt");

  assertNotIncludes(codingFallbackPolicy, "이번 수정에서는 pygame, pyglet, arcade 같은 외부 패키지와 pip install 시도를 금지한다", "no repair dependency ban");
  assertIncludes(codingFallbackPolicy, "OMNI_HEADLESS_TEST=1 smoke 실행", "repair headless guidance");
  assertIncludes(utils, "ApplyCodingQualityGateToExecution", "quality gate applied after verification");
  assertIncludes(utils, "ResolveMaxCodingRepairPasses", "dynamic repair pass count");
  assertIncludes(codingLanguagePolicy, "\"\" or \"auto\" => \"auto\"", "auto language preserved");
  assertIncludes(utils, "CodingFallbackDecisionPolicy.ShouldPreferFileBundleFallback", "bundle fallback delegates to decision policy");
  assertIncludes(codingFallbackDecisionPolicy, "projectPrefersMultiFile", "bundle fallback defaults to project profile");
  assertIncludes(codingFallbackPolicy, "일반 코딩 요청은 여러 파일 프로젝트 구조를 우선하라", "bundle prompt multi-file default");
  assertIncludes(codingFallbackPolicy, "projectProfile", "bundle schema includes project profile");
  assertIncludes(codingLanguagePolicy, "\"react\" or \"vite\" or \"react-vite\"", "react vite normalization");
  assertIncludes(codingExecutionSafetyPolicy, "normalizedLanguage is \"javascript\" or \"typescript\" or \"react-vite\"", "frontend deferred command guard");
  assertIncludes(codingLanguagePolicy, "\"golang\" => \"go\"", "go normalization");
  assertIncludes(codingLanguagePolicy, "\"rs\" or \"cargo\" => \"rust\"", "rust normalization");
  assertIncludes(codingFallbackPolicy, "go|rs|php|rb|swift", "extended path regex");
  assertIncludes(codingFallbackPolicy, "[quality-gate] 미충족 항목", "quality repair guidance");
  assertIncludes(codingPromptPolicy, "테스트용 초기화 로그, 빈 함수, 껍데기 UI, placeholder 데이터만으로 완료 처리 금지", "common dummy ban");
  assertIncludes(codingFallbackPolicy, "package.json 의 dependencies/devDependencies", "node dependency repair guidance");
  assertIncludes(codingFallbackPolicy, "csproj PackageReference", "dotnet repair guidance");
  assertIncludes(codingFallbackPolicy, "build.gradle/pom 설정", "jvm repair guidance");
  assertIncludes(codingFallbackPolicy, "Makefile/CMakeLists.txt", "native repair guidance");

  assertIncludes(rerun, "TryBuildNodeProjectRerunCommand", "node project rerun helper");
  assertIncludes(rerun, "TryBuildDotnetProjectRerunCommand", "dotnet project rerun helper");
  assertIncludes(rerun, "TryBuildJvmProjectRerunCommand", "jvm project rerun helper");
  assertIncludes(rerun, "TryBuildNativeProjectRerunCommand", "native project rerun helper");
  assertIncludes(rerun, "BuildCppFallbackCommand", "cpp fallback rerun");
  assertIncludes(rerun, "gradlew.bat", "windows rerun gradle wrapper");
  assertIncludes(rerun, "for /r build %f in (*.exe)", "windows rerun native executable discovery");

  assertIncludes(coding, "CodingWorkerSelectionPolicy.HasQualityFailure", "orchestration quality failure trigger");
  assertIncludes(coding, "CodingWorkerSelectionPolicy.SelectBest", "multi best result selection");
  assertIncludes(coding, "CodingWorkerSelectionPolicy.MergeChangedFilesForBest", "best worker changed files promoted");

  assertIncludes(projectProfiles, "private sealed record CodingProjectProfile", "project profile record");
  assertIncludes(projectProfiles, "ResolveCodingProjectProfile", "project profile resolver");
  assertIncludes(projectProfiles, "python-package", "python package profile");
  assertIncludes(projectProfiles, "react-vite", "react vite profile");
  assertIncludes(projectProfiles, "typescript-node", "typescript node profile");
  assertIncludes(projectProfiles, "go-module", "go module profile");
  assertIncludes(projectProfiles, "rust-cargo", "rust cargo profile");
  assertIncludes(projectProfiles, "csharp-project", "csharp project profile");
  assertIncludes(projectProfiles, "cpp-cmake", "cpp cmake profile");
  assertIncludes(projectProfiles, "IsExplicitSingleFileSimpleTask", "single file gate");

  assertIncludes(dashboardConstants, "[\"typescript\", \"TypeScript\"]", "typescript UI option");
  assertIncludes(dashboardConstants, "[\"react-vite\", \"React/Vite\"]", "react vite UI option");
  assertIncludes(dashboardConstants, "[\"go\", \"Go\"]", "go UI option");
  assertIncludes(dashboardConstants, "[\"rust\", \"Rust\"]", "rust UI option");

  process.stdout.write(JSON.stringify({ ok: true, assertions: 106 }, null, 2) + "\n");
}

main();
