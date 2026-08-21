-- ============================================================================
-- ███  MANUAL DATA REPAIR — ROLLBACK  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- Reverts po-flow-final-two-supplier-repair.sql for the SAME two groups only,
-- restoring the exact prior state captured by the 2026-08-20 review:
--   SupplierId -> NULL
--   SupplierNameSnapshot -> N'Fornecedor não definido'   (legacy placeholder)
--   SupplierNifSnapshot  -> NULL
-- and removing ONLY the audit rows carrying the [HIST-SUPPLIER-REQ-178] /
-- [HIST-SUPPLIER-REQ-245] tags.
-- REQ-084 (CANCELLED, HISTORICAL_INERT_NO_REPAIR_RECOMMENDED), REQ-193/194 and
-- REQ-200 are NOT part of this package.
-- Note: per the established repair pattern, UpdatedAtUtc/UpdatedByUserId are
-- stamped at rollback time (pre-repair bookkeeping values are not preserved).
--
-- SINGLE TRANSACTION, all-or-nothing: it operates only when BOTH rows currently
-- match the exact values the repair wrote (expected SupplierId + supplier NIF
-- snapshot); any divergence aborts everything for manual review.
-- Statuses/workflow untouched. Idempotent: if both are already back to the
-- original legacy state, it skips.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-final-two-supplier-repair-rollback.sql -v actor="<admin user guid>"
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
    ExpectedSupplierId INT,
    ExpectedSupplierNif NVARCHAR(50),
    AuditTag NVARCHAR(40)
);
INSERT INTO @targets VALUES
 (N'REQ-29/07/2026-178', '3d67213e-daba-4615-a0fc-108b19ea1a3e', 66,  N'5417231983', N'[HIST-SUPPLIER-REQ-178]'),
 (N'REQ-12/08/2026-245', 'fe684497-448f-471a-8461-377ba3dc47c5', 159, N'5417386740', N'[HIST-SUPPLIER-REQ-245]');

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @inRepairedState INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r        ON r.RequestNumber = t.RequestNumber
         JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         WHERE g.SupplierId = t.ExpectedSupplierId AND g.SupplierNifSnapshot = t.ExpectedSupplierNif);
    DECLARE @inOriginalState INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r        ON r.RequestNumber = t.RequestNumber
         JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         WHERE g.SupplierId IS NULL
           AND g.SupplierNameSnapshot = N'Fornecedor não definido'
           AND g.SupplierNifSnapshot IS NULL);

    IF @inOriginalState = 2 AND @inRepairedState = 0
    BEGIN
        PRINT 'ALREADY_ROLLED_BACK: both groups are at the original legacy state — nothing written.';
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
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN @targets t ON t.ExpectedGroupId = g.Id
        WHERE g.SupplierId = t.ExpectedSupplierId AND g.SupplierNifSnapshot = t.ExpectedSupplierNif;

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

            PRINT CONCAT('ROLLED BACK: both groups restored to the legacy state; ', @@ROWCOUNT, ' tagged audit row(s) removed.');
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
       g.SupplierId, g.SupplierNameSnapshot, g.SupplierNifSnapshot
FROM RequestPoGroups g
JOIN Requests r         ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Id IN ('3d67213e-daba-4615-a0fc-108b19ea1a3e','fe684497-448f-471a-8461-377ba3dc47c5')
ORDER BY r.RequestNumber;
