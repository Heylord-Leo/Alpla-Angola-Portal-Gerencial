# Alpla Angola - Portal Gerencial | Agent Instructions

> This file is the source of truth for AI execution behavior in this project. It is mirrored across `GEMINI.md`, `CLAUDE.md`, and `AGENTS.md`.

## 1. Mandatory Task Lifecycle (Root SOP)

All task execution in this repository MUST follow the standard operating procedure defined in:
👉 **[directives/SOP_TASK_LIFECYCLE.md](file:///C:/dev/alpla-portal/directives/SOP_TASK_LIFECYCLE.md)**

**Summary of Enforcement:**
- **Analyze & Plan**: Always present an implementation plan for non-trivial tasks.
- **Guidance Checks**: Read `directives/*.md`, `docs/*.md`, and `docs/DECISIONS.md` before implementation.
- **Stop & Validate**: Implementation must stop and wait for manual user validation.
- **No Default Persistence**: Commits and pushes are strictly reserved for the `/task-publish` workflow.

## 2. The 3-Layer Architecture

1.  **Layer 1: Directive (What to do)**: SOPs in `directives/`.
2.  **Layer 2: Orchestration (Decision making)**: The AI agent.
3.  **Layer 3: Execution (Doing the work)**: Deterministic scripts in `.agents/scripts/` (if any).

## 3. Project Governance

- **Source of Truth**: `docs/CHANGELOG.md` is the primary record for project history and versioning.
- **Documentation Hygiene**: Update only what is truly impacted, maintaining high markdown standards.
- **Security Check**: For any authentication or session logic, refer to `FRONTEND_FOUNDATION.md` and `SOP_TASK_LIFECYCLE.md`.

## 4. Standard Commands

- **`/task-review`**: Read-only status check and analysis of changes.
- **`/task-publish`**: Formalizes the release, bumps version, and performs `git commit`/`git push`.

---
*For more details on specific features or architecture, refer to the `docs/` directory.*
