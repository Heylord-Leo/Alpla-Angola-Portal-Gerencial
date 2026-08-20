-- ============================================================================
-- ███  MANUAL DATA REPAIR — ROLLBACK  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- Reverts po-flow-population-b-supplier-repair.sql for the SAME six groups only,
-- restoring the exact prior state captured by the 2026-08-20 review:
--   SupplierId -> NULL
--   SupplierNameSnapshot -> N'Fornecedor não definido'   (legacy placeholder)
--   SupplierNifSnapshot  -> NULL
-- and removing ONLY the audit rows carrying the [POP-B-SUPPLIER-REQ-***] tags.
--
-- SINGLE TRANSACTION, all-or-nothing: it operates only when ALL six rows currently
-- match the exact values the repair wrote (expected SupplierId + supplier NIF snapshot);
-- any divergence aborts everything for manual review. Statuses/workflow untouched.
-- Idempotent: if all six are already back to the original legacy state, it skips.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-population-b-supplier-repair-rollback.sql -v actor="<admin user guid>"
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
 (N'REQ-03/08/2026-208', '2bbeb116-7742-4b2d-aece-5c339bc418d8', 34,  N'5410002857', N'[POP-B-SUPPLIER-REQ-208]'),
 (N'REQ-05/08/2026-215', '3d3c7558-7987-44d2-ad2e-5c7c5d70f16f', 34,  N'5410002857', N'[POP-B-SUPPLIER-REQ-215]'),
 (N'REQ-06/08/2026-222', 'db7e000c-79d4-41fb-b370-55b8d8c883b2', 34,  N'5410002857', N'[POP-B-SUPPLIER-REQ-222]'),
 (N'REQ-12/08/2026-237', '63a27af3-ff62-421f-966e-e212188a1bce', 257, N'5417101524', N'[POP-B-SUPPLIER-REQ-237]'),
 (N'REQ-12/08/2026-238', 'ecf53b7a-16ce-4844-92ba-781cd22df721', 257, N'5417101524', N'[POP-B-SUPPLIER-REQ-238]'),
 (N'REQ-12/08/2026-241', '72e38cf2-f445-4462-9435-b86d59a195ea', 257, N'5417101524', N'[POP-B-SUPPLIER-REQ-241]');

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

    IF @inOriginalState = 6 AND @inRepairedState = 0
    BEGIN
        PRINT 'ALREADY_ROLLED_BACK: all six groups are at the original legacy state — nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF @inRepairedState <> 6
    BEGIN
        PRINT CONCAT('ABORTED (MANUAL_REVIEW_REQUIRED): rows in repaired state = ', @inRepairedState,
                     ' of 6 — current values do not match what this repair wrote. Nothing written, rolled back.');
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

        IF @@ROWCOUNT <> 6
        BEGIN
            PRINT 'ABORTED: rollback UPDATE affected a row count different from 6 — rolled back, nothing persisted.';
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

            PRINT CONCAT('ROLLED BACK: six groups restored to the legacy state; ', @@ROWCOUNT, ' tagged audit row(s) removed.');
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
WHERE g.Id IN ('2bbeb116-7742-4b2d-aece-5c339bc418d8','3d3c7558-7987-44d2-ad2e-5c7c5d70f16f',
               'db7e000c-79d4-41fb-b370-55b8d8c883b2','63a27af3-ff62-421f-966e-e212188a1bce',
               'ecf53b7a-16ce-4844-92ba-781cd22df721','72e38cf2-f445-4462-9435-b86d59a195ea')
ORDER BY r.RequestNumber;
