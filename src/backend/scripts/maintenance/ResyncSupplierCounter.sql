/*
====================================================================================================
RESYNC SUPPLIER COUNTER - Alpla Angola Portal Gerencial
====================================================================================================
Description:
    Finds the highest existing SUP- numeric suffix and updates the SystemCounters 
    table to ensure the next generation is unique.
====================================================================================================
*/

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @MaxSeq INT = 0;

    -- Extract numeric portion safely from SUP-XXXXXX format
    SELECT @MaxSeq = MAX(CAST(SUBSTRING(PortalCode, 5, 6) AS INT))
    FROM Suppliers
    WHERE PortalCode LIKE 'SUP-%' 
      AND LEN(PortalCode) = 10 
      AND ISNUMERIC(SUBSTRING(PortalCode, 5, 6)) = 1;

    PRINT 'Highest existing sequence detected: ' + CAST(@MaxSeq AS VARCHAR);

    IF EXISTS (SELECT * FROM SystemCounters WHERE Id = 'SUPPLIER_PORTAL_CODE')
    BEGIN
        UPDATE SystemCounters 
        SET [CurrentValue] = @MaxSeq, 
            [LastUpdatedUtc] = GETUTCDATE() 
        WHERE Id = 'SUPPLIER_PORTAL_CODE';
        PRINT 'Updated existing SUPPLIER_PORTAL_CODE counter to ' + CAST(@MaxSeq AS VARCHAR);
    END
    ELSE
    BEGIN
        INSERT INTO SystemCounters (Id, [CurrentValue], [LastUpdatedUtc])
        VALUES ('SUPPLIER_PORTAL_CODE', @MaxSeq, GETUTCDATE());
        PRINT 'Created new SUPPLIER_PORTAL_CODE counter starting at ' + CAST(@MaxSeq AS VARCHAR);
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    PRINT 'Error during resync: ' + @ErrorMessage;
    THROW;
END CATCH
