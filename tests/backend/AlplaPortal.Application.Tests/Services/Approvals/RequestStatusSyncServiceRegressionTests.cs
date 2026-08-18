using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Regression-loop coverage: once a request has a PO registered on one of several PO groups
/// (Request.Status == PO_PARTIALLY_UPLOADED), a later, unrelated SyncStatusAsync call — e.g.
/// triggered by an unrelated line-item action — must not silently revert the request back to
/// QUOTATION_COMPLETED. This is the exact bug the multi-batch/multi-PO-group status fix closes.
/// </summary>
public class RequestStatusSyncServiceRegressionTests
{
    private static ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);

        context.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Quotation" });
        context.RequestStatuses.AddRange(new List<RequestStatus>
        {
            new() { Id = 1, Code = RequestConstants.Statuses.WaitingFinalApproval, DisplayOrder = 6 },
            new() { Id = 2, Code = RequestConstants.Statuses.QuotationCompleted, DisplayOrder = 21 },
            new() { Id = 3, Code = RequestConstants.Statuses.PoPartiallyUploaded, DisplayOrder = 22 },
            new() { Id = 4, Code = RequestConstants.Statuses.PoIssued, DisplayOrder = 13 },
            new() { Id = 5, Code = RequestConstants.Statuses.PoRequested, DisplayOrder = 12 },
        });
        context.SaveChanges();

        return context;
    }

    [Fact]
    public async Task SyncStatusAsync_DoesNotRegressPoPartiallyUploaded_BackToQuotationCompleted()
    {
        var context = GetInMemoryDbContext();
        var service = new RequestStatusSyncService(context, NullLogger<RequestStatusSyncService>.Instance);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "Multi-group regression test",
            RequestTypeId = 1,
            StatusId = 1 // WAITING_FINAL_APPROVAL
        };

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = 1,
            Status = RequestConstants.ApprovalBatchStatuses.Approved
        };

        var lineItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved
        };

        // Both PO groups start WAITING_PO (mirrors request 100's post-final-approval state).
        var group1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, ApprovalBatchId = batch.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo };
        var group2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, ApprovalBatchId = batch.Id, Status = RequestConstants.PoGroupStatuses.WaitingPo };

        context.Requests.Add(request);
        context.ApprovalBatches.Add(batch);
        context.RequestLineItems.Add(lineItem);
        context.RequestPoGroups.AddRange(group1, group2);
        await context.SaveChangesAsync();

        // Step 1: final approval settles → PO_REQUESTED (v2.229.1: zero of N P.O.s registered
        // is the actionable awaiting-P.O. state, no longer QUOTATION_COMPLETED).
        await service.SyncStatusAsync(request.Id, Guid.NewGuid());
        await context.SaveChangesAsync();

        var afterFirstSync = await context.Requests.Include(r => r.Status).FirstAsync(r => r.Id == request.Id);
        Assert.Equal(RequestConstants.Statuses.PoRequested, afterFirstSync.Status.Code);

        // Step 2: Buyer registers the PO on group1 (simulating RegisterPo's own persistence,
        // which does not go through SyncStatusAsync). One group is now PO_ISSUED, the other is
        // still WAITING_PO — the request is manually moved to PO_PARTIALLY_UPLOADED, exactly as
        // RegisterPo's aggregation would set it.
        group1.Status = RequestConstants.PoGroupStatuses.PoIssued;
        afterFirstSync.StatusId = 3; // PO_PARTIALLY_UPLOADED
        await context.SaveChangesAsync();

        // Step 3: an unrelated trigger fires SyncStatusAsync again (e.g. a not-quoted-item
        // resolution elsewhere on the request). Batches/items haven't changed — this must NOT
        // recompute QUOTATION_COMPLETED and stomp the PO progress already made.
        await service.SyncStatusAsync(request.Id, Guid.NewGuid());
        await context.SaveChangesAsync();

        var afterSecondSync = await context.Requests.Include(r => r.Status).FirstAsync(r => r.Id == request.Id);
        Assert.Equal(RequestConstants.Statuses.PoPartiallyUploaded, afterSecondSync.Status.Code);
        Assert.NotEqual(RequestConstants.Statuses.QuotationCompleted, afterSecondSync.Status.Code);
    }

    [Fact]
    public async Task SyncStatusAsync_AfterBothGroupsIssued_ReflectsPoIssued_NotQuotationCompleted()
    {
        var context = GetInMemoryDbContext();
        var service = new RequestStatusSyncService(context, NullLogger<RequestStatusSyncService>.Instance);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "Both groups issued",
            RequestTypeId = 1,
            StatusId = 3 // PO_PARTIALLY_UPLOADED
        };

        var batch = new ApprovalBatch
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            BatchNumber = 1,
            Status = RequestConstants.ApprovalBatchStatuses.Approved
        };

        var lineItem = new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved
        };

        var group1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, ApprovalBatchId = batch.Id, Status = RequestConstants.PoGroupStatuses.PoIssued };
        var group2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, ApprovalBatchId = batch.Id, Status = RequestConstants.PoGroupStatuses.PoIssued };

        context.Requests.Add(request);
        context.ApprovalBatches.Add(batch);
        context.RequestLineItems.Add(lineItem);
        context.RequestPoGroups.AddRange(group1, group2);
        await context.SaveChangesAsync();

        await service.SyncStatusAsync(request.Id, Guid.NewGuid());
        await context.SaveChangesAsync();

        var updated = await context.Requests.Include(r => r.Status).FirstAsync(r => r.Id == request.Id);
        Assert.Equal(RequestConstants.Statuses.PoIssued, updated.Status.Code);
    }
}
