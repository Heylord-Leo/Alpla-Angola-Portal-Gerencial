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
/// Phase 4B.1 (Issue 1) — a PAYMENT request must expose a "Registrar P.O." action after final
/// approval, exactly like QUOTATION, without being pushed into PO_REQUESTED. The action is
/// group-driven: the Buyer's button and the RegisterPo PAYMENT branch both need a WAITING_PO
/// <see cref="RequestPoGroup"/> to hang on. Final approval builds that group from the request's
/// source documents — but the group builder read <c>request.LineItems</c>, a navigation the caller
/// never loaded, so a MULTI-DOCUMENT payment produced an EMPTY plan and no group at all, leaving the
/// Buyer with a P.O. next-action label and nothing to click. These tests pin the corrected behaviour.
/// </summary>
public class PaymentPoActionAvailabilityTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId)
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
                    new(ClaimTypes.Role, RoleConstants.FinalApprover)
                }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private static void SeedCommon(ApplicationDbContext ctx, Guid actorId)
    {
        ctx.Users.Add(new User { Id = actorId, FullName = "PO Tester", Email = "po@test.local" });
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 4, Code = "WAITING_FINAL_APPROVAL", Name = "Ag. Aprovação Final", DisplayOrder = 4 },
            new RequestStatus { Id = 5, Code = "APPROVED", Name = "Aprovado", DisplayOrder = 5 },
            new RequestStatus { Id = 6, Code = "PO_REQUESTED", Name = "Aguardando P.O.", DisplayOrder = 6 });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.Suppliers.Add(new Supplier { Id = 1, Name = "ACME Lda", TaxId = "5000000000" });
    }

    /// <summary>A PAYMENT at final approval that carries a source document with a linked item.</summary>
    private static async Task<Guid> SeedMultiDocumentPaymentAsync(ApplicationDbContext ctx, Guid actorId)
    {
        SeedCommon(ctx, actorId);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-PAY-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST payment PO availability",
            RequestTypeId = 1,
            StatusId = 4,
            RequesterId = actorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CurrencyId = 1,
            EstimatedTotalAmount = 100m,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(request);

        var document = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            AttachmentId = Guid.NewGuid(),
            SupplierId = 1,
            SupplierNameSnapshot = "ACME Lda",
            PlantId = 1,
            SourceDocumentType = "PROFORMA",
            DocumentNumber = "FT 2026/1",
            DocumentDate = DateTime.UtcNow.AddDays(-2),
            DueDate = DateTime.UtcNow.AddDays(28),
            Currency = "AOA",
            GrossAmount = 100m,
            SequenceNumber = 1,
            IsVoided = false,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = actorId
        };
        ctx.PaymentSourceDocuments.Add(document);

        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            PaymentSourceDocumentId = document.Id,
            Description = "Serviço",
            Quantity = 1,
            UnitPrice = 100m,
            TotalAmount = 100m,
            IsDeleted = false
        });

        await ctx.SaveChangesAsync();
        return request.Id;
    }

    /// <summary>A legacy PAYMENT with no source documents — the header-only single-group path.</summary>
    private static async Task<Guid> SeedLegacyHeaderPaymentAsync(ApplicationDbContext ctx, Guid actorId)
    {
        SeedCommon(ctx, actorId);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-PAYL-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST legacy payment",
            RequestTypeId = 1,
            StatusId = 4,
            RequesterId = actorId,
            DepartmentId = 1,
            CompanyId = 1,
            PlantId = 1,
            CurrencyId = 1,
            SupplierId = 1,
            EstimatedTotalAmount = 250m,
            PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task A_B_PaymentFinalApproval_MultiDocument_CreatesWaitingPoGroup()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedMultiDocumentPaymentAsync(ctx, actorId);

        var result = await BuildController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "aprovar" });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var groups = await ctx.RequestPoGroups.Where(g => g.RequestId == requestId).ToListAsync();

        // The Buyer's "Registrar P.O." button and the RegisterPo PAYMENT branch both need this group.
        Assert.Single(groups);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo, groups[0].Status);
    }

    [Fact]
    public async Task E_PaymentFinalApproval_KeepsApproved_NotPoRequested()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedMultiDocumentPaymentAsync(ctx, actorId);

        await BuildController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "aprovar" });

        ctx.ChangeTracker.Clear();
        var request = await ctx.Requests.Include(r => r.Status).SingleAsync(r => r.Id == requestId);

        // PAYMENT keeps its live scalar. It must NOT be pushed into PO_REQUESTED just to expose a
        // button — the P.O. action is driven by the WAITING_PO group, not the request status.
        Assert.Equal("APPROVED", request.Status!.Code);
    }

    [Fact]
    public async Task LegacyHeaderPaymentFinalApproval_StillCreatesWaitingPoGroup()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedLegacyHeaderPaymentAsync(ctx, actorId);

        var result = await BuildController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "aprovar" });

        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var groups = await ctx.RequestPoGroups.Where(g => g.RequestId == requestId).ToListAsync();

        // The header-only path never read line items, so it was already correct — pinned here so the
        // Issue-1 fix (loading line items for the document path) does not regress it.
        Assert.Single(groups);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo, groups[0].Status);
    }
}
