# RULE - Stage Matrix (Workflow Permission Model)

## Objective

This matrix replaces simplistic page-mode rules (like `isDraft ? editable : readOnly`) with a nuanced permission model. The system distinguishes between header editing, item editing, attachment management, and operational workflow actions.

## Proposed Stage Matrix

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

## Interpretation Rules

### 1. Read-only is not just "not draft"
A request is truly read-only only when no further user intervention is expected (e.g., `COMPLETED`).

### 2. Protected Header != Fully Blocked
Some stages protect header fields while allowing attachment uploads or workflow actions.

### 3. Approved != Read-only
For **APPROVED** or **PO_ISSUED**, the screen must allow PO registration, payment proof uploads, etc.

## Recommended Implementation

Replace generic logic with explicit permission flags:
- `canEditHeader`
- `canEditItems`
- `canManageAttachments`
- `canExecuteOperationalAction`
- `isReadOnly`
