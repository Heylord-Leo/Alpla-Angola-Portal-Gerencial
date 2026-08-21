using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// v2.230.0 DEV-fix pass — ComputeDisplayWorkflowState corrections:
/// (Fix 2) superseded batches never drive the active display state (REQ-23/07/2026-140 shape
/// must display "P.O Emitida", not "Processamento Parcial");
/// (Fix 3) active groups spanning the PO gate display MIXED_PROCESSING instead of the
/// least-advanced label; same-side-of-gate mixes keep least-advanced (the post-gate mixed
/// vocabulary belongs to RequestGroupDisplayStateCalculator); terminal request states stay
/// authoritative. Pure-method tests — the service instance only carries an unused context.
/// </summary>
public class DisplayWorkflowStateMultiGroupTests
{
    private static RequestStatusSyncService NewService()
    {
        var ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new RequestStatusSyncService(ctx, NullLogger<RequestStatusSyncService>.Instance);
    }

    private static RequestLineItem Item(string qls, Guid? groupId = null) =>
        new() { Id = Guid.NewGuid(), IsDeleted = false, QuotationLifecycleStatus = qls, RequestPoGroupId = groupId };

    private static RequestPoGroup Group(string status, Guid? batchId = null)
    {
        var g = new RequestPoGroup { Id = Guid.NewGuid(), Status = status, ApprovalBatchId = batchId };
        return g;
    }

    private static ApprovalBatch Batch(string status, params Guid[] lineItemIds)
    {
        var b = new ApprovalBatch { Id = Guid.NewGuid(), BatchNumber = 1, Status = status };
        foreach (var id in lineItemIds)
            b.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = b.Id, RequestLineItemId = id });
        return b;
    }

    [Fact]
    public void Req140Shape_SupersededBatch_PlusIssuedGroup_DisplaysPoIssued_NotMixed()
    {
        var group = Group(RequestConstants.PoGroupStatuses.PoIssued);
        var item = Item(RequestConstants.QuotationLifecycleStatuses.BatchAssigned, group.Id);
        var staleBatch = Batch(RequestConstants.ApprovalBatchStatuses.AreaAdjustment, item.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "PO_ISSUED",
            new List<RequestLineItem> { item },
            new List<ApprovalBatch> { staleBatch },
            new List<RequestPoGroup> { group });

        Assert.Equal("PO_ISSUED", result);
    }

    [Fact]
    public void LiveBatch_NotSuperseded_StillProducesMixedProcessing()
    {
        var issuedGroup = Group(RequestConstants.PoGroupStatuses.PoIssued);
        var issuedItem = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, issuedGroup.Id);
        var newItem = Item(RequestConstants.QuotationLifecycleStatuses.BatchAssigned); // not covered
        var liveBatch = Batch(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, newItem.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "PO_ISSUED",
            new List<RequestLineItem> { issuedItem, newItem },
            new List<ApprovalBatch> { liveBatch },
            new List<RequestPoGroup> { issuedGroup });

        Assert.Equal("MIXED_PROCESSING", result);
    }

    [Fact]
    public void GroupsSpanningPoGate_WaitingPo_PlusPaymentScheduled_DisplayMixedProcessing()
    {
        var g1 = Group(RequestConstants.PoGroupStatuses.WaitingPo);
        var g2 = Group(RequestConstants.PoGroupStatuses.PaymentScheduled);
        var i1 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g1.Id);
        var i2 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g2.Id);
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.Approved, i1.Id, i2.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "PO_PARTIALLY_UPLOADED",
            new List<RequestLineItem> { i1, i2 },
            new List<ApprovalBatch> { batch },
            new List<RequestPoGroup> { g1, g2 });

        Assert.Equal("MIXED_PROCESSING", result);
    }

    [Fact]
    public void PostGateOnlyMix_PoIssued_PlusPaymentScheduled_KeepsLeastAdvanced()
    {
        var g1 = Group(RequestConstants.PoGroupStatuses.PoIssued);
        var g2 = Group(RequestConstants.PoGroupStatuses.PaymentScheduled);
        var i1 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g1.Id);
        var i2 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g2.Id);
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.Approved, i1.Id, i2.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "PO_ISSUED",
            new List<RequestLineItem> { i1, i2 },
            new List<ApprovalBatch> { batch },
            new List<RequestPoGroup> { g1, g2 });

        // Post-gate mixed labels are RequestGroupDisplayStateCalculator's vocabulary
        // ("Pagamentos em andamento"); this projection keeps the least-advanced status.
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, result);
    }

    [Fact]
    public void PostGateOnlyMix_PaymentScheduled_PlusWaitingReceipt_KeepsLeastAdvanced()
    {
        var g1 = Group(RequestConstants.PoGroupStatuses.PaymentScheduled);
        var g2 = Group(RequestConstants.PoGroupStatuses.WaitingReceipt);
        var i1 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g1.Id);
        var i2 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g2.Id);
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.Approved, i1.Id, i2.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "PAYMENT_SCHEDULED",
            new List<RequestLineItem> { i1, i2 },
            new List<ApprovalBatch> { batch },
            new List<RequestPoGroup> { g1, g2 });

        Assert.Equal(RequestConstants.PoGroupStatuses.PaymentScheduled, result);
    }

    [Fact]
    public void IdenticalStatusGroups_AreNeverMixed()
    {
        var g1 = Group(RequestConstants.PoGroupStatuses.WaitingPo);
        var g2 = Group(RequestConstants.PoGroupStatuses.WaitingPo);
        var i1 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g1.Id);
        var i2 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g2.Id);
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.Approved, i1.Id, i2.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "PO_REQUESTED",
            new List<RequestLineItem> { i1, i2 },
            new List<ApprovalBatch> { batch },
            new List<RequestPoGroup> { g1, g2 });

        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo, result);
    }

    [Fact]
    public void AllCompleted_DisplayUnchanged()
    {
        var g1 = Group(RequestConstants.PoGroupStatuses.Completed);
        var g2 = Group(RequestConstants.PoGroupStatuses.Completed);
        var i1 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g1.Id);
        var i2 = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, g2.Id);
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.Approved, i1.Id, i2.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "COMPLETED",
            new List<RequestLineItem> { i1, i2 },
            new List<ApprovalBatch> { batch },
            new List<RequestPoGroup> { g1, g2 });

        Assert.Equal("FULLY_COMPLETED", result);
    }

    [Fact]
    public void CancelledRequest_DisplayStaysCancelled_EvenWithHistoricalGroups()
    {
        var group = Group(RequestConstants.PoGroupStatuses.PoIssued);
        var item = Item(RequestConstants.QuotationLifecycleStatuses.QuotationApproved, group.Id);
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.Approved, item.Id);

        var result = NewService().ComputeDisplayWorkflowState(
            RequestConstants.Types.Quotation, "CANCELLED",
            new List<RequestLineItem> { item },
            new List<ApprovalBatch> { batch },
            new List<RequestPoGroup> { group });

        Assert.Equal("CANCELLED", result);
    }
}
