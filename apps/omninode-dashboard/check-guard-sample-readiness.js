#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const REQUIRED_CHANNELS = ["chat", "coding", "telegram"];
const EXPECTED_SCHEMA_VERSION = "guard_retry_timeline.v1";
const DEFAULT_REQUIRED_TOTAL = 30;
const DEFAULT_REQUIRED_PER_CHANNEL = 1;

function parseArgs(argv) {
  let statePath = "";
  let writePath = "";
  let requiredTotal = DEFAULT_REQUIRED_TOTAL;
  let requiredPerChannel = DEFAULT_REQUIRED_PER_CHANNEL;
  let allowSeed = false;
  let enforceReady = false;

  for (let i = 2; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === "--state-path") {
      const next = argv[i + 1];
      assert(next, "--state-path 옵션에는 파일 경로가 필요합니다.");
      statePath = path.resolve(next);
      i += 1;
      continue;
    }
    if (token === "--write") {
      const next = argv[i + 1];
      assert(next, "--write 옵션에는 출력 파일 경로가 필요합니다.");
      writePath = path.resolve(next);
      i += 1;
      continue;
    }
    if (token === "--required-total") {
      const next = argv[i + 1];
      assert(next, "--required-total 옵션에는 0 이상의 숫자가 필요합니다.");
      const parsed = Number(next);
      assert(Number.isFinite(parsed) && parsed >= 0, "--required-total 옵션은 0 이상의 숫자여야 합니다.");
      requiredTotal = Math.floor(parsed);
      i += 1;
      continue;
    }
    if (token === "--required-per-channel") {
      const next = argv[i + 1];
      assert(next, "--required-per-channel 옵션에는 0 이상의 숫자가 필요합니다.");
      const parsed = Number(next);
      assert(Number.isFinite(parsed) && parsed >= 0, "--required-per-channel 옵션은 0 이상의 숫자여야 합니다.");
      requiredPerChannel = Math.floor(parsed);
      i += 1;
      continue;
    }
    if (token === "--allow-seed") {
      allowSeed = true;
      continue;
    }
    if (token === "--enforce-ready") {
      enforceReady = true;
      continue;
    }
    throw new Error(`지원하지 않는 인자: ${token}`);
  }

  return {
    statePath,
    writePath,
    requiredTotal,
    requiredPerChannel,
    allowSeed,
    enforceReady
  };
}

function resolveStatePath(explicitPath) {
  if (explicitPath) {
    return explicitPath;
  }

  const envPath = `${process.env.OMNINODE_GUARD_RETRY_TIMELINE_STATE_PATH || ""}`.trim();
  if (envPath) {
    return path.resolve(envPath);
  }

  return path.join(os.tmpdir(), "omninode_guard_retry_timeline.json");
}

function readJson(filePath, label) {
  assert(fs.existsSync(filePath), `${label} 파일이 없습니다: ${filePath}`);
  const text = fs.readFileSync(filePath, "utf8");
  try {
    return JSON.parse(text);
  } catch (error) {
    throw new Error(`${label} JSON 파싱 실패: ${error.message}`);
  }
}

function normalizeChannel(value) {
  const token = `${value || ""}`.trim().toLowerCase();
  if (REQUIRED_CHANNELS.includes(token)) {
    return token;
  }
  return "";
}

function isSeedEntry(entry) {
  const id = `${entry && entry.id ? entry.id : ""}`.trim().toLowerCase();
  return id.startsWith("seed-");
}

function summarize(payload, policy, statePath) {
  assert(payload && typeof payload === "object" && !Array.isArray(payload), "state JSON 루트는 객체여야 합니다.");
  const entries = Array.isArray(payload.entries) ? payload.entries : [];

  const byChannel = REQUIRED_CHANNELS.reduce((acc, channel) => {
    acc[channel] = 0;
    return acc;
  }, {});

  let consideredEntries = 0;
  let ignoredSeedEntries = 0;
  let ignoredNonOperationalChannels = 0;
  let invalidEntries = 0;
  let invalidCapturedAtEntries = 0;
  let earliestCapturedAtMs = Number.POSITIVE_INFINITY;
  let latestCapturedAtMs = Number.NEGATIVE_INFINITY;

  for (const entry of entries) {
    if (!entry || typeof entry !== "object") {
      invalidEntries += 1;
      continue;
    }

    const channel = normalizeChannel(entry.channel);
    if (!channel) {
      ignoredNonOperationalChannels += 1;
      continue;
    }

    if (!policy.allowSeed && isSeedEntry(entry)) {
      ignoredSeedEntries += 1;
      continue;
    }

    const capturedAtMs = Date.parse(`${entry.capturedAt || ""}`);
    if (!Number.isFinite(capturedAtMs)) {
      invalidCapturedAtEntries += 1;
      continue;
    }

    consideredEntries += 1;
    byChannel[channel] += 1;
    if (capturedAtMs < earliestCapturedAtMs) {
      earliestCapturedAtMs = capturedAtMs;
    }
    if (capturedAtMs > latestCapturedAtMs) {
      latestCapturedAtMs = capturedAtMs;
    }
  }

  const totalSamples = REQUIRED_CHANNELS.reduce((sum, channel) => sum + byChannel[channel], 0);
  const channelShortfalls = REQUIRED_CHANNELS.reduce((acc, channel) => {
    const shortfall = Math.max(0, policy.requiredPerChannel - byChannel[channel]);
    acc[channel] = shortfall;
    return acc;
  }, {});
  const missingChannels = REQUIRED_CHANNELS.filter((channel) => channelShortfalls[channel] > 0);
  const totalShortfall = Math.max(0, policy.requiredTotal - totalSamples);

  const reasons = [];
  if (totalShortfall > 0) {
    reasons.push(`total_shortfall:${totalShortfall}`);
  }
  if (missingChannels.length > 0) {
    reasons.push(`channel_shortfall:${missingChannels.join(",")}`);
  }

  const ready = totalShortfall === 0 && missingChannels.length === 0;

  return {
    ok: true,
    generatedAtUtc: new Date().toISOString(),
    schemaVersion: `${payload.schemaVersion || ""}`,
    schemaVersionMatched: `${payload.schemaVersion || ""}` === EXPECTED_SCHEMA_VERSION,
    policy: {
      requiredTotalSamples: policy.requiredTotal,
      requiredPerChannelSamples: policy.requiredPerChannel,
      requiredChannels: [...REQUIRED_CHANNELS],
      excludeSeedEntries: !policy.allowSeed
    },
    state: {
      path: statePath,
      totalEntries: entries.length,
      consideredEntries,
      ignoredSeedEntries,
      ignoredNonOperationalChannels,
      invalidEntries,
      invalidCapturedAtEntries,
      earliestCapturedAtUtc: Number.isFinite(earliestCapturedAtMs) ? new Date(earliestCapturedAtMs).toISOString() : null,
      latestCapturedAtUtc: Number.isFinite(latestCapturedAtMs) ? new Date(latestCapturedAtMs).toISOString() : null
    },
    samples: {
      total: totalSamples,
      byChannel
    },
    readiness: {
      ready,
      reasons,
      totalShortfall,
      channelShortfalls
    }
  };
}

function run() {
  const args = parseArgs(process.argv);
  const statePath = resolveStatePath(args.statePath);
  const payload = readJson(statePath, "guard_retry_timeline state");
  const report = summarize(
    payload,
    {
      requiredTotal: args.requiredTotal,
      requiredPerChannel: args.requiredPerChannel,
      allowSeed: args.allowSeed
    },
    statePath
  );

  if (args.writePath) {
    fs.mkdirSync(path.dirname(args.writePath), { recursive: true });
    fs.writeFileSync(args.writePath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  }

  console.log(JSON.stringify(report, null, 2));

  if (args.enforceReady) {
    assert.equal(
      report.readiness.ready,
      true,
      [
        "guard 샘플 누적 기준 미충족",
        `(total=${report.samples.total}/${report.policy.requiredTotalSamples},`,
        `chat=${report.samples.byChannel.chat},`,
        `coding=${report.samples.byChannel.coding},`,
        `telegram=${report.samples.byChannel.telegram})`
      ].join(" ")
    );
  }
}

run();
