# Controlled Repair — Legacy Monetary-Scale Defect (REQ-11/08/2026-228)

> Status: implemented in v2.241.0. **The data correction has NOT been executed** on TEST or PROD — it
> is a separate authorized operation. This document describes the capability and its runbook.

## Purpose

Correct the confirmed **legacy x1000 monetary-scale defect** for a single request whose line total was
inflated by a persisted **negative** `RequestLineItem.DiscountAmount`. A pre-fix monetary input produced
a x1000-scaled declared total that was reconciled against a correct line subtotal by storing a negative
discount; the backend line formula `TotalAmount = (Quantity × UnitPrice) − DiscountAmount` then inflated
the total ×1000, and every downstream snapshot (request estimated/approved, PO-group total, scheduled
payment) inherited it. The causal input path was fixed in v2.229.8 (`f91d1a0`); this repair corrects the
**legacy data** the fix did not self-heal.

Reference incident: **REQ-11/08/2026-228** — `4000 × 4,600.00 = 18,400,000.00`, stored as
`18,400,000,000.00` (discount `−18,381,600,000.00`).

## What this is NOT

This is **not** a generic financial editor. The caller cannot name arbitrary fields or set any amount
directly. It repairs exactly one defect shape and refuses everything else. It never touches the P.O.
identity or its attachments, never changes any status, never alters the OCR/extraction history, and never
executes automatically.

## Endpoint

`POST /api/v1/admin/repairs/legacy-monetary-scale/{requestId}` — **System Administrator only**.

- `?confirm=false` (default) — **preview** (dry run). Writes nothing.
- `?confirm=true` — **apply**. Requires a body with a non-empty `reason`.

Body (strong intent only):

```json
{ "expectedCurrentTotal": 18400000000, "expectedCorrectTotal": 18400000, "reason": "…" }
```

The service derives the corrected line/request/PO/payment values canonically (line via the
`LineItemFactory` formula with discount 0 and no IVA; request estimated via the canonical aggregate; PO
group and payment from the corrected authoritative amount).

## Refusal conditions (apply refuses, no write, unless ALL hold)

- exactly one active line; exactly one PO group; exactly one payment;
- **defect fingerprint**: the line discount is negative and `(Quantity × UnitPrice) − DiscountAmount`
  equals the current (wrong) `TotalAmount`, and that total is actually wrong;
- no line IVA, no line percent discount, no request-level (global) discount;
- `Quantity × UnitPrice == expectedCorrectTotal`;
- current line/request-estimated/request-approved/PO-group/payment totals all `== expectedCurrentTotal`;
- request status `PAYMENT_SCHEDULED`; payment status `SCHEDULED` and **unpaid** (`ActualPaidAmount` null/0);
- currency `AOA` on request, PO group, payment and approved snapshot;
- non-empty `reason`.

If the request is already correct, apply returns an **idempotent no-op** (`ALREADY_CORRECT`), not an error.

## Preview / apply semantics

- **Preview** returns every current-vs-corrected amount, the list of safety checks (each pass/fail),
  `AlreadyCorrect`, `WillWrite`, `AffectedRows` and opaque concurrency fingerprints. It performs **zero
  writes**.
- **Apply** corrects the four persisted amounts, appends corrective audit, and commits everything in a
  **single `SaveChanges`** (all-or-nothing — a failure at any point, including the audit insert, rolls the
  whole set back). Status change: none.

## Concurrency

Request and PoGroup carry a SQL Server rowversion (optimistic concurrency). Line and Payment have none, so
they are guarded by re-verifying their exact pre-repair values. A concurrent change to a guarded row after
the operator previewed yields a **CONFLICT** with no partial write; re-preview and retry.

## Idempotency

A second preview after a successful apply reports `AlreadyCorrect = true`, `WillWrite = false`. A second
apply is a no-op (`ALREADY_CORRECT`) and writes **no** further audit rows.

## Audit

Existing history rows (e.g. `REGISTER_PO`, `PAYMENT_SCHEDULED`, OCR-mismatch comments) are **immutable and
never rewritten** — they will keep showing the old figure. The apply **appends**:

- one `RequestStatusHistory` with `ActionTaken = FINANCIAL_CORRECTION` and
  `PreviousStatusId == NewStatusId` (no transition), carrying the reason and a `RepairId`;
- one `RequestFieldChangeHistory` per changed field (line total, line discount, request estimated,
  request approved, PO-group total, payment planned amount).

## PROD execution runbook (do not deviate)

1. **PROD read-only scan** for affected records (see the incident report predicate) to confirm scope.
2. Confirm the target request is still `PAYMENT_SCHEDULED` and **unpaid**.
3. **PROD DB backup** (the standard migration/backup gate); record the `.bak` path.
4. Deploy the repair capability (v2.241.0) — no schema migration is required.
5. **Preview** the request; a human reviews every current-vs-corrected amount and that all safety checks pass.
6. **Human approval** of the preview.
7. **Apply** (`confirm=true`) with a reason.
8. Verify DB + application values are corrected; second preview is an idempotent no-op.
9. Human UI verification: payment screen, request list/detail and PO-group amount all show the corrected value.
10. Confirm no workflow status changed; close the incident.

No step is skippable. The data correction is authorized separately from the code deployment.
