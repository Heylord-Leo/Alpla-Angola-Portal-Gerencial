# Post-Payment Completion — Release 4: Operation Invoice (Phase 1)

> Status: **Phase 1 (a+b+c+d) complete and approved — closed at v2.225.0 on `Portal-Gerencial-rev1`.**
> OperationInvoice CRUD, allocation writes, UI and OCR do **not** exist yet — they are Phases 2+.

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
