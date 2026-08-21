-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- POPULATION-B SUPPLIER REPAIR (allow-list: EXACTLY six requests)
--
--   TDA-COMERCIO INDÚSTRIA, LDA (SupplierId 34, NIF 5410002857):
--     REQ-03/08/2026-208 (company 2), REQ-05/08/2026-215 (company 2), REQ-06/08/2026-222 (company 1)
--   Embrace Angola - Prestação de Serviços, LDA (SupplierId 257, NIF 5417101524):
--     REQ-12/08/2026-237, REQ-12/08/2026-238, REQ-12/08/2026-241 (all company 1)
--
-- Evidence: NIF_EXACT extraction from each request's stored PROFORMA source document
-- (attachment ids + SHA-256 pinned below), reviewed 2026-08-20. Filenames were NOT used
-- as evidence — only the extracted NIF matched exactly to the Portal supplier.
--
-- Writes ONLY RequestPoGroups.SupplierId / SupplierNameSnapshot / SupplierNifSnapshot
-- (+ UpdatedAtUtc/UpdatedByUserId bookkeeping, the established A1 pattern) and one
-- DATA_INTEGRITY_REPAIR audit row per request (PreviousStatusId = NewStatusId).
-- Requests.SupplierId (header) is deliberately NOT touched (see review note in the
-- accompanying report). Never touches: request status, group status,
-- PurchaseOrderNumber, totals, attachments, workflow/payment/finance/quotation state.
--
-- SINGLE TRANSACTION over all six rows — all-or-nothing. Any guard failure on ANY row
-- aborts the entire repair. Idempotent: all six already repaired => ALREADY_REPAIRED,
-- no writes, no duplicate audit rows; a MIX of repaired/unrepaired => abort for manual
-- review (no silent completion of the remainder).
--
-- Usage:
--   sqlcmd -S <instance> -d Portal-Gerencial-Test -E -b -i po-flow-population-b-supplier-repair.sql -v actor="<admin user guid>"
--   (rehearse on TEST first; then, with authorization, -d Portal-Gerencial)
-- Rollback: po-flow-population-b-supplier-repair-rollback.sql
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
    AuditTag NVARCHAR(40)
);
INSERT INTO @targets VALUES
 (N'REQ-03/08/2026-208', '2bbeb116-7742-4b2d-aece-5c339bc418d8', 2, 34,  N'5410002857', '9eea40d5-fbb1-4bcd-8052-5ccd89b2359d', N'202525d267b9e29224cb0fa4cf62c5ee77e43fe394fbe9c746742d0af553a548', N'[POP-B-SUPPLIER-REQ-208]'),
 (N'REQ-05/08/2026-215', '3d3c7558-7987-44d2-ad2e-5c7c5d70f16f', 2, 34,  N'5410002857', '9b05afc9-7568-4b54-83d8-e93c8d559128', N'd20d0e54cbdd4ff0ca00afc40119f2b22f9633abbb247f55c834f12ff98fd607', N'[POP-B-SUPPLIER-REQ-215]'),
 (N'REQ-06/08/2026-222', 'db7e000c-79d4-41fb-b370-55b8d8c883b2', 1, 34,  N'5410002857', 'eecff47d-d519-4f77-80ba-a579870a7d08', N'7cac8d5073007f138efdd776156cc2b5129ffa72492d2f31ed576c56d5b92fe7', N'[POP-B-SUPPLIER-REQ-222]'),
 (N'REQ-12/08/2026-237', '63a27af3-ff62-421f-966e-e212188a1bce', 1, 257, N'5417101524', '5bcffcef-f312-4668-8b79-33b8aef8f409', N'0ea0ea5d2e921a9eb1232af0b3586cb082dfc2b5efa6b6ba67ceb8475eb219f8', N'[POP-B-SUPPLIER-REQ-237]'),
 (N'REQ-12/08/2026-238', 'ecf53b7a-16ce-4844-92ba-781cd22df721', 1, 257, N'5417101524', '0732c086-93ad-4bb8-8aba-decdbe7949da', N'aea16d770608c2e319e38e7a6e178406566679dcc6344a3bf3970fa46fc41f12', N'[POP-B-SUPPLIER-REQ-238]'),
 (N'REQ-12/08/2026-241', '72e38cf2-f445-4462-9435-b86d59a195ea', 1, 257, N'5417101524', 'cfd7fa5c-15ca-4f27-9f7d-1b1e1916f073', N'd9c20b5ff7f52bf1fd703668526dce49a35febef29df3175fe19aa5bef20926c', N'[POP-B-SUPPLIER-REQ-241]');

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
              AND s.TaxId = t.ExpectedSupplierNif
              AND s.IsActive = 1
              AND EXISTS (SELECT 1 FROM RequestAttachments a
                          WHERE a.Id = t.EvidenceAttachmentId AND a.RequestId = r.Id
                            AND a.AttachmentTypeCode = 'PROFORMA'
                            AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                            AND LOWER(a.FileHash) = LOWER(t.EvidenceFileHash))))
    BEGIN
        PRINT 'ABORTED: at least one target failed a guard (group identity, snapshots, statuses, company, supplier NIF/active, or evidence hash). Run the preflight for the per-row PASS/FAIL detail. Nothing written, rolled back.';
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
                          ' [Reparo de integridade — P.O Flow v2.229.12] Fornecedor do grupo ', LEFT(CONVERT(NVARCHAR(36), t.ExpectedGroupId), 8),
                          ' preenchido a partir de evidência documental NIF_EXACT: SupplierId NULL',
                          ' (snapshot legado ''Fornecedor não definido''/NULL) -> ', t.ExpectedSupplierId,
                          ' — ', s.Name, ' (NIF ', s.TaxId, ').',
                          ' Evidência: anexo PROFORMA ', CONVERT(NVARCHAR(36), t.EvidenceAttachmentId),
                          ' SHA-256 ', t.EvidenceFileHash, ', NIF extraído do documento armazenado = NIF do fornecedor (revisão 2026-08-20).',
                          ' Motivo: grupo P.O legado da População B sem fornecedor estruturado.',
                          ' Nenhum estado de workflow, número de P.O, valor ou anexo alterado.'),
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
                PRINT 'REPAIRED: all six Population-B groups now carry their NIF-exact suppliers (6 audit rows written).';
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
WHERE g.Id IN ('2bbeb116-7742-4b2d-aece-5c339bc418d8','3d3c7558-7987-44d2-ad2e-5c7c5d70f16f',
               'db7e000c-79d4-41fb-b370-55b8d8c883b2','63a27af3-ff62-421f-966e-e212188a1bce',
               'ecf53b7a-16ce-4844-92ba-781cd22df721','72e38cf2-f445-4462-9435-b86d59a195ea')
ORDER BY r.RequestNumber;
