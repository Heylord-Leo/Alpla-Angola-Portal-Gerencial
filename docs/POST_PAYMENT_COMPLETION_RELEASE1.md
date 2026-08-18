# Post-Payment Completion Workflow — Release 1 (Domain Foundation)

> Operational companion to plan v6 (`Post-Payment Completion Workflow — Implementation Plan v6`).
> Covers what Release 1 deployed, how to verify it, and how to roll it back.
> **Release 1 changes no user-facing behaviour.** The feature flag is `false` in every environment.

---

## 1. What Release 1 is

Schema and code foundation only:

- new columns on `RequestPoGroups`, `Requests`, `Quotations`, `RequestStatusHistories`;
- `rowversion` concurrency tokens on `Requests` and `RequestPoGroups`;
- the `FinalInvoiceReconciliations` table (created empty, first written in Release 3);
- the `WAITING_FISCAL_RECEIPT` lookup row (seeded, assigned to nothing until Release 4);
- a filtered unique index on `RequestStatusHistories.IdempotencyKey`;
- pure domain helpers (feature policy, idempotency-key builders, dimension derivation);
- a feature-gated **no-op** two-phase completion service;
- one guard inside `FinalizeRequest`, entirely within an `if (feature enabled)` branch.

**Not in Release 1**: any frontend change, any new endpoint, any DTO, OCR/reconciliation logic,
notification handler, automatic completion, data backfill, `RECEIPT` rename, or post-completion
replacement.

---

## 2. Feature flag

| Setting | Value | Where |
|---|---|---|
| `PostPaymentCompletion.Enabled` | `false` | `appsettings.json` (base — inherited by every environment) |
| `PostPaymentCompletion.EffectiveDateUtc` | `9999-12-31T23:59:59Z` | same |

Three independent layers keep the workflow off:

1. `PostPaymentCompletionOptions` defaults to `Enabled = false` in C#, so a missing configuration
   section can never switch it on.
2. `appsettings.json` ships `false`. TEST and PROD use server-side `appsettings.Test.json` /
   `appsettings.Production.json`, which are preserved by the deploy workflows and contain **no**
   `PostPaymentCompletion` section — they therefore inherit `false` from the base file.
3. `appsettings.Development.json` (gitignored, local-only) also carries `false`.

**Local override for Release 2+ development** — never commit an enabled value. From Release 2 the
agreed method is the **gitignored `appsettings.Development.json`**, which is local-only and never
deployed:

```jsonc
"PostPaymentCompletion": {
  "Enabled": true,
  "EffectiveDateUtc": "2020-01-01T00:00:00Z"
}
```

An environment override (`$env:PostPaymentCompletion__Enabled = 'true'`) works identically and is
equally safe if preferred. Either way the committed `appsettings.json` stays `false`, and TEST/PROD
read server-side files that contain no `PostPaymentCompletion` section at all.

Enabling the flag in TEST is a **separately approved change**, not part of Release 1 or Release 2.

### Effective-date semantics

The date is evaluated against `Request.CreatedAtUtc` **only**, and it decides *when classification
is enforced at creation* — never whether classification may be skipped:

| State | Classification before completion |
|---|---|
| Completed request | Not required — stays `LEGACY_COMPLETED`, nothing is backfilled |
| Open, has PO groups, created **on/after** the effective date | Required (enforced at creation from Release 2) |
| Open, has PO groups, created **before** the effective date | **Still required** — via the Release 5 Finance classification workflow |
| Open, **no** PO group | Not applicable — the only case where legacy `FinalizeRequest` stays permitted |

---

## 3. Migration

`20260730155156_AddPostPaymentDimensions`

**Purely additive.** The generated SQL contains 20 `ALTER TABLE … ADD`, 1 `CREATE TABLE`,
3 `CREATE INDEX`, and 1 `INSERT` of a new lookup row. There is **no** `DROP`, `DELETE`,
`TRUNCATE`, `UPDATE`, `ALTER COLUMN` or `sp_rename` anywhere in `Up()`.

Two notes for the DBA:

- `ALTER TABLE [Requests] ADD [RowVersion] rowversion NOT NULL` and the same on
  `RequestPoGroups` are size-changing adds: SQL Server stamps every existing row with a value.
  No business column is read or written, but on large tables plan for the table lock and log
  growth accordingly.
- `ALTER TABLE [RequestPoGroups] ADD [FinalInvoiceStatus] nvarchar(50) NOT NULL DEFAULT
  N'UNCLASSIFIED'` sets the initial value of a **new** column. It is not an `UPDATE` of existing
  data, and it is exactly what acceptance criterion 7 requires.

Review the exact statements with:

```powershell
cd C:\dev\alpla-portal\src\backend
dotnet ef migrations script 20260727162615_AddQuotationItemOcrBaseline `
                            20260730155156_AddPostPaymentDimensions `
  --project AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj `
  --startup-project AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj `
  --idempotent --output .\r1.sql
```

---

## 4. Verification

### 4.1 Before applying (baseline)

Record the legacy `RECEIPT` count so check 7 can be compared against it:

```sql
SELECT COUNT(*) AS ReceiptAttachmentsBefore
FROM dbo.RequestAttachments WHERE AttachmentTypeCode = 'RECEIPT';
```

Run the read-only inventory: [`scripts/db/post-payment-release1-audit-readonly.sql`](../scripts/db/post-payment-release1-audit-readonly.sql).

### 4.2 After applying

Run [`scripts/db/post-payment-release1-verify-readonly.sql`](../scripts/db/post-payment-release1-verify-readonly.sql).

| # | Check | Expected |
|---|---|---|
| 1 | Migration recorded | `PASS` — exactly 1 row in `__EFMigrationsHistory` |
| 2 | New columns present and shaped | `PASS` — 20 of 20 |
| 3 / 3b | `FinalInvoiceStatus` = `UNCLASSIFIED` everywhere + default constraint present | `PASS` — 0 non-UNCLASSIFIED |
| 4 / 4b | `RowVersion` populated on `Requests` and `RequestPoGroups` | `PASS` — 0 NULL |
| 5 / 5b | `IdempotencyKey` NULL on every row; filtered unique index exists | `PASS` — 0 non-null keys |
| 6 / 6b / 6c | `WAITING_FISCAL_RECEIPT` seeded, used by nothing | `PASS` — 1 lookup row, 0 usages |
| 7 | `RECEIPT` count unchanged; no `FINAL_INVOICE` / `FISCAL_RECEIPT` attachment | `PASS` — must equal the §4.1 baseline |
| 8 / 8b / 8c | No dimension, no completion identity, no classification written | `PASS` — 0 everywhere |
| 9 | `FinalInvoiceReconciliations` exists and is empty | `PASS` — 0 rows |

### 4.3 Application behaviour (manual, TEST)

1. Take a request in `WAITING_RECEIPT` that owns a PO group and finalize it as Finance —
   it must complete exactly as before. This is the shape the guard rejects once the feature is on,
   so it is the sharpest possible check that the flag is genuinely off.
2. Confirm no new UI element appears anywhere.
3. Confirm the new endpoints do not exist (none were added).
4. Check the startup log for `PostPaymentCompletion` warnings — there should be none.

---

## 5. Rollback

Release 1 has **no runtime behaviour to roll back**: with the flag false the application executes
its pre-Release-1 code paths. Choose the smallest sufficient action.

### 5.1 Preferred — roll back the application only

Redeploy the previous API build. The schema is additive and forward/backward compatible: the
older binaries simply ignore the new columns, the new lookup row, and the new table. Nothing
reads or writes them.

**This is the default choice.** No data is at risk and no migration needs reversing.

### 5.2 If the schema must also be reverted

Only if a real problem is traced to the schema itself.

1. **Take a full database backup first** and record the backup identifier.
2. Generate and review the down script before executing anything:

```powershell
cd C:\dev\alpla-portal\src\backend
dotnet ef migrations script 20260730155156_AddPostPaymentDimensions `
                            20260727162615_AddQuotationItemOcrBaseline `
  --project AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj `
  --startup-project AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj `
  --output .\r1_down.sql
```

3. The down script drops `FinalInvoiceReconciliations`, drops
   `UX_RequestStatusHistory_IdempotencyKey`, deletes the `WAITING_FISCAL_RECEIPT` lookup row
   (`Id = 29`), and drops the 20 added columns.
4. **Precondition — verify before running it.** The down script is only safe while nothing uses
   the new schema. All of these must return zero:

```sql
SELECT COUNT(*) FROM dbo.FinalInvoiceReconciliations;
SELECT COUNT(*) FROM dbo.RequestStatusHistories WHERE IdempotencyKey IS NOT NULL;
SELECT COUNT(*) FROM dbo.Requests        WHERE CompletionCycleId IS NOT NULL;
SELECT COUNT(*) FROM dbo.RequestPoGroups WHERE FinalInvoiceStatus <> 'UNCLASSIFIED'
                                            OR FiscalReceiptAttachmentId IS NOT NULL
                                            OR OperationalReceiptCompletedAtUtc IS NOT NULL;
SELECT COUNT(*) FROM dbo.Requests r
  INNER JOIN dbo.RequestStatuses s ON s.Id = r.StatusId
  WHERE s.Code = 'WAITING_FISCAL_RECEIPT';
```

   If any is non-zero, **stop**: the workflow was activated and dropping the columns would destroy
   business data. Restore from backup instead of rolling the migration back.

5. Execute the reviewed down script inside an explicit transaction, verify, then commit.
6. Redeploy the matching previous API build — the current build's model expects the new columns.

### 5.3 Rollback decision rule

| Situation | Action |
|---|---|
| Application defect, schema fine | §5.1 — redeploy previous API build |
| Migration failed part-way | Restore from the pre-migration backup |
| Schema itself is the problem, nothing written | §5.2 — reviewed down script after the precondition check |
| Anything in the new schema already holds data | Restore from backup. Never drop populated columns |

---

## 6. Known follow-ups for later releases

Recorded here so they are not rediscovered late. **None of these is implemented in Release 1.**

1. **Transaction-safe duplicate handling (Releases 3–4, mandatory).**
   The filtered unique index on `IdempotencyKey` will reject a duplicate history insert. Handling
   that by catching SQL error 2601/2627 and continuing is **not acceptable**, because the same
   `SaveChanges` also carries the business-state change and SQL Server may abort the whole
   transaction — silently losing the state update that justified the event. The centralized
   handler must instead reload and re-evaluate state, or isolate the history insert behind a
   savepoint or a conditional (`NOT EXISTS`) insert. Noted in the code at
   `RequestStatusHistoryConfiguration`.

2. **`WAITING_FISCAL_RECEIPT` priority collision (Release 4).**
   Plan v6 §8.5 proposes priority 80 in `RequestStatusCalculator.GroupStatusPriority`, but 80 is
   already taken by `WAITING_RECONCILIATION`. Release 4 must choose a distinct value (85 sits
   naturally between `WAITING_RECONCILIATION` = 80 and `IN_FOLLOWUP` = 90) and add the status to
   the `PoGroupStatuses` helper arrays, which Release 1 deliberately left untouched.

3. **`EmailOutbox` correlation uniqueness (Release 3) — already satisfied.**
   The plan reserved a filtered unique index on `EmailOutbox.CorrelationId` for Release 3. The
   existing `IX_EmailOutbox_Correlation_Recipient_Active` (unique on `CorrelationId`,
   `RecipientEmail`, filtered to active states) already provides it. Release 3 should reuse it
   rather than add another.

4. **`Request.CompletedAtUtc` does not exist.**
   Plan v6 §11.4 pseudocode references it. Release 1 added only `CompletionCycleId` (the mandated
   scope). Release 4 must either add the timestamp column or take the completion moment from
   `UpdatedAtUtc` / the `REQUEST_COMPLETED` history row.

5. **`ParentCompletionSweep` (Release 4).**
   Phase 2 runs in-process after commit; a host recycle in that window leaves a completable
   request open. Recoverable and non-corrupting, but Release 4 owes the reconciliation sweep.
