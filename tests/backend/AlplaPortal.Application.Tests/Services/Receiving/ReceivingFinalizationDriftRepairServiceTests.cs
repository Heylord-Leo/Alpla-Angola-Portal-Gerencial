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

// v2.245.0 Receiving Finalization Fix — repair preview/apply/idempotency (§24 L–O).
public class ReceivingFinalizationDriftRepairServiceTests
{
    private const int S_INFOLLOWUP = 1, LS_RECEIVED = 10, LS_WAITING = 11, LS_PARTIAL = 12;

    private static DbContextOptions<ApplicationDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    private sealed record Seed(Guid RequestId, Guid GroupId, Guid LineItemId, Guid WinningItemId, Guid ActorId);

    /// <param name="duplicateWinningLine">seed a second winning item on line 1 → ambiguity</param>
    /// <param name="preLinked">seed the line already linked → healthy</param>
    private static async Task<Seed> SeedReq013(DbContextOptions<ApplicationDbContext> options,
        bool duplicateWinningLine = false, bool preLinked = false)
    {
        await using var ctx = new ApplicationDbContext(options);
        var actor = new User { Id = Guid.NewGuid(), FullName = "Admin", Email = $"a-{Guid.NewGuid():N}@t.local" };
        ctx.Users.Add(actor);
        ctx.RequestTypes.Add(new RequestType { Id = 1, Code = "QUOTATION", Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = S_INFOLLOWUP, Code = "IN_FOLLOWUP", Name = "Em acompanhamento" });
        ctx.Set<LineItemStatus>().AddRange(
            new LineItemStatus { Id = LS_RECEIVED, Code = "RECEIVED", Name = "Recebido" },
            new LineItemStatus { Id = LS_WAITING, Code = "WAITING_QUOTATION", Name = "Aguardando" },
            new LineItemStatus { Id = LS_PARTIAL, Code = "PARTIALLY_RECEIVED", Name = "Parcial" });
        ctx.Currencies.Add(new Currency { Id = 1, Code = "AOA", Symbol = "Kz" });

        var winId = Guid.NewGuid();
        var quotationId = Guid.NewGuid();
        var reqId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var liId = Guid.NewGuid();

        var req = new Request
        {
            Id = reqId, RequestNumber = "REQ-01/07/2026-013", Title = "Monitor", StatusId = S_INFOLLOWUP,
            RequestTypeId = 1, DepartmentId = 4, CompanyId = 1, PlantId = 1, CurrencyId = 1,
            RequesterId = actor.Id, CreatedAtUtc = DateTime.UtcNow, SelectedQuotationId = quotationId,
        };
        ctx.Requests.Add(req);

        var quotation = new Quotation
        {
            Id = quotationId, RequestId = reqId, IsSelected = true, SupplierNameSnapshot = "NCR", Currency = "AOA",
        };
        quotation.Items.Add(new QuotationItem { Id = winId, QuotationId = quotationId, LineNumber = 1, Description = "Monitor", Quantity = 2, ReceivedQuantity = 2, LineItemStatusId = LS_RECEIVED });
        if (duplicateWinningLine)
            quotation.Items.Add(new QuotationItem { Id = Guid.NewGuid(), QuotationId = quotationId, LineNumber = 1, Description = "Monitor dup", Quantity = 2, ReceivedQuantity = 2, LineItemStatusId = LS_RECEIVED });
        ctx.Quotations.Add(quotation);

        var group = new RequestPoGroup { Id = groupId, RequestId = reqId, Status = "IN_FOLLOWUP", SupplierNameSnapshot = "NCR", CreatedByUserId = actor.Id, CreatedAtUtc = DateTime.UtcNow };
        ctx.RequestPoGroups.Add(group);

        ctx.RequestLineItems.Add(new RequestLineItem
        {
            Id = liId, RequestId = reqId, RequestPoGroupId = groupId, LineNumber = 1, Description = "Monitor",
            Quantity = 2, ReceivedQuantity = 0, IsDeleted = false,
            LineItemStatusId = preLinked ? LS_RECEIVED : LS_WAITING,
            SelectedQuotationItemId = preLinked ? winId : (Guid?)null,
        });

        await ctx.SaveChangesAsync();
        return new Seed(reqId, groupId, liId, winId, actor.Id);
    }

    [Fact]
    public async Task Preview_Req013_ReportsOneRepairable_WritesNothing()
    {
        var options = NewDbOptions();
        var seed = await SeedReq013(options);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new ReceivingFinalizationDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);

        Assert.Equal("PREVIEW", res.Status);
        Assert.Equal(1, res.Scanned);
        Assert.Equal(1, res.Eligible);
        Assert.Equal(1, res.WouldRepair);
        Assert.Equal(0, res.Repaired);
        Assert.Contains(res.Rows, r => r.Decision == "REPAIR" && r.RequestNumber == "REQ-01/07/2026-013");

        await using var verify = new ApplicationDbContext(options);
        var li = await verify.RequestLineItems.FirstAsync(x => x.Id == seed.LineItemId);
        Assert.Null(li.SelectedQuotationItemId); // preview wrote nothing
        Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "RECEIVING_LINKAGE_REPAIR"));
    }

    [Fact]
    public async Task Apply_Req013_RestoresLink_SyncsFacts_NoStatusChange_TechnicalAuditOnly()
    {
        var options = NewDbOptions();
        var seed = await SeedReq013(options);
        await using (var ctx = new ApplicationDbContext(options))
        {
            var res = await new ReceivingFinalizationDriftRepairService(ctx).RunAsync(apply: true, actorId: seed.ActorId, reason: "Incidente REQ-013");
            Assert.Equal("APPLIED", res.Status);
            Assert.Equal(1, res.Repaired);
        }
        await using var verify = new ApplicationDbContext(options);
        var li = await verify.RequestLineItems.Include(x => x.LineItemStatus).FirstAsync(x => x.Id == seed.LineItemId);
        Assert.Equal(seed.WinningItemId, li.SelectedQuotationItemId);   // link restored
        Assert.Equal(2, li.ReceivedQuantity);                          // facts synced
        Assert.Equal("RECEIVED", li.LineItemStatus!.Code);
        var group = await verify.RequestPoGroups.FirstAsync(g => g.Id == seed.GroupId);
        Assert.Equal("IN_FOLLOWUP", group.Status);                     // NO status advance
        var req = await verify.Requests.FirstAsync(r => r.Id == seed.RequestId);
        Assert.Equal(S_INFOLLOWUP, req.StatusId);                      // request scalar unchanged
        Assert.True(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "RECEIVING_LINKAGE_REPAIR"));
        Assert.False(await verify.RequestStatusHistories.AnyAsync(h => h.ActionTaken == "CONFIRM_RECEIVING")); // never fabricated
    }

    [Fact]
    public async Task Apply_IsIdempotent()
    {
        var options = NewDbOptions();
        var seed = await SeedReq013(options);
        await using (var ctx = new ApplicationDbContext(options))
            await new ReceivingFinalizationDriftRepairService(ctx).RunAsync(apply: true, actorId: seed.ActorId, reason: "first");
        await using (var ctx = new ApplicationDbContext(options))
        {
            var second = await new ReceivingFinalizationDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
            Assert.Equal(0, second.WouldRepair);          // nothing left to repair
            Assert.Equal(1, second.AlreadyHealthy);
        }
    }

    [Fact]
    public async Task Ambiguous_DuplicateWinningLineNumber_Refused_NotRepaired()
    {
        var options = NewDbOptions();
        var seed = await SeedReq013(options, duplicateWinningLine: true);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new ReceivingFinalizationDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(0, res.WouldRepair);
        Assert.Equal(1, res.Ambiguous);
        Assert.Contains(res.Rows, r => r.Decision == "AMBIGUOUS");
    }

    [Fact]
    public async Task AlreadyLinked_IsHealthy_NotOverwritten()
    {
        var options = NewDbOptions();
        var seed = await SeedReq013(options, preLinked: true);
        await using var ctx = new ApplicationDbContext(options);
        var res = await new ReceivingFinalizationDriftRepairService(ctx).RunAsync(apply: false, actorId: seed.ActorId, reason: null);
        Assert.Equal(0, res.WouldRepair);
        Assert.Equal(1, res.AlreadyHealthy);
    }
}
