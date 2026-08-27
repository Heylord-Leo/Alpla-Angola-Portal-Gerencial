using System;
using System.Collections.Generic;
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
/// v2.230.0 — lifecycle guards on the request-wide LEGACY approval endpoints
/// (REQ-23/07/2026-140 regression): once any active group crossed the PO gate, neither legacy
/// final approval nor legacy area approval may run again — the re-approval on 2026-08-14/20
/// overwrote a PO_ISSUED request with APPROVED. Remaining items flow through batch endpoints.
/// </summary>
public class MultiGroupLifecycleGuardTests
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

    private static async Task<Guid> SeedQuotationAtFinalApprovalWithIssuedGroupAsync(
        ApplicationDbContext ctx, Guid actorId, string groupStatus)
    {
        ctx.Users.Add(new User { Id = actorId, FullName = "Guard Tester", Email = "guard@test.local" });
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 4, Code = "WAITING_FINAL_APPROVAL", Name = "Ag. Aprovação Final", DisplayOrder = 4 },
            new RequestStatus { Id = 5, Code = "APPROVED", Name = "Aprovado", DisplayOrder = 5 },
            // Phase 4B: legacy QUOTATION final approval now normalizes the scalar to PO_REQUESTED (the
            // canonical post-final-approval state the batch path already produces) when WAITING_PO groups exist.
            new RequestStatus { Id = 6, Code = "PO_REQUESTED", Name = "Aguardando P.O.", DisplayOrder = 6 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-140G-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST multi-group guard",
            RequestTypeId = 1,
            StatusId = 4,
            RequesterId = actorId,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            Status = groupStatus,
            SupplierNameSnapshot = "ZZTEST FORNECEDOR",
            TotalAmount = 100m
        });
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task LegacyFinalApproval_RefusesRequest_WhoseGroupAlreadyCrossedThePoGate()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedQuotationAtFinalApprovalWithIssuedGroupAsync(
            ctx, actorId, RequestConstants.PoGroupStatuses.PoIssued);

        var result = await BuildController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "re-approve attempt" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Fluxo já avançado", problem.Title);

        // Nothing regressed: the request keeps its status; the group keeps PO_ISSUED.
        ctx.ChangeTracker.Clear();
        Assert.Equal(4, (await ctx.Requests.SingleAsync(r => r.Id == requestId)).StatusId);
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued,
            (await ctx.RequestPoGroups.SingleAsync()).Status);
    }

    [Fact]
    public async Task LegacyFinalApproval_RefusesAdvancePaymentTrackGroups_Too()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedQuotationAtFinalApprovalWithIssuedGroupAsync(
            ctx, actorId, RequestConstants.PoGroupStatuses.AdvancePaymentRequired);

        var result = await BuildController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "re-approve attempt" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Fluxo já avançado", problem.Title);
    }

    [Fact]
    public async Task LegacyFinalApproval_StillAllows_PreGateGroups()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedQuotationAtFinalApprovalWithIssuedGroupAsync(
            ctx, actorId, RequestConstants.PoGroupStatuses.Pending);

        var result = await BuildController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "legitimate approval" });

        // Pre-gate groups (PENDING) do not trip the lifecycle guard — the legacy approval
        // proceeds (OkObjectResult) and activates the group to WAITING_PO.
        Assert.IsType<OkObjectResult>(result);
        ctx.ChangeTracker.Clear();
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo,
            (await ctx.RequestPoGroups.SingleAsync()).Status);
    }
}
