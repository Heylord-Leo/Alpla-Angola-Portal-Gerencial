namespace AlplaPortal.Application.DTOs.Requests;

public class ApprovalActionDto
{
    public string? Comment { get; set; }
    public Guid? SelectedQuotationId { get; set; }
    public Dictionary<Guid, ItemApprovalAssignmentDto>? ItemAssignments { get; set; }
}

public class ItemApprovalAssignmentDto
{
    public int PlantId { get; set; }
    public int CostCenterId { get; set; }
}
