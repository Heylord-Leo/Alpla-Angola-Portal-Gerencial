# Buyer Workspace — quotation Edit/Delete actions, and the REQ-15/10/2026-578 case

> Released as v2.247.9 (2026-10-16). Companion to `docs/APPROVAL_NOTIFICATIONS_PHASES.md` (which is unrelated to
> this topic and was not changed here).

## 1. The case that motivated this change (PROD, v2.247.8)

REQ-15/10/2026-578 (QUOTATION, WAITING_QUOTATION, two items) showed no "Enviar itens para aprovação" after four
quotations from the same supplier had been imported. Read-only PROD diagnostics (2026-10-16) established:

- both request items active with unchanged ids; every one of the four quotations held two `EXTRA_ITEM` lines with no
  mapping plus two `NOT_QUOTED` placeholders mapped to the items; `ReadyItems = 0`;
- the backend OCR matcher had reported `EXACT_MATCH / DESCRIPTION_EXACT` (0.95) to the correct items for every import;
- the three earlier quotations referenced deleted documents; the latest one an active document.

Coverage rule (unchanged): an item is ready for a batch only when a quotation line has `MappedRequestLineItemId` equal
to the item and `ReconciliationStatus` MAPPED or SUBSTITUTE (`BuyerQueueProjectionBuilder.ClassifyItemCoverage`).
`EXTRA_ITEM`, `NOT_QUOTED` and `IGNORED` never count. The saved shape therefore produced "2 pendentes / 0 tratados" and
hid the submit action, by design of the predicate.

**Resolution (user-reported live evidence, not SQL-verified).** The Buyer edited the latest quotation through the
classic tool (`/buyer/items/classic` → "Editar"), linked the two supplier lines to the two items, and the request was
submitted for approval. No data repair, SQL or deletion was performed by the Portal team; the resulting PROD state was
not re-queried and is not claimed here.

**Separate tracked defect (NOT fixed in this change).** The wizard never consumes the backend reconciliation records
(`matchStatus` / `requesterItemId` are read only by the read-only `ReconciliationPanel`); its own auto-suggest is a raw
`trim().toLowerCase()` equality (`WizardStepReconciliation.tsx`, "Auto-suggest logic on mount") while the backend
normaliser also strips accents, punctuation and repeated spaces; and the request-item panel's only hint for an
uncovered item is "Marque os itens solicitados não cotados antes de avançar." with a single "Não cotado nesta cotação"
button. When the auto-suggest misses, that guidance leads to `EXTRA_ITEM` + `NOT_QUOTED` records that can never become
ready. Proposed fix (to be scheduled on its own): pre-map lines from EXACT_MATCH records (suggest PROBABLE_MATCH),
align the normaliser, reword the hint to link-first, and warn at final review when no item is covered. Hardening:
validate on save that `MappedRequestLineItemId` belongs to the request (currently unchecked).

## 2. What this change adds

"Cotações & Documentos" in the new Buyer Workspace (`/buyer/requests/{id}`) now offers, per quotation row identified by
supplier, document number and date:

- **Editar** — opens the SAME in-place edit the classic tool uses: `useWorkspaceWizardHost.openEditQuotation` fetches
  the request detail, selects the quotation (lines with mapping/status) and calls the shared controller's
  `handleOpenWizard(…, editQuotation)` without a rework batch → `PUT /api/v1/requests/{id}/quotations/{quotationId}`.
  Identity is preserved; the attachment link is preserved unless the Buyer changes the document (the endpoint
  soft-deletes the previous one only when `ProformaAttachmentId` changes); lines are upserted; batch-referenced lines
  cannot be removed (409); history `QUOTATION_UPDATED`. A successful save triggers the existing `onSaved` →
  `loadWorkspace(true)`, so coverage, counts, next actions, quotations and documents refresh without a page reload.
- **Excluir** — confirmation (destructive variant) that states the endpoint's actual behaviour: the structured
  quotation and all its lines are removed definitively; the linked document, when there is one, is marked removed
  automatically (`DOCUMENTO_REMOVIDO`); the action is refused if the quotation is referenced by an approval batch or the
  request left the quotation window; a `QUOTATION_DELETED` history row is written. Then
  `DELETE /api/v1/requests/{id}/quotations/{quotationId}`; success refreshes the projection the same way; the server's
  refusal text is shown in an error banner; one submission at a time (busy guard, disabled buttons).
- Buttons appear under the SAME rule as the classic list (`BuyerItemsList`: `canMutateQuotation = window &&
  group.buyerId === currentUser.id`): request in the quotation window (`DRAFT`, `WAITING_QUOTATION`, `AREA_ADJUSTMENT`,
  `FINAL_ADJUSTMENT`, mirroring `RequestWorkflowHelper.CanMutateQuotation`) AND assigned to the current user, who must
  hold the Buyer role (the Workspace route also admits Local Managers and administrators, the classic route does not).
  Unassigned requests, other Buyers, Local Managers and System Administrators who are not the assigned Buyer see no
  buttons — as in the classic tool. Nothing is bypassed: the backend decides scope, window and batch references.
- Failure handling: delete and edit run one at a time (busy guard, disabled buttons, the wizard's own save lock). A
  server refusal is shown in an error banner with the server's text. A failed silent re-read AFTER a successful
  mutation is reported distinctly ("A ação foi concluída com sucesso, mas não foi possível atualizar o workspace…
  não repita a ação") without replacing the page with the load-error view and without inviting a retry of the mutation.

Files: `pages/Buyer/workspaceQuotationActions.ts` (+ tests), `pages/Buyer/BuyerRequestWorkspace.tsx`,
`pages/Buyer/QuotationWizard/hooks/useWorkspaceWizardHost.ts`.

Guided Tour: the Buyer Request Workspace had no page tour. Per the publish SOP a new page tour
`page-buyer-request-workspace` (route prefix `/buyer/requests`) was created and registered, with stable anchors
(`buyer-workspace-header`, `buyer-workspace-tabs`, `buyer-workspace-quotations`, `buyer-workspace-quotation-actions`,
`buyer-workspace-classic-link`); the actions step explains Editar/Excluir and their restrictions and is skipped
automatically when the buttons are not rendered (other users, other tabs, no quotations).

## 3. Backend guard reviewed — one gap closed

`UpdateQuotation` already checked the access scope, the quotation window (with the rework exception) and
batch-referenced lines. `DeleteQuotation` checked the window and batch references but **not the access scope**: any
authenticated user could delete a quotation of a request outside their company/plant/department scope by id. Added the
same `GetScopedRequestsQuery` check (404, as in PUT); it runs before the quotation is even loaded, so no mutation can
precede it. Authorization model as it stands (unchanged here): `RequestsController` is `[Authorize]` only; POST, PUT and
DELETE quotation endpoints check scope and workflow state but neither the Buyer role nor request ownership — ownership
is enforced only by the UI rule above (classic and Workspace alike). Tightening the backend to Buyer-role/ownership is a
separate decision affecting the classic tool and the API; it is recorded, not implemented. `QuotationDeleteEndpointTests` cover: success side effects
(quotation and lines removed, linked document soft-deleted, `QUOTATION_DELETED` and `DOCUMENTO_REMOVIDO` rows, request
items untouched), no-document variant, window refusal (409, nothing written), batch-reference refusal (409, nothing
written), out-of-scope refusal for DELETE and PUT (404, nothing written) with the in-scope Buyer still allowed, and
unknown ids (404). No migration, no EF model change.

## 4. Checks and limits

Frontend: tsc, Vite build, vitest (new `workspaceQuotationActions.test.ts`: window/role matrix, identification and
confirmation text incl. the document consequence, delete orchestration success / server refusal / re-entry guard,
source wiring guards). Backend: new test class plus the full suite. The frontend suite has no DOM runtime: the
rendered buttons, dialog and refresh are guarded at source level and need the TEST pass below.

## 5. Bounded TEST validation plan (after deployment; no PROD data involved)

On a disposable QUOTATION request in WAITING_QUOTATION with two items and two quotations (one with a document, one
manual, no batch): (1) as a Buyer, open the Workspace tab "Cotações & Documentos" → Editar/Excluir visible per row with
supplier, document and date in the tooltips; (2) Editar on the document quotation → wizard opens in "Editar Cotação"
with the saved lines; change one mapping and save → same quotation id, same attachment, history `QUOTATION_UPDATED`,
coverage and next action updated without reload; cancel the wizard → nothing changes; (3) Excluir on the manual
quotation → confirmation lists no document consequence → `QUOTATION_DELETED` row, quotation gone, counts refreshed;
(4) Excluir on the document quotation → confirmation names the document consequence → document marked removed,
`DOCUMENTO_REMOVIDO` + `QUOTATION_DELETED`; (5) batch-reference refusal — creating a batch moves the request to
WAITING_AREA_APPROVAL, which leaves the quotation window, so the buttons are correctly hidden afterwards; test the
refusal either with a stale confirmation (open the Excluir confirmation in tab A, create the batch from that quotation in
tab B, then confirm in tab A) or with a direct authenticated `DELETE /api/v1/requests/{id}/quotations/{quotationId}`:
expect 409 "Ação Bloqueada" in the banner / response and, by read-only SQL, unchanged `Quotations`, `QuotationItems`,
`RequestAttachments.IsDeleted` and no new `QUOTATION_DELETED` / `DOCUMENTO_REMOVIDO` history rows; (6) visibility:
another Buyer, an unassigned request, a Local Manager or an administrator who is not the assigned Buyer → no buttons;
the assigned Buyer on a request past FINAL_APPROVED → no buttons; (7) a request outside the user's scope → DELETE and
PUT return 404 (API-level check); (8) refresh failure after success is only reproducible by interrupting the API
between the mutation and the re-read; if attempted, the banner must read "A ação foi concluída com sucesso…" and no
retry is to be made. Do not run any of this on REQ-578.

## 6. "Assumir pedido" in the Workspace (working tree, unreleased, 2026-10-16)

**Live evidence (TEST, v2.247.9, user-reported).** REQ-05/10/2026-517 was assigned to Samuel Mpoco; Leonardo Cintra
assumed it through the classic tool; the Workspace then showed Leonardo as Buyer and exposed Editar/Excluir. This
validates the visibility rule after an ownership change (window AND assigned Buyer). It does not validate edit, save or
delete behaviour, and it must not be read as "a Buyer can take over another Buyer's request": Leonardo holds System
Administrator privileges, and the backend allows takeover only to coordinators.

**Backend rules (traced, unchanged).** `POST …/assign-buyer` (`RequestsController.AssignBuyer`): access scope (404);
self-assignment (no `targetUserId`) requires the Buyer ROLE — a System Administrator or Local Manager without it gets
403 even on an unassigned request; a request already assigned to someone else is refused for an ordinary Buyer with
409 ("O reencaminhamento só é permitido por coordenadores") and allowed for System Administrator / Local Manager;
assigning another user is coordinator-only and the target must hold the Buyer role (409 otherwise); idempotent when
already assigned to the actor (200). Audit `COMPRADOR_ATRIBUIDO`, requester in-app notice and the `BuyerAssigned`
e-mail event are the endpoint's. PAYMENT requests take the correction-group path (§G.20.1: claim of an unassigned
WAITING_PO_CORRECTION group, coordinator transfer, rowversion arbitration) — untouched.

**Discrepancy reported, not changed.** The classic list shows "Assumir Pedido" to every non-owner and lets the server
refuse; an ordinary Buyer therefore sees a button that always fails on an assigned request. The backend was not
broadened: ordinary Buyers still cannot take over an assigned request. `QuotationAssignBuyerTests` pins the matrix
(claim + idempotence + audit/event; ordinary-Buyer takeover 409 with nothing written; coordinator+Buyer takeover for
System Administrator and Local Manager; coordinator without Buyer role 403; coordinator reassignment to a Buyer, refusal
for a non-Buyer target, ordinary Buyer assigning others 403; out-of-scope 404).

**Workspace behaviour (new).** The header's "Comprador" field offers, for QUOTATION requests not in a terminal state:
"Assumir pedido" when unassigned and the user has the Buyer role; "Assumir pedido de outro comprador" when assigned to
someone else and the user is a System Administrator or Local Manager WITH the Buyer role — after a confirmation that
names the request and the current Buyer and states the effects (quotation actions move to the new owner; history and
requester notice as in the classic tool). Nothing is offered when the server would refuse (ordinary Buyer on an assigned
request, coordinator without the Buyer role, already mine, PAYMENT, terminal status); the button's tooltip gives the
reason. Submission goes through the existing endpoint (self-assignment only; no reassignment to others from the
Workspace). One submission at a time; server refusals shown verbatim; a successful assignment re-reads the projection
(owner, next actions, quotation Editar/Excluir visibility) and a failed re-read after success is reported with the
"não repita a ação" message. `BuyerWorkspaceDto.RequestTypeCode` was added (read-only) so the UI can exclude PAYMENT.
Guided tour: step "Assumir pedido" added (anchor `buyer-workspace-assign`, skipped when the button is not rendered).

**TEST plan (bounded).** On disposable QUOTATION requests: (1) unassigned, as a Buyer → "Assumir pedido" → owner,
next action and Editar/Excluir appear without reload, history `COMPRADOR_ATRIBUIDO`, requester notice; (2) assigned to
another Buyer, as an ordinary Buyer → no button; direct API call → 409, nothing written; (3) assigned to another Buyer,
as System Administrator + Buyer → "Assumir pedido de outro comprador" → confirmation names the current Buyer → owner
changes, previous owner loses the actions; (4) as System Administrator without the Buyer role → no button; API → 403;
(5) PAYMENT request → no button; (6) cancel the confirmation → nothing changes. Do not use REQ-517 or REQ-578.

## 7. Ordinary Edit/Delete only for quotations EVER submitted for approval — durable marker (working tree, unreleased, 2026-10-16)

**Motivation.** REQ-05/10/2026-517: scalar WAITING_QUOTATION (other items still pending) while a batch was already
"Aguardando P.O."; the Workspace showed Editar/Excluir for the submitted quotation. The classic list hid them through a
client-side check of batch references; `PUT …/quotations/{id}` allowed in-place edits of a submitted quotation as long
as no referenced line was removed. REQ-517 and REQ-578 were not modified.

### 7.1 Rule
A quotation is "submitted" when **either**
- its durable marker is set (`Quotations.FirstSubmittedForApprovalAtUtc` not null), **or**
- any of its lines is still referenced as a selected item (`ApprovalBatchItems.SelectedQuotationItemId`) or as an
  approval candidate (`ApprovalBatchItemCandidates`) of ANY approval batch, whatever the batch status (rejected,
  cancelled, adjustment and approved batches do not unlock), or as the line-level award pointer of a request item
  (`RequestLineItems.SelectedQuotationItemId`, legacy request-level approval).

The decision is per quotation: a never-submitted sibling on the same request stays editable and deletable (partial
submission). Request adjustment states (AREA_ADJUSTMENT / FINAL_ADJUSTMENT) do not unlock an in-place edit: the
explicit revision/rework flow (`SaveQuotation` with `reworkBatchId` / `revisesQuotationId`) creates a NEW quotation and
is untouched by the lock (`ReworkRevisionSaveQuotationTests.G_MarkerLockedOriginal…`). Behaviour change: an in-place PUT
of a submitted quotation during AREA/FINAL_ADJUSTMENT (previously accepted when no referenced line was removed) is now
refused; the revision is the supported path.

### 7.2 Durable marker (`Quotation.FirstSubmittedForApprovalAtUtc`, `FirstSubmittedBatchId`, `FirstSubmittedSource`, `RowVersion`)
- Written by `Infrastructure/Services/Purchasing/QuotationSubmissionStamper`, which runs inside **both**
  `ApplicationDbContext.SaveChanges` overrides before the write is sent — same transaction as the submission evidence,
  so every submission path is covered structurally: CreateBatch candidates, UpdateBatch rework (new candidates),
  extra-item INCLUDE (`BatchExtraItemDecisionService`), approver winner stamp (`ApprovalBatchItem.SelectedQuotationItemId`)
  and the legacy request-level award (`RequestLineItem.SelectedQuotationItemId`, batch id null).
- Set only when still null (repeated submissions keep the FIRST batch); never overwritten, replaced by another
  non-null value or cleared through tracked writes (sync and async SaveChanges, including the retry-after-failure
  path) — a tracked quotation whose marker, pointer or source was nulled/changed in memory has the persisted values
  restored before save. No product code or `scripts/db` script writes `Quotations` through raw SQL, `ExecuteUpdate`
  or untracked `Update()`; raw SQL is the only way around the guard and is out of scope by design.
- `FirstSubmittedSource` records the evidence class: `SUBMISSION` (stamped live by the submission's own SaveChanges:
  the actual instant), `BACKFILL_BATCH` (migration backfill from a still-existing batch reference: **persisted batch
  evidence** dated by the batch's `CreatedAtUtc`) or `BACKFILL_LINE_AWARD_ESTIMATED` (legacy line-level award only:
  proven submission, APPROXIMATE date). Batch creation and first submission are atomic only in `CreateBatch` (batch,
  items and candidates in one SaveChanges); the rework edit (`UpdateBatch`) and the extra-item INCLUDE add candidates
  to an EXISTING batch later, so a backfilled batch date is a lower bound, not the exact first-submission time. Wording
  follows the source: `SUBMISSION` → "submetida para aprovação em dd/MM/yyyy HH:mm UTC"; `BACKFILL_BATCH` → "registo de
  submissão … Lote #N, STATUS, criado em dd/MM/yyyy" (no time of day, no claim of instant); estimated → "data de
  referência aproximada dd/MM/yyyy". Several batches: deterministic earliest by (CreatedAtUtc, BatchNumber, BatchId),
  and the pointer and the timestamp always come from that same row.
- Scope/performance: the stamper takes ONE snapshot of the tracked entries per SaveChanges (one DetectChanges, which
  SaveChanges performs anyway), inspects only candidates, batch items and request line items, issues at most two
  keyed lookups (quotation items / quotations by id) and only when submission evidence is pending, and never calls
  SaveChanges itself (no recursion, no table scans).
- Stamped through the quotation's **tracked instance** when one exists (CreateBatch/UpdateBatch load quotation items
  with `Include(qi => qi.Quotation)`), so the UPDATE runs under the quotation's RowVersion as read at the start of the
  submission.
- **FK/delete behaviour (verified on SQL Server LocalDB).** `FirstSubmittedBatchId` is a plain indexed column with **no
  foreign key** by design: batch deletion (cascade to items/candidates), batch-item removal and candidate removal never
  clear, null or block it (`QuotationSubmissionMarkerRelationalTests.No_foreign_key_on_FirstSubmittedBatchId…`). When
  the batch still exists the lock reason names it ("Lote #N, STATUS; referências do lote entretanto removidas ou
  recompostas"); when it is gone the reason says "lote original já removido ou recomposto". Candidate/selected
  references themselves keep `DeleteBehavior.NoAction` towards `QuotationItems` (unchanged), which is why a stale
  DELETE of a just-submitted quotation can also fail on the REFERENCE constraint (see 7.4).
- Real code paths covered by tests: `BatchExtraItemDecisionServiceTests.ApplyAsync_ReversalIncludeToExclude_WhenSafe…`
  (reversal removes the batch item → marker kept, still locked) and
  `CandidateBasedApprovalBatchTests.Returned_batch_can_be_edited_and_resubmitted` (rework recomposition drops a
  candidate → its quotation keeps the marker and the lock).

### 7.3 Shared decision and enforcement
`QuotationSubmissionLock` resolves the state from live references first (earliest batch number/status), then the
line-level award, then the marker (`Evidence` = BATCH_REFERENCE | LINE_AWARD | DURABLE_MARKER; code `QUOTATION_SUBMITTED`).
- `UpdateQuotation`: evaluated after loading the quotation, before header/attachment/line changes → 409 with
  `code`, `batchNumber`, `evidence`. `DeleteQuotation`: same decision replaces the previous inline check → 409.
- Exposed on `RequestDetailsDto.Quotations[]` and `BuyerWorkspaceQuotationDto` (`IsSubmittedForApproval`,
  `SubmittedBatchNumber`, `SubmissionLockReason`); Workspace shows "Submetida (Lote #N)" / "Submetida para aprovação"
  with the server reason instead of Editar/Excluir (`resolveQuotationActionState`); classic list uses the server flag
  first and its legacy client-side check only as a fallback. Both UIs never recompute the lock.
- Stale forms (edit wizard / delete confirmation opened before a submission) end in the server's 409; nothing is
  written and no notification is emitted (tests assert header, lines, document link, history and outbox unchanged).

### 7.4 Concurrency (verified on SQL Server LocalDB)
`Quotations.RowVersion` (rowversion) is the arbiter between a Buyer edit/delete and a submission committing at the
same time:
- `UpdateQuotation` always rewrites the header row under its RowVersion (`Entry(quotation).State = Modified`) even
  when the payload leaves it unchanged; a submission committed between the lock check and the save (the stamper
  bumps the row) makes the save fail with `DbUpdateConcurrencyException` → 409 `QUOTATION_CONCURRENT_CHANGE`, nothing
  written (`Stale_edit_is_refused_409…`).
- `DeleteQuotation`: the same race fails either on the RowVersion or, earlier, on the REFERENCE constraint of the new
  candidate towards the line being deleted (SqlException 547 naming a FK towards `Quotations`/`QuotationItems`); both
  are mapped to 409 `QUOTATION_CONCURRENT_CHANGE` (`Stale_delete_is_refused_409…`). Other database errors, including
  other constraint violations, are not mapped and surface through the generic pipeline as before.
- Refused actions leave no partial business writes: the stale-edit test also swaps the proforma document and asserts
  the old attachment is not soft-deleted and the link is unchanged; both race tests assert no history row, no outbox
  row, no workflow event and no in-app notification.
- Known residual: the legacy request-level area approval (`RequestsController.ProcessAreaApproval`, line-level award)
  has no concurrency filter, so a Buyer edit committed between its read and its save surfaces as a 500 instead of a
  409; the write itself still rolls back (single SaveChanges). Pre-existing endpoint behaviour, not changed here.
- The reverse race — a Buyer edit committed after the submission read its quotations — fails the submission's save
  (`DbUpdateConcurrencyException`, mapped to 409 by `ApprovalConcurrencyExceptionFilter` on `ApprovalBatchController`);
  nothing of the batch or the marker persists (`Submission_fails_and_rolls_back…`). A failed submission save (FK
  violation) also leaves no marker (`Failed_submission_save_leaves_no_marker_behind`).
- No new transaction scopes: each endpoint is one `SaveChanges` = one transaction, as before.

### 7.5 Historical boundary (explicit)
The migration backfills the marker from the references that **still exist** at migration time (candidates and
selected items of any batch status, source `BACKFILL_BATCH`, persisted batch evidence dated by batch creation; legacy line-level awards, source
`BACKFILL_LINE_AWARD_ESTIMATED`, approximate date = line `UpdatedAtUtc`, else quotation `CreatedAtUtc`, batch id null:
a proven submission whose time is an estimate and is labelled as such in the lock reason). A quotation whose references were all removed BEFORE the migration
(rework recomposition, extra-item reversal, batch deletion) **cannot be recovered**: the `BATCH_CANDIDATES_SUBMITTED`
history row carries supplier text only, and nothing is inferred from supplier names or free-text history. Such
quotations stay unlocked until (if ever) they are referenced again; this is a documented limitation, not a claim of
complete historical recovery. From the deployment onwards the marker is exact for every new submission.

### 7.6 Migration requirements — `20261016204148_AddQuotationSubmissionMarker`
- Additive: `Quotations.FirstSubmittedForApprovalAtUtc datetime2 NULL`, `Quotations.FirstSubmittedBatchId
  uniqueidentifier NULL` (+ `IX_Quotations_FirstSubmittedBatchId`, no FK), `Quotations.FirstSubmittedSource nvarchar(40) NULL`,
  `Quotations.RowVersion rowversion NOT NULL`; then `BackfillSql` (idempotent, only rows still NULL; same text executed
  by the relational test).
- `Down` drops the four columns and the index (backfilled/stamped markers are lost on rollback — re-running `Up`
  re-derives them from references that still exist, within the 7.5 boundary).
- Validated locally only (2026-10-16, final migration id 20261016204148): Up (20261009084838 + this one) → verify →
  Down → Up on a disposable LocalDB database restored from a copy-only backup of the local dev clone (146 quotations:
  131 marked = 129 `BACKFILL_BATCH` + 2 `BACKFILL_LINE_AWARD_ESTIMATED`, 15 never submitted; 0 referenced-but-unmarked,
  0 marked-without-reference, 0 batch-pointer mismatches, 6 locked through REJECTED/CANCELLED first batches, no FK on
  the pointer, deterministic after the round trip); the copy was dropped afterwards and the dev clone itself was not migrated. A from-zero
  database build is not possible in this repository (historical migration `20260603152331` is not replayable), so
  migrations are validated on already-migrated copies. The EnsureCreated sandbox `Portal-Gerencial-IntegrationTests`
  was recreated from the current model (its bootstrap detects the missing columns). **Not applied to TEST or PROD.**
  Deployment:
  Apply TEST Migrations (this migration, backend down or tolerant — the API with the new build requires the columns),
  then Deploy TEST; PROD via the standard Apply PRODUCTION Migrations → Deploy PRODUCTION gates. Backend and frontend
  of the same release go together (DTO flags/409 codes).
- Rollback coordination: the previous backend build does not read the new columns, so a code rollback without `Down`
  is safe; `Down` only if the release is abandoned.

### 7.7 Tests
`QuotationSubmissionLockTests` (InMemory): lock matrix (candidate / selected / both; WAITING_AREA_APPROVAL,
WAITING_FINAL_APPROVAL, REJECTED, CANCELLED, AREA_ADJUSTMENT with request in adjustment, APPROVED) → PUT/DELETE 409
with code/batch/evidence, snapshot unchanged, no event, no in-app call; sibling never-submitted editable/deletable;
no-batch quotation editable/deletable; detail and Workspace projections agree; pure decision order incl. the
approximate-date wording; stamping on candidate and selected-only seeds (source SUBMISSION); legacy line award
stamps without batch and locks; award removal/reassignment never unlocks the marked quotation and stamps the new
target; repeated submission keeps the first marker; marker/pointer/source never cleared, replaced or moved (async and
sync saves); marker alone keeps the lock after reference removal and after whole-batch
deletion (guards + both projections). `QuotationSubmissionMarkerRelationalTests` (LocalDB): no FK + marker survives
removal/deletion; stale edit 409 with document swap (old proforma kept, link unchanged, no history/outbox/event/notice);
stale delete 409 (no event/notice); submission loses to a prior edit and rolls back; failed submission
leaves no marker; never-submitted sibling editable/deletable on SQL Server while the others refuse; backfill SQL
(REJECTED candidate, CANCELLED selected-only, two batches → earliest with consistent pointer, legacy award →
estimated source and approximate wording once the award pointer is gone, untouched) and idempotence.
`ReworkRevisionSaveQuotationTests.G_MarkerLockedOriginal…` (LocalDB): explicit revision still accepted, revision starts
unlocked, original marker untouched. `quotationSubmissionLock.test.ts`: per-quotation UI state and both UIs' wiring.
Side fixes in the LocalDB suites (seeds used fixed supplier `PortalCode`/`TaxId` that the recreated sandbox's unique
indexes now reject; randomized, cleanup made tolerant). Assignment feature unchanged (`QuotationAssignBuyerTests`,
`workspaceAssignActions.test.ts`).
