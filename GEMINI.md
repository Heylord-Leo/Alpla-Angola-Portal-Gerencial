# Alpla Angola - Portal Gerencial | Agent Instructions

> [!IMPORTANT]
> This file is the primary source of truth for agent execution behavior. It is mirrored across `AGENTS.md`, `CLAUDE.md`, and `GEMINI.md`.

## 1. Execution Priorities

- **Process-Driven**: Always follow the mandatory task lifecycle before implementation.
- **Spec-Driven (SDD)**: Create or update an `implementation_plan.md` for every non-trivial task.
- **Reference Integrity**: Update only what is truly impacted, maintaining high documentation standards.
- **Safety First**: Commits and pushes are strictly reserved for the `/task-publish` workflow.

## 2. Mandatory Task Lifecycle (Root SOP)

All task execution in this repository MUST follow the standard operating procedure defined in:
👉 **[directives/SOP_TASK_LIFECYCLE.md](file:///C:/dev/alpla-portal/directives/SOP_TASK_LIFECYCLE.md)**

**Summary of Enforcement:**
- **Analyze & Plan**: Present an implementation plan for non-trivial presentations.
- **Guidance Checks**: Review active baseline rules in `directives/`:
    - `RULE_WORKFLOW_PERMISSIONS.md` (Status & Stages)
    - `RULE_KPI_DASHBOARD.md` (UI Consistency)
    - `RULE_ORDER_NUMBERING.md` (Business Identifiers)
    - `RULE_REGRESSION_DEBUGGING.md` (Fix Analysis)
- **Document Integrity**: Update `docs/` and `CHANGELOG.md` following Documentation Hygiene rules.
- **Stop & Validate**: Stop implementation and wait for manual user validation.

## 3. The 3-Layer Architecture

1.  **Layer 1: Directive (What to do)**: Structured SOPs and Rules in `directives/`.
2.  **Layer 2: Orchestration (Agent)**: You, the AI assistant.
3.  **Layer 3: Execution (Workflows)**: Operational prompts in `.agents/workflows/`.

## 4. Standard Commands

- **`/task-review`**: Read-only status check and impact analysis.
- **`/task-publish`**: Formalizes the release, updates versioning, and performs Git persistence.

---
*For design philosophy and architecture, refer to [docs/agent-system/ARCHITECTURE.md](file:///C:/dev/alpla-portal/docs/agent-system/ARCHITECTURE.md).*
