<#
.SYNOPSIS
    READ-ONLY migration preflight: asserts the database identity, validates that the applied
    migration history is an exact prefix of the repository's migration files, and requires the
    pending set to be EXACTLY the list the owner expects. Writes nothing, backs up nothing,
    generates no SQL. Exit 0 only when every assertion holds.

.DESCRIPTION
    Companion of scripts/db/apply-migrations.ps1 (which lists pending migrations and applies them in
    the same run). This script is the owner-review gate that runs BEFORE "Apply TEST/PROD Migrations":
      1. SELECT DB_NAME() must equal -ExpectedDatabase (fail-closed; never a substring match).
      2. __EFMigrationsHistory must exist and its rows must be an exact, contiguous prefix of the
         filesystem list (same DEC-145 rule as the apply helper): gapped, interleaved, foreign or
         duplicated histories are refused.
      3. The pending list (filesystem − applied) must equal -ExpectedPending EXACTLY (same ids, same
         order, same count). An empty -ExpectedPending asserts "nothing pending".
    Only SELECT statements are issued. Safe to run against any environment with a read-only login.

.PARAMETER ConnectionString
    Target database connection string (masked by the caller in CI logs).
.PARAMETER ExpectedDatabase
    Exact database name, e.g. "Portal-Gerencial-Test".
.PARAMETER RepoRoot
    Repository root (the migrations folder is resolved under it).
.PARAMETER ExpectedPending
    Migration ids expected to be pending, in order. Empty = assert nothing pending.
.PARAMETER MigrationsPath
    Override of the migrations folder (relative to RepoRoot). Default: the Infrastructure folder.

.EXAMPLE
    .\preflight-migrations.ps1 -ConnectionString $cs -ExpectedDatabase "Portal-Gerencial-Test" `
        -RepoRoot C:\dev\alpla-portal -ExpectedPending 20261109150614_AddSupplierCreditAccounts
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ConnectionString,
    [Parameter(Mandatory = $true)] [string]$ExpectedDatabase,
    [Parameter(Mandatory = $true)] [string]$RepoRoot,
    [Parameter(Mandatory = $false)] [string[]]$ExpectedPending = @(),
    [Parameter(Mandatory = $false)] [string]$MigrationsPath = "src/backend/AlplaPortal.Infrastructure/Data/Migrations"
)

$ErrorActionPreference = "Stop"

function Fail([string]$message) {
    Write-Host "::error::PREFLIGHT FAILED: $message"
    Write-Host "PREFLIGHT RESULT: FAIL - $message" -ForegroundColor Red
    exit 1
}

# ── 1. Filesystem list (same filter as the deploy workflows: *.cs minus Designer / Snapshot) ──
$migrationsDir = Join-Path $RepoRoot $MigrationsPath
if (-not (Test-Path -LiteralPath $migrationsDir)) { Fail "migrations folder not found: $migrationsDir" }
$expectedMigrations = @(Get-ChildItem -LiteralPath $migrationsDir -Filter "*.cs" -Name |
    Where-Object { $_ -notmatch '\.Designer\.cs$' -and $_ -notmatch 'Snapshot\.cs$' } |
    ForEach-Object { $_ -replace '\.cs$', '' } |
    Sort-Object)
if ($expectedMigrations.Count -eq 0) { Fail "no migration files found under $migrationsDir" }
Write-Host "Repository migrations: $($expectedMigrations.Count) (newest: $($expectedMigrations[-1]))"

# ── 2. Database identity and history (SELECT only) ──
$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
try {
    $conn.Open()
    $dbName = (New-Object System.Data.SqlClient.SqlCommand("SELECT DB_NAME()", $conn)).ExecuteScalar()
    $server = (New-Object System.Data.SqlClient.SqlCommand("SELECT @@SERVERNAME", $conn)).ExecuteScalar()
    Write-Host "Connected: server=[$server] database=[$dbName]"
    if ($dbName -cne $ExpectedDatabase) { Fail "database identity mismatch: expected [$ExpectedDatabase], connected to [$dbName]" }

    $hasHistory = (New-Object System.Data.SqlClient.SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.__EFMigrationsHistory') IS NULL THEN 0 ELSE 1 END", $conn)).ExecuteScalar()
    if ([int]$hasHistory -ne 1) { Fail "__EFMigrationsHistory does not exist in [$dbName] - not a migrated Portal database" }

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId"
    $reader = $cmd.ExecuteReader()
    $applied = @()
    while ($reader.Read()) { $applied += $reader["MigrationId"].ToString() }
    $reader.Close()
}
finally { if ($conn.State -eq 'Open') { $conn.Close() } }
$applied = @($applied)
Write-Host "Applied migrations in [$dbName]: $($applied.Count)" + $(if ($applied.Count -gt 0) { " (newest: $($applied[-1]))" } else { "" })

# ── 3. Exact-prefix validation (DEC-145) ──
if ($applied.Count -gt $expectedMigrations.Count) {
    Fail "database has MORE migrations ($($applied.Count)) than the repository ($($expectedMigrations.Count)) - foreign or newer history"
}
for ($i = 0; $i -lt $applied.Count; $i++) {
    if ($applied[$i] -cne $expectedMigrations[$i]) {
        Fail "history diverges at position $i - expected [$($expectedMigrations[$i])], found [$($applied[$i])]"
    }
}
Write-Host "OK - applied history is an exact prefix of the repository list."

# ── 4. Pending set must equal the expected list exactly ──
$pending = @($expectedMigrations | Select-Object -Skip $applied.Count)
$expectedPending = @($ExpectedPending | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() })
Write-Host "Pending (repository - database): $($pending.Count)"
foreach ($m in $pending) { Write-Host "  - $m" }
Write-Host "Expected pending (owner input):  $($expectedPending.Count)"
foreach ($m in $expectedPending) { Write-Host "  - $m" }

if ($pending.Count -ne $expectedPending.Count) {
    Fail "pending count $($pending.Count) != expected $($expectedPending.Count)"
}
for ($i = 0; $i -lt $pending.Count; $i++) {
    if ($pending[$i] -cne $expectedPending[$i]) {
        Fail "pending list differs at position $i - actual [$($pending[$i])], expected [$($expectedPending[$i])]"
    }
}

Write-Host "PREFLIGHT RESULT: PASS - [$dbName] history valid; pending set is exactly the expected list ($($pending.Count) migration(s))." -ForegroundColor Green
Write-Host "::notice::Preflight PASS for [$dbName]: $($pending.Count) pending migration(s) exactly as expected. Nothing was written."
exit 0
