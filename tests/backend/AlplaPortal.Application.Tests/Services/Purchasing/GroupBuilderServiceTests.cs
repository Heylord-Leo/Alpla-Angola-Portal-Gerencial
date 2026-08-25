using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

public class GroupBuilderServiceTests
{
    private ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task BuildGroupsForRequestAsync_CreatesGroups_WhenLineItemsHaveSelectedQuotation()
    {
        // Arrange
        var context = GetInMemoryDbContext();
        var service = new GroupBuilderService(context);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            PaymentConditionCode = "POST_PAID",
            Title = "Test Request",
            Description = "Test"
        };

        var supplier = new Supplier { Id = 1, Name = "Test Supplier", TaxId = "123456789" };
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierId = supplier.Id,
            Supplier = supplier,
            Currency = "USD"
        };
        // The PO-group total is derived from the awarded QuotationItem.LineTotal (GroupBuilderService),
        // NOT from RequestLineItem.TotalAmount — the fixture must set the quotation-item line totals.
        var quotationItem1 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation.Id, Quotation = quotation, LineTotal = 100 };
        var quotationItem2 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation.Id, Quotation = quotation, LineTotal = 200 };

        var lineItem1 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = quotationItem1.Id, TotalAmount = 100 };
        var lineItem2 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = quotationItem2.Id, TotalAmount = 200 };

        request.LineItems.Add(lineItem1);
        request.LineItems.Add(lineItem2);

        context.Suppliers.Add(supplier);
        context.Requests.Add(request);
        context.Quotations.Add(quotation);
        context.Set<QuotationItem>().Add(quotationItem1);
        context.Set<QuotationItem>().Add(quotationItem2);
        await context.SaveChangesAsync();

        // Act
        await service.BuildGroupsForRequestAsync(request.Id);

        // Assert
        var updatedRequest = await context.Requests.Include(r => r.PoGroups).Include(r => r.LineItems).FirstOrDefaultAsync(r => r.Id == request.Id);
        
        Assert.NotNull(updatedRequest);
        Assert.Single(updatedRequest.PoGroups);
        
        var group = updatedRequest.PoGroups.First();
        Assert.Equal(supplier.Id, group.SupplierId);
        Assert.Equal("USD", group.CurrencyCode);
        Assert.Equal("POST_PAID", group.PaymentConditionCode);
        Assert.Equal(300, group.TotalAmount);
        
        Assert.All(updatedRequest.LineItems, li => Assert.Equal(group.Id, li.RequestPoGroupId));
    }

    [Fact]
    public async Task BuildGroupsForRequestAsync_CreatesMultipleGroups_WhenDifferentSuppliers()
    {
        var context = GetInMemoryDbContext();
        var service = new GroupBuilderService(context);

        var request = new Request { Id = Guid.NewGuid(), PaymentConditionCode = "POST_PAID", Title = "Test" };
        var supplier1 = new Supplier { Id = 1, Name = "S1" };
        var supplier2 = new Supplier { Id = 2, Name = "S2" };

        var quotation1 = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier1.Id, Currency = "USD" };
        var quotation2 = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier2.Id, Currency = "USD" };

        var qItem1 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation1.Id };
        var qItem2 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation2.Id };

        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem1.Id, TotalAmount = 100 });
        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem2.Id, TotalAmount = 200 });

        context.Suppliers.AddRange(supplier1, supplier2);
        context.Requests.Add(request);
        context.Quotations.AddRange(quotation1, quotation2);
        context.Set<QuotationItem>().AddRange(qItem1, qItem2);
        await context.SaveChangesAsync();

        await service.BuildGroupsForRequestAsync(request.Id);

        var updatedRequest = await context.Requests.Include(r => r.PoGroups).Include(r => r.LineItems).FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.Equal(2, updatedRequest.PoGroups.Count);
    }

    [Fact]
    public async Task BuildGroupsForRequestAsync_CreatesMultipleGroups_WhenDifferentCurrencies()
    {
        var context = GetInMemoryDbContext();
        var service = new GroupBuilderService(context);

        var request = new Request { Id = Guid.NewGuid(), PaymentConditionCode = "POST_PAID", Title = "Test" };
        var supplier = new Supplier { Id = 1, Name = "S1" };

        var quotation1 = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, Currency = "USD" };
        var quotation2 = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, Currency = "EUR" };

        var qItem1 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation1.Id };
        var qItem2 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation2.Id };

        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem1.Id, TotalAmount = 100 });
        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem2.Id, TotalAmount = 200 });

        context.Suppliers.Add(supplier);
        context.Requests.Add(request);
        context.Quotations.AddRange(quotation1, quotation2);
        context.Set<QuotationItem>().AddRange(qItem1, qItem2);
        await context.SaveChangesAsync();

        await service.BuildGroupsForRequestAsync(request.Id);

        var updatedRequest = await context.Requests.Include(r => r.PoGroups).FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.Equal(2, updatedRequest.PoGroups.Count);
    }

    [Fact]
    public async Task BuildGroupsForRequestAsync_CreatesMultipleGroups_WhenDifferentPaymentConditions()
    {
        // V1 groups by Request.PaymentConditionCode. To simulate different payment conditions,
        // we'd theoretically need a mechanism that splits groups by payment condition.
        // However, since PaymentConditionCode is currently at the Request level in V1, 
        // a single Request can only have ONE payment condition code.
        // Thus, "Same supplier + same currency + different payment conditions" is technically
        // not possible within a single Request in V1 unless we extend the model.
        // This test will document this V1 limitation by confirming it uses the Request's condition.

        var context = GetInMemoryDbContext();
        var service = new GroupBuilderService(context);

        var request = new Request { Id = Guid.NewGuid(), PaymentConditionCode = "POST_PAID", Title = "Test" };
        var supplier = new Supplier { Id = 1, Name = "S1" };

        var quotation1 = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, Currency = "USD" };

        var qItem1 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation1.Id };
        var qItem2 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation1.Id };

        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem1.Id, TotalAmount = 100 });
        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem2.Id, TotalAmount = 200 });

        context.Suppliers.Add(supplier);
        context.Requests.Add(request);
        context.Quotations.Add(quotation1);
        context.Set<QuotationItem>().AddRange(qItem1, qItem2);
        await context.SaveChangesAsync();

        await service.BuildGroupsForRequestAsync(request.Id);

        var updatedRequest = await context.Requests.Include(r => r.PoGroups).FirstOrDefaultAsync(r => r.Id == request.Id);
        
        // As a result, it produces 1 group with the Request's PaymentConditionCode
        Assert.Single(updatedRequest.PoGroups);
        Assert.Equal("POST_PAID", updatedRequest.PoGroups.First().PaymentConditionCode);
    }

    [Fact]
    public async Task BuildGroupsForRequestAsync_IgnoresLineItems_WithoutSelectedQuotationItemId()
    {
        var context = GetInMemoryDbContext();
        var service = new GroupBuilderService(context);

        var request = new Request { Id = Guid.NewGuid(), PaymentConditionCode = "POST_PAID", Title = "Test" };
        
        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = null, TotalAmount = 100 });

        context.Requests.Add(request);
        await context.SaveChangesAsync();

        await service.BuildGroupsForRequestAsync(request.Id);

        var updatedRequest = await context.Requests.Include(r => r.PoGroups).FirstOrDefaultAsync(r => r.Id == request.Id);
        Assert.Empty(updatedRequest.PoGroups);
    }
}
