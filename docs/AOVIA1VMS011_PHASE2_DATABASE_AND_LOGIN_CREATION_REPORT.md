# SQL Database & Login Provisioning Report: AOVIA1VMS011
**Application:** Alpla Angola - Portal Gerencial  
**Execution Date:** May 23, 2026, 16:48:00 UTC+1  
**Status:** **SUCCESSFULLY PROVISIONED** (Portal Databases & Logins Created, Isolated Security Mapped)

---

## 1. Executive Summary

This report documents the successful provisioning of the dedicated Portal Gerencial relational databases and secure SQL Authentication logins on the default SQL Server 2019 Express instance **`MSSQLSERVER`** on server **`AOVIA1VMS011`** (Windows Server 2022 Standard).

Using the validated administrative session `ALPLA\adm_cintra01` (promoted to `sysadmin` in `v2.148.0`), the provisioning script was executed locally via PowerShell. All Portal databases, administrator logins, and runtime logins have been safely created and mapped with rigorous database isolation controls in place.

---

## 2. Provisioning Summary

### A. SQL Server Instance Details
*   **Target Server:** `AOVIA1VMS011`
*   **SQL Server Instance:** `MSSQLSERVER` (Default Instance, SQL Server 2019 Express)
*   **Execution Context:** `ALPLA\adm_cintra01` (Windows Authentication, Sysadmin)

### B. Relational Databases Created
*   **Production Database:** **`[Portal-Gerencial]`**
*   **Test/Staging Database:** **`[Portal-Gerencial-Test]`**
*   *Note:* Because the database names contain hyphens, all application configurations, migrations, and scripts strictly utilize bracket notation.

### C. SQL Authentication Logins & Mappings
The script generated and mapped three SQL Authentication logins. To guarantee the highest level of database security:
1.  **`adm_portalgerencial`** (Portal Database Administrator Login):
    *   **Scope:** Assigned as **`db_owner`** on **both** `[Portal-Gerencial]` and `[Portal-Gerencial-Test]`.
    *   **Limits:** Does NOT receive `sysadmin` server-level role membership. Has zero visibility or connection permissions on `INNUX`, `INNUXTIME`, or any unrelated databases.
2.  **`usr_portalgerencial`** (Production Application Runtime Login):
    *   **Scope:** Assigned as **`db_owner`** (temporarily to support Entity Framework Core DDL migrations) on `[Portal-Gerencial]`.
    *   **Limits:** Has **no** database user mapping or access to `[Portal-Gerencial-Test]`, system databases, or unrelated databases.
3.  **`usr_portalgerencial_test`** (Test/Staging Application Runtime Login):
    *   **Scope:** Assigned as **`db_owner`** (temporarily to support Entity Framework Core DDL migrations) on `[Portal-Gerencial-Test]`.
    *   **Limits:** Has **no** database user mapping or access to `[Portal-Gerencial]`, system databases, or unrelated databases.

> [!TIP]
> **Temporary Privilege Recommendation:** The `db_owner` privileges mapped to runtime logins `usr_portalgerencial` and `usr_portalgerencial_test` are required to execute EF migrations during initial deployment. Once the database schema is stabilized in production, it is highly recommended to reduce these permissions to least-privilege runtime roles (`db_datareader`, `db_datawriter`, plus explicit `EXECUTE` rights on stored procedures/functions).

---

## 3. Password Handling & Security Audits

*   **In-Memory Password Generation:** Passwords were generated dynamically during local execution in server memory using a cryptographically secure random pool (`System.Security.Cryptography.RNGCryptoServiceProvider`).
*   **Zero Leakage Policy:** **No** plaintext passwords, security hashes, or credentials have been written to any scripts, logs, reports, or committed to source control.
*   **Password Storage:** The generated credentials were shown exactly once on Leonardo's local PowerShell terminal during execution to allow immediate secure copy-pasting into ALPLA's corporate password vault.
*   **Log Verification:** The report logs contain only the secure placeholder `[REDACTED_SECURE_PASSWORD]`.

---

## 4. Cross-Database Isolation Audit

The provisioning script executed automated security sweep checks to verify strict isolation:
*   **System Databases Isolation:** Verified that `adm_portalgerencial`, `usr_portalgerencial`, and `usr_portalgerencial_test` have **zero** database user mappings in `master`, `model`, `msdb`, or `tempdb`.
*   **Production/Test Segregation:**
    *   Audited that `usr_portalgerencial` has **no user mapped** in `[Portal-Gerencial-Test]` (cross-database check: *Isolated*).
    *   Audited that `usr_portalgerencial_test` has **no user mapped** in `[Portal-Gerencial]` (cross-database check: *Isolated*).
*   **Attendance Database Segregation:** Verified that no Portal SQL logins have any visibility or access to existing Innux databases (`INNUX`, `INNUXTIME`, or `INUTIME`).

---

## 5. SQL Express Backup Strategy (SQL Agent Limitation)

Because the default instance on `AOVIA1VMS011` is **SQL Server 2019 Express Edition**, the SQL Server Agent (`SQLSERVERAGENT`) is unavailable and cannot be run. 

To satisfy the backup requirements without SQL Agent, a robust automated backup strategy using **Windows Task Scheduler** and **PowerShell/SQLCMD** is recommended:

### Automated Backup Blueprint
1.  **Backup Script Location:** Save a standard SQL backup script at `D:\PortalGerencial\Backups\Scripts\BackupPortalDbs.sql`:
    ```sql
    DECLARE @BackupFile NVARCHAR(500)
    SET @BackupFile = 'D:\PortalGerencial\Backups\Portal-Gerencial_' + FORMAT(GETDATE(), 'yyyyMMdd_HHmmss') + '.bak'
    BACKUP DATABASE [Portal-Gerencial] TO DISK = @BackupFile WITH INIT, COMPRESSION, STATS = 10;
    GO
    ```
2.  **PowerShell Wrapper:** Save a PowerShell wrapper script at `D:\PortalGerencial\Backups\Scripts\RunBackup.ps1` to execute the SQL backup and manage retention (e.g. deleting backups older than 30 days):
    ```powershell
    $BackupDir = "D:\PortalGerencial\Backups"
    sqlcmd -S localhost -E -i "$BackupDir\Scripts\BackupPortalDbs.sql"
    
    # Retention cleanup (30 days)
    Get-ChildItem -Path $BackupDir -Filter *.bak | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force
    ```
3.  **Windows Task Scheduler:** Configure a Daily Task in Windows Task Scheduler on `AOVIA1VMS011`:
    *   **Account:** Run under `NT AUTHORITY\SYSTEM` or a dedicated IT service account with local admin privileges.
    *   **Trigger:** Daily at 02:00 AM.
    *   **Action:** Start a program.
        *   *Program/Script:* `powershell.exe`
        *   *Arguments:* `-NoProfile -ExecutionPolicy Bypass -File "D:\PortalGerencial\Backups\Scripts\RunBackup.ps1"`

---

## 6. Conclusions & Next Steps

### Current Status:
The Phase 2 database and login preparation is **successfully completed**. The instance is fully ready to host the Portal Gerencial applications.

### Remaining Next Step:
Proceed to **Phase 3**:
1. Prepare the IIS packages and compile backend binaries.
2. Store the generated SQL login credentials in the secure IIS Environment Variables (`D:\PortalGerencial\Api\web.config` or machine-level environment variables) under keys:
   *   `ConnectionStrings__PortalDatabase`
   *   `ConnectionStrings__PortalTestDatabase`
3. Deploy binaries to `D:\PortalGerencial\Api` and `D:\PortalGerencial-Test\Api`.
4. Launch the application and execute the initial Entity Framework Core migrations to construct the Portal database schemas.
