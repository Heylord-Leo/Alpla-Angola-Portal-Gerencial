# Supplier account credit (Stage 1) — TEST deployment plan, revision 3 (for review, nothing executed)

Revision 3 of 2026-11-13 after the owner's review of revision 2. Base: branch `Portal-Gerencial-rev1` at HEAD `3d57d95` (v2.249.9 =
`origin/Portal-Gerencial-rev1`; `main` = `4238302`, untouched). **Nothing has been executed against TEST or PROD**: no commit, no
push, no version-file bump, no service restart, no TEST workflow run, no migration, no deployment, no configuration change.

Owner planning choices (recorded; they authorize preparation and validation only):

| Choice | Value |
|---|---|
| Release version | **v2.250.0** |
| Shared files | selective supplier-credit hunks; do not wait for the HR / DEV-unlock publications |
| Committed `SupplierCredit:NewReservationsEnabled` | **false** |
| Guided Tours | existing tours reviewed; **no new tour** |
| Migration preflight (read-only script + workflow) | **included in this release** |
| Primary application rollback | **restore of the preserved application-file backup** (R-B), after build-identity and configuration checks; a rollback tag stays optional and is **not** created now |

Legend: **[Verified]** = read or executed in the repository / workflows / local databases during this task; **[Recorded]** = project
records, not re-verified now; **[Not verified]** = TEST server / database / GitHub facts for the operator; **[Proposed]** = prepared,
not run.

---

## 0. Validated package and its snapshot [Verified]

Disposable local worktree `C:\dev\alpla-credit-release`, local branch `release-prep/supplier-credit-v2.250.0` at `3d57d95` (not pushed;
the branch ref itself still points at the base — the validated content is the **worktree index**, snapshotted below). It holds the
complete intended release: 50 modified + 39 added paths (89 entries; §1), the three shared files reduced to their credit hunks (§2),
`appsettings.json` → `NewReservationsEnabled: false`, the preflight script + workflow (§3). The active workspace, its branch, the peers'
HR / unlock files and the main-tree index (empty before and after) were not touched.

| Gate (run on the package, not on the mixed tree) | Result |
|---|---|
| `dotnet build src/backend/AlplaPortal.sln -c Release` | 0 errors (134 pre-existing warnings) |
| Backend full suite on the untouched base `3d57d95` | 3 382 tests: 3 381 passed, 1 failed — `PoDocumentSupersessionMigrationRehearsalTests…` (stale newest-migration expectation; pre-existing) |
| Backend full suite on the package | 3 466 tests: 3 465 passed, 1 failed (the same case), 0 skipped; relational suites EXECUTED on the LocalDB sandbox: `SupplierCreditRelationalTests` 5/5, `RequestsExplorerFilteredTotalsRelationalTests` 1/1, `PoGroupCancellationRelationalTests` 7/7 |
| Frontend `tsc -b` | clean |
| Frontend `vitest run` (whole suite) | 3 failed (`requestWorkflowSnapshot`, `workspaceQuotationActions`, `v2458ClassificationFlow` — source-level regex assertions on unrelated files); the same 3 fail on the untouched base with the same `node_modules` → pre-existing |
| `financePaymentsView.test.mjs` (node) | 104 passed |
| `npm run build` (Vite production) | built, 182 assets |
| Preflight script (§3) | PASS / FAIL paths exercised locally |
| Revision-3 focused checks (§6.4, §7.1) | `Fixture_F4_refusal_is_insufficient_credit_not_the_po_limit` 4/4; `Reversed_full_coverage_groups_reopen_into_their_bank_status_and_can_then_be_cancelled` 2/2 (Pós-Pago → `PO_ISSUED`, 100 % Antecipado → `ADVANCE_PAYMENT_REQUIRED`), run on the main tree, files synced into the package |

**Validation snapshot:** after `git add -A` in the worktree (its own index only), `git write-tree` =
**`5b32586a830103ec7de411eeba7e70bd0bd353fd`** (2 424 index entries; `git diff --cached 3d57d95 --name-status` = 50 M + 39 A; manifest
`git ls-files -s` saved beside the session). The exported patch `git diff --cached 3d57d95 --binary` (1 279 685 bytes) applies
cleanly to the main tree's index in dry-run (`git apply --cached --check` OK; index and working files untouched). The **final**
snapshot is re-taken at publication after the version / CHANGELOG edits (§5); the equality rule of §2.1 applies to that final hash.

---

## 1. Release scope

### 1.1 Tracked files modified (50)

Backend (31): `Controllers/FinanceController.cs`, `Controllers/RequestsController.cs`, `Controllers/DashboardV2Controller.cs`,
`Services/Dashboard/OperationalPipelineProjection.cs`, `Program.cs` (8 credit lines only), `appsettings.json` (`SupplierCredit` section,
default `false`), `Application/DTOs/Finance/FinanceObligationDtos.cs`, `Application/DTOs/Requests/DashboardSummaryDto.cs`,
`…/RegisterPoActionDto.cs`, `…/RequestListItemDto.cs`, `…/RequestPoGroupDto.cs`, `Application/Interfaces/Finance/IFinancePaymentEligibilityService.cs`,
`Application/Interfaces/ISupplierCapabilityEvaluator.cs`, `Domain/Constants/FinanceActionClasses.cs`, `Domain/Constants/WorkflowEventCodes.cs`,
`Domain/Entities/RequestPayment.cs`, `Domain/Entities/RequestPoGroup.cs`, `Domain/Services/FinanceObligationProjectionBuilder.cs`,
`Domain/Services/GroupCompletionProjection.cs`, `Domain/Services/RequestWorkflowProjectionBuilder.cs`, `Infrastructure/Data/ApplicationDbContext.cs`,
`Infrastructure/Data/Configurations/EntityConfigurations.cs`, `Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs`,
`Infrastructure/Services/Dashboard/FinancialSummaryProjection.cs`, `Infrastructure/Services/Finance/FinanceObligationSummaryProjection.cs`,
`…/Finance/FinancePaymentEligibilityService.cs`, `…/Purchasing/PoGroupCancellationService.cs`, `…/Suppliers/SupplierCapabilityEvaluator.cs`,
`…/WorkflowNotificationOrchestrator.cs`.
Frontend (17): `components/CorrectPoModal.tsx`, `components/RegisterPoModal.tsx`, `components/modals/FinanceActionModal.tsx`,
`lib/advancePaymentScheduledGuidance.test.ts`, `lib/api.ts` (credit hunks), `lib/correctPoSubmission.ts`, `lib/financePaymentsView.ts`,
`lib/financePaymentsView.test.mjs`, `pages/Contracts/SupplierFichaDetailContent.tsx`, `pages/Finance/FinanceHistory.tsx`,
`pages/Finance/FinancePaymentsList.tsx`, `pages/Receiving/ReceivingWorkspace.tsx`, `pages/Requests/RequestEdit.tsx`,
`pages/Requests/components/RequestGroupProgress.tsx`, `…/modern/ActionCarouselWidget.tsx`, `…/modern/RequestsDashboard.tsx`,
`…/modern/RequestsTableWidget.tsx`, `…/print/requestPrintModel.ts`, `types/index.ts` (credit hunks).
Tests (2): `…/Services/Purchasing/PoGroupCancellationRelationalTests.cs` (1 line), `…/Services/Requests/RequestLotTimelineBuilderTests.cs`.

### 1.2 Added files (39 paths)

Backend (15): `Api/Authorization/PoGroupBuyerAuthorization.cs`, `Api/Controllers/SupplierCreditsController.cs`, `Application/DTOs/SupplierCredit/*`,
`Application/Interfaces/Suppliers/*`, `Domain/Configuration/SupplierCreditOptions.cs`, `Domain/Constants/SupplierCreditConstants.cs`,
`Domain/Entities/SupplierCreditAccount.cs`, `Domain/Services/RequestFilteredTotals.cs`, `Domain/Services/SupplierCreditPolicy.cs`,
`Infrastructure/Data/Configurations/SupplierCreditConfigurations.cs`, `Infrastructure/Data/Migrations/20261109150614_AddSupplierCreditAccounts.cs` + `.Designer.cs`,
`Infrastructure/Services/Finance/SupplierCreditFactsBuilder.cs`, `…/Finance/SupplierCreditSettlementService.cs`, `…/Suppliers/SupplierCreditLedgerService.cs`.
Frontend (9): `components/po/SupplierCreditCoverageBlock.tsx`, `lib/advanceObligationNotice.ts` (+ test), `lib/requestsFilteredTotals.ts` (+ test),
`lib/workflowEventLabels.ts` (+ test), `pages/Contracts/SupplierCreditSection.tsx`, `pages/Finance/FinancePayments.css`.
Tests (8): `Services/Finance/FinanceSupplierCreditEligibilityAndProjectionTests.cs`, `…/SupplierCreditWorkflowEndpointTests.cs`,
`…/SupplierCreditStage1ClosureEndpointTests.cs`, `…/SupplierCreditRelationalTests.cs`, `Services/Requests/RequestsExplorerFilteredTotalsTests.cs`,
`…/RequestsExplorerFilteredTotalsRelationalTests.cs`, `Services/Suppliers/SupplierCreditPolicyTests.cs`, `…/SupplierCreditSettlementServiceTests.cs`.
Preflight (2): `scripts/db/preflight-migrations.ps1`, `.github/workflows/preflight-migrations-test.yml`.
Docs (4): `docs/SUPPLIER_CREDIT_PO_COVERAGE.md`, `…_DEV_TEST_GUIDE.md`, `…_DEV_RETEST_GUIDE.md`, this plan.
Added at publication (§5): edits to `docs/CHANGELOG.md`, `docs/VERSION.md`, `src/frontend/src/config.ts` (→ 53 modified).

### 1.3 Excluded (stay in the working tree) [Verified by diff keywords]

| Task | Files |
|---|---|
| HR bulk mapping | `Controllers/HRLeaveController.cs`, `HRLeaveController.BulkMapping.cs`, `DTOs/HR/HRBulkMappingDtos.cs`, `Validation/HRBulkMappingPolicy.cs`, `pages/HR/HREmployeeDirectory.tsx`, `pages/HR/HRBulkMappingPage.tsx`, `pages/HR/hr-bulk-mapping.css`, `App.tsx`, `docs/HR_DIRECTORY_BULK_MAPPING.md`, HR hunks of `lib/api.ts` (`@@ -1271,0 +1291,28`) and `types/index.ts` (`@@ -1861,0 +1977,42`) |
| Development-only unlock | `Controllers/Admin/IntegrationSettingsController.cs`, `DTOs/Integration/IntegrationSettingsDtos.cs`, `Interfaces/Integration/IDevelopmentIntegrationUnlockService.cs`, `Validation/DevelopmentIntegrationUnlockPolicy.cs`, `Services/Integration/DevelopmentIntegrationUnlockService.cs`, `pages/Admin/IntegrationSettings.tsx`, `docs/DEV_DATA_REFRESH.md`, `tests/…/Services/Integration/`, unlock hunks of `Program.cs` (`@@ -89,0 +90,2`), `lib/api.ts` (`@@ -2611,0 +2704,16`), `types/index.ts` (`@@ -1913,0 +2071,2`) |
| Other | `implementation_plan.md`, `other/`, `docs/HR_MODULE_*_RESET.md`, `docs/PO_GROUP_CANCELLATION_TEST_ACCOUNTS_v2.249.0.md`, `scripts/db/test-accounts-eligibility-v2249-readonly.sql`, `appsettings.Development.json`, build outputs (`vite.config.js` / `.d.ts` regenerated by `tsc -b` and restored) |

---

## 2. Staging the exact package — method, commands reviewed

Shared files in the package [Verified]: `Program.cs` +8 (options binding + two DI registrations); `lib/api.ts` +66 / −1; `types/index.ts`
+124 / −3; no HR / unlock identifier remains; the package builds and type-checks without the excluded files.

### 2.1 Equality rule

The proof of "committed tree = validated package" is a **tree-hash equality**, not a comparison against the preparation branch name
(which still points at the base): the tree of the release commit must equal the **final snapshot** `git write-tree` of the worktree
index, taken after the publication edits. `git diff-tree -r <final-snapshot-tree> <release-commit>^{tree}` must print nothing. Any line
is an unexpected difference and stops the publication.

### 2.2 Command review

- `git apply --cached <patch>` applies the patch to the **index only** and leaves the working files alone — this is the required
  behaviour for the mixed main tree (the peers' HR / unlock lines in `Program.cs`, `api.ts`, `types/index.ts` remain in the working files,
  unstaged). **[Verified]** by the dry run `git apply --cached --check` (OK, index unchanged, 58 modified working files unchanged).
- `git apply --index <patch>` would update the index **and the working tree** — it would overwrite the mixed working files and is
  **not** used. The combination `--cached --index` written in revision 2 is withdrawn.
- Pre-existing staged content: `git diff --cached --stat` of the main tree must be **empty** before staging (it is today [Verified]); if
  not, stop and report — the package must not be mixed with another task's staged work.
- New files are part of the same patch (they are in the worktree index, so `git diff --cached 3d57d95 --binary` includes them as
  additions); nothing is copied into the main working tree.

### 2.3 Publication procedure (`/task-publish`, after authorization)

1. In the worktree: make the publication edits (`docs/CHANGELOG.md` entry + "Current Version" v2.250.0, `docs/VERSION.md`,
   `src/frontend/src/config.ts`), re-run `tsc -b` there, `git add -A` (worktree index), `git write-tree` → **final snapshot tree** `T`.
2. Export `git diff --cached 3d57d95 --binary > credit-v2.250.0.patch`.
3. In the main tree (`Portal-Gerencial-rev1` at `3d57d95`, index empty): `git apply --cached --check` then `git apply --cached`;
   `git write-tree` in the main tree must print exactly `T`; `git diff --cached --stat` must list 53 M + 39 A; `git status` shows the
   peers' files still modified / untracked and unstaged.
4. `git commit` (index only; Conventional Commit); `git diff-tree -r T <commit>^{tree}` → empty; `git log -1 --format=%T <commit>` = `T`.
5. Build / test gates of §5 on the committed tree (worktree checkout of the commit or `git stash`-free re-run in the release worktree
   after `git reset --hard <commit>` **in the worktree only**).
6. Push `Portal-Gerencial-rev1` only with explicit authorization.

---

## 3. Migration preflight (read-only, included in the release)

Migration [Verified]: `20261109150614_AddSupplierCreditAccounts` — 4 `AddColumn` (nullable / defaulted), 3 `CreateTable`, indexes; additive;
newest file in the repository. `scripts/db/preflight-migrations.ps1` asserts fail-closed: exact `DB_NAME()`, `__EFMigrationsHistory` exists
and is an exact prefix of the repository list (count, order, ids), pending set equals the owner-typed list exactly. Only SELECT statements.
`.github/workflows/preflight-migrations-test.yml` (`workflow_dispatch`, runner `alpla-portal-test`, environment `test`, concurrency
`test-database`) reads the server-preserved `appsettings.Test.json`, masks the connection string, runs the script with
`-ExpectedDatabase Portal-Gerencial-Test` and `expected_pending`. Local validation [Verified]: DEV clone PASS (nothing expected) / FAIL
(credit migration expected but already applied) / FAIL (wrong identity); sandbox FAIL (no history table). Expected TEST run: PASS with
exactly `20261109150614_AddSupplierCreditAccounts` pending — any other result stops the sequence. **[Not verified]**: TEST history today.
`Down` is never run.

---

## 4. Flag in TEST

Committed default `false` [Verified]. Supplied only through the server-preserved `appsettings.Test.json` + app-pool recycle; the deploy and the
file-backup restore both preserve that file [Verified in `deploy-test.yml`]. **[Not verified]**: current content of that file. Sequence: deploy
with the flag absent → read-only smoke → flag `true` + recycle → `GET …/credit-eligibility` shows `enabled: true` → functional smoke.
Kill switch: `false` + recycle (existing holds keep working). PROD stays `false`; out of scope.

---

## 5. Gates and release preparation

Gates on the committed tree (repeat of §0): backend Release build; backend full suite (expected: 3 466 + any new, 1 pre-existing failure,
relational suites executed with LocalDB started); frontend `tsc -b`, `vitest run` (3 pre-existing failures), node test, Vite build; secrets
gate (`git diff --cached` without connection strings / passwords / tokens / `AdditionalConfig`); snapshot gate (`dotnet ef migrations list`
against the DEV clone: no pending model changes); flag gate; equality gate §2.1.

Release preparation: v2.250.0 (MINOR) in CHANGELOG ("Current Version" + entry disclosing the migration, the configuration key and
default, TEST-only activation, explorer per-currency totals + SQL error-130 fix, Finance-queue currency and tooltip fixes, preflight
workflow), VERSION.md, config.ts — the workflows refuse a mismatching version input [Verified]. Guided Tour impact phrase: **"existing tour
reviewed, no changes needed."** Commit `feat(finance,purchasing): supplier account credit on P.O. groups — stage 1 …`. Push only to
`origin/Portal-Gerencial-rev1`; no PR to `main`.

Canonical TEST workflow [Verified]: `Preflight TEST Migrations` → `Apply TEST Migrations` (DB-name check, DEC-145 prefix check,
`*_pre-migration.bak`, idempotent script) → `Deploy to TEST` (build, expected-migration list, SHA-256 inventory, migration-readiness gate,
backup-destination gate, timestamped file backup of api + web, pool stop, deploy with preserved config, pool start, health smoke, build
identity `2.250.0+<sha>`). All dispatched by the owner on `Portal-Gerencial-rev1`.

---

## 6. Backups, rollback, restore, and credit movements

### 6.1 Backups

Workflow backups [Verified]: `*_pre-migration.bak` (migration run), `backup_<timestamp>\api` + `\web` (deploy run); both fail closed on
`BACKUP_PATH` / free space **[Not verified]**. Operator backups added by this plan: COPY_ONLY full backup **"pre-smoke"** of
`Portal-Gerencial-Test` immediately before `Apply TEST Migrations`; COPY_ONLY **"pre-credit-movements"** after the read-only smoke and
before the flag is switched on. Record names, sizes and times.

### 6.2 Application rollback — primary mechanism R-B (file-backup restore)

Before the v2.250.0 deploy the operator records: `GET /api/app/version` → `buildId` and `buildMetadataStatus` (expected
`2.249.9+3d57d95`, `Valid` — **[Recorded]** from the v2.249.9 deploy, not verified today), `GET /build-metadata.json` → `buildId`, the
SHA-256 of `appsettings.Test.json`, and the folder `backup_<timestamp>` the deploy creates (it contains `api\build-manifest.json` whose
`buildId` must equal the recorded one — check this **before** relying on it).

Restore order (operator, manual): stop the API and Web app pools → `robocopy backup_<timestamp>\api API_DEPLOY_PATH /E /IS /IT
/XF appsettings.Test.json appsettings.Production.json appsettings.Staging.json appsettings.Local.json` and the same for `web` →
`appsettings.Test.json`: remove the `SupplierCredit` section (or `false`), confirm its hash otherwise unchanged → start the pools →
`GET /api/app/version` must return the recorded `buildId` with `Valid`, `GET /build-metadata.json` the same `buildId` → health check.
The schema stays migrated; the previous build ignores the new tables and nullable / defaulted columns **except** for credit rows (6.4).
Optional R-A (tag `v2.249.9` at `3d57d95`, dispatch `Deploy to TEST` on the tag with `version = v2.249.9`): remains available; **no tag is
created now**.

### 6.3 Database restore

Only route that removes credit movements the application cannot undo. It discards **every** TEST change after the chosen backup
(other testers' requests, HR mapping tests, approvals, uploads, SMTP / configuration rows, the migration history row). Coordination: announce
a TEST freeze window, confirm with the HR and Operations testers, stop all TEST sessions. Order: stop pools → `RESTORE DATABASE
[Portal-Gerencial-Test] … WITH REPLACE` from the chosen COPY_ONLY backup → confirm `DB_NAME()` and the newest history row
(`20261105163725…` for "pre-smoke", `20261109150614…` for "pre-credit-movements") → redeploy the matching application ("pre-smoke" →
v2.249.9 via R-B; "pre-credit-movements" → v2.250.0 may stay) → start pools → identity check → SMTP redirection check (§8) → read-only smoke.

### 6.4 Credit movements before an application rollback — verified guards and executable cleanup

Guards [Verified in code]: cancellation only in `WAITING_PO`, `WAITING_PO_CORRECTION`, `PO_ISSUED`, `ADVANCE_PAYMENT_REQUIRED`, refused
when any COMPLETED or SCHEDULED payment row, receiving, invoice or fiscal-receipt evidence exists (`PoGroupCancellationEvaluator`);
release endpoint only for a RESERVED hold on a parked group; `reverse-credit` only for an APPLIED **full coverage** without later evidence
(`WAITING_SUPPLIER_DELIVERY` is not an evidence status; receiving, invoice allocation or reconciliation are), by a **different** active
Finance user in plant scope; partial coverage → `CREDIT_REVERSAL_PARTIAL_UNSUPPORTED`.

Verified by the focused test `Reversed_full_coverage_groups_reopen_into_their_bank_status_and_can_then_be_cancelled` [Verified, 2/2]:

| Fixture | After D-1 | Reversal (Finance user ≠ the one who applied) | Reopened state | Cancellation |
|---|---|---|---|---|
| F2 Pós-Pago full | `PAYMENT_COMPLETED`, `SUPPLIER_CREDIT` COMPLETED 200 000 | allowed (no evidence) | `PO_ISSUED`, `FINAL_BALANCE` PLANNED 200 000, credit row CANCELLED, coverage 0 | allowed (no COMPLETED / SCHEDULED row) → `CANCELLED`, Finance confirms the ECF annulment |
| F3 100 % Antecipado full | `WAITING_SUPPLIER_DELIVERY`, `SUPPLIER_CREDIT` COMPLETED 150 000 | allowed (no receipt / invoice / reconciliation) | `ADVANCE_PAYMENT_REQUIRED`, `ADVANCE` PLANNED 150 000, credit row CANCELLED | allowed → `CANCELLED` |
| Ledger after both | — | REVERSAL entries | balance back to the start, reservations `REVERSED` | — |

Cleanup order before an application rollback (all through the UI / API, no financial-row edit): release every RESERVED hold (F1 via
cancellation or **Remover**; F7 if present) → reverse F2 and F3 (Finance 2) → cancel F2 and F3 → confirm the ECF annulments → account
figures: reserved 0 for every account. **If any reversal or cancellation is refused** (later evidence such as a receiving line, an invoice
allocation or a reconciliation on the synthetic group; or F1 already settled at D-2) → the application rollback is **not** performed with
movements left behind: use the database restore of 6.3. Frozen movements under v2.249.9 are not accepted as compatible (code-reviewed
at `3d57d95`: completion ignores `SUPPLIER_CREDIT` rows, queue and cash figures would count them as cash; not executed, not proven).

---

## 7. TEST smoke — setup, fixtures, account-state sequence

### 7.0 Setup (writes, performed before the read-only smoke; not created yet)

| Object | Value |
|---|---|
| Supplier ficha (Contracts) | **`ZZTEST FORNECEDOR CRÉDITO`**, NIF **`5999999901`** (synthetic; operator checks read-only that no supplier has it), ACTIVE, fictitious address, **no bank data** |
| Accounts (ficha → Conta corrente de crédito) | APA / AOA `CREDIT_IN` **1 000 000,00** ref `ZZTEST-NC-2026-0001`; APA / USD `CREDIT_IN` **1 000,00** ref `…-0002`; APS / AOA `CREDIT_IN` **200 000,00** ref `…-0003` |
| Users | Buyer B1 scoped Viana 1 + Viana 2 + APS plant (responsible Buyer of every fixture); Buyer B2 (other Buyer, same scope); Finance F1 (applies), Finance F2 (reverses), both plant-scoped and active |
| Requests (QUOTATION, one line each, awarded to the synthetic supplier's quotation through the normal approval flow) | F1–F7 below, request numbers `ZZTEST-SC-01 … 07` in the title for the explorer search |
| Documents | §7.2 PDFs; uploaded to their own fixture only |

### 7.1 Fixture matrix

| Id | Company / plant | Currency | Group total | Terms | Credit decision | Proforma | P.O. | Role |
|---|---|---|---|---|---|---|---|---|
| F1 | APA / Viana 1 | AOA | 500 000,00 | Pós-Pago | 350 000,00 partial | `ZZTEST-PF-2026-0001` | `ECF10 2026/9901` | C partial; **D-2 partial settlement, last, mandatory** — never touched by E / F |
| F2 | APA / Viana 2 | AOA | 200 000,00 | Pós-Pago | 200 000,00 full | `…-0002` | `ECF10 2026/9902` | B shared APA balance; D-1 `apply-credit`; E reversal + cancellation |
| F3 | APA / Viana 1 | AOA | 150 000,00 | 100 % Antecipado | 150 000,00 full | `…-0003` | `ECF10 2026/9903` | D-1 b2p 409 + `apply-credit` → `WAITING_SUPPLIER_DELIVERY`; E reversal + cancellation |
| F4 | APA / Viana 2 | AOA | **400 000,00** | Pós-Pago | **400 000,00 requested → 409 insufficient** (available 300 000 at that step); then **50 000,00** reserved | `…-0004` | `ECF10 2026/9904` | C insufficient (requested ≤ total, > available); E cancellation releases the hold |
| F5 | APS / APS plant | AOA | 100 000,00 | Pós-Pago | 100 000,00 full (reserved only) | `…-0005` | `ECF10 2026/9905` | B company isolation (APS 200 000) |
| F6 | APA / Viana 1 | USD | 800,00 | Pós-Pago | none (eligibility only) | `…-0006` | `ECF10 2026/9906` | B currency isolation |
| F7 | APA / Viana 1 | AOA | 80 000,00 | Pós-Pago | 20 000,00 partial | `…-0007` | `ECF10 2026/9907` | **F (optional flag-off keep / remove)** — separate from F1 |

F4 refusal verified [Verified, `Fixture_F4_refusal_is_insufficient_credit_not_the_po_limit`]: 400 000 requested on a 400 000 group with
300 000 available → `INSUFFICIENT_SUPPLIER_CREDIT` (the near-full normalisation never lifts coverage above the available); the old
120 000 / 350 000 combination would have returned `CREDIT_AMOUNT_INVALID` (P.O. limit). Nothing is written on the refusal (service and
SQL Server tests of DEV §10.7.2).

### 7.2 Synthetic documents (prepared before 7.0, outside the repository)

Searchable A4 PDFs generated from a template (text layer, no scan), header on every page **"DOCUMENTO SINTÉTICO — AMBIENTE DE TESTE
(TEST) — SEM VALOR COMERCIAL"**, supplier block exactly `ZZTEST FORNECEDOR CRÉDITO` / NIF `5999999901` / fictitious address, customer = the
buying company of the matrix, one line matching the request line, currency and total exactly as the matrix, terms text as the matrix, **no
IBAN, no bank, no real contact**. Proformas `ZZTEST-PF-2026-0001…0007`; P.O. documents titled "Encomenda Mat Escritório/Diversos ECF10
2026/99nn" (`ECF10 2026/9901…9907`; **[Not verified]** that no real `ECF10 2026/99xx` exists in TEST — operator checks
`RequestPoGroups.PurchaseOrderNumber` read-only and shifts the range if needed); two synthetic payment proofs (F1 bank part 150 000,00;
spare). Why: registration compares the extracted supplier name (`SupplierNameComparer.NamesMatch`), total and currency with the group and
needs the Primavera P.O. grammar [Verified]; the DEV PDFs name other suppliers. In the quotation upload always pick the existing
synthetic supplier; never accept an OCR-created supplier [Recorded, REQ-365].

### 7.3 Sequence and APA / AOA account state (balance / reserved / available)

| Step | Action | APA / AOA after | Other |
|---|---|---|---|
| A (read-only, flag false) | build identity; ficha lists the 3 accounts (Finance `canManage:false`); explorer search `ZZTEST-SC` → per-currency totals identical across pages; wizard on F1 shows "não está ativa"; **A2** operator read-only SQL per-currency sum over non-cancelled `ZZTEST-SC-%` requests = explorer footer | 1 000 000 / 0 / 1 000 000 | USD 1 000 / 0 / 1 000; APS 200 000 / 0 / 200 000 |
| — | COPY_ONLY "pre-credit-movements"; flag `true` + recycle; eligibility `enabled: true` | unchanged | |
| B | F1 and F2 wizards show the same APA / AOA account; F5 the APS account; F6 the USD account only; ficha: second APA / AOA account → 409 `CREDIT_ACCOUNT_EXISTS` | unchanged | |
| C1 | F1 register 350 000 partial → rows `FINAL_BALANCE` 150 000 + `SUPPLIER_CREDIT` 350 000 PLANNED, `PO_ISSUED` | 1 000 000 / 350 000 / 650 000 | B2 on F1 with credit → 403 (before B1 registers) |
| C2 | F2 register 200 000 full | 1 000 000 / 550 000 / 450 000 | |
| C3 | F3 register 150 000 full advance → `ADVANCE_PAYMENT_REQUIRED` | 1 000 000 / 700 000 / 300 000 | |
| C4 | F4 request 400 000 → **409 `INSUFFICIENT_SUPPLIER_CREDIT`**, group still `WAITING_PO`, no rows, no history; then 50 000 → `PO_ISSUED` | 1 000 000 / 750 000 / 250 000 | |
| C5 | F5 register 100 000 full | — | APS 200 000 / 100 000 / 100 000 |
| C6 | F7 register 20 000 partial | 1 000 000 / 770 000 / 230 000 | |
| D-1a | F2: `schedule` / `pay` 409 → `apply-credit` → `PAYMENT_COMPLETED`; duplicate refused | 800 000 / 570 000 / 230 000 | |
| D-1b | F3: `b2p/schedule-advance` and `b2p/confirm-advance` 409 → `apply-credit` → `WAITING_SUPPLIER_DELIVERY` | 650 000 / 420 000 / 230 000 | |
| E1 | F4 cancel → hold released, no ledger entry; Finance confirms the ECF annulment | 650 000 / 370 000 / 280 000 | |
| E2 | F2: F1-user reversal → 403; F2-user reversal → `PO_ISSUED` + `FINAL_BALANCE` 200 000 PLANNED; cancel F2 | 850 000 / 370 000 / 480 000 | |
| E3 | F3: reversal by F2-user → `ADVANCE_PAYMENT_REQUIRED` + `ADVANCE` 150 000 PLANNED; cancel F3 | 1 000 000 / 370 000 / 630 000 | |
| F (optional) | flag `false` + recycle; F7: Finance return → **Manter** (hold 20 000 unchanged, rows recreated once) → Finance return → **Alterar** 400 → **Remover** → released; new reservation on F5-type group → 400. Then **flag `true` + recycle; eligibility `enabled: true` — mandatory before D-2** | 1 000 000 / 350 000 / 650 000 | |
| G | each credit event → one redirected e-mail with the TEST banner; request history, Finanças → Histórico and Print View in Portuguese | — | |
| **D-2 (last, mandatory)** | **Preconditions:** flag `true` (eligibility `enabled: true`); F1 `PO_ISSUED`, reservation RESERVED 350 000, rows `FINAL_BALANCE` PLANNED 150 000 + `SUPPLIER_CREDIT` PLANNED 350 000, APA / AOA = 1 000 000 / 350 000 / 650 000 (if F was skipped: 1 000 000 / 370 000 / 630 000 with F7's hold still present — acceptable). Then: `pay` 150 001 → 400 `PAYMENT_AMOUNT_MISMATCH`; `pay` 150 000 → credit applied atomically, `PAYMENT_COMPLETED`; reversal attempt → 409 `CREDIT_REVERSAL_PARTIAL_UNSUPPORTED` | 650 000 / 0 / 650 000 (or / 20 000 / 630 000 with F7) | after this step only the database restore removes F1's movements |

If F is run, F1 is never touched by it (F7 is the keep / remove fixture); the flag is re-enabled and verified before D-2. If the flag cannot
be re-enabled, D-2 is **not** attempted and the round is reported incomplete.

Cleanup after the window: F7 hold removed (if still present), F5 cancelled, synthetic supplier INACTIVE; F1's settled movements remain
until the next `Sync PROD Data to TEST` or a database restore.

---

## 8. E-mail and integrations in TEST

[Verified in code] non-`PROD` `AppEnvironment:Code` → banner + redirection of every recipient to `SmtpSettings.TestRecipientEmail` when
`RedirectAllToTestRecipient` is on (database row, Admin SMTP page). Operator checklist **[Not verified]**: `AppEnvironment:Code = TEST`;
redirect on with the owner as recipient and originals shown; built-in test e-mail received; Primavera / Innux / AlplaPROD disabled; AP
notification config disabled or covered; `DevFixtures:FinanceEnabled` absent.

---

## 9. Execution sequence

| # | Step | Who | Authorization |
|---|---|---|---|
| 1 | `/task-publish`: publication edits in the worktree, final snapshot `T`, index-only staging, commit, equality and gates (§2.3, §5) | agent | **publication** |
| 2 | push `Portal-Gerencial-rev1` | agent | explicit push |
| 3 | operator records TEST identity (`/api/app/version`, `/build-metadata.json`), `appsettings.Test.json` hash, SMTP state | operator | — |
| 4 | COPY_ONLY "pre-smoke" backup | operator | — |
| 5 | `Preflight TEST Migrations`, `expected_pending = 20261109150614_AddSupplierCreditAccounts` → PASS | owner | read-only run |
| 6 | `Apply TEST Migrations` v2.250.0 | owner | **migration** |
| 7 | `Deploy to TEST` v2.250.0; verify `backup_<timestamp>\api\build-manifest.json` = recorded identity | owner / operator | **deployment** |
| 8 | 7.0 setup writes (supplier, accounts, users, requests, documents) | owner / Contracts / Buyer | — |
| 9 | smoke A + A2 (read-only) | owner / operator | — |
| 10 | COPY_ONLY "pre-credit-movements" backup; flag `true` + recycle | operator | configuration |
| 11 | smoke B, C, D-1, E, (F with flag back on), G, then **D-2 last** | owner | — |
| 12 | TEST validation window; closure report | owner / agent | — |

Rollback decision: before D-2 → cleanup of 6.4 (release holds, reverse + cancel F2 / F3) then R-B; if any step is refused → 6.3. After D-2 →
6.3 only (pre-credit-movements backup + v2.250.0, or pre-smoke backup + v2.249.9 via R-B), inside an announced freeze.

---

## 10. Remaining blockers and decisions

| # | Item | Status |
|---|---|---|
| 1 | TEST migration history (exactly one pending) | **[Not verified]** until step 5 |
| 2 | SMTP redirection, AP config, providers (§8) | **[Not verified]** operator checklist |
| 3 | NIF `5999999901` and `ECF10 2026/9901–9907` free in TEST | **[Not verified]** operator read-only check |
| 4 | `BACKUP_PATH`, free space, the two COPY_ONLY backups, recorded identity before deploy | operator |
| 5 | Freeze window and tester coordination for a possible database restore | owner |
| 6 | Optional rollback tag `v2.249.9` | owner, later; not created now |
| 7 | Residual DEV visual: 684 tooltip after the Vite reload | not blocking |
| 8 | Out of scope: PROD, `main`, Stage 2, supplier-name search, performance, home "vs mês anterior" | — |

**Readiness:** the package (snapshot `5b32586a…`, §0) is ready for **publication authorization**; publication re-snapshots after the version
edits and proves tree equality (§2.1). Migration (steps 5–6) and deployment (step 7) remain separate authorizations.
