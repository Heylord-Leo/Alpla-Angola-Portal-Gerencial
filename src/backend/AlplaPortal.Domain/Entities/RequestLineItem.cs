using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Domain.Entities;

public class RequestLineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    /// <summary>Technical row order within the request. Not to be confused with business ItemPriority.</summary>
    public int LineNumber { get; set; }

    /// <summary>Business priority classification: HIGH, MEDIUM, LOW.</summary>
    public string ItemPriority { get; set; } = "MEDIUM";

    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }

    // Kept for direct fast querying, though typically mathematically computed
    public decimal TotalAmount { get; set; } 

    // Optional override. Usually defaults to parent Request Currency
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>Item lifecycle status. Assigned automatically by backend; buyer-controlled in future phases.</summary>
    public int? LineItemStatusId { get; set; }
    public LineItemStatus? LineItemStatus { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string? SupplierName { get; set; }

    public int? CostCenterId { get; set; }
    public CostCenter? CostCenter { get; set; }

    public int? IvaRateId { get; set; }
    public IvaRate? IvaRate { get; set; }

    /// <summary>Optional reference to the master item catalog. Null = manual/free-text item.</summary>
    public int? ItemCatalogId { get; set; }
    public ItemCatalog? ItemCatalogItem { get; set; }

    public string? Notes { get; set; }
    
    public DateTime? DueDate { get; set; }

    // Receiving Fields
    public decimal ReceivedQuantity { get; set; }
    public string? DivergenceNotes { get; set; }

    public bool IsDeleted { get; set; }

    public Guid? RequestPoGroupId { get; set; }
    public RequestPoGroup? RequestPoGroup { get; set; }

    /// <summary>
    /// PAYMENT only: the source document this item is being paid against. Authoritative — the
    /// distribution of a source document across PO groups is derived from its items' RequestPoGroupId,
    /// which is why no separate allocation table exists. Null on QUOTATION items and on PAYMENT items
    /// created before the multi-document feature.
    /// </summary>
    public Guid? PaymentSourceDocumentId { get; set; }
    public PaymentSourceDocument? PaymentSourceDocument { get; set; }

    public Guid? SelectedQuotationItemId { get; set; }
    public QuotationItem? SelectedQuotationItem { get; set; }

    // ── Partial Quotation Approval: Lifecycle Fields ──

    /// <summary>
    /// Quotation lifecycle status for partial batch approval:
    /// null (legacy/pending), QUOTATION_PENDING, BATCH_ASSIGNED, QUOTATION_APPROVED,
    /// NOT_QUOTED_PROPOSED, NOT_QUOTED_ACCEPTED.
    /// </summary>
    public string? QuotationLifecycleStatus { get; set; }

    /// <summary>Justification required when proposing item as not-quoted (min 20 chars).</summary>
    public string? NotQuotedJustification { get; set; }

    /// <summary>User who proposed the item as not-quoted.</summary>
    public Guid? NotQuotedProposedByUserId { get; set; }

    /// <summary>UTC timestamp when item was proposed as not-quoted.</summary>
    public DateTime? NotQuotedProposedAtUtc { get; set; }

    /// <summary>User who accepted/rejected the not-quoted proposal.</summary>
    public Guid? NotQuotedDecisionByUserId { get; set; }

    /// <summary>UTC timestamp of the not-quoted acceptance/rejection.</summary>
    public DateTime? NotQuotedDecisionAtUtc { get; set; }

    /// <summary>Comment from the decision-maker when accepting/rejecting the not-quoted proposal (e.g., rejection reason).</summary>
    public string? NotQuotedDecisionComment { get; set; }

    /// <summary>Financial allocation lines (1..N, sum of percentages = 100%). Used for split allocation across plants/cost centers.</summary>
    public ICollection<RequestLineItemAllocation> Allocations { get; set; } = new List<RequestLineItemAllocation>();

    // ── Provenance & idempotency (buyer reconciliation workaround) ──

    /// <summary>
    /// How this line was created. Null = standard requester/create flow.
    /// Known value: "BUYER_RECONCILIATION" — created by the Buyer from a proforma line
    /// during quotation reconciliation to cover an omitted requested item.
    /// See <see cref="AlplaPortal.Domain.Constants.LineItemCreationOrigins"/>.
    /// </summary>
    public string? CreationOrigin { get; set; }

    /// <summary>
    /// Soft reference to the proforma <see cref="RequestAttachment"/> the line was derived from,
    /// when created via the reconciliation workaround. Used for cross-session duplicate detection.
    /// No hard FK to avoid delete-cascade coupling.
    /// </summary>
    public Guid? SourceProformaAttachmentId { get; set; }

    /// <summary>
    /// Client-supplied idempotency token (UUID) for the single "add as requested item" operation.
    /// Persisted with a unique filtered index so retries/double-clicks/races return the same line
    /// instead of creating a duplicate. Session-scoped: does NOT dedupe across sessions
    /// (that is handled by <see cref="SourceProformaAttachmentId"/>-based detection).
    /// </summary>
    public string? CreationIdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
