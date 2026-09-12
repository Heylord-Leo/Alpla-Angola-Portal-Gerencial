using AlplaPortal.Application.DTOs.Approvals;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

// v2.244.0 Approval Center V2 — Phase 2. Service-level coverage: scope isolation (§7), filters,
// paging, sort (§24), legacy pre-batch rows (§22), multi-batch display parse (§23), timeline (§25).
public class ApprovalHistoryServiceTests
{
    // Status ids/codes
    private const int SWaitArea = 1, SWaitFinal = 2, SApproved = 3, SRejected = 4, SAreaAdj = 5;

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private sealed record Seed(Guid PayReqId, Guid QuotReqId, Guid OutOfScopeReqId, Guid ApproverAId, Guid ApproverBId);

    private static void AddHistory(ApplicationDbContext ctx, Guid requestId, Guid actorId, string action,
        int? prevStatusId, int newStatusId, string? comment, DateTime at)
    {
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = requestId, ActorUserId = actorId, ActionTaken = action,
            PreviousStatusId = prevStatusId, NewStatusId = newStatusId, Comment = comment, CreatedAtUtc = at,
        });
    }

    private static async Task<Seed> SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var ctx = new ApplicationDbContext(options);

        var approverA = new User { Id = Guid.NewGuid(), FullName = "Ana Aprovadora", Email = $"a-{Guid.NewGuid():N}@t.local" };
        var approverB = new User { Id = Guid.NewGuid(), FullName = "Bruno Final", Email = $"b-{Guid.NewGuid():N}@t.local" };
        var requester = new User { Id = Guid.NewGuid(), FullName = "Rui Solicitante", Email = $"r-{Guid.NewGuid():N}@t.local" };
        ctx.Users.AddRange(approverA, approverB, requester);

        ctx.RequestTypes.AddRange(
            new RequestType { Id = 1, Code = "QUOTATION", Name = "Cotação" },
            new RequestType { Id = 2, Code = "PAYMENT", Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = SWaitArea, Code = "WAITING_AREA_APPROVAL", Name = "Aguardando Área" },
            new RequestStatus { Id = SWaitFinal, Code = "WAITING_FINAL_APPROVAL", Name = "Aguardando Final" },
            new RequestStatus { Id = SApproved, Code = "APPROVED", Name = "Aprovado" },
            new RequestStatus { Id = SRejected, Code = "REJECTED", Name = "Rejeitado" },
            new RequestStatus { Id = SAreaAdj, Code = "AREA_ADJUSTMENT", Name = "Reajuste Área" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        // Required org lookups — Include of a required nav drops rows if the target is missing (InMemory).
        ctx.Set<Company>().Add(new Company { Id = 1, Name = "ALPLA Angola" });
        ctx.Set<Plant>().Add(new Plant { Id = 1, Name = "Planta Luanda", CompanyId = 1 });
        ctx.Set<Department>().Add(new Department { Id = 4, Name = "Produção" });

        Request MakeReq(int typeId, string num, string title) => new()
        {
            Id = Guid.NewGuid(), Title = title, RequestNumber = num,
            StatusId = SApproved, RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = requester.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            ApprovedTotalAmount = 1000m,
        };

        var pay = MakeReq(2, "REQ-PAY-1", "Pagamento X");
        var quot = MakeReq(1, "REQ-QUOT-1", "Cotação Y");
        var outScope = MakeReq(2, "REQ-OUT-9", "Fora de Escopo");
        ctx.Requests.AddRange(pay, quot, outScope);

        var t0 = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);

        // PAYMENT (scalar): area approve, final approve, final reject
        AddHistory(ctx, pay.Id, approverA.Id, "APPROVE", SWaitArea, SWaitFinal, "Aprovação da Área realizada.", t0);
        AddHistory(ctx, pay.Id, approverB.Id, "APPROVE", SWaitFinal, SApproved, "Aprovação Final realizada.", t0.AddDays(1));
        AddHistory(ctx, pay.Id, approverB.Id, "REJECT", SWaitFinal, SRejected, "Rejeitado por divergência.", t0.AddDays(2));

        // QUOTATION: batch area approve (Lote #1), batch final approve (Lote #2), context BATCH_CREATED,
        // and a LEGACY pre-batch scalar area approve (no ApprovalBatch row exists at all).
        AddHistory(ctx, quot.Id, approverA.Id, "BATCH_AREA_APPROVED", SWaitArea, SWaitArea, "Aprovação de Área do Lote #1.", t0.AddDays(3));
        AddHistory(ctx, quot.Id, approverB.Id, "BATCH_FINAL_APPROVED", SWaitFinal, SWaitFinal, "Aprovação Final do Lote #2. Montante: 500,00.", t0.AddDays(4));
        AddHistory(ctx, quot.Id, approverA.Id, "BATCH_CREATED", SWaitArea, SWaitArea, "Lote criado.", t0.AddDays(3).AddHours(-1));
        AddHistory(ctx, quot.Id, approverA.Id, "SUBMIT", null, SWaitArea, "Submetido.", t0.AddHours(-2));
        AddHistory(ctx, quot.Id, approverA.Id, "APPROVE", SWaitArea, SWaitFinal, "Aprovação da Área (legado).", t0.AddDays(5));

        // OUT OF SCOPE payment approval (must never appear when not in scopedIds)
        AddHistory(ctx, outScope.Id, approverA.Id, "APPROVE", SWaitFinal, SApproved, "Fora de escopo.", t0.AddDays(6));

        await ctx.SaveChangesAsync();
        return new Seed(pay.Id, quot.Id, outScope.Id, approverA.Id, approverB.Id);
    }

    private static IReadOnlyCollection<Guid> InScope(Seed s) => new[] { s.PayReqId, s.QuotReqId };

    private static ApprovalHistoryFilter NoFilter() => new();

    [Fact]
    public async Task GetHistory_ReturnsOnlyDecisions_WithSummary_ExcludesContextAndOutOfScope()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var res = await svc.GetHistoryAsync(InScope(seed), NoFilter(), 1, 25, "dateDesc");

        // Decisions in scope: PAY(area approve, final approve, final reject) + QUOT(batch area, batch final, legacy area) = 6
        // BATCH_CREATED and SUBMIT are context (excluded); OUT-OF-SCOPE excluded.
        Assert.Equal(6, res.TotalCount);
        Assert.DoesNotContain(res.Items, i => i.ActionTaken == "BATCH_CREATED" || i.ActionTaken == "SUBMIT");
        Assert.DoesNotContain(res.Items, i => i.RequestNumber == "REQ-OUT-9");
        // Approved: pay area, pay final, batch area, batch final, legacy area = 5. Rejected: 1.
        Assert.Equal(5, res.ApprovedCount);
        Assert.Equal(1, res.RejectedCount);
    }

    [Fact]
    public async Task GetHistory_ScopeIsolation_ExcludesOutOfScopeRequest()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        // Only the out-of-scope request id is "visible" → nothing from pay/quot should surface,
        // and the out-of-scope row is visible only because we explicitly scope to it here.
        var onlyOut = await svc.GetHistoryAsync(new[] { seed.OutOfScopeReqId }, NoFilter(), 1, 25, "dateDesc");
        Assert.Single(onlyOut.Items);
        Assert.Equal("REQ-OUT-9", onlyOut.Items.First().RequestNumber);

        // Empty scope → nothing.
        var none = await svc.GetHistoryAsync(Array.Empty<Guid>(), NoFilter(), 1, 25, "dateDesc");
        Assert.Equal(0, none.TotalCount);
    }

    [Fact]
    public async Task GetHistory_StageFilter_Area_OnlyAreaRows()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var res = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { Stage = "AREA" }, 1, 25, "dateDesc");
        Assert.All(res.Items, i => Assert.Equal("AREA", i.ApprovalLevel));
        // pay area approve + batch area approve + legacy area approve = 3
        Assert.Equal(3, res.TotalCount);
    }

    [Fact]
    public async Task GetHistory_DecisionFilter_Rejected()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var res = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { Decision = "REJECTED" }, 1, 25, "dateDesc");
        Assert.Single(res.Items);
        Assert.Equal("FINAL", res.Items.First().ApprovalLevel);
    }

    [Fact]
    public async Task GetHistory_RequestTypeFilter_Payment()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var res = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { RequestType = "PAYMENT" }, 1, 25, "dateDesc");
        Assert.All(res.Items, i => Assert.Equal("PAYMENT", i.RequestTypeCode));
        Assert.Equal(3, res.TotalCount);
    }

    [Fact]
    public async Task GetHistory_ApproverFilter_And_SearchByRequestNumber()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var byApprover = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { ApproverId = seed.ApproverBId }, 1, 25, "dateDesc");
        Assert.All(byApprover.Items, i => Assert.Equal(seed.ApproverBId, i.ApproverUserId));

        var bySearch = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { Search = "req-quot" }, 1, 25, "dateDesc");
        Assert.All(bySearch.Items, i => Assert.Equal("REQ-QUOT-1", i.RequestNumber));
        Assert.True(bySearch.TotalCount >= 1);
    }

    [Fact]
    public async Task GetHistory_DateRange_Filters()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        // Only the very last two days of activity.
        var from = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        var res = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { DateFrom = from }, 1, 25, "dateDesc");
        Assert.All(res.Items, i => Assert.True(i.DecisionAtUtc >= from));
    }

    [Fact]
    public async Task GetHistory_Pagination_And_SortDirections()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var p1 = await svc.GetHistoryAsync(InScope(seed), NoFilter(), 1, 2, "dateDesc");
        Assert.Equal(2, p1.Items.Count());
        Assert.Equal(6, p1.TotalCount);
        Assert.Equal(3, p1.TotalPages);

        var asc = await svc.GetHistoryAsync(InScope(seed), NoFilter(), 1, 25, "dateAsc");
        var desc = await svc.GetHistoryAsync(InScope(seed), NoFilter(), 1, 25, "dateDesc");
        Assert.True(asc.Items.First().DecisionAtUtc <= asc.Items.Last().DecisionAtUtc);
        Assert.True(desc.Items.First().DecisionAtUtc >= desc.Items.Last().DecisionAtUtc);
    }

    [Fact]
    public async Task GetHistory_Legacy_ScalarQuotation_AppearsWithoutBatch()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var res = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { RequestType = "QUOTATION" }, 1, 25, "dateDesc");
        // The legacy scalar APPROVE on the QUOTATION request must be present (no ApprovalBatch seeded).
        Assert.Contains(res.Items, i => i.ActionTaken == "APPROVE" && i.ApprovalLevel == "AREA" && i.Comment!.Contains("legado"));
    }

    [Fact]
    public async Task GetHistory_MultiBatch_ParsesLoteForDisplay_NoBatchIdentity()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var res = await svc.GetHistoryAsync(InScope(seed), new ApprovalHistoryFilter { RequestType = "QUOTATION" }, 1, 25, "dateDesc");
        var area = res.Items.First(i => i.ActionTaken == "BATCH_AREA_APPROVED");
        var final = res.Items.First(i => i.ActionTaken == "BATCH_FINAL_APPROVED");
        Assert.Equal(1, area.BatchNumber);
        Assert.Equal(2, final.BatchNumber);
        // The DTO exposes NO ApprovalBatchId — parsing is display-only (compile-time guarantee).
    }

    [Fact]
    public async Task Export_UsesSameFilterAndScope_AsTable()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var filter = new ApprovalHistoryFilter { Stage = "FINAL" };
        var table = await svc.GetHistoryAsync(InScope(seed), filter, 1, 25, "dateDesc");
        var export = await svc.GetExportRowsAsync(InScope(seed), filter, "dateDesc");
        Assert.Equal(table.TotalCount, export.Count);
        Assert.All(export, r => Assert.Equal("FINAL", r.ApprovalLevel));
    }

    [Fact]
    public async Task Timeline_ChronologicalOrder_ActorComments_Status_ContextAndDecisionsTogether()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalHistoryService(ctx);

        var tl = await svc.GetTimelineAsync(seed.QuotReqId);

        // Ascending by time.
        for (int i = 1; i < tl.Count; i++)
            Assert.True(tl[i].CreatedAtUtc >= tl[i - 1].CreatedAtUtc);

        // Includes both context (SUBMIT / BATCH_CREATED) and decisions (batch approvals + legacy scalar).
        Assert.Contains(tl, e => e.ActionTaken == "SUBMIT" && !e.IsDecision);
        Assert.Contains(tl, e => e.ActionTaken == "BATCH_CREATED" && !e.IsDecision);
        Assert.Contains(tl, e => e.ActionTaken == "BATCH_FINAL_APPROVED" && e.IsDecision && e.ApprovalLevel == "FINAL");
        Assert.Contains(tl, e => e.ActionTaken == "APPROVE" && e.IsDecision);

        // Actor + comment + status retained.
        var finalEvt = tl.First(e => e.ActionTaken == "BATCH_FINAL_APPROVED");
        Assert.Equal(seed.ApproverBId, finalEvt.ActorUserId);
        Assert.Equal("Bruno Final", finalEvt.ActorName);
        Assert.False(string.IsNullOrEmpty(finalEvt.Comment));
        Assert.Equal(2, finalEvt.BatchNumber);
    }
}
