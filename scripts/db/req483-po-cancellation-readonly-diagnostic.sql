-- READ-ONLY diagnostic for REQ-27/09/2026-483 (P.O. group 71f902e8-3bb8-4b3b-bdd8-3a8b203c06f4).
-- Every statement is a SELECT. Run against PROD [Portal-Gerencial] with a read-only account. Nothing is changed.
-- Purpose: establish the exact current state before any decision on this request (see report §8).
SET NOCOUNT ON;
DECLARE @g uniqueidentifier = '71f902e8-3bb8-4b3b-bdd8-3a8b203c06f4';
DECLARE @r uniqueidentifier = (SELECT RequestId FROM dbo.RequestPoGroups WHERE Id = @g);

SELECT 'D0 identity' AS section, DB_NAME() AS db, GETUTCDATE() AS nowUtc, @r AS requestId;

SELECT 'D1 request' AS section, r.RequestNumber, s.Code AS statusCode, r.IsCancelled, r.BuyerId, r.ApprovedTotalAmount, r.ApprovedCurrencyCode, r.UpdatedAtUtc
FROM dbo.Requests r JOIN dbo.RequestStatuses s ON s.Id = r.StatusId WHERE r.Id = @r;

SELECT 'D2 groups' AS section, g.Id, g.Status, g.SupplierNameSnapshot, g.CurrencyCode, g.TotalAmount, g.PurchaseOrderNumber,
       g.ApprovalBatchId, g.PoResponsibleBuyerId, g.PaymentConditionCode, g.AdvancePaymentPercent,
       g.OperationalReceiptCompletedAtUtc, g.OperationInvoiceStatus, g.CompletedAtUtc, g.UpdatedAtUtc
FROM dbo.RequestPoGroups g WHERE g.RequestId = @r;

SELECT 'D3 batches' AS section, b.Id, b.BatchNumber, b.Status, b.ApprovedTotalAmount, b.UpdatedAtUtc FROM dbo.ApprovalBatches b WHERE b.RequestId = @r;

SELECT 'D4 batch items' AS section, bi.ApprovalBatchId, bi.RequestLineItemId, bi.SelectedQuotationItemId, bi.SelectedCandidateId, bi.WinnerSelectedAtUtc
FROM dbo.ApprovalBatchItems bi JOIN dbo.ApprovalBatches b ON b.Id = bi.ApprovalBatchId WHERE b.RequestId = @r;

SELECT 'D5 line items' AS section, li.Id, li.LineNumber, li.Description, li.IsDeleted, li.RequestPoGroupId, li.SelectedQuotationItemId,
       li.QuotationLifecycleStatus, li.LineItemStatusId, li.ReceivedQuantity
FROM dbo.RequestLineItems li WHERE li.RequestId = @r;

SELECT 'D6 payments (expected: none active)' AS section, p.Id, p.RequestPoGroupId, p.PaymentType, p.PaymentStatus, p.PaymentSequence, p.ScheduledDateUtc, p.PaidDateUtc
FROM dbo.RequestPayments p WHERE p.RequestId = @r;

SELECT 'D7 attachments' AS section, a.Id, a.AttachmentTypeCode, a.RequestPoGroupId, a.IsDeleted, a.VoidedAtUtc, a.UploadedAtUtc
FROM dbo.RequestAttachments a WHERE a.RequestId = @r ORDER BY a.UploadedAtUtc;

SELECT 'D8 history (last 15)' AS section, h.CreatedAtUtc, h.ActionTaken, ps.Code AS prevStatus, ns.Code AS newStatus, LEFT(h.Comment, 300) AS comment
FROM dbo.RequestStatusHistories h
LEFT JOIN dbo.RequestStatuses ps ON ps.Id = h.PreviousStatusId
LEFT JOIN dbo.RequestStatuses ns ON ns.Id = h.NewStatusId
WHERE h.RequestId = @r ORDER BY h.CreatedAtUtc DESC OFFSET 0 ROWS FETCH NEXT 15 ROWS ONLY;

SELECT 'D9 receiving / fiscal evidence (expected: none)' AS section,
       (SELECT COUNT(*) FROM dbo.OperationInvoices oi WHERE oi.RequestId = @r AND oi.VoidedAtUtc IS NULL) AS operation_invoices,
       (SELECT COUNT(*) FROM dbo.RequestAttachments a WHERE a.RequestPoGroupId = @g AND a.AttachmentTypeCode LIKE '%RECEIPT%' AND a.IsDeleted = 0) AS receipt_attachments;

SELECT 'D10 outbox / notices for the return' AS section, e.EventCode, e.Status, e.RecipientEmail, e.CreatedAtUtc
FROM dbo.EmailOutbox e WHERE e.RequestId = @r ORDER BY e.CreatedAtUtc DESC;
