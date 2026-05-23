# ==========================================================================================
# Orchestration Script: Phase 1 Server Preparation on AOVIA1VMS011
# Application: Alpla Angola - Portal Gerencial
# Date: May 23, 2026
# Security: Highly Secure, Idempotent, and Production-Ready
# ==========================================================================================

$ErrorActionPreference = "Stop"

# Log helper
function Log-Info {
    param([string]$Message)
    Write-Host "[INFO] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message" -ForegroundColor Green
}

function Log-Warn {
    param([string]$Message)
    Write-Host "[WARN] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message" -ForegroundColor Yellow
}

function Log-Error {
    param([string]$Message)
    Write-Host "[ERROR] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message" -ForegroundColor Red
}

Log-Info "Starting Phase 1 Server Preparation on AOVIA1VMS011..."

# ==========================================================================================
# 1. Host Validation
# ==========================================================================================
$ComputerName = $env:COMPUTERNAME
if ($ComputerName -ne "AOVIA1VMS011") {
    Log-Warn "Script is running on ${ComputerName}, but is configured for server AOVIA1VMS011. Proceeding under assumption of remote setup."
}

# ==========================================================================================
# 2. Windows Web Server (IIS) Features Enablement
# ==========================================================================================
Log-Info "Enabling IIS Web Server Role and sub-features..."
try {
    Import-Module ServerManager
    
    # Feature checklist
    $Features = @("Web-Server", "Web-WebServer", "Web-Common-Http", "Web-Static-Content", 
                  "Web-Default-Doc", "Web-Http-Errors", "Web-Http-Redirect", "Web-Performance", 
                  "Web-Stat-Compression", "Web-Security", "Web-Filtering", "Web-Windows-Auth", 
                  "Web-App-Dev", "Web-Net-Ext45", "Web-Asp-Net45", "Web-WebSockets", 
                  "Web-Mgmt-Tools", "Web-Mgmt-Console")

    foreach ($Feature in $Features) {
        $State = Get-WindowsFeature -Name $Feature
        if ($State.InstallState -ne "Installed") {
            Log-Info "Installing Windows Feature: ${Feature}..."
            Install-WindowsFeature -Name $Feature -IncludeManagementTools | Out-Null
            Log-Info "Successfully installed feature: ${Feature}"
        } else {
            Log-Info "Windows Feature already installed: ${Feature}"
        }
    }
}
catch {
    Log-Error "Failed to install IIS features: $_"
    throw
}

# ==========================================================================================
# 3. IIS URL Rewrite Module Installation (Offline MSI)
# ==========================================================================================
Log-Info "Verifying IIS URL Rewrite Module installation..."
$UrlRewriteKey = "HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite"
if (-not (Test-Path $UrlRewriteKey)) {
    Log-Info "URL Rewrite module not detected. Performing local offline installation..."
    $MsiPath = "C:\temp\rewrite_amd64_en-US.msi"
    if (-not (Test-Path $MsiPath)) {
        Log-Error "Critical URL Rewrite installer MSI was not found at: ${MsiPath}"
        Log-Error "Offline setup requires the MSI to be pre-positioned. Script halted."
        exit 1
    }
    
    try {
        Log-Info "Executing MSI installer: ${MsiPath}..."
        $Process = Start-Process -FilePath "msiexec.exe" -ArgumentList "/i `"${MsiPath}`" /qn /norestart" -Wait -NoNewWindow -PassThru
        if ($Process.ExitCode -eq 0) {
            Log-Info "URL Rewrite Module successfully installed."
        } else {
            Log-Error "URL Rewrite Module installation failed with exit code: $($Process.ExitCode)"
            exit 1
        }
    }
    catch {
        Log-Error "Error executing URL Rewrite Module installer: $_"
        throw
    }
} else {
    Log-Info "URL Rewrite Module is already installed. Skipping."
}

# ==========================================================================================
# 4. Verify ASP.NET Core Hosting Bundle
# ==========================================================================================
Log-Info "Checking ASP.NET Core IIS Module (ANCM)..."
$AncmKey = "HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2"
if (-not (Test-Path $AncmKey)) {
    Log-Warn "ASP.NET Core Module registry key not detected. ASP.NET Core Hosting Bundle may need to be re-run/repaired after IIS installation."
} else {
    Log-Info "ASP.NET Core IIS Module registry configuration is active."
}

# ==========================================================================================
# 5. Create Local isolated App Pools & Sites (Unified Reverse Proxy SNI-based Site Setup)
# ==========================================================================================
Log-Info "Loading IIS administrative module..."
try {
    Import-Module WebAdministration
}
catch {
    Log-Error "Failed to load WebAdministration module. Please verify IIS is fully installed."
    throw
}

# Helper to configure app pools safely
function Provision-AppPool {
    param([string]$PoolName)
    if (-not (Test-Path "IIS:\AppPools\${PoolName}")) {
        Log-Info "Creating Application Pool: ${PoolName}..."
        New-WebAppPool -Name $PoolName | Out-Null
    } else {
        Log-Info "Application Pool already exists: ${PoolName}"
    }
    
    # Configure pool settings: Integrated Pipeline, No Managed Code, ApplicationPoolIdentity
    Set-ItemProperty -Path "IIS:\AppPools\${PoolName}" -Name "managedRuntimeVersion" -Value "" | Out-Null
    Set-ItemProperty -Path "IIS:\AppPools\${PoolName}" -Name "managedPipelineMode" -Value 0 | Out-Null # Integrated
    Set-ItemProperty -Path "IIS:\AppPools\${PoolName}" -Name "processModel.identityType" -Value 4 | Out-Null # ApplicationPoolIdentity
    Log-Info "App Pool configuration verified: ${PoolName}"
}

# Helper to configure websites safely
function Provision-Website {
    param(
        [string]$SiteName,
        [string]$PhysicalPath,
        [string]$HostHeader,
        [string]$AppPool,
        [string]$ApiPhysicalPath,
        [string]$ApiAppPool
    )

    if (-not (Test-Path $PhysicalPath)) {
        Log-Warn "Physical root path for ${SiteName} does not exist yet: ${PhysicalPath}. Creating it..."
        New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
    }

    # Provision frontend pool
    Provision-AppPool -PoolName $AppPool

    # Provision website
    if (-not (Test-Path "IIS:\Sites\${SiteName}")) {
        Log-Info "Creating Website: ${SiteName}..."
        New-Website -Name $SiteName -PhysicalPath $PhysicalPath -Port 80 -HostHeader $HostHeader -ApplicationPool $AppPool | Out-Null
    } else {
        Log-Info "Website already exists: ${SiteName}. Updating physical path and pool..."
        Set-ItemProperty -Path "IIS:\Sites\${SiteName}" -Name "physicalPath" -Value $PhysicalPath | Out-Null
        Set-ItemProperty -Path "IIS:\Sites\${SiteName}" -Name "applicationPool" -Value $AppPool | Out-Null
    }

    # Bind HTTP 80
    $HttpBinding = Get-WebBinding -Name $SiteName -Protocol "http" -Port 80
    if ($null -eq $HttpBinding) {
        New-WebBinding -Name $SiteName -IPAddress "*" -Port 80 -Protocol "http" -HostHeader $HostHeader | Out-Null
        Log-Info "Added HTTP binding for ${SiteName} on port 80"
    }

    # Provision API backend pool
    Provision-AppPool -PoolName $ApiAppPool

    # Provision API sub-application
    $ApiAppPath = "IIS:\Sites\${SiteName}\api"
    if (-not (Test-Path $ApiAppPath)) {
        Log-Info "Creating API Sub-Application /api inside site ${SiteName}..."
        New-WebApplication -Site $SiteName -Name "api" -PhysicalPath $ApiPhysicalPath -ApplicationPool $ApiAppPool | Out-Null
    } else {
        Log-Info "API Sub-Application /api already exists inside site ${SiteName}. Updating path and pool..."
        Set-ItemProperty -Path "$ApiAppPath" -Name "physicalPath" -Value $ApiPhysicalPath | Out-Null
        Set-ItemProperty -Path "$ApiAppPath" -Name "applicationPool" -Value $ApiAppPool | Out-Null
    }
}

# Provision Production Site & API App Pool
Provision-Website -SiteName "PortalGerencial.Production" `
                  -PhysicalPath "D:\PortalGerencial\Frontend" `
                  -HostHeader "portalangola.alpla.com" `
                  -AppPool "PortalGerencialAppPool" `
                  -ApiPhysicalPath "D:\PortalGerencial\Api" `
                  -ApiAppPool "PortalGerencialApiPool"

# Provision Test Site & API App Pool
Provision-Website -SiteName "PortalGerencial.Test" `
                  -PhysicalPath "D:\PortalGerencial-Test\Frontend" `
                  -HostHeader "portalangola-test.alpla.com" `
                  -AppPool "PortalGerencialTestAppPool" `
                  -ApiPhysicalPath "D:\PortalGerencial-Test\Api" `
                  -ApiAppPool "PortalGerencialTestApiPool"

# ==========================================================================================
# 6. Secure SSL Certificate Import & Binding (Prompting passwords securely)
# ==========================================================================================
Log-Info "Executing secure SSL Certificate import phase..."

function Bind-SSLCertificate {
    param(
        [string]$SiteName,
        [string]$HostHeader,
        [string]$PfxPath,
        [string]$FriendlyPrompt
    )

    if (-not (Test-Path $PfxPath)) {
        Log-Error "SSL certificate file not found at: ${PfxPath}"
        exit 1
    }

    # Prompt securely for the PFX password
    Log-Info "Secure prompt: Entering credentials for ${FriendlyPrompt}..."
    $Password = Read-Host -Prompt "Enter password for certificate (${FriendlyPrompt})" -AsSecureString
    
    try {
        Log-Info "Importing certificate into Local Machine personal store..."
        # Import to Personal store of LocalMachine
        $Cert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation "Cert:\LocalMachine\My" -Password $Password -Exportable
        $Thumbprint = $Cert.Thumbprint
        Log-Info "Certificate successfully imported. Subject: $($Cert.Subject) | Thumbprint: ${Thumbprint} | Expiration: $($Cert.NotAfter)"
        
        # IIS Binding HTTPS on port 443 with SNI
        $HttpsBinding = Get-WebBinding -Name $SiteName -Protocol "https" -Port 443 -HostHeader $HostHeader
        if ($null -eq $HttpsBinding) {
            Log-Info "Creating HTTPS binding for ${SiteName} on port 443 with SNI..."
            New-WebBinding -Name $SiteName -IPAddress "*" -Port 443 -Protocol "https" -HostHeader $HostHeader -SslFlags 1 | Out-Null
            $HttpsBinding = Get-WebBinding -Name $SiteName -Protocol "https" -Port 443 -HostHeader $HostHeader
        } else {
            Log-Info "HTTPS binding already exists for ${SiteName}. Re-binding certificate..."
        }

        # Bind certificate to IIS website
        $HttpsBinding.AddSslCertificate($Thumbprint, "My")
        Log-Info "Certificate successfully bound to IIS Site: ${SiteName} ($($HostHeader):443)"
    }
    catch {
        Log-Error "Failed to import or bind certificate for $($SiteName): $_"
        exit 1
    }
}

# Bind Production PFX
Bind-SSLCertificate -SiteName "PortalGerencial.Production" `
                    -HostHeader "portalangola.alpla.com" `
                    -PfxPath "C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx" `
                    -FriendlyPrompt "Production SSL portalangola.alpla.com"

# Bind Test PFX
Bind-SSLCertificate -SiteName "PortalGerencial.Test" `
                    -HostHeader "portalangola-test.alpla.com" `
                    -PfxPath "C:\dev\alpla-portal\334ad6893b414f90a349c960c5e45af4.pfx" `
                    -FriendlyPrompt "Test/Staging SSL portalangola-test.alpla.com"

# ==========================================================================================
# 7. Configure NTFS Directory ACLs for dynamic App Pool Identities
# ==========================================================================================
Log-Info "Configuring NTFS security access permissions on folders..."

function Apply-NTFSPermissions {
    param(
        [string]$Path,
        [string]$Identity,
        [string]$Rights,
        [bool]$ProtectHeritage = $false
    )

    if (-not (Test-Path $Path)) {
        Log-Warn "Skipping NTFS permission setting. Folder path not found: ${Path}"
        return
    }

    Log-Info "Applying security rule [${Rights}] to ${Path} for: ${Identity}..."
    $Acl = Get-Acl $Path
    
    if ($ProtectHeritage) {
        # Break inheritance, preserve existing permissions as explicit
        $Acl.SetAccessRuleProtection($true, $true)
    }

    $AccessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $Identity,
        $Rights,
        "ContainerInherit, ObjectInherit",
        "None",
        "Allow"
    )
    
    $Acl.AddAccessRule($AccessRule)
    Set-Acl $Path $Acl
    Log-Info "Security rule applied successfully."
}

# Production Environment Permissions
Apply-NTFSPermissions -Path "D:\PortalGerencial\Frontend" -Identity "IIS_IUSRS" -Rights "ReadAndExecute" -ProtectHeritage $true
Apply-NTFSPermissions -Path "D:\PortalGerencial\Api" -Identity "IIS APPPOOL\PortalGerencialApiPool" -Rights "ReadAndExecute" -ProtectHeritage $true
Apply-NTFSPermissions -Path "D:\PortalGerencial\Logs" -Identity "IIS APPPOOL\PortalGerencialApiPool" -Rights "Modify"
Apply-NTFSPermissions -Path "D:\PortalGerencial\Attachments" -Identity "IIS APPPOOL\PortalGerencialApiPool" -Rights "Modify"
Apply-NTFSPermissions -Path "D:\PortalGerencial\Temp" -Identity "IIS APPPOOL\PortalGerencialApiPool" -Rights "Modify"

# Test/Staging Environment Permissions
Apply-NTFSPermissions -Path "D:\PortalGerencial-Test\Frontend" -Identity "IIS_IUSRS" -Rights "ReadAndExecute" -ProtectHeritage $true
Apply-NTFSPermissions -Path "D:\PortalGerencial-Test\Api" -Identity "IIS APPPOOL\PortalGerencialTestApiPool" -Rights "ReadAndExecute" -ProtectHeritage $true
Apply-NTFSPermissions -Path "D:\PortalGerencial-Test\Logs" -Identity "IIS APPPOOL\PortalGerencialTestApiPool" -Rights "Modify"
Apply-NTFSPermissions -Path "D:\PortalGerencial-Test\Attachments" -Identity "IIS APPPOOL\PortalGerencialTestApiPool" -Rights "Modify"
Apply-NTFSPermissions -Path "D:\PortalGerencial-Test\Temp" -Identity "IIS APPPOOL\PortalGerencialTestApiPool" -Rights "Modify"

# ==========================================================================================
# 8. Firewall Inbound Openings
# ==========================================================================================
Log-Info "Configuring Windows Defender Firewall rules..."
try {
    # Check if HTTP inbound rule exists, if not create
    $HttpRule = Get-NetFirewallRule -Name "PortalGerencial-Inbound-HTTP" -ErrorAction SilentlyContinue
    if ($null -eq $HttpRule) {
        Log-Info "Creating firewall rule: Allow HTTP 80 Inbound..."
        New-NetFirewallRule -DisplayName "Portal Gerencial Inbound (HTTP)" `
                            -Name "PortalGerencial-Inbound-HTTP" `
                            -Direction Inbound `
                            -Action Allow `
                            -Protocol TCP `
                            -LocalPort 80 `
                            -Enabled True | Out-Null
    } else {
        Log-Info "Firewall rule already active: Portal Gerencial Inbound (HTTP)"
    }

    # Check if HTTPS inbound rule exists, if not create
    $HttpsRule = Get-NetFirewallRule -Name "PortalGerencial-Inbound-HTTPS" -ErrorAction SilentlyContinue
    if ($null -eq $HttpsRule) {
        Log-Info "Creating firewall rule: Allow HTTPS 443 Inbound..."
        New-NetFirewallRule -DisplayName "Portal Gerencial Inbound (HTTPS)" `
                            -Name "PortalGerencial-Inbound-HTTPS" `
                            -Direction Inbound `
                            -Action Allow `
                            -Protocol TCP `
                            -LocalPort 443 `
                            -Enabled True | Out-Null
    } else {
        Log-Info "Firewall rule already active: Portal Gerencial Inbound (HTTPS)"
    }
}
catch {
    Log-Warn "Failed to apply Windows Defender Firewall rules. Verify PowerShell has Administrator access or check system policies: $_"
}

Log-Info "=========================================================================================="
Log-Info "Phase 1 Server Preparation has completed successfully."
Log-Info "IIS service enabled, local App Pools and Sites prepared, SSL PFX certs bound via SNI,"
Log-Info "NTFS ACL folder security permissions applied, and Firewall openings created."
Log-Info "=========================================================================================="
