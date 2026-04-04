# Antigravity Directives - Alpla Angola Portal Gerencial

This package gathers base directives to guide the Antigravity AI assistant with more consistency in the **Alpla Angola Portal Gerencial** project.

## How to Use

When starting a new task, explicitly reference the applicable directive in the prompt, for example:
- "Follow `SOP_TASK_LIFECYCLE.md`"
- "Respect `RULE_WORKFLOW_PERMISSIONS.md`"
- "Apply `RULE_KPI_DASHBOARD.md`"

## Included Directives (Active Baseline)

### Process SOPs (Standard Operating Procedures)
0. `SOP_TASK_LIFECYCLE.md` (MANDATORY: Analyze, Plan, Guidance Check, Stop/Validate)
1. `SOP_TASK_CLOSING.md` (Task Review and Publication/Git persistency, includes Doc Hygiene)

### Domain Rules
2. `RULE_WORKFLOW_PERMISSIONS.md` (Unified Status/Stage and Action permissions)
3. `RULE_KPI_DASHBOARD.md` (KPI card consistency and interaction)
4. `RULE_ORDER_NUMBERING.md` (Unique and secure request numbering)
5. `RULE_REGRESSION_DEBUGGING.md` (Investigating and fixing recent breaks)

## Reference and Legacy Materials

For historical context or specific milestone validation, refer to:
- `docs/rules/reference/SOP_VERTICAL_SLICE_VALIDATION.md`
- `docs/rules/legacy/RULE_ITEM_IMPLEMENTATION.md`
- `docs/rules/legacy/SOP_POST_CREATE_FLOW.md`

## Default Project Rules

Always start a task by reading `SOP_TASK_LIFECYCLE.md` to ensure governance compliance.

### Important Project Rules
- Do NOT work on deployment.
- Focus only on the specific feature or correction requested.
- If you find a directly related issue in the same flow, fix it too.

### Documentation Hygiene
If a change affects documented UX behavior, update only what is truly impacted, especially:
- `docs/FRONTEND_FOUNDATION.md`
- `docs/CHANGELOG.md`
- `docs/DECISIONS.md` (if needed)
