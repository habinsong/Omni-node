import { lstatSync, readFileSync, readlinkSync } from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

const REQUIRED_DIRECTORIES = ["apps", "docs"];
const OPTIONAL_CANONICAL_DIRECTORIES = ["workspace"];
const ROOT_ALIAS_SYMLINKS = [
  { path: "coding", target: "workspace/coding" },
  { path: "runtime", target: "workspace/runtime" },
  { path: "omninode-core", target: "apps/omninode-core" },
  { path: "omninode-dashboard", target: "apps/omninode-dashboard" },
  { path: "omninode-middleware", target: "apps/omninode-middleware" },
  { path: "omninode-sandbox", target: "apps/omninode-sandbox" }
];
const REQUIRED_GITIGNORE_PATTERNS = [
  "node_modules/",
  "output/",
  "workspace/",
  "docs/gemini-retriever-plan/loop-automation/runtime/",
  "apps/.runtime/"
];
const ARTIFACT_PATHS = [
  "node_modules",
  "output",
  "workspace",
  "workspace/.runtime",
  "workspace/runtime",
  "workspace/coding",
  "docs/gemini-retriever-plan/loop-automation/runtime",
  "apps/.runtime"
];

function toAbsolute(relativePath) {
  return path.join(repoRoot, relativePath);
}

function readTrackedFiles(relativePath) {
  const result = spawnSync("git", ["ls-files", "--", relativePath], {
    cwd: repoRoot,
    encoding: "utf8",
    env: process.env,
    maxBuffer: 32 * 1024 * 1024
  });

  if (result.status !== 0) {
    throw new Error(`git ls-files 실패: ${relativePath}`);
  }

  return result.stdout
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function ensureDirectory(relativePath) {
  const stat = lstatSync(toAbsolute(relativePath), { throwIfNoEntry: false });
  if (!stat || !stat.isDirectory()) {
    throw new Error(`필수 canonical 디렉터리가 없거나 디렉터리가 아닙니다: ${relativePath}`);
  }
}

function inspectCanonicalDirectory(relativePath) {
  const stat = lstatSync(toAbsolute(relativePath), { throwIfNoEntry: false });
  return {
    path: relativePath,
    present: !!stat,
    kind: !stat ? "missing" : stat.isDirectory() ? "directory" : stat.isSymbolicLink() ? "symlink" : "file"
  };
}

function ensureAliasTargets() {
  return ROOT_ALIAS_SYMLINKS.map((entry) => {
    const stat = lstatSync(toAbsolute(entry.path), { throwIfNoEntry: false });
    if (!stat) {
      throw new Error(`하위 호환 alias가 없습니다: ${entry.path}`);
    }
    if (!stat.isSymbolicLink()) {
      throw new Error(`하위 호환 alias는 심볼릭 링크여야 합니다: ${entry.path}`);
    }

    const rawTarget = readlinkSync(toAbsolute(entry.path));
    const normalizedTarget = path.normalize(rawTarget);
    const expectedTarget = path.normalize(entry.target);
    if (normalizedTarget !== expectedTarget) {
      throw new Error(`하위 호환 alias 대상이 다릅니다: ${entry.path} -> ${rawTarget} (expected: ${entry.target})`);
    }

    return { path: entry.path, target: normalizedTarget };
  });
}

function ensureGitignorePatterns() {
  const gitignore = readFileSync(toAbsolute(".gitignore"), "utf8");
  const lines = new Set(
    gitignore
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
  );

  const missingPatterns = REQUIRED_GITIGNORE_PATTERNS.filter((pattern) => !lines.has(pattern));
  if (missingPatterns.length > 0) {
    throw new Error(`.gitignore 누락 패턴: ${missingPatterns.join(", ")}`);
  }
}

function ensureArtifactsAreUntracked() {
  const trackedArtifactCounts = {};
  const violations = [];

  for (const relativePath of ARTIFACT_PATHS) {
    const trackedFiles = readTrackedFiles(relativePath);
    trackedArtifactCounts[relativePath] = trackedFiles.length;
    if (trackedFiles.length > 0) {
      violations.push(`${relativePath} (${trackedFiles.length})`);
    }
  }

  if (violations.length > 0) {
    throw new Error(`재생성 가능한 아티팩트가 git 인덱스에 남아 있습니다: ${violations.join(", ")}`);
  }

  return trackedArtifactCounts;
}

function main() {
  REQUIRED_DIRECTORIES.forEach(ensureDirectory);
  const canonicalDirectories = OPTIONAL_CANONICAL_DIRECTORIES.map(inspectCanonicalDirectory);
  const aliasSymlinks = ensureAliasTargets();
  ensureGitignorePatterns();
  const trackedArtifactCounts = ensureArtifactsAreUntracked();

  console.log(JSON.stringify({
    ok: true,
    canonicalDirectories,
    aliasSymlinks,
    trackedArtifactCounts
  }, null, 2));
}

main();
