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

## Deployment Steps

1. **Stop the IIS Application Pool** for the target site
2. **Deploy the new build artifacts** to the target directory on the server
3. **Start the IIS Application Pool**
4. **Monitor startup logs** — the application will:
   - Run `Database.Migrate()` automatically
   - Validate critical table existence post-migration
   - **Crash on failure** in TEST/PRODUCTION (by design — prevents ghost migration issues)
5. **If startup fails:**
   - Check the console/event log for `[STARTUP] CRITICAL:` messages
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
2. Deploy the new build. Only new migrations (after the last recorded one) will be applied.
3. If the baseline is NOT registered before startup, EF Core will try to recreate existing tables and **crash** (by design in TEST/PRODUCTION).

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

This means you can start the backend with `dotnet run` and it will attempt to apply migrations automatically. However, if your local database is severely out of sync, manually running `dotnet ef database update` first gives better error messages.

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

