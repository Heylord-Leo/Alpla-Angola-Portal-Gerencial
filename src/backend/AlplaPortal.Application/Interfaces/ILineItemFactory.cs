using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// Input for creating a <see cref="RequestLineItem"/>. Controllers are responsible for
/// authorization, state guards and context resolution; this spec carries only the
/// already-validated data needed to build and stage the entity.
/// </summary>
public sealed class LineItemCreationSpec
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int? UnitId { get; set; }
    public decimal UnitPrice { get; set; }
    public int? CurrencyId { get; set; }
    public int? PlantId { get; set; }
    public int? CostCenterId { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public int? IvaRateId { get; set; }
    public int? ItemCatalogId { get; set; }
    public string? SupplierName { get; set; }
    public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
    public string ItemPriority { get; set; } = "MEDIUM";

    /// <summary>Optional explicit lifecycle status (e.g. QUOTATION_PENDING). Null = legacy/uninitialized.</summary>
    public string? QuotationLifecycleStatus { get; set; }

    // ── Provenance / idempotency ──
    public string? CreationOrigin { get; set; }
    public Guid? SourceProformaAttachmentId { get; set; }
    public string? CreationIdempotencyKey { get; set; }

    // ── History ──
    /// <summary>RequestStatusHistory.ActionTaken code recorded for this creation.</summary>
    public string HistoryAction { get; set; } = Domain.Constants.LineItemHistoryActions.ItemAdded;
    /// <summary>Optional pre-built history comment. When null the factory builds a default one.</summary>
    public string? HistoryComment { get; set; }
}

/// <summary>
/// Centralizes creation of <see cref="RequestLineItem"/> so the standard add-item endpoint and the
/// buyer-reconciliation workaround do not duplicate quantity/unit/total/line-number/status/history/
/// provenance logic. The factory stages the entity and a history entry into the tracked context
/// (it does NOT call SaveChanges); the caller owns the transaction boundary and the total recompute.
/// </summary>
public interface ILineItemFactory
{
    /// <summary>
    /// Builds a <see cref="RequestLineItem"/> from <paramref name="spec"/> and the parent
    /// <paramref name="request"/> (line-number, initial status, currency, supplier inheritance,
    /// total math, provenance), plus a <see cref="RequestStatusHistory"/> entry, and adds both to
    /// the context without saving. Returns the new item.
    /// </summary>
    Task<RequestLineItem> BuildAndStageAsync(Request request, LineItemCreationSpec spec, Guid actorId, string actorFullName);
}
