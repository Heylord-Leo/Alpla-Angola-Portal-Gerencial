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
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
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
/// v2.245.4 — the NORMAL final-approval flow (POST {id}/final-approval/approve → ProcessFinalApproval →
/// BuildPaymentPoGroupsAsync) for PAYMENT requests. Independent of the payment-po-repair test: proves that a
/// new header-only PAYMENT request creates exactly one active group with every active line item linked
/// (deleted items excluded), and that a document-based PAYMENT still yields per-document groups with each item
/// linked only to its own document's group.
/// </summary>
public class PaymentFinalApprovalLinkageTests
{
    private const int S_WAITING_FINAL = 5, S_APPROVED = 6, S_PO_REQUESTED = 8;

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static RequestsController BuildFinalApproverController(ApplicationDbContext ctx, Guid actorId)
    {
        var options = new PostPaymentCompletionOptions();
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
            Options.Create(options));

        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, RoleConstants.FinalApprover)
                }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    /// <summary>A PAYMENT request waiting for final approval, no groups yet.</summary>
    private static (Guid requestId, Guid actorId) SeedWaitingFinal(ApplicationDbContext ctx, int? headerSupplierId)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Final Approver", Email = $"fa-{Guid.NewGuid():N}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_WAITING_FINAL, Code = RequestConstants.Statuses.WaitingFinalApproval, Name = "Ag. Aprovação Final" },
            new RequestStatus { Id = S_APPROVED, Code = RequestConstants.Statuses.FinalApproved, Name = "Aprovado" },
            new RequestStatus { Id = S_PO_REQUESTED, Code = RequestConstants.Statuses.PoRequested, Name = "Aguardando P.O." });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 10, Name = "Fornecedor A", TaxId = "A" },
            new Supplier { Id = 20, Name = "Fornecedor B", TaxId = "B" });
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-FA-" + Guid.NewGuid().ToString("N")[..6], Title = "fa",
            RequestTypeId = 2, StatusId = S_WAITING_FINAL, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1,
            SupplierId = headerSupplierId, PaymentConditionCode = "POST_PAID", EstimatedTotalAmount = 300m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        };
        ctx.Requests.Add(request);
        return (request.Id, actor.Id);
    }

    // ── Normal flow, header-only PAYMENT: one active group, every active item linked, deleted excluded ──
    [Fact]
    public async Task FinalApproval_HeaderOnlyPayment_CreatesOneGroup_LinksAllActiveItems_ExcludesDeleted()
    {
        using var ctx = NewContext();
        var (requestId, actorId) = SeedWaitingFinal(ctx, headerSupplierId: 10);
        var live1 = Guid.NewGuid(); var live2 = Guid.NewGuid(); var live3 = Guid.NewGuid(); var deleted = Guid.NewGuid();
        ctx.RequestLineItems.AddRange(
            new RequestLineItem { Id = live1, RequestId = requestId, LineNumber = 1, Description = "a", Quantity = 1, TotalAmount = 100m },
            new RequestLineItem { Id = live2, RequestId = requestId, LineNumber = 2, Description = "b", Quantity = 1, TotalAmount = 100m },
            new RequestLineItem { Id = live3, RequestId = requestId, LineNumber = 3, Description = "c", Quantity = 1, TotalAmount = 100m },
            new RequestLineItem { Id = deleted, RequestId = requestId, LineNumber = 4, Description = "x", Quantity = 1, IsDeleted = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await BuildFinalApproverController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "ok" });
        Assert.IsType<OkObjectResult>(result);

        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == requestId);
        Assert.Equal(RequestConstants.Statuses.FinalApproved, request.Status!.Code);

        var groups = await ctx.RequestPoGroups.AsNoTracking().Where(g => g.RequestId == requestId).ToListAsync();
        var group = Assert.Single(groups);                                        // exactly one group
        Assert.NotEqual(RequestConstants.PoGroupStatuses.Cancelled, group.Status); // active
        Assert.Equal(10, group.SupplierId);

        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == requestId).ToListAsync();
        foreach (var id in new[] { live1, live2, live3 })
            Assert.Equal(group.Id, items.Single(i => i.Id == id).RequestPoGroupId);  // every active item linked
        Assert.Null(items.Single(i => i.Id == deleted).RequestPoGroupId);            // deleted item excluded
    }

    // ── Normal flow, document-based PAYMENT: per-document groups, each item on its own group only ──
    [Fact]
    public async Task FinalApproval_DocumentBasedPayment_MultiGroupBehaviorUnchanged()
    {
        using var ctx = NewContext();
        var (requestId, actorId) = SeedWaitingFinal(ctx, headerSupplierId: null);
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

        var result = await BuildFinalApproverController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "ok" });
        Assert.IsType<OkObjectResult>(result);

        var groups = await ctx.RequestPoGroups.AsNoTracking().Where(g => g.RequestId == requestId).ToListAsync();
        Assert.Equal(2, groups.Count);
        var groupA = groups.Single(g => g.SupplierId == 10);
        var groupB = groups.Single(g => g.SupplierId == 20);

        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == requestId).ToListAsync();
        Assert.Equal(groupA.Id, items.Single(i => i.Id == a1).RequestPoGroupId);
        Assert.Equal(groupB.Id, items.Single(i => i.Id == b1).RequestPoGroupId);
        Assert.Equal(groupB.Id, items.Single(i => i.Id == b2).RequestPoGroupId);
        Assert.Equal(2, items.Select(i => i.RequestPoGroupId).Distinct().Count()); // never all items on one group
    }
}
