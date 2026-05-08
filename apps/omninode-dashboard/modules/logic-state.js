import {
  CODING_LANGUAGES,
  DEFAULT_CODEX_MODEL
} from "./dashboard-constants.js";

export const LOGIC_NODE_LIBRARY = [
  {
    key: "flow",
    label: "흐름",
    items: [
      ["start", "시작"],
      ["end", "끝내기"],
      ["output", "출력"],
      ["if", "조건 갈래"],
      ["delay", "잠깐 기다리기"],
      ["parallel_split", "동시에 시작"],
      ["parallel_join", "모두 끝날 때까지 기다리기"],
      ["set_var", "값 기억하기"],
      ["template", "문장 만들기"]
    ]
  },
  {
    key: "ai",
    label: "문답/코딩",
    items: [
      ["chat_single", "한 모델로 답변"],
      ["chat_orchestration", "역할 나눠 답변"],
      ["chat_multi", "여러 답변 비교"],
      ["coding_single", "한 모델로 구현"],
      ["coding_orchestration", "역할 나눠 구현"],
      ["coding_multi", "구현안 비교"]
    ]
  },
  {
    key: "automation",
    label: "자동화",
    items: [
      ["routine_run", "루틴 실행"]
    ]
  },
  {
    key: "data",
    label: "데이터/도구",
    items: [
      ["memory_search", "기억에서 찾기"],
      ["memory_get", "메모리 문서 읽기"],
      ["web_search", "웹에서 찾기"],
      ["web_fetch", "웹페이지 읽기"],
      ["file_read", "파일 읽기"],
      ["file_write", "파일 저장"]
    ]
  },
  {
    key: "ops",
    label: "운영",
    items: [
      ["session_list", "열린 세션 보기"],
      ["session_spawn", "새 작업 세션"],
      ["session_send", "세션에 보내기"],
      ["cron_status", "예약 작업 상태"],
      ["cron_run", "예약 작업 실행"],
      ["browser_execute", "브라우저 제어"],
      ["canvas_execute", "캔버스 제어"],
      ["nodes_pending", "승인 대기 보기"],
      ["nodes_invoke", "노드 명령 보내기"],
      ["telegram_stub", "텔레그램 흉내"]
    ]
  }
];

export const LOGIC_NODE_DEFAULT_SIZE = Object.freeze({
  width: 168,
  height: 126
});

export const LOGIC_NODE_MIN_SIZE = Object.freeze({
  width: 148,
  height: 104
});

export const LOGIC_NODE_MAX_SIZE = Object.freeze({
  width: 420,
  height: 320
});

const LOGIC_NODE_MIN_SIZE_BY_TYPE = Object.freeze({
  start: { width: 176, height: 186 },
  end: { width: 184, height: 156 },
  output: { width: 280, height: 260 },
  if: { width: 238, height: 198 },
  delay: { width: 190, height: 186 },
  parallel_split: { width: 198, height: 156 },
  parallel_join: { width: 198, height: 156 },
  set_var: { width: 228, height: 186 },
  template: { width: 232, height: 176 },
  chat_single: { width: 272, height: 228 },
  chat_orchestration: { width: 286, height: 212 },
  chat_multi: { width: 286, height: 212 },
  coding_single: { width: 272, height: 198 },
  coding_orchestration: { width: 286, height: 212 },
  coding_multi: { width: 286, height: 212 },
  routine_run: { width: 236, height: 172 },
  memory_search: { width: 232, height: 172 },
  memory_get: { width: 220, height: 168 },
  web_search: { width: 232, height: 172 },
  web_fetch: { width: 246, height: 198 },
  file_read: { width: 220, height: 168 },
  file_write: { width: 220, height: 168 },
  session_list: { width: 228, height: 166 },
  session_spawn: { width: 246, height: 186 },
  session_send: { width: 236, height: 196 },
  cron_status: { width: 212, height: 154 },
  cron_run: { width: 220, height: 168 },
  browser_execute: { width: 248, height: 198 },
  canvas_execute: { width: 248, height: 198 },
  nodes_pending: { width: 216, height: 156 },
  nodes_invoke: { width: 248, height: 182 },
  telegram_stub: { width: 228, height: 168 }
});

const DEFAULT_NODE_TITLES = Object.fromEntries(
  LOGIC_NODE_LIBRARY.flatMap((group) => group.items.map(([type, label]) => [type, label]))
);

const LOGIC_NONE_MODEL = "none";
const DEFAULT_GROQ_SINGLE_MODEL = "meta-llama/llama-4-scout-17b-16e-instruct";
const DEFAULT_GROQ_WORKER_MODEL = "openai/gpt-oss-120b";
const DEFAULT_GEMINI_MODEL = "gemini-3-flash-preview";
const DEFAULT_CEREBRAS_MODEL = "gpt-oss-120b";
const DEFAULT_NVIDIA_MODEL = "meta/llama-3.1-70b-instruct";

const BOOLEAN_OPTIONS = [
  { value: "true", label: "예" },
  { value: "false", label: "아니오" }
];

const ENABLED_OPTIONS = [
  { value: "true", label: "사용" },
  { value: "false", label: "사용 안 함" }
];

const IF_OPERATOR_OPTIONS = [
  { value: "equals", label: "같음" },
  { value: "not_equals", label: "다름" },
  { value: "contains", label: "포함" },
  { value: "not_contains", label: "포함 안 함" },
  { value: "starts_with", label: "시작 일치" },
  { value: "ends_with", label: "끝 일치" },
  { value: "gt", label: "크다" },
  { value: "gte", label: "크거나 같다" },
  { value: "lt", label: "작다" },
  { value: "lte", label: "작거나 같다" },
  { value: "is_truthy", label: "참으로 평가" },
  { value: "is_falsy", label: "거짓으로 평가" }
];

const CHAT_PROVIDER_OPTIONS = [
  { value: "groq", label: "Groq" },
  { value: "gemini", label: "Gemini" },
  { value: "cerebras", label: "Cerebras" },
  { value: "nvidia", label: "NVIDIA NIM" },
  { value: "copilot", label: "Copilot" },
  { value: "codex", label: "Codex" }
];

const ORCHESTRATION_PROVIDER_OPTIONS = [
  { value: "auto", label: "AUTO" },
  ...CHAT_PROVIDER_OPTIONS
];

const SUMMARY_PROVIDER_OPTIONS = [
  { value: "auto", label: "AUTO" },
  { value: "groq", label: "Groq" },
  { value: "gemini", label: "Gemini" },
  { value: "cerebras", label: "Cerebras" },
  { value: "nvidia", label: "NVIDIA NIM" },
  { value: "copilot", label: "Copilot" },
  { value: "codex", label: "Codex" }
];

const WEB_FRESHNESS_OPTIONS = [
  { value: "", label: "기본" },
  { value: "day", label: "1일 이내" },
  { value: "week", label: "1주 이내" },
  { value: "month", label: "1개월 이내" }
];

const WEB_EXTRACT_MODE_OPTIONS = [
  { value: "", label: "자동" },
  { value: "article", label: "본문" },
  { value: "text", label: "텍스트" },
  { value: "markdown", label: "마크다운" },
  { value: "html", label: "HTML" }
];

const SESSION_RUNTIME_OPTIONS = [
  { value: "acp", label: "ACP" },
  { value: "subagent", label: "Subagent" }
];

const SESSION_MODE_OPTIONS = [
  { value: "run", label: "Run" },
  { value: "session", label: "Session" }
];

const CRON_RUN_MODE_OPTIONS = [
  { value: "manual", label: "즉시 실행" },
  { value: "due", label: "예정된 작업만" }
];

const BROWSER_ACTION_OPTIONS = [
  { value: "status", label: "상태" },
  { value: "tabs", label: "탭 목록" },
  { value: "start", label: "브라우저 시작" },
  { value: "stop", label: "브라우저 중지" },
  { value: "navigate", label: "현재 탭 이동" },
  { value: "open", label: "새 탭 열기" },
  { value: "focus", label: "탭 포커스" },
  { value: "close", label: "탭 닫기" }
];

const CANVAS_ACTION_OPTIONS = [
  { value: "status", label: "상태" },
  { value: "present", label: "캔버스 표시" },
  { value: "hide", label: "캔버스 숨김" },
  { value: "navigate", label: "URL 이동" },
  { value: "eval", label: "JavaScript 실행" },
  { value: "snapshot", label: "스냅샷" },
  { value: "a2ui_push", label: "A2UI push" },
  { value: "a2ui_reset", label: "A2UI reset" }
];

const CANVAS_OUTPUT_FORMAT_OPTIONS = [
  { value: "markdown", label: "Markdown" },
  { value: "json", label: "JSON" },
  { value: "png", label: "PNG" },
  { value: "jpeg", label: "JPEG" }
];

const NODES_ACTION_OPTIONS = [
  { value: "invoke", label: "명령 호출" },
  { value: "notify", label: "시스템 알림" },
  { value: "describe", label: "노드 설명" },
  { value: "status", label: "상태" },
  { value: "pending", label: "대기 목록" },
  { value: "approve", label: "요청 승인" },
  { value: "reject", label: "요청 거절" }
];

const PRIORITY_OPTIONS = [
  { value: "passive", label: "수동" },
  { value: "active", label: "활성" },
  { value: "timeSensitive", label: "긴급" }
];

const DELIVERY_OPTIONS = [
  { value: "auto", label: "자동" },
  { value: "system", label: "시스템" },
  { value: "overlay", label: "오버레이" }
];

const CODING_LANGUAGE_OPTIONS = CODING_LANGUAGES.map(([value, label]) => ({ value, label }));

function section(title, description = "") {
  return {
    kind: "section",
    title,
    description
  };
}

function field(key, label, control, extras = {}) {
  return {
    kind: "field",
    key,
    label,
    control,
    ...extras
  };
}

const LOGIC_NODE_INSPECTOR_DEFINITIONS = {
  start: {
    description: "흐름이 여기서 시작됩니다. 입력값을 적으면 첫 번째 단계에 그대로 넘깁니다.",
    example: "예: 분석할 주문 번호, 질문, 또는 처리할 텍스트",
    outputs: [
      "다음 노드로 넘길 원본 입력 (`text`)",
      "입력값 복사본 (`data.input`)"
    ],
    fields: [
      field("input", "시작 입력", "textarea", {
        rows: 4,
        placeholder: "흐름 실행 시 시작 노드에 전달할 입력값을 적으세요.\n예: 오늘 회의 내용을 요약해 줘."
      })
    ]
  },
  end: {
    description: "마지막 결과를 사람이 읽기 좋은 형태로 정리합니다. 비워 두면 바로 앞 단계 결과를 그대로 씁니다.",
    example: "예: 여러 단계 결과를 한 문단 요약으로 마무리",
    outputs: [
      "마지막에 보여줄 문장 (`text`)",
      "저장된 마무리 결과 (`data.result`)"
    ],
    fields: [
      field("result", "마무리 문장", "textarea", {
        rows: 4,
        placeholder: "회의 요약:\n1. 오늘 결정한 내용\n2. 다음 할 일\n3. 담당자"
      })
    ]
  },
  output: {
    description: "앞 단계에서 넘어온 출력 텍스트를 생략 없이 확인합니다. 비워 두면 연결된 입력을 그대로 보여줍니다.",
    example: "예: 최종 답변 전문, 생성된 코드 전문, 수집된 결과 전문을 그대로 표시",
    outputs: [
      "화면에 보여줄 출력 전문 (`text`)",
      "저장된 출력 전문 (`data.result`)"
    ],
    fields: [
      field("result", "출력 텍스트", "textarea", {
        rows: 6,
        placeholder: "비워 두면 연결된 입력 텍스트를 그대로 출력합니다."
      })
    ]
  },
  if: {
    description: "값을 비교해서 두 갈래 중 어디로 보낼지 정합니다.",
    example: "예: 검토 상태가 '승인'이면 발송, 아니면 보류",
    outputs: [
      "선택된 갈래 (`data.branch`)",
      "비교에 사용한 실제 값 (`data.left/right/operator`)"
    ],
    fields: [
      field("leftRef", "비교할 값", "text", {
        placeholder: "예: 승인 상태, 결제 결과, 재고 여부"
      }),
      field("operator", "연산자", "select", {
        options: IF_OPERATOR_OPTIONS
      }),
      field("rightValue", "비교 대상", "text", {
        placeholder: "승인"
      })
    ]
  },
  delay: {
    description: "잠깐 기다린 뒤 다음 단계를 실행합니다. 초 단위나 밀리초 단위 중 하나를 쓰면 됩니다.",
    example: "예: 10초 기다렸다가 상태를 다시 조회",
    outputs: [
      "실제로 기다린 시간 (`data.milliseconds`)"
    ],
    fields: [
      field("seconds", "지연 초", "number", {
        min: "0",
        step: "1",
        placeholder: "10"
      }),
      field("milliseconds", "지연 밀리초", "number", {
        min: "0",
        step: "100",
        placeholder: "10000"
      })
    ]
  },
  parallel_split: {
    description: "같은 입력을 여러 갈래로 동시에 보내고 싶을 때 씁니다.",
    example: "예: 같은 문의를 요약, 번역, 키워드 추출로 동시에 보냄",
    outputs: [
      "같은 입력을 여러 갈래로 전달한 결과 (`text`)"
    ],
    fields: []
  },
  parallel_join: {
    description: "앞선 갈래들이 전부 끝날 때까지 기다렸다가 다음 단계로 넘어갑니다.",
    example: "예: 요약·번역·검토가 모두 끝난 뒤 한 번에 모아서 마무리",
    outputs: [
      "갈래를 다시 모은 뒤 넘기는 결과 (`text`)"
    ],
    fields: []
  },
  set_var: {
    description: "나중 단계에서 다시 쓸 값을 이름 붙여 저장합니다.",
    example: "예: 고객 이름을 `customerName`으로 저장",
    outputs: [
      "저장한 이름 (`data.name`)",
      "저장한 값 (`data.value`)"
    ],
    fields: [
      field("name", "변수 이름", "text", {
        placeholder: "customerName"
      }),
      field("value", "값", "textarea", {
        rows: 4,
        placeholder: "예: 홍길동, 주문번호 2026-0313, 검토 완료"
      })
    ]
  },
  template: {
    description: "여러 값들을 끼워 넣어 한 문장이나 문단을 만듭니다.",
    example: "예: 고객 이름과 주문 상태를 넣어 안내 문구 만들기",
    outputs: [
      "완성된 문장 (`text`)",
      "같은 결과 복사본 (`data.rendered`)"
    ],
    fields: [
      field("template", "문장 초안", "textarea", {
        rows: 5,
        placeholder: "안녕하세요 고객님. 주문 상태를 안내드릴게요.\n현재 상태: 배송 준비 중\n예상 도착: 내일 오후"
      })
    ]
  },
  chat_single: {
    description: "한 모델에게 바로 질문해서 답을 받습니다. 가장 단순한 답변 노드입니다.",
    example: "예: 오늘 오전 회의 내용을 팀 공지용으로 5줄 요약",
    outputs: [
      "모델이 작성한 답변 (`text`)",
      "이번 실행에만 쓰는 대화 ID (`conversationId`)"
    ],
    fields: [
      section("무엇을 물을지", "질문이나 지시문을 적습니다."),
      field("input", "질문 / 프롬프트", "textarea", {
        rows: 5,
        placeholder: "오늘 오전 회의 내용을 팀 공지용으로 5줄로 요약해 줘."
      }),
      field("memoryNotes", "메모리 노트", "text", {
        placeholder: "project-summary, release-checklist"
      }),
      section("어떤 모델로 할지", "공급자와 모델을 직접 고릅니다."),
      field("provider", "공급자", "select", {
        options: CHAT_PROVIDER_OPTIONS
      }),
      field("model", "모델", "provider-model", {
        providerKey: "provider"
      }),
      section("참고 자료", "웹 검색이나 고정 URL을 같이 붙일 수 있습니다."),
      field("webSearchEnabled", "웹 참고 사용", "select", {
        options: ENABLED_OPTIONS
      }),
      field("webUrls", "같이 볼 URL", "textarea", {
        rows: 4,
        placeholder: "https://openai.com\nhttps://developer.mozilla.org"
      })
    ]
  },
  chat_orchestration: {
    description: "한 번에 끝내지 않고, 정리와 검토처럼 역할을 나눠 답을 만듭니다.",
    example: "예: 먼저 초안을 만들고, 빠진 점을 다시 검토해서 답변 정리",
    outputs: [
      "마지막에 묶인 답변 (`text`)",
      "실제로 선택된 모델 정보 (`data.provider/model/route`)"
    ],
    fields: [
      section("무엇을 부탁할지", "질문이나 요청 내용을 적습니다."),
      field("input", "질문 / 프롬프트", "textarea", {
        rows: 5,
        placeholder: "새 기능 소개 문안을 먼저 쓰고, 빠진 위험 요소가 있으면 같이 적어 줘."
      }),
      field("memoryNotes", "메모리 노트", "text", {
        placeholder: "product-brief, faq"
      }),
      section("중심 모델", "AUTO로 두면 요청을 보고 가장 맞는 모델을 고릅니다."),
      field("provider", "중심 공급자", "select", {
        options: ORCHESTRATION_PROVIDER_OPTIONS
      }),
      field("model", "중심 모델", "provider-model", {
        providerKey: "provider",
        autoLabel: "AUTO"
      }),
      section("보조 모델", "역할을 나눠 쓸 모델을 고릅니다."),
      field("groqModel", "Groq 보조 모델", "catalog-select", {
        catalogKey: "groqWorkerOptions"
      }),
      field("geminiModel", "Gemini 보조 모델", "catalog-select", {
        catalogKey: "geminiWorkerOptions"
      }),
      field("cerebrasModel", "Cerebras 보조 모델", "catalog-select", {
        catalogKey: "cerebrasWorkerOptions"
      }),
      field("nvidiaModel", "NVIDIA NIM 보조 모델", "catalog-select", {
        catalogKey: "nvidiaWorkerOptions"
      }),
      field("copilotModel", "Copilot 보조 모델", "catalog-select", {
        catalogKey: "copilotWorkerOptions"
      }),
      field("codexModel", "Codex 보조 모델", "catalog-select", {
        catalogKey: "codexWorkerOptions"
      }),
      section("참고 자료", "검색 여부와 같이 볼 URL을 정합니다."),
      field("webSearchEnabled", "웹 참고 사용", "select", {
        options: ENABLED_OPTIONS
      }),
      field("webUrls", "같이 볼 URL", "textarea", {
        rows: 4,
        placeholder: "https://example.com/guide\nhttps://example.com/faq"
      })
    ]
  },
  chat_multi: {
    description: "여러 모델 답변을 나란히 받아 보고, 마지막에 공통 결론만 정리합니다.",
    example: "예: 같은 질문을 여러 모델에 던져 보고 가장 일관된 결론만 추리기",
    outputs: [
      "마지막 비교 요약 (`text`)",
      "요약에 사용한 공급자 (`data.requestedSummaryProvider`)"
    ],
    fields: [
      section("무엇을 비교할지", "같은 질문을 여러 모델에 보냅니다."),
      field("input", "질문 / 프롬프트", "textarea", {
        rows: 5,
        placeholder: "이 오류 로그를 보고 가장 가능성 높은 원인 3가지를 알려 줘."
      }),
      field("memoryNotes", "메모리 노트", "text", {
        placeholder: "incident-history, deployment-notes"
      }),
      section("비교할 모델", "나란히 돌릴 모델을 고릅니다."),
      field("groqModel", "Groq 모델", "catalog-select", {
        catalogKey: "groqWorkerOptions"
      }),
      field("geminiModel", "Gemini 모델", "catalog-select", {
        catalogKey: "geminiWorkerOptions"
      }),
      field("cerebrasModel", "Cerebras 모델", "catalog-select", {
        catalogKey: "cerebrasWorkerOptions"
      }),
      field("nvidiaModel", "NVIDIA NIM 모델", "catalog-select", {
        catalogKey: "nvidiaWorkerOptions"
      }),
      field("copilotModel", "Copilot 모델", "catalog-select", {
        catalogKey: "copilotWorkerOptions"
      }),
      field("codexModel", "Codex 모델", "catalog-select", {
        catalogKey: "codexWorkerOptions"
      }),
      section("마지막 정리", "비교 결과를 한 번에 요약할 공급자를 고릅니다."),
      field("summaryProvider", "요약 공급자", "select", {
        options: SUMMARY_PROVIDER_OPTIONS
      }),
      field("webSearchEnabled", "웹 참고 사용", "select", {
        options: ENABLED_OPTIONS
      }),
      field("webUrls", "같이 볼 URL", "textarea", {
        rows: 4,
        placeholder: "https://status.example.com\nhttps://docs.example.com/api"
      })
    ]
  },
  coding_single: {
    description: "한 모델이 처음부터 끝까지 구현을 맡습니다. 가장 단순한 코딩 노드입니다.",
    example: "예: CSV 파일에서 매출 합계를 계산하는 파이썬 스크립트 만들기",
    outputs: [
      "작업 결과 요약 (`text`)",
      "만들거나 바꾼 파일 목록 (`artifacts`)"
    ],
    fields: [
      section("무엇을 만들지", "구현할 요구사항을 적습니다."),
      field("input", "요구사항", "textarea", {
        rows: 5,
        placeholder: "CSV 파일을 읽어서 매출 합계를 계산하고, 결과를 콘솔에 출력하는 파이썬 스크립트를 만들어 줘."
      }),
      field("memoryNotes", "메모리 노트", "text", {
        placeholder: "code-style, project-rules"
      }),
      section("어떤 모델로 할지", "구현을 맡길 공급자와 모델을 고릅니다."),
      field("provider", "공급자", "select", {
        options: ORCHESTRATION_PROVIDER_OPTIONS
      }),
      field("model", "모델", "provider-model", {
        providerKey: "provider",
        autoLabel: "AUTO"
      }),
      field("language", "언어", "select", {
        options: CODING_LANGUAGE_OPTIONS
      }),
      section("참고 자료", "공식 문서나 검색 결과를 같이 붙일 수 있습니다."),
      field("webSearchEnabled", "웹 참고 사용", "select", {
        options: ENABLED_OPTIONS
      }),
      field("webUrls", "같이 볼 URL", "textarea", {
        rows: 4,
        placeholder: "https://docs.python.org/3/library/csv.html"
      })
    ]
  },
  coding_orchestration: {
    description: "설계, 구현, 검토처럼 역할을 나눠서 코드를 만듭니다.",
    example: "예: 구현 후 검토 단계를 한 번 더 거쳐 안정적으로 코드 만들기",
    outputs: [
      "최종 작업 요약 (`text`)",
      "실제로 선택된 모델과 언어 (`data.provider/model/language`)"
    ],
    fields: [
      section("무엇을 만들지", "요구사항과 조건을 적습니다."),
      field("input", "요구사항", "textarea", {
        rows: 5,
        placeholder: "로그 파일을 읽어 에러만 모아 JSON으로 저장하는 스크립트를 만들어 줘."
      }),
      field("memoryNotes", "메모리 노트", "text", {
        placeholder: "code-style, deployment-rules"
      }),
      section("중심 모델", "AUTO면 메인 구현 모델을 알아서 고릅니다."),
      field("provider", "중심 공급자", "select", {
        options: ORCHESTRATION_PROVIDER_OPTIONS
      }),
      field("model", "중심 모델", "provider-model", {
        providerKey: "provider",
        autoLabel: "AUTO"
      }),
      field("language", "언어", "select", {
        options: CODING_LANGUAGE_OPTIONS
      }),
      section("보조 모델", "검토나 수정에 참여할 모델을 고릅니다."),
      field("groqModel", "Groq 보조 모델", "catalog-select", {
        catalogKey: "groqWorkerOptions"
      }),
      field("geminiModel", "Gemini 보조 모델", "catalog-select", {
        catalogKey: "geminiWorkerOptions"
      }),
      field("cerebrasModel", "Cerebras 보조 모델", "catalog-select", {
        catalogKey: "cerebrasWorkerOptions"
      }),
      field("nvidiaModel", "NVIDIA NIM 보조 모델", "catalog-select", {
        catalogKey: "nvidiaWorkerOptions"
      }),
      field("copilotModel", "Copilot 보조 모델", "catalog-select", {
        catalogKey: "copilotWorkerOptions"
      }),
      field("codexModel", "Codex 보조 모델", "catalog-select", {
        catalogKey: "codexWorkerOptions"
      }),
      section("참고 자료", "필요하면 공식 문서나 참고 URL을 붙입니다."),
      field("webSearchEnabled", "웹 참고 사용", "select", {
        options: ENABLED_OPTIONS
      }),
      field("webUrls", "같이 볼 URL", "textarea", {
        rows: 4,
        placeholder: "https://nodejs.org/api/fs.html"
      })
    ]
  },
  coding_multi: {
    description: "여러 모델이 각자 구현한 결과를 비교해서 더 나은 방향을 고릅니다.",
    example: "예: 같은 요구사항을 여러 방식으로 구현해 보고 가장 단순한 안 고르기",
    outputs: [
      "비교 후 정리한 결론 (`text`)",
      "후보 구현에서 나온 파일 목록 (`artifacts`)"
    ],
    fields: [
      section("무엇을 비교할지", "같은 요구사항을 여러 모델에 맡깁니다."),
      field("input", "요구사항", "textarea", {
        rows: 5,
        placeholder: "같은 기능을 가장 단순하게 구현하는 방식을 비교해서 추천해 줘."
      }),
      field("memoryNotes", "메모리 노트", "text", {
        placeholder: "code-style, existing-api"
      }),
      section("마지막 정리", "비교 결과를 정리할 공급자와 모델입니다."),
      field("provider", "정리 공급자", "select", {
        options: ORCHESTRATION_PROVIDER_OPTIONS
      }),
      field("model", "정리 모델", "provider-model", {
        providerKey: "provider",
        autoLabel: "AUTO"
      }),
      field("language", "언어", "select", {
        options: CODING_LANGUAGE_OPTIONS
      }),
      section("비교할 모델", "실제로 나란히 돌릴 모델을 고릅니다."),
      field("groqModel", "Groq 모델", "catalog-select", {
        catalogKey: "groqWorkerOptions"
      }),
      field("geminiModel", "Gemini 모델", "catalog-select", {
        catalogKey: "geminiWorkerOptions"
      }),
      field("cerebrasModel", "Cerebras 모델", "catalog-select", {
        catalogKey: "cerebrasWorkerOptions"
      }),
      field("nvidiaModel", "NVIDIA NIM 모델", "catalog-select", {
        catalogKey: "nvidiaWorkerOptions"
      }),
      field("copilotModel", "Copilot 모델", "catalog-select", {
        catalogKey: "copilotWorkerOptions"
      }),
      field("codexModel", "Codex 모델", "catalog-select", {
        catalogKey: "codexWorkerOptions"
      }),
      section("참고 자료", "참고할 문서나 검색을 붙일 수 있습니다."),
      field("webSearchEnabled", "웹 참고 사용", "select", {
        options: ENABLED_OPTIONS
      }),
      field("webUrls", "같이 볼 URL", "textarea", {
        rows: 4,
        placeholder: "https://react.dev/reference/react/useEffect"
      })
    ]
  },
  routine_run: {
    description: "이미 만들어 둔 루틴이나 다른 그래프를 여기서 다시 실행합니다.",
    example: "예: 매일 보고서 루틴을 이 흐름 안에서 한 번 더 실행",
    outputs: [
      "실행한 루틴 ID (`data.routineId`)",
      "실행 뒤 상태 (`data.status`)"
    ],
    fields: [
      field("routineId", "루틴 ID", "catalog-select", {
        catalogKey: "routineOptions",
        placeholder: "실행할 루틴을 선택하거나 직접 입력"
      }),
      field("graphId", "그래프 ID 별칭", "text", {
        placeholder: "order-check-flow"
      })
    ]
  },
  memory_search: {
    description: "기억해 둔 문서나 대화 기록에서 필요한 내용을 찾아옵니다.",
    example: "예: 지난주 회의에서 '가격 정책'이 언급된 부분 찾기",
    outputs: [
      "찾아낸 내용 요약 (`text`)",
      "찾은 개수 (`data.count`)"
    ],
    fields: [
      field("query", "검색어", "textarea", {
        rows: 4,
        placeholder: "지난주 회의에서 가격 정책이 언급된 부분만 찾아 줘."
      }),
      field("maxResults", "최대 결과 수", "number", {
        min: "1",
        step: "1",
        placeholder: "5"
      }),
      field("minScore", "최소 점수", "number", {
        min: "0",
        max: "1",
        step: "0.05",
        placeholder: "0.35"
      })
    ]
  },
  memory_get: {
    description: "메모리 문서에서 필요한 부분을 바로 읽어옵니다.",
    example: "예: MEMORY.md에서 배포 규칙만 읽어 오기",
    outputs: [
      "읽어 온 본문 (`text`)",
      "읽은 경로 (`data.path`)"
    ],
    fields: [
      field("path", "메모리 경로", "path", {
        placeholder: "memory-notes/release-rules.md",
        pathScope: "memory"
      }),
      field("from", "시작 줄", "number", {
        min: "1",
        step: "1",
        placeholder: "1"
      }),
      field("lines", "줄 수", "number", {
        min: "1",
        step: "1",
        placeholder: "120"
      })
    ]
  },
  web_search: {
    description: "웹에서 검색해서 관련 링크와 요약을 가져옵니다.",
    example: "예: OpenAI Responses API rate limit 검색",
    outputs: [
      "검색 결과 요약 (`text`)",
      "실제로 사용한 공급자와 결과 수 (`data.provider/count`)"
    ],
    fields: [
      field("query", "검색어", "textarea", {
        rows: 4,
        placeholder: "OpenAI Responses API rate limit 2026"
      }),
      field("count", "결과 수", "number", {
        min: "1",
        step: "1",
        placeholder: "5"
      }),
      field("freshness", "최신성", "select", {
        options: WEB_FRESHNESS_OPTIONS
      })
    ]
  },
  web_fetch: {
    description: "웹페이지 하나를 열어서 본문 위주로 읽어옵니다.",
    example: "예: 공지 페이지 본문만 읽어서 다음 단계에 넘기기",
    outputs: [
      "읽어 온 본문 (`text`)",
      "최종 URL과 응답 정보 (`data.finalUrl/status/contentType`)"
    ],
    fields: [
      field("url", "URL", "text", {
        placeholder: "https://example.com/notice"
      }),
      field("extractMode", "추출 방식", "select", {
        options: WEB_EXTRACT_MODE_OPTIONS
      }),
      field("maxChars", "최대 문자 수", "number", {
        min: "1000",
        step: "1000",
        placeholder: "50000"
      })
    ]
  },
  file_read: {
    description: "프로젝트 안 파일을 읽어서 다음 단계에 넘깁니다.",
    example: "예: docs/release-notes.md 내용을 읽어서 요약하기",
    outputs: [
      "읽은 파일 내용 (`text`)",
      "실제 파일 위치 (`artifacts`)"
    ],
    fields: [
      field("path", "파일 경로", "path", {
        placeholder: "docs/release-notes.md",
        pathScope: "workspace"
      }),
      field("maxChars", "최대 문자 수", "number", {
        min: "1000",
        step: "1000",
        placeholder: "120000"
      })
    ]
  },
  file_write: {
    description: "프로젝트 안 원하는 위치에 파일을 저장합니다.",
    example: "예: reports/daily-summary.md 파일로 오늘 요약 저장",
    outputs: [
      "저장 결과 안내 (`text`)",
      "저장한 파일 위치 (`artifacts`)"
    ],
    fields: [
      field("path", "파일 경로", "path", {
        placeholder: "reports/daily-summary.md",
        pathScope: "workspace",
        allowDirectorySelection: true
      }),
      field("content", "파일 내용", "textarea", {
        rows: 6,
        placeholder: "# 오늘 요약\n\n1. 오전 회의 결정 사항\n2. 오후 작업 예정\n3. 확인이 필요한 항목"
      })
    ]
  },
  session_list: {
    description: "지금 열려 있는 세션을 훑어보고 필요한 대상을 찾습니다.",
    example: "예: 지금 살아 있는 코딩 세션만 찾아 보기",
    outputs: [
      "찾은 세션 수 (`data.count`)",
      "첫 번째 세션 키 (`data.firstSessionKey`)"
    ],
    fields: [
      field("kinds", "세션 종류", "text", {
        placeholder: "chat,coding,task"
      }),
      field("limit", "최대 세션 수", "number", {
        min: "1",
        step: "1",
        placeholder: "20"
      }),
      field("activeMinutes", "활성 기준 분", "number", {
        min: "1",
        step: "1",
        placeholder: "1440"
      }),
      field("messageLimit", "메시지 미리보기 수", "number", {
        min: "1",
        step: "1",
        placeholder: "5"
      }),
      field("search", "검색어", "text", {
        placeholder: "release"
      }),
      field("scope", "스코프", "text", {
        placeholder: "coding"
      }),
      field("mode", "모드", "text", {
        placeholder: "single"
      })
    ]
  },
  session_spawn: {
    description: "지금 흐름과 분리된 새 작업 세션을 열어 따로 처리하게 합니다.",
    example: "예: README 개선안만 별도 세션에서 먼저 정리하게 하기",
    outputs: [
      "새로 만든 세션 키 (`sessionKey`)",
      "실행 정보 (`data.runId/runtime/status`)"
    ],
    fields: [
      section("무엇을 맡길지", "새 세션에 보낼 작업 내용을 적습니다."),
      field("task", "작업", "textarea", {
        rows: 5,
        placeholder: "README 개선안 3개만 먼저 정리해 줘."
      }),
      field("label", "라벨", "text", {
        placeholder: "README 검토"
      }),
      section("실행 방식", "실행 엔진과 제한 시간을 정합니다."),
      field("runtime", "실행 엔진", "select", {
        options: SESSION_RUNTIME_OPTIONS
      }),
      field("mode", "세션 방식", "select", {
        options: SESSION_MODE_OPTIONS
      }),
      field("thread", "스레드 유지", "select", {
        options: BOOLEAN_OPTIONS
      }),
      field("runTimeoutSeconds", "실행 제한 초", "number", {
        min: "0",
        step: "60",
        placeholder: "900"
      }),
      field("timeoutSeconds", "대기 제한 초", "number", {
        min: "0",
        step: "60",
        placeholder: "900"
      }),
      field("alias", "세션 별칭", "text", {
        placeholder: "readme-review"
      })
    ]
  },
  session_send: {
    description: "이미 열어 둔 세션에 추가 지시를 보내고 응답을 받습니다.",
    example: "예: 방금 만든 초안을 3문장으로 줄여 달라고 다시 보내기",
    outputs: [
      "응답을 받은 세션 키 (`sessionKey`)",
      "세션이 돌려준 답변 (`text`)"
    ],
    fields: [
      field("sessionKey", "세션 키", "text", {
        placeholder: "예: readme-review"
      }),
      field("message", "전송 메시지", "textarea", {
        rows: 5,
        placeholder: "방금 만든 초안을 한글로 더 짧게 줄여 줘."
      }),
      field("timeoutSeconds", "응답 대기 초", "number", {
        min: "1",
        step: "1",
        placeholder: "180"
      })
    ]
  },
  cron_status: {
    description: "예약 작업이 켜져 있는지와 등록된 작업 수를 확인합니다.",
    example: "예: 지금 예약 실행이 살아 있는지 확인",
    outputs: [
      "예약 기능 활성 여부 (`data.enabled`)",
      "작업 수와 저장 위치 (`data.jobs/storePath`)"
    ],
    fields: []
  },
  cron_run: {
    description: "예약 작업을 기다리지 않고 지금 바로 한 번 실행합니다.",
    example: "예: daily-report 작업을 지금 즉시 실행",
    outputs: [
      "실제로 실행됐는지 여부 (`data.ran`)",
      "실행 사유나 오류 (`data.reason/error`)"
    ],
    fields: [
      field("jobId", "작업 ID", "text", {
        placeholder: "daily-report"
      }),
      field("runMode", "실행 방식", "select", {
        options: CRON_RUN_MODE_OPTIONS
      })
    ]
  },
  browser_execute: {
    description: "브라우저를 열거나, 탭을 고르거나, 주소를 이동시키는 작업입니다.",
    example: "예: 새 탭으로 상태 페이지 열기",
    outputs: [
      "실행한 작업과 현재 주소 정보 (`data.action/profile/activeUrl`)"
    ],
    fields: [
      field("action", "동작", "select", {
        options: BROWSER_ACTION_OPTIONS
      }),
      field("profile", "프로필", "text", {
        placeholder: "default"
      }),
      field("targetUrl", "대상 URL", "text", {
        placeholder: "https://status.openai.com"
      }),
      field("targetId", "대상 탭 ID", "text", {
        placeholder: "브라우저가 알려준 탭 ID"
      }),
      field("limit", "탭 제한", "number", {
        min: "1",
        step: "1",
        placeholder: "10"
      })
    ]
  },
  canvas_execute: {
    description: "캔버스를 보여 주거나 숨기고, 이동하고, 캡처하는 작업입니다.",
    example: "예: 현재 캔버스를 PNG로 저장",
    outputs: [
      "실행한 작업과 대상 정보 (`data.action/profile/url`)"
    ],
    fields: [
      field("action", "동작", "select", {
        options: CANVAS_ACTION_OPTIONS
      }),
      field("profile", "프로필", "text", {
        placeholder: "default"
      }),
      field("target", "타깃", "text", {
        placeholder: "main"
      }),
      field("targetUrl", "대상 URL", "text", {
        placeholder: "https://example.com/dashboard"
      }),
      field("javaScript", "JavaScript", "textarea", {
        rows: 5,
        placeholder: "document.querySelector('h1')?.textContent"
      }),
      field("jsonl", "JSONL", "textarea", {
        rows: 5,
        placeholder: "{\"op\":\"label\",\"text\":\"오늘 요약\"}"
      }),
      field("outputFormat", "출력 형식", "select", {
        options: CANVAS_OUTPUT_FORMAT_OPTIONS
      }),
      field("maxWidth", "최대 폭", "number", {
        min: "320",
        step: "64",
        placeholder: "1440"
      })
    ]
  },
  nodes_pending: {
    description: "승인이나 처리를 기다리는 요청이 얼마나 쌓였는지 확인합니다.",
    example: "예: 아직 승인 안 된 요청 수 확인",
    outputs: [
      "대기 중인 요청 수 (`data.pendingCount`)",
      "현재 노드 수 (`data.nodeCount`)"
    ],
    fields: [
      field("profile", "프로필", "text", {
        placeholder: "default"
      })
    ]
  },
  nodes_invoke: {
    description: "노드 명령을 보내거나, 승인/거절하거나, 시스템 알림을 띄울 때 씁니다.",
    example: "예: 배포 승인 요청 보내기",
    outputs: [
      "실행한 동작과 대상 정보 (`data.action/selectedNodeId/selectedCommand`)"
    ],
    fields: [
      field("action", "동작", "select", {
        options: NODES_ACTION_OPTIONS
      }),
      field("profile", "프로필", "text", {
        placeholder: "default"
      }),
      field("node", "대상 노드", "text", {
        placeholder: "build-agent"
      }),
      field("requestId", "요청 ID", "text", {
        placeholder: "req-1234"
      }),
      field("title", "알림 제목", "text", {
        placeholder: "배포 승인 필요"
      }),
      field("body", "알림 본문", "textarea", {
        rows: 4,
        placeholder: "production 배포를 진행할까요?"
      }),
      field("priority", "우선순위", "select", {
        options: PRIORITY_OPTIONS
      }),
      field("delivery", "전달 방식", "select", {
        options: DELIVERY_OPTIONS
      }),
      field("invokeCommand", "호출 명령", "text", {
        placeholder: "app.echo"
      }),
      field("invokeParamsJson", "호출 파라미터 JSON", "textarea", {
        rows: 5,
        placeholder: "{\"text\":\"배포를 시작합니다.\"}"
      })
    ]
  },
  telegram_stub: {
    description: "텔레그램에서 들어온 것처럼 명령을 테스트할 때 씁니다.",
    example: "예: `/llm status` 같은 명령을 실제 연결 없이 미리 확인",
    outputs: [
      "텔레그램 응답 형태 결과 (`text`)",
      "재시도 정보 (`data.retryAttempt`)"
    ],
    fields: [
      field("text", "텔레그램 입력", "textarea", {
        rows: 4,
        placeholder: "/llm 오늘 배포 상태 알려줘"
      }),
      field("webSearchEnabled", "웹 참고 사용", "select", {
        options: ENABLED_OPTIONS
      }),
      field("webUrls", "같이 볼 URL", "textarea", {
        rows: 4,
        placeholder: "https://status.example.com"
      })
    ]
  }
};

export function createEmptyLogicGraph(overrides = {}) {
  const timestamp = Date.now();
  return {
    graphId: "",
    title: "새 작업 흐름",
    description: "",
    version: "logic.graph.v1",
    viewport: {
      x: 96,
      y: 72,
      zoom: 1
    },
    schedule: {
      scheduleSourceMode: "manual",
      scheduleKind: "daily",
      scheduleTime: "08:00",
      timezoneId: "Asia/Seoul",
      dayOfMonth: 1,
      weekdays: [1, 2, 3, 4, 5],
      enabled: false
    },
    enabled: true,
    nodes: [
      createLogicNode("start", {
        nodeId: `start-${timestamp}`,
        position: { x: 180, y: 160 }
      })
    ],
    edges: [],
    ...overrides
  };
}

export function getLogicNodeMinimumSize(type) {
  const safeType = `${type || ""}`.trim();
  const target = LOGIC_NODE_MIN_SIZE_BY_TYPE[safeType];
  return {
    width: target?.width || LOGIC_NODE_MIN_SIZE.width,
    height: target?.height || LOGIC_NODE_MIN_SIZE.height
  };
}

export function normalizeLogicNodeSize(size, type = "") {
  const normalized = size && typeof size === "object" ? size : {};
  const minimum = getLogicNodeMinimumSize(type);
  const widthValue = Number(normalized.width);
  const heightValue = Number(normalized.height);
  const safeWidth = Number.isFinite(widthValue) ? widthValue : LOGIC_NODE_DEFAULT_SIZE.width;
  const safeHeight = Number.isFinite(heightValue) ? heightValue : LOGIC_NODE_DEFAULT_SIZE.height;
  return {
    width: Math.max(minimum.width, Math.min(LOGIC_NODE_MAX_SIZE.width, Math.round(safeWidth))),
    height: Math.max(minimum.height, Math.min(LOGIC_NODE_MAX_SIZE.height, Math.round(safeHeight)))
  };
}

export function createLogicNode(type, overrides = {}) {
  const safeType = `${type || "template"}`.trim() || "template";
  return {
    nodeId: overrides.nodeId || `node-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    type: safeType,
    title: overrides.title || DEFAULT_NODE_TITLES[safeType] || safeType,
    position: overrides.position || {
      x: 240,
      y: 180
    },
    size: normalizeLogicNodeSize(overrides.size, safeType),
    enabled: overrides.enabled !== false,
    continueOnError: !!overrides.continueOnError,
    config: {
      ...defaultNodeConfig(safeType),
      ...(overrides.config || {})
    },
    outputs: {
      ...(overrides.outputs || {})
    }
  };
}

export function createLogicEdge(edgeId, sourceNodeId, targetNodeId, overrides = {}) {
  return {
    edgeId: edgeId || `edge-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    sourceNodeId,
    sourcePort: overrides.sourcePort || "main",
    targetNodeId,
    targetPort: overrides.targetPort || "main",
    condition: overrides.condition || null
  };
}

export function createLogicState() {
  return {
    graphs: [],
    selectedGraphId: "",
    draftGraph: createEmptyLogicGraph(),
    selectedNodeId: "",
    selectedEdgeId: "",
    pendingSourceNodeId: "",
    activeRunId: "",
    runSnapshot: null,
    runEvents: [],
    jsonBuffer: "",
    dirty: false,
    lastMessage: "",
    loading: false
  };
}

export function cloneLogicGraph(graph) {
  return JSON.parse(JSON.stringify(graph || createEmptyLogicGraph()));
}

export function summarizeLogicGraph(graph) {
  if (!graph || typeof graph !== "object") {
    return "";
  }

  const nodeCount = Array.isArray(graph.nodes) ? graph.nodes.length : 0;
  const edgeCount = Array.isArray(graph.edges) ? graph.edges.length : 0;
  return `${nodeCount}개 노드 · ${edgeCount}개 연결`;
}

export function getLogicNodeInspectorDefinition(type) {
  const safeType = `${type || ""}`.trim();
  return LOGIC_NODE_INSPECTOR_DEFINITIONS[safeType] || {
    description: "이 노드는 아직 설명이 준비되지 않았습니다.",
    outputs: [],
    fields: []
  };
}

function defaultNodeConfig(type) {
  switch (type) {
    case "end":
    case "output":
      return {
        result: ""
      };
    case "if":
      return {
        leftRef: "{{run.input}}",
        operator: "contains",
        rightValue: "조건"
      };
    case "set_var":
      return {
        name: "message",
        value: "{{run.input}}"
      };
    case "template":
      return {
        template: "{{run.input}}"
      };
    case "delay":
      return {
        seconds: "1",
        milliseconds: ""
      };
    case "chat_single":
      return {
        input: "{{run.input}}",
        provider: "groq",
        model: DEFAULT_GROQ_SINGLE_MODEL,
        memoryNotes: "",
        webSearchEnabled: "true",
        webUrls: ""
      };
    case "chat_orchestration":
      return {
        input: "{{run.input}}",
        provider: "auto",
        model: "",
        memoryNotes: "",
        groqModel: DEFAULT_GROQ_WORKER_MODEL,
        geminiModel: DEFAULT_GEMINI_MODEL,
        cerebrasModel: DEFAULT_CEREBRAS_MODEL,
        nvidiaModel: DEFAULT_NVIDIA_MODEL,
        copilotModel: LOGIC_NONE_MODEL,
        codexModel: LOGIC_NONE_MODEL,
        webSearchEnabled: "true",
        webUrls: ""
      };
    case "chat_multi":
      return {
        input: "{{run.input}}",
        memoryNotes: "",
        groqModel: DEFAULT_GROQ_WORKER_MODEL,
        geminiModel: DEFAULT_GEMINI_MODEL,
        cerebrasModel: DEFAULT_CEREBRAS_MODEL,
        nvidiaModel: DEFAULT_NVIDIA_MODEL,
        copilotModel: LOGIC_NONE_MODEL,
        codexModel: LOGIC_NONE_MODEL,
        summaryProvider: "auto",
        webSearchEnabled: "true",
        webUrls: ""
      };
    case "coding_single":
      return {
        input: "{{run.input}}",
        provider: "auto",
        model: "",
        language: "auto",
        memoryNotes: "",
        webSearchEnabled: "true",
        webUrls: ""
      };
    case "coding_orchestration":
      return {
        input: "{{run.input}}",
        provider: "auto",
        model: "",
        language: "auto",
        memoryNotes: "",
        groqModel: DEFAULT_GROQ_WORKER_MODEL,
        geminiModel: DEFAULT_GEMINI_MODEL,
        cerebrasModel: DEFAULT_CEREBRAS_MODEL,
        nvidiaModel: DEFAULT_NVIDIA_MODEL,
        copilotModel: LOGIC_NONE_MODEL,
        codexModel: LOGIC_NONE_MODEL,
        webSearchEnabled: "true",
        webUrls: ""
      };
    case "coding_multi":
      return {
        input: "{{run.input}}",
        provider: "auto",
        model: "",
        language: "auto",
        memoryNotes: "",
        groqModel: DEFAULT_GROQ_WORKER_MODEL,
        geminiModel: DEFAULT_GEMINI_MODEL,
        cerebrasModel: DEFAULT_CEREBRAS_MODEL,
        nvidiaModel: DEFAULT_NVIDIA_MODEL,
        copilotModel: LOGIC_NONE_MODEL,
        codexModel: LOGIC_NONE_MODEL,
        webSearchEnabled: "true",
        webUrls: ""
      };
    case "routine_run":
      return {
        routineId: "",
        graphId: ""
      };
    case "memory_search":
      return {
        query: "{{run.input}}",
        maxResults: "5",
        minScore: "0.35"
      };
    case "memory_get":
      return {
        path: "MEMORY.md",
        from: "1",
        lines: "120"
      };
    case "web_search":
      return {
        query: "{{run.input}}",
        count: "5",
        freshness: ""
      };
    case "web_fetch":
      return {
        url: "https://example.com",
        extractMode: "article",
        maxChars: "50000"
      };
    case "file_write":
      return {
        path: "logic/output.txt",
        content: "{{run.input}}"
      };
    case "file_read":
      return {
        path: "README.md",
        maxChars: "120000"
      };
    case "session_list":
      return {
        kinds: "",
        limit: "20",
        activeMinutes: "43200",
        messageLimit: "20",
        search: "",
        scope: "",
        mode: ""
      };
    case "session_spawn":
      return {
        task: "{{run.input}}",
        label: "",
        runtime: "acp",
        runTimeoutSeconds: "900",
        timeoutSeconds: "900",
        thread: "false",
        mode: "run",
        alias: "child"
      };
    case "session_send":
      return {
        sessionKey: "{{sessions.child}}",
        message: "{{run.input}}",
        timeoutSeconds: "300"
      };
    case "cron_run":
      return {
        jobId: "",
        runMode: "manual"
      };
    case "browser_execute":
      return {
        action: "status",
        profile: "default",
        targetUrl: "https://example.com",
        targetId: "",
        limit: "20"
      };
    case "canvas_execute":
      return {
        action: "status",
        profile: "default",
        target: "main",
        targetUrl: "https://example.com",
        javaScript: "document.title",
        jsonl: "{\"op\":\"label\",\"text\":\"hello\"}",
        outputFormat: "markdown",
        maxWidth: "1280"
      };
    case "nodes_pending":
      return {
        profile: "default"
      };
    case "nodes_invoke":
      return {
        action: "invoke",
        profile: "default",
        node: "",
        requestId: "",
        title: "",
        body: "",
        priority: "active",
        delivery: "auto",
        invokeCommand: "app.echo",
        invokeParamsJson: "{\"text\":\"hello\"}"
      };
    case "telegram_stub":
      return {
        text: "/llm status",
        webSearchEnabled: "true",
        webUrls: ""
      };
    default:
      return {};
  }
}
