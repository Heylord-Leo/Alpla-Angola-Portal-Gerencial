<#
.SYNOPSIS
    TEST Migration Application Script - v2.185.9
.DESCRIPTION
    Run this script on AOVIA1VMS011 via PowerShell (as Administrator).
    It performs: Backup -> Apply 2 pending migrations -> Verify IDs -> Verify Count.
.NOTES
    Database: Portal-Gerencial-Test
    Server:   AOVIA1VMS011 (localhost)
#>

$ErrorActionPreference = "Stop"
$database = "Portal-Gerencial-Test"
$server = "."

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " TEST Migration Application - v2.185.9 " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ---- STEP 1: Database Backup ----
Write-Host "STEP 1/4 - Creating database backup..." -ForegroundColor Yellow
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupDir = "C:\Apps\AlplaPortal\Test\backups\db"
if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
}
$backupPath = Join-Path $backupDir "${database}_${timestamp}_pre-migration-v2-185-9.bak"

$backupSql = @"
BACKUP DATABASE [$database]
TO DISK = N'$backupPath'
WITH INIT, FORMAT,
     NAME = N'$database Pre-Migration v2.185.9 Backup',
     STATS = 10;
"@

sqlcmd -S $server -d master -Q $backupSql -b
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED - Backup failed! Aborting." -ForegroundColor Red
    exit 1
}
Write-Host "OK - Backup saved to: $backupPath" -ForegroundColor Green
Write-Host ""

# ---- STEP 2: Apply Migration SQL ----
Write-Host "STEP 2/4 - Applying pending migrations..." -ForegroundColor Yellow

# Write the migration SQL to a temp file to avoid PowerShell here-string parsing issues.
$tempSqlFile = Join-Path $env:TEMP "apply-test-migrations-v2-185-9.sql"

@'
-- Migration 1: 20260421155149_AddContractDocumentSoftDelete
IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421155149_AddContractDocumentSoftDelete'
)
BEGIN
    PRINT '--- Applying: 20260421155149_AddContractDocumentSoftDelete ---';

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments') AND name = N'IsDeleted')
    BEGIN
        ALTER TABLE [dbo].[ContractDocuments] ADD [IsDeleted] bit NOT NULL CONSTRAINT [DF_ContractDocuments_IsDeleted] DEFAULT (0);
        PRINT '  + Added column [IsDeleted]';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments') AND name = N'DeletedAtUtc')
    BEGIN
        ALTER TABLE [dbo].[ContractDocuments] ADD [DeletedAtUtc] datetime2 NULL;
        PRINT '  + Added column [DeletedAtUtc]';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments') AND name = N'DeletedByUserId')
    BEGIN
        ALTER TABLE [dbo].[ContractDocuments] ADD [DeletedByUserId] uniqueidentifier NULL;
        PRINT '  + Added column [DeletedByUserId]';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments') AND name = N'IX_ContractDocuments_IsDeleted')
    BEGIN
        CREATE INDEX [IX_ContractDocuments_IsDeleted] ON [dbo].[ContractDocuments] ([IsDeleted]);
        PRINT '  + Created index [IX_ContractDocuments_IsDeleted]';
    END

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260421155149_AddContractDocumentSoftDelete', N'8.0.2');
    PRINT '  + Recorded in __EFMigrationsHistory';
    PRINT '--- DONE ---';
END
ELSE
    PRINT '--- SKIP: 20260421155149_AddContractDocumentSoftDelete (already applied) ---';
GO

-- Migration 2: 20260425101500_AddAttendanceJustifications
IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425101500_AddAttendanceJustifications'
)
BEGIN
    PRINT '--- Applying: 20260425101500_AddAttendanceJustifications ---';

    IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.HRAttendanceJustifications') AND type = 'U')
    BEGIN
        CREATE TABLE [dbo].[HRAttendanceJustifications] (
            [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
            [HREmployeeId] uniqueidentifier NOT NULL,
            [Date] date NOT NULL,
            [JustificationCode] nvarchar(20) NULL,
            [JustificationText] nvarchar(500) NOT NULL,
            [SubmittedByUserId] uniqueidentifier NOT NULL,
            [ApprovedByUserId] uniqueidentifier NULL,
            [Status] nvarchar(20) NOT NULL DEFAULT (N'Pending'),
            [CreatedAtUtc] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
            [UpdatedAtUtc] datetime2 NULL,
            CONSTRAINT [PK_HRAttendanceJustifications] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_HRAttendanceJustifications_HREmployees_HREmployeeId]
                FOREIGN KEY ([HREmployeeId]) REFERENCES [dbo].[HREmployees] ([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_HRAttendanceJustifications_Users_SubmittedByUserId]
                FOREIGN KEY ([SubmittedByUserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
        );
        PRINT '  + Created table [HRAttendanceJustifications]';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HRAttendanceJustifications') AND name = N'IX_HRAttendanceJustifications_HREmployeeId_Date')
    BEGIN
        CREATE INDEX [IX_HRAttendanceJustifications_HREmployeeId_Date] ON [dbo].[HRAttendanceJustifications] ([HREmployeeId], [Date]);
        PRINT '  + Created index [IX_HRAttendanceJustifications_HREmployeeId_Date]';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HRAttendanceJustifications') AND name = N'IX_HRAttendanceJustifications_Status')
    BEGIN
        CREATE INDEX [IX_HRAttendanceJustifications_Status] ON [dbo].[HRAttendanceJustifications] ([Status]);
        PRINT '  + Created index [IX_HRAttendanceJustifications_Status]';
    END

    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260425101500_AddAttendanceJustifications', N'8.0.2');
    PRINT '  + Recorded in __EFMigrationsHistory';
    PRINT '--- DONE ---';
END
ELSE
    PRINT '--- SKIP: 20260425101500_AddAttendanceJustifications (already applied) ---';
GO
'@ | Set-Content -Path $tempSqlFile -Encoding UTF8

Write-Host "  SQL written to: $tempSqlFile"

sqlcmd -S $server -d $database -i $tempSqlFile -b
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED - Migration script failed! Check output above." -ForegroundColor Red
    Write-Host "Database backup is at: $backupPath" -ForegroundColor Yellow
    Remove-Item $tempSqlFile -ErrorAction SilentlyContinue
    exit 1
}
Write-Host "OK - Migration SQL applied successfully." -ForegroundColor Green
Remove-Item $tempSqlFile -ErrorAction SilentlyContinue
Write-Host ""

# ---- STEP 3: Verify both migration IDs ----
Write-Host "STEP 3/4 - Verifying migration IDs in __EFMigrationsHistory..." -ForegroundColor Yellow

$verifySql = @'
SET NOCOUNT ON;
SELECT MigrationId, ProductVersion
FROM [__EFMigrationsHistory]
WHERE MigrationId IN (
    N'20260421155149_AddContractDocumentSoftDelete',
    N'20260425101500_AddAttendanceJustifications'
)
ORDER BY MigrationId;
'@

$result = sqlcmd -S $server -d $database -Q $verifySql -W -s "|"
Write-Host ($result | Out-String)
$matchCount = ($result | Where-Object { $_ -match "2026" }).Count
if ($matchCount -eq 2) {
    Write-Host "OK - Both migration IDs confirmed." -ForegroundColor Green
} else {
    Write-Host "WARNING - Expected 2 migration rows, found $matchCount" -ForegroundColor Red
}
Write-Host ""

# ---- STEP 4: Verify total migration count ----
Write-Host "STEP 4/4 - Verifying total migration count..." -ForegroundColor Yellow

$countSql = "SET NOCOUNT ON; SELECT COUNT(*) FROM [__EFMigrationsHistory];"
$totalCount = (sqlcmd -S $server -d $database -Q $countSql -W -h -1).Trim()
Write-Host "Total applied migrations: $totalCount"
if ($totalCount -eq "52") {
    Write-Host "OK - Total migration count is 52. Schema is aligned." -ForegroundColor Green
} else {
    Write-Host "WARNING - Expected 52, got $totalCount" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " MIGRATION APPLICATION COMPLETE         " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Re-run the v2.185.9 TEST deployment from GitHub Actions" -ForegroundColor White
Write-Host "  2. Verify the pending migration check passes" -ForegroundColor White
Write-Host "  3. Confirm http://localhost:5001/health returns 200" -ForegroundColor White
Write-Host "  4. Validate login at https://portalgerencial-test.alpla.net" -ForegroundColor White
Write-Host ""
