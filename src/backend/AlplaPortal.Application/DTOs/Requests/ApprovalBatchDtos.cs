namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>
/// DTO for creating an ApprovalBatch via POST /api/v1/requests/{id}/batches.
/// </summary>
public class CreateApprovalBatchDto
{
    /// <summary>Items to include in the batch, each with a buyer-selected winner.</summary>
    public List<BatchItemDto> Items { get; set; } = new();

    /// <summary>Optional buyer comment describing the batch.</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Single item within a batch creation request.
/// </summary>
public class BatchItemDto
{
    /// <summary>The request line item to include in the batch.</summary>
    public Guid RequestLineItemId { get; set; }

    /// <summary>The quotation item selected as the winner for this line item.</summary>
    public Guid SelectedQuotationItemId { get; set; }
}

/// <summary>
/// Detailed response DTO for a single ApprovalBatch (used for GET detail and POST response).
/// </summary>
public class ApprovalBatchDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public int BatchNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public decimal? ApprovedTotalAmount { get; set; }
    public string? BudgetJustification { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public List<ApprovalBatchItemDto> Items { get; set; } = new();
}

/// <summary>
/// Response DTO for a single item within an ApprovalBatch.
/// </summary>
public class ApprovalBatchItemDto
{
    public Guid Id { get; set; }
    public Guid RequestLineItemId { get; set; }
    public string? RequestLineItemDescription { get; set; }
    public int? RequestLineItemLineNumber { get; set; }
    public decimal? RequestLineItemQuantity { get; set; }

    public Guid SelectedQuotationItemId { get; set; }
    public string? SelectedQuotationItemDescription { get; set; }
    public decimal? SelectedQuotationItemUnitPrice { get; set; }
    public decimal? SelectedQuotationItemLineTotal { get; set; }
    public string? SupplierName { get; set; }
}

/// <summary>
/// Summary response DTO for listing batches (used for GET list).
/// </summary>
public class ApprovalBatchListItemDto
{
    public Guid Id { get; set; }
    public int BatchNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public int ItemCount { get; set; }
    public decimal? ApprovedTotalAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
}

/// <summary>
/// DTO for batch-level area approval actions (approve / reject / request-adjustment).
/// Mirrors ApprovalActionDto but omits ItemAwards, SelectedQuotationId, and FinancialIntegrityOverride.
/// Winners are read from ApprovalBatchItem.SelectedQuotationItemId.
/// </summary>
public class BatchApprovalActionDto
{
    public string? Comment { get; set; }

    /// <summary>Maps RequestLineItemId → {PlantId, CostCenterId} (single-allocation shorthand).</summary>
    public Dictionary<Guid, ItemApprovalAssignmentDto>? ItemAssignments { get; set; }

    /// <summary>Maps RequestLineItemId → List of allocation lines (multi-allocation).</summary>
    public Dictionary<Guid, List<ItemAllocationLineDto>>? ItemAllocations { get; set; }

    /// <summary>Budget justification (required when budget status is critical/over-budget).</summary>
    public string? BudgetJustification { get; set; }

    /// <summary>Alternative budget line reassignments.</summary>
    public List<AllocationReassignmentDto> Reassignments { get; set; } = new();

    /// <summary>Extra item decisions (APPROVE / REJECT). Keys are QuotationItemIds.</summary>
    public Dictionary<Guid, ExtraItemDecisionDto>? ExtraItemDecisions { get; set; }
}

/// <summary>
/// DTO for editing a batch during rework (AREA_ADJUSTMENT or FINAL_ADJUSTMENT).
/// Allows changing items and their selected winners.
/// </summary>
public class UpdateApprovalBatchDto
{
    /// <summary>Updated list of items. Allows adding/removing items and changing SelectedQuotationItemId.</summary>
    public List<BatchItemDto> Items { get; set; } = new();

    /// <summary>Optional updated comment.</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// DTO for cancelling/voiding an ApprovalBatch.
/// </summary>
public class CancelApprovalBatchRequestDto
{
    /// <summary>Mandatory justification explaining why the batch is being cancelled.</summary>
    public string Justification { get; set; } = string.Empty;
}
