<#
.SYNOPSIS
    Auto-generates the expected EF Core migration list from the migrations folder.

.DESCRIPTION
    Scans src/backend/AlplaPortal.Infrastructure/Data/Migrations/ for migration
    files and outputs sorted migration IDs. Excludes *.Designer.cs and
    *ModelSnapshot.cs files.

    This script replaces the hardcoded migration arrays that were previously
    maintained in three separate locations (DEC-139).

.PARAMETER RepoRoot
    Path to the repository root. Defaults to two levels up from the script location.

.PARAMETER AsArray
    If set, outputs the migration IDs as a PowerShell array assignment suitable
    for embedding in other scripts. Otherwise outputs one ID per line.

.OUTPUTS
    Migration IDs, one per line (default) or as a PowerShell array.

.EXAMPLE
    # List all expected migrations
    .\get-expected-migrations.ps1

.EXAMPLE
    # From the repo root
    .\scripts\db\get-expected-migrations.ps1 -RepoRoot "C:\dev\alpla-portal"

.NOTES
    DEC-139: Replaces hardcoded migration lists in deploy-test.yml,
    deploy-prod.yml, and check-pending-migrations.ps1.
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$RepoRoot,

    [Parameter(Mandatory = $false)]
    [switch]$AsArray
)

$ErrorActionPreference = "Stop"

# Resolve repo root
if (-not $RepoRoot) {
    # Default: script is at scripts/db/, repo root is ../../
    $RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
}

$migrationsPath = Join-Path $RepoRoot "src\backend\AlplaPortal.Infrastructure\Data\Migrations"

if (-not (Test-Path $migrationsPath)) {
    Write-Error "Migrations directory not found: $migrationsPath"
    exit 1
}

# Scan for migration files, excluding Designer and Snapshot files
$migrationFiles = Get-ChildItem -Path $migrationsPath -Filter "*.cs" -Name |
    Where-Object { $_ -notmatch '\.Designer\.cs$' -and $_ -notmatch 'Snapshot\.cs$' } |
    ForEach-Object { $_ -replace '\.cs$', '' } |
    Sort-Object

if ($migrationFiles.Count -eq 0) {
    Write-Error "No migration files found in $migrationsPath"
    exit 1
}

if ($AsArray) {
    # Output as PowerShell array for embedding
    Write-Output "`$expectedMigrations = @("
    for ($i = 0; $i -lt $migrationFiles.Count; $i++) {
        $comma = if ($i -lt $migrationFiles.Count - 1) { "," } else { "" }
        Write-Output "    `"$($migrationFiles[$i])`"$comma"
    }
    Write-Output ")"
} else {
    # Output one per line (default)
    foreach ($m in $migrationFiles) {
        Write-Output $m
    }
}
