# Rollback Procedure — PRODUCTION

**Environment:** PRODUCTION  
**Server:** AOVIA1VMS011  
**API Port:** 5002  
**Database:** `[Portal-Gerencial]`

---

## When to Rollback

Use this procedure when a Production deployment causes:
- Application crash (App Pool stops repeatedly)
- Critical runtime errors (500 errors on core endpoints)
- Database migration failure (application won't start)
- Data corruption detected post-deployment
- Business-critical feature regression

---

## Prerequisites

- RDP access to AOVIA1VMS011 with Administrator privileges
- Knowledge of the backup timestamp to restore from

---

## Rollback Procedures

### Procedure A: Application-Only Rollback (No DB Changes)

Use when the deployment did not include database migrations, or when the database is fine but the application has issues.

1. **Identify the backup to restore:**
   ```powershell
   Get-ChildItem "C:\Apps\AlplaPortal\Prod\backups" | Sort-Object LastWriteTime -Descending
   ```

2. **Stop Production App Pools:**
   ```powershell
   Import-Module WebAdministration
   Stop-WebAppPool -Name "AlplaPortal-Prod-Api-Pool"
   Stop-WebAppPool -Name "AlplaPortal-Prod-Web-Pool"
   Start-Sleep -Seconds 5
   ```

3. **Restore API files:**
   ```powershell
   $backupDir = "C:\Apps\AlplaPortal\Prod\backups\backup_YYYYMMDD_HHMMSS"
   
   # Preserve config files before restore
   $configBackup = "$env:TEMP\rollback_config"
   New-Item -ItemType Directory -Force -Path $configBackup | Out-Null
   Copy-Item "C:\Apps\AlplaPortal\Prod\api\appsettings.Production.json" $configBackup -Force -ErrorAction SilentlyContinue
   
   # Restore API from backup
   robocopy "$backupDir\api" "C:\Apps\AlplaPortal\Prod\api" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP
   
   # Restore preserved config
   Copy-Item "$configBackup\appsettings.Production.json" "C:\Apps\AlplaPortal\Prod\api\" -Force -ErrorAction SilentlyContinue
   ```

4. **Restore Web files:**
   ```powershell
   # Preserve Production web.config (port 5002)
   $webConfigBackup = "$env:TEMP\rollback_webconfig"
   New-Item -ItemType Directory -Force -Path $webConfigBackup | Out-Null
   Copy-Item "C:\Apps\AlplaPortal\Prod\web\web.config" $webConfigBackup -Force -ErrorAction SilentlyContinue
   
   # Restore Web from backup
   robocopy "$backupDir\web" "C:\Apps\AlplaPortal\Prod\web" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP
   
   # Restore preserved web.config
   Copy-Item "$webConfigBackup\web.config" "C:\Apps\AlplaPortal\Prod\web\" -Force -ErrorAction SilentlyContinue
   ```

5. **Start Production App Pools:**
   ```powershell
   Start-WebAppPool -Name "AlplaPortal-Prod-Api-Pool"
   Start-WebAppPool -Name "AlplaPortal-Prod-Web-Pool"
   ```

6. **Validate:**
   ```powershell
   # Check pools are running
   Get-WebAppPoolState -Name "AlplaPortal-Prod-Api-Pool"
   Get-WebAppPoolState -Name "AlplaPortal-Prod-Web-Pool"
   
   # Check API health
   Invoke-WebRequest -Uri "http://localhost:5002/health" -UseBasicParsing
   
   # Check port listener
   Get-NetTCPConnection -LocalPort 5002 -State Listen
   ```

---

### Procedure B: Full Rollback (Application + Database)

Use when a database migration corrupted data or schema, and you need to restore the database to its pre-deployment state.

> **CAUTION:** Restoring the database will lose any data created after the backup (new orders, user changes, etc.). Use only as a last resort.

1. **Stop Production App Pools** (same as Procedure A, step 2)

2. **Identify the database backup:**
   ```powershell
   Get-ChildItem "C:\Apps\AlplaPortal\Prod\backups\db" | Sort-Object LastWriteTime -Descending
   ```

3. **Restore the database:**
   ```sql
   -- Run in SSMS or sqlcmd against the SQL Server instance
   -- First, close all connections to the database
   USE master;
   ALTER DATABASE [Portal-Gerencial] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
   
   -- Restore from backup (adjust path and filename)
   RESTORE DATABASE [Portal-Gerencial]
   FROM DISK = 'C:\Apps\AlplaPortal\Prod\backups\db\Portal-Gerencial_YYYYMMDD_HHMMSS.bak'
   WITH REPLACE;
   
   -- Return to multi-user mode
   ALTER DATABASE [Portal-Gerencial] SET MULTI_USER;
   ```

4. **Restore application files** (same as Procedure A, steps 3-4)

5. **Start Production App Pools** (same as Procedure A, step 5)

6. **Validate** (same as Procedure A, step 6)

---

## Post-Rollback Actions

| # | Action |
|:---:|:---|
| 1 | Document the rollback in `CHANGELOG.md` |
| 2 | Investigate the root cause of the failure |
| 3 | If the failure was a migration issue, test the migration on Test first |
| 4 | If the failure was a code issue, fix and redeploy to Test before Production |
| 5 | Verify Test environment was not impacted by the rollback |

---

## Critical Safety Rules

- **Never** restore Test files to Production or vice versa.
- **Never** restore `[Portal-Gerencial-Test]` backup to `[Portal-Gerencial]`.
- Always preserve `appsettings.Production.json` during rollback.
- Always preserve the Production `web.config` (port 5002).
- After database restore, the next application startup will re-apply migrations. If the migration itself was the problem, you need to fix the migration code first.
