# RULE - Regression Debugging

## Objective

To investigate and fix regressions introduced by recent changes in the **Alpla Angola Portal Gerencial** project, identifying the root cause and correcting the issue without unnecessarily expanding scope.

## When to Use

Use this rule when functionality that previously worked begins to fail after a recent adjustment, especially in flows such as:
- Request creation/edition.
- Draft deletion.
- Numbering logic.
- Feedbacks.
- Line items.
- Status/type conditional rules.

## Expected Inputs

- Clear description of the observed problem.
- Last change or prompt preceding the bug.
- Affected screen/flow.
- Visible error message, if any.
- Recently modified files or modules.

## Expected Outputs

- Root cause identified.
- Correction applied.
- Modified files listed.
- Objective technical summary.
- Manual verification performed.

## Procedure

### 1. Reproduce the Issue
Do not start fixing blindly.
- Reproduce the error locally.
- Confirm the exact breaking flow.
- Identify if the error is consistent or intermittent.

### 2. Delimit the Regression
Compare current behavior with expected behavior.
- What changed recently?
- What was the last related prompt/adjustment?
- Is the failure in the frontend, backend, integration, validation, or data?

### 3. Inspect Recent Changes
Review files touched by the most recent change first, prioritizing:
- Submit/click handlers.
- Payloads and DTOs.
- Validations and transactions.
- Numbering logic.
- Status/type conditionals.
- Feedback/navigation side effects.

### 4. Verify Technical Signals
Check:
- Browser Console.
- Network tab (request/response, HTTP status, error body).
- Backend logs and stack traces.

### 5. Identify the Root Cause
Do not stop at superficial symptoms. Real causes might include:
- Field removed in frontend but still required in backend.
- Improper button types (`submit` vs `button`).
- Undefined request IDs.
- Uncommitted transactions.
- Incorrect endpoints or unique constraint collisions.

### 6. Fix with Minimal Scope
Apply the smallest correct fix possible. Avoid broad refactorings or unrelated screen redesigns.

### 7. Revalidate the Main Flow
After the fix:
- Reproduce the original scenario.
- Confirm the issue is resolved.
- Test the nearest "happy path" and potential direct side effects.

## Definition of Done

The regression is considered fixed only if:
1. The root cause is clearly identified.
2. The fix resolves the original problem.
3. The related flow remains functional.
4. No obvious new breaks occur in the same path.
5. Manual validation evidence is provided.
