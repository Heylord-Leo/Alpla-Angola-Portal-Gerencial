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
> Migrations must be applied manually using a DBA account **before** deploying the new build.
> The deployment workflow will **block** startup if pending migrations are detected.

> [!WARNING]
> **When adding a new EF Core migration**, the expected migration list must be updated in the **same task/release** in all three locations:
> 1. `scripts/db/check-pending-migrations.ps1` — hardcoded `$expectedMigrations` array
> 2. `.github/workflows/deploy-test.yml` — inline `$expected` array in the "Check for pending EF Core migrations" step
> 3. `.github/workflows/deploy-prod.yml` — inline `$expected` array in the "Check for pending EF Core migrations" step
>
> Failure to update these lists will cause the deployment workflow to report the new migration as "unknown" or miss it during the pending check.
> A future improvement may auto-generate this list from the Migrations folder.

### 1. Check for Pending Migrations

From your development workstation, compare the expected migrations against the target database:

```powershell
# Check TEST
.\scripts\db\check-pending-migrations.ps1 -ConnectionString "Server=AOVIA1VMS011;Database=Portal-Gerencial-Test;User Id=...;Password=...;TrustServerCertificate=True"

# Check PRODUCTION
.\scripts\db\check-pending-migrations.ps1 -ConnectionString "Server=AOVIA1VMS011;Database=Portal-Gerencial;User Id=...;Password=...;TrustServerCertificate=True"
```

If the script outputs `RESULT: PASS`, no migration action is needed.

### 2. Generate Migration SQL Script

If pending migrations are detected:

```powershell
# Generate idempotent SQL script from the last applied migration
dotnet ef migrations script <last-applied-migration-id> -i -o scripts/db/apply-migrations.sql `
  --project src/backend/AlplaPortal.Infrastructure `
  --startup-project src/backend/AlplaPortal.Api
```

### 3. Review and Apply

1. **Back up the target database** using SSMS or `BACKUP DATABASE`.
2. **Review** the generated SQL script for safety.
3. **Apply** the script to the target database using SSMS or `sqlcmd` with a DBA-level account.
4. **Verify** `__EFMigrationsHistory` contains all expected migration IDs.
5. Re-run `check-pending-migrations.ps1` to confirm `RESULT: PASS`.

### 4. Deploy and Verify

1. Run the GitHub Actions deployment workflow.
2. The workflow's "Check for pending EF Core migrations" step will verify the database.
3. If all migrations are applied, the App Pools will start and the smoke test will run.
4. If any migrations are pending, the deployment will fail with a clear error message.

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

