-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- REQ-146 PO-NUMBER CORRECTION (allow-list: EXACTLY one request, PO field ONLY)
--
--   REQ-23/07/2026-146 (company 1, PAYMENT_SCHEDULED — live payment flow)
--   Group ba7db94e-1c21-497a-bb19-f0dc77bb5391 (PAYMENT_SCHEDULED, total 5410.01)
--     PurchaseOrderNumber: N'2026A/11' -> N'ECF10 2026A/11' (canonical ECF10-2026A-11)
--
-- Evidence (decisive, 2026-08-20): the stored PO PDF heading was reviewed by a
-- human — "Encomenda Mat Escritório/Diversos ECF10 2026A/11", N.º Contrib.
-- 5417371270. The stored '2026A/11' is that reference with the ECF10 family
-- dropped. 'FP - 63' is the N.º Doc. Externo on the document — NOT the PO
-- identity, never written. The '2026A' year-series is legitimate Primavera
-- output the v2.229.12 parser grammar does not yet recognize (data-only fix).
--
-- Supplier is ALREADY CORRECT (14 BISMARK PAPELARIA, NIF 5417371270): guarded,
-- NOT modified. Writes ONLY RequestPoGroups.PurchaseOrderNumber (+ UpdatedAtUtc /
-- UpdatedByUserId bookkeeping) and ONE tagged DATA_INTEGRITY_REPAIR audit row
-- (PreviousStatusId = NewStatusId). Never touches: SupplierId, snapshots,
-- Requests.SupplierId, statuses, payment state, totals, attachments, supplier master.
--
-- SINGLE TRANSACTION, all-or-nothing, expected rowcount exactly 1. Idempotent:
-- PO already 'ECF10 2026A/11' => ALREADY_REPAIRED; anything other than the exact
-- reviewed state => abort for manual review.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-req146-po-number-repair.sql -v actor="<admin user guid>"
--   (rehearse on a restored clone copy first; then, with authorization, -d Portal-Gerencial)
-- Rollback: po-flow-req146-po-number-repair-rollback.sql
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

-- ── Pinned expectations (reviewed 2026-08-20) ──
DECLARE @requestNumber NVARCHAR(50)      = N'REQ-23/07/2026-146';
DECLARE @groupId UNIQUEIDENTIFIER        = 'ba7db94e-1c21-497a-bb19-f0dc77bb5391';
DECLARE @companyId INT                   = 1;
DECLARE @total DECIMAL(18,2)             = 5410.01;
DECLARE @supplierId INT                  = 14;
DECLARE @supplierNif NVARCHAR(50)        = N'5417371270';
DECLARE @oldPo NVARCHAR(100)             = N'2026A/11';
DECLARE @newPo NVARCHAR(100)             = N'ECF10 2026A/11';
DECLARE @poAttachmentId UNIQUEIDENTIFIER = '447f4ef6-fc70-454a-a01b-7af3dd04b15e';
DECLARE @poFileHash NVARCHAR(100)        = N'596ee7416cc0d67ea39ff5bfda073d2d00f44f7f89aa077549dad7ea5bde9e7e';

BEGIN TRANSACTION;
BEGIN TRY
    -- ── Idempotency triage ──
    DECLARE @inPendingState BIT = CASE WHEN EXISTS (
        SELECT 1
        FROM Requests r
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN RequestPoGroups g  ON g.RequestId = r.Id
        JOIN Suppliers s        ON s.Id = @supplierId
        WHERE r.RequestNumber = @requestNumber
          AND g.Id = @groupId
          AND (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r.Id) = 1
          AND g.PurchaseOrderNumber = @oldPo
          AND g.SupplierId = @supplierId
          AND g.SupplierNameSnapshot = s.Name
          AND g.SupplierNifSnapshot = @supplierNif
          AND rs.Code = 'PAYMENT_SCHEDULED'
          AND g.Status = 'PAYMENT_SCHEDULED'
          AND r.CompanyId = @companyId
          AND g.TotalAmount = @total
          AND s.TaxId = @supplierNif
          AND s.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a
                      WHERE a.Id = @poAttachmentId AND a.RequestId = r.Id
                        AND a.AttachmentTypeCode = 'PO'
                        AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                        AND a.RequestPoGroupId = @groupId
                        AND LOWER(a.FileHash) = LOWER(@poFileHash))
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups gx
                          WHERE gx.Id <> @groupId AND gx.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(gx.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%2026A#11')
    ) THEN 1 ELSE 0 END;

    DECLARE @inRepairedState BIT = CASE WHEN EXISTS (
        SELECT 1
        FROM Requests r
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN RequestPoGroups g  ON g.RequestId = r.Id
        JOIN Suppliers s        ON s.Id = @supplierId
        WHERE r.RequestNumber = @requestNumber
          AND g.Id = @groupId
          AND g.PurchaseOrderNumber = @newPo
          AND g.SupplierId = @supplierId
          AND g.SupplierNameSnapshot = s.Name
          AND g.SupplierNifSnapshot = @supplierNif
          AND rs.Code = 'PAYMENT_SCHEDULED'
          AND g.Status = 'PAYMENT_SCHEDULED'
    ) THEN 1 ELSE 0 END;

    IF @inRepairedState = 1 AND @inPendingState = 0
    BEGIN
        PRINT 'ALREADY_REPAIRED: the group already carries PO ''ECF10 2026A/11'' — nothing written, no duplicate audit row.';
        COMMIT TRANSACTION;
    END
    ELSE IF @inPendingState <> 1
    BEGIN
        PRINT 'ABORTED (MANUAL_REVIEW_REQUIRED): the current state does not match the exact reviewed state (guards: group identity, current PO ''2026A/11'', supplier 14 with exact snapshots, statuses PAYMENT_SCHEDULED, company, total, PO-attachment binding+hash, canonical no-collision). Run the preflight for detail. Nothing written, rolled back.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.PurchaseOrderNumber = @newPo,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN Requests r ON r.Id = g.RequestId
        WHERE g.Id = @groupId
          AND r.RequestNumber = @requestNumber
          AND g.PurchaseOrderNumber = @oldPo
          AND g.SupplierId = @supplierId;

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'ABORTED: UPDATE affected a row count different from 1 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   CONCAT(N'[HIST-PO-REQ-146]',
                          N' [Reparo de integridade — campanha histórica final] Grupo ', LEFT(CONVERT(NVARCHAR(36), @groupId), 8),
                          N': PurchaseOrderNumber ''2026A/11'' -> ''ECF10 2026A/11''.',
                          N' Base: cabeçalho do documento de P.O armazenado REVISTO POR HUMANO — "Encomenda Mat Escritório/Diversos ECF10 2026A/11", N.º Contrib. 5417371270;',
                          N' o valor antigo ''2026A/11'' era a mesma referência com a família ECF10 truncada.',
                          N' ''FP - 63'' é o N.º Doc. Externo desse documento, NÃO a identidade da P.O, e não foi gravado.',
                          N' Fornecedor já estava correto (14 — BISMARK PAPELARIA, NIF 5417371270) e NÃO foi alterado.',
                          N' Anexo P.O ', CONVERT(NVARCHAR(36), @poAttachmentId), N' SHA-256 ', @poFileHash, N' verificado.',
                          N' Nenhum estado de workflow ou de pagamento alterado.'),
                   SYSUTCDATETIME()
            FROM Requests r
            WHERE r.RequestNumber = @requestNumber;

            IF @@ROWCOUNT <> 1
            BEGIN
                PRINT 'ABORTED: audit insert affected a row count different from 1 — rolled back, nothing persisted.';
                ROLLBACK TRANSACTION;
            END
            ELSE
            BEGIN
                PRINT 'REPAIRED: REQ-23/07/2026-146 PO corrected to ''ECF10 2026A/11'' (1 audit row written). Supplier and statuses untouched.';
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
WHERE g.Id = 'ba7db94e-1c21-497a-bb19-f0dc77bb5391';
