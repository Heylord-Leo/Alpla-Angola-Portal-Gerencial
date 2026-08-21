-- ============================================================================
-- POPULATION-B SUPPLIER REPAIR — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the controlled Population-B supplier repair for EXACTLY six requests
-- (NIF_EXACT stored-document evidence, reviewed 2026-08-20):
--   TDA (SupplierId 34, NIF 5410002857):     REQ-03/08/2026-208, REQ-05/08/2026-215, REQ-06/08/2026-222
--   Embrace (SupplierId 257, NIF 5417101524): REQ-12/08/2026-237, REQ-12/08/2026-238, REQ-12/08/2026-241
-- SELECT/PRINT only — no writes of any kind. Run before
-- po-flow-population-b-supplier-repair.sql and STOP unless every row is PASS and the
-- final state is PENDING_REPAIR (or ALREADY_REPAIRED across all six).
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

-- ── Allow-list: the ONLY six targets, with every reviewed expectation pinned ──
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
 (N'REQ-03/08/2026-208', '2bbeb116-7742-4b2d-aece-5c339bc418d8', 2, 34,  N'5410002857', '9eea40d5-fbb1-4bcd-8052-5ccd89b2359d', N'202525d267b9e29224cb0fa4cf62c5ee77e43fe394fbe9c746742d0af553a548'),
 (N'REQ-05/08/2026-215', '3d3c7558-7987-44d2-ad2e-5c7c5d70f16f', 2, 34,  N'5410002857', '9b05afc9-7568-4b54-83d8-e93c8d559128', N'd20d0e54cbdd4ff0ca00afc40119f2b22f9633abbb247f55c834f12ff98fd607'),
 (N'REQ-06/08/2026-222', 'db7e000c-79d4-41fb-b370-55b8d8c883b2', 1, 34,  N'5410002857', 'eecff47d-d519-4f77-80ba-a579870a7d08', N'7cac8d5073007f138efdd776156cc2b5129ffa72492d2f31ed576c56d5b92fe7'),
 (N'REQ-12/08/2026-237', '63a27af3-ff62-421f-966e-e212188a1bce', 1, 257, N'5417101524', '5bcffcef-f312-4668-8b79-33b8aef8f409', N'0ea0ea5d2e921a9eb1232af0b3586cb082dfc2b5efa6b6ba67ceb8475eb219f8'),
 (N'REQ-12/08/2026-238', 'ecf53b7a-16ce-4844-92ba-781cd22df721', 1, 257, N'5417101524', '0732c086-93ad-4bb8-8aba-decdbe7949da', N'aea16d770608c2e319e38e7a6e178406566679dcc6344a3bf3970fa46fc41f12'),
 (N'REQ-12/08/2026-241', '72e38cf2-f445-4462-9435-b86d59a195ea', 1, 257, N'5417101524', 'cfd7fa5c-15ca-4f27-9f7d-1b1e1916f073', N'd9c20b5ff7f52bf1fd703668526dce49a35febef29df3175fe19aa5bef20926c');

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
            THEN 'NIF_EXACT evidence document present, hash verified'
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
  ('expected_supplier_exists_active_with_exact_nif',
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
