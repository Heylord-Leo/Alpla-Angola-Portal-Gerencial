using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Fiscal Receipt binding of one PO group (Release 4 Phase 4B) — the terminal closing document
/// of the post-payment completion workflow.
///
/// <para>Two-step by design: the file is stored first through the standard attachment upload
/// (TYPE_FISCAL_RECEIPT, Finance/SysAdmin only); this endpoint then BINDS it to the group,
/// writes the FISCAL_RECEIPT_UPLOADED history and runs the Phase-1 completion evaluation, all
/// persisted by ONE SaveChanges — no partial state where the stamp exists without its history
/// or completion reading.</para>
///
/// <para>The fiscal-receipt STATE is never persisted: it is always derived through
/// <see cref="FiscalReceiptStateDeriver"/>. A group that owes no separate receipt
/// (RequiresSeparateFiscalReceipt=false) refuses uploads outright — irrelevant evidence must
/// never occupy FiscalReceiptAttachmentId. Replacement/correction of an uploaded receipt is a
/// future explicit workflow, not a Phase 4B side door.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/requests/{requestId:guid}/po-groups/{groupId:guid}/fiscal-receipt")]
public class FiscalReceiptsController : BaseController
{
    /// <summary>Typed business codes — the UI branches on codes, never on Portuguese.</summary>
    public const string NotRequiredCode = "FISCAL_RECEIPT_NOT_REQUIRED";
    public const string LockedCode = "FISCAL_RECEIPT_LOCKED";
    public const string AlreadyUploadedCode = "FISCAL_RECEIPT_ALREADY_UPLOADED";
    public const string AttachmentInvalidCode = "FISCAL_RECEIPT_ATTACHMENT_INVALID";
    public const string RequestStateCode = "FISCAL_RECEIPT_REQUEST_STATE";

    private readonly ILogger<FiscalReceiptsController> _logger;
    private readonly IRequestCompletionService _completion;
    private readonly PostPaymentCompletionOptions _postPaymentOptions;

    public FiscalReceiptsController(
        ApplicationDbContext context,
        ILogger<FiscalReceiptsController> logger,
        IRequestCompletionService completion,
        IOptions<PostPaymentCompletionOptions> postPaymentOptions) : base(context)
    {
        _logger = logger;
        _completion = completion;
        _postPaymentOptions = postPaymentOptions?.Value ?? new PostPaymentCompletionOptions();
    }

    /// <summary>
    /// Binds a stored TYPE_FISCAL_RECEIPT attachment to the group. Finance/SysAdmin only.
    /// Idempotent for an exact retry with the same attachment; a different attachment is refused
    /// (no replacement flow in Phase 4B).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Upload(
        Guid requestId, Guid groupId, [FromBody] UploadFiscalReceiptDto dto)
    {
        // Gated-endpoint contract since Release 1: while the intake feature is disabled the
        // route does not exist — same 404 as an unknown request.
        if (PostPaymentCompletionPolicy.IsFeatureDisabled(_postPaymentOptions))
            return NotFound(Problem404());

        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        // Only Finance uploads the terminal closing document (rules R5/R6). No Buyer, no
        // Receiving, no Requester — being Finance alone is still not sufficient: every
        // structural guard below applies regardless of role.
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Finance) &&
            !roles.Contains(RoleConstants.SystemAdministrator))
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Sem permissão",
                Detail = "Apenas o Financeiro ou o Administrador de Sistema podem carregar o " +
                         "Recibo Fiscal.",
                Status = 403
            });
        }

        // Same post-approval mutation window as every completion-workflow write: COMPLETED,
        // REJECTED, CANCELLED and WAITING_PO_CORRECTION requests take no fiscal receipt.
        if (!OperationInvoiceLifecyclePolicy.CanMutateInRequestStatus(request.Status?.Code))
        {
            var problem = new ProblemDetails
            {
                Title = "Estado do pedido não permite esta operação",
                Detail = "O Recibo Fiscal só pode ser carregado enquanto o pedido está no período " +
                         "pós-aprovação e não em correção de P.O., concluído, rejeitado ou cancelado.",
                Status = 409
            };
            problem.Extensions["code"] = RequestStateCode;
            return Conflict(problem);
        }

        var group = await _context.RequestPoGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.RequestId == requestId);
        if (group == null) return NotFound(Problem404("Grupo não encontrado."));

        // Already uploaded FIRST: an exact retry is an idempotent success even after the group
        // has completed (a retried request must never read "group state changed" for its own
        // successful write); anything else is a refused replacement — post-completion/post-upload
        // correction is a future explicit workflow.
        if (group.FiscalReceiptAttachmentId != null)
        {
            if (group.FiscalReceiptAttachmentId == dto.AttachmentId)
                return Ok(Project(group));

            var problem = new ProblemDetails
            {
                Title = "Recibo Fiscal já carregado",
                Detail = "Este grupo já tem um Recibo Fiscal registado. A substituição do Recibo " +
                         "Fiscal não está disponível — contacte a Administração.",
                Status = 409
            };
            problem.Extensions["code"] = AlreadyUploadedCode;
            return Conflict(problem);
        }

        if (StatusIs(group.Status, RequestConstants.PoGroupStatuses.Cancelled) ||
            StatusIs(group.Status, RequestConstants.PoGroupStatuses.Completed) ||
            StatusIs(group.Status, RequestConstants.PoGroupStatuses.WaitingPoCorrection))
        {
            var problem = new ProblemDetails
            {
                Title = "Grupo não aceita Recibo Fiscal",
                Detail = $"O grupo está em '{group.Status}' — um grupo cancelado, concluído ou em " +
                         "correção de P.O. não aceita Recibo Fiscal.",
                Status = 409
            };
            problem.Extensions["code"] = RequestStateCode;
            return Conflict(problem);
        }

        // No separate receipt owed (approved conditional rule): irrelevant evidence must never
        // occupy the completion identity of a group that completes without it.
        var unclassified = StatusIs(group.OperationInvoiceStatus,
                RequestConstants.OperationInvoiceStatuses.Unclassified)
            || group.SourceDocumentType == null;
        if (!unclassified && !group.RequiresSeparateFiscalReceipt)
        {
            var problem = new ProblemDetails
            {
                Title = "Recibo Fiscal não exigido",
                Detail = "Este grupo não exige Recibo Fiscal separado — o documento classificado " +
                         "já comprova o pagamento.",
                Status = 409
            };
            problem.Extensions["code"] = NotRequiredCode;
            return Conflict(problem);
        }

        // The deriver is the single unlocking rulebook (rule R5): operational receipt AND the
        // Final Invoice obligation must be satisfied first; an unclassified group never unlocks
        // (rule R15). The pending-reason helper names the missing dimensions without duplicating
        // the projector.
        if (FiscalReceiptStateDeriver.Derive(group) !=
            RequestConstants.FiscalReceiptStatuses.PendingUpload)
        {
            var problem = new ProblemDetails
            {
                Title = "Recibo Fiscal bloqueado",
                Detail = "O Recibo Fiscal só pode ser carregado depois de satisfeitas as restantes " +
                         $"obrigações do grupo. Pendente: {PostPaymentPendingReason.Compute(group)}.",
                Status = 409
            };
            problem.Extensions["code"] = LockedCode;
            problem.Extensions["pending"] = PostPaymentPendingReason.Compute(group);
            return Conflict(problem);
        }

        // Attachment integrity: exists, not deleted, belongs to THIS request, is typed as a
        // fiscal receipt, and is not already the fiscal receipt of another group.
        var attachment = await _context.RequestAttachments
            .FirstOrDefaultAsync(a => a.Id == dto.AttachmentId && !a.IsDeleted);
        if (attachment == null || attachment.RequestId != requestId)
        {
            var problem = new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O anexo indicado não foi encontrado neste pedido.",
                Status = 409
            };
            problem.Extensions["code"] = AttachmentInvalidCode;
            return Conflict(problem);
        }

        if (!string.Equals(attachment.AttachmentTypeCode, RequestAttachment.TYPE_FISCAL_RECEIPT,
                StringComparison.OrdinalIgnoreCase))
        {
            var problem = new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O anexo indicado não é um Recibo Fiscal. Carregue o documento com o " +
                         "tipo correto antes de o vincular ao grupo.",
                Status = 409
            };
            problem.Extensions["code"] = AttachmentInvalidCode;
            return Conflict(problem);
        }

        var boundElsewhere = await _context.RequestPoGroups
            .AnyAsync(g => g.FiscalReceiptAttachmentId == dto.AttachmentId && g.Id != groupId);
        if (boundElsewhere)
        {
            var problem = new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O anexo indicado já é o Recibo Fiscal de outro grupo deste pedido.",
                Status = 409
            };
            problem.Extensions["code"] = AttachmentInvalidCode;
            return Conflict(problem);
        }

        // ── Bind + history + Phase-1 evaluation, one SaveChanges ──
        var uploadedAt = DateTime.UtcNow;
        group.FiscalReceiptAttachmentId = attachment.Id;
        group.FiscalReceiptUploadedAtUtc = uploadedAt;
        group.FiscalReceiptUploadedByUserId = CurrentUserId;
        group.UpdatedAtUtc = uploadedAt;
        group.UpdatedByUserId = CurrentUserId;

        var key = PostPaymentIdempotencyKeys.FiscalReceiptUploaded(group.Id, attachment.Id);
        var exists = _context.RequestStatusHistories.Local.Any(h => h.IdempotencyKey == key)
                     || await _context.RequestStatusHistories.AnyAsync(h => h.IdempotencyKey == key);
        if (!exists)
        {
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = requestId,
                ActorUserId = CurrentUserId,
                ActionTaken = WorkflowEventCodes.FiscalReceiptUploaded,
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"Recibo Fiscal \"{attachment.FileName}\" registado no grupo " +
                          $"{group.SupplierNameSnapshot ?? group.Id.ToString("D")[..8]}.",
                IdempotencyKey = key,
                CreatedAtUtc = uploadedAt
            });
        }

        // Phase 1 completion — same transaction, before SaveChanges, per the committed contract.
        // With the receipt stamped, a group whose other obligations are satisfied completes here
        // (GROUP_COMPLETED, GC:{GroupId}:{AttachmentId}). The parent request is NEVER touched:
        // Phase 2 belongs to Phase 4C. Exact no-op while CompletionEnabled=false.
        await _completion.EvaluateGroupCompletionAsync(requestId, groupId, CurrentUserId);

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Fiscal receipt {AttachmentId} bound to group {GroupId} of request {RequestId} by {UserId}.",
            attachment.Id, groupId, requestId, CurrentUserId);

        return Ok(Project(group));
    }

    private static object Project(RequestPoGroup group) => new
    {
        GroupId = group.Id,
        GroupStatus = group.Status,
        FiscalReceiptAttachmentId = group.FiscalReceiptAttachmentId,
        FiscalReceiptUploadedAtUtc = group.FiscalReceiptUploadedAtUtc,
        FiscalReceiptState = FiscalReceiptStateDeriver.Derive(group),
        Completed = string.Equals(group.Status, RequestConstants.PoGroupStatuses.Completed,
            StringComparison.OrdinalIgnoreCase),
        CompletedAtUtc = group.CompletedAtUtc
    };

    private async Task<Request?> LoadScopedRequestAsync(Guid requestId) =>
        await (await GetScopedRequestsQuery())
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestId);

    private static ProblemDetails Problem404(string detail = "Pedido não encontrado.") =>
        new() { Title = "Não encontrado", Detail = detail, Status = 404 };

    private static bool StatusIs(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
