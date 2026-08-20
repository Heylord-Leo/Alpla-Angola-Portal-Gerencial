-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- FINAL SIX HISTORICAL SUPPLIER REPAIR (allow-list: EXACTLY six requests)
--
--   REQ-16/07/2026-084 -> 53  REALVITUR ANGOLA, LIMITADA            (NIF 5417089079, company 1)
--   REQ-29/07/2026-178 -> 66  IMPORAFRICA VEICULOS LDA              (NIF 5417231983, company 1)
--   REQ-31/07/2026-193 -> 45  FIDELIDADE ANGOLA-COMP. DE SEGUROS    (NIF 5417061590, company 1)
--   REQ-31/07/2026-194 -> 45  FIDELIDADE ANGOLA-COMP. DE SEGUROS    (NIF 5417061590, company 2)
--   REQ-31/07/2026-200 -> 157 HENDA HOTELARIA , LDA - HCTA          (NIF 5001094645, company 1)
--   REQ-12/08/2026-245 -> 159 MUSOLAND-MUNDO DAS SOLUCOES-ACESS.CONS.(SU),LDA (NIF 5417386740, company 1)
--
-- Evidence: HUMAN CONFIRMATION of each request's stored source document (review
-- closed 2026-08-20). Filenames and OCR were NOT used as supplier-identity
-- evidence. The pinned PROFORMA attachment id/SHA-256 per row is a drift-detection
-- anchor proving the reviewed document is still the active source document.
--
-- Writes ONLY RequestPoGroups.SupplierId / SupplierNameSnapshot / SupplierNifSnapshot
-- (+ UpdatedAtUtc/UpdatedByUserId bookkeeping, the established pattern) and one
-- DATA_INTEGRITY_REPAIR audit row per request (PreviousStatusId = NewStatusId).
-- Requests.SupplierId (header) is deliberately NOT touched. Never touches: request
-- status, group status, PurchaseOrderNumber, totals, attachments, supplier master
-- or RegistrationStatus, workflow/payment/finance/quotation state.
--
-- OPERATIONAL WARNING (reported by the preflight; NOT a blocking guard): suppliers
-- 45/157/159 are RegistrationStatus = DRAFT — register-po will refuse them until
-- their master registration completes. The historical repair proceeds regardless.
--
-- SINGLE TRANSACTION over all six rows — all-or-nothing. Any guard failure on ANY
-- row aborts the entire repair. Idempotent: all six already repaired =>
-- ALREADY_REPAIRED, no writes, no duplicate audit rows; a MIX of repaired and
-- unrepaired => abort for manual review (no silent completion of the remainder).
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-final-six-supplier-repair.sql -v actor="<admin user guid>"
--   (rehearse on a restored clone copy first; then, with authorization, -d Portal-Gerencial)
-- Rollback: po-flow-final-six-supplier-repair-rollback.sql
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
    ExpectedTotal DECIMAL(18,2),
    AnchorAttachmentId UNIQUEIDENTIFIER,
    AnchorFileHash NVARCHAR(100),
    AuditTag NVARCHAR(40)
);
INSERT INTO @targets VALUES
 (N'REQ-16/07/2026-084', '886c0d0e-80d8-4ebe-8272-e4fa3304f5c3', 1, 53,  N'5417089079',  971392.00, '37fa585d-674f-47b1-b621-248dd845f5b0', N'78f0d3a0d0e26f421fadb0602566dea03affa43a05102c39fe4765da96791746', N'[HIST-SUPPLIER-REQ-084]'),
 (N'REQ-29/07/2026-178', '3d67213e-daba-4615-a0fc-108b19ea1a3e', 1, 66,  N'5417231983',  164167.67, '4831f40f-73d4-41a7-99a8-74c9492acf54', N'18c4299ed825509ff0c4f1a52ff6b498f3f90409bf50e2041ccaf0bc2a8c18a9', N'[HIST-SUPPLIER-REQ-178]'),
 (N'REQ-31/07/2026-193', 'f20b272f-00d9-4a31-a9fc-948ac4d30f8c', 1, 45,  N'5417061590', 3661359.15, '9d68c416-9152-4766-a3e1-45b4ba24099e', N'297a2686dac84a16cf7c719836de9b6d3d062781bbf53324de889edfd551fdf2', N'[HIST-SUPPLIER-REQ-193]'),
 (N'REQ-31/07/2026-194', 'a535dabd-ea4e-4749-ab0f-1da3d136fd4f', 2, 45,  N'5417061590', 1050755.95, '44b9e0da-8baf-44aa-a833-fa992084a12d', N'08cca7aa13599b4e10eabe599a50c014f44a6b52ad94bf08270a0def269a5c96', N'[HIST-SUPPLIER-REQ-194]'),
 (N'REQ-31/07/2026-200', 'a4c5cc42-2f8d-48ec-b9a9-0885c9f92081', 1, 157, N'5001094645', 2120186.00, '0c1be5e5-516f-4b37-8b8a-45d6f6675368', N'94168ba104d99c5ee57ee240aac108e1665c3aab1148263c2bfd3babcf7e6c4e', N'[HIST-SUPPLIER-REQ-200]'),
 (N'REQ-12/08/2026-245', 'fe684497-448f-471a-8461-377ba3dc47c5', 1, 159, N'5417386740',  239400.00, '6f5f7e9c-8899-45ba-93de-63fa47b922bf', N'f55d286e8342b4264cb298add5811f76ecd44443901e3fa3af3b949fac74e02e', N'[HIST-SUPPLIER-REQ-245]');

BEGIN TRANSACTION;
BEGIN TRY
    -- ── Idempotency triage across the WHOLE set ──
    -- Pending = the FULL repairable state (a row whose workflow advanced — e.g. a PO
    -- was registered — is NOT pending even though its supplier fields still look legacy).
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
           AND g.PurchaseOrderNumber IS NULL
           AND g.TotalAmount = t.ExpectedTotal);
    DECLARE @repaired INT =
        (SELECT COUNT(*) FROM @targets t
         JOIN Requests r        ON r.RequestNumber = t.RequestNumber
         JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
         WHERE g.SupplierId = t.ExpectedSupplierId
           AND g.SupplierNifSnapshot = t.ExpectedSupplierNif);

    IF @repaired = 6 AND @pending = 0
    BEGIN
        PRINT 'ALREADY_REPAIRED: all six groups already carry the expected suppliers — nothing written, no duplicate audit rows.';
        COMMIT TRANSACTION;
    END
    ELSE IF @pending <> 6
    BEGIN
        PRINT CONCAT('ABORTED (MANUAL_REVIEW_REQUIRED): pending=', @pending, ' repaired=', @repaired,
                     ' of 6 — mixed or unexpected state. No partial repair is ever performed. Nothing written, rolled back.');
        ROLLBACK TRANSACTION;
    END
    -- ── Every guard must pass on every row, or the WHOLE repair aborts ──
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
              AND g.TotalAmount = t.ExpectedTotal
              AND s.TaxId = t.ExpectedSupplierNif
              AND s.IsActive = 1
              AND EXISTS (SELECT 1 FROM RequestAttachments a
                          WHERE a.Id = t.AnchorAttachmentId AND a.RequestId = r.Id
                            AND a.AttachmentTypeCode = 'PROFORMA'
                            AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                            AND LOWER(a.FileHash) = LOWER(t.AnchorFileHash))))
    BEGIN
        PRINT 'ABORTED: at least one target failed a guard (group identity, snapshots, statuses, company, total, supplier NIF/active, or anchor-document hash). Run the preflight for the per-row PASS/FAIL detail. Nothing written, rolled back.';
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

        IF @@ROWCOUNT <> 6
        BEGIN
            PRINT 'ABORTED: UPDATE affected a row count different from 6 — rolled back, nothing persisted.';
            ROLLBACK TRANSACTION;
        END
        ELSE
        BEGIN
            INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
            SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
                   CONCAT(t.AuditTag,
                          ' [Reparo de integridade — campanha histórica final] Fornecedor do grupo ', LEFT(CONVERT(NVARCHAR(36), t.ExpectedGroupId), 8),
                          ' preenchido: SupplierId NULL (snapshot legado ''Fornecedor não definido''/NULL) -> ', t.ExpectedSupplierId,
                          ' — ', s.Name, ' (NIF ', s.TaxId, ').',
                          ' Base: identidade do fornecedor CONFIRMADA POR REVISÃO HUMANA do documento fonte armazenado (revisão fechada 2026-08-20);',
                          ' nome de ficheiro e OCR NÃO foram usados como evidência de identidade.',
                          ' Âncora documental: anexo PROFORMA ', CONVERT(NVARCHAR(36), t.AnchorAttachmentId),
                          ' SHA-256 ', t.AnchorFileHash, ' verificado no momento do reparo.',
                          ' Motivo: grupo P.O legado sem fornecedor estruturado.',
                          ' Nenhum estado de workflow, número de P.O, valor, anexo ou cadastro de fornecedor alterado.'),
                   SYSUTCDATETIME()
            FROM @targets t
            JOIN Requests r  ON r.RequestNumber = t.RequestNumber
            JOIN Suppliers s ON s.Id = t.ExpectedSupplierId;

            IF @@ROWCOUNT <> 6
            BEGIN
                PRINT 'ABORTED: audit insert affected a row count different from 6 — rolled back, nothing persisted.';
                ROLLBACK TRANSACTION;
            END
            ELSE
            BEGIN
                PRINT 'REPAIRED: all six historical groups now carry their human-confirmed suppliers (6 audit rows written).';
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
WHERE g.Id IN ('886c0d0e-80d8-4ebe-8272-e4fa3304f5c3','3d67213e-daba-4615-a0fc-108b19ea1a3e',
               'f20b272f-00d9-4a31-a9fc-948ac4d30f8c','a535dabd-ea4e-4749-ab0f-1da3d136fd4f',
               'a4c5cc42-2f8d-48ec-b9a9-0885c9f92081','fe684497-448f-471a-8461-377ba3dc47c5')
ORDER BY r.RequestNumber;
