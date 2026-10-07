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
