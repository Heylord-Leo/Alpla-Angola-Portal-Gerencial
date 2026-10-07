<#
.SYNOPSIS
  READ-ONLY: shows the EFFECTIVE AppConfig:ProformaDeadlineAlerts and AppConfig:ApprovalReminders
  configuration of a deployed API (TEST or PROD) without printing any secret.

.DESCRIPTION
  Why: the proforma scheduler fix makes a previously non-functional background service actually run.
  The committed appsettings.json ships ProformaDeadlineAlerts.Enabled = true; whether PROD disables it
  is decided by the server-side appsettings.<Environment>.json (preserved by the deploy workflows) or by
  environment variables. Run this on the host (AOVIA1VMS011) BEFORE deploying change 1/3 and decide.
  Nothing is modified. Connection strings and SMTP passwords are never read or printed.

.PARAMETER ApiDeployPath
  Folder of the deployed API (contains appsettings.json and appsettings.<Env>.json).

.EXAMPLE
  .\check-proforma-alerts-config-readonly.ps1 -ApiDeployPath 'D:\Apps\AlplaPortal\Prod\api'
#>
param(
    [Parameter(Mandatory)] [string] $ApiDeployPath
)

function Get-Section($obj, [string[]] $path) {
    $cur = $obj
    foreach ($p in $path) { if ($null -eq $cur) { return $null }; $cur = $cur.$p }
    return $cur
}

$files = @('appsettings.json', 'appsettings.Production.json', 'appsettings.Test.json', 'appsettings.Staging.json')
foreach ($f in $files) {
    $full = Join-Path $ApiDeployPath $f
    if (-not (Test-Path $full)) { Write-Output "$f : (absent)"; continue }
    $json = Get-Content -Raw -LiteralPath $full | ConvertFrom-Json
    $pf = Get-Section $json @('AppConfig', 'ProformaDeadlineAlerts')
    $rm = Get-Section $json @('AppConfig', 'ApprovalReminders')
    Write-Output "$f :"
    Write-Output ("   ProformaDeadlineAlerts : " + $(if ($null -eq $pf) { '(section absent → inherits)' } else { ($pf | ConvertTo-Json -Compress) }))
    Write-Output ("   ApprovalReminders      : " + $(if ($null -eq $rm) { '(section absent → inherits)' } else { ($rm | ConvertTo-Json -Compress) }))
}

Write-Output "Environment variables overriding these sections (process/machine):"
Get-ChildItem Env: | Where-Object { $_.Name -like 'AppConfig__ProformaDeadlineAlerts__*' -or $_.Name -like 'AppConfig__ApprovalReminders__*' -or $_.Name -eq 'ASPNETCORE_ENVIRONMENT' } |
    ForEach-Object { "   $($_.Name) = $($_.Value)" }
Write-Output ""
Write-Output "Interpretation: effective value = last of appsettings.json → appsettings.<ASPNETCORE_ENVIRONMENT>.json → env vars."
Write-Output "If ProformaDeadlineAlerts.Enabled resolves to true, deploying the scheduler fix WILL start queueing proforma alerts to real recipients."
Write-Output "ApprovalReminders must resolve to Enabled=false (or DryRun=true) in PROD."
