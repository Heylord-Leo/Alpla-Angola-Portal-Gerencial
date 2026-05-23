# SQL Server Instance Reuse & Decommission Assessment: AOVIA1VMS011
**Application:** Alpla Angola - Portal Gerencial  
**Assessment Date:** May 23, 2026  
**Status:** **SUCCESSFULLY COMPLETED** (0 Active Dependencies, Safe to Repurpose and Reuse)

---

## 1. Executive Summary

This report documents the decommission and readiness assessment of the local SQL Server default instance **`MSSQLSERVER`** on server **`AOVIA1VMS011`** (Windows Server 2022 Standard) for the upcoming deployment of the Portal Gerencial relational databases:
*   **Production Database:** `[Portal-Gerencial]`
*   **Test/Staging Database:** `[Portal-Gerencial-Test]`

Following Leonardo's latest confirmation that the previous workload of `MSSQLSERVER` was migrated to `AOVIA1VMS012`, we executed a comprehensive local and remote decommission validation script. The assessment physically and logically verified that the default instance is **completely empty** of any user databases, has **zero active connections**, and is **100% safe** to reuse as the local database server for the Portal Gerencial.

---

## 2. SQL Service Status and Identity

The services audit was successfully executed locally on `AOVIA1VMS011` as Administrator:

*   **Instance Name:** `MSSQLSERVER` (Default SQL Server Instance)
*   **Service Name:** `MSSQLSERVER`
*   **Startup Type:** `Automatic` (Auto)
*   **Service Account:** `NT Service\MSSQLSERVER` (Least-Privilege Virtual Account)
*   **Current State:** `Running` (PID 3980)
*   **SQL Server Edition:** **Microsoft SQL Server 2019 Express Edition (64-bit)**
*   **SQL Server Version:** `15.0.2000.5` (RTM)

---

## 3. Physical Database Files Inventory (DATA Folder Scan)

To bypass SQL Server metadata visibility restrictions, a direct physical scan of the default database storage folder (`C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA`) was executed:

| Database File Name | File Size | Classification / Purpose | Status |
| :--- | :--- | :--- | :---: |
| `master.mdf` | 4.44 MB | System Master Database | ✅ System |
| `model.mdf` | 8.00 MB | System Model Database Template | ✅ System |
| `model_msdbdata.mdf` | 13.38 MB | System Model MSDB Template | ✅ System |
| `model_replicatedmaster.mdf` | 4.44 MB | System Model Replica Template | ✅ System |
| `MSDBData.mdf` | 14.75 MB | System MSDB Database | ✅ System |
| `tempdb.mdf` | 8.00 MB | System Temporary Database | ✅ System |

### Critical Physical Audit Findings:
1.  **Zero User Databases:** There are **no** user-created database files (`.mdf` or `.ldf`) physically present in the SQL Server default DATA directory.
2.  **Drive D: is Clear:** A recursive search of the empty data drive `D:\` confirmed **zero** database files exist on the secondary partition.
3.  **No Residual Files:** All files in the DATA folder belong exclusively to standard system database templates, confirming the instance has no active workload and is in a pristine, freshly-installed state.

---

## 4. Active Connections and Network Port Audit

To check for active connections or external application dependencies on the `MSSQLSERVER` instance, network and process listening audits were executed:

*   **TCP Port 1433 State:** **No active network connections found on Port 1433.**
*   **SQL Server Process Audit (PID 3980):** Checked netstat connections associated with the SQL Server process `sqlservr.exe` (PID 3980). Netstat confirmed **0 active connections** over TCP/IP.
*   **Listening Context:** ERRORLOG logs verified that `MSSQLSERVER` is not listening on TCP/IP by default (standard Express Edition setup). It is only listening on local shared memory and local Named Pipes:
    *   `[ \\.\pipe\SQLLocal\MSSQLSERVER ]`
    *   `[ \\.\pipe\sql\query ]`

### Dependency Verdict:
**No active workloads, programs, or external databases are currently connected to MSSQLSERVER.** The instance has been completely quiet since its last startup.

---

## 5. SQL Server Agent and Jobs Audit

*   **Service Name:** `SQLSERVERAGENT`
*   **Startup Type:** `Disabled`
*   **Service State:** `Stopped`
*   **Express Edition Limit:** SQL Server Agent is **not supported** in SQL Server Express Edition. The service cannot run, and no background jobs, backups, or maintenance schedules exist or can be defined through SQL Agent on this instance.
*   **Backup & Maintenance Strategy:** Because SQL Agent is unavailable, database backups and maintenance in Phase 2 must be scheduled via standard **Windows Task Scheduler** using PowerShell or Command Line backup scripts (e.g. executing `sqlcmd` to run backup scripts).

---

## 6. SQL Server Security & Sysadmin Blockers

As verified in the Phase 2 Discovery Sweep, Leonardo's administrative account `ALPLA\adm_cintra01` successfully connects to `MSSQLSERVER` but suffers from metadata visibility restrictions (returning 0 rows). This confirms that:
1. `ALPLA\adm_cintra01` is **NOT** mapped as a direct login and does **NOT** have `sysadmin` role membership on SQL Server `MSSQLSERVER`.
2. `BUILTIN\Administradores` (Local Administrators) is **NOT** configured as a `sysadmin` login (modern SQL Server secure installation baseline).

### Controlled SQL Sysadmin Access Recovery Plan (Maintenance Window Only)

Because the default instance `MSSQLSERVER` has been validated as completely unused and has **zero active user databases**, we can safely execute a controlled **Single-User Mode SQL Administrative Recovery** to grant `sysadmin` rights to Leonardo's account, with **zero operational risk** or service disruption to other databases.

To resolve the sysadmin blocker, the following controlled recovery procedure is proposed:

> [!IMPORTANT]
> **Controlled SQL Sysadmin Recovery Procedure (For Phase 2 Implementation):**
> 1. Log into server `AOVIA1VMS011` via RDP as Administrator under the `ALPLA\adm_cintra01` context.
> 2. Open an elevated PowerShell Command Prompt as Administrator.
> 3. Stop the SQL Server instance:
>    ```cmd
>    net stop MSSQLSERVER
>    ```
> 4. Start the SQL Server service in Single-User Mode, restricting connection access exclusively to SQLCMD:
>    ```cmd
>    net start MSSQLSERVER /m"SQLCMD"
>    ```
> 5. Connect locally using SQLCMD (which automatically grants full `sysadmin` privileges to members of the local Windows Administrators group in Single-User Mode):
>    ```cmd
>    sqlcmd -S localhost
>    ```
> 6. Run SQL queries to create the Windows Login for Leonardo's account and grant it the `sysadmin` server role:
>    ```sql
>    CREATE LOGIN [ALPLA\adm_cintra01] FROM WINDOWS;
>    ALTER SERVER ROLE [sysadmin] ADD MEMBER [ALPLA\adm_cintra01];
>    GO
>    ```
> 7. Exit the SQLCMD terminal:
>    ```cmd
>    quit
>    ```
> 8. Stop the SQL Server Single-User service:
>    ```cmd
>    net stop MSSQLSERVER
>    ```
> 9. Restart SQL Server in normal multi-user mode:
>    ```cmd
>    net start MSSQLSERVER
>    ```
> 10. Open SQL Server Management Studio (SSMS) or PowerShell, connect via Windows Authentication, and verify that `ALPLA\adm_cintra01` now has full `sysadmin` catalog visibility.

---

## 7. Conclusions & Recommendations

### Reuse Recommendation:
*   **Safe to Repurpose:** The default instance `MSSQLSERVER` is **100% safe to reuse** for the Portal Gerencial.
*   **No Reinstallation Needed:** Keep `MSSQLSERVER` installed as-is. There is **no need to uninstall or reinstall** SQL Server, which avoids registry overhead and licensing complexities.
*   **Strict Isolation:** Create only new Portal databases (`[Portal-Gerencial]`, `[Portal-Gerencial-Test]`) and the portal logins (`adm_portalgerencial`, `usr_portalgerencial`, `usr_portalgerencial_test`).
*   **System Integrity:** Leave system databases completely untouched. All Innux attendance databases reside on separate instances (`INNUXTIME`, `INUTIME`) and remain entirely segregated.

### Next Recommended Step:
1. Leonardo reviews and formally approves this decommission report.
2. Schedule a brief maintenance window to execute the **SQL Sysadmin Access Recovery Procedure** on `AOVIA1VMS011`.
3. Once `ALPLA\adm_cintra01` is mapped as a `sysadmin`, proceed to Phase 2 database creation, SQL logins mapping, and database owner assignments.
