# Dashboard V2 — B9 Stage Aging Policy (Pipeline vs Aging)

> Status: DEV (uncommitted campaign). Live capture: PO_GROUP + APPROVAL_BATCH (B9.2). Buyer/REQUEST grain
> **formally OUT OF SCOPE** for this release (B9.2d — see "Buyer scope closure" below). No backfill, no
> endpoint, no frontend yet.

## Buyer scope closure (B9.2d — CLOSED PRODUCT DECISION)

**Buyer / REQUEST aging is OUT OF SCOPE for the current B9 release.** The active B9 aging grains are
**APPROVAL_BATCH** and **PO_GROUP** only.

- **Architectural reason:** the canonical Buyer operational state
  (`NEEDS_QUOTATION`/`PARTIAL_COVERAGE`/`READY_FOR_APPROVAL`) is a projection over LineItems + all active
  ApprovalBatches' item candidates + the full request Quotations→Items graph. The complete B9.2c write-path
  audit found that **no** buyer-state-changing write path already loads all three sets, so live capture
  would require adding a heavy request-level graph load at each of ~15 write points **solely for B9** — the
  invasive read this campaign forbids. No such hydrate was introduced, and **no** new Request coverage-state
  field was persisted.
- **Buyer stays canonical elsewhere:** B6 Pipeline shows the Buyer populations; B8 Alerts carry NeedByDate
  urgency while a buyer action is open; the Buyer queue / "Minha Operação" carry actionable workload.
- **Schema is future-facing, not removed:** the generic `OperationalStageState`/`OperationalStageTransition`
  model keeps full REQUEST support, and `CanonicalOperationalStageResolver.ResolveBuyerStage` is kept dormant
  and tested (marked FUTURE / NOT ACTIVE). B9 can add Buyer aging in a future release **if** the Buyer domain
  gains a cheaper authoritative transition source. Until then, B9 capture never calls it and the read side
  never expects Buyer REQUEST snapshots.
- **OUT-OF-SCOPE ≠ UNKNOWN:** Buyer entities simply do not participate in B9 rows/counts. `StageEnteredAtUtc
  = null` / `UnknownAgeEntityCount` / "Idade não disponível" apply **only** to IN-SCOPE (APPROVAL_BATCH /
  PO_GROUP) entities whose current stage is known but whose historic entry time cannot be proven — never to
  Buyer.

## The core distinction

B6 **Operational Pipeline** and B9 **Stage Aging** answer *different questions* and therefore use the same
stage vocabulary with **different cardinality rules**.

| | B6 Pipeline (`OperationalPipelineProjection`) | B9 Aging (`OperationalStageState`) |
|---|---|---|
| Question | "What operational populations exist, and where?" | "Where is each entity currently accumulating dwell time?" |
| Cardinality | **May overlap** — one entity can appear in several stages at once (`CanOverlap = true`) | **Exclusive** — exactly ONE active stage per entity (unique `EntityType+EntityId`) |
| Purpose | Informational counts | A single dwell clock per entity |

Because of this, **B6 and B9 do not map 1:1 for overlap stages.** The stage *codes* are shared (both come
from `PipelineStages`), but B9 returns the single **exclusive dwell owner**, and **not every `PipelineStages`
code is an active aging stage** (`FIN_PAID`, `DRAFT`, `COMPLETED` are B6-only).

## The canonical overlap case: paid, awaiting receiving

When a PO group's payment is **completed** (`PoGroupStatuses.PAYMENT_COMPLETED`):

- **B6 Pipeline** may INFORMATIONALLY show the group in **both** `FIN_PAID` (Finanças — "pago / aguardando
  recebimento") **and** `REC_READY` (Recebimento — ready to move to receipt). This overlap is intentional.
- **B9 Aging** shows **`REC_READY` only.** Finance's action is finished, and the group is *immediately*
  Receiving-actionable (`ReceivingActionEvaluator`: `PAYMENT_COMPLETED` → `MoveToReceipt` available →
  bucket `READY_FOR_RECEIPT`). The dwell clock therefore belongs to **Receiving**, and `FIN_PAID` is never a
  B9 active aging stage.

There is **no** source status that rests in "paid but not yet receiving-actionable", so no Finance aging
period is lost by this rule (verified against `ReceivingActionEvaluator.ActionableBucket`).

## Aging-stage mapping matrix (exclusive owner)

| Domain state (persisted status) | B6 Pipeline stage(s) | B9 Aging stage | Reason |
|---|---|---|---|
| Buyer: needs quotation | `NEEDS_QUOTATION` | `NEEDS_QUOTATION` | identical (exclusive) |
| Buyer: partial coverage | `PARTIAL_COVERAGE` | `PARTIAL_COVERAGE` | identical |
| Buyer: ready for approval | `READY_FOR_APPROVAL` | `READY_FOR_APPROVAL` | identical |
| Batch `WAITING_AREA_APPROVAL` | `AREA_APPROVAL` | `AREA_APPROVAL` | identical |
| Batch `WAITING_FINAL_APPROVAL` | `FINAL_APPROVAL` | `FINAL_APPROVAL` | identical |
| Batch `AREA_ADJUSTMENT` / `FINAL_ADJUSTMENT` | `ADJUSTMENT` | `ADJUSTMENT` | identical |
| Group `WAITING_PO` | `PO_WAITING` | `PO_WAITING` | identical |
| Group `WAITING_PO_CORRECTION` | `PO_CORRECTION` | `PO_CORRECTION` | identical |
| Group `PO_ISSUED` / `PAYMENT_REQUEST_SENT` / `ADVANCE_PAYMENT_REQUIRED` | `FIN_NEEDS_SCHEDULING` | `FIN_NEEDS_SCHEDULING` | identical |
| Group `PAYMENT_SCHEDULED` / `ADVANCE_PAYMENT_SCHEDULED` | `FIN_SCHEDULED` | `FIN_SCHEDULED` | identical |
| Group `PAYMENT_COMPLETED` | `FIN_PAID` **+** `REC_READY` (overlap) | **`REC_READY`** | Finance done; Receiving actionable → dwell owner = Receiving |
| Group `WAITING_RECEIPT` | `REC_WAITING` | `REC_WAITING` | identical |
| Group `IN_FOLLOWUP` | `REC_FOLLOWUP` | `REC_FOLLOWUP` | identical |
| Group `WAITING_SUPPLIER_DELIVERY` | `REC_SUPPLIER` | `REC_SUPPLIER` | identical |
| Group `ADVANCE_PAYMENT_COMPLETED` | (transient) | *(none)* | transient advance marker; immediately parked at `WAITING_SUPPLIER_DELIVERY` |
| Group `WAITING_FISCAL_RECEIPT` / `WAITING_RECONCILIATION` | `DOCUMENTATION` | `DOCUMENTATION` | identical |
| Group/batch terminal (`COMPLETED` / `CANCELLED` / `REJECTED` / `APPROVED`) | `COMPLETED` (or none) | *(no snapshot)* | terminal → snapshot removed, history event only |

## B6 ↔ B9 reconciliation policy

The earlier "B6 counts == B9 counts for every code" rule is **too strong**. B9 does NOT reconcile every B6
stage; three categories now exist:

- **A. Shared exclusive stages** — reconcile directly; B6 and B9 populations match where semantics are equivalent.
- **B. B6 overlap stages** — reconcile through the explicit mapping above. A `PAYMENT_COMPLETED` group counts
  in B6 `FIN_PAID` **and** `REC_READY`, but in B9 `REC_READY` only. Intentional and tested
  (`CanonicalOperationalStageResolverTests`); must never arise from accidental predicate drift.
- **C. Out-of-scope B6 stages** — the Buyer stages `NEEDS_QUOTATION` / `PARTIAL_COVERAGE` /
  `READY_FOR_APPROVAL` have **no B9 counterpart in this release** (B9.2d scope closure). They remain valid B6
  pipeline codes; B9 produces no aging rows/counts for them. Guarded by `OperationalStageScopePolicyTests`.

## Backfill readiness

B9.3 backfill requires live capture for every **IN-SCOPE** aging grain (not "every possible grain"):

- **APPROVAL_BATCH** — COMPLETE (B9.2).
- **PO_GROUP** — COMPLETE (B9.2, exclusive-aging in B9.2b).
- **REQUEST / Buyer** — OUT OF SCOPE (B9.2d) — not required.

With Buyer formally descoped, both in-scope grains have complete live capture, so **B9.3 backfill is
authorized** (for APPROVAL_BATCH + PO_GROUP only).
