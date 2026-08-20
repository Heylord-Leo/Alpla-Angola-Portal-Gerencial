-- ============================================================================
-- PO-NUMBER REPAIR — PREFLIGHT (READ-ONLY)
-- ============================================================================
-- Stage 1 of the controlled historical P.O-number repair for EXACTLY:
--   REQ-20/07/2026-098   5002736705 -> ECF10 2026/230   (canonical ECF10-2026-230)
--   REQ-20/07/2026-101   5001713205 -> ECF11 2026/386   (canonical ECF11-2026-386)
-- SELECT/PRINT only — no writes of any kind. Run before po-flow-po-number-repair.sql
-- and STOP unless every CheckResult prints PASS (or ALREADY_REPAIRED).
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

-- ── Context (must show the intended server, database, login and TEST/PROD label) ──
SELECT @@SERVERNAME AS ServerIdentity, DB_NAME() AS DatabaseName, ORIGINAL_LOGIN() AS OriginalLogin,
       CASE DB_NAME() WHEN 'Portal-Gerencial' THEN 'PROD'
                      WHEN 'Portal-Gerencial-Test' THEN 'TEST'
                      ELSE 'DISALLOWED' END AS ExecutionContext;

-- ── Target rows as they exist right now ──
SELECT r.RequestNumber, r.Id AS RequestId, rs.Code AS RequestStatus, g.Id AS GroupId,
       g.Status AS GroupStatus, g.PurchaseOrderNumber, g.SupplierId, s.Name AS SupplierName,
       s.TaxId AS SupplierNif, r.CompanyId, c.Name AS Company, g.TotalAmount
FROM RequestPoGroups g
JOIN Requests r         ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
LEFT JOIN Suppliers s   ON s.Id = g.SupplierId
LEFT JOIN Companies c   ON c.Id = r.CompanyId
WHERE r.RequestNumber IN ('REQ-20/07/2026-098', 'REQ-20/07/2026-101');

-- ── Guard evaluation (same predicates the repair script enforces) ──
SELECT 'REQ-098' AS Target, CheckName,
       CASE WHEN Passed = 1 THEN 'PASS' ELSE 'FAIL' END AS CheckResult
FROM (VALUES
  ('database_is_allowed',
    CASE WHEN DB_NAME() IN ('Portal-Gerencial-Test','Portal-Gerencial') THEN 1 ELSE 0 END),
  ('exactly_one_group_with_old_value',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE r.RequestNumber='REQ-20/07/2026-098' AND g.PurchaseOrderNumber='5002736705')),
  ('resolved_group_is_reviewed_group',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE r.RequestNumber='REQ-20/07/2026-098' AND g.PurchaseOrderNumber='5002736705'
       AND g.Id='f559b59c-867c-4fa8-a339-cece55e5cd7f')),
  ('supplier_is_vm_santos_261',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Suppliers s ON s.Id=g.SupplierId
     WHERE g.Id='f559b59c-867c-4fa8-a339-cece55e5cd7f' AND g.SupplierId=261 AND s.TaxId='5002736705')),
  ('company_is_alpla_plastico_1',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE g.Id='f559b59c-867c-4fa8-a339-cece55e5cd7f' AND r.CompanyId=1)),
  ('statuses_match_review_PAYMENT_COMPLETED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     JOIN RequestStatuses rs ON rs.Id=r.StatusId
     WHERE g.Id='f559b59c-867c-4fa8-a339-cece55e5cd7f' AND g.Status='PAYMENT_COMPLETED' AND rs.Code='PAYMENT_COMPLETED')),
  ('attachment_evidence_hash_consistent',
    (SELECT CASE WHEN COUNT(*) >= 3 AND COUNT(DISTINCT LOWER(a.FileHash)) = 1
                  AND MIN(LOWER(a.FileHash)) = 'f3e08253d89e91d5707de93867e6c55a6d1841515a973a3e7a7da360a46ba322'
             THEN 1 ELSE 0 END
     FROM RequestAttachments a JOIN Requests r ON r.Id=a.RequestId
     WHERE r.RequestNumber='REQ-20/07/2026-098' AND a.AttachmentTypeCode='PO' AND a.IsDeleted=0 AND a.VoidedAtUtc IS NULL)),
  ('no_canonical_collision_ECF10_2026_230_company1',
    (SELECT CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE r.CompanyId=1 AND g.Id <> 'f559b59c-867c-4fa8-a339-cece55e5cd7f' AND g.PurchaseOrderNumber IS NOT NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF10%2026#230'))
) AS checks(CheckName, Passed)
UNION ALL
SELECT 'REQ-101', CheckName, CASE WHEN Passed = 1 THEN 'PASS' ELSE 'FAIL' END
FROM (VALUES
  ('exactly_one_group_with_old_value',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE r.RequestNumber='REQ-20/07/2026-101' AND g.PurchaseOrderNumber='5001713205')),
  ('resolved_group_is_reviewed_group',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE r.RequestNumber='REQ-20/07/2026-101' AND g.PurchaseOrderNumber='5001713205'
       AND g.Id='cd2f005c-7283-4a82-8364-4ce99eb7cc6a')),
  ('supplier_is_gasp_102',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Suppliers s ON s.Id=g.SupplierId
     WHERE g.Id='cd2f005c-7283-4a82-8364-4ce99eb7cc6a' AND g.SupplierId=102 AND s.TaxId='5001713205')),
  ('company_is_alpla_plastico_1',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE g.Id='cd2f005c-7283-4a82-8364-4ce99eb7cc6a' AND r.CompanyId=1)),
  ('statuses_match_review_PAYMENT_SCHEDULED',
    (SELECT CASE WHEN COUNT(*) = 1 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     JOIN RequestStatuses rs ON rs.Id=r.StatusId
     WHERE g.Id='cd2f005c-7283-4a82-8364-4ce99eb7cc6a' AND g.Status='PAYMENT_SCHEDULED' AND rs.Code='PAYMENT_SCHEDULED')),
  ('attachment_evidence_hash_consistent',
    (SELECT CASE WHEN COUNT(*) >= 2 AND COUNT(DISTINCT LOWER(a.FileHash)) = 1
                  AND MIN(LOWER(a.FileHash)) = 'cec3e78ba3ade8d73b7eccb239ce9d6f0ab68f7962c21d8e14440f323ad1d5d0'
             THEN 1 ELSE 0 END
     FROM RequestAttachments a JOIN Requests r ON r.Id=a.RequestId
     WHERE r.RequestNumber='REQ-20/07/2026-101' AND a.AttachmentTypeCode='PO' AND a.IsDeleted=0 AND a.VoidedAtUtc IS NULL)),
  ('no_canonical_collision_ECF11_2026_386_company1',
    (SELECT CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM RequestPoGroups g JOIN Requests r ON r.Id=g.RequestId
     WHERE r.CompanyId=1 AND g.Id <> 'cd2f005c-7283-4a82-8364-4ce99eb7cc6a' AND g.PurchaseOrderNumber IS NOT NULL
       AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber,' ',''),'.',''),'/','#'),'-','#')) LIKE '%ECF11%2026#386'))
) AS checks(CheckName, Passed);

-- ── Idempotency view: has either repair already been applied? ──
SELECT r.RequestNumber,
       CASE WHEN g.PurchaseOrderNumber IN ('ECF10 2026/230','ECF11 2026/386')
            THEN 'ALREADY_REPAIRED' ELSE 'PENDING_REPAIR' END AS RepairState,
       g.PurchaseOrderNumber AS CurrentValue
FROM RequestPoGroups g
JOIN Requests r ON r.Id = g.RequestId
WHERE g.Id IN ('f559b59c-867c-4fa8-a339-cece55e5cd7f', 'cd2f005c-7283-4a82-8364-4ce99eb7cc6a');
