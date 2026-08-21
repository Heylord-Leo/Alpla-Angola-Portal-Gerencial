using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

public class StatusAggregationServiceTests
{
    private ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);
        
        // Seed RequestStatus lookup
        var statuses = new List<RequestStatus>
        {
            new RequestStatus { Id = 1, Code = RequestConstants.Statuses.FinalApproved, DisplayOrder = 50 },
            new RequestStatus { Id = 2, Code = RequestConstants.Statuses.WaitingPoCorrection, DisplayOrder = 55 },
            new RequestStatus { Id = 3, Code = RequestConstants.Statuses.PoIssued, DisplayOrder = 60 },
            new RequestStatus { Id = 4, Code = RequestConstants.Statuses.PaymentRequestSent, DisplayOrder = 70 },
            new RequestStatus { Id = 5, Code = RequestConstants.Statuses.PoPartiallyUploaded, DisplayOrder = 22 }
        };
        context.RequestStatuses.AddRange(statuses);
        context.SaveChanges();
        
        return context;
    }

    [Fact]
    public async Task GetAggregateStatusIdAsync_ReturnsPoPartiallyUploaded_WhenOneGroupIssuedAndOneNeedsCorrection()
    {
        // Arrange
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context, NullLogger<StatusAggregationService>.Instance);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            StatusId = 1 // FinalApproved
        };

        var group1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.PoIssued };
        var group2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.WaitingPoCorrection };

        context.Requests.Add(request);
        context.RequestPoGroups.Add(group1);
        context.RequestPoGroups.Add(group2);
        await context.SaveChangesAsync();

        // Act
        await service.AggregateRequestStatusAsync(request.Id, Guid.NewGuid());

        // Assert
        // One group already issued its PO, the other still needs a PO (correction) — the request
        // must reflect the mixed state (PO_PARTIALLY_UPLOADED), not collapse to either single
        // group's own status.
        var updatedRequest = await context.Requests.FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.NotNull(updatedRequest);
        Assert.Equal(5, updatedRequest.StatusId); // PoPartiallyUploaded
    }

    [Fact]
    public async Task GetAggregateStatusIdAsync_ReturnsPaymentBottleneck_WhenOneGroupReceivedAndOneWaitingPayment()
    {
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context, NullLogger<StatusAggregationService>.Instance);

        var request = new Request { Id = Guid.NewGuid(), Title = "Test", StatusId = 1 };
        var group1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.WaitingReceipt }; // WaitingReceipt (priority 70)
        var group2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.PaymentRequestSent }; // PaymentRequestSent (priority 40)

        context.Requests.Add(request);
        context.RequestPoGroups.AddRange(group1, group2);
        await context.SaveChangesAsync();

        await service.AggregateRequestStatusAsync(request.Id, Guid.NewGuid());

        var updatedRequest = await context.Requests.FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.Equal(4, updatedRequest.StatusId); // PaymentRequestSent
    }

    [Fact]
    public async Task GetAggregateStatusIdAsync_IgnoresCancelledGroups()
    {
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context, NullLogger<StatusAggregationService>.Instance);

        var request = new Request { Id = Guid.NewGuid(), Title = "Test", StatusId = 1 };
        var group1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.Cancelled }; // Cancelled (priority 999)
        var group2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.Completed }; // Completed (priority 100)

        context.Requests.Add(request);
        context.RequestPoGroups.AddRange(group1, group2);
        
        // Add required statuses if not present
        if (!context.RequestStatuses.Any(s => s.Code == RequestConstants.Statuses.Completed))
        {
            context.RequestStatuses.Add(new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, DisplayOrder = 19 });
        }
        if (!context.RequestStatuses.Any(s => s.Code == RequestConstants.Statuses.Cancelled))
        {
            context.RequestStatuses.Add(new RequestStatus { Id = 18, Code = RequestConstants.Statuses.Cancelled, DisplayOrder = 20 });
        }
        
        await context.SaveChangesAsync();

        await service.AggregateRequestStatusAsync(request.Id, Guid.NewGuid());

        var updatedRequest = await context.Requests.FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.Equal(17, updatedRequest.StatusId); // Completed
    }

    // ── v2.230.0: no silent aggregate writers (REQ-23/07/2026-140) ──

    [Fact]
    public async Task EveryAggregateTransition_WritesAnAuditedStatusSyncHistoryRow()
    {
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context, NullLogger<StatusAggregationService>.Instance);
        var actorId = Guid.NewGuid();

        var request = new Request { Id = Guid.NewGuid(), Title = "Test", StatusId = 1 }; // FinalApproved
        var group = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.PoIssued };
        context.Requests.Add(request);
        context.RequestPoGroups.Add(group);
        await context.SaveChangesAsync();

        await service.AggregateRequestStatusAsync(request.Id, actorId);

        var history = Assert.Single(await context.RequestStatusHistories
            .Where(h => h.RequestId == request.Id).ToListAsync());
        Assert.Equal("STATUS_SYNC", history.ActionTaken);
        Assert.Equal(actorId, history.ActorUserId);
        Assert.Equal(1, history.PreviousStatusId);
        Assert.Equal(3, history.NewStatusId); // PoIssued
    }

    [Fact]
    public async Task NoTransition_WritesNoHistoryRow()
    {
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context, NullLogger<StatusAggregationService>.Instance);

        var request = new Request { Id = Guid.NewGuid(), Title = "Test", StatusId = 3 }; // already PoIssued
        var group = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.PoIssued };
        context.Requests.Add(request);
        context.RequestPoGroups.Add(group);
        await context.SaveChangesAsync();

        await service.AggregateRequestStatusAsync(request.Id, Guid.NewGuid());

        Assert.Empty(await context.RequestStatusHistories.Where(h => h.RequestId == request.Id).ToListAsync());
    }
}
