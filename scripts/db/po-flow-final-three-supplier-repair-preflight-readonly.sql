-- ============================================================================
-- FINAL THREE HISTORICAL SUPPLIER REPAIR — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the controlled supplier repair for EXACTLY three requests whose
-- supplier identities were CONFIRMED BY HUMAN REVIEW (2026-08-20) and which
-- passed the latest LIVE PROD preflight as PENDING_REPAIR:
--   REQ-16/07/2026-084 -> 53  REALVITUR ANGOLA, LIMITADA            (NIF 5417089079, company 1)
--   REQ-29/07/2026-178 -> 66  IMPORAFRICA VEICULOS LDA              (NIF 5417231983, company 1)
--   REQ-12/08/2026-245 -> 159 MUSOLAND-MUNDO DAS SOLUCOES-ACESS.CONS.(SU),LDA (NIF 5417386740, company 1)
--
-- REQ-31/07/2026-193 and REQ-31/07/2026-194 were REMOVED from this package:
-- they drifted in live PROD to ADVANCE_PAYMENT_REQUIRED with registered PO
-- values and are handled by the dedicated po-flow-req193-194-supplier-po-*
-- package. REQ-31/07/2026-200 (PO_ISSUED drift) is handled by
-- po-flow-req200-supplier-po-*. This script supersedes the retired
-- po-flow-final-five-supplier-repair-* trio.
--
-- Evidence basis: HUMAN CONFIRMATION of each request's stored source document.
-- The pinned PROFORMA attachment id/hash below is a DRIFT-DETECTION ANCHOR only
-- (proves the reviewed document is still the active source document); it is NOT
-- the supplier-identity evidence and filenames were never used as evidence.
--
-- SELECT/PRINT only — no writes of any kind. Run before
-- po-flow-final-three-supplier-repair.sql and STOP unless every row is PASS and
-- the final state is PENDING_REPAIR (or ALREADY_REPAIRED across all three).
--
-- OPERATIONAL WARNING (does NOT block this historical repair): supplier 159
-- (MUSOLAND) is RegistrationStatus = DRAFT. register-po refuses DRAFT suppliers,
-- so REGISTER_PO on that group may remain blocked until the master registration
-- is completed. Reported below.
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

-- ── Allow-list: the ONLY three targets, with every reviewed expectation pinned ──
DECLARE @targets TABLE (
    RequestNumber NVARCHAR(50) PRIMARY KEY,
    ExpectedGroupId UNIQUEIDENTIFIER,
    ExpectedCompanyId INT,
    ExpectedSupplierId INT,
    ExpectedSupplierNif NVARCHAR(50),
    ExpectedTotal DECIMAL(18,2),
    AnchorAttachmentId UNIQUEIDENTIFIER,
    AnchorFileHash NVARCHAR(100)
);
INSERT INTO @targets VALUES
 (N'REQ-16/07/2026-084', '886c0d0e-80d8-4ebe-8272-e4fa3304f5c3', 1, 53,  N'5417089079',  971392.00, '37fa585d-674f-47b1-b621-248dd845f5b0', N'78f0d3a0d0e26f421fadb0602566dea03affa43a05102c39fe4765da96791746'),
 (N'REQ-29/07/2026-178', '3d67213e-daba-4615-a0fc-108b19ea1a3e', 1, 66,  N'5417231983',  164167.67, '4831f40f-73d4-41a7-99a8-74c9492acf54', N'18c4299ed825509ff0c4f1a52ff6b498f3f90409bf50e2041ccaf0bc2a8c18a9'),
 (N'REQ-12/08/2026-245', 'fe684497-448f-471a-8461-377ba3dc47c5', 1, 159, N'5417386740',  239400.00, '6f5f7e9c-8899-45ba-93de-63fa47b922bf', N'f55d286e8342b4264cb298add5811f76ecd44443901e3fa3af3b949fac74e02e');

-- ── Full current state of each target ──
SELECT t.RequestNumber, rs.Code AS RequestStatus, g.Id AS GroupId, g.Status AS GroupStatus,
       g.SupplierId AS CurrentSupplierId,
       ISNULL(g.SupplierNameSnapshot, N'<NULL>') AS CurrentNameSnapshot,
       ISNULL(g.SupplierNifSnapshot,  N'<NULL>') AS CurrentNifSnapshot,
       t.ExpectedSupplierId, s.Name AS ExpectedSupplierName, t.ExpectedSupplierNif,
       s.IsActive AS SupplierIsActive, s.RegistrationStatus,
       r.CompanyId, c.Name AS Company, g.TotalAmount, t.ExpectedTotal, g.PurchaseOrderNumber,
       t.AnchorAttachmentId, a.FileName AS AnchorFile,
       ISNULL(a.FileHash, N'<NULL>') AS AnchorFileHash,
       CASE WHEN a.Id IS NOT NULL AND LOWER(a.FileHash) = LOWER(t.AnchorFileHash)
            THEN 'Reviewed source document still present, hash verified'
            ELSE 'ANCHOR PROBLEM — reviewed document drifted, do not repair' END AS AnchorStatus
FROM @targets t
LEFT JOIN Requests r         ON r.RequestNumber = t.RequestNumber
LEFT JOIN RequestStatuses rs ON rs.Id = r.StatusId
LEFT JOIN Companies c        ON c.Id = r.CompanyId
LEFT JOIN RequestPoGroups g  ON g.RequestId = r.Id
LEFT JOIN Suppliers s        ON s.Id = t.ExpectedSupplierId
LEFT JOIN RequestAttachments a ON a.Id = t.AnchorAttachmentId AND a.RequestId = r.Id
                               AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
ORDER BY t.RequestNumber;

-- ── Operational warning: DRAFT suppliers (report-only, NOT a blocking guard) ──
SELECT s.Id AS SupplierId, s.Name, s.TaxId, s.RegistrationStatus,
       'WARNING: REGISTER_PO may remain blocked for this supplier until its registration is completed — the historical data repair itself is NOT blocked' AS OperationalWarning
FROM Suppliers s
WHERE s.Id IN (SELECT ExpectedSupplierId FROM @targets)
  AND s.RegistrationStatus <> 'ACTIVE'
ORDER BY s.Id;

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
  ('total_matches_review',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g
     WHERE g.Id = t.ExpectedGroupId AND g.TotalAmount = t.ExpectedTotal)),
  ('expected_supplier_exists_active_with_exact_nif',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM Suppliers s
     WHERE s.Id = t.ExpectedSupplierId AND s.TaxId = t.ExpectedSupplierNif AND s.IsActive = 1)),
  ('anchor_attachment_present_hash_verified',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestAttachments a JOIN Requests r ON r.Id = a.RequestId
     WHERE a.Id = t.AnchorAttachmentId AND r.RequestNumber = t.RequestNumber
       AND a.AttachmentTypeCode = 'PROFORMA' AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
       AND LOWER(a.FileHash) = LOWER(t.AnchorFileHash)))
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
              AND g2.TotalAmount = t.ExpectedTotal
              AND s2.TaxId = t.ExpectedSupplierNif
              AND s2.IsActive = 1
              AND EXISTS (SELECT 1 FROM RequestAttachments a2
                          WHERE a2.Id = t.AnchorAttachmentId AND a2.RequestId = r2.Id
                            AND a2.AttachmentTypeCode = 'PROFORMA'
                            AND a2.IsDeleted = 0 AND a2.VoidedAtUtc IS NULL
                            AND LOWER(a2.FileHash) = LOWER(t.AnchorFileHash)))
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
