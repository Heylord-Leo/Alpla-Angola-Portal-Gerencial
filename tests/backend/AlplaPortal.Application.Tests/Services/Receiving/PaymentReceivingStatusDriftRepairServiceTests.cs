using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Receiving;

// v2.245.0 PAYMENT Receiving Status Drift — controlled legacy repair preview/apply/idempotency and the
// full negative matrix (§17–§20). Behavior mirrors the REQ-06/07/2026-023 read-only investigation.
public class PaymentReceivingStatusDriftRepairServiceTests
{
    private const int T_PAYMENT = 2, T_QUOTATION = 1;
    private const int S_PAYMENT_COMPLETED = 20, S_IN_FOLLOWUP = 1, S_CANCELLED = 30;
    private const string RepairAction = "RECEIVING_PAYMENT_GROUP_SYNC_REPAIR";

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId);

    /// <param name="typeCode">QUOTATION requests are out of scope entirely.</param>
    /// <param name="groupStatus">the operational group's status (PENDING = drifted).</param>
    /// <param name="requestStatusId">request scalar (advanced for the legacy cohort — IN_FOLLOWUP or PAYMENT_COMPLETED).</param>
    /// <param name="withPaymentCompletedHistory">seed a real PAYMENT_COMPLETED history event.</param>
    /// <param name="withDivergence">seed a PAYMENT_DIVERGENCE_DETECTED history event.</param>
    /// <param name="paymentLedgerStatus">null = no RequestPayment rows (legacy cohort); otherwise one row with this status.</param>
    /// <param name="secondGroupStatus">seed a second operational group → ambiguous.</param>
    private static async Task<Seed> SeedPayment(
        DbContextOptions<ApplicationDbContext> options,
        string typeCode = "PAYMENT",
        string groupStatus = "PENDING",
        int requestStatusId = S_PAYMENT_COMPLETED,
        bool withPaymentCompletedHistory = true,
        bool withDivergence = false,
        string? paymentLedgerStatus = null,
        string? secondGroupStatus = null)
    {
        await using var ctx = new ApplicationDbContext(options);
        var actor = new User { Id = Guid.NewGuid(), FullName = "Admin", Email = $"a-{Guid.NewGuid():N}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.AddRange(
            new RequestType { Id = T_QUOTATION, Code = "QUOTATION", Name = "Cotação" },
            new RequestType { Id = T_PAYMENT, Code = "PAYMENT", Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_IN_FOLLOWUP, Code = "IN_FOLLOWUP", Name = "Em acompanhamento" },
            new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = "PAYMENT_COMPLETED", Name = "Pagamento concluído" },
            new RequestStatus { Id = S_CANCELLED, Code = "CANCELLED", Name = "Cancelado" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });

        var reqId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var typeId = typeCode == "PAYMENT" ? T_PAYMENT : T_QUOTATION;

        var req = new Request
        {
            Id = reqId, RequestNumber = "REQ-06/07/2026-023", Title = "Serviço", StatusId = requestStatusId,
            RequestTypeId = typeId, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = actor.Id, CreatedAtUtc = DateTime.UtcNow, ApprovedTotalAmount = 6786374.40m,
        };
        ctx.Requests.Add(req);

        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = groupId, RequestId = reqId, Status = groupStatus, SupplierNameSnapshot = "Fornecedor",
            TotalAmount = 6786374.40m, PaymentConditionCode = "POST_PAID",
            CreatedByUserId = actor.Id, CreatedAtUtc = new DateTime(2026, 7, 20, 17, 25, 39, DateTimeKind.Utc),
        });
        if (secondGroupStatus != null)
            ctx.RequestPoGroups.Add(new RequestPoGroup
            {
                Id = Guid.NewGuid(), RequestId = reqId, Status = secondGroupStatus, SupplierNameSnapshot = "Fornecedor 2",
                CreatedByUserId = actor.Id, CreatedAtUtc = DateTime.UtcNow,
            });

        if (withPaymentCompletedHistory)
            ctx.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = reqId, ActorUserId = actor.Id, ActionTaken = "PAYMENT_COMPLETED",
                PreviousStatusId = S_IN_FOLLOWUP, NewStatusId = S_PAYMENT_COMPLETED, Comment = "Pagamento realizado.",
                CreatedAtUtc = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            });
        if (withDivergence)
            ctx.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = reqId, ActorUserId = actor.Id, ActionTaken = "PAYMENT_DIVERGENCE_DETECTED",
                PreviousStatusId = S_PAYMENT_COMPLETED, NewStatusId = S_PAYMENT_COMPLETED, Comment = "[SISTEMA] divergência.",
                CreatedAtUtc = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            });
        if (paymentLedgerStatus != null)
            ctx.RequestPayments.Add(new RequestPayment
            {
                RequestId = reqId, RequestPoGroupId = groupId, PaymentType = "FINAL_BALANCE", PaymentSequence = 1,
                PlannedAmount = 6786374.40m, PaymentStatus = paymentLedgerStatus, CurrencyCode = "AOA",
                CreatedByUserId = actor.Id, CreatedAtUtc = DateTime.UtcNow,
            });

        await ctx.SaveChangesAsync();
        return new Seed(reqId, groupId, actor.Id);
    }

    // ── §17 REQ-023 regression fixture ──────────────────────────────────────────

    [Fact]
    public async Task Preview_Req023_ReportsOneRepairable_WritesNothing()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);

        Assert.Equal("PREVIEW", res.Status);
        Assert.Equal(1, res.ScannedRequests);
        Assert.Equal(1, res.ScannedGroups);
        Assert.Equal(1, res.Eligible);
        Assert.Equal(1, res.WouldRepair);
        Assert.Equal(0, res.Repaired);
        Assert.Contains(res.Rows, r => r.Decision == "REPAIR" && r.RequestNumber == "REQ-06/07/2026-023" && r.PaymentHistoryFound);

        await using var verify = new ApplicationDbContext(options);
        var g = await verify.RequestPoGroups.FirstAsync(x => x.Id == seed.GroupId);
        Assert.Equal("PENDING", g.Status); // preview wrote nothing
        Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == RepairAction));
    }

    [Fact]
    public async Task Apply_Req023_PromotesGroup_TechnicalAuditOnly_NoBusinessEventFabricated()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options);
        await using (var ctx = new ApplicationDbContext(options))
        {
            var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: true, actorId: seed.ActorId, reason: "Incidente REQ-023");
            Assert.Equal("APPLIED", res.Status);
            Assert.Equal(1, res.Repaired);
            Assert.Contains(res.Rows, r => r.Decision == "REPAIRED");
        }
        await using var verify = new ApplicationDbContext(options);
        var g = await verify.RequestPoGroups.FirstAsync(x => x.Id == seed.GroupId);
        Assert.Equal("PAYMENT_COMPLETED", g.Status);                       // group promoted

        var req = await verify.Requests.FirstAsync(r => r.Id == seed.RequestId);
        Assert.Equal(S_PAYMENT_COMPLETED, req.StatusId);                   // request scalar unchanged

        Assert.True(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == RepairAction)); // technical audit
        // Exactly ONE new PAYMENT_COMPLETED history event (the pre-existing one) — none fabricated by the repair.
        Assert.Equal(1, await verify.RequestStatusHistories.CountAsync(h => h.ActionTaken == "PAYMENT_COMPLETED"));
        Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "CONFIRM_RECEIVING"));
        Assert.Empty(verify.RequestPayments); // no RequestPayment fabricated
    }

    [Fact]
    public async Task Apply_IsIdempotent_SecondPreviewReportsAlreadyHealthy_NoDuplicateAudit()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options);
        await using (var ctx = new ApplicationDbContext(options))
            await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: true, actorId: seed.ActorId, reason: "first");

        await using (var ctx = new ApplicationDbContext(options))
        {
            var preview = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
            Assert.Equal(0, preview.WouldRepair);
            Assert.Equal(1, preview.AlreadyHealthy);
            Assert.Contains(preview.Rows, r => r.Decision == "ALREADY_HEALTHY");
        }
        await using (var ctx = new ApplicationDbContext(options))
        {
            await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: true, actorId: seed.ActorId, reason: "second");
        }
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(1, await verify.RequestStatusHistories.CountAsync(h => h.ActionTaken == RepairAction)); // no duplicate audit
    }

    // ── §18 divergence never changes eligibility ────────────────────────────────

    [Fact]
    public async Task Divergence_Present_StillEligible()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options, withDivergence: true);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(1, res.WouldRepair);
        Assert.Contains(res.Rows, r => r.Decision == "REPAIR" && r.PaymentDivergencePresent);
    }

    [Fact]
    public async Task Divergence_Absent_StillEligible()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options, withDivergence: false);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(1, res.WouldRepair);
        Assert.Contains(res.Rows, r => r.Decision == "REPAIR" && !r.PaymentDivergencePresent);
    }

    // ── §19 negative repair cases ───────────────────────────────────────────────

    [Fact]
    public async Task NoPaymentCompletedHistory_Refused()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options, requestStatusId: S_IN_FOLLOWUP, withPaymentCompletedHistory: false);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(0, res.WouldRepair);
        Assert.Equal(1, res.Refused);
        Assert.Contains(res.Rows, r => r.Decision == "REFUSED");
    }

    [Fact]
    public async Task RequestCancelled_Refused_GroupUntouched()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options, requestStatusId: S_CANCELLED);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: true, actorId: seed.ActorId, reason: "x");
        Assert.Equal(0, res.Repaired);
        Assert.Equal(1, res.Refused);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal("PENDING", (await verify.RequestPoGroups.FirstAsync(g => g.Id == seed.GroupId)).Status);
    }

    [Fact]
    public async Task GroupNotPending_NotScanned_Untouched()
    {
        var options = NewDbOptions();
        // Healthy PAYMENT request: group already PAYMENT_COMPLETED, no prior repair audit → outside population.
        var seed = await SeedPayment(options, groupStatus: "PAYMENT_COMPLETED");
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(0, res.ScannedRequests);
        Assert.Equal(0, res.WouldRepair);
    }

    [Fact]
    public async Task MultipleOperationalGroups_Ambiguous()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options, secondGroupStatus: "PAYMENT_COMPLETED");
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(0, res.WouldRepair);
        Assert.Equal(1, res.Ambiguous);
        Assert.Contains(res.Rows, r => r.Decision == "AMBIGUOUS");
    }

    [Fact]
    public async Task ContradictoryPaymentLedger_Conflicting()
    {
        var options = NewDbOptions();
        // Ledger rows exist but NONE is COMPLETED → contradicts completion.
        var seed = await SeedPayment(options, paymentLedgerStatus: "SCHEDULED");
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(0, res.WouldRepair);
        Assert.Equal(1, res.Conflicting);
        Assert.Contains(res.Rows, r => r.Decision == "CONFLICTING" && r.RequestPaymentRows == 1);
    }

    [Fact]
    public async Task CompletedPaymentLedger_DoesNotContradict_Eligible()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options, paymentLedgerStatus: "COMPLETED");
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(1, res.WouldRepair);
        Assert.Contains(res.Rows, r => r.Decision == "REPAIR" && r.RequestPaymentRows == 1);
    }

    [Fact]
    public async Task NonPaymentRequest_OutOfScope_Untouched()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options, typeCode: "QUOTATION");
        await using var ctx = new ApplicationDbContext(options);
        var res = await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(0, res.ScannedRequests);
        Assert.Equal(0, res.WouldRepair);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal("PENDING", (await verify.RequestPoGroups.FirstAsync(g => g.Id == seed.GroupId)).Status);
    }

    // ── §20 apply safety: preview never writes ──────────────────────────────────

    [Fact]
    public async Task Preview_NeverWrites()
    {
        var options = NewDbOptions();
        var seed = await SeedPayment(options);
        await using (var ctx = new ApplicationDbContext(options))
            await new PaymentReceivingStatusDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal("PENDING", (await verify.RequestPoGroups.FirstAsync(g => g.Id == seed.GroupId)).Status);
        Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == RepairAction));
    }
}
