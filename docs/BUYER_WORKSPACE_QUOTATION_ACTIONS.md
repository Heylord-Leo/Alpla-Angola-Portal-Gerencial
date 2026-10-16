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
