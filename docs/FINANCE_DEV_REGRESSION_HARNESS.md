# Finance DEV Regression Harness (ZZTEST-FIN-*)

> **DEVELOPMENT-ONLY tooling — NOT product functionality.**
> Controller: `src/backend/AlplaPortal.Api/Controllers/DevFinanceFixtureController.cs`
> (follows the existing `DevSeedingController` convention). Route base: `api/v1/dev/fin-fixtures`.

A deterministic integration/regression harness for the Finance domain. It seeds, inspects, mutates
(via the **real** Finance endpoints), validates and resets clearly-synthetic `ZZTEST-FIN-*` scenarios
in the disposable DEV prod-clone. It exists so any Finance-related change can be exercised end-to-end
without touching a single historical (real) request.

The harness **never runs production business logic itself** — it only seeds fixtures and reads state.
Every transition under test is driven through the actual Finance endpoints (`/finance/*`,
`/requests/{id}/b2p/*`, `/requests/{id}/operational/register-po`).

---

## Purpose

Deterministic integration/regression testing for Finance: prove that each Finance action transitions
the correct `RequestPoGroup`/`RequestPayment` without affecting siblings, that the `RequestPayment`
ledger stays complete and its `(RequestId, PaymentType, PaymentSequence)` uniqueness holds, and that
the multi-group projection / filters / notes behave correctly.

## When to run

Run the harness (seed → drive → verify → reset) after **any** change involving:

- Finance workflow / Finance status aggregation
- `RequestPoGroup` lifecycle or `RequestPayment` creation
- `PaymentSequence` allocation (`PaymentSequenceAllocator`)
- P.O. registration (`RegisterPo`, incl. advance-payment creation)
- payment scheduling / cancellation
- payment completion / **direct** payment (pay from `PO_ISSUED`)
- advance payment (schedule / confirm)
- reconciliation (remaining-balance `RequestPayment`)
- post-payment completion (Phase 1 / Phase 2) interactions
- sibling / multi-group behavior
- Finance obligations projection / per-group eligibility
- Finance filters / search / sorting
- Finance notes
- receiving-related Finance transitions where applicable

## Safety

- **DEV only.** Three independent gates, all required:
  - **(A) compile-time** — the controller body is inside `#if DEBUG`; Release builds compile only a
    `NotFound` stub, so it can never reach TEST/PROD.
  - **(B) runtime environment** — `IWebHostEnvironment.IsDevelopment()` must be true.
  - **(C) explicit opt-in** — configuration `DevFixtures:FinanceEnabled` must be `true`.
  - Any gate not satisfied → every endpoint returns **404** (the harness is invisible).
- Operates on **synthetic `ZZTEST-FIN-*` rows only** (RequestNumber starts with `ZZTEST-FIN-` AND
  Title starts with `[ZZTEST-FIN]`). Reset removes only those rows and their children (payments,
  attachments, histories, reconciliations, line items, groups, requests, and the `ZZTEST-FIN`
  synthetic supplier). It is **never** a generic database-mutation API.
- **Never** use historical clone requests as mutation targets.
- **Always reset after testing** and **verify zero fixtures remain** (`GET .../state` returns `[]`).
- Never run against TEST or PROD. Never enable `DevFixtures:FinanceEnabled` in committed / TEST / PROD
  configuration.

## How to enable locally

`appsettings.Development.json` is gitignored / local-only. Add (already present on the maintained dev
box):

```json
"DevFixtures": {
  "FinanceEnabled": true
}
```

Run the API in Development (`ASPNETCORE_ENVIRONMENT=Development`) from a `Debug` build. The clone DB is
`Portal-Gerencial-Dev-ProdClone` on `(localdb)\MSSQLLocalDB` (see `docs/DEV_DATA_REFRESH.md` and
`directives/RULE_DEV_DATABASE.md`). In any other combination the endpoints are 404.

## Available operations

| Method | Route | Purpose |
|--------|-------|---------|
| `GET`  | `api/v1/dev/fin-fixtures/token`  | Mint a DEV JWT (SystemAdministrator + Finance + Buyer) for a real active clone user, so the harness can drive the real Finance/Buyer endpoints with global scope and FK-valid audit attribution. |
| `POST` | `api/v1/dev/fin-fixtures/seed`   | Reset, then create all `ZZTEST-FIN-*` scenarios. Returns the scenario→ids map + resolved org units. Idempotent (reseeds deterministically). |
| `POST` | `api/v1/dev/fin-fixtures/reset`  | Remove all `ZZTEST-FIN-*` rows + children + synthetic supplier. |
| `GET`  | `api/v1/dev/fin-fixtures/state`  | Inspect current `ZZTEST-FIN-*` requests: status, groups, per-group payments, request-level payment ledger (incl. group-less rows), attachments, history. Returns `[]` when clean. |
| `POST` | `api/v1/dev/fin-fixtures/proof`   | Create a DEV-safe `PAYMENT_PROOF` attachment for a group and return its id (the real `/pay` endpoint mandates a proof). Body: `{ requestId, groupId }`. |

## Scenarios

| Fixture | Shape | Exercises |
|---------|-------|-----------|
| `ZZTEST-FIN-A` | single group `PO_ISSUED` | schedule; direct actions |
| `ZZTEST-FIN-B` | single `PAYMENT_SCHEDULED` (future) + scheduled payment | cancel-schedule |
| `ZZTEST-FIN-C` | single `PAYMENT_SCHEDULED` **overdue** (−5d) | overdue pay |
| `ZZTEST-FIN-D` | `PAYMENT_COMPLETED` + `PO_ISSUED` siblings | sibling isolation; multi-group schedule (seq2); multi-group direct pay |
| `ZZTEST-FIN-E` | `PAYMENT_COMPLETED` + `PAYMENT_SCHEDULED` siblings | sibling isolation through scheduled lifecycle |
| `ZZTEST-FIN-F` | `PAYMENT_COMPLETED` + `PO_ISSUED` siblings | group-scoped Return for Adjustment |
| `ZZTEST-FIN-G` | `ADVANCE_PAYMENT_REQUIRED` + PLANNED advance row | advance schedule / confirm (`b2p/*`) |
| `ZZTEST-FIN-H` | single `WAITING_PO` | Buyer-stage guard (Finance cannot act; not surfaced) |
| `ZZTEST-FIN-I` | single `PO_ISSUED`, zero notes | Finance notes + note indicator |
| `ZZTEST-FIN-K` | single `PO_ISSUED` (never scheduled) | direct pay from `PO_ISSUED` (ledger row creation) |
| `ZZTEST-FIN-J1` | `PO_ISSUED` — AlplaPLASTICO / Viana 1 / TI | Company/Plant/Department filter |
| `ZZTEST-FIN-J2` | `PO_ISSUED` — AlplaSOPRO / Viana 3 / Recursos Humanos | Company/Plant/Department filter |
| `ZZTEST-FIN-ADV` | two `WAITING_PO` siblings + `SupplierId` + PO doc | `RegisterPo` advance on two groups → ADVANCE seq1 / seq2 |
| `ZZTEST-FIN-RECON` | `WAITING_RECONCILIATION`, group owns `FINAL_BALANCE` seq1 | reconciliation remaining-balance (group-less `FINAL_BALANCE` seq2) |

Org units are resolved dynamically against the clone (AlplaPLASTICO / AlplaSOPRO, their Viana plants,
TI / Recursos Humanos), so J1/J2 stay meaningful even if ids differ.

---

## Finance change maintenance checklist

```
[ ] Seed Finance DEV fixtures              (POST api/v1/dev/fin-fixtures/seed)
[ ] Run relevant automated tests           (see docs/... test inventory below)
[ ] Run mutable Finance acceptance scenarios (drive the real endpoints)
[ ] Verify multi-group sibling isolation
[ ] Verify RequestPayment ledger integrity
[ ] Verify PaymentSequence uniqueness (RequestId, PaymentType, PaymentSequence)
[ ] Verify reconciliation if affected
[ ] Verify filters / projection if affected
[ ] Reset ZZTEST-FIN fixtures              (POST api/v1/dev/fin-fixtures/reset)
[ ] Confirm 0 ZZTEST-FIN rows remain       (GET api/v1/dev/fin-fixtures/state → [])
```

### Related automated regression tests

Backend (`tests/backend/AlplaPortal.Application.Tests/Services/Finance/` and `.../Services/Requests/`):
`PaymentSequenceAllocatorTests`, `FinanceScheduleSequenceTests`, `FinanceDirectPayLedgerTests`,
`ReconcileRequestLedgerTests`, `FinanceReturnForAdjustmentTests`, `FinanceObligationProjectionTests`,
`FinanceObligationsEndpointTests`, `FinanceObligationsPhase5Tests`, `FinancePaymentEligibilityServiceTests`,
`DevFinanceFixtureHarnessGuardTests`.
Frontend pure helpers: `src/frontend/src/lib/financePaymentsView.test.mjs`.

> Known unrelated baseline: `GroupBuilderServiceTests.BuildGroupsForRequestAsync_CreatesGroups_WhenLineItemsHaveSelectedQuotation`.
