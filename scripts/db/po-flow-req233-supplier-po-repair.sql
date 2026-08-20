-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- REQ-233 SUPPLIER + PO-NUMBER INTEGRITY REPAIR (allow-list: EXACTLY ONE request)
--
--   REQ-11/08/2026-233 (company 1, AlplaPLASTICO)
--   group 091cffd1-921b-4cf6-b6d3-843500820538
--
-- Human-reviewed documentary evidence (2026-08-20, the actual PROD PO PDF):
--   heading "PO Serviços ECF11 2026/423" (canonical ECF11-2026-423);
--   N.º Contrib. on the document = 5410002857 = TDA master NIF (SupplierId 34);
--   N.º Doc. Externo = "FT 00459" — the value wrongly registered as the PO number.
--   "FT 00459" is valid source information (external-document number), NOT the PO identity.
--
-- Writes ONLY, on that single RequestPoGroups row:
--   SupplierId            NULL                        -> 34
--   SupplierNameSnapshot  'Fornecedor não definido'   -> supplier master Name
--   SupplierNifSnapshot   NULL                        -> '5410002857' (master TaxId)
--   PurchaseOrderNumber   'FT 00459'                  -> 'ECF11 2026/423'
--   (+ UpdatedAtUtc/UpdatedByUserId bookkeeping) and ONE DATA_INTEGRITY_REPAIR audit row
--   (PreviousStatusId = NewStatusId). Requests.SupplierId, statuses, totals, attachments,
--   approval history and the supplier master record are NOT touched.
--
-- OPERATOR PIN: run the preflight first; it prints the live PO attachment FileHash.
-- This script requires that hash back via  -v poHash="<sha256>"  and aborts on mismatch —
-- the PO attachment id/hash are thereby pinned from the LIVE database, never guessed.
--
-- SINGLE TRANSACTION — all-or-nothing; any guard failure aborts everything.
-- Idempotent: already repaired => ALREADY_REPAIRED (no writes, no duplicate audit);
-- any other state => abort for manual review.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial -E -b -i po-flow-req233-supplier-po-repair.sql ^
--          -v actor="<admin user guid>" -v poHash="<sha256 from preflight>"
-- Rollback: po-flow-req233-supplier-po-rollback.sql
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

DECLARE @groupId UNIQUEIDENTIFIER = '091cffd1-921b-4cf6-b6d3-843500820538';
DECLARE @proformaId UNIQUEIDENTIFIER = '97d0aeb4-6352-4715-9d8f-a5d5759c08e3';
DECLARE @proformaHash NVARCHAR(100) = N'b4bc9eb55fdd0900da895a5cedc96b6bd95245a11b644dae34cc50fafbae005d';
DECLARE @oldPo NVARCHAR(100) = N'FT 00459';
DECLARE @newPo NVARCHAR(100) = N'ECF11 2026/423';

BEGIN TRANSACTION;
BEGIN TRY
    -- Resolve the live PO attachment: EXACTLY ONE active TYPE_PO row on this request whose
    -- sanitized filename carries the ECF11 2026/423 reference AND whose hash equals the pin.
    DECLARE @poAttachmentId UNIQUEIDENTIFIER =
        (SELECT a.Id FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
         WHERE r.RequestNumber = 'REQ-11/08/2026-233' AND a.AttachmentTypeCode = 'PO'
           AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
           AND UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE '%ECF11%2026423%'
           AND LOWER(a.FileHash) = @poHash);
    DECLARE @activePoAttachments INT =
        (SELECT COUNT(*) FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
         WHERE r.RequestNumber = 'REQ-11/08/2026-233' AND a.AttachmentTypeCode = 'PO'
           AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL);

    -- Idempotency triage
    DECLARE @isRepaired BIT = CASE WHEN EXISTS (
        SELECT 1 FROM RequestPoGroups g JOIN Suppliers s ON s.Id = 34
        WHERE g.Id = @groupId AND g.SupplierId = 34
          AND g.SupplierNameSnapshot = s.Name AND g.SupplierNifSnapshot = N'5410002857'
          AND g.PurchaseOrderNumber = @newPo) THEN 1 ELSE 0 END;

    IF @isRepaired = 1
    BEGIN
        PRINT 'ALREADY_REPAIRED: the group already carries supplier 34 and PO ECF11 2026/423 — nothing written, no duplicate audit row.';
        COMMIT TRANSACTION;
    END
    ELSE IF NOT EXISTS (
        SELECT 1
        FROM Requests r
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN RequestPoGroups g  ON g.RequestId = r.Id
        JOIN Suppliers s        ON s.Id = 34
        WHERE r.RequestNumber = 'REQ-11/08/2026-233'
          AND g.Id = @groupId
          AND (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r.Id) = 1
          AND g.SupplierId IS NULL
          AND g.SupplierNameSnapshot = N'Fornecedor não definido'
          AND g.SupplierNifSnapshot IS NULL
          AND r.SupplierId IS NULL
          AND rs.Code = 'PO_ISSUED'
          AND g.Status = 'PO_ISSUED'
          AND g.PurchaseOrderNumber = @oldPo
          AND r.CompanyId = 1
          AND g.TotalAmount = 529833.40
          AND s.TaxId = N'5410002857'
          AND s.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a
                      WHERE a.Id = @proformaId AND a.RequestId = r.Id
                        AND a.AttachmentTypeCode = 'PROFORMA'
                        AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                        AND LOWER(a.FileHash) = @proformaHash)
          AND @poAttachmentId IS NOT NULL
          AND @activePoAttachments = 1
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g2 JOIN Requests r2 ON r2.Id = g2.RequestId
                          WHERE r2.CompanyId = 1 AND g2.Id <> @groupId AND g2.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g2.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#423'))
    BEGIN
        PRINT 'ABORTED (MANUAL_REVIEW_REQUIRED): the live state does not match the reviewed advanced state (statuses, FT 00459, snapshots, company, total, supplier master, evidence hashes, single PO attachment, or canonical ECF11-2026-423 collision). Run the preflight for detail. Nothing written, rolled back.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.SupplierId = 34,
            g.SupplierNameSnapshot = s.Name,
            g.SupplierNifSnapshot = s.TaxId,
            g.PurchaseOrderNumber = @newPo,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN Suppliers s ON s.Id = 34
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
                   CONCAT(N'[POP-B-SUPPLIER-PO-REQ-233]',
                          N' [Reparo de integridade — P.O Flow v2.229.12] Grupo 091cffd1: fornecedor TDA confirmado por revisão humana do próprio documento de P.O;',
                          N' o documento traz N.º Contrib. 5410002857 (= NIF do cadastro do fornecedor 34) e o cabeçalho "PO Serviços ECF11 2026/423".',
                          N' O valor anteriormente registrado ''FT 00459'' é o "N.º Doc. Externo" do documento de P.O — informação válida do documento, mas NÃO a identidade da P.O.',
                          N' Correções: SupplierId NULL -> 34; snapshots restaurados do cadastro (', s.Name, N', NIF ', s.TaxId, N');',
                          N' PurchaseOrderNumber ''FT 00459'' -> ''ECF11 2026/423'' (canónico ECF11-2026-423).',
                          N' Evidências: anexo P.O ', CONVERT(NVARCHAR(36), @poAttachmentId), N' SHA-256 ', @poHash,
                          N'; proforma TDA ', CONVERT(NVARCHAR(36), @proformaId), N' SHA-256 ', @proformaHash, N'.',
                          N' Revisão 2026-08-20. Nenhum estado de workflow/status, valor, anexo ou cadastro de fornecedor alterado.'),
                   SYSUTCDATETIME()
            FROM Requests r
            JOIN Suppliers s ON s.Id = 34
            WHERE r.RequestNumber = 'REQ-11/08/2026-233';

            IF @@ROWCOUNT <> 1
            BEGIN
                PRINT 'ABORTED: audit insert affected a row count different from 1 — rolled back, nothing persisted.';
                ROLLBACK TRANSACTION;
            END
            ELSE
            BEGIN
                PRINT 'REPAIRED: REQ-233 group now carries supplier 34 and PO ECF11 2026/423 (1 audit row written).';
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
WHERE g.Id = '091cffd1-921b-4cf6-b6d3-843500820538';
