using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Release 4 Phase 4B: the fiscal-receipt binding endpoint
/// (POST /api/v1/requests/{requestId}/po-groups/{groupId}/fiscal-receipt).
///
/// Pins authorization (Finance/SysAdmin only — and role alone is never sufficient), every
/// structural prerequisite with its typed code, retry idempotency, the refused replacement, and
/// the atomic bind + FISCAL_RECEIPT_UPLOADED + Phase-1 completion (GC:{GroupId}:{AttachmentId}).
/// </summary>
public class FiscalReceiptUploadTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static PostPaymentCompletionOptions Flags(
        bool enabled = true, bool completion = true) => new()
        {
            Enabled = enabled,
            CompletionEnabled = completion,
            EffectiveDateUtc = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc)
        };

    private static FiscalReceiptsController BuildController(
        ApplicationDbContext ctx, Guid actorId, string role, PostPaymentCompletionOptions? flags = null)
    {
        var options = flags ?? Flags();
        var controller = new FiscalReceiptsController(
            ctx,
            NullLogger<FiscalReceiptsController>.Instance,
            new RequestCompletionService(
                ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance),
            Options.Create(options));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, role)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid AttachmentId, Guid ActorId);

    /// <summary>
    /// A PAYMENT request in WAITING_RECEIPT whose group is fully unlocked for the fiscal receipt
    /// (classified PROFORMA, SATISFIED invoice, receipt stamped, separate receipt owed) with one
    /// stored TYPE_FISCAL_RECEIPT attachment ready to bind.
    /// </summary>
    private static async Task<Seed> SeedAsync(
        ApplicationDbContext ctx,
        Action<RequestPoGroup>? mutateGroup = null,
        Action<RequestAttachment>? mutateAttachment = null,
        string requestStatusCode = RequestConstants.Statuses.WaitingReceipt)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Finance 4B", Email = "fr4b@test.local" };
        ctx.Users.Add(actor);

        var requestType = new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" };
        ctx.RequestTypes.Add(requestType);

        var status = new RequestStatus { Id = 16, Code = requestStatusCode, Name = "ZZTEST Status", DisplayOrder = 17 };
        ctx.RequestStatuses.Add(status);
        if (requestStatusCode != RequestConstants.Statuses.Completed)
        {
            // Phase 4C: the parent transition needs the COMPLETED lookup row.
            ctx.RequestStatuses.Add(new RequestStatus
            { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 });
        }

        var request = new Request
        {
            Id = Guid.NewGuid(),
            RequestNumber = "ZZTEST-FR4B-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ZZTEST fiscal receipt",
            RequestTypeId = requestType.Id,
            StatusId = status.Id,
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
            SupplierNameSnapshot = "ZZTEST FR Supplier",
            CurrencyCode = "AOA",
            TotalAmount = 100_000m,
            Status = RequestConstants.PoGroupStatuses.WaitingFiscalReceipt,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            RequiresOperationInvoice = true,
            RequiresSeparateFiscalReceipt = true,
            OperationalReceiptCompletedAtUtc = DateTime.UtcNow.AddDays(-1),
            OperationalReceiptCompletedByUserId = actor.Id,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            CreatedByUserId = actor.Id
        };
        mutateGroup?.Invoke(group);
        ctx.RequestPoGroups.Add(group);

        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FileName = "recibo-fiscal-zztest.pdf",
            FileExtension = ".pdf",
            StorageReference = Guid.NewGuid() + ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_FISCAL_RECEIPT,
            UploadedByUserId = actor.Id,
            UploadedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        mutateAttachment?.Invoke(attachment);
        ctx.RequestAttachments.Add(attachment);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, group.Id, attachment.Id, actor.Id);
    }

    private static Task<IActionResult> UploadAsync(
        FiscalReceiptsController controller, Seed seed, Guid? attachmentId = null) =>
        controller.Upload(seed.RequestId, seed.GroupId,
            new UploadFiscalReceiptDto { AttachmentId = attachmentId ?? seed.AttachmentId });

    private static string? CodeOf(IActionResult result) =>
        (result as ObjectResult)?.Value is ProblemDetails p && p.Extensions.TryGetValue("code", out var c)
            ? c?.ToString()
            : null;

    // ── A/B: valid uploads by the two permitted roles ──

    [Theory]
    [InlineData(RoleConstants.Finance)]
    [InlineData(RoleConstants.SystemAdministrator)]
    public async Task A_B_finance_and_sysadmin_bind_the_receipt_and_the_group_completes(string role)
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId, role);

        var result = await UploadAsync(controller, seed);

        Assert.IsType<OkObjectResult>(result);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(seed.AttachmentId, group.FiscalReceiptAttachmentId);
        Assert.NotNull(group.FiscalReceiptUploadedAtUtc);
        Assert.Equal(seed.ActorId, group.FiscalReceiptUploadedByUserId);

        // Bind + history + Phase 1 in ONE SaveChanges: the group is COMPLETED with its GC key.
        Assert.Equal(RequestConstants.PoGroupStatuses.Completed, group.Status);
        Assert.NotNull(group.CompletedAtUtc);

        var upload = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUploaded);
        Assert.Equal(
            PostPaymentIdempotencyKeys.FiscalReceiptUploaded(seed.GroupId, seed.AttachmentId),
            upload.IdempotencyKey);
        Assert.Contains("recibo-fiscal-zztest.pdf", upload.Comment);
        Assert.Contains("ZZTEST FR Supplier", upload.Comment);

        var completed = await ctx.RequestStatusHistories.SingleAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted);
        Assert.Equal(
            PostPaymentIdempotencyKeys.GroupCompleted(seed.GroupId, seed.AttachmentId),
            completed.IdempotencyKey);

        // Phase 4C canonical chain: binding → Phase 1 group COMPLETED → commit → Phase 2 parent
        // COMPLETED through the authoritative service (cycle id + RC history), exactly once.
        var request = await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == seed.RequestId);
        Assert.NotNull(request.CompletionCycleId);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ── C/D: forbidden roles ──

    [Theory]
    [InlineData(RoleConstants.Buyer)]
    [InlineData(RoleConstants.Receiving)]
    public async Task C_D_buyer_and_receiving_are_forbidden(string role)
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId, role);

        var result = await UploadAsync(controller, seed);

        Assert.Equal(403, ((ObjectResult)result).StatusCode);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Null(group.FiscalReceiptAttachmentId);
    }

    // ── E: wrong request / wrong group ──

    [Fact]
    public async Task E_unknown_group_and_foreign_request_return_404()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        var unknownGroup = await controller.Upload(seed.RequestId, Guid.NewGuid(),
            new UploadFiscalReceiptDto { AttachmentId = seed.AttachmentId });
        Assert.Equal(404, ((ObjectResult)unknownGroup).StatusCode);

        var unknownRequest = await controller.Upload(Guid.NewGuid(), seed.GroupId,
            new UploadFiscalReceiptDto { AttachmentId = seed.AttachmentId });
        Assert.Equal(404, ((ObjectResult)unknownRequest).StatusCode);
    }

    // ── F: unclassified group is blocked (fail-closed) ──

    [Fact]
    public async Task F_unclassified_group_is_locked()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, g =>
        {
            g.SourceDocumentType = null;
            g.OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Unclassified;
            g.Status = RequestConstants.PoGroupStatuses.WaitingReceipt;
        });
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        var result = await UploadAsync(controller, seed);

        Assert.Equal(FiscalReceiptsController.LockedCode, CodeOf(result));
        Assert.Equal(409, ((ObjectResult)result).StatusCode);
    }

    // ── G: no separate receipt owed ──

    [Fact]
    public async Task G_group_without_separate_receipt_obligation_refuses_upload()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, g =>
        {
            g.RequiresSeparateFiscalReceipt = false;
            g.SourceDocumentType = RequestConstants.SourceDocumentTypes.InvoiceReceipt;
            g.OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.NotRequired;
            g.RequiresOperationInvoice = false;
            g.Status = RequestConstants.PoGroupStatuses.WaitingReceipt;
        });
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        var result = await UploadAsync(controller, seed);

        Assert.Equal(FiscalReceiptsController.NotRequiredCode, CodeOf(result));
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Null(group.FiscalReceiptAttachmentId); // irrelevant evidence never stored
    }

    // ── H: deriver still locked ──

    [Fact]
    public async Task H_receipt_or_invoice_outstanding_keeps_the_upload_locked()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, g =>
        {
            g.OperationalReceiptCompletedAtUtc = null; // operational receipt missing
            g.Status = RequestConstants.PoGroupStatuses.WaitingReceipt;
        });
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        var result = await UploadAsync(controller, seed);

        Assert.Equal(FiscalReceiptsController.LockedCode, CodeOf(result));
        var problem = (ProblemDetails)((ObjectResult)result).Value!;
        Assert.Contains(PostPaymentPendingReason.OperationalReceipt, problem.Detail);
    }

    // ── I: covered by A/B (WAITING_FISCAL_RECEIPT + valid attachment → COMPLETED) ──
    // The seed's group status IS WAITING_FISCAL_RECEIPT; A/B assert stamp+history+completion.

    // ── J: exact retry is idempotent ──

    [Fact]
    public async Task J_same_attachment_retry_is_an_idempotent_success()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        var first = await UploadAsync(controller, seed);
        var second = await UploadAsync(controller, seed);

        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(second);

        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUploaded));
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted));
        // Phase 2 completed the parent exactly once across both calls.
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(
            h => h.ActionTaken == "REQUEST_COMPLETED"));
    }

    // ── K: a different attachment is a refused replacement ──

    [Fact]
    public async Task K_second_different_attachment_is_refused()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);
        var second = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            FileName = "outro-recibo.pdf",
            FileExtension = ".pdf",
            StorageReference = Guid.NewGuid() + ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_FISCAL_RECEIPT,
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(second);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);
        await UploadAsync(controller, seed);

        var result = await UploadAsync(controller, seed, second.Id);

        Assert.Equal(FiscalReceiptsController.AlreadyUploadedCode, CodeOf(result));
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(seed.AttachmentId, group.FiscalReceiptAttachmentId); // original untouched
    }

    // ── L: wrong attachment type ──

    [Fact]
    public async Task L_non_fiscal_receipt_attachment_is_refused()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx,
            mutateAttachment: a => a.AttachmentTypeCode = RequestAttachment.TYPE_OPERATION_INVOICE);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        var result = await UploadAsync(controller, seed);

        Assert.Equal(FiscalReceiptsController.AttachmentInvalidCode, CodeOf(result));
    }

    // ── M: attachment belongs to another request ──

    [Fact]
    public async Task M_attachment_of_another_request_is_refused()
    {
        var options = NewOptions();
        using var ctx = new ApplicationDbContext(options);
        var seed = await SeedAsync(ctx);

        // A fiscal-receipt attachment on a DIFFERENT request.
        var foreign = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            FileName = "alheio.pdf",
            FileExtension = ".pdf",
            StorageReference = Guid.NewGuid() + ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_FISCAL_RECEIPT,
            UploadedByUserId = seed.ActorId,
            UploadedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        };
        ctx.RequestAttachments.Add(foreign);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);
        var result = await UploadAsync(controller, seed, foreign.Id);

        Assert.Equal(FiscalReceiptsController.AttachmentInvalidCode, CodeOf(result));
    }

    // ── M2: attachment already bound to a sibling group ──

    [Fact]
    public async Task M2_attachment_already_bound_to_a_sibling_group_is_refused()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);

        var sibling = new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = seed.RequestId,
            SupplierNameSnapshot = "ZZTEST Sibling",
            CurrencyCode = "AOA",
            TotalAmount = 1m,
            Status = RequestConstants.PoGroupStatuses.Completed,
            FiscalReceiptAttachmentId = seed.AttachmentId,
            FiscalReceiptUploadedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = seed.ActorId
        };
        ctx.RequestPoGroups.Add(sibling);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);
        var result = await UploadAsync(controller, seed);

        Assert.Equal(FiscalReceiptsController.AttachmentInvalidCode, CodeOf(result));
    }

    // ── N: completed request takes no fiscal receipt ──

    [Fact]
    public async Task N_completed_request_is_refused()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, requestStatusCode: RequestConstants.Statuses.Completed);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance);

        var result = await UploadAsync(controller, seed);

        Assert.Equal(FiscalReceiptsController.RequestStateCode, CodeOf(result));
        Assert.Equal(409, ((ObjectResult)result).StatusCode);
    }

    // ── Flag semantics ──

    [Fact]
    public async Task Feature_disabled_hides_the_endpoint_entirely()
    {
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance,
            Flags(enabled: false, completion: false));

        var result = await UploadAsync(controller, seed);

        Assert.Equal(404, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task Binding_works_as_a_dimension_fact_while_completion_is_off()
    {
        // Enabled=true / CompletionEnabled=false: the receipt is stored and audited, but no
        // Phase-1 transition runs — the group completes later, when 4C activates the lifecycle.
        using var ctx = new ApplicationDbContext(NewOptions());
        var seed = await SeedAsync(ctx, g => g.Status = RequestConstants.PoGroupStatuses.WaitingReceipt);
        var controller = BuildController(ctx, seed.ActorId, RoleConstants.Finance,
            Flags(enabled: true, completion: false));

        var result = await UploadAsync(controller, seed);

        Assert.IsType<OkObjectResult>(result);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == seed.GroupId);
        Assert.Equal(seed.AttachmentId, group.FiscalReceiptAttachmentId);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingReceipt, group.Status); // no transition
        Assert.Null(group.CompletedAtUtc);
        Assert.True(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == WorkflowEventCodes.FiscalReceiptUploaded));
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(
            h => h.ActionTaken == WorkflowEventCodes.GroupCompleted));
    }
}
