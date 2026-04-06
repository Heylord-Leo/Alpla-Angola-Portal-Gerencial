namespace AlplaPortal.Application.DTOs.Requests;

public class ApprovalActionDto
{
    public string? Comment { get; set; }
    public Guid? SelectedQuotationId { get; set; }
    public Dictionary<Guid, int>? ItemCostCenters { get; set; }
}
