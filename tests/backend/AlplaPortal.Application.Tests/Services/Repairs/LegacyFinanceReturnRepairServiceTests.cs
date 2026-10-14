using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Repairs;

/// <summary>
/// Scoped PREVIEW/APPLY/ROLLBACK repair of the legacy request-level Finance return (docs §G.19). InMemory context (no
/// relational transaction; the service skips BeginTransaction on non-relational providers). Only the qualifying group's
/// status and one audit history row may change; everything else is asserted untouched.
/// </summary>
public class LegacyFinanceReturnRepairServiceTests
{
    private const int S_PO_ISSUED = 9, S_WAITING_PO_CORRECTION = 12, S_PO_PARTIAL = 13;
    private static readonly Guid Actor = Guid.NewGuid();

    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Seed { public Guid RequestId, GroupId; }

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, bool returned = true, bool secondLiveGroup = false, bool livePayment = false)
    {
        var s = new Seed { RequestId = Guid.NewGuid(), GroupId = Guid.NewGuid() };
        ctx.RequestTypes.Add(new RequestType { Id = 2, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = S_PO_ISSUED, Code = RequestConstants.Statuses.PoIssued, Name = "P.O. emitida" },
            new RequestStatus { Id = S_WAITING_PO_CORRECTION, Code = RequestConstants.Statuses.WaitingPoCorrection, Name = "Devolvido" },
            new RequestStatus { Id = S_PO_PARTIAL, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "Parcial" });
        ctx.Users.Add(new User { Id = Actor, FullName = "Admin", Email = "admin@test.local", IsActive = true });
        ctx.Requests.Add(new Request { Id = s.RequestId, RequestNumber = "REQ-27/07/2026-156", Title = "Limpeza", RequestTypeId = 2, StatusId = S_WAITING_PO_CORRECTION, RequesterId = Actor, DepartmentId = 7, CompanyId = 1, EstimatedTotalAmount = 1097000m, ApprovedTotalAmount = 1097000m, ApprovedCurrencyCode = "AOA", ApprovedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAtUtc = DateTime.UtcNow.AddDays(-60), UpdatedAtUtc = DateTime.UtcNow.AddDays(-30) });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = s.GroupId, RequestId = s.RequestId, SupplierId = 10, SupplierNameSnapshot = "Qlerius | Serviços de Limpeza", Status = RequestConstants.PoGroupStatuses.PoIssued, TotalAmount = 1097000m, CurrencyCode = "AOA", PurchaseOrderNumber = "ECF11 2026/156", CreatedByUserId = Actor, CreatedAtUtc = DateTime.UtcNow.AddDays(-50) });
        if (secondLiveGroup) ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.RequestId, Status = RequestConstants.PoGroupStatuses.WaitingPoCorrection, TotalAmount = 5m, CurrencyCode = "AOA", CreatedByUserId = Actor, CreatedAtUtc = DateTime.UtcNow });
        ctx.RequestPoGroups.Add(new RequestPoGroup { Id = Guid.NewGuid(), RequestId = s.RequestId, Status = RequestConstants.PoGroupStatuses.Cancelled, TotalAmount = 1m, CurrencyCode = "AOA", CreatedByUserId = Actor, CreatedAtUtc = DateTime.UtcNow }); // cancelled groups are ignored
        ctx.RequestAttachments.AddRange(
            new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, AttachmentTypeCode = RequestAttachment.TYPE_PROFORMA, FileName = "proforma.pdf", StorageReference = "proforma.pdf", UploadedByUserId = Actor, UploadedAtUtc = DateTime.UtcNow },
            new RequestAttachment { Id = Guid.NewGuid(), RequestId = s.RequestId, RequestPoGroupId = s.GroupId, AttachmentTypeCode = RequestAttachment.TYPE_PO, FileName = "po.pdf", StorageReference = "po.pdf", UploadedByUserId = Actor, UploadedAtUtc = DateTime.UtcNow });
        ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = Actor, ActionTaken = "REGISTER_PO", NewStatusId = S_PO_ISSUED, CreatedAtUtc = new DateTime(2026, 8, 3, 16, 23, 10, DateTimeKind.Utc) });
        if (returned) ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = Actor, ActionTaken = "FINANCE_RETURN_ADJUSTMENT", PreviousStatusId = S_PO_ISSUED, NewStatusId = S_WAITING_PO_CORRECTION, CreatedAtUtc = new DateTime(2026, 8, 11, 9, 42, 23, DateTimeKind.Utc) });
        if (livePayment) ctx.RequestPayments.Add(new RequestPayment { RequestId = s.RequestId, RequestPoGroupId = s.GroupId, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = 1, PlannedAmount = 1097000m, CurrencyCode = "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Scheduled, CreatedByUserId = Actor, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();
        return s;
    }

    private static async Task<(int StatusId, decimal? Approved, string? Cur, DateTime? At, int Attachments, int Histories, int Outbox, int ApLogs)> SnapshotAsync(ApplicationDbContext ctx, Guid id)
    {
        var r = await ctx.Requests.AsNoTracking().SingleAsync(x => x.Id == id);
        return (r.StatusId, r.ApprovedTotalAmount, r.ApprovedCurrencyCode, r.ApprovedAtUtc,
            await ctx.RequestAttachments.CountAsync(a => a.RequestId == id), await ctx.RequestStatusHistories.CountAsync(h => h.RequestId == id),
            await ctx.EmailOutbox.CountAsync(e => e.RequestId == id), await ctx.AccountsPayableNotificationLogs.CountAsync(l => l.RequestId == id));
    }

    [Fact]
    public async Task Preview_qualifies_and_writes_nothing()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var before = await SnapshotAsync(ctx, s.RequestId);
        var p = await new LegacyFinanceReturnRepairService(ctx).PreviewAsync(s.RequestId);
        Assert.NotNull(p);
        Assert.Equal("PREVIEW", p!.Mode); Assert.Equal("QUALIFIES", p.Decision); Assert.Equal(s.GroupId, p.GroupId);
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, p.GroupStatusBefore); Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, p.GroupStatusAfter);
        Assert.Equal(1, p.LiveGroupCount); Assert.Equal(0, p.LivePaymentCount);
        Assert.Equal(new DateTime(2026, 8, 11, 9, 42, 23, DateTimeKind.Utc), p.LatestFinanceReturnUtc);
        Assert.Equal(before, await SnapshotAsync(ctx, s.RequestId));
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
    }

    [Fact]
    public async Task Apply_moves_only_the_group_writes_one_audit_row_preserves_everything_else_and_is_idempotent()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var before = await SnapshotAsync(ctx, s.RequestId);
        var svc = new LegacyFinanceReturnRepairService(ctx);

        var r = await svc.ApplyAsync(s.RequestId, Actor, "REQ-156: devolução legada confirmada (G.19)", s.GroupId);
        Assert.Equal("REPAIRED", r!.Decision);
        Assert.Equal(RequestConstants.PoGroupStatuses.WaitingPoCorrection, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
        var after = await SnapshotAsync(ctx, s.RequestId);
        Assert.Equal(before.StatusId, after.StatusId); Assert.Equal(before.Approved, after.Approved); Assert.Equal(before.Cur, after.Cur); Assert.Equal(before.At, after.At);
        Assert.Equal(before.Attachments, after.Attachments); Assert.Equal(before.Outbox, after.Outbox); Assert.Equal(before.ApLogs, after.ApLogs);
        Assert.Equal(before.Histories + 1, after.Histories);
        var audit = Assert.Single(await ctx.RequestStatusHistories.Where(h => h.RequestId == s.RequestId && h.ActionTaken == LegacyFinanceReturnRepairService.RepairAction).ToListAsync());
        Assert.Equal(LegacyFinanceReturnRepairService.IdempotencyKey(s.GroupId), audit.IdempotencyKey);
        Assert.Equal(S_WAITING_PO_CORRECTION, audit.PreviousStatusId); Assert.Equal(S_WAITING_PO_CORRECTION, audit.NewStatusId);
        Assert.Contains("PO_ISSUED → WAITING_PO_CORRECTION", audit.Comment); Assert.Contains("REQ-156: devolução legada confirmada", audit.Comment);

        // Repeated APPLY and PREVIEW after repair
        Assert.Equal("ALREADY_REPAIRED", (await svc.ApplyAsync(s.RequestId, Actor, "again", s.GroupId))!.Decision);
        Assert.Equal("ALREADY_REPAIRED", (await svc.PreviewAsync(s.RequestId))!.Decision);
        Assert.Equal(after, await SnapshotAsync(ctx, s.RequestId));
    }

    [Fact]
    public async Task Apply_fails_closed_when_the_expected_group_differs()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var r = await new LegacyFinanceReturnRepairService(ctx).ApplyAsync(s.RequestId, Actor, "x", Guid.NewGuid());
        Assert.Equal("REFUSED", r!.Decision); Assert.Contains("difere do facto", r.Reason);
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
        Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == LegacyFinanceReturnRepairService.RepairAction));
    }

    [Theory]
    [InlineData("noReturn", "sem FINANCE_RETURN_ADJUSTMENT")]
    [InlineData("livePayment", "pagamento(s) ativo(s)")]
    [InlineData("req275", "não é WAITING_PO_CORRECTION")]
    public async Task Preview_and_apply_refuse_non_qualifying_shapes_without_writing(string shape, string reasonFragment)
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, returned: shape != "noReturn", livePayment: shape == "livePayment", secondLiveGroup: shape == "req275");
        if (shape == "req275") { var req = await ctx.Requests.SingleAsync(r => r.Id == s.RequestId); req.StatusId = S_PO_PARTIAL; await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear(); }
        var before = await SnapshotAsync(ctx, s.RequestId);
        var svc = new LegacyFinanceReturnRepairService(ctx);
        var p = await svc.PreviewAsync(s.RequestId);
        Assert.Equal("REFUSED", p!.Decision); Assert.Contains(reasonFragment, p.Reason);
        var a = await svc.ApplyAsync(s.RequestId, Actor, "x", s.GroupId);
        Assert.Equal("REFUSED", a!.Decision);
        Assert.Equal(before, await SnapshotAsync(ctx, s.RequestId));
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
    }

    [Fact]
    public async Task Unknown_request_returns_null()
    {
        await using var ctx = NewCtx();
        Assert.Null(await new LegacyFinanceReturnRepairService(ctx).PreviewAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Rollback_reverts_only_while_nothing_happened_after_the_repair()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var svc = new LegacyFinanceReturnRepairService(ctx);
        Assert.Equal("REPAIRED", (await svc.ApplyAsync(s.RequestId, Actor, "apply", s.GroupId))!.Decision);

        var rb = await svc.RollbackAsync(s.RequestId, Actor, "aplicado por engano", s.GroupId);
        Assert.Equal("ROLLED_BACK", rb!.Decision);
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, (await ctx.RequestPoGroups.AsNoTracking().SingleAsync(g => g.Id == s.GroupId)).Status);
        Assert.Single(await ctx.RequestStatusHistories.Where(h => h.RequestId == s.RequestId && h.ActionTaken == LegacyFinanceReturnRepairService.RollbackAction).ToListAsync());
        Assert.Equal(S_WAITING_PO_CORRECTION, (await ctx.Requests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId)).StatusId);   // scalar never touched
        Assert.Equal("REFUSED", (await svc.RollbackAsync(s.RequestId, Actor, "twice", s.GroupId))!.Decision);                       // group no longer WAITING_PO_CORRECTION / history after audit
    }

    [Fact]
    public async Task Rollback_is_refused_after_a_legitimate_later_action()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var svc = new LegacyFinanceReturnRepairService(ctx);
        Assert.Equal("REPAIRED", (await svc.ApplyAsync(s.RequestId, Actor, "apply", s.GroupId))!.Decision);
        // The Buyer re-registers (REREGISTER_PO history later than the audit): the repair must never be undone over it.
        ctx.RequestStatusHistories.Add(new RequestStatusHistory { Id = Guid.NewGuid(), RequestId = s.RequestId, ActorUserId = Actor, ActionTaken = "REREGISTER_PO", NewStatusId = S_PO_ISSUED, CreatedAtUtc = DateTime.UtcNow.AddSeconds(5) });
        var g = await ctx.RequestPoGroups.SingleAsync(x => x.Id == s.GroupId); g.Status = RequestConstants.PoGroupStatuses.PoIssued;
        await ctx.SaveChangesAsync(); ctx.ChangeTracker.Clear();

        var rb = await svc.RollbackAsync(s.RequestId, Actor, "late", s.GroupId);
        Assert.Equal("REFUSED", rb!.Decision); Assert.Contains("histórico posterior", rb.Reason);
        Assert.Equal(0, await ctx.RequestStatusHistories.CountAsync(h => h.ActionTaken == LegacyFinanceReturnRepairService.RollbackAction));
    }

    [Fact]
    public async Task Rollback_without_a_repair_audit_is_refused()
    {
        await using var ctx = NewCtx(); var s = await SeedAsync(ctx);
        var rb = await new LegacyFinanceReturnRepairService(ctx).RollbackAsync(s.RequestId, Actor, "x", s.GroupId);
        Assert.Equal("REFUSED", rb!.Decision); Assert.Contains("sem audit", rb.Reason);
    }
}
