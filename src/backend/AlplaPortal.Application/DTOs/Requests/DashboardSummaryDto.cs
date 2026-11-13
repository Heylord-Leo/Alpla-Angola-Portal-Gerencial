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

    /// <summary>Legacy scalar: the single-currency total when exactly one currency is involved, otherwise the raw sum of every
    /// bucket (kept for the home KPI, which already guards multi-currency with <see cref="FilteredCurrencyCodes"/>).</summary>
    public decimal FilteredTotal { get; set; }
    public List<string> FilteredCurrencyCodes { get; set; } = new();

    /// <summary>Explorer "Total Filtrado": one total per currency over every request matching the active filters (all pages),
    /// with the same amount / currency source rules as the rows; multi-currency requests contribute each component once
    /// (RequestFilteredTotals). Empty when nothing matches.</summary>
    public List<CurrencyAmountDto> FilteredTotalsByCurrency { get; set; } = new();

    // Trend & Growth (MoM)
    public decimal? FilteredTotalTrend { get; set; }
    public string? FilteredTotalTrendLabel { get; set; }
}
