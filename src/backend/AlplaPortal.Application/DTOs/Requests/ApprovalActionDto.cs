namespace AlplaPortal.Application.DTOs.Requests;

public class ApprovalActionDto
{
    public string? Comment { get; set; }
    public Guid? SelectedQuotationId { get; set; }
    public Dictionary<Guid, ItemApprovalAssignmentDto>? ItemAssignments { get; set; }
    public Dictionary<Guid, List<ItemAllocationLineDto>>? ItemAllocations { get; set; }
    public Dictionary<Guid, Guid>? ItemAwards { get; set; } // Maps RequestLineItemId -> QuotationItemId

    // Financial Integrity Gate — Override Fields
    // Used at CompleteQuotation when OCR total diverges from quotation total
    public bool FinancialIntegrityOverride { get; set; }
    public string? OverrideJustification { get; set; }

    // Budget Availability Step - Justification for over-budget or missing budget
    public string? BudgetJustification { get; set; }

    // Alternative Budget Lines Reassignments
    public List<AllocationReassignmentDto> Reassignments { get; set; } = new();

    // Extra Items Decisions
    public Dictionary<Guid, ExtraItemDecisionDto>? ExtraItemDecisions { get; set; }
}

public class ExtraItemDecisionDto
{
    public string Decision { get; set; } = null!; // INCLUDE, EXCLUDE (legacy: APPROVE, REJECT)
    public string? Comment { get; set; }
}

public class ItemApprovalAssignmentDto
{
    public int? PlantId { get; set; }
    public int? CostCenterId { get; set; }
}

public class ItemAllocationLineDto
{
    public int PlantId { get; set; }
    public int CostCenterId { get; set; }
    public decimal Percentage { get; set; }
    public string? Comment { get; set; }
}

public class AllocationReassignmentDto
{
    public int OldPlantId { get; set; }
    public int? OldCostCenterId { get; set; }
    public int NewPlantId { get; set; }
    public int? NewCostCenterId { get; set; }
    public List<Guid> AffectedItemIds { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}
