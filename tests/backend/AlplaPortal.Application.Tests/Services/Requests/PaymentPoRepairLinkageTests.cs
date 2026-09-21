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
/// v2.245.4 — the PAYMENT group producer (BuildPaymentPoGroupsAsync) exercised through the real
/// admin/payment-po-repair execute endpoint, which re-runs that same builder. Proves the header
/// (single-group) plan now links every active line item, that the repair therefore creates a properly
/// linked group, and that document-based multi-group plans still link each item only to its own document's
/// group (never all items to one group).
/// </summary>
public class PaymentPoRepairLinkageTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static RequestsController BuildSysAdminController(ApplicationDbContext ctx, Guid actorId)
    {
        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
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
            Options.Create(new PostPaymentCompletionOptions()));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.SystemAdministrator)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private static (Guid requestId, Guid actorId) SeedBase(ApplicationDbContext ctx, int? headerSupplierId)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "SysAdmin", Email = $"sa-{Guid.NewGuid():N}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 6, Code = RequestConstants.Statuses.FinalApproved, Name = "Aprovado" },
            new RequestStatus { Id = 8, Code = RequestConstants.Statuses.PoRequested, Name = "Aguardando P.O." });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 10, Name = "Fornecedor A", TaxId = "A" },
            new Supplier { Id = 20, Name = "Fornecedor B", TaxId = "B" });
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-PPR-" + Guid.NewGuid().ToString("N")[..6], Title = "ppr",
            RequestTypeId = 2, StatusId = 6, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1,
            SupplierId = headerSupplierId, PaymentConditionCode = "POST_PAID", EstimatedTotalAmount = 300m,
            ApprovedAtUtc = DateTime.UtcNow.AddDays(-1), CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        };
        ctx.Requests.Add(request);
        return (request.Id, actor.Id);
    }

    private static Task<IActionResult> ExecuteAsync(RequestsController c, Guid requestId) =>
        c.ExecutePaymentPoRepair(new PaymentPoRepairExecuteRequestDto { RequestIds = new List<Guid> { requestId } });

    // ── Legacy header plan (no source documents): ONE group, EVERY active item linked ──
    [Fact]
    public async Task LegacyHeaderPlan_CreatesOneGroup_AndLinksAllActiveItems()
    {
        using var ctx = NewContext();
        var (requestId, actorId) = SeedBase(ctx, headerSupplierId: 10);
        var live1 = Guid.NewGuid(); var live2 = Guid.NewGuid(); var deleted = Guid.NewGuid();
        ctx.RequestLineItems.AddRange(
            new RequestLineItem { Id = live1, RequestId = requestId, LineNumber = 1, Description = "a", Quantity = 1, TotalAmount = 100m },
            new RequestLineItem { Id = live2, RequestId = requestId, LineNumber = 2, Description = "b", Quantity = 1, TotalAmount = 200m },
            new RequestLineItem { Id = deleted, RequestId = requestId, LineNumber = 3, Description = "x", Quantity = 1, IsDeleted = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await ExecuteAsync(BuildSysAdminController(ctx, actorId), requestId);
        Assert.IsType<OkObjectResult>(result);

        var groups = await ctx.RequestPoGroups.AsNoTracking().Where(g => g.RequestId == requestId).ToListAsync();
        var group = Assert.Single(groups);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo, group.Status);

        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == requestId).ToListAsync();
        Assert.Equal(group.Id, items.Single(i => i.Id == live1).RequestPoGroupId);
        Assert.Equal(group.Id, items.Single(i => i.Id == live2).RequestPoGroupId);
        Assert.Null(items.Single(i => i.Id == deleted).RequestPoGroupId); // deleted items are never attributed
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == requestId && h.ActionTaken == "PAYMENT_PO_GROUP_REPAIRED"));
    }

    // ── Document plan: two documents → two groups; each item linked ONLY to its own document's group ──
    [Fact]
    public async Task DocumentPlan_TwoDocuments_TwoGroups_EachItemLinkedToItsOwnGroup_NeverAllToOne()
    {
        using var ctx = NewContext();
        var (requestId, actorId) = SeedBase(ctx, headerSupplierId: null);
        var attA = new RequestAttachment { Id = Guid.NewGuid(), RequestId = requestId, AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT, FileName = "a.pdf", FileExtension = "pdf", StorageReference = "x/a", UploadedByUserId = actorId, UploadedAtUtc = DateTime.UtcNow };
        var attB = new RequestAttachment { Id = Guid.NewGuid(), RequestId = requestId, AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT, FileName = "b.pdf", FileExtension = "pdf", StorageReference = "x/b", UploadedByUserId = actorId, UploadedAtUtc = DateTime.UtcNow };
        ctx.RequestAttachments.AddRange(attA, attB);
        var docA = new PaymentSourceDocument { Id = Guid.NewGuid(), RequestId = requestId, AttachmentId = attA.Id, SupplierId = 10, Currency = "AOA", SourceDocumentType = "INVOICE" };
        var docB = new PaymentSourceDocument { Id = Guid.NewGuid(), RequestId = requestId, AttachmentId = attB.Id, SupplierId = 20, Currency = "AOA", SourceDocumentType = "INVOICE" };
        ctx.PaymentSourceDocuments.AddRange(docA, docB);
        var a1 = Guid.NewGuid(); var b1 = Guid.NewGuid(); var b2 = Guid.NewGuid();
        ctx.RequestLineItems.AddRange(
            new RequestLineItem { Id = a1, RequestId = requestId, LineNumber = 1, Description = "a1", Quantity = 1, TotalAmount = 100m, PaymentSourceDocumentId = docA.Id },
            new RequestLineItem { Id = b1, RequestId = requestId, LineNumber = 2, Description = "b1", Quantity = 1, TotalAmount = 100m, PaymentSourceDocumentId = docB.Id },
            new RequestLineItem { Id = b2, RequestId = requestId, LineNumber = 3, Description = "b2", Quantity = 1, TotalAmount = 100m, PaymentSourceDocumentId = docB.Id });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await ExecuteAsync(BuildSysAdminController(ctx, actorId), requestId);
        Assert.IsType<OkObjectResult>(result);

        var groups = await ctx.RequestPoGroups.AsNoTracking().Where(g => g.RequestId == requestId).ToListAsync();
        Assert.Equal(2, groups.Count);
        var groupA = groups.Single(g => g.SupplierId == 10);
        var groupB = groups.Single(g => g.SupplierId == 20);

        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == requestId).ToListAsync();
        Assert.Equal(groupA.Id, items.Single(i => i.Id == a1).RequestPoGroupId);
        Assert.Equal(groupB.Id, items.Single(i => i.Id == b1).RequestPoGroupId);
        Assert.Equal(groupB.Id, items.Single(i => i.Id == b2).RequestPoGroupId);
        // never all items on one group
        Assert.Equal(2, items.Select(i => i.RequestPoGroupId).Distinct().Count());
    }
}
