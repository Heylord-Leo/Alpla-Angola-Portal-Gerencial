# Supplier account credit applied to P.O. groups ("Créditos em conta corrente")

> **Status:** Stage 1 (1a ledger + supplier record, 1b QUOTATION workflow for Pós-Pago and 100 % Antecipado, full and partial
> coverage) **implemented 2026-11-09, uncommitted, awaiting manual validation in TEST**. Backend 0 errors, frontend `tsc -b`
> clean, 57 new automated tests green. **One additive migration** `20261109150614_AddSupplierCreditAccounts` — applied to no
> environment yet (DEV applies it on the next `execution/restart_services.ps1`; TEST/PROD via the GitHub Actions migration
> jobs under separate authorization). Stage 2 (Antecipado Parcial, PAYMENT requests, D7 partial kinds, the final-stage rule)
> is **planned only** (§9, revision 7) and needs separate approval. Activation flag `SupplierCredit:NewReservationsEnabled`
> ships **false**; TEST enables it server-side only with this complete build deployed.

## 1. Business model (confirmed by the owner, plan revisions 1–6)

- Purchasing records a supplier's credit on the supplier ficha (`/contracts/fichas`, Contracts role). One account per
  **supplier × buying company × currency** (D1): Viana 1 and Viana 2 share the APA balance; APS is separate; the plant is kept
  on movements for traceability only. The ledger is append-only; a reservation is a **hold**, never a movement.
- The Buyer may use credit when registering the P.O. — option **"Utilizar crédito em conta corrente"** shown below "Condição de
  pagamento", never a fourth condition. Stage 1 terms: Pós-Pago, 100 % Antecipado. Full or partial coverage; a near-full amount
  normalises to full only when the account can cover the total.
- Finance settles: the bank part **exactly** (no tolerance: `cash == planned row`), with the reserved credit applied in the same
  transaction; a fully covered stage is settled by **"Confirmar utilização do crédito"** (`apply-credit`) alone. Bank endpoints
  refuse a fully covered stage (409 `CREDIT_APPLICATION_REQUIRED`).
- Correction keeps the hold (keep / change / remove); cancellation releases it; Finance may reverse an applied credit (D7, a
  different Finance user, reason, before any receiving / invoice / reconciliation evidence); stage 1 supports the
  FULL_COVERAGE kind — partial kinds answer 409 `CREDIT_REVERSAL_PARTIAL_UNSUPPORTED` until stage 2.
- Credit is **settlement, not cash**: it counts for completion, obligations and cancellation evidence; it is excluded from
  cash reporting, Accounts Payable e-mails, dashboard paid cash and the Finance "Pagos" history; `Request.ActualPaidAmount`
  stays cash-only.

## 2. Ledger rules (`SupplierCreditPolicy`, pure)

```
Balance   = Σ SupplierCreditEntries.SignedAmount   (CREDIT_IN +, ADJUSTMENT_IN +, ADJUSTMENT_OUT −, APPLICATION −, REVERSAL +, CLOSE_OUT −)
Hold(res) = Amount − AppliedAmount                  while RESERVED | PARTIALLY_APPLIED; 0 otherwise
Reserved  = Σ Hold ;  Available = Balance − Reserved
Application: entry −stage amount AND the hold shrinks by the same amount → Available unchanged
Reversal:    one REVERSAL entry = AppliedAmount only; the unapplied hold is released by the status change (no entry)
```
Split for a group of total `T`, advance `pct`, coverage `C`: `Adv = round2(T·pct/100)`, `Bal = T − Adv`, `CredAdv = min(C, Adv)`,
`CredBal = C − CredAdv`, `BankAdv = Adv − CredAdv`, `BankBal = Bal − CredBal`. Pós-Pago: everything is the (single) balance;
100 % Antecipado: everything is the (single) advance. Owed rows materialised at registration (stage `SINGLE`): the bank row
(`FINAL_BALANCE` or `ADVANCE`, amount = bank part, only when > 0) and the `SUPPLIER_CREDIT` row (amount = credit). Zero rows are
never created.

## 3. Data model (migration `20261109150614_AddSupplierCreditAccounts`, additive)

| Table / column | Purpose |
|---|---|
| `SupplierCreditAccounts` | supplier × company × currency, `AccountNumber` `CCS-nnnnnn` (SystemCounters), status, rowversion; unique (SupplierId, CompanyId, CurrencyCode) |
| `SupplierCreditEntries` | append-only ledger: type, signed amount, balance-after, reference, reason, workflow linkage (reservation, request, group, stage, plant), filtered-unique `IdempotencyKey` |
| `SupplierCreditReservations` | hold per group: Amount, Advance/Balance split, AppliedAmount, status, applied-entry pointers, release/reversal facts, rowversion; **filtered unique on `RequestPoGroupId` where status ∈ (RESERVED, PARTIALLY_APPLIED, APPLIED)** |
| `RequestPoGroups.CreditCoverageAmount`, `SupplierCreditReservationId` | live coverage (0 = none) and pointer to the latest reservation (kept after release/reversal = "credit-touched") |
| `RequestPayments.SupplierCreditReservationId`, `SettlementStage` | owed-row linkage and stage (SINGLE / ADVANCE / BALANCE); new `PaymentType` `SUPPLIER_CREDIT` |

## 4. Endpoints

| Method / route | Who | Effect |
|---|---|---|
| `GET/POST api/v1/lookups/suppliers/{id}/credit-accounts`, `GET …/{accountId}`, `GET …/{accountId}/movements`, `POST …/{accountId}/entries` | view: every ficha viewer; write: Contracts, SysAdmin (`SupplierSheetCapabilities.CanManageSupplierCredit`) | accounts, balances, movements; Purchasing entries (reference + reason mandatory; ADJUSTMENT_OUT never below the reserved total) |
| `GET api/v1/requests/{id}/po-groups/{gid}/credit-eligibility` | responsible Buyer (role → scope → ownership), SysAdmin | flag, account, max coverage, current hold |
| `POST api/v1/requests/{id}/operational/register-po` (+ `useSupplierCredit`, `supplierCreditAmount`, `supplierCreditCorrectionAction`) | responsible Buyer on the credit path | reservation / keep-change-remove inside the registration transaction; owed rows; history `SUPPLIER_CREDIT_RESERVED` / `_RESERVATION_CHANGED` / `_RELEASED` |
| `POST api/v1/requests/{id}/po-groups/{gid}/credit-reservation/release` | responsible Buyer, Local Manager, SysAdmin; group WAITING_PO / WAITING_PO_CORRECTION; hold RESERVED | release (no ledger entry), notification to plant-scoped Finance |
| `POST api/v1/finance/{id}/apply-credit` | Finance (controller gate) | applies the PLANNED credit row of the stage when no bank part is pending; Pós-Pago → PAYMENT_COMPLETED, 100 % Antecipado → WAITING_SUPPLIER_DELIVERY; event `SUPPLIER_CREDIT_APPLIED` |
| `POST api/v1/finance/{id}/pay`, `b2p/confirm-advance` | Finance | credit-touched groups: `cash == planned bank row` (400 `CREDIT_PAYMENT_AMOUNT_MISMATCH`), credit applied atomically; full coverage → 409 |
| `POST api/v1/finance/{id}/schedule`, `b2p/schedule-advance`, `cancel-schedule`, `return` | Finance | credit-touched groups: schedule the PLANNED bank row (never the total); cancel-schedule returns it to PLANNED; return cancels every PLANNED/SCHEDULED owed row and keeps the hold |
| `POST api/v1/finance/{id}/reverse-credit` | active Finance role in DB, plant scope, ≠ applier, reason ≥ 20 | FULL_COVERAGE reversal: REVERSAL entry, rows cancelled, bank obligation reopened (PO_ISSUED / ADVANCE_PAYMENT_REQUIRED); event `SUPPLIER_CREDIT_REVERSED` |
| legacy `operational/schedule-payment`, `operational/complete-payment`, `b2p/reconcile` | — | 409 for credit-touched requests |

Finance queue: `FinancePaymentEligibilityService.EvaluateGroupActions(…, FinanceGroupCreditFacts)` adds `APPLY_CREDIT` (and removes
SCHEDULE/PAY) when the current stage owes no bank part, and `REVERSE_CREDIT` for an APPLIED full coverage without cash or
evidence. `FinanceObligationDto` carries `creditCoverageAmount`, `creditAppliedAmount`, `creditReservedAmount`,
`creditReservationStatus`, `creditAccountNumber`, `bankPayableAmount`; `paymentFlow` = `CREDIT` for a credit-only stage.

## 5. Concurrency and protections

Lock order inside every writer: group row (UPDLOCK) → account row → the group's reservations → the caller's existing locks (SQL Server
only; InMemory tests skip the hints). One live reservation per group (filtered unique index); application gated three times (row
PLANNED, reservation status, idempotency key `APPLY:{ReservationId}:{Stage}`); reversal key `REV:{ReservationId}`; history keys
`CRES/CCHG/CAPP:{Stage}/CREL/CREV:{ReservationId}`; strict `cash == PlannedAmount`; `ADJUSTMENT_OUT` refused below the reserved
total under the account lock; RowVersion checks on apply/reverse/release (`CREDIT_CONCURRENT_CHANGE`).

## 6. Frontend

- Ficha (`SupplierCreditSection.tsx`, mounted in `SupplierFichaDetailContent` — page and Buyer drawer): accounts table
  (saldo / reservado / disponível), movements, create account and record movements for Purchasing only (`canManage` from the API).
- P.O. wizards (`components/po/SupplierCreditCoverageBlock.tsx` in `RegisterPoModal` and `CorrectPoModal`): account figures,
  checkbox + amount (first registration), Manter / Alterar / Remover (correction), resulting split; the block does not render when
  the supplier has no account for the request company / group currency and no existing hold. Payload fragment built by
  `buildSupplierCreditPayload` (`lib/correctPoSubmission.ts`).
- Finance queue (`FinancePaymentsList`, `financePaymentsView`, `FinanceActionModal`): "Aplicar crédito" primary action for the
  CREDIT flow, "Reverter crédito aplicado" in the kebab, credit chip (Valor da P.O · Crédito · Valor a pagar), confirmation modals
  showing the figures in the obligation's own currency; row grid in `FinancePayments.css` (re-flows below 1280 px / 860 px,
  amounts never wrap).
- Shared presentation helpers (DEV round 1 corrections, §10): `lib/workflowEventLabels.ts` (the five credit event labels used by
  the request page, Finanças → Histórico and the Print View) and `lib/advanceObligationNotice.ts` (the "100% Antecipado" notice:
  total advance · credit coverage · bank advance, for both P.O. wizards).

## 7. Automated tests (2026-11-09, 57 green)

`SupplierCreditPolicyTests` (ledger table of the plan, hold rule, coverage decision incl. normalisation, split table, owed rows for
the three terms, strict cash, reversal kind, keys, stage-1 eligibility); `FinanceSupplierCreditEligibilityAndProjectionTests`
(APPLY_CREDIT / REVERSE_CREDIT rules, request-level union, facts builder, obligation projection bank part + CREDIT flow, completion
with SUPPLIER_CREDIT rows); `SupplierCreditSettlementServiceTests` (reserve partial / near-full / refusals, apply once + duplicate
409, release, reverse full + duplicate, reverse refused partial / evidence, keep / change / change-flag-off / remove,
eligibility); `SupplierCreditWorkflowEndpointTests` (register-po partial and full-advance, other Buyer 403 + flag off 400 + nothing
written, apply-credit once + bank endpoints 409, pay strict 400 then exact 150 with atomic application, reverse-credit self 403 /
admin-only 403 / short reason 400 / success + reopened exact bank payment, cancellation releases the hold, legacy endpoint 409).
Run: `dotnet test --filter FullyQualifiedName~SupplierCredit`. Not covered automatically: SQL Server lock behaviour (RC-1…RC-11 of
the plan need the LocalDB sandbox), notifications content, UI rendering.

Added 2026-11-12 (DEV round 1 corrections, §10): `RequestLotTimelineBuilderTests` — four cases for the settled 100 % Antecipado
group (Pagamento completed, only Recebimento / Execução current; partial advance keeps Pagamento current; AdvancePaymentRequired
stays current); frontend `src/lib/advanceObligationNotice.test.ts` (8) and `src/lib/workflowEventLabels.test.ts` (3), run with
`node node_modules/vitest/vitest.mjs run src/lib/advanceObligationNotice.test.ts src/lib/workflowEventLabels.test.ts` from
`src/frontend` (the `npx` launcher fails on the "&" of the OneDrive path). Last run 2026-11-12: backend filter
`RequestLotTimelineBuilderTests|RequestWorkflowProjection|SupplierCredit|FinanceSupplierCredit` → 116 passed; frontend 41 passed;
`tsc -b` clean.

Added 2026-11-12 (DEV round 2 closure, §10.7): `SupplierCreditStage1ClosureEndpointTests` (5, InMemory endpoints — bank
endpoints 409 on full coverage incl. b2p, partial reversal 409 at the endpoint, flag-off keep / change / remove / release /
cancellation / settlement, release endpoint rules, account management Contracts-only) and `SupplierCreditRelationalTests` (5,
EXECUTED on the LocalDB sandbox — lock path + insufficient credit, two concurrent reservations, two concurrent registrations of
one group, two concurrent applications + idempotency index, account key / plant sharing / company isolation). The relational
suite needs LocalDB (`Portal-Gerencial-IntegrationTests`) and skips silently without it; the SQL Server lock behaviour previously
listed as "not covered automatically" is now executed there.

## 8. Manual validation in TEST (stage 1) — steps that need the owner

1. **Migration** `20261109150614_AddSupplierCreditAccounts` on `Portal-Gerencial-Test` (Apply TEST Migrations), then Deploy to TEST
   of this build. Flag stays **false** for the smoke; then set `SupplierCredit:NewReservationsEnabled=true` in the server-preserved
   `appsettings.Test.json` and restart.
2. **Purchasing (Contracts):** on a TEST supplier ficha create APA/AOA and APA/USD accounts; record a CREDIT_IN with reference;
   over-adjustment (`ADJUSTMENT_OUT` above the available) refused; Finance-without-Contracts sees read-only; Buyer drawer read-only.
3. **Buyer:** QUOTATION request, Viana 1 and Viana 2 groups of the same supplier see the same APA balance; register a P.O. Pós-Pago
   with partial credit (e.g. 350 000 of 500 000): obligations panel shows bank 150 000 + credit 350 000; register another with full
   coverage; 100 % Antecipado with partial credit; another Buyer cannot use credit on this request (403); with the flag off the
   option disappears and keep / remove still work during correction.
4. **Finance:** queue shows Valor da P.O / Crédito reservado / Valor a pagar; full coverage → only "Aplicar crédito" (Pós-Pago →
   PAYMENT_COMPLETED; 100 % Antecipado → WAITING_SUPPLIER_DELIVERY, Receiving may confirm); partial → pay exactly 150 000 (149 999,99
   and 150 000,01 refused), credit applied in the same action; schedule plans 150 000; cancel-schedule keeps the owed row; Finance
   return then correction keep / change / remove with the ficha balances consistent; cancellation of a reserved group releases the
   hold; reversal of a full coverage by a different Finance user reopens the bank obligation; the ficha movements list every step;
   Finance history shows the credit events under their own labels, never under "Pagos"; dashboard paid cash excludes credit.
5. **FLAG-OFF scenario** (plan §3.2): with holds in place, switch the flag off and restart — settlement, release, reversal and keep /
   remove keep working; new reservations and amount changes are refused.

## 9. Stage 2 plan — revision 7 (2026-11-09, for review; not implemented)

Baseline: revision 6 (§4.10 gate `BalanceStageGate` G1–G6, §4.11 `validate-adjusted-balance`, §4.12 shared enforcement). The owner's
four requirements change the gate, the adjusted-balance action, the acceptance list and add a configuration check.

### 9.1 Zero remaining balance vs over-settlement (requirement 1)

`Due_B = V − S` with `V = min(ValidatedTotal, T)` and `S` = completed advance-stage cash + credit.

| Case | Gate result | Effect |
|---|---|---|
| `Due_B > 0` | G5 true → settlement per the planned (or adjusted) BALANCE rows | unchanged |
| **`Due_B == 0`** (validated amount equals what was already settled) | **new G5a "ZERO_BALANCE"** — not a block | Finance action **`close-balance`** (`POST …/po-groups/{gid}/close-balance`, Finance, under the group lock): cancels the PLANNED BALANCE rows (bank and credit) with note "saldo zero após validação", releases the remaining hold **without a positive ledger entry** (`ReleasedHoldAmount`, reservation `APPLIED` with `AppliedAmount < Amount`), writes history `BALANCE_CLOSED_ZERO` (`BZC:{GroupId}:{ShortCloseId?}`) recording T, V, S, released hold; completion payment dimension reads `completedPaidTotal = S ≥ AdjustedSettlementTotal = V` → satisfied. Queue text "Saldo zero — obrigação financeira encerrada". Idempotent: second call 409 `BALANCE_ALREADY_CLOSED`. |
| **`Due_B < 0`** (validated below what was settled) | **G5 false → `BALANCE_SETTLED_EXCEEDS_VALIDATED`** | settlement, `validate-adjusted-balance` and `close-balance` all 409; history `BALANCE_REVIEW_REQUIRED` once (`BRR:{GroupId}:{ValidatedTotalSnapshotId}`); the hold is kept; Finance review flag in the queue; resolution (refund / credit note) is D23, outside this plan. |

State transitions gain `gate ZERO ─close-balance─▶ rows cancelled, hold released, payment dimension satisfied` and
`V < S ─▶ blocked + flagged`. Tests: RC-26 (`close-balance` vs `apply-credit (BALANCE)` → one succeeds), unit tests of G5/G5a on the
boundary `V == S`, `V == S ± 0,01`.

### 9.2 Completion after an approved short-close — 100 ordered, 80 delivered (requirement 2)

Trace with today's code [verified in revision 6 §1.1]: receipt dimension = `OperationalReceiptCompletedAtUtc != null ||
AreAllGroupItemsReceived(group)`; the stamp is written by `ConfirmReceiving` only when every item is received; `IN_FOLLOWUP` is
the state after a partial confirmation; the short-close touches no receiving fact. **Therefore a short-closed group can never
satisfy the receipt dimension today**: after Finance validates the adjusted balance and the BALANCE rows settle, the payment
dimension is satisfied (`completedPaidTotal 400 000 ≥ AdjustedSettlementTotal 400 000`) but `receiptSatisfied` stays false and the
group stays open with `RECEIPT_PENDING`.

Decision for the plan (replaces D25 "out of scope"): **the short-close must become receiving evidence for the undelivered
remainder, written by Receiving, not by Finance.** Proposed smallest change: when an `OperationInvoiceShortClose` is APPROVED and
the group is `IN_FOLLOWUP`, the Receiving queue shows the group with "Encerramento com saldo aprovado — confirmar receção final";
Receiving's existing `confirm-receiving` is extended so that, **with an approved short-close**, it may stamp
`OperationalReceiptCompletedAtUtc` and move the group to `WAITING_RECEIPT` although `AreAllGroupItemsReceived` is false, recording
the undelivered lines in a new `OPERATIONAL_RECEIPT_CLOSED_SHORT` history row (`ORS:{GroupId}:{ShortCloseId}`) and marking the
undelivered lines `CLOSED_SHORT` (line status `CANCELLED_AFTER_APPROVAL` reused? — **decision D25a**: new line status
`SHORT_CLOSED` vs reuse; recommended new status to keep cancellation analytics clean). The completion predicate is untouched (it
already reads the stamp). Until D25a is decided, **adjusted settlement after a short-close stays blocked** in stage 2 with reason
`BALANCE_SHORT_CLOSE_RECEIPT_UNSUPPORTED`, so no group can reach a settled-but-never-completable state — this is the documented
dependency. Sequence proven by the acceptance test: 80 received → `IN_FOLLOWUP`; invoice 400 000 validated → `PARTIALLY_INVOICED`;
short-close approved → queue `BALANCE_SHORT_CLOSE_REQUIRES_VALIDATION`, all settlement 409; Receiving confirms the final receipt
under the short-close → stamp + `WAITING_RECEIPT`; Finance validates the adjusted balance (Due_B 200 000: credit 150 000 + bank
50 000); settlement; payment and receipt dimensions satisfied → group COMPLETED (fiscal receipt per its own rule).

### 9.3 Binding the approved balance to the invoice revision (requirement 3)

The adjusted-balance record gains `AdjustedSettlementInvoiceRevisionKey` = SHA-256 of the ordered set of (OperationInvoiceId,
ReplacementOf chain head, AllocatedGrossAmount, DocumentStatus) of the group's non-superseded allocations at validation, plus the
`ValidatedTotal` snapshot. Rules:

- **Before settlement** — any write that changes the group's allocations or an invoice's status (`SaveAllocations`, `Replace`,
  `Reject`, `Validate` of a further invoice, short-close approval/rejection) recomputes the key inside its own transaction (the
  coverage re-derivation already runs there with a forced group touch) and, when it differs, **invalidates** the record
  (`AdjustedSettlementInvalidatedAtUtc`, reason), cancels any SCHEDULED/PLANNED BALANCE rows created by it (re-creating the
  original planned rows), writes `BALANCE_APPROVAL_INVALIDATED` (`BAI:{GroupId}:{OldKey}`) and sets the gate back to G6 false
  (`BALANCE_SHORT_CLOSE_REQUIRES_VALIDATION`). Finance must run `validate-adjusted-balance` again (new key) before any settlement.
- **After settlement** — the same recomputation finds a COMPLETED BALANCE row: nothing is re-planned; the group is flagged
  `BALANCE_REVIEW_REQUIRED` (history once per key) and **no new payable obligation is created automatically**; resolution is D23.
- **Race protection**: `pay` / `apply-credit (BALANCE)` / `close-balance` take the group row lock, then re-read the record and
  recompute the key from the committed allocations before writing; a mismatch is 409 `ADJUSTED_BALANCE_STALE`. The invoice writers
  take the same group lock before re-deriving (today's `RederiveAsync(forceGroupTouch: true)` already updates the row, so the
  rowversion arbitrates: a settlement that read the record before an invoice replacement committed fails on `RowVersion` or on the
  key check). Tests RC-27 (replace vs pay), RC-28 (allocation change vs validate-adjusted-balance), RC-29 (replace after
  settlement → flag only, no new row).

### 9.4 Configuration dependency (requirement 4)

A7 becomes a checklist item with evidence: before stage 2 validation, read the effective `PostPaymentCompletion.Enabled` in TEST
(`GET api/v1/config` surface or the server-preserved `appsettings.Test.json` — read-only) and record the value in this document;
before the PROD rollout, the same read-only inspection of the PROD configuration (never a change). **Not verified today**: neither
TEST nor PROD values were read in this session (the record of 2026-08-18 says PROD Enabled=true, CompletionEnabled=false).

### 9.5 Acceptance additions (stage 2 TEST)

13. **Exact-zero balance**: advance settled S = 200 000, invoice validated V = 200 000 → gate ZERO; `close-balance` once (hold
    released, no ledger entry, rows cancelled, completion payment dimension satisfied); second call 409; `pay` / `apply-credit`
    409.
14. **Validated below settled**: V = 150 000 < S = 200 000 → `BALANCE_SETTLED_EXCEEDS_VALIDATED`, review flag, every settlement
    and `close-balance` 409, hold kept, ledger unchanged.
15. **80 of 100 through completion**: the §9.2 sequence end to end, including the blocked state while D25a is pending.
16. **Invoice replacement before settlement**: validated balance → `replace` invoice → record invalidated, scheduled row
    reverted, `BALANCE_APPROVAL_INVALIDATED` once, settlement 409 until re-validation.
17. **Invoice replacement after settlement**: flag only, no new PLANNED row, completion untouched.
18. Scenarios 1–12 of revision 6 unchanged.

### 9.6 Decisions open for stage 2

| # | Decision | Recommendation |
|---|---|---|
| D22 | who validates the adjusted balance | any in-scope Finance user (as rev. 6) |
| D23 | resolution of V < S | blocked + flagged in plan; refund / credit-note mechanism separate |
| D24 | validated coverage above T | capped at T |
| **D25a** (new) | receipt dimension of a short-closed group: Receiving confirms the final receipt under the approved short-close with a new line status `SHORT_CLOSED` (recommended) vs reuse `CANCELLED_AFTER_APPROVAL` vs leave unsupported (settlement stays blocked) | new status; until decided, blocked |
| D15–D20, D8–D11 | as revision 5 | |

Rollout unchanged: two TEST stages, one PROD rollout after both pass end-to-end acceptance and the owner approves deployment.

## 10. Stage 1 validation record

Sequence agreed with the owner: **DEV corrections → second DEV round → only then plan the TEST publication.** Nothing in this
section was committed, deployed or configured by the agent; the DEV API must be rebuilt and restarted (coordinated with the active
workspace) before the second round, because four backend files changed (10.3).

### 10.1 DEV round 1 — manual results confirmed by the owner (2026-11-12)

Environment: DEV API + Vite on the `Portal-Gerencial-Dev-ProdClone` clone, migration `20261109150614_AddSupplierCreditAccounts`
applied by the Development auto-migrate, `SupplierCredit:NewReservationsEnabled` switched on in `appsettings.Development.json`
for the functional scenarios and off for the last one.

| # | Scenario | Request / P.O. | Observed result | Verdict |
|---|---|---|---|---|
| 1 | 100 % Antecipado, full credit coverage | REQ-27/10/2026-647 · ECF10 2026/9633 | 11 274,23 RND of credit applied, zero bank payment; group → WAITING_SUPPLIER_DELIVERY | Pass |
| 2 | Full-coverage reversal | same group | a different Finance user reversed; available back to 450 000,00 RND; the same group reopened an 11 274,23 RND bank advance | Pass |
| 3 | 100 % Antecipado, partial credit coverage | REQ-10/11/2026-684 · ECF10 2026/9634 | total 15 840,00 RND, credit 10 000,00, bank advance 5 840,00; 5 841,00 rejected; exact amount succeeded and applied the credit; balance 440 000,00 RND, no remaining hold | Pass |
| 4 | Pós-Pago, partial credit coverage | REQ-10/11/2026-685 · ECF10 2026/9635 | total 12 450,00 RND, credit 10 000,00, bank 2 450,00; 2 451,00 rejected; correct settlement applied the credit, account 430 000,00 RND, group paid awaiting receiving | Pass |
| 5 | Pós-Pago, full credit coverage | ECF10 2026/9636 | owner confirmed success | Pass |
| 6 | Return and correction — Manter / Alterar / Remover | REQ-12/11/2026-688 · ECF10 2026/9638 · CONSULTIT | hold of 1 000,00 AOA survived return + Manter without duplication; Alterar replaced it with 1 500,00; Remover released it, availability back to 1 000 000,00 AOA, full 100 000,00 AOA bank obligation reopened | Pass |
| 7 | Cancellation with a hold | same request, after reserving 1 000,00 AOA again | group cancellation released the hold without a ledger credit or debit; available back to 1 000 000,00 AOA; Finance then confirmed the ECF annulment | Pass |
| 8 | New reservations disabled, existing hold settled | REQ-12/11/2026-689 · ECF10 2026/9639 | fresh wizard showed the disabled message; Finance still applied the existing 100 000,00 AOA reservation with zero bank payment; CCS-000004 ended at 900 000,00 AOA, reserved zero | Pass |
| 9 | Company-level ownership | CONSULTIT · CCS-000004 | Viana 1 and Viana 2 used the same AlplaPLASTICO / AOA account | Pass |

**Still pending (not executed in round 1; stay pending until executed and reported):** the API checks of the DEV guide
(`schedule` / `pay` / `b2p/*` 409 on a fully covered group, duplicate `apply-credit` 409, `credit-reservation/release` 200 / 409,
other Buyer 403, reversal refusals 403 / 400, partial-coverage reversal 409, register-po with the flag off 400), the
insufficient-credit refusal, the APS separate-balance check, and every concurrency check (`CREDIT_CONCURRENT_CHANGE`; SQL Server
row locks are only exercised in TEST).

### 10.2 Round 1 feedback — root causes and corrections (2026-11-12, uncommitted)

| # | Feedback | Root cause (traced in the working tree) | Correction |
|---|---|---|---|
| 1 | Request explorer "Valor Estimado" showed **AOA 11 274,23** for a RND request | `GET requests` projected the amount from `ApprovedTotalAmount` but the currency from the selected quotation / request currency; candidate-based approvals leave `SelectedQuotationId` null, so the approved RND figure carried the requester's AOA. The field is the approved / P.O. amount (not a converted estimate) | `RequestsController` list projection: currency = `ApprovedCurrencyCode` whenever the amount is the approved snapshot; `ApprovedCurrencyCode = "MULTIPLE"` → `HasMultipleCurrencies` + `AmountsByCurrency` (per-currency active totals from `ActiveApprovedTotals`, never summed) and the explorer table, the action carousel and the Receiving list render **one line per currency**. Same currency rule applied to the three Finance projections (`summary`, `cashflow-projections`, `payments`) that shared the pattern. The explorer KPI "Total Filtrado" was then redone per currency (10.5) |
| 2 | "Confirmar Adiantamento" / "Confirmar Liquidação" showed **Valor esperado/agendado … AOA** for RND obligations | `FinanceActionModal` hard-coded "AOA" | New `currencyCode` prop, fed from the selected obligation (`o.currencyCode`) by `FinancePaymentsList`; both PAY modals (advance and settlement) use it |
| 3 | 100 % Antecipado warning said the full P.O. amount would be required as an advance while the credit summary showed a lower bank amount | the notice ignored the credit decision | `lib/advanceObligationNotice.ts`: total advance · credit coverage · bank advance; no credit → original wording unchanged; full coverage → "coberto integralmente … Não há adiantamento bancário"; partial → "{crédito} cobertos por crédito … e {banco} a adiantar por banco". Used by `RegisterPoModal` (first registration) and `CorrectPoModal` (Manter / Alterar / Remover) |
| 4 | Two "ATUAL" stages (Pagamento and Recebimento / Execução) after the credit settlement of 647 | `RequestWorkflowProjectionBuilder` mapped WAITING_SUPPLIER_DELIVERY to `PaymentState = ADVANCE_IN_PROGRESS` regardless of the condition, so the lot timeline marked Pagamento current while the delivery wait is also current. It affected every settled 100 % Antecipado group (bank or credit), not only credit | WAITING_SUPPLIER_DELIVERY / ADVANCE_PAYMENT_COMPLETED with `PaymentConditionCode = ADVANCE_FULL` → `PaymentState = COMPLETE` (Pagamento completed, only Recebimento current). ADVANCE_PARTIAL keeps `ADVANCE_IN_PROGRESS` because the balance is still owed after delivery. Lots of a multi-group request keep their own tracks (unchanged). The request-page group progress mirrors the rule |
| 5 | Monetary values broke across lines in the ficha movements; Finance queue labels / chips / next-action crowded | table cells had no wrap control; the row grid was inline with nowrap chips | Ficha: monetary cells `nowrap` + tabular digits, "aplicado …" on its own line, request / P.O. / plant stacked, reference / reason wraps, tables scroll horizontally. Queue: grid moved to `FinancePayments.css` (six columns ≥ 1280 px, two rows < 1280 px, two columns < 860 px); amount never wraps; the credit chip wraps as a whole with "banco …" on its own token; next action clamps to two lines with the full text on hover; status chip truncates with the full text on hover |
| 6 | Request history showed raw `SUPPLIER_CREDIT_*` codes | the request page had no mapping for the credit events | `lib/workflowEventLabels.ts` shared by the request page ("CRÉDITO RESERVADO" etc., raw code kept as hover title), Finanças → Histórico and the Print View; stored codes untouched |

Not reproduced: none — all six items were traced to a concrete cause in code.

### 10.3 Files changed for the corrections

Backend: `RequestsController.cs` (list projection + hydration), `FinanceController.cs` (three currency projections),
`RequestWorkflowProjectionBuilder.cs`, `RequestListItemDto.cs`, `RequestLotTimelineBuilderTests.cs`.
Frontend: `FinanceActionModal.tsx`, `FinancePaymentsList.tsx` + new `FinancePayments.css`, `RegisterPoModal.tsx`,
`CorrectPoModal.tsx`, `SupplierCreditSection.tsx`, `RequestEdit.tsx`, `FinanceHistory.tsx`, `requestPrintModel.ts`,
`RequestsTableWidget.tsx`, `ActionCarouselWidget.tsx`, `ReceivingWorkspace.tsx`, `RequestGroupProgress.tsx`, `types/index.ts`,
new `lib/advanceObligationNotice.ts` (+ test), new `lib/workflowEventLabels.ts` (+ test).

### 10.4 DEV round 2

Guide: [SUPPLIER_CREDIT_PO_COVERAGE_DEV_RETEST_GUIDE.md](SUPPLIER_CREDIT_PO_COVERAGE_DEV_RETEST_GUIDE.md) — corrected screens
plus the checks still pending from 10.1. The TEST publication is planned only after round 2 is accepted.

Owner screenshots received 2026-11-12 after the corrections (API not yet restarted for the backend items): corrected row
currencies, a single current "Recebimento / Execução" stage for 684, unbroken monetary values in the supplier movements and the
Portuguese credit event label in the request history — confirmed. Finance modal currencies (2.2) and the wizard advance wording
(2.3) still await manual retesting.

### 10.5 Explorer "Total Filtrado" per currency (owner request, 2026-11-12, uncommitted)

Before: the explorer summed the **visible page only**, every currency together, under an "AOA" label (the server's
`FilteredTotal` existed but used the old currency rule and was not shown). Now:

- `RequestFilteredTotals` (Domain, pure + EF-translatable expressions): the row rules — approved snapshot with
  `ApprovedCurrencyCode`, else selected quotation, else estimate with the request currency; null currency bucketed as "AOA" like
  the rows; cancelled / rejected requests excluded; a `MULTIPLE` approval contributes each component once in its own currency from
  its live groups (`ActiveApprovedTotals`: cancelled groups and non-approved batches excluded), never its summed scalar.
- `GetRequests` groups single-currency requests **in the database** over the whole filtered scope (all pages) and reduces only the
  `MULTIPLE` approvals in memory; `DashboardSummaryDto.FilteredTotalsByCurrency` is the new field. `FilteredCurrencyCodes` /
  `FilteredTotal` (home KPI, which shows "Múltiplas moedas" for > 1 currency) are now derived from the same buckets. The MTD trend
  computation is untouched (still the old rule; it only feeds the home KPI trend label).
- Explorer footer (inline and floating): one token per currency, "AOA 100.000,00 · RND 28.290,00", tokens wrap at narrow widths,
  tabular digits, "0,00" when nothing matches; recomputed with every data load (filters, search, type tab, sort, page).
- Tests: `RequestsExplorerFilteredTotalsTests` (12: merge rules — one currency, several, multi-currency request with a cancelled
  group, null currency, empty; SQL Server translation via `ToQueryString` with a no-subquery-inside-SUM shape guard; endpoint —
  one currency, several, multi-currency request, pagination identical on every page, cancelled request excluded, empty results),
  `RequestsExplorerFilteredTotalsRelationalTests` (1, EXECUTED on the LocalDB sandbox, see 10.6) and
  `src/lib/requestsFilteredTotals.test.ts` (5).

### 10.6 DEV blocker after the first rebuild — SQL Server error 130 (2026-11-12, fixed, uncommitted)

**Symptom:** after rebuilding and restarting DEV the explorer loaded nothing; `GET /api/v1/requests` threw SqlException 130
"Cannot perform an aggregate function on an expression containing an aggregate or a subquery" at the `ToListAsync` of the
per-currency totals.

**Root cause (confirmed from the executed SQL on LocalDB):** EF Core pushed the currency CASE into a derived table for the
`GROUP BY`, but kept the amount CASE inline inside `SUM(...)`, and that CASE contained the correlated selected-quotation
subquery `(SELECT TOP(1) [q0].[TotalAmount] FROM [Quotations] …)`. SQL Server does not allow a subquery inside an aggregate.
`ToQueryString()` compiles such a statement without complaint and the InMemory provider evaluates it on the client, so the
previous checks could not catch it.

**Fix:** `RequestFilteredTotals.GroupSingleCurrency` brings the selected quotation in with `SelectMany … DefaultIfEmpty` (a
`LEFT JOIN [Quotations] … ON [r].[SelectedQuotationId] = [q].[Id]`), so the summed expression references columns only. Rules
unchanged: whole filtered scope, one bucket per currency, cancelled / rejected excluded, `MULTIPLE` approvals reduced from live
groups, a dangling `SelectedQuotationId` counts 0 like the row. No request entities are loaded for the totals (only the few
`MULTIPLE` approvals with their groups).

**Regression evidence:** `RequestsExplorerFilteredTotalsRelationalTests` seeds five ZZTEST requests on
`Portal-Gerencial-IntegrationTests` (approved snapshot RND while the request currency is AOA, selected quotation USD with an
ignored estimate, plain AOA estimate, `MULTIPLE` approval with a cancelled group, cancelled request) and EXECUTES the grouped
query: it reproduced error 130 against the previous query and passes with the fix (AOA 100 100 · RND 11 274,23 · USD 700).
Backend filter `RequestsExplorer|RequestLotTimelineBuilderTests|SupplierCredit` → 129 passed.

**API verification through `GET /api/v1/requests` on the restarted DEV API (build of 2026-11-12 15:24, DEV harness token):**

| Call | Result |
|---|---|
| no filters, page 1/20 | 200 · 642 requests · AOA · EUR · RND · USD as four separate buckets |
| search by number / company / plant / status / type (Cotações, Pagamentos) | 200 each, buckets change with the scope |
| company scope, pages 1, 2 and 96 of 5 | identical totals on every page (477 requests) |
| search with no match | 200 · 0 items · no buckets (frontend prints 0,00) |
| status CANCELLED only (47) and CANCELLED + REJECTED (92) | 200 · items listed · no buckets (contribute nothing) |

Note: search by supplier name ("CONSULTIT") returned 0 rows — the search field does not cover the supplier name (pre-existing
behaviour, not part of this change).

**Frontend:** a failed `GET /requests` now shows "Falha ao carregar os pedidos" with the server message and a "Tentar
novamente" button that re-runs the same filtered request; the table and "Nenhum pedido encontrado" are reserved for a successful
response, and the footer total shows "—" while the list is unavailable.

**DEV services:** restarted by the agent on 2026-11-12 at 15:24 via `execution/restart_services.ps1` after the peer session
(HR bulk-mapping) confirmed no in-flight build or test; API PID 60836, Vite up; HR / Viana 3 / dev-unlock files untouched.

### 10.7 DEV round 2 closure (2026-11-12, uncommitted)

Evidence classes used below: **[Manual]** = the owner's visual confirmation on the DEV screens; **[Automated]** = InMemory test on the
real endpoints / services; **[SQL Server]** = test EXECUTED on the LocalDB sandbox `Portal-Gerencial-IntegrationTests` (raw-SQL
locks, filtered unique indexes, concurrency); **[Live API]** = HTTP call against the restarted DEV API (10.6). After the error-130
lesson, SQL compilation and InMemory are never cited as proof of relational behaviour.

#### 10.7.1 Owner confirmations of the corrected screens [Manual, 2026-11-12]

| # | Confirmed | Scenario |
|---|---|---|
| 1 | Explorer rows show the correct currencies | 647 / 684 / 685 RND, 688 AOA |
| 2 | "Total Filtrado" shows one total per currency and stays identical across the pages of the same filtered scope | explorer |
| 3 | Filtering cancelled requests prints "-----" per row and "0,00" as total | explorer, CANCELLED filter |
| 4 | Request 684: Pagamento completed, Recebimento / Execução the only current stage | ECF10 2026/9634 |
| 5 | Supplier-account movement amounts readable, no broken numeric wrapping | ficha CONSULTIT / RND supplier |
| 6 | Credit-history label in Portuguese | request history |
| 7 | "Confirmar Adiantamento" shows **11 274,23 RND** | REQ-647 reopened advance |
| 8 | "Confirmar Liquidação" shows **5 000,00 RND** for the new CSM Bearings post-paid P.O. **ECF10 2026/9640** — display only, **not** a settlement | Finance queue |
| 9 | New-P.O. wizard, ECF10 2026/9637, 5 000,00 RND, 100 % Antecipado: no credit → 5 000,00 RND bank advance; full credit → fully covered, no bank advance; partial 2 500,00 → 2 500,00 credit + 2 500,00 bank; warning and summary agree — presentation only, **no** P.O. registered | wizard |

#### 10.7.2 Remaining Stage 1 checks — reconciliation

| Check | Already passed before this closure | Executed now (new evidence) | Result |
|---|---|---|---|
| Insufficient credit | [Automated] service 409 `INSUFFICIENT_SUPPLIER_CREDIT`, policy `DecideCoverage`, eligibility `MaxCoverage` | [SQL Server] `Reserve_executes_the_SQL_Server_lock_path_and_refuses_insufficient_credit_without_writing`: the `UPDLOCK` reads run, a 500 hold persists on a 600 account, the next 500 is refused 409 inside its transaction, nothing written (1 hold, 1 entry, figures 600 / 500 / 100, eligibility `MaxCoverage` 100) | Pass |
| Concurrent reservations, no over-reservation | nothing | [SQL Server] `Two_concurrent_reservations_on_one_account_never_over_reserve`: two transactions reserve 500 each on a 600 account at the same instant → exactly one hold, loser 409 `INSUFFICIENT_SUPPLIER_CREDIT`, Σ holds 500 ≤ 600. `Two_concurrent_registrations_with_credit_of_the_same_group_produce_exactly_one_reservation`: two `register-po` with credit on the SAME group through the real controller → one 200, one 409 `CREDIT_RESERVATION_CONFLICT` ("Grupo alterado entretanto", status re-read under the group lock), one hold, one set of owed rows (bank 200 + credit 300), one history line | Pass |
| Authorization (management, reservation, application, reversal) | [Automated] other Buyer 403 on `register-po` with credit; reversal self 403 / SysAdmin-without-Finance 403 / short reason 400; `PoGroupBuyerAuthorization` role → scope → ownership | [Automated] `Credit_account_management_is_Contracts_only_and_reads_follow_the_ficha_capabilities`: Finance and the request-scoped Buyer read (`canManage:false`) and get 403 on account creation and on entries; Contracts creates the account (CCS number), records the credit, duplicate key 409. `Release_endpoint_is_for_the_responsible_buyer_on_a_parked_group_only`: other Buyer 403, short reason 400, `PO_ISSUED` 409, second release 409. Application: Finance role attribute + canonical request scope (unchanged) | Pass |
| Duplicate / concurrent settlement | [Automated] second `apply-credit` refused (service 409, endpoint 400 by status); owner scenario 8 | [SQL Server] `Two_concurrent_apply_credit_calls_debit_the_account_exactly_once_and_the_idempotency_index_holds`: two `apply-credit` on a fully covered group at the same instant → one 200, one refusal (409 `CREDIT_ALREADY_APPLIED` / 400 by status), exactly one APPLICATION entry, one COMPLETED credit row, reservation APPLIED, figures 100 / 0 / 100; a third call still writes nothing; a raw duplicate APPLY row is rejected by `UX_SupplierCreditEntries_IdempotencyKey` (SqlException 2601/2627) | Pass |
| Bank payment blocked on full coverage | [Automated] `schedule` 409 `CREDIT_APPLICATION_REQUIRED` | [Automated] `Bank_endpoints_refuse_a_fully_covered_group_pay_schedule_advance_and_confirm_advance`: `finance/pay` 409; 100 % Antecipado `b2p/schedule-advance` 409 and `b2p/confirm-advance` 409; `apply-credit` then settles the advance → WAITING_SUPPLIER_DELIVERY and a later `confirm-advance` is 400 | Pass |
| Reversal refused for partial credit + bank payment | [Automated] service `CREDIT_REVERSAL_PARTIAL_UNSUPPORTED` | [Automated] `ReverseCredit_endpoint_refuses_a_settlement_that_combined_partial_credit_and_a_bank_payment`: 350 credit + 150 bank paid, Finance 2 reverse → 409; reservation stays APPLIED, no REVERSAL entry, both rows stay COMPLETED, nothing reopened | Pass (stage 1 boundary) |
| `NewReservationsEnabled=false` | [Automated] service KEEP (flag on) / CHANGE flag-off 400 / REMOVE flag-off; `register-po` new reservation 400; owner scenario 8 (existing hold settled) | [Automated] `Flag_off_keeps_removes_releases_and_settles_existing_holds_but_refuses_a_changed_reservation` on the real endpoints with the flag off: KEEP 200 (hold unchanged, rows recreated once, no ledger entry), CHANGE 400 `SUPPLIER_CREDIT_DISABLED` with nothing written, REMOVE 200 (released, coverage 0), `apply-credit` 200 (one debit), group cancellation releases the hold; release endpoint 200 on a parked group | Pass |
| Account isolation (supplier × company × currency), Viana 1 / Viana 2 shared | [Manual] owner scenario 9 (Viana 1 and 2 on CCS-000004) | [SQL Server] `Accounts_are_isolated_by_supplier_company_and_currency_and_shared_by_the_plants_of_one_company`: duplicate key rejected by `UX_SupplierCreditAccounts_Supplier_Company_Currency`; ledger service 409 `CREDIT_ACCOUNT_EXISTS`; USD and company-B accounts coexist (CCS number allocated under `UPDLOCK`); plants A1 and A2 resolve the same account, plant B1 its own; a hold from A1 lowers A2's availability (600 → 100) and leaves company B untouched (100) | Pass |
| Correction-wizard advance wording (Manter / Alterar / Remover) | [Automated] `advanceObligationNotice.test.ts` (KEEP keeps the hold, CHANGE the new value, REMOVE none); `CorrectPoModal` calls the same `describeAdvanceObligation` as the new-P.O. wizard the owner confirmed (#9); `SupplierCreditCoverageBlock` sets the amount to the current hold on KEEP | nothing new needed | Covered |

#### 10.7.3 New automated evidence

- `tests/backend/…/Services/Finance/SupplierCreditStage1ClosureEndpointTests.cs` — 5 tests [Automated].
- `tests/backend/…/Services/Finance/SupplierCreditRelationalTests.cs` — 5 tests [SQL Server], `[Collection("IntegrationTests")]`,
  tagged `ZZT-SCR-*` seeds removed in `finally` (sandbox verified empty of residue after the run).
- `SupplierCreditWorkflowEndpointTests` helpers made `internal` (plus a role / flag parameter) so the closure suite reuses them;
  no behaviour change.
- Run 2026-11-12: filter `SupplierCredit|FinanceSupplierCredit|RequestsExplorer|RequestLotTimelineBuilderTests|PoGroupCancellation|SupplierCapabilityEvaluator`
  → **217 passed, 0 failed**.

#### 10.7.4 Observations (no code change)

- No defect found; no fix was needed in DEV, so no rebuild / restart was required (API of 15:24 still current).
- `ReserveOrCorrectAsync` reads the group's live reservation (`UPDLOCK`) **before** taking the group lock, while `ApplyStageAsync`
  follows the documented order (group → reservation). Through the API this is harmless: the registration transaction changes the
  group status and the status re-read under the group lock refuses the loser (proven by the same-group test). Noted for the
  stage 2 hardening list, not changed now.
- The LocalDB sandbox runs with READ_COMMITTED_SNAPSHOT on; the `UPDLOCK` hints block as intended there too.
- Kept separate, not expanded here: supplier-name search in the explorer (10.6) and the home "vs mês anterior" amount rule (10.5).

#### 10.7.5 Genuinely pending manual steps (visual only)

| Step | Exact action | Scenario |
|---|---|---|
| Finance queue readability | Finanças → Obrigações & Pagamentos at 1 440 / 1 100 / 800 px: amount never wraps, credit chip and next action readable | obligations of 684 / 685 / ECF10 2026/9640 |
| Finanças → Histórico and Print View labels | open Finanças → Histórico for REQ-647 and the Print View of REQ-647: "CRÉDITO APLICADO" / "CRÉDITO REVERTIDO" in Portuguese, raw code only on hover | REQ-27/10/2026-647 |
| Load-failure panel (optional) | stop the DEV API, open the explorer: "Falha ao carregar os pedidos" + "Tentar novamente"; restart, press the button, list returns | explorer |
| No 500 / console errors during the round | note in the acceptance | whole round |

#### 10.7.6 Verdict

Stage 1 DEV validation is **complete for every functional check** of the checklist (manual, automated or executed on SQL Server as
listed in 10.7.2). Only the visual steps of 10.7.5 remain, none of which blocks the TEST publication plan. No blocker is open.
Nothing was committed, no version bumped, no TEST / PROD change.

### 10.8 Final DEV acceptance (2026-11-13) and the credit tooltip clarification (uncommitted)

**Visual steps of 10.7.5 — owner confirmations [Manual, 2026-11-13]:** Finance queue for 684, 685 and REQ-690 / ECF10 2026/9640
readable at maximised desktop, 1 100 px and 800 px (amounts, credit chips, states, menus and the active scheduling button, no
overlap); Finanças → Histórico for REQ-647 shows "Crédito reservado", "Crédito aplicado" and "Crédito revertido"; the exported
Print View PDF of REQ-647 shows the same Portuguese labels; no console errors and no HTTP 500 during the round. The optional
API-unavailability visual test (retest guide 2.1-8) was **not performed** and is **not** marked as passed; the panel remains covered
only by code review. The explorer search once refreshed late; the owner attributes it to laptop load — **no application defect is
recorded** and no performance investigation is opened under this task.

**Tooltip finding — label ambiguity, not a financial inconsistency.** On the settled group of 684 (15 840,00 RND, credit
10 000,00, bank advance 5 840,00 paid) the row shows RND 5 840,00 and the credit tooltip showed "Valor a pagar (banco): RND 0,00".
Traced: the figure is the server's `bankPayableAmount` = group total − live coverage − completed cash
(`FinanceObligationProjectionBuilder`, never negative) — i.e. the bank amount **still owed**, which is correctly 0 after the 5 840,00
payment; the row amount is the obligation itself. Figures verified consistent for four shapes in the unit tests: a **synthetic**
pending partial coverage using the amounts of 685 as they were before its settlement (paid 0, pending 2 450,00), the settled
partial of 684 (paid 5 840,00, pending 0), a full coverage (no bank instalment at all), and a reversed full coverage (reopened
obligation pending in full, then paid in full). **No calculation changed.**

Correction (2026-11-13, owner review): the "685 before payment" case is a synthetic fixture of the automated test, **not** the
live DEV data. Read-only check of the DEV clone on 2026-11-13: REQ-10/11/2026-685 is `PAYMENT_COMPLETED`, 12 450,00 RND,
Pós-Pago, reservation APPLIED 10 000,00, rows `SUPPLIER_CREDIT` COMPLETED 10 000,00 + `FINAL_BALANCE` COMPLETED 2 450,00 —
exactly the owner's scenario (credit 10 000 + bank 2 450, then awaiting receipt). Its tooltip now reads "Pago por banco: RND
2 450,00 · Saldo pendente (banco): RND 0,00". REQ-10/11/2026-684 is `WAITING_SUPPLIER_DELIVERY`, 15 840,00 RND, 100 % Antecipado,
APPLIED 10 000,00, rows `SUPPLIER_CREDIT` COMPLETED 10 000,00 + `ADVANCE` COMPLETED 5 840,00. No divergence from the recorded
round-1 results.

**Fix (frontend only, DEV, no restart needed — Vite reload):** `financePaymentsView.resolveCreditChip` now also returns
`bankPaid` = total − live coverage − `bankPayable` (derived only when the server supplied `bankPayableAmount`; 0 otherwise) and
`noBankPart` (live coverage ≥ total). The queue tooltip prints **"Pago por banco: …"** (when > 0) and **"Saldo pendente (banco): …"**
instead of "Valor a pagar (banco)", or **"Parcela bancária: nenhuma (cobertura integral)"** for a full coverage; the chip text
"· banco …" on pending rows is unchanged (still the pending bank part). The "Aplicar crédito" modal line was renamed to
"Saldo pendente (banco)" for the same reason (it is always 0 there, because apply-credit is only allowed when no bank part is
pending). Files: `src/lib/financePaymentsView.ts`, `pages/Finance/FinancePaymentsList.tsx`,
`components/modals/FinanceActionModal.tsx`; tests `src/lib/financePaymentsView.test.mjs` (+6: 684, 685, full coverage both
statuses, reversed then paid, fallback without a server figure, no chip without credit) — run with
`node --experimental-strip-types src/lib/financePaymentsView.test.mjs` from `src/frontend`; `tsc -b` clean.

**Tracked explicitly:** the owner has to glance at the 684 tooltip once after the Vite reload ("Pago por banco: RND 5 840,00 ·
Saldo pendente (banco): RND 0,00") — a presentation check, not a blocker.

**Acceptance:** Stage 1 DEV is **accepted by the owner** (functional checks 10.7.2 + visual checks above), with the tooltip
clarification tracked here. Next step: the TEST deployment plan
[SUPPLIER_CREDIT_TEST_DEPLOYMENT_PLAN.md](SUPPLIER_CREDIT_TEST_DEPLOYMENT_PLAN.md) (for review; nothing executed).

### 10.9 Isolated release-package validation (2026-11-13, plan revision 2, nothing committed)

Owner decisions recorded: v2.250.0; selective hunks for the shared files; committed flag `false`; no new guided tour.

- Package: disposable local worktree `C:\dev\alpla-credit-release`, local branch `release-prep/supplier-credit-v2.250.0` from
  `3d57d95` (not pushed); 50 modified + 38 new files = the supplier-credit release only (plan §1), the three shared files
  reduced to their credit hunks (plan §2), `appsettings.json` → `NewReservationsEnabled: false`, plus the read-only migration
  preflight (`scripts/db/preflight-migrations.ps1`, `.github/workflows/preflight-migrations-test.yml`, plan §3). The active
  workspace, its branch and the peers' HR / unlock files were not touched; the main-tree index was empty before and after.
- Results on the package: backend Release build 0 errors; frontend `tsc -b` clean; `vitest run` 3 failed (source-level
  assertions in `requestWorkflowSnapshot`, `workspaceQuotationActions`, `v2458ClassificationFlow`) — the same 3 fail on the
  untouched base `3d57d95`, run there with the same `node_modules` → pre-existing; `financePaymentsView.test.mjs` 104 passed;
  Vite production build OK.
- Backend full suite on the untouched base: 3 382 tests, 3 381 passed, 1 failed
  (`PoDocumentSupersessionMigrationRehearsalTests…` expects newest migration `20261029140552`, finds `20261105163725` — stale
  expectation, pre-existing). Backend full suite on the package: 3 466 tests, 3 465 passed, 1 failed (the same
  pre-existing case), 0 skipped; `SupplierCreditRelationalTests` 5/5, `RequestsExplorerFilteredTotalsRelationalTests` 1/1 and
  `PoGroupCancellationRelationalTests` 7/7 executed on the LocalDB sandbox.
- Preflight script validated locally in four paths (PASS on the Development clone with nothing expected; FAIL when the credit
  migration is expected there, FAIL on a wrong database identity, FAIL on the history-less sandbox); the TEST run is not executed.
- 685 wording corrected (10.8): the "before payment" case is a synthetic unit-test fixture; the live DEV 685 is settled.
- Plan revision 3 (owner review of revision 2): two focused checks added and run on the main tree —
  `SupplierCreditPolicyTests.Fixture_F4_refusal_is_insufficient_credit_not_the_po_limit` (4/4: 400 000 on a 400 000 group with
  300 000 available → `INSUFFICIENT_SUPPLIER_CREDIT`; the old 120 000 / 350 000 combination → `CREDIT_AMOUNT_INVALID`) and
  `SupplierCreditStage1ClosureEndpointTests.Reversed_full_coverage_groups_reopen_into_their_bank_status_and_can_then_be_cancelled`
  (2/2: Pós-Pago reopens `PO_ISSUED` + `FINAL_BALANCE` PLANNED, 100 % Antecipado reopens `ADVANCE_PAYMENT_REQUIRED` + `ADVANCE`
  PLANNED, both cancellable afterwards, ledger CREDIT_IN / APPLICATION / REVERSAL, figures restored). Validation snapshot of the
  package: tree `5b32586a830103ec7de411eeba7e70bd0bd353fd` (worktree index, 2 424 entries, 50 M + 39 A); its patch applies to the
  main-tree index in dry-run (`git apply --cached --check` OK, working files untouched).
