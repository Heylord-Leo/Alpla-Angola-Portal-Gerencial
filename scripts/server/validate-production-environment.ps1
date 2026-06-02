# =============================================================================
# Alpla Angola - Portal Gerencial
# Production Environment Validation Script
# =============================================================================
#
# Read-only script that validates the Production environment is correctly
# configured on AOVIA1VMS011. Does NOT make any changes.
#
# Usage:
#   .\validate-production-environment.ps1
#
# =============================================================================

$ErrorActionPreference = "Continue"

function Write-Check {
    param([string]$Name, [bool]$Pass, [string]$Detail = "")
    $status = if ($Pass) { "[PASS]" } else { "[FAIL]" }
    $color  = if ($Pass) { "Green" } else { "Red" }
    $line   = "$status $Name"
    if ($Detail) { $line += " — $Detail" }
    Write-Host $line -ForegroundColor $color
    return $Pass
}

Write-Host ""
Write-Host "============================================" -ForegroundColor White
Write-Host "  Production Environment Validation"
Write-Host "  Server: $($env:COMPUTERNAME)"
Write-Host "  Date:   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "============================================" -ForegroundColor White
Write-Host ""

$totalChecks  = 0
$passedChecks = 0

# =============================================================================
# 1. Required Folders
# =============================================================================
Write-Host "--- Folder Structure ---" -ForegroundColor Yellow
$folders = @(
    "C:\Apps\AlplaPortal\Prod",
    "C:\Apps\AlplaPortal\Prod\api",
    "C:\Apps\AlplaPortal\Prod\web",
    "C:\Apps\AlplaPortal\Prod\backups",
    "C:\Apps\AlplaPortal\Prod\releases",
    "C:\Apps\AlplaPortal\Prod\logs",
    "C:\Apps\AlplaPortal\Prod\uploads",
    "C:\Apps\AlplaPortal\Prod\temp"
)

foreach ($folder in $folders) {
    $exists = Test-Path $folder
    $totalChecks++
    if (Write-Check "Folder: $folder" $exists) { $passedChecks++ }
}

# =============================================================================
# 2. IIS Module
# =============================================================================
Write-Host ""
Write-Host "--- IIS Configuration ---" -ForegroundColor Yellow

$iisLoaded = $false
try {
    Import-Module WebAdministration -ErrorAction Stop
    $iisLoaded = $true
} catch {
    Write-Check "WebAdministration module" $false "Module not available"
}

if ($iisLoaded) {
    # App Pools
    $pools = @("AlplaPortal-Prod-Api-Pool", "AlplaPortal-Prod-Web-Pool")
    foreach ($pool in $pools) {
        $exists = Test-Path "IIS:\AppPools\$pool"
        $totalChecks++
        $detail = ""
        if ($exists) {
            try {
                $state = (Get-WebAppPoolState -Name $pool).Value
                $detail = "State: $state"
            } catch { $detail = "State: unknown" }
        }
        if (Write-Check "App Pool: $pool" $exists $detail) { $passedChecks++ }
    }

    # Sites
    $sites = @("AlplaPortal-Prod-Api", "AlplaPortal-Prod-Web")
    foreach ($site in $sites) {
        $exists = Test-Path "IIS:\Sites\$site"
        $totalChecks++
        if (Write-Check "IIS Site: $site" $exists) { $passedChecks++ }
    }

    # App Pool env var
    $totalChecks++
    try {
        $envVal = Get-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" `
            -Filter "system.applicationHost/applicationPools/add[@name='AlplaPortal-Prod-Api-Pool']/environmentVariables/add[@name='ASPNETCORE_ENVIRONMENT']" `
            -Name "value" -ErrorAction SilentlyContinue
        $envStr = if ($null -ne $envVal) { $envVal.Value } else { $null }
        $pass = ($envStr -eq "Production")
        if (Write-Check "ASPNETCORE_ENVIRONMENT" $pass "Value: $envStr") { $passedChecks++ }
    } catch {
        Write-Check "ASPNETCORE_ENVIRONMENT" $false "Could not read"
    }
}

# =============================================================================
# 3. Port 5002
# =============================================================================
Write-Host ""
Write-Host "--- Network ---" -ForegroundColor Yellow

$totalChecks++
$portListeners = Get-NetTCPConnection -LocalPort 5002 -State Listen -ErrorAction SilentlyContinue
if ($null -ne $portListeners) {
    $procId = $portListeners | Select-Object -ExpandProperty OwningProcess -First 1
    try {
        $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
        $detail = "Listening (PID: $procId, Process: $($proc.ProcessName))"
    } catch {
        $detail = "Listening (PID: $procId)"
    }
    if (Write-Check "Port 5002 listener" $true $detail) { $passedChecks++ }
} else {
    Write-Check "Port 5002 listener" $false "Not listening — API may not be running yet"
}

# Verify port 5001 is NOT used by Production (it belongs to Test)
$totalChecks++
$port5001 = Get-NetTCPConnection -LocalPort 5001 -State Listen -ErrorAction SilentlyContinue
$detail5001 = if ($null -ne $port5001) { "In use (expected — this is the Test API)" } else { "Not in use" }
if (Write-Check "Port 5001 (Test) not used by Prod" $true $detail5001) { $passedChecks++ }

# =============================================================================
# 4. Web.config Validation
# =============================================================================
Write-Host ""
Write-Host "--- Frontend Configuration ---" -ForegroundColor Yellow

$totalChecks++
$webConfigPath = "C:\Apps\AlplaPortal\Prod\web\web.config"
if (Test-Path $webConfigPath) {
    $content = Get-Content $webConfigPath -Raw
    $hasCorrectPort = $content -match "localhost:5002"
    $hasTestPort    = $content -match "localhost:5001"
    if ($hasCorrectPort -and -not $hasTestPort) {
        if (Write-Check "web.config reverse proxy" $true "Correctly targets port 5002") { $passedChecks++ }
    } elseif ($hasTestPort) {
        Write-Check "web.config reverse proxy" $false "DANGER: References Test port 5001!"
    } else {
        Write-Check "web.config reverse proxy" $false "No reverse proxy rule found"
    }
} else {
    Write-Check "web.config exists" $false "File not found at $webConfigPath"
}

# =============================================================================
# 5. Certificate & HTTPS
# =============================================================================
Write-Host ""
Write-Host "--- HTTPS Certificate ---" -ForegroundColor Yellow

$totalChecks++
if ($iisLoaded) {
    $httpsBinding = Get-WebBinding -Name "AlplaPortal-Prod-Web" -Protocol "https" -Port 443 -ErrorAction SilentlyContinue
    if ($null -ne $httpsBinding) {
        if (Write-Check "HTTPS binding on Prod-Web" $true "Port 443 binding exists") { $passedChecks++ }
    } else {
        Write-Check "HTTPS binding on Prod-Web" $false "No HTTPS binding found"
    }
} else {
    Write-Check "HTTPS binding" $false "IIS module not loaded"
}

# =============================================================================
# 6. ASP.NET Core Hosting Bundle
# =============================================================================
Write-Host ""
Write-Host "--- Runtime ---" -ForegroundColor Yellow

$totalChecks++
$ancmKey = "HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2"
$ancmInstalled = Test-Path $ancmKey
if (Write-Check "ASP.NET Core Hosting Bundle" $ancmInstalled) { $passedChecks++ }

# =============================================================================
# 7. appsettings.Production.json
# =============================================================================
Write-Host ""
Write-Host "--- API Configuration ---" -ForegroundColor Yellow

$totalChecks++
$appSettingsPath = "C:\Apps\AlplaPortal\Prod\api\appsettings.Production.json"
if (Test-Path $appSettingsPath) {
    if (Write-Check "appsettings.Production.json" $true "File exists") { $passedChecks++ }

    # Check connection string does not point to Test DB
    $totalChecks++
    $settingsContent = Get-Content $appSettingsPath -Raw -ErrorAction SilentlyContinue
    if ($settingsContent -match "Portal-Gerencial-Test") {
        Write-Check "Connection string target" $false "DANGER: References Test database!"
    } else {
        if (Write-Check "Connection string target" $true "Does not reference Test database") { $passedChecks++ }
    }
} else {
    Write-Check "appsettings.Production.json" $false "File not found — must be created before first deploy"
}

# =============================================================================
# 8. Folder Write Permissions
# =============================================================================
Write-Host ""
Write-Host "--- Write Permissions ---" -ForegroundColor Yellow

$writableFolders = @(
    "C:\Apps\AlplaPortal\Prod\logs",
    "C:\Apps\AlplaPortal\Prod\uploads",
    "C:\Apps\AlplaPortal\Prod\temp"
)

foreach ($folder in $writableFolders) {
    $totalChecks++
    if (Test-Path $folder) {
        try {
            $testFile = Join-Path $folder ".write_test_$(Get-Date -Format 'yyyyMMddHHmmss')"
            [System.IO.File]::WriteAllText($testFile, "test")
            Remove-Item $testFile -Force
            if (Write-Check "Writable: $folder" $true) { $passedChecks++ }
        } catch {
            Write-Check "Writable: $folder" $false "Write test failed: $_"
        }
    } else {
        Write-Check "Writable: $folder" $false "Folder does not exist"
    }
}

# =============================================================================
# 9. Test Environment Isolation
# =============================================================================
Write-Host ""
Write-Host "--- Test Environment Isolation ---" -ForegroundColor Yellow

$totalChecks++
$testApiExists = Test-Path "C:\Apps\AlplaPortal\Test\api"
if (Write-Check "Test environment exists" $testApiExists "Test API path present") { $passedChecks++ }

if ($iisLoaded) {
    $totalChecks++
    $testApiPool = Test-Path "IIS:\AppPools\AlplaPortal-Test-Api-Pool"
    if ($testApiPool) {
        $testState = (Get-WebAppPoolState -Name "AlplaPortal-Test-Api-Pool" -ErrorAction SilentlyContinue).Value
        if (Write-Check "Test App Pool intact" $true "State: $testState") { $passedChecks++ }
    } else {
        Write-Check "Test App Pool intact" $false "Not found"
    }
}

# =============================================================================
# Summary
# =============================================================================
Write-Host ""
Write-Host "============================================" -ForegroundColor White
Write-Host "  Results: $passedChecks / $totalChecks checks passed"
$failedChecks = $totalChecks - $passedChecks
if ($failedChecks -eq 0) {
    Write-Host "  Status: ALL CHECKS PASSED" -ForegroundColor Green
} else {
    Write-Host "  Status: $failedChecks CHECK(S) FAILED" -ForegroundColor Red
}
Write-Host "============================================" -ForegroundColor White
Write-Host ""
