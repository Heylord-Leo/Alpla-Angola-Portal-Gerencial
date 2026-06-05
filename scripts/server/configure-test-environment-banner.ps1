# =============================================================================
# Alpla Angola - Portal Gerencial
# Configure TEST Environment Visual Indicators (DEC-140)
# =============================================================================
#
# Target Server:  AOVIA1VMS011
# Environment:    TEST
# App Pool:       AlplaPortal-Test-Api-Pool
#
# Purpose:
#   Sets IIS App Pool environment variables so the API endpoint
#   GET /api/app/environment returns TEST configuration.
#   This enables the visual differentiation banner, sidebar badge,
#   and browser title prefix on the TEST environment.
#
# These variables are stored in applicationHost.config and survive
#   - App Pool recycling
#   - Application redeployment (deploy-test.yml)
#   - appsettings.Test.json changes
#
# Usage:
#   Run as Administrator on AOVIA1VMS011:
#     .\configure-test-environment-banner.ps1
#
# Verification:
#   After running, restart the App Pool and test:
#     Restart-WebAppPool -Name "AlplaPortal-Test-Api-Pool"
#     Invoke-WebRequest -Uri "https://portalgerencial-test.alpla.net/api/app/environment"
#
# =============================================================================

[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = "Stop"
$ApiPoolName = "AlplaPortal-Test-Api-Pool"

# --- Logging ---
function Log-Info  { param([string]$Msg) Write-Host "[INFO]  $(Get-Date -f 'HH:mm:ss') $Msg" -ForegroundColor Green }
function Log-Warn  { param([string]$Msg) Write-Host "[WARN]  $(Get-Date -f 'HH:mm:ss') $Msg" -ForegroundColor Yellow }
function Log-Error { param([string]$Msg) Write-Host "[ERROR] $(Get-Date -f 'HH:mm:ss') $Msg" -ForegroundColor Red }

# --- Pre-checks ---
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Log-Error "This script must be run as Administrator."
    exit 1
}

Import-Module WebAdministration -ErrorAction Stop

if (-not (Test-Path "IIS:\AppPools\$ApiPoolName")) {
    Log-Error "App Pool '$ApiPoolName' does not exist. Cannot proceed."
    exit 1
}
Log-Info "App Pool found: $ApiPoolName"

# --- Set-AppPoolEnvVar helper (reused pattern from setup-production-environment.ps1) ---
function Set-AppPoolEnvVar {
    param(
        [string]$PoolName,
        [string]$VarName,
        [string]$VarValue
    )

    $configPath = "system.applicationHost/applicationPools/add[@name='$PoolName']/environmentVariables"

    try {
        $existing = Get-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" `
            -Filter "$configPath/add[@name='$VarName']" `
            -Name "value" -ErrorAction SilentlyContinue

        if ($null -ne $existing) {
            Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" `
                -Filter "$configPath/add[@name='$VarName']" `
                -Name "value" -Value $VarValue
            Log-Info "Updated: $VarName = $VarValue"
        }
        else {
            Add-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" `
                -Filter "$configPath" `
                -Name "." `
                -Value @{name = $VarName; value = $VarValue }
            Log-Info "Added:   $VarName = $VarValue"
        }
    }
    catch {
        Log-Error "Failed to set '$VarName': $_"
        throw
    }
}

# --- Apply DEC-140 environment variables ---
Log-Info "============================================"
Log-Info "Setting DEC-140 environment variables"
Log-Info "Pool: $ApiPoolName"
Log-Info "============================================"

Set-AppPoolEnvVar -PoolName $ApiPoolName -VarName "AppEnvironment__Code"       -VarValue "TEST"
Set-AppPoolEnvVar -PoolName $ApiPoolName -VarName "AppEnvironment__Name"       -VarValue "Ambiente de Teste"
Set-AppPoolEnvVar -PoolName $ApiPoolName -VarName "AppEnvironment__ShowBanner" -VarValue "true"

# --- Restart the App Pool ---
Log-Info "Restarting App Pool: $ApiPoolName"
Restart-WebAppPool -Name $ApiPoolName
Start-Sleep -Seconds 3

$state = (Get-WebAppPoolState -Name $ApiPoolName).Value
Log-Info "App Pool state after restart: $state"

# --- Verify the endpoint ---
Log-Info "============================================"
Log-Info "Verifying /api/app/environment endpoint"
Log-Info "============================================"

Start-Sleep -Seconds 2
try {
    $response = Invoke-WebRequest -Uri "https://portalgerencial-test.alpla.net/api/app/environment" `
        -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
    $body = $response.Content
    Log-Info "HTTP $($response.StatusCode) - Response: $body"

    $json = $body | ConvertFrom-Json
    if ($json.code -eq "TEST" -and $json.showBanner -eq $true) {
        Log-Info "VERIFICATION PASSED: API returns TEST environment with showBanner=true"
    }
    else {
        Log-Warn "UNEXPECTED RESPONSE: code=$($json.code), showBanner=$($json.showBanner)"
        Log-Warn "Expected: code=TEST, showBanner=true"
    }
}
catch {
    Log-Warn "Could not verify endpoint: $_"
    Log-Warn "Try manually: Invoke-WebRequest -Uri 'https://portalgerencial-test.alpla.net/api/app/environment'"
}

# --- Summary ---
Write-Host ""
Log-Info "============================================"
Log-Info "DEC-140 TEST Configuration Complete"
Log-Info "============================================"
Write-Host ""
Write-Host "Environment variables set on $ApiPoolName`:" -ForegroundColor White
Write-Host "  AppEnvironment__Code       = TEST"
Write-Host "  AppEnvironment__Name       = Ambiente de Teste"
Write-Host "  AppEnvironment__ShowBanner = true"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Open https://portalgerencial-test.alpla.net in a browser"
Write-Host "  2. Press Ctrl+Shift+R (hard refresh)"
Write-Host "  3. Verify: amber TEST banner at top of page"
Write-Host "  4. Verify: [TEST] in browser tab title"
Write-Host "  5. Verify: TEST badge in sidebar (after login)"
Write-Host ""
