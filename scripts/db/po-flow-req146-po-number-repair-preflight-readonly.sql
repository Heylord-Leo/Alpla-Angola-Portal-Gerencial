-- ============================================================================
-- REQ-146 PO-NUMBER CORRECTION — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the PO-only correction for EXACTLY one request:
--   REQ-23/07/2026-146 (company 1, RequestStatus PAYMENT_SCHEDULED)
--   Group ba7db94e-1c21-497a-bb19-f0dc77bb5391 (PAYMENT_SCHEDULED, total 5410.01)
--     PurchaseOrderNumber: '2026A/11' -> 'ECF10 2026A/11' (canonical ECF10-2026A-11)
--
-- Human/document review (decisive, 2026-08-20): the stored PO PDF heading reads
-- "Encomenda Mat Escritório/Diversos ECF10 2026A/11", N.º Contrib. 5417371270.
-- The stored value '2026A/11' is the same reference with the ECF10 family
-- dropped. 'FP - 63' is the N.º Doc. Externo on that document — NOT the PO
-- identity, never written. NOTE: the '2026A' year-series is legitimate Primavera
-- output (e.g. 'ECF10 2026A/13' on REQ-134) that the v2.229.12 parser grammar
-- does not yet recognize — the correction is data-only, no code involved.
--
-- Supplier is ALREADY CORRECT (14 BISMARK PAPELARIA, NIF 5417371270) and is
-- guarded but NOT modified. Payment flow is live — statuses are never touched.
--
-- SELECT/PRINT only — no writes. Run before po-flow-req146-po-number-repair.sql
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
DECLARE @requestNumber NVARCHAR(50)      = N'REQ-23/07/2026-146';
DECLARE @groupId UNIQUEIDENTIFIER        = 'ba7db94e-1c21-497a-bb19-f0dc77bb5391';
DECLARE @companyId INT                   = 1;
DECLARE @total DECIMAL(18,2)             = 5410.01;
DECLARE @supplierId INT                  = 14;
DECLARE @supplierNif NVARCHAR(50)        = N'5417371270';
DECLARE @oldPo NVARCHAR(100)             = N'2026A/11';
DECLARE @newPo NVARCHAR(100)             = N'ECF10 2026A/11';
DECLARE @poAttachmentId UNIQUEIDENTIFIER = '447f4ef6-fc70-454a-a01b-7af3dd04b15e';
DECLARE @poFileHash NVARCHAR(100)        = N'596ee7416cc0d67ea39ff5bfda073d2d00f44f7f89aa077549dad7ea5bde9e7e';

-- ── Full current state ──
SELECT r.RequestNumber, rs.Code AS RequestStatus, r.CompanyId, c.Name AS Company,
       g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot,
       ISNULL(g.PurchaseOrderNumber,  N'<NULL>') AS CurrentPo,
       g.TotalAmount, g.UpdatedAtUtc, g.UpdatedByUserId,
       s.Name AS SupplierMasterName, s.TaxId AS SupplierMasterNif,
       s.IsActive AS SupplierIsActive, s.RegistrationStatus
FROM Requests r
JOIN RequestStatuses rs ON rs.Id = r.StatusId
JOIN Companies c        ON c.Id = r.CompanyId
LEFT JOIN RequestPoGroups g ON g.RequestId = r.Id
LEFT JOIN Suppliers s   ON s.Id = @supplierId
WHERE r.RequestNumber = @requestNumber;

-- ── Reviewed PO attachment (live values vs pins) ──
SELECT a.Id AS AttachmentId, a.AttachmentTypeCode, a.FileName,
       ISNULL(a.FileHash, N'<NULL>') AS FileHash, a.RequestPoGroupId, a.UploadedAtUtc,
       u.FullName AS UploadedBy,
       CASE WHEN a.AttachmentTypeCode = 'PO' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
                 AND a.RequestPoGroupId = @groupId
                 AND LOWER(a.FileHash) = LOWER(@poFileHash)
            THEN 'Reviewed PO document present, bound to the group, hash verified'
            ELSE 'PO ATTACHMENT PROBLEM — do not repair' END AS PoAttachmentStatus
FROM RequestAttachments a
JOIN Requests r ON r.Id = a.RequestId
LEFT JOIN Users u ON u.Id = a.UploadedByUserId
WHERE r.RequestNumber = @requestNumber AND a.Id = @poAttachmentId;

-- ── Canonical collision scan for the 2026A/11 family (all companies, excluding this group) ──
-- Deliberately broad (any '...2026A/11' canonical): over-matching aborts, never repairs.
SELECT r.RequestNumber, r.CompanyId, g.Id AS GroupId, g.PurchaseOrderNumber
FROM RequestPoGroups g
JOIN Requests r ON r.Id = g.RequestId
WHERE g.Id <> @groupId
  AND g.PurchaseOrderNumber IS NOT NULL
  AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%2026A#11';

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
  ('request_status_is_PAYMENT_SCHEDULED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r JOIN RequestStatuses rs ON rs.Id = r.StatusId
     WHERE r.RequestNumber = @requestNumber AND rs.Code = 'PAYMENT_SCHEDULED')),
  ('group_status_is_PAYMENT_SCHEDULED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.Status = 'PAYMENT_SCHEDULED')),
  ('group_supplier_is_correct_14_with_exact_snapshots',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Suppliers s ON s.Id = @supplierId
     WHERE g.Id = @groupId AND g.SupplierId = @supplierId
       AND g.SupplierNameSnapshot = s.Name AND g.SupplierNifSnapshot = @supplierNif)),
  ('current_po_is_exactly_reviewed_old_value',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.PurchaseOrderNumber = @oldPo)),
  ('company_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r
     WHERE r.RequestNumber = @requestNumber AND r.CompanyId = @companyId)),
  ('total_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.TotalAmount = @total)),
  ('supplier_master_14_exists_active_with_exact_nif',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Suppliers s
     WHERE s.Id = @supplierId AND s.TaxId = @supplierNif AND s.IsActive = 1)),
  ('po_attachment_present_bound_hash_verified',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE a.Id = @poAttachmentId AND r.RequestNumber = @requestNumber
       AND a.AttachmentTypeCode = 'PO' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND a.RequestPoGroupId = @groupId
       AND LOWER(a.FileHash) = LOWER(@poFileHash))),
  ('no_canonical_collision_2026A_11_family',
    (SELECT CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id <> @groupId AND g.PurchaseOrderNumber IS NOT NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%2026A#11'))
) AS gd(CheckName, Passed);

-- ── Final state ──
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
          AND g2.PurchaseOrderNumber = @oldPo
          AND g2.SupplierId = @supplierId
          AND g2.SupplierNameSnapshot = s2.Name
          AND g2.SupplierNifSnapshot = @supplierNif
          AND rs2.Code = 'PAYMENT_SCHEDULED'
          AND g2.Status = 'PAYMENT_SCHEDULED'
          AND r2.CompanyId = @companyId
          AND g2.TotalAmount = @total
          AND s2.TaxId = @supplierNif AND s2.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a2
                      WHERE a2.Id = @poAttachmentId AND a2.RequestId = r2.Id
                        AND a2.AttachmentTypeCode = 'PO'
                        AND a2.IsDeleted = 0 AND a2.VoidedAtUtc IS NULL
                        AND a2.RequestPoGroupId = @groupId
                        AND LOWER(a2.FileHash) = LOWER(@poFileHash))
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups gx
                          WHERE gx.Id <> @groupId AND gx.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(gx.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%2026A#11'))
      THEN 'PENDING_REPAIR'
    WHEN EXISTS (
        SELECT 1
        FROM Requests r3
        JOIN RequestStatuses rs3 ON rs3.Id = r3.StatusId
        JOIN RequestPoGroups g3  ON g3.RequestId = r3.Id
        JOIN Suppliers s3        ON s3.Id = @supplierId
        WHERE r3.RequestNumber = @requestNumber
          AND g3.Id = @groupId
          AND g3.PurchaseOrderNumber = @newPo
          AND g3.SupplierId = @supplierId
          AND g3.SupplierNameSnapshot = s3.Name
          AND g3.SupplierNifSnapshot = @supplierNif
          AND rs3.Code = 'PAYMENT_SCHEDULED'
          AND g3.Status = 'PAYMENT_SCHEDULED')
      THEN 'ALREADY_REPAIRED'
    ELSE 'MANUAL_REVIEW_REQUIRED'
  END AS RepairState;
