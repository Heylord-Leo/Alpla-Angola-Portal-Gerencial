-- ═══════════════════════════════════════════════════════════════════════════
-- POST-INSTALL DATABASE VALIDATION
-- Alpla Angola Portal Gerencial
-- ═══════════════════════════════════════════════════════════════════════════
-- Purpose:  Verify the database schema is complete and healthy after
--           installation or upgrade. Run this AFTER deployment.
-- Mode:     READ-ONLY — no data is modified.
-- Usage:    sqlcmd -S <SERVER> -d <DATABASE> -i POST_INSTALL_DATABASE_VALIDATION.sql
-- ═══════════════════════════════════════════════════════════════════════════

SET NOCOUNT ON;
PRINT '================================================================';
PRINT '  POST-INSTALL DATABASE VALIDATION';
PRINT '  Server:   ' + @@SERVERNAME;
PRINT '  Database: ' + DB_NAME();
PRINT '  Executed: ' + CONVERT(VARCHAR(20), GETDATE(), 120);
PRINT '================================================================';

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 1: Critical Tables
-- ═══════════════════════════════════════════════════════════════════════════
PRINT '';
PRINT '--- STEP 1: Critical Table Existence ---';

DECLARE @criticalTables TABLE (TableName NVARCHAR(128));
INSERT INTO @criticalTables VALUES
    ('Users'), ('Roles'), ('UserRoleAssignments'),
    ('Plants'), ('Departments'), ('Companies'),
    ('RequestTypes'), ('RequestStatuses'), ('Requests'),
    ('RequestLineItems'), ('RequestAttachments'), ('RequestStatusHistories'),
    ('IvaRates'), ('Currencies'), ('Units'), ('NeedLevels'),
    ('LineItemStatuses'), ('SystemCounters'), ('CostCenters'),
    ('Suppliers'), ('Quotations'), ('QuotationItems'),
    ('CapexOpexClassifications'), ('UserPlantScopes'), ('UserDepartmentScopes'),
    ('InformationalNotifications'), ('NotificationStatuses'),
    ('AdminLogEntries'), ('LogEntries'), ('DocumentExtractionSettings'),
    ('ContractTypes');

SELECT
    ct.TableName,
    CASE WHEN OBJECT_ID(ct.TableName, 'U') IS NOT NULL THEN 'OK' ELSE '** MISSING **' END AS [Status],
    ISNULL(CAST(p.[rows] AS VARCHAR(20)), '-') AS [RowCount]
FROM @criticalTables ct
LEFT JOIN sys.tables t ON t.name = ct.TableName
LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
ORDER BY ct.TableName;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 2: Critical Columns
-- ═══════════════════════════════════════════════════════════════════════════
PRINT '';
PRINT '--- STEP 2: Critical Column Checks ---';

DECLARE @criticalColumns TABLE (TableName NVARCHAR(128), ColumnName NVARCHAR(128));
INSERT INTO @criticalColumns VALUES
    ('RequestTypes', 'Code'),
    ('IvaRates', 'Code'),
    ('IvaRates', 'RatePercent'),
    ('Users', 'AccessFailedCount'),
    ('Users', 'LockoutEndUtc'),
    ('Users', 'MustChangePassword'),
    ('CostCenters', 'PlantId'),
    ('Departments', 'ResponsibleUserId'),
    ('Suppliers', 'PortalCode'),
    ('Suppliers', 'PrimaveraCode');

SELECT
    cc.TableName,
    cc.ColumnName,
    CASE WHEN c.COLUMN_NAME IS NOT NULL THEN 'OK' ELSE '** MISSING **' END AS [Status],
    ISNULL(c.DATA_TYPE + CASE WHEN c.CHARACTER_MAXIMUM_LENGTH IS NOT NULL THEN '(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR) + ')' ELSE '' END, '-') AS DataType
FROM @criticalColumns cc
LEFT JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_NAME = cc.TableName AND c.COLUMN_NAME = cc.ColumnName
ORDER BY cc.TableName, cc.ColumnName;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 3: Seed Data Verification
-- ═══════════════════════════════════════════════════════════════════════════
PRINT '';
PRINT '--- STEP 3: Required Seed Data ---';

DECLARE @seedChecks TABLE (TableName NVARCHAR(128), ExpectedMinRows INT);
INSERT INTO @seedChecks VALUES
    ('RequestStatuses', 20),
    ('RequestTypes', 2),
    ('Roles', 11),
    ('Currencies', 3),
    ('NeedLevels', 4),
    ('Units', 6),
    ('IvaRates', 5),
    ('LineItemStatuses', 8),
    ('CapexOpexClassifications', 2),
    ('Companies', 2),
    ('Plants', 3),
    ('CostCenters', 5);

-- Dynamic check using cursor since tables may not exist
DECLARE @tbl NVARCHAR(128), @expected INT;
DECLARE @seedResults TABLE (TableName NVARCHAR(128), Expected INT, Actual INT, [Status] NVARCHAR(20));

DECLARE seed_cursor CURSOR FOR SELECT TableName, ExpectedMinRows FROM @seedChecks;
OPEN seed_cursor;
FETCH NEXT FROM seed_cursor INTO @tbl, @expected;

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @sql NVARCHAR(MAX), @actual INT = 0;
    IF OBJECT_ID(@tbl, 'U') IS NOT NULL
    BEGIN
        SET @sql = N'SELECT @cnt = COUNT(*) FROM ' + QUOTENAME(@tbl);
        EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @actual OUTPUT;
    END
    INSERT INTO @seedResults VALUES (@tbl, @expected, @actual,
        CASE WHEN OBJECT_ID(@tbl, 'U') IS NULL THEN '** TABLE MISSING **'
             WHEN @actual >= @expected THEN 'OK'
             ELSE '** LOW (' + CAST(@actual AS VARCHAR) + ') **' END);
    FETCH NEXT FROM seed_cursor INTO @tbl, @expected;
END
CLOSE seed_cursor;
DEALLOCATE seed_cursor;

SELECT * FROM @seedResults ORDER BY TableName;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 4: Migration History
-- ═══════════════════════════════════════════════════════════════════════════
PRINT '';
PRINT '--- STEP 4: Migration Count ---';

SELECT
    COUNT(*) AS TotalMigrations,
    MIN(MigrationId) AS FirstMigration,
    MAX(MigrationId) AS LastMigration
FROM __EFMigrationsHistory;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 5: Active User Check
-- ═══════════════════════════════════════════════════════════════════════════
PRINT '';
PRINT '--- STEP 5: Active Users ---';

SELECT
    COUNT(*) AS TotalUsers,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveUsers,
    SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) AS InactiveUsers
FROM Users;

-- ═══════════════════════════════════════════════════════════════════════════
-- STEP 6: Foreign Key Integrity Spot Checks
-- ═══════════════════════════════════════════════════════════════════════════
PRINT '';
PRINT '--- STEP 6: FK Integrity Spot Checks ---';

DECLARE @fkChecks TABLE (CheckName NVARCHAR(200), OrphanCount INT);

IF OBJECT_ID('Plants', 'U') IS NOT NULL AND OBJECT_ID('Companies', 'U') IS NOT NULL
    INSERT INTO @fkChecks
    SELECT 'Plants -> Companies', COUNT(*) FROM Plants p WHERE NOT EXISTS (SELECT 1 FROM Companies c WHERE c.Id = p.CompanyId);

IF OBJECT_ID('UserPlantScopes', 'U') IS NOT NULL AND OBJECT_ID('Plants', 'U') IS NOT NULL
    INSERT INTO @fkChecks
    SELECT 'UserPlantScopes -> Plants', COUNT(*) FROM UserPlantScopes ups WHERE NOT EXISTS (SELECT 1 FROM Plants p WHERE p.Id = ups.PlantId);

IF OBJECT_ID('UserRoleAssignments', 'U') IS NOT NULL AND OBJECT_ID('Roles', 'U') IS NOT NULL
    INSERT INTO @fkChecks
    SELECT 'UserRoleAssignments -> Roles', COUNT(*) FROM UserRoleAssignments ura WHERE NOT EXISTS (SELECT 1 FROM Roles r WHERE r.Id = ura.RoleId);

IF OBJECT_ID('Requests', 'U') IS NOT NULL AND OBJECT_ID('RequestTypes', 'U') IS NOT NULL
    INSERT INTO @fkChecks
    SELECT 'Requests -> RequestTypes', COUNT(*) FROM Requests r WHERE NOT EXISTS (SELECT 1 FROM RequestTypes rt WHERE rt.Id = r.RequestTypeId);

SELECT
    CheckName,
    OrphanCount,
    CASE WHEN OrphanCount = 0 THEN 'OK' ELSE '** ORPHANS FOUND **' END AS [Status]
FROM @fkChecks;

-- ═══════════════════════════════════════════════════════════════════════════
-- SUMMARY
-- ═══════════════════════════════════════════════════════════════════════════
PRINT '';
PRINT '================================================================';
PRINT '  VALIDATION COMPLETE';
PRINT '  Review all "** MISSING **" or "** LOW **" entries above.';
PRINT '  If any critical checks failed, do NOT proceed with go-live.';
PRINT '================================================================';
