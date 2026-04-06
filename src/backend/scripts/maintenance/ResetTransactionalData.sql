/*
====================================================================================================
RESET TRANSACTIONAL DATA - Alpla Angola Portal Gerencial
====================================================================================================
Description: 
    This script removes all operational/transactional records from the database, effectively
    resetting the environment to a "fresh run" state.
====================================================================================================
*/

BEGIN TRANSACTION;
BEGIN TRY
    PRINT '--- Starting Transactional Data Reset ---';

    -- Safety Enhancement: Temporarily disable check constraints to prevent FK conflicts during cleanup
    -- This is safer than relying solely on deletion order for complex/circular relationships.
    EXEC sp_MSforeachtable "ALTER TABLE ? NOCHECK CONSTRAINT ALL";
    PRINT 'Constraints disabled.';

    -- 1. Notifications & User Preferences (Transactional part)
    DELETE FROM InformationalNotifications;
    PRINT 'Cleaned InformationalNotifications';

    -- 2. History & Audit
    DELETE FROM RequestStatusHistories;
    DELETE FROM AdminLogEntries;
    DELETE FROM LogEntries;
    PRINT 'Cleaned Histories and Logs';

    -- 3. Quotations (Children before Parent)
    DELETE FROM QuotationItems;
    DELETE FROM Quotations;
    PRINT 'Cleaned Quotations and QuotationItems';

    -- 4. Requests (Children before Parent)
    DELETE FROM RequestLineItems;
    DELETE FROM RequestAttachments;
    DELETE FROM Requests;
    PRINT 'Cleaned Requests and their line items and attachments';

    -- 5. Counters Reset
    -- Resetting the Pedido # sequence back to zero for the new validation run.
    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'SystemCounters')
    BEGIN
        -- Reset only transactional counters. Master Data (Suppliers) must be preserved.
        UPDATE SystemCounters 
        SET [CurrentValue] = 0 
        WHERE Id = 'GLOBAL_REQUEST_COUNTER';
        
        PRINT 'Reset GLOBAL_REQUEST_COUNTER to 0';
    END

    -- Final Check: Re-enable ALL constraints to preserve integrity for the new run
    EXEC sp_MSforeachtable "ALTER TABLE ? CHECK CONSTRAINT ALL";
    PRINT 'Constraints re-enabled.';

    PRINT '--- Transactional cleanup complete. System is ready for a fresh run. ---';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    -- Ensure constraints are re-enabled even on failure
    EXEC sp_MSforeachtable "ALTER TABLE ? CHECK CONSTRAINT ALL";
    
    ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    PRINT 'Error during cleanup: ' + @ErrorMessage;
    THROW;
END CATCH
