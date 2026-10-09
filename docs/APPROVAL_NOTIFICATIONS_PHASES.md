# Approval Notifications — Phase 1 (live checks), Phase 2 (fixes), Phase 3 (reminder design)

> Status 2026-10-07: Phase 1 = read-only check set prepared (no live PROD access from the workstation); Phase 2 =
> implemented locally with tests, **not deployed**; Phase 3 = design for review, **not implemented**.
> Code basis: `Portal-Gerencial-rev1` @ `1fd1e74` (= PROD build 2.245.12). Nothing committed; no PROD change; no email sent.

---

## Phase 1 — Verify the current production state

### 1.1 What is already confirmed (from the PROD backup of 2026-09-07 10:27–15:08 UTC, before any local write)

| Finding | Evidence |
|---|---|
| Event emails worked up to 7 Sep 10:27 UTC | `EmailOutbox`: 3,023 SENT, 0 DEAD_LETTER (207 REQUEST_SUBMITTED, 39 QUOTATION_COMPLETED, 546 AREA_APPROVED) |
| **No pending-approval reminder exists** | only timers: `EmailOutboxProcessor`, `ProformaDeadlineAlertService`, `OcrCleanupService` |
| **Batch flow sent no notifications** | `ApprovalBatchController` emitted only adjustment/resubmit events; REQ-214/263/274/278 (dept 11) in area approval since 26–31 Aug with no approval mail queued |
| **Proforma service never completed a cycle in PROD** | 0 `PROFORMA_DEADLINE_CYCLE` events and 0 `ProformaDeadlineAlerts` rows between 3 Jun and 7 Sep, while the outbox processor in the same process worked |
| Scheduler ignored `CheckTimeUtcHour` | `WaitUntilNextCheckTimeAsync` slept 30 s then fixed 24 h |

Everything in the local clone after 7 Sep 15:08 UTC is local-development noise (subjects prefixed `[DEV LOCAL - IGNORE]`,
SMTP password cleared by `dev-safety-neutralization.sql`) and must not be read as PROD.

### 1.2 Unresolved — needs the live checks below

1. Current pending approvals, their stage-entry dates and routed recipients (cascade: plant-specific → global → none).
2. Outbox health after 7 Sep (DEAD_LETTER/FAILED, stale PENDING = processor not alive).
3. Whether the proforma service has run since 7 Sep, its effective `Enabled` flag, and any startup/cycle exception.
4. App pool start mode / idle timeout / recycling (background services live only while `w3wp` lives).

### 1.3 Database checks (read-only)

`scripts/db/notifications-phase1-readonly-checks.sql` — columns verified against the 2.245.12 schema. Run on
`[Portal-Gerencial-Test]` first, then `[Portal-Gerencial]`:

```
sqlcmd -S <instance> -d Portal-Gerencial -E -b -W -i scripts\db\notifications-phase1-readonly-checks.sql -o phase1-PROD.txt
```

Sections: A1 pending approvals with `StageEnteredUtc`, `RoutedSource` (`PLANT_SPECIFIC` / `GLOBAL` / `LEGACY_NOMINEE` /
`FINAL_APPROVER` / `NONE`), `RoutedRecipients`, current-stage mails queued/sent, open batches; A2 open batches with
their own stage entry; B outbox status, stale rows, last SENT, recent errors; C proforma cycles, alert rows, requests
eligible today; D SMTP presence (redacted); E notification errors (30 days). Return the whole file.

### 1.4 Host checks on AOVIA1VMS011 (read-only)

Pool names come from `docs/GITHUB_ACTIONS_PROD_DEPLOYMENT.md` (`AlplaPortal-Prod-Api-Pool`) and the deploy
workflow's TEST safety check (`AlplaPortal-Test-Api-Pool`); the API path from the same doc
(`C:\Apps\AlplaPortal\Prod\api`). Confirm both with the first two commands before trusting the rest.

```powershell
Import-Module WebAdministration
Get-ChildItem IIS:\AppPools | Select-Object Name, State, startMode, @{n='IdleTimeoutMin';e={$_.processModel.idleTimeout.TotalMinutes}}, @{n='RegularRecycleMin';e={$_.recycling.periodicRestart.time.TotalMinutes}}
Get-ChildItem IIS:\Sites | Select-Object Name, PhysicalPath, applicationPool

# Effective alert configuration (non-secret key only) in the preserved production settings
$api = 'C:\Apps\AlplaPortal\Prod\api'
(Get-Content (Join-Path $api 'appsettings.Production.json') -Raw | ConvertFrom-Json).AppConfig.ProformaDeadlineAlerts

# Environment variables THE WORKER PROCESS sees (not your interactive session): applicationHost.config pool-level
# variables and the API web.config <aspNetCore><environmentVariables>. Values of names containing SECRET/PASSWORD/KEY are not printed.
Get-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter "system.applicationHost/applicationPools/add[@name='AlplaPortal-Prod-Api-Pool']/environmentVariables" -name '.' |
  Select-Object -ExpandProperty Collection | Where-Object { $_.name -notmatch 'SECRET|PASSWORD|KEY|CONNECTION' } | Select-Object name, value
[xml]$wc = Get-Content (Join-Path $api 'web.config'); $wc.configuration.'system.webServer'.aspNetCore.environmentVariables.environmentVariable |
  Where-Object { $_.name -notmatch 'SECRET|PASSWORD|KEY|CONNECTION' } | Select-Object name, value
$wc.configuration.'system.webServer'.aspNetCore | Select-Object processPath, arguments, hostingModel, stdoutLogEnabled, stdoutLogFile

# Service start / disabled / exception lines since the last deploy (23 Sep 2026)
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date '2026-09-23')} |
  Where-Object { $_.Message -match 'ProformaDeadlineAlerts|EmailOutboxProcessor' } |
  Select-Object TimeCreated, ProviderName, LevelDisplayName, @{n='Msg';e={$_.Message.Substring(0,[Math]::Min(400,$_.Message.Length))}}
Get-ChildItem (Join-Path $api 'logs') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name, LastWriteTime
Select-String -Path (Join-Path $api 'logs\*') -Pattern 'ProformaDeadlineAlerts' -ErrorAction SilentlyContinue | Select-Object -Last 20 Line
```

Interpretation: `Service is DISABLED via configuration` → the flag is false in the server-side settings or an
environment variable; `Service started` with no `PROFORMA_DEADLINE_CYCLE` rows → look for the exception line that
follows; an idle timeout > 0 with `startMode = OnDemand` → background services stop whenever the pool idles.

---

## Phase 2 — Fixes implemented (local, tests green; not deployed)

### 2.1 Batch workflow notifications

`src/backend/AlplaPortal.Api/Controllers/ApprovalBatchController.cs`

- New `EmitBatchStageNotificationAsync(request, batch, eventCode, correlationId, actorId, comment)`: builds the
  `WorkflowEvent` with `BatchNumber`, dispatches through the existing `IWorkflowNotificationOrchestrator`, and swallows
  + logs any failure so a committed action is never reported as failed.
- Wired **after the final `SaveChanges`** of each action:
  - `CreateBatch` → `QUOTATION_COMPLETED` (requester "Cotação Concluída" + area managers from the DepartmentManager
    cascade with "[AÇÃO NECESSÁRIA] Aprovação de Área pendente"); `REQUEST_SUBMITTED` for a non-quotation request.
  - `BatchAreaApprove` → `AREA_APPROVED` (Final Approver + requester / area approver / buyer).
  - `BatchAreaReject` → `AREA_REJECTED`; `BatchFinalApprove` → `FINAL_APPROVED`; `BatchFinalReject` → `FINAL_REJECTED`.
  - `ResubmitBatch` already emitted `BATCH_RESUBMITTED_TO_AREA`; adjustments already emitted their events. Unchanged.
- **Correlation** = the stage's own `RequestStatusHistories` row Id (`BATCH_CREATED`, `BATCH_AREA_APPROVED`, …), the
  same convention the legacy `RequestsController` uses. Consequences, all tested: a second batch on the same request has
  its own correlation (never deduped against the first); each transition has its own correlation; the outbox and in-app
  dedup (`CorrelationId` + recipient) still collapse re-processing of the same event; a retry of a committed action is
  refused by the stage guard (400) and emits nothing.
- Recipient routing, templates and dedup are the orchestrator's existing ones — inspected: area recipients come
  exclusively from `ApprovalRoutingService.ResolveAreaManagersAsync` (plant-specific, else global; `APPROVAL_EMAIL_NO_RECIPIENT`
  admin-log error when none), final recipient is `Request.FinalApproverId`.

`src/backend/AlplaPortal.Infrastructure/Services/WorkflowNotificationOrchestrator.cs`

- `FormatRequestRef(evt)`: batch-scoped events render the reference as `REQ-… (Lote #N)` in subjects/bodies so an approver
  with several lots on one request sees which one is meant; request-level events are byte-identical to before.

### 2.2 Proforma scheduler

`src/backend/AlplaPortal.Infrastructure/Services/ProformaDeadlineSchedule.cs` (new, pure) and
`ProformaDeadlineAlertService.cs`:

| Situation | Behaviour now |
|---|---|
| Startup before today's anchor (`CheckTimeUtcHour`) | 30 s warm-up, then wait until the anchor |
| Startup/restart after the anchor, no completed cycle today | 30 s warm-up, then **run immediately** (catch-up — the IIS worker may not have been alive at the anchor) |
| Startup/restart after the anchor, cycle already completed today | wait until tomorrow's anchor |
| Subsequent cycles | next anchor strictly after "now" (drift-free; no fixed 24 h sleep) |
| Cancellation (host stopping) | every delay is cancellation-aware; the loop exits and logs "Service stopped" |

"Completed today" is read from the `PROFORMA_DEADLINE_CYCLE` admin-log event the cycle already writes — no schema
change. `CheckIntervalHours` is retained in configuration but is now informational (logged at startup).

### 2.3 Tests (xunit, InMemory) — 17 new, full suite 2,680 / 2,680 green

- `tests/.../Services/Approvals/BatchWorkflowNotificationTests.cs` (9): create emits once with batch number + history
  correlation; two batches → two events with distinct correlations and batch numbers 1/2; area approve, area reject,
  final approve, final reject each emit once with the stage's history correlation and the actor's comment; a retry
  after commit is refused (400) and emits nothing more; the full chain emits exactly three events; an orchestrator
  exception does not change the HTTP result nor the persisted batch; `FormatRequestRef` names the lot only for batch events.
- `tests/.../Services/ProformaDeadlineScheduleTests.cs` (8): startup before/after anchor, catch-up, already-ran-today,
  pre-anchor cycle not counting, next-anchor strictness, clamping.

### 2.4 TEST validation (to run; SMTP redirects everything to the TEST recipient per `sync-prod-data-test.ps1`)

1. Deploy to TEST via the normal workflow. Confirm `/api/app/version`.
2. Buyer: create a batch on a QUOTATION request → expect `EMAIL_OUTBOX_QUEUED` rows (subject contains `(Lote #1)`) for
   the requester and each routed area manager, then `EMAIL_OUTBOX_SENT`; the TEST mailbox receives them with the
   `[TEST - IGNORE]` prefix and the original recipients in the body.
3. Area approver: approve the batch → `AREA_APPROVED` mail to the company's final approver (+ requester/buyer). Repeat
   the approve call → 400, no new outbox row.
4. Create a second batch on the same request → its own `QUOTATION_COMPLETED` rows (not deduped).
5. Reject paths once each (area reject on a fresh batch; final reject after area approval).
6. Scheduler: Event Log / stdout should show `Service started. Daily anchor: 07:00 UTC`, `Last recorded cycle`, `First run
   in …`; after the first cycle a `PROFORMA_DEADLINE_CYCLE` row appears; restart the TEST pool after 07:00 UTC → a
   catch-up cycle only if none ran today.

### 2.5 Production configuration recommendations (conditional on Phase 1 evidence)

- If `AppConfig:ProformaDeadlineAlerts:Enabled` is false in `appsettings.Production.json` or via a pool environment
  variable: decide explicitly whether to enable it; the Phase 2 scheduler makes enabling safe (one cycle per day, dedup).
- If the API pool idles out (`idleTimeout` > 0, `startMode = OnDemand`): consider `startMode = AlwaysRunning` and
  `idleTimeout = 0` for the API pool so background processing is continuous. The catch-up logic covers the gap either way.

### 2.6 Additional defects found, deliberately NOT fixed here

1. **Failed proforma alerts are never retried.** `ProformaDeadlineAlertService` records a `ProformaDeadlineAlerts` row even
   when `EmailSent = false`, and the dedup key `(RequestId, AlertLevel, RecipientUserId)` treats that row as "sent", so a
   transient SMTP failure permanently suppresses that level. It also emails directly through `SmtpClient` instead of the
   outbox, bypassing retry and dead-letter handling. Fix proposal (separate change): queue through `EmailOutbox` and let
   the dedup consider only rows with `EmailSent = 1`, or key dedup on the outbox entry.
2. **Legacy `AreaApproverId` nominee divergence.** The proforma alert routes `WAITING_AREA_APPROVAL` to `Request.AreaApproverId`
   when set, while the orchestrator ignores it and always uses the DepartmentManager cascade. `BatchAreaApprove` also writes
   `AreaApproverId = actor`. Alerts and approval mails can therefore go to different people for the same request.
3. **In-app pending count ignores DepartmentManager routing.** `NotificationService.GetNotificationsAsync` counts area
   approvals by `AreaApproverId == user` or the "Area Approver" role, not by the cascade.

---

## Phase 3 — Recurring pending-approval reminders (design for review)

### 3.1 Business rules (as given) → design decisions

| Rule | Decision |
|---|---|
| One consolidated email per approver per business day | one `ApprovalReminderDigest` row per (recipient, local date); Mon–Fri only, Angola time |
| Pending > 3 calendar days in the current stage | `MinPendingAgeDays = 3` applied to the **approval unit's** stage entry (below) |
| Angola local time | `TimeZoneId = "W. Central Africa Standard Time"` (UTC+1, no DST); `SendTimeLocal = "08:00"` |
| Request number, stage, time waiting, direct link | per unit: `REQ-…`, `(Lote #N)` when batch-scoped, stage label, `Nd Nh`, `/requests/{id}?mode=view` |
| Stop when resolved or no longer responsible | recipients are recomputed on every run from live routing; nothing is "subscribed" |
| Existing routing service and outbox | `IApprovalRoutingService.ResolveAreaManagersAsync`; `EmailOutbox` with `EventCode = APPROVAL_REMINDER_DIGEST` |
| Proforma alerts independent | separate service, table and event code; no shared dedup |

### 3.2 The approval unit — request or batch?

Responsibility and waiting age belong to **the thing the approver can act on**:

- **Batch model** (QUOTATION requests with `ApprovalBatches`): the unit is the batch. Age = batch stage entry
  (`BATCH_CREATED` / `BATCH_RESUBMITTED` for area; `BATCH_AREA_APPROVED` for final — from `RequestStatusHistories`, or a new
  `StageEnteredAtUtc` column on `ApprovalBatches` kept by the controller, which is cheaper and exact). Two open batches on
  one request → two lines, never a third request-level line.
- **Request model** (everything else: PAYMENT, legacy QUOTATION without batches): the unit is the request; age = newest
  history row that moved the request into its current status.
- Exclusion rule to avoid duplicates: when a request has ≥ 1 batch in `WAITING_AREA_APPROVAL` / `WAITING_FINAL_APPROVAL`, the
  request-level line is suppressed and only batch lines are produced. Batches in `AREA_ADJUSTMENT` / `FINAL_ADJUSTMENT` are
  the buyer's to act on and are **not** approvals; excluded.
- Recipients per unit: area stage → cascade for the request's department + plant; final stage → `Company.FinalApproverUserId`
  (active, with email). A unit with no resolvable recipient is reported (admin log `APPROVAL_REMINDER_NO_RECIPIENT`) and
  skipped — exactly the condition that today silently strands approvals.

### 3.3 Components

- `ApprovalReminderOptions` (bind `AppConfig:ApprovalReminders`): `Enabled` (default **false**), `DryRun` (default **true**),
  `SendTimeLocal` ("08:00"), `TimeZoneId`, `MinPendingAgeDays` (3), `BusinessDays` (Mon–Fri), `MaxItemsPerDigest` (50, rest
  summarised as "+N"), `RecipientAllowList` (optional, for first live day).
- `ApprovalReminderSchedule` (pure): next local send time → UTC; skip non-business days; startup catch-up only if today is a
  business day, the send time passed, and no run row exists for today (same pattern as Phase 2).
- `PendingApprovalUnitQuery`: single bounded query producing `(UnitKind, RequestId, BatchId?, BatchNumber?, Stage, StageEnteredUtc,
  DepartmentId, PlantId, CompanyId)` with the exclusion rule above; recipients resolved per unit; grouped by recipient.
- `ApprovalReminderDigestService` (`BackgroundService`): per run, writes one `ApprovalReminderRuns` row, then for each recipient
  **inserts the `ApprovalReminderDigests` row first** (unique `(RecipientUserId, DigestDateLocal)`), and only on success queues
  the outbox entry with `CorrelationId = digest.Id`. A unique-key violation means another instance already produced that
  digest today → skip. This is the restart- and multi-instance-safe dedup; the outbox adds its own `(CorrelationId, recipient)`
  dedup underneath.
- Retry: inherited from the outbox (30 s / 2 min / 10 min, then DEAD_LETTER). The digest row stores `OutboxEntryId`; its
  status is derived by joining the outbox, so an admin sees QUEUED / SENT / DEAD_LETTER per recipient per day.
- Dry run: the full pipeline runs, digest rows are written with `DryRun = 1` and the rendered HTML stored in `PayloadHtml`,
  **no outbox row is created**, and an `APPROVAL_REMINDER_DRYRUN` admin-log event lists recipients and unit counts.
- Admin verification (read-only endpoints under `api/admin/notifications/approval-reminders`): `GET runs` (last 30 runs with
  counts), `GET runs/{id}/digests` (recipient, units, outbox status), `GET preview` (executes the query + rendering on
  demand for the current moment, no persistence) — the operator's "what would go out right now".
- Admin-log events: `APPROVAL_REMINDER_CYCLE` (units, recipients, queued, skipped-dedup, no-recipient), `APPROVAL_REMINDER_QUEUED`
  per digest, `APPROVAL_REMINDER_DRYRUN`, `APPROVAL_REMINDER_NO_RECIPIENT`.

### 3.4 Schema (additive migration)

```
ApprovalReminderRuns      (Id uniqueidentifier PK, StartedAtUtc, CompletedAtUtc, LocalDate date, DryRun bit,
                           UnitsConsidered int, UnitsEligible int, Recipients int, DigestsQueued int, SkippedDedup int,
                           NoRecipientUnits int, Error nvarchar(max) null)
ApprovalReminderDigests   (Id uniqueidentifier PK, RunId FK, RecipientUserId FK Users, DigestDateLocal date,
                           ItemCount int, PayloadHtml nvarchar(max), OutboxEntryId uniqueidentifier null FK EmailOutbox,
                           DryRun bit, CreatedAtUtc)                      UNIQUE (RecipientUserId, DigestDateLocal)
ApprovalReminderDigestItems (Id PK, DigestId FK CASCADE, RequestId, BatchId null, Stage nvarchar(40),
                           StageEnteredUtc, DaysPending int)             -- audit of exactly what each digest listed
ApprovalBatches.StageEnteredAtUtc datetime2 null                        -- optional; maintained by CreateBatch/Resubmit/AreaApprove
```
`EmailOutbox.RequestId` is already nullable, so a digest (many requests) needs no outbox change; `RequestNumber` carries
"DIGEST" and `EventCode = APPROVAL_REMINDER_DIGEST`.

### 3.5 Rollout — no backlog burst by construction

1. Ship with `Enabled = true, DryRun = true` to TEST, then PROD. For 3–5 business days review `APPROVAL_REMINDER_DRYRUN`
   events and `GET preview`: recipients, counts, wording, and that batch/request lines never duplicate.
2. A digest is *one* email per approver per day regardless of how many units are old, so turning `DryRun` off cannot
   release a per-request backlog. The first live day can still be bounded with `RecipientAllowList` (one or two approvers) and
   `MaxItemsPerDigest`.
3. Flip `DryRun = false` for everyone; keep the per-day unique key, so a restart or a second instance can never resend.
4. Holidays: a later `ApprovalReminderHolidays (Date, Name)` table checked by the schedule; out of scope for v1.

### 3.6 Open points for the review

- Confirm the 08:00 Luanda send time and whether Saturday should ever count.
- Confirm that "time waiting" is measured from the current stage entry (not request creation) for both unit kinds.
- Decide whether to add `ApprovalBatches.StageEnteredAtUtc` (exact, cheap) or derive the batch age from history comments.
- Decide whether the legacy `AreaApproverId` nominee should receive reminders when the cascade resolves someone else
  (Phase 2.6 defect 2); the design follows the cascade only.

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

## Supplement carried over from the interim rebuild (2026-10-07)

The interim rebuild (archived as `docs/archive/APPROVAL_NOTIFICATIONS_PHASES.rebuilt-2026-10-07.md`) recorded three
further Phase 2 findings that the original text above lists only as "additional defects, not fixed" (2.6):

3. **Failed proforma alerts were recorded as sent and never retried; sending bypassed the outbox** — fixed in the
   retained change 3 (G.2).
4. **Final-stage routing inconsistent with authorization** (role-only authorization vs single-nominee notification;
   no concurrency guard on batch final approval) — the concurrency guard is retained (change 2); the routing /
   authorization redesign is **deferred** (G.0) and the gap is now documented and reported instead (G.1).
5. **In-app pending count ignored the DepartmentManagers cascade** — deferred with item 4 (unchanged behaviour).


---

## Reconciliation note (2026-10-07, evening)

This file is the **reconciled** version. The original (phases 1–3 and addendum A–F, written earlier on 2026-10-07)
was moved to the Windows Recycle Bin at 14:22 local by an Explorer/IDE-style delete outside this work (no git or
shell command in the session deleted it); it was recovered and is preserved verbatim as
`docs/archive/APPROVAL_NOTIFICATIONS_PHASES.original-2026-10-07.md`. The condensed rebuild written while the
original was missing is preserved as `docs/archive/APPROVAL_NOTIFICATIONS_PHASES.rebuilt-2026-10-07.md`. Everything
above this note is the original text; section G below is the implementation record **after scope separation**
(the rebuild's own section G described the pre-separation scope and is superseded, but kept in the archive).

## G. Implementation record after scope separation (2026-10-07)

Delivered as **v2.246.0** on `Portal-Gerencial-rev1`: `ee4b937` (batch e-mails), `24eae46` (scheduler), `4349338` (concurrency), `c0706fd` (proforma outbox), `d21a50d` (reminder digests), plus the release commit. Not deployed. PROD data, configuration and sending are unchanged.
The three new migrations are applied only to the DEV clone `Portal-Gerencial-Dev-ProdClone`.

### G.0 What was deferred and why

Deferred, preserved under `deferred/company-final-approvers/` (full pre-separation patch, per-file hunks, whole files,
the three superseded migrations, the backfill preflight and the tests that assumed the model):

- `CompanyFinalApprovers` table, its backfill, Master Data multi-select, `Request.FinalDecisionByUserId`;
- the **company-membership authorization restriction** on request-level and batch final approval;
- the queue (`finalQuery`) and in-app count realignment, the `AREA_APPROVED` fan-out to several approvers and the
  "confirmation to the actor" change.

Retained rule (unchanged from today): **who may approve the final stage = "Final Approver" role + access scope**. A
user without an e-mail can still approve; e-mail availability only decides whether a notification can be sent.

### G.1 Recipient rule for final-stage notifications and reminders (current model) and the reported gaps

| Message | Recipient | Source |
|---|---|---|
| `AREA_APPROVED` one-shot e-mail | `Request.FinalApproverId` (nominee at submit = company nominee then) | unchanged (`WorkflowNotificationOrchestrator`) |
| Proforma deadline alerts | request nominee if active with e-mail → else company nominee (`Company.FinalApproverUserId`) if active with e-mail → else none (`NoRecipient`) | `IApprovalRoutingService.ResolveFinalNotificationRecipientsAsync` |
| Reminder digests (final units) | same as proforma | same |
| Area stage (all three) | DepartmentManagers cascade (plant-specific, else global), active with e-mail | unchanged |

Differences between *can approve* and *is notified*, reported by `scripts/db/final-approval-recipients-vs-approvers-readonly.sql`:

1. A role-holder who is not the nominee **can approve but is never notified or reminded** (section B of the script).
2. A nominee without the role **is notified/reminded but cannot approve** (section A).
3. A nominee without an e-mail (or inactive) is **not notifiable**; the fallback is the company nominee; if neither is
   notifiable the unit is reported as `NO_RECIPIENT` (section C) and no role-holder is substituted.
4. When the request nominee differs from the current company nominee, e-mails and digests follow the **request**
   nominee (section D).

On the DEV clone (PROD data of 2026-09-07) the earlier preflight showed both companies' nominee is one person who
holds the role, two other role-holders are nominee of no company, and no pending final-stage request lacks a
notifiable nominee. Rerun the script on TEST/PROD before deploying.

### G.2 Retained changes and files

**Change 1 — batch workflow notifications + proforma scheduler** (no migration)

| File | Why |
|---|---|
| `src/backend/AlplaPortal.Api/Controllers/ApprovalBatchController.cs` **[shared 2,4]** | emits `QUOTATION_COMPLETED`/`REQUEST_SUBMITTED` (create), `AREA_APPROVED`, `AREA_REJECTED`, `FINAL_APPROVED`, `FINAL_REJECTED` after the final SaveChanges; correlation = stage history row Id; orchestrator failure logged, action unaffected |
| `src/backend/AlplaPortal.Infrastructure/Services/WorkflowNotificationOrchestrator.cs` | `FormatRequestRef` → "REQ-… (Lote #N)" (only change left in this file) |
| `src/backend/AlplaPortal.Infrastructure/Services/ProformaDeadlineSchedule.cs` (new), `…/ProformaDeadlineAlertService.cs` **[shared 3]** | daily anchor at `CheckTimeUtcHour`, one catch-up after restart |
| tests `Services/Approvals/BatchWorkflowNotificationTests.cs` (9), `Services/ProformaDeadlineScheduleTests.cs` (8) | |

**Change 2 — batch concurrency protection** (migration `20261007152909_AddApprovalBatchConcurrencyToken`)

| File | Why |
|---|---|
| `src/backend/AlplaPortal.Domain/Entities/ApprovalBatch.cs` **[shared 4]**, `…/Data/ApplicationDbContext.cs` **[shared]** | `RowVersion` concurrency token |
| `src/backend/AlplaPortal.Api/Filters/ApprovalConcurrencyExceptionFilter.cs` (new), `ApprovalBatchController.cs` attribute | `DbUpdateConcurrencyException` → 409 `APPROVAL_CONCURRENCY_CONFLICT` (was an unhandled 500) |
| tests `Services/Approvals/ApprovalConcurrencyRelationalTests.cs` (3, LocalDB) | two role-holders racing BatchFinalApprove / BatchAreaReject → one 200, one 409/400, one history row, one PO-group activation, one event; second approver refused after the first |

**Change 3 — proforma alerts through the outbox** (migration `20261007152958_AddProformaAlertOutboxLinkAndOutboxExpiry`)

| File | Why |
|---|---|
| `…/Services/ProformaDeadlineAlertCycle.cs` (new), `…/ProformaDeadlineAlertService.cs` | alert record + outbox row in one SaveChanges; DEAD_LETTER/EXPIRED levels re-queued; recipients: area cascade / single final nominee |
| `Domain/Entities/ProformaDeadlineAlert.cs`, `Domain/Entities/EmailOutboxEntry.cs` | `OutboxEntryId`, `QueuedCount`, `LastQueuedAtUtc`; `ExpiresAtUtc` |
| `…/Services/EmailOutboxProcessor.cs` | stale rows → terminal `EXPIRED` before sending; `ProcessEntryAsync` public for tests (behaviour unchanged) |
| `Api/Controllers/Admin/AdminDiagnosticsController.cs` | `GET api/admin/diagnostics/proforma-alerts?days=` (queued ≠ delivered) |
| `Application/DTOs/Requests/ApprovalRoutingDtos.cs`, `Application/Interfaces/IApprovalRoutingService.cs`, `…/Services/Approvals/ApprovalRoutingService.cs` | `ResolveFinalNotificationRecipientsAsync` (notification rule only, see G.1) |
| tests `Services/ProformaDeadlineAlertCycleTests.cs` (13), `Services/Approvals/FinalApproverRoutingTests.cs` (4) | incl. "role-holder not alerted", "nominee without e-mail → no recipient" |

**Change 4 — daily reminder digests, disabled** (migration `20261007153058_AddApprovalReminderDigestsAndBatchStageEntry`)

| File | Why |
|---|---|
| `Domain/Entities/ApprovalReminder.cs` (new), `ApprovalBatch.StageEnteredAtUtc` + 3 stamps in `ApprovalBatchController.cs` | units, runs, digests, items; stage-entry clock |
| `…/Services/Reminders/ApprovalReminderOptions.cs`, `ApprovalReminderSchedule.cs`, `PendingApprovalUnitQuery.cs`, `ApprovalReminderDigestRenderer.cs`, `ApprovalReminderDigestCycle.cs`, `ApprovalReminderDigestService.cs` (all new) | pipeline; `Enabled=false`, `DryRun=true` defaults; digest + items (+ outbox) in one SaveChanges; unique `(RecipientUserId, DigestDateLocal, DryRun)` |
| `Api/Controllers/Admin/AdminApprovalRemindersController.cs` (new), `Api/Program.cs`, `Api/appsettings.json` | read-only admin endpoints + in-memory preview; registrations; `AppConfig:ApprovalReminders` |
| `scripts/db/approval-batch-stage-entry-preflight-readonly.sql` (new) | backfill classification R1/R2/R3 and the rows that stay NULL |
| tests `Services/Reminders/ApprovalReminderScheduleTests` (8), `ApprovalReminderDigestRendererTests` (4), `ApprovalReminderDigestCycleTests` (13), `ApprovalReminderRelationalTests` (3, LocalDB) | |

**Operator scripts (new, read-only):** `scripts/db/notifications-phase1-readonly-checks.sql`,
`scripts/db/final-approval-recipients-vs-approvers-readonly.sql`, `scripts/server/check-proforma-alerts-config-readonly.ps1`.

**Restored pre-existing work:** `scripts/db/HR_MODULE_RESET_PLAN.md` and the five `hr-module-reset-*.sql` scripts
(recovered from the Recycle Bin, restored to their original paths, no file was overwritten). The REQ-362/366 set
recovered at the same time was not restored (not requested); it sits in the session scratchpad only.

### G.3 Migration chain (regenerated), DEV-clone reconciliation and validation

The previous chain (`…133052`, `…135920`, `…141636`) existed only in the working tree and only on the DEV clone.
Reconciliation, in this order: COPY_ONLY backup of the clone
(`C:\dev\db-backups\Portal-Gerencial-Dev-ProdClone_with-3-approval-migrations_20261007.bak`, verified) → Down of the
three old migrations with the old files still present → old files deleted, snapshot restored from HEAD → model trimmed
to the retained scope → new chain generated in three steps by exposing one model slice at a time → `has-pending-model-changes`
reports none → new chain applied to the clone (102 history rows, 5/5 waiting batches backfilled).

Incident during reconciliation, disclosed: the first Down command targeted the wrong migration name and EF reverted
seven **committed** migrations on the clone before a foreign-key failure stopped it. The clone was restored from the
backup taken seconds earlier and the revert redone with the correct target (`20260909145120_…`). Nothing outside the
DEV clone was touched.

| New migration | Schema | Data | Travels with |
|---|---|---|---|
| `20261007152909_AddApprovalBatchConcurrencyToken` | `ApprovalBatches.RowVersion` (rowversion) | none | change 2 code |
| `20261007152958_AddProformaAlertOutboxLinkAndOutboxExpiry` | `ProformaDeadlineAlerts.OutboxEntryId` (FK EmailOutbox set-null, index), `QueuedCount` (default 0), `LastQueuedAtUtc`; `EmailOutbox.ExpiresAtUtc` | none | change 3 code; required by change 4 (`ExpiresAtUtc`) |
| `20261007153058_AddApprovalReminderDigestsAndBatchStageEntry` | `ApprovalBatches.StageEnteredAtUtc`; `ApprovalReminderRuns`, `ApprovalReminderDigests` (unique dedup index), `ApprovalReminderDigestItems` | deterministic `StageEntryBackfillSql` (R1/R2/R3, else NULL, idempotent) | change 4 code |

Validation on a disposable copy of the clone (`Portal-Gerencial-MigrationChainCheck`, dropped afterwards):
baseline 99 → Up 102 (5 batches backfilled, dedup index unique, `QueuedCount` default `((0))`) → Down 99 (all new
objects gone) → Up 102 again (idempotent). A build **from an empty database** is not possible with this repository's
history regardless of this work: the committed migration `20260603152331_AddItemCatalogSourceCompanyFix` fails with
"column 'SourceCompany' specified more than once" (pre-existing defect, reported, not fixed).

### G.4 Rollback compatibility with data created by the new code (experiment on the disposable copy)

Rows were inserted exactly as the new code writes them, then the **previous build's** outbox claim, stuck-row recovery
and proforma dedup statements were executed verbatim (transaction rolled back):

| Row created by the new code | Previous build behaviour after a code rollback | Risk |
|---|---|---|
| Outbox `PENDING` past `ExpiresAtUtc` (digest or proforma) | claimed and **sent** — the old processor ignores expiry | stale digest/alert delivered once, within the retry window (minutes to about 13 min after rollback); no duplicate |
| Outbox `FAILED`, retryable, past `ExpiresAtUtc` | retried and sent | same as above |
| Outbox `EXPIRED` | never claimed, never recovered (not `PROCESSING`), never sent | none; terminal row, does not block |
| Outbox `PROCESSING` (crash) | recovered to `FAILED` after 5 min and retried | same as before this work |
| Outbox `DEAD_LETTER` | untouched | same as before |
| `ProformaDeadlineAlerts` row linked to an outbox row | old dedup `EXISTS(Request, Level, Recipient)` → **skips**; no duplicate e-mail | a level whose outbox row dead-lettered is never retried by the old code (pre-existing limitation; the new code re-queues it) |
| Digest / run / item rows | ignored (unknown tables) | none |
| Old code inserting alerts / outbox rows / updating batches on the new schema | works: `QueuedCount` default 0, nullable link columns, `rowversion` server-generated | alerts written by the old build are later treated as "legacy" by the new code (never re-queued) |

Conclusion: a code rollback without schema rollback is safe; the only behavioural difference is that messages the new
code would have expired may be sent late, once. To avoid even that, mark pending rows with a past `ExpiresAtUtc` as
`EXPIRED` manually before reverting (read-only check: `SELECT … FROM EmailOutbox WHERE Status IN ('PENDING','FAILED') AND ExpiresAtUtc < SYSUTCDATETIME()`).
Schema rollback (`Down`, newest first) removes only objects and data created by these features.

### G.5 Configuration: reminders stay off; proforma must be confirmed

- `appsettings.json` ships `AppConfig:ApprovalReminders` with `Enabled=false`, `DryRun=true`. The deploy workflows
  preserve the server-side `appsettings.Test.json` / `appsettings.Production.json`, which do not contain the section, so
  the committed defaults apply until an operator edits the server file or sets `AppConfig__ApprovalReminders__Enabled`.
  The hosted service exits immediately while disabled.
- `appsettings.json` ships `AppConfig:ProformaDeadlineAlerts:Enabled=true`. PROD never ran a cycle (reason unknown).
  With the scheduler fixed, the first deploy of change 1 or 3 **will start queueing proforma alerts** unless PROD's
  effective configuration disables them. Run `scripts/server/check-proforma-alerts-config-readonly.ps1` on the host
  before any deployment and decide explicitly.

### G.6 Test evidence

| Suite | Result |
|---|---|
| Full backend (`AlplaPortal.Application.Tests`, InMemory + LocalDB) | **2728 passed, 0 failed** (2680 before this campaign; +48 beyond change 1's 17) |
| Frontend `tsc -b` | exit 0; no frontend diff remains |
| LocalDB relational | concurrent approvals commit once; overlapping digest instances yield one digest and one outbox row per recipient; same-day restart dedup; duplicate INSERT refused by the unique index; backfill R1/R2/R3/R0 incl. lot #1 vs #10, idempotent |

The six previously adjusted test files are back to their committed versions (the request-level final approval path is
unchanged).

### G.7 Dry-run digest preview (DEV clone, PROD data of 2026-09-07, single-nominee rule)

See the table appended by the preview run below (SQL rendition of the unit and recipient rules; the service itself is
covered by G.6).

Run on 2026-10-07 after the new chain, "now" = 2026-09-08 08:00 Luanda (the morning after the snapshot):

| Measure | Value |
|---|---|
| Units considered (waiting batches + batch-less requests in an approval status) | 25 (10 area, 15 final) |
| Units without an established stage entry | 0 |
| Units eligible (> 3 Luanda calendar days in stage) | 8 |
| Eligible units with no notifiable recipient | 0 |
| Digests that would be produced | 3 |

| Recipient | Items | Stage | Oldest (days) | Subject |
|---|---|---|---|---|
| Biavanga Joao | 5 | area | 19 | Lembrete: 5 aprovações pendentes há mais de 3 dias |
| Carolina Modesto | 2 | area | 22 | Lembrete: 2 aprovações pendentes há mais de 3 dias |
| Nelson Abreu (company/request nominee) | 1 | final | 5 | Lembrete: 1 aprovação pendente há mais de 3 dias |

Identical to the pre-separation preview: on this data the single nominee and the deferred company set resolve to the
same person, so deferring the multi-approver model changes nothing for the first digests.

### G.8 Commits (as delivered) and TEST plan

| # | Commit | Migration | Deployable alone |
|---|---|---|---|
| 1 | `ee4b937` Batch stage notifications + "(Lote #N)" + tests | none | yes |
| 2 | `24eae46` Proforma scheduler anchor + tests | none | yes, **after** confirming PROD `ProformaDeadlineAlerts` config (G.5) |
| 3 | `4349338` `ApprovalBatch.RowVersion` + 409 filter + relational tests | `…152909` | migration first, then code |
| 4 | `c0706fd` Proforma via outbox, `ExpiresAtUtc`, `EXPIRED`, diagnostics, recipient rule + tests | `…152958` | migration first, then code; needs 2 |
| 5 | `d21a50d` Reminder digests (disabled) + preflight + admin endpoints + tests | `…153058` | migration first, then code; needs 4 |
| 6 | release commit: docs, CHANGELOG, VERSION, APP_VERSION v2.246.0 (HR files restored locally, not part of the release) | none | any time |

The chain order `152909 → 152958 → 153058` is fixed by the snapshots; migrations can be applied ahead of their code
(the previous build tolerates the new schema, G.4).

TEST order (SMTP redirect to the TEST mailbox on; record IDs):

1. Run `final-approval-recipients-vs-approvers-readonly.sql` and `approval-batch-stage-entry-preflight-readonly.sql`
   on TEST; review gaps and the batches that would stay NULL.
2. Run `check-proforma-alerts-config-readonly.ps1` on the TEST host; set the TEST value deliberately.
3. Apply the three migrations ("Apply TEST Migrations"); rerun preflight section D; deploy; `GET /api/app/version`.
4. Change 1: batch create / area approve / area reject / final approve / final reject → one outbox row set per event
   with "(Lote #N)", correlation = history Id; replay → 400 and no new row.
5. Change 3 (concurrency): two role-holders approve the same batch simultaneously → one 200, one 409 or 400; one
   `BATCH_FINAL_APPROVED`, one `PO_GROUP_ACTIVATED`, one e-mail set.
6. Change 4 (proforma): eligible PAYMENT request near NeedBy → alert row + outbox row to the **nominee** only;
   `GET api/admin/diagnostics/proforma-alerts` QUEUED → SENT; force a failure → DEAD_LETTER → next cycle re-queues
   (`QueuedCount` 2); restart → single cycle per day.
7. Change 5 (reminders) dry run: `Enabled=true` in TEST config only; after 08:00 Luanda one run row, digests with
   `DeliveryStatus = DRY_RUN`, no outbox rows; `POST api/admin/approval-reminders/preview` writes nothing; restart →
   all recipients dedup-skipped; role-holder who is not nominee receives nothing (expected, G.1).
8. Bounded live day: `DryRun=false` with `RecipientAllowList` = TEST mailbox user; one outbox row with
   `ExpiresAtUtc = +4 h` → SENT; stop SMTP for a cycle → FAILED → DEAD_LETTER on the same row; past `ExpiresAtUtc` →
   `EXPIRED`, nothing sent; next business day → new digest; weekend → none.
9. Rollback rehearsal on TEST: redeploy the previous build on the migrated schema; confirm the processor keeps
   sending fresh rows, ignores `EXPIRED`, and the proforma service skips existing alert rows (G.4).
10. Only then decide PROD: migrations, then commits 1–3, then 4, then 5 with reminders still `Enabled=false`.

### G.9 TEST validation evidence — v2.246.0 (2026-10-07 / 2026-10-08)

Build under test: `2.246.0+7a73e68` (API and frontend both reported it). Environment confirmed on IIS:
`ASPNETCORE_ENVIRONMENT = Test`, SMTP redirect enabled (all e-mails delivered to `leonardo.cintra@alpla.com`, sender
`donotreply@mail.alpla.com`). PROD was untouched throughout.

**Validated**

| Area | Evidence |
|---|---|
| Batch stage notifications | Area and final batch notifications were sent (redirected mailbox) |
| Concurrency guard | Concurrent calls against the same batch, **using the same administrator account**, produced one success and one conflict in each stage (area and final) |
| Single transition per decision | SQL confirmed one area decision, one final decision, one PO-group activation and one notification set per transition |
| Reminder digests, live | 4 digests for 22 approval units, all SENT, all redirected to `leonardo.cintra@alpla.com` |
| Reminder digests, next-day scheduled dry run | 4 digests recorded, zero queued e-mails |
| Reminder preview | Recipients changed between approval stages; resolved requests disappeared from the preview |
| Outbox mechanics (synthetic rows) | Failure followed by a successful retry on the **same** row; DEAD_LETTER after three failures; EXPIRED with zero send attempts |
| Proforma cycle | 12 expired-request alerts created and all SENT; a thirteenth request with two days remaining correctly received no alert |

**Proforma recovery test on REQ-08/10/2026-449** (`RequestId C68ADF17-5344-43AE-AE8C-F6D7B447F769`). The initial failure
fixture was **created through SQL**: an alert/outbox pair for João Catana with an invalid recipient address, which
reached DEAD_LETTER after three failures. The recovery itself was performed by the **real service**: at
2026-10-08 11:00 UTC the cycle reused alert `A6100802-0000-4000-8000-000000000001`, increased `QueuedCount` to 2,
created outbox row `39607171-E8A0-4393-A69B-21067D4BC628` and that row was sent successfully. Nelson Abreu received a
newly generated alert. Cycle log: 1 queued, 1 re-queued, 12 skipped by dedup, 0 without recipient, 14 eligible requests.

**Defects found by the validation, fixed in the working tree after 7a73e68 (not yet released)**

1. Duplicate greeting: `EmailService.SendWorkflowNotificationAsync` adds "Olá <first name>," and both the proforma body
   (`ProformaDeadlineAlertCycle.BuildMessages`) and the digest body (`ApprovalReminderDigestRenderer`) added their own.
   The template now owns the greeting; the two bodies no longer greet. Subjects, banners, original-recipient block,
   routing and links are unchanged.
2. Retry residue on SENT rows: a row that went SENT after earlier failures kept `LastError` and `NextRetryAtUtc`.
   `EmailOutboxProcessor.ProcessEntryAsync` now clears both on the real-send path and on the duplicate-suppression path
   (the suppression reason remains in the `EMAIL_OUTBOX_DEDUP` admin-log event). `RetryCount`, backoff, DEAD_LETTER
   and EXPIRED behaviour are unchanged. Regression tests: `EmailOutboxProcessorSentCleanupTests`, plus greeting
   assertions in the renderer and proforma cycle tests.

**Final TEST configuration after the validation** (restored; health endpoint Healthy): `ProformaDeadlineAlerts.Enabled=false`,
`ProformaDeadlineAlerts.CheckTimeUtcHour=7`, `ApprovalReminders.Enabled=true`, `ApprovalReminders.DryRun=true`,
`ApprovalReminders.MinPendingAgeDays=3`, `ApprovalReminders.SendTimeLocal="08:00"`; SMTP redirect still enabled to
`leonardo.cintra@alpla.com`; sender `donotreply@mail.alpla.com`.

**Not validated (outstanding)**: concurrency with two **distinct** user accounts (the test used one administrator
account for both calls); action replay refusal (400 on repeating an already-applied decision); rollback rehearsal
(previous build on the migrated schema); PROD readiness. These remain open before any PROD decision.

### G.10 P.O. registration → Finance e-mail gap (TEST, 2026-10-09) — diagnosis and fix (working tree, unreleased)

**Observed.** REQ-08/10/2026-449 (`C68ADF17-5344-43AE-AE8C-F6D7B447F769`): `REGISTER_PO` persisted at 2026-10-09 07:56:02 UTC
(NewStatus `PO_ISSUED`); seven "Nova P.O Registrada" in-app notifications share correlation
`13AC2781-343F-41C5-919E-DD213D6B9E0E`; **no** `PO_REGISTERED` outbox row exists for the request; seven active Finance
users with e-mail are scoped to its plant. No finance e-mail was received.

**Cause (code, pre-existing since c2b0a75 of 2026-07-14; untouched by v2.246.x).** `RequestsController.RegisterPo` emitted
`PO_REGISTERED` with only `EventCode`, `RequestId`, `ActionTaken`, `TargetStatusCode`, `ActorUserId` and a fresh GUID
correlation. The orchestrator resolves `PO_REGISTERED` recipients with `AddPlantScopedFinanceRecipientsAsync(evt.PlantId)`;
with a null plant it falls back to all Finance users **with e-mail suppressed** (in-app only, by design of the global
fan-out). Hence bell notifications for everyone and no outbox row, regardless of SMTP, redirect or environment. The
missing `RequestNumber` would also have produced an empty request reference in the subject.

**Fix.** `RegisterPo` now builds the event like `ProcessCommonOperationalTransition`: `RequestNumber`, `RequestTitle`,
`ActorName`, `RequesterId`, `BuyerId`, `AreaApproverId`, `FinalApproverId`, `DepartmentId`, `PlantId`, `CompanyId`,
and `CorrelationId = history.Id` (the persisted `REGISTER_PO`/`REREGISTER_PO` row). The event code is deliberately
unchanged: a correction re-registration still emits `PO_REGISTERED` (the history row carries `REREGISTER_PO`). The
previously silent `catch { }` logs a warning.
Routing rules, permissions, recipient data and the no-plant fallback are unchanged; no orchestrator change was needed
(the plant is always known on the request).

**Regression tests** (`RegisterPoFinanceNotificationTests`, real controller + real orchestrator, InMemory): plant-scoped
Finance users get one `PO_REGISTERED` outbox row each, correlated to the history row and carrying the request number
and actor name, plus their in-app notification; a Finance user scoped only to another plant gets neither; repeating the
registration on an issued PAYMENT request returns 400 "Ação Inválida" and queues nothing; a correction re-registration
keeps the `PO_REGISTERED` event code; a request without plant keeps the unchanged in-app-only fallback.

**Replay rules, as verified in code.** Not every re-registration is forbidden: a group in `WAITING_PO_CORRECTION`
(Finance returned the P.O.) accepts `register-po` again as `REREGISTER_PO`. A QUOTATION group is accepted only in
`WAITING_PO` or `WAITING_PO_CORRECTION`; a PAYMENT request only in `APPROVED`, `WAITING_PO_CORRECTION` or
`PO_PARTIALLY_UPLOADED`. Anything else is refused with 400 "Ação Inválida" before any write.

**How to verify the effective destination (correction to earlier guidance).** `EmailOutbox.RecipientEmail` stores the
**original** recipient; the TEST redirect is applied by `EmailService.ApplyEnvironmentPolicy` at dispatch time and is
never written back to the row. Verify the effective destination with the `EMAIL_ENV_POLICY` admin-log rows
(`[TEST] … → Para: <final address>`, payload `Original: <original>. Redirecionado: True`) and with the received message
(subject prefix `[TEST - IGNORE]`, original-recipient block in the body). Do not expect outbox recipient addresses to
equal the redirect mailbox.

**Bounded TEST procedure (after this fix is deployed; no historical notification is re-sent).**
1. Precondition: `SmtpSettings` row shows `RedirectAllToTestRecipient = 1`, `TestRecipientEmail = leonardo.cintra@alpla.com`;
   `GET /api/app/environment` returns `TEST`.
2. Pick one request whose plant has scoped Finance users (list them: active users with role Finance and a
   `UserPlantScopes` row for the request's plant) and register a P.O. through the UI once.
3. Expect in `RequestStatusHistories` one `REGISTER_PO` row; in `EmailOutbox` one `PO_REGISTERED` row per listed Finance
   user with `CorrelationId` = that history Id, `RequestNumber` filled, `Status` moving `PENDING → SENT`; in
   `AdminLogEntries` one `EMAIL_OUTBOX_QUEUED` per recipient and one `EMAIL_ENV_POLICY` per dispatched row whose
   message ends in `Para: leonardo.cintra@alpla.com`; in the mailbox one message per recipient, prefixed
   `[TEST - IGNORE]`, body listing the original recipient, subject "Nova P.O para Processamento — REQ-…".
4. Registration outside the permitted state (e.g. repeating register-po on a group/request already PO_ISSUED): 400
   "Ação Inválida", no new history, outbox or in-app row. A correction re-registration on a group in
   `WAITING_PO_CORRECTION` is NOT a replay: it is allowed and produces a distinct `REREGISTER_PO` row and notification
   (step 5).
5. If the request has a correction path available (group `WAITING_PO_CORRECTION`), re-register once: one
   `REREGISTER_PO` history row and `PO_REGISTERED` outbox rows to the same Finance users.
6. Confirm no `EMAIL_ENV_POLICY` row of the day shows a `Para:` address other than the redirect mailbox.

### G.11 Accounts Payable e-mail: per-company options for P.O. registration and Finance e-mail (working tree, unreleased)

Business direction (whiteboard: "Create PO — Keep Fin informed — AP / Treasury"; later AP communication around
receipt/service confirmation and invoices; Treasury involvement for advance payments). The whiteboard defines neither
addresses nor triggers; the two options below implement only the "Create PO — keep Finance informed" step and must not
be read as a complete approved specification of the AP/Treasury flow.

**Confirmed TEST facts (2026-10-09).** REQ-08/10/2026-449 belongs to AlplaPLASTICO (CompanyId 1), whose AP configuration
is active (To `alpla-plasticos-accounts@alpla.com`, CC `aovia-treasury@alpla.com`, NotifyOnScheduled/Completed true).
Registering its P.O. produced seven Finance in-app notifications, no `PO_REGISTERED` outbox row (G.10) and no AP group
e-mail, because the AP path only ever ran for `PAYMENT_SCHEDULED` / `PAYMENT_COMPLETED`. TEST AP logs hold 187
successful scheduling sends, 79 successful completion sends and 12 skipped scheduling duplicates; `Success` records SMTP
acceptance, not mailbox delivery.

**Two per-company options** (`AccountsPayableNotificationConfigs`, Master Data › Accounts Payable Email), both
**default false** in the entity, in the create DTO and in the migration; existing rows are not enabled:

| Option | Effect when ON (company configuration must also be active) |
|---|---|
| `NotifyOnPoRegistered` — "Notificar Contas a Pagar quando uma P.O. é registada" | `PO_REGISTERED` sends the AP **review notice** to To + CC through the existing AP mechanism |
| `NotifyFinanceUsersByEmail` — "Também enviar e-mail individual aos utilizadores Finance" | plant-scoped Finance users get the individual e-mail for `PO_REGISTERED`, `PO_CORRECTION_COMPLETED`, `ADVANCE_PAYMENT_REQUIRED` (outbox rows). OFF, missing or inactive configuration → in-app notification only |

Unchanged: `NotifyOnScheduled`/`NotifyOnCompleted` behaviour and wording, requester routing on payment events, approval
e-mails, buyer e-mails, every other event. Finance in-app notifications are never affected by either switch. The
e-mail-permission decision is **per request company**; a second company's configuration is never consulted.

**Wording.** The P.O. notice is a review notice: subject "[Portal Gerencial] P.O. registada — revisão de Contas a Pagar —
Pedido …" (or "P.O. corrigida e re-registada …" when the history row is `REREGISTER_PO`), headline "… — Revisão
Necessária — <empresa>", body stating that the registration **does not mean the payment is authorized or ready** and
that post-paid receipt/invoice requirements still apply. Scheduling/completion keep "Novo pedido de pagamento …".

**Correction re-registration.** `RegisterPo` keeps emitting `PO_REGISTERED` for corrections (G.10, as agreed); the AP
option therefore applies to corrections too, with the "corrigida e re-registada" wording, and each correction is its
own action for dedup purposes (below).

**Dedup granularity, verified and changed minimally.** Before: `AccountsPayableNotificationLogs` unique on
(RequestId, EventCode, RecipientEmail) with `Success = 1 AND Skipped = 0`, and the application check mirrored it —
correct for scheduling/completion (one per request), but request-only dedup would silently suppress the second P.O.
group of a request and every correction. Change: nullable `CorrelationId` on the log; `PO_REGISTERED` dedups on
(Request, Event, Recipient, **CorrelationId = history row**), payment events keep `CorrelationId = NULL` and their
request-level rule; the unique filtered index now includes `CorrelationId` (NULLs compare equal in a SQL Server unique
index, so payment rows keep one success per request/event/recipient). A repeated emission of the **same** action is
still skipped; the controller guards (G.10) refuse registrations outside the permitted state before any emission,
while a correction re-registration in `WAITING_PO_CORRECTION` stays allowed and is a distinct action.

**Delivery mechanism, unchanged and documented.** The AP e-mail is a **direct** `SendWorkflowNotificationAsync` call
inside the request: no `EmailOutbox` row, no automatic retry; a failure is written to the log (`Success = 0`,
`ErrorMessage`) and never retried or blocks the workflow. Individual Finance e-mails go through the outbox (retries,
EXPIRED, dedup by correlation). Moving AP e-mails to the outbox is out of scope here. No historical notification is
replayed.

**Migration `20261009084838_AddAccountsPayablePoRegisteredAndFinanceEmailOptions`** (not applied anywhere):
`AccountsPayableNotificationConfigs.NotifyOnPoRegistered` and `.NotifyFinanceUsersByEmail` (bit NOT NULL DEFAULT 0);
`AccountsPayableNotificationLogs.CorrelationId` (uniqueidentifier NULL); drop + recreate `IX_ApNotifLogs_Dedup` with the
extra key column and the same filter. Previous build on the new schema: inserts work (defaults, nullable column), the
old application-level dedup still holds for payment events. Rollback (`Down`): drops the three columns and recreates the
original index. **Caveat:** `Down` fails if more than one successful `PO_REGISTERED` AP row exists for the same
(request, recipient) — i.e. if the option was used on a request with several groups/corrections; delete or mark those
rows `Skipped` before rolling back. Rolling back the code without the schema is safe.


**Dedup verified on SQL Server (2026-10-09, disposable `Portal-Gerencial-ApDedupCheck`, restored from the clone backup
and migrated with the real `Up`; dropped afterwards; TEST/PROD untouched).** After the migration: 103 history rows,
both new columns present, the two pre-existing configuration rows still opted out (`0/2`), index
`IX_ApNotifLogs_Dedup` unique on (RequestId, EventCode, RecipientEmail, CorrelationId) with filter
`[Success]=(1) AND [Skipped]=(0)`. `AccountsPayableDedupRelationalTests` (real orchestrator, mocked e-mail) against that
database: payment scheduling emitted twice with different history correlations → one success + one skipped row, both
with `CorrelationId NULL`, and a forced second success row is refused by the index; `PO_REGISTERED` for group 1,
group 2 and a correction → three success rows with three distinct correlations, re-emitting group 1 → skipped, a forced
duplicate success for group 1 refused by the index, a further distinct action accepted. The same class runs on the
default LocalDB sandbox in the regular suite.

**Concurrency limitation of the existing direct-send AP path (pre-existing, not changed here).** The dedup is
check-then-act: the `AnyAsync` lookup and the log insert are separate statements around a synchronous SMTP call. Two
concurrent emissions of the same action (or, for payment events, of the same request) can both pass the lookup and both
send; the second insert then fails on the unique index inside the method's `try`, and the catch records a
`Success = 0` failure row although that e-mail was accepted by SMTP. In practice the controllers only emit once per
committed transition, so this needs two simultaneous identical transitions; the fix would be an insert-first (reserve)
pattern or moving AP mail to the outbox, both out of scope for this change.

**Defect found by the SQL Server check and fixed.** On the default LocalDB sandbox (no Finance users seeded) the P.O.
review notice was never sent: `EmitAsync` returned as soon as the per-user recipient list was empty, before the Accounts
Payable block. On the migrated clone copy the fallback Finance recipients hid this. Fix: the "no recipients" case now
skips only the per-user in-app/outbox dispatch; the AP block still runs, so a company whose plant has no Finance user
still reaches its configured AP address. Covered by
`AccountsPayableNotificationRoutingTests.Ap_notice_is_sent_even_when_no_finance_user_exists_for_the_plant`. Side effect
reviewed: for payment events the requester is always a recipient, so nothing changes there; the buyer "awaiting P.O."
instruction after `FINAL_APPROVED` is now also attempted when no other recipient resolved, which is the intended
behaviour of that independent block.

**Test-run note.** The relational classes recreate the shared LocalDB sandbox once when its schema predates the model
(here: the new `CorrelationId` column). That recreation ran while five SQL Server tests outside the `IntegrationTests`
collection were executing in parallel and failed them once; they pass on rerun and in the subsequent full run. This is
the pre-existing bootstrap pattern of the other relational classes, not a product defect.
**Tests** (mocked e-mail service, nothing sent): `AccountsPayableConfigControllerTests` (defaults, persistence,
independent edits, GET, toggle), `AccountsPayableNotificationRoutingTests` (real orchestrator: all four switch
combinations; missing/inactive configuration; event without company/plant; To/CC and company selection; second P.O.
group + correction + repeated action; payment events unchanged incl. request-level dedup and wording; AP failure logged
not retried), `RegisterPoFinanceNotificationTests` (real controller + orchestrator incl. AP notice and switch-off
cases), frontend `apNotificationsForm.test.ts` (defaults, display, independent edits, payload, legacy payload → OFF).
No DOM testing library exists in the frontend (vitest `node` environment), so the UI is covered through the extracted
pure form helpers, not by rendering the panel.

**Whiteboard vs Portal events (source-based; investigation only)**

| Whiteboard step | Portal trigger | Recipients | Mechanism | Gap |
|---|---|---|---|---|
| Create PO — keep Finance informed (AP / Treasury) | `PO_REGISTERED` on register-po | Finance (plant-scoped): in-app always; e-mail only with `NotifyFinanceUsersByEmail`. AP To+CC only with `NotifyOnPoRegistered` | outbox (Finance) / direct (AP) | both options default off → still in-app only until enabled |
| Advance payment required / Treasury | `ADVANCE_PAYMENT_REQUIRED`, `ADVANCE_PAYMENT_SCHEDULED/COMPLETED` exist in the orchestrator but **nothing emits them**; the b2p endpoints (`schedule-advance`, `confirm-advance`, `reconcile`, `confirm-delivery`) emit no event | — | — | no notification at all for advance payments |
| Goods / service receipt confirmation | `confirm-receiving` emits `REQUEST_FINALIZED` (reused code) → requester only; `OPERATIONAL_RECEIPT_COMPLETED` / `GROUP_COMPLETED` are history action codes, never emitted | requester | outbox | AP/Finance not informed of receipt |
| Invoice submission / validation | `FISCAL_RECEIPT_UPLOADED` is a history action code; `FINAL_INVOICE_*` codes have no emitter and no orchestrator case | — | — | no notification |
| Payment scheduling | `PAYMENT_SCHEDULED` (FinanceController `schedule`) | requester (outbox) + AP To/CC (direct, `NotifyOnScheduled`) | outbox / direct | none |
| Payment completion | `PAYMENT_COMPLETED` (FinanceController `pay`) | requester (outbox) + AP To/CC (direct, `NotifyOnCompleted`) | outbox / direct | none |

**Bounded TEST validation (after deployment and migration; nothing historical is re-sent).**
1. Preconditions: `GET /api/app/environment` = `TEST`; `SmtpSettings` redirect to `leonardo.cintra@alpla.com` active;
   sender `donotreply@mail.alpla.com`.
2. Master Data › Accounts Payable Email: existing AlplaPLASTICO row shows both new switches OFF; enable
   `NotifyOnPoRegistered` only; save; reload shows it ON and To/CC unchanged.
3. Register a P.O. on a company-1 request (plant with scoped Finance users): one `REGISTER_PO` history row; one
   `AccountsPayableNotificationLogs` row (`EventCode PO_REGISTERED`, `Success 1`, `CorrelationId` = history Id,
   `RecipientEmail alpla-plasticos-accounts@alpla.com`, `CcEmails aovia-treasury@alpla.com`); one `EMAIL_ENV_POLICY`
   row ending "Para: leonardo.cintra@alpla.com" with the AP address in the payload; the received message has the review
   wording and the `[TEST - IGNORE]` prefix; Finance users have in-app rows and **no** outbox rows.
4. Enable `NotifyFinanceUsersByEmail`; register a P.O. on another group/request: outbox `PO_REGISTERED` rows per
   plant-scoped Finance user (`RecipientEmail` = original addresses) moving to SENT, each with its own
   `EMAIL_ENV_POLICY` row to the redirect mailbox; still one AP log row for this action.
5. Register a second group on the same request → a second AP log row with a different `CorrelationId`; re-register a
   corrected P.O. (group in `WAITING_PO_CORRECTION`) → "P.O. corrigida e re-registada" notice with its own AP log row;
   repeating register-po on the now-issued group (outside the permitted state) → 400 and no new rows.
6. Schedule and complete a payment → scheduling/completion e-mails and logs exactly as before (one each,
   `CorrelationId NULL`).
7. Disable both switches again (or leave as decided); confirm no `EMAIL_ENV_POLICY` row of the day shows a
   `Para:` address other than the redirect mailbox.

### G.12 v2.247.0 on TEST: delivery confirmed, AP notice content defect (working tree, unreleased)

**Delivery evidence (TEST, 2026-10-09, build 2.247.0+c7c4f73).** REQ-22/09/2026-441 (`RequestId
FB421E61-5CC6-4D60-BAA6-50816D041346`), registered P.O. group `3355E6AE-9913-4CCE-BED4-5CB91DA7E3A7`, supplier
MULTI BIZ - COMÉRCIO E SERVIÇOS, LDA, group total 285,000.00 AOA; `REGISTER_PO` history / correlation
`96D37D7F-D9CB-4B4D-BACE-A2C2E7499C92`. With both company options enabled: one Accounts Payable e-mail and seven individual
Finance e-mails, all redirected to the TEST mailbox. Routing, switches, dedup and redirect behaved as designed.

**Content defect.** The AP notice displayed Supplier "—" and "0.00 AOA" (also REQ-08/10/2026-448): the AP method read
`Request.Supplier` and `Request.EstimatedTotalAmount` from the request header, which are empty for group-based requests
and in any case belong to the request, not to the registered group.

**Fix (narrow).** `WorkflowEvent.PoGroupId` carries the registered group; `RegisterPo` sets it; for `PO_REGISTERED` the
AP method reads supplier (name, else snapshot), `TotalAmount` and `CurrencyCode` from **that** group, labels the amount
"Total do grupo P.O." and HTML-encodes supplier and currency. Initial registration and permitted correction
re-registration use the same source; another group's data and the aggregate request total are never used. Without a
`PoGroupId` (not produced by current code) the method logs a warning and falls back to the header values as before.
Recipients, switches, permissions, SMTP, delivery mechanism, history correlation, dedup and the scheduling/completion
content are unchanged.

**Tests.** `RegisterPoFinanceNotificationTests.Ap_notice_uses_the_registered_group_supplier_total_and_currency_for_registration_and_correction`
(real controller + orchestrator; PAYMENT request with header supplier IP WORLD and estimate 400,758.34; group A MULTI
BIZ 285,000.00 AOA, group B USD VENDOR 1,500.00 USD; registering B shows only B's data; correction of A shows only A's
data with "&amp;" encoding; two success log rows keyed by the two history rows, none skipped) and
`AccountsPayableNotificationRoutingTests.Ap_notice_content_comes_from_the_event_group_not_the_request_header_and_falls_back_without_a_group`.

**Bounded TEST validation (after deployment; redirect to leonardo.cintra@alpla.com active, environment `TEST`).**
1. On a request with at least two P.O. groups of different suppliers (or a QUOTATION request), register one group:
   the AP notice shows that group's supplier, "Total do grupo P.O." with that group's amount and currency; the
   `AccountsPayableNotificationLogs` row carries the `REGISTER_PO` history Id as `CorrelationId`.
2. Return the P.O. from Finance and re-register the corrected group: notice "P.O. corrigida e re-registada" with that
   group's data; a second log row with the `REREGISTER_PO` correlation.
3. Confirm a scheduling notice still reads "Novo pedido de pagamento" with its previous content, and that
   `EMAIL_ENV_POLICY` rows show only the redirect mailbox as destination.

### G.13 Payment scheduling/completion e-mails: AP content, distinct wording, departmental aggregate (working tree, unreleased)

**TEST evidence (2026-10-09, build 2.247.1+464e4db).** REQ-07/10/2026-538 (`RequestId
11D0F2EE-27B7-4D82-BFF1-0126F97570F5`, company 1 AlplaPLASTICO, plant 2), P.O. group `7582FD27-2A39-4398-BF8B-CB4E7E8DFD83`,
supplier PTA-ÁGUAS, LDA, total 126,787.50 AOA. Scheduling and completion each produced one Accounts Payable log
(`Success=1, Skipped=0, ErrorMessage=NULL`) and two individual outbox messages (Milton Figueiredo, Adelaide Kambambe;
`SENT, RetryCount=1, LastError=NULL`); all six messages reached the TEST mailbox through the redirect. Delivery is
correct. Observed content defects: (1) both AP notices show Supplier "—"; (2) the completion notice has the scheduling
subject "Novo pedido de pagamento para AlplaPLASTICO" and says the request "entrou na lista de Contas a Pagar";
(3) both departmental notices show "Acumulado (Agendado/Pago) 0.00 AOA" and "0.0%". The development database does not
hold REQ-538; every scenario below is reproduced with isolated test fixtures.

**Confirmed causes (source).**

1. *Supplier "—".* `FinanceController.SchedulePayment` (`POST finance/{id}/schedule`) and `MarkAsPaid`
   (`POST finance/{id}/pay`) both resolve ONE group (`SchedulePaymentDto.RequestPoGroupId` / `ConfirmPaymentDto.RequestPoGroupId`)
   and write ONE `RequestPayment` row (planned = `group.TotalAmount` when scheduling; `ActualPaidAmount`/`PaidDateUtc` when
   completing), then emitted request-level events (no `PoGroupId`). The AP method read `Request.Supplier`,
   `Request.ActualPaidAmount ?? Request.EstimatedTotalAmount` and the request currency. QUOTATION requests have no header
   supplier; the amount happened to be right only because `MarkAsPaid` also copies the last paid amount onto the request
   header, and the currency was always the request's even when the group is in another currency.
2. *Same subject/body.* One template (`BuildApNotificationBody`) served both events; only the "Status atual" row varied.
3. *Departmental 0.00 / 0.0%.* The aggregate (introduced in commit 4c1ce07, 2026-04-13) filtered
   `Status.Code IN ('SCHEDULED','PAID','PARTIAL_PAID')`. None of these codes exists in the request-status catalog
   (`ApplicationDbContext` seed: `PAYMENT_SCHEDULED`, `PAYMENT_COMPLETED`, `ADVANCE_PAYMENT_*`; the `PAID` constant exists but
   is not seeded), so the sum was always zero and the percentage was "0 / 0 → 0.0%". Two further defects in the same block:
   amounts of every currency were summed and labelled with the request's currency, and a zero denominator was rendered as
   "0.0%" instead of "not available".

**The existing departmental rule, as written.** Scope = `Request.DepartmentId` only (departments are global, see
`Department`; the recipients, however, are the department+plant managers). Period = `Request.UpdatedAtUtc >= first day of the
current UTC month` (any update, not the scheduled/paid date). Statuses = the request-level (scalar) status, not group
statuses. Amount = `Request.EstimatedTotalAmount` (for QUOTATION this is the selected quotation total once selected,
otherwise 0). Currency = the request's currency as a label only. Inclusion of the current request = only if its scalar
status matched (after `SaveChanges` + `StatusAggregationService`, so a PAYMENT request is included; a multi-group QUOTATION
whose parent stays at the furthest-behind sibling status is not). Timing = computed after persistence and aggregation.

**Corrections implemented (narrow).**

- `WorkflowEvent.PaymentId` (optional `int?`, the `RequestPayment` row) added next to `PoGroupId`; `SchedulePayment` and
  `MarkAsPaid` set `PoGroupId = group.Id`, `PaymentId = payment.Id` after their `SaveChanges`.
- AP notice for `PAYMENT_SCHEDULED` / `PAYMENT_COMPLETED` (`ResolveApPaymentUnitAsync` + `BuildApPaymentBody`): supplier =
  `group.Supplier.Name` → `group.SupplierNameSnapshot` → selected quotation supplier (QUOTATION) → request supplier → "—"
  (same fall-through as `FinanceGroupDisplayResolver`, which Finance already uses for legacy PAYMENT groups with null
  snapshots); amount = payment row (`PlannedAmount` when scheduling, `ActualPaidAmount` when completing) → group total;
  currency = payment row (unless "---") → group → selected quotation → request; dates = `ScheduledDateUtc` /
  `PaidDateUtc`. Everything is scoped to the event's request and group: never another group, never the request aggregate,
  never a mix of currencies. Legacy request-level emitters (`RequestsController` `operational/schedule-payment` and
  `operational/complete-payment`, no group) fall back to the header values, log a warning and label the amount
  "(pedido)" instead of "(grupo P.O.)".
- Distinct wording: subject "[Portal Gerencial] Pagamento agendado — Pedido X" / headline "Pagamento Agendado — Company",
  body "Um pagamento foi agendado pelas Finanças e entrou na lista de Contas a Pagar", rows "Montante agendado (grupo
  P.O.)" and "Data agendada"; subject "[Portal Gerencial] Pagamento realizado — Pedido X" / headline "Pagamento Realizado —
  Company", body "Um pagamento foi confirmado como realizado pelas Finanças", rows "Montante pago (grupo P.O.)" and "Data
  do pagamento". Supplier, currency, title, actor and company are HTML-encoded. The P.O. registration/correction notices
  (G.11/G.12) are untouched.
- Departmental aggregate (`ComputeDepartmentalMonthContextAsync`, used by the payment notice only): statuses
  `PAYMENT_SCHEDULED`, `PAID`, `PAYMENT_COMPLETED` (the set Finance itself names `financeStatusCodes`); same `CurrencyId` as
  this request; same department; `UpdatedAtUtc >= month start` (unchanged); OTHER requests summed, this request added
  explicitly (so the share is defined and ≤ 100 % regardless of the parent's aggregated status); label "Acumulado
  (Agendado/Pago, incl. este pedido)"; "Impacto" shows the percentage only when total > 0 and this amount > 0, otherwise
  "n/d — sem base de comparação (…)".
- Not changed: recipients, routing, AP configuration and switches, SMTP, TEST redirect, outbox/direct-send mechanisms,
  dedup rules (AP scheduling/completion stay per request+event+recipient; `CorrelationId NULL`), permissions.

**Decisions NOT taken (concrete ambiguities, each needs a business answer before the rule is changed).**

1. Plant scope: the aggregate is department-wide across plants while the recipients are department+plant managers.
2. Period basis: "updated this month" vs "scheduled/paid this month" (`RequestPayment.ScheduledDateUtc` /
   `Request.ActualPaidAtUtc`). A request scheduled in September and merely commented in October counts; one scheduled in
   September and untouched does not.
3. Advance statuses (`ADVANCE_PAYMENT_SCHEDULED/COMPLETED`) are excluded, as before. Note the schedule endpoint emits
   `PAYMENT_SCHEDULED` even when it schedules an advance (group at `ADVANCE_PAYMENT_REQUIRED`); the AP notice then reads
   "Pagamento agendado" with the advance row's planned amount. Not changed (advance scope excluded from this task).
4. Multi-group requests: the departmental "Valor deste Pedido" is the request estimate, not the scheduled group's amount.
5. Foreign-currency activity is excluded from the sum rather than converted (no FX source in the portal).
6. The area-approval notice (`HandlePendingAreaApprovalFanningAsync`) contains the identical dead filter and the same
   mixed-currency sum ("Consumo Departamental Atual"). Left untouched: outside this task's scope; same fix applies.
7. AP dedup for scheduling/completion is per request: scheduling a second group of the same request produces no second AP
   notice (a skipped log row is written). Unchanged by instruction; now covered by a test that documents it.

**Tests (backend 2769/2769).** `AccountsPayableNotificationRoutingTests`: distinct wording + dedup/recipients/logs
preserved; acted-on group and payment row rendered (A: MULTI BIZ 285,000.00 AOA scheduled 20/10/2026; B: USD VENDOR &amp;
CO paid 1,480.00 USD of 1,500.00 planned on 09/10/2026), never the header (IP WORLD / 400,758.34), never the other group;
no-group fallback labelled "(pedido)"; legacy group without supplier/currency falls back to the request supplier/currency.
`FinancePaymentApNotificationTests` (real `FinanceController` + real orchestrator, QUOTATION with no header supplier, zero
estimate, two groups AOA/USD): `SchedulePayment` → AP scheduling notice with PTA-ÁGUAS, 126,787.50 AOA, 20/10/2026, one
success log, requester outbox row; second group skipped by dedup (documented); `MarkAsPaid` → AP completion notice with
USD VENDOR &amp; CO, 1,500.00 USD, 09/10/2026. `DepartmentalPaymentContextTests`: inclusion/exclusion by status, currency,
department and month boundary (100,000 + 60,000 + 40,000 → 200,000.00 AOA, 50.0 %); inclusion of this request when its
parent is still PO_ISSUED (25.0 %); only request → 100.0 %; zero basis → "n/d" (no "0.0%"); requester and manager rows.

**Bounded TEST validation (after deployment; redirect active).**
1. Schedule a payment on a QUOTATION request with ≥ 2 groups of different suppliers: AP subject "Pagamento agendado —
   Pedido …", Fornecedor = that group's supplier, "Montante agendado (grupo P.O.)" = that group's total and currency,
   "Data agendada" = the chosen date; one `AccountsPayableNotificationLogs` row (`Success=1`, `CorrelationId NULL`).
2. Mark it paid with an amount different from the planned total (over-payment allowed): AP subject "Pagamento realizado
   — Pedido …", "Montante pago (grupo P.O.)" = the amount entered, "Data do pagamento" = the paid date; no "entrou na
   lista" sentence.
3. In the departmental e-mails of both steps, "Acumulado (Agendado/Pago, incl. este pedido)" must equal the value of
   query Q3 below plus the request's estimate, and "Impacto" must be that ratio (or "n/d" when the estimate is 0).
4. Requester mail, Finance redirect (`EMAIL_ENV_POLICY`), outbox rows and the P.O. registration notice unchanged.

**Read-only TEST queries (optional, to predict step 3 before deployment).**
```sql
-- Q1: which of the codes exist in the catalog (expected: only PAYMENT_SCHEDULED and PAYMENT_COMPLETED)
SELECT Code FROM RequestStatuses WHERE Code IN ('SCHEDULED','PAID','PARTIAL_PAID','PAYMENT_SCHEDULED','PAYMENT_COMPLETED');
-- Q2: what the OLD rule summed for REQ-538's department this month (expected: 0 rows)
SELECT COUNT(*) AS Rows_, SUM(r.EstimatedTotalAmount) AS Sum_
FROM Requests r JOIN RequestStatuses s ON s.Id = r.StatusId
WHERE r.DepartmentId = (SELECT DepartmentId FROM Requests WHERE Id = '11D0F2EE-27B7-4D82-BFF1-0126F97570F5')
  AND s.Code IN ('SCHEDULED','PAID','PARTIAL_PAID') AND r.UpdatedAtUtc >= DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1);
-- Q3: what the NEW rule sums (other requests, same department, same currency, scheduled/paid, updated this month)
SELECT COUNT(*) AS Rows_, SUM(r.EstimatedTotalAmount) AS OthersSum, c.Code AS Currency
FROM Requests r JOIN RequestStatuses s ON s.Id = r.StatusId LEFT JOIN Currencies c ON c.Id = r.CurrencyId
WHERE r.Id <> '11D0F2EE-27B7-4D82-BFF1-0126F97570F5'
  AND r.DepartmentId = (SELECT DepartmentId FROM Requests WHERE Id = '11D0F2EE-27B7-4D82-BFF1-0126F97570F5')
  AND ((r.CurrencyId IS NULL AND (SELECT CurrencyId FROM Requests WHERE Id = '11D0F2EE-27B7-4D82-BFF1-0126F97570F5') IS NULL)
       OR r.CurrencyId = (SELECT CurrencyId FROM Requests WHERE Id = '11D0F2EE-27B7-4D82-BFF1-0126F97570F5'))
  AND s.Code IN ('PAYMENT_SCHEDULED','PAID','PAYMENT_COMPLETED')
  AND r.UpdatedAtUtc >= DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)
GROUP BY c.Code;
-- Q4: currencies present among scheduled/paid requests of that department this month (shows how much decision 5 matters)
SELECT ISNULL(c.Code,'(null)') AS Currency, COUNT(*) AS Rows_, SUM(r.EstimatedTotalAmount) AS Sum_
FROM Requests r JOIN RequestStatuses s ON s.Id = r.StatusId LEFT JOIN Currencies c ON c.Id = r.CurrencyId
WHERE r.DepartmentId = (SELECT DepartmentId FROM Requests WHERE Id = '11D0F2EE-27B7-4D82-BFF1-0126F97570F5')
  AND s.Code IN ('PAYMENT_SCHEDULED','PAID','PAYMENT_COMPLETED')
  AND r.UpdatedAtUtc >= DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)
GROUP BY c.Code;
```
Q1 establishes that the old filter could never match; Q2 confirms the observed 0.00; Q3 is the value the corrected
notice will add to the request's estimate; Q4 shows whether same-currency scoping excludes material amounts.
Supersedes G.12 step 3 ("Novo pedido de pagamento" is no longer the scheduling subject).

#### G.13.1 Departmental notice — review outcome and final behaviour (revision, working tree)

**Review finding.** The departmental block is a request-level, ESTIMATE-based metric. Numerator = `Request.EstimatedTotalAmount`
(a single header figure, never derived from P.O. groups or payment rows; for QUOTATION it is the selected quotation or
line-item total). Denominator = that estimate plus the estimates of other requests of the department (status
`PAYMENT_SCHEDULED`/`PAID`/`PAYMENT_COMPLETED`, same `CurrencyId`, updated this month). Neither side is a scheduled or paid
amount; "Agendado/Pago" names the status filter only. With several groups the same figures are rendered for every
action on the request; partial/divergent payments never affect it; a request whose groups are in different currencies
already carries a mixed estimate before this block sees it, and `Request.CurrencyId` alone does not prove the group
amounts share that currency. Explicit inclusion of the current request counts its estimate once (others exclude its Id)
and never duplicates an amount.

**Final rendering (payment notices to area managers).**

| Row | Source | Rule |
|---|---|---|
| Valor desta ação (agendado / pago, grupo P.O.) | acted-on `RequestPayment` row via `WorkflowEvent.PoGroupId` + `PaymentId` (same resolver as the AP notice) | planned amount when scheduling, actual paid amount when completing, in the payment row's currency (else group, else request). "n/d (sem registo de pagamento associado a esta ação)" for legacy events without a row. Informative only, never part of the ratio. |
| Valor estimado deste Pedido | `Request.EstimatedTotalAmount` + request currency | always shown, labelled as an estimate; "(moeda do pedido não registada)" when `CurrencyId` is null. |
| Acumulado estimado (pedidos agendados/pagos atualizados no mês, incl. este) | estimates of other comparable requests + this estimate | shown only when THIS request is comparable; otherwise "n/d — reason". |
| Impacto | this estimate / accumulated estimate | percentage only when comparable and both figures > 0; otherwise "n/d — reason". |
| Período | `UpdatedAtUtc >= first day of current UTC month` | stated literally: "pedidos … com última atualização desde dd/MM/yyyy (UTC); não comprova que o agendamento/pagamento ocorreu neste mês. Valores estimados dos pedidos, não montantes agendados/pagos." |

**Comparability rule (no new metric, no FX).** A request's estimate is accepted as a single-currency figure only when
its currency is registered (`Request.CurrencyId` → `Currencies.Code`) and every non-cancelled `RequestPoGroup` of the
request carries that same currency (`CurrencyCode`, else `CurrencyId` → code; vacuously true for a request without
groups). A group with no registered currency makes the request non-comparable (legacy PAYMENT groups with null
snapshots fall here). The same test is applied to the other requests of the denominator: non-comparable requests are
left out of the sum rather than mixed in. Reasons rendered: "moeda do pedido não registada…", "pelo menos um grupo P.O.
deste pedido não tem moeda registada…", "os grupos P.O. deste pedido estão em moedas diferentes da moeda do pedido
(AOA, USD vs AOA)…".

**Worked example.** Request R (quotation, AOA, estimate 286,500.00) with groups A (285,000.00 AOA) and B (1,500.00 USD);
other request X in the department this month, 100,000.00 AOA, PAYMENT_SCHEDULED. Scheduling B renders: Valor desta ação
1,500.00 USD; Valor estimado 286,500.00 AOA; Acumulado n/d — grupos em moedas diferentes (AOA, USD vs AOA); Impacto n/d.
Had both groups been AOA: Acumulado 386,500.00 AOA, Impacto 74.1 %, and identical figures for the scheduling of A and B
(only the action row differs) — the ratio remains a request-level share, not a per-action one.

**Unchanged, explicit limitation — AP dedup.** Accounts Payable scheduling/completion dedup stays per
(request, event, recipient) with `CorrelationId NULL`. On a request with several groups, the scheduling or completion of
any group after the first produces NO Accounts Payable notice (a `Skipped=1` log row is written); the requester and
departmental mails are still sent for each action. Its redesign (per-action dedup like `PO_REGISTERED`) is tracked
separately and is NOT part of this change. The multi-group notification flow is therefore not validated end to end;
only the single-group path matches the TEST evidence of REQ-538.

**Tests (final).** `DepartmentalPaymentContextTests`: same-department/same-currency/comparable aggregation with the
month boundary and exclusion of non-comparable others (100,000 + 60,000 + 40,000 → 200,000.00 AOA, 50.0 %); action
amount planned 126,787.50 vs paid 130,000.00 against an estimate of 120,000.00 (aggregate stays estimate-based);
mixed-currency groups → action 1,500.00 USD shown, aggregate and percentage "n/d" with the reason, no 386,500.00 and no
percentage; group without currency and request without currency → "n/d" with their reasons; inclusion when the parent
is still PO_ISSUED (25.0 %); legacy event without group/payment → action "n/d", estimate metric kept; zero basis →
"n/d" (no "0.0%"); requester and manager rows. AP/Finance/RegisterPo classes unchanged and green.

**TEST validation step 3 (replaces the earlier wording).** In the departmental e-mails: "Valor desta ação" equals the
scheduled (planned) and then the paid amount entered, in the group currency; "Valor estimado deste Pedido" equals the
request estimate; "Acumulado estimado" equals Q3 (restricted to comparable requests) plus the estimate, and "Impacto"
their ratio, or "n/d" with the stated reason when the request has a USD group, a group without currency, or no
currency; the "Período" line names the first day of the month.

**Final review addendum (disclosure of exclusions).** The aggregate row is labelled "Acumulado estimado (pedidos
agendados/pagos atualizados no mês, apenas comparáveis na moeda do pedido, incl. este)". Scheduled/paid requests of the
department updated in the period that are NOT summed (other `CurrencyId`, or a non-cancelled group in another or an
unknown currency) are counted, and when that count is above zero the notice adds "Excluídos: N pedido(s) …, por moeda
diferente ou não comprovada — o acumulado e a percentagem não representam toda a atividade do departamento." Requests
outside the status set or the period are neither summed nor counted. Cancelled P.O. groups are ignored both when
testing this request's comparability and when testing the other requests'; the acted-on group for "Valor desta ação" is
looked up by Id and is never cancelled at the time of the action. Legacy requests without groups keep the documented
behaviour: comparable when the request currency is registered, action amount "n/d" without a payment row. Tests:
exclusion count of 3 in the main aggregation scenario; no disclosure when a cancelled USD group is the only
foreign-currency element (other request included, 62.5 %); cancelled USD group on this request does not break
comparability. Backend 2774/2774.
