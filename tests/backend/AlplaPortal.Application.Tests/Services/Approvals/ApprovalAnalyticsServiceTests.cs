using AlplaPortal.Application.DTOs.Approvals;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

// v2.244.0 Phase 3 — analytics aggregation over InMemory data (§29 J,O,P,Q,R,S,T).
public class ApprovalAnalyticsServiceTests
{
    private const int SWaitArea = 1, SWaitFinal = 2, SApproved = 3, SRejected = 4;

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    private sealed record Seed(Guid PayReqId, Guid QuotReqId, Guid OutReqId, Guid AreaApprover, Guid FinalApprover);

    private static void H(ApplicationDbContext ctx, Guid req, Guid actor, string action, int? prev, int @new, string? comment, DateTime at) =>
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        { Id = Guid.NewGuid(), RequestId = req, ActorUserId = actor, ActionTaken = action, PreviousStatusId = prev, NewStatusId = @new, Comment = comment, CreatedAtUtc = at });

    private static async Task<Seed> SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var ctx = new ApplicationDbContext(options);
        var area = new User { Id = Guid.NewGuid(), FullName = "Ana Área", Email = $"a-{Guid.NewGuid():N}@t.local" };
        var final = new User { Id = Guid.NewGuid(), FullName = "Bruno Final", Email = $"b-{Guid.NewGuid():N}@t.local" };
        var requester = new User { Id = Guid.NewGuid(), FullName = "Rui", Email = $"r-{Guid.NewGuid():N}@t.local" };
        ctx.Users.AddRange(area, final, requester);
        ctx.RequestTypes.AddRange(new RequestType { Id = 1, Code = "QUOTATION", Name = "Cotação" }, new RequestType { Id = 2, Code = "PAYMENT", Name = "Pag" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = SWaitArea, Code = "WAITING_AREA_APPROVAL", Name = "A" },
            new RequestStatus { Id = SWaitFinal, Code = "WAITING_FINAL_APPROVAL", Name = "F" },
            new RequestStatus { Id = SApproved, Code = "APPROVED", Name = "OK" },
            new RequestStatus { Id = SRejected, Code = "REJECTED", Name = "NO" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        ctx.Set<Company>().Add(new Company { Id = 1, Name = "ALPLA" });
        ctx.Set<Plant>().Add(new Plant { Id = 1, Name = "Luanda", CompanyId = 1 });
        ctx.Set<Department>().Add(new Department { Id = 4, Name = "Produção" });

        Request R(int type, string num) => new() { Id = Guid.NewGuid(), Title = num, RequestNumber = num, StatusId = SApproved, RequestTypeId = type, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1, RequesterId = requester.Id, CreatedAtUtc = DateTime.UtcNow.AddDays(-40) };
        var pay = R(2, "REQ-PAY"); var quot = R(1, "REQ-QUOT"); var outr = R(2, "REQ-OUT");
        ctx.Requests.AddRange(pay, quot, outr);

        var pt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        // PAYMENT scalar full flow: area 2h, final 3h (entry+2h→+5h), total 5h.
        H(ctx, pay.Id, requester.Id, "SUBMIT", null, SWaitArea, "Submetido.", pt);
        H(ctx, pay.Id, area.Id, "APPROVE", SWaitArea, SWaitFinal, "Aprovação da Área.", pt.AddHours(2));
        H(ctx, pay.Id, final.Id, "APPROVE", SWaitFinal, SApproved, "Aprovação Final.", pt.AddHours(5));

        var qt = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc);
        // QUOTATION single-batch: area 1h, final 2h, total 3h (request-level).
        H(ctx, quot.Id, area.Id, "BATCH_CREATED", SWaitArea, SWaitArea, "Lote #1 criado.", qt);
        H(ctx, quot.Id, area.Id, "BATCH_AREA_APPROVED", SWaitArea, SWaitArea, "Aprovação de Área do Lote #1.", qt.AddHours(1));
        H(ctx, quot.Id, final.Id, "BATCH_FINAL_APPROVED", SWaitFinal, SWaitFinal, "Aprovação Final do Lote #1.", qt.AddHours(3));

        // OUT OF SCOPE payment (must never appear).
        H(ctx, outr.Id, requester.Id, "SUBMIT", null, SWaitArea, "Submetido.", pt);
        H(ctx, outr.Id, area.Id, "APPROVE", SWaitArea, SWaitFinal, "Aprovação da Área.", pt.AddHours(1));

        await ctx.SaveChangesAsync();
        return new Seed(pay.Id, quot.Id, outr.Id, area.Id, final.Id);
    }

    private static IReadOnlyCollection<Guid> InScope(Seed s) => new[] { s.PayReqId, s.QuotReqId };

    [Fact]
    public async Task Analytics_Summary_Durations_Bottleneck_Types()
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalAnalyticsService(ctx);

        var a = await svc.GetAnalyticsAsync(InScope(seed), new ApprovalHistoryFilter(), "day");

        // T: decision mix — 4 decisions (2 pay approves + 2 batch approves), all approved.
        Assert.Equal(4, a.Summary.TotalDecisions);
        Assert.Equal(4, a.Summary.Approved);
        Assert.Equal(1.0, a.Summary.ApprovalRate);

        // Durations: area {7200, 3600}, final {10800, 7200}, total {18000, 10800}
        Assert.Equal(2, a.AreaDuration.SampleCount);
        Assert.Equal(2, a.FinalDuration.SampleCount);
        Assert.Equal(2, a.Duration.SampleCount);
        Assert.Equal(5400.0, a.AreaDuration.AverageSeconds);
        Assert.Equal(9000.0, a.FinalDuration.AverageSeconds);

        // Bottleneck: final (9000) > area (5400)
        Assert.Equal("FINAL", a.Bottleneck.DominantStage);

        // Type split
        Assert.Equal(2, a.RequestTypes.Quotation);
        Assert.Equal(2, a.RequestTypes.Payment);
    }

    [Fact]
    public async Task Analytics_ScopeIsolation_ExcludesOutOfScope() // O
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalAnalyticsService(ctx);

        var onlyOut = await svc.GetAnalyticsAsync(new[] { seed.OutReqId }, new ApprovalHistoryFilter(), "day");
        Assert.Equal(1, onlyOut.Summary.TotalDecisions); // just the out-of-scope area approve
        var empty = await svc.GetAnalyticsAsync(Array.Empty<Guid>(), new ApprovalHistoryFilter(), "day");
        Assert.Equal(0, empty.Summary.TotalDecisions);
    }

    [Fact]
    public async Task Analytics_DateFilter_NarrowsDecisions() // P
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalAnalyticsService(ctx);

        var from = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);
        var a = await svc.GetAnalyticsAsync(InScope(seed), new ApprovalHistoryFilter { DateFrom = from }, "day");
        // Only the QUOTATION (Aug 11) decisions survive.
        Assert.Equal(2, a.Summary.TotalDecisions);
        Assert.Equal(2, a.RequestTypes.Quotation);
        Assert.Equal(0, a.RequestTypes.Payment);
    }

    [Fact]
    public async Task Analytics_TypeFilter_Payment() // Q
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalAnalyticsService(ctx);

        var a = await svc.GetAnalyticsAsync(InScope(seed), new ApprovalHistoryFilter { RequestType = "PAYMENT" }, "day");
        Assert.Equal(2, a.Summary.TotalDecisions);
        Assert.Equal(2, a.RequestTypes.Payment);
        Assert.Equal(0, a.RequestTypes.Quotation);
    }

    [Fact]
    public async Task Analytics_ApproverMetrics_SpeedIsStageWait() // R + §12
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalAnalyticsService(ctx);

        var a = await svc.GetAnalyticsAsync(InScope(seed), new ApprovalHistoryFilter(), "day");
        var areaApprover = a.Approvers.Single(x => x.ApproverUserId == seed.AreaApprover);
        var finalApprover = a.Approvers.Single(x => x.ApproverUserId == seed.FinalApprover);

        // Area approver closed 2 area decisions; speed samples = area waits {7200, 3600} → avg 5400.
        Assert.Equal(2, areaApprover.DecisionCount);
        Assert.Equal(2, areaApprover.SpeedSampleCount);
        Assert.Equal(5400.0, areaApprover.AverageDecisionSeconds);

        // Final approver: final waits {10800, 7200} → avg 9000 (NOT time since request creation).
        Assert.Equal(2, finalApprover.DecisionCount);
        Assert.Equal(9000.0, finalApprover.AverageDecisionSeconds);
    }

    [Fact]
    public async Task Analytics_Trend_Bucketing() // S
    {
        var options = NewDbOptions();
        var seed = await SeedAsync(options);
        await using var ctx = new ApplicationDbContext(options);
        var svc = new ApprovalAnalyticsService(ctx);

        var a = await svc.GetAnalyticsAsync(InScope(seed), new ApprovalHistoryFilter(), "day");
        // Two decision days: 2026-08-10 (2 payment) and 2026-08-11 (2 quotation).
        Assert.Equal(2, a.Trend.Count);
        Assert.Equal("2026-08-10", a.Trend[0].Bucket);
        Assert.Equal(2, a.Trend[0].Decisions);
        Assert.Equal("2026-08-11", a.Trend[1].Bucket);

        var monthly = await svc.GetAnalyticsAsync(InScope(seed), new ApprovalHistoryFilter(), "month");
        Assert.Single(monthly.Trend);
        Assert.Equal("2026-08", monthly.Trend[0].Bucket);
        Assert.Equal(4, monthly.Trend[0].Decisions);
    }
}
