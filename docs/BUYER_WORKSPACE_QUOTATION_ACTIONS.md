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

## 6. "Assumir pedido" — claim (v2.247.10) and Buyer self-takeover (working tree, unreleased, 2026-10-22)

**Rules in force (v2.247.10, `POST …/assign-buyer`, unchanged for the claim).** Access scope (404); self-claim of an
UNASSIGNED QUOTATION request requires the Buyer ROLE (System Administrator / Local Manager without it: 403); assigning
another user is coordinator-only and the target must hold Buyer (409 otherwise); idempotent when already assigned to the
actor (200). Audit `COMPRADOR_ATRIBUIDO`; ONE notification source: the `BUYER_ASSIGNED` orchestration (new Buyer and
requester, in-app + outbox, dedup by correlation id). The former direct in-app notices written by the controller
duplicated the orchestration's and were removed; wording now distinguishes a self-claim ("assumiu o pedido"), a
coordinator assignment ("foi atribuído(a) ao comprador X por Y") and a PAYMENT correction claim ("correção da P.O",
`PoGroupId` carried on the event). PAYMENT requests keep the correction-group path (§G.20.1) untouched.

**Takeover of another Buyer's request — see §8 (new endpoint `POST …/take-over`).** In v2.247.10 a takeover was
coordinator-only (System Administrator / Local Manager WITH the Buyer role); §8 opens it to any Buyer within the
access scope, with a mandatory justification, stale-owner protection and a full notification matrix. The Workspace and
the classic list use the same decision helper (`resolveAssignAction`): CLAIM when unassigned, TAKEOVER when assigned to
someone else, nothing for PAYMENT, terminal statuses, the current owner or users without the Buyer role.

**REQ-20/08/2026-271 — why the button can be absent (evidence, not a confirmed cause).** The Workspace decides from
the roles cached at login (`sessionStorage.auth_user`, issued from `UserRoleAssignments` into the JWT; 24 h lifetime,
no refresh endpoint). The local dev clone (PROD data as of 2026-09-07; TEST may differ) shows REQ-271 as QUOTATION /
WAITING_QUOTATION / Buyer Samuel Mpoco (eligible shape) and TWO accounts named Leonardo Cintra: `leonardo.cintra@alpla.com`
(System Administrator, Local Manager, Buyer, …) and `leonardo.cintra1988@gmail.com` (Requester, Receiving only). A session
opened with the second account has no Buyer claim, so neither tool offers the action while the admin screen of the
first account shows Buyer checked. This explanation is **unconfirmed** until the session is inspected; no account or
permission is to be changed for the diagnosis. Read-only steps on TEST, in the browser console of the session that
lacks the button (the snippet prints only the account identity, the role claims and the relevant workspace fields —
never the token itself; the operator must not share the raw JWT):

```js
// 1. Account identity + role claims of THIS session (no token output)
const t = sessionStorage.getItem('auth_token') || '';
const claims = JSON.parse(atob(t.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
const roles = [].concat(claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || claims.role || []);
console.table({ email: claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'] || claims.email,
                userId: claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] || claims.nameid,
                expiresUtc: new Date(claims.exp * 1000).toISOString(), roles: roles.join(' | ') });
// 2. Relevant workspace fields of the request (replace REQUEST_ID)
fetch('/api/v1/buyer/requests/REQUEST_ID/workspace', { headers: { Authorization: 'Bearer ' + t } }).then(r => r.json())
  .then(w => console.table({ requestNumber: w.requestNumber, requestTypeCode: w.requestTypeCode, requestStatusCode: w.requestStatusCode, buyerId: w.buyerId, buyerName: w.buyerName }));
// 3. Deployed build vs loaded bundle
fetch('/api/app/version').then(r => r.json()).then(v => console.table({ api: v.version, buildId: v.buildId }));
```

Expected when the hypothesis holds: `roles` without `Buyer` for the signed-in e-mail while the admin screen of the OTHER
account shows Buyer; or `requestTypeCode` ≠ QUOTATION, a terminal status, or `buyerId` = the signed-in user. Any role
change requires a new login (24 h token, roles not refreshed).

**Live result on TEST v2.248.0 (operator, 2026-10-22) and proven root cause.** For Celestina Bernardo on REQ-271 every
condition was true (cached roles and JWT claims include Buyer; QUOTATION / WAITING_QUOTATION / owner Samuel Mpoco;
mode TAKEOVER) and still no button was visible. Cause (source of `e770363`): the button was rendered INSIDE the generic
`Meta` value container of the header grid, whose style is `white-space: nowrap; overflow: hidden; text-overflow:
ellipsis` in a `repeat(auto-fit, minmax(150px, 1fr))` cell; the owner's name plus the label "Assumir pedido de outro
comprador" exceed the cell width, so the button existed in the DOM but was clipped out of view (the diagnostic's
decision was correct; the element was simply invisible). The duplicate-account and missing-role hypotheses do not apply
to this case. Correction (working tree): the "Comprador" field is a dedicated `BuyerOwnerField` that renders the name
(ellipsis) and the action on separate rows outside any overflow-hidden/nowrap box; `BuyerOwnerField.test.ts` renders
the component with react-dom/server (no DOM runtime in vitest) and asserts the button has no clipping ancestor, and
that the header no longer nests the button in `Meta`. The 403 on `/api/v1/lookups/suppliers/pending-approval` is
independent (a sidebar/lookup call outside the header render path; permission not broadened).

Browser confirmation on a build that still clips (TEST v2.248.0), read-only, in the console of the Workspace page:
```js
const b = document.querySelector('[data-tour="buyer-workspace-assign"]');
const box = b?.parentElement; const r = b?.getBoundingClientRect(); const pr = box?.getBoundingClientRect();
console.table({ buttonInDom: !!b, parentOverflow: box && getComputedStyle(box).overflow, parentWhiteSpace: box && getComputedStyle(box).whiteSpace,
  buttonRight: r?.right, parentRight: pr?.right, clipped: !!(r && pr && r.right > pr.right) });
```
Expected before the fix: `buttonInDom: true`, `parentOverflow: hidden`, `clipped: true`. After the fix the button's
parent is a normal block (`overflow: visible`) and `clipped: false`.

### 6.1 Placement change (working tree, unreleased, after v2.248.1)

**Confirmed TEST evidence (unchanged, v2.248.0 → v2.248.1).** Celestina Bernardo on REQ-20/08/2026-271: every eligibility
condition true, mode TAKEOVER, button clipped inside the `Meta` value box; v2.248.1 moved the button out of the clipping
container (`BuyerOwnerField`). That evidence stands on its own and is not re-validated by the change below.

**New placement (this change).** The claim/takeover button leaves the "Comprador" metadata cell (which again shows only
the Buyer's name) and joins the upper-right header action group, next to "Solicitar cotação", "Importar Cotação" and
"Inserir Manualmente" when those are available (`data-tour="buyer-workspace-actions"`, anchor
`buyer-workspace-assign` kept on the button). The group is a wrapping flex row inside a wrapping header, so at narrow
widths the buttons wrap below "Próxima ação" instead of clipping or overlapping the title. `BuyerOwnerField` and its test
were removed. The quotation queue (`/buyer/items`) gains the same action in each row's kebab after "Ver detalhes" /
"Adicionar observação" (`buildRequestRowKebab` + `resolveQueueAssignAction`, same `resolveAssignAction` policy, shared
`TakeOverRequestDialog`, same `runAssignRequest` submission with `expectedBuyerId`): CLAIM calls the existing claim
handler; TAKEOVER opens the dialog with the kebab already closed; success re-reads queue and summary (owner, "Meu
pedido", filtered results, counts) and a failed re-read shows a distinct "não repita a ação" message; cancelling changes
nothing; one submission at a time. Tours: Workspace step text updated; queue tour gains a step on
`[data-tour="buyer-queue-row-actions"]`. Tests: `assignActionPlacement.test.ts` (header group placement, Comprador cell
without a button, queue wiring), `buyerQueueAssignActions.test.ts` (kebab composition), tour tests.
**Visual verification:** no browser is available to the agent in this session — the wrapping behaviour was not
visually verified; verify locally at 360 / 768 / 1280 px widths (DevTools device toolbar) that the action group wraps
below "Próxima ação" without clipping the buttons or overlapping the title.

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

## 8. Buyer self-takeover with justification and participant notifications (working tree, unreleased, 2026-10-22)

**Policy.** Any user holding the Buyer role may take over a QUOTATION request assigned to another Buyer, within the
existing access scope. Refused in CANCELLED, REJECTED, COMPLETED and PAYMENT_COMPLETED (backend and both UIs). A trimmed,
non-empty justification of at most 500 characters is mandatory and is written to the audit history and the
notifications. Third-party assignment stays coordinator-only (`assign-buyer?targetUserId`); coordinators WITHOUT the
Buyer role still cannot take over; PAYMENT ownership (correction groups) is untouched — opening group transfer to ordinary
Buyers would be separate work on `PoCorrectionClaimService` and the queue/dashboard rules of §G.20.1.

**Endpoint `POST /api/v1/requests/{id}/take-over`** (`RequestsController.TakeOverRequest`, body
`{ justification, expectedBuyerId }`), in order: scope 404 → QUOTATION only (409 `TAKEOVER_NOT_APPLICABLE`) → Buyer role
(403 `TAKEOVER_BUYER_ROLE_REQUIRED`) → terminal status (409 `TAKEOVER_TERMINAL_STATUS`) → justification (400
`TAKEOVER_JUSTIFICATION_REQUIRED` / `_TOO_LONG`) → expected owner: field OMITTED (400 `TAKEOVER_EXPECTED_OWNER_MISSING`),
EXPLICIT null (400 `TAKEOVER_EXPECTED_OWNER_NULL` — an unassigned request is claimed, not taken over), invalid (400) →
already mine (200 `TAKEOVER_ALREADY_OWNER`, nothing written — evaluated before the owner check, so a retried request after a lost response is idempotent whatever `expectedBuyerId` it carries) → request now unassigned (409 `TAKEOVER_NOT_ASSIGNED`) →
owner differs from `expectedBuyerId` (409 `TAKEOVER_STALE`, carries `currentBuyerId`/`currentBuyerName`). The raw JSON
element is inspected so omitted and explicit null are distinguishable; a stale confirmation never overwrites a newer
owner.

**Concurrency.** `Requests.RowVersion` is a configured rowversion (`[Timestamp]`, snapshot `IsConcurrencyToken`) and the
ownership UPDATE runs under it: two simultaneous takeovers with the same expected owner yield exactly one winner; the
loser gets 409 `TAKEOVER_CONCURRENT` with nothing written (proven on SQL Server LocalDB:
`RequestTakeOverRelationalTests.Simultaneous_takeovers…`). A later confirmation with the old owner is `TAKEOVER_STALE`
(`Stale_confirmation_after_a_completed_takeover…`). Both UIs send the owner they displayed and block double submission.

**Transactional notifications.** Ownership, the `COMPRADOR_TRANSFERIDO` history row (previous/new Buyer, actor, time,
justification), the `RequestFieldChangeHistories` row for `BuyerId`, the in-app rows and the outbox rows are persisted in
ONE transaction (`BeginTransactionAsync` around SaveChanges + `EmitAsync`, the repository's existing mechanism; the
orchestrator, the in-app service and the controller share the scoped `DbContext`). The orchestrator bubbles failures, so
a failure to record the notification intent rolls ownership and audit back (`Failure_while_recording…`); nothing is sent
synchronously — the outbox worker delivers later with its retry/dead-letter policy (`EmailOutboxProcessor`), rows start
PENDING with the worker's `MaxRetries`. Refusals and the idempotent path emit nothing; re-emitting the same correlation
creates no duplicate rows (outbox and in-app dedup by correlation id + recipient). For this event the in-app rows are
part of the required intent: `EventConfig.RequireInAppPersistence` makes an in-app persistence failure propagate (every
other event keeps the historical "log and continue" policy), so ownership, audit, in-app and outbox roll back together
(`Failure_persisting_an_in_app_notification…`, injected at the database level on SQL Server).

**Recipient matrix — `BUYER_TRANSFERRED` (`WorkflowNotificationOrchestrator.AddBuyerTransferRecipientsAsync`).**
Persisted relationships only; never role groups; active users only; e-mail only when an address exists (in-app
otherwise); dedup by user id, then by normalized e-mail (trim + lower-case) — a second account sharing a mailbox keeps
its own in-app notice and only its e-mail delivery is suppressed.

| # | Recipient | Source | Wording |
|---|---|---|---|
| 1 | Previous Buyer | `Request.BuyerId` before the change | "Deixou de ser o comprador responsável" |
| 2 | New Buyer (actor) | actor | "Passou a ser o comprador responsável" |
| 3 | Requester | `Request.RequesterId` | generic transfer notice |
| 4 | Area / final approvers on record | `Request.AreaApproverId`, `Request.FinalApproverId` | generic |
| 5 | P.O. owners, receipt completers, fiscal-receipt uploaders | `RequestPoGroups.PoResponsibleBuyerId`, `OperationalReceiptCompletedByUserId`, `FiscalReceiptUploadedByUserId` | generic |
| 6 | Finance participants | `RequestPayments.ScheduledByUserId`, `PaidByUserId` | generic |
| 7 | Winner selectors, rework requesters, reuse authorisers | `ApprovalBatchItems.WinnerSelectedByUserId`, `ApprovalBatchAdjustments.RequestedByUserId`, `QuotationReuseAuthorizations.AuthorizedByUserId` | generic |
| 8 | Actors of explicit history actions | `RequestStatusHistories.ActorUserId` where `ActionTaken` ∈ `BuyerTakeOverPolicy.ParticipantHistoryActions` (batch and legacy approval decisions, `QUOTATION_ITEM_AWARDED`, `REGISTER_PO`/`REREGISTER_PO`/`PO_GROUP_ACTIVATED`, `CONFIRM_RECEIVING`/`RECEIVING_PROGRESS`/`RECEIVING_REOPENED`, finance actions, `QUOTATION_REUSE_AUTHORIZED`) — exact codes, no text matching; `OBSERVACAO`, data edits, status syncs excluded | generic |
| 9 | Pending AREA approvers | only while the request or a batch is WAITING_AREA_APPROVAL / AREA_ADJUSTMENT, through `IApprovalRoutingService.ResolveAreaManagersAsync` (plant-specific managers first, else global managers; active users) — the same resolver the approval e-mails use | generic |
| 10 | Pending FINAL approver | only while the request or a batch is WAITING_FINAL_APPROVAL / FINAL_ADJUSTMENT, through `ResolveFinalNotificationRecipientsAsync(request.FinalApproverId, companyId)` (request nominee, else the company's nominee — persisted nominations) | generic |

Pending final approvers are therefore NOT the "Final Approver" role group: without a request or company nominee nobody
is added (the same gap the final-approval e-mails have today). The Accounts Payable mailbox is a group address, not a
participant, and is not notified. TEST SMTP redirection is unchanged: the outbox worker sends through `EmailService`,
which in non-production replaces every recipient by the configured test recipient when `RedirectAllToTestRecipient` is on.

**E-mail content.** Subject "Responsabilidade do pedido transferida — {número}" (role-specific for the previous and
the new Buyer); body: request number and title, previous Buyer, new Buyer, actor, transfer time rendered in the
application timezone with an explicit label — "dd/MM/yyyy HH:mm (Luanda, WAT UTC+1) — HH:mm UTC"
(`AppTimeZone.FormatWithUtc`, the Dashboard's Luanda convention; server time is UTC) — the HTML-encoded justification,
requester, plant, department and the "Ver Pedido" link (`AppConfig:FrontendBaseUrl` + `/requests/{id}?mode=view`).

**UIs.** Shared `TakeOverRequestDialog` (classic list and Workspace): names the request and the current Buyer, states
the transfer, the audit and the e-mails, requires the justification (client-side mirror of the server rule; confirm
disabled while invalid or busy), sends `expectedBuyerId` = the displayed owner. Classic list: the button is now hidden
whenever the decision is NONE (non-Buyers, PAYMENT, terminal, own request) instead of "shown and refused by the server".
Workspace: unchanged re-read after success (owner, next actions, quotation Editar/Excluir), distinct failed-refresh
message, one submission at a time; the dialog stays open on a client-side validation failure. Guided tour step updated.

**Tests.** `RequestTakeOverTests` (InMemory: full rule matrix, justification and expected-owner validation incl.
omitted vs explicit null vs invalid, stale, unassigned, idempotent, out of scope, claim/coordinator paths with a single
notification source), `BuyerTransferNotificationTests` (matrix, wording, dedup by user and e-mail, inactive/no-e-mail
rules, pending resolvers consulted only while pending, no duplicates on re-emit, BUYER_ASSIGNED wording),
`RequestTakeOverRelationalTests` (LocalDB: atomic success with outbox/in-app rows, rollback when the outbox intent
fails and when the in-app insert fails at the database, simultaneous takeovers → one winner, stale confirmation, replay
idempotence that can never overwrite a subsequent transfer), `workspaceAssignActions.test.ts` (decision, confirmation,
validation, orchestration, wiring of both UIs, dialog and tour). No migration: no EF model change (`WorkflowEvent` is
not persisted; new history/field-change rows use existing tables).

**Limitations.** The pending final approver is resolved from nominations only (no role fan-out); participants are
limited to the persisted pointers and action codes listed above; the legacy request-level approval path keeps its own
history codes (`AREA_APPROVED`…), covered by the explicit list but not re-verified live; the REQ-271 duplicate-account
explanation (§6) is unconfirmed until the session diagnostics are run.
