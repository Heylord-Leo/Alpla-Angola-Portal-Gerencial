using AlplaPortal.Application.DTOs.Requests;
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

    public PaymentSourceDocumentsController(
        ApplicationDbContext context,
        ILogger<PaymentSourceDocumentsController> logger) : base(context)
    {
        _logger = logger;
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

        ApplyFields(document, dto);
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedByUserId = CurrentUserId;

        var overrideProblem = await StageClassificationOverrideAsync(document, request!);
        if (overrideProblem != null) return BadRequest(overrideProblem);

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
