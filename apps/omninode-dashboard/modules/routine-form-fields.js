import {
  normalizeRoutineNotifyPolicy,
  normalizeRoutineNotifyTelegram
} from "./routine-utils.js";

const RETRY_MAX = 5;
const RETRY_DELAY_MAX = 300;

const NOTIFY_POLICY_OPTIONS = [
  ["always", "항상"],
  ["on_change", "변경 시만"],
  ["error_only", "오류 시만"],
  ["never", "보내지 않음"]
];

function clampNumber(value, min, max, fallback) {
  const num = Number(value);
  if (!Number.isFinite(num)) return fallback;
  return Math.min(max, Math.max(min, num));
}

/**
 * Renders the retry + telegram-response + notify-policy field group.
 * Used identically in both create and edit forms — single source of truth.
 */
export function renderRoutineRetryAndNotifyFields(props) {
  const { e, form, formType, patchRoutineForm } = props;
  const retries = clampNumber(form.maxRetries ?? 1, 0, RETRY_MAX, 1);
  const retryDelay = clampNumber(form.retryDelaySeconds ?? 15, 0, RETRY_DELAY_MAX, 15);
  const notifyTelegram = normalizeRoutineNotifyTelegram(form.notifyTelegram, true);
  const notifyPolicy = normalizeRoutineNotifyPolicy(form.notifyPolicy, "always");

  return e(
    "div",
    { className: "routine-form-grid routine-retry-notify-grid" },
    e("label", { className: "routine-field" },
      e("span", { className: "routine-field-label" }, "실패 재시도"),
      e("input", {
        className: "input",
        type: "number",
        min: 0,
        max: RETRY_MAX,
        value: `${retries}`,
        onChange: (event) => patchRoutineForm(formType, { maxRetries: Number(event.target.value) || 0 })
      })
    ),
    e("label", { className: "routine-field" },
      e("span", { className: "routine-field-label" }, "재시도 간격(초)"),
      e("input", {
        className: "input",
        type: "number",
        min: 0,
        max: RETRY_DELAY_MAX,
        value: `${retryDelay}`,
        onChange: (event) => patchRoutineForm(formType, { retryDelaySeconds: Number(event.target.value) || 0 })
      })
    ),
    e("label", { className: "routine-field" },
      e("span", { className: "routine-field-label" }, "텔레그램 봇 응답"),
      e("select", {
        className: "input",
        value: notifyTelegram ? "on" : "off",
        onChange: (event) => patchRoutineForm(formType, { notifyTelegram: event.target.value === "on" })
      },
      e("option", { value: "on" }, "켜기"),
      e("option", { value: "off" }, "끄기"))
    ),
    e("label", { className: "routine-field" },
      e("span", { className: "routine-field-label" }, "텔레그램 알림"),
      e("select", {
        className: "input",
        value: notifyPolicy,
        onChange: (event) => patchRoutineForm(formType, { notifyPolicy: event.target.value })
      },
      ...NOTIFY_POLICY_OPTIONS.map(([value, label]) => e("option", { key: value, value }, label)))
    )
  );
}
