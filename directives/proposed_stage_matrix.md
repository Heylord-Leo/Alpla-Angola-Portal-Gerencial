# Proposed Stage Matrix - Workflow Permission Model

This matrix is intended to replace simplistic page-mode rules such as:

- `isDraft ? editable : readOnly`
- `not draft => visualization mode`

The system must distinguish between:

- general/header editing
- item editing
- attachment upload and management
- operational workflow actions
- pure read-only visualization

A request may be protected from general edits without being fully read-only.

## Proposed Stage Matrix

| Stage / Status | General/Header Fields | Items | Attachments | Operational Action | Read-Only? | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **RASCUNHO** | Yes | Yes | Optional / as needed | Save draft / Submit | No | Full editing allowed |
| **AGUARDANDO COTAÇÃO** | Prefer locked | Yes | Yes | Concluir cotação e enviar para aprovação | No | Buyer must still work on items and Proforma |
| **REAJUSTE A.A** | Yes | Yes | Yes | Re-submit | No | Returned by Area Approver; full correction needed |
| **REAJUSTE A.F** | Yes | Yes | Yes | Re-submit | No | Returned by Final Approver; full correction needed |
| **AGUARDANDO APROVAÇÃO ÁREA** | No | No | No | None | Yes | Waiting for approver decision |
| **AGUARDANDO APROVAÇÃO FINAL** | No | No | No | None | Yes | Waiting for approver decision |
| **APROVADO** | Locked | Locked | Yes | Registrar emissão / inserção de P.O | No | Not fully read-only; post-approval buyer/finance action still exists |
| **PO_ISSUED** | Locked | Locked | Yes | Agendar Pagamento / Confirmar Pagamento | No | Both COM and PAG follow this financial flow |
| **PAYMENT_SCHEDULED** | Locked | Locked | Yes | Confirmar Pagamento | No | Finance intervention stage |
| **PAYMENT_COMPLETED** | Locked | Locked | Yes | Mover para Recebimento | No | Evidence of payment must be provided |
| **WAITING_RECEIPT** | Locked | Locked | Yes | Finalizar Pedido | No | Operational verification stage |
| **FINALIZADO / COMPLETO** | No | No | Download only | None | Yes | Fully read-only stage |
| **REJEITADO / CANCELADO** | No | No | Download only | None | Yes | Historical visualization only |

> [!IMPORTANT]
> **Unified Post-PO Rule**: Starting from Stage 5, both `QUOTATION` and `PAYMENT` requests follow the same operational lifecycle after `PO_ISSUED`. Guidance suggesting that Quotations skip payment stages is no longer valid.

## Important Interpretation Rules

### 1. Read-only must not be based only on "not draft"

A request is read-only only when no further user intervention is expected.

### 2. Protected request does not mean fully blocked request

Some stages should protect:

- general request fields

while still allowing:

- attachment upload
- workflow-specific action buttons
- post-approval finance/buyer operations

### 3. Approved does not mean fully read-only

For **APROVADO**, the screen must still allow the required post-approval operational flow, especially:

- P.O registration/upload
- related history entries
- later download of uploaded files

### 4. Quotation stage must remain actionable

For **AGUARDANDO COTAÇÃO**, the system should allow:

- item changes
- Proforma upload
- quotation completion action

Even if general/header fields are protected.

### 5. Payment-related stages also need review

Please verify stages such as:

- payment scheduling
- payment completed
- proof of payment upload

If these stages still require user action, they must not fall into pure visualization mode.

## Recommended Implementation Approach

Please replace generic page-mode logic with a more explicit permission model, such as:

- `canEditHeader`
- `canEditItems`
- `canManageAttachments`
- `canExecuteOperationalAction`
- `isReadOnly`

These permissions should be derived from workflow stage/status, not only from "draft vs not draft".

## Expected Deliverable From Implementation

Please:

1. review current statuses against this matrix
2. map each status to explicit screen permissions
3. correct frontend page-mode logic accordingly
4. ensure backend action validation remains consistent
5. keep history/upload/download behavior working in operational stages
6. summarize the final permission matrix implemented in code/docs
