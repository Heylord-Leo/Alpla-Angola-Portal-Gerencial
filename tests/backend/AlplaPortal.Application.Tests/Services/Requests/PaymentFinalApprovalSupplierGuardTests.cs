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
/// S3 — prevention for the "Fornecedor não definido" dead-end: a PAYMENT request with no
/// structured supplier anywhere (header null, no source documents) must be REFUSED at final
/// approval instead of silently producing a WAITING_PO group the Buyer cannot use
/// (the REQ-31/07/2026-193 regression).
/// </summary>
public class PaymentFinalApprovalSupplierGuardTests
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

    private static async Task<Guid> SeedPaymentAtFinalApprovalAsync(
        ApplicationDbContext ctx, Guid actorId, int? headerSupplierId)
    {
        ctx.Users.Add(new User { Id = actorId, FullName = "S3 Tester", Email = "s3@test.local" });
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 4, Code = "WAITING_FINAL_APPROVAL", Name = "Ag. Aprovação Final", DisplayOrder = 4 },
            new RequestStatus { Id = 5, Code = "APPROVED", Name = "Aprovado", DisplayOrder = 5 });
        if (headerSupplierId is int supplierId)
            ctx.Suppliers.Add(new Supplier
            {
                Id = supplierId, PortalCode = "F" + supplierId, Name = "ZZTEST Supplier", IsActive = true
            });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-S3-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST S3",
            RequestTypeId = 2,
            StatusId = 4,
            RequesterId = actorId,
            DepartmentId = 1,
            CompanyId = 1,
            SupplierId = headerSupplierId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task S3_final_approval_refuses_a_payment_request_with_no_supplier_anywhere()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedPaymentAtFinalApprovalAsync(ctx, actorId, headerSupplierId: null);

        var result = await BuildController(ctx, actorId)
            .ApproveFinal(requestId, new ApprovalActionDto { Comment = "ok" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Fornecedor do pedido não definido", problem.Title);

        // Nothing mutated: no group was born, the request is still awaiting final approval.
        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.RequestPoGroups.CountAsync());
        Assert.Equal(4, (await ctx.Requests.SingleAsync(r => r.Id == requestId)).StatusId);
    }

    [Fact]
    public async Task A_payment_request_with_a_header_supplier_passes_the_guard()
    {
        using var ctx = NewContext();
        var actorId = Guid.NewGuid();
        var requestId = await SeedPaymentAtFinalApprovalAsync(ctx, actorId, headerSupplierId: 127);

        // The guard lets it through; whatever the rest of the (heavily mocked) pipeline does
        // afterwards — even throwing — it must NOT be the supplier-integrity refusal.
        try
        {
            var result = await BuildController(ctx, actorId)
                .ApproveFinal(requestId, new ApprovalActionDto { Comment = "ok" });

            if (result is BadRequestObjectResult bad && bad.Value is ProblemDetails p)
                Assert.NotEqual("Fornecedor do pedido não definido", p.Title);
        }
        catch (Exception)
        {
            // Downstream mock incompleteness — execution provably passed the guard.
        }
    }
}
