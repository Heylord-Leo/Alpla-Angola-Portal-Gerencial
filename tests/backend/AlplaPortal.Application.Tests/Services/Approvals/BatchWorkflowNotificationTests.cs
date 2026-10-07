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
using AlplaPortal.Infrastructure.Services;
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
/// The approval-batch workflow (buyer "Enviar itens para aprovação" → area decision → final
/// decision) emitted NO workflow event at those transitions in v2.245.12, so area managers and
/// final approvers received neither email nor in-app notification for batch-based approvals
/// (PROD: four QUOTATION requests pending area approval since late August 2026 with no approval
/// mail ever queued). These tests pin the corrected contract:
///   • each committed stage transition emits exactly one event, AFTER SaveChanges;
///   • the correlation is the stage's own RequestStatusHistory row — distinct per batch and per
///     transition, so a second batch is never deduped against the first;
///   • retrying a committed action is refused by the stage guard and emits nothing more;
///   • the event carries BatchNumber so templates can name the lot;
///   • a notification failure never turns a committed action into an error response.
/// </summary>
public class BatchWorkflowNotificationTests
{
    private const string BudgetJustification = "Justificativa orçamental de teste com tamanho suficiente para o gate.";
    private const string RejectComment = "Rejeitado para teste de notificação do lote — motivo suficientemente descritivo.";

    private static readonly decimal[] KwanzaTotals = { 253_080m, 660_060m, 266_760m, 328_320m };
    private static readonly decimal[] LuandaTotals = { 272_232m, 625_860m, 287_280m, 312_360m };

    private sealed record Seed(DbContextOptions<ApplicationDbContext> Options, Guid RequestId, Guid Actor, Guid[] LineIds, Guid[] KwanzaItems, Guid[] LuandaItems);

    private static async Task<Seed> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var ctx = new ApplicationDbContext(options);
        var actor = Guid.NewGuid();
        var quotationType = new RequestType { Id = 1, Code = RequestConstants.Types.Quotation, Name = "Cotação" };
        var status = new RequestStatus { Id = 1, Code = "WAITING_QUOTATION", Name = "Em Cotação" };
        ctx.RequestTypes.Add(quotationType);
        ctx.RequestStatuses.Add(status);
        ctx.Currencies.Add(new Currency { Id = 900, Code = "AOA", Symbol = "Kz" });
        ctx.Departments.Add(new Department { Id = 940, Name = "ZZ Departamento" });
        ctx.Companies.Add(new Company { Id = 940, Name = "ZZ Companhia" });
        ctx.Users.Add(new User { Id = actor, FullName = "ZZ Aprovador", Email = "zz.aprovador@test.local" });
        var kwanza = new Supplier { Id = 9101, Name = "Kwanza Industrial", TaxId = "5417000101", PortalCode = "ZZK1" };
        var luanda = new Supplier { Id = 9102, Name = "Luanda Suprimentos", TaxId = "5417000102", PortalCode = "ZZL1" };
        ctx.Suppliers.AddRange(kwanza, luanda);

        var request = new Request
        {
            Id = Guid.NewGuid(), RequestNumber = "REQ-TEST-001", Title = "ZZTEST notificação lote",
            StatusId = status.Id, Status = status, RequestTypeId = quotationType.Id, RequestType = quotationType,
            DepartmentId = 940, CompanyId = 940, CreatedAtUtc = DateTime.UtcNow, RequesterId = actor
        };
        ctx.Requests.Add(request);

        var lineIds = new Guid[4];
        for (var i = 0; i < 4; i++)
        {
            var li = new RequestLineItem { Id = Guid.NewGuid(), RequestId = request.Id, LineNumber = i + 1, Description = $"Item {i + 1}", Quantity = 1, UnitPrice = 0, TotalAmount = 0, IsDeleted = false, CreatedAtUtc = DateTime.UtcNow };
            ctx.RequestLineItems.Add(li);
            lineIds[i] = li.Id;
        }

        Guid[] AddQuotation(Supplier supplier, string doc, decimal[] totals)
        {
            var q = new Quotation { Id = Guid.NewGuid(), RequestId = request.Id, SupplierId = supplier.Id, SupplierNameSnapshot = supplier.Name, DocumentNumber = doc, DocumentDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), Currency = "AOA", SourceType = "MANUAL", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actor };
            ctx.Quotations.Add(q);
            var ids = new Guid[4];
            for (var i = 0; i < 4; i++)
            {
                var qi = new QuotationItem { Id = Guid.NewGuid(), QuotationId = q.Id, LineNumber = i + 1, Description = $"Item {i + 1} — {supplier.Name}", ReconciliationStatus = "MAPPED", MappedRequestLineItemId = lineIds[i], Quantity = 1, UnitPrice = totals[i], GrossSubtotal = totals[i], IvaRatePercent = 0, IvaAmount = 0, LineTotal = totals[i] };
                ctx.QuotationItems.Add(qi);
                ids[i] = qi.Id;
            }
            return ids;
        }
        var kwanzaItems = AddQuotation(kwanza, "FP-KWZ-001", KwanzaTotals);
        var luandaItems = AddQuotation(luanda, "FP-LDA-001", LuandaTotals);
        await ctx.SaveChangesAsync();
        return new Seed(options, request.Id, actor, lineIds, kwanzaItems, luandaItems);
    }

    private static ApprovalBatchController BuildController(ApplicationDbContext ctx, Guid actorId, IWorkflowNotificationOrchestrator orchestrator)
    {
        var routing = new Mock<IApprovalRoutingService>();
        routing.Setup(r => r.ResolveAreaManagersAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(new ApprovalRoutingResultDto { Managers = { new AreaManagerDto { UserId = actorId, FullName = "ZZ Manager" } } });
        routing.Setup(r => r.IsAreaManagerAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(false);
        var controller = new ApprovalBatchController(
            ctx, NullLogger<ApprovalBatchController>.Instance, new Mock<IRequestStatusSyncService>().Object,
            new GroupBuilderService(ctx), routing.Object, new QuotationItemEligibilityService(ctx),
            new BatchExtraItemDecisionService(ctx), new AdjustmentCycleService(ctx), orchestrator);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
            new(ClaimTypes.Role, RoleConstants.SystemAdministrator),
            new(ClaimTypes.Role, RoleConstants.FinalApprover)
        };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        return controller;
    }

    private static CreateApprovalBatchDto BatchDto(Seed s, params int[] lineIndexes)
    {
        var dto = new CreateApprovalBatchDto { Items = new List<BatchItemDto>() };
        foreach (var i in lineIndexes)
            dto.Items.Add(new BatchItemDto
            {
                RequestLineItemId = s.LineIds[i],
                Candidates = { new BatchCandidateInputDto { QuotationItemId = s.KwanzaItems[i] }, new BatchCandidateInputDto { QuotationItemId = s.LuandaItems[i] } }
            });
        return dto;
    }

    private static async Task<Guid> CreateBatchAsync(Seed s, Mock<IWorkflowNotificationOrchestrator> orchestrator, params int[] lineIndexes)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        var result = await BuildController(ctx, s.Actor, orchestrator.Object).CreateBatch(s.RequestId, BatchDto(s, lineIndexes));
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<ApprovalBatchDto>(ok.Value).Id;
    }

    private static async Task<BatchApprovalActionDto> ApproveDtoAsync(Seed s, Guid batchId)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        var items = await ctx.ApprovalBatchItems.AsNoTracking().Include(bi => bi.Candidates).Where(bi => bi.ApprovalBatchId == batchId).ToListAsync();
        var dto = new BatchApprovalActionDto { BudgetJustification = BudgetJustification, Comment = "Aprovado em teste.", Selections = new List<BatchWinnerSelectionDto>(), ItemAssignments = new Dictionary<Guid, ItemApprovalAssignmentDto>() };
        foreach (var bi in items)
        {
            // Cheapest candidate per line (no winner-selection justification needed for the cheapest)
            var index = Array.IndexOf(s.LineIds, bi.RequestLineItemId);
            var cheapest = KwanzaTotals[index] <= LuandaTotals[index] ? s.KwanzaItems[index] : s.LuandaItems[index];
            var candidate = bi.Candidates.Single(c => c.QuotationItemId == cheapest);
            dto.Selections.Add(new BatchWinnerSelectionDto { ApprovalBatchItemId = bi.Id, SelectedCandidateId = candidate.Id });
            dto.ItemAssignments[bi.RequestLineItemId] = new ItemApprovalAssignmentDto { PlantId = 1, CostCenterId = 1 };
        }
        return dto;
    }

    private static async Task<Guid> HistoryIdAsync(Seed s, string actionTaken, int occurrence = 1)
    {
        await using var ctx = new ApplicationDbContext(s.Options);
        var rows = await ctx.RequestStatusHistories.AsNoTracking()
            .Where(h => h.RequestId == s.RequestId && h.ActionTaken == actionTaken)
            .OrderBy(h => h.CreatedAtUtc).ThenBy(h => h.Id).ToListAsync();
        Assert.True(rows.Count >= occurrence, $"expected at least {occurrence} '{actionTaken}' history rows, found {rows.Count}");
        return rows[occurrence - 1].Id;
    }

    // ── Create ──────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Create_batch_emits_quotation_completed_once_with_batch_number_and_history_correlation()
    {
        var s = await SeedAsync();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        var batchId = await CreateBatchAsync(s, orchestrator, 0, 1, 2, 3);

        var createdHistoryId = await HistoryIdAsync(s, "BATCH_CREATED");
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e =>
            e.EventCode == WorkflowEventCodes.QuotationCompleted
            && e.RequestId == s.RequestId
            && e.BatchNumber == 1
            && e.CorrelationId == createdHistoryId
            && e.ActorUserId == s.Actor
            && e.RequesterId == s.Actor
            && e.DepartmentId == 940)), Times.Once);
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Once);
        Assert.NotEqual(Guid.Empty, batchId);
    }

    [Fact]
    public async Task Second_batch_on_the_same_request_emits_its_own_event_with_a_distinct_correlation()
    {
        var s = await SeedAsync();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        var emitted = new List<WorkflowEvent>();
        orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).Callback<WorkflowEvent>(emitted.Add).Returns(Task.CompletedTask);

        await CreateBatchAsync(s, orchestrator, 0, 1);
        await CreateBatchAsync(s, orchestrator, 2, 3);

        Assert.Equal(2, emitted.Count);
        Assert.All(emitted, e => Assert.Equal(WorkflowEventCodes.QuotationCompleted, e.EventCode));
        Assert.Equal(new[] { 1, 2 }, emitted.Select(e => e.BatchNumber!.Value).OrderBy(n => n).ToArray());
        Assert.NotEqual(emitted[0].CorrelationId, emitted[1].CorrelationId);
    }

    // ── Area decisions ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Area_approve_emits_area_approved_once_and_a_retry_is_refused_without_a_second_event()
    {
        var s = await SeedAsync();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        var batchId = await CreateBatchAsync(s, orchestrator, 0, 1, 2, 3);
        var dto = await ApproveDtoAsync(s, batchId);

        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchAreaApprove(s.RequestId, batchId, dto));

        var historyId = await HistoryIdAsync(s, "BATCH_AREA_APPROVED");
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e =>
            e.EventCode == WorkflowEventCodes.AreaApproved && e.BatchNumber == 1 && e.CorrelationId == historyId && e.Comment == "Aprovado em teste.")), Times.Once);

        // Retry of the committed action: stage guard refuses (batch is no longer WAITING_AREA_APPROVAL) → no new event
        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchAreaApprove(s.RequestId, batchId, dto));
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.AreaApproved)), Times.Once);
    }

    [Fact]
    public async Task Area_reject_emits_area_rejected_once_and_a_retry_is_refused_without_a_second_event()
    {
        var s = await SeedAsync();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        var batchId = await CreateBatchAsync(s, orchestrator, 0, 1, 2, 3);
        var dto = new BatchApprovalActionDto { Comment = RejectComment };

        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchAreaReject(s.RequestId, batchId, dto));

        var historyId = await HistoryIdAsync(s, "BATCH_AREA_REJECTED");
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e =>
            e.EventCode == WorkflowEventCodes.AreaRejected && e.BatchNumber == 1 && e.CorrelationId == historyId && e.Comment == RejectComment)), Times.Once);

        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchAreaReject(s.RequestId, batchId, dto));
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e => e.EventCode == WorkflowEventCodes.AreaRejected)), Times.Once);
    }

    // ── Final decisions ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Final_approve_emits_final_approved_once_after_area_approval()
    {
        var s = await SeedAsync();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        var batchId = await CreateBatchAsync(s, orchestrator, 0, 1, 2, 3);
        var approve = await ApproveDtoAsync(s, batchId);
        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchAreaApprove(s.RequestId, batchId, approve));

        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchFinalApprove(s.RequestId, batchId, new BatchApprovalActionDto { Comment = "Aprovação final de teste." }));

        var historyId = await HistoryIdAsync(s, "BATCH_FINAL_APPROVED");
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e =>
            e.EventCode == WorkflowEventCodes.FinalApproved && e.BatchNumber == 1 && e.CorrelationId == historyId)), Times.Once);

        // Full chain: create → area → final = exactly three events, one per committed transition
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Exactly(3));

        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<BadRequestObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchFinalApprove(s.RequestId, batchId, new BatchApprovalActionDto { Comment = "retry" }));
        orchestrator.Verify(o => o.EmitAsync(It.IsAny<WorkflowEvent>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Final_reject_emits_final_rejected_once_after_area_approval()
    {
        var s = await SeedAsync();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        var batchId = await CreateBatchAsync(s, orchestrator, 0, 1, 2, 3);
        var approve = await ApproveDtoAsync(s, batchId);
        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchAreaApprove(s.RequestId, batchId, approve));

        await using (var ctx = new ApplicationDbContext(s.Options))
            Assert.IsType<OkObjectResult>(await BuildController(ctx, s.Actor, orchestrator.Object).BatchFinalReject(s.RequestId, batchId, new BatchApprovalActionDto { Comment = RejectComment }));

        var historyId = await HistoryIdAsync(s, "BATCH_FINAL_REJECTED");
        orchestrator.Verify(o => o.EmitAsync(It.Is<WorkflowEvent>(e =>
            e.EventCode == WorkflowEventCodes.FinalRejected && e.BatchNumber == 1 && e.CorrelationId == historyId && e.Comment == RejectComment)), Times.Once);
    }

    // ── Failure isolation ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Notification_failure_does_not_fail_the_committed_action()
    {
        var s = await SeedAsync();
        var orchestrator = new Mock<IWorkflowNotificationOrchestrator>();
        orchestrator.Setup(o => o.EmitAsync(It.IsAny<WorkflowEvent>())).ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var batchId = await CreateBatchAsync(s, orchestrator, 0, 1, 2, 3); // asserts Ok

        await using var ctx = new ApplicationDbContext(s.Options);
        var batch = await ctx.ApprovalBatches.AsNoTracking().SingleAsync(b => b.Id == batchId);
        Assert.Equal(RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, batch.Status);
        Assert.Equal(1, await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == s.RequestId && h.ActionTaken == "BATCH_CREATED"));
    }

    // ── Template reference ──────────────────────────────────────────────────────────────────────
    [Fact]
    public void Request_reference_names_the_batch_only_for_batch_scoped_events()
    {
        var batchEvt = new WorkflowEvent { EventCode = "X", RequestId = Guid.NewGuid(), RequestNumber = "REQ-01/09/2026-346", TargetStatusCode = "S", ActionTaken = "A", ActorUserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), BatchNumber = 2 };
        var requestEvt = batchEvt with { BatchNumber = null };
        Assert.Equal("REQ-01/09/2026-346 (Lote #2)", WorkflowNotificationOrchestrator.FormatRequestRef(batchEvt));
        Assert.Equal("REQ-01/09/2026-346", WorkflowNotificationOrchestrator.FormatRequestRef(requestEvt));
    }
}
