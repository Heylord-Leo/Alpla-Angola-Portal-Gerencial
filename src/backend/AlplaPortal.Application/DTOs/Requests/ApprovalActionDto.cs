namespace AlplaPortal.Application.DTOs.Requests;

public class ApprovalActionDto
{
    public string? Comment { get; set; }
    public Guid? SelectedQuotationId { get; set; }
    public int? CostCenterId { get; set; }
}
