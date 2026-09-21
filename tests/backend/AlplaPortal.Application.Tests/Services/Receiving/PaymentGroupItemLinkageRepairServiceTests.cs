using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Receiving;

// v2.245.4 — atomic linkage + fail-closed status reconciliation for PAYMENT groups whose items were never
// linked (RequestPoGroupId = NULL). Strict confirmation correlation; canonical scalar aggregation.
public class PaymentGroupItemLinkageRepairServiceTests
{
    private const int T_PAYMENT = 2;
    private const int S_PAYMENT_COMPLETED = 14, S_WAITING_RECEIPT = 16, S_IN_FOLLOWUP = 18, S_COMPLETED = 17, S_CANCELLED = 30;
    private const int LS_RECEIVED = 91, LS_PENDING = 93;
    private const string Repair = PaymentGroupItemLinkageRepairService.RepairActionCode;

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    private static IStatusAggregationService RealAggregator(ApplicationDbContext ctx) =>
        new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance);

    private static string Tag(Guid g) => "GroupId: " + g.ToString().Substring(0, 8);

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid ActorId, Guid[] ItemIds);

    private sealed class SeedOptions
    {
        public string GroupStatus = "WAITING_RECEIPT";
        public int RequestStatusId = S_WAITING_RECEIPT;
        public int Items = 2;
        public bool PaymentCompletedHistory = true;
        public bool FinalBalanceCompletedRow = true;
        public bool AdvanceRow = false;
        /// <summary>null = no move; "TAG" = tagged with this group; "UNTAGGED" = no tag; "OTHER" = another group's tag</summary>
        public string? Move = "TAG";
        /// <summary>null = none; "TAG" | "UNTAGGED" | "OTHER" | "BEFORE_GROUP" (untagged, dated before group creation)</summary>
        public string? Confirm = null;
        public bool OpReceiptStampForGroup = false;
        public bool SecondActiveGroup = false;
        public bool CancelledGroup = false;
        public Guid? ForeignLinkFirstItemTo = null;
    }

    private static async Task<Seed> SeedAsync(DbContextOptions<ApplicationDbContext> options, SeedOptions o)
    {
        await using var ctx = new ApplicationDbContext(options);
        var actor = new User { Id = Guid.NewGuid(), FullName = "Admin", Email = $"a-{Guid.NewGuid():N}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = T_PAYMENT, Code = "PAYMENT", Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = "PAYMENT_COMPLETED", Name = "Pagamento Realizado" },
            new RequestStatus { Id = S_WAITING_RECEIPT, Code = "WAITING_RECEIPT", Name = "Aguardando Recibo" },
            new RequestStatus { Id = S_IN_FOLLOWUP, Code = "IN_FOLLOWUP", Name = "Em Acompanhamento" },
            new RequestStatus { Id = S_COMPLETED, Code = "COMPLETED", Name = "Finalizado" },
            new RequestStatus { Id = S_CANCELLED, Code = "CANCELLED", Name = "Cancelado" });
        ctx.LineItemStatuses.AddRange(
            new LineItemStatus { Id = LS_RECEIVED, Code = "RECEIVED", Name = "Recebido" },
            new LineItemStatus { Id = LS_PENDING, Code = "PENDING", Name = "Pendente" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });

        var reqId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var groupCreated = new DateTime(2026, 8, 6, 10, 35, 31, DateTimeKind.Utc);
        ctx.Requests.Add(new Request
        {
            Id = reqId, RequestNumber = "ZZTEST-LINK-" + Guid.NewGuid().ToString("N")[..6], Title = "link",
            StatusId = o.RequestStatusId, RequestTypeId = T_PAYMENT, DepartmentId = 4, CompanyId = 1, PlantId = 1,
            CurrencyId = 1, RequesterId = actor.Id, CreatedAtUtc = groupCreated.AddDays(-2), SelectedQuotationId = null,
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = groupId, RequestId = reqId, Status = o.GroupStatus, SupplierNameSnapshot = "IP WORLD", SupplierId = 123,
            TotalAmount = 100m, PaymentConditionCode = "POST_PAID", CreatedByUserId = actor.Id, CreatedAtUtc = groupCreated,
        });
        if (o.SecondActiveGroup)
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = reqId, Status = "PAYMENT_COMPLETED", SupplierNameSnapshot = "Other", CreatedByUserId = actor.Id, CreatedAtUtc = groupCreated });
        if (o.CancelledGroup)
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = reqId, Status = "CANCELLED", SupplierNameSnapshot = "Dead", CreatedByUserId = actor.Id, CreatedAtUtc = groupCreated.AddDays(-1) });

        var itemIds = new Guid[o.Items];
        for (var i = 0; i < o.Items; i++)
        {
            itemIds[i] = Guid.NewGuid();
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = itemIds[i], RequestId = reqId, RequestPoGroupId = i == 0 ? o.ForeignLinkFirstItemTo : null,
                LineNumber = i + 1, Description = $"item {i + 1}", Quantity = 1, ReceivedQuantity = 0,
                LineItemStatusId = LS_PENDING, IsDeleted = false,
            });
        }

        void H(string action, int prev, int next, DateTime at, string? comment = null, string? key = null) =>
            ctx.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = reqId, ActorUserId = actor.Id, ActionTaken = action,
                PreviousStatusId = prev, NewStatusId = next, Comment = comment, IdempotencyKey = key, CreatedAtUtc = at,
            });

        if (o.PaymentCompletedHistory)
            H("PAYMENT_COMPLETED", S_PAYMENT_COMPLETED, S_PAYMENT_COMPLETED, groupCreated.AddDays(5), "[IP WORLD | Montante: 100] Pagamento realizado.");
        if (o.Move != null)
            H("MOVE_TO_RECEIPT", S_PAYMENT_COMPLETED, S_WAITING_RECEIPT, groupCreated.AddDays(10),
                o.Move switch
                {
                    "TAG" => $"[Grupo P.O.: IP WORLD | {Tag(groupId)}] Pedido movido para aguardando recibo.",
                    "OTHER" => "[Grupo P.O.: Other | GroupId: deadbeef] Pedido movido para aguardando recibo.",
                    _ => "Pedido movido para aguardando recibo.",
                });
        if (o.Confirm != null)
            H("CONFIRM_RECEIVING", S_PAYMENT_COMPLETED, S_WAITING_RECEIPT,
                o.Confirm == "BEFORE_GROUP" ? groupCreated.AddDays(-1) : groupCreated.AddDays(12),
                o.Confirm switch
                {
                    "TAG" => $"[Grupo P.O.: IP WORLD | {Tag(groupId)}] Atesto que os bens foram recebidos.",
                    "OTHER" => "[Grupo P.O.: Other | GroupId: deadbeef] Atesto que os bens foram recebidos.",
                    _ => "Atesto que os bens foram recebidos.",
                });
        if (o.OpReceiptStampForGroup)
            H("OPERATIONAL_RECEIPT_COMPLETED", S_WAITING_RECEIPT, S_WAITING_RECEIPT, groupCreated.AddDays(12),
                "Recebimento operacional concluído.", AlplaPortal.Domain.Services.PostPaymentIdempotencyKeys.OperationalReceiptCompleted(groupId));

        if (o.FinalBalanceCompletedRow)
            ctx.RequestPayments.Add(new RequestPayment
            {
                RequestId = reqId, RequestPoGroupId = groupId, PaymentType = "FINAL_BALANCE", PaymentSequence = 1,
                PlannedAmount = 100m, ActualPaidAmount = 100m, PaymentStatus = "COMPLETED", CurrencyCode = "AOA",
                CreatedByUserId = actor.Id, CreatedAtUtc = groupCreated.AddDays(5),
            });
        if (o.AdvanceRow)
            ctx.RequestPayments.Add(new RequestPayment
            {
                RequestId = reqId, RequestPoGroupId = groupId, PaymentType = "ADVANCE", PaymentSequence = 1,
                PlannedAmount = 50m, PaymentStatus = "COMPLETED", CurrencyCode = "AOA",
                CreatedByUserId = actor.Id, CreatedAtUtc = groupCreated.AddDays(1),
            });

        await ctx.SaveChangesAsync();
        return new Seed(reqId, groupId, actor.Id, itemIds);
    }

    private static async Task<(RequestPoGroup group, RequestLineItem[] items, Request req)> StateAsync(DbContextOptions<ApplicationDbContext> options, Seed s)
    {
        await using var ctx = new ApplicationDbContext(options);
        var group = await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId);
        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == s.RequestId).OrderBy(li => li.LineNumber).ToArrayAsync();
        var req = await ctx.Requests.Include(r => r.Status).AsNoTracking().SingleAsync(r => r.Id == s.RequestId);
        return (group, items, req);
    }

    private static async Task<int> CountAsync(DbContextOptions<ApplicationDbContext> options, Guid requestId, string action)
    {
        await using var ctx = new ApplicationDbContext(options);
        return await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == requestId && h.ActionTaken == action);
    }

    // ── 1. Premature WAITING_RECEIPT → linked + demoted, scalar via canonical aggregator, nothing fabricated ──
    [Fact]
    public async Task PrematureWaitingReceipt_NoConfirm_LinkedAndDemoted_ScalarReconciled_NothingFabricated()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions()); // WAITING_RECEIPT, tagged MOVE from PAYMENT_COMPLETED, payment done, no confirm

        await using (var ctx = new ApplicationDbContext(options))
        {
            var preview = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(false, s.ActorId, null);
            Assert.Equal(1, preview.WouldRepair);
            Assert.Contains(preview.Rows, r => r.Decision == "REPAIR_LINK_AND_DEMOTE" && r.TargetGroupStatus == "PAYMENT_COMPLETED"
                                              && r.ConfirmEvidence == "NONE" && r.MoveEvidence && r.PaymentEvidence);
        }
        var (g0, items0, req0) = await StateAsync(options, s);
        Assert.All(items0, li => Assert.Null(li.RequestPoGroupId)); // preview wrote nothing
        Assert.Equal("WAITING_RECEIPT", g0.Status);

        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "TEST reason");
            Assert.Equal(1, applied.Repaired);
            Assert.Equal(0, applied.Errors);
            Assert.Contains(applied.Rows, r => r.Decision == "REPAIRED_LINK_AND_DEMOTE");
        }

        var (g, items, req) = await StateAsync(options, s);
        Assert.All(items, li => Assert.Equal(s.GroupId, li.RequestPoGroupId));       // linked
        Assert.Equal("PAYMENT_COMPLETED", g.Status);                                   // demoted
        Assert.Null(g.OperationalReceiptCompletedAtUtc);                               // stamp untouched
        Assert.All(items, li => { Assert.Equal(0, li.ReceivedQuantity); Assert.Equal(LS_PENDING, li.LineItemStatusId); }); // quantities/statuses untouched
        Assert.Equal("PAYMENT_COMPLETED", req.Status!.Code);                           // scalar reconciled
        Assert.Equal(1, await CountAsync(options, s.RequestId, "STATUS_SYNC"));        // via the canonical aggregator
        Assert.Equal(1, await CountAsync(options, s.RequestId, Repair));               // one technical audit
        Assert.Equal(0, await CountAsync(options, s.RequestId, "CONFIRM_RECEIVING"));  // nothing fabricated
        Assert.Equal(0, await CountAsync(options, s.RequestId, "OPERATIONAL_RECEIPT_COMPLETED"));
        Assert.Equal(1, await CountAsync(options, s.RequestId, "PAYMENT_COMPLETED"));  // only the pre-existing one
    }

    // ── 2. Correlated confirmation preserves WAITING_RECEIPT ──
    [Theory]
    [InlineData("TAG", false)]       // tagged CONFIRM_RECEIVING after group creation
    [InlineData("UNTAGGED", false)]  // untagged, but the request only ever had this group
    [InlineData(null, true)]         // OPERATIONAL_RECEIPT_COMPLETED stamp with this group's key
    public async Task CorrelatedConfirmation_LinksItems_PreservesWaitingReceipt(string? confirm, bool opStamp)
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions { Confirm = confirm, OpReceiptStampForGroup = opStamp });
        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "r");
            Assert.Equal(1, applied.Repaired);
            Assert.Contains(applied.Rows, r => r.Decision == "REPAIRED_LINK" && r.ConfirmEvidence == "CORRELATED");
        }
        var (g, items, _) = await StateAsync(options, s);
        Assert.All(items, li => Assert.Equal(s.GroupId, li.RequestPoGroupId));
        Assert.Equal("WAITING_RECEIPT", g.Status); // preserved
    }

    // ── 3. Uncorrelatable confirmation → CONFLICTING, nothing touched, WAITING_RECEIPT NOT preserved-by-linking ──
    [Theory]
    [InlineData("OTHER")]         // tagged with a different group
    [InlineData("BEFORE_GROUP")]  // untagged but written before this group existed
    public async Task UncorrelatedConfirmation_Conflicting_NothingWritten(string confirm)
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions { Confirm = confirm });
        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "r");
            Assert.Equal(0, applied.Repaired);
            Assert.Equal(1, applied.Conflicting);
            Assert.Contains(applied.Rows, r => r.Decision == "CONFLICTING" && r.ConfirmEvidence == "UNCORRELATED");
        }
        var (g, items, req) = await StateAsync(options, s);
        Assert.All(items, li => Assert.Null(li.RequestPoGroupId)); // NOT linked
        Assert.Equal("WAITING_RECEIPT", g.Status);                    // untouched (neither preserved-by-repair nor demoted)
        Assert.Equal("WAITING_RECEIPT", req.Status!.Code);
        Assert.Equal(0, await CountAsync(options, s.RequestId, Repair));
    }

    // ── 4. Forced failure after linkage rolls everything back (linkage, status, aggregation, audit) ──
    [Fact]
    public async Task FailureDuringAggregation_RollsBack_NothingPersisted()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions());
        var throwing = new Mock<IStatusAggregationService>();
        throwing.Setup(a => a.AggregateRequestStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("forced"));

        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await new PaymentGroupItemLinkageRepairService(ctx, throwing.Object).RunAsync(true, s.ActorId, "r");
            Assert.Equal(0, applied.Repaired);
            Assert.Equal(1, applied.Errors);
            Assert.Contains(applied.Rows, r => r.Decision == "ERROR");
        }
        var (g, items, req) = await StateAsync(options, s);
        Assert.All(items, li => Assert.Null(li.RequestPoGroupId)); // linkage rolled back
        Assert.Equal("WAITING_RECEIPT", g.Status);                    // status rolled back
        Assert.Equal("WAITING_RECEIPT", req.Status!.Code);            // scalar untouched
        Assert.Equal(0, await CountAsync(options, s.RequestId, Repair)); // audit rolled back
        Assert.Equal(0, await CountAsync(options, s.RequestId, "STATUS_SYNC"));
    }

    // ── 5. Idempotent second APPLY ──
    [Fact]
    public async Task SecondApply_NoDuplicateChangesOrAudit_PreviewReportsHealthy()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions());
        await using (var ctx = new ApplicationDbContext(options))
            await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "first");
        await using (var ctx = new ApplicationDbContext(options))
        {
            var second = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "second");
            Assert.Equal(0, second.Repaired);
            Assert.Equal(1, second.AlreadyHealthy);
        }
        await using (var ctx = new ApplicationDbContext(options))
        {
            var preview = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(false, s.ActorId, null);
            Assert.Equal(0, preview.WouldRepair);
            Assert.Contains(preview.Rows, r => r.Decision == "ALREADY_HEALTHY");
        }
        Assert.Equal(1, await CountAsync(options, s.RequestId, Repair));        // no duplicate audit
        Assert.Equal(1, await CountAsync(options, s.RequestId, "STATUS_SYNC")); // no duplicate sync
    }

    // ── 6. Multi-group → AMBIGUOUS, untouched ──
    [Fact]
    public async Task MultipleActiveGroups_Ambiguous_Untouched()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions { SecondActiveGroup = true });
        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "r");
            Assert.Equal(1, applied.Ambiguous);
            Assert.Equal(0, applied.Repaired);
        }
        var (_, items, _) = await StateAsync(options, s);
        Assert.All(items, li => Assert.Null(li.RequestPoGroupId));
    }

    // ── 7. Foreign / mixed linkage → CONFLICTING ──
    [Fact]
    public async Task ForeignLinkage_Conflicting_Untouched()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions { ForeignLinkFirstItemTo = Guid.NewGuid() });
        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "r");
            Assert.Equal(1, applied.Conflicting);
            Assert.Equal(0, applied.Repaired);
        }
        var (g, items, _) = await StateAsync(options, s);
        Assert.Null(items[1].RequestPoGroupId);
        Assert.Equal("WAITING_RECEIPT", g.Status);
    }

    // ── 8. Advance-payment flow without confirmation → REFUSED (prior state not provable) ──
    [Fact]
    public async Task WaitingReceipt_AdvanceFlow_NoConfirm_Refused()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions { AdvanceRow = true });
        await using var ctx = new ApplicationDbContext(options);
        var preview = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(false, s.ActorId, null);
        Assert.Equal(1, preview.Refused);
        Assert.Equal(0, preview.WouldRepair);
    }

    // ── 9. Pre-confirmation group (PAYMENT_COMPLETED) → linkage only, status kept ──
    [Fact]
    public async Task PaymentCompletedGroup_LinkOnly_StatusKept()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions { GroupStatus = "PAYMENT_COMPLETED", RequestStatusId = S_PAYMENT_COMPLETED, Move = null });
        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(true, s.ActorId, "r");
            Assert.Contains(applied.Rows, r => r.Decision == "REPAIRED_LINK");
        }
        var (g, items, req) = await StateAsync(options, s);
        Assert.All(items, li => Assert.Equal(s.GroupId, li.RequestPoGroupId));
        Assert.Equal("PAYMENT_COMPLETED", g.Status);
        Assert.Equal("PAYMENT_COMPLETED", req.Status!.Code);
    }

    // ── 10. Terminal request never touched ──
    [Fact]
    public async Task CompletedRequest_Refused()
    {
        var options = NewDbOptions();
        var s = await SeedAsync(options, new SeedOptions { RequestStatusId = S_COMPLETED });
        await using var ctx = new ApplicationDbContext(options);
        var preview = await new PaymentGroupItemLinkageRepairService(ctx, RealAggregator(ctx)).RunAsync(false, s.ActorId, null);
        Assert.Equal(1, preview.Refused);
    }
}
