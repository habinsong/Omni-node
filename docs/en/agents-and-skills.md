# AGENTS, Skills, and Commands

[한국어](../AGENTS_AND_SKILLS.md) · [English](./agents-and-skills.md)

Updated: 2026-05-19

![Skills tab](../assets/readme/dashboard-skills-tab.png)

AGENTS files are always-on instructions. Skills are opt-in behavior packs stored as `SKILL.md`. Commands are reusable prompt templates. Chat, Coding, and Telegram share the same skill activation and stop flow.

Current behavior:

- A selected skill is sticky per conversation and survives middleware restarts.
- The skill badge off button clears both UI selection and server-side sticky state.
- If a prompt names a skill, the prompt wins over the UI dropdown.
- Only one effective skill is allowed at a time; multiple detected skills return a clear rejection.
- URL and web-search fast paths do not bypass active skill context.
- Project skills win over global skills with the same name.
- `/skill create` and the Skills tab do not silently overwrite an existing skill.
