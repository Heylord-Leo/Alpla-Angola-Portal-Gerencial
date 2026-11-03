using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.249.5 — DELETE /api/v1/attachments/{id} never removes REGISTERED P.O. evidence, whatever the request/group status
/// (the TEST finding on REQ-628: "Remover P.O atual" soft-deleted the registered file while the group was
/// WAITING_PO_CORRECTION). Only a temporary, never-registered upload (abandoned wizard attempt) is deletable.
/// Direct controller instantiation, as the other AttachmentsController tests do.
/// </summary>
public class PoEvidenceDeleteRefusalTests : IDisposable
{
    private readonly string _scratchDir;
    public PoEvidenceDeleteRefusalTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_poevidence_" + Guid.NewGuid());
        Directory.CreateDirectory(_scratchDir);
    }
    public void Dispose() { try { Directory.Delete(_scratchDir, recursive: true); } catch { } }

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private AttachmentsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);
        var securityOptions = Options.Create(new SecurityOptions { Upload = new UploadOptions { AllowedExtensions = new List<string> { ".pdf" }, BlockedExtensions = new List<string>(), MaxFileSizeBytes = 10_000_000 } });
        var controller = new AttachmentsController(ctx, envMock.Object, securityOptions, configMock.Object);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return controller;
    }

    private sealed record Seed(ApplicationDbContext Ctx, User Actor, Request Request, RequestPoGroup Group, DateTime T0);

    private static async Task<Seed> SeedAsync(string requestStatus, string groupStatus)
    {
        var ctx = NewContext();
        var actor = new User { Id = Guid.NewGuid(), FullName = "Buyer", Email = $"b-{Guid.NewGuid()}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = 1, Code = requestStatus, Name = requestStatus, DisplayOrder = 1 });
        await ctx.SaveChangesAsync();
        var t0 = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);
        var request = new Request { Id = Guid.NewGuid(), RequestNumber = "ZZTEST-EVID-628", Title = "ZZTEST evidence", RequestTypeId = 1, StatusId = 1, RequesterId = actor.Id, DepartmentId = 1, CompanyId = 1, CreatedAtUtc = t0 };
        ctx.Requests.Add(request);
        var group = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "MAQSIS", CurrencyCode = "AOA", TotalAmount = 100m, Status = groupStatus, CreatedAtUtc = t0, CreatedByUserId = actor.Id, PurchaseOrderNumber = "ECF11-2026-0001" };
        ctx.RequestPoGroups.Add(group);
        await ctx.SaveChangesAsync();
        return new Seed(ctx, actor, request, group, t0);
    }

    private static RequestAttachment PoFile(Seed s, DateTime uploadedAt, string name = "po.pdf") => new()
    {
        Id = Guid.NewGuid(), RequestId = s.Request.Id, RequestPoGroupId = s.Group.Id, FileName = name, FileExtension = ".pdf",
        AttachmentTypeCode = AttachmentConstants.Types.PurchaseOrder, StorageReference = "zz/" + name, UploadedByUserId = s.Actor.Id, UploadedAtUtc = uploadedAt
    };

    private static RequestStatusHistory Registration(Seed s, DateTime at, bool correction, bool withGroupComment = true) => new()
    {
        Id = Guid.NewGuid(), RequestId = s.Request.Id, ActorUserId = s.Actor.Id, ActionTaken = correction ? "REREGISTER_PO" : "REGISTER_PO",
        PreviousStatusId = 1, NewStatusId = 1, CreatedAtUtc = at,
        Comment = withGroupComment ? $"[Grupo P.O.: MAQSIS | Total: 100,00 | GroupId: {s.Group.Id.ToString().Substring(0, 8)}] P.O. registada" : "P.O. registada"
    };

    private static async Task AssertRefusedAsync(ApplicationDbContext ctx, IActionResult result, Guid attachmentId, string expectedReason)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var pd = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PoEvidenceProtection.ErrorCode, pd.Extensions["code"]);
        Assert.Equal(expectedReason, pd.Extensions["reasonCode"]);
        var row = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId);
        Assert.False(row.IsDeleted);
        Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "DOCUMENTO REMOVIDO"));
    }

    [Fact]
    public async Task Current_PO_of_a_group_returned_for_correction_is_refused_409_and_kept()
    {
        // The exact TEST operation: group WAITING_PO_CORRECTION (delete window open before v2.249.5), file is the pointer.
        var s = await SeedAsync(RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.PoGroupStatuses.WaitingPoCorrection);
        var po = PoFile(s, s.T0);
        s.Ctx.RequestAttachments.Add(po);
        s.Group.CurrentPoAttachmentId = po.Id;
        s.Ctx.RequestStatusHistories.Add(Registration(s, s.T0.AddMinutes(1), correction: false));
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(po.Id);
        await AssertRefusedAsync(s.Ctx, result, po.Id, PoEvidenceProtection.Reasons.Current);
    }

    [Fact]
    public async Task Superseded_PO_is_refused_409_even_in_the_APPROVED_window()
    {
        var s = await SeedAsync("APPROVED", RequestConstants.PoGroupStatuses.PoIssued);
        var old = PoFile(s, s.T0, "po-old.pdf");
        var current = PoFile(s, s.T0.AddDays(1), "po-new.pdf");
        old.SupersededAtUtc = s.T0.AddDays(1); old.SupersededByAttachmentId = current.Id; old.SupersededByUserId = s.Actor.Id;
        s.Ctx.RequestAttachments.AddRange(old, current);
        s.Group.CurrentPoAttachmentId = current.Id;
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(old.Id);
        await AssertRefusedAsync(s.Ctx, result, old.Id, PoEvidenceProtection.Reasons.Superseded);
    }

    [Fact]
    public async Task Voided_PO_is_refused_409()
    {
        var s = await SeedAsync("APPROVED", RequestConstants.PoGroupStatuses.WaitingPo);
        var voided = PoFile(s, s.T0);
        voided.VoidedAtUtc = s.T0.AddHours(2);
        s.Ctx.RequestAttachments.Add(voided);
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(voided.Id);
        await AssertRefusedAsync(s.Ctx, result, voided.Id, PoEvidenceProtection.Reasons.Voided);
    }

    [Fact]
    public async Task Legacy_group_without_pointer_registration_history_protects_the_file()
    {
        // Registered before v2.249.4: CurrentPoAttachmentId null, REGISTER_PO row with the group id in the comment.
        var s = await SeedAsync(RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.PoGroupStatuses.WaitingPoCorrection);
        var po = PoFile(s, s.T0);
        s.Ctx.RequestAttachments.Add(po);
        s.Ctx.RequestStatusHistories.Add(Registration(s, s.T0.AddMinutes(2), correction: false));
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(po.Id);
        await AssertRefusedAsync(s.Ctx, result, po.Id, PoEvidenceProtection.Reasons.RegisteredAfterUpload);
    }

    [Fact]
    public async Task Legacy_registration_row_without_group_comment_still_protects()
    {
        var s = await SeedAsync(RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.PoGroupStatuses.WaitingPoCorrection);
        var po = PoFile(s, s.T0);
        s.Ctx.RequestAttachments.Add(po);
        s.Ctx.RequestStatusHistories.Add(Registration(s, s.T0.AddMinutes(2), correction: true, withGroupComment: false));
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(po.Id);
        await AssertRefusedAsync(s.Ctx, result, po.Id, PoEvidenceProtection.Reasons.RegisteredAfterUpload);
    }

    [Fact]
    public async Task Temporary_upload_of_an_abandoned_correction_is_soft_deleted_204()
    {
        // Wizard cancel: the file uploaded by a refused/abandoned attempt (after the last registration, never pointed at).
        var s = await SeedAsync(RequestConstants.Statuses.WaitingPoCorrection, RequestConstants.PoGroupStatuses.WaitingPoCorrection);
        var registered = PoFile(s, s.T0, "po-registered.pdf");
        var temp = PoFile(s, s.T0.AddDays(1), "po-attempt.pdf");
        s.Ctx.RequestAttachments.AddRange(registered, temp);
        s.Group.CurrentPoAttachmentId = registered.Id;
        s.Ctx.RequestStatusHistories.Add(Registration(s, s.T0.AddMinutes(1), correction: false));
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(temp.Id);
        Assert.IsType<NoContentResult>(result);
        Assert.True((await s.Ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == temp.Id)).IsDeleted);
        Assert.False((await s.Ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == registered.Id)).IsDeleted);
        Assert.True(await s.Ctx.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "DOCUMENTO REMOVIDO"));
    }

    [Fact]
    public async Task Temporary_upload_of_an_abandoned_first_registration_is_soft_deleted_204_in_a_WAITING_PO_group()
    {
        // Multi-group request: scalar PO_PARTIALLY_UPLOADED (not in the scalar window), this group still WAITING_PO.
        var s = await SeedAsync(RequestConstants.Statuses.PoPartiallyUploaded, RequestConstants.PoGroupStatuses.WaitingPo);
        var temp = PoFile(s, s.T0.AddDays(1), "po-attempt.pdf");
        s.Ctx.RequestAttachments.Add(temp);
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(temp.Id);
        Assert.IsType<NoContentResult>(result);
        Assert.True((await s.Ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == temp.Id)).IsDeleted);
    }

    [Fact]
    public async Task Status_window_still_applies_before_the_evidence_rule_for_unregistered_uploads()
    {
        // An unregistered file of a PO_ISSUED group outside every window: the old 400 stays (no 409, nothing deleted).
        var s = await SeedAsync(RequestConstants.Statuses.PoIssued, RequestConstants.PoGroupStatuses.PoIssued);
        var temp = PoFile(s, s.T0.AddDays(1));
        s.Ctx.RequestAttachments.Add(temp);
        await s.Ctx.SaveChangesAsync();

        var result = await BuildController(s.Ctx, s.Actor.Id).Delete(temp.Id);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False((await s.Ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == temp.Id)).IsDeleted);
    }
}
