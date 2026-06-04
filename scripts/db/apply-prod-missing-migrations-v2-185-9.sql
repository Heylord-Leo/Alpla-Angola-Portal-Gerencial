-- ============================================================================
-- SAFE MIGRATION SCRIPT FOR PRODUCTION ENVIRONMENT
-- Database: [Portal-Gerencial]
-- Server:   AOVIA1VMS011
-- Version:  v2.185.9
-- Date:     2026-06-04
--
-- !! PRODUCTION SCRIPT — REVIEW CAREFULLY BEFORE EXECUTION !!
--
-- This script applies 2 pending EF Core migrations that are missing
-- their .Designer.cs files. The SQL was manually derived from the
-- migration Up() methods. Each section is idempotent using IF NOT EXISTS
-- guards on both __EFMigrationsHistory and the target schema objects.
--
-- Pending migrations:
--   1. 20260421155149_AddContractDocumentSoftDelete
--   2. 20260425101500_AddAttendanceJustifications
--
-- SAFETY:
--   - Database name verified via RAISERROR before any DDL.
--   - This script only runs IF the migration ID is NOT already in
--     __EFMigrationsHistory.
--   - Each DDL statement checks if the column/table/index already exists.
--   - Safe to re-run (fully idempotent).
--   - Does NOT modify any existing data.
--   - Does NOT touch [Portal-Gerencial-Test].
--
-- PRE-REQUISITES:
--   - Run against [Portal-Gerencial] ONLY.
--   - Use a DBA account (db_owner or ddl_admin), NOT the IIS runtime user.
--   - Take a database backup BEFORE running.
--   - Stop App Pools before running:
--       AlplaPortal-Prod-Api-Pool
--       AlplaPortal-Prod-Web-Pool
--
-- VALIDATED ON TEST:
--   - This exact migration logic was validated on [Portal-Gerencial-Test]
--     on 2026-06-04 and the subsequent v2.185.9 deployment succeeded.
-- ============================================================================

USE [Portal-Gerencial];
GO

-- ============================================================================
-- STEP 0: Verify target database
-- ============================================================================
IF DB_NAME() <> 'Portal-Gerencial'
BEGIN
    RAISERROR('SAFETY CHECK FAILED: This script must run against [Portal-Gerencial]. Current database: %s', 16, 1, DB_NAME());
    RETURN;
END;
GO

-- Extra safety: ensure we are NOT on the Test database
IF DB_NAME() = 'Portal-Gerencial-Test'
BEGIN
    RAISERROR('SAFETY CHECK FAILED: This script must NOT run against [Portal-Gerencial-Test]. Use the TEST script instead.', 16, 1);
    RETURN;
END;
GO

PRINT '=== Target database confirmed: [Portal-Gerencial] (PRODUCTION) ===';
GO

-- ============================================================================
-- MIGRATION 1: 20260421155149_AddContractDocumentSoftDelete
-- Adds soft-delete columns and index to ContractDocuments table.
-- Source: 20260421155149_AddContractDocumentSoftDelete.cs
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260421155149_AddContractDocumentSoftDelete'
)
BEGIN
    PRINT '--- Applying: 20260421155149_AddContractDocumentSoftDelete ---';

    -- Add [IsDeleted] column (bit, NOT NULL, default false)
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments')
          AND name = N'IsDeleted'
    )
    BEGIN
        ALTER TABLE [dbo].[ContractDocuments]
        ADD [IsDeleted] bit NOT NULL CONSTRAINT [DF_ContractDocuments_IsDeleted] DEFAULT (0);
        PRINT '  + Added column [IsDeleted] to [ContractDocuments]';
    END
    ELSE
        PRINT '  ~ Column [IsDeleted] already exists on [ContractDocuments]';

    -- Add [DeletedAtUtc] column (datetime2, nullable)
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments')
          AND name = N'DeletedAtUtc'
    )
    BEGIN
        ALTER TABLE [dbo].[ContractDocuments]
        ADD [DeletedAtUtc] datetime2 NULL;
        PRINT '  + Added column [DeletedAtUtc] to [ContractDocuments]';
    END
    ELSE
        PRINT '  ~ Column [DeletedAtUtc] already exists on [ContractDocuments]';

    -- Add [DeletedByUserId] column (uniqueidentifier, nullable)
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments')
          AND name = N'DeletedByUserId'
    )
    BEGIN
        ALTER TABLE [dbo].[ContractDocuments]
        ADD [DeletedByUserId] uniqueidentifier NULL;
        PRINT '  + Added column [DeletedByUserId] to [ContractDocuments]';
    END
    ELSE
        PRINT '  ~ Column [DeletedByUserId] already exists on [ContractDocuments]';

    -- Create index IX_ContractDocuments_IsDeleted
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.ContractDocuments')
          AND name = N'IX_ContractDocuments_IsDeleted'
    )
    BEGIN
        CREATE INDEX [IX_ContractDocuments_IsDeleted]
        ON [dbo].[ContractDocuments] ([IsDeleted]);
        PRINT '  + Created index [IX_ContractDocuments_IsDeleted]';
    END
    ELSE
        PRINT '  ~ Index [IX_ContractDocuments_IsDeleted] already exists';

    -- Record migration in __EFMigrationsHistory
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260421155149_AddContractDocumentSoftDelete', N'8.0.2');
    PRINT '  + Recorded migration in __EFMigrationsHistory';

    PRINT '--- DONE: 20260421155149_AddContractDocumentSoftDelete ---';
END
ELSE
BEGIN
    PRINT '--- SKIP: 20260421155149_AddContractDocumentSoftDelete (already applied) ---';
END;
GO

-- ============================================================================
-- MIGRATION 2: 20260425101500_AddAttendanceJustifications
-- Creates the HRAttendanceJustifications table with FKs and indexes.
-- Source: 20260425101500_AddAttendanceJustifications.cs
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260425101500_AddAttendanceJustifications'
)
BEGIN
    PRINT '--- Applying: 20260425101500_AddAttendanceJustifications ---';

    -- Create table HRAttendanceJustifications
    IF NOT EXISTS (
        SELECT 1 FROM sys.objects
        WHERE object_id = OBJECT_ID(N'dbo.HRAttendanceJustifications')
          AND type = 'U'
    )
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
                FOREIGN KEY ([HREmployeeId]) REFERENCES [dbo].[HREmployees] ([Id])
                ON DELETE CASCADE,
            CONSTRAINT [FK_HRAttendanceJustifications_Users_SubmittedByUserId]
                FOREIGN KEY ([SubmittedByUserId]) REFERENCES [dbo].[Users] ([Id])
                ON DELETE NO ACTION
        );
        PRINT '  + Created table [HRAttendanceJustifications]';
    END
    ELSE
        PRINT '  ~ Table [HRAttendanceJustifications] already exists';

    -- Index: employee + date for efficient lookup
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.HRAttendanceJustifications')
          AND name = N'IX_HRAttendanceJustifications_HREmployeeId_Date'
    )
    BEGIN
        CREATE INDEX [IX_HRAttendanceJustifications_HREmployeeId_Date]
        ON [dbo].[HRAttendanceJustifications] ([HREmployeeId], [Date]);
        PRINT '  + Created index [IX_HRAttendanceJustifications_HREmployeeId_Date]';
    END
    ELSE
        PRINT '  ~ Index [IX_HRAttendanceJustifications_HREmployeeId_Date] already exists';

    -- Index: status for filtering pending approvals
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.HRAttendanceJustifications')
          AND name = N'IX_HRAttendanceJustifications_Status'
    )
    BEGIN
        CREATE INDEX [IX_HRAttendanceJustifications_Status]
        ON [dbo].[HRAttendanceJustifications] ([Status]);
        PRINT '  + Created index [IX_HRAttendanceJustifications_Status]';
    END
    ELSE
        PRINT '  ~ Index [IX_HRAttendanceJustifications_Status] already exists';

    -- Record migration in __EFMigrationsHistory
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260425101500_AddAttendanceJustifications', N'8.0.2');
    PRINT '  + Recorded migration in __EFMigrationsHistory';

    PRINT '--- DONE: 20260425101500_AddAttendanceJustifications ---';
END
ELSE
BEGIN
    PRINT '--- SKIP: 20260425101500_AddAttendanceJustifications (already applied) ---';
END;
GO

-- ============================================================================
-- VERIFICATION
-- ============================================================================

PRINT '';
PRINT '=== VERIFICATION ===';

SELECT MigrationId, ProductVersion
FROM [__EFMigrationsHistory]
WHERE MigrationId IN (
    N'20260421155149_AddContractDocumentSoftDelete',
    N'20260425101500_AddAttendanceJustifications'
)
ORDER BY MigrationId;

PRINT '';
PRINT '=== EXPECTED: 2 rows above. If 2 rows displayed, both migrations are applied. ===';
PRINT '=== TOTAL MIGRATIONS ===';

SELECT COUNT(*) AS TotalAppliedMigrations FROM [__EFMigrationsHistory];

PRINT '=== Expected total: 52 ===';
GO
