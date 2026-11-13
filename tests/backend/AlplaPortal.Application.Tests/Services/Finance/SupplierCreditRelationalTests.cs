using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.DTOs.SupplierCredit;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Application.Interfaces.Suppliers;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
using AlplaPortal.Infrastructure.Services.Suppliers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Application.Tests.Services.Finance;

/// <summary>
/// RELATIONAL (SQL Server LocalDB sandbox <c>Portal-Gerencial-IntegrationTests</c>) proof for supplier credit, stage 1 closure
/// (DEV round 2, 2026-11-12). Everything here EXECUTES against SQL Server — the raw-SQL <c>UPDLOCK</c> reads of the settlement
/// service, the filtered unique indexes and the account key — which the InMemory suites cannot exercise:
///  • reservation on SQL Server: the lock path runs, a hold persists, an insufficient balance is refused (409) and writes nothing;
///  • two concurrent reservations competing for one balance never over-reserve (exactly one hold, Σ holds ≤ balance);
///  • two concurrent registrations WITH credit of the SAME group produce exactly one reservation / one set of owed rows;
///  • two concurrent apply-credit calls debit the ledger exactly once; a third call is refused; the idempotency index holds;
///  • accounts are keyed by supplier × company × currency (unique index executed), the plants of one company share the
///    account and a second company / currency never sees it.
/// Tests skip silently when LocalDB is unavailable (repo convention); run them where it is. Seeds are tagged ZZT-SCR-* and
/// removed in finally.
/// </summary>
[Collection("IntegrationTests")]
public class SupplierCreditRelationalTests
{
    static SupplierCreditRelationalTests()
    {
        try { using var ctx = NewCtx(); ctx.Database.EnsureCreated(); }
        catch (Exception ex) { Console.Error.WriteLine($"[SupplierCreditRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private static SupplierCreditSettlementService Svc(ApplicationDbContext ctx, bool enabled = true)
        => new(ctx, Options.Create(new SupplierCreditOptions { NewReservationsEnabled = enabled }), NullLogger<SupplierCreditSettlementService>.Instance);

    private sealed record Seed(string Tag, int CompanyA, int CompanyB, int PlantA1, int PlantA2, int PlantB1, int DepartmentId, int SupplierId,
        Guid Owner, Guid Finance1, Guid Finance2,
        Guid Request1, Guid Group1, Guid Request2, Guid Group2, Guid Request3, Guid Group3, Guid AccountA, Guid AccountB);

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    /// <summary>Company A (plants A1, A2) and company B (plant B1); one supplier with account A (company A, AOA, creditA) and
    /// account B (company B, AOA, creditB); three QUOTATION requests — R1 (A1), R2 (A2), R3 (B1) — each with an APPROVED batch
    /// and ONE group WAITING_PO of 500 AOA for that supplier; owner Buyer and two Finance users scoped to the three plants.</summary>
    private static async Task<Seed> SeedAsync(decimal creditA = 600m, decimal creditB = 100m)
    {
        await using var ctx = NewCtx();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var now = DateTime.UtcNow;
        var quotationType = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Quotation, () => new RequestType { Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var poIssued = await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.PoIssued, () => new RequestStatus { Code = RequestConstants.Statuses.PoIssued, Name = "P.O emitida" });
        await GetOrAdd(ctx.RequestStatuses, s => s.Code == RequestConstants.Statuses.PaymentCompleted, () => new RequestStatus { Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pagamento concluído" });
        var waitingQuotation = await GetOrAdd(ctx.LineItemStatuses, l => l.Code == "WAITING_QUOTATION", () => new LineItemStatus { Code = "WAITING_QUOTATION", Name = "Aguardando Cotação" });
        var aoa = await GetOrAdd(ctx.Currencies, c => c.Code == "AOA", () => new Currency { Code = "AOA", Symbol = "Kz", IsActive = true });
        await GetOrAdd(ctx.Currencies, c => c.Code == "USD", () => new Currency { Code = "USD", Symbol = "$", IsActive = true });
        var buyerRole = await GetOrAdd(ctx.Roles, r => r.RoleName == RoleConstants.Buyer, () => new Role { RoleName = RoleConstants.Buyer });
        var financeRole = await GetOrAdd(ctx.Roles, r => r.RoleName == RoleConstants.Finance, () => new Role { RoleName = RoleConstants.Finance });
        await ctx.SaveChangesAsync();

        var companyA = new Company { Name = $"ZZT-SCR-{tag} APA", Code = "Z" + tag[..3] + "A", IsActive = true };
        var companyB = new Company { Name = $"ZZT-SCR-{tag} APS", Code = "Z" + tag[..3] + "B", IsActive = true };
        var department = new Department { Name = $"ZZT-SCR-{tag} Dept", IsActive = true };
        ctx.Companies.AddRange(companyA, companyB); ctx.Departments.Add(department);
        await ctx.SaveChangesAsync();
        var plantA1 = new Plant { Name = $"ZZT-SCR-{tag} Viana 1", CompanyId = companyA.Id, IsActive = true };
        var plantA2 = new Plant { Name = $"ZZT-SCR-{tag} Viana 2", CompanyId = companyA.Id, IsActive = true };
        var plantB1 = new Plant { Name = $"ZZT-SCR-{tag} APS 1", CompanyId = companyB.Id, IsActive = true };
        ctx.Plants.AddRange(plantA1, plantA2, plantB1);
        var owner = new User { Id = Guid.NewGuid(), FullName = "ZZ Comprador", Email = $"zztest-scr-owner-{tag}@test.local", IsActive = true };
        var fin1 = new User { Id = Guid.NewGuid(), FullName = "ZZ Finance 1", Email = $"zztest-scr-fin1-{tag}@test.local", IsActive = true };
        var fin2 = new User { Id = Guid.NewGuid(), FullName = "ZZ Finance 2", Email = $"zztest-scr-fin2-{tag}@test.local", IsActive = true };
        ctx.Users.AddRange(owner, fin1, fin2);
        var supplier = new Supplier { Name = "ZZ CREDITO " + tag, TaxId = "57" + tag, RegistrationStatus = "ACTIVE", PortalCode = "ZZT-SCR-" + tag };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = owner.Id, RoleId = buyerRole.Id }, new UserRoleAssignment { UserId = fin1.Id, RoleId = financeRole.Id }, new UserRoleAssignment { UserId = fin2.Id, RoleId = financeRole.Id });
        foreach (var u in new[] { owner.Id, fin1.Id, fin2.Id })
            foreach (var p in new[] { plantA1.Id, plantA2.Id, plantB1.Id })
                ctx.UserPlantScopes.Add(new UserPlantScope { UserId = u, PlantId = p });

        var accountA = new SupplierCreditAccount { SupplierId = supplier.Id, CompanyId = companyA.Id, CurrencyCode = "AOA", AccountNumber = "CCS-ZZ" + tag[..6] + "A", Status = SC.AccountStatuses.Active, CreatedAtUtc = now, CreatedByUserId = owner.Id };
        var accountB = new SupplierCreditAccount { SupplierId = supplier.Id, CompanyId = companyB.Id, CurrencyCode = "AOA", AccountNumber = "CCS-ZZ" + tag[..6] + "B", Status = SC.AccountStatuses.Active, CreatedAtUtc = now, CreatedByUserId = owner.Id };
        ctx.SupplierCreditAccounts.AddRange(accountA, accountB);
        ctx.SupplierCreditEntries.AddRange(
            new SupplierCreditEntry { AccountId = accountA.Id, EntryType = SC.EntryTypes.CreditIn, SignedAmount = creditA, BalanceAfter = creditA, Reference = "NC-A", Reason = "Crédito inicial de teste (A)", CreatedByUserId = owner.Id, CreatedAtUtc = now },
            new SupplierCreditEntry { AccountId = accountB.Id, EntryType = SC.EntryTypes.CreditIn, SignedAmount = creditB, BalanceAfter = creditB, Reference = "NC-B", Reason = "Crédito inicial de teste (B)", CreatedByUserId = owner.Id, CreatedAtUtc = now });

        var ids = new List<(Guid Request, Guid Group)>();
        var n = 1;
        foreach (var (company, plant) in new[] { (companyA, plantA1), (companyA, plantA2), (companyB, plantB1) })
        {
            var requestId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var batchId = Guid.NewGuid(); var lineId = Guid.NewGuid();
            ctx.Requests.Add(new Request
            {
                Id = requestId, RequestNumber = $"ZZT-SCR-{tag}-{n}", Title = $"ZZTEST crédito {n}", RequestTypeId = quotationType.Id, StatusId = poIssued.Id,
                RequesterId = owner.Id, BuyerId = owner.Id, DepartmentId = department.Id, CompanyId = company.Id, PlantId = plant.Id, CurrencyId = aoa.Id,
                EstimatedTotalAmount = 0m, ApprovedTotalAmount = 500m, ApprovedCurrencyCode = "AOA", CreatedAtUtc = now.AddDays(-3), RequestedDateUtc = now.AddDays(-3), UpdatedAtUtc = now,
            });
            ctx.ApprovalBatches.Add(new ApprovalBatch { Id = batchId, RequestId = requestId, BatchNumber = 1, Status = "APPROVED", ApprovedTotalAmount = 500m, CreatedAtUtc = now.AddDays(-2), CreatedByUserId = owner.Id });
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = groupId, RequestId = requestId, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, CurrencyCode = "AOA", PlantId = plant.Id, TotalAmount = 500m, Status = RequestConstants.PoGroupStatuses.WaitingPo, ApprovalBatchId = batchId, CreatedAtUtc = now.AddDays(-2), CreatedByUserId = owner.Id });
            await ctx.SaveChangesAsync();
            ctx.RequestLineItems.Add(new RequestLineItem { Id = lineId, RequestId = requestId, LineNumber = 1, Description = "Item", Quantity = 1, LineItemStatusId = waitingQuotation.Id, QuotationLifecycleStatus = "QUOTATION_APPROVED", RequestPoGroupId = groupId, CreatedAtUtc = now });
            ctx.ApprovalBatchItems.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batchId, RequestLineItemId = lineId, CreatedAtUtc = now });
            // RegisterPo requires a P.O. file on the group; no extraction is recorded, so the registration confirms the document type explicitly.
            ctx.RequestAttachments.Add(new RequestAttachment { Id = Guid.NewGuid(), RequestId = requestId, RequestPoGroupId = groupId, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = $"po-{n}.pdf", FileExtension = ".pdf", StorageReference = $"zz/po-{tag}-{n}.pdf", UploadedByUserId = owner.Id, UploadedAtUtc = now });
            await ctx.SaveChangesAsync();
            ids.Add((requestId, groupId)); n++;
        }

        return new Seed(tag, companyA.Id, companyB.Id, plantA1.Id, plantA2.Id, plantB1.Id, department.Id, supplier.Id, owner.Id, fin1.Id, fin2.Id,
            ids[0].Request, ids[0].Group, ids[1].Request, ids[1].Group, ids[2].Request, ids[2].Group, accountA.Id, accountB.Id);
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        var req = $"ZZT-SCR-{s.Tag}%"; var mail = $"zztest-scr-%{s.Tag}%"; var org = $"ZZT-SCR-{s.Tag}%";
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE p FROM RequestPayments p INNER JOIN Requests r ON r.Id=p.RequestId WHERE r.RequestNumber LIKE {0};" +
            "UPDATE g SET SupplierCreditReservationId=NULL FROM RequestPoGroups g INNER JOIN Requests r ON r.Id=g.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE e FROM SupplierCreditEntries e INNER JOIN SupplierCreditAccounts a ON a.Id=e.AccountId WHERE a.SupplierId={1};" +
            "DELETE x FROM SupplierCreditReservations x INNER JOIN SupplierCreditAccounts a ON a.Id=x.AccountId WHERE a.SupplierId={1};" +
            "DELETE FROM SupplierCreditAccounts WHERE SupplierId={1};" +
            "DELETE o FROM EmailOutbox o INNER JOIN Requests r ON r.Id=o.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE n FROM InformationalNotifications n WHERE EXISTS (SELECT 1 FROM Requests r WHERE r.RequestNumber LIKE {0} AND n.TargetPath LIKE '%' + CAST(r.Id AS varchar(36)) + '%');" +
            "DELETE h FROM RequestFieldChangeHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE h FROM RequestStatusHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE bi FROM ApprovalBatchItems bi INNER JOIN ApprovalBatches b ON b.Id=bi.ApprovalBatchId INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE li FROM RequestLineItems li INNER JOIN Requests r ON r.Id=li.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE a FROM RequestAttachments a INNER JOIN Requests r ON r.Id=a.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE g FROM RequestPoGroups g INNER JOIN Requests r ON r.Id=g.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE b FROM ApprovalBatches b INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE {0};" +
            "DELETE FROM Requests WHERE RequestNumber LIKE {0};" +
            "DELETE FROM UserPlantScopes WHERE UserId IN (SELECT Id FROM Users WHERE Email LIKE {2});" +
            "DELETE FROM UserRoleAssignments WHERE UserId IN (SELECT Id FROM Users WHERE Email LIKE {2});" +
            "DELETE FROM Users WHERE Email LIKE {2};" +
            "DELETE FROM Suppliers WHERE Id={1};" +
            "DELETE FROM Plants WHERE Name LIKE {3};" +
            "DELETE FROM Companies WHERE Name LIKE {3};" +
            "DELETE FROM Departments WHERE Name LIKE {3};",
            req, s.SupplierId, mail, org);
    }

    private static async Task<(Request request, RequestPoGroup group)> LoadAsync(ApplicationDbContext ctx, Guid requestId, Guid groupId)
    {
        var request = await ctx.Requests.Include(r => r.RequestType).Include(r => r.Currency).Include(r => r.PoGroups).FirstAsync(r => r.Id == requestId);
        return (request, request.PoGroups.Single(g => g.Id == groupId));
    }

    private static ReserveSupplierCreditCommand Reserve(Request r, RequestPoGroup g, decimal amount, Guid actor) => new()
    {
        Request = r, Group = g, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, AdvancePercent = 0m,
        UseSupplierCredit = true, RequestedAmount = amount, NewReservationsEnabled = true, ActorUserId = actor,
    };

    private static RequestsController Buyer(ApplicationDbContext ctx, Guid userId)
    {
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>(); orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Returns(Task.CompletedTask);
        var completion = new PostPaymentCompletionOptions();
        var ctl = new RequestsController(ctx, new Mock<IDocumentExtractionService>().Object, S.AdminLog().Object, NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object, orchestrator.Object, new Mock<IPrimaveraRequestValidationService>().Object, new Mock<IGroupBuilderService>().Object,
            new Mock<IRequestStatusSyncService>().Object, new Mock<IApprovalRoutingService>().Object, new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object, new Mock<IQuotationItemEligibilityService>().Object, new Mock<IBatchExtraItemDecisionService>().Object,
            new InternalCompanyGuard(ctx), Options.Create(completion), Svc(ctx), Options.Create(new SupplierCreditOptions { NewReservationsEnabled = true }));
        var services = new ServiceCollection();
        services.AddSingleton<IStatusAggregationService>(new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));
        services.AddSingleton<IRequestCompletionService>(new RequestCompletionService(ctx, Options.Create(completion), NullLogger<RequestCompletionService>.Instance));
        ctl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Buyer) }, "Test")),
                RequestServices = services.BuildServiceProvider()
            }
        };
        return ctl;
    }

    private static FinanceController Finance(ApplicationDbContext ctx, Guid userId)
    {
        var ctl = new FinanceController(ctx, new Mock<IWorkflowNotificationOrchestrator>().Object, NullLogger<FinanceController>.Instance,
            new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance), new FinancePaymentEligibilityService(), null, Svc(ctx));
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, RoleConstants.Finance) }, "Test")) } };
        return ctl;
    }

    private static RegisterPoActionDto RegisterDto(Seed s, Guid groupId, decimal credit) => new()
    {
        PoGroupId = groupId, PaymentConditionCode = RequestConstants.PaymentConditions.PostPaid, PurchaseOrderNumber = "ECF10 2026/Z" + s.Tag,
        DocumentTypeConfirmed = true, Comment = "ZZTEST tipo de documento confirmado manualmente (sem extração registada no teste)",
        UseSupplierCredit = true, SupplierCreditAmount = credit,
    };

    private static ProblemDetails Problem(IActionResult r) => Assert.IsType<ProblemDetails>(((ObjectResult)r).Value);

    private static string Describe(IActionResult r)
        => r is ObjectResult o ? $"{r.GetType().Name} {o.StatusCode}: {(o.Value is ProblemDetails pd ? $"{pd.Title} — {pd.Detail} [{(pd.Extensions.TryGetValue("code", out var c) ? c : null)}]" : o.Value)}" : r.GetType().Name;

    private static void AssertOk(IActionResult r) => Assert.True(r is OkObjectResult, Describe(r));

    // ═════════════════════════════════════════ reservation on SQL Server ═════════════════════════════════════════

    [Fact]
    public async Task Reserve_executes_the_SQL_Server_lock_path_and_refuses_insufficient_credit_without_writing()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;
        var s = await SeedAsync();
        try
        {
            await using (var ctx = NewCtx())
            {
                await using var tx = await ctx.Database.BeginTransactionAsync();
                var (r, g) = await LoadAsync(ctx, s.Request1, s.Group1);
                var ok = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 500m, s.Owner));
                Assert.True(ok.Succeeded, ok.Detail);
                await ctx.SaveChangesAsync(); await tx.CommitAsync();
            }
            await using (var ctx = NewCtx())
            {
                await using var tx = await ctx.Database.BeginTransactionAsync();
                var (r, g) = await LoadAsync(ctx, s.Request2, s.Group2);
                var insufficient = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 500m, s.Owner));
                Assert.False(insufficient.Succeeded); Assert.Equal(409, insufficient.StatusCode); Assert.Equal(SC.ErrorCodes.Insufficient, insufficient.Code);
                Assert.Contains("insuficiente", insufficient.Detail);
                await tx.RollbackAsync();
            }
            await using (var ctx = NewCtx())
            {
                var holds = await ctx.SupplierCreditReservations.AsNoTracking().Where(x => x.AccountId == s.AccountA).ToListAsync();
                var hold = Assert.Single(holds);
                Assert.Equal(SC.ReservationStatuses.Reserved, hold.Status); Assert.Equal(500m, hold.Amount); Assert.Equal(s.Group1, hold.RequestPoGroupId);
                Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.AccountId == s.AccountA)); // a hold is not a movement
                Assert.Equal((600m, 500m, 100m), await Svc(ctx).AccountFiguresAsync(s.AccountA));
                var g2 = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.Group2);
                Assert.Equal(0m, g2.CreditCoverageAmount); Assert.Null(g2.SupplierCreditReservationId);
                Assert.Equal(0, await ctx.RequestPayments.CountAsync(p => p.RequestPoGroupId == s.Group2));
                var (r2, g2l) = await LoadAsync(ctx, s.Request2, s.Group2);
                var e = await Svc(ctx).EvaluateEligibilityAsync(r2, g2l, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
                Assert.Equal(100m, e.MaxCoverage); Assert.Equal(100m, e.Account!.Available); Assert.True(e.CanReserve);
            }
        }
        finally { await CleanupAsync(s); }
    }

    // ═════════════════════════════════════════ concurrency ═════════════════════════════════════════

    [Fact]
    public async Task Two_concurrent_reservations_on_one_account_never_over_reserve()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;
        var s = await SeedAsync(creditA: 600m);
        try
        {
            var start = new TaskCompletionSource();
            async Task<(bool Ok, int Status, string? Code)> Run(Guid requestId, Guid groupId)
            {
                await using var ctx = NewCtx();
                await using var tx = await ctx.Database.BeginTransactionAsync();
                var (r, g) = await LoadAsync(ctx, requestId, groupId);
                await start.Task;
                var outcome = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r, g, 500m, s.Owner));
                if (!outcome.Succeeded) { await tx.RollbackAsync(); return (false, outcome.StatusCode, outcome.Code); }
                await ctx.SaveChangesAsync(); await tx.CommitAsync();
                return (true, 200, null);
            }
            var t1 = Run(s.Request1, s.Group1); var t2 = Run(s.Request2, s.Group2);
            start.SetResult();
            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(1, results.Count(x => x.Ok));
            var loser = results.Single(x => !x.Ok);
            Assert.Equal(409, loser.Status); Assert.Equal(SC.ErrorCodes.Insufficient, loser.Code);

            await using var check = NewCtx();
            var holds = await check.SupplierCreditReservations.AsNoTracking().Where(x => x.AccountId == s.AccountA).ToListAsync();
            Assert.Single(holds);
            var reserved = holds.Sum(h => SupplierCreditPolicyHold(h));
            Assert.Equal(500m, reserved); Assert.True(reserved <= 600m);
            Assert.Equal((600m, 500m, 100m), await Svc(check).AccountFiguresAsync(s.AccountA));
            Assert.Equal(1, await check.RequestPoGroups.CountAsync(g => (g.Id == s.Group1 || g.Id == s.Group2) && g.SupplierCreditReservationId != null));
        }
        finally { await CleanupAsync(s); }
    }

    private static decimal SupplierCreditPolicyHold(SupplierCreditReservation r) => AlplaPortal.Domain.Services.SupplierCreditPolicy.Hold(r.Status, r.Amount, r.AppliedAmount);

    [Fact]
    public async Task Two_concurrent_registrations_with_credit_of_the_same_group_produce_exactly_one_reservation()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;
        var s = await SeedAsync(creditA: 600m);
        try
        {
            var start = new TaskCompletionSource();
            async Task<IActionResult> Run()
            {
                await using var ctx = NewCtx();
                var ctl = Buyer(ctx, s.Owner);
                await start.Task;
                return await ctl.RegisterPo(s.Request1, RegisterDto(s, s.Group1, 300m));
            }
            var t1 = Run(); var t2 = Run();
            start.SetResult();
            var results = await Task.WhenAll(t1, t2);

            Assert.True(results.Count(r => r is OkObjectResult) == 1, string.Join(" | ", results.Select(Describe)));
            var loser = (ObjectResult)results.Single(r => r is not OkObjectResult);
            Assert.Equal(409, loser.StatusCode);
            Assert.Equal(SC.ErrorCodes.ReservationConflict, Problem(loser).Extensions["code"]); // "Grupo alterado entretanto" — status re-read under the group lock

            await using var check = NewCtx();
            var g = await check.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.Group1);
            Assert.Equal(RequestConstants.Statuses.PoIssued, g.Status); Assert.Equal(300m, g.CreditCoverageAmount); Assert.NotNull(g.SupplierCreditReservationId);
            var holds = await check.SupplierCreditReservations.AsNoTracking().Where(x => x.RequestPoGroupId == s.Group1).ToListAsync();
            var hold = Assert.Single(holds); Assert.Equal(SC.ReservationStatuses.Reserved, hold.Status); Assert.Equal(300m, hold.Amount);
            var rows = await check.RequestPayments.AsNoTracking().Where(p => p.RequestPoGroupId == s.Group1).ToListAsync();
            Assert.Equal(2, rows.Count); // exactly one set of owed rows: bank 200 + credit 300
            Assert.Single(rows, p => p.PaymentType == RequestPayment.PaymentTypes.FinalBalance && p.PlannedAmount == 200m);
            Assert.Single(rows, p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PlannedAmount == 300m);
            Assert.Equal(1, await check.RequestStatusHistories.CountAsync(h => h.RequestId == s.Request1 && h.ActionTaken == SC.HistoryActions.Reserved));
            Assert.Equal((600m, 300m, 300m), await Svc(check).AccountFiguresAsync(s.AccountA));
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task Two_concurrent_apply_credit_calls_debit_the_account_exactly_once_and_the_idempotency_index_holds()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;
        var s = await SeedAsync(creditA: 600m);
        try
        {
            await using (var ctx = NewCtx())
            {
                var registered = await Buyer(ctx, s.Owner).RegisterPo(s.Request1, RegisterDto(s, s.Group1, 500m)); // full coverage → no bank row
                AssertOk(registered);
            }
            await using (var ctx = NewCtx())
            {
                var g = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.Group1);
                Assert.Equal(RequestConstants.Statuses.PoIssued, g.Status); Assert.Equal(500m, g.CreditCoverageAmount);
                Assert.Equal(1, await ctx.RequestPayments.CountAsync(p => p.RequestPoGroupId == s.Group1 && p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned));
            }

            var start = new TaskCompletionSource();
            async Task<IActionResult> Run()
            {
                await using var ctx = NewCtx();
                var ctl = Finance(ctx, s.Finance1);
                await start.Task;
                return await ctl.ApplyCredit(s.Request1, new ApplySupplierCreditDto { RequestPoGroupId = s.Group1, Note = "ZZTEST aplicação concorrente" });
            }
            var t1 = Run(); var t2 = Run();
            start.SetResult();
            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(1, results.Count(r => r is OkObjectResult));
            var loser = (ObjectResult)results.Single(r => r is not OkObjectResult);
            Assert.Contains(loser.StatusCode, new int?[] { 400, 409 }); // 409 CREDIT_ALREADY_APPLIED after the group lock, or 400 when the loser loaded the settled group
            Assert.Contains((string?)Problem(loser).Extensions["code"], new[] { SC.ErrorCodes.AlreadyApplied, SC.ErrorCodes.NothingToApply });

            string key;
            await using (var check = NewCtx())
            {
                var entries = await check.SupplierCreditEntries.AsNoTracking().Where(e => e.AccountId == s.AccountA && e.EntryType == SC.EntryTypes.Application).ToListAsync();
                var entry = Assert.Single(entries); Assert.Equal(-500m, entry.SignedAmount); key = entry.IdempotencyKey!;
                var hold = await check.SupplierCreditReservations.AsNoTracking().SingleAsync(x => x.RequestPoGroupId == s.Group1);
                Assert.Equal(SC.ReservationStatuses.Applied, hold.Status); Assert.Equal(500m, hold.AppliedAmount);
                var creditRows = await check.RequestPayments.AsNoTracking().Where(p => p.RequestPoGroupId == s.Group1 && p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit).ToListAsync();
                var row = Assert.Single(creditRows); Assert.Equal(RequestPayment.PaymentStatuses.Completed, row.PaymentStatus); Assert.Equal(500m, row.ActualPaidAmount);
                Assert.Equal(RequestConstants.Statuses.PaymentCompleted, (await check.RequestPoGroups.AsNoTracking().SingleAsync(x => x.Id == s.Group1)).Status);
                Assert.Equal(1, await check.RequestStatusHistories.CountAsync(h => h.RequestId == s.Request1 && h.ActionTaken == SC.HistoryActions.Applied));
                Assert.Equal((100m, 0m, 100m), await Svc(check).AccountFiguresAsync(s.AccountA));
            }

            // A later duplicate is refused and still nothing is written.
            await using (var ctx = NewCtx())
            {
                var third = (ObjectResult)await Finance(ctx, s.Finance1).ApplyCredit(s.Request1, new ApplySupplierCreditDto { RequestPoGroupId = s.Group1 });
                Assert.Contains(third.StatusCode, new int?[] { 400, 409 });
                Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.AccountId == s.AccountA && e.EntryType == SC.EntryTypes.Application));
            }

            // Last line of defence, executed: the filtered unique index on IdempotencyKey rejects a second APPLY row for the same reservation and stage.
            await using (var ctx = NewCtx())
            {
                var ex = await Assert.ThrowsAsync<SqlException>(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO SupplierCreditEntries (Id, AccountId, EntryType, SignedAmount, BalanceAfter, Reason, IdempotencyKey, CreatedByUserId, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {s.AccountA}, 'APPLICATION', -500, 0, 'ZZTEST duplicate application', {key}, {s.Finance1}, {DateTime.UtcNow})"));
                Assert.Contains(ex.Number, new[] { 2601, 2627 });
                Assert.Contains("UX_SupplierCreditEntries_IdempotencyKey", ex.Message);
                Assert.Equal(1, await ctx.SupplierCreditEntries.CountAsync(e => e.AccountId == s.AccountA && e.EntryType == SC.EntryTypes.Application));
            }
        }
        finally { await CleanupAsync(s); }
    }

    // ═════════════════════════════════════════ account isolation ═════════════════════════════════════════

    [Fact]
    public async Task Accounts_are_isolated_by_supplier_company_and_currency_and_shared_by_the_plants_of_one_company()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;
        var s = await SeedAsync(creditA: 600m, creditB: 100m);
        try
        {
            await using var ctx = NewCtx();

            // The unique key (supplier × company × currency) is enforced by SQL Server, not only by the service pre-check.
            ctx.SupplierCreditAccounts.Add(new SupplierCreditAccount { SupplierId = s.SupplierId, CompanyId = s.CompanyA, CurrencyCode = "AOA", AccountNumber = "CCS-ZZ" + s.Tag[..6] + "X", Status = SC.AccountStatuses.Active, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Owner });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
            Assert.Contains("UX_SupplierCreditAccounts_Supplier_Company_Currency", ex.InnerException?.Message);
            ctx.ChangeTracker.Clear();

            var ledger = new SupplierCreditLedgerService(ctx, new InternalCompanyGuard(ctx), NullLogger<SupplierCreditLedgerService>.Instance);
            var dup = await ledger.CreateAccountAsync(s.SupplierId, new CreateSupplierCreditAccountDto { CompanyId = s.CompanyA, CurrencyCode = "AOA" }, s.Owner);
            Assert.False(dup.Succeeded); Assert.Equal(409, dup.StatusCode); Assert.Equal(SC.ErrorCodes.AccountExists, dup.Code);
            var usd = await ledger.CreateAccountAsync(s.SupplierId, new CreateSupplierCreditAccountDto { CompanyId = s.CompanyA, CurrencyCode = "USD" }, s.Owner);
            Assert.True(usd.Succeeded, usd.Detail); Assert.StartsWith(SC.AccountNumberPrefix, usd.Value!.AccountNumber); Assert.Equal("USD", usd.Value.CurrencyCode); // CCS number allocated under UPDLOCK on SQL Server
            Assert.Equal(3, await ctx.SupplierCreditAccounts.CountAsync(a => a.SupplierId == s.SupplierId));

            // Plants A1 and A2 of company A resolve the SAME account; plant B1 of company B resolves its own; the AOA groups never see the USD account.
            var (r1, g1) = await LoadAsync(ctx, s.Request1, s.Group1);
            var (r2, g2) = await LoadAsync(ctx, s.Request2, s.Group2);
            var (r3, g3) = await LoadAsync(ctx, s.Request3, s.Group3);
            var e1 = await Svc(ctx).EvaluateEligibilityAsync(r1, g1, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
            var e2 = await Svc(ctx).EvaluateEligibilityAsync(r2, g2, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
            var e3 = await Svc(ctx).EvaluateEligibilityAsync(r3, g3, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
            Assert.Equal(s.AccountA, e1.Account!.Id); Assert.Equal(s.AccountA, e2.Account!.Id); Assert.Equal(s.AccountB, e3.Account!.Id);
            Assert.Equal(600m, e1.AvailableForGroup); Assert.Equal(600m, e2.AvailableForGroup); Assert.Equal(100m, e3.AvailableForGroup);
            Assert.All(new[] { e1, e2, e3 }, e => Assert.Equal("AOA", e.Account!.CurrencyCode));

            // A hold taken from plant A1 reduces what plant A2 may reserve; company B is untouched.
            await using (var tx = await ctx.Database.BeginTransactionAsync())
            {
                var ok = await Svc(ctx).ReserveOrCorrectAsync(Reserve(r1, g1, 500m, s.Owner));
                Assert.True(ok.Succeeded, ok.Detail);
                await ctx.SaveChangesAsync(); await tx.CommitAsync();
            }
            ctx.ChangeTracker.Clear();
            (r2, g2) = await LoadAsync(ctx, s.Request2, s.Group2);
            (r3, g3) = await LoadAsync(ctx, s.Request3, s.Group3);
            var e2After = await Svc(ctx).EvaluateEligibilityAsync(r2, g2, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
            var e3After = await Svc(ctx).EvaluateEligibilityAsync(r3, g3, RequestConstants.PaymentConditions.PostPaid, actorMayReserve: true);
            Assert.Equal(100m, e2After.AvailableForGroup); Assert.Equal(100m, e2After.MaxCoverage);
            Assert.Equal(100m, e3After.AvailableForGroup); Assert.Equal(s.AccountB, e3After.Account!.Id);
            Assert.Equal((600m, 500m, 100m), await Svc(ctx).AccountFiguresAsync(s.AccountA));
            Assert.Equal((100m, 0m, 100m), await Svc(ctx).AccountFiguresAsync(s.AccountB));
            Assert.Equal((0m, 0m, 0m), await Svc(ctx).AccountFiguresAsync(usd.Value.Id));
        }
        finally { await CleanupAsync(s); }
    }
}
