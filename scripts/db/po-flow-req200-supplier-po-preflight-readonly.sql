-- ============================================================================
-- REQ-200 SUPPLIER + PO CORRECTION — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the dedicated, state-aware repair for EXACTLY one request:
--   REQ-31/07/2026-200 (company 1, live PROD state: RequestStatus PO_ISSUED)
--   Group a4c5cc42-2f8d-48ec-b9a9-0885c9f92081 (PO_ISSUED, total 2120186.00)
--
-- Human/document review (decisive, 2026-08-20, the actual PROD PO PDF):
--   heading "PO Serviços ECF11 2026/424" (canonical ECF11-2026-424);
--   N.º Contrib. on the document = 5001094645 = HENDA master NIF (SupplierId 157);
--   N.º Doc. Externo = "FT 453" — the value wrongly registered as the PO number.
--   'FT 453' is valid source information (external-document number), NOT the PO
--   identity. The PO was registered under PROD v2.229.9, which lacks the
--   v2.229.12 null-supplier/NIF guards, against a supplier-less group.
--
-- This preflight RESOLVES AND PRINTS the live PO attachment (expected filename
-- PO__Servios__ECF11_2026424_-_424.pdf, uploaded by the buyer just before
-- REGISTER_PO): its Id and full SHA-256 are the OPERATOR PIN that the repair
-- script requires back via  -v poHash="<sha256>".
--
-- OPERATIONAL WARNING (does NOT block this historical repair): supplier 157
-- (HENDA) is RegistrationStatus = DRAFT — future REGISTER_PO operations may
-- remain blocked until the master registration is completed. Reported below.
--
-- SELECT/PRINT only — no writes. Run before po-flow-req200-supplier-po-repair.sql
-- and STOP unless every guard is PASS and RepairState = PENDING_REPAIR.
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

-- ── Environment guard: identical to the repair/rollback scripts ──
DECLARE @connectedDb SYSNAME = DB_NAME();
IF @connectedDb NOT IN ('Portal-Gerencial-Test', 'Portal-Gerencial')
BEGIN
    RAISERROR('ABORTED: connected database is [%s] on server [%s] — the ONLY accepted Portal databases are [Portal-Gerencial-Test] (TEST rehearsal) and [Portal-Gerencial] (PROD). No bypass exists.', 16, 1, @connectedDb, @@SERVERNAME) WITH NOWAIT;
    SET NOEXEC ON;
END

-- ── Context ──
SELECT @@SERVERNAME AS ServerIdentity, DB_NAME() AS DatabaseName, ORIGINAL_LOGIN() AS OriginalLogin,
       CASE DB_NAME() WHEN 'Portal-Gerencial' THEN 'PROD'
                      WHEN 'Portal-Gerencial-Test' THEN 'TEST'
                      ELSE 'DISALLOWED' END AS ExecutionContext;

-- ── Pinned expectations ──
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

-- ── Full current state ──
SELECT r.RequestNumber, r.Id AS RequestId, rs.Code AS RequestStatus, r.CompanyId, c.Name AS Company,
       r.SupplierId AS HeaderSupplierId,
       g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot,
       ISNULL(g.PurchaseOrderNumber,  N'<NULL>') AS CurrentPo,
       g.TotalAmount, g.UpdatedAtUtc, g.UpdatedByUserId,
       s.Id AS ExpectedSupplierId, s.Name AS ExpectedSupplierName, s.TaxId AS ExpectedSupplierNif,
       s.IsActive AS SupplierIsActive, s.RegistrationStatus
FROM Requests r
JOIN RequestStatuses rs ON rs.Id = r.StatusId
JOIN Companies c        ON c.Id = r.CompanyId
LEFT JOIN RequestPoGroups g ON g.RequestId = r.Id
LEFT JOIN Suppliers s   ON s.Id = @supplierId
WHERE r.RequestNumber = @requestNumber;

-- ── Operational warning: DRAFT supplier (report-only, NOT a blocking guard) ──
SELECT s.Id AS SupplierId, s.Name, s.TaxId, s.RegistrationStatus,
       'WARNING: REGISTER_PO may remain blocked for this supplier until its registration is completed — the historical data repair itself is NOT blocked' AS OperationalWarning
FROM Suppliers s
WHERE s.Id = @supplierId AND s.RegistrationStatus <> 'ACTIVE';

-- ── OPERATOR PIN SOURCE: all active PO attachments on this request (live values) ──
-- The repair requires the SHA-256 of the single ECF11 2026/424 PO document below
-- back via -v poHash. Record Id + FileHash from this output.
SELECT a.Id AS PoAttachmentId, a.FileName,
       ISNULL(a.FileHash, N'<NULL>') AS FileHash, a.RequestPoGroupId, a.UploadedAtUtc,
       u.FullName AS UploadedBy,
       CASE WHEN UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE '%ECF11%2026424%'
            THEN 'Filename carries the ECF11 2026/424 reference — candidate for the -v poHash pin'
            ELSE 'Filename does NOT carry the ECF11 2026/424 reference' END AS PinCandidate
FROM RequestAttachments a
JOIN Requests r ON r.Id = a.RequestId
LEFT JOIN Users u ON u.Id = a.UploadedByUserId
WHERE r.RequestNumber = @requestNumber
  AND a.AttachmentTypeCode = 'PO'
  AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
ORDER BY a.UploadedAtUtc;

-- ── Source/proforma anchor (pinned from reviewed data) ──
SELECT a.Id AS SourceAttachmentId, a.AttachmentTypeCode, a.FileName,
       ISNULL(a.FileHash, N'<NULL>') AS FileHash,
       CASE WHEN a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                 AND LOWER(a.FileHash) = LOWER(@sourceFileHash)
            THEN 'Reviewed source document present, hash verified'
            ELSE 'SOURCE ATTACHMENT PROBLEM — do not repair' END AS SourceStatus
FROM RequestAttachments a
JOIN Requests r ON r.Id = a.RequestId
WHERE r.RequestNumber = @requestNumber AND a.Id = @sourceAttachmentId;

-- ── REGISTER_PO / repair trace (context display only) ──
SELECT h.ActionTaken, h.Comment, h.CreatedAtUtc, u.FullName AS Actor
FROM RequestStatusHistories h
JOIN Requests r ON r.Id = h.RequestId
LEFT JOIN Users u ON u.Id = h.ActorUserId
WHERE r.RequestNumber = @requestNumber
  AND h.ActionTaken IN ('REGISTER_PO', 'DATA_INTEGRITY_REPAIR')
ORDER BY h.CreatedAtUtc;

-- ── Canonical collision scan for ECF11-2026-424 (same company, excluding this group) ──
SELECT r.RequestNumber, r.CompanyId, g.Id AS GroupId, g.PurchaseOrderNumber
FROM RequestPoGroups g
JOIN Requests r ON r.Id = g.RequestId
WHERE g.Id <> @groupId
  AND r.CompanyId = @companyId
  AND g.PurchaseOrderNumber IS NOT NULL
  AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#424';

-- ── Guard evaluation: PASS/FAIL per named guard ──
SELECT gd.CheckName, CASE WHEN gd.Passed = 1 THEN 'PASS' ELSE 'FAIL' END AS CheckResult
FROM (VALUES
  ('request_exists_and_unique',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r WHERE r.RequestNumber = @requestNumber)),
  ('exactly_one_group_for_request',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE r.RequestNumber = @requestNumber)),
  ('resolved_group_is_reviewed_group',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE r.RequestNumber = @requestNumber AND g.Id = @groupId)),
  ('request_status_is_PO_ISSUED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r JOIN RequestStatuses rs ON rs.Id = r.StatusId
     WHERE r.RequestNumber = @requestNumber AND rs.Code = 'PO_ISSUED')),
  ('group_status_is_PO_ISSUED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.Status = 'PO_ISSUED')),
  ('current_po_is_exactly_FT_453',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.PurchaseOrderNumber = @oldPo)),
  ('group_supplier_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.SupplierId IS NULL)),
  ('snapshots_are_reviewed_legacy_placeholder',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.SupplierNifSnapshot IS NULL
       AND g.SupplierNameSnapshot = N'Fornecedor não definido')),
  ('request_header_supplier_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = @requestNumber AND r.SupplierId IS NULL)),
  ('company_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = @requestNumber AND r.CompanyId = @companyId)),
  ('total_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.TotalAmount = @total)),
  ('expected_supplier_exists_active_with_exact_nif',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Suppliers s
     WHERE s.Id = @supplierId AND s.TaxId = @supplierNif AND s.IsActive = 1)),
  ('source_attachment_present_hash_verified',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE a.Id = @sourceAttachmentId AND r.RequestNumber = @requestNumber
       AND a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND LOWER(a.FileHash) = LOWER(@sourceFileHash))),
  ('exactly_one_active_po_attachment_with_ECF11_2026_424_filename',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE r.RequestNumber = @requestNumber AND a.AttachmentTypeCode = 'PO'
       AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE '%ECF11%2026424%')),
  ('no_same_company_canonical_collision_ECF11_2026_424',
    (SELECT CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE g.Id <> @groupId AND r.CompanyId = @companyId AND g.PurchaseOrderNumber IS NOT NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#424'))
) AS gd(CheckName, Passed);

-- ── Final state ──
-- PENDING_REPAIR requires the FULL pending predicate; ALREADY_REPAIRED only the
-- exact repaired state; anything else is MANUAL_REVIEW_REQUIRED.
SELECT
  CASE
    WHEN EXISTS (
        SELECT 1
        FROM Requests r2
        JOIN RequestStatuses rs2 ON rs2.Id = r2.StatusId
        JOIN RequestPoGroups g2  ON g2.RequestId = r2.Id
        JOIN Suppliers s2        ON s2.Id = @supplierId
        WHERE r2.RequestNumber = @requestNumber
          AND g2.Id = @groupId
          AND (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r2.Id) = 1
          AND g2.SupplierId IS NULL
          AND g2.SupplierNameSnapshot = N'Fornecedor não definido'
          AND g2.SupplierNifSnapshot IS NULL
          AND g2.PurchaseOrderNumber = @oldPo
          AND r2.SupplierId IS NULL
          AND rs2.Code = 'PO_ISSUED'
          AND g2.Status = 'PO_ISSUED'
          AND r2.CompanyId = @companyId
          AND g2.TotalAmount = @total
          AND s2.TaxId = @supplierNif
          AND s2.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a2
                      WHERE a2.Id = @sourceAttachmentId AND a2.RequestId = r2.Id
                        AND a2.AttachmentTypeCode = 'PROFORMA'
                        AND a2.IsDeleted = 0 AND a2.VoidedAtUtc IS NULL
                        AND LOWER(a2.FileHash) = LOWER(@sourceFileHash))
          AND (SELECT COUNT(*) FROM RequestAttachments a3
               WHERE a3.RequestId = r2.Id AND a3.AttachmentTypeCode = 'PO'
                 AND a3.IsDeleted = 0 AND a3.VoidedAtUtc IS NULL
                 AND UPPER(REPLACE(REPLACE(REPLACE(a3.FileName,' ',''),'.',''),'_','')) LIKE '%ECF11%2026424%') = 1
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups gx JOIN Requests rx ON rx.Id = gx.RequestId
                          WHERE gx.Id <> @groupId AND rx.CompanyId = @companyId AND gx.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(gx.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#424'))
      THEN 'PENDING_REPAIR'
    WHEN EXISTS (
        SELECT 1
        FROM Requests r3
        JOIN RequestStatuses rs3 ON rs3.Id = r3.StatusId
        JOIN RequestPoGroups g3  ON g3.RequestId = r3.Id
        JOIN Suppliers s3        ON s3.Id = @supplierId
        WHERE r3.RequestNumber = @requestNumber
          AND g3.Id = @groupId
          AND g3.SupplierId = @supplierId
          AND g3.SupplierNameSnapshot = s3.Name
          AND g3.SupplierNifSnapshot = @supplierNif
          AND g3.PurchaseOrderNumber = @newPo
          AND rs3.Code = 'PO_ISSUED'
          AND g3.Status = 'PO_ISSUED')
      THEN 'ALREADY_REPAIRED'
    ELSE 'MANUAL_REVIEW_REQUIRED'
  END AS RepairState;
