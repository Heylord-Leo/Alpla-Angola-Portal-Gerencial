using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Explicit, auditable Buyer authorization to reuse ONE quotation item that was previously
/// used (selected) in a CANCELLED approval batch (Option C — no silent reuse).
///
/// Granularity is strictly per QuotationItem: the "reuse entire quotation" UI action creates
/// one record per eligible item, enabling partial reuse/revocation and exact audit.
///
/// Lifecycle: created active → either CONSUMED by the batch that uses the item (IsActive=false,
/// ConsumedByApprovalBatchId set — never reusable again) or REVOKED by a buyer while still
/// unconsumed (IsActive=false, revocation metadata set). A new cancelled use of the item
/// requires a brand-new authorization for that new source batch.
/// </summary>
public class QuotationReuseAuthorization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public Guid QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;

    public Guid QuotationItemId { get; set; }
    public QuotationItem QuotationItem { get; set; } = null!;

    /// <summary>The CANCELLED batch in which the item had been selected — the reuse's provenance.</summary>
    public Guid SourceApprovalBatchId { get; set; }
    public ApprovalBatch SourceApprovalBatch { get; set; } = null!;

    public Guid AuthorizedByUserId { get; set; }
    public DateTime AuthorizedAtUtc { get; set; }

    /// <summary>Mandatory buyer justification for making the item eligible again.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Active = eligible for a future batch. False once consumed or revoked.</summary>
    public bool IsActive { get; set; } = true;

    // ── Consumption (set atomically by the batch that uses the item) ──
    public Guid? ConsumedByApprovalBatchId { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }

    // ── Revocation (only while active and unconsumed) ──
    public Guid? RevokedByUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
