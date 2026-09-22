using System;
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
/// v2.245.9 — the request details DTO (<c>GET /requests/{id}</c>) presents group-scoped lifecycle rows
/// with the GROUP's resulting state and request-level rows with their persisted status name, and
/// exposes each group's operational document classification. Runs on the real relational model
/// (LocalDB) like <see cref="RequestBatchAdjustmentContextProjectionTests"/> — GetRequest's split
/// projection is not faithfully materialized by the InMemory provider; CanConnect() gates the test.
/// </summary>
[Collection("IntegrationTests")]
public class RequestDetailsHistoryTargetSqlTests
{
    static RequestDetailsHistoryTargetSqlTests()
    {
        try
        {
            using var ctx = new ApplicationDbContext(IntegrationTestDatabase.CreateOptions());
            ctx.Database.EnsureCreated();
        }
        catch { /* LocalDB unavailable — CanConnect() gates the test. */ }
    }

    private static DbContextOptions<ApplicationDbContext> DbOptions() => IntegrationTestDatabase.CreateOptions();

    private static RequestsController BuildController(ApplicationDbContext ctx, Guid actorId)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto());
        var statusSync = new Mock<IRequestStatusSyncService>();
        statusSync.Setup(s => s.ComputeDisplayWorkflowStateAsync(It.IsAny<Guid>())).ReturnsAsync(string.Empty);

        var controller = new RequestsController(
            ctx,
            new Mock<IDocumentExtractionService>().Object,
            new AdminLogWriter(new Mock<IServiceScopeFactory>().Object, new Mock<IHttpContextAccessor>().Object, NullLogger<AdminLogWriter>.Instance),
            NullLogger<RequestsController>.Instance,
            new Mock<INotificationService>().Object,
            new Mock<IWorkflowNotificationOrchestrator>().Object,
            new Mock<IPrimaveraRequestValidationService>().Object,
            new Mock<IGroupBuilderService>().Object,
            statusSync.Object,
            routing.Object,
            new Mock<ILineItemFactory>().Object,
            new Mock<IRequestLineItemSubmissionValidator>().Object,
            new Mock<IQuotationItemEligibilityService>().Object,
            new Mock<IBatchExtraItemDecisionService>().Object,
            new AlplaPortal.Infrastructure.Services.Suppliers.InternalCompanyGuard(ctx),
            Options.Create(new PostPaymentCompletionOptions()));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, actorId.ToString()), new Claim(ClaimTypes.Role, RoleConstants.SystemAdministrator) }, "Test")),
                RequestServices = new ServiceCollection().BuildServiceProvider()
            }
        };
        return controller;
    }

    private sealed record Seed(Guid RequestId, Guid ActorId);

    /// <summary>
    /// A completed single-group PAYMENT request as the v2.245.8 TEST validation left it: GROUP_COMPLETED
    /// written while the scalar was WAITING_RECEIPT, then REQUEST_COMPLETED → COMPLETED, plus a legacy
    /// request-level SUBMIT row. Returns null when the shared lookups are absent (older sandbox → skip).
    /// </summary>
    private static async Task<Seed?> SeedAsync()
    {
        await using var ctx = new ApplicationDbContext(DbOptions());
        var actor = new User { Id = Guid.NewGuid(), FullName = "ZZTEST History Target", Email = $"zztest-histtarget-{Guid.NewGuid():N}@test.local" };
        ctx.Users.Add(actor);

        var statuses = await ctx.RequestStatuses
            .Where(s => s.Code == "WAITING_AREA_APPROVAL" || s.Code == RequestConstants.Statuses.WaitingReceipt || s.Code == RequestConstants.Statuses.Completed)
            .ToDictionaryAsync(s => s.Code, s => s.Id);
        var typeId = await ctx.RequestTypes.Where(t => t.Code == RequestConstants.Types.Payment).Select(t => t.Id).FirstOrDefaultAsync();
        if (statuses.Count != 3 || typeId == 0) return null;

        var request = new Request
        {
            Id = Guid.NewGuid(), Title = "ZZTEST_HISTTARGET_" + Guid.NewGuid().ToString("N")[..8],
            RequestNumber = "ZZT-HIST-" + Guid.NewGuid().ToString("N")[..8],
            StatusId = statuses[RequestConstants.Statuses.Completed], RequestTypeId = typeId,
            DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = actor.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
        };
        ctx.Requests.Add(request);

        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(), RequestId = request.Id, SupplierNameSnapshot = "ZZTEST Supplier", CurrencyCode = "AOA",
            TotalAmount = 250_000m, Status = RequestConstants.PoGroupStatuses.Completed,
            SourceDocumentType = RequestConstants.SourceDocumentTypes.Proforma,
            OperationInvoiceStatus = RequestConstants.OperationInvoiceStatuses.Satisfied,
            RequiresOperationInvoice = true, RequiresSeparateFiscalReceipt = true,
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-5), CreatedAtUtc = DateTime.UtcNow.AddDays(-10), CreatedByUserId = actor.Id
        });

        var t0 = DateTime.UtcNow.AddMinutes(-10);
        ctx.RequestStatusHistories.AddRange(
            new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = actor.Id, ActionTaken = "SUBMIT",
                PreviousStatusId = null, NewStatusId = statuses["WAITING_AREA_APPROVAL"], Comment = "legacy", CreatedAtUtc = t0
            },
            new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = actor.Id, ActionTaken = WorkflowEventCodes.GroupCompleted,
                PreviousStatusId = statuses[RequestConstants.Statuses.WaitingReceipt], NewStatusId = statuses[RequestConstants.Statuses.WaitingReceipt],
                Comment = "Grupo ZZTEST Supplier CONCLUÍDO", IdempotencyKey = "GC:" + Guid.NewGuid().ToString("D") + ":ZZTEST", CreatedAtUtc = t0.AddMinutes(4)
            },
            new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = request.Id, ActorUserId = actor.Id, ActionTaken = "REQUEST_COMPLETED",
                PreviousStatusId = statuses[RequestConstants.Statuses.WaitingReceipt], NewStatusId = statuses[RequestConstants.Statuses.Completed],
                Comment = "Pedido CONCLUÍDO", IdempotencyKey = "RC:" + request.Id.ToString("D") + ":ZZTEST", CreatedAtUtc = t0.AddMinutes(5)
            });

        await ctx.SaveChangesAsync();
        return new Seed(request.Id, actor.Id);
    }

    private static async Task CleanupAsync(Guid requestId)
    {
        if (requestId == Guid.Empty) return;
        await using var ctx = new ApplicationDbContext(DbOptions());
        await ctx.Database.ExecuteSqlRawAsync(
            "DELETE FROM RequestStatusHistories WHERE RequestId = {0};" +
            "DELETE FROM RequestPoGroups WHERE RequestId = {0};" +
            "DELETE FROM Requests WHERE Id = {0};" +
            "DELETE FROM Users WHERE Email LIKE 'zztest-histtarget-%' AND NOT EXISTS (SELECT 1 FROM Requests r WHERE r.RequesterId = Users.Id);", requestId);
    }

    [Fact]
    public async Task Details_history_presents_group_completed_as_concluido_request_completed_as_finalizado_and_legacy_rows_unchanged()
    {
        if (!IntegrationTestDatabase.CanConnect()) return;
        var s = await SeedAsync();
        if (s == null) return;
        try
        {
            await using var ctx = new ApplicationDbContext(DbOptions());
            var result = await BuildController(ctx, s.ActorId).GetRequest(s.RequestId);
            var dto = Assert.IsType<RequestDetailsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

            var history = dto.StatusHistory;
            Assert.Equal("Concluído", Assert.Single(history, h => h.ActionTaken == "GROUP_COMPLETED").NewStatusName);
            Assert.Equal("Finalizado", Assert.Single(history, h => h.ActionTaken == "REQUEST_COMPLETED").NewStatusName);
            // Legacy request-level row: the PERSISTED status name, exactly as stored — never relabelled.
            var persistedAreaName = await ctx.RequestStatuses.Where(s => s.Code == "WAITING_AREA_APPROVAL").Select(s => s.Name).SingleAsync();
            Assert.Equal(persistedAreaName, Assert.Single(history, h => h.ActionTaken == "SUBMIT").NewStatusName);
            Assert.DoesNotContain(history, h => h.ActionTaken == "STATUS_SYNC");
            Assert.Equal(3, history.Count);

            // The group's OPERATIONAL classification travels with the group, never through the request header.
            var group = Assert.Single(dto.PoGroups);
            Assert.Equal(RequestConstants.SourceDocumentTypes.Proforma, group.SourceDocumentType);
        }
        finally { await CleanupAsync(s.RequestId); }
    }
}
