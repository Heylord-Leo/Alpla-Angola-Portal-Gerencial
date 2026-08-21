-- ============================================================================
-- REQ-071 SUPPLIER + PO BACKFILL — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the dedicated, state-aware repair for EXACTLY one request:
--   REQ-15/07/2026-071 (company 1, RequestStatus ADVANCE_PAYMENT_REQUIRED)
--   Group 2842545e-f766-4c9b-abc8-5a8fc75ac42f (Status PENDING, total 276119.00)
--
-- Human/document review (decisive, 2026-08-20):
--   * Supplier CONFIRMED BY HUMAN REVIEW: Embrace Angola - Prestação de Serviços,
--     LDA — SupplierId 257, NIF 5417101524.
--   * PO document visually reviewed: heading "PO Serviços ECF11 2026/371",
--     N.º Contrib. 5417101524. Display value: 'ECF11 2026/371'
--     (canonical ECF11-2026-371).
--   * 'FT FC202602/2101254' is the N.º Doc. Externo on that PO — it is NOT the
--     PO number and is never written anywhere.
--
-- Historical context: the PO was registered on 2026-07-16 (REGISTER_PO by the
-- buyer, APPROVED -> ADVANCE_PAYMENT_REQUIRED) BEFORE the current RequestPoGroup
-- row existed (group created 2026-07-20 by a later-model backfill), so the group
-- carries neither supplier nor PO. The group-status PENDING mismatch versus the
-- real lifecycle is a SEPARATE reconciliation issue and is NOT changed here.
--
-- SELECT/PRINT only — no writes. Run before po-flow-req071-supplier-po-repair.sql
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
DECLARE @requestNumber NVARCHAR(50)     = N'REQ-15/07/2026-071';
DECLARE @groupId UNIQUEIDENTIFIER       = '2842545e-f766-4c9b-abc8-5a8fc75ac42f';
DECLARE @companyId INT                  = 1;
DECLARE @total DECIMAL(18,2)            = 276119.00;
DECLARE @supplierId INT                 = 257;
DECLARE @supplierNif NVARCHAR(50)       = N'5417101524';
DECLARE @newPo NVARCHAR(100)            = N'ECF11 2026/371';
DECLARE @poAttachmentId UNIQUEIDENTIFIER = 'b7b91151-713c-498f-bb4d-a7eff2ef510a';
DECLARE @poFileHash NVARCHAR(100)       = N'ac9e9cfc4b040c9b1bbded396e5e7103bb630b4d8351dbb9b76e4bb3305f45d8';

-- ── Full current state ──
SELECT r.RequestNumber, rs.Code AS RequestStatus, r.CompanyId, c.Name AS Company,
       r.SupplierId AS HeaderSupplierId,
       g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot,
       ISNULL(g.PurchaseOrderNumber,  N'<NULL>') AS CurrentPo,
       g.TotalAmount, g.CreatedAtUtc AS GroupCreatedAtUtc,
       s.Id AS ExpectedSupplierId, s.Name AS ExpectedSupplierName, s.TaxId AS ExpectedSupplierNif,
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

-- ── Historical REGISTER_PO trace (context display only) ──
SELECT h.ActionTaken, h.Comment, h.CreatedAtUtc, u.FullName AS Actor
FROM RequestStatusHistories h
JOIN Requests r ON r.Id = h.RequestId
LEFT JOIN Users u ON u.Id = h.ActorUserId
WHERE r.RequestNumber = @requestNumber
  AND h.ActionTaken IN ('REGISTER_PO', 'DATA_INTEGRITY_REPAIR')
ORDER BY h.CreatedAtUtc;

-- ── Canonical collision scan for ECF11-2026-371 (same company, excluding this group) ──
SELECT r.RequestNumber, r.CompanyId, g.Id AS GroupId, g.PurchaseOrderNumber
FROM RequestPoGroups g
JOIN Requests r ON r.Id = g.RequestId
WHERE g.Id <> @groupId
  AND r.CompanyId = @companyId
  AND g.PurchaseOrderNumber IS NOT NULL
  AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#371';

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
  ('request_status_is_ADVANCE_PAYMENT_REQUIRED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Requests r JOIN RequestStatuses rs ON rs.Id = r.StatusId
     WHERE r.RequestNumber = @requestNumber AND rs.Code = 'ADVANCE_PAYMENT_REQUIRED')),
  ('group_status_is_PENDING',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.Status = 'PENDING')),
  ('group_supplier_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.SupplierId IS NULL)),
  ('snapshots_are_reviewed_all_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.SupplierNameSnapshot IS NULL AND g.SupplierNifSnapshot IS NULL)),
  ('purchase_order_number_is_null',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = @groupId AND g.PurchaseOrderNumber IS NULL)),
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
  ('po_attachment_present_bound_hash_verified',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE a.Id = @poAttachmentId AND r.RequestNumber = @requestNumber
       AND a.AttachmentTypeCode = 'PO' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND a.RequestPoGroupId = @groupId
       AND LOWER(a.FileHash) = LOWER(@poFileHash))),
  ('no_same_company_canonical_collision_ECF11_2026_371',
    (SELECT CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id = g.RequestId
     WHERE g.Id <> @groupId AND r.CompanyId = @companyId AND g.PurchaseOrderNumber IS NOT NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#371'))
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
          AND g2.SupplierNameSnapshot IS NULL
          AND g2.SupplierNifSnapshot IS NULL
          AND g2.PurchaseOrderNumber IS NULL
          AND r2.SupplierId IS NULL
          AND rs2.Code = 'ADVANCE_PAYMENT_REQUIRED'
          AND g2.Status = 'PENDING'
          AND r2.CompanyId = @companyId
          AND g2.TotalAmount = @total
          AND s2.TaxId = @supplierNif
          AND s2.IsActive = 1
          AND EXISTS (SELECT 1 FROM RequestAttachments a2
                      WHERE a2.Id = @poAttachmentId AND a2.RequestId = r2.Id
                        AND a2.AttachmentTypeCode = 'PO'
                        AND a2.IsDeleted = 0 AND a2.VoidedAtUtc IS NULL
                        AND a2.RequestPoGroupId = @groupId
                        AND LOWER(a2.FileHash) = LOWER(@poFileHash))
          AND NOT EXISTS (SELECT 1 FROM RequestPoGroups gx JOIN Requests rx ON rx.Id = gx.RequestId
                          WHERE gx.Id <> @groupId AND rx.CompanyId = @companyId AND gx.PurchaseOrderNumber IS NOT NULL
                            AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(gx.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#371'))
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
          AND rs3.Code = 'ADVANCE_PAYMENT_REQUIRED'
          AND g3.Status = 'PENDING')
      THEN 'ALREADY_REPAIRED'
    ELSE 'MANUAL_REVIEW_REQUIRED'
  END AS RepairState;
