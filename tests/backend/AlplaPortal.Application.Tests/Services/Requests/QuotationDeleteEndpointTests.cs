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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// DELETE …/quotations/{id} and PUT …/quotations/{id} guards behind the Workspace "Excluir"/"Editar" actions:
/// quotation window, access scope (added to DELETE for parity with PUT), approval-batch references, and the
/// endpoint's real side effects (lines removed, linked document soft-deleted, audit rows).
/// </summary>
public class QuotationDeleteEndpointTests
{
    private const int S_WAITING_QUOTATION = 1, S_FINAL_APPROVED = 2;
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed { public Guid RequestId, QuotationId, ItemA, ItemB, LineItemId, AttachmentId, Buyer, OtherBuyer; }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string status = RequestConstants.Statuses.WaitingQuotation, bool withAttachment = true, bool referencedByBatch = false)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), QuotationId = Guid.NewGuid(), ItemA = Guid.NewGuid(), ItemB = Guid.NewGuid(), LineItemId = Guid.NewGuid(), AttachmentId = Guid.NewGuid(), Buyer = Guid.NewGuid(), OtherBuyer = Guid.NewGuid() };
        if (!await ctx.RequestTypes.AnyAsync())
        {
            ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
            ctx.RequestStatuses.AddRange(
                new RequestStatus { Id = S_WAITING_QUOTATION, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Aguardando Cotação" },
                new RequestStatus { Id = S_FINAL_APPROVED, Code = RequestConstants.Statuses.FinalApproved, Name = "Aprovado" });
            ctx.Companies.Add(new Company { Id = 1, Name = "Alpla", IsActive = true });
            ctx.Plants.AddRange(new Plant { Id = 1, Name = "Viana 3", CompanyId = 1 }, new Plant { Id = 9, Name = "Outra", CompanyId = 1 });
            ctx.Set<Department>().Add(new Department { Id = 5, Name = "Manutenção", IsActive = true });
        }
        ctx.Users.AddRange(new User { Id = s.Buyer, FullName = "Samuel Mpoco", Email = "samuel@test.local", IsActive = true },
                           new User { Id = s.OtherBuyer, FullName = "Outro Comprador", Email = "outro@test.local", IsActive = true });
        ctx.Requests.Add(new Request
        {
            Id = s.RequestId, RequestNumber = "REQ-15/10/2026-578", Title = "Lã mineral", RequestTypeId = 1,
            StatusId = status == RequestConstants.Statuses.WaitingQuotation ? S_WAITING_QUOTATION : S_FINAL_APPROVED,
            RequesterId = s.Buyer, BuyerId = s.Buyer, DepartmentId = 5, CompanyId = 1, PlantId = 1, EstimatedTotalAmount = 0m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1), UpdatedAtUtc = DateTime.UtcNow
        });
        ctx.RequestLineItems.Add(new RequestLineItem { Id = s.LineItemId, RequestId = s.RequestId, LineNumber = 1, Description = "LA MINERAL 60mm", Quantity = 1, CreatedAtUtc = DateTime.UtcNow });
        if (withAttachment)
            ctx.RequestAttachments.Add(new RequestAttachment { Id = s.AttachmentId, RequestId = s.RequestId, AttachmentTypeCode = RequestAttachment.TYPE_QUOTATION, FileName = "OR3602.pdf", StorageReference = "OR3602.pdf", UploadedByUserId = s.Buyer, UploadedAtUtc = DateTime.UtcNow });
        ctx.Quotations.Add(new Quotation
        {
            Id = s.QuotationId, RequestId = s.RequestId, SupplierNameSnapshot = "Socimorcasal Angola", DocumentNumber = "OR 3602/10/CM", Currency = "AOA",
            TotalAmount = 239099.04m, SourceType = "OCR", ProformaAttachmentId = withAttachment ? s.AttachmentId : null, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Buyer,
            Items = new List<QuotationItem>
            {
                new() { Id = s.ItemA, Description = "Lã 60mm", LineNumber = 1, Quantity = 1, UnitPrice = 110808m, LineTotal = 126321.12m, ReconciliationStatus = "EXTRA_ITEM" },
                new() { Id = s.ItemB, Description = "Lã 100mm", LineNumber = 2, Quantity = 1, UnitPrice = 98928m, LineTotal = 112777.92m, ReconciliationStatus = "EXTRA_ITEM" },
            }
        });
        if (referencedByBatch)
        {
            var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = s.RequestId, BatchNumber = 1, Status = RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Buyer };
            var bi = new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = s.LineItemId, CreatedAtUtc = DateTime.UtcNow };
            ctx.ApprovalBatches.Add(batch); ctx.Set<ApprovalBatchItem>().Add(bi);
            ctx.ApprovalBatchItemCandidates.Add(new ApprovalBatchItemCandidate { Id = Guid.NewGuid(), ApprovalBatchItemId = bi.Id, QuotationItemId = s.ItemA, QuotationId = s.QuotationId, SupplierNameSnapshot = "Socimorcasal Angola", QuotedDescription = "Lã 60mm", QuotedQuantity = 1, UnitPrice = 110808m, GrossSubtotal = 110808m, LineTotal = 126321.12m, Currency = "AOA", CreatedAtUtc = DateTime.UtcNow });
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return s;
    }

    private static Mock<AdminLogWriter> AdminLog()
    {
        var m = new Mock<AdminLogWriter>(Mock.Of<IServiceScopeFactory>(), Mock.Of<IHttpContextAccessor>(), NullLogger<AdminLogWriter>.Instance);
        m.Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return m;
    }

    private static RequestsController Ctl(ApplicationDbContext ctx, Guid userId)
    {
        var controller = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, new Mock<IWorkflowNotificationOrchestrator>().Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(new PostPaymentCompletionOptions()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")), RequestServices = new ServiceCollection().BuildServiceProvider() } };
        return controller;
    }

    private static async Task<(bool QuotationExists, int Lines, bool? AttachmentDeleted, int Deleted, int DocRemoved)> StateAsync(ApplicationDbContext ctx, Seed s)
    {
        ctx.ChangeTracker.Clear();
        var att = await ctx.RequestAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == s.AttachmentId);
        return (await ctx.Quotations.AnyAsync(q => q.Id == s.QuotationId),
                await ctx.QuotationItems.CountAsync(qi => qi.QuotationId == s.QuotationId),
                att?.IsDeleted,
                await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_DELETED"),
                await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "DOCUMENTO_REMOVIDO"));
    }

    [Fact]
    public async Task Delete_removes_the_structured_quotation_and_its_lines_soft_deletes_the_linked_document_and_writes_both_audit_rows()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        Assert.IsType<NoContentResult>(await Ctl(ctx, s.Buyer).DeleteQuotation(s.RequestId, s.QuotationId));
        var st = await StateAsync(ctx, s);
        Assert.False(st.QuotationExists); Assert.Equal(0, st.Lines); Assert.True(st.AttachmentDeleted); Assert.Equal(1, st.Deleted); Assert.Equal(1, st.DocRemoved);
        // The request itself and its items are untouched.
        Assert.Equal(1, await ctx.RequestLineItems.CountAsync(li => li.RequestId == s.RequestId && !li.IsDeleted));
        var deleted = await ctx.RequestStatusHistories.AsNoTracking().SingleAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "QUOTATION_DELETED");
        Assert.Contains("Socimorcasal Angola", deleted.Comment); Assert.Contains("OR 3602/10/CM", deleted.Comment); Assert.Contains("Samuel Mpoco", deleted.Comment);
    }

    [Fact]
    public async Task Delete_without_a_linked_document_removes_only_the_structured_record()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, withAttachment: false);
        Assert.IsType<NoContentResult>(await Ctl(ctx, s.Buyer).DeleteQuotation(s.RequestId, s.QuotationId));
        var st = await StateAsync(ctx, s);
        Assert.False(st.QuotationExists); Assert.Null(st.AttachmentDeleted); Assert.Equal(1, st.Deleted); Assert.Equal(0, st.DocRemoved);
    }

    [Fact]
    public async Task Delete_is_refused_outside_the_quotation_window_and_nothing_changes()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, status: RequestConstants.Statuses.FinalApproved);
        var res = Assert.IsType<ConflictObjectResult>(await Ctl(ctx, s.Buyer).DeleteQuotation(s.RequestId, s.QuotationId));
        Assert.Equal(409, Assert.IsType<ProblemDetails>(res.Value).Status);
        var st = await StateAsync(ctx, s);
        Assert.True(st.QuotationExists); Assert.Equal(2, st.Lines); Assert.False(st.AttachmentDeleted); Assert.Equal(0, st.Deleted); Assert.Equal(0, st.DocRemoved);
    }

    [Fact]
    public async Task Delete_is_refused_when_a_batch_references_one_of_its_lines_and_nothing_changes()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx, referencedByBatch: true);
        var res = Assert.IsType<ConflictObjectResult>(await Ctl(ctx, s.Buyer).DeleteQuotation(s.RequestId, s.QuotationId));
        Assert.Contains("lote de aprovação", Assert.IsType<ProblemDetails>(res.Value).Detail);
        var st = await StateAsync(ctx, s);
        Assert.True(st.QuotationExists); Assert.Equal(2, st.Lines); Assert.False(st.AttachmentDeleted); Assert.Equal(0, st.Deleted);
    }

    [Fact]
    public async Task Delete_and_update_are_refused_for_a_request_outside_the_actors_access_scope()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = s.OtherBuyer, PlantId = 9 }); await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        var ctl = Ctl(ctx, s.OtherBuyer);
        Assert.IsType<NotFoundObjectResult>(await ctl.DeleteQuotation(s.RequestId, s.QuotationId));                       // new guard (parity with PUT)
        Assert.IsType<NotFoundObjectResult>((await ctl.UpdateQuotation(s.RequestId, s.QuotationId, new SaveQuotationRequestDto { SupplierId = 1, Currency = "AOA", Items = new List<SaveQuotationItemDto>() })).Result);
        var st = await StateAsync(ctx, s);
        Assert.True(st.QuotationExists); Assert.Equal(2, st.Lines); Assert.False(st.AttachmentDeleted); Assert.Equal(0, st.Deleted);
        // In-scope Buyer (no scope rows = unrestricted) still can.
        Assert.IsType<NoContentResult>(await Ctl(ctx, s.Buyer).DeleteQuotation(s.RequestId, s.QuotationId));
    }

    [Fact]
    public async Task Delete_of_an_unknown_quotation_or_request_is_not_found()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        Assert.IsType<NotFoundObjectResult>(await Ctl(ctx, s.Buyer).DeleteQuotation(s.RequestId, Guid.NewGuid()));
        Assert.IsType<NotFoundObjectResult>(await Ctl(ctx, s.Buyer).DeleteQuotation(Guid.NewGuid(), s.QuotationId));
        Assert.True((await StateAsync(ctx, s)).QuotationExists);
    }
}
