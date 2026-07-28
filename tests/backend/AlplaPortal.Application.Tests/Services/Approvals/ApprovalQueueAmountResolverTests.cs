using System.Collections.Generic;
using AlplaPortal.Application.Projections;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Unit tests for <see cref="ApprovalQueueAmountResolver"/> — the single authoritative "actionable
/// amount" rule behind every Approval Center queue card and the queue-total KPI. Validation fixture:
/// REQ-17/07/2026-096 (waiting final approval, lot #1, AOA 79,572.00).
/// </summary>
public class ApprovalQueueAmountResolverTests
{
    // Convenience wrapper mirroring the resolver's parameter order.
    private static ApprovalQueueAmountResolver.Resolution Resolve(
        string type,
        bool hasBatch = false,
        int? batchNumber = null,
        decimal? batchSnapshot = null,
        decimal? batchItemSum = null,
        int batchItemCount = 0,
        bool hasSelectedQuotation = false,
        decimal? selectedQuotationTotal = null,
        decimal? requestApprovedTotal = null,
        decimal requestEstimate = 0m)
        => ApprovalQueueAmountResolver.Resolve(type, hasBatch, batchNumber, batchSnapshot, batchItemSum,
            batchItemCount, hasSelectedQuotation, selectedQuotationTotal, requestApprovedTotal, requestEstimate);

    [Fact] // (1) PAYMENT uses the payment amount (Request.EstimatedTotalAmount)
    public void Payment_UsesPaymentAmount()
    {
        var r = Resolve("PAYMENT", requestEstimate: 2_604_672m);
        Assert.Equal(2_604_672m, r.Amount);
        Assert.Equal(ApprovalQueueAmountResolver.Sources.PaymentAmount, r.Source);
    }

    [Fact] // (2) QUOTATION waiting Area Approval uses the active batch total (item sum, no snapshot yet)
    public void QuotationArea_UsesActiveBatchItemSum()
    {
        var r = Resolve("QUOTATION", hasBatch: true, batchNumber: 1, batchSnapshot: null,
            batchItemSum: 4_379_082m, batchItemCount: 2);
        Assert.Equal(4_379_082m, r.Amount);
        Assert.Equal(ApprovalQueueAmountResolver.Sources.BatchItemSum, r.Source);
        Assert.Equal(1, r.LotNumber);
    }

    [Fact] // (3) QUOTATION waiting Final Approval uses the current lot total (approved snapshot) — REQ-096
    public void QuotationFinal_UsesBatchSnapshot()
    {
        var r = Resolve("QUOTATION", hasBatch: true, batchNumber: 1, batchSnapshot: 79_572m,
            batchItemSum: 79_572m, batchItemCount: 1);
        Assert.Equal(79_572m, r.Amount);
        Assert.Equal(ApprovalQueueAmountResolver.Sources.BatchSnapshot, r.Source);
        Assert.False(r.HasInconsistency);
    }

    [Fact] // (4) Partial lot: the batch item sum (only lot items) wins over the full quotation document total
    public void PartialLot_UsesBatchItemSum_NotFullQuotation()
    {
        // Full selected quotation document is larger (includes pending items outside the lot).
        var r = Resolve("QUOTATION", hasBatch: true, batchNumber: 1, batchSnapshot: null,
            batchItemSum: 4_379_082m, batchItemCount: 2,
            hasSelectedQuotation: true, selectedQuotationTotal: 6_689_634m);
        Assert.Equal(4_379_082m, r.Amount);
        Assert.Equal(ApprovalQueueAmountResolver.Sources.BatchItemSum, r.Source);
    }

    [Fact] // (5) QUOTATION without a batch but with a valid selected quotation uses that quotation total
    public void QuotationNoBatch_WithSelectedQuotation_UsesQuotationTotal()
    {
        var r = Resolve("QUOTATION", hasBatch: false, hasSelectedQuotation: true, selectedQuotationTotal: 50_000m);
        Assert.Equal(50_000m, r.Amount);
        Assert.Equal(ApprovalQueueAmountResolver.Sources.SelectedQuotation, r.Source);
    }

    [Fact] // (6) Missing/unresolved amount is NOT a fake zero — it is null
    public void Quotation_Unresolved_IsNullNotZero()
    {
        var r = Resolve("QUOTATION", hasBatch: false, hasSelectedQuotation: false, requestEstimate: 0m);
        Assert.Null(r.Amount);
        Assert.Equal(ApprovalQueueAmountResolver.Sources.Unresolved, r.Source);
    }

    [Fact] // (6b) A batch whose winners cannot be valued is unresolved, not zero
    public void Quotation_BatchWithNoResolvableValue_IsNull()
    {
        var r = Resolve("QUOTATION", hasBatch: true, batchNumber: 2, batchSnapshot: null,
            batchItemSum: null, batchItemCount: 0);
        Assert.Null(r.Amount);
        Assert.Equal(ApprovalQueueAmountResolver.Sources.Unresolved, r.Source);
        Assert.Equal(2, r.LotNumber);
    }

    [Fact] // (7) Genuine zero (zero-value payment) stays a concrete 0, distinguishable from missing
    public void GenuineZero_IsDistinctFromMissing()
    {
        var zeroPayment = Resolve("PAYMENT", requestEstimate: 0m);
        Assert.Equal(0m, zeroPayment.Amount);           // concrete zero
        Assert.NotNull(zeroPayment.Amount);

        var missing = Resolve("QUOTATION", hasBatch: false);
        Assert.Null(missing.Amount);                    // missing
    }

    [Fact] // batch snapshot disagreeing with the item sum surfaces an inconsistency
    public void BatchSnapshotVsItemSumMismatch_FlagsInconsistency()
    {
        var r = Resolve("QUOTATION", hasBatch: true, batchNumber: 1, batchSnapshot: 9_999_999m,
            batchItemSum: 79_572m, batchItemCount: 1);
        Assert.Equal(9_999_999m, r.Amount);
        Assert.True(r.HasInconsistency);
    }

    [Fact] // (8) Queue total equals the sum of authoritative actionable amounts; unresolved excluded, zero counted
    public void QueueTotal_SumsActionable_ExcludingNull()
    {
        var amounts = new List<decimal?>
        {
            Resolve("PAYMENT", requestEstimate: 2_604_672m).Amount,
            Resolve("QUOTATION", hasBatch: true, batchSnapshot: 79_572m, batchItemSum: 79_572m, batchItemCount: 1).Amount,
            Resolve("QUOTATION", hasBatch: false).Amount,          // unresolved (null) — excluded
            Resolve("PAYMENT", requestEstimate: 0m).Amount          // genuine zero — contributes 0
        };

        var total = ApprovalQueueAmountResolver.SumActionable(amounts);
        Assert.Equal(2_604_672m + 79_572m, total);
    }
}
