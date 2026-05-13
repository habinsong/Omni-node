export function createRoutineOutputPreviewState() {
  return {
    open: false,
    title: "",
    content: "",
    imagePath: "",
    imageAlt: ""
  };
}

export function createRoutineProgressState(overrides = {}) {
  return {
    active: false,
    operation: "",
    percent: 0,
    message: "",
    stageKey: "",
    stageTitle: "",
    stageDetail: "",
    stageIndex: 0,
    stageTotal: 5,
    done: false,
    ok: null,
    startedAt: 0,
    updatedAt: 0,
    completedAt: 0,
    ...overrides
  };
}

export function createRoutineState(options) {
  const { defaultMobilePanes } = options;
  return {
    routines: [],
    routineSelectedId: "",
    groqUsageWindowBaseByModel: {},
    mobilePaneByTab: { ...defaultMobilePanes },
    progress: createRoutineProgressState(),
    preview: null,
    schedulerStatus: null,
    listQuery: "",
    listFilter: "all"
  };
}
