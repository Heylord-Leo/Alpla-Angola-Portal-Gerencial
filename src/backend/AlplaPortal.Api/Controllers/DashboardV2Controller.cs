using System;
using System.Threading.Tasks;
using AlplaPortal.Api.Services.Dashboard;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Dashboard V2 (Phase B). Additive, read-only. Slice B1+B2 exposes the Buyer section only:
/// PESSOAL (my assigned buyer work), COMPARTILHADO (the unassigned Compras pool) and GERENCIAL
/// (per-buyer workload, managerial visibility). Counts come from the canonical Buyer projection via
/// <see cref="DashboardV2QueryService"/> and reconcile with the Buyer queue/workspace. The legacy
/// GET /api/requests/cockpit-summary is untouched and remains the live dashboard until the V2 UI
/// replacement slice. See docs/DASHBOARD_V2_PHASE_B_SPECIFICATION.md.
/// </summary>
[Authorize]
[ApiController]
[Route("api/dashboard/v2")]
public class DashboardV2Controller : BaseController
{
    public DashboardV2Controller(ApplicationDbContext context) : base(context) { }

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
}
