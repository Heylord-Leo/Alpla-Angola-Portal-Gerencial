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
/// Release 1 acceptance test for the single production code path this release touches:
/// the Post-Payment guard added to <c>RequestsController.FinalizeRequest</c>.
///
/// The contract being pinned is "no behavioural change while disabled". The first test finalizes
/// a request that owns a PO group — precisely the shape the guard would reject once the feature
/// is on — and asserts it still completes exactly as before. The remaining tests show the guard
/// is real and correctly discriminating once the flag is flipped, so the inertness above is a
/// property of the flag and not of a guard that never runs.
///
/// Follows the established controller-test precedent in this repo (ConfirmAdvancePaymentTests):
/// the controller is instantiated directly over an EF Core InMemory context with every
/// dependency FinalizeRequest does not use mocked away.
/// </summary>
public class FinalizeRequestPostPaymentGuardTests
{
    // FinalizeRequest wraps its work in an explicit transaction. The in-memory store has no
    // transactions, so the warning is downgraded here — the assertions below are about the
    // decision the endpoint takes, not about transactional semantics.
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RequestsController BuildController(
        ApplicationDbContext ctx, Guid actorId, PostPaymentCompletionOptions options)
    {
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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.Finance)
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

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId);

    /// <summary>
    /// A PAYMENT request in WAITING_RECEIPT with one PO group and a legacy RECEIPT attachment —
    /// the exact state Finance finalizes today.
    /// </summary>
    private static async Task<Seed> SeedFinalizableAsync(
        ApplicationDbContext ctx, string finalInvoiceStatus, bool withPoGroup = true)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "Finance Tester", Email = "finalize@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var waitingReceipt = new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 };
        var completed = new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 };
        ctx.RequestStatuses.AddRange(waitingReceipt, completed);

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-FINALIZE-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST finalize guard",
            RequestTypeId = requestType.Id,
            StatusId = waitingReceipt.Id,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
        };
        ctx.Requests.Add(request);

        var groupId = Guid.Empty;
        if (withPoGroup)
        {
            var group = new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierNameSnapshot = "ZZTEST Supplier",
                CurrencyCode = "AOA",
                TotalAmount = 1000m,
                Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
                OperationInvoiceStatus = finalInvoiceStatus,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
                CreatedByUserId = actor.Id
            };
            ctx.RequestPoGroups.Add(group);
            groupId = group.Id;
        }

        ctx.RequestAttachments.Add(new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "recibo.pdf",
            FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_RECEIPT,
            StorageReference = "zztest/recibo.pdf",
            UploadedByUserId = actor.Id,
            UploadedAtUtc = DateTime.UtcNow.AddDays(-1)
        });

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, groupId, actor.Id);
    }

    // ── The Release 1 acceptance criterion ──

    [Fact]
    public async Task Finalize_still_completes_a_grouped_request_while_the_feature_is_disabled()
    {
        using var ctx = NewContext();
        var seed = await SeedFinalizableAsync(ctx, RequestConstants.OperationInvoiceStatuses.Unclassified);

        // Default options: Enabled = false. This is the state shipped in every environment.
        var controller = BuildController(ctx, seed.ActorId, new PostPaymentCompletionOptions());

        var result = await controller.FinalizeRequest(seed.RequestId, new ApprovalActionDto { Comment = "ZZTEST" });

        Assert.IsType<OkObjectResult>(result);

        var status = await ctx.Requests
            .Where(r => r.Id == seed.RequestId)
            .Select(r => r.Status!.Code)
            .FirstAsync();
        Assert.Equal(RequestConstants.Statuses.Completed, status);

        // The legacy path writes its history row exactly as before, with no idempotency key —
        // Release 1 introduces the column, not a new writer.
        var history = await ctx.RequestStatusHistories.SingleAsync(h => h.RequestId == seed.RequestId);
        Assert.Equal("FINALIZE", history.ActionTaken);
        Assert.Null(history.IdempotencyKey);

        // And no post-payment dimension was written anywhere.
        var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == seed.GroupId);
        Assert.Null(group.OperationalReceiptCompletedAtUtc);
        Assert.Null(group.FiscalReceiptAttachmentId);
        Assert.Null(group.CompletedAtUtc);
        Assert.Null(await ctx.Requests.Where(r => r.Id == seed.RequestId)
                                      .Select(r => r.CompletionCycleId).FirstAsync());
    }

    [Fact]
    public async Task Finalize_still_completes_a_groupless_request_while_the_feature_is_disabled()
    {
        using var ctx = NewContext();
        var seed = await SeedFinalizableAsync(
            ctx, RequestConstants.OperationInvoiceStatuses.Unclassified, withPoGroup: false);

        var controller = BuildController(ctx, seed.ActorId, new PostPaymentCompletionOptions());

        var result = await controller.FinalizeRequest(seed.RequestId, new ApprovalActionDto());

        Assert.IsType<OkObjectResult>(result);
    }

    // ── The guard is real once enabled (not part of Release 1 behaviour) ──

    private static PostPaymentCompletionOptions EnabledOptions() => new()
    {
        Enabled = true,
        EffectiveDateUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Enabled_feature_blocks_finalizing_an_unclassified_grouped_request()
    {
        using var ctx = NewContext();
        var seed = await SeedFinalizableAsync(ctx, RequestConstants.OperationInvoiceStatuses.Unclassified);

        var controller = BuildController(ctx, seed.ActorId, EnabledOptions());

        var result = await controller.FinalizeRequest(seed.RequestId, new ApprovalActionDto());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("Classificação Obrigatória", problem.Title);

        var status = await ctx.Requests.Where(r => r.Id == seed.RequestId)
                                       .Select(r => r.Status!.Code).FirstAsync();
        Assert.Equal(RequestConstants.Statuses.WaitingReceipt, status);
    }

    [Fact]
    public async Task Enabled_feature_redirects_a_classified_grouped_request_to_the_new_flow()
    {
        using var ctx = NewContext();
        var seed = await SeedFinalizableAsync(ctx, RequestConstants.OperationInvoiceStatuses.Satisfied);

        var controller = BuildController(ctx, seed.ActorId, EnabledOptions());

        var result = await controller.FinalizeRequest(seed.RequestId, new ApprovalActionDto());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("Fluxo Atualizado", problem.Title);
    }

    [Fact]
    public async Task Enabled_feature_still_allows_the_groupless_legacy_fallback()
    {
        // The only state in which the legacy endpoint remains permitted once the feature is on.
        using var ctx = NewContext();
        var seed = await SeedFinalizableAsync(
            ctx, RequestConstants.OperationInvoiceStatuses.Unclassified, withPoGroup: false);

        var controller = BuildController(ctx, seed.ActorId, EnabledOptions());

        var result = await controller.FinalizeRequest(seed.RequestId, new ApprovalActionDto());

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Enabled_feature_keeps_the_already_completed_response_idempotent()
    {
        using var ctx = NewContext();
        var seed = await SeedFinalizableAsync(ctx, RequestConstants.OperationInvoiceStatuses.Unclassified);

        var completedId = await ctx.RequestStatuses
            .Where(s => s.Code == RequestConstants.Statuses.Completed).Select(s => s.Id).FirstAsync();
        var request = await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId);
        request.StatusId = completedId;
        await ctx.SaveChangesAsync();

        var controller = BuildController(ctx, seed.ActorId, EnabledOptions());

        // A completed request short-circuits BEFORE the guard: an already-closed request must not
        // start reporting an error just because the workflow was switched on.
        var result = await controller.FinalizeRequest(seed.RequestId, new ApprovalActionDto());

        Assert.IsType<OkObjectResult>(result);
    }
}
