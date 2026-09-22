using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Receiving;

/// <summary>
/// v2.245.10 — the SINGLE-REQUEST scope of the payment-group-item-linkage repair. A population of several
/// eligible and non-eligible PAYMENT requests coexists in every test; the scoped run must inspect/mutate
/// exactly the supplied request and leave every other request state-for-state unchanged.
/// </summary>
public class PaymentGroupItemLinkageScopedRepairTests
{
    private const int T_PAYMENT = 2, T_QUOTATION = 1;
    private const int S_PAYMENT_COMPLETED = 14, S_WAITING_RECEIPT = 16, S_IN_FOLLOWUP = 18, S_COMPLETED = 17, S_CANCELLED = 30, S_REJECTED = 31;
    private const int LS_PENDING = 93;
    private const string Repair = PaymentGroupItemLinkageRepairService.RepairActionCode;

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    private static PaymentGroupItemLinkageRepairService Service(ApplicationDbContext ctx) =>
        new(ctx, new StatusAggregationService(ctx, NullLogger<StatusAggregationService>.Instance));

    private sealed record Seeded(Guid RequestId, Guid GroupId);

    private sealed class Opts
    {
        public int TypeId = T_PAYMENT;
        public int RequestStatusId = S_PAYMENT_COMPLETED;
        public string GroupStatus = "PAYMENT_COMPLETED";
        public int Items = 2;
        public bool SecondActiveGroup = false;
        public bool ForeignLinkFirstItem = false;
        public bool NoGroup = false;
    }

    private static async Task<Guid> SeedLookupsAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var ctx = new ApplicationDbContext(options);
        var actor = new User { Id = Guid.NewGuid(), FullName = "Admin", Email = $"a-{Guid.NewGuid():N}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.AddRange(
            new RequestType { Id = T_PAYMENT, Code = "PAYMENT", Name = "Pagamento" },
            new RequestType { Id = T_QUOTATION, Code = "QUOTATION", Name = "Cotação" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_PAYMENT_COMPLETED, Code = "PAYMENT_COMPLETED", Name = "Pagamento Realizado" },
            new RequestStatus { Id = S_WAITING_RECEIPT, Code = "WAITING_RECEIPT", Name = "Aguardando Recibo" },
            new RequestStatus { Id = S_IN_FOLLOWUP, Code = "IN_FOLLOWUP", Name = "Em Acompanhamento" },
            new RequestStatus { Id = S_COMPLETED, Code = "COMPLETED", Name = "Finalizado" },
            new RequestStatus { Id = S_CANCELLED, Code = "CANCELLED", Name = "Cancelado" },
            new RequestStatus { Id = S_REJECTED, Code = "REJECTED", Name = "Rejeitado" });
        ctx.LineItemStatuses.Add(new LineItemStatus { Id = LS_PENDING, Code = "PENDING", Name = "Pendente" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });
        await ctx.SaveChangesAsync();
        return actor.Id;
    }

    /// <summary>
    /// Default shape = the REQ-04/08/2026-209 class: single PAYMENT_COMPLETED group, request
    /// PAYMENT_COMPLETED, two active unlinked items, no receiving, no move, payment evidence, no advance.
    /// </summary>
    private static async Task<Seeded> AddRequestAsync(DbContextOptions<ApplicationDbContext> options, Guid actorId, Opts o, string number)
    {
        await using var ctx = new ApplicationDbContext(options);
        var reqId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var created = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        ctx.Requests.Add(new Request
        {
            Id = reqId, RequestNumber = number, Title = "scoped", StatusId = o.RequestStatusId, RequestTypeId = o.TypeId,
            DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1, RequesterId = actorId, CreatedAtUtc = created.AddDays(-2),
        });
        if (!o.NoGroup)
            ctx.RequestPoGroups.Add(new RequestPoGroup
            {
                Id = groupId, RequestId = reqId, Status = o.GroupStatus, SupplierNameSnapshot = "ZZTEST Supplier", SupplierId = 123,
                TotalAmount = 100m, PaymentConditionCode = "POST_PAID", CreatedByUserId = actorId, CreatedAtUtc = created,
            });
        if (o.SecondActiveGroup)
            ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = reqId, Status = "PAYMENT_COMPLETED", SupplierNameSnapshot = "Other", CreatedByUserId = actorId, CreatedAtUtc = created });
        for (var i = 0; i < o.Items; i++)
            ctx.RequestLineItems.Add(new RequestLineItem
            {
                Id = Guid.NewGuid(), RequestId = reqId, RequestPoGroupId = i == 0 && o.ForeignLinkFirstItem ? Guid.NewGuid() : null,
                LineNumber = i + 1, Description = $"item {i + 1}", Quantity = 1, ReceivedQuantity = 0, LineItemStatusId = LS_PENDING,
            });
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = reqId, ActorUserId = actorId, ActionTaken = "PAYMENT_COMPLETED",
            PreviousStatusId = S_PAYMENT_COMPLETED, NewStatusId = S_PAYMENT_COMPLETED, Comment = "[ZZTEST Supplier | Montante: 100] Pagamento realizado.",
            CreatedAtUtc = created.AddDays(5),
        });
        if (!o.NoGroup)
            ctx.RequestPayments.Add(new RequestPayment
            {
                RequestId = reqId, RequestPoGroupId = groupId, PaymentType = "FINAL_BALANCE", PaymentSequence = 1, PlannedAmount = 100m,
                ActualPaidAmount = 100m, PaymentStatus = "COMPLETED", CurrencyCode = "AOA", CreatedByUserId = actorId, CreatedAtUtc = created.AddDays(5),
            });
        await ctx.SaveChangesAsync();
        return new Seeded(reqId, groupId);
    }

    /// <summary>A full, order-independent state snapshot of ONE request (request, groups, items, payments, histories).</summary>
    private static async Task<string> SnapshotAsync(DbContextOptions<ApplicationDbContext> options, Guid requestId)
    {
        await using var ctx = new ApplicationDbContext(options);
        var req = await ctx.Requests.AsNoTracking().Where(r => r.Id == requestId)
            .Select(r => new { r.StatusId, r.UpdatedAtUtc, r.RequestNumber }).SingleAsync();
        var groups = await ctx.RequestPoGroups.AsNoTracking().Where(g => g.RequestId == requestId).OrderBy(g => g.Id)
            .Select(g => new { g.Id, g.Status, g.UpdatedAtUtc, g.UpdatedByUserId, g.OperationalReceiptCompletedAtUtc }).ToListAsync();
        var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == requestId).OrderBy(li => li.LineNumber)
            .Select(li => new { li.Id, li.RequestPoGroupId, li.ReceivedQuantity, li.LineItemStatusId, li.UpdatedAtUtc }).ToListAsync();
        var payments = await ctx.RequestPayments.AsNoTracking().Where(p => p.RequestId == requestId).OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.PaymentStatus, p.ActualPaidAmount }).ToListAsync();
        var histories = await ctx.RequestStatusHistories.AsNoTracking().Where(h => h.RequestId == requestId).OrderBy(h => h.CreatedAtUtc).ThenBy(h => h.Id)
            .Select(h => new { h.Id, h.ActionTaken, h.PreviousStatusId, h.NewStatusId, h.Comment, h.IdempotencyKey }).ToListAsync();
        return JsonSerializer.Serialize(new { req, groups, items, payments, histories });
    }

    private static async Task<int> CountAsync(DbContextOptions<ApplicationDbContext> options, Guid requestId, string action)
    {
        await using var ctx = new ApplicationDbContext(options);
        return await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == requestId && h.ActionTaken == action);
    }

    private static PaymentGroupItemLinkageRepairService.ScopedExpectation LinkFacts(Seeded s) => new(s.GroupId, "REPAIR_LINK");

    // ── 1. Scoped PREVIEW: only the supplied request, REQ-209-shaped facts, nothing written ──
    [Fact]
    public async Task ScopedPreview_InspectsOnlyTheSuppliedRequest_ReturnsReq209ShapedFacts_WritesNothing()
    {
        var options = NewDbOptions();
        var actor = await SeedLookupsAsync(options);
        var target = await AddRequestAsync(options, actor, new Opts(), "ZZTEST-TARGET");
        var other1 = await AddRequestAsync(options, actor, new Opts(), "ZZTEST-OTHER-1");
        var other2 = await AddRequestAsync(options, actor, new Opts { GroupStatus = "WAITING_RECEIPT", RequestStatusId = S_WAITING_RECEIPT }, "ZZTEST-OTHER-2");
        var before = new[] { await SnapshotAsync(options, target.RequestId), await SnapshotAsync(options, other1.RequestId), await SnapshotAsync(options, other2.RequestId) };

        await using (var ctx = new ApplicationDbContext(options))
        {
            // the global scan sees the whole population…
            var global = await Service(ctx).RunAsync(false, actor, null);
            Assert.Equal(3, global.ScannedRequests);

            // …the scoped one sees exactly the target
            var scoped = await Service(ctx).RunForRequestAsync(target.RequestId, apply: false, actor, null, expected: null);
            Assert.NotNull(scoped);
            Assert.Equal("PREVIEW", scoped!.Status);
            Assert.Equal(PaymentGroupItemLinkageRepairService.ScopeRequest, scoped.Scope);
            Assert.Equal(target.RequestId.ToString(), scoped.RequestId);
            Assert.Equal(1, scoped.ScannedRequests);
            Assert.Equal(1, scoped.WouldRepair);
            var row = Assert.Single(scoped.Rows);
            Assert.Equal("ZZTEST-TARGET", row.RequestNumber);
            Assert.Equal(target.RequestId.ToString(), row.RequestId);
            Assert.Equal(target.GroupId.ToString(), row.PoGroupId);
            Assert.Equal("PAYMENT_COMPLETED", row.RequestStatus);
            Assert.Equal("PAYMENT_COMPLETED", row.GroupStatus);
            Assert.Equal("PAYMENT_COMPLETED", row.TargetGroupStatus);
            Assert.Equal(2, row.ActiveItems);
            Assert.Equal(2, row.UnlinkedItems);
            Assert.Equal(0, row.ReceivedItems);
            Assert.Equal("NONE", row.ConfirmEvidence);
            Assert.False(row.MoveEvidence);
            Assert.True(row.PaymentEvidence);
            Assert.False(row.AdvanceEvidence);
            Assert.False(row.ReceiptPresent);
            Assert.Equal("REPAIR_LINK", row.Decision);
            Assert.DoesNotContain(scoped.Rows, r => r.RequestId == other1.RequestId.ToString() || r.RequestId == other2.RequestId.ToString());
        }

        Assert.Equal(before[0], await SnapshotAsync(options, target.RequestId));
        Assert.Equal(before[1], await SnapshotAsync(options, other1.RequestId));
        Assert.Equal(before[2], await SnapshotAsync(options, other2.RequestId));
    }

    // ── 2. Scoped APPLY mutates only the supplied request; other eligible requests stay state-for-state unchanged ──
    [Fact]
    public async Task ScopedApply_ChangesOnlyTheSuppliedRequest_OtherEligibleRequestsUnchanged()
    {
        var options = NewDbOptions();
        var actor = await SeedLookupsAsync(options);
        var target = await AddRequestAsync(options, actor, new Opts(), "ZZTEST-TARGET");
        var others = new List<Seeded>();
        for (var i = 0; i < 5; i++) others.Add(await AddRequestAsync(options, actor, new Opts(), $"ZZTEST-OTHER-{i}"));
        var othersBefore = new List<string>();
        foreach (var o in others) othersBefore.Add(await SnapshotAsync(options, o.RequestId));

        await using (var ctx = new ApplicationDbContext(options))
        {
            var applied = await Service(ctx).RunForRequestAsync(target.RequestId, apply: true, actor, "REQ-209 scoped repair", LinkFacts(target));
            Assert.NotNull(applied);
            Assert.Equal("APPLIED", applied!.Status);
            Assert.Equal(1, applied.ScannedRequests);
            Assert.Equal(1, applied.Repaired);
            Assert.Equal(0, applied.Errors);
            Assert.Equal("REPAIRED_LINK", Assert.Single(applied.Rows).Decision);
        }

        await using (var ctx = new ApplicationDbContext(options))
        {
            var items = await ctx.RequestLineItems.AsNoTracking().Where(li => li.RequestId == target.RequestId).ToListAsync();
            Assert.All(items, li => Assert.Equal(target.GroupId, li.RequestPoGroupId));
            Assert.Equal("PAYMENT_COMPLETED", (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == target.GroupId)).Status);
            Assert.Equal(S_PAYMENT_COMPLETED, (await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == target.RequestId)).StatusId);
        }
        Assert.Equal(1, await CountAsync(options, target.RequestId, Repair));
        Assert.Equal(0, await CountAsync(options, target.RequestId, "CONFIRM_RECEIVING"));

        for (var i = 0; i < others.Count; i++)
        {
            Assert.Equal(othersBefore[i], await SnapshotAsync(options, others[i].RequestId));   // byte-for-byte
            Assert.Equal(0, await CountAsync(options, others[i].RequestId, Repair));
            Assert.Equal(0, await CountAsync(options, others[i].RequestId, "STATUS_SYNC"));
        }

        // the untouched siblings are still eligible for a later, separately authorized run
        await using (var ctx = new ApplicationDbContext(options))
            Assert.Equal(5, (await Service(ctx).RunAsync(false, actor, null)).WouldRepair);
    }

    // ── 3. Fail-closed classes: unknown, non-PAYMENT, terminal request, ambiguous, conflicting, terminal group ──
    [Fact]
    public async Task Scoped_UnknownRequest_ReturnsNull_WritesNothing()
    {
        var options = NewDbOptions();
        var actor = await SeedLookupsAsync(options);
        var other = await AddRequestAsync(options, actor, new Opts(), "ZZTEST-OTHER");
        var before = await SnapshotAsync(options, other.RequestId);
        await using var ctx = new ApplicationDbContext(options);
        Assert.Null(await Service(ctx).RunForRequestAsync(Guid.NewGuid(), apply: false, actor, null, null));
        Assert.Null(await Service(ctx).RunForRequestAsync(Guid.NewGuid(), apply: true, actor, "r", new(Guid.NewGuid(), "REPAIR_LINK")));
        Assert.Equal(before, await SnapshotAsync(options, other.RequestId));
    }

    public static IEnumerable<object[]> FailClosedShapes()
    {
        yield return new object[] { "NON_PAYMENT", "REFUSED" };
        yield return new object[] { "CANCELLED", "REFUSED" };
        yield return new object[] { "REJECTED", "REFUSED" };
        yield return new object[] { "COMPLETED", "REFUSED" };
        yield return new object[] { "AMBIGUOUS", "AMBIGUOUS" };
        yield return new object[] { "CONFLICTING", "CONFLICTING" };
        yield return new object[] { "TERMINAL_GROUP", "REFUSED" };
        yield return new object[] { "NO_GROUP", "REFUSED" };
    }

    [Theory]
    [MemberData(nameof(FailClosedShapes))]
    public async Task Scoped_FailClosedShape_PreviewAndApplyWriteNothing(string shape, string expectedDecision)
    {
        var options = NewDbOptions();
        var actor = await SeedLookupsAsync(options);
        var opts = shape switch
        {
            "NON_PAYMENT" => new Opts { TypeId = T_QUOTATION },
            "CANCELLED" => new Opts { RequestStatusId = S_CANCELLED },
            "REJECTED" => new Opts { RequestStatusId = S_REJECTED },
            "COMPLETED" => new Opts { RequestStatusId = S_COMPLETED },
            "AMBIGUOUS" => new Opts { SecondActiveGroup = true },
            "CONFLICTING" => new Opts { ForeignLinkFirstItem = true },
            "TERMINAL_GROUP" => new Opts { GroupStatus = "COMPLETED" },
            "NO_GROUP" => new Opts { NoGroup = true },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var target = await AddRequestAsync(options, actor, opts, "ZZTEST-" + shape);
        var healthy = await AddRequestAsync(options, actor, new Opts(), "ZZTEST-ELIGIBLE-SIBLING");
        var targetBefore = await SnapshotAsync(options, target.RequestId);
        var siblingBefore = await SnapshotAsync(options, healthy.RequestId);

        await using (var ctx = new ApplicationDbContext(options))
        {
            var preview = await Service(ctx).RunForRequestAsync(target.RequestId, apply: false, actor, null, null);
            Assert.NotNull(preview);
            Assert.Equal(0, preview!.WouldRepair);
            Assert.Equal(expectedDecision, Assert.Single(preview.Rows).Decision);

            var apply = await Service(ctx).RunForRequestAsync(target.RequestId, apply: true, actor, "r", LinkFacts(target));
            Assert.NotNull(apply);
            Assert.Equal(0, apply!.Repaired);
            Assert.Equal(0, apply.Errors);
            Assert.Equal(expectedDecision, Assert.Single(apply.Rows).Decision);
        }

        Assert.Equal(targetBefore, await SnapshotAsync(options, target.RequestId));
        Assert.Equal(siblingBefore, await SnapshotAsync(options, healthy.RequestId));   // the eligible sibling is never touched either
    }

    // ── 4. Facts changed between PREVIEW and APPLY → fail closed ──
    [Fact]
    public async Task ScopedApply_FactsDifferFromPreview_Refused_WritesNothing()
    {
        var options = NewDbOptions();
        var actor = await SeedLookupsAsync(options);
        var target = await AddRequestAsync(options, actor, new Opts(), "ZZTEST-TARGET");
        var before = await SnapshotAsync(options, target.RequestId);

        await using (var ctx = new ApplicationDbContext(options))
        {
            var otherGroup = await Service(ctx).RunForRequestAsync(target.RequestId, apply: true, actor, "r", new(Guid.NewGuid(), "REPAIR_LINK"));
            Assert.Equal(1, otherGroup!.Refused);
            Assert.Equal(0, otherGroup.Repaired);
            var row = Assert.Single(otherGroup.Rows);
            Assert.Equal("REFUSED", row.Decision);
            Assert.Contains("divergem da pré-visualização", row.Reason);
            Assert.Contains("REPAIR_LINK", row.Reason);   // the live decision is reported, not the refusal itself

            var otherDecision = await Service(ctx).RunForRequestAsync(target.RequestId, apply: true, actor, "r", new(target.GroupId, "REPAIR_LINK_AND_DEMOTE"));
            Assert.Equal(1, otherDecision!.Refused);
            Assert.Equal(0, otherDecision.Repaired);
        }

        Assert.Equal(before, await SnapshotAsync(options, target.RequestId));
        Assert.Equal(0, await CountAsync(options, target.RequestId, Repair));
        Assert.Equal(0, await CountAsync(options, target.RequestId, "STATUS_SYNC"));
    }

    // ── 5. Repeated scoped APPLY is idempotent ──
    [Fact]
    public async Task ScopedApply_Repeated_IsIdempotent()
    {
        var options = NewDbOptions();
        var actor = await SeedLookupsAsync(options);
        var target = await AddRequestAsync(options, actor, new Opts(), "ZZTEST-TARGET");

        await using (var ctx = new ApplicationDbContext(options))
            Assert.Equal(1, (await Service(ctx).RunForRequestAsync(target.RequestId, apply: true, actor, "first", LinkFacts(target)))!.Repaired);
        var afterFirst = await SnapshotAsync(options, target.RequestId);
        // REQ-209 shape: the scalar already matches the group, so the canonical aggregator has nothing to
        // sync — no STATUS_SYNC is fabricated by the repair itself.
        var syncAfterFirst = await CountAsync(options, target.RequestId, "STATUS_SYNC");
        Assert.Equal(0, syncAfterFirst);

        await using (var ctx = new ApplicationDbContext(options))
        {
            var second = await Service(ctx).RunForRequestAsync(target.RequestId, apply: true, actor, "second", LinkFacts(target));
            Assert.Equal(0, second!.Repaired);
            Assert.Equal(1, second.AlreadyHealthy);
            Assert.Equal("ALREADY_HEALTHY", Assert.Single(second.Rows).Decision);
        }
        Assert.Equal(afterFirst, await SnapshotAsync(options, target.RequestId));   // state-for-state identical
        Assert.Equal(1, await CountAsync(options, target.RequestId, Repair));        // no second audit
        Assert.Equal(syncAfterFirst, await CountAsync(options, target.RequestId, "STATUS_SYNC"));
    }

    // ── 6. Scoped APPLY through the same pipeline: premature WAITING_RECEIPT is demoted exactly as the global run does ──
    [Fact]
    public async Task ScopedApply_UsesTheSameClassifier_DemotesPrematureWaitingReceipt()
    {
        var options = NewDbOptions();
        var actor = await SeedLookupsAsync(options);
        var target = await AddRequestAsync(options, actor, new Opts { GroupStatus = "WAITING_RECEIPT", RequestStatusId = S_WAITING_RECEIPT }, "ZZTEST-TARGET");
        await using (var ctx = new ApplicationDbContext(options))
        {
            ctx.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(), RequestId = target.RequestId, ActorUserId = actor, ActionTaken = "MOVE_TO_RECEIPT",
                PreviousStatusId = S_PAYMENT_COMPLETED, NewStatusId = S_WAITING_RECEIPT,
                Comment = $"[Grupo P.O.: ZZTEST Supplier | GroupId: {target.GroupId.ToString()[..8]}] Pedido movido para aguardando recibo.",
                CreatedAtUtc = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new ApplicationDbContext(options))
        {
            var preview = await Service(ctx).RunForRequestAsync(target.RequestId, apply: false, actor, null, null);
            Assert.Equal("REPAIR_LINK_AND_DEMOTE", Assert.Single(preview!.Rows).Decision);

            var applied = await Service(ctx).RunForRequestAsync(target.RequestId, apply: true, actor, "r", new(target.GroupId, "REPAIR_LINK_AND_DEMOTE"));
            Assert.Equal(1, applied!.Repaired);
            Assert.Equal("REPAIRED_LINK_AND_DEMOTE", Assert.Single(applied.Rows).Decision);
        }
        await using (var ctx2 = new ApplicationDbContext(options))
        {
            Assert.Equal("PAYMENT_COMPLETED", (await ctx2.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == target.GroupId)).Status);
            Assert.Equal(S_PAYMENT_COMPLETED, (await ctx2.Requests.AsNoTracking().SingleAsync(r => r.Id == target.RequestId)).StatusId);
        }
    }
}
