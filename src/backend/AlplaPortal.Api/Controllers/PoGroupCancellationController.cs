using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Phase 1 P.O. group cancellation / return to quotation after final approval — Buyer side
/// (docs/PO_GROUP_CANCELLATION.md). Authorization, scope, eligibility, concurrency and audit are decided by
/// <see cref="IPoGroupCancellationService"/>; this controller only translates HTTP.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/requests/{requestId:guid}/po-groups/{groupId:guid}")]
public class PoGroupCancellationController : BaseController
{
    private readonly IPoGroupCancellationService _service;

    public PoGroupCancellationController(ApplicationDbContext context, IPoGroupCancellationService service) : base(context)
    {
        _service = service;
    }

    private PoGroupCancellationActor Actor => new(CurrentUserId, CurrentUserRoles);

    /// <summary>Cancel the group: RESTART_QUOTATION (items back to quotation, new approvals) or CANCEL_ITEMS (items closed).</summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(Guid requestId, Guid groupId, [FromBody] CancelPoGroupRequestDto? dto)
    {
        dto ??= new CancelPoGroupRequestDto();
        var result = await _service.CancelAsync(new CancelPoGroupCommand(
            requestId, groupId, Actor, dto.Kind, dto.ReasonCode, dto.Reason, dto.EcfStatus, dto.EcfReference,
            dto.EcfDeclarationNote, dto.EcfEvidenceAttachmentId, ParseRowVersion(dto.ExpectedGroupRowVersion)));
        return Translate(result);
    }

    /// <summary>Read model of the group's cancellation (404 when the group was never cancelled).</summary>
    [HttpGet("cancellation")]
    public async Task<IActionResult> Get(Guid requestId, Guid groupId)
    {
        var dto = await _service.GetAsync(groupId, Actor);
        if (dto == null || dto.RequestId != requestId) return NotFound(new ProblemDetails { Title = "Cancelamento não encontrado", Status = 404 });
        return Ok(dto);
    }

    /// <summary>Whether the caller may cancel this group now, and why not otherwise (read-side mirror of the write rules).</summary>
    [HttpGet("cancellation/eligibility")]
    public async Task<IActionResult> Eligibility(Guid requestId, Guid groupId)
        => Ok(await _service.EvaluateEligibilityAsync(groupId, Actor));

    /// <summary>Buyer: update the ECF declaration while Finance confirmation is pending or rejected.</summary>
    [HttpPut("cancellation/ecf-declaration")]
    public async Task<IActionResult> UpdateDeclaration(Guid requestId, Guid groupId, [FromBody] UpdateEcfDeclarationDto? dto)
    {
        dto ??= new UpdateEcfDeclarationDto();
        var existing = await _service.GetAsync(groupId, Actor);
        if (existing == null || existing.RequestId != requestId) return NotFound(new ProblemDetails { Title = "Cancelamento não encontrado", Status = 404 });
        var result = await _service.UpdateEcfDeclarationAsync(new UpdateEcfDeclarationCommand(
            existing.Id, Actor, dto.EcfStatus, dto.EcfReference, dto.Note, dto.EvidenceAttachmentId, ParseRowVersion(dto.ExpectedRowVersion)));
        return Translate(result);
    }

    /// <summary>Buyer/SysAdmin: resend the cancellation notification under a NEW correlation (never repeats the cancellation).</summary>
    [HttpPost("cancellation/resend-notification")]
    public async Task<IActionResult> Resend(Guid requestId, Guid groupId)
    {
        var existing = await _service.GetAsync(groupId, Actor);
        if (existing == null || existing.RequestId != requestId) return NotFound(new ProblemDetails { Title = "Cancelamento não encontrado", Status = 404 });
        return Translate(await _service.ResendNotificationAsync(existing.Id, Actor));
    }

    internal static byte[]? ParseRowVersion(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try { return Convert.FromBase64String(base64); } catch (FormatException) { return null; }
    }

    internal static IActionResult TranslateResult(PoGroupCancellationOperationResult result)
    {
        if (result.Succeeded) return new OkObjectResult(result.Payload);
        var pd = new ProblemDetails { Title = result.Title, Detail = result.Detail, Status = result.Status };
        pd.Extensions["code"] = result.Code;
        return new ObjectResult(pd) { StatusCode = result.Status };
    }

    private IActionResult Translate(PoGroupCancellationOperationResult result) => TranslateResult(result);
}

/// <summary>Finance side of the phase 1 P.O. cancellation: the ECF confirmation queue and the confirm/reject decisions (D12).</summary>
[Authorize]
[ApiController]
[Route("api/v1/finance/po-cancellations")]
public class FinancePoCancellationController : BaseController
{
    private readonly IPoGroupCancellationService _service;

    public FinancePoCancellationController(ApplicationDbContext context, IPoGroupCancellationService service) : base(context)
    {
        _service = service;
    }

    private PoGroupCancellationActor Actor => new(CurrentUserId, CurrentUserRoles);

    /// <summary>Cancellations requiring Finance confirmation, in the caller's request scope. status: OPEN (default) | PENDING | REJECTED | CONFIRMED | ALL.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? plantId, [FromQuery] string? search)
    {
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Finance) && !roles.Contains(RoleConstants.SystemAdministrator))
            return StatusCode(403, new ProblemDetails { Title = "Acesso negado", Detail = "Apenas Finanças ou administradores podem consultar a fila de confirmações.", Status = 403 });
        return Ok(await _service.ListForFinanceAsync(Actor, status, plantId, search));
    }

    /// <summary>Finance confirms that the previous ECF was annulled (a human attestation; the Portal does not verify Primavera).</summary>
    [HttpPost("{cancellationId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid cancellationId, [FromBody] FinanceCancellationReviewDto? dto)
    {
        dto ??= new FinanceCancellationReviewDto();
        return PoGroupCancellationController.TranslateResult(await _service.FinanceReviewAsync(
            new FinanceReviewCommand(cancellationId, Actor, Confirm: true, dto.Note, PoGroupCancellationController.ParseRowVersion(dto.ExpectedRowVersion))));
    }

    /// <summary>Finance rejects the Buyer's declaration (note mandatory); the group stays cancelled and the replacement P.O. stays blocked.</summary>
    [HttpPost("{cancellationId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid cancellationId, [FromBody] FinanceCancellationReviewDto? dto)
    {
        dto ??= new FinanceCancellationReviewDto();
        return PoGroupCancellationController.TranslateResult(await _service.FinanceReviewAsync(
            new FinanceReviewCommand(cancellationId, Actor, Confirm: false, dto.Note, PoGroupCancellationController.ParseRowVersion(dto.ExpectedRowVersion))));
    }
}
