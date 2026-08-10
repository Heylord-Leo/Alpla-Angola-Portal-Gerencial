using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

using Agg = RequestConstants.OperationInvoiceStatuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// Release 4 Phase 1c: QUOTATION-origin groups capture ExpectedOperationInvoiceTotal at creation,
/// under the SAME convention as PAYMENT group creation — captured once from the group total when
/// the obligation exists, never recalculated, never invented when the obligation does not exist,
/// and never backfilled onto groups that already carry a value.
/// </summary>
public class QuotationExpectedTotalCaptureTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static GroupBuilderService EnabledService(ApplicationDbContext ctx) =>
        new(ctx, Options.Create(new PostPaymentCompletionOptions { Enabled = true }));

    private sealed record Seed(Request Request, Quotation Quotation);

    private static async Task<Seed> SeedAwardedRequestAsync(
        ApplicationDbContext ctx, string? documentType, decimal lineTotal1 = 100m, decimal lineTotal2 = 200m)
    {
        var request = new Request
        {
            Id = Guid.NewGuid(),
            PaymentConditionCode = "POST_PAID",
            Title = "ZZTEST capture",
            Description = "ZZTEST"
        };

        var supplier = new Supplier { Id = 1, Name = "ZZTEST Supplier", TaxId = "999999999" };
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierId = supplier.Id,
            Supplier = supplier,
            Currency = "USD",
            DocumentType = documentType
        };
        var qItem1 = new QuotationItem
        {
            Id = Guid.NewGuid(), QuotationId = quotation.Id, Quotation = quotation, LineTotal = lineTotal1
        };
        var qItem2 = new QuotationItem
        {
            Id = Guid.NewGuid(), QuotationId = quotation.Id, Quotation = quotation, LineTotal = lineTotal2
        };

        request.LineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem1.Id, TotalAmount = lineTotal1
        });
        request.LineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem2.Id, TotalAmount = lineTotal2
        });

        ctx.Suppliers.Add(supplier);
        ctx.Requests.Add(request);
        ctx.Quotations.Add(quotation);
        ctx.Set<QuotationItem>().AddRange(qItem1, qItem2);
        await ctx.SaveChangesAsync();

        return new Seed(request, quotation);
    }

    [Fact]
    public async Task A_proforma_awarded_group_captures_its_own_total_as_the_expected_amount()
    {
        using var ctx = NewContext();
        var seed = await SeedAwardedRequestAsync(ctx, Types.Proforma);

        await EnabledService(ctx).BuildGroupsForRequestAsync(seed.Request.Id);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == seed.Request.Id);
        Assert.Equal(Types.Proforma, group.SourceDocumentType);
        Assert.Equal(Agg.PendingUpload, group.OperationInvoiceStatus);
        Assert.Equal(300m, group.ExpectedOperationInvoiceTotal);
        Assert.Equal("USD", group.ExpectedOperationInvoiceCurrency);
    }

    [Fact]
    public async Task An_invoice_awarded_group_owes_nothing_and_captures_nothing()
    {
        // The PAYMENT convention exactly: no obligation, no expected total — and no clearing
        // rule either, because there is nothing to clear at creation.
        using var ctx = NewContext();
        var seed = await SeedAwardedRequestAsync(ctx, Types.Invoice);

        await EnabledService(ctx).BuildGroupsForRequestAsync(seed.Request.Id);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == seed.Request.Id);
        Assert.Equal(Agg.NotRequired, group.OperationInvoiceStatus);
        Assert.False(group.RequiresOperationInvoice);
        Assert.Null(group.ExpectedOperationInvoiceTotal);
        Assert.Null(group.ExpectedOperationInvoiceCurrency);
    }

    [Fact]
    public async Task Two_suppliers_capture_two_independent_totals()
    {
        using var ctx = NewContext();
        var request = new Request
        {
            Id = Guid.NewGuid(), PaymentConditionCode = "POST_PAID", Title = "ZZTEST two suppliers"
        };

        var supplier1 = new Supplier { Id = 1, Name = "ZZTEST S1" };
        var supplier2 = new Supplier { Id = 2, Name = "ZZTEST S2" };
        var quotation1 = new Quotation
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 1, Supplier = supplier1,
            Currency = "USD", DocumentType = Types.Proforma
        };
        var quotation2 = new Quotation
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 2, Supplier = supplier2,
            Currency = "EUR", DocumentType = Types.Proforma
        };
        var qItem1 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation1.Id, Quotation = quotation1, LineTotal = 100m };
        var qItem2 = new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotation2.Id, Quotation = quotation2, LineTotal = 200m };

        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem1.Id });
        request.LineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, SelectedQuotationItemId = qItem2.Id });

        ctx.Suppliers.AddRange(supplier1, supplier2);
        ctx.Requests.Add(request);
        ctx.Quotations.AddRange(quotation1, quotation2);
        ctx.Set<QuotationItem>().AddRange(qItem1, qItem2);
        await ctx.SaveChangesAsync();

        await EnabledService(ctx).BuildGroupsForRequestAsync(request.Id);

        var groups = await ctx.RequestPoGroups.Where(g => g.RequestId == request.Id).ToListAsync();
        Assert.Equal(2, groups.Count);

        // Each group's own total, in its own currency — never a request-level number leaking in.
        var g1 = groups.Single(g => g.SupplierId == 1);
        Assert.Equal(100m, g1.ExpectedOperationInvoiceTotal);
        Assert.Equal("USD", g1.ExpectedOperationInvoiceCurrency);

        var g2 = groups.Single(g => g.SupplierId == 2);
        Assert.Equal(200m, g2.ExpectedOperationInvoiceTotal);
        Assert.Equal("EUR", g2.ExpectedOperationInvoiceCurrency);
    }

    [Fact]
    public async Task A_group_that_already_carries_an_expected_total_is_never_recalculated()
    {
        using var ctx = NewContext();
        var seed = await SeedAwardedRequestAsync(ctx, Types.Proforma);

        // A pre-existing group with the same identity and an already-captured snapshot.
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            RequestId = seed.Request.Id,
            SupplierId = 1,
            CurrencyCode = "USD",
            PaymentConditionCode = "POST_PAID",
            SupplierNameSnapshot = "ZZTEST Supplier",
            SourceDocumentType = Types.Proforma,
            OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true,
            ExpectedOperationInvoiceTotal = 999m,
            ExpectedOperationInvoiceCurrency = "USD",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedByUserId = Guid.NewGuid()
        });
        await ctx.SaveChangesAsync();

        await EnabledService(ctx).BuildGroupsForRequestAsync(seed.Request.Id);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == seed.Request.Id);
        Assert.Equal(300m, group.TotalAmount);                       // the live awarded total moved
        Assert.Equal(999m, group.ExpectedOperationInvoiceTotal);     // the snapshot did not
    }

    [Fact]
    public async Task While_the_feature_is_disabled_nothing_is_captured()
    {
        using var ctx = NewContext();
        var seed = await SeedAwardedRequestAsync(ctx, Types.Proforma);

        // No options: the constructor's safe default is disabled — the committed state.
        await new GroupBuilderService(ctx).BuildGroupsForRequestAsync(seed.Request.Id);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == seed.Request.Id);
        Assert.Null(group.ExpectedOperationInvoiceTotal);
        Assert.Equal(Agg.Unclassified, group.OperationInvoiceStatus);   // schema default untouched
    }

    [Fact]
    public async Task A_captured_quotation_group_no_longer_projects_an_unknown_expected_total()
    {
        using var ctx = NewContext();
        var seed = await SeedAwardedRequestAsync(ctx, Types.Proforma);

        await EnabledService(ctx).BuildGroupsForRequestAsync(seed.Request.Id);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == seed.Request.Id);
        var obligation = Assert.Single(OperationInvoiceObligationProjector.Project(new[]
        {
            new OperationInvoiceObligationGroupSnapshot
            {
                GroupId = group.Id,
                SourceDocumentType = group.SourceDocumentType,
                ExpectedTotal = group.ExpectedOperationInvoiceTotal,
                ExpectedCurrency = group.ExpectedOperationInvoiceCurrency,
                PersistedStatus = group.OperationInvoiceStatus
            }
        }).Obligations);

        Assert.NotEqual(OperationInvoiceObligationReasons.ExpectedTotalUnknown, obligation.ReasonCode);
        Assert.Equal(OperationInvoiceObligationReasons.AwaitingOperationInvoice, obligation.ReasonCode);
        Assert.Equal(300m, obligation.ExpectedAmount);
        Assert.False(obligation.StatusDrift);
    }
}
