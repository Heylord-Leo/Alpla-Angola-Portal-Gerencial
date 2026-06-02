# =============================================================================
# Alpla Angola - Portal Gerencial
# Production Environment Bootstrap Script
# =============================================================================
#
# Target Server:  AOVIA1VMS011
# Environment:    PRODUCTION
# API Port:       5002
# Database:       [Portal-Gerencial]
# URL:            https://portalgerencial.alpla.net
#
# This script prepares the server-side Production environment.
# It is idempotent - running it multiple times will not create duplicates
# or break existing configuration.
#
# IMPORTANT:
# - Must be run as Administrator on AOVIA1VMS011.
# - Does NOT create databases - that is a manual DBA task.
# - Does NOT store secrets - connection strings and certificates
#   are provided via parameters or configured manually.
# - Does NOT modify the Test environment in any way.
#
# Usage:
#   .\setup-production-environment.ps1
#   .\setup-production-environment.ps1 -CertificateThumbprint "ABC123..."
#   .\setup-production-environment.ps1 -ConnectionString "Server=...;Database=Portal-Gerencial;..."
#   .\setup-production-environment.ps1 -CertificateThumbprint "ABC123..." -ConnectionString "Server=..."
#
# =============================================================================

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(HelpMessage = "SSL certificate thumbprint for HTTPS binding on portalgerencial.alpla.net")]
    [string]$CertificateThumbprint,

    [Parameter(HelpMessage = "Production SQL Server connection string (will NOT be stored in the repo)")]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

# =============================================================================
# Logging Helpers
# =============================================================================
function Log-Info {
    param([string]$Message)
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Write-Host "[INFO] $ts - $Message" -ForegroundColor Green
}

function Log-Warn {
    param([string]$Message)
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Write-Host "[WARN] $ts - $Message" -ForegroundColor Yellow
}

function Log-Error {
    param([string]$Message)
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Write-Host "[ERROR] $ts - $Message" -ForegroundColor Red
}

function Log-Manual {
    param([string]$Message)
    Write-Host "[MANUAL ACTION REQUIRED] $Message" -ForegroundColor Cyan
}

# =============================================================================
# Configuration Constants
# =============================================================================
$ProdRoot = "C:\Apps\AlplaPortal\Prod"
$ProdApiPath = "$ProdRoot\api"
$ProdWebPath = "$ProdRoot\web"
$ProdBackupsPath = "$ProdRoot\backups"
$ProdReleasesPath = "$ProdRoot\releases"
$ProdLogsPath = "$ProdRoot\logs"
$ProdUploadsPath = "$ProdRoot\uploads"
$ProdTempPath = "$ProdRoot\temp"

$ApiPoolName = "AlplaPortal-Prod-Api-Pool"
$WebPoolName = "AlplaPortal-Prod-Web-Pool"
$ApiSiteName = "AlplaPortal-Prod-Api"
$WebSiteName = "AlplaPortal-Prod-Web"
$ApiPort = 5002
$WebHostname = "portalgerencial.alpla.net"
$DatabaseName = "Portal-Gerencial"

# Collect manual actions to summarize at the end
$ManualActions = [System.Collections.Generic.List[string]]::new()

# =============================================================================
# 0. Precondition Checks
# =============================================================================
Log-Info "=========================================="
Log-Info "Production Environment Bootstrap Starting"
Log-Info "=========================================="

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Log-Error "This script must be run as Administrator."
    # exit 1
}

# Hostname check (informational)
$ComputerName = $env:COMPUTERNAME
if ($ComputerName -ne "AOVIA1VMS011") {
    Log-Warn "Running on '$ComputerName' - expected 'AOVIA1VMS011'. Proceeding anyway."
}

# =============================================================================
# 1. Create Required Folders
# =============================================================================
Log-Info "--- Step 1: Creating Production folder structure ---"

$folders = @(
    $ProdRoot, $ProdApiPath, $ProdWebPath, $ProdBackupsPath,
    $ProdReleasesPath, $ProdLogsPath, $ProdUploadsPath, $ProdTempPath
)

foreach ($folder in $folders) {
    if (-not (Test-Path $folder)) {
        if ($PSCmdlet.ShouldProcess($folder, "Create directory")) {
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            Log-Info "Created: $folder"
        }
    }
    else {
        Log-Info "Exists:  $folder"
    }
}

# =============================================================================
# 2. Validate Port 5002 Availability
# =============================================================================
Log-Info "--- Step 2: Validating port $ApiPort availability ---"

$portInUse = Get-NetTCPConnection -LocalPort $ApiPort -ErrorAction SilentlyContinue
if ($portInUse) {
    $processIds = $portInUse | Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($procId in $processIds) {
        try {
            $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
            $procName = $proc.ProcessName
            Log-Warn "Port $ApiPort is in use by process: $procName (PID: $procId)"
        }
        catch {
            Log-Warn "Port $ApiPort is in use by PID: $procId (process details unavailable)"
        }
    }
    # Don't fail - the port might be in use by the Prod API itself (script re-run)
    Log-Warn "Port $ApiPort is currently in use. If this is the Production API, this is expected on re-run."
}
else {
    Log-Info "Port $ApiPort is available."
}

# =============================================================================
# 3. IIS Module Import
# =============================================================================
Log-Info "--- Step 3: Loading IIS WebAdministration module ---"

try {
    Import-Module WebAdministration -ErrorAction Stop
    Log-Info "WebAdministration module loaded successfully."
}
catch {
    Log-Error "Failed to load WebAdministration module. Is IIS fully installed?"
    exit 1
}

# =============================================================================
# 4. Create/Validate IIS Application Pools
# =============================================================================
Log-Info "--- Step 4: Creating/validating IIS Application Pools ---"

function Provision-AppPool {
    param([string]$PoolName)

    if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
        if ($PSCmdlet.ShouldProcess($PoolName, "Create IIS App Pool")) {
            New-WebAppPool -Name $PoolName | Out-Null
            Log-Info "Created App Pool: $PoolName"
        }
    }
    else {
        Log-Info "App Pool exists: $PoolName"
    }

    if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
        Log-Warn "App Pool '$PoolName' does not exist (likely due to -WhatIf). Skipping configuration."
        return
    }

    # Configure: No Managed Code, Integrated Pipeline, ApplicationPoolIdentity
    Set-ItemProperty -Path "IIS:\AppPools\$PoolName" -Name "managedRuntimeVersion" -Value "" | Out-Null
    Set-ItemProperty -Path "IIS:\AppPools\$PoolName" -Name "managedPipelineMode" -Value 0 | Out-Null
    Set-ItemProperty -Path "IIS:\AppPools\$PoolName" -Name "processModel.identityType" -Value 4 | Out-Null
    Log-Info "App Pool configured: $PoolName (No Managed Code, Integrated, AppPoolIdentity)"
}

Provision-AppPool -PoolName $ApiPoolName
Provision-AppPool -PoolName $WebPoolName

# =============================================================================
# 5. Set App Pool Environment Variables
# =============================================================================
Log-Info "--- Step 5: Setting App Pool environment variables ---"

function Set-AppPoolEnvVar {
    param(
        [string]$PoolName,
        [string]$VarName,
        [string]$VarValue
    )

    $configPath = "system.applicationHost/applicationPools/add[@name='$PoolName']/environmentVariables"

    if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
        Log-Warn "App Pool '$PoolName' does not exist (likely due to -WhatIf). Skipping environment variable '$VarName'."
        return
    }

    try {
        # Check if the variable already exists
        $existing = Get-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" `
            -Filter "$configPath/add[@name='$VarName']" `
            -Name "value" -ErrorAction SilentlyContinue

        if ($null -ne $existing) {
            # Update existing
            Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" `
                -Filter "$configPath/add[@name='$VarName']" `
                -Name "value" -Value $VarValue
            Log-Info "Updated env var: $VarName=$VarValue (pool: $PoolName)"
        }
        else {
            # Add new
            Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" `
                -Filter "$configPath" `
                -Name "." `
                -Value @{name = $VarName; value = $VarValue }
            Log-Info "Added env var: $VarName=$VarValue (pool: $PoolName)"
        }
    }
    catch {
        Log-Warn "Could not set env var '$VarName' on pool '$PoolName' via WebConfiguration. Error: $_"
        Log-Manual "Manually set environment variable '$VarName=$VarValue' on App Pool '$PoolName' via IIS Manager > App Pool > Advanced Settings > Environment Variables."
        $ManualActions.Add("Set env var $VarName=$VarValue on pool $PoolName")
    }
}

Set-AppPoolEnvVar -PoolName $ApiPoolName -VarName "ASPNETCORE_ENVIRONMENT" -VarValue "Production"

# =============================================================================
# 6. Create/Validate IIS Sites
# =============================================================================
Log-Info "--- Step 6: Creating/validating IIS Sites ---"

# --- API Site (port 5002, no hostname) ---
if (-not (Test-Path "IIS:\Sites\$ApiSiteName")) {
    if ($PSCmdlet.ShouldProcess($ApiSiteName, "Create IIS Site on port $ApiPort")) {
        New-Website -Name $ApiSiteName `
            -PhysicalPath $ProdApiPath `
            -Port $ApiPort `
            -ApplicationPool $ApiPoolName | Out-Null
        Log-Info "Created IIS Site: $ApiSiteName (port $ApiPort)"
    }
}
else {
    Log-Info "IIS Site exists: $ApiSiteName"
    # Ensure correct physical path and pool
    Set-ItemProperty -Path "IIS:\Sites\$ApiSiteName" -Name "physicalPath" -Value $ProdApiPath | Out-Null
    Set-ItemProperty -Path "IIS:\Sites\$ApiSiteName" -Name "applicationPool" -Value $ApiPoolName | Out-Null
    Log-Info "Verified: $ApiSiteName -> $ProdApiPath (pool: $ApiPoolName)"
}

# --- Web Site (port 80 with hostname, HTTPS optional) ---
if (-not (Test-Path "IIS:\Sites\$WebSiteName")) {
    if ($PSCmdlet.ShouldProcess($WebSiteName, "Create IIS Site for $WebHostname")) {
        New-Website -Name $WebSiteName `
            -PhysicalPath $ProdWebPath `
            -Port 80 `
            -HostHeader $WebHostname `
            -ApplicationPool $WebPoolName | Out-Null
        Log-Info "Created IIS Site: $WebSiteName (hostname: $WebHostname)"
    }
}
else {
    Log-Info "IIS Site exists: $WebSiteName"
    Set-ItemProperty -Path "IIS:\Sites\$WebSiteName" -Name "physicalPath" -Value $ProdWebPath | Out-Null
    Set-ItemProperty -Path "IIS:\Sites\$WebSiteName" -Name "applicationPool" -Value $WebPoolName | Out-Null
    Log-Info "Verified: $WebSiteName -> $ProdWebPath (pool: $WebPoolName)"
}

# HTTP binding (ensure it exists)
if (-not (Test-Path "IIS:\Sites\$WebSiteName")) {
    Log-Warn "IIS Site '$WebSiteName' does not exist (likely due to -WhatIf). Skipping HTTP binding configuration."
}
else {
    $httpBinding = Get-WebBinding -Name $WebSiteName -Protocol "http" -Port 80 -ErrorAction SilentlyContinue
    if ($null -eq $httpBinding) {
        if ($PSCmdlet.ShouldProcess("$WebSiteName HTTP:80", "Create HTTP binding")) {
            New-WebBinding -Name $WebSiteName -IPAddress "*" -Port 80 -Protocol "http" -HostHeader $WebHostname | Out-Null
            Log-Info "Added HTTP binding: *:80:$WebHostname"
        }
    }
    else {
        Log-Info "HTTP binding exists: *:80:$WebHostname"
    }
}

# =============================================================================
# 7. HTTPS Binding (Certificate)
# =============================================================================
Log-Info "--- Step 7: HTTPS binding configuration ---"

if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Log-Warn "No -CertificateThumbprint provided. Skipping HTTPS binding."
    Log-Manual "Install the SSL certificate for '$WebHostname' and bind it to IIS site '$WebSiteName' on port 443."
    Log-Manual "Command example: New-WebBinding -Name '$WebSiteName' -IPAddress '*' -Port 443 -Protocol 'https' -HostHeader '$WebHostname' -SslFlags 1"
    $ManualActions.Add("Install SSL certificate and create HTTPS binding for $WebSiteName on port 443")
}
else {
    # Verify certificate exists in the local machine store
    $cert = Get-ChildItem -Path "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if ($null -eq $cert) {
        Log-Error "Certificate with thumbprint '$CertificateThumbprint' not found in Cert:\LocalMachine\My."
        Log-Manual "Import the certificate first, then re-run this script with the correct thumbprint."
        $ManualActions.Add("Import SSL certificate with thumbprint $CertificateThumbprint")
    }
    else {
        $certSubject = $cert.Subject
        $certExpires = $cert.NotAfter
        Log-Info "Certificate found: Subject=$certSubject, Expires=$certExpires"

        if (-not (Test-Path "IIS:\Sites\$WebSiteName")) {
            Log-Warn "IIS Site '$WebSiteName' does not exist (likely due to -WhatIf). Skipping HTTPS binding configuration."
        }
        else {
            # Check if HTTPS binding already exists
            $httpsBinding = Get-WebBinding -Name $WebSiteName -Protocol "https" -Port 443 -ErrorAction SilentlyContinue
            if ($null -eq $httpsBinding) {
                if ($PSCmdlet.ShouldProcess("$WebSiteName HTTPS:443", "Create HTTPS binding")) {
                    New-WebBinding -Name $WebSiteName -IPAddress "*" -Port 443 -Protocol "https" -HostHeader $WebHostname -SslFlags 1 | Out-Null
                    Log-Info "Created HTTPS binding: *:443:$WebHostname (SNI)"
                }
            }
            else {
                Log-Info "HTTPS binding already exists on $WebSiteName."
            }

            # Bind the certificate
            try {
                $httpsBinding = Get-WebBinding -Name $WebSiteName -Protocol "https" -Port 443
                if ($null -ne $httpsBinding) {
                    $httpsBinding.AddSslCertificate($CertificateThumbprint, "My")
                    Log-Info "SSL certificate bound to $WebSiteName (thumbprint: $CertificateThumbprint)"
                }
            }
            catch {
                Log-Warn "Could not bind certificate: $_"
                Log-Manual "Manually bind certificate '$CertificateThumbprint' to site '$WebSiteName' port 443 via IIS Manager."
                $ManualActions.Add("Bind SSL certificate to $WebSiteName")
            }
        }
    }
}

# =============================================================================
# 8. Create Production Frontend web.config (Reverse Proxy to port 5002)
# =============================================================================
Log-Info "--- Step 8: Creating Production frontend web.config ---"

$prodWebConfig = Join-Path $ProdWebPath "web.config"

# Only create if it does not already exist - never overwrite a manually configured file
if (-not (Test-Path $prodWebConfig)) {
    if ($PSCmdlet.ShouldProcess($prodWebConfig, "Create Production web.config")) {
        $webConfigContent = @'
<?xml version="1.0" encoding="UTF-8"?>
<!--
  IIS web.config for the Alpla Portal Gerencial PRODUCTION frontend (React SPA).

  This file provides three essential rules:
  0. HTTP to HTTPS redirect (301 Permanent)
  1. Reverse Proxy: Routes /api/* requests to the backend API on localhost:5002.
  2. SPA Fallback: All other non-file requests are rewritten to index.html.

  IMPORTANT: This file is PRODUCTION-specific (port 5002).
  Do NOT replace it with the repository version (port 5001 = Test).
  The CI/CD workflow preserves this file during deployment.

  Prerequisites on the IIS server:
  - IIS URL Rewrite Module v2.1
  - IIS Application Request Routing (ARR) 3.0
  - ARR proxy feature must be enabled
-->
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <!-- Rule 0: HTTP to HTTPS redirect -->
        <rule name="HTTPS-Redirect" stopProcessing="true">
          <match url="(.*)" />
          <conditions>
            <add input="{HTTPS}" pattern="off" ignoreCase="true" />
          </conditions>
          <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
        </rule>

        <!-- Rule 1: Reverse Proxy for API calls (PRODUCTION = port 5002) -->
        <rule name="ReverseProxy-API" stopProcessing="true">
          <match url="^api/(.*)" />
          <action type="Rewrite" url="http://localhost:5002/api/{R:1}" />
        </rule>

        <!-- Rule 2: SPA Fallback (React Router) -->
        <rule name="SPA-Fallback" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
'@
        Set-Content -Path $prodWebConfig -Value $webConfigContent -Encoding UTF8
        Log-Info "Created Production web.config with reverse proxy to localhost:$ApiPort"
    }
}
else {
    Log-Info "Production web.config already exists. Verifying port reference..."
    $existingContent = Get-Content $prodWebConfig -Raw
    if ($existingContent -match "localhost:5001") {
        Log-Warn "WARNING: Production web.config references port 5001 (Test port)!"
        Log-Manual "Update $prodWebConfig to use port 5002 instead of 5001."
        $ManualActions.Add("Fix web.config port: change 5001 to 5002 in $prodWebConfig")
    }
    elseif ($existingContent -match "localhost:$ApiPort") {
        Log-Info "Production web.config correctly references port $ApiPort."
    }
    else {
        Log-Warn "Production web.config does not contain expected reverse proxy rule."
    }
}

# =============================================================================
# 9. NTFS Permissions
# =============================================================================
Log-Info "--- Step 9: Configuring NTFS permissions ---"

function Grant-FolderPermission {
    param(
        [string]$Path,
        [string]$Identity,
        [string]$Rights
    )

    if (-not (Test-Path $Path)) {
        Log-Warn "Skipping permission for non-existent path: $Path"
        return
    }

    try {
        $acl = Get-Acl $Path
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $Identity,
            $Rights,
            "ContainerInherit, ObjectInherit",
            "None",
            "Allow"
        )
        $acl.AddAccessRule($accessRule)
        Set-Acl $Path $acl
        Log-Info "Granted [$Rights] to '$Identity' on '$Path'"
    }
    catch {
        Log-Warn "Could not set permissions on '$Path' for '$Identity': $_"
        Log-Manual "Grant $Rights permission to '$Identity' on '$Path'."
        $ManualActions.Add("Grant $Rights to $Identity on $Path")
    }
}

$apiPoolIdentity = "IIS AppPool\$ApiPoolName"

# API folder: ReadAndExecute
Grant-FolderPermission -Path $ProdApiPath -Identity $apiPoolIdentity -Rights "ReadAndExecute"

# Writable folders: Modify
Grant-FolderPermission -Path $ProdLogsPath    -Identity $apiPoolIdentity -Rights "Modify"
Grant-FolderPermission -Path $ProdUploadsPath -Identity $apiPoolIdentity -Rights "Modify"
Grant-FolderPermission -Path $ProdTempPath    -Identity $apiPoolIdentity -Rights "Modify"
Grant-FolderPermission -Path $ProdBackupsPath -Identity $apiPoolIdentity -Rights "Modify"

# Web folder: ReadAndExecute for IIS_IUSRS
Grant-FolderPermission -Path $ProdWebPath -Identity "IIS_IUSRS" -Rights "ReadAndExecute"

# =============================================================================
# 10. Database Validation
# =============================================================================
Log-Info "--- Step 10: Validating Production database ---"

if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
        $conn.Open()

        # Verify database name
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT DB_NAME() AS DatabaseName"
        $reader = $cmd.ExecuteReader()
        if ($reader.Read()) {
            $dbName = $reader["DatabaseName"]
            if ($dbName -eq $DatabaseName) {
                Log-Info "Database validation PASSED: connected to [$dbName]"
            }
            elseif ($dbName -eq "Portal-Gerencial-Test") {
                Log-Error "CRITICAL: Connection string resolves to TEST database [$dbName]! This is NOT allowed for Production."
                $conn.Close()
                exit 1
            }
            else {
                Log-Warn "Connected to database [$dbName] - expected [$DatabaseName]."
            }
        }
        $reader.Close()
        $conn.Close()
    }
    catch {
        Log-Warn "Could not validate database connection: $_"
        Log-Manual "Verify the Production connection string manually."
        $ManualActions.Add("Verify Production database connection")
    }
}
else {
    Log-Warn "No -ConnectionString provided. Skipping database validation."
    Log-Manual "Create the database [$DatabaseName] if it does not exist."
    Log-Manual "Configure appsettings.Production.json on the server with the correct connection string."
    $ManualActions.Add("Create database [$DatabaseName] if needed")
    $ManualActions.Add("Configure appsettings.Production.json with connection string")
}

# =============================================================================
# 11. Summary
# =============================================================================
Write-Host ""
Log-Info "=========================================="
Log-Info "Production Environment Bootstrap Complete"
Log-Info "=========================================="
Write-Host ""
Write-Host "Production Environment Configuration:" -ForegroundColor White
Write-Host "  Root:       $ProdRoot"
Write-Host "  API Path:   $ProdApiPath"
Write-Host "  Web Path:   $ProdWebPath"
Write-Host "  API Port:   $ApiPort"
Write-Host "  Web URL:    https://$WebHostname"
Write-Host "  Database:   [$DatabaseName]"
Write-Host "  API Pool:   $ApiPoolName"
Write-Host "  Web Pool:   $WebPoolName"
Write-Host "  API Site:   $ApiSiteName"
Write-Host "  Web Site:   $WebSiteName"
Write-Host "  ASP.NET:    Production"
Write-Host ""

if ($ManualActions.Count -gt 0) {
    Write-Host "============================================" -ForegroundColor Cyan
    $manualCount = $ManualActions.Count
    Write-Host "  MANUAL ACTIONS REQUIRED ($manualCount)" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    $i = 1
    foreach ($action in $ManualActions) {
        Write-Host "  $i. $action" -ForegroundColor Cyan
        $i++
    }
    Write-Host ""
}

Write-Host "Next Steps:" -ForegroundColor White
Write-Host "  1. Create/verify appsettings.Production.json in $ProdApiPath"
Write-Host "  2. Configure GitHub environment 'production' with required variables and secrets"
Write-Host "  3. Run the first Production deployment via GitHub Actions"
Write-Host "  4. Validate using validate-production-environment.ps1"
Write-Host ""
