-- ============================================================================
-- REQ-233 SUPPLIER + PO-NUMBER INTEGRITY REPAIR — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the dedicated historical repair for EXACTLY one request:
--   REQ-11/08/2026-233 (company 1, AlplaPLASTICO) group 091cffd1-921b-4cf6-b6d3-843500820538
--
-- Human-reviewed documentary evidence (2026-08-20, actual PROD PO PDF):
--   - PO heading: "PO Serviços ECF11 2026/423"  -> canonical ECF11-2026-423
--   - document N.º Contrib. = 5410002857 = TDA master NIF (SupplierId 34)
--   - document N.º Doc. Externo = "FT 00459" — the value wrongly stored as the PO number.
--     "FT 00459" is VALID source information but is the external-document number,
--     NOT the PO identity.
--
-- This preflight PRINTS the live PO attachment Id and FileHash: the repair script
-- requires that hash to be passed back via  -v poHash="<sha256>"  as an explicit
-- operator pin. SELECT/PRINT only — no writes.
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

DECLARE @groupId UNIQUEIDENTIFIER = '091cffd1-921b-4cf6-b6d3-843500820538';
DECLARE @proformaId UNIQUEIDENTIFIER = '97d0aeb4-6352-4715-9d8f-a5d5759c08e3';
DECLARE @proformaHash NVARCHAR(100) = N'b4bc9eb55fdd0900da895a5cedc96b6bd95245a11b644dae34cc50fafbae005d';

-- ── Full current state ──
SELECT r.RequestNumber, r.Id AS RequestId, rs.Code AS RequestStatus, r.SupplierId AS HeaderSupplierId,
       g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot,
       g.PurchaseOrderNumber AS CurrentPoNumber,
       g.TotalAmount, r.CompanyId, c.Name AS Company,
       s.Id AS TargetSupplierId, s.Name AS TargetSupplierName, s.TaxId AS TargetSupplierNif,
       s.IsActive AS SupplierIsActive
FROM Requests r
JOIN RequestStatuses rs ON rs.Id = r.StatusId
JOIN Companies c        ON c.Id = r.CompanyId
JOIN RequestPoGroups g  ON g.RequestId = r.Id
CROSS JOIN Suppliers s
WHERE r.RequestNumber = 'REQ-11/08/2026-233' AND s.Id = 34;

-- ── Evidence attachments: original TDA proforma + live PO attachment (id/hash to pin) ──
SELECT a.AttachmentTypeCode, a.Id AS AttachmentId, a.FileName,
       ISNULL(a.FileHash, N'<NULL>') AS FileHash, a.UploadedAtUtc, u.FullName AS UploadedBy,
       CASE WHEN a.Id = @proformaId AND LOWER(a.FileHash) = @proformaHash
              THEN 'proforma evidence, hash verified'
            WHEN a.AttachmentTypeCode = 'PO'
              THEN '>>> PIN THIS: pass FileHash to the repair via -v poHash="..." <<<'
            ELSE '' END AS Note
FROM RequestAttachments a
JOIN Requests r ON r.Id = a.RequestId
LEFT JOIN Users u ON u.Id = a.UploadedByUserId
WHERE r.RequestNumber = 'REQ-11/08/2026-233' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
ORDER BY a.UploadedAtUtc;

-- ── Guard evaluation: PASS/FAIL per named guard ──
SELECT gd.CheckName, CASE WHEN gd.Passed = 1 THEN 'PASS' ELSE 'FAIL' END AS CheckResult
FROM (VALUES
  ('database_is_allowed',
    CASE WHEN DB_NAME() IN ('Portal-Gerencial-Test','Portal-Gerencial') THEN 1 ELSE 0 END),
  ('request_exists_and_unique',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r WHERE r.RequestNumber = 'REQ-11/08/2026-233')),
  ('exactly_one_group_and_is_reviewed_group',
    (SELECT CASE WHEN COUNT(*) = 1 AND MIN(CONVERT(NVARCHAR(36), g.Id)) = '091cffd1-921b-4cf6-b6d3-843500820538' THEN 1 ELSE 0 END
     FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId WHERE r.RequestNumber = 'REQ-11/08/2026-233')),
  ('group_supplier_is_null_with_legacy_snapshots',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.SupplierId IS NULL
       AND g.SupplierNameSnapshot = N'Fornecedor não definido' AND g.SupplierNifSnapshot IS NULL)),
  ('request_header_supplier_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = 'REQ-11/08/2026-233' AND r.SupplierId IS NULL)),
  ('workflow_is_reviewed_PO_ISSUED_both_levels',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     JOIN RequestStatuses rs ON rs.Id = r.StatusId
     WHERE g.Id = @groupId AND g.Status = 'PO_ISSUED' AND rs.Code = 'PO_ISSUED')),
  ('current_po_number_is_exactly_FT_00459',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.PurchaseOrderNumber = N'FT 00459')),
  ('company_is_alpla_plastico_1',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = 'REQ-11/08/2026-233' AND r.CompanyId = 1)),
  ('total_is_reviewed_529833_40',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.TotalAmount = 529833.40)),
  ('supplier_34_active_with_exact_master_nif',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Suppliers s
     WHERE s.Id = 34 AND s.TaxId = N'5410002857' AND s.IsActive = 1)),
  ('proforma_evidence_present_hash_verified',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE a.Id = @proformaId AND r.RequestNumber = 'REQ-11/08/2026-233'
       AND a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND LOWER(a.FileHash) = @proformaHash)),
  ('exactly_one_active_PO_attachment_with_ecf11_2026_423_name',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE r.RequestNumber = 'REQ-11/08/2026-233' AND a.AttachmentTypeCode = 'PO'
       AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE '%ECF11%2026423%'
       AND a.FileHash IS NOT NULL)),
  ('no_canonical_collision_ECF11_2026_423_company1',
    (SELECT CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE r.CompanyId = 1 AND g.Id <> @groupId AND g.PurchaseOrderNumber IS NOT NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#423'))
) AS gd(CheckName, Passed);

-- ── Final state classification (hardened e0b178f rules) ──
SELECT
  CASE
    WHEN EXISTS (
        SELECT 1 FROM Requests r
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
          AND rs.Code = 'PO_ISSUED' AND g.Status = 'PO_ISSUED'
          AND g.PurchaseOrderNumber = N'FT 00459'
          AND r.CompanyId = 1
          AND g.TotalAmount = 529833.40
          AND s.TaxId = N'5410002857' AND s.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a WHERE a.Id = @proformaId AND a.RequestId = r.Id
                      AND a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                      AND LOWER(a.FileHash) = @proformaHash)
          AND (SELECT COUNT(*) FROM RequestAttachments a WHERE a.RequestId = r.Id AND a.AttachmentTypeCode = 'PO'
               AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL AND a.FileHash IS NOT NULL
               AND UPPER(REPLACE(REPLACE(REPLACE(a.FileName,' ',''),'.',''),'_','')) LIKE '%ECF11%2026423%') = 1
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g2 JOIN Requests r2 ON r2.Id = g2.RequestId
                          WHERE r2.CompanyId = 1 AND g2.Id <> @groupId AND g2.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g2.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#423'))
      THEN 'PENDING_REPAIR'
    WHEN EXISTS (
        SELECT 1 FROM Requests r
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN RequestPoGroups g  ON g.RequestId = r.Id
        JOIN Suppliers s        ON s.Id = 34
        WHERE r.RequestNumber = 'REQ-11/08/2026-233'
          AND g.Id = @groupId
          AND g.SupplierId = 34
          AND g.SupplierNameSnapshot = s.Name
          AND g.SupplierNifSnapshot = N'5410002857'
          AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#423'
          AND rs.Code = 'PO_ISSUED' AND g.Status = 'PO_ISSUED'
          AND r.CompanyId = 1 AND g.TotalAmount = 529833.40)
      THEN 'ALREADY_REPAIRED'
    ELSE 'MANUAL_REVIEW_REQUIRED'
  END AS RepairState;
