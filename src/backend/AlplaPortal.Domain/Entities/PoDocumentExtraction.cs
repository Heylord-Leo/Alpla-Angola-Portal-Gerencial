using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AlplaPortal.Domain.Services;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// v2.249.4 — SERVER-HELD extraction evidence for a purchase-order document. Written only by
/// <c>POST /api/v1/requests/{id}/po-document-extract</c> (the P.O. registration and correction wizards) from what the
/// extraction provider read, and bound to the request and to the SHA-256 of the bytes the server itself hashed.
/// <c>register-po</c> looks the evidence up by (request, hash of the registered attachment): the client can neither supply
/// nor alter the facts, and a different file (another attachment, a file swapped after the extraction) simply has no
/// evidence → UNCERTAIN → explicit confirmation + justification. Append-only; never updated.
/// </summary>
public class PoDocumentExtraction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    /// <summary>Lower-case hex SHA-256 of the bytes the provider read — the same algorithm the attachment upload stores.</summary>
    public string FileHash { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    public Guid ExtractedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>False when the provider answered but produced nothing usable (envelope success=false).</summary>
    public bool Success { get; set; }
    public string? ProviderStatusCode { get; set; }

    // ── What the pure identifier concluded at extraction time (informational; register-po re-derives from the facts) ──
    public string? Verdict { get; set; }
    public string? ReasonCode { get; set; }
    public string? Reference { get; set; }
    public string? Family { get; set; }

    // ── The facts (identity + business values) ──
    public string? DocumentNumber { get; set; }
    public string? DocumentTitle { get; set; }
    public string? ClassificationType { get; set; }
    public decimal? ClassificationConfidence { get; set; }
    /// <summary>JSON array of other document numbers mentioned in the body (never the document's own number).</summary>
    public string? ReferencedDocumentNumbersJson { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierTaxId { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? GrandTotal { get; set; }

    public IReadOnlyList<string> ReferencedDocumentNumbers()
    {
        if (string.IsNullOrWhiteSpace(ReferencedDocumentNumbersJson)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(ReferencedDocumentNumbersJson)?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>The identifier input built from THIS row — the only input <c>register-po</c> ever uses.</summary>
    public PoDocumentIdentificationInput ToIdentificationInput() => new()
    {
        DocumentNumber = DocumentNumber,
        DocumentTitle = DocumentTitle,
        ClassificationType = ClassificationType,
        ClassificationConfidence = ClassificationConfidence,
        ReferencedDocumentNumbers = ReferencedDocumentNumbers(),
        ExtractionPerformed = Success,
    };
}
