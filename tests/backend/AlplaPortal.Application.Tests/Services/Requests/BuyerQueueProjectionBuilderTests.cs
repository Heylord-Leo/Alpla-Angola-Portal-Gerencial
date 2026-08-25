using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Services;
using Xunit;
using Proj = AlplaPortal.Domain.Services.BuyerQueueProjectionBuilder;
using S = AlplaPortal.Domain.Constants.BuyerQueueConstants;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Phase 1 characterization of the canonical Buyer-queue projection (pure Domain builder) and the
/// shared cancellation evaluator. Locks operational state, coverage taxonomy/status, priority/deadline,
/// attention signals, ownership and cancel capability so a UI refactor cannot silently drift them.
/// </summary>
public class BuyerQueueProjectionBuilderTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly DateTime Today = new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

    // ── builders ──
    private static Proj.ItemInput Item(string? lifecycle = null, bool deleted = false, string? statusCode = null, bool hasSupplier = false)
        => new(Guid.NewGuid(), deleted, lifecycle, statusCode, hasSupplier);

    private static Proj.RequestInput Req(
        IEnumerable<Proj.ItemInput>? items = null,
        IEnumerable<Proj.BatchInput>? batches = null,
        IEnumerable<Proj.QuotationItemInput>? quotationItems = null,
        IEnumerable<Guid>? superseded = null,
        string type = "QUOTATION",
        string status = "WAITING_QUOTATION",
        bool isCancelled = false,
        Guid? buyerId = null,
        string? needLevel = null,
        DateTime? needBy = null,
        bool requestHasSupplier = false,
        bool hasProforma = false)
        => new(
            Guid.NewGuid(), "REQ-001", "Test", type, status, isCancelled,
            buyerId, needLevel, needBy, Today.AddDays(-10),
            requestHasSupplier, hasProforma,
            (items ?? Array.Empty<Proj.ItemInput>()).ToList(),
            (batches ?? Array.Empty<Proj.BatchInput>()).ToList(),
            (quotationItems ?? Array.Empty<Proj.QuotationItemInput>()).ToList(),
            (superseded ?? Array.Empty<Guid>()).ToList());

    // A quotation item MAPPED to a given line, optionally held by a batch.
    private static (Proj.QuotationItemInput qi, Proj.ItemInput item) MappedCandidate(string recon = "MAPPED")
    {
        var item = Item(); // null lifecycle => pending pool
        var qi = new Proj.QuotationItemInput(Guid.NewGuid(), item.Id, recon);
        return (qi, item);
    }

    private static Proj.BatchInput Batch(string status, params Guid[] lineItemIds)
        => new(Guid.NewGuid(), 1, status,
            lineItemIds.Select(id => new Proj.BatchItemInput(id, null, Array.Empty<Guid>())).ToList());

    // ════════════════════ operational state + coverage ════════════════════

    [Fact]
    public void AllPending_Yields_NeedsQuotation_NotCovered()
    {
        var p = Proj.Build(Req(items: new[] { Item(), Item() }), Me, Today);
        Assert.Equal(S.OperationalStates.NeedsQuotation, p.OperationalState);
        Assert.Equal(S.CoverageStatuses.NotCovered, p.CoverageStatus);
        Assert.Equal(2, p.PendingCount);
        Assert.Contains(p.NextBuyerActions, a => a.Code == S.ActionCodes.AddQuotation && a.Actionable);
    }

    [Fact]
    public void AllReady_NoPending_Yields_ReadyForApproval_FullyCovered()
    {
        var (qi, item) = MappedCandidate();
        var p = Proj.Build(Req(items: new[] { item }, quotationItems: new[] { qi }), Me, Today);
        Assert.Equal(S.OperationalStates.ReadyForApproval, p.OperationalState);
        Assert.Equal(S.CoverageStatuses.FullyCovered, p.CoverageStatus);
        Assert.Equal(1, p.CoverageCounts[S.CoverageBuckets.QuotedReadyForBatch]);
        Assert.Contains(p.NextBuyerActions, a => a.Code == S.ActionCodes.SubmitBatch);
    }

    [Fact]
    public void MixReadyAndPending_Yields_PartialCoverage_TwoActions()
    {
        var (qi, ready) = MappedCandidate();
        var p = Proj.Build(Req(items: new[] { ready, Item() }, quotationItems: new[] { qi }), Me, Today);
        Assert.Equal(S.OperationalStates.PartialCoverage, p.OperationalState);
        Assert.Equal(S.CoverageStatuses.PartiallyCovered, p.CoverageStatus);
        Assert.Contains(p.NextBuyerActions, a => a.Code == S.ActionCodes.AddQuotation);
        Assert.Contains(p.NextBuyerActions, a => a.Code == S.ActionCodes.SubmitBatch);
    }

    [Fact]
    public void PartialWithNoReady_HasOnlyAddQuotation()
    {
        // one approved (treated) + one pending, no ready candidate
        var p = Proj.Build(Req(items: new[] { Item("QUOTATION_APPROVED"), Item() }), Me, Today);
        Assert.Equal(S.OperationalStates.PartialCoverage, p.OperationalState);
        Assert.Single(p.NextBuyerActions);
        Assert.Equal(S.ActionCodes.AddQuotation, p.NextBuyerActions[0].Code);
    }

    [Fact]
    public void ItemInActiveApprovalBatch_Yields_AwaitingApproval()
    {
        var item = Item("BATCH_ASSIGNED");
        var p = Proj.Build(Req(items: new[] { item }, batches: new[] { Batch("WAITING_AREA_APPROVAL", item.Id) }), Me, Today);
        Assert.Equal(S.OperationalStates.AwaitingApproval, p.OperationalState);
        Assert.Equal(1, p.CoverageCounts[S.CoverageBuckets.InActiveBatch]);
        Assert.False(p.NextBuyerActions.Single().Actionable);
    }

    [Fact]
    public void AreaAdjustmentBatch_Yields_AdjustmentRequired_Blocking()
    {
        var item = Item("BATCH_ASSIGNED");
        var p = Proj.Build(Req(items: new[] { item }, batches: new[] { Batch("AREA_ADJUSTMENT", item.Id) }), Me, Today);
        Assert.Equal(S.OperationalStates.AdjustmentRequired, p.OperationalState);
        Assert.Equal(S.PriorityBands.ExceptionOrOverdue, p.PriorityBand);
        Assert.True(p.RequiresAttention);
        Assert.Contains(p.AttentionSignals, s => s.Code == S.AttentionCodes.AdjustmentRequired && s.Severity == S.AttentionSeverities.Blocking);
        Assert.Equal(S.ActionCodes.ResolveAdjustment, p.NextBuyerActions.Single().Code);
    }

    [Fact]
    public void FinalAdjustmentBatch_Yields_AdjustmentRequired()
    {
        var item = Item("BATCH_ASSIGNED");
        var p = Proj.Build(Req(items: new[] { item }, batches: new[] { Batch("FINAL_ADJUSTMENT", item.Id) }), Me, Today);
        Assert.Equal(S.OperationalStates.AdjustmentRequired, p.OperationalState);
    }

    [Fact]
    public void NotQuotedProposed_Yields_AwaitingRequesterDecision()
    {
        var p = Proj.Build(Req(items: new[] { Item("NOT_QUOTED_PROPOSED"), Item("QUOTATION_APPROVED") }), Me, Today);
        Assert.Equal(S.OperationalStates.AwaitingRequesterDecision, p.OperationalState);
        Assert.Equal(S.CoverageStatuses.AwaitingDecision, p.CoverageStatus);
    }

    [Fact]
    public void AllApproved_NoPending_Yields_CompletedForBuyer()
    {
        var p = Proj.Build(Req(items: new[] { Item("QUOTATION_APPROVED"), Item("CLOSED_NOT_QUOTED") }), Me, Today);
        Assert.Equal(S.OperationalStates.CompletedForBuyer, p.OperationalState);
        Assert.Equal(S.CoverageStatuses.FullyCovered, p.CoverageStatus);
    }

    [Fact]
    public void CancelledRequest_Yields_CompletedForBuyer()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, isCancelled: true, status: "CANCELLED"), Me, Today);
        Assert.Equal(S.OperationalStates.CompletedForBuyer, p.OperationalState);
    }

    [Fact]
    public void NonQuotationType_Yields_NoBuyerAction()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, type: "PAYMENT", status: "DRAFT"), Me, Today);
        Assert.Equal(S.OperationalStates.NoBuyerAction, p.OperationalState);
    }

    [Fact]
    public void PastBuyerPhaseStatus_Yields_CompletedForBuyer()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, status: "PO_ISSUED"), Me, Today);
        Assert.Equal(S.OperationalStates.CompletedForBuyer, p.OperationalState);
    }

    // ════════════════════ coverage buckets ════════════════════

    [Fact]
    public void DeletedItem_IsCancelledDeleted_And_ExcludedFromActive()
    {
        var p = Proj.Build(Req(items: new[] { Item(deleted: true), Item() }), Me, Today);
        Assert.Equal(1, p.CoverageCounts[S.CoverageBuckets.CancelledDeleted]);
        Assert.Equal(1, p.ActiveItemCount);
        Assert.Equal(S.OperationalStates.NeedsQuotation, p.OperationalState);
    }

    [Fact]
    public void LineItemStatusCancelled_IsCancelledDeleted()
    {
        var p = Proj.Build(Req(items: new[] { Item(statusCode: "CANCELLED"), Item() }), Me, Today);
        Assert.Equal(1, p.CoverageCounts[S.CoverageBuckets.CancelledDeleted]);
    }

    [Fact]
    public void CandidateHeldByActiveBatch_IsNotSelectable_ItemStaysPending()
    {
        // A quotation item mapped to a pending line but already held (as selectedQuotationItemId) by
        // an active batch is NOT a fresh selectable candidate → the line remains PENDING, not READY.
        var pendingLine = Item();
        var heldQi = new Proj.QuotationItemInput(Guid.NewGuid(), pendingLine.Id, "MAPPED");
        var batch = new Proj.BatchInput(Guid.NewGuid(), 1, "WAITING_AREA_APPROVAL",
            new[] { new Proj.BatchItemInput(Guid.NewGuid(), heldQi.Id, Array.Empty<Guid>()) });
        var p = Proj.Build(Req(items: new[] { pendingLine }, batches: new[] { batch }, quotationItems: new[] { heldQi }), Me, Today);
        Assert.Equal(1, p.CoverageCounts[S.CoverageBuckets.PendingQuotation]);
        Assert.Equal(0, p.CoverageCounts[S.CoverageBuckets.QuotedReadyForBatch]);
    }

    [Fact]
    public void SupersededBatch_IsExcluded_And_RaisesWarning()
    {
        var item = Item(); // pending
        var batch = Batch("WAITING_AREA_APPROVAL", item.Id);
        // batch is superseded → excluded from active set; item treated as pending; warning raised.
        var p = Proj.Build(Req(items: new[] { item }, batches: new[] { batch }, superseded: new[] { batch.Id }), Me, Today);
        Assert.Equal(0, p.ActiveBatchCount);
        Assert.Contains(p.AttentionSignals, s => s.Code == S.AttentionCodes.SupersededBatch);
        Assert.True(p.RequiresAttention);
    }

    // ════════════════════ deadline + priority ════════════════════

    [Fact]
    public void Overdue_Yields_ExceptionBand_UrgentSignal_RequiresAttention()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, needBy: Today.AddDays(-1)), Me, Today);
        Assert.Equal(S.DeadlineConditions.Overdue, p.DeadlineCondition);
        Assert.Equal(S.PriorityBands.ExceptionOrOverdue, p.PriorityBand);
        Assert.True(p.RequiresAttention);
        Assert.Contains(p.AttentionSignals, s => s.Code == S.AttentionCodes.Overdue && s.Severity == S.AttentionSeverities.UrgentDeadline);
    }

    [Fact]
    public void DueToday_IsUrgent_ButStandardBand_NotRequiresAttention()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, needBy: Today), Me, Today);
        Assert.Equal(S.DeadlineConditions.DueToday, p.DeadlineCondition);
        Assert.Equal(S.PriorityBands.Standard, p.PriorityBand);
        Assert.False(p.RequiresAttention);
        Assert.Contains(p.AttentionSignals, s => s.Code == S.AttentionCodes.DueToday);
    }

    [Fact]
    public void ApproachingWithinThreeDays_Yields_Approaching()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, needBy: Today.AddDays(BuyerQueueConstants.ApproachingDeadlineDays)), Me, Today);
        Assert.Equal(S.DeadlineConditions.Approaching, p.DeadlineCondition);
    }

    [Fact]
    public void BeyondThreeDays_Yields_WithinDeadline()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, needBy: Today.AddDays(10)), Me, Today);
        Assert.Equal(S.DeadlineConditions.WithinDeadline, p.DeadlineCondition);
    }

    [Fact]
    public void NoNeedByDate_Yields_None()
    {
        var p = Proj.Build(Req(items: new[] { Item() }), Me, Today);
        Assert.Equal(S.DeadlineConditions.None, p.DeadlineCondition);
    }

    // ════════════════════ ownership ════════════════════

    [Fact]
    public void Ownership_Mine_Unassigned_Other()
    {
        Assert.Equal(S.OwnershipStates.Mine, Proj.Build(Req(items: new[] { Item() }, buyerId: Me), Me, Today).OwnershipState);
        Assert.Equal(S.OwnershipStates.Unassigned, Proj.Build(Req(items: new[] { Item() }), Me, Today).OwnershipState);
        Assert.Equal(S.OwnershipStates.Other, Proj.Build(Req(items: new[] { Item() }, buyerId: Other), Me, Today).OwnershipState);
    }

    [Fact]
    public void UnassignedNearDeadline_Actionable_RaisesWarning()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, needBy: Today.AddDays(2)), Me, Today);
        Assert.Contains(p.AttentionSignals, s => s.Code == S.AttentionCodes.UnassignedNearDeadline);
    }

    [Fact]
    public void NeedLevelRank_Ordering()
    {
        Assert.True(Proj.NeedLevelRank("CRITICO") < Proj.NeedLevelRank("URGENTE"));
        Assert.True(Proj.NeedLevelRank("URGENTE") < Proj.NeedLevelRank("NORMAL"));
        Assert.True(Proj.NeedLevelRank("NORMAL") < Proj.NeedLevelRank("BAIXO"));
        Assert.True(Proj.NeedLevelRank("BAIXO") < Proj.NeedLevelRank(null));
    }

    // ════════════════════ cancel capability (projection, buyer mode) ════════════════════

    [Fact]
    public void CanCancel_True_When_WaitingQuotation_Unprocessed()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, status: "WAITING_QUOTATION"), Me, Today);
        Assert.True(p.CanCancel);
        Assert.Null(p.CancelBlockReason);
    }

    [Fact]
    public void CanCancel_False_When_RequestHasSupplier()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, status: "WAITING_QUOTATION", requestHasSupplier: true), Me, Today);
        Assert.False(p.CanCancel);
        Assert.NotNull(p.CancelBlockReason);
    }

    [Fact]
    public void CanCancel_False_For_Draft_InBuyerMode()
    {
        var p = Proj.Build(Req(items: new[] { Item() }, status: "DRAFT"), Me, Today);
        Assert.False(p.CanCancel);
    }

    // ════════════════════ RequestCancellationEvaluator (shared) ════════════════════

    private static RequestCancellationEvaluator.Input CancelInput(
        string type, string status, bool buyerMode = false, bool hasSupplier = false,
        bool proforma = false, bool processed = false, bool paymentAttach = false, bool cancelled = false)
        => new(type, status, cancelled, buyerMode, hasSupplier, proforma, processed, paymentAttach);

    [Fact]
    public void Cancel_Terminal_Blocked()
    {
        var r = RequestCancellationEvaluator.Evaluate(CancelInput("QUOTATION", "COMPLETED"));
        Assert.False(r.CanCancel);
        Assert.Equal("TERMINAL", r.BlockCode);
    }

    [Fact]
    public void Cancel_Quotation_Draft_BuyerBlocked_NonBuyerAllowed()
    {
        Assert.False(RequestCancellationEvaluator.Evaluate(CancelInput("QUOTATION", "DRAFT", buyerMode: true)).CanCancel);
        Assert.True(RequestCancellationEvaluator.Evaluate(CancelInput("QUOTATION", "DRAFT", buyerMode: false)).CanCancel);
    }

    [Fact]
    public void Cancel_Quotation_Processed_Blocked()
    {
        var r = RequestCancellationEvaluator.Evaluate(CancelInput("QUOTATION", "WAITING_QUOTATION", processed: true));
        Assert.False(r.CanCancel);
        Assert.Equal("BUYER_PROCESSING_STARTED", r.BlockCode);
    }

    [Fact]
    public void Cancel_Payment_BuyerForbidden()
    {
        var r = RequestCancellationEvaluator.Evaluate(CancelInput("PAYMENT", "DRAFT", buyerMode: true));
        Assert.False(r.CanCancel);
        Assert.Equal("BUYER_PAYMENT_FORBIDDEN", r.BlockCode);
    }

    [Fact]
    public void Cancel_Payment_NonBuyer_Allowed_UntilOperationalAttachment()
    {
        Assert.True(RequestCancellationEvaluator.Evaluate(CancelInput("PAYMENT", "APPROVED")).CanCancel);
        var blocked = RequestCancellationEvaluator.Evaluate(CancelInput("PAYMENT", "APPROVED", paymentAttach: true));
        Assert.False(blocked.CanCancel);
        Assert.Equal("PAYMENT_EVIDENCE", blocked.BlockCode);
    }
}
