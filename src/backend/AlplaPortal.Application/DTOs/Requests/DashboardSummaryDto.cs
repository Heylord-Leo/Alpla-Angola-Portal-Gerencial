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

    /// <summary>
    /// v2.242.0 — cross-type personal PO-correction count: distinct requests with ≥1
    /// WAITING_PO_CORRECTION group personally owned by the current Buyer (QUOTATION→BuyerId,
    /// PAYMENT→PoResponsibleBuyerId), within the access scope. Drives the "Correções de P.O.
    /// pendentes" footer sticker; shares PersonalPoCorrectionPredicate with "Para Minha Ação".
    /// </summary>
    public int PoCorrectionsForMe { get; set; }

    public decimal FilteredTotal { get; set; }
    public List<string> FilteredCurrencyCodes { get; set; } = new();

    // Trend & Growth (MoM)
    public decimal? FilteredTotalTrend { get; set; }
    public string? FilteredTotalTrendLabel { get; set; }
}
