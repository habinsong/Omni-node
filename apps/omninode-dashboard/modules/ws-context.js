export function requestContextScan(send, options = {}) {
  return send({ type: "context_scan" }, options);
}

export function requestSkillsList(send, options = {}) {
  return send({ type: "skills_list" }, options);
}

export function requestCommandsList(send, options = {}) {
  return send({ type: "commands_list" }, options);
}

export function requestSkillGet(send, name, scope = "project", options = {}) {
  return send({ type: "skill_get", skillName: name, skillScope: scope }, options);
}

export function requestSkillSave(send, { name, scope = "project", description = "", body = "", allowOverwrite = false }, options = {}) {
  return send(
    {
      type: "skill_save",
      skillName: name,
      skillScope: scope,
      skillDescription: description,
      skillBody: body,
      skillAllowOverwrite: !!allowOverwrite,
    },
    options,
  );
}

export function requestSkillDelete(send, name, scope = "project", options = {}) {
  return send({ type: "skill_delete", skillName: name, skillScope: scope }, options);
}

export function requestSkillActiveClear(send, conversationId, options = {}) {
  return send({ type: "skill_active_clear", conversationId }, options);
}
