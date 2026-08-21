-- ============================================================================
-- REQ-193 + REQ-194 FIDELIDADE SUPPLIER + PO CORRECTION — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the dedicated, state-aware TWO-REQUEST repair:
--
--   REQ-31/07/2026-193 (company 1) — group f20b272f-00d9-4a31-a9fc-948ac4d30f8c
--     live PROD: ADVANCE_PAYMENT_REQUIRED (request AND group), supplier NULL,
--     legacy snapshots, PO 'FT 26/72087', total 3661359.15
--   REQ-31/07/2026-194 (company 2) — group a535dabd-ea4e-4749-ab0f-1da3d136fd4f
--     live PROD: ADVANCE_PAYMENT_REQUIRED (request AND group), supplier NULL,
--     legacy snapshots, PO 'FT 73094', total 1050755.95
--
-- Human/document review (decisive, 2026-08-20): both PO PDFs carry N.º Contrib.
-- 5417061590 = FIDELIDADE ANGOLA-COMP. DE SEGUROS (SupplierId 45).
--   REQ-193 heading "PO Serviços ECF11 2026/420" -> PO 'ECF11 2026/420'
--            (canonical ECF11-2026-420); 'FT 26/72087' is the N.º Doc. Externo.
--   REQ-194 heading "PO Serviços ECF11 2026/38"  -> PO 'ECF11 2026/38'
--            (canonical ECF11-2026-38); 'FT 73094' is the N.º Doc. Externo.
-- The stored values are external-document numbers registered as PO numbers under
-- PROD v2.229.9 (which lacks the v2.229.12 guards) — NOT the PO identities.
--
-- This preflight RESOLVES AND PRINTS the live PO attachments per request: their
-- Ids and full SHA-256 hashes are the OPERATOR PINS the repair requires back via
--   -v poHash193="<sha256>" -v poHash194="<sha256>"
-- (the analysis clone predates these PO registrations — pins come from LIVE data).
--
-- OPERATIONAL WARNING (does NOT block this historical repair): supplier 45
-- (FIDELIDADE) may be RegistrationStatus = DRAFT — future REGISTER_PO operations
-- may remain blocked until the master registration is completed. Reported below.
--
-- SELECT/PRINT only — no writes. Run before po-flow-req193-194-supplier-po-repair.sql
-- and STOP unless every guard is PASS and both rows are PENDING_REPAIR.
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

DECLARE @supplierId INT = 45;
DECLARE @supplierNif NVARCHAR(50) = N'5417061590';

-- ── Allow-list: the ONLY two targets, with every reviewed expectation pinned ──
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
    SourceFileHash NVARCHAR(100)
);
INSERT INTO @targets VALUES
 (N'REQ-31/07/2026-193', 'f20b272f-00d9-4a31-a9fc-948ac4d30f8c', 1, 3661359.15,
  N'FT 26/72087', N'ECF11 2026/420', N'%ECF11%2026#420', N'%ECF11%2026420%',
  '9d68c416-9152-4766-a3e1-45b4ba24099e', N'297a2686dac84a16cf7c719836de9b6d3d062781bbf53324de889edfd551fdf2'),
 (N'REQ-31/07/2026-194', 'a535dabd-ea4e-4749-ab0f-1da3d136fd4f', 2, 1050755.95,
  N'FT 73094', N'ECF11 2026/38', N'%ECF11%2026#38', N'%ECF11%202638%',
  '44b9e0da-8baf-44aa-a833-fa992084a12d', N'08cca7aa13599b4e10eabe599a50c014f44a6b52ad94bf08270a0def269a5c96');

-- ── Full current state of each target ──
SELECT t.RequestNumber, r.Id AS RequestId, rs.Code AS RequestStatus, r.CompanyId, c.Name AS Company,
       r.SupplierId AS HeaderSupplierId,
       g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot,
       ISNULL(g.PurchaseOrderNumber,  N'<NULL>') AS CurrentPo,
       t.OldPo AS ExpectedCurrentPo, t.NewPo AS ReviewedCorrectPo,
       g.TotalAmount, t.ExpectedTotal,
       s.Name AS ExpectedSupplierName, s.TaxId AS ExpectedSupplierNif,
       s.IsActive AS SupplierIsActive, s.RegistrationStatus
FROM @targets t
LEFT JOIN Requests r         ON r.RequestNumber = t.RequestNumber
LEFT JOIN RequestStatuses rs ON rs.Id = r.StatusId
LEFT JOIN Companies c        ON c.Id = r.CompanyId
LEFT JOIN RequestPoGroups g  ON g.RequestId = r.Id
LEFT JOIN Suppliers s        ON s.Id = @supplierId
ORDER BY t.RequestNumber;

-- ── Operational warning: DRAFT supplier (report-only, NOT a blocking guard) ──
SELECT s.Id AS SupplierId, s.Name, s.TaxId, s.RegistrationStatus,
       'WARNING: REGISTER_PO may remain blocked for this supplier until its registration is completed — the historical data repair itself is NOT blocked' AS OperationalWarning
FROM Suppliers s
WHERE s.Id = @supplierId AND s.RegistrationStatus <> 'ACTIVE';

-- ── OPERATOR PIN SOURCE: all active PO attachments per request (live values) ──
-- The repair requires the SHA-256 of each request's single reviewed PO document
-- back via -v poHash193 / -v poHash194. Record Id + FileHash from this output.
SELECT t.RequestNumber, a.Id AS PoAttachmentId, a.FileName,
       ISNULL(a.FileHash, N'<NULL>') AS FileHash, a.RequestPoGroupId, a.UploadedAtUtc,
       u.FullName AS UploadedBy,
       CASE WHEN UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE t.PoFilenamePattern
            THEN 'Filename carries the reviewed PO reference — candidate for the operator pin'
            ELSE 'Filename does NOT carry the reviewed PO reference' END AS PinCandidate
FROM @targets t
JOIN Requests r ON r.RequestNumber = t.RequestNumber
JOIN RequestAttachments a ON a.RequestId = r.Id
LEFT JOIN Users u ON u.Id = a.UploadedByUserId
WHERE a.AttachmentTypeCode = 'PO'
  AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
ORDER BY t.RequestNumber, a.UploadedAtUtc;

-- ── Source/proforma anchors (pinned from reviewed data) ──
SELECT t.RequestNumber, a.Id AS SourceAttachmentId, a.AttachmentTypeCode, a.FileName,
       ISNULL(a.FileHash, N'<NULL>') AS FileHash,
       CASE WHEN a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                 AND LOWER(a.FileHash) = LOWER(t.SourceFileHash)
            THEN 'Reviewed source document present, hash verified'
            ELSE 'SOURCE ATTACHMENT PROBLEM — do not repair' END AS SourceStatus
FROM @targets t
JOIN Requests r ON r.RequestNumber = t.RequestNumber
JOIN RequestAttachments a ON a.RequestId = r.Id AND a.Id = t.SourceAttachmentId;

-- ── REGISTER_PO / repair trace (context display only) ──
SELECT r.RequestNumber, h.ActionTaken, h.Comment, h.CreatedAtUtc, u.FullName AS Actor
FROM RequestStatusHistories h
JOIN Requests r ON r.Id = h.RequestId
LEFT JOIN Users u ON u.Id = h.ActorUserId
WHERE r.RequestNumber IN (SELECT RequestNumber FROM @targets)
  AND h.ActionTaken IN ('REGISTER_PO', 'DATA_INTEGRITY_REPAIR')
ORDER BY r.RequestNumber, h.CreatedAtUtc;

-- ── Canonical collision scans (same company, excluding each target group) ──
SELECT t.RequestNumber AS ForTarget, r.RequestNumber, r.CompanyId, g.Id AS GroupId, g.PurchaseOrderNumber
FROM @targets t
JOIN RequestPoGroups g ON g.Id <> t.ExpectedGroupId
JOIN Requests r        ON r.Id = g.RequestId AND r.CompanyId = t.ExpectedCompanyId
WHERE g.PurchaseOrderNumber IS NOT NULL
  AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE t.CanonicalPattern
ORDER BY t.RequestNumber;

-- ── Guard evaluation: PASS/FAIL per named guard per request ──
SELECT t.RequestNumber, gd.CheckName,
       CASE WHEN gd.Passed = 1 THEN 'PASS' ELSE 'FAIL' END AS CheckResult
FROM @targets t
CROSS APPLY (VALUES
  ('request_exists_and_unique',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r WHERE r.RequestNumber = t.RequestNumber)),
  ('exactly_one_group_for_request',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE r.RequestNumber = t.RequestNumber)),
  ('resolved_group_is_reviewed_group',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE r.RequestNumber = t.RequestNumber AND g.Id = t.ExpectedGroupId)),
  ('request_status_is_ADVANCE_PAYMENT_REQUIRED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r JOIN RequestStatuses rs ON rs.Id = r.StatusId
     WHERE r.RequestNumber = t.RequestNumber AND rs.Code = 'ADVANCE_PAYMENT_REQUIRED')),
  ('group_status_is_ADVANCE_PAYMENT_REQUIRED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = t.ExpectedGroupId AND g.Status = 'ADVANCE_PAYMENT_REQUIRED')),
  ('current_po_is_exactly_reviewed_old_value',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = t.ExpectedGroupId AND g.PurchaseOrderNumber = t.OldPo)),
  ('group_supplier_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = t.ExpectedGroupId AND g.SupplierId IS NULL)),
  ('snapshots_are_reviewed_legacy_placeholder',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = t.ExpectedGroupId AND g.SupplierNifSnapshot IS NULL
       AND g.SupplierNameSnapshot = N'Fornecedor não definido')),
  ('request_header_supplier_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = t.RequestNumber AND r.SupplierId IS NULL)),
  ('company_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = t.RequestNumber AND r.CompanyId = t.ExpectedCompanyId)),
  ('total_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = t.ExpectedGroupId AND g.TotalAmount = t.ExpectedTotal)),
  ('expected_supplier_45_exists_active_with_exact_nif',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Suppliers s
     WHERE s.Id = 45 AND s.TaxId = N'5417061590' AND s.IsActive = 1)),
  ('source_attachment_present_hash_verified',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE a.Id = t.SourceAttachmentId AND r.RequestNumber = t.RequestNumber
       AND a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND LOWER(a.FileHash) = LOWER(t.SourceFileHash))),
  ('exactly_one_active_po_attachment_with_reviewed_filename',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE r.RequestNumber = t.RequestNumber AND a.AttachmentTypeCode = 'PO'
       AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE t.PoFilenamePattern)),
  ('no_same_company_canonical_collision_for_new_po',
    (SELECT CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE g.Id <> t.ExpectedGroupId AND r.CompanyId = t.ExpectedCompanyId AND g.PurchaseOrderNumber IS NOT NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE t.CanonicalPattern))
) AS gd(CheckName, Passed)
ORDER BY t.RequestNumber, gd.CheckName;

-- ── Final state per request ──
-- PENDING_REPAIR requires the FULL pending predicate; ALREADY_REPAIRED only the
-- exact repaired state; anything else is MANUAL_REVIEW_REQUIRED.
SELECT t.RequestNumber,
  CASE
    WHEN EXISTS (
        SELECT 1
        FROM Requests r2
        JOIN RequestStatuses rs2 ON rs2.Id = r2.StatusId
        JOIN RequestPoGroups g2  ON g2.RequestId = r2.Id
        JOIN Suppliers s2        ON s2.Id = 45
        WHERE r2.RequestNumber = t.RequestNumber
          AND g2.Id = t.ExpectedGroupId
          AND (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r2.Id) = 1
          AND g2.SupplierId IS NULL
          AND g2.SupplierNameSnapshot = N'Fornecedor não definido'
          AND g2.SupplierNifSnapshot IS NULL
          AND g2.PurchaseOrderNumber = t.OldPo
          AND r2.SupplierId IS NULL
          AND rs2.Code = 'ADVANCE_PAYMENT_REQUIRED'
          AND g2.Status = 'ADVANCE_PAYMENT_REQUIRED'
          AND r2.CompanyId = t.ExpectedCompanyId
          AND g2.TotalAmount = t.ExpectedTotal
          AND s2.TaxId = N'5417061590'
          AND s2.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a2
                      WHERE a2.Id = t.SourceAttachmentId AND a2.RequestId = r2.Id
                        AND a2.AttachmentTypeCode = 'PROFORMA'
                        AND a2.IsDeleted = 0 AND a2.VoidedAtUtc IS NULL
                        AND LOWER(a2.FileHash) = LOWER(t.SourceFileHash))
          AND (SELECT COUNT(*) FROM RequestAttachments a3
               WHERE a3.RequestId = r2.Id AND a3.AttachmentTypeCode = 'PO'
                 AND a3.IsDeleted = 0 AND a3.VoidedAtUtc IS NULL
                 AND UPPER(REPLACE(REPLACE(REPLACE(a3.FileName,' ',''),'.',''),'_','')) LIKE t.PoFilenamePattern) = 1
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups gx JOIN Requests rx ON rx.Id = gx.RequestId
                          WHERE gx.Id <> t.ExpectedGroupId AND rx.CompanyId = t.ExpectedCompanyId AND gx.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(gx.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE t.CanonicalPattern))
      THEN 'PENDING_REPAIR'
    WHEN EXISTS (
        SELECT 1
        FROM Requests r3
        JOIN RequestStatuses rs3 ON rs3.Id = r3.StatusId
        JOIN RequestPoGroups g3  ON g3.RequestId = r3.Id
        JOIN Suppliers s3        ON s3.Id = 45
        WHERE r3.RequestNumber = t.RequestNumber
          AND g3.Id = t.ExpectedGroupId
          AND g3.SupplierId = 45
          AND g3.SupplierNameSnapshot = s3.Name
          AND g3.SupplierNifSnapshot = N'5417061590'
          AND g3.PurchaseOrderNumber = t.NewPo
          AND rs3.Code = 'ADVANCE_PAYMENT_REQUIRED'
          AND g3.Status = 'ADVANCE_PAYMENT_REQUIRED')
      THEN 'ALREADY_REPAIRED'
    ELSE 'MANUAL_REVIEW_REQUIRED'
  END AS RepairState
FROM @targets t
ORDER BY t.RequestNumber;
