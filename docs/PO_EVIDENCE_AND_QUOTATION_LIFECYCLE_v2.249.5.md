# P.O. evidence preservation, saved-quotation lifecycle context and per-item "Menor valor" — v2.249.5

> Findings of the manual TEST validation of **v2.249.4 (`6cf7ba7`)** on `REQ-28/10/2026-628` (2026-10-30), investigated and fixed
> locally on `Portal-Gerencial-rev1`. Companion of `docs/PO_DOCUMENT_VALIDATION_v2.249.4.md` (document identification, correction
> number, supersession) and `docs/PO_GROUP_CANCELLATION.md` (DEC-151). Decision record: DEC-153. **No migration.** Not verified in a
> browser by the agent; manual retest steps in `docs/PO_GROUP_CANCELLATION_TEST_SCRIPT_v2.249.0.md` §3.10.

## 1. Findings, root causes, fixes

| # | Finding (TEST, REQ-628) | Root cause | Fix |
|---|---|---|---|
| 1 | The correction wizard let the operator **delete the previously registered P.O. document** ("Documento removido" 30/10 08:11, same file name re-uploaded 08:13). | `CorrectPoModal` still offered **"Remover P.O atual"** (`api.attachments.delete(existingPo.id)`); `DELETE /api/v1/attachments/{id}` gated P.O. files only by **status** (request `APPROVED`/`WAITING_PO_CORRECTION` or owning group `WAITING_PO_CORRECTION`) and had **no evidence rule** for P.O. files (only PROFORMA/QUOTATION had `QuotationSubmissionLock`). A group returned for correction therefore exposed its registered P.O. to deletion. | Server: `PoEvidenceProtection` (Domain, pure) + `409 PO_EVIDENCE_PROTECTED` in the delete endpoint for any registered P.O. evidence; details DTO flag `isPoEvidence`. Client: remove button gone (replacement = supersession by the correction), both wizards clean up **only** their temporary unregistered upload, attachment list never offers a deletion the server refuses. §2. |
| 2 | **"Cotações salvas"** listed TREVOTECH (awarded, P.O. cancelled, item 1 re-quoted with MAQSIS) like any open quotation — no lifecycle context. | The details DTO carried reuse eligibility per item (`isReuseBlocked`) but **no adjudication history**; the card had no concept of "historical". | `QuotationAdjudicationProjection` (Domain, pure) over batches (items + candidates), lines and the reuse rule → `lifecycleStatus` per quotation and `adjudicationStatus` / `lineCurrentState` per item; chips + "SITUAÇÃO" column + Print View "Situação". §3. |
| 3 | **"MENOR VALOR"** compared the total of a 1-item re-quotation with the total of a cancelled 2-item adjudication. | Card-level `lowestByCurrency` over `totalAmount` in `RequestQuotations`, `BuyerItemsList` and the legacy `ApprovalDetailPanel` path, regardless of line scope, quantities, units or history. | `lib/quotationComparison.ts`: comparison **per request line** on the unit price among **current** candidates with the same currency and unit; card-level claim only on **identical scope**; otherwise a per-item indicator and a scope notice. Approver rules (`WizardStepAward`) untouched. §4. |
| 4 | Outstanding manual validation (quotation rejected as P.O., ECF recognition, duplicate / mismatch, previous review label, AP cancellation setting). | Not demonstrated in TEST yet (REQ-628 MAQSIS is now `PO_ISSUED` with the correct number — no longer awaiting correction). | Local automated coverage mapped in §6; manual checklist §3.10 of the test script uses **another synthetic request** with explicit state preparation. |

## 2. P.O. evidence preservation

### 2.1 What happened in TEST (read from the code paths, not from TEST data)

The "Documento removido" entry is the **soft delete** of the delete endpoint: `RequestAttachment.IsDeleted = true`, history row
`DOCUMENTO REMOVIDO`, request `UpdatedAtUtc` touched. Nothing else is written: the row keeps its `RequestPoGroupId`, `FileHash`,
`StorageReference`, `UploadedAtUtc`; the **bytes stay on disk** (no file I/O on delete); the row disappears from every list that filters
`!IsDeleted` and the download endpoint answers **404** for a deleted row. The group's `CurrentPoAttachmentId` (v2.249.4) was not
touched; if it pointed at the deleted row the pointer was **stale** until the correction registered the re-uploaded file (same
file name, new `RequestAttachment` id, new hash row if re-extracted) and `RegisterPo` pointed the group at it. Because the deleted
row is excluded from `PoDocumentSelection` candidates and from the supersession stamping, it was **neither superseded nor voided**: it
is simply hidden. Recovery (if the owner wants the record visible again) is a scoped data operation — a read-only diagnostic first:

```sql
-- READ-ONLY diagnostic (TEST operator): hidden P.O. files of the MAQSIS group of REQ-628
SELECT a.Id, a.FileName, a.FileHash, a.UploadedAtUtc, a.IsDeleted, a.SupersededAtUtc, a.VoidedAtUtc, g.CurrentPoAttachmentId
FROM RequestAttachments a JOIN RequestPoGroups g ON g.Id = a.RequestPoGroupId
WHERE a.RequestId = 'a0eba2c8-1155-425a-b5a6-d86f0f8fee04' AND a.AttachmentTypeCode = 'PO' ORDER BY a.UploadedAtUtc;
```

The agent did **not** run it and did **not** change TEST data. Un-hiding the row (`IsDeleted = 0` + `SupersededAtUtc/By` = the
re-registration) is a separate, explicitly authorized repair; it is not needed for the workflow (the group is `PO_ISSUED` with the
re-uploaded file as current document).

### 2.2 The rule (`PoEvidenceProtection`, Domain, pure)

A P.O. file is **registered evidence** — never deletable, soft or hard, whatever the request/group status — when any of:

| Reason code | Condition |
|---|---|
| `PO_EVIDENCE_VOIDED` | `VoidedAtUtc` set (DEC-150 annulment evidence). |
| `PO_EVIDENCE_SUPERSEDED` | `SupersededAtUtc` set (replaced by a correction; history). |
| `PO_EVIDENCE_CURRENT` | it is the owning group's `CurrentPoAttachmentId`. |
| `PO_EVIDENCE_REGISTERED` | **legacy groups without pointer**: a `REGISTER_PO` / `REREGISTER_PO` history row of the request dated at or after the upload (1 s tolerance) whose comment names this group (`GroupId: <8 hex>`), or names **no** group (old rows count for every group — protective default). A file with no group ownership is protected by any registration after its upload. |

Everything else is a **temporary, unregistered upload** (the file a refused or abandoned wizard attempt stored — the upload precedes
`register-po`) and stays deletable **inside the existing status window**, now extended to the owning group `WAITING_PO` (abandoned
first registration in a multi-group request whose scalar is `PO_PARTIALLY_UPLOADED`). The old status rule still runs first (a file
outside every window keeps the former `400 Exclusão Bloqueada`).

Endpoint: `DELETE /api/v1/attachments/{id}` → `409 ProblemDetails { code: "PO_EVIDENCE_PROTECTED", reasonCode }`, nothing written,
no `DOCUMENTO REMOVIDO` row. Details DTO: `RequestAttachmentDto.isPoEvidence` (`true`/`false` for P.O. files, `null` otherwise)
computed by the same rule.

### 2.3 Behaviour across the flows

| Flow | Before (v2.249.4) | Now |
|---|---|---|
| Registration (`RegisterPoModal`) | upload → register; on cancel after a refused attempt the uploaded file stayed as an orphan "P.O." file. | upload → register; **clear file** and **Cancelar** call `cleanupTemporaryPoUpload` (best effort, never blocks) on the attempt's own upload id; the id is cleared on success so nothing registered is ever targeted. |
| Correction (`CorrectPoModal`) | "Remover P.O atual" soft-deleted the registered file; cancel left orphans. | No remove action. Label: *"P.O atual (devolvida) — nº … · ficará como histórico (substituída) após a correção; não é removida"*; download kept. Same temporary-upload cleanup as registration. Replacement = `RegisterPo` supersession (v2.249.4). |
| Cancellation (DEC-151) | voids the files (`VoidedAtUtc`). | unchanged; voided files are protected by the rule. |
| Attachment list (`RequestAttachments`) | "Remover" for P.O. when request `APPROVED`. | "Remover" only when `isPoEvidence === false` (and not current / superseded / voided); an unknown flag never offers it. |
| Direct API `DELETE` | status gate only. | status gate + evidence rule (`409`). |

Limitations: the legacy heuristic is protective by design — a temporary file uploaded **before** a later registration of the same group
(old client, abandoned attempt never cleaned) becomes protected too (false positive = a file kept, never a file lost). The cleanup is
best effort: a network failure leaves an unregistered file the server still reports as deletable (`isPoEvidence: false`), removable
from the attachment list. Hidden rows deleted before v2.249.5 are not un-hidden automatically (§2.1).

## 3. Saved-quotation lifecycle context (`QuotationAdjudicationProjection`, Domain, pure)

Inputs: saved quotations and items (`MappedRequestLineItemId`, `ReconciliationStatus`), approval batches with **items + candidates**
(`SelectedQuotationItemId`, `ReleasedAtUtc`, `ReleaseKind`), request lines (`QuotationLifecycleStatus`, `SelectedQuotationItemId`), the
DEC-147/D3 reuse rule (`QuotationReuseEligibilityPolicy`). The details endpoint now loads `ApprovalBatches.Items.Candidates`
(split query) and the request's reuse authorizations; nothing is persisted.

Per item (`SavedQuotationItemDto.adjudicationStatus`, most recent reference decides; an **active** award anywhere wins):

| Status | Meaning |
|---|---|
| `NOT_QUOTED` | NOT_QUOTED / IGNORED placeholder — never a candidate (UI: "Não cotado", never "Ignorada"). |
| `NOT_SUBMITTED` | never a candidate of any batch. |
| `IN_APPROVAL` | candidate/winner of a batch not yet decided. |
| `AWARDED` | winner of an APPROVED batch item that was **not** released. |
| `CANDIDATE_NOT_AWARDED` | candidate of a decided batch item, another item won. |
| `AWARD_CANCELLED` | winner of a released item (P.O.-group cancellation: `adjudicationReleaseKind` = RESTART_QUOTATION / CANCEL_ITEMS) or of a CANCELLED batch. |
| `CANDIDATE_CANCELLED` | candidate (not winner) of such an item/batch. |
| `REJECTED` | last reference is a REJECTED batch. |

`lineCurrentState` for the historical statuses: `REQUOTED_WITH_OTHER` (+ `lineCurrentSupplierName`: the line's current winner is an
actively awarded item of another quotation), `PENDING_REQUOTE`, `CLOSED_AFTER_APPROVAL`; `AWARDED_HERE` for `AWARDED`. The
**current** reuse eligibility stays in `isReuseBlocked` / `isReuseAuthorized` (unchanged) and is rendered as a separate sentence.

Per quotation (`lifecycleStatus`): `HISTORICAL_CANCELLED` (every quoted item historical), `PARTIALLY_HISTORICAL`, `AWARDED`,
`IN_APPROVAL`, `REJECTED`, `NOT_AWARDED`, `OPEN`; plus `activeAwardedItemCount`, `historicalItemCount`, `historicalBatchNumber`,
`historicalReleaseKind`. A cancellation of **one** item's award never classifies the whole quotation.

UI (`lib/quotationLifecycle.ts`): card chip + one-line detail (e.g. *"Histórica — adjudicação cancelada"* / *"Cotação adjudicada (lote
1); a P.O. foi cancelada e os itens foram devolvidos à cotação. Mantida como histórico; não é uma opção atual."*), historical card
dashed/greyed with the total struck through and **no "Selecionar"**; expanded rows get a **SITUAÇÃO** column (*"Adjudicação cancelada
(lote 1)"* + *"Item devolvido à cotação e recotado com MAQSIS. Reutilização sujeita a autorização."*); Print View field **Situação**.

## 4. "Menor valor" per request item (`lib/quotationComparison.ts`)

- Candidates: items `MAPPED`/`SUBSTITUTE` with a request line, **not** historical (`AWARD_CANCELLED`/`CANDIDATE_CANCELLED`, quotation
  `HISTORICAL_CANCELLED`) and **not** reuse-blocked. Historical / unmapped items are never compared.
- Per line: unit price among candidates sharing **currency + unit**; strictly lowest → `LOWEST`, shared → `TIE_LOWEST`, else `HIGHER`;
  a single candidate → `ONLY_CANDIDATE`; different currency/unit → `NOT_COMPARABLE`. Different quantities do not block the unit-price
  comparison but are flagged (*"Menor preço unit. (qtd. diferente)"*).
- Card: **"Menor valor (moeda)"** only when every current quotation covers the **identical** set of lines with identical quantities,
  units and currency (`IDENTICAL_SCOPE`); otherwise *"Menor preço unit. em n de m item(ns)"* and a one-line scope notice (*"As cotações
  cobrem itens ou quantidades diferentes: o total não é comparável…"* / mixed currencies). A single current quotation never claims it.
- Applied in `RequestQuotations` (request page + drawer), `BuyerItemsList` (card border/cue) and the legacy non-candidate path of
  `ApprovalDetailPanel` (`DecisionQuotationCard.isLowest`); candidate batches keep suppressing it; `WizardStepAward` (Area approver's
  per-item award) is untouched.

## 5. Checks (actual results, 2026-10-30, local)

| Check | Result |
|---|---|
| `dotnet build AlplaPortal.Api` | succeeded, 0 errors |
| Backend tests — targeted (`PoEvidenceProtectionTests`, `PoEvidenceDeleteRefusalTests`, `QuotationAdjudicationProjectionTests`, `AttachmentUploadTests`) | 42/42 passed |
| Backend tests — full suite (after the §9 boundary pass) | 3280/3280 passed (2 m 17 s); `PoEvidence*` classes 40/40; relational `PoEvidenceConcurrencyRelationalTests` 3/3 **ran** on LocalDB (11 s / 2 s / 0.2 s, not skipped) |
| vitest (after the §9 boundary pass) | 110 files / 1225 tests passed |
| `tsc --noEmit` | clean |
| `vite build` | built (chunk-size warning pre-existing) |
| EF `has-pending-model-changes` | "No changes have been made to the model since the last migration." (DTO/read-model only) |
| Browser | **not** verified by the agent |

## 6. Finding 4 — local automated coverage vs TEST evidence

| Item | Local automated evidence (exists) | TEST browser evidence |
|---|---|---|
| Quotation rejected as P.O. | `RegisterPoDocumentIdentificationTests.A_document_identified_as_a_quotation_by_the_server_evidence_is_refused_with_every_override`, `…A_PO_file_identical_to_the_requests_quotation_document_is_refused_at_registration_and_at_upload`; `registerPoErrors.test.ts` (`DOCUMENT_NOT_PURCHASE_ORDER`, `DOCUMENT_IS_QUOTATION_EVIDENCE`) | pending — §3.10 step 2 |
| Positive ECF recognition | `PurchaseOrderDocumentIdentifierTests` (ECF11/ECF10/ECF, ECF99 unrecognised); `poDocumentIdentification.test.ts` | pending — §3.10 step 3 |
| Duplicate / mismatch | `…A_duplicate_number_on_a_correction_still_needs_the_duplicate_confirmation_and_justification`, `…A_stale_number_that_differs_from_an_IDENTIFIED_document_is_an_OCR_divergence_under_the_override_policy`; `correctPoSubmission.test.ts` | pending — §3.10 steps 4–5 |
| Previous-review labelling | `PoGroupCancellationServiceTests` (`ResolveReviewChronology`); `poGroupCancellation.test.ts` (`describeFinanceReview`) | pending — §3.10 step 7 |
| AP `NotifyOnPoCancelled` | `AccountsPayableConfigControllerTests`, `PoCancellationNotificationTests`; `apNotificationsForm.test.ts` | pending — §3.10 step 8 |

## 7. Tests added in v2.249.5

Backend: `PoEvidenceProtectionTests` (11), `PoEvidenceDeleteRefusalTests` (8: current pointer in a correction group, superseded in
the APPROVED window, voided, legacy with/without group comment, temporary upload of an abandoned correction → 204, abandoned first
registration in a WAITING_PO group → 204, status window still first), `QuotationAdjudicationProjectionTests` (6: REQ-628 shape,
partial release, reuse authorization separate from history, cancelled batch, in-approval/not-awarded/rejected, placeholders).
Boundary pass (§9): `PoEvidenceConcurrencyTests` (14 — InMemory, two contexts over one store: cross-context re-read, deleted file
named by the client / newest live file, idempotent delete, register-then-delete 409, temporary upload after the last registration in
a pointer-era group, legacy tie/tolerance theory ×4, pointer-era rule, select-from-card unauthorised/authorised/mixed),
`PoEvidenceConcurrencyRelationalTests` (3 — SQL Server LocalDB sandbox, skipped when unavailable: delete waits for the registering
transaction's row lock then 409; register-po waits for the deleting transaction's row lock then 409 `PO_DOCUMENT_CONCURRENT_CHANGE`
with nothing written; real endpoints without contention).
Frontend: `poEvidence.test.ts` (13: offer gate, temporary upload, `isPoEvidenceProtectedRefusal`, kept-vs-failed clean-up, throwing
reporters, no delete after success, delayed clean-up racing a registration), `quotationComparison.test.ts` (15: REQ-628 shape,
identical scope, partial coverage, mixed currencies, units, ties, cancelled/reuse-blocked, unmapped, single, and §9.3: unequal
quantities ×5 — informational only, third candidate at another quantity, firm vs indicative tie, mixed scope, cheaper total at smaller
volume), `quotationLifecycle.test.ts` (9), `quotationSelectability.test.ts` (5: unauthorised, authorised, mixed, placeholders, no item
facts), `poEvidenceAndQuotationLifecycleWiring.test.ts` (11: no remove action, cleanup on clear/cancel and never after success in both
wizards, no unmount/delayed cleanup, refused cleanup logged as "kept", attachment list gate, selection follows eligibility,
comparison/lifecycle wiring, approver wizard untouched, Print View).

## 8. Migration / limitations

No migration (`PoDocumentExtractions`/supersession columns from v2.249.4 are reused; new fields are read-model only). TEST needs only
**Deploy to TEST** for v2.249.5 (Apply TEST Migrations was required for v2.249.4). Limitations: §2.3; the lifecycle projection reads
`ApprovalBatchItem.Candidates`, so batches created before the candidate model (legacy single-winner batches) classify by the winner
only; `lineCurrentState` is `null` when the line has an unusual lifecycle value.

## 9. Boundary cases verified before publication (2026-10-30, local; nothing committed, pushed, deployed; TEST/PROD and REQ-628 untouched)

### 9.1 Evidence deletion versus registration concurrency

**Gap found.** `register-po` selected the P.O. file from a read made **before** its transaction and never wrote that row, so a
`DELETE /attachments/{id}` committed between that read and the commit left the group pointing at a file with `IsDeleted = 1`
(and the delete's own evidence check, made before the registration committed, saw no pointer). Nothing in the model guards the row
(`RequestAttachment` has no `RowVersion`; adding one is a migration and was not chosen).

**Fix (repo lock convention, no migration).** `PoDocumentRowLock` (Infrastructure): `SELECT … FROM [RequestAttachments] WITH
(UPDLOCK, HOLDLOCK) WHERE [Id] = @id` inside the caller's transaction, then `ReloadAsync` of the tracked instance (on non-SQL-Server
providers only the re-read happens).
- `register-po`: right after the prior-ECF guard (already inside `registerTx`) the chosen file is locked and re-read; `IsDeleted`
  / `VoidedAtUtc` / row gone → **409 `PO_DOCUMENT_CONCURRENT_CHANGE`**, transaction disposed → nothing written (no pointer, no
  number, no history row). Supersession of siblings happens after the lock, as before.
- `DELETE` of a `PO` file: the evidence decision and the write now run in **one transaction** (`IsRelational()` only) with the same
  row lock taken **before** `PoEvidenceProtection` is evaluated; a file already soft-deleted by a concurrent clean-up answers 204
  without writing (idempotent).
- Ordering proven on SQL Server (`PoEvidenceConcurrencyRelationalTests`, LocalDB sandbox): (a) lock held by the registering session →
  the delete waits (>2.5 s) → after the registration commits the delete sees the pointer → **409 `PO_EVIDENCE_PROTECTED` /
  `PO_EVIDENCE_CURRENT`**, file kept, no "DOCUMENTO REMOVIDO"; (b) lock held by the deleting session → the real `register-po` waits
  (>2.5 s) → after the soft delete commits it answers **409 `PO_DOCUMENT_CONCURRENT_CHANGE`**, group still `WAITING_PO`, pointer and
  number null, no `REGISTER_PO` row; (c) without contention: register → pointer; delete of the pointer (after a Finance return) → 409.
  The tests skip silently where LocalDB is unavailable (`ALPLA_INTEGRATION_TESTS_REQUIRED=true` makes them fail instead).
- Read-committed-snapshot note: with RCSI the pre-transaction reads see the snapshot, so the in-transaction lock + re-read is what
  closes the window; without RCSI the earliest read may already block and the registration is refused by the pre-checks
  (`PO_ATTACHMENT_INVALID` / "É necessário anexar a P.O"). Either way a registration never commits with a deleted file.

**Temporary upload after the last registration, with older registration history.** Two rules now decide, in this order:
1. **Pointer-era group** (`RequestPoGroup.CurrentPoAttachmentId` set, i.e. registered at least once since v2.249.4): every
   registration made the chosen file the pointer and superseded every other live file, so a live file that is neither the pointer nor
   superseded nor voided was uploaded **after** the last registration → **deletable, whatever the registration timestamps say**
   (`PoEvidenceProtection.Evaluate` returns "not protected" without consulting the history). Proven end to end: register → Finance
   return → new upload → `DELETE` 204, while the registered file still answers 409.
2. **Legacy group (no pointer)**: the timestamp heuristic. `protected ⇔ ∃ registration of the owning group (or of unknown group) with
   AtUtc ≥ UploadedAtUtc − 1 s`. **Ties:** an exact tie is protected; an upload up to 1 s **after** a registration is protected
   (tolerance for the two stamps being taken by different statements of one flow — a protective false positive); an upload more
   than 1 s after the **last** registration of its group is a temporary upload (deletable). A file with no group of its own follows
   the heuristic against every registration of the request. Theory test: 0 s / +1 s / +2 s / −5 s.

### 9.2 Historical quotation versus authorised reuse

The card's "Selecionar" no longer keys on the historical label. `describeQuotationSelectability` (frontend) and a server guard in
`POST …/quotations/{quotationId}/select` apply the **existing D3 eligibility** per item: one considered item that is eligible or whose
reuse is **explicitly authorised** makes the quotation selectable; a quotation whose **every** considered item is reuse-blocked is
refused (**409 `QUOTATION_REUSE_NOT_AUTHORIZED`**, the same code and payload the batch endpoints use). The historical chip, the struck
total and the per-item "Adjudicação cancelada" stay visible in all cases; when selection is blocked the card shows "Seleção sujeita a
autorização de reutilização" (tooltip with the reason). Tested: unauthorised (refused), authorised (selectable; sibling quotation
of the same cancelled batch still refused), mixed historical + current items (selectable), placeholders, legacy payload without item
facts (lifecycle decides). Limitation: the legacy whole-quotation `select` endpoint remains the legacy scalar flow (it rebuilds the
request lines from the winning quotation); it is only reachable at `WAITING_AREA_APPROVAL` and the guard adds eligibility, nothing else.

### 9.3 Quantity differences in lowest-price comparisons

Unequal quantities among the comparable candidates of a line (same currency + unit) now make the **whole line informational**:
status `QUANTITY_DIFFERS` on every candidate, `indicativeLowest` on the lowest unit price, label "Quantidades diferentes — menor preço
unit. apenas indicativo" / "… comparação apenas indicativa", **never** counted in `lowestLineCount`, and the scope notice gains "Há
itens cotados em quantidades diferentes: nesses itens o menor preço unitário é apenas indicativo (o preço pode depender do volume)."
The card chip for such lines is `INDICATIVE` and explicitly qualified; `PER_ITEM` counts only firm lines. The card-level "Menor valor"
already required identical lines, quantities, units and currency (scope key), so a cheaper total at a smaller volume never claims it.
Firm ties at the same quantity stay `TIE_LOWEST`; a tie at different quantities is indicative. Mixed-scope quotations: firm win on the
equal-quantity line, indicative on the other, card counts only the firm one.

### 9.4 Wizard clean-up after a successful registration

Verified in the code (`CorrectPoModal`, `RegisterPoModal`) and pinned by the wiring test: the clean-up is called from exactly two
places — clear-file (X) and **Cancelar** — never from an unmount effect, a timer, or the success path. On success both refs are set to
`null` **before** `onSuccess`, so a later clear/cancel sends nothing. When the registration **succeeded but the client saw a network
error**, the refs still hold the id; a later Cancelar sends the DELETE and the **server guard decides**: the file is the group's pointer
→ `409 PO_EVIDENCE_PROTECTED`. The client now classifies that refusal (`isPoEvidenceProtectedRefusal`) as **"kept as evidence"**:
logged at Information (`FRONTEND_EVENT`), **not** as a failure, with no user-facing error — the registration is neither undone nor
misrepresented. Any other clean-up failure stays a Warning log (an unregistered file may remain; it is the only P.O. file the list
offers to remove). A clean-up started while the file was temporary and answered after a concurrent registration committed (row lock)
takes the same "kept" path.

### 9.5 Checks of the boundary pass (actual results)

See §5 (updated totals). The relational tests ran against the LocalDB sandbox on this machine (durations of several seconds each,
not skipped). Browser verification: **not** performed.
