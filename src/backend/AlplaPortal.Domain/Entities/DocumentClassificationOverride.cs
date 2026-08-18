using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// A deliberate contradiction of what the document was read to be.
///
/// <para>The current classification lives on <see cref="Request"/> / <see cref="Quotation"/>; this
/// table records the moments a human overrode the evidence, and why. Those are different questions:
/// "what is this document classified as" is answered by the entity, "who decided that against the
/// evidence, when, and on what grounds" is answered here — and the second question survives later
/// corrections to the first.</para>
///
/// <para>Rows are keyed by <see cref="IdempotencyKey"/> so that re-saving a draft carrying an
/// already-confirmed decision writes nothing, while changing the decision writes a new row. The
/// history is therefore append-only and free of save-noise.</para>
/// </summary>
public class DocumentClassificationOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>PAYMENT_REQUEST or QUOTATION_MANAGEMENT — see DocumentClassificationContexts.</summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// Always set, including for quotation overrides: a quotation belongs to exactly one request,
    /// and anchoring every row to the request keeps the audit reachable from the request timeline.
    /// </summary>
    public Guid RequestId { get; set; }
    public Request? Request { get; set; }

    /// <summary>Set only for QUOTATION_MANAGEMENT overrides.</summary>
    public Guid? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    /// <summary>The document whose reading was overridden. Null when none was attached yet.</summary>
    public Guid? AttachmentId { get; set; }

    // ── What the evidence said ──
    /// <summary>The type proposed by extraction or by the fallback heuristics.</summary>
    public string? SuggestedType { get; set; }

    /// <summary>Confidence behind <see cref="SuggestedType"/> (0.0–1.0).</summary>
    public decimal? Confidence { get; set; }

    /// <summary>Verbatim document title the suggestion was drawn from, when there was one.</summary>
    public string? TitleFound { get; set; }

    /// <summary>Serialized supporting evidence.</summary>
    public string? EvidenceJson { get; set; }

    /// <summary>Serialized evidence that pointed away from the suggestion.</summary>
    public string? ConflictingEvidenceJson { get; set; }

    /// <summary>OCR (the provider read the document) or FALLBACK (Portal heuristics only).</summary>
    public string? SuggestionSource { get; set; }

    // ── What the human decided ──
    /// <summary>The type the user chose instead.</summary>
    public string SelectedType { get; set; } = string.Empty;

    /// <summary>The user confirmed the contradiction explicitly.</summary>
    public bool Acknowledged { get; set; }

    /// <summary>The written reason. Required for high-risk overrides.</summary>
    public string? Justification { get; set; }

    // ── Who and when ──
    public Guid ActorUserId { get; set; }
    public User? ActorUser { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// DC_OVERRIDE:{Context}:{ScopeId}:{AttachmentId}:{SelectedType}, built exclusively by
    /// <see cref="Services.PostPaymentIdempotencyKeys.DocumentClassificationOverride"/>.
    /// Unique — the same decision can never be recorded twice.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
