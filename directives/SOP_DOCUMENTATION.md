# SOP - Documentation Maintenance

## Objective

To keep the **Alpla Angola Portal Gerencial** project documentation consistent, minimal, and aligned with real changes, without generating excessive or irrelevant updates.

## Expected Inputs

- Code implementation changes.
- Observed functional or UX impact.
- Existing documentation files in the project.

## Expected Outputs

- Updates only to the documentation files truly impacted.
- A concise changelog entry.
- Updated architectural decisions in `DECISIONS.md` only if the change alters a project-wide decision.

## Principle Rule

**Update ONLY what was truly impacted.**

Avoid rewriting broad documentation without absolute necessity.

## Analysis Order

### 1. `FRONTEND_FOUNDATION.md` and `UI_UX_GUIDELINES.md`

Update only if there is a real change in:
- UX behavior.
- Feedback patterns.
- Screen structure.
- Reusable frontend component patterns or **new Tailwind tokens** mapped from CSS.
- Official user interaction flows.

### 2. `CHANGELOG.md`

Update when there is:
- A relevant functional fix.
- UX improvement.
- New reusable component.
- New endpoint or visible functional capability.

The entry must be objective and proportional to the impact.

### 3. `DECISIONS.md`

Update only if the implementation:
- Introduces a new architectural decision.
- Modifies an existing formal decision.
- Consolidates a structural rule that must remain registered.

Do **NOT** use `DECISIONS.md` for small UI adjustments that do not change the project's direction.

## Definition of Done

Documentation is adequate when:
1. It reflects the real implementation change.
2. It contains no redundant information.
3. It does not contradict the system's current behavior.
4. It remains consistent with other core MD files.

## Edge Cases

- **Technical change with no UX impact**: Update only `CHANGELOG.md` if relevant.
- **UX change without architectural decision**: Update `FRONTEND_FOUNDATION.md` and `CHANGELOG.md`.
- **Structural domain or architecture change**: Update `DECISIONS.md`.
- **Divergent old documentation**: Correct only the part proven to be affected.

## Default Project Rules

### Important Project Rules
- Do NOT work on deployment.
- Focus only on the specific task or correction.
- If you find a directly related issue in the same flow, fix it too.

### Documentation Hygiene
If this changes documented UX behavior, update only what is truly impacted, especially:
- `FRONTEND_FOUNDATION.md`
- `CHANGELOG.md`
- `DECISIONS.md` (if needed)
