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
/// Phase 4B — request-lifecycle stabilization. (1) A DRAFT with a PaymentSourceDocument deletes cleanly
/// instead of throwing a raw FK-violation 500 (the PSD graph is removed before its attachment). (2) The
/// legacy QUOTATION final-approval path normalizes the scalar to PO_REQUESTED (matching the batch path)
/// once WAITING_PO groups exist, while PAYMENT keeps APPROVED.
/// </summary>
public class RequestLifecycleStabilizationTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RequestsController Build(ApplicationDbContext ctx, Guid actorId, string role)
    {
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
            Options.Create(new PostPaymentCompletionOptions { Enabled = true, CompletionEnabled = false, EffectiveDateUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) }));
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

    private static void SeedStatuses(ApplicationDbContext ctx)
    {
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = 1, Code = "DRAFT", Name = "Rascunho" },
            new RequestStatus { Id = 2, Code = "WAITING_FINAL_APPROVAL", Name = "Ag. Aprovação Final" },
            new RequestStatus { Id = 3, Code = RequestConstants.Statuses.FinalApproved, Name = "Aprovado" },
            new RequestStatus { Id = 4, Code = RequestConstants.Statuses.PoRequested, Name = "Aguardando P.O." });
        ctx.RequestTypes.AddRange(
            new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" },
            new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
    }

    private static Request SeedDraft(ApplicationDbContext ctx, Guid creator, bool withSourceDocument)
    {
        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-DEL-" + Guid.NewGuid().ToString("N")[..6],
            Title = "t", RequestTypeId = 2, StatusId = 1, RequesterId = creator, CreatedByUserId = creator,
            DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        ctx.Requests.Add(req);
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = req.Id, ActorUserId = creator, ActionTaken = "CREATE",
            PreviousStatusId = 1, NewStatusId = 1, Comment = "created", CreatedAtUtc = DateTime.UtcNow
        });
        if (withSourceDocument)
        {
            var attachment = new RequestAttachment
            {
                Id = Guid.NewGuid(), RequestId = req.Id, FileName = "proforma.pdf", FileExtension = ".pdf",
                AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT, UploadedByUserId = creator, UploadedAtUtc = DateTime.UtcNow
            };
            ctx.Set<RequestAttachment>().Add(attachment);
            var psd = new PaymentSourceDocument { Id = Guid.NewGuid(), RequestId = req.Id, AttachmentId = attachment.Id };
            ctx.PaymentSourceDocuments.Add(psd);
            // a line item that references the source document (the NoAction FK that also must clear)
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "item", Quantity = 1,
                UnitPrice = 10, TotalAmount = 10, PaymentSourceDocumentId = psd.Id
            });
        }
        return req;
    }

    // ── Delete (Fix 1) ──

    [Fact]
    public async Task Delete_Draft_WithPaymentSourceDocument_Succeeds_NoRawFkError()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: true);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await ctx.Requests.AnyAsync(r => r.Id == req.Id));
        Assert.False(await ctx.PaymentSourceDocuments.AnyAsync(p => p.RequestId == req.Id));
    }

    [Fact]
    public async Task Delete_CleanDraft_Succeeds()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: false);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req.Id);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_NonDraft_Returns409()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: false);
        req.StatusId = 2; // WAITING_FINAL_APPROVAL
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req.Id);
        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Delete_ByNonCreatorNonAdmin_Returns403()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var other = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: false);
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, other, RoleConstants.Buyer).DeleteRequest(req.Id);
        Assert.Equal(403, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task Delete_ByCreator_Allowed_And_ByAdmin_Allowed()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var req1 = SeedDraft(ctx, creator, withSourceDocument: true);
        var req2 = SeedDraft(ctx, creator, withSourceDocument: true);
        await ctx.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req1.Id));
        Assert.IsType<NoContentResult>(await Build(ctx, admin, RoleConstants.SystemAdministrator).DeleteRequest(req2.Id));
    }

    // ── Delete (Phase 4B.3): classification-override audit no longer blocks the DRAFT hard-delete ──
    // A DocumentClassificationOverride is written when a source document is classified/overridden and
    // survives the document's removal (append-only audit). Its RequestId→Requests FK is NoAction, so a
    // DRAFT that ever classified a document could not be deleted (REQ-276) until the cleanup removes it.

    private static void AddOverride(ApplicationDbContext ctx, Guid requestId, Guid actorId, string key) =>
        ctx.DocumentClassificationOverrides.Add(new DocumentClassificationOverride
        {
            Id = Guid.NewGuid(), RequestId = requestId, Context = "PAYMENT_REQUEST",
            SelectedType = "PROFORMA", Acknowledged = true, ActorUserId = actorId,
            CreatedAtUtc = DateTime.UtcNow, IdempotencyKey = key
        });

    [Fact] // A
    public async Task Delete_Draft_WithSourceDoc_Item_AndOverride_Succeeds()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: true);
        AddOverride(ctx, req.Id, creator, "DC_OVERRIDE:A:" + req.Id.ToString("N"));
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await ctx.Requests.AnyAsync(r => r.Id == req.Id));
        Assert.False(await ctx.DocumentClassificationOverrides.AnyAsync(o => o.RequestId == req.Id));
    }

    [Fact] // B — REQ-276: document classified then removed; PSD/items gone, override + attachment linger
    public async Task Delete_Draft_RemovedSourceDoc_LingeringOverride_Succeeds()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: false);
        ctx.Set<RequestAttachment>().Add(new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = req.Id, FileName = "removed.pdf", FileExtension = ".pdf",
            AttachmentTypeCode = RequestAttachment.TYPE_PAYMENT_SOURCE_DOCUMENT, UploadedByUserId = creator, UploadedAtUtc = DateTime.UtcNow
        });
        AddOverride(ctx, req.Id, creator, "DC_OVERRIDE:B:" + req.Id.ToString("N"));
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await ctx.DocumentClassificationOverrides.AnyAsync(o => o.RequestId == req.Id));
    }

    [Fact] // C
    public async Task Delete_Draft_MultipleOverrides_AllRemovedBeforeRequest()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: true);
        AddOverride(ctx, req.Id, creator, "DC_OVERRIDE:C1:" + req.Id.ToString("N"));
        AddOverride(ctx, req.Id, creator, "DC_OVERRIDE:C2:" + req.Id.ToString("N"));
        AddOverride(ctx, req.Id, creator, "DC_OVERRIDE:C3:" + req.Id.ToString("N"));
        await ctx.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req.Id));
        Assert.Equal(0, await ctx.DocumentClassificationOverrides.CountAsync(o => o.RequestId == req.Id));
    }

    [Fact] // D — non-DRAFT delete semantics unchanged; audit untouched (cleanup never reached)
    public async Task Delete_NonDraft_WithOverride_Returns409_OverrideUntouched()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: false);
        req.StatusId = 2; // WAITING_FINAL_APPROVAL
        AddOverride(ctx, req.Id, creator, "DC_OVERRIDE:D:" + req.Id.ToString("N"));
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(req.Id);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.True(await ctx.DocumentClassificationOverrides.AnyAsync(o => o.RequestId == req.Id));
        Assert.True(await ctx.Requests.AnyAsync(r => r.Id == req.Id));
    }

    [Fact] // E — authorization unchanged: non-creator/non-admin blocked, nothing written
    public async Task Delete_ByNonCreatorNonAdmin_WithOverride_Returns403_NoWrite()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var other = Guid.NewGuid();
        var req = SeedDraft(ctx, creator, withSourceDocument: false);
        AddOverride(ctx, req.Id, creator, "DC_OVERRIDE:E:" + req.Id.ToString("N"));
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, other, RoleConstants.Buyer).DeleteRequest(req.Id);

        Assert.Equal(403, ((ObjectResult)result).StatusCode);
        Assert.True(await ctx.DocumentClassificationOverrides.AnyAsync(o => o.RequestId == req.Id));
        Assert.True(await ctx.Requests.AnyAsync(r => r.Id == req.Id));
    }

    [Fact] // F — the cleanup is scoped to this request; another request's audit is untouched
    public async Task Delete_Draft_LeavesUnrelatedRequestOverridesIntact()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var creator = Guid.NewGuid();
        var reqDel = SeedDraft(ctx, creator, withSourceDocument: false);
        var reqKeep = SeedDraft(ctx, creator, withSourceDocument: false);
        AddOverride(ctx, reqDel.Id, creator, "DC_OVERRIDE:Fdel:" + reqDel.Id.ToString("N"));
        AddOverride(ctx, reqKeep.Id, creator, "DC_OVERRIDE:Fkeep:" + reqKeep.Id.ToString("N"));
        await ctx.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Build(ctx, creator, RoleConstants.Buyer).DeleteRequest(reqDel.Id));
        Assert.False(await ctx.DocumentClassificationOverrides.AnyAsync(o => o.RequestId == reqDel.Id));
        Assert.True(await ctx.DocumentClassificationOverrides.AnyAsync(o => o.RequestId == reqKeep.Id));
    }

    // ── QUOTATION final-approval status normalization (Fix 3) ──

    [Fact]
    public async Task LegacyQuotationFinalApproval_WithPendingGroup_NormalizesScalarToPoRequested()
    {
        using var ctx = NewContext(); SeedStatuses(ctx);
        var actor = Guid.NewGuid();
        ctx.Users.Add(new User { Id = actor, FullName = "Final Approver", Email = "fa@t.local", IsActive = true });
        var req = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "ZZTEST-Q-" + Guid.NewGuid().ToString("N")[..6],
            Title = "q", RequestTypeId = 1, StatusId = 2 /* WAITING_FINAL_APPROVAL */, RequesterId = actor,
            CreatedByUserId = actor, DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(req);
        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = req.Id, LineNumber = 1, Description = "item", Quantity = 1,
            UnitPrice = 10, TotalAmount = 10, QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved
        });
        // A group created at area approval, still PENDING — final approval activates it to WAITING_PO.
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = req.Id, Status = RequestConstants.PoGroupStatuses.Pending,
            CurrencyCode = "AOA", TotalAmount = 10, CreatedByUserId = actor, CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await Build(ctx, actor, RoleConstants.FinalApprover)
            .ApproveFinal(req.Id, new ApprovalActionDto { Comment = "ok" });

        // The legacy request-wide path now lands the QUOTATION on the canonical PO_REQUESTED scalar
        // (matching the batch path), NOT the stale "APPROVED".
        var status = (await ctx.Requests.Include(r => r.Status).AsNoTracking().FirstAsync(r => r.Id == req.Id)).Status!.Code;
        Assert.Equal(RequestConstants.Statuses.PoRequested, status);
        // …and the group was activated to WAITING_PO (P.O.-eligible).
        Assert.True(await ctx.RequestPoGroups.AnyAsync(g => g.RequestId == req.Id && g.Status == RequestConstants.PoGroupStatuses.WaitingPo));
    }
}
