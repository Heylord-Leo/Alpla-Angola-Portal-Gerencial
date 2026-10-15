# Approval notifications — investigation, fixes and reminder digests

> Status 2026-10-07: all four changes implemented locally on `Portal-Gerencial-rev1` (working tree on top of `1fd1e74`
> = PROD 2.245.12), **not committed, not deployed**. PROD data, configuration and sending are unchanged. The three
> migrations were applied only to the DEV clone `Portal-Gerencial-Dev-ProdClone`. Section G is the implementation
> record; section D plus G.6 list the TEST checks that still await execution.
>
> Note: the original copy of this file (phases 1–3 written earlier on 2026-10-07) disappeared from the working tree
> during the session; it was rebuilt from the surviving addendum text and the implementation. The investigation summary
> below is condensed from that first version.

## Phase 1 — verify the PROD state (no live PROD access from the workstation)

Evidence boundary: the DEV clone is the PROD backup of 2026-09-07 (10:27–15:08 UTC); everything after that in the clone is
local noise. `scripts/db/notifications-phase1-readonly-checks.sql` is the read-only script for the operator to run on
PROD/TEST (outbox health, proforma cycle events, pending approvals and their oldest stage entry, SMTP/redirect settings
without secrets). Findings on the clone: outbox healthy (3023 SENT / 0 DEAD_LETTER), zero `PROFORMA_DEADLINE_CYCLE`
events and zero `ProformaDeadlineAlerts` rows in the whole PROD period, four QUOTATION requests waiting for area approval
since August with no approval e-mail ever queued (batch flow emitted no event).

## Phase 2 — defects found and fixed

1. **Batch workflow emitted no notifications.** `ApprovalBatchController` CreateBatch / BatchAreaApprove / BatchAreaReject /
   BatchFinalApprove / BatchFinalReject wrote history and changed state but never called the orchestrator. Fixed
   (change 1): each action emits the matching `WorkflowEvent` after its final `SaveChanges`, correlated to the stage history
   row; the request reference reads "REQ-… (Lote #N)".
2. **Proforma scheduler ignored `CheckTimeUtcHour`.** The loop slept 30 s then 24 h from process start. Fixed (change 1):
   pure `ProformaDeadlineSchedule` anchors to the configured UTC hour, catches up once after a restart when today's anchor
   passed with no recorded cycle, and never runs twice a day.
3. **Failed proforma alerts were recorded as sent and never retried; sending bypassed the outbox.** Fixed (change 3).
4. **Final-stage routing inconsistent with authorization** (role-only authorization vs single-nominee notification; no
   concurrency guard on batch final approval). Fixed (change 2).
5. **In-app pending count ignored the DepartmentManagers cascade.** Fixed (change 2).

## Phase 3 — reminder design (approved decisions)

08:00 Africa/Luanda, Monday–Friday; a unit is reminded once it has waited **more than 3 calendar days in its current
stage**; the stage entry is a dedicated `ApprovalBatches.StageEnteredAtUtc` (request-level units use the latest status
history row into the current status); recipients come from the same routing that authorizes (area cascade, company
final approvers), legacy per-request nominees are not reminded. One consolidated digest per approver per business day,
persisted with its items and the outbox row in one transaction, deduplicated by a unique key, disabled and dry-run by
default. Implemented as change 4.

---

## Addendum (2026-10-07) — decisions, reliability, alternative approvers, TEST evidence

### A. The four Phase 3 decisions — recommendation and consequences

| # | Decision | Recommendation | Consequences of each option |
|---|---|---|---|
| 1 | Send time / days | **08:00 Luanda (UTC+1), Mon–Fri** | 08:00 lands before the working day, so approvers act the same morning; a later hour (e.g. 10:00) catches overnight approvals but delays action; including Saturday adds noise for a non-working day and no faster approvals. The time is a config value, so it can change without a release. |
| 2 | "Time waiting" basis | **From entry into the current stage**, with the request creation date shown as secondary text | Stage-based age restarts after an adjustment/resubmission cycle, which is fair to the approver and reflects what they can act on; creation-based age would nag approvers for delays caused upstream (quotation, buyer). The downside — a request that bounced several times looks "young" — is mitigated by showing the creation date in the row. |
| 3 | Batch stage entry | **Add `ApprovalBatches.StageEnteredAtUtc`** (written by CreateBatch, ResubmitBatch, BatchAreaApprove; backfilled from history in the migration) | Exact and cheap to query; one additive migration plus three controller writes. Deriving from `RequestStatusHistories.Comment LIKE 'Lote #N'` needs no schema change but is fragile and slow on large histories, and breaks the moment a comment format changes. |
| 4 | Legacy `AreaApproverId` nominee | **Cascade only** for reminders, and align the proforma alert to the cascade too (Phase 2.6 defect 2) | Authorization already follows the cascade (`CanActAsAreaManagerAsync` → `IsAreaManagerAsync`), so a nominee who is not an active DepartmentManager **cannot approve** — reminding them would be a reminder nobody can act on. A nominee who *is* a manager is resolved by the cascade anyway. The only loss is alerts to a non-acting nominee, which is the intended correction. |

### B. Reliability of the digest pipeline

**B1. Dedup record and outbox entry — one transaction.** The per-recipient unit of work is a single EF `SaveChangesAsync`
containing the `ApprovalReminderDigests` row (unique `(RecipientUserId, DigestDateLocal)`), its `ApprovalReminderDigestItems`,
and the `EmailOutbox` row (`CorrelationId = digest.Id`). SQL Server commits them atomically, so "digest recorded but
email never queued" cannot occur: either both exist or neither. Concurrency: two instances (or an overlapping run after
a restart) both attempt the insert; the unique index makes exactly one commit succeed, the other receives a
`DbUpdateException` with SQL error 2601/2627, logs `APPROVAL_REMINDER_DEDUP_SKIP`, and queues nothing. Transient
failures (deadlock 1205, timeout -2) are retried up to 3 times with a fresh context, each attempt re-running the same
insert so the unique key still arbitrates. Any other failure rolls back both rows and is recorded on the
`ApprovalReminderRuns` row and as `APPROVAL_REMINDER_RECIPIENT_FAILED`; the next day's run is unaffected because the
key is per local date. The outbox's own `(CorrelationId, RecipientEmail)` dedup is a second, independent guard. No
`ExecutionStrategy` retry is configured in `ApplicationDbContext`, so there is no hidden double-execution path.

**B2. Stale content at dispatch.** The body is rendered at queue time; the processor normally sends within 10 s, worst
case about 13 min across the three retries, then DEAD_LETTER. Measures: (a) the digest header states "situação às HH:mm
(Luanda)" and every line links to the live request, so a reader always lands on current state; (b) a new nullable
`EmailOutbox.ExpiresAtUtc` — the processor marks an entry `EXPIRED` instead of sending once past it (digests set 4 h);
a dead-letter retry can therefore never deliver yesterday's digest; (c) the next business day's digest is recomputed
from live data, so a resolved or reassigned approval simply disappears. Re-rendering at dispatch was considered and
rejected: it would couple the generic processor to reminder logic and still race with approvals happening seconds
later. Residual, documented: within the expiry window an approval resolved minutes before dispatch may still be listed;
the link shows it as resolved.

**B3. Oversized digests — still one email per approver.** `MaxItemsPerDigest` (default 50): the oldest 50 units are
listed in full; the remainder is one summary line "+ N aprovações pendentes adicionais" with a link to the approvals
center filtered to the user (`/approvals?mine=1`). A hard body cap (`MaxBodyBytes`, default 256 KB) is checked before
queuing; if still exceeded, the digest degrades to counts per stage plus the same link. Azure Communication Services
accepts far larger messages (10 MB), so the cap exists for readability and client rendering, not transport.

### C. Alternative approvers — confirmed business rule vs current implementation

**Rule:** approvers of a stage are alternatives; the first eligible approval completes the stage; eligibility is
recomputed every cycle; a completed stage disappears from everyone's digests.

| Concern | Area stage (today) | Final stage (today) |
|---|---|---|
| Who may act | Any active `DepartmentManager` of the request's department resolved by the cascade (plant-specific, else global) — `IsAreaManagerAsync`. **Already alternative.** | Anyone holding the **"Final Approver" role** whose plant/department scope includes the request (`RequestAccessScope`). The company nominee `Company.FinalApproverUserId` → `Request.FinalApproverId` is **not** checked. |
| Initial notification | All resolved managers (`QUOTATION_COMPLETED` / `REQUEST_SUBMITTED`, and after Phase 2 also batch creation). | **Only** `Request.FinalApproverId` (`AREA_APPROVED`). A second role-holder can approve but is never notified; a nominee without the role is notified but cannot approve. |
| Pending UI / queue | `areaQuery`: `AreaApproverId == user` OR cascade membership. | `finalQuery`: every scoped `WAITING_FINAL_APPROVAL` request for any role-holder. |
| In-app count | `AreaApproverId == user` OR "Area Approver" role (not the cascade) — defect 2.6 §3. | role-based. |
| Proforma alert | legacy nominee first, else cascade (defect 2.6 §2). | `FinalApproverId` only. |
| Submit validation | — | requires `Company.FinalApproverUserId` to be set. |
| Concurrency | legacy path: `Request.RowVersion` → `DbUpdateConcurrencyException` → 409 `APPROVAL_CONCURRENCY_CONFLICT`. Batch path: the request row is touched (`AreaApproverId`), so the race is caught but surfaces as an unhandled 500 for the loser. | legacy path: same 409. **Batch path: `ApprovalBatch` has no RowVersion and `BatchFinalApprove` does not modify the request row unless the status changes, so two final approvers can both pass the status guard and both commit** (double PO-group activation, two FINAL_APPROVED events). To confirm in TEST with two concurrent calls. |

**Changes needed for a consistent multi-approver final stage** (Phase 2b, to implement before or with the reminders):

1. **Model.** New `CompanyFinalApprovers (Id, CompanyId FK, UserId FK, IsActive, CreatedAtUtc)`, unique `(CompanyId, UserId)`.
   Migration backfills one row per company from `Company.FinalApproverUserId`. `Company.FinalApproverUserId` is kept for
   one release as a read-only fallback and then dropped. `Request.FinalApproverId` changes meaning to **"decided by"**
   (stamped at final approval/rejection), exactly as `AreaApproverId` already works after Phase B.
2. **Routing.** `IApprovalRoutingService.ResolveFinalApproversAsync(companyId)` / `IsFinalApproverAsync(userId, companyId)`:
   active users with the "Final Approver" role listed in `CompanyFinalApprovers` for the request's company. One service,
   used everywhere below — never a second copy of the rule.
3. **Authorization.** `ProcessFinalApproval` (legacy) and `BatchFinalApprove/Reject/RequestAdjustment` require role **and**
   `IsFinalApproverAsync(actor, request.CompanyId)` (admins keep override). Role alone no longer suffices.
4. **Workflow actions.** On final approve/reject stamp `request.FinalApproverId = actor`. Add `RowVersion` to
   `ApprovalBatch` (additive migration) and catch `DbUpdateConcurrencyException` in all batch actions → 409
   `APPROVAL_CONCURRENCY_CONFLICT`; keep PO-group activation idempotent (activate only groups still `PENDING`).
   Result: concurrent approvals of the same stage commit exactly once; the loser gets a clear 409 and the refreshed
   state; no second transition, no second event.
5. **Initial notifications.** `AREA_APPROVED` fans to all resolved final approvers; `FINAL_APPROVED/REJECTED` "decision
   registered" goes to the actor only. `APPROVAL_EMAIL_NO_RECIPIENT` is logged when a company has no eligible final approver.
6. **Submit validation.** "Company has at least one eligible final approver" replaces the single-nominee check.
7. **Pending UI.** `finalQuery` and the in-app count use the company-membership predicate, and `areaQuery`/count drop
   `AreaApproverId == user` in favour of the cascade only. The queue is status-driven, so a stage completed by a
   colleague disappears for the others without extra work.
8. **Proforma alerts and reminders.** Both resolve final recipients through the same routing method.
9. **Admin UI.** Master Data → Company: multi-select of final approvers replacing the single dropdown.

**Validation to ship with that change (two eligible approvers per stage):** both receive the stage email (two outbox
rows, same correlation); the pending queue and the digest preview list the unit for both; approver 1 approves → 200,
stage transition and one downstream event; approver 2's queue, in-app count and next-day digest no longer contain it;
approver 2 retries the same action → 400 (stage guard) or 409 (concurrent race), no second transition, no second
event, no second outbox row. Same script for area (two DepartmentManagers) and final (two CompanyFinalApprovers).

### D. Phase 2 — TEST validation checklist and expected evidence

Precondition: TEST has `SmtpSettings.RedirectAllToTestRecipient = 1` (set by `sync-prod-data-test.ps1`); every mail
lands in the TEST mailbox with `[TEST - IGNORE]` and the original recipients in the body. Record IDs as you go.

| # | Step | Expected evidence |
|---|---|---|
| 1 | Deploy build to TEST; `GET /api/app/version` | version/buildId of this change; TEST Valid |
| 2 | Pick a QUOTATION request with at least 2 quotations; Buyer creates batch #1 (2 lines) | `RequestStatusHistories`: `BATCH_CREATED`; `EmailOutbox`: rows with `EventCode = QUOTATION_COMPLETED`, `CorrelationId = that history Id`, one per routed area manager + requester, subject contains `(Lote #1)`; `AdminLogEntries`: `EMAIL_OUTBOX_QUEUED` then `EMAIL_OUTBOX_SENT`; mailbox receives them |
| 3 | Buyer creates batch #2 (remaining lines) on the same request | new `QUOTATION_COMPLETED` rows with a **different** `CorrelationId`, subject `(Lote #2)`; no `EMAIL_OUTBOX_DEDUP` event |
| 4 | Area manager approves batch #1 | `BATCH_AREA_APPROVED` history; `EmailOutbox` `AREA_APPROVED` rows (final approver + requester + area approver + buyer) with that history Id; mailbox |
| 5 | Repeat the same approve call (double-click / replay) | HTTP 400 "O lote não está em fase de aprovação da área"; **no** new outbox row |
| 6 | Area manager rejects batch #2 | `BATCH_AREA_REJECTED` history; `AREA_REJECTED` rows (requester, approver, buyer) |
| 7 | Final approver approves batch #1 | `BATCH_FINAL_APPROVED` history; `FINAL_APPROVED` rows; replay → 400, no new row |
| 8 | New request: create batch, area approve, final **reject** | `FINAL_REJECTED` row to the requester |
| 9 | Failure isolation | unit-tested only (orchestrator throws → action still 200, batch persisted); not reproducible safely in TEST |
| 10 | Scheduler: after deploy, Event Log / stdout | `Service started. Daily anchor: 07:00 UTC`, `Last recorded cycle: …`, `First run in …` |
| 11 | If deployed after 07:00 UTC with no cycle today | a `PROFORMA_DEADLINE_CYCLE` admin-log row within about 1 min of start (catch-up); `ProformaDeadlineAlerts` rows only for eligible PAYMENT requests |
| 12 | Restart the TEST API pool again the same day | `First run in <hours until tomorrow 07:00>`; **no** second cycle row that day |
| 13 | Next morning | exactly one `PROFORMA_DEADLINE_CYCLE` row at about 07:00 UTC |

### E. Which tests prove what

| Test class | Exercises | Does NOT exercise |
|---|---|---|
| `BatchWorkflowNotificationTests` (9) | the **real `ApprovalBatchController`** actions on an InMemory `ApplicationDbContext` with the real `GroupBuilderService`, eligibility, extra-item and adjustment-cycle services: guards, persistence, history rows, and the `WorkflowEvent` handed to the orchestrator (code, batch number, correlation = history Id, actor, comment, department); retry refusal; failure isolation | the orchestrator itself (mocked), hence recipient routing, templates, outbox rows, SMTP; `IRequestStatusSyncService` is mocked, so request-status side effects are not covered |
| `Request_reference_names_the_batch_only_for_batch_scoped_events` | the `FormatRequestRef` helper only | any rendering path |
| `ProformaDeadlineScheduleTests` (8) | the pure `ProformaDeadlineSchedule` arithmetic | the service loop (`DelayAsync`, `TryGetLastCycleUtcAsync`, cancellation) — covered by TEST steps 10–13 |

Nothing in the new suite runs `WorkflowNotificationOrchestrator` or `EmailOutboxProcessor` end to end; the pre-existing
suite does not either. TEST steps 2–8 are therefore the only end-to-end evidence for routing and delivery.

### F. The scheduler fix does **not** fix failed alerts being recorded as sent

`ProformaDeadlineAlertService` still writes a `ProformaDeadlineAlerts` row when `EmailSent = false` and its dedup
`(RequestId, AlertLevel, RecipientUserId)` treats that row as delivered, so one SMTP hiccup permanently silences that
level for that recipient; it still sends through `SmtpClient` directly, bypassing the outbox retries. **Recommendation:
fix this before enabling the service in PROD.** The minimal change is to queue through `EmailOutbox` (reusing the
`SendWorkflowNotificationAsync` template via the processor) and record the alert row only when the outbox row is
created, keying dedup on that row; a smaller stop-gap is to exclude `EmailSent = 0` rows from the dedup query. Until
then, enabling the service in PROD on a day with an SMTP problem would burn each request's alert levels silently.


---

## G. Implementation record (2026-10-07) — four separately reviewable changes

Everything below is in the working tree only. Commits are reserved for `/task-publish`. Files marked **[shared]** carry
hunks from more than one change; they are listed under every change they serve, so the four changes are reviewable as
file groups even though they cannot be split into disjoint patches.

### G.1 Change 1 — existing batch notifications + proforma scheduler

| File | What |
|---|---|
| `src/backend/AlplaPortal.Api/Controllers/ApprovalBatchController.cs` **[shared 2,4]** | `EmitBatchStageNotificationAsync`; emits `QUOTATION_COMPLETED` (or `REQUEST_SUBMITTED` for non-QUOTATION) on CreateBatch, `AREA_APPROVED`, `AREA_REJECTED`, `FINAL_APPROVED`, `FINAL_REJECTED` after the final SaveChanges; CorrelationId = the stage history row Id; orchestrator failure is logged, never fails the action |
| `src/backend/AlplaPortal.Infrastructure/Services/WorkflowNotificationOrchestrator.cs` **[shared 2]** | `FormatRequestRef` → "REQ-… (Lote #N)" in subjects/bodies |
| `src/backend/AlplaPortal.Infrastructure/Services/ProformaDeadlineSchedule.cs` (new) | pure anchor arithmetic (`ComputeInitialDelay`, `ComputeNextDelay`) |
| `src/backend/AlplaPortal.Infrastructure/Services/ProformaDeadlineAlertService.cs` **[shared 3]** | loop honours `CheckTimeUtcHour`; catch-up once per day from the last `PROFORMA_DEADLINE_CYCLE` admin-log row |
| tests `Services/Approvals/BatchWorkflowNotificationTests.cs` (9), `Services/ProformaDeadlineScheduleTests.cs` (8) | real controller on InMemory DbContext; pure schedule |

### G.2 Change 2 — routing consistency, alternative final approvers, concurrency protection

| File | What |
|---|---|
| `Domain/Entities/CompanyFinalApprover.cs` (new) | company ↔ user membership (IsActive, audit) |
| `Domain/Entities/Request.cs` | **new** `FinalDecisionByUserId` / `FinalDecisionBy` (decision actor). `FinalApproverId` is **not repurposed**: it keeps its historical "nominee at submit time" meaning, so every existing read (history, projections, old e-mails) stays valid |
| `Domain/Entities/ApprovalBatch.cs` **[shared 4]** | `RowVersion` (optimistic concurrency token) |
| `Application/DTOs/Requests/ApprovalRoutingDtos.cs`, `Application/Interfaces/IApprovalRoutingService.cs` | `ResolveFinalApproversAsync(companyId)`, `IsFinalApproverAsync(userId, companyId)`, `GetFinalApproverCompanyIdsAsync(userId)` |
| `Infrastructure/Services/Approvals/ApprovalRoutingService.cs` | eligible = active user, non-empty e-mail, holds "Final Approver" role, and (member of `CompanyFinalApprovers` **or** legacy `Company.FinalApproverUserId`) — union semantics; one rule used everywhere |
| `Infrastructure/Data/ApplicationDbContext.cs` **[shared 3,4]** | DbSet + unique (CompanyId, UserId), FKs; `RowVersion` mapping; `FinalDecisionBy` FK |
| `Infrastructure/Data/Migrations/20261007133052_AddCompanyFinalApproversAndBatchConcurrency.cs` (+Designer, snapshot) | schema + `BackfillSql` (see G.5) |
| `Api/Filters/ApprovalConcurrencyExceptionFilter.cs` (new) | `DbUpdateConcurrencyException` → 409 `APPROVAL_CONCURRENCY_CONFLICT` (applied to `ApprovalBatchController`) |
| `Api/Controllers/ApprovalBatchController.cs` **[shared]** | `CanActAsFinalApproverAsync` on BatchFinalApprove/Reject/RequestAdjustment (admin override); stamps `FinalDecisionByUserId` |
| `Api/Controllers/RequestsController.cs` | request-level `ProcessFinalApproval` requires company eligibility for non-admins and stamps `FinalDecisionByUserId`; pending-approvals `finalQuery` scoped to the user's eligible companies |
| `Infrastructure/Services/NotificationService.cs` | in-app pending count uses the DepartmentManagers cascade (area) and company membership (final) |
| `Infrastructure/Services/WorkflowNotificationOrchestrator.cs` **[shared]** | `AREA_APPROVED` fans out to all eligible final approvers (fallback: legacy `FinalApproverId`; else `APPROVAL_EMAIL_NO_RECIPIENT`); `FINAL_APPROVED` goes to the actor + requester + buyer |
| `Api/Controllers/LookupsController.cs`, `src/frontend/src/pages/Settings/MasterData.tsx` | companies expose/accept `FinalApproverUserIds`; candidates are validated (active, e-mail, role, company scope) and invalid ones are returned as a 400 list — **no permission is granted to fix a candidate**; UI: "Aprovadores Finais Alternativos" checkbox list |
| `scripts/db/company-final-approvers-backfill-preflight-readonly.sql` (new) | read-only: who will be inserted, who will **not** and why, role-holders with recent decisions who are not nominees |
| tests `FinalApproverRoutingTests` (3), `AlternativeApproverNotificationTests` (5, real orchestrator), `ApprovalConcurrencyRelationalTests` (4, LocalDB) | relational tests: two concurrent BatchFinalApprove calls → one 200 and one 409/400, **one** `BATCH_FINAL_APPROVED` history row, **one** `PO_GROUP_ACTIVATED`, group `WAITING_PO`, **one** `FinalApproved` event; same for BatchAreaReject; second approver refused after the first; backfill SQL inserts only eligible rows and is idempotent |
| 6 existing test files (`RequestLevelApprovalBatchModelGateTests`, `PaymentFinalApprovalSupplierGuardTests`, `MultiGroupLifecycleGuardTests`, `PaymentFinalApprovalLinkageTests`, `PaymentPoActionAvailabilityTests`, `RequestLifecycleStabilizationTests`) | their routing mock now answers "eligible" (one line each): these actors are the company's final approver in business terms, and the request-level path now checks that |

### G.3 Change 3 — proforma alerts through the outbox, with real failure/retry handling

| File | What |
|---|---|
| `Infrastructure/Services/ProformaDeadlineAlertCycle.cs` (new) | eligibility (PAYMENT + NeedByDateUtc + WAITING_AREA/FINAL_APPROVAL), recipients via routing (area cascade / company final approvers), per recipient one `EmailOutbox` row (`PROFORMA_DEADLINE_<LEVEL>`, CorrelationId = alert Id, `ExpiresAtUtc` = now + 24 h) written **in the same SaveChanges** as the `ProformaDeadlineAlerts` row; a level whose latest outbox row is `DEAD_LETTER`/`EXPIRED` is **re-queued** on the next cycle against the same record (`QueuedCount`, `LastQueuedAtUtc`, note in `ErrorMessage`); `DescribeDelivery` → QUEUED / SENT / FAILED / SENT_LEGACY / FAILED_LEGACY, always read from the outbox row |
| `Infrastructure/Services/ProformaDeadlineAlertService.cs` **[shared 1]** | slim host delegating to the cycle |
| `Domain/Entities/ProformaDeadlineAlert.cs` | `OutboxEntryId`, `QueuedCount`, `LastQueuedAtUtc`; `EmailSent` documented as legacy |
| `Domain/Entities/EmailOutboxEntry.cs`, `Infrastructure/Services/EmailOutboxProcessor.cs` | `ExpiresAtUtc`; the processor marks a stale row `EXPIRED` (terminal) before sending. `EXPIRED` never matches the claim filter (PENDING / retryable FAILED) and is not PROCESSING, so stuck-row recovery cannot pick it up: **no row blocks**. `ProcessEntryAsync` made public for tests; behaviour unchanged |
| `Api/Controllers/Admin/AdminDiagnosticsController.cs` | `GET api/admin/diagnostics/proforma-alerts?days=30` — alert records with their outbox delivery status (queued ≠ delivered) |
| `Infrastructure/Data/Migrations/20261007135920_AddProformaAlertOutboxLinkAndOutboxExpiry.cs` | see G.5 |
| tests `Services/ProformaDeadlineAlertCycleTests.cs` (11) | atomic queueing per alternative final approver, dedup of pending/sent levels, re-queue after DEAD_LETTER/EXPIRED, legacy rows never retried, `IsExpired` boundaries |

### G.4 Change 4 — daily approval digests (disabled; dry-run when enabled)

| File | What |
|---|---|
| `Domain/Entities/ApprovalReminder.cs` (new) | `ApprovalReminderRun` (counters incl. `UnitsWithoutStageEntry`, `UnitsWithoutRecipient`, `SkippedDedup`, `SkippedAllowList`, `Failed`, `Error`), `ApprovalReminderDigest` (subject, rendered body, outbox link), `ApprovalReminderDigestItem` (which unit, which stage entry, which age) |
| `Domain/Entities/ApprovalBatch.cs` **[shared 2]**, `Api/Controllers/ApprovalBatchController.cs` **[shared]** | `StageEnteredAtUtc` stamped on CreateBatch, on area approval (entry into final) and on resubmission (re-entry into area) — the three transitions into a waiting stage |
| `Infrastructure/Services/Reminders/ApprovalReminderOptions.cs` | section `AppConfig:ApprovalReminders`; **defaults Enabled=false, DryRun=true**; 08:00, `W. Central Africa Standard Time` (fallback fixed +01:00), MinPendingAgeDays 3, Mon–Fri, MaxItemsPerDigest 50, MaxBodyBytes 256 KB, DigestExpiryHours 4, RecipientAllowList, ApprovalsCenterPath |
| `…/ApprovalReminderSchedule.cs` | pure: next business-day send instant; restart catch-up only if today is a business day, the send time passed and no run row exists for today; `DaysPending` in Luanda calendar days |
| `…/PendingApprovalUnitQuery.cs` | mirrors the Approvals Center projection: one unit per batch in WAITING_AREA/FINAL_APPROVAL, plus one per batch-less request in an approval status (QUOTATION requests with batches never yield a request unit); stage entry = `StageEnteredAtUtc` / latest history row into the current status; **units without an established stage entry are reported, never guessed**; recipients exclusively from `IApprovalRoutingService` |
| `…/ApprovalReminderDigestRenderer.cs` | oldest 50 listed, "+N" overflow with link, byte cap → degraded counts-only body; header states the as-of time (Luanda) and that authorization is checked when a link is opened |
| `…/ApprovalReminderDigestCycle.cs` | run row first; per recipient **digest + items (+ outbox row in live mode) in one SaveChanges**; pre-check + unique index `(RecipientUserId, DigestDateLocal, DryRun)` = persistent dedup (unique violation → `SkippedDedup`, 3 transient retries, then `Failed` + `APPROVAL_REMINDER_RECIPIENT_FAILED`); dry run stores the digest and creates **no** outbox row; allow-list; `PreviewAsync` persists nothing; admin-log events `APPROVAL_REMINDER_CYCLE / QUEUED / DRYRUN / DEDUP_SKIP / NO_RECIPIENT / RECIPIENT_FAILED` |
| `…/ApprovalReminderDigestService.cs` | hosted scheduler; idle while disabled |
| `Api/Controllers/Admin/AdminApprovalRemindersController.cs` (new) | System Administrator only: `GET config`, `GET runs`, `GET runs/{id}/digests` (delivery status from the outbox), `GET digests/{id}` (body + items), `POST preview` (in-memory, nothing persisted or queued) |
| `Api/Program.cs`, `Api/appsettings.json` | options + scoped cycle + hosted service; `AppConfig:ApprovalReminders` with `Enabled=false`, `DryRun=true` |
| `Infrastructure/Data/Migrations/20261007141636_AddApprovalReminderDigestsAndBatchStageEntry.cs` | schema + `StageEntryBackfillSql` (G.5) |
| `scripts/db/approval-batch-stage-entry-preflight-readonly.sql` (new) | read-only: how the backfill classifies every waiting batch (R1/R2/R3) and **which batches stay NULL**, with their history rows for manual review; post-migration section D |
| tests `Services/Reminders/ApprovalReminderScheduleTests` (8), `ApprovalReminderDigestRendererTests` (4), `ApprovalReminderDigestCycleTests` (13), `ApprovalReminderRelationalTests` (3, LocalDB) | see G.6 |

**Digest staleness (documented behaviour).** The body is rendered at queue time; the outbox row carries
`ExpiresAtUtc = queue time + 4 h`, so a digest that could not be delivered within its window is marked `EXPIRED` and never
sent later. Within the window a unit decided minutes earlier may still be listed; the body says so, every line links to the
live request, and the portal enforces authorization when the link is followed (the digest grants nothing). The next
business day's digest is recomputed from live data.

**Legacy nominees.** A `Request.AreaApproverId` nominee who is not an active DepartmentManager still sees the request in
the queue (unchanged) but receives no digest, because the cascade decides who can act. This is the approved decision 4.

### G.5 Migration impact (all additive; applied to the DEV clone only)

| Migration | Schema | Data | Clone result (PROD backup 2026-09-07) |
|---|---|---|---|
| `20261007133052_AddCompanyFinalApproversAndBatchConcurrency` | new `CompanyFinalApprovers` (unique CompanyId+UserId; FK Companies cascade, Users restrict); `Requests.FinalDecisionByUserId` nullable + index + FK Users restrict; `ApprovalBatches.RowVersion` rowversion | `BackfillSql`: one row per company from `Company.FinalApproverUserId` **only if** the user is active, has an e-mail and holds "Final Approver"; otherwise not inserted (the preflight lists why). Idempotent | preflight A: 2 rows will be inserted (AlplaPLASTICO, AlplaSOPRO → the same nominee); B: 0 rejected; C: 0 other role-holders with recent final decisions; D: 3 role-holders. Post: 2 rows present |
| `20261007135920_AddProformaAlertOutboxLinkAndOutboxExpiry` | `ProformaDeadlineAlerts.OutboxEntryId` (FK EmailOutbox set-null, index), `QueuedCount`, `LastQueuedAtUtc`; `EmailOutbox.ExpiresAtUtc` nullable | none | applied |
| `20261007141636_AddApprovalReminderDigestsAndBatchStageEntry` | `ApprovalBatches.StageEnteredAtUtc` nullable; new `ApprovalReminderRuns`, `ApprovalReminderDigests` (unique RecipientUserId+DigestDateLocal+DryRun; FK Runs cascade, Users restrict, EmailOutbox set-null), `ApprovalReminderDigestItems` (FK digest cascade) | `StageEntryBackfillSql`, deterministic: R1 final stage ← latest `BATCH_AREA_APPROVED` row of the lot; R2 area stage ← latest `BATCH_RESUBMITTED` row of the lot; R3 area stage never left (no adjustment/resubmit/edit/approval row mentions the lot) ← batch `CreatedAtUtc`; **else NULL and reported** (excluded from digests). Settled batches untouched. Idempotent | preflight: 5 waiting batches (all WAITING_AREA_APPROVAL), all R3, **0 unestablished**; post-migration D: 5 established / 0 NULL |

Nullable column additions and the new tables are metadata-only; the `rowversion` column on `ApprovalBatches` rewrites that
(small) table. `Down` removes everything added. Nothing existing is renamed, dropped or repurposed. `dotnet ef database
update` on the clone applied the three migrations in sequence without error.

### G.6 Test evidence

| Suite | Result |
|---|---|
| Full backend suite (`AlplaPortal.Application.Tests`, InMemory + LocalDB) | **2731 passed, 0 failed** (2680 before changes 2–4; +51 new tests) |
| Frontend `tsc -b` | exit 0 |
| Change 1 | 17 tests (G.1) |
| Change 2 | 12 tests incl. 4 relational: concurrent approvals commit exactly once (one transition, one PO-group activation, one event); backfill eligibility + idempotence |
| Change 3 | 11 tests |
| Change 4 | 28 tests: schedule (catch-up on restart, no catch-up on weekends, exact-send-time edge, Luanda calendar days vs UTC); renderer (cap/overflow/degrade/encoding); cycle on InMemory with the **real routing service** and two alternative approvers per stage (dry run → digests without outbox; live → outbox atomic with `ExpiresAtUtc`; same-day restart → all dedup-skipped; dry-run does not block live; allow-list; preview persists nothing; **reassignment** of an area manager; no-recipient reporting; missing stage entry reporting; no double counting); the outbox processor on digest rows (**SMTP failure → FAILED with backoff → DEAD_LETTER after MaxRetries, same row, nothing re-queued**; stale row → `EXPIRED` without calling SMTP; success → SENT); relational on LocalDB (**two overlapping instances** racing the same day → exactly one digest and one outbox row per recipient, losers dedup-skipped, none failed; restart dedup and a direct duplicate INSERT refused by the unique index; backfill R1/R2/R3/R0 incl. lot #1 vs #10 and settled batches, idempotent) |

What the tests do **not** prove: SMTP transport, the Azure relay, the hosted loops under a real host clock, and the admin
endpoints over HTTP. Those are TEST items (G.7).

### G.7 Dry-run digest preview (DEV clone, PROD data as of 2026-09-07)

Read-only SQL rendition of the unit/recipient rules, run after the migrations on the clone with "now" = 2026-09-08 08:00 Luanda
(the morning after the snapshot). This is not the service itself; the service logic is covered by G.6.

| Measure | Value |
|---|---|
| Units considered (batches waiting + batch-less requests in an approval status) | 25 (10 area, 15 final) |
| Units without an established stage entry | 0 |
| Units eligible (> 3 Luanda calendar days in stage) | 8 |
| Eligible units with no resolvable approver | 0 |
| Digests that would be produced | 3 |

| Recipient | Items | Stage | Oldest (days) | Subject |
|---|---|---|---|---|
| Biavanga Joao | 5 | area | 19 | Lembrete: 5 aprovações pendentes há mais de 3 dias |
| Carolina Modesto | 2 | area | 22 | Lembrete: 2 aprovações pendentes há mais de 3 dias |
| Nelson Abreu | 1 | final | 5 | Lembrete: 1 aprovação pendente há mais de 3 dias |

### G.8 Remaining operational checks (TEST; none executed — no TEST access from this workstation)

Preconditions: TEST `SmtpSettings.RedirectAllToTestRecipient = 1`; record IDs as you go; PROD untouched.

1. Run `scripts/db/company-final-approvers-backfill-preflight-readonly.sql` and
   `scripts/db/approval-batch-stage-entry-preflight-readonly.sql` on TEST **before** migrating; review sections B and C
   (who is not inserted / which batches stay NULL).
2. Deploy; apply the three migrations; rerun both preflights (section D) and `GET /api/app/version`.
3. Change 1 end-to-end: section D steps 2–13 above (batch create/approve/reject e-mails with "(Lote #N)", replay → 400 and
   no new outbox row, scheduler log lines, single `PROFORMA_DEADLINE_CYCLE` per day across a restart).
4. Change 2: Master Data → company with two alternative final approvers (both role-holders); submit → both receive
   `AREA_APPROVED`; approver 1 approves (200), approver 2 repeats → 400/409, one `BATCH_FINAL_APPROVED`, one
   `PO_GROUP_ACTIVATED`, one e-mail set; approver 2's queue/count no longer lists it; same script for the request-level
   (PAYMENT) path; a role-holder of another company gets 403 "não é um aprovador final elegível".
5. Change 3: with an eligible PAYMENT request near its NeedBy date, one cycle → `ProformaDeadlineAlerts` rows **and**
   outbox rows (`PROFORMA_DEADLINE_*`), `GET api/admin/diagnostics/proforma-alerts` shows QUEUED → SENT; force a failure
   (invalid recipient) → DEAD_LETTER → next cycle re-queues the same record (`QueuedCount` 2).
6. Change 4, dry run: set `AppConfig:ApprovalReminders:Enabled=true` (DryRun stays true) in TEST config only; after
   08:00 Luanda one `ApprovalReminderRuns` row, digests visible via `GET api/admin/approval-reminders/runs/{id}/digests`
   with `DeliveryStatus = DRY_RUN`, **no** outbox rows; `POST preview` returns the same content and writes nothing;
   restart the pool the same day → `SkippedDedup` = recipients, no second digest.
7. Change 4, bounded live day: `DryRun=false`, `RecipientAllowList` = the TEST mailbox user only; one outbox row with
   `ExpiresAtUtc = +4 h` → SENT in the redirected mailbox; stop the SMTP relay for one cycle → FAILED/retries then
   DEAD_LETTER on the **same** row; set `ExpiresAtUtc` in the past on a pending row → `EXPIRED`, nothing sent; next
   business day → new digest; weekend → no run.
8. Only after 6–7 pass: decide the PROD rollout (`Enabled=true, DryRun=true` first; then `DryRun=false` with an allow-list).
   PROD stays `Enabled=false` with this change.
