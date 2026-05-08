// Skill management UI panel renderer.
// Lists skills, opens SKILL.md for editing, and creates/deletes project/global skills.

const SKILL_NAME_PATTERN = /^[a-z0-9][a-z0-9-]{0,62}$/;

export function renderSkillsRoot(deps) {
  const {
    e,
    authed,
    skills,
    selectedSkillKey,
    setSelectedSkillKey,
    skillEditor,
    setSkillEditor,
    skillStatus,
    loadingSkillKey,
    skillsBusy,
    skillSearch,
    setSkillSearch,
    onRefreshSkills,
    onLoadSkillDetail,
    onSaveSkill,
    onDeleteSkill,
    onStartNewSkill
  } = deps;

  const skillItems = normalizeSkills(skills);
  const query = `${skillSearch || ""}`.trim().toLowerCase();
  const filteredItems = query
    ? skillItems.filter((skill) => [
      skill.name,
      skill.scope,
      skill.description,
      skill.path
    ].some((value) => `${value || ""}`.toLowerCase().includes(query)))
    : skillItems;
  const selected = selectedSkillKey
    ? skillItems.find((skill) => skill.key === selectedSkillKey)
    : null;
  const projectCount = skillItems.filter((skill) => skill.scope === "project").length;
  const globalCount = skillItems.filter((skill) => skill.scope === "global").length;
  const isCreating = Boolean(skillEditor?.isNew);
  const selectedNeedsDetail = Boolean(selected && !isCreating && loadingSkillKey === selected.key);

  return e("section", { className: "skills-root" },
    e("div", { className: "skills-page-head" },
      e("div", null,
        e("h2", null, "스킬"),
        e("p", null, "대화에서 바로 꺼내 쓸 작업 방식을 만들고, SKILL.md 내용을 한 화면에서 고칩니다.")
      ),
      e("div", { className: "skills-head-actions" },
        e("button", {
          type: "button",
          className: "btn",
          onClick: onRefreshSkills,
          disabled: !authed || skillsBusy
        }, skillsBusy ? "읽는 중..." : "목록 다시 읽기"),
        e("button", {
          type: "button",
          className: "btn primary",
          onClick: onStartNewSkill,
          disabled: !authed
        }, "새 스킬 만들기")
      )
    ),
    skillStatus
      ? e("div", { className: `skills-status-bar ${skillStatus.kind || ""}` }, skillStatus.message)
      : null,
    e("div", { className: "skills-overview-grid" },
      renderMetric(e, "전체", `${skillItems.length}개`, "현재 연결된 스킬"),
      renderMetric(e, "프로젝트", `${projectCount}개`, ".omni/skills"),
      renderMetric(e, "전역", `${globalCount}개`, "~/.omninode/skills"),
      renderMetric(e, "선택", selected?.name || (isCreating ? "새 스킬" : "-"), selected ? scopeLabel(selected.scope) : "편집할 항목")
    ),
    e("div", { className: "skills-workbench" },
      renderSkillSidebar({
        e,
        authed,
        skillItems,
        filteredItems,
        selectedSkillKey,
        query: skillSearch || "",
        setQuery: setSkillSearch,
        skillsBusy,
        onRefreshSkills,
        onStartNewSkill,
        onSelect: (skill) => {
          setSelectedSkillKey(skill.key);
          onLoadSkillDetail(skill.name, skill.scope);
        }
      }),
      renderSkillEditorPanel({
        e,
        authed,
        selected,
        selectedNeedsDetail,
        skillEditor,
        loadingSkillKey,
        setSkillEditor,
        onSaveSkill,
        onDeleteSkill
      })
    )
  );
}

function renderSkillSidebar(options) {
  const {
    e,
    authed,
    skillItems,
    filteredItems,
    selectedSkillKey,
    query,
    setQuery,
    skillsBusy,
    onRefreshSkills,
    onStartNewSkill,
    onSelect
  } = options;

  return e("aside", { className: "skills-sidebar", "aria-label": "스킬 목록" },
    e("div", { className: "skills-sidebar-head" },
      e("div", null,
        e("strong", null, "스킬 목록"),
        e("span", null, `${filteredItems.length}/${skillItems.length}개`)
      ),
      e("button", {
        type: "button",
        className: "btn icon-btn",
        title: "목록 다시 읽기",
        onClick: onRefreshSkills,
        disabled: !authed || skillsBusy
      }, "↻")
    ),
    e("label", { className: "skills-search-field" },
      e("span", null, "검색"),
      e("input", {
        className: "input",
        value: query,
        placeholder: "이름, 설명, 경로 검색",
        onChange: (event) => setQuery(event.target.value)
      })
    ),
    filteredItems.length === 0
      ? e("div", { className: "skills-empty" },
        skillItems.length === 0
          ? "아직 등록된 스킬이 없습니다. 새 스킬을 만들거나 채팅에서 스킬 생성을 요청하세요."
          : "검색 결과가 없습니다."
      )
      : e("div", { className: "skills-list" },
        filteredItems.map((skill) => {
          const isActive = skill.key === selectedSkillKey;
          return e("button", {
            key: skill.key,
            type: "button",
            className: `skills-list-item ${isActive ? "active" : ""}`,
            onClick: () => onSelect(skill)
          },
          e("div", { className: "skills-list-main" },
            e("strong", null, skill.name),
            e("span", { className: `skills-scope-chip ${skill.scope}` }, scopeLabel(skill.scope))
          ),
          e("p", null, skill.description || "설명이 없습니다."),
          e("span", { className: "skills-list-path" }, compactPath(skill.path)));
        })
      ),
    e("button", {
      type: "button",
      className: "skills-create-strip",
      onClick: onStartNewSkill,
      disabled: !authed
    }, "새 스킬 만들기")
  );
}

function renderSkillEditorPanel(options) {
  const {
    e,
    authed,
    selected,
    selectedNeedsDetail,
    skillEditor,
    loadingSkillKey,
    setSkillEditor,
    onSaveSkill,
    onDeleteSkill
  } = options;

  if (!authed) {
    return renderEmptyPanel(e, "인증이 필요합니다.", "스킬 목록을 읽거나 저장하려면 먼저 세션 인증을 완료하세요.");
  }

  if (selectedNeedsDetail) {
    return e("section", { className: "skills-editor" },
      e("div", { className: "skills-editor-loading" },
        e("div", { className: "skills-loading-mark" }, "…"),
        e("strong", null, `${selected.name} 불러오는 중`),
        e("p", null, "SKILL.md 내용을 읽고 있습니다.")
      )
    );
  }

  if (!skillEditor) {
    if (selected) {
      return renderEmptyPanel(
        e,
        "스킬 내용을 불러오지 못했습니다.",
        loadingSkillKey
          ? "SKILL.md 응답을 기다리고 있습니다."
          : "목록을 다시 읽거나 왼쪽 목록에서 스킬을 다시 선택하세요."
      );
    }
    return renderEmptyPanel(e, "스킬을 선택하세요.", "왼쪽 목록에서 고르거나 새 스킬을 만들어 바로 작성할 수 있습니다.");
  }

  return renderSkillEditorForm({
    e,
    skillEditor,
    setSkillEditor,
    onSaveSkill,
    onDeleteSkill,
    isExisting: !skillEditor.isNew
  });
}

function renderSkillEditorForm({
  e,
  skillEditor,
  setSkillEditor,
  onSaveSkill,
  onDeleteSkill,
  isExisting
}) {
  const editor = skillEditor;
  const normalizedName = `${editor.name || ""}`.trim();
  const nameInvalid = editor.isNew && normalizedName.length > 0 && !SKILL_NAME_PATTERN.test(normalizedName);
  const bodyLength = `${editor.body || ""}`.length;
  const descriptionLength = `${editor.description || ""}`.trim().length;
  const usageText = normalizedName ? `${normalizedName} 스킬 사용해` : "스킬 이름을 먼저 입력하세요";

  return e("section", { className: "skills-editor" },
    e("div", { className: "skills-editor-head" },
      e("div", null,
        e("span", { className: "skills-editor-eyebrow" }, editor.isNew ? "새 스킬" : scopeLabel(editor.scope)),
        e("h3", null, editor.isNew ? "스킬 만들기" : editor.name),
        e("p", null, editor.isNew
          ? "이름, 설명, 본문을 채우면 SKILL.md 파일로 저장됩니다."
          : "설명과 본문을 고치면 기존 SKILL.md에 바로 반영됩니다.")
      ),
      e("div", { className: "skills-editor-meta" },
        e("span", null, `${descriptionLength}자 설명`),
        e("span", null, `${bodyLength}자 본문`)
      )
    ),
    e("div", { className: "skills-usage-card" },
      e("div", null,
        e("strong", null, "대화에서 이렇게 사용"),
        e("p", null, usageText)
      ),
      e("span", { className: `skills-scope-chip ${editor.scope || "project"}` }, scopeLabel(editor.scope))
    ),
    e("div", { className: "skills-editor-grid" },
      e("label", { className: "skills-field" },
        e("span", null, "이름"),
        editor.isNew
          ? e("input", {
            className: `input ${nameInvalid ? "invalid" : ""}`,
            value: editor.name || "",
            placeholder: "예: ui-review",
            onChange: (event) => setSkillEditor({ ...editor, name: event.target.value.trim().toLowerCase() })
          })
          : e("div", { className: "skills-readonly-field" }, editor.name),
        nameInvalid
          ? e("small", { className: "skills-field-error" }, "소문자, 숫자, 하이픈만 사용할 수 있습니다.")
          : e("small", null, "대화에서 부를 짧은 이름입니다.")
      ),
      e("label", { className: "skills-field" },
        e("span", null, "저장 위치"),
        editor.isNew
          ? e("select", {
            className: "input",
            value: editor.scope || "project",
            onChange: (event) => setSkillEditor({ ...editor, scope: event.target.value })
          },
          e("option", { value: "project" }, "프로젝트 .omni/skills"),
          e("option", { value: "global" }, "전역 ~/.omninode/skills"))
          : e("div", { className: "skills-readonly-field" }, editor.scope === "global" ? "전역 ~/.omninode/skills" : "프로젝트 .omni/skills"),
        e("small", null, editor.scope === "global" ? "모든 프로젝트에서 재사용합니다." : "현재 Omni-node 프로젝트에서 사용합니다.")
      )
    ),
    e("label", { className: "skills-field" },
      e("span", null, "설명"),
      e("input", {
        className: "input",
        value: editor.description || "",
        placeholder: "언제 이 스킬을 써야 하는지 한 문장으로 적으세요.",
        onChange: (event) => setSkillEditor({ ...editor, description: event.target.value })
      }),
      e("small", null, "목록과 스킬 자동 선택 판단에 쓰이는 문장입니다.")
    ),
    e("label", { className: "skills-field skills-body-field" },
      e("span", null, "스킬 내용"),
      e("textarea", {
        className: "input skills-body-input",
        rows: 18,
        value: editor.body || "",
        placeholder: "이 스킬을 적용할 조건, 답변 방식, 금지할 행동, 예시를 적으세요.",
        onChange: (event) => setSkillEditor({ ...editor, body: event.target.value })
      })
    ),
    e("div", { className: "skills-editor-actions" },
      isExisting
        ? e("button", {
          type: "button",
          className: "btn danger",
          onClick: () => {
            if (window.confirm(`'${editor.name}' 스킬을 삭제하시겠습니까? 이 동작은 되돌릴 수 없습니다.`)) {
              onDeleteSkill(editor.name, editor.scope);
            }
          }
        }, "삭제")
        : e("button", {
          type: "button",
          className: "btn",
          onClick: () => {
            if (`${editor.body || ""}`.trim() && !window.confirm("작성 중인 본문을 기본 양식으로 바꿀까요?")) {
              return;
            }
            setSkillEditor({ ...editor, body: defaultSkillBody(normalizedName || "new-skill") });
          }
        }, "기본 양식 넣기"),
      e("button", {
        type: "button",
        className: "btn primary",
        onClick: () => onSaveSkill({ ...editor, name: normalizedName }),
        disabled: !normalizedName || nameInvalid
      }, editor.isNew ? "스킬 저장" : "변경 저장")
    )
  );
}

function renderMetric(e, label, value, helper) {
  return e("article", { className: "skills-metric" },
    e("span", null, label),
    e("strong", null, value),
    e("p", null, helper)
  );
}

function renderEmptyPanel(e, title, description) {
  return e("section", { className: "skills-editor" },
    e("div", { className: "skills-editor-empty" },
      e("strong", null, title),
      e("p", null, description)
    )
  );
}

function normalizeSkills(skills) {
  return (Array.isArray(skills) ? skills : [])
    .map((skill) => {
      const name = `${skill.name || skill.Name || ""}`.trim();
      const scope = `${skill.scope || skill.Scope || "project"}`.trim().toLowerCase() === "global" ? "global" : "project";
      return {
        name,
        scope,
        description: `${skill.description || skill.Description || ""}`.trim(),
        path: `${skill.path || skill.Path || ""}`.trim(),
        key: `${scope}:${name}`
      };
    })
    .filter((skill) => skill.name)
    .sort((a, b) => {
      if (a.scope !== b.scope) {
        return a.scope === "project" ? -1 : 1;
      }
      return a.name.localeCompare(b.name, "ko");
    });
}

function scopeLabel(scope) {
  return scope === "global" ? "전역" : "프로젝트";
}

function compactPath(path) {
  const value = `${path || ""}`.trim();
  if (!value) {
    return "경로 없음";
  }
  return value
    .replace(/^.*\/\.omni\/skills\//, ".omni/skills/")
    .replace(/^.*\/\.omninode\/skills\//, "~/.omninode/skills/");
}

function defaultSkillBody(name) {
  return [
    `# ${name}`,
    "",
    "## 언제 사용하나",
    "- 사용자가 이 스킬 이름을 직접 언급했을 때 적용한다.",
    "",
    "## 작업 방식",
    "- 먼저 요청 의도를 짧게 확인한다.",
    "- 필요한 출력 형식을 지킨다.",
    "- 모르는 내용은 추측하지 않는다.",
    "",
    "## 답변 형식",
    "- 핵심 결과를 먼저 말한다.",
    "- 필요한 경우 짧은 예시를 붙인다."
  ].join("\n");
}
