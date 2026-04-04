# SOP - Vertical Slice Validation

## Objective

To validate a vertical slice delivery in the **Alpla Angola Portal Gerencial** project functionally, technically, and documentally before considering the stage concluded.

## When to Use

Use this directive when an implementation is finished and it is necessary to verify if the flow is truly stable, consistent with the project, and ready for the next step.

## Expected Inputs

- Description of the delivered functionality.
- List of modified files.
- Expected functional flow.
- Related status/permission rules.
- Applicable project documentation.

## Expected Outputs

- Objective summary of what was validated.
- List of executed tests.
- Bugs, inconsistencies, or risks identified.
- Confirmation of what is ready.
- Indication of what still needs correction.

## Procedure

### 1. Understand the Validated Slice
Before testing:
- Identify the implemented flow.
- Understand the expected behavior.
- Review the modified files.
- Know which statuses should allow or block the action.

### 2. Validate the Main Happy Path
Execute the full main flow end-to-end (e.g., create, redirect, edit, add item, save, delete).
Confirm:
- Success completion.
- Correct visual feedback.
- Data persistence.
- Expected navigation.

### 3. Validate Error Scenarios
Force at least one relevant functional error:
- Empty mandatory field.
- Invalid operation by status.
- Simulated or observed API failure.
Confirm:
- Error appears clearly.
- No silent failures.
- The UI does not break.
- The user retains control of the flow.

### 4. Validate Persistence
After action completion:
- Refresh the page.
- Reopen the record.
- Go back to the list and navigate again.
Confirm:
- Data remains correct.
- Temporary messages do not reappear.
- Totals and lists remain consistent.

### 5. Validate Status and Permission Rules
Test the same screen or action in different states.
Confirm:
- Actions are visible only when permitted.
- Backend blocks improper operations even if the frontend fails.
- No bypass exists through direct UI manipulation.

### 6. Validate Side Effects
Check if the change caused regressions in related flows (e.g., post-creation, feedbacks, numbering, totals, navigation).

### 7. Technical Frontend Validation
Open DevTools and check:
- Console errors.
- Correct Network requests.
- Absence of relevant React warnings.
- No loops or duplicate requests.

### 8. Technical Backend Validation
Check:
- Backend logs for silent exceptions.
- Incorrect validations.
- Concurrency issues.
- Persistence inconsistencies.

### 9. Review Impacted Documentation
Only update what is truly affected (e.g., `FRONTEND_FOUNDATION.md`, `CHANGELOG.md`, `DECISIONS.md`).

## Definition of Done
The vertical slice is validated if:
1. The main flow works end-to-end.
2. Data persists correctly.
3. No obvious regressions exist in the same flow.
4. Errors are handled with proper feedback.
5. Status/permission rules are respected.
6. Essential documentation is consistent.
