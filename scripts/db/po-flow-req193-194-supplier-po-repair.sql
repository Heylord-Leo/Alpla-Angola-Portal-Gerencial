-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- REQ-193 + REQ-194 FIDELIDADE SUPPLIER + PO CORRECTION
-- (allow-list: EXACTLY TWO requests, single all-or-nothing transaction)
--
--   REQ-31/07/2026-193 (company 1) — group f20b272f-00d9-4a31-a9fc-948ac4d30f8c
--     SupplierId            NULL                        -> 45
--     SupplierNameSnapshot  'Fornecedor não definido'   -> supplier master Name
--     SupplierNifSnapshot   NULL                        -> N'5417061590' (master TaxId)
--     PurchaseOrderNumber   'FT 26/72087'               -> N'ECF11 2026/420'
--   REQ-31/07/2026-194 (company 2) — group a535dabd-ea4e-4749-ab0f-1da3d136fd4f
--     SupplierId            NULL                        -> 45
--     SupplierNameSnapshot  'Fornecedor não definido'   -> supplier master Name
--     SupplierNifSnapshot   NULL                        -> N'5417061590' (master TaxId)
--     PurchaseOrderNumber   'FT 73094'                  -> N'ECF11 2026/38'
--
-- Human-reviewed documentary evidence (2026-08-20, the actual PROD PO PDFs):
--   both documents carry N.º Contrib. 5417061590 = FIDELIDADE ANGOLA-COMP. DE
--   SEGUROS (SupplierId 45); headings "PO Serviços ECF11 2026/420" (193) and
--   "PO Serviços ECF11 2026/38" (194). The stored values 'FT 26/72087' and
--   'FT 73094' are each document's N.º Doc. Externo — valid source information
--   registered as PO numbers under PROD v2.229.9, but NOT the PO identities.
--
-- OPERATOR PINS: run the preflight first; it prints each request's live PO
-- attachment with its SHA-256. This script requires both hashes back via
--   -v poHash193="<sha256>" -v poHash194="<sha256>"
-- and aborts on any mismatch — the PO attachment ids/hashes are pinned from the
-- LIVE database, never guessed (the analysis clone predates these registrations).
--
-- Writes ONLY the four group supplier/PO fields per row (+ UpdatedAtUtc /
-- UpdatedByUserId bookkeeping) and EXACTLY TWO tagged DATA_INTEGRITY_REPAIR audit
-- rows (PreviousStatusId = NewStatusId). Never touches: Requests.SupplierId,
-- RequestStatus, GroupStatus (ADVANCE_PAYMENT_REQUIRED preserved), advance-payment
-- / finance state, totals, attachments, approvals/history (other than the new
-- audit rows), supplier master or its RegistrationStatus.
--
-- OPERATIONAL WARNING (reported by the preflight; NOT a blocking guard): supplier
-- 45 may be RegistrationStatus = DRAFT — future REGISTER_PO operations may remain
-- blocked until its registration completes. The repair proceeds regardless.
--
-- SINGLE TRANSACTION over both rows — all-or-nothing; any guard failure on EITHER
-- row aborts the entire repair (@@ROWCOUNT must be exactly 2 on UPDATE and audit
-- INSERT). Idempotent: both already repaired => ALREADY_REPAIRED (no writes, no
-- duplicate audit); a MIX or any other state => abort for manual review.
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial -E -b -i po-flow-req193-194-supplier-po-repair.sql ^
--          -v actor="<admin user guid>" -v poHash193="<sha256>" -v poHash194="<sha256>"
--   (rehearse on a restored clone copy first, seeding the drifted state)
-- Rollback: po-flow-req193-194-supplier-po-rollback.sql
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

-- ── Operator pins: the PO attachment SHA-256 values printed by the preflight ──
DECLARE @poHash193 NVARCHAR(100) = LOWER(N'$(poHash193)');
DECLARE @poHash194 NVARCHAR(100) = LOWER(N'$(poHash194)');
IF @poHash193 IS NULL OR LEN(@poHash193) <> 64 OR @poHash193 LIKE '%[^0-9a-f]%'
BEGIN
    PRINT 'ABORTED: pass REQ-193''s PO attachment SHA-256 (64 hex chars, from the preflight output) via  -v poHash193="<sha256>".';
    SET NOEXEC ON;
END
IF @poHash194 IS NULL OR LEN(@poHash194) <> 64 OR @poHash194 LIKE '%[^0-9a-f]%'
BEGIN
    PRINT 'ABORTED: pass REQ-194''s PO attachment SHA-256 (64 hex chars, from the preflight output) via  -v poHash194="<sha256>".';
    SET NOEXEC ON;
END

-- ── Allow-list with every reviewed expectation pinned ──
DECLARE @targets TABLE (
    RequestNumber NVARCHAR(50) PRIMARY KEY,
    ExpectedGroupId UNIQUEIDENTIFIER,
    ExpectedCompanyId INT,
    ExpectedTotal DECIMAL(18,2),
    OldPo NVARCHAR(100),
    NewPo NVARCHAR(100),
    CanonicalPattern NVARCHAR(60),
    PoFilenamePattern NVARCHAR(60),
    SourceAttachmentId UNIQUEIDENTIFIER,
    SourceFileHash NVARCHAR(100),
    PoHash NVARCHAR(100),
    PoAttachmentId UNIQUEIDENTIFIER,
    AuditTag NVARCHAR(40)
);
INSERT INTO @targets VALUES
 (N'REQ-31/07/2026-193', 'f20b272f-00d9-4a31-a9fc-948ac4d30f8c', 1, 3661359.15,
  N'FT 26/72087', N'ECF11 2026/420', N'%ECF11%2026#420', N'%ECF11%2026420%',
  '9d68c416-9152-4766-a3e1-45b4ba24099e', N'297a2686dac84a16cf7c719836de9b6d3d062781bbf53324de889edfd551fdf2',
  NULL, NULL, N'[HIST-SUPPLIER-PO-REQ-193]'),
 (N'REQ-31/07/2026-194', 'a535dabd-ea4e-4749-ab0f-1da3d136fd4f', 2, 1050755.95,
  N'FT 73094', N'ECF11 2026/38', N'%ECF11%2026#38', N'%ECF11%202638%',
  '44b9e0da-8baf-44aa-a833-fa992084a12d', N'08cca7aa13599b4e10eabe599a50c014f44a6b52ad94bf08270a0def269a5c96',
  NULL, NULL, N'[HIST-SUPPLIER-PO-REQ-194]');
UPDATE @targets SET PoHash = @poHash193 WHERE RequestNumber = N'REQ-31/07/2026-193';
UPDATE @targets SET PoHash = @poHash194 WHERE RequestNumber = N'REQ-31/07/2026-194';

BEGIN TRANSACTION;
BEGIN TRY
    -- Resolve each live PO attachment: EXACTLY ONE active TYPE_PO row per request whose
    -- sanitized filename carries the reviewed reference AND whose hash equals the pin.
    UPDATE t
    SET t.PoAttachmentId =
        (SELECT a.Id FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
         WHERE r.RequestNumber = t.RequestNumber AND a.AttachmentTypeCode = 'PO'
           AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
           AND UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE t.PoFilenamePattern
           AND LOWER(a.FileHash) = t.PoHash)
    FROM @targets t;

    -- Idempotency triage across the WHOLE set
    DECLARE @pending INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r         ON r.RequestNumber = t.RequestNumber
         JOIN RequestStatuses rs ON rs.Id = r.StatusId
         JOIN RequestPoGroups g  ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         JOIN Suppliers s        ON s.Id = 45
         WHERE (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r.Id) = 1
           AND g.SupplierId IS NULL
           AND g.SupplierNameSnapshot = N'Fornecedor não definido'
           AND g.SupplierNifSnapshot IS NULL
           AND g.PurchaseOrderNumber = t.OldPo
           AND r.SupplierId IS NULL
           AND rs.Code = 'ADVANCE_PAYMENT_REQUIRED'
           AND g.Status = 'ADVANCE_PAYMENT_REQUIRED'
           AND r.CompanyId = t.ExpectedCompanyId
           AND g.TotalAmount = t.ExpectedTotal
           AND s.TaxId = N'5417061590'
           AND s.IsActive = 1
           AND EXISTS (SELECT 1 FROM RequestAttachments a
                       WHERE a.Id = t.SourceAttachmentId AND a.RequestId = r.Id
                         AND a.AttachmentTypeCode = 'PROFORMA'
                         AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                         AND LOWER(a.FileHash) = LOWER(t.SourceFileHash))
           AND t.PoAttachmentId IS NOT NULL
           AND (SELECT COUNT(*) FROM RequestAttachments a2
                WHERE a2.RequestId = r.Id AND a2.AttachmentTypeCode = 'PO'
                  AND a2.IsDeleted = 0 AND a2.VoidedAtUtc IS NULL) = 1
           AND NOT EXISTS (SELECT 1 FROM RequestPoGroups gx JOIN Requests rx ON rx.Id = gx.RequestId
                           WHERE gx.Id <> t.ExpectedGroupId AND rx.CompanyId = t.ExpectedCompanyId AND gx.PurchaseOrderNumber IS NOT NULL
                             AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(gx.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE t.CanonicalPattern));
    DECLARE @repaired INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r        ON r.RequestNumber = t.RequestNumber
         JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         JOIN Suppliers s       ON s.Id = 45
         WHERE g.SupplierId = 45
           AND g.SupplierNameSnapshot = s.Name
           AND g.SupplierNifSnapshot = N'5417061590'
           AND g.PurchaseOrderNumber = t.NewPo);

    IF @repaired = 2 AND @pending = 0
    BEGIN
        PRINT 'ALREADY_REPAIRED: both groups already carry supplier 45 and their reviewed PO numbers — nothing written, no duplicate audit rows.';
        COMMIT TRANSACTION;
    END
    ELSE IF @pending <> 2
    BEGIN
        PRINT CONCAT('ABORTED (MANUAL_REVIEW_REQUIRED): pending=', @pending, ' repaired=', @repaired,
                     ' of 2 — mixed or unexpected state (statuses, current PO values, snapshots, company, totals, supplier master, evidence hashes, single pinned PO attachments, or canonical collisions). Run the preflight for detail. No partial repair is ever performed. Nothing written, rolled back.');
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.SupplierId = 45,
            g.SupplierNameSnapshot = s.Name,
            g.SupplierNifSnapshot = s.TaxId,
            g.PurchaseOrderNumber = t.NewPo,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN Requests r  ON r.Id = g.RequestId
        JOIN @targets t  ON t.RequestNumber = r.RequestNumber AND t.ExpectedGroupId = g.Id
        JOIN Suppliers s ON s.Id = 45
        WHERE g.SupplierId IS NULL
          AND g.PurchaseOrderNumber = t.OldPo;

        IF @@ROWCOUNT <> 2
        BEGIN
            PRINT 'ABORTED: UPDATE affected a row count different from 2 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   CONCAT(t.AuditTag,
                          N' [Reparo de integridade — campanha histórica final] Grupo ', CONVERT(NVARCHAR(36), t.ExpectedGroupId),
                          N': fornecedor FIDELIDADE confirmado por revisão humana do próprio documento de P.O;',
                          N' o documento traz N.º Contrib. 5417061590 (= NIF do cadastro do fornecedor 45) e o cabeçalho "PO Serviços ', t.NewPo, N'".',
                          N' O valor anteriormente registrado ''', t.OldPo, N''' é o "N.º Doc. Externo" do documento de P.O — informação válida do documento, mas NÃO a identidade da P.O.',
                          N' Correções: SupplierId NULL -> 45; snapshots restaurados do cadastro (', s.Name, N', NIF ', s.TaxId, N');',
                          N' PurchaseOrderNumber ''', t.OldPo, N''' -> ''', t.NewPo, N''' (a identidade real da P.O no Primavera).',
                          N' Evidências: anexo P.O ', CONVERT(NVARCHAR(36), t.PoAttachmentId), N' SHA-256 ', t.PoHash,
                          N'; documento fonte PROFORMA ', CONVERT(NVARCHAR(36), t.SourceAttachmentId), N' SHA-256 ', t.SourceFileHash, N'.',
                          N' Revisão 2026-08-20. Nenhum estado de workflow/status/financeiro (adiantamento), valor, anexo ou cadastro de fornecedor alterado.'),
                   SYSUTCDATETIME()
            FROM @targets t
            JOIN Requests r  ON r.RequestNumber = t.RequestNumber
            JOIN Suppliers s ON s.Id = 45;

            IF @@ROWCOUNT <> 2
            BEGIN
                PRINT 'ABORTED: audit insert affected a row count different from 2 — rolled back, nothing persisted.';
                ROLLBACK TRANSACTION;
            END
            ELSE
            BEGIN
                PRINT 'REPAIRED: REQ-193 and REQ-194 groups now carry supplier 45 and POs ''ECF11 2026/420'' / ''ECF11 2026/38'' (2 audit rows written). Statuses untouched (ADVANCE_PAYMENT_REQUIRED preserved).';
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
WHERE g.Id IN ('f20b272f-00d9-4a31-a9fc-948ac4d30f8c','a535dabd-ea4e-4749-ab0f-1da3d136fd4f')
ORDER BY r.RequestNumber;
