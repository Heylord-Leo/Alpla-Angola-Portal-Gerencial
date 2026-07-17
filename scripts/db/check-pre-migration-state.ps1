<#
.SYNOPSIS
    READ-ONLY confirmation of the pre-migration database state for the v2.207.x pending set.

.DESCRIPTION
    Confirms, WITHOUT any modification, that the target database is in the expected state BEFORE a
    (re)attempt of the migration workflow — i.e. the pending migrations were NOT partially applied by
    the STEP 5 failure (DEC-145). It performs SELECT-only queries: it never runs DDL/DML, never
    restores a backup, and never changes __EFMigrationsHistory.

    Exits non-zero if any partial/unexpected object is found, so the operator investigates before any
    restore or retry.

.PARAMETER ConnectionString
    Full SQL Server connection string to the target database.

.PARAMETER ExpectedApplied
    Expected number of applied migrations (TEST after the failure: 79).

.PARAMETER PendingIds
    MigrationIds that must NOT yet be recorded in __EFMigrationsHistory.

.NOTES
    DEC-145. Preserve the existing backup — this script never restores or deletes it.
#>

param(
    [Parameter(Mandatory = $true)][string]$ConnectionString,
    [Parameter(Mandatory = $false)][int]$ExpectedApplied = 79,
    [Parameter(Mandatory = $false)][string[]]$PendingIds = @(
        '20260716182609_AddLineItemProvenanceAndIdempotency',
        '20260717080216_AddSupplierTaxIdUniqueIndex',
        '20260717100012_AddCompanyTaxId'
    )
)

$ErrorActionPreference = "Stop"
$problems = @()

$conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
$conn.Open()
try {
    $dbName = (& { $c = $conn.CreateCommand(); $c.CommandText = "SELECT DB_NAME()"; $c.ExecuteScalar() })
    Write-Host "Connected to database: [$dbName] (READ-ONLY checks)"

    function Invoke-Scalar([string]$sql) {
        $c = $conn.CreateCommand(); $c.CommandText = $sql; return $c.ExecuteScalar()
    }

    # 1) Applied count.
    $count = [int](Invoke-Scalar "SELECT COUNT(*) FROM __EFMigrationsHistory")
    Write-Host "  __EFMigrationsHistory count: $count (expected $ExpectedApplied)"
    if ($count -ne $ExpectedApplied) { $problems += "Applied count is $count, expected $ExpectedApplied." }

    # 2) None of the pending migrations recorded.
    foreach ($id in $PendingIds) {
        $exists = [int](Invoke-Scalar "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = N'$id'")
        Write-Host "  Pending recorded? $id -> $([bool]$exists)"
        if ($exists -ne 0) { $problems += "Pending migration already recorded: $id" }
    }

    # 3) Objects created by the pending migrations must be ABSENT (no partial application).
    $checks = @(
        @{ Label = "RequestLineItems.CreationOrigin (column)"; Sql = "SELECT COL_LENGTH('RequestLineItems','CreationOrigin')" },
        @{ Label = "RequestLineItems.CreationIdempotencyKey (column)"; Sql = "SELECT COL_LENGTH('RequestLineItems','CreationIdempotencyKey')" },
        @{ Label = "Companies.TaxId (column)"; Sql = "SELECT COL_LENGTH('Companies','TaxId')" }
    )
    foreach ($chk in $checks) {
        $val = Invoke-Scalar $chk.Sql
        $present = ($null -ne $val)
        Write-Host "  $($chk.Label) present? $present (expected: False)"
        if ($present) { $problems += "Unexpected object already present: $($chk.Label)" }
    }

    # Indexes created by the pending migrations must be ABSENT.
    $idxChecks = @('IX_Companies_TaxId', 'IX_Suppliers_TaxId')
    foreach ($idx in $idxChecks) {
        $idxExists = [int](Invoke-Scalar "SELECT COUNT(*) FROM sys.indexes WHERE name = N'$idx'")
        Write-Host "  Index $idx present? $([bool]$idxExists) (expected: False)"
        if ($idxExists -ne 0) { $problems += "Unexpected index already present: $idx" }
    }
}
finally {
    $conn.Close()
}

Write-Host ""
if ($problems.Count -gt 0) {
    Write-Host "::error::PRE-MIGRATION STATE CHECK FAILED — partial/unexpected state detected:" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "::error::  - $_" -ForegroundColor Red }
    Write-Host "::error::Investigate manually BEFORE any restore or retry. This script did NOT change anything."
    exit 1
}

Write-Host "OK - Pre-migration state is clean: $ExpectedApplied applied, none of the pending recorded, no partial objects." -ForegroundColor Green
exit 0
