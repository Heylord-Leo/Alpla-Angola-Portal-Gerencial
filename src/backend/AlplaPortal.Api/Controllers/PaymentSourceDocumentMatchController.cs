using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Api.Helpers;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Review-time candidate matching for PAYMENT source documents (v2.229.10 L4 candidate flow).
///
/// <para>Exists because the business-duplicate intelligence used to fire only at persistence —
/// the whole review experience presented every document as new, and a supplier-resolution failure
/// silenced even that. This endpoint runs the SAME assembly and the SAME rule engine
/// (<see cref="PaymentSourceDocumentCandidateSearch"/> + <see cref="PaymentSourceDocumentDuplicateHierarchy"/>)
/// the persistence guard uses, so what the UI predicts and what persistence enforces cannot
/// drift. Advisory only — Create/Update re-decide authoritatively.</para>
///
/// <para>Not request-scoped on purpose: the creation wizard needs an answer before any request
/// exists. Candidates on requests outside the caller's scope are still REPORTED (the duplicate
/// signal must survive) but disclose no identifying metadata or values.</para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/payment-source-documents")]
public class PaymentSourceDocumentMatchController : BaseController
{
    private readonly ILogger<PaymentSourceDocumentMatchController> _logger;

    public PaymentSourceDocumentMatchController(
        ApplicationDbContext context,
        ILogger<PaymentSourceDocumentMatchController> logger) : base(context)
    {
        _logger = logger;
    }

    [HttpPost("match-candidates")]
    public async Task<IActionResult> MatchCandidates([FromBody] MatchSourceDocumentCandidatesDto dto)
    {
        try
        {
            return await MatchCandidatesCoreAsync(dto);
        }
        catch (Exception ex)
        {
            // Advisory endpoint: never leak internals. The persistence guard remains the
            // enforcement, so a concise failure here costs nothing but a missing hint.
            _logger.LogError(ex,
                "[DupCandidates] Preflight failed for number '{Number}'.", dto.DocumentNumber);
            return StatusCode(500, new ProblemDetails
            {
                Title = "Validação de duplicados indisponível",
                Detail = "Não foi possível verificar documentos semelhantes neste momento.",
                Status = 500
            });
        }
    }

    private async Task<IActionResult> MatchCandidatesCoreAsync(MatchSourceDocumentCandidatesDto dto)
    {
        var result = new SourceDocumentCandidatesResultDto();
        if (string.IsNullOrWhiteSpace(dto.DocumentNumber)) return Ok(result);

        // SOURCE EVIDENCE wins for matching: the question is whether the physical document
        // already exists, so what the paper says outranks what the draft kept. The visual L4
        // regression this fixes: a draft that retained an older date/total made a modified
        // document read as "the same commercial identity". Accepted draft values stay untouched
        // and remain what persistence validates.
        var input = new CandidateSearchInput
        {
            CurrentRequestId = dto.ExcludeRequestId,
            ExcludeDocumentId = dto.ExcludeDocumentId,
            CompanyId = dto.CompanyId,
            SupplierId = dto.SupplierId,
            SupplierName = dto.SupplierName,
            SupplierTaxId = dto.OcrSupplierTaxId ?? dto.SupplierTaxId,
            DocumentNumber = dto.OcrDocumentNumber ?? dto.DocumentNumber,
            DocumentSeries = dto.DocumentSeries,
            DocumentDate = dto.OcrDocumentDate ?? dto.DocumentDate,
            Currency = dto.OcrCurrency ?? dto.Currency,
            GrossAmount = dto.OcrGrossAmount ?? dto.GrossAmount
            // No item fingerprint at review time: the incoming items are not persisted yet, and
            // the hierarchy treats absence as "no evidence", exactly as the guard will at Create.
        };

        var comparands = await PaymentSourceDocumentCandidateSearch.AssembleComparandsAsync(_context, input);
        var candidate = PaymentSourceDocumentCandidateSearch.BuildCandidate(input);
        var decisions = PaymentSourceDocumentDuplicateHierarchy.EvaluateAll(candidate, comparands);

        result.NormalizedDocumentNumber =
            PaymentSourceDocumentFingerprint.NormalizeReference(dto.DocumentNumber);

        if (decisions.Count == 0)
        {
            _logger.LogInformation(
                "[DupCandidates] Preflight: number='{Number}' normalized='{Normalized}' supplierId={SupplierId} " +
                "comparands={ComparandCount} candidates=0",
                dto.DocumentNumber, result.NormalizedDocumentNumber, dto.SupplierId, comparands.Count);
            return Ok(result);
        }

        var scoped = await GetScopedRequestsQuery();
        var candidateRequestIds = decisions
            .Where(d => d.Match?.RequestId != null)
            .Select(d => d.Match!.RequestId!.Value)
            .Distinct()
            .ToList();
        var visibleRequestIds = await scoped
            .Where(r => candidateRequestIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync();

        foreach (var decision in decisions)
        {
            var match = decision.Match!;
            // Within-request comparands (no RequestId) are the user's own screen — visible.
            var visible = match.RequestId == null || visibleRequestIds.Contains(match.RequestId.Value);

            result.Candidates.Add(new SourceDocumentCandidateDto
            {
                Classification = ToWire(decision.Classification),
                Verdict = decision.Verdict switch
                {
                    BusinessDuplicateVerdict.Block => "BLOCK",
                    BusinessDuplicateVerdict.Ambiguous => "AMBIGUOUS",
                    _ => "ALLOW"
                },
                Reason = decision.Reason,
                MatchingFields = decision.MatchingFields.ToList(),
                ConflictingFields = decision.ConflictingFields.ToList(),
                RequestVisible = visible,
                RequestId = visible ? match.RequestId : null,
                RequestNumber = visible ? match.RequestNumber : null,
                DocumentId = visible ? match.Id : null,
                SequenceNumber = visible ? match.SequenceNumber : null,
                Existing = visible
                    ? new SourceDocumentCandidateValuesDto
                    {
                        SupplierName = match.SupplierName,
                        SupplierTaxId = match.SupplierTaxId,
                        DocumentNumber = match.DocumentNumber,
                        DocumentDate = match.DocumentDate,
                        Currency = match.Currency,
                        GrossAmount = match.GrossAmount
                    }
                    : null
            });
        }

        result.TopClassification = result.Candidates[0].Classification;

        _logger.LogInformation(
            "[DupCandidates] Preflight: number='{Number}' normalized='{Normalized}' supplierId={SupplierId} " +
            "comparands={ComparandCount} candidates={CandidateCount} top={Top}",
            dto.DocumentNumber, result.NormalizedDocumentNumber, dto.SupplierId,
            comparands.Count, result.Candidates.Count, result.TopClassification);

        return Ok(result);
    }

    private static string ToWire(BusinessDuplicateClassification classification) => classification switch
    {
        BusinessDuplicateClassification.SemanticDuplicate => "SEMANTIC_DUPLICATE",
        BusinessDuplicateClassification.StrongBusinessDuplicate => "STRONG_BUSINESS_DUPLICATE",
        BusinessDuplicateClassification.AmbiguousMatch => "AMBIGUOUS_MATCH",
        BusinessDuplicateClassification.RelatedDocument => "RELATED_DOCUMENT",
        _ => "NONE"
    };
}
