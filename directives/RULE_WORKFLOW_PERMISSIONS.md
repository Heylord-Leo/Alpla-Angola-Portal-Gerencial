# RULE - Workflow and Permissions Matrix

## 1. Objective

This directive establishes the unified permission and workflow model for the **Alpla Angola Portal Gerencial**. It governs how the request status (Stage) determines the availability of UI actions and backend operations, ensuring that the backend acts as the final security boundary.

## 2. Stage Matrix (Primary Quick-Reference)

| Stage / Status | Header Fields | Items | Attachments | Operational Action | Read-Only? | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **DRAFT** | Yes | Yes | Optional | Save / Submit | No | Full editing allowed |
| **WAITING QUOTATION** | Locked | Yes | Yes | Complete Quotation | No | Buyer must work on items/Proforma |
| **AREA ADJUSTMENT** | Yes | Yes | Yes | Re-submit | No | Returned for correction |
| **FINAL ADJUSTMENT** | Yes | Yes | Yes | Re-submit | No | Returned for correction |
| **AREA APPROVAL** | No | No | No | None | Yes | Waiting for decision |
| **FINAL APPROVAL** | No | No | No | None | Yes | Waiting for decision |
| **APPROVED** | Locked | Locked | Yes | Register PO | No | Post-approval actions exist |
| **PO_ISSUED** | Locked | Locked | Yes | Schedule Payment | No | Financial flow starts |
| **PAYMENT_SCHEDULED** | Locked | Locked | Yes | Confirm Payment | No | Finance intervention |
| **PAYMENT_COMPLETED** | Locked | Locked | Yes | Move to Receipt | No | Evidence required |
| **WAITING_RECEIPT** | Locked | Locked | Yes | Finalize Order | No | Final verification |
| **COMPLETED** | No | No | Download | None | Yes | Fully read-only |
| **REJECTED / CANCELED** | No | No | Download | None | Yes | Permanent history |

> [!IMPORTANT]
> **Unified Post-PO Rule**: Starting from `PO_ISSUED`, both `QUOTATION` and `PAYMENT` requests follow the same operational lifecycle.

## 3. Interpretation & Nuance Rules

### 3.1 Read-only Interpretation

- **Read-only** (`isReadOnly`) is true **only** when no further user intervention is expected in the current or future workflow steps (e.g., `COMPLETED`, `REJECTED`, `AREA_APPROVAL`).
- **Locked** fields mean they are read-only for *that specific field group*, even if the overall screen is not read-only.

### 3.2 Precedence of Status

- Logic MUST depend on the `statusCode`.
- A request is editable (mutation allowed) **only** in `DRAFT`, `AREA_ADJUSTMENT`, or `FINAL_ADJUSTMENT`.
- Exception: **WAITING QUOTATION** allows item mutation (Buyer role) but locks Header fields to preserve original requester intent.

### 3.3 Attachment Rules

- **Deletion**: Permitted **only** in editable states and `WAITING_QUOTATION`. In all other states, attachments must be preserved for audit integrity.
- **Addition**: Permitted in post-approval stages (`PO_ISSUED` onwards) to fulfill workflow requirements (e.g., Payment Proof).

### 3.4 Backend as Security Boundary

- Even if UI elements are hidden/disabled, the backend **MUST** re-validate the rule independently.
- Backend must return `400`, `403`, or `409` if an operation is attempted in an invalid state.

## 4. Implementation Guidance

### 4.1 Permission Flags

Use explicit boolean flags derived from the matrix to drive the UI:

- `canEditHeader`: Permits modification of request metadata.
- `canEditItems`: Permits adding, editing, or removing line items.
- `canManageAttachments`: Permits adding/deleting attachments (subject to 3.3).
- `canExecuteOperationalAction`: Permits workflow transitions (Submit, Approve, Register PO).
- `isReadOnly`: Global indicator for fully locked screens.

### 4.2 Ambiguity Resolution

- In case of conflict between the UI and the Matrix, the **Matrix is the source of truth.**
- If a status is not specifically listed, it defaults to `Read-Only: Yes` until explicitly defined.
- If a user role is not authorized for a stage (e.g., Requester in `WAITING_QUOTATION`), the screen should be `Read-Only` for that user regardless of the Stage Matrix permissions.

