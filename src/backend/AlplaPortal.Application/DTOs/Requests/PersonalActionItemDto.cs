namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// v2.242.0 Phase 1 — a single actionable unit of a Buyer/user's personal work, derived (never
/// persisted) from the request/group graph. One request can yield several items (one per actionable
/// group or per scalar action). Group-level actions carry <see cref="PoGroupId"/>; a card that groups
/// several same-owner same-action groups lists them in <see cref="AffectedSuppliers"/>.
/// </summary>
public class PersonalActionItemDto
{
    public string ActionId { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string RequestTitle { get; set; } = string.Empty;
    public string RequestTypeCode { get; set; } = string.Empty;

    public string ActionType { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string ActionStatus { get; set; } = string.Empty;

    public Guid? PoGroupId { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public List<BuyerPoCorrectionGroupDto> AffectedSuppliers { get; set; } = new();

    public Guid OwnerUserId { get; set; }
    public string? OwnerName { get; set; }

    public DateTime? DueDateUtc { get; set; }
    public string? NeedLevelCode { get; set; }
    public DateTime? StageEnteredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsOverdue { get; set; }
    public string PriorityBand { get; set; } = string.Empty;

    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }

    /// <summary>Future-ready deep-link hint (v2.243.0 consumes it): identifies the exact action.</summary>
    public string Route { get; set; } = string.Empty;
}

/// <summary>One action category (chip) with its count and highest priority band present.</summary>
public class MyActionCategoryDto
{
    public string ActionType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public string? HighestPriorityBand { get; set; }
}

/// <summary>my-actions response: the full category summary (chips) plus the paged item slice.</summary>
public class MyActionsResponseDto
{
    public List<MyActionCategoryDto> Categories { get; set; } = new();
    public List<PersonalActionItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
