# Deployment Implementation Plan: AOVIA1VMS011
**Application:** Alpla Angola - Portal Gerencial  
**Author:** AI Technical Assistant  
**Date:** May 22, 2026  
**Status:** Implementation Plan drafted for Review (Deployment NOT yet executed)  

---

## 1. Document Overview & Goal

This document defines the structured, multi-phase **Deployment Implementation Plan** to host the **Alpla Angola Portal Gerencial** on Windows Server **`AOVIA1VMS011`**. 

Following the technical assessment of the environment, Leonardo has confirmed the core deployment strategy:
1. **Local Database Isolation:** Host the dedicated `AlplaPortal` database locally on `AOVIA1VMS011` using a general-purpose SQL Server instance (`MSSQLSERVER` or `MSSQLSERVER01`). The database must remain isolated from any active `Innux`/`Innuxtime` operational attendance databases.
2. **SSL / HTTPS Binding:** Production traffic will be secured via HTTPS using the local certificate file available at `C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx`.
3. **Unified Single-Site Architecture:** IIS will host the static React Vite frontend at the root (`D:\AlplaPortal\Frontend`) and act as a reverse proxy for the backend API sub-application (`D:\AlplaPortal\Api`) mapping `/api/*` requests directly to the ASP.NET Core Kestrel backend.

> [!WARNING]
> **Deployment Authorization Constraint:** This document outlines the roadmap for the deployment prep and server provisioning. Do **NOT** install IIS, modify firewall rules, create database schemas, or execute files on the production server until this plan is formally reviewed and approved by Leonardo.

---

## 2. Phase A: Pre-deployment Code Correction (Configurable Storage)

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

### 3. Update `appsettings.Production.json`
Configure the dedicated data drive production upload path:
```json
"AppConfig": {
  "UploadStoragePath": "D:\\AlplaPortal\\Attachments"
}
```

---

## 3. Phase B: Server Preparation & Directory Provisions

This phase outlines the setup of the Windows Web Server role, installation of IIS dependencies, and creation of the directory hierarchy on the dedicated empty NTFS data drive (**D:**).

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

### 4. Create Folder Structure on Drive D:
Execute the following script to provision isolated, structured application folder trees on the empty **D:** drive:

```powershell
# Create root deployment container
New-Item -ItemType Directory -Force -Path "D:\AlplaPortal"

# Create application subdivisions
$SubFolders = @("Frontend", "Api", "Logs", "Attachments", "Backups", "Packages")
foreach ($Folder in $SubFolders) {
    New-Item -ItemType Directory -Force -Path "D:\AlplaPortal\$Folder"
}
```

### 5. Configure NTFS Folder Security Access Control Lists (ACLs)
To enforce the principle of least privilege, we isolate folder permissions specifically to the virtual application pools:

```powershell
# Disable inheritance and grant strict read permissions for the static frontend to IIS_IUSRS
$AclFE = Get-Acl "D:\AlplaPortal\Frontend"
$AclFE.SetAccessRuleProtection($true, $true) # Protect and copy existing rules
$ArFE = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "ReadAndExecute", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclFE.AddAccessRule($ArFE)
Set-Acl "D:\AlplaPortal\Frontend" $AclFE

# Disable inheritance and grant read/execute permissions for backend binaries to the API virtual account
$AclApi = Get-Acl "D:\AlplaPortal\Api"
$AclApi.SetAccessRuleProtection($true, $true)
$ArApi = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\AlplaPortalApiPool", "ReadAndExecute", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclApi.AddAccessRule($ArApi)
Set-Acl "D:\AlplaPortal\Api" $AclApi

# Grant full read/write/modify access to Logs for the API virtual account
$AclLogs = Get-Acl "D:\AlplaPortal\Logs"
$ArLogs = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\AlplaPortalApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclLogs.AddAccessRule($ArLogs)
Set-Acl "D:\AlplaPortal\Logs" $AclLogs

# Grant full read/write/modify access to Attachments for the API virtual account
$AclAtt = Get-Acl "D:\AlplaPortal\Attachments"
$ArAtt = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS APPPOOL\AlplaPortalApiPool", "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
$AclAtt.AddAccessRule($ArAtt)
Set-Acl "D:\AlplaPortal\Attachments" $AclAtt
```

---

## 4. Phase C: SQL Server Preparation

The relational database must be created locally on `AOVIA1VMS011` with strict service and schema segregation from Innux tables.

### 1. Target SQL Instance Configuration
Leonardo must select one of the general-purpose SQL Server 2019 instances running locally:
*   `AOVIA1VMS011\MSSQLSERVER` (Preferred Default SQL Instance)
*   `AOVIA1VMS011\MSSQLSERVER01` (Alternate Secondary SQL Instance)

> [!CAUTION]
> **Database Co-existence Rules:** Under no circumstances should the dedicated `AlplaPortal` database be provisioned inside instances `INNUX`, `INUTIME`, or `INNUXTIME`. Do not create any application schemas, views, or logins inside the existing attendance databases.

### 2. Dedicated Database Creation
Log in to the selected SQL Server instance using SQL Server Management Studio (SSMS) or SQLCMD as administrator and execute:

```sql
-- Create dedicated database for the Portal
CREATE DATABASE [AlplaPortal]
ON PRIMARY (
    NAME = N'AlplaPortal',
    FILENAME = N'D:\AlplaPortal\Backups\AlplaPortal.mdf',
    SIZE = 256MB,
    MAXSIZE = UNLIMITED,
    FILEGROWTH = 64MB
)
LOG ON (
    NAME = N'AlplaPortal_log',
    FILENAME = N'D:\AlplaPortal\Backups\AlplaPortal_log.ldf',
    SIZE = 128MB,
    MAXSIZE = 2048MB,
    FILEGROWTH = 32MB
);
GO

-- Set recovery model to Simple to prevent log exhaustion unless transaction logs are regularly backed up
ALTER DATABASE [AlplaPortal] SET RECOVERY SIMPLE;
GO
```

### 3. Application Security SQL Login Setup
Create a dedicated local database SQL login for the web application API with minimum required privileges:

```sql
-- Execute in [master]
CREATE LOGIN [usr_portalgerencial] 
WITH PASSWORD = '[REDACTED_SECURE_PASSWORD]', 
     DEFAULT_DATABASE = [AlplaPortal], 
     CHECK_EXPIRATION = OFF, 
     CHECK_POLICY = ON;
GO

-- Execute in [AlplaPortal]
USE [AlplaPortal];
GO
CREATE USER [usr_portalgerencial] FOR LOGIN [usr_portalgerencial];
GO
-- Assign minimum permissions needed to create tables and execute migrations
ALTER ROLE [db_owner] ADD MEMBER [usr_portalgerencial];
GO
```

---

## 5. Phase D: SSL / HTTPS Binding Setup

We secure all browser-to-server traffic by binding the provided SSL certificate file on the IIS website.

### 1. Enforce strict Security Rule
> [!IMPORTANT]
> **Password Handling Policy:** The password for the PFX certificate must never be written into any `.ps1` file, batch script, JSON configuration, README, or markdown file. 
> To import the certificate securely:
> - Use the **Windows Certificate Import Wizard** interactively in the GUI, OR
> - If importing via PowerShell, capture the password securely in memory using `Read-Host -AsSecureString` so the password is never stored or visible in command histories.

### 2. PowerShell SSL Certificate Import
Using an administrator PowerShell console, run the following commands to safely import the PFX:

```powershell
# Define PFX location
$PfxPath = "C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx"

# Interactively request the password in a secure memory container
$Password = Read-Host -Prompt "Enter SSL Certificate Password" -AsSecureString

# Import the certificate into the Local Computer "My" (Personal) certificate store
Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "Cert:\LocalMachine\My" -Password $Password
```

### 3. Retrieve Certificate Thumbprint
Identify the imported certificate's thumbprint for binding operations:
```powershell
# Locate the certificate by subject or expiration
Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Subject -like "*alpla*" }
```

---

## 6. Phase E: Application Configuration Strategy

Production overrides must be securely maintained inside `appsettings.Production.json` or Windows Environment Variables.

### 1. Template: `appsettings.Production.json`
Create this file in the backend source tree. Replace all sensitive parameters (connection strings, tokens) with placeholders:

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
          "path": "D:\\AlplaPortal\\Logs\\log-production-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 31,
          "fileSizeLimitBytes": 52428800,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\MSSQLSERVER;Database=AlplaPortal;User Id=usr_portalgerencial;Password=[REDACTED_SECURE_PASSWORD];Trusted_Connection=False;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "AppConfig": {
    "UploadStoragePath": "D:\\AlplaPortal\\Attachments",
    "AllowedHosts": "*",
    "TokenExpirationMinutes": 480
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
  }
}
```

### 2. Windows Environment Variables Alternative (Highly Recommended)
For enhanced security, passwords can be injected into the server environment:
*   Key: `ConnectionStrings__DefaultConnection`
*   Key: `Jwt__Secret`
*   Key: `Integrations__Primavera__Password`
*   Key: `Integrations__Innux__Password`
*   Key: `DocumentExtraction__OpenAi__ApiKey`

---

## 7. Phase F: Frontend & Backend Publishing Workflow

To preserve performance and keep production environments clean, compilations must be built on the developer's build machine.

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
*   **Migration:** Compress `dist` into a ZIP archive, copy to `AOVIA1VMS011`, and extract all files directly to:
    **`D:\AlplaPortal\Frontend`**
*   **SPA Route Rewriting:** Add the following `web.config` file to the root of the Frontend folder to handle React Joyride and SPA route history:
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
*   **Migration:** Copy all binaries from the publish folder to the server at:
    **`D:\AlplaPortal\Api`**

### 3. Establish the IIS Single-Site Bindings
Configure the application in IIS using administrative PowerShell:

```powershell
# Import WebAdministration module
Import-Module WebAdministration

# 1. Create Application Pools
New-WebAppPool -Name "AlplaPortalAppPool"
Set-ItemProperty -Path "IIS:\AppPools\AlplaPortalAppPool" -Name "managedRuntimeVersion" -Value "" # Set to No Managed Code

New-WebAppPool -Name "AlplaPortalApiPool"
Set-ItemProperty -Path "IIS:\AppPools\AlplaPortalApiPool" -Name "managedRuntimeVersion" -Value "" # Set to No Managed Code

# 2. Create the Single Website bound to the static Frontend
New-Website -Name "AlplaPortal.Production" -PhysicalPath "D:\AlplaPortal\Frontend" -Port 80 -ApplicationPool "AlplaPortalAppPool"

# 3. Add HTTPS Binding using the SSL certificate thumbprint
# (Replace [CERT_THUMBPRINT] with the actual thumbprint retrieved in Phase D)
$Thumb = "[CERT_THUMBPRINT]"
New-WebBinding -Name "AlplaPortal.Production" -IPAddress "*" -Port 443 -Protocol "https"
# Bind certificate to Port 443
Get-Item -Path "cert:\LocalMachine\My\$Thumb" | New-Item -Path "IIS:\SslBindings\0.0.0.0!443"

# 4. Map the API Sub-Application
New-WebApplication -Site "AlplaPortal.Production" -Name "api" -PhysicalPath "D:\AlplaPortal\Api" -ApplicationPool "AlplaPortalApiPool"
```

---

## 8. Phase G: Validation & Smoke Testing Checklist

Following the completion of deployment steps, the deployment must be rigorously validated before user release.

| Step | Verification Target | Action / Command | Expected Result | Status |
| :--- | :--- | :--- | :--- | :---: |
| **1** | Web Port Inbound | `Test-NetConnection -ComputerName AOVIA1VMS011 -Port 443` | TCP Connection Succeeded = `True` | `[ ]` |
| **2** | HTTPS Web Routing | Open browser to `https://[PORTAL_URL_DNS]` | Static React welcome page loads securely. Joyride starts. | `[ ]` |
| **3** | Backend API Health | Navigate to `https://[PORTAL_URL_DNS]/api/v1/health` | HTTP 200 returned with JSON: `{"status": "Healthy"}`. | `[ ]` |
| **4** | SQL local DB | Check `D:\AlplaPortal\Logs` files on startup | No database connection exception logs. EF Migrations successfully ran. | `[ ]` |
| **5** | Primavera Integration | Navigate to the Compras dashboard | KPI cards populate with procurement counts, indicating valid DB integration. | `[ ]` |
| **6** | File Uploads Path | Upload a document in the Portal | File is physically created under `D:\AlplaPortal\Attachments`. No permission block. | `[ ]` |
| **7** | Daily rolling logs | Wait 24h or trigger errors | Log files exist in `D:\AlplaPortal\Logs` under rolling date convention. | `[ ]` |
