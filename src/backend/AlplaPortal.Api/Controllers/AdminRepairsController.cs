using System;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Repairs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// v2.242.0 — PAYMENT-only backfill of RequestPoGroup.PoResponsibleBuyerId. <c>confirm=false</c>
    /// (default) is a read-only preview that writes nothing; <c>confirm=true</c> applies (requires a
    /// body with a non-empty reason). Conservative: assigns only when UpdatedByUserId resolves to an
    /// active Buyer and history does not contradict it; idempotent; never touches QUOTATION groups.
    /// </summary>
    [HttpPost("po-responsible-buyer-backfill")]
    public async Task<IActionResult> PoResponsibleBuyerBackfill(
        [FromQuery] bool confirm = false,
        [FromBody] PoResponsibleBuyerBackfillRequest? body = null,
        CancellationToken ct = default)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;

        var service = new PoResponsibleBuyerBackfillService(_context);

        if (!confirm)
            return Ok(await service.PreviewAsync(ct));

        if (body == null || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Para aplicar, envie um corpo com reason." });

        var result = await service.ApplyAsync(CurrentUserId, body.Reason, ct);
        return result.Status == PoResponsibleBuyerBackfillResult.Statuses.Refused
            ? BadRequest(result)
            : Ok(result);
    }

    /// <summary>
    /// v2.245.0 — receiving-finalization-drift repair (incident REQ-01/07/2026-013 class).
    /// <c>confirm=false</c> (default) is a read-only preview that writes nothing; <c>confirm=true</c>
    /// applies (requires a body with a non-empty reason). Conservative: restores only the missing
    /// SelectedQuotationItemId link + syncs receiving facts from the RECEIVED winning quotation item;
    /// idempotent; never advances group/request status, never writes CONFIRM_RECEIVING, never touches
    /// payment/PO/approval; refuses ambiguous line numbers.
    /// </summary>
    [HttpPost("receiving-finalization-drift")]
    public async Task<IActionResult> ReceivingFinalizationDrift(
        [FromQuery] bool confirm = false,
        [FromBody] ReceivingFinalizationRepairRequest? body = null,
        CancellationToken ct = default)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;

        var service = new ReceivingFinalizationDriftRepairService(_context);

        if (!confirm)
            return Ok(await service.RunAsync(apply: false, actorId: CurrentUserId, reason: null, ct: ct));

        if (body == null || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Para aplicar, envie um corpo com reason." });

        var result = await service.RunAsync(apply: true, actorId: CurrentUserId, reason: body.Reason, ct: ct);
        return Ok(result);
    }

    /// <summary>
    /// v2.245.0 — payment-receiving-status-drift repair (incident REQ-06/07/2026-023 class).
    /// <c>confirm=false</c> (default) is a read-only preview that writes nothing; <c>confirm=true</c>
    /// applies (requires a body with a non-empty reason). Conservative: promotes the single operational
    /// group of a legacy PAYMENT request from PENDING → PAYMENT_COMPLETED (the state the current pay flow
    /// would have produced) ONLY when authoritative PAYMENT_COMPLETED history exists, there is exactly one
    /// operational group, and the payment ledger does not contradict completion. Never writes
    /// CONFIRM_RECEIVING, never fabricates receipt/payment rows, never touches Request.Status/PO/approval/
    /// divergence; divergence never excludes a candidate; idempotent.
    /// </summary>
    [HttpPost("payment-receiving-status-drift")]
    public async Task<IActionResult> PaymentReceivingStatusDrift(
        [FromQuery] bool confirm = false,
        [FromBody] PaymentReceivingDriftRepairRequest? body = null,
        CancellationToken ct = default)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;

        var service = new PaymentReceivingStatusDriftRepairService(_context);

        if (!confirm)
            return Ok(await service.RunAsync(apply: false, actorId: CurrentUserId, reason: null, ct: ct));

        if (body == null || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Para aplicar, envie um corpo com reason." });

        var result = await service.RunAsync(apply: true, actorId: CurrentUserId, reason: body.Reason, ct: ct);
        return Ok(result);
    }

    /// <summary>
    /// v2.245.4 — payment-group-item-linkage repair. <c>confirm=false</c> (default) is a read-only preview
    /// that writes nothing; <c>confirm=true</c> applies (requires a body with a non-empty reason). Per
    /// request, in ONE transaction: links active unlinked line items to the single non-cancelled group;
    /// demotes a WAITING_RECEIPT group that has NO group-correlated confirmation but provable
    /// move-from-PAYMENT_COMPLETED + payment evidence back to PAYMENT_COMPLETED; reconciles the request
    /// scalar through the canonical aggregator; writes one technical audit. Fails closed on ambiguity
    /// (>1 group), conflicting/uncorrelatable evidence and terminal states. Never fabricates
    /// CONFIRM_RECEIVING / OPERATIONAL_RECEIPT_COMPLETED / PAYMENT_COMPLETED, quantities or statuses.
    ///
    /// <para><b>Scope (v2.245.10).</b> This route is the GLOBAL population scan. PREVIEW stays read-only
    /// and unrestricted. A global APPLY must state its scope explicitly — <c>scope=all</c> — otherwise it is
    /// refused (400) and writes nothing: <c>confirm=true</c> alone can no longer mutate the whole
    /// population. A single request is repaired ONLY through
    /// <c>POST …/payment-group-item-linkage/{requestId}</c>; a <c>requestId</c> given to this route is
    /// refused (400) rather than silently widened to the population.</para>
    /// </summary>
    [HttpPost("payment-group-item-linkage")]
    public async Task<IActionResult> PaymentGroupItemLinkage(
        [FromQuery] bool confirm = false,
        [FromQuery] string? scope = null,
        [FromQuery] Guid? requestId = null,
        [FromBody] PaymentGroupItemLinkageRepairRequest? body = null,
        CancellationToken ct = default)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;

        // A request id on the population route is an attempt to scope: never widen it to the population.
        if (requestId.HasValue)
            return BadRequest(new
            {
                error = "Para um único pedido use a rota POST api/v1/admin/repairs/payment-group-item-linkage/{requestId}. " +
                        "Esta rota é o scan global e ignora requestId por segurança — nada foi executado."
            });

        var aggregator = HttpContext.RequestServices.GetRequiredService<IStatusAggregationService>();
        var service = new PaymentGroupItemLinkageRepairService(_context, aggregator);

        if (!confirm)
            return Ok(await service.RunAsync(apply: false, actorId: CurrentUserId, reason: null, ct: ct));

        if (!string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new
            {
                error = "A aplicação GLOBAL exige scope=all explícito (confirm=true&scope=all). " +
                        "Para reparar um único pedido use POST api/v1/admin/repairs/payment-group-item-linkage/{requestId}. Nada foi executado."
            });

        if (body == null || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Para aplicar, envie um corpo com reason." });

        var result = await service.RunAsync(apply: true, actorId: CurrentUserId, reason: body.Reason, ct: ct);
        return Ok(result);
    }

    /// <summary>
    /// v2.245.10 — payment-group-item-linkage repair scoped to EXACTLY ONE request. <c>confirm=false</c>
    /// (default) previews only this request through the same classifier as the global scan and writes
    /// nothing; 404 when the request does not exist; a request outside the supported defect class (not
    /// PAYMENT) or refused/ambiguous/conflicting is reported as such. <c>confirm=true</c> requires a body
    /// with a non-empty <c>reason</c> plus the PREVIEW facts (<c>expectedPoGroupId</c>,
    /// <c>expectedDecision</c>); the service re-classifies on a fresh tracked load inside the per-request
    /// transaction, fails closed (409, nothing written) when the live facts differ, repairs only this
    /// request with the same aggregation, idempotency key and audit, and answers 200 with
    /// ALREADY_HEALTHY on a repeated call. The route constraint (<c>{requestId:guid}</c>) makes a malformed
    /// id unroutable — it can never fall back to the population route.
    /// </summary>
    [HttpPost("payment-group-item-linkage/{requestId:guid}")]
    public async Task<IActionResult> PaymentGroupItemLinkageForRequest(
        Guid requestId,
        [FromQuery] bool confirm = false,
        [FromBody] PaymentGroupItemLinkageScopedRepairRequest? body = null,
        CancellationToken ct = default)
    {
        var guard = GuardSysAdmin();
        if (guard != null) return guard;

        var aggregator = HttpContext.RequestServices.GetRequiredService<IStatusAggregationService>();
        var service = new PaymentGroupItemLinkageRepairService(_context, aggregator);

        if (!confirm)
        {
            var preview = await service.RunForRequestAsync(requestId, apply: false, actorId: CurrentUserId, reason: null, expected: null, ct: ct);
            if (preview == null) return NotFound(new { error = "Pedido não encontrado." });
            return Ok(preview);
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "Para aplicar, envie um corpo com reason, expectedPoGroupId e expectedDecision (os factos da pré-visualização)." });
        if (body.ExpectedPoGroupId == null || body.ExpectedPoGroupId == Guid.Empty || string.IsNullOrWhiteSpace(body.ExpectedDecision))
            return BadRequest(new { error = "Para aplicar, restate os factos da pré-visualização: expectedPoGroupId e expectedDecision (REPAIR_LINK ou REPAIR_LINK_AND_DEMOTE)." });

        var expected = new PaymentGroupItemLinkageRepairService.ScopedExpectation(body.ExpectedPoGroupId.Value, body.ExpectedDecision.Trim());
        var result = await service.RunForRequestAsync(requestId, apply: true, actorId: CurrentUserId, reason: body.Reason, expected: expected, ct: ct);
        if (result == null) return NotFound(new { error = "Pedido não encontrado." });

        if (result.Errors > 0) return StatusCode(StatusCodes.Status500InternalServerError, result);
        if (result.Repaired > 0 || result.AlreadyHealthy > 0) return Ok(result);
        return Conflict(result); // REFUSED / AMBIGUOUS / CONFLICTING / facts mismatch — nothing written
    }
}
