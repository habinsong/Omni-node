export function createCodingState(options) {
  const {
    noneModel,
    defaultGroqWorkerModel,
    defaultGeminiWorkerModel,
    defaultCerebrasModel,
    defaultNvidiaModel
  } = options;

  return {
    resultByConversation: {},
    progressByKey: {},
    filePreviewByConversation: {},
    runtimeByConversation: {},
    executionInputByConversation: {},
    showExecutionLogsByConversation: {},
    inputSingle: "",
    inputOrch: "",
    inputMulti: "",
    singleProvider: "codex",
    singleModel: "",
    singleLanguage: "auto",
    orchProvider: "auto",
    orchModel: "",
    orchLanguage: "auto",
    orchGroqModel: defaultGroqWorkerModel,
    orchGeminiModel: defaultGeminiWorkerModel,
    orchCerebrasModel: defaultCerebrasModel,
    orchNvidiaModel: defaultNvidiaModel,
    orchCopilotModel: noneModel,
    orchCodexModel: noneModel,
    multiProvider: "gemini",
    multiModel: "",
    multiLanguage: "auto",
    multiGroqModel: defaultGroqWorkerModel,
    multiGeminiModel: defaultGeminiWorkerModel,
    multiCerebrasModel: defaultCerebrasModel,
    multiNvidiaModel: defaultNvidiaModel,
    multiCopilotModel: noneModel,
    multiCodexModel: noneModel
  };
}
