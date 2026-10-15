using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Events;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Purchasing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// RELATIONAL (SQL Server LocalDB sandbox <c>Portal-Gerencial-IntegrationTests</c>) proof of the
/// alternative-approver concurrency contract: two eligible approvers decide the same batch at the
/// same time → exactly ONE committed transition, ONE PO-group activation, ONE downstream workflow
/// event; the loser either hits the stage guard (400) or the RowVersion race
/// (DbUpdateConcurrencyException, mapped to 409 by ApprovalConcurrencyExceptionFilter in HTTP).
/// InMemory cannot exercise rowversion semantics, hence this suite. Also verifies the migration's
/// validated backfill SQL. Skips (returns) when LocalDB is unavailable, like the sibling suites.
/// </summary>
[Collection("IntegrationTests")]
public class ApprovalConcurrencyRelationalTests
{
    /// <summary>Model-drift-aware bootstrap: recreate the sandbox when its schema differs from the current model (RowVersion / StageEnteredAtUtc / reminder tables; or a deferred CompanyFinalApprovers table left behind).</summary>
    static ApprovalConcurrencyRelationalTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            if (ctx.Database.CanConnect())
            {
                var deferredTable = ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.CompanyFinalApprovers'), 0) AS [Value]").AsEnumerable().First();
                var rowVersion = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.ApprovalBatches', 'RowVersion'), 0) AS int) AS [Value]").AsEnumerable().First();
                var reminders = ctx.Database.SqlQueryRaw<int>("SELECT ISNULL(OBJECT_ID('dbo.ApprovalReminderDigests'), 0) AS [Value]").AsEnumerable().First();
                var stageEntry = ctx.Database.SqlQueryRaw<int>("SELECT CAST(ISNULL(COL_LENGTH('dbo.ApprovalBatches', 'StageEnteredAtUtc'), 0) AS int) AS [Value]").AsEnumerable().First();
                if (deferredTable != 0 || rowVersion == 0 || reminders == 0 || stageEntry == 0)
                {
                    // Pooled connections from sibling suites would make DROP DATABASE fail silently.
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    ctx.Database.ExecuteSqlRaw("ALTER DATABASE CURRENT SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                    ctx.Database.EnsureDeleted();
                }
            }
            ctx.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            // LocalDB unavailable — CanConnect() gates every test. Anything else must be visible.
            Console.Error.WriteLine($"[ApprovalConcurrencyRelationalTests] sandbox bootstrap: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool CanConnect() => IntegrationTestDatabase.CanConnect();
    private static ApplicationDbContext NewCtx() => new(IntegrationTestDatabase.CreateOptions());

    private sealed record Seed(Guid RequestId, Guid BatchId, Guid GroupId, Guid ApproverA, Guid ApproverB, int CompanyId, int DepartmentId, Guid LineId, List<Guid> UserIds);

    private static async Task<T> GetOrAdd<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, Func<T> create) where T : class
    {
        var existing = await set.FirstOrDefaultAsync(match);
        if (existing != null) return existing;
        var e = create(); set.Add(e); return e;
    }

    /// <summary>One QUOTATION request + one batch in <paramref name="batchStatus"/> with one item and one PENDING PO group; two approvers holding the Final Approver role (current model: role + scope).</summary>
    private static async Task<Seed> SeedAsync(string batchStatus)
    {
        await using var ctx = NewCtx();
        var tag = "ZZTEST_" + Guid.NewGuid().ToString("N")[..8];
        var quotationType = await GetOrAdd(ctx.RequestTypes, t => t.Code == RequestConstants.Types.Quotation, () => new RequestType { Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        var status = await GetOrAdd(ctx.RequestStatuses, s => s.Code == "WAITING_FINAL_APPROVAL", () => new RequestStatus { Code = "WAITING_FINAL_APPROVAL", Name = "Aprovação Final" });
        var role = await GetOrAdd(ctx.Roles, r => r.RoleName == RoleConstants.FinalApprover, () => new Role { RoleName = RoleConstants.FinalApprover });
        await ctx.SaveChangesAsync();

        var company = new Company { Name = tag + " Co", IsActive = true };
        var dept = new Department { Name = tag + " Dep", IsActive = true };
        ctx.Companies.Add(company); ctx.Departments.Add(dept);
        var a = new User { Id = Guid.NewGuid(), FullName = tag + " A", Email = tag.ToLower() + ".a@test.local", IsActive = true };
        var b = new User { Id = Guid.NewGuid(), FullName = tag + " B", Email = tag.ToLower() + ".b@test.local", IsActive = true };
        var requester = new User { Id = Guid.NewGuid(), FullName = tag + " Req", Email = tag.ToLower() + ".r@test.local", IsActive = true };
        ctx.Users.AddRange(a, b, requester);
        await ctx.SaveChangesAsync();
        ctx.UserRoleAssignments.AddRange(new UserRoleAssignment { UserId = a.Id, RoleId = role.Id }, new UserRoleAssignment { UserId = b.Id, RoleId = role.Id });

        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = tag, Title = tag, StatusId = status.Id, RequestTypeId = quotationType.Id,
            DepartmentId = dept.Id, CompanyId = company.Id, RequesterId = requester.Id, CreatedAtUtc = DateTime.UtcNow, RequestedDateUtc = DateTime.UtcNow
        };
        ctx.Requests.Add(request);
        var line = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = 1, Description = tag + " item", Quantity = 1, UnitPrice = 100, TotalAmount = 100, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestLineItems.Add(line);
        var batch = new ApprovalBatch { Id = Guid.NewGuid(), RequestId = request.Id, BatchNumber = 1, Status = batchStatus, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = requester.Id };
        ctx.ApprovalBatches.Add(batch);
        ctx.ApprovalBatchItems.Add(new ApprovalBatchItem { Id = Guid.NewGuid(), ApprovalBatchId = batch.Id, RequestLineItemId = line.Id, CreatedAtUtc = DateTime.UtcNow });
        var group = new RequestPoGroup { Id = Guid.NewGuid(), RequestId = request.Id, ApprovalBatchId = batch.Id, Status = RequestConstants.PoGroupStatuses.Pending, TotalAmount = 100m, CreatedByUserId = requester.Id, SupplierNameSnapshot = tag + " Supplier" };
        ctx.RequestPoGroups.Add(group);
        await ctx.SaveChangesAsync();
        return new Seed(request.Id, batch.Id, group.Id, a.Id, b.Id, company.Id, dept.Id, line.Id, new List<Guid> { a.Id, b.Id, requester.Id });
    }

    private static async Task CleanupAsync(Seed s)
    {
        await using var ctx = NewCtx();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestStatusHistories WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestPoGroups WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ApprovalBatchItems WHERE ApprovalBatchId = {s.BatchId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ApprovalBatches WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RequestLineItems WHERE RequestId = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Requests WHERE Id = {s.RequestId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Companies WHERE Id = {s.CompanyId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Departments WHERE Id = {s.DepartmentId}");
        foreach (var u in s.UserIds)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserRoleAssignments WHERE UserId = {u}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Users WHERE Id = {u}");
        }
    }

    private static ApprovalBatchController BuildController(ApplicationDbContext ctx, Guid actorId, Mock<IWorkflowNotificationOrchestrator> orchestrator, bool areaManager)
    {
        // Current model: final approval authorization = "Final Approver" role (claims below) + scope; routing is not consulted.
        var routingMock = new Mock<IApprovalRoutingService>();
        routingMock.Setup(r => r.IsAreaManagerAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(areaManager);
        var controller = new ApprovalBatchController(ctx, NullLogger<ApprovalBatchController>.Instance, new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx), routingMock.Object, new QuotationItemEligibilityService(ctx), new BatchExtraItemDecisionService(ctx),
            new AdjustmentCycleService(ctx), orchestrator.Object);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.ToString()), new(ClaimTypes.Role, RoleConstants.FinalApprover), new(ClaimTypes.Role, RoleConstants.AreaApprover) };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return controller;
    }

    /// <summary>Runs one action per approver on its own context, concurrently; returns (okCount, conflictCount, guardCount).</summary>
    private static async Task<(int ok, int conflict, int guard, int other)> RaceAsync(Seed s, Mock<IWorkflowNotificationOrchestrator> orchestrator, bool areaManager,
        Func<ApprovalBatchController, Task<IActionResult>> action)
    {
        var gate = new TaskCompletionSource();
        async Task<object> Run(Guid actor)
        {
            await using var ctx = NewCtx();
            var controller = BuildController(ctx, actor, orchestrator, areaManager);
            await gate.Task; // both start together
            try { return await action(controller); }
            catch (DbUpdateConcurrencyException ex) { return ex; }
        }
        var t1 = Run(s.ApproverA); var t2 = Run(s.ApproverB);
        gate.SetResult();
        var results = await Task.WhenAll(t1, t2);
        int ok = results.Count(r => r is OkObjectResult);
        int conflict = results.Count(r => r is DbUpdateConcurrencyException);
        int guard = results.Count(r => r is BadRequestObjectResult);
        return (ok, conflict, guard, results.Length - ok - conflict - guard);
    }

    [Fact]
    public async Task Two_final_approvers_approving_the_same_batch_concurrently_commit_exactly_once()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        try
        {
            var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
            var (ok, conflict, guard, other) = await RaceAsync(s, orchestrator, areaManager: false,
                c => c.BatchFinalApprove(s.RequestId, s.BatchId, new BatchApprovalActionDto { Comment = "race" }));

            Assert.Equal(1, ok);
            Assert.Equal(0, other);
            Assert.Equal(1, conflict + guard); // loser: RowVersion race (→409 in HTTP) or stage guard (400)

            await using var verify = NewCtx();
            var batch = await verify.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == s.BatchId);
            Assert.Equal(RequestConstants.ApprovalBatchStatuses.Approved, batch.Status);
            Assert.Equal(1, await verify.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "BATCH_FINAL_APPROVED"));
            Assert.Equal(1, await verify.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "PO_GROUP_ACTIVATED"));
            Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPo, (await verify.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
            orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.FinalApproved)), Times.Once);
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task Two_area_managers_rejecting_the_same_batch_concurrently_commit_exactly_once()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval);
        try
        {
            var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
            var (ok, conflict, guard, other) = await RaceAsync(s, orchestrator, areaManager: true,
                c => c.BatchAreaReject(s.RequestId, s.BatchId, new BatchApprovalActionDto { Comment = "race reject with a sufficiently long reason" }));

            Assert.Equal(1, ok);
            Assert.Equal(0, other);
            Assert.Equal(1, conflict + guard);

            await using var verify = NewCtx();
            Assert.Equal(RequestConstants.ApprovalBatchStatuses.Rejected, (await verify.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == s.BatchId)).Status);
            Assert.Equal(1, await verify.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "BATCH_AREA_REJECTED"));
            orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.AreaRejected)), Times.Once);
        }
        finally { await CleanupAsync(s); }
    }

    [Fact]
    public async Task After_the_first_final_approval_the_other_approver_is_refused_and_emits_nothing()
    {
        if (!CanConnect()) return;
        var s = await SeedAsync(RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval);
        try
        {
            var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
            await using (var ctx = NewCtx())
                Assert.IsType<OkObjectResult>(await BuildController(ctx, s.ApproverA, orchestrator, false).BatchFinalApprove(s.RequestId, s.BatchId, new BatchApprovalActionDto { Comment = "first" }));
            await using (var ctx = NewCtx())
                Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, s.ApproverB, orchestrator, false).BatchFinalApprove(s.RequestId, s.BatchId, new BatchApprovalActionDto { Comment = "second" }));
            orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Once);
        }
        finally { await CleanupAsync(s); }
    }
}
