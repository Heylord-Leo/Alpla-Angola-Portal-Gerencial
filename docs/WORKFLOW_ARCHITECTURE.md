# Workflow Architecture

This document serves as the authoritative reference for the Request workflows implemented in the Alpla Angola Portal.

## 1. PAYMENT Workflow

The `PAYMENT` workflow is the primary lifecycle for direct purchasing and payment requests. It follows a multi-stage approval process followed by an operational execution pipeline.

### State Machine / Transitions

| Status Code | Portuguese Name | Responsible | Next Action |
| :--- | :--- | :--- | :--- |
| `DRAFT` | Rascunho | Solicitante | Submeter o pedido |
| `WAITING_AREA_APPROVAL` | Aguardando Aprovação da Área | Aprovador da Área | Aprovar, Rejeitar ou Ajuste |
| `AREA_ADJUSTMENT` | Reajuste Necessário (Área) | Solicitante | Revisar e Reenviar |
| `WAITING_FINAL_APPROVAL` | Aguardando Aprovação Final | Aprovador Final | Aprovar, Rejeitar ou Ajuste |
| `FINAL_ADJUSTMENT` | Reajuste Necessário (A.F) | Solicitante | Revisar e Reenviar |
| `APPROVED` | Aprovado | Comprador | Registrar P.O (Operational) |
| `PO_ISSUED` | P.O Emitida | Financeiro | Pagar, Agendar ou Devolver P.O |
| `WAITING_PO_CORRECTION` | Aguardando Correção P.O | Comprador | Corrigir e re-registrar P.O |
| `PAYMENT_SCHEDULED` | Pagamento Agendado | Financeiro | Finalizar Pagamento (Operational) |
| `PAYMENT_COMPLETED` | Pagamento Realizado | Recebimento | Mover p/ Recibo (Operacional) |
| `WAITING_RECEIPT` | Aguardando Recibo | Recebimento | Operação de Recebimento (Operacional) |
| `IN_FOLLOWUP` | Em Acompanhamento | Recebimento | Finalizar Recebimento (Operacional) |
| `COMPLETED` | Finalizado | - | - |
| `REJECTED` | Rejeitado | - | - |
| `CANCELLED` | Cancelado | - | - |

### Special Rules (PAYMENT)

- **Item Requirement**: `PAYMENT` requests **must** have at least one line item before submission.
- **Rework Restriction**: `PAYMENT` requests do **not** allow "Reajuste" actions during approval stages. They must be explicitly Approved or Rejected.
- **Financeiro Branching**: In the `PO_ISSUED` stage, two exclusive actions are available:
  - **PAGAR**: Transitions directly to `PAYMENT_COMPLETED`.
  - **AGENDAR PAGAMENTO**: Transitions to `PAYMENT_SCHEDULED`.
- **Simplified Operational Path**: The legacy `PAYMENT_REQUEST_SENT` status is bypassed/removed.

---

## 2. QUOTATION Workflow

The `QUOTATION` workflow is a streamlined lifecycle for price research and quotation gathering.

### State Machine / Transitions

| Status Code | Portuguese Name | Responsible | Next Action |
| :--- | :--- | :--- | :--- |
| `DRAFT` | Rascunho | Solicitante | Submeter para Cotação |
| `WAITING_QUOTATION` | Aguardando Cotação | Comprador | Registrar cotações e propostas |
| `WAITING_AREA_APPROVAL` | Aguardando Aprovação da Área | Aprovador da Área | Aprovar, Rejeitar, Reajuste ou **Editar CC** |
| `WAITING_FINAL_APPROVAL` | Aguardando Aprovação Final | Aprovador Final | Aprovar, Rejeitar, Reajuste ou **Selecionar Vencedor** |
| `APPROVED` | Aprovado | Comprador | Registrar P.O (Operational) |
| `PO_ISSUED` | P.O Emitida | Financeiro | Pagar, Agendar ou Devolver P.O |
| `WAITING_PO_CORRECTION` | Aguardando Correção P.O | Comprador | Corrigir e re-registrar P.O |
| `PAYMENT_SCHEDULED` | Pagamento Agendado | Financeiro | Finalizar Pagamento (Operational) |
| `PAYMENT_COMPLETED` | Pagamento Realizado | Recebimento | Mover p/ Recibo (Operacional) |
| `WAITING_RECEIPT` | Aguardando Recibo | Recebimento | Operação de Recebimento (Operacional) |
| `IN_FOLLOWUP` | Em Acompanhamento | Recebimento | Finalizar Recebimento (Operacional) |
| `COMPLETED` | Finalizado | - | - |

### Special Rules (QUOTATION)

- **Item Requirement**: `QUOTATION` requests can be submitted as Draft with zero items, but MUST have at least one line item before the Buyer can "Concluir Cotação".
- **Quotation Completion**: The "**CONCLUIR COTAÇÃO E ENVIAR PARA APROVAÇÃO**" action requires at least one item, a linked supplier, and the **Proforma** attachment. It does NOT require a winner selection.
- **Winner Selection**: The selection of the winning quotation is an exclusive responsibility of the **Aprovador Final** during the `WAITING_FINAL_APPROVAL` stage.
- **Cost Center Management**: The definition/editing of cost centers is an exclusive responsibility of the **Aprovador de Área** during the `WAITING_AREA_APPROVAL` stage.
- **Winning Quotation Authority (Stage 9)**: Once a winner is selected during `WAITING_FINAL_APPROVAL`, its items and total amount become the **authoritative commercial source** for the final approval flow.
- **Supplier Identification**: For `QUOTATION` requests, the `SupplierId` at the request header level is considered a snapshot; the authoritative supplier is always the one linked to the **Selected Quotation**.
- **Commercial Locking vs. Operational Continuation**: After `APPROVED`, the commercial decision (winner, items, total) is **locked** and immutable. The operational workflow (P.O. registration, payment, receipt) follows the same sequence as the `PAYMENT` workflow. Responsibility shifts from **Buyer** (`APPROVED`) to **Finance** (`PO_ISSUED`) and subsequently to **Recebimento** (`PAYMENT_COMPLETED`) for the receiving phase.
- **UI Pivot**: The frontend dynamically replaces the original request items table with the winning quotation items once a selection is made or the request is approved.
- **Authoritative Receiving Source (Stage 10.2)**: For `QUOTATION` requests, the **Winning Quotation Items** are the authoritative source for the operational receiving phase. Original `RequestLineItems` are preserved for historical traceability but are not used for quantity/divergence tracking during receiving.

---

## 3. Transitional Authorization Model

Until a formal RBAC (Role-Based Access Control) and Authentication middleware (e.g., JWT) is implemented, the portal operates on a **Transitional Authorization Model**:
- **Role Simulation**: The frontend passes a `userMode` header (or equivalent context) to simulate the active role (e.g., `BUYER`, `FINAL_APPROVER`).
- **Manual Enforcement**: Controllers manually inspect `GetUserMode()` and request statuses to enforce business rules.
- **Strict Avoidance of Middleware**: To prevent 500 Internal Server Errors (`"No authentication handlers are registered"`), backend endpoints **do not** use standard ASP.NET `[Authorize]` attributes or `Forbid()` / `Unauthorized()` action results.
- **Explicit Status Codes**: When access is denied based on transitional roles, endpoints must return an explicit `StatusCode(403, "...")` or a `Conflict(...)` ProblemDetails response rather than relying on authorization filters.

---

## 4. Operational Stage Permissions (v0.9.16+)

Stages from `APPROVED` to `WAITING_RECEIPT` are classified as **Operational**. They are NOT fully read-only and require specific user actions to progress.

### Permission Matrix

| Status | Header | Items | Attachments | Action Buttons |
| :--- | :--- | :--- | :--- | :--- |
| `APPROVED` | Locked | Locked | **Editable (P.O)** | Registrar P.O |
| `WAITING_PO_CORRECTION` | Locked | Locked | **Editable (P.O)** | Corrigir P.O |
| `PO_ISSUED` | Locked | Locked | **Editable (Schedule)** | Agendar / Pagar / Devolver |
| `PAYMENT_SCHEDULED` | Locked | Locked | **Editable (Proof)** | Confirmar Pagamento / Devolver |
| `PAYMENT_COMPLETED` | Locked | Locked | **Editable (Proof)** | Mover p/ Recibo |
| `WAITING_RECEIPT` | Locked | **Conferência (Items)** | **Editable (Receipt)** | Registrar Recebimento |
| `IN_FOLLOWUP` | Locked | **Conferência (Items)** | **Editable (Receipt)** | Finalizar Pedido |

### Attachment Deletion Rules

Deletion is restricted by document type and status to preserve audit integrity:

- **PROFORMA**: Locked once submitted for approval.
- **PO**: Deletable in `APPROVED` or `WAITING_PO_CORRECTION`.
- **PAYMENT_SCHEDULE**: Deletable in `PO_ISSUED` or `PAYMENT_SCHEDULED`.
- **PAYMENT_PROOF**: Deletable in `PAYMENT_SCHEDULED`, `PAYMENT_COMPLETED`, or `WAITING_RECEIPT`.

---

## 5. Shared UI Behaviors

### Finalized vs. Working Statuses

The system distinguishes between **Working** statuses (where actions are still required) and **Finalized** statuses (where the lifecycle ends).

**Finalized Statuses**:

- `COMPLETED` (Payment)
- `REJECTED`
- `CANCELLED`

**Implications**:

- Finalized requests **do not** receive background urgency highlights (Red/Orange/Yellow).
- Finalized requests are filtered out of "Attention Needed" views.
- Finalized requests are strictly read-only.

### Action Bar Isolation

To prevent role confusion, action bars in `RequestEdit.tsx` are strictly partitioned:

- **Approval Actions**: Only visible in `WAITING_AREA_APPROVAL` and `WAITING_FINAL_APPROVAL`.
- **Rework Actions**: (Reenviar) Only visible in adjustment statuses.
- **AÇÕES OPERACIONAIS**: Only visible in `APPROVED`, `WAITING_PO_CORRECTION`, and subsequent operational stages.
- **Quotation Action Bar**: Strictly isolated to `QUOTATION` requests and stages.

---

## 6. Finance Return / PO Correction Loop

When Finance identifies an issue with a registered PO, they can return the request to the Buyer for correction.

### Transition Flow

```
PO_ISSUED ──[FINANCE_RETURN]──> WAITING_PO_CORRECTION ──[REREGISTER_PO]──> PO_ISSUED
PAYMENT_SCHEDULED ──[FINANCE_RETURN]──> WAITING_PO_CORRECTION ──[REREGISTER_PO]──> PO_ISSUED
```

### Business Rules

- **Source-Status Guard**: Finance can only return from `PO_ISSUED` or `PAYMENT_SCHEDULED`. Returns from `PAYMENT_COMPLETED` or any other status are blocked.
- **Scheduling Invalidation**: When returning from `PAYMENT_SCHEDULED`, the prior payment scheduling is intentionally invalidated. After correction, Finance must re-evaluate and re-schedule from `PO_ISSUED`.
- **Approval Preservation**: The correction loop does NOT reset approvals. The request returns to `PO_ISSUED` (not `APPROVED`) after correction.
- **Distinct Action Codes**: History entries use `REGISTER_PO` for initial registration and `REREGISTER_PO` for correction, ensuring clear auditability.
- **Notification**: `PO_CORRECTION_COMPLETED` event notifies plant-scoped Finance users when the Buyer completes the correction.

### Audit Trail

A correction cycle produces the following history entries:
1. `FINANCE_RETURN_ADJUSTMENT` (PO_ISSUED → WAITING_PO_CORRECTION) with Finance justification
2. `REREGISTER_PO` (WAITING_PO_CORRECTION → PO_ISSUED) with Buyer correction comment

Both old and new PO attachments are preserved in the attachment history for traceability.

### Urgency & Sorting

1. **Priority Rank**: Overdue (3) > Today (2) > Soon (1) > Active/no-deadline (0) > Finalized (-1).
2. **Need Level Priority**: Within each urgency rank, requests are sorted by Need Level descending — Crítico (4) > Urgente (3) > Normal (2) > Baixo (1) > sem nível (0).
3. **Recency**: `createdAtUtc` descending is used as the final tie-breaker.

> **Finalized statuses** (`REJECTED`, `CANCELLED`, `COMPLETED`, `QUOTATION_COMPLETED`) receive score **-1** and always rank below all active items, including active items with no deadline (score 0).
> `APPROVED` is an active operational status and receives a date-based score like any other working status.
