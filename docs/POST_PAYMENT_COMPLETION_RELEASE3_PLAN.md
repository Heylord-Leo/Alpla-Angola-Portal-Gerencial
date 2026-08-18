# Release 3 — Operation Invoice Workflow (Implementation Plan, rev. 4)

> Supersedes the "Final Invoice" design in Plan v6 §9–§11.
> Taxonomy and obligations from [POST_PAYMENT_COMPLETION_PLAN_V7.md](./POST_PAYMENT_COMPLETION_PLAN_V7.md).
> Release 2 closing state: [POST_PAYMENT_COMPLETION_RELEASE2.md](./POST_PAYMENT_COMPLETION_RELEASE2.md).
>
> **rev. 2** — a PO group may be covered by **many** operation invoices; satisfaction is cumulative.
> **rev. 3** — an operation invoice may cover **many PO groups**, through allocations.
> **rev. 4** — a **PAYMENT request may carry many source documents**. Quotation Management is
> untouched.
>
> **Status: awaiting authorization. Nothing in this document is implemented.**

---

## 1. Shape of the problem

Release 3 now spans two stages of a request's life, and each has its own many-relationship. They are
**different entities and must never be merged** (§4).

```
STAGE 1 — origin (PAYMENT only)          STAGE 2 — post-payment obligation

Request 1 ── N PaymentSourceDocument      Request 1 ── N OperationInvoice
                    │                                        │ 1
                    │ items                                  N
                    ▼                          OperationInvoiceAllocation
            RequestLineItem                                  N
                    │ RequestPoGroupId                       │ 1
                    ▼                                        │
            RequestPoGroup ◄─────────────────────────────────┘
```

- **Many source documents per PAYMENT request** — one request paying two invoices from the same
  supplier for two plants. Each document keeps its own identity, OCR, classification and items.
- **Many operation invoices per group** — partial deliveries, staged services, monthly invoicing.
  Satisfaction is *cumulative coverage*, never document presence.
- **Many groups per operation invoice** — a supplier consolidates several groups onto one Factura.
  The document is stored **once**; only its allocation is per group.

`RequestPoGroup` is the hinge: source documents flow *into* it, operation invoices are allocated
*against* it, and it is the unit at which every obligation is measured.

### 1.1 Terminology debt from Release 1 — clearing it is free today

Release 1 shipped `FINAL_INVOICE` naming; Release 2 renamed only the status enum. Release 3 finishes
it, because Release 3 is the first code that writes any of it.

Verified against the production clone `Portal-Gerencial-Dev-ProdClone`:

| Table / column | Rows |
|---|---|
| `FinalInvoiceReconciliations` | **0** |
| `RequestPoGroups.FinalInvoiceAttachmentId IS NOT NULL` | **0** |
| `RequestAttachments` typed `FINAL_INVOICE` / `FISCAL_RECEIPT` | **0** / **0** |
| `RequestPoGroups` with `OperationInvoiceStatus <> 'UNCLASSIFIED'` | **0** (of 84) |
| `RequestReconciliations` | **0** |

**Every rename and column drop below is pure metadata** — no data migration, no compatibility window.
That stops being true the moment Release 3 ships.

| Current | Release 3 |
|---|---|
| `RequestAttachment.TYPE_FINAL_INVOICE` | `TYPE_OPERATION_INVOICE = "OPERATION_INVOICE"` |
| `RequestPoGroup.FinalInvoice{AttachmentId, UploadedAtUtc, UploadedByUserId, ValidatedAtUtc, ValidatedByUserId, RejectionReason}` | **dropped** — superseded by §3 |
| `FinalInvoiceReconciliation` (entity + table) | `OperationInvoiceReconciliation`, now **per allocation** |
| `PostPaymentIdempotencyKeys.FinalInvoice*`, prefixes `FI_*` | `OperationInvoice*`, prefixes `OI_*` |
| `WorkflowEventCodes.FinalInvoice*` (7 codes) | `OperationInvoice*` |

**Deliberately not renamed:** `RequestReconciliation.FinalInvoiceAmount` — pre-existing Buy-to-Pay
vocabulary for a different concept. Renaming another module as a side effect is scope creep.

**Check before implementing:** whether any email template, notification body or seeded lookup
references the seven `FINAL_INVOICE_*` codes as literal strings.

---

# PART A — Multiple source documents (PAYMENT only)

## 2. Scope boundary — what does *not* change

**This applies only to the Payment request creation and edit flow.** Quotation Management keeps one
document per quotation, exactly as today. Explicitly confirmed:

- **no multi-document UI in Quotation Management**;
- **no `PaymentSourceDocument` use anywhere in the quotation path**;
- **no "Adicionar outra factura" button in the Quotation Wizard**;
- **no plant-based grouping added to quotation documents**;
- **`Quotation.SourceDocumentType` remains the identity of that quotation's single document**;
- **winning-quotation propagation continues unchanged** — `GroupBuilderService` keeps grouping by
  Supplier + Currency + PaymentCondition and is not touched by Part A.

This separation is structural, not conventional: the quotation path builds groups from
`QuotationItem.Quotation`, and the payment path builds them from source documents (§3.6). They meet
only at `RequestPoGroup`, which both already produce.

## 3. `PaymentSourceDocument`

### 3.1 Entity

```
PaymentSourceDocument
  Id                                  Guid, PK
  RequestId                           Guid, FK → Requests (cascade)
  AttachmentId                        Guid, FK → RequestAttachments, UNIQUE
  SupplierId                          int?,  FK → Suppliers
  SupplierNameSnapshot                string?
  SupplierTaxIdSnapshot               string?
  PlantId                             int?,  FK → Plants
  SourceDocumentType                  string?     ← the identity; Release 2 taxonomy
  DocumentNumber                      string?
  DocumentSeries                      string?
  DocumentDate                        DateTime?
  DueDate                             DateTime?
  Currency                            string?
  NetAmount                           decimal?
  TaxAmount                           decimal?
  GrossAmount                         decimal?

  OcrSuggestion                       string?
  OcrConfidence                       decimal?
  OcrEvidenceJson                     string?
  OcrConflictingEvidenceJson          string?
  OcrTitleFound                       string?
  ClassificationSource                string?     (USER_SELECTED | OCR_CONFIRMED | FINANCE_REVIEW)
  ClassificationSuggestionSource      string?     (OCR | FALLBACK)
  ClassificationConflictAcknowledged  bool
  ClassificationJustification         string?
  ClassificationReviewedByFinance     bool
  ClassificationReviewedByUserId      Guid?
  ClassificationReviewedAtUtc         DateTime?

  SequenceNumber                      int         (1..N within the request — "Documento 2")
  IsVoided                            bool        (removed after submission; never hard-deleted)
  VoidedAtUtc / VoidedByUserId / VoidReason

  CreatedAtUtc / CreatedByUserId / UpdatedAtUtc / UpdatedByUserId
  RowVersion                          byte[] [Timestamp]
```

Additions beyond the mandated list, each with a reason:

- **`SupplierNameSnapshot`, `SupplierTaxIdSnapshot`, `OcrTitleFound`, `ClassificationSuggestionSource`**
  — the same evidence Release 2 already persists per request; multi-document must not lose it.
- **`DocumentSeries`** — needed by duplicate detection (§10) exactly as for operation invoices.
- **`SequenceNumber`** — stable "Documento 1 / 2 / 3" labelling that survives a removal.
- **`IsVoided` instead of deletion** — a document removed *before* submission may be hard-deleted
  (nothing downstream exists yet); one removed *after* submission must be voided, because a
  classification decision and its justification were already audited. §3.4 states which applies.
- **`ClassificationReviewedByFinance`** — `OTHER` and `ADVANCE_INVOICE` require Finance review per
  document, not per request.
- **`AttachmentId` UNIQUE** — one attachment is one source document.

### 3.2 `Request.SourceDocumentType` and friends become compatibility fields

Release 2 put the classification on `Request`. With several documents it can no longer be
authoritative — an obligation cannot be derived from a request that carries a PROFORMA *and* an
INVOICE.

**Rule:** for a PAYMENT request, obligations derive **only** from `PaymentSourceDocument` rows.
`Request.SourceDocumentType` and its evidence columns are retained, populated **only when the request
has exactly one active source document**, and read by nothing that decides an obligation. They exist
so existing list screens, filters and the Release 2 detail view keep working during the transition.

The same treatment applies to two request-level fields that multi-document makes ambiguous:

| Field | Consumers found | Treatment |
|---|---|---|
| `Request.SupplierId` | ~20 backend sites | Compatibility only. Populated when all active documents share one supplier; otherwise `null`, and the UI shows "vários fornecedores". |
| `Request.PlantId` | ~57 backend sites | Compatibility only, same rule. **The larger surface — this is the main regression risk in Part A** and needs a dedicated review pass in step 2 of §22. |

Nothing that computes an obligation, a group or a total may read these three fields for a PAYMENT
request. That is a rule with tests (§21, cases 11–12).

### 3.3 Line-item association — recommendation

**Recommended: `RequestLineItem.PaymentSourceDocumentId` (nullable, FK, `NoAction`).** The preferred
option in the mandate is also the right one, and the current model already supports it without
distortion:

`RequestLineItem` **already carries `PlantId`, `SupplierId`, `CurrencyId` and `RequestPoGroupId`
per line** — verified. The item model is already per-line, not per-request, so attaching a source
document to a line adds one FK and changes no existing shape. No alternative association is needed.

Rules:
- One item belongs to **exactly one** source document. No many-to-many.
- Nullable, because QUOTATION items have no source document and legacy PAYMENT items have none.
- On PAYMENT, required at submission for every item (§3.5).
- Voiding a document voids its items (`IsDeleted`), never orphans them.

### 3.4 Totals

```
PaymentSourceDocument.ItemsTotal  = Σ TotalAmount of its active items
Request.EstimatedTotalAmount      = Σ GrossAmount of active (non-voided) source documents
```

Validated at save and at submission:

1. every active document's `Σ items ≈ GrossAmount` within tolerance (§9.3) — otherwise the
   document's own value is not attributable to what is being bought;
2. the request header total equals the sum of active documents within tolerance;
3. **no document counted twice** — enforced by the UNIQUE `AttachmentId` plus the duplicate rules in
   §10;
4. voided documents and their items contribute nothing.

**Currency:** all active source documents must share one currency, unless a multi-currency rule is
approved separately. There is none today, so Release 3 enforces single-currency and reports a clear
error rather than silently summing across currencies.

**Removal before vs after submission.** Before submission a document is hard-deleted with its
attachment and items — nothing downstream exists. After submission (draft returned for adjustment) it
is **voided**, because its classification decision, any conflict justification, and its history were
already audited, and audit must survive the object it describes — the Release 2 rule.

### 3.5 Submission rules

A PAYMENT request may be submitted only when **every active source document** has: attachment ·
supplier · document number · a valid `SourceDocumentType` that `CanInitiatePayment` · document date ·
plant · currency · consistent values (§3.4 rule 1) · at least one associated item · **any OCR
classification conflict resolved** (acknowledged, and justified where high-risk) · and its Finance
review flag satisfied where the type requires one.

**One invalid document blocks the whole request.** Drafts may remain incomplete — the rule is a
submission gate, exactly as the Release 2 classification rule is.

Errors are reported **per document**, naming it ("Documento 2 — Simotecnica FT-002"), never as a
single opaque failure.

### 3.6 Obligations per document, and PO group creation

Each document derives its obligations independently:

```
DocumentObligationResolver.Resolve(document.SourceDocumentType, DocumentUsageContext.PaymentRequest)
```

Nothing is aggregated into a request-level type.

**The payment PO-group path must change.** Today it creates **exactly one** group per payment
request, hardcoded from `request.SupplierId` / `CurrencyId` / `EstimatedTotalAmount` — verified in
`ProcessFinalApproval`. That is replaced by grouping over the source documents' items.

**Grouping key — recommended:**

```
Supplier + Currency + PaymentCondition + Plant + SourceDocumentType
```

The first three match the existing quotation-path key. **Plant** is added per the mandate, and
requires a new `RequestPoGroup.PlantId` column — the group has none today.

**`SourceDocumentType` is in the key, and this is a deliberate recommendation, not an oversight.**
Consider one supplier, one plant, two documents: a PROFORMA and an INVOICE. Without the type in the
key they land in one group, which would then have to owe an operation invoice for part of its value
and not for the rest — an obligation the model cannot express, forcing either a `MIXED` type the
resolver cannot handle or a silent compromise. **A PO group is the unit of post-payment obligation,
so two lines with different obligations must not share one.** Groups are cheap; an inexpressible
obligation is not.

**One source document does not imply one group.** A single document whose items span two plants
produces two groups — the same consolidation phenomenon as Part B, on the origin side.

### 3.7 `PaymentSourceDocumentAllocation` — investigated, and **not recommended**

The mandate asks for it only if needed. It is not, and adding it would be actively worse.

**Why it is unnecessary.** `RequestLineItem` already carries **both** `PaymentSourceDocumentId`
(§3.3) and `RequestPoGroupId` (already exists). The allocation of a document to a group is therefore
already fully expressed by its items:

```sql
-- groups this document reaches, and how much of it lands in each
SELECT RequestPoGroupId, SUM(TotalAmount)
FROM   RequestLineItems
WHERE  PaymentSourceDocumentId = @id AND IsDeleted = 0
GROUP  BY RequestPoGroupId
```

**Why it would be worse.** A stored allocation amount can disagree with the sum of the lines it
claims to represent, and then two numbers describe one fact. The derived form cannot drift.
Combined with the §3.4 rule that a document's items must sum to its gross within tolerance, the
derivation is exact.

**Why Part B is different — the asymmetry is deliberate.** An operation invoice's allocation
amounts *are* stored (§6.2), because Finance may allocate money that is not line-attributable —
pro-rata tax, rounding residue, a service charge covering several groups. A payment source document
has no such freedom: its value is its items. Different problem, different answer.

**If the case appears later:** because the derived view is already the contract, a
`PaymentSourceDocumentAllocation` table can be introduced behind it without changing the items, the
grouping, or any consumer.

### 3.8 Payment creation UI — document collection

Only in the Payment request screens (create and draft edit). Structure:

```
DOCUMENTOS DO PEDIDO                                   Total: 1.200.000,00 AOA
────────────────────────────────────────────────────────────────────────────
▼ Documento 1 · Simotecnica · FT-001 · Viana 1              500.000,00 AOA  ✓
    [anexo] [OCR] fornecedor · nº · tipo (?ⓘ⚠) · datas · moeda · planta
    itens ...
▶ Documento 2 · Simotecnica · FT-002 · Viana 2              700.000,00 AOA  ⚠
▶ Documento 3 · (incompleto)                                        —      ✕

[ + Adicionar outro documento ]        [ Duplicar dados básicos do Documento 1 ]
```

- **Collapsible cards** (`CollapsibleSection`, already in the Portal) — one expanded at a time by
  default. A three-invoice request must not become one enormous scrolling form.
- Each card shows a **status glyph** and its own total in the header, so the collapsed view still
  answers "what is missing".
- **At least one document**; add and remove; **each validates independently**.
- **The OCR conflict modal is per document** — Release 2's `SourceDocumentTypeField` with pending-
  value semantics is reused **per card**, with its own conflict state. **No document may overwrite
  another's state**: state is keyed by document id, never by a shared form-level variable. This is
  the single most likely bug in Part A and is called out in tests (§21, case 5–6).
- **Request total recalculates live** from active documents.
- Responsive at 1920×1080 and 1600×900; dark mode; Release 2's icon+modal rule for every contextual
  message.

**"Duplicar dados básicos"** copies supplier, currency, payment condition and department. It **never**
copies attachment, document number, OCR result, values, items, or a classification justification —
those are the facts that make a document a distinct document.

---

# PART B — Operation invoices across PO groups

## 4. Two stages, two entities — never merged

| | `PaymentSourceDocument` | `OperationInvoice` |
|---|---|---|
| When | Initiates a PAYMENT request | Arrives later, against an obligation |
| Scope | Request (PAYMENT only) | Request (both types) |
| Reaches groups via | Its items' `RequestPoGroupId` (derived) | `OperationInvoiceAllocation` (stored) |
| Decides | What is being paid, and what will be owed | Whether what was owed has been delivered |

| Origin document | Consequence |
|---|---|
| `PROFORMA` | Source document → **creates** an operation-invoice obligation |
| `INVOICE` | Source document → **may already satisfy** the obligation for its covered amount |
| `ADVANCE_INVOICE` | Source document → creates operation-invoice **and** advance-regularization obligations |

**One entity must never serve both stages.** A proforma that initiated a payment and the factura that
later discharges it are different documents with different lifecycles, permissions and audits;
collapsing them is how "final invoice" became ambiguous in the first place.

## 5. `OperationInvoice` — the document, Request-scoped

```
OperationInvoice
  Id · RequestId (scope) · AttachmentId (UNIQUE) · SupplierId
  SupplierTaxIdSnapshot · BilledCompanyNameRead
  DocumentNumber · DocumentSeries · DocumentDate · Currency
  NetAmount · TaxAmount · GrossAmount
  Status · AmountsEnteredManually
  UploadedAtUtc · UploadedByUserId · ValidatedAtUtc · ValidatedByUserId
  RejectionReason · SupersededByOperationInvoiceId · RowVersion
```

Additions beyond the mandated list: `SupplierTaxIdSnapshot` and `BilledCompanyNameRead` (the
compatibility checks compare what the *document says* against what the *Portal holds*; storing only
the resolved `SupplierId` discards the evidence that justified the match), and `DocumentSeries`
(required by §10). `AttachmentId` UNIQUE is the structural half of "never duplicate the attachment
per group".

### 5.1 Per-invoice status

| Status | Meaning |
|---|---|
| `UPLOADED` | Received; extraction, allocation suggestion and reconciliation not yet computed. Transient. |
| `PENDING_VALIDATION` | Computed and queued for Finance. |
| `VALIDATED` | Accepted. Its allocations count toward their groups' coverage. |
| `REJECTED` | Judged wrong. Terminal for this row. |
| `REPLACEMENT_REQUESTED` | Right document, unusable copy. Terminal. |
| `DIVERGENCE_DETECTED` | Something Finance must decide explicitly. |

If OCR fails, the invoice still moves to `PENDING_VALIDATION` awaiting manual amounts — it never
sticks in `UPLOADED`.

**Status lives on the document, not the allocation.** A partially-valid document is not a fact about
the world. The consequence is stated in §7.2.

## 6. Allocations and lines

### 6.1 `OperationInvoiceAllocation`

```
Id · OperationInvoiceId · RequestPoGroupId
AllocatedNetAmount · AllocatedTaxAmount · AllocatedGrossAmount
SequenceNumber · RowVersion
```

UNIQUE `(OperationInvoiceId, RequestPoGroupId)` — covering more of a group means a **larger
allocation**, never a second row. UNIQUE `(RequestPoGroupId, SequenceNumber)` — the group's own
numbering, shown as *Fatura 1 de N* for that group.

### 6.2 `OperationInvoiceLine`

```
Id · OperationInvoiceId · OperationInvoiceAllocationId · RequestPoGroupId
BaselineLineId · BaselineLineType · LineNumber · Description
Quantity · UnitPrice · DiscountAmount · TaxAmount · LineTotal · MatchStatus
```

**Two deliberate deviations, both proposed for approval:**

1. **`OperationInvoiceAllocationId` is nullable.** OCR reads twelve lines and eleven are
   recognisable; the twelfth is real and must be *representable* while unallocated. Forcing every
   line into an allocation at read time means inventing an allocation or dropping the line — and
   dropping a line is how an invoice quietly under-reports what the supplier billed. **Rule: any line
   with `MatchStatus = UNALLOCATED` blocks validation.** Visible and blocking, never hidden.
2. **`OperationInvoiceId` and `RequestPoGroupId` denormalized** (both derivable from the allocation).
   The cumulative-quantity query (§9.4) runs per baseline line across every validated invoice and
   would otherwise need two joins on the hottest read path. Set from the allocation on write, never
   independently; enforced by a single write path plus a test.

## 7. Aggregate status, per group

### 7.1 Derivation

Value set: `UNCLASSIFIED` · `NOT_REQUIRED` · `PENDING_UPLOAD` · `PARTIALLY_INVOICED` ·
`PENDING_VALIDATION` · `DIVERGENCE_DETECTED` · `SATISFIED`, with
`Satisfied = { NOT_REQUIRED, SATISFIED }` and everything else blocking.

A single pure function `OperationInvoiceAggregateDeriver.Derive(group, allocationsWithInvoiceStatus,
approvedShortClose)`, over **only that group's allocations**:

```
1. classification not set                                      → UNCLASSIFIED
2. !RequiresOperationInvoice                                   → NOT_REQUIRED
3. any allocation whose invoice is DIVERGENCE_DETECTED         → DIVERGENCE_DETECTED
4. any allocation whose invoice is UPLOADED|PENDING_VALIDATION → PENDING_VALIDATION
5. approved short close                                        → SATISFIED
6. validatedTotal ≥ expected − tolerance                       → SATISFIED
7. validatedTotal > 0                                          → PARTIALLY_INVOICED
8. otherwise                                                   → PENDING_UPLOAD

validated(group) = Σ AllocatedGross where invoice.Status = VALIDATED
pending(group)   = Σ AllocatedGross where invoice.Status ∈ {UPLOADED, PENDING_VALIDATION}
remaining        = max(0, expected − validated)
```

Rejected and replacement-requested invoices contribute nothing but stay visible forever.

**Precedence rationale.** Divergence outranks everything — the only state needing an explicit human
decision before anything can move. Work with Finance outranks work with the uploader, because Finance
can act without a third party. Unresolved rejections collapse into 7–8: the aggregate says *where the
work is*, the always-visible allocation list says *why*.

**Storage.** The existing `RequestPoGroup.OperationInvoiceStatus` column is **repurposed** as the
maintained aggregate rather than adding a parallel column. Every current consumer
(`FiscalReceiptStateDeriver`, `PostPaymentPendingReason`, `GroupBuilderService`, `RequestsController`)
only asks `IsSatisfied` / `IsBlocking` / *is it `UNCLASSIFIED`* — verified — so they keep working; the
Finance queue needs an indexable column; and two columns meaning almost the same thing is how the
identity/obligation confusion started in Release 2. Written by **one function only**, inside the same
transaction as the change that caused it. The four numbers are **derived**, not stored — they are one
`SUM` away, and storing them would create a second source of truth.

### 7.2 One document, several groups — the consequence

An invoice allocated to A and B carries **one** status. If Finance marks it `DIVERGENCE_DETECTED`,
**both A and B become `DIVERGENCE_DETECTED`**; if Finance rejects it, both lose that coverage. This is
correct — the disputed thing is the document — but it means **the UI must disclose the shared scope**
(§14.4), or rejecting one invoice silently moving two groups is inexplicable.

## 8. Compatibility — which groups an invoice may cover

| Rule | What it actually tests |
|---|---|
| **Same supplier** | The real constraint. A multi-supplier quotation, and now a multi-document payment, both produce groups with *different* suppliers in one request; an invoice may only touch its own supplier's groups. |
| **Same supplier NIF** | `Supplier.TaxId` vs the NIF printed on the document. Absent baseline NIF is *not comparable*, never *matched*. |
| **Same legal entity** | Within one request `CompanyId` is constant, so this is not a group-vs-group test — it verifies the invoice was **billed to the right ALPLA entity**, by `Company.TaxId` first, name second. |
| **Same currency** | Mismatch **short-circuits**: every amount comparison becomes meaningless, so report and stop rather than present a nonsense variance. |
| **Same Request** | **Structural** — `OperationInvoice.RequestId` is the scope, so cross-request is impossible to represent (§8.1). |
| **Lines allocable** | Every line maps to a baseline line owned by the group it is allocated to (§9.3). |
| **Allocations reconcile** | Σ allocated ≈ invoice total within tolerance (§9.2). |

Any failure is **blocking**. Finance may **reject**, **request replacement**, or **accept with a
mandatory justification** — never silently accept, and acceptance is a distinct, separately audited
action.

### 8.1 Cross-request scope

Blocked structurally, the strongest available form of "not in this release". Should the case ever
become real, the change is to move the scope up (`OperationInvoice.SupplierId` + allocations reaching
groups across requests) **without touching the allocation or line tables**, which already carry
`RequestPoGroupId` rather than assuming a single parent.

## 9. Expected value, validation and reconciliation

### 9.1 `ExpectedOperationInvoiceTotal`

Captured once at obligation activation, stored on the group (with its currency), because re-deriving
it later would drift when a quotation or line item is edited — silently moving the finish line.

| Request type | Baseline |
|---|---|
| QUOTATION | The winning quotation total for that group |
| PAYMENT | The group's share of its source documents — Σ items assigned to the group |

**The baseline is always the commercial value of what was ordered — never what was paid.** That one
sentence makes the advance case (§12) fall out instead of needing a special rule.

Finance may set it explicitly with a mandatory justification (recorded with actor and timestamp); a
hand-set finish line is shown as such in the UI. **Bounded strictly by §11.**

### 9.2 Two validation layers, both required

**A. The document** — supplier · NIF · billed legal entity · number and series · date · currency ·
`Net + Tax ≈ Gross` · fiscal evidence. An operation invoice that OCR reads as `PROFORMA` or
`ESTIMATE` is a **warning surfaced to Finance**, not an automatic rejection: the Portal states what it
read, Finance decides.

**B. The allocations** — every group compatible (§8) · Σ allocated ≈ invoice total within tolerance,
for net, tax and gross · no `UNALLOCATED` line · no line allocated to a group that does not own its
baseline line · cumulative quantities within baseline (§9.4) · no group pushed past expected beyond
tolerance (§9.5).

**A document cannot be validated while its allocation is invalid.** The endpoint re-evaluates both
server-side and never trusts a verdict computed at upload time.

Under- and over-allocation are reported as **different** failures: under usually means a missing
group, over usually means a double-counted line. Rounding residue is absorbed by tolerance rather
than silently assigned to one group.

### 9.3 Line ownership

Each line's `BaselineLineId` must be owned by the group named in `line.RequestPoGroupId`. A line whose
baseline belongs to A but is allocated to B is rejected **by name** — this is what stops a consolidated
invoice quietly moving cost between groups.

### 9.4 Cumulative quantity — duplicate protection

For each baseline line: `Σ quantity across ALL validated invoices + this invoice's quantity ≤
baseline quantity + tolerance`. Exceeded → `DIVERGENCE_DETECTED`, **naming the line**.

This is why `OperationInvoiceLine` must exist: a cumulative question across documents and groups
cannot live inside a JSON snapshot. Without it the only possible test is the total, which would let
two invoices bill the same item twice while the sum stayed under the expected value.

**The same baseline line invoiced twice at half quantity each is accepted** — legitimate partial
delivery, not a duplicate.

### 9.5 Group over-coverage and tolerance

`validated(group) + thisAllocation.Gross > expected + tolerance` → `DIVERGENCE_DETECTED`, acceptable
by Finance with justification, never absorbed.

Tolerance is `RequestConstants.FinancialIntegrity.CalculateTolerance()` — the mechanism the Portal
already uses. **No new hard-coded tolerance.** Reached through **one** call site,
`OperationInvoiceTolerance.For(amount, context)`, so a Finance-specific tolerance can later be
introduced by changing that function alone, with no change to the state model. The same function is
reused for the Part A totals checks (§3.4).

### 9.6 The snapshot

One immutable `OperationInvoiceReconciliation` row **per allocation per computed comparison** — the
comparison is against a group's baseline, so the allocation is its natural grain. Never updated in
place. Release 1 columns reused; added: `OperationInvoiceId`, `OperationInvoiceAllocationId`,
`RequestPoGroupId`, `NifMatched`, `CompanyMatched`, `ClassificationWarning`, `AllocatedTotal`,
`CumulativeValidatedTotalBefore`, `ExpectedTotalAtComparison`.

Invoice-level identity results are **duplicated onto every allocation snapshot** rather than
normalized. In an immutable audit record that duplication is a virtue: each row answers *"why was
this group's coverage accepted"* completely, years later, without joining to anything.

## 10. Duplicate protection

| Signal | Effect |
|---|---|
| **Attachment hash** already in the request (either stage) | **Hard block** — 409 naming the existing document. With the UNIQUE `AttachmentId`, this enforces "never upload the same document once per group". |
| Same supplier + NIF + number + series + currency + date, different hash | **Finance warning**, not a block. Suppliers legitimately reissue and re-scan. |
| Same supplier + number, everything else different | Warning, lower prominence. |

The structural guarantee matters more than the heuristic: because the invoice is Request-scoped with
allocations, **there is no workflow in which uploading once per group is the natural thing to do.**
The correct action is also the easy one. The same hash rule applies to `PaymentSourceDocument`.

## 11. `ESTIMATE` stays blocked as a Payment origin

`CanInitiatePayment` is false for `ESTIMATE`, enforced at creation and at submission since Release 2,
and now enforced **per source document**.

**The expected-total override must never become a way around it.** The two are orthogonal:

- setting an expected total operates **only on a group that already has a legitimate obligation**;
- it can never create an obligation, change a classification, or make a non-payable document payable;
- the endpoint refuses when the group's source document cannot initiate payment.

Any genuine exception requires a controlled, Finance-reviewed workflow with justification and audit,
**outside the normal Payment path**. That workflow is **not in Release 3**, recorded so it is not
improvised later.

## 12. Advance invoice

1. **Baseline is the full operation value.** The advance paid is a payment fact, never a baseline.
2. **The advance amount is handled through `RequestReconciliation`** — credit note, refund,
   compensation, final balance stay in the existing module. Release 3 writes none of them.
3. **Several operation invoices may cover the operation**, and one may be consolidated across groups.
4. **Multiple advances** remain supported by `RequestReconciliation.ReconciliationSequence` (1:N).
5. **Finance review is mandatory** on every advance document and group.
6. **Coverage alone does not satisfy an advance group**: coverage satisfies the *invoice* obligation,
   the reconciliation satisfies the *money* obligation. Both are required.

Handoff: when a group's aggregate reaches `SATISFIED` and the group carries an advance, Release 3
opens or updates a `RequestReconciliation` for it, carrying the cumulative validated total forward.
For non-advance groups a reconciliation is *offered* when an accepted divergence represents a real
financial difference — never created automatically. `RequestReconciliations` holds 0 rows, so there is
no legacy shape to accommodate.

**These rules remain provisional pending formal Finance confirmation** (§20.2).

## 13. Short close

Buyer **proposes**; Finance **decides**. The Buyer can never approve, including their own proposal.

```
OperationInvoiceShortClose
  Id · RequestPoGroupId · Status (PROPOSED | APPROVED | REJECTED)
  ProposedByUserId · ProposedAtUtc · ProposalJustification (mandatory) · EvidenceAttachmentId
  RemainingAmountAtProposal        ← frozen, so the audit records what was actually written off
  DecidedByUserId · DecidedAtUtc · DecisionReason · RowVersion
```

A separate entity rather than group columns, because it has a review lifecycle and can legitimately
repeat: a rejected proposal leaves the obligation open, and a second proposal is a second row with its
own justification and evidence. Filtered UNIQUE so only one row per group is `PROPOSED` or `APPROVED`
at a time. Finance may propose and approve directly, with both actors recorded. An approved short
close makes the aggregate `SATISFIED` below the expected total (§7.1 rule 5).

---

# PART C — Interfaces, mechanics and delivery

## 14. UI

### 14.1 Payment creation — progressive, one document at a time

**Corrected after manual review of the first Phase 3 implementation.** That version rendered the
legacy `Input de Documento & Faturamento` editor *and* a second full document collection at the same
time: two supplier fields, two document numbers, two totals, and an empty `Documento 1` card
reporting zero next to a form the OCR had already filled. The domain model was right; the screen
asked for the same invoice twice.

The flow is now progressive. It never shows an empty document card, and never shows two document
editors at once:

```
Importar documento ou inserir manualmente
  → rever os dados                         (one reusable editor, bound to one tempId)
  → Confirmar e adicionar documento        (blocked while mandatory issues remain)
  → recolhe num cartão resumido            (supplier · number · type · plant · total · status)
  → adicionar outro documento, se preciso  (OCR / Manual / Duplicar dados básicos)
  → total consolidado                      (confirmed documents only)
  → guardar rascunho ou gerar pedido
```

Four components, one of each, shared by creation and editing:

| Component | Responsibility |
|---|---|
| `AddPaymentDocumentChoice` | How the next document starts. Inline panel for the first, modal for the rest. |
| `PaymentSourceDocumentCard` (`variant="editor"`) | The one open document. Not collapsible while open. |
| `PaymentDocumentSummaryCard` | A document already dealt with, as one scannable line. |
| `PaymentDocumentsSummary` | The consolidated value. Confirmed documents only. |

`lib/paymentDocumentComposition.ts` holds the state model as pure functions — `CompositionState`,
`DocumentLifecycle` (`EXTRACTING` / `EDITING` / `REVIEW_REQUIRED` / `CONFIRMED` / `ERROR`),
`confirmationBlockers`, `submissionBlockers`, `activeDraftDisposition`. Whether a document may be
confirmed is one question with one answer, not something each component decides for itself.

**Confirmation is not submission.** It settles one document inside the client-side composition: its
value starts counting, the editor collapses, and the next document may begin. The backend re-checks
everything at submission and stays authoritative — `confirmationBlockers` deliberately mirrors
`PaymentSourceDocumentValidator` so the user learns about a missing field while looking at the
document rather than at the end of the request.

Consequences that fall out of the model:

- **Items belong to the open editor**, never to a global grid above the cards. A line has an owner by
  construction.
- **Sequence numbers are issued once.** Removing Documento 2 of three leaves Documento 1 and
  Documento 3 — renumbering would rewrite what the user already called "Documento 3".
- **Replacing an attachment un-confirms the document.** It is no longer the document that was
  confirmed, and must not keep saying so.
- **The plant is not copied** by "Duplicar dados básicos". Two invoices from one supplier for Viana 1
  and Viana 2 is the case this feature exists for; copying would pre-fill the wrong answer.
- **The document still open is never lost silently** when a draft is saved (§15 of the corrective
  brief): complete enough → kept, otherwise the user chooses keep / discard / continue editing.

### 14.2 Request Details — per group

```
Faturas da Operação — Grupo: Simotecnica · Viana 1        Parcialmente Faturado
──────────────────────────────────────────────────────────────────────────────
Esperado          10.000.000,00 AOA
Validado           7.000.000,00 AOA  ██████████████░░░░░░  70%
Aguarda validação          0,00 AOA
Remanescente       3.000.000,00 AOA
```

All four always shown including zeros — "nothing awaiting validation" is information. The group header
names its **plant** and **source document type**, both now part of its identity (§3.6).

A separate **"Documentos de origem"** panel lists the request's `PaymentSourceDocument` rows with
their classification, values and the groups they fed.

### 14.3 Allocation list, per group

One row per allocation: sequence, document number/date, **allocated amount**, invoice status,
uploader, validator, reason behind a `FieldMessageIcon`. Superseded rows stay visible, recessed,
linked to their replacement.

### 14.4 Shared-invoice disclosure — required

Wherever an allocation is shown and its invoice covers more than one group:

> **FT 2026/118** — alocado **6.000.000,00** de **10.000.000,00** · partilhado com **2 grupos**

with the other groups reachable from the row. Without this, rejecting one document silently changing
two groups' states would be inexplicable (§7.2). Not decoration — the disclosure that makes the shared
model honest.

### 14.5 Finance working area

New **"Faturas da Operação"** area beside `FinancePaymentsList`, same skeleton, two views:

- **Documentos a validar** — one row per invoice (validation is document-level): supplier, number,
  date, gross, **number of groups covered**, allocation-reconciliation state. Default filter
  `PENDING_VALIDATION` + `DIVERGENCE_DETECTED`.
- **Obrigações** — one row per group: request, group (with plant), source document type with the
  Release 2 fiscal badge, the four coverage numbers, progress bar, aggregate status.

Detail drawer: document panel (identity checks as pass/fail rows, OCR classification reading) above
the allocation panel (one block per group with its baseline comparison and per-line reconciliation),
then the Finance actions.

### 14.6 Rules inherited from Release 2 — non-negotiable

- **Contextual messages are severity icons + modals, never inline blocks.** `FieldMessageIcon` and
  `InfoModal` reused as-is; concise validation errors stay inline.
- **Accepting a divergence, approving a short close, overriding an expected total, and resolving a
  per-document classification conflict** all reuse the conflict-modal pattern: comparison,
  consequence, acknowledgement, justification with live counter, confirm/cancel.
- Responsive at 1920×1080 and 1600×900; comparison and allocation tables scroll in their own
  containers; the page never scrolls horizontally. Dark mode via theme tokens only.

## 15. API surface and permissions

**Part A — payment source documents**

| Method | Route | Permission |
|---|---|---|
| `GET` | `/api/v1/requests/{id}/source-documents` | Requester, Buyer, Finance, approvers |
| `POST` | `…/source-documents` (upload + OCR) | Requester (owner), Buyer, Finance |
| `PUT` | `…/source-documents/{docId}` (fields, classification, conflict resolution) | as above, DRAFT only |
| `DELETE` | `…/source-documents/{docId}` (hard delete before submission, void after) | as above |
| `POST` | `…/source-documents/{docId}/finance-review` | **Finance only** |

**Part B — operation invoices**

| Method | Route | Permission |
|---|---|---|
| `GET` | `…/operation-invoices` · `…/operation-invoice-obligations` | Requester, Buyer, Finance, approvers |
| `GET` | `…/operation-invoices/compatible-groups?supplierId=&currency=` | Requester, Buyer, Finance |
| `POST` | `…/operation-invoices` (upload + initial allocations) | Requester, Buyer, **Finance** |
| `PUT` | `…/operation-invoices/{id}/amounts` · `…/allocations` | Uploader, Finance — while not `VALIDATED` |
| `POST` | `…/operation-invoices/{id}/validate` · `/reject` · `/request-replacement` · `/accept-divergence` | **Finance only** |
| `PUT` | `…/po-groups/{groupId}/operation-invoice-obligation/expected-total` | **Finance only** |
| `POST` | `…/po-groups/{groupId}/operation-invoice-obligation/short-close` (propose) | **Buyer**, Finance |
| `POST` | `…/short-close/{id}/approve` · `/reject` | **Finance only** |
| `GET` | `/api/v1/finance/operation-invoices` · `/operation-invoice-obligations` | **Finance only** |

**Permissions summary.** Requester and Buyer may upload source documents and operation invoices, and
may allocate; they may never validate, reject, request replacement, accept a divergence, change an
expected total, perform a Finance classification review, or approve a short close. **A Finance user
may upload and then validate the same operation invoice** — approved — with `UploadedByUserId` and
`ValidatedByUserId` recorded separately so a segregation rule can be enforced later without a data
migration. **The Buyer may propose a short close and can never approve one.**

### 15.1 DTOs

*Part A* — `PaymentSourceDocumentDto` (fields + attachment, OCR block, classification block, items,
computed items total, validation state), `SavePaymentSourceDocumentDto`,
`PaymentRequestTotalsDto` (per-document totals + request total + reconciliation state).

*Part B* — `OperationInvoiceDto` (+ `Allocations[]`, `IdentityChecks`, `AllocationReconciliation`),
`OperationInvoiceAllocationDto` (group, sequence, allocated net/tax/gross, group coverage after,
`Lines[]`), `OperationInvoiceLineDto`, `OperationInvoiceObligationDto` (per group: aggregate status,
four totals, tolerance, expected-total provenance, allocations, active short-close),
`CompatibleGroupDto` (why compatible, or the specific reason it is not),
`OperationInvoiceReconciliationDto`.

*Commands*, each carrying the relevant `RowVersion`: `UploadOperationInvoiceDto` (with allocations),
`UpdateOperationInvoiceAmountsDto`, `UpdateAllocationsDto`, `ValidateOperationInvoiceDto`,
`RejectOperationInvoiceDto`, `AcceptDivergenceDto`, `SetExpectedTotalDto`, `ProposeShortCloseDto`,
`DecideShortCloseDto`.

*Queue* — `FinanceOperationInvoiceQueueItemDto`, `FinanceObligationQueueItemDto`.

## 16. Concurrency

| Level | Token | Rule |
|---|---|---|
| **Source document** | `PaymentSourceDocument.RowVersion` | Two people editing Documento 1 and Documento 2 never collide; two editing the same document do, and the conflict is reported. |
| **Operation invoice** | `OperationInvoice.RowVersion` | Every Finance decision sends it. **Never auto-retried** — a decision is a judgement about a specific state; re-applying it to a different state is not the same decision. |
| **Allocation** | `OperationInvoiceAllocation.RowVersion` | Same: a stale edit is reported, not merged. |
| **Group aggregate** | `RequestPoGroup.RowVersion` | **Bounded single automatic retry** — the aggregate is a pure function of the allocation set, so reload-recompute-save yields the right answer. |

**Multi-group deadlock avoidance.** Validating a consolidated invoice recomputes several groups in one
transaction; two concurrent validations over overlapping group sets could deadlock if they locked in
different orders. **Groups are always loaded and updated ordered by `RequestPoGroupId`.** Cheap, and
the failure it prevents is intermittent and hard to reproduce.

## 17. Idempotency

| Event | Key |
|---|---|
| Source document classified / conflict overridden | `DC_OVERRIDE:PAYMENT_REQUEST:{PaymentSourceDocumentId}:{AttachmentId}:{SelectedType}` |
| Operation invoice uploaded | `OI_UP:{RequestId}:{AttachmentId}` |
| Validated / rejected / replacement / divergence accepted | `OI_VAL|OI_REJ|OI_REP|OI_DIV:{OperationInvoiceId}` |
| Obligation satisfied | `OI_SAT:{RequestPoGroupId}` |
| Short close proposed / approved | `OI_SHORT_PROP:{ShortCloseId}` · `OI_SHORT:{ShortCloseId}` |

The Release 2 classification-override key **changes scope for PAYMENT**: from the request to the
source document, because a request may now hold several independent classification decisions. The
Release 2 shape is otherwise preserved, and the Quotation key is unchanged.

Validating one consolidated invoice may legitimately emit **several** `OI_SAT` rows — one per group it
completed — each separately deduplicated.

**Transaction-safe duplicate handling.** The Release 1 §6.1 mandate applies in full: a duplicate
history insert must never be handled by catching SQL 2601/2627 and continuing, because the same
`SaveChanges` carries the state transition. Release 2 already built and shipped the pattern —
`SaveChangesWithClassificationAuditRetryAsync` recognises **its own index names**, detaches only the
duplicate audit entity, and re-saves so the business update survives. Release 3 **generalises that
method** into a reusable helper and uses it for every transition above.

## 18. Notifications and audit

`EmailOutbox` with the existing `IX_EmailOutbox_Correlation_Recipient_Active` unique index.
Correlation id = the event's idempotency key, so a retried transition cannot send a second email.

| Event | Recipients |
|---|---|
| `OPERATION_INVOICE_OBLIGATION_ACTIVATED` | Requester, Buyer |
| `OPERATION_INVOICE_UPLOADED` · `_VALIDATION_REQUIRED` | Finance |
| `OPERATION_INVOICE_VALIDATED` | Requester, Buyer |
| `OPERATION_INVOICE_REJECTED` · `_REPLACEMENT_REQUESTED` | Uploader, Requester, Buyer |
| `OPERATION_INVOICE_DIVERGENCE_ACCEPTED` | Requester, Buyer |
| `OPERATION_INVOICE_OBLIGATION_SATISFIED` | Requester, Buyer, Finance |
| `OPERATION_INVOICE_SHORT_CLOSE_PROPOSED` | Finance |
| `OPERATION_INVOICE_SHORT_CLOSE_APPROVED` / `_REJECTED` | Proposer, Requester, Buyer |
| `PAYMENT_SOURCE_DOCUMENT_FINANCE_REVIEW_REQUIRED` | Finance |

A decision on a **consolidated** invoice names every affected group in the body — otherwise a
recipient sees "invoice rejected" and cannot tell which obligation moved. A validation leaving a group
partially invoiced says so: *"validada; faltam 3.000.000 AOA no grupo Simotecnica · Viana 1"*.

**Audit.** One `RequestStatusHistory` row per transition, keyed per §17, naming the document, the
affected groups and the resulting coverage. One immutable reconciliation snapshot per allocation per
comparison. Per-document provenance for both stages. Expected-total overrides, divergence acceptances
and short closes carry actor, timestamp and mandatory justification; short closes additionally carry
the proposer, their evidence and the frozen remaining amount. All history anchors to the request,
which the same-request constraint makes unambiguous for consolidated invoices too.

## 19. Migration, rollback and the Release 4 interface

### 19.1 Migration — `OperationInvoiceWorkflowAndPaymentSourceDocuments`

1. Rename `FinalInvoiceReconciliations` → `OperationInvoiceReconciliations`; rename
   `FinalInvoiceAttachmentId` → `OperationInvoiceAttachmentId`; add `OperationInvoiceId`,
   `OperationInvoiceAllocationId`, `RequestPoGroupId`, `NifMatched`, `CompanyMatched`,
   `ClassificationWarning`, `AllocatedTotal`, `CumulativeValidatedTotalBefore`,
   `ExpectedTotalAtComparison`.
2. **Drop** the six `RequestPoGroups.FinalInvoice*` columns — 0 rows.
3. Add `RequestPoGroups.PlantId` (FK, `NoAction`), `ExpectedOperationInvoiceTotal`,
   `ExpectedOperationInvoiceCurrency`, `ExpectedTotalSetByUserId`, `ExpectedTotalSetAtUtc`,
   `ExpectedTotalJustification`.
4. Create `PaymentSourceDocuments` — UNIQUE `AttachmentId`; UNIQUE `(RequestId, SequenceNumber)`;
   index `(RequestId, IsVoided)`, `(SupplierId, DocumentNumber, DocumentSeries)`.
5. Add `RequestLineItems.PaymentSourceDocumentId` (FK, `NoAction`, nullable); index it.
6. Create `OperationInvoices` — UNIQUE `AttachmentId`; index `(RequestId, Status)`,
   `(SupplierId, DocumentNumber, DocumentSeries)`.
7. Create `OperationInvoiceAllocations` — UNIQUE `(OperationInvoiceId, RequestPoGroupId)` and
   `(RequestPoGroupId, SequenceNumber)`; index `(RequestPoGroupId)`.
8. Create `OperationInvoiceLines` — cascade from `OperationInvoices`; index `(BaselineLineId)`,
   `(OperationInvoiceAllocationId)`.
9. Create `OperationInvoiceShortCloses` — filtered UNIQUE on `RequestPoGroupId` where
   `Status IN ('PROPOSED','APPROVED')`.
10. Index `RequestPoGroups (OperationInvoiceStatus)` for the Finance queue.
11. Rename the attachment type constant to `OPERATION_INVOICE`; add `PAYMENT_SOURCE_DOCUMENT`
    (code only — 0 rows carry the old code).

All FKs `NoAction` except lines→invoice/allocation and source-document→request (cascade): an audit
trail must survive the deletion of what it describes — the Release 2 rule — while a line has no
meaning without its document.

**No data movement anywhere** (§1.1). **Existing single-document PAYMENT requests are not
backfilled**: they keep `Request.SourceDocumentType` and are read through the compatibility path
(§3.2). Backfilling would invent a `PaymentSourceDocument` row and an attachment linkage that nobody
recorded — the Release 1 rule against inventing historical facts.

**The scaffolder output must be checked by hand** — it mis-guessed a rename in Release 2.

### 19.2 Rollback

| Situation | Action |
|---|---|
| Application defect, schema fine | Revert the commit(s). The feature is flag-disabled, so behaviour is already inert. |
| Migration failed part-way | Restore from the pre-migration backup. |
| Schema is the problem, **no rows written** | Reviewed down script: drop the five new tables, drop the two new FK columns, restore the six dropped `RequestPoGroup` columns, reverse the reconciliation rename. |
| **Any new table already holds rows** | **Restore from backup. Never drop a populated table.** An `OperationInvoice` row is the justification for a payment; a `PaymentSourceDocument` row is the justification for a request. |

The practical rollback for a behavioural problem is to **turn the flag off**, which stops every path
in this release without touching schema or code.

### 19.3 Interface offered to Release 4

Release 4 may depend on exactly these, and nothing else:

| Contract | Guarantee |
|---|---|
| `OperationInvoiceStatuses.IsSatisfied(group.OperationInvoiceStatus)` | True iff the operation-invoice dimension is done — `NOT_REQUIRED` or `SATISFIED`, including an approved short close. |
| `…IsBlocking(...)` | True iff the dimension still blocks progression. |
| `OperationInvoiceAggregateDeriver.Derive(...)` | Pure; safe to call for read-only evaluation without a transaction. |
| `group.OperationInvoiceStatus` | Maintained inside the same transaction as any change affecting it — never stale within a committed state. |
| `OI_SAT:{RequestPoGroupId}` | Emitted exactly once per group when the dimension becomes satisfied; Release 4's fiscal-receipt unlock can key on it. |
| `group.PlantId`, `group.SourceDocumentType` | Stable group identity, now including plant. |

**Release 4 must not read `OperationInvoices`, allocations or `PaymentSourceDocuments` directly to
decide completion.** The aggregate is the contract; the documents are an implementation detail.

## 20. Finance decisions and open questions

### 20.1 Approved

- The seven-value taxonomy and the per-type obligations.
- **A PAYMENT request may carry many source documents**, each with its own identity, OCR,
  classification, items and audit. **Quotation Management is unchanged.**
- **A PO group may be covered by many operation invoices**; satisfaction is cumulative.
- **An operation invoice may cover many PO groups of the same request**, stored once with per-group
  allocations. **Cross-request is out of scope and blocked structurally.**
- Finance may upload and validate the same operation invoice, with both actors recorded. Requester and
  Buyer may upload and allocate but never validate.
- **The Buyer may propose a short close and can never approve one.**
- Tolerance reuses the existing `FinancialIntegrity` mechanism, centralised behind one call site.
- Supplier, NIF, company and currency mismatches are **blocking**, resolvable only by reject,
  replacement, or justified acceptance.
- **`ESTIMATE` stays blocked as a Payment origin**, and the expected-total override cannot circumvent
  that.

### 20.2 Provisional — implemented as stated, reversible

- **P1–P5 (advance).** Baseline is the full operation value; the advance difference is a
  regularization handled by `RequestReconciliation`, never a divergence; coverage alone does not
  satisfy an advance group; Finance review mandatory; sequenced advances via `ReconciliationSequence`.
- **P6.** Cumulative validated total may not exceed a group's expected total beyond tolerance without
  explicit, justified Finance acceptance.
- **P7.** Rounding residue when splitting a consolidated invoice is absorbed by tolerance rather than
  assigned to a particular group.
- **P8.** A repeated document number without an identical hash is a warning, not a block.
- **P9 (new).** `SourceDocumentType` is part of the payment grouping key (§3.6), so a proforma line
  and an invoice line for the same supplier and plant produce **two** groups.
- **P10 (new).** All active source documents of one payment request must share a single currency.

### 20.3 Still requiring formal Finance confirmation

None blocks implementation; each has a stated default.

- **Q5 — `OTHER` routing.** Permitted outcomes of a Finance review: reclassify, or set the obligation
  directly? *Default: both, audited.*
- **Q6 — Currency.** May an operation invoice be issued in a different currency from the commercial
  document? *Default: blocking divergence, acceptable with justification.*
- **Q7 — Deadline.** Should an overdue operation invoice raise an alert, and should a *partially*
  invoiced group age differently? *Default: no alert in Release 3.*
- **Q8 — `FA` prefix.** ALPLA uses it for both *Factura* and *Factura de Adiantamento*. *Default:
  continued silence from the fallback classifier.*
- **Q11 — Expected total for estimate-based payments.** *Default: Finance may set it, justified and
  audited, bounded by §11.*
- **Q12 — Tax allocation.** May a consolidated invoice's non-attributable IVA be split pro-rata by
  net? *Default: yes, recorded as such in the snapshot.*
- **Q13 — Reallocation after validation.** Reject-and-re-upload, or a controlled reallocation?
  *Default: reject and re-upload — a validated allocation is immutable in Release 3.*
- **Q14 (new) — Mixed types in one payment request.** Is one request paying a PROFORMA *and* an
  INVOICE from the same supplier a real case, or an error worth warning about? *Default: allowed, and
  it produces two groups (P9).*
- **Q15 (new) — Who may add a source document after submission?** Currently only in DRAFT or during a
  returned adjustment. *Default: DRAFT and adjustment only.*

## 21. Tests

**Part A — payment source documents**

1. One payment request with one document — behaves exactly as Release 2 did.
2. One payment request with two documents — both persist independently.
3. Two documents for **different plants** → two PO groups.
4. Two documents for the **same plant**, same supplier, same type → **one** group.
5. **Independent OCR classification** — document 2's suggestion never appears on document 1.
6. **Independent classification conflicts** — resolving document 1's conflict leaves document 2's
   unresolved, and neither overwrites the other's state.
7. One invalid document blocks submission of the whole request, naming the document.
8. Request total equals the sum of active document totals.
9. A removed/voided document is excluded from the total and from grouping.
10. Items remain associated with the correct source document across edit and reload.
11. `Request.SourceDocumentType` is **not** read to derive an obligation on a multi-document request.
12. `Request.SupplierId` / `PlantId` are null on a mixed request and no consumer misreports.
13. Every item must carry a `PaymentSourceDocumentId` at submission.
14. Mixed currencies across documents are rejected with a clear error.
15. **No multi-document behaviour appears in Quotation Management** — the wizard, quotation
    persistence, quotation OCR and `GroupBuilderService` are unchanged, asserted explicitly.
16. Two documents with the same supplier and plant but **different types** → two groups (P9).

**Part B — operation invoices**

17. One invoice → one group.
18. One invoice → two compatible groups (6M + 4M of 10M), evaluated independently.
19. Incompatible **supplier** blocked, naming the group.
20. Incompatible **NIF** blocked.
21. Incompatible **company** (billed to the wrong legal entity) blocked.
22. Incompatible **currency** blocked; the amount comparison short-circuits.
23. Allocations **below** the invoice total beyond tolerance → invalid, reported as under-allocation.
24. Allocations **above** → invalid, reported as over-allocation.
25. Allocations within tolerance (rounding residue) → valid.
26. A line whose baseline belongs to A allocated to B → blocked, naming the line.
27. **Duplicate baseline quantity protection** — cumulative over-coverage per line → divergence,
    naming the line.
28. The same baseline line invoiced twice at half quantity each → **accepted**.
29. Partial invoicing leaves the group `PARTIALLY_INVOICED`; a later invoice satisfies it.
30. **Independent aggregate state per group** — rejecting a consolidated invoice moves both groups;
    validating satisfies only groups whose coverage is complete.
31. **Exact duplicate attachment blocked** (both stages), naming the existing document.
32. Repeated document number with a different hash → warning, not a block.
33. `UNALLOCATED` lines block validation.
34. **Cross-request allocation blocked.**
35. Only `VALIDATED` invoices count toward coverage.
36. Coverage within tolerance satisfies; just outside does not.
37. Group over-coverage beyond tolerance → divergence.

**Aggregate, short close, permissions, mechanics**

38. Each of the seven document types yields the correct initial aggregate per context.
39. `UNCLASSIFIED` blocks upload, allocation, validation and progression.
40. Precedence: divergence > pending-validation > pending-upload.
41. Derivation is pure — the same allocation set always yields the same status.
42. **Buyer proposes** a short close with justification and evidence → obligation still open.
43. **Finance approves** → `SATISFIED`, audited, `OI_SHORT` emitted once.
44. **Buyer cannot approve** — including their own proposal.
45. Finance rejects → obligation open; a second proposal is allowed.
46. Only Finance may validate, reject, request replacement, accept divergence, set an expected total,
    perform a classification review, or decide a short close.
47. Finance may upload **and** validate the same operation invoice, with both actors recorded.
48. **`ESTIMATE` cannot originate a Payment, and setting an expected total cannot make it payable.**
49. Idempotency: retried validate → one history row; a consolidated validation emits one `OI_SAT` per
    completed group.
50. Transaction safety: a duplicate history insert never discards the status transition.
51. Document concurrency: stale `RowVersion` → conflict reported, decision **not** auto-retried.
52. Group concurrency: two invoices validated concurrently touching one group → both succeed,
    aggregate correct after the bounded recompute retry.
53. Deterministic group ordering under multi-group validation (no deadlock).
54. Advance: baseline is the full operation value; the difference routes to `RequestReconciliation`;
    coverage alone does not satisfy.
55. Multi-supplier quotation: an invoice may only touch its own supplier's group.
56. `INVOICE_RECEIPT` satisfies in post-payment evidence context; still cannot originate a request.
57. Feature flag off → every endpoint inert, no state written.
58. Snapshot immutability: a second comparison creates a second row.

**Frontend:** this repository still has **no frontend test framework**. The document-collection UI,
the allocation UI and the Finance area are covered by manual validation only, as Releases 1–3 have
been. Adding vitest + testing-library remains a separate, self-contained change and should not be
bundled here.

## 22. Suggested delivery order

1. **Rename pass + migration** (§1.1, §19.1), scaffolder output checked by hand. Green build and
   suite before anything else is written.
2. **`Request.PlantId` / `SupplierId` compatibility review** — the ~57 + ~20 consumers found in
   §3.2, classified as safe / needs-group / needs-document. This is the main regression risk in Part
   A and is done *before* the model changes, not after.
3. `PaymentSourceDocument` entity, item association, totals and validation — tests 1–2, 8–14.
4. Payment PO-group creation over source documents, including plant and type in the key — tests 3–4,
   16; plus test 15 asserting the quotation path is untouched.
5. Payment creation UI (document collection, per-document conflict) — tests 5–7.
6. `OperationInvoice` / allocation / line entities, expected-total capture, and the pure
   `OperationInvoiceAggregateDeriver` — tests 38–41 **before any endpoint exists**.
7. Compatibility resolver and `compatible-groups` — tests 19–22, 34.
8. Upload with allocations, sequence numbering, hash duplicate block — tests 17–18, 31–33.
9. OCR extraction, manual-entry fallback, suggested split, and the shared reconciliation core
   extracted from `QuotationReconciliationCalculator`.
10. Allocation arithmetic and cumulative checks — tests 23–30, 35–37.
11. Finance decisions with document/allocation/group concurrency and the generalised transaction-safe
    save — tests 49–53.
12. Expected-total override (bounded by §11) and the short-close pair — tests 42–48.
13. Advance path and the `RequestReconciliation` handoff — test 54.
14. Finance UI (two views) and the per-group allocation panel with shared-invoice disclosure.
15. Notifications.
16. Full validation, `/task-review`, one commit, local manual validation.

Each step keeps the suite green and the feature flag off.
