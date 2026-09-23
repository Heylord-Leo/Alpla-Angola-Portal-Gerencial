using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// v2.245.11 — request-scope authorization of the advance-payment mutation endpoints
/// (POST requests/{id}/b2p/schedule-advance and b2p/confirm-advance). Both now apply the CANONICAL request
/// scope (<see cref="RequestAccessScope"/> through BaseController.GetScopedRequestsQuery — System Administrator
/// unfiltered; everyone else filtered by UserPlantScopes / UserDepartmentScopes; a user with no scopes is
/// unfiltered), exactly as Finance obligations/payments, MarkAsPaid, SchedulePayment, CancelSchedule and
/// ReturnForAdjustment already did. Out-of-scope = 404 (the "does not exist" answer), zero writes.
/// </summary>
public class AdvancePaymentScopeAuthorizationTests
{
    private const int PaymentTypeId = 1;
    private static readonly DateTime Today = DateTime.UtcNow.Date;

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

    private static RequestsController BuildRequestsController(ApplicationDbContext ctx, Guid actorId, params string[] roles)
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

    private static FinanceController BuildFinanceController(ApplicationDbContext ctx, Guid actorId, params string[] roles)
    {
        if (roles.Length == 0) roles = new[] { RoleConstants.Finance };
        var controller = new FinanceController(
            ctx,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance),
            new FinancePaymentEligibilityService());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(actorId, roles) } };
        return controller;
    }

    // ── Seed: two plants / two departments, one PAYMENT request with a scheduled advance in each plant ──

    private sealed class Seed
    {
        public Guid SysAdmin, FinancePlant1, FinancePlant2, FinanceDept1, FinanceUnscoped, BuyerPlant1;
        public Dictionary<string, int> StatusIds = new();
        public Request Plant1Request = null!, Plant2Request = null!;
        public RequestPoGroup Plant1Group = null!, Plant2Group = null!;
        public RequestAttachment Plant1Proof = null!, Plant2Proof = null!;
    }

    private static User NewUser(string name) => new() { Id = Guid.NewGuid(), FullName = name, Email = $"{Guid.NewGuid()}@t.local" };

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, string groupStatus = RequestConstants.Statuses.AdvancePaymentScheduled)
    {
        var seed = new Seed();
        var sysAdmin = NewUser("SysAdmin"); var fin1 = NewUser("Finance Plant 1"); var fin2 = NewUser("Finance Plant 2");
        var finDept1 = NewUser("Finance Dept 1"); var finAll = NewUser("Finance Unscoped"); var buyer1 = NewUser("Buyer Plant 1");
        ctx.Users.AddRange(sysAdmin, fin1, fin2, finDept1, finAll, buyer1);
        seed.SysAdmin = sysAdmin.Id; seed.FinancePlant1 = fin1.Id; seed.FinancePlant2 = fin2.Id;
        seed.FinanceDept1 = finDept1.Id; seed.FinanceUnscoped = finAll.Id; seed.BuyerPlant1 = buyer1.Id;

        ctx.RequestTypes.Add(new RequestType { Id = PaymentTypeId, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.Departments.AddRange(new Department { Id = 1, Name = "Compras" }, new Department { Id = 2, Name = "Produção" });
        ctx.Plants.AddRange(new Plant { Id = 1, Name = "Planta 1", CompanyId = 1 }, new Plant { Id = 2, Name = "Planta 2", CompanyId = 1 });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.Suppliers.Add(new Supplier { Id = 1, Name = "FORNECEDOR A" });
        ctx.UserPlantScopes.AddRange(
            new UserPlantScope { UserId = fin1.Id, PlantId = 1 },
            new UserPlantScope { UserId = fin2.Id, PlantId = 2 },
            new UserPlantScope { UserId = buyer1.Id, PlantId = 1 });
        ctx.UserDepartmentScopes.Add(new UserDepartmentScope { UserId = finDept1.Id, DepartmentId = 1 });

        var id = 10;
        foreach (var code in new[]
                 {
                     RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentScheduled,
                     RequestConstants.Statuses.WaitingSupplierDelivery, RequestConstants.Statuses.PaymentScheduled,
                     RequestConstants.Statuses.PaymentCompleted
                 })
        {
            id++;
            ctx.RequestStatuses.Add(new RequestStatus { Id = id, Code = code, Name = code, DisplayOrder = id });
            seed.StatusIds[code] = id;
        }
        await ctx.SaveChangesAsync();

        (seed.Plant1Request, seed.Plant1Group, seed.Plant1Proof) = await AddAdvanceRequestAsync(ctx, seed, "REQ-P1", plantId: 1, departmentId: 1, groupStatus);
        (seed.Plant2Request, seed.Plant2Group, seed.Plant2Proof) = await AddAdvanceRequestAsync(ctx, seed, "REQ-P2", plantId: 2, departmentId: 2, groupStatus);
        return seed;
    }

    private static async Task<(Request, RequestPoGroup, RequestAttachment)> AddAdvanceRequestAsync(
        ApplicationDbContext ctx, Seed seed, string number, int plantId, int departmentId, string groupStatus)
    {
        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = number, Title = number, RequestTypeId = PaymentTypeId,
            StatusId = seed.StatusIds[groupStatus], RequesterId = seed.SysAdmin, DepartmentId = departmentId, CompanyId = 1,
            PlantId = plantId, CurrencyId = 1, SupplierId = 1, EstimatedTotalAmount = 1_000m, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Requests.Add(request);
        ctx.RequestAttachments.Add(new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = request.Id, FileName = "po.pdf", FileExtension = ".pdf",
            AttachmentTypeCode = AttachmentConstants.Types.PurchaseOrder, IsDeleted = false
        });
        var group = new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = 1, SupplierNameSnapshot = "FORNECEDOR A", CurrencyCode = "AOA",
            TotalAmount = 1_000m, Status = groupStatus, PurchaseOrderNumber = "ECF11 2026/520", AdvancePaymentPercent = 100m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1), CreatedByUserId = seed.SysAdmin
        };
        ctx.RequestPoGroups.Add(group);
        var scheduled = groupStatus == RequestConstants.Statuses.AdvancePaymentScheduled;
        ctx.RequestPayments.Add(new RequestPayment
        {
            RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.Advance, PaymentSequence = 1,
            PlannedAmount = 1_000m, CurrencyCode = "AOA",
            ScheduledDateUtc = scheduled ? Today.AddDays(2) : null, ScheduledByUserId = scheduled ? seed.SysAdmin : null,
            PaymentStatus = scheduled ? RequestPayment.PaymentStatuses.Scheduled : RequestPayment.PaymentStatuses.Planned,
            CreatedByUserId = seed.SysAdmin, CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        var proof = new RequestAttachment
        {
            Id = Guid.NewGuid(), RequestId = request.Id, FileName = "comprovativo.pdf", FileExtension = ".pdf",
            AttachmentTypeCode = AttachmentConstants.Types.PaymentProof, IsDeleted = false
        };
        ctx.RequestAttachments.Add(proof);
        await ctx.SaveChangesAsync();
        return (request, group, proof);
    }

    /// <summary>Everything a mutation could touch: request, group, payment row, paid timestamp, proof linkage, history, aggregation.</summary>
    private sealed record Snapshot(int RequestStatusId, DateTime? RequestUpdatedAt, Guid? RequestUpdatedBy, DateTime? RequestPaidAt,
        string GroupStatus, DateTime? GroupUpdatedAt, int PaymentCount, string PaymentStatus, DateTime? ScheduledAt, decimal? ActualPaid,
        DateTime? PaidDate, Guid? Proof, Guid? PaidBy, Guid? AttachmentGroup, int HistoryCount, int StatusSyncCount);

    private static async Task<Snapshot> SnapshotAsync(ApplicationDbContext ctx, Guid requestId, Guid groupId, Guid proofId)
    {
        ctx.ChangeTracker.Clear();
        var r = await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == requestId);
        var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == groupId);
        var payments = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == requestId).ToListAsync();
        var p = payments.Single(x => x.RequestPoGroupId == groupId);
        var a = await ctx.RequestAttachments.AsNoTracking().SingleAsync(x => x.Id == proofId);
        var history = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == requestId).ToListAsync();
        return new Snapshot(r.StatusId, r.UpdatedAtUtc, r.UpdatedByUserId, r.ActualPaidAtUtc, g.Status, g.UpdatedAtUtc, payments.Count,
            p.PaymentStatus, p.ScheduledDateUtc, p.ActualPaidAmount, p.PaidDateUtc, p.PaymentProofAttachmentId, p.PaidByUserId,
            a.RequestPoGroupId, history.Count, history.Count(h => h.ActionTaken == "STATUS_SYNC"));
    }

    private static ConfirmAdvancePaymentDto Confirm(Guid groupId, Guid proofId) => new()
        { RequestPoGroupId = groupId, PaymentProofAttachmentId = proofId, ActualPaidAmount = 1_000m, PaidDate = Today, Comment = "confirm" };

    private static ScheduleAdvancePaymentDto Schedule(Guid groupId) => new()
        { RequestPoGroupId = groupId, ScheduledDate = Today.AddDays(3), Comment = "schedule" };

    private static FinanceObligationsResponseDto Obligations(ActionResult<FinanceObligationsResponseDto> r) =>
        Assert.IsType<FinanceObligationsResponseDto>(Assert.IsType<OkObjectResult>(r.Result).Value);

    // ── confirm-advance ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmAdvance_InScopeFinanceUser_Succeeds_WithAggregation()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);

        // The obligation is in the caller's Finance queue (same canonical scope) …
        var queue = Obligations(await BuildFinanceController(ctx, s.FinancePlant1).GetObligations());
        Assert.Equal(s.Plant1Request.Id, Assert.Single(queue.PagedResult.Items).RequestId);

        // … and the same caller may settle it.
        var result = await BuildRequestsController(ctx, s.FinancePlant1).ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, s.Plant1Proof.Id));

        Assert.IsType<OkObjectResult>(result);
        var after = await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id);
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery, after.GroupStatus);
        Assert.Equal(s.StatusIds[RequestConstants.Statuses.WaitingSupplierDelivery], after.RequestStatusId); // aggregation ran
        Assert.Equal(RequestPayment.PaymentStatuses.Completed, after.PaymentStatus);
        Assert.Equal(s.Plant1Proof.Id, after.Proof);
        Assert.Equal(s.Plant1Group.Id, after.AttachmentGroup);
        Assert.Equal(1, after.StatusSyncCount);
    }

    [Fact]
    public async Task ConfirmAdvance_OutOfScopeFinanceUser_Hidden404_ZeroWrites()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);

        // Plant-1 Finance user, plant-2 request — the same answer as a non-existent request.
        var result = await BuildRequestsController(ctx, s.FinancePlant1).ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id));

        Assert.IsType<NotFoundResult>(result); // bare 404, no body → nothing disclosed
        var after = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);
        Assert.Equal(before, after);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, after.GroupStatus);
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, after.PaymentStatus);
        Assert.Null(after.ActualPaid); Assert.Null(after.PaidDate); Assert.Null(after.Proof); Assert.Null(after.PaidBy);
        Assert.Null(after.RequestPaidAt); Assert.Null(after.AttachmentGroup);
        Assert.Equal(0, after.HistoryCount); Assert.Equal(0, after.StatusSyncCount);
        // Not in the caller's Finance queue either — the read and mutation boundaries agree.
        Assert.DoesNotContain(Obligations(await BuildFinanceController(ctx, s.FinancePlant1).GetObligations()).PagedResult.Items,
            c => c.RequestId == s.Plant2Request.Id);
    }

    [Fact]
    public async Task ConfirmAdvance_DepartmentScopedFinanceUser_OutOfScope404_InScopeOk()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);

        // Department-1 user: the department-2 request is hidden (department dimension of the canonical scope) …
        Assert.IsType<NotFoundResult>(await BuildRequestsController(ctx, s.FinanceDept1).ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id)));
        Assert.Equal(before, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));
        // … the department-1 request is reachable.
        Assert.IsType<OkObjectResult>(await BuildRequestsController(ctx, s.FinanceDept1).ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, s.Plant1Proof.Id)));
    }

    [Fact]
    public async Task ConfirmAdvance_InScopeRequest_ForeignGroupId_Fails400_NoDisclosure_NoWrites()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before1 = await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id);
        var before2 = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);

        // Valid in-scope requestId + the plant-2 group's id (foreign group).
        var result = await BuildRequestsController(ctx, s.FinancePlant1).ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant2Group.Id, s.Plant1Proof.Id));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Grupo P.O não encontrado no request.", bad.Value); // fixed message; says nothing about the foreign group
        Assert.DoesNotContain(s.Plant2Group.Id.ToString(), bad.Value!.ToString());
        Assert.Equal(before1, await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id));
        Assert.Equal(before2, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));
    }

    [Fact]
    public async Task ConfirmAdvance_InScopeGroup_ThroughForeignRequestId_Fails_NoWrites()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before1 = await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id);
        var before2 = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);

        // The caller's own (in-scope) group, addressed through an OUT-OF-SCOPE requestId → hidden 404.
        Assert.IsType<NotFoundResult>(await BuildRequestsController(ctx, s.FinancePlant1)
            .ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant1Group.Id, s.Plant1Proof.Id)));
        // The plant-2 group through the caller's in-scope requestId → 400 "group not in request"; still no writes anywhere.
        Assert.IsType<BadRequestObjectResult>(await BuildRequestsController(ctx, s.FinancePlant1)
            .ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id)));

        Assert.Equal(before1, await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id));
        Assert.Equal(before2, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));
    }

    [Fact]
    public async Task ConfirmAdvance_SystemAdministrator_UnfilteredPerCanonicalScope_RoleCheckUnchanged()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);

        // Scope and permission are independent (unchanged): a System Administrator WITHOUT the Finance role is
        // still refused by the existing role check (403), nothing written …
        Assert.Equal(403, Assert.IsType<ObjectResult>(await BuildRequestsController(ctx, s.SysAdmin, RoleConstants.SystemAdministrator)
            .ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id))).StatusCode);
        Assert.Equal(before, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));

        // … while a System Administrator WITH the Finance role is unfiltered by scope (RequestAccessScope contract).
        Assert.IsType<OkObjectResult>(await BuildRequestsController(ctx, s.SysAdmin, RoleConstants.SystemAdministrator, RoleConstants.Finance)
            .ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id)));
        Assert.Equal(RequestConstants.Statuses.WaitingSupplierDelivery,
            (await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id)).GroupStatus);
    }

    [Fact]
    public async Task ConfirmAdvance_UnscopedFinanceUser_UnfilteredPerCanonicalScope()
    {
        // Existing contract (RequestAccessScope): a user with neither plant nor department scope is unfiltered.
        var ctx = NewContext();
        var s = await SeedAsync(ctx);

        Assert.IsType<OkObjectResult>(await BuildRequestsController(ctx, s.FinanceUnscoped)
            .ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id)));
    }

    [Fact]
    public async Task ConfirmAdvance_MissingFinanceRole_Remains403_InScopeOrNot_NoWrites()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before1 = await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id);
        var before2 = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);

        var inScope = await BuildRequestsController(ctx, s.BuyerPlant1, RoleConstants.Buyer).ConfirmAdvancePayment(s.Plant1Request.Id, Confirm(s.Plant1Group.Id, s.Plant1Proof.Id));
        var outOfScope = await BuildRequestsController(ctx, s.BuyerPlant1, RoleConstants.Buyer).ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id));

        Assert.Equal(403, Assert.IsType<ObjectResult>(inScope).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(outOfScope).StatusCode);
        Assert.Equal(before1, await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id));
        Assert.Equal(before2, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));
    }

    [Fact]
    public async Task ConfirmAdvance_And_MarkAsPaid_EnforceTheSameRequestScope()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx);
        var before = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);
        var pay = new ConfirmPaymentDto { RequestPoGroupId = s.Plant2Group.Id, PaymentProofAttachmentId = s.Plant2Proof.Id, ActualPaidAmount = 1_000m, PaidDate = Today };

        // Same caller, same out-of-scope request: both mutation endpoints answer the canonical hidden 404.
        Assert.IsType<NotFoundResult>(await BuildRequestsController(ctx, s.FinancePlant1).ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id)));
        Assert.IsType<NotFoundResult>(await BuildFinanceController(ctx, s.FinancePlant1).MarkAsPaid(s.Plant2Request.Id, pay));
        Assert.Equal(before, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));

        // In scope: MarkAsPaid still refuses the advance (409, sole-settlement invariant) and confirm-advance settles it.
        var conflict = Assert.IsType<ConflictObjectResult>(await BuildFinanceController(ctx, s.FinancePlant2).MarkAsPaid(s.Plant2Request.Id, pay));
        Assert.Equal(FinanceController.AdvanceRequiresConfirmAdvanceCode, Assert.IsType<ProblemDetails>(conflict.Value).Extensions["code"]);
        Assert.Equal(before, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));
        Assert.IsType<OkObjectResult>(await BuildRequestsController(ctx, s.FinancePlant2).ConfirmAdvancePayment(s.Plant2Request.Id, Confirm(s.Plant2Group.Id, s.Plant2Proof.Id)));
    }

    // ── schedule-advance (the directly equivalent advance-payment mutation) ────────────────────

    [Fact]
    public async Task ScheduleAdvance_InScopeOk_OutOfScopeHidden404_ZeroWrites_ForeignGroup400()
    {
        var ctx = NewContext();
        var s = await SeedAsync(ctx, groupStatus: RequestConstants.Statuses.AdvancePaymentRequired);
        var before2 = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);

        // Out of scope → 404, nothing written (payment stays PLANNED, group stays REQUIRED, no history, no aggregation).
        Assert.IsType<NotFoundResult>(await BuildRequestsController(ctx, s.FinancePlant1).ScheduleAdvancePayment(s.Plant2Request.Id, Schedule(s.Plant2Group.Id)));
        var after2 = await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id);
        Assert.Equal(before2, after2);
        Assert.Equal(RequestPayment.PaymentStatuses.Planned, after2.PaymentStatus);
        Assert.Null(after2.ScheduledAt);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentRequired, after2.GroupStatus);
        Assert.Equal(0, after2.HistoryCount);

        // Foreign group through the in-scope request → 400 fixed message, nothing written on either request.
        var before1 = await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id);
        var bad = Assert.IsType<BadRequestObjectResult>(await BuildRequestsController(ctx, s.FinancePlant1).ScheduleAdvancePayment(s.Plant1Request.Id, Schedule(s.Plant2Group.Id)));
        Assert.Equal("Grupo P.O não encontrado no request.", bad.Value);
        Assert.Equal(before1, await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id));
        Assert.Equal(before2, await SnapshotAsync(ctx, s.Plant2Request.Id, s.Plant2Group.Id, s.Plant2Proof.Id));

        // Missing Finance role → 403 (unchanged).
        Assert.Equal(403, Assert.IsType<ObjectResult>(await BuildRequestsController(ctx, s.BuyerPlant1, RoleConstants.Buyer).ScheduleAdvancePayment(s.Plant1Request.Id, Schedule(s.Plant1Group.Id))).StatusCode);

        // In scope → scheduled; SysAdmin unfiltered for the other plant.
        Assert.IsType<OkObjectResult>(await BuildRequestsController(ctx, s.FinancePlant1).ScheduleAdvancePayment(s.Plant1Request.Id, Schedule(s.Plant1Group.Id)));
        var done1 = await SnapshotAsync(ctx, s.Plant1Request.Id, s.Plant1Group.Id, s.Plant1Proof.Id);
        Assert.Equal(RequestConstants.Statuses.AdvancePaymentScheduled, done1.GroupStatus);
        Assert.Equal(s.StatusIds[RequestConstants.Statuses.AdvancePaymentScheduled], done1.RequestStatusId);
        Assert.Equal(RequestPayment.PaymentStatuses.Scheduled, done1.PaymentStatus);
        Assert.IsType<OkObjectResult>(await BuildRequestsController(ctx, s.SysAdmin, RoleConstants.SystemAdministrator, RoleConstants.Finance).ScheduleAdvancePayment(s.Plant2Request.Id, Schedule(s.Plant2Group.Id)));
    }
}
