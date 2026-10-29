# P.O. group cancellation / return to quotation after final approval — phase 1

> Status: **implemented in the working tree, unreleased (2026-10-28)**. No commit, push, version bump, deploy or data change was
> performed. This document is the implementation plan (SOP step 2) and the module reference. Business decisions D1–D16 were
> taken by the owner on 2026-10-27 and are reproduced in §2. The root `implementation_plan.md` belongs to another task and was
> left untouched.

## 1. Problem

After final approval the only exit the Portal offered for a P.O. group was "correct the P.O." (Finance return →
`WAITING_PO_CORRECTION` → `register-po`). A group whose order was cancelled (REQ-27/09/2026-483: Finance returned with
"PEDIDO CANCELADO", ECF annulled outside the Portal) had no supported path: batch cancel refuses `APPROVED`, request cancel
refuses once Buyer processing started, close-not-quoted refuses approved lines, and `PoGroupStatuses.Cancelled` was read in
35 places but written nowhere. Phase 1 adds that path without touching the original quotation, the approved batch, the
P.O. document or the history.

## 2. Decisions (owner, 2026-10-27)

| # | Decision |
|---|---|
| D1 | The responsible Buyer (`Request.BuyerId` or the group's `PoResponsibleBuyerId`) or a System Administrator executes the cancellation. Other Buyers first claim / take over. The requester is notified only. |
| D2 | Scheduled payments (`PAYMENT_SCHEDULED`, `ADVANCE_PAYMENT_SCHEDULED`) refuse the action until Finance cancels the schedule or returns the group. |
| D3 | Candidates (and the winner) of a released item need the existing explicit reuse authorization (`authorize-reuse`, DEC-147) before entering a new batch. |
| D4 | Finance must confirm the annulment of the previous ECF when the group ever had a registered P.O. Re-quotation and area/final approval proceed meanwhile; **registering a replacement P.O. is blocked server-side until every applicable prior cancellation is CONFIRMED**. No P.O. ever → no confirmation, no block. |
| D5 | `Request.ApprovedTotalAmount` / `ApprovalBatch.ApprovedTotalAmount` stay as historical evidence; active figures are computed per currency and never summed across currencies. |
| D6 | Line allocations are preserved. |
| D7 | The batch stays `APPROVED`; affected `ApprovalBatchItems` are explicitly marked released. |
| D8 | The request becomes `CANCELLED` (`IsCancelled`) when, after a CANCEL_ITEMS outcome, no line, in-flight batch or live group remains. |
| D9 | PAYMENT-type requests are outside phase 1. |
| D10 | Structured reason codes + a mandatory explanation (20–1000 chars). |
| D11 | A line with status `ORDERED` refuses the action with an actionable "requires investigation" message; ORDERED is never reset automatically; siblings untouched. |
| D12 | No administrative override of the Finance confirmation. Only active users with the Finance role **and** the plant scope of the group confirm or reject; SysAdmin alone is refused; a SysAdmin who also holds Finance with the plant scope acts as Finance; nobody confirms their own declaration. |
| D13 | Missing Finance recipients and failed notifications are visible to the Buyer and administrators; a narrowly scoped, audited "resend notification" exists (new correlation, rate-limited); the Finance queue is pull-based so confirmation never depends on e-mail. |
| D14 | Legacy Finance returns are shown as "tipo não classificado" with the original reason; never inferred from free text; no second return needed. |
| D15 | Reusing the number of a previous cancelled P.O. is warned and requires a confirmed explanation (audited), never blocked by itself. |
| D16 | The requester is notified (in-app + e-mail) of the cancellation; the Finance confirmation outcome goes to the responsible Buyer only. |

## 3. Confirmed behaviour of the surrounding code (unchanged)

- `SyncLineItemStatusesAsync` (RequestsController) is the only writer of line status `ORDERED`/`WAITING_ORDER`, is request-wide
  and runs only through the legacy `ApplyStatusChangeAndSyncItemsAsync` path — never from `RegisterPo`, the Finance return,
  batch endpoints or this feature. Restart restores `WAITING_QUOTATION`, `PENDING`, `UNDER_REVIEW`, `WAITING_ORDER` or null
  to `WAITING_QUOTATION`; `RECEIVED`/`PARTIALLY_RECEIVED`/received quantity refuse; `ORDERED` refuses (D11).
- The only writer of `PO_ISSUED`/`ADVANCE_PAYMENT_REQUIRED` for a group is `RegisterPo` (plus the legacy-return repair tool,
  which already refuses non-`WAITING_PO_CORRECTION` groups). P.O. files are uploaded through `AttachmentsController.Upload`.
- Voiding (`VoidedAtUtc/By/Reason`, DEC-150) keeps the file listed and downloadable, renders "Sem efeito", and excludes it from
  every "document present" gate. An attachment belongs to one request and at most one group.
- `PurchaseOrderNumber` has no uniqueness constraint; the generic duplicate check (`PrimaveraPoReference`) stays.

## 4. Workflow

1. **Finance return** (`POST /api/v1/finance/{id}/return`) now validates `notes` server-side and accepts `returnKind`
   (`PO_CORRECTION` default | `ORDER_CANCELLATION_REQUESTED`) and `returnReasonCode`. The group still goes to
   `WAITING_PO_CORRECTION`; the kind is advisory (D14) and stored in `RequestPoGroups.LastFinanceReturn*`.
2. **Buyer outcome** on a group (Workspace tab "Grupos P.O.", header action `CANCEL_PO_GROUP`, request page):
   *Corrigir P.O.* (unchanged) · *Cancelar P.O. e voltar a cotar* (`RESTART_QUOTATION`) · *Cancelar P.O. e encerrar itens*
   (`CANCEL_ITEMS`). *v2.249.2 — guidance and navigation by the stored kind:* the Buyer queue row of a QUOTATION request opens the
   Workspace "Grupos P.O." tab (primary CTA "Corrigir P.O." / "Gerir P.O. devolvida" / "Rever P.O. devolvida" by kind; kebab
   "Abrir Workspace"), the Para Minha Ação card routes there too (a PAYMENT correction keeps its §G.20 deep-link); the Workspace header state reads
   "Correção de P.O." (correction), "P.O. devolvida — cancelamento solicitado" (next actions led by the cancellation, correction
   still offered) or the neutral "P.O. devolvida por Finanças" (legacy/mixed); the group card adds a one-line guidance per kind;
   the Finance obligations row, the request page / Print View "Próxima ação" and the Print View history heading use the same
   stored kind (legacy → neutral). Nothing is inferred from the reason text. A group in `WAITING_PO` offers **Registrar P.O.**
   (first registration, request page), never the correction tool; `PO_REGISTRATION_PENDING` ("P.O. a registar") replaces
   "Concluído para Compras" while a first P.O. is pending (non-actionable, hidden by default in the queue). Reason code + explanation; when the group ever had a P.O., the Buyer **declares** the ECF situation
   (`PENDING_EXTERNAL` | `DECLARED_CANCELLED` + reference | `UNKNOWN`), optionally attaching evidence (`PO_CANCELLATION`).
3. **Server, one transaction**: group → `CANCELLED`; cancellation record + items + ECF event; `PLANNED` payments →
   `CANCELLED`; P.O. files voided; batch items released; lines restored (restart) or closed as `CANCELLED_AFTER_APPROVAL`
   (close); history (`PO_GROUP_CANCELLED`, `PO_GROUP_ITEMS_RELEASED|CLOSED`, field changes); request status re-derived
   (`RequestStatusSyncService`) or set `CANCELLED` (D8); notification emitted inside the transaction (in-app persistence
   required); commit.
4. **Finance** sees the cancellation under "Cancelamentos a confirmar" (`GET /api/v1/finance/po-cancellations`) and confirms
   or rejects (`POST …/{id}/confirm|reject`). Rejection keeps the group cancelled and the block in place and notifies the
   Buyer, who updates the declaration (`PUT …/cancellation/ecf-declaration`) for a new review.
5. **Re-quotation**: released lines are `QUOTATION_PENDING` with no group; their former candidates are
   `REUSE_AUTHORIZATION_REQUIRED` (D3); new quotations are unlocked; a new batch goes through area and final approval; the
   new group activates to `WAITING_PO`. *v2.249.1:* when a sibling group has already crossed the P.O. gate the request scalar
   stays PO_PARTIALLY_UPLOADED / PO_ISSUED-like by design (never regressed); the quotation entries, `SaveQuotation` and
   `ocr-extract` then rely on the **item-level window** (`QuotationWindowPolicy`: released or never-quoted lines not held by an
   active/approved batch) — lines of other groups/batches are refused per line (409 "Item já comprometido"). The same rule keeps
   "Importar Cotação" / "Inserir Manualmente" visible after a partial batch submission.
   *v2.249.2 / v2.249.3 (corrections of the v2.249.1 note):* the QUOTATION/PROFORMA document **upload and delete** (v2.249.2)
   and **`ocr-extract`** (v2.249.3, now Buyer/SysAdmin only, reconciliation suggestions among the open lines only) were NOT
   changed in v2.249.1; they now honour the item-level window (`AttachmentsController.QuotationDocumentWindowOpenAsync`,
   `QuotationWindowEvaluator`), as do
   `PUT/DELETE …/quotations/{id}` (PUT refuses per line a mapping onto a held line; the §7 submission lock is unchanged). The
   Workspace exposes the server's window (`quotationWindowOpen`, `openLineItemIds`) and the quotation queue, the Dashboard
   Buyer section/workload, the personal actions, the alerts and the pipeline admit a batch-model request with a
   `QUOTATION_PENDING` line even past the P.O. gate (`BuyerQueueAdmission`, one shared bound). Evidence stays protected: the
   submitted quotation of the cancelled P.O. and its PROFORMA/QUOTATION document cannot be edited or deleted (§7 lock, 409
   `QUOTATION_SUBMITTED`); a mixed payload touching a committed line is refused as a whole. **Coverage follows D3:** the released line's former candidates
   are `REUSE_AUTHORIZATION_REQUIRED` and therefore NOT "Pronto para lote" — the line is "Pendente de cotação" until the Buyer
   authorizes the reuse (classic tools → "Reutilizar cotação") or registers a new quotation; the submission modal applies the same
   rule and says so on the pending row (`QuotationReuseEligibilityPolicy`, one rule for the batch endpoint and the projection).
6. **Replacement P.O.** (`register-po`, P.O. upload, `schedule-advance`, `confirm-advance`) is refused with
   `409 PRIOR_ECF_CONFIRMATION_PENDING` while any prior cancellation of the group's lines is not `CONFIRMED`.

## 5. Replacement-P.O. block — precise rules (D4)

- **Required when** the cancelled group has a `PurchaseOrderNumber`, any `PO` attachment (voided or not, not deleted) or a
  `REGISTER_PO`/`REREGISTER_PO` history row referencing it (`PoGroupCancellationEvaluator.RequiresFinanceConfirmation`).
- **Tracked per line** in `RequestPoGroupCancellationItems`; the guard (`PriorEcfConfirmationGuard`) resolves the target group's
  live lines → their cancellations → any `FinanceConfirmationRequired && FinanceConfirmationStatus <> CONFIRMED` blocks. Multiple
  restart cycles add rows; a group mixing a released line with fresh lines is blocked as a whole.
- **Concurrency**: `RegisterPo` now runs in an explicit transaction and reads the cancellation rows
  `WITH (UPDLOCK, HOLDLOCK)`; Finance confirm/reject reads the row `WITH (UPDLOCK, ROWLOCK)` in its own transaction. Proven on
  SQL Server LocalDB (`PoGroupCancellationRelationalTests`): the confirmation waits for the registering transaction and the
  refusal writes nothing. On non-SQL providers the lock is a no-op (InMemory tests).
- **Alternate routes**: the P.O. upload (`AttachmentsController`), `schedule-advance` and `confirm-advance` carry the same check.
  No override exists (D12).

## 6. Data model (migration `20261028082044_AddPoGroupCancellation`, additive)

- `RequestPoGroupCancellations` (1:1 with the group, unique): kind, reason code/text, snapshots (previous status, P.O. number,
  total, currency, batch number), origin Finance return history id, who/when; Buyer declaration (`FinanceConfirmationRequired`,
  `EcfStatus`, `EcfReference`, note, evidence attachment, declared by/at); Finance confirmation (`FinanceConfirmationStatus`
  NOT_REQUIRED|PENDING|CONFIRMED|REJECTED, confirmed by/at, note, last reviewed by/at); notification bookkeeping
  (`FinanceRecipientsResolvedCount`, `BuyerRecipientResolved`, `LastNotificationCorrelationId`, `LastNotifiedAtUtc`,
  `NotificationResendCount`); `RowVersion`.
- `RequestPoGroupCancellationItems` (line, previous group, batch item, outcome RELEASED|CLOSED, previous line status / lifecycle /
  selected quotation item).
- `RequestPoGroupCancellationEcfEvents` (append-only: BUYER_DECLARED, BUYER_UPDATED, FINANCE_CONFIRMED, FINANCE_REJECTED,
  NOTIFICATION_RESENT; actor + role snapshot + timestamp).
- `RequestPoGroups`: `LastFinanceReturnKind`, `LastFinanceReturnReasonCode`, `LastFinanceReturnedAtUtc`,
  `LastFinanceReturnedByUserId` (NULL = legacy).
- `ApprovalBatchItems`: `ReleasedAtUtc`, `ReleasedByUserId`, `ReleaseKind`, `PoGroupCancellationId` (+ index; no FK).
- `AccountsPayableNotificationConfigs.NotifyOnPoCancelled` (default 0).
- Constants: lifecycle `CANCELLED_AFTER_APPROVAL`; attachment type `PO_CANCELLATION`; events `PO_GROUP_CANCELLED`,
  `PO_CANCELLATION_CONFIRMED`, `PO_CANCELLATION_DECLARATION_REJECTED`; history actions in `PoGroupCancellationConstants.HistoryActions`.
- No backfill. Existing data is untouched.
- *v2.249.4* — additive migration `20261029140552_AddPoDocumentSupersessionAndExtraction`: `RequestPoGroups.CurrentPoAttachmentId`,
  `RequestAttachments.SupersededAtUtc / SupersededByAttachmentId / SupersededByUserId` (P.O. document supersession on correction —
  distinct from voiding) and the table `PoDocumentExtractions` (server-held P.O. extraction evidence, bound to request + file hash).
  See `docs/PO_DOCUMENT_VALIDATION_v2.249.4.md` §3–§4 and §7.

## 7. API

| Endpoint | Who | Notes |
|---|---|---|
| `POST /api/v1/requests/{requestId}/po-groups/{groupId}/cancel` | responsible Buyer / SysAdmin | body `CancelPoGroupRequestDto`; 400 `KIND_INVALID`, `REASON_CODE_INVALID`, `JUSTIFICATION_REQUIRED`, `ECF_DECLARATION_REQUIRED`, `ECF_REFERENCE_REQUIRED`; 403 `NOT_RESPONSIBLE_BUYER`; 409 `GROUP_STATUS_NOT_CANCELLABLE`, `PAYMENT_SCHEDULED_ACTIVE`, `PAYMENT_OR_RECEIPT_EVIDENCE`, `ORDERED_LINE_REQUIRES_INVESTIGATION`, `GROUP_LINKAGE_INCONSISTENT`, `GROUP_ALREADY_CANCELLED`, `REQUEST_TERMINAL`, `REQUEST_TYPE_NOT_SUPPORTED`, `CANCELLATION_CONCURRENT_CHANGE` |
| `GET …/cancellation`, `GET …/cancellation/eligibility` | scope | read models with per-caller actions |
| `PUT …/cancellation/ecf-declaration` | responsible Buyer / SysAdmin | while PENDING or REJECTED; 409 `DECLARATION_LOCKED` after confirmation |
| `POST …/cancellation/resend-notification` | responsible Buyer / SysAdmin | new correlation; 409 `RESEND_TOO_SOON` (< 2 min) |
| `GET /api/v1/finance/po-cancellations?status=OPEN|PENDING|REJECTED|CONFIRMED|ALL&plantId&search` | Finance / SysAdmin | per-row `canReview` + `reviewBlockReason` |
| `POST /api/v1/finance/po-cancellations/{id}/confirm` / `/reject` | Finance with plant scope, active, not the declarer | reject note ≥ 20 chars; 403 `FINANCE_SCOPE_REQUIRED`, `SELF_CONFIRMATION_FORBIDDEN`; 409 `FINANCE_CONFIRMATION_NOT_REQUIRED`, `FINANCE_CONFIRMATION_ALREADY_DONE` |
| `POST /api/v1/requests/{id}/operational/register-po` | Buyer | new 409 `PRIOR_ECF_CONFIRMATION_PENDING`; new 400 `PREVIOUS_PO_NUMBER_REUSED` resolved with `previousPoNumberReuseConfirmed` + `previousPoNumberReuseComment` (audited as `PO_NUMBER_REUSED_AFTER_CANCELLATION`) |
| `POST /api/v1/finance/{id}/return` | Finance | `notes` mandatory (400 `RETURN_NOTES_REQUIRED`), `returnKind`, `returnReasonCode` |

Read models: `RequestPoGroupDto.lastFinanceReturn / cancellation / priorEcfConfirmation / rowVersion`;
`RequestDetailsDto.activeApprovedTotals`; `BuyerWorkspaceDto.poGroups / activeApprovedTotals`; batch DTOs
`releasedItemCount / releasedLineNumbers / activeTotalsByCurrency / releasedTotalsByCurrency`; queue next action `CANCEL_PO_GROUP`.
*v2.249.2:* `BuyerWorkspaceDto.quotationWindowOpen / quotationWindowClosedReasonCode / openLineItemIds`;
`BuyerWorkspacePoGroupDto.canRegisterPo` (WAITING_PO) vs `canCorrectPo` (WAITING_PO_CORRECTION only);
`BuyerWorkspaceQuotationDto.reuseBlockedItemCount / reuseAuthorizedItemCount`; `BuyerPoCorrectionGroupDto.returnKind /
returnKindLabel` (queue rows and personal actions); `RequestApprovalBatchItemDto.releasedAtUtc / releaseKind`; personal-action
`route` = Workspace groups tab for QUOTATION (PAYMENT unchanged); queue operational state `PO_REGISTRATION_PENDING`.
*v2.249.4 (findings of the v2.249.3 TEST validation — `docs/PO_DOCUMENT_VALIDATION_v2.249.4.md`):* `POST /api/v1/requests/{id}/po-document-extract`
(scope, Buyer/SysAdmin) persists the server's reading of a P.O. document (`PoDocumentExtractions`, bound to request + SHA-256) and
returns the `direct-ocr` envelope plus `headerSuggestions.purchaseOrderIdentification`; `register-po` requires the number on a
correction (`400 PO_NUMBER_REQUIRED`), identifies the document from that server-held evidence only (looked up by the registered file's
own hash; the DTO carries no extraction facts): `400 DOCUMENT_NOT_PURCHASE_ORDER` (no override), `400 DOCUMENT_TYPE_CONFIRMATION_REQUIRED`
unless `documentTypeConfirmed` + Comment (no evidence / failed / uncertain), a kept number that differs from the document → `OCR_MISMATCH`
override policy; refuses a P.O. file byte-identical to a quotation document of the request (`400 DOCUMENT_IS_QUOTATION_EVIDENCE`, also at
the upload); audits a changed number (`PO_NUMBER_CORRECTED`) and stamps the current / superseded P.O. files; the obligations read model reports a
CANCELLED group as history (`groupCancelled`, `derivedStatus=CANCELLED`, excluded from the rollup) and the short-close / invoice
write endpoints refuse it (`409 OI_GROUP_CANCELLED`); `PoGroupCancellationDto` / Finance queue rows carry
`financeReviewAppliesToCurrentDeclaration` + `previousFinanceReview`; `RequestPoGroupDto.currentPoAttachmentId /
historicalLineItemCount`, `RequestAttachmentDto.requestPoGroupId / supersededAtUtc / supersededByAttachmentId / isCurrentPo`,
`SavedQuotationDto.quotedItemCount / notQuotedItemCount`, `BuyerWorkspacePoGroupDto.historicalLineNumbers`,
`BuyerWorkspaceQuotationDto.itemsNotQuotedCount`; `NotifyOnPoCancelled` configurable on `/api/v1/ap-notification-configs`.

## 8. Notifications — explicit recipient policy

### 8.1 Who receives `PO_GROUP_CANCELLED` (cancellation, restart, declaration update, resend)

| Recipient | In-app | E-mail (outbox) | Rule |
|---|---|---|---|
| Responsible Buyer (`Request.BuyerId`) and the group's `PoResponsibleBuyerId` | always | always when the user has an address | self-notification allowed |
| Requester (D16) | always | always when the user has an address | cancellation only; never the Finance confirmation outcome |
| Area managers (routing), persisted area/final approvers, `BATCH_FINAL_APPROVED` actors | always | always when the user has an address | |
| **Finance users**: ACTIVE users with the Finance role whose plant scope contains the group's plant | always | **always** when the user has an address — independent of the company switch `NotifyFinanceUsersByEmail`, which governs other events only | never a wider set; without a plant the fallback is every Finance user **in-app only** |
| Accounts Payable mailbox (company) | — | only with `NotifyOnPoCancelled`; an **outbox row** (recipient = AP address, CC = configured copies) written inside the transaction, deduplicated per correlation + address | additional, never a substitute |

Recipients are deduplicated by user, then by normalized e-mail (a second account sharing a mailbox keeps its in-app notice; the mailbox gets one e-mail). The non-PROD redirect, subject prefix and banner are applied by the untouched `EmailService` when the outbox worker sends. `PO_CANCELLATION_CONFIRMED` / `_DECLARATION_REJECTED`: responsible Buyer(s) only.

### 8.2 Outcome recorded on the cancellation (D13)

Each emission (initial, declaration update, resend) records on `RequestPoGroupCancellations`: `FinanceRecipientsResolvedCount` (eligible), `FinanceRecipientsWithEmailCount`, `FinanceEmailsQueuedCount`, `FinanceEmailPolicyOutcome` and `BuyerRecipientResolved` / `BuyerEmailQueued`, plus `LastNotificationCorrelationId`.

| `FinanceEmailPolicyOutcome` | Meaning | Visible as |
|---|---|---|
| `QUEUED` | eligible Finance users had addresses; outbox rows were queued | delivery list per recipient |
| `NO_ELIGIBLE_FINANCE_USERS` | no active Finance user is scoped to the plant | warning + admin log `PO_CANCELLATION_NO_FINANCE_RECIPIENT`; fix the users' plant scope, then "Reenviar notificação" |
| `ELIGIBLE_USERS_WITHOUT_EMAIL` | users exist but have no address (in-app only) | warning naming the count; fix the addresses, then resend |
| `NO_PLANT_IN_APP_ONLY` | the group/request has no plant; fallback is in-app only | warning; set the plant or confirm from the Finance queue |

Delivery is the outbox worker's later fact (`EmailOutboxProcessor` polls every 10 s, batch 10, 20 s start-up delay in DEV; a list read right after the cancellation legitimately shows every recipient as `PENDING`): the read model (`PoGroupCancellationDto.delivery`) is read LIVE on every Workspace load and lists, for the last correlation, each recipient's outbox status (`PENDING`, `PROCESSING`, `SENT`, `FAILED` with next retry, `DEAD_LETTER` with the last error, `EXPIRED`). `FAILED`/`DEAD_LETTER` produce an actionable warning and the admin log entries `EMAIL_OUTBOX_RETRY_SCHEDULED` / `EMAIL_OUTBOX_DEAD_LETTER` (Administração → Logs do Sistema). *v2.249.2:* the card shows when the state was read, notes the background processing and offers **"Atualizar estado"** (re-read of the projection only — never a resend; no polling is added, consistent with the other Workspace surfaces). Recovery = "Reenviar notificação" (responsible Buyer or SysAdmin): a new history row = a new correlation, so outbox and in-app dedup do not suppress it, the cancellation itself is never repeated, the resend is audited (`PO_CANCELLATION_NOTIFICATION_RESENT` + ECF event) and rate-limited (2 minutes). The Finance confirmation queue is pull-based and never depends on e-mail.

## 9. Legacy Finance returns — REQ-27/09/2026-483

- Groups returned before this version have `LastFinanceReturnKind = NULL` → "Tipo não classificado (devolução anterior à
  versão)"; the reason is read from the `FINANCE_RETURN_ADJUSTMENT` history row ("Motivo:" text). All three outcomes are
  available; nothing is inferred or backfilled (D14).
- **Read-only diagnostic (owner runs in PROD, SELECT only):** `scripts/db/req483-po-cancellation-readonly-diagnostic.sql`.
  Expected safe shape: one approved batch, group `71f902e8-…` in `WAITING_PO_CORRECTION`, no COMPLETED/SCHEDULED payment,
  no received quantity, no operation invoice, only the P.O. attachment, line status not `ORDERED`/`RECEIVED`.
- **Post-deployment workflow (after v2.249.x is in PROD; not executed by the agent):** the responsible Buyer opens the
  Workspace of REQ-483 → tab "Grupos P.O." → the group shows "Devolvido por Finanças — tipo não classificado — Motivo:
  PEDIDO CANCELADO" → "Cancelar P.O." → choose *voltar a cotar* or *encerrar itens* (business decision), reason
  `ORDER_CANCELLED_BY_COMPANY` (or `SUPPLIER_CANCELLED`), explanation, ECF "Já foi anulada fora do Portal" with reference
  `ECF10 2026/364` → confirm. Finance (plant of the request, not the declarer) confirms in "Cancelamentos a confirmar".
  If re-quoting: authorize reuse or add a new quotation, submit a new batch, area + final approval; `register-po` on the
  new group is accepted only after the confirmation. Nothing about REQ-483 is changed by the deployment itself.

## 10. Deployment prerequisites, rollback limitations and containment

### 10.1 Prerequisites
- MINOR release via `/task-publish` (candidate v2.249.0). TEST: **Apply TEST Migrations** (`20261028082044_AddPoGroupCancellation`) before **Deploy to TEST**; PROD: **Apply PRODUCTION Migrations** then **Deploy to PRODUCTION**. The idempotent script adds 9 nullable/defaulted columns, 3 tables and 7 indexes; it contains no data statements. No configuration is required; optional per company: `NotifyOnPoCancelled`.
- Never run `Down`: it drops the cancellation tables (the only record of declarations and confirmations) and the release markers.

### 10.2 Rollback BEFORE the feature was used (no cancellation rows, no released items)
Application rollback (Procedure A) is safe. The new tables stay empty; the new nullable columns are ignored by the old code; the old model does not map them, so nothing reads or writes them. Finance returns made by the new code carry `LastFinanceReturn*` values that the old code ignores.

### 10.3 Rollback AFTER groups were cancelled / items released — what the old code does
| Area | Old code behaviour over new data |
|---|---|
| Cancelled groups | ignored by every existing filter (status CANCELLED was already filtered in 35 sites); their history rows show raw action codes; no "Grupos P.O." tab, no cancellation card, no Finance queue |
| Released batch items | the old `CreateBatch`/`UpdateBatch` treat them as still held → a NEW batch for those lines is refused ("já está em outro lote ativo"); batches already created by the new code keep working |
| Pending ECF confirmations | invisible: the Finance queue and the confirm/reject endpoints do not exist; the records stay in the table untouched |
| **Replacement P.O. guard** | **absent**: the old `RegisterPo` has no prior-ECF check. A replacement group that the new code already activated (WAITING_PO) **can be registered without Finance confirmation**; the old P.O. upload has no check either |
| Same-number warning (D15) | absent; the generic duplicate check (same number on another group) still warns |
| Finance return kind | the old modal sends no kind; `notes` validation is UI-only again |
| Voided P.O. files | stay voided ("Sem efeito") and excluded from presence gates (existing behaviour) |
| Active totals (D5) | not computed; lists fall back to the historical `ApprovedTotalAmount`, which for restarted requests counts the released value (and, after a new approval, twice) |
| Departmental e-mail amounts | old resolver reports `RecordedApproved` even with cancelled groups |

### 10.4 Containment procedure if a rollback is unavoidable after use
1. Before deploying the old build, run (read-only):
   `SELECT c.Id, c.RequestId, r.RequestNumber, c.PurchaseOrderNumberSnapshot, c.EcfStatus, c.FinanceConfirmationStatus FROM RequestPoGroupCancellations c JOIN Requests r ON r.Id=c.RequestId WHERE c.FinanceConfirmationRequired=1 AND c.FinanceConfirmationStatus<>'CONFIRMED'`
   and, for those requests, the WAITING_PO groups covering their released lines
   (`SELECT g.Id, g.RequestId FROM RequestPoGroups g WHERE g.Status='WAITING_PO' AND EXISTS (SELECT 1 FROM RequestLineItems li JOIN RequestPoGroupCancellationItems i ON i.RequestLineItemId=li.Id JOIN RequestPoGroupCancellations c ON c.Id=i.RequestPoGroupCancellationId WHERE li.RequestPoGroupId=g.Id AND c.FinanceConfirmationRequired=1 AND c.FinanceConfirmationStatus<>'CONFIRMED')`).
2. For every row: either obtain the Finance confirmation BEFORE the rollback (new code still running), or place an operational hold — Finance informs the responsible Buyers in writing that no replacement P.O. may be registered for those requests until the new build is back; the SysAdmin reviews `REGISTER_PO` history rows for those groups daily while the old build runs.
3. Do not delete or edit cancellation rows, released markers or voided attachments during the rollback window.
4. Prefer a **forward fix** once the feature has been used: the data model is additive and every write path of the new code is guarded; the old code's lack of the ECF guard is the one bypass that no data-side measure prevents.

## 11. Tests and evidence

Backend (`tests/backend/.../Services/Purchasing/`):
- `PoGroupCancellationDomainTests` — evaluator matrix, D11, ECF rule, active totals (per currency), resolver `ActiveApproved`, projection with released items.
- `PoGroupCancellationServiceTests` — restart/close, evidence preserved, sibling isolation, no-P.O., legacy return, refusals, D1, D12 matrix (service layer), confirm/reject/update/resend, Finance queue, multi-cycle block, eligibility, delivery state and warnings.
- `PoCancellationFinanceAuthorizationApiTests` — **D12 at the API boundary** (controller + claims only): SysAdmin alone refused on confirm and reject; Finance claim without a database assignment refused; Finance of another plant → 404 (scope) and, when in scope, group of another plant → 403; inactive refused; the declarer refused even with Finance + SysAdmin, also after a declaration update by a different responsible Buyer; an active Finance user with the plant scope rejects and another confirms; SysAdmin+Finance with plant scope acts as Finance; queue endpoint role gate and per-caller `canReview`.
- `PoCancellationRegisterPoGuardTests` — D4 block on `register-po` and P.O. upload, allowed after confirmation, sibling unaffected, D15 warning/explanation/audit, unchanged correction flow.
- `PoCancellationNotificationTests` — recipient matrix, Finance e-mail independent of the AP switch and of a missing AP configuration, outcomes NO_ELIGIBLE_FINANCE_USERS / ELIGIBLE_USERS_WITHOUT_EMAIL / NO_PLANT_IN_APP_ONLY, wording variants, dedup by correlation, resend under a new correlation, AP notice as an outbox row (CC, no direct send, dedup) and its delivery/retry by the real outbox worker, D16.
- `PoGroupCancellationRelationalTests` (SQL Server LocalDB sandbox) — guard lock blocks a concurrent Finance confirmation until the registering transaction ends; `RegisterPo` refused on SQL Server writes nothing; two simultaneous cancellations → one; failure inside the transaction leaves nothing; outbox and in-app rows written before a later failure are rolled back; committed cancellation persists business, audit, in-app and outbox rows together and a retry adds nothing; `RegisterPo` transaction coexists with the completion phases when the lifecycle is enabled.
Frontend: `src/lib/poGroupCancellation.test.ts`, `src/lib/registerPoErrors.test.ts`, `src/pages/Buyer/poGroupCancellationWiring.test.ts` plus extended existing tests.

## 12. Transaction and delivery boundaries

- Accounts Payable durability: the AP notice is an `EmailOutbox` row created by the orchestrator on the caller's context, so it
  commits with the cancellation (a crash after commit cannot lose the intent; a rollback cannot leave it queued). The outbox
  worker delivers it after the commit with the standard budget (30 s / 120 s / 600 s, then `DEAD_LETTER` with admin log
  `EMAIL_OUTBOX_DEAD_LETTER`); `EmailService` applies the non-PROD redirect, prefix and banner at send time; the row appears in
  the cancellation's delivery list like any recipient. Duplicate protection: insert-time dedup per (correlation, AP address),
  send-time dedup on a SENT twin; a re-emission of the same action adds nothing; a resend is a new correlation and therefore a
  new, intended notice. No `AccountsPayableNotificationLogs` row is written for this event (the outbox row is the audit). No SMTP
  call happens inside any business transaction of this feature.
- `PoGroupCancellationService` opens one EF transaction per operation. Inside it: the business rows (group, lines, batch items, payments, attachments), the cancellation rows, the history rows, the field changes, the status re-derivation, **and** the orchestrator emit. The orchestrator's in-app rows (`NotificationService`, same scoped `DbContext`) and the outbox rows (`EmailOutbox`, same context) are therefore part of the same transaction: they commit together or roll back together (`RequireInAppPersistence` makes an in-app failure propagate). Nothing leaves the process while the transaction is open: the outbox worker only picks rows after commit (per-user rows and the AP row alike).
- Retries: a repeated cancel is refused (`GROUP_ALREADY_CANCELLED`); a repeated Finance review is refused (`FINANCE_CONFIRMATION_ALREADY_DONE`); a re-emission of the same correlation adds no outbox/in-app rows; a resend is a deliberate new correlation.
- `RegisterPo` now opens a transaction before the D4 guard and commits after the status write and the completion phase one. `EvaluateGroupCompletionAsync` opens no transaction of its own; `EvaluateParentCompletionAsync` (phase two, which refuses an ambient transaction) runs after the commit, when `Database.CurrentTransaction` is already null. Proven on SQL Server with the lifecycle enabled (`RegisterPo_transaction_coexists_with_the_completion_phases_when_the_lifecycle_is_enabled`); in PROD the lifecycle is currently disabled (`CompletionEnabled=false`), so both phases are no-ops today.

## 13. Manual TEST checklist (owner)

Synthetic requests only (ZZTEST-…), plant with ≥ 2 Finance users scoped; SMTP redirect ON in TEST.
1. Return for correction (kind PO_CORRECTION) → Buyer corrects → `REREGISTER_PO`; nothing else changes. Blank notes → 400.
2. Request A (PO_ISSUED group, 2 lines in 2 groups): cancel group 1 *voltar a cotar*, ECF "já anulada" + reference → group
   CANCELLED, P.O. file "Sem efeito" and still downloadable, line 1 pending, batch #1 APPROVED with "Itens libertados: #1",
   sibling untouched, history rows, e-mails (Buyer, requester, Finance "confirmar anulação") redirected to the TEST recipient
   with the prefix; Finance queue shows the row. Same with ECF "ainda não anulada" (e-mail asks to annul) and "não sei".
3. Finance: non-Finance/SysAdmin-only → 403; other-plant Finance → not visible; declarer with Finance → 403 self; reject
   without note → 400; reject → Buyer notified, group stays cancelled; Buyer updates declaration → PENDING again; confirm →
   Buyer notified.
4. New quotation on line 1 → authorize reuse of the old candidate (or add a new quotation) → new batch → area + final →
   new group WAITING_PO → P.O. upload / register → 409 while pending or rejected; after confirmation → OK. Same number as the
   cancelled P.O. → warning, explanation, history `PO_NUMBER_REUSED_AFTER_CANCELLATION`.
5. Group without any P.O. (WAITING_PO) → cancel → no ECF fields, Finance informational only, no block.
6. Legacy return shape (return made before deployment) → "tipo não classificado", original reason shown, cancel works.
7. Second cycle: cancel the replacement group again → both cancellations tracked; block until the second is confirmed.
8. Mixed currencies: request with RND + AOA groups → header "Aprovado ativo" per currency, never a sum; historical total shown
   separately; departmental e-mail context uses live groups.
9. Refusals: PAYMENT_SCHEDULED group; COMPLETED payment; received line; `ORDERED` line (message "requer investigação");
   PAYMENT-type request.
10. No Finance user scoped to the plant → action succeeds, warning shown, admin log `PO_CANCELLATION_NO_FINANCE_RECIPIENT`,
    queue still lists the row; "Reenviar notificação" → 409 within 2 min, then new outbox rows. Force an SMTP failure → retries
    30/120/600 s → `EMAIL_OUTBOX_DEAD_LETTER` in System Logs; resend after fixing SMTP.
11. Dashboards / Finance obligations / receiving queue: cancelled group absent; counts coherent.
12. Recipient policy: company with `NotifyFinanceUsersByEmail = false` → Finance users of the plant still receive the e-mail (redirected to the TEST recipient); a Finance user of another plant and an inactive one receive nothing; AP mailbox only with `NotifyOnPoCancelled`.
13. Delivery visibility: on the cancelled-group card, the delivery list shows PENDING → SENT per recipient; with SMTP broken, FAILED (retry time) then DEAD_LETTER with the error and the warning text; after fixing SMTP, "Reenviar notificação" queues a new set (new correlation) and the cancellation is not repeated.
14. Browser/visual pass (not performed by the agent): Grupos P.O. tab and action visibility per role/state; dialog required fields and consequences; legacy unclassified return text; Finance panel confirm/reject (note mandatory on reject); pending-ECF banner and blocked replacement P.O. (upload and register); same-number warning and justification; DUPLICATE_PO and OCR_MISMATCH branches of RegisterPoModal (now effective after the error-classifier fix); historical vs active amounts, mixed currencies, released items; loading, error, success and empty states.
