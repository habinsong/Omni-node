// Skill management UI panel renderer.
// Lists skills (project + global), allows view/edit/delete/create via SKILL.md.

export function renderSkillsRoot(deps) {
  const {
    e,
    skills,
    selectedSkillKey,
    setSelectedSkillKey,
    skillEditor,
    setSkillEditor,
    skillStatus,
    skillsBusy,
    onRefreshSkills,
    onLoadSkillDetail,
    onSaveSkill,
    onDeleteSkill,
    onStartNewSkill,
  } = deps;

  const skillItems = Array.isArray(skills) ? skills : [];
  const selected = selectedSkillKey
    ? skillItems.find((s) => `${s.scope}:${s.name}` === selectedSkillKey)
    : null;

  const sidebar = e(
    "aside",
    { className: "skills-sidebar" },
    e(
      "div",
      { className: "skills-sidebar-header" },
      e("strong", null, "스킬 목록"),
      e(
        "div",
        { className: "skills-sidebar-actions" },
        e(
          "button",
          { className: "btn-small", onClick: onRefreshSkills, disabled: skillsBusy },
          skillsBusy ? "갱신 중..." : "새로고침",
        ),
        e(
          "button",
          { className: "btn-small btn-primary", onClick: onStartNewSkill },
          "+ 새 스킬",
        ),
      ),
    ),
    skillItems.length === 0
      ? e(
          "div",
          { className: "skills-empty" },
          "등록된 스킬이 없습니다. \"+ 새 스킬\" 버튼이나 채팅에서 \"X 스킬 만들어줘\"로 추가하세요.",
        )
      : e(
          "ul",
          { className: "skills-list" },
          ...skillItems.map((skill) => {
            const key = `${skill.scope}:${skill.name}`;
            const isActive = key === selectedSkillKey;
            return e(
              "li",
              {
                key,
                className: `skills-list-item ${isActive ? "active" : ""}`,
                onClick: () => {
                  setSelectedSkillKey(key);
                  onLoadSkillDetail(skill.name, skill.scope);
                },
              },
              e("div", { className: "skills-list-name" }, skill.name),
              e(
                "div",
                { className: "skills-list-scope" },
                skill.scope === "global" ? "전역" : "프로젝트",
              ),
              e(
                "div",
                { className: "skills-list-desc" },
                truncate(skill.description, 80),
              ),
            );
          }),
        ),
  );

  const editor = e(
    "section",
    { className: "skills-editor" },
    !skillEditor && !selected
      ? e(
          "div",
          { className: "skills-editor-empty" },
          "왼쪽 목록에서 스킬을 선택하거나 \"+ 새 스킬\"을 눌러 시작하세요.",
        )
      : renderSkillEditorForm({
          e,
          skillEditor,
          setSkillEditor,
          skillStatus,
          onSaveSkill,
          onDeleteSkill,
          isExisting: Boolean(selected) && !skillEditor?.isNew,
        }),
  );

  return e("div", { className: "skills-root" }, sidebar, editor);
}

function renderSkillEditorForm({
  e,
  skillEditor,
  setSkillEditor,
  skillStatus,
  onSaveSkill,
  onDeleteSkill,
  isExisting,
}) {
  const editor = skillEditor || { name: "", scope: "project", description: "", body: "", isNew: true };
  const nameInvalid = editor.isNew && editor.name && !/^[a-z0-9][a-z0-9-]{0,62}$/.test(editor.name);

  return e(
    "div",
    { className: "skills-editor-form" },
    e(
      "div",
      { className: "skills-editor-row" },
      e("label", { className: "skills-editor-label" }, "이름"),
      editor.isNew
        ? e("input", {
            type: "text",
            className: `skills-editor-input ${nameInvalid ? "invalid" : ""}`,
            placeholder: "kebab-case-name (예: pr-summary)",
            value: editor.name || "",
            onChange: (ev) => setSkillEditor({ ...editor, name: ev.target.value }),
          })
        : e("div", { className: "skills-editor-readonly" }, editor.name),
      nameInvalid
        ? e(
            "div",
            { className: "skills-editor-hint error" },
            "소문자/숫자/하이픈만 가능합니다 (시작은 영문자나 숫자).",
          )
        : null,
    ),
    e(
      "div",
      { className: "skills-editor-row" },
      e("label", { className: "skills-editor-label" }, "범위"),
      editor.isNew
        ? e(
            "select",
            {
              className: "skills-editor-input",
              value: editor.scope || "project",
              onChange: (ev) => setSkillEditor({ ...editor, scope: ev.target.value }),
            },
            e("option", { value: "project" }, "프로젝트 (.omni/skills/)"),
            e("option", { value: "global" }, "전역 (~/.omninode/skills/)"),
          )
        : e(
            "div",
            { className: "skills-editor-readonly" },
            editor.scope === "global" ? "전역" : "프로젝트",
          ),
    ),
    e(
      "div",
      { className: "skills-editor-row" },
      e("label", { className: "skills-editor-label" }, "설명"),
      e("input", {
        type: "text",
        className: "skills-editor-input",
        placeholder: "이 스킬을 언제 쓰는지 한 줄로 적어주세요.",
        value: editor.description || "",
        onChange: (ev) => setSkillEditor({ ...editor, description: ev.target.value }),
      }),
    ),
    e(
      "div",
      { className: "skills-editor-row" },
      e("label", { className: "skills-editor-label" }, "본문 (Markdown)"),
      e("textarea", {
        className: "skills-editor-textarea",
        rows: 18,
        placeholder: "스킬의 적용 조건, 출력 형식, 예시 등을 적어주세요.",
        value: editor.body || "",
        onChange: (ev) => setSkillEditor({ ...editor, body: ev.target.value }),
      }),
    ),
    skillStatus
      ? e(
          "div",
          { className: `skills-editor-status ${skillStatus.kind || ""}` },
          skillStatus.message,
        )
      : null,
    e(
      "div",
      { className: "skills-editor-actions" },
      e(
        "button",
        {
          className: "btn-primary",
          onClick: () => onSaveSkill(editor),
          disabled: !editor.name || nameInvalid,
        },
        editor.isNew ? "생성" : "저장",
      ),
      isExisting
        ? e(
            "button",
            {
              className: "btn-danger",
              onClick: () => {
                if (window.confirm(`'${editor.name}' 스킬을 삭제하시겠습니까? 이 동작은 되돌릴 수 없습니다.`)) {
                  onDeleteSkill(editor.name, editor.scope);
                }
              },
            },
            "삭제",
          )
        : null,
    ),
  );
}

function truncate(text, n) {
  if (!text) return "";
  return text.length > n ? text.slice(0, n - 1) + "…" : text;
}
