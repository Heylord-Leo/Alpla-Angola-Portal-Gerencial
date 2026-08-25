# Buyer DEV Regression Harness (ZZTEST-BUY-*)

> Official, permanent **DEV-ONLY** maintenance tool for the canonical Buyer queue
> (`GET /api/v1/buyer/queue`) and the `BuyerQueueProjectionBuilder`. It seeds / inspects / resets
> deterministic synthetic QUOTATION scenarios in the disposable DEV prod-clone so every Buyer
> operational state can be exercised end-to-end **without touching any historical (real) request**.
>
> Sibling of the Finance harness — see [FINANCE_DEV_REGRESSION_HARNESS.md](./FINANCE_DEV_REGRESSION_HARNESS.md).
> Canonical model reference: [BUYER_QUEUE_CANONICAL_MODEL.md](./BUYER_QUEUE_CANONICAL_MODEL.md).

## Why it exists

The Buyer queue derives eight operational states, a coverage taxonomy, priority/deadline bands,
attention signals, ownership and cancel capability — all server-side. Regressions are easy to
introduce and hard to see on production data (which is mostly a single state). This harness plants
one clean request per state so a reviewer (or a future refactor) can confirm the projection and the
endpoint still classify each scenario correctly.

It **never runs production business logic itself**. It only plants persisted rows; the queue
projection under test is exercised through the **real** `GET /api/v1/buyer/queue` endpoint over HTTP.

## Defense-in-depth (never reachable in TEST/PROD)

Three independent gates; failing any one makes **every** endpoint return `404`:

| Gate | Mechanism |
|------|-----------|
| **A — compile-time** | The whole controller is inside `#if DEBUG` (a `NotFound` stub ships in Release). |
| **B — runtime env** | `IWebHostEnvironment.IsDevelopment()` must be `true`. |
| **C — explicit opt-in** | `DevFixtures:BuyerEnabled` must be `true` — set only in local, git-ignored `appsettings.Development.json`. |

```jsonc
// appsettings.Development.json (LOCAL ONLY — never commit)
{
  "DevFixtures": { "BuyerEnabled": true }
}
```

## Fixture identity & isolation

- `RequestNumber` starts with **`ZZTEST-BUY-`** **and** `Title` starts with **`[ZZTEST-BUY]`**.
- `reset` (and the reset run at the start of `seed`) removes **only** those synthetic rows and their
  children (candidates → batch items → batches; quotation items → quotations; history; attachments;
  line items; requests). It is never a generic database-mutation API and never targets real requests.

## Endpoints

`Route: api/v1/dev/buyer-fixtures`

| Method | Path | Purpose |
|--------|------|---------|
| `GET`  | `/token` | Mint a `System Administrator` + `Buyer` JWT (global scope + self-claim), attributed to a real active admin user for FK-valid audit. |
| `GET`  | `/state` | Dump the current ZZTEST-BUY-* requests: status, buyer, need level/date, items (lifecycle), batches, quotations. |
| `POST` | `/seed`  | Reset then plant the scenario set below. Returns the scenario→requestId map with each `expectedState`. |
| `POST` | `/reset` | Remove all ZZTEST-BUY-* fixtures and children. |

## Seeded scenarios

| Key | Request status | Items | Extras | Expected operational state |
|-----|----------------|-------|--------|----------------------------|
| **B1** | WAITING_QUOTATION | 2 pending | unassigned, no deadline | `NEEDS_QUOTATION` |
| **B2** | WAITING_QUOTATION | 1 pending + MAPPED quotation candidate | assigned | `READY_FOR_APPROVAL` |
| **B3** | WAITING_QUOTATION | 1 approved + 1 pending | assigned, URGENTE | `PARTIAL_COVERAGE` |
| **B4** | WAITING_AREA_APPROVAL | 1 BATCH_ASSIGNED | WAITING_AREA_APPROVAL batch | `AWAITING_APPROVAL` |
| **B5** | AREA_ADJUSTMENT | 1 BATCH_ASSIGNED | AREA_ADJUSTMENT batch, **overdue**, CRITICO | `ADJUSTMENT_REQUIRED` (Band 1, BLOCKING) |
| **B6** | WAITING_QUOTATION | 1 NOT_QUOTED_PROPOSED + 1 approved | assigned | `AWAITING_REQUESTER_DECISION` |
| **B7** | WAITING_AREA_APPROVAL | 2 approved | assigned | `COMPLETED_FOR_BUYER` (hidden unless `includeCompleted=true`) |
| **B8** | WAITING_QUOTATION | 1 pending | unassigned, **overdue**, CRITICO | `NEEDS_QUOTATION` (Band 1 + `UNASSIGNED_NEAR_DEADLINE`) |
| **B9** | WAITING_QUOTATION | 1 pending + MAPPED quotation | assigned; **selected** quotation from a real ZZTEST-BUY supplier + 2 issued POs (AOA + EUR) | `READY_FOR_APPROVAL` — Workspace supplier-carousel scenario (per-currency totals, never summed) |

## Typical acceptance loop

```bash
BASE=http://localhost:5251/api/v1
TOKEN=$(curl -s $BASE/dev/buyer-fixtures/token | jq -r .token)
curl -s -X POST $BASE/dev/buyer-fixtures/seed -H "Authorization: Bearer $TOKEN" | jq .

# Active queue (completed hidden): expect B1..B6 + B8 (7 rows), never B7.
curl -s "$BASE/buyer/queue?ownership=all&pageSize=50" -H "Authorization: Bearer $TOKEN" \
  | jq '.totalCount, [.items[] | {n:.requestNumber, state:.operationalState, band:.priorityBand, attn:.requiresAttention}]'

# Summary cards.
curl -s "$BASE/buyer/queue/summary" -H "Authorization: Bearer $TOKEN" | jq .

# Only unassigned: expect B1 + B8.
curl -s "$BASE/buyer/queue?ownership=unassigned" -H "Authorization: Bearer $TOKEN" | jq '[.items[].requestNumber]'

# Include completed: B7 appears.
curl -s "$BASE/buyer/queue?includeCompleted=true&pageSize=50" -H "Authorization: Bearer $TOKEN" | jq '[.items[].requestNumber]'

curl -s -X POST $BASE/dev/buyer-fixtures/reset -H "Authorization: Bearer $TOKEN" | jq .
```

## MAINTENANCE TRIGGERS — keep this harness in lock-step

Update the scenarios / this doc whenever any of the following change:

- `BuyerQueueProjectionBuilder` — operational-state precedence, coverage buckets/status, priority
  bands, deadline conditions, attention signals, ownership, or cancel evaluation.
- `BuyerQueueConstants` — new/renamed states, buckets, action codes, or `ApproachingDeadlineDays`.
- `RequestCancellationEvaluator` — cancellation rules (shared with `CancelRequest`).
- The queue endpoint contract (`BuyerQueueController`) — filters, pagination, summary shape, or DTOs.
- Entity shape used by the builder (`RequestLineItem.QuotationLifecycleStatus`, `ApprovalBatch`/
  `ApprovalBatchItem`/`Candidates`, `QuotationItem` reconciliation, superseded-batch policy).

If a new operational state or coverage bucket is added, add a seeded scenario that produces it.
