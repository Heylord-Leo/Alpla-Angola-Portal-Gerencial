# Alpla Angola Gerencial - Portal

## Overview

This repository contains the core structure of the **Alpla Angola Gerencial** project. The main focus is to provide an automated and organized environment using the **DOE** (Directives, Orchestration, Execution) agent architecture, separating intention, decision, and deterministic execution.

## Folder Structure

### DOE Architecture (Core)

- `directives/` – Instructions and manuals in Markdown format (SOPs, defines what the agent should do).
- `execution/` – Executable and deterministic Python scripts focused on processes (defines how to do it).
- `.tmp/` – Temporary files, logs (`.tmp/logs/`), and process artifacts generated at runtime (ignored by git).

### Support Folders

- `docs/` – General project documentation. Start by reading `docs/PROJECT_OVERVIEW.md`.
- `src/` – Modules and source code with reusable logic.
- `tests/` – Automated tests.
- `data/` – Persistent local reference data or examples (fixtures).
- `configs/` – Configuration files and templates (Use `.env` for sensitive data).

## Getting Started (Basic Setup)

1. **Clone the repository** to your local machine.
2. **Configure the Environment**:
   - Copy `.env.example` to create your `.env` file.
   - Fill in the required environment variables (never commit the `.env` file).
3. **Install Dependencies** for the Python scripts in `execution/` and `src/`.
4. **Read the Documentation**:
   - See `docs/PROJECT_OVERVIEW.md` to understand the project scope.
   - Read `directives/README.md` and `execution/README.md` for DOE development rules.
5. **Agent Governance**:
   - Refer to `AGENTS.md` for core execution guardrails and priorities.
