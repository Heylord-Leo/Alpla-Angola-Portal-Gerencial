param(
    [Parameter(Mandatory = $true)]
    [string]$ConfirmRestore,

    [Parameter(Mandatory = $true)]
    [string]$ConfirmNoProdChanges,

    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$ReleaseVersion,

    [Parameter(Mandatory = $true)]
    [string]$RefName,

    [Parameter(Mandatory = $true)]
    [string]$RequiredRefName,

    [Parameter(Mandatory = $true)]
    [string]$VersionFilePath,

    [Parameter(Mandatory = $true)]
    [string]$SourceDbName,

    [Parameter(Mandatory = $true)]
    [string]$TargetDbName,

    [Parameter(Mandatory = $true)]
    [string]$SourceAppPath,

    [Parameter(Mandatory = $true)]
    [string]$TargetAppPath,

    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$ResolvedCommitSha,

    [Parameter(Mandatory = $true)]
    [string]$WorkflowRef
)

# =============================================================================
# Sync PROD Data to TEST - Input, Ref and Version Validation
#
# Non-destructive by design: this script never opens a SQL connection, never
# creates or restores a backup, and never touches the attachments folders.
# It only validates inputs and prints safe metadata. The caller (workflow)
# must not proceed to scripts/db/sync-prod-data-test.ps1 if this script
# throws.
# =============================================================================

$ErrorActionPreference = "Stop"

$SemVerPattern = '^v\d+\.\d+\.\d+$'
$ShaPattern = '^[0-9a-fA-F]{40}$'

function Get-RepositoryVersion {
    param([Parameter(Mandatory = $true)][string]$VersionFilePath)

    if (-not (Test-Path $VersionFilePath)) {
        throw "VERSION FILE NOT FOUND: $VersionFilePath"
    }

    $lines = Get-Content -Path $VersionFilePath
    $headerIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '## Current Version') {
            $headerIndex = $i
            break
        }
    }

    if ($headerIndex -eq -1) {
        throw "VERSION PARSE ERROR: '## Current Version' heading not found in $VersionFilePath."
    }

    for ($j = $headerIndex + 1; $j -lt $lines.Count; $j++) {
        $candidate = $lines[$j].Trim()
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    }

    throw "VERSION PARSE ERROR: no version value found after 'Current Version' heading in $VersionFilePath."
}

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Sync PROD Data to TEST - Input Validation" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# ─────────────────────────────────────────────────────────────────────────────
# 1. Ref Guard - must run before any other check
# ─────────────────────────────────────────────────────────────────────────────
if ($RefName -ne $RequiredRefName) {
    throw "REF INVALIDA: this workflow must be executed from $RequiredRefName. Current ref: $RefName."
}

# ─────────────────────────────────────────────────────────────────────────────
# 2. Confirmation Guards
# ─────────────────────────────────────────────────────────────────────────────
if ($ConfirmRestore -ne "RESTORE_PROD_TO_TEST") {
    throw "INPUT INVALIDO: confirm_restore must be exactly RESTORE_PROD_TO_TEST."
}

if ($ConfirmNoProdChanges -ne "I_UNDERSTAND_PROD_WILL_NOT_BE_MODIFIED") {
    throw "INPUT INVALIDO: confirm_no_prod_changes must be exactly I_UNDERSTAND_PROD_WILL_NOT_BE_MODIFIED."
}

# ─────────────────────────────────────────────────────────────────────────────
# 3. Dynamic Repository Version Validation
# ─────────────────────────────────────────────────────────────────────────────
$repoVersion = (Get-RepositoryVersion -VersionFilePath $VersionFilePath).Trim()

if ($repoVersion -notmatch $SemVerPattern) {
    throw "VERSION PARSE ERROR: repository version '$repoVersion' read from $VersionFilePath does not match SemVer pattern $SemVerPattern."
}

$inputVersion = $ReleaseVersion.Trim()

if ([string]::IsNullOrWhiteSpace($inputVersion)) {
    throw "INPUT INVALIDO: release_version must not be empty."
}

if ($inputVersion -notmatch $SemVerPattern) {
    throw "INPUT INVALIDO: release_version '$inputVersion' is not valid SemVer (expected format vX.Y.Z)."
}

if ($inputVersion -ne $repoVersion) {
    throw "INPUT INVALIDO: release_version '$inputVersion' does not match repository version '$repoVersion'."
}

# ─────────────────────────────────────────────────────────────────────────────
# 4. Database and Path Safety (mirrors the stronger checks already enforced
#    inside scripts/db/sync-prod-data-test.ps1; kept here as an early,
#    fail-fast gate before any file/network work happens)
# ─────────────────────────────────────────────────────────────────────────────
if ($SourceDbName -ne "Portal-Gerencial") {
    throw "VALIDACAO FALHOU: source database must be exactly 'Portal-Gerencial'. Found: '$SourceDbName'."
}

if ($TargetDbName -ne "Portal-Gerencial-Test") {
    throw "VALIDACAO FALHOU: target database must be exactly 'Portal-Gerencial-Test'. Found: '$TargetDbName'."
}

if ($SourceDbName -eq $TargetDbName) {
    throw "VALIDACAO FALHOU: source and target databases must differ."
}

if ([string]::IsNullOrWhiteSpace($SourceAppPath)) {
    throw "VALIDACAO FALHOU: source application path must not be empty."
}

if ([string]::IsNullOrWhiteSpace($TargetAppPath)) {
    throw "VALIDACAO FALHOU: target application path must not be empty."
}

if ($SourceAppPath -notmatch '\\Prod$') {
    throw "VALIDACAO FALHOU: source application path must end with '\Prod'. Found: '$SourceAppPath'."
}

if ($TargetAppPath -notmatch '\\Test$') {
    throw "VALIDACAO FALHOU: target application path must end with '\Test'. Found: '$TargetAppPath'."
}

if ($SourceAppPath -eq $TargetAppPath) {
    throw "VALIDACAO FALHOU: source and target application paths must differ."
}

$forbiddenAppPaths = @("C:\", "C:\Apps", "C:\Apps\AlplaPortal", "C:\Windows", "C:\Program Files")
foreach ($protectedPath in $forbiddenAppPaths) {
    if ($SourceAppPath -eq $protectedPath -or $TargetAppPath -eq $protectedPath) {
        throw "VALIDACAO FALHOU: application path cannot be a protected system directory: $protectedPath."
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 5. Resolved Commit SHA Safety (defense in depth: the resolved SHA already
#    comes validated from scripts/db/resolve-sync-commit-metadata.ps1, but
#    this script never trusts an upstream value without checking its shape)
# ─────────────────────────────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($ResolvedCommitSha)) {
    throw "TRACEABILITY ERROR: resolved commit SHA must not be empty."
}

if ($ResolvedCommitSha -notmatch $ShaPattern) {
    throw "TRACEABILITY ERROR: resolved commit SHA '$ResolvedCommitSha' is not a valid 40-character hexadecimal SHA."
}

# ─────────────────────────────────────────────────────────────────────────────
# 6. Safe Traceability Output (only reached once every guard above passed)
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Validacao concluida com sucesso." -ForegroundColor Green
Write-Host "Resumo (somente metadados seguros - nenhum segredo impresso):" -ForegroundColor Green
Write-Host "  Workflow ref (selecionado):   $WorkflowRef"
Write-Host "  github.ref_name:              $RefName"
Write-Host "  Resolved commit SHA:          $ResolvedCommitSha"
Write-Host "  Repository version:           $repoVersion"
Write-Host "  release_version input:        $inputVersion"
Write-Host "  Confirmation accepted:        yes"
Write-Host "  Source database:              $SourceDbName"
Write-Host "  Target database:              $TargetDbName"
Write-Host "  Source application path:      $SourceAppPath"
Write-Host "  Target application path:      $TargetAppPath"
Write-Host ""
Write-Host "Nota: o nome da instancia SQL Server e resolvido e impresso pelo" -ForegroundColor DarkGray
Write-Host "proprio scripts/db/sync-prod-data-test.ps1 (inalterado), a partir" -ForegroundColor DarkGray
Write-Host "das connection strings locais do runner, antes da etapa de restore." -ForegroundColor DarkGray
