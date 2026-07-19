# Deployment Checklist — Alpla Angola Portal Gerencial

> [!IMPORTANT]
> This checklist must be completed for every deployment to TEST or PRODUCTION.
> For local development, see the dedicated section at the end.

## Pre-Deployment

- [ ] Verify the correct Git tag/version is being deployed
- [ ] Confirm `appsettings.{Environment}.json` connection string is correct for the target database
- [ ] Verify the IIS Application Pool is configured for the `.NET 8` runtime
- [ ] Confirm the `ASPNETCORE_ENVIRONMENT` variable is set (`Test` or `Production`)
- [ ] Back up the target database before deployment
- [ ] Review CHANGELOG.md for breaking changes in this release

## EF Core Migration Checklist (Mandatory Before Deploy)

> [!IMPORTANT]
> **Since v2.185.9 (DEC-137)**, `Database.Migrate()` is **disabled** in non-Development environments.
> The IIS runtime identity does NOT have DDL permissions.
> The deployment workflow will **block** startup if pending migrations are detected.

> [!TIP]
> **Since DEC-139**, migrations can be applied automatically via GitHub Actions workflows
> instead of manual RDP + sqlcmd execution. The expected migration list is auto-generated
> from the EF Core migrations folder — no more hardcoded arrays to maintain.

### Release Flow: Without Migrations

If the release does NOT include database schema changes:

1. `task-publish`
2. Run **Deploy to TEST** workflow
3. Validate TEST
4. Run **Deploy to PRODUCTION** workflow

### Release Flow: With Migrations

If the release includes database schema changes:

1. `task-publish`
2. Run **Apply TEST Migrations** workflow
3. Run **Deploy to TEST** workflow
4. Validate TEST
5. Run **Apply PRODUCTION Migrations** workflow (requires `YES-PROD` confirmation)
6. Run **Deploy to PRODUCTION** workflow
7. Validate PRODUCTION

### How to Identify If a Release Includes Migrations

- **New files** in `src/backend/AlplaPortal.Infrastructure/Data/Migrations/`
- **CHANGELOG.md** mentions database/schema changes
- **Deploy workflow** reports pending migrations and blocks startup
- **`check-pending-migrations.ps1`** reports pending migrations
- **Apply Migrations workflow** detects and reports pending count

### Manual Migration Fallback

If the automated workflow cannot apply a migration (e.g., missing `.Designer.cs` — see DEC-138):

1. **Back up the target database** using SSMS or `BACKUP DATABASE`.
2. **Generate** or craft an idempotent SQL script manually.
3. **Apply** the script to the target database using SSMS or `sqlcmd` with a DBA-level account.
4. **Verify** `__EFMigrationsHistory` contains all expected migration IDs.
5. Re-run `check-pending-migrations.ps1` to confirm `RESULT: PASS`.

### Migration List Maintenance (DEC-139)

The expected migration list is now **auto-generated** from the EF Core migrations folder.
When you add a new migration, you only need to:

1. Add the migration file to `src/backend/AlplaPortal.Infrastructure/Data/Migrations/`
2. Ensure it includes a proper `.Designer.cs` file (for `dotnet ef` compatibility)

The following files are auto-generated and no longer need manual updates:
- `expected-migrations.txt` — generated during build, packaged with the API artifact
- `get-expected-migrations.ps1` — scans the migrations folder at runtime
- Deploy workflow migration checks — read from `expected-migrations.txt`

### Design-Time Tooling (DEC-144)

Idempotent SQL is generated at design time WITHOUT running the API host:

- **Local `dotnet-ef` tool**, pinned to **8.0.11** in `.config/dotnet-tools.json`. The workflows run
  `dotnet tool restore` and **assert** `dotnet ef --version` contains `8.0.11` (fail otherwise). No global
  tool is installed/updated on the runner.
- **`DesignTimeDbContextFactory`** (`AlplaPortal.Infrastructure/Data/`) supplies the DbContext; both
  `--project` and `--startup-project` target `AlplaPortal.Infrastructure`, so `Program.cs` (and its runtime
  connection-string guard) never execute during generation. No real connection string is required to
  generate the script; the factory's non-operational placeholder never connects to LocalDB.
- Generation reuses the workflow's Release build. Generation failure aborts before any SQL is applied.

### Incremental Range Scripting (DEC-145)

The script is generated for the **pending range only**, not from the first migration — a full script
re-emits historical migration bodies and can fail to compile when a historical body references a column
a later migration dropped (e.g. `Departments.ResponsibleUserId`, error 207).

- **Strict prefix validation** runs first (before backup/generation): applied migrations must be an exact,
  contiguous prefix of the filesystem list (`get-expected-migrations.ps1` order). Blocks on
  out-of-order / gap / interleaved-pending / duplicate / foreign history, reporting index/expected/found.
  History is never auto-corrected.
- **FROM/TO**: `dotnet ef migrations script <FROM> <TO> --idempotent` with `FROM` = last applied
  (`0` when the DB is empty — full history is expected there) and `TO` = last filesystem migration.
- **Empty DB**: `FROM = 0` — the full script is expected and required.
- **No pending**: early success; no backup, no script, no `sqlcmd`.
- **Exact-set validation**: the MigrationIds INSERTed into `__EFMigrationsHistory` must equal the pending
  set exactly (none already-applied, none after TO); incremental scripts must contain no
  `ResponsibleUserId` and no `SET QUOTED_IDENTIFIER OFF`. Mismatch aborts before `sqlcmd`.
- **After a STEP 5 failure** (multi-batch, no global transaction — not guaranteed atomic): the script
  prints a read-only `__EFMigrationsHistory` snapshot and requires manual verification of history + objects
  **before** any restore or retry. Run `scripts/db/check-pre-migration-state.ps1` (read-only) to confirm
  the expected pre-migration state (e.g. TEST after the 2026-07-17 failure: 79 applied, none of the 3 new
  recorded, `CreationOrigin` / `Companies.TaxId` / `IX_Companies_TaxId` / `IX_Suppliers_TaxId` absent).
  **Never auto-restore.**


---

## Deployment Steps

1. **Complete the EF Core Migration Checklist above** (if the release includes schema changes)
2. **Stop the IIS Application Pool** for the target site
3. **Deploy the new build artifacts** to the target directory on the server
4. **Start the IIS Application Pool**
5. **Monitor startup logs** — the application will:
   - Detect any remaining pending migrations (safety net)
   - Validate critical table existence
   - **Crash with a descriptive message** if pending migrations are found (by design in TEST/PRODUCTION)
6. **If startup fails:**
   - Check the console/event log for `[STARTUP] FATAL:` or `[STARTUP] PENDING:` messages
   - Apply the missing migrations manually and restart
   - Run `docs/POST_INSTALL_DATABASE_VALIDATION.sql` against the database to diagnose
   - Do NOT manually force the app to start — fix the root cause first

## Post-Deployment Validation

- [ ] Verify the application starts successfully (no crash loops)
- [ ] Run `docs/POST_INSTALL_DATABASE_VALIDATION.sql` against the target database
- [ ] Verify all critical tables show `OK` status
- [ ] Verify all seed data rows meet minimum counts
- [ ] Verify no orphan FK records exist
- [ ] Test the `/api/v1/health` endpoint returns `200 OK`
- [ ] Test login with an active user
- [ ] Test `GET /api/v1/lookups/request-types` returns RequestTypes with Code field
- [ ] Test `GET /api/v1/iva-rates` returns IVA rates
- [ ] Test `RequestCreate` / "Novo Rascunho" flow end-to-end
- [ ] Verify `/api/v1/users/me` returns correct plant scopes

## First-Time Installation (Clean Database)

For a clean database with no existing data:

1. The consolidated baseline migration (`20260225000000_ConsolidatedBaseline`) will create all tables and seed data
2. All subsequent migrations will run in order
3. After deployment, create the first administrator user:
   - Use the application's user management interface, OR
   - Execute `docs/ADMIN_USER_SEED_TEMPLATE.sql` (edit the template with actual values first)
4. Run the full post-deployment validation checklist above

## Existing Database (Upgrade)

For databases that already have the schema (e.g., TEST after manual repair):

1. **Before deploying the new build**, register the baseline migration if it is not already recorded:
   ```sql
   IF NOT EXISTS (
       SELECT 1 FROM __EFMigrationsHistory
       WHERE MigrationId = '20260225000000_ConsolidatedBaseline'
   )
   BEGIN
       INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
       VALUES ('20260225000000_ConsolidatedBaseline', '8.0.2');
       PRINT '[OK] Registered ConsolidatedBaseline migration.';
   END
   ELSE
   BEGIN
       PRINT '[SKIP] ConsolidatedBaseline already registered.';
   END
   ```
2. Apply any pending migrations using the procedure in the **EF Core Migration Checklist** above.
3. Deploy the new build. The application will verify that all migrations are applied on startup.

## Emergency Rollback

If the deployment causes critical issues:

1. Stop the IIS Application Pool
2. Restore the database from the pre-deployment backup
3. Deploy the previous version's build artifacts
4. Start the IIS Application Pool
5. Run post-deployment validation again
6. Document the issue in CHANGELOG.md for investigation

---

## Production Environment Specifics

> [!IMPORTANT]
> The Production environment runs on `AOVIA1VMS011` alongside the Test environment.
> Production uses **completely separate** paths, ports, IIS sites, and database.

| Aspect | Value |
|:---|:---|
| API Port | **5002** (NOT 5000 or 5001) |
| Database | **`[Portal-Gerencial]`** (NOT `Portal-Gerencial-Test`) |
| API Path | `C:\Apps\AlplaPortal\Prod\api` |
| Web Path | `C:\Apps\AlplaPortal\Prod\web` |
| URL | `https://portalgerencial.alpla.net` |
| ASP.NET Environment | `Production` |
| IIS API Pool | `AlplaPortal-Prod-Api-Pool` |
| IIS Web Pool | `AlplaPortal-Prod-Web-Pool` |
| Configuration | `appsettings.Production.json` (server-side only) |

For the full Production deployment guide, see [GITHUB_ACTIONS_PROD_DEPLOYMENT.md](file:///c:/dev/alpla-portal/docs/GITHUB_ACTIONS_PROD_DEPLOYMENT.md).  
For Production rollback, see [ROLLBACK_PROCEDURE_PROD.md](file:///c:/dev/alpla-portal/docs/ROLLBACK_PROCEDURE_PROD.md).  
For Production post-deploy validation, see [POST_DEPLOYMENT_CHECKLIST_PROD.md](file:///c:/dev/alpla-portal/docs/POST_DEPLOYMENT_CHECKLIST_PROD.md).

### Email / SMTP Configuration (Production)

> [!IMPORTANT]
> A new Production database has **no email settings by default**. Password reset, workflow notifications, and proforma deadline alerts will fail silently until SMTP is configured.

**Where email settings are stored:**

| Table | Purpose |
|:---|:---|
| `SmtpSettings` | Primary SMTP config — server, port, sender email, sender name, SSL, AES-encrypted password |
| `IntegrationProviders` (Code=`SMTP`) | Integration dashboard record |
| `IntegrationConnectionStatus` (SMTP) | Last test result and connection status |
| `IntegrationProviderSettings` (SMTP) | Optional extended settings for the SMTP provider |

**How to initialize Production email from Test:**

1. Run `scripts/db/configure-production-email.sql` on AOVIA1VMS011 using SSMS.
2. The script copies `SmtpSettings` and integration status from `[Portal-Gerencial-Test]` to `[Portal-Gerencial]`.
3. Creates a backup before making changes (SQL Express — no compression).

**Prerequisites:**

- Both environments must share the same `AppConfig:EncryptionKey` in their respective `appsettings.{Environment}.json` files. The SMTP password is AES-encrypted with this key. If the keys differ, the Production API will fail to decrypt the password copied from Test.

**How to validate email sending:**

1. Open Production Portal > Administração > Integrações.
2. Locate the **Email / SMTP Service** provider.
3. Click **Testar Conexão** — this sends a test email to the sender address.
4. If successful, trigger a password reset for a known user (e.g., `leonardo.cintra@alpla.com`).
5. Verify the email arrives in the inbox.

**Security notes:**

- Never log, print, or commit the SMTP password or `EncryptionKey`.
- The `SmtpSettings.EncryptedPassword` column is AES-encrypted and never exposed via the API.
- The SQL migration script masks all sensitive values in its output.

---

## Sync PROD Data to TEST (Manual, Destructive on TEST)

> [!CAUTION]
> This workflow **overwrites `[Portal-Gerencial-Test]` completely** with a restore of PROD data.
> PROD (`[Portal-Gerencial]`) is always the **read-only source** and is never modified by this workflow.

**Where it lives:** `.github/workflows/sync-prod-data-test.yml` + `scripts/db/sync-prod-data-test.ps1`,
maintained in **Portal-Gerencial-rev1** (DEC-147). It is no longer maintained on `main` directly — `main`
only receives it through the normal `Portal-Gerencial-rev1` → `main` merge process.

**How to run it:**

1. In GitHub Actions, open **Sync PROD Data to TEST**.
2. Click **Run workflow** and set **"Use workflow from"** to **`Portal-Gerencial-rev1`** explicitly — the
   workflow only exists on that branch's definition, and it also **hard-fails** if the resolved
   `github.ref_name` is not `Portal-Gerencial-rev1` (no bypass input exists by design).
3. Fill in the three required inputs:
   - `confirm_restore` = exactly `RESTORE_PROD_TO_TEST`
   - `confirm_no_prod_changes` = exactly `I_UNDERSTAND_PROD_WILL_NOT_BE_MODIFIED`
   - `release_version` = the **current value of `docs/VERSION.md` → "Current Version"** on the branch
     being run (e.g. `v2.208.0`). The workflow reads `docs/VERSION.md` itself and rejects any value that
     doesn't match it exactly (and rejects anything that isn't valid SemVer `vX.Y.Z`), so the input can
     never be silently inferred or substituted.
4. Before validation, a **"Resolve commit metadata"** step determines the commit SHA used for
   traceability. `GITHUB_SHA` (supplied directly by GitHub Actions for the dispatched run) is always
   authoritative and is used as the resolved SHA. This runner does **not** require Git to be installed:
   `actions/checkout@v4` may fall back to downloading the repository as a REST API archive when `git` is
   not in `PATH`, in which case there is no `.git` directory to inspect. When `git` **and** `.git`
   metadata both happen to be available, the step opportunistically runs `git rev-parse HEAD` as an extra
   cross-check and **fails only if it disagrees** with `GITHUB_SHA` — it never fails merely because Git is
   missing (`scripts/db/resolve-sync-commit-metadata.ps1`).
5. All of the above is checked in a dedicated **"Validate inputs, ref and version"** step that runs
   *before* `scripts/db/sync-prod-data-test.ps1` is ever invoked — no backup, restore, or attachment
   operation happens if validation fails.
6. Once validation passes, the job prints safe traceability metadata (workflow ref, `github.ref_name`,
   resolved commit SHA, repository version, `release_version` input, confirmation status, source/target
   database names and application paths). It never prints passwords, connection strings, or secret values.
   The SQL Server instance name is printed later, by the unchanged sync script itself, right before the
   restore step.

**What the sync script still does (unchanged since it was authored on `main`):** backs up TEST
(pre-restore) and PROD (source) to `C:\Apps\AlplaPortal\Test\backups\db`; restores the PROD backup over
`[Portal-Gerencial-Test]` (`SINGLE_USER WITH ROLLBACK IMMEDIATE` → `RESTORE ... WITH REPLACE` →
`MULTI_USER`); remaps the `usr_portalgerencial_test` login; neutralizes `EmailOutbox`, `SmtpSettings`, and
`IntegrationProviders` in TEST so it can never send real emails or call real external integrations;
mirrors PROD attachments into TEST via Robocopy (`/MIR`, exit code `>= 8` fails the job); restores the IIS
TEST app pool at the end.

**Maintenance note:** the workflow currently emits a Node 20 deprecation warning from
`actions/checkout@v4`. It is informational only and unrelated to the synchronization logic — do not
change `actions/checkout` or Node configuration to silence it as part of an unrelated change.

---

## Local Development Database

> [!NOTE]
> This section is for the developer workstation only. It does NOT apply to AOVIA1VMS011 (TEST) or Production.

### Connection String

The local development database is configured in:

```
src/backend/AlplaPortal.Api/appsettings.Development.json
```

Default connection:
```
Server=(localdb)\MSSQLLocalDB;Database=AlplaPortalV1;Trusted_Connection=True;...
```

To verify your local database name, check the `ConnectionStrings.DefaultConnection` value in that file.

> [!CAUTION]
> Never commit changes to `appsettings.Development.json` that contain real passwords, server names, or production connection strings.

### Check Current Local Database State

Before deciding how to update, check if your local database exists and has data:

```powershell
# Check if the database exists
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "SELECT name FROM sys.databases WHERE name = 'AlplaPortalV1'"

# Check migration history (if database exists)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d AlplaPortalV1 -Q "SELECT COUNT(*) AS MigrationCount FROM __EFMigrationsHistory"
```

### Option A: Clean Recreate (Recommended if no important local data)

This is the **simplest and safest** approach. It drops the old database and lets EF Core create everything from scratch using the new consolidated baseline + all subsequent migrations.

```powershell
# Navigate to the API project directory
cd C:\dev\alpla-portal\src\backend\AlplaPortal.Api

# Drop the existing local database
dotnet ef database drop --force

# Recreate from scratch — this runs ConsolidatedBaseline + all subsequent migrations
dotnet ef database update
```

After completion:
- All 29 baseline tables will be created with seed data
- All subsequent migrations will apply in order
- No admin user will exist — create one through the UI or use `ADMIN_USER_SEED_TEMPLATE.sql`

### Option B: Migrate Existing Data (If you have important local data)

If your local database contains test data you want to preserve:

**Step 1: Back up the local database**
```powershell
# Create a backup directory
New-Item -Path "C:\dev\alpla-portal\.tmp\backups" -ItemType Directory -Force

# Back up LocalDB (adjust database name if different)
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "BACKUP DATABASE [AlplaPortalV1] TO DISK = 'C:\dev\alpla-portal\.tmp\backups\AlplaPortalV1_pre_v2.156.0.bak' WITH FORMAT"
```

**Step 2: Register the consolidated baseline**
```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d AlplaPortalV1 -Q "
IF NOT EXISTS (
    SELECT 1 FROM __EFMigrationsHistory
    WHERE MigrationId = '20260225000000_ConsolidatedBaseline'
)
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260225000000_ConsolidatedBaseline', '8.0.2');
    PRINT '[OK] Registered ConsolidatedBaseline migration.';
END
ELSE
BEGIN
    PRINT '[SKIP] ConsolidatedBaseline already registered.';
END
"
```

**Step 3: Run pending migrations**
```powershell
cd C:\dev\alpla-portal\src\backend\AlplaPortal.Api
dotnet ef database update
```

**Step 4: If migration fails, fall back to Option A**
```powershell
dotnet ef database drop --force
dotnet ef database update
```

### Local Post-Migration Validation

After updating the local database (either option), validate the schema:

```powershell
# Run the validation script against your local database
sqlcmd -S "(localdb)\MSSQLLocalDB" -d AlplaPortalV1 -i "C:\dev\alpla-portal\docs\POST_INSTALL_DATABASE_VALIDATION.sql"
```

All critical tables should show `OK`. Seed data counts should meet minimums.

### Local Development Startup Behavior

In Development mode (`ASPNETCORE_ENVIRONMENT=Development`), the application will:
- Run `Database.Migrate()` automatically on startup
- Validate critical tables exist
- **Log a warning** if migration fails (does NOT crash — unlike TEST/PRODUCTION)
- Continue running to allow local debugging

> [!IMPORTANT]
> Since v2.185.9 (DEC-137), `Database.Migrate()` is **only** executed in Development.
> In TEST, Staging, and Production, the application **detects** pending migrations
> and **crashes with a descriptive message** listing each missing migration ID.
> It never attempts DDL operations in non-Development environments.

### Local Quick-Start After v2.156.0

```powershell
# 1. Pull the latest code
git pull origin main

# 2. Recreate local database (recommended)
cd C:\dev\alpla-portal\src\backend\AlplaPortal.Api
dotnet ef database drop --force
dotnet ef database update

# 3. Validate
sqlcmd -S "(localdb)\MSSQLLocalDB" -d AlplaPortalV1 -i "C:\dev\alpla-portal\docs\POST_INSTALL_DATABASE_VALIDATION.sql"

# 4. Start backend
dotnet run

# 5. Start frontend (separate terminal)
cd C:\dev\alpla-portal\src\frontend
npm run dev
```

Backend: http://localhost:5000 | Frontend: http://localhost:5173


## Local Development Runtime Notes (DEC-147)

- **Vite HMR unreliable on the OneDrive/junction tree**: file changes are often not detected by the
  watcher — after frontend changes, **restart the Vite dev server** (port 5173). Do not diagnose stale
  UI without first restarting Vite.
- **`dotnet run` may fail with "Access is denied"** on the apphost `AlplaPortal.Api.exe` (corporate
  execution policy on the OneDrive path). Start the dev backend with:
  `ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5000 dotnet bin/Debug/net8.0/AlplaPortal.Api.dll`
- Official development ports remain **5000 (API)** and **5173 (frontend)**.
