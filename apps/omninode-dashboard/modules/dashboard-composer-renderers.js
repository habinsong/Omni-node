import { CODING_LANGUAGES } from "./dashboard-constants.js";

function renderCerebrasOptions(e, prefix, items) {
  return items.map((item) =>
    e("option", { key: `${prefix}-${item.id}`, value: item.id }, item.label)
  );
}

function renderCerebrasWorkerOptions(e, prefix, noneModel, items) {
  return [
    e("option", { key: `${prefix}-none`, value: noneModel }, "Cerebras: 선택 안함"),
    ...renderCerebrasOptions(e, prefix, items)
  ];
}

function renderLanguageOptions(e) {
  return CODING_LANGUAGES.map((item) => e("option", { key: item[0], value: item[0] }, item[1]));
}

function renderComposerSupportDocks(e, codingResultDock, safeRefactorDock) {
  if (!codingResultDock && !safeRefactorDock) {
    return null;
  }

  return e(
    "div",
    { className: "composer-support-docks" },
    codingResultDock || null,
    safeRefactorDock || null
  );
}

export function renderChatComposerPanel(props) {
  const {
    e,
    mode,
    renderComposerInputBar,
    constants,
    optionSets,
    selectedModels,
    values,
    setters,
    helpers,
    actions
  } = props;

  const {
    NONE_MODEL,
    DEFAULT_GROQ_SINGLE_MODEL,
    DEFAULT_CODEX_MODEL,
    DEFAULT_GEMINI_WORKER_MODEL,
    DEFAULT_CEREBRAS_MODEL,
    DEFAULT_NVIDIA_MODEL,
    CEREBRAS_MODEL_CHOICES
  } = constants;
  const {
    groqModelOptions,
    copilotModelOptions,
    codexModelOptions,
    geminiModelOptions,
    nvidiaModelOptions,
    groqWorkerModelOptions,
    geminiWorkerModelOptions,
    nvidiaWorkerModelOptions,
    copilotWorkerModelOptions,
    codexWorkerModelOptions
  } = optionSets;
  const {
    selectedGroqModel,
    selectedCopilotModel
  } = selectedModels;
  const {
    chatSingleProvider,
    chatSingleModel,
    chatInputSingle,
    chatOrchProvider,
    chatOrchModel,
    chatInputOrch,
    chatOrchGroqModel,
    chatOrchGeminiModel,
    chatOrchCerebrasModel,
    chatOrchNvidiaModel,
    chatOrchCopilotModel,
    chatOrchCodexModel,
    chatMultiGroqModel,
    chatMultiGeminiModel,
    chatMultiCerebrasModel,
    chatMultiNvidiaModel,
    chatMultiCopilotModel,
    chatMultiCodexModel,
    chatMultiSummaryProvider,
    chatInputMulti
  } = values;
  const {
    setChatSingleProvider,
    setChatSingleModel,
    setChatInputSingle,
    setChatOrchProvider,
    setChatOrchModel,
    setChatInputOrch,
    setChatOrchGroqModel,
    setChatOrchGeminiModel,
    setChatOrchCerebrasModel,
    setChatOrchNvidiaModel,
    setChatOrchCopilotModel,
    setChatOrchCodexModel,
    setChatMultiGroqModel,
    setChatMultiGeminiModel,
    setChatMultiCerebrasModel,
    setChatMultiNvidiaModel,
    setChatMultiCopilotModel,
    setChatMultiCodexModel,
    setChatMultiSummaryProvider,
    setChatInputMulti
  } = setters;
  const { isNoneModel } = helpers;
  const {
    sendChatSingle,
    sendChatOrchestration,
    sendChatMulti,
    createRoutineFromCurrentInput,
    createPlanFromCurrentInput
  } = actions;

  if (mode === "single") {
    return e(
      "div",
      { className: "composer messenger-composer" },
      e("div", { className: "toolbar" },
        e("select", {
          className: "input compact",
          value: chatSingleProvider,
          onChange: (event) => {
            const value = event.target.value;
            setChatSingleProvider(value);
            if (value === "groq") {
              setChatSingleModel(selectedGroqModel || DEFAULT_GROQ_SINGLE_MODEL);
            } else if (value === "copilot") {
              setChatSingleModel(selectedCopilotModel || "");
            } else if (value === "codex") {
              setChatSingleModel(DEFAULT_CODEX_MODEL);
            } else if (value === "gemini") {
              setChatSingleModel(DEFAULT_GEMINI_WORKER_MODEL);
            } else if (value === "cerebras") {
              setChatSingleModel(DEFAULT_CEREBRAS_MODEL);
            } else if (value === "nvidia") {
              setChatSingleModel(DEFAULT_NVIDIA_MODEL);
            } else {
              setChatSingleModel("");
            }
          }
        },
          e("option", { value: "groq" }, "Groq"),
          e("option", { value: "gemini" }, "Gemini"),
          e("option", { value: "cerebras" }, "Cerebras"),
          e("option", { value: "nvidia" }, "NVIDIA NIM"),
          e("option", { value: "copilot" }, "Copilot"),
          e("option", { value: "codex" }, "Codex")),
        chatSingleProvider === "groq"
          ? e("select", {
            className: "input compact",
            value: chatSingleModel || selectedGroqModel,
            onChange: (event) => setChatSingleModel(event.target.value)
          }, groqModelOptions.length === 0 ? e("option", { value: "" }, "Groq 모델 로딩 전") : groqModelOptions)
          : chatSingleProvider === "copilot"
            ? e("select", {
              className: "input compact",
              value: chatSingleModel || selectedCopilotModel,
              onChange: (event) => setChatSingleModel(event.target.value)
            }, copilotModelOptions.length === 0 ? e("option", { value: "" }, "Copilot 모델 로딩 전") : copilotModelOptions)
            : chatSingleProvider === "codex"
              ? e("select", {
                className: "input compact",
                value: chatSingleModel || DEFAULT_CODEX_MODEL,
                onChange: (event) => setChatSingleModel(event.target.value)
              }, codexModelOptions)
              : chatSingleProvider === "cerebras"
                ? e("select", {
                  className: "input compact",
                  value: chatSingleModel || DEFAULT_CEREBRAS_MODEL,
                  onChange: (event) => setChatSingleModel(event.target.value)
              }, renderCerebrasOptions(e, "chat-single-cerebras", CEREBRAS_MODEL_CHOICES))
                : chatSingleProvider === "nvidia"
                  ? e("select", {
                    className: "input compact",
                    value: chatSingleModel || DEFAULT_NVIDIA_MODEL,
                    onChange: (event) => setChatSingleModel(event.target.value)
                  }, nvidiaModelOptions)
                  : e("select", {
                    className: "input compact",
                    value: chatSingleModel || DEFAULT_GEMINI_WORKER_MODEL,
                    onChange: (event) => setChatSingleModel(event.target.value)
                  }, geminiModelOptions)
      ),
      renderComposerInputBar({
        value: chatInputSingle,
        onChange: (event) => setChatInputSingle(event.target.value),
        onSend: sendChatSingle,
        onCreateRoutine: () => createRoutineFromCurrentInput("chat:single"),
        onCreatePlan: () => createPlanFromCurrentInput("chat:single"),
        pendingKey: "chat:single",
        placeholder: "질문 입력"
      })
    );
  }

  if (mode === "orchestration") {
    return e(
      "div",
      { className: "composer messenger-composer" },
      e("div", { className: "preset-hint" }, "오케스트레이션은 요청 성격을 보고 워커 역할을 자동 분담합니다. 초안, 리스크 점검, 예시 구체화, 최종 검토를 나눠 통합합니다."),
      e("div", { className: "toolbar" },
        e("select", {
          className: "input compact",
          value: chatOrchProvider,
          onChange: (event) => {
            const value = event.target.value;
            setChatOrchProvider(value);
            if (value === "groq") {
              setChatOrchModel(selectedGroqModel || "");
            } else if (value === "copilot") {
              setChatOrchModel(selectedCopilotModel || "");
            } else if (value === "codex") {
              setChatOrchModel(chatOrchCodexModel || DEFAULT_CODEX_MODEL);
            } else if (value === "gemini") {
              setChatOrchModel(
                isNoneModel(chatOrchGeminiModel) ? DEFAULT_GEMINI_WORKER_MODEL : (chatOrchGeminiModel || DEFAULT_GEMINI_WORKER_MODEL)
              );
            } else if (value === "cerebras") {
              setChatOrchModel(chatOrchCerebrasModel || DEFAULT_CEREBRAS_MODEL);
            } else if (value === "nvidia") {
              setChatOrchModel(
                isNoneModel(chatOrchNvidiaModel) ? DEFAULT_NVIDIA_MODEL : (chatOrchNvidiaModel || DEFAULT_NVIDIA_MODEL)
              );
            } else {
              setChatOrchModel("");
            }
          }
        },
          e("option", { value: "auto" }, "AUTO"),
          e("option", { value: "groq" }, "Groq"),
          e("option", { value: "gemini" }, "Gemini"),
          e("option", { value: "cerebras" }, "Cerebras"),
          e("option", { value: "nvidia" }, "NVIDIA NIM"),
          e("option", { value: "copilot" }, "Copilot"),
          e("option", { value: "codex" }, "Codex")),
        chatOrchProvider === "groq"
          ? e("select", {
            className: "input compact",
            value: chatOrchModel || selectedGroqModel,
            onChange: (event) => setChatOrchModel(event.target.value)
          }, groqModelOptions.length === 0 ? e("option", { value: "" }, "Groq 모델 로딩 전") : groqModelOptions)
          : chatOrchProvider === "cerebras"
            ? e("select", {
              className: "input compact",
              value: chatOrchModel || chatOrchCerebrasModel,
              onChange: (event) => setChatOrchModel(event.target.value)
            }, renderCerebrasOptions(e, "chat-orch-cerebras", CEREBRAS_MODEL_CHOICES))
            : chatOrchProvider === "copilot"
              ? e("select", {
                className: "input compact",
                value: chatOrchModel || selectedCopilotModel,
                onChange: (event) => setChatOrchModel(event.target.value)
              }, copilotModelOptions.length === 0 ? e("option", { value: "" }, "Copilot 모델 로딩 전") : copilotModelOptions)
              : chatOrchProvider === "codex"
                ? e("select", {
                  className: "input compact",
                  value: chatOrchModel || chatOrchCodexModel || DEFAULT_CODEX_MODEL,
                  onChange: (event) => setChatOrchModel(event.target.value)
                }, codexModelOptions)
                : chatOrchProvider === "nvidia"
                  ? e("select", {
                    className: "input compact",
                    value: (!isNoneModel(chatOrchModel) ? chatOrchModel : "")
                      || (!isNoneModel(chatOrchNvidiaModel) ? chatOrchNvidiaModel : DEFAULT_NVIDIA_MODEL),
                    onChange: (event) => setChatOrchModel(event.target.value)
                  }, nvidiaModelOptions)
                  : chatOrchProvider === "gemini"
                    ? e("select", {
                      className: "input compact",
                      value: (!isNoneModel(chatOrchModel) ? chatOrchModel : "")
                        || (!isNoneModel(chatOrchGeminiModel) ? chatOrchGeminiModel : DEFAULT_GEMINI_WORKER_MODEL),
                      onChange: (event) => setChatOrchModel(event.target.value)
                    }, geminiModelOptions)
                    : e("div", { className: "fixed-chip" }, "AUTO")
      ),
      e("div", { className: "toolbar" },
        e("div", { className: "fixed-chip" }, "워커 모델"),
        e("select", {
          className: "input compact",
          value: chatOrchGroqModel,
          onChange: (event) => setChatOrchGroqModel(event.target.value)
        }, groqWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: chatOrchGeminiModel,
          onChange: (event) => setChatOrchGeminiModel(event.target.value)
        }, geminiWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: chatOrchCerebrasModel,
          onChange: (event) => setChatOrchCerebrasModel(event.target.value)
        }, renderCerebrasWorkerOptions(e, "chat-orch-cerebras-worker", NONE_MODEL, CEREBRAS_MODEL_CHOICES)),
        e("select", {
          className: "input compact",
          value: chatOrchNvidiaModel,
          onChange: (event) => setChatOrchNvidiaModel(event.target.value)
        }, nvidiaWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: chatOrchCopilotModel,
          onChange: (event) => setChatOrchCopilotModel(event.target.value)
        }, copilotWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: chatOrchCodexModel,
          onChange: (event) => setChatOrchCodexModel(event.target.value)
        }, codexWorkerModelOptions)
      ),
      renderComposerInputBar({
        value: chatInputOrch,
        onChange: (event) => setChatInputOrch(event.target.value),
        onSend: sendChatOrchestration,
        onCreateRoutine: () => createRoutineFromCurrentInput("chat:orchestration"),
        onCreatePlan: () => createPlanFromCurrentInput("chat:orchestration"),
        pendingKey: "chat:orchestration",
        placeholder: "병렬 통합 질문 입력"
      })
    );
  }

  return e(
    "div",
    { className: "composer messenger-composer" },
    e("div", { className: "preset-hint" }, "다중 LLM은 모델별 답변을 각각 넘겨보고, 아래 공통 정리에서 겹치는 핵심과 차이를 확인할 때 쓰는 모드입니다."),
    e("div", { className: "toolbar" },
      e("select", {
        className: "input compact",
        value: chatMultiGroqModel,
        onChange: (event) => setChatMultiGroqModel(event.target.value)
      }, groqWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: chatMultiGeminiModel,
        onChange: (event) => setChatMultiGeminiModel(event.target.value)
      }, geminiWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: chatMultiCerebrasModel,
        onChange: (event) => setChatMultiCerebrasModel(event.target.value)
      }, renderCerebrasWorkerOptions(e, "chat-multi-cerebras-worker", NONE_MODEL, CEREBRAS_MODEL_CHOICES)),
      e("select", {
        className: "input compact",
        value: chatMultiNvidiaModel,
        onChange: (event) => setChatMultiNvidiaModel(event.target.value)
      }, nvidiaWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: chatMultiCopilotModel,
        onChange: (event) => setChatMultiCopilotModel(event.target.value)
      }, copilotWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: chatMultiCodexModel,
        onChange: (event) => setChatMultiCodexModel(event.target.value)
      }, codexWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: chatMultiSummaryProvider,
        onChange: (event) => setChatMultiSummaryProvider(event.target.value)
      },
        e("option", { value: "auto" }, "요약: AUTO"),
        e("option", { value: "gemini" }, "요약: Gemini"),
        e("option", { value: "groq" }, "요약: Groq"),
        e("option", { value: "cerebras" }, "요약: Cerebras"),
        e("option", { value: "nvidia" }, "요약: NVIDIA NIM"),
        e("option", { value: "copilot" }, "요약: Copilot"),
        e("option", { value: "codex" }, "요약: Codex"))
    ),
    renderComposerInputBar({
      value: chatInputMulti,
      onChange: (event) => setChatInputMulti(event.target.value),
      onSend: sendChatMulti,
      onCreateRoutine: () => createRoutineFromCurrentInput("chat:multi"),
      onCreatePlan: () => createPlanFromCurrentInput("chat:multi"),
      pendingKey: "chat:multi",
      placeholder: "다중 LLM 비교 질문 입력"
    })
  );
}

export function renderCodingComposerPanel(props) {
  const {
    e,
    mode,
    renderComposerInputBar,
    codingResultDock,
    safeRefactorDock,
    constants,
    optionSets,
    selectedModels,
    values,
    setters,
    helpers,
    actions
  } = props;

  const {
    NONE_MODEL,
    DEFAULT_CODEX_MODEL,
    DEFAULT_GEMINI_WORKER_MODEL,
    DEFAULT_CEREBRAS_MODEL,
    DEFAULT_NVIDIA_MODEL,
    CEREBRAS_MODEL_CHOICES
  } = constants;
  const {
    groqModelOptions,
    copilotModelOptions,
    codexModelOptions,
    geminiModelOptions,
    nvidiaModelOptions,
    groqWorkerModelOptions,
    geminiWorkerModelOptions,
    nvidiaWorkerModelOptions,
    copilotWorkerModelOptions,
    codexWorkerModelOptions
  } = optionSets;
  const {
    selectedGroqModel,
    selectedCopilotModel
  } = selectedModels;
  const {
    codingSingleProvider,
    codingSingleModel,
    codingSingleLanguage,
    codingInputSingle,
    codingOrchProvider,
    codingOrchModel,
    codingOrchLanguage,
    codingInputOrch,
    codingOrchGroqModel,
    codingOrchGeminiModel,
    codingOrchCerebrasModel,
    codingOrchNvidiaModel,
    codingOrchCopilotModel,
    codingOrchCodexModel,
    codingMultiProvider,
    codingMultiModel,
    codingMultiLanguage,
    codingInputMulti,
    codingMultiGroqModel,
    codingMultiGeminiModel,
    codingMultiCerebrasModel,
    codingMultiNvidiaModel,
    codingMultiCopilotModel,
    codingMultiCodexModel
  } = values;
  const {
    setCodingSingleProvider,
    setCodingSingleModel,
    setCodingSingleLanguage,
    setCodingInputSingle,
    setCodingOrchProvider,
    setCodingOrchModel,
    setCodingOrchLanguage,
    setCodingInputOrch,
    setCodingOrchGroqModel,
    setCodingOrchGeminiModel,
    setCodingOrchCerebrasModel,
    setCodingOrchNvidiaModel,
    setCodingOrchCopilotModel,
    setCodingOrchCodexModel,
    setCodingMultiProvider,
    setCodingMultiModel,
    setCodingMultiLanguage,
    setCodingInputMulti,
    setCodingMultiGroqModel,
    setCodingMultiGeminiModel,
    setCodingMultiCerebrasModel,
    setCodingMultiNvidiaModel,
    setCodingMultiCopilotModel,
    setCodingMultiCodexModel
  } = setters;
  const { isNoneModel } = helpers;
  const {
    sendCodingSingle,
    sendCodingOrchestration,
    sendCodingMulti,
    createRoutineFromCurrentInput,
    createPlanFromCurrentInput
  } = actions;

  if (mode === "single") {
    return e(
      "div",
      { className: "composer messenger-composer" },
      e("div", { className: "toolbar" },
        e("select", {
          className: "input compact",
          value: codingSingleProvider,
          onChange: (event) => {
            const value = event.target.value;
            setCodingSingleProvider(value);
            if (value === "groq") {
              setCodingSingleModel(selectedGroqModel || "");
            } else if (value === "cerebras") {
              setCodingSingleModel(DEFAULT_CEREBRAS_MODEL);
            } else if (value === "nvidia") {
              setCodingSingleModel(DEFAULT_NVIDIA_MODEL);
            } else if (value === "copilot") {
              setCodingSingleModel(selectedCopilotModel || "");
            } else if (value === "codex") {
              setCodingSingleModel(DEFAULT_CODEX_MODEL);
            } else if (value === "gemini") {
              setCodingSingleModel(DEFAULT_GEMINI_WORKER_MODEL);
            } else {
              setCodingSingleModel("");
            }
          }
        },
          e("option", { value: "auto" }, "AUTO"),
          e("option", { value: "groq" }, "Groq"),
          e("option", { value: "gemini" }, "Gemini"),
          e("option", { value: "cerebras" }, "Cerebras"),
          e("option", { value: "nvidia" }, "NVIDIA NIM"),
          e("option", { value: "copilot" }, "Copilot"),
          e("option", { value: "codex" }, "Codex")),
        codingSingleProvider === "groq"
          ? e("select", {
            className: "input compact",
            value: codingSingleModel || selectedGroqModel,
            onChange: (event) => setCodingSingleModel(event.target.value)
          }, groqModelOptions.length === 0 ? e("option", { value: "" }, "Groq 모델 로딩 전") : groqModelOptions)
          : codingSingleProvider === "copilot"
            ? e("select", {
              className: "input compact",
              value: codingSingleModel || selectedCopilotModel,
              onChange: (event) => setCodingSingleModel(event.target.value)
            }, copilotModelOptions.length === 0 ? e("option", { value: "" }, "Copilot 모델 로딩 전") : copilotModelOptions)
            : codingSingleProvider === "codex"
              ? e("select", {
                className: "input compact",
                value: codingSingleModel || DEFAULT_CODEX_MODEL,
                onChange: (event) => setCodingSingleModel(event.target.value)
              }, codexModelOptions)
              : codingSingleProvider === "cerebras"
                ? e("select", {
                  className: "input compact",
                  value: codingSingleModel || DEFAULT_CEREBRAS_MODEL,
                  onChange: (event) => setCodingSingleModel(event.target.value)
                }, renderCerebrasOptions(e, "coding-single-cerebras", CEREBRAS_MODEL_CHOICES))
                : codingSingleProvider === "nvidia"
                  ? e("select", {
                    className: "input compact",
                    value: codingSingleModel || DEFAULT_NVIDIA_MODEL,
                    onChange: (event) => setCodingSingleModel(event.target.value)
                  }, nvidiaModelOptions)
                  : codingSingleProvider === "gemini"
                    ? e("select", {
                      className: "input compact",
                      value: codingSingleModel || DEFAULT_GEMINI_WORKER_MODEL,
                      onChange: (event) => setCodingSingleModel(event.target.value)
                    }, geminiModelOptions)
                    : e("div", { className: "fixed-chip" }, "AUTO"),
        e("select", {
          className: "input compact",
          value: codingSingleLanguage,
          onChange: (event) => setCodingSingleLanguage(event.target.value)
        }, renderLanguageOptions(e))
      ),
      renderComposerSupportDocks(e, codingResultDock, safeRefactorDock),
      renderComposerInputBar({
        value: codingInputSingle,
        onChange: (event) => setCodingInputSingle(event.target.value),
        onSend: sendCodingSingle,
        onCreateRoutine: () => createRoutineFromCurrentInput("coding:single"),
        onCreatePlan: () => createPlanFromCurrentInput("coding:single"),
        pendingKey: "coding:single",
        placeholder: "처음부터 끝까지 구현할 요구사항 입력"
      })
    );
  }

  if (mode === "orchestration") {
    return e(
      "div",
      { className: "composer messenger-composer" },
      e("div", { className: "preset-hint" }, "오케스트레이션 코딩은 기획, 개발, 검증 및 테스트, 수정 단계를 모델들이 나눠 맡습니다. `주 구현`을 지정하면 개발 단계에 우선 배치하고, 입력 없이 실행하면 자동 역할 분배로 시작합니다."),
      e("div", { className: "toolbar" },
        e("select", {
          className: "input compact",
          value: codingOrchProvider,
          onChange: (event) => {
            const value = event.target.value;
            setCodingOrchProvider(value);
            if (value === "groq") {
              setCodingOrchModel(selectedGroqModel || "");
            } else if (value === "copilot") {
              setCodingOrchModel(selectedCopilotModel || "");
            } else if (value === "codex") {
              setCodingOrchModel(codingOrchCodexModel || DEFAULT_CODEX_MODEL);
            } else if (value === "cerebras") {
              setCodingOrchModel(codingOrchCerebrasModel || DEFAULT_CEREBRAS_MODEL);
            } else if (value === "nvidia") {
              setCodingOrchModel(
                isNoneModel(codingOrchNvidiaModel) ? DEFAULT_NVIDIA_MODEL : (codingOrchNvidiaModel || DEFAULT_NVIDIA_MODEL)
              );
            } else if (value === "gemini") {
              setCodingOrchModel(
                isNoneModel(codingOrchGeminiModel) ? DEFAULT_GEMINI_WORKER_MODEL : (codingOrchGeminiModel || DEFAULT_GEMINI_WORKER_MODEL)
              );
            } else {
              setCodingOrchModel("");
            }
          }
        },
          e("option", { value: "auto" }, "주 구현: AUTO"),
          e("option", { value: "groq" }, "주 구현: Groq"),
          e("option", { value: "gemini" }, "주 구현: Gemini"),
          e("option", { value: "cerebras" }, "주 구현: Cerebras"),
          e("option", { value: "nvidia" }, "주 구현: NVIDIA NIM"),
          e("option", { value: "copilot" }, "주 구현: Copilot"),
          e("option", { value: "codex" }, "주 구현: Codex")),
        codingOrchProvider === "groq"
          ? e("select", {
            className: "input compact",
            value: codingOrchModel || selectedGroqModel,
            onChange: (event) => setCodingOrchModel(event.target.value)
          }, groqModelOptions.length === 0 ? e("option", { value: "" }, "Groq 모델 로딩 전") : groqModelOptions)
          : codingOrchProvider === "copilot"
            ? e("select", {
              className: "input compact",
              value: codingOrchModel || selectedCopilotModel,
              onChange: (event) => setCodingOrchModel(event.target.value)
            }, copilotModelOptions.length === 0 ? e("option", { value: "" }, "Copilot 모델 로딩 전") : copilotModelOptions)
            : codingOrchProvider === "codex"
              ? e("select", {
                className: "input compact",
                value: codingOrchModel || codingOrchCodexModel || DEFAULT_CODEX_MODEL,
                onChange: (event) => setCodingOrchModel(event.target.value)
              }, codexModelOptions)
              : codingOrchProvider === "cerebras"
                ? e("select", {
                  className: "input compact",
                  value: codingOrchModel || codingOrchCerebrasModel || DEFAULT_CEREBRAS_MODEL,
                  onChange: (event) => setCodingOrchModel(event.target.value)
                }, renderCerebrasOptions(e, "coding-orch-cerebras", CEREBRAS_MODEL_CHOICES))
                : codingOrchProvider === "nvidia"
                  ? e("select", {
                    className: "input compact",
                    value: (!isNoneModel(codingOrchModel) ? codingOrchModel : "")
                      || (!isNoneModel(codingOrchNvidiaModel) ? codingOrchNvidiaModel : DEFAULT_NVIDIA_MODEL),
                    onChange: (event) => setCodingOrchModel(event.target.value)
                  }, nvidiaModelOptions)
                  : codingOrchProvider === "gemini"
                    ? e("select", {
                      className: "input compact",
                      value: (!isNoneModel(codingOrchModel) ? codingOrchModel : "")
                        || (!isNoneModel(codingOrchGeminiModel) ? codingOrchGeminiModel : DEFAULT_GEMINI_WORKER_MODEL),
                      onChange: (event) => setCodingOrchModel(event.target.value)
                    }, geminiModelOptions)
                    : e("div", { className: "fixed-chip" }, "AUTO"),
        e("select", {
          className: "input compact",
          value: codingOrchLanguage,
          onChange: (event) => setCodingOrchLanguage(event.target.value)
        }, renderLanguageOptions(e))
      ),
      e("div", { className: "toolbar" },
        e("div", { className: "fixed-chip" }, "워커 모델"),
        e("select", {
          className: "input compact",
          value: codingOrchGroqModel,
          onChange: (event) => setCodingOrchGroqModel(event.target.value)
        }, groqWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: codingOrchGeminiModel,
          onChange: (event) => setCodingOrchGeminiModel(event.target.value)
        }, geminiWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: codingOrchCerebrasModel,
          onChange: (event) => setCodingOrchCerebrasModel(event.target.value)
        }, renderCerebrasWorkerOptions(e, "coding-orch-cerebras-worker", NONE_MODEL, CEREBRAS_MODEL_CHOICES)),
        e("select", {
          className: "input compact",
          value: codingOrchNvidiaModel,
          onChange: (event) => setCodingOrchNvidiaModel(event.target.value)
        }, nvidiaWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: codingOrchCopilotModel,
          onChange: (event) => setCodingOrchCopilotModel(event.target.value)
        }, copilotWorkerModelOptions),
        e("select", {
          className: "input compact",
          value: codingOrchCodexModel,
          onChange: (event) => setCodingOrchCodexModel(event.target.value)
        }, codexWorkerModelOptions)
      ),
      renderComposerSupportDocks(e, codingResultDock, safeRefactorDock),
      renderComposerInputBar({
        value: codingInputOrch,
        onChange: (event) => setCodingInputOrch(event.target.value),
        onSend: sendCodingOrchestration,
        onCreateRoutine: () => createRoutineFromCurrentInput("coding:orchestration"),
        onCreatePlan: () => createPlanFromCurrentInput("coding:orchestration"),
        pendingKey: "coding:orchestration",
        placeholder: "기획부터 수정까지 역할 분담할 요구사항 입력"
      })
    );
  }

  return e(
    "div",
    { className: "composer messenger-composer" },
    e("div", { className: "preset-hint" }, "다중 코딩은 선택한 각 모델이 서로 독립된 폴더에서 처음부터 끝까지 완주하고, 아래에서 모델별 결과와 공통점/차이를 비교합니다."),
    e("div", { className: "toolbar" },
      e("select", {
        className: "input compact",
        value: codingMultiProvider,
        onChange: (event) => {
          const value = event.target.value;
          setCodingMultiProvider(value);
          if (value === "groq") {
            setCodingMultiModel(selectedGroqModel || "");
          } else if (value === "cerebras") {
            setCodingMultiModel(codingMultiCerebrasModel || DEFAULT_CEREBRAS_MODEL);
          } else if (value === "nvidia") {
            setCodingMultiModel(
              isNoneModel(codingMultiNvidiaModel) ? DEFAULT_NVIDIA_MODEL : (codingMultiNvidiaModel || DEFAULT_NVIDIA_MODEL)
            );
          } else if (value === "copilot") {
            setCodingMultiModel(selectedCopilotModel || "");
          } else if (value === "codex") {
            setCodingMultiModel(codingMultiCodexModel || DEFAULT_CODEX_MODEL);
          } else if (value === "gemini") {
            setCodingMultiModel(DEFAULT_GEMINI_WORKER_MODEL);
          } else {
            setCodingMultiModel("");
          }
        }
      },
        e("option", { value: "auto" }, "비교 요약: AUTO"),
        e("option", { value: "groq" }, "비교 요약: Groq"),
      e("option", { value: "gemini" }, "비교 요약: Gemini"),
      e("option", { value: "cerebras" }, "비교 요약: Cerebras"),
      e("option", { value: "nvidia" }, "비교 요약: NVIDIA NIM"),
      e("option", { value: "copilot" }, "비교 요약: Copilot"),
        e("option", { value: "codex" }, "비교 요약: Codex")),
      codingMultiProvider === "groq"
        ? e("select", {
          className: "input compact",
          value: codingMultiModel || selectedGroqModel,
          onChange: (event) => setCodingMultiModel(event.target.value)
        }, groqModelOptions.length === 0 ? e("option", { value: "" }, "Groq 모델 로딩 전") : groqModelOptions)
        : codingMultiProvider === "copilot"
          ? e("select", {
            className: "input compact",
            value: codingMultiModel || selectedCopilotModel,
            onChange: (event) => setCodingMultiModel(event.target.value)
          }, copilotModelOptions.length === 0 ? e("option", { value: "" }, "Copilot 모델 로딩 전") : copilotModelOptions)
          : codingMultiProvider === "codex"
            ? e("select", {
              className: "input compact",
              value: codingMultiModel || codingMultiCodexModel || DEFAULT_CODEX_MODEL,
              onChange: (event) => setCodingMultiModel(event.target.value)
            }, codexModelOptions)
            : codingMultiProvider === "cerebras"
              ? e("select", {
                className: "input compact",
                value: codingMultiModel || codingMultiCerebrasModel || DEFAULT_CEREBRAS_MODEL,
                onChange: (event) => setCodingMultiModel(event.target.value)
              }, renderCerebrasOptions(e, "coding-multi-cerebras", CEREBRAS_MODEL_CHOICES))
              : codingMultiProvider === "nvidia"
                ? e("select", {
                  className: "input compact",
                  value: codingMultiModel || codingMultiNvidiaModel || DEFAULT_NVIDIA_MODEL,
                  onChange: (event) => setCodingMultiModel(event.target.value)
                }, nvidiaModelOptions)
                : codingMultiProvider === "gemini"
                  ? e("select", {
                    className: "input compact",
                    value: codingMultiModel || DEFAULT_GEMINI_WORKER_MODEL,
                    onChange: (event) => setCodingMultiModel(event.target.value)
                  }, geminiModelOptions)
                  : e("div", { className: "fixed-chip" }, "AUTO"),
      e("select", {
        className: "input compact",
        value: codingMultiLanguage,
        onChange: (event) => setCodingMultiLanguage(event.target.value)
      }, renderLanguageOptions(e))
    ),
    e("div", { className: "toolbar" },
      e("div", { className: "fixed-chip" }, "워커 모델"),
      e("select", {
        className: "input compact",
        value: codingMultiGroqModel,
        onChange: (event) => setCodingMultiGroqModel(event.target.value)
      }, groqWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: codingMultiGeminiModel,
        onChange: (event) => setCodingMultiGeminiModel(event.target.value)
      }, geminiWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: codingMultiCerebrasModel,
        onChange: (event) => setCodingMultiCerebrasModel(event.target.value)
      }, renderCerebrasWorkerOptions(e, "coding-multi-cerebras-worker", NONE_MODEL, CEREBRAS_MODEL_CHOICES)),
      e("select", {
        className: "input compact",
        value: codingMultiNvidiaModel,
        onChange: (event) => setCodingMultiNvidiaModel(event.target.value)
      }, nvidiaWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: codingMultiCopilotModel,
        onChange: (event) => setCodingMultiCopilotModel(event.target.value)
      }, copilotWorkerModelOptions),
      e("select", {
        className: "input compact",
        value: codingMultiCodexModel,
        onChange: (event) => setCodingMultiCodexModel(event.target.value)
      }, codexWorkerModelOptions)
    ),
    renderComposerSupportDocks(e, codingResultDock, safeRefactorDock),
    renderComposerInputBar({
      value: codingInputMulti,
      onChange: (event) => setCodingInputMulti(event.target.value),
      onSend: sendCodingMulti,
      onCreateRoutine: () => createRoutineFromCurrentInput("coding:multi"),
      onCreatePlan: () => createPlanFromCurrentInput("coding:multi"),
      pendingKey: "coding:multi",
      placeholder: "여러 모델이 각각 끝까지 구현할 요구사항 입력"
    })
  );
}
