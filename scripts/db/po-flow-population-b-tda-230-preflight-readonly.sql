-- ============================================================================
-- POPULATION-B SUPPLIER REPAIR (TDA, REQ-230 ONLY) — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the controlled Population-B supplier repair for EXACTLY ONE request
-- whose issuer was CONFIRMED BY HUMAN REVIEW (2026-08-20) as TDA:
--   REQ-11/08/2026-230 (company 2, AlplaSOPRO) -> SupplierId 34
-- REQ-233 was EXCLUDED from this repair: its live workflow advanced to PO_ISSUED with
-- PO 'FT 00459' (under pre-v2.229.12 PROD code) and is under separate manual review.
-- The malformed OCR NIF (541002857) is NOT used as supplier data — snapshots come
-- from the supplier master record (TaxId 5410002857) only.
-- SELECT/PRINT only — no writes. Run before po-flow-population-b-tda-230-repair.sql
-- and STOP unless every row is PASS and the final state is PENDING_REPAIR
-- (or ALREADY_REPAIRED).
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

-- ── Allow-list: the ONLY target, with every reviewed expectation pinned ──
DECLARE @targets TABLE (
    RequestNumber NVARCHAR(50) PRIMARY KEY,
    ExpectedGroupId UNIQUEIDENTIFIER,
    ExpectedCompanyId INT,
    ExpectedSupplierId INT,
    ExpectedSupplierNif NVARCHAR(50),
    EvidenceAttachmentId UNIQUEIDENTIFIER,
    EvidenceFileHash NVARCHAR(100)
);
INSERT INTO @targets VALUES
 (N'REQ-11/08/2026-230', 'f28ec394-3553-43ff-b492-0ae6524d238f', 2, 34, N'5410002857', '9c132114-b612-40ac-bcbf-ab9cdf2b7452', N'44ad6c2ac207d33eb918a160ffa83f3b6880b7ab71d271352c1e3b791c3f0cf9');

-- ── Full current state of each target ──
SELECT t.RequestNumber, rs.Code AS RequestStatus, g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot,
       t.ExpectedSupplierId, s.Name AS ExpectedSupplierName, t.ExpectedSupplierNif,
       s.IsActive AS SupplierIsActive, s.RegistrationStatus,
       r.CompanyId, c.Name AS Company, g.TotalAmount, g.PurchaseOrderNumber,
       t.EvidenceAttachmentId, a.FileName AS EvidenceFile,
       ISNULL(a.FileHash, N'<NULL>') AS EvidenceFileHash,
       CASE WHEN a.Id IS NOT NULL AND LOWER(a.FileHash) = LOWER(t.EvidenceFileHash)
            THEN 'evidence document present, hash verified, issuer HUMAN-CONFIRMED = TDA'
            ELSE 'EVIDENCE PROBLEM — do not repair' END AS EvidenceStatus
FROM @targets t
LEFT JOIN Requests r         ON r.RequestNumber = t.RequestNumber
LEFT JOIN RequestStatuses rs ON rs.Id = r.StatusId
LEFT JOIN Companies c        ON c.Id = r.CompanyId
LEFT JOIN RequestPoGroups g  ON g.RequestId = r.Id
LEFT JOIN Suppliers s        ON s.Id = t.ExpectedSupplierId
LEFT JOIN RequestAttachments a ON a.Id = t.EvidenceAttachmentId AND a.RequestId = r.Id
                               AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
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
  ('statuses_match_review_APPROVED_WAITING_PO',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     JOIN RequestStatuses rs ON rs.Id = r.StatusId
     WHERE g.Id = t.ExpectedGroupId AND g.Status = 'WAITING_PO' AND rs.Code = 'APPROVED')),
  ('purchase_order_number_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = t.ExpectedGroupId AND g.PurchaseOrderNumber IS NULL)),
  ('company_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = t.RequestNumber AND r.CompanyId = t.ExpectedCompanyId)),
  ('supplier_34_exists_active_with_exact_master_nif',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Suppliers s
     WHERE s.Id = t.ExpectedSupplierId AND s.TaxId = t.ExpectedSupplierNif AND s.IsActive = 1)),
  ('evidence_attachment_present_hash_verified',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE a.Id = t.EvidenceAttachmentId AND r.RequestNumber = t.RequestNumber
       AND a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND LOWER(a.FileHash) = LOWER(t.EvidenceFileHash)))
) AS gd(CheckName, Passed)
ORDER BY t.RequestNumber, gd.CheckName;

-- ── Final state per request ──
-- PENDING_REPAIR requires the FULL pending predicate (every guard the repair enforces),
-- not merely a NULL supplier: a row whose workflow advanced (e.g. PO registered) must
-- classify MANUAL_REVIEW_REQUIRED even though its supplier fields still look legacy.
SELECT t.RequestNumber,
       CASE
         WHEN EXISTS (
            SELECT 1
            FROM Requests r2
            JOIN RequestStatuses rs2 ON rs2.Id = r2.StatusId
            JOIN RequestPoGroups g2  ON g2.RequestId = r2.Id
            JOIN Suppliers s2        ON s2.Id = t.ExpectedSupplierId
            WHERE r2.RequestNumber = t.RequestNumber
              AND g2.Id = t.ExpectedGroupId
              AND (SELECT COUNT(*) FROM RequestPoGroups gg WHERE gg.RequestId = r2.Id) = 1
              AND g2.SupplierId IS NULL
              AND g2.SupplierNameSnapshot = N'Fornecedor não definido'
              AND g2.SupplierNifSnapshot IS NULL
              AND r2.SupplierId IS NULL
              AND rs2.Code = 'APPROVED'
              AND g2.Status = 'WAITING_PO'
              AND g2.PurchaseOrderNumber IS NULL
              AND r2.CompanyId = t.ExpectedCompanyId
              AND s2.TaxId = t.ExpectedSupplierNif
              AND s2.IsActive = 1
              AND EXISTS (SELECT 1 FROM RequestAttachments a2
                          WHERE a2.Id = t.EvidenceAttachmentId AND a2.RequestId = r2.Id
                            AND a2.AttachmentTypeCode = 'PROFORMA'
                            AND a2.IsDeleted = 0 AND a2.VoidedAtUtc IS NULL
                            AND LOWER(a2.FileHash) = LOWER(t.EvidenceFileHash)))
           THEN 'PENDING_REPAIR'
         WHEN g.SupplierId = t.ExpectedSupplierId AND g.SupplierNifSnapshot = t.ExpectedSupplierNif
              AND g.SupplierNameSnapshot = s.Name
           THEN 'ALREADY_REPAIRED'
         ELSE 'MANUAL_REVIEW_REQUIRED'
       END AS RepairState,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot
FROM @targets t
LEFT JOIN Requests r        ON r.RequestNumber = t.RequestNumber
LEFT JOIN RequestPoGroups g ON g.Id = t.ExpectedGroupId AND g.RequestId = r.Id
LEFT JOIN Suppliers s       ON s.Id = t.ExpectedSupplierId
ORDER BY t.RequestNumber;
