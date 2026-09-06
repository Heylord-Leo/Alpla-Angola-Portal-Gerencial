using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Dashboard V2 B9.3 — privileged, operator-triggered maintenance for the honest legacy stage-state
/// backfill. NOT a user-facing feature and NEVER runs automatically on startup. Restricted to System
/// Administrator. Both actions run the SAME classification pipeline; `preview` is a dry run that writes
/// nothing, `apply` performs the idempotent backfill. Buyer/REQUEST is out of scope (B9.2d). This endpoint
/// only populates the CURRENT snapshot — it never writes transition history and never overwrites LIVE state.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/admin/stage-aging/backfill")]
public class OperationalStageBackfillController : BaseController
{
    public OperationalStageBackfillController(ApplicationDbContext context) : base(context) { }

    private IActionResult? GuardSysAdmin()
        => CurrentUserRoles.Contains(RoleConstants.SystemAdministrator) ? null : Forbid();

    /// <summary>Dry run — classify every in-scope entity and return the proposed counts. Writes nothing.</summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;

        var result = await new OperationalStageBackfillService(_context).BackfillAsync(dryRun: true, ct: ct);
        return Ok(result);
    }

    /// <summary>Apply the idempotent backfill (safe to rerun). Requires an explicit confirm flag.</summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;
        if (!confirm) return BadRequest(new { error = "Pass ?confirm=true to apply. Use GET preview for a dry run." });

        var result = await new OperationalStageBackfillService(_context).BackfillAsync(dryRun: false, ct: ct);
        return Ok(result);
    }
}
