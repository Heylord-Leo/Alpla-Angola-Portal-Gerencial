using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B5.1 — canonical "Minha Operação" (PESSOAL) projection. Locks the product intent: ONLY work the
/// signed-in user personally owns appears; shared role membership (Finance / Receiving / Final Approval /
/// unassigned Buyer pool) never inflates the personal count, and SysAdmin does not inherit others'
/// personal actions. Buyer actionability is delegated to the canonical BuyerQueueProjectionBuilder.
/// </summary>
public class PersonalActionProjectionTests
{
    private const int TypeQuotation = 1;
    private const int TypePayment = 2;
    // Status lookup ids
    private const int StDraft = 10, StWaitingQuotation = 11, StWaitingArea = 12, StWaitingFinal = 13,
                      StPaymentCompleted = 14, StAreaAdjustment = 15;

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new ApplicationDbContext(options);
        ctx.RequestTypes.Add(new RequestType { Id = TypeQuotation, Code = RequestConstants.Types.Quotation, Name = "Cotação" });
        ctx.RequestTypes.Add(new RequestType { Id = TypePayment, Code = RequestConstants.Types.Payment, Name = "Pagamento" });
        ctx.RequestStatuses.AddRange(
            new RequestStatus { Id = StDraft, Code = RequestConstants.Statuses.Draft, Name = "Rascunho" },
            new RequestStatus { Id = StWaitingQuotation, Code = RequestConstants.Statuses.WaitingQuotation, Name = "Ag. Cotação" },
            new RequestStatus { Id = StWaitingArea, Code = RequestConstants.Statuses.WaitingAreaApproval, Name = "Ag. Aprovação Área" },
            new RequestStatus { Id = StWaitingFinal, Code = RequestConstants.Statuses.WaitingFinalApproval, Name = "Ag. Aprovação Final" },
            new RequestStatus { Id = StPaymentCompleted, Code = RequestConstants.Statuses.PaymentCompleted, Name = "Pago" },
            new RequestStatus { Id = StAreaAdjustment, Code = RequestConstants.Statuses.AreaAdjustment, Name = "Reajuste Área" });
        ctx.SaveChanges();
        return ctx;
    }

    private static Request NewRequest(int statusId, int typeId, Guid requester, Guid? buyer = null,
        Guid? areaApprover = null, int departmentId = 5, int? plantId = 1)
    {
        return new Request
        {
            Id = Guid.NewGuid(),
            Title = "T-" + Guid.NewGuid().ToString("N")[..6],
            RequestNumber = "R-" + Guid.NewGuid().ToString("N")[..6],
            StatusId = statusId,
            RequestTypeId = typeId,
            RequesterId = requester,
            BuyerId = buyer,
            AreaApproverId = areaApprover,
            DepartmentId = departmentId,
            CompanyId = 1,
            PlantId = plantId,
            CurrencyId = 1,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    // A WAITING_QUOTATION quotation request with one pending line item → NeedsQuotation → ADD_QUOTATION.
    private static void AddPendingLineItem(Request r)
        => r.LineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(), RequestId = r.Id, Description = "item", Quantity = 1, IsDeleted = false,
            QuotationLifecycleStatus = null,
        });

    private static async Task<DashboardV2PersonalSectionDto> Build(ApplicationDbContext ctx, Guid userId)
    {
        await ctx.SaveChangesAsync();
        var projection = new PersonalActionProjection(ctx);
        return await projection.BuildAsync(ctx.Requests, userId, DateTime.UtcNow.Date);
    }

    // ── BUYER ──────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Buyer_assigned_actionable_is_included()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        var r = NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: me);
        AddPendingLineItem(r);
        ctx.Requests.Add(r);

        var dto = await Build(ctx, me);

        Assert.Contains(dto.Actions, a => a.Domain == PersonalActionDomains.Buyer
            && a.ActionType == BuyerQueueConstants.ActionCodes.AddQuotation && a.RequestId == r.Id);
        Assert.Equal("/buyer/items?ownership=me", dto.Actions.First(a => a.Domain == PersonalActionDomains.Buyer).TargetPath);
    }

    [Fact]
    public async Task Buyer_assigned_but_nonactionable_is_excluded()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        // No line items → CompletedForBuyer (HiddenByDefault) → no buyer action.
        ctx.Requests.Add(NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: me));

        var dto = await Build(ctx, me);

        Assert.DoesNotContain(dto.Actions, a => a.Domain == PersonalActionDomains.Buyer);
    }

    [Fact]
    public async Task Buyer_unassigned_pool_is_excluded()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        var r = NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: null); // unassigned
        AddPendingLineItem(r);
        ctx.Requests.Add(r);

        var dto = await Build(ctx, me);

        Assert.DoesNotContain(dto.Actions, a => a.Domain == PersonalActionDomains.Buyer);
    }

    [Fact]
    public async Task Buyer_other_users_assignment_is_excluded()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var r = NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: other);
        AddPendingLineItem(r);
        ctx.Requests.Add(r);

        var dto = await Build(ctx, me);

        Assert.DoesNotContain(dto.Actions, a => a.Domain == PersonalActionDomains.Buyer);
    }

    // ── FINANCE / RECEIVING / FINAL APPROVAL: no personal path exists ────────────────
    [Fact]
    public async Task Finance_stage_request_not_owned_yields_no_personal_action()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        // A PAYMENT request in a finance-relevant status, owned by someone else. There is no Finance
        // personal domain, so nothing should appear regardless of the viewer holding a Finance role.
        ctx.Requests.Add(NewRequest(StPaymentCompleted, TypePayment, requester: Guid.NewGuid()));

        var dto = await Build(ctx, me);

        Assert.Empty(dto.Actions);
        Assert.Equal(0, dto.Summary.ActionableActions);
    }

    [Fact]
    public async Task Receiving_stage_request_not_owned_yields_no_personal_action()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(NewRequest(StPaymentCompleted, TypeQuotation, requester: Guid.NewGuid(), buyer: Guid.NewGuid()));

        var dto = await Build(ctx, me);

        Assert.Empty(dto.Actions);
    }

    [Fact]
    public async Task FinalApproval_stage_yields_no_personal_action()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        // WAITING_FINAL_APPROVAL is role-shared (PD-01) — never personal, even for a Final Approver.
        ctx.Requests.Add(NewRequest(StWaitingFinal, TypeQuotation, requester: Guid.NewGuid()));

        var dto = await Build(ctx, me);

        Assert.DoesNotContain(dto.Actions, a => a.Domain == PersonalActionDomains.Approval);
    }

    // ── AREA APPROVAL ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Area_owned_via_active_department_manager_is_included()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        var r = NewRequest(StWaitingArea, TypeQuotation, requester: Guid.NewGuid(), departmentId: 5, plantId: 1);
        ctx.Requests.Add(r);
        ctx.DepartmentManagers.Add(new DepartmentManager { UserId = me, DepartmentId = 5, PlantId = 1, IsActive = true });

        var dto = await Build(ctx, me);

        Assert.Contains(dto.Actions, a => a.Domain == PersonalActionDomains.Approval
            && a.ActionType == PersonalActionTypes.AreaApproval && a.RequestId == r.Id);
    }

    [Fact]
    public async Task Area_owned_via_area_approver_id_is_included()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        var r = NewRequest(StWaitingArea, TypeQuotation, requester: Guid.NewGuid(), areaApprover: me);
        ctx.Requests.Add(r);

        var dto = await Build(ctx, me);

        Assert.Contains(dto.Actions, a => a.Domain == PersonalActionDomains.Approval && a.RequestId == r.Id);
    }

    [Fact]
    public async Task Area_visible_but_not_owned_is_excluded()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        // WAITING_AREA_APPROVAL but the user is neither the nominee nor a department manager for it.
        ctx.Requests.Add(NewRequest(StWaitingArea, TypeQuotation, requester: Guid.NewGuid(), departmentId: 9, plantId: 2));
        ctx.DepartmentManagers.Add(new DepartmentManager { UserId = me, DepartmentId = 5, PlantId = 1, IsActive = true });

        var dto = await Build(ctx, me);

        Assert.DoesNotContain(dto.Actions, a => a.Domain == PersonalActionDomains.Approval);
    }

    [Fact]
    public async Task Area_inactive_department_manager_is_excluded()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(NewRequest(StWaitingArea, TypeQuotation, requester: Guid.NewGuid(), departmentId: 5, plantId: 1));
        ctx.DepartmentManagers.Add(new DepartmentManager { UserId = me, DepartmentId = 5, PlantId = 1, IsActive = false });

        var dto = await Build(ctx, me);

        Assert.DoesNotContain(dto.Actions, a => a.Domain == PersonalActionDomains.Approval);
    }

    // ── REQUESTER ────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Requester_own_draft_is_included()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        var r = NewRequest(StDraft, TypePayment, requester: me);
        ctx.Requests.Add(r);

        var dto = await Build(ctx, me);

        Assert.Contains(dto.Actions, a => a.Domain == PersonalActionDomains.Requester
            && a.ActionType == PersonalActionTypes.SubmitDraft && a.RequestId == r.Id);
        Assert.Equal($"/requests/{r.Id}", dto.Actions.First(a => a.Domain == PersonalActionDomains.Requester).TargetPath);
    }

    [Fact]
    public async Task Requester_other_users_draft_is_excluded()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        ctx.Requests.Add(NewRequest(StDraft, TypePayment, requester: Guid.NewGuid()));

        var dto = await Build(ctx, me);

        Assert.DoesNotContain(dto.Actions, a => a.Domain == PersonalActionDomains.Requester);
    }

    [Fact]
    public async Task Requester_own_nondraft_is_excluded()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();
        // Owns the request but it is past DRAFT and the user is not its area owner → no personal action.
        ctx.Requests.Add(NewRequest(StWaitingArea, TypePayment, requester: me, departmentId: 9, plantId: 2));

        var dto = await Build(ctx, me);

        Assert.Empty(dto.Actions);
    }

    // ── ADMIN: ownership always wins (no global bypass) ──────────────────────────────
    [Fact]
    public async Task Admin_does_not_inherit_other_users_personal_actions()
    {
        using var ctx = NewDb();
        var admin = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        // Other users' owned work of every domain, none owned by the admin.
        var buyerReq = NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: someoneElse);
        AddPendingLineItem(buyerReq);
        ctx.Requests.Add(buyerReq);
        ctx.Requests.Add(NewRequest(StDraft, TypePayment, requester: someoneElse));
        var areaReq = NewRequest(StWaitingArea, TypeQuotation, requester: Guid.NewGuid(), departmentId: 5, plantId: 1);
        ctx.Requests.Add(areaReq);
        ctx.DepartmentManagers.Add(new DepartmentManager { UserId = someoneElse, DepartmentId = 5, PlantId = 1, IsActive = true });

        var dto = await Build(ctx, admin); // scoped = ALL requests (admin), but ownership filters to admin

        Assert.Empty(dto.Actions);
    }

    // ── SUMMARY + LEGACY RECONCILIATION (product-intent lock) ────────────────────────
    [Fact]
    public async Task Summary_counts_actions_distinct_requests_and_domains()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid();

        var buyerReq = NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: me);
        AddPendingLineItem(buyerReq);
        ctx.Requests.Add(buyerReq);
        ctx.Requests.Add(NewRequest(StDraft, TypePayment, requester: me));
        var areaReq = NewRequest(StWaitingArea, TypeQuotation, requester: Guid.NewGuid(), areaApprover: me);
        ctx.Requests.Add(areaReq);

        var dto = await Build(ctx, me);

        Assert.Equal(3, dto.Summary.ActionableActions);
        Assert.Equal(3, dto.Summary.ActionableRequests);
        Assert.Equal(1, dto.Summary.ByDomain.Single(d => d.Domain == PersonalActionDomains.Buyer).Actions);
        Assert.Equal(1, dto.Summary.ByDomain.Single(d => d.Domain == PersonalActionDomains.Approval).Actions);
        Assert.Equal(1, dto.Summary.ByDomain.Single(d => d.Domain == PersonalActionDomains.Requester).Actions);
    }

    [Fact]
    public async Task Shared_role_work_does_not_inflate_personal_count()
    {
        using var ctx = NewDb();
        var me = Guid.NewGuid(); // imagine this user holds Buyer + Finance + Receiving + Final Approver

        // The ONLY thing personally owned: one assigned actionable buyer request.
        var mine = NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: me);
        AddPendingLineItem(mine);
        ctx.Requests.Add(mine);

        // Shared/role work the legacy union would have counted — none owned by this user:
        ctx.Requests.Add(NewRequest(StPaymentCompleted, TypePayment, requester: Guid.NewGuid()));   // Finance/Receiving
        ctx.Requests.Add(NewRequest(StWaitingFinal, TypeQuotation, requester: Guid.NewGuid()));       // Final Approval (shared)
        var unassigned = NewRequest(StWaitingQuotation, TypeQuotation, requester: Guid.NewGuid(), buyer: null); // unassigned pool
        AddPendingLineItem(unassigned);
        ctx.Requests.Add(unassigned);
        ctx.Requests.Add(NewRequest(StWaitingArea, TypeQuotation, requester: Guid.NewGuid(), departmentId: 9, plantId: 2)); // area not owned

        var dto = await Build(ctx, me);

        // Legacy would show ~5; the honest personal count is exactly 1 (the assigned buyer action).
        Assert.Equal(1, dto.Summary.ActionableActions);
        Assert.Equal(PersonalActionDomains.Buyer, dto.Actions.Single().Domain);
    }

    [Fact]
    public async Task Empty_when_user_owns_nothing()
    {
        using var ctx = NewDb();
        var dto = await Build(ctx, Guid.NewGuid());
        Assert.Empty(dto.Actions);
        Assert.Equal(0, dto.Summary.ActionableActions);
        Assert.Equal(0, dto.Summary.ActionableRequests);
        Assert.Empty(dto.Summary.ByDomain);
    }
}
