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
using AlplaPortal.Domain.Services;
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
/// Release 4 Phase 4D: the completion-readiness read model
/// (GET /requests/{id}/completion-readiness) — a faithful projection of the Phase 4A rulebook
/// with the approved ownership mapping, readable under NORMAL request visibility, and honest
/// while CompletionEnabled=false.
/// </summary>
public class CompletionReadinessEndpointTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RequestsController BuildController(
        ApplicationDbContext ctx, Guid actorId, bool completionEnabled = false, string role = "Requester")
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
            Options.Create(new PostPaymentCompletionOptions
            {
                Enabled = true,
                CompletionEnabled = completionEnabled,
                EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
            }));

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

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId);

    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        Action<RequestPoGroup>? mutateGroup = null,
        Action<ApplicationDbContext, Request, RequestPoGroup>? extraSeed = null)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Readiness", Email = "ready4d@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 });

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-RD4D-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST readiness",
            RequestTypeId = 2,
            StatusId = 16,
            RequesterId = actor.Id,
            DepartmentId = 1,
            CompanyId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            SupplierNameSnapshot = "ZZTEST Readiness Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 100_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            PurchaseOrderNumber = "PO-ZZ-4D",
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            OperationalReceiptCompletedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actor.Id
        };
        mutateGroup?.Invoke(group);
        ctx.RequestPoGroups.Add(group);

        extraSeed?.Invoke(ctx, request, group);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, actor.Id);
    }

    private static async Task<CompletionReadinessDto> GetAsync(
        RequestsController controller, Guid requestId)
    {
        var action = await controller.GetCompletionReadiness(requestId);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<CompletionReadinessDto>(ok.Value);
    }

    // ── A: fully ready group (fiscal receipt uploaded) ──

    [Fact]
    public async Task A_single_ready_group_reads_complete_and_ready()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, g =>
        {
            g.FiscalReceiptAttachmentId = Guid.NewGuid();
            g.FiscalReceiptUploadedAtUtc = DateTime.UtcNow;
        });

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        var group = Assert.Single(dto.Groups);
        Assert.True(group.Complete);
        Assert.Empty(group.BlockingReasons);
        Assert.True(dto.IsCompletionReady);
        Assert.Equal(1, dto.CompletedGroupCount);
        Assert.Equal(0, dto.BlockingGroupCount);
    }

    // ── B/C/D/E/F: each missing dimension with its approved owner ──

    [Theory]
    [InlineData("PO", GroupCompletionBlockingReasons.PoMissing, GroupCompletionOwnership.Buyer)]
    [InlineData("PAYMENT", GroupCompletionBlockingReasons.PaymentPending, GroupCompletionOwnership.Finance)]
    [InlineData("RECEIPT", GroupCompletionBlockingReasons.ReceiptPending, GroupCompletionOwnership.Receiving)]
    [InlineData("INVOICE", GroupCompletionBlockingReasons.OperationInvoicePending, GroupCompletionOwnership.Finance)]
    [InlineData("FISCAL", GroupCompletionBlockingReasons.FiscalReceiptPending, GroupCompletionOwnership.Finance)]
    public async Task B_to_F_each_blocked_dimension_reports_its_owner(
        string scenario, string expectedReason, string expectedOwner)
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, g =>
        {
            switch (scenario)
            {
                case "PO": g.Status = RequestConstants.PoGroupStatuses.WaitingPo; break;
                case "PAYMENT": g.Status = RequestConstants.PoGroupStatuses.PaymentScheduled; break;
                case "RECEIPT": g.OperationalReceiptCompletedAtUtc = null; break;
                case "INVOICE": g.OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingValidation; break;
                case "FISCAL": break; // receipt required and absent — the seed default
            }
        });

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        var group = Assert.Single(dto.Groups);
        Assert.False(group.Complete);
        Assert.False(dto.IsCompletionReady);
        Assert.Contains(group.BlockingReasons, r => r.Code == expectedReason && r.OwnerCode == expectedOwner);
    }

    [Fact]
    public async Task B2_po_correction_reports_the_buyer_as_owner()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, g => g.Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection);

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        var group = Assert.Single(dto.Groups);
        Assert.False(group.NoBlockingCorrection);
        Assert.Contains(group.BlockingReasons, r =>
            r.Code == GroupCompletionBlockingReasons.PoCorrectionPending &&
            r.OwnerCode == GroupCompletionOwnership.Buyer);
    }

    // ── G: no separate fiscal receipt owed → satisfied without evidence ──

    [Fact]
    public async Task G_group_without_separate_receipt_is_satisfied_without_an_attachment()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, g => g.RequiresSeparateFiscalReceipt = false);

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        var group = Assert.Single(dto.Groups);
        Assert.False(group.FiscalReceiptRequired);
        Assert.True(group.FiscalReceiptSatisfied);
        Assert.Null(group.FiscalReceipt);
        Assert.True(group.Complete);
        Assert.True(dto.IsCompletionReady);
    }

    // ── H: legacy UNCLASSIFIED fails closed with the Finance/Admin owner ──

    [Fact]
    public async Task H_unclassified_group_reports_classification_pending()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, g =>
        {
            g.SourceDocumentType = null;
            g.OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Unclassified;
        });

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        var group = Assert.Single(dto.Groups);
        Assert.False(group.Classified);
        Assert.False(group.Complete);
        Assert.Equal(GroupCompletionBlockingReasons.ClassificationPending, group.BlockingReasons.First().Code);
        Assert.Equal(GroupCompletionOwnership.FinanceAdmin, group.BlockingReasons.First().OwnerCode);
    }

    // ── I: multi-group mixed readiness — the request-level result is authoritative ──

    [Fact]
    public async Task I_mixed_groups_keep_the_request_pending_with_honest_counts()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx,
            mutateGroup: g =>
            {
                g.Status = RequestConstants.PoGroupStatuses.Completed;
                g.RequiresSeparateFiscalReceipt = false;
                g.CompletedAtUtc = DateTime.UtcNow;
            },
            extraSeed: (c, request, _) => c.RequestPoGroups.Add(new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                SupplierNameSnapshot = "ZZTEST Blocked Sibling",
                CurrencyCode = "AOA",
                TotalAmount = 1m,
                Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
                SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
                OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.PendingUpload,
                RequiresOperationInvoice = true,
                RequiresSeparateFiscalReceipt = true,
                OperationalReceiptCompletedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = request.RequesterId
            }));

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        Assert.Equal(2, dto.TotalGroupCount);
        Assert.Equal(1, dto.CompletedGroupCount);
        Assert.Equal(1, dto.BlockingGroupCount);
        Assert.False(dto.IsCompletionReady);
        Assert.False(dto.IsCompleted);
    }

    // ── J: ClosedShort evidence rides the projection ──

    [Fact]
    public async Task J_approved_short_close_is_exposed_as_closed_short()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx,
            mutateGroup: g => g.RequiresSeparateFiscalReceipt = false,
            extraSeed: (c, _, group) => c.OperationInvoiceShortCloses.Add(new OperationInvoiceShortClose
            {
                Id = Guid.NewGuid(),
                RequestPoGroupId = group.Id,
                Status = RequestConstants.ShortCloseStatuses.Approved,
                ProposedByUserId = Guid.NewGuid(),
                ProposedAtUtc = DateTime.UtcNow.AddDays(-1),
                ProposalJustification = "ZZTEST encerramento por saldo aceite para o teste 4D.",
                RemainingAmountAtProposal = 10_000m
            }));

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        var group = Assert.Single(dto.Groups);
        Assert.True(group.ClosedShort);
        Assert.True(group.OperationInvoiceSatisfied);
        Assert.True(group.Complete);
    }

    // ── L: CompletionEnabled=false still returns honest facts, flagged as inactive ──

    [Fact]
    public async Task L_completion_disabled_returns_honest_readiness_with_the_lifecycle_flag_off()
    {
        using var ctx = NewContext();
        var seed = await SeedAsync(ctx, g => g.RequiresSeparateFiscalReceipt = false);

        var dto = await GetAsync(BuildController(ctx, seed.ActorId, completionEnabled: false), seed.RequestId);

        Assert.False(dto.CompletionLifecycleEnabled);
        Assert.True(dto.IsCompletionReady); // the facts, honestly — activation is a separate truth
        Assert.False(dto.IsCompleted);

        var enabledDto = await GetAsync(BuildController(ctx, seed.ActorId, completionEnabled: true), seed.RequestId);
        Assert.True(enabledDto.CompletionLifecycleEnabled);
    }

    // ── Fiscal receipt evidence summary ──

    [Fact]
    public async Task Fiscal_receipt_evidence_carries_file_uploader_and_instant()
    {
        using var ctx = NewContext();
        Guid attachmentId = Guid.NewGuid();
        var seed = await SeedAsync(ctx,
            mutateGroup: g => { },
            extraSeed: (c, request, group) =>
            {
                var uploader = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Finance Uploader", Email = "up4d@test.local" };
                c.Users.Add(uploader);
                c.RequestAttachments.Add(new RequestAttachment
                {
                    Id = attachmentId,
                    RequestId = request.Id,
                    FileName = "recibo-4d.pdf",
                    FileExtension = ".pdf",
                    StorageReference = attachmentId + ".pdf",
                    AttachmentTypeCode = RequestAttachment.TYPE_FISCAL_RECEIPT,
                    UploadedByUserId = uploader.Id,
                    UploadedAtUtc = DateTime.UtcNow,
                    IsDeleted = false
                });
                group.FiscalReceiptAttachmentId = attachmentId;
                group.FiscalReceiptUploadedAtUtc = DateTime.UtcNow;
                group.FiscalReceiptUploadedByUserId = uploader.Id;
            });

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        var receipt = Assert.Single(dto.Groups).FiscalReceipt;
        Assert.NotNull(receipt);
        Assert.Equal(attachmentId, receipt!.AttachmentId);
        Assert.Equal("recibo-4d.pdf", receipt.FileName);
        Assert.Equal("ZZTEST Finance Uploader", receipt.UploadedByName);
        Assert.NotNull(receipt.UploadedAtUtc);
    }

    // ── Completed request reads completed with its instant ──

    [Fact]
    public async Task Completed_request_reports_completed_with_the_rc_instant()
    {
        using var ctx = NewContext();
        var completedAt = DateTime.UtcNow.AddHours(-2);
        var seed = await SeedAsync(ctx,
            mutateGroup: g =>
            {
                g.Status = RequestConstants.PoGroupStatuses.Completed;
                g.RequiresSeparateFiscalReceipt = false;
                g.CompletedAtUtc = completedAt;
            },
            extraSeed: (c, request, _) =>
            {
                request.StatusId = 17;
                c.RequestStatusHistories.Add(new RequestStatusHistory
                {
                    Id = Guid.NewGuid(),
                    RequestId = request.Id,
                    ActorUserId = request.RequesterId,
                    ActionTaken = "REQUEST_COMPLETED",
                    NewStatusId = 17,
                    Comment = "ZZTEST",
                    CreatedAtUtc = completedAt
                });
            });

        var dto = await GetAsync(BuildController(ctx, seed.ActorId), seed.RequestId);

        Assert.True(dto.IsCompleted);
        Assert.Equal(completedAt, dto.CompletedAtUtc);
        Assert.Equal(RequestConstants.Statuses.Completed, dto.RequestStatusCode);
    }
}
