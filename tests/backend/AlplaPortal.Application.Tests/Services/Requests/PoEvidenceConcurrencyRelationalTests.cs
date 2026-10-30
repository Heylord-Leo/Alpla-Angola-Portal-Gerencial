using System;
using System.Collections.Generic;
using System.IO;
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
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// RELATIONAL (SQL Server LocalDB sandbox <c>Portal-Gerencial-IntegrationTests</c>, repo convention: skip when unavailable) proof
/// of the v2.249.5 evidence/registration concurrency rule (docs/PO_EVIDENCE_AND_QUOTATION_LIFECYCLE_v2.249.5.md §9.1):
///  • DELETE /attachments/{id} of a P.O. file waits for a transaction that holds the file's row lock (register-po) and, once that
///    transaction made the file the group's current document, is refused (409 PO_EVIDENCE_PROTECTED) — the file is kept;
///  • register-po waits for a transaction that holds the row lock (the delete) and, once that transaction soft-deleted the file,
///    is refused (409 PO_DOCUMENT_CONCURRENT_CHANGE) with nothing written — a registration never commits with a deleted file;
///  • without contention the real endpoints behave as before on SQL Server (register → pointer; delete of the pointer → 409).
/// The lock is <see cref="PoDocumentRowLock"/> (UPDLOCK, HOLDLOCK inside the transaction), the same mechanism both endpoints use.
/// </summary>
[Collection("IntegrationTests")]
public class PoEvidenceConcurrencyRelationalTests : IDisposable
{
    static PoEvidenceConcurrencyRelationalTests()
    {
        try { using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions()); ctx.Database.EnsureCreated(); }
        catch (Exception ex) { Console.Error.WriteLine($"[PoEvidenceConcurrencyRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}"); }
    }

    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_poconcrel_" + Guid.NewGuid());
    public PoEvidenceConcurrencyRelationalTests() => Directory.CreateDirectory(_scratchDir);
    public void Dispose() { try { Directory.Delete(_scratchDir, true); } catch { } }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private sealed record Seed(Guid RequestId, Guid Owner, Guid GroupA, Guid GroupB, Guid Line1, Guid Line2, Guid PoFileA, Guid TempFileB, int PlantId, string Tag);

    private static async Task CleanupAsync(ApplicationDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE h FROM RequestStatusHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE h FROM RequestFieldChangeHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE o FROM EmailOutbox o INNER JOIN Requests r ON r.Id=o.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE n FROM InformationalNotifications n WHERE EXISTS (SELECT 1 FROM Requests r WHERE r.RequestNumber LIKE 'ZZT-POE-%' AND n.TargetPath LIKE '%' + CAST(r.Id AS varchar(36)) + '%');" +
            "DELETE e FROM PoDocumentExtractions e INNER JOIN Requests r ON r.Id=e.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE c FROM ApprovalBatchItemCandidates c INNER JOIN ApprovalBatchItems bi ON bi.Id=c.ApprovalBatchItemId INNER JOIN ApprovalBatches b ON b.Id=bi.ApprovalBatchId INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE bi FROM ApprovalBatchItems bi INNER JOIN ApprovalBatches b ON b.Id=bi.ApprovalBatchId INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "UPDATE li SET RequestPoGroupId=NULL, SelectedQuotationItemId=NULL FROM RequestLineItems li INNER JOIN Requests r ON r.Id=li.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "UPDATE g SET CurrentPoAttachmentId=NULL FROM RequestPoGroups g INNER JOIN Requests r ON r.Id=g.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE a FROM RequestAttachments a INNER JOIN Requests r ON r.Id=a.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE p FROM RequestPayments p INNER JOIN Requests r ON r.Id=p.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE g FROM RequestPoGroups g INNER JOIN Requests r ON r.Id=g.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE b FROM ApprovalBatches b INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId INNER JOIN Requests r ON r.Id=q.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE q FROM Quotations q INNER JOIN Requests r ON r.Id=q.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE li FROM RequestLineItems li INNER JOIN Requests r ON r.Id=li.RequestId WHERE r.RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE FROM Requests WHERE RequestNumber LIKE 'ZZT-POE-%';" +
            "DELETE FROM UserPlantScopes WHERE UserId IN (SELECT Id FROM Users WHERE Email LIKE 'zztest-poe-%');" +
            "DELETE FROM UserRoleAssignments WHERE UserId IN (SELECT Id FROM Users WHERE Email LIKE 'zztest-poe-%');" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-poe-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId=Users.Id OR r.BuyerId=Users.Id);" +
            "DELETE FROM Suppliers WHERE PortalCode LIKE 'ZZT-POE-%';");
    }

    /// <summary>Group A PO_ISSUED (legacy: number + file, no pointer); group B WAITING_PO with one temporary P.O. upload (the file under test).</summary>
    private static async Task<Seed?> SeedAsync()
    {
        await using var ctx = NewCtx();
        await CleanupAsync(ctx);
        var typeId = await ctx.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync();
        var statusId = await ctx.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.PoPartiallyUploaded).Select(s => s.Id).FirstOrDefaultAsync();
        var plant = await ctx.Plants.OrderBy(p => p.Id).FirstOrDefaultAsync();
        var dept = await ctx.Set<Department>().OrderBy(d => d.Id).FirstOrDefaultAsync();
        var buyerRole = await ctx.Roles.Where(r => r.RoleName == RoleConstants.Buyer).Select(r => r.Id).FirstOrDefaultAsync();
        var waitingQuotation = await ctx.LineItemStatuses.Where(l => l.Code == "WAITING_QUOTATION").Select(l => l.Id).FirstOrDefaultAsync();
        if (typeId == 0 || statusId == 0 || plant == null || dept == null || buyerRole == 0 || waitingQuotation == 0) return null;
        var tag = Guid.NewGuid().ToString("N")[..8];
        var owner = new User { Id = Guid.NewGuid(), FullName = "ZZ Comprador", Email = $"zztest-poe-owner-{tag}@test.local", IsActive = true };
        var req = new User { Id = Guid.NewGuid(), FullName = "ZZ Requisitante", Email = $"zztest-poe-req-{tag}@test.local", IsActive = true };
        ctx.Users.AddRange(owner, req);
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = owner.Id, RoleId = buyerRole });
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = owner.Id, PlantId = plant.Id });
        var supA = new Supplier { Name = "ZZ PRODUCTIVE " + tag, TaxId = "56" + tag, RegistrationStatus = "ACTIVE", PortalCode = "ZZT-POE-A-" + tag };
        var supB = new Supplier { Name = "ZZ MAQSIS " + tag, TaxId = "57" + tag, RegistrationStatus = "ACTIVE", PortalCode = "ZZT-POE-B-" + tag };
        ctx.Suppliers.AddRange(supA, supB);
        await ctx.SaveChangesAsync();

        var s = new Seed(Guid.NewGuid(), owner.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), plant.Id, tag);
        ctx.Requests.Add(new Request { Id = s.RequestId, RequestNumber = "ZZT-POE-" + tag, Title = "ZZTEST P.O. evidence concurrency", RequestTypeId = typeId, StatusId = statusId, RequesterId = req.Id, BuyerId = owner.Id, DepartmentId = dept.Id, CompanyId = plant.CompanyId, PlantId = plant.Id, EstimatedTotalAmount = 0m, ApprovedTotalAmount = 100m, ApprovedCurrencyCode = "AOA", CreatedAtUtc = DateTime.UtcNow.AddDays(-5), UpdatedAtUtc = DateTime.UtcNow });
        var q1 = Guid.NewGuid(); var q2 = Guid.NewGuid(); var qi1 = Guid.NewGuid(); var qi2 = Guid.NewGuid(); var batch = Guid.NewGuid();
        ctx.Quotations.AddRange(
            new Quotation { Id = q1, RequestId = s.RequestId, SupplierId = supA.Id, SupplierNameSnapshot = supA.Name, Currency = "AOA", TotalAmount = 60m, SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow.AddDays(-4), CreatedByUserId = owner.Id },
            new Quotation { Id = q2, RequestId = s.RequestId, SupplierId = supB.Id, SupplierNameSnapshot = supB.Name, Currency = "AOA", TotalAmount = 40m, SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow.AddDays(-4), CreatedByUserId = owner.Id });
        ctx.QuotationItems.AddRange(
            new QuotationItem { Id = qi1, QuotationId = q1, Description = "A", MappedRequestLineItemId = s.Line1, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 60m, LineTotal = 60m },
            new QuotationItem { Id = qi2, QuotationId = q2, Description = "B", MappedRequestLineItemId = s.Line2, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 40m, LineTotal = 40m });
        ctx.RequestLineItems.AddRange(
            new RequestLineItem { Id = s.Line1, RequestId = s.RequestId, LineNumber = 1, Description = "Item A", Quantity = 1, LineItemStatusId = waitingQuotation, QuotationLifecycleStatus = "QUOTATION_APPROVED", CreatedAtUtc = DateTime.UtcNow },
            new RequestLineItem { Id = s.Line2, RequestId = s.RequestId, LineNumber = 2, Description = "Item B", Quantity = 1, LineItemStatusId = waitingQuotation, QuotationLifecycleStatus = "QUOTATION_APPROVED", CreatedAtUtc = DateTime.UtcNow });
        ctx.ApprovalBatches.Add(new ApprovalBatch { Id = batch, RequestId = s.RequestId, BatchNumber = 1, Status = "APPROVED", ApprovedTotalAmount = 100m, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = owner.Id });
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = s.GroupA, RequestId = s.RequestId, SupplierId = supA.Id, SupplierNameSnapshot = supA.Name, CurrencyCode = "AOA", PlantId = plant.Id, TotalAmount = 60m, Status = "PO_ISSUED", ApprovalBatchId = batch, PurchaseOrderNumber = "ECF10 2026/A" + tag, PaymentConditionCode = "POST_PAID", PoResponsibleBuyerId = owner.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = owner.Id },
            new RequestPoGroup { Id = s.GroupB, RequestId = s.RequestId, SupplierId = supB.Id, SupplierNameSnapshot = supB.Name, CurrencyCode = "AOA", PlantId = plant.Id, TotalAmount = 40m, Status = "WAITING_PO", ApprovalBatchId = batch, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = owner.Id });
        await ctx.SaveChangesAsync();
        var l1 = await ctx.RequestLineItems.SingleAsync(l => l.Id == s.Line1); l1.RequestPoGroupId = s.GroupA; l1.SelectedQuotationItemId = qi1;
        var l2 = await ctx.RequestLineItems.SingleAsync(l => l.Id == s.Line2); l2.RequestPoGroupId = s.GroupB; l2.SelectedQuotationItemId = qi2;
        ctx.ApprovalBatchItems.AddRange(
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch, RequestLineItemId = s.Line1, SelectedQuotationItemId = qi1, CreatedAtUtc = DateTime.UtcNow.AddDays(-3) },
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch, RequestLineItemId = s.Line2, SelectedQuotationItemId = qi2, CreatedAtUtc = DateTime.UtcNow.AddDays(-3) });
        ctx.RequestAttachments.AddRange(
            new RequestAttachment { Id = s.PoFileA, RequestId = s.RequestId, RequestPoGroupId = s.GroupA, AttachmentTypeCode = "PO", FileName = "po-a.pdf", FileExtension = ".pdf", StorageReference = "zz/po-a.pdf", FileHash = "a1" + tag, UploadedByUserId = owner.Id, UploadedAtUtc = DateTime.UtcNow.AddDays(-2) },
            new RequestAttachment { Id = s.TempFileB, RequestId = s.RequestId, RequestPoGroupId = s.GroupB, AttachmentTypeCode = "PO", FileName = "po-b-temp.pdf", FileExtension = ".pdf", StorageReference = "zz/po-b-temp.pdf", FileHash = "b1" + tag, UploadedByUserId = owner.Id, UploadedAtUtc = DateTime.UtcNow.AddMinutes(-1) });
        ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = owner.Id, ActionTaken = "REGISTER_PO", NewStatusId = statusId, CreatedAtUtc = DateTime.UtcNow.AddDays(-2), Comment = $"[Grupo P.O.: {supA.Name} | GroupId: {s.GroupA.ToString().Substring(0, 8)}] P.O registrada com sucesso." });
        await ctx.SaveChangesAsync();
        return s;
    }

    private static RequestsController Buyer(ApplicationDbContext ctx, Guid buyerId)
    {
        var options = new PostPaymentCompletionOptions();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var ctl = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, S.AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx), Options.Create(options));
        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(ctx, Options.Create(options), NullLogger<RequestCompletionService>.Instance));
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, buyerId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")), RequestServices = services.BuildServiceProvider() } };
        return ctl;
    }

    private AttachmentsController Attachments(ApplicationDbContext ctx, Guid actorId)
    {
        var env = new Mock<IWebHostEnvironment>(); env.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var config = new Mock<IConfiguration>(); config.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);
        var security = Options.Create(new SecurityOptions { Upload = new UploadOptions { AllowedExtensions = new List<string> { ".pdf" }, BlockedExtensions = new List<string>(), MaxFileSizeBytes = 10_000_000 } });
        var ctl = new AttachmentsController(ctx, env.Object, security, config.Object);
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")) } };
        return ctl;
    }

    private static RegisterPoActionDto Dto(Seed s) => new()
    {
        PoGroupId = s.GroupB, PaymentConditionCode = "POST_PAID", PurchaseOrderNumber = "ECF10 2026/B" + s.Tag, PoAttachmentId = s.TempFileB,
        DocumentTypeConfirmed = true, Comment = "ZZTEST tipo de documento confirmado manualmente (sem extração registada no teste)"
    };

    private static ProblemDetails Problem(IActionResult r) => Assert.IsType<ProblemDetails>(((ObjectResult)r).Value);

    [Fact]
    public async Task Delete_waits_for_the_registering_transactions_row_lock_and_is_then_refused_because_the_file_became_the_current_document()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            // tx1 = the registering session: holds the file's UPDLOCK/HOLDLOCK (what register-po does right after its prior-ECF guard).
            await using var ctx1 = NewCtx();
            await using var tx1 = await ctx1.Database.BeginTransactionAsync();
            var file1 = await ctx1.RequestAttachments.SingleAsync(a => a.Id == s.TempFileB);
            Assert.True(await PoDocumentRowLock.LockAndReloadAsync(ctx1, file1));

            // Session 2 tries to delete the (still temporary) file concurrently → waits on the row lock.
            await using var ctx2 = NewCtx();
            var delete = Attachments(ctx2, s.Owner).Delete(s.TempFileB);
            var completedEarly = await Task.WhenAny(delete, Task.Delay(2500)) == delete;
            Assert.False(completedEarly, "the delete must wait for the registering transaction");

            // The registering transaction makes the file the group's current document and commits.
            var group1 = await ctx1.RequestPoGroups.SingleAsync(g => g.Id == s.GroupB);
            group1.CurrentPoAttachmentId = s.TempFileB; group1.Status = "PO_ISSUED"; group1.PurchaseOrderNumber = "ECF10 2026/B" + s.Tag;
            await ctx1.SaveChangesAsync();
            await tx1.CommitAsync();

            var result = await delete;
            var pd = Problem(result);
            Assert.Equal(409, pd.Status);
            Assert.Equal(PoEvidenceProtection.ErrorCode, pd.Extensions["code"]);
            Assert.Equal(PoEvidenceProtection.Reasons.Current, pd.Extensions["reasonCode"]);

            await using var check = NewCtx();
            Assert.False((await check.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.TempFileB)).IsDeleted);
            Assert.False(await check.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "DOCUMENTO REMOVIDO"));
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }

    [Fact]
    public async Task RegisterPo_waits_for_the_deleting_transactions_row_lock_and_is_then_refused_with_nothing_written()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            // tx2 = the deleting session: holds the file's UPDLOCK/HOLDLOCK (what DELETE does before the evidence guard).
            await using var ctx2 = NewCtx();
            await using var tx2 = await ctx2.Database.BeginTransactionAsync();
            var file2 = await ctx2.RequestAttachments.SingleAsync(a => a.Id == s.TempFileB);
            Assert.True(await PoDocumentRowLock.LockAndReloadAsync(ctx2, file2));

            // The real register-po runs concurrently: its pre-transaction reads pass (shared locks), its in-transaction lock waits.
            await using var ctx1 = NewCtx();
            var register = Buyer(ctx1, s.Owner).RegisterPo(s.RequestId, Dto(s));
            var completedEarly = await Task.WhenAny(register, Task.Delay(2500)) == register;
            Assert.False(completedEarly, "register-po must wait for the deleting transaction");

            // The deleting session soft-deletes the temporary upload and commits.
            file2.IsDeleted = true;
            await ctx2.SaveChangesAsync();
            await tx2.CommitAsync();

            var result = await register;
            var pd = Problem(result);
            Assert.Equal(409, pd.Status);
            Assert.Equal(PoDocumentRowLock.ConcurrentChangeCode, pd.Extensions["code"]);

            await using var check = NewCtx();
            var group = await check.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB);
            Assert.Equal("WAITING_PO", group.Status); Assert.Null(group.CurrentPoAttachmentId); Assert.Null(group.PurchaseOrderNumber);
            Assert.True((await check.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.TempFileB)).IsDeleted);
            Assert.False(await check.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO" && h.Comment!.Contains(s.GroupB.ToString().Substring(0, 8))));
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }

    [Fact]
    public async Task Without_contention_the_real_endpoints_register_the_file_and_then_refuse_its_deletion_on_SQL_Server()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            await using (var ctx = NewCtx())
            {
                var ok = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, Dto(s));
                Assert.IsType<OkObjectResult>(ok);
            }
            await using (var ctx = NewCtx())
            {
                var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB);
                Assert.Equal("PO_ISSUED", group.Status); Assert.Equal(s.TempFileB, group.CurrentPoAttachmentId);
            }
            await using (var ctx = NewCtx())
            {
                // Finance return: the delete status window is open again — the evidence rule alone must keep the registered file.
                var group = await ctx.RequestPoGroups.SingleAsync(g => g.Id == s.GroupB); group.Status = "WAITING_PO_CORRECTION"; await ctx.SaveChangesAsync();
            }
            await using (var ctx = NewCtx())
            {
                var refused = await Attachments(ctx, s.Owner).Delete(s.TempFileB);
                var pd = Problem(refused);
                Assert.Equal(PoEvidenceProtection.ErrorCode, pd.Extensions["code"]);
                Assert.Equal(PoEvidenceProtection.Reasons.Current, pd.Extensions["reasonCode"]);
            }
            await using (var ctx = NewCtx())
            {
                Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.TempFileB)).IsDeleted);
                // The legacy group A file (PO_ISSUED group: refused by the status window before the evidence rule) is kept too.
                var refusedA = await Attachments(ctx, s.Owner).Delete(s.PoFileA);
                Assert.True(refusedA is BadRequestObjectResult or ConflictObjectResult);
                Assert.False((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.PoFileA)).IsDeleted);
            }
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }
}
