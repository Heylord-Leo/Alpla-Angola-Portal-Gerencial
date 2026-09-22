using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

using Agg = RequestConstants.OperationInvoiceStatuses;
using Doc = RequestConstants.OperationInvoiceDocumentStatuses;

/// <summary>
/// v2.245.8 hardening — the lifecycle of an uploaded OPERATION_INVOICE attachment when the invoice
/// registration is interrupted after the upload. The attachment is a durable server fact: the backend
/// lists the ones no invoice claims (<c>GET unclaimed-attachments</c>), the create is idempotent per
/// attachment (a lost successful response is recovered by retrying), a concurrent second create is
/// refused by the attachment claim, and the ONLY deletion (<c>POST attachments/{id}/release</c>) is
/// narrow: never a claimed, foreign, deleted, voided or non-invoice attachment.
/// </summary>
public class OperationInvoiceAttachmentRecoveryTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ApplicationDbContext NewContext(DbContextOptions<ApplicationDbContext> options) => new(options);

    private static OperationInvoicesController Build(ApplicationDbContext ctx, Guid actorId, string role = RoleConstants.Finance)
    {
        var controller = new OperationInvoicesController(
            ctx,
            NullLogger<OperationInvoicesController>.Instance,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            new AlplaPortal.Infrastructure.Services.OperationInvoiceCoverageService(ctx));
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

    private sealed record Seed(Guid RequestId, Guid ActorId);

    /// <summary>A classified request whose group owes an invoice (the create is admissible).</summary>
    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx)
    {
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST Finance", Email = $"rec-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 16, Code = RequestConstants.Statuses.WaitingReceipt, Name = "Aguardando Recibo", DisplayOrder = 17 });
        ctx.Suppliers.Add(new Supplier { Id = 10, Name = "ZZTEST Supplier", TaxId = "500100200" });
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-REC-" + Guid.NewGuid().ToString("N")[..8], Title = "ZZTEST recovery",
            RequestTypeId = 2, StatusId = 16, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, PlantId = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
        };
        ctx.Requests.Add(request);
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 10, SupplierNameSnapshot = "ZZTEST Supplier", CurrencyCode = "AOA",
            TotalAmount = 250_000m, Status = RequestConstants.PoGroupStatuses.WaitingReceipt,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma, OperationInvoiceStatus = Agg.PendingUpload,
            RequiresOperationInvoice = true, ExpectedOperationInvoiceTotal = 250_000m, ExpectedOperationInvoiceCurrency = "AOA",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5), CreatedByUserId = actor.Id
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return new Seed(request.Id, actor.Id);
    }

    private static async Task<Guid> UploadAsync(ApplicationDbContext ctx, Seed seed, string type = RequestAttachment.TYPE_OPERATION_INVOICE,
        bool deleted = false, bool voided = false, Guid? requestId = null, string fileName = "fatura.pdf", DateTime? at = null)
    {
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = requestId ?? seed.RequestId, FileName = fileName, FileExtension = ".pdf", FileSizeMBytes = 0.2m,
            FileHash = Guid.NewGuid().ToString("N"), AttachmentTypeCode = type,
            StorageReference = "zztest/" + Guid.NewGuid().ToString("N")[..8] + ".pdf",
            UploadedByUserId = seed.ActorId, UploadedAtUtc = at ?? DateTime.UtcNow, IsDeleted = deleted,
            VoidedAtUtc = voided ? DateTime.UtcNow : null
        };
        ctx.RequestAttachments.Add(attachment);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return attachment.Id;
    }

    private static SaveOperationInvoiceDto Dto(Guid attachmentId, string number = "FT ZZTEST/1") => new()
    {
        AttachmentId = attachmentId, SupplierId = 10, DocumentNumber = number, DocumentSeries = "A",
        DocumentDate = new DateTime(2026, 9, 1), Currency = "AOA", NetAmount = 219_298.25m, TaxAmount = 30_701.75m, GrossAmount = 250_000m
    };

    private static List<OperationInvoiceUnclaimedAttachmentDto> Unclaimed(ActionResult<List<OperationInvoiceUnclaimedAttachmentDto>> result) =>
        Assert.IsType<List<OperationInvoiceUnclaimedAttachmentDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static void AssertCode(IActionResult result, string code) =>
        Assert.Equal(code, Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(result).Value).Extensions["code"]);

    // ── recovery read ──

    [Fact]
    public async Task Business_rejection_after_upload_leaves_the_attachment_listed_as_unclaimed_and_reusable()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        Guid first, second;
        using (var ctx = NewContext(options))
        {
            first = await UploadAsync(ctx, seed, fileName: "a.pdf");
            Assert.IsType<OkObjectResult>(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(first, "FT 1")));
            // the second registration is refused by a BUSINESS rule (duplicate fiscal identity) AFTER its upload
            second = await UploadAsync(ctx, seed, fileName: "b.pdf");
            AssertCode(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(second, "FT 1")), OperationInvoicesController.DuplicateErrorCode);
        }
        using (var ctx = NewContext(options))
        {
            var list = Unclaimed(await Build(ctx, seed.ActorId).ListUnclaimedAttachments(seed.RequestId));
            var only = Assert.Single(list);
            Assert.Equal(second, only.AttachmentId);      // not unreachable: recoverable by any session
            Assert.Equal("b.pdf", only.FileName);
            Assert.Equal("ZZTEST Finance", only.UploadedByName);
            // ... and reusable: the corrected retry claims it without a new upload
            Assert.IsType<OkObjectResult>(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(second, "FT 2")));
            Assert.Empty(Unclaimed(await Build(ctx, seed.ActorId).ListUnclaimedAttachments(seed.RequestId)));
        }
    }

    [Fact]
    public async Task Unclaimed_list_excludes_claimed_deleted_voided_other_types_and_other_requests()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var unclaimed = await UploadAsync(ctx, seed, fileName: "keep.pdf");
        var claimed = await UploadAsync(ctx, seed);
        Assert.IsType<OkObjectResult>(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(claimed)));
        await UploadAsync(ctx, seed, deleted: true);
        await UploadAsync(ctx, seed, voided: true);
        await UploadAsync(ctx, seed, type: RequestAttachment.TYPE_RECEIPT);
        var other = new Request { Id = Guid.NewGuid(), RequestNumber = "ZZTEST-OTHER", Title = "o", RequestTypeId = 2, StatusId = 16, RequesterId = seed.ActorId, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(other); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        await UploadAsync(ctx, seed, requestId: other.Id);

        var list = Unclaimed(await Build(ctx, seed.ActorId).ListUnclaimedAttachments(seed.RequestId));
        Assert.Equal(unclaimed, Assert.Single(list).AttachmentId);
    }

    // ── idempotent create ──

    [Fact]
    public async Task Lost_successful_response_then_retry_returns_the_same_invoice_never_a_second_one()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var attachmentId = await UploadAsync(ctx, seed);

        var first = Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(attachmentId))).Value);
        // the client never saw `first` (timeout) and retries with the same attachment — even with edited fields
        var retry = Assert.IsType<OperationInvoiceDto>(Assert.IsType<OkObjectResult>(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(attachmentId, "FT OTHER"))).Value);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == "FATURA_OPERACAO_REGISTADA"));
        Assert.Empty(Unclaimed(await Build(ctx, seed.ActorId).ListUnclaimedAttachments(seed.RequestId)));
    }

    // ── release ──

    [Fact]
    public async Task Choosing_another_file_releases_the_previous_unclaimed_upload_only_soft_delete_with_history()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var previous = await UploadAsync(ctx, seed, fileName: "old.pdf");
        var untouched = await UploadAsync(ctx, seed, fileName: "other-session.pdf");

        Assert.IsType<NoContentResult>(await Build(ctx, seed.ActorId).ReleaseUnclaimedAttachment(seed.RequestId, previous));

        var row = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == previous);
        Assert.True(row.IsDeleted);                       // soft delete, storage untouched
        Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == untouched)).IsDeleted);
        var history = await ctx.RequestStatusHistories.SingleAsync(h => h.ActionTaken == "DOCUMENTO REMOVIDO");
        Assert.Contains("old.pdf", history.Comment);
        Assert.Contains("sem fatura associada", history.Comment);
        var list = Unclaimed(await Build(ctx, seed.ActorId).ListUnclaimedAttachments(seed.RequestId));
        Assert.Equal(untouched, Assert.Single(list).AttachmentId);
        // a released attachment can no longer be claimed
        Assert.IsType<BadRequestObjectResult>(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(previous)));
    }

    [Fact]
    public async Task Release_never_deletes_a_claimed_attachment()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var attachmentId = await UploadAsync(ctx, seed);
        Assert.IsType<OkObjectResult>(await Build(ctx, seed.ActorId).Create(seed.RequestId, Dto(attachmentId)));
        var historyBefore = await ctx.RequestStatusHistories.CountAsync();

        AssertCode(await Build(ctx, seed.ActorId).ReleaseUnclaimedAttachment(seed.RequestId, attachmentId), OperationInvoicesController.AttachmentClaimedCode);

        Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId)).IsDeleted);
        Assert.Equal(historyBefore, await ctx.RequestStatusHistories.CountAsync());
        Assert.Equal(1, await ctx.OperationInvoices.CountAsync());
    }

    [Fact]
    public async Task Release_refuses_deleted_voided_non_invoice_foreign_and_unknown_attachments_without_writes()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var deleted = await UploadAsync(ctx, seed, deleted: true);
        var voided = await UploadAsync(ctx, seed, voided: true);
        var receipt = await UploadAsync(ctx, seed, type: RequestAttachment.TYPE_RECEIPT);
        var other = new Request { Id = Guid.NewGuid(), RequestNumber = "ZZTEST-OTHER", Title = "o", RequestTypeId = 2, StatusId = 16, RequesterId = seed.ActorId, DepartmentId = 1, CompanyId = 1, PlantId = 1, CreatedAtUtc = DateTime.UtcNow };
        ctx.Requests.Add(other); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var foreign = await UploadAsync(ctx, seed, requestId: other.Id);
        var historyBefore = await ctx.RequestStatusHistories.CountAsync();
        var c = Build(ctx, seed.ActorId);

        AssertCode(await c.ReleaseUnclaimedAttachment(seed.RequestId, deleted), OperationInvoicesController.AttachmentNotReleasableCode);
        AssertCode(await c.ReleaseUnclaimedAttachment(seed.RequestId, voided), OperationInvoicesController.AttachmentNotReleasableCode);
        AssertCode(await c.ReleaseUnclaimedAttachment(seed.RequestId, receipt), OperationInvoicesController.AttachmentNotReleasableCode);
        Assert.IsType<NotFoundObjectResult>(await c.ReleaseUnclaimedAttachment(seed.RequestId, foreign));   // anti-enumeration
        Assert.IsType<NotFoundObjectResult>(await c.ReleaseUnclaimedAttachment(seed.RequestId, Guid.NewGuid()));
        Assert.IsType<NotFoundObjectResult>(await c.ReleaseUnclaimedAttachment(Guid.NewGuid(), foreign));

        Assert.Equal(historyBefore, await ctx.RequestStatusHistories.CountAsync());
        Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == receipt)).IsDeleted);
        Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == foreign)).IsDeleted);
    }

    [Theory]
    [InlineData(RoleConstants.Requester)]
    [InlineData(RoleConstants.Receiving)]
    public async Task Release_requires_a_mutation_role(string role)
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var attachmentId = await UploadAsync(ctx, seed);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Build(ctx, seed.ActorId, role).ReleaseUnclaimedAttachment(seed.RequestId, attachmentId)).StatusCode);
        Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId)).IsDeleted);
    }

    // ── concurrency ──

    [Fact]
    public async Task Concurrent_retry_from_another_session_cannot_create_a_second_invoice_for_the_same_upload()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        var attachmentId = await UploadAsync(NewContext(options), seed);

        // session A registers; session B (stale, believes the create failed) retries with the same attachment
        using (var a = NewContext(options))
            Assert.IsType<OkObjectResult>(await Build(a, seed.ActorId).Create(seed.RequestId, Dto(attachmentId)));
        using (var b = NewContext(options))
        {
            var retry = Assert.IsType<OkObjectResult>(await Build(b, seed.ActorId).Create(seed.RequestId, Dto(attachmentId, "FT B")));
            Assert.Equal(1, await b.OperationInvoices.CountAsync());
            Assert.NotNull(retry.Value);
        }
        // and a release attempted by session B after A's create is refused — the evidence stays
        using (var b = NewContext(options))
        {
            AssertCode(await Build(b, seed.ActorId).ReleaseUnclaimedAttachment(seed.RequestId, attachmentId), OperationInvoicesController.AttachmentClaimedCode);
            Assert.False((await b.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == attachmentId)).IsDeleted);
        }
    }

    [Fact]
    public async Task Release_is_idempotent_in_effect_a_second_release_finds_nothing_to_release()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var attachmentId = await UploadAsync(ctx, seed);
        Assert.IsType<NoContentResult>(await Build(ctx, seed.ActorId).ReleaseUnclaimedAttachment(seed.RequestId, attachmentId));
        AssertCode(await Build(ctx, seed.ActorId).ReleaseUnclaimedAttachment(seed.RequestId, attachmentId), OperationInvoicesController.AttachmentNotReleasableCode);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == "DOCUMENTO REMOVIDO"));
    }

    [Fact]
    public async Task Preflight_rejection_leaves_nothing_uploaded_and_the_unclaimed_list_empty()
    {
        var options = NewOptions();
        var seed = await SeedAsync(NewContext(options));
        using var ctx = NewContext(options);
        var request = await ctx.Requests.SingleAsync(r => r.Id == seed.RequestId);
        ctx.RequestStatuses.Add(new RequestStatus { Id = 17, Code = RequestConstants.Statuses.Completed, Name = "Finalizado", DisplayOrder = 19 });
        request.StatusId = 17;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        Assert.IsType<ConflictObjectResult>(await Build(ctx, seed.ActorId).CreatePreflight(seed.RequestId));
        Assert.Equal(0, await ctx.RequestAttachments.CountAsync());
        Assert.Empty(Unclaimed(await Build(ctx, seed.ActorId).ListUnclaimedAttachments(seed.RequestId)));
    }
}
