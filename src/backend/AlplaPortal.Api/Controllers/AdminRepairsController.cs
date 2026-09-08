using System;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// v2.241.0 — privileged, operator-triggered controlled repairs. NOT user-facing and NEVER runs
/// automatically. Restricted to System Administrator. Each action is a single-intent repair for one
/// confirmed defect class — deliberately NOT a generic financial editor.
///
/// <para>The legacy-monetary-scale action fixes the confirmed x1000 defect (incident
/// REQ-11/08/2026-228): <c>confirm=false</c> is a dry-run preview that writes nothing;
/// <c>confirm=true</c> applies the correction atomically. The caller supplies only the wrong total it
/// expects and the correct total it intends plus a reason — the service derives and guards
/// everything else.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/admin/repairs")]
public class AdminRepairsController : BaseController
{
    public AdminRepairsController(ApplicationDbContext context) : base(context) { }

    private IActionResult? GuardSysAdmin()
        => CurrentUserRoles.Contains(RoleConstants.SystemAdministrator) ? null : Forbid();

    /// <summary>
    /// Preview (confirm=false, default) or apply (confirm=true) the legacy monetary-scale correction
    /// for a single request. Preview writes nothing. Apply requires a body with a non-empty reason.
    /// </summary>
    [HttpPost("legacy-monetary-scale/{requestId:guid}")]
    public async Task<IActionResult> LegacyMonetaryScale(
        Guid requestId,
        [FromQuery] bool confirm = false,
        [FromBody] LegacyMonetaryScaleRepairRequest? body = null,
        CancellationToken ct = default)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;

        var service = new LegacyMonetaryScaleRepairService(_context);

        if (!confirm)
        {
            var preview = await service.PreviewAsync(
                requestId, body?.ExpectedCurrentTotal, body?.ExpectedCorrectTotal, ct);
            if (preview == null) return NotFound(new { error = "Pedido não encontrado." });
            return Ok(preview);
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Para aplicar, envie um corpo com expectedCurrentTotal, expectedCorrectTotal e reason." });

        // The corrective audit MUST attribute the repair to the authenticated Portal user who runs
        // confirm=true — the canonical CurrentUserId (from the request principal). No fallback: if the
        // principal cannot be mapped to a real Portal user, refuse and write nothing.
        var user = await _context.Users.FindAsync(new object?[] { CurrentUserId }, ct);
        if (user == null)
            return BadRequest(new { error = "Não foi possível identificar o utilizador autenticado para registrar a auditoria da correção." });

        var result = await service.ApplyAsync(requestId, body, user.Id, user.FullName ?? user.Email ?? user.Id.ToString(), ct);

        return result.Status switch
        {
            LegacyMonetaryScaleRepairResult.Statuses.Applied => Ok(result),
            LegacyMonetaryScaleRepairResult.Statuses.AlreadyCorrect => Ok(result),
            LegacyMonetaryScaleRepairResult.Statuses.Conflict => Conflict(result),
            _ => BadRequest(result), // REFUSED (safety gate / not found / missing reason)
        };
    }
}
