using System;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Api.Services.Dashboard;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using AlplaPortal.Infrastructure.Services.Finance;
using AlplaPortal.Infrastructure.Services.Receiving;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Dashboard V2 (Phase B). Additive, read-only. Slice B1+B2 exposes the Buyer section only:
/// PESSOAL (my assigned buyer work), COMPARTILHADO (the unassigned Compras pool) and GERENCIAL
/// (per-buyer workload, managerial visibility). Counts come from the canonical Buyer projection via
/// <see cref="DashboardV2QueryService"/> and reconcile with the Buyer queue/workspace. The Phase B V2
/// sections have fully replaced the legacy Dashboard; the legacy GET /api/requests/cockpit-summary
/// endpoint was retired in B9.6. See docs/DASHBOARD_V2_PHASE_B_SPECIFICATION.md.
/// </summary>
[Authorize]
[ApiController]
[Route("api/dashboard/v2")]
public class DashboardV2Controller : BaseController
{
    private readonly IFinancePaymentEligibilityService _financeEligibility;

    public DashboardV2Controller(ApplicationDbContext context, IFinancePaymentEligibilityService financeEligibility)
        : base(context) => _financeEligibility = financeEligibility;

    [HttpGet("buyer")]
    public async Task<ActionResult<DashboardV2BuyerSectionDto>> GetBuyerSection(
        [FromQuery] int? company = null,
        [FromQuery] int? plant = null,
        [FromQuery] int? department = null,
        [FromQuery] string? needLevel = null)
    {
        var roles = CurrentUserRoles;
        var isBuyer = roles.Contains(RoleConstants.Buyer);
        var canSeeWorkload = roles.Contains(RoleConstants.LocalManager)
                             || roles.Contains(RoleConstants.SystemAdministrator);

        var scoped = await GetScopedRequestsQuery();
        var service = new DashboardV2QueryService(_context);

        var dto = await service.BuildBuyerSectionAsync(
            scoped, CurrentUserId, isBuyer, canSeeWorkload, DateTime.UtcNow,
            company, plant, department, needLevel);

        return Ok(dto);
    }

    /// <summary>
    /// B3 — Finance shared queue (operational counts). Shared plane for Finance-role users;
    /// managerial aggregate (view-only, identical counts) for Local Manager / SysAdmin without the
    /// Finance role. Counts reconcile with /api/v1/finance/obligations (same canonical projection).
    /// No monetary amounts (B7); no mutation. Absent planes are null.
    /// </summary>
    [HttpGet("finance")]
    public async Task<ActionResult<DashboardV2FinanceSectionDto>> GetFinanceSection()
    {
        var roles = CurrentUserRoles;
        var isFinance = roles.Contains(RoleConstants.Finance);
        var canSeeManagerial = roles.Contains(RoleConstants.LocalManager)
                               || roles.Contains(RoleConstants.SystemAdministrator);

        var scoped = await GetScopedRequestsQuery();
        var service = new DashboardV2QueryService(_context);
        var projection = new FinanceObligationSummaryProjection(_financeEligibility);

        var dto = await service.BuildFinanceSectionAsync(
            scoped, projection, isFinance, canSeeManagerial, DateTime.UtcNow.Date);

        return Ok(dto);
    }

    /// <summary>
    /// B4 — Receiving shared queue (operational counts). Shared plane for Receiving-role users;
    /// managerial aggregate (view-only, identical counts) for Local Manager / SysAdmin without the
    /// Receiving role. Counts reconcile with /api/v1/receiving/queue (same canonical projection).
    /// No aging, no money, no mutation. Absent planes are null.
    /// </summary>
    [HttpGet("receiving")]
    public async Task<ActionResult<DashboardV2ReceivingSectionDto>> GetReceivingSection()
    {
        var roles = CurrentUserRoles;
        var isReceiving = roles.Contains(RoleConstants.Receiving);
        var canSeeManagerial = roles.Contains(RoleConstants.LocalManager)
                               || roles.Contains(RoleConstants.SystemAdministrator);

        var scoped = await GetScopedRequestsQuery();
        var service = new DashboardV2QueryService(_context);
        var projection = new ReceivingQueueProjection();

        var dto = await service.BuildReceivingSectionAsync(scoped, projection, isReceiving, canSeeManagerial);
        return Ok(dto);
    }

    /// <summary>
    /// B5 — "Minha Operação" (PESSOAL). Canonical personal actions the SIGNED-IN user personally owns:
    /// assigned actionable Buyer work (BuyerId == me), Area-approval work the user owns (AreaApproverId
    /// == me OR active DepartmentManager scope), and the user's own DRAFT requests. Shared role work is
    /// never personal — Final Approval (PD-01), Finance, Receiving and the unassigned Buyer pool are
    /// excluded, and current-user ownership always wins (no SysAdmin global bypass). No shared/managerial
    /// plane, no monetary amounts, no aging (B5.1 defers urgency). May be legitimately empty.
    /// </summary>
    [HttpGet("personal")]
    public async Task<ActionResult<DashboardV2PersonalSectionDto>> GetPersonalSection()
    {
        var scoped = await GetScopedRequestsQuery();
        var projection = new PersonalActionProjection(_context);
        var dto = await projection.BuildAsync(scoped, CurrentUserId, DateTime.UtcNow.Date);
        return Ok(dto);
    }

    /// <summary>
    /// B6 — canonical Operational Pipeline (GERENCIAL, read-only, informational). One stage per operational
    /// position, each on its own canonical entity unit; a request may appear in several stages
    /// (CanOverlap). Counts come straight from the canonical Buyer/Approval/PO/Finance/Receiving/Completion
    /// sources — no scalar-status flattening. Scoped by RequestAccessScope; broadly visible to any
    /// authenticated user; no ownership plane. No aging, no money, no urgency, no alerts.
    /// </summary>
    [HttpGet("pipeline")]
    public async Task<ActionResult<DashboardV2PipelineDto>> GetPipeline()
    {
        var scoped = await GetScopedRequestsQuery();
        var projection = new OperationalPipelineProjection(_context, _financeEligibility);
        var dto = await projection.BuildAsync(scoped, DateTime.UtcNow.Date);
        return Ok(dto);
    }

    /// <summary>
    /// B7 — canonical currency-safe Financial Summary (GERENCIAL, read-only). Current monetary exposure per
    /// category, partitioned by currency (never summed across currencies, no FX). Gated (PD-B7-02): only
    /// Finance, Local Manager or System Administrator are entitled; everyone else gets a null section that
    /// the frontend hides. Scoped by RequestAccessScope. No urgency (B3), no paid history (B7.3),
    /// no completed card. Amounts are all-in totals at each grain (IVA-inclusive where quotation-based).
    /// </summary>
    [HttpGet("financial")]
    public async Task<ActionResult<DashboardV2FinancialDto>> GetFinancialSummary([FromQuery] string? period = null)
    {
        var roles = CurrentUserRoles;
        var entitled = roles.Contains(RoleConstants.Finance)
                       || roles.Contains(RoleConstants.LocalManager)
                       || roles.Contains(RoleConstants.SystemAdministrator);

        if (!entitled)
            return Ok(new DashboardV2FinancialDto { CurrentExposure = null, PaidHistory = null, GeneratedAtUtc = DateTime.UtcNow });

        var today = DateTime.UtcNow.Date;
        var scoped = await GetScopedRequestsQuery();
        var projection = new FinancialSummaryProjection(_context, _financeEligibility);
        var exposure = await projection.BuildAsync(scoped, today);
        // B7.3 paid history — a separate direct RequestPayments query (no finance-projection re-run).
        var paidHistory = await projection.BuildPaidHistoryAsync(scoped, today, period);
        return Ok(new DashboardV2FinancialDto { CurrentExposure = exposure, PaidHistory = paidHistory, GeneratedAtUtc = DateTime.UtcNow });
    }

    /// <summary>
    /// B8 — canonical Alerts (read-only). Risk/deadline conditions over canonical entities that still have an
    /// open action, higher-signal than the queues. Buyer alerts reuse canonical Buyer actionability over a
    /// bounded near-deadline candidate set (no third Buyer sweep); Finance alerts use one flat RequestPayments
    /// query (no finance-projection re-run). No Receiving/Approval/PO/Documentation aging (B9). Entitlement:
    /// Buyer / Finance / Local Manager / SysAdmin; others get a null summary the frontend hides. Scoped by
    /// RequestAccessScope; managerial visibility is view-only.
    /// </summary>
    [HttpGet("alerts")]
    public async Task<ActionResult<DashboardV2AlertsDto>> GetAlerts()
    {
        var roles = CurrentUserRoles;
        var isBuyer = roles.Contains(RoleConstants.Buyer);
        var isFinance = roles.Contains(RoleConstants.Finance);
        var canSeeManagerial = roles.Contains(RoleConstants.LocalManager)
                               || roles.Contains(RoleConstants.SystemAdministrator);

        var scoped = await GetScopedRequestsQuery();
        var projection = new CanonicalAlertProjection(_context);
        var dto = await projection.BuildAsync(scoped, CurrentUserId, isBuyer, isFinance, canSeeManagerial, DateTime.UtcNow.Date);
        return Ok(dto);
    }

    /// <summary>
    /// B9.4 — canonical Stage Aging (GERENCIAL, read-only). Time-in-current-stage for in-scope
    /// APPROVAL_BATCH + PO_GROUP entities, from the OperationalStageState snapshots (never a legacy
    /// projection/cockpit sweep). Age is Africa/Luanda calendar-days; unknown age is first-class (only
    /// known-age entities count toward severity). Managerial entitlement (Local Manager / SysAdmin) — others
    /// get a null Summary the frontend hides. Buyer/REQUEST is out of scope; no navigation (read-only).
    /// </summary>
    [HttpGet("stage-aging")]
    public async Task<ActionResult<DashboardV2StageAgingDto>> GetStageAging(CancellationToken cancellationToken)
    {
        var roles = CurrentUserRoles;
        var entitled = roles.Contains(RoleConstants.LocalManager)
                       || roles.Contains(RoleConstants.SystemAdministrator);

        var scoped = await GetScopedRequestsQuery();
        var projection = new StageAgingProjection(_context);
        var dto = await projection.BuildAsync(scoped, entitled, DateTime.UtcNow, cancellationToken);
        return Ok(dto);
    }
}
