# SOP - Post-Create Flow Correction

## Objective

To adjust the flow behavior immediately after request creation in the **Alpla Angola Portal Gerencial** project, focusing on conditional UX, user feedback, and correct navigation based on request type and state.

## Expected Inputs

- Source code in `C:\dev\alpla-portal`.
- Current observed behavior after creating a request.
- Request type (`QUOTATION`, `PAYMENT`, etc.).
- Functional rules defined in existing documentation and implementation.
- Involved components: `RequestCreate`, `RequestsList`, `RequestEdit`, etc.

## Expected Outputs

- Correct redirection after request creation.
- Clear and coherent user feedback.
- Conditional behavior by request type and status.
- No improper persistent messages or silent errors.

## Functional Rules

### 1. Post-Create Must Respect Request Type
- **QUOTATION**: Redirect to the list and display a message guiding the user to wait for quotation.
- **PAYMENT**: Redirect to the edit screen to allow line item inclusion.
Do not apply generic redirection if project rules require conditional behavior.

### 2. Useful and Temporary Feedback
- Display success or error messages clearly.
- Use auto-close patterns if standardized in the project.
- Prevent old messages from reappearing improperly after refresh or navigation.

### 3. No Silent Failures
If creation fails:
- Show error feedback to the user.
- Log the cause for diagnostic purposes.
- Do NOT redirect as if the operation succeeded.

### 4. Deterministic Navigation
- The user must land on the correct screen after creation.
- Behavior should not depend on fixed numeric IDs if a stable `Code` field exists.
- Clear temporary navigation states after feedback consumption.

## Technical Implementation

### Frontend
Verify and adjust:
- Creation payload.
- Lookup consumption (`request type`, `status`).
- Post-creation decision logic.
- Navigation using `navigate(...)`.
- Feedback transport via navigation state or local state.

### Backend
Only adjust if directly related to post-create flow:
- Incomplete creation response.
- Missing fields required for frontend decision-making.
- Type/status return inconsistencies.

## Definition of Done
The task is complete if:
1. `QUOTATION` follows correct redirection and feedback.
2. `PAYMENT` follows correct redirection and feedback.
3. Success/error messages do not get stuck indefinitely.
4. Flow remains functional after refresh.
5. Errors do not occur silently.

## Minimum Manual Verification
1. Create a `QUOTATION` request; verify redirection to list and feedback.
2. Create a `PAYMENT` request; verify redirection to edit and feedback.
3. Test creation failure and validate error feedback.
4. Refresh the page and ensure the message does not reappear.
5. Check console and network for silent failures.
