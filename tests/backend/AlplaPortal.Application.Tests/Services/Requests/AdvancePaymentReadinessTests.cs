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
using AlplaPortal.Infrastructure.Services.Purchasing;
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
/// v2.229.2 (REQ-17/08/2026-232): the REAL ConfirmAdvancePayment chain against the completion
/// readiness read model — the exact production shape the projection-only Phase 4A pins missed
/// (they seeded groups already inside the paid-stage ladder; the true post-confirmation state
/// is ADVANCE_PAYMENT_COMPLETED, proven paid only by its COMPLETED ADVANCE row).
/// </summary>
public class AdvancePaymentReadinessTests
{
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

        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(
            ctx, NullLogger<StatusAggregationService>.Instance, Options.Create(options)));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new(ClaimTypes.Role, RoleConstants.Finance)
                }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ProofAttachmentId, Guid ActorId);

    /// <summary>
    /// REQ-232's shape: single classified group, TotalAmount 100.000, ADVANCE_FULL 100%,
    /// group ADVANCE_PAYMENT_REQUIRED with its PLANNED ADVANCE row, receipt and Final Invoice
    /// pending, separate fiscal receipt owed.
    /// </summary>
    private static async Task<Seed> SeedFullAdvanceAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Finance 232", Email = "adv232@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 3, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 23, Code = RequestConstants.Statuses.AdvancePaymentRequired, Name = "Adiantamento Necessário", DisplayOrder = 23 },
            new RequestStatus { Id = 24, Code = RequestConstants.Statuses.AdvancePaymentCompleted, Name = "Adiantamento Realizado", DisplayOrder = 24 },
            new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-REQ232-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST full advance readiness",
            RequestTypeId = 3,
            StatusId = 23,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "KWANZA INDUSTRIAL & SERVICOS, LDA (ZZTEST)",
            CurrencyCode = "AOA",
            TotalAmount = 100_000m,
            Status = RequestConstants.PoGroupStatuses.AdvancePaymentRequired,
            PurchaseOrderNumber = "PO-R4A-001-ZZTEST",
            PaymentConditionCode = RequestConstants.PaymentConditions.AdvanceFull,
            AdvancePaymentPercent = 100m,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingUpload,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            RequiresAdvanceRegularization = false,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = request.Id,
            RequestPoGroupId = group.Id,
            PaymentType = RequestPayment.PaymentTypes.Advance,
            PaymentStatus = RequestPayment.PaymentStatuses.Planned,
            PlannedPercent = 100m,
            PlannedAmount = 100_000m,
            CurrencyCode = "AOA",
            CreatedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        });

        var proof = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "comprovativo-adiantamento.pdf",
            FileExtension = ".pdf",
            StorageReference = Guid.NewGuid() + ".pdf",
            AttachmentTypeCode = AttachmentConstants.Types.PaymentProof,
            UploadedByUserId = actor.Id,
            UploadedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(proof);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, proof.Id, actor.Id);
    }

    [Fact]
    public async Task Confirmed_full_advance_reads_payment_satisfied_with_no_phase4_transition()
    {
        using var ctx = NewContext();
        var seed = await SeedFullAdvanceAsync(ctx);
        var flags = new PostPaymentCompletionOptions
        {
            Enabled = true,
            CompletionEnabled = false, // the exact TEST configuration
            EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
        };
        var controller = BuildController(ctx, seed.ActorId, flags);

        // The REAL Finance flow — "Confirmar Adiantamento", 100% with proof.
        var confirm = await controller.ConfirmAdvancePayment(seed.RequestId, new ConfirmAdvancePaymentDto
        {
            RequestPoGroupId = seed.GroupId,
            PaymentProofAttachmentId = seed.ProofAttachmentId,
            ActualPaidAmount = 100_000m,
            PaidDate = new DateTime(2026, 8, 17),
            Comment = "ZZTEST adiantamento integral"
        });
        Assert.IsType<OkObjectResult>(confirm);

        // The controller wrote the production shape: COMPLETED ADVANCE row + advance status.
        var payment = await ctx.RequestPayments.AsNoTracking()
            .SingleAsync(p => p.RequestPoGroupId == seed.GroupId);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, payment.PaymentStatus);
        Assert.Equal(100_000m, payment.ActualPaidAmount);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentCompleted, group.Status);

        // CompletionEnabled=false: no Phase-4 transition, stamp or history of any kind.
        Assert.Null(group.CompletedAtUtc);
        Assert.Null(group.OperationalReceiptCompletedAtUtc);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey != null));

        // The readiness endpoint over the PERSISTED rows: payment satisfied, group honest.
        var action = await controller.GetCompletionReadiness(seed.RequestId);
        var dto = Assert.IsType<CompletionReadinessDto>(((OkObjectResult)action.Result!).Value);

        var readiness = Assert.Single(dto.Groups);
        Assert.True(readiness.PaymentSatisfied);
        Assert.False(readiness.Complete);
        var codes = readiness.BlockingReasons.Select(r => r.Code).ToList();
        Assert.DoesNotContain("PAYMENT_PENDING", codes);
        Assert.Contains("RECEIPT_PENDING", codes);
        Assert.Contains("OPERATION_INVOICE_PENDING", codes);
        Assert.Contains("FISCAL_RECEIPT_PENDING", codes);
    }
}
