# Post-Payment Completion — Release 4: Operation Invoice (Phase 1)

> Status: **Phase 1 closed at v2.225.0. Phase 2 (a–e) complete and approved — closed at
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

## Phase 4 — Completion lifecycle (architecture approved; 4A implemented)

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
