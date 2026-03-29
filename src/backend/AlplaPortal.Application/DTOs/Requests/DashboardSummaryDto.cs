namespace AlplaPortal.Application.DTOs.Requests;

public class DashboardSummaryDto
{
    public int TotalRequests { get; set; }
    public int WaitingQuotation { get; set; }
    public int WaitingAreaApproval { get; set; }
    public int WaitingFinalApproval { get; set; }
    public int InAdjustment { get; set; }
    public int InAttention { get; set; }

    // KPI Cards Specific
    public int AwaitingPayment { get; set; }
    public int AwaitingApproval { get; set; }
    public int CompletedRequests { get; set; }
}
