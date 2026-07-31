# Post-Payment Completion Workflow — Plan v7 (Corrected Document Taxonomy)

> Supersedes plan v6 for everything touching document classification.
> v6 remains valid for the two-phase completion architecture, stable idempotency keys and the
> feature-flag strategy — those are unchanged and already delivered in Release 1.
>
> **Why v7 exists.** v6 assumed a binary `PROFORMA | FINAL_INVOICE` model. Manual testing of
> Release 2 showed that model cannot represent the documents the Portal actually receives under
> Angola's *Regime Jurídico das Facturas* (Presidential Decree 71/25), and that the Portal accepted
> an `FT` invoice classified as "Fatura Proforma" without any warning.

---

## 1. What was wrong

| # | Defect | Consequence |
|---|---|---|
| D1 | Two values cannot express Orçamento, Factura de Adiantamento or Factura-Recibo | Real documents were forced into a wrong category |
| D2 | One field meant both "what document is this" and "what is still owed" (`ToFinalInvoiceStatus`) | Identity and obligation could not diverge, so neither could be corrected independently |
| D3 | OCR never classified the document; the invoice prompt has no document-type field at all | Nothing existed to reconcile the user's choice against |
| D4 | No conflict detection, warning, justification or audit | An `FT` accepted as Proforma, silently |
| D5 | "Fiscal Receipt always required" | Legally wrong for a Factura-Recibo, which already documents payment |
| D6 | `ADVANCE_INVOICE` had no representation | Collapsing it into either value produces a wrong obligation |
| D7 | Advance regularization already existed in Buy-to-Pay but was keyed off payment condition, not document identity | Risk of building a second, parallel mechanism |

---

## 2. Approved taxonomy

Document **identity** — what the supplier actually issued.

| Code | Portuguese | Fiscal | Notes |
|---|---|---|---|
| `ESTIMATE` | Orçamento / Cotação | ❌ | Named `ESTIMATE`, not `QUOTATION`: that string is already the request type, the `Quotation` entity and `QuotationLifecycleStatuses` |
| `PROFORMA` | Factura Pró-forma | ❌ | |
| `ADVANCE_INVOICE` | Factura de Adiantamento | ✅ | Fiscal advance document |
| `INVOICE` | Factura | ✅ | Replaces `FINAL_INVOICE` |
| `INVOICE_RECEIPT` | Factura-Recibo | ✅ | Documents the operation **and** its full payment |
| `OTHER` | Outro documento | ❓ | Always requires Finance review |
| `UNCLASSIFIED` | Não classificado | ❓ | Default; blocks progression |

**Renames** (free: zero classified rows existed anywhere when this was applied):

```
Request.BillingDocumentType        → Request.SourceDocumentType
RequestPoGroup.BillingDocumentType → RequestPoGroup.SourceDocumentType
RequestPoGroup.FinalInvoiceStatus  → RequestPoGroup.OperationInvoiceStatus
FINAL_INVOICE (value)              → INVOICE
```

A temporary **read-time alias** maps `FINAL_INVOICE → INVOICE` so a stray legacy value is
interpreted rather than rejected. New `FINAL_INVOICE` values are never persisted.

---

## 3. Identity is not obligation

Two separate concerns, never one field again.

**Identity** (`Request`, `Quotation`, `RequestPoGroup`):

```
SourceDocumentType                 the taxonomy value above
SourceDocumentTypeSource           USER_SELECTED | OCR_CONFIRMED | FINANCE_REVIEW
SourceDocumentTypeOcrSuggestion    what OCR proposed (nullable)
SourceDocumentTypeOcrConfidence    0.0–1.0 (nullable)
SourceDocumentTypeEvidenceJson     title, supporting/conflicting evidence, markers
ClassificationConflictAcknowledged the user was warned and proceeded
ClassificationJustification        mandatory on a high-risk conflict (≥ 20 chars)
```

**Obligations** — never stored by hand, always produced by one pure function:

```csharp
DocumentObligationResolver.Resolve(sourceDocumentType, context)
```

`context` ∈ `PaymentRequest` | `QuotationManagement` | `PostPaymentEvidence`, returning:

```
CanInitiatePayment                RequiresAdvanceRegularization
CanBeUsedInQuotation              RequiresFinanceClassificationReview
RequiresOperationInvoice          RequiresOperationalReceipt
RequiresSeparateFiscalReceipt     BlocksProgression + BlockingReason
```

---

## 4. Obligation matrix

🟡 = still requires formal ALPLA Finance confirmation.

| | ESTIMATE | PROFORMA | ADVANCE_INVOICE | INVOICE | INVOICE_RECEIPT | OTHER | UNCLASSIFIED |
|---|---|---|---|---|---|---|---|
| Fiscal | ❌ | ❌ | ✅ | ✅ | ✅ | ❓ | ❓ |
| **Can initiate a Payment request** | **❌** | ✅ | ✅ | ✅ | **❌** | ⚠️ Finance review | ❌ |
| **Valid in Quotation Management** | ✅ | ✅ | ⚠️ unusual → review | ✅ | **❌** normally | ⚠️ review | ❌ |
| Later operation invoice required | ✅ | ✅ | ✅ | ❌ | ❌ | ⚠️ Finance | ⚠️ unknown |
| Separate fiscal receipt required | ✅ | ✅ | ⚠️ unless other valid proof 🟡 | ✅ | **❌ already documented** | ⚠️ Finance | ⚠️ unknown |
| Advance regularization | ❌ | ❌ | ✅ | ❌ | ❌ | ⚠️ Finance | ❌ |
| Finance confirms classification | ✅ (if used at all) | ❌ | ✅ | ❌ | ✅ | ✅ | ✅ to exit |
| Operational receipt | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Blocks completion | non-fiscal — cannot close on this alone | operation invoice + receipt | operation invoice + credit note + payment proof + Finance validation | fiscal receipt | operational receipt only | Finance review | classification |

### 4.1 `INVOICE_RECEIPT` — corrected

A Factura-Recibo states the operation **and its full payment already happened**. Letting it start a
payable request would ask the Portal to pay the same thing twice.

- **New Payment request:** not accepted as the originating payable document.
- **Quotation Management:** not a normal option; only via an explicit Finance-reviewed exception.
- **Post-payment evidence:** fully valid — it satisfies both the operation-invoice obligation and
  the separate-payment-receipt obligation.
- **Reimbursement / out-of-process payment:** needs a separate approved workflow or mandatory
  Finance review; it is not a normal Payment origin.

### 4.2 `ESTIMATE` — corrected

Non-fiscal. Valid in Quotation Management, and **cannot normally initiate a Payment request** —
it cannot independently authorize payment. A future exceptional advance case would require
mandatory justification, Finance review, explicit approval, and a later fiscal-document obligation.
It is never silently allowed as a Payment source.

### 4.3 `ADVANCE_INVOICE` — provisional ALPLA rule

Fiscal; may initiate an advance-payment request; never treated as `PROFORMA`; never treated as the
operation invoice. Requires a later operation invoice, advance regularization, Credit Note evidence
where applicable, payment evidence, and explicit Finance validation. Completion is blocked while any
regularization remains open.

**Reuses existing Buy-to-Pay machinery rather than duplicating it** — `RequestReconciliation`
(1:N, sequenced, `CreditNoteRequired`/`CreditNoteAttachmentId`, decisions, actors) and
`RequestPayment.PaymentTypes` (`ADVANCE`, `FINAL_BALANCE`, `REGULARIZATION`, `REFUND`). Multiple
sequenced advances are therefore supported natively.

**Provisional pending formal ALPLA Finance confirmation.**

---

## 5. OCR classification

Added to the invoice extraction contract, mirroring the proven `paymentCondition` shape:

```jsonc
"documentClassification": {
  "type": "ESTIMATE|PROFORMA|ADVANCE_INVOICE|INVOICE|INVOICE_RECEIPT|OTHER|null",
  "confidence": 0.0,
  "titleFound": "verbatim document title or null",
  "supportingEvidence": ["…"],
  "conflictingEvidence": ["…"],
  "fiscalMarkers": ["…"],
  "nonFiscalMarkers": ["…"]
}
```

**Evidence priority:** explicit title → non-fiscal declarations ("sem valor fiscal") → fiscal
certification markers → payment-settlement wording ("Recebemos", "Liquidado") → prefixes.
**A prefix alone never determines the classification** and caps confidence at 0.50.

**Conflict handling** — OCR never auto-selects the field:

| Case | Behaviour |
|---|---|
| Selection == suggestion | `OCR_CONFIRMED`, silent |
| Conflict, confidence < 0.70 | Warning + explicit acknowledgement |
| Conflict, confidence ≥ 0.70 | Blocking acknowledgement + justification ≥ 20 chars |
| Fiscal suggestion vs non-fiscal selection | **Always high-risk**, regardless of confidence — this is the FT→Proforma case |
| `ADVANCE_INVOICE`, `OTHER`, or `INVOICE_RECEIPT` outside its normal context | Finance review required |

Every case persists selection, suggestion, confidence, evidence, conflicting evidence, actor and
justification.

---

## 6. UI

Label everywhere: **"Tipo de documento anexado"** — it describes the artefact, not a future workflow.

**Payment request** — only Payment-origin options: Factura Pró-forma · Factura de Adiantamento ·
Factura · Outro documento (review). `ESTIMATE` and `INVOICE_RECEIPT` are not offered.

**Quotation Management** — Orçamento/Cotação · Factura Pró-forma · Factura ·
Factura de Adiantamento (marked unusual, review) · Outro (review). Factura-Recibo is not offered.

Both show a fiscal/non-fiscal badge, the OCR suggestion and evidence, conflict warnings, and a
derived-obligation preview.

---

## 7. Migration and compatibility

No historical fact invention · no bulk inference from prefixes · no destructive rewrite · no PROD or
TEST data change. Renames were applied while **zero rows were classified**, so no data moved.
Historical completed requests stay untouched; `UNCLASSIFIED` continues to block.

---

## 8. Revised release plan

| Release | Scope | Status |
|---|---|---|
| R1 | Domain foundation, two-phase completion skeleton | ✅ committed (`2586a97`, fix `f026231`) |
| R2 (original) | Binary classification | ✅ committed (`f3e3475`) — superseded, not reverted |
| **R2 corrected** | Taxonomy + identity/obligation split + resolver + context-aware UI **and** OCR classification, conflict handling, audit | **this work** — two internal commits, one consolidated manual test |
| R3 | **Operation Invoice** (was "Final Invoice") — conditional on `RequiresOperationInvoice`; reconciliation baseline accounts for advances | pending |
| R4 | Operational receipt · **conditional** fiscal receipt (`RequiresSeparateFiscalReceipt`) · two-phase completion activation · `ParentCompletionSweep` | pending |
| R4B | Advance-invoice regularization bound to `RequestReconciliation` | pending |
| R5 | Historical classification (7 options) + Finance review queue + PROD activation | pending |

**`WAITING_FISCAL_RECEIPT` priority:** must not be 80 — `WAITING_RECONCILIATION` already holds it.
R4 selects a free value from the live map and documents the rationale.

---

## 9. Open questions for ALPLA Finance

1. `ADVANCE_INVOICE`: is a Credit Note always required, or only when the advance is not absorbed by the final invoice?
2. Factura-Recibo: confirm no separate payment receipt is ever required.
3. `ESTIMATE`: hard-block from Payment, or allow with Finance approval?
4. Pró-forma-initiated requests: must Finance confirm the later Factura before completion?
5. `FA` prefix: used for both "Factura" and "Factura de Adiantamento"? If so it must carry near-zero weight.
6. Multiple advance invoices: real today, or future-only?
7. Is 0.70 the right bar for demanding a written justification?
8. Credit Note timing: before completion, or a recorded commitment to issue it?
9. `OTHER`: which documents realistically arrive here (Nota de Débito, Recibo, Autofacturação)?
10. Standalone Recibo: confirm it is only ever supporting evidence, never an originating document.
