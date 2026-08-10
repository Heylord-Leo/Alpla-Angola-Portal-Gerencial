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
/// The documents that originate a PAYMENT request.
///
/// <para><b>PAYMENT only.</b> Quotation Management keeps one document per quotation and nothing here
/// touches it — a request of type QUOTATION is refused outright rather than silently ignored.</para>
///
/// <para>Everything here is gated on the request being editable (DRAFT or returned for adjustment).
/// After submission the documents are what the approvers approved, and changing one requires the
/// request to be formally returned first — a decision somebody makes and the timeline records.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/requests/{requestId:guid}/source-documents")]
public class PaymentSourceDocumentsController : BaseController
{
    private readonly ILogger<PaymentSourceDocumentsController> _logger;
    private readonly IInternalCompanyGuard _internalCompanies;

    public PaymentSourceDocumentsController(
        ApplicationDbContext context,
        ILogger<PaymentSourceDocumentsController> logger,
        IInternalCompanyGuard internalCompanies) : base(context)
    {
        _logger = logger;
        _internalCompanies = internalCompanies;
    }

    // ── Counterparty integrity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Refuses a document whose supplier is an internal ALPLA legal entity.
    /// </summary>
    ///
    /// <remarks>
    /// <para>The backend half of the rule. The composer already refuses to confirm such a document,
    /// but a supplier that cannot be paid must not become persisted state just because the request
    /// arrived by some other route — and the picker filter, being a filter, is advice.</para>
    ///
    /// <para>Typed so callers can branch on it rather than on message text.</para>
    /// </remarks>
    private async Task<ProblemDetails?> GuardSupplierNotInternalAsync(int? supplierId)
    {
        var internalCompany = await _internalCompanies.ResolveSupplierAsync(supplierId);
        if (internalCompany == null) return null;

        var problem = new ProblemDetails
        {
            Title = "Fornecedor inválido para pagamento",
            Detail = InternalCompanyPolicy.SupplierMessage,
            Status = 400
        };
        problem.Extensions["code"] = InternalCompanyPolicy.ViolationCode;
        problem.Extensions["internalCompanyId"] = internalCompany.Id;
        problem.Extensions["internalCompanyName"] = internalCompany.Name;
        return problem;
    }

    // ── Read ────────────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetSummary(Guid requestId)
    {
        var request = await LoadRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());
        if (request.RequestType?.Code != RequestConstants.Types.Payment) return Ok(EmptySummary(requestId));

        return Ok(await BuildSummaryAsync(request));
    }

    // ── Duplicate preflight ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether a file may be attached, asked BEFORE it is uploaded or read.
    ///
    /// <para>Without this the answer arrives from <c>Create</c>, i.e. after the user has waited for
    /// OCR, corrected every extracted field and confirmed the document. The rule is identical — the
    /// same <see cref="PaymentSourceDocumentDuplicatePolicy"/> decides both here and inside the
    /// persistence transaction — so this is purely a matter of asking earlier.</para>
    ///
    /// <para>Not authoritative, and not meant to be: two clients can both pass this and only one can
    /// win the insert. The 409 at persistence remains the enforcement.</para>
    /// </summary>
    [HttpPost("check-duplicate")]
    public async Task<IActionResult> CheckDuplicate(
        Guid requestId, [FromBody] CheckSourceDocumentDuplicateDto dto)
    {
        var request = await LoadRequestAsync(requestId);
        if (request == null) return NotFound(Problem404());
        if (request.RequestType?.Code != RequestConstants.Types.Payment)
            return Ok(new SourceDocumentDuplicateResultDto());

        var hash = dto.ContentHash?.Trim();
        if (string.IsNullOrWhiteSpace(hash)) return Ok(new SourceDocumentDuplicateResultDto());

        // ── This request ──
        var siblings = await _context.PaymentSourceDocuments
            .Where(d => d.RequestId == requestId)
            .Join(_context.RequestAttachments, d => d.AttachmentId, a => a.Id, (d, a) => new { d, a })
            .Select(x => new DuplicateCandidateDocument
            {
                Id = x.d.Id,
                AttachmentId = x.d.AttachmentId,
                SequenceNumber = x.d.SequenceNumber,
                DocumentNumber = x.d.DocumentNumber,
                SupplierName = x.d.SupplierNameSnapshot,
                FileHash = x.a.FileHash,
                IsVoided = x.d.IsVoided
            })
            .ToListAsync();

        // ── Any other request ──
        // Resolved here rather than inside the policy because the answer depends on what this user
        // is allowed to see, which is not a domain rule.
        var elsewhere = await _context.RequestAttachments
            .Include(a => a.Request)
            .Include(a => a.UploadedByUser)
            .Where(a => a.FileHash == hash && !a.IsDeleted && a.RequestId != requestId)
            .FirstOrDefaultAsync();

        var decision = PaymentSourceDocumentDuplicatePolicy.Decide(
            hash, siblings, dto.ReplacingDocumentId, existsOnAnotherRequest: elsewhere != null);

        var result = new SourceDocumentDuplicateResultDto
        {
            IsDuplicate = decision.IsDuplicate,
            DuplicateScope = decision.Scope switch
            {
                DuplicateScope.SameRequest => "SAME_REQUEST",
                DuplicateScope.OtherRequest => "OTHER_REQUEST",
                _ => "NONE"
            },
            Outcome = decision.Outcome switch
            {
                DuplicateOutcome.Block => "BLOCK",
                DuplicateOutcome.Warn => "WARN",
                _ => "NONE"
            },
            ConflictingDocumentId = decision.ConflictingDocumentId,
            ConflictingSequenceNumber = decision.ConflictingSequenceNumber,
            ConflictingDocumentNumber = decision.ConflictingDocumentNumber,
            SupplierName = decision.SupplierName
        };

        // The other request's identity is disclosed only to someone who could open it anyway. The
        // duplicate itself is still reported — withholding that would let a file be silently reused.
        if (decision.Scope == DuplicateScope.OtherRequest && elsewhere != null)
        {
            var scoped = await GetScopedRequestsQuery();
            if (await scoped.AnyAsync(r => r.Id == elsewhere.RequestId))
            {
                result.RequestId = elsewhere.RequestId;
                result.RequestNumber = elsewhere.Request?.RequestNumber;
                result.UploadedBy = elsewhere.UploadedByUser?.FullName;
                result.CreatedAtUtc = elsewhere.UploadedAtUtc;
            }
        }

        return Ok(result);
    }

    // ── Create ──────────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create(Guid requestId, [FromBody] SavePaymentSourceDocumentDto dto)
    {
        var request = await LoadRequestAsync(requestId);
        var guard = GuardEditable(request);
        if (guard != null) return guard;

        if (dto.AttachmentId == null || dto.AttachmentId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Documento sem anexo",
                Detail = "Anexe o ficheiro do documento antes de o registar.",
                Status = 400
            });
        }

        var attachment = await _context.RequestAttachments
            .FirstOrDefaultAsync(a => a.Id == dto.AttachmentId.Value && a.RequestId == requestId);

        if (attachment == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Anexo inválido",
                Detail = "O anexo indicado não pertence a este pedido.",
                Status = 400
            });
        }

        // One attachment is one source document. A retried upload therefore returns the existing
        // row instead of creating a twin — the unique index would refuse it anyway, and a 409 for
        // a network retry the user never saw would be inexplicable.
        var existing = await _context.PaymentSourceDocuments
            .FirstOrDefaultAsync(d => d.AttachmentId == attachment.Id);

        if (existing != null)
        {
            if (existing.RequestId != requestId)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Anexo já utilizado",
                    Detail = "Este anexo já está registado noutro pedido.",
                    Status = 400
                });
            }

            return Ok(await ProjectAsync(existing));
        }

        // The same FILE (by hash) already registered on this request is a genuine duplicate, not a
        // retry: the user picked the same invoice twice. Refused with the offending document named.
        var duplicate = await FindDuplicateByHashAsync(requestId, attachment);
        if (duplicate != null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Documento duplicado",
                Detail = $"Este ficheiro já está registado como Documento {duplicate.SequenceNumber} " +
                         $"({duplicate.DocumentNumber ?? "sem número"}).",
                Status = 409
            });
        }

        var supplierProblem = await GuardSupplierNotInternalAsync(dto.SupplierId);
        if (supplierProblem != null) return BadRequest(supplierProblem);

        var document = new PaymentSourceDocument
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            AttachmentId = attachment.Id,
            SequenceNumber = await NextSequenceAsync(requestId),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId
        };

        ApplyFields(document, dto);

        // A renamed or re-scanned copy of an invoice already on this request has different bytes but
        // is the same debt. Supplier + number + series is what identifies it.
        var sameBusinessDocument = await FindDuplicateBusinessDocumentAsync(requestId, document.Id, document);
        if (sameBusinessDocument != null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Documento duplicado",
                Detail = $"O documento {document.DocumentNumber} deste fornecedor já está registado " +
                         $"como Documento {sameBusinessDocument.SequenceNumber} neste pedido.",
                Status = 409
            });
        }

        _context.PaymentSourceDocuments.Add(document);
        await RecordHistoryAsync(
            request!, "DOCUMENTO_ORIGEM_ADICIONADO",
            $"Documento de origem {document.SequenceNumber} adicionado ({dto.DocumentNumber ?? attachment.FileName}).",
            PostPaymentIdempotencyKeys.PaymentSourceDocumentAdded(requestId, attachment.Id));

        var overrideProblem = await StageClassificationOverrideAsync(document, request!);
        if (overrideProblem != null) return BadRequest(overrideProblem);

        await SyncHeaderCompatibilityAsync(request!);
        await SaveAsync();

        return Ok(await ProjectAsync(document));
    }

    // ── Update ──────────────────────────────────────────────────────────────────────────────

    [HttpPut("{documentId:guid}")]
    public async Task<IActionResult> Update(
        Guid requestId, Guid documentId, [FromBody] SavePaymentSourceDocumentDto dto)
    {
        var request = await LoadRequestAsync(requestId);
        var guard = GuardEditable(request);
        if (guard != null) return guard;

        var document = await _context.PaymentSourceDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.RequestId == requestId);

        if (document == null) return NotFound(Problem404("Documento de origem não encontrado."));

        if (document.IsVoided)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Documento anulado",
                Detail = "Um documento anulado não pode ser alterado. Adicione um novo documento.",
                Status = 409
            });
        }

        var concurrency = ApplyConcurrencyToken(document, dto.RowVersion);
        if (concurrency != null) return concurrency;

        var updateSupplierProblem = await GuardSupplierNotInternalAsync(dto.SupplierId);
        if (updateSupplierProblem != null) return BadRequest(updateSupplierProblem);

        // Replacing the attachment makes this a different document. The previous OCR reading and
        // any override decision belonged to the FILE that was replaced, so they must not silently
        // carry over — a prior confirmation is not a confirmation of something nobody has read.
        var attachmentReplaced =
            dto.AttachmentId.HasValue &&
            dto.AttachmentId.Value != Guid.Empty &&
            dto.AttachmentId.Value != document.AttachmentId;

        if (attachmentReplaced)
        {
            var replacement = await _context.RequestAttachments
                .FirstOrDefaultAsync(a => a.Id == dto.AttachmentId!.Value && a.RequestId == requestId);

            if (replacement == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Anexo inválido",
                    Detail = "O anexo indicado não pertence a este pedido.",
                    Status = 400
                });
            }

            var previousAttachmentId = document.AttachmentId;
            document.AttachmentId = replacement.Id;
            ResetClassificationDecision(document);

            await RecordHistoryAsync(
                request!, "DOCUMENTO_ORIGEM_SUBSTITUIDO",
                $"Anexo do documento de origem {document.SequenceNumber} substituído. " +
                "A classificação anterior foi descartada e tem de ser decidida novamente.",
                PostPaymentIdempotencyKeys.PaymentSourceDocumentReplaced(document.Id, replacement.Id));

            _logger.LogInformation(
                "PaymentSourceDocument {DocumentId} attachment replaced {Old} -> {New}; classification reset.",
                document.Id, previousAttachmentId, replacement.Id);
        }

        var previous = (document.SupplierId, document.Currency, document.PlantId,
                        document.SourceDocumentType);

        ApplyFields(document, dto);
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = CurrentUserId;

        var overrideProblem = await StageClassificationOverrideAsync(document, request!);
        if (overrideProblem != null) return BadRequest(overrideProblem);

        // Release 4 Phase 1c/1d: if this document's lines already sit in PO groups, an edit that
        // touches any grouping-key dimension (supplier, currency, plant, document type) must
        // re-derive those groups IN THIS SAME transaction — or be refused. Returning here persists
        // nothing: document change, group re-stamp and audit are atomic by construction (one
        // SaveAsync at the end).
        var integrityProblem = await GuardGroupingKeyIntegrityAsync(request!, document, previous);
        if (integrityProblem != null) return Conflict(integrityProblem);

        await SyncHeaderCompatibilityAsync(request!);

        try
        {
            await SaveAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConcurrencyConflictAsync(document.Id);
        }

        return Ok(await ProjectAsync(document));
    }

    // ── Remove / void ───────────────────────────────────────────────────────────────────────

    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Remove(
        Guid requestId, Guid documentId, [FromBody] VoidPaymentSourceDocumentDto? dto = null)
    {
        var request = await LoadRequestAsync(requestId);
        var guard = GuardEditable(request);
        if (guard != null) return guard;

        var document = await _context.PaymentSourceDocuments
            .Include(d => d.LineItems)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.RequestId == requestId);

        if (document == null) return NotFound(Problem404("Documento de origem não encontrado."));
        if (document.IsVoided) return Ok(await BuildSummaryAsync(request!));

        var concurrency = ApplyConcurrencyToken(document, dto?.RowVersion);
        if (concurrency != null) return concurrency;

        // Release 4 Phase 1c: a document whose lines already landed in PO groups is part of an
        // approved financial grouping. Removing it would change group membership and totals after
        // approval — a reconciliation decision, never a self-service delete.
        if (document.LineItems.Any(i => !i.IsDeleted && i.RequestPoGroupId != null))
        {
            var problem = new ProblemDetails
            {
                Title = "Documento vinculado a grupos de pagamento",
                Detail = "Este documento de origem já alimenta grupos de pagamento aprovados. " +
                         "A sua remoção alteraria a composição financeira dos grupos e requer " +
                         "reconciliação pelo Financeiro.",
                Status = 409
            };
            problem.Extensions["code"] = PoGroupReclassificationBlockReasons.DocumentContributesToGroups;
            return Conflict(problem);
        }

        // A request that was never submitted has nothing downstream: the document may go entirely.
        // Once it has been submitted, its classification decision was audited, so it is VOIDED —
        // an audit must survive the object it describes.
        var neverSubmitted = request!.SubmittedAtUtc == null;

        foreach (var item in document.LineItems.Where(i => !i.IsDeleted))
            item.IsDeleted = true;

        if (PaymentSourceDocumentPolicy.MayHardDelete(neverSubmitted))
        {
            _context.PaymentSourceDocuments.Remove(document);

            await RecordHistoryAsync(
                request, "DOCUMENTO_ORIGEM_REMOVIDO",
                $"Documento de origem {document.SequenceNumber} removido antes da submissão.",
                idempotencyKey: null);
        }
        else
        {
            document.IsVoided = true;
            document.VoidedAtUtc = DateTime.UtcNow;
            document.VoidedByUserId = CurrentUserId;
            document.VoidReason = string.IsNullOrWhiteSpace(dto?.Reason)
                ? "Anulado durante o ajuste do pedido."
                : dto!.Reason!.Trim();

            await RecordHistoryAsync(
                request, "DOCUMENTO_ORIGEM_ANULADO",
                $"Documento de origem {document.SequenceNumber} anulado. Motivo: {document.VoidReason}",
                PostPaymentIdempotencyKeys.PaymentSourceDocumentVoided(document.Id));
        }

        await SyncHeaderCompatibilityAsync(request);

        try
        {
            await SaveAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConcurrencyConflictAsync(document.Id);
        }

        return Ok(await BuildSummaryAsync(request));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private async Task<Request?> LoadRequestAsync(Guid requestId) =>
        await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.RequestType)
            .FirstOrDefaultAsync(r => r.Id == requestId);

    /// <summary>
    /// Refuses everything the state or the request type forbids, with a reason the user can act on.
    /// Finance permissions do not bypass this: the gate is about the request's stage, not the
    /// caller's role.
    /// </summary>
    private IActionResult? GuardEditable(Request? request)
    {
        if (request == null) return NotFound(Problem404());

        if (request.RequestType?.Code != RequestConstants.Types.Payment)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tipo de pedido inválido",
                Detail = "Documentos de origem existem apenas em pedidos de Pagamento. " +
                         "A Gestão de Cotações mantém um documento por cotação.",
                Status = 400
            });
        }

        if (!PaymentSourceDocumentPolicy.IsEditable(request.Status?.Code))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Pedido não editável",
                Detail = PaymentSourceDocumentPolicy.EditBlockedReason(request.Status?.Code),
                Status = 409
            });
        }

        return null;
    }

    private static ProblemDetails Problem404(string detail = "Pedido não encontrado.") =>
        new() { Title = "Não encontrado", Detail = detail, Status = 404 };

    private async Task<int> NextSequenceAsync(Guid requestId)
    {
        // Sequence never reuses a number, including one freed by a removal: "Documento 2" must mean
        // the same document to everyone who discussed it.
        var max = await _context.PaymentSourceDocuments
            .Where(d => d.RequestId == requestId)
            .Select(d => (int?)d.SequenceNumber)
            .MaxAsync();

        return (max ?? 0) + 1;
    }

    private async Task<PaymentSourceDocument?> FindDuplicateByHashAsync(
        Guid requestId, RequestAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.FileHash)) return null;

        return await _context.PaymentSourceDocuments
            .Where(d => d.RequestId == requestId && !d.IsVoided)
            .Join(_context.RequestAttachments, d => d.AttachmentId, a => a.Id, (d, a) => new { d, a })
            .Where(x => x.a.FileHash == attachment.FileHash)
            .Select(x => x.d)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// The same business document already registered on this request, under a different file.
    ///
    /// <para>The hash check only catches the identical file. Renaming, re-scanning or re-exporting
    /// the same invoice produces different bytes and would otherwise sail through — while being,
    /// commercially, the same debt about to be paid twice. Supplier plus document number is what
    /// identifies an invoice; the series distinguishes the legitimate case where two suppliers use
    /// the same numbering.</para>
    /// </summary>
    private async Task<PaymentSourceDocument?> FindDuplicateBusinessDocumentAsync(
        Guid requestId, Guid documentId, PaymentSourceDocument candidate)
    {
        var number = candidate.DocumentNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number) || candidate.SupplierId == null) return null;

        var series = candidate.DocumentSeries?.Trim();

        var siblings = await _context.PaymentSourceDocuments
            .Where(d => d.RequestId == requestId && !d.IsVoided && d.Id != documentId
                        && d.SupplierId == candidate.SupplierId)
            .ToListAsync();

        return siblings.FirstOrDefault(d =>
            string.Equals(d.DocumentNumber?.Trim(), number, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.DocumentSeries?.Trim() ?? string.Empty, series ?? string.Empty,
                          StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyFields(PaymentSourceDocument document, SavePaymentSourceDocumentDto dto)
    {
        document.SupplierId = dto.SupplierId ?? document.SupplierId;
        document.SupplierTaxIdSnapshot = dto.SupplierTaxIdSnapshot ?? document.SupplierTaxIdSnapshot;
        document.PlantId = dto.PlantId ?? document.PlantId;

        document.SourceDocumentType =
            RequestConstants.SourceDocumentTypes.Normalize(dto.SourceDocumentType)
            ?? document.SourceDocumentType;

        document.DocumentNumber = Trimmed(dto.DocumentNumber) ?? document.DocumentNumber;
        document.DocumentSeries = Trimmed(dto.DocumentSeries) ?? document.DocumentSeries;
        document.DocumentDate = dto.DocumentDate ?? document.DocumentDate;
        document.DueDate = dto.DueDate ?? document.DueDate;
        document.Currency = Trimmed(dto.Currency)?.ToUpperInvariant() ?? document.Currency;

        document.NetAmount = dto.NetAmount ?? document.NetAmount;
        document.TaxAmount = dto.TaxAmount ?? document.TaxAmount;
        document.GrossAmount = dto.GrossAmount ?? document.GrossAmount;

        document.OcrSuggestion =
            RequestConstants.SourceDocumentTypes.Normalize(dto.OcrSuggestion) ?? document.OcrSuggestion;
        document.OcrConfidence = dto.OcrConfidence ?? document.OcrConfidence;
        document.OcrTitleFound = dto.OcrTitleFound ?? document.OcrTitleFound;
        document.OcrEvidenceJson = dto.OcrEvidenceJson ?? document.OcrEvidenceJson;
        document.OcrConflictingEvidenceJson =
            dto.OcrConflictingEvidenceJson ?? document.OcrConflictingEvidenceJson;

        document.ClassificationSource =
            Trimmed(dto.ClassificationSource)?.ToUpperInvariant() ?? document.ClassificationSource;
        document.ClassificationSuggestionSource =
            RequestConstants.DocumentClassificationSources.Normalize(dto.ClassificationSuggestionSource)
            ?? document.ClassificationSuggestionSource;
        document.ClassificationConflictAcknowledged =
            dto.ClassificationConflictAcknowledged ?? document.ClassificationConflictAcknowledged;
        document.ClassificationJustification =
            Trimmed(dto.ClassificationJustification) ?? document.ClassificationJustification;
    }

    /// <summary>
    /// Everything that was true of the file that has just been replaced. Cleared together, because
    /// a confirmation of one reading is not a confirmation of another.
    /// </summary>
    private static void ResetClassificationDecision(PaymentSourceDocument document)
    {
        document.OcrSuggestion = null;
        document.OcrConfidence = null;
        document.OcrTitleFound = null;
        document.OcrEvidenceJson = null;
        document.OcrConflictingEvidenceJson = null;
        document.ClassificationSuggestionSource = null;
        document.ClassificationConflictAcknowledged = false;
        document.ClassificationJustification = null;
        document.ClassificationReviewedByFinance = false;
        document.ClassificationReviewedByUserId = null;
        document.ClassificationReviewedAtUtc = null;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IActionResult? ApplyConcurrencyToken(PaymentSourceDocument document, byte[]? rowVersion)
    {
        if (rowVersion == null || rowVersion.Length == 0) return null;

        _context.Entry(document).Property(d => d.RowVersion).OriginalValue = rowVersion;
        return null;
    }

    /// <summary>
    /// A stale edit is reported with the current state attached, never merged. The caller needs
    /// enough to reload and show what moved — a bare 409 would leave the user guessing.
    /// </summary>
    private async Task<IActionResult> ConcurrencyConflictAsync(Guid documentId)
    {
        _context.ChangeTracker.Clear();

        var current = await _context.PaymentSourceDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId);

        return Conflict(new
        {
            Title = "Documento alterado entretanto",
            Detail = "Este documento foi alterado por outra pessoa desde que o abriu. " +
                     "Recarregue para ver o estado atual antes de repetir a alteração.",
            Status = 409,
            Current = current == null ? null : await ProjectAsync(current)
        });
    }

    /// <summary>
    /// Records a classification that contradicts the reading — scoped to the DOCUMENT, not the
    /// request, because one request now holds several independent decisions.
    /// </summary>
    private async Task<ProblemDetails?> StageClassificationOverrideAsync(
        PaymentSourceDocument document, Request request)
    {
        var decision = DocumentClassificationOverrideRecorder.Evaluate(
            new DocumentClassificationOverrideRequest
            {
                Context = RequestConstants.DocumentClassificationContexts.PaymentRequest,
                ScopeId = document.Id,
                AttachmentId = document.AttachmentId,
                SuggestedType = document.OcrSuggestion,
                Confidence = document.OcrConfidence,
                TitleFound = document.OcrTitleFound,
                EvidenceJson = document.OcrEvidenceJson,
                ConflictingEvidenceJson = document.OcrConflictingEvidenceJson,
                SuggestionSource = document.ClassificationSuggestionSource,
                SelectedType = document.SourceDocumentType,
                Acknowledged = document.ClassificationConflictAcknowledged,
                Justification = document.ClassificationJustification
            });

        if (decision.RejectionReason != null)
        {
            return new ProblemDetails
            {
                Title = "Classificação Divergente",
                Detail = $"Documento {document.SequenceNumber}: {decision.RejectionReason}",
                Status = 400
            };
        }

        if (!decision.ShouldRecord) return null;

        var already = await _context.DocumentClassificationOverrides
            .AnyAsync(o => o.IdempotencyKey == decision.IdempotencyKey);
        if (already) return null;

        _context.DocumentClassificationOverrides.Add(new DocumentClassificationOverride
        {
            Id = Guid.NewGuid(),
            Context = decision.NormalizedContext,
            RequestId = request.Id,
            QuotationId = null,
            AttachmentId = document.AttachmentId,
            SuggestedType = decision.NormalizedSuggestedType,
            Confidence = document.OcrConfidence,
            TitleFound = document.OcrTitleFound,
            EvidenceJson = document.OcrEvidenceJson,
            ConflictingEvidenceJson = document.OcrConflictingEvidenceJson,
            SuggestionSource = decision.NormalizedSuggestionSource,
            SelectedType = decision.NormalizedSelectedType,
            Acknowledged = document.ClassificationConflictAcknowledged,
            Justification = decision.TrimmedJustification,
            ActorUserId = CurrentUserId,
            CreatedAtUtc = DateTime.UtcNow,
            IdempotencyKey = decision.IdempotencyKey
        });

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = CurrentUserId,
            ActionTaken = "CLASSIFICACAO_DOCUMENTO_DIVERGENTE",
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = $"Documento {document.SequenceNumber}: {decision.HistoryComment}",
            IdempotencyKey = decision.IdempotencyKey,
            CreatedAtUtc = DateTime.UtcNow
        });

        return null;
    }

    /// <summary>
    /// Release 4 Phase 1c/1d: the transactional half of the grouping-key integrity contract.
    ///
    /// <para>When the edited document's active lines already belong to PO groups, each affected
    /// group is judged by <see cref="PoGroupReclassificationPlanner"/> over the resulting
    /// <see cref="PaymentGroupingKey"/> of every document feeding it — the same canonical key the
    /// group builder deduplicates by, so no second comparison algorithm exists. An agreeing group
    /// is re-stamped (identity + derived obligation fields; the captured
    /// <c>ExpectedOperationInvoiceTotal</c> is a financial snapshot and is never recalculated or
    /// cleared); a disagreeing group, or one whose financial evidence forbids the change, refuses
    /// the whole update with a typed 409. Affected groups come from line ownership
    /// (<c>RequestLineItem.PaymentSourceDocumentId</c> → <c>RequestPoGroupId</c>) — never from the
    /// request header, the supplier or the old values.</para>
    ///
    /// <para>The payment condition travels as the GROUP's own value: documents do not carry one,
    /// and the Buyer legitimately refines it per group at P.O. registration — so it can never
    /// diverge through a document edit and is never treated as one.</para>
    ///
    /// <para>Runs before the single <c>SaveAsync</c>, so document change, group re-stamp and audit
    /// row commit together or not at all. No background repair, no lazy repair from reads.</para>
    /// </summary>
    private async Task<ProblemDetails?> GuardGroupingKeyIntegrityAsync(
        Request request, PaymentSourceDocument document,
        (int? SupplierId, string? Currency, int? PlantId, string? SourceDocumentType) previous)
    {
        // Key-relevant change detection through the SAME normalization the group builder uses.
        // The condition component is irrelevant here (identical on both sides by construction).
        var previousKey = PaymentGroupingKey.From(
            previous.SupplierId, previous.Currency, null, previous.PlantId, previous.SourceDocumentType);
        var newKey = PaymentGroupingKey.From(
            document.SupplierId, document.Currency, null, document.PlantId, document.SourceDocumentType);

        if (previousKey.Equals(newKey)) return null;

        var affectedGroupIds = await _context.RequestLineItems
            .Where(li => li.PaymentSourceDocumentId == document.Id &&
                         !li.IsDeleted && li.RequestPoGroupId != null)
            .Select(li => li.RequestPoGroupId!.Value)
            .Distinct()
            .ToListAsync();

        if (affectedGroupIds.Count == 0) return null;   // DRAFT path: no groups exist yet.

        var groups = await _context.RequestPoGroups
            .Where(g => affectedGroupIds.Contains(g.Id))
            .ToListAsync();

        // Current key dimensions of every active document feeding each affected group. The edited
        // document's NEW values are substituted explicitly rather than trusted to tracker
        // identity resolution.
        var contributions = await _context.RequestLineItems
            .Where(li => li.RequestPoGroupId != null &&
                         affectedGroupIds.Contains(li.RequestPoGroupId.Value) &&
                         !li.IsDeleted && li.PaymentSourceDocumentId != null)
            .Join(_context.PaymentSourceDocuments.Where(d => !d.IsVoided),
                  li => li.PaymentSourceDocumentId, d => d.Id,
                  (li, d) => new
                  {
                      GroupId = li.RequestPoGroupId!.Value,
                      d.Id, d.SupplierId, d.Currency, d.PlantId, d.SourceDocumentType
                  })
            .Distinct()
            .ToListAsync();

        var groupsWithAllocations = await _context.OperationInvoiceAllocations
            .Where(a => affectedGroupIds.Contains(a.RequestPoGroupId))
            .Select(a => a.RequestPoGroupId)
            .Distinct()
            .ToListAsync();

        var groupsWithActiveShortClose = await _context.OperationInvoiceShortCloses
            .Where(c => affectedGroupIds.Contains(c.RequestPoGroupId) &&
                        RequestConstants.ShortCloseStatuses.Active.Contains(c.Status))
            .Select(c => c.RequestPoGroupId)
            .Distinct()
            .ToListAsync();

        var groupsWithReconciliationSnapshots = await _context.OperationInvoiceReconciliations
            .Where(r => affectedGroupIds.Contains(r.RequestPoGroupId))
            .Select(r => r.RequestPoGroupId)
            .Distinct()
            .ToListAsync();

        var groupsWithPoAttachments = await _context.RequestAttachments
            .Where(a => a.RequestPoGroupId != null && affectedGroupIds.Contains(a.RequestPoGroupId.Value))
            .Select(a => a.RequestPoGroupId!.Value)
            .Distinct()
            .ToListAsync();

        var groupsWithPayments = await _context.RequestPayments
            .Where(p => p.RequestPoGroupId != null && affectedGroupIds.Contains(p.RequestPoGroupId.Value))
            .Select(p => p.RequestPoGroupId!.Value)
            .Distinct()
            .ToListAsync();

        var groupsWithReconciliations = await _context.RequestReconciliations
            .Where(r => r.RequestPoGroupId != null && affectedGroupIds.Contains(r.RequestPoGroupId.Value))
            .Select(r => r.RequestPoGroupId!.Value)
            .Distinct()
            .ToListAsync();

        var inputs = groups.Select(g => new PoGroupReclassificationInput
        {
            GroupId = g.Id,
            CurrentGroupKey = PaymentGroupingKey.From(
                g.SupplierId, g.CurrencyCode, g.PaymentConditionCode, g.PlantId, g.SourceDocumentType),
            ContributingKeys = contributions
                .Where(c => c.GroupId == g.Id)
                .Select(c => c.Id == document.Id
                    ? PaymentGroupingKey.From(
                        document.SupplierId, document.Currency, g.PaymentConditionCode,
                        document.PlantId, document.SourceDocumentType)
                    : PaymentGroupingKey.From(
                        c.SupplierId, c.Currency, g.PaymentConditionCode,
                        c.PlantId, c.SourceDocumentType))
                .ToList(),
            HasOperationInvoiceActivity =
                groupsWithAllocations.Contains(g.Id) ||
                groupsWithActiveShortClose.Contains(g.Id) ||
                groupsWithReconciliationSnapshots.Contains(g.Id) ||
                g.FiscalReceiptAttachmentId != null ||
                g.OperationalReceiptCompletedAtUtc != null,
            HasCommercialEvidence =
                !string.IsNullOrWhiteSpace(g.PurchaseOrderNumber) ||
                groupsWithPoAttachments.Contains(g.Id) ||
                groupsWithPayments.Contains(g.Id) ||
                groupsWithReconciliations.Contains(g.Id),
            HasCapturedExpectedTotal = g.ExpectedOperationInvoiceTotal != null
        }).ToList();

        var decisions = PoGroupReclassificationPlanner.Plan(inputs);

        var blocked = decisions.FirstOrDefault(d => d.Action == PoGroupReclassificationAction.Blocked);
        if (blocked != null)
        {
            var problem = new ProblemDetails
            {
                Title = "Alteração bloqueada",
                Detail = $"Documento {document.SequenceNumber}: {blocked.BlockReason}",
                Status = 409
            };
            problem.Extensions["code"] = blocked.BlockReasonCode;
            return problem;
        }

        // Supplier/currency rows for a full-key re-stamp, resolved once.
        var newSupplier = document.SupplierId.HasValue
            ? await _context.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == document.SupplierId.Value)
            : null;

        foreach (var decision in decisions.Where(d => d.Action == PoGroupReclassificationAction.Restamp))
        {
            var group = groups.First(g => g.Id == decision.GroupId);
            var key = decision.NewKey!.Value;
            var obligations = decision.Obligations!;

            if (group.SupplierId != key.SupplierId)
            {
                group.SupplierId = key.SupplierId;
                group.SupplierNameSnapshot =
                    newSupplier?.Name ?? document.SupplierNameSnapshot ?? group.SupplierNameSnapshot;
                group.SupplierNifSnapshot = newSupplier?.TaxId ?? document.SupplierTaxIdSnapshot;
            }

            if (!string.Equals(group.CurrencyCode, key.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                group.CurrencyCode = key.CurrencyCode ?? group.CurrencyCode;
                group.CurrencyId = key.CurrencyCode == null
                    ? group.CurrencyId
                    : (await _context.Currencies.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Code == key.CurrencyCode))?.Id;
            }

            group.PlantId = key.PlantId;
            group.SourceDocumentType = key.SourceDocumentType;
            group.OperationInvoiceStatus = obligations.OperationInvoiceStatus;
            group.RequiresOperationInvoice = obligations.RequiresOperationInvoice;
            group.RequiresSeparateFiscalReceipt = obligations.RequiresSeparateFiscalReceipt;
            group.RequiresAdvanceRegularization = obligations.RequiresAdvanceRegularization;
            group.RequiresFinanceClassificationReview = obligations.RequiresFinanceClassificationReview;
            // ExpectedOperationInvoiceTotal/Currency untouched: a captured snapshot, never
            // recalculated by an identity correction and never cleared by NOT_REQUIRED. A currency
            // change with a captured total was already blocked above, so no falsification is
            // possible here. Nothing is opportunistically captured either — capture happens only
            // at group creation.
            group.UpdatedAtUtc = DateTime.UtcNow;
            group.UpdatedByUserId = CurrentUserId;

            // One history row telling both facts. No idempotency key: a repeated correction is a
            // new event, and the comment carries what changed, making each row self-explaining.
            var typeTransition = decision.ChangedDimensions.Contains(
                PoGroupReclassificationPlanner.DimensionNames.SourceDocumentType)
                ? $" Tipo de documento: " +
                  $"{RequestConstants.SourceDocumentTypes.DisplayName(previous.SourceDocumentType)} → " +
                  $"{RequestConstants.SourceDocumentTypes.DisplayName(document.SourceDocumentType)}."
                : string.Empty;

            await RecordHistoryAsync(
                request, "GRUPO_OBRIGACAO_REDERIVADA",
                $"Documento de origem {document.SequenceNumber} alterado " +
                $"({string.Join(", ", decision.ChangedDimensions)})." + typeTransition +
                $" Identidade e obrigações do grupo '{group.SupplierNameSnapshot}' rederivadas: " +
                $"fatura final " +
                (obligations.RequiresOperationInvoice ? "exigida." : "não exigida."),
                idempotencyKey: null);
        }

        return null;
    }

    private async Task RecordHistoryAsync(
        Request request, string action, string comment, string? idempotencyKey)
    {
        if (idempotencyKey != null)
        {
            var exists = await _context.RequestStatusHistories
                .AnyAsync(h => h.IdempotencyKey == idempotencyKey);
            if (exists) return;
        }

        _context.RequestStatusHistories.Add(new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            ActorUserId = CurrentUserId,
            ActionTaken = action,
            PreviousStatusId = request.StatusId,
            NewStatusId = request.StatusId,
            Comment = comment,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Keeps the request header's compatibility fields honest: populated when every active document
    /// agrees, null when they do not. Never a manufactured composite, and never the thing that
    /// decides an obligation, a group or a total.
    /// </summary>
    private async Task SyncHeaderCompatibilityAsync(Request request)
    {
        var active = await _context.PaymentSourceDocuments
            .Where(d => d.RequestId == request.Id && !d.IsVoided)
            .Select(d => new { d.SupplierId, d.PlantId, d.SourceDocumentType, d.GrossAmount })
            .ToListAsync();

        var header = PaymentSourceDocumentPolicy.DeriveHeader(
            active.Select(d => new PaymentSourceDocumentHeaderInput(
                d.SupplierId, d.PlantId, d.SourceDocumentType)));

        request.SupplierId = header.SupplierId;
        request.SourceDocumentType = header.SourceDocumentType;

        // Request.PlantId is deliberately NOT overwritten: it is the routing and authorization
        // plant, and this release does not reopen approval routing.

        // EstimatedTotalAmount stays a display mirror of the authoritative sum.
        if (active.Count > 0)
            request.EstimatedTotalAmount = active.Sum(d => d.GrossAmount ?? 0m);

        request.UpdatedAtUtc = DateTime.UtcNow;
        request.UpdatedByUserId = CurrentUserId;
    }

    private async Task SaveAsync() => await _context.SaveChangesAsync();

    // ── Projection ──────────────────────────────────────────────────────────────────────────

    private static PaymentSourceDocumentsSummaryDto EmptySummary(Guid requestId) =>
        new() { RequestId = requestId, CanEditDocuments = false, CanSubmit = true };

    private async Task<PaymentSourceDocumentsSummaryDto> BuildSummaryAsync(Request request)
    {
        var documents = await _context.PaymentSourceDocuments
            .Where(d => d.RequestId == request.Id)
            .OrderBy(d => d.SequenceNumber)
            .ToListAsync();

        var projected = new List<PaymentSourceDocumentDto>();
        foreach (var d in documents) projected.Add(await ProjectAsync(d));

        var active = projected.Where(d => !d.IsVoided).ToList();

        var validation = PaymentSourceDocumentValidator.Validate(
            active.Select(ToState), requireClassification: true);

        foreach (var dto in active)
        {
            dto.ValidationMessages = validation.Problems
                .Where(p => p.DocumentId == dto.Id)
                .Select(p => p.Message)
                .ToList();

            // A document saved before this rule existed can still name an ALPLA entity. Historical
            // rows are never rewritten — the problem is SHOWN, on the document that has it, and
            // submission refuses until somebody corrects it deliberately.
            var internalCompany = await _internalCompanies.ResolveSupplierAsync(dto.SupplierId);
            if (internalCompany != null)
            {
                dto.SupplierIsInternalCompany = true;
                dto.SupplierInternalCompanyName = internalCompany.Name;
                // IsValid is derived from this list, so inserting the message is what makes the
                // document invalid — there is no separate flag to keep in step.
                dto.ValidationMessages.Insert(0, InternalCompanyPolicy.SupplierMessage);
            }
        }

        var editable = PaymentSourceDocumentPolicy.IsEditable(request.Status?.Code);

        return new PaymentSourceDocumentsSummaryDto
        {
            RequestId = request.Id,
            UsesMultiDocumentModel = request.UsesMultiSourceDocuments,
            Documents = active,
            VoidedDocuments = projected.Where(d => d.IsVoided).ToList(),
            RequestTotal = validation.RequestTotal,
            Currency = active.Select(d => d.Currency).FirstOrDefault(c => c != null),
            CanEditDocuments = editable,
            EditBlockedReason = editable ? null : PaymentSourceDocumentPolicy.EditBlockedReason(request.Status?.Code),
            RequestValidationMessages = validation.Problems
                .Where(p => p.DocumentId == Guid.Empty)
                .Select(p => p.Message)
                .ToList(),
            MixedTypeNotice = validation.MixedTypeNotice,
            CanSubmit = validation.CanSubmit
        };
    }

    internal static PaymentSourceDocumentState ToState(PaymentSourceDocumentDto d) => new()
    {
        Id = d.Id,
        SequenceNumber = d.SequenceNumber,
        Label = $"Documento {d.SequenceNumber}",
        HasAttachment = d.AttachmentId != Guid.Empty,
        SupplierId = d.SupplierId,
        PlantId = d.PlantId,
        DocumentNumber = d.DocumentNumber,
        SourceDocumentType = d.SourceDocumentType,
        DocumentDate = d.DocumentDate,
        DueDate = d.DueDate,
        Currency = d.Currency,
        GrossAmount = d.GrossAmount,
        ItemsTotal = d.ItemsTotal,
        ActiveItemCount = d.Items.Count,
        OcrSuggestion = d.OcrSuggestion,
        OcrConfidence = d.OcrConfidence,
        ClassificationConflictAcknowledged = d.ClassificationConflictAcknowledged,
        ClassificationJustification = d.ClassificationJustification
    };

    private async Task<PaymentSourceDocumentDto> ProjectAsync(PaymentSourceDocument d)
    {
        var attachment = await _context.RequestAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == d.AttachmentId);

        var plantName = d.PlantId.HasValue
            ? await _context.Plants.AsNoTracking()
                .Where(p => p.Id == d.PlantId.Value).Select(p => p.Name).FirstOrDefaultAsync()
            : null;

        var supplierName = d.SupplierNameSnapshot ?? (d.SupplierId.HasValue
            ? await _context.Suppliers.AsNoTracking()
                .Where(s => s.Id == d.SupplierId.Value).Select(s => s.Name).FirstOrDefaultAsync()
            : null);

        var items = await _context.RequestLineItems
            .AsNoTracking()
            .Include(i => i.Unit)
            .Where(i => i.PaymentSourceDocumentId == d.Id && !i.IsDeleted)
            .OrderBy(i => i.LineNumber)
            .Select(i => new PaymentSourceDocumentItemDto
            {
                Id = i.Id,
                LineNumber = i.LineNumber,
                Description = i.Description,
                Quantity = i.Quantity,
                UnitId = i.UnitId,
                UnitCode = i.Unit != null ? i.Unit.Code : null,
                UnitPrice = i.UnitPrice,
                DiscountAmount = i.DiscountAmount,
                IvaRateId = i.IvaRateId,
                TotalAmount = i.TotalAmount,
                PlantId = i.PlantId,
                SupplierId = i.SupplierId,
                RequestPoGroupId = i.RequestPoGroupId
            })
            .ToListAsync();

        var obligations = DocumentObligationResolver.Resolve(
            d.SourceDocumentType, DocumentUsageContext.PaymentRequest);

        var reviewerName = d.ClassificationReviewedByUserId.HasValue
            ? await _context.Users.AsNoTracking()
                .Where(u => u.Id == d.ClassificationReviewedByUserId.Value)
                .Select(u => u.FullName).FirstOrDefaultAsync()
            : null;

        return new PaymentSourceDocumentDto
        {
            Id = d.Id,
            SequenceNumber = d.SequenceNumber,
            AttachmentId = d.AttachmentId,
            AttachmentFileName = attachment?.FileName,
            AttachmentStorageReference = attachment?.StorageReference,
            SupplierId = d.SupplierId,
            SupplierNameSnapshot = supplierName,
            SupplierTaxIdSnapshot = d.SupplierTaxIdSnapshot,
            PlantId = d.PlantId,
            PlantName = plantName,
            SourceDocumentType = d.SourceDocumentType,
            DocumentNumber = d.DocumentNumber,
            DocumentSeries = d.DocumentSeries,
            DocumentDate = d.DocumentDate,
            DueDate = d.DueDate,
            Currency = d.Currency,
            NetAmount = d.NetAmount,
            TaxAmount = d.TaxAmount,
            GrossAmount = d.GrossAmount,
            ItemsTotal = items.Sum(i => i.TotalAmount),
            OcrSuggestion = d.OcrSuggestion,
            OcrConfidence = d.OcrConfidence,
            OcrTitleFound = d.OcrTitleFound,
            OcrEvidenceJson = d.OcrEvidenceJson,
            OcrConflictingEvidenceJson = d.OcrConflictingEvidenceJson,
            ClassificationSource = d.ClassificationSource,
            ClassificationSuggestionSource = d.ClassificationSuggestionSource,
            ClassificationConflictAcknowledged = d.ClassificationConflictAcknowledged,
            ClassificationJustification = d.ClassificationJustification,
            ClassificationReviewedByFinance = d.ClassificationReviewedByFinance,
            ClassificationReviewedByUserId = d.ClassificationReviewedByUserId,
            ClassificationReviewedByName = reviewerName,
            ClassificationReviewedAtUtc = d.ClassificationReviewedAtUtc,
            RequiresOperationInvoice = obligations.RequiresOperationInvoice,
            RequiresAdvanceRegularization = obligations.RequiresAdvanceRegularization,
            RequiresFinanceClassificationReview = obligations.RequiresFinanceClassificationReview,
            IsVoided = d.IsVoided,
            VoidReason = d.VoidReason,
            Items = items,
            RowVersion = d.RowVersion
        };
    }
}
