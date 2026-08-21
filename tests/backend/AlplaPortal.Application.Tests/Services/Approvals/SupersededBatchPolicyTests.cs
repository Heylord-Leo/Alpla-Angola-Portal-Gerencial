using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// v2.230.0 — superseded-batch detection (REQ-23/07/2026-140 class). Detection only, never a
/// mutation: misclassifying LIVE work as stale is the dangerous direction, so every ambiguous
/// shape must classify as NOT superseded.
/// </summary>
public class SupersededBatchPolicyTests
{
    private static ApprovalBatch Batch(string status, params Guid[] lineItemIds)
    {
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), Status = status, BatchNumber = 1 };
        foreach (var liId in lineItemIds)
            batch.Items.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = liId });
        return batch;
    }

    private static RequestLineItem Item(Guid id, Guid? groupId, bool deleted = false) =>
        new() { Id = id, RequestPoGroupId = groupId, IsDeleted = deleted };

    private static RequestPoGroup Group(Guid id, string status, Guid? batchId = null) =>
        new() { Id = id, Status = status, ApprovalBatchId = batchId };

    [Fact]
    public void Req140Shape_InApprovalBatch_ItemCoveredByBatchlessActiveGroup_IsSuperseded()
    {
        var liId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.AreaAdjustment, liId);
        var items = new List<RequestLineItem> { Item(liId, groupId) };
        var groups = new List<RequestPoGroup> { Group(groupId, RequestConstants.PoGroupStatuses.PoIssued) };

        Assert.True(SupersededBatchPolicy.IsSuperseded(batch, items, groups));
    }

    [Fact]
    public void SettledBatch_IsNeverSuperseded()
    {
        var liId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.Approved, liId);
        var items = new List<RequestLineItem> { Item(liId, groupId) };
        var groups = new List<RequestPoGroup> { Group(groupId, RequestConstants.PoGroupStatuses.PoIssued) };

        Assert.False(SupersededBatchPolicy.IsSuperseded(batch, items, groups));
    }

    [Fact]
    public void ItemNotCoveredByAnyGroup_BatchStaysLive()
    {
        var liId = Guid.NewGuid();
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, liId);
        var items = new List<RequestLineItem> { Item(liId, groupId: null) };

        Assert.False(SupersededBatchPolicy.IsSuperseded(batch, items, new List<RequestPoGroup>()));
    }

    [Fact]
    public void GroupBelongingToTheSameBatch_DoesNotSupersedeIt()
    {
        var liId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval, liId);
        var items = new List<RequestLineItem> { Item(liId, groupId) };
        var groups = new List<RequestPoGroup> { Group(groupId, RequestConstants.PoGroupStatuses.Pending, batchId: batch.Id) };

        Assert.False(SupersededBatchPolicy.IsSuperseded(batch, items, groups));
    }

    [Fact]
    public void CancelledCoveringGroup_DoesNotSupersede()
    {
        var liId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, liId);
        var items = new List<RequestLineItem> { Item(liId, groupId) };
        var groups = new List<RequestPoGroup> { Group(groupId, RequestConstants.PoGroupStatuses.Cancelled) };

        Assert.False(SupersededBatchPolicy.IsSuperseded(batch, items, groups));
    }

    [Fact]
    public void EmptyBatch_FailsOpenToLive()
    {
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), Status = RequestConstants.ApprovalBatchStatuses.AreaAdjustment };
        Assert.False(SupersededBatchPolicy.IsSuperseded(batch, new List<RequestLineItem>(), new List<RequestPoGroup>()));
    }

    [Fact]
    public void MixedCoverage_OneItemUncovered_BatchStaysLive()
    {
        var covered = Guid.NewGuid();
        var uncovered = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var batch = Batch(RequestConstants.ApprovalBatchStatuses.AreaAdjustment, covered, uncovered);
        var items = new List<RequestLineItem> { Item(covered, groupId), Item(uncovered, null) };
        var groups = new List<RequestPoGroup> { Group(groupId, RequestConstants.PoGroupStatuses.PoIssued) };

        Assert.False(SupersededBatchPolicy.IsSuperseded(batch, items, groups));
    }

    [Fact]
    public void PoGate_PreGateStatuses_AreNotCrossed()
    {
        Assert.False(SupersededBatchPolicy.HasCrossedPoGate(Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.Pending)));
        Assert.False(SupersededBatchPolicy.HasCrossedPoGate(Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.WaitingPo)));
        Assert.False(SupersededBatchPolicy.HasCrossedPoGate(Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.WaitingPoCorrection)));
        Assert.True(SupersededBatchPolicy.HasCrossedPoGate(Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.PoIssued)));
        Assert.True(SupersededBatchPolicy.HasCrossedPoGate(Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.AdvancePaymentRequired)));
        Assert.True(SupersededBatchPolicy.HasCrossedPoGate(Group(Guid.NewGuid(), RequestConstants.PoGroupStatuses.Completed)));
    }
}
