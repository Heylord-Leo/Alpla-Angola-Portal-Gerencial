using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Validation;
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
    private readonly IOperationInvoiceCoverageService _coverage;

    public OperationInvoicesController(
        ApplicationDbContext context,
        ILogger<OperationInvoicesController> logger,
        IInternalCompanyGuard internalCompanies,
        IOperationInvoiceCoverageService coverage) : base(context)
    {
        _logger = logger;
        _internalCompanies = internalCompanies;
        _coverage = coverage;
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
    public const string NotValidatableCode = "OPERATION_INVOICE_NOT_VALIDATABLE";
    public const string NotRejectableCode = "OPERATION_INVOICE_NOT_REJECTABLE";

    // ── Phase 3A allocation codes ──
    public const string AllocationNotEditableCode = "OI_ALLOC_NOT_EDITABLE";
    public const string AllocationGroupInvalidCode = "OI_ALLOC_GROUP_INVALID";
    public const string AllocationSupplierMismatchCode = "OI_ALLOC_SUPPLIER_MISMATCH";
    public const string AllocationCurrencyMismatchCode = "OI_ALLOC_CURRENCY_MISMATCH";
    public const string AllocationInvoiceOverCode = "OI_ALLOC_INVOICE_OVER";
    public const string AllocationGroupOverCode = "OI_ALLOC_GROUP_OVER";
    public const string ValidateAllocationIncompleteCode = "OI_VALIDATE_ALLOCATION_INCOMPLETE";
    public const string ValidateDivergenceRequiredCode = "OI_VALIDATE_DIVERGENCE_REQUIRED";

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

        // ── Header-drift guard (Phase 3A): once allocations exist, the MERGED header must still
        // satisfy the same hard identity rules the allocation write enforced — an edit cannot
        // invalidate evidence that was accepted against the old header. Same codes, same rules.
        var allocationIntegrityProblem = await GuardAllocatedGroupsIntegrityAsync(
            requestId, invoice.Id, newSupplierId, newCurrency);
        if (allocationIntegrityProblem != null) return allocationIntegrityProblem;

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

        // Phase 3A: a void may retire pending-decision coverage — re-derive the allocated groups
        // in this same transaction. A voided invoice never counted as effective, so no forced
        // touch is needed: only a real status change matters here.
        var voidedGroupIds = await _context.OperationInvoiceAllocations
            .Where(a => a.OperationInvoiceId == invoice.Id)
            .Select(a => a.RequestPoGroupId)
            .Distinct()
            .ToListAsync();
        if (voidedGroupIds.Count > 0)
        {
            var coverageChanges = await _coverage.RederiveAsync(voidedGroupIds, forceGroupTouch: false);
            AddGroupStatusHistories(request, coverageChanges);
        }

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

    // ── Finance validation / rejection (Phase 2e) ───────────────────────────────────────────

    /// <summary>
    /// The Finance decision that makes an invoice trustworthy — NOT the act that makes it cover
    /// anything: coverage arrives only with Phase 3 allocation. Every integrity boundary re-runs
    /// against the PERSISTED row, because validation is the last gate before this document can
    /// ever carry financial weight, and nothing valid at create time is trusted to still be valid.
    /// </summary>
    [HttpPost("{operationInvoiceId:guid}/validate")]
    public async Task<IActionResult> Validate(
        Guid requestId, Guid operationInvoiceId, [FromBody] ValidateOperationInvoiceDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardFinanceRole();
        if (roleProblem != null) return roleProblem;

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var invoice = await _context.OperationInvoices
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);
        if (invoice == null) return NotFound(Problem404("Fatura final não encontrada."));

        // Idempotent retry: validating what is already VALIDATED is the same decision arriving
        // twice — the Phase 2d convention. A CONFLICTING decision (validating a REJECTED invoice)
        // is never idempotent success; it falls through to the lifecycle gate below.
        if (string.Equals(invoice.Status,
                RequestConstants.OperationInvoiceDocumentStatuses.Validated,
                StringComparison.OrdinalIgnoreCase))
        {
            return Ok(await ProjectAsync(invoice));
        }

        if (!OperationInvoiceLifecyclePolicy.CanValidate(invoice.Status))
        {
            var problem = new ProblemDetails
            {
                Title = "Fatura não pode ser validada",
                Detail = "Só uma fatura a aguardar validação pode ser validada.",
                Status = 409
            };
            problem.Extensions["code"] = NotValidatableCode;
            return Conflict(problem);
        }

        var staleToken = ApplyConcurrencyToken(invoice, dto.RowVersion);
        if (staleToken != null) return staleToken;

        // ── Header integrity, re-read from the persisted row ──
        var errors = new Dictionary<string, string[]>();
        if (invoice.SupplierId is null or <= 0)
            errors["SupplierId"] = new[] { "A fatura não tem fornecedor." };
        if (string.IsNullOrWhiteSpace(invoice.DocumentNumber))
            errors["DocumentNumber"] = new[] { "A fatura não tem número do documento." };
        if (invoice.DocumentDate == null)
            errors["DocumentDate"] = new[] { "A fatura não tem data do documento." };
        if (string.IsNullOrWhiteSpace(invoice.Currency))
            errors["Currency"] = new[] { "A fatura não tem moeda." };
        if (invoice.GrossAmount is null or <= 0)
            errors["GrossAmount"] = new[] { "A fatura não tem um total válido (maior que zero)." };

        if (invoice.NetAmount.HasValue != invoice.TaxAmount.HasValue)
        {
            errors["NetAmount"] = new[]
                { "A fatura tem valor líquido e imposto incompletos — indique ambos ou nenhum." };
        }
        else if (invoice.NetAmount.HasValue && invoice.GrossAmount is > 0 &&
                 Math.Abs(invoice.GrossAmount.Value -
                          (invoice.NetAmount.Value + invoice.TaxAmount!.Value)) >
                 OperationInvoiceTolerance.For(invoice.GrossAmount.Value))
        {
            errors["GrossAmount"] = new[]
            {
                "O total da fatura não corresponde à soma do valor líquido com o imposto " +
                "(fora da tolerância)."
            };
        }
        // DueDate stays optional — an approved business decision; its absence never blocks.

        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));

        // ── Supplier integrity: the internal-ALPLA rule holds at the final boundary too ──
        var (_, supplierProblem) = await GuardSupplierAsync(invoice.SupplierId!.Value);
        if (supplierProblem != null) return supplierProblem;

        // ── Attachment: still present, still this request's, still the right kind, still active ──
        var attachment = await _context.RequestAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == invoice.AttachmentId);
        if (attachment == null || attachment.RequestId != requestId ||
            !string.Equals(attachment.AttachmentTypeCode, RequestAttachment.TYPE_OPERATION_INVOICE,
                StringComparison.OrdinalIgnoreCase) ||
            attachment.IsDeleted || attachment.VoidedAtUtc != null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O ficheiro da fatura já não é válido. Substitua o anexo antes de validar.",
                Status = 400
            });
        }

        // ── The duplicate race (Phase 2e rule): another effective invoice may have claimed this
        // identity or this file since creation. Validation must never mint a second effective
        // invoice for either.
        var duplicate = await FindEffectiveDuplicateAsync(
            invoice.SupplierId!.Value, invoice.DocumentNumber, invoice.DocumentSeries,
            excludeInvoiceId: invoice.Id);
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

        var fileDuplicate = await FindEffectiveFileDuplicateAsync(
            attachment.FileHash, excludeInvoiceId: invoice.Id);
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

        // ── Phase 3A allocation gate: Finance validates a FULLY-ATTRIBUTED document. The sum of
        // allocations must reconcile to the invoice gross within tolerance — no unexplained
        // balance may quietly disappear, and an unallocated invoice with a real gross cannot
        // become effective coverage of nothing. ──
        var allocations = await _context.OperationInvoiceAllocations
            .Where(a => a.OperationInvoiceId == invoice.Id)
            .ToListAsync();

        var grossForGate = invoice.GrossAmount!.Value;
        var gateTolerance = OperationInvoiceTolerance.For(grossForGate);
        var allocatedTotal = allocations.Sum(a => a.AllocatedGrossAmount);
        if (Math.Abs(grossForGate - allocatedTotal) > gateTolerance)
        {
            var problem = new ProblemDetails
            {
                Title = "Alocações incompletas",
                Detail = allocations.Count == 0
                    ? "A fatura não tem alocações. Distribua o total da fatura pelos grupos antes de validar."
                    : "A soma das alocações não corresponde ao total da fatura (fora da tolerância). " +
                      "Nenhum saldo da fatura pode ficar por explicar.",
                Status = 409
            };
            problem.Extensions["code"] = ValidateAllocationIncompleteCode;
            problem.Extensions["invoiceGross"] = grossForGate;
            problem.Extensions["allocatedTotal"] = allocatedTotal;
            problem.Extensions["tolerance"] = gateTolerance;
            return Conflict(problem);
        }

        // ── Belt-and-braces identity recheck (Phase 3A drift fix): the persisted header must
        // STILL satisfy the hard integrity rules every allocation was accepted under. Update
        // already refuses drifting edits; this recheck is the last line against historical rows,
        // manual database drift and any future path that bypasses Update. A mismatch FAILS the
        // validation — the reconciliation snapshot is evidence of a valid comparison, never a
        // bypass mechanism.
        var allocationIntegrityProblem = await GuardAllocatedGroupsIntegrityAsync(
            requestId, invoice.Id, invoice.SupplierId, invoice.Currency);
        if (allocationIntegrityProblem != null) return allocationIntegrityProblem;

        // ── Over-expected groups need the EXPLICIT Finance decision (approved rule #13):
        // an acceptance entry with a meaningful justification per affected group — never
        // inferred, never auto-accepted, never silently capped. ──
        var gateGroupIds = allocations.Select(a => a.RequestPoGroupId).Distinct().ToList();
        var gateGroups = await _context.RequestPoGroups
            .Where(g => gateGroupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id);

        var gateValidatedByOthers = await _context.OperationInvoiceAllocations.AsNoTracking()
            .Where(a => gateGroupIds.Contains(a.RequestPoGroupId) && a.OperationInvoiceId != invoice.Id)
            .Join(_context.OperationInvoices.Where(i =>
                    i.Status == RequestConstants.OperationInvoiceDocumentStatuses.Validated &&
                    i.SupersededByOperationInvoiceId == null),
                a => a.OperationInvoiceId, i => i.Id,
                (a, i) => new { a.RequestPoGroupId, a.AllocatedGrossAmount })
            .GroupBy(a => a.RequestPoGroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.AllocatedGrossAmount) })
            .ToDictionaryAsync(x => x.GroupId, x => x.Total);

        var acceptancesByGroup = (dto.DivergenceAcceptances ?? new List<OperationInvoiceDivergenceAcceptanceDto>())
            .GroupBy(a => a.RequestPoGroupId)
            .ToDictionary(g => g.Key, g => g.Last());

        var divergenceByGroup = new Dictionary<Guid, (decimal Residual, string Justification)>();
        foreach (var allocation in allocations)
        {
            var group = gateGroups[allocation.RequestPoGroupId];
            if (group.ExpectedOperationInvoiceTotal is not > 0) continue;

            var expected = group.ExpectedOperationInvoiceTotal.Value;
            var groupTolerance = OperationInvoiceTolerance.For(expected);
            var coveredBefore = gateValidatedByOthers.TryGetValue(group.Id, out var v) ? v : 0m;
            var residual = coveredBefore + allocation.AllocatedGrossAmount - expected;
            if (residual <= groupTolerance) continue;

            acceptancesByGroup.TryGetValue(group.Id, out var acceptance);
            if (acceptance is not { Accepted: true } ||
                !ReconciliationJustificationValidator.IsValid(acceptance.Justification, out _))
            {
                var problem = new ProblemDetails
                {
                    Title = "Divergência requer decisão explícita",
                    Detail = $"A alocação leva o grupo {group.SupplierNameSnapshot ?? group.Id.ToString()} " +
                             $"acima do total esperado ({expected:N2} {group.CurrencyCode}). A validação exige " +
                             "a aceitação explícita da divergência com justificativa (mín. 20 caracteres significativos).",
                    Status = 409
                };
                problem.Extensions["code"] = ValidateDivergenceRequiredCode;
                problem.Extensions["requestPoGroupId"] = group.Id;
                problem.Extensions["expectedTotal"] = expected;
                problem.Extensions["currentValidated"] = coveredBefore;
                problem.Extensions["allocatedAmount"] = allocation.AllocatedGrossAmount;
                problem.Extensions["tolerance"] = groupTolerance;
                return Conflict(problem);
            }

            divergenceByGroup[group.Id] = (residual, acceptance.Justification!.Trim());
        }

        // ── The decision. Nothing else moves: amounts, attachment, snapshots, chain, void
        // fields all stay exactly as Finance saw them.
        invoice.Status = RequestConstants.OperationInvoiceDocumentStatuses.Validated;
        invoice.ValidatedAtUtc = DateTime.UtcNow;
        invoice.ValidatedByUserId = CurrentUserId;
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        invoice.UpdatedByUserId = CurrentUserId;

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "FATURA_OPERACAO_VALIDADA",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Fatura final {invoice.DocumentNumber} validada pelo Financeiro." +
                      (divergenceByGroup.Count > 0
                          ? $" Divergência aceite em {divergenceByGroup.Count} grupo(s)."
                          : string.Empty),
            IdempotencyKey = PostPaymentIdempotencyKeys.OperationInvoiceValidated(requestId, invoice.Id),
            CreatedAtUtc = DateTime.UtcNow
        });

        // ── Phase 3A: one IMMUTABLE reconciliation snapshot per allocation, written only on this
        // transition (the idempotent VALIDATED retry above never reaches here, so an exact retry
        // can never duplicate a snapshot). ──
        var companyName = await _context.Companies.AsNoTracking()
            .Where(c => c.Id == request.CompanyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();

        foreach (var allocation in allocations)
        {
            var group = gateGroups[allocation.RequestPoGroupId];
            var expected = group.ExpectedOperationInvoiceTotal ?? 0m;
            var coveredBefore = gateValidatedByOthers.TryGetValue(group.Id, out var v) ? v : 0m;
            var hasDivergence = divergenceByGroup.TryGetValue(group.Id, out var divergence);

            _context.OperationInvoiceReconciliations.Add(new OperationInvoiceReconciliation
            {
                Id = Guid.NewGuid(),
                RequestPoGroupId = group.Id,
                OperationInvoiceAttachmentId = invoice.AttachmentId,
                OperationInvoiceId = invoice.Id,
                OperationInvoiceAllocationId = allocation.Id,
                NifMatched = !string.IsNullOrWhiteSpace(invoice.SupplierTaxIdSnapshot) &&
                             !string.IsNullOrWhiteSpace(group.SupplierNifSnapshot) &&
                             string.Equals(invoice.SupplierTaxIdSnapshot.Trim(), group.SupplierNifSnapshot.Trim(),
                                 StringComparison.OrdinalIgnoreCase),
                CompanyMatched = !string.IsNullOrWhiteSpace(invoice.BilledCompanyNameRead) &&
                                 !string.IsNullOrWhiteSpace(companyName) &&
                                 invoice.BilledCompanyNameRead.Contains(companyName!, StringComparison.OrdinalIgnoreCase),
                SupplierMatched = invoice.SupplierId.HasValue && group.SupplierId.HasValue &&
                                  invoice.SupplierId == group.SupplierId,
                CurrencyMatched = string.Equals(invoice.Currency, group.CurrencyCode, StringComparison.OrdinalIgnoreCase),
                AllocatedTotal = allocation.AllocatedGrossAmount,
                CumulativeValidatedTotalBefore = coveredBefore,
                ExpectedTotalAtComparison = expected,
                BaselineTotal = group.TotalAmount,
                InvoiceTotal = invoice.GrossAmount!.Value,
                ResidualVariance = expected > 0 ? coveredBefore + allocation.AllocatedGrossAmount - expected : 0m,
                ToleranceApplied = OperationInvoiceTolerance.For(expected),
                DivergenceDetected = hasDivergence,
                DivergenceAccepted = hasDivergence,
                DivergenceJustification = hasDivergence ? divergence.Justification : null,
                ReconciliationDataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    invoice.DocumentNumber,
                    invoice.DocumentSeries,
                    invoice.Currency,
                    invoice.NetAmount,
                    invoice.TaxAmount,
                    invoice.GrossAmount,
                    AllocationSequence = allocation.SequenceNumber,
                    GroupSupplier = group.SupplierNameSnapshot,
                    GroupCurrency = group.CurrencyCode
                }),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = CurrentUserId
            });
        }

        // ── Effective coverage moved: re-derive every allocated group WITH the forced row touch,
        // so a concurrent effective-coverage writer conflicts instead of double-covering. ──
        var validateCoverageChanges = await _coverage.RederiveAsync(gateGroupIds, forceGroupTouch: true);
        AddGroupStatusHistories(request, validateCoverageChanges);

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

    /// <summary>
    /// The other Finance decision: the uploaded document is not accepted. Terminal — the invoice
    /// and its attachment stay readable forever, and both the fiscal identity and the file hash
    /// are released (approved rule: rejection may concern metadata, not the physical file, so the
    /// same file may legitimately return).
    /// </summary>
    [HttpPost("{operationInvoiceId:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid requestId, Guid operationInvoiceId, [FromBody] RejectOperationInvoiceDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardFinanceRole();
        if (roleProblem != null) return roleProblem;

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var invoice = await _context.OperationInvoices
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);
        if (invoice == null) return NotFound(Problem404("Fatura final não encontrada."));

        // Idempotent retry: rejecting what is already REJECTED returns the persisted decision —
        // including its recorded reason, never a rewritten one. Rejecting a VALIDATED invoice is
        // a CONFLICTING decision and falls through to the lifecycle gate.
        if (string.Equals(invoice.Status,
                RequestConstants.OperationInvoiceDocumentStatuses.Rejected,
                StringComparison.OrdinalIgnoreCase))
        {
            return Ok(await ProjectAsync(invoice));
        }

        if (!OperationInvoiceLifecyclePolicy.CanReject(invoice.Status))
        {
            var problem = new ProblemDetails
            {
                Title = "Fatura não pode ser rejeitada",
                Detail = "Só uma fatura a aguardar validação pode ser rejeitada. Uma fatura " +
                         "validada corrige-se por substituição.",
                Status = 409
            };
            problem.Extensions["code"] = NotRejectableCode;
            return Conflict(problem);
        }

        var reason = Trimmed(dto.Reason);
        if (reason == null)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Reason"] = new[] { "Indique o motivo da rejeição." }
            }));
        }

        var staleToken = ApplyConcurrencyToken(invoice, dto.RowVersion);
        if (staleToken != null) return staleToken;

        invoice.Status = RequestConstants.OperationInvoiceDocumentStatuses.Rejected;
        invoice.RejectionReason = reason;
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        invoice.UpdatedByUserId = CurrentUserId;
        // Deliberately untouched: Validated* stamps (this was never validated), the attachment
        // (never voided by a rejection), and the invoice row itself (readable forever).

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = "FATURA_OPERACAO_REJEITADA",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Fatura final {invoice.DocumentNumber} rejeitada pelo Financeiro. " +
                      $"Motivo: {reason}",
            IdempotencyKey = PostPaymentIdempotencyKeys.OperationInvoiceRejected(requestId, invoice.Id),
            CreatedAtUtc = DateTime.UtcNow
        });

        // Phase 3A: a rejection drops the invoice's allocations out of the pending-decision set —
        // re-derive the allocated groups in this same transaction. Rejected allocations were never
        // effective, so no forced touch: only a real status change matters here.
        var rejectedGroupIds = await _context.OperationInvoiceAllocations
            .Where(a => a.OperationInvoiceId == invoice.Id)
            .Select(a => a.RequestPoGroupId)
            .Distinct()
            .ToListAsync();
        if (rejectedGroupIds.Count > 0)
        {
            var coverageChanges = await _coverage.RederiveAsync(rejectedGroupIds, forceGroupTouch: false);
            AddGroupStatusHistories(request, coverageChanges);
        }

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

    // ── Phase 3A: Allocations ───────────────────────────────────────────────────────────────

    /// <summary>The invoice's allocation set, with server-derived effectiveness per row.</summary>
    [HttpGet("{operationInvoiceId:guid}/allocations")]
    public async Task<ActionResult<List<OperationInvoiceAllocationDto>>> GetAllocations(
        Guid requestId, Guid operationInvoiceId)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var invoice = await _context.OperationInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);
        if (invoice == null) return NotFound(Problem404("Fatura final não encontrada."));

        return Ok(await ProjectAllocationsAsync(invoice));
    }

    /// <summary>
    /// Atomic replace-set of one invoice's allocations (Phase 3A). The payload IS the resulting
    /// set; the server adds/updates/removes rows to match it, validates the ENTIRE result before
    /// mutating anything, re-derives every touched group's aggregate in the same transaction, and
    /// commits with ONE SaveChanges. An identical payload is an idempotent no-op.
    ///
    /// <para>Effectiveness is never decided here: drafts on an UPLOADED/PENDING_VALIDATION
    /// invoice count only toward pending coverage; validation is what makes them effective.</para>
    /// </summary>
    [HttpPut("{operationInvoiceId:guid}/allocations")]
    public async Task<IActionResult> SaveAllocations(
        Guid requestId, Guid operationInvoiceId, [FromBody] SaveOperationInvoiceAllocationsDto dto)
    {
        var request = await LoadScopedRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());

        var roleProblem = GuardMutationRole();
        if (roleProblem != null) return roleProblem;

        var statusProblem = GuardMutableRequestStatus(request);
        if (statusProblem != null) return statusProblem;

        var invoice = await _context.OperationInvoices
            .Include(i => i.Allocations)
            .FirstOrDefaultAsync(i => i.Id == operationInvoiceId && i.RequestId == requestId);
        if (invoice == null) return NotFound(Problem404("Fatura final não encontrada."));

        // Drafting window: only while the document awaits the Finance decision. A VALIDATED
        // invoice's allocations are immutable (correct via Reject/Replace); a terminal invoice
        // keeps its historical drafts untouched.
        if (!RequestConstants.OperationInvoiceDocumentStatuses.IsAwaitingDecision(invoice.Status))
        {
            var problem = new ProblemDetails
            {
                Title = "Alocações não podem ser alteradas",
                Detail = "As alocações só podem ser editadas enquanto a fatura aguarda validação. " +
                         "Numa fatura validada, a correção passa por rejeição/substituição.",
                Status = 409
            };
            problem.Extensions["code"] = AllocationNotEditableCode;
            return Conflict(problem);
        }

        var staleToken = ApplyConcurrencyToken(invoice, dto.RowVersion);
        if (staleToken != null) return staleToken;

        // ── Payload shape ──
        var items = dto.Allocations ?? new List<SaveOperationInvoiceAllocationItemDto>();
        var errors = new Dictionary<string, string[]>();

        if (items.GroupBy(i => i.RequestPoGroupId).Any(g => g.Count() > 1))
            errors["Allocations"] = new[] { "O mesmo grupo aparece mais de uma vez — uma fatura atinge um grupo com no máximo uma alocação." };

        if (invoice.SupplierId is null or <= 0)
            errors["SupplierId"] = new[] { "A fatura não tem fornecedor — defina-o antes de alocar." };
        if (string.IsNullOrWhiteSpace(invoice.Currency))
            errors["Currency"] = new[] { "A fatura não tem moeda — defina-a antes de alocar." };
        if (invoice.GrossAmount is null or <= 0)
            errors["GrossAmount"] = new[] { "A fatura não tem um total válido — defina-o antes de alocar." };

        for (var n = 0; n < items.Count; n++)
        {
            var item = items[n];
            if (item.AllocatedGrossAmount <= 0)
                errors[$"Allocations[{n}].AllocatedGrossAmount"] = new[] { "O valor alocado deve ser maior que zero." };

            // Components travel together and must reconcile to the gross — nothing disappears.
            if (item.AllocatedNetAmount != 0 || item.AllocatedTaxAmount != 0)
            {
                var difference = Math.Abs(item.AllocatedGrossAmount - (item.AllocatedNetAmount + item.AllocatedTaxAmount));
                if (difference > OperationInvoiceTolerance.For(item.AllocatedGrossAmount))
                    errors[$"Allocations[{n}].AllocatedGrossAmount"] = new[] { "Líquido + imposto não corresponde ao valor alocado (fora da tolerância)." };
            }
        }

        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));

        // ── Group integrity: identity + obligation eligibility, all validated BEFORE mutating ──
        var groupIds = items.Select(i => i.RequestPoGroupId).ToList();
        var groups = await _context.RequestPoGroups
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id);

        // Validated coverage each group already holds from OTHER invoices — the number the
        // over-expected rule is measured against (drafts elsewhere never pre-consume budget).
        var validatedByOthers = await _context.OperationInvoiceAllocations.AsNoTracking()
            .Where(a => groupIds.Contains(a.RequestPoGroupId) && a.OperationInvoiceId != invoice.Id)
            .Join(_context.OperationInvoices.Where(i =>
                    i.Status == RequestConstants.OperationInvoiceDocumentStatuses.Validated &&
                    i.SupersededByOperationInvoiceId == null),
                a => a.OperationInvoiceId, i => i.Id,
                (a, i) => new { a.RequestPoGroupId, a.AllocatedGrossAmount })
            .GroupBy(a => a.RequestPoGroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Sum(x => x.AllocatedGrossAmount) })
            .ToDictionaryAsync(x => x.GroupId, x => x.Total);

        var isFinanceActor = CurrentUserRoles.Contains(RoleConstants.Finance) ||
                             CurrentUserRoles.Contains(RoleConstants.SystemAdministrator);

        foreach (var item in items)
        {
            if (!groups.TryGetValue(item.RequestPoGroupId, out var group) || group.RequestId != requestId)
            {
                var problem = new ProblemDetails
                {
                    Title = "Grupo inválido",
                    Detail = "O grupo indicado não pertence ao pedido desta fatura — uma fatura só cobre grupos do seu próprio pedido.",
                    Status = 409
                };
                problem.Extensions["code"] = AllocationGroupInvalidCode;
                problem.Extensions["requestPoGroupId"] = item.RequestPoGroupId;
                return Conflict(problem);
            }

            // Obligation eligibility: classified, owing an invoice, in an accepting state, and not
            // parked in PO correction. A pre-Final PENDING group is UNCLASSIFIED and fails here.
            if (!group.RequiresOperationInvoice ||
                !RequestConstants.OperationInvoiceStatuses.AcceptsUploadIn(group.OperationInvoiceStatus) ||
                string.Equals(group.Status, RequestConstants.PoGroupStatuses.WaitingPoCorrection, StringComparison.OrdinalIgnoreCase))
            {
                var problem = new ProblemDetails
                {
                    Title = "Grupo não elegível para alocação",
                    Detail = "Este grupo não está num estado que aceite alocação de fatura final " +
                             $"(obrigação: {group.OperationInvoiceStatus}; grupo: {group.Status}).",
                    Status = 409
                };
                problem.Extensions["code"] = AllocationGroupInvalidCode;
                problem.Extensions["requestPoGroupId"] = group.Id;
                return Conflict(problem);
            }

            // Frozen-evidence identity: the group's snapshot vs the invoice's registered facts.
            if (group.SupplierId.HasValue && invoice.SupplierId.HasValue && group.SupplierId != invoice.SupplierId)
            {
                var problem = new ProblemDetails
                {
                    Title = "Fornecedor não corresponde",
                    Detail = $"A fatura é do fornecedor {invoice.SupplierId}, mas o grupo pertence a " +
                             $"{group.SupplierNameSnapshot ?? group.SupplierId.ToString()}.",
                    Status = 409
                };
                problem.Extensions["code"] = AllocationSupplierMismatchCode;
                problem.Extensions["requestPoGroupId"] = group.Id;
                return Conflict(problem);
            }

            if (!string.IsNullOrWhiteSpace(group.CurrencyCode) &&
                !string.Equals(group.CurrencyCode, invoice.Currency, StringComparison.OrdinalIgnoreCase))
            {
                var problem = new ProblemDetails
                {
                    Title = "Moeda não corresponde",
                    Detail = $"A fatura está em {invoice.Currency}, mas o grupo está em {group.CurrencyCode}.",
                    Status = 409
                };
                problem.Extensions["code"] = AllocationCurrencyMismatchCode;
                problem.Extensions["requestPoGroupId"] = group.Id;
                return Conflict(problem);
            }

            // ── Over-expected rule (approved #13). Buyer: hard block. Finance/SysAdmin: allowed
            // only as an explicit divergence candidate — meaningful explanation required NOW, and
            // the acceptance decision still belongs to validation. Nothing is ever capped. ──
            if (group.ExpectedOperationInvoiceTotal is > 0)
            {
                var expected = group.ExpectedOperationInvoiceTotal.Value;
                var tolerance = OperationInvoiceTolerance.For(expected);
                var covered = validatedByOthers.TryGetValue(group.Id, out var v) ? v : 0m;

                if (covered + item.AllocatedGrossAmount > expected + tolerance)
                {
                    if (!isFinanceActor)
                    {
                        var problem = new ProblemDetails
                        {
                            Title = "Alocação excede o valor esperado do grupo",
                            Detail = "A alocação ultrapassa o total esperado do grupo. Apenas o " +
                                     "Financeiro pode registar uma divergência acima do esperado.",
                            Status = 409
                        };
                        problem.Extensions["code"] = AllocationGroupOverCode;
                        problem.Extensions["requestPoGroupId"] = group.Id;
                        problem.Extensions["expectedTotal"] = expected;
                        problem.Extensions["currentValidated"] = covered;
                        problem.Extensions["attemptedAllocated"] = item.AllocatedGrossAmount;
                        problem.Extensions["tolerance"] = tolerance;
                        return Conflict(problem);
                    }

                    if (!ReconciliationJustificationValidator.IsValid(item.Notes, out var notesError))
                    {
                        return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                        {
                            [$"Allocations[{groupIds.IndexOf(group.Id)}].Notes"] = new[]
                            {
                                "A alocação excede o total esperado do grupo — explique a divergência " +
                                $"nas observações da alocação. {notesError}"
                            }
                        }));
                    }
                }
            }
        }

        // ── Invoice-side balance: the document cannot distribute more than it is worth ──
        var attemptedTotal = items.Sum(i => i.AllocatedGrossAmount);
        var invoiceGross = invoice.GrossAmount!.Value;
        var invoiceTolerance = OperationInvoiceTolerance.For(invoiceGross);
        if (attemptedTotal > invoiceGross + invoiceTolerance)
        {
            var problem = new ProblemDetails
            {
                Title = "Alocações excedem o total da fatura",
                Detail = "A soma das alocações ultrapassa o total da própria fatura.",
                Status = 409
            };
            problem.Extensions["code"] = AllocationInvoiceOverCode;
            problem.Extensions["invoiceGross"] = invoiceGross;
            problem.Extensions["currentAllocated"] = invoice.Allocations.Sum(a => a.AllocatedGrossAmount);
            problem.Extensions["attemptedAllocated"] = attemptedTotal;
            problem.Extensions["tolerance"] = invoiceTolerance;
            return Conflict(problem);
        }

        // ── Idempotency: an identical resulting set is the same request arriving twice ──
        var existingByGroup = invoice.Allocations.ToDictionary(a => a.RequestPoGroupId);
        var isNoOp = items.Count == existingByGroup.Count && items.All(item =>
            existingByGroup.TryGetValue(item.RequestPoGroupId, out var existing) &&
            existing.AllocatedNetAmount == item.AllocatedNetAmount &&
            existing.AllocatedTaxAmount == item.AllocatedTaxAmount &&
            existing.AllocatedGrossAmount == item.AllocatedGrossAmount &&
            string.Equals(existing.Notes ?? string.Empty, (item.Notes ?? string.Empty).Trim(), StringComparison.Ordinal));
        if (isNoOp) return Ok(await ProjectAllocationsAsync(invoice));

        var hadAllocations = invoice.Allocations.Count > 0;
        var previousByGroup = invoice.Allocations
            .ToDictionary(a => a.RequestPoGroupId, a => a.AllocatedGrossAmount);

        // ── Apply: remove missing, update changed, add new ──
        var wantedGroupIds = items.Select(i => i.RequestPoGroupId).ToHashSet();
        foreach (var stale in invoice.Allocations.Where(a => !wantedGroupIds.Contains(a.RequestPoGroupId)).ToList())
        {
            invoice.Allocations.Remove(stale);
            _context.OperationInvoiceAllocations.Remove(stale);
        }

        // Per-group sequence numbers: the Nth allocation THAT group ever received.
        var touchedGroupIds = wantedGroupIds.Union(previousByGroup.Keys).ToList();
        var maxSequenceByGroup = await _context.OperationInvoiceAllocations.AsNoTracking()
            .Where(a => touchedGroupIds.Contains(a.RequestPoGroupId))
            .GroupBy(a => a.RequestPoGroupId)
            .Select(g => new { GroupId = g.Key, Max = g.Max(a => a.SequenceNumber) })
            .ToDictionaryAsync(x => x.GroupId, x => x.Max);

        var nowUtc = DateTime.UtcNow;
        foreach (var item in items)
        {
            var trimmedNotes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();
            if (existingByGroup.TryGetValue(item.RequestPoGroupId, out var existing))
            {
                if (existing.AllocatedNetAmount != item.AllocatedNetAmount ||
                    existing.AllocatedTaxAmount != item.AllocatedTaxAmount ||
                    existing.AllocatedGrossAmount != item.AllocatedGrossAmount ||
                    !string.Equals(existing.Notes, trimmedNotes, StringComparison.Ordinal))
                {
                    existing.AllocatedNetAmount = item.AllocatedNetAmount;
                    existing.AllocatedTaxAmount = item.AllocatedTaxAmount;
                    existing.AllocatedGrossAmount = item.AllocatedGrossAmount;
                    existing.Notes = trimmedNotes;
                    existing.UpdatedAtUtc = nowUtc;
                    existing.UpdatedByUserId = CurrentUserId;
                }
            }
            else
            {
                var nextSequence = (maxSequenceByGroup.TryGetValue(item.RequestPoGroupId, out var max) ? max : 0) + 1;
                maxSequenceByGroup[item.RequestPoGroupId] = nextSequence;
                _context.OperationInvoiceAllocations.Add(new OperationInvoiceAllocation
                {
                    Id = Guid.NewGuid(),
                    OperationInvoiceId = invoice.Id,
                    RequestPoGroupId = item.RequestPoGroupId,
                    AllocatedNetAmount = item.AllocatedNetAmount,
                    AllocatedTaxAmount = item.AllocatedTaxAmount,
                    AllocatedGrossAmount = item.AllocatedGrossAmount,
                    SequenceNumber = nextSequence,
                    Notes = trimmedNotes,
                    CreatedAtUtc = nowUtc,
                    CreatedByUserId = CurrentUserId
                });
            }
        }

        invoice.UpdatedAtUtc = nowUtc;
        invoice.UpdatedByUserId = CurrentUserId;

        // ── History: the resulting set, with per-group previous values where they changed ──
        var setDescriptions = items.Select(item =>
        {
            var group = groups[item.RequestPoGroupId];
            var previous = previousByGroup.TryGetValue(item.RequestPoGroupId, out var p) ? p : (decimal?)null;
            var change = previous.HasValue && previous.Value != item.AllocatedGrossAmount
                ? $" (anterior {previous.Value:N2})"
                : string.Empty;
            return $"{group.SupplierNameSnapshot ?? "grupo"} {item.AllocatedGrossAmount:N2} {invoice.Currency}{change}";
        });
        var removedDescriptions = previousByGroup.Keys.Except(wantedGroupIds)
            .Select(gid => groups.TryGetValue(gid, out var g) ? g.SupplierNameSnapshot ?? gid.ToString() : gid.ToString())
            .ToList();

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ActorUserId = CurrentUserId,
            ActionTaken = hadAllocations ? "OI_ALLOC_CHANGED" : "OI_ALLOC_SET",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Fatura {invoice.DocumentNumber}: alocações {(hadAllocations ? "alteradas" : "definidas")} — " +
                      (items.Count > 0 ? string.Join("; ", setDescriptions) : "todas removidas") +
                      (removedDescriptions.Count > 0 ? $". Removidas: {string.Join(", ", removedDescriptions)}" : "") + ".",
            CreatedAtUtc = nowUtc
        });

        // ── Re-derive every touched group's aggregate in this same transaction ──
        var coverageChanges = await _coverage.RederiveAsync(touchedGroupIds, forceGroupTouch: false);
        AddGroupStatusHistories(request, coverageChanges);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        return Ok(await ProjectAllocationsAsync(invoice));
    }

    /// <summary>GROUP_OI_STATUS audit rows for every aggregate transition a write produced.</summary>
    private void AddGroupStatusHistories(Request request, List<GroupCoverageChange> changes)
    {
        foreach (var change in changes.Where(c => c.StatusChanged))
        {
            _context.RequestStatusHistories.Add(new RequestStatusHistory
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ActorUserId = CurrentUserId,
                ActionTaken = "GROUP_OI_STATUS",
                PreviousStatusId = request.StatusId,
                NewStatusId = request.StatusId,
                Comment = $"Obrigação de fatura final do grupo {change.RequestPoGroupId}: " +
                          $"{change.PreviousStatus} → {change.NewStatus} " +
                          $"(validado {change.Coverage.ValidatedTotal:N2}; pendente {change.Coverage.PendingValidationTotal:N2}; " +
                          $"restante {change.Coverage.RemainingAmount:N2} de {change.Coverage.ExpectedTotal:N2}).",
                CreatedAtUtc = DateTime.UtcNow
            });
        }
    }

    /// <summary>Allocation read projection — invoice status classifies each row server-side.</summary>
    private async Task<List<OperationInvoiceAllocationDto>> ProjectAllocationsAsync(OperationInvoice invoice)
    {
        var rows = await _context.OperationInvoiceAllocations.AsNoTracking()
            .Where(a => a.OperationInvoiceId == invoice.Id)
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.SequenceNumber)
            .ToListAsync();

        var groupIds = rows.Select(r => r.RequestPoGroupId).Distinct().ToList();
        var groupInfo = await _context.RequestPoGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.SupplierNameSnapshot, g.CurrencyCode })
            .ToDictionaryAsync(g => g.Id);

        return rows.Select(a =>
        {
            groupInfo.TryGetValue(a.RequestPoGroupId, out var g);
            return new OperationInvoiceAllocationDto
            {
                Id = a.Id,
                OperationInvoiceId = a.OperationInvoiceId,
                RequestPoGroupId = a.RequestPoGroupId,
                AllocatedNetAmount = a.AllocatedNetAmount,
                AllocatedTaxAmount = a.AllocatedTaxAmount,
                AllocatedGrossAmount = a.AllocatedGrossAmount,
                SequenceNumber = a.SequenceNumber,
                Notes = a.Notes,
                CreatedAtUtc = a.CreatedAtUtc,
                UpdatedAtUtc = a.UpdatedAtUtc,
                InvoiceStatus = invoice.Status,
                InvoiceDocumentNumber = invoice.DocumentNumber,
                InvoiceDocumentSeries = invoice.DocumentSeries,
                IsEffective = RequestConstants.OperationInvoiceDocumentStatuses.CountsTowardCoverage(invoice.Status),
                IsPendingDecision = RequestConstants.OperationInvoiceDocumentStatuses.IsAwaitingDecision(invoice.Status),
                GroupSupplierName = g?.SupplierNameSnapshot,
                GroupCurrencyCode = g?.CurrencyCode
            };
        }).ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Validate/Reject are Finance decisions. SystemAdministrator follows the
    /// administrative can-act convention; the Buyer never reaches these.</summary>
    private IActionResult? GuardFinanceRole()
    {
        var roles = CurrentUserRoles;
        if (roles.Contains(RoleConstants.Finance) ||
            roles.Contains(RoleConstants.SystemAdministrator))
        {
            return null;
        }

        return StatusCode(403, new ProblemDetails
        {
            Title = "Sem permissão",
            Detail = "Apenas o Financeiro pode validar ou rejeitar faturas finais.",
            Status = 403
        });
    }

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

    /// <summary>
    /// The hard identity rules of the allocation write, re-run against a CANDIDATE header state —
    /// by Update (merged header, before persistence) and by Validate (persisted header, before
    /// any snapshot or status mutation). One rulebook, one set of codes: request scope and
    /// obligation eligibility answer <see cref="AllocationGroupInvalidCode"/>, supplier answers
    /// <see cref="AllocationSupplierMismatchCode"/>, currency answers
    /// <see cref="AllocationCurrencyMismatchCode"/> — never an alternate code for the same
    /// invariant. Company needs no rule of its own (invoice and group share the request, and a
    /// request has exactly one company); Plant is deliberately absent (the invoice carries no
    /// plant identity — a request-scoped document may legitimately span several plants' groups).
    /// </summary>
    private async Task<IActionResult?> GuardAllocatedGroupsIntegrityAsync(
        Guid requestId, Guid invoiceId, int? supplierId, string? currency)
    {
        var allocatedGroupIds = await _context.OperationInvoiceAllocations.AsNoTracking()
            .Where(a => a.OperationInvoiceId == invoiceId)
            .Select(a => a.RequestPoGroupId)
            .Distinct()
            .ToListAsync();
        if (allocatedGroupIds.Count == 0) return null;

        var groups = await _context.RequestPoGroups.AsNoTracking()
            .Where(g => allocatedGroupIds.Contains(g.Id))
            .ToListAsync();

        foreach (var group in groups)
        {
            if (group.RequestId != requestId ||
                !group.RequiresOperationInvoice ||
                string.Equals(group.Status, RequestConstants.PoGroupStatuses.WaitingPoCorrection,
                    StringComparison.OrdinalIgnoreCase))
            {
                var problem = new ProblemDetails
                {
                    Title = "Alocação inconsistente com o grupo",
                    Detail = "Uma alocação desta fatura aponta para um grupo que já não sustenta a " +
                             "obrigação de fatura final. Corrija as alocações antes de continuar.",
                    Status = 409
                };
                problem.Extensions["code"] = AllocationGroupInvalidCode;
                problem.Extensions["requestPoGroupId"] = group.Id;
                return Conflict(problem);
            }

            if (group.SupplierId.HasValue && supplierId.HasValue && group.SupplierId != supplierId)
            {
                var problem = new ProblemDetails
                {
                    Title = "Fornecedor não corresponde",
                    Detail = $"A fatura ficaria com o fornecedor {supplierId}, mas uma alocação " +
                             $"existente pertence ao grupo de {group.SupplierNameSnapshot ?? group.SupplierId.ToString()}. " +
                             "Remova a alocação antes de alterar o fornecedor.",
                    Status = 409
                };
                problem.Extensions["code"] = AllocationSupplierMismatchCode;
                problem.Extensions["requestPoGroupId"] = group.Id;
                return Conflict(problem);
            }

            if (!string.IsNullOrWhiteSpace(group.CurrencyCode) &&
                !string.Equals(group.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase))
            {
                var problem = new ProblemDetails
                {
                    Title = "Moeda não corresponde",
                    Detail = $"A fatura ficaria em {currency}, mas uma alocação existente pertence a " +
                             $"um grupo em {group.CurrencyCode}. Remova a alocação antes de alterar a moeda.",
                    Status = 409
                };
                problem.Extensions["code"] = AllocationCurrencyMismatchCode;
                problem.Extensions["requestPoGroupId"] = group.Id;
                return Conflict(problem);
            }
        }

        return null;
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
