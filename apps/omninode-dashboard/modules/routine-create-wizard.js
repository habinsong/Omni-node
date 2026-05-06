import { renderRoutineRetryAndNotifyFields } from "./routine-form-fields.js";

const STEPS = [
  { key: "request", label: "요청", description: "무엇을 자동화할지 적습니다." },
  { key: "schedule", label: "스케줄 + 모드", description: "언제, 어떻게 실행할지 정합니다." },
  { key: "advanced", label: "고급", description: "재시도 / 텔레그램 알림 / 봇 응답을 조정합니다." }
];

/**
 * Progressive-disclosure create wizard.
 *
 * Form data lives in parent state (`routineCreateForm`) — this component
 * only owns its `currentStep` local state, so submission paths and message
 * handlers in app.js keep working unchanged.
 *
 * Step gating prevents Step 2/3 entry until prior step is valid (defensive).
 */
export function createRoutineCreateWizard({ React, e }) {
  const { useState, memo } = React;

  const RoutineCreateWizard = memo(function RoutineCreateWizard(props) {
    const {
      routineCreateForm,
      patchRoutineForm,
      toggleRoutineWeekday,
      createRoutineFromUi,
      onInputKeyDown,
      isRoutineCreatePending,
      routineAgentProviderOptions,
      routineAgentModelOptions,
      errorMessage,
      renderRoutineExecutionModeBuilder,
      renderRoutineScheduleBuilder
    } = props;

    const [currentStep, setCurrentStep] = useState(0);

    // Defensive gating — derived, not stored
    const requestText = `${routineCreateForm.request || ""}`.trim();
    const step1Valid = requestText.length >= 5;
    const step2Reachable = step1Valid;
    const step3Reachable = step1Valid;
    const reachableByIndex = [true, step2Reachable, step3Reachable];

    const goTo = (index) => {
      if (!reachableByIndex[index]) return;
      setCurrentStep(index);
    };

    const goNext = () => {
      if (currentStep >= STEPS.length - 1) return;
      const nextIdx = currentStep + 1;
      if (!reachableByIndex[nextIdx]) return;
      setCurrentStep(nextIdx);
    };

    const goBack = () => {
      if (currentStep <= 0) return;
      setCurrentStep(currentStep - 1);
    };

    const stepNav = e("div", { className: "routine-wizard-stepnav" },
      ...STEPS.map((step, index) => {
        const reachable = reachableByIndex[index];
        const isActive = currentStep === index;
        const isDone = index < currentStep && reachableByIndex[index];
        const cls = `routine-wizard-step ${isActive ? "active" : ""} ${isDone ? "done" : ""} ${reachable ? "" : "disabled"}`.trim();
        return e("button", {
          key: step.key,
          type: "button",
          className: cls,
          disabled: !reachable,
          onClick: () => goTo(index)
        },
          e("span", { className: "routine-wizard-step-index" }, `${index + 1}`),
          e("span", { className: "routine-wizard-step-label" }, step.label)
        );
      })
    );

    const stepHeader = e("div", { className: "routine-wizard-stepheader" },
      e("div", { className: "routine-head-kicker" }, `STEP ${currentStep + 1} / ${STEPS.length}`),
      e("div", { className: "routine-wizard-stephint" }, STEPS[currentStep].description)
    );

    let stepBody = null;
    if (currentStep === 0) {
      stepBody = e("div", { className: "routine-section-card routine-create-card" },
        e("div", { className: "routine-form-grid routine-form-grid-primary" },
          e("label", { className: "routine-field" },
            e("span", { className: "routine-field-label" }, "루틴 이름"),
            e("input", {
              className: "input",
              value: routineCreateForm.title,
              onChange: (event) => patchRoutineForm("create", { title: event.target.value }),
              placeholder: "비워두면 요청 기반으로 자동 생성"
            })
          ),
          e("label", { className: "routine-field routine-field-full" },
            e("span", { className: "routine-field-label" }, "요청 원문"),
            e("textarea", {
              className: "textarea routine-input",
              value: routineCreateForm.request,
              onChange: (event) => patchRoutineForm("create", { request: event.target.value }),
              onKeyDown: (event) => onInputKeyDown(event, createRoutineFromUi),
              placeholder: "예: 매일 오전 8시에 주요 기사와 서버 상태를 요약해줘"
            })
          ),
          !step1Valid && requestText.length > 0
            ? e("div", { className: "routine-wizard-validation" }, "요청 원문은 최소 5자 이상이어야 합니다.")
            : null
        )
      );
    } else if (currentStep === 1) {
      stepBody = e("div", { className: "routine-execution-config-stack" },
        renderRoutineExecutionModeBuilder({
          e,
          form: routineCreateForm,
          formType: "create",
          patchRoutineForm,
          routineAgentProviderOptions,
          routineAgentModelOptions
        }),
        renderRoutineScheduleBuilder({
          e,
          form: routineCreateForm,
          formType: "create",
          patchRoutineForm,
          toggleRoutineWeekday
        })
      );
    } else {
      stepBody = e("div", { className: "routine-section-card" },
        renderRoutineRetryAndNotifyFields({
          e,
          form: routineCreateForm,
          formType: "create",
          patchRoutineForm
        })
      );
    }

    const submitNow = currentStep === STEPS.length - 1;
    const navRow = e("div", { className: "routine-wizard-navrow" },
      e("button", {
        type: "button",
        className: "btn ghost",
        onClick: goBack,
        disabled: currentStep === 0 || isRoutineCreatePending
      }, "이전"),
      submitNow
        ? e("button", {
          type: "button",
          className: "btn primary routine-submit-btn",
          onClick: createRoutineFromUi,
          disabled: isRoutineCreatePending || !step1Valid
        }, isRoutineCreatePending ? "생성 중..." : "루틴 생성")
        : e("button", {
          type: "button",
          className: "btn primary",
          onClick: goNext,
          disabled: !reachableByIndex[currentStep + 1] || isRoutineCreatePending
        }, "다음 단계"),
      currentStep === STEPS.length - 1
        ? null
        : e("button", {
          type: "button",
          className: "btn",
          onClick: createRoutineFromUi,
          disabled: isRoutineCreatePending || !step1Valid,
          title: "고급 옵션 기본값으로 바로 생성"
        }, "건너뛰고 생성")
    );

    return e("section", { className: "routine-list-panel routine-create-panel routine-wizard" },
      e("div", { className: "routine-head" },
        e("div", null,
          e("div", { className: "routine-head-kicker" }, "새 루틴"),
          e("h2", null, "루틴 만들기")
        )
      ),
      e("p", { className: "hint routine-panel-hint" }, "단계별로 입력하면 누락 위험을 줄입니다. 텔레그램 응답과 알림은 마지막 단계에서 조정합니다."),
      errorMessage ? e("div", { className: "error-banner" }, errorMessage) : null,
      stepNav,
      stepHeader,
      stepBody,
      navRow
    );
  });

  return RoutineCreateWizard;
}
