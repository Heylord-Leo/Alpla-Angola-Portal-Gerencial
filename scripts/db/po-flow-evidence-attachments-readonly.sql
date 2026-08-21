-- ============================================================================
-- PO-FLOW EVIDENCE SCAN — ATTACHMENT INVENTORY (READ-ONLY)
-- ============================================================================
-- Stage 1 of the document-evidence process (see po-flow-evidence-scan.ps1).
-- Produces the attachment inventory for:
--   (B) the 15 Population-B requests (supplier identity evidence — source documents)
--   (C) the 10 suspicious historical P.O records (TYPE_PO evidence)
-- Run against the environment where the attachment binaries exist (TEST/PROD server).
-- SELECT only — no writes.
-- ============================================================================

-- (B) Population B: source-typed attachments of the 15 supplier-less requests
SELECT 'POPULATION_B' AS Scope,
       r.RequestNumber,
       a.Id            AS AttachmentId,
       a.FileName,
       a.FileHash,
       a.AttachmentTypeCode,
       a.StorageReference,
       a.IsDeleted
FROM Requests r
JOIN RequestAttachments a ON a.RequestId = r.Id AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
WHERE r.RequestNumber IN (
    'REQ-15/07/2026-071','REQ-16/07/2026-084','REQ-29/07/2026-178','REQ-31/07/2026-193',
    'REQ-31/07/2026-194','REQ-31/07/2026-200','REQ-03/08/2026-208','REQ-05/08/2026-215',
    'REQ-06/08/2026-222','REQ-11/08/2026-230','REQ-11/08/2026-233','REQ-12/08/2026-237',
    'REQ-12/08/2026-238','REQ-12/08/2026-241','REQ-12/08/2026-245')
  AND a.AttachmentTypeCode IN ('PROFORMA','PAYMENT_SOURCE_DOCUMENT')

UNION ALL

-- (C) Suspicious historical P.O numbers: the TYPE_PO attachment that carries the true reference
SELECT 'SUSPICIOUS_PO' AS Scope,
       r.RequestNumber,
       a.Id,
       a.FileName,
       a.FileHash,
       a.AttachmentTypeCode,
       a.StorageReference,
       a.IsDeleted
FROM Requests r
JOIN RequestAttachments a ON a.RequestId = r.Id AND a.IsDeleted = 0 AND a.VoidedAtUtc IS NULL
WHERE r.RequestNumber IN (
    'REQ-03/07/2026-017','REQ-13/07/2026-038','REQ-20/07/2026-098','REQ-20/07/2026-101',
    'REQ-22/07/2026-138','REQ-22/07/2026-139','REQ-23/07/2026-140','REQ-23/07/2026-146',
    'REQ-30/07/2026-188','REQ-31/07/2026-192')
  AND a.AttachmentTypeCode = 'PO'
ORDER BY Scope, RequestNumber;
