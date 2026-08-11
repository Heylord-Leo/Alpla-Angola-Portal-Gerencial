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

    /// <summary>Typed business codes, so the UI branches on codes and never on Portuguese.</summary>
    public const string DuplicateErrorCode = "OPERATION_INVOICE_DUPLICATE";
    public const string NotEditableCode = "OPERATION_INVOICE_NOT_EDITABLE";
    public const string NotVoidableCode = "OPERATION_INVOICE_NOT_VOIDABLE";
    public const string NotReplaceableCode = "OPERATION_INVOICE_NOT_REPLACEABLE";
    public const string ConcurrencyCode = "OPERATION_INVOICE_CONCURRENCY";
    public const string DownstreamEvidenceCode = "OPERATION_INVOICE_EVIDENCE_EXISTS";
    public const string FileDuplicateErrorCode = "OPERATION_INVOICE_FILE_DUPLICATE";
    public const string AttachmentClaimedCode = "OPERATION_INVOICE_ATTACHMENT_CLAIMED";

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

        // 1. The identical file, already claimed by an effective invoice anywhere in the Portal —
        // the same helper Create/Update/Replace enforce with, so the preflight can never drift
        // from the authoritative answer.
        result.SameFile = await ToCandidateAsync(
            await FindEffectiveFileDuplicateAsync(dto.ContentHash?.Trim()));

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
        var roleProblem = GuardMutationRole();
        if (roleProblem != null) return roleProblem;

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
        var errors = ValidateNewInvoiceFields(dto);
        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));

        // ── Idempotent retry (the source-document Create precedent): one attachment is one
        // invoice, so the same attachment offered again IS the same create — a network retry the
        // user never saw must get the existing row back, not an inexplicable conflict.
        var existingForAttachment = await _context.OperationInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.AttachmentId == dto.AttachmentId!.Value);
        if (existingForAttachment != null && existingForAttachment.RequestId == requestId)
            return Ok(await ProjectAsync(existingForAttachment));

        // ── Supplier integrity: the same rule, the same guard, no bypass ──
        var (supplier, supplierProblem) = await GuardSupplierAsync(dto.SupplierId!.Value);
        if (supplierProblem != null) return supplierProblem;

        // ── Attachment: the Portal's one file mechanism, validated — never a second store ──
        var attachmentProblem = await GuardAttachmentAsync(requestId, dto.AttachmentId!.Value);
        if (attachmentProblem != null) return attachmentProblem;

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
            SupplierTaxIdSnapshot = supplier!.TaxId,
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

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsAttachmentUniqueViolation(ex))
        {
            // Two concurrent creates passed the claim check; the unique index kept the truth.
            _context.ChangeTracker.Clear();
            return AttachmentClaimedConflict();
        }

        _logger.LogInformation(
            "Operation invoice {OperationInvoiceId} registered on request {RequestId} in status {Status}.",
            invoice.Id, requestId, invoice.Status);

        return Ok(await ProjectAsync(invoice));
    }

    // ── Update (Phase 2c) ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edits an invoice while the lifecycle still allows it (UPLOADED / PENDING_VALIDATION).
    /// Partial-update semantics, the source-document convention: a null field keeps the persisted
    /// value. Status and every workflow stamp are untouchable here — they belong to their
    /// transitions. Every gate that guarded Create runs again: nothing valid at create time is
    /// trusted to still be valid now.
    /// </summary>
    [HttpPut("{operationInvoiceId:guid}")]
    public async Task<IActionResult> Update(
        Guid requestId, Guid operationInvoiceId, [FromBody] SaveOperationInvoiceDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardMutationRole();
        if (roleProblem != null) return roleProblem;

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var invoice = await _context.OperationInvoices
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);
        if (invoice == null) return NotFound(Problem404("Fatura final não encontrada."));

        if (!OperationInvoiceLifecyclePolicy.IsEditable(invoice.Status))
        {
            var problem = new ProblemDetails
            {
                Title = "Fatura não editável",
                Detail = "Esta fatura já não pode ser alterada. Uma fatura validada corrige-se " +
                         "por substituição; uma fatura terminal permanece como está.",
                Status = 409
            };
            problem.Extensions["code"] = NotEditableCode;
            return Conflict(problem);
        }

        var staleToken = ApplyConcurrencyToken(invoice, dto.RowVersion);
        if (staleToken != null) return staleToken;

        // ── Effective values after the merge — validated as a whole, not per field ──
        var newSupplierId = dto.SupplierId ?? invoice.SupplierId;
        var newNumber = Trimmed(dto.DocumentNumber) ?? invoice.DocumentNumber;
        var newSeries = Trimmed(dto.DocumentSeries) ?? invoice.DocumentSeries;
        var newCurrency = Trimmed(dto.Currency)?.ToUpperInvariant() ?? invoice.Currency;
        var newGross = dto.GrossAmount ?? invoice.GrossAmount;
        var newNet = dto.NetAmount ?? invoice.NetAmount;
        var newTax = dto.TaxAmount ?? invoice.TaxAmount;

        var errors = new Dictionary<string, string[]>();
        if (newSupplierId is null or <= 0)
            errors["SupplierId"] = new[] { "Indique o fornecedor." };
        if (string.IsNullOrWhiteSpace(newNumber))
            errors["DocumentNumber"] = new[] { "Indique o número do documento." };
        if (string.IsNullOrWhiteSpace(newCurrency))
            errors["Currency"] = new[] { "Indique a moeda." };
        if (newGross is null or <= 0)
            errors["GrossAmount"] = new[] { "Indique o total da fatura (maior que zero)." };

        if (newNet.HasValue != newTax.HasValue)
        {
            errors["NetAmount"] = new[]
                { "Indique o valor líquido e o imposto em conjunto, ou nenhum dos dois." };
        }
        else if (newNet.HasValue && newGross is > 0 &&
                 Math.Abs(newGross.Value - (newNet.Value + newTax!.Value)) >
                 OperationInvoiceTolerance.For(newGross.Value))
        {
            errors["GrossAmount"] = new[]
            {
                "O total da fatura não corresponde à soma do valor líquido com o imposto " +
                "(fora da tolerância)."
            };
        }

        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));

        // ── Supplier: existence + the internal-ALPLA rule, re-checked on every edit ──
        var (supplier, supplierProblem) = await GuardSupplierAsync(newSupplierId!.Value);
        if (supplierProblem != null) return supplierProblem;

        // ── Attachment replacement, only when a DIFFERENT file is offered ──
        var attachmentReplaced =
            dto.AttachmentId.HasValue &&
            dto.AttachmentId.Value != Guid.Empty &&
            dto.AttachmentId.Value != invoice.AttachmentId;

        if (attachmentReplaced)
        {
            var attachmentProblem = await GuardAttachmentAsync(
                requestId, dto.AttachmentId!.Value, excludeInvoiceId: invoice.Id);
            if (attachmentProblem != null) return attachmentProblem;
        }

        // ── Duplicate: the edited identity must not collide with any OTHER effective invoice ──
        var duplicate = await FindEffectiveDuplicateAsync(
            newSupplierId!.Value, newNumber, newSeries, excludeInvoiceId: invoice.Id);
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

        // ── Apply, with a field-change summary so the audit says WHAT moved ──
        var changes = new List<string>();
        if (invoice.SupplierId != newSupplierId) changes.Add("Fornecedor");
        if (!string.Equals(invoice.DocumentNumber, newNumber, StringComparison.Ordinal)) changes.Add("Número");
        if (!string.Equals(invoice.DocumentSeries, newSeries, StringComparison.Ordinal)) changes.Add("Série");
        if (dto.DocumentDate.HasValue && invoice.DocumentDate != dto.DocumentDate) changes.Add("Data do Documento");
        if (dto.DueDate.HasValue && invoice.DueDate != dto.DueDate) changes.Add("Data de Vencimento");
        if (!string.Equals(invoice.Currency, newCurrency, StringComparison.Ordinal)) changes.Add("Moeda");
        if (invoice.NetAmount != newNet || invoice.TaxAmount != newTax || invoice.GrossAmount != newGross)
            changes.Add("Valores");
        if (dto.Notes != null && !string.Equals(invoice.Notes, Trimmed(dto.Notes), StringComparison.Ordinal))
            changes.Add("Notas");
        if (dto.AmountsEnteredManually.HasValue &&
            invoice.AmountsEnteredManually != dto.AmountsEnteredManually.Value)
            changes.Add("Origem dos Valores");
        if (attachmentReplaced) changes.Add("Anexo");

        if (changes.Count == 0) return Ok(await ProjectAsync(invoice));   // a no-op stays silent

        invoice.SupplierId = newSupplierId;
        invoice.SupplierTaxIdSnapshot = supplier!.TaxId;
        invoice.DocumentNumber = newNumber;
        invoice.DocumentSeries = newSeries;
        if (dto.DocumentDate.HasValue) invoice.DocumentDate = dto.DocumentDate;
        if (dto.DueDate.HasValue) invoice.DueDate = dto.DueDate;
        invoice.Currency = newCurrency;
        invoice.NetAmount = newNet;
        invoice.TaxAmount = newTax;
        invoice.GrossAmount = newGross;
        if (dto.Notes != null) invoice.Notes = Trimmed(dto.Notes);
        if (dto.AmountsEnteredManually.HasValue)
            invoice.AmountsEnteredManually = dto.AmountsEnteredManually.Value;
        // The old attachment row is never touched: it stays in RequestAttachments, historically
        // accessible. Only the invoice's pointer moves, to a validated unclaimed file.
        if (attachmentReplaced) invoice.AttachmentId = dto.AttachmentId!.Value;

        invoice.UpdatedAtUtc = DateTime.UtcNow;
        invoice.UpdatedByUserId = CurrentUserId;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "FATURA_OPERACAO_ALTERADA",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Fatura final {invoice.DocumentNumber} alterada " +
                      $"({string.Join(", ", changes)}).",
            IdempotencyKey = null,   // repeated edits are distinct events
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (IsAttachmentUniqueViolation(ex))
        {
            _context.ChangeTracker.Clear();
            return AttachmentClaimedConflict();
        }

        return Ok(await ProjectAsync(invoice));
    }

    // ── Void (Phase 2c) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Registered in error" — never a Finance rejection. Terminal: the invoice and its
    /// attachment stay readable forever, and the fiscal identity is freed for a reissue.
    /// </summary>
    [HttpPost("{operationInvoiceId:guid}/void")]
    public async Task<IActionResult> Void(
        Guid requestId, Guid operationInvoiceId, [FromBody] VoidOperationInvoiceDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardMutationRole();
        if (roleProblem != null) return roleProblem;

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var invoice = await _context.OperationInvoices
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);
        if (invoice == null) return NotFound(Problem404("Fatura final não encontrada."));

        // Idempotent retry: voiding what is already VOIDED is the same void arriving twice.
        if (string.Equals(invoice.Status, RequestConstants.OperationInvoiceDocumentStatuses.Voided,
                StringComparison.OrdinalIgnoreCase))
        {
            return Ok(await ProjectAsync(invoice));
        }

        if (!OperationInvoiceLifecyclePolicy.CanVoid(invoice.Status))
        {
            var problem = new ProblemDetails
            {
                Title = "Fatura não pode ser anulada",
                Detail = "Só uma fatura ainda não validada pode ser anulada. Uma fatura validada " +
                         "corrige-se por substituição.",
                Status = 409
            };
            problem.Extensions["code"] = NotVoidableCode;
            return Conflict(problem);
        }

        var reason = Trimmed(dto.Reason);
        if (reason == null)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Reason"] = new[] { "Indique o motivo da anulação." }
            }));
        }

        var staleToken = ApplyConcurrencyToken(invoice, dto.RowVersion);
        if (staleToken != null) return staleToken;

        invoice.Status = RequestConstants.OperationInvoiceDocumentStatuses.Voided;
        invoice.VoidedAtUtc = DateTime.UtcNow;
        invoice.VoidedByUserId = CurrentUserId;
        invoice.VoidReason = reason;
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        invoice.UpdatedByUserId = CurrentUserId;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "FATURA_OPERACAO_ANULADA",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Fatura final {invoice.DocumentNumber} anulada. Motivo: {reason}",
            IdempotencyKey = PostPaymentIdempotencyKeys.OperationInvoiceVoided(requestId, invoice.Id),
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Ok(await ProjectAsync(invoice));
    }

    // ── Replace / supersession (Phase 2c) ───────────────────────────────────────────────────

    /// <summary>
    /// The only path out of VALIDATED, Finance-only: the original becomes REPLACEMENT_REQUESTED
    /// with the mandatory reason (the entity's documented use of <c>RejectionReason</c>) and the
    /// forward pointer, and the corrected invoice is created in PENDING_VALIDATION — one
    /// transaction, or nothing. The original's fiscal identity stops being effective in that same
    /// transaction, which is what lets the correction reuse it; every OTHER effective invoice
    /// still blocks it. Downstream financial evidence on the original (allocations,
    /// reconciliation snapshots — Phase 3 artifacts) blocks replacement outright: nothing is ever
    /// cascaded or transferred to the replacement.
    /// </summary>
    [HttpPost("{operationInvoiceId:guid}/replace")]
    public async Task<IActionResult> Replace(
        Guid requestId, Guid operationInvoiceId, [FromBody] ReplaceOperationInvoiceDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        // Finance-only (approved rule #12): replacing a VALIDATED financial document is a Finance
        // decision. SystemAdministrator follows the administrative can-act convention; the Buyer
        // does not reach this one.
        var roles = CurrentUserRoles;
        if (!roles.Contains(RoleConstants.Finance) &&
            !roles.Contains(RoleConstants.SystemAdministrator))
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Sem permissão",
                Detail = "Apenas o Financeiro pode substituir uma fatura validada.",
                Status = 403
            });
        }

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var original = await _context.OperationInvoices
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);
        if (original == null) return NotFound(Problem404("Fatura final não encontrada."));

        // Idempotent retry: the same replacement arriving twice (same original, same new file)
        // gets the existing correction back instead of "não pode ser substituída".
        if (string.Equals(original.Status,
                RequestConstants.OperationInvoiceDocumentStatuses.ReplacementRequested,
                StringComparison.OrdinalIgnoreCase) &&
            original.SupersededByOperationInvoiceId != null &&
            dto.AttachmentId.HasValue)
        {
            var existingReplacement = await _context.OperationInvoices.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == original.SupersededByOperationInvoiceId.Value);
            if (existingReplacement != null &&
                existingReplacement.AttachmentId == dto.AttachmentId.Value)
            {
                return Ok(await ProjectAsync(existingReplacement));
            }
        }

        if (!OperationInvoiceLifecyclePolicy.CanReplace(original.Status))
        {
            var problem = new ProblemDetails
            {
                Title = "Fatura não pode ser substituída",
                Detail = "Só uma fatura validada se corrige por substituição. Antes da validação, " +
                         "altere ou anule a fatura; uma fatura terminal permanece como está.",
                Status = 409
            };
            problem.Extensions["code"] = NotReplaceableCode;
            return Conflict(problem);
        }

        // Downstream evidence: Phase 3 artifacts that already answer against THIS invoice.
        var hasAllocations = await _context.OperationInvoiceAllocations
            .AnyAsync(a => a.OperationInvoiceId == original.Id);
        var hasReconciliations = await _context.OperationInvoiceReconciliations
            .AnyAsync(r => r.OperationInvoiceId == original.Id);
        if (hasAllocations || hasReconciliations)
        {
            var problem = new ProblemDetails
            {
                Title = "Substituição bloqueada por evidência financeira",
                Detail = "Esta fatura já tem alocações ou reconciliação registadas. A correção " +
                         "requer reconciliação pelo Financeiro; nada é transferido automaticamente.",
                Status = 409
            };
            problem.Extensions["code"] = DownstreamEvidenceCode;
            return Conflict(problem);
        }

        var reason = Trimmed(dto.ReplacementReason);
        if (reason == null)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["ReplacementReason"] = new[] { "Indique o motivo da substituição." }
            }));
        }

        var staleToken = ApplyConcurrencyToken(original, dto.RowVersion);
        if (staleToken != null) return staleToken;

        // ── The corrected invoice passes every Create gate ──
        var errors = ValidateNewInvoiceFields(dto);
        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));

        var (supplier, supplierProblem) = await GuardSupplierAsync(dto.SupplierId!.Value);
        if (supplierProblem != null) return supplierProblem;

        // A NEW file is mandatory: the original keeps its attachment forever, and the
        // claimed-check refuses any attempt to reuse it. The file-hash exclusion covers only the
        // original — re-uploading its identical content with a corrected header is legitimate.
        var attachmentProblem = await GuardAttachmentAsync(
            requestId, dto.AttachmentId!.Value, excludeInvoiceId: original.Id);
        if (attachmentProblem != null) return attachmentProblem;

        // The original stops being effective IN THIS TRANSACTION, so its identity is excluded —
        // and only its. Any other effective invoice still owns its identity.
        var duplicate = await FindEffectiveDuplicateAsync(
            dto.SupplierId!.Value, dto.DocumentNumber, dto.DocumentSeries,
            excludeInvoiceId: original.Id);
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

        // ── One transaction: old out, new in, one audit row ──
        var replacement = new OperationInvoice
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            AttachmentId = dto.AttachmentId!.Value,
            SupplierId = dto.SupplierId,
            SupplierTaxIdSnapshot = supplier!.TaxId,
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
            AmountsEnteredManually = dto.AmountsEnteredManually ?? true,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = CurrentUserId
        };
        _context.OperationInvoices.Add(replacement);

        original.Status = RequestConstants.OperationInvoiceDocumentStatuses.ReplacementRequested;
        original.RejectionReason = reason;   // the entity's documented replacement-reason slot
        original.SupersededByOperationInvoiceId = replacement.Id;
        original.UpdatedAtUtc = DateTime.UtcNow;
        original.UpdatedByUserId = CurrentUserId;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "FATURA_OPERACAO_SUBSTITUIDA",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Fatura final {original.DocumentNumber} substituída pela fatura " +
                      $"{replacement.DocumentNumber}. Motivo: {reason} " +
                      "A fatura corrigida aguarda validação do Financeiro.",
            IdempotencyKey = PostPaymentIdempotencyKeys.OperationInvoiceReplaced(
                original.Id, replacement.AttachmentId),
            CreatedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
        catch (DbUpdateException ex) when (IsAttachmentUniqueViolation(ex))
        {
            _context.ChangeTracker.Clear();
            return AttachmentClaimedConflict();
        }

        _logger.LogInformation(
            "Operation invoice {OriginalId} superseded by {ReplacementId} on request {RequestId}.",
            original.Id, replacement.Id, requestId);

        return Ok(await ProjectAsync(replacement));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Header-field validation shared by Create and Replace — one rulebook, one wording.</summary>
    private static Dictionary<string, string[]> ValidateNewInvoiceFields(SaveOperationInvoiceDto dto)
    {
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

        return errors;
    }

    /// <summary>Supplier existence + the internal-ALPLA rule — shared by Create, Update, Replace.</summary>
    private async Task<(Supplier? Supplier, IActionResult? Problem)> GuardSupplierAsync(int supplierId)
    {
        var supplier = await _context.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == supplierId);
        if (supplier == null)
        {
            return (null, BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["SupplierId"] = new[] { "Fornecedor selecionado não existe." }
            })));
        }

        var internalCompany = await _internalCompanies.ResolveSupplierAsync(supplierId);
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
            return (null, BadRequest(problem));
        }

        return (supplier, null);
    }

    /// <summary>Finance and Buyer mutate; SystemAdministrator per the administrative can-act
    /// convention — never bypassing the financial-integrity rules that follow.</summary>
    private IActionResult? GuardMutationRole()
    {
        var roles = CurrentUserRoles;
        if (roles.Contains(RoleConstants.Finance) ||
            roles.Contains(RoleConstants.Buyer) ||
            roles.Contains(RoleConstants.SystemAdministrator))
        {
            return null;
        }

        return StatusCode(403, new ProblemDetails
        {
            Title = "Sem permissão",
            Detail = "Apenas o Financeiro e o Comprador podem registar ou alterar faturas finais.",
            Status = 403
        });
    }

    /// <summary>The approved mutation window — WAITING_PO_CORRECTION included in the blocked set.</summary>
    private IActionResult? GuardMutableRequestStatus(Request request)
    {
        if (OperationInvoiceLifecyclePolicy.CanMutateInRequestStatus(request.Status?.Code)) return null;

        var problem = new ProblemDetails
        {
            Title = "Estado do pedido não permite alterações a faturas finais",
            Detail = "As faturas finais só podem ser alteradas depois da aprovação do pedido " +
                     "(incluindo após o pagamento) e enquanto o pedido não estiver concluído, " +
                     "rejeitado, cancelado ou em correção de P.O.",
            Status = 409
        };
        problem.Extensions["code"] = NotEditableCode;
        return Conflict(problem);
    }

    /// <summary>
    /// Attachment gate shared by Create, Update-with-replacement and Replace: exists, belongs to
    /// this request, TYPE_OPERATION_INVOICE, not deleted/voided, unclaimed (the unique index
    /// stays the structural backstop; this turns it into an answer the user can act on), and —
    /// Phase 2d — its FILE CONTENT is not already an effective operation invoice anywhere in the
    /// Portal: two RequestAttachment rows holding the same physical file are the same fiscal
    /// document, and the same fiscal file must not be recognized as a new debt twice.
    ///
    /// <para><paramref name="excludeInvoiceId"/> applies to the FILE-HASH check only (Update: an
    /// invoice may re-receive its own content; Replace: the original stops being effective in the
    /// same transaction). The CLAIM check never excludes anyone — a replacement must never reuse
    /// the original's attachment row.</para>
    /// </summary>
    private async Task<IActionResult?> GuardAttachmentAsync(
        Guid requestId, Guid attachmentId, Guid? excludeInvoiceId = null)
    {
        var attachment = await _context.RequestAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId);

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

        var claimed = await _context.OperationInvoices.AnyAsync(i => i.AttachmentId == attachmentId);
        if (claimed) return AttachmentClaimedConflict();

        var fileDuplicate = await FindEffectiveFileDuplicateAsync(attachment.FileHash, excludeInvoiceId);
        if (fileDuplicate != null)
        {
            var problem = new ProblemDetails
            {
                Title = "Ficheiro já registado",
                Detail = "Este ficheiro já corresponde a uma fatura final registada no Portal " +
                         $"({fileDuplicate.DocumentNumber ?? "sem número"}).",
                Status = 409
            };
            problem.Extensions["code"] = FileDuplicateErrorCode;
            problem.Extensions["existingOperationInvoiceId"] = fileDuplicate.Id;
            problem.Extensions["existingRequestId"] = fileDuplicate.RequestId;
            return Conflict(problem);
        }

        return null;
    }

    /// <summary>
    /// The same physical file (by hash), already claimed by an EFFECTIVE invoice anywhere in the
    /// Portal — the approved global scope, same as the business identity. Terminal invoices
    /// release their file exactly as they release their fiscal identity.
    /// </summary>
    private async Task<OperationInvoice?> FindEffectiveFileDuplicateAsync(
        string? fileHash, Guid? excludeInvoiceId = null)
    {
        if (string.IsNullOrWhiteSpace(fileHash)) return null;

        var candidates = await _context.OperationInvoices.AsNoTracking()
            .Join(_context.RequestAttachments.Where(a => a.FileHash == fileHash),
                  i => i.AttachmentId, a => a.Id, (i, a) => i)
            .ToListAsync();

        return candidates.FirstOrDefault(i =>
            i.Id != excludeInvoiceId &&
            OperationInvoiceLifecyclePolicy.IsEffectiveForDuplicateCheck(i.Status));
    }

    private IActionResult AttachmentClaimedConflict()
    {
        var problem = new ProblemDetails
        {
            Title = "Anexo já utilizado",
            Detail = "Este ficheiro já está registado como uma fatura final.",
            Status = 409
        };
        problem.Extensions["code"] = AttachmentClaimedCode;
        return Conflict(problem);
    }

    /// <summary>
    /// The database race the application checks cannot close: two concurrent creates passing the
    /// claim check and hitting UX_OperationInvoice_AttachmentId. Mapped to the SAME typed
    /// conflict; anything else stays what it is — an unrelated DB failure must never be dressed
    /// up as a duplicate.
    /// </summary>
    public static bool IsAttachmentUniqueViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains(
            "UX_OperationInvoice_AttachmentId", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Concurrency token: an explicit staleness precheck against the loaded row (deterministic on
    /// every provider), plus the OriginalValue pass-through so the database still guards the race
    /// between this load and the save — the PaymentSourceDocuments convention, hardened.
    /// </summary>
    private IActionResult? ApplyConcurrencyToken(OperationInvoice invoice, byte[]? rowVersion)
    {
        if (rowVersion == null || rowVersion.Length == 0) return null;

        if (!rowVersion.SequenceEqual(invoice.RowVersion ?? Array.Empty<byte>()))
            return ConcurrencyConflict();

        _context.Entry(invoice).Property(i => i.RowVersion).OriginalValue = rowVersion;
        return null;
    }

    private IActionResult ConcurrencyConflict()
    {
        _context.ChangeTracker.Clear();
        var problem = new ProblemDetails
        {
            Title = "Fatura alterada entretanto",
            Detail = "Esta fatura foi alterada por outra pessoa desde que a abriu. Recarregue " +
                     "para ver o estado atual antes de repetir a alteração.",
            Status = 409
        };
        problem.Extensions["code"] = ConcurrencyCode;
        return Conflict(problem);
    }

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
        int supplierId, string? documentNumber, string? documentSeries,
        Guid? excludeInvoiceId = null)
    {
        var number = documentNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number)) return null;

        var series = documentSeries?.Trim() ?? string.Empty;

        var candidates = await _context.OperationInvoices
            .AsNoTracking()
            .Where(i => i.SupplierId == supplierId)
            .ToListAsync();

        return candidates.FirstOrDefault(i =>
            i.Id != excludeInvoiceId &&
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
