# Agent System Architecture

This document describes the design philosophy and structural hierarchy of the agent instructions, directives, and workflows used in the **Alpla Angola Portal Gerencial** project.

> [!NOTE]
> This document is for **reference only**. It explains the architecture but does not contain executable instructions. For operational rules, see `AGENTS.md` and `directives/`.

## 1. The 3rd Layer Architecture

The agent system is organized into three distinct layers to maximize reliability, consistency, and deterministic execution.

| Layer | Component | Description |
| :--- | :--- | :--- |
| **Layer 1** | **Directives (SOPs/Rules)** | Deterministic natural language instructions (Markdown). They define "What to do" and "How to think." |
| **Layer 2** | **Orchestration (Agent)** | The AI agent (you). Responsible for reading directives, making decisions, and sequencing tools. |
| **Layer 3** | **Execution (Workflows/Scripts)** | Operational commands (slash-commands) and deterministic scripts that perform the actual work. |

## 2. SDD & DOE Philosophy

This project follows a **Spec-Driven Development (SDD)** and **Design of Experiments (DOE)** approach to agent instructions:

- **SDD (Spec-Driven)**: Every implementation phase starts with an `implementation_plan.md` derived from the project's specifications and current directives.
- **DOE (Design of Experiments)**: We treat instructions as experiments. If a task fails or a pattern breaks, we update the corresponding directive (`SOP_` or `RULE_`) to prevent future regressions.

## 3. Instruction Hierarchy

1.  **Operational Root (`AGENTS.md`)**: The primary entry point. Contains core guardrails and execution priorities.
2.  **SOPs (`directives/SOP_*.md`)**: Standard Operating Procedures for processes (Lifecycle, Documentation, Closing).
3.  **Rules (`directives/RULE_*.md`)**: Static rules for domains (Status, Permissions, UI Standards).
4.  **Workflows (`.agents/workflows/`)**: Lean operational prompts that invoke the above layers.

## 4. Maintenance Rule

When updating directives:
- **Consolidate** instead of duplicate.
- **English-first** content for model consistency.
- **Reference Integrity**: Always update cross-references when a file name or path changes.
