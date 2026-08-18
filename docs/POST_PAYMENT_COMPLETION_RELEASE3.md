# Post-Payment Completion — Release 3 (closing record)

**Version:** v2.224.0 · **Branch:** `Portal-Gerencial-rev1` · **Closed:** 2026-08-06
**Feature flag:** `PostPaymentCompletion.Enabled` — **`false` in every committed configuration.**

A PAYMENT request may now carry **several source documents**, each with its own file,
classification, supplier, plant, dates, items and totals. The Operation Invoice stage is modelled
but **not implemented** — see §6.

---

## 1. Final architecture

### PAYMENT origin stage — implemented

```
Request
  └── 1:N PaymentSourceDocument        (attachment, supplier, plant, type, dates, amounts)
        └── RequestLineItem.PaymentSourceDocumentId
```

Payment grouping key:

```
Supplier + Currency + PaymentCondition + Plant + SourceDocumentType
```

Plant and source-document type are part of the key because obligations differ by both: two invoices
from one supplier for Viana 1 and Viana 2 are separate obligations, and a proforma and an invoice
carry different post-payment duties.

### Operation Invoice stage — modelled, NOT implemented

```
Request
  └── 1:N OperationInvoice
        ├── 1:N OperationInvoiceAllocation
        └── 1:N OperationInvoiceLine
```

Entities, the pure `OperationInvoiceAggregateDeriver` and the tables exist from Phase 1. **There is
no controller, no service and no UI.** `SELECT COUNT(*) FROM OperationInvoices` = 0. Phase 4 has not
started.

---

## 2. Compatibility fields

`Request.SupplierId`, `Request.PlantId` and `Request.SourceDocumentType` remain **compatibility
echoes** on a multi-document request:

| Field | Authority | Compatibility use |
|---|---|---|
| `Request.SupplierId` | `PaymentSourceDocument.SupplierId` | echo of the first document; never validated |
| `Request.SourceDocumentType` | `PaymentSourceDocument.SourceDocumentType` | echo; **never blocks submission** |
| `Request.PlantId` | routing and authorization **only** | genuinely different from document plants, which drive grouping — the difference is disclosed, never enforced |

Submission validates the **documents**. `ValidatePaymentSourceDocumentsForSubmissionAsync` branches
on document count: with documents it validates each one; without them it keeps the legacy
header rules untouched.

---

## 3. Manual validation record

All scenarios validated locally against `Portal-Gerencial-Dev-ProdClone` with the flag enabled in
the gitignored `appsettings.Development.json`.

| # | Scenario | Result |
|---|---|---|
| 1 | One PAYMENT source document | PASS |
| 2 | Multiple source documents, independent state | PASS |
| 3 | OCR import | PASS |
| 4 | Manual entry | PASS |
| 5 | Per-document OCR — reading Documento 2 never disturbs Documento 1 | PASS |
| 6 | Blocking OCR loading view; no empty-editor flash | PASS |
| 7 | OCR failure → error view with retry / manual / another file / remove | PASS |
| 8 | Supplier creation from an unmatched OCR supplier (one modal, one Save) | PASS |
| 9 | Supplier duplicate handling (NIF conflict, name suspicion, internal NIF) | PASS |
| 10 | `OTHER` / `UNCLASSIFIED` → concrete type: no conflict modal, audited | PASS |
| 11 | Real classification conflict: modal, acknowledgement, justification | PASS |
| 12 | Due date blocks confirmation, not persistence | PASS |
| 13 | Save draft and reopen — documents, items, OCR evidence preserved | PASS |
| 14 | RequestEdit read-only review; documents as compact cards | PASS |
| 15 | `Editar documentos do pedido` opens the composer | PASS |
| 16 | Drawer horizontal resize, persisted and clamped | PASS |
| 17 | Source-documents fetch loop — one GET per open | PASS |
| 18 | Flashing document message — gone | PASS |
| 19 | Request Type read-only (UI) and refused (API) | PASS |
| 20 | Exact-file duplicate, same request — blocked **before OCR** | PASS |
| 21 | Business-document duplicate (supplier + number + series) | PASS |
| 22 | Cross-request duplicate warning with acknowledgement | PASS |
| 23 | Replacement preserves the document when the candidate is refused | PASS |
| 24 | Submission with **no** legacy Proforma slot | PASS |
| 25 | Legacy PAYMENT (zero source documents) unchanged | PASS |
| 26 | Quotation Management unchanged | PASS |
| 27 | Buyer P.O. workflow unchanged | PASS |
| 28 | 1600×900 and dark mode | PASS |
| 29 | Catalogue reconciliation across source documents (v2.224.1) | **pending TEST validation** |

### Post-release TEST finding — corrected in v2.224.1

TEST validation of v2.224.0 found that **catalogue reconciliation was missing from the
multi-document PAYMENT flow**. The stage existed before Release 3 and was not deliberately removed:
`RequestCreate` chose which items to reconcile with

```ts
Number(formData.requestTypeId) === 2 && paymentDraft ? paymentDraft.items : requesterItems
```

and under the multi-document model `paymentDraft` is null, because the legacy single-document editor
no longer renders. The expression fell through to `requesterItems`, which a PAYMENT request leaves
empty, so the guardrail ran correctly against nothing.

Restored in **v2.224.1**, reusing the existing `CatalogItemReconciliationModal`,
`ReconciliationWarningDialog`, `useCatalogItemReconciliation`, `batch-match` and
`reconciliation-create`. Scenario 29 below covers it; the flat item list is assembled across all
confirmed documents and every answer is written back by `tempId`, so a line's
`PaymentSourceDocumentId`, quantity, price, discount, IVA and totals are untouched.

### Known remaining limitation — non-blocking

A **persistence-time race-condition duplicate 409** is surfaced as the specific per-document card
error rather than the duplicate modal. The message names the conflicting document and the data is
correct; only the presentation differs from the preflight path. It requires two clients racing on
the same file within one request.

---

## 4. Migrations

| Migration | Nature | State |
|---|---|---|
| `20260803130410_DocumentClassificationOverrideAudit` | additive | applied |
| `20260804103631_OperationInvoiceWorkflowAndPaymentSourceDocuments` | additive + **SQL precondition guard** | applied |
| `20260804145017_AddUsesMultiSourceDocuments` | additive, `defaultValue: false` | applied |

**Precondition guard.** The Phase 1 migration THROWS if `FinalInvoiceReconciliations` holds rows or
any group carries an operation-invoice attachment, rather than silently reinterpreting data whose
meaning changed. The EF scaffolder had mis-paired three renames purely by type
(`FinalInvoiceValidatedByUserId` → `ExpectedTotalSetByUserId` and two others); these were corrected
by hand to Drop+Add in both `Up()` and `Down()`.

**No synthetic backfill.** Verified on the dev clone: **154 requests — 149 legacy
(`UsesMultiSourceDocuments = 0`), 5 multi-document** (all created during manual testing).
`OperationInvoices` = 0. No historical request was reinterpreted as multi-document; the discriminator
is persisted at creation and never inferred from a date or a row count.

**`Down()` limitations.** Reverting drops `PaymentSourceDocuments`, `OperationInvoice*` and
`UsesMultiSourceDocuments` with their data. The renamed columns are restored structurally, not
semantically — a value written as `ExpectedTotalSetByUserId` reappears under the old name with its
new meaning. A revert is therefore only safe while those tables are empty, which the precondition
guard enforces on the way in.

---

## 5. Feature flag

```jsonc
// src/backend/AlplaPortal.Api/appsettings.json
"PostPaymentCompletion": {
    "Enabled": false,                        // committed default — unchanged
    "EffectiveDateUtc": "9999-12-31T23:59:59Z"
}
```

Surfaces as `paymentMultiDocumentEnabled`, `postPaymentCompletionEnabled` and
`sourceDocumentTypeRequired` on `FeatureFlagsDto`. With the flag off, every screen renders exactly as
it did before Release 3. Local enablement lives only in the gitignored
`appsettings.Development.json`.

---

## 6. Not implemented in Release 3

- Operation Invoice upload, allocation, validation and short close (Phase 4/5).
- Finance working area for operation invoices.
- Notifications for the post-payment stage.
- Frontend automated tests — no test framework exists in this repository; all UI validation above is
  manual.

---

## 7. Commit chain (local, nothing pushed)

Phases 1 → 3 plus correctives: `283e43b`, `acca3be`, `cc28a51`, `0800a01`, `cb01743`, `572419a`,
`8758c11`, `f801de2`, `b7fd480`, `fede935`, `b2a35a9`, `acb20ce`, `5709173`, `3af543f`, `f55100b`,
`1dd9a49`, `10c3928`, `461a120`, `ecf4800`, and this closing commit.
