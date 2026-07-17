<#
.SYNOPSIS
    Apply pending EF Core migrations to a target database.

.DESCRIPTION
    Reusable migration application script for GitHub Actions workflows (DEC-139).
    Detects pending migrations, backs up the database, generates idempotent SQL
    via dotnet ef, validates the script covers all pending migrations, applies
    the SQL, and verifies __EFMigrationsHistory.

    This script does NOT re-enable Database.Migrate() in the application.
    It does NOT grant DDL permissions to IIS runtime users.
    It preserves all DEC-137 safety rules.

.PARAMETER ConnectionString
    Full SQL Server connection string to the target database.

.PARAMETER Environment
    Target environment: TEST or PROD.

.PARAMETER BackupDir
    Directory for database backups. Will be created if missing.

.PARAMETER RepoRoot
    Path to the repository root (for dotnet ef and migration folder).

.PARAMETER ExpectedDatabase
    Expected database name (safety check). E.g. "Portal-Gerencial-Test" or "Portal-Gerencial".

.PARAMETER SkipBackup
    If set, skips the database backup step. Use only in emergency.

.NOTES
    DEC-139: Controlled migration execution via GitHub Actions.
    Preserves DEC-137: No auto-migrate in startup, no DDL for IIS runtime users.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [Parameter(Mandatory = $true)]
    [ValidateSet("TEST", "PROD")]
    [string]$Environment,

    [Parameter(Mandatory = $true)]
    [string]$BackupDir,

    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedDatabase,

    [Parameter(Mandatory = $false)]
    [switch]$SkipBackup
)

$ErrorActionPreference = "Stop"

# Pure, testable helpers for incremental (FROM/TO) scripting and prefix validation (DEC-145).
. (Join-Path $PSScriptRoot "migration-range.ps1")

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  EF Core Migration Application - $Environment" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# 1. Generate expected migration list from filesystem
# ─────────────────────────────────────────────────────────────────────────────

$getExpectedScript = Join-Path $RepoRoot "scripts\db\get-expected-migrations.ps1"
if (-not (Test-Path $getExpectedScript)) {
    Write-Host "::error::get-expected-migrations.ps1 not found at $getExpectedScript"
    exit 1
}

$expectedMigrations = & $getExpectedScript -RepoRoot $RepoRoot
if (-not $expectedMigrations -or $expectedMigrations.Count -eq 0) {
    Write-Host "::error::No expected migrations generated from filesystem."
    exit 1
}
Write-Host "Expected migrations (from filesystem): $($expectedMigrations.Count)"

# ─────────────────────────────────────────────────────────────────────────────
# 2. Connect and detect pending migrations
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "STEP 1 - Detecting pending migrations..." -ForegroundColor Yellow

$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()

# Safety check: confirm database name
$dbCmd = $conn.CreateCommand()
$dbCmd.CommandText = "SELECT DB_NAME()"
$dbName = $dbCmd.ExecuteScalar()
Write-Host "Connected to database: [$dbName]"

if ($dbName -ne $ExpectedDatabase) {
    $conn.Close()
    Write-Host "::error::SAFETY CHECK FAILED: Expected [$ExpectedDatabase], connected to [$dbName]."
    if ($dbName -eq "Portal-Gerencial-Test" -and $Environment -eq "PROD") {
        Write-Host "::error::CRITICAL: Production workflow connected to TEST database! Aborting."
    }
    exit 1
}

if ($Environment -eq "PROD" -and $dbName -eq "Portal-Gerencial-Test") {
    $conn.Close()
    Write-Host "::error::CRITICAL SAFETY CHECK: PROD workflow must NOT run against Portal-Gerencial-Test."
    exit 1
}

# Read applied migrations
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId"
$reader = $cmd.ExecuteReader()
$applied = @()
while ($reader.Read()) { $applied += $reader["MigrationId"].ToString() }
$reader.Close()
$conn.Close()

Write-Host "Applied migrations in [$dbName]: $($applied.Count)"

# Normalize to arrays so positional indexing is reliable (a single row is a scalar otherwise).
$expectedMigrations = @($expectedMigrations)
$applied = @($applied)

# ─────────────────────────────────────────────────────────────────────────────
# STRICT PREFIX VALIDATION (DEC-145) — before any backup, generation or application.
# The applied migrations MUST be an exact, contiguous prefix of the filesystem list. This blocks
# out-of-order / gapped / interleaved / duplicated / foreign histories, which the incremental
# FROM/TO generation would otherwise silently mis-handle.
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "STEP 1b - Validating applied history is an exact prefix of the filesystem..." -ForegroundColor Yellow
$prefix = Test-MigrationPrefix -Expected $expectedMigrations -Applied $applied
if (-not $prefix.Valid) {
    Write-Host "::error::MIGRATION HISTORY VALIDATION FAILED: $($prefix.Reason)"
    if ($prefix.Index -ge 0) {
        Write-Host "::error::  Position:  $($prefix.Index)"
        Write-Host "::error::  Expected:  $($prefix.Expected)"
        Write-Host "::error::  Found:     $($prefix.Found)"
    }
    Write-Host "::error::The database history diverges from the repository. NO backup, script or SQL was run."
    Write-Host "::error::Investigate manually — the history is NOT auto-corrected."
    exit 1
}
Write-Host "OK - Applied history is a valid prefix of the filesystem list." -ForegroundColor Green

# Compare
$pending = @()
foreach ($m in $expectedMigrations) {
    if ($applied -notcontains $m) { $pending += $m }
}
$pending = @($pending)

if ($pending.Count -eq 0) {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host "  No pending migrations." -ForegroundColor Green
    Write-Host "  Database [$dbName] is fully aligned." -ForegroundColor Green
    Write-Host "  Total applied: $($applied.Count)" -ForegroundColor Green
    Write-Host "=============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "::notice::No pending migrations. Database schema is up to date."
    exit 0
}

Write-Host ""
Write-Host "$($pending.Count) PENDING migration(s) detected:" -ForegroundColor Yellow
foreach ($m in $pending) {
    Write-Host "  - $m" -ForegroundColor Yellow
}
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# 3. Database backup
# ─────────────────────────────────────────────────────────────────────────────

if (-not $SkipBackup) {
    Write-Host "STEP 2 - Creating database backup..." -ForegroundColor Yellow

    if (-not (Test-Path $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupFile = Join-Path $BackupDir "${dbName}_${timestamp}_pre-migration.bak"

    $connBackup = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $connBackup.Open()

    # Detect SQL Server edition (Express does not support COMPRESSION)
    $edCmd = $connBackup.CreateCommand()
    $edCmd.CommandText = "SELECT SERVERPROPERTY('Edition')"
    $edition = [string]$edCmd.ExecuteScalar()
    Write-Host "SQL Server Edition: $edition"

    if ($edition -match 'Express') {
        $backupSql = "BACKUP DATABASE [$dbName] TO DISK = N'$backupFile' WITH FORMAT, NAME = N'Pre-Migration Backup'"
    } else {
        $backupSql = "BACKUP DATABASE [$dbName] TO DISK = N'$backupFile' WITH FORMAT, COMPRESSION, NAME = N'Pre-Migration Backup'"
    }

    $bkCmd = $connBackup.CreateCommand()
    $bkCmd.CommandTimeout = 300
    $bkCmd.CommandText = $backupSql
    $bkCmd.ExecuteNonQuery() | Out-Null
    $connBackup.Close()

    Write-Host "OK - Backup saved to: $backupFile" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "STEP 2 - SKIPPED (backup disabled)" -ForegroundColor Yellow
    Write-Host ""
}

# ─────────────────────────────────────────────────────────────────────────────
# 4. Generate idempotent SQL via dotnet ef
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "STEP 3 - Generating idempotent migration SQL..." -ForegroundColor Yellow

# Both --project and --startup-project point to AlplaPortal.Infrastructure. It is self-contained for
# design time (DbContext + migrations + SqlServer provider + EFCore.Design + DesignTimeDbContextFactory)
# and has NO application host, so the EF tools use the factory directly and NEVER build/run the API
# host (Program.cs). This keeps the runtime connection-string guard in Program.cs untouched while
# preventing it from ever executing during design-time SQL generation.
$infraProject = Join-Path $RepoRoot "src\backend\AlplaPortal.Infrastructure"
$startupProject = $infraProject
$sqlOutputFile = Join-Path $env:TEMP "apply-migrations-$Environment-$(Get-Date -Format 'yyyyMMdd_HHmmss').sql"

# Determine the incremental range (DEC-145). Generating from the FIRST migration re-emits historical
# migration bodies; when a historical body references a column dropped by a later migration
# (e.g. Departments.ResponsibleUserId), SQL Server fails to COMPILE it inside the guarded IF block
# before the runtime guard can skip it. FROM = last applied (or '0' for an empty DB); TO = last expected.
$range = Get-MigrationRange -Expected $expectedMigrations -Applied $applied
$fromMigration = $range.From
$toMigration = $range.To
if ($fromMigration -eq '0') {
    Write-Host "Empty database: FROM = 0 (full history is expected and required for a fresh database)." -ForegroundColor Yellow
} else {
    Write-Host "Incremental range: FROM (last applied, not re-applied) = $fromMigration" -ForegroundColor Yellow
}
Write-Host "                   TO   (last on filesystem)             = $toMigration" -ForegroundColor Yellow

# Use the repo-pinned LOCAL dotnet-ef tool (.config/dotnet-tools.json -> 8.0.11). Deterministic and
# independent of any global tool state on the runner (we never install/update/uninstall global tools).
# Run from the repo root so the local tool manifest is discovered.
Push-Location $RepoRoot
try {
    Write-Host "Restoring local dotnet tools (.config/dotnet-tools.json)..." -ForegroundColor Yellow
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        Write-Host "::error::dotnet tool restore failed. Cannot obtain the pinned dotnet-ef tool."
        exit 1
    }

    # Validate the EFFECTIVE version is exactly 8.0.11 — fail fast if anything else is in effect.
    $efVersionOutput = (dotnet ef --version 2>&1 | Out-String)
    Write-Host "dotnet ef --version:"
    Write-Host $efVersionOutput
    if ($efVersionOutput -notmatch '8\.0\.11') {
        Write-Host "::error::Expected dotnet-ef 8.0.11 but a different version is in effect. Aborting."
        exit 1
    }
    Write-Host "OK - dotnet-ef 8.0.11 confirmed." -ForegroundColor Green

    # Generate the INCREMENTAL idempotent SQL for the FROM..TO range only. The design-time factory
    # (DesignTimeDbContextFactory) supplies the DbContext WITHOUT constructing/running the API host
    # (Program.cs), so the runtime connection-string guard is never triggered. Reuse the Release build
    # the workflow already produced (single, deterministic attempt).
    dotnet ef migrations script $fromMigration $toMigration --idempotent `
        --configuration Release `
        --no-build `
        --project $infraProject `
        --startup-project $startupProject `
        --output $sqlOutputFile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "::error::Failed to generate migration SQL script. Aborting before any SQL is applied."
        exit 1
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $sqlOutputFile)) {
    Write-Host "::error::SQL output file was not created: $sqlOutputFile"
    exit 1
}

$sqlContent = Get-Content $sqlOutputFile -Raw

# Ensure SQL Server SET options are correct for indexes/computed columns (Fix for QUOTED_IDENTIFIER error)
$setOptions = @"
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
"@
$sqlContent = $setOptions + "`r`n" + $sqlContent

# Remove any SET QUOTED_IDENTIFIER OFF injected by EF Core scaffolding (critical fix)
$qiOffCount = ([regex]::Matches($sqlContent, 'SET\s+QUOTED_IDENTIFIER\s+OFF', 'IgnoreCase')).Count
if ($qiOffCount -gt 0) {
    Write-Host "WARNING: Found $qiOffCount occurrence(s) of 'SET QUOTED_IDENTIFIER OFF' in generated SQL. Replacing with ON." -ForegroundColor Yellow
    $sqlContent = [regex]::Replace($sqlContent, 'SET\s+QUOTED_IDENTIFIER\s+OFF', 'SET QUOTED_IDENTIFIER ON', 'IgnoreCase')
} else {
    Write-Host "OK - No 'SET QUOTED_IDENTIFIER OFF' found in generated SQL." -ForegroundColor Green
}

Set-Content -Path $sqlOutputFile -Value $sqlContent -Encoding UTF8

$sqlSize = (Get-Item $sqlOutputFile).Length
Write-Host "Generated SQL script: $sqlOutputFile ($sqlSize bytes)"

# Diagnostic: print first 20 lines of the SQL file
Write-Host ""
Write-Host "--- SQL file first 20 lines ---" -ForegroundColor Cyan
Get-Content $sqlOutputFile -TotalCount 20 | ForEach-Object { Write-Host "  $_" }
Write-Host "--- end preview ---" -ForegroundColor Cyan

# ─────────────────────────────────────────────────────────────────────────────
# 5. Validate generated SQL covers all pending migrations (DEC-138 protection)
# ─────────────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "STEP 4 - Validating the generated SQL covers EXACTLY the pending set..." -ForegroundColor Yellow

# Authoritative check: the MigrationIds the script INSERTs into __EFMigrationsHistory must equal the
# pending set exactly — not a generic substring search.
$scriptMigrations = @(Get-MigrationIdsFromScript -SqlContent $sqlContent)
$pendingSorted = @($pending | Sort-Object)
$scriptSorted = @($scriptMigrations | Sort-Object)

$missingFromSql = @($pending | Where-Object { $scriptMigrations -notcontains $_ })
$extraInSql = @($scriptMigrations | Where-Object { $pending -notcontains $_ })
# None of the ALREADY-APPLIED migrations may be (re-)inserted into __EFMigrationsHistory by this range.
$reappliedApplied = @($scriptMigrations | Where-Object { $applied -contains $_ })
# Nothing after the TO migration may appear.
$toIndex = [array]::IndexOf($expectedMigrations, $toMigration)
$afterTo = @($scriptMigrations | Where-Object { [array]::IndexOf($expectedMigrations, $_) -gt $toIndex })

$validationFailed = $false
if ($missingFromSql.Count -gt 0) { $validationFailed = $true; Write-Host "::error::Pending migration(s) MISSING from the script:"; $missingFromSql | ForEach-Object { Write-Host "::error::  MISSING: $_" } }
if ($extraInSql.Count -gt 0)     { $validationFailed = $true; Write-Host "::error::Script records migration(s) that are NOT pending:"; $extraInSql | ForEach-Object { Write-Host "::error::  UNEXPECTED: $_" } }
if ($reappliedApplied.Count -gt 0) { $validationFailed = $true; Write-Host "::error::Script would (re-)insert already-applied migration(s):"; $reappliedApplied | ForEach-Object { Write-Host "::error::  RE-APPLIED: $_" } }
if ($afterTo.Count -gt 0)        { $validationFailed = $true; Write-Host "::error::Script records migration(s) after TO ($toMigration):"; $afterTo | ForEach-Object { Write-Host "::error::  AFTER-TO: $_" } }
if ($scriptMigrations.Count -ne $pending.Count) { $validationFailed = $true; Write-Host "::error::Recorded count ($($scriptMigrations.Count)) != pending count ($($pending.Count))." }

# In the incremental scenario (non-empty DB), the script must NOT reference a column dropped by history
# (Departments.ResponsibleUserId). For an EMPTY database (FROM = 0) the full history is expected and the
# column legitimately appears (created then dropped in sequence), so this check is skipped.
if ($fromMigration -ne '0') {
    if ([regex]::IsMatch($sqlContent, 'ResponsibleUserId', 'IgnoreCase')) {
        $validationFailed = $true
        Write-Host "::error::Incremental script unexpectedly references 'ResponsibleUserId' (a dropped historical column)."
    } else {
        Write-Host "OK - No 'ResponsibleUserId' reference in the incremental script." -ForegroundColor Green
    }
}

if ($validationFailed) {
    Write-Host ""
    Write-Host "::error::Expected (pending): $($pendingSorted -join ', ')" -ForegroundColor Red
    Write-Host "::error::Found (in script):  $($scriptSorted -join ', ')" -ForegroundColor Red
    Write-Host "::error::Aborting BEFORE sqlcmd. No SQL was applied."
    Remove-Item $sqlOutputFile -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "OK - The script records EXACTLY the $($pending.Count) pending migration(s), nothing else." -ForegroundColor Green
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# 6. Apply migrations via sqlcmd (with -I for QUOTED_IDENTIFIER ON)
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "STEP 5 - Applying migrations to [$dbName]..." -ForegroundColor Yellow

# Parse connection string components for sqlcmd
# Support both Windows Auth (Integrated Security) and SQL Auth (User Id/Password)
$connBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($ConnectionString)
$server = $connBuilder.DataSource
$database = $connBuilder.InitialCatalog

# --- Preflight check: verify QUOTED_IDENTIFIER is ON with -I flag ---
Write-Host ""
Write-Host "Preflight: verifying QUOTED_IDENTIFIER session option..." -ForegroundColor Yellow

if ($connBuilder.IntegratedSecurity) {
    Write-Host "  sqlcmd mode: Windows Authentication (-E -I) | Server: $server | Database: $database"
    $preflightResult = sqlcmd -S $server -d $database -E -I -h -1 -W -Q "SELECT SESSIONPROPERTY('QUOTED_IDENTIFIER') AS QI;" 2>&1
} else {
    $userId = $connBuilder.UserID
    $password = $connBuilder.Password
    Write-Host "  sqlcmd mode: SQL Authentication (-U ***) | Server: $server | Database: $database"
    $preflightResult = sqlcmd -S $server -d $database -U $userId -P $password -I -h -1 -W -Q "SELECT SESSIONPROPERTY('QUOTED_IDENTIFIER') AS QI;" 2>&1
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "::error::Preflight QUOTED_IDENTIFIER check failed. sqlcmd returned exit code $LASTEXITCODE." -ForegroundColor Red
    Write-Host "::error::Output: $preflightResult" -ForegroundColor Red
    exit 1
}

# Parse the result (should be "1" for ON)
$qiValue = ($preflightResult | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1)
if ($null -eq $qiValue) { $qiValue = "$preflightResult".Trim() }
Write-Host "  QUOTED_IDENTIFIER = $qiValue"

if ("$qiValue" -ne "1") {
    Write-Host ""
    Write-Host "::error::PREFLIGHT FAILED: QUOTED_IDENTIFIER is OFF ($qiValue) even with -I flag." -ForegroundColor Red
    Write-Host "::error::Cannot apply migrations safely. The database server or ODBC driver may override session SET options." -ForegroundColor Red
    Write-Host "::error::Investigate the sqlcmd version, ODBC driver, and SQL Server login defaults." -ForegroundColor Red
    Remove-Item $sqlOutputFile -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "OK - Preflight passed: QUOTED_IDENTIFIER = 1 (ON)" -ForegroundColor Green
Write-Host ""

# --- Execute the migration SQL with -I (QUOTED_IDENTIFIER ON) ---
if ($connBuilder.IntegratedSecurity) {
    sqlcmd -S $server -d $database -E -I -b -i $sqlOutputFile
} else {
    sqlcmd -S $server -d $database -U $userId -P $password -I -b -i $sqlOutputFile
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "::error::Migration SQL failed! Check output above." -ForegroundColor Red

    # The EF idempotent script uses multiple GO-separated batches and NO global transaction, so a
    # failure is NOT guaranteed to be atomic. Do a READ-ONLY post-failure snapshot to help decide,
    # and explicitly tell the operator to verify state BEFORE any restore or retry. We do NOT restore.
    Write-Host ""
    Write-Host "::warning::Post-failure READ-ONLY state (no restore performed, no schema changed by this step):" -ForegroundColor Yellow
    try {
        $connPf = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
        $connPf.Open()
        $pfCmd = $connPf.CreateCommand()
        $pfCmd.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory"
        Write-Host ("  __EFMigrationsHistory count: {0}" -f $pfCmd.ExecuteScalar())
        $pfCmd.CommandText = "SELECT TOP 5 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC"
        $rd = $pfCmd.ExecuteReader()
        Write-Host "  Last applied MigrationIds:"
        while ($rd.Read()) { Write-Host "    - $($rd['MigrationId'])" }
        $rd.Close(); $connPf.Close()
    } catch {
        Write-Host "  (Could not read post-failure state: $($_.Exception.Message))"
    }
    Write-Host ""
    Write-Host "::warning::MANUAL CHECK REQUIRED before any restore or retry:" -ForegroundColor Yellow
    Write-Host "  1) Confirm __EFMigrationsHistory does NOT contain a partially-recorded migration."
    Write-Host "  2) Confirm the objects created by the pending migrations are absent or in the expected prior state."
    Write-Host "  3) Only then decide whether to fix-forward or restore. Do NOT auto-restore."
    if (-not $SkipBackup) {
        Write-Host "  Backup available at: $backupFile" -ForegroundColor Yellow
    }
    Remove-Item $sqlOutputFile -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "OK - Migration SQL applied successfully." -ForegroundColor Green
Remove-Item $sqlOutputFile -ErrorAction SilentlyContinue
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# 7. Verify __EFMigrationsHistory
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "STEP 6 - Verifying __EFMigrationsHistory..." -ForegroundColor Yellow

$connVerify = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$connVerify.Open()

$cmdVerify = $connVerify.CreateCommand()
$cmdVerify.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId"
$readerVerify = $cmdVerify.ExecuteReader()
$appliedAfter = @()
while ($readerVerify.Read()) { $appliedAfter += $readerVerify["MigrationId"].ToString() }
$readerVerify.Close()
$connVerify.Close()

Write-Host "Applied migrations after update: $($appliedAfter.Count)"

# Check all expected are now applied
$stillPending = @()
foreach ($m in $expectedMigrations) {
    if ($appliedAfter -notcontains $m) { $stillPending += $m }
}

if ($stillPending.Count -gt 0) {
    Write-Host "::warning::$($stillPending.Count) migration(s) still pending after application:" -ForegroundColor Red
    foreach ($m in $stillPending) {
        Write-Host "  - $m" -ForegroundColor Red
    }
    exit 1
}

# Verify the previously pending migrations are now applied
$newlyApplied = @()
foreach ($m in $pending) {
    if ($appliedAfter -contains $m) { $newlyApplied += $m }
}

Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host "  Migration Application Complete" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host "  Environment:      $Environment" -ForegroundColor Green
Write-Host "  Database:         [$dbName]" -ForegroundColor Green
Write-Host "  Migrations applied: $($newlyApplied.Count)" -ForegroundColor Green
Write-Host "  Total now applied:  $($appliedAfter.Count)" -ForegroundColor Green
Write-Host "  Total expected:     $($expectedMigrations.Count)" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""

if ($Environment -eq "TEST") {
    Write-Host "NEXT STEP: Run 'Deploy to TEST' workflow." -ForegroundColor White
} else {
    Write-Host "NEXT STEP: Run 'Deploy to PRODUCTION' workflow." -ForegroundColor White
}

Write-Host ""
exit 0
