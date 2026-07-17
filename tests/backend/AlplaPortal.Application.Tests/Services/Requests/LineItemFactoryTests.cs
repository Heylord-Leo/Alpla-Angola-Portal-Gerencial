using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

public class LineItemFactoryTests
{
    private static ApplicationDbContext NewContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Request NewRequest(string typeCode, int? currencyId = 3, int? plantId = 1, Guid? supplierRequestId = null, int? supplierId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            RequestType = new RequestType { Id = typeCode == "QUOTATION" ? 1 : 2, Code = typeCode, Name = typeCode },
            CurrencyId = currencyId,
            PlantId = plantId,
            SupplierId = supplierId,
            StatusId = 1,
            LineItems = new System.Collections.Generic.List<RequestLineItem>()
        };

    [Fact]
    public async Task Build_AssignsSequentialLineNumber_AndStagesHistory()
    {
        using var ctx = NewContext();
        var factory = new LineItemFactory(ctx);
        var request = NewRequest("QUOTATION");
        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), LineNumber = 1, Description = "existing" });

        var actor = Guid.NewGuid();
        var item = await factory.BuildAndStageAsync(request, new LineItemCreationSpec
        {
            Description = "novo item",
            Quantity = 2,
            UnitId = 5,
            UnitPrice = 10m,
            HistoryAction = LineItemHistoryActions.ItemAddedFromProforma
        }, actor, "Comprador Teste");
        await ctx.SaveChangesAsync();

        Assert.Equal(2, item.LineNumber);
        Assert.Equal(1, ctx.RequestLineItems.Count(li => li.Description == "novo item"));
        var history = ctx.RequestStatusHistories.Single();
        Assert.Equal(LineItemHistoryActions.ItemAddedFromProforma, history.ActionTaken);
        Assert.Equal(actor, history.ActorUserId);
    }

    [Fact]
    public async Task Build_ComputesTotalWithDiscountAndIva()
    {
        using var ctx = NewContext();
        ctx.IvaRates.Add(new IvaRate { Id = 7, Name = "IVA 14", RatePercent = 14m });
        await ctx.SaveChangesAsync();
        var factory = new LineItemFactory(ctx);
        var request = NewRequest("QUOTATION");

        // net = (3 * 100) - 50 = 250 ; iva = 250 * 14% = 35 ; total = 285
        var item = await factory.BuildAndStageAsync(request, new LineItemCreationSpec
        {
            Description = "com iva",
            Quantity = 3,
            UnitId = 1,
            UnitPrice = 100m,
            DiscountAmount = 50m,
            IvaRateId = 7
        }, Guid.NewGuid(), "X");

        Assert.Equal(285m, item.TotalAmount);
    }

    [Fact]
    public async Task Build_ZeroPrice_ProducesZeroTotal()
    {
        using var ctx = NewContext();
        var factory = new LineItemFactory(ctx);
        var request = NewRequest("QUOTATION");

        var item = await factory.BuildAndStageAsync(request, new LineItemCreationSpec
        {
            Description = "solicitado",
            Quantity = 4,
            UnitId = 1,
            UnitPrice = 0m,
            QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending,
            CreationOrigin = LineItemCreationOrigins.BuyerReconciliation
        }, Guid.NewGuid(), "X");

        Assert.Equal(0m, item.TotalAmount);
        Assert.Equal(RequestConstants.QuotationLifecycleStatuses.QuotationPending, item.QuotationLifecycleStatus);
        Assert.Equal(LineItemCreationOrigins.BuyerReconciliation, item.CreationOrigin);
    }

    [Fact]
    public async Task Build_PersistsProvenanceFields()
    {
        using var ctx = NewContext();
        var factory = new LineItemFactory(ctx);
        var request = NewRequest("QUOTATION");
        var proformaId = Guid.NewGuid();

        var item = await factory.BuildAndStageAsync(request, new LineItemCreationSpec
        {
            Description = "d",
            Quantity = 1,
            UnitId = 1,
            UnitPrice = 0m,
            SourceProformaAttachmentId = proformaId,
            CreationIdempotencyKey = "key-123",
            CreationOrigin = LineItemCreationOrigins.BuyerReconciliation
        }, Guid.NewGuid(), "X");

        Assert.Equal(proformaId, item.SourceProformaAttachmentId);
        Assert.Equal("key-123", item.CreationIdempotencyKey);
    }

    [Fact]
    public async Task Build_QuotationStatusId1_PaymentStatusId2()
    {
        using var ctx = NewContext();
        var factory = new LineItemFactory(ctx);

        var q = await factory.BuildAndStageAsync(NewRequest("QUOTATION"), new LineItemCreationSpec { Description = "q", Quantity = 1, UnitId = 1 }, Guid.NewGuid(), "X");
        var p = await factory.BuildAndStageAsync(NewRequest("PAYMENT"), new LineItemCreationSpec { Description = "p", Quantity = 1, UnitId = 1 }, Guid.NewGuid(), "X");

        Assert.Equal(1, q.LineItemStatusId);
        Assert.Equal(2, p.LineItemStatusId);
    }

    [Fact]
    public async Task Build_PaymentInheritsRequestSupplier_QuotationDoesNot()
    {
        using var ctx = NewContext();
        var factory = new LineItemFactory(ctx);

        var payment = NewRequest("PAYMENT", supplierId: 42);
        var pItem = await factory.BuildAndStageAsync(payment, new LineItemCreationSpec { Description = "p", Quantity = 1, UnitId = 1, SupplierName = "ignored" }, Guid.NewGuid(), "X");
        Assert.Equal(42, pItem.SupplierId);
        Assert.Null(pItem.SupplierName);

        var quotation = NewRequest("QUOTATION", supplierId: 42);
        var qItem = await factory.BuildAndStageAsync(quotation, new LineItemCreationSpec { Description = "q", Quantity = 1, UnitId = 1, SupplierName = "Fornecedor X" }, Guid.NewGuid(), "X");
        Assert.Null(qItem.SupplierId);
        Assert.Equal("Fornecedor X", qItem.SupplierName);
    }
}
