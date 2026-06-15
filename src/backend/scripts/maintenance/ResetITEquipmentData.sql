/*
====================================================================================================
RESET I.T EQUIPMENT OPERATIONAL DATA — Alpla Angola Portal Gerencial
====================================================================================================
Description: 
    This script removes all IT Equipment operational/transactional records, effectively resetting
    the IT asset inventory to a clean state while preserving all master/catalog data.

PRESERVED TABLES (Master/Catalog):
    - Companies, Plants, Departments
    - ITEquipmentTypes, ITEquipmentManufacturers, ITEquipmentModels
    - ITEquipmentProcessors, ITEquipmentMemoryOptions
    - Users, Roles, UserRoleAssignments, UserPlantScopes, UserDepartmentScopes
    - SystemCounters (non-IT keys preserved)
    - All other module tables

DELETED TABLES (Operational — in FK dependency order):
    1. ITEquipmentDeliveryItems
    2. ITEquipmentDocuments
    3. ITEquipmentMovementLogs
    4. ITEquipmentAssignments
    5. ITEquipmentDeliveryTerms
    6. ITEquipmentAcquisitions
    7. ITEquipments

PHYSICAL FILES NOTE:
    IT equipment documents are stored in: data/attachments/it-equipment/
    This script does NOT delete physical files.
    Before running in PROD:
      1. Back up the database
      2. Back up the folder: data/attachments/it-equipment/
      3. After successful execution, manually archive or delete files from that folder

EXECUTION ORDER:
    1. DEV   — Execute and validate
    2. TEST  — Back up DB first, then execute and validate
    3. PROD  — Full backup (DB + files), then execute with supervision

IDEMPOTENCY: Safe to run when tables are already empty.
====================================================================================================
*/

-- ──────────────────────────────────────────────────────────────────────────────
-- PRE-CHECK: Row counts before deletion
-- ──────────────────────────────────────────────────────────────────────────────
PRINT '============================================================';
PRINT '  PRE-CHECK: Current row counts (before deletion)';
PRINT '============================================================';

SELECT 'ITEquipments' AS [Table], COUNT(*) AS [RowCount] FROM ITEquipments
UNION ALL
SELECT 'ITEquipmentAssignments', COUNT(*) FROM ITEquipmentAssignments
UNION ALL
SELECT 'ITEquipmentMovementLogs', COUNT(*) FROM ITEquipmentMovementLogs
UNION ALL
SELECT 'ITEquipmentAcquisitions', COUNT(*) FROM ITEquipmentAcquisitions
UNION ALL
SELECT 'ITEquipmentDocuments', COUNT(*) FROM ITEquipmentDocuments
UNION ALL
SELECT 'ITEquipmentDeliveryTerms', COUNT(*) FROM ITEquipmentDeliveryTerms
UNION ALL
SELECT 'ITEquipmentDeliveryItems', COUNT(*) FROM ITEquipmentDeliveryItems
UNION ALL
SELECT 'SystemCounters (IT_ASSET keys)', COUNT(*) FROM SystemCounters WHERE Id LIKE 'IT_ASSET:%'
UNION ALL
SELECT 'SystemCounters (DELIVERY_TERM)', COUNT(*) FROM SystemCounters WHERE Id LIKE 'DELIVERY_TERM%';

PRINT '';
PRINT '============================================================';
PRINT '  PRESERVED: Master/Catalog data row counts';
PRINT '============================================================';

SELECT 'Companies' AS [Table], COUNT(*) AS [RowCount] FROM Companies
UNION ALL
SELECT 'Plants', COUNT(*) FROM Plants
UNION ALL
SELECT 'Departments', COUNT(*) FROM Departments
UNION ALL
SELECT 'ITEquipmentTypes', COUNT(*) FROM ITEquipmentTypes
UNION ALL
SELECT 'ITEquipmentManufacturers', COUNT(*) FROM ITEquipmentManufacturers
UNION ALL
SELECT 'ITEquipmentModels', COUNT(*) FROM ITEquipmentModels
UNION ALL
SELECT 'ITEquipmentProcessors', COUNT(*) FROM ITEquipmentProcessors
UNION ALL
SELECT 'ITEquipmentMemoryOptions', COUNT(*) FROM ITEquipmentMemoryOptions
UNION ALL
SELECT 'Users', COUNT(*) FROM Users
UNION ALL
SELECT 'Roles', COUNT(*) FROM Roles;

-- ──────────────────────────────────────────────────────────────────────────────
-- DELETION: Wrapped in transaction
-- ──────────────────────────────────────────────────────────────────────────────
BEGIN TRANSACTION;
BEGIN TRY
    PRINT '';
    PRINT '============================================================';
    PRINT '  EXECUTING: IT Equipment Operational Data Reset';
    PRINT '============================================================';

    -- 1. Delivery Items (child of DeliveryTerm + Equipment + Assignment)
    DELETE FROM ITEquipmentDeliveryItems;
    PRINT '  [1/7] Deleted ITEquipmentDeliveryItems: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- 2. Documents (child of Equipment, Acquisition, Assignment, DeliveryTerm)
    --    Must be deleted before DeliveryTerms because DeliveryTerm has FK to Document (GeneratedDocumentId, SignedDocumentId)
    --    But Document also has FK to DeliveryTerm. Temporarily disable the constraint.
    --    Actually, DeliveryTerm.GeneratedDocumentId → Document is a nullable FK with NoAction delete.
    --    Document.DeliveryTermId → DeliveryTerm is also nullable FK with NoAction delete.
    --    Since we're deleting ALL of both, order doesn't matter for NoAction FKs.
    --    But Document has FK Restrict on Equipment, so delete Documents before Equipment.
    DELETE FROM ITEquipmentDocuments;
    PRINT '  [2/7] Deleted ITEquipmentDocuments: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- 3. Movement Logs (child of Equipment — cascade, but explicit delete is cleaner)
    DELETE FROM ITEquipmentMovementLogs;
    PRINT '  [3/7] Deleted ITEquipmentMovementLogs: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- 4. Assignments (child of Equipment — cascade)
    DELETE FROM ITEquipmentAssignments;
    PRINT '  [4/7] Deleted ITEquipmentAssignments: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- 5. Delivery Terms (has FK to Document for GeneratedDocumentId/SignedDocumentId — already deleted)
    DELETE FROM ITEquipmentDeliveryTerms;
    PRINT '  [5/7] Deleted ITEquipmentDeliveryTerms: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- 6. Acquisitions (1:1 with Equipment — cascade)
    DELETE FROM ITEquipmentAcquisitions;
    PRINT '  [6/7] Deleted ITEquipmentAcquisitions: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- 7. Equipment (root table)
    DELETE FROM ITEquipments;
    PRINT '  [7/7] Deleted ITEquipments: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- 8. Reset IT-specific SystemCounters
    PRINT '';
    PRINT '  Resetting IT-specific SystemCounters...';

    -- Reset IT Asset sequence counters
    DELETE FROM SystemCounters WHERE Id LIKE 'IT_ASSET:%';
    PRINT '  Deleted IT_ASSET counters: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';

    -- Reset Delivery Term counter (if it exists)
    IF EXISTS (SELECT 1 FROM SystemCounters WHERE Id LIKE 'DELIVERY_TERM%')
    BEGIN
        UPDATE SystemCounters SET [CurrentValue] = 0 WHERE Id LIKE 'DELIVERY_TERM%';
        PRINT '  Reset DELIVERY_TERM counter(s) to 0';
    END

    -- IMPORTANT: Do NOT touch other counters (GLOBAL_REQUEST_COUNTER, SUPPLIER_COUNTER, etc.)
    PRINT '  Non-IT SystemCounters preserved.';

    PRINT '';
    PRINT '============================================================';
    PRINT '  SUCCESS: IT Equipment operational data reset complete.';
    PRINT '============================================================';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrorState INT = ERROR_STATE();
    PRINT '';
    PRINT '!!! ERROR DURING RESET !!!';
    PRINT 'Message: ' + @ErrorMessage;
    PRINT 'Transaction has been ROLLED BACK. No data was deleted.';
    THROW;
END CATCH

-- ──────────────────────────────────────────────────────────────────────────────
-- POST-CHECK: Row counts after deletion
-- ──────────────────────────────────────────────────────────────────────────────
PRINT '';
PRINT '============================================================';
PRINT '  POST-CHECK: Row counts after deletion';
PRINT '============================================================';

SELECT 'ITEquipments' AS [Table], COUNT(*) AS [RowCount] FROM ITEquipments
UNION ALL
SELECT 'ITEquipmentAssignments', COUNT(*) FROM ITEquipmentAssignments
UNION ALL
SELECT 'ITEquipmentMovementLogs', COUNT(*) FROM ITEquipmentMovementLogs
UNION ALL
SELECT 'ITEquipmentAcquisitions', COUNT(*) FROM ITEquipmentAcquisitions
UNION ALL
SELECT 'ITEquipmentDocuments', COUNT(*) FROM ITEquipmentDocuments
UNION ALL
SELECT 'ITEquipmentDeliveryTerms', COUNT(*) FROM ITEquipmentDeliveryTerms
UNION ALL
SELECT 'ITEquipmentDeliveryItems', COUNT(*) FROM ITEquipmentDeliveryItems
UNION ALL
SELECT 'SystemCounters (IT_ASSET keys)', COUNT(*) FROM SystemCounters WHERE Id LIKE 'IT_ASSET:%';

PRINT '';
PRINT '  PRESERVED data verification:';

SELECT 'Companies' AS [Table], COUNT(*) AS [RowCount] FROM Companies
UNION ALL
SELECT 'Plants', COUNT(*) FROM Plants
UNION ALL
SELECT 'Departments', COUNT(*) FROM Departments
UNION ALL
SELECT 'ITEquipmentTypes', COUNT(*) FROM ITEquipmentTypes
UNION ALL
SELECT 'ITEquipmentManufacturers', COUNT(*) FROM ITEquipmentManufacturers
UNION ALL
SELECT 'ITEquipmentModels', COUNT(*) FROM ITEquipmentModels
UNION ALL
SELECT 'ITEquipmentProcessors', COUNT(*) FROM ITEquipmentProcessors
UNION ALL
SELECT 'ITEquipmentMemoryOptions', COUNT(*) FROM ITEquipmentMemoryOptions
UNION ALL
SELECT 'Users', COUNT(*) FROM Users
UNION ALL
SELECT 'Roles', COUNT(*) FROM Roles
UNION ALL
SELECT 'SystemCounters (total)', COUNT(*) FROM SystemCounters;

PRINT '';
PRINT '============================================================';
PRINT '  DONE. System is ready for fresh IT Equipment inventory.';
PRINT '============================================================';
PRINT '';
PRINT '  REMINDER: If running in PROD, manually archive/remove files from:';
PRINT '            data/attachments/it-equipment/';
PRINT '';
