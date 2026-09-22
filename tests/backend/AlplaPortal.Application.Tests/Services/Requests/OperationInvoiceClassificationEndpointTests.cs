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
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
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

using Agg = RequestConstants.OperationInvoiceStatuses;
using Doc = RequestConstants.OperationInvoiceDocumentStatuses;
using Types = RequestConstants.SourceDocumentTypes;

/// <summary>
/// v2.245.8 — Finance classification of an UNCLASSIFIED legacy P.O. group
/// (<c>POST {requestId}/po-groups/{groupId}/operation-invoice-classification</c>), the create
/// preflight (<c>POST {requestId}/operation-invoices/preflight</c>) and the full legacy path:
/// received + confirmed + supplier-receipt group with schema-default obligation columns →
/// classify → register the final invoice → legacy finalization becomes possible.
/// </summary>
public class OperationInvoiceClassificationEndpointTests
{
    private const int STATUS_PAYMENT_COMPLETED_ID = 14;
    private const int STATUS_WAITING_RECEIPT_ID = 16;
    private const int STATUS_COMPLETED_ID = 17;
    private const int STATUS_IN_FOLLOWUP_ID = 18;
    private const int STATUS_PO_CORRECTION_ID = 19;

    private const string ValidJustification = "ZZTEST Factura Pró-forma anexada ao pedido original";

    private static readonly PostPaymentCompletionOptions Enabled = new()
    {
        Enabled = true, CompletionEnabled = false,
        EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
    };
    private static readonly PostPaymentCompletionOptions Disabled = new() { Enabled = false };

    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ApplicationDbContext NewContext(DbContextOptions<ApplicationDbContext> options) => new(options);

    private static ClaimsPrincipal UserWithRole(Guid actorId, string role) =>
        new(new ClaimsIdentity(new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, role)
        }, "Test"));

    private static ControllerContext Ctx(Guid actorId, string role) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = UserWithRole(actorId, role),
            RequestServices = new ServiceCollection().BuildServiceProvider()
        }
    };

    private static OperationInvoiceClassificationController BuildClassification(
        ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.Finance,
        PostPaymentCompletionOptions? options = null)
    {
        var controller = new OperationInvoiceClassificationController(
            ctx,
            NullLogger<OperationInvoiceClassificationController>.Instance,
            new OperationInvoiceCoverageService(ctx),
            Options.Create(options ?? Enabled));
        controller.ControllerContext = Ctx(actorId, role);
        return controller;
    }

    private static OperationInvoicesController BuildInvoices(
        ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.Finance)
    {
        var controller = new OperationInvoicesController(
            ctx,
            NullLogger<OperationInvoicesController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            new OperationInvoiceCoverageService(ctx));
        controller.ControllerContext = Ctx(actorId, role);
        return controller;
    }

    private static RequestsController BuildRequests(ApplicationDbContext ctx, Guid actorId)
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
            Options.Create(Enabled));
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IStatusAggregationService>().Object);
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(
            ctx, Options.Create(Enabled), NullLogger<RequestCompletionService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = UserWithRole(actorId, RoleConstants.Finance),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId, Guid[] ItemIds);

    /// <summary>
    /// The affected legacy shape: a PAYMENT request at WAITING_RECEIPT whose single group was created with
    /// the feature OFF — <c>SourceDocumentType = null</c>, UNCLASSIFIED, no obligation flags, no expected
    /// total — later received (2/2), confirmed (operational-receipt stamp) and with an active supplier RECEIPT.
    /// </summary>
    private static async Task<Seed> SeedLegacyAsync(
        ApplicationDbContext ctx,
        string requestTypeCode = RequestConstants.Types.Payment,
        int requestStatusId = STATUS_WAITING_RECEIPT_ID,
        string groupStatus = RequestConstants.PoGroupStatuses.WaitingReceipt,
        decimal totalAmount = 250_000m,
        bool withReceipt = true,
        bool withOperationalStamp = true)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Finance", Email = $"fin-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.AddRange(
            new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" },
            new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = STATUS_PAYMENT_COMPLETED_ID, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento Concluído", DisplayOrder = 14 },
            new RequestStatus { Id = STATUS_WAITING_RECEIPT_ID, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 },
            new RequestStatus { Id = STATUS_COMPLETED_ID, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 },
            new RequestStatus { Id = STATUS_IN_FOLLOWUP_ID, Code = RequestConstants.Statuses.InFollowup, Name = "Em Acompanhamento", DisplayOrder = 18 },
            new RequestStatus { Id = STATUS_PO_CORRECTION_ID, Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "P.O. em Correção", DisplayOrder = 12 });
        ctx.LineItemStatuses.AddRange(
            new LineItemStatus { Id = 91, Code = "RECEIVED", Name = "Recebido" },
            new LineItemStatus { Id = 93, Code = "PENDING", Name = "Pendente" });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "500100200" });

        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-CLS-" + Guid.NewGuid().ToString("N")[..8], Title = "ZZTEST legacy classification",
            RequestTypeId = requestTypeCode == RequestConstants.Types.Payment ? 2 : 1, StatusId = requestStatusId,
            RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, PlantId = 1,
            SourceDocumentType = null, // header never carried a type (pre-effective-date request)
            CreatedAtUtc = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc)
        };
        ctx.Requests.Add(request);

        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 10, SupplierNameSnapshot = "ZZTEST Supplier",
            CurrencyCode = "AOA", TotalAmount = totalAmount, Status = groupStatus, PurchaseOrderNumber = "ZZTEST-PO",
            // schema defaults of a group created while the feature was off
            SourceDocumentType = null, OperationInvoiceStatus = Agg.Unclassified,
            RequiresOperationInvoice = false, RequiresSeparateFiscalReceipt = false,
            ExpectedOperationInvoiceTotal = null, ExpectedOperationInvoiceCurrency = null,
            OperationalReceiptCompletedAtUtc = withOperationalStamp ? DateTime.UtcNow.AddDays(-1) : null,
            OperationalReceiptCompletedByUserId = withOperationalStamp ? actor.Id : null,
            CreatedAtUtc = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc), CreatedByUserId = actor.Id
        };
        ctx.RequestPoGroups.Add(group);

        var itemIds = new List<Guid>();
        for (var line = 1; line <= 2; line++)
        {
            var item = new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = request.Id, RequestPoGroupId = group.Id, LineNumber = line,
                Description = "ZZTEST item " + line, Quantity = 1m, ReceivedQuantity = 1m, LineItemStatusId = 91
            };
            ctx.RequestLineItems.Add(item);
            itemIds.Add(item.Id);
        }

        if (withReceipt)
        {
            ctx.RequestAttachments.Add(new RequestAttachment
            {
                Id = Guid.NewGuid(), RequestId = request.Id, AttachmentTypeCode = RequestAttachment.TYPE_RECEIPT,
                FileName = "recibo.pdf", FileExtension = "pdf", FileSizeMBytes = 0.01m, StorageReference = "x/recibo.pdf",
                UploadedByUserId = actor.Id, UploadedAtUtc = DateTime.UtcNow.AddHours(-2), IsDeleted = false
            });
        }

        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = actor.Id, ActionTaken = "CONFIRM_RECEIVING",
            PreviousStatusId = STATUS_PAYMENT_COMPLETED_ID, NewStatusId = STATUS_WAITING_RECEIPT_ID,
            Comment = $"[Grupo P.O.: ZZTEST Supplier | GroupId: {group.Id.ToString().Substring(0, 8)}] ZZTEST",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, actor.Id, itemIds.ToArray());
    }

    private static ClassifyOperationInvoiceDto Dto(string? type = Types.Proforma, string? justification = ValidJustification) =>
        new() { SourceDocumentType = type, Justification = justification };

    private static Task<IActionResult> ClassifyAsync(OperationInvoiceClassificationController c, Seed seed, ClassifyOperationInvoiceDto? dto = null, Guid? groupId = null) =>
        c.Classify(seed.RequestId, groupId ?? seed.GroupId, dto ?? Dto());

    private static async Task<RequestPoGroup> GroupAsync(ApplicationDbContext ctx, Guid groupId) =>
        await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupId);

    private static async Task AssertUntouchedAsync(ApplicationDbContext ctx, Seed seed, int historyBefore)
    {
        var g = await GroupAsync(ctx, seed.GroupId);
        Assert.Null(g.SourceDocumentType);
        Assert.Equal(Agg.Unclassified, g.OperationInvoiceStatus);
        Assert.False(g.RequiresOperationInvoice);
        Assert.Null(g.ExpectedOperationInvoiceTotal);
        Assert.Equal(historyBefore, await ctx.RequestStatusHistories.CountAsync());
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == OperationInvoiceClassificationController.HistoryAction));
    }

    private static void AssertCode(IActionResult result, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(expectedCode, Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
    }

    private static Dictionary<string, string[]> ValidationErrors(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        return problem.Errors.ToDictionary(e => e.Key, e => e.Value);
    }

    // ═══════════════════════════ success ═══════════════════════════

    [Theory]
    [InlineData(RoleConstants.Finance)]
    [InlineData(RoleConstants.SystemAdministrator)]
    public async Task Authorized_role_classifies_the_legacy_group_deriving_obligations_expected_total_and_audit(string role)
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));

        using (var ctx = NewContext(options))
        {
            var result = await ClassifyAsync(BuildClassification(ctx, seed.ActorId, role), seed);

            var body = Assert.IsType<OperationInvoiceClassificationResultDto>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(seed.GroupId, body.GroupId);
            Assert.Null(body.PreviousSourceDocumentType);
            Assert.Equal(Types.Proforma, body.SourceDocumentType);
            Assert.Equal(Agg.PendingUpload, body.OperationInvoiceStatus);
            Assert.True(body.RequiresOperationInvoice);
            Assert.True(body.RequiresSeparateFiscalReceipt);
            Assert.Equal(250_000m, body.ExpectedAmount);
            Assert.Equal("AOA", body.ExpectedCurrency);
        }

        using (var ctx = NewContext(options))
        {
            var g = await GroupAsync(ctx, seed.GroupId);
            // identity + the resolver-derived obligation fields, exactly the group-creation convention
            Assert.Equal(Types.Proforma, g.SourceDocumentType);
            Assert.Equal(Agg.PendingUpload, g.OperationInvoiceStatus);
            Assert.True(g.RequiresOperationInvoice);
            Assert.True(g.RequiresSeparateFiscalReceipt);
            Assert.False(g.RequiresAdvanceRegularization);
            Assert.False(g.RequiresFinanceClassificationReview);
            // expected total = the group's own ordered total, currency = the group's, audit trio stamped
            Assert.Equal(250_000m, g.ExpectedOperationInvoiceTotal);
            Assert.Equal("AOA", g.ExpectedOperationInvoiceCurrency);
            Assert.Equal(seed.ActorId, g.ExpectedTotalSetByUserId);
            Assert.NotNull(g.ExpectedTotalSetAtUtc);
            Assert.Contains("[CLASSIFICAÇÃO]", g.ExpectedTotalJustification);
            Assert.Equal(seed.ActorId, g.UpdatedByUserId);
            // receiving facts, group status and unrelated facts preserved
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, g.Status);
            Assert.NotNull(g.OperationalReceiptCompletedAtUtc);
            Assert.Equal("ZZTEST-PO", g.PurchaseOrderNumber);
            Assert.All(await ctx.RequestLineItems.AsNoTracking().Where(i => i.RequestPoGroupId == seed.GroupId).ToListAsync(),
                i => { Assert.Equal(1m, i.ReceivedQuantity); Assert.Equal(91, i.LineItemStatusId); });
            Assert.True(await ctx.RequestAttachments.AnyAsync(a => a.RequestId == seed.RequestId && a.AttachmentTypeCode == RequestAttachment.TYPE_RECEIPT && !a.IsDeleted));
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == "CONFIRM_RECEIVING"));
            var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
            Assert.Equal(STATUS_WAITING_RECEIPT_ID, request.StatusId); // scalar untouched by classification

            // one explicit audit event: previous → new meaning + obligations + justification
            var audit = await ctx.RequestStatusHistories.SingleAsync(h => h.ActionTaken == OperationInvoiceClassificationController.HistoryAction);
            Assert.Equal(seed.ActorId, audit.ActorUserId);
            Assert.Equal(seed.RequestId, audit.RequestId);
            Assert.Contains($"GroupId: {seed.GroupId.ToString().Substring(0, 8)}", audit.Comment);
            Assert.Contains("Não classificado → Factura Pró-forma", audit.Comment);
            Assert.Contains("UNCLASSIFIED → PENDING_UPLOAD", audit.Comment);
            Assert.Contains("fatura final exigida", audit.Comment);
            Assert.Contains("total esperado AOA", audit.Comment);
            Assert.Contains($"Motivo: {ValidJustification}", audit.Comment);
            Assert.Equal(STATUS_WAITING_RECEIPT_ID, audit.NewStatusId);
        }
    }

    [Fact]
    public async Task Operational_receipt_stamp_and_active_supplier_receipt_never_block_the_first_classification()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options), withReceipt: true, withOperationalStamp: true);
        using var ctx = NewContext(options);
        Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed));
        var g = await GroupAsync(ctx, seed.GroupId);
        Assert.NotNull(g.OperationalReceiptCompletedAtUtc); // preserved, not cleared
        Assert.Equal(Types.Proforma, g.SourceDocumentType);
    }

    [Fact]
    public async Task Invoice_type_owes_no_final_invoice_captures_no_expected_total_and_reads_complete_but_for_the_fiscal_receipt()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);

        var result = await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed, Dto(Types.Invoice));

        var body = Assert.IsType<OperationInvoiceClassificationResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(Agg.NotRequired, body.OperationInvoiceStatus);
        Assert.False(body.RequiresOperationInvoice);
        Assert.True(body.RequiresSeparateFiscalReceipt);
        Assert.Null(body.ExpectedAmount);

        var g = await ctx.RequestPoGroups.Include(x => x.LineItems).ThenInclude(li => li.LineItemStatus).AsNoTracking().SingleAsync(x => x.Id == seed.GroupId);
        Assert.Null(g.ExpectedOperationInvoiceTotal);
        Assert.Null(g.ExpectedTotalSetByUserId);
        var projection = GroupCompletionProjector.Project(g, Array.Empty<RequestPayment>(), Array.Empty<RequestReconciliation>());
        Assert.True(projection.Classified);
        Assert.True(projection.OperationInvoiceSatisfied);
        Assert.True(projection.ReceiptSatisfied);
        Assert.True(projection.PaymentSatisfied);
        Assert.Equal(new[] { GroupCompletionBlockingReasons.FiscalReceiptPending }, projection.BlockingReasons);
        Assert.True(projection.ReadyForFiscalReceipt);
    }

    [Fact]
    public async Task Proforma_classification_reads_classified_with_only_the_final_invoice_pending()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed));

        var g = await ctx.RequestPoGroups.Include(x => x.LineItems).ThenInclude(li => li.LineItemStatus).AsNoTracking().SingleAsync(x => x.Id == seed.GroupId);
        var projection = GroupCompletionProjector.Project(g, Array.Empty<RequestPayment>(), Array.Empty<RequestReconciliation>());
        Assert.True(projection.Classified);
        Assert.DoesNotContain(GroupCompletionBlockingReasons.ClassificationPending, projection.BlockingReasons);
        Assert.Contains(GroupCompletionBlockingReasons.OperationInvoicePending, projection.BlockingReasons);
    }

    [Fact]
    public async Task Quotation_origin_request_classifies_with_the_quotation_context()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options), requestTypeCode: RequestConstants.Types.Quotation);
        using var ctx = NewContext(options);
        Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed, Dto(Types.AdvanceInvoice)));
        var g = await GroupAsync(ctx, seed.GroupId);
        Assert.Equal(Types.AdvanceInvoice, g.SourceDocumentType);
        Assert.True(g.RequiresAdvanceRegularization);
        Assert.True(g.RequiresFinanceClassificationReview);
    }

    [Fact]
    public async Task Legacy_type_aliases_are_normalized_before_persisting()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed, Dto(" estimate ")));
        Assert.Equal(Types.Proforma, (await GroupAsync(ctx, seed.GroupId)).SourceDocumentType);
    }

    [Fact]
    public async Task Multi_group_request_classifies_only_the_selected_group()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        Guid siblingId;
        using (var ctx = NewContext(options))
        {
            var sibling = new RequestPoGroup
            {
                Id = Guid.NewGuid(), RequestId = seed.RequestId, SupplierId = 10, SupplierNameSnapshot = "ZZTEST Sibling", CurrencyCode = "AOA",
                TotalAmount = 1_000m, Status = RequestConstants.PoGroupStatuses.WaitingReceipt, SourceDocumentType = null,
                OperationInvoiceStatus = Agg.Unclassified, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = seed.ActorId
            };
            ctx.RequestPoGroups.Add(sibling);
            await ctx.SaveChangesAsync();
            siblingId = sibling.Id;
        }

        using (var ctx = NewContext(options))
        {
            Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed));
            var sibling = await GroupAsync(ctx, siblingId);
            Assert.Null(sibling.SourceDocumentType);
            Assert.Equal(Agg.Unclassified, sibling.OperationInvoiceStatus);
            Assert.Null(sibling.ExpectedOperationInvoiceTotal);
            var audit = await ctx.RequestStatusHistories.SingleAsync(h => h.ActionTaken == OperationInvoiceClassificationController.HistoryAction);
            Assert.Contains(seed.GroupId.ToString().Substring(0, 8), audit.Comment);
            Assert.DoesNotContain(siblingId.ToString().Substring(0, 8), audit.Comment);
        }
    }

    // ═══════════════════════════ refusals (no writes) ═══════════════════════════

    [Theory]
    [InlineData(RoleConstants.Buyer)]
    [InlineData(RoleConstants.Requester)]
    [InlineData(RoleConstants.Receiving)]
    public async Task Unauthorized_roles_get_403_and_nothing_is_written(string role)
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        var before = await ctx.RequestStatusHistories.CountAsync();

        var result = await ClassifyAsync(BuildClassification(ctx, seed.ActorId, role), seed);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        await AssertUntouchedAsync(ctx, seed, before);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("curto")]
    public async Task Blank_or_meaningless_justification_is_400_and_nothing_is_written(string? justification)
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        var before = await ctx.RequestStatusHistories.CountAsync();

        var result = await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed, Dto(Types.Proforma, justification));

        Assert.Contains("Justification", ValidationErrors(result).Keys);
        await AssertUntouchedAsync(ctx, seed, before);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BANANA")]
    [InlineData("UNCLASSIFIED")]
    [InlineData("INVOICE_RECEIPT")]   // may never originate a payment (BlocksProgression)
    [InlineData("OTHER")]             // needs Finance review — cannot carry the process
    public async Task Invalid_or_ineligible_document_type_is_400_and_nothing_is_written(string? type)
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        var before = await ctx.RequestStatusHistories.CountAsync();

        var result = await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed, Dto(type));

        Assert.Contains("SourceDocumentType", ValidationErrors(result).Keys);
        await AssertUntouchedAsync(ctx, seed, before);
    }

    [Fact]
    public async Task Null_body_is_400_not_a_crash()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        var result = await BuildClassification(ctx, seed.ActorId).Classify(seed.RequestId, seed.GroupId, null);
        var errors = ValidationErrors(result);
        Assert.Contains("SourceDocumentType", errors.Keys);
        Assert.Contains("Justification", errors.Keys);
    }

    [Fact]
    public async Task Unknown_request_unknown_group_and_cross_request_group_are_404_with_no_writes()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        Guid otherRequestId, otherGroupId;
        using (var ctx = NewContext(options))
        {
            var other = new Request
            {
                Id = Guid.NewGuid(), RequestNumber = "ZZTEST-OTHER", Title = "other", RequestTypeId = 2, StatusId = STATUS_WAITING_RECEIPT_ID,
                RequesterId = seed.ActorId, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow
            };
            var otherGroup = new RequestPoGroup
            {
                Id = Guid.NewGuid(), RequestId = other.Id, SupplierNameSnapshot = "Other", CurrencyCode = "AOA", TotalAmount = 5m,
                Status = RequestConstants.PoGroupStatuses.WaitingReceipt, OperationInvoiceStatus = Agg.Unclassified,
                CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = seed.ActorId
            };
            ctx.Requests.Add(other); ctx.RequestPoGroups.Add(otherGroup);
            await ctx.SaveChangesAsync();
            otherRequestId = other.Id; otherGroupId = otherGroup.Id;
        }

        using (var ctx = NewContext(options))
        {
            var before = await ctx.RequestStatusHistories.CountAsync();
            var c = BuildClassification(ctx, seed.ActorId);
            Assert.IsType<NotFoundObjectResult>(await c.Classify(Guid.NewGuid(), seed.GroupId, Dto()));
            Assert.IsType<NotFoundObjectResult>(await c.Classify(seed.RequestId, Guid.NewGuid(), Dto()));
            Assert.IsType<NotFoundObjectResult>(await c.Classify(seed.RequestId, otherGroupId, Dto())); // group of another request
            await AssertUntouchedAsync(ctx, seed, before);
            Assert.Null((await GroupAsync(ctx, otherGroupId)).SourceDocumentType);
            _ = otherRequestId;
        }
    }

    [Fact]
    public async Task A_second_classification_is_refused_and_exactly_one_audit_fact_exists()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using (var ctx = NewContext(options))
            Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed));

        using (var ctx = NewContext(options))
        {
            // a stale second decision (even with a different type) finds the group classified
            var result = await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed, Dto(Types.Invoice));
            AssertCode(result, OperationInvoiceClassificationController.AlreadyClassifiedCode);
            var g = await GroupAsync(ctx, seed.GroupId);
            Assert.Equal(Types.Proforma, g.SourceDocumentType);          // the first decision stands
            Assert.Equal(250_000m, g.ExpectedOperationInvoiceTotal);
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == OperationInvoiceClassificationController.HistoryAction));
        }
    }

    [Fact]
    public async Task Terminal_request_is_409_with_no_writes()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options), requestStatusId: STATUS_COMPLETED_ID);
        using var ctx = NewContext(options);
        var before = await ctx.RequestStatusHistories.CountAsync();
        AssertCode(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed), OperationInvoiceClassificationController.NotEligibleCode);
        await AssertUntouchedAsync(ctx, seed, before);
    }

    [Fact]
    public async Task Request_in_po_correction_is_409_with_no_writes()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options), requestStatusId: STATUS_PO_CORRECTION_ID);
        using var ctx = NewContext(options);
        var before = await ctx.RequestStatusHistories.CountAsync();
        AssertCode(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed), OperationInvoiceClassificationController.NotEligibleCode);
        await AssertUntouchedAsync(ctx, seed, before);
    }

    [Theory]
    [InlineData(RequestConstants.PoGroupStatuses.Cancelled)]
    [InlineData(RequestConstants.PoGroupStatuses.Completed)]
    public async Task Terminal_group_is_409_with_no_writes(string groupStatus)
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options), groupStatus: groupStatus);
        using var ctx = NewContext(options);
        var before = await ctx.RequestStatusHistories.CountAsync();
        AssertCode(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed), OperationInvoiceClassificationController.NotEligibleCode);
        await AssertUntouchedAsync(ctx, seed, before);
    }

    [Theory]
    [InlineData("ALLOCATION")]
    [InlineData("SHORT_CLOSE")]
    [InlineData("FISCAL_RECEIPT")]
    public async Task Genuine_operation_invoice_activity_blocks_classification(string activity)
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using (var ctx = NewContext(options))
        {
            switch (activity)
            {
                case "ALLOCATION":
                    var invoice = new OperationInvoice
                    {
                        Id = Guid.NewGuid(), RequestId = seed.RequestId, AttachmentId = Guid.NewGuid(), Status = Doc.PendingValidation,
                        GrossAmount = 10m, UploadedAtUtc = DateTime.UtcNow, UploadedByUserId = seed.ActorId
                    };
                    ctx.OperationInvoices.Add(invoice);
                    ctx.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
                    {
                        Id = Guid.NewGuid(), OperationInvoiceId = invoice.Id, RequestPoGroupId = seed.GroupId, AllocatedGrossAmount = 10m, SequenceNumber = 1,
                        CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = seed.ActorId
                    });
                    break;
                case "SHORT_CLOSE":
                    ctx.OperationInvoiceShortCloses.Add(new OperationInvoiceShortClose
                    {
                        Id = Guid.NewGuid(), RequestPoGroupId = seed.GroupId, Status = RequestConstants.ShortCloseStatuses.Proposed,
                        ProposedByUserId = seed.ActorId, ProposedAtUtc = DateTime.UtcNow, ProposalJustification = ValidJustification,
                        RemainingAmountAtProposal = 1m
                    });
                    break;
                case "FISCAL_RECEIPT":
                    var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == seed.GroupId);
                    g.FiscalReceiptAttachmentId = Guid.NewGuid();
                    g.FiscalReceiptUploadedAtUtc = DateTime.UtcNow;
                    break;
            }
            await ctx.SaveChangesAsync();
        }

        using (var ctx = NewContext(options))
        {
            var before = await ctx.RequestStatusHistories.CountAsync();
            AssertCode(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed), OperationInvoiceClassificationController.ActivityExistsCode);
            var g = await GroupAsync(ctx, seed.GroupId);
            Assert.Null(g.SourceDocumentType);
            Assert.Equal(before, await ctx.RequestStatusHistories.CountAsync());
        }
    }

    [Fact]
    public async Task While_the_feature_is_disabled_the_endpoint_does_not_exist()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        var before = await ctx.RequestStatusHistories.CountAsync();
        Assert.IsType<NotFoundObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId, options: Disabled), seed));
        await AssertUntouchedAsync(ctx, seed, before);
    }

    // ═══════════════════════════ create preflight ═══════════════════════════

    [Fact]
    public async Task Preflight_refuses_an_unclassified_request_with_the_create_endpoints_own_code_and_writes_nothing()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        var attachmentsBefore = await ctx.RequestAttachments.CountAsync();

        AssertCode(await BuildInvoices(ctx, seed.ActorId).CreatePreflight(seed.RequestId), OperationInvoicesController.NoObligationCode);

        Assert.Equal(attachmentsBefore, await ctx.RequestAttachments.CountAsync());
        Assert.Equal(0, await ctx.OperationInvoices.CountAsync());
    }

    [Fact]
    public async Task Preflight_is_admissible_once_the_group_is_classified()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed));

        var result = await BuildInvoices(ctx, seed.ActorId).CreatePreflight(seed.RequestId);
        var body = Assert.IsType<OperationInvoiceCreatePreflightDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(body.Admissible);
    }

    [Fact]
    public async Task Preflight_mirrors_the_create_role_scope_and_status_gates()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));
        using var ctx = NewContext(options);
        Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed));

        Assert.Equal(403, Assert.IsType<ObjectResult>(await BuildInvoices(ctx, seed.ActorId, RoleConstants.Requester).CreatePreflight(seed.RequestId)).StatusCode);
        Assert.IsType<NotFoundObjectResult>(await BuildInvoices(ctx, seed.ActorId).CreatePreflight(Guid.NewGuid()));

        var request = await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId);
        request.StatusId = STATUS_PO_CORRECTION_ID;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        Assert.IsType<ConflictObjectResult>(await BuildInvoices(ctx, seed.ActorId).CreatePreflight(seed.RequestId));
    }

    // ═══════════════════════════ the full legacy path ═══════════════════════════

    [Fact]
    public async Task Legacy_path_classify_then_register_the_final_invoice_then_legacy_finalization_succeeds()
    {
        var options = NewOptions();
        var seed = await SeedLegacyAsync(NewContext(options));

        // Before: the legacy finalization is refused (R15) and no invoice can be registered.
        using (var ctx = NewContext(options))
        {
            var finalize = await BuildRequests(ctx, seed.ActorId).FinalizeRequest(seed.RequestId, new ApprovalActionDto { Comment = "ZZTEST" });
            var bad = Assert.IsType<BadRequestObjectResult>(finalize);
            Assert.Equal("Classificação Obrigatória", Assert.IsType<ProblemDetails>(bad.Value).Title);

            var attachment = NewInvoiceAttachment(ctx, seed);
            await ctx.SaveChangesAsync();
            AssertCode(await BuildInvoices(ctx, seed.ActorId).Create(seed.RequestId, InvoiceDto(attachment.Id)), OperationInvoicesController.NoObligationCode);
            Assert.Equal(0, await ctx.OperationInvoices.CountAsync());
        }

        // The drawer decision.
        using (var ctx = NewContext(options))
            Assert.IsType<OkObjectResult>(await ClassifyAsync(BuildClassification(ctx, seed.ActorId), seed));

        // Now the existing final-invoice flow is reachable with the derived obligation ...
        using (var ctx = NewContext(options))
        {
            var attachment = NewInvoiceAttachment(ctx, seed);
            await ctx.SaveChangesAsync();
            var created = await BuildInvoices(ctx, seed.ActorId).Create(seed.RequestId, InvoiceDto(attachment.Id));
            var body = Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(created).Value);
            Assert.Equal(Doc.PendingValidation, body.Status);
        }

        // ... and the legacy finalization (Phase-3B window: Enabled=true, CompletionEnabled=false) passes R15.
        using (var ctx = NewContext(options))
        {
            var finalize = await BuildRequests(ctx, seed.ActorId).FinalizeRequest(seed.RequestId, new ApprovalActionDto { Comment = "ZZTEST finalização" });
            Assert.IsType<OkObjectResult>(finalize);
        }

        using (var ctx = NewContext(options))
        {
            var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
            Assert.Equal(RequestConstants.Statuses.Completed, request.Status!.Code);
            var actions = await ctx.RequestStatusHistories.Where(h => h.RequestId == seed.RequestId).Select(h => h.ActionTaken).ToListAsync();
            Assert.Contains(OperationInvoiceClassificationController.HistoryAction, actions);
            Assert.Contains("FATURA_OPERACAO_REGISTADA", actions);
            Assert.Contains("FINALIZE", actions);
            Assert.Equal(1, actions.Count(a => a == OperationInvoiceClassificationController.HistoryAction));
        }
    }

    private static RequestAttachment NewInvoiceAttachment(ApplicationDbContext ctx, Seed seed)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = seed.RequestId, FileName = "fatura.pdf", FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE,
            StorageReference = "zztest/fatura-" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId, UploadedAtUtc = DateTime.UtcNow
        };
        ctx.RequestAttachments.Add(attachment);
        return attachment;
    }

    private static SaveOperationInvoiceDto InvoiceDto(Guid attachmentId) => new()
    {
        AttachmentId = attachmentId, SupplierId = 10, DocumentNumber = "FT ZZTEST/1", DocumentSeries = "A",
        DocumentDate = new DateTime(2026, 9, 1), Currency = "AOA", NetAmount = 219_298.25m, TaxAmount = 30_701.75m, GrossAmount = 250_000m,
        Notes = "ZZTEST fatura final do grupo legado"
    };
}
