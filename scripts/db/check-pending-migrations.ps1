<#
.SYNOPSIS
    Check for pending EF Core migrations against a target database.

.DESCRIPTION
    Compares the expected EF Core migration IDs (from the deployed API assembly)
    with the __EFMigrationsHistory table in the target database.
    Reports applied, pending, and unknown migrations.
    Returns exit code 0 if all expected migrations are applied, 1 otherwise.

.PARAMETER ConnectionString
    Full SQL Server connection string to the target database.

.PARAMETER ApiPath
    Path to the deployed API directory containing AlplaPortal.Api.dll.
    Used to extract the list of expected migrations from the compiled assembly.
    If not specified, uses a hardcoded list from the source repository.

.PARAMETER MigrationListFile
    Path to a text file containing expected migration IDs (one per line).
    Alternative to -ApiPath when the assembly is not available.

.EXAMPLE
    # Check TEST database
    .\check-pending-migrations.ps1 -ConnectionString "Server=AOVIA1VMS011;Database=Portal-Gerencial-Test;User Id=...;Password=...;TrustServerCertificate=True"

.EXAMPLE
    # Check PRODUCTION database
    .\check-pending-migrations.ps1 -ConnectionString "Server=AOVIA1VMS011;Database=Portal-Gerencial;User Id=...;Password=...;TrustServerCertificate=True"

.NOTES
    This script only runs SELECT queries. It does NOT modify the database.
    It does NOT require DDL or db_owner permissions.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [Parameter(Mandatory = $false)]
    [string]$MigrationListFile
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# 1. Determine expected migrations
# ─────────────────────────────────────────────────────────────────────────────

if ($MigrationListFile -and (Test-Path $MigrationListFile)) {
    Write-Host "[INFO] Loading expected migrations from: $MigrationListFile"
    $expectedMigrations = Get-Content $MigrationListFile | Where-Object { $_ -match '\S' } | ForEach-Object { $_.Trim() }
} else {
    # Auto-generate from the EF Core migrations folder (DEC-139)
    $getExpectedScript = Join-Path $PSScriptRoot "get-expected-migrations.ps1"
    if (Test-Path $getExpectedScript) {
        Write-Host "[INFO] Auto-generating expected migration list from migrations folder (DEC-139)..."
        $expectedMigrations = & $getExpectedScript -RepoRoot (Resolve-Path (Join-Path $PSScriptRoot "..\.."))
        if (-not $expectedMigrations -or $expectedMigrations.Count -eq 0) {
            Write-Error "get-expected-migrations.ps1 returned no migrations. Check the migrations folder."
            exit 1
        }
        Write-Host "[INFO] Auto-generated $($expectedMigrations.Count) expected migrations from filesystem."
    } else {
        Write-Error "get-expected-migrations.ps1 not found at $getExpectedScript. Cannot determine expected migrations."
        Write-Host "[INFO] Use -MigrationListFile to provide a text file with expected migration IDs."
        exit 1
    }
}

Write-Host "[INFO] Expected migrations: $($expectedMigrations.Count)"

# ─────────────────────────────────────────────────────────────────────────────
# 2. Query applied migrations from the target database
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "[INFO] Connecting to database..."

$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()

# Check database name for safety
$dbCmd = $conn.CreateCommand()
$dbCmd.CommandText = "SELECT DB_NAME() AS DatabaseName"
$dbName = $dbCmd.ExecuteScalar()
Write-Host "[INFO] Connected to database: [$dbName]"

# Check if __EFMigrationsHistory exists
$histCheckCmd = $conn.CreateCommand()
$histCheckCmd.CommandText = "SELECT CASE WHEN OBJECT_ID('__EFMigrationsHistory', 'U') IS NOT NULL THEN 1 ELSE 0 END"
$histExists = [int]$histCheckCmd.ExecuteScalar()

if ($histExists -ne 1) {
    Write-Host "::error::__EFMigrationsHistory table does not exist in [$dbName]. The database has never had EF Core migrations applied."
    $conn.Close()
    exit 1
}

# Read applied migrations
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId"
$reader = $cmd.ExecuteReader()

$appliedMigrations = @()
while ($reader.Read()) {
    $appliedMigrations += $reader["MigrationId"].ToString()
}
$reader.Close()
$conn.Close()

Write-Host "[INFO] Applied migrations in [$dbName]: $($appliedMigrations.Count)"

# ─────────────────────────────────────────────────────────────────────────────
# 3. Compare expected vs applied
# ─────────────────────────────────────────────────────────────────────────────

$pendingMigrations = @()
$appliedOk = @()
$unknownMigrations = @()

foreach ($expected in $expectedMigrations) {
    if ($appliedMigrations -contains $expected) {
        $appliedOk += $expected
    } else {
        $pendingMigrations += $expected
    }
}

foreach ($applied in $appliedMigrations) {
    if ($expectedMigrations -notcontains $applied) {
        $unknownMigrations += $applied
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 4. Report results
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "============================================="
Write-Host "  EF Core Migration Status Report"
Write-Host "  Database: [$dbName]"
Write-Host "============================================="
Write-Host ""

if ($appliedOk.Count -gt 0) {
    Write-Host "Applied ($($appliedOk.Count)):"
    foreach ($m in $appliedOk) {
        Write-Host "  [OK] $m"
    }
    Write-Host ""
}

if ($pendingMigrations.Count -gt 0) {
    Write-Host "PENDING ($($pendingMigrations.Count)):"
    foreach ($m in $pendingMigrations) {
        Write-Host "  [PENDING] $m"
    }
    Write-Host ""
}

if ($unknownMigrations.Count -gt 0) {
    Write-Host "UNKNOWN — in database but not in expected list ($($unknownMigrations.Count)):"
    foreach ($m in $unknownMigrations) {
        Write-Host "  [UNKNOWN] $m"
    }
    Write-Host ""
}

Write-Host "============================================="
Write-Host "  Summary"
Write-Host "============================================="
Write-Host "  Expected:  $($expectedMigrations.Count)"
Write-Host "  Applied:   $($appliedOk.Count)"
Write-Host "  Pending:   $($pendingMigrations.Count)"
Write-Host "  Unknown:   $($unknownMigrations.Count)"
Write-Host "============================================="

if ($pendingMigrations.Count -gt 0) {
    Write-Host ""
    Write-Host "RESULT: FAIL — $($pendingMigrations.Count) pending migration(s) must be applied before the API can start."
    Write-Host ""
    Write-Host "REMEDIATION:"
    Write-Host "  1. Generate an idempotent SQL script from the development machine:"
    Write-Host "     dotnet ef migrations script <last-applied-migration> -i -o migration.sql \"
    Write-Host "       --project src\backend\AlplaPortal.Infrastructure \"
    Write-Host "       --startup-project src\backend\AlplaPortal.Api"
    Write-Host "  2. Review the script for safety."
    Write-Host "  3. Apply the script to [$dbName] using SSMS or sqlcmd with a DBA account."
    Write-Host "  4. Re-run this check to confirm all migrations are applied."
    Write-Host "  5. Restart the API App Pool."
    exit 1
} else {
    Write-Host ""
    Write-Host "RESULT: PASS — All expected migrations are applied."
    exit 0
}
