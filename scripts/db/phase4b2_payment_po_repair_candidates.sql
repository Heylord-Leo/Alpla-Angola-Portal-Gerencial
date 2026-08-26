/* ============================================================================================
   Phase 4B.2 — Historical PAYMENT PO-group repair — PROD CANDIDATE REPORT  (READ-ONLY)

   Purpose : list every PAYMENT request that is APPROVED but has NO RequestPoGroup, with enough
             context to decide SAFE TO REPAIR / MANUAL REVIEW / SKIP. Mirrors the pure classifier
             AlplaPortal.Domain.Services.PaymentPoGroupRepairPlanner exactly.

   SAFETY  : SELECT-only. It performs NO writes and MUST NOT be run as part of any migration,
             deployment or job. Run it manually against PROD to produce the operator report; the
             actual repair is done through the SysAdmin endpoint
             POST /api/v1/requests/admin/payment-po-repair/execute with an EXPLICIT id list.

   NOTE    : object names follow the EF Core model (DbSet names). Add your schema prefix (e.g. dbo.)
             as your environment requires. This script is prepared, NOT executed by the assistant.
   ============================================================================================ */

WITH Candidates AS (
    SELECT r.Id, r.RequestNumber, r.SupplierId, r.ApprovedAtUtc, r.StatusId, r.RequestTypeId
    FROM Requests r
    JOIN RequestTypes    rt ON rt.Id = r.RequestTypeId
    JOIN RequestStatuses rs ON rs.Id = r.StatusId
    WHERE rt.Code = 'PAYMENT'
      AND rs.Code = 'APPROVED'
      AND NOT EXISTS (SELECT 1 FROM RequestPoGroups g WHERE g.RequestId = r.Id)
),
Facts AS (
    SELECT
        c.Id AS RequestId,
        c.RequestNumber,
        c.ApprovedAtUtc,
        CASE WHEN c.ApprovedAtUtc IS NOT NULL THEN 1 ELSE 0 END AS FinalApprovalCompleted,
        (SELECT COUNT(*) FROM PaymentSourceDocuments d
           WHERE d.RequestId = c.Id AND d.IsVoided = 0)                              AS SourceDocumentCount,
        (SELECT COUNT(*) FROM RequestLineItems li
           WHERE li.RequestId = c.Id AND li.IsDeleted = 0)                           AS ActiveLineItemCount,
        (SELECT COUNT(*) FROM RequestLineItems li
           WHERE li.RequestId = c.Id AND li.IsDeleted = 0
             AND li.PaymentSourceDocumentId IS NOT NULL)                             AS LinkedItemCount,
        CASE WHEN c.SupplierId IS NOT NULL
                  OR EXISTS (SELECT 1 FROM PaymentSourceDocuments d
                             WHERE d.RequestId = c.Id AND d.IsVoided = 0 AND d.SupplierId IS NOT NULL)
             THEN 1 ELSE 0 END                                                        AS HasSupplierSource,
        CASE WHEN EXISTS (SELECT 1 FROM RequestAttachments a
                          WHERE a.RequestId = c.Id AND a.IsDeleted = 0
                            AND a.AttachmentTypeCode = 'PO')
             THEN 1 ELSE 0 END                                                        AS HasPoEvidence,
        CASE WHEN EXISTS (SELECT 1 FROM RequestAttachments a
                          WHERE a.RequestId = c.Id AND a.IsDeleted = 0
                            AND a.AttachmentTypeCode IN
                                ('PO','PAYMENT_PROOF','PAYMENT_SCHEDULE','ADVANCE_PAYMENT_PROOF',
                                 'OPERATION_INVOICE','FISCAL_RECEIPT','RECEIPT','RECEIVING_EVIDENCE'))
                  OR EXISTS (SELECT 1 FROM RequestPayments p WHERE p.RequestId = c.Id)
             THEN 1 ELSE 0 END                                                        AS HasDownstreamEvidence
    FROM Candidates c
)
SELECT
    f.RequestId,
    f.RequestNumber,
    'PAYMENT'  AS RequestTypeCode,
    'APPROVED' AS ScalarStatusCode,
    f.ApprovedAtUtc                                             AS FinalApprovalAtUtc,
    f.ActiveLineItemCount,
    f.SourceDocumentCount,
    f.LinkedItemCount,
    0                                                            AS ExistingGroupCount,   -- by definition
    f.HasPoEvidence,
    f.HasDownstreamEvidence,
    CASE
        WHEN f.SourceDocumentCount > 0 AND f.LinkedItemCount > 0 THEN 'MULTI_DOCUMENT'
        WHEN f.SourceDocumentCount = 0                          THEN 'LEGACY_HEADER'
        ELSE 'AMBIGUOUS'
    END                                                          AS Model,
    /* Verdict — identical order of precedence to PaymentPoGroupRepairPlanner.Assess */
    CASE
        WHEN f.FinalApprovalCompleted = 0                                   THEN 'SKIP'
        WHEN f.HasDownstreamEvidence = 1                                    THEN 'MANUAL REVIEW'
        WHEN f.SourceDocumentCount > 0 AND f.LinkedItemCount = 0            THEN 'MANUAL REVIEW'
        WHEN f.SourceDocumentCount > 0 AND f.LinkedItemCount > 0            THEN 'SAFE TO REPAIR'
        WHEN f.SourceDocumentCount = 0 AND f.HasSupplierSource = 1          THEN 'SAFE TO REPAIR'
        ELSE 'MANUAL REVIEW'
    END                                                          AS ProposedAction
FROM Facts f
ORDER BY ProposedAction, f.ApprovedAtUtc;
