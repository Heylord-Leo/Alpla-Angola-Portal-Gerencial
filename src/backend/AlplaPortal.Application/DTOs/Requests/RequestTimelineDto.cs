namespace AlplaPortal.Application.DTOs.Requests;

public class RequestTimelineDto
{
    public List<TimelineStepDto> Steps { get; set; } = new();

    /// <summary>
    /// v2.230.0 — per-lot progress timelines for the Requests-list expanded row. Populated
    /// ONLY when the request has ≥ 2 logical operational units (multi-lot QUOTATION);
    /// null otherwise so single-lot/PAYMENT/legacy consumers keep the Steps path unchanged.
    /// </summary>
    public List<LotTimelineDto>? Lots { get; set; }
}

/// <summary>One logical lot (ApprovalBatch and/or its RequestPoGroup) with its own progress timeline.</summary>
public class LotTimelineDto
{
    public string UnitType { get; set; } = string.Empty;   // BATCH | GROUP
    public Guid UnitId { get; set; }
    /// <summary>Real ApprovalBatch.BatchNumber (own or origin batch); null for legacy/batchless groups — never fabricated.</summary>
    public int? LotNumber { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public decimal TotalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public List<TimelineStepDto> Steps { get; set; } = new();
}

public class TimelineStepDto
{
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset? CompletedAt { get; set; }
    public string State { get; set; } = "pending"; // completed, current, pending, blocked
}
