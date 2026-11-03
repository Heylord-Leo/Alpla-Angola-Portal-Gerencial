/* ============================================================================
   Portal Gerencial — v2.249.5 manual TEST validation, finding 1 (REQ-21/10/2026-595)
   QUOTATION DISCOUNT / APPROVED AMOUNT DIAGNOSTIC (READ-ONLY)

   File ....... scripts/db/quotation-discount-pricing-req595-readonly.sql
   Target ..... [Portal-Gerencial-Test] ONLY. Any other database name aborts the
                whole batch before any SELECT runs (guard below).
   Mode ....... SELECT-only. No DDL, no DML, no temp tables, no transactions,
                no GO separators (one batch, so the guard's RETURN stops everything).
   Secrets .... none selected (no Users credential columns, no SMTP, no tokens).
   Purpose .... establish from PERSISTED facts, not from arithmetic, which of
                the three hypotheses explains the 416.850,00 approved for a
                345.775,50 quotation:
                  H1  per-line discounts never extracted by the provider
                      (OcrExtractedItems.DiscountAmount = 0 on the raw rows);
                  H2  extracted but dropped by the wizard's "Strategy A" anchor
                      (raw rows carry the discount; the quotation lines carry
                      OcrOriginalDiscountAmount = 0, because the baseline is
                      written from the wizard's RESOLVED value, not from the raw
                      extraction);
                  H3  changed during manual review (raw rows AND OcrOriginal*
                      carry the discount; the final DiscountAmount is 0 and
                      LineAdjustmentJustification / QUOTATION_UPDATED rows exist);
                  H4  no OCR header total was sent, so the reconciliation gate
                      never ran: ONLY when #3 carries OcrOriginal* AND #5 has
                      neither QUOTATION_RESIDUAL_JUSTIFIED nor QUOTATION_UPDATED
                      evidence AND #4 shows the header read; the mere absence of
                      a QUOTATION_RESIDUAL_JUSTIFIED row proves nothing by itself.
                H1-H4 stay OPEN until these result sets discriminate them; the
                operator's actual PDF (20261021_104430-LUANDAY.pdf) must be
                compared column by column with #4.
                In every case the persisted model routes a quotation-level
                discount (Quotations.DiscountAmount) only into the quotation
                totals; candidates / groups / approved totals are per line
                (see docs/QUOTATION_DISCOUNT_PRICING_AND_PO_WIZARD_REFUSAL_v2.249.6.md).

   How to read the result sets
     #1 request scalar; #2 quotation headers (DiscountAmount = GLOBAL discount;
     TotalDiscountAmount = line discounts + global); #3 quotation lines with the
     OCR baseline and the per-line arithmetic check; #4 RAW provider rows
     (OcrExtractedItems, every extraction batch, newest first), the H1/H2
     discriminator; #5 history of quotation writes and submissions; #6 batches,
     items and FROZEN candidate snapshots; #7 P.O. groups; #8 P.O.-wizard
     extractions (finding 2 evidence: GrandTotal / classification / verdict);
     #9 inventory of EVERY quotation in TEST with an unallocated global discount
     that reached a non-cancelled batch (scope of the "existing requests"
     proposal); #10 same inventory for line-sum mismatches without global discount;
     #11 legacy rows with TotalAmount = 0 and positive considered lines
     (QuotationLinePricingPolicy kind LEGACY_TOTAL_NOT_COMPUTED: impossible for the
     current write path; resolved for compatibility, listed so the owner knows).
   ============================================================================ */
SET NOCOUNT ON;

IF DB_NAME() <> N'Portal-Gerencial-Test'
BEGIN
    SELECT N'ABORTED: this diagnostic runs only against [Portal-Gerencial-Test]; current database = ' + DB_NAME() AS Guard;
    RETURN;
END;

DECLARE @RequestNumber NVARCHAR(50) = N'REQ-21/10/2026-595';
DECLARE @RequestId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM Requests WHERE RequestNumber = @RequestNumber);

/* #1 - request scalar */
SELECT N'#1 request' AS ResultSet, r.Id, r.RequestNumber, r.Title, s.Code AS StatusCode, r.EstimatedTotalAmount, r.ApprovedTotalAmount,
       r.ApprovedCurrencyCode, r.DiscountAmount AS RequestLevelDiscount, r.PaymentConditionCode, r.AdvancePaymentPercent, r.CreatedAtUtc, r.UpdatedAtUtc
FROM Requests r LEFT JOIN RequestStatuses s ON s.Id = r.StatusId
WHERE r.Id = @RequestId;

/* #2 - quotation headers: DiscountAmount is the quotation-level ("global") discount the lines do not carry */
SELECT N'#2 quotation headers' AS ResultSet, q.Id AS QuotationId, q.SupplierId, q.SupplierNameSnapshot, q.DocumentNumber, q.DocumentDate, q.DocumentType,
       q.Currency, q.SourceType, q.SourceFileName, q.ProformaAttachmentId,
       q.TotalGrossAmount, q.TotalDiscountAmount, q.DiscountAmount AS GlobalDiscount, q.TotalTaxableBase, q.TotalIvaAmount, q.TotalAmount,
       q.IsSelected, q.CreatedAtUtc, q.CreatedByUserId, q.FirstSubmittedForApprovalAtUtc, q.FirstSubmittedBatchId, q.FirstSubmittedSource,
       x.ConsideredLineTotalSum,
       CASE WHEN q.DiscountAmount > 0 THEN N'GLOBAL_DISCOUNT_UNALLOCATED'
            WHEN q.TotalAmount > 0 AND ABS(x.ConsideredLineTotalSum - q.TotalAmount) > 1.00 THEN N'LINE_TOTAL_MISMATCH'
            ELSE N'LINE_RESOLVED' END AS LinePricingVerdict
FROM Quotations q
CROSS APPLY (SELECT ISNULL(SUM(qi.LineTotal), 0) AS ConsideredLineTotalSum
             FROM QuotationItems qi
             WHERE qi.QuotationId = q.Id AND qi.ReconciliationStatus IN (N'MAPPED', N'SUBSTITUTE', N'EXTRA_ITEM')) x
WHERE q.RequestId = @RequestId
ORDER BY q.CreatedAtUtc;

/* #3 - quotation lines: final values, OCR baseline (resolved by the wizard at extraction time) and the per-line arithmetic */
SELECT N'#3 quotation lines' AS ResultSet, q.SupplierNameSnapshot, qi.QuotationId, qi.Id AS QuotationItemId, qi.LineNumber, qi.Description,
       qi.Quantity, qi.UnitId, qi.UnitPrice, qi.DiscountAmount, qi.DiscountPercent, qi.IvaRateId, qi.IvaRatePercent,
       qi.GrossSubtotal, qi.IvaAmount, qi.LineTotal, qi.ReconciliationStatus, qi.MappedRequestLineItemId,
       qi.OcrOriginalQuantity, qi.OcrOriginalUnitPrice, qi.OcrOriginalDiscountAmount, qi.OcrOriginalIvaRatePercent, qi.OcrOriginalUnitText, qi.OcrOriginalLineTotal,
       qi.LineAdjustmentJustification, qi.ReconciliationJustification,
       ROUND(qi.Quantity * qi.UnitPrice, 2) AS CheckGross,
       ROUND((ROUND(qi.Quantity * qi.UnitPrice, 2) - qi.DiscountAmount) * (1 + qi.IvaRatePercent / 100.0), 2) AS CheckLineTotalWithLineDiscount,
       ROUND(ROUND(qi.Quantity * qi.UnitPrice, 2) * (1 + qi.IvaRatePercent / 100.0), 2) AS CheckLineTotalWithoutDiscount
FROM QuotationItems qi INNER JOIN Quotations q ON q.Id = qi.QuotationId
WHERE q.RequestId = @RequestId
ORDER BY q.CreatedAtUtc, qi.LineNumber;

/* #4 - RAW provider rows persisted by ocr-extract (never edited): H1 vs H2 discriminator */
SELECT N'#4 raw OCR rows' AS ResultSet, o.ExtractionBatchId, o.ExtractedAtUtc, o.ProviderName, o.QualityScore, o.AttachmentId,
       o.LineNumber, o.RawDescription, o.Quantity, o.RawUnit, o.ResolvedUnitId, o.UnitPrice, o.DiscountAmount AS RawDiscountAmount,
       o.DiscountPercent AS RawDiscountPercent, o.TaxRate AS RawTaxRate, o.LineTotal AS RawLineTotal,
       ROUND(ISNULL(o.Quantity, 0) * ISNULL(o.UnitPrice, 0), 2) AS CheckGross,
       ROUND(ISNULL(o.Quantity, 0) * ISNULL(o.UnitPrice, 0), 2) - ISNULL(o.LineTotal, 0) AS CheckImpliedDiscount_StrategyA
FROM OcrExtractedItems o
WHERE o.RequestId = @RequestId
ORDER BY o.ExtractedAtUtc DESC, o.LineNumber;

/* #5 - history of quotation writes / reconciliation overrides / submissions / approvals (actor name only, no credentials) */
SELECT N'#5 history' AS ResultSet, h.CreatedAtUtc, h.ActionTaken, u.FullName AS Actor, ps.Code AS PreviousStatus, ns.Code AS NewStatus, h.Comment
FROM RequestStatusHistories h
LEFT JOIN Users u ON u.Id = h.ActorUserId
LEFT JOIN RequestStatuses ps ON ps.Id = h.PreviousStatusId
LEFT JOIN RequestStatuses ns ON ns.Id = h.NewStatusId
WHERE h.RequestId = @RequestId
  AND (h.ActionTaken IN (N'COTACAO_ADICIONADA', N'COTACAO_SUBSTITUIDA', N'QUOTATION_UPDATED', N'QUOTATION_RESIDUAL_JUSTIFIED',
                         N'QUOTATION_DELETED', N'BATCH_CANDIDATES_SUBMITTED', N'BATCH_CREATED', N'BATCH_AREA_APPROVED', N'BATCH_FINAL_APPROVED',
                         N'REGISTER_PO', N'REREGISTER_PO')
       OR h.ActionTaken LIKE N'BATCH%' OR h.ActionTaken LIKE N'%QUOTATION%' OR h.ActionTaken LIKE N'%COTACAO%')
ORDER BY h.CreatedAtUtc;

/* #6 - batches, items and FROZEN candidate snapshots (what approval actually valued) */
SELECT N'#6 batches/candidates' AS ResultSet, b.Id AS BatchId, b.BatchNumber, b.Status AS BatchStatus, b.ApprovedTotalAmount, b.ApprovedCurrencyCode, b.CreatedAtUtc AS BatchCreatedAtUtc,
       bi.Id AS BatchItemId, bi.RequestLineItemId, bi.SelectedCandidateId, bi.SelectedQuotationItemId, bi.ReleasedAtUtc,
       c.Id AS CandidateId, c.QuotationId, c.QuotationItemId, c.SupplierNameSnapshot, c.QuotedDescription, c.QuotedQuantity, c.UnitPrice,
       c.DiscountAmount AS CandidateLineDiscount, c.IvaRatePercent, c.IvaAmount, c.GrossSubtotal, c.LineTotal AS CandidateLineTotal, c.Currency,
       c.QuotationDocumentNumber, c.HasReconciliationWarnings, c.LineAdjustmentJustificationSnapshot, c.CreatedAtUtc AS CandidateCreatedAtUtc,
       CASE WHEN bi.SelectedCandidateId = c.Id THEN 1 ELSE 0 END AS IsWinner
FROM ApprovalBatches b
INNER JOIN ApprovalBatchItems bi ON bi.ApprovalBatchId = b.Id
LEFT JOIN ApprovalBatchItemCandidates c ON c.ApprovalBatchItemId = bi.Id
WHERE b.RequestId = @RequestId
ORDER BY b.BatchNumber, bi.CreatedAtUtc, c.CreatedAtUtc;

/* #7 - P.O. groups (TotalAmount = sum of the winners' line totals at area approval) */
SELECT N'#7 PO groups' AS ResultSet, g.Id AS GroupId, g.ApprovalBatchId, g.SupplierId, g.SupplierNameSnapshot, g.SupplierNifSnapshot, g.CurrencyCode,
       g.TotalAmount AS GroupTotal, g.Status, g.PaymentConditionCode, g.AdvancePaymentPercent, g.PurchaseOrderNumber, g.CurrentPoAttachmentId,
       g.SourceDocumentType, g.CreatedAtUtc, g.UpdatedAtUtc,
       (SELECT COUNT(*) FROM RequestLineItems li WHERE li.RequestPoGroupId = g.Id AND li.IsDeleted = 0) AS LineCount
FROM RequestPoGroups g
WHERE g.RequestId = @RequestId
ORDER BY g.CreatedAtUtc;

/* #8 - P.O.-wizard extractions for this request (finding 2: the proforma run through "Registrar P.O.") */
SELECT N'#8 PO document extractions' AS ResultSet, e.Id, e.CreatedAtUtc, e.FileName, e.FileHash, e.Success, e.ProviderStatusCode, e.Verdict, e.ReasonCode,
       e.Reference, e.Family, e.DocumentNumber, e.DocumentTitle, e.ClassificationType, e.ClassificationConfidence, e.SupplierName, e.SupplierTaxId,
       e.CurrencyCode, e.GrandTotal, e.ReferencedDocumentNumbersJson
FROM PoDocumentExtractions e
WHERE e.RequestId = @RequestId
ORDER BY e.CreatedAtUtc DESC;

/* #9 - inventory (whole TEST database): quotations with an UNALLOCATED global discount that reached a non-cancelled batch */
SELECT N'#9 inventory: global discount in approval' AS ResultSet, r.RequestNumber, rs.Code AS RequestStatus, q.Id AS QuotationId, q.SupplierNameSnapshot,
       q.DocumentNumber, q.Currency, q.DiscountAmount AS GlobalDiscount, q.TotalAmount AS QuotationTotal, x.ConsideredLineTotalSum,
       b.BatchNumber, b.Status AS BatchStatus, b.ApprovedTotalAmount,
       (SELECT COUNT(*) FROM ApprovalBatchItems bi2
        WHERE bi2.ApprovalBatchId = b.Id
          AND bi2.SelectedCandidateId IN (SELECT c2.Id FROM ApprovalBatchItemCandidates c2 WHERE c2.QuotationId = q.Id)) AS WinningLinesFromThisQuotation,
       (SELECT STRING_AGG(CONVERT(NVARCHAR(36), g.Id) + N' ' + g.Status + N' ' + CONVERT(NVARCHAR(30), g.TotalAmount), N' ; ')
        FROM RequestPoGroups g WHERE g.ApprovalBatchId = b.Id AND g.SupplierId = q.SupplierId) AS GroupsOfThisSupplier
FROM Quotations q
INNER JOIN Requests r ON r.Id = q.RequestId
LEFT JOIN RequestStatuses rs ON rs.Id = r.StatusId
CROSS APPLY (SELECT ISNULL(SUM(qi.LineTotal), 0) AS ConsideredLineTotalSum
             FROM QuotationItems qi
             WHERE qi.QuotationId = q.Id AND qi.ReconciliationStatus IN (N'MAPPED', N'SUBSTITUTE', N'EXTRA_ITEM')) x
INNER JOIN ApprovalBatchItemCandidates c ON c.QuotationId = q.Id
INNER JOIN ApprovalBatchItems bi ON bi.Id = c.ApprovalBatchItemId
INNER JOIN ApprovalBatches b ON b.Id = bi.ApprovalBatchId
WHERE q.DiscountAmount > 0
  AND b.Status <> N'CANCELLED'
GROUP BY r.RequestNumber, rs.Code, q.Id, q.SupplierNameSnapshot, q.DocumentNumber, q.Currency, q.DiscountAmount, q.TotalAmount, q.SupplierId,
         x.ConsideredLineTotalSum, b.Id, b.BatchNumber, b.Status, b.ApprovedTotalAmount
ORDER BY r.RequestNumber, b.BatchNumber;

/* #10 - inventory: line-sum mismatch WITHOUT a global discount (legacy arithmetic drift) */
SELECT N'#10 inventory: line-sum mismatch' AS ResultSet, r.RequestNumber, rs.Code AS RequestStatus, q.Id AS QuotationId, q.SupplierNameSnapshot, q.DocumentNumber,
       q.Currency, q.TotalAmount AS QuotationTotal, x.ConsideredLineTotalSum, x.ConsideredLineTotalSum - q.TotalAmount AS Difference,
       q.FirstSubmittedForApprovalAtUtc
FROM Quotations q
INNER JOIN Requests r ON r.Id = q.RequestId
LEFT JOIN RequestStatuses rs ON rs.Id = r.StatusId
CROSS APPLY (SELECT ISNULL(SUM(qi.LineTotal), 0) AS ConsideredLineTotalSum
             FROM QuotationItems qi
             WHERE qi.QuotationId = q.Id AND qi.ReconciliationStatus IN (N'MAPPED', N'SUBSTITUTE', N'EXTRA_ITEM')) x
WHERE q.DiscountAmount = 0
  AND q.TotalAmount > 0
  AND ABS(x.ConsideredLineTotalSum - q.TotalAmount) > 1.00
ORDER BY r.RequestNumber, q.CreatedAtUtc;

/* #11 - inventory: legacy zero totals with positive considered lines (LEGACY_TOTAL_NOT_COMPUTED). The current SaveQuotation /
   UpdateQuotation compute TotalAmount from the lines, so any row here predates that arithmetic. Resolved for compatibility
   (approval values the lines); listed, never repaired by this script. */
SELECT N'#11 inventory: legacy zero total' AS ResultSet, r.RequestNumber, rs.Code AS RequestStatus, q.Id AS QuotationId, q.SupplierNameSnapshot,
       q.DocumentNumber, q.Currency, q.TotalAmount AS QuotationTotal, q.DiscountAmount AS GlobalDiscount, x.ConsideredLineTotalSum,
       q.SourceType, q.CreatedAtUtc, q.FirstSubmittedForApprovalAtUtc,
       (SELECT COUNT(*) FROM ApprovalBatchItemCandidates c WHERE c.QuotationId = q.Id) AS CandidateRows
FROM Quotations q
INNER JOIN Requests r ON r.Id = q.RequestId
LEFT JOIN RequestStatuses rs ON rs.Id = r.StatusId
CROSS APPLY (SELECT ISNULL(SUM(qi.LineTotal), 0) AS ConsideredLineTotalSum
             FROM QuotationItems qi
             WHERE qi.QuotationId = q.Id AND qi.ReconciliationStatus IN (N'MAPPED', N'SUBSTITUTE', N'EXTRA_ITEM')) x
WHERE q.TotalAmount <= 0
  AND x.ConsideredLineTotalSum > 0
ORDER BY r.RequestNumber, q.CreatedAtUtc;
