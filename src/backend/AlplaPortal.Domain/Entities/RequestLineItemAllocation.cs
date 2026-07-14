using System;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Represents a financial allocation line for a RequestLineItem.
/// Each item can have 1..N allocation lines, each assigning a percentage
/// of the item's total amount to a specific Plant and Cost Center.
/// The sum of percentages per item must equal exactly 100%.
/// </summary>
public class RequestLineItemAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestLineItemId { get; set; }
    public RequestLineItem RequestLineItem { get; set; } = null!;

    public int PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    public int CostCenterId { get; set; }
    public CostCenter CostCenter { get; set; } = null!;

    /// <summary>Percentage of item amount allocated (> 0, sum per item = 100). Stored as decimal(9,4).</summary>
    public decimal Percentage { get; set; }

    /// <summary>0-based order preserving the approver's input sequence. Used for deterministic tie-breaking and stable UI rendering.</summary>
    public int AllocationOrder { get; set; }

    /// <summary>Optional justification for this allocation line (max 500 chars).</summary>
    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
