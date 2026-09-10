using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.242.0 — the conservative PAYMENT-only backfill of RequestPoGroup.PoResponsibleBuyerId. Assigns
/// only when UpdatedByUserId resolves to an active Buyer and history does not contradict it; never
/// touches QUOTATION groups; idempotent.
/// </summary>
public class PoResponsibleBuyerBackfillServiceTests
{
    private const int PaymentTypeId = 9, QuotationTypeId = 1, StatusId = 3, BuyerRoleId = 5;
    private const string WPC = "WAITING_PO_CORRECTION";

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static void SeedLookups(ApplicationDbContext ctx)
    {
        ctx.RequestTypes.Add(new RequestType { Id = PaymentTypeId, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestTypes.Add(new RequestType { Id = QuotationTypeId, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestStatuses.Add(new RequestStatus { Id = StatusId, Code = RequestConstants.Statuses.PoPartiallyUploaded, Name = "P.O Parcial" });
        ctx.Roles.Add(new Role { Id = BuyerRoleId, RoleName = RoleConstants.Buyer });
    }

    private static Guid AddUser(ApplicationDbContext ctx, string name, bool active, bool buyer)
    {
        var id = Guid.NewGuid();
        ctx.Users.Add(new User { Id = id, FullName = name, Email = $"{Guid.NewGuid():N}@t.local", IsActive = active });
        if (buyer) ctx.Set<UserRoleAssignment>().Add(new UserRoleAssignment { UserId = id, RoleId = BuyerRoleId });
        return id;
    }

    private static Guid AddPaymentGroup(ApplicationDbContext ctx, string number, string? po, Guid? updatedBy,
        Guid? alreadyOwner = null, int typeId = PaymentTypeId)
    {
        var reqId = Guid.NewGuid();
        ctx.Requests.Add(new Request
        {
            Id = reqId, RequestNumber = number, Title = number, Description = "t",
            RequestTypeId = typeId, StatusId = StatusId, RequesterId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(), DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow
        });
        var gId = Guid.NewGuid();
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = gId, RequestId = reqId, SupplierNameSnapshot = "SUP", CurrencyCode = "AOA", TotalAmount = 1m,
            Status = WPC, PurchaseOrderNumber = po, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = updatedBy, PoResponsibleBuyerId = alreadyOwner
        });
        return gId;
    }

    [Fact]
    public async Task Preview_assigns_reliable_payment_registrant_only()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = AddUser(ctx, "Celestina", active: true, buyer: true);
        var gid = AddPaymentGroup(ctx, "REQ-254", "FAC2025/125", updatedBy: buyer);
        await ctx.SaveChangesAsync();

        var preview = await new PoResponsibleBuyerBackfillService(ctx).PreviewAsync();
        Assert.Equal(1, preview.Scanned);
        Assert.Equal(1, preview.WouldAssign);
        Assert.Equal(0, preview.Assigned); // preview writes nothing
        var row = Assert.Single(preview.Rows);
        Assert.Equal("WouldAssign", row.Decision);
        Assert.Equal(buyer, row.CandidateBuyerId);
        // Nothing persisted by preview.
        Assert.Null((await ctx.RequestPoGroups.FindAsync(gid))!.PoResponsibleBuyerId);
    }

    [Fact]
    public async Task Quotation_groups_are_never_targeted()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = AddUser(ctx, "Samuel", active: true, buyer: true);
        AddPaymentGroup(ctx, "REQ-Q", "PO-Q", updatedBy: buyer, typeId: QuotationTypeId);
        await ctx.SaveChangesAsync();

        var preview = await new PoResponsibleBuyerBackfillService(ctx).PreviewAsync();
        Assert.Equal(0, preview.Scanned); // QUOTATION excluded from candidate scope
    }

    [Fact]
    public async Task Already_assigned_and_no_po_are_out_of_scope()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = AddUser(ctx, "Celestina", active: true, buyer: true);
        AddPaymentGroup(ctx, "REQ-assigned", "PO1", updatedBy: buyer, alreadyOwner: buyer); // already owned
        AddPaymentGroup(ctx, "REQ-nopo", null, updatedBy: buyer);                            // no P.O.
        await ctx.SaveChangesAsync();

        var preview = await new PoResponsibleBuyerBackfillService(ctx).PreviewAsync();
        Assert.Equal(0, preview.Scanned);
    }

    [Fact]
    public async Task NonBuyer_or_null_updatedby_is_unresolved()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var nonBuyer = AddUser(ctx, "Nelson", active: true, buyer: false);
        AddPaymentGroup(ctx, "REQ-nonbuyer", "PO2", updatedBy: nonBuyer);
        AddPaymentGroup(ctx, "REQ-null", "PO3", updatedBy: null);
        await ctx.SaveChangesAsync();

        var preview = await new PoResponsibleBuyerBackfillService(ctx).PreviewAsync();
        Assert.Equal(2, preview.Scanned);
        Assert.Equal(0, preview.WouldAssign);
        Assert.Equal(2, preview.Unresolved);
    }

    [Fact]
    public async Task Conflicting_history_actor_downgrades_to_unresolved()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = AddUser(ctx, "Celestina", active: true, buyer: true);
        var other = AddUser(ctx, "Samuel", active: true, buyer: true);
        var reqId = Guid.NewGuid();
        var gid = Guid.NewGuid();
        var shortId = gid.ToString().Substring(0, 8);
        ctx.Requests.Add(new Request
        {
            Id = reqId, RequestNumber = "REQ-conflict", Title = "REQ-conflict", Description = "t",
            RequestTypeId = PaymentTypeId, StatusId = StatusId, RequesterId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(), DepartmentId = 1, CompanyId = 1, CreatedAtUtc = DateTime.UtcNow
        });
        ctx.RequestPoGroups.Add(new RequestPoGroup
        {
            Id = gid, RequestId = reqId, SupplierNameSnapshot = "SUP", CurrencyCode = "AOA", TotalAmount = 1m,
            Status = WPC, PurchaseOrderNumber = "PO4", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = buyer
        });
        // A REGISTER_PO history row naming this group's short id but by a DIFFERENT actor.
        ctx.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(), RequestId = reqId, ActorUserId = other, ActionTaken = "REGISTER_PO",
            NewStatusId = StatusId, Comment = $"[GroupId: {shortId}] registrada", CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var preview = await new PoResponsibleBuyerBackfillService(ctx).PreviewAsync();
        Assert.Equal(1, preview.Conflicting);
        Assert.Equal(0, preview.WouldAssign);
        Assert.Equal("Conflicting", preview.Rows.Single().Decision);
    }

    [Fact]
    public async Task Apply_assigns_and_is_idempotent()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = AddUser(ctx, "Celestina", active: true, buyer: true);
        var gid = AddPaymentGroup(ctx, "REQ-254", "FAC2025/125", updatedBy: buyer);
        await ctx.SaveChangesAsync();

        var svc = new PoResponsibleBuyerBackfillService(ctx);
        var applied = await svc.ApplyAsync(buyer, "backfill");
        Assert.Equal(1, applied.Assigned);
        Assert.Equal(buyer, (await ctx.RequestPoGroups.FindAsync(gid))!.PoResponsibleBuyerId);

        // Rerun: nothing left in scope (already owned) → no-op.
        var again = await svc.ApplyAsync(buyer, "backfill");
        Assert.Equal(0, again.Scanned);
        Assert.Equal(0, again.Assigned);
    }

    [Fact]
    public async Task Apply_without_reason_is_refused_and_writes_nothing()
    {
        using var ctx = NewContext();
        SeedLookups(ctx);
        var buyer = AddUser(ctx, "Celestina", active: true, buyer: true);
        var gid = AddPaymentGroup(ctx, "REQ-254", "PO", updatedBy: buyer);
        await ctx.SaveChangesAsync();

        var res = await new PoResponsibleBuyerBackfillService(ctx).ApplyAsync(buyer, "   ");
        Assert.Equal(PoResponsibleBuyerBackfillResult.Statuses.Refused, res.Status);
        Assert.Null((await ctx.RequestPoGroups.FindAsync(gid))!.PoResponsibleBuyerId);
    }
}
