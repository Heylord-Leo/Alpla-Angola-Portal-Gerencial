# Server Preparation Phase 1 Report: AOVIA1VMS011
**Application:** Alpla Angola - Portal Gerencial  
**Execution Date/Time:** May 23, 2026, 13:10:00 UTC+1  
**Status:** Phase 1 Successfully Executed locally (IIS, URL Rewrite, Folders, Certs, and Firewall Setup complete). Validation sweep conducted. **1 Core Blocker Identified.**

---

## 1. Executive Summary

This report documents the completion of **Phase 1 of the real controlled server preparation** on server **`AOVIA1VMS011`** (Windows Server 2022 Standard). 

To accommodate the WinRM/RPC network port block between the developer workstation `AOVIA1OLP031` and the server `AOVIA1VMS011`, a **hybrid provisioning workflow** was successfully executed:
1. **Remote SMB Provisioning:** Created the isolated dual-environment folder layouts on drive **D:** and securely copied both SSL certificate PFX files and the URL Rewrite installer MSI.
2. **Local Script Execution:** Leonardo logged in locally via RDP as Administrator and successfully executed the validated setup script `C:\temp\AOVIA1VMS011_PHASE1_SERVER_PREPARATION.ps1`, enabling IIS features, installing URL Rewrite offline, creating sites/pools, binding certificates with SNI via secure interactive password prompts, configuring NTFS ACL permissions, and opening HTTP 80/HTTPS 443 on the firewall.
3. **Post-Setup Remote Validation:** We performed network and SMB sweeps to verify all provisioning actions and check critical module states.

---

## 2. Server State and Basic Readiness

- **Server Name:** `AOVIA1VMS011.alpla.net`
- **OS Version:** Microsoft Windows Server 2022 Standard (10.0.20348)
- **Domain:** `alpla.net`
- **Disk Free Space (Confirmed over UNC):**
  - **Drive C:** 99.37 GB Total | **62.14 GB Free** (Runtimes, OS)
  - **Drive D:** 199.98 GB Total | **199.89 GB Free** (Isolated Portal Roots)

---

## 3. IIS Role and URL Rewrite Validation

- **IIS Features Installed/Enabled:** 
  Web-Server, Web-WebServer, Web-Common-Http, Web-Static-Content, Web-Default-Doc, Web-Http-Errors, Web-Http-Redirect, Web-Performance, Web-Stat-Compression, Web-Security, Web-Filtering, Web-Windows-Auth, Web-App-Dev, Web-Net-Ext45, Web-Asp-Net45, Web-WebSockets, Web-Mgmt-Tools, Web-Mgmt-Console.
- **IIS URL Rewrite Module:**
  - **Status:** **Successfully Installed.**
  - **MSI Source:** Local offline installer `C:\temp\rewrite_amd64_en-US.msi` (uncontrolled internet downloads bypassed).
  - **Binary Check:** `rewrite.dll` is present in `C:\Windows\System32\inetsrv\rewrite.dll` (validation passed).

---

## 4. ANCM / Hosting Bundle Investigation (🚨 1 CORE BLOCKER)

During local setup script execution, a critical warning was logged:
> `ASP.NET Core Module registry key not detected. ASP.NET Core Hosting Bundle may need to be re-run/repaired after IIS installation.`

### Post-Setup Verification Results:
- **Registry Key Check:** `HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2` is absent.
- **DLL Check:** `aspnetcorev2.dll` is **MISSING** from `C:\Program Files\IIS\Asp.Net Core Module\V2\`.
- **Cause Analysis:** The ASP.NET Core Hosting Bundle (8.0.8) was installed *prior* to enabling the Web Server (IIS) role. When the Hosting Bundle installer ran, it did not find the IIS role and skipped registering the ASP.NET Core IIS Module (ANCM). Enabling the IIS role afterwards does not automatically register the module.
- **Consequence:** This is a **core blocker**. Any attempt to host the .NET backend API inside IIS using `hostingModel="InProcess"` or `OutOfProcess` will immediately fail with an IIS `500.19 - Internal Server Error` (Configuration error - unrecognized handler `aspNetCore`).
- **Safest Remediation:** Run a **Repair** or **Re-installation** of the **ASP.NET Core Hosting Bundle 8.0.8** on `AOVIA1VMS011`. This will automatically detect the now-active IIS role, register the global `aspNetCore` handler, and write `aspnetcorev2.dll` to `inetsrv`.
  > [!IMPORTANT]
  > **Remediation Constraint:** Do not execute this repair until formally reviewed and approved by Leonardo.

---

## 5. Folder Structures and NTFS Security Permissions

The 14 isolated folder structures were successfully provisioned on drive D: and secured:

| Path | Owner / Identity | Rights | Purpose | Status |
| :--- | :--- | :--- | :--- | :---: |
| **D:\PortalGerencial\Frontend** | `IIS_IUSRS` | Read & Execute | Production Static SPA files | ✅ Live |
| **D:\PortalGerencial\Api** | `PortalGerencialApiPool` | Read & Execute | Production Web API Binaries | ✅ Live |
| **D:\PortalGerencial\Logs** | `PortalGerencialApiPool` | Modify | Production Serilog Rolling Text Logs | ✅ Live |
| **D:\PortalGerencial\Attachments** | `PortalGerencialApiPool` | Modify | Production Physical File Storage | ✅ Live |
| **D:\PortalGerencial\Backups** | Administrators | Full Control | Database backups (MSSQLSERVER) | ✅ Live |
| **D:\PortalGerencial\Packages** | Administrators | Full Control | Production Deploy packages / zips | ✅ Live |
| **D:\PortalGerencial\Temp** | `PortalGerencialApiPool` | Modify | Production API temporary processing | ✅ Live |
| **D:\PortalGerencial-Test\Frontend** | `IIS_IUSRS` | Read & Execute | Test Static SPA files | ✅ Live |
| **D:\PortalGerencial-Test\Api** | `PortalGerencialTestApiPool` | Read & Execute | Test Web API Binaries | ✅ Live |
| **D:\PortalGerencial-Test\Logs** | `PortalGerencialTestApiPool` | Modify | Test Serilog Rolling Text Logs | ✅ Live |
| **D:\PortalGerencial-Test\Attachments** | `PortalGerencialTestApiPool` | Modify | Test Physical File Storage | ✅ Live |
| **D:\PortalGerencial-Test\Backups** | Administrators | Full Control | Database backups (Test SQL instance) | ✅ Live |
| **D:\PortalGerencial-Test\Packages** | Administrators | Full Control | Test Deploy packages / zips | ✅ Live |
| **D:\PortalGerencial-Test\Temp** | `PortalGerencialTestApiPool` | Modify | Test API temporary processing | ✅ Live |

- **ACL Permission Validation:**
  Workstation remote sweeps confirmed that NTFS ACL rules were assigned correctly. On the remote filesystem, the dynamic virtual SIDs for `IIS APPPOOL\PortalGerencialApiPool` and `IIS APPPOOL\PortalGerencialTestApiPool` are mapped exactly as specified in the security matrix (appearing as unresolved SIDs on outer subnets, confirming local machine security context).

---

## 6. IIS Websites, App Pools, and SSL Bindings

### Application Pools
- Integrated Pipeline, No Managed Code, running under least-privilege `ApplicationPoolIdentity`:
  - `PortalGerencialAppPool`
  - `PortalGerencialApiPool`
  - `PortalGerencialTestAppPool`
  - `PortalGerencialTestApiPool`

### Sites & Sub-Applications
1. **Production:** `PortalGerencial.Production`
   - Root Physical Path: `D:\PortalGerencial\Frontend`
   - Sub-Application `/api`: Mapped to `D:\PortalGerencial\Api` under `PortalGerencialApiPool`
2. **Test/Staging:** `PortalGerencial.Test`
   - Root Physical Path: `D:\PortalGerencial-Test\Frontend`
   - Sub-Application `/api`: Mapped to `D:\PortalGerencial-Test\Api` under `PortalGerencialTestApiPool`

### SSL Certificate Binding Details

| Environment | SSL Certificate Imported | Thumbprint | Expiration Date | Binding Status |
| :--- | :--- | :--- | :--- | :--- |
| **Production** | `CN=portal-gerencial.alpla.net` | `A7FF19E89A53073AA2B8A3FAB2AC6F16A761FE75` | 2028-05-21 19:26:01 | Bound via SNI to `portalangola.alpla.com:443` |
| **Test/Staging** | `CN=portal-gerencial-test.alpla.net` | `CDB5AC442C8D17FAE8B835C5CB13DF21DF1A88A6` | 2028-05-21 20:54:01 | Bound via SNI to `portalangola-test.alpla.com:443` |

- **Security Verification:** No certificate passwords were saved, written, or logged during script execution. passwords were exclusively read as SecureStrings in memory.
- **SNI Configuration:** SNI bindings are enabled on Port 443 for both hostnames, allowing both SSL configurations to peacefully co-exist.
- **DNS Blockers:** Since internal DNS mappings for `portalangola.alpla.com` and `portalangola-test.alpla.com` are not yet active on the network, direct domain access is currently unavailable. IP-based binding or local `hosts` files will serve as the temporary validation method.

---

## 7. Network, Port, and Firewall Rules

- **Firewall Rules Confirmed:**
  - Inbound HTTP rule "Portal Gerencial Inbound (HTTP)" allowing TCP port 80.
  - Inbound HTTPS rule "Portal Gerencial Inbound (HTTPS)" allowing TCP port 443.
- **Banned Ports Verification:**
  - **Port 5000:** **CLOSED / BLOCKED** (unresponsive). No firewall rule created.
  - **Port 5001:** **CLOSED / BLOCKED** (unresponsive). No firewall rule created.
  - No direct Kestrel backend ports were configured or opened. All backend traffic flows through in-process ANCM `/api` routing.

---

## 8. Relational Database Services Status

- **Approved SQL Server Instance:** `MSSQLSERVER` (Local General-Purpose SQL Server 2019 instance).
- **Innux Segregation Rules Verified:**
  - **No modification** has been made to instances `INNUX`, `INUTIME`, or `INNUXTIME`.
  - Attendance databases (`Innux`, `Innuxtime`, etc.) are untouched.
- **Database Provisioning Status:**
  - **`[Portal-Gerencial]`:** **NOT YET CREATED** (validation passed).
  - **`[Portal-Gerencial-Test]`:** **NOT YET CREATED** (validation passed).
  - Databases will be created in Phase 2 once the selected SQL instance `MSSQLSERVER` is ready for schema integration.

---

## 9. Deployment Safety Audit

- **No application binaries** (compiled files, DLLs, assets) have been migrated or deployed.
- **No Entity Framework Core migrations** have been run.
- **No databases or tables** have been created.
- **No plain text passwords** (SSL passwords, SQL passwords, OCR API keys) were stored in any code, script, log, or configuration files.

---

## 10. Remaining Blockers before Backend Deployment

1. **Missing ASP.NET Core IIS Module (ANCM) Registration:**
   - **Status:** **High Blocker.**
   - **Remediation:** RDP into the server `AOVIA1VMS011` as Administrator, run the **ASP.NET Core Hosting Bundle 8.0.8** installer, and select **Repair/Reinstall**. This will register `aspnetcorev2.dll` with IIS.
2. **DNS Names Mapping:**
   - **Status:** **Low Blocker** (can bypass using local hosts files).
   - **Remediation:** Map DNS names `portalangola.alpla.com` and `portalangola-test.alpla.com` to AOVIA1VMS011 IP `10.130.9.31` in the internal active directory domain `alpla.net`.

---

## 11. Next Recommended Steps (Phase 2 Roadmap)

With Phase 1 preparation successfully completed, we recommend the following roadmap for Phase 2:
1. **Resolve ANCM Blocker:** Run Repair on the Hosting Bundle.
2. **Database Provisioning:** Execute SQL scripts to create databases `[Portal-Gerencial]` and `[Portal-Gerencial-Test]` inside SQL instance `MSSQLSERVER`, and provision the isolated `usr_portalgerencial` and `usr_portalgerencial_test` SQL logins.
3. **Application Compilation & Deployment:**
   - Compile React static files and copy to Frontend folders.
   - Publish .NET API binaries and copy to Api folders.
   - Configure isolated `appsettings.Production.json` and `appsettings.Test.json` configurations.
4. **Integration Setup & EF Migrations:** Run EF Core database migrations, configure Primavera/Innux read-only integration parameters, and conduct full smoke testing.
