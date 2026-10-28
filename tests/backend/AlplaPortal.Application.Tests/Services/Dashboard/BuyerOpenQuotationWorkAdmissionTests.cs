using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Services.Dashboard;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Application.Services.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// v2.249.2 — a line RELEASED to quotation by a P.O.-group cancellation while the request scalar stays past the
/// P.O. gate (sibling group issued — REQ-28/10/2026-628 shape) must be discoverable in EVERY Buyer work surface, not
/// only by URL: the Dashboard Buyer section/workload, the personal "Minha Operação" actions, the near-deadline alerts,
/// the operational pipeline and the Para Minha Ação card — once per request, with no duplicate action, and with the
/// legacy pre-batch shape still excluded. One shared SQL bound (BuyerQueueAdmission) feeds the same pure projection.
/// </summary>
public class BuyerOpenQuotationWorkAdmissionTests
{
    private const int QuotationTypeId = 1, PoPartiallyUploadedId = 5, PoIssuedId = 8;
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed record Seed(Guid Buyer, Guid Req628, Guid Legacy, Guid ReleasedLine);

    /// <summary>REQ-628 after the restart (PO_PARTIALLY_UPLOADED, APPROVED batch, line 1 released, line 2 approved + PO_ISSUED,
    /// overdue) and a LEGACY PO_ISSUED request without batches and a null-lifecycle line (never Buyer work again).</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.Add(new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = PoPartiallyUploadedId, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O. parcial" },
            new RequestStatus { Id = PoIssuedId, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" });
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaSOPRO" });
        ctx.Departments.Add(new Department { Id = 1, Name = "Manutenção" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 });
        var buyer = Guid.NewGuid();
        ctx.Users.Add(new User { Id = buyer, FullName = "Samuel Mpoco", Email = "samuel@t.local", IsActive = true });

        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-28/10/2026-628", Title = "TEST v2.249.0 – A – voltar a cotar", Description = "d",
            RequestTypeId = QuotationTypeId, StatusId = PoPartiallyUploadedId, RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = Today.AddDays(-3), NeedByDateUtc = Today.AddDays(-1),
        };
        ctx.Requests.Add(req);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = req.Id, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.Approved, CreatedByUserId = buyer, CreatedAtUtc = Today.AddDays(-2) };
        ctx.ApprovalBatches.Add(batch);
        var s2 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = 11, SupplierNameSnapshot = "MULTI BIZ", Status = RequestConstants.PoGroupStatuses.PoIssued, CurrencyCode = "AOA", TotalAmount = 120_000m, PurchaseOrderNumber = "ECF99 2026/9002", ApprovalBatchId = batch.Id, CreatedByUserId = buyer, CreatedAtUtc = Today.AddDays(-1) };
        var s1 = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = req.Id, SupplierId = 10, SupplierNameSnapshot = "TREVOTECH LDA", Status = RequestConstants.PoGroupStatuses.Cancelled, CurrencyCode = "AOA", TotalAmount = 250_000m, PurchaseOrderNumber = "ECF99 2026/9001", ApprovalBatchId = batch.Id, CreatedByUserId = buyer, CreatedAtUtc = Today.AddDays(-1) };
        ctx.RequestPoGroups.AddRange(s1, s2);
        var line1 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "Rolo de filme stretch", Quantity = 10, UnitPrice = 0, TotalAmount = 0, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationPending };
        var line2 = new RequestLineItem { Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 2, Description = "Fita adesiva", Quantity = 20, UnitPrice = 6000, TotalAmount = 120_000, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved, RequestPoGroupId = s2.Id };
        ctx.RequestLineItems.AddRange(line1, line2);
        ctx.ApprovalBatchItems.AddRange(
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = line1.Id, CreatedAtUtc = Today.AddDays(-2), ReleasedAtUtc = Today.AddHours(-1), ReleasedByUserId = buyer, ReleaseKind = "RESTART_QUOTATION" },
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = line2.Id, CreatedAtUtc = Today.AddDays(-2) });

        var legacy = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-LEGACY", Title = "legacy", Description = "d",
            RequestTypeId = QuotationTypeId, StatusId = PoIssuedId, RequesterId = buyer, CreatedByUserId = buyer, BuyerId = buyer,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = Today.AddDays(-30), NeedByDateUtc = Today.AddDays(-10),
        };
        ctx.Requests.Add(legacy);
        ctx.RequestLineItems.Add(new RequestLineItem { Id = Guid.NewGuid(), RequestId = legacy.Id, LineNumber = 1, Description = "x", Quantity = 1, UnitPrice = 1, TotalAmount = 1, QuotationLifecycleStatus = null });
        await ctx.SaveChangesAsync();
        return new Seed(buyer, req.Id, legacy.Id, line1.Id);
    }

    [Fact]
    public async Task Shared_admission_rule_admits_the_released_line_shape_and_excludes_the_legacy_shape()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var admitted = await ctx.Requests.Where(BuyerQueueAdmission.IsBuyerActiveOrHasOpenQuotationWork).Select(r => r.Id).ToListAsync();
        Assert.Contains(s.Req628, admitted);
        Assert.DoesNotContain(s.Legacy, admitted);
        var queue = await ctx.Requests.Where(BuyerQueueAdmission.IsBuyerActiveOrHasOpenQuotationWorkOrPoCorrection).Select(r => r.Id).ToListAsync();
        Assert.Contains(s.Req628, queue);
        Assert.DoesNotContain(s.Legacy, queue);
        var loaded = await ctx.Requests.Include(r => r.ApprovalBatches).Include(r => r.LineItems).SingleAsync(r => r.Id == s.Req628);
        Assert.True(BuyerQueueAdmission.HasOpenQuotationWork(loaded));
    }

    [Fact]
    public async Task Dashboard_Buyer_section_counts_the_request_once_and_its_pending_line()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var dto = await new DashboardV2QueryService(ctx).BuildBuyerSectionAsync(ctx.Requests, s.Buyer, isBuyer: true, canSeeWorkload: true, Today);
        Assert.NotNull(dto.Personal);
        Assert.Equal(1, dto.Personal!.AssignedRequests);     // REQ-628 only; the legacy request is not Buyer work
        Assert.Equal(1, dto.Personal.ActionableRequests);
        Assert.Equal(1, dto.Personal.PendingQuotationItems);  // the released line
        Assert.Equal(0, dto.Personal.ReadyForBatchItems);     // nothing ready without a new/authorized quotation
    }

    [Fact]
    public async Task Personal_actions_list_exactly_one_ADD_QUOTATION_for_the_request_and_nothing_for_the_legacy_one()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var dto = await new PersonalActionProjection(ctx).BuildAsync(ctx.Requests, s.Buyer, Today);
        var mine = dto.Actions.Where(a => a.RequestId == s.Req628).ToList();
        var a = Assert.Single(mine);
        Assert.Equal(BuyerQueueConstants.ActionCodes.AddQuotation, a.ActionType);
        Assert.Equal(PersonalActionDomains.Buyer, a.Domain);
        Assert.NotNull(a.TargetPath); // the Buyer queue, which now admits the request and opens its Workspace
        Assert.DoesNotContain(dto.Actions, x => x.RequestId == s.Legacy);
    }

    [Fact]
    public async Task Overdue_released_line_raises_exactly_one_Buyer_alert()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var dto = await new CanonicalAlertProjection(ctx).BuildAsync(ctx.Requests, s.Buyer, isBuyer: true, isFinance: false, canSeeManagerial: false, Today);
        var alert = Assert.Single(dto.Alerts.Where(x => x.RequestId == s.Req628));
        Assert.Equal(AlertTypes.BuyerOverdue, alert.AlertType);
        Assert.DoesNotContain(dto.Alerts, x => x.RequestId == s.Legacy);
    }

    [Fact]
    public async Task Operational_pipeline_counts_the_request_under_Cobertura_parcial()
    {
        using var ctx = NewContext();
        await SeedAsync(ctx);
        var dto = await new OperationalPipelineProjection(ctx, new FinancePaymentEligibilityService()).BuildAsync(ctx.Requests, Today);
        var partial = Assert.Single(dto.Stages.Where(st => st.Stage == PipelineStages.PartialCoverage));
        Assert.Equal(1, partial.RequestCount);
        Assert.Equal(0, dto.Stages.Where(st => st.Stage == PipelineStages.ReadyForApproval).Sum(st => st.RequestCount));
    }

    [Fact]
    public async Task Para_Minha_Acao_card_Cotar_itens_opens_the_Workspace_and_is_the_only_Buyer_card_for_the_request()
    {
        using var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var requests = await ctx.Requests.Include(r => r.RequestType).Include(r => r.Status).Include(r => r.LineItems).Include(r => r.PoGroups).ToListAsync();
        var res = new PersonalActionProjectionService().Build(requests,
            new PersonalActionProjectionService.Context(s.Buyer, IsBuyer: true, IsFinance: false, IsReceiver: false, IsFinalApprover: false, Today),
            null, 1, 50, null, null, null, null);
        var cards = res.Items.Where(i => i.RequestId == s.Req628).ToList();
        var card = Assert.Single(cards);
        Assert.Equal(PersonalActionPredicates.ActionTypes.QuotationRequired, card.ActionType);
        Assert.Equal("Cotar itens", card.ActionLabel);
        Assert.Equal($"/buyer/requests/{s.Req628}", card.Route);
        // Note: the Para Minha Ação QUOTATION_REQUIRED predicate is unchanged by v2.249.2 (any non-terminal QUOTATION request
        // with a null/pending line); only the route of the card changed. No assertion on the legacy request here.
    }
}
