# RULE - Status and Permissions

## Objective

To ensure that UI actions and backend operations in the **Alpla Angola Portal Gerencial** project respect the request status and user permissions, with the backend acting as the final security boundary.

## Expected Inputs

- Current request status.
- Action being implemented or adjusted.
- User role or applicable access rule.
- Current screen behavior and involved endpoints.

## Expected Outputs

- Visible actions only when they make functional sense.
- Mutations blocked in non-editable states.
- Backend re-validating the rule independently of the UI.

## Mandatory Principles

### 1. UI is Not the Only Protection
Even if a button is hidden on the frontend, the backend MUST validate the rule again.

### 2. Status Governs the Action
Action availability must depend on the actual status of the request.

### 3. Permission and Status Work Together
User access to a screen is not enough. The operation must also make sense for the current state of the document.

## Practical Rules

### Editable Request
A request is considered editable only in statuses officially permitted by the project:
- `DRAFT`
- Equivalent rework states (e.g., `AREA_ADJUSTMENT`, `FINAL_ADJUSTMENT`), if implemented.

In these cases:
- Header editing is permitted (except in `WAITING_QUOTATION` to preserve original intent).
- Adding, editing, and removing items is allowed.
- Draft deletion is allowed if the project rule permits.

### Non-Editable Request
In non-editable states:
- Hide or disable mutation actions.
- Prevent adding, editing, or deleting items.
- Prevent draft deletion.
- Ensure the backend returns appropriate errors if the operation is attempted directly.

## Expected Patterns

### Frontend
- Conditional buttons based on `statusCode`.
- Clear messages when an operation fails.
- Do not leave an action visible if the rule completely blocks its use.

### Backend
- Validate the existence of the request.
- Validate the permitted status for the operation.
- Validate user permission/authorization.
- Return consistent errors (e.g., `400`, `403`, or `409`).

## Application Examples

### Delete Draft
- Show button only in `DRAFT` status.
- Confirm the action with the user.
- Backend only allows deletion if the status remains `DRAFT`.

### Manage Quotations (Workspace)
- Allow add/edit/delete only in editable requests.
- Backend must block mutation in non-editable requests even if the route is called manually.

### Manage Request Attachments
- Allow removal only in editable states (`DRAFT`, `AREA_ADJUSTMENT`, `FINAL_ADJUSTMENT`, `WAITING_QUOTATION`).
- Backend must block deletion in any other status to preserve audit integrity.
- Adding attachments is permitted in post-approval operational stages to fulfill workflow requirements (e.g., Payment Proof).

### Sensitive Workflow Fields
- Do not expose fields belonging to the approver stage or later flow to the requester.
- Keep the field in the backend/model if necessary, but outside the requester's UI.

## Definition of Done

Implementation is correct when:
1. The UI shows only valid actions for the current status.
2. The backend prevents invalid operations.
3. No inconsistency exists between what the UI allows and what the API accepts.
4. Error scenarios return comprehensible feedback.

## Edge Cases
- User opens the screen in an editable status, but another process changes the status before saving.
- User attempts to call the endpoint manually outside the UI.
- Item or request was already removed when the action is sent.
- Silent error feedback on the frontend.
- Status displayed on the screen is outdated compared to the backend.
