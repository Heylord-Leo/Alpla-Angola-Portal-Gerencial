<#
.SYNOPSIS
    Standalone (no framework) unit tests for migration-range.ps1 — DB-independent.
.DESCRIPTION
    Run: pwsh -File scripts/db/migration-range.Tests.ps1
    Exercises prefix validation, FROM/TO range determination, and MigrationId extraction.
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "migration-range.ps1")

$fail = 0
function Assert([bool]$cond, [string]$name) {
    if ($cond) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else { Write-Host "  FAIL  $name" -ForegroundColor Red; $script:fail++ }
}

$exp = @('m01','m02','m03','m04','m05')  # canonical filesystem order

Write-Host "Test-MigrationPrefix:"
# 1) Valid prefix with pending
Assert (Test-MigrationPrefix -Expected $exp -Applied @('m01','m02')).Valid "1 valid prefix (2 applied, 3 pending)"
# 8) No pending (all applied) is still a valid prefix
Assert (Test-MigrationPrefix -Expected $exp -Applied $exp).Valid "8 no pending -> valid prefix"
# 7) Empty database is a valid (empty) prefix
Assert (Test-MigrationPrefix -Expected $exp -Applied @()).Valid "7 empty DB -> valid prefix"
# 2) Applied migration not on filesystem
Assert (-not (Test-MigrationPrefix -Expected $exp -Applied @('m01','mXX')).Valid) "2 applied not in filesystem -> block"
# 3) Gap in history (m02 missing)
Assert (-not (Test-MigrationPrefix -Expected $exp -Applied @('m01','m03')).Valid) "3 gap -> block"
# 4) Divergent order
Assert (-not (Test-MigrationPrefix -Expected $exp -Applied @('m02','m01')).Valid) "4 divergent order -> block"
# 5) Pending interleaved (m02 pending, m03 applied)
Assert (-not (Test-MigrationPrefix -Expected $exp -Applied @('m01','m03','m04')).Valid) "5 interleaved pending -> block"
# 6) Duplicate in applied
Assert (-not (Test-MigrationPrefix -Expected $exp -Applied @('m01','m01')).Valid) "6a duplicate in applied -> block"
# 6) Duplicate in expected
Assert (-not (Test-MigrationPrefix -Expected @('m01','m01','m02') -Applied @('m01')).Valid) "6b duplicate in expected -> block"
# more applied than expected
Assert (-not (Test-MigrationPrefix -Expected @('m01','m02') -Applied @('m01','m02','m03')).Valid) "count applied>expected -> block"
# divergence detail (index/expected/found)
$d = Test-MigrationPrefix -Expected $exp -Applied @('m01','m03')
Assert ($d.Index -eq 1 -and $d.Expected -eq 'm02' -and $d.Found -eq 'm03') "divergence reports index/expected/found"

Write-Host "Get-MigrationRange:"
# 9/10) FROM = last applied, TO = last expected, pending = remainder
$r = Get-MigrationRange -Expected $exp -Applied @('m01','m02')
Assert ($r.From -eq 'm02') "9 FROM = last applied"
Assert ($r.To -eq 'm05') "10 TO = last expected"
Assert ($r.Pending.Count -eq 3 -and $r.Pending[0] -eq 'm03' -and $r.Pending[2] -eq 'm05') "range pending = exactly the 3 remaining"
# 7) Empty DB -> FROM = 0
$re = Get-MigrationRange -Expected $exp -Applied @()
Assert ($re.From -eq '0' -and $re.To -eq 'm05' -and $re.Pending.Count -eq 5) "7 empty DB -> FROM=0, all pending"
# 8) No pending -> empty pending set
$rn = Get-MigrationRange -Expected $exp -Applied $exp
Assert ($rn.Pending.Count -eq 0) "8 no pending -> empty pending"

Write-Host "Get-MigrationIdsFromScript:"
$sample = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03')
BEGIN
    ALTER TABLE [X] ADD [Y] int NULL;
END;
GO
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'm03', N'8.0.11');
GO
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'm04', N'8.0.11');
GO
"@
$ids = @(Get-MigrationIdsFromScript -SqlContent $sample)
Assert ($ids.Count -eq 2 -and $ids -contains 'm03' -and $ids -contains 'm04') "extracts exactly the inserted MigrationIds"

Write-Host ""
if ($fail -gt 0) { Write-Host "RESULT: $fail test(s) FAILED" -ForegroundColor Red; exit 1 }
Write-Host "RESULT: ALL TESTS PASSED" -ForegroundColor Green
exit 0
