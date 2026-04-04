---
description: Official task finalization, logging, and commit/push
---

> [!IMPORTANT]
> This workflow follows the standard operating procedure defined in:
> 👉 **[directives/SOP_TASK_CLOSING.md](file:///C:/dev/alpla-portal/directives/SOP_TASK_CLOSING.md)**
>
> This is the **ONLY** authorized workflow for `git commit` and `git push`.

1. **Validation**: Ensure all steps from `/task-review` are satisfied and the work is stable.
2. **Persistence**: Finalize documentation (`CHANGELOG.md`, `VERSION.md`) and perform Git commit/push.
3. **Convention**: Use **Conventional Commits** (`feat:`, `fix:`, `docs:`, etc.) for the message.
4. **Safety**: Only push to `origin/main` when the task is fully coherent and verified.
