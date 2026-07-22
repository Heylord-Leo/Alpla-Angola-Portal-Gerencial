param(
    [Parameter(Mandatory = $true)]
    [string]$ConfirmExport,

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
    [AllowEmptyString()]
    [string]$ResolvedCommitSha,

    [Parameter(Mandatory = $true)]
    [string]$WorkflowRef
)

# =============================================================================
# Export PROD Data for Dev - Input, Ref and Version Validation
#
# Mirrors scripts/db/validate-sync-prod-data-test-inputs.ps1. Non-destructive
# by design: this script never opens a SQL connection and never creates a
# backup. It only validates inputs and prints safe metadata. The caller
# (workflow) must not proceed to scripts/db/export-prod-data-dev.ps1 if this
# script throws.
#
# This workflow is READ-ONLY against Production: it only ever runs
# BACKUP DATABASE, which does not modify Portal-Gerencial in any way.
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
Write-Host "  Export PROD Data for Dev - Input Validation" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# 1. Ref Guard - must run before any other check
# -----------------------------------------------------------------------------
if ($RefName -ne $RequiredRefName) {
    throw "REF INVALIDA: this workflow must be executed from $RequiredRefName. Current ref: $RefName."
}

# -----------------------------------------------------------------------------
# 2. Confirmation Guard
# -----------------------------------------------------------------------------
if ($ConfirmExport -ne "EXPORT_PROD_FOR_LOCAL_DEV") {
    throw "INPUT INVALIDO: confirm_export must be exactly EXPORT_PROD_FOR_LOCAL_DEV."
}

# -----------------------------------------------------------------------------
# 3. Dynamic Repository Version Validation
# -----------------------------------------------------------------------------
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

# -----------------------------------------------------------------------------
# 4. Source Database Safety (fail-fast gate before any SQL connection is
#    opened by export-prod-data-dev.ps1)
# -----------------------------------------------------------------------------
$forbiddenDatabaseNames = @('Portal-Gerencial-Test', 'AlplaPortalV1')

if ($SourceDbName -ne "Portal-Gerencial") {
    throw "VALIDACAO FALHOU: source database must be exactly 'Portal-Gerencial'. Found: '$SourceDbName'."
}

if ($forbiddenDatabaseNames -contains $SourceDbName) {
    throw "VALIDACAO FALHOU: '$SourceDbName' is a forbidden source database name."
}

# -----------------------------------------------------------------------------
# 5. Resolved Commit SHA Safety (defense in depth: the resolved SHA already
#    comes validated from scripts/db/resolve-sync-commit-metadata.ps1, but
#    this script never trusts an upstream value without checking its shape)
# -----------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($ResolvedCommitSha)) {
    throw "TRACEABILITY ERROR: resolved commit SHA must not be empty."
}

if ($ResolvedCommitSha -notmatch $ShaPattern) {
    throw "TRACEABILITY ERROR: resolved commit SHA '$ResolvedCommitSha' is not a valid 40-character hexadecimal SHA."
}

# -----------------------------------------------------------------------------
# 6. Safe Traceability Output (only reached once every guard above passed)
# -----------------------------------------------------------------------------
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
Write-Host ""
Write-Host "Nota: este workflow e SOMENTE LEITURA para a PROD (apenas BACKUP DATABASE)." -ForegroundColor DarkGray
Write-Host "Nenhuma escrita, restore ou alteracao de dados ocorre em Portal-Gerencial." -ForegroundColor DarkGray
