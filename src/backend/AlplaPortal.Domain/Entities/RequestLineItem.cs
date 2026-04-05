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

    public string? Notes { get; set; }
    
    public DateTime? DueDate { get; set; }

    // Receiving Fields
    public decimal ReceivedQuantity { get; set; }
    public string? DivergenceNotes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
