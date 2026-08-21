-- ============================================================================
-- ███  MANUAL DATA REPAIR — ROLLBACK  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- Reverts po-flow-req071-supplier-po-repair.sql for the SAME single group only,
-- restoring the exact reviewed pre-repair state:
--   SupplierId           -> NULL
--   SupplierNameSnapshot -> NULL   (this group had NO legacy placeholder)
--   SupplierNifSnapshot  -> NULL
--   PurchaseOrderNumber  -> NULL
-- and removing ONLY the audit row tagged [HIST-SUPPLIER-PO-REQ-071].
-- Note: per the established repair pattern, UpdatedAtUtc/UpdatedByUserId are
-- stamped at rollback time (pre-repair bookkeeping values are not preserved —
-- here both were NULL before the repair).
--
-- SINGLE TRANSACTION, all-or-nothing: operates only when the row currently matches
-- the exact values the repair wrote (supplier 257 + master snapshots + PO
-- 'ECF11 2026/371'); any divergence aborts everything for manual review.
-- Statuses/workflow untouched. Idempotent if already at the pre-repair state.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-req071-supplier-po-rollback.sql -v actor="<admin user guid>"
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

DECLARE @groupId UNIQUEIDENTIFIER = '2842545e-f766-4c9b-abc8-5a8fc75ac42f';
DECLARE @supplierId INT           = 257;
DECLARE @supplierNif NVARCHAR(50) = N'5417101524';
DECLARE @repairedPo NVARCHAR(100) = N'ECF11 2026/371';

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @inRepairedState BIT = CASE WHEN EXISTS (
        SELECT 1 FROM RequestPoGroups g JOIN Suppliers s ON s.Id = @supplierId
        WHERE g.Id = @groupId AND g.SupplierId = @supplierId
          AND g.SupplierNameSnapshot = s.Name AND g.SupplierNifSnapshot = @supplierNif
          AND g.PurchaseOrderNumber = @repairedPo) THEN 1 ELSE 0 END;
    DECLARE @inOriginalState BIT = CASE WHEN EXISTS (
        SELECT 1 FROM RequestPoGroups g
        WHERE g.Id = @groupId AND g.SupplierId IS NULL
          AND g.SupplierNameSnapshot IS NULL
          AND g.SupplierNifSnapshot IS NULL
          AND g.PurchaseOrderNumber IS NULL) THEN 1 ELSE 0 END;

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
        SET g.SupplierId = NULL,
            g.SupplierNameSnapshot = NULL,
            g.SupplierNifSnapshot = NULL,
            g.PurchaseOrderNumber = NULL,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        WHERE g.Id = @groupId
          AND g.SupplierId = @supplierId
          AND g.SupplierNifSnapshot = @supplierNif
          AND g.PurchaseOrderNumber = @repairedPo;

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
            WHERE r.RequestNumber = N'REQ-15/07/2026-071'
              AND h.ActionTaken = 'DATA_INTEGRITY_REPAIR'
              AND h.Comment LIKE '[[]HIST-SUPPLIER-PO-REQ-071]%';

            PRINT CONCAT('ROLLED BACK: group restored to the reviewed pre-repair state; ', @@ROWCOUNT, ' tagged audit row(s) removed.');
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
WHERE g.Id = '2842545e-f766-4c9b-abc8-5a8fc75ac42f';
