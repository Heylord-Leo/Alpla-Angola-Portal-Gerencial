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
    public int PendingMyApproval { get; set; }
    public int AwaitingPo { get; set; }
    public int CompletedRequests { get; set; }

    public decimal FilteredTotal { get; set; }
    public List<string> FilteredCurrencyCodes { get; set; } = new();

    // Trend & Growth (MoM)
    public decimal? FilteredTotalTrend { get; set; }
    public string? FilteredTotalTrendLabel { get; set; }
}
