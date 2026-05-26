using System.Text.Json;
using AlplaPortal.Application.Interfaces.Contracts;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Contracts;

/// <summary>
/// Orchestrates the async contract OCR pipeline:
/// 1. Loads the staged document and creates an ExtractionRecord (PENDING → PROCESSING)
/// 2. Calls the extraction provider (OpenAI)
/// 3. Normalises the raw DTO into typed portal values
/// 4. Persists per-field rows and updates the record status to COMPLETED | FAILED
/// 5. Updates Contract.OcrStatus so the frontend can poll for completion
/// </summary>
public class ContractOcrBackgroundProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentExtractionService _extraction;
    private readonly IContractOcrNormalisationService _normalisation;
    private readonly ILogger<ContractOcrBackgroundProcessor> _logger;

    // Raw JSON stored verbatim for debugging; capped to 64 KB
    private const int MaxRawJsonBytes = 65_536;

    public ContractOcrBackgroundProcessor(
        ApplicationDbContext db,
        IDocumentExtractionService extraction,
        IContractOcrNormalisationService normalisation,
        ILogger<ContractOcrBackgroundProcessor> logger)
    {
        _db          = db;
        _extraction  = extraction;
        _normalisation = normalisation;
        _logger      = logger;
    }

    /// <summary>
    /// Executes the full async OCR pipeline for a contract document.
    /// Designed to be called from a fire-and-forget Task in the API controller
    /// (using a scoped service factory so the DbContext lifetime is isolated).
    /// </summary>
    public async Task ProcessAsync(
        Guid contractId,
        Guid documentId,
        Guid triggeredByUserId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Contract OCR pipeline started — ContractId={ContractId}, DocumentId={DocumentId}, TriggeredBy={UserId}",
            contractId, documentId, triggeredByUserId);

        // ── 1. Load the contract document ────────────────────────────────
        var document = await _db.ContractDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ContractId == contractId, ct);

        if (document == null)
        {
            _logger.LogWarning("Contract OCR aborted — document {DocId} not found for contract {ContractId}",
                documentId, contractId);
            await SetContractOcrStatusAsync(contractId, ContractConstants.OcrStatuses.Failed, ct);
            return;
        }

        // ── 2. Create extraction record (PENDING) ─────────────────────────
        var record = new ContractOcrExtractionRecord
        {
            ContractId         = contractId,
            ContractDocumentId = documentId,
            TriggeredByUserId  = triggeredByUserId,
            TriggeredAtUtc     = DateTime.UtcNow,
            Status             = ContractConstants.OcrStatuses.Processing,
            ProviderName       = "OPENAI",
        };

        _db.ContractOcrExtractionRecords.Add(record);

        // Link the document back to this record (most recent wins)
        document.OcrExtractionRecordId = record.Id;

        await SetContractOcrStatusAsync(contractId, ContractConstants.OcrStatuses.Processing, ct, save: false);
        await _db.SaveChangesAsync(ct);

        // ── 3. Stream the document and call extraction provider ───────────
        try
        {
            var filePath = document.StoragePath;
            if (!File.Exists(filePath))
            {
                await FailRecordAsync(record, $"Document file not found at path: {filePath}", ct);
                return;
            }

            await using var fileStream = File.OpenRead(filePath);
            _logger.LogInformation(
                "[OCR-Processor] Calling extraction provider for ContractId={ContractId}, File='{FileName}', Path='{FilePath}'",
                contractId, document.FileName, filePath);

            var extractionResult = await _extraction.ExtractAsync(
                fileStream, document.FileName, sourceContext: "CONTRACT", ct: ct);

            _logger.LogInformation(
                "[OCR-Processor] Extraction provider returned for ContractId={ContractId}: Success={Success}, ContractNull={IsNull}, QualityScore={Quality}, RoutingStrategy={Strategy}",
                contractId, extractionResult.Success, extractionResult.Contract == null,
                extractionResult.QualityScore, extractionResult.Metadata?.RoutingStrategy ?? "(null)");

            if (!extractionResult.Success || extractionResult.Contract == null)
            {
                string reason = (!extractionResult.Success && extractionResult.Contract == null)
                    ? "Provider call returned Success=false and Contract=null. The extraction provider failed entirely — likely a JSON parse error, empty response, or HTTP error. Check [OCR-Contract] logs above."
                    : !extractionResult.Success
                        ? "Provider call returned Success=false with a non-null Contract. A JSON mapping error may have occurred. Check [OCR-Contract] MapContractFromJson logs."
                        : "Provider call returned Success=true but Contract DTO is null. This typically means the PDF was routed through the invoice pipeline (keyword triage mismatch). " +
                          "Verify that sourceContext='CONTRACT' is being forwarded correctly and that [OCR] routing logs show DocumentStrategy.Contract.";

                var err = $"{reason} QualityScore={extractionResult.QualityScore}";
                _logger.LogError(
                    "[OCR-Processor] FAILED for ContractId={ContractId} — {Reason}",
                    contractId, reason);
                await FailRecordAsync(record, err, ct);
                return;
            }

            // ── 4. Store raw LLM output ───────────────────────────────────
            var rawJson = JsonSerializer.Serialize(extractionResult.Contract);
            if (System.Text.Encoding.UTF8.GetByteCount(rawJson) > MaxRawJsonBytes)
                rawJson = rawJson[..MaxRawJsonBytes] + "…[TRUNCATED]";
            record.RawJsonResult = rawJson;

            // Populate provider metadata from extraction result
            record.ChunkCount          = extractionResult.Metadata?.ChunkCount ?? 1;
            record.TotalTokensUsed     = extractionResult.Metadata?.TotalTokens ?? 0;
            record.QualityScore        = extractionResult.QualityScore;
            record.IsPartial           = extractionResult.Metadata?.IsPartial ?? false;
            record.ConflictsDetected   = extractionResult.Metadata?.ConflictsDetected ?? false;
            record.NativeTextDetected  = extractionResult.Metadata?.NativeTextDetected ?? false;
            record.RoutingStrategy     = extractionResult.Metadata?.RoutingStrategy;

            // ── 5. Normalise ──────────────────────────────────────────────
            var normalised = await _normalisation.NormaliseAsync(extractionResult.Contract, ct);

            _logger.LogInformation(
                "[OCR-Processor] Building field rows from normalised result for ContractId={ContractId}, RecordId={RecordId}.",
                contractId, record.Id);
            var fields = BuildFieldRows(record, extractionResult.Contract, normalised);
            _logger.LogInformation(
                "[OCR-Processor] {FieldCount} field row(s) to persist for ContractId={ContractId}.",
                fields.Count, contractId);
            _db.ContractOcrExtractedFields.AddRange(fields);

            // ── 7. Mark COMPLETED ─────────────────────────────────────────
            record.Status        = ContractConstants.OcrStatuses.Completed;
            record.ProcessedAtUtc = DateTime.UtcNow;

            await SetContractOcrStatusAsync(contractId, ContractConstants.OcrStatuses.Completed, ct, save: false);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[OCR-Processor] Contract OCR COMPLETED — ContractId={ContractId}, RecordId={RecordId}, Fields={FieldCount}, Quality={Quality:F2}, RoutingStrategy={Strategy}",
                contractId, record.Id, fields.Count, record.QualityScore, record.RoutingStrategy ?? "(unknown)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in Contract OCR pipeline for ContractId={ContractId}", contractId);
            await FailRecordAsync(record, $"Unhandled error: {ex.Message}", ct);
        }
    }

    // ── Field row construction ───────────────────────────────────────────────

    private static List<ContractOcrExtractedField> BuildFieldRows(
        ContractOcrExtractionRecord record,
        AlplaPortal.Application.DTOs.Extraction.ExtractionContractDto raw,
        AlplaPortal.Application.Interfaces.Contracts.ContractOcrNormalisedResult n)
    {
        var fields = new List<ContractOcrExtractedField>();

        // State A — AUTO_FILL (dates)
        AddField(fields, record, "EffectiveDateUtc",
            raw.EffectiveDate,
            n.EffectiveDate?.ToString("yyyy-MM-dd"),
            n.EffectiveDateConf ?? raw.EffectiveDateConfidence,
            ContractConstants.OcrDisplayHints.AutoFill);

        AddField(fields, record, "ExpirationDateUtc",
            raw.EndDate,
            n.ExpirationDate?.ToString("yyyy-MM-dd"),
            n.ExpirationDateConf ?? raw.EndDateConfidence,
            ContractConstants.OcrDisplayHints.AutoFill);

        AddField(fields, record, "SignedAtUtc",
            raw.SignatureDate,
            n.SignatureDate?.ToString("yyyy-MM-dd"),
            n.SignatureDateConf ?? raw.SignatureDateConfidence,
            ContractConstants.OcrDisplayHints.AutoFill);

        // State A — AUTO_FILL (financial)
        AddField(fields, record, "TotalContractValue",
            raw.TotalContractValue,
            n.TotalContractValue?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            n.TotalContractValueConf ?? raw.TotalContractValueConfidence,
            ContractConstants.OcrDisplayHints.AutoFill);

        AddField(fields, record, "CurrencyId",
            raw.CurrencyRaw,
            n.CurrencyId?.ToString(),
            n.CurrencyConf ?? raw.CurrencyConfidence,
            ContractConstants.OcrDisplayHints.AutoFill);

        // State B — SUGGESTION
        AddField(fields, record, "Title",
            raw.Parties,
            n.SuggestedTitle,
            null,
            ContractConstants.OcrDisplayHints.Suggestion);

        AddField(fields, record, "CounterpartyName",
            raw.SupplierName,
            n.SuggestedCounterparty,
            null,
            ContractConstants.OcrDisplayHints.Suggestion);

        AddField(fields, record, "GoverningLaw",
            raw.GoverningLaw,
            n.SuggestedGoverningLaw,
            raw.GoverningLawConfidence,
            ContractConstants.OcrDisplayHints.Suggestion);

        AddField(fields, record, "PaymentTerms",
            raw.PaymentTerms,
            n.SuggestedPaymentTerms,
            null,
            ContractConstants.OcrDisplayHints.Suggestion);

        // Description: 1–2 sentence scope summary from the LLM object/scope clause.
        // Suppressed if both raw.ContractScope and n.SuggestedDescription are empty
        // (the Where filter at line ~299 removes empty-both rows automatically).
        AddField(fields, record, "Description",
            raw.ContractScope,
            n.SuggestedDescription,
            null,                                             // confidence: always suggestion, no threshold
            ContractConstants.OcrDisplayHints.Suggestion);

        if (n.SupplierMatch != null)
        {
            AddField(fields, record, "SupplierId",
                raw.SupplierName,
                n.SupplierMatch.SupplierId.ToString(),
                (decimal)n.SupplierMatch.MatchScore,
                ContractConstants.OcrDisplayHints.Suggestion);
        }

        // REFERENCE_ONLY — audit/display panel only
        AddField(fields, record, "TerminationClausesText",
            raw.TerminationClauses, raw.TerminationClauses, null,
            ContractConstants.OcrDisplayHints.ReferenceOnly);

        AddField(fields, record, "SignatoryNames",
            raw.SignatoryNames, raw.SignatoryNames, null,
            ContractConstants.OcrDisplayHints.ReferenceOnly);

        AddField(fields, record, "ExternalContractReference",
            raw.ExternalContractReference, raw.ExternalContractReference, null,
            ContractConstants.OcrDisplayHints.ReferenceOnly);

        // ── Phase 1.1 financial derivation rows ──────────────────────────
        // REFERENCE_ONLY: informational display in the panel (no form target)
        AddField(fields, record, "RecurringMonthlyAmount",
            raw.RecurringMonthlyAmount,
            n.RecurringMonthlyAmountParsed?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                ?? raw.RecurringMonthlyAmount,
            raw.RecurringMonthlyAmountConfidence,
            ContractConstants.OcrDisplayHints.ReferenceOnly);

        if (n.ContractDurationMonths.HasValue)
        {
            AddField(fields, record, "ContractDurationMonths",
                n.ContractDurationMonths.Value.ToString(),
                n.ContractDurationMonths.Value.ToString(),
                raw.ContractDurationMonthsConfidence,
                ContractConstants.OcrDisplayHints.ReferenceOnly);
        }

        // SUGGESTION: user must click Aplicar to push derived total into TotalContractValue form field
        if (n.SuggestedTotalContractValue.HasValue)
        {
            AddField(fields, record, "SuggestedTotalContractValue",
                n.RecurringMonthlyAmountRaw,
                n.SuggestedTotalContractValue.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                n.SuggestedTotalContractValueConf,
                ContractConstants.OcrDisplayHints.Suggestion);
        }

        // REFERENCE_ONLY: financial inconsistency warning flag — displayed as panel banner
        if (n.FinancialInconsistencyDetected)
        {
            AddField(fields, record, "FinancialInconsistencyWarning",
                raw.WrittenAmountText,
                "true",
                null,
                ContractConstants.OcrDisplayHints.ReferenceOnly);
        }

        // Remove rows where both raw and normalised are empty (nothing to show)
        return fields.Where(f =>
            !string.IsNullOrWhiteSpace(f.RawExtractedValue) ||
            !string.IsNullOrWhiteSpace(f.NormalisedValue)).ToList();
    }


    private static void AddField(
        List<ContractOcrExtractedField> list,
        ContractOcrExtractionRecord record,
        string fieldName,
        string? rawValue,
        string? normalisedValue,
        decimal? confidence,
        string displayHint)
    {
        list.Add(new ContractOcrExtractedField
        {
            ExtractionRecordId = record.Id,
            ContractId         = record.ContractId,
            FieldName          = fieldName,
            RawExtractedValue  = rawValue,
            NormalisedValue    = normalisedValue,
            ConfidenceScore    = confidence,
            DisplayHint        = displayHint,
            ConfirmedByUser    = false,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task FailRecordAsync(ContractOcrExtractionRecord record, string error, CancellationToken ct)
    {
        _logger.LogWarning("Contract OCR failed — RecordId={RecordId}, Error={Error}", record.Id, error);
        record.Status         = ContractConstants.OcrStatuses.Failed;
        record.ErrorMessage   = error;
        record.ProcessedAtUtc = DateTime.UtcNow;
        await SetContractOcrStatusAsync(record.ContractId, ContractConstants.OcrStatuses.Failed, ct, save: false);
        try { await _db.SaveChangesAsync(ct); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to persist OCR failure record"); }
    }

    private async Task SetContractOcrStatusAsync(
        Guid contractId, string status, CancellationToken ct, bool save = true)
    {
        var contract = await _db.Contracts.FindAsync(new object[] { contractId }, ct);
        if (contract == null) return;
        contract.OcrStatus = status;
        if (save) await _db.SaveChangesAsync(ct);
    }
}
