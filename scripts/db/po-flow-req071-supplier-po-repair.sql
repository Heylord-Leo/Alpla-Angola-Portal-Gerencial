-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- REQ-071 SUPPLIER + PO BACKFILL (allow-list: EXACTLY one request)
--
--   REQ-15/07/2026-071 (company 1, ADVANCE_PAYMENT_REQUIRED)
--   Group 2842545e-f766-4c9b-abc8-5a8fc75ac42f (PENDING, total 276119.00)
--     SupplierId            NULL -> 257 (Embrace Angola - Prestação de Serviços, LDA)
--     SupplierNameSnapshot  NULL -> supplier master name
--     SupplierNifSnapshot   NULL -> N'5417101524'
--     PurchaseOrderNumber   NULL -> N'ECF11 2026/371'
--
-- Evidence (decisive, 2026-08-20): supplier CONFIRMED BY HUMAN REVIEW; PO PDF
-- visually reviewed — heading "PO Serviços ECF11 2026/371", N.º Contrib.
-- 5417101524. 'FT FC202602/2101254' is the N.º Doc. Externo on that PO, NOT the
-- PO number, and is never written. The PO was historically registered on
-- 2026-07-16 (REGISTER_PO, APPROVED -> ADVANCE_PAYMENT_REQUIRED) BEFORE the
-- current group row existed (created 2026-07-20), which is why the group carries
-- neither supplier nor PO.
--
-- Writes ONLY the four group supplier/PO fields above (+ UpdatedAtUtc /
-- UpdatedByUserId bookkeeping) and ONE tagged DATA_INTEGRITY_REPAIR audit row
-- (PreviousStatusId = NewStatusId). Never touches: RequestStatus, GroupStatus
-- (the PENDING mismatch is a SEPARATE reconciliation issue), finance /
-- advance-payment state, amounts, attachments, approvals, Requests.SupplierId,
-- supplier master.
--
-- SINGLE TRANSACTION, all-or-nothing, expected rowcount exactly 1. Idempotent:
-- exact repaired state => ALREADY_REPAIRED (no writes, no duplicate audit);
-- anything other than the exact reviewed pending state => abort for manual review.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-req071-supplier-po-repair.sql -v actor="<admin user guid>"
--   (rehearse on a restored clone copy first; then, with authorization, -d Portal-Gerencial)
-- Rollback: po-flow-req071-supplier-po-rollback.sql
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
DECLARE @requestNumber NVARCHAR(50)      = N'REQ-15/07/2026-071';
DECLARE @groupId UNIQUEIDENTIFIER        = '2842545e-f766-4c9b-abc8-5a8fc75ac42f';
DECLARE @companyId INT                   = 1;
DECLARE @total DECIMAL(18,2)             = 276119.00;
DECLARE @supplierId INT                  = 257;
DECLARE @supplierNif NVARCHAR(50)        = N'5417101524';
DECLARE @newPo NVARCHAR(100)             = N'ECF11 2026/371';
DECLARE @poAttachmentId UNIQUEIDENTIFIER = 'b7b91151-713c-498f-bb4d-a7eff2ef510a';
DECLARE @poFileHash NVARCHAR(100)        = N'ac9e9cfc4b040c9b1bbded396e5e7103bb630b4d8351dbb9b76e4bb3305f45d8';

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
          AND g.SupplierId IS NULL
          AND g.SupplierNameSnapshot IS NULL
          AND g.SupplierNifSnapshot IS NULL
          AND g.PurchaseOrderNumber IS NULL
          AND r.SupplierId IS NULL
          AND rs.Code = 'ADVANCE_PAYMENT_REQUIRED'
          AND g.Status = 'PENDING'
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
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups gx JOIN Requests rx ON rx.Id = gx.RequestId
                          WHERE gx.Id <> @groupId AND rx.CompanyId = @companyId AND gx.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(gx.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#371')
    ) THEN 1 ELSE 0 END;

    DECLARE @inRepairedState BIT = CASE WHEN EXISTS (
        SELECT 1
        FROM Requests r
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN RequestPoGroups g  ON g.RequestId = r.Id
        JOIN Suppliers s        ON s.Id = @supplierId
        WHERE r.RequestNumber = @requestNumber
          AND g.Id = @groupId
          AND g.SupplierId = @supplierId
          AND g.SupplierNameSnapshot = s.Name
          AND g.SupplierNifSnapshot = @supplierNif
          AND g.PurchaseOrderNumber = @newPo
          AND rs.Code = 'ADVANCE_PAYMENT_REQUIRED'
          AND g.Status = 'PENDING'
    ) THEN 1 ELSE 0 END;

    IF @inRepairedState = 1 AND @inPendingState = 0
    BEGIN
        PRINT 'ALREADY_REPAIRED: the group already carries supplier 257 and PO ''ECF11 2026/371'' — nothing written, no duplicate audit row.';
        COMMIT TRANSACTION;
    END
    ELSE IF @inPendingState <> 1
    BEGIN
        PRINT 'ABORTED (MANUAL_REVIEW_REQUIRED): the current state does not match the exact reviewed pending state (guards: group identity, all-NULL snapshots, NULL PO, statuses ADVANCE_PAYMENT_REQUIRED/PENDING, company, total, supplier NIF/active, PO-attachment binding+hash, canonical no-collision). Run the preflight for detail. Nothing written, rolled back.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.SupplierId = @supplierId,
            g.SupplierNameSnapshot = s.Name,
            g.SupplierNifSnapshot = s.TaxId,
            g.PurchaseOrderNumber = @newPo,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN Requests r  ON r.Id = g.RequestId
        JOIN Suppliers s ON s.Id = @supplierId
        WHERE g.Id = @groupId
          AND r.RequestNumber = @requestNumber
          AND g.SupplierId IS NULL
          AND g.SupplierNameSnapshot IS NULL
          AND g.SupplierNifSnapshot IS NULL
          AND g.PurchaseOrderNumber IS NULL;

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'ABORTED: UPDATE affected a row count different from 1 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   CONCAT(N'[HIST-SUPPLIER-PO-REQ-071]',
                          N' [Reparo de integridade — campanha histórica final] Grupo ', CONVERT(NVARCHAR(36), @groupId),
                          N': SupplierId NULL -> 257 — ', s.Name, N' (NIF ', s.TaxId, N'), snapshots restaurados do cadastro; PurchaseOrderNumber NULL -> ''ECF11 2026/371'' (canónico ECF11-2026-371).',
                          N' Base: fornecedor Embrace CONFIRMADO POR REVISÃO HUMANA; documento de P.O revisto visualmente — cabeçalho "PO Serviços ECF11 2026/371", N.º Contrib. 5417101524.',
                          N' ''FT FC202602/2101254'' é o N.º Doc. Externo desse documento, NÃO a identidade da P.O, e não foi gravado.',
                          N' Contexto: a P.O foi registrada em 16/07/2026 (REGISTER_PO) ANTES da linha de grupo atual existir (criada 20/07/2026), pelo que o grupo ficou sem fornecedor e sem P.O.',
                          N' Anexo P.O ', CONVERT(NVARCHAR(36), @poAttachmentId), N' SHA-256 ', @poFileHash, N' verificado.',
                          N' Revisão 2026-08-20. Nenhum estado de workflow/status/financeiro alterado; o desalinhamento do status do grupo (PENDING) é reconciliação separada, não tratada aqui.'),
                   SYSUTCDATETIME()
            FROM Requests r
            JOIN Suppliers s ON s.Id = @supplierId
            WHERE r.RequestNumber = @requestNumber;

            IF @@ROWCOUNT <> 1
            BEGIN
                PRINT 'ABORTED: audit insert affected a row count different from 1 — rolled back, nothing persisted.';
                ROLLBACK TRANSACTION;
            END
            ELSE
            BEGIN
                PRINT 'REPAIRED: REQ-15/07/2026-071 group now carries supplier 257 and PO ''ECF11 2026/371'' (1 audit row written). Statuses untouched.';
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
WHERE g.Id = '2842545e-f766-4c9b-abc8-5a8fc75ac42f';
