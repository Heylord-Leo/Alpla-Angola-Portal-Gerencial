# Implementation Plan — Mandatory Items, Reconciliation Workaround & Contextual Supplier Creation

> Status: **In implementation** (approved plan). No commit/push/version changes until the official `/task-publish` phase.
> Local unrelated change to preserve: `src/backend/AlplaPortal.Api/Program.cs` (out of scope — do not stage).

This plan addresses three related problems in the Purchasing / Payment / OCR / Quotation-Wizard flows.

---

## Phase 1 — Reconciliation workaround (add omitted requested item from proforma)

### Goal
Allow the Buyer, during quotation reconciliation, to explicitly create a **requested** `RequestLineItem`
from a proforma/OCR line — for old requests with **zero** items *and* for requests that already have
some items but where others were **omitted**. This is semantically **distinct** from `EXTRA_ITEM`.

### Semantic distinction (must remain two actions)
- **Add as requested item** → creates a `RequestLineItem` immediately, `QuotationLifecycleStatus = QUOTATION_PENDING`,
  outside any batch, does **not** depend on `EXTRA_ITEM` approval. Origin recorded as `BUYER_RECONCILIATION`.
- **Mark as additional item** (`EXTRA_ITEM`) → unchanged; remains a proposal subject to approver acceptance.

### Conservative state scope (initial)
Allowed **only** when ALL hold (re-validated in backend):
- `RequestType = QUOTATION`
- `Status = WAITING_QUOTATION`
- not terminal (not APPROVED/PO_ISSUED/PAID/COMPLETED/REJECTED/CANCELLED/…)
- the new item belongs to **no** batch
- actor has Buyer role
Blocked (this phase): AREA/FINAL approval & adjustment, WAITING_COST_CENTER, any batch-bearing/approved/terminal state.
Expansion to adjustment states is a **separate future task**.

### Value rule (conservative)
New line created with `UnitPrice = 0` (never copy the proforma price as "requested value").
Copy only: Description, Quantity, Unit, catalog reference when present. Total recompute via existing
`RecalculateEstimatedTotalAsync` — a 0-priced line does not inflate the total.

### Common domain service (no duplication)
Extract line-item creation into a reusable service (`ILineItemFactory` / `LineItemCreationService`)
shared by `AddLineItem` and the new endpoint. It centralizes: description/quantity/unit validation,
line-number assignment, entity creation, `QuotationLifecycleStatus`, initial values, total recompute,
history, provenance fields, transactional persistence. Controllers keep: authorization, context,
state guards, request/response translation.

### Provenance & audit (structured, not free-text)
New nullable columns on `RequestLineItem`:
- `CreationOrigin` (e.g. `BUYER_RECONCILIATION`; null/`STANDARD` for normal creation)
- `SourceProformaAttachmentId` (Guid?, soft reference to the proforma `RequestAttachment`)
- `CreationIdempotencyKey` (string?, client-supplied UUID; **unique filtered index** where not null)
- (reuse existing `CreatedByUserId`, `CreatedAtUtc`)
Plus a dedicated history event `ITEM_ADDED_FROM_PROFORMA`.

### Idempotency vs cross-session duplicate detection (two DIFFERENT mechanisms)

**(1) Same-operation idempotency** — protects double-click, retry, concurrent same call, network
failure after the DB write, immediate re-send:
- Frontend generates a UUID **once** when it starts creating a given proforma line and reuses it on retries.
- Backend persists it in `CreationIdempotencyKey` with a **unique filtered index**.
- On a repeat with the same key → return the **existing** item (200/idempotent), never create a second.
- Handled inside a transaction; the DB unique index is the final guard against races.
- Limitation (explicit): this UUID lives in wizard memory. It does **not** survive reload / new browser /
  other machine / OCR reprocessing / document re-upload. It is **not** a cross-session solution.

**(2) Cross-session probable-duplicate detection** — protects against the same real proforma line being
added again in a different session, where the session UUID is gone:
- Before creating, backend looks for a likely prior creation using **persisted** signals:
  `RequestId` + `SourceProformaAttachmentId` + normalized Description + Quantity + Unit (+ catalog ref).
- **Not** a rigid unique index on (description, quantity, unit) — two legitimate lines may share values.
- Outcomes:
  - **Unambiguous match** (same proforma attachment + same normalized line already produced a
    `BUYER_RECONCILIATION` item): return existing, do not duplicate, rehydrate wizard.
  - **Probable (not unambiguous)**: return a `duplicateSuspected` response with the candidate(s); the buyer
    chooses *use existing* or *create anyway* (explicit confirmation flag), and the choice is recorded.
  - **No match**: create normally.
- Ideal persistent source (checked during impl): if a persisted proforma-line / QuotationItem id exists at
  action time, prefer it. Currently the wizard reconciles an **in-memory** OCR draft (QuotationItems persist
  only at quotation save), so at action time there is no persisted QuotationItemId → rely on
  `CreationIdempotencyKey` (retries) + `SourceProformaAttachmentId`-based detection (cross-session).

### Endpoint (Phase 1)
`POST /api/v1/requests/{requestId}/line-items/from-proforma`
- Body: description, quantity, unitId, itemCatalogId?, sourceProformaAttachmentId?, idempotencyKey (required),
  confirmCreateDespiteDuplicate? (bool).
- Guards (all 400/403/409 as appropriate): existence, QUOTATION, WAITING_QUOTATION, not terminal, Buyer role,
  not touching any batch.
- Returns created (or existing/idempotent) line item id + a `duplicate` signal when applicable.

### Frontend (Phase 1)
- `WizardStepReconciliation`: add **"Adicionar como item solicitado"** action (distinct from EXTRA_ITEM),
  opening a small OCR-prefilled review form (description, quantity, unit, catalog ref; **no price as requested value**).
- On confirm → call endpoint with a per-line UUID; on success insert into Panel 2, set the OCR line to
  `MAPPED` + `mappedRequestLineItemId = newId`; persist mapping in the normal quotation save.
- On post-create failure → retry with the same UUID returns the existing item (rehydrate; no manual re-create).
- Handle `duplicateSuspected` with a confirm dialog (use existing / create anyway).

### Tests (Phase 1)
Unit + integration: zero-item request; partially-omitted request; distinct from EXTRA_ITEM; item born
QUOTATION_PENDING & batch-free; blocked states; retry/double-click no-dup; two-tab race; OCR reprocessed;
document removed/re-uploaded; created-then-closed-before-save; network failure + reopen; history/provenance persisted.

---

## Phase 2 — Mandatory items on new requests

- **Quotation**: enforce ≥1 valid item at **`CreateRequest`** (it is the definitive send; status → WAITING_QUOTATION).
  Reject `null`/`[]`/empty lines, frontend + backend.
- **Payment**: keep `CreateRequest` allowing an item-less **DRAFT** (progressive OCR/attachments); enforce ≥1 valid
  item at **`Submit`**, frontend + backend.
- **Minimum valid item**:
  - Quotation: Description non-empty, Quantity > 0, Unit required.
  - Payment (at Submit): Description non-empty, Quantity > 0, Unit required, positive unit/total value,
    per-line consistency (qty × unit − discount + IVA = total); keep overall request total validation.
    A zero-value line must not be masked by another positive line.
  - **Due date is NOT a per-item mandatory field** (belongs to invoice/request).
- Historical item-less requests remain accessible; **no destructive migration**.

## Phase 3 — Contextual supplier creation (Payment OCR)

- Primary target **Scenario B** (no `requestId` during creation OCR — confirmed): contextual endpoint
  (e.g. `POST /api/v1/lookups/suppliers/from-payment-ocr`), authorized for users who may create Payment requests.
  Creates supplier as `DRAFT` only; origin `PAYMENT_OCR`; no manage/activate/approve/deactivate/Primavera-edit.
- Scenario A (existing `requestId`): only if a real edit/reprocess flow needs it, reusing the same service.
- **Backend-authoritative matching** (order): exact normalized NIF → exact normalized Name → inactive →
  probable similar → allow create. Same-NIF blocks (returns existing, even inactive, shown clearly, no auto-reactivate);
  same name + different NIF → alert/confirm; empty NIF → name matching; races guarded by unique indexes + conflict handling.
  Align frontend/backend normalization (case, spaces, punctuation, accents, dashes, corporate suffixes).
- Policy contextual (not a plain role add): may-create-Payment + context; existing general-permission users keep working.
- Suppliers remain **global** (no company/plant scope) unless an existing scope rule is found — then stop & document.

## Phase 4 — Tests, build, runtime, regression
## Phase 5 — Docs / Guided Tour / CHANGELOG / VERSION / commit / push (only after approval, via workflow)

---

## Execution constraints (active)
No reset/rebase/amend/force; no branch deletion; additive/safe migrations only; no historical-data mutation;
no general role expansion; no adjustment-state expansion; never copy proforma price to requested value;
session UUID is not a complete cross-session dedup; no CHANGELOG/VERSION before publish; no commit before
tests+review; no push without explicit authorization.

---

## Phase 6 — PO OCR null-safety & error-handling fix (RegisterPoModal / CorrectPoModal)

> Status: **Implemented and manually validated (Musoland PO test document).** Ready for commit.
> Unrelated to Phases 1–5 above (separate investigation: PO-systemic OCR flow, confirmed
> HTTP 200 / client-side crash contradiction on a real Primavera PO — REQ Serviços Ministério
> 2026/0041, MUSOLAND supplier).

### Confirmed bug
`RegisterPoModal.tsx`'s `requestData.supplierName` (sourced from the nullable
`RequestPoGroupDto.supplierNameSnapshot`, passed through unguarded by `RequestEdit.tsx`) could be
`null`/`undefined` at runtime despite its non-nullable prop type. Comparing it via
`calculateSimilarity` → `normalizeForComparison` → `s.normalize('NFD')` threw a `TypeError` while
processing an already-successful (HTTP 200) `direct-ocr` response, which the modal's blanket
`catch` then mislabeled as "Documento ilegível ou em formato não suportado."

### Fix
- Extracted the previously-duplicated `calculateSimilarity`/`normalizeForComparison` plus the OCR
  response/mismatch-processing logic into `src/frontend/src/lib/ocrPoValidation.ts` (pure, shared
  by both modals — eliminates the duplication and the divergence between them).
- `RegisterPoModal.tsx`/`CorrectPoModal.tsx`: null-safe expected supplier/total via
  `resolveExpectedSupplierName`/`resolveExpectedTotalAmount`; split `runOcrValidation` into a
  transport/backend-error catch (real `err.detail`/`err.message`/`correlationId`, logged via
  `logger` with `componentKey: 'OcrDirectExtract'`) and a client-processing-error catch (shows
  "Os dados foram extraídos do documento, mas ocorreu um erro ao processar o resultado.", logged as
  `OCR_CLIENT_PROCESSING_ERROR`) — never conflating the two again.
- Removed the dead `suggestions?.purchaseOrderNumber?.value` read (no such backend DTO field
  exists); PO number is now explicitly sourced from `documentNumber` only.
- `lib/logger.ts`: added `'OcrDirectExtract'` component key and `'OCR_CLIENT_PROCESSING_ERROR'`
  event type. `lib/api.ts`: `directOcrExtract`'s error path now logs (previously silent).
- **Follow-up correction**: the initial fix used a `'---'` display placeholder as the comparison
  value passed into `calculateSimilarity`, which — combined with `String.includes('')` being
  vacuously true — scored a missing expected supplier as a 0.9 near-match instead of a divergence.
  Corrected by separating the comparison value from the display value:
  `resolveExpectedSupplierName` now returns `string | null` (never a placeholder);
  `resolveSupplierDisplay` renders `'Não definido'` for UI only. `calculateSimilarity` now returns
  `number | null` and refuses to score when either side is null/blank/strips to empty content.
  `buildOcrMismatchResult` reports a missing expected supplier as its own explicit "not comparable"
  divergence (never a silent match). `RegisterPoModalProps.requestData`'s
  `supplierName`/`totalAmount` widened to `string | null` / `number | null` to match runtime
  reality (the shared `RequestDetailsDto.supplierName` used by `CorrectPoModal` was already
  correctly nullable; its `estimatedTotalAmount` was deliberately left non-nullable in the shared
  DTO to avoid a disproportionate ripple across its many other consumers — the existing runtime
  guard already covers it).

### Tests
No test runner existed in the frontend project (no vitest/jest, no test script). Per explicit
user decision, added `src/frontend/src/lib/ocrPoValidation.test.mjs` — 36 cases run via Node's
built-in `node --test` (Node 24, zero new dependencies), covering the exact confirmed HTTP 200
fixture (null optional fields, ADVANCE_FULL + null advance percent, PO number from
documentNumber, total 855000, ALPLA-vs-MUSOLAND supplier divergence, transport-error message
resolution with/without correlationId, and the full nullable-supplier comparison matrix: null/
undefined/empty/whitespace on either side, both empty, identical names, slightly different names,
and an explicit regression guard proving placeholder-like strings no longer score as matches).
Full component-render assertions (e.g. "message absent from the DOM") are **not** covered — that
needs React Testing Library, which was explicitly declined for this phase.

### Scope limits observed
No changes to `OpenAiDocumentExtractionProvider`, no dedicated PO extractor/prompt/schema, no
broader OCR pipeline refactor. The ALPLA/MUSOLAND supplier-role inversion remains a separate,
un-actioned extraction-quality issue for a later phase.
