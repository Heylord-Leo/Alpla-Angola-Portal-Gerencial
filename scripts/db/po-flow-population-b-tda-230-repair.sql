-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- POPULATION-B SUPPLIER REPAIR — TDA, HUMAN-CONFIRMED (allow-list: EXACTLY ONE request)
--
--   REQ-11/08/2026-230 (company 2, AlplaSOPRO) group f28ec394-3553-43ff-b492-0ae6524d238f
--   -> SupplierId 34 (TDA-COMERCIO INDÚSTRIA, LDA, NIF 5410002857, active)
--
-- REQ-233 was EXCLUDED from this repair: its live workflow advanced to PO_ISSUED with
-- PO 'FT 00459' (under pre-v2.229.12 PROD code) and is under separate manual review.
--
-- Evidence basis (reviewed 2026-08-20):
--   - issuer of the stored PROFORMA CONFIRMED BY HUMAN REVIEW as TDA;
--   - OCR supplier name matched supplier 34 under accent-aware normalization;
--   - OCR supplierTaxId read 541002857 (malformed — one zero short); that value matches
--     NO Portal supplier and is NOT used as supplier data: snapshots are written from the
--     supplier MASTER record only (TaxId 5410002857). The TDA master record is not modified.
--   - evidence attachment id + SHA-256 pinned below and re-verified at execution time.
--
-- Writes ONLY RequestPoGroups.SupplierId / SupplierNameSnapshot / SupplierNifSnapshot
-- (+ UpdatedAtUtc/UpdatedByUserId bookkeeping) and one DATA_INTEGRITY_REPAIR audit row
-- (PreviousStatusId = NewStatusId). Requests.SupplierId (header) is NOT touched.
-- Never touches: request status, group status, PurchaseOrderNumber, totals, attachments,
-- workflow/payment/finance/quotation state.
--
-- SINGLE TRANSACTION — all-or-nothing. Any guard failure aborts the entire repair.
-- Idempotent: already repaired => ALREADY_REPAIRED, no writes, no duplicate audit rows;
-- any other state => abort for manual review.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-population-b-tda-230-repair.sql -v actor="<admin user guid>"
--   (rehearse on TEST first; then, with authorization, -d Portal-Gerencial)
-- Rollback: po-flow-population-b-tda-230-rollback.sql
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

-- ── Environment guard: ONLY Portal-Gerencial-Test (TEST rehearsal) or Portal-Gerencial (PROD) ──
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

-- ── Audit actor (sqlcmd variable, must exist in Users) ──
DECLARE @actor UNIQUEIDENTIFIER = TRY_CAST('$(actor)' AS UNIQUEIDENTIFIER);
IF @actor IS NULL OR NOT EXISTS (SELECT 1 FROM Users WHERE Id = @actor)
BEGIN
    PRINT 'ABORTED: pass a valid administrator user id via  -v actor="<guid>"  (must exist in Users).';
    SET NOEXEC ON;
END

-- ── Allow-list with every reviewed expectation pinned ──
DECLARE @targets TABLE (
    RequestNumber NVARCHAR(50) PRIMARY KEY,
    ExpectedGroupId UNIQUEIDENTIFIER,
    ExpectedCompanyId INT,
    ExpectedSupplierId INT,
    ExpectedSupplierNif NVARCHAR(50),
    EvidenceAttachmentId UNIQUEIDENTIFIER,
    EvidenceFileHash NVARCHAR(100),
    OcrMalformedNif NVARCHAR(50),
    AuditTag NVARCHAR(40)
);
INSERT INTO @targets VALUES
 (N'REQ-11/08/2026-230', 'f28ec394-3553-43ff-b492-0ae6524d238f', 2, 34, N'5410002857', '9c132114-b612-40ac-bcbf-ab9cdf2b7452', N'44ad6c2ac207d33eb918a160ffa83f3b6880b7ab71d271352c1e3b791c3f0cf9', N'541002857', N'[POP-B-SUPPLIER-REQ-230]');

BEGIN TRANSACTION;
BEGIN TRY
    -- ── Idempotency triage across the WHOLE set ──
    -- Pending = the FULL repairable state (a row whose workflow advanced — e.g. a PO was
    -- registered — is NOT pending even though its supplier fields still look legacy).
    DECLARE @pending INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r         ON r.RequestNumber = t.RequestNumber
         JOIN RequestStatuses rs ON rs.Id = r.StatusId
         JOIN RequestPoGroups g  ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         WHERE g.SupplierId IS NULL
           AND g.SupplierNameSnapshot = N'Fornecedor não definido'
           AND g.SupplierNifSnapshot IS NULL
           AND r.SupplierId IS NULL
           AND rs.Code = 'APPROVED'
           AND g.Status = 'WAITING_PO'
           AND g.PurchaseOrderNumber IS NULL);
    DECLARE @repaired INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r        ON r.RequestNumber = t.RequestNumber
         JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         WHERE g.SupplierId = t.ExpectedSupplierId
           AND g.SupplierNifSnapshot = t.ExpectedSupplierNif);

    IF @repaired = 1 AND @pending = 0
    BEGIN
        PRINT 'ALREADY_REPAIRED: the group already carries supplier 34 — nothing written, no duplicate audit rows.';
        COMMIT TRANSACTION;
    END
    ELSE IF @pending <> 1
    BEGIN
        PRINT CONCAT('ABORTED (MANUAL_REVIEW_REQUIRED): pending=', @pending, ' repaired=', @repaired,
                     ' of 1 — unexpected state. Nothing written, rolled back.');
        ROLLBACK TRANSACTION;
    END
    -- ── Every guard must pass, or the repair aborts ──
    ELSE IF EXISTS (
        SELECT 1 FROM @targets t
        WHERE NOT EXISTS (
            SELECT 1
            FROM Requests r
            JOIN RequestStatuses rs  ON rs.Id = r.StatusId
            JOIN RequestPoGroups g   ON g.RequestId = r.Id
            JOIN Suppliers s         ON s.Id = t.ExpectedSupplierId
            WHERE r.RequestNumber = t.RequestNumber
              AND g.Id = t.ExpectedGroupId
              AND (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r.Id) = 1
              AND g.SupplierId IS NULL
              AND g.SupplierNameSnapshot = N'Fornecedor não definido'
              AND g.SupplierNifSnapshot IS NULL
              AND r.SupplierId IS NULL
              AND rs.Code = 'APPROVED'
              AND g.Status = 'WAITING_PO'
              AND g.PurchaseOrderNumber IS NULL
              AND r.CompanyId = t.ExpectedCompanyId
              AND s.TaxId = t.ExpectedSupplierNif
              AND s.IsActive = 1
              AND EXISTS (SELECT 1 FROM RequestAttachments a
                          WHERE a.Id = t.EvidenceAttachmentId AND a.RequestId = r.Id
                            AND a.AttachmentTypeCode = 'PROFORMA'
                            AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                            AND LOWER(a.FileHash) = LOWER(t.EvidenceFileHash))))
    BEGIN
        PRINT 'ABORTED: at least one target failed a guard (group identity, snapshots, statuses, company, supplier master NIF/active, or evidence hash). Run the preflight for the per-row PASS/FAIL detail. Nothing written, rolled back.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.SupplierId = t.ExpectedSupplierId,
            g.SupplierNameSnapshot = s.Name,
            g.SupplierNifSnapshot = s.TaxId,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN Requests r  ON r.Id = g.RequestId
        JOIN @targets t  ON t.RequestNumber = r.RequestNumber AND t.ExpectedGroupId = g.Id
        JOIN Suppliers s ON s.Id = t.ExpectedSupplierId
        WHERE g.SupplierId IS NULL;

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'ABORTED: UPDATE affected a row count different from 1 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   CONCAT(t.AuditTag,
                          ' [Reparo de integridade — P.O Flow v2.229.12] Fornecedor do grupo ', LEFT(CONVERT(NVARCHAR(36), t.ExpectedGroupId), 8),
                          ' preenchido: SupplierId NULL (snapshot legado ''Fornecedor não definido''/NULL) -> ',
                          t.ExpectedSupplierId, ' — ', s.Name, ' (NIF do cadastro ', s.TaxId, ').',
                          ' Identidade do emissor CONFIRMADA POR REVISÃO HUMANA (revisão 2026-08-20).',
                          ' OCR havia lido NIF malformado ', t.OcrMalformedNif,
                          ' (um zero a menos; não corresponde a nenhum fornecedor) — valor OCR NÃO utilizado; NIF gravado é o do cadastro.',
                          ' Evidência: anexo PROFORMA ', CONVERT(NVARCHAR(36), t.EvidenceAttachmentId),
                          ' SHA-256 ', t.EvidenceFileHash, '.',
                          ' Motivo: grupo P.O legado da População B sem fornecedor estruturado.',
                          ' Nenhum estado de workflow, número de P.O, valor ou anexo alterado.'),
                   SYSUTCDATETIME()
            FROM @targets t
            JOIN Requests r  ON r.RequestNumber = t.RequestNumber
            JOIN Suppliers s ON s.Id = t.ExpectedSupplierId;

            IF @@ROWCOUNT <> 1
            BEGIN
                PRINT 'ABORTED: audit insert affected a row count different from 1 — rolled back, nothing persisted.';
                ROLLBACK TRANSACTION;
            END
            ELSE
            BEGIN
                PRINT 'REPAIRED: REQ-230 group now carries supplier 34 (1 audit row written).';
                COMMIT TRANSACTION;
            END
        END
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('ERROR — entire repair rolled back: ', ERROR_MESSAGE());
END CATCH;

-- ── After-state (read-only) ──
SELECT r.RequestNumber, rs.Code AS RequestStatus, g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId, g.SupplierNameSnapshot, g.SupplierNifSnapshot,
       g.PurchaseOrderNumber, g.TotalAmount
FROM RequestPoGroups g
JOIN Requests r         ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
WHERE g.Id = 'f28ec394-3553-43ff-b492-0ae6524d238f';
