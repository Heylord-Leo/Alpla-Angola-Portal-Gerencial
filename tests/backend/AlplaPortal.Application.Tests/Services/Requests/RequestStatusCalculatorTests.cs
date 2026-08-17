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
}
