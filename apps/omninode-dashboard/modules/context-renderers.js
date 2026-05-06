function formatContextTimestamp(value) {
  const raw = `${value || ""}`.trim();
  if (!raw) {
    return "-";
  }

  const parsed = new Date(raw);
  if (Number.isNaN(parsed.getTime())) {
    return raw;
  }

  return parsed.toLocaleString("ko-KR", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false
  });
}

function formatContextAction(value) {
  const normalized = `${value || ""}`.trim().toLowerCase();
  if (normalized === "scan") {
    return "문맥 스캔";
  }
  if (normalized === "skills") {
    return "skill 조회";
  }
  if (normalized === "commands") {
    return "command 조회";
  }
  return "-";
}

function renderInstructionSourcesTable(e, sources) {
  const rows = Array.isArray(sources) ? sources : [];
  return e("div", { className: "table-wrap" },
    e("table", { className: "model-table" },
      e("thead", null,
        e("tr", null,
          e("th", null, "순서"),
          e("th", null, "범위"),
          e("th", null, "경로")
        )
      ),
      e("tbody", null,
        rows.length === 0
          ? e("tr", null, e("td", { colSpan: 3 }, "발견된 instruction source가 없습니다."))
          : rows.map((source) => e("tr", { key: `${source.path}:${source.order}` },
            e("td", null, String(source.order || "-")),
            e("td", null, source.scope || "-"),
            e("td", null, source.path || "-")
          ))
      )
    )
  );
}

function renderSkillTable(e, skills) {
  const rows = Array.isArray(skills) ? skills : [];
  return e("div", { className: "table-wrap" },
    e("table", { className: "model-table" },
      e("thead", null,
        e("tr", null,
          e("th", null, "이름"),
          e("th", null, "범위"),
          e("th", null, "설명"),
          e("th", null, "경로")
        )
      ),
      e("tbody", null,
        rows.length === 0
          ? e("tr", null, e("td", { colSpan: 4 }, "발견된 skill이 없습니다."))
          : rows.map((skill) => e("tr", { key: `${skill.path}:${skill.name}` },
            e("td", null, skill.name || "-"),
            e("td", null, skill.scope || "-"),
            e("td", null, skill.description || "-"),
            e("td", null, skill.path || "-")
          ))
      )
    )
  );
}

function renderCommandTable(e, commands) {
  const rows = Array.isArray(commands) ? commands : [];
  return e("div", { className: "table-wrap" },
    e("table", { className: "model-table" },
      e("thead", null,
        e("tr", null,
          e("th", null, "이름"),
          e("th", null, "범위"),
          e("th", null, "요약"),
          e("th", null, "경로")
        )
      ),
      e("tbody", null,
        rows.length === 0
          ? e("tr", null, e("td", { colSpan: 4 }, "발견된 command template이 없습니다."))
          : rows.map((command) => e("tr", { key: `${command.path}:${command.name}` },
            e("td", null, command.name || "-"),
            e("td", null, command.scope || "-"),
            e("td", null, command.summary || "-"),
            e("td", null, command.path || "-")
          ))
      )
    )
  );
}

export function renderContextPanel(props) {
  const {
    e,
    authed,
    contextState,
    refreshProjectContext,
    refreshSkillsList,
    refreshCommandsList
  } = props;

  const snapshot = contextState.snapshot || null;
  const instructions = snapshot?.instructions || null;
  const sources = Array.isArray(instructions?.sources) ? instructions.sources : [];
  const skills = Array.isArray(contextState.skills) && contextState.skills.length > 0
    ? contextState.skills
    : (Array.isArray(snapshot?.skills) ? snapshot.skills : []);
  const commands = Array.isArray(contextState.commands) && contextState.commands.length > 0
    ? contextState.commands
    : (Array.isArray(snapshot?.commands) ? snapshot.commands : []);
  const disabled = !authed || contextState.loading || contextState.loadingSkills || contextState.loadingCommands;
  const instructionText = `${instructions?.combinedText || ""}`.trim();

  return e("section", { className: "panel settings-optimized-panel settings-context-panel" },
    e("div", { className: "plans-panel-head" },
      e("div", null,
        e("h2", null, "프로젝트 문맥"),
        e("p", { className: "hint" }, "AGENTS, fallback 문서, `.omni/skills`, `.omni/commands`를 한 번에 스캔합니다.")
      ),
      e("div", { className: "row plans-head-actions" },
        e("button", {
          className: "btn",
          disabled,
          onClick: refreshProjectContext
        }, contextState.loading ? "스캔 중..." : "문맥 스캔"),
        e("button", {
          className: "btn",
          disabled,
          onClick: refreshSkillsList
        }, contextState.loadingSkills ? "조회 중..." : "skills 새로고침"),
        e("button", {
          className: "btn",
          disabled,
          onClick: refreshCommandsList
        }, contextState.loadingCommands ? "조회 중..." : "commands 새로고침")
      )
    ),
    contextState.lastError
      ? e("div", { className: "error-banner" }, contextState.lastError)
      : null,
    e("div", { className: "plan-summary-grid" },
      e("div", { className: "doctor-summary-card" },
        e("div", { className: "doctor-summary-label" }, "instruction"),
        e("div", { className: "doctor-summary-value plan-status-value" }, String(sources.length))
      ),
      e("div", { className: "doctor-summary-card" },
        e("div", { className: "doctor-summary-label" }, "skill"),
        e("div", { className: "doctor-summary-value plan-status-value" }, String(skills.length))
      ),
      e("div", { className: "doctor-summary-card" },
        e("div", { className: "doctor-summary-label" }, "command"),
        e("div", { className: "doctor-summary-value plan-status-value" }, String(commands.length))
      ),
      e("div", { className: "doctor-summary-card" },
        e("div", { className: "doctor-summary-label" }, "스냅샷"),
        e("div", { className: "doctor-summary-value plan-status-value" }, contextState.loaded ? "완료" : "대기")
      )
    ),
    e("article", { className: "plan-detail-card" },
      e("div", { className: "plans-section-head" },
        e("strong", null, "스냅샷 정보"),
        e("span", { className: "tiny" }, formatContextTimestamp(snapshot?.scannedAtUtc))
      ),
      e("div", { className: "tiny" }, `프로젝트 루트: ${snapshot?.projectRoot || "-"}`),
      e("div", { className: "tiny" }, `현재 디렉터리: ${snapshot?.currentDirectory || "-"}`),
      e("div", { className: "tiny" }, `마지막 액션: ${formatContextAction(contextState.lastAction)}`)
    ),
    e("article", { className: "plan-detail-card" },
      e("div", { className: "plans-section-head" }, e("strong", null, "Instruction 소스")),
      renderInstructionSourcesTable(e, sources)
    ),
    e("article", { className: "plan-detail-card" },
      e("div", { className: "plans-section-head" }, e("strong", null, "병합 문맥")),
      instructionText
        ? e("pre", { className: "doctor-check-detail" }, instructionText)
        : e("div", { className: "empty plan-empty-state" }, "병합된 instruction text가 없습니다.")
    ),
    e("article", { className: "plan-detail-card" },
      e("div", { className: "plans-section-head" }, e("strong", null, "Skill 목록")),
      renderSkillTable(e, skills)
    ),
    e("article", { className: "plan-detail-card" },
      e("div", { className: "plans-section-head" }, e("strong", null, "Command 템플릿")),
      renderCommandTable(e, commands)
    )
  );
}
