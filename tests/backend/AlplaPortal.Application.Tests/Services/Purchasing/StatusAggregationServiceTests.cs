using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.EntityFrameworkCore;
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
            new RequestStatus { Id = 4, Code = RequestConstants.Statuses.PaymentRequestSent, DisplayOrder = 70 }
        };
        context.RequestStatuses.AddRange(statuses);
        context.SaveChanges();
        
        return context;
    }

    [Fact]
    public async Task GetAggregateStatusIdAsync_ReturnsFurthestBehindStatus()
    {
        // Arrange
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            StatusId = 1 // FinalApproved
        };

        var group1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.PoIssued }; // PoIssued (60)
        var group2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.WaitingPoCorrection }; // WaitingPoCorrection (55)

        context.Requests.Add(request);
        context.RequestPoGroups.Add(group1);
        context.RequestPoGroups.Add(group2);
        await context.SaveChangesAsync();

        // Act
        await service.AggregateRequestStatusAsync(request.Id);

        // Assert
        // Should return the status with lowest DisplayOrder among the groups (WaitingPoCorrection = 55 = Id 2)
        var updatedRequest = await context.Requests.FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.NotNull(updatedRequest);
        Assert.Equal(2, updatedRequest.StatusId); // WaitingPoCorrection has DisplayOrder=20 which is lowest
    }

    [Fact]
    public async Task GetAggregateStatusIdAsync_ReturnsPaymentBottleneck_WhenOneGroupReceivedAndOneWaitingPayment()
    {
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context);

        var request = new Request { Id = Guid.NewGuid(), Title = "Test", StatusId = 1 };
        var group1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.WaitingReceipt }; // WaitingReceipt (priority 70)
        var group2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, Status = RequestConstants.Statuses.PaymentRequestSent }; // PaymentRequestSent (priority 40)

        context.Requests.Add(request);
        context.RequestPoGroups.AddRange(group1, group2);
        await context.SaveChangesAsync();

        await service.AggregateRequestStatusAsync(request.Id);

        var updatedRequest = await context.Requests.FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.Equal(4, updatedRequest.StatusId); // PaymentRequestSent
    }

    [Fact]
    public async Task GetAggregateStatusIdAsync_IgnoresCancelledGroups()
    {
        var context = GetInMemoryDbContext();
        var service = new StatusAggregationService(context);

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

        await service.AggregateRequestStatusAsync(request.Id);

        var updatedRequest = await context.Requests.FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.Equal(17, updatedRequest.StatusId); // Completed
    }
}
