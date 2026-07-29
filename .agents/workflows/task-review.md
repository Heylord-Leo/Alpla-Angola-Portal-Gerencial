---
description: Read-only task state review without commit or push
---

> [!NOTE]
> This workflow follows the standard operating procedure defined in:
> 👉 **[directives/SOP_TASK_CLOSING.md](file:///C:/dev/alpla-portal/directives/SOP_TASK_CLOSING.md)**

> [!IMPORTANT]
> `/task-review` is **strictly read-only**. It MUST NOT stage, commit, push, switch
> branches, or modify `docs/VERSION.md`, `docs/CHANGELOG.md`, `src/frontend/src/config.ts`,
> or any other file. It only inspects and reports. Only `/task-publish` may persist changes.

## 1. Working-tree analysis

1. **Branch & HEAD**: report the current local branch and short HEAD. Confirm HEAD is **not detached**.
2. **Full status**: inspect **all** modified, staged, unstaged, and untracked files (`git status`), not only the files you expect.
3. **Intended file set**: state the set of files this task was meant to change.
4. **Unrelated / other-agent files**: compare the intended set against the complete working tree. This repository may be edited by more than one agent (e.g. Claude and Antigravity) at the same time — **never assume every pending file belongs to this task**. List any pending file outside the intended set as *unrelated / to be preserved*.
5. **Shared-file overlap**: flag any file in the intended set that also appears to carry changes from another task. Report the overlap; do **not** attempt to split or rewrite it.

## 2. Change & impact analysis

1. **Implementation vs plan**: review code and UX against the agreed design.
2. **Security**: check for accidental secrets, credentials, or tokens; confirm no secret is placed in `VITE_*` variables or other browser-visible metadata.
3. **Migrations**: detect any added/changed EF Core migration.
4. **Deployment-sensitive changes**: detect changes under `.github/workflows/`, `appsettings*.json`, `src/frontend/public/web.config`, IIS/deploy scripts, or runner labels, and note their TEST/PROD impact and rollback considerations.
5. **Documentation hygiene**: verify whether `directives/`, `docs/`, `CHANGELOG.md`, and `VERSION.md` require updates (per SOP §3 — update only what was truly impacted).
6. **Guided Tour impact (assessment only)**: determine whether the task added/removed/changed routes, pages, menus, drawers, modals, panels, or important actions that would require a tour update. Creating/updating tours is performed in `/task-publish`; here, only assess and record the expected result.

## 3. Validation status

Report the status of each applicable gate using the vocabulary in §5. **Never report a gate as PASS unless it was actually executed and passed** — use `NOT EXECUTED` or `NOT APPLICABLE` honestly.

- backend build;
- frontend `tsc --noEmit`;
- frontend Vite build;
- relevant automated tests (not mandatory for every task unless required/requested);
- manual validation expectation;
- Markdown/structure validation for documentation-only tasks.

## 4. Release consistency (read-only)

- Version sources per SOP: **`docs/CHANGELOG.md` is authoritative**, `docs/VERSION.md` follows it, `src/frontend/src/config.ts` is the UI mirror. Report whether the three agree (do not change them).
- If a version increment appears required (PATCH/MINOR/MAJOR per SOP §2.1), state the proposed level; if ambiguous, defer to user confirmation.
- Sanity-check any proposed changelog wording against the actual diff (no unimplemented claims).

## 5. Conclusion — standardized readiness report

Summarize with per-item status from this vocabulary:

```
PASS | FAIL | WARNING | NOT APPLICABLE | NOT EXECUTED | AWAITING AUTHORIZATION
```

Report at least:

- **Task scope**: task name · branch · HEAD · intended files · actual modified files · staged files · unrelated pending files · migrations detected · workflow/config changes detected.
- **Validation**: source review · backend build · frontend TypeScript · frontend Vite build · relevant tests · manual validation · security review · deployment impact · rollback considerations.
- **Release**: previous version · proposed version · increment type · authoritative version source · changelog status · documentation status · Guided Tour impact · build-metadata readiness.

End with an explicit verdict: **READY for `/task-publish`** or **NOT READY**, listing blockers and warnings. `/task-review` never commits, never pushes, and never updates version or changelog.
