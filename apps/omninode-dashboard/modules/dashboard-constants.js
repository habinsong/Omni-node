export const CHAT_MODES = [
  { key: "single", label: "단일 모델" },
  { key: "orchestration", label: "오케스트레이션" },
  { key: "multi", label: "다중 LLM" }
];

export const CODING_MODES = [
  { key: "single", label: "단일 코딩" },
  { key: "orchestration", label: "오케스트레이션 코딩" },
  { key: "multi", label: "다중 코딩" }
];

export const CODING_LANGUAGES = [
  ["auto", "자동"],
  ["python", "Python"],
  ["javascript", "JavaScript"],
  ["c", "C"],
  ["cpp", "C++"],
  ["csharp", "C#"],
  ["java", "Java"],
  ["kotlin", "Kotlin"],
  ["html", "HTML"],
  ["css", "CSS"],
  ["bash", "Bash"]
];

export const ROUTINE_WEEKDAY_OPTIONS = [
  { value: 1, label: "월" },
  { value: 2, label: "화" },
  { value: 3, label: "수" },
  { value: 4, label: "목" },
  { value: 5, label: "금" },
  { value: 6, label: "토" },
  { value: 0, label: "일" }
];

export const DEFAULT_ROUTINE_AGENT_PROVIDER = "codex";
export const DEFAULT_ROUTINE_AGENT_MODEL = "gpt-5.4";
export const DEFAULT_CODEX_MODEL = "gpt-5.4";

export const CODEX_MODEL_CHOICES = [
  { id: DEFAULT_CODEX_MODEL, label: DEFAULT_CODEX_MODEL },
  { id: "gpt-5.5", label: "gpt-5.5" },
  { id: "gpt-5.4-mini", label: "gpt-5.4-mini" },
  { id: "gpt-5.3-codex", label: "gpt-5.3-codex" },
  { id: "gpt-5.2-codex", label: "gpt-5.2-codex" },
  { id: "gpt-5.2", label: "gpt-5.2" }
];

export const DEFAULT_MOBILE_PANES = {
  chat: "thread",
  coding: "thread",
  routine: "overview",
  logic: "canvas",
  settings: "auth"
};
