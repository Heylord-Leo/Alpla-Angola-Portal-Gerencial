using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// Release 2: propagation of the winning quotation's billing document type onto the PO group.
///
/// The group's Final Invoice obligation is derived here, and it is the value the whole
/// post-payment workflow later depends on — so the losing quotations must have no influence, and
/// an ambiguous or absent classification must fall back to UNCLASSIFIED rather than to
/// "nothing is owed".
/// </summary>
public class GroupBuilderBillingClassificationTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static GroupBuilderService Service(ApplicationDbContext ctx, bool featureEnabled) =>
        new(ctx, Options.Create(new PostPaymentCompletionOptions
        {
            Enabled = featureEnabled,
            EffectiveDateUtc = featureEnabled
                ? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : DateTime.MaxValue
        }));

    /// <summary>
    /// Seeds a QUOTATION request whose single awarded item comes from a quotation carrying
    /// <paramref name="winningDocumentType"/>. When <paramref name="losingDocumentType"/> is
    /// supplied, a second quotation from the same supplier/currency exists but wins nothing.
    /// </summary>
    private static async Task<Guid> SeedAsync(
        ApplicationDbContext ctx, string? winningDocumentType, string? losingDocumentType = null)
    {
        var request = new Request
        {
            Id = Guid.NewGuid(),
            Title = "ZZTEST classification",
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
            CreatedAtUtc = DateTime.UtcNow
        };
        var supplier = new Supplier { Id = 1, Name = "ZZTEST Supplier", TaxId = "500000000" };

        var winning = new Quotation
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, Supplier = supplier,
            Currency = "AOA", DocumentType = winningDocumentType, IsSelected = true
        };
        var winningItem = new QuotationItem
        {
            Id = Guid.NewGuid(), QuotationId = winning.Id, Quotation = winning, LineTotal = 100m
        };

        var lineItem = new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = request.Id,
            SelectedQuotationItemId = winningItem.Id, TotalAmount = 100m
        };
        request.LineItems.Add(lineItem);

        ctx.Suppliers.Add(supplier);
        ctx.Requests.Add(request);
        ctx.Quotations.Add(winning);
        ctx.Set<QuotationItem>().Add(winningItem);

        if (losingDocumentType != null)
        {
            var losing = new Quotation
            {
                Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, Supplier = supplier,
                Currency = "AOA", DocumentType = losingDocumentType, IsSelected = false
            };
            // Same supplier and currency as the winner, so it would land in the same group key —
            // but no line item selects it, so it must not influence the obligation.
            ctx.Quotations.Add(losing);
            ctx.Set<QuotationItem>().Add(new QuotationItem
            {
                Id = Guid.NewGuid(), QuotationId = losing.Id, Quotation = losing, LineTotal = 999m
            });
        }

        await ctx.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task Winning_proforma_quotation_leaves_a_pending_final_invoice_obligation()
    {
        using var ctx = NewContext();
        var requestId = await SeedAsync(ctx, RequestConstants.BillingDocumentTypes.Proforma);

        await Service(ctx, featureEnabled: true).BuildGroupsForRequestAsync(requestId);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        Assert.Equal(RequestConstants.BillingDocumentTypes.Proforma, group.BillingDocumentType);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.PendingUpload, group.FinalInvoiceStatus);
    }

    [Fact]
    public async Task Winning_final_invoice_quotation_owes_nothing_further()
    {
        using var ctx = NewContext();
        var requestId = await SeedAsync(ctx, RequestConstants.BillingDocumentTypes.FinalInvoice);

        await Service(ctx, featureEnabled: true).BuildGroupsForRequestAsync(requestId);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.NotApplicableInitialFinalInvoice, group.FinalInvoiceStatus);
    }

    [Fact]
    public async Task An_unclassified_winning_quotation_leaves_the_group_unclassified()
    {
        using var ctx = NewContext();
        var requestId = await SeedAsync(ctx, winningDocumentType: null);

        await Service(ctx, featureEnabled: true).BuildGroupsForRequestAsync(requestId);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        Assert.Null(group.BillingDocumentType);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.Unclassified, group.FinalInvoiceStatus);
    }

    [Fact]
    public async Task A_losing_quotation_never_changes_the_obligation()
    {
        // The winner says PROFORMA (an invoice is owed); a losing quotation says FINAL_INVOICE.
        // If losers leaked in, the obligation would be wrongly cleared.
        using var ctx = NewContext();
        var requestId = await SeedAsync(
            ctx,
            winningDocumentType: RequestConstants.BillingDocumentTypes.Proforma,
            losingDocumentType: RequestConstants.BillingDocumentTypes.FinalInvoice);

        await Service(ctx, featureEnabled: true).BuildGroupsForRequestAsync(requestId);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        Assert.Equal(RequestConstants.BillingDocumentTypes.Proforma, group.BillingDocumentType);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.PendingUpload, group.FinalInvoiceStatus);
    }

    [Fact]
    public async Task With_the_feature_disabled_the_group_keeps_the_schema_default()
    {
        using var ctx = NewContext();
        var requestId = await SeedAsync(ctx, RequestConstants.BillingDocumentTypes.Proforma);

        await Service(ctx, featureEnabled: false).BuildGroupsForRequestAsync(requestId);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        Assert.Null(group.BillingDocumentType);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.Unclassified, group.FinalInvoiceStatus);
    }

    [Fact]
    public async Task Rebuilding_after_a_winner_change_recalculates_the_obligation()
    {
        // Winner replacement: the award moves to a quotation with a different classification, and
        // the group is rebuilt before any operational stage. The obligation must follow.
        using var ctx = NewContext();
        var requestId = await SeedAsync(ctx, RequestConstants.BillingDocumentTypes.Proforma);
        var service = Service(ctx, featureEnabled: true);

        await service.BuildGroupsForRequestAsync(requestId);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.PendingUpload,
            (await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId)).FinalInvoiceStatus);

        // The buyer re-classifies the winning quotation and the award is rebuilt.
        var quotation = await ctx.Quotations.FirstAsync(q => q.RequestId == requestId);
        quotation.DocumentType = RequestConstants.BillingDocumentTypes.FinalInvoice;
        await ctx.SaveChangesAsync();

        await service.BuildGroupsForRequestAsync(requestId);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.NotApplicableInitialFinalInvoice, group.FinalInvoiceStatus);
    }

    [Fact]
    public async Task A_group_that_already_started_its_post_payment_lifecycle_is_not_reclassified()
    {
        // Once real documents exist against the obligation, a rebuild must not overwrite it —
        // that would discard evidence rather than correct a classification.
        using var ctx = NewContext();
        var requestId = await SeedAsync(ctx, RequestConstants.BillingDocumentTypes.Proforma);
        var service = Service(ctx, featureEnabled: true);

        await service.BuildGroupsForRequestAsync(requestId);

        var group = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        group.FinalInvoiceAttachmentId = Guid.NewGuid();
        group.FinalInvoiceStatus = RequestConstants.FinalInvoiceStatuses.Validated;
        await ctx.SaveChangesAsync();

        var quotation = await ctx.Quotations.FirstAsync(q => q.RequestId == requestId);
        quotation.DocumentType = RequestConstants.BillingDocumentTypes.FinalInvoice;
        await ctx.SaveChangesAsync();

        await service.BuildGroupsForRequestAsync(requestId);

        var reloaded = await ctx.RequestPoGroups.SingleAsync(g => g.RequestId == requestId);
        Assert.Equal(RequestConstants.FinalInvoiceStatuses.Validated, reloaded.FinalInvoiceStatus);
    }
}
