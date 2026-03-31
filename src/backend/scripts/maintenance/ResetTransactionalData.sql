/*
====================================================================================================
RESET TRANSACTIONAL DATA - Alpla Angola Portal Gerencial
====================================================================================================
Description: 
    This script removes all operational/transactional records from the database, effectively
    resetting the environment to a "fresh run" state.

Transactional Data (DELETED):
    - Requests and their associations (Line Items, Attachments, Status Histories)
    - Quotations and their items
    - Notifications and administrative/system logs
    - Sequence counters for request numbers

Master/Configuration Data (PRESERVED):
    - Users, Roles, Permissions, and Organizational Structure (Companies, Plants, Departments)
    - Organizational units and organizational scopes
    - Master Data: Cost Centers, IVA Rates, Units, Currencies, Suppliers
    - Configuration: Request Types, Statuses, Capex/Opex classifications, and Need Levels
    - Migration history and database schema structure

Usage: 
    Execute this script within the target database (AlplaPortalV1) using SQL Server Management Studio 
    or any SQL-compatible tool.
====================================================================================================
*/

-- Disable constraints temporarily to ease cleanup (not strictly necessary but safer for complex FKs)
BEGIN TRANSACTION;
BEGIN TRY

    PRINT '--- Resetting Transactional Data ---';

    -- 1. Notifications
    DELETE FROM InformationalNotifications;
    PRINT 'Cleaned InformationalNotifications';

    -- 2. History
    DELETE FROM RequestStatusHistories;
    PRINT 'Cleaned RequestStatusHistories';

    -- 3. Quotations
    DELETE FROM QuotationItems;
    DELETE FROM Quotations;
    PRINT 'Cleaned Quotations and QuotationItems';

    -- 4. Requests (Cascading cleanup manually)
    DELETE FROM RequestLineItems;
    DELETE FROM RequestAttachments;
    DELETE FROM Requests;
    PRINT 'Cleaned Requests and their line items and attachments';

    -- 5. Logs
    DELETE FROM AdminLogEntries;
    DELETE FROM LogEntries;
    PRINT 'Cleaned AdminLogEntries and LogEntries';

    -- 6. Counters
    -- Reset the Pedido # sequence
    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'SystemCounters')
    BEGIN
        UPDATE SystemCounters SET [CurrentValue] = 0;
        PRINT 'Reset SystemCounters to 0';
    END

    -- Final Check
    PRINT 'Transactional cleanup complete.';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    PRINT 'Error during cleanup: ' + @ErrorMessage;
    THROW;
END CATCH
