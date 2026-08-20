-- ============================================================================
-- ███  MANUAL DATA REPAIR — ROLLBACK  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- Reverts po-flow-po-number-repair.sql for the SAME allow-list only:
--   REQ-20/07/2026-098  'ECF10 2026/230' -> '5002736705'
--   REQ-20/07/2026-101  'ECF11 2026/386' -> '5001713205'
-- and removes ONLY the audit rows tagged [PO-REPAIR-REQ-098]/[PO-REPAIR-REQ-101].
-- Guarded and idempotent: a group already holding the old value is skipped.
-- One transaction per request. Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial -E -b -i po-flow-po-number-repair-rollback.sql ^
--          -v actor="<admin user guid>"
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @connectedDb SYSNAME = DB_NAME();
IF @connectedDb NOT IN ('Portal-Gerencial', 'Portal-Gerencial-Dev-ProdClone')
BEGIN
    RAISERROR('ABORTED: connected database is [%s] on server [%s] — allowed: [Portal-Gerencial] (real) or [Portal-Gerencial-Dev-ProdClone] (rehearsal).', 16, 1, @connectedDb, @@SERVERNAME) WITH NOWAIT;
    SET NOEXEC ON;
END
PRINT CONCAT('Connected: server=', @@SERVERNAME, ' database=', DB_NAME(), ' login=', SYSTEM_USER);

DECLARE @actor UNIQUEIDENTIFIER = TRY_CAST('$(actor)' AS UNIQUEIDENTIFIER);
IF @actor IS NULL OR NOT EXISTS (SELECT 1 FROM Users WHERE Id = @actor)
BEGIN
    PRINT 'ABORTED: pass a valid administrator user id via  -v actor="<guid>"  (must exist in Users).';
    SET NOEXEC ON;
END

-- ════════════════════════ REQ-20/07/2026-098 ════════════════════════
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @g1 UNIQUEIDENTIFIER = 'f559b59c-867c-4fa8-a339-cece55e5cd7f';

    IF EXISTS (SELECT 1 FROM RequestPoGroups WHERE Id = @g1 AND PurchaseOrderNumber = N'5002736705')
    BEGIN
        PRINT 'REQ-098: already at the original value (5002736705) — skipping, nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF NOT EXISTS (
        SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
        WHERE g.Id = @g1 AND r.RequestNumber = 'REQ-20/07/2026-098'
          AND g.PurchaseOrderNumber = N'ECF10 2026/230')
    BEGIN
        PRINT 'REQ-098: current value is neither the repaired nor the original one — MANUAL REVIEW REQUIRED, rolled back.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE RequestPoGroups
        SET PurchaseOrderNumber = N'5002736705',
            UpdatedAtUtc = SYSUTCDATETIME(),
            UpdatedByUserId = @actor
        WHERE Id = @g1 AND PurchaseOrderNumber = N'ECF10 2026/230';

        DELETE FROM RequestStatusHistories
        WHERE RequestId = (SELECT Id FROM Requests WHERE RequestNumber = 'REQ-20/07/2026-098')
          AND ActionTaken = 'DATA_INTEGRITY_REPAIR'
          AND Comment LIKE '[[]PO-REPAIR-REQ-098]%';

        PRINT 'REQ-098: rolled back — ''ECF10 2026/230'' -> ''5002736705'' (repair audit row removed).';
        COMMIT TRANSACTION;
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('REQ-098: ERROR — rolled back: ', ERROR_MESSAGE());
END CATCH;

-- ════════════════════════ REQ-20/07/2026-101 ════════════════════════
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @g2 UNIQUEIDENTIFIER = 'cd2f005c-7283-4a82-8364-4ce99eb7cc6a';

    IF EXISTS (SELECT 1 FROM RequestPoGroups WHERE Id = @g2 AND PurchaseOrderNumber = N'5001713205')
    BEGIN
        PRINT 'REQ-101: already at the original value (5001713205) — skipping, nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF NOT EXISTS (
        SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
        WHERE g.Id = @g2 AND r.RequestNumber = 'REQ-20/07/2026-101'
          AND g.PurchaseOrderNumber = N'ECF11 2026/386')
    BEGIN
        PRINT 'REQ-101: current value is neither the repaired nor the original one — MANUAL REVIEW REQUIRED, rolled back.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE RequestPoGroups
        SET PurchaseOrderNumber = N'5001713205',
            UpdatedAtUtc = SYSUTCDATETIME(),
            UpdatedByUserId = @actor
        WHERE Id = @g2 AND PurchaseOrderNumber = N'ECF11 2026/386';

        DELETE FROM RequestStatusHistories
        WHERE RequestId = (SELECT Id FROM Requests WHERE RequestNumber = 'REQ-20/07/2026-101')
          AND ActionTaken = 'DATA_INTEGRITY_REPAIR'
          AND Comment LIKE '[[]PO-REPAIR-REQ-101]%';

        PRINT 'REQ-101: rolled back — ''ECF11 2026/386'' -> ''5001713205'' (repair audit row removed).';
        COMMIT TRANSACTION;
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('REQ-101: ERROR — rolled back: ', ERROR_MESSAGE());
END CATCH;

-- ── After-state (read-only) ──
SELECT r.RequestNumber, rs.Code AS RequestStatus, g.Id AS GroupId, g.Status AS GroupStatus,
       g.PurchaseOrderNumber
FROM RequestPoGroups g
JOIN Requests r         ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Id IN ('f559b59c-867c-4fa8-a339-cece55e5cd7f', 'cd2f005c-7283-4a82-8364-4ce99eb7cc6a');
