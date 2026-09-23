using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
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

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// v2.245.12 — the PAYMENT_PROOF upload that precedes confirm-advance for a PAYMENT-type request at
/// ADVANCE_PAYMENT_SCHEDULED (the TEST lifecycle finding on REQ-12/08/2026-241): AttachmentsController's
/// request-scalar lifecycle list never contained the status, so the dedicated advance flow could not receive
/// its mandatory proof. Covers the exact Finance UI sequence (upload → confirm-advance), the preserved
/// statuses, the QUOTATION group rule, group ownership, scope, roles, foreign proof, repeat and the MarkAsPaid
/// 409 invariant. Every rejection is proven write-free by a before/after snapshot.
/// </summary>
public class PaymentProofUploadAdvanceLifecycleTests : IDisposable
{
    private const int PaymentTypeId = 1;
    private const int QuotationTypeId = 2;
    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private readonly string _scratchDir;

    public PaymentProofUploadAdvanceLifecycleTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "ZZTEST_proof_upload_" + Guid.NewGuid());
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best-effort */ }
    }

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ClaimsPrincipal Principal(Guid actorId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private AttachmentsController BuildAttachments(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        if (roles.Length == 0) roles = new[] { RoleConstants.Finance };
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(_scratchDir);
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c[It.IsAny<string>()]).Returns(_scratchDir);
        var securityOptions = Options.Create(new SecurityOptions
        {
            Upload = new UploadOptions { AllowedExtensions = new List<string> { ".pdf" }, BlockedExtensions = new List<string>(), MaxFileSizeBytes = 10_000_000 }
        });
        var controller = new AttachmentsController(ctx, envMock.Object, securityOptions, configMock.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(actorId, roles) } };
        return controller;
    }

    private static RequestsController BuildRequests(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        if (roles.Length == 0) roles = new[] { RoleConstants.Finance };
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
            Microsoft.Extensions.Options.Options.Create(new AlplaPortal.Domain.Configuration.PostPaymentCompletionOptions()));
        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = Principal(actorId, roles), RequestServices = services.BuildServiceProvider() }
        };
        return controller;
    }

    private static FinanceController BuildFinance(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        if (roles.Length == 0) roles = new[] { RoleConstants.Finance };
        var controller = new FinanceController(ctx, new Mock<IWorkflowNotificationOrchestrator>().Object, NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance), new FinancePaymentEligibilityService());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(actorId, roles) } };
        return controller;
    }

    private static IFormFile Pdf(string fileName = "TEST-ONLY-advance-proof.pdf")
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% TEST ONLY - Portal Gerencial v2.245.12\n%%EOF\n");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", fileName) { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    // ── Seed ──────────────────────────────────────────────────────────────────────────────────

    private sealed class Seed
    {
        public Guid SysAdmin, FinancePlant1, FinancePlant2, FinanceUnscoped, BuyerPlant1, RequesterPlant1;
        public Dictionary<string, int> StatusIds = new();
        public Request Plant1Request = null!, Plant2Request = null!, Quotation = null!;
        public RequestPoGroup Plant1Group = null!, Plant2Group = null!, QuotationGroup = null!;
        public int Plant1PaymentId, Plant2PaymentId;
    }

    private static User NewUser(string name) => new() { Id = Guid.NewGuid(), FullName = name, Email = $"{Guid.NewGuid()}@t.local" };

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string plant1Status = RequestConstants.Statuses.AdvancePaymentScheduled)
    {
        var s = new Seed();
        var sys = NewUser("SysAdmin"); var f1 = NewUser("Finance Plant 1"); var f2 = NewUser("Finance Plant 2"); var fa = NewUser("Finance Unscoped");
        var b1 = NewUser("Buyer Plant 1"); var r1 = NewUser("Requester Plant 1");
        ctx.Users.AddRange(sys, f1, f2, fa, b1, r1);
        s.SysAdmin = sys.Id; s.FinancePlant1 = f1.Id; s.FinancePlant2 = f2.Id; s.FinanceUnscoped = fa.Id; s.BuyerPlant1 = b1.Id; s.RequesterPlant1 = r1.Id;
        ctx.RequestTypes.AddRange(
            new RequestType { Id = PaymentTypeId, Code = RequestConstants.Types.Payment, Name = "Pagamento" },
            new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.Departments.Add(new Department { Id = 1, Name = "Compras" });
        ctx.Plants.AddRange(new Plant { Id = 1, Name = "Planta 1", CompanyId = 1 }, new Plant { Id = 2, Name = "Planta 2", CompanyId = 1 });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.Suppliers.Add(new Supplier { Id = 1, Name = "Embrace Angola" });
        ctx.UserPlantScopes.AddRange(
            new UserPlantScope { UserId = f1.Id, PlantId = 1 }, new UserPlantScope { UserId = f2.Id, PlantId = 2 },
            new UserPlantScope { UserId = b1.Id, PlantId = 1 }, new UserPlantScope { UserId = r1.Id, PlantId = 1 });
        var id = 10;
        foreach (var code in new[]
                 {
                     RequestConstants.Statuses.Draft, RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PoPartiallyUploaded,
                     RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled,
                     RequestConstants.Statuses.AdvancePaymentCompleted, RequestConstants.Statuses.WaitingSupplierDelivery,
                     RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted
                 })
        {
            id++;
            ctx.RequestStatuses.Add(new RequestStatus { Id = id, Code = code, Name = code, DisplayOrder = id });
            s.StatusIds[code] = id;
        }
        await ctx.SaveChangesAsync();

        (s.Plant1Request, s.Plant1Group, s.Plant1PaymentId) = await AddPaymentRequestAsync(ctx, s, "REQ-12/08/2026-241", plantId: 1, plant1Status, 10_830m);
        (s.Plant2Request, s.Plant2Group, s.Plant2PaymentId) = await AddPaymentRequestAsync(ctx, s, "REQ-P2", plantId: 2, RequestConstants.Statuses.AdvancePaymentScheduled, 5_000m);

        // QUOTATION whose scalar (PO_PARTIALLY_UPLOADED) is outside the proof list — only the group rule can accept it.
        s.Quotation = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-Q", Title = "REQ-Q", RequestTypeId = QuotationTypeId,
            StatusId = s.StatusIds[RequestConstants.Statuses.PoPartiallyUploaded], RequesterId = s.SysAdmin, DepartmentId = 1, CompanyId = 1,
            PlantId = 1, CurrencyId = 1, EstimatedTotalAmount = 900m, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(s.Quotation);
        s.QuotationGroup = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = s.Quotation.Id, SupplierId = 1, SupplierNameSnapshot = "Embrace Angola", CurrencyCode = "AOA",
            TotalAmount = 900m, Status = RequestConstants.Statuses.AdvancePaymentScheduled, AdvancePaymentPercent = 40m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1), CreatedByUserId = s.SysAdmin
        };
        ctx.RequestPoGroups.AddRange(s.QuotationGroup,
            new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.Quotation.Id, SupplierId = 1, SupplierNameSnapshot = "Other", CurrencyCode = "AOA", TotalAmount = 100m, Status = RequestConstants.PoGroupStatuses.WaitingPo, CreatedAtUtc = DateTime.UtcNow.AddDays(-1), CreatedByUserId = s.SysAdmin });
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = s.Quotation.Id, RequestPoGroupId = s.QuotationGroup.Id, PaymentType = RequestPayment.PaymentTypes.Advance, PaymentSequence = 1,
            PlannedAmount = 360m, CurrencyCode = "AOA", ScheduledDateUtc = Today.AddDays(1), ScheduledByUserId = s.SysAdmin,
            PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = s.SysAdmin, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.SaveChangesAsync();
        return s;
    }

    private static async Task<(Request, RequestPoGroup, int)> AddPaymentRequestAsync(ApplicationDbContext ctx, Seed s, string number, int plantId, string status, decimal amount)
    {
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = number, Title = number, RequestTypeId = PaymentTypeId, StatusId = s.StatusIds[status],
            RequesterId = s.SysAdmin, DepartmentId = 1, CompanyId = 1, PlantId = plantId, CurrencyId = 1, SupplierId = 1,
            EstimatedTotalAmount = amount, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(request);
        ctx.RequestAttachments.Add(new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = request.Id, FileName = "po.pdf", FileExtension = ".pdf", StorageReference = "zz/po.pdf",
            AttachmentTypeCode = AttachmentConstants.Types.PurchaseOrder, UploadedByUserId = s.SysAdmin, UploadedAtUtc = DateTime.UtcNow.AddDays(-1), IsDeleted = false
        });
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 1, SupplierNameSnapshot = "Embrace Angola", CurrencyCode = "AOA",
            TotalAmount = amount, Status = status, PurchaseOrderNumber = "FT 2101433", AdvancePaymentPercent = 100m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1), CreatedByUserId = s.SysAdmin
        };
        ctx.RequestPoGroups.Add(group);
        var advanceStatus = status switch
        {
            RequestConstants.Statuses.AdvancePaymentRequired => RequestPayment.PaymentStatuses.Planned,
            _ => RequestPayment.PaymentStatuses.Scheduled
        };
        var payment = new RequestPayment
        {
            RequestId = request.Id, RequestPoGroupId = group.Id,
            PaymentType = status == RequestConstants.Statuses.PaymentScheduled ? RequestPayment.PaymentTypes.FinalBalance : RequestPayment.PaymentTypes.Advance,
            PaymentSequence = 1, PlannedAmount = amount, CurrencyCode = "AOA",
            ScheduledDateUtc = advanceStatus == RequestPayment.PaymentStatuses.Scheduled ? Today.AddDays(-2) : null,
            ScheduledByUserId = advanceStatus == RequestPayment.PaymentStatuses.Scheduled ? s.SysAdmin : null,
            PaymentStatus = advanceStatus, CreatedByUserId = s.SysAdmin, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.RequestPayments.Add(payment);
        await ctx.SaveChangesAsync();
        return (request, group, payment.Id);
    }

    /// <summary>Everything a rejected mutation must leave untouched.</summary>
    private sealed record Snapshot(string RequestStatus, DateTime? RequestPaidAt, decimal? RequestPaidAmount, string GroupStatus,
        string Payments, string Attachments, int HistoryCount, int StatusSyncCount, string HistoryActions);

    private static async Task<Snapshot> SnapAsync(ApplicationDbContext ctx, Guid requestId, Guid groupId)
    {
        ctx.ChangeTracker.Clear();
        var r = await ctx.Requests.Include(x => x.Status).AsNoTracking().SingleAsync(x => x.Id == requestId);
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == groupId);
        var pays = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == requestId).OrderBy(p => p.Id).ToListAsync();
        var atts = await ctx.RequestAttachments.AsNoTracking().Where(a => a.RequestId == requestId).OrderBy(a => a.Id).ToListAsync();
        var hist = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == requestId).ToListAsync();
        return new Snapshot(r.Status!.Code, r.ActualPaidAtUtc, r.ActualPaidAmount, g.Status,
            string.Join("|", pays.Select(p => $"{p.Id}:{p.PaymentType}:{p.PaymentStatus}:{p.PlannedAmount}:{p.ActualPaidAmount}:{p.PaidDateUtc}:{p.PaymentProofAttachmentId}:{p.PaidByUserId}")),
            string.Join("|", atts.Select(a => $"{a.Id}:{a.AttachmentTypeCode}:{a.RequestPoGroupId}:{a.IsDeleted}:{a.VoidedAtUtc}")),
            hist.Count, hist.Count(h => h.ActionTaken == "STATUS_SYNC"),
            string.Join(",", hist.Select(h => h.ActionTaken).OrderBy(x => x)));
    }

    private static Guid AttachmentIdOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        var first = Assert.Single(list);
        return (Guid)first.GetType().GetProperty("id")!.GetValue(first)!;
    }

    private static ConfirmAdvancePaymentDto Confirm(Guid groupId, Guid proofId, decimal amount = 10_830m) => new()
        { RequestPoGroupId = groupId, PaymentProofAttachmentId = proofId, ActualPaidAmount = amount, PaidDate = Today, Comment = "TEST v2.245.12 lifecycle" };

    // ── 11. Full PAYMENT advance lifecycle: upload → confirm-advance ─────────────────────────

    [Fact]
    public async Task Lifecycle_PaymentAdvanceScheduled_ProofUploadThenConfirmAdvance()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, before.RequestStatus);
        Assert.Contains($"{s.Plant1PaymentId}:ADVANCE:SCHEDULED:10830", before.Payments);

        // Step A — the Finance UI's proof upload (form: files + typeCode=PAYMENT_PROOF + poGroupId).
        var upload = await BuildAttachments(ctx, s.FinancePlant1).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null,
            AttachmentConstants.Types.PaymentProof, s.Plant1Group.Id);
        var proofId = AttachmentIdOf(upload);

        ctx.ChangeTracker.Clear();
        var proof = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == proofId);
        Assert.Equal(s.Plant1Request.Id, proof.RequestId);
        Assert.Equal(AttachmentConstants.Types.PaymentProof, proof.AttachmentTypeCode);
        Assert.Equal(s.Plant1Group.Id, proof.RequestPoGroupId);
        Assert.False(proof.IsDeleted); Assert.Null(proof.VoidedAtUtc);
        Assert.Equal(s.FinancePlant1, proof.UploadedByUserId);
        var afterUpload = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);
        Assert.Equal(before.RequestStatus, afterUpload.RequestStatus);
        Assert.Equal(before.GroupStatus, afterUpload.GroupStatus);
        Assert.Equal(before.Payments, afterUpload.Payments);              // upload alone touches no payment row
        Assert.Null(afterUpload.RequestPaidAt);
        Assert.Equal(before.HistoryCount + 1, afterUpload.HistoryCount);  // exactly the document audit row
        Assert.Equal(before.StatusSyncCount, afterUpload.StatusSyncCount);
        var uploadHistory = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.Plant1Request.Id).ToListAsync();
        Assert.Equal("DOCUMENTO ADICIONADO", Assert.Single(uploadHistory).ActionTaken);

        // Step B — the dedicated confirm-advance with that attachment.
        var result = await BuildRequests(ctx, s.FinancePlant1).ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, proofId));
        Assert.IsType<OkObjectResult>(result);

        ctx.ChangeTracker.Clear();
        var payments = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == s.Plant1Request.Id).ToListAsync();
        var payment = Assert.Single(payments);                             // no second row, no FINAL_BALANCE fabricated
        Assert.Equal(s.Plant1PaymentId, payment.Id);
        Assert.Equal(RequestPayment.PaymentTypes.Advance, payment.PaymentType);
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, payment.PaymentStatus);
        Assert.Equal(10_830m, payment.ActualPaidAmount);
        Assert.Equal(Today, payment.PaidDateUtc);
        Assert.Equal(proofId, payment.PaymentProofAttachmentId);
        Assert.Equal(s.FinancePlant1, payment.PaidByUserId);
        Assert.False(payment.HasDivergence);

        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.Plant1Group.Id);
        var request = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == s.Plant1Request.Id);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, group.Status);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, request.Status!.Code); // canonical aggregation ran
        Assert.NotEqual(RequestConstants.Statuses.PaymentCompleted, group.Status);
        Assert.Equal(s.Plant1Group.Id, (await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == proofId)).RequestPoGroupId);

        var history = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.Plant1Request.Id).ToListAsync();
        Assert.Equal(new[] { "ADVANCE_PAYMENT_COMPLETED", "DOCUMENTO ADICIONADO", "STATUS_SYNC" }, history.Select(h => h.ActionTaken).OrderBy(x => x).ToArray());
        Assert.Contains("Embrace Angola", history.Single(h => h.ActionTaken == "ADVANCE_PAYMENT_COMPLETED").Comment);
        Assert.DoesNotContain(history, h => h.ActionTaken is "PAYMENT_COMPLETED" or "CONFIRM_RECEIVING" or "OPERATIONAL_RECEIPT_COMPLETED" or "MOVE_TO_RECEIPT" or "PAYMENT_DIVERGENCE_DETECTED");
        Assert.Null(request.ActualPaidAtUtc); // the request-level paid timestamp belongs to MarkAsPaid only
    }

    // ── 12/13. Upload lifecycle rule ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_PaymentProof_PaymentType_AdvancePaymentScheduled_Accepted()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var result = await BuildAttachments(ctx, s.FinancePlant1).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant1Group.Id);
        var id = AttachmentIdOf(result);
        var att = await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(AttachmentConstants.Types.PaymentProof, att.AttachmentTypeCode);
        Assert.Equal(s.Plant1Group.Id, att.RequestPoGroupId);
    }

    [Theory]
    [InlineData(RequestConstants.Statuses.AdvancePaymentRequired)]
    [InlineData(RequestConstants.Statuses.PaymentScheduled)]
    [InlineData(RequestConstants.Statuses.PoIssued)]
    [InlineData(RequestConstants.Statuses.AdvancePaymentScheduled)]
    public async Task Upload_PaymentProof_PaymentType_PreservedAndNewStatuses_Accepted(string status)
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx, plant1Status: status);
        var result = await BuildAttachments(ctx, s.FinancePlant1).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant1Group.Id);
        AttachmentIdOf(result);
    }

    [Fact]
    public async Task Upload_PaymentProof_InvalidRequestStatus_Rejected400_NothingWritten()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx, plant1Status: RequestConstants.Statuses.Draft);
        var before = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);

        var result = await BuildAttachments(ctx, s.FinancePlant1).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant1Group.Id);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Upload Bloqueado", Assert.IsType<ProblemDetails>(bad.Value).Title);
        Assert.Equal(before, await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id));
        Assert.DoesNotContain(await ctx.RequestAttachments.AsNoTracking().Where(a => a.RequestId == s.Plant1Request.Id).ToListAsync(), a => a.AttachmentTypeCode == AttachmentConstants.Types.PaymentProof);
    }

    [Fact]
    public async Task Upload_PaymentProof_QuotationScheduledAdvance_AcceptedThroughGroupRule()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        // Scalar PO_PARTIALLY_UPLOADED is not in the request-level list — only the QUOTATION group rule accepts it.
        var result = await BuildAttachments(ctx, s.SysAdmin, RoleConstants.SystemAdministrator).Upload(s.Quotation.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.QuotationGroup.Id);
        var id = AttachmentIdOf(result);
        Assert.Equal(s.QuotationGroup.Id, (await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == id)).RequestPoGroupId);
    }

    // ── 3/4/13. Group ownership, scope and roles ───────────────────────────────────────────────

    [Fact]
    public async Task Upload_PaymentProof_ForeignGroupId_Rejected400_NothingWrittenOnEitherRequest()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before1 = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);
        var before2 = await SnapAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id);

        // In-scope request, another request's group id (PAYMENT type — previously stored unchecked).
        var result = await BuildAttachments(ctx, s.SysAdmin, RoleConstants.SystemAdministrator).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant2Group.Id);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var pd = Assert.IsType<ProblemDetails>(bad.Value);
        Assert.Equal("Grupo P.O. Inválido", pd.Title);
        Assert.DoesNotContain(s.Plant2Group.Id.ToString(), pd.Detail);   // nothing about the foreign group disclosed
        Assert.Equal(before1, await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id));
        Assert.Equal(before2, await SnapAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id));
        // A random id fails the same way.
        Assert.IsType<BadRequestObjectResult>(await BuildAttachments(ctx, s.SysAdmin, RoleConstants.SystemAdministrator).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, Guid.NewGuid()));
        Assert.Equal(before1, await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id));
    }

    [Fact]
    public async Task Upload_PaymentProof_OutOfScopeFinanceUser_Hidden404_NothingWritten()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id);

        var result = await BuildAttachments(ctx, s.FinancePlant1).Upload(s.Plant2Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant2Group.Id);

        var nf = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Pedido não encontrado ou sem permissão de acesso.", nf.Value); // same answer as a non-existent request
        Assert.IsType<NotFoundObjectResult>(await BuildAttachments(ctx, s.FinancePlant1).Upload(Guid.NewGuid(), new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, null));
        Assert.Equal(before, await SnapAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id));
    }

    [Fact]
    public async Task Upload_PaymentProof_UnscopedFinanceUser_And_SysAdmin_FollowCanonicalScope()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        AttachmentIdOf(await BuildAttachments(ctx, s.FinanceUnscoped).Upload(s.Plant2Request.Id, new List<IFormFile> { Pdf("a.pdf") }, null, AttachmentConstants.Types.PaymentProof, s.Plant2Group.Id));
        AttachmentIdOf(await BuildAttachments(ctx, s.SysAdmin, RoleConstants.SystemAdministrator).Upload(s.Plant2Request.Id, new List<IFormFile> { Pdf("b.pdf") }, null, AttachmentConstants.Types.PaymentProof, s.Plant2Group.Id));
    }

    [Fact]
    public async Task ConfirmAdvance_WithoutFinanceRole_403_NothingWritten_UploadRoleContractUnchanged()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        // The upload endpoint's contract is unchanged: in the pre-payment states it requires authentication and
        // request scope, not a specific role (same as PO_ISSUED before this release). The settlement endpoint is
        // where the Finance role is enforced.
        var proofId = AttachmentIdOf(await BuildAttachments(ctx, s.RequesterPlant1, RoleConstants.Requester).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant1Group.Id));
        var before = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);

        var result = await BuildRequests(ctx, s.BuyerPlant1, RoleConstants.Buyer).ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, proofId));

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(before, await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id));
    }

    // ── 6/13. confirm-advance guards with a real uploaded proof ───────────────────────────────

    [Fact]
    public async Task ConfirmAdvance_ProofUploadedToAnotherRequest_Rejected_NothingWritten()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var foreignProof = AttachmentIdOf(await BuildAttachments(ctx, s.SysAdmin, RoleConstants.SystemAdministrator).Upload(s.Plant2Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant2Group.Id));
        var before1 = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);
        var before2 = await SnapAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id);

        var result = await BuildRequests(ctx, s.SysAdmin, RoleConstants.SystemAdministrator, RoleConstants.Finance).ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, foreignProof));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Anexo Inválido", Assert.IsType<ProblemDetails>(bad.Value).Title);
        Assert.Equal(before1, await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id));
        Assert.Equal(before2, await SnapAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id));
    }

    [Fact]
    public async Task ConfirmAdvance_Repeated_FailsClosed_NoDuplicatePaymentProofOrHistory()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var proofId = AttachmentIdOf(await BuildAttachments(ctx, s.FinancePlant1).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant1Group.Id));
        var requests = BuildRequests(ctx, s.FinancePlant1);
        Assert.IsType<OkObjectResult>(await requests.ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, proofId)));
        var first = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);

        var again = await requests.ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, proofId));

        var bad = Assert.IsType<BadRequestObjectResult>(again);
        Assert.Equal("Ação Inválida", Assert.IsType<ProblemDetails>(bad.Value).Title);
        var second = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);
        Assert.Equal(first, second);
        Assert.Single(await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == s.Plant1Request.Id).ToListAsync());
        Assert.Single(await ctx.RequestAttachments.AsNoTracking().Where(a => a.RequestId == s.Plant1Request.Id && a.AttachmentTypeCode == AttachmentConstants.Types.PaymentProof).ToListAsync());
        Assert.Single(await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == s.Plant1Request.Id && h.ActionTaken == "ADVANCE_PAYMENT_COMPLETED").ToListAsync());
    }

    // ── 16. The only successful route stays confirm-advance ───────────────────────────────────

    [Fact]
    public async Task MarkAsPaid_WithUploadedProof_StillRejects409_AdvanceRequiresConfirmAdvance_NothingWritten()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var proofId = AttachmentIdOf(await BuildAttachments(ctx, s.FinancePlant1).Upload(s.Plant1Request.Id, new List<IFormFile> { Pdf() }, null, AttachmentConstants.Types.PaymentProof, s.Plant1Group.Id));
        var before = await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id);

        var result = await BuildFinance(ctx, s.FinancePlant1).MarkAsPaid(s.Plant1Request.Id, new ConfirmPaymentDto
        {
            RequestPoGroupId = s.Plant1Group.Id, PaymentProofAttachmentId = proofId, ActualPaidAmount = 10_830m, PaidDate = Today
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(FinanceController.AdvanceRequiresConfirmAdvanceCode, Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
        Assert.Equal(before, await SnapAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id));
        Assert.Null((await ctx.RequestPayments.AsNoTracking().SingleAsync(p => p.Id == s.Plant1PaymentId)).PaymentProofAttachmentId);
    }
}
