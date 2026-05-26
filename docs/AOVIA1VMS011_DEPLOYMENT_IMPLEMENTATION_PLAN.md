# Deployment Implementation Plan: AOVIA1VMS011
**Application:** Alpla Angola - Portal Gerencial  
**Author:** AI Technical Assistant  
**Date:** May 22, 2026  
**Status:** Implementation Plan drafted for Review (Deployment NOT yet executed)  

---

## 1. Document Overview & Goal

This document defines the structured, multi-phase **Deployment Implementation Plan** to host the **Alpla Angola Portal Gerencial** on Windows Server **`AOVIA1VMS011`** in a **dual-environment** configuration: **Production** and **Test/Staging**.

Following the technical assessment of the environment, Leonardo has confirmed the core deployment strategy:
1. **Local Database Isolation:** Host dedicated databases locally on `AOVIA1VMS011` using a general-purpose SQL Server instance (`MSSQLSERVER` or `MSSQLSERVER01`). Production uses `[Portal-Gerencial]` and Test/Staging uses `[Portal-Gerencial-Test]`. Both names contain hyphens — all SQL references must use bracket notation. Both databases must remain isolated from any active `Innux`/`Innuxtime` operational attendance databases.
2. **SSL / HTTPS Binding:** Each environment has its own SSL certificate:
   - **Production:** `C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx`
   - **Test/Staging:** `C:\dev\alpla-portal\334ad6893b414f90a349c960c5e45af4.pfx`
3. **Unified Single-Site Architecture (Per Environment):** Each environment gets its own IIS site with a static React Vite frontend at the root and an ASP.NET Core backend as an in-process sub-application via ANCM (`hostingModel="InProcess"`). **No separate Kestrel port is exposed in either environment.** Port 5000 is reserved/unavailable on this server and must never be used by either environment.
4. **Dual-Environment Isolation:** Production and Test/Staging are completely isolated from each other: separate databases, separate folder trees, separate IIS sites, separate application pools, separate SSL certificates, separate configuration files, and separate log/attachment/temp storage. Test must **never** share production resources.

> [!WARNING]
> **Deployment Authorization Constraint:** This document outlines the roadmap for the deployment prep and server provisioning. Do **NOT** install IIS, modify firewall rules, create database schemas, or execute files on the production server until this plan is formally reviewed and approved by Leonardo.

---

## 2. Environment Reference Table

| Attribute | Production | Test/Staging |
|:---|:---|:---|
| **Database** | `[Portal-Gerencial]` | `[Portal-Gerencial-Test]` |
| **SQL Login** | `usr_portalgerencial` | `usr_portalgerencial_test` |
| **Base Folder** | `D:\PortalGerencial` | `D:\PortalGerencial-Test` |
| **IIS Site** | `PortalGerencial.Production` | `PortalGerencial.Test` |
| **Frontend Pool** | `PortalGerencialAppPool` | `PortalGerencialTestAppPool` |
| **API Pool** | `PortalGerencialApiPool` | `PortalGerencialTestApiPool` |
| **URL (Preferred)** | `https://portalangola.alpla.com` | `https://portalangola-test.alpla.com` |
| **Config File** | `appsettings.Production.json` | `appsettings.Test.json` |
| **Attachments** | `D:\PortalGerencial\Attachments` | `D:\PortalGerencial-Test\Attachments` |
| **Logs** | `D:\PortalGerencial\Logs` | `D:\PortalGerencial-Test\Logs` |
| **Temp** | `D:\PortalGerencial\Temp` | `D:\PortalGerencial-Test\Temp` |
| **Backups** | `D:\PortalGerencial\Backups` | `D:\PortalGerencial-Test\Backups` |
| **Packages** | `D:\PortalGerencial\Packages` | `D:\PortalGerencial-Test\Packages` |
| **SSL Certificate** | `82460ec13b4d0f90a349c960c5e45ac8.pfx` | `334ad6893b414f90a349c960c5e45af4.pfx` |

> [!CAUTION]
> **Strict Isolation Rules:**
> - Test must **never** share the production database.
> - Test must **never** write to production attachment folders.
> - Test must **never** write to production log folders.
> - Test must **never** use the production connection string.
> - Neither environment may touch INNUX, INNUXTIME, or INUTIME databases.
> - Backend port 5000 remains unavailable for **both** environments.
> - Both environments use IIS in-process hosting (no Kestrel port exposed).
> - Certificate passwords must **never** be stored in documentation, scripts, logs, or source control.

---

## 3. Phase A: Pre-deployment Code Correction (Configurable Storage)

To prevent production attachments from polluting the system `C:` drive (due to a hardcoded relative path traversal vulnerability in `AttachmentsController.cs`), we must implement a pre-deployment code refactoring.

### 1. Refactor `AttachmentsController.cs`
We will modify the backend controller to read the file upload directory from the configuration file, falling back to a safe internal application subfolder if no override key is present:

```csharp
// Path: src/backend/AlplaPortal.Api/Controllers/AttachmentsController.cs

public class AttachmentsController : ControllerBase
{
    private readonly string _storagePath;
    private readonly ILogger<AttachmentsController> _logger;

    public AttachmentsController(IWebHostEnvironment env, IConfiguration configuration, ILogger<AttachmentsController> logger)
    {
        _logger = logger;
        
        // Retrieve the storage path override from appsettings.json
        string pathConfig = configuration["AppConfig:UploadStoragePath"];
        
        if (!string.IsNullOrWhiteSpace(pathConfig))
        {
            _storagePath = Path.GetFullPath(pathConfig);
        }
        else
        {
            // Safe fallback inside the content root to avoid traversing up to C:\ data directory
            _storagePath = Path.Combine(env.ContentRootPath, "data", "attachments");
        }

        // Ensure target directory exists on startup
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
            _logger.LogInformation("Created attachments directory at: {StoragePath}", _storagePath);
        }
    }
    
    // ... rest of controller operations remain unchanged
}
```

### 2. Update `appsettings.json` (Development Base)
Include a standard configuration stub in the base configuration file:
```json
"AppConfig": {
  "UploadStoragePath": "data/attachments"
}
```

### 3. Environment-Specific Upload Paths

**`appsettings.Production.json`:**
```json
"AppConfig": {
  "UploadStoragePath": "D:\\PortalGerencial\\Attachments"
}
```

**`appsettings.Test.json`:**
```json
"AppConfig": {
  "UploadStoragePath": "D:\\PortalGerencial-Test\\Attachments"
}
```

---

## 4. Phase B: Server Preparation & Directory Provisions

This phase outlines the setup of the Windows Web Server role, installation of IIS dependencies, and creation of the directory hierarchies for **both** environments on the dedicated empty NTFS data drive (**D:**).

### 1. IIS Web Server Installation
Enable the Web Server (IIS) role using the Administrator PowerShell console on `AOVIA1VMS011`:

```powershell
# Enable the IIS Role with standard Web Server features and Management tools
Install-WindowsFeature -name Web-Server -IncludeManagementTools

# Enable ASP.NET 4.8 and basic application development features for host integrations
Install-WindowsFeature -name Web-Net-Ext45, Web-Asp-Net45, Web-WebSockets
```

### 2. IIS URL Rewrite Module Installation
1. Download the official **IIS URL Rewrite Module v2.1 (x64)** installer from Microsoft:
   `https://www.iis.net/downloads/microsoft/url-rewrite`
2. Run the MSI installer: `rewrite_amd64_en-US.msi` silently on the server:
   ```cmd
   msiexec /i rewrite_amd64_en-US.msi /qn /norestart
   ```

### 3. Confirm ASP.NET Core Hosting Bundle
The diagnostic sweep shows that ASP.NET Core Runtime `8.0.8` is already installed. If any verification is needed, run:
```powershell
# Check for registry keys indicating the IIS hosting bundle registration
Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2" -ErrorAction SilentlyContinue
```

### 4. Create Folder Structures on Drive D:
Execute the following script to provision isolated, structured application folder trees for **both** environments on the empty **D:** drive:

```powershell
# ========================
# PRODUCTION Environment
# ========================
New-Item -ItemType Directory -Force -Path "D:\PortalGerencial"

$ProdFolders = @("Frontend", "Api", "Logs", "Attachments", "Backups", "Packages", "Temp")
foreach ($Folder in $ProdFolders) {
    New-Item -ItemType Directory -Force -Path "D:\PortalGerencial\$Folder"
}

# ========================
# TEST/STAGING Environment
# ========================
New-Item -ItemType Directory -Force -Path "D:\PortalGerencial-Test"

$TestFolders = @("Frontend", "Api", "Logs", "Attachments", "Backups", "Packages", "Temp")
foreach ($Folder in $TestFolders) {
    New-Item -ItemType Directory -Force -Path "D:\PortalGerencial-Test\$Folder"
}
```

### 5. Configure NTFS Folder Security Access Control Lists (ACLs)
To enforce the principle of least privilege, we isolate folder permissions specifically to the virtual application pools.

**Production ACLs:**

```powershell
# --- PRODUCTION Frontend ---
$AclFE = Get-Acl "D:\PortalGerencial\Frontend"
$AclFE.SetAccessRuleProtection($true, $true)
$ArFE = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "ReadAndExecute", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclFE.AddAccessRule($ArFE)
Set-Acl "D:\PortalGerencial\Frontend" $AclFE

# --- PRODUCTION Api ---
$AclApi = Get-Acl "D:\PortalGerencial\Api"
$AclApi.SetAccessRuleProtection($true, $true)
$ArApi = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialApiPool", "ReadAndExecute", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclApi.AddAccessRule($ArApi)
Set-Acl "D:\PortalGerencial\Api" $AclApi

# --- PRODUCTION Logs ---
$AclLogs = Get-Acl "D:\PortalGerencial\Logs"
$ArLogs = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclLogs.AddAccessRule($ArLogs)
Set-Acl "D:\PortalGerencial\Logs" $AclLogs

# --- PRODUCTION Attachments ---
$AclAtt = Get-Acl "D:\PortalGerencial\Attachments"
$ArAtt = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclAtt.AddAccessRule($ArAtt)
Set-Acl "D:\PortalGerencial\Attachments" $AclAtt

# --- PRODUCTION Temp ---
$AclTemp = Get-Acl "D:\PortalGerencial\Temp"
$ArTemp = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclTemp.AddAccessRule($ArTemp)
Set-Acl "D:\PortalGerencial\Temp" $AclTemp
```

**Test/Staging ACLs:**

```powershell
# --- TEST Frontend ---
$AclFE_T = Get-Acl "D:\PortalGerencial-Test\Frontend"
$AclFE_T.SetAccessRuleProtection($true, $true)
$ArFE_T = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "ReadAndExecute", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclFE_T.AddAccessRule($ArFE_T)
Set-Acl "D:\PortalGerencial-Test\Frontend" $AclFE_T

# --- TEST Api ---
$AclApi_T = Get-Acl "D:\PortalGerencial-Test\Api"
$AclApi_T.SetAccessRuleProtection($true, $true)
$ArApi_T = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialTestApiPool", "ReadAndExecute", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclApi_T.AddAccessRule($ArApi_T)
Set-Acl "D:\PortalGerencial-Test\Api" $AclApi_T

# --- TEST Logs ---
$AclLogs_T = Get-Acl "D:\PortalGerencial-Test\Logs"
$ArLogs_T = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialTestApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclLogs_T.AddAccessRule($ArLogs_T)
Set-Acl "D:\PortalGerencial-Test\Logs" $AclLogs_T

# --- TEST Attachments ---
$AclAtt_T = Get-Acl "D:\PortalGerencial-Test\Attachments"
$ArAtt_T = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialTestApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclAtt_T.AddAccessRule($ArAtt_T)
Set-Acl "D:\PortalGerencial-Test\Attachments" $AclAtt_T

# --- TEST Temp ---
$AclTemp_T = Get-Acl "D:\PortalGerencial-Test\Temp"
$ArTemp_T = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\PortalGerencialTestApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclTemp_T.AddAccessRule($ArTemp_T)
Set-Acl "D:\PortalGerencial-Test\Temp" $AclTemp_T
```

---

## 5. Phase C: SQL Server Preparation

The relational databases must be created locally on `AOVIA1VMS011` with strict service and schema segregation from Innux tables.

### 1. Target SQL Instance Configuration
Leonardo must select one of the general-purpose SQL Server 2019 instances running locally:
*   `AOVIA1VMS011\MSSQLSERVER` (Preferred Default SQL Instance)
*   `AOVIA1VMS011\MSSQLSERVER01` (Alternate Secondary SQL Instance)

> [!CAUTION]
> **Database Co-existence Rules:** Under no circumstances should the dedicated `[Portal-Gerencial]` or `[Portal-Gerencial-Test]` databases be provisioned inside instances `INNUX`, `INUTIME`, or `INNUXTIME`. Do not create any application schemas, views, or logins inside the existing attendance databases.

### 2. Production Database Creation

Log in to the selected SQL Server instance using SQL Server Management Studio (SSMS) or SQLCMD as administrator and execute:

```sql
-- ========================================
-- PRODUCTION DATABASE: [Portal-Gerencial]
-- ========================================
-- NOTE: Name contains a hyphen — always use bracket notation [Portal-Gerencial]
CREATE DATABASE [Portal-Gerencial]
ON PRIMARY (
    NAME = N'Portal-Gerencial',
    FILENAME = N'D:\PortalGerencial\Backups\Portal-Gerencial.mdf',
    SIZE = 256MB,
    MAXSIZE = UNLIMITED,
    FILEGROWTH = 64MB
)
LOG ON (
    NAME = N'Portal-Gerencial_log',
    FILENAME = N'D:\PortalGerencial\Backups\Portal-Gerencial_log.ldf',
    SIZE = 128MB,
    MAXSIZE = 2048MB,
    FILEGROWTH = 32MB
);
GO

-- Set recovery model to Simple to prevent log exhaustion unless transaction logs are regularly backed up
ALTER DATABASE [Portal-Gerencial] SET RECOVERY SIMPLE;
GO
```

### 3. Test/Staging Database Creation

```sql
-- =============================================
-- TEST/STAGING DATABASE: [Portal-Gerencial-Test]
-- =============================================
-- NOTE: Name contains hyphens — always use bracket notation [Portal-Gerencial-Test]
CREATE DATABASE [Portal-Gerencial-Test]
ON PRIMARY (
    NAME = N'Portal-Gerencial-Test',
    FILENAME = N'D:\PortalGerencial-Test\Backups\Portal-Gerencial-Test.mdf',
    SIZE = 128MB,
    MAXSIZE = UNLIMITED,
    FILEGROWTH = 64MB
)
LOG ON (
    NAME = N'Portal-Gerencial-Test_log',
    FILENAME = N'D:\PortalGerencial-Test\Backups\Portal-Gerencial-Test_log.ldf',
    SIZE = 64MB,
    MAXSIZE = 1024MB,
    FILEGROWTH = 32MB
);
GO

ALTER DATABASE [Portal-Gerencial-Test] SET RECOVERY SIMPLE;
GO
```

### 4. Application Security SQL Logins

**Production Login:**

```sql
-- Execute in [master]
CREATE LOGIN [usr_portalgerencial] 
WITH PASSWORD = '[REDACTED_SECURE_PASSWORD]', 
     DEFAULT_DATABASE = [Portal-Gerencial], 
     CHECK_EXPIRATION = OFF, 
     CHECK_POLICY = ON;
GO

-- Execute in [Portal-Gerencial]
USE [Portal-Gerencial];
GO
CREATE USER [usr_portalgerencial] FOR LOGIN [usr_portalgerencial];
GO
ALTER ROLE [db_owner] ADD MEMBER [usr_portalgerencial];
GO
```

**Test/Staging Login:**

```sql
-- Execute in [master]
CREATE LOGIN [usr_portalgerencial_test] 
WITH PASSWORD = '[REDACTED_SECURE_PASSWORD]', 
     DEFAULT_DATABASE = [Portal-Gerencial-Test], 
     CHECK_EXPIRATION = OFF, 
     CHECK_POLICY = ON;
GO

-- Execute in [Portal-Gerencial-Test]
USE [Portal-Gerencial-Test];
GO
CREATE USER [usr_portalgerencial_test] FOR LOGIN [usr_portalgerencial_test];
GO
ALTER ROLE [db_owner] ADD MEMBER [usr_portalgerencial_test];
GO
```

---

## 6. Phase D: SSL / HTTPS Binding Setup

Each environment uses a **separate SSL certificate file**. We secure all browser-to-server traffic by binding the certificates to their respective IIS sites.

### 1. Enforce Strict Security Rule
> [!IMPORTANT]
> **Password Handling Policy:** The passwords for both PFX certificate files must never be written into any `.ps1` file, batch script, JSON configuration, README, or markdown file.
> To import each certificate securely:
> - Use the **Windows Certificate Import Wizard** interactively in the GUI, OR
> - If importing via PowerShell, capture the password securely in memory using `Read-Host -AsSecureString` so the password is never stored or visible in command histories.

### 2. SSL Certificate Paths

| Environment | PFX Certificate File |
|:---|:---|
| **Production** | `C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx` |
| **Test/Staging** | `C:\dev\alpla-portal\334ad6893b414f90a349c960c5e45af4.pfx` |

### 3. PowerShell SSL Certificate Import

**Production Certificate:**

```powershell
$PfxPathProd = "C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx"
$PasswordProd = Read-Host -Prompt "Enter Production SSL Certificate Password" -AsSecureString
Import-PfxCertificate -FilePath $PfxPathProd -CertStoreLocation "Cert:\LocalMachine\My" -Password $PasswordProd
```

**Test/Staging Certificate:**

```powershell
$PfxPathTest = "C:\dev\alpla-portal\334ad6893b414f90a349c960c5e45af4.pfx"
$PasswordTest = Read-Host -Prompt "Enter Test/Staging SSL Certificate Password" -AsSecureString
Import-PfxCertificate -FilePath $PfxPathTest -CertStoreLocation "Cert:\LocalMachine\My" -Password $PasswordTest
```

### 4. Retrieve Certificate Thumbprints
Identify the imported certificates' thumbprints for binding operations:
```powershell
# List all certificates to identify both thumbprints
Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Subject -like "*alpla*" } | Format-Table Subject, Thumbprint, NotAfter
```

---

## 7. Phase E: Application Configuration Strategy

Each environment has its own configuration file with isolated connection strings, paths, and integration settings.

> [!CAUTION]
> **Configuration Cross-Contamination Prevention:**
> - Production configuration must **never** reference Test/Staging paths or databases.
> - Test/Staging configuration must **never** reference Production paths or databases.
> - Connection strings, attachment paths, and log paths must be verified independently for each environment before deployment.

### 1. Template: `appsettings.Production.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "D:\\PortalGerencial\\Logs\\log-production-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 31,
          "fileSizeLimitBytes": 52428800,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=Portal-Gerencial;User Id=usr_portalgerencial;Password=[REDACTED_SECURE_PASSWORD];Trusted_Connection=False;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "AppConfig": {
    "UploadStoragePath": "D:\\PortalGerencial\\Attachments",
    "AllowedHosts": "*",
    "TokenExpirationMinutes": 480,
    "EnvironmentLabel": "Production"
  },
  "Jwt": {
    "Issuer": "AlplaPortalProduction",
    "Audience": "AlplaPortalUsers",
    "Secret": "[REDACTED_32+_CHARACTER_CRYPTOGRAPHIC_KEY]"
  },
  "Integrations": {
    "Primavera": {
      "Enabled": true,
      "Server": "AOVIA1VMS012",
      "InstanceName": "SQLALPLA",
      "AuthenticationMode": "SQL",
      "Username": "sa",
      "Password": "[REDACTED_ERP_PASSWORD]",
      "Companies": {
        "ALPLAPLASTICO": { "DatabaseName": "PRI297514001", "Enabled": true },
        "ALPLASOPRO": { "DatabaseName": "PRI297514003", "Enabled": true }
      }
    },
    "Innux": {
      "Enabled": true,
      "Server": "np:\\\\AOVIA1VMS012\\pipe\\MSSQL$SQLINNUX\\sql\\query",
      "DatabaseName": "Innux",
      "AuthenticationMode": "SQL",
      "Username": "sa",
      "Password": "[REDACTED_ERP_PASSWORD]"
    }
  },
  "DocumentExtraction": {
    "ActiveProvider": "OPENAI",
    "OpenAi": {
      "ApiKey": "[REDACTED_OPENAI_KEY]",
      "Model": "gpt-4o",
      "TimeoutSeconds": 60
    }
  },
  "Email": {
    "Enabled": true
  }
}
```

### 2. Template: `appsettings.Test.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Information",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "D:\\PortalGerencial-Test\\Logs\\log-test-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 14,
          "fileSizeLimitBytes": 52428800,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [TEST] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=Portal-Gerencial-Test;User Id=usr_portalgerencial_test;Password=[REDACTED_SECURE_PASSWORD];Trusted_Connection=False;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "AppConfig": {
    "UploadStoragePath": "D:\\PortalGerencial-Test\\Attachments",
    "AllowedHosts": "*",
    "TokenExpirationMinutes": 480,
    "EnvironmentLabel": "Test/Staging"
  },
  "Jwt": {
    "Issuer": "AlplaPortalTest",
    "Audience": "AlplaPortalTestUsers",
    "Secret": "[REDACTED_32+_CHARACTER_CRYPTOGRAPHIC_KEY_TEST]"
  },
  "Integrations": {
    "Primavera": {
      "Enabled": true,
      "Server": "AOVIA1VMS012",
      "InstanceName": "SQLALPLA",
      "AuthenticationMode": "SQL",
      "Username": "sa",
      "Password": "[REDACTED_ERP_PASSWORD]",
      "Companies": {
        "ALPLAPLASTICO": { "DatabaseName": "PRI297514001", "Enabled": true },
        "ALPLASOPRO": { "DatabaseName": "PRI297514003", "Enabled": true }
      }
    },
    "Innux": {
      "Enabled": true,
      "Server": "np:\\\\AOVIA1VMS012\\pipe\\MSSQL$SQLINNUX\\sql\\query",
      "DatabaseName": "Innux",
      "AuthenticationMode": "SQL",
      "Username": "sa",
      "Password": "[REDACTED_ERP_PASSWORD]"
    }
  },
  "DocumentExtraction": {
    "ActiveProvider": "OPENAI",
    "OpenAi": {
      "ApiKey": "[REDACTED_OPENAI_KEY]",
      "Model": "gpt-4o",
      "TimeoutSeconds": 60
    }
  },
  "Email": {
    "Enabled": false
  }
}
```

### 3. Windows Environment Variables Alternative (Highly Recommended)
For enhanced security, passwords can be injected into the server environment:
*   Key: `ConnectionStrings__DefaultConnection`
*   Key: `Jwt__Secret`
*   Key: `Integrations__Primavera__Password`
*   Key: `Integrations__Innux__Password`
*   Key: `DocumentExtraction__OpenAi__ApiKey`

When using environment variables, set them per-environment using IIS Application Pool environment variables or Windows machine-level environment variables scoped by site identity.

> [!WARNING]
> **IIS Environment Variables Storage tradeoff:**
> Setting environment variables on an IIS Application Pool via `appcmd.exe` persists the keys and values in plaintext inside the central IIS configuration file `C:\Windows\System32\inetsrv\config\applicationHost.config`.
> While this file is strictly protected by the OS (only accessible to Administrators and SYSTEM), it is still stored *on disk* in plaintext. This is a known staging tradeoff.
> For maximum security in production environments, consider mapping the App Pool identity to a SQL Server Windows login (Trusted Connection / Windows Auth) to eliminate SQL Server login passwords entirely.

---

## 8. Integration Write-Safety Classification

Leonardo confirmed that Test/Staging may initially connect to OCR, Primavera, and Innux. However, any integration that can write, update, trigger workflows, send emails, generate ERP changes, alter Innux/Primavera data, or create side effects in external systems must be carefully classified.

> [!WARNING]
> **Do not assume that because an integration is reachable, it is safe to execute write operations from Test/Staging.** Initial Test/Staging validation should prefer **read-only** integration access.

### Integration Safety Matrix

| Integration | Capability | Test/Staging Classification | Notes |
|:---|:---|:---|:---|
| **Primavera DB** (read company/items/suppliers) | Read-only | ✅ **Enabled (Read-Only)** | Reads procurement, supplier, and item master data from `AOVIA1VMS012\SQLALPLA`. No write operations to Primavera tables. Safe for Test/Staging. |
| **Innux DB** (read attendance/punches) | Read-only | ✅ **Enabled (Read-Only)** | Reads attendance records, punch data, employee schedules. No write operations to Innux tables. Safe for Test/Staging. |
| **OCR / Document Extraction** (OpenAI Vision) | External API call | ✅ **Enabled** | Sends document images to OpenAI API for extraction. Consumes API credits but does not modify any internal system data. Safe for Test/Staging. |
| **Email Notifications** (SMTP) | Write (sends emails) | 🚫 **Disabled in Test/Staging** | Email notifications must be **disabled by default** in Test/Staging (`"Email": { "Enabled": false }`) to prevent real workflow emails from reaching production users. Must be explicitly approved before enabling. |
| **Primavera ERP write-back** (if future) | Write | 🚫 **Disabled / Not Implemented** | Any future integration that creates purchase orders, invoices, or modifies ERP records must be blocked in Test/Staging until explicitly approved as a separate decision. |
| **Innux write-back** (if future) | Write | 🚫 **Disabled / Not Implemented** | Any future integration that modifies attendance records, schedules, or punch data must be blocked in Test/Staging. |
| **Webhook / External triggers** (if future) | Write | 🚫 **Sandboxed / Dry-run** | Any future external webhook or trigger integration must operate in dry-run/simulation mode in Test/Staging. |

> [!IMPORTANT]
> **Any future enablement of write-capable integrations in Test/Staging must be documented as a separate architectural decision with explicit approval from Leonardo.**

---

## 9. Phase F: Frontend & Backend Publishing Workflow

To preserve performance and keep environments clean, compilations must be built on the developer's build machine.

### 1. Build and Publish the Static Frontend (React Vite)
On the build machine:
```bash
# Navigate to the frontend directory
cd C:\dev\alpla-portal\src\frontend

# Ensure production packages are configured and optimized
npm install

# Build static files
npm run build
```
*   **Artifact:** React generates static assets inside the `dist` folder.
*   **Migration:** Compress `dist` into a ZIP archive, copy to `AOVIA1VMS011`, and extract all files directly to the target environment folder.
*   **Production:** `D:\PortalGerencial\Frontend`
*   **Test/Staging:** `D:\PortalGerencial-Test\Frontend`
*   **SPA Route Rewriting:** Add the following `web.config` file to the root of **each** Frontend folder to handle React Joyride and SPA route history:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="ReactSPA" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
            <add input="{REQUEST_URI}" pattern="^/(api)" negate="true" />
          </conditions>
          <action type="Rewrite" url="/" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
```

### 2. Publish the Backend API (.NET 8)
On the build machine:
```bash
# Navigate to backend API project
cd C:\dev\alpla-portal\src\backend\AlplaPortal.Api

# Compile and publish optimized binaries
dotnet publish AlplaPortal.Api.csproj -c Release -o C:\dev\alpla-portal\publish\api
```
*   **Artifact:** .NET publishes optimized binaries to `C:\dev\alpla-portal\publish\api`.
*   **Migration:** Copy all binaries from the publish folder to the target environment folder.
*   **Production:** `D:\PortalGerencial\Api`
*   **Test/Staging:** `D:\PortalGerencial-Test\Api`

### 3. Establish the IIS Dual-Site Configuration

Configure both environments in IIS using administrative PowerShell:

```powershell
Import-Module WebAdministration

# ====================================
# PRODUCTION Application Pools & Site
# ====================================
New-WebAppPool -Name "PortalGerencialAppPool"
Set-ItemProperty -Path "IIS:\AppPools\PortalGerencialAppPool" -Name "managedRuntimeVersion" -Value ""

New-WebAppPool -Name "PortalGerencialApiPool"
Set-ItemProperty -Path "IIS:\AppPools\PortalGerencialApiPool" -Name "managedRuntimeVersion" -Value ""

New-Website -Name "PortalGerencial.Production" -PhysicalPath "D:\PortalGerencial\Frontend" -Port 80 -HostHeader "portalangola.alpla.com" -ApplicationPool "PortalGerencialAppPool"

# Add HTTPS binding with Production certificate
$ThumbProd = "[PROD_CERT_THUMBPRINT]"
New-WebBinding -Name "PortalGerencial.Production" -IPAddress "*" -Port 443 -Protocol "https" -HostHeader "portalangola.alpla.com" -SslFlags 1
# Bind the production certificate (SNI-based)
(Get-WebBinding -Name "PortalGerencial.Production" -Protocol "https").AddSslCertificate($ThumbProd, "My")

# IMPORTANT: Port 5000 is RESERVED. Do NOT configure any Kestrel URL binding.
New-WebApplication -Site "PortalGerencial.Production" -Name "api" -PhysicalPath "D:\PortalGerencial\Api" -ApplicationPool "PortalGerencialApiPool"

# ====================================
# TEST/STAGING Application Pools & Site
# ====================================
New-WebAppPool -Name "PortalGerencialTestAppPool"
Set-ItemProperty -Path "IIS:\AppPools\PortalGerencialTestAppPool" -Name "managedRuntimeVersion" -Value ""

New-WebAppPool -Name "PortalGerencialTestApiPool"
Set-ItemProperty -Path "IIS:\AppPools\PortalGerencialTestApiPool" -Name "managedRuntimeVersion" -Value ""

New-Website -Name "PortalGerencial.Test" -PhysicalPath "D:\PortalGerencial-Test\Frontend" -Port 80 -HostHeader "portalangola-test.alpla.com" -ApplicationPool "PortalGerencialTestAppPool"

# Add HTTPS binding with Test certificate
$ThumbTest = "[TEST_CERT_THUMBPRINT]"
New-WebBinding -Name "PortalGerencial.Test" -IPAddress "*" -Port 443 -Protocol "https" -HostHeader "portalangola-test.alpla.com" -SslFlags 1
# Bind the test certificate (SNI-based)
(Get-WebBinding -Name "PortalGerencial.Test" -Protocol "https").AddSslCertificate($ThumbTest, "My")

# IMPORTANT: Port 5000 is RESERVED. Do NOT configure any Kestrel URL binding.
New-WebApplication -Site "PortalGerencial.Test" -Name "api" -PhysicalPath "D:\PortalGerencial-Test\Api" -ApplicationPool "PortalGerencialTestApiPool"
```

> [!IMPORTANT]
> The backend `web.config` inside **both** `D:\PortalGerencial\Api` and `D:\PortalGerencial-Test\Api` must specify `hostingModel="InProcess"` in the `aspNetCore` element. This ensures the .NET process runs inside `w3wp.exe` directly, with **no separate Kestrel port exposed**. Do **NOT** configure `ASPNETCORE_URLS` to port 5000 or 5001 in either environment.

> [!NOTE]
> **DNS Fallback:** If DNS for `portalangola-test.alpla.com` is not ready, a temporary fallback approach is to use a different port (e.g., 8443) or configure a local `hosts` file entry on client machines. The preferred long-term model is hostname-based HTTPS binding on port 443 via SNI (Server Name Indication).

---

## 10. Phase G: Release Promotion Workflow

All releases must follow a **Test-First** deployment flow. The same build package deployed to Test/Staging must be promoted to Production after validation.

```
┌──────────────┐     ┌──────────────────┐     ┌───────────────────┐     ┌──────────────────┐
│  A. Build    │────▶│  B. Deploy to    │────▶│  C-G. Validate    │────▶│  H. Promote to   │
│  Package     │     │  Test/Staging    │     │  on Test/Staging  │     │  Production      │
└──────────────┘     └──────────────────┘     └───────────────────┘     └──────────────────┘
```

| Step | Action | Details |
|:---:|:---|:---|
| **A** | Build package | Compile frontend (`npm run build`) and backend (`dotnet publish`) on the build machine. |
| **B** | Deploy to Test/Staging | Copy frontend artifacts to `D:\PortalGerencial-Test\Frontend`. Copy backend binaries to `D:\PortalGerencial-Test\Api`. |
| **C** | Validate smoke tests | Execute the Test/Staging smoke test checklist (Section 11). |
| **D** | Validate database migrations | Explicitly execute the pre-placed idempotent migrations SQL script using sqlcmd with Windows Auth against `[Portal-Gerencial-Test]` and verify `__EFMigrationsHistory`. |
| **E** | Validate upload/download | Upload and download a document. Verify files appear in `D:\PortalGerencial-Test\Attachments`. |
| **F** | Validate login & permissions | Log in with test accounts. Verify RBAC roles, sidebar visibility, and module access. |
| **G** | Validate integrations | Confirm Primavera read, Innux attendance read, and OCR extraction work correctly against Test/Staging. Verify no write side-effects. |
| **H** | Promote to Production | After Leonardo approves: deploy the **same package** to `D:\PortalGerencial\Frontend` and `D:\PortalGerencial\Api`. Run the Production smoke test checklist. |

> [!WARNING]
> **Never deploy directly to Production without first validating on Test/Staging.**

---

## 11. Phase H: Validation & Smoke Testing Checklists

Following the completion of deployment steps, each environment must be rigorously validated.

### Production Smoke Tests

| Step | Verification Target | Action / Command | Expected Result | Status |
| :--- | :--- | :--- | :--- | :---: |
| **1** | Web Port Inbound | `Test-NetConnection -ComputerName AOVIA1VMS011 -Port 443` | TCP Connection Succeeded = `True` | `[ ]` |
| **2** | HTTPS Web Routing | Open browser to `https://portalangola.alpla.com` | Static React welcome page loads securely. Joyride starts. | `[ ]` |
| **3** | Backend API Health | Navigate to `https://portalangola.alpla.com/api/v1/health` | HTTP 200 returned with JSON: `{"status": "Healthy"}`. | `[ ]` |
| **4** | SQL local DB | Check `D:\PortalGerencial\Logs` files on startup | No database connection exception logs. EF Migrations successfully ran against `[Portal-Gerencial]`. | `[ ]` |
| **5** | Primavera Integration | Navigate to the Compras dashboard | KPI cards populate with procurement counts, indicating valid DB integration. | `[ ]` |
| **6** | File Uploads Path | Upload a document in the Portal | File is physically created under `D:\PortalGerencial\Attachments`. No permission block. | `[ ]` |
| **7** | Daily rolling logs | Wait 24h or trigger errors | Log files exist in `D:\PortalGerencial\Logs` under rolling date convention. | `[ ]` |
| **8** | Port 5000 NOT bound | `Test-NetConnection -ComputerName AOVIA1VMS011 -Port 5000` | TCP Connection Succeeded = `False`. Backend does **not** listen on port 5000. | `[ ]` |

### Test/Staging Smoke Tests

| Step | Verification Target | Action / Command | Expected Result | Status |
| :--- | :--- | :--- | :--- | :---: |
| **T1** | Web Port Inbound | `Test-NetConnection -ComputerName AOVIA1VMS011 -Port 443` | TCP Connection Succeeded = `True` | `[ ]` |
| **T2** | HTTPS Web Routing | Open browser to `https://portalangola-test.alpla.com` | Static React welcome page loads securely. | `[ ]` |
| **T3** | Backend API Health | Navigate to `https://portalangola-test.alpla.com/api/v1/health` | HTTP 200 returned with JSON: `{"status": "Healthy"}`. | `[ ]` |
| **T4** | SQL local DB | Check tables via sqlcmd / SSMS after running migration.sql | No database connection exceptions. __EFMigrationsHistory and expected database tables are present. | `[ ]` |
| **T5** | Primavera Integration | Navigate to the Compras dashboard | KPI cards populate with procurement counts (read-only integration). | `[ ]` |
| **T6** | File Uploads Path | Upload a document in the Portal | File is physically created under `D:\PortalGerencial-Test\Attachments`. NOT under Production path. | `[ ]` |
| **T7** | Daily rolling logs | Trigger an error or wait | Log files exist in `D:\PortalGerencial-Test\Logs`. NOT in Production log path. | `[ ]` |
| **T8** | Port 5000 NOT bound | `Test-NetConnection -ComputerName AOVIA1VMS011 -Port 5000` | TCP Connection Succeeded = `False`. | `[ ]` |
| **T9** | Email disabled | Trigger a workflow notification | No email is sent. Backend logs show email suppression. | `[ ]` |
| **T10** | Cross-contamination | Verify `D:\PortalGerencial\Attachments` and `D:\PortalGerencial\Logs` | No test artifacts appear in Production folders. | `[ ]` |
