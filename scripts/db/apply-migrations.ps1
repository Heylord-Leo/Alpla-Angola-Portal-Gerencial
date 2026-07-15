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

# Compare
$pending = @()
foreach ($m in $expectedMigrations) {
    if ($applied -notcontains $m) { $pending += $m }
}

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

$infraProject = Join-Path $RepoRoot "src\backend\AlplaPortal.Infrastructure"
$startupProject = Join-Path $RepoRoot "src\backend\AlplaPortal.Api"
$sqlOutputFile = Join-Path $env:TEMP "apply-migrations-$Environment-$(Get-Date -Format 'yyyyMMdd_HHmmss').sql"

# Ensure dotnet-ef tool is available
$toolsPath = Join-Path $env:USERPROFILE ".dotnet\tools"
$env:PATH = "$toolsPath;$env:PATH"
if ($env:GITHUB_PATH) {
    Add-Content -Path $env:GITHUB_PATH -Value $toolsPath
}

$dotnetEf = Join-Path $toolsPath "dotnet-ef.exe"

# Install/Update global dotnet-ef to a compatible version (8.0.11)
Write-Host "Installing/Updating dotnet-ef global tool (v8.0.11)..." -ForegroundColor Yellow
dotnet tool update --global dotnet-ef --version 8.0.11 2>&1

if (-not (Test-Path $dotnetEf)) {
    throw "dotnet-ef.exe nao encontrado em $dotnetEf apos instalacao."
}

$efToolCheck = & $dotnetEf --version 2>&1
Write-Host "dotnet-ef version: $efToolCheck"

# Generate idempotent SQL script (from start, covers all migrations)
& $dotnetEf migrations script --idempotent `
    --project $infraProject `
    --startup-project $startupProject `
    --output $sqlOutputFile `
    --no-build 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "::warning::dotnet ef script with --no-build failed. Trying with build..."
    & $dotnetEf migrations script --idempotent `
        --project $infraProject `
        --startup-project $startupProject `
        --output $sqlOutputFile 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "::error::Failed to generate migration SQL script."
        exit 1
    }
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
Write-Host "STEP 4 - Validating SQL script covers all pending migrations..." -ForegroundColor Yellow

$missingFromSql = @()
foreach ($m in $pending) {
    # Check for the INSERT INTO __EFMigrationsHistory for this migration ID
    if ($sqlContent -notmatch [regex]::Escape($m)) {
        $missingFromSql += $m
    }
}

if ($missingFromSql.Count -gt 0) {
    Write-Host ""
    Write-Host "::error::CRITICAL: The generated SQL script does NOT include the following pending migration(s):" -ForegroundColor Red
    foreach ($m in $missingFromSql) {
        Write-Host "::error::  MISSING: $m" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "::error::This is likely caused by missing .Designer.cs files (see DEC-138)." -ForegroundColor Red
    Write-Host "::error::These migrations must be applied manually using a handcrafted SQL script." -ForegroundColor Red
    Write-Host "::error::See: docs/DEPLOYMENT_CHECKLIST.md" -ForegroundColor Red

    # Clean up temp file
    Remove-Item $sqlOutputFile -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "OK - All $($pending.Count) pending migrations are covered in the generated SQL." -ForegroundColor Green
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
    if (-not $SkipBackup) {
        Write-Host "Database backup is at: $backupFile" -ForegroundColor Yellow
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
