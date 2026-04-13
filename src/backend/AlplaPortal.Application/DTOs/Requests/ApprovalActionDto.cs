namespace AlplaPortal.Application.DTOs.Requests;

public class ApprovalActionDto
{
    public string? Comment { get; set; }
    public Guid? SelectedQuotationId { get; set; }
    public Dictionary<Guid, ItemApprovalAssignmentDto>? ItemAssignments { get; set; }

    // Financial Integrity Gate — Override Fields
    // Used at CompleteQuotation when OCR total diverges from quotation total
    public bool FinancialIntegrityOverride { get; set; }
    public string? OverrideJustification { get; set; }
}

public class ItemApprovalAssignmentDto
{
    public int PlantId { get; set; }
    public int CostCenterId { get; set; }
}
