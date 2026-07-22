# PROD → Local Development Data Refresh

**Application:** Alpla Angola - Portal Gerencial
**Source:** Production database `Portal-Gerencial` on `AOVIA1VMS011`
**Target:** Local database `Portal-Gerencial-Dev-ProdClone` on the developer's own LocalDB instance
**Workflow File:** [export-prod-data-dev.yml](file:///c:/dev/alpla-portal/.github/workflows/export-prod-data-dev.yml)
**Scripts:** [validate-export-prod-data-dev-inputs.ps1](file:///c:/dev/alpla-portal/scripts/db/validate-export-prod-data-dev-inputs.ps1), [export-prod-data-dev.ps1](file:///c:/dev/alpla-portal/scripts/db/export-prod-data-dev.ps1), [import-prod-data-dev.ps1](file:///c:/dev/alpla-portal/scripts/db/import-prod-data-dev.ps1), [dev-safety-neutralization.sql](file:///c:/dev/alpla-portal/scripts/db/dev-safety-neutralization.sql)

---

## 1. Purpose

This gives a developer a realistic, Production-shaped local database for Development work, without ever pointing a local application instance directly at Production, and without touching the shared `Portal-Gerencial-Test` database used by the TEST environment.

The refresh is split into two independent halves:

1. **Export** (`export-prod-data-dev.yml`, GitHub Actions, runs on `AOVIA1VMS011`) — creates a fresh `BACKUP DATABASE` of `Portal-Gerencial`, computes its SHA-256 checksum, uploads both files as a 1-day-retention build artifact, and then deletes both files from the runner's local disk.
2. **Import** (`import-prod-data-dev.ps1`, runs LOCALLY on the developer's own machine) — verifies the downloaded backup's checksum, restores it into a brand-new, isolated LocalDB database (`Portal-Gerencial-Dev-ProdClone`), then runs `dev-safety-neutralization.sql` to disable every outbound email/integration path and clear live secrets and password-reset tokens before the developer is given the connection string to use.

> **This workflow does NOT:**
> - Modify, restore over, or read from `Portal-Gerencial-Test`
> - Modify, drop, detach, or back up `AlplaPortalV1` (the database already used by `appsettings.Development.json`)
> - Modify `appsettings.Development.json` in any way
> - Pseudonymize or scrub Users, Suppliers, Requests, or any other transactional/business data (see [§8 Residual PII Risk](#8-residual-pii-risk))
> - Leave the exported `.bak`/`.bak.sha256` behind on the Production runner under normal success or failure (see [§5 Cleanup Guarantees](#5-cleanup-guarantees))
> - Run automatically — every destructive step requires an explicit confirmation phrase and a passing checksum verification

---

## 2. Architecture

```
AOVIA1VMS011 (self-hosted runner, Production SQL Server)
   |
   |  workflow_dispatch: "Export PROD Data for Dev"
   |  environment: production (approval gate) | permissions: contents: read
   v
BACKUP DATABASE [Portal-Gerencial] --> .bak file
                                        |
                                        +--> Get-FileHash (SHA-256) --> .bak.sha256 file
                                                        |
                                                        v
                                   GitHub Actions artifact (both files, 1-day retention)
                                                        |
                                       cleanup step deletes BOTH files from AOVIA1VMS011
                                       (runs on success, on upload failure, and on any
                                        later step failure - if: always())
                                                        |
                                                        | developer downloads both files
                                                        v
                                          Developer's local machine
                                                        |
                              import-prod-data-dev.ps1 -ChecksumFilePath ... -Apply
                                                        |
                        checksum verified (pure file I/O, before any SQL connection)
                                                        |
        +-------------------------------+-------------------------------+-------------------------------+
        v                               v                               v
  RESTORE DATABASE WITH REPLACE   dev-safety-neutralization.sql   robocopy attachments
  into LocalDB: Portal-Gerencial-  (fails closed - THROWs if       (additive only, never
  Dev-ProdClone                    verification does not pass)     /MIR, never /PURGE)
        |
        v
  $env:ConnectionStrings__DefaultConnection printed
  (only if every step above succeeded)
```

Reused patterns (no logic duplicated from memory — every pattern below was read from the existing file before being adapted):

| Pattern | Source | Used in |
|:---|:---|:---|
| Edition-aware backup clause (Express → no `COMPRESSION`) | `sync-prod-data-test.ps1` | `export-prod-data-dev.ps1`, `import-prod-data-dev.ps1` |
| `RESTORE FILELISTONLY` to discover real logical file names (never hardcode-trust them) | `sync-prod-data-test.ps1` | `import-prod-data-dev.ps1` |
| Defensive `IF OBJECT_ID(...) IS NOT NULL` post-restore neutralization | `sync-prod-data-test.ps1` | `dev-safety-neutralization.sql` (extended with `COL_LENGTH` column-level guards, dynamic SQL, and a fail-closed verification pass covering every neutralized table, including `IntegrationProviderSettings`) |
| Robocopy exit-code convention (`0-7` non-fatal, `>=8` fatal) | `sync-prod-data-test.ps1`, `deploy-prod.yml` | `import-prod-data-dev.ps1` |
| Ref-name + confirmation-phrase + SemVer-vs-`docs/VERSION.md` input validation | `validate-sync-prod-data-test-inputs.ps1` | `validate-export-prod-data-dev-inputs.ps1` |
| `GITHUB_SHA`-authoritative commit resolution (Git optional) | `resolve-sync-commit-metadata.ps1` | reused unmodified by `export-prod-data-dev.yml` |
| Forbidden-database-name / forbidden-path guard checked before any SQL connection or file write | `deploy-prod.yml`, `apply-migrations-prod.yml` | `import-prod-data-dev.ps1`, `export-prod-data-dev.ps1` |
| `PROD_DB_CONNECTION_STRING` secret | `deploy-prod.yml`, `apply-migrations-prod.yml` | `export-prod-data-dev.yml` (no new secret created) |

---

## 3. Prerequisites

### 3.1 GitHub Environment

Reuses the existing **`production`** environment and its approval gate. No new GitHub Environment, secret, or variable is required — `export-prod-data-dev.yml` reuses the existing `PROD_DB_CONNECTION_STRING` secret and `BACKUP_PATH` variable, and declares only `permissions: contents: read` (no write access to contents, issues, pull requests, packages, or deployments).

### 3.2 Local machine

- SQL Server LocalDB (`MSSQLLocalDB` instance) — already installed and running on this machine (`sqllocaldb info MSSQLLocalDB` confirms `State: Running`).
- Enough free disk space for the backup and restored database (Production backup is small — under 100 MB at time of writing).
- Optional: read access to a copy of the Production attachments folder, only if `-AttachmentMode` other than `None` is used.

---

## 4. Artifact contents, retention, and sensitivity

Each export run produces an artifact named `prod-dev-clone-backup-<version>-run<run_id>` containing **exactly two files**:

```
Portal-Gerencial_dev-export_run-<run_id>_attempt-<run_attempt>_<timestamp>.bak
Portal-Gerencial_dev-export_run-<run_id>_attempt-<run_attempt>_<timestamp>.bak.sha256
```

The run ID and run attempt are embedded in the filename (in addition to a timestamp) specifically so no two runs can ever collide on a filename, even if started within the same second.

**Artifact retention is 1 day** — the minimum GitHub Actions allows. There is no technical reason to hold a Production database artifact longer than that; it should be downloaded and deleted promptly by the person who requested it.

**Sensitivity warning — read before downloading:** the `.bak` file is a full backup of `Portal-Gerencial`, taken *before* any neutralization. Until the local `import-prod-data-dev.ps1 -Apply` step completes successfully, the downloaded `.bak` contains, in its raw table data:
- AES-encrypted `IntegrationProviderSettings.EncryptedPassword` / `ApiKeyEncrypted`
- AES-encrypted `SmtpSettings.EncryptedPassword`
- `Users.PasswordHash`, and any live, unexpired `Users.PasswordResetToken` rows
- The full, un-scrubbed contents of Users, Suppliers, Requests, and every other business table

Treat the downloaded `.bak`/`.bak.sha256` with the same confidentiality as Production itself for as long as they exist on your machine. **Delete both files as soon as the local import has succeeded** (the import script's final success message says this too).

---

## 5. Cleanup guarantees

The workflow's **"Clean up Production backup files from the runner"** step:
- Runs with `if: always()` — after a successful upload, after a failed upload, and after any later step failure — as long as the export step produced a path.
- Deletes **only** the two literal files named by `steps.export.outputs.backup_file_path` and `steps.export.outputs.checksum_file_path`, using `Test-Path -LiteralPath` / `Remove-Item -LiteralPath` — never a directory, never a wildcard, never anything else already sitting in `BACKUP_PATH`.
- **Fails the workflow** if either file cannot be confirmed removed (deliberate policy: a Production backup that cannot be deleted from the runner must surface as a failure requiring manual attention, not a silent success).

`export-prod-data-dev.ps1` itself also has a defensive cleanup path: if an error occurs *after* the `.bak`/`.bak.sha256` files are created but *before* their paths are written to `$GITHUB_OUTPUT` (so the workflow-level cleanup step would have no path to act on), the script's own `catch` block removes whatever it already created before re-throwing the error.

Net effect: under normal success and under ordinary failure paths, no Production `.bak` is left behind on `AOVIA1VMS011`.

---

## 6. First-Run Command Sequence (proposed — not executed by this change)

### Step 1 — Export (GitHub Actions, requires `production` environment approval)

Dispatch **"Export PROD Data for Dev"** from `main` with:
- `confirm_export` = `EXPORT_PROD_FOR_LOCAL_DEV`
- `release_version` = current value in `docs/VERSION.md` (e.g. `v2.209.0`)

Download the resulting artifact (`prod-dev-clone-backup-<version>-run<run_id>`) and extract **both** the `.bak` and `.bak.sha256` files into the same local folder, e.g. `C:\Temp\`.

### Step 2 — Preview the import (no changes made, checksum already verified)

```powershell
.\scripts\db\import-prod-data-dev.ps1 `
  -BackupFilePath "C:\Temp\Portal-Gerencial_dev-export_run-123456_attempt-1_20260721_101500.bak" `
  -ChecksumFilePath "C:\Temp\Portal-Gerencial_dev-export_run-123456_attempt-1_20260721_101500.bak.sha256"
```

Preview mode always prints: the backup path, the checksum source, the computed local SHA-256, and whether it matches — even without `-Apply`, so you know before running it for real whether the download is intact.

### Step 3 — Apply the import

```powershell
.\scripts\db\import-prod-data-dev.ps1 `
  -BackupFilePath "C:\Temp\Portal-Gerencial_dev-export_run-123456_attempt-1_20260721_101500.bak" `
  -ChecksumFilePath "C:\Temp\Portal-Gerencial_dev-export_run-123456_attempt-1_20260721_101500.bak.sha256" `
  -Apply -Confirmation "APPLY-PROD-CLONE-IMPORT-DEV"
```

`-Apply` is refused outright — before any SQL connection is opened — unless either `-ChecksumFilePath` or `-ExpectedSha256` is supplied and the computed hash matches. Prefer `-ChecksumFilePath` (the paired file from the export artifact) as shown above; `-ExpectedSha256 <64-hex-chars>` is available if only the raw hash value was communicated to you.

### Step 4 (optional) — Include attachments

```powershell
.\scripts\db\import-prod-data-dev.ps1 `
  -BackupFilePath "C:\Temp\Portal-Gerencial_dev-export_run-123456_attempt-1_20260721_101500.bak" `
  -ChecksumFilePath "C:\Temp\Portal-Gerencial_dev-export_run-123456_attempt-1_20260721_101500.bak.sha256" `
  -AttachmentMode FullClone `
  -AttachmentSourcePath "\\path\to\a\copy\of\prod\attachments" `
  -Apply -Confirmation "APPLY-PROD-CLONE-IMPORT-DEV"
```

Attachment sync is always additive: it copies files that do not already exist locally (`/E /XC /XN /XO`), and never uses `/MIR` or `/PURGE`. It will never overwrite or delete the 383 files already present in `data\attachments` in this repository checkout. The target path is validated to resolve inside a normalized `...\data\attachments` directory, and can never resolve to a drive root, the Windows/Program Files trees, the repository root itself, or the same path as the source.

### Step 5 — Point the local API at the clone

The script prints this line only if every safety check passed — the checksum match, the restore, `dev-safety-neutralization.sql`'s fail-closed verification, and the post-restore `ONLINE` state check:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=(localdb)\MSSQLLocalDB;Database=Portal-Gerencial-Dev-ProdClone;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
```

**Do not start the local API against this database unless the import script printed "Import completed successfully" and this connection-string line.** If the script stopped earlier with an error, the database is not safe to use yet.

Set the variable in the same shell session before running `dotnet run` for `AlplaPortal.Api`. This does not modify `appsettings.Development.json`, and unsetting the variable (or closing the shell) reverts to the existing `AlplaPortalV1` LocalDB database.

### Step 6 — Clean up your local downloads

Once the import has succeeded and you have confirmed the application starts correctly against the clone, **delete the downloaded `.bak` and `.bak.sha256` files** from your machine. They are no longer needed once `Portal-Gerencial-Dev-ProdClone` exists, and they contain un-neutralized Production data (see [§4](#4-artifact-contents-retention-and-sensitivity)).

---

## 7. Rollback procedure

Because `Portal-Gerencial-Dev-ProdClone` is disposable local Development data (never referenced by any deployed environment), rollback is intentionally simple:

- **If the previous clone must be restored:** `import-prod-data-dev.ps1` automatically backs up an existing `Portal-Gerencial-Dev-ProdClone` to `-LocalBackupDir` (default `%USERPROFILE%\AlplaPortalDevCloneBackups`) before any `WITH REPLACE` restore, named `Portal-Gerencial-Dev-ProdClone_<timestamp>_pre-replace.bak`. Restore that file manually via SSMS/`sqlcmd` (`RESTORE DATABASE ... WITH REPLACE`) if you need to undo a bad import.
- **If the clone is simply unwanted:** drop it directly (`DROP DATABASE [Portal-Gerencial-Dev-ProdClone]` against `(localdb)\MSSQLLocalDB`) and re-run the import from a fresh export whenever needed — there is no environment or deployment dependent on this database.
- **`AlplaPortalV1` is never touched by any part of this pipeline**, so no rollback is ever needed for it as a result of running this refresh.

---

## 8. What Gets Neutralized

`dev-safety-neutralization.sql` runs after every restore and **fails closed** — if any of the checks below cannot be verified, it raises an error and `import-prod-data-dev.ps1` stops before printing the connection-string instructions.

| Area | Action | Paired fail-closed verification |
|:---|:---|:---|
| `EmailOutbox` | Every `PENDING` / `PROCESSING` / `FAILED` row is moved to `DEAD_LETTER` | Zero rows remain in any active/retryable status |
| `SmtpSettings` | Forces `RedirectAllToTestRecipient = 1`, `AllowRealRecipientsInNonProduction = 0`, subject-prefix and body-warning banners enabled, and clears the encrypted SMTP password | Zero rows without safe redirection settings |
| `IntegrationProviders` | All rows set to `IsEnabled = 0` | Zero rows with `IsEnabled = 1` |
| `IntegrationProviderSettings` | Clears `EncryptedPassword`, `ApiKeyEncrypted`, `Server`, `ApiBaseUrl`, and `AdditionalConfig`; marks rows `IsReadOnly = 1` | Zero rows with a non-null `EncryptedPassword`, `ApiKeyEncrypted`, `Server`, or `ApiBaseUrl`; zero rows with `IsReadOnly = 0` (and, defensively, zero rows with `IsEnabled = 1` if that column ever exists on this table) |
| `Users` | Clears `PasswordResetToken` / `PasswordResetTokenExpiryUtc` for every row | Zero rows with a non-null `PasswordResetToken` |

Every table and column reference above is guarded with `OBJECT_ID` / `COL_LENGTH` existence checks and executed via dynamic SQL, so the script does not fail to compile against a schema version where one of these tables or columns does not exist — it simply skips that specific check and prints why, without failing merely because an optional table or column is absent.

**Verified independently of this pipeline's own claims:** clearing `SmtpSettings.EncryptedPassword` does not prevent the local API from starting. `SmtpSettingsService.GetEffectiveSettingsAsync` falls back to an empty/`appsettings`-sourced password with no exception when the encrypted value is `NULL`, `EmailService` never validates SMTP configuration at startup (only lazily per send), and `EmailOutboxProcessor` claims zero rows once every `EmailOutbox` entry has been moved to `DEAD_LETTER` — so the send path is never even reached.

---

## 9. Attachment modes: FullClone vs. Incremental

Because attachment files are GUID-named and immutable once created (confirmed in `AttachmentsController`), **`FullClone` and `Incremental` currently execute the identical, maximally-safe robocopy invocation**:

```
/E /XC /XN /XO
```

(recurse; skip any file that already exists locally, regardless of timestamp — never overwrite, never delete). There is no timestamp-checkpoint logic distinguishing the two modes today. The distinction is **operational and documentary**, not a different copy algorithm:

- **`FullClone`** — intended for the very first run on a machine: recursively scans the complete source tree and copies every attachment not already present locally.
- **`Incremental`** — intended for later, top-up runs: recursively scans the source tree and copies every attachment not already present locally (in practice, a much smaller delta, since most files will already exist).

Both are always additive-only: no `/MIR`, no `/PURGE`, no deletion, no overwriting of existing files. Robocopy exit codes 0–7 are treated as non-fatal (per the repository's existing convention); 8 or higher aborts the script. Source and target paths are each normalized via `[System.IO.Path]::GetFullPath()` and validated: the target can never resolve to a drive root, the Windows or Program Files trees, the repository root itself, or the same path as the source, and must resolve inside a normalized `...\data\attachments` path.

---

## 10. Residual PII Risk

This first implementation does **not** pseudonymize or scrub:
- User names, emails, or password hashes
- Supplier names, contacts, or banking details
- Request, quotation, PO, and payment content and any attached documents

Anyone running this refresh is working with a **full copy of Production business data** on their local machine, with only the outbound-communication and live-credential surface neutralized. Treat the resulting local database and any copied attachments with the same confidentiality as Production itself: do not commit them, do not copy them to shared/unencrypted locations, and delete the downloaded `.bak`/`.bak.sha256` once the local import has succeeded (see [§6, Step 6](#6-first-run-command-sequence-proposed--not-executed-by-this-change)).

---

## 11. Troubleshooting

| Symptom | Likely cause |
|:---|:---|
| `CHECKSUM REQUIRED` | `-Apply` was supplied without `-ChecksumFilePath` or `-ExpectedSha256` — supply one (preferably the `.bak.sha256` downloaded alongside the backup) |
| `CHECKSUM VERIFICATION FAILED` | The downloaded `.bak` is corrupted or incomplete — re-download both files from the workflow artifact and retry; no SQL connection is opened when this happens |
| `SQL CONNECTION FAILED: could not connect to LocalDB instance` | Run `sqllocaldb start MSSQLLocalDB` first |
| `DEVELOPMENT SAFETY NEUTRALIZATION FAILED` | A table/column this script depends on may have changed shape; check the printed `[SQL]` messages for which check failed before re-running |
| `ATTACHMENT SYNC FAILED: robocopy exit code >= 8` | Check that `-AttachmentSourcePath` is reachable and not locked; robocopy codes 0–7 are informational and already tolerated |
| `VALIDATION FAILED: attachment target path must end in '\data\attachments'` | A custom `-AttachmentTargetPath` was supplied that doesn't resolve into a `...\data\attachments` directory — use the default or correct the path |
| Script exits without printing the `ConnectionStrings__DefaultConnection` line | This is the fail-closed behavior working as intended — read the error above it, the import must be treated as incomplete, and the API must not be started against the target database |
| Export workflow's cleanup step fails | Treat as requiring manual verification: log into `AOVIA1VMS011` and confirm whether the named `.bak`/`.bak.sha256` files under `BACKUP_PATH\dev-export` still exist, and delete them by their exact printed filename if so |
