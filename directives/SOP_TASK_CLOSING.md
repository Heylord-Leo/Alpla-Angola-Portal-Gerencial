# SOP - Task Closing (Review & Publish)

> [!IMPORTANT]
> This directive is an extension of **`SOP_TASK_LIFECYCLE.md`**. Task closing must only be initiated after the implementation phase is complete and manual validation has been performed.

## 1. Objective

To ensure every completed task in the project follows a standardized process of quality review, documentation reconciliation, and versioning before being officially delivered to the repository.

## 2. Closing Workflows

The system provides two operational workflows (slash-commands) to satisfy this SOP.

### 2.1 Review (`/task-review`)

**Focus**: Quality and impact analysis without permanent changes.

1. **Git Status**: Identify modified and untracked files.
2. **Change Analysis**: Review implementation (code and UX) against the original plan.
3. **Documentation Audit**: Verify if `directives/`, `docs/`, `CHANGELOG.md`, and `VERSION.md` require updates.
4. **Versioning Impact**: Evaluate functional impact for versioning (PATCH/MINOR/MAJOR).
   - **PATCH**: Localized bugfixes or style adjustments.
   - **MINOR**: New functional features or significant workflow improvements.
   - **MAJOR**: Breaking changes or large module delivery milestones (Stages).
5. **Recommendation**: Inform the user if the work is ready for publication or requires further attention.

### 2.2 Publish (`/task-publish`)

**Focus**: Finalization, official record, and commit/push. This is the **ONLY** authorized workflow for Git persistence.

1. **Final Review**: Execute all steps from the Review phase.
2. **Integrity Check**: If there are unstable or unrelated changes, the process must stop and request cleanup.
3. **Update Docs**: Apply required changes to `docs/` and `directives/`.
4. **Versioning**:
   - Update `docs/CHANGELOG.md` with a concise delivery description.
   - Update `docs/VERSION.md` with the new version number.
5. **Service Restart**: If code was changed, restart backend and frontend services before final confirmation (if requested).
6. **Commit**: Create a commit using **Conventional Commits** (`feat:`, `fix:`, `docs:`, `refactor:`, etc.).
7. **Push**: Send to `origin/main` only when the work is stable and coherent.

## 3. Documentation Hygiene (Mandatory)

To keep project documentation consistent and minimal, follow these analysis rules during any closing phase:

### 3.1 Primary Rule

**Update ONLY what was truly impacted.** Avoid rewriting broad documentation without absolute necessity.

### 3.2 Analysis Hierarchy

- **`docs/FRONTEND_FOUNDATION.md`**: Update only if there is a real change in UX behavior, component patterns, or reusable CSS tokens.
- **`docs/CHANGELOG.md`**: Add concise entries for functional fixes, UX improvements, or new capabilities.
- **`docs/DECISIONS.md`**: Update only if a project-wide architectural decision was made or modified.

### 3.3 Edge Cases

- **Technical change (no UX impact)**: Update only `CHANGELOG.md`.
- **UX change (no architectural impact)**: Update `FRONTEND_FOUNDATION.md` and `CHANGELOG.md`.
- **Structural or domain change**: Update `DECISIONS.md`.

## 4. Golden Rules

- **Stability First**: Never publish if the work is not a stable and functional unit.
- **Truth Source**: `CHANGELOG.md` is the absolute truth for versioning; `VERSION.md` simply follows it.
- **User Validation**: If there is ambiguity regarding the versioning level (Patch/Minor/Major), always request user confirmation.
- **No Implicit Writes**: Actions that modify Git must be explicitly confirmed or localized to the `/task-publish` prompt.
