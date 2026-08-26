using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Phase 4B.2 — the historical PAYMENT PO-group repair endpoints. Proves the repair reuses the
/// canonical builder, is idempotent, keeps the scalar APPROVED, links items to their group, and
/// refuses anything not SAFE_TO_REPAIR — all through the SysAdmin-only endpoints.
/// </summary>
public class PaymentPoGroupRepairEndpointTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId, string role)
    {
        var options = new PostPaymentCompletionOptions
        {
            Enabled = true,
            CompletionEnabled = false,
            EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
        };

        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(
                new Mock<IServiceScopeFactory>().Object,
                new Mock<IHttpContextAccessor>().Object,
                NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object,
            new Mock<IApprovalRoutingService>().Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(options));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, role)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private static void SeedCommon(ApplicationDbContext ctx, Guid actorId)
    {
        ctx.Users.Add(new User { Id = actorId, FullName = "Admin", Email = "admin@test.local" });
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 5, Code = "APPROVED", Name = "Aprovado", DisplayOrder = 5 });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.Suppliers.Add(new Supplier { Id = 1, Name = "ACME Lda", TaxId = "5000000000" });
    }

    /// <summary>An APPROVED multi-document payment with a linked item and NO groups (historical drift).</summary>
    private static async Task<(Guid RequestId, Guid ItemId)> SeedApprovedMultiDocNoGroupsAsync(
        ApplicationDbContext ctx, Guid actorId)
    {
        SeedCommon(ctx, actorId);
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REP-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST repair multi-doc",
            RequestTypeId = 1, StatusId = 5, RequesterId = actorId,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            EstimatedTotalAmount = 100m,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
            ApprovedAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Requests.Add(request);

        var doc = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(), RequestId = request.Id, AttachmentId = Guid.NewGuid(),
            SupplierId = 1, SupplierNameSnapshot = "ACME Lda", PlantId = 1,
            SourceDocumentType = "PROFORMA", DocumentNumber = "FT 1", Currency = "AOA",
            DueDate = DateTime.UtcNow.AddDays(20), GrossAmount = 100m, SequenceNumber = 1,
            IsVoided = false, CreatedAtUtc = DateTime.UtcNow.AddDays(-10), CreatedByUserId = actorId
        };
        ctx.PaymentSourceDocuments.Add(doc);

        var itemId = Guid.NewGuid();
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = itemId, RequestId = request.Id, PaymentSourceDocumentId = doc.Id,
            Description = "Serviço", Quantity = 1, UnitPrice = 100m, TotalAmount = 100m, IsDeleted = false
        });

        await ctx.SaveChangesAsync();
        return (request.Id, itemId);
    }

    private static async Task<Guid> SeedApprovedLegacyNoGroupsAsync(ApplicationDbContext ctx, Guid actorId)
    {
        SeedCommon(ctx, actorId);
        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REPL-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST repair legacy",
            RequestTypeId = 1, StatusId = 5, RequesterId = actorId,
            DepartmentId = 1, CompanyId = 1, PlantId = 1, CurrencyId = 1, SupplierId = 1,
            EstimatedTotalAmount = 250m,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
            ApprovedAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static PaymentPoRepairExecuteRequestDto Ids(params Guid[] ids) =>
        new() { RequestIds = ids.ToList() };

    [Fact]
    public async Task A_J_I_MultiDoc_Repair_CreatesWaitingPoGroup_LinksItem_KeepsApproved()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var (requestId, itemId) = await SeedApprovedMultiDocNoGroupsAsync(ctx, actorId);

        var result = await BuildController(ctx, actorId, RoleConstants.SystemAdministrator)
            .ExecutePaymentPoRepair(Ids(requestId));

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = Assert.IsAssignableFrom<List<PaymentPoRepairResultDto>>(ok.Value);
        Assert.Equal("REPAIRED", rows.Single().Outcome);
        Assert.Equal(1, rows.Single().GroupsCreated);

        ctx.ChangeTracker.Clear();
        var groups = await ctx.RequestPoGroups.Where(g => g.RequestId == requestId).ToListAsync();
        Assert.Single(groups);                                              // A/B
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo, groups[0].Status);

        var item = await ctx.RequestLineItems.SingleAsync(i => i.Id == itemId);
        Assert.Equal(groups[0].Id, item.RequestPoGroupId);                  // J — traceability

        var request = await ctx.Requests.Include(r => r.Status).SingleAsync(r => r.Id == requestId);
        Assert.Equal("APPROVED", request.Status!.Code);                     // I — scalar untouched
    }

    [Fact]
    public async Task B_LegacyHeader_Repair_CreatesGroup()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedApprovedLegacyNoGroupsAsync(ctx, actorId);

        await BuildController(ctx, actorId, RoleConstants.SystemAdministrator)
            .ExecutePaymentPoRepair(Ids(requestId));

        ctx.ChangeTracker.Clear();
        var groups = await ctx.RequestPoGroups.Where(g => g.RequestId == requestId).ToListAsync();
        Assert.Single(groups);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo, groups[0].Status);
    }

    [Fact]
    public async Task C_RepairTwice_IsIdempotent_NoDuplicateGroups()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var (requestId, _) = await SeedApprovedMultiDocNoGroupsAsync(ctx, actorId);

        var controller = BuildController(ctx, actorId, RoleConstants.SystemAdministrator);
        await controller.ExecutePaymentPoRepair(Ids(requestId));
        var second = await controller.ExecutePaymentPoRepair(Ids(requestId));

        var ok = Assert.IsType<OkObjectResult>(second);
        var rows = Assert.IsAssignableFrom<List<PaymentPoRepairResultDto>>(ok.Value);
        Assert.Equal("SKIPPED", rows.Single().Outcome);   // already has groups → no-op

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.RequestPoGroups.CountAsync(g => g.RequestId == requestId));
    }

    [Fact]
    public async Task E_DownstreamEvidence_IsManualReview_NoWrite()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var (requestId, _) = await SeedApprovedMultiDocNoGroupsAsync(ctx, actorId);

        // A P.O. attachment without any group — an anomaly the repair must never paper over.
        ctx.RequestAttachments.Add(new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = requestId, AttachmentTypeCode = RequestAttachment.TYPE_PO,
            FileName = "po.pdf", UploadedByUserId = actorId, UploadedAtUtc = DateTime.UtcNow, IsDeleted = false
        });
        await ctx.SaveChangesAsync();

        var result = await BuildController(ctx, actorId, RoleConstants.SystemAdministrator)
            .ExecutePaymentPoRepair(Ids(requestId));

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = Assert.IsAssignableFrom<List<PaymentPoRepairResultDto>>(ok.Value);
        Assert.Equal("MANUAL_REVIEW", rows.Single().Outcome);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.RequestPoGroups.CountAsync(g => g.RequestId == requestId));
    }

    [Fact]
    public async Task DryRun_ReportsSafeToRepair_WithExpectedGroupCount_AndWritesNothing()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var (requestId, _) = await SeedApprovedMultiDocNoGroupsAsync(ctx, actorId);

        var result = await BuildController(ctx, actorId, RoleConstants.SystemAdministrator)
            .GetPaymentPoRepairCandidates(null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsAssignableFrom<List<PaymentPoRepairCandidateDto>>(ok.Value);
        var row = report.Single(r => r.RequestId == requestId);
        Assert.Equal("SAFE_TO_REPAIR", row.Classification);
        Assert.Equal("MultiDocument", row.Model);
        Assert.Equal(1, row.ExpectedGroupCount);

        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.RequestPoGroups.CountAsync(g => g.RequestId == requestId)); // zero writes
    }

    [Fact]
    public async Task NonSysAdmin_IsForbidden()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var (requestId, _) = await SeedApprovedMultiDocNoGroupsAsync(ctx, actorId);

        var exec = await BuildController(ctx, actorId, RoleConstants.Buyer)
            .ExecutePaymentPoRepair(Ids(requestId));
        var dry = await BuildController(ctx, actorId, RoleConstants.Buyer)
            .GetPaymentPoRepairCandidates(null);

        Assert.Equal(403, Assert.IsType<ObjectResult>(exec).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(dry).StatusCode);
    }
}
