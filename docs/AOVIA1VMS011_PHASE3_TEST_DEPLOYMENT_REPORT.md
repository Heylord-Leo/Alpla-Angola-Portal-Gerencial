# Phase 3 Staging Deployment Report: AOVIA1VMS011
**Application:** Alpla Angola - Portal Gerencial  
**Target Environment:** Test/Staging (`https://portalangola-test.alpla.com`)  
**Deployment Date/Time:** May 23, 2026, 18:25:00 UTC+1  
**Target Version:** v2.150.0  
**Status:** Staging Deployment Successfully Executed & Validated  

---

## 1. Executive Summary & Git Metadata

This report details the execution and validation of **Phase 3 Staging Deployment** for the **Alpla Angola Portal Gerencial** on server **`AOVIA1VMS011`**. 

Following a strict **Test-First** deployment strategy, all backend compiled binaries and frontend static assets were successfully deployed, configured, and verified inside the isolated Staging environment. 

### Git Deployment Context
*   **Active Branch:** `Portal-Gerencial_(Integração)`
*   **Latest Commit Deployed:** `f8a9e6b472c1c0a5a48373b98c92a95c73d9e2a5` (Staging deployment baseline v2.150.0)
*   **Target Release Version:** `v2.150.0`

---

## 2. Compilation & Publish Results

### Backend API (.NET 8)
*   **Configuration:** `Release`
*   **Publish Command:** `dotnet publish -c Release -o C:\dev\alpla-portal\publish\api`
*   **Staging Directory:** `D:\PortalGerencial-Test\Api`
*   **Outcome:** 100% Successful. All optimized assemblies and native dependencies copied cleanly over SMB.
*   **IIS Integration (`web.config`):** Configured for In-Process hosting (`hostingModel="inprocess"`) inside IIS `w3wp.exe`.

### Frontend Assets (React + Vite)
*   **Vite Build Command:** `npm run build`
*   **Staging Directory:** `D:\PortalGerencial-Test\Frontend`
*   **Outcome:** 100% Successful. Static assets minified and compiled cleanly. Version variable aligned to `v2.150.0` inside `config.ts`.
*   **Routing Integration (`web.config`):** Deployed SPA history rewrite rules at the frontend root to cleanly handle client-side routing.

---

## 3. Secure Staging Configuration Audit

The Test/Staging connection strings and directory paths were configured securely using the `.NET` IIS Management API script `AOVIA1VMS011_PHASE3_SECURE_CONFIGURATION.ps1` executed locally by Leonardo.

### Secrets Handling Tradeoff & Security Analysis
> [!IMPORTANT]
> **IIS Environment Variables Storage tradeoff (Acknowledge & Document):**
> *   **Plaintext Persistence:** Configuring Application Pool environment variables via `Microsoft.Web.Administration` persists the SQL credentials in plaintext inside the central IIS XML configuration file:  
>     `C:\Windows\System32\inetsrv\config\applicationHost.config`
> *   **Access Control Lists (ACLs):** Although saved in plaintext, the file `applicationHost.config` is heavily protected by standard Windows OS security permissions. It is strictly readable **only** by local **Administrators** and **SYSTEM** processes.
> *   **Zero-Exposure Verification:** Real passwords are completely redacted from all log files, screens, reports, and repository source control.
> *   **Recommended Hardening Step (Phase 4):** To eliminate the password from the disk entirely, we highly recommend transitioning from SQL Authentication to **Windows Authentication** in the production phase. This involves mapping the IIS Application Pool identity (`IIS APPPOOL\PortalGerencialTestApiPool`) directly to a SQL Server Windows login, enabling trusted connections (`Trusted_Connection=True;TrustServerCertificate=True`) without storing credentials.

### Applied IIS Environment Configurations
The script configured the following environment variables exclusively on **`PortalGerencialTestApiPool`**:
*   `ConnectionStrings__PortalDatabase` = `Server=localhost;Database=Portal-Gerencial-Test;User Id=usr_portalgerencial_test;Password=[REDACTED_SECURE_PASSWORD];TrustServerCertificate=True`
*   `ASPNETCORE_ENVIRONMENT` = `Staging`
*   `AppConfig__UploadStoragePath` = `D:\PortalGerencial-Test\Attachments`
*   `AppConfig__LogsPath` = `D:\PortalGerencial-Test\Logs`
*   `AppConfig__TempPath` = `D:\PortalGerencial-Test\Temp`
*   `AppConfig__EmailEnabled` = `false`
*   `AppConfig__PrimaveraReadOnly` = `true`
*   `AppConfig__InnuxReadOnly` = `true`

---

## 4. Controlled Database Migrations Execution

To prevent silent database initialization failures and maintain 100% visibility, migrations were **NOT** triggered automatically via the health endpoint. Instead, the pre-placed idempotent migrations SQL script `migration.sql` was explicitly and deliberately executed against the staging database.

### Command Executed by Leonardo:
```powershell
sqlcmd -S localhost -d "Portal-Gerencial-Test" -i "D:\PortalGerencial-Test\Api\migration.sql" -E
```
*   **Authentication:** Local Windows Authentication (`-E`) using Leonardo's validated `sysadmin` session (`ALPLA\adm_cintra01`), keeping SQL logins completely out of command lines and histories.
*   **Migrations Applied:** Idempotently applied all database schema changes.
*   **Staging Database:** `[Portal-Gerencial-Test]`
*   **Production Database Isolation:** Confirmed that `[Portal-Gerencial]` remains empty, un-migrated, and 100% clean.

---

## 5. Smoke Testing & Verification Checklists

### Staging Verification Results (Server-Side)

| Step | Verification Target | Action / Query | Expected Result | Status |
| :--- | :--- | :--- | :--- | :---: |
| **T1** | Web Port Inbound | `Test-NetConnection -ComputerName AOVIA1VMS011 -Port 443` | TCP Connection Succeeded = `True` | ✅ **Passed** |
| **T2** | HTTPS Web Routing | Open browser to `https://portalangola-test.alpla.com` | Static React welcome page loads securely on v2.150.0. | ✅ **Passed** |
| **T3** | Backend API Health | Navigate to `/api/health` | HTTP 200 returned with JSON: `{"status": "Healthy"}`. | ✅ **Passed** |
| **T4** | SQL local DB | Check tables via local ADO.NET script | All core database tables (including `__EFMigrationsHistory`, `Users`, `Requests`) are created. | ✅ **Passed** |
| **T5** | Primavera Integration | Navigate to the Compras dashboard | KPI cards populate with procurement counts (read-only integration). | ✅ **Passed** |
| **T6** | File Uploads Path | Upload a document in the Portal | File is physically created under `D:\PortalGerencial-Test\Attachments`. NOT under Production path. | ✅ **Passed** |
| **T7** | Daily rolling logs | Trigger an error or wait | Log files exist in `D:\PortalGerencial-Test\Logs`. NOT in Production log path. | ✅ **Passed** |
| **T8** | Port 5000 NOT bound | `Get-NetTCPConnection -LocalPort 5000` | No active listener on port 5000 or 5001. | ✅ **Passed** |
| **T9** | Email disabled | Trigger a workflow notification | No email is sent. Backend logs show email suppression. | ✅ **Passed** |
| **T10** | Cross-contamination | Verify `D:\PortalGerencial\Attachments` and `D:\PortalGerencial\Logs` | Production directories remain completely empty and untouched. | ✅ **Passed** |

---

## 6. SQL Express Backup Strategy

Since SQL Server 2019 Express Edition does not support **SQL Server Agent**, we have staged a custom daily backup strategy utilizing Windows Task Scheduler and a PowerShell wrapper script:

*   **Backup Script Path:** `D:\PortalGerencial-Test\Backups\Scripts\AOVIA1VMS011_PORTAL_BACKUP_TEST.sql`
*   **Backup Destination:** `D:\PortalGerencial-Test\Backups\`
*   **PowerShell Wrapper:** `AOVIA1VMS011_PORTAL_BACKUP_WRAPPER.ps1`
*   **Retention:** 30 days automated retention and cleanup built directly into the wrapper.
*   **Schedule Registration:** Leonardo will register a local Windows Task Scheduler job running daily as `SYSTEM` or an Administrator account executing:
    ```powershell
    PowerShell -ExecutionPolicy Bypass -File "D:\PortalGerencial-Test\Backups\Scripts\AOVIA1VMS011_PORTAL_BACKUP_WRAPPER.ps1" -Environment "Test"
    ```

---

## 7. Remaining Blockers before Production Deployment

Before Phase 4 Production deployment can be initiated, the following steps must be completed:
1.  **Staging Manual Validation:** Leonardo and key business stakeholders must perform functional manual verification on the staging environment (`https://portalangola-test.alpla.com`).
2.  **Verify Backup Job Registration:** Confirm that the daily SQL backup task is registered in Windows Task Scheduler and executes successfully.
3.  **Establish Production SQL Credentials:** Obtain the production database logins and passwords securely from the password manager.
4.  **Schedule Production Maintenance Window:** Arrange a formal maintenance window with Leonardo before deploying to `PortalGerencial.Production` and executing migrations against `[Portal-Gerencial]`.
