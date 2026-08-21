-- ============================================================================
-- ███  MANUAL DATA REPAIR — ROLLBACK  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- Reverts po-flow-req193-194-supplier-po-repair.sql for the SAME two groups only,
-- restoring the exact reviewed pre-repair state:
--   REQ-31/07/2026-193 (group f20b272f-00d9-4a31-a9fc-948ac4d30f8c):
--     SupplierId NULL, SupplierNameSnapshot N'Fornecedor não definido',
--     SupplierNifSnapshot NULL, PurchaseOrderNumber N'FT 26/72087'
--   REQ-31/07/2026-194 (group a535dabd-ea4e-4749-ab0f-1da3d136fd4f):
--     SupplierId NULL, SupplierNameSnapshot N'Fornecedor não definido',
--     SupplierNifSnapshot NULL, PurchaseOrderNumber N'FT 73094'
-- and removing ONLY the two audit rows tagged [HIST-SUPPLIER-PO-REQ-193] /
-- [HIST-SUPPLIER-PO-REQ-194].
-- Note: per the established repair pattern, UpdatedAtUtc/UpdatedByUserId are
-- stamped at rollback time (pre-repair bookkeeping values are not preserved).
--
-- SINGLE TRANSACTION, all-or-nothing: operates only when BOTH rows currently
-- match the exact values the repair wrote (supplier 45 + master snapshots + the
-- reviewed new PO numbers); any divergence aborts everything for manual review.
-- Statuses/workflow untouched (ADVANCE_PAYMENT_REQUIRED preserved). Idempotent
-- if both are already at the reviewed pre-repair state.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial -E -b -i po-flow-req193-194-supplier-po-rollback.sql -v actor="<admin user guid>"
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

DECLARE @targets TABLE (
    RequestNumber NVARCHAR(50) PRIMARY KEY,
    ExpectedGroupId UNIQUEIDENTIFIER,
    RepairedPo NVARCHAR(100),
    OriginalPo NVARCHAR(100),
    AuditTag NVARCHAR(40)
);
INSERT INTO @targets VALUES
 (N'REQ-31/07/2026-193', 'f20b272f-00d9-4a31-a9fc-948ac4d30f8c', N'ECF11 2026/420', N'FT 26/72087', N'[HIST-SUPPLIER-PO-REQ-193]'),
 (N'REQ-31/07/2026-194', 'a535dabd-ea4e-4749-ab0f-1da3d136fd4f', N'ECF11 2026/38',  N'FT 73094',    N'[HIST-SUPPLIER-PO-REQ-194]');

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @inRepairedState INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r        ON r.RequestNumber = t.RequestNumber
         JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         JOIN Suppliers s       ON s.Id = 45
         WHERE g.SupplierId = 45
           AND g.SupplierNameSnapshot = s.Name
           AND g.SupplierNifSnapshot = N'5417061590'
           AND g.PurchaseOrderNumber = t.RepairedPo);
    DECLARE @inOriginalState INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r        ON r.RequestNumber = t.RequestNumber
         JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         WHERE g.SupplierId IS NULL
           AND g.SupplierNameSnapshot = N'Fornecedor não definido'
           AND g.SupplierNifSnapshot IS NULL
           AND g.PurchaseOrderNumber = t.OriginalPo);

    IF @inOriginalState = 2 AND @inRepairedState = 0
    BEGIN
        PRINT 'ALREADY_ROLLED_BACK: both groups are at the reviewed pre-repair state — nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF @inRepairedState <> 2
    BEGIN
        PRINT CONCAT('ABORTED (MANUAL_REVIEW_REQUIRED): rows in repaired state = ', @inRepairedState,
                     ' of 2 — current values do not match what this repair wrote. Nothing written, rolled back.');
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.SupplierId = NULL,
            g.SupplierNameSnapshot = N'Fornecedor não definido',
            g.SupplierNifSnapshot = NULL,
            g.PurchaseOrderNumber = t.OriginalPo,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN @targets t ON t.ExpectedGroupId = g.Id
        WHERE g.SupplierId = 45
          AND g.SupplierNifSnapshot = N'5417061590'
          AND g.PurchaseOrderNumber = t.RepairedPo;

        IF @@ROWCOUNT <> 2
        BEGIN
            PRINT 'ABORTED: rollback UPDATE affected a row count different from 2 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            DELETE h
            FROM RequestStatusHistories h
            JOIN Requests r  ON r.Id = h.RequestId
            JOIN @targets t  ON t.RequestNumber = r.RequestNumber
            WHERE h.ActionTaken = 'DATA_INTEGRITY_REPAIR'
              AND h.Comment LIKE REPLACE(t.AuditTag, '[', '[[]') + '%';

            PRINT CONCAT('ROLLED BACK: both groups restored to the reviewed pre-repair state; ', @@ROWCOUNT, ' tagged audit row(s) removed.');
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
WHERE g.Id IN ('f20b272f-00d9-4a31-a9fc-948ac4d30f8c','a535dabd-ea4e-4749-ab0f-1da3d136fd4f')
ORDER BY r.RequestNumber;
