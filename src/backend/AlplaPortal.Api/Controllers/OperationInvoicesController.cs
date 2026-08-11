using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Operation invoices ("Faturas Finais") of one PAYMENT request — Release 4 Phase 2b: Create and
/// Read only. Allocation to PO groups is Phase 3; validation is Phase 2e; OCR is Phase 5.
///
/// <para>A dedicated controller, following the <see cref="PaymentSourceDocumentsController"/>
/// precedent for request-scoped nested resources: the invoice lifecycle has its own permissions,
/// gates and audits, and folding it into the 9000-line RequestsController would bury them.</para>
///
/// <para><b>Phase 1 non-interaction is deliberate:</b> nothing here touches
/// <c>RequestPoGroup</c> — an unallocated invoice changes no obligation status, no expected
/// total and no coverage until Phase 3 allocations exist.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/requests/{requestId:guid}/operation-invoices")]
public class OperationInvoicesController : BaseController
{
    private readonly ILogger<OperationInvoicesController> _logger;
    private readonly IInternalCompanyGuard _internalCompanies;

    public OperationInvoicesController(
        ApplicationDbContext context,
        ILogger<OperationInvoicesController> logger,
        IInternalCompanyGuard internalCompanies) : base(context)
    {
        _logger = logger;
        _internalCompanies = internalCompanies;
    }

    /// <summary>Typed duplicate code, so the UI can branch without parsing Portuguese.</summary>
    public const string DuplicateErrorCode = "OPERATION_INVOICE_DUPLICATE";

    // ── Read ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every invoice of the request, unallocated ones included — the Phase 1 obligations
    /// projection intentionally does not surface them, so this list is where an uploaded invoice
    /// becomes visible before Phase 3 allocation.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<OperationInvoiceDto>>> List(Guid requestId)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var invoices = await _context.OperationInvoices
            .AsNoTracking()
            .Where(i => i.RequestId == requestId)
            .OrderBy(i => i.UploadedAtUtc)
            .ToListAsync();

        var result = new List<OperationInvoiceDto>(invoices.Count);
        foreach (var invoice in invoices) result.Add(await ProjectAsync(invoice));
        return Ok(result);
    }

    [HttpGet("{operationInvoiceId:guid}")]
    public async Task<ActionResult<OperationInvoiceDto>> Get(Guid requestId, Guid operationInvoiceId)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var invoice = await _context.OperationInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);

        if (invoice == null) return NotFound(Problem404("Fatura final não encontrada."));
        return Ok(await ProjectAsync(invoice));
    }

    // ── Duplicate preflight ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Advisory early warning, asked before the user finishes typing. Not authoritative and not
    /// meant to be — the 409 at persistence remains the enforcement.
    /// </summary>
    [HttpPost("check-duplicate")]
    public async Task<ActionResult<OperationInvoiceDuplicateResultDto>> CheckDuplicate(
        Guid requestId, [FromBody] CheckOperationInvoiceDuplicateDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var result = new OperationInvoiceDuplicateResultDto();

        // 1. The identical file, already claimed by an effective invoice anywhere in the Portal.
        var hash = dto.ContentHash?.Trim();
        if (!string.IsNullOrWhiteSpace(hash))
        {
            var sameFile = await _context.OperationInvoices
                .AsNoTracking()
                .Join(_context.RequestAttachments.Where(a => a.FileHash == hash),
                      i => i.AttachmentId, a => a.Id, (i, a) => i)
                .ToListAsync();

            result.SameFile = await ToCandidateAsync(sameFile
                .FirstOrDefault(i => OperationInvoiceLifecyclePolicy.IsEffectiveForDuplicateCheck(i.Status)));
        }

        // 2. The same fiscal identity: supplier + number + series, globally.
        if (dto.SupplierId.HasValue && !string.IsNullOrWhiteSpace(dto.DocumentNumber))
        {
            result.SameBusinessDocument = await ToCandidateAsync(
                await FindEffectiveDuplicateAsync(dto.SupplierId.Value, dto.DocumentNumber, dto.DocumentSeries));
        }

        return Ok(result);
    }

    // ── Create ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a final/operation invoice against this request. Manual entry only in Phase 2b;
    /// the invoice lands in PENDING_VALIDATION and contributes no coverage until Phase 3
    /// allocation plus Finance validation.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid requestId, [FromBody] SaveOperationInvoiceDto dto)
    {
        // Visibility first: an out-of-scope request must be indistinguishable from a nonexistent
        // one, before any role or validation answer leaks that it exists.
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        // Role gate (approved Phase 2 rule): Finance and Buyer register invoices; the requester
        // and receiving read. SystemAdministrator follows the administrative can-act convention —
        // and, like everyone, never bypasses the financial-integrity rules below.
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Finance) &&
            !roles.Contains(RoleConstants.Buyer) &&
            !roles.Contains(RoleConstants.SystemAdministrator))
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Sem permissão",
                Detail = "Apenas o Financeiro e o Comprador podem registar faturas finais.",
                Status = 403
            });
        }

        if (request.RequestType?.Code != RequestConstants.Types.Payment)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tipo de pedido inválido",
                Detail = "Faturas finais existem apenas em pedidos de Pagamento.",
                Status = 400
            });
        }

        if (!OperationInvoiceLifecyclePolicy.CanCreateInRequestStatus(request.Status?.Code))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Estado do pedido não permite faturas finais",
                Detail = "A fatura final só pode ser registada depois da aprovação do pedido " +
                         "(incluindo após o pagamento) e enquanto o pedido não estiver concluído, " +
                         "rejeitado ou cancelado.",
                Status = 409
            });
        }

        // ── Field validation → the standard errors dictionary the frontend already renders ──
        var errors = new Dictionary<string, string[]>();
        if (dto.AttachmentId == null || dto.AttachmentId == Guid.Empty)
            errors["AttachmentId"] = new[] { "Anexe o ficheiro da fatura antes de a registar." };
        if (dto.SupplierId is null or <= 0)
            errors["SupplierId"] = new[] { "Indique o fornecedor." };
        if (string.IsNullOrWhiteSpace(dto.DocumentNumber))
            errors["DocumentNumber"] = new[] { "Indique o número do documento." };
        if (dto.DocumentDate == null)
            errors["DocumentDate"] = new[] { "Indique a data do documento." };
        if (string.IsNullOrWhiteSpace(dto.Currency))
            errors["Currency"] = new[] { "Indique a moeda." };
        if (dto.GrossAmount is null or <= 0)
            errors["GrossAmount"] = new[] { "Indique o total da fatura (maior que zero)." };

        // Net and tax travel together or not at all — a lone half would force the server to
        // invent the other, and nothing here invents values.
        if (dto.NetAmount.HasValue != dto.TaxAmount.HasValue)
        {
            errors["NetAmount"] = new[]
                { "Indique o valor líquido e o imposto em conjunto, ou nenhum dos dois." };
        }
        else if (dto.NetAmount.HasValue && dto.GrossAmount is > 0)
        {
            var gross = dto.GrossAmount.Value;
            var difference = Math.Abs(gross - (dto.NetAmount.Value + dto.TaxAmount!.Value));
            if (difference > OperationInvoiceTolerance.For(gross))
            {
                errors["GrossAmount"] = new[]
                {
                    "O total da fatura não corresponde à soma do valor líquido com o imposto " +
                    "(fora da tolerância)."
                };
            }
        }

        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));

        // ── Supplier integrity: the same rule, the same guard, no bypass ──
        var supplier = await _context.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == dto.SupplierId!.Value);
        if (supplier == null)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["SupplierId"] = new[] { "Fornecedor selecionado não existe." }
            }));
        }

        var internalCompany = await _internalCompanies.ResolveSupplierAsync(dto.SupplierId);
        if (internalCompany != null)
        {
            var problem = new ProblemDetails
            {
                Title = "Fornecedor inválido para pagamento",
                Detail = InternalCompanyPolicy.SupplierMessage,
                Status = 400
            };
            problem.Extensions["code"] = InternalCompanyPolicy.ViolationCode;
            problem.Extensions["internalCompanyId"] = internalCompany.Id;
            problem.Extensions["internalCompanyName"] = internalCompany.Name;
            return BadRequest(problem);
        }

        // ── Attachment: the Portal's one file mechanism, validated — never a second store ──
        var attachment = await _context.RequestAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == dto.AttachmentId!.Value);

        if (attachment == null || attachment.RequestId != requestId)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O anexo indicado não pertence a este pedido.",
                Status = 400
            });
        }

        if (!string.Equals(attachment.AttachmentTypeCode, RequestAttachment.TYPE_OPERATION_INVOICE,
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O anexo indicado não é do tipo Fatura Final (OPERATION_INVOICE).",
                Status = 400
            });
        }

        if (attachment.IsDeleted || attachment.VoidedAtUtc != null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O anexo indicado foi removido ou anulado.",
                Status = 400
            });
        }

        // One attachment is one invoice. The unique index is the structural backstop; this check
        // turns the raw constraint failure into an answer the user can act on.
        var attachmentClaimed = await _context.OperationInvoices
            .AnyAsync(i => i.AttachmentId == dto.AttachmentId!.Value);
        if (attachmentClaimed)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Anexo já utilizado",
                Detail = "Este ficheiro já está registado como uma fatura final.",
                Status = 409
            });
        }

        // ── Duplicate enforcement: GLOBAL fiscal identity (approved Phase 2 rule) ──
        // A fiscal invoice must not be recognized as debt in two Portal requests. Terminal
        // invoices (VOIDED/REJECTED/REPLACEMENT_REQUESTED) no longer occupy the identity, which
        // is what keeps a corrected reissue representable.
        var duplicate = await FindEffectiveDuplicateAsync(
            dto.SupplierId!.Value, dto.DocumentNumber, dto.DocumentSeries);
        if (duplicate != null)
        {
            var problem = new ProblemDetails
            {
                Title = "Fatura duplicada",
                Detail = $"A fatura {duplicate.DocumentNumber} deste fornecedor já está registada " +
                         "no Portal.",
                Status = 409
            };
            problem.Extensions["code"] = DuplicateErrorCode;
            problem.Extensions["existingOperationInvoiceId"] = duplicate.Id;
            problem.Extensions["existingRequestId"] = duplicate.RequestId;
            return Conflict(problem);
        }

        // ── Persist: invoice + audit, one transaction, or neither ──
        var invoice = new OperationInvoice
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            AttachmentId = dto.AttachmentId!.Value,
            SupplierId = dto.SupplierId,
            SupplierTaxIdSnapshot = supplier.TaxId,
            // BilledCompanyNameRead stays null: it records what OCR READ, and nothing was read.
            DocumentNumber = dto.DocumentNumber!.Trim(),
            DocumentSeries = Trimmed(dto.DocumentSeries),
            DocumentDate = dto.DocumentDate,
            DueDate = dto.DueDate,
            Currency = dto.Currency!.Trim().ToUpperInvariant(),
            NetAmount = dto.NetAmount,
            TaxAmount = dto.TaxAmount,
            GrossAmount = dto.GrossAmount,
            Notes = Trimmed(dto.Notes),
            Status = OperationInvoiceLifecyclePolicy.InitialManualStatus,
            // Manual creation IS typed numbers; the flag exists so the future OCR intake can say
            // the opposite, not so a manual create can pretend it was read.
            AmountsEnteredManually = dto.AmountsEnteredManually ?? true,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = CurrentUserId
        };

        _context.OperationInvoices.Add(invoice);

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "FATURA_OPERACAO_REGISTADA",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Fatura final {invoice.DocumentNumber} " +
                      $"({supplier.Name}, {invoice.Currency} " +
                      $"{invoice.GrossAmount:N2}) registada. Aguarda validação do Financeiro.",
            IdempotencyKey = PostPaymentIdempotencyKeys.OperationInvoiceRegistered(
                requestId, invoice.AttachmentId),
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Operation invoice {OperationInvoiceId} registered on request {RequestId} in status {Status}.",
            invoice.Id, requestId, invoice.Status);

        return Ok(await ProjectAsync(invoice));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private async Task<Request?> LoadScopedRequestAsync(Guid requestId) =>
        await (await GetScopedRequestsQuery())
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestId);

    private static ProblemDetails Problem404(string detail = "Pedido não encontrado.") =>
        new() { Title = "Não encontrado", Detail = detail, Status = 404 };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The same fiscal identity among EFFECTIVE invoices, Portal-wide: supplier + trimmed
    /// case-insensitive number + series, where a null series equals a blank one — the
    /// source-document normalization, applied at the approved global scope.
    /// </summary>
    private async Task<OperationInvoice?> FindEffectiveDuplicateAsync(
        int supplierId, string? documentNumber, string? documentSeries)
    {
        var number = documentNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number)) return null;

        var series = documentSeries?.Trim() ?? string.Empty;

        var candidates = await _context.OperationInvoices
            .AsNoTracking()
            .Where(i => i.SupplierId == supplierId)
            .ToListAsync();

        return candidates.FirstOrDefault(i =>
            OperationInvoiceLifecyclePolicy.IsEffectiveForDuplicateCheck(i.Status) &&
            string.Equals(i.DocumentNumber?.Trim(), number, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(i.DocumentSeries?.Trim() ?? string.Empty, series,
                          StringComparison.OrdinalIgnoreCase));
    }

    private async Task<OperationInvoiceDuplicateCandidateDto?> ToCandidateAsync(OperationInvoice? invoice)
    {
        if (invoice == null) return null;

        var requestNumber = await _context.Requests.AsNoTracking()
            .Where(r => r.Id == invoice.RequestId)
            .Select(r => r.RequestNumber)
            .FirstOrDefaultAsync();

        return new OperationInvoiceDuplicateCandidateDto
        {
            OperationInvoiceId = invoice.Id,
            RequestId = invoice.RequestId,
            RequestNumber = requestNumber,
            DocumentNumber = invoice.DocumentNumber,
            DocumentSeries = invoice.DocumentSeries,
            Status = invoice.Status
        };
    }

    private async Task<OperationInvoiceDto> ProjectAsync(OperationInvoice invoice)
    {
        var attachment = await _context.RequestAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == invoice.AttachmentId);

        var supplierName = invoice.SupplierId.HasValue
            ? await _context.Suppliers.AsNoTracking()
                .Where(s => s.Id == invoice.SupplierId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync()
            : null;

        var uploadedByName = await _context.Users.AsNoTracking()
            .Where(u => u.Id == invoice.UploadedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync();

        return new OperationInvoiceDto
        {
            Id = invoice.Id,
            RequestId = invoice.RequestId,
            SupplierId = invoice.SupplierId,
            SupplierName = supplierName,
            SupplierTaxIdSnapshot = invoice.SupplierTaxIdSnapshot,
            DocumentNumber = invoice.DocumentNumber,
            DocumentSeries = invoice.DocumentSeries,
            DocumentDate = invoice.DocumentDate,
            DueDate = invoice.DueDate,
            Currency = invoice.Currency,
            NetAmount = invoice.NetAmount,
            TaxAmount = invoice.TaxAmount,
            GrossAmount = invoice.GrossAmount,
            Status = invoice.Status,
            AmountsEnteredManually = invoice.AmountsEnteredManually,
            Notes = invoice.Notes,
            AttachmentId = invoice.AttachmentId,
            AttachmentFileName = attachment?.FileName,
            AttachmentStorageReference = attachment?.StorageReference,
            UploadedAtUtc = invoice.UploadedAtUtc,
            UploadedByUserId = invoice.UploadedByUserId,
            UploadedByName = uploadedByName,
            UpdatedAtUtc = invoice.UpdatedAtUtc,
            UpdatedByUserId = invoice.UpdatedByUserId,
            ValidatedAtUtc = invoice.ValidatedAtUtc,
            ValidatedByUserId = invoice.ValidatedByUserId,
            RejectionReason = invoice.RejectionReason,
            VoidedAtUtc = invoice.VoidedAtUtc,
            VoidReason = invoice.VoidReason,
            SupersededByOperationInvoiceId = invoice.SupersededByOperationInvoiceId,
            RowVersion = invoice.RowVersion
        };
    }
}
