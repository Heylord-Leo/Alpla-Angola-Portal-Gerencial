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

Write-Host "Test-ResponsibleUserIdSafety:"
# Synthetic migration order mirroring the real pending set's shape: AddDepartmentManagers (legitimate
# read/backfill), PhaseC (legitimate drop + audit backup), then later unrelated migrations.
$ruExpected = @(
    'm01_AddSignedReturnDocumentId',
    'm02_AddDepartmentManagers',
    'm03_PhaseCRemoveLegacyAreaApprovalConfig',
    'm04_AddLineItemProvenanceAndIdempotency',
    'm05_AddQuotationReuseAuthorizations'
)
$ruBoundary = 'm03_PhaseCRemoveLegacyAreaApprovalConfig'

# 1) No occurrence at all -> safe
$noRef = "IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm01_AddSignedReturnDocumentId')`nBEGIN`n    ALTER TABLE [X] ADD [Y] int NULL;`nEND;`nGO"
Assert (Test-ResponsibleUserIdSafety -SqlContent $noRef -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe "1 no occurrence -> safe"

# 2) DROP COLUMN inside the boundary (PhaseC) block -> accepted
$dropCol = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03_PhaseCRemoveLegacyAreaApprovalConfig')
BEGIN
    ALTER TABLE [Departments] DROP COLUMN [ResponsibleUserId];
END;
GO
"@
Assert (Test-ResponsibleUserIdSafety -SqlContent $dropCol -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe "2 DROP COLUMN in boundary block -> accepted"

# 3) DROP INDEX / DROP CONSTRAINT inside the boundary block -> accepted
$dropIdxConstraint = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03_PhaseCRemoveLegacyAreaApprovalConfig')
BEGIN
    ALTER TABLE [Departments] DROP CONSTRAINT [FK_Departments_Users_ResponsibleUserId];
    DROP INDEX [IX_Departments_ResponsibleUserId] ON [Departments];
END;
GO
"@
Assert (Test-ResponsibleUserIdSafety -SqlContent $dropIdxConstraint -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe "3 DROP INDEX/CONSTRAINT in boundary block -> accepted"

# Real-world shape: read-only backfill (m02, before boundary) + audit backup insert into another table (m03) -> accepted
$legitimateReal = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm02_AddDepartmentManagers')
BEGIN
    INSERT INTO DepartmentManagers (DepartmentId, PlantId, UserId, IsActive, CreatedAtUtc)
    SELECT d.Id, NULL, d.ResponsibleUserId, 1, GETUTCDATE()
    FROM Departments d
    WHERE d.ResponsibleUserId IS NOT NULL;
END;
GO
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03_PhaseCRemoveLegacyAreaApprovalConfig')
BEGIN
    INSERT INTO dbo._PhaseC_DepartmentResponsibleBackup (DepartmentId, ResponsibleUserId)
    SELECT d.Id, d.ResponsibleUserId FROM Departments d WHERE d.ResponsibleUserId IS NOT NULL;
    ALTER TABLE [Departments] DROP CONSTRAINT [FK_Departments_Users_ResponsibleUserId];
    DROP INDEX [IX_Departments_ResponsibleUserId] ON [Departments];
    ALTER TABLE [Departments] DROP COLUMN [ResponsibleUserId];
END;
GO
"@
Assert (Test-ResponsibleUserIdSafety -SqlContent $legitimateReal -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe "3b real backfill + audit-backup insert into OTHER table -> accepted"

# 4) ADD ResponsibleUserId -> rejected
$addCol = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03_PhaseCRemoveLegacyAreaApprovalConfig')
BEGIN
    ALTER TABLE [Departments] ADD [ResponsibleUserId] uniqueidentifier NULL;
END;
GO
"@
Assert (-not (Test-ResponsibleUserIdSafety -SqlContent $addCol -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe) "4 ADD ResponsibleUserId -> rejected"

# 5) CREATE INDEX using ResponsibleUserId -> rejected
$createIdx = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03_PhaseCRemoveLegacyAreaApprovalConfig')
BEGIN
    CREATE INDEX [IX_Departments_ResponsibleUserId] ON [Departments] ([ResponsibleUserId]);
END;
GO
"@
Assert (-not (Test-ResponsibleUserIdSafety -SqlContent $createIdx -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe) "5 CREATE INDEX on ResponsibleUserId -> rejected"

# 6) New FK involving ResponsibleUserId -> rejected
$newFk = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03_PhaseCRemoveLegacyAreaApprovalConfig')
BEGIN
    ALTER TABLE [Departments] ADD CONSTRAINT [FK_Departments_Users_ResponsibleUserId] FOREIGN KEY ([ResponsibleUserId]) REFERENCES [Users] ([Id]);
END;
GO
"@
Assert (-not (Test-ResponsibleUserIdSafety -SqlContent $newFk -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe) "6 new FK on ResponsibleUserId -> rejected"

# 7) UPDATE setting ResponsibleUserId -> rejected
$updateSet = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm03_PhaseCRemoveLegacyAreaApprovalConfig')
BEGIN
    UPDATE [Departments] SET [ResponsibleUserId] = NULL;
END;
GO
"@
Assert (-not (Test-ResponsibleUserIdSafety -SqlContent $updateSet -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe) "7 UPDATE SET ResponsibleUserId -> rejected"

# 8) ResponsibleUserId appears in a migration block AFTER the boundary -> rejected
$afterBoundary = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'm04_AddLineItemProvenanceAndIdempotency')
BEGIN
    SELECT ResponsibleUserId FROM Departments;
END;
GO
"@
Assert (-not (Test-ResponsibleUserIdSafety -SqlContent $afterBoundary -ExpectedMigrations $ruExpected -BoundaryMigrationId $ruBoundary).Safe) "8 reference after boundary migration -> rejected"

Write-Host "Test-ModelSnapshotNoLegacyProperty:"
$snapshotClean = @"
modelBuilder.Entity("AlplaPortal.Domain.Entities.Department", b =>
{
    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
    b.Property<string>("Code").HasColumnType("nvarchar(max)");
    b.ToTable("Departments");
});
modelBuilder.Entity("AlplaPortal.Domain.Entities.SomeOtherEntity", b =>
{
    b.Property<Guid?>("CurrentResponsibleUserId").HasColumnType("uniqueidentifier");
    b.ToTable("SomeOtherEntity");
});
"@
Assert (Test-ModelSnapshotNoLegacyProperty -SnapshotContent $snapshotClean -EntityFullName "AlplaPortal.Domain.Entities.Department" -PropertyName "ResponsibleUserId").Safe "9a clean snapshot (only differently-named property elsewhere) -> safe"

$snapshotStale = @"
modelBuilder.Entity("AlplaPortal.Domain.Entities.Department", b =>
{
    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("int");
    b.Property<Guid?>("ResponsibleUserId").HasColumnType("uniqueidentifier");
    b.ToTable("Departments");
});
"@
Assert (-not (Test-ModelSnapshotNoLegacyProperty -SnapshotContent $snapshotStale -EntityFullName "AlplaPortal.Domain.Entities.Department" -PropertyName "ResponsibleUserId").Safe) "9b ModelSnapshot still defines ResponsibleUserId -> rejected"

$snapshotStaleFk = @"
modelBuilder.Entity("AlplaPortal.Domain.Entities.Department", b =>
{
    b.HasOne("AlplaPortal.Domain.Entities.User", "ResponsibleUser")
        .WithMany()
        .HasForeignKey("ResponsibleUserId");
});
"@
Assert (-not (Test-ModelSnapshotNoLegacyProperty -SnapshotContent $snapshotStaleFk -EntityFullName "AlplaPortal.Domain.Entities.Department" -PropertyName "ResponsibleUserId").Safe) "9c ModelSnapshot still has FK on ResponsibleUserId -> rejected"

# Against the REAL, current repository snapshot file (read-only)
$realSnapshotPath = Join-Path $PSScriptRoot "..\..\src\backend\AlplaPortal.Infrastructure\Data\Migrations\ApplicationDbContextModelSnapshot.cs"
if (Test-Path $realSnapshotPath) {
    $realSnapshot = Get-Content $realSnapshotPath -Raw
    $realCheck = Test-ModelSnapshotNoLegacyProperty -SnapshotContent $realSnapshot -EntityFullName "AlplaPortal.Domain.Entities.Department" -PropertyName "ResponsibleUserId"
    Assert $realCheck.Safe "9d real repository ApplicationDbContextModelSnapshot.cs has no Departments.ResponsibleUserId"
} else {
    Write-Host "  SKIP  9d real snapshot file not found at $realSnapshotPath" -ForegroundColor Yellow
}

Write-Host ""
if ($fail -gt 0) { Write-Host "RESULT: $fail test(s) FAILED" -ForegroundColor Red; exit 1 }
Write-Host "RESULT: ALL TESTS PASSED" -ForegroundColor Green
exit 0
