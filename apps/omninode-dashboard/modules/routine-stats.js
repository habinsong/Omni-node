import { normalizeRoutineExecutionModeValue } from "./routine-utils.js";

const ERROR_PATTERN = /error|fail|timeout|blocked/i;

/**
 * Single-pass aggregation of routine list stats.
 * Replaces 4 separate `routines.filter(...)` calls and centralizes the
 * "what counts as failure / scheduled / browser-agent" rules.
 */
export function computeRoutineStats(routines) {
  const list = Array.isArray(routines) ? routines : [];
  let enabled = 0;
  let browserAgent = 0;
  let failed = 0;
  let scheduled = 0;

  for (const item of list) {
    if (!item) continue;
    if (item.enabled) enabled += 1;
    const mode = normalizeRoutineExecutionModeValue(item.resolvedExecutionMode || item.executionMode);
    if (mode === "browser_agent") browserAgent += 1;
    if (ERROR_PATTERN.test(`${item.lastStatus || ""}`)) failed += 1;
    if (`${item.nextRunLocal || ""}`.trim().length > 0) scheduled += 1;
  }

  return {
    total: list.length,
    enabled,
    disabled: Math.max(0, list.length - enabled),
    browserAgent,
    failed,
    scheduled
  };
}
