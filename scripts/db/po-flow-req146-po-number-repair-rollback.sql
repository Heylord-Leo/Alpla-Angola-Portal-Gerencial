-- ============================================================================
-- ███  MANUAL DATA REPAIR — ROLLBACK  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- Reverts po-flow-req146-po-number-repair.sql for the SAME single group only,
-- restoring the exact reviewed pre-repair state:
--   PurchaseOrderNumber -> N'2026A/11'
-- and removing ONLY the audit row tagged [HIST-PO-REQ-146].
-- Supplier fields were never modified by the repair and are not modified here.
-- Note: per the established repair pattern, UpdatedAtUtc/UpdatedByUserId are
-- stamped at rollback time (pre-repair bookkeeping values are not preserved).
--
-- SINGLE TRANSACTION, all-or-nothing: operates only when the row currently
-- matches the exact values the repair wrote (PO 'ECF10 2026A/11' with supplier 14
-- unchanged); any divergence aborts everything for manual review.
-- Statuses/workflow untouched. Idempotent if already at the pre-repair state.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-req146-po-number-repair-rollback.sql -v actor="<admin user guid>"
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @connectedDb SYSNAME = DB_NAME();
IF @connectedDb NOT IN ('Portal-Gerencial-Test', 'Portal-Gerencial')
BEGIN
    RAISERROR('ABORTED: connected database is [%s] on server [%s] — the ONLY accepted Portal databases are [Portal-Gerencial-Test] (TEST rehearsal) and [Portal-Gerencial] (PROD). No bypass exists.', 16, 1, @connectedDb, @@SERVERNAME) WITH NOWAIT;
    SET NOEXEC ON;
END
PRINT CONCAT('Connected: server=', @@SERVERNAME,
             ' | database=', DB_NAME(),
             ' | original_login=', ORIGINAL_LOGIN(),
             ' | context=', CASE DB_NAME() WHEN 'Portal-Gerencial' THEN 'PROD'
                                           WHEN 'Portal-Gerencial-Test' THEN 'TEST'
                                           ELSE 'DISALLOWED' END);

DECLARE @actor UNIQUEIDENTIFIER = TRY_CAST('$(actor)' AS UNIQUEIDENTIFIER);
IF @actor IS NULL OR NOT EXISTS (SELECT 1 FROM Users WHERE Id = @actor)
BEGIN
    PRINT 'ABORTED: pass a valid administrator user id via  -v actor="<guid>"  (must exist in Users).';
    SET NOEXEC ON;
END

DECLARE @groupId UNIQUEIDENTIFIER = 'ba7db94e-1c21-497a-bb19-f0dc77bb5391';
DECLARE @supplierId INT           = 14;
DECLARE @repairedPo NVARCHAR(100) = N'ECF10 2026A/11';
DECLARE @originalPo NVARCHAR(100) = N'2026A/11';

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @inRepairedState BIT = CASE WHEN EXISTS (
        SELECT 1 FROM RequestPoGroups g
        WHERE g.Id = @groupId AND g.PurchaseOrderNumber = @repairedPo
          AND g.SupplierId = @supplierId) THEN 1 ELSE 0 END;
    DECLARE @inOriginalState BIT = CASE WHEN EXISTS (
        SELECT 1 FROM RequestPoGroups g
        WHERE g.Id = @groupId AND g.PurchaseOrderNumber = @originalPo
          AND g.SupplierId = @supplierId) THEN 1 ELSE 0 END;

    IF @inOriginalState = 1 AND @inRepairedState = 0
    BEGIN
        PRINT 'ALREADY_ROLLED_BACK: the group is at the reviewed pre-repair state — nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF @inRepairedState <> 1
    BEGIN
        PRINT 'ABORTED (MANUAL_REVIEW_REQUIRED): current values do not match what this repair wrote. Nothing written, rolled back.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.PurchaseOrderNumber = @originalPo,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        WHERE g.Id = @groupId
          AND g.PurchaseOrderNumber = @repairedPo
          AND g.SupplierId = @supplierId;

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'ABORTED: rollback UPDATE affected a row count different from 1 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            DELETE h
            FROM RequestStatusHistories h
            JOIN Requests r ON r.Id = h.RequestId
            WHERE r.RequestNumber = N'REQ-23/07/2026-146'
              AND h.ActionTaken = 'DATA_INTEGRITY_REPAIR'
              AND h.Comment LIKE '[[]HIST-PO-REQ-146]%';

            PRINT CONCAT('ROLLED BACK: group PO restored to ''2026A/11''; ', @@ROWCOUNT, ' tagged audit row(s) removed.');
            COMMIT TRANSACTION;
        END
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('ERROR — entire rollback aborted: ', ERROR_MESSAGE());
END CATCH;

-- ── After-state (read-only) ──
SELECT r.RequestNumber, rs.Code AS RequestStatus, g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId, g.SupplierNameSnapshot, g.SupplierNifSnapshot, g.PurchaseOrderNumber
FROM RequestPoGroups g
JOIN Requests r         ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Id = 'ba7db94e-1c21-497a-bb19-f0dc77bb5391';
