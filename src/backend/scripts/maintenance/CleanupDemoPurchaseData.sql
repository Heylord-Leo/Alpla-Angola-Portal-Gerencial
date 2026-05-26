-- ====================================================================
-- Script Name: CleanupDemoPurchaseData.sql
-- Description: Safely deletes all demo purchase data created by the DemoDataGenerator tool.
--              It uses the '[DEMO]' prefix in the Requests.Title to identify demo records.
--              It will NOT delete any master data (Suppliers, Items, Users, etc.).
-- ====================================================================

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT 'Starting cleanup of demo purchase data...';

    -- 1. Identify Demo Requests
    DECLARE @DemoRequestIds TABLE (Id UNIQUEIDENTIFIER);
    INSERT INTO @DemoRequestIds (Id)
    SELECT Id FROM Requests WHERE Title LIKE '[DEMO]%';

    DECLARE @Count INT = (SELECT COUNT(*) FROM @DemoRequestIds);
    PRINT 'Found ' + CAST(@Count AS VARCHAR) + ' demo requests to delete.';

    IF @Count > 0
    BEGIN
        -- 2. Delete Request Attachments
        PRINT 'Deleting RequestAttachments...';
        DELETE FROM RequestAttachments 
        WHERE RequestId IN (SELECT Id FROM @DemoRequestIds);

        -- 3. Delete Quotations
        PRINT 'Deleting Quotations...';
        DELETE FROM Quotations
        WHERE RequestId IN (SELECT Id FROM @DemoRequestIds);

        -- 4. Delete Request Status Histories
        PRINT 'Deleting RequestStatusHistories...';
        DELETE FROM RequestStatusHistories
        WHERE RequestId IN (SELECT Id FROM @DemoRequestIds);

        -- 5. Delete Request Line Items
        PRINT 'Deleting RequestLineItems...';
        DELETE FROM RequestLineItems
        WHERE RequestId IN (SELECT Id FROM @DemoRequestIds);

        -- 6. Delete the Requests themselves
        PRINT 'Deleting Requests...';
        DELETE FROM Requests
        WHERE Id IN (SELECT Id FROM @DemoRequestIds);

        PRINT 'Cleanup completed successfully.';
    END
    ELSE
    BEGIN
        PRINT 'No demo requests found. Nothing to delete.';
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT 'Error occurred during cleanup. Transaction rolled back.';
    PRINT ERROR_MESSAGE();
END CATCH;
