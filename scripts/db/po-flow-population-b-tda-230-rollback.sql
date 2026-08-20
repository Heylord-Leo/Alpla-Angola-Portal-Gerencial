-- ============================================================================
-- ███  MANUAL DATA REPAIR — ROLLBACK  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- Reverts po-flow-population-b-tda-230-repair.sql for the SAME single group only,
-- restoring the exact prior state captured by the 2026-08-20 review:
--   SupplierId -> NULL
--   SupplierNameSnapshot -> N'Fornecedor não definido'   (legacy placeholder)
--   SupplierNifSnapshot  -> NULL
-- and removing ONLY the audit row tagged [POP-B-SUPPLIER-REQ-230].
--
-- SINGLE TRANSACTION, all-or-nothing: operates only when the row currently matches the
-- exact values the repair wrote; any divergence aborts everything for manual review.
-- Statuses/workflow untouched. Idempotent if already at the legacy state.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-population-b-tda-230-rollback.sql -v actor="<admin user guid>"
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
 (N'REQ-11/08/2026-230', 'f28ec394-3553-43ff-b492-0ae6524d238f', 34, N'5410002857', N'[POP-B-SUPPLIER-REQ-230]');

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

    IF @inOriginalState = 1 AND @inRepairedState = 0
    BEGIN
        PRINT 'ALREADY_ROLLED_BACK: the group is at the original legacy state — nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF @inRepairedState <> 1
    BEGIN
        PRINT CONCAT('ABORTED (MANUAL_REVIEW_REQUIRED): rows in repaired state = ', @inRepairedState,
                     ' of 1 — current value does not match what this repair wrote. Nothing written, rolled back.');
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

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'ABORTED: rollback UPDATE affected a row count different from 1 — rolled back, nothing persisted.';
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

            PRINT CONCAT('ROLLED BACK: group restored to the legacy state; ', @@ROWCOUNT, ' tagged audit row(s) removed.');
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
WHERE g.Id = 'f28ec394-3553-43ff-b492-0ae6524d238f';
