using System;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.230.0 — Requests-list expanded-row per-lot progress timelines. Lot resolution reuses the
/// workflow projection (no second superseded/dedupe algorithm); stages are SEMANTIC (never
/// numeric ordering); Recebimento/Execução and Documentação Fiscal are distinct; lot numbers
/// are real domain identity only.
/// </summary>
public class RequestLotTimelineBuilderTests
{
    private static Request MakeRequest(string statusCode) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Status = new RequestStatus { Id = 1, Code = statusCode },
            RequestType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "QUOTATION" }
        };

    private static RequestPoGroup Group(string status, string supplier = "FORN A", string? po = null, Guid? batchId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            SupplierNameSnapshot = supplier,
            PurchaseOrderNumber = po,
            TotalAmount = 100m,
            CurrencyCode = "AOA",
            ApprovalBatchId = batchId
        };

    private static string StageState(System.Collections.Generic.IReadOnlyList<LotTimelineStep> steps, string label) =>
        steps.Single(s => s.Label == label).State;

    /// <summary>Builds a single-group request and returns the mapped stages for that group status.</summary>
    private static System.Collections.Generic.IReadOnlyList<LotTimelineStep> StagesFor(string groupStatus, string? po = "ECF10 2026/1")
    {
        var request = MakeRequest("PO_ISSUED");
        request.PoGroups.Add(Group(groupStatus, po: po));
        var unit = RequestWorkflowProjectionBuilder.Build(request, "PO_ISSUED").Units.Single();
        return RequestLotTimelineBuilder.MapStages(unit);
    }

    // ── Historical compatibility (≥1-unit rule) ──

    /// <summary>Case 1: batchless single group WAITING_PO → one unit timeline, PO current, no fabricated lot number.</summary>
    [Fact]
    public void SingleBatchlessGroup_WaitingPo_ProducesOneLot_NoFabricatedNumber()
    {
        var request = MakeRequest("APPROVED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, po: null));
        var projection = RequestWorkflowProjectionBuilder.Build(request, "APPROVED");

        var lot = Assert.Single(RequestLotTimelineBuilder.BuildLots(request, projection));
        Assert.Null(lot.LotNumber);
        Assert.Equal("current", StageState(lot.Steps, RequestLotTimelineBuilder.StagePo));
        Assert.Equal("completed", StageState(lot.Steps, RequestLotTimelineBuilder.StageApprovals));
    }

    /// <summary>Case 2/6: batchless single group PO_ISSUED with lagging scalar — timeline driven by the group.</summary>
    [Fact]
    public void SingleBatchlessGroup_PoIssued_ScalarLagging_TimelineDrivenByGroup()
    {
        var request = MakeRequest("APPROVED"); // scalar behind the group lifecycle (REQ-140 class)
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, "CASA DO PAPEL", po: "5001334395"));
        var projection = RequestWorkflowProjectionBuilder.Build(request, "APPROVED");

        var lot = Assert.Single(RequestLotTimelineBuilder.BuildLots(request, projection));
        Assert.Null(lot.LotNumber);
        Assert.Equal("completed", StageState(lot.Steps, RequestLotTimelineBuilder.StagePo));
        Assert.Equal("current", StageState(lot.Steps, RequestLotTimelineBuilder.StagePayment));
        Assert.Equal("pending", StageState(lot.Steps, RequestLotTimelineBuilder.StageReceiving));
    }

    /// <summary>Case 3: batchless PAYMENT_SCHEDULED → Payment current.</summary>
    [Fact]
    public void SingleBatchlessGroup_PaymentScheduled_PaymentCurrent()
    {
        var request = MakeRequest("PAYMENT_SCHEDULED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PaymentScheduled, po: "ECF10 2026/7"));
        var projection = RequestWorkflowProjectionBuilder.Build(request, "PAYMENT_SCHEDULED");

        var lot = Assert.Single(RequestLotTimelineBuilder.BuildLots(request, projection));
        Assert.Equal("current", StageState(lot.Steps, RequestLotTimelineBuilder.StagePayment));
    }

    /// <summary>Case 4: batch-backed single group → one unit timeline carrying the REAL lot number.</summary>
    [Fact]
    public void SingleBatchBackedGroup_OneLot_WithRealLotNumber()
    {
        var request = MakeRequest("PO_REQUESTED");
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 7, Status = RequestConstants.ApprovalBatchStatuses.Approved };
        request.ApprovalBatches.Add(batch);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, po: null, batchId: batch.Id));
        var projection = RequestWorkflowProjectionBuilder.Build(request, "PO_REQUESTED");

        var lot = Assert.Single(RequestLotTimelineBuilder.BuildLots(request, projection));
        Assert.Equal(7, lot.LotNumber);
    }

    /// <summary>Case 7: no batches, no groups (class A) → Lots omitted, legacy Request timeline path.</summary>
    [Fact]
    public void NoUnits_ProducesNoLots_LegacyFallback()
    {
        var request = MakeRequest("WAITING_QUOTATION");
        var projection = RequestWorkflowProjectionBuilder.Build(request, "WAITING_QUOTATION");

        Assert.Empty(RequestLotTimelineBuilder.BuildLots(request, projection));
    }

    /// <summary>Cases 8/9: CANCELLED and REJECTED requests never render lot timelines.</summary>
    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("REJECTED")]
    public void TerminalRequests_NoLotTimelines(string terminalStatus)
    {
        var request = MakeRequest(terminalStatus);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, po: "ECF10 2026/8"));
        var projection = RequestWorkflowProjectionBuilder.Build(request, terminalStatus);

        Assert.Empty(projection.Units);
        Assert.Empty(RequestLotTimelineBuilder.BuildLots(request, projection));
    }

    /// <summary>Case 10: two supplier groups from the SAME batch legitimately share Lote #1.</summary>
    [Fact]
    public void TwoGroupsFromSameBatch_ShareTheSameRealLotNumber()
    {
        var request = MakeRequest("PO_REQUESTED");
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.Approved };
        request.ApprovalBatches.Add(batch);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, "A", po: null, batchId: batch.Id));
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, "B", po: null, batchId: batch.Id));

        var lots = RequestLotTimelineBuilder.BuildLots(request, RequestWorkflowProjectionBuilder.Build(request, "PO_REQUESTED"));

        Assert.Equal(2, lots.Count);
        Assert.All(lots, l => Assert.Equal(1, l.LotNumber));
        Assert.Equal(2, lots.Select(l => l.SupplierName).Distinct().Count()); // suppliers differentiate
    }

    // ── Scenario B: two WAITING_PO lots, both at P.O. stage ──
    [Fact]
    public void TwoWaitingPoGroups_TwoLots_BothAtPoStage()
    {
        var request = MakeRequest("PO_REQUESTED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, "A", po: null));
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, "B", po: null));
        var projection = RequestWorkflowProjectionBuilder.Build(request, "PO_REQUESTED");

        var lots = RequestLotTimelineBuilder.BuildLots(request, projection);

        Assert.Equal(2, lots.Count);
        Assert.All(lots, lot =>
        {
            Assert.Equal("current", StageState(lot.Steps, RequestLotTimelineBuilder.StagePo));
            Assert.Equal("completed", StageState(lot.Steps, RequestLotTimelineBuilder.StageApprovals));
            Assert.Equal("pending", StageState(lot.Steps, RequestLotTimelineBuilder.StagePayment));
        });
    }

    // ── Scenario C: PO_ISSUED group + live batch → Payment + Approvals ──
    [Fact]
    public void IssuedGroup_PlusLiveBatch_PaymentAndApprovalStages()
    {
        var request = MakeRequest("PO_ISSUED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, "A", po: "ECF10 2026/1"));
        var li = new RequestLineItem { Id = Guid.NewGuid() };
        request.LineItems.Add(li);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval };
        batch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(batch);

        var lots = RequestLotTimelineBuilder.BuildLots(request, RequestWorkflowProjectionBuilder.Build(request, "MIXED_PROCESSING"));

        Assert.Equal(2, lots.Count);
        var batchLot = lots.Single(l => l.UnitType == "BATCH");
        var groupLot = lots.Single(l => l.UnitType == "GROUP");
        Assert.Equal(2, batchLot.LotNumber);
        Assert.Equal("current", StageState(batchLot.Steps, RequestLotTimelineBuilder.StageApprovals));
        Assert.Equal("pending", StageState(batchLot.Steps, RequestLotTimelineBuilder.StagePo));
        Assert.Equal("completed", StageState(groupLot.Steps, RequestLotTimelineBuilder.StagePo));
        Assert.Equal("current", StageState(groupLot.Steps, RequestLotTimelineBuilder.StagePayment));
    }

    // ── Scenario D: PAYMENT_SCHEDULED + WAITING_PO ──
    [Fact]
    public void PaymentScheduled_PlusWaitingPo_PaymentAndPoStages()
    {
        var request = MakeRequest("PO_PARTIALLY_UPLOADED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PaymentScheduled, "A", po: "ECF10 2026/1"));
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, "B", po: null));

        var lots = RequestLotTimelineBuilder.BuildLots(request, RequestWorkflowProjectionBuilder.Build(request, "MIXED_PROCESSING"));

        var scheduled = lots.Single(l => l.SupplierName == "A");
        var waiting = lots.Single(l => l.SupplierName == "B");
        Assert.Equal("current", StageState(scheduled.Steps, RequestLotTimelineBuilder.StagePayment));
        Assert.Equal("completed", StageState(scheduled.Steps, RequestLotTimelineBuilder.StagePo));
        Assert.Equal("current", StageState(waiting.Steps, RequestLotTimelineBuilder.StagePo));
    }

    // ── Scenario E: COMPLETED + PO_ISSUED ──
    [Fact]
    public void Completed_PlusIssued_CompletedAndPaymentStages()
    {
        var request = MakeRequest("PO_ISSUED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.Completed, "A", po: "ECF10 2026/1"));
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, "B", po: "ECF11 2026/2"));

        var lots = RequestLotTimelineBuilder.BuildLots(request, RequestWorkflowProjectionBuilder.Build(request, "PO_ISSUED"));

        var done = lots.Single(l => l.SupplierName == "A");
        Assert.All(done.Steps, s => Assert.Equal("completed", s.State));
        var issued = lots.Single(l => l.SupplierName == "B");
        Assert.Equal("current", StageState(issued.Steps, RequestLotTimelineBuilder.StagePayment));
    }

    // ── Scenario F: all completed ──
    [Fact]
    public void AllCompleted_AllLotStepsCompleted()
    {
        var request = MakeRequest("COMPLETED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.Completed, "A", po: "ECF10 2026/1"));
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.Completed, "B", po: "ECF11 2026/2"));

        var lots = RequestLotTimelineBuilder.BuildLots(request, RequestWorkflowProjectionBuilder.Build(request, "FULLY_COMPLETED"));

        Assert.Equal(2, lots.Count);
        Assert.All(lots.SelectMany(l => l.Steps), s => Assert.Equal("completed", s.State));
    }

    // ── Scenario G/I: superseded batch + issued group → exactly one lot ──
    [Fact]
    public void SupersededBatch_PlusIssuedGroup_ExactlyOneLot_AtProjectionLevel()
    {
        var request = MakeRequest("PO_ISSUED");
        var group = Group(RequestConstants.PoGroupStatuses.PoIssued, "CASA DO PAPEL", po: "ECF10 2026/251");
        request.PoGroups.Add(group);
        var li = new RequestLineItem { Id = Guid.NewGuid(), RequestPoGroupId = group.Id };
        request.LineItems.Add(li);
        var stale = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment };
        stale.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = stale.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(stale);

        var projection = RequestWorkflowProjectionBuilder.Build(request, "PO_ISSUED");

        // Case 5: exactly ONE group timeline — the stale batch never becomes a second lot,
        // and the single unit now renders the unit-based timeline (≥1-unit rule).
        Assert.Single(projection.Units);
        var lot = Assert.Single(RequestLotTimelineBuilder.BuildLots(request, projection));
        Assert.Equal("GROUP", lot.UnitType);
        Assert.Null(lot.LotNumber); // batchless — never fabricated from the stale batch
        Assert.Equal("current", StageState(lot.Steps, RequestLotTimelineBuilder.StagePayment));
    }

    // ── Decision 2: list badge prefers the unit's truthful label (display only) ──

    /// <summary>Case 6: scalar APPROVED + single PO_ISSUED group → badge override "P.O Emitida".</summary>
    [Fact]
    public void BadgeOverride_ScalarLaggingSingleUnit_PrefersUnitLabel()
    {
        var request = MakeRequest("APPROVED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, "CASA DO PAPEL", po: "5001334395"));
        var projection = RequestWorkflowProjectionBuilder.Build(request, "APPROVED");

        var badge = RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(projection, "APPROVED");

        Assert.NotNull(badge);
        Assert.Equal("PO_ISSUED", badge!.Value.Code);
        Assert.Equal("P.O Emitida", badge.Value.Label);
    }

    /// <summary>Case 11: terminal scalars remain authoritative — no badge override.</summary>
    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("REJECTED")]
    [InlineData("COMPLETED")]
    public void BadgeOverride_TerminalScalar_NeverOverridden(string terminal)
    {
        var request = MakeRequest(terminal);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, po: "ECF10 2026/9"));
        var projection = RequestWorkflowProjectionBuilder.Build(request, terminal);

        Assert.Null(RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(projection, terminal));
    }

    /// <summary>Case 12: no units, multi-unit, or unit agreeing with the scalar → fallback to statusName.</summary>
    [Fact]
    public void BadgeOverride_NoReliableOverride_FallsBackToScalar()
    {
        // No units.
        var empty = MakeRequest("WAITING_QUOTATION");
        Assert.Null(RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(
            RequestWorkflowProjectionBuilder.Build(empty, "WAITING_QUOTATION"), "WAITING_QUOTATION"));

        // Unit agrees with the scalar.
        var agreeing = MakeRequest("PO_ISSUED");
        agreeing.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, po: "ECF10 2026/10"));
        Assert.Null(RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(
            RequestWorkflowProjectionBuilder.Build(agreeing, "PO_ISSUED"), "PO_ISSUED"));

        // Multi-unit rows keep the existing group-aware/display path.
        var multi = MakeRequest("PO_PARTIALLY_UPLOADED");
        multi.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, "A", po: "ECF10 2026/11"));
        multi.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, "B", po: null));
        Assert.Null(RequestWorkflowProjectionBuilder.ResolveSingleUnitBadgeOverride(
            RequestWorkflowProjectionBuilder.Build(multi, "MIXED_PROCESSING"), "PO_PARTIALLY_UPLOADED"));
    }

    // ── Fix-1 dedupe carried over: active batch + its PENDING group = ONE lot ──
    [Fact]
    public void ActiveBatch_PlusItsPendingGroup_NeverTwoLots()
    {
        var request = MakeRequest("PO_ISSUED");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, "A", po: "ECF10 2026/1"));
        var li = new RequestLineItem { Id = Guid.NewGuid() };
        request.LineItems.Add(li);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 2, Status = RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval };
        batch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(batch);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.Pending, "B", po: null, batchId: batch.Id));

        var lots = RequestLotTimelineBuilder.BuildLots(request, RequestWorkflowProjectionBuilder.Build(request, "MIXED_PROCESSING"));

        Assert.Equal(2, lots.Count); // issued group + the batch — NOT three
        Assert.Single(lots, l => l.UnitType == "BATCH");
    }

    // ── Recebimento/Execução vs Documentação Fiscal (Decision C — established v2.229.9
    //    semantics: WAITING_RECEIPT is entered AFTER receiving completes and waits for the
    //    supplier's receipt document, i.e. fiscal-documentation phase) ──
    [Fact]
    public void WaitingReceipt_ReceivingCompleted_FiscalCurrent()
    {
        var steps = StagesFor(RequestConstants.PoGroupStatuses.WaitingReceipt);
        Assert.Equal("completed", StageState(steps, RequestLotTimelineBuilder.StageReceiving));
        Assert.Equal("current", StageState(steps, RequestLotTimelineBuilder.StageFiscal));
        Assert.Equal("completed", StageState(steps, RequestLotTimelineBuilder.StagePayment));
        Assert.Equal("pending", StageState(steps, RequestLotTimelineBuilder.StageCompleted));
    }

    /// <summary>Real REQ-13/07/2026-052 shape: batchless WAITING_RECEIPT after final payment —
    /// Financeiro / fiscal-document semantics preserved end to end.</summary>
    [Fact]
    public void Req052Shape_BatchlessWaitingReceipt_FiscalCurrent_FinanceResponsible()
    {
        var request = MakeRequest("WAITING_RECEIPT");
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingReceipt, "PROPCO", po: null));
        var projection = RequestWorkflowProjectionBuilder.Build(request, "WAITING_RECEIPT");

        var unit = Assert.Single(projection.Units);
        Assert.Equal("Financeiro", unit.ResponsibleRole);
        Assert.Equal("Anexar recibo do fornecedor e finalizar pedido", unit.NextAction!.Label);
        Assert.Equal("COMPLETE", unit.ReceivingState);
        Assert.Equal("WAITING_SUPPLIER_RECEIPT", unit.CompletionState);

        var lot = Assert.Single(RequestLotTimelineBuilder.BuildLots(request, projection));
        Assert.Equal("completed", StageState(lot.Steps, RequestLotTimelineBuilder.StageReceiving));
        Assert.Equal("current", StageState(lot.Steps, RequestLotTimelineBuilder.StageFiscal));
    }

    /// <summary>True receiving/execution states stay in Recebimento/Execução.</summary>
    [Theory]
    [InlineData("WAITING_RECONCILIATION")]
    [InlineData("IN_FOLLOWUP")]
    public void TrueReceivingStates_StayInReceivingStage(string groupStatus)
    {
        var steps = StagesFor(groupStatus);
        Assert.Equal("current", StageState(steps, RequestLotTimelineBuilder.StageReceiving));
        Assert.Equal("pending", StageState(steps, RequestLotTimelineBuilder.StageFiscal));
    }

    [Fact]
    public void WaitingFiscalReceipt_MapsToFiscal_ReceivingCompleted()
    {
        var steps = StagesFor(RequestConstants.PoGroupStatuses.WaitingFiscalReceipt);
        Assert.Equal("completed", StageState(steps, RequestLotTimelineBuilder.StageReceiving));
        Assert.Equal("current", StageState(steps, RequestLotTimelineBuilder.StageFiscal));
        Assert.Equal("pending", StageState(steps, RequestLotTimelineBuilder.StageCompleted));
    }

    [Fact]
    public void WaitingPoCorrection_StaysPoStageWork()
    {
        var steps = StagesFor(RequestConstants.PoGroupStatuses.WaitingPoCorrection, po: null);
        Assert.Equal("current", StageState(steps, RequestLotTimelineBuilder.StagePo));
        Assert.Equal("pending", StageState(steps, RequestLotTimelineBuilder.StagePayment));
    }

    [Fact]
    public void PaymentCompleted_NextApplicableStage_ReceivingCurrent()
    {
        var steps = StagesFor(RequestConstants.PoGroupStatuses.PaymentCompleted);
        Assert.Equal("completed", StageState(steps, RequestLotTimelineBuilder.StagePayment));
        Assert.Equal("current", StageState(steps, RequestLotTimelineBuilder.StageReceiving));
    }

    // ── Advance-payment track ──
    [Fact]
    public void AdvancePaymentScheduled_PaymentCurrent()
    {
        var steps = StagesFor(RequestConstants.PoGroupStatuses.AdvancePaymentScheduled, po: null);
        Assert.Equal("current", StageState(steps, RequestLotTimelineBuilder.StagePayment));
        Assert.Equal("pending", StageState(steps, RequestLotTimelineBuilder.StageReceiving));
    }

    [Fact]
    public void WaitingSupplierDelivery_ReceivingExecutionCurrent()
    {
        var steps = StagesFor(RequestConstants.PoGroupStatuses.WaitingSupplierDelivery, po: "ECF11 2026/3");
        Assert.Equal("current", StageState(steps, RequestLotTimelineBuilder.StageReceiving));
        Assert.Equal("completed", StageState(steps, RequestLotTimelineBuilder.StagePo));
    }

    // ── Batch adjustment keeps blocked/attention semantics ──
    [Fact]
    public void BatchInAdjustment_ApprovalsBlocked()
    {
        var request = MakeRequest("WAITING_AREA_APPROVAL");
        var li = new RequestLineItem { Id = Guid.NewGuid() };
        request.LineItems.Add(li);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 3, Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment };
        batch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = li.Id });
        request.ApprovalBatches.Add(batch);
        var unit = RequestWorkflowProjectionBuilder.Build(request, "WAITING_AREA_APPROVAL").Units.Single();

        var steps = RequestLotTimelineBuilder.MapStages(unit);
        Assert.Equal("blocked", StageState(steps, RequestLotTimelineBuilder.StageApprovals));
    }

    // ── Adjustment 2: lot numbers are real domain identity only ──
    [Fact]
    public void BatchBackedGroup_ResolvesRealLotNumber_Structurally()
    {
        var request = MakeRequest("PO_PARTIALLY_UPLOADED");
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 4, Status = RequestConstants.ApprovalBatchStatuses.Approved };
        request.ApprovalBatches.Add(batch);
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.PoIssued, "A", po: "ECF10 2026/1", batchId: batch.Id));
        request.PoGroups.Add(Group(RequestConstants.PoGroupStatuses.WaitingPo, "B", po: null)); // batchless

        var lots = RequestLotTimelineBuilder.BuildLots(request, RequestWorkflowProjectionBuilder.Build(request, "MIXED_PROCESSING"));

        Assert.Equal(4, lots.Single(l => l.SupplierName == "A").LotNumber);
        Assert.Null(lots.Single(l => l.SupplierName == "B").LotNumber); // never fabricated
    }
}
