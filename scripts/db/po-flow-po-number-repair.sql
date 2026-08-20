-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- PO-FLOW REPAIR — HISTORICAL P.O NUMBERS (allow-list: REQ-098 and REQ-101 ONLY)
--
--   REQ-20/07/2026-098  group f559b59c-867c-4fa8-a339-cece55e5cd7f
--     5002736705 (VM Santos NIF) -> 'ECF10 2026/230'   canonical ECF10-2026-230
--     evidence: 3 stored TYPE_PO attachments, identical SHA-256
--       f3e08253d89e91d5707de93867e6c55a6d1841515a973a3e7a7da360a46ba322,
--       three independent positive parses (reviewed 2026-08-20)
--
--   REQ-20/07/2026-101  group cd2f005c-7283-4a82-8364-4ce99eb7cc6a
--     5001713205 (Gasp NIF) -> 'ECF11 2026/386'        canonical ECF11-2026-386
--     evidence: 2 stored TYPE_PO attachments, identical SHA-256
--       cec3e78ba3ade8d73b7eccb239ce9d6f0ab68f7962c21d8e14440f323ad1d5d0,
--       SHA matched DB FileHash + positive parse (reviewed 2026-08-20)
--
-- Writes ONLY RequestPoGroups.PurchaseOrderNumber (plus UpdatedAtUtc/UpdatedByUserId
-- bookkeeping) + one DATA_INTEGRITY_REPAIR audit row per request (status unchanged:
-- PreviousStatusId = NewStatusId). Never touches request status, group status, supplier,
-- totals, attachments, approval or payment state.
--
-- Rows are RESOLVED from the live schema (RequestNumber + exact old value) and then
-- cross-checked against the reviewed GroupId — any drift aborts that transaction.
-- Idempotent: a group already carrying the corrected value is skipped without error.
-- One transaction per request. Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial -E -b -i po-flow-po-number-repair.sql ^
--          -v actor="<admin user guid>"
-- Rollback: po-flow-po-number-repair-rollback.sql (guarded, restores the old values).
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

-- ── Environment guard: refuse to run anywhere but the real database or its rehearsal clone ──
DECLARE @connectedDb SYSNAME = DB_NAME();
IF @connectedDb NOT IN ('Portal-Gerencial', 'Portal-Gerencial-Dev-ProdClone')
BEGIN
    RAISERROR('ABORTED: connected database is [%s] on server [%s] — allowed: [Portal-Gerencial] (real) or [Portal-Gerencial-Dev-ProdClone] (rehearsal).', 16, 1, @connectedDb, @@SERVERNAME) WITH NOWAIT;
    SET NOEXEC ON;
END
PRINT CONCAT('Connected: server=', @@SERVERNAME, ' database=', DB_NAME(), ' login=', SYSTEM_USER);

-- ── Audit actor (sqlcmd variable, must exist in Users) ──
DECLARE @actor UNIQUEIDENTIFIER = TRY_CAST('$(actor)' AS UNIQUEIDENTIFIER);
IF @actor IS NULL OR NOT EXISTS (SELECT 1 FROM Users WHERE Id = @actor)
BEGIN
    PRINT 'ABORTED: pass a valid administrator user id via  -v actor="<guid>"  (must exist in Users).';
    SET NOEXEC ON;
END

-- ════════════════════════ REQ-20/07/2026-098 ════════════════════════
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @expectedGroup1 UNIQUEIDENTIFIER = 'f559b59c-867c-4fa8-a339-cece55e5cd7f';
    DECLARE @old1 NVARCHAR(100) = N'5002736705';
    DECLARE @new1 NVARCHAR(100) = N'ECF10 2026/230';
    DECLARE @hash1 NVARCHAR(100) = N'f3e08253d89e91d5707de93867e6c55a6d1841515a973a3e7a7da360a46ba322';

    -- Resolve from the live schema: the single group of REQ-098 still holding the old value.
    DECLARE @g1 UNIQUEIDENTIFIER =
        (SELECT g.Id FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
         WHERE r.RequestNumber = 'REQ-20/07/2026-098' AND g.PurchaseOrderNumber = @old1);

    IF @g1 IS NULL AND EXISTS (
        SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
        WHERE r.RequestNumber = 'REQ-20/07/2026-098' AND g.PurchaseOrderNumber = @new1)
    BEGIN
        PRINT 'REQ-098: already repaired (PurchaseOrderNumber = ECF10 2026/230) — skipping, nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF @g1 IS NULL
          OR @g1 <> @expectedGroup1
          OR (SELECT COUNT(*) FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
              WHERE r.RequestNumber = 'REQ-20/07/2026-098' AND g.PurchaseOrderNumber = @old1) <> 1
          -- supplier identity unchanged (VM Santos, whose NIF is exactly the wrong stored value)
          OR NOT EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Suppliers s ON s.Id = g.SupplierId
                         WHERE g.Id = @expectedGroup1 AND g.SupplierId = 261 AND s.TaxId = @old1)
          -- legal entity unchanged (ALPLA Plástico)
          OR NOT EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
                         WHERE g.Id = @expectedGroup1 AND r.CompanyId = 1)
          -- workflow state unchanged vs review
          OR NOT EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
                         JOIN RequestStatuses rs ON rs.Id = r.StatusId
                         WHERE g.Id = @expectedGroup1 AND g.Status = 'PAYMENT_COMPLETED'
                           AND rs.Code = 'PAYMENT_COMPLETED')
          -- document evidence still present and unambiguous (>=3 copies, single SHA-256)
          OR NOT EXISTS (SELECT 1 FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
                         WHERE r.RequestNumber = 'REQ-20/07/2026-098' AND a.AttachmentTypeCode = 'PO'
                           AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                         HAVING COUNT(*) >= 3 AND COUNT(DISTINCT LOWER(a.FileHash)) = 1
                            AND MIN(LOWER(a.FileHash)) = @hash1)
          -- no canonical ECF10-2026-230 collision inside the same legal entity
          OR EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
                     WHERE r.CompanyId = 1 AND g.Id <> @expectedGroup1 AND g.PurchaseOrderNumber IS NOT NULL
                       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF10%2026#230')
    BEGIN
        PRINT 'REQ-098: PRECONDITIONS FAILED — nothing written, rolled back. Re-run the preflight and re-review.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE RequestPoGroups
        SET PurchaseOrderNumber = @new1,
            UpdatedAtUtc = SYSUTCDATETIME(),
            UpdatedByUserId = @actor
        WHERE Id = @g1 AND PurchaseOrderNumber = @old1;

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'REQ-098: unexpected row count on UPDATE — rolled back.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   '[PO-REPAIR-REQ-098] [Reparo de integridade — P.O Flow v2.229.12] Número de P.O do grupo f559b59c corrigido: valor anterior ''5002736705'' (NIF do próprio fornecedor VM SANTOS — nunca uma P.O) -> ''ECF10 2026/230'' (canónico ECF10-2026-230). Evidência: 3 anexos TYPE_PO idênticos SHA-256 f3e08253...a46ba322, três parses Primavera positivos independentes (revisão 2026-08-20). Nenhum estado de workflow, fornecedor, valor ou anexo alterado.',
                   SYSUTCDATETIME()
            FROM Requests r WHERE r.RequestNumber = 'REQ-20/07/2026-098';

            PRINT 'REQ-098: repaired — ''5002736705'' -> ''ECF10 2026/230'' (audit row written).';
            COMMIT TRANSACTION;
        END
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('REQ-098: ERROR — rolled back: ', ERROR_MESSAGE());
END CATCH;

-- ════════════════════════ REQ-20/07/2026-101 ════════════════════════
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @expectedGroup2 UNIQUEIDENTIFIER = 'cd2f005c-7283-4a82-8364-4ce99eb7cc6a';
    DECLARE @old2 NVARCHAR(100) = N'5001713205';
    DECLARE @new2 NVARCHAR(100) = N'ECF11 2026/386';
    DECLARE @hash2 NVARCHAR(100) = N'cec3e78ba3ade8d73b7eccb239ce9d6f0ab68f7962c21d8e14440f323ad1d5d0';

    DECLARE @g2 UNIQUEIDENTIFIER =
        (SELECT g.Id FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
         WHERE r.RequestNumber = 'REQ-20/07/2026-101' AND g.PurchaseOrderNumber = @old2);

    IF @g2 IS NULL AND EXISTS (
        SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
        WHERE r.RequestNumber = 'REQ-20/07/2026-101' AND g.PurchaseOrderNumber = @new2)
    BEGIN
        PRINT 'REQ-101: already repaired (PurchaseOrderNumber = ECF11 2026/386) — skipping, nothing written.';
        COMMIT TRANSACTION;
    END
    ELSE IF @g2 IS NULL
          OR @g2 <> @expectedGroup2
          OR (SELECT COUNT(*) FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
              WHERE r.RequestNumber = 'REQ-20/07/2026-101' AND g.PurchaseOrderNumber = @old2) <> 1
          OR NOT EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Suppliers s ON s.Id = g.SupplierId
                         WHERE g.Id = @expectedGroup2 AND g.SupplierId = 102 AND s.TaxId = @old2)
          OR NOT EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
                         WHERE g.Id = @expectedGroup2 AND r.CompanyId = 1)
          OR NOT EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
                         JOIN RequestStatuses rs ON rs.Id = r.StatusId
                         WHERE g.Id = @expectedGroup2 AND g.Status = 'PAYMENT_SCHEDULED'
                           AND rs.Code = 'PAYMENT_SCHEDULED')
          OR NOT EXISTS (SELECT 1 FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
                         WHERE r.RequestNumber = 'REQ-20/07/2026-101' AND a.AttachmentTypeCode = 'PO'
                           AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                         HAVING COUNT(*) >= 2 AND COUNT(DISTINCT LOWER(a.FileHash)) = 1
                            AND MIN(LOWER(a.FileHash)) = @hash2)
          OR EXISTS (SELECT 1 FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
                     WHERE r.CompanyId = 1 AND g.Id <> @expectedGroup2 AND g.PurchaseOrderNumber IS NOT NULL
                       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#386')
    BEGIN
        PRINT 'REQ-101: PRECONDITIONS FAILED — nothing written, rolled back. Re-run the preflight and re-review.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE RequestPoGroups
        SET PurchaseOrderNumber = @new2,
            UpdatedAtUtc = SYSUTCDATETIME(),
            UpdatedByUserId = @actor
        WHERE Id = @g2 AND PurchaseOrderNumber = @old2;

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'REQ-101: unexpected row count on UPDATE — rolled back.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   '[PO-REPAIR-REQ-101] [Reparo de integridade — P.O Flow v2.229.12] Número de P.O do grupo cd2f005c corrigido: valor anterior ''5001713205'' (NIF do próprio fornecedor Gasp Transportes — nunca uma P.O; origem dos falsos duplicados em REQ-206/REQ-248) -> ''ECF11 2026/386'' (canónico ECF11-2026-386). Evidência: 2 anexos TYPE_PO idênticos SHA-256 cec3e78b...3ad1d5d0 = FileHash do BD, parse Primavera positivo (revisão 2026-08-20). Nenhum estado de workflow, fornecedor, valor ou anexo alterado.',
                   SYSUTCDATETIME()
            FROM Requests r WHERE r.RequestNumber = 'REQ-20/07/2026-101';

            PRINT 'REQ-101: repaired — ''5001713205'' -> ''ECF11 2026/386'' (audit row written).';
            COMMIT TRANSACTION;
        END
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('REQ-101: ERROR — rolled back: ', ERROR_MESSAGE());
END CATCH;

-- ── After-state (read-only) ──
SELECT r.RequestNumber, rs.Code AS RequestStatus, g.Id AS GroupId, g.Status AS GroupStatus,
       g.PurchaseOrderNumber, g.SupplierId, g.SupplierNameSnapshot, g.TotalAmount
FROM RequestPoGroups g
JOIN Requests r         ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Id IN ('f559b59c-867c-4fa8-a339-cece55e5cd7f', 'cd2f005c-7283-4a82-8364-4ce99eb7cc6a');
