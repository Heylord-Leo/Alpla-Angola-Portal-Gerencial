# Post-Payment Completion — Release 4: Operation Invoice (Phase 1)

> ## RELEASE 4 — CLOSED / ACCEPTED IN TEST (2026-08-18)
>
> **Final acceptance recorded 2026-08-18 at v2.229.9 (`d0c91d4`, TEST build
> `2.229.9+d0c91d4`, runtime `Enabled=true, CompletionEnabled=true,
> EffectiveDateUtc=2026-08-06T00:00:00Z`).** All Release 4 acceptance criteria passed:
>
> - **STATE 1 (dormant safety, `CompletionEnabled=false`) — PASSED** on
>   REQ-17/08/2026-232: full lifecycle to every completion dimension satisfied (P.O.,
>   advance payment, receiving handoff + quantity registration + attestation with the
>   persisted OperationalReceipt fact, Final Invoice registration/allocation/validation,
>   Fiscal Receipt binding), UI honestly read "Requisitos de conclusão satisfeitos", and
>   NOTHING transitioned: group and request were NOT persisted COMPLETED, no automatic
>   closure. Six defects found and fixed during the cycle (v2.229.1–.6 below).
> - **STATE 2 (active lifecycle, `CompletionEnabled=true`) — PASSED** on
>   REQ-18/08/2026-233, clean end-to-end: Final Approval → Aguardando P.O. → PO
>   registration → advance payment → Ag. Entrega/Serviço → Receiving workspace →
>   operational receiving + attestation → Final Invoice + validation → Fiscal Receipt →
>   group persisted COMPLETED (GROUP_COMPLETED history, "Grupo Concluído") → parent
>   persisted COMPLETED automatically (REQUEST_COMPLETED history, CompletionCycleId,
>   "Pedido Concluído"/Finalizado) — **no manual FinalizeRequest anywhere**. Final 8-stage
>   timeline: Rascunho · Cotação · Aprovações · P.O. / Contratação · Pagamento ·
>   Recebimento / Execução · Documentação Fiscal · Concluído.
> - **Dormant→active recovery — PASSED** on REQ-232: "dormant facts → lifecycle
>   activation → legitimate later trigger → automatic Phase-1 + Phase-2 recovery". The
>   parent-sweep PREVIEW (live from TEST: `completionEnabled=true, eligibleCount=0,
>   skippedCount=0, requests=[]`) correctly did NOT list it — its group was not persisted
>   COMPLETED, and the sweep is by design Phase-2-only, never inferring group completion
>   from readiness. A legitimate repeated ConfirmReceiving then produced
>   CONFIRM_RECEIVING → GROUP_COMPLETED → REQUEST_COMPLETED → Finalizado, with the
>   previously recorded OperationalReceipt fact as the underlying receipt evidence.
>   Sweep APPLY was never executed (zero eligible candidates — expected, not a gap).
> - **Final automated validation** (2026-08-18, at `d0c91d4`): backend
>   **1489 passed / 1 failed / 1490** — the single failure is the pre-existing
>   `GroupBuilderServiceTests.BuildGroupsForRequestAsync_CreatesGroups_WhenLineItemsHaveSelectedQuotation`
>   baseline (pre-dates Release 4, reported separately, deliberately not "fixed" in
>   closure); frontend `npx tsc --noEmit` clean, `npm run build` successful.
> - **Migration inventory for the whole v2.229.x range**: NO schema migration (the
>   Release 1–3 schema carried the entire lifecycle). Two DATA-ONLY RC repairs:
>   `20260817101152_RepairWorkflowStatusNamesAndAwaitingPo` (NCHAR-idempotent status-name
>   Unicode repair + Aguardando-P.O. parked-request correction) and
>   `20260817124004_HandoffParkedAdvancePaidGroupsToDelivery` (advance-paid parked
>   groups/parents → WAITING_SUPPLIER_DELIVERY). Both applied in TEST; both are exactly
>   what PROD will need (PROD carries the identical corruption/parked rows).
> - Remaining findings are classified backlog / Release 4.1 (see "Closure backlog" below)
>   — **no unresolved Release 4 correctness blocker**. PROD untouched
>   (`PostPaymentCompletion` absent there = defaults false); no PR/merge/tag; sweep APPLY
>   never run; Phase 5 not started.
>
> **PROD rollout plan (prepared, NOT executed, separate authorization required):**
> 1) full PROD backup; 2) Apply PROD migrations (the two data-only repairs above ride the
> normal pending-migration chain); 3) deploy API+Web `v2.229.9` with
> **`CompletionEnabled=false`** (add the `PostPaymentCompletion` section explicitly:
> `Enabled=true, CompletionEnabled=false, EffectiveDateUtc` per business decision) —
> deploy/restart alone must not and will not complete anything (no startup evaluation
> exists; verified in TEST); 4) verify legacy operation + status-name repairs + parked
> corrections + readiness surfaces (a STATE-1-style dormant check); 5) SEPARATELY
> authorize `CompletionEnabled=true` (config-only, the TEST activation runbook applies);
> 6) sweep stays preview-first — APPLY only after reviewed preview.
>
> ### Closure backlog (deferred — NOT Release 4 blockers)
> - **A. PO Packaging / REQ-21/07/2026-131 (→ Release 4.1)** — same supplier + currency +
>   payment condition merges multiple quotation documents into one `RequestPoGroup`;
>   RegisterPo is 1 PO : 1 group. Verdict: **BY-DESIGN BUT BUSINESS MODEL INSUFFICIENT**.
>   Business rule to implement: "Same supplier does not necessarily mean same Purchase
>   Order." Preferred model (approved direction): Buyer-defined PO packages — default one
>   package per quotation document, Buyer may merge/split before the first PO, package
>   frozen after PO registration, `RequestPoGroup` remains the downstream Release 4
>   identity anchor. Includes the missing regression suite (same supplier + two quotation
>   documents; first-ever `BuildGroupsForBatchAsync` coverage).
> - **B.** Finance workspace: dedicated Final Invoice / completion queue.
> - **C.** Finance Payments header supplier "---" for batch-model QUOTATION requests
>   (`FinanceController` resolves legacy `SelectedQuotationId`/`Request.Supplier`).
> - **D.** Receiving workspace: group-aware listing for multi-group requests.
> - **E.** Dual receiving-record unification (RequestLineItem × winning QuotationItem).
> - **F.** Phase 5 — Final Invoice OCR / document intelligence.
> - **G.** Monetary-input residuals: legacy `RequestCreate` OCR item editor and Approvals
>   `WizardStepAllocation` still use `type="number"`.
> - **H.** REQ-229-class legacy APPROVED awaiting-P.O. presentation = expected,
>   non-blocking; Release 5 legacy classification tooling; short-close reopening
>   workflow; generic drawer hydration.
>
> Closure is documentation-only: v2.229.9 remains the final Release 4 version (no
> artificial bump). Release 4.1 design is referenced here as backlog only — nothing of it
> is implemented in Release 4.
>
> ---
>
> Earlier status (historical): **Phase 1 closed at v2.225.0. Phase 2 (a–e) complete and approved — closed at
> v2.226.0 on `Portal-Gerencial-rev1`.** The OperationInvoice document lifecycle exists
> (API-only); **allocation writes, UI and OCR do not** — they are Phases 3+.
>
> **TEST deployment order for v2.226.0 (not automatic):** 1) apply migrations on TEST
> (`20260811090848_AddOperationInvoicePhase2Fields` has NOT been applied there yet);
> 2) deploy TEST. Never the reverse.

## Release numbering

The delivered sequence is authoritative: **Release 3 = multi-document PAYMENT**
(closed at v2.224.0, see `POST_PAYMENT_COMPLETION_RELEASE3.md`), **Release 4 = Operation
Invoice**. The older `POST_PAYMENT_COMPLETION_PLAN_V7.md` §8 predates that split and labels this
scope "R3 — Operation Invoice"; read its "R3" as this Release 4. No migration, entity or
historical changelog entry is renamed on account of the numbering.

## Phase 1a — Obligation projection (commit `f2533fd`)

`OperationInvoiceObligationProjector` (Domain, pure) composes the two existing sources of truth —
`DocumentObligationResolver` ("is an operation invoice owed?") and
`OperationInvoiceAggregateDeriver` ("how covered is it?") — into one explained answer per PO
group plus a request-level rollup. It introduces no rule of its own, persists nothing, and never
repairs what it finds.

Context invariance is a pinned contract: what a document **owes** never depends on where it is
presented (`DocumentUsageContext` affects only blocking/review flags). Guard test:
`DocumentObligationResolverTests.What_a_document_owes_never_depends_on_where_it_is_presented`.

## Phase 1b — Read-only endpoint (commit `2dd7b22`)

`GET /api/v1/requests/{id}/operation-invoice-obligations` on `RequestsController`:

- same visibility scope as every request read (`GetScopedRequestsQuery`);
- gated on `PostPaymentCompletion.Enabled` — while disabled it returns the same 404 as an
  unknown request (the Release 1 "no new endpoint is reachable" contract);
- derived status is returned **beside** the cached `OperationInvoiceStatus`; a disagreement is
  `statusDrift: true`, logged as a warning, never repaired on read;
- superseded operation invoices are excluded from coverage;
- contributing `PaymentSourceDocumentIds` come from the group's own line items
  (`RequestLineItem.PaymentSourceDocumentId`), deduplicated in line order — never from the
  request header.

## Phase 1c — Re-stamping invariant (commits `37594be`, `3dc4768`)

**The invariant:** document classification and the group obligation cache commit **together or
not at all** — same database transaction, no background jobs, no startup repair, no lazy repair
from GET. The projector remains the diagnostic net for historical or unexpected drift.

Write-path inventory (as of Phase 1c):

| Path | Statuses | Groups can exist? | Behaviour |
|---|---|---|---|
| `PaymentSourceDocumentsController.Update` | DRAFT, AREA_ADJUSTMENT, FINAL_ADJUSTMENT | Defensively yes (PAYMENT adjustment is currently blocked at both approval stages, so today only DRAFT is reachable — pre-groups) | Re-stamps affected groups via `PoGroupReclassificationPlanner`, or refuses with a typed 409 |
| `RequestsController.UpdateRequest` header type | DRAFT only | Never | No re-stamp needed |
| Quotation update (`Quotation.DocumentType`) | DRAFT, WAITING_QUOTATION, AREA_ADJUSTMENT, FINAL_ADJUSTMENT | PENDING groups only | Self-healing: batch return **deletes** the batch's PENDING groups; (re-)approval rebuilds and re-stamps through `GroupBuilderService`, which refuses to touch a group with post-payment activity |
| Finance classification review (Release 5) | future | Yes | **Must** reuse `PoGroupReclassificationPlanner` |

Affected groups are identified by **line ownership** (`PaymentSourceDocument →
RequestLineItems.PaymentSourceDocumentId → RequestPoGroupId`), never by supplier, request id or
the old type.

### Mixed-document groups (grouping-key invalidation) — explicit business restriction

The grouping key is Supplier + Currency + PaymentCondition + Plant + SourceDocumentType. If one
document in a group is reclassified while its siblings keep the old type, the group can no longer
satisfy the key. **The change is refused** (`409`, code `GROUPING_KEY_INVALIDATED`) — financial
groups are never silently regrouped after approval. Such a correction requires Finance
reconciliation; an automatic regrouping policy is a future business decision.

Equally refused: reclassifying a document whose group has started its post-payment lifecycle
(`OPERATION_INVOICE_ACTIVITY_STARTED`) and voiding a document whose lines feed a group
(`SOURCE_DOCUMENT_IN_PO_GROUP`).

### Re-stamped fields

`SourceDocumentType`, `OperationInvoiceStatus`, `RequiresOperationInvoice`,
`RequiresSeparateFiscalReceipt`, `RequiresAdvanceRegularization`,
`RequiresFinanceClassificationReview` — and, on a permitted commercial re-stamp (Phase 1d),
the identity columns `SupplierId` + supplier snapshots, `CurrencyCode`/`CurrencyId`, `PlantId`.
Nothing else. One audit row (`GRUPO_OBRIGACAO_REDERIVADA`) explains the document transition and
the group consequence.

## Phase 1d — Grouping-key integrity (full key)

Phase 1c protected `SourceDocumentType`; Phase 1d generalizes the same planner to the complete
key **Supplier + Currency + PaymentCondition + Plant + SourceDocumentType**, compared through
`PaymentGroupingKey` — the group builder's own canonical normalization, never a second algorithm.

Field ownership (PAYMENT): supplier, currency, plant and type are **document-owned**
(`PaymentSourceDocument`), reach groups through line ownership, and are editable only through
the source-document Update endpoint. The **payment condition is not document-owned**: the
request-level value feeds the key at build time (currently never written → null), and the Buyer
legitimately refines the GROUP's own value at P.O. registration — so a document edit can never
diverge it, and the Buyer flow is not guarded. `Request.PlantId` remains routing-only and plays
no part in the key.

Rules, in order, per affected group (identified by line ownership only):

1. contributors that would **disagree** on the key → `409 GROUPING_KEY_INVALIDATED`
   (any dimension; the message names which);
2. contributors still agreeing with the group's stamp → no action;
3. a change to a **commercial dimension** (supplier/currency/condition/plant) is blocked by any
   financial evidence — registered P.O. number, P.O. attachments, payments, reconciliations,
   operation-invoice activity, or a captured `ExpectedOperationInvoiceTotal` when the currency
   would change (the snapshot is denominated in it) → `409 GROUP_FINANCIAL_EVIDENCE_EXISTS`;
4. a **type-only** change is blocked by operation-invoice activity alone
   (`409 OPERATION_INVOICE_ACTIVITY_STARTED`) — a P.O. documents the commercial identity, not
   the obligation the type derives. **This type-vs-commercial distinction is explicitly
   approved** (Phase 1d closure): a type-only coherent re-stamp may proceed under a registered
   P.O. when there is no operation-invoice / short-close / reconciliation / receipt activity and
   grouping remains internally valid, while commercial-dimension changes stay blocked once any
   downstream commercial evidence exists;
5. otherwise the group is atomically re-stamped in the same transaction, snapshots refreshed,
   expected total untouched, one audit row.

Pre-group edits (DRAFT) keep today's behaviour untouched. The internal-ALPLA supplier rule runs
first, under its own contract (400, its own code) — grouping-key integrity never absorbs it.

## ExpectedOperationInvoiceTotal — snapshot semantics

- Captured **once**, when a group requiring an operation invoice is created (PAYMENT: planned
  group total at `BuildPaymentPoGroupsAsync`; QUOTATION since Phase 1c: awarded group total in
  `GroupBuilderService.ApplyDocumentClassification` — the same convention, not a second one).
- A classification correction **never recalculates and never clears it**, including a transition
  to NOT_REQUIRED. It is a financial snapshot; the deriver simply ignores it while no obligation
  exists, and it is already correct if the obligation later reopens.
- **No historical backfill.** A legacy/pre-flag group keeps `null` and surfaces honestly as
  `EXPECTED_TOTAL_UNKNOWN` (amounts `null`, never 0) — closable only by short-close or by Finance
  setting the total by hand (`ExpectedTotalSetByUserId` + justification).
- Rollup amounts are summed **per currency only**; groups with no currency land in the `UNKNOWN`
  bucket. There is no fallback to request-level currency.

## Phase 2 — OperationInvoice CRUD (2a–2e, closed at v2.226.0)

The manual final-invoice document lifecycle, header-only, on
`/api/v1/requests/{id}/operation-invoices`. **Allocation does not exist yet** — Phase 3 owns
allocation and reconciliation exclusively.

> **v2.228.1 correction — obligation-driven, never type-driven.** Phase 2b shipped with a
> PAYMENT-only guard on Create ("Faturas finais existem apenas em pedidos de Pagamento") that
> the Phase 3 model superseded: OperationInvoice is request-scoped and OBLIGATION-driven, and
> **both PAYMENT and QUOTATION register final invoices**. The guard is now: the request must own
> at least one classified `RequestPoGroup` with `RequiresOperationInvoice = true`, else 409
> `OPERATION_INVOICE_NO_OBLIGATION`. Approved tightening: a legacy PAYMENT request whose groups
> are all UNCLASSIFIED is refused too — an invoice that could never be allocated must not be
> registered. Guard order in Create: visibility 404 → role 403 → obligation 409 →
> lifecycle-status 409 → field validation → supplier/attachment integrity → duplicates.

```
create (Finance/Buyer) ──► PENDING_VALIDATION ──► VALIDATED ──► REPLACEMENT_REQUESTED
                                │        │            (immutable;      (terminal; forward
                                │        │             Finance-only     pointer to the
                                │        │             replacement)     correction)
                                │        └──────────► REJECTED  (terminal; identity+file released)
                                └───────────────────► VOIDED    (terminal; identity+file released)
```

Permissions: **Finance** creates/updates/voids and is the only role that validates, rejects and
replaces. **Buyer** creates/updates/voids editable invoices (no uploader-only ownership).
**Requester/Receiving** are read-only. **SystemAdministrator** follows the administrative can-act
convention with no financial-integrity bypass. `DueDate` is optional throughout.

**Lifecycle** (`OperationInvoiceLifecyclePolicy`, pure): manual creation lands in
`PENDING_VALIDATION` (`UPLOADED` is reserved for the future OCR intake and never skips the
queue). Editing and voiding stop at validation. **The Finance validation boundary**: only
Finance (SysAdmin per the administrative can-act convention, with no integrity bypass) decides
`PENDING_VALIDATION → VALIDATED | REJECTED`, and validation re-runs every integrity gate against
the persisted row — header completeness, net+tax tolerance, internal-ALPLA supplier, attachment
validity, and BOTH global duplicate dimensions — so a duplicate that appeared after creation can
never become a second effective invoice. **VALIDATED is immutable**: no edit, no void; correction
is Finance-only replacement (old → `REPLACEMENT_REQUESTED` + reason + forward pointer; corrected
invoice enters the queue like any other). **Rejection releases both duplicate identities**
(fiscal identity and file hash — approved: a rejection may concern metadata, not the physical
file, so the same file may return). **Validation without allocation creates no coverage**: a
VALIDATED unallocated invoice changes nothing in Phase 1 — trust comes from validation, coverage
only from Phase 3 allocation. `DueDate` is optional and never blocks. Duplicates are global for
effective invoices on both dimensions; terminal statuses (`REJECTED`, `VOIDED`,
`REPLACEMENT_REQUESTED`) release them; `DIVERGENCE_DETECTED` remains effective. Exact retries
are idempotent (create-by-attachment, re-void, identical replace, re-validate, re-reject) —
conflicting decisions never are.

**Rejected-replacement lifecycle** (reviewed, intentional): A(validated) → replaced by B →
B rejected ⇒ A stays `REPLACEMENT_REQUESTED` (a recorded Finance decision is never rewritten),
B is terminal and cannot be replaced from. The recovery path is a **plain Create** — both A and
B are non-effective, so the fiscal identity is free; the new invoice C starts unlinked to the
dead chain. Known cosmetic gap, accepted: B carries no pointer to C, so the audit chain is
A→B (rejected), C standalone.

## Phase 3 — CLOSED at v2.228.4 (Phases 3A + 3B)

**Formally closed after the TEST validation cycle v2.228.0 → v2.228.4** (pending the final
v2.228.4 TEST smoke). The manual acceptance record: Scenario A (REQ-12/08/2026-230 — QUOTATION
classification/expected capture, full and cumulative coverage, the complete Finance divergence
flow at 103,2% with frozen +30.000 reconciliation, reject/re-derive) and Scenario B (incomplete
allocation block, 90% partial coverage, short-close with frozen remaining, segregation of
duties, "Encerrado com Saldo Aceite" never presenting as 100%). Patch trail:

- **v2.228.1** — registration is obligation-driven, never type-driven (PAYMENT + QUOTATION;
  `OPERATION_INVOICE_NO_OBLIGATION`).
- **v2.228.2** — drawer menu layering; calendar-date vs UTC-instant display split.
- **v2.228.3** — SATISFIED is financial, not structural: Finance divergence on fully covered
  groups; `OI_ALLOC_GROUP_CLOSED_SHORT` hard blocker; wizard eligibility + justification gate.
- **v2.228.4 (closure patch)** — supplier-at-registration rule
  (`OPERATION_INVOICE_SUPPLIER_NOT_IN_REQUEST`: the invoice supplier must own an
  obligation-bearing group of the request; modal offers only those suppliers); standard 401/403
  session handling in the Phase 3B API module; lifecycle gating of every Phase 3 action (the
  coverage section stays a pre-Final read-only preview); short-close hidden while a pending
  invoice covers the remaining (validated=0 full-balance proposals stay possible with an
  explicit warning — backend policy unchanged by decision); "Divergência Aceite: +valor" badge
  (derived from the validation-gate invariant: effective coverage above expected + tolerance
  exists only through explicit acceptance).

Open backlog carried out of Phase 3 (deliberately NOT in scope): dedicated Finance "Faturas
Finais" workspace; monetary input masking; OCR/autofill (Phase 5); generic drawer hydration;
short-close reopening workflow; Release 5 legacy classification. Phase 4 (completion lifecycle,
operational/fiscal receipts, `CompletionEnabled=true`) started AFTER this closure — see the
Phase 4 section below.

## Phase 4 — Completion lifecycle (Phases 4A–4D CLOSED; 4E RC = v2.229.0)

**Status: Phase 4A CLOSED · Phase 4B CLOSED · Phase 4C CLOSED · Phase 4D CLOSED · Phase 4E
RC prepared as v2.229.0 (2026-08-17).** First TEST deployment ships with
`Enabled=true, CompletionEnabled=false`; activation (`CompletionEnabled=true`) is a separate,
explicitly authorized step after the State 1 manual checklist.

### Final architecture (authoritative record)

```
GroupComplete =
    Classified                       // OperationInvoiceStatus ≠ UNCLASSIFIED ∧ SourceDocumentType ≠ null
    AND PoSatisfied                  // group left PENDING / WAITING_PO
    AND NoBlockingCorrection         // group ≠ WAITING_PO_CORRECTION
    AND PaymentSatisfied             // no owed PLANNED/SCHEDULED payment; no active reconciliation;
                                     // regularization discharged when required; paid stage reached
    AND ReceiptSatisfied             // operational receipt stamp, or item records prove full receipt
    AND OperationInvoiceSatisfied    // aggregate NOT_REQUIRED | SATISFIED (incl. accepted
                                     // divergence and approved short-close)
    AND FiscalReceiptSatisfied

FiscalReceiptSatisfied =
    RequiresSeparateFiscalReceipt ? (attachment bound + upload stamp) : true

RequestComplete =
    every relevant (non-cancelled) group persisted COMPLETED
    AND no active request-level reconciliation
```

Final closure notes on the predicate (2026-08-18): `FiscalReceiptSatisfied` =
`RequiresSeparateFiscalReceipt ? (valid bound Fiscal Receipt) : true`; UNCLASSIFIED fails
closed; COMPLETED is terminal with no automatic reopen; `RequestCompletionService` is the
SOLE first-writer of COMPLETED for grouped classified requests while
`CompletionEnabled=true` (LineItems shortcut delegates, aggregation defers/reaffirms only,
sweep re-invokes the same service).

**Final effective trigger matrix (as shipped, re-audited at closure):** ConfirmReceiving ·
Fiscal Receipt binding · OperationInvoice Validate / Reject / Void / Replace · short-close
APPROVE · MarkAsPaid · ConfirmAdvancePayment · ReconcileRequest · RegisterPo (incl. the
corrected-PO path) · LineItems last-item shortcut (delegating) · SysAdmin recovery sweep
(Phase 2 only). Each trigger runs Phase 1 (`EvaluateGroupCompletionAsync`) inside the
caller's transaction before SaveChanges and Phase 2 (`EvaluateParentCompletionAsync`)
strictly post-commit, never failing the business action; all are inert while the flags are
off. No direct competing COMPLETED writer remains for grouped classified requests.

**RC correction series (STATE 1/2 findings — all deployed to TEST):**
- **v2.229.1** — "Aguardando P.O." (PO_REQUESTED repurpose) + status-name Unicode repair +
  migration UTF-8 transport hardening (data-only migration).
- **v2.229.2** — full-advance PaymentSatisfied by amount evidence (`paidInFull`).
- **v2.229.3** — advance-paid → WAITING_SUPPLIER_DELIVERY handoff (+ data-only repair
  migration); `evidenceContradictsLadder` refinement.
- **v2.229.4** — operational receiving attestation modal + optional `RECEIVING_EVIDENCE`
  attachment type (legacy `RECEIPT` untouched).
- **v2.229.5** — dual-record batch-model receiving fact (`OperationalReceiptFacts` reads
  either side of the award pointer; fail-closed hardening).
- **v2.229.6** — "Grupo Concluído" requires persisted COMPLETED (readiness reads
  "Requisitos Satisfeitos"/"Pronto para Concluir"); persisted-based group counts.
- **v2.229.7** — legacy FinalizeRequest UI suppressed for grouped+classified requests
  under the active lifecycle; readiness-derived header guidance.
- **v2.229.8** — locale-independent monetary inputs (shared `MoneyInput`).
- **v2.229.9** — 8-stage timeline (Recebimento / Execução × Documentação Fiscal) +
  Fiscal Receipt modal visual alignment.

Invariants closed with Release 4:

- **PaymentSatisfied recognizes authoritative evidence (v2.229.2, REQ-17/08/2026-232)** —
  the paid-status ladder covers only the standard branch; a full advance leaves the group in
  `ADVANCE_PAYMENT_COMPLETED` and may never visit a ladder status. The predicate is
  therefore `(paidStage OR paidInFull)`, where `paidInFull` = Σ COMPLETED owed-money rows of
  the GROUP ≥ `TotalAmount` − standard tolerance. Null-group rows block when pending but are
  never counted as any group's money; partial advances stay pending even before their
  FINAL_BALANCE row exists; reconciliation/regularization blockers are unchanged.
- **ReceiptSatisfied recognizes both receiving records (v2.229.5, REQ-17/08/2026-232)** —
  the receiving record legitimately lives on either side of the award pointer. The legacy
  QUOTATION flow registers on the winning `QuotationItem`; PAYMENT (no pointer) and the
  batch/candidate QUOTATION model (pointer kept only for compatibility, request-level
  `SelectedQuotationId` null, UI registers on the line item) both write the
  `RequestLineItem`. `OperationalReceiptFacts.AreAllGroupItemsReceived` therefore accepts an
  item as received when EITHER record reads RECEIVED — neither side alone is authoritative.
  Empty or all-soft-deleted item collections fail closed; partial receiving on both sides
  still blocks. (The dual receiving-record storage itself is recorded technical debt for a
  future unification; the frontend writer keeps its batch-model behavior.)
- **UNCLASSIFIED fails closed** — an unclassified group is skipped by Phase 1, blocks
  Phase 2, and is only resolvable by the future Release 5 classification tool. No inference,
  no backfill.
- **COMPLETED is terminal** — no automatic reopening exists anywhere; post-completion
  correction is a future explicit workflow. `CompletionCycleId` is assigned exactly once.
- **One completion writer** — under `CompletionEnabled=true`, grouped classified requests
  complete exclusively through `RequestCompletionService.EvaluateParentCompletionAsync`
  (legacy exceptions: groupless `FinalizeRequest`; zero-group not-quoted auto-close). The
  LineItems shortcut delegates; aggregation defers and may only reaffirm.
- **No migration in the whole of Release 4 Phases 4A–4E** — the Release 1 schema foundation
  carried the entire lifecycle.
- **Recovery sweep** — `admin/release4/parent-completion-sweep` (preview Finance/SysAdmin;
  apply SysAdmin + reason, fails closed while the lifecycle is off) re-invokes the
  authoritative Phase 2 for requests whose groups all completed but whose parent transition
  was lost; it never writes COMPLETED directly and never repairs facts.
- **Phase 5 remains OCR/document intelligence** (`OperationInvoiceLine`, UPLOADED intake) —
  untouched by Release 4.

Architecture report approved on 2026-08-14 with all eight business decisions resolved:

1. **Conditional fiscal receipt** — `FiscalReceiptStateDeriver`/completability honor
   `RequiresSeparateFiscalReceipt`: a group classified as Factura-Recibo (or any future
   no-separate-receipt identity) owes no separate Recibo Fiscal and must never wait for one.
   The persisted classification result is authoritative; never re-inferred from
   `SourceDocumentType` at evaluation time.
2. **GROUP_COMPLETED identity** — `GC:{GroupId}:{FiscalReceiptAttachmentId}` when a receipt is
   owed; `GC:{GroupId}:NOFR` when none is. Never an empty GUID, never a timestamp.
3. **Payment predicate** — per group: no owed-money `RequestPayment` (ADVANCE/FINAL_BALANCE/
   REGULARIZATION) still PLANNED/SCHEDULED (request-level rows block every group); no active
   `RequestReconciliation` (request- or group-level); `RequiresAdvanceRegularization` demands a
   COMPLETED reconciliation; the group must have reached the actually-paid stage (PAID and
   PAYMENT_COMPLETED equivalent; WAITING_RECONCILIATION is NOT a paid stage). SCHEDULED is
   never paid. `Request.ActualPaidAmount`/request-level status are never the source of truth.
4. **Lazy operational receipt** — pre-activation groups whose item records already prove full
   receipt are stamped by the Phase 1 WRITE path at evaluation time; the physical receiving
   date is never fabricated — the history states the stamp is derived from pre-existing
   receiving records. The pure projection never writes.
5. **Service receipt** — services and materials share the identical item-received requirement;
   no exemption, no separate confirmation flow (there is no material/service field anywhere in
   the model — receipt is item-based and universal).
6. **Completion is terminal** — COMPLETED never reopens automatically; post-completion
   correction is a future explicit workflow. Group completion writes `CompletedAtUtc` once.
7. **Legacy/unclassified fail-closed** — an UNCLASSIFIED (or null-source) group is skipped by
   the evaluation, never thrown on, never inferred over, and blocks the future parent
   completion until the Release 5 classification tool.
8. **Competing writers** — consolidation (legacy `LineItemsController` auto-complete
   suppression, `RequestStatusCalculator` WAITING_FISCAL_RECEIPT priority, trigger wiring)
   deferred to Phase 4C by decision; Phase 4A wires NO production caller.

### Phase 4A — group completion projection + Phase 1 (implemented; no callers)

- **`GroupCompletionProjector`** (`Domain/Services/GroupCompletionProjection.cs`) — the single
  rulebook. Projects, per non-cancelled group: `Classified`, `PoSatisfied` (left
  PENDING/WAITING_PO), `NoBlockingCorrection` (WAITING_PO_CORRECTION independent hard
  blocker), `PaymentSatisfied` (decision 3), `ReceiptSatisfied` (stamp OR
  `OperationalReceiptFacts.AreAllGroupItemsReceived` — the Api receiving helper now delegates
  to this same domain fact), `OperationInvoiceSatisfied`
  (`OperationInvoiceStatuses.IsSatisfied` — NOT_REQUIRED/SATISFIED incl. accepted divergence
  and approved short-close; no duplicate financial calculation), `ClosedShort` (informational),
  `FiscalReceiptRequired`/`FiscalReceiptSatisfied` (decision 1), `Complete`,
  `ReadyForFiscalReceipt`, and ordered `BlockingReasons` codes
  (`GroupCompletionBlockingReasons`) for future ownership labels — codes only, presentation
  text stays out of the domain.
- **`FiscalReceiptStateDeriver`** — new derived state `NOT_REQUIRED` (never persisted);
  UPLOADED still reported honestly whenever an upload exists; unclassified stays LOCKED even
  though the obligation column default is false; `IsGroupCompletable` requires the attachment
  id only when a receipt is owed.
- **`RequestCompletionService.EvaluateGroupCompletionAsync`** (Phase 1) — real since 4A.
  Contract preserved: caller's transaction, no SaveChanges, no own transaction,
  change-tracker-aware loads (coverage-service pattern: Deleted dropped, Added joined),
  `CompletionEnabled=false` → exact no-op with zero queries. Per evaluated group: terminal
  states (CANCELLED/COMPLETED) are strict no-ops; UNCLASSIFIED skipped; lazy receipt stamp +
  `OPERATIONAL_RECEIPT_COMPLETED` (`OR_DONE:{GroupId}`); all-but-owed-receipt →
  `WAITING_FISCAL_RECEIPT` + `FISCAL_RECEIPT_UNLOCKED` (`FR_UNLOCK:{GroupId}`, written only on
  actual change); complete → group `COMPLETED` + `CompletedAtUtc` + `GROUP_COMPLETED` with the
  decision-2 identity. History dedup checks the change tracker AND the database; the filtered
  unique index remains the backstop. Parent request untouched; `ParentEvaluationRequired`
  returned as hint; Phase 2 still guarded (ambient-transaction) and `NotImplementedException`
  until 4C.
- **Not in 4A** (by instruction): fiscal-receipt upload writer, production trigger callers
  (ConfirmReceiving/Finance/OperationInvoice/short-close/PO), parent completion, legacy-writer
  suppression, status-calculator priority, frontend, flag changes, migration (none needed).

### Phase 4B — operational receipt stamping + fiscal receipt upload (implemented)

**Operational receipt (live writer).** `ConfirmReceiving` now stamps
`OperationalReceiptCompletedAtUtc/ByUserId` when the confirmation leaves every group item
RECEIVED (`OperationalReceiptFacts` — the same rulebook as the projection), with the actual
event time and the confirming actor, and writes `OPERATIONAL_RECEIPT_COMPLETED`
(`OR_DONE:{GroupId}`) with normal receipt-completion wording. **Approved flag semantics: the
stamp is a factual dimension record gated by `PostPaymentCompletion.Enabled`, NOT by
`CompletionEnabled`** — under the current TEST configuration (`Enabled=true,
CompletionEnabled=false`) the stamp and its history are written while all Phase-4 transitions
stay dormant; with `Enabled=false` the legacy receiving path is byte-identical (no stamp, no
history). Partial receiving stamps nothing and keeps the IN_FOLLOWUP behavior. A retried
confirmation preserves the original stamp and writes no duplicate. Ownership split: NEW
receiving events → ConfirmReceiving (live wording); pre-activation fully-received groups with
no stamp → the Phase-1 lazy derivation (derivation wording). `OR_DONE` dedup makes double
writing impossible. After the receiving mutation, ConfirmReceiving invokes
`EvaluateGroupCompletionAsync` in the same transaction before SaveChanges — the one approved
4B trigger caller; it is an exact no-op while `CompletionEnabled=false`.

**Fiscal receipt (two-step, atomic binding).**
- Step 1 — file storage: the standard attachment upload now supports
  `TYPE_FISCAL_RECEIPT` ("Recibo Fiscal") as a narrow case: same post-approval/pre-completion
  window as the completion lifecycle (`OperationInvoiceLifecyclePolicy`), **Finance/SysAdmin
  only** (Requesters/Buyers/Receiving cannot store this type; previously it fell into the
  generic default case). Legacy `TYPE_RECEIPT` untouched (rule R18).
- Step 2 — binding: `POST /api/v1/requests/{requestId}/po-groups/{groupId}/fiscal-receipt`
  (`FiscalReceiptsController`, body `{ attachmentId }`), Finance/SysAdmin only, endpoint
  404-gated by `Enabled`. Guard order: feature gate → scoped 404 → role 403 → request mutation
  window (409 `FISCAL_RECEIPT_REQUEST_STATE`; COMPLETED/REJECTED/CANCELLED/WAITING_PO_CORRECTION
  refuse) → group 404 → **idempotent already-uploaded check** (same attachment → 200, no new
  history, even after the group completed; different attachment → 409
  `FISCAL_RECEIPT_ALREADY_UPLOADED`, no replacement flow in 4B) → group terminal/correction
  states (409) → 409 `FISCAL_RECEIPT_NOT_REQUIRED` when `RequiresSeparateFiscalReceipt=false`
  ("Este grupo não exige Recibo Fiscal separado.") → deriver must read PENDING else 409
  `FISCAL_RECEIPT_LOCKED` (detail lists the pending dimensions via `PostPaymentPendingReason`;
  unclassified is LOCKED) → attachment integrity (exists on THIS request, not deleted, typed
  `FISCAL_RECEIPT`, not already another group's receipt) else 409
  `FISCAL_RECEIPT_ATTACHMENT_INVALID`.
- On success, ONE SaveChanges persists: the binding
  (`FiscalReceiptAttachmentId/UploadedAtUtc/UploadedByUserId`), `FISCAL_RECEIPT_UPLOADED`
  history (`FR_UP:{GroupId}:{AttachmentId}`, group + file + supplier identified) and the Phase-1
  evaluation — a `WAITING_FISCAL_RECEIPT` group completes here (`GROUP_COMPLETED`,
  `GC:{GroupId}:{AttachmentId}`). No partial state is representable. With
  `CompletionEnabled=false` the binding is stored and audited as a dimension fact and no
  transition runs. The fiscal-receipt STATE remains derived (`FiscalReceiptStateDeriver`),
  never persisted.

**Known 4C consolidation item (recorded, not fixed in 4B by instruction):** once
`CompletionEnabled=true`, ConfirmReceiving's post-save `AggregateRequestStatusAsync` could
propagate an all-groups-COMPLETED reading to the parent request without history (aggregation
back door), and `LineItemsController.UpdateStatus` retains its legacy auto-complete. Both are
exactly the competing-writer consolidations Phase 4C performs before the flag ever turns on;
`WAITING_FISCAL_RECEIPT` also still needs its `RequestStatusCalculator` priority (95) in 4C.

**Not in 4B**: parent completion (Phase 2 dormant, ambient guard intact), the remaining
trigger callers (payment/invoice/short-close/PO), legacy-writer suppression, calculator
priority, frontend, flag changes, migration (none needed).

### Phase 4C — parent completion + trigger matrix + writer consolidation (implemented)

**Phase 2 is real** (`EvaluateParentCompletionAsync`): exact no-op while
`Enabled && CompletionEnabled` is false; ambient-transaction guard unchanged; own short
transaction over FRESHLY reloaded state (`ReloadAsync` + `AsNoTracking` group/reconciliation
reads — never the caller's stale tracker). Blockers, in order: request not found (error
result) → already COMPLETED (AlreadyCompleted + the persisted `CompletionCycleId`, nothing
new ever written — COMPLETED is terminal) → rejected/cancelled → zero groups (groupless
legacy flow owns completion) → all groups cancelled → any UNCLASSIFIED/null-source group
(R15 fail-closed; Release 5 owns classification) → any non-cancelled group not COMPLETED
(the Phase-1 commitment is trusted — obligations are never re-derived in Phase 2) → ANY
active reconciliation of the request, explicitly including null-group request-level rows.
The winning transition assigns `CompletionCycleId` exactly once, sets COMPLETED, writes
`REQUEST_COMPLETED` (`RC:{RequestId}:{CompletionCycleId}`, group count + cycle in the
comment) in ONE SaveChanges/commit, then emits `RequestFinalized` post-commit with
`CorrelationId = CompletionCycleId` (non-critical). **Concurrency**: retry-once on
`DbUpdateConcurrencyException`; the retry reloads committed state and returns
AlreadyCompleted with the winner's cycle id (never a second identity); a second consecutive
conflict returns `ConflictUnresolved` (logged; the dimension action is never rolled back —
the next trigger or the sweep recovers).

**Authoritative COMPLETED writer rule** — after 4C the only first-writers of
`Request.StatusId = COMPLETED` are: (A) `RequestCompletionService` for grouped classified
requests under `CompletionEnabled=true`; (B) legacy `FinalizeRequest` for groupless requests
(and for grouped requests only while `CompletionEnabled=false` — the Phase 3B window); (C)
the not-quoted auto-close paths (zero active groups/batches, invariants unchanged).
Consolidations: `LineItemsController.UpdateStatus` no longer completes a grouped request
while completion is on — it delegates to Phase 1 (item's group; lazy receipt stamp through
the shared engine) + post-save Phase 2; legacy behaviour byte-identical when the flag is off
or the request is groupless. `StatusAggregationService` defers a calculated COMPLETED for a
grouped request while completion is on (it may reaffirm an already-completed request, never
be first); legacy behaviour preserved when off. `RequestStatusCalculator` gains
`WAITING_FISCAL_RECEIPT = 95` (no collision; WAITING_RECEIPT 70 stays further behind,
COMPLETED 100 still wins). `FinalizeRequest` unchanged — its dormant redirect (grouped +
classified + completion on → 400 "Fluxo Atualizado") simply becomes live at activation.

**Trigger matrix wired** (Phase 1 inside the dimension transaction/save; Phase 2 strictly
post-commit, never failing the user action):

| Path | Phase 1 scope | Notes |
|---|---|---|
| ConfirmReceiving (4B) | group | Phase 2 added post-commit |
| Fiscal receipt binding (4B) | group | Phase 2 added post-save |
| Invoice Validate | all request groups | THE effective-coverage event (incl. accepted divergence) |
| Invoice Reject / Void | all request groups | pending-coverage retirement |
| Invoice Replace | all request groups | uniformity; replacement is blocked on downstream evidence |
| Short-close APPROVE | group | SATISFIED via ClosedShort |
| Short-close REJECT/withdraw | — NOT wired | a PROPOSED short-close contributes nothing to effective coverage; nothing can change |
| MarkAsPaid | group | actual payment only — SCHEDULED is never paid |
| ConfirmAdvancePayment | group | advance actually paid |
| ReconcileRequest | all request groups | COMPLETED reconciliation may discharge regularization; a created FINAL_BALANCE is a NEW tracked blocker Phase 1 observes before the save |
| RegisterPo (incl. corrected-P.O.) | all request groups | projector decides; a P.O. alone never completes |
| Allocation draft changes | — NOT wired | pending-only, never effective |

Wiring is inert while `CompletionEnabled=false` (the service self-gates with zero queries);
controller dependencies are optional-by-default (DI always supplies them in production) so
every existing direct construction keeps its exact legacy behaviour.

**Recovery sweep** — `GET/POST /api/v1/admin/release4/parent-completion-sweep/{preview|apply}`
(`ParentCompletionSweepController`). Preview (Finance/SysAdmin, ungated dry-run — consistent
with the expected-total activation tool): open, non-cancelled, grouped requests whose
non-cancelled groups are ALL COMPLETED, with skip reasons (`UNCLASSIFIED_GROUP`,
`ACTIVE_RECONCILIATION`). Apply (SysAdmin only, mandatory meaningful reason): fails closed
with 409 `COMPLETION_DISABLED` while `Enabled && CompletionEnabled` is not true — the sweep
never implicitly activates Phase 4 — and recovers exclusively by invoking
`EvaluateParentCompletionAsync` (never a direct COMPLETED write; every service guard applies
identically). Idempotent: recovered requests stop being candidates. The sweep never
completes UNCLASSIFIED, never fixes obligations, never fabricates facts, never bypasses
reconciliation, never touches groupless requests.

**Not in 4C**: frontend (Phase 4D), flag changes (TEST stays `Enabled=true,
CompletionEnabled=false`), version bump (Phase 4 RC closure), migration (none needed),
post-completion reopen workflow, Phase 5.

### Phase 4D — completion readiness UI + fiscal receipt UX (implemented)

**Read model** — `GET /api/v1/requests/{id}/completion-readiness`
(`CompletionReadinessDto`): a SIBLING of the obligations endpoint by design (the obligations
DTO is the coverage/allocation workspace; readiness answers "may this complete and what is
missing" — merging would bury the lifecycle booleans in an already-large financial payload;
the UI joins the two by group id). Normal request visibility (never Finance-only); 404-gated
by `Enabled`; honest under `CompletionEnabled=false` with `completionLifecycleEnabled`
carried in the payload. Request level: readiness/completed flags, RC-event instant, active
reconciliation, group counts. Group level: the ten projection booleans, PO/supplier/plant
identity, `CompletedAtUtc`, ordered `blockingReasons[{code, ownerCode}]` (ownership assigned
by the new domain `GroupCompletionOwnership` map — approved: classification →
Finance/Admin, PO family → Buyer, payment/reconciliation/invoice/fiscal receipt → Finance,
receipt → Receiving), and the fiscal-receipt evidence summary (file, instant, uploader).
`CompletionCycleId` is deliberately NOT exposed (internal idempotency identity).

**UI** — new "Conclusão do Pedido" section (`RequestCompletionSection`) rendered directly
below "Fatura Final — Cobertura" in the request detail (and therefore in the Finance drawer,
which hosts the same detail). One compact card per group: checklist P.O. · Pagamento ·
Recebimento · Fatura Final · Recibo Fiscal with states ✓/○/—/⚠ (no-separate-receipt groups
show "Não aplicável", never "missing"); "O que falta" lines as Portuguese business phrases
with ownership ("Aguardando pagamento — Financeiro"); Phase 3 evidence reused verbatim
("Encerrado com Saldo Aceite" from the projection, "Divergência Aceite: +valor" from the
obligations invariant helper); UNCLASSIFIED legacy groups show "Classificação pendente —
Financeiro / Administração" with the explanatory sentence and NO fake fix (Release 5 owns
classification). Completed groups show "Grupo Concluído" + instant with the checklist kept
visible for audit; a completed request shows "Pedido Concluído" + the RC instant and no
mutation actions. Multi-group headers summarize "N de M grupos concluídos"; readiness is
always the SERVER's verdict, never a client-side card count. **There is deliberately no
"Concluir Pedido" button** — completion is automatic through the backend engine.

**CompletionEnabled=false presentation rule (approved)** — a fully satisfied request shows
"Requisitos de conclusão satisfeitos" with the calm note "O ciclo automático de conclusão
ainda não está ativo neste ambiente."; "Pronto para concluir" appears only when the read
model says the lifecycle is active. After a fiscal-receipt upload with the lifecycle off,
the dimension shows satisfied honestly and the modal says the group's closure follows the
completion cycle — never "Grupo concluído" from UI booleans alone.

**Fiscal receipt UX** — "Registrar Recibo Fiscal" CTA appears only for Finance/SysAdmin,
only when the read model says the receipt is required, unsatisfied, and the ONLY remaining
blocker (the backend deriver refuses anything else). `FiscalReceiptModal`: group context
(supplier, P.O., plant, invoice/receipt state incl. short-close evidence), the explanatory
sentence "O Recibo Fiscal confirma documentalmente o encerramento fiscal deste grupo.", file
upload (stored as `TYPE_FISCAL_RECEIPT` via the standard attachment pipeline, group-linked),
binding through the Phase 4B endpoint, then readiness refresh with the honest result state.
Uploaded receipts show file/date/uploader with the standard download action and NO replace
action. Structured errors mapped to Portuguese (`FISCAL_RECEIPT_*`, `COMPLETION_DISABLED`);
the binding endpoint now returns 409 `FISCAL_RECEIPT_CONCURRENCY` on a RowVersion race,
which the UI presents with "Recarregar dados" and never auto-resubmits.

**WAITING_FISCAL_RECEIPT presentation** — the seeded lookup name "Aguardando Recibo Fiscal"
(badge color included) feeds every list/badge that renders status names; `utils.ts` gains
the responsible/next-action entry (Financeiro → registar o Recibo Fiscal). No raw enum
reaches the user.

**Not in 4D**: Phase 4E RC closure/bump, flag changes, migration (none), OCR, dedicated
Finance workspace, manual completion, fiscal-receipt replacement.

## Phase 3A — Allocation, reconciliation & short-close (backend only, flag off)

Backend activation of the dormant Phase 3 entities. **No UI (Phase 3B), no completion wiring
(Phase 4), no OCR (Phase 5); `PostPaymentCompletion.Enabled` stays `false` everywhere.** While
UNCLASSIFIED groups fail eligibility, the endpoints are functionally inert on legacy data.

### Feature-flag split (Phase 3A checkpoint)

The single flag was too broad: enabling it would have opened the coverage capability AND
redirected every grouped request into a Phase 4 completion path that does not exist yet. The
`PostPaymentCompletion` section therefore carries two switches:

| Switch | Governs | Phase 3B TEST | Phase 4 | Committed default |
|---|---|---|---|---|
| `Enabled` | intake/classification, multi-source-document enforcement, group obligation stamping + `ExpectedOperationInvoiceTotal` capture, obligations read model, R15 unclassified guard, frontend capability discovery | **true** | true | **false** |
| `CompletionEnabled` | legacy-FinalizeRequest redirect into the new completion path, `RequestCompletionService` evaluation (Phase 4 lifecycle) | **false** | true | **false** |

Effective completion is `Enabled && CompletionEnabled`
(`PostPaymentCompletionPolicy.IsCompletionDisabled`) — `CompletionEnabled` alone fails closed.
During the Phase 3B window (`Enabled=true, CompletionEnabled=false`): new groups are classified
with expected totals captured, the obligations/coverage read model is reachable, the allocation
flow works, the R15 guard blocks finalizing unclassified grouped requests, **and a classified
grouped request still finalizes through the legacy path** — the completion redirect and the
Phase 4 lifecycle stay dormant (`RequestCompletionService` is a no-op, never
`NotImplementedException`). The frontend reads both states from `/api/v1/config/features`
(`PostPaymentCompletionEnabled`, `CompletionLifecycleEnabled`).

### Allocation lifecycle (draft-then-count)

`PUT /api/v1/requests/{id}/operation-invoices/{oid}/allocations` — an **atomic replace-set**:
the payload IS the resulting set; the server derives add/update/remove, validates the ENTIRE
result before mutating anything, and commits with one SaveChanges. N:M with **one row per
(invoice, group)** — enforced by payload validation and the pre-existing unique index. Rules,
in gate order:

1. **Drafting window**: only while the invoice is `UPLOADED`/`PENDING_VALIDATION`
   (`OI_ALLOC_NOT_EDITABLE` otherwise). VALIDATED allocations are immutable — correct via
   reject/replace. Roles: Buyer/Finance/SysAdmin, same mutation window as every invoice write.
2. **Group integrity** (`OI_ALLOC_GROUP_INVALID` / `OI_ALLOC_SUPPLIER_MISMATCH` /
   `OI_ALLOC_CURRENCY_MISMATCH`): the group must belong to the same request, be classified with
   `RequiresOperationInvoice`, be in an upload-accepting aggregate state, not be parked in
   `WAITING_PO_CORRECTION`, and match the invoice's supplier and currency (no FX).
3. **Invoice-side balance** (`OI_ALLOC_INVOICE_OVER`): Σ allocations ≤ invoice gross +
   tolerance (`max(1.00, 0.1%)` — the Portal's standard financial tolerance).
4. **Over-expected rule (approved decision #13)**: an allocation that pushes
   `validated-by-others + allocated` beyond the group's expected total + tolerance is **hard-
   blocked for the Buyer** (`OI_ALLOC_GROUP_OVER`, with expected/current/attempted/tolerance in
   the problem payload) and is a **divergence candidate for Finance/SysAdmin** — allowed only
   with a meaningful explanation (≥20 chars, placeholder-rejected) in the allocation `Notes`.
   Nothing is ever silently capped; the acceptance decision itself belongs to validation.

An identical payload is an **idempotent no-op** (no audit row, no touch). Sequence numbers are
per-group ("the Nth allocation this group ever received"). Audit: `OI_ALLOC_SET` on first set,
`OI_ALLOC_CHANGED` after, with per-group amounts and "anterior X" on changes.

**Supplier/Currency integrity is enforced twice** (Phase 3A checkpoint corrective patch): once
when the allocation is created/updated (the rules above), and again whenever the invoice
HEADER changes or validates — an Update whose merged Supplier/Currency would contradict an
existing allocation is refused with the SAME codes (`OI_ALLOC_SUPPLIER_MISMATCH` /
`OI_ALLOC_CURRENCY_MISMATCH`; header-drift guard), and Validate re-runs the identical recheck
against the persisted header before any snapshot or status mutation, protecting against
historical rows, manual database drift and any path that bypasses Update. The reconciliation
snapshot's `SupplierMatched/CurrencyMatched/CompanyMatched` remain evidence of a valid
comparison, never a bypass mechanism. Company integrity is structurally inherited from the
shared request scope (one company per request — no duplicate comparison); the invoice carries
**no plant identity** by design, so no plant rule exists and a request-scoped invoice may
legitimately allocate across several plants' groups.

### Effective coverage rule and re-derivation

**Only allocations on VALIDATED, non-superseded invoices count toward coverage**; drafts count
toward pending. `OperationInvoiceCoverageService.RederiveAsync` recomputes the cached
`RequestPoGroup.OperationInvoiceStatus` through `OperationInvoiceAggregateDeriver` (single
aggregate policy) **inside the caller's transaction** for every allocation/validation/
rejection/void/short-close write, reading the caller's in-transaction state through the change
tracker. Transitions are audited as `GROUP_OI_STATUS` rows. Coverage totals are **never
persisted** — always derived. Concurrency: effective-coverage writes (validation, short-close
approval) force a RowVersion-checked touch on every affected group even when the derived status
is unchanged, so two racing effective-coverage writers can never both commit against the same
stale reading; draft writes rely on the invoice RowVersion alone.

### Validation gate, divergence acceptance and the reconciliation snapshot

Finance validation now validates a **fully-attributed document**: Σ allocations must equal the
invoice gross within tolerance (`OI_VALIDATE_ALLOCATION_INCOMPLETE`) — an unallocated invoice
can no longer be validated. Every group the validation would push beyond its expected total +
tolerance requires an **explicit acceptance entry** (`DivergenceAcceptances[]`, `Accepted=true`
+ meaningful justification) or the validation fails with `OI_VALIDATE_DIVERGENCE_REQUIRED` and
the full numeric context. Never inferred, never auto-accepted, never capped.

On the VALIDATED transition, one **immutable `OperationInvoiceReconciliation` snapshot per
allocation** records the moment of the decision: NIF/supplier/currency/company matches (facts,
not gates — mismatches are recorded honestly, the decision was Finance's), allocated total,
cumulative validated-before, expected-at-comparison, baseline, invoice total, signed residual
variance, applied tolerance, and the divergence decision with its justification. Snapshots are
never updated; the idempotent VALIDATED early-return precedes the write, so an exact retry can
never duplicate one.

### Expected-total activation (audited backfill)

`/api/v1/admin/release4/expected-operation-invoice-totals` — the controlled exception to the
"no historical backfill" rule, for turning the feature on over pre-flag data. **GET preview**
(Finance/SysAdmin, dry-run) lists every group whose expected total is null, split into eligible
(classified + `RequiresOperationInvoice` + `TotalAmount > 0` → proposed = `TotalAmount`) and
skipped (`NOT_CLASSIFIED` / `NOT_REQUIRED` / `NO_TOTAL`). **POST apply** (SysAdmin only,
mandatory meaningful reason) freezes `TotalAmount` as the expected total with the manual-set
audit trio and justification `[ATIVAÇÃO R4] {reason}`, re-derives, and writes one
`OI_EXPECTED_TOTAL_BACKFILLED` history row per touched request. **Never overwrites** a non-null
expected total; structurally idempotent (a written group stops being a candidate).

### Short-close policy (separation of duties)

`/api/v1/requests/{id}/po-groups/{gid}/operation-invoice-short-close` — the audited two-person
decision that a group will legitimately never reach its expected total. **Propose**
(Buyer/Finance/SysAdmin): eligible obligation, remaining = expected − validated > tolerance,
meaningful justification, optional evidence attachment (must belong to the request);
`RemainingAmountAtProposal` is frozen. One ACTIVE (PROPOSED/APPROVED) short-close per group —
precheck plus the filtered unique index as concurrency backstop. **Approve** (Finance/SysAdmin,
**proposer ≠ approver** structurally — a SysAdmin cannot approve their own proposal): re-derives
the group to SATISFIED/`ClosedShort` in the same transaction. **Reject** (Finance/SysAdmin, or
**the proposer themself as the withdrawal path** — the model has no separate cancellation
state; a self-rejection with its mandatory reason is the recorded withdrawal). Decisions are
terminal; retries are idempotent; events `OI_SHORTCLOSE_PROPOSED/APPROVED/REJECTED`.

### Obligations endpoint additions

Each obligation now carries `CoveragePercent` (validated/expected × 100, `null` when the
expected total is unknown or not positive — never 0) and `Allocations[]` (the group-side view of
the same rows: invoice number/series/status, amounts, sequence, notes, `IsEffective`,
`IsPendingDecision`, audit timestamps). The endpoint remains flag-gated (404 while disabled)
and strictly read-only.

### Phase 4/5 boundaries (not in 3A)

- **Phase 4 (completion)**: group/request completion transitions, fiscal receipts, operational
  receipt UI. Approved business decision recorded for Phase 4 planning: **operational receipt is
  required for service-type groups too** (documentation only; no code in 3A).
- **Phase 5 (OCR)**: `OperationInvoiceLine`, line matching, `UPLOADED` intake. Dormant.
- Phase 3B (allocation/validation/short-close UI) follows separately; nothing in 3A renders.

## Known open questions for Phase 2/3 (Finance)

Carried from plan v7 §9, plus Release 4 findings:

1. Upload roles and permitted workflow statuses for operation invoices; Finance validation flow.
2. `OperationInvoice.DueDate` — needed? (schema addition if so).
3. Over-invoicing tolerance policy (proposal recorded: prevent over-allocation at write time,
   subject to a Finance-approved tolerance; no OVER status exists).
4. Currency: proposal — the invoice currency must match every allocated group's currency; no FX.
5. Cross-request supplier consolidation (the model is request-scoped by design).
6. P.O. cardinality: confirm 1 group = 1 P.O. before Phase 3.
7. Mixed-document regrouping: keep the block, or define a Finance-driven regroup flow.
8. ~~Supplier/plant/currency edits on a grouped document~~ — closed by Phase 1d: the full
   grouping key is guarded by the same planner and transaction.
