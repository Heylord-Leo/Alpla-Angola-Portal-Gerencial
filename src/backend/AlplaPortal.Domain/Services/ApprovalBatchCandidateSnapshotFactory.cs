using System;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Single place that freezes a quotation line into an <see cref="ApprovalBatchItemCandidate"/>
/// snapshot at batch submission time. Pure entity construction — the caller must have loaded the
/// QuotationItem with its Quotation (and, when available, Quotation.Supplier and Unit) so every
/// snapshot value comes from SERVER-side truth; nothing here ever originates from the client
/// beyond the optional BuyerNote.
/// </summary>
public static class ApprovalBatchCandidateSnapshotFactory
{
    public static ApprovalBatchItemCandidate Create(
        QuotationItem quotationItem,
        Guid approvalBatchItemId,
        string? buyerNote,
        Guid actorId,
        DateTime nowUtc)
    {
        if (quotationItem.Quotation == null)
            throw new InvalidOperationException(
                "ApprovalBatchCandidateSnapshotFactory requires the QuotationItem to be loaded with its Quotation.");

        var quotation = quotationItem.Quotation;

        return new ApprovalBatchItemCandidate
        {
            Id = Guid.NewGuid(),
            ApprovalBatchItemId = approvalBatchItemId,
            QuotationItemId = quotationItem.Id,
            QuotationId = quotation.Id,

            SupplierId = quotation.SupplierId,
            SupplierNameSnapshot = quotation.Supplier?.Name ?? quotation.SupplierNameSnapshot,
            SupplierNifSnapshot = quotation.Supplier?.TaxId,

            QuotedDescription = quotationItem.Description,
            QuotedQuantity = quotationItem.Quantity,
            UnitId = quotationItem.UnitId,
            UnitTextSnapshot = quotationItem.Unit?.Name ?? quotationItem.OcrOriginalUnitText,

            UnitPrice = quotationItem.UnitPrice,
            DiscountAmount = quotationItem.DiscountAmount,
            IvaRatePercent = quotationItem.IvaRatePercent,
            IvaAmount = quotationItem.IvaAmount,
            GrossSubtotal = quotationItem.GrossSubtotal,
            LineTotal = quotationItem.LineTotal,
            Currency = quotation.Currency,

            QuotationDocumentNumber = quotation.DocumentNumber,
            QuotationDocumentDate = quotation.DocumentDate,

            HasReconciliationWarnings =
                quotationItem.ReconciliationStatus != "MAPPED"
                || !string.IsNullOrWhiteSpace(quotationItem.ReconciliationJustification)
                || !string.IsNullOrWhiteSpace(quotationItem.LineAdjustmentJustification),
            ReconciliationStatusSnapshot = quotationItem.ReconciliationStatus,
            ReconciliationJustificationSnapshot = quotationItem.ReconciliationJustification,
            LineAdjustmentJustificationSnapshot = quotationItem.LineAdjustmentJustification,

            BuyerNote = string.IsNullOrWhiteSpace(buyerNote) ? null : buyerNote.Trim(),

            CreatedAtUtc = nowUtc,
            CreatedByUserId = actorId
        };
    }
}
