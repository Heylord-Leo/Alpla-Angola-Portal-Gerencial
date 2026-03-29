---
trigger: always_on
---

# SOP - Task Lifecycle & Execution Governance

This directive defines the mandatory standard operating procedure (SOP) for all task execution within the Alpla Portal project.

## 1. Task Lifecycle Summary

All tasks must follow this sequential flow:

1. **Analyze**: Understand the user's intent, the affected components, and the technical context.
2. **Plan**: Draft an implementation plan for any non-trivial task. Request user approval before proceeding.
3. **Guidance Check**: Review relevant project governance and architectural files:
    - `directives/*.md`
    - `docs/*.md`
    - **`docs/DECISIONS.md`** (Canonical reference for architectural decisions)
4. **Implement**: Execute the changes in the codebase.
5. **Stop**: Notify the user that implementation is complete and wait for manual validation.
6. **Close**: Finalize the task only through official closing workflows.

## 2. Default Execution Constraints

To maintain project integrity and prevent accidental releases, the following rules apply by default:

- **No Implicit Commits**: Never perform a `git commit` as part of normal implementation work.
- **No Implicit Pushes**: Never perform a `git push` as part of normal implementation work.
- **No Implicit Versioning**: Never update `docs/VERSION.md` or `docs/CHANGELOG.md` during implementation.
- **Wait for Validation**: Always stop after implementation and wait for the user's feedback or manual testing result.

## 3. Official Closing Workflows

The only authorized ways to finalize a task are:

### `/task-review` (Read-only Review)

- Performs a status check of the work.
- Identifies modified files and summarizes changes.
- Checks if documentation updates are required.
- **Does NOT commit or push.**

### `/task-publish` (Official Release)

- The **only** workflow allowed to perform a commit and push.
- Finalizes release documentation (`CHANGELOG.md` and `VERSION.md`).
- Bumps the version according to the impact (PATCH/MINOR/MAJOR).
- Must only be invoked when the work is stable, verified, and coherent.

## 4. Governance Rule

If there is any ambiguity regarding the scope, the documentation impact, or the stability of the change, **always stop and ask for clarification** before proceeding to any closing step.
