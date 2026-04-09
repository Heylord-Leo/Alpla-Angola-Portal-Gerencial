using System;

namespace AlplaPortal.Domain.Entities;

public class QuotationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public Quotation Quotation { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }
    
    // Deterministic ordering
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    
    // New Financial Fields (Step 10 Refinement)
    public int LineNumber { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? IvaRateId { get; set; }
    public IvaRate? IvaRate { get; set; }
    public decimal IvaRatePercent { get; set; } // Snapshot at the time of creation/update
    public decimal GrossSubtotal { get; set; }
    public decimal IvaAmount { get; set; }

    // Receiving Fields
    public decimal ReceivedQuantity { get; set; }
    public string? DivergenceNotes { get; set; }
    public int? LineItemStatusId { get; set; }
    public LineItemStatus? LineItemStatus { get; set; }
}
