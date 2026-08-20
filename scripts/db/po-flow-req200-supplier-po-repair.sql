-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- REQ-200 SUPPLIER + PO-NUMBER INTEGRITY REPAIR (allow-list: EXACTLY ONE request)
--
--   REQ-31/07/2026-200 (company 1, live PROD state PO_ISSUED)
--   Group a4c5cc42-2f8d-48ec-b9a9-0885c9f92081 (PO_ISSUED, total 2120186.00)
--     SupplierId            NULL                        -> 157
--     SupplierNameSnapshot  'Fornecedor não definido'   -> supplier master Name
--     SupplierNifSnapshot   NULL                        -> N'5001094645' (master TaxId)
--     PurchaseOrderNumber   'FT 453'                    -> N'ECF11 2026/424'
--
-- Human-reviewed documentary evidence (2026-08-20, the actual PROD PO PDF):
--   heading "PO Serviços ECF11 2026/424" (canonical ECF11-2026-424);
--   N.º Contrib. on the document = 5001094645 = HENDA HOTELARIA , LDA - HCTA
--   (SupplierId 157); N.º Doc. Externo = "FT 453" — the value wrongly registered
--   as the PO number under PROD v2.229.9 (which lacks the v2.229.12 guards).
--   'FT 453' is valid source information, NOT the PO identity.
--
-- OPERATOR PIN: run the preflight first; it prints the live PO attachment
-- (expected filename PO__Servios__ECF11_2026424_-_424.pdf) with its SHA-256.
-- This script requires that hash back via  -v poHash="<sha256>"  and aborts on
-- mismatch — the PO attachment id/hash are pinned from the LIVE database, never
-- guessed (the analysis clone predates this PO registration).
--
-- Writes ONLY the four group supplier/PO fields above (+ UpdatedAtUtc /
-- UpdatedByUserId bookkeeping) and ONE tagged DATA_INTEGRITY_REPAIR audit row
-- (PreviousStatusId = NewStatusId). Never touches: Requests.SupplierId,
-- RequestStatus, GroupStatus (PO_ISSUED preserved), totals, attachments,
-- approvals/history (other than the new audit row), supplier master or its
-- RegistrationStatus.
--
-- OPERATIONAL WARNING (reported by the preflight; NOT a blocking guard):
-- supplier 157 is RegistrationStatus = DRAFT — future REGISTER_PO operations may
-- remain blocked until its registration completes. The repair proceeds regardless.
--
-- SINGLE TRANSACTION — all-or-nothing, expected rowcount exactly 1. Idempotent:
-- exact repaired state => ALREADY_REPAIRED (no writes, no duplicate audit);
-- any other state => abort for manual review.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial -E -b -i po-flow-req200-supplier-po-repair.sql ^
--          -v actor="<admin user guid>" -v poHash="<sha256 from preflight>"
--   (rehearse on a restored clone copy first, seeding the drifted state)
-- Rollback: po-flow-req200-supplier-po-rollback.sql
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

-- ── Operator pin: the PO attachment SHA-256 printed by the preflight ──
DECLARE @poHash NVARCHAR(100) = LOWER(N'$(poHash)');
IF @poHash IS NULL OR LEN(@poHash) <> 64 OR @poHash LIKE '%[^0-9a-f]%'
BEGIN
    PRINT 'ABORTED: pass the PO attachment SHA-256 (64 hex chars, from the preflight output) via  -v poHash="<sha256>".';
    SET NOEXEC ON;
END

-- ── Pinned expectations (reviewed 2026-08-20) ──
DECLARE @requestNumber NVARCHAR(50)       = N'REQ-31/07/2026-200';
DECLARE @groupId UNIQUEIDENTIFIER         = 'a4c5cc42-2f8d-48ec-b9a9-0885c9f92081';
DECLARE @companyId INT                    = 1;
DECLARE @total DECIMAL(18,2)              = 2120186.00;
DECLARE @supplierId INT                   = 157;
DECLARE @supplierNif NVARCHAR(50)         = N'5001094645';
DECLARE @oldPo NVARCHAR(100)              = N'FT 453';
DECLARE @newPo NVARCHAR(100)              = N'ECF11 2026/424';
DECLARE @sourceAttachmentId UNIQUEIDENTIFIER = '0c1be5e5-516f-4b37-8b8a-45d6f6675368';
DECLARE @sourceFileHash NVARCHAR(100)     = N'94168ba104d99c5ee57ee240aac108e1665c3aab1148263c2bfd3babcf7e6c4e';

BEGIN TRANSACTION;
BEGIN TRY
    -- Resolve the live PO attachment: EXACTLY ONE active TYPE_PO row on this request whose
    -- sanitized filename carries the ECF11 2026/424 reference AND whose hash equals the pin.
    DECLARE @poAttachmentId UNIQUEIDENTIFIER =
        (SELECT a.Id FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
         WHERE r.RequestNumber = @requestNumber AND a.AttachmentTypeCode = 'PO'
           AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
           AND UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE '%ECF11%2026424%'
           AND LOWER(a.FileHash) = @poHash);
    DECLARE @activePoAttachments INT =
        (SELECT COUNT(*) FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
         WHERE r.RequestNumber = @requestNumber AND a.AttachmentTypeCode = 'PO'
           AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL);

    -- Idempotency triage
    DECLARE @isRepaired BIT = CASE WHEN EXISTS (
        SELECT 1 FROM RequestPoGroups g JOIN Suppliers s ON s.Id = @supplierId
        WHERE g.Id = @groupId AND g.SupplierId = @supplierId
          AND g.SupplierNameSnapshot = s.Name AND g.SupplierNifSnapshot = @supplierNif
          AND g.PurchaseOrderNumber = @newPo) THEN 1 ELSE 0 END;

    IF @isRepaired = 1
    BEGIN
        PRINT 'ALREADY_REPAIRED: the group already carries supplier 157 and PO ''ECF11 2026/424'' — nothing written, no duplicate audit row.';
        COMMIT TRANSACTION;
    END
    ELSE IF NOT EXISTS (
        SELECT 1
        FROM Requests r
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN RequestPoGroups g  ON g.RequestId = r.Id
        JOIN Suppliers s        ON s.Id = @supplierId
        WHERE r.RequestNumber = @requestNumber
          AND g.Id = @groupId
          AND (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r.Id) = 1
          AND g.SupplierId IS NULL
          AND g.SupplierNameSnapshot = N'Fornecedor não definido'
          AND g.SupplierNifSnapshot IS NULL
          AND r.SupplierId IS NULL
          AND rs.Code = 'PO_ISSUED'
          AND g.Status = 'PO_ISSUED'
          AND g.PurchaseOrderNumber = @oldPo
          AND r.CompanyId = @companyId
          AND g.TotalAmount = @total
          AND s.TaxId = @supplierNif
          AND s.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a
                      WHERE a.Id = @sourceAttachmentId AND a.RequestId = r.Id
                        AND a.AttachmentTypeCode = 'PROFORMA'
                        AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                        AND LOWER(a.FileHash) = LOWER(@sourceFileHash))
          AND @poAttachmentId IS NOT NULL
          AND @activePoAttachments = 1
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g2 JOIN Requests r2 ON r2.Id = g2.RequestId
                          WHERE r2.CompanyId = @companyId AND g2.Id <> @groupId AND g2.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g2.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#424'))
    BEGIN
        PRINT 'ABORTED (MANUAL_REVIEW_REQUIRED): the live state does not match the reviewed advanced state (statuses PO_ISSUED, PO ''FT 453'', legacy snapshots, header supplier NULL, company, total, supplier master, source-document hash, single pinned PO attachment, or canonical ECF11-2026-424 collision). Run the preflight for detail. Nothing written, rolled back.';
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
        JOIN Suppliers s ON s.Id = @supplierId
        WHERE g.Id = @groupId
          AND g.SupplierId IS NULL
          AND g.PurchaseOrderNumber = @oldPo;

        IF @@ROWCOUNT <> 1
        BEGIN
            PRINT 'ABORTED: UPDATE affected a row count different from 1 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   CONCAT(N'[HIST-SUPPLIER-PO-REQ-200]',
                          N' [Reparo de integridade — campanha histórica final] Grupo ', CONVERT(NVARCHAR(36), @groupId),
                          N': fornecedor HENDA confirmado por revisão humana do próprio documento de P.O;',
                          N' o documento traz N.º Contrib. 5001094645 (= NIF do cadastro do fornecedor 157) e o cabeçalho "PO Serviços ECF11 2026/424".',
                          N' O valor anteriormente registrado ''FT 453'' é o "N.º Doc. Externo" do documento de P.O — informação válida do documento, mas NÃO a identidade da P.O.',
                          N' Correções: SupplierId NULL -> 157; snapshots restaurados do cadastro (', s.Name, N', NIF ', s.TaxId, N');',
                          N' PurchaseOrderNumber ''FT 453'' -> ''ECF11 2026/424'' (canónico ECF11-2026-424).',
                          N' Evidências: anexo P.O ', CONVERT(NVARCHAR(36), @poAttachmentId), N' SHA-256 ', @poHash,
                          N'; documento fonte PROFORMA ', CONVERT(NVARCHAR(36), @sourceAttachmentId), N' SHA-256 ', @sourceFileHash, N'.',
                          N' Revisão 2026-08-20. Nenhum estado de workflow/status, valor, anexo ou cadastro de fornecedor alterado.'),
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
                PRINT 'REPAIRED: REQ-31/07/2026-200 group now carries supplier 157 and PO ''ECF11 2026/424'' (1 audit row written). Statuses untouched (PO_ISSUED preserved).';
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
WHERE g.Id = 'a4c5cc42-2f8d-48ec-b9a9-0885c9f92081';
