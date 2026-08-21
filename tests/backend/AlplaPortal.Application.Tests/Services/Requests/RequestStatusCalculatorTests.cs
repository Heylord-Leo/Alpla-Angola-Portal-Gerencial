using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

public class RequestStatusCalculatorTests
{
    private static Request MakeRequest(string currentStatusCode = "WAITING_FINAL_APPROVAL")
    {
        return new Request
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Status = new RequestStatus { Id = 1, Code = currentStatusCode }
        };
    }

    private static ApprovalBatch MakeBatch(string status) =>
        new() { Id = Guid.NewGuid(), Status = status };

    private static RequestPoGroup MakeGroup(string status, Guid? batchId = null) =>
        new() { Id = Guid.NewGuid(), Status = status, ApprovalBatchId = batchId };

    private static RequestLineItem MakeItem(string? lifecycleStatus, bool isDeleted = false) =>
        new() { Id = Guid.NewGuid(), IsDeleted = isDeleted, QuotationLifecycleStatus = lifecycleStatus };

    // ── Phase 1 boundary ──

    [Fact]
    public void NoBatchesNoGroups_PreservesCurrentStatus()
    {
        var request = MakeRequest("WAITING_QUOTATION");

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal("WAITING_QUOTATION", result.StatusCode);
        Assert.Null(result.IssueCode);
    }

    [Fact]
    public void PendingItems_KeepsWaitingQuotation_EvenIfBatchesApproved()
    {
        var request = MakeRequest("APPROVED");
        request.ApprovalBatches.Add(MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved));
        request.LineItems.Add(MakeItem(null)); // null → treated as QUOTATION_PENDING

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.WaitingQuotation, result.StatusCode);
    }

    [Fact]
    public void BatchStillInFinalApproval_GroupsAlreadyPending_Phase1Governs()
    {
        // Mixed multi-batch request: one batch already final-approved (its groups activated to
        // WAITING_PO), another batch still awaiting final approval (its groups still PENDING).
        var request = MakeRequest("WAITING_FINAL_APPROVAL");
        var approvedBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        var pendingBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        request.ApprovalBatches.Add(approvedBatch);
        request.ApprovalBatches.Add(pendingBatch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, approvedBatch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.Pending, pendingBatch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.WaitingFinalApproval, result.StatusCode);
        Assert.Null(result.IssueCode);
    }

    [Fact]
    public void AllBatchesRejected_DoesNotReturnQuotationCompleted()
    {
        var request = MakeRequest("WAITING_FINAL_APPROVAL");
        request.ApprovalBatches.Add(MakeBatch(RequestConstants.ApprovalBatchStatuses.Rejected));
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal("WAITING_FINAL_APPROVAL", result.StatusCode); // preserved, not QUOTATION_COMPLETED
        Assert.NotEqual(RequestConstants.Statuses.QuotationCompleted, result.StatusCode);
    }

    [Fact]
    public void OneApprovedOneRejectedBatch_AggregatesOnlyApprovedBatchGroups()
    {
        var request = MakeRequest("WAITING_FINAL_APPROVAL");
        var approvedBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        var rejectedBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Rejected);
        request.ApprovalBatches.Add(approvedBatch);
        request.ApprovalBatches.Add(rejectedBatch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, approvedBatch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        // v2.229.1: zero of N P.O.s registered is the actionable awaiting-P.O. state.
        Assert.Equal(RequestConstants.Statuses.PoRequested, result.StatusCode);
        Assert.Null(result.IssueCode);
    }

    [Fact]
    public void BatchesApprovedButNoPoGroups_ReturnsQuotationCompleted()
    {
        var request = MakeRequest("WAITING_FINAL_APPROVAL");
        request.ApprovalBatches.Add(MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved));
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.QuotationCompleted, result.StatusCode);
    }

    // ── Phase 2: required scenario coverage ──

    [Fact]
    public void AllGroupsWaitingPo_ReturnsPoRequested()
    {
        // v2.229.1 (REQ-17/08/2026-232): previously QUOTATION_COMPLETED — technically true,
        // operationally misleading (the request is actively waiting for the Buyer's first P.O.).
        var request = MakeRequest("WAITING_FINAL_APPROVAL");
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoRequested, result.StatusCode);
        Assert.Null(result.IssueCode);
    }

    [Fact]
    public void OneGroupPoIssuedOneWaitingPo_ReturnsPoPartiallyUploaded()
    {
        var request = MakeRequest(RequestConstants.Statuses.QuotationCompleted);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoPartiallyUploaded, result.StatusCode);
    }

    [Fact]
    public void AllGroupsPoIssued_ReturnsPoIssued()
    {
        var request = MakeRequest(RequestConstants.Statuses.PoPartiallyUploaded);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoIssued, result.StatusCode);
    }

    [Fact]
    public void OnePaymentCompletedOnePoIssued_ReturnsPoIssued_FurthestBehindWins()
    {
        var request = MakeRequest(RequestConstants.Statuses.PoIssued);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PaymentCompleted, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoIssued, result.StatusCode);
    }

    [Fact]
    public void OneWaitingReceiptOnePaymentScheduled_ReturnsPaymentScheduled()
    {
        var request = MakeRequest(RequestConstants.Statuses.PaymentScheduled);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingReceipt, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PaymentScheduled, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PaymentScheduled, result.StatusCode);
    }

    [Fact]
    public void AllGroupsCompleted_ReturnsCompleted()
    {
        var request = MakeRequest(RequestConstants.Statuses.WaitingReceipt);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.Completed, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.Completed, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.Completed, result.StatusCode);
    }

    [Fact]
    public void CancelledGroupsAreExcluded_HealthyGroupDrivesResult()
    {
        var request = MakeRequest(RequestConstants.Statuses.QuotationCompleted);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.Cancelled, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        // v2.229.1: the healthy WAITING_PO group makes this an awaiting-P.O. request.
        Assert.Equal(RequestConstants.Statuses.PoRequested, result.StatusCode);
        Assert.Null(result.IssueCode);
    }

    [Fact]
    public void AllGroupsCancelled_PreservesCurrentStatus()
    {
        var request = MakeRequest("SOME_STATUS");
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.Cancelled, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal("SOME_STATUS", result.StatusCode);
        Assert.Null(result.IssueCode);
    }

    [Fact]
    public void AdvancePaymentStates_OrderedBeforePoIssued()
    {
        var request = MakeRequest(RequestConstants.Statuses.PoIssued);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.AdvancePaymentRequired, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, result.StatusCode);
    }

    [Fact]
    public void WaitingPoCorrection_Uniform_ReturnsWaitingPoCorrection()
    {
        var request = MakeRequest(RequestConstants.Statuses.QuotationCompleted);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPoCorrection, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.WaitingPoCorrection, result.StatusCode);
    }

    [Fact]
    public void PriorityMap_WaitingPoNeverOutrankedByPoIssued()
    {
        // Regression guard for the old StatusAggregationService bug: WAITING_PO (unmapped in the
        // old priority table) must never be treated as "more advanced" than PO_ISSUED.
        var request = MakeRequest(RequestConstants.Statuses.PoIssued);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, batch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoPartiallyUploaded, result.StatusCode);
        Assert.NotEqual(RequestConstants.Statuses.PoIssued, result.StatusCode);
    }

    // ── Conservative PENDING handling (user-directed correction) ──

    [Fact]
    public void ApprovedBatch_PendingPlusWaitingPo_PreservesStatus_DoesNotAdvanceFromHealthySibling()
    {
        var request = MakeRequest(RequestConstants.Statuses.WaitingFinalApproval);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        var pendingGroup = MakeGroup(RequestConstants.PoGroupStatuses.Pending, batch.Id);
        request.PoGroups.Add(pendingGroup);
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, batch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.WaitingFinalApproval, result.StatusCode); // preserved, not QUOTATION_COMPLETED
        Assert.Equal(RequestStatusIssueCode.UnexpectedPendingPoGroups, result.IssueCode);
        Assert.Equal(new[] { pendingGroup.Id }, result.AffectedPoGroupIds);
    }

    [Fact]
    public void ApprovedBatch_OnlyPending_PreservesStatusAndReturnsIssue()
    {
        var request = MakeRequest(RequestConstants.Statuses.WaitingFinalApproval);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        var pendingGroup = MakeGroup(RequestConstants.PoGroupStatuses.Pending, batch.Id);
        request.PoGroups.Add(pendingGroup);

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.WaitingFinalApproval, result.StatusCode);
        Assert.Equal(RequestStatusIssueCode.UnexpectedPendingPoGroups, result.IssueCode);
        Assert.Single(result.AffectedPoGroupIds!);
        Assert.Equal(pendingGroup.Id, result.AffectedPoGroupIds![0]);
    }

    [Fact]
    public void RejectedBatch_AnomalousPendingGroup_ExcludedWithoutIssue()
    {
        var request = MakeRequest(RequestConstants.Statuses.WaitingFinalApproval);
        var rejectedBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Rejected);
        var approvedBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(rejectedBatch);
        request.ApprovalBatches.Add(approvedBatch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        // Anomalous: a PENDING group still linked to the rejected batch (normally deleted by
        // BatchAreaReject/BatchFinalReject's defensive cleanup).
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.Pending, rejectedBatch.Id));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo, approvedBatch.Id));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoRequested, result.StatusCode);
        Assert.Null(result.IssueCode); // excluded by batch filter before the PENDING check runs
    }

    [Fact]
    public void CancelledGroupPlusPendingGroup_StillRaisesIssue()
    {
        var request = MakeRequest(RequestConstants.Statuses.WaitingFinalApproval);
        var batch = MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved);
        request.ApprovalBatches.Add(batch);
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved));
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.Cancelled, batch.Id));
        var pendingGroup = MakeGroup(RequestConstants.PoGroupStatuses.Pending, batch.Id);
        request.PoGroups.Add(pendingGroup);

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestStatusIssueCode.UnexpectedPendingPoGroups, result.IssueCode);
        Assert.Equal(new[] { pendingGroup.Id }, result.AffectedPoGroupIds);
    }

    // ── Batchless group workflow (e.g. PAYMENT-type single auto-created group) ──

    [Fact]
    public void BatchlessGroup_WaitingPo_PreservesStatus_DoesNotReturnQuotationCompleted()
    {
        var request = MakeRequest("APPROVED");
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal("APPROVED", result.StatusCode);
        Assert.NotEqual(RequestConstants.Statuses.QuotationCompleted, result.StatusCode);
    }

    [Fact]
    public void BatchlessGroup_PoIssued_ReturnsPoIssued()
    {
        var request = MakeRequest("APPROVED");
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoIssued, result.StatusCode);
    }

    // ── v2.230.0: PO-gate floor + superseded/cancelled batch exclusion (REQ-140 class) ──

    /// <summary>
    /// The exact REQ-23/07/2026-140 shape: an abandoned AREA_ADJUSTMENT batch whose only item
    /// was processed by the legacy batchless path into a PO_ISSUED group. Before the floor, the
    /// batch's Phase-1 authority regressed the request to WAITING_AREA_APPROVAL (silently) and a
    /// re-approval overwrote PO_ISSUED with APPROVED. The aggregate must stay PO_ISSUED.
    /// </summary>
    [Fact]
    public void Req140Shape_AbandonedBatch_PlusBatchlessIssuedGroup_StaysPoIssued()
    {
        var request = MakeRequest("PO_ISSUED");
        var group = MakeGroup(RequestConstants.PoGroupStatuses.PoIssued);
        request.PoGroups.Add(group);

        var item = MakeItem(RequestConstants.QuotationLifecycleStatuses.BatchAssigned);
        item.RequestPoGroupId = group.Id;
        request.LineItems.Add(item);

        var staleBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.AreaAdjustment);
        staleBatch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = staleBatch.Id, RequestLineItemId = item.Id });
        request.ApprovalBatches.Add(staleBatch);

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PoIssued, result.StatusCode);
    }

    /// <summary>
    /// Multi-group: Group A already PO_ISSUED, a LIVE second batch still awaiting final
    /// approval for other items. The persisted scalar must not regress to
    /// WAITING_FINAL_APPROVAL — the in-flight wave surfaces via the display projection
    /// (MIXED_PROCESSING), never via the compatibility aggregate.
    /// </summary>
    [Fact]
    public void PoGateFloor_LiveSecondBatchInApproval_DoesNotRegressScalar()
    {
        var request = MakeRequest("PO_ISSUED");
        var issuedGroup = MakeGroup(RequestConstants.PoGroupStatuses.PoIssued);
        request.PoGroups.Add(issuedGroup);

        var issuedItem = MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
        issuedItem.RequestPoGroupId = issuedGroup.Id;
        request.LineItems.Add(issuedItem);

        // Second, genuinely live wave: its item is NOT covered by any group yet.
        var pendingItem = MakeItem(RequestConstants.QuotationLifecycleStatuses.BatchAssigned);
        request.LineItems.Add(pendingItem);
        var liveBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        liveBatch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = liveBatch.Id, RequestLineItemId = pendingItem.Id });
        request.ApprovalBatches.Add(liveBatch);

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.NotEqual(RequestConstants.Statuses.WaitingFinalApproval, result.StatusCode);
        Assert.Equal(RequestConstants.Statuses.PoIssued, result.StatusCode);
    }

    /// <summary>Pending items never drag an issued request back to WAITING_QUOTATION.</summary>
    [Fact]
    public void PoGateFloor_PendingItems_DoNotRegressToWaitingQuotation()
    {
        var request = MakeRequest("PO_ISSUED");
        request.PoGroups.Add(MakeGroup(RequestConstants.PoGroupStatuses.PoIssued));
        request.LineItems.Add(MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationPending));
        request.ApprovalBatches.Add(MakeBatch(RequestConstants.ApprovalBatchStatuses.Approved));

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.NotEqual(RequestConstants.Statuses.WaitingQuotation, result.StatusCode);
    }

    /// <summary>
    /// Pre-gate: a superseded batch (items covered by an active WAITING_PO group from another
    /// path) must not hold the request in WAITING_AREA_APPROVAL — the WAITING_PO group drives.
    /// </summary>
    [Fact]
    public void SupersededBatch_PreGate_DoesNotHoldRequestInApproval()
    {
        var request = MakeRequest("WAITING_AREA_APPROVAL");
        var group = MakeGroup(RequestConstants.PoGroupStatuses.WaitingPo);
        request.PoGroups.Add(group);

        var item = MakeItem(RequestConstants.QuotationLifecycleStatuses.QuotationApproved);
        item.RequestPoGroupId = group.Id;
        request.LineItems.Add(item);

        var staleBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval);
        staleBatch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = staleBatch.Id, RequestLineItemId = item.Id });
        request.ApprovalBatches.Add(staleBatch);

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.NotEqual(RequestConstants.Statuses.WaitingAreaApproval, result.StatusCode);
    }

    /// <summary>Payment lifecycle states are protected by the same floor.</summary>
    [Fact]
    public void PoGateFloor_PaymentScheduledGroup_IgnoresInApprovalBatch()
    {
        var request = MakeRequest("PAYMENT_SCHEDULED");
        var group = MakeGroup(RequestConstants.PoGroupStatuses.PaymentScheduled);
        request.PoGroups.Add(group);

        var newItem = MakeItem(RequestConstants.QuotationLifecycleStatuses.BatchAssigned);
        request.LineItems.Add(newItem);
        var liveBatch = MakeBatch(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval);
        liveBatch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = liveBatch.Id, RequestLineItemId = newItem.Id });
        request.ApprovalBatches.Add(liveBatch);

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.PaymentScheduled, result.StatusCode);
    }
}
