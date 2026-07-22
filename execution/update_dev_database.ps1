# ===========================================================================
# execution/update_dev_database.ps1 - Protected EF Core migration for Development
#
# Default mode: preview/read-only (lists applied and pending migrations).
# Requires -Apply AND -Confirmation 'APPLY-MIGRATIONS-TO-DEV-CLONE' to perform
# an actual database update.
#
# Canonical database: Portal-Gerencial-Dev-ProdClone
# SQL instance:       (localdb)\MSSQLLocalDB
#
# This script NEVER accepts an arbitrary database or connection-string parameter.
# It NEVER touches TEST or PROD.
# ===========================================================================
param(
    [switch]$Apply,
    [string]$Confirmation
)

$ErrorActionPreference = 'Stop'

# -- Constants ---------------------------------------------------------------
$CanonicalDb   = 'Portal-Gerencial-Dev-ProdClone'
$SqlInstance   = '(localdb)\MSSQLLocalDB'
$ForbiddenDbs  = @('AlplaPortalV1', 'Portal-Gerencial', 'Portal-Gerencial-Test')
$CloneConnStr  = "Server=$SqlInstance;Database=$CanonicalDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$RepoRoot      = Resolve-Path "$PSScriptRoot/.."
$ApiDir        = Join-Path $RepoRoot 'src/backend/AlplaPortal.Api'
$InfraDir      = Join-Path $RepoRoot 'src/backend/AlplaPortal.Infrastructure'
$RequiredPhrase = 'APPLY-MIGRATIONS-TO-DEV-CLONE'

# -- Gate: -Apply requires -Confirmation ------------------------------------
if ($Apply -and $Confirmation -ne $RequiredPhrase) {
    Write-Host "[FATAL] -Apply requires -Confirmation '$RequiredPhrase'." -ForegroundColor Red
    Write-Host "        Usage: .\update_dev_database.ps1 -Apply -Confirmation '$RequiredPhrase'" -ForegroundColor Yellow
    exit 1
}

Write-Host "===========================================================" -ForegroundColor Cyan
Write-Host " Protected EF Core Migration - Development Clone"            -ForegroundColor Cyan
$modeLabel = if ($Apply) { 'APPLY' } else { 'PREVIEW (read-only)' }
Write-Host " Mode: $modeLabel"                                           -ForegroundColor Cyan
Write-Host "===========================================================" -ForegroundColor Cyan

# -- Step 1: Set environment for all child processes -------------------------
Write-Host "`n[1/8] Setting environment variables for child processes..."
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = $CloneConnStr
Write-Host "       ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT"
Write-Host "       Target database        = $CanonicalDb"
Write-Host "       SQL instance           = $SqlInstance"

# -- Step 2: Ensure LocalDB is running --------------------------------------
Write-Host "`n[2/8] Ensuring LocalDB instance is running..."
$info = sqllocaldb info MSSQLLocalDB 2>&1
if ($info -match 'State:\s+Stopped') {
    sqllocaldb start MSSQLLocalDB | Out-Null
    Start-Sleep -Seconds 2
}

# -- Step 3: Direct connection + DB_NAME() verification ---------------------
Write-Host "`n[3/8] Connecting and verifying DB_NAME()..."
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($CloneConnStr)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT DB_NAME()"
    $actualDb = $cmd.ExecuteScalar()

    # Also verify instance is LocalDB
    $cmd2 = $conn.CreateCommand()
    $cmd2.CommandText = "SELECT @@SERVERNAME"
    $actualServer = $cmd2.ExecuteScalar()

    $conn.Close()
    $conn.Dispose()
}
catch {
    Write-Host "[FATAL] Cannot connect to '$CanonicalDb' on '$SqlInstance'." -ForegroundColor Red
    Write-Host "[FATAL] Error: $_" -ForegroundColor Red
    exit 1
}

if ($actualDb -ne $CanonicalDb) {
    Write-Host "[FATAL] DB_NAME() returned '$actualDb' - expected '$CanonicalDb'." -ForegroundColor Red
    exit 1
}

foreach ($forbidden in $ForbiddenDbs) {
    if ($actualDb -eq $forbidden) {
        Write-Host "[FATAL] Connected to forbidden database '$forbidden'. Aborting." -ForegroundColor Red
        exit 1
    }
}

Write-Host "[OK]   DB_NAME()    = $actualDb" -ForegroundColor Green
Write-Host "[OK]   @@SERVERNAME = $actualServer" -ForegroundColor Green

# -- Step 4: Secondary evidence - dotnet ef dbcontext info -------------------
Write-Host "`n[4/8] Running 'dotnet ef dbcontext info' (secondary evidence)..."
$efInfo = dotnet ef dbcontext info --project $InfraDir --startup-project $ApiDir 2>&1
$efInfo | ForEach-Object { Write-Host "       $_" }

# -- Step 5: List migrations (preview) --------------------------------------
Write-Host "`n[5/8] Listing EF Core migrations..."
$migList = dotnet ef migrations list --project $InfraDir --startup-project $ApiDir 2>&1
$migList | ForEach-Object { Write-Host "       $_" }

$pendingCount = ($migList | Where-Object { $_ -match '\(Pending\)' }).Count
Write-Host "`n       Applied + Pending total: $($migList.Count) entries"
Write-Host "       Pending: $pendingCount"

# -- If preview only, stop here ---------------------------------------------
if (-not $Apply) {
    Write-Host "`n===========================================================" -ForegroundColor Cyan
    Write-Host " PREVIEW COMPLETE - no changes made."                           -ForegroundColor Cyan
    Write-Host " To apply: .\update_dev_database.ps1 -Apply -Confirmation '$RequiredPhrase'" -ForegroundColor Yellow
    Write-Host "===========================================================" -ForegroundColor Cyan
    exit 0
}

# -- Step 6: Recheck DB_NAME() immediately before applying ------------------
Write-Host "`n[6/8] Re-verifying DB_NAME() before apply..."
$conn2 = New-Object System.Data.SqlClient.SqlConnection($CloneConnStr)
$conn2.Open()
$cmd3 = $conn2.CreateCommand()
$cmd3.CommandText = "SELECT DB_NAME()"
$recheckDb = $cmd3.ExecuteScalar()
$conn2.Close()
$conn2.Dispose()

if ($recheckDb -ne $CanonicalDb) {
    Write-Host "[FATAL] Pre-apply recheck: DB_NAME() = '$recheckDb' - aborting." -ForegroundColor Red
    exit 1
}
Write-Host "[OK]   Pre-apply DB_NAME() = $recheckDb" -ForegroundColor Green

# -- Step 7: Apply migrations -----------------------------------------------
Write-Host "`n[7/8] Applying 'dotnet ef database update'..."
dotnet ef database update --project $InfraDir --startup-project $ApiDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FATAL] 'dotnet ef database update' failed with exit code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "[OK]   Database update completed." -ForegroundColor Green

# -- Step 8: Post-apply verification ----------------------------------------
Write-Host "`n[8/8] Post-apply verification..."

# Recheck DB_NAME()
$conn3 = New-Object System.Data.SqlClient.SqlConnection($CloneConnStr)
$conn3.Open()
$cmd4 = $conn3.CreateCommand()
$cmd4.CommandText = "SELECT DB_NAME()"
$postDb = $cmd4.ExecuteScalar()

# Latest migration
$cmd5 = $conn3.CreateCommand()
$cmd5.CommandText = "SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC"
$latestMig = $cmd5.ExecuteScalar()

$conn3.Close()
$conn3.Dispose()

Write-Host "[OK]   Post-apply DB_NAME()       = $postDb" -ForegroundColor Green
Write-Host "[OK]   Post-apply latest migration = $latestMig" -ForegroundColor Green

# Re-list migrations
Write-Host "`n       Post-apply migration list:"
$postMigList = dotnet ef migrations list --project $InfraDir --startup-project $ApiDir 2>&1
$postMigList | ForEach-Object { Write-Host "       $_" }

Write-Host "`n===========================================================" -ForegroundColor Green
Write-Host " MIGRATIONS APPLIED SUCCESSFULLY."                              -ForegroundColor Green
Write-Host " Database: $postDb on $SqlInstance"                             -ForegroundColor Green
Write-Host "===========================================================" -ForegroundColor Green
