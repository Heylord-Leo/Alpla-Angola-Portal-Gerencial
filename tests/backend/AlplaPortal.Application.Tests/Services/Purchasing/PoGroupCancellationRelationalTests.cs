using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Requests;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using S = AlplaPortal.Application.Tests.Services.Purchasing.PoCancellationTestSeed;
using C = AlplaPortal.Domain.Constants.PoGroupCancellationConstants;

namespace AlplaPortal.Application.Tests.Services.Purchasing;

/// <summary>
/// RELATIONAL (SQL Server LocalDB sandbox <c>Portal-Gerencial-IntegrationTests</c>) proof for phase 1:
///  • the D4 guard's <c>UPDLOCK, HOLDLOCK</c> read inside a transaction blocks a concurrent Finance confirmation until
///    that transaction ends (and the confirmation then succeeds) — registration and confirmation never interleave;
///  • RegisterPo on real SQL Server is refused while the prior cancellation is unconfirmed and accepted after it
///    (exercises the raw-SQL lock path end to end);
///  • two simultaneous cancellations of the same group produce exactly one cancellation (RowVersion / unique index);
///  • a failure inside the cancellation transaction leaves nothing behind (group, line, document, rows).
/// Tests skip silently when LocalDB is unavailable (repo convention); run them where it is.
/// </summary>
[Collection("IntegrationTests")]
public class PoGroupCancellationRelationalTests
{
    static PoGroupCancellationRelationalTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                // Drift-aware bootstrap: the sandbox predates this feature's tables → rebuild it (EnsureCreated is a no-op on an existing DB).
                var has = ctx.Database.SqlQueryRaw<int>("SELECT CAST(CASE WHEN COL_LENGTH('dbo.RequestPoGroupCancellations', 'FinanceEmailPolicyOutcome') IS NULL OR COL_LENGTH('dbo.RequestPoGroups', 'CurrentPoAttachmentId') IS NULL OR OBJECT_ID('dbo.PoDocumentExtractions') IS NULL THEN 0 ELSE 1 END AS int) AS [Value]").AsEnumerable().First();   // v2.249.4 drift
                if (has == 0)
                {
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    ctx.Database.ExecuteSqlRaw("ALTER DATABASE CURRENT SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                    ctx.Database.EnsureDeleted();
                }
            }
            ctx.Database.EnsureCreated();
        }
        catch (Exception ex) { Console.Error.WriteLine($"[PoGroupCancellationRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private sealed record Seed(Guid RequestId, Guid Owner, Guid Finance1, Guid Finance2, Guid Requester, Guid Line1, Guid Line2, Guid GroupA, Guid GroupB, Guid Batch, Guid PoAttachment, int PlantId, string Tag);

    private static async Task CleanupAsync(ApplicationDbContext ctx)
    {
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE i FROM RequestPoGroupCancellationItems i INNER JOIN RequestPoGroupCancellations c ON c.Id=i.RequestPoGroupCancellationId INNER JOIN Requests r ON r.Id=c.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE e FROM RequestPoGroupCancellationEcfEvents e INNER JOIN RequestPoGroupCancellations c ON c.Id=e.RequestPoGroupCancellationId INNER JOIN Requests r ON r.Id=c.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE c FROM RequestPoGroupCancellations c INNER JOIN Requests r ON r.Id=c.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE o FROM EmailOutbox o INNER JOIN Requests r ON r.Id=o.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE n FROM InformationalNotifications n WHERE EXISTS (SELECT 1 FROM Requests r WHERE r.RequestNumber LIKE 'ZZT-POC-%' AND n.TargetPath LIKE '%' + CAST(r.Id AS varchar(36)) + '%');" +
            "DELETE h FROM RequestFieldChangeHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE h FROM RequestStatusHistories h INNER JOIN Requests r ON r.Id=h.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE c FROM ApprovalBatchItemCandidates c INNER JOIN ApprovalBatchItems bi ON bi.Id=c.ApprovalBatchItemId INNER JOIN ApprovalBatches b ON b.Id=bi.ApprovalBatchId INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE bi FROM ApprovalBatchItems bi INNER JOIN ApprovalBatches b ON b.Id=bi.ApprovalBatchId INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "UPDATE li SET RequestPoGroupId=NULL, SelectedQuotationItemId=NULL FROM RequestLineItems li INNER JOIN Requests r ON r.Id=li.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE a FROM RequestAttachments a INNER JOIN Requests r ON r.Id=a.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE p FROM RequestPayments p INNER JOIN Requests r ON r.Id=p.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE g FROM RequestPoGroups g INNER JOIN Requests r ON r.Id=g.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE b FROM ApprovalBatches b INNER JOIN Requests r ON r.Id=b.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE qi FROM QuotationItems qi INNER JOIN Quotations q ON q.Id=qi.QuotationId INNER JOIN Requests r ON r.Id=q.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE q FROM Quotations q INNER JOIN Requests r ON r.Id=q.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE li FROM RequestLineItems li INNER JOIN Requests r ON r.Id=li.RequestId WHERE r.RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE FROM Requests WHERE RequestNumber LIKE 'ZZT-POC-%';" +
            "DELETE FROM UserPlantScopes WHERE UserId IN (SELECT Id FROM Users WHERE Email LIKE 'zztest-poc-%');" +
            "DELETE FROM UserRoleAssignments WHERE UserId IN (SELECT Id FROM Users WHERE Email LIKE 'zztest-poc-%');" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-poc-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId=Users.Id OR r.BuyerId=Users.Id);" +
            "DELETE FROM Suppliers WHERE PortalCode LIKE 'ZZT-POC-%';");
    }

    /// <summary>QUOTATION request with APPROVED batch #1: line 1 in group A (PO_ISSUED, P.O. number + file), line 2 in group B (WAITING_PO). Uses the sandbox's seeded lookups.</summary>
    private static async Task<Seed?> SeedAsync(string groupAStatus = "PO_ISSUED")
    {
        await using var ctx = NewCtx();
        await CleanupAsync(ctx);
        var typeId = await ctx.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync();
        var statusId = await ctx.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.PoIssued).Select(s => s.Id).FirstOrDefaultAsync();
        var plant = await ctx.Plants.OrderBy(p => p.Id).FirstOrDefaultAsync();
        var dept = await ctx.Set<Department>().OrderBy(d => d.Id).FirstOrDefaultAsync();
        var buyerRole = await ctx.Roles.Where(r => r.RoleName == RoleConstants.Buyer).Select(r => r.Id).FirstOrDefaultAsync();
        var financeRole = await ctx.Roles.Where(r => r.RoleName == RoleConstants.Finance).Select(r => r.Id).FirstOrDefaultAsync();
        var waitingQuotation = await ctx.LineItemStatuses.Where(l => l.Code == "WAITING_QUOTATION").Select(l => l.Id).FirstOrDefaultAsync();
        if (typeId == 0 || statusId == 0 || plant == null || dept == null || buyerRole == 0 || financeRole == 0 || waitingQuotation == 0) return null;
        var tag = Guid.NewGuid().ToString("N")[..8];
        var owner = new User { Id = Guid.NewGuid(), FullName = "ZZ Comprador", Email = $"zztest-poc-owner-{tag}@test.local", IsActive = true };
        var fin1 = new User { Id = Guid.NewGuid(), FullName = "ZZ Finance 1", Email = $"zztest-poc-fin1-{tag}@test.local", IsActive = true };
        var fin2 = new User { Id = Guid.NewGuid(), FullName = "ZZ Finance 2", Email = $"zztest-poc-fin2-{tag}@test.local", IsActive = true };
        var req = new User { Id = Guid.NewGuid(), FullName = "ZZ Requisitante", Email = $"zztest-poc-req-{tag}@test.local", IsActive = true };
        ctx.Users.AddRange(owner, fin1, fin2, req);
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = owner.Id, RoleId = buyerRole }, new UserRoleAssignment { UserId = fin1.Id, RoleId = financeRole }, new UserRoleAssignment { UserId = fin2.Id, RoleId = financeRole });
        ctx.UserPlantScopes.AddRange(new UserPlantScope { UserId = owner.Id, PlantId = plant.Id }, new UserPlantScope { UserId = fin1.Id, PlantId = plant.Id }, new UserPlantScope { UserId = fin2.Id, PlantId = plant.Id });
        var supA = new Supplier { Name = "ZZ PRODUCTIVE " + tag, TaxId = "54" + tag, RegistrationStatus = "ACTIVE", PortalCode = "ZZT-POC-A-" + tag };
        var supB = new Supplier { Name = "ZZ OUTRO " + tag, TaxId = "55" + tag, RegistrationStatus = "ACTIVE", PortalCode = "ZZT-POC-B-" + tag };
        ctx.Suppliers.AddRange(supA, supB);
        await ctx.SaveChangesAsync();

        var s = new Seed(Guid.NewGuid(), owner.Id, fin1.Id, fin2.Id, req.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), plant.Id, tag);
        ctx.Requests.Add(new Request { Id = s.RequestId, RequestNumber = "ZZT-POC-" + tag, Title = "ZZTEST P.O. cancellation", RequestTypeId = typeId, StatusId = statusId, RequesterId = req.Id, BuyerId = owner.Id, DepartmentId = dept.Id, CompanyId = plant.CompanyId, PlantId = plant.Id, EstimatedTotalAmount = 0m, ApprovedTotalAmount = 100m, ApprovedCurrencyCode = "AOA", CreatedAtUtc = DateTime.UtcNow.AddDays(-5), UpdatedAtUtc = DateTime.UtcNow });
        var q1 = Guid.NewGuid(); var q2 = Guid.NewGuid(); var qi1 = Guid.NewGuid(); var qi2 = Guid.NewGuid();
        ctx.Quotations.AddRange(
            new Quotation { Id = q1, RequestId = s.RequestId, SupplierId = supA.Id, SupplierNameSnapshot = supA.Name, Currency = "AOA", TotalAmount = 60m, SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow.AddDays(-4), CreatedByUserId = owner.Id },
            new Quotation { Id = q2, RequestId = s.RequestId, SupplierId = supB.Id, SupplierNameSnapshot = supB.Name, Currency = "AOA", TotalAmount = 40m, SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow.AddDays(-4), CreatedByUserId = owner.Id });
        ctx.QuotationItems.AddRange(
            new QuotationItem { Id = qi1, QuotationId = q1, Description = "A", MappedRequestLineItemId = s.Line1, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 60m, LineTotal = 60m },
            new QuotationItem { Id = qi2, QuotationId = q2, Description = "B", MappedRequestLineItemId = s.Line2, ReconciliationStatus = "MAPPED", Quantity = 1, UnitPrice = 40m, LineTotal = 40m });
        ctx.RequestLineItems.AddRange(
            new RequestLineItem { Id = s.Line1, RequestId = s.RequestId, LineNumber = 1, Description = "Item A", Quantity = 1, LineItemStatusId = waitingQuotation, QuotationLifecycleStatus = "QUOTATION_APPROVED", CreatedAtUtc = DateTime.UtcNow },
            new RequestLineItem { Id = s.Line2, RequestId = s.RequestId, LineNumber = 2, Description = "Item B", Quantity = 1, LineItemStatusId = waitingQuotation, QuotationLifecycleStatus = "QUOTATION_APPROVED", CreatedAtUtc = DateTime.UtcNow });
        ctx.ApprovalBatches.Add(new ApprovalBatch { Id = s.Batch, RequestId = s.RequestId, BatchNumber = 1, Status = "APPROVED", ApprovedTotalAmount = 100m, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = owner.Id });
        ctx.RequestPoGroups.AddRange(
            new RequestPoGroup { Id = s.GroupA, RequestId = s.RequestId, SupplierId = supA.Id, SupplierNameSnapshot = supA.Name, CurrencyCode = "AOA", PlantId = plant.Id, TotalAmount = 60m, Status = groupAStatus, ApprovalBatchId = s.Batch, PurchaseOrderNumber = "ECF10 2026/" + tag, PaymentConditionCode = "POST_PAID", PoResponsibleBuyerId = owner.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = owner.Id },
            new RequestPoGroup { Id = s.GroupB, RequestId = s.RequestId, SupplierId = supB.Id, SupplierNameSnapshot = supB.Name, CurrencyCode = "AOA", PlantId = plant.Id, TotalAmount = 40m, Status = "WAITING_PO", ApprovalBatchId = s.Batch, CreatedAtUtc = DateTime.UtcNow.AddDays(-3), CreatedByUserId = owner.Id });
        await ctx.SaveChangesAsync();
        // second write unit: line → group / winner pointers and batch items (circular FK with the line award)
        var l1 = await ctx.RequestLineItems.SingleAsync(l => l.Id == s.Line1); l1.RequestPoGroupId = s.GroupA; l1.SelectedQuotationItemId = qi1;
        var l2 = await ctx.RequestLineItems.SingleAsync(l => l.Id == s.Line2); l2.RequestPoGroupId = s.GroupB; l2.SelectedQuotationItemId = qi2;
        ctx.ApprovalBatchItems.AddRange(
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = s.Batch, RequestLineItemId = s.Line1, SelectedQuotationItemId = qi1, CreatedAtUtc = DateTime.UtcNow.AddDays(-3) },
            new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = s.Batch, RequestLineItemId = s.Line2, SelectedQuotationItemId = qi2, CreatedAtUtc = DateTime.UtcNow.AddDays(-3) });
        ctx.RequestAttachments.Add(new RequestAttachment { Id = s.PoAttachment, RequestId = s.RequestId, RequestPoGroupId = s.GroupA, AttachmentTypeCode = "PO", FileName = "po.pdf", FileExtension = ".pdf", StorageReference = "zz/po.pdf", UploadedByUserId = owner.Id, UploadedAtUtc = DateTime.UtcNow.AddDays(-2) });
        await ctx.SaveChangesAsync();
        return s;
    }

    private static PoGroupCancellationService Svc(ApplicationDbContext ctx, IWorkflowNotificationOrchestrator? orchestrator = null)
        => S.Service(ctx, orchestrator ?? S.OrchestratorMock().Mock.Object);

    private static CancelPoGroupCommand Cancel(Seed s, Guid? group = null) => new(s.RequestId, group ?? s.GroupA, S.Buyer(s.Owner), "RESTART_QUOTATION", "SUPPLIER_CANCELLED",
        "Fornecedor informou que não consegue entregar o item.", "DECLARED_CANCELLED", "ECF10 2026/" + s.Tag, null, null, null);

    /// <summary>Real orchestrator + real in-app service on the SAME context (production wiring); the in-app service can be replaced to inject a failure.</summary>
    private static (WorkflowNotificationOrchestrator Orchestrator, Mock<IEmailService> Email) RealOrchestrator(ApplicationDbContext ctx, INotificationService? inApp = null)
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendWorkflowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:FrontendBaseUrl"] = "https://portal.test" }).Build();
        var orchestrator = new WorkflowNotificationOrchestrator(ctx, inApp ?? new NotificationService(ctx, NullLogger<NotificationService>.Instance), email.Object, config,
            NullLogger<WorkflowNotificationOrchestrator>.Instance, S.AdminLog().Object, new ApprovalRoutingService(ctx));
        return (orchestrator, email);
    }

    private static RequestsController Buyer(ApplicationDbContext ctx, Guid buyerId, PostPaymentCompletionOptions? completion = null)
    {
        var options = completion ?? new PostPaymentCompletionOptions();
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

    /// <summary>After cancelling A, approve batch #2 with group C for line 1 (WAITING_PO, P.O. file attached).</summary>
    private static async Task<(Guid CancellationId, Guid GroupC)> RestartedAsync(Seed s)
    {
        Guid cancellationId;
        await using (var ctx = NewCtx())
        {
            var r = await Svc(ctx).CancelAsync(Cancel(s));
            Assert.True(r.Succeeded, $"{r.Code}: {r.Detail}");
            cancellationId = ((PoGroupCancellationResultDto)r.Payload!).CancellationId;
        }
        var groupC = Guid.NewGuid();
        await using (var ctx = NewCtx())
        {
            var supA = await ctx.RequestPoGroups.AsNoTracking().Where(g => g.Id == s.GroupA).Select(g => new { g.SupplierId, g.SupplierNameSnapshot }).SingleAsync();
            var batch2 = Guid.NewGuid();
            ctx.ApprovalBatches.Add(new ApprovalBatch { Id = batch2, RequestId = s.RequestId, BatchNumber = 2, Status = "APPROVED", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Owner });
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = groupC, RequestId = s.RequestId, SupplierId = supA.SupplierId, SupplierNameSnapshot = supA.SupplierNameSnapshot, CurrencyCode = "AOA", PlantId = s.PlantId, TotalAmount = 58m, Status = "WAITING_PO", ApprovalBatchId = batch2, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = s.Owner });
            await ctx.SaveChangesAsync();
            var line = await ctx.RequestLineItems.SingleAsync(l => l.Id == s.Line1); line.RequestPoGroupId = groupC; line.QuotationLifecycleStatus = "QUOTATION_APPROVED";
            ctx.ApprovalBatchItems.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch2, RequestLineItemId = s.Line1, CreatedAtUtc = DateTime.UtcNow });
            ctx.RequestAttachments.Add(new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, RequestPoGroupId = groupC, AttachmentTypeCode = "PO", FileName = "po-c.pdf", FileExtension = ".pdf", StorageReference = "zz/po-c.pdf", UploadedByUserId = s.Owner, UploadedAtUtc = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }
        return (cancellationId, groupC);
    }

    [Fact]
    public async Task Guard_lock_blocks_a_concurrent_Finance_confirmation_until_the_registering_transaction_ends()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            var (cancellationId, groupC) = await RestartedAsync(s);

            // tx1: the registering transaction holds the guard's UPDLOCK/HOLDLOCK on the cancellation rows of line 1
            await using var ctx1 = NewCtx();
            await using var tx1 = await ctx1.Database.BeginTransactionAsync();
            var block = await new PriorEcfConfirmationGuard(ctx1).EvaluateForGroupAsync(groupC, lockRows: true);
            Assert.True(block.Blocked); Assert.Equal(cancellationId, Assert.Single(block.Cancellations).CancellationId);

            // tx2: Finance tries to confirm concurrently → waits on the row lock
            await using var ctx2 = NewCtx();
            var confirm = Svc(ctx2).FinanceReviewAsync(new FinanceReviewCommand(cancellationId, S.Finance(s.Finance1), true, "Verificado.", null));
            var completedEarly = await Task.WhenAny(confirm, Task.Delay(2500)) == confirm;
            Assert.False(completedEarly, "the Finance confirmation must wait for the registering transaction");

            await tx1.RollbackAsync();
            var result = await confirm;
            Assert.True(result.Succeeded, $"{result.Code}: {result.Detail}");

            await using var ctx3 = NewCtx();
            Assert.False((await new PriorEcfConfirmationGuard(ctx3).EvaluateForGroupAsync(groupC, lockRows: false)).Blocked);
            Assert.Equal("CONFIRMED", (await ctx3.RequestPoGroupCancellations.AsNoTracking().SingleAsync(c => c.Id == cancellationId)).FinanceConfirmationStatus);
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }

    [Fact]
    public async Task RegisterPo_on_SQL_Server_is_refused_until_confirmation_and_the_refusal_writes_nothing()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            var (cancellationId, groupC) = await RestartedAsync(s);
            var dto = new RegisterPoActionDto { PoGroupId = groupC, PaymentConditionCode = "POST_PAID", PurchaseOrderNumber = "ECF10 2026/NEW" + s.Tag, DocumentTypeConfirmed = true, Comment = "ZZTEST tipo de documento confirmado manualmente (sem extração registada no teste)" };

            await using (var ctx = NewCtx())
            {
                var refused = await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, dto);
                var pd = Assert.IsType<ProblemDetails>(Assert.IsType<ConflictObjectResult>(refused).Value);
                Assert.Equal(C.ErrorCodes.PriorEcfConfirmationPending, pd.Extensions["code"]);
            }
            await using (var ctx = NewCtx())
            {
                Assert.Equal("WAITING_PO", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupC)).Status);
                Assert.False(await ctx.RequestStatusHistories.AnyAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "REGISTER_PO" && h.Comment!.Contains(groupC.ToString().Substring(0, 8))));
                Assert.True((await Svc(ctx).FinanceReviewAsync(new FinanceReviewCommand(cancellationId, S.Finance(s.Finance2), true, null, null))).Succeeded);
            }
            await using (var ctx = NewCtx())
            {
                Assert.IsType<OkObjectResult>(await Buyer(ctx, s.Owner).RegisterPo(s.RequestId, dto));
            }
            await using (var ctx = NewCtx())
            {
                var c = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupC);
                Assert.Equal("PO_ISSUED", c.Status); Assert.Equal(dto.PurchaseOrderNumber, c.PurchaseOrderNumber);
                Assert.Equal("CANCELLED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
                Assert.Equal("WAITING_PO", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupB)).Status);   // sibling untouched throughout
            }
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }

    [Fact]
    public async Task Two_simultaneous_cancellations_of_the_same_group_produce_exactly_one()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            await using var ctxA = NewCtx(); await using var ctxB = NewCtx();
            var gate = new SemaphoreSlim(0, 2);
            async Task<PoGroupCancellationOperationResult> Run(ApplicationDbContext ctx) { gate.Release(); await gate.WaitAsync(); return await Svc(ctx).CancelAsync(Cancel(s)); }
            var results = await Task.WhenAll(Run(ctxA), Run(ctxB));
            Assert.Equal(1, results.Count(r => r.Succeeded));
            var loser = results.Single(r => !r.Succeeded);
            Assert.Equal(409, loser.Status);
            Assert.Contains(loser.Code, new[] { C.ErrorCodes.CancellationConcurrentChange, C.ErrorCodes.GroupAlreadyCancelled });
            await using var ctx = NewCtx();
            Assert.Equal(1, await ctx.RequestPoGroupCancellations.CountAsync(c => c.RequestPoGroupId == s.GroupA));
            Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == C.HistoryActions.GroupCancelled));
            Assert.Equal(1, await ctx.RequestPoGroupCancellationItems.CountAsync(i => i.RequestLineItemId == s.Line1));
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }

    [Fact]
    public async Task A_failure_inside_the_cancellation_transaction_leaves_nothing_behind()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            var failing = new Mock<IWorkflowNotificationOrchestrator>();
            failing.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).ThrowsAsync(new InvalidOperationException("injected: in-app notification store unavailable"));
            await using (var ctx = NewCtx())
                await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(ctx, failing.Object).CancelAsync(Cancel(s)));
            await using (var ctx = NewCtx())
            {
                Assert.Equal("PO_ISSUED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
                var line = await ctx.RequestLineItems.AsNoTracking().SingleAsync(l => l.Id == s.Line1);
                Assert.Equal(s.GroupA, line.RequestPoGroupId); Assert.Equal("QUOTATION_APPROVED", line.QuotationLifecycleStatus);
                Assert.Null((await ctx.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.PoAttachment)).VoidedAtUtc);
                Assert.Equal(0, await ctx.RequestPoGroupCancellations.CountAsync(c => c.RequestId == s.RequestId));
                Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == C.HistoryActions.GroupCancelled));
                Assert.Equal(0, await ctx.ApprovalBatchItems.CountAsync(bi => bi.ApprovalBatchId == s.Batch && bi.ReleasedAtUtc != null));
            }
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }
    /// <summary>In-app store fails on the SECOND recipient: the first recipient's outbox row and in-app row were already written
    /// inside the transaction — after the rollback none of them exists, nor any business change. Nothing was sent (the
    /// e-mail transport is never called inside EmitAsync; the AP notice is deferred to after the commit).</summary>
    [Fact]
    public async Task Outbox_and_in_app_rows_written_before_a_later_failure_are_rolled_back_with_the_business_change()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            await using var ctx = NewCtx();
            var real = new NotificationService(ctx, NullLogger<NotificationService>.Instance);
            var inApp = new Mock<INotificationService>(); var calls = 0;
            inApp.Setup(n => n.CreateNotificationWithDedupAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .Returns<Guid, string, string, string, string, Guid, string>(async (u, t, m, ty, p, c, cat) =>
                {
                    if (++calls == 2) throw new InvalidOperationException("injected: in-app store unavailable");
                    return await real.CreateNotificationWithDedupAsync(u, t, m, ty, p, c, cat);
                });
            var (orchestrator, email) = RealOrchestrator(ctx, inApp.Object);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Svc(ctx, orchestrator).CancelAsync(Cancel(s)));
            Assert.True(calls >= 2);
            email.VerifyNoOtherCalls();

            await using var check = NewCtx();
            Assert.Equal(0, await check.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId));
            Assert.Equal(0, await check.InformationalNotifications.CountAsync(n => n.TargetPath!.Contains(s.RequestId.ToString())));
            Assert.Equal(0, await check.RequestPoGroupCancellations.CountAsync(c => c.RequestId == s.RequestId));
            Assert.Equal("PO_ISSUED", (await check.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
            Assert.Null((await check.RequestAttachments.AsNoTracking().SingleAsync(a => a.Id == s.PoAttachment)).VoidedAtUtc);
            Assert.Equal(0, await check.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == C.HistoryActions.GroupCancelled));
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }

    /// <summary>Happy path with the production wiring: business rows, histories, in-app rows and outbox rows exist together
    /// after the commit, keyed by one correlation; the transport was not called by EmitAsync; a repeated cancel is refused
    /// without new rows (retry safety).</summary>
    [Fact]
    public async Task Committed_cancellation_persists_business_audit_in_app_and_outbox_rows_atomically_and_a_retry_adds_nothing()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            Guid cancellationId; Guid? correlation;
            await using (var ctx = NewCtx())
            {
                var (orchestrator, email) = RealOrchestrator(ctx);
                var r = await Svc(ctx, orchestrator).CancelAsync(Cancel(s));
                Assert.True(r.Succeeded, $"{r.Code}: {r.Detail}");
                email.VerifyNoOtherCalls();   // no AP configuration seeded → nothing external; outbox rows only
                cancellationId = ((PoGroupCancellationResultDto)r.Payload!).CancellationId;
            }
            await using (var ctx = NewCtx())
            {
                var c = await ctx.RequestPoGroupCancellations.AsNoTracking().SingleAsync(x => x.Id == cancellationId);
                correlation = c.LastNotificationCorrelationId; Assert.NotNull(correlation);
                Assert.Equal("QUEUED", c.FinanceEmailPolicyOutcome); Assert.Equal(2, c.FinanceEmailsQueuedCount); Assert.True(c.BuyerEmailQueued);
                var outbox = await ctx.EmailOutbox.AsNoTracking().Where(e => e.CorrelationId == correlation).ToListAsync();
                Assert.Contains(outbox, o => o.RecipientEmail.StartsWith("zztest-poc-fin1")); Assert.Contains(outbox, o => o.RecipientEmail.StartsWith("zztest-poc-fin2")); Assert.Contains(outbox, o => o.RecipientEmail.StartsWith("zztest-poc-owner"));
                Assert.All(outbox, o => Assert.Equal("PENDING", o.Status));
                Assert.True(await ctx.InformationalNotifications.AnyAsync(n => n.EventCorrelationId == correlation && n.UserId == s.Finance1));
                Assert.Equal("CANCELLED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupA)).Status);
            }
            await using (var ctx = NewCtx())
            {
                var (orchestrator, _) = RealOrchestrator(ctx);
                var again = await Svc(ctx, orchestrator).CancelAsync(Cancel(s));
                Assert.Equal(409, again.Status); Assert.Equal(C.ErrorCodes.GroupAlreadyCancelled, again.Code);
                Assert.Equal(1, await ctx.RequestPoGroupCancellations.CountAsync(c => c.RequestPoGroupId == s.GroupA));
                Assert.Equal(await ctx.EmailOutbox.CountAsync(e => e.CorrelationId == correlation), await ctx.EmailOutbox.CountAsync(e => e.RequestId == s.RequestId));
            }
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }

    /// <summary>RegisterPo with the post-payment completion lifecycle ENABLED: phase one runs inside the registering
    /// transaction (it opens none of its own) and phase two runs after the commit (its ambient-transaction guard does not
    /// fire). Proven on SQL Server, where a nested BeginTransaction would throw.</summary>
    [Fact]
    public async Task RegisterPo_transaction_coexists_with_the_completion_phases_when_the_lifecycle_is_enabled()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(); if (s == null) return;
        try
        {
            var (cancellationId, groupC) = await RestartedAsync(s);
            await using (var ctx = NewCtx())
                Assert.True((await Svc(ctx).FinanceReviewAsync(new FinanceReviewCommand(cancellationId, S.Finance(s.Finance1), true, null, null))).Succeeded);
            var enabled = new PostPaymentCompletionOptions { Enabled = true, CompletionEnabled = true };
            await using (var ctx = NewCtx())
            {
                var result = await Buyer(ctx, s.Owner, enabled).RegisterPo(s.RequestId, new RegisterPoActionDto { PoGroupId = groupC, PaymentConditionCode = "POST_PAID", PurchaseOrderNumber = "ECF10 2026/ENB" + s.Tag, DocumentTypeConfirmed = true, Comment = "ZZTEST tipo de documento confirmado manualmente (sem extração registada no teste)" });
                Assert.IsType<OkObjectResult>(result);
                Assert.Null(ctx.Database.CurrentTransaction);   // the registering transaction was committed and released
            }
            await using (var ctx = NewCtx())
                Assert.Equal("PO_ISSUED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == groupC)).Status);
        }
        finally { await using var c = NewCtx(); await CleanupAsync(c); }
    }
}
