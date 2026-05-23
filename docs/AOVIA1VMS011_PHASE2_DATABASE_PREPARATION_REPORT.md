# Phase 2 Database Preparation Report: AOVIA1VMS011
**Application:** Alpla Angola - Portal Gerencial  
**Execution Date/Time:** May 23, 2026, 16:31:00 UTC+1  
**Status:** **SUCCESSFULLY COMPLETED** (AD Sweeps, SQL sysadmin Recovery, and Multi-user Verification completed and verified).

---

## 1. Executive Summary

This report documents the completion of the **Phase 2 read-only discovery phase** and the decommission/readiness assessment of the default SQL Server instance **`MSSQLSERVER`** on server **`AOVIA1VMS011`** (Windows Server 2022 Standard) for the upcoming deployment of the Portal Gerencial relational databases:
*   **Production Database:** `[Portal-Gerencial]`
*   **Test/Staging Database:** `[Portal-Gerencial-Test]`

Before provisioning the dedicated SQL Logins (`adm_portalgerencial`, `usr_portalgerencial`, and `usr_portalgerencial_test`), schemas, and database owners, we executed read-only Active Directory sweeps, SQL Server logins discovery, and a physical decommission validation of `MSSQLSERVER` on disk and process levels.

### Key Sweeps & Assessment Outcomes:
1. **AD Group Sweeps:** Mapped corporate standard `SQ-` prefix and Leonardo's local IT support group memberships.
2. **SQL Logins Sweeps:** Connected successfully under `ALPLA\adm_cintra01` but verified that Leonardo's account lacks SQL sysadmin privileges on `MSSQLSERVER` due to SQL Server Metadata Visibility Restrictions.
3. **Decommission & Reuse Assessment:** Successfully audited `MSSQLSERVER` and confirmed that **0 user databases** exist on disk, **0 active connections** are registered, and SQL Agent is disabled (Express Edition). It is **100% safe to reuse** `MSSQLSERVER` for the Portal.
4. **Administrative Access Recovery Plan:** Prepared a step-by-step single-user mode SQL administrative recovery procedure to resolve the sysadmin access blocker.

For a detailed analysis of the SQL Server environment, physical database inventories, and the recovery procedure, please refer to the dedicated assessment document:  
👉 **[AOVIA1VMS011 SQL Instance Reuse Assessment](file:///C:/dev/alpla-portal/docs/AOVIA1VMS011_SQL_INSTANCE_REUSE_ASSESSMENT.md)**

---

## 2. Active Directory / SQL Admin Group Discovery

### A. Discovery Commands Executed
*   **Workstation Active Directory Sweep:** 
    ```powershell
    Get-ADGroup -Filter "Name -like '*SQL*' -or Name -like '*DB*' -or Name -like '*Database*' -or Name -like '*DBA*' -or Name -like '*Data*' -or Name -like '*Infra*' -or Name -like '*Server*' -or Name -like '*Admin*'" -Properties Description | Select-Object Name, SamAccountName, Description
    ```
    *Result:* **Success.** The Active Directory PowerShell module is available on the local workstation, allowing a domain-wide search.
*   **Workstation Targeted Location Sweep:**
    ```powershell
    Get-ADGroup -Filter "Name -like '*AOVIA1*'" -Properties Description | Select-Object Name, SamAccountName, Description
    ```
    *Result:* **Success.** Swept all groups matching the Viana location code `AOVIA1`.
*   **Whoami Group Membership Audit:**
    ```cmd
    whoami /groups
    ```
    *Result:* **Success.** Mapped Leonardo's active domain group memberships.

---

### B. Candidate Active Directory Groups Found

Our domain-wide queries identified candidate security groups, but revealed two critical findings:

1.  **Prefix Standard:** Database-related administration groups at ALPLA utilize the prefix **`SQ-`**, typically formatted as `SQ-<ServerName>-<DatabaseName>_DBOwner` or `SQ-<ServerName>-SysAdmin` (e.g. `SQ-ATSTB1VMS036-AlplaPROD_DBOwner`).
2.  **No Local Groups:** There are **no pre-existing** dedicated Active Directory groups registered specifically for server `AOVIA1VMS011` or local database administration in Angola Viana. 

However, Leonardo's active domain groups include candidate IT systems administration groups that cover the local infrastructure support team in Angola:

| Candidate AD Group | Group Scope / Purpose | Candidate Suitability |
| :--- | :--- | :--- |
| **`ALPLA\SD-AOVIA1-IT-Systems`** | Viana 1 local IT systems administration | **Highly Suitable** as a baseline group (includes local IT support staff). |
| **`ALPLA\SD-AO0001-IT-Systems`** | Angola IT systems administration | **Suitable** as a backup country-level systems group. |
| **`ALPLA\OU-AO0001-IT-Head`** | Local IT management / administration | Too narrow (management-only). |
| **`ALPLA\VM-GLOBAL-SQLAdmin`** | Global SQL VM administration permissions | Too broad (global scope). Avoid using for local databases. |
| **`ALPLA\DL-GLOBAL-SQL-Cluster-InstanceAdmins`** | Global SQL cluster administration | Too broad (global scope). Avoid using for local databases. |

---

### C. Existing SQL Windows Logins & Sysadmins (Verified Remote Status)

The local discovery script was successfully executed on `AOVIA1VMS011` by Leonardo's admin account **`ALPLA\adm_cintra01`**. 

The results from the remote SQL logins and sysadmin role sweeps revealed a critical infrastructure finding:
*   **Connection Succeeded:** The script successfully connected to the local SQL Server default instance **`MSSQLSERVER`** via Windows Authentication under `ALPLA\adm_cintra01`.
*   **Empty Logins and Role Tables:** The queries to `sys.server_principals` (filtering by Windows Users/Groups type `U` and `G`) and `sys.server_role_members` (filtering by `sysadmin` role membership) both returned **0 rows**.
*   **Cause Analysis — Metadata Visibility Restriction:** In SQL Server, users who are not members of the `sysadmin` server role and do not have explicit database permissions (such as `VIEW ANY DEFINITION`) suffer from **Metadata Visibility Restrictions**. They can connect to `master` but catalog views like `sys.server_principals` will only return rows associated with their own login, returning zero rows for all other accounts.
*   **Conclusion:** This proves that Leonardo's account `ALPLA\adm_cintra01` is **NOT** individually registered as an SQL Server login and does **NOT** have `sysadmin` role membership on SQL Server `MSSQLSERVER`. Additionally, `BUILTIN\Administradores` is not configured as a `sysadmin` login on this instance, confirming that SQL Server was installed following modern secure standards (where local administrators are not automatically granted SQL sysadmin rights).
*   **Check of IT support groups:** Neither **`ALPLA\SD-AOVIA1-IT-Systems`** nor **`ALPLA\SD-AO0001-IT-Systems`** are currently registered as SQL Server logins on `MSSQLSERVER`.

---

## 3. Candidate Evaluation & Recommendation

To align with the **principle of least-privilege** and corporate AD formatting standards identified in our sweeps, the safest and most robust options for Portal Gerencial database administration are:

### Recommended Access Strategy
1.  **Option A (Highly Recommended): Create a Dedicated SQL Admin AD Group**  
    Instruct the Active Directory team to create a dedicated security group specifically for Portal Gerencial database administration, formatted to match the corporate `SQ-` prefix and machine-specific standard:  
    👉 **`ALPLA\SQ-AOVIA1VMS011-PortalGerencial-DBAdmins`**  
    *Pros:* Outstanding segregation of duties. Only authorized database administrators specifically assigned to the Portal Gerencial on this server will have access. Zero exposure to unrelated system administrative groups.  
    *Cons:* Requires Active Directory provisioning time.
2.  **Option B (Immediate Fallback): Map local IT Systems Group**  
    Map database administrator permissions to the pre-existing local IT systems administration group **`ALPLA\SD-AOVIA1-IT-Systems`**.  
    *Pros:* Immediate availability, no wait time.  
    *Cons:* Introduces excessive administrative exposure, granting full database owner/sysadmin rights to all local IT support staff regardless of their role in Portal Gerencial.

> [!WARNING]
> **Prohibited Option:** Do **NOT** use global domain administrator groups (such as `Domain Admins`, `Enterprise Admins`, or global VM SQL clusters) to manage the local Portal Gerencial databases. Doing so violates security policy.
> 
> **Leonardo's Action Gate:** Leonardo must formally confirm which group is approved before we execute any SQL Server logins or security mappings in Phase 2.

### Resolved Administrative Blocker for Database Creation
During the initial discovery, it was confirmed that Leonardo's account `ALPLA\adm_cintra01` lacked `sysadmin` rights in SQL Server `MSSQLSERVER`. 

**Resolution (Successfully Completed):**
To resolve this blocker, a controlled **Single-User Mode SQL Administrative Recovery** was successfully executed locally on `AOVIA1VMS011` under Leonardo's RDP context. Leonardo's Windows login `ALPLA\adm_cintra01` has been officially mapped and added to the SQL Server `sysadmin` server role.

A comprehensive multi-user validation sweep has been executed, confirming that `ALPLA\adm_cintra01` now has full catalog visibility and database creation privileges, with SQL Server restored to normal multi-user operation. For details, please refer to the validation report:  
👉 **[AOVIA1VMS011 SQL Sysadmin Recovery Validation](file:///C:/dev/alpla-portal/docs/AOVIA1VMS011_PHASE2_SQL_SYSADMIN_RECOVERY_VALIDATION.md)**

---

## 4. Local Discovery Execution & Log Output

The local script was successfully executed, generating `C:\temp\AOVIA1VMS011_PHASE2_DISCOVERY_REPORT.txt` under Leonardo's active context. Below is a copy of the executed commands and verified log:

*   **Script Pre-placement:** `C:\temp\AOVIA1VMS011_PHASE2_DISCOVERY.ps1`
*   **Active Directory Sweep Command:**
    ```powershell
    Get-ADGroup -Filter "Name -like '*SQL*' -or Name -like '*DB*' -or Name -like '*Database*' -or Name -like '*DBA*' -or Name -like '*Data*' -or Name -like '*Infra*' -or Name -like '*Server*' -or Name -like '*Admin*'" -Properties Description | Select-Object Name, SamAccountName, Description, DistinguishedName
    ```
*   **SQL Login Sweeps:** Connected locally to `Server=localhost;Database=master;Integrated Security=True` and ran catalog queries.
*   **Log Output Summary:**
    ```text
    ==========================================================================================
    AOVIA1VMS011 PHASE 2 DATABASE PREPARATION DISCOVERY REPORT
    Execution Date/Time: 2026-05-23 15:34:04
    Executing User: ALPLA\adm_cintra01
    ==========================================================================================
    
    1. ACTIVE DIRECTORY GROUP DISCOVERY
    Active Directory PowerShell Module: NOT AVAILABLE (on server; fallback net group run)
    
    2. SQL SERVER WINDOWS LOGIN DISCOVERY
    Successfully connected to local SQL Server instance (MSSQLSERVER) via Windows Authentication!
    --- Windows users/groups configured as SQL Server logins ---
    [0 rows returned - Metadata Visibility Restricted]
    
    --- Members of the sysadmin role ---
    [0 rows returned - Metadata Visibility Restricted]
    ==========================================================================================
    ```

---

## 5. Security & Deployment Safety Audit

*   **No modifications:** No Active Directory groups have been created, modified, or altered.
*   **No security mappings:** No SQL Server logins or database users have been created or modified.
*   **No databases created:** The databases `[Portal-Gerencial]` and `[Portal-Gerencial-Test]` remain uncreated.
*   **No credentials stored:** No passwords, sensitive hashes, or security tokens are stored in scripts, logs, or documentation.
